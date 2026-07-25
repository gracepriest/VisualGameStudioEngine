# raylib 5.5 Parity — Textures Batch 3b (Image load / generate / query in RAM) — Design

**Status:** design
**Date:** 2026-07-24
**Scope:** the SECOND sub-batch of the textures module (master spec
`2026-07-24-raylib-parity-engine-wrapper-design.md` §5). raylib's rtextures module is **115
functions**; it is split **3a Color/pixel (17, SHIPPED @ `77b95d9`)** → **3b Image-in-RAM (this
spec)** → 3c Image mutate + software draw (44) → 3d Texture GPU (4). 3b covers the
functions that create, load, generate, and query an `Image` **in CPU memory** — no GL, no window —
so like 3a the automated correctness suite is complete coverage. Shared architecture + conventions
inherited from the master spec and shipped [[raylib-parity-shapes-batch1]] /
[[raylib-parity-text-batch2]] / [[raylib-parity-textures-batch3a]].

---

## 1. Coverage reconciliation (28 GL-free candidates → 22 shipped this batch)

The recon identified 28 absent GL-free Image functions in the load/generate/query group. After
grounding each against `raylib.h` (bundled `packages/raylib.5.5.0/build/native/include/raylib.h`),
**six move out** for principled reasons, leaving **22** to ship now. None are dropped from the
roadmap — the deferred ones relocate to a later batch that can verify them properly.

| Function | Disposition | Reason |
|---|---|---|
| `ImageText` (rl:1351) | **Defer → GUI-verified batch (with 3d)** | Renders text with `GetFontDefault()`; the default font is only populated by `InitWindow`. Headless it is unverifiable and may null-deref `font.recs`/`font.glyphs`. |
| `ImageTextEx` (rl:1352) | **Defer → GUI-verified batch (with 3d)** | Renders text with a caller `Font`, which is GPU-loaded (`LoadFont*` needs GL). Same window dependency. |
| `GenImageText` (rl:1345) | **Defer → GUI-verified batch (with 3d)** | "grayscale image from text data" — also text rendering, same font-state dependency. Grouped with the other two rather than split. |
| `ExportImageToMemory` (rl:1333) | **Defer → file-I/O batch** | Returns `unsigned char*` of an **unknown** encoded size; caller-buffer can't pre-size it and a static engine buffer is fragile for multi-MB encodes. Belongs with `LoadFileData`/`UnloadFileData`. |
| `UnloadImageColors` (rl:1380) | **Drop (obsoleted)** | The caller-buffer form of `LoadImageColors` (see §2) never hands a raylib pointer to VB, so there is nothing for VB to free. |
| `UnloadImagePalette` (rl:1381) | **Drop (obsoleted)** | Same — obsoleted by the caller-buffer form of `LoadImagePalette`. |

**Net:** 28 − 3 (text-render, → 3d) − 1 (`ExportImageToMemory`, → file-I/O) − 2 (Unloads, obsoleted)
= **22 functions this batch.** All 22 are pure CPU/RAM and headless-verifiable.

`LoadImageFromTexture` / `LoadImageFromScreen` (rl:1328–1329) were never in this group — they read
back GPU memory and belong to **3d**.

---

## 2. Conventions (the decisions this sub-batch locks in)

1. **`Image` passed and returned BY VALUE.** `Image` is already defined in `Utiliy.vb` (opaque
   `data As IntPtr` + `width, height, mipmaps, format As Integer` = 24 bytes on x64). It marshals by
   value across the C ABI exactly like `Font` (same shape: opaque pointer + ints), which the engine
   already returns by value (`Framework_LoadFontEx`). **Lifecycle:** every `Image` this batch hands
   back is freed by the **existing** `Framework_UnloadImage(Image)` — no new Unload export is needed.
