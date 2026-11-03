<!-- SYNTAX TEST "text.html.aspx" "page in the developer's style"
<%@ Page Language="VB" AutoEventWireup="false" CodeFile="Buy.aspx.vb" Inherits="Buy" %>
<!-- <--- meta.tag.directive.aspx punctuation.section.embedded.begin.aspx
<!--^^^^ keyword.control.directive.aspx
<!--     ^^^^^^^^ entity.other.attribute-name.aspx
<!--              ^^^^ string.quoted.double.aspx
<!--                                                                                 ^^ punctuation.section.embedded.end.aspx
<%@ Register TagPrefix="uc1" TagName="Header" Src="~/Header.ascx" %>
<!--^^^^^^^^ keyword.control.directive.aspx
<!--                                              ^^^^^^^^^^^^^^^ string.quoted.double.aspx
 <body id="BodyTag" runat="server">
<!-- <~~---- entity.name.tag.html
<!--                ^^^^^ entity.other.attribute-name.html
   <uc1:Header id="Header1" runat="server"></uc1:Header>
<!--^^^ meta.tag.server.aspx entity.name.tag.namespace.aspx
<!--   ^ punctuation.separator.namespace.aspx
<!--    ^^^^^^ entity.name.tag.aspx
<!--           ^^ entity.other.attribute-name.html
<!--              ^^^^^^^^^ string.quoted.double.html
<!--                                         ^^^ entity.name.tag.namespace.aspx
<!--                                             ^^^^^^ entity.name.tag.aspx
    <asp:button ID="btnSave" runat="server" Text="Save" />
<!-- ^^^ entity.name.tag.namespace.aspx
<!--     ^^^^^^ entity.name.tag.aspx
<!--                                                    ^^ punctuation.definition.tag.end.html
     <td id="SidebarMenu" runat="server">
<!--  ^^ entity.name.tag.html
<% If IsPostBack Then %>
<!-- <-- meta.embedded.block.aspx punctuation.section.embedded.begin.aspx
<!-- <~~~-- source.vb.embedded.aspx keyword.control.asp
<!--                  ^^ punctuation.section.embedded.end.aspx
<p>Posted at <%= DateTime.Now.ToString("t") %></p>
<!--         ^^^ punctuation.section.embedded.begin.aspx
<!--                                   ^^^ source.vb.embedded.aspx string.quoted.double.asp
<!--                                        ^^ punctuation.section.embedded.end.aspx
<!--                                            ^ entity.name.tag.html
<% End If %>
<!-- <~~~------ source.vb.embedded.aspx keyword.control.asp
<%-- server comment <%= not code %> --%>
<!-- <---------------------------------------- comment.block.aspx - meta.embedded.block.aspx
<!-- html comment: <%= StillRuns %> -->
<!--               ^^^ comment.block.html punctuation.section.embedded.begin.aspx
