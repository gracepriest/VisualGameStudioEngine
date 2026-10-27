' Portable control library - NumericUpDown (task 38).
'
' The catalog keeps these four rows as Int (C9), because the designer's data is
' whole-number. The runtime library follows O14 and reads/writes Decimal exactly:
' Decimal.Parse preserves the element text's scale and Decimal.ToString writes it
' back invariantly.
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class NumericUpDown
        Inherits Control

        Private _onInput As Action(Of DomEvent)

        Public Property Minimum As Decimal
            Get
                Return Me.VgsDecimalAttribute("Minimum", "min", "0")
            End Get
            Set(value As Decimal)
                Me.VgsElementFor("Minimum").setAttribute("min", Me.VgsDecimalText(value))
            End Set
        End Property

        Public Property Maximum As Decimal
            Get
                Return Me.VgsDecimalAttribute("Maximum", "max", "100")
            End Get
            Set(value As Decimal)
                Me.VgsElementFor("Maximum").setAttribute("max", Me.VgsDecimalText(value))
            End Set
        End Property

        Public Property Value As Decimal
            Get
                Return Decimal.Parse(Me.VgsValueText())
            End Get
            Set(value As Decimal)
                Dim el As Element = Me.VgsElementFor("Value")
                Dim oldValue As Decimal = Decimal.Parse(Me.VgsValueText())
                el.value = Me.VgsDecimalText(value)
                If oldValue <> value Then Me.OnValueChanged(EventArgs.Empty)
            End Set
        End Property

        Public Property Increment As Decimal
            Get
                Return Me.VgsDecimalAttribute("Increment", "step", "1")
            End Get
            Set(value As Decimal)
                Me.VgsElementFor("Increment").setAttribute("step", Me.VgsDecimalText(value))
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

        Public Property TextAlign As HorizontalAlignment
            Get
                Dim w As Window = ::window
                Dim s As String = w.getComputedStyle(Me.VgsElementFor("TextAlign")).textAlign
                If s Is Nothing Then s = ""
                If s = "" Then s = Me.VgsStyleValue("text-align")
                If s = "center" Then Return CType(2, HorizontalAlignment)
                If s = "right" Then Return CType(1, HorizontalAlignment)
                Return CType(0, HorizontalAlignment)
            End Get
            Set(value As HorizontalAlignment)
                Dim align As String = "left"
                If value = CType(2, HorizontalAlignment) Then
                    align = "center"
                ElseIf value = CType(1, HorizontalAlignment) Then
                    align = "right"
                End If
                Me.VgsSetStyle("text-align", align)
            End Set
        End Property

        Public Event ValueChanged(sender As Object, e As EventArgs)

        Protected Overridable Sub OnValueChanged(e As EventArgs)
            RaiseEvent ValueChanged(Me, e)
        End Sub

        ' The DOM's `input` is per keystroke; WinForms commits ValueChanged when the
        ' edit is accepted. Spec §5.6 records this browser-forced divergence.
        Private Sub VgsOnDomInput(e As DomEvent)
            Me.OnValueChanged(EventArgs.Empty)
        End Sub

        Public Overrides Sub VgsAttachExtraListeners()
            _onInput = AddressOf Me.VgsOnDomInput
            Me.VgsElement().addEventListener("input", _onInput)
        End Sub

        Public Overrides Sub VgsDetachExtraListeners()
            Me.VgsElement().removeEventListener("input", _onInput)
            _onInput = Nothing
        End Sub

        Public Function VgsDecimalAttribute(member As String, attribute As String, fallback As String) As Decimal
            Dim raw As String = Me.VgsElementFor(member).getAttribute(attribute)
            If raw Is Nothing OrElse raw = "" Then raw = fallback
            Return Decimal.Parse(raw)
        End Function

        Public Function VgsValueText() As String
            Dim raw As String = Me.VgsElementFor("Value").value
            If raw Is Nothing OrElse raw = "" Then raw = "0"
            Return raw
        End Function

        Public Function VgsDecimalText(value As Decimal) As String
            Return value.ToString()
        End Function
    End Class
End Namespace
