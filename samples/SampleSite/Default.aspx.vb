Partial Class _Default
 Inherits System.Web.UI.Page

 Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load
  If Not IsPostBack Then
   ucHeader.Title = "Home"
  End If
 End Sub

 ' Wired from markup: OnClick="btnSave_Click"
 Protected Sub btnSave_Click(ByVal sender As Object, ByVal e As System.EventArgs)
  lblResult.Text = TextHelpers.Clean(txtName.Text)
 End Sub

 ' Wired via Handles, which requires btnClear to be a WithEvents field.
 Protected Sub btnClear_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles btnClear.Click
  txtName.Text = String.Empty
  lblResult.Text = String.Empty
 End Sub
End Class
