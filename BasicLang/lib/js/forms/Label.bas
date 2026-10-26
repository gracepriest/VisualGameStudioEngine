' Portable control library - Label (task 31).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class Label
        Inherits Control

        Public Property TextAlign As ContentAlignment
            Get
                Return Me.VgsContentAlignment(ContentAlignment.TopLeft)
            End Get
            Set(value As ContentAlignment)
                Me.VgsSetContentAlignment(value)
            End Set
        End Property
    End Class
End Namespace
