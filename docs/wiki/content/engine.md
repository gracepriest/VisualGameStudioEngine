title: Engine overview
lede: A Raylib-backed 2D engine behind a stable C ABI — 2,913 exports across roughly fifty subsystems.
---
`VisualGameStudioEngine.dll` is a C++ DLL whose entire public surface is
`extern "C"` / `__cdecl` functions declared in `VisualGameStudioEngine/framework.h`
(~362 KB of header, ~1.1 MB of implementation in `framework.cpp`). Nothing but plain C
types crosses the boundary: handles are `int`, strings are `const char*`, colours are
four `unsigned char` components.

That discipline is what lets BasicLang, C++, C# and VB.NET all call the same engine.

<div class="stat-row">
<div class="stat"><div class="stat-v">2,913</div><div class="stat-l">Exports</div></div>
<div class="stat"><div class="stat-v">~50</div><div class="stat-l">Subsystems</div></div>
<div class="stat"><div class="stat-v">~35K</div><div class="stat-l">Lines of C++</div></div>
<div class="stat"><div class="stat-v">C ABI</div><div class="stat-l">Boundary</div></div>
</div>

## Two layers of API

**Framework** — the immediate-mode layer. Initialise a window, poll input, draw, shut
down. Direct, stateless calls that map closely onto Raylib.

**Engine** — the retained layer built on top: an ECS, scene management, prefabs and
serialization, and roughly forty gameplay subsystems that hold their own state inside the
DLL and expose it through handles.

You can use one, the other, or both in the same program. The [getting-started](#/getting-started)
loop shows both mixed.

## Building it

The native engine builds through **Visual Studio 2022 MSBuild** on
`VisualGameStudioEngine.vcxproj` (x64/Release), auto-discovered via `vswhere`.
`dotnet build` cannot build it. Raylib arrives through `packages.config`.

## Core types in the header

```cpp
FW_NAME_MAX = 64    // max entity name length
FW_PATH_MAX = 128   // max file path length
FW_TAG_MAX  = 32    // max tag length
```

Enumerations declared at the top of `framework.h`, all mirrored in the VB.NET wrapper:

`EngineState` · `ComponentType` · `AnimLoopMode` · `PhysicsBodyType` ·
`CollisionShapeType` · `AudioGroup` · `UIElementType` · `UIAnchor` · `UIState` ·
`SceneTransitionType` · `TransitionEasing` · `TransitionState` · `TweenEasing` ·
`TweenLoopMode` · `TweenState`

```vb
Enum EngineState
    ENGINE_STOPPED = 0
    ENGINE_RUNNING = 1
    ENGINE_PAUSED = 2
    ENGINE_QUITTING = 3
End Enum
```

## Callbacks

Function-pointer typedefs let native code call back into your game: `DrawCallback`,
`SceneVoidFn`, `SceneUpdateFixedFn`, `SceneUpdateFrameFn`, `UICallback`,
`UIValueCallback`, `UITextCallback`, `PhysicsCollisionCallback`, `LoadingCallback`,
`LoadingDrawCallback`, `TweenCallback`, `TweenUpdateCallback`.

## ABI-safe introspection structs

A small set of POD structs — `Transform2DData`, `Velocity2DData`, `BoxCollider2DData`,
`SceneCallbacks` and friends — cross the boundary by value for editor-style
introspection. Everything else stays behind handles.

## Frame shape

```text
Framework_Update()             -- input, timing, physics step, systems
Framework_Camera_Update()      -- camera follow, shake, transitions
Framework_Ecs_UpdateVelocities()
Framework_BeginDrawing()
  Framework_ClearBackground(...)
  Framework_Camera_BeginMode()
    ... world-space drawing, Framework_Ecs_DrawSprites()
  Framework_Camera_EndMode()
  ... screen-space UI
Framework_EndDrawing()
Framework_UpdateAllMusic()
Framework_Audio_Update()
```

Camera mode must be closed before screen-space UI. Audio and music update once per frame,
after drawing.

## Where to go next

- [API surface](#/engine-api) — every subsystem, with export counts.
- [ECS](#/engine-ecs) — entities, the eight components, and the built-in systems.
- [Engine ⇄ wrapper](#/engine-binding) — the invariant that keeps VB.NET in sync.
- `docs/API_REFERENCE.md` — the long-form function reference with worked examples.
