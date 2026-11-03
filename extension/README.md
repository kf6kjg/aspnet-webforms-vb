# ASP.NET Web Forms (VB) for Web Site projects

Editor support for legacy ASP.NET Web Forms **Web Site** projects written in VB.NET
(`.aspx`, `.ascx`, `.master` markup with `CodeFile=` code-behind). It's built for one
developer's code bases and hardcodes their conventions.

## What works (stage 1)

- **Syntax highlighting** for `.aspx` / `.ascx` / `.master`:
  - HTML, including `<script>` and `<style>`
  - `<%@ ... %>` directives
  - `<%-- --%>` server comments
  - `asp:` / `uc1:` server-control tags
  - VB in `<% %>`, `<%= %>`, `<%# %>`, `<%: %>` blocks, including inside quoted HTML
    attributes such as `onclick="Edit(<%#Container.DataItem("ID")%>)"` and inside
    JavaScript/CSS
  - as in ASP.NET, `%>` always ends a block, so `<%' commented-out markup %>` works
- **Snippets** in the developer's style:
  - markup: `page`, `control`, `register`, `uc1`, `dataitem`, `expr`, `code`,
    `asp:TextBox`, `asp:Button`, `asp:CheckBox`, `asp:DropDownList`,
    `asp:RadioButtonList`, `asp:HiddenField`, `asp:FileUpload`, `asp:ImageButton`,
    `asp:GridView`, `asp:TemplateField`, `runat-td`, `runat-tr`, `runat-span`,
    `runat-div`, `runat-hidden`
  - VB code-behind: `codebehind-page`, `codebehind-control`, `pageload`, `handles`
- Toggle-comment uses `<%-- --%>`.

## What works (stage 2): navigation

Ctrl+click (or F12) on any of these:

| In | On | Goes to |
| --- | --- | --- |
| markup | `CodeFile="Buy.aspx.vb"` | the code-behind file |
| markup | `Inherits="Buy"` | `Partial Class Buy` in the code-behind |
| markup | `Src="~/Header.ascx"` in `@Register` | the user control file |
| markup | the tag name in `<uc1:Header …>` | the registered user control file |
| markup | `OnClick="btnSave_Click"` (any server `On*=`, not `OnClientClick`) | `Sub btnSave_Click` |
| markup | `ID="btnSave"` on a server control | every `Sub … Handles btnSave.*` (a peek list when there are several) |
| markup | `href=`, `src=`, `action=`, `background=`, `ImageUrl=`, `NavigateUrl=`, `PostBackUrl=` | the referenced file in the site |
| code-behind | `btnSave` in `Handles btnSave.Click` | that control's `ID` in the markup |

And, as in Visual Studio:

- **F7**: View Code (from markup to its code-behind)
- **Shift+F7**: View Markup (from code-behind to its markup)

Both are also on the editor's right-click menu.

How references resolve: `~/` and `/` are relative to the nearest folder that holds a
`web.config`; other paths are relative to the page. Names match case-insensitively, as
on Windows. `?query` and `#fragment` are ignored. External URLs (`http:`, `mailto:`,
`javascript:`, `//host`), `#anchors`, URLs built with `<%= %>`, and HTML built inside
JavaScript strings are never followed. Controls inside a `<%-- --%>` or `<% %>` block
don't count, because ASP.NET doesn't create them either.

## What works (stage 2): reference checks

Open `.aspx` / `.ascx` / `.master` files are checked as you type. Results show as
squiggles and in the Problems panel, with source "ASP.NET":

| Severity | Problem |
| --- | --- |
| Error | `CodeFile=` names a file that doesn't exist |
| Error | `Inherits=` names a class the code-behind doesn't declare |
| Error | `@Register Src=` names a user control that doesn't exist |
| Error | a `<uc1:X runat="server">` tag has no matching `@Register` in the file |
| Error | `OnClick="Foo"` (or another server `On*=`) names a `Sub` the code-behind lacks |
| Warning | `href=` / `src=` / `ImageUrl=` and similar name a local file that doesn't exist |

The errors are references ASP.NET refuses to compile. The warnings are files a browser
would get a 404 for; turn them off with the `aspxVb.diagnostics.missingFiles` setting.
Only open files are checked, so backup copies elsewhere in the site stay quiet. Problems
inside VB code (for example `Handles` naming a control that doesn't exist) come from the
language server in stage 3.

## What works (stage 3): IntelliSense in VB files

In code-behind (`.aspx.vb`, `.ascx.vb`, `.master.vb`) and `App_Code` files, a bundled
language server compiles the code the way ASP.NET 4.x does on his server:

- **Completion** with descriptions, including the controls declared in the markup
  (`txtName.`), App_Code classes, web-service proxies, `Bin` DLLs, and the framework.
- **Error marking** exactly as the VB compiler reports it (VB 11, `Option Strict` /
  `Explicit` from `web.config`), updated as you type. Editing the markup (adding or
  renaming a control) updates what the code-behind sees.
- **Go to definition:**
  - your own code: the declaration
  - a control: its `ID` in the markup
  - a web-service proxy: the `.wsdl`
  - framework and `Bin` symbols (`Trace`, `Session`, …): a generated read-only outline
- **Hover** shows the signature and documentation.

The server runs on .NET 10 or newer. You don't install it yourself: the
[.NET Install Tool](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.vscode-dotnet-runtime)
extension (installed alongside this one) uses an existing .NET if there is one, otherwise
it downloads a private copy the first time. If the server can't start, highlighting and
navigation keep working; its log is in the Output panel under "ASP.NET Web Forms (VB)
language server". The `aspxVb.server.path` setting can point at a different build.

### Missing assemblies

If a site's `web.config` lists an assembly the web server loads from its GAC (for
example `MySql.Data`), the editor doesn't have it, and code using it shows errors. The
extension tells you when that happens, with a **How to fix** button. In short: get the
DLL (from NuGet or the server's GAC), put it in a folder outside the site, and list that
folder in the `aspxVb.assemblyFolders` setting. The full steps are in
`MISSING-ASSEMBLIES.md`, which the **How to fix** button opens.

## Not yet

- Signature help (parameter info while typing a call).
- IntelliSense inside `<% %>` blocks in markup.
- `Global.asax` code-behind, `Profile` properties, and `@MasterType`.
- VB inside `<% %>` uses VS Code's built-in VB grammar, the same one `.vb` files get.
  It is a simple grammar, so highlighting is basic.
- Page and item templates aren't provided; use a file-template extension.

## Development

From the repo root: `mise run ext:install-deps`, then:

- `mise run ext:test`: navigation unit tests, grammar tests, and a manifest check
- `mise run server:test`: language server tests
- `mise run ext:smoke`: runs over every real site under `$SITES_ROOT`, read-only.
  It tokenizes every markup file and reports any `%>` the grammar loses, then tries
  every navigation target and reports the ones that don't resolve.
- `mise run ext:package`: builds `extension/dist/*.vsix`

To install the build: `code --install-extension extension/dist/aspnet-webforms-vb-0.5.0.vsix`.
