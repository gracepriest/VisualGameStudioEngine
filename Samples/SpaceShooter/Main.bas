' Space Shooter - Arcade Game
' A simple space shooter demonstrating ECS and game mechanics

Const SCREEN_WIDTH = 800
Const SCREEN_HEIGHT = 600
Const PLAYER_SPEED = 300.0
Const BULLET_SPEED = 500.0
Const ENEMY_SPEED = 100.0
Const MAX_BULLETS = 50
Const MAX_ENEMIES = 20

' Player state
Dim playerX As Single = 400
Dim playerY As Single = 500
Dim score As Integer = 0
Dim lives As Integer = 3
Dim gameOver As Boolean = False

' Bullets
Dim bulletX(MAX_BULLETS) As Single
Dim bulletY(MAX_BULLETS) As Single
Dim bulletActive(MAX_BULLETS) As Boolean
Dim bulletCount As Integer = 0

' Enemies
Dim enemyX(MAX_ENEMIES) As Single
Dim enemyY(MAX_ENEMIES) As Single
Dim enemyActive(MAX_ENEMIES) As Boolean
Dim spawnTimer As Single = 0

' Shooting cooldown
Dim shootCooldown As Single = 0

Sub Main()
    Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "Space Shooter")
    Framework_SetFixedStep(1.0 / 60.0)

    InitializeGame()

    While Not Framework_ShouldClose()
        Update()
        Draw()
    End While

    Framework_Shutdown()
End Sub

Sub InitializeGame()
    playerX = SCREEN_WIDTH / 2
    playerY = SCREEN_HEIGHT - 80
    score = 0
    lives = 3
    gameOver = False

    ' Clear bullets
    For i = 0 To MAX_BULLETS - 1
        bulletActive(i) = False
    Next

    ' Clear enemies
    For i = 0 To MAX_ENEMIES - 1
        enemyActive(i) = False
    Next
End Sub

Sub Update()
    Dim dt = Framework_GetDeltaTime()

    If gameOver Then
        If Framework_IsKeyPressed(KEY_SPACE) Then
            InitializeGame()
        End If
        Return
    End If

    ' Player movement
    If Framework_IsKeyDown(KEY_LEFT) Or Framework_IsKeyDown(KEY_A) Then
        playerX -= PLAYER_SPEED * dt
    End If
    If Framework_IsKeyDown(KEY_RIGHT) Or Framework_IsKeyDown(KEY_D) Then
        playerX += PLAYER_SPEED * dt
    End If

    ' Clamp player position
    playerX = Clamp(playerX, 20, SCREEN_WIDTH - 40)

    ' Shooting
    shootCooldown -= dt
    If (Framework_IsKeyDown(KEY_SPACE) Or Framework_IsKeyDown(KEY_UP)) And shootCooldown <= 0 Then
        SpawnBullet(playerX + 10, playerY - 10)
        shootCooldown = 0.15  ' Fire rate
    End If

    ' Update bullets
    For i = 0 To MAX_BULLETS - 1
        If bulletActive(i) Then
            bulletY(i) -= BULLET_SPEED * dt
            If bulletY(i) < -10 Then
                bulletActive(i) = False
            End If
        End If
    Next

    ' Spawn enemies
    spawnTimer -= dt
    If spawnTimer <= 0 Then
        SpawnEnemy()
        spawnTimer = 1.0 + Rnd() * 1.5  ' Random spawn interval
    End If

    ' Update enemies
    For i = 0 To MAX_ENEMIES - 1
        If enemyActive(i) Then
            enemyY(i) += ENEMY_SPEED * dt

            ' Check collision with player
            If CheckCollision(enemyX(i), enemyY(i), 30, 30, playerX, playerY, 30, 30) Then
                enemyActive(i) = False
                lives -= 1
                If lives <= 0 Then
                    gameOver = True
                End If
            End If

            ' Remove if off screen
            If enemyY(i) > SCREEN_HEIGHT + 10 Then
                enemyActive(i) = False
            End If
        End If
    Next

    ' Check bullet-enemy collisions
    For b = 0 To MAX_BULLETS - 1
        If bulletActive(b) Then
            For e = 0 To MAX_ENEMIES - 1
                If enemyActive(e) Then
                    If CheckCollision(bulletX(b), bulletY(b), 5, 15, enemyX(e), enemyY(e), 30, 30) Then
                        bulletActive(b) = False
                        enemyActive(e) = False
                        score += 100
                        Exit For
                    End If
                End If
            Next
        End If
    Next
