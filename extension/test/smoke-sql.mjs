// Tokenize every .vb file under a folder (read-only) with the VB grammar plus the SQL
// injection, and check that the lines it highlights as SQL are exactly the statements
// src/sqlmarker.ts finds, so the grammar and the code action agree (ADR 0010). Also counts
// where the code action would offer a marker. Prints paths, line numbers and counts only,
// never file content. Needs `npm run compile` first.
//
//   node test/smoke-sql.mjs <folder>
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import vsctm from "vscode-textmate";
import oniguruma from "vscode-oniguruma";
import sqlmarker from "../out/sqlmarker.js";

const { sqlRegions, statementAt, suggestMarker } = sqlmarker;
const here = path.dirname(fileURLToPath(import.meta.url));
const grammarFiles = {
  "source.asp.vb.net": path.join(here, "vendor-grammars/asp-vb-net.tmLanguage.json"),
  "source.vb.sql-injection": path.join(here, "../syntaxes/vb-sql.injection.tmLanguage.json"),
};

const root = process.argv[2];
if (!root) throw new Error("usage: smoke-sql.mjs <folder>");

const wasm = fs.readFileSync(path.join(here, "../node_modules/vscode-oniguruma/release/onig.wasm")).buffer;
await oniguruma.loadWASM(wasm);
const registry = new vsctm.Registry({
  onigLib: Promise.resolve({
    createOnigScanner: (p) => new oniguruma.OnigScanner(p),
    createOnigString: (s) => new oniguruma.OnigString(s),
  }),
  loadGrammar: async (scope) => {
    const file = grammarFiles[scope];
    return file ? vsctm.parseRawGrammar(fs.readFileSync(file, "utf8"), file) : null;
  },
  getInjections: (scope) => (scope === "source.asp.vb.net" ? ["source.vb.sql-injection"] : []),
});
const grammar = await registry.loadGrammar("source.asp.vb.net");

const skip = new Set(["obj", "precompiledweb", ".vs", ".git", "node_modules", "aspnet_client"]);
function* walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!skip.has(e.name.toLowerCase())) yield* walk(p); }
    else if (/\.vb$/i.test(e.name)) yield p;
  }
}

let files = 0, sqlLines = 0, regions = 0, bad = 0;
const offers = new Map();
for (const file of walk(root)) {
  files++;
  const lines = fs.readFileSync(file, "latin1").split(/\r\n|\n|\r/);
  const expected = new Set();
  for (const r of sqlRegions(lines)) {
    regions++;
    for (let i = r.start; i <= r.end; i++) expected.add(i);
  }
  const problems = [];
  let stack = vsctm.INITIAL;
  lines.forEach((line, i) => {
    const r = grammar.tokenizeLine(line, stack);
    stack = r.ruleStack;
    const got = r.tokens.some((t) => t.scopes.includes("meta.sql-statement.vb"));
    if (got) sqlLines++;
    // A blank line right after a marker holds the region's zero-width end; either answer is fine.
    if (line.trim() === "") return;
    if (got && !expected.has(i)) problems.push(`line ${i + 1}: highlighted as SQL outside a statement`);
    if (!got && expected.has(i)) problems.push(`line ${i + 1}: SQL statement line not highlighted`);
  });
  for (let i = 0; i < lines.length; i = statementAt(lines, i).end + 1) {
    for (const s of suggestMarker(lines, i)) {
      const where = s.replace ? "narrow lang=sql" : /^[ \t]*SQLCmd\d*\b/i.test(lines[i]) ? "SQLCmd" : "other variable";
      const key = `${where} → ${s.dialect}${s.preferred ? "" : " (one of two)"}`;
      offers.set(key, (offers.get(key) ?? 0) + 1);
    }
  }
  if (problems.length) {
    bad++;
    console.log(`${path.relative(root, file)}\n  ${problems.slice(0, 3).join("\n  ")}${problems.length > 3 ? `\n  … ${problems.length - 3} more` : ""}`);
  }
}
console.log(`\n${files} .vb files: ${regions} SQL statements, ${sqlLines} highlighted lines, ${bad} files where grammar and statement rules disagree.`);
console.log("Code action offers:");
for (const [k, n] of [...offers].sort((a, b) => b[1] - a[1])) console.log(`  ${n}\t${k}`);
if (bad) process.exitCode = 1;
