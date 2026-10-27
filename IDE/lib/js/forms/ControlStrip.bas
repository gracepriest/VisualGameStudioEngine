' Portable control library - ToolStrip host (task 39).
Using System

Namespace System.Windows.Forms
    Public Class ToolStrip
        Inherits Control

        Private _items As ToolStripItemCollection

        Public Sub New()
            MyBase.New()
            _items = New ToolStripItemCollection(Me, True)
        End Sub

        Public ReadOnly Property Items As ToolStripItemCollection
            Get
                If _items Is Nothing Then _items = New ToolStripItemCollection(Me, True)
                Return _items
            End Get
        End Property

        Public Event ItemClicked(sender As Object, e As ToolStripItemClickedEventArgs)

        Protected Overridable Sub OnItemClicked(e As ToolStripItemClickedEventArgs)
            RaiseEvent ItemClicked(Me, e)
        End Sub

        Public Sub VgsRaiseItemClicked(item As ToolStripItem)
            Me.OnItemClicked(New ToolStripItemClickedEventArgs(item))
        End Sub
    End Class
End Namespace
