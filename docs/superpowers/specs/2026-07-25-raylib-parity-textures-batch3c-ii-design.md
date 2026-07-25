# raylib 5.5 Parity — Textures Batch 3c-ii (Image software drawing) — Design

**Status:** design
**Date:** 2026-07-25
**Scope:** the second half of textures sub-batch 3c. raylib's rtextures was split
`3a Color/pixel (17, shipped)` → `3b Image-in-RAM (22, shipped)` → `3c Image mutate + software draw`
→ `3d Texture GPU (4)`; 3c itself split into **3c-i Image mutators (22, shipped @ `a097f96`)** and
**3c-ii software drawing (20, this spec)**. 3c-ii binds the `// Image drawing functions` (CPU
software renderer) group. It inherits the **ByRef-`Image` contract** proven in 3c-i and adds the
`Vector2`/`Rectangle`-by-value, `Vector2*`-array, by-value-`Image`-src, and multi-Color argument
shapes that 3c-i deferred. Shared architecture + conventions from the master spec and shipped
[[raylib-parity-textures-batch3c-i]] / [[raylib-parity-shapes-batch1]].

---

## 1. Coverage (raylib rtextures "Image drawing functions" group — raylib.h 1387–1408)

The group is 22 functions. This batch binds **the 20 CPU-drawing functions**; the 2 font/window-
dependent ones defer.

| Bucket | Count | Disposition |
|---|---|---|
| **Software drawing — this batch** | **20** | bind now (§3) |
| Font/window-dependent | 2 | **defer** — `ImageDrawText`, `ImageDrawTextEx` (need `InitWindow`/default font → the GUI/font batch with 3d) |

20 + 2 = 22. ✔ **Zero name collisions** — verified: `Framework_ImageDraw*` / `Framework_ImageClearBackground`
are all absent from framework.h (grep = 0). The existing *screen*-drawing helpers are `Framework_DrawRectangle`/
`Framework_DrawCircle`/… (no `Image` prefix) — different symbols, no clash.

Counts after this batch: exports **2603 → 2623 (+20)**, imports **2535 → 2555 (+20)**.

---

## 2. Conventions

