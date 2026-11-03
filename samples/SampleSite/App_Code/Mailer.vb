Imports Microsoft.VisualBasic

Public Module Mailer
    ' Uses the proxy ASP.NET generates from App_WebReferences/com/example/mail/MailService.wsdl.
    Public Function Notify(ByVal toAddress As String) As Boolean
        Dim svc As New com.example.mail.MailService()
        Dim queued As com.example.mail.Message() = svc.GetOutbox()
        Dim report As System.Data.DataSet = svc.GetReport()
        Return svc.SendMail(toAddress, "Sample", com.example.mail.Priority.High) AndAlso queued.Length >= 0 AndAlso report IsNot Nothing
    End Function
End Module
