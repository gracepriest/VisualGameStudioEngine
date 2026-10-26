' Portable control library - ProgressBar (task 38).
'
' ProgressBar reports a value; it does not notify. The catalog's Minimum row is
' WinForms-only, so the portable class deliberately has no Minimum member and the
' web compiler refuses shared code that uses it (spec §5.4).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class ProgressBar
        Inherits Control

        Public Property Maximum As Integer
            Get
                Dim raw As String = Me.VgsElementFor("Maximum").getAttribute("max")
                If raw Is Nothing OrElse raw = "" Then Return 100
                Return Integer.Parse(raw)
            End Get
            Set(value As Integer)
                Me.VgsElementFor("Maximum").setAttribute("max", CStr(value))
            End Set
        End Property

        Public Property Value As Integer
            Get
                Dim raw As String = Me.VgsElementFor("Value").value
                If raw Is Nothing OrElse raw = "" Then Return 0
                Return Integer.Parse(raw)
            End Get
            Set(value As Integer)
                Me.VgsElementFor("Value").value = CStr(value)
            End Set
        End Property
    End Class
End Namespace
