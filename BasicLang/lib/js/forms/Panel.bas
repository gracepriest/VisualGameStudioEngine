' Portable control library - Panel (task 31).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class Panel
        Inherits Control

        Public Property BorderStyle As BorderStyle
            Get
                Dim w As Window = ::window
                Dim s As String = w.getComputedStyle(Me.VgsElementFor("BorderStyle")).borderStyle
                If s Is Nothing Then s = ""
                ' The property shadows the enum's name in member expressions; explicit
                ' casts use the enum's declared None/FixedSingle/Fixed3D values (0/1/2).
                If s = "solid" Then Return CType(1, BorderStyle)
                If s = "inset" OrElse s = "groove" Then Return CType(2, BorderStyle)
                Return CType(0, BorderStyle)
            End Get
            Set(value As BorderStyle)
                If value = CType(0, BorderStyle) Then
                    Me.VgsSetStyle("border", "none")
                ElseIf value = CType(1, BorderStyle) Then
                    Me.VgsSetStyle("border", "1px solid")
                ElseIf value = CType(2, BorderStyle) Then
                    Me.VgsSetStyle("border", "2px inset")
                End If
            End Set
        End Property

        Public Property AutoScroll As Boolean
            Get
                Dim w As Window = ::window
                Dim s As String = w.getComputedStyle(Me.VgsElementFor("AutoScroll")).overflow
                If s Is Nothing Then s = ""
                Return s = "auto" OrElse s = "scroll"
            End Get
            Set(value As Boolean)
                If value Then
                    Me.VgsSetStyle("overflow", "auto")
                Else
                    Me.VgsSetStyle("overflow", "")
                End If
            End Set
        End Property
    End Class
End Namespace
