Imports System.Drawing
Imports System.Net.Mime.MediaTypeNames
Imports System.Numerics
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices

Public Module FrameworkWrapper


    ' Define the callback delegate
    Public Delegate Sub DrawCallback()

    ' RaylibWrapper.vb (top of the module)
    Friend Const ENGINE_DLL As String = "C:\Users\melvi\source\repos\VisualGameStudioEngine\x64\Release\VisualGameStudioEngine.dll"

    ' Window management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Initialize(width As Integer, height As Integer, title As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Update()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_ShouldClose() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shutdown()
    End Sub

    ' Callback functions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetDrawCallback(callback As DrawCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_BeginDrawing()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_EndDrawing()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ClearBackground(r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawText(text As String, x As Integer, y As Integer, fontSize As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawRectangle(x As Integer, y As Integer, width As Integer, height As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawLine(startPosX As Integer, startPosY As Integer, endPosX As Integer, endPosY As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawCircle(startPosX As Integer, startPosY As Integer, endPosX As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    ' Keyboard input
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyPressed(key As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyPressedRepeat(key As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyDown(key As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyReleased(key As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyUp(key As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetKeyPressed() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetCharPressed() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetExitKey(key As Integer)
    End Sub

    ' Mouse input
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseX() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseY() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonPressed(button As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonDown(button As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonReleased(button As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonUp(button As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMousePosition() As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseDelta() As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMousePosition(x As Integer, y As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMouseOffset(offsetX As Integer, offsetY As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMouseScale(scaleX As Single, scaleY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseWheelMove() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseWheelMoveV() As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMouseCursor(cursor As Integer)
    End Sub

    ' Cursor-related functions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ShowCursor()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_HideCursor()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsCursorHidden() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_EnableCursor()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DisableCursor()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsCursorOnScreen() As Boolean
    End Function

    ' Timing-related functions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetTargetFPS(fps As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFrameTime() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetTime() As Double
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFPS() As Integer
    End Function

    ' Collision detection functions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionRecs(rec1 As Rectangle, rec2 As Rectangle) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionCircles(center1 As Vector2, radius1 As Single, center2 As Vector2, radius2 As Single) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionCircleRec(center As Vector2, radius As Single, rec As Rectangle) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionCircleLine(center As Vector2, radius As Single, p1 As Vector2, p2 As Vector2) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointRec(point As Vector2, rec As Rectangle) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointCircle(point As Vector2, center As Vector2, radius As Single) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointTriangle(point As Vector2, p1 As Vector2, p2 As Vector2, p3 As Vector2) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointLine(point As Vector2, p1 As Vector2, p2 As Vector2, threshold As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointPoly(point As Vector2, points As Vector2(), pointCount As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionLines(startPos1 As Vector2, endPos1 As Vector2, startPos2 As Vector2, endPos2 As Vector2, ByRef collisionPoint As Vector2) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetCollisionRec(rec1 As Rectangle, rec2 As Rectangle) As Rectangle
    End Function










    ' ===========================
    '       Texture API
    ' ===========================

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi, EntryPoint:="Framework_LoadTexture")>
    Public Function Framework_LoadTexture(fileName As String) As Texture2D
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_UnloadTexture")>
    Public Sub Framework_UnloadTexture(tex As Texture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_IsTextureValid")>
    Public Function Framework_IsTextureValid(tex As Texture2D) As Boolean
    End Function

    ' DrawTexture family (RGBA bytes for tint)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_DrawTexture")>
    Public Sub Framework_DrawTexture(tex As Texture2D, x As Integer, y As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_DrawTextureV")>
    Public Sub Framework_DrawTextureV(tex As Texture2D, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_DrawTextureEx")>
    Public Sub Framework_DrawTextureEx(tex As Texture2D, position As Vector2, rotation As Single, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_DrawTextureRec")>
    Public Sub Framework_DrawTextureRec(tex As Texture2D, source As Rectangle, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_DrawTexturePro")>
    Public Sub Framework_DrawTexturePro(tex As Texture2D, source As Rectangle, dest As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_DrawTextureNPatch")>
    Public Sub Framework_DrawTextureNPatch(tex As Texture2D, nInfo As NPatchInfo, dest As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    ' Updates & config
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_UpdateTexture")>
    Public Sub Framework_UpdateTexture(tex As Texture2D, pixels As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_UpdateTextureRec")>
    Public Sub Framework_UpdateTextureRec(tex As Texture2D, rec As Rectangle, pixels As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_GenTextureMipmaps")>
    Public Sub Framework_GenTextureMipmaps(ByRef tex As Texture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_SetTextureFilter")>
    Public Sub Framework_SetTextureFilter(tex As Texture2D, filter As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_SetTextureWrap")>
    Public Sub Framework_SetTextureWrap(tex As Texture2D, wrap As Integer)
    End Sub

    ' ===========================
    '       Image API
    ' ===========================

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi, EntryPoint:="Framework_LoadImage")>
    Public Function Framework_LoadImage(fileName As String) As Image
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_UnloadImage")>
    Public Sub Framework_UnloadImage(img As Image)
    End Sub

    ' Mutating image ops take ByRef to pass pointer to C++
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_ImageColorInvert")>
    Public Sub Framework_ImageColorInvert(ByRef img As Image)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_ImageResize")>
    Public Sub Framework_ImageResize(ByRef img As Image, width As Integer, height As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_ImageFlipVertical")>
    Public Sub Framework_ImageFlipVertical(ByRef img As Image)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_LoadTextureFromImage")>
    Public Function Framework_LoadTextureFromImage(img As Image) As Texture2D
    End Function

    ' ===========================
    '    RenderTexture & Camera
    ' ===========================

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_LoadRenderTexture")>
    Public Function Framework_LoadRenderTexture(width As Integer, height As Integer) As RenderTexture2D
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_UnloadRenderTexture")>
    Public Sub Framework_UnloadRenderTexture(target As RenderTexture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_IsRenderTextureValid")>
    Public Function Framework_IsRenderTextureValid(target As RenderTexture2D) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_BeginTextureMode")>
    Public Sub Framework_BeginTextureMode(target As RenderTexture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_EndTextureMode")>
    Public Sub Framework_EndTextureMode()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_BeginMode2D")>
    Public Sub Framework_BeginMode2D(cam As Camera2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_EndMode2D")>
    Public Sub Framework_EndMode2D()
    End Sub

    ' Handy helper to compute a frame rectangle in a sheet
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_SpriteFrame")>
    Public Function Framework_SpriteFrame(sheetArea As Rectangle, frameW As Integer, frameH As Integer, index As Integer, columns As Integer) As Rectangle
    End Function

    ' ===========================
    '         Fonts / Text
    ' ===========================

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi, EntryPoint:="Framework_LoadFontEx")>
    Public Function Framework_LoadFontEx(fileName As String, fontSize As Integer, glyphs As IntPtr, glyphCount As Integer) As Font
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="Framework_UnloadFont")>
    Public Sub Framework_UnloadFont(font As Font)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi, EntryPoint:="Framework_DrawTextEx")>
    Public Sub Framework_DrawTextEx(font As Font, text As String, pos As Vector2, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub


    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetFixedStep(seconds As Double) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_ResetFixedClock() : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Function Framework_StepFixed() As Boolean : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Function Framework_GetFixedStep() As Double : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Function Framework_GetAccumulator() As Double : End Function




    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_DrawFPS(x As Integer, y As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_DrawGrid(slices As Integer, spacing As Single) : End Sub
    ' ===== Audio =====
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Function Framework_InitAudio() As Boolean : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_CloseAudio() : End Sub

    ' Sound (SFX)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)> Public Function Framework_LoadSoundH(path As String) As Integer : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_UnloadSoundH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_PlaySoundH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_StopSoundH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_PauseSoundH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_ResumeSoundH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetSoundVolumeH(h As Integer, v As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetSoundPitchH(h As Integer, p As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetSoundPanH(h As Integer, pan As Single) : End Sub




    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi, EntryPoint:="Framework_LoadShaderF")>
    Public Function Framework_LoadShaderF(vsPath As String, fsPath As String) As Shader
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_UnloadShader(sh As Shader) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_BeginShaderMode(sh As Shader) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_EndShaderMode() : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)> Public Function Framework_GetShaderLocation(sh As Shader, name As String) As Integer : End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetShaderValue1f(sh As Shader, loc As Integer, v As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetShaderValue2f(sh As Shader, loc As Integer, x As Single, y As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetShaderValue3f(sh As Shader, loc As Integer, x As Single, y As Single, z As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetShaderValue4f(sh As Shader, loc As Integer, x As Single, y As Single, z As Single, w As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)> Public Sub Framework_SetShaderValue1i(sh As Shader, loc As Integer, v As Integer) : End Sub

    '===== Textures (handle-based) =====
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AcquireTextureH(path As String) As Integer : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ReleaseTextureH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsTextureValidH(h As Integer) As Boolean : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureH(h As Integer, x As Integer, y As Integer, r As Byte, g As Byte, b As Byte, a As Byte) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureVH(h As Integer, pos As Vector2, r As Byte, g As Byte, b As Byte, a As Byte) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureExH(h As Integer, pos As Vector2, rotation As Single, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureRecH(h As Integer, src As Rectangle, pos As Vector2, r As Byte, g As Byte, b As Byte, a As Byte) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureProH(h As Integer, src As Rectangle, dst As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte) : End Sub

    ' ===== Fonts (handle-based) =====
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AcquireFontH(path As String, fontSize As Integer) As Integer : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ReleaseFontH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsFontValidH(h As Integer) As Boolean : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_DrawTextExH(h As Integer, text As String, pos As Vector2, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte) : End Sub

    ' ===== Music (handle-based) =====
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AcquireMusicH(path As String) As Integer : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ReleaseMusicH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMusicValidH(h As Integer) As Boolean : End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_PlayMusicH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_StopMusicH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_PauseMusicH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResumeMusicH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMusicVolumeH(h As Integer, v As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMusicPitchH(h As Integer, p As Single) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UpdateMusicH(h As Integer) : End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UpdateAllMusic() : End Sub

    ' ===== Unified resources shutdown =====
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResourcesShutdown() : End Sub





    ' ==== Scene callbacks ====
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub SceneVoidFn()
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub SceneUpdateFixedFn(dt As Double)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub SceneUpdateFrameFn(dt As Single)



    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CreateScriptScene(cb As SceneCallbacks) As Integer
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DestroyScene(sceneHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SceneChange(sceneHandle As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ScenePush(sceneHandle As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ScenePop()
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_SceneHas() As Boolean
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SceneTick()
    End Sub

End Module




Public Module Utiliy
    Public Const WINDOW_WIDTH = 1200
    Public WINDOW_HEIGHT = 720

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
        Framework_DrawRectangle(CInt(rec.x), CInt(rec.y), rec.width, rec.height, r, g, b, a)
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
