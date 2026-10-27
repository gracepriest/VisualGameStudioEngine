' Portable control library - StatusStrip host (task 39).
Namespace System.Windows.Forms
    Public Class StatusStrip
        Inherits ToolStrip

        ' See MenuStrip.Items: the concrete type must be visible on the derived class
        ' for a field declared as StatusStrip.
        Public ReadOnly Property Items As ToolStripItemCollection
            Get
                Return CType(MyBase.Items, ToolStripItemCollection)
            End Get
        End Property
    End Class
End Namespace
