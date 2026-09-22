title: Engine overview
lede: A Raylib-backed 2D engine behind a stable C ABI — 2,913 exports across roughly seventy subsystems.
---
`VisualGameStudioEngine.dll` is a C++ DLL whose entire public surface is
`extern "C"` functions declared in `VisualGameStudioEngine/framework.h`
(~362 KB of header, ~1.1 MB of implementation in `framework.cpp`). Almost all of it is
plain C: handles are `int`, strings are `const char*`, colours are four `unsigned char`
components.

> [note] **The boundary is not purely plain C.** Over 370 of the 2,913 exports name a
> raylib POD struct in their signature and **123 return one by value** —
> `Framework_GetMousePosition()` returns `Vector2` (framework.h:344),
> `Framework_LoadTexture()` returns `Texture2D` (:390), and the `Framework_Image*` family
> passes `Image` / `Rectangle` / `Color` by value. The header says so itself: "Raw raylib
> Sound API (struct BY VALUE)". The VB.NET side mirrors those layouts by hand — 36
> `Public Structure` declarations in `RaylibWrapper/Utiliy.vb` (`Color`, `Vector2`,
> `Rectangle`, `Texture2D`, `Image`, `Font`, `Shader`, `Camera2D`, `SceneCallbacks`, …).
> The 33 handle-based `…H` exports (`Framework_AcquireTextureH`, `Framework_PlayMusicH`, …)
> replace the resource struct with an `int`, though a few still take `Vector2` /
> `Rectangle` for position and source rect.

That discipline is what lets BasicLang, C++, C# and VB.NET all call the same engine.

<div class="stat-row">
<div class="stat"><div class="stat-v">2,913</div><div class="stat-l">Exports</div></div>
<div class="stat"><div class="stat-v">~70</div><div class="stat-l">Subsystems</div></div>
<div class="stat"><div class="stat-v">~35K</div><div class="stat-l">Lines of C++</div></div>
<div class="stat"><div class="stat-v">C ABI</div><div class="stat-l">Boundary</div></div>
</div>

## Two layers of API

**Framework** — the immediate-mode layer. Initialise a window, poll input, draw, shut
down. Direct, stateless calls that map closely onto Raylib.

**Engine** — the retained layer built on top: an ECS, scene management, prefabs and
serialization, and dozens of gameplay subsystems that hold their own state inside the
DLL and expose it through handles.

> [note] The two layers are a conceptual split, not a naming one. **Every one of the 2,913
> exports is prefixed `Framework_`** — there is no `Engine_*` symbol in the DLL. The
> retained systems are told apart by their subsystem segment (`Framework_Ecs_`,
> `Framework_Scene_`, `Framework_Inventory_`, …), of which there are about seventy.

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

Fifteen enumerations are declared at the top of `framework.h`; another twenty sit further
down, beside the subsystem that uses them (`AudioFilterType`, `LogLevel`,
`ParticleEmitterShape`, `PhysicsJointType`, `BTNodeType`, `AnimStateType`,
`InputSourceType`, `EventDataType`, `TimerState`, `SteeringBehavior`, `ItemRarity`,
`EquipSlot`, and the rest) — 35 in all. The top-of-header fifteen:

`EngineState` · `ComponentType` · `AnimLoopMode` · `PhysicsBodyType` ·
`CollisionShapeType` · `AudioGroup` · `UIElementType` · `UIAnchor` · `UIState` ·
`SceneTransitionType` · `TransitionEasing` · `TransitionState` · `TweenEasing` ·
`TweenLoopMode` · `TweenState`

> [trap] **Only six of those fifteen are mirrored in the VB.NET wrapper.**
> `SceneTransitionType`, `TransitionEasing`, `TransitionState`, `TweenEasing`,
> `TweenLoopMode` and `TweenState` are declared `Public Enum` in
> `RaylibWrapper/RaylibWrapper.vb`. `EngineState`, `ComponentType`, `AnimLoopMode`,
> `PhysicsBodyType`, `CollisionShapeType`, `AudioGroup`, `UIElementType`, `UIAnchor` and
> `UIState` have no VB counterpart at all — pass the raw `Integer`, or declare the enum in
> your own code. The block below is the C++ declaration written out in VB syntax, not
> wrapper source.

