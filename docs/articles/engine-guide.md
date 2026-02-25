# Game Engine Guide

This guide covers the Visual Game Studio Engine for 2D game development.

## Architecture

The engine provides two layers of functionality:

1. **Framework** — Direct Raylib wrapper for immediate-mode 2D drawing and input
2. **ECS** — Entity Component System for object management, hierarchy, and collision

Both layers share the same DLL and can be mixed freely in a single game.

---

## Framework Mode

### Initialization and Game Loop

```vb
Imports RaylibWrapper.FrameworkWrapper

Module MyGame
    Sub Main()
        Framework_Initialize(800, 600, "My Game")
        Framework_SetTargetFPS(60)
        Framework_InitAudio()

        While Not Framework_ShouldClose()
            Framework_Update()       ' Process input, advance time

            Framework_BeginDrawing()
            Framework_ClearBackground(30, 30, 50, 255)

            OnDraw()

            Framework_EndDrawing()
        End While

        Framework_CloseAudio()
        Framework_Shutdown()
    End Sub
End Module
```

### Drawing

```vb
Sub OnDraw()
    ' Shapes
    Framework_DrawRectangle(100, 100, 50, 50, 255, 0, 0, 255)
    Framework_DrawCircle(200, 200, 25, 0, 255, 0, 255)
    Framework_DrawLine(0, 0, 400, 300, 255, 255, 0, 255)
    Framework_DrawTriangle(300, 100, 350, 200, 250, 200, 0, 100, 255, 255)

    ' Text
    Framework_DrawText("Score: 100", 10, 10, 20, 255, 255, 255, 255)
    Framework_DrawFPS(10, 560)
End Sub
```

### Input

```vb
Sub OnUpdate()
    ' Keyboard
    If Framework_IsKeyPressed(Keys.Space) Then
        Jump()
    End If
    If Framework_IsKeyDown(Keys.Right) Then
        MoveRight()
    End If

    ' Mouse
    Dim mx As Single = Framework_GetMouseX()
    Dim my As Single = Framework_GetMouseY()
    If Framework_IsMouseButtonPressed(0) Then
        Shoot(mx, my)
    End If
End Sub
```

---

## Entity Component System

### Creating Entities

```vb
' Create player entity
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(player, "Player")
Framework_Ecs_SetTag(player, "player")

' Add components
Framework_Ecs_AddTransform2D(player, 400, 300, 0, 1, 1)
Framework_Ecs_AddVelocity2D(player, 0, 0)
Framework_Ecs_AddBoxCollider2D(player, -20, -20, 40, 40)
Framework_Ecs_SetEnabled(player, True)
```

### Transform Component

```vb
' Set position / rotation / scale individually
Framework_Ecs_SetTransformPosition(entity, x, y)
Framework_Ecs_SetTransformRotation(entity, rotation)   ' Degrees
Framework_Ecs_SetTransformScale(entity, scaleX, scaleY)

' Get local values
Dim px, py As Single
Framework_Ecs_GetTransformPosition(entity, px, py)
Dim rot As Single = Framework_Ecs_GetTransformRotation(entity)

' Get world position (accounts for parent hierarchy)
Dim wx, wy As Single
Framework_Ecs_GetWorldPosition(entity, wx, wy)
```

### Velocity Component

```vb
' Set velocity (applied to transform by UpdateVelocities each frame)
Framework_Ecs_SetVelocity(entity, vx, vy)

' Get velocity
Dim vx, vy As Single
Framework_Ecs_GetVelocity(entity, vx, vy)

' Remove component
Framework_Ecs_RemoveVelocity2D(entity)
```

### Sprite Component

```vb
' Load texture and attach to entity
Dim tex As Integer = Framework_AcquireTextureH("assets/player.png")

Framework_Ecs_AddSprite2D(entity)
Framework_Ecs_SetSpriteTexture(entity, tex)
Framework_Ecs_SetSpriteTint(entity, 255, 255, 255, 255)
Framework_Ecs_SetSpriteLayer(entity, 1)       ' Higher = drawn on top

' Sprite sheet — pick a specific frame
Dim src As Rectangle = Framework_SpriteFrame(32, 32, frameIndex)
Framework_Ecs_SetSpriteSource(entity, src.x, src.y, src.width, src.height)
```

### Entity Hierarchy

