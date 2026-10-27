Using System

' Portable control library - Form (task 29).
Namespace System.Windows.Forms
    Public Class Form
        Inherits Control

        Public Sub New()
            MyBase.New()
            Dim doc As Document = ::document
            If doc.body.id = "" Then doc.body.id = "vgs-form-root"
            Dim area As Element = doc.querySelector(".vgs-form")
            If area Is Nothing Then
                Throw New InvalidOperationException("Form could not find the page's .vgs-form area.")
            End If
            Me.VgsAttachRoot(area)
        End Sub

        Public Overrides Property Text As String
            Get
                Dim doc As Document = ::document
                Return doc.title
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Dim doc As Document = ::document
                Dim old As String = doc.title
                If old Is Nothing Then old = ""
                If old = value Then Return
                doc.title = value
                Me.OnTextChanged(EventArgs.Empty)
            End Set
        End Property

        Public Overrides Function VgsElementFor(member As String) As Element
            If member = "style" Then
                Dim body As Element = ::document.body
                Return body
            End If
            Return MyBase.VgsElementFor(member)
        End Function

        Public Property ClientSize As Size
            Get
                Dim area As Element = Me.VgsElementFor("ClientSize")
                Return New Size(area.clientWidth, area.clientHeight)
            End Get
            Set(value As Size)
                Dim area As Element = Me.VgsElementFor("ClientSize")
                area.style.width = CStr(value.Width) & "px"
                area.style.height = CStr(value.Height) & "px"
            End Set
        End Property
        ' WinForms' Close hides the form. The page and its element stay alive so a later
        ' Show can bring the same form back.
        Public Sub Close()
            Me.VgsElementFor("Close").hidden = True
        End Sub
    End Class
End Namespace
