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

## Not yet

- IntelliSense, error marking, and go to definition for VB symbols (`Trace.`, `DB.`,
  …). Those need the language server, stage 3.
- Ctrl+click on a control used in ordinary code (for example `txtName.Text`). Only the
  `Handles` clause is covered here; the language server will cover the rest.
- VB inside `<% %>` uses VS Code's built-in VB grammar, the same one `.vb` files get.
  It is a simple grammar, so highlighting is basic.
- Page and item templates aren't provided; use a file-template extension.

## Development

From the repo root: `mise run ext:install-deps`, then:

- `mise run ext:test`: navigation unit tests, grammar tests, and a manifest check
- `mise run ext:smoke`: runs over every real site under `$SITES_ROOT`, read-only.
  It tokenizes every markup file and reports any `%>` the grammar loses, then tries
  every navigation target and reports the ones that don't resolve.
- `mise run ext:package`: builds `extension/dist/*.vsix`

To install the build: `code --install-extension extension/dist/aspnet-webforms-vb-0.4.0.vsix`.
