' Portable control library - RadioButton (task 31).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class RadioButton
        Inherits Control

        ' Registry only; checked/group values live on the inputs, never in shadow fields.
        Private Shared _radios As List(Of RadioButton)
        Private _onChange As Action(Of DomEvent)

        Public Sub New()
            MyBase.New()
            If _radios Is Nothing Then _radios = New List(Of RadioButton)()
        End Sub

        Public Property Checked As Boolean
            Get
                Return Me.VgsInputElement().checked
            End Get
            Set(value As Boolean)
                Dim el As Element = Me.VgsInputElement()
                If el.checked = value Then Return
                el.checked = value
                Me.VgsReconcileGroup()
            End Set
        End Property

        Public Property GroupName As String
            Get
                Dim s As String = Me.VgsInputElement("GroupName").getAttribute("name")
                If s Is Nothing Then s = ""
                Return s
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Me.VgsInputElement("GroupName").setAttribute("name", value)
                Me.VgsReconcileGroup()
            End Set
        End Property

        Public Event CheckedChanged(sender As Object, e As EventArgs)

        Protected Overridable Sub OnCheckedChanged(e As EventArgs)
            RaiseEvent CheckedChanged(Me, e)
        End Sub

        Public Function VgsSameGroup(other As RadioButton) As Boolean
            Dim a As String = Me.GroupName
            If a = "" Then Return False
            ' A removed container's children no longer belong to the live page's group.
            Dim root As Element = Me.VgsElement().closest(".vgs-form")
            If root Is Nothing OrElse root IsNot other.VgsElement().closest(".vgs-form") Then Return False
            Return a = other.GroupName
        End Function

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

        Public Sub VgsReconcileGroup()
            If Me.Checked Then
                For Each radio As RadioButton In _radios
                    If radio IsNot Me AndAlso Me.VgsSameGroup(radio) Then
                        ' The browser may ALREADY have unchecked this peer, without a change
                        ' event. Its DOM snapshot still tells us an event is owed.
                        radio.VgsInputElement().checked = False
                        radio.VgsNotifyCheckedChanged()
                    End If
                Next
            End If
            Me.VgsNotifyCheckedChanged()
        End Sub

        Public Overrides Sub VgsHandleDomClick(e As DomEvent)
            If e.target IsNot Me.VgsInputElement() Then Return
            Me.VgsReconcileGroup()
            Me.OnClick(EventArgs.Empty)
        End Sub

        Private Sub VgsOnDomChange(e As DomEvent)
            Me.VgsReconcileGroup()
        End Sub

        Public Overrides Sub VgsAttachExtraListeners()
            Dim input As Element = Me.VgsInputElement()
            Dim name As String = input.getAttribute("name")
            If name Is Nothing OrElse name = "" Then
                Dim container As Element = Me.VgsElement().parentElement
                If container IsNot Nothing Then input.setAttribute("name", container.id)
            End If
            If input.checked Then
                input.setAttribute("data-vgs-checked", "true")
            Else
                input.setAttribute("data-vgs-checked", "false")
            End If
            _radios.Add(Me)
            _onChange = AddressOf Me.VgsOnDomChange
            input.addEventListener("change", _onChange)
        End Sub

        Public Overrides Sub VgsDetachExtraListeners()
            Dim kept As New List(Of RadioButton)()
            For Each radio As RadioButton In _radios
                If radio IsNot Me Then kept.Add(radio)
            Next
            _radios = kept
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
