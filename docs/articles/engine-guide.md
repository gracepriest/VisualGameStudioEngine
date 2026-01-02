# Game Engine Guide

This guide covers the Visual Game Studio Engine for 2D game development.

## Architecture

The engine provides two tiers of functionality:

1. **Framework** - Direct Raylib wrapper for simple games
2. **Engine** - Full ECS with scenes, prefabs, and physics

## Framework Mode

### Initialization

```vb
' Initialize window
Framework_Initialize(800, 600, "My Game")
Framework_SetFixedStep(1.0 / 60.0)  ' 60 FPS
Framework_InitAudio()

' Set callbacks
Framework_SetDrawCallback(AddressOf OnDraw)
Framework_SetUpdateCallback(AddressOf OnUpdate)

' Game loop
While Not Framework_ShouldClose()
    Framework_Update()
End While

' Cleanup
Framework_Shutdown()
```

### Drawing

```vb
Sub OnDraw()
    Framework_BeginDrawing()
    Framework_ClearBackground(30, 30, 50, 255)

    ' Draw shapes
    Framework_DrawRectangle(100, 100, 50, 50, 255, 0, 0, 255)
    Framework_DrawCircle(200, 200, 25, 0, 255, 0, 255)
    Framework_DrawLine(0, 0, 400, 300, 255, 255, 0, 255)

    ' Draw text
    Framework_DrawText("Score: 100", 10, 10, 20, 255, 255, 255, 255)

    Framework_EndDrawing()
End Sub
```

### Input Handling

```vb
Sub OnUpdate()
    ' Keyboard
    If Framework_IsKeyPressed(KEY_SPACE) Then
        Jump()
    End If

    If Framework_IsKeyDown(KEY_RIGHT) Then
        MoveRight()
    End If

    ' Mouse
    Dim mx = Framework_GetMouseX()
    Dim my = Framework_GetMouseY()

    If Framework_IsMouseButtonPressed(0) Then
        Shoot(mx, my)
    End If
End Sub
```

## Entity Component System

### Creating Entities

```vb
' Create player entity
Dim player = Framework_Ecs_CreateEntity()
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
' Set position
Framework_Ecs_SetTransform2D(entity, x, y, rotation, scaleX, scaleY)

' Get position
Dim x, y, rot, sx, sy As Single
Framework_Ecs_GetTransform2D(entity, x, y, rot, sx, sy)

' Get world position (accounts for hierarchy)
Framework_Ecs_GetWorldPosition(entity, worldX, worldY)
```

### Velocity Component

```vb
' Set velocity
Framework_Ecs_SetVelocity2D(entity, vx, vy)

' Get velocity
Dim vx, vy As Single
Framework_Ecs_GetVelocity2D(entity, vx, vy)
```

### Entity Hierarchy

```vb
' Create parent-child relationship
Dim parent = Framework_Ecs_CreateEntity()
Dim child = Framework_Ecs_CreateEntity()
Framework_Ecs_SetParent(child, parent)

' Navigate hierarchy
Dim parentId = Framework_Ecs_GetParent(child)
Dim firstChild = Framework_Ecs_GetFirstChild(parent)
Dim nextSibling = Framework_Ecs_GetNextSibling(firstChild)
Dim childCount = Framework_Ecs_GetChildCount(parent)
```

### Finding Entities

```vb
' Find by name
Dim player = Framework_Ecs_FindByName("Player")

' Find by tag
Dim enemies(100) As Integer
Dim count = Framework_Ecs_FindByTag("enemy", enemies, 100)
For i = 0 To count - 1
    ProcessEnemy(enemies(i))
Next
```

## Camera System

### Creating Camera

```vb
Dim camera = Framework_Camera2D_Create()

' Configure camera
Framework_Camera2D_SetOffset(camera, 400, 300)  ' Center of screen
Framework_Camera2D_SetZoom(camera, 1.0)
Framework_Camera2D_SetRotation(camera, 0)
```

