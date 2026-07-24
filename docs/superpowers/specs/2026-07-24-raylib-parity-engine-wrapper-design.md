# raylib 5.5 Parity — Engine + Wrapper (Batch 1: shapes)

**Status:** design
**Date:** 2026-07-24
**Scope of this spec:** the shared architecture for bringing `VisualGameStudioEngine.dll`
(vsg.dll) and `RaylibWrapper.vb` to full raylib 5.5.0 parity, plus the detailed design
for the **first sub-project — the `shapes` module (37 functions)**.

---

## 1. Goal

Expose every raylib 5.5.0 public function through the engine's stable C ABI and its
VB.NET P/Invoke wrapper, so that VB.NET consumers can call the full raylib surface.

- **Authoritative source:** `~/.nuget/packages/raylib/5.5.0/build/native/include/raylib.h`
  (538 `RLAPI` functions; the engine already links this exact package via
  `packages\raylib.5.5.0\build\native\raylib.targets`).
- **Current coverage:** ~145 of 538 already have a `Framework_*` export.
- **Gap:** ~438 functions, distributed across raylib's modules (see §5).

### Non-goals (explicitly out of scope)

- **`FrameworkStdLib.cs` / BasicLang bindings.** This effort updates only the two layers
  the user named: vsg.dll (`framework.h` / `framework.cpp`) and `RaylibWrapper.vb`. Making
  the new functions callable from BasicLang is a separate follow-up, because BasicLang must
  first be able to express `Rectangle`/`Color` struct arguments to call hybrid signatures.
- **MSIL / LLVM backends** — out of project scope per prior decision.
- **A permanent export↔import sync test.** Verification is per-batch (§4.4), not a
  standing harness. (Noted as a worthwhile future addition, but not built here.)
- **Changing any existing export.** See §3.

---

## 2. Architecture (shared by all batches)

The plumbing already exists — this is overwhelmingly additive *declaration* work.

### 2.1 Engine layer (`framework.h` / `framework.cpp`)

`pch.h` already `#include "raylib.h"`, so every raylib struct and function is already in
scope inside the engine. Each new export is a thin forwarder:

```cpp
// framework.h
__declspec(dllexport) void Framework_DrawRectangleRec(Rectangle rec, Color color);

// framework.cpp
void Framework_DrawRectangleRec(Rectangle rec, Color color) {
    DrawRectangleRec(rec, color);
}
```

All exports keep the existing conventions: `extern "C"` linkage, `__cdecl`, `__declspec(dllexport)`.

### 2.2 Wrapper layer (`RaylibWrapper.vb` + `Utiliy.vb`)

Each engine export gets one matching `<DllImport>` in `RaylibWrapper.vb`
(`CallingConvention.Cdecl`, `CharSet`/`LPStr` for strings, matching the engine-⇄-wrapper
sync invariant). Struct definitions live in `Utiliy.vb`.

### 2.3 Marshaling boundary — the hybrid rule

This is already the de-facto pattern in the existing API (`framework.h` already declares
`Vector2 Framework_GetMousePosition()` and `Rectangle Framework_CheckCollisionRecs(Rectangle, Rectangle)`).
We make it explicit and apply it consistently to new functions:

- **By value (real `<StructLayout(Sequential)>` structs):**
  `Vector2`, `Vector3`, `Vector4`/`Quaternion`, `Matrix`, `Rectangle`, `Color`,
  `Camera2D`, `Camera3D`, `Ray`, `RayCollision`, `BoundingBox`, `Transform`, `GlyphInfo`,
  `NPatchInfo`. Several already exist in `Utiliy.vb`
  (`Color`, `Vector2`, `Rectangle`, `Texture2D`, `Image`, `RenderTexture2D`, `Camera2D`,
  `Font`, `Shader`, `TextureCubemap`, `NPatchInfo`); later batches add the missing ones.
- **Arrays of value structs** (`const Vector2 *points, int pointCount`) marshal as a
  blittable `Vector2()` array (`<MarshalAs(UnmanagedType.LPArray)>`, default for a
  `Sequential` struct array) plus a separate `pointCount As Integer`.
- **By opaque handle (`Integer`):** resources whose raylib struct holds raw C pointers —
  `Model`, `Mesh`, `Material`, `ModelAnimation`, `Wave`, `Sound`, `Music`, `AudioStream`,
  `VrStereoConfig`. These extend the engine's existing handle-registry pattern
  (`AcquireTextureH` / `ReleaseTextureH`). .NET never receives their internal pointers.
  *(Pre-existing struct-based resource exports such as the `Texture2D`/`Font`/`Shader`
  forms are left as they are — see §3.)*

