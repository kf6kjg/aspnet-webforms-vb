// Tokenize every .aspx/.ascx/.master under a folder (read-only) and report files where
// the grammar loses track of server code: a "%>" that doesn't close a block, or a
// block/comment still open at end of file. Prints file paths only, never file content.
//
//   node test/smoke-real-files.mjs <folder>
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import vsctm from "vscode-textmate";
import oniguruma from "vscode-oniguruma";

const here = path.dirname(fileURLToPath(import.meta.url));
const grammarFiles = {
  "text.html.aspx": path.join(here, "../syntaxes/aspx.tmLanguage.json"),
  "text.html.basic": path.join(here, "vendor-grammars/html.tmLanguage.json"),
  "text.html.derivative": path.join(here, "vendor-grammars/html-derivative.tmLanguage.json"),
  "source.asp.vb.net": path.join(here, "vendor-grammars/asp-vb-net.tmLanguage.json"),
  "source.js": path.join(here, "vendor-grammars/JavaScript.tmLanguage.json"),
  "source.css": path.join(here, "vendor-grammars/css.tmLanguage.json"),
};

const root = process.argv[2];
if (!root) throw new Error("usage: smoke-real-files.mjs <folder>");

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
});
const grammar = await registry.loadGrammar("text.html.aspx");

const skip = new Set(["obj", "precompiledweb", ".vs", ".git", "node_modules", "aspnet_client"]);
function* walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!skip.has(e.name.toLowerCase())) yield* walk(p); }
    else if (/\.(aspx|ascx|master)$/i.test(e.name)) yield p;
  }
}

let files = 0, bad = 0;
const openScopes = ["meta.embedded.block.aspx", "comment.block.aspx", "meta.tag.directive.aspx"];
for (const file of walk(root)) {
  files++;
  const lines = fs.readFileSync(file, "latin1").split(/\r?\n/);
  let stack = vsctm.INITIAL;
  const problems = [];
  lines.forEach((line, i) => {
    const r = grammar.tokenizeLine(line, stack);
    for (const t of r.tokens) {
      const text = line.slice(t.startIndex, t.endIndex);
      // A "%>" that is plain text (not a block end, and not inside a comment or JS/HTML string) means a block was lost.
      if (text.includes("%>") && !t.scopes.some((s) => /^(punctuation\.section\.embedded\.end|comment\.block\.aspx|punctuation\.definition\.comment)/.test(s))) {
        problems.push(`line ${i + 1}: unmatched %> (${t.scopes.slice(1).join(" ")})`);
      }
    }
    stack = r.ruleStack;
  });
  const last = grammar.tokenizeLine("", stack).tokens.at(-1)?.scopes ?? [];
  const stillOpen = last.filter((s) => openScopes.includes(s));
  if (stillOpen.length) problems.push(`end of file still inside ${stillOpen.join(", ")}`);
  if (problems.length) {
    bad++;
    console.log(`${path.relative(root, file)}\n  ${problems.slice(0, 3).join("\n  ")}${problems.length > 3 ? `\n  … ${problems.length - 3} more` : ""}`);
  }
}
console.log(`\n${files} files tokenized, ${bad} with problems.`);
if (bad) process.exitCode = 1;
