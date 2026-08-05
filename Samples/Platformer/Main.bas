' Platformer - Simple Platform Game
' Demonstrates physics, collision detection, and level design

Const SCREEN_WIDTH As Integer = 800
Const SCREEN_HEIGHT As Integer = 600
Const GRAVITY As Integer = 800
Const JUMP_FORCE As Integer = -400
Const MOVE_SPEED As Integer = 250
Const TILE_SIZE As Integer = 40
Const GRID_WIDTH As Integer = 20
Const GRID_HEIGHT As Integer = 15
Const COIN_COUNT As Integer = 4
Const COIN_RADIUS_SQUARED As Integer = 900

' Key codes (GLFW/raylib values, declared the way the other samples declare them)
Const KEY_SPACE As Integer = 32
Const KEY_A As Integer = 65
Const KEY_D As Integer = 68
Const KEY_W As Integer = 87
Const KEY_RIGHT As Integer = 262
Const KEY_LEFT As Integer = 263
Const KEY_UP As Integer = 265

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
Dim level(GRID_WIDTH, GRID_HEIGHT) As Integer

Sub Main()
    GameInit(SCREEN_WIDTH, SCREEN_HEIGHT, "Platformer")

    LoadLevel()

    While Not GameShouldClose()
        Update()
        Draw()
    End While

    GameShutdown()
End Sub

Sub LoadLevel()
    ' Clear level
    For x As Integer = 0 To GRID_WIDTH - 1
        For y As Integer = 0 To GRID_HEIGHT - 1
            level(x, y) = 0
        Next
    Next

    ' Ground
    For x As Integer = 0 To GRID_WIDTH - 1
        level(x, 14) = 1
    Next

    ' Platforms
    For x As Integer = 3 To 6
        level(x, 11) = 2
    Next
    For x As Integer = 8 To 11
        level(x, 9) = 2
    Next
    For x As Integer = 13 To 16
        level(x, 7) = 2
    Next
    For x As Integer = 5 To 8
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
    coinX(0) = 180
    coinY(0) = 400
    coinX(1) = 380
    coinY(1) = 320
    coinX(2) = 580
    coinY(2) = 240
    coinX(3) = 260
    coinY(3) = 160

    For i As Integer = 0 To COIN_COUNT - 1
        coinCollected(i) = False
    Next
End Sub

Sub Update()
    Dim dt As Single = GameGetDeltaTime()

    ' Horizontal movement
    playerVX = 0
    If IsKeyDown(KEY_LEFT) Or IsKeyDown(KEY_A) Then
        playerVX = -MOVE_SPEED
        facingRight = False
    End If
    If IsKeyDown(KEY_RIGHT) Or IsKeyDown(KEY_D) Then
        playerVX = MOVE_SPEED
        facingRight = True
    End If

    ' Jumping
    If onGround And (IsKeyPressed(KEY_SPACE) Or IsKeyPressed(KEY_UP) Or IsKeyPressed(KEY_W)) Then
        playerVY = JUMP_FORCE
        onGround = False
    End If

    ' Apply gravity
    playerVY += GRAVITY * dt

    ' Move player and check collisions
    MovePlayer(dt)

    ' Collect coins. Compared as SQUARED distances so no square root is needed.
    For i As Integer = 0 To COIN_COUNT - 1
        If Not coinCollected(i) Then
            Dim dx As Single = playerX - coinX(i)
            Dim dy As Single = playerY - coinY(i)
            Dim distSquared As Single = dx * dx + dy * dy
            If distSquared < COIN_RADIUS_SQUARED Then
                coinCollected(i) = True
                coins += 1
            End If
        End If
    Next

    ' Check spike collision
    Dim tileX As Integer = CInt(playerX / TILE_SIZE)
    Dim tileY As Integer = CInt((playerY + 30) / TILE_SIZE)
    If tileX >= 0 And tileX < GRID_WIDTH And tileY >= 0 And tileY < GRID_HEIGHT Then
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
    Dim tileX1 As Integer = CInt(playerX / TILE_SIZE)
    Dim tileX2 As Integer = CInt((playerX + 24) / TILE_SIZE)
    Dim tileY1 As Integer = CInt(playerY / TILE_SIZE)
    Dim tileY2 As Integer = CInt((playerY + 30) / TILE_SIZE)

    For tx As Integer = tileX1 To tileX2
        For ty As Integer = tileY1 To tileY2
            If tx >= 0 And tx < GRID_WIDTH And ty >= 0 And ty < GRID_HEIGHT Then
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
    tileX1 = CInt(playerX / TILE_SIZE)
    tileX2 = CInt((playerX + 24) / TILE_SIZE)
    tileY1 = CInt(playerY / TILE_SIZE)
    tileY2 = CInt((playerY + 32) / TILE_SIZE)

    For tx As Integer = tileX1 To tileX2
        For ty As Integer = tileY1 To tileY2
            If tx >= 0 And tx < GRID_WIDTH And ty >= 0 And ty < GRID_HEIGHT Then
                Dim tile As Integer = level(tx, ty)
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

