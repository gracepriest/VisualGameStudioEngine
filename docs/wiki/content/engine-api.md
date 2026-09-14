title: Engine API surface
lede: Every subsystem in `framework.h`, roughly sized, so you can tell at a glance what the engine already does for you.
---
Counts below are prefix occurrences in `VisualGameStudioEngine/framework.h` and are
indicative, not contractual — **the surface drifts, so grep to confirm before relying on
a number.** The long-form reference with worked examples is `docs/API_REFERENCE.md`.

## The unprefixed framework layer

These have no subsystem prefix: `Framework_Initialize`, `Framework_Update`,
`Framework_ShouldClose`, `Framework_Shutdown`, `Framework_Pause` / `Resume` / `Quit` /
`IsPaused` / `GetState`, the timing calls (`SetTargetFPS`, `GetFPS`, `GetFrameTime`,
`GetDeltaTime`, `GetTime`, `GetFrameCount`, `SetTimeScale`), drawing control
(`BeginDrawing`, `EndDrawing`, `ClearBackground`), shape and text drawing, texture and
image loading, render textures, keyboard and mouse input, collision checks, and font
handling.

## Gameplay systems

| Subsystem | ~Fns | What it does |
|---|---:|---|
| `Framework_Ecs_` | 133 | Entities and the eight built-in components — see [ECS](#/engine-ecs) |
| `Framework_Dialogue_` | 84 | Branching conversations and dialogue trees |
| `Framework_Quest_` (+ `QuestChain_`) | 78 | Quest definitions, objectives, state, chains |
| `Framework_Inventory_` (+ `Item_`, `Equipment_`, `LootTable_`) | 111 | Items, stacks, equipment slots, loot tables |
| `Framework_FSM_` | 50 | Finite state machines for game logic and AI |
| `Framework_BT_` | 52 | Behaviour trees for AI decision making |
| `Framework_Steer_` + `NavGrid_` + `Path_` + `AI_` | 89 | Navigation grids, A* pathfinding, path smoothing, steering behaviours |
| `Framework_Event_` | 51 | Publish/subscribe messaging |
| `Framework_Timer_` | 49 | Delayed execution and scheduling |
| `Framework_Save_` | 38 | Game state persistence — typed key/value plus arrays |
| `Framework_Achievement_` | 31 | Achievement definitions and unlock state |
| `Framework_Leaderboard_` | 24 | Score tables |
| `Framework_Cutscene_` | 37 | Scripted sequences |
| `Framework_Locale_` | 20 | Localization |
| `Framework_Net_` | 27 | Networking |

## Simulation and physics

| Subsystem | ~Fns | What it does |
|---|---:|---|
| `Framework_Physics_` | 72 | 2D rigid bodies, collision shapes, overlap queries |
| `Framework_Joint_` | 61 | Physics joints and constraints |
| `Framework_Spatial_` | 14 | Quadtree spatial partitioning for fast queries |
| `Framework_Tween_` | 64 | Property animation and interpolation, 20+ easing curves |
| `Framework_Pool_` | 41 | Object pooling with statistics |

## Rendering and presentation

| Subsystem | ~Fns | What it does |
|---|---:|---|
| `Framework_Camera_` | 48 | Smooth follow, deadzone, look-ahead, shake, zoom, rotation, pan, bounds |
| `Framework_UI_` | 65 | Retained UI elements, anchors, states, callbacks |
| `Framework_Effects_` | 66 | Screen effects |
| `Framework_Shader_` (+ `Shader` basics) | 40 | Shader load and uniform setters |
| `Framework_Lighting_` + `Light_` + `Shadow_` | 68 | Dynamic 2D lighting: ambient, point, spot, directional, shadows, blend modes |
| `Framework_Parallax_` | 22 | Parallax scrolling layers |
| `Framework_Trail_` | 21 | Trail renderer |
| `Framework_Particle_` / `Particles_` | 12 | Particle systems |
| `Framework_Batch_` | 12 | Sprite batching |
| `Framework_Atlas_` + `SpriteSheet_` + `Tileset_` | 37 | Texture atlases, sprite sheets, tilesets |
| `Framework_Culling_` | 8 | Automatic viewport render culling |
| `Framework_Render_` | 5 | Render targets |
| `Framework_Color_` | 6 | HSV conversion, lerp, utilities |

## Animation

| Subsystem | ~Fns | What it does |
|---|---:|---|
| `Framework_AnimCtrl_` | 70 | Animation controller — states, transitions, parameters |
| `Framework_Skeleton_` | 44 | Skeletal animation |
| `Framework_AnimClip_` | 10 | Clip definitions |
| `Framework_Animators_` | 1 | Global animator count |

## Content and scenes

| Subsystem | ~Fns | What it does |
|---|---:|---|
| `Framework_Scene_` | 33 | Scene stack, 14 transition types, 21 easing curves, loading screens, preloading |
| `Framework_Level_` | 67 | Level editor system |
| `Framework_Tilemap_` / `Tilemaps_` | 3 | Tilemaps and tilemap collision integration |
| `Framework_Prefab_` | 5 | Prefabs and serialization |
| `Framework_Resource_` | 47 | Resource validation and leak detection |
| `Framework_Asset_` | 30 | Ref-counted handle cache for textures, fonts, music |
| `Framework_Async_` | 12 | Async asset loading |

## Audio

`Framework_Audio_` — **134 functions**, the largest single subsystem:

- Group volume control and per-group sound assignment (`AudioGroup` enum)
- Spatial 2D audio positioning
- Sound pooling for frequently repeated sounds
- Streaming music with groups, crossfading, and playlists
- Filters (low-pass, high-pass, band-pass), reverb, echo, distortion, compressor
- Audio buses, snapshots (preset configurations), and analysis

## Input

| Subsystem | ~Fns | What it does |
|---|---:|---|
| `Framework_Input_` | 49 | Action-based input manager, plus direct gamepad queries that bypass it |
| unprefixed | — | Keyboard, mouse, cursor control, touch and gestures |

## Tooling, debug and performance

| Subsystem | ~Fns | What it does |
|---|---:|---|
| `Framework_Debug_` + `DebugDraw_` | 30 | Debug overlay: collider bounds, hierarchy lines, entity stats, world-space debug drawing |
| `Framework_Perf_` | 32 | Frame timing, draw-call tracking, profiling scopes, performance graphs |
| `Framework_Memory_` | 11 | Memory tracking |
| `Framework_Budget_` | 8 | Frame budget system |
| `Framework_Console_` + `Cmd_` | 45 | On-screen command console and command registration |
| `Framework_Log_` | 4 | Log levels and output |
| `Framework_Component_` | 11 | Runtime component introspection — read/write any field by entity + type + index |
| `Framework_Settings_` | 11 | Engine settings |
| `Framework_Random_` | 12 | MT19937 random number generator |
| `Framework_Entity_` | 3 | Entity-level helpers |

## Utility

Bezier curves and splines · gradient drawing · nine-slice drawing · additional shape
drawing · text measurement · window and display utilities · screenshot and recording ·
a standalone sprite animation player.

## Reading the header

`framework.h` is organised by uppercase section banners, so the fastest way to explore is
to grep for them:

```powershell
Select-String -Path VisualGameStudioEngine/framework.h -Pattern '^\s*// [A-Z]{3,}'
```

Every declaration is `__declspec(dllexport)` with `extern "C"` linkage. Every one of them
must have a matching `<DllImport>` in the wrapper — see
[Engine ⇄ wrapper](#/engine-binding).
