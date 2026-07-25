# raylib 5.5 Parity — Textures Batch 3d (Texture GPU round-trips + font-image fns) — Design

**Status:** design
**Date:** 2026-07-25
**Scope:** the FINAL textures sub-batch. Closes out rtextures after
`3a Color/pixel (17)` → `3b Image-in-RAM (22)` → `3c-i mutators (22)` → `3c-ii software draw (20)`.
3d binds **9 functions**: the 4 Texture GPU round-trips + the 4 font→image fns deferred from
3b/3c + the 1 orphaned `GenImageText`. It is the **first window-dependent batch** — the 8
GPU/font functions need a live GL context, so their verification is a **`TestVbDLL --textures3d`
smoke scene**, not the headless NUnit suite. `GenImageText` is the lone headless straggler
(grayscale-from-text-data, no font/GL) and keeps a normal NUnit correctness test. Conventions
inherited from the master spec and shipped [[raylib-parity-textures-batch3c-ii]] /
[[raylib-parity-textures-batch3b]] / [[raylib-parity-textures-batch3a]].

---

## 1. Coverage (9 functions across three raylib groups — all ABSENT, zero collisions)

| Group (raylib.h) | Fn | Why it's here now |
|---|---|---|
| Texture loading (1413–1414) | `LoadTextureFromImage`, `LoadTextureCubemap` | need GPU access — deferred until the smoke harness |
| Image loading (1328–1329) | `LoadImageFromTexture`, `LoadImageFromScreen` | GPU→CPU readback / framebuffer capture — need a context |
| Image manipulation (1351–1352) | `ImageText`, `ImageTextEx` | font-dependent (default/custom font ← `InitWindow`) — deferred in 3b |
| Image drawing (1407–1408) | `ImageDrawText`, `ImageDrawTextEx` | font-dependent **and** `Image*` in-out (the ByRef-Image primitive shipped in 3c) — deferred in 3c |
| Image generation (1345) | `GenImageText` | **headless** (grayscale from text *data*, no font/GL); mis-deferred in 3b — folded in to close the orphan |

**Verified absent:** no `Framework_LoadTextureFromImage`/`…FromTexture`/`…FromScreen`/`LoadTextureCubemap`/
`ImageText*`/`ImageDrawText*`/`GenImageText` exist in framework.h. No collision — the existing texture
loaders are `Framework_LoadTexture(const char*)` (path-keyed) and `Framework_AcquireTextureH` (handle
cache); neither expresses Image↔GPU round-trips, so 3d uses the by-value system (§2.1).

**Not in scope (correctly excluded):** `UpdateTexture`/`UpdateTextureRec` (take `const void* pixels`, a
raw-buffer CPU→GPU concern → a later texture-update batch); `LoadRenderTexture` (creates a render target,
no `Image`); `ExportImageToMemory` (unknown-size `unsigned char*` → the file-I/O batch).

Counts after this batch: exports **2623 → 2632 (+9)**, imports **2555 → 2564 (+9)**.

---

## 2. Conventions

### 2.1 ⛔ By-value `Texture2D`/`TextureCubemap`/`Image` — NOT the handle cache
The engine has an `int`/`IntPtr` texture-handle asset cache (`Framework_AcquireTextureH(const char* path)`),
but it is **path-keyed** — it can only load from a file and cannot build a texture from an in-RAM `Image`
or read a GPU texture back to CPU. These 4 GPU operations are exactly what the handle façade can't express,
so they use the **primary by-value struct system**, matching the proven precedent
`Framework_LoadTexture(const char*) As Texture2D` (framework.h:390) and `Framework_GetShapesTexture() As
Texture2D`. Struct-by-value returns across the C ABI are already shipped: `Color` (3a), `Image` (3b),
`Rectangle`/`Vector2` (shapes). `Texture2D` (20 B) and `Image` (24 B) exceed 16 B → returned via the hidden
sret pointer; the engine and the VB marshaler agree on `Sequential` layout, so the returned struct's fields
(`width`/`height`/`id`) are the round-trip assertion in the smoke.

