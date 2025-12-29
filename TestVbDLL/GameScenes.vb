Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices


Class TitleScene
    Inherits Scene
    Dim paddle1 As New Paddle
    Dim paddle2 As New Paddle
    Dim player1 As New Player(1, "PLAYER 1", 0, paddle1)
    Dim player2 As New Player(2, "PLAYER 2", 0, paddle2)
    Dim menuIndex As Integer = 0
    Dim temp As String = "Title Scene - Press SPACE to Start"
    'pong text centering
    Dim txtPong As String = "PONG"
    Dim txtPlayer1 As String = "1 PLAYER"
    Dim txtPlayer2 As String = "2 PLAYER"
    Dim txtOptions As String = "  OPTIONS"
    Dim txt2DDemo As String = "  2D DEMO"
    'locations
    Dim textWidth As Integer = 10 * (temp.Length())
    Dim x As Integer = (800 - textWidth) / 2
    Dim y As Integer = WINDOW_HEIGHT / 2 - 10
    Dim menuPos As New Rectangle(WINDOW_WIDTH / 2.5, y, 365, 40)
    'Dim atlas As New TextureHandle("images/blocks.png")

    ' Tell it the tile geometry of your sheet:
    ' (example numbers—measure your tile width/height and cols/rows)
    'Dim tiles = SliceGrid(frameW:=32, frameH:=16, columns:=6, rows:=12, spacingX:=0)
    'Dim pong = SliceGrid(frameW:=190, frameH:=70, columns:=1, rows:=1, spacingX:=0)
    'Dim atlasTexture As New TextureHandle("images/blocks.png")
    'Dim atlas1 As New SpriteAtlas(atlasTexture)
    'Dim s1 As New Sprite(atlas1, "block", New Vector2(50, 50))


    Protected Overrides Sub OnEnter()
        Console.WriteLine("TitleScene OnEnter")
        player1.gPaddle.setSide("left")
        paddle2.setSide("right")
        'player2.gPaddle.ChangeSprite("block")
        player1.gPaddle.setColor(255, 0, 0, 255)
        player2.gPaddle.setColor(0, 0, 255, 255)

        'atlas1.Add("block", New Rectangle(0, 0, 32, 16))



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

        'If Framework_IsKeyPressed(257) Then
        '    Framework_PlaySoundH(sfxHit)
        '    SetCurrentScene(New MenuScene)
        'End If
        'paddle1.Update(dt)
        '' is hold down
        'If Framework_IsKeyDown(Keys.UP) Then
        '    paddle1.dy -= paddle1.paddleSpeed
        'ElseIf Framework_IsKeyDown(Keys.DOWN) Then
        '    paddle1.dy += paddle1.paddleSpeed
        'Else
        '    paddle1.dy = 0.0F
        'End If
        If Framework_IsKeyPressed(Keys.UP) AndAlso menuIndex > 0 Then
            'play sound
            Framework_PlaySoundH(sfxWall)
            menuIndex -= 1
            menuPos.Y -= 50

        ElseIf Framework_IsKeyPressed(Keys.DOWN) AndAlso menuIndex < 3 Then
            'play sound
            Framework_PlaySoundH(sfxWall)
            menuIndex += 1
            menuPos.Y += 50

        End If
        If Framework_IsKeyPressed(Keys.SPACE) Then

            Select Case menuIndex
                Case 0
                    'change scene to 1 player
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New Player1Scene)

                Case 1
                    'start a 2 player game
                    Push(New Player2Scene)
                Case 2
                    'Options Menu
                    Push(New OptionScene)
                Case 3
                    '2D Systems Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New Demo2DScene)
            End Select
        End If

    End Sub
    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(46, 67, 111, 255)


        'paddle
        player1.gPaddle.Draw()
        player2.gPaddle.Draw()
        'Framework_DrawText(temp, x, y, 20, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPong, New Vector2(WINDOW_WIDTH / 2.8, 50), 100, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPlayer1, New Vector2(WINDOW_WIDTH / 2.5, y), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPlayer2, New Vector2(WINDOW_WIDTH / 2.5, y + 50), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtOptions, New Vector2(WINDOW_WIDTH / 2.6, y + 100), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txt2DDemo, New Vector2(WINDOW_WIDTH / 2.6, y + 150), 40, 1.0F, 100, 255, 100, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, temp, New Vector2(WINDOW_WIDTH / 3.8, y + 220), 20, 1.0F, 255, 255, 255, 255)
        menuPos.DrawRectangle(255, 0, 0, 90)
        'draw tile #0 at (50, 50)
        'atlas.DrawRec(pong(0), New Vector2(50, 80))

        '' Draw tile #13 at (100,80)
        'atlas.DrawRec(tiles(1), New Vector2(100, 80))
        'atlas.DrawRec(tiles(2), New Vector2(150, 80))
        'atlas.DrawRec(tiles(3), New Vector2(200, 80))
        'atlas.DrawRec(tiles(4), New Vector2(250, 80))
        'atlas.DrawRec(tiles(5), New Vector2(300, 80))
        'atlas.DrawRec(tiles(6), New Vector2(350, 80))
        'atlas.DrawRec(tiles(7), New Vector2(400, 80))
        'atlas.DrawRec(tiles(8), New Vector2(450, 80))
        'loop to draw the rest of the tiles in a grid
        'Const COLS As Integer = 6
        'Const ROWS As Integer = 3
        'Dim maxTiles As Integer = Math.Min(tiles.Count, COLS * ROWS)

        'Dim startX As Integer = 50
        'Dim startY As Integer = 120
        'Dim offsetX As Integer = 50
        'Dim offsetY As Integer = 90

        'For i As Integer = 0 To maxTiles - 1
        '    Dim col As Integer = i Mod COLS
        '    Dim row As Integer = i \ COLS
        '    Dim posX As Integer = startX + col * offsetX
        '    Dim posY As Integer = startY + row * offsetY
        '    'atlas.DrawRec(tiles(i), New Vector2(posX, posY))
        '    atlas.DrawPro(tiles(i), New Rectangle(posX, posY, 32 * 2, 16 * 2), New Vector2(0, 0), 90.0F, 255, 255, 255, 255)
        'Next

        'For i As Integer = maxTiles To tiles.Count - 1
        '    Dim col As Integer = i Mod COLS
        '    Dim row As Integer = i \ COLS
        '    Dim posX As Integer = startX + col * offsetX
        '    Dim posY As Integer = startY + row * offsetY
        '    'atlas.DrawRec(tiles(i), New Vector2(posX, posY))
        '    atlas.DrawPro(tiles(i), New Rectangle(posX, posY, 32 * 2, 16 * 2), New Vector2(0, 0), 90.0F, 255, 255, 255, 255)
        'Next
        's1.setScale(2.0F)
        's1.setRotation(90.0F)
        's1.Draw()

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

