# 0005. Feature scope: what he uses in Visual Studio

Date: 2026-11-02 · Status: Accepted

## Context

The goal is for the developer to work on his sites in VS Code on Linux the way he does
in Visual Studio on Windows. The project director listed the Visual Studio features he
actually uses: **IntelliSense, syntax highlighting, and error marking**. He also uses his
own Visual Studio item templates for new pages ("Built from WebSite Form template v. 1.0"
appears in his files). The scan showed that event wiring is about 99% `Handles` clauses,
and almost never `OnClick=` in markup.

## Decision

In scope, with the direction "make his life as easy as is reasonable to implement and
support":

1. Syntax highlighting for `.aspx` / `.ascx` / `.master`, plus snippets in his style.
2. Navigation without a compiler:
   - markup ↔ code-behind
   - `OnClick="Foo"` → `Sub Foo`
   - `Handles ctl.Event` ↔ the control in the markup
   - `<%@ Register %>` → the user control's file
3. Language server: IntelliSense (completion), error marking (diagnostics), and
   **go to definition**. He doesn't use go to definition today, but it comes almost for
   free once the workspace exists.

Out of scope: page/item templates. Another extension (a general file-template one) will
handle that better than this one could. Also out of scope: the designer view and
debugging, which need Windows.

## Consequences

- A feature not on this list needs a new ADR or a direct decision from the project
  director.
- Snippets cover small fragments, such as directives, controls, and handlers. Whole-page
  scaffolding is left to the template extension.
