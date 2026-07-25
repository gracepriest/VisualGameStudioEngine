# raylib 5.5 Parity — Textures Batch 3c-i (Image mutators) — Design

**Status:** design
**Date:** 2026-07-25
**Scope:** the THIRD sub-batch of the textures module (master spec
`2026-07-24-raylib-parity-engine-wrapper-design.md` §5). raylib's rtextures module was split
`3a Color/pixel (17, shipped)` → `3b Image-in-RAM (22, shipped)` → **`3c Image mutate + software draw`**
→ `3d Texture GPU (4)`. 3c is itself split (user decision) into **3c-i Image mutators (22, this spec)**
and **3c-ii software drawing (20, next)**. 3c-i is done first because it is the lower-risk half —
scalar / Color-quad args, no vertex arrays, and it settles the **ByRef-`Image` mutation contract**
that 3c-ii inherits. Shared architecture + conventions inherited from the master spec and shipped
[[raylib-parity-textures-batch3a]] / [[raylib-parity-textures-batch3b]].

---

## 1. Coverage (raylib rtextures "Image manipulation functions" group — the in-place mutators)

The `// Image manipulation functions` group in `raylib.h` (5.5.0, lines 1348–1383) is 36 functions.
This batch binds **the 22 that are `void ImageXxx(Image *image, …)` in-place mutators and are still
absent**. Reconciliation of the full group so nothing is lost:

| Bucket | Count | Disposition |
|---|---|---|
| **In-place mutators — this batch** | **22** | bind now (§3) |
| Already faithful (pre-existing `void(Image*[, ints])` passthroughs) | 3 | **skip** — `ImageResize`, `ImageFlipVertical`, `ImageColorInvert` (framework.h:419–421) |
| By-value `Image` producers | 3 | **shipped in 3b** — `ImageCopy`, `ImageFromImage`, `ImageFromChannel` |
| Queries | 2 | **shipped in 3b** — `GetImageAlphaBorder` (→`Rectangle`), `GetImageColor` (→`Color`) |
| Caller-buffer color loaders | 2 | **shipped in 3b** — `LoadImageColors`, `LoadImagePalette` |
| Unload companions | 2 | **intentionally dropped in 3b** — `UnloadImageColors`, `UnloadImagePalette` (caller-buffer obviates them) |
| Font/window-dependent | 2 | **defer** — `ImageText`, `ImageTextEx` (need `InitWindow`/default font → the GUI/font batch) |

22 + 3 + 3 + 2 + 2 + 2 + 2 = 36. ✔ Every function accounted for. **Zero name collisions** — all 22
`Framework_Image*` symbols are greenfield (verified against framework.h). The 20 `// Image drawing
functions` are 3c-ii, not this spec. `ImageDrawText`/`ImageDrawTextEx` (also font-dependent) defer
with the other two text fns.

Counts after this batch: exports **2581 → 2603 (+22)**, imports **2513 → 2535 (+22)**.

---

## 2. Conventions (the decisions this sub-batch locks in)

### 2.1 ⛔ The ByRef-`Image` mutation contract (the central decision)
Every one of the 22 raylib functions takes `Image *image` and may **reallocate `image.data`** (resize,
crop, format, mipmaps, dither all `RL_MALLOC` a new buffer, `RL_FREE` the old, and overwrite the
`data`/`width`/`height`/`mipmaps`/`format` fields in place). To make those write-backs visible to the
managed caller:

- **Engine (`framework.h`/`framework.cpp`):** `void Framework_ImageXxx(Image* img, …)`, return `void`,
  a one-line forwarder that passes the pointer straight to raylib's identically-named function. **No
  dereference-and-reassign** — raylib mutates through the pointer.
- **Wrapper (`RaylibWrapper.vb`):** `ByRef img As Image`.

**Why it works:** the VB `Image` struct (`Utiliy.vb:88–95`) is `<StructLayout(Sequential)>` with
`data As IntPtr` + four `Integer`s — **fully blittable**. Under P/Invoke, `ByRef` on a blittable struct
pins the caller's actual struct and passes its address (no marshaled temp copy). The engine's `Image*`
therefore points at the caller's real `Image` memory; raylib's realloc + field rewrites land there. On
return the managed `Image` already holds the new pointer + dims + format, the old buffer is freed by
raylib (no leak), and ownership stays single — one eventual `Framework_UnloadImage(img)`.

