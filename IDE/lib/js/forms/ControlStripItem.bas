' Portable control library - ToolStripItem and its live collection (task 39).
Using System

Namespace System.Windows.Forms
    Public Class ToolStripItem
        Private _name As String
        Private _element As Element
        Private _strip As ToolStrip
        Private _rootStrip As ToolStrip
        Private _parentItem As ToolStripMenuItem
        Private _onClick As Action(Of DomEvent)
        Private _onDoubleClick As Action(Of DomEvent)
        Private _onMouseDown As Action(Of DomEvent)
        Private _onMouseUp As Action(Of DomEvent)
        Private _onMouseEnter As Action(Of DomEvent)
        Private _onMouseLeave As Action(Of DomEvent)

        Public Sub New()
            _name = ""
        End Sub

        Public Property Name As String
            Get
                Return _name
            End Get
            Set(value As String)
                _name = value
            End Set
        End Property

        ' The page shows accelerator markers stripped, exactly as the emitter writes them.
        Private Function VgsDisplayText(value As String) As String
            If value Is Nothing Then Return ""
            Dim result As String = ""
            Dim skip As Boolean = False
            For i As Integer = 0 To value.Length - 1
                If skip Then
                    skip = False
                Else
                    Dim ch As String = Mid(value, i + 1, 1)
                    If ch = "&" Then
                        If i + 1 < value.Length AndAlso Mid(value, i + 2, 1) = "&" Then
                            result = result & "&"
                            skip = True
                        End If
                    Else
                        result = result & ch
                    End If
                End If
            Next
            Return result
        End Function

        Public Function VgsElementFor(member As String) As Element
            If _element Is Nothing Then
                Throw New InvalidOperationException("Item '" & _name & "' cannot use '" & member & "' before it is added to a host's Items collection.")
            End If
            Return _element
        End Function

        Public Function VgsAttachedElement() As Element
            Return _element
        End Function

        Public Function VgsStrip() As ToolStrip
            Return _rootStrip
        End Function

        Public ReadOnly Property Element As Element
            Get
                Return Me.VgsElementFor("Element")
            End Get
        End Property

        Protected Overridable Function VgsClickAllowed() As Boolean
            Return True
        End Function

        ' M25: the listener precedes its AddressOf in the attach hook.
        Private Sub VgsOnDomClick(e As DomEvent)
            ' A nested menu item owns its own click. Stopping propagation keeps its parent
            ' item's listener from raising a second Click/ItemClicked for the same event.
            e.stopPropagation()
            If Not Me.VgsClickAllowed() Then Return
            Me.OnClick(EventArgs.Empty)
            Dim strip As ToolStrip = Me.VgsStrip()
            If strip IsNot Nothing Then strip.VgsRaiseItemClicked(Me)
        End Sub

        Private Sub VgsOnDomDoubleClick(e As DomEvent)
            Me.OnDoubleClick(EventArgs.Empty)
        End Sub

        Private Sub VgsOnDomMouseDown(e As DomEvent)
            Me.OnMouseDown(Me.VgsMouseArgs(e))
        End Sub

        Private Sub VgsOnDomMouseUp(e As DomEvent)
            Me.OnMouseUp(Me.VgsMouseArgs(e))
        End Sub

        Private Sub VgsOnDomMouseEnter(e As DomEvent)
            Me.OnMouseEnter(EventArgs.Empty)
        End Sub

        Private Sub VgsOnDomMouseLeave(e As DomEvent)
            Me.OnMouseLeave(EventArgs.Empty)
        End Sub

        Public Overridable Property Text As String
            Get
                Dim el As Element = Me.VgsElementFor("Text")
                If el.tagName = "INPUT" Then Return el.value
                Dim node As Element = el.firstChild
                If node IsNot Nothing AndAlso node.nodeType = 3 Then
                    Dim text As String = node.nodeValue
                    If text Is Nothing Then Return ""
                    Return text
                End If
                Return ""
            End Get
            Set(value As String)
                Dim el As Element = Me.VgsElementFor("Text")
                Dim shown As String = Me.VgsDisplayText(value)
                If el.tagName = "INPUT" Then
                    el.value = shown
                    Return
                End If
                Dim node As Element = el.firstChild
                If node IsNot Nothing AndAlso node.nodeType = 3 Then
                    node.nodeValue = shown
                    Return
                End If
                Dim text As Element = ::document.createTextNode(shown)
                If node Is Nothing Then
                    el.appendChild(text)
                Else
                    el.insertBefore(text, node)
                End If
            End Set
        End Property

        Public Overridable Property Visible As Boolean
            Get
                Return Not Me.VgsElementFor("Visible").hidden
            End Get
            Set(value As Boolean)
                Me.VgsElementFor("Visible").hidden = Not value
            End Set
        End Property

        Public Property CssClass As String
            Get
                Return Me.VgsElementFor("CssClass").className
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Me.VgsElementFor("CssClass").className = value
            End Set
        End Property

        Public Property Style As String
            Get
                Dim raw As String = Me.VgsElementFor("Style").getAttribute("style")
                If raw Is Nothing Then Return ""
                Return raw
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Me.VgsElementFor("Style").setAttribute("style", value)
            End Set
        End Property

        Public Property ToolTipText As String
            Get
                Dim raw As String = Me.VgsElementFor("ToolTipText").getAttribute("title")
                If raw Is Nothing Then Return ""
                Return raw
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Me.VgsElementFor("ToolTipText").setAttribute("title", value)
            End Set
        End Property

        Public Event Click(sender As Object, e As EventArgs)
        Public Event DoubleClick(sender As Object, e As EventArgs)
        Public Event MouseDown(sender As Object, e As MouseEventArgs)
        Public Event MouseUp(sender As Object, e As MouseEventArgs)
        Public Event MouseEnter(sender As Object, e As EventArgs)
        Public Event MouseLeave(sender As Object, e As EventArgs)

        Protected Overridable Sub OnClick(e As EventArgs)
            RaiseEvent Click(Me, e)
        End Sub

        Protected Overridable Sub OnDoubleClick(e As EventArgs)
            RaiseEvent DoubleClick(Me, e)
        End Sub

        Protected Overridable Sub OnMouseDown(e As MouseEventArgs)
            RaiseEvent MouseDown(Me, e)
        End Sub

        Protected Overridable Sub OnMouseUp(e As MouseEventArgs)
            RaiseEvent MouseUp(Me, e)
        End Sub

        Protected Overridable Sub OnMouseEnter(e As EventArgs)
            RaiseEvent MouseEnter(Me, e)
        End Sub

        Protected Overridable Sub OnMouseLeave(e As EventArgs)
            RaiseEvent MouseLeave(Me, e)
        End Sub

        Public Function VgsMouseArgs(e As DomEvent) As MouseEventArgs
            Dim button As MouseButtons = CType(0, MouseButtons)
            If e.button = 0 Then button = CType(1048576, MouseButtons)
            If e.button = 1 Then button = CType(4194304, MouseButtons)
            If e.button = 2 Then button = CType(2097152, MouseButtons)
            Return New MouseEventArgs(button, 1, e.clientX, e.clientY, 0)
        End Function

        Public Sub VgsAttach(element As Element, owner As ToolStrip)
            Me.VgsAttachCore(element)
            _strip = owner
            _rootStrip = owner
            _parentItem = Nothing
        End Sub

        Public Sub VgsAttachToItem(element As Element, parent As ToolStripMenuItem)
            Me.VgsAttachCore(element)
            _strip = Nothing
            _parentItem = parent
            _rootStrip = CType(parent.VgsStrip(), ToolStrip)
        End Sub

        Private Sub VgsAttachCore(element As Element)
            If element Is Nothing Then
                Throw New InvalidOperationException("Item '" & _name & "' has no element to attach to.")
            End If
            Me.VgsRemoveClickListener()
            _element = element
            _onClick = AddressOf Me.VgsOnDomClick
            _onDoubleClick = AddressOf Me.VgsOnDomDoubleClick
            _onMouseDown = AddressOf Me.VgsOnDomMouseDown
            _onMouseUp = AddressOf Me.VgsOnDomMouseUp
            _onMouseEnter = AddressOf Me.VgsOnDomMouseEnter
            _onMouseLeave = AddressOf Me.VgsOnDomMouseLeave
            element.addEventListener("click", _onClick)
            element.addEventListener("dblclick", _onDoubleClick)
            element.addEventListener("mousedown", _onMouseDown)
            element.addEventListener("mouseup", _onMouseUp)
            element.addEventListener("mouseenter", _onMouseEnter)
            element.addEventListener("mouseleave", _onMouseLeave)
        End Sub

        Public Sub VgsDetach()
            Me.VgsRemoveClickListener()
            _element = Nothing
            _strip = Nothing
            _rootStrip = Nothing
            _parentItem = Nothing
        End Sub

        Private Sub VgsRemoveClickListener()
            If _element IsNot Nothing Then
                If _onClick IsNot Nothing Then _element.removeEventListener("click", _onClick)
                If _onDoubleClick IsNot Nothing Then _element.removeEventListener("dblclick", _onDoubleClick)
                If _onMouseDown IsNot Nothing Then _element.removeEventListener("mousedown", _onMouseDown)
                If _onMouseUp IsNot Nothing Then _element.removeEventListener("mouseup", _onMouseUp)
                If _onMouseEnter IsNot Nothing Then _element.removeEventListener("mouseenter", _onMouseEnter)
                If _onMouseLeave IsNot Nothing Then _element.removeEventListener("mouseleave", _onMouseLeave)
            End If
            _onClick = Nothing
            _onDoubleClick = Nothing
            _onMouseDown = Nothing
            _onMouseUp = Nothing
            _onMouseEnter = Nothing
            _onMouseLeave = Nothing
        End Sub

        Public Sub VgsNotifyClicked()
            Me.OnClick(EventArgs.Empty)
            Dim strip As ToolStrip = Me.VgsStrip()
            If strip IsNot Nothing Then strip.VgsRaiseItemClicked(Me)
        End Sub
    End Class

    ' The page is the source of order for a strip, but the item objects are the source of
    ' identity: a page has no way to reconstruct a ToolStripMenuItem instance. The two are
    ' kept together by adding/removing the object and its element in the same call.
    Public Class ToolStripItemCollection
        Private _items As List(Of ToolStripItem)
        Private _strip As ToolStrip
        Private _item As ToolStripMenuItem

        Public Sub New(owner As Object, ignored As Boolean)
            _items = New List(Of ToolStripItem)()
            If TypeOf owner Is ToolStrip Then
                _strip = CType(owner, ToolStrip)
                _item = Nothing
            Else
                _strip = Nothing
                _item = CType(owner, ToolStripMenuItem)
            End If
        End Sub

        Public ReadOnly Property Count As Integer
            Get
                Return _items.Count
            End Get
        End Property

        Public Function Item(index As Integer) As ToolStripItem
            Return _items.Item(index)
        End Function

        Public Sub Add(item As ToolStripItem)
            If item Is Nothing Then Throw New ArgumentNullException("item")
            Me.VgsRemoveExisting(item)
            _items.Add(item)
            Me.VgsAttachItem(item)
            Me.VgsPlace(item, _items.Count - 1)
        End Sub

        Public Sub Insert(index As Integer, item As ToolStripItem)
            If item Is Nothing Then Throw New ArgumentNullException("item")
            If index < 0 OrElse index > _items.Count Then Throw New ArgumentOutOfRangeException("index")
            Me.VgsRemoveExisting(item)
            Dim kept As New List(Of ToolStripItem)()
            Dim total As Integer = _items.Count
            For at As Integer = 0 To total
                If at = index Then kept.Add(item)
                If at < total Then kept.Add(_items.Item(at))
            Next
            _items = kept
            Me.VgsAttachItem(item)
            Me.VgsPlace(item, index)
        End Sub

        Public Sub Remove(item As ToolStripItem)
            Dim index As Integer = Me.IndexOf(item)
            If index >= 0 Then Me.RemoveAt(index)
        End Sub

        Public Sub RemoveAt(index As Integer)
            If index < 0 OrElse index >= _items.Count Then Throw New ArgumentOutOfRangeException("index")
            Dim item As ToolStripItem = _items.Item(index)
            Dim kept As New List(Of ToolStripItem)()
            Dim total As Integer = _items.Count
            For at As Integer = 0 To total - 1
                If at <> index Then kept.Add(_items.Item(at))
            Next
            _items = kept
            Me.VgsRemoveElement(item)
            item.VgsDetach()
        End Sub

        Public Sub Clear()
            Dim total As Integer = _items.Count
            For at As Integer = 1 To total
                Me.RemoveAt(0)
            Next
        End Sub

        Public Function IndexOf(item As ToolStripItem) As Integer
            Dim total As Integer = _items.Count
            For at As Integer = 0 To total - 1
                If _items.Item(at) Is item Then Return at
            Next
            Return -1
        End Function

        Public Function Contains(item As ToolStripItem) As Boolean
            Return Me.IndexOf(item) >= 0
        End Function

        Private Function VgsContainerOrNothing() As Element
            Dim root As Element = Nothing
            If _strip IsNot Nothing Then
                If Not _strip.VgsIsAttached() Then Return Nothing
                root = _strip.VgsElement()
            ElseIf _item IsNot Nothing Then
                root = CType(_item.VgsAttachedElement(), Element)
                If root Is Nothing Then Return Nothing
            End If
            If root Is Nothing Then Return Nothing
            Dim child As Element = root.firstElementChild
            If child IsNot Nothing AndAlso child.tagName = "UL" Then Return child
            Return root
        End Function

        Private Sub VgsRemoveExisting(item As ToolStripItem)
            Dim index As Integer = Me.IndexOf(item)
            If index < 0 Then Return
            Dim kept As New List(Of ToolStripItem)()
            Dim total As Integer = _items.Count
            For at As Integer = 0 To total - 1
                If at <> index Then kept.Add(_items.Item(at))
            Next
            _items = kept
        End Sub

        Private Sub VgsAttachItem(item As ToolStripItem)
            Dim el As Element = ::document.getElementById(item.Name)
            If el Is Nothing Then
                Throw New InvalidOperationException("Item '" & item.Name & "' has no element with that id on the page.")
            End If
            If _strip IsNot Nothing Then
                item.VgsAttach(el, _strip)
            Else
                item.VgsAttachToItem(el, _item)
            End If
        End Sub

        Private Sub VgsPlace(item As ToolStripItem, index As Integer)
            Dim container As Element = Me.VgsContainerOrNothing()
            If container Is Nothing Then Return
            Dim el As Element = item.VgsElementFor("Items")
            If index >= _items.Count - 1 Then
                container.appendChild(el)
                Return
            End If
            Dim reference As Element = _items.Item(index + 1).VgsElementFor("Items")
            container.insertBefore(el, reference)
        End Sub

        Private Sub VgsRemoveElement(item As ToolStripItem)
            Dim el As Element = item.VgsAttachedElement()
            If el Is Nothing Then Return
            Dim parent As Element = el.parentElement
            If parent IsNot Nothing Then parent.removeChild(el)
        End Sub
    End Class
End Namespace
