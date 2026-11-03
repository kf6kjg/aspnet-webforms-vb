# ASP VB.NET Web Forms Site Editor Support on Linux

Designed specifically for a single dev's practice and needs. Feel free to fork and improve if you have an interest. I'll take PRs.

## Goal

Let one developer write and work on his legacy ASP.NET Web Forms (VB.NET) code bases in
VS Code on Linux the way he does in Visual Studio on Windows. The means is a VS Code
extension that gives language support for those code bases, which use the **Web Site**
project model:

- A `.sln` that points at a folder, with **no** `.vbproj` and **no** `.designer.vb` files.
- `.aspx`, `.ascx`, and `.master` markup, `.vb` code-behind, `App_Code/`, and `Bin/*.dll`.
- Markup links to code-behind through `CodeFile=` and `Inherits=` in the `@Page` /
  `@Control` / `@Master` directive.
- Control fields such as `txtName` aren't declared anywhere on disk, because ASP.NET
  generates them at runtime.

The extension hardcodes this developer's conventions instead of trying to be general
([ADR 0001](docs/adr/0001-hardcode-one-developers-conventions.md)). Other projects will
probably not work.

## Architecture

Three stages, each usable on its own. Features are limited to what he uses in Visual
Studio: IntelliSense, syntax highlighting, and error marking, plus go to definition
([ADR 0005](docs/adr/0005-feature-scope.md)). Page templates are left to a separate
file-template extension.

1. **Grammar and snippets** (`/extension`, TypeScript): a TextMate grammar for
   `.aspx` / `.ascx` / `.master` covering HTML, `<% %>` blocks, and `<asp:>` tags with
   embedded VB, plus snippets.
2. **Navigation layer** (`/extension`, regex / light parsing, no compiler):
   - jump between markup and code-behind
   - `OnClick="Foo"` → `Sub Foo`
   - `Handles btnSave.Click` ↔ the `btnSave` control in markup
   - `<%@ Register %>` → the user control's file
3. **Language server** (`/server`, a separate .NET process) that builds a synthetic,
   in-memory Roslyn VB workspace
   ([ADR 0002](docs/adr/0002-roslyn-workspace-with-nuget-reference-assemblies.md)), and
   compiles the way ASP.NET 4.x does
   ([ADR 0004](docs/adr/0004-compile-like-aspnet-4x-web-site.md)):
   - App_Code is one compilation. Each page or control is its own compilation, against
     App_Code and the user controls it registers.
   - references from `Bin/*.dll`, `web.config` `<assemblies>`, .NET Framework 4.6.1
     reference assemblies (including `System.Web`), and NuGet stand-ins for GAC-only
     assemblies (`MySql.Data`)
   - for each page, control, and master page, the `runat="server"` controls are read
     from the markup and declared in a virtual partial class (`asp:TextBox` →
     `WebControls.TextBox`, `<td runat="server">` → `HtmlControls.HtmlTableCell`;
     user controls are resolved through `@Register`)
   - VB 11 with the in-box compiler's options, so it accepts what IIS accepts
   - provides completion, diagnostics, and go to definition
   - nothing is written to disk
   - not handled: controls inside templates (the scan found none)

## Repo layout

| Path | Contents |
| --- | --- |
| [extension/](extension) | VS Code extension: grammar, snippets, navigation (stages 1-2) |
| [server/](server) | Language server (not started) |
| [spike/](spike) | Throwaway feasibility console app |
| [samples/](samples) | A tiny fake Web Site project to test against |
| [scripts/](scripts) | Repo scripts (.NET file-based apps), run through mise tasks |
| [docs/](docs) | ADRs and spike results |

## Status

- [x] Repo scaffold, ADRs, sample Web Site
- [x] **Feasibility spike: works on Linux.** Diagnostics and completions are correct
  for code-behind combined with hand-written virtual partials, against .NET Framework
  4.8 reference assemblies from NuGet. See [docs/spike-results.md](docs/spike-results.md)
  for what it did *not* prove.
- [x] Site scanner (`mise run scan`): inventory of the real code bases' syntax and
  conventions. Aggregate findings: [docs/scan-findings.md](docs/scan-findings.md).