```vb
' Create parent-child relationship
Dim parent As Integer = Framework_Ecs_CreateEntity()
Dim child As Integer  = Framework_Ecs_CreateEntity()
Framework_Ecs_SetParent(child, parent)

' Navigate hierarchy
Dim parentId    As Integer = Framework_Ecs_GetParent(child)
Dim firstChild  As Integer = Framework_Ecs_GetFirstChild(parent)
Dim nextSibling As Integer = Framework_Ecs_GetNextSibling(firstChild)
Dim childCount  As Integer = Framework_Ecs_GetChildCount(parent)

' Traverse all children
Dim c As Integer = Framework_Ecs_GetFirstChild(parent)
While c <> -1
    ' process c
    c = Framework_Ecs_GetNextSibling(c)
End While

' Detach from parent
Framework_Ecs_DetachFromParent(child)
```

### Finding Entities

```vb
' Find by name
Dim player As Integer = Framework_Ecs_FindByName("Player")

' Find by tag
Dim enemies(63) As Integer
Dim count As Integer = Framework_Ecs_FindAllByTag("enemy", enemies, 64)
For i As Integer = 0 To count - 1
    ProcessEnemy(enemies(i))
Next
```

### ECS Systems (call each frame)

```vb
While Not Framework_ShouldClose()
    Framework_Update()

    ' Update
    Framework_Ecs_UpdateVelocities()  ' Apply velocity to transform

    Framework_BeginDrawing()
    Framework_ClearBackground(30, 30, 50, 255)

    Framework_Camera_BeginMode()
        Framework_Ecs_DrawSprites()   ' Render all Sprite2D components
    Framework_Camera_EndMode()

    Framework_Debug_Render()          ' Debug overlay (no-op when disabled)

    Framework_EndDrawing()
End While
```

---

## Camera System

The engine has a single global Camera2D with basic and enhanced modes.

### Basic Setup

```vb
Framework_Camera_SetZoom(1.0F)
Framework_Camera_SetOffset(400, 300)    ' View center (half screen)
Framework_Camera_SetTarget(400, 300)    ' World point to center on
```

### Camera Follow (Enhanced)

```vb
' Smooth follow with deadzone and world bounds
Framework_Camera_SetFollowTarget(playerEntity)
Framework_Camera_SetFollowLerp(0.1F)              ' 0 = no smoothing, 1 = instant
Framework_Camera_SetFollowEnabled(True)
Framework_Camera_SetDeadzone(40, 30)
Framework_Camera_SetDeadzoneEnabled(True)
Framework_Camera_SetBounds(0, 0, levelWidth, levelHeight)

' Must call each frame
Framework_Camera_Update()
```

### Camera in Draw Loop

```vb
Framework_BeginDrawing()
Framework_ClearBackground(0, 0, 0, 255)

Framework_Camera_BeginMode()
    ' World objects drawn here (scrolled by camera)
    Framework_Ecs_DrawSprites()
Framework_Camera_EndMode()

' HUD drawn here (not scrolled)
Framework_DrawText("Score: 0", 10, 10, 20, 255, 255, 255, 255)

Framework_EndDrawing()
```

### Camera Effects

```vb
' Screen shake on impact
Framework_Camera_Shake(10.0F, 0.3F)
Framework_Camera_ShakeEx(20.0F, 5.0F, 0.4F, 1.5F)   ' Directional

' Screen flash
Framework_Camera_Flash(255, 255, 255, 200, 0.15F)

' Smooth zoom
Framework_Camera_ZoomTo(2.0F, 0.5F)

' Coordinate conversion
Dim worldPos As Vector2 = Framework_Camera_ScreenToWorld(mouseX, mouseY)
Dim screenPos As Vector2 = Framework_Camera_WorldToScreen(entityX, entityY)
```

---

## Collision Detection

### Box Colliders

```vb
' Add collider with local offset and size
Framework_Ecs_AddBoxCollider2D(entity, offsetX, offsetY, width, height)

' Get world-space AABB
Dim bounds As Rectangle = Framework_Ecs_GetBoxColliderWorldBounds(entity)

' Set as trigger (no physics response, just overlap detection)
Framework_Ecs_SetBoxColliderTrigger(entity, True)
```

### Overlap Queries

