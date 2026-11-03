
Partial Class Checkout
 Inherits System.Web.UI.Page

 ' Exercises what the real sites taught the server: a <title runat="server"> outside a
 ' server <head> is a generic control, an ID that is a VB keyword is declared as [return],
 ' HTML server controls get HtmlControls types, and lower-case asp: tags still resolve.
 Private Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load
  PgTitle.InnerText = "Checkout"
  [return].Value = "https://" + Request.ServerVariables("HTTP_HOST") + "/Default.aspx"
  Total.InnerHtml = FormatCurrency(Val(Amount.Value), 2)
  TotalRow.Visible = Mailer.Notify("someone@example.com")
 End Sub

 Private Sub btnPay_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles btnPay.Click
  BodyTag.Attributes("class") = "Paid"
 End Sub
End Class
