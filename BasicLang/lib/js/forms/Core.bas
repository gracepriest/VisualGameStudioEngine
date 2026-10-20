' Portable control library - shared non-Control System.Windows.Forms shells (task 28).
' Task 29 adds the members the common style properties convert; later kind tasks extend them.
Namespace System.Windows.Forms
    Public Class ObjectCollection
    End Class

    Public Class Padding
        Public Property Left As Integer
        Public Property Top As Integer
        Public Property Right As Integer
        Public Property Bottom As Integer

        Public Sub New()
        End Sub

        Public Function VgsCss() As String
            Return Me.Top & "px " & Me.Right & "px " & Me.Bottom & "px " & Me.Left & "px"
        End Function
    End Class

    Public Class Cursor
        Public Property Name As String

        Public Sub New()
            Me.Name = "default"
        End Sub

        Public Shared Function VgsNamed(name As String) As Cursor
            Dim value As New Cursor()
            value.Name = name
            Return value
        End Function

        Public Function VgsCss() As String
            Return Me.Name
        End Function
    End Class

    Public Class Cursors
        Public Shared ReadOnly Property Default As Cursor
            Get
                Return Cursor.VgsNamed("default")
            End Get
        End Property

        Public Shared ReadOnly Property Arrow As Cursor
            Get
                Return Cursor.VgsNamed("default")
            End Get
        End Property

        Public Shared ReadOnly Property Hand As Cursor
            Get
                Return Cursor.VgsNamed("pointer")
            End Get
        End Property

        Public Shared ReadOnly Property Cross As Cursor
            Get
                Return Cursor.VgsNamed("crosshair")
            End Get
        End Property

        Public Shared ReadOnly Property Wait As Cursor
            Get
                Return Cursor.VgsNamed("wait")
            End Get
        End Property

        Public Shared ReadOnly Property IBeam As Cursor
            Get
                Return Cursor.VgsNamed("text")
            End Get
        End Property

        Public Shared ReadOnly Property SizeAll As Cursor
            Get
                Return Cursor.VgsNamed("move")
            End Get
        End Property

        Public Shared ReadOnly Property SizeNS As Cursor
            Get
                Return Cursor.VgsNamed("ns-resize")
            End Get
        End Property

        Public Shared ReadOnly Property SizeWE As Cursor
            Get
                Return Cursor.VgsNamed("ew-resize")
            End Get
        End Property
    End Class
End Namespace
