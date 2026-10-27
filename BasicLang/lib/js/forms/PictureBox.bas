' Portable control library — PictureBox (task 37).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class PictureBox
        Inherits Control

        ' The document stores a path string; the page's <img src> is that path. The desktop
        ' initializer converts it separately with Image.FromFile (see the preflight).
        Public Property Image As String
            Get
                Dim raw As String = Me.VgsElementFor("Image").getAttribute("src")
                If raw Is Nothing Then raw = ""
                Return raw
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Dim el As Element = Me.VgsElementFor("Image")
                If value = "" Then
                    el.removeAttribute("src")
                Else
                    el.setAttribute("src", value)
                End If
            End Set
        End Property

        Public Property SizeMode As PictureBoxSizeMode
            Get
                Dim raw As String = Me.VgsElementFor("SizeMode").getAttribute("data-vgs-sizemode")
                If raw Is Nothing OrElse raw = "" Then Return CType(0, PictureBoxSizeMode)
                Return CType(Integer.Parse(raw), PictureBoxSizeMode)
            End Get
            Set(value As PictureBoxSizeMode)
                Me.VgsElementFor("SizeMode").setAttribute("data-vgs-sizemode", CStr(CInt(value)))
                Dim fit As String = "none"
                Dim position As String = "left top"
                If value = CType(1, PictureBoxSizeMode) Then
                    fit = "fill"
                    position = "center"
                ElseIf value = CType(4, PictureBoxSizeMode) Then
                    fit = "contain"
                    position = "center"
                ElseIf value = CType(3, PictureBoxSizeMode) Then
                    fit = "none"
                    position = "center"
                ElseIf value = CType(2, PictureBoxSizeMode) Then
                    fit = "none"
                    position = "left top"
                End If
                Me.VgsSetStyle("object-fit", fit)
                Me.VgsSetStyle("object-position", position)
            End Set
        End Property
    End Class
End Namespace
