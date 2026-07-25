' SampleTextures3d.vb
' Windowed smoke scene for the raylib 5.5 textures Batch 3d wrapper bindings. These 8 functions
' are GL-dependent (they need a live OpenGL context), so the headless NUnit correctness suite
' CANNOT cover them — this scene is their verification. It exercises, with mechanical self-asserts:
'   1. Framework_LoadTextureFromImage  (Image -> GPU Texture2D)
'   2. Framework_LoadImageFromTexture  (GPU Texture2D -> CPU Image readback)
'   3. Framework_ImageText             (text -> CPU Image, default font)
'   4. Framework_ImageTextEx           (text -> CPU Image, Font by value + spacing)
'   5. Framework_ImageDrawText         (ByRef dst Image, draw text onto an image)
'   6. Framework_ImageDrawTextEx       (ByRef dst Image, Font by value)
'   7. Framework_LoadTextureCubemap    (Image -> cubemap; bind-only for a 2D engine)
'   8. Framework_LoadImageFromScreen   (framebuffer -> CPU Image; only after a rendered frame)
' A Font is obtained via Framework_GetFontDefault() (default font atlas — needs GL, so post-Init).
' All one-shot GL uploads/readbacks run after Framework_Initialize (context live) and the screen
' capture runs mid-loop after >=1 Framework_Update() has rendered a frame, before Framework_Shutdown.
' Prints "[textures3d] PASS" or "[textures3d] FAIL: <which>" and exports textures3d_capture.png.
' Run: TestVbDLL.exe --textures3d   (auto-closes after a few seconds; ESC also exits.)

Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

