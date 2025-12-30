Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy
Imports RaylibWrapper.UtiliyClasses

' TestVbDLL specific utilities - types come from RaylibWrapper.Utiliy
Public Module TestUtiliy

    ' Build source rects for a grid-based atlas.
    ' margin/spacing let you skip guide lines or padding if the sheet has them.
    Public Function SliceGrid(frameW As Integer, frameH As Integer,
                              columns As Integer, rows As Integer,
                              Optional marginX As Integer = 0, Optional marginY As Integer = 0,
                              Optional spacingX As Integer = 0, Optional spacingY As Integer = 0) As List(Of Rectangle)
        Dim rects As New List(Of Rectangle)
        For r = 0 To rows - 1
            For c = 0 To columns - 1
                Dim sx = marginX + c * (frameW + spacingX)
                Dim sy = marginY + r * (frameH + spacingY)
                rects.Add(New Rectangle(sx, sy, frameW, frameH))
            Next
        Next
        Return rects
    End Function

    ' Create a sprite frame class, to hold rectangle and offset and pivot
    Public Class SpriteFrame
        Public Property rect As Rectangle
        Public Property offset As Vector2
        Public Property pivot As Vector2
        Public Sub New(r As Rectangle, Optional off As Vector2 = Nothing, Optional piv As Vector2 = Nothing)
            Me.rect = r
            Me.offset = off
            If piv.x = 0 AndAlso piv.y = 0 Then
                Me.pivot = New Vector2(r.width / 2, r.height / 2)
            Else
                Me.pivot = piv
            End If
        End Sub
    End Class

    ' Sprite atlas class
    Public Class SpriteAtlas
        Public Property texture As TextureHandle
        Public Property frames As Dictionary(Of String, SpriteFrame) = New Dictionary(Of String, SpriteFrame)(StringComparer.OrdinalIgnoreCase)
        Public Sub New(tex As TextureHandle)
            texture = tex
        End Sub
        ' Add a frame
        Public Sub Add(name As String, rect As Rectangle, Optional offset As Vector2 = Nothing, Optional pivot As Vector2 = Nothing)
            frames.Add(name, New SpriteFrame(rect, offset, pivot))
        End Sub
        ' Add a frame from a spriteframe
        Public Sub Add(name As String, frame As SpriteFrame)
            frames.Add(name, frame)
        End Sub
        ' Get a frame
        Public Function GetFrame(name As String) As SpriteFrame
            If frames.ContainsKey(name) Then
                Return frames(name)
            Else
                Return Nothing
            End If
        End Function
    End Class

    ' Sprite class
    Public Class Sprite
        Public Property atlas As SpriteAtlas
        Public Property position As New Vector2(0, 0)
        Public Property scale As Single = 1.0F
        Public Property rotation As Single = 0
        Public Property _color As New Color(255, 255, 255, 255)
        Public Property _name As String

        Public Sub New(atlas As SpriteAtlas, frameName As String, position As Vector2)
            Me.atlas = atlas
            Me.position = position
            Me._name = frameName
        End Sub

        Public Sub Draw()
            Dim frame = atlas.GetFrame(_name)
            Dim dst As New Rectangle(position.x, position.y, frame.rect.width * scale, frame.rect.height * scale)
            Dim origin As New Vector2(frame.rect.width * scale / 2, frame.rect.height * scale / 2)
            If frame IsNot Nothing Then
                atlas.texture.DrawPro(frame.rect, dst, origin, rotation, 255, 255, 255, 255)
            End If
        End Sub

        Public Sub setScale(s As Single)
            scale = s
        End Sub

        Public Sub setRotation(r As Single)
            rotation = r
        End Sub

        Public Sub SetColor(r As Byte, g As Byte, b As Byte, a As Byte)
            _color = New Color(r, g, b, a)
        End Sub
    End Class

End Module
