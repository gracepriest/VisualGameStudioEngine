Public Class Paddle
    'Dim paddleTexture As New TextureHandle("C:\Users\melvi\source\repos\VisualGameStudioEngine\x64\Release\net8.0\image\paddle")
    Public paddlePos As New Vector2(50.0F, 250.0F)
    Public paddleSpeed As Single = 25.0F
    Public paddleWidth As Integer = 20
    Public paddleHeight As Integer = 100
    Public dy As Single = 0.0F

    Public Sub Update(dt As Double)
        If (dy < 0) Then
            paddlePos.Y = Math.Max(0, paddlePos.Y + dy * CSng(dt))
        Else
            paddlePos.Y = Math.Min(720 - paddleHeight, paddlePos.Y + dy * CSng(dt))
        End If
    End Sub

    Public Sub Draw()
        'using a rectangle for the paddle
        Framework_DrawRectangle(CInt(paddlePos.X), CInt(paddlePos.Y), paddleWidth, paddleHeight, 255, 255, 255, 255)
    End Sub

End Class
