<!-- SYNTAX TEST "text.html.aspx" "%> always ends a code block, as in the ASP.NET parser"
   <table class="BodyTable"><%' %>
<!--                          ^ comment.line.apostrophe.asp
<!--                            ^^ punctuation.section.embedded.end.aspx
          <%'Type,Name,Grouped,Comment %>
<!--        ^^^^^^^^^^^^^^^^^^^^^^^^^^^ source.vb.embedded.aspx comment.line.apostrophe.asp
<!--                                   ^^ punctuation.section.embedded.end.aspx
         <%'<input id="Password2" runat="server" type="text" />%><br />
<!--        ^^^^^^ comment.line.apostrophe.asp
<!--                                                           ^^ punctuation.section.embedded.end.aspx
<!--                                                              ^^ entity.name.tag.html - source.vb.embedded.aspx
<% s = "50%>" %><b>x</b>
<!--   ^^^ string.quoted.double.asp
<!--      ^^ punctuation.section.embedded.end.aspx
<!--                  ^ entity.name.tag.html
<% s = "say ""hi""" %>
<!--        ^^ constant.character.escape.apostrophe.asp
<!--                ^^ punctuation.section.embedded.end.aspx
<%
<!-- <-- punctuation.section.embedded.begin.aspx
  ' a comment line inside a multi-line block
<!-- <~~----------- comment.line.apostrophe.asp
  Response.Write("x") %>
<!--             ^^^ string.quoted.double.asp
<!--                  ^^ punctuation.section.embedded.end.aspx
<p>after</p>
<!-- <~- entity.name.tag.html - source.vb.embedded.aspx
