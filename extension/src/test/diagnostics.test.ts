import { test } from "node:test";
import * as assert from "node:assert/strict";
import { checkMarkup, checkVb } from "../diagnostics";
import { Host } from "../navigation";

const files: Record<string, string> = {
  "/site/web.config": "<configuration />",
  "/site/Header.ascx": '<%@ Control Language="VB" AutoEventWireup="false" CodeFile="Header.ascx.vb" Inherits="Header" %>',
  "/site/styles/Site.css": "",
  "/site/Good.aspx.vb": [
    "Partial Class Good",
    " Inherits System.Web.UI.Page",
    " Protected Sub btnGo_Click(sender As Object, e As EventArgs)",
    " End Sub",
    "End Class",
  ].join("\n"),
  "/site/Good.aspx": [
    '<%@ Page Language="VB" AutoEventWireup="false" CodeFile="Good.aspx.vb" Inherits="Good" %>',
    '<%@ Register TagPrefix="uc1" TagName="Header" Src="~/Header.ascx" %>',
    '<link href="/styles/Site.css" rel="stylesheet" />',
    '<uc1:Header id="Header1" runat="server"></uc1:Header>',
    '<asp:Button ID="btnGo" runat="server" OnClick="btnGo_Click" OnClientClick="return check();" />',
    "<p>Pasted from Word<o:p></o:p></p>",
    '<a href="http://example.com/">x</a> <img src="<%= Img %>" />',
    '<%-- <uc9:Gone runat="server" /> --%>',
  ].join("\n"),
  "/site/Bad.aspx": [
    '<%@ Page Language="VB" AutoEventWireup="false" CodeFile="Nope.aspx.vb" Inherits="Bad" %>',
    '<%@ Register TagPrefix="uc1" TagName="Footer" Src="~/Footer.ascx" %>',
    '<uc1:Header id="Header1" runat="server"></uc1:Header>',
    '<img src="/images/missing.png" />',
  ].join("\n"),
  "/site/Controls/Footer.ascx": "",
  "/site/Controls/Cased.aspx": [
    '<%@ Page Language="VB" AutoEventWireup="false" CodeFile="../good.aspx.vb" Inherits="Good" %>',
    '<%@ Register TagPrefix="uc1" TagName="Footer" Src="~/controls/footer.ascx" %>',
    '<link href="/Styles/site.css?v=2" rel="stylesheet" /> <a href="./Footer.ascx">same</a>',
    '<asp:Button ID="btnGo" runat="server" OnClick="btnGo_Click" />',
  ].join("\n"),
  "/site/Sample.aspx.cs": "public partial class Sample : System.Web.UI.Page { }",
  "/site/Sample.aspx": '<%@ Page Language="C#" CodeFile="Sample.aspx.cs" Inherits="Other" %>\n<asp:Button ID="b" runat="server" OnClick="Gone_Click" />',
  "/site/Go.aspx.vb": 'Response.Redirect("good.aspx")\nResponse.Redirect("/Nope.aspx?id=" + id)\nimg.Src = "/styles/Site.css"\nResponse.Redirect("Bad" & ".aspx")',
  "/site/Blocks.aspx": '<% Response.Redirect("/nope.aspx") %>\n<script src="<%= ResolveUrl("~/Styles/site.css") %>"></script>',
  "/site/WrongClass.aspx": '<%@ Page Language="VB" AutoEventWireup="false" CodeFile="Good.aspx.vb" Inherits="Other" %>\n<asp:Button ID="b" runat="server" OnClick="Gone_Click" />',
};

const host: Host = {
  async readDir(dir) {
    const prefix = dir === "/" ? "/" : dir + "/";
    const names = new Set<string>();
    for (const f of Object.keys(files)) if (f.startsWith(prefix)) names.add(f.slice(prefix.length).split("/")[0]);
    return names.size ? [...names] : undefined;
  },
  async readText(file) {
    return files[file];
  },
};

async function check(file: string, missingFiles = true) {
  const problems = await (file.endsWith(".vb") ? checkVb : checkMarkup)(host, file, files[file], { missingFiles });
  return problems.map((p) => `${p.severity} ${p.code} ${files[file].slice(p.span.start, p.span.end)}`);
}

test("a correct page has no problems (Word tags, external and server-built URLs, server comments)", async () => {
  assert.deepEqual(await check("/site/Good.aspx"), []);
});

test("broken references are errors; a missing local file is a warning", async () => {
  assert.deepEqual(await check("/site/Bad.aspx"), [
    "error missing-codefile Nope.aspx.vb",
    "error missing-register-src ~/Footer.ascx",
    "error unregistered-tag uc1:Header",
    "warning missing-file /images/missing.png",
  ]);
});

test("Inherits naming a class the code-behind doesn't declare, and OnClick naming a missing Sub", async () => {
  assert.deepEqual(await check("/site/WrongClass.aspx"), ["error missing-class Other", "error missing-handler Gone_Click"]);
});

test("a reference whose case differs from disk resolves, with a warning that spells it as on disk", async () => {
  assert.deepEqual(await check("/site/Controls/Cased.aspx"), [
    "warning case-mismatch ../good.aspx.vb",
    "warning case-mismatch ~/controls/footer.ascx",
    "warning case-mismatch /Styles/site.css?v=2",
  ]);
  const messages = (await checkMarkup(host, "/site/Controls/Cased.aspx", files["/site/Controls/Cased.aspx"], { missingFiles: true })).map((p) => p.message);
  assert.match(messages[0], /\('\.\.\/Good\.aspx\.vb'\)/);
  assert.match(messages[1], /\('~\/Controls\/Footer\.ascx'\)/);
  assert.match(messages[2], /\('\/styles\/Site\.css'\)/);
});

test("case warnings on URLs are turned off with missing-file warnings", async () => {
  assert.ok(!(await check("/site/Controls/Cased.aspx", false)).some((p) => p.includes("site.css")));
});

test("missing-file warnings can be turned off", async () => {
  assert.ok(!(await check("/site/Bad.aspx", false)).some((p) => p.includes("missing-file")));
});

test("a C# code-behind isn't checked by VB rules", async () => {
  assert.deepEqual(await check("/site/Sample.aspx"), []);
});

test("site paths in VB strings get the same warnings, in .vb files and <% %> blocks", async () => {
  assert.deepEqual(await check("/site/Go.aspx.vb"), ["warning case-mismatch good.aspx", "warning missing-file /Nope.aspx?id="]);
  assert.deepEqual(await check("/site/Blocks.aspx"), ["warning missing-file /nope.aspx", "warning case-mismatch ~/Styles/site.css"]);
  assert.deepEqual(await check("/site/Go.aspx.vb", false), []);
  assert.deepEqual(await check("/site/Blocks.aspx", false), []);
});
