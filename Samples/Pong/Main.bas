' Pong - Classic Arcade Game
' A simple two-player Pong game demonstrating BasicLang game development

' Constants
Const SCREEN_WIDTH = 800
Const SCREEN_HEIGHT = 600
Const PADDLE_WIDTH = 20
Const PADDLE_HEIGHT = 100
Const BALL_SIZE = 15
Const PADDLE_SPEED = 400.0
Const BALL_SPEED = 350.0

' Game state
Dim player1Y As Single = 250
Dim player2Y As Single = 250
Dim ballX As Single = 400
Dim ballY As Single = 300
Dim ballVX As Single = BALL_SPEED
Dim ballVY As Single = BALL_SPEED / 2
Dim score1 As Integer = 0
Dim score2 As Integer = 0
Dim gameOver As Boolean = False

Sub Main()
    Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "Pong")
    Framework_SetFixedStep(1.0 / 60.0)

    While Not Framework_ShouldClose()
        Update()
        Draw()
    End While

    Framework_Shutdown()
End Sub

Sub Update()
    Dim dt = Framework_GetDeltaTime()

    If gameOver Then
        If Framework_IsKeyPressed(KEY_SPACE) Then
            ResetGame()
        End If
        Return
    End If

    ' Player 1 controls (W/S keys)
    If Framework_IsKeyDown(KEY_W) Then
        player1Y -= PADDLE_SPEED * dt
    End If
    If Framework_IsKeyDown(KEY_S) Then
        player1Y += PADDLE_SPEED * dt
    End If

    ' Player 2 controls (Up/Down arrows)
    If Framework_IsKeyDown(KEY_UP) Then
        player2Y -= PADDLE_SPEED * dt
    End If
    If Framework_IsKeyDown(KEY_DOWN) Then
        player2Y += PADDLE_SPEED * dt
    End If

    ' Clamp paddle positions
    player1Y = Clamp(player1Y, 0, SCREEN_HEIGHT - PADDLE_HEIGHT)
    player2Y = Clamp(player2Y, 0, SCREEN_HEIGHT - PADDLE_HEIGHT)

    ' Update ball position
    ballX += ballVX * dt
    ballY += ballVY * dt

    ' Ball collision with top/bottom walls
    If ballY <= 0 Or ballY >= SCREEN_HEIGHT - BALL_SIZE Then
        ballVY = -ballVY
        ballY = Clamp(ballY, 0, SCREEN_HEIGHT - BALL_SIZE)
    End If

    ' Ball collision with paddles
    ' Player 1 paddle (left side)
    If ballX <= PADDLE_WIDTH + 30 And ballX >= 30 Then
        If ballY + BALL_SIZE >= player1Y And ballY <= player1Y + PADDLE_HEIGHT Then
            ballVX = Abs(ballVX)  ' Bounce right
            ' Add spin based on where ball hits paddle
            Dim hitPos = (ballY - player1Y) / PADDLE_HEIGHT
            ballVY = (hitPos - 0.5) * BALL_SPEED
        End If
    End If

    ' Player 2 paddle (right side)
    If ballX + BALL_SIZE >= SCREEN_WIDTH - PADDLE_WIDTH - 30 And ballX <= SCREEN_WIDTH - 30 Then
        If ballY + BALL_SIZE >= player2Y And ballY <= player2Y + PADDLE_HEIGHT Then
            ballVX = -Abs(ballVX)  ' Bounce left
            Dim hitPos = (ballY - player2Y) / PADDLE_HEIGHT
            ballVY = (hitPos - 0.5) * BALL_SPEED
        End If
    End If

    ' Score points
    If ballX < 0 Then
        score2 += 1
        ResetBall()
    ElseIf ballX > SCREEN_WIDTH Then
        score1 += 1
        ResetBall()
    End If

    ' Check for game over
    If score1 >= 10 Or score2 >= 10 Then
        gameOver = True
    End If
End Sub

Sub ResetBall()
    ballX = SCREEN_WIDTH / 2
    ballY = SCREEN_HEIGHT / 2
    ballVX = BALL_SPEED * If(ballVX > 0, -1, 1)
    ballVY = (Rnd() - 0.5) * BALL_SPEED
End Sub

Sub ResetGame()
    score1 = 0
    score2 = 0
    gameOver = False
    ResetBall()
End Sub

Function Clamp(value As Single, min As Single, max As Single) As Single
    If value < min Then Return min
    If value > max Then Return max
    Return value
End Function

Sub Draw()
    Framework_BeginDrawing()
    Framework_ClearBackground(20, 20, 30, 255)

    ' Draw center line
    For i = 0 To SCREEN_HEIGHT Step 30
        Framework_DrawRectangle(SCREEN_WIDTH / 2 - 2, i, 4, 15, 100, 100, 100, 255)
    Next

    ' Draw paddles
    Framework_DrawRectangle(30, player1Y, PADDLE_WIDTH, PADDLE_HEIGHT, 255, 255, 255, 255)
    Framework_DrawRectangle(SCREEN_WIDTH - 30 - PADDLE_WIDTH, player2Y, PADDLE_WIDTH, PADDLE_HEIGHT, 255, 255, 255, 255)

    ' Draw ball
    Framework_DrawRectangle(ballX, ballY, BALL_SIZE, BALL_SIZE, 255, 200, 0, 255)

    ' Draw scores
    Framework_DrawText($"{score1}", SCREEN_WIDTH / 4, 50, 60, 255, 255, 255, 255)
    Framework_DrawText($"{score2}", 3 * SCREEN_WIDTH / 4, 50, 60, 255, 255, 255, 255)

    ' Draw game over message
    If gameOver Then
        Dim winner = If(score1 >= 10, "Player 1", "Player 2")
        Framework_DrawText($"{winner} Wins!", SCREEN_WIDTH / 2 - 120, SCREEN_HEIGHT / 2 - 40, 40, 255, 255, 0, 255)
        Framework_DrawText("Press SPACE to restart", SCREEN_WIDTH / 2 - 150, SCREEN_HEIGHT / 2 + 20, 24, 200, 200, 200, 255)
    End If

    ' Draw instructions
    Framework_DrawText("P1: W/S  |  P2: Up/Down", 10, SCREEN_HEIGHT - 30, 18, 150, 150, 150, 255)

    Framework_EndDrawing()
End Sub
