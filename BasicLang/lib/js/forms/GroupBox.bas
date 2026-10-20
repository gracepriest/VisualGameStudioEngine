' Portable control library - GroupBox (task 31).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class GroupBox
        Inherits Control

        Public Overrides Property Text As String
            Get
                Return Me.VgsCaptionText("LEGEND")
            End Get
            Set(value As String)
                Me.VgsSetCaptionText("LEGEND", value)
            End Set
        End Property
    End Class
End Namespace
