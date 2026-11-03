// Stage 2 navigation (ADR 0005): markup <-> code-behind, OnClick="Foo" -> Sub Foo,
// Handles ctl.Event <-> the control in markup, <uc1:X> / @Register Src -> the .ascx,
// and site paths (href=, Response.Redirect("…"), …) -> the file.
// Pure logic over an injected file host, shared by the extension and the smoke test.

import * as path from "node:path";
import { Span, attr, findControl, isEventHandlerAttr, isServerTag, mainDirective, maskServerBlocks, registrations, directives, serverCode, tags, within } from "./markup";
import { findClass, findMethod, handlersOf, handlesAt, vbStrings } from "./vbcode";
import { DirReader, findSiteRoot, isCodeBehind, markupForCodeBehind, resolveOnDisk, resolveReference, respelled } from "./paths";

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

export interface Resolved {
  file: string;
  /** `ref` spelled as the file is named on disk, when its case differs. */
  respelled?: string;
}

/** Where `ref`, written in `file`, points on disk. */
export async function locateRef(host: Host, ref: string, file: string): Promise<Resolved | undefined> {
  // From the on-disk spelling of the file's own folder, so only the reference's case is judged.
  const dir = (await resolveOnDisk(host, path.dirname(file))) ?? path.dirname(file);
  const siteRoot = ref.startsWith("~") || ref.startsWith("/") ? await findSiteRoot(host, dir) : undefined;
  const wanted = path.resolve(resolveReference(ref, dir, siteRoot));
  const found = await resolveOnDisk(host, wanted);
  if (!found) return undefined;
  return found === wanted ? { file: found } : { file: found, respelled: respelled(ref, found) };
}

export async function resolveRef(host: Host, ref: string, file: string): Promise<string | undefined> {
  return (await locateRef(host, ref, file))?.file;
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

  const inCode = await pathJump(host, file, vbPathRefs(serverCode(text), file), offset);
  if (inCode) return inCode;

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

/** VB calls whose first argument is a site path (Response.Redirect, Server.MapPath, Page.ResolveUrl, …). */
const vbPathCall = /(?:\b(?:Redirect|RedirectPermanent|MapPath|ResolveUrl|ResolveClientUrl|LoadControl)|\bServer[ \t]*\.[ \t]*(?:Transfer|Execute))[ \t]*\([ \t]*"$/i;
/** `img.Src = "…"`: an assignment to one of the urlAttrs properties. */
const vbUrlProperty = new RegExp(`\\.[ \\t]*(?:${[...urlAttrs].join("|")})[ \\t]*=[ \\t]*"$`, "i");

export interface PathRef {
  /** The file part, as localPath returns it. */
  ref: string;
  /** The literal's contents, inside the quotes. */
  span: Span;
}

/**
 * Site paths in VB string literals: the first argument of a vbPathCall, or a value
 * assigned to a URL property. A literal joined to more with & or + counts only once its
 * path is complete (a ? or # is in it), so "~/Pics/" & name is skipped. A folder
 * (Server.MapPath("\Uploads\") & name) isn't a file to open, and is often a virtual
 * directory, so a trailing slash skips it too. Relative paths count in code-behind and
 * markup, which sit beside their page, but not in App_Code, which runs for whatever page
 * was requested.
 */
export function vbPathRefs(text: string, file: string): PathRef[] {
  const { code, strings } = vbStrings(text);
  const relativeOk = !/\.vb$/i.test(file) || isCodeBehind(file);
  const joined = /[ \t]*[&+]/y;
  const refs: PathRef[] = [];
  for (const s of strings) {
    const before = code.slice(code.lastIndexOf("\n", s.span.start) + 1, s.span.start);
    if (!vbPathCall.test(before) && !vbUrlProperty.test(before)) continue;
    joined.lastIndex = s.span.end + 1;
    if (joined.test(code) && !/[?#]/.test(s.value)) continue;
    const ref = localPath(s.value);
    if (ref && !/[\\/]$/.test(ref) && (relativeOk || /^[~/\\]/.test(ref))) refs.push({ ref, span: s.span });
  }
  return refs;
}

async function pathJump(host: Host, file: string, refs: PathRef[], offset: number): Promise<Jump | undefined> {
  const r = refs.find((x) => within(x.span, offset));
  const target = r && (await resolveRef(host, r.ref, file));
  return target ? { origin: r!.span, targets: [{ file: target }] } : undefined;
}

/** A site path in a VB string (Response.Redirect("~/Buy.aspx"), img.Src = "…") -> that file. */
export async function definitionInVb(host: Host, file: string, text: string, offset: number): Promise<Jump | undefined> {
  return pathJump(host, file, vbPathRefs(text, file), offset);
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
