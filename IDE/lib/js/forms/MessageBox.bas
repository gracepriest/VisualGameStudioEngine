' Portable control library - MessageBox (task 29).
'
' Browser dialogs have no title argument, so a caption prefixes the message and the
' title is the recorded divergence. The return mapping follows WinForms' buttons.
Namespace System.Windows.Forms
    Public Class MessageBox
        Public Shared Function Show(text As String, Optional caption As String = "", Optional buttons As MessageBoxButtons = CType(0, MessageBoxButtons)) As DialogResult
            Dim message As String = text
            If caption <> "" Then message = caption & vbCrLf & message

            Dim w As Window = ::window
            If buttons = CType(0, MessageBoxButtons) Then
                w.alert(message)
                Return CType(1, DialogResult)
            End If

            Dim answer As Boolean = w.confirm(message)
            If buttons = CType(4, MessageBoxButtons) Then
                If answer Then Return CType(6, DialogResult)
                Return CType(7, DialogResult)
            End If

            If answer Then Return CType(1, DialogResult)
            Return CType(2, DialogResult)
        End Function
    End Class
End Namespace
