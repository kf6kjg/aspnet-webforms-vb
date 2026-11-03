# What the real code bases use

Date: 2026-11-02 · Tool: [scripts/scan-sites.cs](../scripts/scan-sites.cs) (`mise run scan`)

The scanner reads the developer's site share read-only. Its full output
(`.scan/report.md`, `.scan/report.json`) names sites, paths, and DLLs, so it's
gitignored. This page keeps the aggregate findings only.

Scope: 24 site folders, not counting backup copies or three third-party sample folders
(`SCAN_EXCLUDE`). 20 of them have pages; the rest are `.asmx` web services or
near-empty folders. In total: 1,498 `.aspx`, 57 `.ascx`, 0 `.master`, and 1,723 `.vb`
files.

## Confirmed conventions

| Area | Finding |
| --- | --- |
| Project model | All Web Site. 0 `.designer.vb` files; the only `.vbproj` is a stale 2005-era Web Application file. |
| Code linkage | 100% `CodeFile=` + `Inherits=` (1,498 pages, 57 controls). No `CodeBehind=`, `Src=`, or inline `<script runat="server">`. |
| Event wiring | `AutoEventWireup="false"` on every page and on all but one control. About **5,500 `Handles` clauses**, against only 76 `On*=` markup attributes (55 `OnClick`). |
| User controls | 3,166 `@Register` directives, **all** in the `Src="~/..."` + `TagName` form, almost all with prefix `uc1`. A Header/Footer/SysMenu trio appears on nearly every page. |
| Master pages | None. |
| Framework | `targetFramework` 4.6.1 (most sites), 4.6 (4 sites), 4.0 (1 site). |
| Compiler | No `<system.codedom>` section anywhere, so IIS uses the in-box .NET 4.x VB compiler. Roslyn finds **no syntax newer than VB 10** in any `.vb` file. |
| Options | `strict="false" explicit="true"` in `web.config`; one file has an `Option` statement. |
| Namespaces | `web.config` adds `System.Data`, `Microsoft.VisualBasic`, `System.Drawing`, and often `MySql.Data.MySqlClient` to the ASP.NET defaults. |
| References | `web.config <assemblies>` lists Framework assemblies plus **`MySql.Data` 6.10.6.0 or 6.3.4.0, which isn't in any `Bin/`**, so it's presumably in the server's GAC. Only 5 sites have `Bin/` DLLs (iTextSharp, Newtonsoft, a few portable-profile libraries); none target `netstandard`. |
| Server controls | About 22,000 with an `ID`. **About 62% are HTML tags with `runat="server"`** (`td`, `tr`, `span`, `form`, `input type=hidden`, `div`, `textarea`, `select`, …); the rest are `asp:` and `uc1:` tags. |
| Templates | 1,330 `<ItemTemplate>`s (GridView `TemplateField`s), but **0 server controls inside templates**: template bodies are `<%# Container.DataItem(...) %>` expressions. |
| Code blocks | `<%# %>` 2,332 · `<%= %>` 244 · `<% %>` 7 · no `<%: %>`, `<%$ %>`, or `<%-- --%>`. |
| Casing | Tag names vary in case (`asp:Button` / `asp:button`, `ItemTemplate` / `itemtemplate`); `Language="VB"` and `"vb"`. |
| `.sln` files | They name sites by absolute mapped-drive path (`W:\WebApps\...`), not by relative path. |

## Implications for each stage

These are findings, not decisions. Each one changes what the next stage needs to do.

**Grammar and snippets**
- `.aspx` and `.ascx` matter; `.master` is unused today, and since it shares the same
  grammar there's no reason to drop it.
- Tag and attribute matching must be case-insensitive.
- `<%# %>` data-binding expressions are the main embedded-VB case; `<%= %>` comes second.

**Navigation**
- The markup ↔ code-behind jump via `CodeFile=` covers every page and control.
- `@Register Src="~/..."` resolves against the site root, the folder that holds `web.config`.
- `OnClick="Foo"` → `Sub Foo` is rare (55 uses). The common case is the reverse
  direction, `Handles btnSave.Click` ↔ `<asp:Button ID="btnSave">`, but that isn't in the
  agreed feature list. **Open question for the project director.**

**Language server**
- Reference assemblies: use the `Microsoft.NETFramework.ReferenceAssemblies.net461`
  package (and `net46`), not `net48` as the spike did.
- Language version: VB 10 or VB 11 (the in-box .NET 4.x compiler is believed to stop at
  VB 11) would match what IIS accepts, and costs nothing, since no newer syntax is in use.
- **The virtual partials must map HTML tags to `System.Web.UI.HtmlControls` types**
  (`td` → `HtmlTableCell`, `input type=hidden` → `HtmlInputHidden`, …), not just `asp:`
  tags. The spike only covered `asp:` tags.
- Skipping controls inside templates costs nothing for this code.
- `MySql.Data` has to come from somewhere: the `MySql.Data` 6.10.6 NuGet package, or the
  DLL copied from the Windows server's GAC.
- **Compiling the whole site as one unit will produce false errors.** Four sites have
  the same class name (`Inherits=`) in several pages, mostly copies in `Backups/`
  subfolders or files like `*.bak.vb`. ASP.NET compiles per folder and per page, so it
  never sees the clash. Two options: skip backup-looking files, or compile `App_Code`
  once and each page separately against it, which is how ASP.NET actually works.
  This decision belongs in an ADR when the server stage starts.
- Three `.vb` files contain real syntax errors (unfinished statements in pages that are
  probably dead). The server must tolerate broken files and report their errors.
- Find the site root from the folder (`web.config` + `App_Code`/pages) rather than from
  the `.sln`, because the `.sln` paths are Windows drive paths.

## Scanner limitations

- Tags and directives are matched with regexes, not parsed. Counts are close but not exact.
- Site discovery: a folder counts as a site if a `.sln` points at it, or if it holds a
  `web.config` next to `.as?x` files. A site whose top folder has neither would be missed.
- The backup heuristic is name-based (`Backup*`, `Old*`, `* - Copy`, `* - Original*`).
