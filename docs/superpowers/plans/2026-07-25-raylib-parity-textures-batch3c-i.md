# raylib Textures Batch 3c-i (Image mutators) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the 22 raylib 5.5 `void ImageXxx(Image*, …)` in-place Image mutators as C-ABI engine exports (`framework.h`/`framework.cpp`) with 1:1 VB.NET P/Invoke bindings (`RaylibWrapper.vb`), under the ByRef-`Image` mutation contract.

**Architecture:** Each function is a thin passthrough. Engine forwarder takes `Image* img`, reassembles any `Color` params from `unsigned char r,g,b,a`, and calls the identically-named raylib function. VB binds `ByRef img As Image` (blittable struct → in-place reallocs propagate to the managed caller). No new structs. GL-free → 100% headless verification.

**Tech Stack:** C++20 (MSVC 14.44, VS 2022 Enterprise MSBuild), raylib 5.5.0 (static-linked, packages.config), VB.NET (net8.0), NUnit.

**Spec:** `docs/superpowers/specs/2026-07-25-raylib-parity-textures-batch3c-i-design.md`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `VisualGameStudioEngine/framework.h` | C-ABI export decls | +22 `__declspec(dllexport)` decls in a new `Batch 3c-i` banner block, adjacent to the existing `Batch 3b` image block (inside the single `extern "C"` region) |
| `VisualGameStudioEngine/framework.cpp` | forwarders | +22 forwarders in the same order, next to the existing image forwarders |
| `RaylibWrapper/RaylibWrapper.vb` | P/Invoke bindings | +22 `<DllImport>` Subs in a new `#Region "Raylib Image mutators (Batch 3c-i)"` |
| `VisualGameStudio.Tests/Native/RaylibImageMutatorParityTests.cs` | parity guard (text-scan) | **new** |
| `VisualGameStudio.Tests/Native/RaylibImageMutatorTests.cs` | correctness (`[Category("Integration")]`) | **new** |
| `IDE/VisualGameStudioEngine.dll`, `.lib`, `IDE/RaylibWrapper.dll` | prebuilt IDE artifacts | refreshed at finish |

**Conventions (from spec §2):** `Image*` engine ↔ `ByRef img As Image` wrapper. `Color` params → `unsigned char r,g,b,a` (engine) / `As Byte` (wrapper); forwarder rebuilds `Color{r,g,b,a}`; multi-color sigs use descriptive prefixes (`fillR`, `colorR`/`replaceR`) — never bare `or`. `ImageAlphaMask` secondary `Image alphaMask` passes **by value**. `ImageKernelConvolution` kernel → `Single()` array (bare, no `<MarshalAs>`) + faithful `kernelSize` count. Match adjacent formatting exactly.

---

## Task 0: Baseline

**Files:** none (verification only)

- [ ] **Step 1: Confirm branch + clean tree**

Run: `git branch --show-current` → `raylib-textures-3c-i-mutators`; `git status --short` → only the spec/plan docs (committed) or clean.

- [ ] **Step 2: Record baseline counts**

Run (PowerShell):
```powershell
(Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "__declspec\(dllexport\)").Count
(Select-String -Path "RaylibWrapper\RaylibWrapper.vb" -Pattern "<DllImport\(").Count
```
Expected baseline: **2581** exports / **2513** imports (target after batch: 2603 / 2535). Record actuals.

- [ ] **Step 3: Confirm the 3 skips exist and the 22 targets are absent**

Run: `Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "Framework_ImageResize\(|Framework_ImageFlipVertical\(|Framework_ImageColorInvert\("` → 3 hits (skips present). `Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "Framework_ImageColorTint\(|Framework_ImageCrop\(|Framework_ImageRotate\("` → 0 hits (targets absent). If any target already exists, STOP and reconcile with the spec.

---

## Task 1: Parity guard test (RED)

**Files:**
- Create: `VisualGameStudio.Tests/Native/RaylibImageMutatorParityTests.cs`

- [ ] **Step 1: Write the parity test** (model on `VisualGameStudio.Tests/Native/RaylibImageParityTests.cs`)

