' Portable control library — shared System.Windows.Forms types (task 28).
'
' These are class SHELLS. Task 29 attaches Control/Form to the DOM and owns their members;
' Tasks 31+ add the per-kind properties and methods. Keeping the hierarchy here makes shared
' user code compile before the behaviour lands.
Namespace System.Windows.Forms
    Public Class Control
    End Class

    Public Class Form
        Inherits Control
    End Class

    Public Class ControlCollection
    End Class

    Public Class ObjectCollection
    End Class

    Public Class Padding
    End Class

    Public Class Cursor
    End Class

    Public Class Cursors
    End Class

    Public Class MessageBox
    End Class

    Public Class MouseEventArgs
    End Class

    Public Class KeyEventArgs
    End Class

    Public Class KeyPressEventArgs
    End Class

    Public Class LinkLabelLinkClickedEventArgs
    End Class

    Public Class ToolStripItemClickedEventArgs
    End Class
End Namespace
