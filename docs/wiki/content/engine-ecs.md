title: ECS
lede: Integer entity handles, eleven component types (eight of them introspectable), a hierarchy, and the built-in systems that run them.
---
The ECS is the retained layer on top of the immediate-mode framework. Entities are plain
`int` handles, components are added individually, and a handful of built-in systems do
the per-frame work.

> [trap] `Framework_Ecs_CreateEntity()` hands out ids from a monotonic counter and never
> reuses one, so a stale handle can never alias a new entity — but it returns **-1** once
> 100000 entities are alive (`MAX_ENTITIES`). It does not throw. Every `Framework_Ecs_Add*`
> call on -1 is a silent no-op, so check the return value in any loop that spawns.

## Entities

```vb
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(player, "Player")     ' FW_NAME_MAX = 64
Framework_Ecs_SetTag(player, "player")      ' FW_TAG_MAX  = 32
Framework_Ecs_SetEnabled(player, True)
```

## The eleven component types

`ComponentType` is a stable enum — the IDs are part of the ABI, because introspection
addresses components by them:

```cpp
// framework.h — C++ only; RaylibWrapper.vb has no ComponentType enum,
// VB callers pass these as plain Integer.
enum ComponentType {
    COMP_NONE             = 0,
    COMP_TRANSFORM2D      = 1,
    COMP_SPRITE2D         = 2,
    COMP_NAME             = 3,
    COMP_TAG              = 4,
    COMP_HIERARCHY        = 5,
    COMP_VELOCITY2D       = 6,
    COMP_BOXCOLLIDER2D    = 7,
    COMP_ENABLED          = 8,
    COMP_TILEMAP          = 9,
    COMP_ANIMATOR         = 10,
    COMP_PARTICLE_EMITTER = 11,
    COMP_COUNT  // = 12, keep last
};
```

> [trap] The IDs start at **1**, not 0 — `COMP_NONE = 0` is the "no component" sentinel
> that `Framework_Entity_GetComponentTypeAt` returns when the entity is dead or the index
> is out of range. Introspection only understands ids **1-8**; `COMP_TILEMAP`,
> `COMP_ANIMATOR` and `COMP_PARTICLE_EMITTER` exist on entities but
> `Framework_Entity_HasComponent` and `Framework_Component_GetFieldCount` return
> `false`/`0` for them.

| Component | Added with | Holds |
|---|---|---|
| Transform2D | `Framework_Ecs_AddTransform2D(e, x, y, rot, sx, sy)` | Position, rotation, scale |
| Sprite2D | `Framework_Ecs_AddSprite2D(e, tex, srcX, srcY, srcW, srcH, r, g, b, a, layer)` | Texture handle, source rect, tint, layer, visible flag |
| Velocity2D | `Framework_Ecs_AddVelocity2D(e, vx, vy)` | Linear velocity |
| BoxCollider2D | `Framework_Ecs_AddBoxCollider2D(e, ox, oy, w, h, isTrigger)` | Offset, size, trigger flag |
| Name | `Framework_Ecs_SetName(e, "…")` | Up to 63 chars — `FW_NAME_MAX` is 64 **including** the NUL; longer names truncate silently |
| Tag | `Framework_Ecs_SetTag(e, "…")` | Up to 31 chars — `FW_TAG_MAX` is 32 **including** the NUL; longer tags truncate silently |
| Hierarchy | `Framework_Ecs_SetParent(child, parent)` | Parent/child links |
| Enabled | Added automatically by `Framework_Ecs_CreateEntity()`; toggle with `Framework_Ecs_SetEnabled(e, False)` | Active flag (defaults to `True`) |

## Hierarchy

```vb
Dim weapon As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetParent(weapon, player)
```

A child's transform composes with its parent's — but only partially, and the gap bites.

> [trap] Composition is **not** a matrix multiply. `Framework_Ecs_GetWorldPosition` simply
> adds the parent's world position to the child's local position: the offset is never
> rotated by the parent's world rotation, nor multiplied by the parent's world scale.
> World rotation is a plain sum and world scale a plain product, so the child *renders*
> rotated and scaled — it just does not orbit its parent or move outward when the parent
> scales. Translating a parent moves its children correctly; rotating or scaling one does
> not. Build turret-on-tank offsets yourself if you need true orbiting.
The debug overlay can draw hierarchy lines, which is the fastest way to see a broken
parent link.

## Built-in systems

Call these yourself, in the order your game needs — the engine does not hide a scheduler
from you:

| Call | Does |
|---|---|
| `Framework_Ecs_UpdateVelocities(dt)` | Integrates Velocity2D into Transform2D; skips entities that are not active in hierarchy or have no Transform2D |
| `Framework_Ecs_DrawSprites()` | Draws Sprite2Ds that are visible, alive, active in hierarchy and have a Transform2D — sorted ascending by `layer` |
| `Framework_Animators_Update(dt)` | Advances every playing Animator on a live entity and writes the current clip frame into that entity's Sprite2D source rect |
| `Framework_Particles_Update(dt)` / `Framework_Particles_Draw()` | Simulates and renders the ParticleEmitter on every live entity |
| `Framework_Tilemaps_Draw()` | Draws every Tilemap on a live, enabled entity (`Framework_Ecs_DrawTilemap(e)` draws just one) |

