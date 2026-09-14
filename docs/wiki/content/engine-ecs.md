title: ECS
lede: Integer entity handles, eight components, a hierarchy, and the built-in systems that run them.
---
The ECS is the retained layer on top of the immediate-mode framework. Entities are plain
`int` handles, components are added individually, and a handful of built-in systems do
the per-frame work.

## Entities

```vb
Dim player As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetName(player, "Player")     ' FW_NAME_MAX = 64
Framework_Ecs_SetTag(player, "player")      ' FW_TAG_MAX  = 32
Framework_Ecs_SetEnabled(player, True)
```

## The eight component types

`ComponentType` is a stable enum — the IDs are part of the ABI, because introspection
addresses components by them:

```vb
Enum ComponentType
    COMP_TRANSFORM2D  = 0
    COMP_SPRITE2D     = 1
    COMP_NAME         = 2
    COMP_TAG          = 3
    COMP_HIERARCHY    = 4
    COMP_VELOCITY2D   = 5
    COMP_BOXCOLLIDER2D = 6
    COMP_ENABLED      = 7
End Enum
```

| Component | Added with | Holds |
|---|---|---|
| Transform2D | `Framework_Ecs_AddTransform2D(e, x, y, rot, sx, sy)` | Position, rotation, scale |
| Sprite2D | `Framework_Ecs_AddSprite2D(e)` | Texture handle, source rect, tint, origin |
| Velocity2D | `Framework_Ecs_AddVelocity2D(e, vx, vy)` | Linear velocity |
| BoxCollider2D | `Framework_Ecs_AddBoxCollider2D(e, ox, oy, w, h)` | Offset and size |
| Name | `Framework_Ecs_SetName(e, "…")` | Up to 64 chars |
| Tag | `Framework_Ecs_SetTag(e, "…")` | Up to 32 chars |
| Hierarchy | `Framework_Ecs_SetParent(child, parent)` | Parent/child links |
| Enabled | `Framework_Ecs_SetEnabled(e, True)` | Active flag |

## Hierarchy

```vb
Dim weapon As Integer = Framework_Ecs_CreateEntity()
Framework_Ecs_SetParent(weapon, player)
```

A child's transform composes with its parent's, so moving the player moves the weapon.
The debug overlay can draw hierarchy lines, which is the fastest way to see a broken
parent link.

## Built-in systems

Call these yourself, in the order your game needs — the engine does not hide a scheduler
from you:

| Call | Does |
|---|---|
| `Framework_Ecs_UpdateVelocities()` | Integrates Velocity2D into Transform2D |
| `Framework_Ecs_DrawSprites()` | Draws every enabled Sprite2D |

Physics, camera, audio and the rest are separate subsystems with their own update calls.

## Collision

```vb
If Framework_Physics_CheckEntityOverlap(player, enemy) Then
    ' ...
End If
```

`Framework_Physics_` also offers box and circle overlap queries against the world, and
`Framework_Spatial_` provides a quadtree when a linear scan stops being acceptable.

## Introspection

`Framework_Component_` exposes runtime field read/write, addressed by entity + component
type + field index. This is what an editor or an inspector panel would use, and it is why
`ComponentType` IDs must stay stable.

The ABI-safe structs `Transform2DData`, `Velocity2DData` and `BoxCollider2DData` cross the
boundary by value for bulk reads.

## Debug overlay

`Framework_Debug_` / `Framework_DebugDraw_` draw collider bounds, hierarchy lines and
entity statistics on top of the scene, plus world-space debug shapes. Turn it on while
you are building a system and off before you ship.

## Beyond the built-in eight

The ECS is deliberately small. Richer behaviour comes from the surrounding subsystems —
[animation controllers](#/engine-api), FSMs, behaviour trees, steering agents, tweens,
timers and the event bus — each keyed by its own handles and associated with entities by
your code.
