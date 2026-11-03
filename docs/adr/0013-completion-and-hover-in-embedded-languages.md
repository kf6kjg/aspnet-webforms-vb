# 0013. Completion and hover in embedded languages

Date: 2026-11-03 · Status: Proposed

## Context

[ADR 0010](0010-sql-highlighting-in-vb-strings.md) and
[ADR 0011](0011-web-languages-in-vb-strings.md) color SQL, HTML, JavaScript, CSS, XML
and JSON inside VB strings. The project director asked whether completion and hover are
feasible there too, in markup and in code-behind. That goes beyond
[ADR 0005](0005-feature-scope.md)'s list, so it needs a decision. This ADR records the
feasibility assessment and a proposed plan. Nothing is implemented yet.

What exists today:
- **Markup has no HTML, CSS or JavaScript completion or hover at all.** VS Code's
  built-in HTML features are tied to the `html` language id, so `aspx-vb` files don't
  get them. The language server's features cover `.vb` files only. The TextMate grammar
  provides coloring and nothing more.
- **Code-behind:** Roslyn has nothing to say inside a string literal.
- **The embedded regions are already found in TypeScript.** `languageRegions`
  (`src/langmarker.ts`) and `sqlRegions` (`src/sqlmarker.ts`) return the statements the
  injection grammars color, and `mise run ext:smoke` checks that they agree with the
  grammars on every real line. `maskServerBlocks` (`src/markup.ts`) already blanks
  `<% %>` blocks in markup.