```vb
' Find all entities overlapping a box region
Dim results(31) As Integer
Dim count As Integer = Framework_Physics_OverlapBox(x, y, width, height, results, 32)
For i As Integer = 0 To count - 1
    HandleOverlap(results(i))
Next

' Find all entities overlapping a circle
count = Framework_Physics_OverlapCircle(cx, cy, radius, results, 32)

' Check two specific entities
If Framework_Physics_CheckEntityOverlap(playerEntity, enemyEntity) Then
    TakeDamage()
End If

' Get everything overlapping a given entity
count = Framework_Physics_GetOverlappingEntities(player, results, 32)
```

### Geometric Collision Checks

```vb
' Pure math checks (no ECS involved)
Dim hit As Boolean = Framework_CheckCollisionRecs(rec1, rec2)
Dim hit2 As Boolean = Framework_CheckCollisionCircles(center1, r1, center2, r2)
Dim hit3 As Boolean = Framework_CheckCollisionPointRec(mousePos, buttonRect)
```

---

## Audio

### Basic Sounds

```vb
Framework_InitAudio()

Dim jumpSfx As Integer = Framework_LoadSoundH("jump.wav")
Framework_PlaySoundH(jumpSfx)
Framework_SetSoundVolumeH(jumpSfx, 0.8F)
Framework_SetSoundPitchH(jumpSfx, 1.1F)

' Music (must call UpdateAllMusic each frame)
Dim music As Integer = Framework_AcquireMusicH("theme.ogg")
Framework_PlayMusicH(music)
Framework_SetMusicVolumeH(music, 0.5F)

' In game loop:
Framework_UpdateAllMusic()

' Cleanup
Framework_UnloadSoundH(jumpSfx)
Framework_ReleaseMusicH(music)
Framework_CloseAudio()
```

### Audio Manager (Groups and Spatial)

```vb
' Group volumes (0=SFX, 1=Music — your own convention)
Framework_Audio_SetGroupVolume(0, 0.8F)
Framework_Audio_SetGroupVolume(1, 0.5F)

' Spatial audio — attenuate by world distance
Framework_Audio_SetSpatialEnabled(True)
Framework_Audio_SetSpatialFalloff(100, 600)

Dim px, py As Single
Framework_Ecs_GetTransformPosition(player, px, py)
Framework_Audio_SetListenerPosition(px, py)

Dim explosionSfx As Integer = Framework_Audio_LoadSound("explosion.wav", 0)
Framework_Audio_PlaySoundAt(explosionSfx, worldX, worldY)

' Sound pooling for frequently played sounds
Dim pool As Integer = Framework_Audio_CreatePool(explosionSfx, 8)
Framework_Audio_PlayFromPool(pool)

' Must be called each frame
Framework_Audio_Update()
```

---

## Debug Overlay

```vb
' Enable during development
Framework_Debug_SetEnabled(True)
Framework_Debug_DrawEntityBounds(True)  ' Show collider AABBs
Framework_Debug_DrawHierarchy(True)     ' Show parent-child lines
Framework_Debug_DrawStats(True)         ' Show entity/component counts

' Call each frame after EndDrawing
Framework_Debug_Render()
```

---

## Timing

### Delta Time

```vb
' Scale movement by delta time for frame-rate independence
Dim dt As Single = Framework_GetDeltaTime()
x += speed * dt
```

### Time Scale

```vb
Framework_SetTimeScale(0.5F)  ' Slow motion
Framework_SetTimeScale(1.0F)  ' Normal
Framework_SetTimeScale(2.0F)  ' Fast forward
```

### Fixed Timestep

```vb
Framework_SetFixedStep(1.0 / 60.0)  ' Physics step at 60Hz
' Call Framework_StepFixed() manually for deterministic simulation
```

### Frame Count

```vb
Dim frame As Long = Framework_GetFrameCount()
If frame Mod 180 = 0 Then
    SpawnEnemy()  ' Every 3 seconds at 60 FPS
End If
```

---

## Best Practices

### Entity Management

```vb
' Always check before accessing
If Framework_Ecs_IsAlive(entity) Then
    Dim px, py As Single
    Framework_Ecs_GetTransformPosition(entity, px, py)
End If

' Destroy and discard the ID
Framework_Ecs_DestroyEntity(entity)
entity = -1
```

### Resource Management

```vb
' Load once — release when the scene/game ends
Dim tex As Integer = Framework_AcquireTextureH("player.png")

' Use throughout
Framework_Ecs_SetSpriteTexture(player, tex)

' Release on cleanup
Framework_ReleaseTextureH(tex)
```

