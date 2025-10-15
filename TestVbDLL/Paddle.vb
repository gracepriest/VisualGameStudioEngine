Public Class Paddle
    Public paddlePos As New Vector2(50.0F, 250.0F)
    Public paddleSpeed As Single = 35.0F
    Public paddleWidth As Single = 20
    Public paddleHeight As Single = 100
    Public dy As Single = 0.0F
    Public ownerID As Integer
    Public side As String 'left or right
    Public isAI As Boolean = False
    'texture for the paddle
    Public paddleTextureId As Integer
    Dim atlasTexture As New TextureHandle("images/blocks.png")
    Dim atlas As New SpriteAtlas(atlasTexture)
    Dim _sprite As Sprite
    Dim ba As Ball
    'rgba color for the paddle if no texture is set
    Public paddleColor As (r As Byte, g As Byte, b As Byte, a As Byte) = (255, 255, 255, 255)

    Public Sub New()
        atlas.Add("block", New Rectangle(0, 0, 32, 16))
    End Sub

    Public Sub getBall(b As Ball)
        ba = b
    End Sub
    Public Sub setSide(s As String)
        side = s
        If side = "left" Then
            paddlePos.x = 50.0F
        Else
            paddlePos.x = 1130.0F
        End If

    End Sub
    'Public Sub setTexture(tex As TextureHandle)
    '    paddleTextureId = tex
    'End Sub
    Public Sub setTextureById(texId As Integer)
        paddleTextureId = texId
    End Sub
    'set the color of the paddle
    Public Sub setColor(r As Byte, g As Byte, b As Byte, a As Byte)
        paddleColor = (r, g, b, a)
    End Sub
    'set color using Color structure
    Public Sub setColor(c As Color)
        paddleColor = (c.r, c.g, c.b, c.a)
    End Sub

    Public Sub Update(dt As Double)
        If Not isAI Then
            If (dy < 0) Then
                paddlePos.y = Math.Max(0, paddlePos.y + dy * CSng(dt))
            Else
                paddlePos.y = Math.Min(720 - paddleHeight, paddlePos.y + dy * CSng(dt))
            End If
            If Not _sprite Is Nothing Then
                _sprite.position = paddlePos
            End If
        Else
            If ba.dx > 0 Then 'ball moving right
                If ba.ballPos.x > 500 Then 'ball is on the right side of the screen
                    If ba.ballPos.y + ba.ballHeight < paddlePos.y + paddleHeight And ba.ballPos.y + ba.ballHeight / 2 <> paddlePos.y + paddleHeight / 2 Then
                        'move up
                        paddlePos.y = Math.Max(0, paddlePos.y - (paddleSpeed + 150) * CSng(dt))
                    ElseIf ba.ballPos.y + ba.ballHeight > paddlePos.y + paddleHeight Then
                        'move down
                        paddlePos.y = Math.Min(720 - paddleHeight, paddlePos.y + (paddleSpeed + 150) * CSng(dt))
                    End If
                    If Not _sprite Is Nothing Then
                        _sprite.position = paddlePos
                    End If
                End If
            End If
        End If

    End Sub
    Public Sub ChangeSprite(tex As String)
        _sprite = New Sprite(atlas, tex, New Vector2(paddlePos.x, paddlePos.y))
        _sprite.scale = 2.0F
        _sprite.rotation = 90.0F
    End Sub
    Public Sub Draw()
        If _sprite Is Nothing Then
            'using a rectangle for the paddle
            Framework_DrawRectangle(CInt(paddlePos.x), CInt(paddlePos.y), paddleWidth, paddleHeight, paddleColor.r, paddleColor.g, paddleColor.b, paddleColor.a)
        Else
            _sprite.Draw()
        End If
    End Sub
    Public Sub reset()
        paddlePos.y = 250.0F
    End Sub
End Class