```vb
Enum EngineState
    ENGINE_STOPPED = 0
    ENGINE_RUNNING = 1
    ENGINE_PAUSED = 2
    ENGINE_QUITTING = 3
End Enum
```

## Callbacks

Function-pointer typedefs let native code call back into your game. Twelve are declared
above the `extern "C"` block: `DrawCallback`, `SceneVoidFn`, `SceneUpdateFixedFn`,
`SceneUpdateFrameFn`, `UICallback`, `UIValueCallback`, `UITextCallback`,
`PhysicsCollisionCallback`, `LoadingCallback`, `LoadingDrawCallback`, `TweenCallback`,
`TweenUpdateCallback`.

Another **36 are declared inside the `extern "C"` block**, next to the subsystem that
raises them — 48 in total. Most of these take a trailing `void* userData` the twelve above
do not: `EventCallback` (plus `Int` / `Float` / `String` / `Vector2` / `Entity` variants),
`TimerCallback` (plus `Int` / `Float`), `BTActionCallback` / `BTConditionCallback`,
`StateEnterCallback` / `StateUpdateCallback` / `StateExitCallback` / `TransitionCondition`,
`AnimStateCallback`, `DialogueCallback` / `DialogueChoiceCallback` /
`DialogueConditionCallback`, `InventoryCallback` / `ItemUseCallback` / `ItemDropCallback`,
`PoolResetCallback` / `PoolInitCallback`, `ResourceCreatedCallback` /
`ResourceDestroyedCallback`, `NetConnectCallback` / `NetDisconnectCallback` /
`NetMessageCallback`, `CmdConsoleCallback`, `QuestStateCallback` /
`ObjectiveUpdateCallback`, `LocaleChangedCallback`, `AchievementUnlockedCallback`,
`CutsceneCallback` / `CutsceneFinishedCallback`. Grep `typedef .*(\*` in `framework.h` for
the exact set.

## ABI-safe introspection structs

Exactly four POD structs are declared in the header: `Transform2DData`, `Velocity2DData`
and `BoxCollider2DData` cross the boundary by value for editor-style introspection, and
`SceneCallbacks` carries a scene's function pointers into `Framework_CreateScriptScene`.
There are no others declared here — the raylib POD types (`Vector2`, `Image`, `Texture2D`,
…) come from `raylib.h` via `pch.h`, and everything the engine itself owns stays behind
integer handles.

## Frame shape

```text
Framework_Update()             -- frame count, BeginDrawing -> draw callback -> EndDrawing, music pump, time accumulation
Framework_Camera_Update(dt)    -- camera follow, shake, transitions
Framework_Ecs_UpdateVelocities(dt)
Framework_BeginDrawing()
  Framework_ClearBackground(...)
  Framework_Camera_BeginMode()
    ... world-space drawing, Framework_Ecs_DrawSprites()
  Framework_Camera_EndMode()
  ... screen-space UI
Framework_EndDrawing()
Framework_UpdateAllMusic()
Framework_Audio_Update(dt)
```

Camera mode must be closed before screen-space UI.

> [trap] **`Framework_Update()` already opens and closes a Raylib frame, and already pumps
> music.** Its body is `BeginDrawing()` → the callback registered with
> `Framework_SetDrawCallback` → `EndDrawing()` → `Framework_UpdateAllMusic()` (unless audio
> is paused) → time accumulation (`framework.cpp:942`). Either drive the frame through a
> draw callback, or drive your own `Framework_BeginDrawing` / `Framework_EndDrawing` block
> — doing both presents twice per iteration, and the trailing
> `Framework_UpdateAllMusic()` is then a second pump.

> [note] `Framework_Update()` steps nothing else. Physics (`Framework_Physics_Step(dt)`),
> scenes (`Framework_Scene_Update(dt)`), UI (`Framework_UI_Update()`), the action-based
> input manager (`Framework_Input_Update()`), tweens (`Framework_Tween_Update(dt)`) and
> timers (`Framework_Timer_Update(dt)`) are each separate exports you call once per frame.

## Where to go next

- [API surface](#/engine-api) — every subsystem, with export counts.
- [ECS](#/engine-ecs) — entities, the eleven component types, and the built-in systems.
- [Engine ⇄ wrapper](#/engine-binding) — the invariant that keeps VB.NET in sync.
- `docs/API_REFERENCE.md` — the long-form function reference with worked examples.
