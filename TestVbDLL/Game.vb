

Public Class Game
    'a function Init the game window
    Public Function Init() As Integer
        Try
            REM initialize the game window
            If Not Framework_Initialize(WINDOW_WIDTH, WINDOW_HEIGHT, "Two Scenes (DLL-managed)") Then Return InitResult.INIT_NO_WINDOW

            ' Optional: make ESC close the window
            Framework_SetExitKey(256) ' 256 = KEY_ESCAPE
            Framework_SetFixedStep(1.0 / 60.0)

            If Not Framework_InitAudio() Then Return InitResult.INIT_NO_AUDIO

        Catch ex As Exception

        End Try
        Return InitResult.INIT_OK
    End Function

    Public Function Load() As Integer
        Try
            'Load game resources here
            LoadSounds()
            LoadFonts()
            'LoadImages()

            ' Create scenes
            'Dim _titleHandle As Integer = SetCurrentScene(New TitleScene)
            ' Start with title  
            'Framework_SceneChange(_titleHandle)
            ' Wire up the engine draw callback
            ChangeTo(New TitleScene)
            WireEngineDraw()
        Catch ex As Exception
            Console.WriteLine("Failed to load game resources: " & ex.Message)
            Return -1
        End Try
        Return 0
    End Function
    Public Sub Run()
        Init()
        Load()
        ' Normal loop
        While Not Framework_ShouldClose()
            Framework_Update()
        End While
    End Sub
    'a function to shutdown the game window
    Public Sub Shutdown()
        ' Cleanup
        ' Assuming _titleHandle is accessible here; if not, it should be stored as a class member
        ' Framework_DestroyScene(_titleHandle)
        UnloadFonts()
        'UnloadImages()
        UnloadSounds()
        Framework_Shutdown()
    End Sub
End Class
