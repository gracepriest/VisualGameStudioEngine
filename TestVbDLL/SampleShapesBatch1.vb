' SampleShapesBatch1.vb
' Smoke scene for the raylib 5.5 shapes Batch 1 wrapper bindings. Exercises the four
' representative marshaling shapes the design spec §4.4 calls out — DrawRectangleRec
' (Rectangle by value), DrawRectanglePro (Rectangle + Vector2), DrawLineStrip (Vector2()
' array path), DrawRectangleGradientEx (4 decomposed Colors) — plus the pre-existing
' int-based Framework_DrawRectangle to prove int/float coexistence. Run: TestVbDLL.exe --shapes
' Auto-closes after a few seconds (so it terminates unattended); ESC also exits.

Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

Public Class SampleShapesBatch1

    Private Const SCREEN_WIDTH As Integer = 800
    Private Const SCREEN_HEIGHT As Integer = 600
    Private Const MAX_FRAMES As Integer = 600   ' ~10s at 60fps, then self-close

    Private drawDelegate As DrawCallback
    Private _frames As Integer = 0
    Private _angle As Single = 0.0F
    Private ReadOnly _strip As Vector2()

    Public Sub New()
        ' Zigzag polyline for the DrawLineStrip (Vector2[]) array-marshaling path.
        _strip = New Vector2() {
            New Vector2(430, 380), New Vector2(480, 300), New Vector2(530, 380),
            New Vector2(580, 300), New Vector2(630, 380), New Vector2(680, 300)
        }
    End Sub

    Public Sub Run()
        If Not Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "raylib Shapes Batch 1 — smoke") Then
            Console.WriteLine("Failed to initialize framework!")
            Return
        End If
        Framework_SetExitKey(Keys.ESCAPE)

        drawDelegate = AddressOf OnDraw
        Framework_SetDrawCallback(drawDelegate)

        While Not Framework_ShouldClose() AndAlso _frames < MAX_FRAMES
            Framework_Update()
            _frames += 1
        End While

        Framework_Shutdown()
        Console.WriteLine($"Shapes smoke ran {_frames} frames with no missing-export error.")
    End Sub

    Private Sub OnDraw()
        _angle += 1.0F

        Framework_BeginDrawing()
        Framework_ClearBackground(28, 28, 40, 255)

        ' A9: DrawRectangleRec — Rectangle by value, Color decomposed to r,g,b,a.
        Dim rec As Rectangle = New Rectangle With {.x = 60, .y = 90, .width = 150, .height = 100}
        Framework_DrawRectangleRec(rec, 90, 180, 255, 255)

        ' A10: DrawRectanglePro — Rectangle + Vector2 origin + rotation.
        Dim proRec As Rectangle = New Rectangle With {.x = 320, .y = 150, .width = 120, .height = 80}
        Dim origin As Vector2 = New Vector2(60, 40)
        Framework_DrawRectanglePro(proRec, origin, _angle, 255, 140, 60, 255)

        ' A13: DrawRectangleGradientEx — four decomposed Colors (TL, BL, TR, BR).
        Dim gradRec As Rectangle = New Rectangle With {.x = 60, .y = 320, .width = 300, .height = 180}
        Framework_DrawRectangleGradientEx(gradRec,
            255, 0, 0, 255,      ' top-left    red
            0, 0, 255, 255,      ' bottom-left blue
            0, 255, 0, 255,      ' top-right   green
            255, 255, 0, 255)    ' bottom-right yellow

        ' B1: DrawLineStrip — Vector2() array + count (the array-marshaling path).
        Framework_DrawLineStrip(_strip, _strip.Length, 255, 255, 255, 255)

        ' Pre-existing int-based DrawRectangle — proves int/float coexistence.
        Framework_DrawRectangle(560, 470, 180, 60, 120, 220, 120, 255)

        Framework_DrawText("raylib shapes Batch 1 smoke — Rec / Pro / GradientEx / LineStrip / (int)Rectangle",
                           14, 14, 18, 230, 230, 230, 255)

        Framework_EndDrawing()
    End Sub

End Class
