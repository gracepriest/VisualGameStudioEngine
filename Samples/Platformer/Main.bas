' Platformer - Simple Platform Game
' Demonstrates physics, collision detection, and level design

Const SCREEN_WIDTH = 800
Const SCREEN_HEIGHT = 600
Const GRAVITY = 800.0
Const JUMP_FORCE = -400.0
Const MOVE_SPEED = 250.0
Const TILE_SIZE = 40

' Player state
Dim playerX As Single = 100
Dim playerY As Single = 400
Dim playerVX As Single = 0
Dim playerVY As Single = 0
Dim onGround As Boolean = False
Dim facingRight As Boolean = True

' Collectibles
Dim coins As Integer = 0
Dim coinX(10) As Single
Dim coinY(10) As Single
Dim coinCollected(10) As Boolean

' Level data (20x15 grid)
' 0 = empty, 1 = solid block, 2 = platform (one-way), 3 = spike
Dim level(20, 15) As Integer

Sub Main()
    Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "Platformer")
    Framework_SetFixedStep(1.0 / 60.0)

    LoadLevel()

    While Not Framework_ShouldClose()
        Update()
        Draw()
    End While

    Framework_Shutdown()
End Sub

Sub LoadLevel()
    ' Clear level
    For x = 0 To 19
        For y = 0 To 14
            level(x, y) = 0
        Next
    Next

    ' Ground
    For x = 0 To 19
        level(x, 14) = 1
    Next

    ' Platforms
    For x = 3 To 6
        level(x, 11) = 2
    Next
    For x = 8 To 11
        level(x, 9) = 2
    Next
    For x = 13 To 16
        level(x, 7) = 2
    Next
    For x = 5 To 8
        level(x, 5) = 2
    Next

    ' Walls
    level(0, 13) = 1
    level(0, 12) = 1
    level(19, 13) = 1
    level(19, 12) = 1

    ' Spikes
    level(10, 13) = 3
    level(11, 13) = 3

    ' Place coins
    coinX(0) = 180 : coinY(0) = 400
    coinX(1) = 380 : coinY(1) = 320
    coinX(2) = 580 : coinY(2) = 240
    coinX(3) = 260 : coinY(3) = 160

    For i = 0 To 3
        coinCollected(i) = False
    Next
End Sub

Sub Update()
    Dim dt = Framework_GetDeltaTime()

    ' Horizontal movement
    playerVX = 0
    If Framework_IsKeyDown(KEY_LEFT) Or Framework_IsKeyDown(KEY_A) Then
        playerVX = -MOVE_SPEED
        facingRight = False
    End If
    If Framework_IsKeyDown(KEY_RIGHT) Or Framework_IsKeyDown(KEY_D) Then
        playerVX = MOVE_SPEED
        facingRight = True
    End If

    ' Jumping
    If onGround And (Framework_IsKeyPressed(KEY_SPACE) Or Framework_IsKeyPressed(KEY_UP) Or Framework_IsKeyPressed(KEY_W)) Then
        playerVY = JUMP_FORCE
        onGround = False
    End If

    ' Apply gravity
    playerVY += GRAVITY * dt

    ' Move player and check collisions
    MovePlayer(dt)

    ' Collect coins
    For i = 0 To 3
        If Not coinCollected(i) Then
            Dim dist = Sqrt((playerX - coinX(i)) ^ 2 + (playerY - coinY(i)) ^ 2)
            If dist < 30 Then
                coinCollected(i) = True
                coins += 1
            End If
        End If
    Next

    ' Check spike collision
    Dim tileX = Int(playerX / TILE_SIZE)
    Dim tileY = Int((playerY + 30) / TILE_SIZE)
    If tileX >= 0 And tileX < 20 And tileY >= 0 And tileY < 15 Then
        If level(tileX, tileY) = 3 Then
            ' Respawn player
            playerX = 100
            playerY = 400
            playerVY = 0
        End If
    End If
End Sub

