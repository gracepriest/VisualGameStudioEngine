Imports System.Runtime.InteropServices

Public Module FrameworkWrapper

    ' Define structures used by the framework
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Vector2
        Public X As Single
        Public Y As Single

        Public Sub New(x As Single, y As Single)
            Me.X = x
            Me.Y = y
        End Sub
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure Rectangle
        Public X As Single
        Public Y As Single
        Public Width As Single
        Public Height As Single

        Public Sub New(x As Single, y As Single, width As Single, height As Single)
            Me.X = x
            Me.Y = y
            Me.Width = width
            Me.Height = height
        End Sub
    End Structure

    ' Define the callback delegate
    Public Delegate Sub DrawCallback()

    ' Window management
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_Initialize(width As Integer, height As Integer, title As String) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Update()
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_ShouldClose() As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_Shutdown()
    End Sub

    ' Callback functions
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetDrawCallback(callback As DrawCallback)
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_BeginDrawing()
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_EndDrawing()
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ClearBackground(r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawText(text As String, x As Integer, y As Integer, fontSize As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawRectangle(x As Integer, y As Integer, width As Integer, height As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawLine(startPosX As Integer, startPosY As Integer, endPosX As Integer, endPosY As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DrawCircle(startPosX As Integer, startPosY As Integer, endPosX As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub

    ' Keyboard input
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyPressed(key As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyPressedRepeat(key As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyDown(key As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyReleased(key As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsKeyUp(key As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetKeyPressed() As Integer
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetCharPressed() As Integer
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetExitKey(key As Integer)
    End Sub

    ' Mouse input
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseX() As Integer
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseY() As Integer
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonPressed(button As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonDown(button As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonReleased(button As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsMouseButtonUp(button As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMousePosition() As Vector2
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseDelta() As Vector2
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMousePosition(x As Integer, y As Integer)
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMouseOffset(offsetX As Integer, offsetY As Integer)
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMouseScale(scaleX As Single, scaleY As Single)
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseWheelMove() As Single
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetMouseWheelMoveV() As Vector2
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetMouseCursor(cursor As Integer)
    End Sub

    ' Cursor-related functions
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ShowCursor()
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_HideCursor()
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsCursorHidden() As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_EnableCursor()
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_DisableCursor()
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_IsCursorOnScreen() As Boolean
    End Function

    ' Timing-related functions
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_SetTargetFPS(fps As Integer)
    End Sub

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFrameTime() As Single
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetTime() As Double
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetFPS() As Integer
    End Function

    ' Collision detection functions
    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionRecs(rec1 As Rectangle, rec2 As Rectangle) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionCircles(center1 As Vector2, radius1 As Single, center2 As Vector2, radius2 As Single) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionCircleRec(center As Vector2, radius As Single, rec As Rectangle) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionCircleLine(center As Vector2, radius As Single, p1 As Vector2, p2 As Vector2) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointRec(point As Vector2, rec As Rectangle) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointCircle(point As Vector2, center As Vector2, radius As Single) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointTriangle(point As Vector2, p1 As Vector2, p2 As Vector2, p3 As Vector2) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointLine(point As Vector2, p1 As Vector2, p2 As Vector2, threshold As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionPointPoly(point As Vector2, points As Vector2(), pointCount As Integer) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_CheckCollisionLines(startPos1 As Vector2, endPos1 As Vector2, startPos2 As Vector2, endPos2 As Vector2, ByRef collisionPoint As Vector2) As Boolean
    End Function

    <DllImport("VisualGameStudioEngine.dll", CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_GetCollisionRec(rec1 As Rectangle, rec2 As Rectangle) As Rectangle
    End Function

End Module