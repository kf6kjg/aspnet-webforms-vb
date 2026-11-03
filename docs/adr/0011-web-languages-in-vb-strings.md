# 0011. HTML, JavaScript, CSS, XML and JSON in VB strings

Date: 2026-11-03 · Status: Accepted

## Context

After SQL ([ADR 0010](0010-sql-highlighting-in-vb-strings.md)), the project director asked
for the other languages the developer writes in VB strings. This goes beyond
[ADR 0005](0005-feature-scope.md)'s list, so this records the decision.

Measured with Roslyn on the developer's sites (read-only, 1,560 distinct `.vb` files),
counting each statement whose strings have text:
- **HTML is the big one:** about 4,700 statements. They are spread over many variable
  names, but two kinds of target are reliable:
  - **A variable name containing `html`** (`tHtmlOut`, `OutHtml`, `tHTML`, `HTMLText`,
    `DivHTML`, the typo `tHMTLOut`, …): 2,096 statements. 93% visibly hold HTML, and the
    rest are plain text that goes into the same HTML. All start a line with plain `=`.
  - **`X.InnerHtml =`:** 1,382 statements, HTML by definition, though many are plain
    text.
  - **Neither:** `Response.Write` (282 statements, 59% HTML; the rest are XML responses
    and bare values) and the name `tOut` (55% HTML).
- **JavaScript is mostly API arguments:**
  - `X.Attributes.Add("on…", "…")`: 1,260
  - `X.Attributes("on…") = "…"`: 517
  - the menu helpers' `SBMenu` / `TabNav` / `TreeView.AddItem("L", "<script>", title)`:
    924. Their source documents an `"L"` entry as a JavaScript link.
  - plus `on…=` attributes inside HTML strings
- **CSS:** `Attributes("style")` / `Attributes.Add("style", …)` (290), plus `style=`
  inside HTML strings.
- **XML:** 61 statements, the XML-RPC messages to the grid server.
- **JSON:** 7 statements in 3 files.
- **Too rare to be worth it:** one `Regex` call, 32 XPath strings, a few format strings.
- **Markup** already gets JavaScript and CSS from the HTML grammar, except
  `OnClientClick=` on `asp:` tags (10 uses), which isn't in its list of event attributes.

## Decision

- **A second TextMate injection grammar for VB files**
  (`syntaxes/vb-web.injection.tmLanguage.json`), built like ADR 0010's. It has no
  language-server work, and each language has its own small rule set that stays inside
  each string fragment.
- **Without a marker** (hardcoded conventions, per
  [ADR 0001](0001-hardcode-one-developers-conventions.md)):
  - HTML: a statement that starts a line by assigning (`=`, `&=`, `+=`) to a name whose
    last part contains `html` or `hmtl`, which includes `X.InnerHtml`.
  - JavaScript:
    - the second argument of `X.Attributes.Add("on…", …)` and of `X.AddItem("L", …)`;
      the `AddItem` title argument stays a plain VB string
    - the right side of `X.Attributes("on…") = …` (or `.Attributes.Item(…)`)
  - CSS: the same two `Attributes` forms with `"style"`.
- **Markers:** ADR 0010's `' lang=<language>` comment line takes the new words `html`,
  `js` (alias `javascript`), `css`, `xml` and `json`. A marker of any language claims the
  statement below it, so `' lang=html` above `SQLCmd = …` makes it HTML, not SQL.
- **What is colored:**
  - HTML: tags, attribute names and values, entities, comments and doctypes. CSS in
    `style=""…""` and JavaScript in `on…=""…""` are colored within the fragment. A
    fragment that continues a tag from the previous fragment (` class=…`, `>`) is
    colored as part of the tag.
  - JavaScript: keywords, function calls, strings, numbers and comments.
  - CSS: property names and values, colors, numbers and units, `url()`.
  - XML: tags with namespaces, attributes, `<?xml ?>`, CDATA and comments.
  - JSON: keys, strings, numbers, `true` / `false` / `null` and punctuation.
  - Strings inside parentheses belong to the statement too (`IIf(x, "<b>", "")`,
    `.Replace("<br>", …)`), unlike SQL. A plain-text string such as `dr("Name")` gets no
    color either way.
- **The end of a statement:** ADR 0010's rule, and also **while a parenthesis is open**,
  as after `IIf(a And` at a line end. VB 10 can't end a statement inside parentheses,
  and the grammars' parenthesis rules already spanned lines. The code action's statement
  rules (`src/vbstatement.ts`, shared by SQL and the new languages) now count
  parentheses too. Without this, the code action could insert a marker inside such a
  statement.
- **The code action** offers "Mark as HTML" (or JavaScript, CSS, XML, JSON). It appears
  on an unmarked assignment, `Response.Write` or `Return` that neither grammar already
  highlights, when one of its fragments:
  - HTML: starts with a tag, a comment or a doctype, or closes a tag. A `<br>` inside a
    message doesn't qualify.
  - XML: starts with `<?xml`.
  - JSON: starts with `{"` or `[{`.
  - JavaScript: starts with `var x =`, `function (`, `document.`, `window.`, `opener.`
    or `alert(`.
  - CSS: is nothing but known CSS declarations.

  A statement with HTML isn't also offered JavaScript or CSS, since HTML carries those.
  When more than one language is offered, none is preferred. The language-marker snippet
  (prefix `lang=`) now lists every language.
- **`OnClientClick=` in markup** gets the HTML grammar's JavaScript treatment for `on…`
  attributes.
- **Performance:** each grammar has one line-start (`^`) rule that tests for every one
  of its triggers. The per-language rules hang off it with `\G`, because every `^` rule
  costs a regex search on every scan, and a `\G` rule almost nothing. The SQL grammar
  was restructured the same way. Its behavior didn't change.

## Consequences

- Measured with `mise run ext:smoke` (all 1,819 `.vb` files, backup copies included):
  - **Highlighted without a marker:** 5,721 HTML statements (16,274 lines), 2,887
    JavaScript and 290 CSS.
  - **SQL is unchanged:** 11,956 statements, and the same 324 + 920 code-action offers.
  - **Agreement:** the grammars and the statement rules agree on every line.
- **The code action appears on:**
  - 1,638 HTML statements, mostly `tOut`, `tMsg`, `tLetter`, `Display`, email bodies
    and `Session("QForm")`. A sample showed real HTML output, the weakest being messages
    like `"DB Error: " + … + "<br />"`.
  - 66 XML statements (the XML-RPC strings)
  - 22 JavaScript (`strScript`)
  - 5 JSON
  - 1 CSS
- **Tokenizing cost:** all real `.vb` files take 42 s with both injections against 24 s
  without. The restructuring roughly halved SQL's own cost. Before it, the two
  injections took 75 s.
- **Accepted inaccuracies:**
  - A tag split across fragments (`"<td class=" + x + ">"`) is colored per fragment:
    the value between gets no color.
  - A `>` that starts a text fragment is taken for the end of a tag.
- **Not done:** `Response.Write` and `tOut` without a marker, regular expressions,
  XPath, and `<script>` blocks inside HTML strings (rare).