Physics, camera, audio and the rest are separate subsystems with their own update calls.

## Collision

```vb
If Framework_Physics_CheckEntityOverlap(player, enemy) Then
    ' ...
End If
```

`Framework_Physics_` also offers box and circle overlap queries against the world
(`Framework_Physics_OverlapBox`, `Framework_Physics_OverlapCircle`,
`Framework_Physics_GetOverlappingEntities`) — none of which filter on the Enabled
component, so an entity you disabled keeps reporting overlaps even though
`Framework_Ecs_DrawSprites` and `Framework_Ecs_UpdateVelocities` have stopped drawing and
moving it; filter with `Framework_Ecs_IsActiveInHierarchy` yourself — and
`Framework_Spatial_` provides a **uniform grid** — `Framework_Spatial_CreateGrid(worldW, worldH, cellSize)`
plus `QueryRect`/`QueryCircle`/`QueryPoint`/`GetNearestEntity` — when a linear scan stops
being acceptable.

> [note] The comment above the declarations in `framework.h` says "Quadtree"; it is wrong.
> There is no subdivision — the grid allocates `ceil(worldW/cellSize) * ceil(worldH/cellSize)`
> cells up front, so pick `cellSize` against your world size, not your entity count.

## Introspection

`Framework_Component_` exposes runtime field read/write, addressed by entity + component
type + field index. `Framework_Entity_GetComponentCount(e)` / `Framework_Entity_GetComponentTypeAt(e, i)`
enumerate what an entity has; `Framework_Component_GetFieldCount/GetFieldName/GetFieldType`
take `(compType, fieldIndex)` with **no entity** — they are static schema. Field type codes:
`0=float, 1=int, 2=bool, 3=string`. This is what an editor or an inspector panel would use,
and it is why `ComponentType` IDs must stay stable.

| Component | id | Fields |
|---|---|---|
| Transform2D | 1 | 5 — `posX`, `posY`, `rotation`, `scaleX`, `scaleY` |
| Sprite2D | 2 | 11 — `textureHandle`, `srcX/Y/W/H`, `tintR/G/B/A`, `layer`, `visible` |
| Name | 3 | 1 — `name` |
| Tag | 4 | 1 — `tag` |
| Hierarchy | 5 | 3 — `parent`, `firstChild`, `nextSibling` |
| Velocity2D | 6 | 2 — `vx`, `vy` |
| BoxCollider2D | 7 | 5 — `offsetX`, `offsetY`, `width`, `height`, `isTrigger` |
| Enabled | 8 | 1 — `enabled` |

> [trap] Introspection stops at id 8. `COMP_TILEMAP` (9), `COMP_ANIMATOR` (10) and
> `COMP_PARTICLE_EMITTER` (11) are real components you can add to an entity, but
> `Framework_Entity_GetComponentCount`, `GetComponentTypeAt` and `HasComponent` never
> report them and `GetFieldCount` returns 0 for them. An inspector built on this API is
> blind to tilemaps, animators and emitters.

> [trap] The ABI-safe structs `Transform2DData`, `Velocity2DData` and `BoxCollider2DData`
> are declared in `framework.h` but **no export uses them** — they are dead declarations,
> and there is no matching structure in `RaylibWrapper.vb`. There is no bulk-read path
> today: introspection reads and writes one scalar field per call. Building an inspector
> means one `Framework_Component_GetField*` call per field.

## Debug overlay

`Framework_Debug_` / `Framework_DebugDraw_` draw collider bounds, hierarchy lines and
entity statistics on top of the scene, plus world-space debug shapes.

> [trap] Two switches, both required. The overlay needs `Framework_Debug_SetEnabled(True)`
> *and* its per-layer toggles (`DrawEntityBounds` / `DrawHierarchy` / `DrawStats`), and
> `Framework_Debug_Render()` must be called **after** your scene draw — it early-returns
> when the overlay is not enabled, so flipping only `Framework_Debug_DrawHierarchy(True)`
> draws nothing. Trigger colliders render green, solid ones yellow; hierarchy lines are
> drawn child-to-parent in pale blue. `Framework_DebugDraw_` is a **separate** switch:
> shapes are discarded at call time unless `Framework_DebugDraw_SetEnabled(True)`, and
> they only reach the screen when you call `Framework_DebugDraw_Flush()`. Turn it all off
> before you ship.

## Beyond the components

The ECS is deliberately small. Richer behaviour comes from the surrounding subsystems —
[animation controllers](#/engine-api), FSMs, behaviour trees, steering agents, tweens,
timers and the event bus. Most are keyed by their own handles and associated with
entities by your code — but three bind to an entity directly in the engine:
`Framework_AnimCtrl_CreateInstance(controllerId, entityId)`,
`Framework_FSM_CreateForEntity(name, entity)` and `Framework_Steer_CreateAgent(entity)`.

> [note] Steering is exported as `Framework_Steer_`, not `Framework_Steering_` — grep for
> the short form. And Tilemap, Animator and ParticleEmitter above are not "beyond" the
> ECS at all: they are real components (ids 9-11) added with `Framework_Ecs_AddTilemap`,
> `Framework_Ecs_AddAnimator` and `Framework_Ecs_AddParticleEmitter`. They are simply
> invisible to the introspection API.
