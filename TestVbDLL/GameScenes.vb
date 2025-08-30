Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices


Class TitleScene
    Inherits Scene

    Protected Overrides Sub OnEnter()
        Console.WriteLine("TitleScene OnEnter")
    End Sub
    Protected Overrides Sub OnExit()
        Console.WriteLine("TitleScene OnExit")
    End Sub
    Protected Overrides Sub OnResume()
        Console.WriteLine("TitleScene OnResume")
    End Sub
    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub
    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ENTER -> go to Game
        If Framework_IsKeyPressed(257) Then
            Framework_PlaySoundH(sfxHit)
            SetCurrentScene(New MenuScene)
        End If
    End Sub
    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(100, 149, 237, 255)
        Dim temp As String = "Title Scene - Press SPACE to Start"
        Dim textWidth As Integer = 10 * (temp.Length())
        Dim x As Integer = (800 - textWidth) / 2
        Dim y As Integer = 200
        Framework_DrawText(temp, x, y, 20, 255, 255, 255, 255)
    End Sub
End Class

Class MenuScene
    Inherits Scene
    Dim x As Single = 100, y As Single = 150, vx As Single = 120.0F, vy As Single = 0.0F, g As Single = 800.0F
    Protected Overrides Sub OnEnter()
        Console.WriteLine("TitleScene OnEnter")
    End Sub
    Protected Overrides Sub OnExit()
        Console.WriteLine("TitleScene OnExit")
    End Sub
    Protected Overrides Sub OnResume()
        Console.WriteLine("TitleScene OnResume")
    End Sub
    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub
    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ENTER -> go to Game
        If Framework_IsKeyPressed(257) Then
            SetCurrentScene(New TitleScene)
        End If
        ' Simple physics
        vy += g * CSng(dt)
        x += vx * CSng(dt)
        y += vy * CSng(dt)

        If x < 0 Then x = 0 : vx = Math.Abs(vx)
        If x > 780 Then x = 780 : vx = -Math.Abs(vx)
        If y > 430 Then y = 430 : vy = -Math.Abs(vy) * 0.6F
    End Sub
    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(10, 10, 20, 255)
        Framework_DrawText("GAME SCENE (Backspace to Title)", 20, 14, 20, 255, 255, 255, 255)
        Framework_DrawRectangle(CInt(x), CInt(y), 20, 20, 120, 220, 255, 255)
        Framework_DrawFPS(700, 10)
    End Sub
End Class
