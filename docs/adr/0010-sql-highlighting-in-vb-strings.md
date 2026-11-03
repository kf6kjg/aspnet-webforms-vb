# 0010. SQL highlighting in VB strings

Date: 2026-11-02 · Status: Accepted; the statement end (open parentheses), shared statement code and grammar structure amended by [ADR 0011](0011-web-languages-in-vb-strings.md)

## Context

The developer's code builds SQL in VB string literals, and VS Code shows it as one plain
string. The project director asked for SQL highlighting there, which goes beyond
[ADR 0005](0005-feature-scope.md)'s list, so this records the decision.

Measured on the developer's sites (read-only, 1,730 `.vb` files, backup copies skipped):
- **One variable holds the SQL.** Roslyn finds 11,415 assignments to `SQLCmd` (or
  `SQLCmd2`, …). Each starts a line, uses plain `=`, and joins string fragments with `+`.
  70% of them span several lines by implicit line continuation. `SQLCmd` is a class field
  (`Private SQLCmd As String`) in about 1,550 files. About 300 SQL-building statements use
  other variables (`tWhere`, `tSelect`, `SQLUpd`, …). No SQL is in the markup:
  SqlDataSources get theirs in code (`.SelectCommand = SQLCmd`).
- **Recognizing SQL by the string's own text doesn't work.** Queries are split into
  fragments like `","`, `") Values ("` and `"SchoolID"`, so a text test finds only 48% of
  them. Of the strings it does flag, 13% aren't SQL but log messages such as
  `"Insert Page record: "`.
- **Two dialects, mixed even within one site folder.** The `MyDB` helper class connects
  with `MySqlConnection` in some apps and `SqlConnection` in others. 92% of statements
  contain nothing dialect-specific. The rest use T-SQL (`[name]`, `Top n`, `GetDate()`,
  `IsNull()`) or MySQL (`` `name` ``, `Limit n`, `Now()`, `IfNull()`, `Concat()`).
  PostgreSQL may come later.
- **Keyword case:** `Select`, `From`, `Where`. All-caps SQL is rare (50 statements).

## Decision

- **A TextMate injection grammar for VB files**, with no language-server work:
  - **Implicit:** a statement that starts a line with `SQLCmd =` (`SQLCmd\d*`) is SQL
    through the end of the statement, highlighted with the generic dialect. This is a
    hardcoded convention, per [ADR 0001](0001-hardcode-one-developers-conventions.md).
  - **Explicit:** a comment line `' lang=<dialect>` directly before a statement marks
    that statement as SQL in that dialect. A blank line in between cancels it. It also overrides the default on a
    `SQLCmd` statement. It goes on the line before because VB 10 doesn't allow a comment
    inside a statement that continues onto the next line. The marker is
    case-insensitive, accepts `language=` as well as `lang=`, and allows spaces around `=`.
- **Marker dialects:**
  - `sql`: generic
  - `tsql` (alias `mssql`)
  - `mysql` (alias `mariadb`)
  - `pgsql` (aliases `postgres`, `postgresql`): for now the common set plus `$1`
    parameters, `::` casts, `Limit`, `Returning` and `ILike`. It can be extended when a
    PostgreSQL site appears.

  Each dialect has its own marker rule, built from shared rules: one common set, one set
  per dialect, and a generic set that combines every dialect's set. The generic set leaves
  `#` alone, because it starts a comment in MySQL but names a temporary table in T-SQL.
- **The end of a statement** is a comment, or the first line that doesn't end in a
  continuation token (`+`, `&`, `,`, `(`, `=`, `_`). This is exact for VB 10: the
  prototype matched Roslyn's statement spans on all 43,859 lines of the 11,415 statements,
  and highlighted nothing outside them. The code action applies the same rule in
  `src/sqlmarker.ts`. `mise run ext:smoke` checks that the grammar and that file agree on
  every real `.vb` file.
- **Coloring stays inside each fragment.** A `'…'` SQL literal is colored only when it
  closes in the same fragment, so `"Name='" + x + "'"` can't break the VB string coloring.
  Colored: keywords, functions, literals, quoted names, numbers, operators, `@`
  parameters. The VB string keeps its string scope, so themes still treat it as a string.
- **A code action ("Mark as SQL (…)") suggests the marker**, offered by the extension, not
  the server:
  - On an unmarked assignment to another variable whose fragments look strongly like SQL:
    `Select … From`, `Insert Into`, `Update x Set` or `Delete From`; a fragment that
    starts with a condition on a column (`and Language=`, `Order by SortOrder`); or
    more than one fragment starting with a clause keyword such as `Where`, `And`, `From`.
  - On a `SQLCmd` statement, or one marked `lang=sql`, that uses dialect-specific syntax.
  - The suggested dialect comes from the statement's own syntax. With no dialect
    syntax, it suggests `sql`. With both dialects' syntax, it offers both and prefers
    neither.
  - It inserts `' lang=<dialect>` above the statement at the statement's indentation,
    or changes the dialect word of an existing `lang=sql` marker.
- **No diagnostic for possible SQL.** A hint would sit on every false positive for good,
  and diagnostics in this extension mean real problems.
- A `lang=sql` snippet in `vb-webforms.code-snippets` puts the marker in completion.
- Not included: per-app or per-file dialect defaults, which only the server could apply
  across files. Also not included: SQL error checking. If it is ever added, it would read
  the marker's dialect, so the marker's syntax should stay stable.

## Consequences

- About 99% of the SQL is highlighted without editing any of the developer's files.
- Measured with `mise run ext:smoke` (all 1,819 `.vb` files, backup copies included), the
  code action appears on:
  - 324 statements that use other variables, nearly all of them real SQL builders
    (`tLanguage`, `tWhere`, `tSelect`, `tSearch`, …)
  - 920 `SQLCmd` statements with dialect-specific syntax (533 T-SQL, 379 MySQL, 8 with
    both)

  It appears only on the line where the cursor is.
- Tokenizing all real `.vb` files takes about 30% longer with the injection (34 s against
  26 s for 432,000 lines). VS Code tokenizes as you edit, so this isn't noticeable.
- SQL in a variable not named `SQLCmd` stays a plain string until it gets a marker.
- If VB 14 syntax were ever used, a comment after a continuation token would end the
  highlighted region one line early. Roslyn found no syntax newer than VB 10 in these sites.
