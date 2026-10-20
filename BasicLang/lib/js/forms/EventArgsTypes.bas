Using System
Using System.Drawing

' Portable control library - event arguments (task 29).
'
' EventArgs.Empty is the singleton every parameterless event uses. The Forms argument
' classes carry only the values the library raises; richer WinForms members belong to the
' kind tasks that need them.
Namespace System
    Public Class EventArgs
        Private Shared _empty As EventArgs

        Public Shared ReadOnly Property Empty As EventArgs
            Get
                If _empty Is Nothing Then
                    _empty = New EventArgs()
                End If
                Return _empty
            End Get
        End Property
    End Class
End Namespace

Namespace System.Windows.Forms
    Public Class MouseEventArgs
        Inherits EventArgs

        Public Property Button As MouseButtons
        Public Property Clicks As Integer
        Public Property X As Integer
        Public Property Y As Integer
        Public Property Delta As Integer

        Public ReadOnly Property Location As Point
            Get
                Return New Point(Me.X, Me.Y)
            End Get
        End Property

        Public Sub New(button As MouseButtons, clicks As Integer, x As Integer, y As Integer, delta As Integer)
            Me.Button = button
            Me.Clicks = clicks
            Me.X = x
            Me.Y = y
            Me.Delta = delta
        End Sub
    End Class

    Public Class KeyEventArgs
        Inherits EventArgs

        Private _shift As Boolean
        Private _control As Boolean
        Private _alt As Boolean

        Public Property KeyCode As Keys
        Public Property Handled As Boolean
        Public Property SuppressKeyPress As Boolean

        Public ReadOnly Property Shift As Boolean
            Get
                Return _shift
            End Get
        End Property

        Public ReadOnly Property Control As Boolean
            Get
                Return _control
            End Get
        End Property

        Public ReadOnly Property Alt As Boolean
            Get
                Return _alt
            End Get
        End Property

        Public ReadOnly Property Modifiers As Keys
            Get
                Dim value As Integer = 0
                If _shift Then value = value + 65536
                If _control Then value = value + 131072
                If _alt Then value = value + 262144
                Return CType(value, Keys)
            End Get
        End Property

        Public ReadOnly Property KeyData As Keys
            Get
                Return CType(CInt(Me.KeyCode) + CInt(Me.Modifiers), Keys)
            End Get
        End Property

        Public Sub New(keyCode As Keys, shift As Boolean, control As Boolean, alt As Boolean)
            Me.KeyCode = keyCode
            _shift = shift
            _control = control
            _alt = alt
        End Sub
    End Class

    Public Class KeyPressEventArgs
        Inherits EventArgs

        Public Property KeyChar As Char
        Public Property Handled As Boolean

        Public Sub New(keyChar As Char)
            Me.KeyChar = keyChar
        End Sub
    End Class

    Public Class LinkLabelLinkClickedEventArgs
        Inherits EventArgs
    End Class

    Public Class ToolStripItemClickedEventArgs
        Inherits EventArgs
    End Class
End Namespace
