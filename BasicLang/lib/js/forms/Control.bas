Using System
Using System.Drawing

' Portable control library - Control and its collection (task 29).
'
' A Control owns only its Name and its element reference. Every other value is read from
' and written to the element, so the page remains the single source of truth (spec 5.3).
Namespace System.Windows.Forms
    Public Class Control
        Private _name As String
        Private _element As Element
        Private _parent As Control
        Private _controls As ControlCollection
        Private _onClick As Action(Of DomEvent)
        Private _onMouseDown As Action(Of DomEvent)
        Private _onMouseUp As Action(Of DomEvent)
        Private _onMouseMove As Action(Of DomEvent)
        Private _onKeyDown As Action(Of DomEvent)
        Private _onKeyUp As Action(Of DomEvent)
        Private _onFocusIn As Action(Of DomEvent)

        Public Sub New()
            _name = ""
            _controls = New ControlCollection(Me)
        End Sub

        Public Property Name As String
            Get
                Return _name
            End Get
            Set(value As String)
                _name = value
            End Set
        End Property

        Public ReadOnly Property Controls As ControlCollection
            Get
                Return _controls
            End Get
        End Property

        Public ReadOnly Property Parent As Control
            Get
                Return _parent
            End Get
        End Property

        Public ReadOnly Property Element As Element
            Get
                Return Me.VgsElement()
            End Get
        End Property

        Public Overridable Property Text As String
            Get
                Return Me.VgsElementFor("Text").textContent
            End Get
            Set(value As String)
                If value Is Nothing Then value = ""
                Dim el As Element = Me.VgsElementFor("Text")
                Dim old As String = el.textContent
                If old Is Nothing Then old = ""
                el.textContent = value
                If old <> value Then Me.OnTextChanged(EventArgs.Empty)
            End Set
        End Property

        Public Overridable Property Enabled As Boolean
            Get
                Dim el As Element = Me.VgsElementFor("Enabled")
                If el.getAttribute("data-vgs-enabled") = "false" Then Return False
                If Me.VgsAncestorDisabled(el.parentElement) Then Return False
                Return True
            End Get
            Set(value As Boolean)
                Dim el As Element = Me.VgsElementFor("Enabled")
                Dim oldOwn As Boolean = el.getAttribute("data-vgs-enabled") <> "false"
                If oldOwn = value Then Return
                If value Then
                    el.setAttribute("data-vgs-enabled", "true")
                Else
                    el.setAttribute("data-vgs-enabled", "false")
                End If
                Me.VgsApplyEnabled(el, Me.VgsAncestorDisabled(el.parentElement))
            End Set
        End Property

        Public Overridable Property Visible As Boolean
            Get
                Dim el As Element = Me.VgsElementFor("Visible")
                If el.hidden Then Return False
                Dim p As Element = el.parentElement
                While p IsNot Nothing
                    If p.hidden Then Return False
                    p = p.parentElement
                End While
                Return True
            End Get
            Set(value As Boolean)
                Dim el As Element = Me.VgsElementFor("Visible")
                Dim newHidden As Boolean = Not value
                If el.hidden = newHidden Then Return
                el.hidden = newHidden
            End Set
        End Property

        Public Overridable Property ForeColor As Color
            Get
                Return New Color()
            End Get
            Set(value As Color)
                Me.VgsSetStyle("color", value.VgsCss())
            End Set
        End Property

        Public Overridable Property BackColor As Color
            Get
                Return New Color()
            End Get
            Set(value As Color)
                Me.VgsSetStyle("background-color", value.VgsCss())
            End Set
        End Property

        Public Overridable Property Font As Font
            Get
                Return New Font()
            End Get
            Set(value As Font)
                Me.VgsSetStyle("font-family", value.Name)
                Me.VgsSetStyle("font-size", value.Size & "pt")
                If value.Bold Then
                    Me.VgsSetStyle("font-weight", "bold")
                Else
                    Me.VgsSetStyle("font-weight", "normal")
                End If
                If value.Italic Then
                    Me.VgsSetStyle("font-style", "italic")
                Else
                    Me.VgsSetStyle("font-style", "normal")
                End If
            End Set
        End Property

        Public Overridable Property Cursor As Cursor
            Get
                Return New Cursor()
            End Get
            Set(value As Cursor)
                Me.VgsSetStyle("cursor", value.VgsCss())
            End Set
        End Property

        Public Overridable Property Padding As Padding
            Get
                Return New Padding()
            End Get
            Set(value As Padding)
                Me.VgsSetStyle("padding", value.VgsCss())
            End Set
        End Property

        Public Sub Show()
            Me.Visible = True
        End Sub

        Public Sub Hide()
            Me.Visible = False
        End Sub

        Public Sub Focus()
            Me.VgsElementFor("Focus").focus()
        End Sub

        Public Sub BringToFront()
            Dim el As Element = Me.VgsElementFor("BringToFront")
            If el.parentElement Is Nothing Then
                Throw New InvalidOperationException("Control '" & Me.Name & "' has no parent element to bring forward.")
            End If
            el.parentElement.appendChild(el)
        End Sub

        Public Event Click(sender As Object, e As EventArgs)
        Public Event TextChanged(sender As Object, e As EventArgs)
        Public Event MouseDown(sender As Object, e As MouseEventArgs)
        Public Event MouseUp(sender As Object, e As MouseEventArgs)
        Public Event MouseMove(sender As Object, e As MouseEventArgs)
        Public Event KeyDown(sender As Object, e As KeyEventArgs)
        Public Event KeyUp(sender As Object, e As KeyEventArgs)
        Public Event KeyPress(sender As Object, e As KeyPressEventArgs)
        Public Event Enter(sender As Object, e As EventArgs)

        Protected Overridable Sub OnClick(e As EventArgs)
            RaiseEvent Click(Me, e)
        End Sub

        Protected Overridable Sub OnTextChanged(e As EventArgs)
            RaiseEvent TextChanged(Me, e)
        End Sub

        Protected Overridable Sub OnMouseDown(e As MouseEventArgs)
            RaiseEvent MouseDown(Me, e)
        End Sub

        Protected Overridable Sub OnMouseUp(e As MouseEventArgs)
            RaiseEvent MouseUp(Me, e)
        End Sub

        Protected Overridable Sub OnMouseMove(e As MouseEventArgs)
            RaiseEvent MouseMove(Me, e)
        End Sub

        Protected Overridable Sub OnKeyDown(e As KeyEventArgs)
            RaiseEvent KeyDown(Me, e)
        End Sub

        Protected Overridable Sub OnKeyUp(e As KeyEventArgs)
            RaiseEvent KeyUp(Me, e)
        End Sub

        Protected Overridable Sub OnKeyPress(e As KeyPressEventArgs)
            RaiseEvent KeyPress(Me, e)
        End Sub

        Protected Overridable Sub OnEnter(e As EventArgs)
            RaiseEvent Enter(Me, e)
        End Sub

        ' M25: these declarations must stay above VgsAttachListeners, the method that
        ' takes their AddressOf. A listener declared below is refused by the backend.
        Private Sub VgsOnDomClick(e As DomEvent)
            Me.OnClick(EventArgs.Empty)
        End Sub

        Private Sub VgsOnDomMouseDown(e As DomEvent)
            Me.OnMouseDown(Me.VgsMouseArgs(e))
        End Sub

        Private Sub VgsOnDomMouseUp(e As DomEvent)
            Me.OnMouseUp(Me.VgsMouseArgs(e))
        End Sub

        Private Sub VgsOnDomMouseMove(e As DomEvent)
            Me.OnMouseMove(Me.VgsMouseArgs(e))
        End Sub

        Private Sub VgsOnDomKeyDown(e As DomEvent)
            Dim args As New KeyEventArgs(Me.VgsKeyCode(e), e.shiftKey, e.ctrlKey, e.altKey)
            Me.OnKeyDown(args)
            If args.Handled OrElse args.SuppressKeyPress Then e.preventDefault()
            If Not e.ctrlKey AndAlso Not e.altKey AndAlso Not args.SuppressKeyPress AndAlso e.key.Length = 1 Then
                Dim press As New KeyPressEventArgs(ChrW(AscW(e.key)))
                Me.OnKeyPress(press)
                If press.Handled Then e.preventDefault()
            End If
        End Sub

        Private Sub VgsOnDomKeyUp(e As DomEvent)
            Dim args As New KeyEventArgs(Me.VgsKeyCode(e), e.shiftKey, e.ctrlKey, e.altKey)
            Me.OnKeyUp(args)
            If args.Handled Then e.preventDefault()
        End Sub

        Private Sub VgsOnDomFocusIn(e As DomEvent)
            Dim related As Element = e.relatedTarget
            If related Is Nothing OrElse Not Me.VgsElement().contains(related) Then
                Me.OnEnter(EventArgs.Empty)
            End If
        End Sub

        Public Function VgsMouseArgs(e As DomEvent) As MouseEventArgs
            Return New MouseEventArgs(Me.VgsMouseButton(e.button), 1, e.clientX, e.clientY, 0)
        End Function

        Public Function VgsMouseButton(button As Integer) As MouseButtons
            If button = 0 Then Return MouseButtons.Left
            If button = 1 Then Return MouseButtons.Middle
            If button = 2 Then Return MouseButtons.Right
            Return MouseButtons.None
        End Function

        Public Function VgsKeyCode(e As DomEvent) As Keys
            Dim code As String = e.code
            If code = "" Then code = e.key

            If code = "Enter" OrElse code = "NumpadEnter" Then Return Keys.Enter
            If code = "Escape" Then Return Keys.Escape
            If code = "Backspace" Then Return Keys.Back
            If code = "Tab" Then Return Keys.Tab
            If code = "Space" Then Return Keys.Space
            If code = "Delete" Then Return Keys.Delete
            If code = "Insert" Then Return Keys.Insert
            If code = "Home" Then Return Keys.Home
            If code = "End" Then Return CType(35, Keys)
            If code = "PageUp" Then Return Keys.PageUp
            If code = "PageDown" Then Return Keys.PageDown
            If code = "ArrowLeft" Then Return Keys.Left
            If code = "ArrowUp" Then Return Keys.Up
            If code = "ArrowRight" Then Return Keys.Right
            If code = "ArrowDown" Then Return Keys.Down
            If code = "ShiftLeft" OrElse code = "ShiftRight" Then Return Keys.ShiftKey
            If code = "ControlLeft" OrElse code = "ControlRight" Then Return Keys.ControlKey
            If code = "AltLeft" OrElse code = "AltRight" Then Return Keys.Menu

            If code.Length = 4 AndAlso Left(code, 3) = "Key" Then
                Dim letter As Integer = AscW(Mid(code, 4, 1))
                If letter >= 97 AndAlso letter <= 122 Then letter = letter - 32
                Return CType(letter, Keys)
            End If

            If code.Length = 5 AndAlso Left(code, 5) = "Digit" Then
                Return CType(Integer.Parse(Mid(code, 6, 1)) + 48, Keys)
            End If

            If code.Length = 7 AndAlso Left(code, 6) = "Numpad" Then
                Return CType(Integer.Parse(Mid(code, 7, 1)) + 96, Keys)
            End If

            If code.Length >= 2 AndAlso Left(code, 1) = "F" Then
                Dim n As Integer = Integer.Parse(Mid(code, 2, code.Length - 1))
                If n >= 1 AndAlso n <= 12 Then Return CType(111 + n, Keys)
            End If

            If code.Length = 1 Then
                Dim letter As Integer = AscW(code)
                If letter >= 97 AndAlso letter <= 122 Then letter = letter - 32
                Return CType(letter, Keys)
            End If

            Return Keys.None
        End Function

        Public Function VgsAncestorDisabled(start As Element) As Boolean
            Dim p As Element = start
            While p IsNot Nothing
                If p.getAttribute("data-vgs-enabled") = "false" Then Return True
                p = p.parentElement
            End While
            Return False
        End Function

        Public Sub VgsApplyEnabled(el As Element, ancestorDisabled As Boolean)
            Dim ownDisabled As Boolean = ancestorDisabled
            If el.getAttribute("data-vgs-enabled") = "false" Then ownDisabled = True

            Dim tag As String = el.tagName
            If tag = "BUTTON" OrElse tag = "INPUT" OrElse tag = "SELECT" OrElse tag = "TEXTAREA" OrElse tag = "FIELDSET" Then
                el.disabled = ownDisabled
            Else
                If ownDisabled Then
                    el.setAttribute("aria-disabled", "true")
                    el.classList.add("vgs-disabled")
                Else
                    el.removeAttribute("aria-disabled")
                    el.classList.remove("vgs-disabled")
                End If
            End If

            Dim child As Element = el.firstElementChild
            While child IsNot Nothing
                Me.VgsApplyEnabled(child, ownDisabled)
                child = child.nextElementSibling
            End While
        End Sub

        Public Sub VgsSetStyle(name As String, value As String)
            Dim el As Element = Me.VgsElementFor("style")
            If name = "color" Then
                el.style.color = value
            ElseIf name = "background-color" Then
                el.style.backgroundColor = value
            ElseIf name = "font-family" Then
                el.style.fontFamily = value
            ElseIf name = "font-size" Then
                el.style.fontSize = value
            ElseIf name = "font-weight" Then
                el.style.fontWeight = value
            ElseIf name = "font-style" Then
                el.style.fontStyle = value
            ElseIf name = "cursor" Then
                el.style.cursor = value
            ElseIf name = "padding" Then
                el.style.padding = value
            End If
        End Sub

        Public Function VgsElement() As Element
            If _element Is Nothing Then
                Throw New InvalidOperationException("Control '" & _name & "' is not attached to a page.")
            End If
            Return _element
        End Function

        Public Function VgsElementFor(member As String) As Element
            If _element Is Nothing Then
                Throw New InvalidOperationException("Control '" & _name & "' cannot use '" & member & "' before it is added to a parent's Controls collection.")
            End If
            Return _element
        End Function

        Public Function VgsIsAttached() As Boolean
            Return _element IsNot Nothing
        End Function

        Public Sub VgsAttach(element As Element, parent As Control)
            If element Is Nothing Then
                Throw New InvalidOperationException("Control '" & _name & "' has no element to attach to.")
            End If
            _element = element
            _parent = parent
            Me.VgsAttachListeners()
        End Sub

        Public Sub VgsAttachRoot(element As Element)
            Me.VgsAttach(element, Nothing)
        End Sub

        Public Sub VgsDetach()
            If _element IsNot Nothing Then
                _element.removeEventListener("click", _onClick)
                _element.removeEventListener("mousedown", _onMouseDown)
                _element.removeEventListener("mouseup", _onMouseUp)
                _element.removeEventListener("mousemove", _onMouseMove)
                _element.removeEventListener("keydown", _onKeyDown)
                _element.removeEventListener("keyup", _onKeyUp)
                _element.removeEventListener("focusin", _onFocusIn)
                _element.remove()
            End If
            _element = Nothing
            _parent = Nothing
        End Sub

        Private Sub VgsAttachListeners()
            _onClick = AddressOf Me.VgsOnDomClick
            _onMouseDown = AddressOf Me.VgsOnDomMouseDown
            _onMouseUp = AddressOf Me.VgsOnDomMouseUp
            _onMouseMove = AddressOf Me.VgsOnDomMouseMove
            _onKeyDown = AddressOf Me.VgsOnDomKeyDown
            _onKeyUp = AddressOf Me.VgsOnDomKeyUp
            _onFocusIn = AddressOf Me.VgsOnDomFocusIn
            _element.addEventListener("click", _onClick)
            _element.addEventListener("mousedown", _onMouseDown)
            _element.addEventListener("mouseup", _onMouseUp)
            _element.addEventListener("mousemove", _onMouseMove)
            _element.addEventListener("keydown", _onKeyDown)
            _element.addEventListener("keyup", _onKeyUp)
            _element.addEventListener("focusin", _onFocusIn)
        End Sub
    End Class

    Public Class ControlCollection
        Private _owner As Control
        Private _items As List(Of Control)

        Public Sub New(owner As Control)
            _owner = owner
            _items = New List(Of Control)()
        End Sub

        Public ReadOnly Property Count As Integer
            Get
                Return _items.Count
            End Get
        End Property

        Public Function Item(index As Integer) As Control
            Return _items.Item(index)
        End Function

        Public Sub Add(child As Control)
            If child Is Nothing Then Throw New ArgumentNullException("child")
            Dim doc As Document = ::document
            Dim el As Element = doc.getElementById(child.Name)
            If el Is Nothing Then
                Throw New InvalidOperationException("Control '" & child.Name & "' has no element with that id on the page.")
            End If
            If _owner.VgsIsAttached() AndAlso Not _owner.VgsElement().contains(el) Then
                Throw New InvalidOperationException("Control '" & child.Name & "' is not inside its parent's element.")
            End If
            _items.Add(child)
            child.VgsAttach(el, _owner)
        End Sub

        Public Sub Remove(child As Control)
            If child Is Nothing Then Return
            Dim kept As New List(Of Control)()
            For Each item As Control In _items
                If item IsNot child Then kept.Add(item)
            Next
            _items = kept
            child.VgsDetach()
        End Sub
    End Class
End Namespace
