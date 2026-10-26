' Portable control library - TrackBar (task 38).
'
' The catalog gives TrackBar two DOM events: Scroll is the range input's `input`
' and ValueChanged is its `change` (spec §5.6). A programmatic Value change raises
' ValueChanged only, as WinForms does.
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class TrackBar
        Inherits Control

        Private _onInput As Action(Of DomEvent)
        Private _onChange As Action(Of DomEvent)

        Public Property Minimum As Integer
            Get
                Return Me.VgsIntAttribute("Minimum", "min", 0)
            End Get
            Set(value As Integer)
                Me.VgsElementFor("Minimum").setAttribute("min", CStr(value))
            End Set
        End Property

        Public Property Maximum As Integer
            Get
                Return Me.VgsIntAttribute("Maximum", "max", 10)
            End Get
            Set(value As Integer)
                Me.VgsElementFor("Maximum").setAttribute("max", CStr(value))
            End Set
        End Property

        Public Property Value As Integer
            Get
                Return Integer.Parse(Me.VgsValueText())
            End Get
            Set(value As Integer)
                Dim el As Element = Me.VgsElementFor("Value")
                Dim oldValue As Integer = Integer.Parse(Me.VgsValueText())
                el.value = CStr(value)
                If oldValue <> value Then Me.OnValueChanged(EventArgs.Empty)
            End Set
        End Property

        Public Event Scroll(sender As Object, e As EventArgs)
        Public Event ValueChanged(sender As Object, e As EventArgs)

        Protected Overridable Sub OnScroll(e As EventArgs)
            RaiseEvent Scroll(Me, e)
        End Sub

        Protected Overridable Sub OnValueChanged(e As EventArgs)
            RaiseEvent ValueChanged(Me, e)
        End Sub

        Private Sub VgsOnDomInput(e As DomEvent)
            Me.OnScroll(EventArgs.Empty)
        End Sub

        Private Sub VgsOnDomChange(e As DomEvent)
            Me.OnValueChanged(EventArgs.Empty)
        End Sub

        Public Overrides Sub VgsAttachExtraListeners()
            _onInput = AddressOf Me.VgsOnDomInput
            _onChange = AddressOf Me.VgsOnDomChange
            Me.VgsElement().addEventListener("input", _onInput)
            Me.VgsElement().addEventListener("change", _onChange)
        End Sub

        Public Overrides Sub VgsDetachExtraListeners()
            Me.VgsElement().removeEventListener("input", _onInput)
            Me.VgsElement().removeEventListener("change", _onChange)
            _onInput = Nothing
            _onChange = Nothing
        End Sub

        Public Function VgsIntAttribute(member As String, attribute As String, fallback As Integer) As Integer
            Dim raw As String = Me.VgsElementFor(member).getAttribute(attribute)
            If raw Is Nothing OrElse raw = "" Then Return fallback
            Return Integer.Parse(raw)
        End Function

        Public Function VgsValueText() As String
            Dim raw As String = Me.VgsElementFor("Value").value
            If raw Is Nothing OrElse raw = "" Then raw = "0"
            Return raw
        End Function
    End Class
End Namespace
