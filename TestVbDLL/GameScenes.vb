Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy
Imports RaylibWrapper.UtiliyClasses


Class TitleScene
    Inherits Scene
    Dim paddle1 As New Paddle
    Dim paddle2 As New Paddle
    Dim player1 As New Player(1, "PLAYER 1", 0, paddle1)
    Dim player2 As New Player(2, "PLAYER 2", 0, paddle2)
    Dim menuIndex As Integer = 0
    Dim temp As String = "Arrow Keys to Navigate, SPACE to Select"

    ' Menu items in grid order (left column, right column per row)
    Dim menuItems() As String = {
        "1 PLAYER", "2 PLAYER",
        "OPTIONS", "2D DEMO",
        "UI DEMO", "PHYSICS",
        "SHOWCASE", "TWEEN",
        "CAMERA", "AI PATH",
        "AUDIO", "EFFECTS",
        "DIALOGUE", "INVENTORY",
        "QUEST", "LIGHTING",
        "TIMER/EVT", "FSM",
        "SAVE/LOAD", "BATCHING",
        "ATLAS", "LEVEL ED",
        "NETWORK", ""
    }

    ' Menu item colors (R, G, B)
    Dim menuColors(,) As Byte = {
        {255, 255, 255}, {255, 255, 255},
        {255, 255, 255}, {100, 255, 100},
        {100, 200, 255}, {255, 150, 100},
        {255, 215, 0}, {255, 100, 255},
        {100, 255, 200}, {255, 200, 100},
        {100, 200, 255}, {255, 100, 200},
        {255, 200, 150}, {200, 150, 255},
        {150, 255, 200}, {255, 255, 150},
        {150, 200, 255}, {255, 150, 200},
        {200, 255, 150}, {100, 200, 255},
        {255, 180, 100}, {150, 255, 150},
        {200, 150, 255}, {100, 100, 100}
    }

    ' Grid layout constants
    Const MENU_COLS As Integer = 2
    Const MENU_ROWS As Integer = 12
    Const MENU_START_Y As Integer = 100
    Const MENU_ITEM_WIDTH As Integer = 220
    Const MENU_ITEM_HEIGHT As Integer = 38
    Const MENU_GAP_X As Integer = 30
    Const MENU_GAP_Y As Integer = 8
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
        ' Grid navigation (2 columns)
        If Framework_IsKeyPressed(Keys.UP) AndAlso menuIndex >= MENU_COLS Then
            Framework_PlaySoundH(sfxWall)
            menuIndex -= MENU_COLS
        ElseIf Framework_IsKeyPressed(Keys.DOWN) AndAlso menuIndex < menuItems.Length - MENU_COLS Then
            Framework_PlaySoundH(sfxWall)
            menuIndex += MENU_COLS
        ElseIf Framework_IsKeyPressed(Keys.LEFT) AndAlso (menuIndex Mod MENU_COLS) > 0 Then
            Framework_PlaySoundH(sfxWall)
            menuIndex -= 1
        ElseIf Framework_IsKeyPressed(Keys.RIGHT) AndAlso (menuIndex Mod MENU_COLS) < MENU_COLS - 1 Then
            Framework_PlaySoundH(sfxWall)
            menuIndex += 1
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
                Case 7
                    'Tweening Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoTweenScene)
                Case 8
                    'Camera Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoCameraScene)
                Case 9
                    'AI/Pathfinding Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoAIScene)
                Case 10
                    'Audio Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoAudioScene)
                Case 11
                    'Screen Effects Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoEffectsScene)
                Case 12
                    'Dialogue System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoDialogueScene)
                Case 13
                    'Inventory System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoInventoryScene)
                Case 14
                    'Quest System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoQuestScene)
                Case 15
                    'Lighting System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoLightingScene)
                Case 16
                    'Timer/Event System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoTimerEventScene)
                Case 17
                    'FSM State Machine Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoFSMScene)
                Case 18
                    'Save/Load System Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoSaveLoadScene)
                Case 19
                    'Sprite Batching Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoBatchingScene)
                Case 20
                    'Texture Atlas Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoAtlasScene)
                Case 21
                    'Level Editor Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoLevelScene)
                Case 22
                    'Networking Demo
                    Framework_PlaySoundH(sfxHit)
                    ChangeTo(New DemoNetworkScene)
            End Select
        End If

    End Sub
    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(46, 67, 111, 255)

        ' Draw title
        Framework_DrawTextExH(RETRO_FONT.Handle, "PONG", New Vector2(WINDOW_WIDTH / 2 - 120, 30), 80, 1.0F, 255, 255, 255, 255)

        ' Calculate grid start position (centered)
        Dim gridWidth As Integer = MENU_COLS * MENU_ITEM_WIDTH + (MENU_COLS - 1) * MENU_GAP_X
        Dim startX As Integer = (WINDOW_WIDTH - gridWidth) / 2

        ' Draw menu items in grid
        For i As Integer = 0 To menuItems.Length - 1
            ' Skip empty menu items
            If String.IsNullOrEmpty(menuItems(i)) Then Continue For

            Dim col As Integer = i Mod MENU_COLS
            Dim row As Integer = i \ MENU_COLS

            Dim itemX As Integer = startX + col * (MENU_ITEM_WIDTH + MENU_GAP_X)
            Dim itemY As Integer = MENU_START_Y + row * (MENU_ITEM_HEIGHT + MENU_GAP_Y)

            ' Draw selection highlight
            If i = menuIndex Then
                Framework_DrawRectangle(itemX - 5, itemY - 5, MENU_ITEM_WIDTH + 10, MENU_ITEM_HEIGHT + 10, 255, 50, 50, 150)
                Framework_DrawRectangle(itemX - 3, itemY - 3, MENU_ITEM_WIDTH + 6, MENU_ITEM_HEIGHT + 6, 255, 100, 100, 200)
            End If

            ' Draw item background
            Framework_DrawRectangle(itemX, itemY, MENU_ITEM_WIDTH, MENU_ITEM_HEIGHT, 30, 40, 60, 200)

            ' Draw item text (centered in box)
            Dim textX As Integer = itemX + 15
            Dim textY As Integer = itemY + 8
            Framework_DrawTextExH(RETRO_FONT.Handle, menuItems(i), New Vector2(textX, textY), 22, 1.0F,
                menuColors(i, 0), menuColors(i, 1), menuColors(i, 2), 255)
        Next

        ' Draw instructions at bottom
        Dim instructY As Integer = MENU_START_Y + MENU_ROWS * (MENU_ITEM_HEIGHT + MENU_GAP_Y) + 30
        Framework_DrawText(temp, (WINDOW_WIDTH - temp.Length * 10) / 2, instructY, 18, 200, 200, 200, 255)

        ' Draw paddles as decoration
        player1.gPaddle.Draw()
        player2.gPaddle.Draw()
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
        ' ENTER or BACKSPACE -> go to Title
        If Framework_IsKeyPressed(Keys.ENTER) OrElse Framework_IsKeyPressed(Keys.BACKSPACE) Then
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
            menuPos.y -= 50

        ElseIf Framework_IsKeyPressed(Keys.DOWN) AndAlso menuIndex < 2 Then
            'play sound
            Framework_PlaySoundH(sfxWall)
            menuIndex += 1
            menuPos.y += 50

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
        ElseIf Framework_IsKeyPressed(Keys.BACKSPACE) Then
            'return to title
            ChangeTo(New TitleScene)
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
        'return to title on space or backspace
        If Framework_IsKeyPressed(Keys.SPACE) OrElse Framework_IsKeyPressed(Keys.BACKSPACE) Then
            ChangeTo(New TitleScene)
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        'draw the win screen
        Framework_ClearBackground(10, 10, 20, 255)
        Framework_DrawText(winningPlayer.Name & " WINS!", WINDOW_WIDTH / 2 - 275, 250, 50, 255, 255, 255, 255)
        Framework_DrawText("PRESS SPACE OR BACKSPACE TO RETURN!", WINDOW_WIDTH / 2 - 275, 300, 25, 255, 255, 255, 255)
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
        Framework_Effects_ResetAll()
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
            _ball.ballPos.x = paddle.paddlePos.x + paddle.paddleWidth + 1
        Else
            _ball.ballPos.x = paddle.paddlePos.x - _ball.ballWidth - 1
        End If

        ' Randomize Y velocity based on hit position
        Dim hitOffset As Single = (_ball.ballPos.y + _ball.ballHeight / 2) - (paddle.paddlePos.y + paddle.paddleHeight / 2)
        _ball.dy = hitOffset * 3

        ' Increment rally
        _rallyCount += 1
        If _rallyCount > _maxRally Then _maxRally = _rallyCount

        ' Screen flash effect (when effects enabled)
        Framework_Effects_Flash(255, 255, 255, 0.05F)

        ' Spawn particles at collision
        Dim particleX As Single = _ball.ballPos.x + _ball.ballWidth / 2
        Dim particleY As Single = _ball.ballPos.y + _ball.ballHeight / 2
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
        If _ball.ballPos.y < 0 Then
            _ball.ballPos.y = 0
            _ball.dy = -_ball.dy
            Framework_PlaySoundH(sfxWall)
            SpawnHitParticles(_ball.ballPos.x + _ball.ballWidth / 2, 5, 150, 150, 150)
        End If

        ' Bottom wall
        If _ball.ballPos.y >= WINDOW_HEIGHT - _ball.ballHeight Then
            _ball.ballPos.y = WINDOW_HEIGHT - _ball.ballHeight
            _ball.dy = -_ball.dy
            Framework_PlaySoundH(sfxWall)
            SpawnHitParticles(_ball.ballPos.x + _ball.ballWidth / 2, WINDOW_HEIGHT - 5, 150, 150, 150)
        End If
    End Sub

    Private Sub CheckScoring()
        ' Ball goes off left side - Player 2 scores
        If _ball.ballPos.x < -_ball.ballWidth Then
            OnScore(2)
        End If

        ' Ball goes off right side - Player 1 scores
        If _ball.ballPos.x > WINDOW_WIDTH Then
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

