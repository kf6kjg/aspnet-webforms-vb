// VB statements and `' lang=<language>` marker lines, shared by the string highlighting of
// SQL (ADR 0010, sqlmarker.ts) and HTML, JavaScript, CSS, XML and JSON (ADR 0011,
// langmarker.ts). The statement rules here must match the injection grammars'. No vscode
// imports.

export type Dialect = "sql" | "tsql" | "mysql" | "pgsql";
export type WebLanguage = "html" | "js" | "css" | "xml" | "json";
export type Language = Dialect | WebLanguage;

const aliases: Record<string, Language> = {
  sql: "sql",
  tsql: "tsql",
  mssql: "tsql",
  mysql: "mysql",
  mariadb: "mysql",
  pgsql: "pgsql",
  postgres: "pgsql",
  postgresql: "pgsql",
  html: "html",
  js: "js",
  javascript: "js",
  css: "css",
  xml: "xml",
  json: "json",
};

export function isDialect(language: Language): language is Dialect {
  return language === "sql" || language === "tsql" || language === "mysql" || language === "pgsql";
}

const markerPattern = /^([ \t]*'[ \t]*lang(?:uage)?[ \t]*=[ \t]*)(postgresql|postgres|pgsql|mariadb|mysql|mssql|tsql|sql|html|javascript|js|css|xml|json)\b/i;
/** The first line of an assignment: `x =`, `Dim x As String =`, `Session("x") &=`. */
export const assignmentStart = /^[ \t]*(?:Dim[ \t]+)?[A-Za-z_][\w.]*(?:\([^)]*\))?(?:[ \t]+As[ \t]+[\w.]+)?[ \t]*(?:=|&=|\+=)/i;
const continuation = /[+&_,(=][ \t]*$/;

export interface Marker {
  language: Language;
  /** Column range of the language word, e.g. `sql` in `' lang=sql`. */
  start: number;
  end: number;
}

/** The `' lang=<language>` marker on this line, if the line is one. */
export function markerOf(line: string): Marker | undefined {
  const m = markerPattern.exec(line);
  if (!m) return undefined;
  return { language: aliases[m[2].toLowerCase()], start: m[1].length, end: m[1].length + m[2].length };
}

export interface Lexed {
  /** The code with each string literal replaced by `""` and the comment removed. */
  code: string;
  /** Contents of the string literals, `""` unescaped. */
  strings: string[];
  hasComment: boolean;
}

export function lex(line: string): Lexed {
  let code = "";
  const strings: string[] = [];
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (c === '"') {
      let s = "";
      let j = i + 1;
      for (; j < line.length; j++) {
        if (line[j] !== '"') s += line[j];
        else if (line[j + 1] === '"') { s += '"'; j++; }
        else break;
      }
      strings.push(s);
      code += '""';
      i = j;
    } else if (c === "'") {
      return { code, strings, hasComment: true };
    } else {
      code += c;
    }
  }
  return { code, strings, hasComment: false };
}

interface LineShape {
  /** A comment, or no trailing + & _ , ( =: the statement ends here unless a parenthesis is open. */
  ends: boolean;
  /** VB 10 has no comments inside a statement, so a comment always ends one. */
  comment: boolean;
  /** Parentheses this line closes that it didn't open, and opens that it doesn't close. */
  closes: number;
  opens: number;
}

function shape(line: string): LineShape {
  const l = lex(line);
  let depth = 0, closes = 0;
  for (const c of l.code) {
    if (c === "(") depth++;
    else if (c === ")" && --depth < 0) { closes++; depth = 0; }
  }
  return { ends: l.hasComment || !continuation.test(l.code), comment: l.hasComment, closes, opens: depth };
}

export interface Statement {
  /** First and last line, inclusive. */
  start: number;
  end: number;
}

/**
 * The statement containing `line`. A comment ends a VB 10 statement; otherwise it
 * continues past a trailing + & _ , ( = and while a parenthesis is open, as after
 * `IIf(a And` (the grammars' parenthesis rules span lines the same way).
 */
export function statementAt(lines: readonly string[], line: number): Statement {
  // Back: a line that ends a statement is a boundary only if nothing after it closes a parenthesis opened before it.
  let start = line;
  let unclosed = shape(lines[line]).closes;
  while (start > 0 && line - start < 200) {
    const prev = shape(lines[start - 1]);
    if (prev.comment || (prev.ends && unclosed === 0)) break;
    unclosed = prev.closes + Math.max(0, unclosed - prev.opens);
    start--;
  }
  let end = start;
  let depth = 0;
  for (; end < lines.length - 1; end++) {
    const s = shape(lines[end]);
    depth = Math.max(0, depth - s.closes) + s.opens;
    if (s.comment || (s.ends && depth === 0)) break;
  }
  return { start, end: Math.max(end, line) };
}

/** The contents of every string literal in the statement. */
export function fragmentsOf(lines: readonly string[], st: Statement): string[] {
  const fragments: string[] = [];
  for (let i = st.start; i <= st.end; i++) fragments.push(...lex(lines[i]).strings);
  return fragments;
}

/** The last line of the statement a marker on `line` marks: a blank line right after the marker cancels it. */
export function markedEnd(lines: readonly string[], line: number): number {
  return lines[line + 1].trim() === "" ? line + 1 : statementAt(lines, line + 1).end;
}
