Imports Microsoft.VisualBasic

Public Module TextHelpers
    Public Function Clean(ByVal input As String) As String
        If input Is Nothing Then Return String.Empty
        Return input.Trim()
    End Function
End Module
