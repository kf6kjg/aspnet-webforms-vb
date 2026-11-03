import { test } from "node:test";
import * as assert from "node:assert/strict";
import { implicitLanguage, languageRegions, languagesIn, suggestLanguage } from "../langmarker";

// In the developer's style: one-space indent, strings joined with + over continued lines.
const lines = [
  " Sub Load()",                                                                 // 0
  "  tHtmlOut = \"<table>\" +",                                                  // 1
  "             \"<tr><td>\" + Name + \"</td></tr>\"",                           // 2
  "  PageTitle.InnerHtml = \"Members\"",                                         // 3
  "  Body.Attributes.Add(\"onload\", \"ShowMsg();\")",                           // 4
  "  ShowMsg.Attributes(\"onclick\") = \"Go(\" + ID + \");\"",                   // 5
  "  SBMenu.AddItem(\"L\", \"CallEdit(0,'F.aspx');\", \"New Entry\")",           // 6
  "  Row.Attributes(\"style\") = \"display: none;\"",                            // 7
  "  tOut = \"<span class=\"\"Errors\"\">*</span>\"",                            // 8
  "  ' lang=json",                                                               // 9
  "  tOut = \"{\"\"Error\"\":\"\"Missing Site\"\"}\"",                           // 10
  "  SQLCmd = \"Select Body From page \" +",                                     // 11
  "           \"Where a='<b>'\"",                                                // 12
  "  tMsg = \"Saved!<br />\"",                                                   // 13
  "  Trace.Warn(\"Page\", \"<b>Load</b>\")",                                     // 14
  "  Response.Write(\"<?xml version=\"\"1.0\"\"?><ok/>\")",                      // 15
  " End Sub",                                                                    // 16
];

test("implicitLanguage: html names, InnerHtml, and the JavaScript and CSS attribute APIs", () => {
  assert.equal(implicitLanguage(lines[1]), "html");
  assert.equal(implicitLanguage(lines[3]), "html");
  assert.equal(implicitLanguage(" tHMTLOut = x"), "html");
  assert.equal(implicitLanguage(" tHtml &= \"<b>\""), "html");
  assert.equal(implicitLanguage(lines[4]), "js");
  assert.equal(implicitLanguage(lines[5]), "js");
  assert.equal(implicitLanguage(" X.Attributes.Item(\"onchange\") = \"a();\""), "js");
  assert.equal(implicitLanguage(lines[6]), "js");
  assert.equal(implicitLanguage(" TabNav.Additem(\"L\", \"GoTab('x');\", t)"), "js");
  assert.equal(implicitLanguage(lines[7]), "css");
  assert.equal(implicitLanguage(" X.Attributes.Add(\"style\", \"color: red;\")"), "css");
  assert.equal(implicitLanguage(" X.Attributes.Add(\"class\", \"Form\")"), undefined);
  assert.equal(implicitLanguage(" SBMenu.AddItem(\"P\", \"Page.aspx\", \"Link\")"), undefined);
  assert.equal(implicitLanguage(" If tHtml = \"\" Then"), undefined);
  assert.equal(implicitLanguage(" n = tHtml.Length"), undefined);
  assert.equal(implicitLanguage(lines[8]), undefined);
});

test("languageRegions finds implicit and marked statements, and leaves SQL to sqlRegions", () => {
  assert.deepEqual(languageRegions(lines), [
    { start: 1, end: 2, language: "html" },
    { start: 3, end: 3, language: "html" },
    { start: 4, end: 4, language: "js" },
    { start: 5, end: 5, language: "js" },
    { start: 6, end: 6, language: "js" },
    { start: 7, end: 7, language: "css" },
    { start: 9, end: 10, language: "json", marker: 9 },
  ]);
  assert.deepEqual(languageRegions(["' lang=sql", " tHtml = \"Select 1\""]), []);
  assert.deepEqual(languageRegions(["' lang=css", " tHtml = \"a: b;\""]), [{ start: 0, end: 1, language: "css", marker: 0 }]);
});

test("languagesIn: strong signs only, and HTML wins over the JavaScript and CSS it carries", () => {
  assert.deepEqual(languagesIn(["<table width=\"100%\">"]), ["html"]);
  assert.deepEqual(languagesIn([" </td>", "</tr>"]), ["html"]);
  assert.deepEqual(languagesIn(["Saved!<br />"]), []);
  assert.deepEqual(languagesIn(["<?xml version=\"1.0\"?>", "<methodCall>"]), ["xml"]);
  assert.deepEqual(languagesIn(["{\"paymentType\":\"purchase\",\"amount\":\""]), ["json"]);
  assert.deepEqual(languagesIn(["{0} items"]), []);
  assert.deepEqual(languagesIn(["var aMsg=", "; function ShowMsg() {"]), ["js"]);
  assert.deepEqual(languagesIn(["Function buy_land("]), []);
  assert.deepEqual(languagesIn(["cursor: pointer; color: #FF0033;"]), ["css"]);
  assert.deepEqual(languagesIn(["Note: see below;"]), []);
  assert.deepEqual(languagesIn(["<span onclick=\"", "window.open('x');"]), ["html"]);
});

test("suggestLanguage: an unmarked statement whose strings look like a language gets the marker above it", () => {
  assert.deepEqual(suggestLanguage(lines, 8), [{ language: "html", preferred: true, insertAbove: 8, text: "  ' lang=html" }]);
  assert.deepEqual(suggestLanguage(lines, 15), [{ language: "xml", preferred: true, insertAbove: 15, text: "  ' lang=xml" }]);
  assert.deepEqual(suggestLanguage([" Return \"<b>\" + x + \"</b>\""], 0).map((s) => s.language), ["html"]);
  const both = ["x = \"<?xml version=\"\"1.0\"\"?>\" +", "    \"<html><body></body></html>\""];
  assert.deepEqual(suggestLanguage(both, 1).map((s) => [s.language, s.preferred]), [["html", false], ["xml", false]]);
});

test("suggestLanguage: nothing for implicit, marked, SQL, call statements, weak HTML, or a marker line", () => {
  for (const line of [1, 2, 3, 4, 6, 7, 9, 10, 11, 12, 13, 14, 16]) assert.deepEqual(suggestLanguage(lines, line), [], `line ${line}`);
});
