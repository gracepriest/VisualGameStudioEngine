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
        Private _foreColor As Color
        Private _backColor As Color
        Private _font As Font
        Private _cursor As Cursor
        Private _padding As Padding

        ' Task 33: ONE page stylesheet, one #id rule per touched control (spec 5.8).
        ' Entries are "id|name: value;" so a re-set property replaces in place and
        ' keeps its declaration order; values never contain "|".
        Private Shared _vgsDecls As List(Of String)
        Private Shared _vgsIds As List(Of String)
        Private Shared _vgsStyleElement As Element

        Public Sub New()
            _name = ""
            _foreColor = New Color()
            _backColor = New Color()
            _font = New Font()
            _cursor = New Cursor()
            _padding = New Padding()
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

        ' Task 35: the catalog's D2 web-only rows are real library members, not designer-only
        ' attributes. They stay behind #If WEB in shared code; desktop has no such members.
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
                Me.VgsRequestReflow()
            End Set
        End Property

        Public Property Left As Integer
            Get
                Return Me.VgsReadLeft()
            End Get
            Set(value As Integer)
                Me.VgsWriteGeometry(value, Me.VgsReadTop(), Me.VgsReadWidth(), Me.VgsReadHeight())
            End Set
        End Property

        Public Property Top As Integer
            Get
                Return Me.VgsReadTop()
            End Get
            Set(value As Integer)
                Me.VgsWriteGeometry(Me.VgsReadLeft(), value, Me.VgsReadWidth(), Me.VgsReadHeight())
            End Set
        End Property

        Public Property Width As Integer
            Get
                Return Me.VgsReadWidth()
            End Get
            Set(value As Integer)
                Me.VgsWriteGeometry(Me.VgsReadLeft(), Me.VgsReadTop(), value, Me.VgsReadHeight())
            End Set
        End Property

        Public Property Height As Integer
            Get
                Return Me.VgsReadHeight()
            End Get
            Set(value As Integer)
                Me.VgsWriteGeometry(Me.VgsReadLeft(), Me.VgsReadTop(), Me.VgsReadWidth(), value)
            End Set
        End Property

        Public Property Right As Integer
            Get
                Return Me.VgsReadLeft() + Me.VgsReadWidth()
            End Get
        End Property

        Public Property Bottom As Integer
            Get
                Return Me.VgsReadTop() + Me.VgsReadHeight()
            End Get
        End Property

        Public Property Location As Point
            Get
                Return New Point(Me.VgsReadLeft(), Me.VgsReadTop())
            End Get
            Set(value As Point)
                Me.VgsWriteGeometry(value.X, value.Y, Me.VgsReadWidth(), Me.VgsReadHeight())
            End Set
        End Property

        Public Property Size As Size
            Get
                Return New Size(Me.VgsReadWidth(), Me.VgsReadHeight())
            End Get
            Set(value As Size)
                Me.VgsWriteGeometry(Me.VgsReadLeft(), Me.VgsReadTop(), value.Width, value.Height)
            End Set
        End Property

        Public Property Anchor As AnchorStyles
            Get
                Dim raw As String = Me.VgsElementFor("Anchor").getAttribute("data-vgs-anchor")
                If raw Is Nothing OrElse raw = "" Then Return CType(5, AnchorStyles)
                Return CType(Integer.Parse(raw), AnchorStyles)
            End Get
            Set(value As AnchorStyles)
                Dim x As Integer = Me.VgsReadLeft()
                Dim y As Integer = Me.VgsReadTop()
                Dim w As Integer = Me.VgsReadWidth()
                Dim h As Integer = Me.VgsReadHeight()
                Me.VgsElementFor("Anchor").setAttribute("data-vgs-anchor", CStr(CInt(value)))
                Me.VgsWriteGeometry(x, y, w, h)
            End Set
        End Property

        Public Property Dock As DockStyle
            Get
                Dim raw As String = Me.VgsElementFor("Dock").getAttribute("data-vgs-dock")
                If raw = "Top" Then Return DockStyle.Top
                If raw = "Bottom" Then Return DockStyle.Bottom
                If raw = "Left" Then Return DockStyle.Left
                If raw = "Right" Then Return DockStyle.Right
                If raw = "Fill" Then Return DockStyle.Fill
                Return DockStyle.None
            End Get
            Set(value As DockStyle)
                Dim el As Element = Me.VgsElementFor("Dock")
                Dim x As Integer = Me.VgsReadLeft()
                Dim y As Integer = Me.VgsReadTop()
                Dim w As Integer = Me.VgsReadWidth()
                Dim h As Integer = Me.VgsReadHeight()
                Dim name As String = "None"
                If value = DockStyle.Top Then name = "Top"
                If value = DockStyle.Bottom Then name = "Bottom"
                If value = DockStyle.Left Then name = "Left"
                If value = DockStyle.Right Then name = "Right"
                If value = DockStyle.Fill Then name = "Fill"
                el.setAttribute("data-vgs-dock", name)
                If value = DockStyle.None Then
                    ' Restore the requested bounds, not the dock's last live rectangle.
                    Me.VgsWriteGeometry(Me.VgsRequested("data-vgs-x", x), Me.VgsRequested("data-vgs-y", y), Me.VgsRequested("data-vgs-w", w), Me.VgsRequested("data-vgs-h", h))
                Else
                    ' R2: the script owns every geometry property of a docked control.
                    Me.VgsClearGeometry(el.id)
                    Me.VgsStoreRequested(x, y, w, h)
                    Me.VgsFlushStyles()
                    Me.VgsRequestReflow()
                End If
            End Set
        End Property

        Public Overridable Property ForeColor As Color
            Get
                Return _foreColor
            End Get
            Set(value As Color)
                _foreColor = value
                Me.VgsSetStyle("color", value.VgsCss())
            End Set
        End Property
        Public Overridable Property BackColor As Color
            Get
                Return _backColor
            End Get
            Set(value As Color)
                _backColor = value
                Me.VgsSetStyle("background-color", value.VgsCss())
            End Set
        End Property
        Public Overridable Property Font As Font
            Get
                Return _font
            End Get
            Set(value As Font)
                _font = value
                Me.VgsSetStyle("font-family", Chr(34) & value.Name & Chr(34))
                Me.VgsSetStyle("font-size", CStr(value.Size) & "pt")
                If value.Bold Then
                    Me.VgsSetStyle("font-weight", "bold")
                Else
                    Me.VgsRemoveDeclaration(Me.VgsElementFor("style").id, "font-weight")
                End If
                If value.Italic Then
                    Me.VgsSetStyle("font-style", "italic")
                Else
                    Me.VgsRemoveDeclaration(Me.VgsElementFor("style").id, "font-style")
                End If
                Dim decoration As String = ""
                If value.Underline Then decoration = "underline"
                If value.Strikeout Then
                    If decoration = "" Then decoration = "line-through" Else decoration = decoration & " line-through"
                End If
                If decoration = "" Then
                    Me.VgsRemoveDeclaration(Me.VgsElementFor("style").id, "text-decoration")
                Else
                    Me.VgsSetStyle("text-decoration", decoration)
                End If
                Me.VgsFlushStyles()
            End Set
        End Property
        Public Overridable Property Cursor As Cursor
            Get
                Return _cursor
            End Get
            Set(value As Cursor)
                _cursor = value
                Me.VgsSetStyle("cursor", value.VgsCss())
            End Set
        End Property
        Public Overridable Property Padding As Padding
            Get
                Return _padding
            End Get
            Set(value As Padding)
                _padding = value
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
            Dim el As Element = Me.VgsElementFor("Focus")
            If el.getAttribute("data-vgs-enable-target") = "input" Then
                Me.VgsInputElement().focus()
            Else
                el.focus()
            End If
        End Sub

        Public Sub BringToFront()
            Dim el As Element = Me.VgsElementFor("BringToFront")
            If el.parentElement Is Nothing Then
                Throw New InvalidOperationException("Control '" & Me.Name & "' has no parent element to bring forward.")
            End If
            el.parentElement.appendChild(el)
            Me.VgsRequestReflow()
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
            Me.VgsHandleDomClick(e)
        End Sub

        Public Overridable Sub VgsHandleDomClick(e As DomEvent)
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

        ' Task 37: the owner of an ObjectCollection is told when the live selection changed.
        ' ComboBox/ListBox override this to mark the DOM and raise SelectedIndexChanged.
        Public Overridable Sub VgsNotifySelectionChanged()
        End Sub

        ' Kind hooks never change the common event surface. Delegates are retained by each
        ' kind so removing or replacing an element removes the SAME listener identities.
        Public Overridable Sub VgsAttachExtraListeners()
        End Sub

        Public Overridable Sub VgsDetachExtraListeners()
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

            If code.Length = 4 AndAlso Strings.Left(code, 3) = "Key" Then
                Dim letter As Integer = AscW(Mid(code, 4, 1))
                If letter >= 97 AndAlso letter <= 122 Then letter = letter - 32
                Return CType(letter, Keys)
            End If

            If code.Length = 5 AndAlso Strings.Left(code, 5) = "Digit" Then
                Return CType(Integer.Parse(Mid(code, 6, 1)) + 48, Keys)
            End If

            If code.Length = 7 AndAlso Strings.Left(code, 6) = "Numpad" Then
                Return CType(Integer.Parse(Mid(code, 7, 1)) + 96, Keys)
            End If

            If code.Length >= 2 AndAlso Strings.Left(code, 1) = "F" Then
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
            Dim target As String = el.getAttribute("data-vgs-enable-target")
            If target = "input" Then
                el.querySelector("input").disabled = ownDisabled
                If ownDisabled Then
                    el.classList.add("vgs-disabled")
                Else
                    el.classList.remove("vgs-disabled")
                End If
            ElseIf target = "native" OrElse (target Is Nothing AndAlso (tag = "BUTTON" OrElse tag = "INPUT" OrElse tag = "SELECT" OrElse tag = "TEXTAREA" OrElse tag = "FIELDSET")) Then
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
            Dim id As String = el.id
            If id = "" Then
                Throw New InvalidOperationException("Control '" & Me.Name & "' has no id for its style rule.")
            End If
            If value = "" Then
                Me.VgsRemoveDeclaration(id, name)
            Else
                Me.VgsSetDeclaration(id, name, value)
            End If
            Me.VgsFlushStyles()
        End Sub

        ' The rule for this control, without the page's media wrapper. Also the
        ' node tier's reader: the seam is a rule, never an inline style.
        Public Function VgsStyleRule() As String
            If _vgsDecls Is Nothing Then Return ""
            Dim id As String = Me.VgsElementFor("style").id
            Dim prefix As String = id & "|"
            Dim rule As String = ""
            For Each entry As String In _vgsDecls
                If Strings.Left(entry, prefix.Length) = prefix Then
                    rule = rule & Mid(entry, prefix.Length + 1, entry.Length - prefix.Length)
                End If
            Next
            Return rule
        End Function

        ' The last value this library wrote, independent of a browser's cascade.
        Public Function VgsStyleValue(name As String) As String
            If _vgsDecls Is Nothing Then Return ""
            Dim id As String = Me.VgsElementFor("style").id
            Dim prefix As String = id & "|" & name & ":"
            For Each entry As String In _vgsDecls
                If Strings.Left(entry, prefix.Length) = prefix Then
                    Dim value As String = Mid(entry, prefix.Length + 1, entry.Length - prefix.Length)
                    If Mid(value, value.Length, 1) = ";" Then value = Strings.Left(value, value.Length - 1)
                    If Strings.Left(value, 1) = " " Then value = Mid(value, 2, value.Length - 1)
                    Return value
                End If
            Next
            Return ""
        End Function

        Private Sub VgsSetDeclaration(id As String, name As String, value As String)
            If _vgsDecls Is Nothing Then _vgsDecls = New List(Of String)()
            If _vgsIds Is Nothing Then _vgsIds = New List(Of String)()
            Dim prefix As String = id & "|" & name & ":"
            For i As Integer = 0 To _vgsDecls.Count - 1
                If Strings.Left(_vgsDecls(i), prefix.Length) = prefix Then
                    _vgsDecls(i) = prefix & " " & value & ";"
                    Return
                End If
            Next
            _vgsDecls.Add(prefix & " " & value & ";")
            If Not Me.VgsHasId(id) Then _vgsIds.Add(id)
        End Sub

        Private Sub VgsRemoveDeclaration(id As String, name As String)
            If _vgsDecls Is Nothing Then Return
            Dim prefix As String = id & "|" & name & ":"
            Dim kept As New List(Of String)()
            For Each entry As String In _vgsDecls
                If Strings.Left(entry, prefix.Length) <> prefix Then kept.Add(entry)
            Next
            _vgsDecls = kept
        End Sub

        Public Function VgsHasId(id As String) As Boolean
            If _vgsIds Is Nothing Then Return False
            For Each existing As String In _vgsIds
                If existing = id Then Return True
            Next
            Return False
        End Function

        Private Sub VgsClearGeometry(id As String)
            If _vgsDecls Is Nothing Then Return
            Dim kept As New List(Of String)()
            For Each entry As String In _vgsDecls
                Dim prefix As String = id & "|"
                Dim owned As Boolean = Strings.Left(entry, prefix.Length) = prefix
                Dim keep As Boolean = True
                If owned Then
                    Dim decl As String = Mid(entry, prefix.Length + 1, entry.Length - prefix.Length)
                    If Strings.Left(decl, 5) = "left:" OrElse Strings.Left(decl, 6) = "right:" OrElse Strings.Left(decl, 4) = "top:" OrElse Strings.Left(decl, 7) = "bottom:" OrElse Strings.Left(decl, 6) = "width:" OrElse Strings.Left(decl, 7) = "height:" Then
                        keep = False
                    End If
                End If
                If keep Then kept.Add(entry)
            Next
            _vgsDecls = kept
        End Sub

        Private Sub VgsRemoveControlDeclarations(id As String)
            If _vgsDecls Is Nothing Then Return
            Dim prefix As String = id & "|"
            Dim kept As New List(Of String)()
            For Each entry As String In _vgsDecls
                If Strings.Left(entry, prefix.Length) <> prefix Then kept.Add(entry)
            Next
            _vgsDecls = kept
        End Sub

        Private Sub VgsFlushStyles()
            If _vgsDecls Is Nothing Then Return
            If _vgsStyleElement Is Nothing Then
                _vgsStyleElement = ::document.createElement("style")
                _vgsStyleElement.id = "vgs-live"
                ::document.head.appendChild(_vgsStyleElement)
            End If
            Dim bp As Integer = Me.VgsBreakpoint()
            Dim text As String = ""
            If bp > 0 Then text = "@media (width >= " & CStr(bp) & "px){"
            For Each id As String In _vgsIds
                Dim rule As String = ""
                Dim prefix As String = id & "|"
                For Each entry As String In _vgsDecls
                    If Strings.Left(entry, prefix.Length) = prefix Then
                        rule = rule & Mid(entry, prefix.Length + 1, entry.Length - prefix.Length)
                    End If
                Next
                If rule <> "" Then text = text & "#" & id & "{" & rule & "}"
            Next
            If bp > 0 Then text = text & "}"
            _vgsStyleElement.textContent = text
        End Sub

        Public Function VgsRequested(name As String, fallback As Integer) As Integer
            Dim raw As String = Me.VgsElement().getAttribute(name)
            If raw Is Nothing OrElse raw = "" Then Return fallback
            Return Integer.Parse(raw)
        End Function

        Public Function VgsBreakpoint() As Integer
            Dim form As Element = ::document.querySelector(".vgs-form")
            If form Is Nothing Then Return 0
            Dim raw As String = form.getAttribute("data-vgs-bp")
            If raw Is Nothing OrElse raw = "" Then Return 0
            Return Integer.Parse(raw)
        End Function

        Public Function VgsDesktop() As Boolean
            Dim bp As Integer = Me.VgsBreakpoint()
            If bp <= 0 Then Return True
            Dim w As Window = ::window
            Return w.innerWidth >= bp
        End Function

        ' Live rectangle in the container's client coordinates at desktop width;
        ' the requested bounds below the breakpoint (spec 5.9).
        Public Function VgsReadLeft() As Integer
            If Not Me.VgsDesktop() Then Return Me.VgsMetric("data-vgs-x")
            Dim el As Element = Me.VgsElement()
            Dim container As Element = el.parentElement
            If container Is Nothing Then Return 0
            Dim inset As Integer = 0
            Dim raw As String = container.getAttribute("data-vgs-inset")
            If raw IsNot Nothing AndAlso raw <> "" Then inset = Integer.Parse(raw)
            Return CInt(el.getBoundingClientRect().left - container.getBoundingClientRect().left) - inset
        End Function

        Public Function VgsReadTop() As Integer
            If Not Me.VgsDesktop() Then Return Me.VgsMetric("data-vgs-y")
            Dim el As Element = Me.VgsElement()
            Dim container As Element = el.parentElement
            If container Is Nothing Then Return 0
            Dim inset As Integer = 0
            Dim raw As String = container.getAttribute("data-vgs-inset")
            If raw IsNot Nothing AndAlso raw <> "" Then inset = Integer.Parse(raw)
            Return CInt(el.getBoundingClientRect().top - container.getBoundingClientRect().top) - inset
        End Function

        Public Function VgsReadWidth() As Integer
            If Not Me.VgsDesktop() Then Return Me.VgsMetric("data-vgs-w")
            Return CInt(Me.VgsElement().getBoundingClientRect().width)
        End Function

        Public Function VgsReadHeight() As Integer
            If Not Me.VgsDesktop() Then Return Me.VgsMetric("data-vgs-h")
            Return CInt(Me.VgsElement().getBoundingClientRect().height)
        End Function

        Public Function VgsContainerClientWidth() As Integer
            If Not Me.VgsDesktop() Then
                Dim parentEl As Element = Me.VgsElement().parentElement
                If parentEl Is Nothing Then Return 0
                Dim raw As String = parentEl.getAttribute("data-vgs-cw")
                If raw Is Nothing OrElse raw = "" Then Return 0
                Return Integer.Parse(raw)
            End If
            Dim container As Element = Me.VgsElement().parentElement
            If container Is Nothing Then Return 0
            Return container.clientWidth
        End Function

        Public Function VgsContainerClientHeight() As Integer
            If Not Me.VgsDesktop() Then
                Dim parentEl As Element = Me.VgsElement().parentElement
                If parentEl Is Nothing Then Return 0
                Dim raw As String = parentEl.getAttribute("data-vgs-ch")
                If raw Is Nothing OrElse raw = "" Then Return 0
                Return Integer.Parse(raw)
            End If
            Dim container As Element = Me.VgsElement().parentElement
            If container Is Nothing Then Return 0
            Return container.clientHeight
        End Function

        Private Sub VgsStoreRequested(x As Integer, y As Integer, w As Integer, h As Integer)
            Dim el As Element = Me.VgsElement()
            el.setAttribute("data-vgs-x", CStr(x))
            el.setAttribute("data-vgs-y", CStr(y))
            el.setAttribute("data-vgs-w", CStr(w))
            el.setAttribute("data-vgs-h", CStr(h))
        End Sub

        Public Function VgsStretched(insets As Integer) As String
            If insets < 0 Then Return "calc(100% + " & CStr(-insets) & "px)"
            Return "calc(100% - " & CStr(insets) & "px)"
        End Function

        Public Function VgsCentred(offset As Integer, container As Integer) As String
            Dim shift As Double = offset - container / 2.0
            If shift < 0 Then Return "calc(50% - " & CStr(-shift) & "px)"
            Return "calc(50% + " & CStr(shift) & "px)"
        End Function

        ' MIRROR of FormAnchorCss.Axis (spec 5.9); PortableAnchorLockStepTests gates the pair.
        Private Sub VgsWriteGeometry(x As Integer, y As Integer, w As Integer, h As Integer)
            Dim el As Element = Me.VgsElementFor("Geometry")
            Dim id As String = el.id
            Me.VgsStoreRequested(x, y, w, h)
            Dim dockName As String = el.getAttribute("data-vgs-dock")
            If dockName IsNot Nothing AndAlso dockName <> "" AndAlso dockName <> "None" Then
                Me.VgsClearGeometry(id)
                Me.VgsFlushStyles()
                Me.VgsRequestReflow()
                Return
            End If

            Me.VgsClearGeometry(id)
            Dim cw As Integer = Me.VgsContainerClientWidth()
            Dim ch As Integer = Me.VgsContainerClientHeight()
            Dim a As Integer = CInt(Me.Anchor)
            Dim toLeft As Boolean = (a >= 4 AndAlso a < 8) OrElse a >= 12
            Dim toRight As Boolean = a >= 8
            Dim toTop As Boolean = (a Mod 2) = 1
            Dim toBottom As Boolean = (a Mod 4) >= 2

            Dim usedW As Integer = w
            If usedW < 0 Then usedW = 0
            Dim farW As Integer = cw - x - usedW
            If toLeft AndAlso toRight Then
                Me.VgsSetDeclaration(id, "left", CStr(x) & "px")
                Me.VgsSetDeclaration(id, "right", CStr(farW) & "px")
                Me.VgsSetDeclaration(id, "width", Me.VgsStretched(x + farW))
            ElseIf toRight Then
                Me.VgsSetDeclaration(id, "right", CStr(farW) & "px")
                If w > 0 Then Me.VgsSetDeclaration(id, "width", CStr(w) & "px")
            ElseIf toLeft Then
                Me.VgsSetDeclaration(id, "left", CStr(x) & "px")
                If w > 0 Then Me.VgsSetDeclaration(id, "width", CStr(w) & "px")
            Else
                Me.VgsSetDeclaration(id, "left", Me.VgsCentred(x, cw))
                If w > 0 Then Me.VgsSetDeclaration(id, "width", CStr(w) & "px")
            End If

            Dim usedH As Integer = h
            If usedH < 0 Then usedH = 0
            Dim farH As Integer = ch - y - usedH
            If toTop AndAlso toBottom Then
                Me.VgsSetDeclaration(id, "top", CStr(y) & "px")
                Me.VgsSetDeclaration(id, "bottom", CStr(farH) & "px")
                Me.VgsSetDeclaration(id, "height", Me.VgsStretched(y + farH))
            ElseIf toBottom Then
                Me.VgsSetDeclaration(id, "bottom", CStr(farH) & "px")
                If h > 0 Then Me.VgsSetDeclaration(id, "height", CStr(h) & "px")
            ElseIf toTop Then
                Me.VgsSetDeclaration(id, "top", CStr(y) & "px")
                If h > 0 Then Me.VgsSetDeclaration(id, "height", CStr(h) & "px")
            Else
                Me.VgsSetDeclaration(id, "top", Me.VgsCentred(y, ch))
                If h > 0 Then Me.VgsSetDeclaration(id, "height", CStr(h) & "px")
            End If

            Me.VgsFlushStyles()
            Me.VgsRequestReflow()
        End Sub

        Public Sub VgsRequestReflow()
            Dim w As Object = ::window
            Dim hook As VgsDockHook = CType(w.vgsDock, VgsDockHook)
            If hook Is Nothing Then
                Throw New InvalidOperationException("This portable page has no 'vgsDock' hook; a portable page must carry the dock script.")
            End If
            hook.request()
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
            If _element IsNot Nothing Then Me.VgsRemoveListeners()
            _element = element
            _parent = parent
            Me.VgsAttachListeners()
        End Sub

        Public Sub VgsAttachRoot(element As Element)
            Me.VgsAttach(element, Nothing)
        End Sub

        Public Sub VgsDetach()
            If _element IsNot Nothing Then
                Me.VgsRemoveListeners()
                Me.VgsRemoveControlDeclarations(_element.id)
                Me.VgsFlushStyles()
                _element.remove()
            End If
            _element = Nothing
            _parent = Nothing
        End Sub

        Private Sub VgsRemoveListeners()
            Me.VgsDetachExtraListeners()
            _element.removeEventListener("click", _onClick)
            _element.removeEventListener("mousedown", _onMouseDown)
            _element.removeEventListener("mouseup", _onMouseUp)
            _element.removeEventListener("mousemove", _onMouseMove)
            _element.removeEventListener("keydown", _onKeyDown)
            _element.removeEventListener("keyup", _onKeyUp)
            _element.removeEventListener("focusin", _onFocusIn)
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
            Me.VgsAttachExtraListeners()
        End Sub

        ' A TextBox swap retains the Control and all user event subscriptions, but never
        ' leaves a library listener on the discarded element. Parent/collection stay intact.
        Public Sub VgsReplaceElement(element As Element)
            Dim old As Element = Me.VgsElementFor("Multiline")
            Me.VgsRemoveListeners()
            If old.parentElement IsNot Nothing Then old.parentElement.replaceChild(element, old)
            _element = element
            Me.VgsAttachListeners()
        End Sub

        Public Function VgsInputElement(Optional member As String = "Checked") As Element
            Dim el As Element = Me.VgsElementFor(member)
            If el.tagName = "INPUT" Then Return el
            Dim input As Element = el.querySelector("input")
            If input Is Nothing Then Throw New InvalidOperationException("Control '" & Me.Name & "' has no input element.")
            Return input
        End Function

        ' Task 32 metadata is emitted from FormClientArea, never re-derived by kind here.
        ' Task 33's runtime geometry uses these design-client and docking inputs.
        Public Function VgsMetric(name As String) As Integer
            Dim value As String = Me.VgsElement().getAttribute(name)
            If value Is Nothing OrElse value = "" Then Return 0
            Return Integer.Parse(value)
        End Function

        Public Function VgsClientInset() As Integer
            Return Me.VgsMetric("data-vgs-inset")
        End Function

        Public Function VgsDesignClientWidth() As Integer
            Return Me.VgsMetric("data-vgs-cw")
        End Function

        Public Function VgsDesignClientHeight() As Integer
            Return Me.VgsMetric("data-vgs-ch")
        End Function

        Public Function VgsDockPadding() As Padding
            Dim pad As New Padding()
            pad.Left = Me.VgsMetric("data-vgs-pl")
            pad.Top = Me.VgsMetric("data-vgs-pt")
            pad.Right = Me.VgsMetric("data-vgs-pr")
            pad.Bottom = Me.VgsMetric("data-vgs-pb")
            Return pad
        End Function

        ' Direct children only: a nested GroupBox's legend is not its parent's caption.
        Public Function VgsCaptionElement(tag As String) As Element
            Dim el As Element = Me.VgsElementFor("Text")
            Dim part As Element = el.firstElementChild
            While part IsNot Nothing
                If part.tagName = tag Then Return part
                part = part.nextElementSibling
            End While
            Dim doc As Document = ::document
            part = doc.createElement(tag)
            If tag = "LEGEND" Then
                el.insertBefore(part, el.firstElementChild)
            Else
                el.appendChild(part)
            End If
            Return part
        End Function

        Public Function VgsCaptionText(tag As String) As String
            Dim text As String = Me.VgsCaptionElement(tag).textContent
            If text Is Nothing Then text = ""
            Return text
        End Function

        Public Sub VgsSetCaptionText(tag As String, value As String)
            If value Is Nothing Then value = ""
            Dim part As Element = Me.VgsCaptionElement(tag)
            Dim old As String = part.textContent
            If old Is Nothing Then old = ""
            If old = value Then Return
            part.textContent = value
            Me.OnTextChanged(EventArgs.Empty)
        End Sub

        Public Function VgsContentAlignment(defaultValue As ContentAlignment) As ContentAlignment
            Dim el As Element = Me.VgsElementFor("TextAlign")
            Dim raw As String = el.getAttribute("data-vgs-textalign")
            If raw IsNot Nothing AndAlso raw <> "" Then Return CType(Integer.Parse(raw), ContentAlignment)
            Dim w As Window = ::window
            Dim horizontal As String = w.getComputedStyle(el).textAlign
            Dim row As Integer = 0
            If CInt(defaultValue) >= 256 Then
                row = 256
            ElseIf CInt(defaultValue) >= 16 Then
                row = 16
            Else
                row = 1
            End If
            If horizontal = "center" Then Return CType(row * 2, ContentAlignment)
            If horizontal = "right" Then Return CType(row * 4, ContentAlignment)
            If horizontal = "left" Then Return CType(row, ContentAlignment)
            Return defaultValue
        End Function

        Public Sub VgsSetContentAlignment(value As ContentAlignment)
            Dim el As Element = Me.VgsElementFor("TextAlign")
            el.setAttribute("data-vgs-textalign", CStr(CInt(value)))
            Dim align As String = "left"
            If value = ContentAlignment.TopCenter OrElse value = ContentAlignment.MiddleCenter OrElse value = ContentAlignment.BottomCenter Then
                align = "center"
            ElseIf value = ContentAlignment.TopRight OrElse value = ContentAlignment.MiddleRight OrElse value = ContentAlignment.BottomRight Then
                align = "right"
            End If
            Me.VgsSetStyle("text-align", align)
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
            Dim kept As New List(Of Control)()
            For Each item As Control In _items
                If item IsNot child Then kept.Add(item)
            Next
            kept.Add(child)
            _items = kept
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
