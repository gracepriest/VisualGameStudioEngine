Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices

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