Sub Draw()
    GameBeginFrame()
    ClearBackground(100, 150, 200)

    ' Draw level
    For x As Integer = 0 To GRID_WIDTH - 1
        For y As Integer = 0 To GRID_HEIGHT - 1
            Dim px As Integer = x * TILE_SIZE
            Dim py As Integer = y * TILE_SIZE

            Select Case level(x, y)
                Case 1  ' Solid block
                    DrawRectangle(px, py, TILE_SIZE, TILE_SIZE, 80, 60, 40, 255)
                    DrawRectangle(px + 2, py + 2, TILE_SIZE - 4, TILE_SIZE - 4, 100, 80, 60, 255)
                Case 2  ' Platform
                    DrawRectangle(px, py, TILE_SIZE, 10, 120, 100, 80, 255)
                Case 3  ' Spike, outlined with lines (the framework has no triangle fill)
                    DrawLine(px + 20, py, px, py + TILE_SIZE, 200, 50, 50, 255)
                    DrawLine(px + 20, py, px + TILE_SIZE, py + TILE_SIZE, 200, 50, 50, 255)
                    DrawLine(px, py + TILE_SIZE, px + TILE_SIZE, py + TILE_SIZE, 200, 50, 50, 255)
            End Select
        Next
    Next

    ' Draw coins
    For i As Integer = 0 To COIN_COUNT - 1
        If Not coinCollected(i) Then
            DrawCircle(coinX(i), coinY(i), 12, 255, 220, 0, 255)
            DrawCircle(coinX(i), coinY(i), 8, 255, 200, 0, 255)
        End If
    Next

    ' Draw player
    Dim playerColor As Integer = 100
    If onGround Then
        playerColor = 0
    End If
    DrawRectangle(playerX, playerY, 25, 32, 50, 150 + playerColor, 255, 255)
    ' Eyes
    If facingRight Then
        DrawRectangle(playerX + 15, playerY + 8, 5, 5, 0, 0, 0, 255)
    Else
        DrawRectangle(playerX + 5, playerY + 8, 5, 5, 0, 0, 0, 255)
    End If

    ' Draw UI
    DrawText("Coins: " & coins & "/" & COIN_COUNT, 20, 20, 24, 255, 255, 255, 255)

    ' Draw instructions
    DrawText("Arrow keys/WASD to move, SPACE to jump", 20, SCREEN_HEIGHT - 30, 16, 255, 255, 255, 200)

    ' Win message
    If coins >= COIN_COUNT Then
        DrawText("You collected all coins!", SCREEN_WIDTH / 2 - 140, SCREEN_HEIGHT / 2, 28, 255, 220, 0, 255)
    End If

    GameEndFrame()
End Sub
