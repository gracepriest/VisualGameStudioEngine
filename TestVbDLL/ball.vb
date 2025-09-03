Public Class ball
    Public ballPos As New Vector2(50.0F, 250.0F)
    Public ballWidth As Integer = 16
    Public ballHeight As Integer = 16
    Public radius As Integer = 8
    Public dy As Single = 0.0F
    Public dx As Single = 0.0F

    Public Sub Update(dt As Double)
        ballPos.X += dx * CSng(dt)
        ballPos.Y += dy * CSng(dt)
        'limit ball to screen
        If ballPos.Y < 0 Then
            ballPos.Y = 0
            dy = -dy
        ElseIf ballPos.Y + ballHeight > WINDOW_HEIGHT Then
            ballPos.Y = WINDOW_HEIGHT - ballHeight
        End If
    End Sub

    Public Sub BallReSet()
        'reset ball to center
        ballPos.X = WINDOW_WIDTH / 2.0F
        ballPos.Y = WINDOW_HEIGHT / 2.0F
        dy = 0.0F
        dx = 0.0F
    End Sub

    Public Sub Draw()
        'using a rectangle for the paddle
        Framework_DrawRectangle(CInt(ballPos.X), CInt(ballPos.Y), ballWidth, ballHeight, 255, 255, 255, 255)

    End Sub
    Public Function BallCollieded(paddle As Paddle) As Boolean
        'check for collision between ball and paddle using framework collision function
        Return Framework_CheckCollisionRecs(New Rectangle(CInt(ballPos.X), CInt(ballPos.Y), ballWidth, ballHeight), New Rectangle(CInt(paddle.paddlePos.X), CInt(paddle.paddlePos.Y), paddle.paddleWidth, paddle.paddleHeight))
    End Function
    Public Sub ReflectFromPaddle(paddle As Paddle)
        'reverse x direction
        dx = -dx
        'add some y direction based on where the ball hit the paddle
        Dim paddleCenter As Single = paddle.paddlePos.Y + paddle.paddleHeight / 2.0F
        Dim ballCenter As Single = ballPos.Y + ballHeight / 2.0F
        Dim offset As Single = ballCenter - paddleCenter
        dy = offset / (paddle.paddleHeight / 2.0F) * 1.0F
        If dx > 0 Then
            ballPos.X = paddle.paddlePos.X + paddle.paddleWidth
        Else
            ballPos.X = paddle.paddlePos.X - ballWidth
        End If
    End Sub
End Class
