# 0014. Follow and check site paths in VB strings

Date: 2026-11-03 · Status: Accepted

## Context

Ctrl+click and the missing-file and case warnings
([ADR 0012](0012-windows-and-reference-case.md)) work on paths in markup: `href=`, `src=`,
`ImageUrl=` and the like. The same kind of path in VB code did nothing, for example
`Response.Redirect("~/Buy.aspx")`. The language server has nothing to say inside a string
literal, and the extension's markup checks blanked out `<% %>` blocks. The project
director asked for VB paths to behave like markup paths, for a consistent feel. Visual
Studio doesn't do this, so it's beyond [ADR 0005](0005-feature-scope.md)'s list.

Measured on the developer's sites (read-only): 4,006 `Response.Redirect("…")` literals,
all but 6 in page code-behind. Most of the rest are `img.Src = "…"`, `ImageUrl = "…"` and
`Server.MapPath("…")`. His code joins strings with `+`. A literal followed by `+` is
either a complete path ending in `?Key=` or `"https://" + …`. No `<% %>` block in his
markup holds a path call.

## Decision

- **Which strings count.** The first argument of `Redirect`, `RedirectPermanent`,
  `MapPath`, `ResolveUrl`, `ResolveClientUrl`, `LoadControl`, `Server.Transfer` and
  `Server.Execute`, and a value assigned to a property with a markup URL attribute's
  name (`.Src =`, `.HRef =`, `.ImageUrl =`, `.NavigateUrl =`, …). Other strings are not
  checked. A path-shaped string could be anything, such as a label's text or a
  comparison.
- **Only complete paths.** A literal joined to more with `&` or `+` counts only when it
  already holds a `?` or `#`. So `"Buy.aspx?KeyID=" + id` counts, and `"~/Pics/" & name`
  doesn't. A literal ending in `/` or `\` is a folder, so it's skipped: it isn't a file to
  open, and on the sites it's usually an upload or virtual directory that isn't in the
  app on disk (`Server.MapPath("\Resources\") & name`).
- **Relative paths** resolve from the file. That's the page's folder for code-behind and
  for `<% %>` blocks in markup. `App_Code` code runs for whatever page was requested, so
  only `~/` and `/` paths count there.
- **Same resolution and warnings as markup.** The same resolver (`locateRef`) handles
  case-insensitive matching and the on-disk spelling. The same `missing-file` and
  `case-mismatch` warnings appear, under the same `aspxVb.diagnostics.missingFiles`
  setting. Open `.vb` files are now checked by the extension as well as by the language
  server.
- **Ctrl+click / F12** comes from an extension definition provider for all `vb`
  documents. It runs alongside the language server, which returns nothing on a string
  literal, so the two don't compete.

## Consequences

- On the real sites (`mise run ext:smoke`), 3,890 VB string paths resolve. The 125 that
  don't are real: pages in design and backup copies whose targets weren't copied, dead
  links, and `/…` paths in sub-applications. There IIS means the server root and not
  the app, the same limit markup `href="/…"` has.
- 535 new case warnings in 295 files, from 5 distinct references. 528 of them are
  `/default.aspx` against `Default.aspx` on disk.
- A path built in a variable first (`url = "~/x.aspx"` … `Response.Redirect(url)`) is not
  followed. Neither is a path whose call spans a `_` line continuation.
