' Portable control library - shared non-Control System.Windows.Forms shells (task 28).
' Task 29 adds the members the common style properties convert; later kind tasks extend them.
Namespace System.Windows.Forms
    ' Task 37: the live <option> children of an owner's <select>. The page is the list:
    ' there is no parallel ArrayList, so markup, Items.Add and a user's DOM edit all agree.
    Public Class ObjectCollection
        Private _owner As Control

        Public Sub New(owner As Control)
            _owner = owner
        End Sub

        Public ReadOnly Property Count As Integer
            Get
                Return Me.VgsOptionCount(Me.VgsSelectElement())
            End Get
        End Property

        Public Function Item(index As Integer) As Object
            Dim el As Element = Me.VgsSelectElement()
            If index < 0 OrElse index >= Me.VgsOptionCount(el) Then Throw New ArgumentOutOfRangeException("index")
            Return Me.VgsOptionText(Me.VgsOptionAt(el, index))
        End Function

        Public Function IndexOf(item As Object) As Integer
            If item Is Nothing Then Return -1
            Dim wanted As String = "" & item
            Dim el As Element = Me.VgsSelectElement()
            Dim index As Integer = 0
            Dim total As Integer = el.childElementCount
            If total = 0 Then Return -1
            For i As Integer = 0 To total - 1
                Dim child As Element = Me.VgsElementAt(el, i)
                If child.tagName = "OPTION" Then
                    If Me.VgsOptionText(child) = wanted Then Return index
                    index = index + 1
                End If
            Next
            Return -1
        End Function

        Public Function Contains(item As Object) As Boolean
            Return Me.IndexOf(item) >= 0
        End Function

        Public Sub Add(item As Object)
            Dim el As Element = Me.VgsSelectElement()
            Dim oldCount As Integer = Me.VgsOptionCount(el)
            Dim oldIndex As Integer = el.selectedIndex
            el.appendChild(Me.VgsCreateOption(item))
            If oldCount = 0 Then
                ' WinForms does not select the first item merely because one was added.
                el.selectedIndex = -1
            ElseIf oldIndex >= 0 Then
                ' Pin the surviving option: an implicit HTML default can otherwise move.
                el.selectedIndex = oldIndex
            End If
            Me.VgsMark(el)
        End Sub

        Public Sub Insert(index As Integer, item As Object)
            Dim el As Element = Me.VgsSelectElement()
            Dim oldCount As Integer = Me.VgsOptionCount(el)
            If index < 0 OrElse index > oldCount Then Throw New ArgumentOutOfRangeException("index")
            Dim oldIndex As Integer = el.selectedIndex
            Dim optionEl As Element = Me.VgsCreateOption(item)
            If index = oldCount Then
                el.appendChild(optionEl)
            Else
                el.insertBefore(optionEl, Me.VgsOptionAt(el, index))
            End If
            If oldCount = 0 Then
                el.selectedIndex = -1
            ElseIf oldIndex >= 0 Then
                If index <= oldIndex Then
                    el.selectedIndex = oldIndex + 1
                Else
                    el.selectedIndex = oldIndex
                End If
            End If
            Me.VgsMark(el)
        End Sub

        Public Sub Remove(item As Object)
            Dim index As Integer = Me.IndexOf(item)
            If index >= 0 Then Me.RemoveAt(index)
        End Sub

        Public Sub RemoveAt(index As Integer)
            Dim el As Element = Me.VgsSelectElement()
            If index < 0 OrElse index >= Me.VgsOptionCount(el) Then Throw New ArgumentOutOfRangeException("index")
            Dim oldIndex As Integer = el.selectedIndex
            Dim removedSelected As Boolean = oldIndex = index
            el.removeChild(Me.VgsOptionAt(el, index))
            If oldIndex < 0 Then
                Me.VgsMark(el)
                Return
            End If
            If removedSelected Then
                ' The native <select> and WinForms both end at -1. The plan asks for the
                ' change notification that the desktop control omits (see the preflight).
                el.selectedIndex = -1
                _owner.VgsNotifySelectionChanged()
            ElseIf index < oldIndex Then
                el.selectedIndex = oldIndex - 1
                Me.VgsMark(el)
            Else
                el.selectedIndex = oldIndex
                Me.VgsMark(el)
            End If
        End Sub

        Public Sub Clear()
            Dim el As Element = Me.VgsSelectElement()
            Dim hadSelection As Boolean = el.selectedIndex >= 0
            Dim total As Integer = el.childElementCount
            If total > 0 Then
                For i As Integer = 0 To total - 1
                    Dim child As Element = Me.VgsOptionAt(el, 0)
                    If child IsNot Nothing Then el.removeChild(child)
                Next
            End If
            el.selectedIndex = -1
            If hadSelection Then
                _owner.VgsNotifySelectionChanged()
            Else
                Me.VgsMark(el)
            End If
        End Sub

        ' ---- selection seam used by ComboBox/ListBox. All are Vgs-prefixed, so the
        ' ---- public-method manifest does not grow a second time for them.

        Public Function VgsSelectedIndex() As Integer
            Return Me.VgsSelectElement().selectedIndex
        End Function

        Public Function VgsSelectedText(Optional member As String = "Items") As String
            Dim el As Element = Me.VgsSelectElement(member)
            Dim index As Integer = el.selectedIndex
            If index < 0 OrElse index >= Me.VgsOptionCount(el) Then Return ""
            Return Me.VgsOptionText(Me.VgsOptionAt(el, index))
        End Function

        Public Sub VgsSelectText(member As String, value As String)
            If value Is Nothing Then value = ""
            Dim el As Element = Me.VgsSelectElement(member)
            Dim index As Integer = 0
            Dim total As Integer = el.childElementCount
            If total > 0 Then
                For i As Integer = 0 To total - 1
                    Dim child As Element = Me.VgsElementAt(el, i)
                    If child.tagName = "OPTION" Then
                        If Me.VgsOptionText(child) = value Then
                            Me.VgsSetSelectedIndexCore(el, index)
                            Return
                        End If
                        index = index + 1
                    End If
                Next
            End If
            Me.VgsSetSelectedIndexCore(el, -1)
        End Sub

        Public Sub VgsSetSelectedIndex(value As Integer)
            Me.VgsSetSelectedIndexCore(Me.VgsSelectElement(), value)
        End Sub

        Public Sub VgsMarkSelection()
            Me.VgsMark(Me.VgsSelectElement())
        End Sub

        Public Sub VgsInitSelectionMarker()
            Me.VgsMark(Me.VgsSelectElement())
        End Sub

        Public Sub VgsDomSelectionChanged()
            Dim el As Element = Me.VgsSelectElement()
            Dim raw As String = el.getAttribute("data-vgs-selected")
            If raw Is Nothing OrElse raw = "" Then
                Me.VgsMark(el)
                Return
            End If
            Dim previous As Integer = Integer.Parse(raw)
            If el.selectedIndex = previous Then Return
            _owner.VgsNotifySelectionChanged()
        End Sub

        Public Function VgsSelectElement(Optional member As String = "Items") As Element
            Dim el As Element = _owner.VgsElementFor(member)
            If el.tagName <> "SELECT" Then
                Throw New InvalidOperationException("Control '" & _owner.Name & "' needs a <select> for '" & member & "'.")
            End If
            Return el
        End Function

        Private Sub VgsSetSelectedIndexCore(el As Element, value As Integer)
            Dim count As Integer = Me.VgsOptionCount(el)
            If value < -1 OrElse value >= count Then Throw New ArgumentOutOfRangeException("SelectedIndex")
            Dim oldIndex As Integer = el.selectedIndex
            If oldIndex = value Then
                Me.VgsMark(el)
                Return
            End If
            el.selectedIndex = value
            _owner.VgsNotifySelectionChanged()
        End Sub

        Public Function VgsCreateOption(item As Object) As Element
            Dim doc As Document = ::document
            Dim optionEl As Element = doc.createElement("option")
            optionEl.textContent = Me.VgsItemText(item)
            Return optionEl
        End Function

        Public Function VgsItemText(item As Object) As String
            If item Is Nothing Then Return ""
            Return "" & item
        End Function

        Public Function VgsOptionText(optionEl As Element) As String
            Dim text As String = optionEl.textContent
            If text Is Nothing Then text = ""
            Return text
        End Function

        Public Function VgsOptionCount(el As Element) As Integer
            Dim count As Integer = 0
            Dim total As Integer = el.childElementCount
            If total = 0 Then Return 0
            For i As Integer = 0 To total - 1
                If Me.VgsElementAt(el, i).tagName = "OPTION" Then count = count + 1
            Next
            Return count
        End Function

        Public Function VgsOptionAt(el As Element, index As Integer) As Element
            Dim at As Integer = 0
            Dim total As Integer = el.childElementCount
            If total = 0 Then Return Nothing
            For i As Integer = 0 To total - 1
                Dim child As Element = Me.VgsElementAt(el, i)
                If child.tagName = "OPTION" Then
                    If at = index Then Return child
                    at = at + 1
                End If
            Next
            Return Nothing
        End Function

        Public Function VgsElementAt(el As Element, index As Integer) As Element
            Dim child As Element = el.firstElementChild
            For i As Integer = 1 To index
                child = child.nextElementSibling
            Next
            Return child
        End Function

        Private Sub VgsMark(el As Element)
            el.setAttribute("data-vgs-selected", CStr(el.selectedIndex))
        End Sub
    End Class

    Public Class Padding
        Public Property Left As Integer
        Public Property Top As Integer
        Public Property Right As Integer
        Public Property Bottom As Integer

        Public Sub New()
        End Sub




        Public Function VgsCss() As String
            Return Me.Top & "px " & Me.Right & "px " & Me.Bottom & "px " & Me.Left & "px"
        End Function
    End Class

    Public Class Cursor
        Public Property Name As String

        Public Sub New()
            Me.Name = "default"
        End Sub

        Public Shared Function VgsNamed(name As String) As Cursor
            Dim value As New Cursor()
            value.Name = name
            Return value
        End Function

        Public Function VgsCss() As String
            Return Me.Name
        End Function
    End Class

    Public Class Cursors
        Public Shared ReadOnly Property AppStarting As Cursor
            Get
                Return Cursor.VgsNamed("progress")
            End Get
        End Property

        Public Shared ReadOnly Property Arrow As Cursor
            Get
                Return Cursor.VgsNamed("default")
            End Get
        End Property

        Public Shared ReadOnly Property Cross As Cursor
            Get
                Return Cursor.VgsNamed("crosshair")
            End Get
        End Property

        Public Shared ReadOnly Property Default As Cursor
            Get
                Return Cursor.VgsNamed("default")
            End Get
        End Property

        Public Shared ReadOnly Property IBeam As Cursor
            Get
                Return Cursor.VgsNamed("text")
            End Get
        End Property

        Public Shared ReadOnly Property No As Cursor
            Get
                Return Cursor.VgsNamed("not-allowed")
            End Get
        End Property

        Public Shared ReadOnly Property SizeAll As Cursor
            Get
                Return Cursor.VgsNamed("move")
            End Get
        End Property

        Public Shared ReadOnly Property SizeNESW As Cursor
            Get
                Return Cursor.VgsNamed("nesw-resize")
            End Get
        End Property

        Public Shared ReadOnly Property SizeNS As Cursor
            Get
                Return Cursor.VgsNamed("ns-resize")
            End Get
        End Property

        Public Shared ReadOnly Property SizeNWSE As Cursor
            Get
                Return Cursor.VgsNamed("nwse-resize")
            End Get
        End Property

        Public Shared ReadOnly Property SizeWE As Cursor
            Get
                Return Cursor.VgsNamed("ew-resize")
            End Get
        End Property

        Public Shared ReadOnly Property UpArrow As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property WaitCursor As Cursor
            Get
                Return Cursor.VgsNamed("wait")
            End Get
        End Property

        Public Shared ReadOnly Property Help As Cursor
            Get
                Return Cursor.VgsNamed("help")
            End Get
        End Property

        Public Shared ReadOnly Property HSplit As Cursor
            Get
                Return Cursor.VgsNamed("row-resize")
            End Get
        End Property

        Public Shared ReadOnly Property VSplit As Cursor
            Get
                Return Cursor.VgsNamed("col-resize")
            End Get
        End Property

        Public Shared ReadOnly Property NoMove2D As Cursor
            Get
                Return Cursor.VgsNamed("all-scroll")
            End Get
        End Property

        Public Shared ReadOnly Property NoMoveHoriz As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property NoMoveVert As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanEast As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanNE As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanNorth As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanNW As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanSE As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanSouth As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanSW As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property PanWest As Cursor
            Get
                Return Cursor.VgsNamed("")
            End Get
        End Property

        Public Shared ReadOnly Property Hand As Cursor
            Get
                Return Cursor.VgsNamed("pointer")
            End Get
        End Property

    End Class
End Namespace
