title: Getting started
lede: Prerequisites, the build order that works, and a game loop you can run today.
---
## Prerequisites

| Requirement | Why |
|---|---|
| .NET 8.0 SDK | Everything managed — compiler, IDE, tests |
| Visual Studio 2022 | Required to build the native engine DLL via MSBuild (auto-discovered through `vswhere`) |
| A C++ toolchain | clang/LLVM, gcc, or MSVC — only if you compile BasicLang to native |
| `ilasm` / `llc` | Only for the MSIL and LLVM backends — both stop at generated `.il` / `.ll` and you assemble or compile them yourself. Windows already ships `ilasm.exe` under `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319` |
| Windows | The engine DLL and the managed debugger are Windows-first; the IDE shell itself is Avalonia |

If your C++ toolchain is not on `PATH`, pin it in the IDE under **Settings → C++**.
A pinned path is authoritative: a wrong one fails the build loudly instead of silently
falling back to something else.

## Fastest path: use the prebuilt drop

`IDE/` holds a hand-committed xcopy drop of the built IDE and CLI. Nothing to build:

```powershell
IDE/VisualGameStudio.exe                       # run the IDE
IDE/BasicLang.exe MyFile.bas --target=csharp   # compile a single file
IDE/BasicLang.exe build MyProject.blproj       # build a project
IDE/BasicLang.exe new --list                   # list project templates
```

> [trap] `IDE/` goes stale, and a stale drop has been mistaken for a code bug more than once.
> Refresh it with `robocopy <Shell bin> IDE /E` — **never** `/MIR`, because the engine DLL
> and its import library live only there and mirroring deletes them. Verify a refresh by
> running the deployed binary (`IDE/BasicLang.exe new --list`), never by looking at timestamps.

## Building from source

```powershell
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release   # the IDE
dotnet build BasicLang/BasicLang.csproj -c Release                             # compiler alone
```

`VisualGameStudio.Shell` is the run target. `VisualGameStudio.Editor` is a library — the
editor control — and building it does not produce an IDE.

The native engine builds separately, through VS 2022 MSBuild on
`VisualGameStudioEngine.vcxproj` (x64/Release). `dotnet build` cannot build it.

> [note] After changing any `.axaml` file, run `dotnet clean` before building. A stale
> Avalonia build cache causes runtime crashes that look nothing like a XAML error.

## Your first project

```powershell
IDE/BasicLang.exe new game -n MyGame
cd MyGame
../IDE/BasicLang.exe run
```

Templates available from the CLI include `console`, `game`, `web` (JavaScript/browser),
and C++ variants for console, library and engine projects. See
[Projects and templates](#/projects) for the full list and the `.blproj` format.

## A game loop, start to finish

The framework layer is immediate-mode: initialise, loop, shut down.

```vb
Framework_Initialize(800, 600, "My Game")
Framework_SetTargetFPS(60)
Framework_InitAudio()

While Not Framework_ShouldClose()
    Dim dt As Single = Framework_GetDeltaTime()
    Framework_Camera_Update(dt)
    Framework_Ecs_UpdateVelocities(dt)

    Framework_BeginDrawing()
    Framework_ClearBackground(30, 30, 50, 255)
    Framework_Camera_BeginMode()
        Framework_DrawText("Hello World!", 100, 100, 24, 255, 255, 255, 255)
        Framework_Ecs_DrawSprites()
    Framework_Camera_EndMode()
    Framework_EndDrawing()

    Framework_UpdateAllMusic()
    Framework_Audio_Update(dt)
End While

Framework_CloseAudio()
Framework_Shutdown()
```

Order matters. `Framework_Initialize` must come before any resource load;
`Framework_InitAudio` before any sound call; camera mode must be closed before UI is
drawn in screen space; audio and music update once per frame after drawing.

> [trap] There are two frame shapes and they do not mix. The loop above is **manual** mode:
> you call `Framework_BeginDrawing`/`Framework_EndDrawing` and `Framework_UpdateAllMusic`
> yourself. **Callback** mode is `Framework_SetDrawCallback(AddressOf OnDraw)` plus a loop
> whose entire body is `Framework_Update()` — that one call already does `BeginDrawing` →
> your callback → `EndDrawing` → `Framework_UpdateAllMusic`. Calling `Framework_Update`
> inside a manual loop draws and updates music twice per frame.

## Adding an entity

The ECS layer sits on top of the same framework calls. Components are added one at a
time to an integer entity handle:

```vb
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(player, "Player")
Framework_Ecs_SetTag(player, "player")
Framework_Ecs_AddTransform2D(player, 400, 300, 0, 1, 1)
Framework_Ecs_AddVelocity2D(player, 0, 0)
Framework_Ecs_AddBoxCollider2D(player, -20, -20, 40, 40, False)   ' last arg: isTrigger
' entity, texture, src rect (0,0,0,0 = whole texture), tint RGBA, layer
Framework_Ecs_AddSprite2D(player, texHandle, 0, 0, 0, 0, 255, 255, 255, 255, 0)
Framework_Ecs_SetEnabled(player, True)

Dim weapon As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetParent(weapon, player)     ' hierarchy: weapon follows player

If Framework_Physics_CheckEntityOverlap(player, enemy) Then
    ' handle collision
End If
```

See [ECS](#/engine-ecs) for the component set and the built-in systems.

## Running with the IDE

1. **File → New Project**, pick a language and backend. BasicLang offers **C# (.NET)**, **MSIL**, **Native C++**, **JavaScript (Web)** and **LLVM**; a plain C++ project offers **LLVM (clang++)**, **GCC (g++)** or **MSVC**.
2. Edit; IntelliSense comes up once the language server starts — watch the Output panel.
3. `Ctrl+Shift+B` builds. `F5` runs with debugging, `Ctrl+F5` without.
4. `F9` toggles a breakpoint; the gutter distinguishes bound from unbound ones.

For a native project, F5 launches `lldb-dap` and your `.bas` breakpoints still bind —
the C++ backend emits `#line` directives that map generated C++ back to BasicLang source.

## When something does not work

| Symptom | First thing to check |
|---|---|
| No IntelliSense | Output panel for LSP errors; the server auto-restarts up to 3 times, then stops — re-launch it from the command palette (`Ctrl+Shift+P` → **Start Language Server**) |
| C++ build fails with a missing compiler | **Settings → C++** — is a bad path pinned? |
| Native debug never binds breakpoints | Was the build a Debug build with `#line` emission? |
| Engine function missing from VB.NET | The [wrapper invariant](#/engine-binding) — the `DllImport` may not exist |
| The IDE crashes right after a XAML edit | `dotnet clean`, then rebuild |