- **Volume** (from ADR 0011's measurements): about 5,700 HTML statements, 2,900
  JavaScript, 290 CSS, 12,000 SQL, 61 XML and 7 JSON in the sites' `.vb` files.
- VS Code ships HTML and CSS language services (with MDN data for hover), a JSON
  service, and TypeScript for JavaScript. It ships nothing for SQL or XML.

## Decision (proposed)

### Mechanism: request forwarding to same-layout virtual documents

VS Code's documented pattern for embedded languages:
- The extension registers its own completion and hover providers.
- For a position inside an embedded region, it builds a virtual document in that
  region's language and forwards the request to the built-in provider with
  `vscode.executeCompletionItemProvider` / `vscode.executeHoverProvider`.
- The virtual document is served by a `TextDocumentContentProvider` under a custom URI
  scheme.

The virtual document keeps **the same lines and columns** as the real file. Every
character outside the embedded text becomes a space, and newlines are kept. So:
- **No position mapping is needed.** Positions and ranges coming back apply to the real
  file unchanged.
- **Joined fragments come through as whitespace.** The quotes and the VB expression
  between them become spaces: `"<td class='" + x + "'>"` reads as `<td class='       '>`.
- **VB's doubled quote** `""` becomes ` "` (space, quote). That is still valid in an
  HTML attribute (`class= "a "`) and in a JavaScript string.
- Wrappers a language needs, such as a selector and `{` around bare CSS declarations, go
  in the blanked columns before the region.

### Markup

1. **Forward the whole page as HTML.** The virtual document is the page with server
   blocks and directives blanked. The built-in HTML service then provides:
   - tag, attribute and value completion and hover
   - CSS in `<style>` and `style=""`
   - JavaScript in `<script>` and `on…=""`
2. **`OnClientClick=`** is rewritten in the virtual document as `onclick` followed by six
   spaces. The columns stay the same, and the value gets JavaScript treatment.
3. `<asp:…>` tags reach the HTML service as unknown elements. It offers nothing for them,
   which is the same as today.

### Code-behind

Each statement found by `languageRegions` / `sqlRegions` is copied into a virtual
document for its language. Per language:

| Language | Provider | What it gives | Effort | Value |
|---|---|---|---|---|
| CSS (`Attributes("style")`) | built-in CSS service | property and value completion, MDN hover | low | high for its uses |
| HTML | built-in HTML service | tags, attributes, values, MDN hover; CSS and JS inside them | low–medium | high |
| JavaScript | see open question 2 | DOM APIs (`document.`, `window.`) | medium | modest |
| SQL | the extension's own | see below | varies | varies |
| XML, JSON | — | nothing useful without schemas | — | not done (too rare) |

- **HTML across statements:** each file's HTML statements share one virtual document.
  Markup built up over several statements, such as a `<table>` opened in one statement
  and rows added in a loop, reads as one loose HTML document. The HTML service tolerates
  that. Closing-tag completion may be wrong across statements.
- **JavaScript in VB strings is mostly short handlers** (`return confirm('…');`,
  `window.open(…)`) calling page functions defined in script files the virtual document
  can't see. Completion helps only with DOM and language members.
- **SQL has no off-the-shelf service.** Three levels, cheapest first:
  1. Keywords and functions per dialect (the marker's or `sql`), with short hover text,
     from a hand-written table. Easy, but adds little: the developer knows the keywords.
  2. **Table and column names harvested from the site's own SQL statements**:
     identifiers after `From`, `Join`, `Into` and `Update`, and the column lists of
     `Insert` and `Select`. There are 12,000 statements to learn from. This needs no
     database and follows the hardcoded-conventions approach of
     [ADR 0001](0001-hardcode-one-developers-conventions.md). **Recommended level.**
  3. A real schema read from the database in `web.config`. This needs credentials and
     network access and has to handle two dialects. Not done.

### Editor behavior

- **Completion inside strings doesn't pop up while typing**, because VS Code turns off
  `editor.quickSuggestions.strings` by default. One of these, to be decided in the
  spike:
  - set it on in `configurationDefaults` for `[vb]`
  - or register trigger characters (`<`, space, `:`, `.`) and leave quick suggestions
    alone
- **Roslyn's hover on a string literal**, if it shows one (such as the `String` type),
  would stack with the embedded hover. If so, language client middleware drops the
  server's hover inside embedded regions.

### Order

1. Markup forwarded to HTML: the largest gain, using services that already exist.
2. CSS and HTML in VB strings.
3. JavaScript in VB strings.
4. SQL names harvested from the site.

Each step ships separately, and each can stop the plan if the previous one disappoints.

### Open questions for a spike before acceptance

1. **Does the built-in HTML extension serve a virtual document under a custom scheme?**
   VS Code's request-forwarding sample does this with CSS. HTML is believed to work the
   same way, but this hasn't been checked.
2. **DOM-aware JavaScript.** On a non-file URI, the built-in TypeScript extension may
   give syntax-only results, with no DOM members. Fallbacks, in order:
   - put the JavaScript inside a `<script>` element of the virtual HTML document, so the
     HTML server's own JavaScript support (which includes the DOM library) answers
   - bundle TypeScript in the extension, which costs size and memory
3. **Cost per request.** The virtual document is rebuilt from a region scan on each
   request, or cached per document version. This needs measuring on the largest real
   `.vb` and markup files.

## Not included

- Attribute completion on `<asp:…>` tags (`CssClass`, `AutoPostBack`, …). That would
  come from the language server and the control types it already knows. It is a separate
  feature, not about embedded strings.
- Completion and hover for VB inside `<% %>` and `<%# Eval(…) %>`. That needs the
  server to compile markup VB into the page's compilation, which is a larger, separate
  piece of work.
- Diagnostics in embedded languages. Per ADR 0010, diagnostics in this extension mean
  real problems, and a fragment-built HTML or SQL statement is rarely valid on its own.
- Signature help, rename and other features beyond completion and hover.

## Consequences

If accepted:
- Markup gets the HTML, CSS and JavaScript help Visual Studio gives, apart from
  `<asp:…>` attributes and VB in server blocks.
- Code-behind gets completion and hover in the HTML, CSS and JavaScript strings that are
  already colored. SQL gets names its own site already uses.
- The new providers live in the extension. The language server is unchanged, apart from
  possible hover filtering in the language client middleware.
- The region rules gain a third consumer, after the grammars and the code actions. The
  smoke test's agreement check keeps all three aligned.
- Strings that are colored only after a `' lang=` marker get help only after it too,
  since the regions are the same.
