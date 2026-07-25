' SampleTextBatch2.vb
' Smoke scene for the raylib 5.5 text Batch 2 wrapper bindings. Exercises the GL-only paths
' the headless correctness suite can't cover: GetFontDefault (Font by value), DrawTextEx,
' DrawTextPro (rotated), DrawTextCodepoint, and MeasureTextExV (Font -> Vector2) used for
' centering — plus the PtrToStringAnsi string wrappers (TextToUpper, TextJoin) rendered via the
' pre-existing Framework_DrawText. Run: TestVbDLL.exe --text
' Auto-closes after a few seconds (so it terminates unattended); ESC also exits.

Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

Public Class SampleTextBatch2

    Private Const SCREEN_WIDTH As Integer = 800
    Private Const SCREEN_HEIGHT As Integer = 600
    Private Const MAX_FRAMES As Integer = 600   ' ~10s at 60fps, then self-close

    Private drawDelegate As DrawCallback
    Private _frames As Integer = 0
    Private _angle As Single = 0.0F
    Private _font As Font

    Public Sub Run()
        If Not Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "raylib Text Batch 2 — smoke") Then
            Console.WriteLine("Failed to initialize framework!")
            Return
        End If
        Framework_SetExitKey(Keys.ESCAPE)

        ' GetFontDefault requires a GL context (loads the default font atlas), so fetch it after Initialize.
        _font = Framework_GetFontDefault()

        drawDelegate = AddressOf OnDraw
        Framework_SetDrawCallback(drawDelegate)

        While Not Framework_ShouldClose() AndAlso _frames < MAX_FRAMES
            Framework_Update()
            _frames += 1
        End While

        Framework_Shutdown()
        Console.WriteLine($"Text smoke ran {_frames} frames with no missing-export error.")
    End Sub

    Private Sub OnDraw()
        _angle += 0.6F

        Framework_BeginDrawing()
        Framework_ClearBackground(24, 26, 38, 255)

        ' DrawTextEx — Font by value + spacing.
        Framework_DrawTextEx(_font, "DrawTextEx via GetFontDefault()", New Vector2(40, 70), 28, 2,
                             235, 235, 235, 255)

        ' MeasureTextExV — Font -> Vector2, used to horizontally center a line.
        Dim centered As String = "MeasureTextExV centers this line"
        Dim size As Vector2 = Framework_MeasureTextExV(_font, centered, 26, 2)
        Framework_DrawTextEx(_font, centered, New Vector2((SCREEN_WIDTH - size.x) / 2.0F, 150), 26, 2,
                             120, 200, 255, 255)

        ' DrawTextPro — rotated about an origin.
        Framework_DrawTextPro(_font, "DrawTextPro (rotated)", New Vector2(410, 320),
                              New Vector2(0, 0), _angle, 26, 2, 255, 180, 80, 255)

        ' DrawTextCodepoint — a single codepoint ('A' = 65).
        Framework_DrawTextCodepoint(_font, 65, New Vector2(60, 300), 96, 90, 220, 140, 255)

        ' String wrappers (IntPtr + PtrToStringAnsi) rendered via the pre-existing DrawText.
        Dim upper As String = TextToUpper("hello text batch 2")
        Framework_DrawText(upper, 40, 470, 22, 200, 200, 200, 255)
        Dim joined As String = TextJoin(New String() {"parity", "green", "38", "funcs"}, " / ")
        Framework_DrawText(joined, 40, 510, 22, 160, 210, 160, 255)

        Framework_EndDrawing()
    End Sub

End Class
