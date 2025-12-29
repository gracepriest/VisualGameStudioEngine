' RaylibWrapper.vb - Visual Game Studio Engine
' Framework v1.0 / Engine v0.5 - Complete P/Invoke Wrapper
Imports System.Drawing
Imports System.Net.Mime.MediaTypeNames
Imports System.Numerics
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices

Public Module FrameworkWrapper

    ' Define the callback delegate
    Public Delegate Sub DrawCallback()

    Friend Const ENGINE_DLL As String = "VisualGameStudioEngine.dll"

#Region "Engine State & Lifecycle"
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

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetState() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pause()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Resume()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quit()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsPaused() As Boolean
    End Function
#End Region

#Region "Draw Control"
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

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawCircleLines(cx As Integer, cy As Integer, radius As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawRectangleLines(x As Integer, y As Integer, width As Integer, height As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawFPS(x As Integer, y As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawGrid(slices As Integer, spacing As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTriangle(x1 As Integer, y1 As Integer, x2 As Integer, y2 As Integer, x3 As Integer, y3 As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
#End Region

#Region "Timing"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetTargetFPS(fps As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFrameTime() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetDeltaTime() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetTime() As Double
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFPS() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFrameCount() As ULong
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetTimeScale(scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetTimeScale() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetFixedStep(seconds As Double)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResetFixedClock()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_StepFixed() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFixedStep() As Double
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetAccumulator() As Double
    End Function
#End Region

#Region "Input - Keyboard"
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
#End Region

#Region "Input - Mouse"
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
#End Region

#Region "Collisions"
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
    Public Function Framework_GetCollisionRec(rec1 As Rectangle, rec2 As Rectangle) As Rectangle
    End Function
#End Region

#Region "Textures (raw)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_LoadTexture(fileName As String) As Texture2D
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UnloadTexture(tex As Texture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsTextureValid(tex As Texture2D) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTexture(tex As Texture2D, x As Integer, y As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureV(tex As Texture2D, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureEx(tex As Texture2D, position As Vector2, rotation As Single, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureRec(tex As Texture2D, source As Rectangle, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTexturePro(tex As Texture2D, source As Rectangle, dest As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureNPatch(tex As Texture2D, nInfo As NPatchInfo, dest As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UpdateTexture(tex As Texture2D, pixels As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UpdateTextureRec(tex As Texture2D, rec As Rectangle, pixels As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_GenTextureMipmaps(ByRef tex As Texture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetTextureFilter(tex As Texture2D, filter As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetTextureWrap(tex As Texture2D, wrap As Integer)
    End Sub
#End Region

#Region "Textures (handle-based)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AcquireTextureH(path As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ReleaseTextureH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsTextureValidH(h As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureH(h As Integer, x As Integer, y As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureVH(h As Integer, pos As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureExH(h As Integer, pos As Vector2, rotation As Single, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureRecH(h As Integer, src As Rectangle, pos As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawTextureProH(h As Integer, src As Rectangle, dst As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetTextureWidth(h As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetTextureHeight(h As Integer) As Integer
    End Function
#End Region

#Region "Images"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_LoadImage(fileName As String) As Image
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UnloadImage(img As Image)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageColorInvert(ByRef img As Image)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageResize(ByRef img As Image, width As Integer, height As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageFlipVertical(ByRef img As Image)
    End Sub
#End Region

#Region "Render Textures & Camera2D (raw)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LoadRenderTexture(width As Integer, height As Integer) As RenderTexture2D
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UnloadRenderTexture(target As RenderTexture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsRenderTextureValid(target As RenderTexture2D) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_BeginTextureMode(target As RenderTexture2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_EndTextureMode()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_BeginMode2D(cam As Camera2D)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_EndMode2D()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_SpriteFrame(sheetArea As Rectangle, frameW As Integer, frameH As Integer, index As Integer, columns As Integer) As Rectangle
    End Function
#End Region

#Region "Camera2D (managed)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetPosition(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetTarget(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetRotation(rotation As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetZoom(zoom As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetOffset(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_GetPosition() As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_GetZoom() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_GetRotation() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_FollowEntity(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_BeginMode()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_EndMode()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_ScreenToWorld(screenX As Single, screenY As Single) As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_WorldToScreen(worldX As Single, worldY As Single) As Vector2
    End Function
#End Region

#Region "Fonts (raw)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_LoadFontEx(fileName As String, fontSize As Integer, glyphs As IntPtr, glyphCount As Integer) As Font
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UnloadFont(font As Font)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_DrawTextEx(font As Font, text As String, pos As Vector2, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
#End Region

#Region "Fonts (handle-based)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AcquireFontH(path As String, fontSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ReleaseFontH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsFontValidH(h As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_DrawTextExH(h As Integer, text As String, pos As Vector2, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
#End Region

#Region "Shaders"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_LoadShaderF(vsPath As String, fsPath As String) As Shader
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UnloadShader(sh As Shader)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_BeginShaderMode(sh As Shader)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_EndShaderMode()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_GetShaderLocation(sh As Shader, name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetShaderValue1f(sh As Shader, loc As Integer, v As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetShaderValue2f(sh As Shader, loc As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetShaderValue3f(sh As Shader, loc As Integer, x As Single, y As Single, z As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetShaderValue4f(sh As Shader, loc As Integer, x As Single, y As Single, z As Single, w As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetShaderValue1i(sh As Shader, loc As Integer, v As Integer)
    End Sub
#End Region

#Region "Audio"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_InitAudio() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_CloseAudio()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMasterVolume(volume As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMasterVolume() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_PauseAllAudio()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResumeAllAudio()
    End Sub

    ' Sounds
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_LoadSoundH(path As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UnloadSoundH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_PlaySoundH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_StopSoundH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_PauseSoundH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResumeSoundH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetSoundVolumeH(h As Integer, v As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetSoundPitchH(h As Integer, p As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetSoundPanH(h As Integer, pan As Single)
    End Sub

    ' Music
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AcquireMusicH(path As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ReleaseMusicH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMusicValidH(h As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_PlayMusicH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_StopMusicH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_PauseMusicH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResumeMusicH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMusicVolumeH(h As Integer, v As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMusicPitchH(h As Integer, p As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UpdateMusicH(h As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UpdateAllMusic()
    End Sub
#End Region

#Region "Asset Root"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_SetAssetRoot(path As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetAssetRoot() As IntPtr
    End Function
#End Region

#Region "Scene System"
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

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_SceneGetCurrent() As Integer
    End Function
#End Region

#Region "ECS - Entities"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_CreateEntity() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_DestroyEntity(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_IsAlive(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_ClearAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetEntityCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetAllEntities(buffer As Integer(), bufferSize As Integer) As Integer
    End Function
#End Region

#Region "ECS - Name Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Ecs_SetName(entity As Integer, name As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetName(entity As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasName(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Ecs_FindByName(name As String) As Integer
    End Function

    Public Function Ecs_GetName(entity As Integer) As String
        Dim ptr = Framework_Ecs_GetName(entity)
        If ptr = IntPtr.Zero Then Return ""
        Return Marshal.PtrToStringAnsi(ptr)
    End Function
#End Region

#Region "ECS - Tag Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Ecs_SetTag(entity As Integer, tag As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTag(entity As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasTag(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Ecs_FindAllByTag(tag As String, buffer As Integer(), bufferSize As Integer) As Integer
    End Function

    Public Function Ecs_GetTag(entity As Integer) As String
        Dim ptr = Framework_Ecs_GetTag(entity)
        If ptr = IntPtr.Zero Then Return ""
        Return Marshal.PtrToStringAnsi(ptr)
    End Function
#End Region

#Region "ECS - Enabled Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEnabled(entity As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_IsEnabled(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_IsActiveInHierarchy(entity As Integer) As Boolean
    End Function
#End Region

#Region "ECS - Hierarchy Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetParent(entity As Integer, parent As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetParent(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetFirstChild(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetNextSibling(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetChildCount(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetChildren(entity As Integer, buffer As Integer(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_DetachFromParent(entity As Integer)
    End Sub
#End Region

#Region "ECS - Transform2D Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AddTransform2D(entity As Integer, x As Single, y As Single, rotation As Single, sx As Single, sy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasTransform2D(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetTransformPosition(entity As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetTransformRotation(entity As Integer, rotation As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetTransformScale(entity As Integer, sx As Single, sy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTransformPosition(entity As Integer) As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTransformScale(entity As Integer) As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTransformRotation(entity As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetWorldPosition(entity As Integer) As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetWorldRotation(entity As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetWorldScale(entity As Integer) As Vector2
    End Function
#End Region

#Region "ECS - Velocity2D Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AddVelocity2D(entity As Integer, vx As Single, vy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasVelocity2D(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetVelocity(entity As Integer, vx As Single, vy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetVelocity(entity As Integer) As Vector2
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_RemoveVelocity2D(entity As Integer)
    End Sub
#End Region

#Region "ECS - BoxCollider2D Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AddBoxCollider2D(entity As Integer, offsetX As Single, offsetY As Single, width As Single, height As Single, isTrigger As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasBoxCollider2D(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetBoxCollider(entity As Integer, offsetX As Single, offsetY As Single, width As Single, height As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetBoxColliderTrigger(entity As Integer, isTrigger As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetBoxColliderWorldBounds(entity As Integer) As Rectangle
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_RemoveBoxCollider2D(entity As Integer)
    End Sub
#End Region

#Region "ECS - Sprite2D Component"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AddSprite2D(entity As Integer, textureHandle As Integer, srcX As Single, srcY As Single, srcW As Single, srcH As Single, r As Byte, g As Byte, b As Byte, a As Byte, layer As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasSprite2D(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetSpriteTint(entity As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetSpriteVisible(entity As Integer, visible As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetSpriteLayer(entity As Integer, layer As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetSpriteSource(entity As Integer, srcX As Single, srcY As Single, srcW As Single, srcH As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetSpriteTexture(entity As Integer, textureHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_RemoveSprite2D(entity As Integer)
    End Sub
#End Region

#Region "ECS - Systems"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_UpdateVelocities(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_DrawSprites()
    End Sub
#End Region

#Region "Physics - Overlap Queries"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_OverlapBox(x As Single, y As Single, w As Single, h As Single, buffer As Integer(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_OverlapCircle(x As Single, y As Single, radius As Single, buffer As Integer(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_CheckEntityOverlap(entityA As Integer, entityB As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetOverlappingEntities(entity As Integer, buffer As Integer(), bufferSize As Integer) As Integer
    End Function
#End Region

#Region "Debug Overlay"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Debug_IsEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_DrawEntityBounds(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_DrawHierarchy(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_DrawStats(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_Render()
    End Sub
#End Region

#Region "Prefabs & Serialization"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Scene_Save(path As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Scene_Load(path As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Prefab_Load(path As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Prefab_Instantiate(prefabH As Integer, parentEntity As Integer, x As Single, y As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Prefab_Unload(prefabH As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Prefab_SaveEntity(entity As Integer, path As String) As Boolean
    End Function
#End Region

#Region "Tilemap System"
    ' Tileset management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tileset_Create(textureHandle As Integer, tileWidth As Integer, tileHeight As Integer, columns As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tileset_Destroy(tilesetHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tileset_IsValid(tilesetHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tileset_GetTileWidth(tilesetHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tileset_GetTileHeight(tilesetHandle As Integer) As Integer
    End Function

    ' Tilemap component
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AddTilemap(entity As Integer, tilesetHandle As Integer, mapWidth As Integer, mapHeight As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasTilemap(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_RemoveTilemap(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetTile(entity As Integer, x As Integer, y As Integer, tileIndex As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTile(entity As Integer, x As Integer, y As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_FillTiles(entity As Integer, tileIndex As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetTileCollision(entity As Integer, tileIndex As Integer, solid As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTileCollision(entity As Integer, tileIndex As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTilemapWidth(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetTilemapHeight(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_DrawTilemap(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tilemaps_Draw()
    End Sub

    ' Tilemap collision queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tilemap_PointSolid(entity As Integer, worldX As Single, worldY As Single) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tilemap_BoxSolid(entity As Integer, worldX As Single, worldY As Single, w As Single, h As Single) As Boolean
    End Function
#End Region

#Region "Animation System"
    ' Animation clip management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AnimClip_Create(name As String, frameCount As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_AnimClip_Destroy(clipHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_AnimClip_IsValid(clipHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_AnimClip_SetFrame(clipHandle As Integer, frameIndex As Integer, srcX As Single, srcY As Single, srcW As Single, srcH As Single, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_AnimClip_SetLoopMode(clipHandle As Integer, loopMode As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_AnimClip_GetFrameCount(clipHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_AnimClip_GetTotalDuration(clipHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_AnimClip_FindByName(name As String) As Integer
    End Function

    ' Animator component
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AddAnimator(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasAnimator(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_RemoveAnimator(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetAnimatorClip(entity As Integer, clipHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_GetAnimatorClip(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AnimatorPlay(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AnimatorPause(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AnimatorStop(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AnimatorSetSpeed(entity As Integer, speed As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_AnimatorIsPlaying(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_AnimatorGetFrame(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AnimatorSetFrame(entity As Integer, frameIndex As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Animators_Update(dt As Single)
    End Sub
#End Region

#Region "Particle System"
    ' Particle emitter component
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_AddParticleEmitter(entity As Integer, textureHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_HasParticleEmitter(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_RemoveParticleEmitter(entity As Integer)
    End Sub

    ' Emitter configuration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterRate(entity As Integer, particlesPerSecond As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterLifetime(entity As Integer, minLife As Single, maxLife As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterVelocity(entity As Integer, minVx As Single, minVy As Single, maxVx As Single, maxVy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterColorStart(entity As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterColorEnd(entity As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterSize(entity As Integer, startSize As Single, endSize As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterGravity(entity As Integer, gx As Single, gy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterSpread(entity As Integer, angleDegrees As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterDirection(entity As Integer, dirX As Single, dirY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterMaxParticles(entity As Integer, maxParticles As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_SetEmitterSourceRect(entity As Integer, srcX As Single, srcY As Single, srcW As Single, srcH As Single)
    End Sub

    ' Emitter control
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_EmitterStart(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_EmitterStop(entity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_EmitterBurst(entity As Integer, count As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_EmitterIsActive(entity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Ecs_EmitterGetParticleCount(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Ecs_EmitterClear(entity As Integer)
    End Sub

    ' Particle systems update/draw
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Particles_Update(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Particles_Draw()
    End Sub
#End Region

#Region "UI System"
    ' UI Element lifecycle
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_UI_CreateLabel(text As String, x As Single, y As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_UI_CreateButton(text As String, x As Single, y As Single, width As Single, height As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_CreatePanel(x As Single, y As Single, width As Single, height As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_CreateSlider(x As Single, y As Single, width As Single, minVal As Single, maxVal As Single, initialVal As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_UI_CreateCheckbox(text As String, x As Single, y As Single, initialState As Boolean) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_UI_CreateTextInput(x As Single, y As Single, width As Single, height As Single, placeholder As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_CreateProgressBar(x As Single, y As Single, width As Single, height As Single, initialValue As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_CreateImage(textureHandle As Integer, x As Single, y As Single, width As Single, height As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_Destroy(elementId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_DestroyAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_IsValid(elementId As Integer) As Boolean
    End Function

    ' UI Element properties - Common
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetPosition(elementId As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetSize(elementId As Integer, width As Single, height As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetAnchor(elementId As Integer, anchor As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetVisible(elementId As Integer, visible As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetEnabled(elementId As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetParent(elementId As Integer, parentId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetLayer(elementId As Integer, layer As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetX(elementId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetY(elementId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetWidth(elementId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetHeight(elementId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetState(elementId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetType(elementId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_IsVisible(elementId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_IsEnabled(elementId As Integer) As Boolean
    End Function

    ' UI Element properties - Text/Font
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_UI_SetText(elementId As Integer, text As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetText(elementId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetFont(elementId As Integer, fontHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetFontSize(elementId As Integer, size As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetTextColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetTextAlign(elementId As Integer, anchor As Integer)
    End Sub

    ' UI Element properties - Colors
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetBackgroundColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetBorderColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetHoverColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetPressedColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetDisabledColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetBorderWidth(elementId As Integer, width As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetCornerRadius(elementId As Integer, radius As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetPadding(elementId As Integer, left As Single, top As Single, right As Single, bottom As Single)
    End Sub

    ' UI Element properties - Value-based
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetValue(elementId As Integer, value As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetValue(elementId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetMinMax(elementId As Integer, minVal As Single, maxVal As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetChecked(elementId As Integer, checked As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_IsChecked(elementId As Integer) As Boolean
    End Function

    ' UI Element properties - TextInput specific
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_UI_SetPlaceholder(elementId As Integer, text As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetMaxLength(elementId As Integer, maxLength As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetPasswordMode(elementId As Integer, isPassword As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetCursorPosition(elementId As Integer, position As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetCursorPosition(elementId As Integer) As Integer
    End Function

    ' UI Element properties - Image specific
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetTexture(elementId As Integer, textureHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetSourceRect(elementId As Integer, srcX As Single, srcY As Single, srcW As Single, srcH As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetTint(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    ' UI System update/draw
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_Update()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_Draw()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetHovered() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_GetFocused() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_SetFocus(elementId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_UI_HasFocus() As Boolean
    End Function

    ' UI Layout helpers
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_LayoutVertical(parentId As Integer, spacing As Single, paddingX As Single, paddingY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_UI_LayoutHorizontal(parentId As Integer, spacing As Single, paddingX As Single, paddingY As Single)
    End Sub

    ' UI Constants
    Public Const UI_LABEL As Integer = 0
    Public Const UI_BUTTON As Integer = 1
    Public Const UI_PANEL As Integer = 2
    Public Const UI_SLIDER As Integer = 3
    Public Const UI_CHECKBOX As Integer = 4
    Public Const UI_TEXTINPUT As Integer = 5
    Public Const UI_PROGRESSBAR As Integer = 6
    Public Const UI_IMAGE As Integer = 7

    Public Const UI_ANCHOR_TOP_LEFT As Integer = 0
    Public Const UI_ANCHOR_TOP_CENTER As Integer = 1
    Public Const UI_ANCHOR_TOP_RIGHT As Integer = 2
    Public Const UI_ANCHOR_CENTER_LEFT As Integer = 3
    Public Const UI_ANCHOR_CENTER As Integer = 4
    Public Const UI_ANCHOR_CENTER_RIGHT As Integer = 5
    Public Const UI_ANCHOR_BOTTOM_LEFT As Integer = 6
    Public Const UI_ANCHOR_BOTTOM_CENTER As Integer = 7
    Public Const UI_ANCHOR_BOTTOM_RIGHT As Integer = 8

    Public Const UI_STATE_NORMAL As Integer = 0
    Public Const UI_STATE_HOVERED As Integer = 1
    Public Const UI_STATE_PRESSED As Integer = 2
    Public Const UI_STATE_DISABLED As Integer = 3
    Public Const UI_STATE_FOCUSED As Integer = 4
#End Region

#Region "Cleanup"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResourcesShutdown()
    End Sub
#End Region

End Module