### 2.4 Return-by-value

Functions returning a value struct (`Vector2 GetSplinePointLinear(...)`) return it by
value across the ABI; the wrapper declares a `Function ... As Vector2`. This is already
proven by `Framework_GetMousePosition() As Vector2`.

---

## 3. The additive-only guarantee

**No existing export is renamed, removed, or has its signature changed.**

- New functions use raylib's own names where the flat name is free. `Framework_DrawRectangleRec`
  does not collide with the existing int-based `Framework_DrawRectangle`; both ship.
- Where a raylib function overlaps *functionally* with an existing differently-named export
  (e.g. raylib's `DrawSplineCatmullRom` vs the pre-existing `Framework_DrawSpline`), both are
  kept. The raylib-named export is the faithful passthrough; the older one is untouched.
- This is what unblocks the float-position problem discovered earlier: the correct fix is the
  *additive* `Framework_DrawRectangleRec(Rectangle, Color)` / `Framework_DrawRectangleV`, not a
  change to the int-based `Framework_DrawRectangle`.

---

## 4. Batch 1 — `shapes` (37 functions)

Chosen as batch 1 because it contains the float-rectangle functions that motivated this work
and requires **no new structs** (only `Vector2`, `Rectangle`, `Color`, `Texture2D` — all
already defined in both layers), so it validates the entire pipeline end-to-end at the lowest
risk.

### 4.1 Function inventory

Grouped by marshaling shape (all signatures from raylib 5.5.0):

**A. Simple value-struct params (no arrays, no return struct) — 19**
```
void DrawPixelV(Vector2 position, Color color)
void DrawLineV(Vector2 startPos, Vector2 endPos, Color color)
void DrawLineEx(Vector2 startPos, Vector2 endPos, float thick, Color color)
void DrawLineBezier(Vector2 startPos, Vector2 endPos, float thick, Color color)
void DrawCircleGradient(int centerX, int centerY, float radius, Color inner, Color outer)
void DrawCircleV(Vector2 center, float radius, Color color)
void DrawCircleLinesV(Vector2 center, float radius, Color color)
void DrawRectangleV(Vector2 position, Vector2 size, Color color)
void DrawRectangleRec(Rectangle rec, Color color)
void DrawRectanglePro(Rectangle rec, Vector2 origin, float rotation, Color color)
void DrawRectangleGradientV(int posX, int posY, int width, int height, Color top, Color bottom)
void DrawRectangleGradientH(int posX, int posY, int width, int height, Color left, Color right)
void DrawRectangleGradientEx(Rectangle rec, Color topLeft, Color bottomLeft, Color topRight, Color bottomRight)
void DrawRectangleLinesEx(Rectangle rec, float lineThick, Color color)
void DrawRectangleRoundedLinesEx(Rectangle rec, float roundness, int segments, float lineThick, Color color)
void DrawPolyLinesEx(Vector2 center, int sides, float radius, float rotation, float lineThick, Color color)
void DrawSplineSegmentLinear(Vector2 p1, Vector2 p2, float thick, Color color)
void DrawSplineSegmentBasis(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float thick, Color color)
void DrawSplineSegmentCatmullRom(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float thick, Color color)
```
(plus `DrawSplineSegmentBezierQuadratic`, `DrawSplineSegmentBezierCubic` — same shape)

**B. `Vector2[]` array params — 8**
```
void DrawLineStrip(const Vector2 *points, int pointCount, Color color)
void DrawTriangleFan(const Vector2 *points, int pointCount, Color color)
void DrawTriangleStrip(const Vector2 *points, int pointCount, Color color)
void DrawSplineLinear(const Vector2 *points, int pointCount, float thick, Color color)
void DrawSplineBasis(const Vector2 *points, int pointCount, float thick, Color color)
void DrawSplineCatmullRom(const Vector2 *points, int pointCount, float thick, Color color)
void DrawSplineBezierQuadratic(const Vector2 *points, int pointCount, float thick, Color color)
void DrawSplineBezierCubic(const Vector2 *points, int pointCount, float thick, Color color)
```

**C. Returns `Vector2` — pure math, no GL context — 5**
```
Vector2 GetSplinePointLinear(Vector2 startPos, Vector2 endPos, float t)
Vector2 GetSplinePointBasis(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float t)
Vector2 GetSplinePointCatmullRom(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float t)
Vector2 GetSplinePointBezierQuad(Vector2 p1, Vector2 c2, Vector2 p3, float t)
Vector2 GetSplinePointBezierCubic(Vector2 p1, Vector2 c2, Vector2 c3, Vector2 p4, float t)
```

**D. Shapes-texture state (Texture2D by value) — 3**
```
void      SetShapesTexture(Texture2D texture, Rectangle source)
Texture2D GetShapesTexture(void)
Rectangle GetShapesTextureRectangle(void)
```

Total: 19 + 8 + 5 + 3 = **35** distinct signatures shown; the two additional
`DrawSplineSegmentBezier*` bring the batch to **37**.

### 4.2 Naming

All 37 use their exact raylib names with the `Framework_` prefix. None collide with an
existing export (verified: each is absent from `framework.h`, including `H`/`F`/`Ex` suffix
variants). Functional overlaps with older exports (splines, bezier) are retained per §3.

### 4.3 Struct prerequisites

None new. `Vector2`, `Rectangle`, `Color`, `Texture2D` are already defined in both
`framework.h` (via raylib.h) and `Utiliy.vb`. Group D returns `Texture2D`/`Rectangle` by
value — the wrapper already round-trips these structs, so no new marshaling infra.

### 4.4 Verification plan

1. **Build vsg.dll** via VS 2022 MSBuild on `VisualGameStudioEngine.vcxproj` (x64/Release),
   auto-discovered through vswhere (confirmed available: VS 2022 Enterprise). Zero warnings
   on the new code.
2. **Build the wrapper** (`RaylibWrapper.vbproj`, Release).
3. **Parity check:** the 37 new `__declspec(dllexport)` in `framework.h` each have exactly
   one matching `<DllImport>` in `RaylibWrapper.vb`, with argument types consistent with the
   hybrid rule (§2.3). Checked by a one-off script during the batch; not a standing test.
4. **Smoke program (VB.NET):** a short program that opens a window and calls a
   representative subset in a real draw loop — at minimum `DrawRectangleRec`,
   `DrawRectanglePro`, `DrawLineStrip` (array path), and `DrawRectangleGradientEx` — plus the
   int/float coexistence check that the pre-existing `Framework_DrawRectangle` still resolves.

### 4.5 Testing (TDD)

- **Group C (5 pure-math functions)** are genuinely unit-testable with known values and are
  written test-first: e.g. `GetSplinePointLinear((0,0),(10,0),0.5)` == `(5,0)`;
  `GetSplinePointBezierCubic` endpoints at `t=0`/`t=1` equal the first/last control points.
  These run in the existing NUnit suite through the wrapper (no GL context required).
- **Groups A, B, D (drawing / GL-state)** need a live GL context and cannot run headless in
  NUnit; they are covered by the §4.4 smoke program and the build-parity check rather than by
  unit tests. This asymmetry is called out so the "green suite" is not mistaken for full
  coverage of the drawing functions.

---

## 5. Remaining sub-projects (future specs)

Each is its own spec → plan → implement → verify cycle, in roughly this order:

| Order | Module | Missing | New structs introduced |
|------:|--------|--------:|------------------------|
| 1 | **shapes** (this spec) | 37 | none |
| 2 | text | 40 | `GlyphInfo`, codepoint arrays |
| 3 | textures | 93 | `Image` handling, color arrays |
| 4 | audio | 52 | handles: `Wave`, `Sound`, `Music`, `AudioStream` |
| 5 | core | 127 | `Vector2`/`Matrix` maths, `VrStereoConfig`, automation events, file I/O |
| 6 | rgestures | 8 | none |
| 7 | rcamera | 2 | `Camera3D` |
| 8 | models (3D) | 75 | handles: `Model`, `Mesh`, `Material`, `ModelAnimation`; `Ray`, `RayCollision`, `BoundingBox` |

3D (`models`) is intentionally last: it exercises the handle pattern hardest and is furthest
from the 2D use cases driving the work.

---

## 6. Risks

- **Engine-⇄-wrapper drift.** The root cause of the original bug was a type declared
  inconsistently across layers with nothing checking it. Mitigation: the §4.4 parity check
  each batch, and preferring raylib's exact signatures so the two layers copy the same source
  of truth.
- **Native rebuild required per batch.** Each batch needs a VS 2022 MSBuild of the C++ engine
  and a refresh of the prebuilt `IDE/` binaries. Confirmed feasible in this environment.
- **Array marshaling** (Group B) is the only non-trivial marshaling in batch 1; the smoke
  program must exercise at least one array function to prove it before later batches rely on
  the same pattern.
