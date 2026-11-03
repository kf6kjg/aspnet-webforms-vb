# 0004. Compile the way ASP.NET 4.x compiles a Web Site

Date: 2026-11-02 · Status: Accepted

## Context

The [site scan](../scan-findings.md) found the same class name (`Inherits=`) in several
pages of four sites, mostly copies in `Backups/` folders and `*.bak.vb` files. If the
whole site were compiled as one unit, those would show up as errors that the developer
never sees on Windows. The project director's direction: work the way the version of
ASP.NET he is used to works. That version is .NET Framework 4.6/4.6.1, Web Site model,
with no `<system.codedom>` override.

## Decision

The language server copies ASP.NET 4.x Web Site compilation instead of compiling the
whole site at once:

- **App_Code** is its own compilation, the equivalent of `App_Code.dll`.
- **Each page or user control** (`.aspx` / `.ascx` / `.master`) gets its own
  compilation. It contains the file's `CodeFile` code-behind plus its generated partial
  class, and references:
  - the framework reference assemblies
  - `Bin/*.dll`
  - `web.config` `<assemblies>`
  - App_Code
  - the user controls the page names in `@Register` (or `@Reference`)

  A page doesn't see other pages' classes, which is what ASP.NET does.
- **`.vb` files that no markup file names and that aren't in App_Code** (backups,
  `*.bak.vb`) aren't compiled, the same as on IIS.
- **Language and options match the in-box .NET 4.x VB compiler**: VB 11
  (`LanguageVersion.VisualBasic11`) — the scan found nothing past VB 10 in his code —
  with `Option Strict` / `Explicit` from `web.config`, `Option Infer On`, and the root
  `web.config` default imports plus the site's `<namespaces>`.
- **The generated partial class** declares a `Protected WithEvents` field for every
  `runat="server"` control with an ID that isn't inside a template:
  - `asp:` tags map to `System.Web.UI.WebControls`
  - HTML tags map to `System.Web.UI.HtmlControls` (`td` → `HtmlTableCell`, and so on)
  - user controls map to their `Inherits` class
- **Assemblies that come from the GAC on the server** (today, `MySql.Data`) are taken
  from the matching NuGet package for now.

## Consequences

- No false errors from backup copies, and errors are reported against the page they
  belong to.
- More compilations to manage than one whole-site build, but each is small and Roslyn
  can share syntax trees and App_Code between them.
- The VB 11 ceiling is believed to be the in-box compiler's limit, not measured. To
  confirm, run `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe /?` on the server
  and read the version banner.
- Supersedes the "all .vb files in one compilation" wording in
  [ADR 0002](0002-roslyn-workspace-with-nuget-reference-assemblies.md). The rest of
  0002 stands.
