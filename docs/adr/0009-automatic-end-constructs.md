# 0009. Automatic end constructs

Date: 2026-11-02 · Status: Accepted. Supersedes the "not included: automatic `End If` /
`End Sub` insertion" point of [0008](0008-vb-pretty-listing.md).

## Context

Visual Studio inserts the end of a block when you press Enter after its opening line
(`If x Then` → a blank line plus `End If`). ADR 0008 left this out because Roslyn doesn't
expose that editor feature. The project director asked for it anyway; the developer is
used to it.

## Decision

On Enter after a line that opens a block, the extension asks the server
(`aspxVb/endConstruct`) for the end, then inserts it below the new line at the opener's
formatted indentation. The cursor stays on the blank line inside the block.
- **When to insert:** the server tries the insertion and re-parses. The end is kept if it
  closes the new block and leaves one block fewer unclosed. Checking "is this block's end
  missing?" is not enough. A new `If` inside an existing `If` takes the outer block's
  `End If` in the parse, so the inner block looks closed and the outer one is open.
- **Splitting a single-line form:** Enter after `Then` in `If c Then Foo()`, or just
  before `Foo()`, leaves a multi-line `If` with no `End If`. The rest of the line becomes
  the body, indented one space past the opener, with `End If` below it. The cursor stays
  in front of the moved code, as in Visual Studio. The same re-parse check decides, so
  splitting a line inside a block that is already closed changes nothing.
- **Not an on-type formatting edit:** VS Code shrinks formatting edits to a minimal diff.
  Inserting `End If` plus a line break at the start of the next line is the same change
  as inserting a line break plus `End If` at the cursor. VS Code applies the second form
  and moves the cursor past `End If`. So the server returns the edit with the cursor's new
  position, and the extension applies both.
- Covered: `If … Then` (multi-line), `Sub` / `Function` / `Sub New`, `For` /
  `For Each` → `Next`, `While`, `Do` → `Loop`, `Select Case`, `Try` → `Catch ex As
  Exception` + `End Try` (as Visual Studio does), `With`, `Using`, `SyncLock`, `Class` /
  `Module` / `Structure` / `Interface`, `Enum`, `Namespace`. The keyword is written in
  canonical casing.
- Nothing is inserted when:
  - the block already has its end, and inserting another would leave a stray end
  - it's a single-line `If`
  - the statement on the finished line continues onto the next one (implicit line
    continuation, as in `If a AndAlso` + `b Then`)
- `Property` is excluded: on its own line, `Property X As String` is a complete
  auto-property (VB 10+), so an inserted `End Property` would break correct code.

## Consequences

- A new opener typed inside a block of the same kind gets its end right below, as in
  Visual Studio, even if he meant it to wrap the lines that follow.
- Measured on the developer's sites (read-only): Enter after each of 308,472 lines in
  1,819 error-free files inserted nothing, so there are no false insertions. Each check
  takes about 2 ms.
- `Get` / `Set` scaffolding for full properties is not generated.
