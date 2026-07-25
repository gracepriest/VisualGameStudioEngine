# raylib 5.5 Parity — Textures Batch 3b (Image load / generate / query in RAM) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 22 raylib 5.5 "Image in RAM" functions (load / generate / non-mutating query) to the engine C-ABI + VB wrapper — establishing the **`Image`-struct-by-value** and **caller-buffer `Color[]`** conventions the rest of the textures module inherits.

**Architecture:** Additive, same shared architecture as shipped [[raylib-parity-textures-batch3a]] / [[raylib-parity-text-batch2]]. `Image` crosses the ABI **by value** (opaque `IntPtr data` + 4 ints = 24 bytes; the engine already returns `Image` by value from `Framework_LoadImage` and passes it by value to `Framework_UnloadImage`). Color **params** decompose to `unsigned char r,g,b,a` (house rule); `Color`/`Rectangle` **returns** come back by value (proven in 3a). The two `Color*`-returning functions use the **caller-buffer** pattern (engine copies into a caller-owned `Color[]` and frees raylib's buffer internally — the shipped `Framework_LoadCodepoints` shape), so no new Unload exports are needed. All 22 are GL-free → **no GUI smoke; the automated correctness suite is complete coverage.**

**Tech Stack:** C++17 engine (raylib 5.5 static-linked), VB.NET net8.0 wrapper, NUnit. Build via vswhere MSBuild (`-p:SolutionDir=<repo>\`, restore `-p:RestorePackagesConfig=true`); TestVbDLL is not touched. Spec: `docs/superpowers/specs/2026-07-24-raylib-parity-textures-batch3b-design.md` (authoritative — §3 table lists all 22 signatures + wrapper shapes).

---

## Key decisions (from the spec)
1. **`Image` RETURN/PARAM = by value** (`As Image`) — reuses the existing `Utiliy.vb` `Image` struct; freed by the existing `Framework_UnloadImage`. No new struct, no new Unload.
2. **Caller-buffer for `LoadImageColors`/`LoadImagePalette`** — `int Framework_LoadImageColors(Image, Color* outColors)` / `int Framework_LoadImagePalette(Image, int maxPaletteSize, Color* outColors)`; the engine copies + frees raylib's buffer, returns the count. VB side `outColors As Color()` (blittable → pinned in place, **no `<Out>`**, exactly like `Framework_LoadCodepoints`'s `Integer()`). **This obviates `UnloadImageColors`/`UnloadImagePalette` — do NOT bind them.**
3. **Color params → `u8 r,g,b,a`** with **descriptive names** in multi-color signatures (`startR…`, `innerR…`, `col1R…`) — never the bare token `or` (reserved C++ operator). `Color`/`Rectangle` returns → by value; **bool** → `<MarshalAs(I1)>`; strings → `CharSet.Ansi`; file-data `unsigned char*` → `Byte()` + `int dataSize`; `int* frames` → `ByRef`.
4. **Asset-path asymmetry** — readers (`LoadImageRaw`, `LoadImageAnim`) resolve via `ResolveAssetPath` (like `Framework_LoadImage`); writers (`ExportImage`, `ExportImageAsCode`) use the path **as-is**.
5. **6 functions are NOT in this batch** (spec §1): `ImageText`/`ImageTextEx`/`GenImageText` (font/window-dependent → 3d), `ExportImageToMemory` (unknown-size ptr → file-I/O batch), `UnloadImageColors`/`UnloadImagePalette` (obsoleted by caller-buffer). Do not add them.
6. **Insertion:** decls after the existing `// Images` block (`framework.h:421`, before `// Fonts / advanced text` :423); forwarders after the existing image forwarders (`framework.cpp:1245`, before `Framework_LoadFontEx` :1247) — keeps image code together and inside the existing `extern "C"` scope. Wrapper in a new `#Region "Raylib Image load/gen/query (Batch 3b)"`.

---

## File structure
| File | Change | Responsibility |
|---|---|---|
| `RaylibWrapper/Utiliy.vb` | Modify (Color `<StructLayout>`) | cosmetic hygiene (no new struct) |
| `VisualGameStudioEngine/framework.h` | Modify (+22 decls) | export declarations |
| `VisualGameStudioEngine/framework.cpp` | Modify (+22 forwarders) | forwarders (reassemble Color params; return Image/Color/Rectangle by value; 2 caller-buffer copies; resolve reader paths) |
| `RaylibWrapper/RaylibWrapper.vb` | Modify (+22 DllImports) | bindings, new `#Region` |
| `VisualGameStudio.Tests/Native/RaylibImageParityTests.cs` | Create | parity scan (no engine) |
| `VisualGameStudio.Tests/Native/RaylibImageTests.cs` | Create | GL-free correctness (local `[DllImport]`, Integration) |
| `IDE/VisualGameStudioEngine.{dll,lib}`, `IDE/RaylibWrapper.dll` | Refresh | ship exports/bindings |

The 22 functions are in spec §3 (authoritative). **No GUI smoke / TestVbDLL scene** (all GL-free).

---

## Task 0: Build baseline
FOREGROUND. (Engine builds since Batch 1; MSBuild path below is proven on this machine — if absent, discover via `vswhere -latest -find MSBuild\**\Bin\MSBuild.exe`.)
- [ ] **Step 1:** restore: `& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" VisualGameStudioEngine.sln -t:restore -p:RestorePackagesConfig=true` (idempotent).
- [ ] **Step 2:** engine build → `& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" "VisualGameStudioEngine\VisualGameStudioEngine.vcxproj" -p:Configuration=Release -p:Platform=x64 "-p:SolutionDir=C:\Users\melvi\source\repos\VisualGameStudioEngine\" -v:minimal` → 0 errors.
- [ ] **Step 3:** wrapper build → `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 4:** counts framework.h `__declspec(dllexport)` = **2559**; RaylibWrapper.vb `<DllImport(` = **2491**. No commit.

## Task 1: Color struct hygiene
**Files:** Modify `RaylibWrapper/Utiliy.vb:8` — add the explicit `<StructLayout>` to `Color` (the only sibling struct missing it). Behavior-neutral (defaults to sequential already); done while this batch touches the file.
- [ ] **Step 1:** change the `Color` declaration from `Public Structure Color` to:
```vbnet
<StructLayout(LayoutKind.Sequential)>
Public Structure Color
```
(leave the fields/ctor unchanged; `Imports System.Runtime.InteropServices` is already present).
- [ ] **Step 2:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 3: Commit** `chore(wrapper): explicit StructLayout on Color struct (raylib textures Batch 3b hygiene)`.

## Task 2: Parity guard (RED)
**Files:** Create `VisualGameStudio.Tests/Native/RaylibImageParityTests.cs` — same shape as `RaylibColorParityTests.cs` (read it as the template: `RepoRoot()` climbs to `VisualGameStudioEngine.sln`, reads `framework.h` + `RaylibWrapper.vb`, asserts each `Framework_<name>(` in BOTH). The **22** names (order irrelevant):
```
LoadImageRaw, LoadImageAnim, LoadImageAnimFromMemory, LoadImageFromMemory, IsImageValid,
ExportImage, ExportImageAsCode, GenImageColor, GenImageGradientLinear, GenImageGradientRadial,
GenImageGradientSquare, GenImageChecked, GenImageWhiteNoise, GenImagePerlinNoise, GenImageCellular,
ImageCopy, ImageFromImage, ImageFromChannel, GetImageAlphaBorder, GetImageColor,
LoadImageColors, LoadImagePalette
```
Assert `Has.Length.EqualTo(22)`. Class `RaylibImageParityTests`.
> ⚠ Token boundary: the trailing `(` keeps names distinct — `Framework_LoadImage(` (existing) ≠ `Framework_LoadImageRaw(`; `Framework_ImageFromImage(` ≠ `Framework_ImageFromChannel(`. Do NOT list `LoadImage`/`UnloadImage`/`ImageResize` (already-existing, not this batch).
- [ ] **Step 1:** write it. **Step 2:** run `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibImageParityTests"` → RED (22 missing). **Step 3: Commit** `test(raylib): image load/gen/query Batch 3b parity guard (red)`.

## Task 3: The 22 functions (engine + wrapper)
**Files:** `framework.h` (after :421), `framework.cpp` (after :1245 — grep `Framework_ImageFlipVertical` to find the real forwarder anchor; the `.cpp` definitions are NOT at the same line offset as the `.h` decls), `RaylibWrapper.vb` (new `#Region`). Add all 22 per spec §3, `.h`/`.cpp`/`.vb` in identical order. New banner in `.h`/`.cpp`: `// ==== IMAGE LOAD/GEN/QUERY (raylib 5.5 passthrough — Batch 3b) ====`.

**Worked examples (one per distinct shape — the remaining functions map onto these; full list in spec §3):**
```cpp
// framework.h  (decls)
__declspec(dllexport) Image     Framework_GenImageColor(int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) Image     Framework_GenImageChecked(int width, int height, int checksX, int checksY, unsigned char col1R, unsigned char col1G, unsigned char col1B, unsigned char col1A, unsigned char col2R, unsigned char col2G, unsigned char col2B, unsigned char col2A);
__declspec(dllexport) Image     Framework_LoadImageRaw(const char* fileName, int width, int height, int format, int headerSize);
__declspec(dllexport) Image     Framework_LoadImageAnim(const char* fileName, int* frames);
__declspec(dllexport) Image     Framework_LoadImageFromMemory(const char* fileType, const unsigned char* fileData, int dataSize);
__declspec(dllexport) bool      Framework_IsImageValid(Image image);
__declspec(dllexport) bool      Framework_ExportImage(Image image, const char* fileName);
__declspec(dllexport) Image     Framework_ImageFromImage(Image image, Rectangle rec);
__declspec(dllexport) Rectangle Framework_GetImageAlphaBorder(Image image, float threshold);
__declspec(dllexport) Color     Framework_GetImageColor(Image image, int x, int y);
__declspec(dllexport) int       Framework_LoadImageColors(Image image, Color* outColors);
__declspec(dllexport) int       Framework_LoadImagePalette(Image image, int maxPaletteSize, Color* outColors);
```
```cpp
// framework.cpp  (forwarders)
Image Framework_GenImageColor(int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    return GenImageColor(width, height, Color{r,g,b,a});
}
Image Framework_GenImageChecked(int width, int height, int checksX, int checksY,
        unsigned char col1R, unsigned char col1G, unsigned char col1B, unsigned char col1A,
        unsigned char col2R, unsigned char col2G, unsigned char col2B, unsigned char col2A) {
    return GenImageChecked(width, height, checksX, checksY, Color{col1R,col1G,col1B,col1A}, Color{col2R,col2G,col2B,col2A});
}
Image Framework_LoadImageRaw(const char* fileName, int width, int height, int format, int headerSize) {
    std::string p = ResolveAssetPath(fileName);            // reader: resolve
    return LoadImageRaw(p.c_str(), width, height, format, headerSize);
}
Image Framework_LoadImageAnim(const char* fileName, int* frames) {
    std::string p = ResolveAssetPath(fileName);            // reader: resolve
    return LoadImageAnim(p.c_str(), frames);
}
Image Framework_LoadImageFromMemory(const char* fileType, const unsigned char* fileData, int dataSize) {
    return LoadImageFromMemory(fileType, fileData, dataSize);
}
bool  Framework_IsImageValid(Image image) { return IsImageValid(image); }
bool  Framework_ExportImage(Image image, const char* fileName) { return ExportImage(image, fileName); }   // writer: as-is
Image Framework_ImageFromImage(Image image, Rectangle rec) { return ImageFromImage(image, rec); }
Rectangle Framework_GetImageAlphaBorder(Image image, float threshold) { return GetImageAlphaBorder(image, threshold); }
Color Framework_GetImageColor(Image image, int x, int y) { return GetImageColor(image, x, y); }
int Framework_LoadImageColors(Image image, Color* outColors) {
    Color* src = LoadImageColors(image);
    if (!src) return 0;                                    // invalid-image guard
    int n = image.width * image.height;
    for (int i = 0; i < n; ++i) outColors[i] = src[i];
    UnloadImageColors(src);                                 // raylib's paired free
    return n;
}
int Framework_LoadImagePalette(Image image, int maxPaletteSize, Color* outColors) {
    int count = 0;
    Color* pal = LoadImagePalette(image, maxPaletteSize, &count);
    if (!pal) return 0;                                    // invalid-image guard
    int n = count < maxPaletteSize ? count : maxPaletteSize;
    for (int i = 0; i < n; ++i) outColors[i] = pal[i];
    UnloadImagePalette(pal);                                // raylib's paired free
    return count;
}
```
```vbnet
' RaylibWrapper.vb — #Region "Raylib Image load/gen/query (Batch 3b)"
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GenImageColor(width As Integer, height As Integer, r As Byte, g As Byte, b As Byte, a As Byte) As Image
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
Public Function Framework_LoadImageRaw(fileName As String, width As Integer, height As Integer, format As Integer, headerSize As Integer) As Image
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
Public Function Framework_LoadImageAnim(fileName As String, ByRef frames As Integer) As Image
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
Public Function Framework_LoadImageFromMemory(fileType As String, fileData As Byte(), dataSize As Integer) As Image
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_IsImageValid(image As Image) As <MarshalAs(UnmanagedType.I1)> Boolean
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
Public Function Framework_ExportImage(image As Image, fileName As String) As <MarshalAs(UnmanagedType.I1)> Boolean
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_ImageFromImage(image As Image, rec As Rectangle) As Image
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GetImageAlphaBorder(image As Image, threshold As Single) As Rectangle
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GetImageColor(image As Image, x As Integer, y As Integer) As Color
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_LoadImageColors(image As Image, outColors As Color()) As Integer
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_LoadImagePalette(image As Image, maxPaletteSize As Integer, outColors As Color()) As Integer
End Function
```
**Remaining 10 follow the shapes above (spec §3):** `LoadImageAnimFromMemory` (byte-array + `ByRef frames`, `As Image`, Ansi); `ExportImageAsCode` (bool, Ansi, writer as-is); `GenImageGradientLinear` (`int direction` + two decomposed colors `startR…/endR…`); `GenImageGradientRadial`/`GenImageGradientSquare` (`float density` + `innerR…/outerR…`); `GenImageWhiteNoise` (`float factor`); `GenImagePerlinNoise` (`int offsetX,offsetY, float scale`); `GenImageCellular` (`int tileSize`); `ImageCopy` (`Image`→`Image`); `ImageFromChannel` (`int selectedChannel`).
- [ ] **Step 1–3:** add 22 decls / forwarders / DllImports (same order in all three files).
- [ ] **Step 4:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors (VB compiles independent of the native build).
- [ ] **Step 5: Commit** `feat(engine): raylib image load/gen/query Batch 3b (22 fns, Image-by-value + caller-buffer)`.

## Task 4: Rebuild, stage, parity GREEN
- [ ] **Step 1:** engine build (Task 0 Step 2 cmd) → 0 errors. **Step 2:** wrapper build → 0 errors.
- [ ] **Step 3:** counts framework.h == **2581**, RaylibWrapper.vb == **2513** (+22 each).
- [ ] **Step 4:** parity test GREEN (`--filter "FullyQualifiedName~RaylibImageParityTests"`).

## Task 5: Correctness suite (automated, Integration) — go/no-go for Image-by-value + caller-buffer
**Files:** Create `VisualGameStudio.Tests/Native/RaylibImageTests.cs` — read `RaylibColorTests.cs` as the template. `[Category("Integration")]`, self-contained local `[DllImport("VisualGameStudioEngine.dll", CallingConvention=Cdecl)]`, self-skip via a `Guard<T>` helper on `DllNotFoundException`/`EntryPointNotFoundException`. Local mirrors:
```csharp
[StructLayout(LayoutKind.Sequential)] private struct RColor { public byte r, g, b, a; }
[StructLayout(LayoutKind.Sequential)] private struct RImage { public IntPtr data; public int width, height, mipmaps, format; }
[StructLayout(LayoutKind.Sequential)] private struct RRect  { public float x, y, width, height; }
private const int PIXELFORMAT_UNCOMPRESSED_R8G8B8A8 = 7;
```
Local `[DllImport]` each of the 22 (returns: `RImage`/`RColor`/`RRect`/`bool [return:MarshalAs(I1)]`/`int`; `Framework_LoadImageColors(RImage, RColor[], ...)`). **The FIRST test proves `Image` by-value return + the caller-buffer pixel path** — if it fails, STOP and surface before continuing.

Assertions (spec §5.2; all GL-free, deterministic):
- **Go/no-go:** `var img = Framework_GenImageColor(3,2, 10,20,30,255);` → `Framework_IsImageValid(img)` true, `img.width==3`, `img.height==2`. Then `var buf = new RColor[6]; var n = Framework_LoadImageColors(img, buf);` → `n==6` and **every** element `{10,20,30,255}`. (`Framework_UnloadImage(img)` after.)
- `Framework_GetImageColor(img, 1, 1)` → `{10,20,30,255}`.
- White-noise extremes: `GenImageWhiteNoise(4,4, 0f)` → `GetImageColor(.,0,0)` == `{0,0,0,255}`; `GenImageWhiteNoise(4,4, 1f)` → `{255,255,255,255}`.
- `GenImageChecked(2,2, 1,1, 255,0,0,255, 0,0,255,255)` → `GetImageColor(.,0,0)`==red, `GetImageColor(.,1,0)`==blue.
- `ImageCopy(img)` → valid, same dims, `GetImageColor` matches. `ImageFromImage(img, RRect{0,0,2,1})` → `width==2, height==1`. `ImageFromChannel(img, 0)` → valid, same dims.
- `GetImageAlphaBorder(GenImageColor(4,4,9,9,9,255), 0f)` → `{0,0,4,4}`.
- `LoadImagePalette(GenImageColor(4,4, 7,8,9,255), 16, buf16)` → returns `1`, `buf16[0]=={7,8,9,255}`.
- `IsImageValid(new RImage())` (all-zero) → false.
- Plausibility (formula-dependent): `GenImageGradientLinear/Radial/Square`, `GenImagePerlinNoise`, `GenImageCellular` → `IsImageValid` true + dims correct.
- **File round-trip** (absolute temp paths, `try/finally` delete):
```csharp
var tmpPng = Path.Combine(Path.GetTempPath(), "vgs3b_" + Guid.NewGuid().ToString("N") + ".png");
var img2 = Framework_GenImageColor(2,2, 50,60,70,255);
Assert.That(Framework_ExportImage(img2, tmpPng), Is.True);          // writer as-is
var bytes = File.ReadAllBytes(tmpPng);
var fromMem = Framework_LoadImageFromMemory(".png", bytes, bytes.Length);
Assert.That(Framework_IsImageValid(fromMem), Is.True);
Assert.That((fromMem.width, fromMem.height), Is.EqualTo((2,2)));
Assert.That(Framework_GetImageColor(fromMem, 0, 0), Is.EqualTo(new RColor{r=50,g=60,b=70,a=255}));
int frames = 0; var anim = Framework_LoadImageAnim(tmpPng, ref frames);   // reader resolves; abs path OK
Assert.That(frames, Is.EqualTo(1)); Assert.That(Framework_IsImageValid(anim), Is.True);
int framesM = 0; var animM = Framework_LoadImageAnimFromMemory(".png", bytes, bytes.Length, ref framesM);
Assert.That(framesM, Is.EqualTo(1));
// Unload img2/fromMem/anim/animM; delete tmpPng in finally.
```
- **`LoadImageRaw`:** write 16 bytes (`2×2 × {50,60,70,255}`) to an absolute temp file → `Framework_LoadImageRaw(tmpRaw, 2, 2, 7, 0)` → valid, `GetImageColor(.,0,0)=={50,60,70,255}`; delete in `finally`.
- **`ExportImageAsCode`:** `Framework_ExportImageAsCode(img2, tmpH)` true and `File.Exists(tmpH)`; delete in `finally`.
- [ ] **Step 1:** write it (RColor field comparisons: either `Assert.That((c.r,c.g,c.b,c.a), Is.EqualTo(((byte)…)))` tuples or per-field, matching `RaylibColorTests.cs`). **Step 2:** runs GREEN after Task 6 stages the fresh DLL (or stage `x64\Release\VisualGameStudioEngine.dll` for an immediate run). **Step 3: Commit** `test(raylib): image load/gen/query Batch 3b correctness incl. Image-by-value + caller-buffer (integration)`.

## Task 6: IDE refresh + DoD + finish
- [ ] **Step 1:** clear locks (`dotnet build-server shutdown`, kill stray `--lsp`/testhost), robocopy `x64\Release\VisualGameStudioEngine.{dll,lib}` + `RaylibWrapper\bin\Release\net8.0\RaylibWrapper.dll` → `IDE\` (`/R:1 /W:1`). Commit `chore: refresh prebuilt IDE binaries (engine+wrapper) with raylib image Batch 3b`.
- [ ] **Step 2:** run correctness suite (`--filter "FullyQualifiedName~RaylibImageTests"`) → GREEN (incl. the go/no-go first test).
- [ ] **Step 3: DoD:** parity GREEN (22; counts 2581/2513); fast subset (`--filter "TestCategory!=Integration"`) no regression; correctness GREEN; grep guards (the 6 deferred names — `ImageText(`, `ImageTextEx(`, `GenImageText(`, `ExportImageToMemory(`, `UnloadImageColors(`, `UnloadImagePalette(` — are **absent** from framework.h/RaylibWrapper.vb; the existing `Framework_LoadImage/UnloadImage/ImageColorInvert/Resize/FlipVertical` are **untouched**). **No GUI smoke — all GL-free.**
- [ ] **Step 4:** superpowers:finishing-a-development-branch → merge to master, push. Update memory ([[raylib-parity-textures-batch3a]] roadmap: mark 3b shipped; 3c/3d + the 3 deferred text-image fns next).
```
