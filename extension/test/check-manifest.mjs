// Every file package.json contributes must exist and parse; snippet bodies must not be empty.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const pkg = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8"));
const c = pkg.contributes;
const files = [
  ...c.languages.map((l) => l.configuration),
  ...c.grammars.map((g) => g.path),
  ...c.snippets.map((s) => s.path),
];
let failed = false;
for (const f of files) {
  try {
    const json = JSON.parse(fs.readFileSync(path.join(root, f), "utf8"));
    if (f.endsWith(".code-snippets")) {
      for (const [name, s] of Object.entries(json)) {
        if (!s.prefix || !s.body || s.body.length === 0) throw new Error(`snippet "${name}" lacks prefix or body`);
      }
    }
    console.log(`✓ ${f}`);
  } catch (e) {
    failed = true;
    console.log(`✗ ${f}: ${e.message}`);
  }
}
if (failed) process.exitCode = 1;
