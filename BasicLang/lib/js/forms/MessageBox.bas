' Portable control library - MessageBox (task 29).
'
' Browser dialogs have no title argument, so a caption prefixes the message and the
' title is the recorded divergence. The return mapping follows WinForms' buttons.
Namespace System.Windows.Forms
    Public Class MessageBox
        Public Shared Function Show(text As String, Optional caption As String = "", Optional buttons As MessageBoxButtons = MessageBoxButtons.OK) As DialogResult
            Dim message As String = text
            If caption <> "" Then message = caption & vbCrLf & message

            Dim w As Window = ::window
            If buttons = MessageBoxButtons.OK Then
                w.alert(message)
                Return DialogResult.OK
            End If

            Dim answer As Boolean = w.confirm(message)
            If buttons = MessageBoxButtons.YesNo Then
                If answer Then Return DialogResult.Yes
                Return DialogResult.No
            End If

            If answer Then Return DialogResult.OK
            Return DialogResult.Cancel
        End Function
    End Class
End Namespace
