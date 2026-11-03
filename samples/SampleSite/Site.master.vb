Partial Class Site
    Inherits System.Web.UI.MasterPage

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load
        litFooter.Text = "Sample footer " & DateTime.Now.Year.ToString()
    End Sub
End Class
