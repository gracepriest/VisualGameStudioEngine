' Portable control library — ListBox (task 37).
Using System
Using System.Drawing

Namespace System.Windows.Forms
    Public Class ListBox
        Inherits Control

        Private _items As ObjectCollection
        Private _onChange As Action(Of DomEvent)

        Public Sub New()
            MyBase.New()
            _items = New ObjectCollection(Me)
        End Sub

        Public ReadOnly Property Items As ObjectCollection
            Get
                If _items Is Nothing Then _items = New ObjectCollection(Me)
                Return _items
            End Get
        End Property

        Public Property SelectedIndex As Integer
            Get
                Return Me.Items.VgsSelectedIndex()
            End Get
            Set(value As Integer)
                Me.Items.VgsSetSelectedIndex(value)
            End Set
        End Property

        ' Web-only row (§5.4): the page's native multiple selection. WinForms' SelectionMode
        ' is a separate, WinForms-only vocabulary and is not approximated here.
        Public Property MultiSelect As Boolean
            Get
                Return Me.VgsElementFor("MultiSelect").multiple
            End Get
            Set(value As Boolean)
                Me.VgsElementFor("MultiSelect").multiple = value
            End Set
        End Property

        Public Event SelectedIndexChanged(sender As Object, e As EventArgs)

        Protected Overridable Sub OnSelectedIndexChanged(e As EventArgs)
            RaiseEvent SelectedIndexChanged(Me, e)
        End Sub

        ' M25: the listener precedes its AddressOf in the attach hook.
        Private Sub VgsOnDomChange(e As DomEvent)
            Me.Items.VgsDomSelectionChanged()
        End Sub

        Public Overrides Sub VgsAttachExtraListeners()
            Me.Items.VgsInitSelectionMarker()
            _onChange = AddressOf Me.VgsOnDomChange
            Me.Items.VgsSelectElement().addEventListener("change", _onChange)
        End Sub

        Public Overrides Sub VgsDetachExtraListeners()
            Me.Items.VgsSelectElement().removeEventListener("change", _onChange)
            _onChange = Nothing
        End Sub

        Public Overrides Sub VgsNotifySelectionChanged()
            Me.Items.VgsMarkSelection()
            Me.OnSelectedIndexChanged(EventArgs.Empty)
        End Sub
    End Class
End Namespace