Class Player1Scene
    Inherits Scene

    Dim paddle1 As New Paddle
    Dim paddle2 As New Paddle
    Dim player1 As New Player(1, "PLAYER 1", 0, paddle1)
    Dim player2 As New Player(2, "PLAYER 2", 0, paddle2)

    Dim ball1 As New Ball
    Dim player1Score As Integer = 0
    Dim player2Score As Integer = 0
    Dim txtPlayer1Name As String = "PLAYER 1"
    Dim txtPlayer2Name As String = "PLAYER 2"
    Dim servingPlayer As Integer = 1
    Dim rand As New Random()



    Public Sub New()
        player1.gPaddle.setSide("left")
        player2.gPaddle.setSide("right")
        player1.Score = 0
        player2.Score = 0
        player1.Name = txtPlayer1Name
        player2.Name = txtPlayer2Name
        player1.gPaddle.setColor(255, 0, 0, 255)
        player2.gPaddle.isAI = True
        player2.gPaddle.getBall(ball1)

    End Sub
    Protected Overrides Sub OnEnter()
        Console.WriteLine("PlayScene OnEnter")

        'push serve scene
        Push(New ServeScene(txtPlayer1Name, player1, player2))
    End Sub
    Protected Overrides Sub OnExit()
        Console.WriteLine("PlayScene OnExit")
    End Sub
    Protected Overrides Sub OnResume()
        Console.WriteLine("PlayScene OnResume")
        'reset ball
        ball1.BallReSet()
        player1.gPaddle.reset()
        player2.gPaddle.reset()
        'init ball velocity if it's stationary base on serving player
        ball1.dy = rand.Next(-50, 50)

        If servingPlayer = 1 Then
            ball1.dx = rand.Next(175, 300)
        Else
            ball1.dx = -rand.Next(175, 300)
        End If

    End Sub
    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub
    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ENTER -> go to Game
        If Framework_IsKeyPressed(Keys.BACKSPACE) Then
            ChangeTo(New TitleScene)
        End If


        'detect ball collision with paddles
        If ball1.AABBCheck(paddle1) Then
            'Framework_PlaySoundH(sfxHit)
            'reverse x direction
            ball1.dx = -ball1.dx * 1.2F 'increase speed by 3% each hit
            ball1.ballPos.X = paddle1.paddlePos.X + paddle1.paddleWidth 'move ball outside of paddle to prevent sticking
            'randomize y velocity
            If ball1.dy <= 0 Then
                'draw a hit text for testing
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = -CSng(rand.Next(10, 200))
            Else
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = CSng(rand.Next(10, 200))
            End If

        End If

        If ball1.AABBCheck(paddle2) Then
            'Framework_PlaySoundH(sfxHit)
            'reverse x direction
            ball1.dx = -ball1.dx * 1.2F 'increase speed by 3% each hit
            ball1.ballPos.X = paddle2.paddlePos.X - ball1.ballWidth 'move ball outside of paddle to prevent sticking
            'randomize y velocity
            If ball1.dy <= 0 Then
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = -CSng(rand.Next(10, 200))
            Else
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = CSng(rand.Next(10, 200))
            End If
        End If
        'detect Ball out of bounds
        'limit Ball to screen
        If ball1.ballPos.y < 0 Then
            ball1.ballPos.y = 0
            ball1.dy = -ball1.dy
        End If
        If ball1.ballPos.y >= WINDOW_HEIGHT Then
            ball1.ballPos.y = WINDOW_HEIGHT - ball1.ballHeight
            ball1.dy = -ball1.dy
            'hit wall sound
        End If

        If ball1.ballPos.x < 0 Then
            'player 2 scores
            player2.Score += 1
            servingPlayer = 2
            'play score sound
            'check for win condition
            If player2.Score >= 10 Then
                'player 2 wins
                'change to win scene
                'winningPlayer = 2
                ChangeTo(New EndScene(player2))
            Else
                'change to serve scene
                Push(New ServeScene(txtPlayer1Name, player1, player2))
            End If

        End If

        If ball1.ballPos.x > WINDOW_WIDTH - ball1.ballWidth Then
            'player 1 scores
            player1.Score += 1
            servingPlayer = 1
            'play score sound
            'check for win condition
            If player1.Score >= 10 Then
                'player 1 wins
                'change to win scene
                'winningPlayer = 1
                ChangeTo(New EndScene(player1))
            Else
                'change to serve scene
                Push(New ServeScene(txtPlayer1Name, player1, player2))
            End If
        End If


        ' Player 1 controls
        ' is hold down
        If Framework_IsKeyDown(Keys.UP) Then
            paddle1.dy -= paddle1.paddleSpeed
        ElseIf Framework_IsKeyDown(Keys.DOWN) Then
            paddle1.dy += paddle1.paddleSpeed
        Else
            paddle1.dy = 0.0F
        End If
        'player 2 controls
        If Framework_IsKeyDown(Keys.W) Then
            paddle2.dy -= paddle1.paddleSpeed
        ElseIf Framework_IsKeyDown(Keys.S) Then
            paddle2.dy += paddle1.paddleSpeed
        Else
            paddle2.dy = 0.0F
        End If
        'paddle1.Update(dt)
        'paddle2.Update(dt)
        ball1.Update(dt)
        'player 1 controls
        player1.gPaddle.Update(dt)
        player2.gPaddle.Update(dt)


        ' Player 2 controls
        ' is hold down

    End Sub
    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(10, 10, 20, 255)
        Framework_DrawText(player1.Score.ToString, WINDOW_WIDTH / 2 - 150, 20, 100, 255, 255, 255, 255)
        Framework_DrawText(player2.Score.ToString, WINDOW_WIDTH / 2 + 150, 20, 100, 255, 255, 255, 255)
        'paddle1.Draw()
        'paddle2.Draw()
        player1.gPaddle.Draw()
        player2.gPaddle.Draw()
        ball1.Draw()

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

