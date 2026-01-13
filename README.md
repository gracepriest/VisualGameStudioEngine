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

## API Reference

### Engine State

| Function | Description |
|----------|-------------|
| `Framework_Initialize(w, h, title)` | Initialize window |
| `Framework_Update()` | Process frame |
| `Framework_ShouldClose()` | Check if window should close |
| `Framework_Shutdown()` | Cleanup and close |
| `Framework_Pause()` | Pause engine updates |
| `Framework_Resume()` | Resume engine updates |
| `Framework_Quit()` | Request engine quit |
| `Framework_GetState()` | Get engine state enum |

### Timing

| Function | Description |
|----------|-------------|
| `Framework_SetFixedStep(dt)` | Set fixed timestep (e.g., 1/60) |
| `Framework_GetDeltaTime()` | Get last frame time |
| `Framework_GetFrameTime()` | Get frame time (alias) |
| `Framework_GetFrameCount()` | Get total frames elapsed |
| `Framework_SetTimeScale(scale)` | Set time scale (0.5 = half speed) |
| `Framework_GetTimeScale()` | Get current time scale |

### Input

| Function | Description |
|----------|-------------|
| `Framework_IsKeyPressed(key)` | Key just pressed this frame |
| `Framework_IsKeyDown(key)` | Key currently held |
| `Framework_IsKeyReleased(key)` | Key just released |
| `Framework_GetMouseX/Y()` | Mouse position |
| `Framework_IsMouseButtonPressed(btn)` | Mouse button pressed |

### Drawing

| Function | Description |
|----------|-------------|
| `Framework_BeginDrawing()` | Start drawing |
| `Framework_EndDrawing()` | End drawing and swap buffers |
| `Framework_ClearBackground(r,g,b,a)` | Clear screen |
| `Framework_DrawText(...)` | Draw text |
| `Framework_DrawRectangle(...)` | Draw filled rectangle |
| `Framework_DrawCircle(...)` | Draw filled circle |
| `Framework_DrawLine(...)` | Draw line |
| `Framework_DrawTriangle(...)` | Draw filled triangle |

### ECS - Entity Management

| Function | Description |
|----------|-------------|
| `Framework_Ecs_CreateEntity()` | Create new entity, returns ID |
| `Framework_Ecs_DestroyEntity(id)` | Destroy entity |
| `Framework_Ecs_EntityExists(id)` | Check if entity exists |
| `Framework_Ecs_GetEntityCount()` | Get total entity count |

### ECS - Name & Tag

| Function | Description |
|----------|-------------|
| `Framework_Ecs_SetName(id, name)` | Set entity name (max 64 chars) |
| `Framework_Ecs_GetNamePtr(id)` | Get name as IntPtr |
| `Framework_Ecs_SetTag(id, tag)` | Set entity tag (max 32 chars) |
| `Framework_Ecs_GetTagPtr(id)` | Get tag as IntPtr |
| `Framework_Ecs_FindByName(name)` | Find entity by name |
| `Framework_Ecs_FindByTag(tag, out, max)` | Find all entities with tag |

### ECS - Transform2D

| Function | Description |
|----------|-------------|
| `Framework_Ecs_AddTransform2D(id, x, y, rot, sx, sy)` | Add transform |
| `Framework_Ecs_GetTransform2D(id, ...)` | Get transform values |
| `Framework_Ecs_SetTransform2D(id, ...)` | Set transform values |
| `Framework_Ecs_GetWorldPosition(id, x, y)` | Get world position (hierarchy-aware) |

### ECS - Hierarchy

| Function | Description |
|----------|-------------|
| `Framework_Ecs_SetParent(child, parent)` | Set parent entity (-1 = root) |
| `Framework_Ecs_GetParent(id)` | Get parent entity ID |
| `Framework_Ecs_GetFirstChild(id)` | Get first child entity |
| `Framework_Ecs_GetNextSibling(id)` | Get next sibling entity |
| `Framework_Ecs_GetChildCount(id)` | Get number of children |

### ECS - Components

| Function | Description |
|----------|-------------|
| `Framework_Ecs_AddVelocity2D(id, vx, vy)` | Add velocity component |
| `Framework_Ecs_GetVelocity2D(id, vx, vy)` | Get velocity |
| `Framework_Ecs_SetVelocity2D(id, vx, vy)` | Set velocity |
| `Framework_Ecs_AddBoxCollider2D(id, ox, oy, w, h)` | Add AABB collider |
| `Framework_Ecs_GetBoxCollider2D(...)` | Get collider bounds |
| `Framework_Ecs_SetEnabled(id, enabled)` | Enable/disable entity |
| `Framework_Ecs_IsEnabled(id)` | Check if enabled |

### Camera2D

| Function | Description |
|----------|-------------|
| `Framework_Camera2D_Create()` | Create camera, returns ID |
| `Framework_Camera2D_Destroy(id)` | Destroy camera |
| `Framework_Camera2D_Begin(id)` | Begin camera mode |
| `Framework_Camera2D_End(id)` | End camera mode |
| `Framework_Camera2D_SetTarget(id, x, y)` | Set camera target |
| `Framework_Camera2D_SetOffset(id, x, y)` | Set camera offset |
| `Framework_Camera2D_SetZoom(id, zoom)` | Set camera zoom |
| `Framework_Camera2D_SetRotation(id, rot)` | Set camera rotation |
| `Framework_Camera2D_Follow(id, entity)` | Follow entity |

### Physics Queries

| Function | Description |
|----------|-------------|
| `Framework_Physics_OverlapPoint(x, y, out, max)` | Find entities at point |
| `Framework_Physics_OverlapBox(x, y, w, h, out, max)` | Find entities in box |

### Prefabs & Serialization

| Function | Description |
|----------|-------------|
| `Framework_Prefab_SaveEntity(id, path)` | Save entity as prefab |
| `Framework_Prefab_Load(path)` | Load prefab, returns handle |
| `Framework_Prefab_Instantiate(h, parent, x, y)` | Spawn prefab instance |
| `Framework_Prefab_Unload(h)` | Unload prefab |
| `Framework_Scene_Save(path)` | Save entire scene |
| `Framework_Scene_Load(path)` | Load scene |

### Debug Overlay

| Function | Description |
|----------|-------------|
| `Framework_Debug_SetEnabled(enabled)` | Enable/disable debug |
| `Framework_Debug_DrawEntityBounds(enabled)` | Show entity colliders |
| `Framework_Debug_DrawHierarchy(enabled)` | Show hierarchy lines |
| `Framework_Debug_DrawStats(enabled)` | Show entity stats |
| `Framework_Debug_Render()` | Render debug overlay |

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
