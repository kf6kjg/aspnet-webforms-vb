// Markup reference checks: the stage 2 resolvers, reported as problems when they miss.
// Errors are references ASP.NET refuses to compile; warnings are local files a browser
// would get a 404 for, and references whose case differs from the file on disk, which
// a case-sensitive server wouldn't find (ADR 0012). Site paths in VB strings
// (Response.Redirect("…"), in .vb files and <% %> blocks) get the same warnings (ADR 0014);
// everything else in VB code is left to the language server (stage 3), which reports it
// as the compiler does. No vscode imports.

import * as path from "node:path";
import { Span, attr, isEventHandlerAttr, isServerTag, mainDirective, maskServerBlocks, directives, serverCode, tags } from "./markup";
import { findClass, findMethod } from "./vbcode";
import { Host, PathRef, Resolved, localPath, locateRef, urlAttrs, vbPathRefs } from "./navigation";

export interface Problem {
  span: Span;
  severity: "error" | "warning";
  code: "missing-codefile" | "missing-class" | "missing-register-src" | "unregistered-tag" | "missing-handler" | "missing-file" | "case-mismatch";
  message: string;
}

export interface CheckOptions {
  /** Report missing href/src/…Url and Response.Redirect("…") files, and their case mismatches (warnings). */
  missingFiles: boolean;
}

/** Wraps a host so repeated directory reads during one check hit the share once. */
function cached(host: Host): Host {
  const dirs = new Map<string, Promise<string[] | undefined>>();
  return {
    readDir: (dir) => {
      if (!dirs.has(dir)) dirs.set(dir, host.readDir(dir));
      return dirs.get(dir)!;
    },
    readText: (file) => host.readText(file),
  };
}

function caseProblem(span: Span, ref: string, found: Resolved): Problem[] {
  if (!found.respelled || found.respelled === ref) return [];
  return [{ span, severity: "warning", code: "case-mismatch", message: `'${ref}' differs in case from the file on disk ('${found.respelled}'); a case-sensitive server wouldn't find it.` }];
}

async function urlProblems(host: Host, file: string, span: Span, ref: string): Promise<Problem[]> {
  const found = await locateRef(host, ref, file);
  if (found) return caseProblem(span, ref, found);
  return [{ span, severity: "warning", code: "missing-file", message: `File '${ref}' not found in the site.` }];
}

async function pathProblems(host: Host, file: string, refs: PathRef[]): Promise<Problem[]> {
  const problems: Problem[] = [];
  for (const r of refs) problems.push(...(await urlProblems(host, file, r.span, r.ref)));
  return problems;
}

export async function checkMarkup(baseHost: Host, file: string, text: string, options: CheckOptions): Promise<Problem[]> {
  const host = cached(baseHost);
  const problems: Problem[] = [];
  const all = directives(text);

  // @Page/@Control/@Master: CodeFile and Inherits.
  let code: string | undefined;
  let codeName: string | undefined;
  const main = mainDirective(text);
  const codeFile = main && attr(main.attrs, "CodeFile");
  if (codeFile) {
    const found = await locateRef(host, codeFile.value, file);
    const target = found?.file;
    if (found) problems.push(...caseProblem(codeFile.valueSpan, codeFile.value, found));
    code = target && (await host.readText(target));
    codeName = target && path.basename(target);
    if (code && !/\.vb$/i.test(target!)) {
      // A C# code-behind (third-party samples): out of scope, so don't judge it by VB rules.
      code = undefined;
    } else if (!code) {
      problems.push({ span: codeFile.valueSpan, severity: "error", code: "missing-codefile", message: `Code-behind file '${codeFile.value}' not found.` });
    } else {
      const inherits = attr(main!.attrs, "Inherits");
      if (inherits && !findClass(code, inherits.value)) {
        problems.push({ span: inherits.valueSpan, severity: "error", code: "missing-class", message: `Class '${inherits.value}' not found in ${codeName}.` });
      }
    }
  }

  // @Register Src.
  const registers = all.filter((d) => d.name.toLowerCase() === "register");
  for (const d of registers) {
    const src = attr(d.attrs, "Src");
    if (!src) continue;
    const found = await locateRef(host, src.value, file);
    if (found) problems.push(...caseProblem(src.valueSpan, src.value, found));
    else problems.push({ span: src.valueSpan, severity: "error", code: "missing-register-src", message: `User control '${src.value}' not found.` });
  }

  const masked = maskServerBlocks(text);
  for (const t of tags(masked)) {
    if (t.closing) continue;

    // <uc1:Header runat="server"> needs a matching @Register. Prefixed tags without
    // runat="server" (e.g. Word's <o:p>) are plain text to ASP.NET.
    if (t.prefix && t.prefix.toLowerCase() !== "asp" && isServerTag(t)) {
      const prefix = t.prefix.toLowerCase();
      const known = registers.some((d) => {
        const p = attr(d.attrs, "TagPrefix")?.value.toLowerCase();
        const n = attr(d.attrs, "TagName")?.value.toLowerCase();
        return p === prefix && (n === undefined || n === t.name.toLowerCase());
      });
      if (!known) {
        problems.push({ span: t.nameSpan, severity: "error", code: "unregistered-tag", message: `Unknown server tag '${t.prefix}:${t.name}': no @Register for it in this file.` });
      }
    }

    for (const a of t.attrs) {
      if (code && isEventHandlerAttr(t, a) && !findMethod(code, a.value)) {
        problems.push({ span: a.valueSpan, severity: "error", code: "missing-handler", message: `'${a.value}' is not a Sub or Function in ${codeName}.` });
      }
      if (options.missingFiles && urlAttrs.has(a.name.toLowerCase())) {
        const ref = localPath(text.slice(a.valueSpan.start, a.valueSpan.end));
        if (ref) problems.push(...(await urlProblems(host, file, a.valueSpan, ref)));
      }
    }
  }
  if (options.missingFiles) problems.push(...(await pathProblems(host, file, vbPathRefs(serverCode(text), file))));
  return problems;
}

/** A .vb file's site paths in strings: the same warnings as URLs in markup. */
export async function checkVb(baseHost: Host, file: string, text: string, options: CheckOptions): Promise<Problem[]> {
  return options.missingFiles ? pathProblems(cached(baseHost), file, vbPathRefs(text, file)) : [];
}
