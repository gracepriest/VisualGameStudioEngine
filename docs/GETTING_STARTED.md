# Getting Started with VisualGameStudioEngine

A step-by-step guide to creating your first game with the VisualGameStudioEngine framework.

## Prerequisites

- **Visual Studio 2022** (or later) with C++ and VB.NET workloads
- **.NET 8.0 SDK**
- **Windows 10/11** (64-bit)
- **OpenGL 3.3+** compatible GPU

## Project Setup

### Option 1: Use the Existing TestVbDLL Project

The easiest way to start is by modifying the existing `TestVbDLL` project:

1. Open `VisualGameStudioEngine.sln` in Visual Studio
2. Set `TestVbDLL` as the startup project
3. Build the solution (the C++ engine compiles first, then the VB.NET projects)
4. Run to see the demo scenes

### Option 2: Create a New Project

1. Create a new VB.NET Console Application (.NET 8.0)
2. Add a reference to `RaylibWrapper.dll` from the `RaylibWrapper/bin/Release/net8.0` folder
3. Copy `VisualGameStudioEngine.dll` and required Raylib files to your output directory
4. Import the wrapper: `Imports RaylibWrapper.FrameworkWrapper`

## Your First Game

### Step 1: Basic Window

```vb
Imports RaylibWrapper.FrameworkWrapper

Module MyFirstGame
    Sub Main()
        ' Create an 800x600 window
        Framework_Initialize(800, 600, "My First Game")
        Framework_SetTargetFPS(60)

        ' Game loop
        While Not Framework_ShouldClose()
            Framework_Update()          ' Process input and systems

            Framework_BeginDrawing()
            Framework_ClearBackground(30, 30, 50, 255)
            Framework_DrawText("My First Game!", 320, 280, 32, 255, 255, 255, 255)
            Framework_EndDrawing()
        End While

        Framework_Shutdown()
    End Sub
End Module
```

### Step 2: Add a Player Entity

Expand your game with an entity-based player using the ECS:

```vb
Imports RaylibWrapper.FrameworkWrapper

Module MyFirstGame
    Private player As Integer
    Private playerTex As Integer

    Sub Main()
        Framework_Initialize(800, 600, "My First Game")
        Framework_SetTargetFPS(60)

        ' Load texture and create player entity
        playerTex = Framework_AcquireTextureH("assets/player.png")

        player = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(player, "Player")
        Framework_Ecs_SetTag(player, "player")
        Framework_Ecs_AddTransform2D(player, 400, 300, 0, 1, 1)
        Framework_Ecs_AddVelocity2D(player, 0, 0)
        Framework_Ecs_AddBoxCollider2D(player, -16, -16, 32, 32)
        Framework_Ecs_AddSprite2D(player)
        Framework_Ecs_SetSpriteTexture(player, playerTex)
        Framework_Ecs_SetEnabled(player, True)

        While Not Framework_ShouldClose()
            Update()
            Draw()
        End While

        Cleanup()
    End Sub

    Private Sub Update()
        Framework_Update()

        Dim dt As Single = Framework_GetDeltaTime()
        Dim speed As Single = 200.0F * dt

        ' WASD / Arrow key movement
        Dim vx As Single = 0
        Dim vy As Single = 0
        If Framework_IsKeyDown(Keys.W) OrElse Framework_IsKeyDown(Keys.Up)    Then vy = -speed
        If Framework_IsKeyDown(Keys.S) OrElse Framework_IsKeyDown(Keys.Down)  Then vy =  speed
        If Framework_IsKeyDown(Keys.A) OrElse Framework_IsKeyDown(Keys.Left)  Then vx = -speed
        If Framework_IsKeyDown(Keys.D) OrElse Framework_IsKeyDown(Keys.Right) Then vx =  speed

        Framework_Ecs_SetVelocity(player, vx, vy)
        Framework_Ecs_UpdateVelocities()  ' Apply velocity to transform
    End Sub

    Private Sub Draw()
        Framework_BeginDrawing()
        Framework_ClearBackground(30, 30, 50, 255)

        Framework_Ecs_DrawSprites()   ' Render all Sprite2D components

        ' Show position
        Dim px, py As Single
        Framework_Ecs_GetTransformPosition(player, px, py)
        Framework_DrawText($"Position: ({px:F0}, {py:F0})", 10, 10, 20, 255, 255, 255, 255)
        Framework_DrawText("Use WASD or Arrow Keys to move", 10, 36, 16, 180, 180, 180, 255)

        Framework_EndDrawing()
    End Sub

    Private Sub Cleanup()
        Framework_Ecs_DestroyEntity(player)
        Framework_ReleaseTextureH(playerTex)
        Framework_Shutdown()
    End Sub
End Module
```