- [x] Stage 1: TextMate grammar for `.aspx` / `.ascx` / `.master`. It has grammar tests,
  and all 1,630 real markup files tokenize without losing a `%>` (`mise run ext:smoke`).
- [x] Stage 1: snippets for markup and VB code-behind, in the developer's style
  (see [extension/README.md](extension/README.md))
- [x] Stage 2: markup ↔ code-behind jump (Ctrl+click `CodeFile=` / `Inherits=`, F7 /
  Shift+F7)
- [x] Stage 2: `OnClick="Foo"` → `Sub Foo`
- [x] Stage 2: `Handles ctl.Event` ↔ markup control
- [x] Stage 2: `<%@ Register %>` and `<uc1:X>` → user control file
- [x] Stage 2 addition (the project director's request, beyond ADR 0005's list):
  `href=` / `src=` / `ImageUrl=` and similar → the referenced site file
- [x] Stage 2 addition (the project director's request): reference checks in open markup
  files. Errors for broken `CodeFile` / `Inherits` / `@Register` / unregistered `uc1:`
  tags / missing `On*=` handlers; warnings for missing local files. Across all 1,630
  real markup files: 30 errors in 10 files, all real (backup and design copies plus one
  live page), and 394 missing-file warnings in 139 files.
- Stage 2 against the real sites (`mise run ext:smoke`): about 12,000 code navigation
  targets and 8,315 URL references resolve. The 394 URL references that don't are
  missing images, scripts, and pages, or pages that belong to other apps. The 60 that don't are real broken references in the sites (pages in
  design and backup copies whose user controls weren't copied with them, one page with
  unregistered `uc1:` tags, and one page with `Handles` on controls that don't exist)
  plus a C# sample.
- [ ] Stage 3: App_Code + per-page compilations
- [ ] Stage 3: references from `Bin/*.dll`, `web.config` `<assemblies>`, Framework reference assemblies, NuGet `MySql.Data`
- [ ] Stage 3: virtual partial classes generated from markup (WebControls, HtmlControls, `@Register` user controls)
- [ ] Stage 3: language server wired into the extension: completion, diagnostics, go to definition

## Known limitations and open questions

- Language features (diagnostics, completion) are planned for `.vb` code-behind only.
  VB inside `<% %>` blocks will get syntax highlighting, nothing more.
- The VB 11 language ceiling is believed to be the limit of the in-box .NET 4.x
  compiler, but hasn't been checked on his server (see ADR 0004).
- Highlighting of VB inside `<% %>` is only as good as VS Code's built-in VB grammar,
  which is simple. The language server will improve how `.vb` files work, but not this
  highlighting.
- `MySql.Data` comes from NuGet, not from the exact DLL in the server's GAC. Small API
  differences are possible.

## Development

Tools and tasks are managed by [mise](https://mise.jdx.dev/)
([ADR 0003](docs/adr/0003-mise-for-tools-and-tasks.md)):

```sh
mise trust && mise install   # once: .NET SDK and Node.js, at the versions in mise.toml
mise tasks                   # list tasks
mise run spike               # Roslyn feasibility spike against samples/
mise run scan                # read-only scan of the real sites -> .scan/ (gitignored)
mise run ext:install-deps    # once: extension npm dev dependencies
mise run ext:test            # grammar tests + manifest check
mise run ext:smoke           # grammar + navigation over every real site under $SITES_ROOT
mise run ext:package         # build extension/dist/*.vsix
```

`mise run scan` needs `SITES_ROOT` (and usually `SITES_DRIVE_MAP` and `SCAN_EXCLUDE`)
set in a gitignored `mise.local.toml`:

```toml
[env]
SITES_ROOT = "/path/to/mounted/share/WebApps"
SITES_DRIVE_MAP = "W:=/path/to/mounted/share"   # .sln files use W:\WebApps\... paths
SCAN_EXCLUDE = "ThirdPartyFolder;OtherSamples"   # root-relative, ';'-separated
```

## License

[MIT](LICENSE). The grammars copied under `extension/test/vendor-grammars/` are Microsoft's,
also MIT (see the README there).
