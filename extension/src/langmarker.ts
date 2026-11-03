// HTML, JavaScript, CSS, XML and JSON in VB strings (ADR 0011): which statements the
// grammar (syntaxes/vb-web.injection.tmLanguage.json) highlights in which language, and
// which `' lang=<language>` marker to suggest for a statement. Statements and markers are
// vbstatement.ts's. The statement rules here must match the grammar's. No vscode imports.
import { sqlCmdStart } from "./sqlmarker";
import { assignmentStart, fragmentsOf, isDialect, markedEnd, markerOf, Statement, statementAt, WebLanguage } from "./vbstatement";

export type { WebLanguage };

export const languageNames: Record<WebLanguage, string> = { html: "HTML", js: "JavaScript", css: "CSS", xml: "XML", json: "JSON" };

/** Statements highlighted without a marker, by their first line. */
const implicitRules: [RegExp, WebLanguage][] = [
  // Assignment to a name with html in it: tHtmlOut, OutHtml, X.InnerHtml, the typo tHMTLOut.
  [/^[ \t]*(?:\w+\.)*\w*(?:html|hmtl)\w*[ \t]*[&+]?=/i, "html"],
  [/^[ \t]*(?:\w+\.)*Attributes\.Add[ \t]*\([ \t]*"on[a-z]+"[ \t]*,/i, "js"],
  [/^[ \t]*(?:\w+\.)*Attributes(?:\.Item)?[ \t]*\([ \t]*"on[a-z]+"[ \t]*\)[ \t]*=/i, "js"],
  // The SBMenu / TabNav / TreeView helpers run an "L" entry's second argument as JavaScript.
  [/^[ \t]*(?:\w+\.)*AddItem[ \t]*\([ \t]*"L"[ \t]*,/i, "js"],
  [/^[ \t]*(?:\w+\.)*Attributes\.Add[ \t]*\([ \t]*"style"[ \t]*,/i, "css"],
  [/^[ \t]*(?:\w+\.)*Attributes(?:\.Item)?[ \t]*\([ \t]*"style"[ \t]*\)[ \t]*=/i, "css"],
];

/** The language of a statement starting with this line, without a marker. */
export function implicitLanguage(line: string): WebLanguage | undefined {
  return implicitRules.find(([pattern]) => pattern.test(line))?.[1];
}

export interface LanguageRegion extends Statement {
  language: WebLanguage;
  /** The marker line, if the statement is marked; it belongs to the region. */
  marker?: number;
}

/** Every region the grammar highlights as HTML, JavaScript, CSS, XML or JSON, in order. */
export function languageRegions(lines: readonly string[]): LanguageRegion[] {
  const regions: LanguageRegion[] = [];
  for (let i = 0; i < lines.length; i++) {
    const marker = markerOf(lines[i]);
    if (marker && i + 1 < lines.length) {
      const end = markedEnd(lines, i);
      if (!isDialect(marker.language)) regions.push({ start: i, end, language: marker.language, marker: i });
      i = end;
    } else if (sqlCmdStart.test(lines[i])) {
      i = statementAt(lines, i).end;
    } else {
      const language = implicitLanguage(lines[i]);
      if (!language) continue;
      const end = statementAt(lines, i).end;
      regions.push({ start: i, end, language });
      i = end;
    }
  }
  return regions;
}

const htmlTag = "table|tr|td|th|div|span|p|a|img|input|select|option|ul|ol|li|h[1-6]|br|hr|b|i|u|font|center|form|textarea|iframe|label|button|html|head|body|script|style|link|video|source|footer";
const closingTag = "table|tr|td|th|div|span|p|a|ul|ol|li|select|option|b|i|u|font|center|form|textarea|label|button|html|head|body|script|style|h[1-6]";
/** A fragment that starts with a tag, a comment or a doctype, or that closes a tag. A `<br>` in a message doesn't qualify. */
const strongHtml = new RegExp(`^\\s*<(?:/?(?:${htmlTag})\\b|!--|!DOCTYPE)|</(?:${closingTag})>`, "i");
const xmlProlog = /^\s*<\?xml\b/i;
const jsonStart = /^\s*(?:\{\s*"|\[\s*[{["])/;
// Case-sensitive, so a VB-looking "Function buy_land(" in a log message doesn't count.
const strongJs = /^\s*(?:var\s+[\w$]+\s*=|function\b[\s\w$]*\(|(?:document|window|opener)\.\w|alert\()/;
const cssProperty = "color|background(?:-[a-z]+)?|font(?:-[a-z]+)?|margin(?:-[a-z]+)?|padding(?:-[a-z]+)?|width|height|display|border(?:-[a-z]+)?|text-[a-z]+|float|position|visibility|vertical-align|cursor|overflow(?:-[xy])?|left|top|right|bottom|z-index|line-height|white-space";
/** A fragment that is nothing but declarations: `display: none;`, `cursor: pointer; color: #F00;`. */
const strongCss = new RegExp(`^\\s*(?:(?:${cssProperty})\\s*:\\s*[^:;]+;\\s*)+$`, "i");
const responseWrite = /^[ \t]*Response\.Write[ \t]*\(/i;
const returnStart = /^[ \t]*Return\b/i;

/**
 * Languages the fragments look strongly like, most likely first. HTML carries JavaScript
 * and CSS in its attributes, so a statement with HTML isn't offered those as well.
 */
export function languagesIn(fragments: readonly string[]): WebLanguage[] {
  const found: WebLanguage[] = [];
  if (fragments.some((f) => strongHtml.test(f))) found.push("html");
  if (fragments.some((f) => xmlProlog.test(f))) found.push("xml");
  if (fragments.some((f) => jsonStart.test(f))) found.push("json");
  if (!found.includes("html")) {
    if (fragments.some((f) => strongJs.test(f))) found.push("js");
    if (fragments.some((f) => strongCss.test(f))) found.push("css");
  }
  return found;
}

export interface LanguageSuggestion {
  language: WebLanguage;
  preferred: boolean;
  /** Insert `text` as a new line above this line. */
  insertAbove: number;
  text: string;
}

/**
 * Markers worth suggesting for the statement containing `line` (ADR 0011): on an unmarked
 * assignment, Response.Write or Return that isn't already SQL or highlighted without a
 * marker, and whose strings look strongly like one of the languages.
 */
export function suggestLanguage(lines: readonly string[], line: number): LanguageSuggestion[] {
  if (line >= lines.length || markerOf(lines[line])) return [];
  const st = statementAt(lines, line);
  const first = lines[st.start];
  if (st.start > 0 && markerOf(lines[st.start - 1])) return [];
  if (sqlCmdStart.test(first) || implicitLanguage(first)) return [];
  if (!assignmentStart.test(first) && !responseWrite.test(first) && !returnStart.test(first)) return [];
  const found = languagesIn(fragmentsOf(lines, st));
  const indent = /^[ \t]*/.exec(first)![0];
  return found.map((language) => ({
    language,
    preferred: found.length === 1,
    insertAbove: st.start,
    text: `${indent}' lang=${language}`,
  }));
}
