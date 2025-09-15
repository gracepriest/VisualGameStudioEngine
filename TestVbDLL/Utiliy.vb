Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices

Public Module Utiliy
    Public Const WINDOW_WIDTH = 1200
    Public WINDOW_HEIGHT = 720
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


End Module
