Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices

Public Module Utiliy
    Public Const WINDOW_WIDTH = 1200
    Public WINDOW_HEIGHT = 720

    <StructLayout(LayoutKind.Sequential)>
    Public Structure Color
        Public r As Byte
        Public g As Byte
        Public b As Byte
        Public a As Byte
        Public Sub New(r1 As Byte, g1 As Byte, b1 As Byte, a1 As Byte)
            Me.r = r1
            Me.g = g1
            Me.b = b1
            Me.a = a1
        End Sub
    End Structure
    'Enum for initialization results
    Enum InitResult
        INIT_OK = 0
        INIT_NO_WINDOW
        INIT_NO_AUDIO
        INIT_BAD_CONTEXT
    End Enum

    <StructLayout(LayoutKind.Sequential)>
    Public Structure SceneCallbacks
        Public onEnter As SceneVoidFn
        Public onExit As SceneVoidFn
        Public onResume As SceneVoidFn
        Public onUpdateFixed As SceneUpdateFixedFn
        Public onUpdateFrame As SceneUpdateFrameFn
        Public onDraw As SceneVoidFn
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Shader
        Public id As Integer
        Public locs As IntPtr  ' int* (opaque)
    End Structure
    ' Define structures used by the framework
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Vector2
        Public x As Single
        Public y As Single

        Public Sub New(x1 As Single, y1 As Single)
            Me.x = x1
            Me.y = y1
        End Sub
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure Rectangle
        Public x As Single
        Public y As Single
        Public width As Single
        Public height As Single

        Public Sub New(x1 As Single, y1 As Single, width1 As Single, height1 As Single)
            Me.x = x1
            Me.y = y1
            Me.width = width1
            Me.height = height1
        End Sub
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Texture2D
        Public id As UInteger   ' OpenGL texture id
        Public width As Integer
        Public height As Integer
        Public mipmaps As Integer
        Public format As Integer
    End Structure
    ' TextureCubemap == Texture/Texture2D in raylib
    <StructLayout(LayoutKind.Sequential)>
    Public Structure TextureCubemap
        Public id As UInteger
        Public width As Integer
        Public height As Integer
        Public mipmaps As Integer
        Public format As Integer
    End Structure

    ' CPU-side image (pixel pointer lives in C/C++; we treat as IntPtr)
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Image
        Public data As IntPtr
        Public width As Integer
        Public height As Integer
        Public mipmaps As Integer
        Public format As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure RenderTexture2D
        Public id As UInteger
        Public texture As Texture2D
        Public depth As Texture2D
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure NPatchInfo
        Public source As Rectangle
        Public left As Integer
        Public top As Integer
        Public right As Integer
        Public bottom As Integer
        Public layout As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure Camera2D
        Public offset As Vector2
        Public target As Vector2
        Public rotation As Single
        Public zoom As Single
    End Structure

    ' You only need enough of Font to pass it by value
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Font
        Public baseSize As Integer
        Public glyphCount As Integer
        Public glyphPadding As Integer
        Public texture As Texture2D
        Public recs As IntPtr      ' Rectangle* (opaque to VB)
        Public glyphs As IntPtr    ' GlyphInfo* (opaque to VB)
    End Structure

    ' raylib GlyphInfo — returned by value from Framework_GetGlyphInfo (raylib text Batch 2)
    <StructLayout(LayoutKind.Sequential)>
    Public Structure GlyphInfo
        Public value As Integer       ' Unicode codepoint
        Public offsetX As Integer
        Public offsetY As Integer
        Public advanceX As Integer
        Public image As Image
    End Structure

    ' raylib Vector3/Vector4 — returned/passed by value by raylib color/pixel fns (Batch 3a:
    ' ColorToHSV -> Vector3, ColorNormalize -> Vector4, ColorFromNormalized <- Vector4). Also
    ' needed by the later core/models batches.
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Vector3
        Public x As Single
        Public y As Single
        Public z As Single
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure Vector4
        Public x As Single
        Public y As Single
        Public z As Single
        Public w As Single
    End Structure

    ' raylib Matrix — 4x4, column-major (OpenGL style). Returned BY VALUE by the core screen-space helpers
    ' (GetCameraMatrix/GetCameraMatrix2D; core Batch C5). ⚠ raylib DECLARES the 16 floats in a scrambled order
    ' (m0,m4,m8,m12 / m1,m5,m9,m13 / …) — memory layout follows that order, so the fields MUST be listed in the
    ' same sequence for the sequential marshaling to line up byte-for-byte with the C struct.
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Matrix
        Public m0 As Single
        Public m4 As Single
        Public m8 As Single
        Public m12 As Single
        Public m1 As Single
        Public m5 As Single
        Public m9 As Single
        Public m13 As Single
        Public m2 As Single
        Public m6 As Single
        Public m10 As Single
        Public m14 As Single
        Public m3 As Single
        Public m7 As Single
        Public m11 As Single
        Public m15 As Single
    End Structure

    ' raylib Camera3D (aka Camera) — passed BY VALUE to the core screen-space / view-matrix helpers (Batch C5).
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Camera3D
        Public position As Vector3
        Public target As Vector3
        Public up As Vector3
        Public fovy As Single
        Public projection As Integer
    End Structure

    ' raylib Ray — origin + normalized direction. Returned BY VALUE by GetScreenToWorldRay(Ex) (Batch C5).
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Ray
        Public position As Vector3
        Public direction As Vector3
    End Structure

    ' raylib Wave — PCM audio data held in RAM (raudio module, Batch audio-A1). Returned/passed BY VALUE;
    ' WaveCrop/WaveFormat mutate it in place (ByRef). `data` is the raw sample buffer pointer.
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Wave
        Public frameCount As UInteger   ' Total number of frames (considering channels)
        Public sampleRate As UInteger   ' Frequency (samples per second)
        Public sampleSize As UInteger   ' Bit depth (bits per sample): 8, 16, 32
        Public channels As UInteger     ' Number of channels (1-mono, 2-stereo, ...)
        Public data As IntPtr           ' Buffer data pointer
    End Structure

    ' raylib AudioStream — handle to the audio system's internal buffer/processor (raudio module, Batch audio-A2).
    ' The two leading pointers are opaque (rAudioBuffer*/rAudioProcessor*); the struct is embedded BY VALUE in Sound/Music.
    <StructLayout(LayoutKind.Sequential)>
    Public Structure AudioStream
        Public buffer As IntPtr         ' Pointer to internal data used by the audio system
        Public processor As IntPtr      ' Pointer to internal data processor, useful for audio effects
        Public sampleRate As UInteger   ' Frequency (samples per second)
        Public sampleSize As UInteger   ' Bit depth (bits per sample): 8, 16, 32
        Public channels As UInteger     ' Number of channels (1-mono, 2-stereo, ...)
    End Structure

    ' raylib Sound — a playable sound backed by an AudioStream (raudio module, Batch audio-A2). Passed BY VALUE.
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Sound
        Public stream As AudioStream    ' Audio stream
        Public frameCount As UInteger   ' Total number of frames (considering channels)
    End Structure

    ' raylib Music — streaming audio backed by an AudioStream (raudio module, Batch audio-A3). Passed BY VALUE.
    ' Mirrors Sound (stream + frameCount) plus a `looping` flag and an opaque decoder-context tail. `looping` is a
    ' C bool (1 byte) → <MarshalAs(UnmanagedType.I1)> so the trailing ctxType/ctxData fields stay correctly aligned.
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Music
        Public stream As AudioStream                                ' Audio stream
        Public frameCount As UInteger                               ' Total number of frames (considering channels)
        <MarshalAs(UnmanagedType.I1)> Public looping As Boolean     ' Music looping enable
        Public ctxType As Integer                                   ' Type of music context (audio filetype)
        Public ctxData As IntPtr                                    ' Audio context data, depends on type
    End Structure

    Public Enum TextureFilter
        Point = 0
        Bilinear = 1
        Trilinear = 2
        Anisotropic4x = 3
        Anisotropic8x = 4
        Anisotropic16x = 5
    End Enum

    Public Enum TextureWrap
        Repeat_ = 0
        Clamp = 1
        MirrorRepeat = 2
        MirrorClamp = 3
    End Enum

    ' ===========================
    '     Convenience Helpers
    ' ===========================

    ' Safely upload a managed byte() to a GPU texture (no unsafe code in VB)
    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub Framework_UpdateTextureFromBytes(tex As Texture2D, data As Byte())
        Dim handle = GCHandle.Alloc(data, GCHandleType.Pinned)
        Try
            Framework_UpdateTexture(tex, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
    End Sub

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Sub Framework_UpdateTextureRecFromBytes(tex As Texture2D, rect As Rectangle, data As Byte())
        Dim handle = GCHandle.Alloc(data, GCHandleType.Pinned)
        Try
            Framework_UpdateTextureRec(tex, rect, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
    End Sub
    Public Enum Keys
        SPACE = 32
        BACKSPACE = 259
        ENTER = 257
        ESCAPE = 256
        UP = 265
        DOWN = 264
        LEFT = 263
        RIGHT = 262
        W = 87
        S = 83
        A = 65
        D = 68
        P = 80
        R = 82
        F = 70
        M = 77
        O = 79
        C = 67
        Q = 81
        E = 69
        T = 84
        L = 76
        N = 78
        G = 71
        H = 72
        U = 85
        X = 88
        Z = 90
        B = 66
        J = 74
        ONE = 49
        TWO = 50
        THREE = 51
        FOUR = 52
        FIVE = 53
        SIX = 54
        SEVEN = 55
        EIGHT = 56
        NINE = 57
        ZERO = 48
    End Enum
    <System.Runtime.CompilerServices.Extension>
    Public Sub DrawRectangle(rec As Rectangle, r As Byte, g As Byte, b As Byte, a As Byte)
        Framework_DrawRectangle(CInt(rec.X), CInt(rec.Y), rec.Width, rec.Height, r, g, b, a)
    End Sub

    ' Build source rects for a grid-based atlas.
    ' margin/spacing let you skip guide lines or padding if the sheet has them.
    Public Function SliceGrid(frameW As Integer, frameH As Integer,
                              columns As Integer, rows As Integer,
                              Optional marginX As Integer = 0, Optional marginY As Integer = 0,
                              Optional spacingX As Integer = 0, Optional spacingY As Integer = 0) As List(Of Utiliy.Rectangle)
        Dim rects As New List(Of Utiliy.Rectangle)
        For r = 0 To rows - 1
            For c = 0 To columns - 1
                Dim sx = marginX + c * (frameW + spacingX)
                Dim sy = marginY + r * (frameH + spacingY)
                rects.Add(New Utiliy.Rectangle(sx, sy, frameW, frameH))
            Next
        Next
        Return rects
    End Function
    'create a sprite frame class, to hold rectangle and offset and pivot
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
    ' ===========================
    ' sprite atlas class
    Public Class SpriteAtlas
        Public Property texture As TextureHandle
        'how do i add stringcomparer.orderedignorecase to this dictionary?
        Public Property frames As Dictionary(Of String, SpriteFrame) = New Dictionary(Of String, SpriteFrame)(StringComparer.OrdinalIgnoreCase)
        Public Sub New(tex As TextureHandle)
            texture = tex
        End Sub
        'add a frame
        Public Sub Add(name As String, rect As Rectangle, Optional offset As Vector2 = Nothing, Optional pivot As Vector2 = Nothing)
            frames.Add(name, New SpriteFrame(rect, offset, pivot))
        End Sub
        'add a frame from a spriteframe
        Public Sub Add(name As String, frame As SpriteFrame)
            frames.Add(name, frame)
        End Sub
        'get a frame
        Public Function GetFrame(name As String) As SpriteFrame
            If frames.ContainsKey(name) Then
                Return frames(name)
            Else
                Return Nothing
            End If
        End Function


    End Class
    ' ===========================
    'sprite class
    Public Class Sprite
        Public Property atlas As SpriteAtlas
        Public Property position As New Vector2(0, 0)
        Public Property scale As Single = 1.0F
        Public Property rotation As Single = 0
        Public Property _color As New Color(255, 255, 255, 255)
        Public Property _name As String
        'constructor
        Public Sub New(atlas As SpriteAtlas, frameName As String, position As Vector2)
            Me.atlas = atlas
            Me.position = position
            Me._name = frameName
        End Sub
        'draw the sprite
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
    ' ===========================
    'helper funtions
    Public Sub Framework_DrawTextureProX(tex As Texture2D, source As Rectangle, dest As Rectangle, origin As Vector2, rotation As Single, col As Color)
        Framework_DrawTexturePro(tex, source, dest, origin, rotation, col.r, col.g, col.b, col.a)
    End Sub
    'HANDLE VERSION OF DRAWTEXTUREPRO
    Public Sub Framework_DrawTextureProX(hanle As IntPtr, source As Rectangle, dest As Rectangle, origin As Vector2, rotation As Single, col As Color)
        Framework_DrawTextureProH(hanle, source, dest, origin, rotation, col.r, col.g, col.b, col.a)
    End Sub

End Module