Class Player2Scene
    Inherits Scene

    Dim paddle1 As New Paddle
    Dim paddle2 As New Paddle
    Dim player1 As New Player(1, "PLAYER 1", 0, paddle1)
    Dim player2 As New Player(2, "PLAYER 2", 0, paddle2)

    Dim ball1 As New Ball
    Dim player1Score As Integer = 0
    Dim player2Score As Integer = 0
    Dim txtPlayer1Name As String = "PLAYER 1"
    Dim txtPlayer2Name As String = "PLAYER 2"
    Dim servingPlayer As Integer = 1
    Dim rand As New Random()



    Public Sub New()
        player1.gPaddle.setSide("left")
        player2.gPaddle.setSide("right")
        player1.Score = 0
        player2.Score = 0
        player1.Name = txtPlayer1Name
        player2.Name = txtPlayer2Name
        player1.gPaddle.setColor(255, 0, 0, 255)
        player2.gPaddle.isAI = False
        player2.gPaddle.getBall(ball1)

    End Sub
    Protected Overrides Sub OnEnter()
        Console.WriteLine("PlayScene OnEnter")

        'push serve scene
        Push(New ServeScene(txtPlayer1Name, player1, player2))
    End Sub
    Protected Overrides Sub OnExit()
        Console.WriteLine("PlayScene OnExit")
    End Sub
    Protected Overrides Sub OnResume()
        Console.WriteLine("PlayScene OnResume")
        'reset ball
        ball1.BallReSet()
        player1.gPaddle.reset()
        player2.gPaddle.reset()
        'init ball velocity if it's stationary base on serving player
        ball1.dy = rand.Next(-50, 50)

        If servingPlayer = 1 Then
            ball1.dx = rand.Next(175, 300)
        Else
            ball1.dx = -rand.Next(175, 300)
        End If

    End Sub
    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub
    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ENTER -> go to Game
        If Framework_IsKeyPressed(Keys.BACKSPACE) Then
            ChangeTo(New TitleScene)
        End If


        'detect ball collision with paddles
        If ball1.AABBCheck(paddle1) Then
            'Framework_PlaySoundH(sfxHit)
            'reverse x direction
            ball1.dx = -ball1.dx * 1.2F 'increase speed by 3% each hit
            ball1.ballPos.x = paddle1.paddlePos.x + paddle1.paddleWidth 'move ball outside of paddle to prevent sticking
            'randomize y velocity
            If ball1.dy <= 0 Then
                'draw a hit text for testing
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = -CSng(rand.Next(10, 200))
            Else
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = CSng(rand.Next(10, 200))
            End If

        End If

        If ball1.AABBCheck(paddle2) Then
            'Framework_PlaySoundH(sfxHit)
            'reverse x direction
            ball1.dx = -ball1.dx * 1.2F 'increase speed by 3% each hit
            ball1.ballPos.x = paddle2.paddlePos.x - ball1.ballWidth 'move ball outside of paddle to prevent sticking
            'randomize y velocity
            If ball1.dy <= 0 Then
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = -CSng(rand.Next(10, 200))
            Else
                Framework_DrawText("HIT!", 400, 200, 30, 255, 0, 0, 255)
                ball1.dy = CSng(rand.Next(10, 200))
            End If
        End If
        'detect Ball out of bounds
        'limit Ball to screen
        If ball1.ballPos.y < 0 Then
            ball1.ballPos.y = 0
            ball1.dy = -ball1.dy
        End If
        If ball1.ballPos.y >= WINDOW_HEIGHT Then
            ball1.ballPos.y = WINDOW_HEIGHT - ball1.ballHeight
            ball1.dy = -ball1.dy
            'hit wall sound
        End If

        If ball1.ballPos.x < 0 Then
            'player 2 scores
            player2.Score += 1
            servingPlayer = 2
            'play score sound
            'check for win condition
            If player2.Score >= 10 Then
                'player 2 wins
                'change to win scene
                'winningPlayer = 2
                ChangeTo(New EndScene(player2))
            Else
                'change to serve scene
                Push(New ServeScene(txtPlayer1Name, player1, player2))
            End If

        End If

        If ball1.ballPos.x > WINDOW_WIDTH - ball1.ballWidth Then
            'player 1 scores
            player1.Score += 1
            servingPlayer = 1
            'play score sound
            'check for win condition
            If player1.Score >= 10 Then
                'player 1 wins
                'change to win scene
                'winningPlayer = 1
                ChangeTo(New EndScene(player1))
            Else
                'change to serve scene
                Push(New ServeScene(txtPlayer1Name, player1, player2))
            End If
        End If


        ' Player 1 controls
        ' is hold down
        If Framework_IsKeyDown(Keys.UP) Then
            paddle1.dy -= paddle1.paddleSpeed
        ElseIf Framework_IsKeyDown(Keys.DOWN) Then
            paddle1.dy += paddle1.paddleSpeed
        Else
            paddle1.dy = 0.0F
        End If
        'player 2 controls
        If Framework_IsKeyDown(Keys.W) Then
            paddle2.dy -= paddle1.paddleSpeed
        ElseIf Framework_IsKeyDown(Keys.S) Then
            paddle2.dy += paddle1.paddleSpeed
        Else
            paddle2.dy = 0.0F
        End If
        'paddle1.Update(dt)
        'paddle2.Update(dt)
        ball1.Update(dt)
        'player 1 controls
        player1.gPaddle.Update(dt)
        player2.gPaddle.Update(dt)


        ' Player 2 controls
        ' is hold down

    End Sub
    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(10, 10, 20, 255)
        Framework_DrawText(player1.Score.ToString, WINDOW_WIDTH / 2 - 150, 20, 100, 255, 255, 255, 255)
        Framework_DrawText(player2.Score.ToString, WINDOW_WIDTH / 2 + 150, 20, 100, 255, 255, 255, 255)
        'paddle1.Draw()
        'paddle2.Draw()
        player1.gPaddle.Draw()
        player2.gPaddle.Draw()
        ball1.Draw()

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