### 2.2 `Font` by value (ImageTextEx / ImageDrawTextEx)
`Font` (Utiliy.vb:124–131) is passed **by value** — it embeds an inline `Texture2D` (the glyph atlas) plus
two opaque `IntPtr`s (`recs`/`glyphs`). The codebase already passes `Font` by value (text Batch 2's
`Framework_GetGlyphInfo`/measure fns). The atlas must be live (font loaded via `LoadFont*` after
`InitWindow`) for the call; a zeroed atlas yields empty glyphs. `ImageText`/`ImageDrawText` use the **default
font** (populated by `InitWindow`) → no `Font` arg.

### 2.3 Other marshaling
- **`Color` params → `unsigned char r,g,b,a`** (house rule); forwarder rebuilds `Color{r,g,b,a}`. Applies to
  ImageText/ImageTextEx/ImageDrawText/ImageDrawTextEx.
- **String inputs → `As String` + `CharSet:=CharSet.Ansi`** (LPStr; the codebase's ~256-site convention for
  string INPUTS — distinct from string RETURNS which use IntPtr+PtrToStringAnsi; no fn here returns a string).
- **`ImageDrawText`/`ImageDrawTextEx` take `Image* dst` → `ByRef dst As Image`** (the shipped 3c mutation
  contract). `ImageDrawTextEx` also takes `Vector2 position` by value.
- **`LoadTextureCubemap` `int layout`** → `layout As Integer` (faithful passthrough; the `CubemapLayout` enum
  has 5 values `AUTO_DETECT=0 … CROSS_FOUR_BY_THREE=4`, `PANORAMA` removed in 5.5 — we do NOT add a VB enum,
  YAGNI for a 2D engine).
- **Image returns are heap-owned** → freed by the existing `Framework_UnloadImage`. **Texture returns** →
  freed by the existing `Framework_UnloadTexture`. No new Unload exports.
- **`GenImageText` is fully headless** — `Image GenImageText(int width, int height, const char* text)` copies
  the text bytes into a `PIXELFORMAT_UNCOMPRESSED_GRAYSCALE` buffer; no font, no GL.

### 2.4 Placement & mechanics
- `framework.h`/`framework.cpp`: 9 exports/forwarders in a new `Batch 3d` banner after the 3c-ii block
  (inside `extern "C"`). Forwarders reassemble `Color` for the font fns; GPU fns are 1-line passthroughs.
- `RaylibWrapper.vb`: one `#Region "Raylib Texture GPU + font-image (Batch 3d)"`; `Function` for the 6
  Image/Texture returns, `Sub` for the 2 `ImageDrawText*`.
- No new structs (`Texture2D`, `TextureCubemap`, `Font`, `Image`, `Vector2` all exist).

---

## 3. The 9 functions

`u8` = `unsigned char`. Struct returns are by value; `Image* dst` → `ByRef dst As Image`; strings
`CharSet.Ansi`; Color → `u8 r,g,b,a`.

| # | raylib 5.5 | engine export | wrapper |
|--|--|--|--|
| 1 | `Texture2D LoadTextureFromImage(Image)` | `Texture2D Framework_LoadTextureFromImage(Image image)` | `Function(image As Image) As Texture2D` |
| 2 | `TextureCubemap LoadTextureCubemap(Image, int layout)` | `TextureCubemap Framework_LoadTextureCubemap(Image image, int layout)` | `Function(image As Image, layout As Integer) As TextureCubemap` |
| 3 | `Image LoadImageFromTexture(Texture2D)` | `Image Framework_LoadImageFromTexture(Texture2D texture)` | `Function(texture As Texture2D) As Image` |
| 4 | `Image LoadImageFromScreen(void)` | `Image Framework_LoadImageFromScreen(void)` | `Function() As Image` |
| 5 | `Image ImageText(const char* text, int fontSize, Color)` | `Image Framework_ImageText(const char* text, int fontSize, u8 r,g,b,a)` | `Function(text As String, fontSize As Integer, r,g,b,a As Byte) As Image` (Ansi) |
| 6 | `Image ImageTextEx(Font, const char* text, float fontSize, float spacing, Color tint)` | `Image Framework_ImageTextEx(Font font, const char* text, float fontSize, float spacing, u8 r,g,b,a)` | `Function(font As Font, text As String, fontSize As Single, spacing As Single, r,g,b,a As Byte) As Image` (Ansi) |
| 7 | `void ImageDrawText(Image* dst, const char* text, int posX, int posY, int fontSize, Color)` | `void Framework_ImageDrawText(Image* dst, const char* text, int posX, int posY, int fontSize, u8 r,g,b,a)` | `Sub(ByRef dst As Image, text As String, posX As Integer, posY As Integer, fontSize As Integer, r,g,b,a As Byte)` (Ansi) |
| 8 | `void ImageDrawTextEx(Image* dst, Font, const char* text, Vector2 position, float fontSize, float spacing, Color tint)` | `void Framework_ImageDrawTextEx(Image* dst, Font font, const char* text, Vector2 position, float fontSize, float spacing, u8 r,g,b,a)` | `Sub(ByRef dst As Image, font As Font, text As String, position As Vector2, fontSize As Single, spacing As Single, r,g,b,a As Byte)` (Ansi) |
| 9 | `Image GenImageText(int width, int height, const char* text)` | `Image Framework_GenImageText(int width, int height, const char* text)` | `Function(width As Integer, height As Integer, text As String) As Image` (Ansi) |

