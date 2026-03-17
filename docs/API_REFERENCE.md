# VisualGameStudioEngine API Reference

A comprehensive 2D game engine framework built on Raylib with VB.NET P/Invoke bindings.

All functions are exported from `VisualGameStudioEngine.dll` and declared in `RaylibWrapper/RaylibWrapper.vb` via P/Invoke.

## Table of Contents

1. [Core Framework](#core-framework)
2. [Timing & Time Control](#timing--time-control)
3. [Drawing](#drawing)
4. [Input — Keyboard](#input--keyboard)
5. [Input — Mouse](#input--mouse)
6. [Collision Checks](#collision-checks)
7. [Textures & Images](#textures--images)
8. [Handle-Based Texture API](#handle-based-texture-api)
9. [Render Textures & Off-Screen Rendering](#render-textures--off-screen-rendering)
10. [Camera 2D — Basic](#camera-2d--basic)
11. [Camera 2D — Enhanced](#camera-2d--enhanced)
12. [Fonts & Text](#fonts--text)
13. [Audio — Basic](#audio--basic)
14. [Audio Manager — Advanced](#audio-manager--advanced)
15. [Audio Effects & Filters](#audio-effects--filters)
16. [Shaders](#shaders)
17. [Scene System — Basic](#scene-system--basic)
18. [Scene Manager — Transitions & Loading](#scene-manager--transitions--loading)
19. [ECS — Entity Management](#ecs--entity-management)
20. [ECS — Name Component](#ecs--name-component)
21. [ECS — Tag Component](#ecs--tag-component)
22. [ECS — Enabled Component](#ecs--enabled-component)
23. [ECS — Hierarchy Component](#ecs--hierarchy-component)
24. [ECS — Transform2D Component](#ecs--transform2d-component)
25. [ECS — Velocity2D Component](#ecs--velocity2d-component)
26. [ECS — BoxCollider2D Component](#ecs--boxcollider2d-component)
27. [ECS — Sprite2D Component](#ecs--sprite2d-component)
28. [ECS — Built-in Systems](#ecs--built-in-systems)
29. [Physics Overlap Queries](#physics-overlap-queries)
30. [Component Introspection](#component-introspection)
31. [Debug Overlay](#debug-overlay)
32. [Asset Cache](#asset-cache)
33. [Bezier Curves & Splines](#bezier-curves--splines)
34. [Gradient Drawing](#gradient-drawing)
35. [Parallax Scrolling](#parallax-scrolling)
36. [Trail Renderer](#trail-renderer)
37. [Random Number Generator](#random-number-generator)
38. [Additional Shape Drawing](#additional-shape-drawing)
39. [Text Measurement & Advanced Text](#text-measurement--advanced-text)
40. [Gamepad/Controller Input](#gamepadcontroller-input)
41. [Color Utilities](#color-utilities)
42. [Window/Display Utilities](#windowdisplay-utilities)
43. [Sprite Animation Player](#sprite-animation-player)
44. [Tilemap Collision](#tilemap-collision)
45. [Nine-Slice Drawing](#nine-slice-drawing)
46. [Touch Input](#touch-input)
47. [Screenshot & Recording](#screenshot--recording)
48. [Constants & Enums](#constants--enums)
49. [VB.NET Helper Classes](#vbnet-helper-classes)

---

## Quick Start Examples

### Example 1: Basic Game Loop

```vb
Imports RaylibWrapper.FrameworkWrapper

Module MyGame
    Sub Main()
        Framework_Initialize(800, 600, "My First Game")
        Framework_SetTargetFPS(60)
        Framework_InitAudio()

        While Not Framework_ShouldClose()
            Framework_Update()
            Framework_BeginDrawing()
            Framework_ClearBackground(30, 30, 50, 255)
            Framework_DrawText("Hello, World!", 350, 280, 24, 255, 255, 255, 255)
            Framework_EndDrawing()
        End While

        Framework_CloseAudio()
        Framework_Shutdown()
    End Sub
End Module
```

### Example 2: Creating and Moving an Entity

```vb
' Create a player entity
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(player, "Player")
Framework_Ecs_AddTransform2D(player, 400, 300, 0, 1, 1)
Framework_Ecs_AddVelocity2D(player, 0, 0)
Framework_Ecs_AddSprite2D(player)
Framework_Ecs_SetEnabled(player, True)

' Load texture and assign to sprite
Dim tex As Integer = Framework_AcquireTextureH("player.png")
Framework_Ecs_SetSpriteTexture(player, tex)

' In your update loop
Dim dt As Single = Framework_GetDeltaTime()
Dim speed As Single = 200.0F * dt
If Framework_IsKeyDown(Keys.W) Then
    Dim vx, vy As Single
    Framework_Ecs_GetVelocity(player, vx, vy)
    Framework_Ecs_SetVelocity(player, vx, -speed)
End If

' Update systems
Framework_Ecs_UpdateVelocities()

' Draw
Framework_Ecs_DrawSprites()
```

### Example 3: Camera Follow with Shake

```vb
' Set up camera to follow player
Framework_Camera_SetFollowTarget(player)
Framework_Camera_SetFollowLerp(0.1F)
Framework_Camera_SetFollowEnabled(True)
Framework_Camera_SetBounds(0, 0, 3200, 2400)

' In game loop
Framework_Camera_Update()
Framework_Camera_BeginMode()
    Framework_Ecs_DrawSprites()
Framework_Camera_EndMode()

' On player hit - trigger shake
Framework_Camera_Shake(12.0F, 0.4F)
```

### Example 4: Audio with Groups and Fades

```vb
Framework_InitAudio()

' Load sounds into groups (0=SFX, 1=Music, 2=UI)
Dim jumpSfx As Integer = Framework_Audio_LoadSound("jump.wav", 0)
Dim bgMusic As Integer = Framework_Audio_LoadMusic("theme.ogg")

Framework_Audio_PlayMusic(bgMusic)
Framework_Audio_FadeInMusic(bgMusic, 2.0F)

' Set spatial audio listener at player
Framework_Audio_SetSpatialEnabled(True)
Framework_Audio_SetSpatialFalloff(100, 800)

' Play jump sound at player world position
Dim px, py As Single
Framework_Ecs_GetTransformPosition(player, px, py)
Framework_Audio_SetListenerPosition(px, py)
Framework_Audio_PlaySoundAt(jumpSfx, px, py)

' Each frame
Framework_Audio_Update()
```

### Example 5: Scene Transitions

```vb
Dim menuScene As Integer = Framework_CreateScriptScene()
Dim gameScene As Integer = Framework_CreateScriptScene()

' Change scene with a fade transition
Framework_Scene_SetTransitionColor(0, 0, 0, 255)
Framework_Scene_ChangeWithTransitionEx(gameScene, 1, 0.5F, 0)  ' Fade, 0.5s, Linear
```

### Example 6: Screen Shake

```vb
' Simple shake on player hit
Framework_Camera_Shake(15.0F, 0.5F)

' Directional shake (x-heavy for horizontal impact)
Framework_Camera_ShakeEx(20.0F, 5.0F, 0.3F, 1.5F)

' Screen flash effect
Framework_Camera_Flash(255, 255, 255, 200, 0.15F)
```

---

## Core Framework

```vb
Framework_Initialize(width As Integer, height As Integer, title As String)
Framework_Update()
Framework_ShouldClose() As Boolean
Framework_Shutdown()
Framework_Pause()
Framework_Resume()
Framework_IsPaused() As Boolean
Framework_Quit()
Framework_GetState() As Integer  ' Returns EngineState enum value
```

---

## Timing & Time Control

```vb
Framework_SetTargetFPS(fps As Integer)
Framework_GetFPS() As Integer
Framework_GetFrameTime() As Single
Framework_GetDeltaTime() As Single          ' Alias for GetFrameTime
Framework_GetTime() As Double              ' Total elapsed time in seconds
Framework_GetFrameCount() As Long
Framework_SetTimeScale(scale As Single)    ' 0.5 = half speed, 2.0 = double
Framework_GetTimeScale() As Single
Framework_SetFixedStep(dt As Double)       ' e.g., 1.0 / 60.0
Framework_GetFixedStep() As Double
Framework_StepFixed()                      ' Advance one fixed timestep manually
```

---

## Drawing

```vb
' Frame control
Framework_BeginDrawing()
Framework_EndDrawing()
Framework_ClearBackground(r As Byte, g As Byte, b As Byte, a As Byte)

' Primitives
Framework_DrawPixel(x As Integer, y As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawLine(x1 As Single, y1 As Single, x2 As Single, y2 As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawRectangle(x As Single, y As Single, w As Single, h As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawCircle(cx As Single, cy As Single, radius As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawCircleLines(cx As Single, cy As Single, radius As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTriangle(x1 As Single, y1 As Single, x2 As Single, y2 As Single, x3 As Single, y3 As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTriangleLines(x1 As Single, y1 As Single, x2 As Single, y2 As Single, x3 As Single, y3 As Single, r As Byte, g As Byte, b As Byte, a As Byte)

' Text & HUD
Framework_DrawText(text As String, x As Single, y As Single, size As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawFPS(x As Integer, y As Integer)
Framework_DrawGrid(slices As Integer, spacing As Single)
```

---

## Input — Keyboard

```vb
Framework_IsKeyPressed(key As Integer) As Boolean       ' True once per press
Framework_IsKeyPressedRepeat(key As Integer) As Boolean ' True with OS key repeat
Framework_IsKeyDown(key As Integer) As Boolean          ' True while held
Framework_IsKeyReleased(key As Integer) As Boolean      ' True once on release
Framework_IsKeyUp(key As Integer) As Boolean            ' True while not held
Framework_GetKeyPressed() As Integer                    ' Key code of last pressed key
Framework_GetCharPressed() As Integer                   ' Char code of last pressed char
Framework_SetExitKey(key As Integer)                    ' Which key closes window
```

---

## Input — Mouse

```vb
Framework_GetMouseX() As Single
Framework_GetMouseY() As Single
Framework_GetMousePosition() As Vector2
Framework_GetMouseDelta() As Vector2
Framework_SetMousePosition(x As Integer, y As Integer)
Framework_SetMouseOffset(offsetX As Integer, offsetY As Integer)
Framework_SetMouseScale(scaleX As Single, scaleY As Single)
Framework_GetMouseWheelMove() As Single
Framework_GetMouseWheelMoveV() As Vector2
Framework_SetMouseCursor(cursor As Integer)
Framework_IsMouseButtonPressed(button As Integer) As Boolean
Framework_IsMouseButtonDown(button As Integer) As Boolean
Framework_IsMouseButtonReleased(button As Integer) As Boolean
Framework_IsMouseButtonUp(button As Integer) As Boolean
Framework_ShowCursor()
Framework_HideCursor()
Framework_IsCursorHidden() As Boolean
Framework_EnableCursor()
Framework_DisableCursor()
Framework_IsCursorOnScreen() As Boolean
```

---

## Collision Checks

These are pure math checks — no ECS involvement. For entity collider queries see [Physics Overlap Queries](#physics-overlap-queries).

```vb
Framework_CheckCollisionRecs(rec1 As Rectangle, rec2 As Rectangle) As Boolean
Framework_CheckCollisionCircles(center1 As Vector2, radius1 As Single, center2 As Vector2, radius2 As Single) As Boolean
Framework_CheckCollisionCircleRec(center As Vector2, radius As Single, rec As Rectangle) As Boolean
Framework_CheckCollisionCircleLine(center As Vector2, radius As Single, p1 As Vector2, p2 As Vector2) As Boolean
Framework_CheckCollisionPointRec(point As Vector2, rec As Rectangle) As Boolean
Framework_CheckCollisionPointCircle(point As Vector2, center As Vector2, radius As Single) As Boolean
Framework_CheckCollisionPointTriangle(point As Vector2, p1 As Vector2, p2 As Vector2, p3 As Vector2) As Boolean
Framework_CheckCollisionPointLine(point As Vector2, p1 As Vector2, p2 As Vector2, threshold As Integer) As Boolean
Framework_CheckCollisionPointPoly(point As Vector2, points As IntPtr, pointCount As Integer) As Boolean
Framework_CheckCollisionLines(startPos1 As Vector2, endPos1 As Vector2, startPos2 As Vector2, endPos2 As Vector2, ByRef collisionPoint As Vector2) As Boolean
Framework_GetCollisionRec(rec1 As Rectangle, rec2 As Rectangle) As Rectangle
```

---

## Textures & Images

```vb
' Direct texture structs (lower-level)
Framework_LoadTexture(fileName As String) As Texture2D
Framework_UnloadTexture(texture As Texture2D)
Framework_IsTextureValid(texture As Texture2D) As Boolean
Framework_UpdateTexture(texture As Texture2D, pixels As IntPtr)
Framework_UpdateTextureRec(texture As Texture2D, rec As Rectangle, pixels As IntPtr)
Framework_GenTextureMipmaps(ByRef texture As Texture2D)
Framework_SetTextureFilter(texture As Texture2D, filter As Integer)
Framework_SetTextureWrap(texture As Texture2D, wrap As Integer)

' Drawing
Framework_DrawTexture(texture As Texture2D, posX As Integer, posY As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTextureV(texture As Texture2D, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTextureEx(texture As Texture2D, position As Vector2, rotation As Single, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTextureRec(texture As Texture2D, source As Rectangle, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTexturePro(texture As Texture2D, source As Rectangle, dest As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTextureNPatch(texture As Texture2D, nPatchInfo As NPatchInfo, dest As Rectangle, origin As Vector2, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)

' CPU images
Framework_LoadImage(fileName As String) As Image
Framework_UnloadImage(image As Image)
Framework_ImageColorInvert(ByRef image As Image)
Framework_ImageResize(ByRef image As Image, newWidth As Integer, newHeight As Integer)
Framework_ImageFlipVertical(ByRef image As Image)

' Sprite sheets
Framework_SpriteFrame(frameWidth As Integer, frameHeight As Integer, frameIndex As Integer) As Rectangle
```

---

## Handle-Based Texture API

A ref-counted handle system that caches textures by filename and automatically unloads when all handles are released. Prefer this over raw Texture2D for most use cases.

```vb
Framework_AcquireTextureH(fileName As String) As Integer  ' Returns handle; increments ref count
Framework_ReleaseTextureH(handle As Integer)              ' Decrements ref count; unloads at 0
Framework_IsTextureValidH(handle As Integer) As Boolean
Framework_GetTextureWidth(handle As Integer) As Integer
Framework_GetTextureHeight(handle As Integer) As Integer

' Drawing by handle
Framework_DrawTextureH(handle As Integer, x As Integer, y As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTextureVH(handle As Integer, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTextureExH(handle As Integer, position As Vector2, rotation As Single, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)
```

**Example:**
```vb
' Load once — multiple callers can AcquireTextureH the same file, ref-counted
Dim tex As Integer = Framework_AcquireTextureH("assets/player.png")
Framework_DrawTextureH(tex, 100, 100, 255, 255, 255, 255)

' Release when done
Framework_ReleaseTextureH(tex)
```

---

## Render Textures & Off-Screen Rendering

```vb
Framework_LoadRenderTexture(width As Integer, height As Integer) As RenderTexture2D
Framework_UnloadRenderTexture(target As RenderTexture2D)
Framework_IsRenderTextureValid(target As RenderTexture2D) As Boolean
Framework_BeginTextureMode(target As RenderTexture2D)  ' Redirect all draws to texture
Framework_EndTextureMode()                              ' Resume drawing to screen
Framework_BeginMode2D(camera As Camera2D)              ' Apply camera transform
Framework_EndMode2D()
```

**Example:**
```vb
Dim rt As RenderTexture2D = Framework_LoadRenderTexture(800, 600)

' Render scene to texture
Framework_BeginTextureMode(rt)
    Framework_ClearBackground(0, 0, 0, 255)
    Framework_Ecs_DrawSprites()
Framework_EndTextureMode()

' Draw texture to screen (flipped vertically — OpenGL convention)
Framework_DrawTextureRec(rt.texture,
    New Rectangle(0, 0, 800, -600),
    New Vector2(0, 0),
    New Color(255, 255, 255, 255))
```

---

## Camera 2D — Basic

The engine maintains a single global Camera2D.

```vb
Framework_Camera_SetPosition(x As Single, y As Single)
Framework_Camera_SetTarget(x As Single, y As Single)
Framework_Camera_SetRotation(rotation As Single)       ' Degrees
Framework_Camera_SetZoom(zoom As Single)               ' 1.0 = normal
Framework_Camera_SetOffset(x As Single, y As Single)  ' Screen-space offset
Framework_Camera_GetPosition() As Vector2
Framework_Camera_GetZoom() As Single
Framework_Camera_GetRotation() As Single
Framework_Camera_BeginMode()
Framework_Camera_EndMode()
Framework_Camera_ScreenToWorld(x As Single, y As Single) As Vector2
Framework_Camera_WorldToScreen(x As Single, y As Single) As Vector2
Framework_Camera_Reset()
```

---

## Camera 2D — Enhanced

```vb
' Smooth entity follow
Framework_Camera_SetFollowTarget(entityId As Integer)
Framework_Camera_SetFollowLerp(lerp As Single)          ' 0.0 = no smoothing, 1.0 = instant
Framework_Camera_SetFollowEnabled(enabled As Boolean)
Framework_Camera_IsFollowEnabled() As Boolean

' Deadzone — camera only moves when target leaves this rect
Framework_Camera_SetDeadzone(width As Single, height As Single)
Framework_Camera_SetDeadzoneEnabled(enabled As Boolean)

' Look-ahead — shift camera in direction of travel
Framework_Camera_SetLookahead(distance As Single)
Framework_Camera_SetLookaheadVelocity(vx As Single, vy As Single)

' Screen shake
Framework_Camera_Shake(intensity As Single, duration As Single)
Framework_Camera_ShakeEx(intensityX As Single, intensityY As Single, duration As Single, falloff As Single)
Framework_Camera_StopShake()
Framework_Camera_IsShaking() As Boolean

' World bounds — camera won't show outside these limits
Framework_Camera_SetBounds(x As Single, y As Single, width As Single, height As Single)
Framework_Camera_GetBounds() As Rectangle
Framework_Camera_SetZoomLimits(minZoom As Single, maxZoom As Single)

' Smooth transitions
Framework_Camera_ZoomTo(zoom As Single, duration As Single)
Framework_Camera_ZoomAt(zoom As Single, worldX As Single, worldY As Single, duration As Single)
Framework_Camera_RotateTo(rotation As Single, duration As Single)
Framework_Camera_PanTo(x As Single, y As Single, duration As Single)
Framework_Camera_PanBy(dx As Single, dy As Single, duration As Single)

' Screen flash overlay
Framework_Camera_Flash(r As Byte, g As Byte, b As Byte, a As Byte, duration As Single)

' Must be called each frame when using follow/shake/zoom transitions
Framework_Camera_Update()
```

---

## Fonts & Text

```vb
' Direct font structs
Framework_LoadFontEx(fileName As String, fontSize As Integer, codepoints As IntPtr, codepointCount As Integer) As Font
Framework_UnloadFont(font As Font)
Framework_DrawTextEx(font As Font, text As String, position As Vector2, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte)

' Handle-based (ref-counted, cached by filename+size)
Framework_AcquireFontH(fileName As String, fontSize As Integer) As Integer
Framework_ReleaseFontH(handle As Integer)
Framework_IsFontValidH(handle As Integer) As Boolean
Framework_DrawTextExH(handle As Integer, text As String, position As Vector2, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte)
```

---

## Audio — Basic

The basic API uses handle integers. For pooling, groups, and effects see [Audio Manager — Advanced](#audio-manager--advanced).

```vb
' Init / Shutdown
Framework_InitAudio()
Framework_CloseAudio()
Framework_SetMasterVolume(volume As Single)   ' 0.0 – 1.0
Framework_GetMasterVolume() As Single
Framework_PauseAllAudio()
Framework_ResumeAllAudio()

' Sounds
Framework_LoadSoundH(fileName As String) As Integer
Framework_UnloadSoundH(handle As Integer)
Framework_PlaySoundH(handle As Integer)
Framework_StopSoundH(handle As Integer)
Framework_PauseSoundH(handle As Integer)
Framework_ResumeSoundH(handle As Integer)
Framework_SetSoundVolumeH(handle As Integer, volume As Single)
Framework_SetSoundPitchH(handle As Integer, pitch As Single)   ' 1.0 = normal
Framework_SetSoundPanH(handle As Integer, pan As Single)       ' -1.0 left … 1.0 right

' Music streams
Framework_AcquireMusicH(fileName As String) As Integer
Framework_ReleaseMusicH(handle As Integer)
Framework_IsMusicValidH(handle As Integer) As Boolean
Framework_PlayMusicH(handle As Integer)
Framework_StopMusicH(handle As Integer)
Framework_PauseMusicH(handle As Integer)
Framework_ResumeMusicH(handle As Integer)
Framework_SetMusicVolumeH(handle As Integer, volume As Single)
Framework_SetMusicPitchH(handle As Integer, pitch As Single)
Framework_UpdateMusicH(handle As Integer)     ' Call each frame for active stream
Framework_UpdateAllMusic()                    ' Call each frame — updates all active streams
```

---

## Audio Manager — Advanced

Managed audio system with groups, spatial audio, pooling, playlists, and crossfading.

```vb
' Volume groups
Framework_Audio_SetGroupVolume(group As Integer, volume As Single)
Framework_Audio_GetGroupVolume(group As Integer) As Single
Framework_Audio_SetGroupMuted(group As Integer, muted As Boolean)
Framework_Audio_IsGroupMuted(group As Integer) As Boolean
Framework_Audio_FadeGroupVolume(group As Integer, targetVolume As Single, duration As Single)

' Managed sounds (tracked by the audio manager)
Framework_Audio_LoadSound(fileName As String, group As Integer) As Integer  ' Returns soundId
Framework_Audio_UnloadSound(soundId As Integer)
Framework_Audio_PlaySound(soundId As Integer)
Framework_Audio_PlaySoundEx(soundId As Integer, volume As Single, pitch As Single, pan As Single)
Framework_Audio_StopSound(soundId As Integer)
Framework_Audio_SetSoundGroup(soundId As Integer, group As Integer)
Framework_Audio_GetSoundGroup(soundId As Integer) As Integer

' Spatial audio
Framework_Audio_SetListenerPosition(x As Single, y As Single)
Framework_Audio_PlaySoundAt(soundId As Integer, x As Single, y As Single)
Framework_Audio_PlaySoundAtEx(soundId As Integer, x As Single, y As Single, volume As Single, pitch As Single)
Framework_Audio_SetSpatialFalloff(minDistance As Single, maxDistance As Single)
Framework_Audio_SetSpatialEnabled(enabled As Boolean)

' Sound pooling — pre-allocate N instances for frequently played sounds
Framework_Audio_CreatePool(soundId As Integer, count As Integer) As Integer  ' Returns poolId
Framework_Audio_DestroyPool(poolId As Integer)
Framework_Audio_PlayFromPool(poolId As Integer)
Framework_Audio_PlayFromPoolEx(poolId As Integer, volume As Single, pitch As Single, pan As Single)

' Managed music
Framework_Audio_LoadMusic(fileName As String) As Integer  ' Returns musicId
Framework_Audio_UnloadMusic(musicId As Integer)
Framework_Audio_PlayMusic(musicId As Integer)
Framework_Audio_StopMusic(musicId As Integer)
Framework_Audio_PauseMusic(musicId As Integer)
Framework_Audio_ResumeMusic(musicId As Integer)
Framework_Audio_SetMusicVolume(musicId As Integer, volume As Single)
Framework_Audio_SetMusicPitch(musicId As Integer, pitch As Single)
Framework_Audio_SetMusicLooping(musicId As Integer, loop As Boolean)
Framework_Audio_IsMusicPlaying(musicId As Integer) As Boolean
Framework_Audio_GetMusicLength(musicId As Integer) As Single    ' Duration in seconds
Framework_Audio_GetMusicPosition(musicId As Integer) As Single  ' Playback position in seconds
Framework_Audio_SeekMusic(musicId As Integer, position As Single)

' Transitions
Framework_Audio_CrossfadeTo(fromMusicId As Integer, toMusicId As Integer, duration As Single)
Framework_Audio_FadeOutMusic(musicId As Integer, duration As Single)
Framework_Audio_FadeInMusic(musicId As Integer, duration As Single)

' Playlists
Framework_Audio_CreatePlaylist() As Integer   ' Returns playlistId
Framework_Audio_DestroyPlaylist(playlistId As Integer)
Framework_Audio_PlaylistAdd(playlistId As Integer, musicId As Integer)
Framework_Audio_PlaylistRemove(playlistId As Integer, index As Integer)
Framework_Audio_PlaylistPlay(playlistId As Integer)
Framework_Audio_PlaylistNext(playlistId As Integer)
Framework_Audio_PlaylistPrev(playlistId As Integer)
Framework_Audio_PlaylistSetShuffle(playlistId As Integer, shuffle As Boolean)
Framework_Audio_PlaylistSetRepeat(playlistId As Integer, repeat As Boolean)

' Must be called each frame
Framework_Audio_Update()
```

---

## Audio Effects & Filters

```vb
' Filters (low-pass, high-pass, band-pass)
Framework_Audio_CreateFilter(filterType As Integer) As Integer  ' Returns filterId
Framework_Audio_DestroyFilter(filterId As Integer)
Framework_Audio_SetFilterCutoff(filterId As Integer, frequency As Single)
Framework_Audio_SetFilterResonance(filterId As Integer, resonance As Single)
Framework_Audio_SetFilterGain(filterId As Integer, gain As Single)
Framework_Audio_ApplyFilterToSound(filterId As Integer, soundId As Integer)
Framework_Audio_ApplyFilterToGroup(filterId As Integer, group As Integer)
Framework_Audio_RemoveFilterFromSound(filterId As Integer, soundId As Integer)
Framework_Audio_RemoveFilterFromGroup(filterId As Integer, group As Integer)
Framework_Audio_SetFilterEnabled(filterId As Integer, enabled As Boolean)
Framework_Audio_IsFilterEnabled(filterId As Integer) As Boolean

' Reverb
Framework_Audio_CreateReverb() As Integer  ' Returns reverbId
Framework_Audio_DestroyReverb(reverbId As Integer)
Framework_Audio_SetReverbDecay(reverbId As Integer, time As Single)
Framework_Audio_SetReverbDensity(reverbId As Integer, density As Single)
Framework_Audio_SetReverbDiffusion(reverbId As Integer, diffusion As Single)
Framework_Audio_SetReverbRoomSize(reverbId As Integer, size As Single)
Framework_Audio_SetReverbWetDry(reverbId As Integer, wet As Single)  ' 0.0 dry … 1.0 wet
Framework_Audio_SetReverbPreDelay(reverbId As Integer, delay As Single)
Framework_Audio_ApplyReverbToSound(reverbId As Integer, soundId As Integer)
Framework_Audio_ApplyReverbToGroup(reverbId As Integer, group As Integer)
Framework_Audio_RemoveReverbFromSound(reverbId As Integer, soundId As Integer)
Framework_Audio_RemoveReverbFromGroup(reverbId As Integer, group As Integer)
Framework_Audio_SetReverbPreset(reverbId As Integer, preset As Integer)

' Echo
Framework_Audio_CreateEcho(delay As Single, feedback As Single) As Integer
Framework_Audio_DestroyEcho(echoId As Integer)

' Distortion
Framework_Audio_CreateDistortion(drive As Single, mix As Single) As Integer
Framework_Audio_DestroyDistortion(distortionId As Integer)

' Compressor
Framework_Audio_CreateCompressor(threshold As Single, ratio As Single) As Integer
Framework_Audio_DestroyCompressor(compressorId As Integer)
```

---

## Shaders

```vb
Framework_LoadShaderF(vsFileName As String, fsFileName As String) As Shader
Framework_UnloadShader(shader As Shader)
Framework_BeginShaderMode(shader As Shader)
Framework_EndShaderMode()
Framework_GetShaderLocation(shader As Shader, uniformName As String) As Integer

' Set uniforms
Framework_SetShaderValue1f(shader As Shader, locIndex As Integer, value As Single)
Framework_SetShaderValue2f(shader As Shader, locIndex As Integer, x As Single, y As Single)
Framework_SetShaderValue3f(shader As Shader, locIndex As Integer, x As Single, y As Single, z As Single)
Framework_SetShaderValue4f(shader As Shader, locIndex As Integer, x As Single, y As Single, z As Single, w As Single)
Framework_SetShaderValue1i(shader As Shader, locIndex As Integer, value As Integer)
```

**Example:**
```vb
Dim vignette As Shader = Framework_LoadShaderF(Nothing, "assets/shaders/vignette.fs")
Dim intensityLoc As Integer = Framework_GetShaderLocation(vignette, "intensity")
Framework_SetShaderValue1f(vignette, intensityLoc, 0.6F)

' Apply to full-screen render texture
Framework_BeginShaderMode(vignette)
    Framework_DrawTexturePro(rt.texture, ...)
Framework_EndShaderMode()
```

---

## Scene System — Basic

A scene holds its own entity set, callbacks, and lifecycle. Scenes can be stacked.

```vb
Framework_CreateScriptScene() As Integer   ' Returns sceneId
Framework_DestroyScene(sceneId As Integer)
Framework_SceneChange(sceneId As Integer)  ' Immediate switch, no transition
Framework_ScenePush(sceneId As Integer)    ' Push onto stack
Framework_ScenePop()                       ' Pop top scene
Framework_SceneHas(sceneId As Integer) As Boolean
Framework_SceneTick(sceneId As Integer)    ' Update given scene
Framework_SceneGetCurrent() As Integer     ' Returns current scene id
```

---

## Scene Manager — Transitions & Loading

```vb
' Default transition settings
Framework_Scene_SetTransition(transitionType As Integer, duration As Single)
Framework_Scene_SetTransitionEx(transitionType As Integer, duration As Single, easing As Integer)
Framework_Scene_SetTransitionColor(r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Scene_GetTransitionType() As Integer
Framework_Scene_GetTransitionDuration() As Single
Framework_Scene_GetTransitionEasing() As Integer

' Transitioning scene changes
Framework_Scene_ChangeWithTransition(sceneId As Integer)
Framework_Scene_ChangeWithTransitionEx(sceneId As Integer, transitionType As Integer, duration As Single, easing As Integer)
Framework_Scene_PushWithTransition(sceneId As Integer)
Framework_Scene_PopWithTransition()

' Transition state
Framework_Scene_IsTransitioning() As Boolean
Framework_Scene_GetTransitionState() As Integer
Framework_Scene_GetTransitionProgress() As Single    ' 0.0 – 1.0
Framework_Scene_SkipTransition()

' Loading screen
Framework_Scene_SetLoadingEnabled(enabled As Boolean)
Framework_Scene_IsLoadingEnabled() As Boolean
Framework_Scene_SetLoadingMinDuration(seconds As Single)
Framework_Scene_SetLoadingCallback(callback As IntPtr)
Framework_Scene_SetLoadingDrawCallback(callback As IntPtr)

' Scene stack info
Framework_Scene_GetStackSize() As Integer
Framework_Scene_GetSceneAt(index As Integer) As Integer
```

**Transition types:**

| Value | Name | Description |
|-------|------|-------------|
| 0 | None | Instant switch |
| 1 | Fade | Fade to color |
| 2 | SlideLeft | Slide outgoing left |
| 3 | SlideRight | Slide outgoing right |
| 4 | SlideUp | Slide outgoing up |
| 5 | SlideDown | Slide outgoing down |
| 6 | Wipe | Wipe across |
| 7 | Pixelate | Pixelate/dissolve |
| 8 | Dissolve | Random pixel dissolve |
| 9 | Iris | Circle iris |
| 10 | Swipe | Full swipe |
| 11 | Zoom | Zoom out/in |
| 12 | Rotate | Rotate out/in |
| 13 | Checkerboard | Checkerboard reveal |
| 14 | Random | Random transition each time |

**Easing types:** 0=Linear, 1=EaseIn, 2=EaseOut, 3=EaseInOut, 4=Bounce, 5=Elastic, 6=Back, 7=Spring (21 total variants)

---

## ECS — Entity Management

Entity IDs are plain integers. `-1` is the null/invalid entity.

```vb
Framework_Ecs_CreateEntity() As Integer
Framework_Ecs_DestroyEntity(id As Integer)
Framework_Ecs_IsAlive(id As Integer) As Boolean
Framework_Ecs_ClearAll()
Framework_Ecs_GetEntityCount() As Integer
```

---

## ECS — Name Component

Names are limited to 64 characters (`FW_NAME_MAX`).

```vb
Framework_Ecs_SetName(id As Integer, name As String)
Framework_Ecs_GetName(id As Integer) As String
Framework_Ecs_HasName(id As Integer) As Boolean
Framework_Ecs_FindByName(name As String) As Integer   ' Returns -1 if not found
```

---

## ECS — Tag Component

Tags are limited to 32 characters (`FW_TAG_MAX`). Used for group queries.

```vb
Framework_Ecs_SetTag(id As Integer, tag As String)
Framework_Ecs_GetTag(id As Integer) As String
Framework_Ecs_HasTag(id As Integer) As Boolean
Framework_Ecs_FindAllByTag(tag As String, ids As Integer(), maxCount As Integer) As Integer  ' Returns actual count found
```

---

## ECS — Enabled Component

Entities can be locally enabled/disabled. `IsActiveInHierarchy` accounts for parent enabled state.

```vb
Framework_Ecs_SetEnabled(id As Integer, enabled As Boolean)
Framework_Ecs_IsEnabled(id As Integer) As Boolean
Framework_Ecs_IsActiveInHierarchy(id As Integer) As Boolean
```

---

## ECS — Hierarchy Component

Entities form a tree. World transforms propagate through the hierarchy.

```vb
Framework_Ecs_SetParent(childId As Integer, parentId As Integer)  ' parentId = -1 for root
Framework_Ecs_GetParent(id As Integer) As Integer
Framework_Ecs_GetFirstChild(id As Integer) As Integer
Framework_Ecs_GetNextSibling(id As Integer) As Integer
Framework_Ecs_GetChildCount(id As Integer) As Integer
Framework_Ecs_GetChildren(id As Integer, ids As Integer(), maxCount As Integer) As Integer
Framework_Ecs_DetachFromParent(id As Integer)
```

**Traversal example:**
```vb
' Iterate children
Dim child As Integer = Framework_Ecs_GetFirstChild(parent)
While child <> -1
    ' process child
    child = Framework_Ecs_GetNextSibling(child)
End While
```

---

## ECS — Transform2D Component

Local position/rotation/scale. World values are computed from the hierarchy chain.

```vb
Framework_Ecs_AddTransform2D(id As Integer, x As Single, y As Single, rotation As Single, scaleX As Single, scaleY As Single)
Framework_Ecs_HasTransform2D(id As Integer) As Boolean

' Local space
Framework_Ecs_SetTransformPosition(id As Integer, x As Single, y As Single)
Framework_Ecs_GetTransformPosition(id As Integer, ByRef x As Single, ByRef y As Single)
Framework_Ecs_SetTransformRotation(id As Integer, rotation As Single)  ' Degrees
Framework_Ecs_GetTransformRotation(id As Integer) As Single
Framework_Ecs_SetTransformScale(id As Integer, scaleX As Single, scaleY As Single)
Framework_Ecs_GetTransformScale(id As Integer, ByRef scaleX As Single, ByRef scaleY As Single)

' World space (hierarchy applied)
Framework_Ecs_GetWorldPosition(id As Integer, ByRef x As Single, ByRef y As Single)
Framework_Ecs_GetWorldRotation(id As Integer) As Single
Framework_Ecs_GetWorldScale(id As Integer, ByRef scaleX As Single, ByRef scaleY As Single)
```

---

## ECS — Velocity2D Component

Velocity is applied to Transform2D position each frame by `Framework_Ecs_UpdateVelocities()`.

```vb
Framework_Ecs_AddVelocity2D(id As Integer, vx As Single, vy As Single)
Framework_Ecs_HasVelocity2D(id As Integer) As Boolean
Framework_Ecs_SetVelocity(id As Integer, vx As Single, vy As Single)
Framework_Ecs_GetVelocity(id As Integer, ByRef vx As Single, ByRef vy As Single)
Framework_Ecs_RemoveVelocity2D(id As Integer)
```

---

## ECS — BoxCollider2D Component

Axis-aligned bounding box (AABB) relative to the entity's transform origin.

```vb
Framework_Ecs_AddBoxCollider2D(id As Integer, offsetX As Single, offsetY As Single, width As Single, height As Single)
Framework_Ecs_HasBoxCollider2D(id As Integer) As Boolean
Framework_Ecs_SetBoxCollider(id As Integer, offsetX As Single, offsetY As Single, width As Single, height As Single)
Framework_Ecs_SetBoxColliderTrigger(id As Integer, isTrigger As Boolean)  ' Trigger = no physics response
Framework_Ecs_GetBoxColliderWorldBounds(id As Integer) As Rectangle       ' World-space AABB
Framework_Ecs_RemoveBoxCollider2D(id As Integer)
```

---

## ECS — Sprite2D Component

Renders a textured rectangle at the entity's world transform.

```vb
Framework_Ecs_AddSprite2D(id As Integer)
Framework_Ecs_HasSprite2D(id As Integer) As Boolean
Framework_Ecs_SetSpriteTexture(id As Integer, textureHandle As Integer)
Framework_Ecs_SetSpriteTint(id As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Ecs_SetSpriteVisible(id As Integer, visible As Boolean)
Framework_Ecs_SetSpriteLayer(id As Integer, layer As Integer)       ' Higher = drawn on top
Framework_Ecs_SetSpriteSource(id As Integer, x As Single, y As Single, width As Single, height As Single)  ' Source rect for sprite sheets
Framework_Ecs_RemoveSprite2D(id As Integer)
```

---

## ECS — Built-in Systems

Call these each frame in your update/draw loop.

```vb
Framework_Ecs_UpdateVelocities()  ' Apply Velocity2D to Transform2D positions (dt-scaled)
Framework_Ecs_DrawSprites()       ' Render all Sprite2D components sorted by layer
```

---

## Physics Overlap Queries

Broad-phase queries against BoxCollider2D components.

```vb
Framework_Physics_OverlapBox(x As Single, y As Single, width As Single, height As Single, ids As Integer(), maxCount As Integer) As Integer
Framework_Physics_OverlapCircle(cx As Single, cy As Single, radius As Single, ids As Integer(), maxCount As Integer) As Integer
Framework_Physics_CheckEntityOverlap(idA As Integer, idB As Integer) As Boolean
Framework_Physics_GetOverlappingEntities(id As Integer, ids As Integer(), maxCount As Integer) As Integer
```

**Example:**
```vb
Dim results(31) As Integer
Dim count As Integer = Framework_Physics_OverlapBox(px - 50, py - 50, 100, 100, results, 32)
For i As Integer = 0 To count - 1
    If Framework_Ecs_GetTag(results(i)) = "enemy" Then
        ' process hit
    End If
Next
```

---

## Component Introspection

Runtime reflection over any entity's components and their fields. Useful for editors, save systems, and debug tools.

```vb
' Entity-level
Framework_Entity_GetComponentCount(id As Integer) As Integer
Framework_Entity_GetComponentTypeAt(id As Integer, index As Integer) As Integer  ' Returns ComponentType enum
Framework_Entity_HasComponent(id As Integer, componentType As Integer) As Boolean

' Field discovery
Framework_Component_GetFieldCount(id As Integer, componentType As Integer) As Integer
Framework_Component_GetFieldName(id As Integer, componentType As Integer, index As Integer) As String
Framework_Component_GetFieldType(id As Integer, componentType As Integer, index As Integer) As String

' Field read
Framework_Component_GetFieldFloat(id As Integer, componentType As Integer, fieldName As String) As Single
Framework_Component_GetFieldInt(id As Integer, componentType As Integer, fieldName As String) As Integer
Framework_Component_GetFieldBool(id As Integer, componentType As Integer, fieldName As String) As Boolean
Framework_Component_GetFieldString(id As Integer, componentType As Integer, fieldName As String) As String

' Field write
Framework_Component_SetFieldFloat(id As Integer, componentType As Integer, fieldName As String, value As Single)
Framework_Component_SetFieldInt(id As Integer, componentType As Integer, fieldName As String, value As Integer)
Framework_Component_SetFieldBool(id As Integer, componentType As Integer, fieldName As String, value As Boolean)
Framework_Component_SetFieldString(id As Integer, componentType As Integer, fieldName As String, value As String)
```

---

## Debug Overlay

Renders bounds, hierarchy lines, and stats on top of the game. Disabled by default.

```vb
Framework_Debug_SetEnabled(enabled As Boolean)
Framework_Debug_IsEnabled() As Boolean
Framework_Debug_DrawEntityBounds(enabled As Boolean)  ' Draw collider AABBs
Framework_Debug_DrawHierarchy(enabled As Boolean)     ' Draw parent→child lines
Framework_Debug_DrawStats(enabled As Boolean)         ' Draw entity/component counts
Framework_Debug_Render()                              ' Call after EndDrawing each frame
```

---

## Asset Cache

```vb
Framework_SetAssetRoot(path As String)  ' Base directory prepended to all asset paths
Framework_GetAssetRoot() As String
```

---

## Bezier Curves & Splines

Draw smooth curves and get points along them for movement paths, particle trails, etc.

```vb
' Draw a quadratic bezier curve (one control point)
Framework_DrawBezierQuad(x1, y1, cx, cy, x2, y2, thick, r, g, b, a)

' Draw a cubic bezier curve (two control points)
Framework_DrawBezierCubic(x1, y1, cx1, cy1, cx2, cy2, x2, y2, thick, r, g, b, a)

' Get a point along a quadratic bezier at parameter t (0.0 to 1.0)
Framework_BezierQuadPoint(x1, y1, cx, cy, x2, y2, t, ByRef outX, ByRef outY)

' Get a point along a cubic bezier at parameter t (0.0 to 1.0)
Framework_BezierCubicPoint(x1, y1, cx1, cy1, cx2, cy2, x2, y2, t, ByRef outX, ByRef outY)

' Draw a Catmull-Rom spline through an array of points
Framework_DrawSpline(points As Single(), count As Integer, thick As Single, r, g, b, a)

' Get a point along the spline at parameter t (0.0 to 1.0)
Framework_SplinePoint(points As Single(), count As Integer, t As Single, ByRef outX, ByRef outY)
```

### Example: Moving Entity Along Bezier Path
```vb
Dim t As Single = 0
While Not Framework_ShouldClose()
    t += Framework_GetDeltaTime() * 0.5  ' Move at half speed
    If t > 1.0 Then t = 0.0

    Dim px As Single, py As Single
    Framework_BezierCubicPoint(100, 400, 200, 100, 600, 100, 700, 400, t, px, py)
    Framework_Ecs_SetTransformPosition(entityId, px, py)
End While
```

---

## Gradient Drawing

Draw rectangles, circles, and lines with color gradients.

```vb
' Horizontal gradient rectangle (left color to right color)
Framework_DrawGradientRectH(x, y, w, h, r1, g1, b1, a1, r2, g2, b2, a2)

' Vertical gradient rectangle (top color to bottom color)
Framework_DrawGradientRectV(x, y, w, h, r1, g1, b1, a1, r2, g2, b2, a2)

' Four-corner gradient rectangle
Framework_DrawGradientRect4(x, y, w, h, tlR,tlG,tlB,tlA, trR,trG,trB,trA, blR,blG,blB,blA, brR,brG,brB,brA)

' Radial gradient circle (center color to edge color)
Framework_DrawGradientCircle(cx, cy, radius, r1, g1, b1, a1, r2, g2, b2, a2)

' Gradient line (start color to end color)
Framework_DrawGradientLine(x1, y1, x2, y2, thick, r1, g1, b1, a1, r2, g2, b2, a2)
```

---

## Parallax Scrolling

Create layered scrolling backgrounds with different speeds for depth effect.

```vb
' Create and configure a parallax layer
Dim layerId As Integer = Framework_Parallax_CreateLayer(textureHandle)
Framework_Parallax_SetScrollSpeed(layerId, 0.5, 0.0)  ' Half speed = far background
Framework_Parallax_SetRepeat(layerId, True, False)      ' Tile horizontally
Framework_Parallax_SetZOrder(layerId, -10)               ' Draw behind everything

' Full API
Framework_Parallax_CreateLayer(textureHandle) As Integer
Framework_Parallax_DestroyLayer(layerId)
Framework_Parallax_SetScrollSpeed(layerId, speedX, speedY)
Framework_Parallax_SetOffset(layerId, offsetX, offsetY)
Framework_Parallax_SetScale(layerId, scaleX, scaleY)
Framework_Parallax_SetTint(layerId, r, g, b, a)
Framework_Parallax_SetZOrder(layerId, zOrder)
Framework_Parallax_SetAutoScroll(layerId, enabled, speedX, speedY)
Framework_Parallax_SetRepeat(layerId, repeatX, repeatY)
Framework_Parallax_GetScrollSpeed(layerId, ByRef speedX, ByRef speedY)
Framework_Parallax_GetOffset(layerId, ByRef offsetX, ByRef offsetY)
Framework_Parallax_GetScale(layerId, ByRef scaleX, ByRef scaleY)
Framework_Parallax_GetTint(layerId, ByRef r, ByRef g, ByRef b, ByRef a)
Framework_Parallax_GetZOrder(layerId) As Integer
Framework_Parallax_IsAutoScrolling(layerId) As Boolean
Framework_Parallax_GetLayerCount() As Integer
Framework_Parallax_Update()       ' Call each frame
Framework_Parallax_DrawAll()      ' Draw all layers in z-order
Framework_Parallax_DestroyAll()   ' Cleanup all layers
```

---

## Trail Renderer

Create visual trails behind moving objects with width tapering and color fading.

```vb
' Create a trail and attach to an entity
Dim trailId As Integer = Framework_Trail_Create()
Framework_Trail_SetWidth(trailId, 8.0, 1.0)      ' Taper from 8px to 1px
Framework_Trail_SetColor(trailId, 255,100,50,255, 255,100,50,0)  ' Fade to transparent
Framework_Trail_SetLifetime(trailId, 0.5)          ' Points last 0.5 seconds
Framework_Trail_AttachToEntity(trailId, playerId)  ' Auto-follow entity

' Full API
Framework_Trail_Create() As Integer
Framework_Trail_Destroy(trailId)
Framework_Trail_SetWidth(trailId, startWidth, endWidth)
Framework_Trail_SetColor(trailId, r1,g1,b1,a1, r2,g2,b2,a2)
Framework_Trail_SetLifetime(trailId, seconds)
Framework_Trail_AttachToEntity(trailId, entityId)
Framework_Trail_DetachFromEntity(trailId)
Framework_Trail_AddPoint(trailId, x, y)        ' Manual point (if not attached)
Framework_Trail_Clear(trailId)                  ' Clear all points
Framework_Trail_SetEnabled(trailId, enabled)
Framework_Trail_IsEnabled(trailId) As Boolean
Framework_Trail_GetPointCount(trailId) As Integer
Framework_Trail_GetTrailCount() As Integer
Framework_Trail_Update()         ' Call each frame
Framework_Trail_DrawAll()        ' Draw all active trails
Framework_Trail_DestroyAll()     ' Cleanup all trails
```

---

## Random Number Generator

Seeded random number generation for deterministic or varied gameplay. Seed is auto-initialized on engine start; call `Framework_RandomSeed` for reproducible sequences.

| Function | Description |
|----------|-------------|
| `Framework_RandomSeed(seed As Integer)` | Seed the RNG with a specific value |
| `Framework_RandomInt(min As Integer, max As Integer) As Integer` | Random integer in range [min, max] |
| `Framework_RandomFloat(min As Single, max As Single) As Single` | Random float in range [min, max] |
| `Framework_RandomBool() As Boolean` | Random true/false (50/50) |
| `Framework_RandomChance(percent As Single) As Boolean` | True with given probability (0-100) |
| `Framework_RandomDirection(ByRef outX As Single, ByRef outY As Single)` | Random unit vector (normalized) |
| `Framework_RandomPointInCircle(cx As Single, cy As Single, radius As Single, ByRef outX As Single, ByRef outY As Single)` | Random point inside a circle |
| `Framework_RandomPointInRect(x As Single, y As Single, w As Single, h As Single, ByRef outX As Single, ByRef outY As Single)` | Random point inside a rectangle |
| `Framework_RandomWeighted(weights As Single(), count As Integer) As Integer` | Weighted random index selection |
| `Framework_RandomDice(sides As Integer) As Integer` | Roll a die (returns 1 to sides) |
| `Framework_RandomShuffle(arr As Integer(), count As Integer)` | Shuffle an array in place |
| `Framework_RandomColor(ByRef outR As Integer, ByRef outG As Integer, ByRef outB As Integer, ByRef outA As Integer)` | Random RGBA color (alpha always 255) |

### Example: Spawning Random Enemies

```vb
' Seed for reproducible testing
Framework_RandomSeed(12345)

' Spawn enemies at random positions in a zone
For i As Integer = 1 To 10
    Dim spawnX As Single, spawnY As Single
    Framework_RandomPointInRect(100, 100, 600, 400, spawnX, spawnY)

    Dim enemyId As Integer = Framework_Ecs_CreateEntity()
    Framework_Ecs_SetTransformPosition(enemyId, spawnX, spawnY)

    ' 30% chance of being elite
    If Framework_RandomChance(30) Then
        Framework_Ecs_SetTag(enemyId, "elite")
    End If
Next

' Random movement direction
Dim dirX As Single, dirY As Single
Framework_RandomDirection(dirX, dirY)

' Roll for damage
Dim damage As Integer = Framework_RandomDice(6) + Framework_RandomDice(6)
```

---

## Additional Shape Drawing

Extended shape primitives for drawing ellipses, rings, rounded rectangles, polygons, and circle sectors.

| Function | Description |
|----------|-------------|
| `Framework_DrawEllipse(cx As Integer, cy As Integer, radiusH As Single, radiusV As Single, r, g, b, a As Integer)` | Draw filled ellipse |
| `Framework_DrawEllipseLines(cx As Integer, cy As Integer, radiusH As Single, radiusV As Single, r, g, b, a As Integer)` | Draw ellipse outline |
| `Framework_DrawRing(cx As Single, cy As Single, innerRadius As Single, outerRadius As Single, startAngle As Single, endAngle As Single, segments As Integer, r, g, b, a As Integer)` | Draw filled ring/arc |
| `Framework_DrawRingLines(cx As Single, cy As Single, innerRadius As Single, outerRadius As Single, startAngle As Single, endAngle As Single, segments As Integer, r, g, b, a As Integer)` | Draw ring outline |
| `Framework_DrawRoundedRect(x As Single, y As Single, w As Single, h As Single, roundness As Single, segments As Integer, r, g, b, a As Integer)` | Draw filled rounded rectangle |
| `Framework_DrawRoundedRectLines(x As Single, y As Single, w As Single, h As Single, roundness As Single, segments As Integer, thick As Single, r, g, b, a As Integer)` | Draw rounded rectangle outline |
| `Framework_DrawPoly(cx As Single, cy As Single, sides As Integer, radius As Single, rotation As Single, r, g, b, a As Integer)` | Draw filled regular polygon |
| `Framework_DrawPolyLines(cx As Single, cy As Single, sides As Integer, radius As Single, rotation As Single, thick As Single, r, g, b, a As Integer)` | Draw polygon outline |
| `Framework_DrawCircleSector(cx As Single, cy As Single, radius As Single, startAngle As Single, endAngle As Single, segments As Integer, r, g, b, a As Integer)` | Draw filled circle sector (pie slice) |
| `Framework_DrawCircleSectorLines(cx As Single, cy As Single, radius As Single, startAngle As Single, endAngle As Single, segments As Integer, r, g, b, a As Integer)` | Draw circle sector outline |

### Example: Drawing UI Elements

```vb
' Rounded button background
Framework_DrawRoundedRect(200, 300, 160, 48, 0.5, 6, 60, 120, 200, 255)

' Health ring indicator (270 degrees = 75% health)
Framework_DrawRing(400, 300, 40, 50, 0, 270, 36, 0, 200, 0, 255)

' Hexagonal tile
Framework_DrawPoly(300, 200, 6, 40, 30, 100, 150, 100, 255)

' Pie chart sector
Framework_DrawCircleSector(500, 300, 80, 0, 120, 36, 200, 80, 80, 255)
```

---

## Text Measurement & Advanced Text

Measure text dimensions before drawing, and draw text with alignment options.

| Function | Description |
|----------|-------------|
| `Framework_MeasureText(text As String, fontSize As Integer) As Integer` | Measure text width in pixels (default font) |
| `Framework_MeasureTextEx(fontId As Integer, text As String, fontSize As Single, spacing As Single, ByRef outW As Single, ByRef outH As Single)` | Measure text width and height with custom font |
| `Framework_DrawTextCentered(text As String, cx As Integer, cy As Integer, fontSize As Integer, r, g, b, a As Integer)` | Draw text centered at position |
| `Framework_DrawTextRight(text As String, rightX As Integer, y As Integer, fontSize As Integer, r, g, b, a As Integer)` | Draw text right-aligned to position |

### Example: Centered Title and Score

```vb
' Center the title on screen
Dim screenW As Integer = 800
Framework_DrawTextCentered("SPACE SHOOTER", screenW / 2, 40, 36, 255, 255, 255, 255)

' Right-align score
Dim scoreText As String = "Score: " & score.ToString()
Framework_DrawTextRight(scoreText, screenW - 20, 10, 20, 255, 255, 0, 255)

' Measure text for custom positioning
Dim textW As Single, textH As Single
Framework_MeasureTextEx(fontId, "Level Complete", 32, 2, textW, textH)
' Use textW and textH to position a background box behind text
Framework_DrawRectangle(CInt(400 - textW/2 - 10), 280, CInt(textW + 20), CInt(textH + 20), 0, 0, 0, 180)
```

---

## Gamepad/Controller Input

Support for gamepad/controller input. Gamepad indices start at 0. Button and axis constants follow Raylib conventions (Xbox layout).

| Function | Description |
|----------|-------------|
| `Framework_IsGamepadAvailable(gamepad As Integer) As Boolean` | Check if a gamepad is connected |
| `Framework_IsGamepadButtonPressed(gamepad As Integer, button As Integer) As Boolean` | Check if button was just pressed |
| `Framework_IsGamepadButtonDown(gamepad As Integer, button As Integer) As Boolean` | Check if button is currently held |
| `Framework_IsGamepadButtonReleased(gamepad As Integer, button As Integer) As Boolean` | Check if button was just released |
| `Framework_GetGamepadAxisValue(gamepad As Integer, axis As Integer) As Single` | Get axis value (-1.0 to 1.0) |
| `Framework_GetGamepadName(gamepad As Integer) As String` | Get gamepad name string |
| `Framework_GetGamepadButtonCount(gamepad As Integer) As Integer` | Get number of buttons |
| `Framework_GetGamepadAxisCount(gamepad As Integer) As Integer` | Get number of axes |
| `Framework_SetGamepadMappings(mappings As String) As Integer` | Set custom SDL gamepad mappings |
| `Framework_GetGamepadButtonPressed() As Integer` | Get last pressed gamepad button |

### Example: Dual-Stick Controller Movement

```vb
If Framework_IsGamepadAvailable(0) Then
    ' Left stick for movement
    Dim moveX As Single = Framework_GetGamepadAxisValue(0, 0)  ' Left X
    Dim moveY As Single = Framework_GetGamepadAxisValue(0, 1)  ' Left Y

    ' Dead zone
    If Math.Abs(moveX) < 0.15 Then moveX = 0
    If Math.Abs(moveY) < 0.15 Then moveY = 0

    playerX += moveX * speed * Framework_GetDeltaTime()
    playerY += moveY * speed * Framework_GetDeltaTime()

    ' A button to jump (button 7 = Xbox A)
    If Framework_IsGamepadButtonPressed(0, 7) Then
        playerVelY = -jumpForce
    End If

    ' Right trigger for shooting (axis 5)
    Dim trigger As Single = Framework_GetGamepadAxisValue(0, 5)
    If trigger > 0.5 Then
        FireBullet()
    End If
End If
```

---

## Color Utilities

Convert between color spaces, blend colors, and apply adjustments.

| Function | Description |
|----------|-------------|
| `Framework_ColorFromHSV(hue As Single, saturation As Single, value As Single, ByRef outR As Integer, ByRef outG As Integer, ByRef outB As Integer)` | Convert HSV (0-360, 0-1, 0-1) to RGB |
| `Framework_ColorToHSV(r As Integer, g As Integer, b As Integer, ByRef outH As Single, ByRef outS As Single, ByRef outV As Single)` | Convert RGB to HSV |
| `Framework_ColorLerp(r1, g1, b1, a1 As Integer, r2, g2, b2, a2 As Integer, t As Single, ByRef outR, ByRef outG, ByRef outB, ByRef outA As Integer)` | Linearly interpolate between two colors |
| `Framework_ColorAlphaMultiply(r, g, b, a As Integer, alpha As Single, ByRef outR, ByRef outG, ByRef outB, ByRef outA As Integer)` | Multiply alpha channel by factor |
| `Framework_ColorBrighten(r, g, b As Integer, factor As Single, ByRef outR, ByRef outG, ByRef outB As Integer)` | Brighten color (factor 0-1) |
| `Framework_ColorDarken(r, g, b As Integer, factor As Single, ByRef outR, ByRef outG, ByRef outB As Integer)` | Darken color (factor 0-1) |

### Example: Rainbow Cycle and Damage Flash

```vb
' Rainbow cycle using HSV
Dim hue As Single = (Framework_GetTime() * 60) Mod 360
Dim cr As Integer, cg As Integer, cb As Integer
Framework_ColorFromHSV(hue, 1.0, 1.0, cr, cg, cb)
Framework_DrawCircle(400, 300, 50, cr, cg, cb, 255)

' Flash red on damage, lerp back to white
Dim flashT As Single = Math.Max(0, 1.0 - timeSinceHit)
Dim fr As Integer, fg As Integer, fb As Integer, fa As Integer
Framework_ColorLerp(255,255,255,255, 255,0,0,255, flashT, fr, fg, fb, fa)

' Darken color for shadow
Dim sr As Integer, sg As Integer, sb As Integer
Framework_ColorDarken(fr, fg, fb, 0.5, sr, sg, sb)
```

---

## Window/Display Utilities

Control window properties, toggle fullscreen/borderless modes, and query display information.

| Function | Description |
|----------|-------------|
| `Framework_ToggleFullscreen()` | Toggle fullscreen mode |
| `Framework_ToggleBorderless()` | Toggle borderless windowed mode |
| `Framework_SetWindowSize(width As Integer, height As Integer)` | Resize the window |
| `Framework_SetWindowPosition(x As Integer, y As Integer)` | Move the window |
| `Framework_SetWindowTitle(title As String)` | Change window title |
| `Framework_GetScreenWidth() As Integer` | Get current screen/window width |
| `Framework_GetScreenHeight() As Integer` | Get current screen/window height |
| `Framework_GetMonitorCount() As Integer` | Get number of connected monitors |
| `Framework_GetMonitorWidth(monitor As Integer) As Integer` | Get monitor width |
| `Framework_GetMonitorHeight(monitor As Integer) As Integer` | Get monitor height |
| `Framework_GetMonitorRefreshRate(monitor As Integer) As Integer` | Get monitor refresh rate |
| `Framework_SetWindowMinSize(width As Integer, height As Integer)` | Set minimum window size |

### Example: Fullscreen Toggle and Display Info

```vb
' Toggle fullscreen with F11
If Framework_IsKeyPressed(KEY_F11) Then
    Framework_ToggleFullscreen()
End If

' Adapt to screen size
Dim w As Integer = Framework_GetScreenWidth()
Dim h As Integer = Framework_GetScreenHeight()
Dim scale As Single = CSng(w) / 800.0   ' Scale relative to base 800px width

' Display monitor info
Dim monCount As Integer = Framework_GetMonitorCount()
For i As Integer = 0 To monCount - 1
    Dim mw As Integer = Framework_GetMonitorWidth(i)
    Dim mh As Integer = Framework_GetMonitorHeight(i)
    Dim hz As Integer = Framework_GetMonitorRefreshRate(i)
    Console.WriteLine("Monitor " & i & ": " & mw & "x" & mh & " @ " & hz & "Hz")
Next
```

---

## Sprite Animation Player

Create named animations with frame ranges and playback control. Animations reference frames from a sprite sheet texture.

| Function | Description |
|----------|-------------|
| `Framework_AnimPlayer_Create() As Integer` | Create a new animation player (returns ID) |
| `Framework_AnimPlayer_Destroy(playerId As Integer)` | Destroy an animation player |
| `Framework_AnimPlayer_AddAnim(playerId As Integer, name As String, startFrame As Integer, endFrame As Integer, fps As Single)` | Add a named animation with frame range and speed |
| `Framework_AnimPlayer_Play(playerId As Integer, name As String)` | Play a named animation |
| `Framework_AnimPlayer_Stop(playerId As Integer)` | Stop current animation |
| `Framework_AnimPlayer_Pause(playerId As Integer)` | Pause current animation |
| `Framework_AnimPlayer_Resume(playerId As Integer)` | Resume paused animation |
| `Framework_AnimPlayer_GetFrame(playerId As Integer) As Integer` | Get current frame index |
| `Framework_AnimPlayer_SetSpeed(playerId As Integer, speed As Single)` | Set playback speed multiplier |
| `Framework_AnimPlayer_IsPlaying(playerId As Integer) As Boolean` | Check if animation is playing |
| `Framework_AnimPlayer_SetLoop(playerId As Integer, loop As Boolean)` | Enable/disable looping |
| `Framework_AnimPlayer_GetCurrentAnim(playerId As Integer) As String` | Get name of current animation |
| `Framework_AnimPlayer_Update(playerId As Integer, dt As Single)` | Update animation (call each frame) |
| `Framework_AnimPlayer_Draw(playerId As Integer, textureId As Integer, x As Single, y As Single, frameW As Integer, frameH As Integer)` | Draw current frame from sprite sheet |

### Example: Character Animation

```vb
Dim animPlayer As Integer = Framework_AnimPlayer_Create()
Dim spriteSheet As Integer = Framework_LoadTexture("player_sheet.png")

' Define animations (frame indices in sprite sheet)
Framework_AnimPlayer_AddAnim(animPlayer, "idle", 0, 3, 8)      ' Frames 0-3 at 8 FPS
Framework_AnimPlayer_AddAnim(animPlayer, "run", 4, 11, 12)     ' Frames 4-11 at 12 FPS
Framework_AnimPlayer_AddAnim(animPlayer, "jump", 12, 15, 10)   ' Frames 12-15 at 10 FPS
Framework_AnimPlayer_AddAnim(animPlayer, "attack", 16, 21, 15) ' Frames 16-21 at 15 FPS

Framework_AnimPlayer_Play(animPlayer, "idle")

' In game loop:
Dim dt As Single = Framework_GetDeltaTime()
Framework_AnimPlayer_Update(animPlayer, dt)

' Switch animation based on state
If isRunning Then
    If Framework_AnimPlayer_GetCurrentAnim(animPlayer) <> "run" Then
        Framework_AnimPlayer_Play(animPlayer, "run")
    End If
End If

' Draw at player position (each frame is 64x64)
Framework_AnimPlayer_Draw(animPlayer, spriteSheet, playerX, playerY, 64, 64)
```

---

## Tilemap Collision

Create tile-based maps with collision detection. Tiles are addressed by column/row indices. Solid tiles block entity movement.

| Function | Description |
|----------|-------------|
| `Framework_Tilemap_Create(cols As Integer, rows As Integer, tileW As Integer, tileH As Integer) As Integer` | Create a tilemap (returns ID) |
| `Framework_Tilemap_Destroy(mapId As Integer)` | Destroy a tilemap |
| `Framework_Tilemap_SetTile(mapId As Integer, col As Integer, row As Integer, tileIndex As Integer)` | Set tile index at position |
| `Framework_Tilemap_GetTile(mapId As Integer, col As Integer, row As Integer) As Integer` | Get tile index at position |
| `Framework_Tilemap_SetSolid(mapId As Integer, tileIndex As Integer, solid As Boolean)` | Mark a tile index as solid/passable |
| `Framework_Tilemap_IsSolid(mapId As Integer, col As Integer, row As Integer) As Boolean` | Check if tile at position is solid |
| `Framework_Tilemap_CheckCollision(mapId As Integer, x As Single, y As Single, w As Single, h As Single) As Boolean` | Check rectangle collision against solid tiles |
| `Framework_Tilemap_WorldToTile(mapId As Integer, worldX As Single, worldY As Single, ByRef col As Integer, ByRef row As Integer)` | Convert world coordinates to tile indices |
| `Framework_Tilemap_TileToWorld(mapId As Integer, col As Integer, row As Integer, ByRef worldX As Single, ByRef worldY As Single)` | Convert tile indices to world coordinates |
| `Framework_Tilemap_Draw(mapId As Integer, textureId As Integer, tilesPerRow As Integer)` | Draw tilemap using a tile atlas texture |
| `Framework_Tilemap_DrawOffset(mapId As Integer, textureId As Integer, tilesPerRow As Integer, offsetX As Single, offsetY As Single)` | Draw tilemap with scroll offset |
| `Framework_Tilemap_Fill(mapId As Integer, tileIndex As Integer)` | Fill entire map with one tile index |

### Example: Platformer Tilemap

```vb
Dim tileAtlas As Integer = Framework_LoadTexture("tiles.png")
Dim map As Integer = Framework_Tilemap_Create(20, 15, 32, 32)

' Mark wall/ground tiles as solid
Framework_Tilemap_SetSolid(map, 1, True)   ' Tile index 1 = wall
Framework_Tilemap_SetSolid(map, 2, True)   ' Tile index 2 = ground

' Build floor
For col As Integer = 0 To 19
    Framework_Tilemap_SetTile(map, col, 14, 2)  ' Ground on bottom row
Next

' Add some walls
Framework_Tilemap_SetTile(map, 5, 10, 1)
Framework_Tilemap_SetTile(map, 5, 11, 1)

' Check player collision before moving
Dim newX As Single = playerX + velX * dt
Dim newY As Single = playerY + velY * dt
If Not Framework_Tilemap_CheckCollision(map, newX, newY, 24, 32) Then
    playerX = newX
    playerY = newY
End If

' Draw the map (8 tiles per row in atlas texture)
Framework_Tilemap_Draw(map, tileAtlas, 8)
```

---

## Nine-Slice Drawing

Draw scalable UI elements using nine-slice (9-patch) sprites. The corners stay fixed size while edges and center stretch.

| Function | Description |
|----------|-------------|
| `Framework_NineSlice_Create(textureId As Integer, left As Integer, right As Integer, top As Integer, bottom As Integer) As Integer` | Create a nine-slice config (returns ID) with border insets |
| `Framework_NineSlice_Destroy(configId As Integer)` | Destroy a nine-slice config |
| `Framework_NineSlice_Draw(configId As Integer, x As Single, y As Single, w As Single, h As Single)` | Draw nine-slice at position and size |
| `Framework_NineSlice_DrawTinted(configId As Integer, x As Single, y As Single, w As Single, h As Single, r, g, b, a As Integer)` | Draw nine-slice with color tint |
| `Framework_NineSlice_SetBorders(configId As Integer, left As Integer, right As Integer, top As Integer, bottom As Integer)` | Update border insets |
| `Framework_NineSlice_DrawEx(configId As Integer, x As Single, y As Single, w As Single, h As Single, rotation As Single, r, g, b, a As Integer)` | Draw with rotation and tint |

### Example: Scalable Dialog Box

```vb
Dim panelTex As Integer = Framework_LoadTexture("ui_panel.png")

' Create nine-slice with 12px borders on all sides
Dim panel As Integer = Framework_NineSlice_Create(panelTex, 12, 12, 12, 12)

' Draw dialog at different sizes - corners stay sharp
Framework_NineSlice_Draw(panel, 100, 100, 300, 200)      ' Small dialog
Framework_NineSlice_Draw(panel, 100, 350, 600, 150)      ' Wide banner

' Tinted variant for warning dialog
Framework_NineSlice_DrawTinted(panel, 200, 200, 400, 250, 255, 200, 200, 255)

' Draw text inside (offset by border size)
Framework_DrawText("Are you sure?", 112, 112, 20, 255, 255, 255, 255)

' Cleanup
Framework_NineSlice_Destroy(panel)
```

---

## Touch Input

Multi-touch input support for touch screens and mobile targets. Touch points are indexed (0 = first finger, etc.).

| Function | Description |
|----------|-------------|
| `Framework_GetTouchPointCount() As Integer` | Get number of active touch points |
| `Framework_GetTouchPointId(index As Integer) As Integer` | Get touch point ID by index |
| `Framework_GetTouchX() As Integer` | Get X position of first touch point |
| `Framework_GetTouchY() As Integer` | Get Y position of first touch point |
| `Framework_GetTouchPosition(index As Integer, ByRef outX As Integer, ByRef outY As Integer)` | Get position of touch point by index |
| `Framework_IsTouchPressed(index As Integer) As Boolean` | Check if touch point was just pressed |
| `Framework_IsTouchReleased(index As Integer) As Boolean` | Check if touch point was just released |
| `Framework_GetTouchDelta(index As Integer, ByRef outDX As Single, ByRef outDY As Single)` | Get touch movement delta since last frame |
| `Framework_GetGestureDetected() As Integer` | Get last detected gesture type |
| `Framework_GetGesturePinchAngle() As Single` | Get pinch gesture angle |

### Example: Touch-Based Movement

```vb
Dim touchCount As Integer = Framework_GetTouchPointCount()

If touchCount > 0 Then
    ' First finger controls movement
    Dim tx As Integer, ty As Integer
    Framework_GetTouchPosition(0, tx, ty)

    ' Move player toward touch point
    Dim dx As Single = tx - playerX
    Dim dy As Single = ty - playerY
    Dim dist As Single = Math.Sqrt(dx * dx + dy * dy)
    If dist > 5 Then
        playerX += (dx / dist) * speed * Framework_GetDeltaTime()
        playerY += (dy / dist) * speed * Framework_GetDeltaTime()
    End If

    ' Second finger fires
    If touchCount > 1 AndAlso Framework_IsTouchPressed(1) Then
        FireBullet()
    End If
End If

' Detect pinch-to-zoom
Dim gesture As Integer = Framework_GetGestureDetected()
If gesture = GESTURE_PINCH Then
    Dim angle As Single = Framework_GetGesturePinchAngle()
    ' Adjust camera zoom
End If
```

---

## Screenshot & Recording

Capture screenshots and record gameplay to file.

| Function | Description |
|----------|-------------|
| `Framework_TakeScreenshot(fileName As String)` | Save a screenshot to file (PNG format) |
| `Framework_StartRecording(fileName As String)` | Start recording frames to file |
| `Framework_StopRecording()` | Stop recording |
| `Framework_IsRecording() As Boolean` | Check if currently recording |

### Example: Screenshot on Key Press

```vb
' Take screenshot with F12
If Framework_IsKeyPressed(KEY_F12) Then
    Dim fileName As String = "screenshot_" & DateTime.Now.ToString("yyyyMMdd_HHmmss") & ".png"
    Framework_TakeScreenshot(fileName)
End If

' Toggle recording with F9
If Framework_IsKeyPressed(KEY_F9) Then
    If Framework_IsRecording() Then
        Framework_StopRecording()
    Else
        Framework_StartRecording("gameplay.gif")
    End If
End If

' Show recording indicator
If Framework_IsRecording() Then
    Framework_DrawCircle(760, 20, 8, 255, 0, 0, 255)
    Framework_DrawText("REC", 730, 12, 16, 255, 0, 0, 255)
End If
```

---

## Constants & Enums

### Engine State

```vb
Const ENGINE_STOPPED  As Integer = 0
Const ENGINE_RUNNING  As Integer = 1
Const ENGINE_PAUSED   As Integer = 2
Const ENGINE_QUITTING As Integer = 3
```

### Component Types

```vb
Const COMP_TRANSFORM2D   As Integer = 0
Const COMP_SPRITE2D      As Integer = 1
Const COMP_NAME          As Integer = 2
Const COMP_TAG           As Integer = 3
Const COMP_HIERARCHY     As Integer = 4
Const COMP_VELOCITY2D    As Integer = 5
Const COMP_BOXCOLLIDER2D As Integer = 6
Const COMP_ENABLED       As Integer = 7
```

### String Limits

```vb
Const FW_NAME_MAX As Integer = 64   ' Max entity name length
Const FW_TAG_MAX  As Integer = 32   ' Max entity tag length
Const FW_PATH_MAX As Integer = 128  ' Max file path length
```

### Mouse Buttons

```vb
Const MOUSE_BUTTON_LEFT   As Integer = 0
Const MOUSE_BUTTON_RIGHT  As Integer = 1
Const MOUSE_BUTTON_MIDDLE As Integer = 2
```

### Texture Filter

```vb
Const TEXTURE_FILTER_POINT           As Integer = 0
Const TEXTURE_FILTER_BILINEAR        As Integer = 1
Const TEXTURE_FILTER_TRILINEAR       As Integer = 2
Const TEXTURE_FILTER_ANISOTROPIC_4X  As Integer = 3
Const TEXTURE_FILTER_ANISOTROPIC_8X  As Integer = 4
Const TEXTURE_FILTER_ANISOTROPIC_16X As Integer = 5
```

### Texture Wrap

```vb
Const TEXTURE_WRAP_REPEAT        As Integer = 0
Const TEXTURE_WRAP_CLAMP         As Integer = 1
Const TEXTURE_WRAP_MIRROR_REPEAT As Integer = 2
Const TEXTURE_WRAP_MIRROR_CLAMP  As Integer = 3
```

---

## VB.NET Helper Classes

`RaylibWrapper` provides disposable wrapper classes for common resource types.

### TextureHandle

```vb
Dim tex As New TextureHandle("assets/player.png")

' Draw methods
tex.Draw(x, y, tint)
tex.DrawV(position, tint)
tex.DrawEx(position, rotation, scale, tint)
tex.DrawRec(source, position, tint)
tex.DrawPro(source, dest, origin, rotation, tint)

' Properties
Dim w As Integer = tex.Width
Dim h As Integer = tex.Height
Dim valid As Boolean = tex.IsValid

' Dispose when done (releases the handle)
tex.Dispose()
' Or: Using tex As New TextureHandle("player.png") ... End Using
```

### FontHandle

```vb
Dim font As New FontHandle("assets/fonts/roboto.ttf", 24)

font.DrawText("Hello!", position, fontSize, spacing, tint)
Dim valid As Boolean = font.IsValid

font.Dispose()
```

### MusicHandle

```vb
Dim music As New MusicHandle("assets/audio/theme.ogg")

music.Play()
music.Pause()
music.Stop()
music.SetVolume(0.8F)
music.SetPitch(1.0F)

Dim duration As Single = music.GetDuration()
Dim position As Single = music.GetPosition()

music.Dispose()
```
