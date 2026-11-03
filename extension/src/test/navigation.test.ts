import { test } from "node:test";
import * as assert from "node:assert/strict";
import * as path from "node:path";
import { Host, codeBehindFor, definitionInCodeBehind, definitionInMarkup, localPath, markupFor } from "../navigation";
import { directives, maskServerBlocks, tags } from "../markup";
import { handlersOf, handlesAt } from "../vbcode";

// A tiny site in the developer's style. Casing of references deliberately differs
// from disk in places, as it does on his Windows share.
const files: Record<string, string> = {
  "/site/web.config": "<configuration />",
  "/site/Buy.aspx": [
    '<%@ Page Language="VB" AutoEventWireup="false" CodeFile="Buy.aspx.vb" Inherits="Buy" %>',
    '<%@ Register TagPrefix="uc1" TagName="Header" Src="~/controls/header.ascx" %>',
    '<body id="BodyTag" runat="server">',
    ' <uc1:Header id="Header1" runat="server"></uc1:Header>',
    ' <span onclick="CallEdit(<%#Container.DataItem("ID")%>);" class="NavLink">x</span>',
    ' <asp:Button ID="btnSave" Text="Save" runat="server" CssClass="Button"/>',
    ' <asp:Button ID="btnOld" runat="server" OnClick="btnOld_Click" OnClientClick="return confirm(\'?\');" />',
    ' <td id="SidebarMenu" runat="server"></td>',
    ' <%-- <asp:TextBox ID="txtGone" runat="server" /> --%>',
    ' <link href="/styles/TreeView.css" type="text/css" rel="stylesheet" />',
    ' <script type="text/javascript" src="/scripts/Cookie.js"></script>',
    ' <asp:ImageButton ID="ibGo" ImageUrl="~/Images/GO.gif" runat="server" />',
    ' <a href="Controls/Header.ascx?x=1#top">rel</a> <a href="http://example.com/a.css">ext</a> <a href="#top">anchor</a>',
    ' <script src="<%= ResolveUrl("~/scripts/Cookie.js") %>"></script> <img src="My%20Pic.png" /> <img src="missing.png" />',
    "</body>",
  ].join("\n"),
  "/site/Buy.aspx.vb": [
    "Partial Class Buy",
    " Inherits System.Web.UI.Page",
    "",
    " Private Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load",
    "  SidebarMenu.InnerHtml = \"\"",
    " End Sub",
    "",
    " Private Sub btnSave_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles btnSave.Click, btnSAVE.Command",
    " End Sub",
    "",
    " Protected Sub btnOld_Click(sender As Object, e As EventArgs)",
    " End Sub",
    "",
    " Private Sub Gone(ByVal sender As Object, ByVal e As System.EventArgs) Handles txtGone.TextChanged",
    " End Sub",
    "End Class",
  ].join("\n"),
  "/site/Controls/Header.ascx": '<%@ Control Language="VB" AutoEventWireup="false" CodeFile="Header.ascx.vb" Inherits="Controls_Header" %>',
  "/site/Controls/Header.ascx.vb": "Partial Class Controls_Header\n Inherits System.Web.UI.UserControl\nEnd Class",
  "/site/Styles/TreeView.css": "",
  "/site/scripts/Cookie.js": "",
  "/site/Images/go.gif": "",
  "/site/My Pic.png": "",
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

const buy = files["/site/Buy.aspx"];
const buyVb = files["/site/Buy.aspx.vb"];
/** Offset of the n-th occurrence of `needle` (1-based), plus `delta`. */
function at(text: string, needle: string, delta = 1, n = 1): number {
  let i = -1;
  for (let k = 0; k < n; k++) i = text.indexOf(needle, i + 1);
  assert.ok(i >= 0, `"${needle}" not found`);
  return i + delta;
}
function slice(file: string, span?: { start: number; end: number }): string {
  return span ? files[file].slice(span.start, span.end) : "(top of file)";
}

test("markup -> code-behind via CodeFile, and back", async () => {
  assert.equal(await codeBehindFor(host, "/site/Buy.aspx", buy), "/site/Buy.aspx.vb");
  assert.equal(await markupFor(host, "/site/Buy.aspx.vb"), "/site/Buy.aspx");
  const j = await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, 'CodeFile="', 12));
  assert.deepEqual(j?.targets, [{ file: "/site/Buy.aspx.vb" }]);
});

test("Inherits -> Partial Class in code-behind", async () => {
  const j = await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, 'Inherits="', 11));
  assert.equal(j?.targets.length, 1);
  assert.equal(slice(j!.targets[0].file, j!.targets[0].span), "Buy");
});

test("@Register Src resolves ~/ against the folder holding web.config, case-insensitively", async () => {
  const j = await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, "~/controls"));
  assert.deepEqual(j?.targets, [{ file: "/site/Controls/Header.ascx" }]);
});

