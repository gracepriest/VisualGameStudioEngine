Public Module UtiliyClasses
    Public NotInheritable Class TextureHandle
        Implements IDisposable

        Public ReadOnly Handle As Integer
        Public ReadOnly Path As String
        Private _disposed As Boolean

        Public Sub New(path As String)
            Me.Path = path
            Handle = Framework_AcquireTextureH(path)
            If Handle = 0 OrElse Not Framework_IsTextureValidH(Handle) Then
                ' release the bad handle (if any) and fail fast
                If Handle <> 0 Then Framework_ReleaseTextureH(Handle)
                Throw New IO.FileNotFoundException($"Texture not loaded: {path}")
            End If
        End Sub

        Public ReadOnly Property IsValid As Boolean
            Get
                Return Not _disposed AndAlso Framework_IsTextureValidH(Handle)
            End Get
        End Property

        ' Simple draw (your original)
        Public Sub Draw(x As Integer, y As Integer,
                    Optional r As Byte = 255, Optional g As Byte = 255,
                    Optional b As Byte = 255, Optional a As Byte = 255)
            If Not IsValid Then Exit Sub
            Framework_DrawTextureH(Handle, x, y, r, g, b, a)
        End Sub

        ' Vector position
        Public Sub DrawV(pos As Vector2,
                     Optional r As Byte = 255, Optional g As Byte = 255,
                     Optional b As Byte = 255, Optional a As Byte = 255)
            If Not IsValid Then Exit Sub
            Framework_DrawTextureVH(Handle, pos, r, g, b, a)
        End Sub

        ' Rotation + scale
        Public Sub DrawEx(pos As Vector2, rotation As Single, scale As Single,
                      Optional r As Byte = 255, Optional g As Byte = 255,
                      Optional b As Byte = 255, Optional a As Byte = 255)
            If Not IsValid Then Exit Sub
            Framework_DrawTextureExH(Handle, pos, rotation, scale, r, g, b, a)
        End Sub

        ' Source rect (sprite sheets)
        Public Sub DrawRec(src As Rectangle, pos As Vector2,
                       Optional r As Byte = 255, Optional g As Byte = 255,
                       Optional b As Byte = 255, Optional a As Byte = 255)
            If Not IsValid Then Exit Sub
            Framework_DrawTextureRecH(Handle, src, pos, r, g, b, a)
        End Sub

        ' Full control (dest rect, origin, rotation)
        Public Sub DrawPro(src As Rectangle, dst As Rectangle, origin As Vector2, rotation As Single,
                       Optional r As Byte = 255, Optional g As Byte = 255,
                       Optional b As Byte = 255, Optional a As Byte = 255)
            If Not IsValid Then Exit Sub
            Framework_DrawTextureProH(Handle, src, dst, origin, rotation, r, g, b, a)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If _disposed Then Return
            Framework_ReleaseTextureH(Handle)
            _disposed = True
        End Sub
    End Class
    Public NotInheritable Class FontHandle
        Implements IDisposable
        Public ReadOnly Handle As Integer
        Public Sub New(path As String, size As Integer)
            Handle = Framework_AcquireFontH(path, size)
        End Sub
        Public Sub DrawText(text As String, pos As Vector2, size As Single, spacing As Single, Optional r As Byte = 255, Optional g As Byte = 255, Optional b As Byte = 255, Optional a As Byte = 255)
            Framework_DrawTextExH(Handle, text, pos, size, spacing, r, g, b, a)
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Framework_ReleaseFontH(Handle)
        End Sub
    End Class

    Public NotInheritable Class MusicHandle
        Implements IDisposable
        Public ReadOnly Handle As Integer
        Public Sub New(path As String)
            Handle = Framework_AcquireMusicH(path)
        End Sub
        Public Sub Play()
            Framework_PlayMusicH(Handle)
        End Sub
        Public Sub Pause()
            Framework_PauseMusicH(Handle)
        End Sub
        Public Sub [Stop]()
            Framework_StopMusicH(Handle)
        End Sub
        Public Sub ResumePlayback()
            Framework_ResumeMusicH(Handle)
        End Sub
        Public Sub Volume(v As Single)
            Framework_SetMusicVolumeH(Handle, v)
        End Sub
        Public Sub Pitch(p As Single)
            Framework_SetMusicPitchH(Handle, p)
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Framework_ReleaseMusicH(Handle)
        End Sub
    End Class
    Public Class SpriteAnim
        Public FrameW As Integer, FrameH As Integer, Columns As Integer, Count As Integer, Fps As Single
        Private _time As Single, _index As Integer
        Public Sub New(w As Integer, h As Integer, cols As Integer, count As Integer, fps As Single)
            FrameW = w : FrameH = h : Columns = cols : count = count : fps = fps
        End Sub
        Public Sub Update(dt As Single)
            _time += dt
            While _time >= 1.0F / Fps
                _time -= 1.0F / Fps
                _index = (_index + 1) Mod Count
            End While
        End Sub
        Public Function SourceRect(sheet As Rectangle) As Rectangle
            Return Framework_SpriteFrame(sheet, FrameW, FrameH, _index, Columns)
        End Function
    End Class
End Module
