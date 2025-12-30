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
    Dim txtUIDemo As String = "  UI DEMO"
    Dim txtPhysicsDemo As String = "PHYSICS DEMO"
    Dim txtShowcase As String = "SHOWCASE PONG"
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

        ElseIf Framework_IsKeyPressed(Keys.DOWN) AndAlso menuIndex < 6 Then
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
                Case 4
                    'UI System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoUIScene)
                Case 5
                    'Physics System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoPhysicsScene)
                Case 6
                    'Showcase Pong - ALL Framework Features
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New PongShowcaseScene)
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
        Framework_DrawTextExH(RETRO_FONT.Handle, txtUIDemo, New Vector2(WINDOW_WIDTH / 2.6, y + 200), 40, 1.0F, 100, 200, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtPhysicsDemo, New Vector2(WINDOW_WIDTH / 2.85, y + 250), 40, 1.0F, 255, 150, 100, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, txtShowcase, New Vector2(WINDOW_WIDTH / 2.8, y + 300), 40, 1.0F, 255, 215, 0, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, temp, New Vector2(WINDOW_WIDTH / 3.8, y + 370), 20, 1.0F, 255, 255, 255, 255)
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
' PongShowcaseScene - Demonstrates ALL Framework Features in a Pong game
' Features: Screen Effects, Achievements, Leaderboard, Tweening, Timers,
'           Particles, UI System, Camera Effects, Audio Groups
' ============================================================================
Class PongShowcaseScene
    Inherits Scene

    ' Game Objects
    Private _paddle1 As New Paddle()
    Private _paddle2 As New Paddle()
    Private _ball As New Ball()

    ' Scores
    Private _player1Score As Integer = 0
    Private _player2Score As Integer = 0
    Private Const WIN_SCORE As Integer = 5

    ' Game state
    Private _gameState As Integer = 0 ' 0=countdown, 1=playing, 2=paused, 3=gameover
    Private _countdownTimer As Single = 3.0F
    Private _gameTimer As Single = 0.0F
    Private _rallyCount As Integer = 0
    Private _maxRally As Integer = 0
    Private _servingPlayer As Integer = 1

    ' Random
    Private _rand As New Random()

    ' Particle emitter entity
    Private _particleEntity As Integer = -1

    ' Achievement IDs
    Private _achFirstHit As Integer = 0
    Private _achFirstScore As Integer = 0
    Private _achRally5 As Integer = 0
    Private _achRally10 As Integer = 0
    Private _achWinner As Integer = 0
    Private _achPerfectGame As Integer = 0

    ' Leaderboard ID
    Private _leaderboardId As Integer = 0

    ' Tween IDs
    Private _paddle1TweenY As Integer = -1
    Private _paddle2TweenY As Integer = -1

    ' Effects state
    Private _screenShakeActive As Boolean = False
    Private _effectsInitialized As Boolean = False

    ' UI Elements
    Private _pausePanel As Integer = -1
    Private _pauseLabel As Integer = -1
    Private _resumeButton As Integer = -1

    Protected Overrides Sub OnEnter()
        Console.WriteLine("PongShowcaseScene OnEnter - FRAMEWORK SHOWCASE")

        ' Initialize screen effects for overlay effects (flash, fade, vignette)
        ' Using simple overlay mode - no render texture needed
        _effectsInitialized = True
        Framework_Effects_SetVignetteEnabled(True)
        Framework_Effects_SetVignetteIntensity(0.3F)
        Framework_Effects_SetVignetteRadius(0.9F)
        Framework_Effects_SetVignetteSoftness(0.5F)

        ' Setup Paddles
        _paddle1.setSide("left")
        _paddle2.setSide("right")
        _paddle1.setColor(100, 200, 255, 255)  ' Blue
        _paddle2.setColor(255, 100, 100, 255)  ' Red
        _paddle2.isAI = True
        _paddle2.getBall(_ball)

        ' Setup Achievements
        SetupAchievements()

        ' Setup Leaderboard
        _leaderboardId = Framework_Leaderboard_Create("PongHighScores", LEADERBOARD_SORT_DESC, 10)
        Framework_Leaderboard_Load(_leaderboardId, "pong_scores.dat")

        ' Setup Particle Emitter for ball collision effects
        SetupParticles()

        ' Setup Pause UI (hidden initially)
        SetupPauseUI()

        ' Start with countdown
        _gameState = 0
        _countdownTimer = 3.0F

        ' Play start sound
        Framework_PlaySoundH(sfxWall)
    End Sub

    Private Sub SetupAchievements()
        ' Create achievements
        _achFirstHit = Framework_Achievement_Create("first_hit", "First Contact", "Hit the ball for the first time")
        Framework_Achievement_SetPoints(_achFirstHit, 10)

        _achFirstScore = Framework_Achievement_Create("first_score", "On The Board", "Score your first point")
        Framework_Achievement_SetPoints(_achFirstScore, 20)

        _achRally5 = Framework_Achievement_Create("rally5", "Getting Warmed Up", "Achieve a 5-hit rally")
        Framework_Achievement_SetPoints(_achRally5, 30)
        Framework_Achievement_SetProgressTarget(_achRally5, 5)

        _achRally10 = Framework_Achievement_Create("rally10", "Rally Master", "Achieve a 10-hit rally")
        Framework_Achievement_SetPoints(_achRally10, 50)
        Framework_Achievement_SetProgressTarget(_achRally10, 10)

        _achWinner = Framework_Achievement_Create("winner", "Champion", "Win a game")
        Framework_Achievement_SetPoints(_achWinner, 100)

        _achPerfectGame = Framework_Achievement_Create("perfect", "Flawless Victory", "Win without opponent scoring")
        Framework_Achievement_SetPoints(_achPerfectGame, 200)
        Framework_Achievement_SetHidden(_achPerfectGame, True)

        ' Set notification position
        Framework_Achievement_SetNotificationPosition(WINDOW_WIDTH - 320, 80)
        Framework_Achievement_SetNotificationDuration(4.0F)

        ' Try to load saved achievements
        Framework_Achievement_Load("achievements.dat")
    End Sub

    Private Sub SetupParticles()
        _particleEntity = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(_particleEntity, "BallParticles")
        Framework_Ecs_AddTransform2D(_particleEntity, WINDOW_WIDTH / 2, WINDOW_HEIGHT / 2, 0.0F, 1.0F, 1.0F)
        Framework_Ecs_AddParticleEmitter(_particleEntity, 0)

        Framework_Ecs_SetEmitterRate(_particleEntity, 0.0F)  ' Burst only
        Framework_Ecs_SetEmitterMaxParticles(_particleEntity, 100)
        Framework_Ecs_SetEmitterLifetime(_particleEntity, 0.3F, 0.8F)
        Framework_Ecs_SetEmitterVelocity(_particleEntity, -200.0F, -200.0F, 200.0F, 200.0F)
        Framework_Ecs_SetEmitterColorStart(_particleEntity, 255, 255, 100, 255)
        Framework_Ecs_SetEmitterColorEnd(_particleEntity, 255, 150, 0, 0)
        Framework_Ecs_SetEmitterSize(_particleEntity, 8.0F, 2.0F)
        Framework_Ecs_SetEmitterGravity(_particleEntity, 0.0F, 300.0F)
    End Sub

    Private Sub SetupPauseUI()
        _pausePanel = Framework_UI_CreatePanel(0, 0, 300, 200)
        Framework_UI_SetAnchor(_pausePanel, UI_ANCHOR_CENTER)
        Framework_UI_SetBackgroundColor(_pausePanel, 30, 30, 40, 240)
        Framework_UI_SetBorderColor(_pausePanel, 100, 100, 255, 255)
        Framework_UI_SetBorderWidth(_pausePanel, 3)
        Framework_UI_SetCornerRadius(_pausePanel, 15)
        Framework_UI_SetVisible(_pausePanel, False)

        _pauseLabel = Framework_UI_CreateLabel("PAUSED", 0, 30)
        Framework_UI_SetParent(_pauseLabel, _pausePanel)
        Framework_UI_SetAnchor(_pauseLabel, UI_ANCHOR_TOP_CENTER)
        Framework_UI_SetFontSize(_pauseLabel, 36)
        Framework_UI_SetTextColor(_pauseLabel, 255, 255, 255, 255)

        _resumeButton = Framework_UI_CreateButton("Press P to Resume", 0, 100, 200, 40)
        Framework_UI_SetParent(_resumeButton, _pausePanel)
        Framework_UI_SetAnchor(_resumeButton, UI_ANCHOR_TOP_CENTER)
        Framework_UI_SetFontSize(_resumeButton, 16)
    End Sub

    Private Sub SpawnHitParticles(x As Single, y As Single, r As Byte, g As Byte, b As Byte)
        ' Move emitter to collision point
        Framework_Ecs_SetTransformPosition(_particleEntity, x, y)
        Framework_Ecs_SetEmitterColorStart(_particleEntity, r, g, b, 255)
        Framework_Ecs_SetEmitterColorEnd(_particleEntity, r, g, b, 0)

        ' Burst particles
        Framework_Ecs_EmitterBurst(_particleEntity, 15)
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("PongShowcaseScene OnExit")

        ' Save achievements and leaderboard
        Framework_Achievement_Save("achievements.dat")
        Framework_Leaderboard_Save(_leaderboardId, "pong_scores.dat")

        ' Cleanup particles
        If _particleEntity >= 0 Then
            Framework_Ecs_DestroyEntity(_particleEntity)
        End If

        Framework_UI_DestroyAll()
    End Sub

    Protected Overrides Sub OnResume()
        Console.WriteLine("PongShowcaseScene OnResume")
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' Update achievements
        Framework_Achievement_Update(dt)

        ' Update particles
        Framework_Particles_Update(dt)

        ' Update UI
        Framework_UI_Update()

        ' Update screen effects (flash, fade timers)
        Framework_Effects_Update(dt)

        ' ESC or BACKSPACE to return to title
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Pause toggle
        If Framework_IsKeyPressed(Keys.P) Then
            If _gameState = 1 Then
                _gameState = 2
                Framework_UI_SetVisible(_pausePanel, True)
            ElseIf _gameState = 2 Then
                _gameState = 1
                Framework_UI_SetVisible(_pausePanel, False)
            End If
        End If

        Select Case _gameState
            Case 0 ' Countdown
                UpdateCountdown(dt)
            Case 1 ' Playing
                UpdateGame(dt)
            Case 2 ' Paused
                ' Do nothing
            Case 3 ' Game Over
                UpdateGameOver(dt)
        End Select
    End Sub

    Private Sub UpdateCountdown(dt As Single)
        _countdownTimer -= dt
        If _countdownTimer <= 0 Then
            _gameState = 1
            StartRound()
        End If
    End Sub

    Private Sub StartRound()
        _ball.BallReSet()
        _paddle1.reset()
        _paddle2.reset()
        _rallyCount = 0

        ' Set ball velocity based on serving player
        _ball.dy = _rand.Next(-50, 50)
        If _servingPlayer = 1 Then
            _ball.dx = _rand.Next(200, 350)
        Else
            _ball.dx = -_rand.Next(200, 350)
        End If

        ' Screen fade in effect (when effects enabled)
        ' Framework_Effects_FadeIn(0.3F)
    End Sub

    Private Sub UpdateGame(dt As Single)
        _gameTimer += dt

        ' Player 1 controls (Arrow keys) - increased speed for responsiveness
        Dim playerSpeed As Single = _paddle1.paddleSpeed * 8.0F  ' Much faster than base speed
        If Framework_IsKeyDown(Keys.UP) Then
            _paddle1.dy = -playerSpeed
        ElseIf Framework_IsKeyDown(Keys.DOWN) Then
            _paddle1.dy = playerSpeed
        Else
            _paddle1.dy = 0.0F
        End If

        ' Update paddles and ball
        _paddle1.Update(dt)
        _paddle2.Update(dt)
        _ball.Update(dt)

        ' Ball collision with paddles
        CheckPaddleCollision()

        ' Ball collision with walls
        CheckWallCollision()

        ' Ball out of bounds (scoring)
        CheckScoring()
    End Sub

    Private Sub CheckPaddleCollision()
        ' Check paddle 1 collision
        If _ball.AABBCheck(_paddle1) Then
            OnPaddleHit(_paddle1, True)
        End If

        ' Check paddle 2 collision
        If _ball.AABBCheck(_paddle2) Then
            OnPaddleHit(_paddle2, False)
        End If
    End Sub

    Private Sub OnPaddleHit(paddle As Paddle, isPlayer1 As Boolean)
        ' Play hit sound
        Framework_PlaySoundH(sfxHit)

        ' Reverse ball direction with speed increase
        _ball.dx = -_ball.dx * 1.05F
        If Math.Abs(_ball.dx) > 800 Then _ball.dx = Math.Sign(_ball.dx) * 800 ' Cap speed

        ' Move ball outside paddle
        If isPlayer1 Then
            _ball.ballPos.X = paddle.paddlePos.X + paddle.paddleWidth + 1
        Else
            _ball.ballPos.X = paddle.paddlePos.X - _ball.ballWidth - 1
        End If

        ' Randomize Y velocity based on hit position
        Dim hitOffset As Single = (_ball.ballPos.Y + _ball.ballHeight / 2) - (paddle.paddlePos.Y + paddle.paddleHeight / 2)
        _ball.dy = hitOffset * 3

        ' Increment rally
        _rallyCount += 1
        If _rallyCount > _maxRally Then _maxRally = _rallyCount

        ' Screen flash effect (when effects enabled)
        Framework_Effects_Flash(255, 255, 255, 0.05F)

        ' Spawn particles at collision
        Dim particleX As Single = _ball.ballPos.X + _ball.ballWidth / 2
        Dim particleY As Single = _ball.ballPos.Y + _ball.ballHeight / 2
        If isPlayer1 Then
            SpawnHitParticles(particleX, particleY, 100, 200, 255)
        Else
            SpawnHitParticles(particleX, particleY, 255, 100, 100)
        End If

        ' Update achievements
        Framework_Achievement_Unlock(_achFirstHit)
        Framework_Achievement_SetProgress(_achRally5, _rallyCount)
        Framework_Achievement_SetProgress(_achRally10, _rallyCount)
    End Sub

    Private Sub CheckWallCollision()
        ' Top wall
        If _ball.ballPos.Y < 0 Then
            _ball.ballPos.Y = 0
            _ball.dy = -_ball.dy
            Framework_PlaySoundH(sfxWall)
            SpawnHitParticles(_ball.ballPos.X + _ball.ballWidth / 2, 5, 150, 150, 150)
        End If

        ' Bottom wall
        If _ball.ballPos.Y >= WINDOW_HEIGHT - _ball.ballHeight Then
            _ball.ballPos.Y = WINDOW_HEIGHT - _ball.ballHeight
            _ball.dy = -_ball.dy
            Framework_PlaySoundH(sfxWall)
            SpawnHitParticles(_ball.ballPos.X + _ball.ballWidth / 2, WINDOW_HEIGHT - 5, 150, 150, 150)
        End If
    End Sub

    Private Sub CheckScoring()
        ' Ball goes off left side - Player 2 scores
        If _ball.ballPos.X < -_ball.ballWidth Then
            OnScore(2)
        End If

        ' Ball goes off right side - Player 1 scores
        If _ball.ballPos.X > WINDOW_WIDTH Then
            OnScore(1)
        End If
    End Sub

    Private Sub OnScore(scoringPlayer As Integer)
        If scoringPlayer = 1 Then
            _player1Score += 1
            _servingPlayer = 1
            Framework_Effects_Flash(100, 200, 255, 0.2F)  ' Blue flash
        Else
            _player2Score += 1
            _servingPlayer = 2
            Framework_Effects_Flash(255, 100, 100, 0.2F)  ' Red flash
        End If

        ' Screen shake on score (when effects enabled)
        ' Framework_Effects_Shake(8.0F, 0.3F)

        ' Achievement for first score
        Framework_Achievement_Unlock(_achFirstScore)

        ' Reset rally
        _rallyCount = 0

        ' Check for win
        If _player1Score >= WIN_SCORE OrElse _player2Score >= WIN_SCORE Then
            OnGameOver()
        Else
            ' Continue - brief pause then restart
            _gameState = 0
            _countdownTimer = 1.5F
        End If
    End Sub

    Private Sub OnGameOver()
        _gameState = 3

        ' Determine winner
        Dim winner As String = If(_player1Score > _player2Score, "PLAYER 1", "AI")
        Dim winnerScore As Integer = Math.Max(_player1Score, _player2Score)

        ' Submit to leaderboard
        Framework_Leaderboard_SubmitScore(_leaderboardId, winner, winnerScore)

        ' Achievements
        If _player1Score > _player2Score Then
            Framework_Achievement_Unlock(_achWinner)

            ' Perfect game?
            If _player2Score = 0 Then
                Framework_Achievement_Unlock(_achPerfectGame)
            End If
        End If

        ' Save progress
        Framework_Achievement_Save("achievements.dat")
        Framework_Leaderboard_Save(_leaderboardId, "pong_scores.dat")
    End Sub

    Private Sub UpdateGameOver(dt As Single)
        ' Press SPACE to restart
        If Framework_IsKeyPressed(Keys.SPACE) Then
            ' Reset game
            _player1Score = 0
            _player2Score = 0
            _gameTimer = 0
            _maxRally = 0
            _gameState = 0
            _countdownTimer = 3.0F
            ' Framework_Effects_FadeIn(0.5F)
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        ' Clear background
        Framework_ClearBackground(15, 20, 30, 255)

        ' Draw center line
        For i As Integer = 0 To WINDOW_HEIGHT Step 30
            Framework_DrawRectangle(WINDOW_WIDTH / 2 - 2, i, 4, 15, 50, 50, 60, 255)
        Next

        ' Draw paddles
        _paddle1.Draw()
        _paddle2.Draw()

        ' Draw ball (if playing or countdown < 1)
        If _gameState = 1 OrElse (_gameState = 0 AndAlso _countdownTimer < 1.0F) Then
            _ball.Draw()
        End If

        ' Draw particles
        Framework_Particles_Draw()

        ' Draw scores
        Framework_DrawTextExH(RETRO_FONT.Handle, _player1Score.ToString(), New Vector2(WINDOW_WIDTH / 2 - 100, 30), 80, 1.0F, 100, 200, 255, 255)
        Framework_DrawTextExH(RETRO_FONT.Handle, _player2Score.ToString(), New Vector2(WINDOW_WIDTH / 2 + 60, 30), 80, 1.0F, 255, 100, 100, 255)

        ' Draw game state specific UI
        Select Case _gameState
            Case 0 ' Countdown
                Dim countText As String = Math.Ceiling(_countdownTimer).ToString()
                If _countdownTimer < 0.5F Then countText = "GO!"
                Framework_DrawTextExH(RETRO_FONT.Handle, countText, New Vector2(WINDOW_WIDTH / 2 - 40, WINDOW_HEIGHT / 2 - 40), 80, 1.0F, 255, 255, 0, 255)

            Case 1 ' Playing
                ' Rally counter
                Framework_DrawText("Rally: " & _rallyCount.ToString(), 10, 10, 16, 150, 150, 150, 255)
                Framework_DrawText("Best: " & _maxRally.ToString(), 10, 30, 16, 150, 150, 150, 255)
                Framework_DrawText("Time: " & CInt(_gameTimer).ToString() & "s", 10, 50, 16, 150, 150, 150, 255)

            Case 3 ' Game Over
                Dim winner As String = If(_player1Score > _player2Score, "PLAYER 1 WINS!", "AI WINS!")
                Framework_DrawTextExH(RETRO_FONT.Handle, winner, New Vector2(WINDOW_WIDTH / 2 - 200, WINDOW_HEIGHT / 2 - 50), 50, 1.0F, 255, 215, 0, 255)
                Framework_DrawText("Press SPACE to play again", WINDOW_WIDTH / 2 - 120, WINDOW_HEIGHT / 2 + 30, 20, 200, 200, 200, 255)

                ' Draw leaderboard
                DrawLeaderboard()
        End Select

        ' Draw UI (pause menu)
        Framework_UI_Draw()

        ' Draw achievement notifications
        Framework_Achievement_DrawNotifications()

        ' Draw overlay effects (vignette, flash, fade) - no render texture needed
        If _effectsInitialized Then
            Framework_Effects_DrawOverlays(WINDOW_WIDTH, WINDOW_HEIGHT)
        End If

        ' Draw FPS and instructions (outside effects)
        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
        Framework_DrawText("[P] Pause  [BACKSPACE] Menu", WINDOW_WIDTH - 250, WINDOW_HEIGHT - 25, 14, 100, 100, 100, 255)

        ' Achievement stats
        Dim unlocked As Integer = Framework_Achievement_GetUnlockedCount()
        Dim total As Integer = Framework_Achievement_GetCount()
        Dim points As Integer = Framework_Achievement_GetEarnedPoints()
        Framework_DrawText($"Achievements: {unlocked}/{total} ({points} pts)", WINDOW_WIDTH - 250, WINDOW_HEIGHT - 45, 14, 150, 150, 100, 255)
    End Sub

    Private Sub DrawLeaderboard()
        Framework_DrawText("HIGH SCORES", WINDOW_WIDTH / 2 - 60, WINDOW_HEIGHT / 2 + 80, 20, 255, 215, 0, 255)

        Dim entryCount As Integer = Framework_Leaderboard_GetEntryCount(_leaderboardId)
        Dim displayCount As Integer = Math.Min(entryCount, 5)

        For i As Integer = 1 To displayCount
            Dim namePtr As IntPtr = Framework_Leaderboard_GetEntryName(_leaderboardId, i)
            Dim name As String = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(namePtr)
            Dim score As Integer = Framework_Leaderboard_GetEntryScore(_leaderboardId, i)
            Framework_DrawText($"{i}. {name}: {score}", WINDOW_WIDTH / 2 - 60, WINDOW_HEIGHT / 2 + 100 + i * 20, 16, 200, 200, 200, 255)
        Next
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