### Using Camera

```vb
Sub OnDraw()
    Framework_BeginDrawing()
    Framework_ClearBackground(0, 0, 0, 255)

    ' Begin camera mode
    Framework_Camera2D_Begin(camera)

    ' Draw world objects (transformed by camera)
    DrawWorld()

    Framework_Camera2D_End(camera)

    ' Draw UI (not transformed)
    DrawUI()

    Framework_EndDrawing()
End Sub
```

### Camera Follow

```vb
' Follow an entity
Framework_Camera2D_Follow(camera, playerEntity)

' Or manually update
Sub OnUpdate()
    Dim px, py As Single
    Framework_Ecs_GetWorldPosition(player, px, py)
    Framework_Camera2D_SetTarget(camera, px, py)
End Sub
```

## Physics

### Collision Detection

```vb
' Check overlap at point
Dim results(10) As Integer
Dim count = Framework_Physics_OverlapPoint(mouseX, mouseY, results, 10)

' Check overlap in box
count = Framework_Physics_OverlapBox(x, y, width, height, results, 10)

' Process collisions
For i = 0 To count - 1
    Dim entity = results(i)
    HandleCollision(entity)
Next
```

### Box Colliders

```vb
' Add collider with offset
Framework_Ecs_AddBoxCollider2D(entity, offsetX, offsetY, width, height)

' Get collider bounds
Dim ox, oy, w, h As Single
Framework_Ecs_GetBoxCollider2D(entity, ox, oy, w, h)
```

## Prefabs

### Saving Prefabs

```vb
' Create entity with components
Dim enemy = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(enemy, "Enemy")
Framework_Ecs_AddTransform2D(enemy, 0, 0, 0, 1, 1)
Framework_Ecs_AddVelocity2D(enemy, -50, 0)
Framework_Ecs_AddBoxCollider2D(enemy, -16, -16, 32, 32)

' Save as prefab
Framework_Prefab_SaveEntity(enemy, "enemy.prefab")
```

### Loading and Instantiating

```vb
' Load prefab
Dim prefabHandle = Framework_Prefab_Load("enemy.prefab")

' Spawn instances
For i = 0 To 10
    Dim x = 800 + i * 100
    Dim y = 100 + (i Mod 3) * 50
    Dim instance = Framework_Prefab_Instantiate(prefabHandle, -1, x, y)
Next

' Cleanup when done
Framework_Prefab_Unload(prefabHandle)
```

## Scene Management

### Saving Scenes

```vb
' Save current scene
Framework_Scene_Save("level1.scene")
```

### Loading Scenes

```vb
' Load scene (clears existing entities)
Framework_Scene_Load("level1.scene")
```

## Debug Overlay

### Enabling Debug

```vb
Framework_Debug_SetEnabled(True)
Framework_Debug_DrawEntityBounds(True)   ' Show colliders
Framework_Debug_DrawHierarchy(True)      ' Show parent-child lines
Framework_Debug_DrawStats(True)          ' Show entity count
```

### Rendering Debug

```vb
Sub OnDraw()
    Framework_BeginDrawing()

    ' Draw game...

    ' Draw debug overlay last
    Framework_Debug_Render()

    Framework_EndDrawing()
End Sub
```

## Timing

### Delta Time

```vb
Sub OnUpdate()
    Dim dt = Framework_GetDeltaTime()

    ' Move at consistent speed
    x += speed * dt
End Sub
```

### Time Scale

```vb
' Slow motion
Framework_SetTimeScale(0.5)

' Normal speed
Framework_SetTimeScale(1.0)

' Fast forward
Framework_SetTimeScale(2.0)

' Pause (stops physics, not rendering)
Framework_SetTimeScale(0.0)
```

### Frame Counting

```vb
Dim frameCount = Framework_GetFrameCount()
If frameCount Mod 60 = 0 Then
    SpawnEnemy()  ' Every second at 60 FPS
End If
```

