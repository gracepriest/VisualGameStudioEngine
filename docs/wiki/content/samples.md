title: Samples
lede: Buildable projects, single-file sources, and the test games that exercise the engine from both sides.
---
## Buildable BasicLang projects

`.blproj` projects you can open in the IDE or build from the CLI. Each has its own
README.

| Sample | Location | Shows |
|---|---|---|
| Pong | `SampleGames/Pong/` | Input, collision, scoring, a complete loop |
| Space Shooter | `SampleGames/SpaceShooter/` | Entities, spawning, projectiles, audio |

```powershell
cd SampleGames/Pong
../../IDE/BasicLang.exe run
```

## Single-file BasicLang sources

`Samples/` holds the same games plus a platformer as single `.bas` files — useful when
you want to read one program top to bottom rather than navigate a project:

- `Samples/Pong/`
- `Samples/SpaceShooter/`
- `Samples/Platformer/`

Compile one directly:

```powershell
IDE/BasicLang.exe Samples/Pong/pong.bas --target=csharp
IDE/BasicLang.exe Samples/Pong/pong.bas --target=cpp --show-generated
```

## VB.NET samples

`TestVbDLL/` exercises the engine through the P/Invoke wrapper — the managed half of the
[binding invariant](#/engine-binding).

| File | What it is |
|---|---|
| `SampleA_FrameworkOnly.vb` | "Catch the Falling Blocks" — window, loop, keyboard input, 2D rendering, pause/resume |
| the larger game in the project | Engine + wrapper end to end |
| `FrameworkTests.vb` | Smoke tests over the exported surface |

## Native C++ sample

`CPPengineTest/` is a native smoke test and game loop that links the engine directly,
without the wrapper. If something works here but not in `TestVbDLL/`, the problem is the
wrapper, not the engine.

## Scratch projects

`TestGame/`, `TestMultiFile/` and `TestWinForms/` are working projects used during manual
and integration testing — multi-file import resolution, WinForms output, and general
build shapes. They are not curated samples, but they are the quickest place to reproduce
a project-shape bug.

## Templates as samples

Every template is a working program. Generating one is often faster than reading a sample:

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
