# raylib 5.5 Parity — Textures Batch 3a (Color / pixel) — Design

**Status:** design
**Date:** 2026-07-24
**Scope:** the FIRST sub-batch of the textures module (master spec
`2026-07-24-raylib-parity-engine-wrapper-design.md` §5). raylib's rtextures module is **115
functions** (22 already faithful, **93 absent**) — too large for one batch, so it is split:
**3a Color/pixel (17, this spec)** → 3b Image-in-RAM (28) → 3c Image mutate+draw (44) → 3d
Texture GPU (4). 3a is done first because it is entirely GL-free (fully unit-testable) and
settles the **Color-return** and **Vector3/Vector4** conventions every later sub-batch inherits.
Shared architecture + conventions inherited from the master spec and shipped
[[raylib-parity-shapes-batch1]] / [[raylib-parity-text-batch2]].

---

## 1. Coverage (raylib rtextures "Color/pixel" group — 17 functions)

All 17 are **ABSENT / clean** (0 already-faithful, **0 name collisions**). The engine's existing
underscore-namespaced color helpers (`Framework_Color_FromHSV`, `Framework_Color_ToHSV`,
`Framework_Color_Lerp`, `Framework_Color_Alpha`, `Framework_Color_Brighten`, `Framework_Color_Invert`)
**semantically overlap but do NOT collide** — the faithful raylib names (`Framework_ColorTint`,
`Framework_ColorFromHSV`, …) differ by the underscore, so both coexist per the additive rule. The
existing helpers are left untouched.

---

## 2. Conventions (the decisions this sub-batch locks in)

1. **⛔ Color RETURN = the `Color` struct BY VALUE.** raylib's 12 Color-returning functions come
   back as `Color` (already defined in `Utiliy.vb`, 4 bytes `r,g,b,a`). Color **params stay
   decomposed** to `unsigned char r,g,b,a` (house rule, 2400+ bindings) — a deliberate in-bytes /
   out-struct asymmetry. This is new territory (no existing export returns a Color); the Color
   struct is 4 bytes and blittable, so it returns by value across the C ABI like raylib's own.
   **Small-struct-return marshaling is the key risk — see §5/§6.**
2. **New structs `Vector3` / `Vector4`** added to `Utiliy.vb` (`<StructLayout(Sequential)>`, 3 and 4
   `Single`s) for `ColorNormalize`→`Vector4`, `ColorFromNormalized`←`Vector4`, `ColorToHSV`→`Vector3`.
   Passed/returned by value (like the existing `Vector2`). Needed later by the core/models batches too.
3. **`void*` pixel pointers** (`GetPixelColor` srcPtr, `SetPixelColor` dstPtr) → VB `IntPtr`. The
   caller owns the buffer; the engine only reads/writes through it.
4. **Bool return** (`ColorIsEqual`) → `As <MarshalAs(UnmanagedType.I1)> Boolean` (C++ bool = 1 byte).
5. **Naming**: faithful raylib names with the `Framework_` prefix; no `EntryPoint:=` remap.

---

## 3. The 17 functions

`u8` = `unsigned char`. Color params → `u8 r,g,b,a`; Color returns → `Color` by value.

| # | raylib 5.5 | engine export | wrapper |
|--|--|--|--|
| 1 | `bool ColorIsEqual(Color, Color)` | `bool Framework_ColorIsEqual(u8 r,g,b,a, u8 r2,g2,b2,a2)` | `As <MarshalAs(I1)> Boolean` |
| 2 | `Color Fade(Color, float alpha)` | `Color Framework_Fade(u8 r,g,b,a, float alpha)` | `As Color` |
| 3 | `int ColorToInt(Color)` | `int Framework_ColorToInt(u8 r,g,b,a)` | `As Integer` |
| 4 | `Vector4 ColorNormalize(Color)` | `Vector4 Framework_ColorNormalize(u8 r,g,b,a)` | `As Vector4` |
| 5 | `Color ColorFromNormalized(Vector4)` | `Color Framework_ColorFromNormalized(Vector4 normalized)` | `As Color` |
| 6 | `Vector3 ColorToHSV(Color)` | `Vector3 Framework_ColorToHSV(u8 r,g,b,a)` | `As Vector3` |
| 7 | `Color ColorFromHSV(float h, float s, float v)` | `Color Framework_ColorFromHSV(float h, float s, float v)` | `As Color` |
| 8 | `Color ColorTint(Color, Color tint)` | `Color Framework_ColorTint(u8 r,g,b,a, u8 tr,tg,tb,ta)` | `As Color` |
| 9 | `Color ColorBrightness(Color, float factor)` | `Color Framework_ColorBrightness(u8 r,g,b,a, float factor)` | `As Color` |
| 10 | `Color ColorContrast(Color, float contrast)` | `Color Framework_ColorContrast(u8 r,g,b,a, float contrast)` | `As Color` |
| 11 | `Color ColorAlpha(Color, float alpha)` | `Color Framework_ColorAlpha(u8 r,g,b,a, float alpha)` | `As Color` |
| 12 | `Color ColorAlphaBlend(Color dst, Color src, Color tint)` | `Color Framework_ColorAlphaBlend(u8 dr,dg,db,da, u8 sr,sg,sb,sa, u8 tr,tg,tb,ta)` | `As Color` |
| 13 | `Color ColorLerp(Color, Color, float factor)` | `Color Framework_ColorLerp(u8 r,g,b,a, u8 r2,g2,b2,a2, float factor)` | `As Color` |
| 14 | `Color GetColor(unsigned int hexValue)` | `Color Framework_GetColor(unsigned int hexValue)` | `(hexValue As UInteger) As Color` |
| 15 | `Color GetPixelColor(void* srcPtr, int format)` | `Color Framework_GetPixelColor(void* srcPtr, int format)` | `(srcPtr As IntPtr, format As Integer) As Color` |
| 16 | `void SetPixelColor(void* dstPtr, Color, int format)` | `void Framework_SetPixelColor(void* dstPtr, u8 r,g,b,a, int format)` | `(dstPtr As IntPtr, r,g,b,a As Byte, format As Integer)` |
| 17 | `int GetPixelDataSize(int w, int h, int format)` | `int Framework_GetPixelDataSize(int width, int height, int format)` | `As Integer` |

