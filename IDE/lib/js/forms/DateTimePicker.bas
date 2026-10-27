' Portable control library - DateTimePicker (task 38).
'
' The page's date input exposes valueAsDate directly (spec §5.4). An empty date
' input has no DateTime value, so the getter can return Nothing; WinForms cannot
' represent that state and the divergence is recorded in the Task 38 preflight.
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class DateTimePicker
        Inherits Control

        Private _onChange As Action(Of DomEvent)

        Public Property Value As DateTime
            Get
                Return Me.VgsElementFor("Value").valueAsDate
            End Get
            Set(value As DateTime)
                Dim el As Element = Me.VgsElementFor("Value")
                Dim oldText As String = el.value
                If oldText Is Nothing Then oldText = ""
                el.valueAsDate = value
                Dim newText As String = el.value
                If newText Is Nothing Then newText = ""
                If oldText <> newText Then Me.OnValueChanged(EventArgs.Empty)
            End Set
        End Property

        Public Event ValueChanged(sender As Object, e As EventArgs)

        Protected Overridable Sub OnValueChanged(e As EventArgs)
            RaiseEvent ValueChanged(Me, e)
        End Sub

        Private Sub VgsOnDomChange(e As DomEvent)
            Me.OnValueChanged(EventArgs.Empty)
        End Sub

        Public Overrides Sub VgsAttachExtraListeners()
            _onChange = AddressOf Me.VgsOnDomChange
            Me.VgsElement().addEventListener("change", _onChange)
        End Sub

        Public Overrides Sub VgsDetachExtraListeners()
            Me.VgsElement().removeEventListener("change", _onChange)
            _onChange = Nothing
        End Sub
    End Class
End Namespace
