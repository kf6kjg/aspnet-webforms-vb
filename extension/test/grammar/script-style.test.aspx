<!-- SYNTAX TEST "text.html.aspx" "server code inside JS strings, CSS and single-quoted attributes"
<script type="text/javascript">
  var url = '<%= ResolveUrl("~/Logon.aspx") %>';
<!--         ^^^ source.js punctuation.section.embedded.begin.aspx
<!--                        ^^^^^^^^^^^^^^ source.vb.embedded.aspx string.quoted.double.asp
<!--                                        ^^ punctuation.section.embedded.end.aspx
<!--                                          ^^ source.js
  var n = "<%=Count%>"; if (n > 0) { go(); }
<!--       ^^^ punctuation.section.embedded.begin.aspx
<!--               ^^ punctuation.section.embedded.end.aspx
<!--                    ^^ source.js keyword.control.conditional.js
</script>
<!-- <~~------ entity.name.tag.html
<style>
  .w { width: <%= W %>px; }
<!--          ^^^ source.css punctuation.section.embedded.begin.aspx
<!--                ^^ punctuation.section.embedded.end.aspx
</style>
<!-- <~~----- entity.name.tag.html
<asp:Label ID="lbl" runat="server" Text='<%# Eval("Name") %>' />
<!--                                     ^^^ string.quoted.single.html punctuation.section.embedded.begin.aspx
<!--                                              ^^^^^^ source.vb.embedded.aspx string.quoted.double.asp
<!--                                                      ^^ punctuation.section.embedded.end.aspx
<!--                                                          ^^ punctuation.definition.tag.end.html
<div style="width:<%= W %>px" onclick="go(<%= Id %>)" class="x">
<!--              ^^^ punctuation.section.embedded.begin.aspx
<!--                                      ^^^ punctuation.section.embedded.begin.aspx
<!--                                             ^^ punctuation.section.embedded.end.aspx
<!--                                                  ^^^^^ entity.other.attribute-name.html
<a href="x.aspx" onclick="return confirm('Sure?');">ok</a>
<!--                             ^^^^^^^ source.js