## Audio

### Playing Sounds

```vb
' Initialize audio system
Framework_InitAudio()

' Load and play sound
Dim sound = Framework_LoadSound("hit.wav")
Framework_PlaySound(sound)

' Music
Dim music = Framework_LoadMusic("background.mp3")
Framework_PlayMusic(music)
Framework_SetMusicVolume(music, 0.5)
```

## Best Practices

### Entity Management

```vb
' Store entity IDs, not references
Dim playerEntity As Integer

' Check existence before use
If Framework_Ecs_EntityExists(playerEntity) Then
    ProcessPlayer(playerEntity)
End If

' Clean up destroyed entities
Framework_Ecs_DestroyEntity(entity)
```

### Component Access

```vb
' Cache frequently accessed components
Dim playerX, playerY As Single

Sub OnUpdate()
    ' Update cached values
    Framework_Ecs_GetTransform2D(player, playerX, playerY, _, _, _)

    ' Use cached values
    If playerX < 0 Then WrapToRight()
End Sub
```

### Performance

```vb
' Pool frequently created entities
Dim bulletPool(100) As Integer
Dim poolIndex As Integer

Function GetBullet() As Integer
    If poolIndex < 100 Then
        Dim bullet = bulletPool(poolIndex)
        Framework_Ecs_SetEnabled(bullet, True)
        poolIndex += 1
        Return bullet
    End If
    Return -1
End Function

Sub ReturnBullet(bullet As Integer)
    Framework_Ecs_SetEnabled(bullet, False)
    poolIndex -= 1
    bulletPool(poolIndex) = bullet
End Sub
```

## Complete Example

```vb
Dim player As Integer
Dim camera As Integer

Sub Main()
    Framework_Initialize(800, 600, "My Game")
    Framework_SetFixedStep(1.0 / 60.0)

    ' Create player
    player = Framework_Ecs_CreateEntity()
    Framework_Ecs_SetName(player, "Player")
    Framework_Ecs_AddTransform2D(player, 400, 300, 0, 1, 1)
    Framework_Ecs_AddVelocity2D(player, 0, 0)
    Framework_Ecs_AddBoxCollider2D(player, -16, -16, 32, 32)
    Framework_Ecs_SetEnabled(player, True)

    ' Create camera
    camera = Framework_Camera2D_Create()
    Framework_Camera2D_SetOffset(camera, 400, 300)
    Framework_Camera2D_Follow(camera, player)

    ' Game loop
    While Not Framework_ShouldClose()
        Update()
        Draw()
    End While

    Framework_Shutdown()
End Sub

Sub Update()
    Dim speed = 200.0
    Dim dt = Framework_GetDeltaTime()
    Dim vx, vy As Single

    If Framework_IsKeyDown(KEY_LEFT) Then vx = -speed
    If Framework_IsKeyDown(KEY_RIGHT) Then vx = speed
    If Framework_IsKeyDown(KEY_UP) Then vy = -speed
    If Framework_IsKeyDown(KEY_DOWN) Then vy = speed

    Framework_Ecs_SetVelocity2D(player, vx, vy)
End Sub

Sub Draw()
    Framework_BeginDrawing()
    Framework_ClearBackground(30, 30, 50, 255)

    Framework_Camera2D_Begin(camera)

    ' Draw player
    Dim x, y As Single
    Framework_Ecs_GetWorldPosition(player, x, y)
    Framework_DrawRectangle(x - 16, y - 16, 32, 32, 0, 255, 0, 255)

    Framework_Camera2D_End(camera)

    ' UI
    Framework_DrawText("Arrow keys to move", 10, 10, 20, 255, 255, 255, 255)

    Framework_EndDrawing()
End Sub
```

## Next Steps

- [BasicLang Guide](basiclang-guide.md) - Language reference
- [IDE User Guide](ide-guide.md) - Development environment
- [API Reference](../api/index.md) - Complete API
