Public Class Paddle
    'Dim paddleTexture As New TextureHandle("C:\Users\melvi\source\repos\VisualGameStudioEngine\x64\Release\net8.0\image\paddle")
    Public paddlePos As New Vector2(50.0F, 250.0F)
    Public paddleSpeed As Single = 25.0F
    Public paddleWidth As Single = 20
    Public paddleHeight As Single = 100
    Public dy As Single = 0.0F
    Public ownerID As Integer
    Public side As String 'left or right
    Public isAI As Boolean = False
    'texture for the paddle
    Public paddleTextureId As Integer
    Public paddleTexture As TextureHandle

    Public Sub setSide(s As String)
        side = s
        If side = "left" Then
            paddlePos.x = 50
        Else
            paddlePos.x = 1130.0F
        End If
    End Sub
    Public Sub setTexture(tex As TextureHandle)
        paddleTexture = tex
    End Sub
    Public Sub setTextureById(texId As Integer)
        paddleTextureId = texId
    End Sub

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
