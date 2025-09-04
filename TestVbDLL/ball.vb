
Imports RL = TestVbDLL.Utiliy.Rectangle
Public Class Ball
    Public ballPos As New Vector2(WINDOW_WIDTH / 2, WINDOW_HEIGHT / 2)
    Public ballWidth As Single = 16
    Public ballHeight As Single = 16
    Public radius As Integer = 8
    Public dy As Single = 0.0F
    Public dx As Single = 0.0F

    Public Sub Update(dt As Double)
        ballPos.x += dx * CSng(dt)
        ballPos.y += dy * CSng(dt)
    End Sub

    Public Sub BallReSet()
        'reset ball to center
        ballPos.x = WINDOW_WIDTH / 2.0F
        ballPos.y = WINDOW_HEIGHT / 2.0F
        dy = 0.0F
        dx = 0.0F
    End Sub

    Public Sub Draw()
        'using a rectangle for the paddle
        Framework_DrawRectangle(CInt(ballPos.x), CInt(ballPos.y), ballWidth, ballHeight, 255, 255, 255, 255)

    End Sub
    Public Function BallCollieded(paddle1 As Paddle) As Boolean
        Dim gBall As RL
        With gBall
            .x = ballPos.x
            .y = ballPos.y
            .width = ballWidth
            .height = ballHeight
        End With

        Dim gPaddle As RL
        With gPaddle
            .x = paddle1.paddlePos.x
            .y = paddle1.paddlePos.y
            .width = paddle1.paddleWidth
            .height = paddle1.paddleHeight
        End With
        'check for collision between ball and paddle using framework collision function
        'Console.WriteLine("Ball Position: " & ballPos.X & ", " & ballPos.Y)
        'Console.WriteLine("Paddle Position: " & paddle1.paddlePos.X & ", " & paddle1.paddlePos.Y)
        'Console.WriteLine(Framework_CheckCollisionRecs(gBall, gPaddle))
        Return Framework_CheckCollisionRecs(gBall, gPaddle)
    End Function
    'aabb
    Public Function AABBCheck(paddle1 As Paddle) As Boolean
        If (ballPos.x < paddle1.paddlePos.x + paddle1.paddleWidth AndAlso
            ballPos.x + ballWidth > paddle1.paddlePos.x AndAlso
            ballPos.y < paddle1.paddlePos.y + paddle1.paddleHeight AndAlso
            ballPos.y + ballHeight > paddle1.paddlePos.y) Then
            Return True
        End If
        Return False
    End Function
End Class
