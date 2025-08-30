Module Surface
    ' Surface texture
    Public paddle As TextureHandle
    Public ball As TextureHandle
    Public net As TextureHandle
    Public border As TextureHandle
    Public background As TextureHandle

    Public Function LoadImages() As Boolean
        Try
            ' Load game textures here
            paddle = New TextureHandle("resources/paddle.png")
            ball = New TextureHandle("resources/ball.png")
            net = New TextureHandle("resources/net.png")
            border = New TextureHandle("resources/border.png")
            background = New TextureHandle("resources/background.png")

        Catch ex As Exception
            Console.WriteLine("Failed to load texture: " & ex.Message)
            Return False
        End Try
        Return True
    End Function
    ' Unload images
    Public Sub UnloadImages()
        If paddle IsNot Nothing Then
            paddle.Dispose()
            paddle = Nothing
        End If
        If ball IsNot Nothing Then
            ball.Dispose()
            ball = Nothing
        End If
        If net IsNot Nothing Then
            net.Dispose()
            net = Nothing
        End If
        If border IsNot Nothing Then
            border.Dispose()
            border = Nothing
        End If
        If background IsNot Nothing Then
            background.Dispose()
            background = Nothing
        End If
    End Sub
End Module