Forwarders reassemble `Color{r,g,b,a}` for params and `return <raylibFn>(...)` for Color/Vector returns.

---

## 4. Structs to add (`Utiliy.vb`)
```vbnet
<StructLayout(LayoutKind.Sequential)>
Public Structure Vector3
    Public x As Single
    Public y As Single
    Public z As Single
End Structure

<StructLayout(LayoutKind.Sequential)>
Public Structure Vector4
    Public x As Single
    Public y As Single
    Public z As Single
    Public w As Single
End Structure
```
`Color` (4 bytes, already defined) and `Vector2` (already returned by value) are reused as-is.

---

## 5. Verification (100% GL-free — no GUI smoke needed)

Every function is pure CPU math, so the automated correctness suite is complete coverage (a first —
shapes needed a GUI smoke for drawing, text for font rendering; 3a needs none).

1. **Parity guard** (NUnit text-scan, no engine): 17 `Framework_<name>` exports ↔ 17 `<DllImport>`.
2. **Correctness** (NUnit `[Category("Integration")]`, self-contained local `[DllImport]`, self-skip on
   DllNotFound/EntryPointNotFound; the Batch-1/2 pattern). **The very first assertion proves the
   Color-struct-return marshaling.** Deterministic known values:
   - `ColorToInt(255,0,0,255)` == `0xFF0000FF`; `GetColor(0xFF0000FF)` → `{255,0,0,255}` (round-trip).
   - `ColorNormalize(255,128,0,255)` → `{1.0, ~0.502, 0.0, 1.0}`; `ColorFromNormalized({1,0,0,1})` → `{255,0,0,255}`.
   - `ColorToHSV(255,0,0,255)` → `{h≈0, s≈1, v≈1}`; `ColorFromHSV(0,1,1)` → `{255,0,0,255}` (round-trip).
     ⚠ raylib returns hue in **degrees [0..360]**, not normalized — red's h=0 is scale-safe, but any
     future non-red assertion (e.g. green → h≈120.0) must be written in degrees.
   - `Fade(255,255,255,255, 0.5f)` → alpha ≈127; `ColorAlpha(255,0,0,255, 0.5f)` → alpha ≈127.
   - `ColorTint(255,255,255,255, 128,128,128,255)` → `r≈128`; `ColorLerp(0,0,0,255, 255,255,255,255, 0.5f)` → `{~127,~127,~127,255}`.
   - `ColorBrightness(100,100,100,255, 0f)` → unchanged; `ColorAlphaBlend(...)` → runs + plausible.
   - `ColorContrast(...)` → runs (exact value from raylib formula if easily derived, else plausibility).
   - `ColorIsEqual((255,0,0,255),(255,0,0,255))` == true; vs `(…,254)` == false.
   - `GetPixelDataSize(4,4, 7)` == 64 (format 7 = UNCOMPRESSED_R8G8B8A8, 4 bytes/px).
   - `SetPixelColor(buf, 10,20,30,40, 7)` then `GetPixelColor(buf, 7)` → `{10,20,30,40}` — proves the
     `void*`/`IntPtr` pixel path (allocate a 4-byte buffer via `Marshal.AllocHGlobal`, free after).
3. **IDE refresh** ships the rebuilt engine `.dll`+`.lib` + wrapper `.dll` (per the
   [[raylib-parity-shapes-batch1]] playbook: build vcxproj with `-p:SolutionDir`, restore
   `-p:RestorePackagesConfig=true`; TestVbDLL only via VS MSBuild `-p:Platform=x64`).

---

## 6. Risks
- **Small-struct (4-byte `Color`) return ABI** — the one novel marshaling, and low risk: BOTH x64
  return paths are already proven in-repo — the register path by `Framework_GetMousePosition() As
  Vector2` (8 bytes) and the sret path by `Framework_GetCollisionRec(...) As Rectangle` (16 bytes,
  framework.h:385). A 4-byte `Color` is a sub-8-byte register return — the simplest case, bracketed
  by two working precedents. `Vector3`/`Vector4` (12/16 bytes) use the same sret path as `Rectangle`.
  The §5.2 first assertion (`GetColor` fields correct) is the go/no-go proof; if it somehow fails,
  fall back to a packed `unsigned int` return (the alternative the user weighed) before 3b.
- **`Color` struct has no explicit `<StructLayout>`** in Utiliy.vb (relies on the default sequential
  for a 4-byte all-`Byte` struct). Fine for return-by-value, but the correctness test's field checks
  confirm the layout end to end.
- **`ColorContrast`/`ColorBrightness`/`ColorAlphaBlend` exact outputs** follow raylib's formulas; where
  a clean known value isn't obvious, assert plausibility rather than couple the test to raylib internals.
