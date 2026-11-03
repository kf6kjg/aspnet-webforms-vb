import { test } from "node:test";
import * as assert from "node:assert/strict";
import { dialectsIn, looksLikeSql, markerOf, sqlRegions, statementAt, suggestMarker } from "../sqlmarker";

// In the developer's style: one-space indent, SQLCmd built with + over continued lines.
const lines = [
  "Partial Class Admin",                                                   // 0
  " Private SQLCmd As String",                                             // 1
  " Sub Load()",                                                           // 2
  "  SQLCmd = \"Select Name \" +",                                         // 3
  "           \"From control \" +",                                       // 4
  "           \"Where Control=\" + MyDB.SQLStr(x)",                        // 5
  "  tMsg = \"Insert Page record: \" + SQLCmd",                            // 6
  "  tWhere = \" and Language=\" + MyDB.SQLStr(y) +",                      // 7
  "           \" and Active=1\"",                                         // 8
  "  ' lang=sql",                                                          // 9
  "  tSel = \"Select Top 10 Name From [users]\"",                          // 10
  "  SQLCmd = \"Select Name From users Limit 5\" ' newest first",          // 11
  "  ' Language = MySQL",                                                  // 12
  "  SQLCmd = \"Select `Name` From users\"",                               // 13
  "  x = \"Select a From b\" & \"x\"",                                     // 14
  " End Sub",                                                              // 15
  "End Class",                                                             // 16
];

test("markerOf reads the dialect, its aliases and its column range", () => {
  assert.deepEqual(markerOf("  ' lang=sql"), { dialect: "sql", start: 9, end: 12 });
  assert.equal(markerOf("' LANGUAGE = MariaDB")?.dialect, "mysql");
  assert.equal(markerOf("'lang=mssql")?.dialect, "tsql");
  assert.equal(markerOf("' lang=postgres")?.dialect, "pgsql");
  assert.equal(markerOf("' lang=sqlite"), undefined);
  assert.equal(markerOf("x = 1 ' lang=sql"), undefined);
});

test("statementAt follows implicit line continuation both ways, and stops at a comment", () => {
  assert.deepEqual(statementAt(lines, 4), { start: 3, end: 5 });
  assert.deepEqual(statementAt(lines, 3), { start: 3, end: 5 });
  assert.deepEqual(statementAt(lines, 6), { start: 6, end: 6 });
  assert.deepEqual(statementAt(lines, 8), { start: 7, end: 8 });
  assert.deepEqual(statementAt(["a = \"x\" + ' not a continuation", "b = 1"], 0), { start: 0, end: 0 });
  assert.deepEqual(statementAt(["a = \"it's +\"", "b = 1"], 0), { start: 0, end: 0 });
});

test("sqlRegions finds SQLCmd statements and marked statements, as the grammar does", () => {
  assert.deepEqual(sqlRegions(lines), [
    { start: 3, end: 5, dialect: "sql" },
    { start: 9, end: 10, dialect: "sql", marker: 9 },
    { start: 11, end: 11, dialect: "sql" },
    { start: 12, end: 13, dialect: "mysql", marker: 12 },
  ]);
});

test("looksLikeSql accepts queries and clause fragments, not log messages", () => {
  assert.ok(looksLikeSql(["Select Name ", "From control ", "Where x="]));
  assert.ok(looksLikeSql(["Update users Set Name=", " Where ID="]));
  assert.ok(looksLikeSql([" and Language=", " and Active=1"]));
  assert.ok(looksLikeSql([" and B.Language="]));
  assert.ok(looksLikeSql([" Order by SortOrder"]));
  assert.ok(looksLikeSql(["From ", "Where "]));
  assert.ok(!looksLikeSql(["Where "]));
  assert.ok(!looksLikeSql(["Insert Page record: "]));
  assert.ok(!looksLikeSql(["Update users SQLCmd: "]));
  assert.ok(!looksLikeSql([" and "]));
});

test("dialectsIn spots T-SQL and MySQL syntax", () => {
  assert.deepEqual(dialectsIn("Select Top 10 Name From [users]"), ["tsql"]);
  assert.deepEqual(dialectsIn("Select IfNull(a,0) From b Limit 5"), ["mysql"]);
  assert.deepEqual(dialectsIn("Select Name From users"), []);
  assert.deepEqual(dialectsIn("Select GetDate(), Now()"), ["tsql", "mysql"]);
});

test("suggestMarker: unmarked SQL in another variable gets lang=sql above it", () => {
  assert.deepEqual(suggestMarker(lines, 8), [{ dialect: "sql", preferred: true, insertAbove: 7, text: "  ' lang=sql" }]);
  assert.deepEqual(suggestMarker(lines, 14), [{ dialect: "sql", preferred: true, insertAbove: 14, text: "  ' lang=sql" }]);
});

test("suggestMarker: nothing for log messages, plain SQLCmd, a dialect marker, or the marker line", () => {
  assert.deepEqual(suggestMarker(lines, 6), []);
  assert.deepEqual(suggestMarker(lines, 4), []);
  assert.deepEqual(suggestMarker(lines, 13), []);
  assert.deepEqual(suggestMarker(lines, 9), []);
  assert.deepEqual(suggestMarker(lines, 1), []);
});

test("suggestMarker: dialect syntax narrows lang=sql in place, or marks a SQLCmd statement", () => {
  assert.deepEqual(suggestMarker(lines, 10), [{ dialect: "tsql", preferred: true, replace: { line: 9, start: 9, end: 12 }, text: "tsql" }]);
  assert.deepEqual(suggestMarker(lines, 11), [{ dialect: "mysql", preferred: true, insertAbove: 11, text: "  ' lang=mysql" }]);
});

test("suggestMarker: both dialects' syntax offers both, preferring neither", () => {
  const both = [" SQLCmd = \"Select GetDate(), Now()\""];
  assert.deepEqual(suggestMarker(both, 0).map((s) => [s.dialect, s.preferred]), [["tsql", false], ["mysql", false]]);
});

test("suggestMarker: a dialect-specific query in another variable gets that dialect", () => {
  const q = [" tSelect = \"Select Top 5 Name \" +", "  \"From [users]\""];
  assert.deepEqual(suggestMarker(q, 1), [{ dialect: "tsql", preferred: true, insertAbove: 0, text: " ' lang=tsql" }]);
});
