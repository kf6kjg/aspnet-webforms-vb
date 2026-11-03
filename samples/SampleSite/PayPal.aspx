<%@ Page Language="VB" AutoEventWireup="false" CodeFile="PayPal.aspx.vb" Inherits="PayPal" %>
<!DOCTYPE html>
<html>
<head>
 <title id="PgTitle" runat="server">Checkout</title>
</head>
<body id="BodyTag" runat="server">
 <form action="https://www.paypal.com/cgi-bin/webscr" method="post">
  <input type="hidden" id="return" runat="server" />
  <input type="text" id="Amount" runat="server" />
  <table>
   <tr id="TotalRow" runat="server">
    <td id="Total" runat="server"></td>
   </tr>
  </table>
  <asp:button id="btnPay" text="Pay" runat="server" />
 </form>
</body>
</html>
