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
  - VB code-behind: `codebehind-page`, `codebehind-control`, `pageload`, `handles`,
    `lang=` (the language marker)
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
on Windows; a reference whose case differs from disk still works, with a warning (below). `?query` and `#fragment` are ignored. External URLs (`http:`, `mailto:`,
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
| Warning | `CodeFile=`, `@Register Src=` or a URL names a file in a different case than on disk (a case-sensitive server wouldn't find it); the message spells it as on disk |

The errors are references ASP.NET refuses to compile. The warnings are files a browser
would get a 404 for; turn them off with the `aspxVb.diagnostics.missingFiles` setting.
Only open files are checked, so backup copies elsewhere in the site stay quiet. Problems
inside VB code (for example `Handles` naming a control that doesn't exist) come from the
language server in stage 3.

## What works (stage 3): completion and error marking in VB files

In code-behind (`.aspx.vb`, `.ascx.vb`, `.master.vb`) and `App_Code` files, a bundled
language server compiles the code the way ASP.NET 4.x does on the web server:

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
- **Pretty listing**, as in Visual Studio: pressing Enter formats the line you just
  finished. Keywords and names get their proper casing (`end if` → `End If`, `txtname` →
  `txtName`), spacing is normalized (`a=b` → `a = b`), and the line is indented by one
  space per block. Comments stay exactly where you put them, and line endings stay CRLF.
  **Format Document** / **Format Selection** do the same for more lines. The extension
  turns on format-on-type and a one-space indent for VB files, and the same indent for
  markup. Markup itself isn't reformatted.
- **Automatic block ends**, as in Visual Studio: Enter after `If x Then`, `Sub …`, `For …`,
  `Try`, `Select Case`, `With`, `Class …` and the like adds the matching `End If` /
  `End Sub` / `Next` / `Catch … End Try` below the cursor, unless the block already has
  one. This also works inside a block of the same kind, such as an `If` inside an `If`.
  Enter after `Then` in a one-line `If c Then Foo()` turns it into a block: `Foo()` on its
  own line, then `End If`. `Property` is left alone, because on its own it can be a complete auto-property.

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

## SQL in VB strings

The strings that build a query are highlighted as SQL: keywords, functions, `'literals'`,
`[names]` and `` `names` ``, `@parameters`, numbers and operators. The string keeps its
string color, and the SQL stands out inside it. This needs no language server.

- **Automatic:** any statement that starts with `SQLCmd =` (or `SQLCmd2 =`, …), through
  the last line of the statement, as in
  `SQLCmd = "Select Name " +` / `"From control " +` / `"Where ID=" + MyDB.SQLNo(x)`.
- **Other variables:** put a marker comment on the line just above the statement:

  ```vb
  ' lang=sql
  tWhere = " and Language=" + MyDB.SQLStr(tLang)
  ```

  The marker names the dialect: `sql` (generic), `tsql`, `mysql` or `pgsql`. A marker
  above a `SQLCmd` statement replaces the automatic generic dialect. The `lang=`
  snippet inserts one.
- **Suggestions:** when the cursor is on a statement whose strings look like SQL, the
  lightbulb offers **Mark as SQL** and adds the marker. On a `SQLCmd` statement (or a
  `lang=sql` one) that uses T-SQL or MySQL syntax, such as `Top 10`, `GetDate()`,
  `Limit 5` or `IfNull()`, it offers **Mark as SQL (T-SQL)** / **(MySQL)**. Log messages
  like `"Insert Page record: "` aren't suggested.

The dialects differ only in a few rules. For example, `#` starts a comment only with
`lang=mysql`, because in T-SQL it names a temporary table.

## HTML, JavaScript, CSS, XML and JSON in VB strings

The same goes for the page output built in strings: tags, attributes, entities and
comments in HTML, with the CSS in `style=""…""` and the JavaScript in `onclick=""…""`;
JavaScript keywords, calls and strings; CSS properties and values; XML tags; and JSON
keys and values.

- **Automatic:**
  - HTML: any statement that assigns to a name containing `html`, such as `tHtmlOut =`,
    `OutHtml =` or `PageTitle.InnerHtml =`. Strings inside `IIf(…)` and other calls in
    the statement count too.
  - JavaScript: the script argument of `X.Attributes.Add("onclick", …)` (any `on…`
    attribute) and of the menu helpers' `SBMenu.AddItem("L", …)`, and the right side
    of `X.Attributes("onload") = …`.
  - CSS: the same with `"style"`, as in `Row.Attributes("style") = "display: none;"`.
- **Other statements:** the marker line above the statement, as for SQL:
  `' lang=html`, `' lang=js` (or `javascript`), `' lang=css`, `' lang=xml` or
  `' lang=json`. A marker always wins, so `' lang=html` above `SQLCmd = …` makes that
  statement HTML.
- **Suggestions:** on an assignment, `Response.Write` or `Return` whose strings start
  with a tag (or close one), start with `<?xml` or `{"`, look like a script
  (`var x =`, `function`, `window.`), or are nothing but CSS declarations, the
  lightbulb offers **Mark as HTML** (or XML, JSON, JavaScript, CSS). Messages with a
  `<br>` inside them aren't suggested.
- In markup, `OnClientClick="…"` on `asp:` controls is highlighted as JavaScript, like
  `onclick="…"` on HTML tags.

Each string is colored on its own. In `"<td class=" + x + ">"` the tag and `>` are
colored, but the value in between can't be.

## Not yet

- Signature help (parameter info while typing a call).
- Formatting a line when you leave it with the arrow keys (only Enter triggers it).
- Completion and error marking inside `<% %>` blocks in markup.
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
  every navigation target and reports the ones that don't resolve. Last, it tokenizes
  every `.vb` file and reports any line where the highlighted string language (SQL,
  HTML, JavaScript, CSS, XML, JSON) and the code action's statement rules disagree.
- `mise run ext:package`: builds `extension/dist/*.vsix`

To install the build: `code --install-extension extension/dist/aspnet-webforms-vb-<version>.vsix`.