2. **⛔ Caller-buffer for the two `Color*`-returning functions** (the key decision). Instead of
   returning raylib's heap pointer, the engine forwarder calls raylib, copies into a **caller-owned**
   `Color[]`, frees raylib's buffer internally, and returns the count — the same shape as the shipped
   `Framework_LoadCodepoints` (`framework.cpp:28431`), with one deliberate difference: `LoadImageColors`
   has **no capacity parameter** (the caller is contractually required to size `width*height`, which it
   can always compute from the `Image`) so it copies unconditionally, whereas `LoadImagePalette` clamps
   to `maxPaletteSize` exactly as `LoadCodepoints` clamps to `outCapacity`. This obviates `UnloadImageColors`/
   `UnloadImagePalette` and removes all leak risk:
   - `int Framework_LoadImageColors(Image image, Color* outColors)` — caller sizes `width*height`;
     returns `width*height` (0 if the image is invalid).
   - `int Framework_LoadImagePalette(Image image, int maxPaletteSize, Color* outColors)` — caller
     sizes `maxPaletteSize`; returns the actual distinct-color `count` (≤ `maxPaletteSize`).
   Both forwarders **guard against a NULL raylib return** (invalid image) and return 0 rather than
   deref. The VB side declares `outColors As Color()`; because `Color` is blittable (4 `Byte`s), the
   marshaler pins the managed array and passes its address, so the engine's in-place writes are
   visible **without** an `<Out>` attribute — identical to `outCodepoints As Integer()`.
3. **Color params → decomposed `unsigned char r,g,b,a`** (house rule, matches 3a and the 2400+
   existing bindings). Decomposed color bytes get **descriptive names** in multi-color signatures
   (`startR…`, `innerR…`, `col1R…`) — never the bare token `or`, which is a reserved C++ alternative
   operator.
4. **Color / `Rectangle` RETURNS → by value** (proven in 3a: `Color` register-return,
   `Rectangle` sret-return). Used by `GetImageColor` → `Color`, `GetImageAlphaBorder` → `Rectangle`.
5. **`const char*` in → `As String`, `CharSet.Ansi`.** **`const unsigned char*` file data in →
   `Byte()`** (blittable, pinned) + a separate `int dataSize`. **`int* frames` out → `ByRef … As
   Integer`.**
6. **`bool` return → `As <MarshalAs(UnmanagedType.I1)> Boolean`** (C++ `bool` = 1 byte):
   `IsImageValid`, `ExportImage`, `ExportImageAsCode`.