Class OptionScene
    Inherits Scene
    Dim txtPong As String = "PONG"
    Dim txtPlayer1 As String = "1 PLAYER"
    Dim txtPlayer2 As String = "2 PLAYER"
    Dim txtOptions As String = "  OPTIONS"
    Dim temp As String = "Title Scene - Press BACK SPACE to Start"
    Dim menuIndex As Integer = 0
    'locations
    Dim textWidth As Integer = 10 * (temp.Length())
    Dim x As Integer = (800 - textWidth) / 2
    Dim y As Integer = WINDOW_HEIGHT / 2 - 10
    Dim menuPos As New Rectangle(WINDOW_WIDTH / 2.5, y, 365, 40)

    Protected Overrides Sub OnEnter()
        Console.WriteLine("OtionScene OnEnter")
    End Sub
    Protected Overrides Sub OnExit()
        Console.WriteLine("OptionScene OnExit")

    End Sub
    Protected Overrides Sub OnResume()
        Console.WriteLine("OptionScene OnResume")

    End Sub
    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.UP) AndAlso menuIndex > 0 Then
            'play sound
            Framework_PlaySoundH(sfxWall)
            menuIndex -= 1
            menuPos.Y -= 50

        ElseIf Framework_IsKeyPressed(Keys.DOWN) AndAlso menuIndex < 2 Then
            'play sound
            Framework_PlaySoundH(sfxWall)
            menuIndex += 1
            menuPos.Y += 50

        End If

        If Framework_IsKeyPressed(Keys.BACKSPACE) Then
            Pop()
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(46, 67, 111, 255)
        Framework_DrawRectangle(250, 150, 800, 450, 100, 100, 100, 255)
        'Framework_DrawText(temp, x, y, 20, 255, 255, 255, 255)
        'Framework_DrawTextExH(RETRO_FONT.Handle, txtPong, New Vector2(WINDOW_WIDTH / 2.8, 50), 100, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPlayer1, New Vector2(WINDOW_WIDTH / 2.5, y), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPlayer2, New Vector2(WINDOW_WIDTH / 2.5, y + 50), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtOptions, New Vector2(WINDOW_WIDTH / 2.6, y + 100), 40, 1.0F, 255, 255, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, temp, New Vector2(WINDOW_WIDTH / 4.7, y + 170), 19, 1.0F, 255, 255, 255, 255)
        menuPos.DrawRectangle(255, 0, 0, 90)
    End Sub
