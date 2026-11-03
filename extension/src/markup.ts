// Light parsing of .aspx/.ascx/.master markup: directives, tags and attributes, with
// character offsets so the editor layer can map them to ranges. Regex-based on purpose
// (ADR 0005, stage 2: no compiler). No vscode imports, so it runs under plain node.

export interface Span {
  start: number;
  end: number;
}

export interface Attr {
  name: string;
  value: string;
  nameSpan: Span;
  valueSpan: Span;
}

export interface Directive {
  /** "Page", "Control", "Register", …; "" for the nameless `<%@ Language="VB" %>` form. */
  name: string;
  span: Span;
  attrs: Attr[];
}

export interface Tag {
  /** "asp" in <asp:Button>; undefined for plain HTML tags. */
  prefix?: string;
  name: string;
  /** Span of the whole "prefix:name" (or "name") token. */
  nameSpan: Span;
  span: Span;
  attrs: Attr[];
  closing: boolean;
}

const attrPattern = /([A-Za-z_][\w.:-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))/g;

function parseAttrs(text: string, base: number): Attr[] {
  const attrs: Attr[] = [];
  for (const m of text.matchAll(attrPattern)) {
    const value = m[2] ?? m[3] ?? m[4] ?? "";
    const quoted = m[2] !== undefined || m[3] !== undefined;
    const start = base + m.index!;
    const valueEnd = start + m[0].length - (quoted ? 1 : 0);
    attrs.push({
      name: m[1],
      value,
      nameSpan: { start, end: start + m[1].length },
      valueSpan: { start: valueEnd - value.length, end: valueEnd },
    });
  }
  return attrs;
}

export function attr(attrs: Attr[], name: string): Attr | undefined {
  const lower = name.toLowerCase();
  return attrs.find((a) => a.name.toLowerCase() === lower);
}

export function within(span: Span, offset: number): boolean {
  return offset >= span.start && offset <= span.end;
}

/**
 * Blank out <% … %> blocks and <%-- … --%> comments (same length, newlines kept) so
 * tag parsing doesn't trip over quotes or '>' inside server code. Controls inside a
 * server comment disappear, as they do for ASP.NET; HTML comments are kept because
 * ASP.NET still parses server controls inside them.
 */
export function maskServerBlocks(text: string): string {
  return text.replace(/<%--[\s\S]*?--%>|<%[\s\S]*?%>/g, (m) => m.replace(/[^\n]/g, " "));
}

export function directives(text: string): Directive[] {
  const result: Directive[] = [];
  for (const m of text.matchAll(/<%@\s*([\s\S]*?)%>/g)) {
    const bodyStart = m.index! + m[0].indexOf(m[1], 3);
    const nameMatch = /^([A-Za-z]+)\b(?!\s*=)/.exec(m[1]);
    const name = nameMatch ? nameMatch[1] : "";
    const attrsText = nameMatch ? m[1].slice(name.length) : m[1];
    result.push({
      name,
      span: { start: m.index!, end: m.index! + m[0].length },
      attrs: parseAttrs(attrsText, bodyStart + (nameMatch ? name.length : 0)),
    });
  }
  return result;
}

/** The @Page, @Control or @Master directive, which carries CodeFile and Inherits. */
export function mainDirective(text: string): Directive | undefined {
  return directives(text).find((d) => /^(page|control|master)$/i.test(d.name));
}

export interface Registration {
  tagPrefix: string;
  tagName: string;
  src: Attr;
}

/** User controls registered with <%@ Register TagPrefix TagName Src %>. */
export function registrations(text: string): Registration[] {
  return directives(text)
    .filter((d) => d.name.toLowerCase() === "register")
    .flatMap((d) => {
      const prefix = attr(d.attrs, "TagPrefix");
      const name = attr(d.attrs, "TagName");
      const src = attr(d.attrs, "Src");
      return prefix && name && src ? [{ tagPrefix: prefix.value, tagName: name.value, src }] : [];
    });
}

const tagPattern = /<(\/?)([A-Za-z][\w.-]*)(?::([A-Za-z][\w.-]*))?((?:"[^"]*"|'[^']*'|[^'">])*)>/g;

/** All tags in already-masked markup (see maskServerBlocks). */
export function tags(masked: string): Tag[] {
  const result: Tag[] = [];
  for (const m of masked.matchAll(tagPattern)) {
    const start = m.index!;
    const nameStart = start + 1 + m[1].length;
    const nameLength = m[2].length + (m[3] !== undefined ? 1 + m[3].length : 0);
    result.push({
      prefix: m[3] !== undefined ? m[2] : undefined,
      name: m[3] ?? m[2],
      nameSpan: { start: nameStart, end: nameStart + nameLength },
      span: { start, end: start + m[0].length },
      attrs: parseAttrs(m[4], nameStart + nameLength),
      closing: m[1] === "/",
    });
  }
  return result;
}

export function isServerTag(tag: Tag): boolean {
  return attr(tag.attrs, "runat")?.value.toLowerCase() === "server";
}

/** The runat="server" tag whose ID matches, case-insensitively as in VB. */
export function findControl(masked: string, id: string): Tag | undefined {
  const lower = id.toLowerCase();
  return tags(masked).find((t) => !t.closing && isServerTag(t) && attr(t.attrs, "ID")?.value.toLowerCase() === lower);
}

/**
 * Attributes that name a code-behind method: On* on prefixed server tags (OnClick,
 * OnSelectedIndexChanged, …, but not the client-side OnClientClick), and OnServer* on
 * HTML server tags (onserverclick).
 */
export function isEventHandlerAttr(tag: Tag, a: Attr): boolean {
  if (!isServerTag(tag) || !/^\w+$/.test(a.value)) return false;
  if (/^onserver/i.test(a.name)) return true;
  return tag.prefix !== undefined && /^on/i.test(a.name) && !/^onclient/i.test(a.name);
}
