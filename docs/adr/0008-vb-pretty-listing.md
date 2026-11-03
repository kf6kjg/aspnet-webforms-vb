# 0008. VB pretty listing in the developer's style

Date: 2026-11-02 · Status: Accepted (the exclusion of automatic end constructs is superseded by [0009](0009-automatic-end-constructs.md))

## Context

Visual Studio's VB editor formats code as you type ("pretty listing"):
- keywords and identifiers are corrected to their canonical casing
- spacing is normalized
- each line is re-indented when you leave it

The project director has seen the developer rely on this without thinking about it; he
would miss it at once. That goes beyond [ADR 0005](0005-feature-scope.md)'s list, so this
records the decision. His rules, from the project director and his code:
- explicit one-space indentation
- some comments placed at a fixed column
- CRLF line endings, since the files come from Windows

Roslyn's public `CaseCorrector` is internal in the current version. The formatter takes
its options from `.editorconfig`.

## Decision

- **The language server formats VB documents:**
  - Format Document / Format Selection.
  - As you type: Enter formats the line just finished, which is when pretty listing acts
    in Visual Studio. The extension sets `editor.formatOnType` on for VB files.
- **The pipeline** (`server/AspxVb.Server/Features/Formatting.cs`):
  1. case correction, built on public APIs: keywords take Roslyn's canonical text;
     identifiers bound to a symbol take its declared name (`response.write` →
     `Response.Write`, `txtname` → `txtName`)
  2. Roslyn's formatter, with an in-memory site `.editorconfig`: `indent_size = 1`,
     `indent_style = space`, `end_of_line = crlf`
  3. comments never move: a whole-line comment's line is left as is, and the whitespace
     before a trailing comment is restored behind the formatted code
  4. one edit per changed line
- **Editor defaults for `.vb`, `.aspx`, `.ascx`, `.master`:** a one-space indent, spaces,
  and no indent guessing. VS Code's built-in VB indentation rules then place new lines
  correctly.
- **Accepted by measurement.** `mise run server:format-check` formats every compiled file
  of every real site, read-only. The bar is "next to nothing changes on code he wrote with
  pretty listing". Result: 787 of 437,356 lines (0.18%). These are old lines VS never
  re-listed (casing such as `InnerHTML` → `InnerHtml`, operator spacing, trailing
  whitespace) and a few files with off-by-one indentation regions. VS would make the same
  changes when those lines are edited.
- **Not included:** formatting `.aspx`/`.ascx` markup (whitespace there can change the
  page) and Visual Studio's automatic `End If` / `End Sub` insertion (a separate editor
  feature Roslyn doesn't expose).

## Consequences

- Editing a line in an old file can change that line's casing, spacing or indentation,
  as in Visual Studio. Untouched lines never change unless he runs Format Document.
- Leaving a line with the arrow keys doesn't format it; only Enter does (LSP has no
  "line left" event).
- If his Visual Studio settings turn out to differ in some respect, the measurement tool
  shows the effect before he does. His settings export is available if needed.
