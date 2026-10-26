' Portable control library - ToolStripMenuItem (task 39).
Namespace System.Windows.Forms
    Public Class ToolStripMenuItem
        Inherits ToolStripItem

        Private _dropDownItems As ToolStripItemCollection

        Public Sub New()
            MyBase.New()
            _dropDownItems = New ToolStripItemCollection(Me, True)
        End Sub

        Public ReadOnly Property DropDownItems As ToolStripItemCollection
            Get
                If _dropDownItems Is Nothing Then _dropDownItems = New ToolStripItemCollection(Me, True)
                Return _dropDownItems
            End Get
        End Property
    End Class
End Namespace
