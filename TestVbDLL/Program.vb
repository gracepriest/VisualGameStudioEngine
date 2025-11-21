

Module Program
    ' Global state for this tiny game
    Private _playerEntity As Integer
    Private _playerSpeed As Single = 250.0F
    Private _playerTextureHandle As Integer
    Private _drawCallback As DrawCallback

    Sub Main()
        ' Initialize window
        If Not Framework_Initialize(800, 600, "ECS Sprite Move Demo") Then
            Return
        End If

        Framework_SetTargetFPS(60)

        ' --- Load sprite as a texture handle ---
        ' Make sure this file exists in your output folder (e.g. bin\Debug\net8.0\assets\player.png)
        _playerTextureHandle = Framework_AcquireTextureH("ninja.jpg")

        ' --- Create one ECS entity with Transform + Sprite ---
        Framework_Ecs_ClearAll()

        _playerEntity = Framework_Ecs_CreateEntity()

        ' Start in the center of the screen
        Framework_Ecs_AddTransform2D(
            _playerEntity,
            400.0F, 300.0F,   ' x, y
            0.0F,             ' rotation in degrees
            1.0F, 1.0F        ' scale x, y
        )

        ' Assume sprite is 64x64. Change this if your image is different.
        Framework_Ecs_AddSprite2D(
            _playerEntity,
            _playerTextureHandle,
            0.0F, 0.0F,       ' srcX, srcY
            128.0F, 128.0F,     ' srcW, srcH
            255, 255, 255, 255,
            0                 ' layer
        )

        ' --- Set draw callback so Framework_Update() can call us ---
        _drawCallback = AddressOf DrawFrame
        Framework_SetDrawCallback(_drawCallback)

        ' --- Main loop ---
        While Not Framework_ShouldClose()
            UpdateFrame()
            Framework_Update()
        End While

        ' Cleanup
        Framework_Ecs_ClearAll()
        Framework_ReleaseTextureH(_playerTextureHandle)
        Framework_Shutdown()
    End Sub

    ' Handle input and move the player entity
    Private Sub UpdateFrame()
        Dim dt As Single = Framework_GetFrameTime()
        Dim move As Single = _playerSpeed * dt

        ' Read current position from ECS
        Dim pos As Vector2 = Framework_Ecs_GetTransformPosition(_playerEntity)

        ' Arrow keys / WASD movement
        If Framework_IsKeyDown(Keys.LEFT) OrElse Framework_IsKeyDown(Keys.A) Then
            pos.x -= move
        End If
        If Framework_IsKeyDown(Keys.RIGHT) OrElse Framework_IsKeyDown(Keys.D) Then
            pos.x += move
        End If
        If Framework_IsKeyDown(Keys.UP) OrElse Framework_IsKeyDown(Keys.W) Then
            pos.y -= move
        End If
        If Framework_IsKeyDown(Keys.DOWN) OrElse Framework_IsKeyDown(Keys.S) Then
            pos.y += move
        End If

        ' Write updated position back into ECS
        Framework_Ecs_SetTransformPosition(_playerEntity, pos.x, pos.y)
    End Sub

    ' Draw callback: clears screen and lets ECS render all Sprite2D entities
    Private Sub DrawFrame()
        Framework_ClearBackground(30, 30, 40, 255)

        ' Draw all ECS Sprite2D components (sorted by layer)
        Framework_Ecs_DrawSprites()

        ' Simple debug info
        Framework_DrawFPS(10, 10)
    End Sub
End Module
