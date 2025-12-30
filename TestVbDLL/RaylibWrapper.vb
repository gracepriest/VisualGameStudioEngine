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

    ' Enhanced Camera - Smooth follow
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetFollowTarget(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetFollowLerp(lerpSpeed As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_GetFollowLerp() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetFollowEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_IsFollowEnabled() As Boolean
    End Function

    ' Enhanced Camera - Deadzone
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetDeadzone(width As Single, height As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_GetDeadzone(ByRef width As Single, ByRef height As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetDeadzoneEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_IsDeadzoneEnabled() As Boolean
    End Function

    ' Enhanced Camera - Look-ahead
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetLookahead(distance As Single, smoothing As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetLookaheadEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetLookaheadVelocity(vx As Single, vy As Single)
    End Sub

    ' Enhanced Camera - Screen shake
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_Shake(intensity As Single, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_ShakeEx(intensity As Single, duration As Single, frequency As Single, decay As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_StopShake()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_IsShaking() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_GetShakeIntensity() As Single
    End Function

    ' Enhanced Camera - Bounds
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetBounds(minX As Single, minY As Single, maxX As Single, maxY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_GetBounds(ByRef minX As Single, ByRef minY As Single, ByRef maxX As Single, ByRef maxY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetBoundsEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_IsBoundsEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_ClearBounds()
    End Sub

    ' Enhanced Camera - Zoom controls
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_SetZoomLimits(minZoom As Single, maxZoom As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_ZoomTo(targetZoom As Single, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_ZoomAt(targetZoom As Single, worldX As Single, worldY As Single, duration As Single)
    End Sub

    ' Enhanced Camera - Rotation
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_RotateTo(targetRotation As Single, duration As Single)
    End Sub

    ' Enhanced Camera - Pan/move
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_PanTo(worldX As Single, worldY As Single, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_PanBy(deltaX As Single, deltaY As Single, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_IsPanning() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_StopPan()
    End Sub

    ' Enhanced Camera - Flash effect
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_Flash(r As Byte, g As Byte, b As Byte, a As Byte, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Camera_IsFlashing() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_DrawFlash()
    End Sub

    ' Enhanced Camera - Update and Reset
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_Update(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Camera_Reset()
    End Sub
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

#Region "Scene Manager - Transitions & Loading Screens"
    ' Scene Transition Types
    Public Enum SceneTransitionType As Integer
        TRANSITION_NONE = 0
        TRANSITION_FADE = 1
        TRANSITION_FADE_WHITE = 2
        TRANSITION_SLIDE_LEFT = 3
        TRANSITION_SLIDE_RIGHT = 4
        TRANSITION_SLIDE_UP = 5
        TRANSITION_SLIDE_DOWN = 6
        TRANSITION_WIPE_LEFT = 7
        TRANSITION_WIPE_RIGHT = 8
        TRANSITION_WIPE_UP = 9
        TRANSITION_WIPE_DOWN = 10
        TRANSITION_CIRCLE_IN = 11
        TRANSITION_CIRCLE_OUT = 12
        TRANSITION_PIXELATE = 13
        TRANSITION_DISSOLVE = 14
    End Enum

    ' Transition Easing Types
    Public Enum TransitionEasing As Integer
        EASE_LINEAR = 0
        EASE_IN_QUAD = 1
        EASE_OUT_QUAD = 2
        EASE_IN_OUT_QUAD = 3
        EASE_IN_CUBIC = 4
        EASE_OUT_CUBIC = 5
        EASE_IN_OUT_CUBIC = 6
        EASE_IN_EXPO = 7
        EASE_OUT_EXPO = 8
        EASE_IN_OUT_EXPO = 9
    End Enum

    ' Transition State
    Public Enum TransitionState As Integer
        TRANS_STATE_NONE = 0
        TRANS_STATE_OUT = 1
        TRANS_STATE_LOADING = 2
        TRANS_STATE_IN = 3
    End Enum

    ' Scene Manager Callbacks
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub LoadingCallback(progress As Single)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub LoadingDrawCallback()

    ' Transition Configuration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetTransition(transitionType As Integer, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetTransitionEx(transitionType As Integer, duration As Single, easing As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetTransitionColor(r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetTransitionType() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetTransitionDuration() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetTransitionEasing() As Integer
    End Function

    ' Scene Change with Transition
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_ChangeWithTransition(sceneHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_ChangeWithTransitionEx(sceneHandle As Integer, transitionType As Integer, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_PushWithTransition(sceneHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_PopWithTransition()
    End Sub

    ' Transition State Queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_IsTransitioning() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetTransitionState() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetTransitionProgress() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SkipTransition()
    End Sub

    ' Loading Screen
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetLoadingEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_IsLoadingEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetLoadingMinDuration(seconds As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetLoadingMinDuration() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetLoadingCallback(callback As LoadingCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetLoadingDrawCallback(callback As LoadingDrawCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_SetLoadingProgress(progress As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetLoadingProgress() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_IsLoading() As Boolean
    End Function

    ' Scene Stack Queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetStackSize() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetSceneAt(index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_GetPreviousScene() As Integer
    End Function

    ' Scene Update (handles transitions, loading, and scene ticks)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_Update(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_Draw()
    End Sub

    ' Preloading
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_PreloadStart(sceneHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Scene_IsPreloading() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Scene_PreloadCancel()
    End Sub
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

#Region "Physics System"
    ' World settings
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetGravity(gx As Single, gy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_GetGravity(ByRef gx As Single, ByRef gy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetIterations(velocityIterations As Integer, positionIterations As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_IsEnabled() As Boolean
    End Function

    ' Body creation/destruction
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_CreateBody(bodyType As Integer, x As Single, y As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_DestroyBody(bodyHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_IsBodyValid(bodyHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_DestroyAllBodies()
    End Sub

    ' Body type
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyType(bodyHandle As Integer, bodyType As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyType(bodyHandle As Integer) As Integer
    End Function

    ' Body transform
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyPosition(bodyHandle As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_GetBodyPosition(bodyHandle As Integer, ByRef x As Single, ByRef y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyRotation(bodyHandle As Integer, radians As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyRotation(bodyHandle As Integer) As Single
    End Function

    ' Body dynamics
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyVelocity(bodyHandle As Integer, vx As Single, vy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_GetBodyVelocity(bodyHandle As Integer, ByRef vx As Single, ByRef vy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyAngularVelocity(bodyHandle As Integer, omega As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyAngularVelocity(bodyHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_ApplyForce(bodyHandle As Integer, fx As Single, fy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_ApplyForceAtPoint(bodyHandle As Integer, fx As Single, fy As Single, px As Single, py As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_ApplyImpulse(bodyHandle As Integer, ix As Single, iy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_ApplyTorque(bodyHandle As Integer, torque As Single)
    End Sub

    ' Body properties
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyMass(bodyHandle As Integer, mass As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyMass(bodyHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyRestitution(bodyHandle As Integer, restitution As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyRestitution(bodyHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyFriction(bodyHandle As Integer, friction As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyFriction(bodyHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyGravityScale(bodyHandle As Integer, scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyGravityScale(bodyHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyLinearDamping(bodyHandle As Integer, damping As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyAngularDamping(bodyHandle As Integer, damping As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyFixedRotation(bodyHandle As Integer, fixed As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_IsBodyFixedRotation(bodyHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodySleepingAllowed(bodyHandle As Integer, allowed As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_WakeBody(bodyHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_IsBodyAwake(bodyHandle As Integer) As Boolean
    End Function

    ' Collision shapes
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyCircle(bodyHandle As Integer, radius As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyCircleOffset(bodyHandle As Integer, radius As Single, offsetX As Single, offsetY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyBox(bodyHandle As Integer, width As Single, height As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyBoxOffset(bodyHandle As Integer, width As Single, height As Single, offsetX As Single, offsetY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyPolygon(bodyHandle As Integer, vertices As Single(), vertexCount As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyShapeType(bodyHandle As Integer) As Integer
    End Function

    ' Collision filtering
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyLayer(bodyHandle As Integer, layer As UInteger)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyMask(bodyHandle As Integer, mask As UInteger)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyTrigger(bodyHandle As Integer, isTrigger As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_IsBodyTrigger(bodyHandle As Integer) As Boolean
    End Function

    ' Entity binding
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_BindToEntity(bodyHandle As Integer, entityId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBoundEntity(bodyHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetEntityBody(entityId As Integer) As Integer
    End Function

    ' User data
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetBodyUserData(bodyHandle As Integer, userData As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_GetBodyUserData(bodyHandle As Integer) As Integer
    End Function

    ' Physics queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_RaycastFirst(startX As Single, startY As Single, dirX As Single, dirY As Single, maxDist As Single,
        ByRef hitX As Single, ByRef hitY As Single, ByRef hitNormalX As Single, ByRef hitNormalY As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_RaycastAll(startX As Single, startY As Single, dirX As Single, dirY As Single, maxDist As Single,
        bodyBuffer As Integer(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_QueryCircle(x As Single, y As Single, radius As Single, bodyBuffer As Integer(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_QueryBox(x As Single, y As Single, width As Single, height As Single, bodyBuffer As Integer(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_TestOverlap(bodyA As Integer, bodyB As Integer) As Boolean
    End Function

    ' Simulation
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_Step(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SyncToEntities()
    End Sub

    ' Debug rendering
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_SetDebugDraw(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Physics_IsDebugDrawEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Physics_DrawDebug()
    End Sub

    ' Physics body type constants
    Public Const BODY_STATIC As Integer = 0
    Public Const BODY_DYNAMIC As Integer = 1
    Public Const BODY_KINEMATIC As Integer = 2

    ' Collision shape type constants
    Public Const SHAPE_CIRCLE As Integer = 0
    Public Const SHAPE_BOX As Integer = 1
    Public Const SHAPE_POLYGON As Integer = 2
#End Region

#Region "Audio Manager"
    ' Group volume control
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetGroupVolume(group As Integer, volume As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_GetGroupVolume(group As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetGroupMuted(group As Integer, muted As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_IsGroupMuted(group As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_FadeGroupVolume(group As Integer, targetVolume As Single, duration As Single)
    End Sub

    ' Sound with group assignment
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Audio_LoadSound(path As String, group As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_UnloadSound(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaySound(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaySoundEx(handle As Integer, volume As Single, pitch As Single, pan As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_StopSound(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetSoundGroup(handle As Integer, group As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_GetSoundGroup(handle As Integer) As Integer
    End Function

    ' Spatial audio
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetListenerPosition(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_GetListenerPosition(ByRef x As Single, ByRef y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaySoundAt(handle As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaySoundAtEx(handle As Integer, x As Single, y As Single, volume As Single, pitch As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetSpatialFalloff(minDist As Single, maxDist As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetSpatialEnabled(enabled As Boolean)
    End Sub

    ' Sound pooling
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Audio_CreatePool(path As String, poolSize As Integer, group As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_DestroyPool(poolHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlayFromPool(poolHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlayFromPoolAt(poolHandle As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlayFromPoolEx(poolHandle As Integer, volume As Single, pitch As Single, pan As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_StopPool(poolHandle As Integer)
    End Sub

    ' Music with advanced features
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Audio_LoadMusic(path As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_UnloadMusic(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlayMusic(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_StopMusic(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PauseMusic(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_ResumeMusic(handle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetMusicVolume(handle As Integer, volume As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetMusicPitch(handle As Integer, pitch As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SetMusicLooping(handle As Integer, looping As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_IsMusicPlaying(handle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_GetMusicLength(handle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_GetMusicPosition(handle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_SeekMusic(handle As Integer, position As Single)
    End Sub

    ' Music crossfading
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_CrossfadeTo(newMusicHandle As Integer, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_FadeOutMusic(handle As Integer, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_FadeInMusic(handle As Integer, duration As Single, targetVolume As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_IsCrossfading() As Boolean
    End Function

    ' Playlist system
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_CreatePlaylist() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_DestroyPlaylist(playlistHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistAdd(playlistHandle As Integer, musicHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistRemove(playlistHandle As Integer, index As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistClear(playlistHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistPlay(playlistHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistStop(playlistHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistNext(playlistHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistPrev(playlistHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistSetShuffle(playlistHandle As Integer, shuffle As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistSetRepeat(playlistHandle As Integer, mode As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_PlaylistGetCurrent(playlistHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Audio_PlaylistGetCount(playlistHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_PlaylistSetCrossfade(playlistHandle As Integer, duration As Single)
    End Sub

    ' Audio manager update
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Audio_Update(dt As Single)
    End Sub

    ' Audio group constants
    Public Const AUDIO_GROUP_MASTER As Integer = 0
    Public Const AUDIO_GROUP_MUSIC As Integer = 1
    Public Const AUDIO_GROUP_SFX As Integer = 2
    Public Const AUDIO_GROUP_VOICE As Integer = 3
    Public Const AUDIO_GROUP_AMBIENT As Integer = 4
#End Region

#Region "Input Manager"
    ' Input source type constants
    Public Const INPUT_SOURCE_KEYBOARD As Integer = 0
    Public Const INPUT_SOURCE_MOUSE_BUTTON As Integer = 1
    Public Const INPUT_SOURCE_MOUSE_AXIS As Integer = 2
    Public Const INPUT_SOURCE_GAMEPAD_BUTTON As Integer = 3
    Public Const INPUT_SOURCE_GAMEPAD_AXIS As Integer = 4
    Public Const INPUT_SOURCE_GAMEPAD_TRIGGER As Integer = 5

    ' Mouse axis constants
    Public Const MOUSE_AXIS_X As Integer = 0
    Public Const MOUSE_AXIS_Y As Integer = 1
    Public Const MOUSE_AXIS_WHEEL As Integer = 2
    Public Const MOUSE_AXIS_WHEEL_H As Integer = 3

    ' Gamepad axis constants (FW_ prefix to avoid raylib conflict)
    Public Const FW_GAMEPAD_AXIS_LEFT_X As Integer = 0
    Public Const FW_GAMEPAD_AXIS_LEFT_Y As Integer = 1
    Public Const FW_GAMEPAD_AXIS_RIGHT_X As Integer = 2
    Public Const FW_GAMEPAD_AXIS_RIGHT_Y As Integer = 3
    Public Const FW_GAMEPAD_AXIS_LEFT_TRIGGER As Integer = 4
    Public Const FW_GAMEPAD_AXIS_RIGHT_TRIGGER As Integer = 5

    ' Action management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Input_CreateAction(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_DestroyAction(actionHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Input_GetAction(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsActionValid(actionHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_ClearAllActions()
    End Sub

    ' Keyboard bindings
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_BindKey(actionHandle As Integer, keyCode As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_UnbindKey(actionHandle As Integer, keyCode As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_ClearKeyBindings(actionHandle As Integer)
    End Sub

    ' Mouse button bindings
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_BindMouseButton(actionHandle As Integer, button As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_UnbindMouseButton(actionHandle As Integer, button As Integer)
    End Sub

    ' Gamepad button bindings
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_BindGamepadButton(actionHandle As Integer, button As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_UnbindGamepadButton(actionHandle As Integer, button As Integer)
    End Sub

    ' Axis bindings
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_BindMouseAxis(actionHandle As Integer, axis As Integer, scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_BindGamepadAxis(actionHandle As Integer, axis As Integer, scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_ClearAxisBindings(actionHandle As Integer)
    End Sub

    ' Action state queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsActionPressed(actionHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsActionDown(actionHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsActionReleased(actionHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetActionValue(actionHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetActionRawValue(actionHandle As Integer) As Single
    End Function

    ' Action configuration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_SetActionDeadzone(actionHandle As Integer, deadzone As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetActionDeadzone(actionHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_SetActionSensitivity(actionHandle As Integer, sensitivity As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetActionSensitivity(actionHandle As Integer) As Single
    End Function

    ' Gamepad management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsGamepadAvailable(gamepadId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetGamepadName(gamepadId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetGamepadCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_SetActiveGamepad(gamepadId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetActiveGamepad() As Integer
    End Function

    ' Direct gamepad queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsGamepadButtonPressed(gamepadId As Integer, button As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsGamepadButtonDown(gamepadId As Integer, button As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsGamepadButtonReleased(gamepadId As Integer, button As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetGamepadAxisValue(gamepadId As Integer, axis As Integer) As Single
    End Function

    ' Rebinding support
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_StartListening(actionHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_IsListening() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_StopListening()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_WasBindingCaptured() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetCapturedSourceType() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Input_GetCapturedCode() As Integer
    End Function

    ' Rumble/vibration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_SetGamepadVibration(gamepadId As Integer, leftMotor As Single, rightMotor As Single, duration As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_StopGamepadVibration(gamepadId As Integer)
    End Sub

    ' Input system update
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Input_Update()
    End Sub

    ' Serialization
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Input_SaveBindings(filename As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Input_LoadBindings(filename As String) As Boolean
    End Function
#End Region

#Region "Save/Load System"
    ' Save slot management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_SetDirectory(directory As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_GetDirectory() As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_GetSlotCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_SlotExists(slot As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_DeleteSlot(slot As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_CopySlot(fromSlot As Integer, toSlot As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_GetSlotInfo(slot As Integer) As IntPtr
    End Function

    ' Save/Load operations
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_BeginSave(slot As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_EndSave() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_BeginLoad(slot As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_EndLoad() As Boolean
    End Function

    ' Data serialization - Write
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_WriteInt(key As String, value As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_WriteFloat(key As String, value As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_WriteBool(key As String, value As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_WriteString(key As String, value As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_WriteVector2(key As String, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_WriteIntArray(key As String, values As Integer(), count As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_WriteFloatArray(key As String, values As Single(), count As Integer)
    End Sub

    ' Data serialization - Read
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_ReadInt(key As String, defaultValue As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_ReadFloat(key As String, defaultValue As Single) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_ReadBool(key As String, defaultValue As Boolean) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_ReadString(key As String, defaultValue As String) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_ReadVector2(key As String, ByRef x As Single, ByRef y As Single, defX As Single, defY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_ReadIntArray(key As String, buffer As Integer(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_ReadFloatArray(key As String, buffer As Single(), bufferSize As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_HasKey(key As String) As Boolean
    End Function

    ' Metadata
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Save_SetMetadata(key As String, value As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Save_GetMetadata(slot As Integer, key As String) As IntPtr
    End Function

    ' Auto-save
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Save_SetAutoSaveEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_IsAutoSaveEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Save_SetAutoSaveInterval(seconds As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_GetAutoSaveInterval() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Save_SetAutoSaveSlot(slot As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_GetAutoSaveSlot() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Save_TriggerAutoSave()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Save_Update(dt As Single)
    End Sub

    ' Quick save/load
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_QuickSave() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Save_QuickLoad() As Boolean
    End Function

    ' Settings
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Settings_SetInt(key As String, value As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Settings_GetInt(key As String, defaultValue As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Settings_SetFloat(key As String, value As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Settings_GetFloat(key As String, defaultValue As Single) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Settings_SetBool(key As String, value As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Settings_GetBool(key As String, defaultValue As Boolean) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Settings_SetString(key As String, value As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Settings_GetString(key As String, defaultValue As String) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Settings_Save() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Settings_Load() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Settings_Clear()
    End Sub
#End Region

#Region "Tweening System"
    ' Tween Easing Types
    Public Enum TweenEasing As Integer
        TWEEN_LINEAR = 0
        TWEEN_IN_QUAD = 1
        TWEEN_OUT_QUAD = 2
        TWEEN_IN_OUT_QUAD = 3
        TWEEN_IN_CUBIC = 4
        TWEEN_OUT_CUBIC = 5
        TWEEN_IN_OUT_CUBIC = 6
        TWEEN_IN_EXPO = 7
        TWEEN_OUT_EXPO = 8
        TWEEN_IN_OUT_EXPO = 9
        TWEEN_IN_SINE = 10
        TWEEN_OUT_SINE = 11
        TWEEN_IN_OUT_SINE = 12
        TWEEN_IN_BACK = 13
        TWEEN_OUT_BACK = 14
        TWEEN_IN_OUT_BACK = 15
        TWEEN_IN_ELASTIC = 16
        TWEEN_OUT_ELASTIC = 17
        TWEEN_IN_OUT_ELASTIC = 18
        TWEEN_IN_BOUNCE = 19
        TWEEN_OUT_BOUNCE = 20
        TWEEN_IN_OUT_BOUNCE = 21
    End Enum

    ' Tween Loop Mode
    Public Enum TweenLoopMode As Integer
        TWEEN_LOOP_NONE = 0
        TWEEN_LOOP_RESTART = 1
        TWEEN_LOOP_YOYO = 2
        TWEEN_LOOP_INCREMENT = 3
    End Enum

    ' Tween State
    Public Enum TweenState As Integer
        TWEEN_STATE_IDLE = 0
        TWEEN_STATE_PLAYING = 1
        TWEEN_STATE_PAUSED = 2
        TWEEN_STATE_COMPLETED = 3
    End Enum

    ' Tween Callbacks
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub TweenCallback(tweenId As Integer)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub TweenUpdateCallback(tweenId As Integer, value As Single)

    ' Float Tweens
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_Float(fromValue As Single, toValue As Single, duration As Single, easing As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_FloatTo(ByRef target As Single, toValue As Single, duration As Single, easing As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_FloatFromTo(ByRef target As Single, fromValue As Single, toValue As Single, duration As Single, easing As Integer) As Integer
    End Function

    ' Vector2 Tweens
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_Vector2(fromX As Single, fromY As Single, toX As Single, toY As Single, duration As Single, easing As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_Vector2To(ByRef targetX As Single, ByRef targetY As Single, toX As Single, toY As Single, duration As Single, easing As Integer) As Integer
    End Function

    ' Color Tweens
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_Color(fromR As Byte, fromG As Byte, fromB As Byte, fromA As Byte, toR As Byte, toG As Byte, toB As Byte, toA As Byte, duration As Single, easing As Integer) As Integer
    End Function

    ' Tween Control
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Play(tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Pause(tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Resume(tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Stop(tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Restart(tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Kill(tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Complete(tweenId As Integer)
    End Sub

    ' Tween State Queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_IsValid(tweenId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetState(tweenId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_IsPlaying(tweenId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_IsPaused(tweenId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_IsCompleted(tweenId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetProgress(tweenId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetElapsed(tweenId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetDuration(tweenId As Integer) As Single
    End Function

    ' Tween Value Getters
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetFloat(tweenId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_GetVector2(tweenId As Integer, ByRef x As Single, ByRef y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_GetColor(tweenId As Integer, ByRef r As Byte, ByRef g As Byte, ByRef b As Byte, ByRef a As Byte)
    End Sub

    ' Tween Configuration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetDelay(tweenId As Integer, delay As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetDelay(tweenId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetLoopMode(tweenId As Integer, loopMode As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetLoopMode(tweenId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetLoopCount(tweenId As Integer, count As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetLoopCount(tweenId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetCurrentLoop(tweenId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetTimeScale(tweenId As Integer, scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetTimeScale(tweenId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetAutoKill(tweenId As Integer, autoKill As Boolean)
    End Sub

    ' Tween Callbacks
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetOnStart(tweenId As Integer, callback As TweenCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetOnUpdate(tweenId As Integer, callback As TweenUpdateCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetOnComplete(tweenId As Integer, callback As TweenCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetOnLoop(tweenId As Integer, callback As TweenCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetOnKill(tweenId As Integer, callback As TweenCallback)
    End Sub

    ' Sequence Building
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_CreateSequence() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SequenceAppend(seqId As Integer, tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SequenceJoin(seqId As Integer, tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SequenceInsert(seqId As Integer, atTime As Single, tweenId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SequenceAppendDelay(seqId As Integer, delay As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SequenceAppendCallback(seqId As Integer, callback As TweenCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_PlaySequence(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_PauseSequence(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_StopSequence(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_KillSequence(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_IsSequenceValid(seqId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_IsSequencePlaying(seqId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetSequenceDuration(seqId As Integer) As Single
    End Function

    ' Entity Property Tweens
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_EntityPosition(entity As Integer, toX As Single, toY As Single, duration As Single, easing As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_EntityRotation(entity As Integer, toRotation As Single, duration As Single, easing As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_EntityScale(entity As Integer, toScaleX As Single, toScaleY As Single, duration As Single, easing As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_EntityAlpha(entity As Integer, toAlpha As Byte, duration As Single, easing As Integer) As Integer
    End Function

    ' Global Tween Management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_Update(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_PauseAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_ResumeAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_KillAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetActiveCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Tween_SetGlobalTimeScale(scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_GetGlobalTimeScale() As Single
    End Function

    ' Easing Function Utility
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Tween_Ease(t As Single, easing As Integer) As Single
    End Function
#End Region

#Region "Event System"
    ' ========================================================================
    ' EVENT SYSTEM - Publish/Subscribe messaging
    ' ========================================================================

    ' Event Data Types
    Public Enum EventDataType
        None = 0
        Int = 1
        Float = 2
        StringType = 3
        Vector2Type = 4
        Entity = 5
        Pointer = 6
    End Enum

    ' Event Callback Delegates
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub EventCallback(eventId As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub EventCallbackInt(eventId As Integer, value As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub EventCallbackFloat(eventId As Integer, value As Single, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub EventCallbackString(eventId As Integer, value As IntPtr, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub EventCallbackVector2(eventId As Integer, x As Single, y As Single, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub EventCallbackEntity(eventId As Integer, entity As Integer, userData As IntPtr)

    ' Event Registration and Naming
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_Register(eventName As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_GetId(eventName As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_GetName(eventId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_Exists(eventName As String) As Boolean
    End Function

    ' Subscribe to Events (returns subscription handle)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_Subscribe(eventId As Integer, callback As EventCallback, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeInt(eventId As Integer, callback As EventCallbackInt, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeFloat(eventId As Integer, callback As EventCallbackFloat, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeString(eventId As Integer, callback As EventCallbackString, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeVector2(eventId As Integer, callback As EventCallbackVector2, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeEntity(eventId As Integer, callback As EventCallbackEntity, userData As IntPtr) As Integer
    End Function

    ' Subscribe by Name (convenience)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeByName(eventName As String, callback As EventCallback, userData As IntPtr) As Integer
    End Function

    ' One-shot Subscriptions (auto-unsubscribe after first trigger)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeOnce(eventId As Integer, callback As EventCallback, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeOnceInt(eventId As Integer, callback As EventCallbackInt, userData As IntPtr) As Integer
    End Function

    ' Unsubscribe
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_Unsubscribe(subscriptionId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_UnsubscribeAll(eventId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_UnsubscribeCallback(eventId As Integer, callback As EventCallback)
    End Sub

    ' Publish Events (immediate dispatch)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_Publish(eventId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishInt(eventId As Integer, value As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishFloat(eventId As Integer, value As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishString(eventId As Integer, value As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishVector2(eventId As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishEntity(eventId As Integer, entity As Integer)
    End Sub

    ' Publish by Name (convenience)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishByName(eventName As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishByNameInt(eventName As String, value As Integer)
    End Sub

    ' Queued/Deferred Events
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_Queue(eventId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_QueueInt(eventId As Integer, value As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_QueueFloat(eventId As Integer, value As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_QueueString(eventId As Integer, value As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_QueueDelayed(eventId As Integer, delay As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_QueueDelayedInt(eventId As Integer, value As Integer, delay As Single)
    End Sub

    ' Entity-Specific Events
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_SubscribeToEntity(entity As Integer, eventId As Integer, callback As EventCallbackEntity, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishToEntity(entity As Integer, eventId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PublishToEntityInt(entity As Integer, eventId As Integer, value As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_UnsubscribeFromEntity(entity As Integer, eventId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_UnsubscribeAllFromEntity(entity As Integer)
    End Sub

    ' Priority Control
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_SetPriority(subscriptionId As Integer, priority As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_GetPriority(subscriptionId As Integer) As Integer
    End Function

    ' Event State and Management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_SetEnabled(subscriptionId As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_IsEnabled(subscriptionId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_IsSubscriptionValid(subscriptionId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_GetSubscriberCount(eventId As Integer) As Integer
    End Function

    ' Queue Processing and Management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_ProcessQueue(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_ClearQueue()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_GetQueuedCount() As Integer
    End Function

    ' Global Event System Management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_PauseAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_ResumeAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_IsPaused() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Event_Clear()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_GetEventCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Event_GetTotalSubscriptions() As Integer
    End Function
#End Region

#Region "Timer System"
    ' ========================================================================
    ' TIMER SYSTEM - Delayed execution and scheduling
    ' ========================================================================

    ' Timer States
    Public Enum TimerState
        Pending = 0
        Running = 1
        Paused = 2
        Completed = 3
        Cancelled = 4
    End Enum

    ' Timer Callback Delegates
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub TimerCallback(timerId As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub TimerCallbackInt(timerId As Integer, value As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub TimerCallbackFloat(timerId As Integer, value As Single, userData As IntPtr)

    ' Basic Timers (one-shot)
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_After(delay As Single, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_AfterInt(delay As Single, callback As TimerCallbackInt, value As Integer, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_AfterFloat(delay As Single, callback As TimerCallbackFloat, value As Single, userData As IntPtr) As Integer
    End Function

    ' Repeating Timers
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_Every(interval As Single, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_EveryInt(interval As Single, callback As TimerCallbackInt, value As Integer, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_EveryLimit(interval As Single, repeatCount As Integer, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    ' Timer with Initial Delay then Repeat
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_AfterThenEvery(delay As Single, interval As Single, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    ' Timer Control
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_Cancel(timerId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_Pause(timerId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_Resume(timerId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_Reset(timerId As Integer)
    End Sub

    ' Timer State Queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_IsValid(timerId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_IsRunning(timerId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_IsPaused(timerId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetState(timerId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetElapsed(timerId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetRemaining(timerId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetRepeatCount(timerId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetCurrentRepeat(timerId As Integer) As Integer
    End Function

    ' Timer Configuration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SetTimeScale(timerId As Integer, scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetTimeScale(timerId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SetInterval(timerId As Integer, interval As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetInterval(timerId As Integer) As Single
    End Function

    ' Entity-Bound Timers
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_AfterEntity(entity As Integer, delay As Single, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_EveryEntity(entity As Integer, interval As Single, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_CancelAllForEntity(entity As Integer)
    End Sub

    ' Sequence Building
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_CreateSequence() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequenceAppend(seqId As Integer, delay As Single, callback As TimerCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequenceAppendInt(seqId As Integer, delay As Single, callback As TimerCallbackInt, value As Integer, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequenceStart(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequencePause(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequenceResume(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequenceCancel(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequenceReset(seqId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_SequenceIsValid(seqId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_SequenceIsRunning(seqId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_SequenceGetDuration(seqId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_SequenceGetElapsed(seqId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SequenceSetLoop(seqId As Integer, shouldLoop As Boolean)
    End Sub

    ' Global Timer Management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_Update(dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_PauseAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_ResumeAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_CancelAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetActiveCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_SetGlobalTimeScale(scale As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_GetGlobalTimeScale() As Single
    End Function

    ' Frame-Based Timers
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_AfterFrames(frames As Integer, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Timer_EveryFrames(frames As Integer, callback As TimerCallback, userData As IntPtr) As Integer
    End Function

    ' Utility Functions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Timer_ClearCompleted()
    End Sub
#End Region

#Region "Object Pooling"
    ' ========================================================================
    ' OBJECT POOLING - Efficient object reuse
    ' ========================================================================

    ' Pool Callback Delegates
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub PoolResetCallback(poolId As Integer, objectIndex As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub PoolInitCallback(poolId As Integer, objectIndex As Integer, userData As IntPtr)

    ' Pool Creation and Management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_Create(poolName As String, initialCapacity As Integer, maxCapacity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetByName(poolName As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_Destroy(poolId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_IsValid(poolId As Integer) As Boolean
    End Function

    ' Pool Configuration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_SetAutoGrow(poolId As Integer, autoGrow As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetAutoGrow(poolId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_SetGrowAmount(poolId As Integer, amount As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetGrowAmount(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_SetResetCallback(poolId As Integer, callback As PoolResetCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_SetInitCallback(poolId As Integer, callback As PoolInitCallback, userData As IntPtr)
    End Sub

    ' Acquire and Release Objects
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_Acquire(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_Release(poolId As Integer, objectIndex As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_ReleaseAll(poolId As Integer)
    End Sub

    ' Pool State Queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetCapacity(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetActiveCount(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetAvailableCount(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_IsEmpty(poolId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_IsFull(poolId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_IsObjectActive(poolId As Integer, objectIndex As Integer) As Boolean
    End Function

    ' Pool Statistics
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetTotalAcquires(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetTotalReleases(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetPeakUsage(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_ResetStats(poolId As Integer)
    End Sub

    ' Pre-warming
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_Warmup(poolId As Integer, count As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_Shrink(poolId As Integer)
    End Sub

    ' Entity Pools
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_CreateEntityPool(poolName As String, prefabId As Integer, initialCapacity As Integer, maxCapacity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_AcquireEntity(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_ReleaseEntity(poolId As Integer, entity As Integer)
    End Sub

    ' Iterate Active Objects
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetFirstActive(poolId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetNextActive(poolId As Integer, currentIndex As Integer) As Integer
    End Function

    ' Bulk Operations
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_AcquireMultiple(poolId As Integer, count As Integer, outIndices As Integer()) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_ReleaseMultiple(poolId As Integer, indices As Integer(), count As Integer)
    End Sub

    ' Global Pool Management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Pool_GetPoolCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_DestroyAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Pool_ReleaseAllPools()
    End Sub
#End Region

#Region "State Machine"
    ' State callback delegates
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub StateEnterCallback(fsmId As Integer, stateId As Integer, previousState As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub StateUpdateCallback(fsmId As Integer, stateId As Integer, deltaTime As Single, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub StateExitCallback(fsmId As Integer, stateId As Integer, nextState As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Function TransitionCondition(fsmId As Integer, fromState As Integer, toState As Integer, userData As IntPtr) As Boolean

    ' FSM creation and management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_Create(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_CreateForEntity(name As String, entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_Destroy(fsmId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetByName(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetForEntity(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_IsValid(fsmId As Integer) As Boolean
    End Function

    ' State registration
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_AddState(fsmId As Integer, stateName As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetState(fsmId As Integer, stateName As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetStateName(fsmId As Integer, stateId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_RemoveState(fsmId As Integer, stateId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetStateCount(fsmId As Integer) As Integer
    End Function

    ' State callbacks
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetStateEnter(fsmId As Integer, stateId As Integer, callback As StateEnterCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetStateUpdate(fsmId As Integer, stateId As Integer, callback As StateUpdateCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetStateExit(fsmId As Integer, stateId As Integer, callback As StateExitCallback, userData As IntPtr)
    End Sub

    ' Transitions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_AddTransition(fsmId As Integer, fromState As Integer, toState As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetTransitionCondition(fsmId As Integer, transitionId As Integer, condition As TransitionCondition, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_RemoveTransition(fsmId As Integer, transitionId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_CanTransition(fsmId As Integer, fromState As Integer, toState As Integer) As Boolean
    End Function

    ' Any-state transitions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_AddAnyTransition(fsmId As Integer, toState As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetAnyTransitionCondition(fsmId As Integer, transitionId As Integer, condition As TransitionCondition, userData As IntPtr)
    End Sub

    ' State machine control
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetInitialState(fsmId As Integer, stateId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_Start(fsmId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_Stop(fsmId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_Pause(fsmId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_Resume(fsmId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_IsRunning(fsmId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_IsPaused(fsmId As Integer) As Boolean
    End Function

    ' State queries
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetCurrentState(fsmId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetPreviousState(fsmId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetTimeInState(fsmId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetStateChangeCount(fsmId As Integer) As Integer
    End Function

    ' Manual transitions
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_TransitionTo(fsmId As Integer, stateId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_TransitionToByName(fsmId As Integer, stateName As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_TryTransition(fsmId As Integer, toState As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_RevertToPrevious(fsmId As Integer)
    End Sub

    ' State history
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetHistorySize(fsmId As Integer, size As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetHistoryState(fsmId As Integer, index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetHistoryCount(fsmId As Integer) As Integer
    End Function

    ' Triggers
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_AddTrigger(fsmId As Integer, triggerName As String, fromState As Integer, toState As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_FireTrigger(fsmId As Integer, triggerName As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_FireTriggerWithData(fsmId As Integer, triggerName As String, data As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_RemoveTrigger(fsmId As Integer, triggerId As Integer)
    End Sub

    ' Update
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_Update(fsmId As Integer, deltaTime As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_UpdateAll(deltaTime As Single)
    End Sub

    ' Global FSM management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_DestroyAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_PauseAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_ResumeAll()
    End Sub

    ' Debug
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_FSM_SetDebugEnabled(fsmId As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_FSM_GetDebugEnabled(fsmId As Integer) As Boolean
    End Function
#End Region

#Region "Profiling and Performance"
    ' Log levels
    Public Const LOG_LEVEL_TRACE As Integer = 0
    Public Const LOG_LEVEL_DEBUG As Integer = 1
    Public Const LOG_LEVEL_INFO As Integer = 2
    Public Const LOG_LEVEL_WARNING As Integer = 3
    Public Const LOG_LEVEL_ERROR As Integer = 4
    Public Const LOG_LEVEL_FATAL As Integer = 5

    ' Frame timing
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetFPS() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetFrameTime() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetFrameTimeAvg() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetFrameTimeMin() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetFrameTimeMax() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_SetSampleCount(count As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetFrameCount() As Integer
    End Function

    ' Draw call tracking
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetDrawCalls() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetTriangleCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_ResetDrawStats()
    End Sub

    ' Memory tracking
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetEntityCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetTextureCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetSoundCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetFontCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetTextureMemory() As Long
    End Function

    ' Profiling scopes
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_BeginScope(name As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_EndScope()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetScopeTime(name As String) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetScopeTimeAvg(name As String) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Perf_GetScopeCallCount(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_ResetScopes()
    End Sub

    ' Performance graphs
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_SetGraphEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_SetGraphPosition(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_SetGraphSize(width As Single, height As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_DrawGraph()
    End Sub

    ' Console/Logging
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Log(level As Integer, message As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Log_SetMinLevel(level As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Log_GetMinLevel() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Log_SetFileOutput(filename As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Log_CloseFile()
    End Sub

    ' On-screen console
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_SetEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Console_IsEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_SetPosition(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_SetSize(width As Single, height As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_SetMaxLines(maxLines As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_Clear()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_Print(message As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_PrintColored(message As String, r As Byte, g As Byte, b As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Console_Draw()
    End Sub

    ' Debug drawing
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Line(x1 As Single, y1 As Single, x2 As Single, y2 As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Rect(x As Single, y As Single, w As Single, h As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_RectFilled(x As Single, y As Single, w As Single, h As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Circle(x As Single, y As Single, radius As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_CircleFilled(x As Single, y As Single, radius As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Point(x As Single, y As Single, size As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Arrow(x1 As Single, y1 As Single, x2 As Single, y2 As Single, headSize As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Text(x As Single, y As Single, text As String, r As Byte, g As Byte, b As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Grid(cellSize As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Cross(x As Single, y As Single, size As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    ' Debug draw settings
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_SetEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_DebugDraw_IsEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_SetPersistent(persistent As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Clear()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DebugDraw_Flush()
    End Sub

    ' System overlays
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetShowFPS(show As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetShowFrameTime(show As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetShowDrawCalls(show As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetShowEntityCount(show As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetShowMemory(show As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetShowPhysics(show As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetShowColliders(show As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetOverlayPosition(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Debug_SetOverlayColor(r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    ' Frame profiling
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_BeginFrame()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Perf_EndFrame()
    End Sub
#End Region

#Region "AI and Pathfinding"
    ' ========================================================================
    ' AI AND PATHFINDING - Navigation grids, A* pathfinding, steering behaviors
    ' ========================================================================

    ' Steering Behavior Types
    Public Enum SteeringBehavior As Integer
        STEER_NONE = 0
        STEER_SEEK = 1
        STEER_FLEE = 2
        STEER_ARRIVE = 3
        STEER_PURSUE = 4
        STEER_EVADE = 5
        STEER_WANDER = 6
        STEER_PATH_FOLLOW = 7
        STEER_OBSTACLE_AVOID = 8
        STEER_SEPARATION = 9
        STEER_ALIGNMENT = 10
        STEER_COHESION = 11
    End Enum

    ' Heuristic Types
    Public Enum PathHeuristic As Integer
        HEURISTIC_MANHATTAN = 0
        HEURISTIC_EUCLIDEAN = 1
        HEURISTIC_CHEBYSHEV = 2
    End Enum

    ' ---- Navigation Grid ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_NavGrid_Create(width As Integer, height As Integer, cellSize As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_Destroy(gridId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_NavGrid_IsValid(gridId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_SetOrigin(gridId As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_SetWalkable(gridId As Integer, cellX As Integer, cellY As Integer, walkable As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_NavGrid_IsWalkable(gridId As Integer, cellX As Integer, cellY As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_SetCost(gridId As Integer, cellX As Integer, cellY As Integer, cost As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_NavGrid_GetCost(gridId As Integer, cellX As Integer, cellY As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_SetDiagonalEnabled(gridId As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_SetDiagonalCost(gridId As Integer, cost As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_SetHeuristic(gridId As Integer, heuristic As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_Clear(gridId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_Fill(gridId As Integer, walkable As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_SetRect(gridId As Integer, x As Integer, y As Integer, w As Integer, h As Integer, walkable As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_WorldToCell(gridId As Integer, worldX As Single, worldY As Single, ByRef cellX As Integer, ByRef cellY As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_CellToWorld(gridId As Integer, cellX As Integer, cellY As Integer, ByRef worldX As Single, ByRef worldY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_NavGrid_GetWidth(gridId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_NavGrid_GetHeight(gridId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_NavGrid_GetCellSize(gridId As Integer) As Single
    End Function

    ' ---- A* Pathfinding ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Path_Find(gridId As Integer, startX As Single, startY As Single, endX As Single, endY As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Path_FindCell(gridId As Integer, startCellX As Integer, startCellY As Integer, endCellX As Integer, endCellY As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Path_Destroy(pathId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Path_IsValid(pathId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Path_GetLength(pathId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Path_GetWaypoint(pathId As Integer, index As Integer, ByRef x As Single, ByRef y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Path_GetTotalDistance(pathId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Path_Smooth(pathId As Integer, epsilon As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Path_Reverse(pathId As Integer)
    End Sub

    ' ---- Steering Agents ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_CreateAgent(entity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_DestroyAgent(agentId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_IsValid(agentId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_GetAgentForEntity(entity As Integer) As Integer
    End Function

    ' Agent properties
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetMaxSpeed(agentId As Integer, maxSpeed As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_GetMaxSpeed(agentId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetMaxForce(agentId As Integer, maxForce As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_GetMaxForce(agentId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetMass(agentId As Integer, mass As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_GetMass(agentId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_GetVelocity(agentId As Integer, ByRef vx As Single, ByRef vy As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetVelocity(agentId As Integer, vx As Single, vy As Single)
    End Sub

    ' Target
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetTarget(agentId As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetTargetEntity(agentId As Integer, targetEntity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_ClearTarget(agentId As Integer)
    End Sub

    ' Behaviors
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_EnableBehavior(agentId As Integer, behavior As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_IsBehaviorEnabled(agentId As Integer, behavior As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetBehaviorWeight(agentId As Integer, behavior As Integer, weight As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_GetBehaviorWeight(agentId As Integer, behavior As Integer) As Single
    End Function

    ' Arrive behavior config
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetArriveRadius(agentId As Integer, slowRadius As Single, stopRadius As Single)
    End Sub

    ' Wander behavior config
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetWanderParams(agentId As Integer, radius As Single, distance As Single, jitter As Single)
    End Sub

    ' Path following
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetPath(agentId As Integer, pathId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_ClearPath(agentId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetPathLooping(agentId As Integer, shouldLoop As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetWaypointRadius(agentId As Integer, radius As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_GetCurrentWaypoint(agentId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Steer_HasReachedEnd(agentId As Integer) As Boolean
    End Function

    ' Agent update
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_Update(agentId As Integer, dt As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_UpdateAll(dt As Single)
    End Sub

    ' Obstacle avoidance
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetAvoidanceRadius(agentId As Integer, radius As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_AddObstacle(x As Single, y As Single, radius As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_ClearObstacles()
    End Sub

    ' Flocking
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetFlockingParams(agentId As Integer, separationDist As Single, alignDist As Single, cohesionDist As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_SetFlockGroup(agentId As Integer, groupId As Integer)
    End Sub

    ' Debug visualization
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_NavGrid_DrawDebug(gridId As Integer, showCosts As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Path_DrawDebug(pathId As Integer, r As Byte, g As Byte, b As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Steer_DrawDebug(agentId As Integer)
    End Sub

    ' Global AI management
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_AI_DestroyAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_AI_GetAgentCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_AI_GetPathCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_AI_GetGridCount() As Integer
    End Function
#End Region

#Region "Dialogue System"
    ' ========================================================================
    ' DIALOGUE SYSTEM - Branching conversations and dialogue trees
    ' ========================================================================

    ' Dialogue Callbacks
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub DialogueCallback(dialogueId As Integer, nodeId As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub DialogueChoiceCallback(dialogueId As Integer, nodeId As Integer, choiceIndex As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Function DialogueConditionCallback(dialogueId As Integer, condition As String, userData As IntPtr) As Boolean

    ' ---- Dialogue Creation and Management ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_Create(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_Destroy(dialogueId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetByName(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_IsValid(dialogueId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_Clear(dialogueId As Integer)
    End Sub

    ' ---- Node Creation ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_AddNode(dialogueId As Integer, nodeTag As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_RemoveNode(dialogueId As Integer, nodeId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetNodeByTag(dialogueId As Integer, tag As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetNodeCount(dialogueId As Integer) As Integer
    End Function

    ' ---- Node Content ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetNodeSpeaker(dialogueId As Integer, nodeId As Integer, speaker As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetNodeSpeaker(dialogueId As Integer, nodeId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetNodeText(dialogueId As Integer, nodeId As Integer, text As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetNodeText(dialogueId As Integer, nodeId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetNodePortrait(dialogueId As Integer, nodeId As Integer, textureHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetNodePortrait(dialogueId As Integer, nodeId As Integer) As Integer
    End Function

    ' ---- Node Connections ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetNextNode(dialogueId As Integer, nodeId As Integer, nextNodeId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetNextNode(dialogueId As Integer, nodeId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetStartNode(dialogueId As Integer, nodeId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetStartNode(dialogueId As Integer) As Integer
    End Function

    ' ---- Choices ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_AddChoice(dialogueId As Integer, nodeId As Integer, choiceText As String, targetNodeId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_RemoveChoice(dialogueId As Integer, nodeId As Integer, choiceIndex As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetChoiceCount(dialogueId As Integer, nodeId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetChoiceText(dialogueId As Integer, nodeId As Integer, choiceIndex As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetChoiceTarget(dialogueId As Integer, nodeId As Integer, choiceIndex As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetChoiceCondition(dialogueId As Integer, nodeId As Integer, choiceIndex As Integer, condition As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetChoiceCondition(dialogueId As Integer, nodeId As Integer, choiceIndex As Integer) As IntPtr
    End Function

    ' ---- Conditional Nodes ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetNodeCondition(dialogueId As Integer, nodeId As Integer, condition As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetNodeCondition(dialogueId As Integer, nodeId As Integer) As IntPtr
    End Function

    ' ---- Node Events ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetNodeEvent(dialogueId As Integer, nodeId As Integer, eventName As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetNodeEvent(dialogueId As Integer, nodeId As Integer) As IntPtr
    End Function

    ' ---- Variables ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetVarInt(varName As String, value As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetVarInt(varName As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetVarFloat(varName As String, value As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetVarFloat(varName As String) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetVarBool(varName As String, value As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetVarBool(varName As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetVarString(varName As String, value As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetVarString(varName As String) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_ClearVar(varName As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_ClearAllVars()
    End Sub

    ' ---- Playback ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_Start(dialogueId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_StartAtNode(dialogueId As Integer, nodeId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_Stop()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_IsActive() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetActiveDialogue() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetCurrentNode() As Integer
    End Function

    ' ---- Advance Dialogue ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_Continue() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_SelectChoice(choiceIndex As Integer) As Boolean
    End Function

    ' ---- Current Node Queries ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetCurrentSpeaker() As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetCurrentText() As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetCurrentPortrait() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetCurrentChoiceCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetCurrentChoiceText(choiceIndex As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_IsCurrentChoiceAvailable(choiceIndex As Integer) As Boolean
    End Function

    ' ---- Typewriter Effect ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetTypewriterEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_IsTypewriterEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetTypewriterSpeed(charsPerSecond As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetTypewriterSpeed() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SkipTypewriter()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_IsTypewriterComplete() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetVisibleText() As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetVisibleCharCount() As Integer
    End Function

    ' ---- Callbacks ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetOnStartCallback(callback As DialogueCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetOnEndCallback(callback As DialogueCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetOnNodeEnterCallback(callback As DialogueCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetOnNodeExitCallback(callback As DialogueCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetOnChoiceCallback(callback As DialogueChoiceCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetConditionHandler(callback As DialogueConditionCallback, userData As IntPtr)
    End Sub

    ' ---- Update ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_Update(dt As Single)
    End Sub

    ' ---- Speaker Management ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_RegisterSpeaker(speakerId As String, displayName As String, defaultPortrait As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_UnregisterSpeaker(speakerId As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetSpeakerDisplayName(speakerId As String) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_GetSpeakerPortrait(speakerId As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Dialogue_SetSpeakerPortrait(speakerId As String, textureHandle As Integer)
    End Sub

    ' ---- History ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_SetHistoryEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_IsHistoryEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetHistoryCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetHistorySpeaker(index As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetHistoryText(index As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_ClearHistory()
    End Sub

    ' ---- Save/Load ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_SaveToFile(dialogueId As Integer, filename As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Dialogue_LoadFromFile(filename As String) As Integer
    End Function

    ' ---- Global Management ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Dialogue_DestroyAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Dialogue_GetCount() As Integer
    End Function
#End Region

#Region "Inventory System"
    ' ========================================================================
    ' INVENTORY SYSTEM
    ' ========================================================================

    ' Enums
    Public Enum ItemRarity
        Common = 0
        Uncommon = 1
        Rare = 2
        Epic = 3
        Legendary = 4
    End Enum

    Public Enum EquipSlot
        None = 0
        Head = 1
        Chest = 2
        Legs = 3
        Feet = 4
        Hands = 5
        MainHand = 6
        OffHand = 7
        Accessory1 = 8
        Accessory2 = 9
    End Enum

    ' Callbacks
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub InventoryCallback(inventoryId As Integer, slotIndex As Integer, itemId As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub ItemUseCallback(inventoryId As Integer, slotIndex As Integer, itemId As Integer, quantity As Integer, userData As IntPtr)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Function ItemDropCallback(inventoryId As Integer, slotIndex As Integer, itemId As Integer, quantity As Integer, userData As IntPtr) As Boolean

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub EquipCallback(equipmentId As Integer, slot As Integer, itemId As Integer, userData As IntPtr)

    ' ---- Item Definition API ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Item_Define(name As String, description As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_SetIcon(itemId As Integer, textureHandle As Integer, x As Single, y As Single, w As Single, h As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_SetStackable(itemId As Integer, stackable As Boolean, maxStack As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Item_SetCategory(itemId As Integer, category As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_SetRarity(itemId As Integer, rarity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_SetEquipSlot(itemId As Integer, slot As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Item_SetStat(itemId As Integer, statName As String, value As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Item_GetStat(itemId As Integer, statName As String) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_SetValue(itemId As Integer, value As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_SetWeight(itemId As Integer, weight As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_SetConsumable(itemId As Integer, consumable As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Item_SetCustomData(itemId As Integer, data As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Item_GetByName(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_GetName(itemId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_GetDescription(itemId As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_GetRarity(itemId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_GetEquipSlot(itemId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_GetValue(itemId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_GetWeight(itemId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_IsStackable(itemId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_GetMaxStack(itemId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_IsConsumable(itemId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Item_Exists(itemId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_Undefine(itemId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Item_UndefineAll()
    End Sub

    ' ---- Inventory API ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Inventory_Create(name As String, capacity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_Destroy(inventoryId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Inventory_GetByName(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetCapacity(inventoryId As Integer, capacity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetCapacity(inventoryId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetMaxWeight(inventoryId As Integer, maxWeight As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetMaxWeight(inventoryId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetCurrentWeight(inventoryId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetOwner(inventoryId As Integer, entityId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetOwner(inventoryId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetAutoStack(inventoryId As Integer, autoStack As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_AddItem(inventoryId As Integer, itemId As Integer, quantity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_RemoveItem(inventoryId As Integer, itemId As Integer, quantity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_AddToSlot(inventoryId As Integer, slotIndex As Integer, itemId As Integer, quantity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_RemoveFromSlot(inventoryId As Integer, slotIndex As Integer, quantity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_ClearSlot(inventoryId As Integer, slotIndex As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SwapSlots(inventoryId As Integer, slotA As Integer, slotB As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_MoveItem(srcInvId As Integer, srcSlot As Integer, dstInvId As Integer, dstSlot As Integer, quantity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetSlotItem(inventoryId As Integer, slotIndex As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetSlotQuantity(inventoryId As Integer, slotIndex As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_IsSlotEmpty(inventoryId As Integer, slotIndex As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_LockSlot(inventoryId As Integer, slotIndex As Integer, locked As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_IsSlotLocked(inventoryId As Integer, slotIndex As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Inventory_SetSlotCustomData(inventoryId As Integer, slotIndex As Integer, data As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetSlotCustomData(inventoryId As Integer, slotIndex As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_CountItem(inventoryId As Integer, itemId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_HasItem(inventoryId As Integer, itemId As Integer, quantity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_FindItem(inventoryId As Integer, itemId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetEmptySlotCount(inventoryId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_GetUsedSlotCount(inventoryId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_IsFull(inventoryId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_Sort(inventoryId As Integer, sortMode As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_MergeStacks(inventoryId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_Clear(inventoryId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_UseItem(inventoryId As Integer, slotIndex As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Inventory_DropItem(inventoryId As Integer, slotIndex As Integer, quantity As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetOnItemAdded(inventoryId As Integer, callback As InventoryCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetOnItemRemoved(inventoryId As Integer, callback As InventoryCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetOnItemUsed(inventoryId As Integer, callback As ItemUseCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_SetOnItemDropped(inventoryId As Integer, callback As ItemDropCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Inventory_DestroyAll()
    End Sub

    ' ---- Equipment API ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_Create(ownerEntity As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Equipment_Destroy(equipmentId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_Equip(equipmentId As Integer, itemId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_EquipToSlot(equipmentId As Integer, slot As Integer, itemId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_Unequip(equipmentId As Integer, slot As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_GetInSlot(equipmentId As Integer, slot As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_IsSlotOccupied(equipmentId As Integer, slot As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Equipment_GetTotalStat(equipmentId As Integer, statName As String) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Equipment_Clear(equipmentId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Equipment_SetOnEquip(equipmentId As Integer, callback As EquipCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Equipment_SetOnUnequip(equipmentId As Integer, callback As EquipCallback, userData As IntPtr)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_EquipFromInventory(equipmentId As Integer, inventoryId As Integer, slotIndex As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Equipment_UnequipToInventory(equipmentId As Integer, slot As Integer, inventoryId As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Equipment_DestroyAll()
    End Sub

    ' ---- Loot Table API ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_LootTable_Create(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_LootTable_Destroy(tableId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_LootTable_GetByName(name As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_LootTable_AddEntry(tableId As Integer, itemId As Integer, minQty As Integer, maxQty As Integer, weight As Single, dropChance As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_LootTable_SetDropCount(tableId As Integer, minDrops As Integer, maxDrops As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_LootTable_SetAllowDuplicates(tableId As Integer, allow As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_LootTable_ClearEntries(tableId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LootTable_Roll(tableId As Integer, inventoryId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LootTable_RollDry(tableId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LootTable_GetRollResultItem(index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LootTable_GetRollResultQuantity(index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_LootTable_DestroyAll()
    End Sub
#End Region

#Region "Quest System"
    ' Quest States
    Public Const QUEST_STATE_NOT_STARTED As Integer = 0
    Public Const QUEST_STATE_IN_PROGRESS As Integer = 1
    Public Const QUEST_STATE_COMPLETED As Integer = 2
    Public Const QUEST_STATE_FAILED As Integer = 3

    ' Objective Types
    Public Const OBJECTIVE_TYPE_CUSTOM As Integer = 0
    Public Const OBJECTIVE_TYPE_KILL As Integer = 1
    Public Const OBJECTIVE_TYPE_COLLECT As Integer = 2
    Public Const OBJECTIVE_TYPE_TALK As Integer = 3
    Public Const OBJECTIVE_TYPE_REACH As Integer = 4
    Public Const OBJECTIVE_TYPE_INTERACT As Integer = 5
    Public Const OBJECTIVE_TYPE_ESCORT As Integer = 6
    Public Const OBJECTIVE_TYPE_DEFEND As Integer = 7
    Public Const OBJECTIVE_TYPE_EXPLORE As Integer = 8

    ' Callback delegates
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub QuestStateCallback(questId As Integer, newState As Integer)

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Public Delegate Sub ObjectiveUpdateCallback(questId As Integer, objectiveIndex As Integer, currentProgress As Integer, requiredProgress As Integer)

    ' ---- Quest Definition ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Quest_Define(questId As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_SetName(questHandle As Integer, name As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_SetDescription(questHandle As Integer, description As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_SetCategory(questHandle As Integer, category As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetLevel(questHandle As Integer, level As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetRepeatable(questHandle As Integer, repeatable As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetAutoComplete(questHandle As Integer, autoComplete As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetHidden(questHandle As Integer, hidden As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetTimeLimit(questHandle As Integer, seconds As Single)
    End Sub

    ' ---- Quest Prerequisites ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_AddPrerequisite(questHandle As Integer, requiredQuestId As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetMinLevel(questHandle As Integer, minLevel As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_CheckPrerequisites(questHandle As Integer) As Boolean
    End Function

    ' ---- Objectives ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Quest_AddObjective(questHandle As Integer, objectiveType As Integer, description As String, requiredCount As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_SetObjectiveTarget(questHandle As Integer, objectiveIndex As Integer, targetId As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetObjectiveLocation(questHandle As Integer, objectiveIndex As Integer, x As Single, y As Single, radius As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetObjectiveOptional(questHandle As Integer, objectiveIndex As Integer, optional_ As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetObjectiveHidden(questHandle As Integer, objectiveIndex As Integer, hidden As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetObjectiveCount(questHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetObjectiveDescription(questHandle As Integer, objectiveIndex As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetObjectiveType(questHandle As Integer, objectiveIndex As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetObjectiveProgress(questHandle As Integer, objectiveIndex As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetObjectiveRequired(questHandle As Integer, objectiveIndex As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_IsObjectiveComplete(questHandle As Integer, objectiveIndex As Integer) As Boolean
    End Function

    ' ---- Rewards ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_AddRewardItem(questHandle As Integer, itemDefId As Integer, quantity As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetRewardExperience(questHandle As Integer, experience As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetRewardCurrency(questHandle As Integer, currencyType As Integer, amount As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_AddRewardUnlock(questHandle As Integer, unlockId As String)
    End Sub

    ' ---- Quest State Management ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_Start(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_Complete(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_Fail(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_Abandon(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_Reset(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetState(questHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_IsActive(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_IsCompleted(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_CanStart(questHandle As Integer) As Boolean
    End Function

    ' ---- Progress Tracking ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetObjectiveProgress(questHandle As Integer, objectiveIndex As Integer, progress As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_AddObjectiveProgress(questHandle As Integer, objectiveIndex As Integer, amount As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetCompletionPercent(questHandle As Integer) As Single
    End Function

    ' ---- Auto-Progress Reporting ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_ReportKill(targetType As String, count As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_ReportCollect(itemDefId As Integer, count As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_ReportTalk(npcId As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_ReportLocation(x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_ReportInteract(objectId As String)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_Quest_ReportCustom(eventType As String, eventData As String)
    End Sub

    ' ---- Quest Queries ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Quest_GetByStringId(questId As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetName(questHandle As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetDescription(questHandle As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetCategory(questHandle As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetStringId(questHandle As Integer) As IntPtr
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetLevel(questHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetTimeRemaining(questHandle As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetTimeElapsed(questHandle As Integer) As Single
    End Function

    ' ---- Active Quest List ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetActiveCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetActiveAt(index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetCompletedCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetCompletedAt(index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetAvailableCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetAvailableAt(index As Integer) As Integer
    End Function

    ' ---- Quest Tracking (HUD) ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetTracked(questHandle As Integer, tracked As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_IsTracked(questHandle As Integer) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetTrackedCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetTrackedAt(index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetMaxTracked(maxTracked As Integer)
    End Sub

    ' ---- Callbacks ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetOnStateChange(callback As QuestStateCallback)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_SetOnObjectiveUpdate(callback As ObjectiveUpdateCallback)
    End Sub

    ' ---- Quest Chains ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_QuestChain_Create(chainId As String) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_QuestChain_AddQuest(chainHandle As Integer, questHandle As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_QuestChain_GetCurrentQuest(chainHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_QuestChain_GetProgress(chainHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_QuestChain_GetLength(chainHandle As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_QuestChain_IsComplete(chainHandle As Integer) As Boolean
    End Function

    ' ---- Save/Load ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Quest_SaveProgress(saveSlot As Integer, key As String) As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_Quest_LoadProgress(saveSlot As Integer, key As String) As Boolean
    End Function

    ' ---- Global Management ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_Update(deltaTime As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_UndefineAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Quest_ResetAllProgress()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Quest_GetDefinedCount() As Integer
    End Function

    ' ---- Helper Functions ----
    Public Function GetQuestName(questHandle As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetName(questHandle)
        If ptr = IntPtr.Zero Then Return ""
        Return Marshal.PtrToStringAnsi(ptr)
    End Function

    Public Function GetQuestDescription(questHandle As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetDescription(questHandle)
        If ptr = IntPtr.Zero Then Return ""
        Return Marshal.PtrToStringAnsi(ptr)
    End Function

    Public Function GetQuestCategory(questHandle As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetCategory(questHandle)
        If ptr = IntPtr.Zero Then Return ""
        Return Marshal.PtrToStringAnsi(ptr)
    End Function

    Public Function GetQuestStringId(questHandle As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetStringId(questHandle)
        If ptr = IntPtr.Zero Then Return ""
        Return Marshal.PtrToStringAnsi(ptr)
    End Function

    Public Function GetObjectiveDescription(questHandle As Integer, objectiveIndex As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetObjectiveDescription(questHandle, objectiveIndex)
        If ptr = IntPtr.Zero Then Return ""
        Return Marshal.PtrToStringAnsi(ptr)
    End Function
#End Region

#Region "2D Lighting System"
    ' Light Types
    Public Const LIGHT_TYPE_POINT As Integer = 0
    Public Const LIGHT_TYPE_SPOT As Integer = 1
    Public Const LIGHT_TYPE_DIRECTIONAL As Integer = 2

    ' Shadow Quality
    Public Const SHADOW_QUALITY_NONE As Integer = 0
    Public Const SHADOW_QUALITY_HARD As Integer = 1
    Public Const SHADOW_QUALITY_SOFT As Integer = 2

    ' Blend Modes
    Public Const LIGHT_BLEND_ADDITIVE As Integer = 0
    Public Const LIGHT_BLEND_MULTIPLY As Integer = 1

    ' ---- Lighting System Control ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_Initialize(width As Integer, height As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_Shutdown()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Lighting_IsEnabled() As Boolean
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetResolution(width As Integer, height As Integer)
    End Sub

    ' ---- Ambient Light ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetAmbientColor(r As Byte, g As Byte, b As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetAmbientIntensity(intensity As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Lighting_GetAmbientIntensity() As Single
    End Function

    ' ---- Point Lights ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_CreatePoint(x As Single, y As Single, radius As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_Destroy(lightId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetPosition(lightId As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_GetPosition(lightId As Integer, ByRef x As Single, ByRef y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetColor(lightId As Integer, r As Byte, g As Byte, b As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetIntensity(lightId As Integer, intensity As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetIntensity(lightId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetRadius(lightId As Integer, radius As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetRadius(lightId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetEnabled(lightId As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_IsEnabled(lightId As Integer) As Boolean
    End Function

    ' ---- Spot Lights ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_CreateSpot(x As Single, y As Single, radius As Single, angle As Single, coneAngle As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetDirection(lightId As Integer, angle As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetDirection(lightId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetConeAngle(lightId As Integer, angle As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetConeAngle(lightId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetSoftEdge(lightId As Integer, softness As Single)
    End Sub

    ' ---- Directional Light (Global) ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetDirectionalAngle(angle As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetDirectionalColor(r As Byte, g As Byte, b As Byte)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetDirectionalIntensity(intensity As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetDirectionalEnabled(enabled As Boolean)
    End Sub

    ' ---- Light Properties ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetFalloff(lightId As Integer, falloff As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetFalloff(lightId As Integer) As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetFlicker(lightId As Integer, amount As Single, speed As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetPulse(lightId As Integer, minIntensity As Single, maxIntensity As Single, speed As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_SetLayer(lightId As Integer, layer As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetLayer(lightId As Integer) As Integer
    End Function

    ' ---- Light Attachment ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_AttachToEntity(lightId As Integer, entityId As Integer, offsetX As Single, offsetY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_Detach(lightId As Integer)
    End Sub

    ' ---- Shadow Occluders ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Shadow_CreateBox(x As Single, y As Single, width As Single, height As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Shadow_CreateCircle(x As Single, y As Single, radius As Single) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Shadow_CreatePolygon(points As Single(), pointCount As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shadow_Destroy(occluderId As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shadow_SetPosition(occluderId As Integer, x As Single, y As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shadow_SetRotation(occluderId As Integer, angle As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shadow_SetEnabled(occluderId As Integer, enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shadow_AttachToEntity(occluderId As Integer, entityId As Integer, offsetX As Single, offsetY As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shadow_Detach(occluderId As Integer)
    End Sub

    ' ---- Shadow Settings ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetShadowQuality(quality As Integer)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Lighting_GetShadowQuality() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetShadowBlur(blur As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetShadowColor(r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    ' ---- Day/Night Cycle ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetTimeOfDay(time As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Lighting_GetTimeOfDay() As Single
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetDayNightSpeed(speed As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetDayNightEnabled(enabled As Boolean)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetSunriseTime(hour As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetSunsetTime(hour As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetDayAmbient(r As Byte, g As Byte, b As Byte, intensity As Single)
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_SetNightAmbient(r As Byte, g As Byte, b As Byte, intensity As Single)
    End Sub

    ' ---- Rendering ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_BeginLightPass()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_EndLightPass()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_RenderToScreen()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Lighting_Update(deltaTime As Single)
    End Sub

    ' ---- Light Queries ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetCount() As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetAt(index As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetType(lightId As Integer) As Integer
    End Function

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Light_GetBrightnessAt(x As Single, y As Single) As Single
    End Function

    ' ---- Global Management ----
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Light_DestroyAll()
    End Sub

    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shadow_DestroyAll()
    End Sub
#End Region

#Region "Cleanup"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ResourcesShutdown()
    End Sub
#End Region

End Module
