

Module Program
    ' Keep every delegate alive so the GC can’t collect them.
    ' Title scene delegates
    Private _tEnter As SceneVoidFn
    Private _tExit As SceneVoidFn
    Private _tResume As SceneVoidFn
    Private _tFixed As SceneUpdateFixedFn
    Private _tFrame As SceneUpdateFrameFn
    Private _tDraw As SceneVoidFn
    Private _titleHandle As Integer

    ' Game scene delegates
    Private _gEnter As SceneVoidFn
    Private _gExit As SceneVoidFn
    Private _gResume As SceneVoidFn
    Private _gFixed As SceneUpdateFixedFn
    Private _gFrame As SceneUpdateFrameFn
    Private _gDraw As SceneVoidFn
    Private _gameHandle As Integer

    ' Engine draw callback -> just ticks the DLL scene system
    Private Sub EngineDraw()
        Framework_SceneTick()
    End Sub

    Sub Main()
        If Not Framework_Initialize(800, 450, "Two Scenes (DLL-managed)") Then Return

        ' Optional: make ESC close the window
        Framework_SetExitKey(256) ' 256 = KEY_ESCAPE
        Framework_SetFixedStep(1.0 / 60.0)

        ' -------------------------
        ' Title scene callbacks
        ' -------------------------
        _tEnter = Sub()
                      ' e.g., load menu assets later
                  End Sub
        _tExit = Sub()
                 End Sub
        _tResume = Sub()
                   End Sub
        _tFixed = Sub(dt As Double)
                  End Sub
        _tFrame = Sub(dt As Single)
                      ' ENTER -> go to Game
                      If Framework_IsKeyPressed(257) Then ' KEY_ENTER
                          Framework_SceneChange(_gameHandle)
                      End If
                  End Sub
        _tDraw = Sub()
                     Framework_ClearBackground(20, 24, 28, 255)
                     Framework_DrawText("TITLE SCENE", 40, 40, 28, 255, 255, 255, 255)
                     Framework_DrawText("Press ENTER to start", 40, 80, 20, 200, 220, 255, 255)
                     Framework_DrawText("ESC closes app (engine exit key)", 40, 110, 16, 200, 200, 200, 255)
                     Framework_DrawFPS(700, 10)
                 End Sub

        Dim titleCbs As New SceneCallbacks With {
            .onEnter = _tEnter, .onExit = _tExit, .onResume = _tResume,
            .onUpdateFixed = _tFixed, .onUpdateFrame = _tFrame, .onDraw = _tDraw
        }
        _titleHandle = Framework_CreateScriptScene(titleCbs)

        ' -------------------------
        ' Game scene callbacks
        ' -------------------------
        Dim x As Single = 100, y As Single = 150, vx As Single = 120.0F, vy As Single = 0.0F, g As Single = 800.0F

        _gEnter = Sub()
                      ' Set a faster physics rate if you like
                      Framework_SetFixedStep(1.0 / 120.0)
                  End Sub
        _gExit = Sub()
                 End Sub
        _gResume = Sub()
                   End Sub

        _gFixed = Sub(dt As Double)
                      ' Simple physics
                      vy += g * CSng(dt)
                      x += vx * CSng(dt)
                      y += vy * CSng(dt)

                      If x < 0 Then x = 0 : vx = Math.Abs(vx)
                      If x > 780 Then x = 780 : vx = -Math.Abs(vx)
                      If y > 430 Then y = 430 : vy = -Math.Abs(vy) * 0.6F
                  End Sub

        _gFrame = Sub(dt As Single)
                      ' BACKSPACE -> back to Title
                      If Framework_IsKeyPressed(259) Then ' KEY_BACKSPACE
                          Framework_SceneChange(_titleHandle)
                      End If
                  End Sub

        _gDraw = Sub()
                     Framework_ClearBackground(10, 10, 20, 255)
                     Framework_DrawText("GAME SCENE (Backspace to Title)", 20, 14, 20, 255, 255, 255, 255)
                     Framework_DrawRectangle(CInt(x), CInt(y), 20, 20, 120, 220, 255, 255)
                     Framework_DrawFPS(700, 10)
                 End Sub

        Dim gameCbs As New SceneCallbacks With {
            .onEnter = _gEnter, .onExit = _gExit, .onResume = _gResume,
            .onUpdateFixed = _gFixed, .onUpdateFrame = _gFrame, .onDraw = _gDraw
        }
        _gameHandle = Framework_CreateScriptScene(gameCbs)

        ' Start at Title
        Framework_SceneChange(_titleHandle)

        ' Wire the engine’s draw callback to tick the DLL scene system
        Dim drawDel As New DrawCallback(AddressOf EngineDraw)
        Framework_SetDrawCallback(drawDel)

        ' Normal loop
        While Not Framework_ShouldClose()
            Framework_Update()
        End While

        ' Cleanup
        Framework_DestroyScene(_gameHandle)
        Framework_DestroyScene(_titleHandle)
        Framework_Shutdown()
    End Sub
End Module