### Step 3: Collision Detection

Use `BoxCollider2D` components and overlap queries for collision:

```vb
' Setup entities with colliders
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_AddTransform2D(player, 100, 300, 0, 1, 1)
Framework_Ecs_AddBoxCollider2D(player, -16, -16, 32, 32)
Framework_Ecs_SetTag(player, "player")

Dim coin As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_AddTransform2D(coin, 400, 300, 0, 1, 1)
Framework_Ecs_AddBoxCollider2D(coin, -12, -12, 24, 24)
Framework_Ecs_SetTag(coin, "coin")

' In your update loop — check what overlaps the player's area
Dim px, py As Single
Framework_Ecs_GetTransformPosition(player, px, py)

Dim hits(31) As Integer
Dim count As Integer = Framework_Physics_OverlapBox(px - 16, py - 16, 32, 32, hits, 32)

For i As Integer = 0 To count - 1
    If hits(i) <> player AndAlso Framework_Ecs_GetTag(hits(i)) = "coin" Then
        Framework_Ecs_DestroyEntity(hits(i))
        score += 1
    End If
Next
```

### Step 4: Camera Follow

```vb
' Point camera at player with smooth follow
Framework_Camera_SetFollowTarget(player)
Framework_Camera_SetFollowLerp(0.08F)      ' 0 = no smoothing, 1 = instant snap
Framework_Camera_SetFollowEnabled(True)
Framework_Camera_SetBounds(0, 0, 3200, 2400)  ' Clamp to level size

' In your update loop
Framework_Camera_Update()

' Wrap world drawing in camera mode
Framework_BeginDrawing()
Framework_ClearBackground(30, 30, 50, 255)

Framework_Camera_BeginMode()
    Framework_Ecs_DrawSprites()
Framework_Camera_EndMode()

' Draw HUD outside camera (not scrolled)
Framework_DrawText($"Score: {score}", 10, 10, 24, 255, 255, 0, 255)

Framework_EndDrawing()
```

### Step 5: Audio

```vb
Framework_InitAudio()

' Load sounds
Dim jumpSfx As Integer = Framework_LoadSoundH("assets/jump.wav")
Dim bgMusic As Integer = Framework_AcquireMusicH("assets/theme.ogg")

' Play music (call UpdateAllMusic each frame)
Framework_PlayMusicH(bgMusic)
Framework_SetMusicVolumeH(bgMusic, 0.6F)

' In your game loop
Framework_Update()
Framework_UpdateAllMusic()   ' Must be called each frame for streaming music

' Play a sound on jump
If Framework_IsKeyPressed(Keys.Space) Then
    Framework_PlaySoundH(jumpSfx)
End If

' Cleanup
Framework_UnloadSoundH(jumpSfx)
Framework_ReleaseMusicH(bgMusic)
Framework_CloseAudio()
```

---

## Understanding the Architecture

### The Game Loop

Every game follows this pattern:

```vb
While Not Framework_ShouldClose()
    Framework_Update()      ' Process input, advance time

    Framework_BeginDrawing()
    Framework_ClearBackground(r, g, b, a)

    ' --- draw world ---
    Framework_Camera_BeginMode()
        Framework_Ecs_DrawSprites()
    Framework_Camera_EndMode()

    ' --- draw HUD (not scrolled) ---
    Framework_DrawText("Score: 0", 10, 10, 20, 255, 255, 255, 255)

    Framework_EndDrawing()
End While
```

> **Note:** `Framework_Update()` must be called before reading input or delta time each frame.

### Entity Component System (ECS)

Entities are plain integers. Components are added individually:

```vb
' Create entity and add components
Dim enemy As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(enemy, "Grunt")
Framework_Ecs_SetTag(enemy, "enemy")
Framework_Ecs_AddTransform2D(enemy, 200, 100, 0, 1, 1)
Framework_Ecs_AddVelocity2D(enemy, -80, 0)
Framework_Ecs_AddBoxCollider2D(enemy, -16, -16, 32, 32)
Framework_Ecs_AddSprite2D(enemy)
Framework_Ecs_SetSpriteTexture(enemy, enemyTex)
Framework_Ecs_SetSpriteLayer(enemy, 1)    ' Higher = drawn on top
Framework_Ecs_SetEnabled(enemy, True)

' Query position
If Framework_Ecs_IsAlive(enemy) Then
    Dim ex, ey As Single
    Framework_Ecs_GetTransformPosition(enemy, ex, ey)
    Framework_Ecs_SetVelocity(enemy, -80, 0)
End If

' Destroy when done
Framework_Ecs_DestroyEntity(enemy)
```

