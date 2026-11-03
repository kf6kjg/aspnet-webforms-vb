import { test } from "node:test";
import * as assert from "node:assert/strict";
import { fragmentsOf, markedEnd, markerOf, statementAt } from "../vbstatement";

const lines = [
  "  SQLCmd = \"Select Name \" +",        // 0
  "           \"From control \" +",      // 1
  "           \"Where Control=\" + x",   // 2
  "  tMsg = \"Saved: \" + SQLCmd",        // 3
  "  tWhere = \" and a=\" + y +",         // 4
  "           \" and \"\"b\"\"=1\"",      // 5
  "  ' lang=html",                        // 6
  "",                                     // 7
];

test("markerOf reads the language, its aliases and its column range", () => {
  assert.deepEqual(markerOf("  ' lang=sql"), { language: "sql", start: 9, end: 12 });
  assert.equal(markerOf("' LANGUAGE = MariaDB")?.language, "mysql");
  assert.equal(markerOf("'lang=mssql")?.language, "tsql");
  assert.equal(markerOf("' lang=postgres")?.language, "pgsql");
  assert.equal(markerOf("' lang=HTML")?.language, "html");
  assert.equal(markerOf("' lang=javascript")?.language, "js");
  assert.equal(markerOf("' lang=js")?.language, "js");
  assert.equal(markerOf("' lang=json")?.language, "json");
  assert.deepEqual(markerOf(" ' language = css"), { language: "css", start: 14, end: 17 });
  assert.equal(markerOf("' lang=xml")?.language, "xml");
  assert.equal(markerOf("' lang=sqlite"), undefined);
  assert.equal(markerOf("' lang=html5"), undefined);
  assert.equal(markerOf("x = 1 ' lang=sql"), undefined);
});

test("statementAt follows implicit line continuation both ways, and stops at a comment", () => {
  assert.deepEqual(statementAt(lines, 1), { start: 0, end: 2 });
  assert.deepEqual(statementAt(lines, 0), { start: 0, end: 2 });
  assert.deepEqual(statementAt(lines, 3), { start: 3, end: 3 });
  assert.deepEqual(statementAt(lines, 5), { start: 4, end: 5 });
  assert.deepEqual(statementAt(["a = \"x\" + ' not a continuation", "b = 1"], 0), { start: 0, end: 0 });
  assert.deepEqual(statementAt(["a = \"it's +\"", "b = 1"], 0), { start: 0, end: 0 });
});

test("fragmentsOf unescapes every string of the statement", () => {
  assert.deepEqual(fragmentsOf(lines, { start: 4, end: 5 }), [" and a=", " and \"b\"=1"]);
});

test("markedEnd: a marker marks the statement below it, unless a blank line comes first", () => {
  assert.equal(markedEnd(lines, 6), 7);
  assert.equal(markedEnd(["' lang=html", " a = \"<b>\" +", "  \"</b>\"", "b = 1"], 0), 2);
});

test("statementAt: an open parenthesis continues the statement past a line ending in And", () => {
  const iif = [
    " tHtml = \"<br>\" +",                    // 0
    "  IIf(a.Length = 0 And",                 // 1
    "      b.Length = 0, \"\", \"<b>\") +",   // 2
    "  \"</td>\"",                             // 3
    " x = 1",                                 // 4
  ];
  for (const line of [0, 1, 2, 3]) assert.deepEqual(statementAt(iif, line), { start: 0, end: 3 }, `line ${line}`);
  assert.deepEqual(statementAt(iif, 4), { start: 4, end: 4 });
  assert.deepEqual(statementAt([" Foo(a,", "  b) ' done", " y = (1"], 0), { start: 0, end: 1 });
});