```csharp
using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3c-i (Image mutators): every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; of the same name in
/// RaylibWrapper.vb. Pure text scan — no engine load. Trailing '(' anchors near-name pairs
/// (Framework_ImageResize( != Framework_ImageResizeNN( != Framework_ImageResizeCanvas(;
/// Framework_ImageColorContrast( != Framework_ImageColorBrightness().
/// </summary>
[TestFixture]
public class RaylibImageMutatorParityTests
{
    private static readonly string[] Batch3cI =
    {
        "ImageFormat", "ImageToPOT", "ImageCrop", "ImageAlphaCrop", "ImageAlphaClear",
        "ImageAlphaMask", "ImageAlphaPremultiply", "ImageBlurGaussian", "ImageKernelConvolution",
        "ImageResizeNN", "ImageResizeCanvas", "ImageMipmaps", "ImageDither", "ImageFlipHorizontal",
        "ImageRotate", "ImageRotateCW", "ImageRotateCCW", "ImageColorTint", "ImageColorGrayscale",
        "ImageColorContrast", "ImageColorBrightness", "ImageColorReplace",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_batch3cI_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3cI)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3cI, Has.Length.EqualTo(22));
        });
    }
}
```

- [ ] **Step 2: Run — verify it FAILS**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibImageMutatorParityTests"`
Expected: FAIL (all 22 missing from framework.h and RaylibWrapper.vb).

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.Tests/Native/RaylibImageMutatorParityTests.cs
git commit -m "test(raylib): image-mutator Batch 3c-i parity guard (RED)"
```

---

## Task 2: 22 engine exports (framework.h decls + framework.cpp forwarders)

**Files:**
- Modify: `VisualGameStudioEngine/framework.h`
- Modify: `VisualGameStudioEngine/framework.cpp`

- [ ] **Step 1: Add the 22 decls to framework.h**

Locate the existing `Batch 3b` image banner block (grep `Batch 3b`). Immediately after it, inside the same `extern "C"` region, add (match the adjacent `__declspec(dllexport) void` alignment style):

```cpp
// ==== IMAGE MUTATORS (raylib 5.5 passthrough — Batch 3c-i) ====
__declspec(dllexport) void Framework_ImageFormat(Image* img, int newFormat);
__declspec(dllexport) void Framework_ImageToPOT(Image* img, unsigned char fillR, unsigned char fillG, unsigned char fillB, unsigned char fillA);
__declspec(dllexport) void Framework_ImageCrop(Image* img, Rectangle crop);
__declspec(dllexport) void Framework_ImageAlphaCrop(Image* img, float threshold);
__declspec(dllexport) void Framework_ImageAlphaClear(Image* img, unsigned char r, unsigned char g, unsigned char b, unsigned char a, float threshold);
__declspec(dllexport) void Framework_ImageAlphaMask(Image* img, Image alphaMask);
__declspec(dllexport) void Framework_ImageAlphaPremultiply(Image* img);
__declspec(dllexport) void Framework_ImageBlurGaussian(Image* img, int blurSize);
__declspec(dllexport) void Framework_ImageKernelConvolution(Image* img, const float* kernel, int kernelSize);
__declspec(dllexport) void Framework_ImageResizeNN(Image* img, int newWidth, int newHeight);
__declspec(dllexport) void Framework_ImageResizeCanvas(Image* img, int newWidth, int newHeight, int offsetX, int offsetY, unsigned char fillR, unsigned char fillG, unsigned char fillB, unsigned char fillA);
__declspec(dllexport) void Framework_ImageMipmaps(Image* img);
__declspec(dllexport) void Framework_ImageDither(Image* img, int rBpp, int gBpp, int bBpp, int aBpp);
__declspec(dllexport) void Framework_ImageFlipHorizontal(Image* img);
__declspec(dllexport) void Framework_ImageRotate(Image* img, int degrees);
__declspec(dllexport) void Framework_ImageRotateCW(Image* img);
__declspec(dllexport) void Framework_ImageRotateCCW(Image* img);
__declspec(dllexport) void Framework_ImageColorTint(Image* img, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageColorGrayscale(Image* img);
__declspec(dllexport) void Framework_ImageColorContrast(Image* img, float contrast);
__declspec(dllexport) void Framework_ImageColorBrightness(Image* img, int brightness);
__declspec(dllexport) void Framework_ImageColorReplace(Image* img, unsigned char colorR, unsigned char colorG, unsigned char colorB, unsigned char colorA, unsigned char replaceR, unsigned char replaceG, unsigned char replaceB, unsigned char replaceA);
```

- [ ] **Step 2: Add the 22 forwarders to framework.cpp**

Locate the existing `Batch 3b` image forwarders (grep `Batch 3b`). Immediately after them, add in the same order:

```cpp
// ==== IMAGE MUTATORS (raylib 5.5 passthrough — Batch 3c-i) ====
void Framework_ImageFormat(Image* img, int newFormat) { ImageFormat(img, newFormat); }
void Framework_ImageToPOT(Image* img, unsigned char fillR, unsigned char fillG, unsigned char fillB, unsigned char fillA) { Color c = { fillR, fillG, fillB, fillA }; ImageToPOT(img, c); }
void Framework_ImageCrop(Image* img, Rectangle crop) { ImageCrop(img, crop); }
void Framework_ImageAlphaCrop(Image* img, float threshold) { ImageAlphaCrop(img, threshold); }
void Framework_ImageAlphaClear(Image* img, unsigned char r, unsigned char g, unsigned char b, unsigned char a, float threshold) { Color c = { r, g, b, a }; ImageAlphaClear(img, c, threshold); }
void Framework_ImageAlphaMask(Image* img, Image alphaMask) { ImageAlphaMask(img, alphaMask); }
void Framework_ImageAlphaPremultiply(Image* img) { ImageAlphaPremultiply(img); }
void Framework_ImageBlurGaussian(Image* img, int blurSize) { ImageBlurGaussian(img, blurSize); }
void Framework_ImageKernelConvolution(Image* img, const float* kernel, int kernelSize) { ImageKernelConvolution(img, kernel, kernelSize); }
void Framework_ImageResizeNN(Image* img, int newWidth, int newHeight) { ImageResizeNN(img, newWidth, newHeight); }
void Framework_ImageResizeCanvas(Image* img, int newWidth, int newHeight, int offsetX, int offsetY, unsigned char fillR, unsigned char fillG, unsigned char fillB, unsigned char fillA) { Color c = { fillR, fillG, fillB, fillA }; ImageResizeCanvas(img, newWidth, newHeight, offsetX, offsetY, c); }
void Framework_ImageMipmaps(Image* img) { ImageMipmaps(img); }
void Framework_ImageDither(Image* img, int rBpp, int gBpp, int bBpp, int aBpp) { ImageDither(img, rBpp, gBpp, bBpp, aBpp); }
void Framework_ImageFlipHorizontal(Image* img) { ImageFlipHorizontal(img); }
void Framework_ImageRotate(Image* img, int degrees) { ImageRotate(img, degrees); }
void Framework_ImageRotateCW(Image* img) { ImageRotateCW(img); }
void Framework_ImageRotateCCW(Image* img) { ImageRotateCCW(img); }
void Framework_ImageColorTint(Image* img, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageColorTint(img, c); }
void Framework_ImageColorGrayscale(Image* img) { ImageColorGrayscale(img); }
void Framework_ImageColorContrast(Image* img, float contrast) { ImageColorContrast(img, contrast); }
void Framework_ImageColorBrightness(Image* img, int brightness) { ImageColorBrightness(img, brightness); }
void Framework_ImageColorReplace(Image* img, unsigned char colorR, unsigned char colorG, unsigned char colorB, unsigned char colorA, unsigned char replaceR, unsigned char replaceG, unsigned char replaceB, unsigned char replaceA) { Color c = { colorR, colorG, colorB, colorA }; Color rep = { replaceR, replaceG, replaceB, replaceA }; ImageColorReplace(img, c, rep); }
```

- [ ] **Step 3: Sanity-check counts** — `Select-String framework.h -Pattern "__declspec\(dllexport\)"` count == baseline + 22. (No build yet — build is Task 4.)

- [ ] **Step 4: Commit**

```bash
git add VisualGameStudioEngine/framework.h VisualGameStudioEngine/framework.cpp
git commit -m "feat(engine): raylib image mutators Batch 3c-i (22 exports, ByRef Image)"
```

---

## Task 3: 22 wrapper imports (parity GREEN)

**Files:**
- Modify: `RaylibWrapper/RaylibWrapper.vb`

- [ ] **Step 1: Add the 22 DllImport Subs**

In the `Public Module FrameworkWrapper`, add a new region (place it near the existing image DllImports / after the Batch 3b region). All return `void` → `Sub`. `ByRef img As Image` throughout; `ImageAlphaMask` secondary is `alphaMask As Image` (by value); `ImageKernelConvolution` kernel is a bare `Single()` array.

```vbnet
#Region "Raylib Image mutators (Batch 3c-i)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageFormat(ByRef img As Image, newFormat As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageToPOT(ByRef img As Image, fillR As Byte, fillG As Byte, fillB As Byte, fillA As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageCrop(ByRef img As Image, crop As Rectangle)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageAlphaCrop(ByRef img As Image, threshold As Single)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageAlphaClear(ByRef img As Image, r As Byte, g As Byte, b As Byte, a As Byte, threshold As Single)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageAlphaMask(ByRef img As Image, alphaMask As Image)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageAlphaPremultiply(ByRef img As Image)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageBlurGaussian(ByRef img As Image, blurSize As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageKernelConvolution(ByRef img As Image, kernel As Single(), kernelSize As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageResizeNN(ByRef img As Image, newWidth As Integer, newHeight As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageResizeCanvas(ByRef img As Image, newWidth As Integer, newHeight As Integer, offsetX As Integer, offsetY As Integer, fillR As Byte, fillG As Byte, fillB As Byte, fillA As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageMipmaps(ByRef img As Image)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDither(ByRef img As Image, rBpp As Integer, gBpp As Integer, bBpp As Integer, aBpp As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageFlipHorizontal(ByRef img As Image)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageRotate(ByRef img As Image, degrees As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageRotateCW(ByRef img As Image)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageRotateCCW(ByRef img As Image)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageColorTint(ByRef img As Image, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageColorGrayscale(ByRef img As Image)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageColorContrast(ByRef img As Image, contrast As Single)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageColorBrightness(ByRef img As Image, brightness As Integer)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageColorReplace(ByRef img As Image, colorR As Byte, colorG As Byte, colorB As Byte, colorA As Byte, replaceR As Byte, replaceG As Byte, replaceB As Byte, replaceA As Byte)
    End Sub
#End Region
```

- [ ] **Step 2: Run the parity guard — verify it PASSES**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibImageMutatorParityTests"`
Expected: PASS (pure text scan; no build of the engine needed). Import count == baseline + 22.

- [ ] **Step 3: Commit**

```bash
git add RaylibWrapper/RaylibWrapper.vb
git commit -m "feat(wrapper): raylib image mutators Batch 3c-i (22 DllImports, ByRef Image)"
```

---

## Task 4: Rebuild native engine

**Files:** none (build only) — produces `x64\Release\VisualGameStudioEngine.dll`+`.lib`

- [ ] **Step 1: Restore + build the vcxproj** (shapes-batch1 incantation)

```powershell
$msb = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msb "VisualGameStudioEngine.sln" -t:restore -p:RestorePackagesConfig=true -v:minimal
& $msb "VisualGameStudioEngine\VisualGameStudioEngine.vcxproj" -p:Configuration=Release -p:Platform=x64 "-p:SolutionDir=C:\Users\melvi\source\repos\VisualGameStudioEngine\" -v:minimal
```
Expected: `BUILD EXIT: 0`; `x64\Release\VisualGameStudioEngine.dll` rebuilt. Pre-existing C4190 warnings are benign. If a NEW error appears (e.g. a signature typo), fix it in Task 2's files and rebuild.

---

## Task 5: Correctness test (GL-free, verify GREEN with Skipped:0)

**Files:**
- Create: `VisualGameStudio.Tests/Native/RaylibImageMutatorTests.cs`

- [ ] **Step 1: Stage the freshly built DLL into `IDE\`** (the test copies the engine DLL from `IDE\`; stale → EntryPointNotFound → self-skip)

```powershell
Copy-Item "x64\Release\VisualGameStudioEngine.dll" "IDE\VisualGameStudioEngine.dll" -Force
Copy-Item "x64\Release\VisualGameStudioEngine.lib" "IDE\VisualGameStudioEngine.lib" -Force
```

- [ ] **Step 2: Write the correctness test** — model on `VisualGameStudio.Tests/Native/RaylibImageTests.cs` (3b): same `[Category("Integration")]`, local `[DllImport("VisualGameStudioEngine.dll")]`, local struct mirrors `RImage{IntPtr data;int width,height,mipmaps,format}` / `RColor{byte r,g,b,a}` / `RRect{float x,y,width,height}`, and the `Guard<T>` self-skip on `DllNotFoundException`/`EntryPointNotFoundException`. Reuse `Framework_GenImageColor` / `Framework_GenImageChecked` / `Framework_LoadImageColors` / `Framework_UnloadImage` (from 3b) to build/read images. Declare the 22 mutators with `ref RImage img` (blittable → in-place write-back). `PIXELFORMAT_UNCOMPRESSED_GRAYSCALE = 1`, `..._R8G8B8A8 = 7`.

Assertions (from spec §5.2) — one `[Test]` each or grouped logically:
  - **ByRef propagation (first proof):** `GenImageColor(3,2, red)` → `ImageResizeNN(ref img, 6,4)` → `img.width==6 && img.height==4`; `LoadImageColors` count == 24.
  - `ImageCrop(ref img, {0,0,2,2})` on 4×4 → `width==2 && height==2`.
  - `ImageColorGrayscale(ref img)` on a red image → every pixel `r==g==b`.
  - `ImageColorReplace(ref img, red, blue)` on red → all pixels blue.
  - `ImageColorTint(ref img, white, {128,128,128,255})` → pixel `r≈128` (±2).
  - `ImageColorBrightness(ref img, +60)` on mid-gray → brighter than original.
  - `ImageColorContrast(ref img, 0f)` → unchanged.
  - `ImageFlipHorizontal` on a 2×1 (left black / right white) → pixels swapped.
  - `ImageRotateCW` on a 2×1 → `width==1 && height==2`. `ImageRotate(ref img, 90)` runs + plausible.
  - `ImageFormat(ref img, GRAYSCALE=1)` → `img.format==1`; `LoadImageColors` still returns width*height (normalized RGBA).
  - `ImageMipmaps(ref img)` on 4×4 → `img.mipmaps > 1`.
  - `ImageToPOT(ref img, fill)` on 3×3 → `width` and `height` are powers of two ≥3 (expect 4×4).
  - `ImageAlphaMask(ref img, mask)` — build a mask via `GenImageColor`; run; the by-value-`Image` path executes without crash and masked alpha follows the mask.
  - `ImageKernelConvolution(ref img, identity3x3, 9)` where identity = `{0,0,0, 0,1,0, 0,0,0}` → image ≈ unchanged (proves the `Single()` array marshals).
  - `ImageBlurGaussian(ref img, 2)`, `ImageDither(ref img, 5,6,5,0)`, `ImageResizeCanvas(ref img, 6,6, 1,1, fill)`, `ImageAlphaCrop(ref img, 0f)`, `ImageAlphaClear(ref img, blank, 0f)`, `ImageAlphaPremultiply(ref img)`, `ImageRotateCCW`, `ImageFlipVertical`-analog not needed → run + plausible dimension/format state.
  - Free every image with `Framework_UnloadImage` at test end.

- [ ] **Step 3: Run — verify GREEN with Skipped:0**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibImageMutatorTests"`
Expected: `Passed: N, Skipped: 0`. **If Skipped>0 → the DLL is stale/missing the exports — re-stage (Step 1) and re-run.** Engine INFO log lines prove real execution.

- [ ] **Step 4: Commit**

```bash
git add VisualGameStudio.Tests/Native/RaylibImageMutatorTests.cs
git commit -m "test(raylib): image-mutator Batch 3c-i correctness (GL-free, ByRef propagation)"
```

---

## Task 6: IDE refresh + Definition-of-Done + finish

**Files:**
- Rebuild + refresh: `IDE/VisualGameStudioEngine.dll`, `.lib`, `IDE/RaylibWrapper.dll`

- [ ] **Step 1: Rebuild the VB wrapper**

Run: `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → EXIT 0. (Confirms the 22 DllImports are VB-valid.)

- [ ] **Step 2: Refresh all three IDE artifacts**

```powershell
robocopy "x64\Release" "IDE" VisualGameStudioEngine.dll VisualGameStudioEngine.lib /R:1 /W:1
robocopy "RaylibWrapper\bin\Release\net8.0" "IDE" RaylibWrapper.dll /R:1 /W:1
```
(robocopy exit codes 0–7 are success.)

- [ ] **Step 3: Definition-of-Done greps**

```powershell
(Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "__declspec\(dllexport\)").Count   # == baseline + 22 (2603)
(Select-String -Path "RaylibWrapper\RaylibWrapper.vb" -Pattern "<DllImport\(").Count                  # == baseline + 22 (2535)
```
Confirm no `ImageText`/`ImageTextEx`/`ImageDrawText`/`ImageDrawTextEx` were bound (`Select-String RaylibWrapper.vb -Pattern "Framework_ImageText|Framework_ImageDrawText"` → 0 hits).

- [ ] **Step 4: Full parity + correctness + fast subset**

Run:
```
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibImageMutator"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```
Expected: mutator parity + correctness GREEN (Skipped:0); fast subset ~3311✓, 0 failures.

- [ ] **Step 5: Finish** — invoke superpowers:finishing-a-development-branch. Tests pass → present the 4 options; on "merge it" ff-merge to master + push + delete branch (established repo pattern). Update memory (`raylib-parity-textures-batch3c-i.md` ledger + the 3a/3b roadmap bullets + MEMORY.md pointer). 3c-ii (20 software-drawing fns) remains.

---

## Notes
- **DRY/YAGNI:** pure passthrough, no helper layer, no new structs. Do not "improve" raylib's behavior (kernel square-validation, degree clamping) — faithful passthrough.
- **TDD:** parity RED (Task 1) → GREEN (Task 3); correctness proven against the freshly built DLL (Task 5).
- **The one correctness-critical detail:** every `Image*`/`ByRef img As Image`. A by-value slip silently discards mutations AND risks a double-free — the Task 5 ByRef-propagation assertions (dims read back after resize/crop) are the guard.