Representative forwarders:
```cpp
Texture2D Framework_LoadTextureFromImage(Image image) { return LoadTextureFromImage(image); }
Image Framework_LoadImageFromScreen(void) { return LoadImageFromScreen(); }
Image Framework_ImageText(const char* text, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    Color c = { r, g, b, a };
    return ImageText(text, fontSize, c);
}
void Framework_ImageDrawText(Image* dst, const char* text, int posX, int posY, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    Color c = { r, g, b, a };
    ImageDrawText(dst, text, posX, posY, fontSize, c);
}
Image Framework_GenImageText(int width, int height, const char* text) { return GenImageText(width, height, text); }
```

---

## 4. Structs

**None new.** `Texture2D` (Utiliy.vb:70), `TextureCubemap` (:78, byte-identical to Texture2D), `Font`
(:124, inline Texture2D + 2 IntPtr), `Image` (:88), `Vector2` (:45) all exist and are blittable.

---

## 5. Verification (three surfaces)

### 5.1 Parity guard (automated, headless)
NUnit text-scan: 9 `Framework_<name>(` exports ↔ 9 `<DllImport>` imports; trailing `(` anchors
`LoadImageFromTexture(` vs `LoadImageFromScreen(`, `ImageText(` vs `ImageTextEx(`, `ImageDrawText(` vs
`ImageDrawTextEx(`. New `VisualGameStudio.Tests/Native/RaylibTexture3dParityTests.cs`, `Has.Length.EqualTo(9)`.

