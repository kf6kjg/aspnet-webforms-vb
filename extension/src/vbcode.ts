// Light parsing of VB code-behind: method and class declarations and Handles clauses.
// VB is case-insensitive, so every match is too. No vscode imports.

import { Span } from "./markup";

const modifiers = "(?:(?:Public|Private|Protected|Friend|Shared|Overridable|Overrides|Overloads|NotOverridable|MustOverride|Partial|Async)[ \\t]+)*";

function nameSpanOf(m: RegExpExecArray, name: string): Span {
  const start = m.index + m[0].length - name.length;
  return { start, end: start + name.length };
}

function isIdentifier(name: string): boolean {
  return /^[A-Za-z_]\w*$/.test(name);
}

/** The name in `Sub Name(` / `Function Name(`. */
export function findMethod(text: string, name: string): Span | undefined {
  if (!isIdentifier(name)) return undefined;
  const m = new RegExp(`^[ \\t]*${modifiers}(?:Sub|Function)[ \\t]+${name}(?=\\W)`, "im").exec(text);
  return m ? nameSpanOf(m, name) : undefined;
}

/** The name in `Partial Class Name`. Accepts a namespace-qualified name (uses the last part). */
export function findClass(text: string, qualifiedName: string): Span | undefined {
  const name = qualifiedName.split(".").pop() ?? "";
  if (!isIdentifier(name)) return undefined;
  const m = new RegExp(`^[ \\t]*(?:(?:Partial|Public|Friend|MustInherit|NotInheritable)[ \\t]+)*Class[ \\t]+${name}(?=\\W|$)`, "im").exec(text);
  return m ? nameSpanOf(m, name) : undefined;
}

export interface HandlesItem {
  control: string;
  event: string;
  controlSpan: Span;
  /** Span of "control.Event". */
  span: Span;
}

const ownEventSources = new Set(["me", "mybase", "myclass"]);

/** Items of the Handles clause on the line containing `offset`, skipping Me/MyBase. */
function handlesItemsOnLine(text: string, offset: number): HandlesItem[] {
  const lineStart = text.lastIndexOf("\n", offset - 1) + 1;
  const lineEndIdx = text.indexOf("\n", offset);
  const line = text.slice(lineStart, lineEndIdx < 0 ? text.length : lineEndIdx);
  return handlesItemsIn(line, lineStart);
}

function handlesItemsIn(line: string, base: number): HandlesItem[] {
  const code = line.replace(/'.*$/, (c) => " ".repeat(c.length));
  const h = /\bHandles\b/i.exec(code);
  if (!h) return [];
  const items: HandlesItem[] = [];
  const after = h.index + h[0].length;
  for (const m of code.slice(after).matchAll(/([A-Za-z_]\w*)\s*\.\s*([A-Za-z_]\w*)/g)) {
    if (ownEventSources.has(m[1].toLowerCase())) continue;
    const start = base + after + m.index!;
    items.push({
      control: m[1],
      event: m[2],
      controlSpan: { start, end: start + m[1].length },
      span: { start, end: start + m[0].length },
    });
  }
  return items;
}

/** The Handles item (e.g. `btnSave.Click`) under `offset`, if any. */
export function handlesAt(text: string, offset: number): HandlesItem | undefined {
  return handlesItemsOnLine(text, offset).find((i) => offset >= i.span.start && offset <= i.span.end);
}

export interface Handler {
  method: string;
  methodSpan: Span;
  event: string;
}

/** Methods whose Handles clause names `controlId` (e.g. every `Handles btnSave.X`). */
export function handlersOf(text: string, controlId: string): Handler[] {
  const lower = controlId.toLowerCase();
  const result: Handler[] = [];
  // Sub Name(...) [_ newline] Handles a.B, c.D
  const decl = new RegExp(`^[ \\t]*${modifiers}(?:Sub|Function)[ \\t]+([A-Za-z_]\\w*)[ \\t]*\\([^)]*\\)[ \\t]*(?:_[ \\t]*\\r?\\n[ \\t]*)?(Handles\\b[^\\r\\n]*)`, "gim");
  for (const m of text.matchAll(decl)) {
    const handlesStart = m.index! + m[0].length - m[2].length;
    for (const item of handlesItemsIn(m[2], handlesStart)) {
      if (item.control.toLowerCase() === lower) {
        const nameStart = m.index! + m[0].search(new RegExp(`\\b${m[1]}\\b`));
        result.push({ method: m[1], methodSpan: { start: nameStart, end: nameStart + m[1].length }, event: item.event });
      }
    }
  }
  return result;
}