**This is not new territory.** The 3 already-faithful mutators (`Framework_ImageResize`,
`Framework_ImageFlipVertical`, `Framework_ImageColorInvert`) already implement exactly this contract
(`Image*` engine-side, `ByRef img As Image` wrapper-side, verified). **We mirror them; we do NOT
migrate or touch them.** Additional in-repo precedent for ByRef-struct marshaling: `ByRef Texture2D`
(`Framework_GenTextureMipmaps`), `ByRef ... As Vector2` out-param (`Framework_CheckCollisionLines`). No
`<Out>`/`<In>` attributes anywhere in the codebase — bare `ByRef` is the house style for blittable
in-out structs; follow it.

**⛔ The trap (documented so no implementer reintroduces it):** a by-value
`void Framework_ImageXxx(Image image)` forwarding `ImageXxx(&image)` would realloc/rewrite the *local
copy* — the mutation is discarded on return **and** raylib's `RL_FREE` of the copy's old `data` frees a
buffer the caller still references → dangling / double-free on the next `Unload`. By-value is correct
only for pure producers/queries (3a/3b), never for an `Image*` mutator.

### 2.2 Color params → decomposed `unsigned char r,g,b,a` (house rule)
Every `Color` argument lowers to four `unsigned char` (engine) / `As Byte` (wrapper), and the forwarder
reassembles `Color{r,g,b,a}` before calling raylib. Multi-Color signatures use **descriptive prefixes**
(never a bare `or`/`and`/`xor` token, which is reserved in C++):
- `ImageColorReplace` → `colorR,colorG,colorB,colorA, replaceR,replaceG,replaceB,replaceA`.
- `ImageToPOT` / `ImageResizeCanvas` fill → `fillR,fillG,fillB,fillA`.
- `ImageAlphaClear` / `ImageColorTint` single color → `r,g,b,a`.

### 2.3 Other arg types
- **`Rectangle` by value** (`ImageCrop`) — reuse the existing `Rectangle` struct (blittable, already
  passed by value across the ABI; precedent throughout shapes).
- **⚠ `Image` by value as a *secondary* read-only arg** (`ImageAlphaMask(Image* image, Image alphaMask)`):
  `img` is `ByRef`, `alphaMask` is a **by-value `Image`** (never mutated). This is 3c-i's one
  mixed-passing signature — the same shape as 3c-ii's `ImageDraw`, but it lands here. Forwarder:
  `void Framework_ImageAlphaMask(Image* img, Image alphaMask) { ImageAlphaMask(img, alphaMask); }`.
- **`const float* kernel, int kernelSize`** (`ImageKernelConvolution`) → wrapper `kernel As Single()`
  (bare array, **no `<MarshalAs>`** — blittable arrays pin in place, per the shapes `Vector2()`
  precedent) + `kernelSize As Integer`. **Faithful passthrough:** `kernelSize` is raylib's total element
  count (raylib derives the side length internally); the wrapper does **not** validate a perfect square
  — raylib owns that contract.
- **Scalars** (`int`, `float`) map to `As Integer` / `As Single`.
- No `Color`/`Image`/`Rectangle` is returned by any of the 22 (all `void`), so no by-value-return work.

### 2.4 Placement & mechanics
- `framework.h`: 22 `__declspec(dllexport)` decls in one new banner block, adjacent to the existing
  image exports, mirroring 3a/3b placement (inside the single `extern "C"` region).
- `framework.cpp`: 22 forwarders in the **same order**, next to the existing image forwarders.
- `RaylibWrapper.vb`: one `#Region "Raylib Image mutators (Batch 3c-i)"`, name-for-name,
  `CallingConvention.Cdecl`, `ENGINE_DLL`, no `EntryPoint:=` remap. `CharSet` is irrelevant (no strings).
- No new structs (`Image`, `Color`-as-bytes, `Rectangle` all exist).

---

## 3. The 22 functions

`u8` = `unsigned char`. Engine = `Framework_<name>`; wrapper = `<DllImport>` of the same name. Every
`Image*` binds `ByRef img As Image`.

