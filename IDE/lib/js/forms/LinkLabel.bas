' Portable control library — LinkLabel (task 37).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class LinkLabel
        Inherits Label

        Public Event LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)

        Protected Overridable Sub OnLinkClicked(e As LinkLabelLinkClickedEventArgs)
            RaiseEvent LinkClicked(Me, e)
        End Sub

        ' A page <a> navigates unless the click is cancelled. The control always cancels it,
        ' and a disabled link raises neither Click nor LinkClicked (spec §5.4).
        Public Overrides Sub VgsHandleDomClick(e As DomEvent)
            e.preventDefault()
            If Not Me.Enabled Then Return
            MyBase.VgsHandleDomClick(e)
            Me.OnLinkClicked(New LinkLabelLinkClickedEventArgs())
        End Sub
    End Class
End Namespace
