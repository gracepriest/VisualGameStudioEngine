Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices


Class TitleScene
    Inherits Scene
    Dim paddle1 As New Paddle
    Dim paddle2 As New Paddle
    Dim menuIndex As Integer = 0
    Dim temp As String = "Title Scene - Press SPACE to Start"
    'pong text centering
    Dim txtPong As String = "PONG"
    Dim txtPlayer1 As String = "1 PLAYER"
    Dim txtPlayer2 As String = "2 PLAYER"
    Dim txtOptions As String = "  OPTIONS"
    'locations
    Dim textWidth As Integer = 10 * (temp.Length())
    Dim x As Integer = (800 - textWidth) / 2
    Dim y As Integer = WINDOW_HEIGHT / 2 - 10
    Dim menuPos As New Rectangle(WINDOW_WIDTH / 2.5, y, 365, 40)

    Protected Overrides Sub OnEnter()
        Console.WriteLine("TitleScene OnEnter")
        paddle2.paddlePos.X = 1130.0F

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
        'paddle1.Update(dt)
        '' is hold down
        'If Framework_IsKeyDown(Keys.UP) Then
        '    paddle1.dy -= paddle1.paddleSpeed
        'ElseIf Framework_IsKeyDown(Keys.DOWN) Then
        '    paddle1.dy += paddle1.paddleSpeed
        'Else
        '    paddle1.dy = 0.0F
        'End If
        If Framework_IsKeyPressed(Keys.UP And menuIndex > 0) Then
            menuIndex -= 1
            menuPos.Y -= 50
        ElseIf Framework_IsKeyPressed(Keys.DOWN And menuIndex < 2) Then
            menuIndex += 1
            menuPos.Y += 50
        End If
        If Framework_IsKeyPressed(Keys.SPACE) Then
            Select Case menuIndex
                Case 0
                'change scene to 1 player

                Case 1
                'start a 2 player game
                Case 2
                    'Options Menu
            End Select
        End If

    End Sub
    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(46, 67, 111, 255)


        'paddle
        paddle1.Draw()
        paddle2.Draw()
        'Framework_DrawText(temp, x, y, 20, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPong, New Vector2(WINDOW_WIDTH / 2.8, 50), 100, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPlayer1, New Vector2(WINDOW_WIDTH / 2.5, y), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPlayer2, New Vector2(WINDOW_WIDTH / 2.5, y + 50), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtOptions, New Vector2(WINDOW_WIDTH / 2.5, y + 100), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, temp, New Vector2(WINDOW_WIDTH / 3.8, y + 170), 20, 1.0F, 255, 255, 255, 255)
        menuPos.DrawRectangle(255, 0, 0, 90)
        'RETRO_FONT.DrawText(temp, New Vector2(x, y), 20, 1.0F, 255, 255, 255, 255)
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