### Entity Pooling

Pre-create entities and toggle enabled instead of creating/destroying each frame:

```vb
Const POOL_SIZE As Integer = 64
Dim bulletPool(POOL_SIZE - 1) As Integer
Dim nextFree As Integer = 0

Sub InitPool()
    For i As Integer = 0 To POOL_SIZE - 1
        bulletPool(i) = Framework_Ecs_CreateEntity()
        Framework_Ecs_AddTransform2D(bulletPool(i), 0, 0, 0, 1, 1)
        Framework_Ecs_AddVelocity2D(bulletPool(i), 0, 0)
        Framework_Ecs_AddBoxCollider2D(bulletPool(i), -4, -4, 8, 8)
        Framework_Ecs_SetEnabled(bulletPool(i), False)   ' Start disabled
    Next
End Sub

Function AcquireBullet(x As Single, y As Single, vx As Single, vy As Single) As Integer
    For i As Integer = 0 To POOL_SIZE - 1
        If Not Framework_Ecs_IsEnabled(bulletPool(i)) Then
            Framework_Ecs_SetTransformPosition(bulletPool(i), x, y)
            Framework_Ecs_SetVelocity(bulletPool(i), vx, vy)
            Framework_Ecs_SetEnabled(bulletPool(i), True)
            Return bulletPool(i)
        End If
    Next
    Return -1   ' Pool exhausted
End Function

Sub ReleaseBullet(id As Integer)
    Framework_Ecs_SetEnabled(id, False)
End Sub
```

---

## Complete Example

```vb
Imports RaylibWrapper.FrameworkWrapper

Module Game
    Dim player As Integer
    Dim playerTex As Integer
    Dim score As Integer = 0

    Sub Main()
        Framework_Initialize(800, 600, "My Game")
        Framework_SetTargetFPS(60)

        ' Setup player
        playerTex = Framework_AcquireTextureH("assets/player.png")
        player = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(player, "Player")
        Framework_Ecs_AddTransform2D(player, 400, 300, 0, 1, 1)
        Framework_Ecs_AddVelocity2D(player, 0, 0)
        Framework_Ecs_AddBoxCollider2D(player, -16, -16, 32, 32)
        Framework_Ecs_AddSprite2D(player)
        Framework_Ecs_SetSpriteTexture(player, playerTex)
        Framework_Ecs_SetEnabled(player, True)

        ' Camera follow
        Framework_Camera_SetFollowTarget(player)
        Framework_Camera_SetFollowLerp(0.1F)
        Framework_Camera_SetFollowEnabled(True)
        Framework_Camera_SetBounds(0, 0, 3200, 3200)

        While Not Framework_ShouldClose()
            Update()
            Draw()
        End While

        Framework_ReleaseTextureH(playerTex)
        Framework_Shutdown()
    End Sub

    Sub Update()
        Framework_Update()

        Dim speed As Single = 200.0F * Framework_GetDeltaTime()
        Dim vx As Single = 0, vy As Single = 0

        If Framework_IsKeyDown(Keys.Left)  Then vx = -speed
        If Framework_IsKeyDown(Keys.Right) Then vx =  speed
        If Framework_IsKeyDown(Keys.Up)    Then vy = -speed
        If Framework_IsKeyDown(Keys.Down)  Then vy =  speed

        Framework_Ecs_SetVelocity(player, vx, vy)
        Framework_Ecs_UpdateVelocities()

        ' Camera
        Framework_Camera_Update()
    End Sub

    Sub Draw()
        Framework_BeginDrawing()
        Framework_ClearBackground(30, 30, 50, 255)

        ' World (scrolled by camera)
        Framework_Camera_BeginMode()
            Framework_Ecs_DrawSprites()
        Framework_Camera_EndMode()

        ' HUD (fixed)
        Framework_DrawText($"Score: {score}", 10, 10, 24, 255, 255, 0, 255)
        Framework_DrawText("Arrow Keys to move", 10, 40, 16, 180, 180, 180, 255)

        Framework_Debug_Render()

        Framework_EndDrawing()
    End Sub
End Module
```

---

## Next Steps

- [BasicLang Guide](basiclang-guide.md) — Language reference
- [IDE User Guide](ide-guide.md) — Development environment
- [API Reference](../API_REFERENCE.md) — Complete function listing