End Sub

Sub SpawnBullet(x As Single, y As Single)
    For i = 0 To MAX_BULLETS - 1
        If Not bulletActive(i) Then
            bulletX(i) = x
            bulletY(i) = y
            bulletActive(i) = True
            Return
        End If
    Next
End Sub

Sub SpawnEnemy()
    For i = 0 To MAX_ENEMIES - 1
        If Not enemyActive(i) Then
            enemyX(i) = 30 + Rnd() * (SCREEN_WIDTH - 60)
            enemyY(i) = -30
            enemyActive(i) = True
            Return
        End If
    Next
End Sub

Function CheckCollision(x1 As Single, y1 As Single, w1 As Single, h1 As Single, _
                        x2 As Single, y2 As Single, w2 As Single, h2 As Single) As Boolean
    Return x1 < x2 + w2 And x1 + w1 > x2 And y1 < y2 + h2 And y1 + h1 > y2
End Function

Function Clamp(value As Single, min As Single, max As Single) As Single
    If value < min Then Return min
    If value > max Then Return max
    Return value
End Function

Sub Draw()
    Framework_BeginDrawing()
    Framework_ClearBackground(10, 10, 25, 255)

    ' Draw stars background
    DrawStars()

    ' Draw player (spaceship triangle)
    Framework_DrawTriangle(playerX + 15, playerY, _
                          playerX, playerY + 30, _
                          playerX + 30, playerY + 30, _
                          0, 200, 255, 255)

    ' Draw bullets
    For i = 0 To MAX_BULLETS - 1
        If bulletActive(i) Then
            Framework_DrawRectangle(bulletX(i), bulletY(i), 5, 15, 255, 255, 0, 255)
        End If
    Next

    ' Draw enemies
    For i = 0 To MAX_ENEMIES - 1
        If enemyActive(i) Then
            Framework_DrawRectangle(enemyX(i), enemyY(i), 30, 30, 255, 50, 50, 255)
            Framework_DrawRectangle(enemyX(i) + 5, enemyY(i) + 5, 8, 8, 255, 200, 0, 255)
            Framework_DrawRectangle(enemyX(i) + 17, enemyY(i) + 5, 8, 8, 255, 200, 0, 255)
        End If
    Next

    ' Draw UI
    Framework_DrawText($"Score: {score}", 20, 20, 24, 255, 255, 255, 255)
    Framework_DrawText($"Lives: {lives}", SCREEN_WIDTH - 120, 20, 24, 255, 100, 100, 255)

    ' Draw game over
    If gameOver Then
        Framework_DrawRectangle(0, 0, SCREEN_WIDTH, SCREEN_HEIGHT, 0, 0, 0, 180)
        Framework_DrawText("GAME OVER", SCREEN_WIDTH / 2 - 100, SCREEN_HEIGHT / 2 - 40, 40, 255, 0, 0, 255)
        Framework_DrawText($"Final Score: {score}", SCREEN_WIDTH / 2 - 90, SCREEN_HEIGHT / 2 + 10, 24, 255, 255, 255, 255)
        Framework_DrawText("Press SPACE to restart", SCREEN_WIDTH / 2 - 130, SCREEN_HEIGHT / 2 + 50, 20, 200, 200, 200, 255)
    End If

    Framework_EndDrawing()
End Sub

Sub DrawStars()
    ' Simple pseudo-random stars based on fixed seed
    For i = 0 To 50
        Dim starX = (i * 137 + 17) Mod SCREEN_WIDTH
        Dim starY = (i * 251 + 31) Mod SCREEN_HEIGHT
        Dim brightness = 100 + (i * 23) Mod 155
        Framework_DrawRectangle(starX, starY, 2, 2, brightness, brightness, brightness, 255)
    Next
End Sub