Public Class SampleTextures3d

    Private Const SCREEN_WIDTH As Integer = 800
    Private Const SCREEN_HEIGHT As Integer = 600
    Private Const MAX_FRAMES As Integer = 600      ' ~10s at 60fps, then self-close
    Private Const CAPTURE_FRAME As Integer = 3     ' grab the framebuffer after a few rendered frames

    Private drawDelegate As DrawCallback
    Private _frames As Integer = 0

    ' Mechanical pass/fail accounting for the GL fns the headless suite can't reach.
    Private _allPass As Boolean = True
    Private _failReason As String = ""

    ' GL resources (freed before Shutdown).
    Private _font As Font
    Private _srcImg As Image           ' checkerboard source
    Private _readback As Image         ' LoadImageFromTexture result
    Private _canvas As Image           ' ImageDrawText / ImageDrawTextEx target (ByRef)
    Private _imgT As Image             ' ImageText result
    Private _imgTx As Image            ' ImageTextEx result
    Private _shot As Image             ' LoadImageFromScreen result
    Private _srcTex As Texture2D       ' LoadTextureFromImage(_srcImg)
    Private _canvasTex As Texture2D    ' LoadTextureFromImage(_canvas) — the composed text image, drawn
    Private _cubemap As TextureCubemap
    Private _shotCaptured As Boolean = False

    Public Sub Run()
        If Not Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "raylib Textures Batch 3d — GPU + font-image smoke") Then
            Console.WriteLine("Failed to initialize framework!")
            Return
        End If
        Framework_SetExitKey(Keys.ESCAPE)

        ' GL context is live now — do every upload/readback here (default font atlas also needs GL).
        _font = Framework_GetFontDefault()

        ' --- 1. Source Image (asset-free checkerboard) -> GPU texture round-trip ---
        _srcImg = Framework_GenImageChecked(128, 128, 16, 16, 40, 40, 60, 255, 200, 200, 220, 255)

        _srcTex = Framework_LoadTextureFromImage(_srcImg)
        Check(_srcTex.id <> 0UI, "LoadTextureFromImage id==0")
        Check(_srcTex.width = _srcImg.width, "LoadTextureFromImage width mismatch")
        Check(_srcTex.height = _srcImg.height, "LoadTextureFromImage height mismatch")

        ' --- 2. GPU texture -> CPU image readback ---
        _readback = Framework_LoadImageFromTexture(_srcTex)
        Check(_readback.data <> IntPtr.Zero, "LoadImageFromTexture data==null")
        Check(_readback.width = _srcTex.width, "LoadImageFromTexture width mismatch")
        Check(_readback.height = _srcTex.height, "LoadImageFromTexture height mismatch")

        ' --- 3. ImageText: text -> CPU image (default font, red) ---
        _imgT = Framework_ImageText("Hi", 20, 255, 0, 0, 255)
        Check(_imgT.data <> IntPtr.Zero, "ImageText data==null")
        Check(_imgT.width > 0 AndAlso _imgT.height > 0, "ImageText zero dims")

        ' --- 4. ImageTextEx: Font by value + spacing (white) ---
        _imgTx = Framework_ImageTextEx(_font, "Hi", 20, 1, 255, 255, 255, 255)
        Check(_imgTx.data <> IntPtr.Zero, "ImageTextEx data==null")
        Check(_imgTx.width > 0 AndAlso _imgTx.height > 0, "ImageTextEx zero dims")

        ' --- 5 & 6. ImageDrawText / ImageDrawTextEx: mutate a blank canvas Image by ref ---
        _canvas = Framework_GenImageColor(220, 70, 12, 14, 22, 255)   ' dark blank canvas
        Framework_ImageDrawText(_canvas, "ImageDrawText", 4, 4, 20, 255, 255, 255, 255)
        Check(_canvas.data <> IntPtr.Zero, "ImageDrawText nulled canvas")
        Check(_canvas.width = 220 AndAlso _canvas.height = 70, "ImageDrawText resized canvas")
        Framework_ImageDrawTextEx(_canvas, _font, "DrawTextEx", New Vector2(4, 34), 20, 1, 120, 220, 120, 255)
        Check(_canvas.data <> IntPtr.Zero, "ImageDrawTextEx nulled canvas")

        ' Upload the composed text canvas to a texture so the visual run shows the font-image result.
        _canvasTex = Framework_LoadTextureFromImage(_canvas)
        Check(_canvasTex.id <> 0UI, "LoadTextureFromImage(canvas) id==0")

        ' --- 7. LoadTextureCubemap: bind-only for a 2D engine (no cubemap asset). Log id, don't fail. ---
        Dim cubeSrc As Image = Framework_GenImageColor(64, 384, 80, 80, 120, 255)  ' vertical strip, auto-detect layout
        _cubemap = Framework_LoadTextureCubemap(cubeSrc, 0)
        Console.WriteLine($"[textures3d] LoadTextureCubemap id={_cubemap.id} ({_cubemap.width}x{_cubemap.height}) — bind-only, id==0 is acceptable for a 2D engine")
        Framework_UnloadImage(cubeSrc)

        ' Draw callback renders the round-tripped texture + the composed font-image texture each frame.
        drawDelegate = AddressOf OnDraw
        Framework_SetDrawCallback(drawDelegate)

        ' --- 8. LoadImageFromScreen must run AFTER a rendered frame, while GL is still live ---
        While Not Framework_ShouldClose() AndAlso _frames < MAX_FRAMES
            Framework_Update()
            _frames += 1
            If _frames = CAPTURE_FRAME AndAlso Not _shotCaptured Then
                _shot = Framework_LoadImageFromScreen()
                _shotCaptured = True
                Check(_shot.data <> IntPtr.Zero, "LoadImageFromScreen data==null")
                Check(_shot.width = SCREEN_WIDTH AndAlso _shot.height = SCREEN_HEIGHT,
                      $"LoadImageFromScreen dims {_shot.width}x{_shot.height} != {SCREEN_WIDTH}x{SCREEN_HEIGHT}")
                ' Export for the user's visual eyeball (raylib 5.5 ExportImage — Batch 3b).
                Framework_ExportImage(_shot, "textures3d_capture.png")
            End If
        End While

        ' Free every GPU texture + CPU image BEFORE Shutdown (texture unload needs the GL context).
        Framework_UnloadTexture(_srcTex)
        Framework_UnloadTexture(_canvasTex)
        If _cubemap.id <> 0UI Then
            Dim ct As Texture2D
            ct.id = _cubemap.id : ct.width = _cubemap.width : ct.height = _cubemap.height
            ct.mipmaps = _cubemap.mipmaps : ct.format = _cubemap.format
            Framework_UnloadTexture(ct)
        End If
        Framework_UnloadImage(_srcImg)
        Framework_UnloadImage(_readback)
        Framework_UnloadImage(_canvas)
        Framework_UnloadImage(_imgT)
        Framework_UnloadImage(_imgTx)
        If _shotCaptured Then Framework_UnloadImage(_shot)

        Framework_Shutdown()

        If Not _shotCaptured Then Check(False, "LoadImageFromScreen never ran (window closed before frame " & CAPTURE_FRAME & ")")

        If _allPass Then
            Console.WriteLine("[textures3d] PASS")
        Else
            Console.WriteLine("[textures3d] FAIL:" & _failReason)
        End If
        Console.WriteLine($"[textures3d] ran {_frames} frames; capture -> textures3d_capture.png")
    End Sub

    Private Sub OnDraw()
        Framework_BeginDrawing()
        Framework_ClearBackground(24, 26, 38, 255)

        ' The GPU round-tripped checkerboard (LoadTextureFromImage).
        Framework_DrawTexture(_srcTex, 60, 120, 255, 255, 255, 255)

        ' The composed font-image texture (ImageText/ImageDrawText/ImageDrawTextEx -> texture).
        Framework_DrawTexture(_canvasTex, 320, 150, 255, 255, 255, 255)

        Framework_DrawText("raylib Textures Batch 3d smoke — LoadTextureFromImage / LoadImageFromTexture / ImageText(Ex) / ImageDrawText(Ex) / Cubemap / LoadImageFromScreen",
                           14, 14, 16, 220, 220, 220, 255)
        Framework_DrawText("left: GPU checkerboard round-trip   right: composed font-image texture",
                           14, 40, 16, 150, 190, 240, 255)

        Framework_EndDrawing()
    End Sub

    ' Accumulate mechanical pass/fail; records the first failing label into the summary line.
    Private Sub Check(condition As Boolean, label As String)
        If Not condition Then
            _allPass = False
            _failReason &= " " & label & ";"
        End If
    End Sub

End Class