### Initialization Order

```vb
' 1. Core framework (always first)
Framework_Initialize(width, height, title)
Framework_SetTargetFPS(60)

' 2. Audio (if needed)
Framework_InitAudio()

' 3. Asset root (optional — prepends to all load paths)
Framework_SetAssetRoot("assets/")

' 4. Camera setup (optional)
Framework_Camera_SetFollowEnabled(False)

' 5. Start game loop
While Not Framework_ShouldClose()
    ' ...
End While

' 6. Cleanup in reverse order
Framework_CloseAudio()
Framework_Shutdown()
```

---

## Common Patterns

### Resource Management

Load resources once, reuse handles throughout the game:

```vb
' Load at startup or scene enter
Dim playerTex As Integer = Framework_AcquireTextureH("player.png")
Dim jumpSfx   As Integer = Framework_LoadSoundH("jump.wav")
Dim bgMusic   As Integer = Framework_AcquireMusicH("music.ogg")

' Use throughout the game
Framework_Ecs_SetSpriteTexture(player, playerTex)
Framework_PlaySoundH(jumpSfx)
Framework_PlayMusicH(bgMusic)

' Release at shutdown or scene exit
Framework_ReleaseTextureH(playerTex)
Framework_UnloadSoundH(jumpSfx)
Framework_ReleaseMusicH(bgMusic)
```

Or use the disposable VB.NET helper classes:

```vb
Using tex As New TextureHandle("player.png")
Using font As New FontHandle("ui.ttf", 24)
    ' tex and font auto-release when Using block exits
    tex.Draw(100, 100, Color.White)
    font.DrawText("Hello!", New Vector2(10, 10), 24, 1, Color.White)
End Using
End Using
```

### Sprite Sheet Animation (Manual)

```vb
' 4-frame walk cycle, each frame 32x32, on a 128x32 sprite sheet
Dim walkFrame As Integer = 0
Dim frameTimer As Single = 0
Const FRAME_DURATION As Single = 0.1F

' In update
frameTimer += Framework_GetDeltaTime()
If frameTimer >= FRAME_DURATION Then
    frameTimer = 0
    walkFrame = (walkFrame + 1) Mod 4
End If

' Set sprite source rect to current frame
Dim src As Rectangle = Framework_SpriteFrame(32, 32, walkFrame)
Framework_Ecs_SetSpriteSource(player, src.x, src.y, src.width, src.height)
```

### Scene Management

```vb
' Create scenes at startup
Dim menuScene As Integer = Framework_CreateScriptScene()
Dim gameScene  As Integer = Framework_CreateScriptScene()

' Immediate switch
Framework_SceneChange(gameScene)

' Switch with a fade transition (type=1=Fade, 0.5s, easing=3=EaseInOut)
Framework_Scene_SetTransitionColor(0, 0, 0, 255)
Framework_Scene_ChangeWithTransitionEx(gameScene, 1, 0.5F, 3)

' Push/pop for pause menus
Framework_Scene_PushWithTransition(pauseScene)   ' Overlay pause menu
Framework_Scene_PopWithTransition()              ' Return to game
```

### Camera Effects

```vb
' Smooth follow with deadzone
Framework_Camera_SetFollowTarget(player)
Framework_Camera_SetFollowLerp(0.1F)
Framework_Camera_SetDeadzone(40, 30)
Framework_Camera_SetDeadzoneEnabled(True)
Framework_Camera_SetBounds(0, 0, levelWidth, levelHeight)

' Zoom
Framework_Camera_SetZoom(1.5F)
Framework_Camera_ZoomTo(2.0F, 0.5F)  ' Smooth zoom to 2x over 0.5s

' Impact effects
Framework_Camera_Shake(10.0F, 0.3F)                  ' Simple shake
Framework_Camera_ShakeEx(15.0F, 5.0F, 0.4F, 1.5F)   ' Directional shake
Framework_Camera_Flash(255, 255, 255, 200, 0.15F)     ' White flash

' Camera must be updated each frame when using follow/shake/zoom
Framework_Camera_Update()
```

### Entity Hierarchy

```vb
' Attach a weapon to the player — weapon moves with player
Dim weapon As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_AddTransform2D(weapon, 20, 0, 0, 1, 1)  ' 20px offset from player
Framework_Ecs_SetParent(weapon, player)

' World position is computed through the chain
Dim wx, wy As Single
Framework_Ecs_GetWorldPosition(weapon, wx, wy)

' Detach (returns to world root)
Framework_Ecs_DetachFromParent(weapon)

' Traverse children
Dim child As Integer = Framework_Ecs_GetFirstChild(player)
While child <> -1
    ' process child...
    child = Framework_Ecs_GetNextSibling(child)
End While
```