### 5.2 GenImageText correctness (automated, headless — the one non-window fn)
NUnit `[Category("Integration")]`, local `[DllImport]`, `Guard` self-skip. `GenImageText(16, 16, "AB")` →
returned `Image.width==16 && height==16`, `data != IntPtr.Zero`, `format == PIXELFORMAT_UNCOMPRESSED_GRAYSCALE
(1)`; `LoadImageColors` (or `GetPixelDataSize(16,16,1)`) consistent; free with `Framework_UnloadImage`. Stage
the fresh DLL into `IDE\` first; **confirm Passed, Skipped 0** (staging trap).

### 5.3 `TestVbDLL --textures3d` smoke scene (the 8 window fns — the real verification)
**The headless NUnit suite CANNOT cover these** — the test host never calls `InitWindow`, so
`LoadTextureFromImage`/`LoadTextureCubemap` (GPU upload), the GPU→CPU readbacks (`rlReadTexturePixels`), and
`LoadImageFromScreen` (framebuffer) all fail/return-empty in-process. Do NOT add a headless correctness test
for them.

New file `TestVbDLL/SampleTextures3d.vb` (model on the existing `SampleTextBatch2.vb` / `SampleShapesBatch1.vb`
scene + game-loop pattern); a `--textures3d` dispatch guard in `TestVbDLL/Program.vb`. Flow (all GL work after
window init):
1. Init window (per the existing scenes' `Framework_Initialize`/draw-callback pattern).
2. Source `Image` via a 3b `GenImageColor`/`GenImageChecked` (asset-free).
3. `tex = Framework_LoadTextureFromImage(img)` → **assert `tex.id <> 0` and `tex.width/height == img.width/height`.**
4. Draw the texture each frame (`Framework_DrawTexture`/`DrawTexturePro`) so there is a rendered frame.
5. Round-trip: `img2 = Framework_LoadImageFromTexture(tex)` → **assert `img2.width == tex.width`, `img2.height == tex.height`, `img2.data <> IntPtr.Zero`.**
6. `ImageText("Hi", 20, RED)` → Image; `ImageTextEx(defaultFontOrLoaded, "Hi", 20, 1, WHITE)` → Image; make a
   blank Image and `ImageDrawText(ByRef img3, "Hi", …)` / `ImageDrawTextEx(ByRef img3, font, …)` → **assert each
   returned/mutated Image has non-null data + expected dims**, and (optional) upload one to a texture and draw
   it for the visual check.
7. `LoadTextureCubemap` — bind + call on a synthesized layout image; **assert it returns without crashing and
   log `cubemap.id`** (a 2D engine has no cubemap asset → bind-only; do not hard-fail on id==0).
8. After ≥1 drawn frame, `shot = Framework_LoadImageFromScreen()` → **assert non-null data + dims == screen**;
   `Framework_ExportImage(shot, "textures3d_capture.png")` (shipped in 3b) so the user can eyeball the readback.
9. Print a `PASS/FAIL` summary line for the mechanical asserts; auto-close after N frames (bounded loop) so the
   scene is runnable unattended. Unload every texture (`Framework_UnloadTexture`) and image
   (`Framework_UnloadImage`) before shutdown.

**Programmatically asserted:** texture `id<>0`, round-trip `width`/`height` equality, non-null image data,
capture dims, font-image non-null dims. **User's visual checkpoint:** the drawn texture + the text-images
render correctly and the exported `textures3d_capture.png` is a faithful readback (not black/garbage).

**Build:** `TestVbDLL` builds **only via VS 2022 MSBuild with `-p:Platform=x64`** (it ProjectReferences the C++
`.vcxproj`s → `dotnet build` fails MSB4278). **Rebuild the native engine first** — TestVbDLL copies
`x64\Release\VisualGameStudioEngine.dll`, so the new 3d exports must be built before the vbproj build. Exe lands
at `TestVbDLL\bin\x64\Release\net8.0\TestVbDLL.exe`; run `--textures3d`.

### 5.4 IDE refresh
Rebuild engine `.dll`+`.lib` + `RaylibWrapper.dll` into `IDE\` (shapes-batch1 playbook).

---

## 6. Risks
- **Struct-by-value returns** (`Texture2D`/`Image`) — proven (3a/3b), but a layout/packing mismatch surfaces as
  a corrupted `width`/`height`; §5.3 step-3/5 asserts + §5.2 are the guard.
- **`LoadImageFromScreen` needs a completed frame** — call it AFTER a `BeginDrawing/EndDrawing`, never pre-loop
  (empty/undefined framebuffer otherwise).
- **`ImageTextEx`/`ImageDrawTextEx` Font atlas lifetime** — the `Font` must be loaded (post-`InitWindow`) and
  stay live for the call; unload after. `ImageText`/`ImageDrawText` sidestep via the default font.
- **`LoadTextureCubemap`** — no cubemap asset in a 2D repo → bind-only smoke (validity-log, not hard-assert);
  the binding + parity guard still ship.
- **Windowed smoke is the user's checkpoint** — the mechanical PASS/FAIL covers marshaling; visual correctness
  (readback fidelity, glyph rendering) is human-verified. This is the established pattern (shapes `--shapes`,
  text `--text`).
- **TestVbDLL build constraint** — VS MSBuild `-p:Platform=x64` only; rebuild the engine before the vbproj.
- **Color/Font arg order in the 2 `Ex` fns** — `ImageTextEx(font, text, fontSize, spacing, tint)` and
  `ImageDrawTextEx(dst, font, text, position, fontSize, spacing, tint)` are the transpose-prone signatures;
  §3's exact table mitigates.
