// Tokenize every .vb file under a folder (read-only) with the VB grammar plus the SQL and
// web injections, and check that each line's highlighted language is exactly the one
// src/sqlmarker.ts and src/langmarker.ts give it, so the grammars and the code action agree
// (ADR 0010, ADR 0011). Also counts where the code action would offer a marker, and times
// tokenizing with and without the injections. Prints paths, line numbers and counts only,
// never file content. Needs `npm run compile` first.
//
//   node test/smoke-strings.mjs <folder>
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import vsctm from "vscode-textmate";
import oniguruma from "vscode-oniguruma";
import sqlmarker from "../out/sqlmarker.js";
import langmarker from "../out/langmarker.js";
import vbstatement from "../out/vbstatement.js";

const { sqlRegions, suggestMarker } = sqlmarker;
const { languageRegions, suggestLanguage } = langmarker;
const { statementAt } = vbstatement;
const here = path.dirname(fileURLToPath(import.meta.url));
const grammarFiles = {
  "source.asp.vb.net": path.join(here, "vendor-grammars/asp-vb-net.tmLanguage.json"),
  "source.vb.sql-injection": path.join(here, "../syntaxes/vb-sql.injection.tmLanguage.json"),
  "source.vb.web-injection": path.join(here, "../syntaxes/vb-web.injection.tmLanguage.json"),
};
// Region scope → language, as the code reports it (SQL dialects all count as "sql").
const regionScopes = {
  "meta.sql-statement.vb": "sql",
  "meta.html-statement.vb": "html",
  "meta.javascript-statement.vb": "js",
  "meta.css-statement.vb": "css",
  "meta.xml-statement.vb": "xml",
  "meta.json-statement.vb": "json",
};

const root = process.argv[2];
if (!root) throw new Error("usage: smoke-strings.mjs <folder>");

const wasm = fs.readFileSync(path.join(here, "../node_modules/vscode-oniguruma/release/onig.wasm")).buffer;
await oniguruma.loadWASM(wasm);
function vbGrammar(injections) {
  const registry = new vsctm.Registry({
    onigLib: Promise.resolve({
      createOnigScanner: (p) => new oniguruma.OnigScanner(p),
      createOnigString: (s) => new oniguruma.OnigString(s),
    }),
    loadGrammar: async (scope) => {
      const file = grammarFiles[scope];
      return file ? vsctm.parseRawGrammar(fs.readFileSync(file, "utf8"), file) : null;
    },
    getInjections: (scope) => (scope === "source.asp.vb.net" ? injections : []),
  });
  return registry.loadGrammar("source.asp.vb.net");
}
const grammar = await vbGrammar(["source.vb.sql-injection", "source.vb.web-injection"]);
const plain = await vbGrammar([]);

const skip = new Set(["obj", "precompiledweb", ".vs", ".git", "node_modules", "aspnet_client"]);
function* walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!skip.has(e.name.toLowerCase())) yield* walk(p); }
    else if (/\.vb$/i.test(e.name)) yield p;
  }
}

function tokenizeAll(g, lines, onLine) {
  let stack = vsctm.INITIAL;
  lines.forEach((line, i) => {
    const r = g.tokenizeLine(line, stack);
    stack = r.ruleStack;
    onLine?.(r, i);
  });
}

let files = 0, bad = 0, msWith = 0, msWithout = 0;
const statements = new Map(), highlighted = new Map(), offers = new Map();
const count = (m, k, n = 1) => m.set(k, (m.get(k) ?? 0) + n);
for (const file of walk(root)) {
  files++;
  const lines = fs.readFileSync(file, "latin1").split(/\r\n|\n|\r/);
  const expected = new Map();
  for (const r of [...sqlRegions(lines).map((r) => ({ ...r, language: "sql" })), ...languageRegions(lines)]) {
    count(statements, r.language);
    for (let i = r.start; i <= r.end; i++) {
      if (expected.has(i)) throw new Error(`${file}:${i + 1}: in both a ${expected.get(i)} and a ${r.language} region`);
      expected.set(i, r.language);
    }
  }
  const problems = [];
  let t = performance.now();
  tokenizeAll(grammar, lines, (r, i) => {
    const got = new Set(r.tokens.flatMap((tk) => tk.scopes.filter((s) => s in regionScopes).map((s) => regionScopes[s])));
    if (got.size) count(highlighted, [...got].join("+"));
    // A blank line right after a marker holds the region's zero-width end; either answer is fine.
    if (lines[i].trim() === "") return;
    const want = expected.get(i);
    const gotText = [...got].join("+") || "nothing";
    if (got.size > 1 || (want ?? "nothing") !== gotText) problems.push(`line ${i + 1}: highlighted as ${gotText}, statement rules say ${want ?? "nothing"}`);
  });
  msWith += performance.now() - t;
  t = performance.now();
  tokenizeAll(plain, lines);
  msWithout += performance.now() - t;
  for (let i = 0; i < lines.length; i = statementAt(lines, i).end + 1) {
    for (const s of suggestMarker(lines, i)) {
      const where = s.replace ? "narrow lang=sql" : /^[ \t]*SQLCmd\d*\b/i.test(lines[i]) ? "SQLCmd" : "other variable";
      count(offers, `SQL, ${where} → ${s.dialect}${s.preferred ? "" : " (one of two)"}`);
    }
    for (const s of suggestLanguage(lines, i)) count(offers, `${s.language}${s.preferred ? "" : " (one of several)"}`);
  }
  if (problems.length) {
    bad++;
    console.log(`${path.relative(root, file)}\n  ${problems.slice(0, 3).join("\n  ")}${problems.length > 3 ? `\n  … ${problems.length - 3} more` : ""}`);
  }
}
const list = (m) => [...m].sort((a, b) => b[1] - a[1]).map(([k, n]) => `  ${n}\t${k}`).join("\n");
console.log(`\n${files} .vb files, ${bad} where the grammars and the statement rules disagree.`);
console.log(`Statements by language:\n${list(statements)}`);
console.log(`Highlighted lines by language:\n${list(highlighted)}`);
console.log(`Code action offers:\n${list(offers)}`);
console.log(`Tokenizing: ${(msWith / 1000).toFixed(1)} s with the injections, ${(msWithout / 1000).toFixed(1)} s without.`);
if (bad) process.exitCode = 1;
