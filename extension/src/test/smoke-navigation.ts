// Run every stage 2 navigation against the real sites (read-only) and report targets
// that don't resolve. Prints paths and identifiers only, never file content.
//
//   node out/test/smoke-navigation.js <folder>
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { Host, PathRef, codeBehindFor, definitionInCodeBehind, definitionInMarkup, definitionInVb, localPath, markupFor, vbPathRefs } from "../navigation";
import { attr, isEventHandlerAttr, maskServerBlocks, registrations, serverCode, tags } from "../markup";
import { handlersOf } from "../vbcode";
import { Problem, checkMarkup, checkVb } from "../diagnostics";

const dirCache = new Map<string, Promise<string[] | undefined>>();
const host: Host = {
  readDir: (dir) => {
    if (!dirCache.has(dir)) dirCache.set(dir, fs.readdir(dir).catch(() => undefined));
    return dirCache.get(dir)!;
  },
  readText: (file) => fs.readFile(file, "latin1").catch(() => undefined),
};

const skip = new Set(["obj", "precompiledweb", ".vs", ".git", "node_modules", "aspnet_client"]);
async function* walk(dir: string): AsyncGenerator<string> {
  for (const e of await fs.readdir(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!skip.has(e.name.toLowerCase())) yield* walk(p); }
    else if (/\.(aspx|ascx|master|vb)$/i.test(e.name)) yield p;
  }
}

const tally = new Map<string, { ok: number; fail: string[] }>();
const problemTally = new Map<string, { files: Set<string>; examples: string[] }>();
function record(kind: string, ok: boolean, example: string): void {
  const t = tally.get(kind) ?? { ok: 0, fail: [] };
  if (ok) t.ok++; else t.fail.push(example);
  tally.set(kind, t);
}

function recordProblems(rel: string, text: string, problems: Problem[]): void {
  for (const p of problems) {
    const key = `${p.severity} ${p.code}`;
    const t = problemTally.get(key) ?? { files: new Set<string>(), examples: [] };
    t.files.add(rel);
    t.examples.push(`${rel}:${text.slice(0, p.span.start).split("\n").length}: ${p.message}`);
    problemTally.set(key, t);
  }
}

async function recordVbPaths(kind: string, rel: string, text: string, refs: PathRef[], go: (offset: number) => Promise<unknown>): Promise<void> {
  for (const r of refs) record(kind, !!(await go(r.span.start)), `${rel}: "${text.slice(r.span.start, r.span.end)}"`);
}

async function main(root: string): Promise<void> {
  for await (const file of walk(root)) {
    const rel = path.relative(root, file);
    const text = (await host.readText(file))!;
    if (/\.vb$/i.test(file)) {
      recordProblems(rel, text, await checkVb(host, file, text, { missingFiles: true }));
      await recordVbPaths("VB string path -> site file", rel, text, vbPathRefs(text, file), async (o) => (await definitionInVb(host, file, text, o))?.targets.length);
      continue;
    }
    const masked = maskServerBlocks(text);

    recordProblems(rel, text, await checkMarkup(host, file, text, { missingFiles: true }));
    await recordVbPaths("<% %> string path -> site file", rel, text, vbPathRefs(serverCode(text), file), async (o) => (await definitionInMarkup(host, file, text, o))?.targets.length);

    const codeFile = await codeBehindFor(host, file, text);
    record("markup -> code-behind", !!codeFile, rel);
    // Several backup pages can share one CodeFile; any markup found for it counts.
    if (codeFile) record("code-behind -> markup", !!(await markupFor(host, codeFile)), path.relative(root, codeFile));

    for (const r of registrations(text)) {
      const j = await definitionInMarkup(host, file, text, r.src.valueSpan.start);
      record("@Register Src -> .ascx", !!j?.targets.length, `${rel}: ${r.src.value}`);
    }

    const code = codeFile ? (await host.readText(codeFile)) ?? "" : "";
    for (const t of tags(masked)) {
      if (t.closing) continue;
      if (t.prefix && t.prefix.toLowerCase() !== "asp") {
        const j = await definitionInMarkup(host, file, text, t.nameSpan.start + 1);
        record("<prefix:Tag> -> registered .ascx", !!j?.targets.length, `${rel}: ${t.prefix}:${t.name}`);
      }
      for (const a of t.attrs.filter((x) => isEventHandlerAttr(t, x))) {
        const j = await definitionInMarkup(host, file, text, a.valueSpan.start);
        record("On*=\"Foo\" -> Sub Foo", !!j?.targets.length, `${rel}: ${a.name}="${a.value}"`);
      }
      for (const a of t.attrs.filter((x) => /^(href|src|action|background|imageurl|navigateurl|postbackurl)$/i.test(x.name))) {
        if (!localPath(text.slice(a.valueSpan.start, a.valueSpan.end))) continue;
        const j = await definitionInMarkup(host, file, text, a.valueSpan.start);
        record("href/src/…Url -> site file", !!j?.targets.length, `${rel}: ${a.name}="${a.value}"`);
      }
      const id = attr(t.attrs, "ID");
      if (id && code && handlersOf(code, id.value).length > 0) {
        const j = await definitionInMarkup(host, file, text, id.valueSpan.start);
        record("control ID -> Handles Subs", !!j?.targets.length, `${rel}: ${id.value}`);
      }
    }

    if (codeFile && code) {
      // Same view of a line as the extension: text after ' is a comment.
      const uncommented = code.replace(/'[^\r\n]*/g, (c) => " ".repeat(c.length));
      for (const m of uncommented.matchAll(/\bHandles\b[^\r\n]*/gi)) {
        for (const item of m[0].matchAll(/([A-Za-z_]\w*)\s*\.\s*[A-Za-z_]\w*/g)) {
          if (/^(me|mybase|myclass)$/i.test(item[1])) continue;
          const offset = m.index! + item.index! + 1;
          const j = await definitionInCodeBehind(host, codeFile, code, offset);
          record("Handles ctl.Event -> markup control", !!j?.targets.length, `${path.relative(root, codeFile)}: ${item[0]}`);
        }
      }
    }
  }

  let failed = 0;
  for (const [kind, t] of tally) {
    failed += t.fail.length;
    console.log(`${kind}: ${t.ok} ok, ${t.fail.length} unresolved`);
    for (const f of t.fail.slice(0, 40)) console.log(`    ${f}`);
    if (t.fail.length > 40) console.log(`    … ${t.fail.length - 40} more`);
  }
  console.log("\nDiagnostics (what the Problems panel would show if every file were open):");
  for (const [key, t] of [...problemTally].sort()) {
    console.log(`${key}: ${t.examples.length} in ${t.files.size} files`);
    if (key.startsWith("error")) for (const e of t.examples.slice(0, 12)) console.log(`    ${e}`);
  }
  console.log(failed ? `\n${failed} unresolved (each is either a real broken reference or a parser gap).` : "\nAll resolved.");
}

main(process.argv[2] ?? (() => { throw new Error("usage: smoke-navigation.js <folder>"); })());
