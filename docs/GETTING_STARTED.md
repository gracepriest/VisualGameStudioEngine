# Getting Started with VisualGameStudioEngine

A step-by-step guide to creating your first game with the VisualGameStudioEngine framework.

## Prerequisites

- **Visual Studio 2022** (or later) with C++ and VB.NET workloads
- **.NET 8.0 SDK**
- **Windows 10/11** (the framework uses Windows-specific APIs for controller rumble and networking)

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

Create a new VB file with a minimal game loop:

```vb
Imports RaylibWrapper.FrameworkWrapper

Module MyFirstGame
    Sub Main()
        ' Create a 800x600 window
        Framework_Initialize(800, 600, "My First Game")
        Framework_SetTargetFPS(60)

        ' Game loop
        While Not Framework_ShouldClose()
            Framework_BeginFrame()

            ' Clear to dark blue background
            Framework_ClearBackground(30, 30, 50, 255)

            ' Draw centered text
            Framework_DrawText("My First Game!", 320, 280, 32, 255, 255, 255, 255)

            Framework_EndFrame()
        End While

        Framework_Shutdown()
    End Sub
End Module
```

### Step 2: Add a Player

Expand your game with an entity-based player:

```vb
Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

Module MyFirstGame
    Private player As Integer
    Private playerTexture As Integer

    Sub Main()
        Framework_Initialize(800, 600, "My First Game")
        Framework_SetTargetFPS(60)

        ' Create player entity
        player = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(player, "Player")
        Framework_Ecs_SetPosition(player, 400, 300)

        ' Load player texture (or use a colored rectangle if no texture)
        ' playerTexture = Framework_LoadTextureH("player.png")
        ' Framework_Ecs_AddSprite(player, playerTexture)

        While Not Framework_ShouldClose()
            Update()
            Draw()
        End While

        Cleanup()
    End Sub

    Private Sub Update()
        Framework_BeginFrame()

        ' Get delta time for smooth movement
        Dim dt As Single = Framework_GetDeltaTime()
        Dim speed As Single = 200.0F * dt

        ' WASD movement
        Dim moveX As Single = 0
        Dim moveY As Single = 0

        If Framework_IsKeyDown(Keys.W) OrElse Framework_IsKeyDown(Keys.Up) Then moveY = -speed
        If Framework_IsKeyDown(Keys.S) OrElse Framework_IsKeyDown(Keys.Down) Then moveY = speed
        If Framework_IsKeyDown(Keys.A) OrElse Framework_IsKeyDown(Keys.Left) Then moveX = -speed
        If Framework_IsKeyDown(Keys.D) OrElse Framework_IsKeyDown(Keys.Right) Then moveX = speed

        Framework_Ecs_Translate(player, moveX, moveY)
    End Sub

    Private Sub Draw()
        Framework_ClearBackground(30, 30, 50, 255)

        ' Get player position
        Dim px, py As Single
        Framework_Ecs_GetPosition(player, px, py)

        ' Draw player as a rectangle (if no sprite)
        Framework_DrawRectangle(px - 16, py - 16, 32, 32, 100, 200, 255, 255)

        ' Draw player using ECS (if sprite is set)
        ' Framework_Ecs_DrawAll()

        ' Show position
        Framework_DrawText($"Position: ({px:F0}, {py:F0})", 10, 10, 20, 255, 255, 255, 255)
        Framework_DrawText("Use WASD or Arrow Keys to move", 10, 40, 16, 180, 180, 180, 255)

        Framework_EndFrame()
    End Sub

    Private Sub Cleanup()
        Framework_Ecs_DestroyEntity(player)
        If playerTexture > 0 Then Framework_ReleaseTextureH(playerTexture)
        Framework_Shutdown()
    End Sub
End Module
```

### Step 3: Add Physics

Make your player respond to gravity and collisions:

