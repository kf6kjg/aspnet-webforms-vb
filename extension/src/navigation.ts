// Stage 2 navigation (ADR 0005): markup <-> code-behind, OnClick="Foo" -> Sub Foo,
// Handles ctl.Event <-> the control in markup, <uc1:X> / @Register Src -> the .ascx.
// Pure logic over an injected file host, shared by the extension and the smoke test.

import * as path from "node:path";
import { Span, attr, findControl, isEventHandlerAttr, isServerTag, mainDirective, maskServerBlocks, registrations, directives, tags, within } from "./markup";
import { findClass, findMethod, handlersOf, handlesAt } from "./vbcode";
import { DirReader, findSiteRoot, markupForCodeBehind, resolveOnDisk, resolveReference } from "./paths";

export interface Host extends DirReader {
  /** File text, or undefined if it can't be read. */
  readText(file: string): Promise<string | undefined>;
}

export interface Target {
  file: string;
  /** Range to select in the target file's text; omitted means the top of the file. */
  span?: Span;
}

export interface Jump {
  /** What to underline in the source document. */
  origin: Span;
  targets: Target[];
}

export async function resolveRef(host: Host, ref: string, file: string): Promise<string | undefined> {
  const dir = path.dirname(file);
  const siteRoot = ref.startsWith("~") || ref.startsWith("/") ? await findSiteRoot(host, dir) : undefined;
  return resolveOnDisk(host, resolveReference(ref, dir, siteRoot));
}

/** The code-behind named by CodeFile=, falling back to "<markup>.vb". */
export async function codeBehindFor(host: Host, markupFile: string, markupText: string): Promise<string | undefined> {
  const codeFile = mainDirective(markupText) && attr(mainDirective(markupText)!.attrs, "CodeFile");
  return (codeFile && (await resolveRef(host, codeFile.value, markupFile))) ?? resolveOnDisk(host, markupFile + ".vb");
}

export async function markupFor(host: Host, codeBehindFile: string): Promise<string | undefined> {
  const markup = markupForCodeBehind(codeBehindFile);
  return markup ? resolveOnDisk(host, markup) : undefined;
}

async function inCodeBehind(host: Host, markupFile: string, markupText: string, find: (code: string) => Span[]): Promise<Target[]> {
  const codeFile = await codeBehindFor(host, markupFile, markupText);
  const code = codeFile && (await host.readText(codeFile));
  return code ? find(code).map((span) => ({ file: codeFile!, span })) : [];
}

export async function definitionInMarkup(host: Host, file: string, text: string, offset: number): Promise<Jump | undefined> {
  const directive = directives(text).find((d) => within(d.span, offset));
  if (directive) {
    const a = directive.attrs.find((x) => within(x.valueSpan, offset));
    if (!a) return undefined;
    const name = a.name.toLowerCase();
    const isMain = /^(page|control|master)$/i.test(directive.name);
    if (isMain && name === "codefile") {
      const target = await codeBehindFor(host, file, text);
      return target ? { origin: a.valueSpan, targets: [{ file: target }] } : undefined;
    }
    if (isMain && name === "inherits") {
      return { origin: a.valueSpan, targets: await inCodeBehind(host, file, text, (code) => [findClass(code, a.value)].filter((s): s is Span => !!s)) };
    }
    if (directive.name.toLowerCase() === "register" && name === "src") {
      const target = await resolveRef(host, a.value, file);
      return target ? { origin: a.valueSpan, targets: [{ file: target }] } : undefined;
    }
    return undefined;
  }

  const tag = tags(maskServerBlocks(text)).find((t) => within(t.span, offset));
  if (!tag) return undefined;

  if (within(tag.nameSpan, offset) && tag.prefix) {
    const reg = registrations(text).find((r) => r.tagPrefix.toLowerCase() === tag.prefix!.toLowerCase() && r.tagName.toLowerCase() === tag.name.toLowerCase());
    const target = reg && (await resolveRef(host, reg.src.value, file));
    return target ? { origin: tag.nameSpan, targets: [{ file: target }] } : undefined;
  }

  const a = tag.attrs.find((x) => within(x.valueSpan, offset));
  if (!a || a.value.length === 0) return undefined;
  if (isEventHandlerAttr(tag, a)) {
    return { origin: a.valueSpan, targets: await inCodeBehind(host, file, text, (code) => [findMethod(code, a.value)].filter((s): s is Span => !!s)) };
  }
  if (a.name.toLowerCase() === "id" && isServerTag(tag)) {
    return { origin: a.valueSpan, targets: await inCodeBehind(host, file, text, (code) => handlersOf(code, a.value).map((h) => h.methodSpan)) };
  }
  if (urlAttrs.has(a.name.toLowerCase())) {
    // Read the value from the unmasked text so server code (<%= %>) is visible.
    const ref = localPath(text.slice(a.valueSpan.start, a.valueSpan.end));
    const target = ref && (await resolveRef(host, ref, file));
    return target ? { origin: a.valueSpan, targets: [{ file: target }] } : undefined;
  }
  return undefined;
}

/** HTML attributes and ASP.NET control properties that hold a URL. */
export const urlAttrs = new Set(["href", "src", "action", "background", "imageurl", "navigateurl", "postbackurl"]);

/**
 * The file part of a URL that points into the site, or undefined for external URLs
 * (http:, mailto:, javascript:, //host), in-page anchors, URLs whose path is built by
 * server code, and HTML inside JavaScript strings or client templates
 * (href="' + item.url + '", src="{{{img}}}").
 */
export function localPath(url: string): string | undefined {
  const v = url.trim();
  if (!v || v.startsWith("#") || v.startsWith("//") || /^[a-z][a-z0-9+.-]*:/i.test(v)) return undefined;
  const p = v.split(/[?#]/)[0];
  if (!p || p.includes("<%") || /[{}'"]|\s\+|\+\s/.test(p)) return undefined;
  try {
    return decodeURI(p);
  } catch {
    return p;
  }
}

export async function definitionInCodeBehind(host: Host, file: string, text: string, offset: number): Promise<Jump | undefined> {
  const item = handlesAt(text, offset);
  if (!item || !within(item.controlSpan, offset)) return undefined;
  const markupFile = await markupFor(host, file);
  const markup = markupFile && (await host.readText(markupFile));
  if (!markup) return undefined;
  const control = findControl(maskServerBlocks(markup), item.control);
  const id = control && attr(control.attrs, "ID");
  return id ? { origin: item.controlSpan, targets: [{ file: markupFile!, span: id.valueSpan }] } : undefined;
}