' ============================================================================
' DemoUIScene - Showcases the UI System
' ============================================================================
Class DemoUIScene
    Inherits Scene

    ' UI Element IDs
    Private _panelId As Integer = -1
    Private _titleLabelId As Integer = -1
    Private _buttonId As Integer = -1
    Private _sliderId As Integer = -1
    Private _sliderLabelId As Integer = -1
    Private _checkboxId As Integer = -1
    Private _textInputId As Integer = -1
    Private _progressBarId As Integer = -1
    Private _progressLabelId As Integer = -1

    ' Demo state
    Private _progressValue As Single = 0.0F
    Private _progressDirection As Integer = 1
    Private _clickCount As Integer = 0

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoUIScene OnEnter - Showcasing UI System")

        ' Create a panel as container
        _panelId = Framework_UI_CreatePanel(0, 0, 400, 450)
        Framework_UI_SetAnchor(_panelId, UI_ANCHOR_CENTER)
        Framework_UI_SetBackgroundColor(_panelId, 40, 40, 45, 240)
        Framework_UI_SetBorderColor(_panelId, 100, 100, 100, 255)
        Framework_UI_SetBorderWidth(_panelId, 2)
        Framework_UI_SetCornerRadius(_panelId, 12)

        ' Title label
        _titleLabelId = Framework_UI_CreateLabel("UI System Demo", 0, 20)
        Framework_UI_SetParent(_titleLabelId, _panelId)
        Framework_UI_SetAnchor(_titleLabelId, UI_ANCHOR_TOP_CENTER)
        Framework_UI_SetFontSize(_titleLabelId, 28)
        Framework_UI_SetTextColor(_titleLabelId, 255, 255, 255, 255)

        ' Button
        _buttonId = Framework_UI_CreateButton("Click Me!", 0, 70, 200, 45)
        Framework_UI_SetParent(_buttonId, _panelId)
        Framework_UI_SetAnchor(_buttonId, UI_ANCHOR_TOP_CENTER)
        Framework_UI_SetFontSize(_buttonId, 20)
        Framework_UI_SetCornerRadius(_buttonId, 8)

        ' Slider with label
        _sliderLabelId = Framework_UI_CreateLabel("Volume: 50%", 50, 140)
        Framework_UI_SetParent(_sliderLabelId, _panelId)
        Framework_UI_SetFontSize(_sliderLabelId, 16)
        Framework_UI_SetTextColor(_sliderLabelId, 200, 200, 200, 255)

        _sliderId = Framework_UI_CreateSlider(50, 170, 300, 0, 100, 50)
        Framework_UI_SetParent(_sliderId, _panelId)
        Framework_UI_SetHoverColor(_sliderId, 70, 130, 180, 255)
        Framework_UI_SetPressedColor(_sliderId, 100, 149, 237, 255)

        ' Checkbox
        _checkboxId = Framework_UI_CreateCheckbox("Enable Sound Effects", 50, 220, True)
        Framework_UI_SetParent(_checkboxId, _panelId)
        Framework_UI_SetSize(_checkboxId, 250, 24)
        Framework_UI_SetFontSize(_checkboxId, 16)
        Framework_UI_SetTextColor(_checkboxId, 200, 200, 200, 255)

        ' Text input
        _textInputId = Framework_UI_CreateTextInput(50, 270, 300, 36, "Enter your name...")
        Framework_UI_SetParent(_textInputId, _panelId)
        Framework_UI_SetFontSize(_textInputId, 16)
        Framework_UI_SetCornerRadius(_textInputId, 6)

        ' Progress bar with label
        _progressLabelId = Framework_UI_CreateLabel("Loading: 0%", 50, 330)
        Framework_UI_SetParent(_progressLabelId, _panelId)
        Framework_UI_SetFontSize(_progressLabelId, 16)
        Framework_UI_SetTextColor(_progressLabelId, 200, 200, 200, 255)

        _progressBarId = Framework_UI_CreateProgressBar(50, 360, 300, 24, 0)
        Framework_UI_SetParent(_progressBarId, _panelId)
        Framework_UI_SetHoverColor(_progressBarId, 76, 175, 80, 255)  ' Green fill
        Framework_UI_SetCornerRadius(_progressBarId, 4)
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoUIScene OnExit")
        Framework_UI_DestroyAll()
    End Sub

    Protected Overrides Sub OnResume()
        Console.WriteLine("DemoUIScene OnResume")
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ESC or BACKSPACE to return to title
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Update UI system
        Framework_UI_Update()

        ' Check for button click (manual polling since we can't use callbacks easily from VB)
        If Framework_UI_GetState(_buttonId) = UI_STATE_PRESSED Then
            ' Button was clicked
        End If

        ' Check if button was just clicked (state changed from pressed)
        Static lastButtonState As Integer = UI_STATE_NORMAL
        Dim currentState As Integer = Framework_UI_GetState(_buttonId)
        If lastButtonState = UI_STATE_PRESSED AndAlso currentState = UI_STATE_HOVERED Then
            _clickCount += 1
            Framework_UI_SetText(_buttonId, "Clicked " & _clickCount.ToString() & " times!")
        End If
        lastButtonState = currentState

        ' Update slider label based on slider value
        Dim sliderVal As Single = Framework_UI_GetValue(_sliderId)
        Framework_UI_SetText(_sliderLabelId, "Volume: " & CInt(sliderVal).ToString() & "%")

        ' Animate progress bar
        _progressValue += dt * 20 * _progressDirection
        If _progressValue >= 100 Then
            _progressValue = 100
            _progressDirection = -1
        ElseIf _progressValue <= 0 Then
            _progressValue = 0
            _progressDirection = 1
        End If
        Framework_UI_SetValue(_progressBarId, _progressValue)
        Framework_UI_SetText(_progressLabelId, "Loading: " & CInt(_progressValue).ToString() & "%")
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(25, 25, 35, 255)

        ' Draw all UI elements
        Framework_UI_Draw()

        ' Draw instructions at bottom
        Framework_DrawText("Click the button, drag the slider, toggle checkbox, type in text field", 10, WINDOW_HEIGHT - 50, 16, 150, 150, 150, 255)
        Framework_DrawText("[BACKSPACE] Return to menu", 10, WINDOW_HEIGHT - 25, 16, 255, 150, 150, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ============================================================================
' PHYSICS DEMO SCENE - Demonstrates 2D Physics System
' ============================================================================
Class DemoPhysicsScene
    Inherits Scene

    ' Physics bodies
    Private _groundBody As Integer = -1
    Private _leftWallBody As Integer = -1
    Private _rightWallBody As Integer = -1
    Private _platformBody As Integer = -1
    Private _ballBodies As New List(Of Integer)
    Private _boxBodies As New List(Of Integer)

    ' Spawn timer
    Private _spawnTimer As Single = 0.0F
    Private _spawnInterval As Single = 0.5F

    ' Debug toggle
    Private _showDebug As Boolean = True

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoPhysicsScene OnEnter - Showcasing Physics System")

        ' Set gravity
        Framework_Physics_SetGravity(0, 500)

        ' Create ground (static)
        _groundBody = Framework_Physics_CreateBody(BODY_STATIC, WINDOW_WIDTH / 2, WINDOW_HEIGHT - 20)
        Framework_Physics_SetBodyBox(_groundBody, WINDOW_WIDTH, 40)
        Framework_Physics_SetBodyFriction(_groundBody, 0.5F)

        ' Create walls (static)
        _leftWallBody = Framework_Physics_CreateBody(BODY_STATIC, 20, WINDOW_HEIGHT / 2)
        Framework_Physics_SetBodyBox(_leftWallBody, 40, WINDOW_HEIGHT)
        Framework_Physics_SetBodyFriction(_leftWallBody, 0.3F)

        _rightWallBody = Framework_Physics_CreateBody(BODY_STATIC, WINDOW_WIDTH - 20, WINDOW_HEIGHT / 2)
        Framework_Physics_SetBodyBox(_rightWallBody, 40, WINDOW_HEIGHT)
        Framework_Physics_SetBodyFriction(_rightWallBody, 0.3F)

        ' Create angled platform (static)
        _platformBody = Framework_Physics_CreateBody(BODY_STATIC, WINDOW_WIDTH / 2, WINDOW_HEIGHT / 2)
        Framework_Physics_SetBodyBox(_platformBody, 300, 20)
        Framework_Physics_SetBodyRestitution(_platformBody, 0.3F)

        ' Enable debug drawing
        Framework_Physics_SetDebugDraw(_showDebug)

        ' Spawn some initial objects
        SpawnBall(300, 100)
        SpawnBall(400, 80)
        SpawnBall(500, 120)
        SpawnBox(350, 50)
        SpawnBox(450, 30)
    End Sub

    Private Sub SpawnBall(x As Single, y As Single)
        Dim body As Integer = Framework_Physics_CreateBody(BODY_DYNAMIC, x, y)
        Framework_Physics_SetBodyCircle(body, 15 + CSng(_ballBodies.Count Mod 3) * 5)
        Framework_Physics_SetBodyMass(body, 1.0F)
        Framework_Physics_SetBodyRestitution(body, 0.7F)  ' Bouncy
        Framework_Physics_SetBodyFriction(body, 0.2F)
        _ballBodies.Add(body)
    End Sub

    Private Sub SpawnBox(x As Single, y As Single)
        Dim body As Integer = Framework_Physics_CreateBody(BODY_DYNAMIC, x, y)
        Dim size As Single = 25 + CSng(_boxBodies.Count Mod 3) * 10
        Framework_Physics_SetBodyBox(body, size, size)
        Framework_Physics_SetBodyMass(body, 2.0F)
        Framework_Physics_SetBodyRestitution(body, 0.3F)
        Framework_Physics_SetBodyFriction(body, 0.5F)
        _boxBodies.Add(body)
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoPhysicsScene OnExit")

        ' Cleanup all physics bodies
        Framework_Physics_DestroyAllBodies()
        _ballBodies.Clear()
        _boxBodies.Clear()
    End Sub

    Protected Overrides Sub OnResume()
        Console.WriteLine("DemoPhysicsScene OnResume")
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ESC or BACKSPACE to return to title
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Toggle debug draw with D key
        If Framework_IsKeyPressed(Keys.D) Then
            _showDebug = Not _showDebug
            Framework_Physics_SetDebugDraw(_showDebug)
        End If

        ' Spawn ball with left click
        If Framework_IsMouseButtonPressed(0) Then
            Dim mx As Integer = Framework_GetMouseX()
            Dim my As Integer = Framework_GetMouseY()
            SpawnBall(mx, my)
        End If

        ' Spawn box with right click
        If Framework_IsMouseButtonPressed(1) Then
            Dim mx As Integer = Framework_GetMouseX()
            Dim my As Integer = Framework_GetMouseY()
            SpawnBox(mx, my)
        End If

        ' Auto spawn timer
        _spawnTimer += dt
        If _spawnTimer >= _spawnInterval Then
            _spawnTimer = 0
            ' Random spawn at top
            Dim rand As New Random()
            Dim spawnX As Single = 100 + CSng(rand.NextDouble() * (WINDOW_WIDTH - 200))
            If rand.Next(2) = 0 Then
                SpawnBall(spawnX, 50)
            Else
                SpawnBox(spawnX, 50)
            End If
        End If

        ' Limit max bodies (cleanup oldest)
        While _ballBodies.Count + _boxBodies.Count > 50
            If _ballBodies.Count > 0 Then
                Framework_Physics_DestroyBody(_ballBodies(0))
                _ballBodies.RemoveAt(0)
            ElseIf _boxBodies.Count > 0 Then
                Framework_Physics_DestroyBody(_boxBodies(0))
                _boxBodies.RemoveAt(0)
            End If
        End While

        ' Apply force with SPACE (push everything up)
        If Framework_IsKeyDown(Keys.SPACE) Then
            For Each body In _ballBodies
                Framework_Physics_ApplyForce(body, 0, -500)
            Next
            For Each body In _boxBodies
                Framework_Physics_ApplyForce(body, 0, -800)
            Next
        End If

        ' Step physics simulation
        Framework_Physics_Step(dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(15, 15, 25, 255)

        ' Draw static objects (ground, walls, platform)
        ' Ground
        Framework_DrawRectangle(0, WINDOW_HEIGHT - 40, WINDOW_WIDTH, 40, 60, 60, 80, 255)
        ' Walls
        Framework_DrawRectangle(0, 0, 40, WINDOW_HEIGHT, 60, 60, 80, 255)
        Framework_DrawRectangle(WINDOW_WIDTH - 40, 0, 40, WINDOW_HEIGHT, 60, 60, 80, 255)
        ' Platform
        Framework_DrawRectangle(CInt(WINDOW_WIDTH / 2 - 150), CInt(WINDOW_HEIGHT / 2 - 10), 300, 20, 80, 80, 100, 255)

        ' Draw dynamic bodies
        For Each body In _ballBodies
            Dim x As Single = 0, y As Single = 0
            Framework_Physics_GetBodyPosition(body, x, y)
            Dim radius As Single = 15 + CSng(_ballBodies.IndexOf(body) Mod 3) * 5
            ' Color based on velocity
            Dim vx As Single = 0, vy As Single = 0
            Framework_Physics_GetBodyVelocity(body, vx, vy)
            Dim speed As Single = CSng(Math.Sqrt(vx * vx + vy * vy))
            Dim r As Byte = CByte(Math.Min(255, 100 + speed * 0.5))
            Dim g As Byte = CByte(Math.Max(0, 200 - speed * 0.3))
            Framework_DrawCircle(CInt(x), CInt(y), radius, r, g, 150, 255)
        Next

        For Each body In _boxBodies
            Dim x As Single = 0, y As Single = 0
            Framework_Physics_GetBodyPosition(body, x, y)
            Dim size As Single = 25 + CSng(_boxBodies.IndexOf(body) Mod 3) * 10
            ' Color based on velocity
            Dim vx As Single = 0, vy As Single = 0
            Framework_Physics_GetBodyVelocity(body, vx, vy)
            Dim speed As Single = CSng(Math.Sqrt(vx * vx + vy * vy))
            Dim r As Byte = CByte(Math.Min(255, 150 + speed * 0.3))
            Dim b As Byte = CByte(Math.Max(0, 200 - speed * 0.3))
            Framework_DrawRectangle(CInt(x - size / 2), CInt(y - size / 2), CInt(size), CInt(size), r, 100, b, 255)
        Next

        ' Draw physics debug overlay
        If _showDebug Then
            Framework_Physics_DrawDebug()
        End If

        ' Instructions
        Framework_DrawText("[Left Click] Spawn Ball  [Right Click] Spawn Box", 10, 10, 16, 200, 200, 200, 255)
        Framework_DrawText("[SPACE] Apply Upward Force  [D] Toggle Debug", 10, 30, 16, 200, 200, 200, 255)
        Framework_DrawText("[BACKSPACE] Return to menu", 10, 50, 16, 255, 150, 150, 255)
        Framework_DrawText("Bodies: " & (_ballBodies.Count + _boxBodies.Count).ToString(), 10, WINDOW_HEIGHT - 30, 16, 150, 255, 150, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class