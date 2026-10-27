' Portable control library - ToolStripButton (task 39).
Namespace System.Windows.Forms
    Public Class ToolStripButton
        Inherits ToolStripItem

        Public Property Enabled As Boolean
            Get
                Return Not Me.VgsElementFor("Enabled").disabled
            End Get
            Set(value As Boolean)
                Me.VgsElementFor("Enabled").disabled = Not value
            End Set
        End Property

        Protected Overrides Function VgsClickAllowed() As Boolean
            Return Me.Enabled AndAlso Me.Visible
        End Function
    End Class
End Namespace