| # | raylib 5.5 | engine export | wrapper param tail |
|--|--|--|--|
| 1 | `void ImageFormat(Image*, int newFormat)` | `Framework_ImageFormat(Image* img, int newFormat)` | `newFormat As Integer` |
| 2 | `void ImageToPOT(Image*, Color fill)` | `Framework_ImageToPOT(Image* img, u8 fillR,fillG,fillB,fillA)` | `fillR..fillA As Byte` |
| 3 | `void ImageCrop(Image*, Rectangle crop)` | `Framework_ImageCrop(Image* img, Rectangle crop)` | `crop As Rectangle` |
| 4 | `void ImageAlphaCrop(Image*, float threshold)` | `Framework_ImageAlphaCrop(Image* img, float threshold)` | `threshold As Single` |
| 5 | `void ImageAlphaClear(Image*, Color, float threshold)` | `Framework_ImageAlphaClear(Image* img, u8 r,g,b,a, float threshold)` | `r..a As Byte, threshold As Single` |
| 6 | `void ImageAlphaMask(Image*, Image alphaMask)` | `Framework_ImageAlphaMask(Image* img, Image alphaMask)` | `alphaMask As Image` (by value) |
| 7 | `void ImageAlphaPremultiply(Image*)` | `Framework_ImageAlphaPremultiply(Image* img)` | — |
| 8 | `void ImageBlurGaussian(Image*, int blurSize)` | `Framework_ImageBlurGaussian(Image* img, int blurSize)` | `blurSize As Integer` |
| 9 | `void ImageKernelConvolution(Image*, const float* kernel, int kernelSize)` | `Framework_ImageKernelConvolution(Image* img, const float* kernel, int kernelSize)` | `kernel As Single(), kernelSize As Integer` |
| 10 | `void ImageResizeNN(Image*, int newWidth, int newHeight)` | `Framework_ImageResizeNN(Image* img, int newWidth, int newHeight)` | `newWidth, newHeight As Integer` |
| 11 | `void ImageResizeCanvas(Image*, int newWidth, int newHeight, int offsetX, int offsetY, Color fill)` | `Framework_ImageResizeCanvas(Image* img, int newWidth,newHeight,offsetX,offsetY, u8 fillR,fillG,fillB,fillA)` | `newWidth..offsetY As Integer, fillR..fillA As Byte` |
| 12 | `void ImageMipmaps(Image*)` | `Framework_ImageMipmaps(Image* img)` | — |
| 13 | `void ImageDither(Image*, int rBpp, int gBpp, int bBpp, int aBpp)` | `Framework_ImageDither(Image* img, int rBpp,gBpp,bBpp,aBpp)` | `rBpp..aBpp As Integer` |
| 14 | `void ImageFlipHorizontal(Image*)` | `Framework_ImageFlipHorizontal(Image* img)` | — |
| 15 | `void ImageRotate(Image*, int degrees)` | `Framework_ImageRotate(Image* img, int degrees)` | `degrees As Integer` |
| 16 | `void ImageRotateCW(Image*)` | `Framework_ImageRotateCW(Image* img)` | — |
| 17 | `void ImageRotateCCW(Image*)` | `Framework_ImageRotateCCW(Image* img)` | — |
| 18 | `void ImageColorTint(Image*, Color)` | `Framework_ImageColorTint(Image* img, u8 r,g,b,a)` | `r..a As Byte` |
| 19 | `void ImageColorGrayscale(Image*)` | `Framework_ImageColorGrayscale(Image* img)` | — |
| 20 | `void ImageColorContrast(Image*, float contrast)` | `Framework_ImageColorContrast(Image* img, float contrast)` | `contrast As Single` |
| 21 | `void ImageColorBrightness(Image*, int brightness)` | `Framework_ImageColorBrightness(Image* img, int brightness)` | `brightness As Integer` |
| 22 | `void ImageColorReplace(Image*, Color color, Color replace)` | `Framework_ImageColorReplace(Image* img, u8 colorR,colorG,colorB,colorA, u8 replaceR,replaceG,replaceB,replaceA)` | `colorR..colorA, replaceR..replaceA As Byte` |

Representative forwarders:
```cpp
void Framework_ImageColorTint(Image* img, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    Color c = { r, g, b, a };
    ImageColorTint(img, c);
}
void Framework_ImageAlphaMask(Image* img, Image alphaMask) { ImageAlphaMask(img, alphaMask); }
void Framework_ImageKernelConvolution(Image* img, const float* kernel, int kernelSize) {
    ImageKernelConvolution(img, kernel, kernelSize);
}
```

---

## 4. Structs

**None new.** `Image` (Utiliy.vb:88), `Rectangle` (:55), and the byte-decomposed `Color` convention all
exist. `Image` is passed `ByRef` (mutators) or by value (`ImageAlphaMask` secondary arg) — both already
supported by the blittable struct.

---

## 5. Verification (100% GL-free — no GUI smoke)

Every one of the 22 is pure CPU; the headless correctness suite is complete coverage.

### 5.1 Parity guard
NUnit text-scan (no engine load): 22 `Framework_<name>(` exports ↔ 22 `<DllImport>` imports, trailing
`(` anchors near-name pairs (`Framework_ImageColorContrast(` vs `Framework_ImageColorBrightness(`,
`Framework_ImageResize(` vs `Framework_ImageResizeNN(`/`Framework_ImageResizeCanvas(`). New file
`VisualGameStudio.Tests/Native/RaylibImageMutatorParityTests.cs`, `Has.Length.EqualTo(22)`.

