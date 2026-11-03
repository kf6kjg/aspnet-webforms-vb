Partial Class Controls_Header
 Inherits System.Web.UI.UserControl

 Public Property Title() As String
  Get
   Return litTitle.Text
  End Get
  Set(ByVal value As String)
   litTitle.Text = value
  End Set
 End Property
End Class
