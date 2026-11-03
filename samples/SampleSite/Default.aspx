<%@ Page Title="Home" Language="VB" MasterPageFile="~/Site.master" AutoEventWireup="false" CodeFile="Default.aspx.vb" Inherits="_Default" %>
<%@ Register TagPrefix="uc" TagName="Header" Src="~/Controls/Header.ascx" %>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
    <uc:Header ID="ucHeader" runat="server" />

    <asp:Label ID="lblName" runat="server" AssociatedControlID="txtName" Text="Name:" />
    <asp:TextBox ID="txtName" runat="server" />
    <asp:Button ID="btnSave" runat="server" Text="Save" OnClick="btnSave_Click" />
    <asp:Button ID="btnClear" runat="server" Text="Clear" />
    <asp:Label ID="lblResult" runat="server" />

    <% If IsPostBack Then %>
        <p>Posted back at <%= DateTime.Now.ToString("t") %></p>
    <% End If %>
</asp:Content>
