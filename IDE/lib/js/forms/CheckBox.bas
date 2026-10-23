' Portable control library - CheckBox (task 31).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class CheckBox
        Inherits Control

        Private _onChange As Action(Of DomEvent)

        Public Property Checked As Boolean
            Get
                Return Me.VgsInputElement().checked
            End Get
            Set(value As Boolean)
                Dim el As Element = Me.VgsInputElement()
                If el.checked = value Then Return
                el.checked = value
                Me.VgsNotifyCheckedChanged()
            End Set
        End Property

        Public Event CheckedChanged(sender As Object, e As EventArgs)

        Protected Overridable Sub OnCheckedChanged(e As EventArgs)
            RaiseEvent CheckedChanged(Me, e)
        End Sub

        Public Sub VgsNotifyCheckedChanged()
            Dim input As Element = Me.VgsInputElement()
            Dim old As Boolean = input.getAttribute("data-vgs-checked") = "true"
            If old = input.checked Then Return
            If input.checked Then
                input.setAttribute("data-vgs-checked", "true")
            Else
                input.setAttribute("data-vgs-checked", "false")
            End If
            Me.OnCheckedChanged(EventArgs.Empty)
        End Sub

        Public Overrides Sub VgsHandleDomClick(e As DomEvent)
            ' A caption click bubbles first, then the label forwards a second click to
            ' the input. Only that input click represents the CheckBox gesture.
            If e.target IsNot Me.VgsInputElement() Then Return
            Me.VgsNotifyCheckedChanged()
            Me.OnClick(EventArgs.Empty)
        End Sub

        Private Sub VgsOnDomChange(e As DomEvent)
            Me.VgsNotifyCheckedChanged()
        End Sub

        Public Overrides Sub VgsAttachExtraListeners()
            Dim input As Element = Me.VgsInputElement()
            If input.checked Then
                input.setAttribute("data-vgs-checked", "true")
            Else
                input.setAttribute("data-vgs-checked", "false")
            End If
            _onChange = AddressOf Me.VgsOnDomChange
            input.addEventListener("change", _onChange)
        End Sub

        Public Overrides Sub VgsDetachExtraListeners()
            If _onChange IsNot Nothing Then
                Me.VgsInputElement().removeEventListener("change", _onChange)
                _onChange = Nothing
            End If
        End Sub

        Public Overrides Property Text As String
            Get
                Return Me.VgsCaptionText("SPAN")
            End Get
            Set(value As String)
                Me.VgsSetCaptionText("SPAN", value)
            End Set
        End Property
    End Class
End Namespace
