' Portable control library - System.Drawing value and drawing types (task 28).
'
' Task 29 adds only the members the common Control properties convert. Slice 3 (task 40)
' owns the exhaustive WinForms value surface.
Namespace System.Drawing
    Public Class Color
        Public Property A As Integer
        Public Property R As Integer
        Public Property G As Integer
        Public Property B As Integer
        Public Property Name As String

        Public Sub New()
            Me.A = 0
            Me.Name = ""
        End Sub

        Public Shared Function VgsNamed(name As String) As Color
            Dim value As New Color()
            value.Name = name
            value.A = 255
            If name = "transparent" Then value.A = 0
            Return value
        End Function

        Public Shared ReadOnly Property Empty As Color
            Get
                Return New Color()
            End Get
        End Property

        Public Shared ReadOnly Property Transparent As Color
            Get
                Return VgsNamed("transparent")
            End Get
        End Property

        Public Shared ReadOnly Property Black As Color
            Get
                Return VgsNamed("black")
            End Get
        End Property

        Public Shared ReadOnly Property White As Color
            Get
                Return VgsNamed("white")
            End Get
        End Property

        Public Shared ReadOnly Property Gray As Color
            Get
                Return VgsNamed("gray")
            End Get
        End Property

        Public Shared ReadOnly Property Red As Color
            Get
                Return VgsNamed("red")
            End Get
        End Property

        Public Shared ReadOnly Property Green As Color
            Get
                Return VgsNamed("green")
            End Get
        End Property

        Public Shared ReadOnly Property Blue As Color
            Get
                Return VgsNamed("blue")
            End Get
        End Property

        Public Function VgsCss() As String
            If Me.Name <> "" Then Return Me.Name
            If Me.A = 0 Then Return "transparent"
            Return "rgb(" & Me.R & "," & Me.G & "," & Me.B & ")"
        End Function
    End Class

    Public Class Point
        Public Property X As Integer
        Public Property Y As Integer

        Public Sub New(x As Integer, y As Integer)
            Me.X = x
            Me.Y = y
        End Sub
    End Class

    Public Class Size
        Public Property Width As Integer
        Public Property Height As Integer

        Public Sub New(width As Integer, height As Integer)
            Me.Width = width
            Me.Height = height
        End Sub
    End Class

    Public Class Image
    End Class

    Public Class Font
        Public Property Name As String
        Public Property Size As Double
        Public Property Bold As Boolean
        Public Property Italic As Boolean
        Public Property Underline As Boolean
        Public Property Strikeout As Boolean

        Public Sub New()
            Me.Name = "Segoe UI"
            Me.Size = 9
        End Sub
    End Class

    Public Enum FontStyle
        Regular = 0
        Bold = 1
        Italic = 2
        Underline = 4
        Strikeout = 8
    End Enum
End Namespace