### 2.1 ByRef-`Image` dst (inherited from 3c-i)
Every function takes `Image *dst`. Engine `void Framework_ImageDrawXxx(Image* dst, …)`; wrapper
`ByRef dst As Image`. Unlike the 3c-i mutators, the software-draw functions write pixels **into the
existing `dst->data` buffer and do NOT reallocate** (the image keeps its size/format), so a by-value
`Image` would technically also mutate the caller's pixels (the `data` pointer is shared). We
nevertheless use **`ByRef` uniformly** — it matches the 3c-i contract, is self-documenting ("this
writes to dst"), and is future-proof against any draw that might one day realloc. Thin forwarder,
pointer passed straight through.

### 2.2 Color params → decomposed `unsigned char r,g,b,a` (house rule)
Reassembled `Color{r,g,b,a}` in the forwarder. The one multi-Color signature is
**`ImageDrawTriangleEx`** (3 colors) → descriptive prefixes `c1R,c1G,c1B,c1A, c2R,c2G,c2B,c2A,
c3R,c3G,c3B,c3A` (never a bare `or`).

### 2.3 The new argument shapes (what 3c-i deferred)
- **`Vector2` by value** (`DrawPixelV`, `DrawLineV/Ex`, `DrawCircleV`, `DrawCircleLinesV`,
  `DrawRectangleV`, all triangles) — blittable, passed by value; reuse the existing `Vector2` struct
  (proven throughout shapes). Wrapper `position As Vector2`.
- **`Rectangle` by value** (`DrawRectangleRec`, `DrawRectangleLines`, and `ImageDraw`'s `srcRec`/
  `dstRec`) — existing struct, by value.
- **⚠ `ImageDraw(Image* dst, Image src, Rectangle srcRec, Rectangle dstRec, Color tint)`** — the
  mixed-passing signature: `dst` is `Image*` (ByRef), `src` is a **by-value `Image`** (read-only,
  never mutated). This is exactly the `ImageAlphaMask` shape from 3c-i. Forwarder passes `src` by
  value (never `&src`). Wrapper: `ByRef dst As Image, src As Image, srcRec As Rectangle, dstRec As
  Rectangle, r,g,b,a As Byte`.
- **`Vector2* points, int pointCount`** (`DrawTriangleFan`, `DrawTriangleStrip`) → engine
  `Vector2* points, int pointCount` (match raylib's non-const signature; no cast); wrapper **bare
  `points As Vector2()` + `pointCount As Integer`, NO `<MarshalAs>`** (blittable array pins in place;
  the shapes-batch-1 `Framework_CheckCollisionPointPoly` / spline-array precedent). **Faithful
  passthrough** — no null/empty guard added; raylib owns bounds behavior (caller must pass a valid
  array + matching count).

### 2.4 Placement & mechanics (identical to 3c-i)
- `framework.h`: 20 `__declspec(dllexport)` decls in a new `Batch 3c-ii` banner block, immediately
  after the 3c-i block (inside the single `extern "C"` region).
- `framework.cpp`: 20 forwarders in the same order, after the 3c-i forwarders.
- `RaylibWrapper.vb`: one `#Region "Raylib Image software drawing (Batch 3c-ii)"`, name-for-name,
  `CallingConvention.Cdecl`, `ENGINE_DLL`, no `EntryPoint:=` remap, no `CharSet` (no strings).
- No new structs (`Image`, `Vector2`, `Rectangle`, byte-`Color` all exist).

---

## 3. The 20 functions

`u8` = `unsigned char`. Every `Image* dst` binds `ByRef dst As Image`. Color → `u8 r,g,b,a`.

| # | raylib 5.5 | engine export | wrapper param tail |
|--|--|--|--|
| 1 | `void ImageClearBackground(Image*, Color)` | `Framework_ImageClearBackground(Image* dst, u8 r,g,b,a)` | `r..a As Byte` |
| 2 | `void ImageDrawPixel(Image*, int posX, int posY, Color)` | `Framework_ImageDrawPixel(Image* dst, int posX, int posY, u8 r,g,b,a)` | `posX,posY As Integer, r..a As Byte` |
| 3 | `void ImageDrawPixelV(Image*, Vector2 position, Color)` | `Framework_ImageDrawPixelV(Image* dst, Vector2 position, u8 r,g,b,a)` | `position As Vector2, r..a As Byte` |
| 4 | `void ImageDrawLine(Image*, int sX,sY,eX,eY, Color)` | `Framework_ImageDrawLine(Image* dst, int startPosX, int startPosY, int endPosX, int endPosY, u8 r,g,b,a)` | `startPosX..endPosY As Integer, r..a As Byte` |
| 5 | `void ImageDrawLineV(Image*, Vector2 start, Vector2 end, Color)` | `Framework_ImageDrawLineV(Image* dst, Vector2 start, Vector2 end, u8 r,g,b,a)` | `start, end As Vector2, r..a As Byte` |
| 6 | `void ImageDrawLineEx(Image*, Vector2 start, Vector2 end, int thick, Color)` | `Framework_ImageDrawLineEx(Image* dst, Vector2 start, Vector2 end, int thick, u8 r,g,b,a)` | `start,end As Vector2, thick As Integer, r..a As Byte` |
| 7 | `void ImageDrawCircle(Image*, int centerX, int centerY, int radius, Color)` | `Framework_ImageDrawCircle(Image* dst, int centerX, int centerY, int radius, u8 r,g,b,a)` | `centerX,centerY,radius As Integer, r..a As Byte` |
| 8 | `void ImageDrawCircleV(Image*, Vector2 center, int radius, Color)` | `Framework_ImageDrawCircleV(Image* dst, Vector2 center, int radius, u8 r,g,b,a)` | `center As Vector2, radius As Integer, r..a As Byte` |
| 9 | `void ImageDrawCircleLines(Image*, int centerX, int centerY, int radius, Color)` | `Framework_ImageDrawCircleLines(Image* dst, int centerX, int centerY, int radius, u8 r,g,b,a)` | `centerX,centerY,radius As Integer, r..a As Byte` |
| 10 | `void ImageDrawCircleLinesV(Image*, Vector2 center, int radius, Color)` | `Framework_ImageDrawCircleLinesV(Image* dst, Vector2 center, int radius, u8 r,g,b,a)` | `center As Vector2, radius As Integer, r..a As Byte` |
| 11 | `void ImageDrawRectangle(Image*, int posX, int posY, int width, int height, Color)` | `Framework_ImageDrawRectangle(Image* dst, int posX, int posY, int width, int height, u8 r,g,b,a)` | `posX,posY,width,height As Integer, r..a As Byte` |
| 12 | `void ImageDrawRectangleV(Image*, Vector2 position, Vector2 size, Color)` | `Framework_ImageDrawRectangleV(Image* dst, Vector2 position, Vector2 size, u8 r,g,b,a)` | `position,size As Vector2, r..a As Byte` |
| 13 | `void ImageDrawRectangleRec(Image*, Rectangle rec, Color)` | `Framework_ImageDrawRectangleRec(Image* dst, Rectangle rec, u8 r,g,b,a)` | `rec As Rectangle, r..a As Byte` |
| 14 | `void ImageDrawRectangleLines(Image*, Rectangle rec, int thick, Color)` | `Framework_ImageDrawRectangleLines(Image* dst, Rectangle rec, int thick, u8 r,g,b,a)` | `rec As Rectangle, thick As Integer, r..a As Byte` |
| 15 | `void ImageDrawTriangle(Image*, Vector2 v1, Vector2 v2, Vector2 v3, Color)` | `Framework_ImageDrawTriangle(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, u8 r,g,b,a)` | `v1,v2,v3 As Vector2, r..a As Byte` |
| 16 | `void ImageDrawTriangleEx(Image*, Vector2 v1,v2,v3, Color c1,c2,c3)` | `Framework_ImageDrawTriangleEx(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, u8 c1R,c1G,c1B,c1A, u8 c2R,c2G,c2B,c2A, u8 c3R,c3G,c3B,c3A)` | `v1,v2,v3 As Vector2, c1R..c3A As Byte` |
| 17 | `void ImageDrawTriangleLines(Image*, Vector2 v1,v2,v3, Color)` | `Framework_ImageDrawTriangleLines(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, u8 r,g,b,a)` | `v1,v2,v3 As Vector2, r..a As Byte` |
| 18 | `void ImageDrawTriangleFan(Image*, Vector2* points, int pointCount, Color)` | `Framework_ImageDrawTriangleFan(Image* dst, Vector2* points, int pointCount, u8 r,g,b,a)` | `points As Vector2(), pointCount As Integer, r..a As Byte` |
| 19 | `void ImageDrawTriangleStrip(Image*, Vector2* points, int pointCount, Color)` | `Framework_ImageDrawTriangleStrip(Image* dst, Vector2* points, int pointCount, u8 r,g,b,a)` | `points As Vector2(), pointCount As Integer, r..a As Byte` |
| 20 | `void ImageDraw(Image*, Image src, Rectangle srcRec, Rectangle dstRec, Color tint)` | `Framework_ImageDraw(Image* dst, Image src, Rectangle srcRec, Rectangle dstRec, u8 r,g,b,a)` | `src As Image, srcRec As Rectangle, dstRec As Rectangle, r..a As Byte` |

Representative forwarders:
```cpp
void Framework_ImageDrawRectangleRec(Image* dst, Rectangle rec, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    Color c = { r, g, b, a };
    ImageDrawRectangleRec(dst, rec, c);
}
void Framework_ImageDrawTriangleFan(Image* dst, Vector2* points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    Color c = { r, g, b, a };
    ImageDrawTriangleFan(dst, points, pointCount, c);
}
void Framework_ImageDraw(Image* dst, Image src, Rectangle srcRec, Rectangle dstRec, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    Color tint = { r, g, b, a };
    ImageDraw(dst, src, srcRec, dstRec, tint);   // src by value — NOT &src
}
void Framework_ImageDrawTriangleEx(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3,
    unsigned char c1R, unsigned char c1G, unsigned char c1B, unsigned char c1A,
    unsigned char c2R, unsigned char c2G, unsigned char c2B, unsigned char c2A,
    unsigned char c3R, unsigned char c3G, unsigned char c3B, unsigned char c3A) {
    Color c1 = { c1R, c1G, c1B, c1A }; Color c2 = { c2R, c2G, c2B, c2A }; Color c3 = { c3R, c3G, c3B, c3A };
    ImageDrawTriangleEx(dst, v1, v2, v3, c1, c2, c3);
}
```

---

## 4. Structs

**None new.** `Image` (Utiliy.vb:88, ByRef dst + by-value `ImageDraw` src), `Vector2` (:45),
`Rectangle` (:55), and the byte-decomposed `Color` convention all exist.

---

## 5. Verification (100% GL-free — no GUI smoke)

CPU software renderer; the headless correctness suite is complete coverage.

### 5.1 Parity guard
NUnit text-scan: 20 `Framework_<name>(` exports ↔ 20 `<DllImport>` imports; trailing `(` anchors
near-name pairs (`ImageDrawCircle(` vs `ImageDrawCircleV(` vs `ImageDrawCircleLines(` vs
`ImageDrawCircleLinesV(`; `ImageDraw(` vs `ImageDrawPixel(`). New file
`VisualGameStudio.Tests/Native/RaylibImageDrawParityTests.cs`, `Has.Length.EqualTo(20)`.

### 5.2 Correctness (NUnit `[Category("Integration")]`, local `[DllImport]`, self-skip on
DllNotFound/EntryPointNotFound; model on `RaylibImageMutatorTests.cs`)
Gen a blank/known `Image` (`Framework_GenImageColor`), draw into it **ByRef**, then read pixels via
`Framework_GetImageColor(x,y)` / `Framework_LoadImageColors` and assert. Because draws don't realloc,
the proof is **pixel state**, not dims. Deterministic checks:
- `ImageClearBackground(4×4, red)` → all 16 pixels == `{255,0,0,255}`.
- `ImageDrawPixel(dst, 1,1, blue)` on a black image → `(1,1)==blue`, `(0,0)` still black.
- `ImageDrawPixelV(dst, {2,2}, green)` → `(2,2)==green` (proves the by-value `Vector2` path).
- `ImageDrawRectangle(dst, 0,0, 2,2, white)` on black 4×4 → `(0,0)`&`(1,1)` white, `(3,3)` black.
- `ImageDrawRectangleRec(dst, {1,1,2,2}, white)` → interior filled, outside untouched (proves `Rectangle` by value).
- `ImageDrawLine(dst, 0,0, 3,0, X)` → the top row's endpoints set.
- `ImageDrawCircle(dst, 4,4, 3, X)` on an 8×8 → the center pixel set.
- `ImageDrawTriangle(dst, {1,1},{6,1},{3,6}, X)` on 8×8 → an interior pixel set.
- `ImageDrawTriangleEx(dst, v1,v2,v3, red,green,blue)` → interior pixel non-background (proves 3-color path).
- `ImageDrawTriangleFan(dst, points[4], 4, X)` and `ImageDrawTriangleStrip(dst, points[4], 4, X)` on 8×8
  → the `Vector2()` array marshals, an interior pixel set (proves the array path + count).
- **`ImageDraw`**: `src` = solid red 2×2, `dst` = black 4×4, `srcRec={0,0,2,2}`, `dstRec={0,0,2,2}`,
  tint white → `dst(0,0)`&`dst(1,1)` == red, `dst(3,3)` black. Proves the **by-value `Image` src** blit.
- Remaining line/circle/rectangle variants (`DrawLineV/Ex`, `DrawCircleV`, `DrawCircleLines(V)`,
  `DrawRectangleV`, `DrawRectangleLines`, `DrawTriangleLines`) → run + at least one expected pixel set.
- Free every `Image` (dst + `ImageDraw`'s src) with `Framework_UnloadImage`.

⚠ **Staging trap (standing lesson):** stage the freshly built `x64\Release\VisualGameStudioEngine.dll`+`.lib`
into `IDE\` BEFORE running; **confirm `Passed: N, Skipped: 0`** (a stale DLL self-skips green).

### 5.3 IDE refresh
Rebuild engine `.dll`+`.lib` + `RaylibWrapper.dll` into `IDE\` (shapes-batch1 playbook).

---

## 6. Risks
- **`ImageDraw` mixed passing** (§2.3) — `dst` `Image*`, `src` by-value `Image` (never `&src`).
  Directly analogous to 3c-i's `ImageAlphaMask`. Covered by the by-value-src blit test.
- **`Vector2*` array marshaling** (`DrawTriangleFan/Strip`) — bare `Vector2()` + `pointCount`, no
  `<MarshalAs>`; faithful passthrough (no null/empty guard — raylib owns bounds). Test with a valid
  4-point array. Note: `pointCount` must match the array length the caller passes.
- **`ImageDrawTriangleEx` 12 color bytes** — transpose-prone; descriptive `c1…/c2…/c3…` names + §3's
  exact table mitigate; the 3-color test asserts a non-background interior pixel.
- **`Vector2`/`Rectangle` by value** — blittable, proven in shapes; low risk. The `DrawPixelV` /
  `DrawRectangleRec` pixel assertions confirm the value marshaling end to end.
- **Color decomposition/reassembly** — every forwarder rebuilds `Color{r,g,b,a}`; the pixel-equality
  assertions catch any byte-order slip.
- **ByRef vs by-value for dst** — either works for non-realloc draws (shared `data`), but ByRef is
  used uniformly (§2.1); no correctness risk, just contract consistency.
- **Deferred-fn leakage** — `ImageDrawText`/`ImageDrawTextEx` must NOT be bound; the parity guard's
  fixed 20-name list is the backstop, and Task 6 greps for zero `Framework_ImageDrawText`.
