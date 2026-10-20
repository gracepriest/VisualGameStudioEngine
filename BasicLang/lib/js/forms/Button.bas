' Portable control library - Button (task 31).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class Button
        Inherits Control

        Public Property TextAlign As ContentAlignment
            Get
                Return Me.VgsContentAlignment(ContentAlignment.MiddleCenter)
            End Get
            Set(value As ContentAlignment)
                Me.VgsSetContentAlignment(value)
            End Set
        End Property

        Public Sub PerformClick()
            If Not Me.Enabled OrElse Not Me.Visible Then Return
            Me.OnClick(EventArgs.Empty)
        End Sub
    End Class
End Namespace