' =============================================================================
' DEMO TWEEN SCENE - Showcases the Tweening System
' =============================================================================
Public Class DemoTweenScene
    Inherits Scene

    ' Tweened objects
    Private _boxX As Single = 100
    Private _boxY As Single = 300
    Private _boxScale As Single = 1.0F
    Private _boxRotation As Single = 0
    Private _boxAlpha As Single = 255

    ' Circle for color tweening
    Private _circleR As Single = 255
    Private _circleG As Single = 100
    Private _circleB As Single = 100

    ' Active tweens
    Private _positionTweenId As Integer = -1
    Private _scaleTweenId As Integer = -1
    Private _rotationTweenId As Integer = -1
    Private _colorTweenId As Integer = -1

    ' Current easing type for display
    Private _currentEasing As Integer = 0
    Private ReadOnly _easingNames() As String = {
        "Linear", "QuadIn", "QuadOut", "QuadInOut",
        "CubicIn", "CubicOut", "CubicInOut",
        "BackIn", "BackOut", "BackInOut",
        "ElasticIn", "ElasticOut", "ElasticInOut",
        "BounceIn", "BounceOut", "BounceInOut"
    }

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoTweenScene OnEnter - Showcasing Tweening System")
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoTweenScene OnExit")
        Framework_Tween_KillAll()
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Change easing type
        If Framework_IsKeyPressed(Keys.LEFT) Then
            _currentEasing = (_currentEasing - 1 + _easingNames.Length) Mod _easingNames.Length
        End If
        If Framework_IsKeyPressed(Keys.RIGHT) Then
            _currentEasing = (_currentEasing + 1) Mod _easingNames.Length
        End If

        ' Start position tween (1 key)
        If Framework_IsKeyPressed(Keys.ONE) Then
            If _positionTweenId >= 0 Then Framework_Tween_Kill(_positionTweenId)
            _boxX = 100
            _positionTweenId = Framework_Tween_FloatTo(_boxX, 900, 2.0F, _currentEasing)
        End If

        ' Start scale tween (2 key)
        If Framework_IsKeyPressed(Keys.TWO) Then
            If _scaleTweenId >= 0 Then Framework_Tween_Kill(_scaleTweenId)
            _boxScale = 0.5F
            _scaleTweenId = Framework_Tween_FloatTo(_boxScale, 2.0F, 1.5F, _currentEasing)
        End If

        ' Start rotation tween (3 key)
        If Framework_IsKeyPressed(Keys.THREE) Then
            If _rotationTweenId >= 0 Then Framework_Tween_Kill(_rotationTweenId)
            _boxRotation = 0
            _rotationTweenId = Framework_Tween_FloatTo(_boxRotation, 360, 2.0F, _currentEasing)
        End If

        ' Start color tween (4 key)
        If Framework_IsKeyPressed(Keys.FOUR) Then
            _circleR = 255 : _circleG = 100 : _circleB = 100
            ' Tween to blue
            Framework_Tween_FloatTo(_circleR, 100, 1.5F, _currentEasing)
            Framework_Tween_FloatTo(_circleG, 100, 1.5F, _currentEasing)
            Framework_Tween_FloatTo(_circleB, 255, 1.5F, _currentEasing)
        End If

        ' Update all tweens
        Framework_Tween_Update(dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(25, 25, 40, 255)

        ' Draw title
        Framework_DrawText("TWEENING SYSTEM DEMO", 10, 10, 24, 255, 255, 255, 255)
        Framework_DrawText("Current Easing: " & _easingNames(_currentEasing), 10, 40, 18, 255, 200, 100, 255)

        ' Draw instructions
        Framework_DrawText("[LEFT/RIGHT] Change Easing Type", 10, WINDOW_HEIGHT - 110, 16, 200, 200, 200, 255)
        Framework_DrawText("[1] Position Tween  [2] Scale Tween", 10, WINDOW_HEIGHT - 88, 16, 150, 255, 150, 255)
        Framework_DrawText("[3] Rotation Tween  [4] Color Tween", 10, WINDOW_HEIGHT - 66, 16, 150, 255, 150, 255)
        Framework_DrawText("[BACKSPACE] Return to menu", 10, WINDOW_HEIGHT - 44, 16, 255, 150, 150, 255)

        ' Draw position-tweened box
        Dim boxSize As Integer = CInt(50 * _boxScale)
        Dim boxCenterX As Integer = CInt(_boxX)
        Dim boxCenterY As Integer = 200
        Framework_DrawRectangle(boxCenterX - boxSize \ 2, boxCenterY - boxSize \ 2, boxSize, boxSize, 100, 200, 255, 255)
        Framework_DrawText("Position", CInt(_boxX) - 25, 260, 14, 200, 200, 200, 255)

        ' Draw scale-tweened box
        Dim scaleBoxSize As Integer = CInt(50 * _boxScale)
        Framework_DrawRectangle(500 - scaleBoxSize \ 2, 350 - scaleBoxSize \ 2, scaleBoxSize, scaleBoxSize, 255, 200, 100, 255)
        Framework_DrawText("Scale: " & _boxScale.ToString("F2"), 460, 400, 14, 200, 200, 200, 255)

        ' Draw rotation indicator (simplified - just show angle value)
        Framework_DrawCircle(800, 350, 40, 100, 255, 150, 255)
        Framework_DrawText("Rotation: " & CInt(_boxRotation).ToString() & "°", 750, 400, 14, 200, 200, 200, 255)

        ' Draw color-tweened circle
        Framework_DrawCircle(300, 500, 50, CByte(_circleR), CByte(_circleG), CByte(_circleB), 255)
        Framework_DrawText("Color Tween", 255, 560, 14, 200, 200, 200, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' =============================================================================
' DEMO CAMERA SCENE - Showcases the Camera System
' =============================================================================
Public Class DemoCameraScene
    Inherits Scene

    Private _playerX As Single = 600
    Private _playerY As Single = 360
    Private _playerSpeed As Single = 200

    Private _targetZoom As Single = 1.0F
    Private _shakeIntensity As Single = 0

    ' World objects to show camera movement
    Private ReadOnly _worldObjects As New List(Of (x As Integer, y As Integer, w As Integer, h As Integer, r As Byte, g As Byte, b As Byte))

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoCameraScene OnEnter - Showcasing Camera System")

        ' Initialize camera position
        Framework_Camera_SetPosition(_playerX, _playerY)

        ' Create some world objects
        Dim rnd As New Random(42)
        For i As Integer = 0 To 29
            _worldObjects.Add((
                rnd.Next(-500, 1700),
                rnd.Next(-300, 1000),
                rnd.Next(30, 100),
                rnd.Next(30, 100),
                CByte(rnd.Next(50, 200)),
                CByte(rnd.Next(50, 200)),
                CByte(rnd.Next(50, 200))
            ))
        Next
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoCameraScene OnExit")
        Framework_Camera_Reset()
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Player movement
        Dim moveX As Single = 0
        Dim moveY As Single = 0
        If Framework_IsKeyDown(Keys.W) OrElse Framework_IsKeyDown(Keys.UP) Then moveY = -1
        If Framework_IsKeyDown(Keys.S) OrElse Framework_IsKeyDown(Keys.DOWN) Then moveY = 1
        If Framework_IsKeyDown(Keys.A) OrElse Framework_IsKeyDown(Keys.LEFT) Then moveX = -1
        If Framework_IsKeyDown(Keys.D) OrElse Framework_IsKeyDown(Keys.RIGHT) Then moveX = 1

        _playerX += moveX * _playerSpeed * dt
        _playerY += moveY * _playerSpeed * dt

        ' Camera follow
        Framework_Camera_SetTarget(_playerX, _playerY)
        Framework_Camera_SetFollowLerp(0.05F)

        ' Zoom controls
        If Framework_IsKeyDown(Keys.Q) Then _targetZoom = Math.Min(2.0F, _targetZoom + dt)
        If Framework_IsKeyDown(Keys.E) Then _targetZoom = Math.Max(0.5F, _targetZoom - dt)
        Framework_Camera_SetZoom(_targetZoom)

        ' Screen shake
        If Framework_IsKeyPressed(Keys.SPACE) Then
            Framework_Camera_Shake(10, 0.5F)
        End If

        ' Update camera
        Framework_Camera_Update(dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(30, 30, 45, 255)

        ' Begin camera transform
        Framework_Camera_BeginMode()

        ' Draw world grid
        For x As Integer = -10 To 30
            For y As Integer = -5 To 15
                Dim gridX As Integer = x * 100
                Dim gridY As Integer = y * 100
                Framework_DrawRectangle(gridX, gridY, 98, 98, 40, 40, 55, 255)
            Next
        Next

        ' Draw world objects
        For Each obj In _worldObjects
            Framework_DrawRectangle(obj.x, obj.y, obj.w, obj.h, obj.r, obj.g, obj.b, 255)
        Next

        ' Draw player
        Framework_DrawCircle(CInt(_playerX), CInt(_playerY), 20, 255, 200, 100, 255)
        Framework_DrawCircle(CInt(_playerX), CInt(_playerY), 15, 255, 150, 50, 255)

        ' End camera transform
        Framework_Camera_EndMode()

        ' Draw UI (screen space)
        Framework_DrawText("CAMERA SYSTEM DEMO", 10, 10, 24, 255, 255, 255, 255)
        Framework_DrawText("[WASD/Arrows] Move Player", 10, WINDOW_HEIGHT - 110, 16, 200, 200, 200, 255)
        Framework_DrawText("[Q/E] Zoom In/Out  [SPACE] Screen Shake", 10, WINDOW_HEIGHT - 88, 16, 150, 255, 150, 255)
        Framework_DrawText("[BACKSPACE] Return to menu", 10, WINDOW_HEIGHT - 66, 16, 255, 150, 150, 255)
        Framework_DrawText("Zoom: " & _targetZoom.ToString("F2") & "x", 10, 45, 16, 150, 255, 150, 255)
        Framework_DrawText("Player: (" & CInt(_playerX) & ", " & CInt(_playerY) & ")", 10, 65, 16, 150, 200, 255, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' =============================================================================
' DEMO AI SCENE - Showcases the AI/Pathfinding System
' =============================================================================
Public Class DemoAIScene
    Inherits Scene

    Private Const GRID_SIZE As Integer = 20
    Private Const CELL_SIZE As Integer = 30

    Private _gridHandle As Integer = -1
    Private _pathHandle As Integer = -1

    ' Agent starts at cell center (cell 2,2 = 2*30+15 = 75)
    Private _agentX As Single = 75
    Private _agentY As Single = 75
    ' Target at cell (17,17) center
    Private _targetX As Single = 525
    Private _targetY As Single = 525

    ' Path following
    Private _currentPath As New List(Of (x As Single, y As Single))
    Private _pathIndex As Integer = 0
    Private _agentSpeed As Single = 150

    ' Obstacles
    Private ReadOnly _obstacles As New List(Of (x As Integer, y As Integer))

    ' Debug: last click position
    Private _lastClickX As Integer = -1
    Private _lastClickY As Integer = -1
    Private _lastClickType As String = ""

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoAIScene OnEnter - Showcasing AI/Pathfinding System")

        ' Create navigation grid
        _gridHandle = Framework_NavGrid_Create(GRID_SIZE, GRID_SIZE, CELL_SIZE)

        ' Add some obstacles
        Dim rnd As New Random(123)
        For i As Integer = 0 To 39
            Dim ox As Integer = rnd.Next(2, GRID_SIZE - 2)
            Dim oy As Integer = rnd.Next(2, GRID_SIZE - 2)
            ' Don't block start or end areas
            If (ox < 4 AndAlso oy < 4) OrElse (ox > GRID_SIZE - 5 AndAlso oy > GRID_SIZE - 5) Then Continue For
            _obstacles.Add((ox, oy))
            Framework_NavGrid_SetWalkable(_gridHandle, ox, oy, False)
        Next

        ' Find initial path
        FindPath()
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoAIScene OnExit")
        If _pathHandle >= 0 Then Framework_Path_Destroy(_pathHandle)
        If _gridHandle >= 0 Then Framework_NavGrid_Destroy(_gridHandle)
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Private Sub FindPath()
        _currentPath.Clear()
        _pathIndex = 0

        ' Destroy previous path if exists
        If _pathHandle >= 0 Then Framework_Path_Destroy(_pathHandle)

        ' Convert positions to grid coordinates (use integer division, not rounding)
        Dim startCellX As Integer = CInt(Math.Floor(_agentX / CELL_SIZE))
        Dim startCellY As Integer = CInt(Math.Floor(_agentY / CELL_SIZE))
        Dim endCellX As Integer = CInt(Math.Floor(_targetX / CELL_SIZE))
        Dim endCellY As Integer = CInt(Math.Floor(_targetY / CELL_SIZE))

        ' Find path using A*
        Console.WriteLine($"FindPath: from cell ({startCellX},{startCellY}) to ({endCellX},{endCellY})")
        _pathHandle = Framework_Path_FindCell(_gridHandle, startCellX, startCellY, endCellX, endCellY)
        Console.WriteLine($"Path handle: {_pathHandle}")

        If _pathHandle >= 0 Then
            Dim pathLength As Integer = Framework_Path_GetLength(_pathHandle)
            Console.WriteLine($"Path length: {pathLength}")
            For i As Integer = 0 To pathLength - 1
                Dim px As Single = 0, py As Single = 0
                Framework_Path_GetWaypoint(_pathHandle, i, px, py)
                _currentPath.Add((px, py))
                Console.WriteLine($"  Waypoint {i}: ({px},{py})")
            Next
        Else
            Console.WriteLine("Path not found!")
        End If
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Get mouse position
        Dim mx As Integer = Framework_GetMouseX()
        Dim my As Integer = Framework_GetMouseY()
        Dim cellX As Integer = mx \ CELL_SIZE
        Dim cellY As Integer = my \ CELL_SIZE

        ' Set new target with LEFT mouse click (same as Physics demo)
        If Framework_IsMouseButtonPressed(0) Then
            _lastClickX = mx
            _lastClickY = my
            _lastClickType = "LEFT"

            ' Check if clicked cell is walkable and within grid
            If cellX >= 0 AndAlso cellX < GRID_SIZE AndAlso cellY >= 0 AndAlso cellY < GRID_SIZE Then
                If Framework_NavGrid_IsWalkable(_gridHandle, cellX, cellY) Then
                    ' Snap target to cell center
                    _targetX = cellX * CELL_SIZE + CELL_SIZE / 2
                    _targetY = cellY * CELL_SIZE + CELL_SIZE / 2
                    FindPath()
                End If
            End If
        End If

        ' Toggle obstacle with RIGHT mouse click
        If Framework_IsMouseButtonPressed(1) Then
            _lastClickX = mx
            _lastClickY = my
            _lastClickType = "RIGHT"

            If cellX >= 0 AndAlso cellX < GRID_SIZE AndAlso cellY >= 0 AndAlso cellY < GRID_SIZE Then
                Dim isWalkable As Boolean = Framework_NavGrid_IsWalkable(_gridHandle, cellX, cellY)
                Framework_NavGrid_SetWalkable(_gridHandle, cellX, cellY, Not isWalkable)

                If isWalkable Then
                    _obstacles.Add((cellX, cellY))
                Else
                    _obstacles.Remove((cellX, cellY))
                End If
                FindPath()
            End If
        End If

        ' Follow path
        If _currentPath.Count > 0 AndAlso _pathIndex < _currentPath.Count Then
            Dim targetPoint = _currentPath(_pathIndex)
            Dim dx As Single = targetPoint.x - _agentX
            Dim dy As Single = targetPoint.y - _agentY
            Dim dist As Single = CSng(Math.Sqrt(dx * dx + dy * dy))

            If dist < 5 Then
                _pathIndex += 1
            Else
                dx /= dist
                dy /= dist
                _agentX += dx * _agentSpeed * dt
                _agentY += dy * _agentSpeed * dt
            End If
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(20, 25, 35, 255)

        ' Draw grid
        For x As Integer = 0 To GRID_SIZE - 1
            For y As Integer = 0 To GRID_SIZE - 1
                Dim px As Integer = x * CELL_SIZE
                Dim py As Integer = y * CELL_SIZE
                Dim isWalkable As Boolean = Framework_NavGrid_IsWalkable(_gridHandle, x, y)

                If isWalkable Then
                    Framework_DrawRectangle(px + 1, py + 1, CELL_SIZE - 2, CELL_SIZE - 2, 45, 50, 60, 255)
                Else
                    Framework_DrawRectangle(px + 1, py + 1, CELL_SIZE - 2, CELL_SIZE - 2, 80, 40, 40, 255)
                End If
            Next
        Next

        ' Draw path waypoints and lines
        If _currentPath.Count > 0 Then
            ' Draw path lines
            For i As Integer = 0 To _currentPath.Count - 2
                Dim p1 = _currentPath(i)
                Dim p2 = _currentPath(i + 1)
                Framework_DrawLine(CInt(p1.x), CInt(p1.y), CInt(p2.x), CInt(p2.y), 100, 255, 100, 255)
            Next
            ' Draw waypoints as small circles
            For i As Integer = 0 To _currentPath.Count - 1
                Dim p = _currentPath(i)
                Dim c As Byte = If(i = _pathIndex, CByte(255), CByte(150))
                Framework_DrawCircle(CInt(p.x), CInt(p.y), 5, c, 255, c, 255)
            Next
        End If

        ' Draw target
        Framework_DrawCircle(CInt(_targetX), CInt(_targetY), 12, 100, 255, 100, 255)
        Framework_DrawCircle(CInt(_targetX), CInt(_targetY), 6, 50, 200, 50, 255)

        ' Draw agent
        Framework_DrawCircle(CInt(_agentX), CInt(_agentY), 15, 255, 200, 100, 255)
        Framework_DrawCircle(CInt(_agentX), CInt(_agentY), 10, 255, 150, 50, 255)

        ' Draw UI
        Dim uiX As Integer = GRID_SIZE * CELL_SIZE + 20
        Framework_DrawText("AI/PATHFINDING DEMO", uiX, 10, 20, 255, 255, 255, 255)
        Framework_DrawText("[Left Click] Set Target", uiX, 50, 14, 150, 255, 150, 255)
        Framework_DrawText("[Right Click] Toggle Wall", uiX, 70, 14, 150, 255, 150, 255)
        Framework_DrawText("[BACKSPACE] Return", uiX, 90, 14, 255, 150, 150, 255)

        ' Debug info
        Framework_DrawText("Path Waypoints: " & _currentPath.Count.ToString(), uiX, 130, 14, 200, 200, 200, 255)
        Framework_DrawText("Current Index: " & _pathIndex.ToString(), uiX, 150, 14, 200, 200, 200, 255)
        Framework_DrawText("Path Handle: " & _pathHandle.ToString(), uiX, 170, 14, 200, 200, 200, 255)
        Framework_DrawText("Agent: (" & CInt(_agentX) & "," & CInt(_agentY) & ")", uiX, 200, 14, 255, 200, 100, 255)
        Framework_DrawText("Target: (" & CInt(_targetX) & "," & CInt(_targetY) & ")", uiX, 220, 14, 100, 255, 100, 255)

        ' Mouse debug
        Dim mouseX As Integer = Framework_GetMouseX()
        Dim mouseY As Integer = Framework_GetMouseY()
        Dim hoverCell As String = $"({mouseX \ CELL_SIZE},{mouseY \ CELL_SIZE})"
        Framework_DrawText("Mouse: (" & mouseX & "," & mouseY & ") " & hoverCell, uiX, 250, 14, 255, 255, 100, 255)
        Framework_DrawText("Last Click: " & _lastClickType & " (" & _lastClickX & "," & _lastClickY & ")", uiX, 270, 14, 255, 150, 255, 255)

        ' Debug: show mouse button states
        Dim lb As Boolean = Framework_IsMouseButtonPressed(0)
        Dim rb As Boolean = Framework_IsMouseButtonPressed(1)
        Dim lbDown As Boolean = Framework_IsMouseButtonDown(0)
        Dim rbDown As Boolean = Framework_IsMouseButtonDown(1)
        Framework_DrawText("LB Pressed: " & lb.ToString() & " Down: " & lbDown.ToString(), uiX, 290, 14, If(lb, CByte(255), CByte(150)), 150, 150, 255)
        Framework_DrawText("RB Pressed: " & rb.ToString() & " Down: " & rbDown.ToString(), uiX, 310, 14, 150, 150, If(rb, CByte(255), CByte(150)), 255)

        ' Draw mouse cursor highlight on grid
        Dim cursorCellX As Integer = mouseX \ CELL_SIZE
        Dim cursorCellY As Integer = mouseY \ CELL_SIZE
        If cursorCellX >= 0 AndAlso cursorCellX < GRID_SIZE AndAlso cursorCellY >= 0 AndAlso cursorCellY < GRID_SIZE Then
            Framework_DrawRectangle(cursorCellX * CELL_SIZE, cursorCellY * CELL_SIZE, CELL_SIZE, CELL_SIZE, 255, 255, 255, 50)
        End If

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' =============================================================================
' DEMO AUDIO SCENE - Showcases the Audio System
' =============================================================================
Public Class DemoAudioScene
    Inherits Scene

    Private _sfxHit As Integer = -1
    Private _sfxWall As Integer = -1

    Private _masterVolume As Single = 1.0F
    Private _sfxVolume As Single = 1.0F
    Private _pitch As Single = 1.0F

    ' Sound source position for spatial audio
    Private _soundSourceX As Single = 600
    Private _soundSourceY As Single = 360

    ' Listener position
    Private _listenerX As Single = 600
    Private _listenerY As Single = 360

    Private _spatialEnabled As Boolean = False

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoAudioScene OnEnter - Showcasing Audio System")

        ' Load sounds
        _sfxHit = Framework_Audio_LoadSound("sounds/hit.wav", 1) ' SFX group
        _sfxWall = Framework_Audio_LoadSound("sounds/wall.wav", 1)

        ' Set initial volumes
        Framework_Audio_SetGroupVolume(0, _masterVolume) ' Master
        Framework_Audio_SetGroupVolume(1, _sfxVolume) ' SFX
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoAudioScene OnExit")
        If _sfxHit >= 0 Then Framework_Audio_UnloadSound(_sfxHit)
        If _sfxWall >= 0 Then Framework_Audio_UnloadSound(_sfxWall)
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            ChangeTo(New TitleScene)
            Return
        End If

        ' Play sounds
        If Framework_IsKeyPressed(Keys.ONE) Then
            If _spatialEnabled Then
                Framework_Audio_PlaySoundAt(_sfxHit, _soundSourceX, _soundSourceY)
            Else
                Framework_Audio_PlaySound(_sfxHit)
            End If
        End If

        If Framework_IsKeyPressed(Keys.TWO) Then
            If _spatialEnabled Then
                Framework_Audio_PlaySoundAt(_sfxWall, _soundSourceX, _soundSourceY)
            Else
                Framework_Audio_PlaySound(_sfxWall)
            End If
        End If

        ' Volume controls
        If Framework_IsKeyDown(Keys.UP) Then
            _masterVolume = Math.Min(1.0F, _masterVolume + dt)
            Framework_Audio_SetGroupVolume(0, _masterVolume)
        End If
        If Framework_IsKeyDown(Keys.DOWN) Then
            _masterVolume = Math.Max(0.0F, _masterVolume - dt)
            Framework_Audio_SetGroupVolume(0, _masterVolume)
        End If

        ' Pitch controls
        If Framework_IsKeyDown(Keys.LEFT) Then
            _pitch = Math.Max(0.5F, _pitch - dt)
        End If
        If Framework_IsKeyDown(Keys.RIGHT) Then
            _pitch = Math.Min(2.0F, _pitch + dt)
        End If

        ' Toggle spatial audio
        If Framework_IsKeyPressed(Keys.SPACE) Then
            _spatialEnabled = Not _spatialEnabled
            Framework_Audio_SetSpatialEnabled(_spatialEnabled)
        End If

        ' Move sound source with mouse
        If _spatialEnabled Then
            _soundSourceX = Framework_GetMouseX()
            _soundSourceY = Framework_GetMouseY()
        End If

        ' Move listener with WASD
        If Framework_IsKeyDown(Keys.W) Then _listenerY -= 200 * dt
        If Framework_IsKeyDown(Keys.S) Then _listenerY += 200 * dt
        If Framework_IsKeyDown(Keys.A) Then _listenerX -= 200 * dt
        If Framework_IsKeyDown(Keys.D) Then _listenerX += 200 * dt
        Framework_Audio_SetListenerPosition(_listenerX, _listenerY)

        Framework_Audio_Update(dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(25, 30, 40, 255)

        ' Draw spatial visualization if enabled
        If _spatialEnabled Then
            ' Draw listener
            Framework_DrawCircle(CInt(_listenerX), CInt(_listenerY), 20, 100, 200, 255, 255)
            Framework_DrawText("LISTENER", CInt(_listenerX) - 30, CInt(_listenerY) + 25, 12, 100, 200, 255, 255)

            ' Draw sound source
            Framework_DrawCircle(CInt(_soundSourceX), CInt(_soundSourceY), 15, 255, 150, 100, 255)
            Framework_DrawText("SOURCE", CInt(_soundSourceX) - 25, CInt(_soundSourceY) + 20, 12, 255, 150, 100, 255)

            ' Draw distance line
            Framework_DrawLine(CInt(_listenerX), CInt(_listenerY), CInt(_soundSourceX), CInt(_soundSourceY), 100, 100, 100, 100)
        End If

        ' Draw UI
        Framework_DrawText("AUDIO SYSTEM DEMO", 10, 10, 24, 255, 255, 255, 255)

        Framework_DrawText("Master Volume: " & (_masterVolume * 100).ToString("F0") & "%", 10, 50, 16, 200, 200, 200, 255)
        Framework_DrawRectangle(10, 70, CInt(200 * _masterVolume), 20, 100, 200, 100, 255)
        Framework_DrawRectangle(10, 70, 200, 20, 80, 80, 80, 100)

        Framework_DrawText("Pitch: " & _pitch.ToString("F2") & "x", 10, 100, 16, 200, 200, 200, 255)
        Framework_DrawRectangle(10, 120, CInt(200 * (_pitch - 0.5F) / 1.5F), 20, 200, 150, 100, 255)
        Framework_DrawRectangle(10, 120, 200, 20, 80, 80, 80, 100)

        Framework_DrawText("Spatial Audio: " & If(_spatialEnabled, "ON", "OFF"), 10, 150, 16, If(_spatialEnabled, CByte(100), CByte(200)), If(_spatialEnabled, CByte(255), CByte(100)), If(_spatialEnabled, CByte(100), CByte(100)), 255)

        ' Instructions
        Framework_DrawText("[1] Play Hit Sound  [2] Play Wall Sound", 10, WINDOW_HEIGHT - 110, 16, 150, 255, 150, 255)
        Framework_DrawText("[UP/DOWN] Volume  [LEFT/RIGHT] Pitch", 10, WINDOW_HEIGHT - 88, 16, 150, 255, 150, 255)
        Framework_DrawText("[SPACE] Toggle Spatial Audio", 10, WINDOW_HEIGHT - 66, 16, 150, 200, 255, 255)
        Framework_DrawText("[WASD] Move Listener (when spatial)", 10, WINDOW_HEIGHT - 44, 16, 200, 200, 200, 255)
        Framework_DrawText("[BACKSPACE] Return to menu", 10, WINDOW_HEIGHT - 22, 16, 255, 150, 150, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' =============================================================================
' DEMO EFFECTS SCENE - Showcases the Screen Effects System
' =============================================================================
Public Class DemoEffectsScene
    Inherits Scene

    ' Effect states
    Private _vignetteEnabled As Boolean = False
    Private _vignetteIntensity As Single = 0.5F

    Private _grayscaleEnabled As Boolean = False
    Private _grayscaleAmount As Single = 1.0F

    Private _sepiaEnabled As Boolean = False
    Private _sepiaAmount As Single = 1.0F

    Private _scanlineEnabled As Boolean = False
    Private _pixelateEnabled As Boolean = False
    Private _pixelSize As Integer = 4

    ' Demo objects to see effects on
    Private ReadOnly _particles As New List(Of (x As Single, y As Single, vx As Single, vy As Single, r As Byte, g As Byte, b As Byte))

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoEffectsScene OnEnter - Showcasing Screen Effects")

        ' Reset camera for this demo (center on screen center, no offset)
        Framework_Camera_SetTarget(WINDOW_WIDTH / 2, WINDOW_HEIGHT / 2)
        Framework_Camera_SetOffset(WINDOW_WIDTH / 2, WINDOW_HEIGHT / 2)

        ' Create some colorful particles to see effects on
        Dim rnd As New Random()
        For i As Integer = 0 To 49
            _particles.Add((
                rnd.Next(100, WINDOW_WIDTH - 100),
                rnd.Next(100, WINDOW_HEIGHT - 100),
                CSng(rnd.NextDouble() * 100 - 50),
                CSng(rnd.NextDouble() * 100 - 50),
                CByte(rnd.Next(100, 255)),
                CByte(rnd.Next(100, 255)),
                CByte(rnd.Next(100, 255))
            ))
        Next
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoEffectsScene OnExit")
        ' Reset all effects and camera
        Framework_Effects_ResetAll()
        Framework_Camera_Reset()
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_Effects_ResetAll()
            Framework_Camera_Reset()
            ChangeTo(New TitleScene)
            Return
        End If

        ' Toggle effects
        If Framework_IsKeyPressed(Keys.ONE) Then
            _vignetteEnabled = Not _vignetteEnabled
            Framework_Effects_SetVignetteEnabled(_vignetteEnabled)
            If _vignetteEnabled Then
                Framework_Effects_SetVignetteIntensity(_vignetteIntensity)
                Framework_Effects_SetVignetteRadius(0.3F)
                Framework_Effects_SetVignetteSoftness(0.5F)
            End If
        End If

        If Framework_IsKeyPressed(Keys.TWO) Then
            _grayscaleEnabled = Not _grayscaleEnabled
            Framework_Effects_SetGrayscaleEnabled(_grayscaleEnabled)
            Framework_Effects_SetGrayscaleAmount(_grayscaleAmount)
        End If

        If Framework_IsKeyPressed(Keys.THREE) Then
            _sepiaEnabled = Not _sepiaEnabled
            Framework_Effects_SetSepiaEnabled(_sepiaEnabled)
            Framework_Effects_SetSepiaAmount(_sepiaAmount)
        End If

        If Framework_IsKeyPressed(Keys.FOUR) Then
            _scanlineEnabled = Not _scanlineEnabled
            Framework_Effects_SetScanlinesEnabled(_scanlineEnabled)
            Framework_Effects_SetScanlinesIntensity(0.3F)
            Framework_Effects_SetScanlinesCount(200)
        End If

        If Framework_IsKeyPressed(Keys.FIVE) Then
            _pixelateEnabled = Not _pixelateEnabled
            Framework_Effects_SetPixelateEnabled(_pixelateEnabled)
            Framework_Effects_SetPixelateSize(_pixelSize)
        End If

        ' Adjust vignette intensity
        If _vignetteEnabled Then
            If Framework_IsKeyDown(Keys.UP) Then
                _vignetteIntensity = Math.Min(1.0F, _vignetteIntensity + dt)
                Framework_Effects_SetVignetteIntensity(_vignetteIntensity)
            End If
            If Framework_IsKeyDown(Keys.DOWN) Then
                _vignetteIntensity = Math.Max(0.0F, _vignetteIntensity - dt)
                Framework_Effects_SetVignetteIntensity(_vignetteIntensity)
            End If
        End If

        ' Screen flash on SPACE
        If Framework_IsKeyPressed(Keys.SPACE) Then
            Framework_Effects_FlashWhite(0.2F)
        End If

        ' Screen shake on ENTER (uses camera system shake)
        If Framework_IsKeyPressed(Keys.ENTER) Then
            Framework_Camera_Shake(10.0F, 0.4F)
        End If

        ' Update particles
        For i As Integer = 0 To _particles.Count - 1
            Dim p = _particles(i)
            p.x += p.vx * dt
            p.y += p.vy * dt

            ' Bounce off walls
            If p.x < 50 OrElse p.x > WINDOW_WIDTH - 50 Then p.vx = -p.vx
            If p.y < 80 OrElse p.y > WINDOW_HEIGHT - 130 Then p.vy = -p.vy

            _particles(i) = p
        Next

        Framework_Camera_Update(dt)
        Framework_Effects_Update(dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(40, 50, 70, 255)

        ' Begin 2D camera mode for shake effect
        Framework_Camera_BeginMode()

        ' Draw colorful background pattern
        For x As Integer = 0 To 11
            For y As Integer = 0 To 7
                Dim hue As Integer = (x * 30 + y * 45) Mod 360
                Dim r As Byte = CByte(128 + 64 * Math.Sin(hue * Math.PI / 180))
                Dim g As Byte = CByte(128 + 64 * Math.Sin((hue + 120) * Math.PI / 180))
                Dim b As Byte = CByte(128 + 64 * Math.Sin((hue + 240) * Math.PI / 180))
                Framework_DrawRectangle(x * 100, y * 90, 98, 88, r, g, b, 100)
            Next
        Next

        ' Draw particles
        For Each p In _particles
            Framework_DrawCircle(CInt(p.x), CInt(p.y), 15, p.r, p.g, p.b, 255)
        Next

        ' Apply grayscale effect (overlay simulation - desaturates by drawing gray over)
        If _grayscaleEnabled Then
            Framework_DrawRectangle(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, 128, 128, 128, 180)
        End If

        ' Apply sepia effect (overlay simulation - warm brownish tint)
        If _sepiaEnabled Then
            Framework_DrawRectangle(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, 112, 66, 20, 100)
        End If

        ' Apply pixelate effect (visual indicator only - true pixelate needs shaders)
        If _pixelateEnabled Then
            ' Draw grid lines to simulate pixelation
            For px As Integer = 0 To WINDOW_WIDTH Step _pixelSize * 4
                Framework_DrawRectangle(px, 0, 1, WINDOW_HEIGHT, 0, 0, 0, 50)
            Next
            For py As Integer = 0 To WINDOW_HEIGHT Step _pixelSize * 4
                Framework_DrawRectangle(0, py, WINDOW_WIDTH, 1, 0, 0, 0, 50)
            Next
        End If

        ' End 2D camera mode
        Framework_Camera_EndMode()

        ' Draw overlay effects (vignette, flash, scanlines)
        Framework_Effects_DrawOverlays(WINDOW_WIDTH, WINDOW_HEIGHT)

        ' Draw UI on top (after effects so it's not affected)
        Framework_DrawText("SCREEN EFFECTS DEMO", 10, 10, 24, 255, 255, 255, 255)

        ' Effect status
        Dim statusY As Integer = 50
        Framework_DrawText("[1] Vignette: " & If(_vignetteEnabled, "ON (" & (_vignetteIntensity * 100).ToString("F0") & "%)", "OFF"), 10, statusY, 14,
            If(_vignetteEnabled, CByte(100), CByte(150)), If(_vignetteEnabled, CByte(255), CByte(150)), If(_vignetteEnabled, CByte(100), CByte(150)), 255)
        Framework_DrawText("[2] Grayscale: " & If(_grayscaleEnabled, "ON", "OFF"), 10, statusY + 18, 14,
            If(_grayscaleEnabled, CByte(100), CByte(150)), If(_grayscaleEnabled, CByte(255), CByte(150)), If(_grayscaleEnabled, CByte(100), CByte(150)), 255)
        Framework_DrawText("[3] Sepia: " & If(_sepiaEnabled, "ON", "OFF"), 10, statusY + 36, 14,
            If(_sepiaEnabled, CByte(100), CByte(150)), If(_sepiaEnabled, CByte(255), CByte(150)), If(_sepiaEnabled, CByte(100), CByte(150)), 255)
        Framework_DrawText("[4] Scanlines: " & If(_scanlineEnabled, "ON", "OFF"), 10, statusY + 54, 14,
            If(_scanlineEnabled, CByte(100), CByte(150)), If(_scanlineEnabled, CByte(255), CByte(150)), If(_scanlineEnabled, CByte(100), CByte(150)), 255)
        Framework_DrawText("[5] Pixelate: " & If(_pixelateEnabled, "ON", "OFF"), 10, statusY + 72, 14,
            If(_pixelateEnabled, CByte(100), CByte(150)), If(_pixelateEnabled, CByte(255), CByte(150)), If(_pixelateEnabled, CByte(100), CByte(150)), 255)

        ' Instructions
        Framework_DrawText("[SPACE] Screen Flash  [ENTER] Screen Shake", 10, WINDOW_HEIGHT - 66, 16, 150, 200, 255, 255)
        Framework_DrawText("[UP/DOWN] Adjust Vignette (when enabled)", 10, WINDOW_HEIGHT - 44, 16, 200, 200, 200, 255)
        Framework_DrawText("[BACKSPACE] Return to menu", 10, WINDOW_HEIGHT - 22, 16, 255, 150, 150, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ==========================================
' DIALOGUE SYSTEM DEMO
' ==========================================
Class DemoDialogueScene
    Inherits Scene

    Private _dialogueId As Integer = -1
    Private _isDialogueSetup As Boolean = False

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoDialogueScene OnEnter - Showcasing Dialogue System")

        ' Create a dialogue tree
        _dialogueId = Framework_Dialogue_Create("DemoDialogue")

        If _dialogueId >= 0 Then
            ' Add nodes to the dialogue
            Dim node1 As Integer = Framework_Dialogue_AddNode(_dialogueId, "greeting")
            Framework_Dialogue_SetNodeSpeaker(_dialogueId, node1, "NPC")
            Framework_Dialogue_SetNodeText(_dialogueId, node1, "Hello traveler! Welcome to the dialogue demo. How can I help you today?")

            Dim node2 As Integer = Framework_Dialogue_AddNode(_dialogueId, "quest_info")
            Framework_Dialogue_SetNodeSpeaker(_dialogueId, node2, "NPC")
            Framework_Dialogue_SetNodeText(_dialogueId, node2, "Ah, looking for adventure? There's a cave to the north with treasure!")

            Dim node3 As Integer = Framework_Dialogue_AddNode(_dialogueId, "shop_info")
            Framework_Dialogue_SetNodeSpeaker(_dialogueId, node3, "NPC")
            Framework_Dialogue_SetNodeText(_dialogueId, node3, "My shop has the finest weapons and armor. Feel free to browse!")

            Dim node4 As Integer = Framework_Dialogue_AddNode(_dialogueId, "goodbye")
            Framework_Dialogue_SetNodeSpeaker(_dialogueId, node4, "NPC")
            Framework_Dialogue_SetNodeText(_dialogueId, node4, "Safe travels, adventurer! May the winds guide your path.")

            Dim node5 As Integer = Framework_Dialogue_AddNode(_dialogueId, "player_thanks")
            Framework_Dialogue_SetNodeSpeaker(_dialogueId, node5, "Player")
            Framework_Dialogue_SetNodeText(_dialogueId, node5, "Thanks for the information!")

            ' Add choices to first node
            Framework_Dialogue_AddChoice(_dialogueId, node1, "Tell me about quests", node2)
            Framework_Dialogue_AddChoice(_dialogueId, node1, "What do you sell?", node3)
            Framework_Dialogue_AddChoice(_dialogueId, node1, "Goodbye", node4)

            ' Add follow-up choices
            Framework_Dialogue_AddChoice(_dialogueId, node2, "Thanks!", node5)
            Framework_Dialogue_AddChoice(_dialogueId, node2, "Tell me more", node1)
            Framework_Dialogue_AddChoice(_dialogueId, node3, "Thanks!", node5)
            Framework_Dialogue_AddChoice(_dialogueId, node3, "Ask something else", node1)
            Framework_Dialogue_SetNextNode(_dialogueId, node5, node4)

            ' Set start node and typewriter settings
            Framework_Dialogue_SetStartNode(_dialogueId, node1)
            Framework_Dialogue_SetTypewriterEnabled(True)
            Framework_Dialogue_SetTypewriterSpeed(40.0F)
            Framework_Dialogue_SetHistoryEnabled(True)

            _isDialogueSetup = True
        End If
    End Sub

    Protected Overrides Sub OnExit()
        If _dialogueId >= 0 Then
            Framework_Dialogue_Stop()
            Framework_Dialogue_Destroy(_dialogueId)
        End If
        Console.WriteLine("DemoDialogueScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_PlaySoundH(sfxHit)
            ChangeTo(New TitleScene)
            Return
        End If

        Framework_Dialogue_Update(dt)

        ' Start dialogue with SPACE if not active
        If Not Framework_Dialogue_IsActive() Then
            If Framework_IsKeyPressed(Keys.SPACE) Then
                Framework_Dialogue_Start(_dialogueId)
            End If
        Else
            ' Handle dialogue interaction
            If Framework_IsKeyPressed(Keys.SPACE) Then
                ' If typewriter not complete, skip it
                If Not Framework_Dialogue_IsTypewriterComplete() Then
                    Framework_Dialogue_SkipTypewriter()
                Else
                    ' If there are choices, don't auto-continue
                    Dim choiceCount As Integer = Framework_Dialogue_GetCurrentChoiceCount()
                    If choiceCount = 0 Then
                        Framework_Dialogue_Continue()
                    End If
                End If
            End If

            ' Handle choice selection with number keys
            Dim choiceCount2 As Integer = Framework_Dialogue_GetCurrentChoiceCount()
            If choiceCount2 > 0 AndAlso Framework_Dialogue_IsTypewriterComplete() Then
                If Framework_IsKeyPressed(Keys.ONE) AndAlso choiceCount2 >= 1 Then
                    Framework_Dialogue_SelectChoice(0)
                ElseIf Framework_IsKeyPressed(Keys.TWO) AndAlso choiceCount2 >= 2 Then
                    Framework_Dialogue_SelectChoice(1)
                ElseIf Framework_IsKeyPressed(Keys.THREE) AndAlso choiceCount2 >= 3 Then
                    Framework_Dialogue_SelectChoice(2)
                ElseIf Framework_IsKeyPressed(Keys.FOUR) AndAlso choiceCount2 >= 4 Then
                    Framework_Dialogue_SelectChoice(3)
                End If
            End If
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(30, 30, 50, 255)

        ' Draw title
        Framework_DrawText("DIALOGUE SYSTEM DEMO", 20, 20, 30, 255, 200, 100, 255)
        Framework_DrawText("Press SPACE to start/continue dialogue | 1-4 to select choices | ESC to exit", 20, 60, 16, 200, 200, 200, 255)

        ' Draw NPC sprite area
        Framework_DrawRectangle(50, 150, 200, 250, 60, 60, 80, 255)
        Framework_DrawText("NPC", 130, 270, 24, 255, 255, 255, 255)
        Framework_DrawCircle(150, 220, 50, 200, 180, 150, 255)

        ' Draw dialogue box
        Dim boxX As Integer = 280
        Dim boxY As Integer = 150
        Dim boxW As Integer = WINDOW_WIDTH - 310
        Dim boxH As Integer = 300

        Framework_DrawRectangle(boxX, boxY, boxW, boxH, 20, 20, 40, 230)
        Framework_DrawRectangleLines(boxX, boxY, boxW, boxH, 100, 100, 150, 255)

        If Framework_Dialogue_IsActive() Then
            ' Get current speaker
            Dim speakerPtr As IntPtr = Framework_Dialogue_GetCurrentSpeaker()
            Dim speaker As String = If(speakerPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(speakerPtr), "???")

            ' Draw speaker name
            Framework_DrawText(speaker & ":", boxX + 15, boxY + 15, 22, 255, 220, 100, 255)

            ' Get visible text (typewriter effect)
            Dim textPtr As IntPtr = Framework_Dialogue_GetVisibleText()
            Dim visibleText As String = If(textPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(textPtr), "")

            ' Draw dialogue text with word wrap
            DrawWrappedText(visibleText, boxX + 15, boxY + 50, boxW - 30, 20, 255, 255, 255, 255)

            ' Draw choices if available and typewriter complete
            Dim choiceCount As Integer = Framework_Dialogue_GetCurrentChoiceCount()
            If choiceCount > 0 AndAlso Framework_Dialogue_IsTypewriterComplete() Then
                Dim choiceY As Integer = boxY + 150
                Framework_DrawText("Choices:", boxX + 15, choiceY, 18, 150, 200, 255, 255)
                choiceY += 30

                For i As Integer = 0 To choiceCount - 1
                    Dim choicePtr As IntPtr = Framework_Dialogue_GetCurrentChoiceText(i)
                    Dim choiceText As String = If(choicePtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(choicePtr), "")
                    Framework_DrawText((i + 1).ToString() & ". " & choiceText, boxX + 25, choiceY, 18, 200, 255, 200, 255)
                    choiceY += 28
                Next
            ElseIf choiceCount = 0 AndAlso Framework_Dialogue_IsTypewriterComplete() Then
                Framework_DrawText("[SPACE to continue]", boxX + 15, boxY + boxH - 40, 16, 150, 150, 150, 255)
            End If
        Else
            Framework_DrawText("Press SPACE to talk to the NPC", boxX + 50, boxY + 120, 20, 150, 150, 200, 255)
        End If

        ' Draw dialogue history panel
        Framework_DrawRectangle(50, 420, WINDOW_WIDTH - 100, 140, 15, 15, 30, 200)
        Framework_DrawText("Dialogue History:", 60, 425, 16, 150, 150, 200, 255)

        Dim historyCount As Integer = Framework_Dialogue_GetHistoryCount()
        Dim histY As Integer = 445
        Dim startIdx As Integer = Math.Max(0, historyCount - 4)
        For i As Integer = startIdx To historyCount - 1
            Dim hSpeakerPtr As IntPtr = Framework_Dialogue_GetHistorySpeaker(i)
            Dim hTextPtr As IntPtr = Framework_Dialogue_GetHistoryText(i)
            Dim hSpeaker As String = If(hSpeakerPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(hSpeakerPtr), "")
            Dim hText As String = If(hTextPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(hTextPtr), "")
            If hText.Length > 60 Then hText = hText.Substring(0, 60) & "..."
            Framework_DrawText(hSpeaker & ": " & hText, 70, histY, 14, 180, 180, 180, 255)
            histY += 22
        Next

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub

    Private Sub DrawWrappedText(text As String, x As Integer, y As Integer, maxWidth As Integer, fontSize As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
        Dim words() As String = text.Split(" "c)
        Dim line As String = ""
        Dim lineY As Integer = y
        Dim charsPerLine As Integer = maxWidth \ (fontSize \ 2)

        For Each word In words
            If line.Length + word.Length + 1 > charsPerLine Then
                Framework_DrawText(line, x, lineY, fontSize, r, g, b, a)
                line = word
                lineY += fontSize + 4
            Else
                If line.Length > 0 Then line &= " "
                line &= word
            End If
        Next
        If line.Length > 0 Then
            Framework_DrawText(line, x, lineY, fontSize, r, g, b, a)
        End If
    End Sub
End Class

' ==========================================
' INVENTORY SYSTEM DEMO
' ==========================================
Class DemoInventoryScene
    Inherits Scene

    Private _inventoryId As Integer = -1
    Private _selectedSlot As Integer = 0
    Private _itemIds As New List(Of Integer)

    ' Item definitions
    Private _swordId As Integer = -1
    Private _shieldId As Integer = -1
    Private _potionId As Integer = -1
    Private _goldId As Integer = -1
    Private _gemId As Integer = -1

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoInventoryScene OnEnter - Showcasing Inventory System")

        ' Define items
        _swordId = Framework_Item_Define("Iron Sword", "A sturdy iron sword for combat")
        Framework_Item_SetRarity(_swordId, 1) ' Uncommon
        Framework_Item_SetEquipSlot(_swordId, 5) ' Weapon
        Framework_Item_SetStatFloat(_swordId, "attack", 15.0F)
        Framework_Item_SetValue(_swordId, 100)
        Framework_Item_SetWeight(_swordId, 5.0F)
        _itemIds.Add(_swordId)

        _shieldId = Framework_Item_Define("Wooden Shield", "Basic protection from attacks")
        Framework_Item_SetRarity(_shieldId, 0) ' Common
        Framework_Item_SetEquipSlot(_shieldId, 6) ' Offhand
        Framework_Item_SetStatFloat(_shieldId, "defense", 10.0F)
        Framework_Item_SetValue(_shieldId, 50)
        Framework_Item_SetWeight(_shieldId, 3.0F)
        _itemIds.Add(_shieldId)

        _potionId = Framework_Item_Define("Health Potion", "Restores 50 health points")
        Framework_Item_SetRarity(_potionId, 0) ' Common
        Framework_Item_SetStackable(_potionId, True, 99)
        Framework_Item_SetUsable(_potionId, True)
        Framework_Item_SetConsumable(_potionId, True)
        Framework_Item_SetValue(_potionId, 25)
        Framework_Item_SetWeight(_potionId, 0.5F)
        _itemIds.Add(_potionId)

        _goldId = Framework_Item_Define("Gold Coin", "Shiny currency")
        Framework_Item_SetRarity(_goldId, 0) ' Common
        Framework_Item_SetStackable(_goldId, True, 9999)
        Framework_Item_SetValue(_goldId, 1)
        Framework_Item_SetWeight(_goldId, 0.01F)
        _itemIds.Add(_goldId)

        _gemId = Framework_Item_Define("Ruby Gem", "A precious red gemstone")
        Framework_Item_SetRarity(_gemId, 3) ' Epic
        Framework_Item_SetStackable(_gemId, True, 10)
        Framework_Item_SetValue(_gemId, 500)
        Framework_Item_SetWeight(_gemId, 0.2F)
        _itemIds.Add(_gemId)

        ' Create inventory with 20 slots
        _inventoryId = Framework_Inventory_Create("PlayerInventory", 20)
        Framework_Inventory_SetMaxWeight(_inventoryId, 100.0F)
        Framework_Inventory_SetAutoStack(_inventoryId, True)

        ' Add starting items
        Framework_Inventory_AddItem(_inventoryId, _swordId, 1)
        Framework_Inventory_AddItem(_inventoryId, _shieldId, 1)
        Framework_Inventory_AddItem(_inventoryId, _potionId, 5)
        Framework_Inventory_AddItem(_inventoryId, _goldId, 100)
        Framework_Inventory_AddItem(_inventoryId, _gemId, 2)
    End Sub

    Protected Overrides Sub OnExit()
        If _inventoryId >= 0 Then
            Framework_Inventory_Destroy(_inventoryId)
        End If
        Framework_Item_UndefineAll()
        Console.WriteLine("DemoInventoryScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_PlaySoundH(sfxHit)
            ChangeTo(New TitleScene)
            Return
        End If

        Dim capacity As Integer = Framework_Inventory_GetCapacity(_inventoryId)

        ' Navigate inventory (5 columns)
        If Framework_IsKeyPressed(Keys.LEFT) AndAlso _selectedSlot > 0 Then
            _selectedSlot -= 1
        ElseIf Framework_IsKeyPressed(Keys.RIGHT) AndAlso _selectedSlot < capacity - 1 Then
            _selectedSlot += 1
        ElseIf Framework_IsKeyPressed(Keys.UP) AndAlso _selectedSlot >= 5 Then
            _selectedSlot -= 5
        ElseIf Framework_IsKeyPressed(Keys.DOWN) AndAlso _selectedSlot < capacity - 5 Then
            _selectedSlot += 5
        End If

        ' Add random item with A
        If Framework_IsKeyPressed(Keys.A) Then
            Dim rnd As New Random()
            Dim randomItem As Integer = _itemIds(rnd.Next(_itemIds.Count))
            Dim qty As Integer = If(Framework_Item_IsStackable(randomItem), rnd.Next(1, 10), 1)
            Framework_Inventory_AddItem(_inventoryId, randomItem, qty)
        End If

        ' Remove item with D
        If Framework_IsKeyPressed(Keys.D) Then
            Dim slotItem As Integer = Framework_Inventory_GetSlotItem(_inventoryId, _selectedSlot)
            If slotItem >= 0 Then
                Framework_Inventory_RemoveFromSlot(_inventoryId, _selectedSlot, 1)
            End If
        End If

        ' Use item with U
        If Framework_IsKeyPressed(Keys.U) Then
            Framework_Inventory_UseItem(_inventoryId, _selectedSlot)
        End If

        ' Sort inventory with S
        If Framework_IsKeyPressed(Keys.S) Then
            Framework_Inventory_Sort(_inventoryId, 1) ' Sort by rarity
        End If

        ' Clear inventory with C
        If Framework_IsKeyPressed(Keys.C) Then
            Framework_Inventory_Clear(_inventoryId)
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(25, 35, 45, 255)

        ' Draw title
        Framework_DrawText("INVENTORY SYSTEM DEMO", 20, 20, 30, 100, 200, 255, 255)
        Framework_DrawText("Arrows: Navigate | A: Add Item | D: Delete | U: Use | S: Sort | C: Clear | ESC: Exit", 20, 60, 14, 200, 200, 200, 255)

        ' Draw inventory grid
        Dim gridX As Integer = 50
        Dim gridY As Integer = 100
        Dim slotSize As Integer = 70
        Dim padding As Integer = 5
        Dim cols As Integer = 5

        Dim capacity As Integer = Framework_Inventory_GetCapacity(_inventoryId)

        For i As Integer = 0 To capacity - 1
            Dim col As Integer = i Mod cols
            Dim row As Integer = i \ cols
            Dim slotX As Integer = gridX + col * (slotSize + padding)
            Dim slotY As Integer = gridY + row * (slotSize + padding)

            ' Draw slot background
            Dim bgR As Byte = 40, bgG As Byte = 50, bgB As Byte = 60
            If i = _selectedSlot Then
                bgR = 80 : bgG = 100 : bgB = 150
            End If
            Framework_DrawRectangle(slotX, slotY, slotSize, slotSize, bgR, bgG, bgB, 255)
            Framework_DrawRectangleLines(slotX, slotY, slotSize, slotSize, 100, 100, 100, 255)

            ' Draw item if present
            Dim itemId As Integer = Framework_Inventory_GetSlotItem(_inventoryId, i)
            If itemId >= 0 Then
                Dim qty As Integer = Framework_Inventory_GetSlotQuantity(_inventoryId, i)
                Dim rarity As Integer = Framework_Item_GetRarity(itemId)
                Dim namePtr As IntPtr = Framework_Item_GetName(itemId)
                Dim name As String = If(namePtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(namePtr), "?")

                ' Rarity colors
                Dim rarityColors(,) As Byte = {
                    {200, 200, 200}, ' Common - gray
                    {100, 255, 100}, ' Uncommon - green
                    {100, 150, 255}, ' Rare - blue
                    {200, 100, 255}, ' Epic - purple
                    {255, 200, 50}   ' Legendary - gold
                }
                Dim rIdx As Integer = Math.Min(rarity, 4)

                ' Draw item icon placeholder (colored rectangle)
                Framework_DrawRectangle(slotX + 10, slotY + 10, slotSize - 20, slotSize - 35, rarityColors(rIdx, 0), rarityColors(rIdx, 1), rarityColors(rIdx, 2), 255)

                ' Draw first letter of item
                Framework_DrawText(name.Substring(0, 1), slotX + 25, slotY + 15, 24, 30, 30, 30, 255)

                ' Draw quantity
                If qty > 1 Then
                    Framework_DrawText(qty.ToString(), slotX + 5, slotY + slotSize - 18, 14, 255, 255, 255, 255)
                End If
            End If
        Next

        ' Draw selected item info panel
        Dim infoX As Integer = 450
        Dim infoY As Integer = 100
        Framework_DrawRectangle(infoX, infoY, 300, 200, 30, 40, 50, 255)
        Framework_DrawRectangleLines(infoX, infoY, 300, 200, 80, 80, 100, 255)
        Framework_DrawText("Selected Item Info", infoX + 10, infoY + 10, 18, 255, 220, 100, 255)

        Dim selectedItemId As Integer = Framework_Inventory_GetSlotItem(_inventoryId, _selectedSlot)
        If selectedItemId >= 0 Then
            Dim namePtr As IntPtr = Framework_Item_GetName(selectedItemId)
            Dim descPtr As IntPtr = Framework_Item_GetDescription(selectedItemId)
            Dim name As String = If(namePtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(namePtr), "?")
            Dim desc As String = If(descPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(descPtr), "")
            Dim rarity As Integer = Framework_Item_GetRarity(selectedItemId)
            Dim value As Integer = Framework_Item_GetValue(selectedItemId)
            Dim weight As Single = Framework_Item_GetWeight(selectedItemId)
            Dim qty As Integer = Framework_Inventory_GetSlotQuantity(_inventoryId, _selectedSlot)

            Dim rarityNames() As String = {"Common", "Uncommon", "Rare", "Epic", "Legendary"}

            Framework_DrawText(name, infoX + 15, infoY + 40, 20, 255, 255, 255, 255)
            Framework_DrawText(desc, infoX + 15, infoY + 70, 14, 180, 180, 180, 255)
            Framework_DrawText("Rarity: " & rarityNames(Math.Min(rarity, 4)), infoX + 15, infoY + 100, 14, 200, 200, 100, 255)
            Framework_DrawText("Value: " & value.ToString() & " gold", infoX + 15, infoY + 120, 14, 255, 215, 0, 255)
            Framework_DrawText("Weight: " & weight.ToString("F1") & " lbs", infoX + 15, infoY + 140, 14, 180, 180, 180, 255)
            Framework_DrawText("Quantity: " & qty.ToString(), infoX + 15, infoY + 160, 14, 180, 180, 180, 255)
        Else
            Framework_DrawText("(Empty Slot)", infoX + 80, infoY + 100, 16, 100, 100, 100, 255)
        End If

        ' Draw inventory stats
        Dim statsY As Integer = 320
        Dim currentWeight As Single = Framework_Inventory_GetCurrentWeight(_inventoryId)
        Dim maxWeight As Single = Framework_Inventory_GetMaxWeight(_inventoryId)
        Dim usedSlots As Integer = Framework_Inventory_GetUsedSlotCount(_inventoryId)

        Framework_DrawText("Inventory Stats:", infoX + 10, statsY, 18, 255, 200, 100, 255)
        Framework_DrawText("Weight: " & currentWeight.ToString("F1") & "/" & maxWeight.ToString("F0"), infoX + 15, statsY + 30, 14, 200, 200, 200, 255)
        Framework_DrawText("Slots Used: " & usedSlots.ToString() & "/" & capacity.ToString(), infoX + 15, statsY + 50, 14, 200, 200, 200, 255)

        ' Weight bar
        Dim barX As Integer = infoX + 15
        Dim barY As Integer = statsY + 75
        Dim barW As Integer = 200
        Dim barH As Integer = 15
        Dim fillW As Integer = CInt((currentWeight / maxWeight) * barW)
        Framework_DrawRectangle(barX, barY, barW, barH, 50, 50, 50, 255)
        Dim weightColor As Byte = If(currentWeight > maxWeight * 0.8F, 255, 100)
        Framework_DrawRectangle(barX, barY, fillW, barH, weightColor, 150, 100, 255)
        Framework_DrawRectangleLines(barX, barY, barW, barH, 100, 100, 100, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ==========================================
' QUEST SYSTEM DEMO
' ==========================================
Class DemoQuestScene
    Inherits Scene

    Private _questHandles As New List(Of Integer)
    Private _selectedQuest As Integer = 0

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoQuestScene OnEnter - Showcasing Quest System")

        ' Define quest 1: Kill quest
        Dim q1 As Integer = Framework_Quest_Define("slay_goblins")
        Framework_Quest_SetName(q1, "Goblin Slayer")
        Framework_Quest_SetDescription(q1, "The village is under threat! Defeat 5 goblins to protect the innocent.")
        Framework_Quest_SetCategory(q1, "Combat")
        Framework_Quest_SetLevel(q1, 1)
        Framework_Quest_AddObjective(q1, 1, "Kill 5 Goblins", 5) ' Type 1 = Kill
        Framework_Quest_SetRewardExperience(q1, 100)
        Framework_Quest_SetRewardCurrency(q1, 0, 50)
        _questHandles.Add(q1)

        ' Define quest 2: Collect quest
        Dim q2 As Integer = Framework_Quest_Define("gather_herbs")
        Framework_Quest_SetName(q2, "Herb Gathering")
        Framework_Quest_SetDescription(q2, "The healer needs medicinal herbs. Collect 3 herbs from the forest.")
        Framework_Quest_SetCategory(q2, "Gathering")
        Framework_Quest_SetLevel(q2, 1)
        Framework_Quest_AddObjective(q2, 2, "Collect 3 Herbs", 3) ' Type 2 = Collect
        Framework_Quest_SetRewardExperience(q2, 50)
        _questHandles.Add(q2)

        ' Define quest 3: Talk quest
        Dim q3 As Integer = Framework_Quest_Define("meet_elder")
        Framework_Quest_SetName(q3, "Meet the Elder")
        Framework_Quest_SetDescription(q3, "The village elder wishes to speak with you about an important matter.")
        Framework_Quest_SetCategory(q3, "Story")
        Framework_Quest_SetLevel(q3, 2)
        Framework_Quest_AddObjective(q3, 3, "Talk to Village Elder", 1) ' Type 3 = Talk
        Framework_Quest_SetRewardExperience(q3, 75)
        _questHandles.Add(q3)

        ' Define quest 4: Timed quest
        Dim q4 As Integer = Framework_Quest_Define("time_trial")
        Framework_Quest_SetName(q4, "Speed Runner")
        Framework_Quest_SetDescription(q4, "Complete this challenge before time runs out!")
        Framework_Quest_SetCategory(q4, "Challenge")
        Framework_Quest_SetLevel(q4, 3)
        Framework_Quest_SetTimeLimit(q4, 60.0F) ' 60 seconds
        Framework_Quest_AddObjective(q4, 0, "Reach the finish line", 1) ' Type 0 = Custom
        Framework_Quest_SetRewardExperience(q4, 200)
        _questHandles.Add(q4)
    End Sub

    Protected Overrides Sub OnExit()
        Framework_Quest_UndefineAll()
        Console.WriteLine("DemoQuestScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_PlaySoundH(sfxHit)
            ChangeTo(New TitleScene)
            Return
        End If

        Framework_Quest_Update(dt)

        ' Navigate quests
        If Framework_IsKeyPressed(Keys.UP) AndAlso _selectedQuest > 0 Then
            _selectedQuest -= 1
        ElseIf Framework_IsKeyPressed(Keys.DOWN) AndAlso _selectedQuest < _questHandles.Count - 1 Then
            _selectedQuest += 1
        End If

        ' Start quest with S
        If Framework_IsKeyPressed(Keys.S) AndAlso _selectedQuest < _questHandles.Count Then
            Dim qh As Integer = _questHandles(_selectedQuest)
            If Framework_Quest_CanStart(qh) Then
                Framework_Quest_Start(qh)
            End If
        End If

        ' Simulate progress with P
        If Framework_IsKeyPressed(Keys.P) AndAlso _selectedQuest < _questHandles.Count Then
            Dim qh As Integer = _questHandles(_selectedQuest)
            If Framework_Quest_IsActive(qh) Then
                Framework_Quest_AddObjectiveProgress(qh, 0, 1)
            End If
        End If

        ' Complete quest with C
        If Framework_IsKeyPressed(Keys.C) AndAlso _selectedQuest < _questHandles.Count Then
            Dim qh As Integer = _questHandles(_selectedQuest)
            Framework_Quest_Complete(qh)
        End If

        ' Abandon quest with A
        If Framework_IsKeyPressed(Keys.A) AndAlso _selectedQuest < _questHandles.Count Then
            Dim qh As Integer = _questHandles(_selectedQuest)
            Framework_Quest_Abandon(qh)
        End If

        ' Reset quest with R
        If Framework_IsKeyPressed(Keys.R) AndAlso _selectedQuest < _questHandles.Count Then
            Dim qh As Integer = _questHandles(_selectedQuest)
            Framework_Quest_Reset(qh)
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(35, 25, 40, 255)

        ' Draw title
        Framework_DrawText("QUEST SYSTEM DEMO", 20, 20, 30, 255, 150, 200, 255)
        Framework_DrawText("UP/DOWN: Select | S: Start | P: Progress | C: Complete | A: Abandon | R: Reset | ESC: Exit", 20, 60, 13, 200, 200, 200, 255)

        ' Draw quest list
        Dim listX As Integer = 30
        Dim listY As Integer = 100
        Dim listW As Integer = 350
        Dim itemH As Integer = 80

        Framework_DrawText("Available Quests:", listX, listY - 25, 18, 255, 220, 100, 255)

        For i As Integer = 0 To _questHandles.Count - 1
            Dim qh As Integer = _questHandles(i)
            Dim itemY As Integer = listY + i * (itemH + 10)

            ' Background
            Dim bgColor As Byte = If(i = _selectedQuest, 60, 40)
            Framework_DrawRectangle(listX, itemY, listW, itemH, bgColor, bgColor + 10, bgColor + 20, 255)
            If i = _selectedQuest Then
                Framework_DrawRectangleLines(listX, itemY, listW, itemH, 255, 200, 100, 255)
            End If

            ' Quest name
            Dim nameStr As String = GetQuestNameSafe(qh)
            Dim state As Integer = Framework_Quest_GetState(qh)
            Dim stateNames() As String = {"Not Started", "In Progress", "Completed", "Failed"}
            Dim stateColors(,) As Byte = {{150, 150, 150}, {255, 200, 100}, {100, 255, 100}, {255, 100, 100}}

            Framework_DrawText(nameStr, listX + 10, itemY + 10, 18, 255, 255, 255, 255)
            Framework_DrawText("[" & stateNames(Math.Min(state, 3)) & "]", listX + 10, itemY + 35, 14, stateColors(state, 0), stateColors(state, 1), stateColors(state, 2), 255)

            ' Progress bar
            If state = 1 Then ' In Progress
                Dim progress As Single = Framework_Quest_GetCompletionPercent(qh)
                Dim barX As Integer = listX + 10
                Dim barY As Integer = itemY + 55
                Dim barW As Integer = listW - 20
                Framework_DrawRectangle(barX, barY, barW, 10, 50, 50, 50, 255)
                Framework_DrawRectangle(barX, barY, CInt(progress * barW), 10, 100, 200, 100, 255)
            End If
        Next

        ' Draw quest details panel
        Dim detailX As Integer = 410
        Dim detailY As Integer = 100
        Dim detailW As Integer = WINDOW_WIDTH - detailX - 30
        Dim detailH As Integer = 350

        Framework_DrawRectangle(detailX, detailY, detailW, detailH, 30, 30, 45, 255)
        Framework_DrawRectangleLines(detailX, detailY, detailW, detailH, 80, 80, 100, 255)
        Framework_DrawText("Quest Details", detailX + 10, detailY + 10, 20, 255, 200, 100, 255)

        If _selectedQuest < _questHandles.Count Then
            Dim qh As Integer = _questHandles(_selectedQuest)

            Dim nameStr As String = GetQuestNameSafe(qh)
            Dim descStr As String = GetQuestDescriptionSafe(qh)
            Dim catStr As String = GetQuestCategorySafe(qh)
            Dim level As Integer = Framework_Quest_GetLevel(qh)
            Dim state As Integer = Framework_Quest_GetState(qh)

            Framework_DrawText(nameStr, detailX + 15, detailY + 45, 22, 255, 255, 255, 255)
            Framework_DrawText("Category: " & catStr & " | Level: " & level.ToString(), detailX + 15, detailY + 75, 14, 180, 180, 180, 255)

            ' Word wrap description
            Dim descY As Integer = detailY + 100
            Dim words() As String = descStr.Split(" "c)
            Dim line As String = ""
            For Each word In words
                If line.Length + word.Length > 45 Then
                    Framework_DrawText(line, detailX + 15, descY, 14, 200, 200, 200, 255)
                    line = word
                    descY += 18
                Else
                    If line.Length > 0 Then line &= " "
                    line &= word
                End If
            Next
            If line.Length > 0 Then Framework_DrawText(line, detailX + 15, descY, 14, 200, 200, 200, 255)

            ' Objectives
            Dim objY As Integer = detailY + 180
            Framework_DrawText("Objectives:", detailX + 15, objY, 16, 255, 220, 100, 255)
            objY += 25

            Dim objCount As Integer = Framework_Quest_GetObjectiveCount(qh)
            For o As Integer = 0 To objCount - 1
                Dim objDescPtr As IntPtr = Framework_Quest_GetObjectiveDescription(qh, o)
                Dim objDesc As String = If(objDescPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(objDescPtr), "")
                Dim progress As Integer = Framework_Quest_GetObjectiveProgress(qh, o)
                Dim required As Integer = Framework_Quest_GetObjectiveRequired(qh, o)
                Dim complete As Boolean = Framework_Quest_IsObjectiveComplete(qh, o)

                Dim checkMark As String = If(complete, "[X]", "[ ]")
                Dim objColor As Byte = If(complete, 100, 200)
                Framework_DrawText(checkMark & " " & objDesc & " (" & progress.ToString() & "/" & required.ToString() & ")", detailX + 20, objY, 14, objColor, 255, objColor, 255)
                objY += 20
            Next

            ' Time remaining for timed quests
            Dim timeRemaining As Single = Framework_Quest_GetTimeRemaining(qh)
            If timeRemaining > 0 AndAlso state = 1 Then
                Framework_DrawText("Time Remaining: " & CInt(timeRemaining).ToString() & "s", detailX + 15, detailY + detailH - 40, 16, 255, 100, 100, 255)
            End If
        End If

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub

    Private Function GetQuestNameSafe(qh As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetName(qh)
        Return If(ptr <> IntPtr.Zero, Marshal.PtrToStringAnsi(ptr), "Unknown Quest")
    End Function

    Private Function GetQuestDescriptionSafe(qh As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetDescription(qh)
        Return If(ptr <> IntPtr.Zero, Marshal.PtrToStringAnsi(ptr), "")
    End Function

    Private Function GetQuestCategorySafe(qh As Integer) As String
        Dim ptr As IntPtr = Framework_Quest_GetCategory(qh)
        Return If(ptr <> IntPtr.Zero, Marshal.PtrToStringAnsi(ptr), "General")
    End Function
End Class

' ==========================================
' LIGHTING SYSTEM DEMO
' ==========================================
Class DemoLightingScene
    Inherits Scene

    Private _lights As New List(Of Integer)
    Private _playerX As Single = 400
    Private _playerY As Single = 300
    Private _playerLight As Integer = -1
    Private _ambientIntensity As Single = 0.2F
    Private _selectedLight As Integer = 0

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoLightingScene OnEnter - Showcasing 2D Lighting System")

        ' Initialize lighting system
        Framework_Lighting_Initialize(WINDOW_WIDTH, WINDOW_HEIGHT)
        Framework_Lighting_SetEnabled(True)
        Framework_Lighting_SetAmbientColor(20, 30, 50)
        Framework_Lighting_SetAmbientIntensity(_ambientIntensity)

        ' Create player torch light
        _playerLight = Framework_Light_CreatePoint(_playerX, _playerY, 150)
        Framework_Light_SetColor(_playerLight, 255, 200, 100)
        Framework_Light_SetIntensity(_playerLight, 1.0F)
        Framework_Light_SetFlicker(_playerLight, 0.1F, 8.0F)
        _lights.Add(_playerLight)

        ' Create static lights around the scene
        Dim light1 As Integer = Framework_Light_CreatePoint(100, 100, 100)
        Framework_Light_SetColor(light1, 255, 100, 100)
        Framework_Light_SetPulse(light1, 0.5F, 1.0F, 2.0F)
        _lights.Add(light1)

        Dim light2 As Integer = Framework_Light_CreatePoint(700, 100, 120)
        Framework_Light_SetColor(light2, 100, 255, 100)
        _lights.Add(light2)

        Dim light3 As Integer = Framework_Light_CreatePoint(100, 500, 80)
        Framework_Light_SetColor(light3, 100, 100, 255)
        _lights.Add(light3)

        ' Create a spotlight
        Dim spotlight As Integer = Framework_Light_CreateSpot(600, 400, 200, 45, 30)
        Framework_Light_SetColor(spotlight, 255, 255, 200)
        _lights.Add(spotlight)
    End Sub

    Protected Overrides Sub OnExit()
        Framework_Light_DestroyAll()
        Framework_Lighting_Shutdown()
        Console.WriteLine("DemoLightingScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_PlaySoundH(sfxHit)
            ChangeTo(New TitleScene)
            Return
        End If

        ' Move player with arrow keys
        Dim speed As Single = 200 * dt
        If Framework_IsKeyDown(Keys.LEFT) Then _playerX -= speed
        If Framework_IsKeyDown(Keys.RIGHT) Then _playerX += speed
        If Framework_IsKeyDown(Keys.UP) Then _playerY -= speed
        If Framework_IsKeyDown(Keys.DOWN) Then _playerY += speed

        ' Clamp player position
        _playerX = Math.Max(20, Math.Min(WINDOW_WIDTH - 20, _playerX))
        _playerY = Math.Max(20, Math.Min(WINDOW_HEIGHT - 20, _playerY))

        ' Update player light position
        Framework_Light_SetPosition(_playerLight, _playerX, _playerY)

        ' Adjust ambient with A/Z
        If Framework_IsKeyDown(Keys.A) Then
            _ambientIntensity = Math.Min(1.0F, _ambientIntensity + 0.5F * dt)
            Framework_Lighting_SetAmbientIntensity(_ambientIntensity)
        ElseIf Framework_IsKeyDown(Keys.Z) Then
            _ambientIntensity = Math.Max(0.0F, _ambientIntensity - 0.5F * dt)
            Framework_Lighting_SetAmbientIntensity(_ambientIntensity)
        End If

        ' Select light with 1-5
        If Framework_IsKeyPressed(Keys.ONE) Then _selectedLight = 0
        If Framework_IsKeyPressed(Keys.TWO) AndAlso _lights.Count > 1 Then _selectedLight = 1
        If Framework_IsKeyPressed(Keys.THREE) AndAlso _lights.Count > 2 Then _selectedLight = 2
        If Framework_IsKeyPressed(Keys.FOUR) AndAlso _lights.Count > 3 Then _selectedLight = 3
        If Framework_IsKeyPressed(Keys.FIVE) AndAlso _lights.Count > 4 Then _selectedLight = 4

        ' Toggle selected light with T
        If Framework_IsKeyPressed(Keys.T) AndAlso _selectedLight < _lights.Count Then
            Dim lid As Integer = _lights(_selectedLight)
            Dim enabled As Boolean = Framework_Light_IsEnabled(lid)
            Framework_Light_SetEnabled(lid, Not enabled)
        End If

        ' Adjust selected light radius with Q/W
        If _selectedLight < _lights.Count Then
            Dim lid As Integer = _lights(_selectedLight)
            Dim radius As Single = Framework_Light_GetRadius(lid)
            If Framework_IsKeyDown(Keys.Q) Then
                Framework_Light_SetRadius(lid, Math.Max(20, radius - 100 * dt))
            ElseIf Framework_IsKeyDown(Keys.W) Then
                Framework_Light_SetRadius(lid, Math.Min(400, radius + 100 * dt))
            End If
        End If

        Framework_Lighting_Update(dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(10, 15, 25, 255)

        ' Begin lighting pass
        Framework_Lighting_BeginLightPass()

        ' Draw scene objects (will be affected by lighting)
        ' Draw some crates/objects
        For x As Integer = 0 To 3
            For y As Integer = 0 To 2
                Dim objX As Integer = 150 + x * 150
                Dim objY As Integer = 180 + y * 150
                Framework_DrawRectangle(objX, objY, 60, 60, 139, 90, 43, 255)
                Framework_DrawRectangleLines(objX, objY, 60, 60, 101, 67, 33, 255)
            Next
        Next

        ' Draw floor tiles
        For tx As Integer = 0 To WINDOW_WIDTH Step 80
            For ty As Integer = 0 To WINDOW_HEIGHT Step 80
                Dim shade As Byte = CByte(40 + ((tx \ 80 + ty \ 80) Mod 2) * 15)
                Framework_DrawRectangle(tx, ty, 80, 80, shade, shade + 5, shade + 10, 255)
            Next
        Next

        ' Draw player
        Framework_DrawCircle(CInt(_playerX), CInt(_playerY), 15, 200, 150, 100, 255)
        Framework_DrawCircleLines(CInt(_playerX), CInt(_playerY), 15, 255, 200, 150, 255)

        ' End lighting pass and render
        Framework_Lighting_EndLightPass()
        Framework_Lighting_RenderToScreen()

        ' Draw UI (not affected by lighting)
        Framework_DrawText("2D LIGHTING SYSTEM DEMO", 20, 20, 24, 255, 200, 100, 255)
        Framework_DrawText("Arrows: Move Player | A/Z: Ambient | 1-5: Select Light | T: Toggle | Q/W: Radius | ESC: Exit", 20, 50, 12, 200, 200, 200, 255)

        ' Light info
        Framework_DrawText("Ambient: " & (_ambientIntensity * 100).ToString("F0") & "%", 20, 530, 14, 180, 180, 180, 255)
        Framework_DrawText("Light Count: " & _lights.Count.ToString(), 20, 550, 14, 180, 180, 180, 255)

        If _selectedLight < _lights.Count Then
            Dim lid As Integer = _lights(_selectedLight)
            Dim radius As Single = Framework_Light_GetRadius(lid)
            Dim enabled As Boolean = Framework_Light_IsEnabled(lid)
            Framework_DrawText("Selected: Light " & (_selectedLight + 1).ToString() & " | Radius: " & radius.ToString("F0") & " | " & If(enabled, "ON", "OFF"), 20, 570, 14, 255, 200, 100, 255)
        End If

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ==========================================
' TIMER/EVENT SYSTEM DEMO
' ==========================================
Class DemoTimerEventScene
    Inherits Scene

    Private _timerIds As New List(Of Integer)
    Private _eventLog As New List(Of String)
    Private _eventCounter As Integer = 0
    Private _timerCounter As Integer = 0
    Private _damageEventId As Integer = -1
    Private _healEventId As Integer = -1
    Private _playerHealth As Integer = 100
    Private _sequenceId As Integer = -1
    Private _sequenceRunning As Boolean = False

    ' Callbacks need to be stored to prevent garbage collection
    Private _timerCallback As TimerCallback
    Private _eventCallback As EventCallback
    Private _eventCallbackInt As EventCallbackInt

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoTimerEventScene OnEnter - Showcasing Timer and Event Systems")

        ' Initialize callbacks
        _timerCallback = AddressOf OnTimerFired
        _eventCallback = AddressOf OnBasicEvent
        _eventCallbackInt = AddressOf OnIntEvent

        ' Register events
        _damageEventId = Framework_Event_Register("player_damage")
        _healEventId = Framework_Event_Register("player_heal")

        ' Subscribe to events
        Framework_Event_SubscribeInt(_damageEventId, _eventCallbackInt, IntPtr.Zero)
        Framework_Event_SubscribeInt(_healEventId, _eventCallbackInt, IntPtr.Zero)

        ' Create a repeating timer
        Dim repeatTimer As Integer = Framework_Timer_Every(2.0F, _timerCallback, IntPtr.Zero)
        _timerIds.Add(repeatTimer)

        AddLog("Demo started! Timer and Event systems ready.")
    End Sub

    Private Sub OnTimerFired(timerId As Integer, userData As IntPtr)
        _timerCounter += 1
        AddLog("Timer fired! Count: " & _timerCounter.ToString())
    End Sub

    Private Sub OnBasicEvent(eventId As Integer, userData As IntPtr)
        AddLog("Basic event " & eventId.ToString() & " received")
    End Sub

    Private Sub OnIntEvent(eventId As Integer, value As Integer, userData As IntPtr)
        If eventId = _damageEventId Then
            _playerHealth = Math.Max(0, _playerHealth - value)
            AddLog("DAMAGE! -" & value.ToString() & " HP (Health: " & _playerHealth.ToString() & ")")
        ElseIf eventId = _healEventId Then
            _playerHealth = Math.Min(100, _playerHealth + value)
            AddLog("HEAL! +" & value.ToString() & " HP (Health: " & _playerHealth.ToString() & ")")
        End If
    End Sub

    Private Sub AddLog(msg As String)
        _eventLog.Add("[" & Framework_GetTime().ToString("F1") & "s] " & msg)
        If _eventLog.Count > 12 Then
            _eventLog.RemoveAt(0)
        End If
    End Sub

    Protected Overrides Sub OnExit()
        Framework_Timer_CancelAll()
        Framework_Event_Clear()
        Console.WriteLine("DemoTimerEventScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_PlaySoundH(sfxHit)
            ChangeTo(New TitleScene)
            Return
        End If

        Framework_Timer_Update(dt)
        Framework_Event_ProcessQueue(dt)

        ' Create one-shot timer with 1
        If Framework_IsKeyPressed(Keys.ONE) Then
            Dim timerId As Integer = Framework_Timer_After(1.0F, _timerCallback, IntPtr.Zero)
            _timerIds.Add(timerId)
            AddLog("Created 1-second one-shot timer")
        End If

        ' Publish damage event with D
        If Framework_IsKeyPressed(Keys.D) Then
            Dim damage As Integer = New Random().Next(5, 20)
            Framework_Event_PublishInt(_damageEventId, damage)
        End If

        ' Publish heal event with H
        If Framework_IsKeyPressed(Keys.H) Then
            Dim heal As Integer = New Random().Next(10, 30)
            Framework_Event_PublishInt(_healEventId, heal)
        End If

        ' Queue delayed event with Q
        If Framework_IsKeyPressed(Keys.Q) Then
            Framework_Event_QueueDelayedInt(_damageEventId, 15, 2.0F)
            AddLog("Queued delayed damage event (2s)")
        End If

        ' Start/stop timer sequence with S
        If Framework_IsKeyPressed(Keys.S) Then
            If Not _sequenceRunning Then
                _sequenceId = Framework_Timer_CreateSequence()
                Framework_Timer_SequenceAppend(_sequenceId, 0.5F, _timerCallback, IntPtr.Zero)
                Framework_Timer_SequenceAppend(_sequenceId, 0.5F, _timerCallback, IntPtr.Zero)
                Framework_Timer_SequenceAppend(_sequenceId, 0.5F, _timerCallback, IntPtr.Zero)
                Framework_Timer_SequenceSetLoop(_sequenceId, False)
                Framework_Timer_SequenceStart(_sequenceId)
                _sequenceRunning = True
                AddLog("Started timer sequence (3 steps)")
            Else
                Framework_Timer_SequenceCancel(_sequenceId)
                _sequenceRunning = False
                AddLog("Cancelled timer sequence")
            End If
        End If

        ' Pause/resume all timers with P
        If Framework_IsKeyPressed(Keys.P) Then
            Static paused As Boolean = False
            If paused Then
                Framework_Timer_ResumeAll()
                AddLog("Resumed all timers")
            Else
                Framework_Timer_PauseAll()
                AddLog("Paused all timers")
            End If
            paused = Not paused
        End If

        ' Clear all with C
        If Framework_IsKeyPressed(Keys.C) Then
            Framework_Timer_CancelAll()
            _timerIds.Clear()
            _timerCounter = 0
            AddLog("Cleared all timers")
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(30, 35, 45, 255)

        ' Draw title
        Framework_DrawText("TIMER & EVENT SYSTEM DEMO", 20, 20, 28, 100, 255, 200, 255)
        Framework_DrawText("1: One-shot Timer | D: Damage | H: Heal | Q: Delayed Event | S: Sequence | P: Pause | C: Clear | ESC: Exit", 20, 55, 12, 200, 200, 200, 255)

        ' Draw player health bar
        Dim healthX As Integer = 50
        Dim healthY As Integer = 100
        Framework_DrawText("Player Health:", healthX, healthY, 18, 255, 255, 255, 255)
        Framework_DrawRectangle(healthX, healthY + 25, 300, 30, 50, 50, 50, 255)
        Dim healthWidth As Integer = CInt((_playerHealth / 100.0F) * 300)
        Dim healthR As Byte = CByte(255 - _playerHealth * 2)
        Dim healthG As Byte = CByte(_playerHealth * 2.5)
        Framework_DrawRectangle(healthX, healthY + 25, healthWidth, 30, healthR, healthG, 50, 255)
        Framework_DrawRectangleLines(healthX, healthY + 25, 300, 30, 100, 100, 100, 255)
        Framework_DrawText(_playerHealth.ToString() & "/100", healthX + 120, healthY + 30, 18, 255, 255, 255, 255)

        ' Draw stats panel
        Dim statsX As Integer = 400
        Dim statsY As Integer = 100
        Framework_DrawRectangle(statsX, statsY, 350, 100, 40, 45, 55, 255)
        Framework_DrawText("System Stats:", statsX + 10, statsY + 10, 18, 255, 200, 100, 255)
        Framework_DrawText("Active Timers: " & Framework_Timer_GetActiveCount().ToString(), statsX + 15, statsY + 40, 14, 200, 200, 200, 255)
        Framework_DrawText("Timer Counter: " & _timerCounter.ToString(), statsX + 15, statsY + 60, 14, 200, 200, 200, 255)
        Framework_DrawText("Queued Events: " & Framework_Event_GetQueuedCount().ToString(), statsX + 15, statsY + 80, 14, 200, 200, 200, 255)

        ' Draw event log
        Dim logX As Integer = 50
        Dim logY As Integer = 220
        Framework_DrawRectangle(logX, logY, WINDOW_WIDTH - 100, 330, 25, 30, 40, 255)
        Framework_DrawRectangleLines(logX, logY, WINDOW_WIDTH - 100, 330, 60, 60, 80, 255)
        Framework_DrawText("Event Log:", logX + 10, logY + 10, 18, 255, 200, 100, 255)

        Dim logLineY As Integer = logY + 40
        For Each logEntry In _eventLog
            Dim color As Byte = 200
            If logEntry.Contains("DAMAGE") Then
                Framework_DrawText(logEntry, logX + 15, logLineY, 14, 255, 100, 100, 255)
            ElseIf logEntry.Contains("HEAL") Then
                Framework_DrawText(logEntry, logX + 15, logLineY, 14, 100, 255, 100, 255)
            ElseIf logEntry.Contains("Timer") Then
                Framework_DrawText(logEntry, logX + 15, logLineY, 14, 255, 200, 100, 255)
            Else
                Framework_DrawText(logEntry, logX + 15, logLineY, 14, 180, 180, 180, 255)
            End If
            logLineY += 22
        Next

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ==========================================
' STATE MACHINE (FSM) DEMO
' ==========================================
Class DemoFSMScene
    Inherits Scene

    Private _fsmId As Integer = -1
    Private _idleState As Integer = -1
    Private _walkState As Integer = -1
    Private _jumpState As Integer = -1
    Private _attackState As Integer = -1
    Private _dieState As Integer = -1

    Private _playerX As Single = 400
    Private _playerY As Single = 400
    Private _playerVelY As Single = 0
    Private _groundY As Single = 400
    Private _isGrounded As Boolean = True
    Private _facingRight As Boolean = True
    Private _health As Integer = 100

    Private _stateLog As New List(Of String)

    ' Callbacks
    Private _idleEnter As StateEnterCallback
    Private _idleUpdate As StateUpdateCallback
    Private _walkEnter As StateEnterCallback
    Private _walkUpdate As StateUpdateCallback
    Private _jumpEnter As StateEnterCallback
    Private _jumpUpdate As StateUpdateCallback
    Private _attackEnter As StateEnterCallback
    Private _attackUpdate As StateUpdateCallback
    Private _dieEnter As StateEnterCallback

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoFSMScene OnEnter - Showcasing State Machine System")

        ' Initialize callbacks
        _idleEnter = AddressOf OnIdleEnter
        _idleUpdate = AddressOf OnIdleUpdate
        _walkEnter = AddressOf OnWalkEnter
        _walkUpdate = AddressOf OnWalkUpdate
        _jumpEnter = AddressOf OnJumpEnter
        _jumpUpdate = AddressOf OnJumpUpdate
        _attackEnter = AddressOf OnAttackEnter
        _attackUpdate = AddressOf OnAttackUpdate
        _dieEnter = AddressOf OnDieEnter

        ' Create FSM
        _fsmId = Framework_FSM_Create("PlayerFSM")
        Framework_FSM_SetDebugEnabled(_fsmId, True)

        ' Add states
        _idleState = Framework_FSM_AddState(_fsmId, "Idle")
        _walkState = Framework_FSM_AddState(_fsmId, "Walk")
        _jumpState = Framework_FSM_AddState(_fsmId, "Jump")
        _attackState = Framework_FSM_AddState(_fsmId, "Attack")
        _dieState = Framework_FSM_AddState(_fsmId, "Die")

        ' Set state callbacks
        Framework_FSM_SetStateEnter(_fsmId, _idleState, _idleEnter, IntPtr.Zero)
        Framework_FSM_SetStateUpdate(_fsmId, _idleState, _idleUpdate, IntPtr.Zero)
        Framework_FSM_SetStateEnter(_fsmId, _walkState, _walkEnter, IntPtr.Zero)
        Framework_FSM_SetStateUpdate(_fsmId, _walkState, _walkUpdate, IntPtr.Zero)
        Framework_FSM_SetStateEnter(_fsmId, _jumpState, _jumpEnter, IntPtr.Zero)
        Framework_FSM_SetStateUpdate(_fsmId, _jumpState, _jumpUpdate, IntPtr.Zero)
        Framework_FSM_SetStateEnter(_fsmId, _attackState, _attackEnter, IntPtr.Zero)
        Framework_FSM_SetStateUpdate(_fsmId, _attackState, _attackUpdate, IntPtr.Zero)
        Framework_FSM_SetStateEnter(_fsmId, _dieState, _dieEnter, IntPtr.Zero)

        ' Add transitions (all states can go to each other)
        Framework_FSM_AddTransition(_fsmId, _idleState, _walkState)
        Framework_FSM_AddTransition(_fsmId, _idleState, _jumpState)
        Framework_FSM_AddTransition(_fsmId, _idleState, _attackState)
        Framework_FSM_AddTransition(_fsmId, _walkState, _idleState)
        Framework_FSM_AddTransition(_fsmId, _walkState, _jumpState)
        Framework_FSM_AddTransition(_fsmId, _walkState, _attackState)
        Framework_FSM_AddTransition(_fsmId, _jumpState, _idleState)
        Framework_FSM_AddTransition(_fsmId, _jumpState, _walkState)
        Framework_FSM_AddTransition(_fsmId, _attackState, _idleState)
        Framework_FSM_AddTransition(_fsmId, _attackState, _walkState)

        ' Any state can go to Die
        Framework_FSM_AddAnyTransition(_fsmId, _dieState)

        ' Add triggers
        Framework_FSM_AddTrigger(_fsmId, "damage", -1, _dieState)

        ' Set initial state and start
        Framework_FSM_SetInitialState(_fsmId, _idleState)
        Framework_FSM_SetHistorySize(_fsmId, 10)
        Framework_FSM_Start(_fsmId)

        AddStateLog("FSM Started in Idle state")
    End Sub

    Private Sub OnIdleEnter(fsmId As Integer, stateId As Integer, previousState As Integer, userData As IntPtr)
        AddStateLog("-> Entered IDLE state")
    End Sub

    Private Sub OnIdleUpdate(fsmId As Integer, stateId As Integer, dt As Single, userData As IntPtr)
        ' Idle animation (slight bob)
    End Sub

    Private Sub OnWalkEnter(fsmId As Integer, stateId As Integer, previousState As Integer, userData As IntPtr)
        AddStateLog("-> Entered WALK state")
    End Sub

    Private Sub OnWalkUpdate(fsmId As Integer, stateId As Integer, dt As Single, userData As IntPtr)
        ' Walking movement handled in main update
    End Sub

    Private Sub OnJumpEnter(fsmId As Integer, stateId As Integer, previousState As Integer, userData As IntPtr)
        _playerVelY = -400
        _isGrounded = False
        AddStateLog("-> Entered JUMP state")
    End Sub

    Private Sub OnJumpUpdate(fsmId As Integer, stateId As Integer, dt As Single, userData As IntPtr)
        ' Gravity and jump physics handled in main update
    End Sub

    Private Sub OnAttackEnter(fsmId As Integer, stateId As Integer, previousState As Integer, userData As IntPtr)
        AddStateLog("-> Entered ATTACK state")
    End Sub

    Private Sub OnAttackUpdate(fsmId As Integer, stateId As Integer, dt As Single, userData As IntPtr)
        ' Attack animation timing
    End Sub

    Private Sub OnDieEnter(fsmId As Integer, stateId As Integer, previousState As Integer, userData As IntPtr)
        AddStateLog("-> Entered DIE state - Game Over!")
    End Sub

    Private Sub AddStateLog(msg As String)
        _stateLog.Add("[" & Framework_GetTime().ToString("F1") & "s] " & msg)
        If _stateLog.Count > 8 Then _stateLog.RemoveAt(0)
    End Sub

    Protected Overrides Sub OnExit()
        Framework_FSM_Destroy(_fsmId)
        Console.WriteLine("DemoFSMScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_PlaySoundH(sfxHit)
            ChangeTo(New TitleScene)
            Return
        End If

        Dim currentState As Integer = Framework_FSM_GetCurrentState(_fsmId)

        ' Don't process input if dead
        If currentState = _dieState Then
            If Framework_IsKeyPressed(Keys.R) Then
                ' Restart
                _health = 100
                _playerX = 400
                _playerY = _groundY
                _playerVelY = 0
                _isGrounded = True
                Framework_FSM_TransitionTo(_fsmId, _idleState)
                AddStateLog("Player revived!")
            End If
            Framework_FSM_Update(_fsmId, dt)
            Return
        End If

        ' Handle input based on current state
        Dim moving As Boolean = False
        If Framework_IsKeyDown(Keys.LEFT) Then
            _playerX -= 200 * dt
            _facingRight = False
            moving = True
        ElseIf Framework_IsKeyDown(Keys.RIGHT) Then
            _playerX += 200 * dt
            _facingRight = True
            moving = True
        End If

        ' Clamp position
        _playerX = Math.Max(30, Math.Min(WINDOW_WIDTH - 30, _playerX))

        ' Jump
        If Framework_IsKeyPressed(Keys.SPACE) AndAlso _isGrounded Then
            Framework_FSM_TransitionTo(_fsmId, _jumpState)
        End If

        ' Attack
        If Framework_IsKeyPressed(Keys.X) AndAlso currentState <> _jumpState Then
            Framework_FSM_TransitionTo(_fsmId, _attackState)
        End If

        ' Damage self (for testing)
        If Framework_IsKeyPressed(Keys.D) Then
            _health -= 25
            AddStateLog("Took 25 damage! Health: " & _health.ToString())
            If _health <= 0 Then
                Framework_FSM_FireTrigger(_fsmId, "damage")
            End If
        End If

        ' Handle jump physics
        If currentState = _jumpState Then
            _playerVelY += 800 * dt ' Gravity
            _playerY += _playerVelY * dt

            If _playerY >= _groundY Then
                _playerY = _groundY
                _playerVelY = 0
                _isGrounded = True
                If moving Then
                    Framework_FSM_TransitionTo(_fsmId, _walkState)
                Else
                    Framework_FSM_TransitionTo(_fsmId, _idleState)
                End If
            End If
        End If

        ' Handle attack state (auto-return after time)
        If currentState = _attackState Then
            Dim timeInState As Single = Framework_FSM_GetTimeInState(_fsmId)
            If timeInState > 0.3F Then
                If moving Then
                    Framework_FSM_TransitionTo(_fsmId, _walkState)
                Else
                    Framework_FSM_TransitionTo(_fsmId, _idleState)
                End If
            End If
        End If

        ' Handle idle/walk transitions
        If currentState = _idleState AndAlso moving Then
            Framework_FSM_TransitionTo(_fsmId, _walkState)
        ElseIf currentState = _walkState AndAlso Not moving AndAlso _isGrounded Then
            Framework_FSM_TransitionTo(_fsmId, _idleState)
        End If

        Framework_FSM_Update(_fsmId, dt)
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(40, 45, 55, 255)

        ' Draw title
        Framework_DrawText("STATE MACHINE (FSM) DEMO", 20, 20, 28, 255, 150, 100, 255)
        Framework_DrawText("Arrows: Move | SPACE: Jump | X: Attack | D: Damage | R: Revive | ESC: Exit", 20, 55, 12, 200, 200, 200, 255)

        ' Draw ground
        Framework_DrawRectangle(0, CInt(_groundY) + 25, WINDOW_WIDTH, 100, 60, 80, 50, 255)

        ' Draw player
        Dim currentState As Integer = Framework_FSM_GetCurrentState(_fsmId)
        Dim playerW As Integer = 40
        Dim playerH As Integer = 50
        Dim drawX As Integer = CInt(_playerX) - playerW \ 2
        Dim drawY As Integer = CInt(_playerY) - playerH + 25

        ' Color based on state
        Dim r As Byte = 100, g As Byte = 150, b As Byte = 200
        If currentState = _walkState Then
            r = 100 : g = 200 : b = 100
        ElseIf currentState = _jumpState Then
            r = 200 : g = 200 : b = 100
        ElseIf currentState = _attackState Then
            r = 255 : g = 100 : b = 100
        ElseIf currentState = _dieState Then
            r = 100 : g = 100 : b = 100
        End If

        ' Draw body
        Framework_DrawRectangle(drawX, drawY, playerW, playerH, r, g, b, 255)
        Framework_DrawRectangleLines(drawX, drawY, playerW, playerH, 255, 255, 255, 255)

        ' Draw direction indicator
        If _facingRight Then
            Framework_DrawTriangle(drawX + playerW, drawY + playerH \ 2, drawX + playerW + 15, drawY + playerH \ 2, drawX + playerW, drawY + playerH \ 2 - 10, 255, 255, 255, 255)
        Else
            Framework_DrawTriangle(drawX, drawY + playerH \ 2, drawX - 15, drawY + playerH \ 2, drawX, drawY + playerH \ 2 - 10, 255, 255, 255, 255)
        End If

        ' Attack effect
        If currentState = _attackState Then
            Dim attackX As Integer = If(_facingRight, drawX + playerW + 5, drawX - 35)
            Framework_DrawRectangle(attackX, drawY + 10, 30, 30, 255, 200, 50, 200)
        End If

        ' Draw health bar
        Dim healthX As Integer = 50
        Dim healthY As Integer = 100
        Framework_DrawText("Health:", healthX, healthY, 16, 255, 255, 255, 255)
        Framework_DrawRectangle(healthX + 70, healthY, 150, 20, 50, 50, 50, 255)
        Framework_DrawRectangle(healthX + 70, healthY, CInt(_health * 1.5F), 20, 255 - CByte(_health * 2), CByte(_health * 2), 50, 255)
        Framework_DrawText(_health.ToString() & "%", healthX + 130, healthY + 2, 14, 255, 255, 255, 255)

        ' Draw FSM info panel
        Dim infoX As Integer = 500
        Dim infoY As Integer = 100
        Framework_DrawRectangle(infoX, infoY, 280, 180, 35, 40, 50, 255)
        Framework_DrawRectangleLines(infoX, infoY, 280, 180, 80, 80, 100, 255)
        Framework_DrawText("FSM Info:", infoX + 10, infoY + 10, 18, 255, 200, 100, 255)

        ' Current state
        Dim stateNamePtr As IntPtr = Framework_FSM_GetStateName(_fsmId, currentState)
        Dim stateName As String = If(stateNamePtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(stateNamePtr), "?")
        Framework_DrawText("Current: " & stateName, infoX + 15, infoY + 40, 16, 255, 255, 255, 255)

        ' Time in state
        Dim timeInState As Single = Framework_FSM_GetTimeInState(_fsmId)
        Framework_DrawText("Time in State: " & timeInState.ToString("F2") & "s", infoX + 15, infoY + 65, 14, 180, 180, 180, 255)

        ' State changes
        Dim changes As Integer = Framework_FSM_GetStateChangeCount(_fsmId)
        Framework_DrawText("State Changes: " & changes.ToString(), infoX + 15, infoY + 85, 14, 180, 180, 180, 255)

        ' History count
        Dim histCount As Integer = Framework_FSM_GetHistoryCount(_fsmId)
        Framework_DrawText("History Size: " & histCount.ToString(), infoX + 15, infoY + 105, 14, 180, 180, 180, 255)

        ' Running status
        Dim running As Boolean = Framework_FSM_IsRunning(_fsmId)
        Framework_DrawText("Running: " & If(running, "Yes", "No"), infoX + 15, infoY + 125, 14, If(running, CByte(100), CByte(255)), If(running, CByte(255), CByte(100)), 100, 255)

        ' State colors legend
        Framework_DrawText("States: ", infoX + 15, infoY + 150, 12, 150, 150, 150, 255)
        Framework_DrawRectangle(infoX + 70, infoY + 150, 12, 12, 100, 150, 200, 255)
        Framework_DrawText("Idle", infoX + 85, infoY + 150, 10, 150, 150, 150, 255)
        Framework_DrawRectangle(infoX + 115, infoY + 150, 12, 12, 100, 200, 100, 255)
        Framework_DrawText("Walk", infoX + 130, infoY + 150, 10, 150, 150, 150, 255)
        Framework_DrawRectangle(infoX + 165, infoY + 150, 12, 12, 200, 200, 100, 255)
        Framework_DrawText("Jump", infoX + 180, infoY + 150, 10, 150, 150, 150, 255)
        Framework_DrawRectangle(infoX + 215, infoY + 150, 12, 12, 255, 100, 100, 255)
        Framework_DrawText("Atk", infoX + 230, infoY + 150, 10, 150, 150, 150, 255)

        ' Draw state log
        Dim logX As Integer = 50
        Dim logY As Integer = 450
        Framework_DrawRectangle(logX, logY, WINDOW_WIDTH - 100, 120, 30, 35, 45, 255)
        Framework_DrawText("State Log:", logX + 10, logY + 5, 14, 255, 200, 100, 255)

        Dim logLineY As Integer = logY + 25
        For Each entry In _stateLog
            Framework_DrawText(entry, logX + 15, logLineY, 12, 180, 180, 180, 255)
            logLineY += 14
        Next

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ==========================================
' SAVE/LOAD SYSTEM DEMO
' ==========================================
Class DemoSaveLoadScene
    Inherits Scene

    Private _playerName As String = "Hero"
    Private _playerLevel As Integer = 1
    Private _playerGold As Integer = 100
    Private _playerX As Single = 400
    Private _playerY As Single = 300
    Private _playTime As Single = 0
    Private _selectedSlot As Integer = 0
    Private _statusMessage As String = "Press S to save, L to load"
    Private _statusTimer As Single = 0

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoSaveLoadScene OnEnter - Showcasing Save/Load System")

        ' Set save directory
        Framework_Save_SetDirectory("saves")
    End Sub

    Protected Overrides Sub OnExit()
        Console.WriteLine("DemoSaveLoadScene OnExit")
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.BACKSPACE) OrElse Framework_IsKeyPressed(Keys.ESCAPE) Then
            Framework_PlaySoundH(sfxHit)
            ChangeTo(New TitleScene)
            Return
        End If

        _playTime += dt
        _statusTimer -= dt

        ' Move player
        Dim speed As Single = 150 * dt
        If Framework_IsKeyDown(Keys.LEFT) Then _playerX -= speed
        If Framework_IsKeyDown(Keys.RIGHT) Then _playerX += speed
        If Framework_IsKeyDown(Keys.UP) Then _playerY -= speed
        If Framework_IsKeyDown(Keys.DOWN) Then _playerY += speed

        _playerX = Math.Max(20, Math.Min(WINDOW_WIDTH - 20, _playerX))
        _playerY = Math.Max(100, Math.Min(WINDOW_HEIGHT - 20, _playerY))

        ' Select slot with 1-3
        If Framework_IsKeyPressed(Keys.ONE) Then _selectedSlot = 0
        If Framework_IsKeyPressed(Keys.TWO) Then _selectedSlot = 1
        If Framework_IsKeyPressed(Keys.THREE) Then _selectedSlot = 2

        ' Add gold with G
        If Framework_IsKeyPressed(Keys.G) Then
            _playerGold += 50
            ShowStatus("Gained 50 gold!")
        End If

        ' Level up with U
        If Framework_IsKeyPressed(Keys.U) Then
            _playerLevel += 1
            ShowStatus("Level up! Now level " & _playerLevel.ToString())
        End If

        ' Save with S
        If Framework_IsKeyPressed(Keys.S) Then
            If Framework_Save_BeginSave(_selectedSlot) Then
                Framework_Save_WriteString("playerName", _playerName)
                Framework_Save_WriteInt("playerLevel", _playerLevel)
                Framework_Save_WriteInt("playerGold", _playerGold)
                Framework_Save_WriteVector2("playerPos", _playerX, _playerY)
                Framework_Save_WriteFloat("playTime", _playTime)
                Framework_Save_SetMetadata("version", "1.0")
                Framework_Save_SetMetadata("character", _playerName)

                If Framework_Save_EndSave() Then
                    ShowStatus("Saved to slot " & (_selectedSlot + 1).ToString() & "!")
                Else
                    ShowStatus("Save failed!")
                End If
            End If
        End If

        ' Load with L
        If Framework_IsKeyPressed(Keys.L) Then
            If Framework_Save_SlotExists(_selectedSlot) Then
                If Framework_Save_BeginLoad(_selectedSlot) Then
                    Dim namePtr As IntPtr = Framework_Save_ReadString("playerName", "Unknown")
                    _playerName = If(namePtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(namePtr), "Unknown")
                    _playerLevel = Framework_Save_ReadInt("playerLevel", 1)
                    _playerGold = Framework_Save_ReadInt("playerGold", 0)
                    Framework_Save_ReadVector2("playerPos", _playerX, _playerY, 400, 300)
                    _playTime = Framework_Save_ReadFloat("playTime", 0)

                    Framework_Save_EndLoad()
                    ShowStatus("Loaded from slot " & (_selectedSlot + 1).ToString() & "!")
                Else
                    ShowStatus("Load failed!")
                End If
            Else
                ShowStatus("Slot " & (_selectedSlot + 1).ToString() & " is empty!")
            End If
        End If

        ' Delete with D
        If Framework_IsKeyPressed(Keys.D) Then
            If Framework_Save_DeleteSlot(_selectedSlot) Then
                ShowStatus("Deleted slot " & (_selectedSlot + 1).ToString())
            Else
                ShowStatus("Delete failed or slot empty")
            End If
        End If

        ' Quick save with Q
        If Framework_IsKeyPressed(Keys.Q) Then
            ' Save to slot 0 first
            If Framework_Save_BeginSave(0) Then
                Framework_Save_WriteString("playerName", _playerName)
                Framework_Save_WriteInt("playerLevel", _playerLevel)
                Framework_Save_WriteInt("playerGold", _playerGold)
                Framework_Save_WriteVector2("playerPos", _playerX, _playerY)
                Framework_Save_WriteFloat("playTime", _playTime)
                Framework_Save_EndSave()
                ShowStatus("Quick saved!")
            End If
        End If

        ' Quick load with W
        If Framework_IsKeyPressed(Keys.W) Then
            If Framework_Save_SlotExists(0) Then
                If Framework_Save_BeginLoad(0) Then
                    Dim namePtr As IntPtr = Framework_Save_ReadString("playerName", "Unknown")
                    _playerName = If(namePtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(namePtr), "Unknown")
                    _playerLevel = Framework_Save_ReadInt("playerLevel", 1)
                    _playerGold = Framework_Save_ReadInt("playerGold", 0)
                    Framework_Save_ReadVector2("playerPos", _playerX, _playerY, 400, 300)
                    _playTime = Framework_Save_ReadFloat("playTime", 0)
                    Framework_Save_EndLoad()
                    ShowStatus("Quick loaded!")
                End If
            Else
                ShowStatus("No quick save found!")
            End If
        End If
    End Sub

    Private Sub ShowStatus(msg As String)
        _statusMessage = msg
        _statusTimer = 3.0F
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(35, 40, 50, 255)

        ' Draw title
        Framework_DrawText("SAVE/LOAD SYSTEM DEMO", 20, 20, 28, 100, 200, 150, 255)
        Framework_DrawText("Arrows: Move | 1-3: Select Slot | S: Save | L: Load | D: Delete | Q: QuickSave | W: QuickLoad | G: Gold | U: Level | ESC: Exit", 20, 55, 11, 200, 200, 200, 255)

        ' Draw player character
        Framework_DrawCircle(CInt(_playerX), CInt(_playerY), 20, 100, 150, 200, 255)
        Framework_DrawCircleLines(CInt(_playerX), CInt(_playerY), 20, 200, 220, 255, 255)
        Framework_DrawText(_playerName, CInt(_playerX) - 20, CInt(_playerY) - 35, 14, 255, 255, 255, 255)

        ' Draw game state panel
        Dim stateX As Integer = 50
        Dim stateY As Integer = 100
        Framework_DrawRectangle(stateX, stateY, 250, 150, 40, 45, 55, 255)
        Framework_DrawRectangleLines(stateX, stateY, 250, 150, 80, 80, 100, 255)
        Framework_DrawText("Current Game State:", stateX + 10, stateY + 10, 16, 255, 200, 100, 255)

        Framework_DrawText("Name: " & _playerName, stateX + 15, stateY + 40, 14, 200, 200, 200, 255)
        Framework_DrawText("Level: " & _playerLevel.ToString(), stateX + 15, stateY + 60, 14, 200, 200, 200, 255)
        Framework_DrawText("Gold: " & _playerGold.ToString(), stateX + 15, stateY + 80, 14, 255, 215, 0, 255)
        Framework_DrawText("Position: " & CInt(_playerX).ToString() & ", " & CInt(_playerY).ToString(), stateX + 15, stateY + 100, 14, 200, 200, 200, 255)

        Dim minutes As Integer = CInt(_playTime) \ 60
        Dim seconds As Integer = CInt(_playTime) Mod 60
        Framework_DrawText("Play Time: " & minutes.ToString() & "m " & seconds.ToString() & "s", stateX + 15, stateY + 120, 14, 200, 200, 200, 255)

        ' Draw save slots panel
        Dim slotX As Integer = 350
        Dim slotY As Integer = 100
        Dim slotW As Integer = 200
        Dim slotH As Integer = 70

        Framework_DrawText("Save Slots:", slotX, slotY - 25, 18, 255, 200, 100, 255)

        For i As Integer = 0 To 2
            Dim itemY As Integer = slotY + i * (slotH + 10)

            ' Slot background
            Dim bgColor As Byte = If(i = _selectedSlot, 60, 40)
            Framework_DrawRectangle(slotX, itemY, slotW, slotH, bgColor, bgColor + 10, bgColor + 20, 255)
            If i = _selectedSlot Then
                Framework_DrawRectangleLines(slotX, itemY, slotW, slotH, 255, 200, 100, 255)
            Else
                Framework_DrawRectangleLines(slotX, itemY, slotW, slotH, 80, 80, 100, 255)
            End If

            ' Slot number
            Framework_DrawText("Slot " & (i + 1).ToString(), slotX + 10, itemY + 10, 16, 255, 255, 255, 255)

            ' Slot contents
            If Framework_Save_SlotExists(i) Then
                ' Try to read metadata
                Dim charPtr As IntPtr = Framework_Save_GetMetadata(i, "character")
                Dim charName As String = If(charPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(charPtr), "Unknown")
                Framework_DrawText("Character: " & charName, slotX + 15, itemY + 35, 12, 150, 200, 150, 255)
                Framework_DrawText("[Has Data]", slotX + 120, itemY + 10, 12, 100, 255, 100, 255)
            Else
                Framework_DrawText("(Empty)", slotX + 60, itemY + 35, 14, 100, 100, 100, 255)
            End If
        Next

        ' Draw status message
        If _statusTimer > 0 Then
            Dim alpha As Byte = CByte(Math.Min(255, _statusTimer * 255))
            Framework_DrawRectangle(WINDOW_WIDTH \ 2 - 150, WINDOW_HEIGHT - 80, 300, 40, 50, 100, 50, alpha)
            Framework_DrawText(_statusMessage, WINDOW_WIDTH \ 2 - 140, WINDOW_HEIGHT - 70, 18, 200, 255, 200, alpha)
        End If

        ' Draw instructions box
        Dim instrX As Integer = 580
        Dim instrY As Integer = 100
        Framework_DrawRectangle(instrX, instrY, 200, 150, 40, 40, 50, 255)
        Framework_DrawText("Controls:", instrX + 10, instrY + 10, 16, 255, 200, 100, 255)
        Framework_DrawText("S - Save to slot", instrX + 15, instrY + 35, 12, 180, 180, 180, 255)
        Framework_DrawText("L - Load from slot", instrX + 15, instrY + 50, 12, 180, 180, 180, 255)
        Framework_DrawText("D - Delete slot", instrX + 15, instrY + 65, 12, 180, 180, 180, 255)
        Framework_DrawText("Q - Quick save", instrX + 15, instrY + 80, 12, 180, 180, 180, 255)
        Framework_DrawText("W - Quick load", instrX + 15, instrY + 95, 12, 180, 180, 180, 255)
        Framework_DrawText("G - Add gold", instrX + 15, instrY + 110, 12, 180, 180, 180, 255)
        Framework_DrawText("U - Level up", instrX + 15, instrY + 125, 12, 180, 180, 180, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ============================================================================
' DEMO: Sprite Batching System
' ============================================================================
Class DemoBatchingScene
    Inherits Scene
    Private _batchId As Integer = 0
    Private _spriteCount As Integer = 1000
    Private _useBatching As Boolean = True
    Private _positions As List(Of Vector2)
    Private _velocities As List(Of Vector2)
    Private _rotations As List(Of Single)
    Private _textureHandle As Integer = 0
    Private _rnd As New Random()

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoBatchingScene OnEnter")
        ' Create a batch with max 10000 sprites
        _batchId = Framework_Batch_Create(10000)

        ' Load a simple texture
        _textureHandle = Framework_AcquireTextureH("images/blocks.png")

        ' Initialize sprite data
        _positions = New List(Of Vector2)
        _velocities = New List(Of Vector2)
        _rotations = New List(Of Single)

        For i As Integer = 0 To _spriteCount - 1
            _positions.Add(New Vector2(_rnd.Next(0, WINDOW_WIDTH), _rnd.Next(0, WINDOW_HEIGHT)))
            _velocities.Add(New Vector2(CSng(_rnd.NextDouble() * 200 - 100), CSng(_rnd.NextDouble() * 200 - 100)))
            _rotations.Add(CSng(_rnd.NextDouble() * 360))
        Next
    End Sub

    Protected Overrides Sub OnExit()
        If _batchId > 0 Then Framework_Batch_Destroy(_batchId)
        If _textureHandle > 0 Then Framework_ReleaseTextureH(_textureHandle)
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        ' ESC to return
        If Framework_IsKeyPressed(Keys.ESCAPE) Then
            SetCurrentScene(New TitleScene)
            Return
        End If

        ' Toggle batching
        If Framework_IsKeyPressed(Keys.B) Then
            _useBatching = Not _useBatching
        End If

        ' Adjust sprite count
        If Framework_IsKeyPressed(Keys.UP) AndAlso _spriteCount < 10000 Then
            _spriteCount += 500
            AddMoreSprites(500)
        End If
        If Framework_IsKeyPressed(Keys.DOWN) AndAlso _spriteCount > 100 Then
            _spriteCount -= 500
            RemoveSprites(500)
        End If

        ' Update sprite positions
        For i As Integer = 0 To _positions.Count - 1
            Dim p As Vector2 = _positions(i)
            Dim v As Vector2 = _velocities(i)
            p.x += v.x * dt
            p.y += v.y * dt
            _rotations(i) += 90 * dt

            ' Bounce off edges
            If p.x < 0 OrElse p.x > WINDOW_WIDTH Then v.x = -v.x
            If p.y < 0 OrElse p.y > WINDOW_HEIGHT Then v.y = -v.y

            _positions(i) = p
            _velocities(i) = v
        Next
    End Sub

    Private Sub AddMoreSprites(count As Integer)
        For i As Integer = 0 To count - 1
            _positions.Add(New Vector2(_rnd.Next(0, WINDOW_WIDTH), _rnd.Next(0, WINDOW_HEIGHT)))
            _velocities.Add(New Vector2(CSng(_rnd.NextDouble() * 200 - 100), CSng(_rnd.NextDouble() * 200 - 100)))
            _rotations.Add(CSng(_rnd.NextDouble() * 360))
        Next
    End Sub

    Private Sub RemoveSprites(count As Integer)
        For i As Integer = 0 To count - 1
            If _positions.Count > 0 Then
                _positions.RemoveAt(_positions.Count - 1)
                _velocities.RemoveAt(_velocities.Count - 1)
                _rotations.RemoveAt(_rotations.Count - 1)
            End If
        Next
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(20, 25, 30, 255)

        If _useBatching AndAlso _batchId > 0 Then
            ' Use sprite batching
            Framework_Batch_Clear(_batchId)
            For i As Integer = 0 To _positions.Count - 1
                Dim p As Vector2 = _positions(i)
                Framework_Batch_AddSprite(_batchId, _textureHandle, p.x, p.y, 32, 16, 0, 0, 32, 16, _rotations(i), 16, 8, 255, 255, 255, 255)
            Next
            Framework_Batch_DrawSorted(_batchId)
        Else
            ' Draw individually (no batching)
            For i As Integer = 0 To _positions.Count - 1
                Dim p As Vector2 = _positions(i)
                Dim src As New Rectangle(0, 0, 32, 16)
                Dim dest As New Rectangle(p.x, p.y, 32, 16)
                Dim origin As New Vector2(16, 8)
                Framework_DrawTextureProH(_textureHandle, src, dest, origin, _rotations(i), 255, 255, 255, 255)
            Next
        End If

        ' Draw info
        Framework_DrawRectangle(10, 10, 300, 130, 30, 30, 40, 220)
        Framework_DrawText("SPRITE BATCHING DEMO", 20, 20, 20, 255, 200, 100, 255)
        Framework_DrawText("Sprites: " & _positions.Count.ToString(), 20, 50, 16, 200, 200, 200, 255)
        Framework_DrawText("Batching: " & If(_useBatching, "ON", "OFF"), 20, 70, 16, If(_useBatching, CByte(100), CByte(255)), If(_useBatching, CByte(255), CByte(100)), 100, 255)
        Framework_DrawText("B - Toggle Batching", 20, 95, 14, 150, 150, 150, 255)
        Framework_DrawText("UP/DOWN - Adjust count (+/-500)", 20, 115, 14, 150, 150, 150, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ============================================================================
' DEMO: Texture Atlas System
' ============================================================================
Class DemoAtlasScene
    Inherits Scene
    Private _atlasId As Integer = 0
    Private _spriteIndices As New List(Of Integer)
    Private _selectedSprite As Integer = 0

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoAtlasScene OnEnter")
        ' Create a 1024x1024 atlas
        _atlasId = Framework_Atlas_Create(1024, 1024)

        ' Add some images to the atlas
        Dim idx1 As Integer = Framework_Atlas_AddImage(_atlasId, "images/blocks.png")
        If idx1 >= 0 Then _spriteIndices.Add(idx1)

        ' Pack the atlas to generate the texture
        Framework_Atlas_Pack(_atlasId)
    End Sub

    Protected Overrides Sub OnExit()
        If _atlasId > 0 Then Framework_Atlas_Destroy(_atlasId)
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.ESCAPE) Then
            SetCurrentScene(New TitleScene)
            Return
        End If

        ' Cycle through sprites
        If Framework_IsKeyPressed(Keys.LEFT) AndAlso _selectedSprite > 0 Then
            _selectedSprite -= 1
        End If
        If Framework_IsKeyPressed(Keys.RIGHT) AndAlso _selectedSprite < _spriteIndices.Count - 1 Then
            _selectedSprite += 1
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(25, 30, 35, 255)

        ' Draw atlas info
        Framework_DrawRectangle(10, 10, 300, 100, 30, 30, 40, 220)
        Framework_DrawText("TEXTURE ATLAS DEMO", 20, 20, 20, 255, 200, 100, 255)
        Framework_DrawText("Sprites in atlas: " & Framework_Atlas_GetSpriteCount(_atlasId).ToString(), 20, 50, 16, 200, 200, 200, 255)
        Framework_DrawText("LEFT/RIGHT - Select sprite", 20, 75, 14, 150, 150, 150, 255)

        ' Draw all sprites from atlas in a grid
        If _atlasId > 0 AndAlso _spriteIndices.Count > 0 Then
            Dim x As Single = 100
            Dim y As Single = 150
            Dim idx As Integer = 0
            For Each spriteIdx In _spriteIndices
                ' Draw the sprite
                Framework_Atlas_DrawSpriteEx(_atlasId, spriteIdx, x, y, 0, 2, 255, 255, 255, 255)

                ' Highlight selected
                If idx = _selectedSprite Then
                    Framework_DrawRectangleLines(CInt(x - 5), CInt(y - 5), 80, 50, 255, 255, 0, 255)
                End If

                x += 100
                If x > WINDOW_WIDTH - 100 Then
                    x = 100
                    y += 80
                End If
                idx += 1
            Next
        End If

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class

' ============================================================================
' DEMO: Level Editor System
' ============================================================================
Class DemoLevelScene
    Inherits Scene
    Private _levelId As Integer = 0
    Private _tilesetHandle As Integer = 0
    Private _selectedTile As Integer = 0
    Private _cursorX As Integer = 5
    Private _cursorY As Integer = 5
    Private Const TILE_SIZE As Integer = 32
    Private Const TILES_PER_ROW As Integer = 6

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoLevelScene OnEnter")
        ' Create a new level
        _levelId = Framework_Level_Create("DemoLevel")
        Framework_Level_SetSize(_levelId, 30, 18)
        Framework_Level_SetTileSize(_levelId, TILE_SIZE, TILE_SIZE)
        Framework_Level_SetBackground(_levelId, 30, 40, 50, 255)

        ' Add a tile layer
        Framework_Level_AddLayer(_levelId, "ground")

        ' Load tileset
        _tilesetHandle = Framework_AcquireTextureH("images/blocks.png")

        ' Create some initial tiles (floor)
        For x As Integer = 0 To 29
            Framework_Level_SetTile(_levelId, 0, x, 17, 0)
        Next
    End Sub

    Protected Overrides Sub OnExit()
        If _levelId > 0 Then Framework_Level_Destroy(_levelId)
        If _tilesetHandle > 0 Then Framework_ReleaseTextureH(_tilesetHandle)
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.ESCAPE) Then
            SetCurrentScene(New TitleScene)
            Return
        End If

        ' Move cursor
        If Framework_IsKeyPressed(Keys.LEFT) AndAlso _cursorX > 0 Then _cursorX -= 1
        If Framework_IsKeyPressed(Keys.RIGHT) AndAlso _cursorX < 29 Then _cursorX += 1
        If Framework_IsKeyPressed(Keys.UP) AndAlso _cursorY > 0 Then _cursorY -= 1
        If Framework_IsKeyPressed(Keys.DOWN) AndAlso _cursorY < 17 Then _cursorY += 1

        ' Select tile type
        If Framework_IsKeyPressed(Keys.ONE) Then _selectedTile = 0
        If Framework_IsKeyPressed(Keys.TWO) Then _selectedTile = 1
        If Framework_IsKeyPressed(Keys.THREE) Then _selectedTile = 2
        If Framework_IsKeyPressed(Keys.FOUR) Then _selectedTile = 3
        If Framework_IsKeyPressed(Keys.FIVE) Then _selectedTile = 4
        If Framework_IsKeyPressed(Keys.SIX) Then _selectedTile = 5

        ' Place/remove tile
        If Framework_IsKeyPressed(Keys.SPACE) Then
            Framework_Level_SetTile(_levelId, 0, _cursorX, _cursorY, _selectedTile)
        End If
        If Framework_IsKeyPressed(Keys.X) Then
            Framework_Level_SetTile(_levelId, 0, _cursorX, _cursorY, -1)
        End If

        ' Save/Load
        If Framework_IsKeyPressed(Keys.S) Then
            Framework_Level_SaveToFile(_levelId, "demo_level.json")
            Console.WriteLine("Level saved!")
        End If
        If Framework_IsKeyPressed(Keys.L) Then
            Dim loaded As Integer = Framework_Level_LoadFromFile("demo_level.json")
            If loaded > 0 Then
                If _levelId > 0 Then Framework_Level_Destroy(_levelId)
                _levelId = loaded
                Console.WriteLine("Level loaded!")
            End If
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(30, 40, 50, 255)

        ' Draw the level
        If _levelId > 0 AndAlso _tilesetHandle > 0 Then
            Framework_Level_Draw(_levelId, _tilesetHandle, TILES_PER_ROW)
        End If

        ' Draw cursor
        Framework_DrawRectangleLines(_cursorX * TILE_SIZE, _cursorY * TILE_SIZE, TILE_SIZE, TILE_SIZE, 255, 255, 0, 255)

        ' Draw info panel
        Framework_DrawRectangle(10, 10, 250, 170, 30, 30, 40, 220)
        Framework_DrawText("LEVEL EDITOR DEMO", 20, 20, 18, 255, 200, 100, 255)
        Framework_DrawText("Cursor: " & _cursorX.ToString() & ", " & _cursorY.ToString(), 20, 45, 14, 200, 200, 200, 255)
        Framework_DrawText("Selected Tile: " & _selectedTile.ToString(), 20, 65, 14, 200, 200, 200, 255)
        Framework_DrawText("Arrows - Move cursor", 20, 90, 12, 150, 150, 150, 255)
        Framework_DrawText("1-6 - Select tile type", 20, 105, 12, 150, 150, 150, 255)
        Framework_DrawText("SPACE - Place tile", 20, 120, 12, 150, 150, 150, 255)
        Framework_DrawText("X - Remove tile", 20, 135, 12, 150, 150, 150, 255)
        Framework_DrawText("S - Save / L - Load", 20, 150, 12, 150, 150, 150, 255)

        ' Draw tile palette
        Framework_DrawRectangle(WINDOW_WIDTH - 220, 10, 210, 80, 30, 30, 40, 220)
        Framework_DrawText("Tile Palette:", WINDOW_WIDTH - 210, 20, 14, 255, 200, 100, 255)
        For i As Integer = 0 To 5
            Dim px As Integer = WINDOW_WIDTH - 210 + i * 32
            Dim py As Integer = 45
            ' Draw tile preview
            Dim src As New Rectangle(i * 32, 0, 32, 16)
            Dim dest As New Rectangle(px, py, 32, 16)
            Framework_DrawTextureProH(_tilesetHandle, src, dest, New Vector2(0, 0), 0, 255, 255, 255, 255)
            If i = _selectedTile Then
                Framework_DrawRectangleLines(px - 2, py - 2, 36, 20, 255, 255, 0, 255)
            End If
        Next

        Framework_DrawFPS(WINDOW_WIDTH - 100, WINDOW_HEIGHT - 30)
    End Sub
End Class

' ============================================================================
' DEMO: Networking System (Stub API Demo)
' ============================================================================
Class DemoNetworkScene
    Inherits Scene
    Private _isServer As Boolean = False
    Private _isClient As Boolean = False
    Private _serverId As Integer = 0
    Private _clientId As Integer = 0
    Private _statusMessage As String = "Press H to Host or J to Join"

    Protected Overrides Sub OnEnter()
        Console.WriteLine("DemoNetworkScene OnEnter")
    End Sub

    Protected Overrides Sub OnExit()
        If _serverId > 0 Then Framework_Net_DestroyServer(_serverId)
        If _clientId > 0 Then Framework_Net_DestroyClient(_clientId)
    End Sub

    Protected Overrides Sub OnResume()
    End Sub

    Protected Overrides Sub OnUpdateFixed(dt As Double)
    End Sub

    Protected Overrides Sub OnUpdateFrame(dt As Single)
        If Framework_IsKeyPressed(Keys.ESCAPE) Then
            SetCurrentScene(New TitleScene)
            Return
        End If

        ' Host server
        If Framework_IsKeyPressed(Keys.H) AndAlso Not _isServer AndAlso Not _isClient Then
            _serverId = Framework_Net_CreateServer(7777, 4)
            If _serverId > 0 Then
                _isServer = True
                _statusMessage = "Server started on port 7777"
            End If
        End If

        ' Join as client
        If Framework_IsKeyPressed(Keys.J) AndAlso Not _isServer AndAlso Not _isClient Then
            _clientId = Framework_Net_CreateClient()
            If _clientId > 0 Then
                If Framework_Net_Connect(_clientId, "127.0.0.1", 7777) Then
                    _isClient = True
                    _statusMessage = "Connected to server!"
                Else
                    _statusMessage = "Failed to connect"
                End If
            End If
        End If

        ' Update network
        If _isServer AndAlso _serverId > 0 Then
            Framework_Net_UpdateServer(_serverId)
        End If
        If _isClient AndAlso _clientId > 0 Then
            Framework_Net_UpdateClient(_clientId)
        End If

        ' Disconnect
        If Framework_IsKeyPressed(Keys.D) Then
            If _isServer Then
                Framework_Net_DestroyServer(_serverId)
                _serverId = 0
                _isServer = False
                _statusMessage = "Server stopped"
            End If
            If _isClient Then
                Framework_Net_Disconnect(_clientId)
                Framework_Net_DestroyClient(_clientId)
                _clientId = 0
                _isClient = False
                _statusMessage = "Disconnected"
            End If
        End If
    End Sub

    Protected Overrides Sub OnDraw()
        Framework_ClearBackground(25, 30, 40, 255)

        ' Draw info panel
        Framework_DrawRectangle(WINDOW_WIDTH \ 2 - 200, 100, 400, 300, 30, 35, 45, 220)
        Framework_DrawText("NETWORKING DEMO", WINDOW_WIDTH \ 2 - 90, 120, 22, 255, 200, 100, 255)

        ' Status
        Framework_DrawText(_statusMessage, WINDOW_WIDTH \ 2 - 180, 170, 16, 200, 200, 200, 255)

        ' Connection info
        If _isServer Then
            Framework_DrawText("Mode: SERVER", WINDOW_WIDTH \ 2 - 180, 210, 18, 100, 255, 100, 255)
            Framework_DrawText("Clients: " & Framework_Net_GetClientCount(_serverId).ToString(), WINDOW_WIDTH \ 2 - 180, 240, 16, 200, 200, 200, 255)
            Framework_DrawText("Running: " & Framework_Net_ServerIsRunning(_serverId).ToString(), WINDOW_WIDTH \ 2 - 180, 260, 16, 200, 200, 200, 255)
        ElseIf _isClient Then
            Framework_DrawText("Mode: CLIENT", WINDOW_WIDTH \ 2 - 180, 210, 18, 100, 200, 255, 255)
            Framework_DrawText("Connected: " & Framework_Net_IsConnected(_clientId).ToString(), WINDOW_WIDTH \ 2 - 180, 240, 16, 200, 200, 200, 255)
            Framework_DrawText("Ping: " & Framework_Net_GetPing(_clientId).ToString() & " ms", WINDOW_WIDTH \ 2 - 180, 260, 16, 200, 200, 200, 255)
        Else
            Framework_DrawText("Mode: DISCONNECTED", WINDOW_WIDTH \ 2 - 180, 210, 18, 150, 150, 150, 255)
        End If

        ' Controls
        Framework_DrawText("H - Host Server", WINDOW_WIDTH \ 2 - 180, 300, 14, 150, 150, 150, 255)
        Framework_DrawText("J - Join Server", WINDOW_WIDTH \ 2 - 180, 320, 14, 150, 150, 150, 255)
        Framework_DrawText("D - Disconnect", WINDOW_WIDTH \ 2 - 180, 340, 14, 150, 150, 150, 255)

        ' Note about stub
        Framework_DrawRectangle(WINDOW_WIDTH \ 2 - 180, 370, 360, 20, 50, 40, 40, 255)
        Framework_DrawText("(Networking is a stub API for demonstration)", WINDOW_WIDTH \ 2 - 175, 373, 12, 200, 150, 100, 255)

        Framework_DrawFPS(WINDOW_WIDTH - 100, 10)
    End Sub
End Class