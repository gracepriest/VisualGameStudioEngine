# VisualGameStudioEngine API Reference

A comprehensive 2D game engine framework built on Raylib with VB.NET P/Invoke bindings.

## Table of Contents

1. [Core Framework](#core-framework)
2. [Entity Component System (ECS)](#entity-component-system)
3. [Resource Management](#resource-management)
4. [Scene Management](#scene-management)
5. [Tilemap System](#tilemap-system)
6. [Animation System](#animation-system)
7. [Particle System](#particle-system)
8. [UI System](#ui-system)
9. [Physics System](#physics-system)
10. [Audio Manager](#audio-manager)
11. [Camera System](#camera-system)
12. [Input Manager](#input-manager)
13. [Save/Load System](#saveload-system)
14. [Tweening System](#tweening-system)
15. [Event System](#event-system)
16. [Timer System](#timer-system)
17. [Object Pooling](#object-pooling)
18. [State Machine](#state-machine)
19. [AI & Pathfinding](#ai--pathfinding)
20. [Dialogue System](#dialogue-system)
21. [Inventory System](#inventory-system)
22. [Quest System](#quest-system)
23. [2D Lighting](#2d-lighting)
24. [Screen Effects](#screen-effects)
25. [Localization](#localization)
26. [Achievement System](#achievement-system)
27. [Cutscene System](#cutscene-system)
28. [Leaderboard System](#leaderboard-system)
29. [Sprite Batching](#sprite-batching)
30. [Texture Atlas](#texture-atlas)
31. [Level Editor](#level-editor)
32. [Networking](#networking)
33. [Shader System](#shader-system)
34. [Skeletal Animation](#skeletal-animation)
35. [Command Console](#command-console)

---

## Quick Start Examples

This section provides practical code examples to help you get started quickly.

### Example 1: Basic Game Loop

```vb
Imports RaylibWrapper.FrameworkWrapper

Module MyGame
    Sub Main()
        ' Initialize the framework
        Framework_Initialize(800, 600, "My First Game")
        Framework_SetTargetFPS(60)

        ' Main game loop
        While Not Framework_ShouldClose()
            Framework_BeginFrame()
            Framework_ClearBackground(30, 30, 50, 255)

            ' Draw text
            Framework_DrawText("Hello, World!", 350, 280, 24, 255, 255, 255, 255)

            Framework_EndFrame()
        End While

        Framework_Shutdown()
    End Sub
End Module
```

### Example 2: Creating and Moving an Entity

```vb
' Create a player entity with a sprite
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(player, "Player")

' Load a texture and add sprite
Dim texture As Integer = Framework_LoadTextureH("player.png")
Framework_Ecs_AddSprite(player, texture)
Framework_Ecs_SetPosition(player, 400, 300)

' In your update loop - simple WASD movement
Dim speed As Single = 200.0F * deltaTime
If Framework_IsKeyDown(Keys.W) Then Framework_Ecs_Translate(player, 0, -speed)
If Framework_IsKeyDown(Keys.S) Then Framework_Ecs_Translate(player, 0, speed)
If Framework_IsKeyDown(Keys.A) Then Framework_Ecs_Translate(player, -speed, 0)
If Framework_IsKeyDown(Keys.D) Then Framework_Ecs_Translate(player, speed, 0)

' Draw all entities
Framework_Ecs_DrawAll()
```

### Example 3: Physics Bodies and Collision

```vb
' Initialize physics
Framework_Physics_Initialize()
Framework_Physics_SetGravity(0, 500)

' Create a dynamic player body
Dim player As Integer = Framework_Ecs_CreateEntity()
Dim playerBody As Integer = Framework_Physics_CreateBody(player, 0) ' 0 = Dynamic
Framework_Physics_AddCircleShape(playerBody, 16, 0, 0)
Framework_Physics_SetBodyPosition(playerBody, 100, 100)

' Create a static ground
Dim ground As Integer = Framework_Ecs_CreateEntity()
Dim groundBody As Integer = Framework_Physics_CreateBody(ground, 1) ' 1 = Static
Framework_Physics_AddBoxShape(groundBody, 800, 32, 0, 0)
Framework_Physics_SetBodyPosition(groundBody, 400, 550)

' Update physics each frame
Framework_Physics_Update(deltaTime)
```

### Example 4: UI Button with Click Handler

```vb
' Create a button
Dim button As Integer = Framework_UI_Create(1) ' 1 = Button type
Framework_UI_SetPosition(button, 300, 200)
Framework_UI_SetSize(button, 200, 50)
Framework_UI_SetText(button, "Click Me!")

' Set button colors
Framework_UI_SetColor(button, 0, 60, 120, 180, 255)  ' Normal
Framework_UI_SetColor(button, 1, 80, 150, 220, 255)  ' Hovered
Framework_UI_SetColor(button, 2, 40, 90, 140, 255)   ' Pressed

' In your update loop
Framework_UI_Update(deltaTime)
If Framework_UI_WasClicked(button) Then
    Console.WriteLine("Button clicked!")
End If

' In your draw loop
Framework_UI_Draw()
```

### Example 5: Audio with Spatial Sound

```vb
' Initialize audio
Framework_InitAudio()

' Load sounds
Dim music As Integer = Framework_LoadMusicH("background.ogg")
Dim sfx As Integer = Framework_LoadSoundH("explosion.wav")

' Play background music with fade-in
Framework_PlayMusicH(music)
Framework_Audio_FadeIn(music, 2.0F)

' Set listener position (usually at player)
Framework_Audio_SetListenerPosition(playerX, playerY)

' Play spatial sound effect at explosion location
Framework_Audio_PlaySpatial(sfx, explosionX, explosionY, 500) ' 500 = max distance
```

### Example 6: Tweening for Smooth Animations

```vb
' Create a tween to move entity from x=100 to x=500 over 2 seconds
Dim tweenId As Integer = Framework_Tween_CreateForEntity(player, 0) ' 0 = PositionX
Framework_Tween_SetTarget(tweenId, 500)
Framework_Tween_SetDuration(tweenId, 2.0F)
Framework_Tween_SetEasing(tweenId, 8) ' 8 = EaseOutBounce
Framework_Tween_Play(tweenId)

' Create a color fade tween
Dim alphaTween As Integer = Framework_Tween_CreateForEntity(player, 6) ' 6 = Alpha
Framework_Tween_SetTarget(alphaTween, 0) ' Fade to invisible
Framework_Tween_SetDuration(alphaTween, 1.5F)
Framework_Tween_SetDelay(alphaTween, 3.0F) ' Wait 3 seconds before starting
Framework_Tween_Play(alphaTween)

' Update tweens each frame
Framework_Tween_Update(deltaTime)
```

### Example 7: Simple A* Pathfinding

```vb
' Create navigation grid (20x15 tiles, 32px each)
Dim navGrid As Integer = Framework_AI_CreateNavGrid(20, 15, 32)

' Mark obstacles as unwalkable
Framework_AI_SetWalkable(navGrid, 5, 5, False)
Framework_AI_SetWalkable(navGrid, 5, 6, False)
Framework_AI_SetWalkable(navGrid, 5, 7, False)

' Find path from (2,2) to (18,12)
Dim pathId As Integer = Framework_AI_FindPath(navGrid, 2, 2, 18, 12)
Dim pathLength As Integer = Framework_AI_GetPathLength(pathId)

' Get path waypoints
For i As Integer = 0 To pathLength - 1
    Dim wx, wy As Single
    Framework_AI_GetPathPoint(pathId, i, wx, wy)
    Console.WriteLine($"Waypoint {i}: ({wx}, {wy})")
Next

' Clean up
Framework_AI_DestroyPath(pathId)
```

### Example 8: Screen Shake Effect

```vb
' Trigger a screen shake (great for impacts/explosions)
Framework_Camera_SetPosition(400, 300)
Framework_Camera_Shake(
    intensity:=15.0F,    ' Maximum offset in pixels
    duration:=0.5F,      ' Duration in seconds
    frequency:=30.0F,    ' Shakes per second
    decay:=True          ' Gradually reduce intensity
)

' Update camera each frame
Framework_Camera_Update(deltaTime)

' In draw loop, use camera transform
Framework_Camera_BeginMode()
    ' Draw game world here (affected by shake)
    Framework_Ecs_DrawAll()
Framework_Camera_EndMode()

' Draw HUD outside camera (not affected by shake)
Framework_DrawText("Score: 1000", 10, 10, 24, 255, 255, 255, 255)
```

### Example 9: Controller Rumble/Vibration

```vb
' Check if gamepad is connected
If Framework_IsGamepadAvailable(0) Then
    ' Impact rumble - quick strong pulse for hits/explosions
    Framework_Input_ImpactRumble(0, 1.0F)

    ' Engine rumble - asymmetric for engine/car effects
    Framework_Input_EngineRumble(0, 0.5F)

    ' Custom pulse pattern
    Framework_Input_PulseGamepad(0, 0.8F, 0.3F) ' intensity, duration

    ' Full control over both motors
    Framework_Input_SetGamepadVibration(0, 0.3F, 0.8F, 1.0F) ' left, right, duration

    ' Check if still vibrating
    If Framework_Input_IsGamepadVibrating(0) Then
        Dim remaining = Framework_Input_GetVibrationTimeRemaining(0)
        Console.WriteLine($"Vibration time remaining: {remaining:F2}s")
    End If
End If
```

### Example 10: Custom Shader Effects

```vb
' Load a built-in shader effect
Dim grayscaleShader As Integer = Framework_Shader_LoadGrayscale()
Dim outlineShader As Integer = Framework_Shader_LoadOutline()

' Configure the outline shader
Framework_Shader_SetVec4ByName(outlineShader, "outlineColor", 1.0F, 0.0F, 0.0F, 1.0F)
Framework_Shader_SetFloatByName(outlineShader, "outlineThickness", 2.0F)

' Apply shader while drawing
Framework_Shader_BeginMode(outlineShader)
    Framework_Ecs_DrawAll() ' All entities drawn with outline
Framework_Shader_EndMode()

' Draw UI without shader
Framework_UI_Draw()

' Clean up shaders when done
Framework_Shader_Unload(grayscaleShader)
Framework_Shader_Unload(outlineShader)
```

---

## Core Framework

### Window & Application

```vb
' Initialize the framework
Framework_Init(width As Integer, height As Integer, title As String)

' Main loop control
Framework_ShouldClose() As Boolean
Framework_BeginFrame()
Framework_EndFrame()
Framework_Shutdown()

' Window properties
Framework_SetTargetFPS(fps As Integer)
Framework_GetScreenWidth() As Integer
Framework_GetScreenHeight() As Integer
Framework_SetFullscreen(fullscreen As Boolean)
Framework_ToggleFullscreen()
```

### Drawing Primitives

```vb
' Clear and background
Framework_ClearBackground(r As Byte, g As Byte, b As Byte, a As Byte)

' Shapes
Framework_DrawRectangle(x As Single, y As Single, w As Single, h As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawRectangleLines(x As Single, y As Single, w As Single, h As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawCircle(x As Single, y As Single, radius As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawLine(x1 As Single, y1 As Single, x2 As Single, y2 As Single, r As Byte, g As Byte, b As Byte, a As Byte)

' Text
Framework_DrawText(text As String, x As Single, y As Single, fontSize As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_DrawTextEx(fontHandle As Integer, text As String, x As Single, y As Single, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte)
```

### Input (Direct)

```vb
' Keyboard
Framework_IsKeyPressed(key As Integer) As Boolean
Framework_IsKeyDown(key As Integer) As Boolean
Framework_IsKeyReleased(key As Integer) As Boolean

' Mouse
Framework_IsMouseButtonPressed(button As Integer) As Boolean
Framework_IsMouseButtonDown(button As Integer) As Boolean
Framework_GetMouseX() As Single
Framework_GetMouseY() As Single
Framework_GetMouseWheelMove() As Single
```

---

## Entity Component System

### Entity Management

```vb
' Create/Destroy
Framework_Ecs_CreateEntity() As Integer
Framework_Ecs_DestroyEntity(entity As Integer)
Framework_Ecs_IsEntityValid(entity As Integer) As Boolean
Framework_Ecs_GetEntityCount() As Integer
Framework_Ecs_DestroyAllEntities()

' Enable/Disable
Framework_Ecs_SetEnabled(entity As Integer, enabled As Boolean)
Framework_Ecs_IsEnabled(entity As Integer) As Boolean
```

### Transform Component

```vb
' Position
Framework_Ecs_SetPosition(entity As Integer, x As Single, y As Single)
Framework_Ecs_GetPosition(entity As Integer, ByRef x As Single, ByRef y As Single)
Framework_Ecs_SetPositionX(entity As Integer, x As Single)
Framework_Ecs_SetPositionY(entity As Integer, y As Single)

' Rotation & Scale
Framework_Ecs_SetRotation(entity As Integer, rotation As Single)
Framework_Ecs_GetRotation(entity As Integer) As Single
Framework_Ecs_SetScale(entity As Integer, scaleX As Single, scaleY As Single)
Framework_Ecs_GetScale(entity As Integer, ByRef scaleX As Single, ByRef scaleY As Single)

' Origin (pivot point)
Framework_Ecs_SetOrigin(entity As Integer, originX As Single, originY As Single)
```

### Sprite Component

```vb
' Add/Remove
Framework_Ecs_AddSprite(entity As Integer, textureHandle As Integer)
Framework_Ecs_HasSprite(entity As Integer) As Boolean
Framework_Ecs_RemoveSprite(entity As Integer)

' Properties
Framework_Ecs_SetSpriteTexture(entity As Integer, textureHandle As Integer)
Framework_Ecs_SetSpriteSourceRect(entity As Integer, x As Single, y As Single, w As Single, h As Single)
Framework_Ecs_SetSpriteTint(entity As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Ecs_SetSpriteFlipX(entity As Integer, flip As Boolean)
Framework_Ecs_SetSpriteFlipY(entity As Integer, flip As Boolean)
Framework_Ecs_SetSpriteLayer(entity As Integer, layer As Integer)
```

### Parent-Child Hierarchy

```vb
Framework_Ecs_SetParent(entity As Integer, parentEntity As Integer)
Framework_Ecs_GetParent(entity As Integer) As Integer
Framework_Ecs_GetChildCount(entity As Integer) As Integer
Framework_Ecs_GetChildAt(entity As Integer, index As Integer) As Integer
Framework_Ecs_DetachFromParent(entity As Integer)
```

### Rendering

```vb
Framework_Ecs_DrawAll()  ' Draw all entities sorted by layer
Framework_Ecs_DrawEntity(entity As Integer)  ' Draw specific entity
```

---

## Resource Management

### Textures

```vb
Framework_LoadTextureH(path As String) As Integer
Framework_AcquireTextureH(handle As Integer)
Framework_ReleaseTextureH(handle As Integer)
Framework_GetTextureWidth(handle As Integer) As Integer
Framework_GetTextureHeight(handle As Integer) As Integer
```

### Fonts

```vb
Framework_LoadFontH(path As String, fontSize As Integer) As Integer
Framework_AcquireFontH(handle As Integer)
Framework_ReleaseFontH(handle As Integer)
```

### Sounds

```vb
Framework_LoadSoundH(path As String) As Integer
Framework_PlaySoundH(handle As Integer)
Framework_StopSoundH(handle As Integer)
Framework_SetSoundVolumeH(handle As Integer, volume As Single)
```

### Music

```vb
Framework_LoadMusicH(path As String) As Integer
Framework_PlayMusicH(handle As Integer)
Framework_StopMusicH(handle As Integer)
Framework_PauseMusicH(handle As Integer)
Framework_ResumeMusicH(handle As Integer)
Framework_SetMusicVolumeH(handle As Integer, volume As Single)
Framework_UpdateMusicH(handle As Integer)
Framework_IsMusicPlayingH(handle As Integer) As Boolean
```

---

## Scene Management

### Scene Callbacks

```vb
' Define scene callbacks structure
Structure SceneCallbacks
    OnEnter As SceneVoidFn
    OnExit As SceneVoidFn
    OnPause As SceneVoidFn
    OnResume As SceneVoidFn
    OnUpdateFixed As SceneUpdateFixedFn
    OnUpdateFrame As SceneUpdateFrameFn
    OnDraw As SceneVoidFn
End Structure
```

### Scene Functions

```vb
Framework_RegisterScene(name As String, callbacks As SceneCallbacks)
Framework_UnregisterScene(name As String)
Framework_PushScene(name As String)
Framework_PopScene()
Framework_SwitchScene(name As String)
Framework_GetCurrentScene() As String
Framework_GetSceneStackSize() As Integer
```

### Transition Types

| Value | Type | Description |
|-------|------|-------------|
| 0 | None | Instant switch |
| 1 | Fade | Fade to black |
| 2 | SlideLeft | Slide left |
| 3 | SlideRight | Slide right |
| 4 | SlideUp | Slide up |
| 5 | SlideDown | Slide down |
| 6 | WipeLeft | Wipe left |
| 7 | WipeRight | Wipe right |
| 8 | CircleIrisIn | Circle iris close |
| 9 | CircleIrisOut | Circle iris open |
| 10 | Pixelate | Pixelate transition |
| 11 | Dissolve | Dissolve effect |

```vb
Framework_Scene_SetTransition(transitionType As Integer, duration As Single, easing As Integer)
Framework_Scene_IsTransitioning() As Boolean
```

---

## Tilemap System

### Tileset

```vb
Framework_Tileset_Create(textureHandle As Integer, tileWidth As Integer, tileHeight As Integer) As Integer
Framework_Tileset_Destroy(tilesetHandle As Integer)
Framework_Tileset_SetTileCollision(tilesetHandle As Integer, tileId As Integer, solid As Boolean)
Framework_Tileset_GetTileCollision(tilesetHandle As Integer, tileId As Integer) As Boolean
```

### Tilemap

```vb
Framework_Tilemap_Create(tilesetHandle As Integer, width As Integer, height As Integer, layer As Integer) As Integer
Framework_Tilemap_Destroy(tilemapHandle As Integer)
Framework_Tilemap_SetTile(tilemapHandle As Integer, x As Integer, y As Integer, tileId As Integer)
Framework_Tilemap_GetTile(tilemapHandle As Integer, x As Integer, y As Integer) As Integer
Framework_Tilemap_Fill(tilemapHandle As Integer, tileId As Integer)
Framework_Tilemap_SetPosition(tilemapHandle As Integer, x As Single, y As Single)
Framework_Tilemap_Draw(tilemapHandle As Integer)
Framework_Tilemap_CheckCollision(tilemapHandle As Integer, x As Single, y As Single, w As Single, h As Single) As Boolean
```

---

## Animation System

### Animation Clips

```vb
Framework_AnimClip_Create(name As String, textureHandle As Integer, frameCount As Integer) As Integer
Framework_AnimClip_Destroy(clipHandle As Integer)
Framework_AnimClip_SetFrame(clipHandle As Integer, frameIndex As Integer, srcX As Single, srcY As Single, srcW As Single, srcH As Single, duration As Single)
Framework_AnimClip_SetLoopMode(clipHandle As Integer, loopMode As Integer)
```

### Loop Modes

| Value | Mode | Description |
|-------|------|-------------|
| 0 | None | Play once and stop |
| 1 | Repeat | Loop forever |
| 2 | PingPong | Play forward then backward |

### Animator Component

```vb
Framework_Ecs_AddAnimator(entity As Integer)
Framework_Ecs_HasAnimator(entity As Integer) As Boolean
Framework_Ecs_SetAnimatorClip(entity As Integer, clipHandle As Integer)
Framework_Ecs_AnimatorPlay(entity As Integer)
Framework_Ecs_AnimatorPause(entity As Integer)
Framework_Ecs_AnimatorStop(entity As Integer)
Framework_Ecs_AnimatorSetSpeed(entity As Integer, speed As Single)
Framework_Ecs_AnimatorIsPlaying(entity As Integer) As Boolean
Framework_Animators_Update(dt As Single)
```

---

## Sprite Sheet System

Grid-based sprite sheet tools for easy frame extraction and animation creation.

### Sheet Creation

```vb
' Create sprite sheet from texture with uniform grid
Framework_SpriteSheet_Create(textureHandle As Integer, frameWidth As Integer, frameHeight As Integer, columns As Integer, rows As Integer, paddingX As Integer, paddingY As Integer) As Integer
Framework_SpriteSheet_Destroy(sheetId As Integer)
Framework_SpriteSheet_DestroyAll()
Framework_SpriteSheet_IsValid(sheetId As Integer) As Boolean
Framework_SpriteSheet_GetCount() As Integer
```

### Frame Access

```vb
' Get frame info
Framework_SpriteSheet_GetTextureHandle(sheetId As Integer) As Integer
Framework_SpriteSheet_GetFrameCount(sheetId As Integer) As Integer
Framework_SpriteSheet_GetColumns(sheetId As Integer) As Integer
Framework_SpriteSheet_GetRows(sheetId As Integer) As Integer
Framework_SpriteSheet_GetFrameSize(sheetId As Integer, ByRef width As Integer, ByRef height As Integer)

' Get frame rectangle by index (left-to-right, top-to-bottom)
Framework_SpriteSheet_GetFrameRect(sheetId As Integer, frameIndex As Integer, ByRef x As Single, ByRef y As Single, ByRef w As Single, ByRef h As Single)

' Get frame rectangle by row/column
Framework_SpriteSheet_GetFrameRectRC(sheetId As Integer, row As Integer, col As Integer, ByRef x As Single, ByRef y As Single, ByRef w As Single, ByRef h As Single)
```

### Animation Integration

```vb
' Create animation clip from sprite sheet
Framework_AnimClip_CreateFromSheet(name As String, sheetId As Integer, startFrame As Integer, frameCount As Integer, frameDuration As Single, loopMode As Integer) As Integer

' Create animation from a specific row
Framework_AnimClip_CreateFromSheetRow(name As String, sheetId As Integer, row As Integer, startCol As Integer, frameCount As Integer, frameDuration As Single, loopMode As Integer) As Integer
```

### Quick Draw

```vb
' Draw a frame directly without entity
Framework_SpriteSheet_DrawFrame(sheetId As Integer, frameIndex As Integer, x As Single, y As Single, r As Byte, g As Byte, b As Byte, a As Byte)
```

### Example Usage

```vb
' Load a 4x4 sprite sheet (16 frames, 32x32 each)
Dim texture = Framework_LoadTextureH("player_walk.png")
Dim sheet = Framework_SpriteSheet_Create(texture, 32, 32, 4, 4, 0, 0)

' Create walk animation from first row (frames 0-3)
Dim walkClip = Framework_AnimClip_CreateFromSheet("walk", sheet, 0, 4, 0.1F, 1)

' Or from a specific row
Dim runClip = Framework_AnimClip_CreateFromSheetRow("run", sheet, 1, 0, 4, 0.08F, 1)

' Attach to entity
Framework_Ecs_AddAnimator(player)
Framework_Ecs_SetAnimatorClip(player, walkClip)
Framework_Ecs_AnimatorPlay(player)
```

---

## Particle System

### Emitter Component

```vb
Framework_Ecs_AddParticleEmitter(entity As Integer, textureHandle As Integer)
Framework_Ecs_HasParticleEmitter(entity As Integer) As Boolean
Framework_Ecs_RemoveParticleEmitter(entity As Integer)
```

### Configuration

```vb
Framework_Ecs_SetEmitterRate(entity As Integer, particlesPerSecond As Single)
Framework_Ecs_SetEmitterLifetime(entity As Integer, minLife As Single, maxLife As Single)
Framework_Ecs_SetEmitterVelocity(entity As Integer, minVx As Single, minVy As Single, maxVx As Single, maxVy As Single)
Framework_Ecs_SetEmitterColorStart(entity As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Ecs_SetEmitterColorEnd(entity As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Ecs_SetEmitterSize(entity As Integer, startSize As Single, endSize As Single)
Framework_Ecs_SetEmitterGravity(entity As Integer, gx As Single, gy As Single)
Framework_Ecs_SetEmitterSpread(entity As Integer, angleDegrees As Single)
Framework_Ecs_SetEmitterDirection(entity As Integer, dirX As Single, dirY As Single)
Framework_Ecs_SetEmitterMaxParticles(entity As Integer, maxParticles As Integer)
```

### Control

```vb
Framework_Ecs_EmitterStart(entity As Integer)
Framework_Ecs_EmitterStop(entity As Integer)
Framework_Ecs_EmitterBurst(entity As Integer, count As Integer)
Framework_Ecs_EmitterIsActive(entity As Integer) As Boolean
Framework_Ecs_EmitterGetParticleCount(entity As Integer) As Integer

' Update and draw all particles
Framework_Particles_Update(dt As Single)
Framework_Particles_Draw()
```

---

## UI System

### Element Types

| Type | Description |
|------|-------------|
| Label | Text display |
| Button | Clickable button |
| Panel | Container |
| Slider | Value slider |
| Checkbox | Toggle |
| TextInput | Text entry |
| ProgressBar | Progress display |
| Image | Texture display |

### Creating Elements

```vb
Framework_UI_CreateLabel(text As String, x As Single, y As Single) As Integer
Framework_UI_CreateButton(text As String, x As Single, y As Single, width As Single, height As Single) As Integer
Framework_UI_CreatePanel(x As Single, y As Single, width As Single, height As Single) As Integer
Framework_UI_CreateSlider(x As Single, y As Single, width As Single, minVal As Single, maxVal As Single, initialVal As Single) As Integer
Framework_UI_CreateCheckbox(text As String, x As Single, y As Single, initialState As Boolean) As Integer
Framework_UI_CreateTextInput(x As Single, y As Single, width As Single, height As Single, placeholder As String) As Integer
Framework_UI_CreateProgressBar(x As Single, y As Single, width As Single, height As Single, initialValue As Single) As Integer
Framework_UI_CreateImage(textureHandle As Integer, x As Single, y As Single, width As Single, height As Single) As Integer
Framework_UI_Destroy(elementId As Integer)
```

### Anchor Points

| Value | Anchor |
|-------|--------|
| 0 | TopLeft |
| 1 | TopCenter |
| 2 | TopRight |
| 3 | MiddleLeft |
| 4 | MiddleCenter |
| 5 | MiddleRight |
| 6 | BottomLeft |
| 7 | BottomCenter |
| 8 | BottomRight |

### Properties

```vb
Framework_UI_SetPosition(elementId As Integer, x As Single, y As Single)
Framework_UI_SetSize(elementId As Integer, width As Single, height As Single)
Framework_UI_SetAnchor(elementId As Integer, anchor As Integer)
Framework_UI_SetVisible(elementId As Integer, visible As Boolean)
Framework_UI_SetEnabled(elementId As Integer, enabled As Boolean)
Framework_UI_SetParent(elementId As Integer, parentId As Integer)
Framework_UI_SetLayer(elementId As Integer, layer As Integer)
Framework_UI_SetText(elementId As Integer, text As String)
Framework_UI_SetValue(elementId As Integer, value As Single)
Framework_UI_GetValue(elementId As Integer) As Single
Framework_UI_SetChecked(elementId As Integer, checked As Boolean)
Framework_UI_IsChecked(elementId As Integer) As Boolean
```

### Styling

```vb
Framework_UI_SetBackgroundColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_UI_SetTextColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_UI_SetBorderColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_UI_SetHoverColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_UI_SetPressedColor(elementId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_UI_SetBorderWidth(elementId As Integer, width As Single)
Framework_UI_SetCornerRadius(elementId As Integer, radius As Single)
Framework_UI_SetPadding(elementId As Integer, left As Single, top As Single, right As Single, bottom As Single)
```

### Update/Draw

```vb
Framework_UI_Update()
Framework_UI_Draw()
Framework_UI_GetHovered() As Integer
Framework_UI_GetFocused() As Integer
```

---

## Physics System

### Body Types

| Value | Type | Description |
|-------|------|-------------|
| 0 | Static | Immovable |
| 1 | Dynamic | Fully simulated |
| 2 | Kinematic | Controlled movement |

### World Settings

```vb
Framework_Physics_SetGravity(gx As Single, gy As Single)
Framework_Physics_GetGravity(ByRef gx As Single, ByRef gy As Single)
Framework_Physics_SetEnabled(enabled As Boolean)
Framework_Physics_IsEnabled() As Boolean
```

### Body Management

```vb
Framework_Physics_CreateBody(bodyType As Integer, x As Single, y As Single) As Integer
Framework_Physics_DestroyBody(bodyHandle As Integer)
Framework_Physics_IsBodyValid(bodyHandle As Integer) As Boolean
Framework_Physics_DestroyAllBodies()
```

### Body Properties

```vb
Framework_Physics_SetBodyPosition(bodyHandle As Integer, x As Single, y As Single)
Framework_Physics_GetBodyPosition(bodyHandle As Integer, ByRef x As Single, ByRef y As Single)
Framework_Physics_SetBodyVelocity(bodyHandle As Integer, vx As Single, vy As Single)
Framework_Physics_GetBodyVelocity(bodyHandle As Integer, ByRef vx As Single, ByRef vy As Single)
Framework_Physics_ApplyForce(bodyHandle As Integer, fx As Single, fy As Single)
Framework_Physics_ApplyImpulse(bodyHandle As Integer, ix As Single, iy As Single)
Framework_Physics_SetBodyMass(bodyHandle As Integer, mass As Single)
Framework_Physics_SetBodyRestitution(bodyHandle As Integer, restitution As Single)
Framework_Physics_SetBodyFriction(bodyHandle As Integer, friction As Single)
Framework_Physics_SetBodyGravityScale(bodyHandle As Integer, scale As Single)
```

### Collision Shapes

```vb
Framework_Physics_SetBodyCircle(bodyHandle As Integer, radius As Single)
Framework_Physics_SetBodyBox(bodyHandle As Integer, width As Single, height As Single)
Framework_Physics_SetBodyTrigger(bodyHandle As Integer, isTrigger As Boolean)
```

### Entity Binding

```vb
Framework_Physics_BindToEntity(bodyHandle As Integer, entityId As Integer)
Framework_Physics_GetBoundEntity(bodyHandle As Integer) As Integer
Framework_Physics_GetEntityBody(entityId As Integer) As Integer
```

### Simulation

```vb
Framework_Physics_Step(dt As Single)
Framework_Physics_SyncToEntities()
Framework_Physics_SetDebugDraw(enabled As Boolean)
Framework_Physics_DrawDebug()
```

### Queries

```vb
Framework_Physics_RaycastFirst(startX As Single, startY As Single, dirX As Single, dirY As Single, maxDist As Single, ByRef hitX As Single, ByRef hitY As Single, ByRef hitNormalX As Single, ByRef hitNormalY As Single) As Integer
Framework_Physics_QueryCircle(x As Single, y As Single, radius As Single, bodyBuffer() As Integer, bufferSize As Integer) As Integer
Framework_Physics_QueryBox(x As Single, y As Single, width As Single, height As Single, bodyBuffer() As Integer, bufferSize As Integer) As Integer
```

---

## Audio Manager

### Audio Groups

| Value | Group |
|-------|-------|
| 0 | Master |
| 1 | Music |
| 2 | SFX |
| 3 | Voice |
| 4 | Ambient |

### Volume Control

```vb
Framework_Audio_SetGroupVolume(group As Integer, volume As Single)
Framework_Audio_GetGroupVolume(group As Integer) As Single
Framework_Audio_SetGroupMuted(group As Integer, muted As Boolean)
Framework_Audio_IsGroupMuted(group As Integer) As Boolean
Framework_Audio_SetMasterVolume(volume As Single)
Framework_Audio_GetMasterVolume() As Single
```

### Spatial Audio

```vb
Framework_Audio_SetListenerPosition(x As Single, y As Single)
Framework_Audio_PlaySoundAt(soundHandle As Integer, x As Single, y As Single) As Integer
Framework_Audio_SetSoundPosition(playId As Integer, x As Single, y As Single)
Framework_Audio_SetMaxDistance(distance As Single)
Framework_Audio_SetRolloffFactor(factor As Single)
```

### Music Streaming

```vb
Framework_Audio_PlayMusic(musicHandle As Integer)
Framework_Audio_StopMusic()
Framework_Audio_PauseMusic()
Framework_Audio_ResumeMusic()
Framework_Audio_CrossfadeTo(musicHandle As Integer, duration As Single)
Framework_Audio_FadeIn(duration As Single)
Framework_Audio_FadeOut(duration As Single)
```

---

## Camera System

### Basic Camera

```vb
Framework_Camera_SetPosition(x As Single, y As Single)
Framework_Camera_GetPosition(ByRef x As Single, ByRef y As Single)
Framework_Camera_SetZoom(zoom As Single)
Framework_Camera_GetZoom() As Single
Framework_Camera_SetRotation(rotation As Single)
Framework_Camera_GetRotation() As Single
```

### Camera Rendering

```vb
Framework_Camera_BeginMode()  ' Apply camera transform
Framework_Camera_EndMode()    ' Restore normal rendering
```

### Smooth Follow

```vb
Framework_Camera_SetTarget(entity As Integer)
Framework_Camera_SetFollowSpeed(speed As Single)
Framework_Camera_SetDeadzone(width As Single, height As Single)
Framework_Camera_SetLookahead(enabled As Boolean, amount As Single)
```

### Effects

```vb
Framework_Camera_Shake(intensity As Single, duration As Single)
Framework_Camera_ShakeWithFrequency(intensity As Single, duration As Single, frequency As Single)
Framework_Camera_SetShakeDecay(decay As Single)
Framework_Camera_StopShake()
Framework_Camera_IsShaking() As Boolean
Framework_Camera_Flash(r As Byte, g As Byte, b As Byte, duration As Single)
```

### Bounds & Zoom

```vb
Framework_Camera_SetBounds(minX As Single, minY As Single, maxX As Single, maxY As Single)
Framework_Camera_ClearBounds()
Framework_Camera_SetZoomLimits(minZoom As Single, maxZoom As Single)
Framework_Camera_ZoomTo(targetZoom As Single, duration As Single, easing As Integer)
Framework_Camera_ZoomAtPoint(targetZoom As Single, worldX As Single, worldY As Single, duration As Single)
```

### Update

```vb
Framework_Camera_Update(dt As Single)
```

---

## Input Manager

### Actions

```vb
Framework_Input_CreateAction(name As String) As Integer
Framework_Input_DestroyAction(actionHandle As Integer)
Framework_Input_GetAction(name As String) As Integer
```

### Bindings

```vb
Framework_Input_BindKey(actionHandle As Integer, keyCode As Integer)
Framework_Input_UnbindKey(actionHandle As Integer, keyCode As Integer)
Framework_Input_BindMouseButton(actionHandle As Integer, button As Integer)
Framework_Input_BindGamepadButton(actionHandle As Integer, button As Integer)
Framework_Input_BindGamepadAxis(actionHandle As Integer, axis As Integer, scale As Single)
```

### Queries

```vb
Framework_Input_IsActionPressed(actionHandle As Integer) As Boolean
Framework_Input_IsActionDown(actionHandle As Integer) As Boolean
Framework_Input_IsActionReleased(actionHandle As Integer) As Boolean
Framework_Input_GetActionValue(actionHandle As Integer) As Single
```

### Configuration

```vb
Framework_Input_SetActionDeadzone(actionHandle As Integer, deadzone As Single)
Framework_Input_SetActionSensitivity(actionHandle As Integer, sensitivity As Single)
```

### Gamepad

```vb
Framework_Input_IsGamepadAvailable(gamepadId As Integer) As Boolean
Framework_Input_GetGamepadName(gamepadId As Integer) As String
```

### Rumble/Vibration (XInput-based for Windows)

```vb
' Core vibration control
Framework_Input_SetGamepadVibration(gamepadId As Integer, leftMotor As Single, rightMotor As Single, duration As Single)
Framework_Input_StopGamepadVibration(gamepadId As Integer)

' Convenience patterns (NEW)
Framework_Input_PulseGamepad(gamepadId As Integer, intensity As Single, duration As Single)
  ' Quick pulse on both motors - good for pickups, small hits
Framework_Input_ImpactRumble(gamepadId As Integer, intensity As Single)
  ' Heavy left motor rumble for explosions, big hits (0.15s duration)
Framework_Input_EngineRumble(gamepadId As Integer, intensity As Single)
  ' Continuous right motor for engines, ongoing effects (no timeout)

' State queries
Framework_Input_IsGamepadVibrating(gamepadId As Integer) As Boolean
Framework_Input_GetVibrationTimeRemaining(gamepadId As Integer) As Single
```

### Rebinding

```vb
Framework_Input_StartListening(actionHandle As Integer)
Framework_Input_IsListening() As Boolean
Framework_Input_StopListening()
Framework_Input_WasBindingCaptured() As Boolean
```

### Persistence

```vb
Framework_Input_SaveBindings(filename As String) As Boolean
Framework_Input_LoadBindings(filename As String) As Boolean
```

---

## Save/Load System

### Save Slots

```vb
Framework_Save_SetDirectory(directory As String)
Framework_Save_SlotExists(slot As Integer) As Boolean
Framework_Save_DeleteSlot(slot As Integer) As Boolean
```

### Saving Data

```vb
Framework_Save_BeginSave(slot As Integer) As Boolean
Framework_Save_WriteInt(key As String, value As Integer)
Framework_Save_WriteFloat(key As String, value As Single)
Framework_Save_WriteBool(key As String, value As Boolean)
Framework_Save_WriteString(key As String, value As String)
Framework_Save_WriteVector2(key As String, x As Single, y As Single)
Framework_Save_EndSave() As Boolean
```

### Loading Data

```vb
Framework_Save_BeginLoad(slot As Integer) As Boolean
Framework_Save_ReadInt(key As String, defaultValue As Integer) As Integer
Framework_Save_ReadFloat(key As String, defaultValue As Single) As Single
Framework_Save_ReadBool(key As String, defaultValue As Boolean) As Boolean
Framework_Save_ReadString(key As String, defaultValue As String) As String
Framework_Save_EndLoad() As Boolean
```

### Auto-Save

```vb
Framework_Save_SetAutoSaveEnabled(enabled As Boolean)
Framework_Save_SetAutoSaveInterval(seconds As Single)
Framework_Save_SetAutoSaveSlot(slot As Integer)
Framework_Save_TriggerAutoSave()
Framework_Save_Update(dt As Single)
```

### Settings (Persistent)

```vb
Framework_Settings_SetInt(key As String, value As Integer)
Framework_Settings_GetInt(key As String, defaultValue As Integer) As Integer
Framework_Settings_SetFloat(key As String, value As Single)
Framework_Settings_GetFloat(key As String, defaultValue As Single) As Single
Framework_Settings_SetBool(key As String, value As Boolean)
Framework_Settings_GetBool(key As String, defaultValue As Boolean) As Boolean
Framework_Settings_Save() As Boolean
Framework_Settings_Load() As Boolean
```

---

## Tweening System

### Easing Types

| Value | Easing | Description |
|-------|--------|-------------|
| 0 | Linear | Constant speed |
| 1 | QuadIn | Accelerate |
| 2 | QuadOut | Decelerate |
| 3 | QuadInOut | Accel then decel |
| 4-6 | Cubic* | Cubic easing |
| 7-9 | Quart* | Quartic easing |
| 10-12 | Quint* | Quintic easing |
| 13-15 | Sine* | Sinusoidal |
| 16-18 | Expo* | Exponential |
| 19-21 | Circ* | Circular |
| 22-24 | Back* | Overshoot |
| 25-27 | Elastic* | Elastic spring |
| 28-30 | Bounce* | Bouncing |

### Creating Tweens

```vb
Framework_Tween_Float(from As Single, to As Single, duration As Single, easing As Integer) As Integer
Framework_Tween_FloatTo(ByRef target As Single, to As Single, duration As Single, easing As Integer) As Integer
Framework_Tween_Vector2(fromX As Single, fromY As Single, toX As Single, toY As Single, duration As Single, easing As Integer) As Integer
Framework_Tween_Color(fromR As Byte, fromG As Byte, fromB As Byte, fromA As Byte, toR As Byte, toG As Byte, toB As Byte, toA As Byte, duration As Single, easing As Integer) As Integer
```

### Tween Control

```vb
Framework_Tween_Play(tweenId As Integer)
Framework_Tween_Pause(tweenId As Integer)
Framework_Tween_Resume(tweenId As Integer)
Framework_Tween_Stop(tweenId As Integer)
Framework_Tween_Restart(tweenId As Integer)
Framework_Tween_Kill(tweenId As Integer)
Framework_Tween_Complete(tweenId As Integer)
```

### Configuration

```vb
Framework_Tween_SetDelay(tweenId As Integer, delay As Single)
Framework_Tween_SetLoopMode(tweenId As Integer, loopMode As Integer)  ' 0=None, 1=Restart, 2=Yoyo
Framework_Tween_SetLoopCount(tweenId As Integer, count As Integer)   ' -1=infinite
Framework_Tween_SetTimeScale(tweenId As Integer, scale As Single)
Framework_Tween_SetAutoKill(tweenId As Integer, autoKill As Boolean)
```

### Value Getters

```vb
Framework_Tween_GetFloat(tweenId As Integer) As Single
Framework_Tween_GetVector2(tweenId As Integer, ByRef x As Single, ByRef y As Single)
Framework_Tween_GetProgress(tweenId As Integer) As Single
Framework_Tween_IsPlaying(tweenId As Integer) As Boolean
Framework_Tween_IsCompleted(tweenId As Integer) As Boolean
```

### Sequences

```vb
Framework_Tween_CreateSequence() As Integer
Framework_Tween_SequenceAppend(seqId As Integer, tweenId As Integer)
Framework_Tween_SequenceJoin(seqId As Integer, tweenId As Integer)
Framework_Tween_SequenceAppendDelay(seqId As Integer, delay As Single)
Framework_Tween_PlaySequence(seqId As Integer)
```

### Entity Convenience

```vb
Framework_Tween_EntityPosition(entity As Integer, toX As Single, toY As Single, duration As Single, easing As Integer) As Integer
Framework_Tween_EntityRotation(entity As Integer, toRotation As Single, duration As Single, easing As Integer) As Integer
Framework_Tween_EntityScale(entity As Integer, toScaleX As Single, toScaleY As Single, duration As Single, easing As Integer) As Integer
Framework_Tween_EntityAlpha(entity As Integer, toAlpha As Byte, duration As Single, easing As Integer) As Integer
```

### Global

```vb
Framework_Tween_Update(dt As Single)
Framework_Tween_PauseAll()
Framework_Tween_ResumeAll()
Framework_Tween_KillAll()
Framework_Tween_SetGlobalTimeScale(scale As Single)
```

---

## Event System

### Event Registration

```vb
Framework_Event_Register(eventName As String) As Integer
Framework_Event_GetId(eventName As String) As Integer
Framework_Event_Exists(eventName As String) As Boolean
```

### Subscribing

```vb
Framework_Event_Subscribe(eventId As Integer, callback As EventCallback, userData As IntPtr) As Integer
Framework_Event_SubscribeInt(eventId As Integer, callback As EventCallbackInt, userData As IntPtr) As Integer
Framework_Event_SubscribeOnce(eventId As Integer, callback As EventCallback, userData As IntPtr) As Integer
Framework_Event_Unsubscribe(subscriptionId As Integer)
```

### Publishing

```vb
Framework_Event_Publish(eventId As Integer)
Framework_Event_PublishInt(eventId As Integer, value As Integer)
Framework_Event_PublishFloat(eventId As Integer, value As Single)
Framework_Event_PublishString(eventId As Integer, value As String)
Framework_Event_PublishEntity(eventId As Integer, entity As Integer)
```

### Queued Events

```vb
Framework_Event_Queue(eventId As Integer)
Framework_Event_QueueDelayed(eventId As Integer, delay As Single)
Framework_Event_ProcessQueue(dt As Single)
Framework_Event_ClearQueue()
```

---

## Timer System

### One-Shot Timers

```vb
Framework_Timer_After(delay As Single, callback As TimerCallback, userData As IntPtr) As Integer
Framework_Timer_AfterInt(delay As Single, callback As TimerCallbackInt, value As Integer, userData As IntPtr) As Integer
```

### Repeating Timers

```vb
Framework_Timer_Every(interval As Single, callback As TimerCallback, userData As IntPtr) As Integer
Framework_Timer_EveryLimit(interval As Single, repeatCount As Integer, callback As TimerCallback, userData As IntPtr) As Integer
Framework_Timer_AfterThenEvery(delay As Single, interval As Single, callback As TimerCallback, userData As IntPtr) As Integer
```

### Control

```vb
Framework_Timer_Cancel(timerId As Integer)
Framework_Timer_Pause(timerId As Integer)
Framework_Timer_Resume(timerId As Integer)
Framework_Timer_Reset(timerId As Integer)
```

### Queries

```vb
Framework_Timer_IsValid(timerId As Integer) As Boolean
Framework_Timer_IsRunning(timerId As Integer) As Boolean
Framework_Timer_GetElapsed(timerId As Integer) As Single
Framework_Timer_GetRemaining(timerId As Integer) As Single
```

### Timer Sequences

```vb
Framework_Timer_CreateSequence() As Integer
Framework_Timer_SequenceAppend(seqId As Integer, delay As Single, callback As TimerCallback, userData As IntPtr)
Framework_Timer_SequenceStart(seqId As Integer)
Framework_Timer_SequenceSetLoop(seqId As Integer, loop As Boolean)
```

### Frame-Based

```vb
Framework_Timer_AfterFrames(frames As Integer, callback As TimerCallback, userData As IntPtr) As Integer
Framework_Timer_EveryFrames(frames As Integer, callback As TimerCallback, userData As IntPtr) As Integer
```

---

## Object Pooling

### Pool Creation

```vb
Framework_Pool_Create(poolName As String, initialCapacity As Integer, maxCapacity As Integer) As Integer
Framework_Pool_GetByName(poolName As String) As Integer
Framework_Pool_Destroy(poolId As Integer)
```

### Configuration

```vb
Framework_Pool_SetAutoGrow(poolId As Integer, autoGrow As Boolean)
Framework_Pool_SetGrowAmount(poolId As Integer, amount As Integer)
```

### Acquire/Release

```vb
Framework_Pool_Acquire(poolId As Integer) As Integer
Framework_Pool_Release(poolId As Integer, objectIndex As Integer)
Framework_Pool_ReleaseAll(poolId As Integer)
```

### Queries

```vb
Framework_Pool_GetCapacity(poolId As Integer) As Integer
Framework_Pool_GetActiveCount(poolId As Integer) As Integer
Framework_Pool_GetAvailableCount(poolId As Integer) As Integer
Framework_Pool_IsEmpty(poolId As Integer) As Boolean
Framework_Pool_IsFull(poolId As Integer) As Boolean
```

### Entity Pools

```vb
Framework_Pool_CreateEntityPool(poolName As String, prefabId As Integer, initialCapacity As Integer, maxCapacity As Integer) As Integer
Framework_Pool_AcquireEntity(poolId As Integer) As Integer
Framework_Pool_ReleaseEntity(poolId As Integer, entity As Integer)
```

---

## State Machine

### FSM Creation

```vb
Framework_FSM_Create(name As String) As Integer
Framework_FSM_CreateForEntity(name As String, entity As Integer) As Integer
Framework_FSM_Destroy(fsmId As Integer)
```

### States

```vb
Framework_FSM_AddState(fsmId As Integer, stateName As String) As Integer
Framework_FSM_GetState(fsmId As Integer, stateName As String) As Integer
Framework_FSM_RemoveState(fsmId As Integer, stateId As Integer)
```

### Transitions

```vb
Framework_FSM_AddTransition(fsmId As Integer, fromState As Integer, toState As Integer) As Integer
Framework_FSM_AddAnyTransition(fsmId As Integer, toState As Integer) As Integer
Framework_FSM_TransitionTo(fsmId As Integer, stateId As Integer) As Boolean
Framework_FSM_TryTransition(fsmId As Integer, toState As Integer) As Boolean
```

### Control

```vb
Framework_FSM_SetInitialState(fsmId As Integer, stateId As Integer)
Framework_FSM_Start(fsmId As Integer)
Framework_FSM_Stop(fsmId As Integer)
Framework_FSM_Pause(fsmId As Integer)
Framework_FSM_Resume(fsmId As Integer)
```

### Queries

```vb
Framework_FSM_GetCurrentState(fsmId As Integer) As Integer
Framework_FSM_GetPreviousState(fsmId As Integer) As Integer
Framework_FSM_GetTimeInState(fsmId As Integer) As Single
Framework_FSM_IsRunning(fsmId As Integer) As Boolean
```

### Triggers

```vb
Framework_FSM_AddTrigger(fsmId As Integer, triggerName As String, fromState As Integer, toState As Integer) As Integer
Framework_FSM_FireTrigger(fsmId As Integer, triggerName As String)
```

### Update

```vb
Framework_FSM_Update(fsmId As Integer, deltaTime As Single)
Framework_FSM_UpdateAll(deltaTime As Single)
```

---

## AI & Pathfinding

### Navigation Grid

```vb
Framework_NavGrid_Create(width As Integer, height As Integer, cellSize As Single) As Integer
Framework_NavGrid_Destroy(gridId As Integer)
Framework_NavGrid_SetOrigin(gridId As Integer, x As Single, y As Single)
Framework_NavGrid_SetWalkable(gridId As Integer, cellX As Integer, cellY As Integer, walkable As Boolean)
Framework_NavGrid_SetCost(gridId As Integer, cellX As Integer, cellY As Integer, cost As Single)
Framework_NavGrid_SetRect(gridId As Integer, x As Integer, y As Integer, w As Integer, h As Integer, walkable As Boolean)
```

### A* Pathfinding

```vb
Framework_Path_Find(gridId As Integer, startX As Single, startY As Single, endX As Single, endY As Single) As Integer
Framework_Path_Destroy(pathId As Integer)
Framework_Path_GetLength(pathId As Integer) As Integer
Framework_Path_GetWaypoint(pathId As Integer, index As Integer, ByRef x As Single, ByRef y As Single)
Framework_Path_Smooth(pathId As Integer)
Framework_Path_SetHeuristic(gridId As Integer, heuristic As Integer)  ' 0=Manhattan, 1=Euclidean, 2=Chebyshev
```

### Steering Behaviors

| Value | Behavior |
|-------|----------|
| 1 | Seek |
| 2 | Flee |
| 3 | Arrive |
| 4 | Pursue |
| 5 | Evade |
| 6 | Wander |
| 7 | PathFollow |
| 8 | ObstacleAvoid |
| 9 | Separation |
| 10 | Alignment |
| 11 | Cohesion |

```vb
Framework_Steer_CreateAgent(entity As Integer) As Integer
Framework_Steer_DestroyAgent(agentId As Integer)
Framework_Steer_SetMaxSpeed(agentId As Integer, maxSpeed As Single)
Framework_Steer_SetMaxForce(agentId As Integer, maxForce As Single)
Framework_Steer_EnableBehavior(agentId As Integer, behavior As Integer, enabled As Boolean)
Framework_Steer_SetBehaviorWeight(agentId As Integer, behavior As Integer, weight As Single)
Framework_Steer_SetTargetPosition(agentId As Integer, x As Single, y As Single)
Framework_Steer_SetTargetEntity(agentId As Integer, targetEntity As Integer)
Framework_Steer_SetPath(agentId As Integer, pathId As Integer)
Framework_Steer_Update(agentId As Integer, deltaTime As Single)
Framework_Steer_UpdateAll(deltaTime As Single)
```

### Debug

```vb
Framework_NavGrid_DrawDebug(gridId As Integer)
Framework_Path_DrawDebug(pathId As Integer, r As Byte, g As Byte, b As Byte)
Framework_Steer_DrawDebug(agentId As Integer)
```

---

## Dialogue System

### Creating Dialogues

```vb
Framework_Dialogue_Create(name As String) As Integer
Framework_Dialogue_Destroy(dialogueId As Integer)
Framework_Dialogue_AddNode(dialogueId As Integer, nodeTag As String) As Integer
Framework_Dialogue_SetNodeSpeaker(dialogueId As Integer, nodeId As Integer, speaker As String)
Framework_Dialogue_SetNodeText(dialogueId As Integer, nodeId As Integer, text As String)
Framework_Dialogue_SetNodePortrait(dialogueId As Integer, nodeId As Integer, textureHandle As Integer)
Framework_Dialogue_SetNextNode(dialogueId As Integer, nodeId As Integer, nextNodeId As Integer)
Framework_Dialogue_SetStartNode(dialogueId As Integer, nodeId As Integer)
```

### Choices

```vb
Framework_Dialogue_AddChoice(dialogueId As Integer, nodeId As Integer, choiceText As String, targetNodeId As Integer) As Integer
Framework_Dialogue_GetChoiceCount(dialogueId As Integer, nodeId As Integer) As Integer
Framework_Dialogue_SetChoiceCondition(dialogueId As Integer, nodeId As Integer, choiceIndex As Integer, condition As String)
```

### Variables

```vb
Framework_Dialogue_SetVarInt(varName As String, value As Integer)
Framework_Dialogue_GetVarInt(varName As String) As Integer
Framework_Dialogue_SetVarBool(varName As String, value As Boolean)
Framework_Dialogue_GetVarBool(varName As String) As Boolean
Framework_Dialogue_SetVarString(varName As String, value As String)
```

### Playback

```vb
Framework_Dialogue_Start(dialogueId As Integer)
Framework_Dialogue_Stop()
Framework_Dialogue_IsActive() As Boolean
Framework_Dialogue_Continue() As Boolean
Framework_Dialogue_SelectChoice(choiceIndex As Integer) As Boolean
```

### Current Node

```vb
Framework_Dialogue_GetCurrentSpeaker() As String
Framework_Dialogue_GetCurrentText() As String
Framework_Dialogue_GetCurrentPortrait() As Integer
Framework_Dialogue_GetCurrentChoiceCount() As Integer
Framework_Dialogue_GetCurrentChoiceText(choiceIndex As Integer) As String
```

### Typewriter Effect

```vb
Framework_Dialogue_SetTypewriterEnabled(enabled As Boolean)
Framework_Dialogue_SetTypewriterSpeed(charsPerSecond As Single)
Framework_Dialogue_SkipTypewriter()
Framework_Dialogue_IsTypewriterComplete() As Boolean
Framework_Dialogue_GetVisibleText() As String
Framework_Dialogue_Update(dt As Single)
```

---

## Inventory System

### Item Definition

```vb
Framework_Item_Define(itemName As String) As Integer
Framework_Item_SetDisplayName(itemDefId As Integer, displayName As String)
Framework_Item_SetDescription(itemDefId As Integer, description As String)
Framework_Item_SetIcon(itemDefId As Integer, textureHandle As Integer)
Framework_Item_SetStackable(itemDefId As Integer, stackable As Boolean)
Framework_Item_SetMaxStack(itemDefId As Integer, maxStack As Integer)
Framework_Item_SetCategory(itemDefId As Integer, category As String)
Framework_Item_SetRarity(itemDefId As Integer, rarity As Integer)  ' 0-4: Common to Legendary
Framework_Item_SetEquipSlot(itemDefId As Integer, equipSlot As Integer)
Framework_Item_SetUsable(itemDefId As Integer, usable As Boolean)
Framework_Item_SetConsumable(itemDefId As Integer, consumable As Boolean)
Framework_Item_SetValue(itemDefId As Integer, value As Integer)
Framework_Item_SetWeight(itemDefId As Integer, weight As Single)
```

### Inventory Container

```vb
Framework_Inventory_Create(name As String, slotCount As Integer) As Integer
Framework_Inventory_Destroy(inventoryId As Integer)
Framework_Inventory_AddItem(inventoryId As Integer, itemDefId As Integer, quantity As Integer) As Boolean
Framework_Inventory_RemoveItem(inventoryId As Integer, itemDefId As Integer, quantity As Integer) As Boolean
Framework_Inventory_ClearSlot(inventoryId As Integer, slotIndex As Integer)
Framework_Inventory_GetItemAt(inventoryId As Integer, slotIndex As Integer) As Integer
Framework_Inventory_GetQuantityAt(inventoryId As Integer, slotIndex As Integer) As Integer
Framework_Inventory_HasItem(inventoryId As Integer, itemDefId As Integer) As Boolean
Framework_Inventory_CountItem(inventoryId As Integer, itemDefId As Integer) As Integer
Framework_Inventory_MoveItem(inventoryId As Integer, fromSlot As Integer, toSlot As Integer) As Boolean
Framework_Inventory_Sort(inventoryId As Integer)
```

### Equipment

```vb
Framework_Equipment_Create(name As String) As Integer
Framework_Equipment_Equip(equipId As Integer, itemDefId As Integer, slot As Integer) As Boolean
Framework_Equipment_Unequip(equipId As Integer, slot As Integer) As Integer
Framework_Equipment_GetItemAt(equipId As Integer, slot As Integer) As Integer
Framework_Equipment_GetTotalStatInt(equipId As Integer, statName As String) As Integer
```

### Loot Tables

```vb
Framework_LootTable_Create(name As String) As Integer
Framework_LootTable_AddEntry(tableId As Integer, itemDefId As Integer, weight As Single, minQty As Integer, maxQty As Integer)
Framework_LootTable_Roll(tableId As Integer, ByRef outQuantity As Integer) As Integer
```

---

## Quest System

### Quest Definition

```vb
Framework_Quest_Define(questId As String) As Integer
Framework_Quest_SetName(questHandle As Integer, name As String)
Framework_Quest_SetDescription(questHandle As Integer, description As String)
Framework_Quest_SetCategory(questHandle As Integer, category As String)
Framework_Quest_SetLevel(questHandle As Integer, level As Integer)
Framework_Quest_SetTimeLimit(questHandle As Integer, seconds As Single)
```

### Objectives

| Type | Description |
|------|-------------|
| 0 | Custom |
| 1 | Kill |
| 2 | Collect |
| 3 | Talk |
| 4 | Reach |
| 5 | Interact |
| 6 | Escort |
| 7 | Defend |
| 8 | Explore |

```vb
Framework_Quest_AddObjective(questHandle As Integer, objectiveType As Integer, description As String, requiredCount As Integer) As Integer
Framework_Quest_SetObjectiveTarget(questHandle As Integer, objectiveIndex As Integer, targetId As String)
Framework_Quest_SetObjectiveProgress(questHandle As Integer, objectiveIndex As Integer, progress As Integer)
```

### Rewards

```vb
Framework_Quest_AddRewardItem(questHandle As Integer, itemDefId As Integer, quantity As Integer)
Framework_Quest_SetRewardExperience(questHandle As Integer, experience As Integer)
Framework_Quest_SetRewardCurrency(questHandle As Integer, currencyType As Integer, amount As Integer)
```

### State Management

```vb
Framework_Quest_Start(questHandle As Integer) As Boolean
Framework_Quest_Complete(questHandle As Integer) As Boolean
Framework_Quest_Fail(questHandle As Integer) As Boolean
Framework_Quest_GetState(questHandle As Integer) As Integer  ' 0=NotStarted, 1=InProgress, 2=Completed, 3=Failed
Framework_Quest_IsActive(questHandle As Integer) As Boolean
```

### Auto-Progress Reporting

```vb
Framework_Quest_ReportKill(targetType As String, count As Integer)
Framework_Quest_ReportCollect(itemDefId As Integer, count As Integer)
Framework_Quest_ReportTalk(npcId As String)
Framework_Quest_ReportLocation(x As Single, y As Single)
```

### Tracking

```vb
Framework_Quest_SetTracked(questHandle As Integer, tracked As Boolean)
Framework_Quest_IsTracked(questHandle As Integer) As Boolean
Framework_Quest_GetTrackedCount() As Integer
```

---

## 2D Lighting

### System Control

```vb
Framework_Lighting_Initialize(width As Integer, height As Integer)
Framework_Lighting_Shutdown()
Framework_Lighting_SetEnabled(enabled As Boolean)
Framework_Lighting_SetAmbientColor(r As Byte, g As Byte, b As Byte)
Framework_Lighting_SetAmbientIntensity(intensity As Single)
```

### Point Lights

```vb
Framework_Light_CreatePoint(x As Single, y As Single, radius As Single) As Integer
Framework_Light_Destroy(lightId As Integer)
Framework_Light_SetPosition(lightId As Integer, x As Single, y As Single)
Framework_Light_SetColor(lightId As Integer, r As Byte, g As Byte, b As Byte)
Framework_Light_SetIntensity(lightId As Integer, intensity As Single)
Framework_Light_SetRadius(lightId As Integer, radius As Single)
Framework_Light_SetEnabled(lightId As Integer, enabled As Boolean)
Framework_Light_SetFlicker(lightId As Integer, amount As Single, speed As Single)
Framework_Light_SetPulse(lightId As Integer, minIntensity As Single, maxIntensity As Single, speed As Single)
Framework_Light_AttachToEntity(lightId As Integer, entityId As Integer, offsetX As Single, offsetY As Single)
```

### Spot Lights

```vb
Framework_Light_CreateSpot(x As Single, y As Single, radius As Single, angle As Single, coneAngle As Single) As Integer
Framework_Light_SetDirection(lightId As Integer, angle As Single)
Framework_Light_SetConeAngle(lightId As Integer, angle As Single)
```

### Shadow Occluders

```vb
Framework_Shadow_CreateBox(x As Single, y As Single, width As Single, height As Single) As Integer
Framework_Shadow_CreateCircle(x As Single, y As Single, radius As Single) As Integer
Framework_Shadow_Destroy(occluderId As Integer)
Framework_Shadow_SetPosition(occluderId As Integer, x As Single, y As Single)
Framework_Shadow_AttachToEntity(occluderId As Integer, entityId As Integer, offsetX As Single, offsetY As Single)
Framework_Lighting_SetShadowQuality(quality As Integer)  ' 0=None, 1=Hard, 2=Soft
```

### Day/Night Cycle

```vb
Framework_Lighting_SetTimeOfDay(time As Single)  ' 0-24 hours
Framework_Lighting_GetTimeOfDay() As Single
Framework_Lighting_SetDayNightSpeed(speed As Single)
Framework_Lighting_SetDayNightEnabled(enabled As Boolean)
Framework_Lighting_SetSunriseTime(hour As Single)
Framework_Lighting_SetSunsetTime(hour As Single)
```

### Rendering

```vb
Framework_Lighting_BeginLightPass()
' Draw your lit objects here
Framework_Lighting_EndLightPass()
Framework_Lighting_RenderToScreen()
Framework_Lighting_Update(deltaTime As Single)
```

---

## Screen Effects

### System Control

```vb
Framework_Effects_Initialize(width As Integer, height As Integer)
Framework_Effects_Shutdown()
Framework_Effects_SetEnabled(enabled As Boolean)
```

### Vignette

```vb
Framework_Effects_SetVignetteEnabled(enabled As Boolean)
Framework_Effects_SetVignetteIntensity(intensity As Single)
Framework_Effects_SetVignetteRadius(radius As Single)
Framework_Effects_SetVignetteSoftness(softness As Single)
Framework_Effects_SetVignetteColor(r As Byte, g As Byte, b As Byte)
```

### Blur

```vb
Framework_Effects_SetBlurEnabled(enabled As Boolean)
Framework_Effects_SetBlurAmount(amount As Single)
Framework_Effects_SetBlurIterations(iterations As Integer)
```

### Chromatic Aberration

```vb
Framework_Effects_SetChromaticEnabled(enabled As Boolean)
Framework_Effects_SetChromaticOffset(offset As Single)
```

### Color Effects

```vb
Framework_Effects_SetGrayscaleEnabled(enabled As Boolean)
Framework_Effects_SetGrayscaleAmount(amount As Single)
Framework_Effects_SetSepiaEnabled(enabled As Boolean)
Framework_Effects_SetSepiaAmount(amount As Single)
Framework_Effects_SetInvertEnabled(enabled As Boolean)
Framework_Effects_SetBrightness(brightness As Single)
Framework_Effects_SetContrast(contrast As Single)
Framework_Effects_SetSaturation(saturation As Single)
```

### Flash/Fade/Shake

```vb
Framework_Effects_Flash(r As Byte, g As Byte, b As Byte, duration As Single)
Framework_Effects_FlashWhite(duration As Single)
Framework_Effects_FlashDamage(duration As Single)
Framework_Effects_FadeIn(duration As Single)
Framework_Effects_FadeOut(duration As Single)
Framework_Effects_FadeToColor(r As Byte, g As Byte, b As Byte, duration As Single)
Framework_Effects_Shake(intensity As Single, duration As Single)
Framework_Effects_ShakeDecay(intensity As Single, duration As Single, decay As Single)
```

### Rendering

```vb
Framework_Effects_BeginCapture()
' Draw your scene here
Framework_Effects_EndCapture()
Framework_Effects_Apply()

' Or use simple overlay mode
Framework_Effects_DrawOverlays(screenWidth As Integer, screenHeight As Integer)
Framework_Effects_Update(deltaTime As Single)
```

### Presets

```vb
Framework_Effects_ApplyPresetRetro()
Framework_Effects_ApplyPresetDream()
Framework_Effects_ApplyPresetHorror()
Framework_Effects_ApplyPresetNoir()
Framework_Effects_ResetAll()
```

---

## Localization

### System Control

```vb
Framework_Locale_Initialize()
Framework_Locale_LoadLanguage(languageCode As String, filePath As String) As Boolean
Framework_Locale_SetLanguage(languageCode As String) As Boolean
Framework_Locale_GetCurrentLanguage() As String
Framework_Locale_GetLanguageCount() As Integer
```

### String Retrieval

```vb
Framework_Locale_GetString(key As String) As String
Framework_Locale_GetStringDefault(key As String, defaultValue As String) As String
Framework_Locale_Format(key As String, arg1 As String) As String
Framework_Locale_Format2(key As String, arg1 As String, arg2 As String) As String
Framework_Locale_HasString(key As String) As Boolean
```

---

## Achievement System

### Creating Achievements

```vb
Framework_Achievement_Create(id As String, name As String, description As String) As Integer
Framework_Achievement_SetIcon(achievementId As Integer, textureHandle As Integer)
Framework_Achievement_SetHidden(achievementId As Integer, hidden As Boolean)
Framework_Achievement_SetPoints(achievementId As Integer, points As Integer)
```

### Progress Achievements

```vb
Framework_Achievement_SetProgressTarget(achievementId As Integer, target As Integer)
Framework_Achievement_SetProgress(achievementId As Integer, progress As Integer)
Framework_Achievement_AddProgress(achievementId As Integer, amount As Integer)
Framework_Achievement_GetProgress(achievementId As Integer) As Integer
Framework_Achievement_GetProgressPercent(achievementId As Integer) As Single
```

### Unlock/Lock

```vb
Framework_Achievement_Unlock(achievementId As Integer)
Framework_Achievement_Lock(achievementId As Integer)
Framework_Achievement_IsUnlocked(achievementId As Integer) As Boolean
```

### Notifications

```vb
Framework_Achievement_SetNotificationsEnabled(enabled As Boolean)
Framework_Achievement_SetNotificationDuration(seconds As Single)
Framework_Achievement_Update(deltaTime As Single)
Framework_Achievement_DrawNotifications()
```

### Persistence

```vb
Framework_Achievement_Save(filePath As String) As Boolean
Framework_Achievement_Load(filePath As String) As Boolean
Framework_Achievement_ResetAll()
```

---

## Cutscene System

### Creating Cutscenes

```vb
Framework_Cutscene_Create(name As String) As Integer
Framework_Cutscene_Destroy(cutsceneId As Integer)
```

### Commands

```vb
Framework_Cutscene_AddWait(cutsceneId As Integer, duration As Single)
Framework_Cutscene_AddDialogue(cutsceneId As Integer, speaker As String, text As String, duration As Single)
Framework_Cutscene_AddMoveActor(cutsceneId As Integer, entityId As Integer, targetX As Single, targetY As Single, duration As Single)
Framework_Cutscene_AddFadeIn(cutsceneId As Integer, duration As Single)
Framework_Cutscene_AddFadeOut(cutsceneId As Integer, duration As Single)
Framework_Cutscene_AddPlaySound(cutsceneId As Integer, soundHandle As Integer)
Framework_Cutscene_AddPlayMusic(cutsceneId As Integer, musicPath As String)
Framework_Cutscene_AddCameraPan(cutsceneId As Integer, targetX As Single, targetY As Single, duration As Single)
Framework_Cutscene_AddCameraZoom(cutsceneId As Integer, targetZoom As Single, duration As Single)
Framework_Cutscene_AddShake(cutsceneId As Integer, intensity As Single, duration As Single)
Framework_Cutscene_AddSetVisible(cutsceneId As Integer, entityId As Integer, visible As Boolean)
```

### Playback

```vb
Framework_Cutscene_Play(cutsceneId As Integer)
Framework_Cutscene_Pause(cutsceneId As Integer)
Framework_Cutscene_Resume(cutsceneId As Integer)
Framework_Cutscene_Stop(cutsceneId As Integer)
Framework_Cutscene_Skip(cutsceneId As Integer)
Framework_Cutscene_SetSkippable(cutsceneId As Integer, skippable As Boolean)
```

### Queries

```vb
Framework_Cutscene_IsPlaying(cutsceneId As Integer) As Boolean
Framework_Cutscene_IsFinished(cutsceneId As Integer) As Boolean
Framework_Cutscene_GetProgress(cutsceneId As Integer) As Single
```

### Update & Draw

```vb
Framework_Cutscene_Update(deltaTime As Single)
Framework_Cutscene_DrawDialogue()
```

---

## Leaderboard System

### Creating Leaderboards

```vb
Framework_Leaderboard_Create(name As String, sortOrder As Integer, maxEntries As Integer) As Integer
Framework_Leaderboard_Destroy(leaderboardId As Integer)
Framework_Leaderboard_Clear(leaderboardId As Integer)
```

### Score Submission

```vb
Framework_Leaderboard_SubmitScore(leaderboardId As Integer, playerName As String, score As Integer) As Integer
Framework_Leaderboard_SubmitScoreEx(leaderboardId As Integer, playerName As String, score As Integer, metadata As String) As Integer
Framework_Leaderboard_IsHighScore(leaderboardId As Integer, score As Integer) As Boolean
Framework_Leaderboard_GetRankForScore(leaderboardId As Integer, score As Integer) As Integer
```

### Queries

```vb
Framework_Leaderboard_GetEntryCount(leaderboardId As Integer) As Integer
Framework_Leaderboard_GetEntryName(leaderboardId As Integer, rank As Integer) As String
Framework_Leaderboard_GetEntryScore(leaderboardId As Integer, rank As Integer) As Integer
Framework_Leaderboard_GetEntryDate(leaderboardId As Integer, rank As Integer) As String
Framework_Leaderboard_GetPlayerRank(leaderboardId As Integer, playerName As String) As Integer
Framework_Leaderboard_GetPlayerBestScore(leaderboardId As Integer, playerName As String) As Integer
Framework_Leaderboard_GetTopScore(leaderboardId As Integer) As Integer
Framework_Leaderboard_GetTopPlayer(leaderboardId As Integer) As String
```

### Persistence

```vb
Framework_Leaderboard_Save(leaderboardId As Integer, filePath As String) As Boolean
Framework_Leaderboard_Load(leaderboardId As Integer, filePath As String) As Boolean
```

---

## Sprite Batching

Reduce draw calls by grouping sprites with same texture into batched renders.

### Batch Creation

```vb
Framework_Batch_Create(maxSprites As Integer) As Integer
Framework_Batch_Destroy(batchId As Integer)
Framework_Batch_Clear(batchId As Integer)
```

### Adding Sprites

```vb
Framework_Batch_AddSprite(batchId As Integer, textureHandle As Integer, x As Single, y As Single, width As Single, height As Single, srcX As Single, srcY As Single, srcW As Single, srcH As Single, rotation As Single, originX As Single, originY As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Batch_AddSpriteSimple(batchId As Integer, textureHandle As Integer, x As Single, y As Single, r As Byte, g As Byte, b As Byte, a As Byte)
```

### Rendering

```vb
Framework_Batch_Draw(batchId As Integer)
Framework_Batch_DrawSorted(batchId As Integer)  ' Sort by texture then draw
```

### Statistics

```vb
Framework_Batch_GetSpriteCount(batchId As Integer) As Integer
Framework_Batch_GetDrawCallCount(batchId As Integer) As Integer
Framework_Batch_SetAutoCull(batchId As Integer, enabled As Boolean)
```

---

## Texture Atlas

Pack multiple sprites into single textures to reduce texture swaps.

### Atlas Creation

```vb
Framework_Atlas_Create(width As Integer, height As Integer) As Integer
Framework_Atlas_Destroy(atlasId As Integer)
Framework_Atlas_IsValid(atlasId As Integer) As Boolean
```

### Adding Sprites

```vb
Framework_Atlas_AddImage(atlasId As Integer, imagePath As String) As Integer
Framework_Atlas_AddRegion(atlasId As Integer, textureHandle As Integer, srcX As Integer, srcY As Integer, srcW As Integer, srcH As Integer) As Integer
Framework_Atlas_Pack(atlasId As Integer) As Boolean
```

### Querying

```vb
Framework_Atlas_GetSpriteRect(atlasId As Integer, spriteIndex As Integer, ByRef x As Single, ByRef y As Single, ByRef w As Single, ByRef h As Single)
Framework_Atlas_GetSpriteCount(atlasId As Integer) As Integer
Framework_Atlas_GetTextureHandle(atlasId As Integer) As Integer
```

### Drawing

```vb
Framework_Atlas_DrawSprite(atlasId As Integer, spriteIndex As Integer, x As Single, y As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Atlas_DrawSpriteEx(atlasId As Integer, spriteIndex As Integer, x As Single, y As Single, rotation As Single, scale As Single, r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Atlas_DrawSpritePro(atlasId As Integer, spriteIndex As Integer, destX As Single, destY As Single, destW As Single, destH As Single, originX As Single, originY As Single, rotation As Single, r As Byte, g As Byte, b As Byte, a As Byte)
```

### Persistence

```vb
Framework_Atlas_SaveToFile(atlasId As Integer, jsonPath As String, imagePath As String) As Boolean
Framework_Atlas_LoadFromFile(jsonPath As String, imagePath As String) As Integer
```

---

## Level Editor

Load/save levels at runtime in JSON format for easy editing.

### Level Management

```vb
Framework_Level_Create(name As String) As Integer
Framework_Level_Destroy(levelId As Integer)
Framework_Level_IsValid(levelId As Integer) As Boolean
```

### Level Properties

```vb
Framework_Level_SetSize(levelId As Integer, width As Integer, height As Integer)
Framework_Level_GetSize(levelId As Integer, ByRef width As Integer, ByRef height As Integer)
Framework_Level_SetTileSize(levelId As Integer, tileWidth As Integer, tileHeight As Integer)
Framework_Level_SetBackground(levelId As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
```

### Tile Layers

```vb
Framework_Level_AddLayer(levelId As Integer, layerName As String) As Integer
Framework_Level_RemoveLayer(levelId As Integer, layerIndex As Integer)
Framework_Level_GetLayerCount(levelId As Integer) As Integer
Framework_Level_SetTile(levelId As Integer, layerIndex As Integer, x As Integer, y As Integer, tileId As Integer)
Framework_Level_GetTile(levelId As Integer, layerIndex As Integer, x As Integer, y As Integer) As Integer
Framework_Level_FillTiles(levelId As Integer, layerIndex As Integer, x As Integer, y As Integer, w As Integer, h As Integer, tileId As Integer)
Framework_Level_ClearLayer(levelId As Integer, layerIndex As Integer)
```

### Objects

```vb
Framework_Level_AddObject(levelId As Integer, objectType As String, x As Single, y As Single) As Integer
Framework_Level_RemoveObject(levelId As Integer, objectId As Integer)
Framework_Level_SetObjectPosition(levelId As Integer, objectId As Integer, x As Single, y As Single)
Framework_Level_SetObjectProperty(levelId As Integer, objectId As Integer, key As String, value As String)
Framework_Level_GetObjectProperty(levelId As Integer, objectId As Integer, key As String) As String
```

### Collision Shapes

```vb
Framework_Level_AddCollisionRect(levelId As Integer, x As Single, y As Single, w As Single, h As Single)
Framework_Level_AddCollisionCircle(levelId As Integer, x As Single, y As Single, radius As Single)
Framework_Level_ClearCollisions(levelId As Integer)
```

### Persistence

```vb
Framework_Level_SaveToFile(levelId As Integer, filePath As String) As Boolean
Framework_Level_LoadFromFile(filePath As String) As Integer
```

### Rendering

```vb
Framework_Level_Draw(levelId As Integer, tilesetHandle As Integer)
Framework_Level_DrawLayer(levelId As Integer, layerIndex As Integer, tilesetHandle As Integer)
```

### Coordinate Conversion

```vb
' Convert world coordinates to tile coordinates
Framework_Level_WorldToTile(levelId As Integer, worldX As Single, worldY As Single, ByRef tileX As Integer, ByRef tileY As Integer)

' Convert tile coordinates to world coordinates (top-left corner)
Framework_Level_TileToWorld(levelId As Integer, tileX As Integer, tileY As Integer, ByRef worldX As Single, ByRef worldY As Single)

' Convert tile coordinates to world coordinates (center)
Framework_Level_TileToWorldCenter(levelId As Integer, tileX As Integer, tileY As Integer, ByRef worldX As Single, ByRef worldY As Single)
```

### Editing Tools

```vb
' Flood fill area with tile
Framework_Level_FloodFill(levelId As Integer, layerIndex As Integer, x As Integer, y As Integer, newTileId As Integer) As Integer

' Undo/Redo system
Framework_Level_BeginEdit(levelId As Integer)      ' Start edit batch
Framework_Level_EndEdit(levelId As Integer)        ' End edit batch
Framework_Level_Undo(levelId As Integer)
Framework_Level_Redo(levelId As Integer)
Framework_Level_CanUndo(levelId As Integer) As Boolean
Framework_Level_CanRedo(levelId As Integer) As Boolean
Framework_Level_ClearHistory(levelId As Integer)

' Selection and copy/paste
Framework_Level_SetSelection(levelId As Integer, x As Integer, y As Integer, w As Integer, h As Integer)
Framework_Level_ClearSelection(levelId As Integer)
Framework_Level_GetSelectionSize(levelId As Integer, ByRef w As Integer, ByRef h As Integer)
Framework_Level_CopySelection(levelId As Integer, layerIndex As Integer)
Framework_Level_PasteSelection(levelId As Integer, layerIndex As Integer, x As Integer, y As Integer)
```

### Tile Properties

```vb
' Tile collision flags
Framework_Level_SetTileCollision(levelId As Integer, tileX As Integer, tileY As Integer, isSolid As Boolean)
Framework_Level_GetTileCollision(levelId As Integer, tileX As Integer, tileY As Integer) As Boolean
Framework_Level_IsTileSolid(levelId As Integer, tileX As Integer, tileY As Integer) As Boolean

' Raycast for collision
Framework_Level_RaycastTiles(levelId As Integer, startX As Single, startY As Single, dirX As Single, dirY As Single, maxDist As Single, ByRef hitX As Single, ByRef hitY As Single) As Boolean
```

### Auto-Tiling

```vb
' Set auto-tile rules (16 tiles for 4-bit neighbor rules)
Framework_Level_SetAutoTileRules(levelId As Integer, baseTileId As Integer, tileMapping() As Integer)
Framework_Level_ClearAutoTileRules(levelId As Integer, baseTileId As Integer)
Framework_Level_ApplyAutoTiling(levelId As Integer, layerIndex As Integer)
```

---

## Networking

TCP client-server networking for multiplayer games.

### Server

```vb
Framework_Net_CreateServer(port As Integer, maxClients As Integer) As Integer
Framework_Net_DestroyServer(serverId As Integer)
Framework_Net_ServerIsRunning(serverId As Integer) As Boolean
Framework_Net_GetClientCount(serverId As Integer) As Integer
Framework_Net_DisconnectClient(serverId As Integer, clientId As Integer)
Framework_Net_UpdateServer(serverId As Integer)
```

### Client

```vb
Framework_Net_CreateClient() As Integer
Framework_Net_DestroyClient(clientId As Integer)
Framework_Net_Connect(clientId As Integer, host As String, port As Integer) As Boolean
Framework_Net_Disconnect(clientId As Integer)
Framework_Net_IsConnected(clientId As Integer) As Boolean
Framework_Net_UpdateClient(clientId As Integer)
```

### Messaging

```vb
Framework_Net_BroadcastMessage(serverId As Integer, channel As String, data As IntPtr, dataSize As Integer, reliable As Boolean)
Framework_Net_SendToClient(serverId As Integer, clientId As Integer, channel As String, data As IntPtr, dataSize As Integer, reliable As Boolean)
Framework_Net_SendMessage(clientId As Integer, channel As String, data As IntPtr, dataSize As Integer, reliable As Boolean)
```

### Callbacks

```vb
' Server callbacks
Delegate Sub NetConnectCallback(connectionId As Integer, userData As IntPtr)
Delegate Sub NetDisconnectCallback(connectionId As Integer, userData As IntPtr)
Delegate Sub NetMessageCallback(connectionId As Integer, channel As String, data As IntPtr, dataSize As Integer, userData As IntPtr)

Framework_Net_SetOnClientConnected(serverId As Integer, callback As NetConnectCallback, userData As IntPtr)
Framework_Net_SetOnClientDisconnected(serverId As Integer, callback As NetDisconnectCallback, userData As IntPtr)
Framework_Net_SetOnServerMessage(serverId As Integer, callback As NetMessageCallback, userData As IntPtr)

' Client callbacks
Framework_Net_SetOnConnected(clientId As Integer, callback As NetConnectCallback, userData As IntPtr)
Framework_Net_SetOnDisconnected(clientId As Integer, callback As NetDisconnectCallback, userData As IntPtr)
Framework_Net_SetOnMessage(clientId As Integer, callback As NetMessageCallback, userData As IntPtr)
```

### Statistics

```vb
Framework_Net_GetPing(clientId As Integer) As Integer
Framework_Net_GetBytesSent(connectionId As Integer) As Integer
Framework_Net_GetBytesReceived(connectionId As Integer) As Integer
```

---

## Shader System

Custom shader loading and GPU uniform management.

### Loading Shaders

```vb
Framework_Shader_Load(vsPath As String, fsPath As String) As Integer
Framework_Shader_LoadFromMemory(vsCode As String, fsCode As String) As Integer
Framework_Shader_Unload(shaderId As Integer)
Framework_Shader_IsValid(shaderId As Integer) As Boolean
```

### Using Shaders

```vb
Framework_Shader_Begin(shaderId As Integer)
Framework_Shader_End()
```

### Uniform Locations

```vb
Framework_Shader_GetLocation(shaderId As Integer, uniformName As String) As Integer
```

### Setting Uniforms (by location)

```vb
Framework_Shader_SetInt(shaderId As Integer, loc As Integer, value As Integer)
Framework_Shader_SetFloat(shaderId As Integer, loc As Integer, value As Single)
Framework_Shader_SetVec2(shaderId As Integer, loc As Integer, x As Single, y As Single)
Framework_Shader_SetVec3(shaderId As Integer, loc As Integer, x As Single, y As Single, z As Single)
Framework_Shader_SetVec4(shaderId As Integer, loc As Integer, x As Single, y As Single, z As Single, w As Single)
Framework_Shader_SetMat4(shaderId As Integer, loc As Integer, mat As IntPtr)
```

### Setting Uniforms (by name)

```vb
Framework_Shader_SetIntByName(shaderId As Integer, name As String, value As Integer)
Framework_Shader_SetFloatByName(shaderId As Integer, name As String, value As Single)
Framework_Shader_SetVec2ByName(shaderId As Integer, name As String, x As Single, y As Single)
Framework_Shader_SetVec3ByName(shaderId As Integer, name As String, x As Single, y As Single, z As Single)
Framework_Shader_SetVec4ByName(shaderId As Integer, name As String, x As Single, y As Single, z As Single, w As Single)
```

### Built-in Shaders

```vb
' Basic effects
Framework_Shader_LoadGrayscale() As Integer    ' Converts to grayscale
Framework_Shader_LoadBlur() As Integer          ' 5x5 box blur
Framework_Shader_LoadCRT() As Integer           ' CRT monitor effect with scanlines

' Advanced effects (NEW)
Framework_Shader_LoadOutline() As Integer       ' Edge detection outline
  ' Uniforms: outlineColor (vec4), outlineThickness (float)
Framework_Shader_LoadGlow() As Integer          ' Soft glow effect
  ' Uniforms: glowIntensity (float), glowRadius (float)
Framework_Shader_LoadDistortion() As Integer    ' Wavy distortion
  ' Uniforms: time (float), distortionStrength (float), waveFrequency (float)
Framework_Shader_LoadChromatic() As Integer     ' RGB channel separation
  ' Uniforms: aberrationAmount (float)
Framework_Shader_LoadPixelate() As Integer      ' Retro pixelation
  ' Uniforms: pixelSize (float)

' Post-processing effects (NEW)
Framework_Shader_LoadVignette() As Integer      ' Darken screen edges
  ' Uniforms: vignetteRadius (float), vignetteSoftness (float), vignetteIntensity (float)
Framework_Shader_LoadBloom() As Integer         ' Bright area glow
  ' Uniforms: bloomThreshold (float), bloomIntensity (float), bloomSpread (float)
Framework_Shader_LoadWave() As Integer          ' Wavy distortion effect
  ' Uniforms: waveAmplitude (float), waveFrequency (float), waveSpeed (float), time (float)
Framework_Shader_LoadSharpen() As Integer       ' Edge enhancement
  ' Uniforms: sharpenAmount (float)
Framework_Shader_LoadFilmGrain() As Integer     ' Noise/grain effect
  ' Uniforms: grainIntensity (float), grainSize (float), time (float)
Framework_Shader_LoadColorAdjust() As Integer   ' Color grading
  ' Uniforms: brightness (float), contrast (float), saturation (float)
```

---

## Skeletal Animation

Bone-based character animation with sprite attachments.

### Skeleton Creation

```vb
Framework_Skeleton_Create(name As String) As Integer
Framework_Skeleton_Destroy(skeletonId As Integer)
Framework_Skeleton_IsValid(skeletonId As Integer) As Boolean
```

### Bones

```vb
' Returns boneId (0 = root, -1 = error)
Framework_Skeleton_AddBone(skeletonId As Integer, boneName As String, parentBoneId As Integer, x As Single, y As Single, rotation As Single, length As Single) As Integer
Framework_Skeleton_GetBoneCount(skeletonId As Integer) As Integer
Framework_Skeleton_GetBoneId(skeletonId As Integer, boneName As String) As Integer
```

### Bone Transforms

```vb
Framework_Skeleton_SetBoneLocalTransform(skeletonId As Integer, boneId As Integer, x As Single, y As Single, rotation As Single, scaleX As Single, scaleY As Single)
Framework_Skeleton_GetBoneWorldPosition(skeletonId As Integer, boneId As Integer, ByRef x As Single, ByRef y As Single)
Framework_Skeleton_GetBoneWorldRotation(skeletonId As Integer, boneId As Integer) As Single
Framework_Skeleton_ComputeWorldTransforms(skeletonId As Integer)
```

### Sprite Attachments

```vb
Framework_Skeleton_AttachSprite(skeletonId As Integer, boneId As Integer, textureHandle As Integer, offsetX As Single, offsetY As Single, originX As Single, originY As Single, width As Single, height As Single)
Framework_Skeleton_DetachSprite(skeletonId As Integer, boneId As Integer)
Framework_Skeleton_SetSpriteSourceRect(skeletonId As Integer, boneId As Integer, srcX As Single, srcY As Single, srcW As Single, srcH As Single)
```

### Animation Creation

```vb
Framework_Skeleton_CreateAnimation(skeletonId As Integer, animName As String, duration As Single) As Integer
Framework_Skeleton_DestroyAnimation(skeletonId As Integer, animId As Integer)
Framework_Skeleton_GetAnimationId(skeletonId As Integer, animName As String) As Integer
```

### Keyframes

```vb
Framework_Skeleton_AddKeyframe(skeletonId As Integer, animId As Integer, boneId As Integer, time As Single, x As Single, y As Single, rotation As Single, scaleX As Single, scaleY As Single)
```

### Playback

```vb
Framework_Skeleton_PlayAnimation(skeletonId As Integer, animId As Integer, loop As Boolean)
Framework_Skeleton_StopAnimation(skeletonId As Integer)
Framework_Skeleton_PauseAnimation(skeletonId As Integer)
Framework_Skeleton_ResumeAnimation(skeletonId As Integer)
Framework_Skeleton_SetAnimationSpeed(skeletonId As Integer, speed As Single)
Framework_Skeleton_GetAnimationTime(skeletonId As Integer) As Single
Framework_Skeleton_SetAnimationTime(skeletonId As Integer, time As Single)
Framework_Skeleton_IsAnimationPlaying(skeletonId As Integer) As Boolean
```

### Blending

```vb
Framework_Skeleton_CrossfadeTo(skeletonId As Integer, animId As Integer, duration As Single, loop As Boolean)
Framework_Skeleton_SetBlendWeight(skeletonId As Integer, animIdA As Integer, animIdB As Integer, weight As Single)
```

### Pose

```vb
Framework_Skeleton_SetPose(skeletonId As Integer, boneId As Integer, x As Single, y As Single, rotation As Single, scaleX As Single, scaleY As Single)
Framework_Skeleton_ResetPose(skeletonId As Integer)
```

### Update & Draw

```vb
Framework_Skeleton_Update(skeletonId As Integer, dt As Single)
Framework_Skeleton_UpdateAll(dt As Single)
Framework_Skeleton_Draw(skeletonId As Integer, x As Single, y As Single, scaleX As Single, scaleY As Single, rotation As Single)
Framework_Skeleton_DrawDebug(skeletonId As Integer, x As Single, y As Single, scale As Single)
```

---

## Command Console

Runtime command console with variables and callbacks.

### Initialization

```vb
Framework_Cmd_Init()
Framework_Cmd_Shutdown()
```

### Registering Commands

```vb
Delegate Sub CmdConsoleCallback(args As String, userData As IntPtr)

Framework_Cmd_RegisterCommand(cmdName As String, description As String, callback As CmdConsoleCallback, userData As IntPtr)
Framework_Cmd_UnregisterCommand(cmdName As String)
```

### Executing Commands

```vb
Framework_Cmd_Execute(commandLine As String)
Framework_Cmd_ExecuteFile(filePath As String) As Boolean
```

### Console Variables (CVars)

```vb
' Registration
Framework_Cmd_RegisterCvarInt(name As String, defaultValue As Integer, description As String)
Framework_Cmd_RegisterCvarFloat(name As String, defaultValue As Single, description As String)
Framework_Cmd_RegisterCvarBool(name As String, defaultValue As Boolean, description As String)
Framework_Cmd_RegisterCvarString(name As String, defaultValue As String, description As String)

' Getters/Setters
Framework_Cmd_GetCvarInt(name As String) As Integer
Framework_Cmd_GetCvarFloat(name As String) As Single
Framework_Cmd_GetCvarBool(name As String) As Boolean
Framework_Cmd_GetCvarString(name As String) As String
Framework_Cmd_SetCvarInt(name As String, value As Integer)
Framework_Cmd_SetCvarFloat(name As String, value As Single)
Framework_Cmd_SetCvarBool(name As String, value As Boolean)
Framework_Cmd_SetCvarString(name As String, value As String)
```

### Command History

```vb
Framework_Cmd_GetHistoryCount() As Integer
Framework_Cmd_GetHistoryEntry(index As Integer) As String
Framework_Cmd_ClearHistory()
```

### Logging

| Level | Value |
|-------|-------|
| TRACE | 0 |
| DEBUG | 1 |
| INFO | 2 |
| WARN | 3 |
| ERROR | 4 |
| FATAL | 5 |

```vb
Framework_Cmd_Log(level As Integer, message As String)
Framework_Cmd_LogInfo(message As String)
Framework_Cmd_LogWarning(message As String)
Framework_Cmd_LogError(message As String)
Framework_Cmd_LogDebug(message As String)
Framework_Cmd_SetLogLevel(level As Integer)
Framework_Cmd_SetLogToFile(enabled As Boolean, filePath As String)
```

### Visual Console

```vb
Framework_Cmd_SetToggleKey(keyCode As Integer)  ' Default: KEY_GRAVE (`)
Framework_Cmd_Toggle()
Framework_Cmd_Show()
Framework_Cmd_Hide()
Framework_Cmd_IsVisible() As Boolean
```

### Appearance

```vb
Framework_Cmd_SetBackgroundColor(r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Cmd_SetTextColor(r As Byte, g As Byte, b As Byte, a As Byte)
Framework_Cmd_SetFontSize(fontSize As Integer)
Framework_Cmd_SetMaxLines(maxLines As Integer)
```

### Update & Draw

```vb
Framework_Cmd_Update(dt As Single)
Framework_Cmd_Draw()
```

---

## Cleanup

```vb
Framework_ResourcesShutdown()  ' Clean up all resources
```

---

## Version

**VisualGameStudioEngine v1.0**

Built on Raylib 5.5
