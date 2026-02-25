# Visual Game Studio Engine

A Raylib-based 2D game engine with C++ core, VB.NET bindings, and a full-featured IDE with IntelliSense support.

Visual Game Studio is a modern game development environment designed to make creating 2D games fast, flexible, and fun. It combines a lightweight yet powerful framework built on top of the proven RayLib library with a complete IDE featuring code completion, syntax highlighting, debugging, and more.

## Components

### Game Engine (C++ DLL)
- **Framework v1.0**: Direct Raylib wrapper for immediate-mode 2D game development
- **Engine v0.5**: Unity-like runtime with ECS, scene management, prefabs, and serialization

### BasicLang Compiler
A custom programming language compiler with:
- Full lexer and parser for a VB-like syntax
- Semantic analysis with type checking
- **Pattern matching** with When guards, Or patterns, Nothing patterns, range/comparison patterns
- LINQ support for queries
- Async/Await support
- Class, interface, and module support
- Template/generic support
- Inline code blocks (C#, C++, LLVM, MSIL)
- Preprocessor directives
- Multiple backend targets (C#, native)

### Visual Game Studio IDE
A full-featured Avalonia-based IDE with:
- **IntelliSense**: Code completion, parameter hints, quick info
- **LSP Integration**: Language Server Protocol for advanced editor features
- **Syntax Highlighting**: Semantic token-based highlighting
- **Code Folding**: Collapsible regions for code organization
- **Bracket Matching**: Automatic bracket highlighting
- **Multi-Cursor Editing**: Multiple cursor support
- **Bookmarks**: Navigate code with bookmarks
- **Breakpoints**: Set breakpoints for debugging
- **Diagnostics**: Real-time error and warning display
- **Go to Definition**: Navigate to symbol definitions
- **Find References**: Find all usages of symbols
- **Document Symbols**: Outline view of code structure
- **Hover Documentation**: Rich hover information

## Architecture

```
VisualGameStudioEngine.dll (C++)    BasicLang.dll (C#)
        |                                  |
        v                                  v
   Stable C ABI                     LSP Server
        |                                  |
        v                                  v
  RaylibWrapper.vb (P/Invoke)     VisualGameStudio IDE
        |                                  |
        v                                  v
   Your VB.NET Game              Code Editor Features
```

## Project Structure

```
VisualGameStudioEngine/
├── BasicLang/                    # Compiler and LSP server
│   ├── Compiler/                 # Lexer, parser, semantic analysis
│   └── LSP/                      # Language Server Protocol handlers
├── VisualGameStudio.Core/        # Core models and services
├── VisualGameStudio.Editor/      # Editor components (Avalonia)
├── VisualGameStudio.ProjectSystem/# Project and solution management
├── VisualGameStudio.Tests/       # Unit test suite (800+ tests)
├── RaylibWrapper/                # VB.NET bindings for engine
└── TestVbDLL/                    # Sample games
```

## Quick Start

### Framework-Only (Simple Games)

```vb
' Initialize
Framework_Initialize(800, 600, "My Game")
Framework_SetFixedStep(1.0 / 60.0)
Framework_InitAudio()

' Set draw callback
Framework_SetDrawCallback(AddressOf MyDraw)

' Game loop
While Not Framework_ShouldClose()
    Framework_Update()
End While

' Cleanup
Framework_Shutdown()

Sub MyDraw()
    Framework_BeginDrawing()
    Framework_ClearBackground(30, 30, 50, 255)
    Framework_DrawText("Hello World!", 100, 100, 24, 255, 255, 255, 255)
    Framework_EndDrawing()
End Sub
```

### Engine-Style (ECS + Scenes)

```vb
' Create an entity
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(player, "Player")
Framework_Ecs_SetTag(player, "player")
Framework_Ecs_AddTransform2D(player, 400, 300, 0, 1, 1)
Framework_Ecs_AddVelocity2D(player, 0, 0)
Framework_Ecs_AddBoxCollider2D(player, -20, -20, 40, 40)
Framework_Ecs_SetEnabled(player, True)

' Create hierarchy
Dim weapon As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetParent(weapon, player)

' Save as prefab
Framework_Prefab_SaveEntity(player, "player.prefab")

' Instantiate prefab
Dim prefab As Integer = Framework_Prefab_Load("player.prefab")
Dim instance As Integer = Framework_Prefab_Instantiate(prefab, -1, 100, 100)
```

## BasicLang Language Features

### Variable Declaration
```vb
Dim x As Integer = 10
Dim name As String = "Hello"
Auto y = 3.14  ' Type inference
Const PI = 3.14159
```

### Control Flow
```vb
If condition Then
    ' statements
ElseIf otherCondition Then
    ' statements
Else
    ' statements
End If

For i = 0 To 10 Step 2
    ' statements
Next

While condition
    ' statements
Wend

Do
    ' statements
Loop Until condition
```

### Functions and Subroutines
```vb
Sub MySub(x As Integer, Optional y As Integer = 0)
    ' statements
End Sub

Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
```

### Object-Oriented Programming
```vb
Class Player
    Private _health As Integer

    Public Property Health As Integer
        Get
            Return _health
        End Get
        Set
            _health = value
        End Set
    End Property

    Public Sub TakeDamage(amount As Integer)
        _health -= amount
    End Sub
End Class
```

### LINQ Support
```vb
Dim numbers = {1, 2, 3, 4, 5}
Dim evens = From n In numbers
            Where n Mod 2 = 0
            Select n
```

### Async/Await
```vb
Async Function LoadDataAsync() As Task(Of String)
    Dim result = Await FetchFromServer()
    Return result
End Function
```

### Pattern Matching
Advanced pattern matching in Select Case statements:

```vb
' When guards - conditional pattern matching
Dim x As Integer = 5
Select Case x
    Case n When n > 10
        Console.WriteLine("Greater than 10")
    Case n When n > 0
        Console.WriteLine("Positive")
    Case 0
        Console.WriteLine("Zero")
    Case Else
        Console.WriteLine("Negative")
End Select

' Or patterns - match multiple alternatives
Dim day As Integer = 6
Select Case day
    Case 1 Or 7
        Console.WriteLine("Weekend")
    Case 2 Or 3 Or 4 Or 5 Or 6
        Console.WriteLine("Weekday")
End Select

' Nothing pattern - null checking
Dim obj As Object = Nothing
Select Case obj
    Case Nothing
        Console.WriteLine("Object is null")
    Case Else
        Console.WriteLine("Object has value")
End Select

' Range patterns
Dim score As Integer = 85
Select Case score
    Case 90 To 100
        Console.WriteLine("A")
    Case 80 To 89
        Console.WriteLine("B")
    Case 70 To 79
        Console.WriteLine("C")
    Case Else
        Console.WriteLine("F")
End Select

' Comparison patterns
Dim value As Integer = 15
Select Case value
    Case Is > 10
        Console.WriteLine("Greater than 10")
    Case Is < 0
        Console.WriteLine("Negative")
    Case Else
        Console.WriteLine("Between 0 and 10")
End Select

' Type patterns
Dim item As Object = "Hello"
Select Case item
    Case s As String
        Console.WriteLine("String: " & s)
    Case i As Integer
        Console.WriteLine("Integer: " & i)
    Case Else
        Console.WriteLine("Unknown type")
End Select
```

## IDE Features

### IntelliSense
The IDE provides intelligent code completion with:
- Keywords and language constructs
- Local variables and parameters
- Type members (properties, methods, fields)
- Built-in functions and types
- Context-aware suggestions

### Diagnostics
Real-time error checking with:
- Syntax errors
- Semantic errors (type mismatches, undefined symbols)
- Warnings (unused variables, deprecated features)
- Information messages (suggestions)

### Navigation
- **Go to Definition**: Jump to where a symbol is defined
- **Find All References**: Find all usages of a symbol
- **Document Outline**: View all symbols in the current file
- **Bookmarks**: Mark important locations in code

### Debugging
- Set breakpoints by clicking the gutter
- Step through code execution
- View variable values
- Debug output window

## Testing

The project includes a comprehensive test suite with 800+ unit tests covering:

- **Core Models**: Project, solution, build configuration
- **Editor Features**: Folding, bracket highlighting, completion, multi-cursor
- **Services**: Bookmark, build, debug, language services
- **LSP/IntelliSense**: Completion, document management, diagnostics, symbols
- **Compiler**: Lexer, parser, token types

### Running Tests

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj
```

## Existing Documentation

The `docs/` folder contains detailed documentation:

| File | Contents |
|------|----------|
| `docs/API_REFERENCE.md` | Full game engine API with code examples (35+ systems) |
| `docs/GETTING_STARTED.md` | Quick-start guide with step-by-step examples |
| `docs/articles/engine-guide.md` | Engine architecture overview |
| `docs/articles/basiclang-guide.md` | BasicLang language reference |
| `docs/articles/debugging-guide.md` | Debugging instructions |
| `docs/articles/ide-guide.md` | IDE user guide |
| `docs/BasicLang-Reference.md` | Full language syntax reference |
| `docs/UserGuide.md` | End-user guide |

---

## API Reference

All functions are exported from `VisualGameStudioEngine.dll` and wrapped in `RaylibWrapper/RaylibWrapper.vb` via P/Invoke.

### Core Framework

| Function | Description |
|----------|-------------|
| `Framework_Initialize(width, height, title)` | Initialize window and engine |
| `Framework_Update()` | Process one frame (input, physics, systems) |
| `Framework_ShouldClose()` | Returns True when window close is requested |
| `Framework_Shutdown()` | Cleanup all resources and close window |
| `Framework_Pause()` | Pause engine updates |
| `Framework_Resume()` | Resume engine updates |
| `Framework_Quit()` | Request graceful shutdown |
| `Framework_IsPaused()` | Returns True if engine is paused |
| `Framework_GetState()` | Returns EngineState enum value |

### Timing & Time Control

| Function | Description |
|----------|-------------|
| `Framework_SetTargetFPS(fps)` | Set target frames per second |
| `Framework_GetFPS()` | Get current frames per second |
| `Framework_GetFrameTime()` | Get last frame duration in seconds |
| `Framework_GetDeltaTime()` | Alias for GetFrameTime |
| `Framework_GetTime()` | Get total elapsed time in seconds |
| `Framework_GetFrameCount()` | Get total frames rendered |
| `Framework_SetTimeScale(scale)` | Set time scale (0.5 = half speed, 2.0 = double) |
| `Framework_GetTimeScale()` | Get current time scale |
| `Framework_SetFixedStep(dt)` | Set fixed timestep (e.g., 1.0/60.0) |
| `Framework_GetFixedStep()` | Get fixed timestep value |
| `Framework_StepFixed()` | Advance one fixed timestep manually |

### Drawing

| Function | Description |
|----------|-------------|
| `Framework_BeginDrawing()` | Begin frame — must be called before drawing |
| `Framework_EndDrawing()` | End frame and swap buffers |
| `Framework_ClearBackground(r, g, b, a)` | Clear screen to RGBA color |
| `Framework_DrawText(text, x, y, size, r, g, b, a)` | Draw text with built-in font |
| `Framework_DrawFPS(x, y)` | Draw FPS counter at position |
| `Framework_DrawGrid(slices, spacing)` | Draw a reference grid |
| `Framework_DrawPixel(x, y, r, g, b, a)` | Draw single pixel |
| `Framework_DrawLine(x1, y1, x2, y2, r, g, b, a)` | Draw line |
| `Framework_DrawRectangle(x, y, w, h, r, g, b, a)` | Draw filled rectangle |
| `Framework_DrawCircle(cx, cy, radius, r, g, b, a)` | Draw filled circle |
| `Framework_DrawCircleLines(cx, cy, radius, r, g, b, a)` | Draw circle outline |
| `Framework_DrawTriangle(x1,y1, x2,y2, x3,y3, r,g,b,a)` | Draw filled triangle |
| `Framework_DrawTriangleLines(x1,y1, x2,y2, x3,y3, r,g,b,a)` | Draw triangle outline |

### Input — Keyboard

| Function | Description |
|----------|-------------|
| `Framework_IsKeyPressed(key)` | True if key was pressed this frame |
| `Framework_IsKeyPressedRepeat(key)` | True if key pressed (with OS repeat) |
| `Framework_IsKeyDown(key)` | True if key is held |
| `Framework_IsKeyReleased(key)` | True if key was released this frame |
| `Framework_IsKeyUp(key)` | True if key is not held |
| `Framework_GetKeyPressed()` | Returns key code of last pressed key |
| `Framework_GetCharPressed()` | Returns character code of last pressed char |
| `Framework_SetExitKey(key)` | Set which key triggers window close |

### Input — Mouse

| Function | Description |
|----------|-------------|
| `Framework_GetMouseX()` | Mouse X position |
| `Framework_GetMouseY()` | Mouse Y position |
| `Framework_GetMousePosition()` | Mouse position as Vector2 |
| `Framework_GetMouseDelta()` | Mouse movement delta since last frame |
| `Framework_SetMousePosition(x, y)` | Warp mouse to position |
| `Framework_SetMouseOffset(offsetX, offsetY)` | Apply offset to mouse readings |
| `Framework_SetMouseScale(scaleX, scaleY)` | Apply scale to mouse readings |
| `Framework_GetMouseWheelMove()` | Mouse wheel delta (float) |
| `Framework_GetMouseWheelMoveV()` | Mouse wheel as Vector2 |
| `Framework_SetMouseCursor(cursor)` | Set system cursor type |
| `Framework_IsMouseButtonPressed(button)` | True if button pressed this frame |
| `Framework_IsMouseButtonDown(button)` | True if button held |
| `Framework_IsMouseButtonReleased(button)` | True if button released this frame |
| `Framework_IsMouseButtonUp(button)` | True if button not held |
| `Framework_ShowCursor()` | Show the system cursor |
| `Framework_HideCursor()` | Hide the system cursor |
| `Framework_IsCursorHidden()` | True if cursor is hidden |
| `Framework_EnableCursor()` | Enable cursor (unlock from window) |
| `Framework_DisableCursor()` | Disable cursor (lock to window) |
| `Framework_IsCursorOnScreen()` | True if cursor is within window |

### Collision Checks

| Function | Description |
|----------|-------------|
| `Framework_CheckCollisionRecs(rec1, rec2)` | Rectangle vs rectangle |
| `Framework_CheckCollisionCircles(c1, r1, c2, r2)` | Circle vs circle |
| `Framework_CheckCollisionCircleRec(center, radius, rec)` | Circle vs rectangle |
| `Framework_CheckCollisionCircleLine(center, radius, p1, p2)` | Circle vs line segment |
| `Framework_CheckCollisionPointRec(point, rec)` | Point inside rectangle |
| `Framework_CheckCollisionPointCircle(point, center, radius)` | Point inside circle |
| `Framework_CheckCollisionPointTriangle(point, p1, p2, p3)` | Point inside triangle |
| `Framework_CheckCollisionPointLine(point, p1, p2, threshold)` | Point on line |
| `Framework_CheckCollisionPointPoly(point, points, count)` | Point inside polygon |
| `Framework_CheckCollisionLines(p1, p2, p3, p4, out collision)` | Line vs line |
| `Framework_GetCollisionRec(rec1, rec2)` | Get overlap rectangle |

### Textures & Images

| Function | Description |
|----------|-------------|
| `Framework_LoadTexture(fileName)` | Load texture from file |
| `Framework_UnloadTexture(texture)` | Unload texture from GPU |
| `Framework_IsTextureValid(texture)` | True if texture is ready |
| `Framework_UpdateTexture(texture, pixels)` | Update texture pixel data |
| `Framework_UpdateTextureRec(texture, rec, pixels)` | Update texture region |
| `Framework_GenTextureMipmaps(texture)` | Generate texture mipmaps |
| `Framework_SetTextureFilter(texture, filter)` | Set texture filter mode |
| `Framework_SetTextureWrap(texture, wrap)` | Set texture wrap mode |
| `Framework_DrawTexture(texture, x, y, r,g,b,a)` | Draw texture at position |
| `Framework_DrawTextureV(texture, pos, r,g,b,a)` | Draw texture at Vector2 |
| `Framework_DrawTextureEx(texture, pos, rotation, scale, tint)` | Draw with rotation/scale |
| `Framework_DrawTextureRec(texture, src, pos, tint)` | Draw source rectangle |
| `Framework_DrawTexturePro(texture, src, dst, origin, rot, tint)` | Full transform draw |
| `Framework_DrawTextureNPatch(texture, nPatch, dst, origin, rot, tint)` | Draw 9-patch |
| `Framework_LoadImage(fileName)` | Load image from file (CPU-side) |
| `Framework_UnloadImage(image)` | Unload image from memory |
| `Framework_ImageColorInvert(image)` | Invert image colors |
| `Framework_ImageResize(image, w, h)` | Resize image |
| `Framework_ImageFlipVertical(image)` | Flip image vertically |
| `Framework_SpriteFrame(frameW, frameH, frameIndex)` | Get source rect for sprite sheet frame |

### Handle-Based Texture API

A ref-counted handle system for safe, disposable texture management.

| Function | Description |
|----------|-------------|
| `Framework_AcquireTextureH(fileName)` | Load or retrieve cached texture, returns handle |
| `Framework_ReleaseTextureH(handle)` | Release handle (unloads when ref count = 0) |
| `Framework_IsTextureValidH(handle)` | True if handle is valid |
| `Framework_DrawTextureH(handle, x, y, r,g,b,a)` | Draw texture by handle |
| `Framework_DrawTextureVH(handle, pos, tint)` | Draw by handle at Vector2 |
| `Framework_DrawTextureExH(handle, pos, rot, scale, tint)` | Draw by handle with transform |
| `Framework_GetTextureWidth(handle)` | Get texture width |
| `Framework_GetTextureHeight(handle)` | Get texture height |

### Render Textures & Off-Screen Rendering

| Function | Description |
|----------|-------------|
| `Framework_LoadRenderTexture(width, height)` | Create render target |
| `Framework_UnloadRenderTexture(target)` | Unload render target |
| `Framework_IsRenderTextureValid(target)` | True if render target is ready |
| `Framework_BeginTextureMode(target)` | Redirect drawing to render texture |
| `Framework_EndTextureMode()` | Stop rendering to texture |
| `Framework_BeginMode2D(camera)` | Begin 2D camera mode |
| `Framework_EndMode2D()` | End 2D camera mode |

### Camera 2D — Basic

| Function | Description |
|----------|-------------|
| `Framework_Camera_SetPosition(x, y)` | Set camera position |
| `Framework_Camera_SetTarget(x, y)` | Set camera look-at target |
| `Framework_Camera_SetRotation(rotation)` | Set camera rotation (degrees) |
| `Framework_Camera_SetZoom(zoom)` | Set camera zoom level |
| `Framework_Camera_SetOffset(x, y)` | Set screen-space offset |
| `Framework_Camera_GetPosition()` | Get camera position as Vector2 |
| `Framework_Camera_GetZoom()` | Get current zoom |
| `Framework_Camera_GetRotation()` | Get current rotation |
| `Framework_Camera_BeginMode()` | Begin camera mode |
| `Framework_Camera_EndMode()` | End camera mode |
| `Framework_Camera_ScreenToWorld(x, y)` | Convert screen coords to world coords |
| `Framework_Camera_WorldToScreen(x, y)` | Convert world coords to screen coords |
| `Framework_Camera_Reset()` | Reset camera to defaults |

### Camera 2D — Enhanced

| Function | Description |
|----------|-------------|
| `Framework_Camera_SetFollowTarget(entityId)` | Set entity for camera to follow |
| `Framework_Camera_SetFollowLerp(lerp)` | Set follow smoothing (0.0–1.0) |
| `Framework_Camera_SetFollowEnabled(enabled)` | Enable/disable auto-follow |
| `Framework_Camera_IsFollowEnabled()` | True if follow is active |
| `Framework_Camera_SetDeadzone(w, h)` | Set follow deadzone size |
| `Framework_Camera_SetDeadzoneEnabled(enabled)` | Enable/disable deadzone |
| `Framework_Camera_SetLookahead(distance)` | Look-ahead distance in direction of travel |
| `Framework_Camera_SetLookaheadVelocity(vx, vy)` | Set velocity used for lookahead |
| `Framework_Camera_Shake(intensity, duration)` | Trigger screen shake |
| `Framework_Camera_ShakeEx(intensityX, intensityY, duration, falloff)` | Directional shake |
| `Framework_Camera_StopShake()` | Stop any active shake |
| `Framework_Camera_IsShaking()` | True if shake is active |
| `Framework_Camera_SetBounds(x, y, w, h)` | Set world bounds (camera won't go outside) |
| `Framework_Camera_GetBounds()` | Get current world bounds |
| `Framework_Camera_SetZoomLimits(minZoom, maxZoom)` | Clamp zoom range |
| `Framework_Camera_ZoomTo(zoom, duration)` | Smooth zoom over time |
| `Framework_Camera_ZoomAt(zoom, worldX, worldY, duration)` | Zoom towards world point |
| `Framework_Camera_RotateTo(rotation, duration)` | Smooth rotation over time |
| `Framework_Camera_PanTo(x, y, duration)` | Smooth pan to world position |
| `Framework_Camera_PanBy(dx, dy, duration)` | Smooth pan by offset |
| `Framework_Camera_Flash(r, g, b, a, duration)` | Screen flash effect |
| `Framework_Camera_Update()` | Update camera (call each frame) |

### Fonts & Text

| Function | Description |
|----------|-------------|
| `Framework_LoadFontEx(fileName, size, codepoints, count)` | Load custom font at given size |
| `Framework_UnloadFont(font)` | Unload font from memory |
| `Framework_DrawTextEx(font, text, pos, size, spacing, r,g,b,a)` | Draw text with custom font |
| `Framework_AcquireFontH(fileName, size)` | Load or retrieve cached font, returns handle |
| `Framework_ReleaseFontH(handle)` | Release font handle |
| `Framework_IsFontValidH(handle)` | True if font handle is valid |
| `Framework_DrawTextExH(handle, text, pos, size, spacing, tint)` | Draw text by font handle |

### Audio — Basic

| Function | Description |
|----------|-------------|
| `Framework_InitAudio()` | Initialize audio subsystem |
| `Framework_CloseAudio()` | Close audio subsystem |
| `Framework_SetMasterVolume(volume)` | Set master volume (0.0–1.0) |
| `Framework_GetMasterVolume()` | Get master volume |
| `Framework_PauseAllAudio()` | Pause all sounds and music |
| `Framework_ResumeAllAudio()` | Resume all paused audio |
| `Framework_LoadSoundH(fileName)` | Load sound, returns handle |
| `Framework_UnloadSoundH(handle)` | Unload sound |
| `Framework_PlaySoundH(handle)` | Play sound |
| `Framework_StopSoundH(handle)` | Stop sound |
| `Framework_PauseSoundH(handle)` | Pause sound |
| `Framework_ResumeSoundH(handle)` | Resume paused sound |
| `Framework_SetSoundVolumeH(handle, volume)` | Set sound volume (0.0–1.0) |
| `Framework_SetSoundPitchH(handle, pitch)` | Set sound pitch (1.0 = normal) |
| `Framework_SetSoundPanH(handle, pan)` | Set stereo pan (-1.0 left to 1.0 right) |
| `Framework_AcquireMusicH(fileName)` | Load streaming music, returns handle |
| `Framework_ReleaseMusicH(handle)` | Unload music stream |
| `Framework_IsMusicValidH(handle)` | True if music handle is valid |
| `Framework_PlayMusicH(handle)` | Play music stream |
| `Framework_StopMusicH(handle)` | Stop music stream |
| `Framework_PauseMusicH(handle)` | Pause music stream |
| `Framework_ResumeMusicH(handle)` | Resume music stream |
| `Framework_SetMusicVolumeH(handle, volume)` | Set music volume |
| `Framework_SetMusicPitchH(handle, pitch)` | Set music pitch |
| `Framework_UpdateMusicH(handle)` | Update music stream buffers |
| `Framework_UpdateAllMusic()` | Update all active music streams |

### Audio Manager — Advanced

| Function | Description |
|----------|-------------|
| `Framework_Audio_SetGroupVolume(group, volume)` | Set volume for audio group |
| `Framework_Audio_GetGroupVolume(group)` | Get audio group volume |
| `Framework_Audio_SetGroupMuted(group, muted)` | Mute/unmute audio group |
| `Framework_Audio_IsGroupMuted(group)` | True if group is muted |
| `Framework_Audio_FadeGroupVolume(group, target, duration)` | Fade group to target volume |
| `Framework_Audio_LoadSound(fileName, group)` | Load sound into group, returns id |
| `Framework_Audio_UnloadSound(soundId)` | Unload managed sound |
| `Framework_Audio_PlaySound(soundId)` | Play managed sound |
| `Framework_Audio_PlaySoundEx(soundId, volume, pitch, pan)` | Play with custom parameters |
| `Framework_Audio_StopSound(soundId)` | Stop managed sound |
| `Framework_Audio_SetSoundGroup(soundId, group)` | Move sound to different group |
| `Framework_Audio_GetSoundGroup(soundId)` | Get sound's current group |
| `Framework_Audio_SetListenerPosition(x, y)` | Set spatial audio listener position |
| `Framework_Audio_PlaySoundAt(soundId, x, y)` | Play sound at world position |
| `Framework_Audio_PlaySoundAtEx(soundId, x, y, volume, pitch)` | Play spatial sound with params |
| `Framework_Audio_SetSpatialFalloff(minDist, maxDist)` | Set spatial attenuation distances |
| `Framework_Audio_SetSpatialEnabled(enabled)` | Enable/disable spatial audio |
| `Framework_Audio_CreatePool(soundId, count)` | Create sound pool, returns poolId |
| `Framework_Audio_DestroyPool(poolId)` | Destroy sound pool |
| `Framework_Audio_PlayFromPool(poolId)` | Play next available instance from pool |
| `Framework_Audio_PlayFromPoolEx(poolId, volume, pitch, pan)` | Play from pool with params |
| `Framework_Audio_LoadMusic(fileName)` | Load music into manager, returns id |
| `Framework_Audio_UnloadMusic(musicId)` | Unload managed music |
| `Framework_Audio_PlayMusic(musicId)` | Play managed music |
| `Framework_Audio_StopMusic(musicId)` | Stop managed music |
| `Framework_Audio_PauseMusic(musicId)` | Pause managed music |
| `Framework_Audio_ResumeMusic(musicId)` | Resume managed music |
| `Framework_Audio_SetMusicVolume(musicId, volume)` | Set music volume |
| `Framework_Audio_SetMusicPitch(musicId, pitch)` | Set music pitch |
| `Framework_Audio_SetMusicLooping(musicId, loop)` | Enable/disable music looping |
| `Framework_Audio_IsMusicPlaying(musicId)` | True if music is playing |
| `Framework_Audio_GetMusicLength(musicId)` | Get music duration in seconds |
| `Framework_Audio_GetMusicPosition(musicId)` | Get playback position in seconds |
| `Framework_Audio_SeekMusic(musicId, position)` | Seek to position in seconds |
| `Framework_Audio_CrossfadeTo(fromId, toId, duration)` | Crossfade between two tracks |
| `Framework_Audio_FadeOutMusic(musicId, duration)` | Fade out music |
| `Framework_Audio_FadeInMusic(musicId, duration)` | Fade in music |
| `Framework_Audio_CreatePlaylist()` | Create playlist, returns playlistId |
| `Framework_Audio_DestroyPlaylist(playlistId)` | Destroy playlist |
| `Framework_Audio_PlaylistAdd(playlistId, musicId)` | Add track to playlist |
| `Framework_Audio_PlaylistRemove(playlistId, index)` | Remove track from playlist |
| `Framework_Audio_PlaylistPlay(playlistId)` | Start playlist playback |
| `Framework_Audio_PlaylistNext(playlistId)` | Skip to next track |
| `Framework_Audio_PlaylistPrev(playlistId)` | Go to previous track |
| `Framework_Audio_PlaylistSetShuffle(playlistId, shuffle)` | Enable/disable shuffle |
| `Framework_Audio_PlaylistSetRepeat(playlistId, repeat)` | Enable/disable repeat |
| `Framework_Audio_Update()` | Update audio manager (call each frame) |

### Audio Effects & Filters

| Function | Description |
|----------|-------------|
| `Framework_Audio_CreateFilter(type)` | Create audio filter, returns filterId |
| `Framework_Audio_DestroyFilter(filterId)` | Destroy filter |
| `Framework_Audio_SetFilterCutoff(filterId, freq)` | Set low/high-pass cutoff frequency |
| `Framework_Audio_SetFilterResonance(filterId, res)` | Set filter resonance |
| `Framework_Audio_SetFilterGain(filterId, gain)` | Set filter gain |
| `Framework_Audio_ApplyFilterToSound(filterId, soundId)` | Apply filter to a sound |
| `Framework_Audio_ApplyFilterToGroup(filterId, group)` | Apply filter to audio group |
| `Framework_Audio_RemoveFilterFromSound(filterId, soundId)` | Remove filter from sound |
| `Framework_Audio_RemoveFilterFromGroup(filterId, group)` | Remove filter from group |
| `Framework_Audio_SetFilterEnabled(filterId, enabled)` | Enable/disable filter |
| `Framework_Audio_IsFilterEnabled(filterId)` | True if filter is active |
| `Framework_Audio_CreateReverb()` | Create reverb effect, returns reverbId |
| `Framework_Audio_DestroyReverb(reverbId)` | Destroy reverb |
| `Framework_Audio_SetReverbDecay(reverbId, time)` | Set reverb decay time |
| `Framework_Audio_SetReverbDensity(reverbId, density)` | Set reverb density |
| `Framework_Audio_SetReverbDiffusion(reverbId, diffusion)` | Set reverb diffusion |
| `Framework_Audio_SetReverbRoomSize(reverbId, size)` | Set reverb room size |
| `Framework_Audio_SetReverbWetDry(reverbId, wet)` | Set wet/dry mix (0.0–1.0) |
| `Framework_Audio_SetReverbPreDelay(reverbId, delay)` | Set pre-delay in seconds |
| `Framework_Audio_ApplyReverbToSound(reverbId, soundId)` | Apply reverb to sound |
| `Framework_Audio_ApplyReverbToGroup(reverbId, group)` | Apply reverb to group |
| `Framework_Audio_RemoveReverbFromSound(reverbId, soundId)` | Remove reverb from sound |
| `Framework_Audio_RemoveReverbFromGroup(reverbId, group)` | Remove reverb from group |
| `Framework_Audio_SetReverbPreset(reverbId, preset)` | Apply reverb preset (room, hall, etc.) |
| `Framework_Audio_CreateEcho(delay, feedback)` | Create echo effect, returns echoId |
| `Framework_Audio_DestroyEcho(echoId)` | Destroy echo |
| `Framework_Audio_CreateDistortion(drive, mix)` | Create distortion effect, returns id |
| `Framework_Audio_DestroyDistortion(distortionId)` | Destroy distortion |
| `Framework_Audio_CreateCompressor(threshold, ratio)` | Create compressor effect, returns id |
| `Framework_Audio_DestroyCompressor(compressorId)` | Destroy compressor |

### Shaders

| Function | Description |
|----------|-------------|
| `Framework_LoadShaderF(vsPath, fsPath)` | Load vertex + fragment shaders from files |
| `Framework_UnloadShader(shader)` | Unload shader program |
| `Framework_BeginShaderMode(shader)` | Apply shader to subsequent draws |
| `Framework_EndShaderMode()` | Stop using custom shader |
| `Framework_GetShaderLocation(shader, uniformName)` | Get uniform location index |
| `Framework_SetShaderValue1f(shader, loc, value)` | Set float uniform |
| `Framework_SetShaderValue2f(shader, loc, x, y)` | Set vec2 uniform |
| `Framework_SetShaderValue3f(shader, loc, x, y, z)` | Set vec3 uniform |
| `Framework_SetShaderValue4f(shader, loc, x, y, z, w)` | Set vec4 uniform |
| `Framework_SetShaderValue1i(shader, loc, value)` | Set int uniform |

### Scene System — Basic

| Function | Description |
|----------|-------------|
| `Framework_CreateScriptScene()` | Create a new scene, returns sceneId |
| `Framework_DestroyScene(sceneId)` | Destroy scene |
| `Framework_SceneChange(sceneId)` | Immediately switch to scene |
| `Framework_ScenePush(sceneId)` | Push scene onto stack |
| `Framework_ScenePop()` | Pop top scene from stack |
| `Framework_SceneHas(sceneId)` | True if scene exists |
| `Framework_SceneTick(sceneId)` | Update the given scene |
| `Framework_SceneGetCurrent()` | Get current active scene id |

### Scene Manager — Transitions & Loading

| Function | Description |
|----------|-------------|
| `Framework_Scene_SetTransition(type, duration)` | Set default transition |
| `Framework_Scene_SetTransitionEx(type, duration, easing)` | Set transition with easing |
| `Framework_Scene_SetTransitionColor(r, g, b, a)` | Set transition overlay color |
| `Framework_Scene_GetTransitionType()` | Get current transition type |
| `Framework_Scene_GetTransitionDuration()` | Get transition duration |
| `Framework_Scene_GetTransitionEasing()` | Get transition easing type |
| `Framework_Scene_ChangeWithTransition(sceneId)` | Change scene with transition |
| `Framework_Scene_ChangeWithTransitionEx(sceneId, type, duration, easing)` | Change with custom transition |
| `Framework_Scene_PushWithTransition(sceneId)` | Push scene with transition |
| `Framework_Scene_PopWithTransition()` | Pop scene with transition |
| `Framework_Scene_IsTransitioning()` | True while transition is playing |
| `Framework_Scene_GetTransitionState()` | Get transition state enum |
| `Framework_Scene_GetTransitionProgress()` | Get progress (0.0–1.0) |
| `Framework_Scene_SkipTransition()` | Skip to end of transition |
| `Framework_Scene_SetLoadingEnabled(enabled)` | Enable loading screen |
| `Framework_Scene_SetLoadingMinDuration(seconds)` | Minimum loading screen time |
| `Framework_Scene_SetLoadingCallback(callback)` | Set progress callback |
| `Framework_Scene_SetLoadingDrawCallback(callback)` | Set custom loading draw callback |
| `Framework_Scene_GetStackSize()` | Get scene stack depth |
| `Framework_Scene_GetSceneAt(index)` | Get scene at stack index |

**Transition Types:** Fade, SlideLeft, SlideRight, SlideUp, SlideDown, Wipe, Pixelate, Dissolve, Iris, Swipe, Zoom, Rotate, Checkerboard, Random

**Easing Types:** Linear, EaseIn, EaseOut, EaseInOut, Bounce, Elastic, Back, Spring (21 total)

### ECS — Entity Management

| Function | Description |
|----------|-------------|
| `Framework_Ecs_CreateEntity()` | Create entity, returns entity ID |
| `Framework_Ecs_DestroyEntity(id)` | Destroy entity and remove all components |
| `Framework_Ecs_IsAlive(id)` | True if entity exists |
| `Framework_Ecs_ClearAll()` | Destroy all entities |
| `Framework_Ecs_GetEntityCount()` | Get number of living entities |

### ECS — Name Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_SetName(id, name)` | Set entity name (max 64 chars) |
| `Framework_Ecs_GetName(id)` | Get entity name as String |
| `Framework_Ecs_HasName(id)` | True if entity has a name |
| `Framework_Ecs_FindByName(name)` | Find first entity with name, returns id |

### ECS — Tag Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_SetTag(id, tag)` | Set entity tag (max 32 chars) |
| `Framework_Ecs_GetTag(id)` | Get entity tag as String |
| `Framework_Ecs_HasTag(id)` | True if entity has a tag |
| `Framework_Ecs_FindAllByTag(tag, out ids, maxCount)` | Get all entities with given tag |

### ECS — Enabled Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_SetEnabled(id, enabled)` | Enable or disable entity |
| `Framework_Ecs_IsEnabled(id)` | True if entity is locally enabled |
| `Framework_Ecs_IsActiveInHierarchy(id)` | True if entity and all parents are enabled |

### ECS — Hierarchy Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_SetParent(childId, parentId)` | Set parent (-1 = root) |
| `Framework_Ecs_GetParent(id)` | Get parent entity id |
| `Framework_Ecs_GetFirstChild(id)` | Get first child entity id |
| `Framework_Ecs_GetNextSibling(id)` | Get next sibling entity id |
| `Framework_Ecs_GetChildCount(id)` | Get number of direct children |
| `Framework_Ecs_GetChildren(id, out ids, maxCount)` | Fill array with child ids |
| `Framework_Ecs_DetachFromParent(id)` | Remove entity from its parent |

### ECS — Transform2D Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_AddTransform2D(id, x, y, rot, sx, sy)` | Add transform with initial values |
| `Framework_Ecs_HasTransform2D(id)` | True if entity has transform |
| `Framework_Ecs_SetTransformPosition(id, x, y)` | Set local position |
| `Framework_Ecs_GetTransformPosition(id, out x, out y)` | Get local position |
| `Framework_Ecs_SetTransformRotation(id, rotation)` | Set local rotation (degrees) |
| `Framework_Ecs_GetTransformRotation(id)` | Get local rotation |
| `Framework_Ecs_SetTransformScale(id, sx, sy)` | Set local scale |
| `Framework_Ecs_GetTransformScale(id, out sx, out sy)` | Get local scale |
| `Framework_Ecs_GetWorldPosition(id, out x, out y)` | Get world position (hierarchy applied) |
| `Framework_Ecs_GetWorldRotation(id)` | Get world rotation (hierarchy applied) |
| `Framework_Ecs_GetWorldScale(id, out sx, out sy)` | Get world scale (hierarchy applied) |

### ECS — Velocity2D Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_AddVelocity2D(id, vx, vy)` | Add velocity component |
| `Framework_Ecs_HasVelocity2D(id)` | True if entity has velocity |
| `Framework_Ecs_SetVelocity(id, vx, vy)` | Set velocity vector |
| `Framework_Ecs_GetVelocity(id, out vx, out vy)` | Get velocity vector |
| `Framework_Ecs_RemoveVelocity2D(id)` | Remove velocity component |

### ECS — BoxCollider2D Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_AddBoxCollider2D(id, offsetX, offsetY, w, h)` | Add box collider |
| `Framework_Ecs_HasBoxCollider2D(id)` | True if entity has box collider |
| `Framework_Ecs_SetBoxCollider(id, offsetX, offsetY, w, h)` | Update collider bounds |
| `Framework_Ecs_SetBoxColliderTrigger(id, isTrigger)` | Set trigger mode |
| `Framework_Ecs_GetBoxColliderWorldBounds(id)` | Get world-space AABB |
| `Framework_Ecs_RemoveBoxCollider2D(id)` | Remove collider component |

### ECS — Sprite2D Component

| Function | Description |
|----------|-------------|
| `Framework_Ecs_AddSprite2D(id)` | Add sprite component |
| `Framework_Ecs_HasSprite2D(id)` | True if entity has sprite |
| `Framework_Ecs_SetSpriteTexture(id, textureHandle)` | Set sprite texture |
| `Framework_Ecs_SetSpriteTint(id, r, g, b, a)` | Set sprite tint color |
| `Framework_Ecs_SetSpriteVisible(id, visible)` | Show or hide sprite |
| `Framework_Ecs_SetSpriteLayer(id, layer)` | Set render sort layer |
| `Framework_Ecs_SetSpriteSource(id, x, y, w, h)` | Set source rectangle (for sprite sheets) |
| `Framework_Ecs_RemoveSprite2D(id)` | Remove sprite component |

### ECS — Built-in Systems

| Function | Description |
|----------|-------------|
| `Framework_Ecs_UpdateVelocities()` | Apply Velocity2D to Transform2D positions |
| `Framework_Ecs_DrawSprites()` | Render all Sprite2D components |

### Physics Overlap Queries

| Function | Description |
|----------|-------------|
| `Framework_Physics_OverlapBox(x, y, w, h, out ids, maxCount)` | Get entities overlapping box |
| `Framework_Physics_OverlapCircle(cx, cy, radius, out ids, maxCount)` | Get entities overlapping circle |
| `Framework_Physics_CheckEntityOverlap(idA, idB)` | True if two entities' colliders overlap |
| `Framework_Physics_GetOverlappingEntities(id, out ids, maxCount)` | Get all entities overlapping with id |

### Component Introspection

| Function | Description |
|----------|-------------|
| `Framework_Entity_GetComponentCount(id)` | Get number of components on entity |
| `Framework_Entity_GetComponentTypeAt(id, index)` | Get ComponentType enum at index |
| `Framework_Entity_HasComponent(id, componentType)` | True if entity has component type |
| `Framework_Component_GetFieldCount(id, componentType)` | Get number of fields in component |
| `Framework_Component_GetFieldName(id, componentType, index)` | Get field name by index |
| `Framework_Component_GetFieldType(id, componentType, index)` | Get field type name |
| `Framework_Component_GetFieldFloat(id, componentType, fieldName)` | Read float field |
| `Framework_Component_GetFieldInt(id, componentType, fieldName)` | Read int field |
| `Framework_Component_GetFieldBool(id, componentType, fieldName)` | Read bool field |
| `Framework_Component_GetFieldString(id, componentType, fieldName)` | Read string field |
| `Framework_Component_SetFieldFloat(id, componentType, fieldName, value)` | Write float field |
| `Framework_Component_SetFieldInt(id, componentType, fieldName, value)` | Write int field |
| `Framework_Component_SetFieldBool(id, componentType, fieldName, value)` | Write bool field |
| `Framework_Component_SetFieldString(id, componentType, fieldName, value)` | Write string field |

### Debug Overlay

| Function | Description |
|----------|-------------|
| `Framework_Debug_SetEnabled(enabled)` | Enable/disable debug overlay |
| `Framework_Debug_IsEnabled()` | True if debug overlay is active |
| `Framework_Debug_DrawEntityBounds(enabled)` | Show entity collider AABB |
| `Framework_Debug_DrawHierarchy(enabled)` | Show parent-child hierarchy lines |
| `Framework_Debug_DrawStats(enabled)` | Show entity/component statistics |
| `Framework_Debug_Render()` | Draw debug overlay (call after EndDrawing) |

### Asset Cache

| Function | Description |
|----------|-------------|
| `Framework_SetAssetRoot(path)` | Set root directory for asset loading |
| `Framework_GetAssetRoot()` | Get current asset root path |

## Samples

### Sample A: Framework-Only

`TestVbDLL/SampleA_FrameworkOnly.vb` - A "Catch the Falling Blocks" game demonstrating:
- Window initialization and game loop
- Input handling (keyboard)
- Basic 2D rendering
- Pause/Resume functionality

### Sample B: Engine ECS

`TestVbDLL/SampleB_EngineECS.vb` - A space shooter demonstrating:
- Entity Component System
- Entity hierarchy
- Prefab save/load
- Physics overlap queries
- Debug overlay
- Scene serialization

## Building

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code
- C++ compiler (for engine DLL)

### Build Steps

1. Build `VisualGameStudioEngine` (C++ DLL)
2. Build `BasicLang` (C# compiler/LSP)
3. Build `VisualGameStudio.Core`, `VisualGameStudio.Editor`, `VisualGameStudio.ProjectSystem`
4. Build `RaylibWrapper` (VB.NET class library)
5. Build `TestVbDLL` (VB.NET executable)

```bash
# Build all projects
dotnet build VisualGameStudioEngine.sln

# Run tests
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj

# Run IDE
dotnet run --project VisualGameStudio.Editor/VisualGameStudio.Editor.csproj
```

## Key Constants

```cpp
FW_NAME_MAX = 64    // Max entity name length
FW_PATH_MAX = 128   // Max file path length
FW_TAG_MAX  = 32    // Max tag length
```

## Engine States

```vb
Enum EngineState
    ENGINE_STOPPED = 0
    ENGINE_RUNNING = 1
    ENGINE_PAUSED = 2
    ENGINE_QUITTING = 3
End Enum
```

## Component Types

```vb
Enum ComponentType
    COMP_TRANSFORM2D = 0
    COMP_SPRITE2D = 1
    COMP_NAME = 2
    COMP_TAG = 3
    COMP_HIERARCHY = 4
    COMP_VELOCITY2D = 5
    COMP_BOXCOLLIDER2D = 6
    COMP_ENABLED = 7
End Enum
```

## Highlights

- **RayLib Powered**: Built on one of the most beginner-friendly and performance-focused game libraries available.
- **DLL Framework**: The heart of Visual Game Basic is a DLL, making it reusable, compact, and easy to integrate.
- **Multi-Language Support**: Call the same framework from Visual Basic, C++, or C#, giving you freedom of choice.
- **Full IDE**: Complete development environment with IntelliSense, debugging, and project management.
- **Comprehensive Testing**: 800+ unit tests ensuring code quality and reliability.
- **LSP Integration**: Modern editor features through Language Server Protocol.

## License

See LICENSE file for details.