```vb
Imports RaylibWrapper.FrameworkWrapper
Imports RaylibWrapper.Utiliy

Module PhysicsGame
    Private player As Integer
    Private playerBody As Integer
    Private ground As Integer
    Private groundBody As Integer

    Sub Main()
        Framework_Initialize(800, 600, "Physics Demo")
        Framework_SetTargetFPS(60)

        ' Initialize physics with gravity
        Framework_Physics_Initialize()
        Framework_Physics_SetGravity(0, 500)

        ' Create player with physics body
        player = Framework_Ecs_CreateEntity()
        Framework_Ecs_SetName(player, "Player")
        playerBody = Framework_Physics_CreateBody(player, 0) ' 0 = Dynamic
        Framework_Physics_AddCircleShape(playerBody, 20, 0, 0)
        Framework_Physics_SetBodyPosition(playerBody, 400, 100)
        Framework_Physics_SetBodyRestitution(playerBody, 0.3F)

        ' Create ground
        ground = Framework_Ecs_CreateEntity()
        groundBody = Framework_Physics_CreateBody(ground, 1) ' 1 = Static
        Framework_Physics_AddBoxShape(groundBody, 700, 20, 0, 0)
        Framework_Physics_SetBodyPosition(groundBody, 400, 550)

        While Not Framework_ShouldClose()
            Dim dt = Framework_GetDeltaTime()
            Framework_BeginFrame()

            ' Apply horizontal force with A/D
            If Framework_IsKeyDown(Keys.A) Then
                Framework_Physics_ApplyForce(playerBody, -500, 0)
            End If
            If Framework_IsKeyDown(Keys.D) Then
                Framework_Physics_ApplyForce(playerBody, 500, 0)
            End If

            ' Jump with Space
            If Framework_IsKeyPressed(Keys.Space) Then
                Framework_Physics_SetBodyVelocity(playerBody, 0, -400)
            End If

            ' Update physics
            Framework_Physics_Update(dt)

            ' Sync entity positions from physics
            Framework_Physics_SyncToEntities()

            ' Draw
            Framework_ClearBackground(30, 30, 50, 255)

            ' Draw player (circle)
            Dim px, py As Single
            Framework_Ecs_GetPosition(player, px, py)
            Framework_DrawCircle(px, py, 20, 100, 200, 255, 255)

            ' Draw ground
            Framework_DrawRectangle(50, 540, 700, 20, 100, 100, 100, 255)

            Framework_DrawText("A/D: Move | Space: Jump", 10, 10, 20, 255, 255, 255, 255)

            Framework_EndFrame()
        End While

        Framework_Physics_Shutdown()
        Framework_Shutdown()
    End Sub
End Module
```

## Understanding the Architecture

### Framework Initialization Order

```vb
' 1. Core framework (required)
Framework_Initialize(width, height, title)

' 2. Audio (if needed)
Framework_InitAudio()

' 3. Physics (if needed)
Framework_Physics_Initialize()

' 4. Other systems are initialized on-demand
```

### The Game Loop

Every game follows this pattern:

```vb
While Not Framework_ShouldClose()
    Framework_BeginFrame()    ' Start frame, get delta time

    UpdateGame()               ' Game logic
    DrawGame()                 ' Rendering

    Framework_EndFrame()       ' Finish frame, swap buffers
End While
```

### Entity Component System (ECS)

The ECS is the heart of game object management:

```vb
' Create an entity (just an ID)
Dim entity = Framework_Ecs_CreateEntity()

' Add components
Framework_Ecs_SetName(entity, "Enemy")
Framework_Ecs_SetPosition(entity, 100, 200)
Framework_Ecs_AddSprite(entity, textureHandle)
Framework_Ecs_SetCollider(entity, 0, 0, 32, 32) ' AABB collider

' Query entities
If Framework_Ecs_IsEntityValid(entity) Then
    Dim x, y As Single
    Framework_Ecs_GetPosition(entity, x, y)
End If

' Destroy when done
Framework_Ecs_DestroyEntity(entity)
```

## Common Patterns

### Scene Management

Organize your game into scenes:

```vb
' In your Scene class
Public Class MenuScene
    Inherits Scene

    Public Overrides Sub OnEnter()
        ' Called when scene starts
    End Sub

    Public Overrides Sub OnUpdateFrame(dt As Single)
        ' Called every frame
        If Framework_IsKeyPressed(Keys.Enter) Then
            Framework_SwitchScene("GameScene")
        End If
    End Sub

    Public Overrides Sub OnDraw()
        ' Render the scene
        Framework_DrawText("Press ENTER to Start", 300, 300, 24, 255, 255, 255, 255)
    End Sub

    Public Overrides Sub OnExit()
        ' Called when scene ends
    End Sub
End Class
```

### Resource Management

Load resources once, reuse handles:

```vb
' Load at game start or scene enter
Private playerTex As Integer = Framework_LoadTextureH("player.png")
Private jumpSfx As Integer = Framework_LoadSoundH("jump.wav")
Private bgMusic As Integer = Framework_LoadMusicH("music.ogg")

' Use throughout the game
Framework_Ecs_AddSprite(player, playerTex)
Framework_PlaySoundH(jumpSfx)
Framework_PlayMusicH(bgMusic)

' Release when done (scene exit or game end)
Framework_ReleaseTextureH(playerTex)
Framework_ReleaseSoundH(jumpSfx)
Framework_ReleaseMusicH(bgMusic)
```

### Input Handling

Use the Input Manager for rebindable controls:

```vb
' Create actions
Dim jumpAction = Framework_Input_CreateAction("Jump")
Dim moveXAction = Framework_Input_CreateAction("MoveX")

' Bind keys
Framework_Input_BindKey(jumpAction, Keys.Space)
Framework_Input_BindKey(jumpAction, Keys.W)
Framework_Input_BindGamepadButton(jumpAction, 0, 0) ' A button

' Use in game loop
If Framework_Input_IsActionPressed(jumpAction) Then
    ' Jump!
End If
Dim moveValue = Framework_Input_GetActionValue(moveXAction)
```

### Particle Effects

Add visual flair with particles:

```vb
' Create a fire-like particle emitter
Dim emitter = Framework_Particle_CreateEmitter()
Framework_Particle_SetPosition(emitter, 400, 300)
Framework_Particle_SetEmissionRate(emitter, 50)        ' 50 particles/second
Framework_Particle_SetLifetime(emitter, 1.0F, 2.0F)    ' 1-2 second lifespan
Framework_Particle_SetVelocity(emitter, 0, -100, 30)   ' Upward with spread
Framework_Particle_SetStartColor(emitter, 255, 200, 50, 255)  ' Yellow
Framework_Particle_SetEndColor(emitter, 255, 50, 0, 0)        ' Fading red
Framework_Particle_SetStartSize(emitter, 10, 15)
Framework_Particle_SetEndSize(emitter, 2, 5)
Framework_Particle_Start(emitter)

' In game loop:
Framework_Particle_Update(dt)  ' Update all emitters
Framework_Particle_Draw()      ' Draw all particles

' Cleanup
Framework_Particle_DestroyEmitter(emitter)
```

### Tweening and Animations

Smooth value transitions:

```vb
' Tween an entity's position over 1 second with ease-out
Dim tween = Framework_Tween_Float(startValue, endValue, 1.0F, EaseType.QuadOut)

' Tween entity position
Dim posX As Single = 100
Framework_Tween_CreateFloat(posX, 500, 0.5F, EaseType.ElasticOut)

' Move entity along path
Framework_Tween_EntityPosition(entity, 200, 300, 1.0F, EaseType.CubicInOut)

' Create a sequence of tweens
Dim seq = Framework_Tween_CreateSequence()
Framework_Tween_SequenceAppend(seq, tween1)
Framework_Tween_SequenceAppend(seq, tween2)
Framework_Tween_SequencePlay(seq)

' Update in game loop
Framework_Tween_Update(dt)
```

### UI System

Create interactive user interfaces:

```vb
' Create UI elements
Dim panel = Framework_UI_CreatePanel(10, 10, 200, 150)
Framework_UI_SetColor(panel, 40, 45, 60, 220)

Dim label = Framework_UI_CreateLabel("Score: 0", 20, 20)
Framework_UI_SetParent(label, panel)  ' Attach to panel

Dim button = Framework_UI_CreateButton("Start Game", 20, 60, 160, 40)
Framework_UI_SetParent(button, panel)

Dim slider = Framework_UI_CreateSlider(20, 110, 160, 20)
Framework_UI_SetSliderRange(slider, 0, 100)
Framework_UI_SetSliderValue(slider, 50)
Framework_UI_SetParent(slider, panel)

' In game loop:
Framework_UI_Update()  ' Handle input
Framework_UI_Draw()    ' Render UI

' Check button clicks
If Framework_UI_WasClicked(button) Then
    ' Start game
End If

' Get slider value
Dim volume = Framework_UI_GetSliderValue(slider)
```

### Camera Effects

Dynamic camera for engaging gameplay:

```vb
' Setup camera to follow player
Framework_Camera_SetTarget(player)
Framework_Camera_SetFollowSmoothing(0.1F)  ' Smooth follow
Framework_Camera_SetDeadzone(50, 30)        ' Movement deadzone

' Add zoom
Framework_Camera_SetZoom(1.5F)
Framework_Camera_ZoomTo(2.0F, 0.5F)  ' Zoom to 2x over 0.5 seconds

' Screen shake on impact
Framework_Camera_Shake(10.0F, 0.3F)  ' Intensity 10, duration 0.3s

' Constrain camera to level bounds
Framework_Camera_SetBounds(0, 0, levelWidth, levelHeight)

' In draw:
Framework_Camera_BeginMode()  ' Apply camera transform
    ' Draw world objects here
Framework_Camera_EndMode()
' Draw UI after (unaffected by camera)
```

### GLSL Shaders

Apply visual effects:

```vb
' Load built-in shaders
Dim grayscale = Framework_Shader_LoadGrayscale()
Dim blur = Framework_Shader_LoadBlur()
Dim crt = Framework_Shader_LoadCRT()
Dim vignette = Framework_Shader_LoadVignette()

' Set shader uniforms
Framework_Shader_SetFloatByName(vignette, "vignetteRadius", 0.5F)
Framework_Shader_SetFloatByName(vignette, "vignetteIntensity", 0.8F)

' In draw:
Framework_Shader_Begin(vignette)
    ' Draw affected content
    Framework_DrawRectangle(100, 100, 200, 200, 255, 100, 50, 255)
Framework_Shader_End()

' Cleanup
Framework_Shader_Unload(vignette)
```

### Save/Load System

Persist game progress:

```vb
' Save game data
Framework_Save_BeginSave(1)  ' Slot 1
Framework_Save_WriteInt("level", currentLevel)
Framework_Save_WriteFloat("health", playerHealth)
Framework_Save_WriteBool("hasKey", hasKey)
Framework_Save_WriteString("playerName", playerName)
Framework_Save_EndSave()

' Load game data
If Framework_Save_SlotExists(1) Then
    Framework_Save_BeginLoad(1)
    currentLevel = Framework_Save_ReadInt("level", 1)      ' Default: 1
    playerHealth = Framework_Save_ReadFloat("health", 100) ' Default: 100
    hasKey = Framework_Save_ReadBool("hasKey", False)
    playerName = Framework_Save_ReadString("playerName", "Player")
    Framework_Save_EndLoad()
End If

' Delete save
Framework_Save_DeleteSlot(1)

' Auto-save with interval
Framework_Save_SetAutoSaveInterval(60.0F)  ' Every 60 seconds
Framework_Save_EnableAutoSave(True)
```

### A* Pathfinding

AI navigation with pathfinding:

```vb
' Create navigation grid
Dim grid = Framework_NavGrid_Create(50, 50, 32)  ' 50x50 tiles, 32px each

' Mark obstacles as unwalkable
Framework_NavGrid_SetWalkable(grid, 10, 10, False)
Framework_NavGrid_Fill(grid, 5, 5, 10, 3, False)  ' Wall region

' Find path
Dim pathId = Framework_AI_FindPath(grid, startX, startY, endX, endY)
If pathId >= 0 Then
    Dim pathLength = Framework_AI_GetPathLength(pathId)
    For i As Integer = 0 To pathLength - 1
        Dim wx, wy As Single
        Framework_AI_GetPathPoint(pathId, i, wx, wy)
        ' Move toward each waypoint
    Next
End If

' Create steering agent
Dim agent = Framework_AI_CreateAgent(entity)
Framework_AI_SetMaxSpeed(agent, 150)
Framework_AI_SetArriveRadius(agent, 20)

' Behaviors
Framework_AI_Seek(agent, targetX, targetY)
Framework_AI_Flee(agent, threatX, threatY)
Framework_AI_Wander(agent)
```