End Class

Class ServeScene
    Inherits Scene
    Dim paddle1 As Player
    Dim paddle2 As Player
    Dim ball1 As New Ball
    Dim player1Score As Integer = 0
    Dim player2Score As Integer = 0
    Dim txtPlayer1Name As String = "PLAYER 1"
    Dim txtPlayer2Name As String = "PLAYER 2"
    Dim servingPlayer As Integer = 0
    Dim rand As New Random()
    Dim txtPlayerName As String = ""
    Dim txtServe As String = "SERVE!"
    Dim txtMessage As String = "PRESS SPACE TO SERVE!"


    Public Sub New(player As String, p1 As Player, p2 As Player)
        paddle1 = p1
        paddle2 = p2
    End Sub
    Protected Overrides Sub OnEnter()
        Console.WriteLine("ServeScene OnEnter")
        paddle1.gPaddle.reset()
        paddle2.gPaddle.reset()
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("ServeScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
        Console.WriteLine("ServeScene OnResume")
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)

    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.SPACE) Then
            'change to play scene
            Pop()
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        'draw the serve screen
        Framework_ClearBackground(10, 10, 20, 255)
        Framework_DrawText(txtPlayerName, WINDOW_WIDTH / 2 - 275, 250, 50, 255, 255, 255, 255)
        Framework_DrawText(txtMessage, WINDOW_WIDTH / 2 - 275, 300, 50, 255, 255, 255, 255)
        Framework_DrawText(player1Score.ToString, WINDOW_WIDTH / 2 - 150, 20, 100, 255, 255, 255, 255)
        Framework_DrawText(player2Score.ToString, WINDOW_WIDTH / 2 + 150, 20, 100, 255, 255, 255, 255)
        paddle1.gPaddle.Draw()
        paddle2.gPaddle.Draw()
        ball1.Draw()

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