test("<uc1:Header> tag name -> registered .ascx", async () => {
  const j = await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, "<uc1:Header", 6));
  assert.deepEqual(j?.targets, [{ file: "/site/Controls/Header.ascx" }]);
  assert.equal(buy.slice(j!.origin.start, j!.origin.end), "uc1:Header");
});

test('OnClick="btnOld_Click" -> Sub btnOld_Click', async () => {
  const j = await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, '"btnOld_Click"', 3));
  assert.equal(slice(j!.targets[0].file, j!.targets[0].span), "btnOld_Click");
});

test("OnClientClick is JavaScript, not a code-behind method", async () => {
  assert.equal(await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, "return confirm", 3)), undefined);
});

test("control ID -> every Sub that Handles it (case-insensitive)", async () => {
  const j = await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, 'ID="btnSave"', 6));
  assert.deepEqual(j!.targets.map((t) => slice(t.file, t.span)), ["btnSave_Click", "btnSave_Click"]);
});

test("Handles btnSave.Click -> the control's ID in markup", async () => {
  const j = await definitionInCodeBehind(host, "/site/Buy.aspx.vb", buyVb, at(buyVb, "Handles btnSave", 10));
  assert.equal(j?.targets[0].file, "/site/Buy.aspx");
  assert.equal(slice("/site/Buy.aspx", j!.targets[0].span), "btnSave");
});

test("Handles Me.Load and Handles on a server-commented control go nowhere", async () => {
  assert.equal(await definitionInCodeBehind(host, "/site/Buy.aspx.vb", buyVb, at(buyVb, "Handles Me", 9)), undefined);
  assert.equal(await definitionInCodeBehind(host, "/site/Buy.aspx.vb", buyVb, at(buyVb, "txtGone.")), undefined);
});

test("server code inside an attribute doesn't break tag parsing", () => {
  const span = tags(maskServerBlocks(buy)).find((t) => t.name === "span" && !t.closing)!;
  assert.deepEqual(span.attrs.map((a) => a.name), ["onclick", "class"]);
});

test("nameless directive form", () => {
  assert.deepEqual(directives('<%@ Language="VB" %>').map((d) => [d.name, d.attrs.map((a) => a.value)]), [["", ["VB"]]]);
});

test("Handles clause on a continuation line", () => {
  const code = "Private Sub X(ByVal sender As Object, ByVal e As EventArgs) _\n    Handles chkA.CheckedChanged, chkB.CheckedChanged\nEnd Sub";
  assert.deepEqual(handlersOf(code, "chkb").map((h) => [h.method, h.event]), [["X", "CheckedChanged"]]);
  assert.equal(handlesAt(code, code.indexOf("chkB") + 1)?.control, "chkB");
});

test("paths use posix separators", () => {
  assert.equal(path.sep, "/");
});

test("a commented-out Handles clause is not navigable", () => {
  const code = "'Private Sub Button1_Click(ByVal sender As Object, ByVal e As EventArgs) Handles Button1.Click\n' handles multiples of the same item per user in the order. Allow edits.";
  assert.equal(handlesAt(code, code.indexOf("Button1.") + 1), undefined);
  assert.equal(handlesAt(code, code.indexOf("order.") + 1), undefined);
  assert.deepEqual(handlersOf(code, "Button1"), []);
});

test("href/src/ImageUrl -> the file, site-rooted for / and ~/, relative otherwise, case-insensitive", async () => {
  const go = async (needle: string, delta = 1) => (await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, needle, delta)))?.targets.map((t) => t.file);
  assert.deepEqual(await go("/styles/TreeView"), ["/site/Styles/TreeView.css"]);
  assert.deepEqual(await go("/scripts/Cookie"), ["/site/scripts/Cookie.js"]);
  assert.deepEqual(await go("~/Images/GO"), ["/site/Images/go.gif"]);
  assert.deepEqual(await go("Controls/Header.ascx?"), ["/site/Controls/Header.ascx"]);
  assert.deepEqual(await go("My%20Pic"), ["/site/My Pic.png"]);
});

test("external URLs, anchors, server-built URLs and missing files go nowhere", async () => {
  for (const needle of ["http://example", '"#top"', "<%= ResolveUrl", "missing.png"]) {
    assert.equal(await definitionInMarkup(host, "/site/Buy.aspx", buy, at(buy, needle, 2)), undefined, needle);
  }
});

test("HTML built in JavaScript strings or client templates is not a path", () => {
  for (const v of ["' + searchitem.imageUrl + '", "{{{img}}}", "\" + base + \""]) assert.equal(localPath(v), undefined, v);
  assert.equal(localPath("/styles/Site.css?v=2"), "/styles/Site.css");
});
