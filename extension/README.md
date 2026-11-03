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

## Not yet

- Navigation (markup ↔ code-behind, `OnClick=` / `Handles` ↔ control, `@Register` →
  file). That's stage 2.
- IntelliSense, error marking, and go to definition. Those need the language server,
  stage 3.
- VB inside `<% %>` uses VS Code's built-in VB grammar, the same one `.vb` files get.
  It is a simple grammar, so highlighting is basic.
- Page and item templates aren't provided; use a file-template extension.

## Development

From the repo root: `mise run ext:install-deps`, then:

- `mise run ext:test`: grammar tests plus a manifest check
- `mise run ext:smoke`: tokenizes every real markup file under `$SITES_ROOT` and
  reports any `%>` the grammar loses
- `mise run ext:package`: builds `extension/dist/*.vsix`

To install the build: `code --install-extension extension/dist/aspnet-webforms-vb-0.1.0.vsix`.