### 5.2 Correctness (NUnit `[Category("Integration")]`, self-contained local `[DllImport]`, self-skip on
DllNotFound/EntryPointNotFound)
Local struct mirrors `RImage{IntPtr data;int width,height,mipmaps,format}` / `RColor{byte r,g,b,a}` /
`RRect{float x,y,width,height}`. Each test gens a small `Image` (via `Framework_GenImageColor` /
`Framework_GenImageChecked` from 3b), runs the mutator **ByRef**, then reads back struct fields and/or
`Framework_LoadImageColors` (3b) and asserts. Deterministic checks:
- **ByRef propagation is the first proof:** `ImageResizeNN(3×2 → 6×4)` → returned `img.width==6 && img.height==4`, and `LoadImageColors` returns 24. `ImageCrop({0,0,2,2})` on a 4×4 → `width==2,height==2`.
- `ImageColorGrayscale` on a red image → every pixel `R==G==B`.
- `ImageColorReplace(red → blue)` on a red image → all pixels blue; a non-matching color untouched.
- `ImageColorTint(white × {128,128,128,255})` → `r≈128`. `ImageColorBrightness(gray, +50)` → brighter.
- `ImageColorInvert`-style not tested here (it's a skip); `ImageColorContrast(0f)` → unchanged.
- `ImageFlipHorizontal` on a left-black/right-white 2×1 → pixels swapped. `ImageRotateCW` on a 2×1 → `width==1,height==2`.
- `ImageFormat(img, UNCOMPRESSED_GRAYSCALE=1)` → `img.format==1`; `LoadImageColors` still normalizes to RGBA for the pixel compare.
- `ImageMipmaps` on a 4×4 → `img.mipmaps > 1`.
- `ImageToPOT(fill)` on a 3×3 → `width`/`height` are powers of two ≥ 3 (4×4); the pad region == fill.
- `ImageAlphaClear`/`ImageAlphaCrop`/`ImageAlphaPremultiply` → run + plausible (alpha/dim changes).
- `ImageAlphaMask(img, mask)` → the by-value secondary `Image` path runs; masked pixels' alpha follows the mask.
- `ImageKernelConvolution(img, identityKernel[9], 9)` → image ≈ unchanged (identity 3×3 kernel), proving the `Single()` array marshals.
- `ImageBlurGaussian`, `ImageDither`, `ImageResizeCanvas`, `ImageRotate(90)`, `ImageRotateCCW` → run + plausible dimension/format state.

⚠ **Staging trap (standing lesson, 3b):** the test copies `VisualGameStudioEngine.dll` from `IDE\`,
stale until the refresh task. A stale DLL → `EntryPointNotFound` → `Assert.Ignore` → tests **SKIP while
looking green**. Stage the freshly built `x64\Release\VisualGameStudioEngine.dll`+`.lib` into `IDE\`
first; **confirm `Passed: N, Skipped: 0`** (engine INFO log lines prove real execution).

### 5.3 IDE refresh
Ship the rebuilt engine `.dll`+`.lib` + `RaylibWrapper.dll` into `IDE\` (shapes-batch1 playbook: build
the vcxproj with `-p:SolutionDir`, restore `-p:RestorePackagesConfig=true`; MSBuild via VS 2022
Enterprise). The engine DLL affects all game apps.

---

## 6. Risks
- **ByRef vs by-value is load-bearing** (§2.1) — the single correctness-critical decision. Mitigated by
  mirroring 3 proven mutators and by §5.2's ByRef-propagation assertions (dims read back after resize/crop).
- **`ImageAlphaMask` mixed passing** (§2.3) — `Image*` + by-value `Image`; the forwarder must pass the
  mask by value (never `&`). Covered by a dedicated test.
- **Multi-Color arg order / naming** — `ImageColorReplace` (2 colors) and `ImageResizeCanvas`
  (ints-then-fill) are the transpose-prone signatures; descriptive names + §3's exact table mitigate.
- **`ImageKernelConvolution` `kernelSize` semantics** — faithful passthrough (total element count); no
  square-validation. Documented so the reviewer doesn't flag a "missing guard."
- **`ImageDither`/`ImageFormat` format churn** — reduce/realloc the buffer; assertions read
  `LoadImageColors` (normalized to RGBA) rather than raw bytes to stay format-agnostic.
- **Deferred-fn leakage** — `ImageText`/`ImageTextEx` (and 3c-ii's `ImageDrawText`/`ImageDrawTextEx`)
  must NOT be bound here; the parity guard's fixed 22-name list is the backstop.
