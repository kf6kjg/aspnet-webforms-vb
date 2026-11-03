// SQL in VB strings (ADR 0010): which statements the grammar
// (syntaxes/vb-sql.injection.tmLanguage.json) highlights as SQL, and which
// `' lang=<dialect>` marker to suggest for a statement. Statements and markers are
// vbstatement.ts's. The statement rules here must match the grammar's. No vscode imports.
import { assignmentStart, Dialect, fragmentsOf, isDialect, markedEnd, markerOf, Statement, statementAt } from "./vbstatement";

export type { Dialect };

export const dialectNames: Record<Dialect, string> = { sql: "SQL", tsql: "T-SQL", mysql: "MySQL", pgsql: "PostgreSQL" };

export const sqlCmdStart = /^[ \t]*SQLCmd\d*\b(?=[ \t]*=)/i;

export interface SqlRegion extends Statement {
  dialect: Dialect;
  /** The marker line, if the statement is marked; it belongs to the region. */
  marker?: number;
}

/** Every region the grammar highlights as SQL, in order. */
export function sqlRegions(lines: readonly string[]): SqlRegion[] {
  const regions: SqlRegion[] = [];
  for (let i = 0; i < lines.length; i++) {
    const marker = markerOf(lines[i]);
    if (marker && i + 1 < lines.length) {
      // The marker's own line counts as the region's start, as in the grammar. Another
      // language's marker claims the statement too (ADR 0011).
      const end = markedEnd(lines, i);
      if (isDialect(marker.language)) regions.push({ start: i, end, dialect: marker.language, marker: i });
      i = end;
    } else if (sqlCmdStart.test(lines[i])) {
      const end = statementAt(lines, i).end;
      regions.push({ start: i, end, dialect: "sql" });
      i = end;
    }
  }
  return regions;
}

const dialectSyntax: Record<"tsql" | "mysql", RegExp[]> = {
  tsql: [/\[\w+\]/, /\bTop\s+\(?\d/i, /\bGetDate\(\)/i, /\bIsNull\(/i, /\bScope_Identity\(/i, /@@Identity/i, /\bConvert\(\s*\w+\s*,/i, /\bNVarChar\b/i, /\bDateAdd\(\s*\w+\s*,/i],
  mysql: [/`/, /\bLimit\s+\d/i, /\bNow\(\)/i, /\bIfNull\(/i, /\bConcat\(/i, /\bDate_Format\(/i, /\bLast_Insert_Id\(/i, /\bGroup_Concat\(/i],
};

/** Dialects whose specific syntax the SQL text uses. */
export function dialectsIn(sql: string): Dialect[] {
  return (["tsql", "mysql"] as const).filter((d) => dialectSyntax[d].some((p) => p.test(sql)));
}

const statementHead = /^\s*(?:Select|Insert|Replace|Update|Delete)\b/i;
const strongSql = /^\s*(?:Select\b[\s\S]*\bFrom\b|(?:Insert|Replace)\s+Into\b|Update\s+[`[\]\w.]+\s+Set\b|Delete\s+From\b)/i;
const clauseStart = /^\s*(?:From|Where|And|Or|Order\s+By|Group\s+By|Having|Inner|Left|Right|Join|Values|Set|Limit|Union|On)\b/i;
const conditionStart = /^\s*(?:(?:Where|And|Or)\s+\(?[\w.]+\s*(?:<>|<=|>=|=|<|>|\bIn\b|\bLike\b|\bIs\b)|(?:Order|Group)\s+By\s+[\w.]+)/i;

/**
 * Fragments that look strongly like SQL: one starts a statement that the rest complete
 * (`Select … From`, `Insert Into`, `Update x Set`, `Delete From`), one starts with a
 * condition on a column (` and Language=`, ` Order by SortOrder`), or more than one starts
 * with a clause keyword. Log messages such as "Insert Page record: " don't qualify.
 */
export function looksLikeSql(fragments: readonly string[]): boolean {
  for (let i = 0; i < fragments.length; i++) {
    if (statementHead.test(fragments[i]) && strongSql.test(fragments.slice(i).join(" "))) return true;
  }
  return fragments.some((f) => conditionStart.test(f)) || fragments.filter((f) => clauseStart.test(f)).length > 1;
}

export interface Suggestion {
  dialect: Dialect;
  preferred: boolean;
  /** Insert `text` as a new line above this line… */
  insertAbove?: number;
  /** …or replace the dialect word of the existing marker on this line. */
  replace?: { line: number; start: number; end: number };
  text: string;
}

/** Markers worth suggesting for the statement containing `line` (ADR 0010). */
export function suggestMarker(lines: readonly string[], line: number): Suggestion[] {
  if (line >= lines.length || markerOf(lines[line])) return [];
  const st = statementAt(lines, line);
  const first = lines[st.start];
  const fragments = fragmentsOf(lines, st);
  const found = dialectsIn(fragments.join(" "));
  const marker = st.start > 0 ? markerOf(lines[st.start - 1]) : undefined;
  const indent = /^[ \t]*/.exec(first)![0];

  let dialects: Dialect[];
  if (marker) {
    if (marker.language !== "sql") return [];
    dialects = found;
  } else if (sqlCmdStart.test(first)) {
    dialects = found;
  } else if (assignmentStart.test(first) && looksLikeSql(fragments)) {
    dialects = found.length ? found : ["sql"];
  } else {
    return [];
  }
  return dialects.map((dialect) => ({
    dialect,
    preferred: dialects.length === 1,
    ...(marker
      ? { replace: { line: st.start - 1, start: marker.start, end: marker.end }, text: dialect }
      : { insertAbove: st.start, text: `${indent}' lang=${dialect}` }),
  }));
}
