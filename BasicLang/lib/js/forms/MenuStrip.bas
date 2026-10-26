' Portable control library - MenuStrip host (task 39).
Namespace System.Windows.Forms
    Public Class MenuStrip
        Inherits ToolStrip

        ' A field declared as MenuStrip is resolved before the compiler has the base
        ' ToolStrip's inherited members in hand, so restate the property here with the
        ' concrete collection type. The storage and behaviour stay on ToolStrip.
        Public ReadOnly Property Items As ToolStripItemCollection
            Get
                Return CType(MyBase.Items, ToolStripItemCollection)
            End Get
        End Property
    End Class
End Namespace