### Timers and Events

Schedule delayed actions:

```vb
' One-shot timer (fires once after delay)
Dim timerId = Framework_Timer_After(2.0F, AddressOf OnTimerComplete)

' Repeating timer
Dim repeatId = Framework_Timer_Every(0.5F, AddressOf OnTick)

' Cancel timer
Framework_Timer_Cancel(timerId)

' Event system
Dim eventId = Framework_Event_Register("PlayerDied")
Framework_Event_Subscribe(eventId, AddressOf OnPlayerDied)

' Publish event from anywhere
Framework_Event_Publish(eventId)

' Publish with data
Framework_Event_PublishInt(eventId, score)
Framework_Event_PublishString(eventId, "Game Over!")

' Update in game loop
Framework_Timer_Update(dt)
Framework_Event_ProcessQueue()
```

### Dialogue System

Create conversations:

```vb
' Create dialogue
Dim dlg = Framework_Dialogue_Create("intro")

' Add nodes
Dim node1 = Framework_Dialogue_AddNode(dlg, "Hello, traveler!")
Framework_Dialogue_SetNodeSpeaker(dlg, node1, "Merchant")

Dim node2 = Framework_Dialogue_AddNode(dlg, "What brings you here?")
Framework_Dialogue_SetNodeSpeaker(dlg, node2, "Merchant")

' Add choices
Framework_Dialogue_AddChoice(dlg, node1, "Looking for supplies", node2)
Framework_Dialogue_AddChoice(dlg, node1, "Just passing through", -1)  ' End

' Start dialogue
Framework_Dialogue_Start(dlg)
Framework_Dialogue_SetTypewriterSpeed(0.05F)

' In game loop
If Framework_Dialogue_IsActive(dlg) Then
    Framework_Dialogue_Update(dt)

    ' Get current text for display
    Dim speaker = Framework_Dialogue_GetCurrentSpeaker(dlg)
    Dim text = Framework_Dialogue_GetCurrentText(dlg)
End If
```

## Next Steps

1. **Explore the Demo Scenes** in `TestVbDLL/GameScenes.vb`
2. **Read the API Reference** for detailed function documentation
3. **Try the different systems**: Physics, Particles, UI, Tweening
4. **Build something!** Start small and expand

## Troubleshooting

### Common Issues

**"DLL not found" error**
- Ensure `VisualGameStudioEngine.dll` is in the same directory as your executable
- Check that all Raylib dependencies are present

**"Entry point not found" error**
- Rebuild the C++ project first, then the VB.NET projects
- Make sure you're using the correct platform (x64)

**Black screen / Nothing rendering**
- Check that `Framework_BeginFrame()` and `Framework_EndFrame()` are called
- Verify `Framework_ClearBackground()` is called each frame
- Ensure entities have sprites set before calling `Framework_Ecs_DrawAll()`

**Physics not working**
- Call `Framework_Physics_Initialize()` before creating bodies
- Call `Framework_Physics_Update(deltaTime)` each frame
- Check that gravity is set: `Framework_Physics_SetGravity(0, 500)`

### Getting Help

- Check the API Reference for function signatures
- Look at existing demo scenes for working examples
- The unit tests in `FrameworkTests.vb` show correct API usage

## System Requirements

| Component | Requirement |
|-----------|-------------|
| OS | Windows 10/11 (64-bit) |
| .NET | .NET 8.0 Runtime |
| IDE | Visual Studio 2022+ |
| GPU | OpenGL 3.3+ compatible |
| RAM | 4GB+ recommended |

---

Happy game development!
