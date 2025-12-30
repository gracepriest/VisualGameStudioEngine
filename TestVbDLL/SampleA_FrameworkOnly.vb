' SampleA_FrameworkOnly.vb
' Demonstrates Framework v1.0 - Direct Raylib wrapper usage without ECS
' A simple "Catch the Falling Blocks" game

Imports System.Runtime.InteropServices
Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

''' <summary>
''' Sample A: Framework-only game demonstrating:
''' - Window initialization and game loop
''' - Input handling (keyboard)
''' - Basic 2D rendering (shapes, text)
''' - Audio playback
''' - Time and delta time usage
''' - Pause/Resume functionality
''' </summary>
Public Class SampleA_FrameworkOnly

    ' Game constants
    Private Const SCREEN_WIDTH As Integer = 800
    Private Const SCREEN_HEIGHT As Integer = 600
    Private Const PLAYER_WIDTH As Integer = 80
    Private Const PLAYER_HEIGHT As Integer = 20
    Private Const PLAYER_SPEED As Single = 400.0F
    Private Const BLOCK_SIZE As Integer = 30
    Private Const BLOCK_FALL_SPEED As Single = 200.0F
    Private Const MAX_BLOCKS As Integer = 10

    ' Player state
    Private playerX As Single = SCREEN_WIDTH / 2 - PLAYER_WIDTH / 2
    Private playerY As Single = SCREEN_HEIGHT - 50

    ' Block state
    Private Structure FallingBlock
        Public X As Single
        Public Y As Single
        Public Active As Boolean
        Public R As Byte
        Public G As Byte
        Public B As Byte
    End Structure

    Private blocks(MAX_BLOCKS - 1) As FallingBlock
    Private rand As New Random()

    ' Game state
    Private score As Integer = 0
    Private gameOver As Boolean = False
    Private spawnTimer As Single = 0.0F
    Private Const SPAWN_INTERVAL As Single = 1.0F

    ' Callback delegate (must be stored to prevent GC)
    Private drawDelegate As DrawCallback

    Public Sub Run()
        ' Initialize framework
        If Not Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "Sample A: Catch the Blocks (Framework v1.0)") Then
            Console.WriteLine("Failed to initialize framework!")
            Return
        End If

        ' Setup game
        Framework_SetFixedStep(1.0 / 60.0)
        Framework_SetExitKey(Keys.ESCAPE)
        Framework_InitAudio()

        ' Wire draw callback
        drawDelegate = AddressOf OnDraw
        Framework_SetDrawCallback(drawDelegate)

        ' Initialize blocks
        For i = 0 To MAX_BLOCKS - 1
            blocks(i).Active = False
        Next

        ' Main game loop
        While Not Framework_ShouldClose()
            Framework_Update()
        End While

        ' Cleanup
        Framework_CloseAudio()
        Framework_Shutdown()
    End Sub

    Private Sub OnDraw()
        ' Get delta time
        Dim dt As Single = Framework_GetFrameTime()

        ' Handle pause toggle
        If Framework_IsKeyPressed(Keys.P) Then
            If Framework_IsPaused() Then
                Framework_Resume()
            Else
                Framework_Pause()
            End If
        End If

        ' Update only if not paused and not game over
        If Not Framework_IsPaused() AndAlso Not gameOver Then
            UpdateGame(dt)
        End If

        ' Handle restart
        If gameOver AndAlso Framework_IsKeyPressed(Keys.R) Then
            RestartGame()
        End If

        ' Render
        Framework_BeginDrawing()
        Framework_ClearBackground(30, 30, 50, 255)

        DrawGame()

        ' Draw UI
        Framework_DrawText("Score: " & score.ToString(), 10, 10, 24, 255, 255, 255, 255)
        Framework_DrawText("P = Pause | ESC = Quit", SCREEN_WIDTH - 220, 10, 16, 180, 180, 180, 255)
        Framework_DrawFPS(SCREEN_WIDTH - 100, SCREEN_HEIGHT - 30)

        ' Draw pause overlay
        If Framework_IsPaused() Then
            Framework_DrawRectangle(0, 0, SCREEN_WIDTH, SCREEN_HEIGHT, 0, 0, 0, 150)
            Framework_DrawText("PAUSED", SCREEN_WIDTH / 2 - 80, SCREEN_HEIGHT / 2 - 20, 48, 255, 255, 0, 255)
            Framework_DrawText("Press P to Resume", SCREEN_WIDTH / 2 - 100, SCREEN_HEIGHT / 2 + 40, 20, 200, 200, 200, 255)
        End If

        ' Draw game over
        If gameOver Then
            Framework_DrawRectangle(0, 0, SCREEN_WIDTH, SCREEN_HEIGHT, 0, 0, 0, 180)
            Framework_DrawText("GAME OVER", SCREEN_WIDTH / 2 - 120, SCREEN_HEIGHT / 2 - 40, 48, 255, 50, 50, 255)
            Framework_DrawText("Final Score: " & score.ToString(), SCREEN_WIDTH / 2 - 80, SCREEN_HEIGHT / 2 + 20, 24, 255, 255, 255, 255)
            Framework_DrawText("Press R to Restart", SCREEN_WIDTH / 2 - 90, SCREEN_HEIGHT / 2 + 60, 20, 200, 200, 200, 255)
        End If

        Framework_EndDrawing()
    End Sub

    Private Sub UpdateGame(dt As Single)
        ' Player movement
        If Framework_IsKeyDown(Keys.LEFT) OrElse Framework_IsKeyDown(Keys.A) Then
            playerX -= PLAYER_SPEED * dt
        End If
        If Framework_IsKeyDown(Keys.RIGHT) OrElse Framework_IsKeyDown(Keys.D) Then
            playerX += PLAYER_SPEED * dt
        End If

        ' Clamp player position
        If playerX < 0 Then playerX = 0
        If playerX > SCREEN_WIDTH - PLAYER_WIDTH Then playerX = SCREEN_WIDTH - PLAYER_WIDTH

        ' Spawn blocks
        spawnTimer += dt
        If spawnTimer >= SPAWN_INTERVAL Then
            spawnTimer = 0
            SpawnBlock()
        End If

        ' Update blocks
        For i = 0 To MAX_BLOCKS - 1
            If blocks(i).Active Then
                blocks(i).Y += BLOCK_FALL_SPEED * dt

                ' Check collision with player
                If blocks(i).Y + BLOCK_SIZE >= playerY AndAlso
                   blocks(i).Y <= playerY + PLAYER_HEIGHT AndAlso
                   blocks(i).X + BLOCK_SIZE >= playerX AndAlso
                   blocks(i).X <= playerX + PLAYER_WIDTH Then
                    ' Caught the block!
                    blocks(i).Active = False
                    score += 10
                End If

                ' Check if block fell off screen
                If blocks(i).Y > SCREEN_HEIGHT Then
                    blocks(i).Active = False
                    gameOver = True ' Missed a block = game over
                End If
            End If
        Next
    End Sub

    Private Sub SpawnBlock()
        ' Find inactive block slot
        For i = 0 To MAX_BLOCKS - 1
            If Not blocks(i).Active Then
                blocks(i).X = rand.Next(0, SCREEN_WIDTH - BLOCK_SIZE)
                blocks(i).Y = -BLOCK_SIZE
                blocks(i).Active = True
                blocks(i).R = CByte(rand.Next(100, 255))
                blocks(i).G = CByte(rand.Next(100, 255))
                blocks(i).B = CByte(rand.Next(100, 255))
                Exit For
            End If
        Next
    End Sub

    Private Sub DrawGame()
        ' Draw player
        Framework_DrawRectangle(CInt(playerX), CInt(playerY), PLAYER_WIDTH, PLAYER_HEIGHT, 100, 200, 255, 255)

        ' Draw blocks
        For i = 0 To MAX_BLOCKS - 1
            If blocks(i).Active Then
                Framework_DrawRectangle(CInt(blocks(i).X), CInt(blocks(i).Y), BLOCK_SIZE, BLOCK_SIZE,
                                       blocks(i).R, blocks(i).G, blocks(i).B, 255)
            End If
        Next
    End Sub

    Private Sub RestartGame()
        score = 0
        gameOver = False
        spawnTimer = 0
        playerX = SCREEN_WIDTH / 2 - PLAYER_WIDTH / 2

        For i = 0 To MAX_BLOCKS - 1
            blocks(i).Active = False
        Next
    End Sub

End Class