7. **Asset-path asymmetry.** File **readers** (`LoadImageRaw`, `LoadImageAnim`) resolve through the
   engine's `ResolveAssetPath` (consistent with the existing `Framework_LoadImage`). Note
   `ResolveAssetPath` does NOT prepend the asset root for absolute paths (its `p[0] != '\\' && p[1]
   != ':'` guard skips `C:\…` and `/…`), but it still passes every path through `NormalizePath`,
   which converts `\`→`/` **and lowercases** — so an absolute reader path is lowercased/slash-normalized,
   not literally unchanged. Harmless on the Windows target (NTFS is case-insensitive and accepts `/`),
   which keeps the §5.2 temp-file round-trips valid. File **writers** (`ExportImage`,
   `ExportImageAsCode`) pass the path through **as-is** (no resolve, exact case) — an export must land
   exactly where the caller names it, never silently redirected or case-folded.
8. **Naming:** faithful raylib names with the `Framework_` prefix; no `EntryPoint:=` remap. **No name
   collisions** — grep confirms none of the 22 new `Framework_<name>` symbols already exist in
   `framework.h`/`framework.cpp` or `RaylibWrapper.vb`. (Other exports merely *contain* "Image" —
   `Framework_LoadImage`, `Framework_LoadFontFromImage`, `Framework_UI_CreateImage`,
   `Framework_Atlas_AddImage`, etc. — but none share a full name with the 22.)

---

## 3. The 22 functions

`u8` = `unsigned char`. Color params → decomposed `u8`; `Image`/`Color`/`Rectangle` cross by value.

### Load / export to disk (7)
| # | raylib 5.5 | engine export | wrapper |
|--|--|--|--|
| 1 | `Image LoadImageRaw(const char*, int, int, int, int)` | `Image Framework_LoadImageRaw(const char* fileName, int width, int height, int format, int headerSize)` *(resolves path)* | `(fileName As String, width, height, format, headerSize As Integer) As Image`, `CharSet.Ansi` |
| 2 | `Image LoadImageAnim(const char*, int*)` | `Image Framework_LoadImageAnim(const char* fileName, int* frames)` *(resolves path)* | `(fileName As String, ByRef frames As Integer) As Image`, `CharSet.Ansi` |
| 3 | `Image LoadImageAnimFromMemory(const char*, const u8*, int, int*)` | `Image Framework_LoadImageAnimFromMemory(const char* fileType, const unsigned char* fileData, int dataSize, int* frames)` | `(fileType As String, fileData As Byte(), dataSize As Integer, ByRef frames As Integer) As Image`, `CharSet.Ansi` |
| 4 | `Image LoadImageFromMemory(const char*, const u8*, int)` | `Image Framework_LoadImageFromMemory(const char* fileType, const unsigned char* fileData, int dataSize)` | `(fileType As String, fileData As Byte(), dataSize As Integer) As Image`, `CharSet.Ansi` |
| 5 | `bool IsImageValid(Image)` | `bool Framework_IsImageValid(Image image)` | `(image As Image) As <MarshalAs(I1)> Boolean` |
| 6 | `bool ExportImage(Image, const char*)` | `bool Framework_ExportImage(Image image, const char* fileName)` *(path as-is)* | `(image As Image, fileName As String) As <MarshalAs(I1)> Boolean`, `CharSet.Ansi` |
| 7 | `bool ExportImageAsCode(Image, const char*)` | `bool Framework_ExportImageAsCode(Image image, const char* fileName)` *(path as-is)* | `(image As Image, fileName As String) As <MarshalAs(I1)> Boolean`, `CharSet.Ansi` |

### Generation (8)
| # | raylib 5.5 | engine export | wrapper |
|--|--|--|--|
| 8 | `Image GenImageColor(int, int, Color)` | `Image Framework_GenImageColor(int width, int height, u8 r,g,b,a)` | `(width, height As Integer, r,g,b,a As Byte) As Image` |
| 9 | `Image GenImageGradientLinear(int,int,int,Color,Color)` | `Image Framework_GenImageGradientLinear(int width, int height, int direction, u8 startR,startG,startB,startA, u8 endR,endG,endB,endA)` | `(width, height, direction As Integer, startR,…,startA As Byte, endR,…,endA As Byte) As Image` |
| 10 | `Image GenImageGradientRadial(int,int,float,Color,Color)` | `Image Framework_GenImageGradientRadial(int width, int height, float density, u8 innerR,innerG,innerB,innerA, u8 outerR,outerG,outerB,outerA)` | `(width, height As Integer, density As Single, innerR,…,innerA As Byte, outerR,…,outerA As Byte) As Image` |
| 11 | `Image GenImageGradientSquare(int,int,float,Color,Color)` | `Image Framework_GenImageGradientSquare(int width, int height, float density, u8 innerR,innerG,innerB,innerA, u8 outerR,outerG,outerB,outerA)` | same shape as #10 |
| 12 | `Image GenImageChecked(int,int,int,int,Color,Color)` | `Image Framework_GenImageChecked(int width, int height, int checksX, int checksY, u8 col1R,col1G,col1B,col1A, u8 col2R,col2G,col2B,col2A)` | `(width, height, checksX, checksY As Integer, col1R,…,col1A As Byte, col2R,…,col2A As Byte) As Image` |
| 13 | `Image GenImageWhiteNoise(int,int,float)` | `Image Framework_GenImageWhiteNoise(int width, int height, float factor)` | `(width, height As Integer, factor As Single) As Image` |
| 14 | `Image GenImagePerlinNoise(int,int,int,int,float)` | `Image Framework_GenImagePerlinNoise(int width, int height, int offsetX, int offsetY, float scale)` | `(width, height, offsetX, offsetY As Integer, scale As Single) As Image` |
| 15 | `Image GenImageCellular(int,int,int)` | `Image Framework_GenImageCellular(int width, int height, int tileSize)` | `(width, height, tileSize As Integer) As Image` |

### Non-mutating manipulation / query / palette (7)
| # | raylib 5.5 | engine export | wrapper |
|--|--|--|--|
| 16 | `Image ImageCopy(Image)` | `Image Framework_ImageCopy(Image image)` | `(image As Image) As Image` |
| 17 | `Image ImageFromImage(Image, Rectangle)` | `Image Framework_ImageFromImage(Image image, Rectangle rec)` | `(image As Image, rec As Rectangle) As Image` |
| 18 | `Image ImageFromChannel(Image, int)` | `Image Framework_ImageFromChannel(Image image, int selectedChannel)` | `(image As Image, selectedChannel As Integer) As Image` |
| 19 | `Rectangle GetImageAlphaBorder(Image, float)` | `Rectangle Framework_GetImageAlphaBorder(Image image, float threshold)` | `(image As Image, threshold As Single) As Rectangle` |
| 20 | `Color GetImageColor(Image, int, int)` | `Color Framework_GetImageColor(Image image, int x, int y)` | `(image As Image, x, y As Integer) As Color` |
| 21 | `Color* LoadImageColors(Image)` | `int Framework_LoadImageColors(Image image, Color* outColors)` *(caller-buffer)* | `(image As Image, outColors As Color()) As Integer` |
| 22 | `Color* LoadImagePalette(Image, int, int*)` | `int Framework_LoadImagePalette(Image image, int maxPaletteSize, Color* outColors)` *(caller-buffer)* | `(image As Image, maxPaletteSize As Integer, outColors As Color()) As Integer` |

**Forwarder shapes.** Value/return passthroughs reassemble `Color{r,g,b,a}` for params and
`return <raylibFn>(...)` for `Image`/`Color`/`Rectangle` returns. The two caller-buffer forwarders
follow `Framework_LoadCodepoints` exactly:
```cpp
int Framework_LoadImageColors(Image image, Color* outColors) {
    Color* src = LoadImageColors(image);
    if (!src) return 0;                          // invalid image guard
    int n = image.width * image.height;
    for (int i = 0; i < n; ++i) outColors[i] = src[i];
    UnloadImageColors(src);                       // raylib's paired free
    return n;
}
int Framework_LoadImagePalette(Image image, int maxPaletteSize, Color* outColors) {
    int count = 0;
    Color* pal = LoadImagePalette(image, maxPaletteSize, &count);
    if (!pal) return 0;                           // invalid image guard
    int n = count < maxPaletteSize ? count : maxPaletteSize;
    for (int i = 0; i < n; ++i) outColors[i] = pal[i];
    UnloadImagePalette(pal);                      // raylib's paired free
    return count;                                 // distinct-color count
}
```
Readers resolve the path (`std::string p = ResolveAssetPath(fileName); return LoadImageRaw(p.c_str(), …);`);
writers use it as-is (`return ExportImage(image, fileName);`).

---

## 4. Structs (`Utiliy.vb`)

**No new structs.** `Image` (:87), `Color` (:8), `Rectangle` (:54) are reused as-is. One cosmetic
pickup flagged in 3a: add the explicit `<StructLayout(LayoutKind.Sequential)>` to the `Color` struct
(:8) — every sibling struct has one and `Color` is the only one missing it. Purely for consistency:
a 4-byte all-`Byte` struct already defaults to sequential and 3a proved by-value `Color` returns work
without it, so this changes no behavior — it is done only while this batch already touches `Utiliy.vb`.

---

## 5. Verification (100% GL-free — no GUI smoke)

All 22 are CPU/RAM, so the automated suite is complete coverage (a window is never created).

1. **Parity guard** (`RaylibImageParityTests.cs`, NUnit text-scan, no engine load): the 22
   `Framework_<name>(` exports ↔ 22 `<DllImport>`s, trailing-`(` token boundary (keeps
   `Framework_ImageFromImage(` distinct from `Framework_ImageFromChannel(`, `Framework_LoadImage(`
   from `Framework_LoadImageRaw(`). Asserts `Batch3b.Length == 22`.
2. **Correctness** (`RaylibImageTests.cs`, `[Category("Integration")]`, self-contained local
   `[DllImport]` with local `RImage`/`RColor`/`RRectangle` mirrors, `Guard()` → `Assert.Ignore` on
   `DllNotFound`/`EntryPointNotFound`). Deterministic known values:
   - **Struct-return sanity:** `GenImageColor(3,2, 10,20,30,255)` → `IsImageValid` true, `width==3`,
     `height==2` (proves `Image` by-value return field-for-field).
   - **Caller-buffer `LoadImageColors`:** on that image, `outColors=New RColor(5){}`,
     `LoadImageColors(img, outColors)` returns `6`, and **every** pixel `== {10,20,30,255}` (proves
     the caller-buffer pixel path end-to-end).
   - **`GetImageColor`** `(img, 1, 1)` → `{10,20,30,255}`.
   - **`GenImageWhiteNoise`** is deterministic at the extremes: `factor 0f` → sampled pixel black
     `{0,0,0,255}`; `factor 1f` → white `{255,255,255,255}`.
   - **`GenImageChecked(2,2, 1,1, red, blue)`** → cell (0,0) red, adjacent cell blue (checked
     alternation) via `GetImageColor`.
   - **`ImageCopy`** → valid, same dims, `GetImageColor` matches source. **`ImageFromImage`** crop
     `rec{0,0,2,1}` from the 3×2 → `width==2, height==1`. **`ImageFromChannel`** channel 0 → valid,
     same dims.
   - **`GetImageAlphaBorder`** on a fully-opaque `GenImageColor(4,4,…,255)`, `threshold 0` → full
     rect `{0,0,4,4}`.
   - **`LoadImagePalette`** on a single-color `GenImageColor(4,4, 7,8,9,255)`, `maxPaletteSize 16` →
     returns `1`, `outColors[0] == {7,8,9,255}`.
   - **`IsImageValid`** on a zeroed `RImage{data=Zero,0,0,0,0}` → `false`.
   - **Gradient / perlin / cellular** (`GenImageGradientLinear/Radial/Square`, `GenImagePerlinNoise`,
     `GenImageCellular`) → valid image, correct dims (formula-dependent pixels → plausibility, per
     the 3a precedent for `ColorContrast`).
   - **File round-trip** (absolute temp paths, cleaned up in `finally`): `GenImageColor(2,2,
     50,60,70,255)` → `ExportImage(img, tmp.png)` true → read `tmp.png` bytes →
     `LoadImageFromMemory(".png", bytes, len)` valid, `2×2`, pixel `{50,60,70,255}`;
     `LoadImageAnim(tmp.png, out frames)` → `frames==1`, valid;
     `LoadImageAnimFromMemory(".png", bytes, len, out frames)` → `frames==1`;
     `ExportImageAsCode(img, tmp.h)` true and the file exists.
   - **`LoadImageRaw`:** write 16 raw bytes (2×2 × `{50,60,70,255}`) to an absolute temp file →
     `LoadImageRaw(tmp, 2, 2, 7 /*UNCOMPRESSED_R8G8B8A8*/, 0)` → valid, `GetImageColor` `{50,60,70,255}`.
3. **IDE refresh** ships the rebuilt engine `.dll`+`.lib` + wrapper `.dll` (the
   [[raylib-parity-shapes-batch1]] playbook: build the vcxproj with `-p:SolutionDir`, restore
   `-p:RestorePackagesConfig=true`; `TestVbDLL` only via VS MSBuild `-p:Platform=x64`). Confirm the
   `VisualGameStudio.Tests.csproj` `<None>` staging already copies `IDE\VisualGameStudioEngine.dll`+`.lib`.

---

## 6. Risks

- **`Image` by-value ABI (24 bytes, opaque ptr + 4 ints)** — very low risk, and NOT novel: the
  shipped `Framework_LoadImage` already **returns** `Image` by value (`framework.cpp:1238`, VB `… As
  Image`) and `Framework_UnloadImage`/`Framework_LoadFontFromImage` already **pass** `Image` by value.
  `Font` (same shape class, larger) is likewise returned/passed by value today. This batch just adds
  more of an ABI the engine already exercises. The §5.2 first assertion (`GenImageColor` dims correct
  after a by-value return) is a cheap smoke on it.
- **Caller-buffer sizing contract** — `LoadImageColors` requires the caller to size `width*height`;
  under-sizing overflows. Mitigated by: the engine copies exactly `width*height` (the buffer the
  caller is contractually required to provide) and the NULL guard; the wrapper's XML doc states the
  sizing rule; the correctness test sizes it correctly and asserts the full-count return.
- **`GenImageText`/`ImageText`/`ImageTextEx` font dependency** — the reason all three are deferred
  (§1). Not shipped here, so no headless-crash exposure this batch.
- **`ExportImageAsCode` side effect** — writes a `.h` file; the test writes to a temp path and
  deletes it in `finally`. `ExportImage`/`ExportImageAsCode` never resolve through the asset root
  (§2.7), so no stray writes into the project tree.
- **Blittable-array marshaling of `Color()` / `Byte()` without `<Out>`** — relies on the CLR
  pinning blittable arrays (writes visible in place). Proven by the shipped `Framework_LoadCodepoints`
  (`Integer()`) using the identical pattern; the §5.2 `LoadImageColors` assertion re-proves it for
  `Color()`.