### Audio Groups and Spatial Sound

```vb
Framework_InitAudio()

' Groups: 0=SFX, 1=Music, 2=Ambient (your own convention)
Dim explosionSfx As Integer = Framework_Audio_LoadSound("explosion.wav", 0)
Dim bgMusic      As Integer = Framework_Audio_LoadMusic("theme.ogg")

' Group volume (e.g. from settings menu)
Framework_Audio_SetGroupVolume(0, 0.8F)  ' SFX at 80%
Framework_Audio_SetGroupVolume(1, 0.5F)  ' Music at 50%

' Spatial sound — falls off with distance
Framework_Audio_SetSpatialEnabled(True)
Framework_Audio_SetSpatialFalloff(100, 600)  ' Full volume within 100px, silent at 600px

' Move listener to camera/player each frame
Dim px, py As Single
Framework_Ecs_GetTransformPosition(player, px, py)
Framework_Audio_SetListenerPosition(px, py)

' Play sound at world position (auto-attenuated by distance)
Framework_Audio_PlaySoundAt(explosionSfx, 500, 200)

' Stream music with fade-in
Framework_Audio_PlayMusic(bgMusic)
Framework_Audio_FadeInMusic(bgMusic, 2.0F)

' Each frame
Framework_Audio_Update()
```

### GLSL Shaders

```vb
' Load vertex + fragment shader from files (pass Nothing to use default)
Dim vignette As Shader = Framework_LoadShaderF(Nothing, "assets/shaders/vignette.fs")
Dim intensityLoc As Integer = Framework_GetShaderLocation(vignette, "intensity")

' Update uniform each frame (e.g. pulse effect)
Dim pulse As Single = 0.5F + 0.1F * CSng(Math.Sin(Framework_GetTime() * 2))
Framework_SetShaderValue1f(vignette, intensityLoc, pulse)

' Apply to world draw (render to texture, then post-process)
Framework_BeginShaderMode(vignette)
    Framework_DrawTexturePro(rt.texture, src, dst, origin, 0, tint)
Framework_EndShaderMode()

' Cleanup
Framework_UnloadShader(vignette)
```

### Debug Overlay

```vb
' Enable during development
Framework_Debug_SetEnabled(True)
Framework_Debug_DrawEntityBounds(True)  ' Show collider boxes
Framework_Debug_DrawHierarchy(True)     ' Show parent-child lines
Framework_Debug_DrawStats(True)         ' Show entity counts

' In your draw loop (after EndDrawing)
Framework_Debug_Render()
```

---

## Troubleshooting

### Common Issues

**"DLL not found" error**
- Ensure `VisualGameStudioEngine.dll` is in the same directory as your executable
- Check that all Raylib dependency DLLs are present
- Confirm you are building for x64

**"Entry point not found" error**
- Rebuild the C++ engine project first, then the VB.NET projects
- Ensure the C++ project is set to Release/x64

**Black screen / Nothing rendering**
- Confirm `Framework_BeginDrawing()` and `Framework_EndDrawing()` bracket all draw calls
- Call `Framework_ClearBackground()` at the start of each draw
- For sprites: ensure `Framework_Ecs_AddSprite2D`, `Framework_Ecs_SetSpriteTexture`, and `Framework_Ecs_SetEnabled` were all called, and `Framework_Ecs_DrawSprites()` is in the draw loop

**Entities not moving**
- Call `Framework_Ecs_UpdateVelocities()` each frame after setting velocity
- Check that `Framework_Ecs_AddTransform2D` and `Framework_Ecs_AddVelocity2D` were both called on the entity

**Music stops immediately**
- Call `Framework_UpdateAllMusic()` every frame — music is streamed and needs continuous feeding

**Camera not following**
- Call `Framework_Camera_Update()` each frame
- Ensure `Framework_Camera_SetFollowEnabled(True)` was called

### Getting Help

- See `docs/API_REFERENCE.md` for complete function signatures
- Look at `TestVbDLL/` for working game examples
- Run `VisualGameStudio.Tests` to see correct API usage patterns

---

## System Requirements

| Component | Requirement |
|-----------|-------------|
| OS | Windows 10/11 (64-bit) |
| .NET | .NET 8.0 Runtime |
| IDE | Visual Studio 2022+ |
| GPU | OpenGL 3.3+ compatible |
| RAM | 4 GB+ recommended |

---

Happy game development!
