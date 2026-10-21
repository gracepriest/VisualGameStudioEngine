' Portable control library - TextBox (task 31).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class TextBox
        Inherits Control

        Private _onInput As Action(Of DomEvent)

        Public Overrides Property Text As String
            Get
                Dim el As Element = Me.VgsElementFor("Text")
                Dim v As String = el.value
                If v Is Nothing Then v = ""
                Return v
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Dim el As Element = Me.VgsElementFor("Text")
                Dim old As String = el.value
                If old Is Nothing Then old = ""
                If old = value Then Return
                el.value = value
                Me.OnTextChanged(EventArgs.Empty)
            End Set
        End Property

        Public Property ReadOnly As Boolean
            Get
                Return Me.VgsElementFor("ReadOnly").readOnly
            End Get
            Set(value As Boolean)
                Me.VgsElementFor("ReadOnly").readOnly = value
            End Set
        End Property

        Public Property MaxLength As Integer
            Get
                Dim el As Element = Me.VgsElementFor("MaxLength")
                Dim s As String = el.getAttribute("maxlength")
                If s Is Nothing OrElse s = "" Then Return 32767
                Return el.maxLength
            End Get
            Set(value As Integer)
                If value < 0 Then Throw New ArgumentOutOfRangeException("MaxLength")
                Me.VgsElementFor("MaxLength").maxLength = value
            End Set
        End Property

        Public Property PlaceholderText As String
            Get
                Dim el As Element = Me.VgsElementFor("PlaceholderText")
                Dim s As String = el.getAttribute("placeholder")
                If s Is Nothing Then s = ""
                Return s
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Me.VgsElementFor("PlaceholderText").setAttribute("placeholder", value)
            End Set
        End Property

        Public Property PasswordChar As Char
            Get
                Dim el As Element = Me.VgsElementFor("PasswordChar")
                Dim s As String = el.getAttribute("data-vgs-passwordchar")
                If s Is Nothing OrElse s = "" Then Return ChrW(0)
                Return ChrW(AscW(s))
            End Get
            Set(value As Char)
                Dim el As Element = Me.VgsElementFor("PasswordChar")
                If value = ChrW(0) Then
                    el.removeAttribute("data-vgs-passwordchar")
                    If Not (el.tagName = "TEXTAREA") Then
                        el.type = "text"
                    End If
                Else
                    el.setAttribute("data-vgs-passwordchar", CStr(value))
                    If Not (el.tagName = "TEXTAREA") Then
                        el.type = "password"
                    End If
                End If
            End Set
        End Property

        Public Property Multiline As Boolean
            Get
                Return Me.VgsElementFor("Multiline").tagName = "TEXTAREA"
            End Get
            Set(value As Boolean)
                Dim el As Element = Me.VgsElementFor("Multiline")
                If (el.tagName = "TEXTAREA") = value Then Return
                Dim doc As Document = ::document
                Dim newEl As Element
                If value Then
                    newEl = doc.createElement("textarea")
                Else
                    newEl = doc.createElement("input")
                End If
                ' Copy every attribute, including arbitrary ARIA/data and future layout metadata.
                For Each attribute As String In el.getAttributeNames()
                    newEl.setAttribute(attribute, el.getAttribute(attribute))
                Next
                newEl.id = el.id
                newEl.className = el.className
                newEl.value = el.value
                newEl.disabled = el.disabled
                newEl.hidden = el.hidden
                newEl.readOnly = el.readOnly
                Dim pc As String = el.getAttribute("data-vgs-passwordchar")
                If value Then
                    newEl.removeAttribute("type")
                ElseIf pc IsNot Nothing AndAlso pc <> "" Then
                    newEl.type = "password"
                Else
                    newEl.type = "text"
                End If
                Me.VgsReplaceElement(newEl)
            End Set
        End Property

        Public Property TextAlign As HorizontalAlignment
            Get
                Dim w As Window = ::window
                Dim s As String = w.getComputedStyle(Me.VgsElementFor("TextAlign")).textAlign
                If s Is Nothing Then s = ""
                If s = "" Then s = Me.VgsStyleValue("text-align")
                If s = "center" Then Return HorizontalAlignment.Center
                If s = "right" Then Return HorizontalAlignment.Right
                Return HorizontalAlignment.Left
            End Get
            Set(value As HorizontalAlignment)
                Dim align As String = "left"
                If value = HorizontalAlignment.Center Then
                    align = "center"
                ElseIf value = HorizontalAlignment.Right Then
                    align = "right"
                End If
                Me.VgsSetStyle("text-align", align)
            End Set
        End Property

        Public Sub Clear()
            Me.Text = ""
        End Sub

        Public Sub AppendText(s As String)
            If s Is Nothing Then s = ""
            Me.Text = Me.Text & s
        End Sub

        Public Sub SelectAll()
            Me.VgsElementFor("SelectAll").select()
        End Sub

        ' M25: the listener precedes its AddressOf in the attach hook.
        Private Sub VgsOnDomInput(e As DomEvent)
            Me.OnTextChanged(EventArgs.Empty)
        End Sub

        Public Overrides Sub VgsAttachExtraListeners()
            _onInput = AddressOf Me.VgsOnDomInput
            Me.VgsElement().addEventListener("input", _onInput)
        End Sub

        Public Overrides Sub VgsDetachExtraListeners()
            Me.VgsElement().removeEventListener("input", _onInput)
            _onInput = Nothing
        End Sub
    End Class
End Namespace