'End scene
Class EndScene
    Inherits Scene

    Public winningPlayer As Player

    Public Sub New(p1 As Player)
        winningPlayer = p1
    End Sub

    Protected Overrides Sub OnEnter()

    End Sub

    Protected Overrides Sub OnExit()

    End Sub

    Protected Overrides Sub OnResume()

    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)

    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        'return to title on space
        If Framework_IsKeyPressed(Keys.SPACE) Then
            ChangeTo(New TitleScene)
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        'draw the win screen
        Framework_ClearBackground(10, 10, 20, 255)
        Framework_DrawText(winningPlayer.Name & " WINS!", WINDOW_WIDTH / 2 - 275, 250, 50, 255, 255, 255, 255)
        Framework_DrawText("PRESS SPACE TO RETURN TO TITLE!", WINDOW_WIDTH / 2 - 275, 300, 25, 255, 255, 255, 255)
        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ============================================================================
' Demo2DScene - Showcases Tilemap, Animation, and Particle systems
' ============================================================================
Class Demo2DScene
    Inherits Scene

    ' Tilemap resources
    Private _tilesetHandle As Integer = 0
    Private _tilemapEntity As Integer = -1
    Private Const MAP_WIDTH As Integer = 25
    Private Const MAP_HEIGHT As Integer = 15
    Private Const TILE_SIZE As Integer = 32

    ' Animation resources
    Private _animClipHandle As Integer = 0
    Private _animatedEntity As Integer = -1
    Private _animPosX As Single = 400.0F
    Private _animPosY As Single = 300.0F
    Private _animVelX As Single = 100.0F
    Private _animVelY As Single = 80.0F

    ' Particle resources
    Private _particleEntity As Integer = -1
    Private _particleTexHandle As Integer = 0

    ' Demo state
    Private _showTilemap As Boolean = True
    Private _showAnimation As Boolean = True
    Private _showParticles As Boolean = True

    Protected Overrides Sub OnEnter()
        Console.WriteLine("Demo2DScene OnEnter - Showcasing 2D Systems")

        ' Create a tileset using procedural colored tiles (no texture needed)
        ' We'll draw the tilemap manually using colored rectangles
        SetupTilemap()
        SetupAnimation()
        SetupParticles()
    End Sub

    Private Sub SetupTilemap()
        ' Create tilemap entity with ECS
        _tilemapEntity = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(_tilemapEntity, "Tilemap")

        ' Create a simple tileset (using handle 0 means no texture - we'll draw procedurally)
        _tilesetHandle = Framework_Tileset_Create(0, TILE_SIZE, TILE_SIZE, 4)

        ' Add tilemap component
        Framework_Ecs_AddTilemap(_tilemapEntity, _tilesetHandle, MAP_WIDTH, MAP_HEIGHT)

        ' Mark wall tiles as solid for collision
        Framework_Ecs_SetTileCollision(_tilemapEntity, 2, True)

        ' Define tile types: 0=empty, 1=ground, 2=wall, 3=decoration
        ' Create a simple level layout
        For y As Integer = 0 To MAP_HEIGHT - 1
            For x As Integer = 0 To MAP_WIDTH - 1
                Dim tileId As Integer = 0

                ' Border walls
                If x = 0 OrElse x = MAP_WIDTH - 1 OrElse y = 0 OrElse y = MAP_HEIGHT - 1 Then
                    tileId = 2 ' Wall
                    ' Ground floor
                ElseIf y = MAP_HEIGHT - 2 Then
                    tileId = 1 ' Ground
                    ' Some platforms
                ElseIf y = 5 AndAlso x >= 5 AndAlso x <= 10 Then
                    tileId = 1
                ElseIf y = 8 AndAlso x >= 14 AndAlso x <= 20 Then
                    tileId = 1
                    ' Decorations
                ElseIf (x + y) Mod 7 = 0 AndAlso y > 1 AndAlso y < MAP_HEIGHT - 2 Then
                    tileId = 3
                End If

                Framework_Ecs_SetTile(_tilemapEntity, x, y, tileId)
            Next
        Next
    End Sub

    Private Sub SetupAnimation()
        ' Create animated entity
        _animatedEntity = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(_animatedEntity, "AnimatedSprite")

        ' Create an animation clip with 4 frames (simulating a bouncing ball)
        _animClipHandle = Framework_AnimClip_Create("bounce", 4)

        ' Set up frames - each frame is a different "phase" of the bounce
        ' Using dummy source rects since we'll draw procedurally
        ' Parameters: clipHandle, frameIndex, srcX, srcY, srcW, srcH, duration
        Framework_AnimClip_SetFrame(_animClipHandle, 0, 0.0F, 0.0F, 32.0F, 32.0F, 0.1F)
        Framework_AnimClip_SetFrame(_animClipHandle, 1, 32.0F, 0.0F, 32.0F, 28.0F, 0.1F)
        Framework_AnimClip_SetFrame(_animClipHandle, 2, 64.0F, 0.0F, 32.0F, 24.0F, 0.1F)
        Framework_AnimClip_SetFrame(_animClipHandle, 3, 96.0F, 0.0F, 32.0F, 28.0F, 0.1F)

        ' Set loop mode to ping-pong for smooth back-and-forth
        Framework_AnimClip_SetLoopMode(_animClipHandle, 2) ' ANIM_LOOP_PINGPONG

        ' Add animator component to entity, then set clip
        Framework_Ecs_AddAnimator(_animatedEntity)
        Framework_Ecs_SetAnimatorClip(_animatedEntity, _animClipHandle)
        Framework_Ecs_AnimatorPlay(_animatedEntity)
        Framework_Ecs_AnimatorSetSpeed(_animatedEntity, 1.5F)
    End Sub

    Private Sub SetupParticles()
        ' Create particle emitter entity
        _particleEntity = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(_particleEntity, "ParticleEmitter")

        ' Add Transform2D for position (will follow mouse)
        Framework_Ecs_AddTransform2D(_particleEntity, 400, 300, 0.0F, 1.0F, 1.0F)

        ' Add particle emitter (no texture - we'll use colored particles)
        Framework_Ecs_AddParticleEmitter(_particleEntity, 0)

        ' Configure emitter properties
        Framework_Ecs_SetEmitterRate(_particleEntity, 30.0F)
        Framework_Ecs_SetEmitterMaxParticles(_particleEntity, 200)
        Framework_Ecs_SetEmitterLifetime(_particleEntity, 1.0F, 2.5F)
        Framework_Ecs_SetEmitterVelocity(_particleEntity, -50.0F, -150.0F, 50.0F, -50.0F)
        Framework_Ecs_SetEmitterColorStart(_particleEntity, 255, 200, 50, 255)
        Framework_Ecs_SetEmitterColorEnd(_particleEntity, 255, 50, 0, 0)
        Framework_Ecs_SetEmitterSize(_particleEntity, 10.0F, 3.0F)
        Framework_Ecs_SetEmitterGravity(_particleEntity, 0.0F, 100.0F)
        Framework_Ecs_SetEmitterSpread(_particleEntity, 45.0F)

        ' Start emitting
        Framework_Ecs_EmitterStart(_particleEntity)
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("Demo2DScene OnExit")

        ' Cleanup resources
        If _tilesetHandle > 0 Then
            Framework_Tileset_Destroy(_tilesetHandle)
        End If

        If _animClipHandle > 0 Then
            Framework_AnimClip_Destroy(_animClipHandle)
        End If

        If _tilemapEntity >= 0 Then
            Framework_Ecs_DestroyEntity(_tilemapEntity)
        End If

        If _animatedEntity >= 0 Then
            Framework_Ecs_DestroyEntity(_animatedEntity)
        End If

        If _particleEntity >= 0 Then
            Framework_Ecs_DestroyEntity(_particleEntity)
        End If
    End Sub

    Protected Overrides Sub OnResume()
        Console.WriteLine("Demo2DScene OnResume")
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
        ' Physics-like updates could go here
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ESC or BACKSPACE to return to title
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Toggle keys
        If Framework_IsKeyPressed(Keys.ONE) Then _showTilemap = Not _showTilemap
        If Framework_IsKeyPressed(Keys.TWO) Then _showAnimation = Not _showAnimation
        If Framework_IsKeyPressed(Keys.THREE) Then _showParticles = Not _showParticles

        ' Update animated entity position (bouncing around)
        _animPosX += _animVelX * dt
        _animPosY += _animVelY * dt

        ' Bounce off walls (inside the tilemap border)
        If _animPosX < TILE_SIZE + 10 OrElse _animPosX > (MAP_WIDTH - 1) * TILE_SIZE - 40 Then
            _animVelX = -_animVelX
            _animPosX = Math.Max(TILE_SIZE + 10, Math.Min(_animPosX, (MAP_WIDTH - 1) * TILE_SIZE - 40))
        End If
        If _animPosY < TILE_SIZE + 10 OrElse _animPosY > (MAP_HEIGHT - 1) * TILE_SIZE - 40 Then
            _animVelY = -_animVelY
            _animPosY = Math.Max(TILE_SIZE + 10, Math.Min(_animPosY, (MAP_HEIGHT - 1) * TILE_SIZE - 40))
        End If

        ' Update all animators globally
        Framework_Animators_Update(dt)

        ' Update particle emitter position to follow mouse
        Dim mouseX As Integer = Framework_GetMouseX()
        Dim mouseY As Integer = Framework_GetMouseY()
        Framework_Ecs_SetTransformPosition(_particleEntity, CSng(mouseX), CSng(mouseY))

        ' Toggle particle emission with mouse click
        If Framework_IsMouseButtonPressed(0) Then
            Dim isActive As Boolean = Framework_Ecs_EmitterIsActive(_particleEntity)
            If isActive Then
                Framework_Ecs_EmitterStop(_particleEntity)
            Else
                Framework_Ecs_EmitterStart(_particleEntity)
            End If
        End If

        ' Update all particles globally
        Framework_Particles_Update(dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(20, 20, 35, 255)

        ' Draw tilemap (procedural since no texture)
        If _showTilemap Then
            DrawProceduralTilemap()
        End If

        ' Draw animated sprite (procedural)
        If _showAnimation Then
            DrawProceduralAnimation()
        End If

        ' Draw particles
        If _showParticles Then
            Framework_Particles_Draw()
        End If

        ' Draw UI
        DrawUI()

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub

    Private Sub DrawProceduralTilemap()
        ' Draw each tile as a colored rectangle
        For y As Integer = 0 To MAP_HEIGHT - 1
            For x As Integer = 0 To MAP_WIDTH - 1
                Dim tileId As Integer = Framework_Ecs_GetTile(_tilemapEntity, x, y)
                Dim px As Integer = x * TILE_SIZE
                Dim py As Integer = y * TILE_SIZE

                Select Case tileId
                    Case 1 ' Ground - brown
                        Framework_DrawRectangle(px, py, TILE_SIZE - 1, TILE_SIZE - 1, 139, 90, 43, 255)
                    Case 2 ' Wall - dark gray
                        Framework_DrawRectangle(px, py, TILE_SIZE - 1, TILE_SIZE - 1, 60, 60, 70, 255)
                    Case 3 ' Decoration - blue sparkle
                        Framework_DrawCircle(px + TILE_SIZE \ 2, py + TILE_SIZE \ 2, 4, 100, 150, 255, 200)
                End Select
            Next
        Next
    End Sub

    Private Sub DrawProceduralAnimation()
        ' Get current animation frame
        Dim frameIndex As Integer = Framework_Ecs_AnimatorGetFrame(_animatedEntity)

        ' Draw animated sprite based on frame (simulating squash/stretch)
        Dim width As Integer = 32
        Dim height As Integer = 32

        Select Case frameIndex
            Case 0 : height = 32 : width = 32  ' Normal
            Case 1 : height = 28 : width = 36  ' Squash
            Case 2 : height = 24 : width = 40  ' More squash
            Case 3 : height = 28 : width = 36  ' Less squash
        End Select

        ' Center the sprite
        Dim drawX As Integer = CInt(_animPosX) - width \ 2
        Dim drawY As Integer = CInt(_animPosY) - height \ 2 + (32 - height) ' Anchor at bottom

        ' Draw bouncing ball with gradient effect
        Framework_DrawCircle(drawX + width \ 2, drawY + height \ 2, width \ 2, 255, 100, 100, 255)
        Framework_DrawCircle(drawX + width \ 2 - 5, drawY + height \ 2 - 5, width \ 4, 255, 200, 200, 200)
    End Sub

    Private Sub DrawUI()
        ' Title
        Framework_DrawText("2D SYSTEMS DEMO", 10, 10, 24, 255, 255, 255, 255)

        ' Instructions
        Dim y As Integer = WINDOW_HEIGHT - 120
        Framework_DrawText("Controls:", 10, y, 18, 200, 200, 200, 255)
        Framework_DrawText("[1] Toggle Tilemap: " & If(_showTilemap, "ON", "OFF"), 10, y + 22, 16, 150, 255, 150, 255)
        Framework_DrawText("[2] Toggle Animation: " & If(_showAnimation, "ON", "OFF"), 10, y + 42, 16, 150, 255, 150, 255)
        Framework_DrawText("[3] Toggle Particles: " & If(_showParticles, "ON", "OFF"), 10, y + 62, 16, 150, 255, 150, 255)
        Framework_DrawText("[Click] Toggle particle emission", 10, y + 82, 16, 150, 150, 255, 255)
        Framework_DrawText("[BACKSPACE] Return to menu", 10, y + 102, 16, 255, 150, 150, 255)

        ' Stats
        Dim particleCount As Integer = Framework_Ecs_EmitterGetParticleCount(_particleEntity)
        Framework_DrawText("Particles: " & particleCount.ToString(), WINDOW_WIDTH - 150, 40, 16, 255, 200, 100, 255)
    End Sub
End Class