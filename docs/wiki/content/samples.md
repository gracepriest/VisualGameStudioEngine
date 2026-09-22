title: Samples
lede: Buildable projects, single-file sources, and the test games that exercise the engine from both sides.
---
## Buildable BasicLang projects

`.blproj` projects you can open in the IDE or build from the CLI. Each has its own
README.

| Sample | Location | Shows |
|---|---|---|
| Pong | `SampleGames/Pong/` | Input, collision, scoring, a complete loop |
| Space Shooter | `SampleGames/SpaceShooter/` | `Select Case` state machine, enemy spawning, a projectile, scaled-integer math — source is `Main.bl` (`.bl` is a supported extension) |

```powershell
cd SampleGames/Pong
../../IDE/BasicLang.exe run
```

## Single-file BasicLang sources

`Samples/` holds three loose single-file `.bas` programs with no `.blproj` — Pong, Space
Shooter and a Platformer, shorter and different in source from the `SampleGames/` projects
of the same name — useful when
you want to read one program top to bottom rather than navigate a project:

- `Samples/Pong/`
- `Samples/SpaceShooter/`
- `Samples/Platformer/`

> [trap] **Only `Samples/Platformer/` uses the BasicLang framework surface.** It calls
> `GameInit` / `GameShouldClose` / `GameShutdown` and defines its own `KEY_*` constants —
> the names the compiler actually registers. `Samples/Pong/Main.bas` and
> `Samples/SpaceShooter/Main.bas` call the raw native exports (`Framework_Initialize`,
> `Framework_SetFixedStep`, `Framework_BeginDrawing`, …) and Pong also uses undeclared
> `KEY_*` constants; none of the 134 entries in `BasicLang/StdLib/FrameworkStdLib.cs` is
> `Framework_`-prefixed, so those two raise `Undefined identifier`. Reach for the Platformer
> or a `SampleGames/` project when you want something that compiles as-is.

Compile one directly:

```powershell
IDE/BasicLang.exe Samples/Platformer/Main.bas --target=csharp
IDE/BasicLang.exe Samples/Platformer/Main.bas --target=cpp --show-generated
```

## VB.NET samples

`TestVbDLL/` exercises the engine through the P/Invoke wrapper — the managed half of the
[binding invariant](#/engine-binding).

| File | What it is |
|---|---|
| `SampleA_FrameworkOnly.vb` | "Catch the Falling Blocks" — window, loop, keyboard input, 2D rendering, audio, delta time, pause/resume. Reference source only: `Program.vb` has no switch for it |
| `Game.vb` (+ `Scene.vb`, `GameScenes.vb`, `Paddle.vb`, `ball.vb`, `AI.vb`, `Player.vb`) | The default program — `TestVbDLL.exe` with no args. Starts on `TitleScene`; `GameScenes.vb` holds 30 `Scene` subclasses (Pong title/menu/play/serve/end plus per-system demos). Engine + wrapper end to end |
| `SampleShapesBatch1.vb` | raylib 5.5 shapes Batch 1 smoke scene — `DrawRectangleRec`/`Pro`, `DrawLineStrip`, `DrawRectangleGradientEx`. Run `TestVbDLL.exe --shapes` |
| `SampleTextBatch2.vb` | raylib 5.5 text Batch 2 smoke scene — `GetFontDefault`, `DrawTextEx`/`Pro`, `DrawTextCodepoint`, `MeasureTextExV`. Run `TestVbDLL.exe --text` |
| `SampleTextures3d.vb` | raylib 5.5 textures Batch 3d smoke scene — 8 GL-only texture/image round-trips with self-asserts. Run `TestVbDLL.exe --textures3d` |
| `FrameworkTests.vb` | 8,067 lines; `RunAllTests` runs 49 per-system / integration / stress suites over the exported surface and prints a pass/fail tally. Run `TestVbDLL.exe --test` (or `-t`) |

## Native C++ sample

`CPPengineTest/` is a native smoke test and game loop that links the engine directly,
without the wrapper. If something works here but not in `TestVbDLL/`, the problem is the
wrapper, not the engine.

## Scratch projects

`TestGame/`, `TestMultiFile/` and `TestWinForms/` are scratch fixtures used during manual
and integration testing. `TestMultiFile/` is the only one with a `.blproj` (multi-file import
resolution over `Main.bas`/`MathUtils.bas`/`StringUtils.bas`, plus a loose `Simple.bas` and a
`GameTypes.bh` header outside its ItemGroup); `TestWinForms/` is a single `.bas` exercising
`Using System.Windows.Forms` .NET interop; and `TestGame/FeatureTest.bas` is an IDE
editor-feature fixture. They are not curated samples, but they are the quickest place to reproduce
a project-shape bug.

## Templates as samples

Most of the eleven built-in templates are a working program — `game`, `console`, `web`,
`webapi`, `test`, `cpp-console`, `cpp-game`. Generating one is often faster than reading a
sample:

> [note] Four are not runnable programs: `empty` emits only a `.blproj` with no source file,
> `sln` only a `.blsln` stub, and `classlib` / `cpp-library` build a library
> (`OutputType=Library`). `basiclang new` also picks up custom templates from disk — any
> directory holding a `template.json`.

```powershell
IDE/BasicLang.exe new --list
IDE/BasicLang.exe new game -n Scratch && cd Scratch && ../IDE/BasicLang.exe run
```

See [Projects and templates](#/projects).

## Worked examples in the docs

`docs/API_REFERENCE.md` opens with six complete examples — basic game loop, creating and
moving an entity, camera follow with shake, audio with groups and fades, scene
transitions, and screen shake — then continues with per-subsystem snippets.
`docs/GETTING_STARTED.md` builds one game in five steps.
