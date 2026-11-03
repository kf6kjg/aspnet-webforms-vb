<!-- SYNTAX TEST "text.html.aspx" "data binding inside quoted HTML attributes, templates and script"
<asp:TemplateField HeaderText="Contact Entries" HeaderStyle-CssClass="Title">
<!-- ^^^^^^^^^^^^^ entity.name.tag.aspx
<!--                                            ^^^^^^^^^^^^^^^^^^^^ entity.other.attribute-name.html
 <ItemTemplate>
<!-- <~~------------ entity.name.tag.html
  <span onclick="CallEdit(<%#Container.DataItem("ContactID")%>,'ContactForm.aspx');" class="NavLink">
<!--                      ^^^ punctuation.section.embedded.begin.aspx
<!--                         ^^^^^^^^^ source.vb.embedded.aspx
<!--                                            ^^^^^^^^^^^ source.vb.embedded.aspx string.quoted.double.asp
<!--                                                        ^^ punctuation.section.embedded.end.aspx
<!--                                                                                 ^^^^^ entity.other.attribute-name.html
<!--                                                                                       ^^^^^^^^^ string.quoted.double.html
   <%#Container.DataItem("Title").ToString().Trim()%>
<!-- <~~~--- punctuation.section.embedded.begin.aspx
<!--                                               ^^ punctuation.section.embedded.end.aspx
  </span><%#ShowActive(Container.DataItem("Active"))%>
<!--                                                ^^ punctuation.section.embedded.end.aspx
 </ItemTemplate>
<input type="hidden" id="Total" value="<%=Total%>" runat="server" />
<!--                                   ^^^ punctuation.section.embedded.begin.aspx
<!--                                           ^^ punctuation.section.embedded.end.aspx
<!--                                                     ^ string.quoted.double.html
<!--                                               ^^^^^ entity.other.attribute-name.html
<script type="text/javascript">
 var total = <%= Total %>;
<!--         ^^^ punctuation.section.embedded.begin.aspx
<!--                   ^^ punctuation.section.embedded.end.aspx
<!--                     ^ source.js
</script>
<!-- <~~------ entity.name.tag.html
<% Dim x As Integer = 5 Mod 2%>
<!--                         ^^ punctuation.section.embedded.end.aspx
<b>after</b>
<!-- <~- entity.name.tag.html - source.vb.embedded.aspx
