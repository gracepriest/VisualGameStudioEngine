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
33. [Constants & Enums](#constants--enums)
34. [VB.NET Helper Classes](#vbnet-helper-classes)

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