Sub MovePlayer(dt As Single)
    ' Move horizontally
    playerX += playerVX * dt

    ' Check horizontal collisions
    Dim tileX1 = Int(playerX / TILE_SIZE)
    Dim tileX2 = Int((playerX + 24) / TILE_SIZE)
    Dim tileY1 = Int(playerY / TILE_SIZE)
    Dim tileY2 = Int((playerY + 30) / TILE_SIZE)

    For tx = tileX1 To tileX2
        For ty = tileY1 To tileY2
            If tx >= 0 And tx < 20 And ty >= 0 And ty < 15 Then
                If level(tx, ty) = 1 Then
                    ' Solid collision
                    If playerVX > 0 Then
                        playerX = tx * TILE_SIZE - 25
                    ElseIf playerVX < 0 Then
                        playerX = (tx + 1) * TILE_SIZE
                    End If
                End If
            End If
        Next
    Next

    ' Move vertically
    playerY += playerVY * dt
    onGround = False

    ' Check vertical collisions
    tileX1 = Int(playerX / TILE_SIZE)
    tileX2 = Int((playerX + 24) / TILE_SIZE)
    tileY1 = Int(playerY / TILE_SIZE)
    tileY2 = Int((playerY + 32) / TILE_SIZE)

    For tx = tileX1 To tileX2
        For ty = tileY1 To tileY2
            If tx >= 0 And tx < 20 And ty >= 0 And ty < 15 Then
                Dim tile = level(tx, ty)
                If tile = 1 Or (tile = 2 And playerVY > 0) Then
                    ' Solid or platform collision
                    If playerVY > 0 Then
                        playerY = ty * TILE_SIZE - 32
                        playerVY = 0
                        onGround = True
                    ElseIf playerVY < 0 And tile = 1 Then
                        playerY = (ty + 1) * TILE_SIZE
                        playerVY = 0
                    End If
                End If
            End If
        Next
    Next

    ' Keep player in bounds
    If playerX < 0 Then playerX = 0
    If playerX > SCREEN_WIDTH - 25 Then playerX = SCREEN_WIDTH - 25
    If playerY > SCREEN_HEIGHT Then
        playerY = 400
        playerVY = 0
        playerX = 100
    End If
End Sub

Function Sqrt(x As Single) As Single
    Return x ^ 0.5
End Function

Sub Draw()
    Framework_BeginDrawing()
    Framework_ClearBackground(100, 150, 200, 255)

    ' Draw level
    For x = 0 To 19
        For y = 0 To 14
            Dim px = x * TILE_SIZE
            Dim py = y * TILE_SIZE

            Select Case level(x, y)
                Case 1  ' Solid block
                    Framework_DrawRectangle(px, py, TILE_SIZE, TILE_SIZE, 80, 60, 40, 255)
                    Framework_DrawRectangle(px + 2, py + 2, TILE_SIZE - 4, TILE_SIZE - 4, 100, 80, 60, 255)
                Case 2  ' Platform
                    Framework_DrawRectangle(px, py, TILE_SIZE, 10, 120, 100, 80, 255)
                Case 3  ' Spike
                    Framework_DrawTriangle(px + 20, py, px, py + TILE_SIZE, px + TILE_SIZE, py + TILE_SIZE, 200, 50, 50, 255)
            End Select
        Next
    Next

    ' Draw coins
    For i = 0 To 3
        If Not coinCollected(i) Then
            Framework_DrawCircle(coinX(i), coinY(i), 12, 255, 220, 0, 255)
            Framework_DrawCircle(coinX(i), coinY(i), 8, 255, 200, 0, 255)
        End If
    Next

    ' Draw player
    Dim playerColor = If(onGround, 0, 100)
    Framework_DrawRectangle(playerX, playerY, 25, 32, 50, 150 + playerColor, 255, 255)
    ' Eyes
    If facingRight Then
        Framework_DrawRectangle(playerX + 15, playerY + 8, 5, 5, 0, 0, 0, 255)
    Else
        Framework_DrawRectangle(playerX + 5, playerY + 8, 5, 5, 0, 0, 0, 255)
    End If

    ' Draw UI
    Framework_DrawText($"Coins: {coins}/4", 20, 20, 24, 255, 255, 255, 255)

    ' Draw instructions
    Framework_DrawText("Arrow keys/WASD to move, SPACE to jump", 20, SCREEN_HEIGHT - 30, 16, 255, 255, 255, 200)

    ' Win message
    If coins >= 4 Then
        Framework_DrawText("You collected all coins!", SCREEN_WIDTH / 2 - 140, SCREEN_HEIGHT / 2, 28, 255, 220, 0, 255)
    End If

    Framework_EndDrawing()
End Sub
