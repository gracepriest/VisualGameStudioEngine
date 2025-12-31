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
