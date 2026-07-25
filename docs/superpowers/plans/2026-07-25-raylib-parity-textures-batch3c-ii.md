# raylib Textures Batch 3c-ii (Image software drawing) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the 20 raylib 5.5 `void ImageDrawXxx(Image*, …)` software (CPU) drawing functions as C-ABI engine exports (`framework.h`/`framework.cpp`) with 1:1 VB.NET P/Invoke bindings (`RaylibWrapper.vb`), under the inherited ByRef-`Image` contract.

**Architecture:** Thin passthrough. Engine forwarder takes `Image* dst`, reassembles `Color` params from `unsigned char r,g,b,a`, and calls the identically-named raylib function; `ImageDraw`'s `src` and the two `Rectangle`s and all `Vector2`s pass by value; `Vector2*` arrays pass as a bare pointer+count. VB binds `ByRef dst As Image`. No new structs. GL-free → 100% headless verification.

**Tech Stack:** C++20 (MSVC 14.44, VS 2022 Enterprise MSBuild), raylib 5.5.0 (static, packages.config), VB.NET (net8.0), NUnit.

**Spec:** `docs/superpowers/specs/2026-07-25-raylib-parity-textures-batch3c-ii-design.md`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `VisualGameStudioEngine/framework.h` | C-ABI export decls | +20 decls in a new `Batch 3c-ii` banner, after the `Batch 3c-i` block (inside `extern "C"`) |
| `VisualGameStudioEngine/framework.cpp` | forwarders | +20 forwarders in the same order, after the `Batch 3c-i` forwarders |
| `RaylibWrapper/RaylibWrapper.vb` | P/Invoke bindings | +20 `<DllImport>` Subs in a new `#Region "Raylib Image software drawing (Batch 3c-ii)"` |
| `VisualGameStudio.Tests/Native/RaylibImageDrawParityTests.cs` | parity guard | **new** |
| `VisualGameStudio.Tests/Native/RaylibImageDrawTests.cs` | correctness (`[Category("Integration")]`) | **new** |
| `IDE/VisualGameStudioEngine.dll`, `.lib`, `IDE/RaylibWrapper.dll` | prebuilt IDE artifacts | refreshed at finish |

**Conventions:** `Image* dst` ↔ `ByRef dst As Image`. `Color`→`u8 r,g,b,a` (forwarder rebuilds `Color{r,g,b,a}`). `Vector2`/`Rectangle` by value. `ImageDraw` `src` **by value** (never `&src`). `Vector2*` arrays → engine `Vector2* points, int pointCount`; wrapper bare `points As Vector2()` (no `<MarshalAs>`). `ImageDrawTriangleEx` 3 colors → `c1R…/c2R…/c3R…`. **⚠ VB keyword:** raylib's `end` param is renamed `endPos` in the wrapper (param names are ABI-irrelevant; `End` is a VB reserved word). NEVER bind `ImageDrawText`/`ImageDrawTextEx`.

---

## Task 0: Baseline

**Files:** none (verification only)

- [ ] **Step 1:** `git branch --show-current` → `raylib-textures-3c-ii-drawing`; `git status --short` clean (spec committed).
- [ ] **Step 2: Record baseline counts** (PowerShell):
```powershell
(Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "__declspec\(dllexport\)").Count   # expect 2603
(Select-String -Path "RaylibWrapper\RaylibWrapper.vb" -Pattern "<DllImport\(").Count                  # expect 2535
```
Target after batch: 2623 / 2555. Record actuals.
- [ ] **Step 3: Confirm the 20 targets are absent** — `Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "Framework_ImageDraw|Framework_ImageClearBackground"` → 0 hits. If any exists, STOP and reconcile.

---

## Task 1: Parity guard test (RED)

**Files:** Create `VisualGameStudio.Tests/Native/RaylibImageDrawParityTests.cs`

- [ ] **Step 1: Write the parity test** (model on `RaylibImageMutatorParityTests.cs`)

```csharp
using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3c-ii (Image software drawing): every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; in RaylibWrapper.vb.
/// Pure text scan — no engine load. Trailing '(' anchors near-name pairs (Framework_ImageDraw(
/// != Framework_ImageDrawPixel(; Framework_ImageDrawCircle( != Framework_ImageDrawCircleV( !=
/// Framework_ImageDrawCircleLines( != Framework_ImageDrawCircleLinesV().
/// </summary>
[TestFixture]
public class RaylibImageDrawParityTests
{
    private static readonly string[] Batch3cII =
    {
        "ImageClearBackground", "ImageDrawPixel", "ImageDrawPixelV", "ImageDrawLine", "ImageDrawLineV",
        "ImageDrawLineEx", "ImageDrawCircle", "ImageDrawCircleV", "ImageDrawCircleLines", "ImageDrawCircleLinesV",
        "ImageDrawRectangle", "ImageDrawRectangleV", "ImageDrawRectangleRec", "ImageDrawRectangleLines",
        "ImageDrawTriangle", "ImageDrawTriangleEx", "ImageDrawTriangleLines", "ImageDrawTriangleFan",
        "ImageDrawTriangleStrip", "ImageDraw",
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
    public void Every_batch3cII_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3cII)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3cII, Has.Length.EqualTo(20));
        });
    }
}
```

- [ ] **Step 2: Run — verify it FAILS.** `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibImageDrawParityTests"` → FAIL (all 20 absent).
- [ ] **Step 3: Commit.** `git add …RaylibImageDrawParityTests.cs; git commit -m "test(raylib): image-draw Batch 3c-ii parity guard (RED)"`

---

## Task 2: 20 engine exports (framework.h decls + framework.cpp forwarders)

**Files:** Modify `VisualGameStudioEngine/framework.h`, `VisualGameStudioEngine/framework.cpp`

- [ ] **Step 1: Add the 20 decls to framework.h** — after the `Batch 3c-i` block (grep `Batch 3c-i`), inside `extern "C"`, matching adjacent formatting:

```cpp
// ==== IMAGE SOFTWARE DRAWING (raylib 5.5 passthrough — Batch 3c-ii) ====
__declspec(dllexport) void Framework_ImageClearBackground(Image* dst, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawPixel(Image* dst, int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawPixelV(Image* dst, Vector2 position, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawLine(Image* dst, int startPosX, int startPosY, int endPosX, int endPosY, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawLineV(Image* dst, Vector2 start, Vector2 end, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawLineEx(Image* dst, Vector2 start, Vector2 end, int thick, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawCircle(Image* dst, int centerX, int centerY, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawCircleV(Image* dst, Vector2 center, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawCircleLines(Image* dst, int centerX, int centerY, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawCircleLinesV(Image* dst, Vector2 center, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawRectangle(Image* dst, int posX, int posY, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawRectangleV(Image* dst, Vector2 position, Vector2 size, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawRectangleRec(Image* dst, Rectangle rec, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawRectangleLines(Image* dst, Rectangle rec, int thick, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawTriangle(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawTriangleEx(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, unsigned char c1R, unsigned char c1G, unsigned char c1B, unsigned char c1A, unsigned char c2R, unsigned char c2G, unsigned char c2B, unsigned char c2A, unsigned char c3R, unsigned char c3G, unsigned char c3B, unsigned char c3A);
__declspec(dllexport) void Framework_ImageDrawTriangleLines(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawTriangleFan(Image* dst, Vector2* points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawTriangleStrip(Image* dst, Vector2* points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDraw(Image* dst, Image src, Rectangle srcRec, Rectangle dstRec, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
```

- [ ] **Step 2: Add the 20 forwarders to framework.cpp** — after the `Batch 3c-i` forwarders, same order:

```cpp
// ==== IMAGE SOFTWARE DRAWING (raylib 5.5 passthrough — Batch 3c-ii) ====
void Framework_ImageClearBackground(Image* dst, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageClearBackground(dst, c); }
void Framework_ImageDrawPixel(Image* dst, int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawPixel(dst, posX, posY, c); }
void Framework_ImageDrawPixelV(Image* dst, Vector2 position, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawPixelV(dst, position, c); }
void Framework_ImageDrawLine(Image* dst, int startPosX, int startPosY, int endPosX, int endPosY, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawLine(dst, startPosX, startPosY, endPosX, endPosY, c); }
void Framework_ImageDrawLineV(Image* dst, Vector2 start, Vector2 end, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawLineV(dst, start, end, c); }
void Framework_ImageDrawLineEx(Image* dst, Vector2 start, Vector2 end, int thick, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawLineEx(dst, start, end, thick, c); }
void Framework_ImageDrawCircle(Image* dst, int centerX, int centerY, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawCircle(dst, centerX, centerY, radius, c); }
void Framework_ImageDrawCircleV(Image* dst, Vector2 center, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawCircleV(dst, center, radius, c); }
void Framework_ImageDrawCircleLines(Image* dst, int centerX, int centerY, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawCircleLines(dst, centerX, centerY, radius, c); }
void Framework_ImageDrawCircleLinesV(Image* dst, Vector2 center, int radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawCircleLinesV(dst, center, radius, c); }
void Framework_ImageDrawRectangle(Image* dst, int posX, int posY, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawRectangle(dst, posX, posY, width, height, c); }
void Framework_ImageDrawRectangleV(Image* dst, Vector2 position, Vector2 size, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawRectangleV(dst, position, size, c); }
void Framework_ImageDrawRectangleRec(Image* dst, Rectangle rec, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawRectangleRec(dst, rec, c); }
void Framework_ImageDrawRectangleLines(Image* dst, Rectangle rec, int thick, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawRectangleLines(dst, rec, thick, c); }
void Framework_ImageDrawTriangle(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawTriangle(dst, v1, v2, v3, c); }
void Framework_ImageDrawTriangleEx(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, unsigned char c1R, unsigned char c1G, unsigned char c1B, unsigned char c1A, unsigned char c2R, unsigned char c2G, unsigned char c2B, unsigned char c2A, unsigned char c3R, unsigned char c3G, unsigned char c3B, unsigned char c3A) { Color c1 = { c1R, c1G, c1B, c1A }; Color c2 = { c2R, c2G, c2B, c2A }; Color c3 = { c3R, c3G, c3B, c3A }; ImageDrawTriangleEx(dst, v1, v2, v3, c1, c2, c3); }
void Framework_ImageDrawTriangleLines(Image* dst, Vector2 v1, Vector2 v2, Vector2 v3, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawTriangleLines(dst, v1, v2, v3, c); }
void Framework_ImageDrawTriangleFan(Image* dst, Vector2* points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawTriangleFan(dst, points, pointCount, c); }
void Framework_ImageDrawTriangleStrip(Image* dst, Vector2* points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawTriangleStrip(dst, points, pointCount, c); }
void Framework_ImageDraw(Image* dst, Image src, Rectangle srcRec, Rectangle dstRec, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color tint = { r, g, b, a }; ImageDraw(dst, src, srcRec, dstRec, tint); }
```

- [ ] **Step 3:** `Select-String framework.h -Pattern "__declspec\(dllexport\)"` count == baseline + 20 (no build yet).
- [ ] **Step 4: Commit.** `git add framework.h framework.cpp; git commit -m "feat(engine): raylib image software drawing Batch 3c-ii (20 exports, ByRef Image dst)"`

---

## Task 3: 20 wrapper imports (parity GREEN)

**Files:** Modify `RaylibWrapper/RaylibWrapper.vb`

- [ ] **Step 1: Add the 20 DllImport Subs** in a new region after the Batch 3c-i region. All `Sub` (void), `ByRef dst As Image`. **⚠ raylib's `end` → `endPos`** (VB keyword). `ImageDraw` `src As Image` by value; `TriangleFan/Strip` `points As Vector2()`.

```vbnet
#Region "Raylib Image software drawing (Batch 3c-ii)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageClearBackground(ByRef dst As Image, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawPixel(ByRef dst As Image, posX As Integer, posY As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawPixelV(ByRef dst As Image, position As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawLine(ByRef dst As Image, startPosX As Integer, startPosY As Integer, endPosX As Integer, endPosY As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawLineV(ByRef dst As Image, start As Vector2, endPos As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawLineEx(ByRef dst As Image, start As Vector2, endPos As Vector2, thick As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawCircle(ByRef dst As Image, centerX As Integer, centerY As Integer, radius As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawCircleV(ByRef dst As Image, center As Vector2, radius As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawCircleLines(ByRef dst As Image, centerX As Integer, centerY As Integer, radius As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawCircleLinesV(ByRef dst As Image, center As Vector2, radius As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawRectangle(ByRef dst As Image, posX As Integer, posY As Integer, width As Integer, height As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawRectangleV(ByRef dst As Image, position As Vector2, size As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawRectangleRec(ByRef dst As Image, rec As Rectangle, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawRectangleLines(ByRef dst As Image, rec As Rectangle, thick As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawTriangle(ByRef dst As Image, v1 As Vector2, v2 As Vector2, v3 As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawTriangleEx(ByRef dst As Image, v1 As Vector2, v2 As Vector2, v3 As Vector2, c1R As Byte, c1G As Byte, c1B As Byte, c1A As Byte, c2R As Byte, c2G As Byte, c2B As Byte, c2A As Byte, c3R As Byte, c3G As Byte, c3B As Byte, c3A As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawTriangleLines(ByRef dst As Image, v1 As Vector2, v2 As Vector2, v3 As Vector2, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawTriangleFan(ByRef dst As Image, points As Vector2(), pointCount As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDrawTriangleStrip(ByRef dst As Image, points As Vector2(), pointCount As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Sub Framework_ImageDraw(ByRef dst As Image, src As Image, srcRec As Rectangle, dstRec As Rectangle, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
#End Region
```

- [ ] **Step 2: Run the parity guard — verify it PASSES** (pure text scan; also compiles the 20 VB DllImports). `dotnet test … --filter "FullyQualifiedName~RaylibImageDrawParityTests"` → PASS. Import count == baseline + 20.
- [ ] **Step 3: Commit.** `git add RaylibWrapper/RaylibWrapper.vb; git commit -m "feat(wrapper): raylib image software drawing Batch 3c-ii (20 DllImports, ByRef Image dst)"`

---

## Task 4: Rebuild native engine

**Files:** none (build only)

- [ ] **Step 1:**
```powershell
$msb = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msb "VisualGameStudioEngine.sln" -t:restore -p:RestorePackagesConfig=true -v:minimal
& $msb "VisualGameStudioEngine\VisualGameStudioEngine.vcxproj" -p:Configuration=Release -p:Platform=x64 "-p:SolutionDir=C:\Users\melvi\source\repos\VisualGameStudioEngine\" -v:minimal
```
Expect `BUILD EXIT: 0`. Pre-existing C4190 warnings benign. Any NEW error → fix in Task 2's files, rebuild.

---

## Task 5: Correctness test (GL-free, GREEN with Skipped:0)

**Files:** Create `VisualGameStudio.Tests/Native/RaylibImageDrawTests.cs`

- [ ] **Step 1: Stage the fresh DLL into `IDE\` FIRST:**
```powershell
Copy-Item "x64\Release\VisualGameStudioEngine.dll" "IDE\VisualGameStudioEngine.dll" -Force
Copy-Item "x64\Release\VisualGameStudioEngine.lib" "IDE\VisualGameStudioEngine.lib" -Force
```

- [ ] **Step 2: Write the correctness test** — model on `VisualGameStudio.Tests/Native/RaylibImageMutatorTests.cs`. Same `[Category("Integration")]`, local `[DllImport("VisualGameStudioEngine.dll")]`, `Guard<T>` self-skip. **Re-declare locally in this new file** the reused 3b helpers (`Framework_GenImageColor`, `Framework_GetImageColor`, `Framework_LoadImageColors`, `Framework_UnloadImage`) and the struct mirrors `RImage`/`RColor`/`RRect`, **plus a new `RVector2 { float x, y }`** mirror. Declare the 20 draw fns locally with `ref RImage dst`, `RVector2` by value, `RRect` by value; `ImageDraw` as `(ref RImage dst, RImage src, RRect srcRec, RRect dstRec, byte r,g,b,a)`; `TriangleFan/Strip` as `(ref RImage dst, RVector2[] points, int pointCount, byte r,g,b,a)`.

Assertions (spec §5.2 — one `[Test]` each or grouped). Draws don't realloc, so the proof is **pixel state** (read via `Framework_GetImageColor(dst, x, y)`):
  - `ImageClearBackground(black 4×4, red)` → all 16 pixels == `(255,0,0,255)`.
  - `ImageDrawPixel(dst, 1,1, blue)` on black → `(1,1)` blue, `(0,0)` still black.
  - `ImageDrawPixelV(dst, {2,2}, green)` → `(2,2)` green (proves `Vector2`-by-value).
  - `ImageDrawRectangle(dst, 0,0, 2,2, white)` on black 4×4 → `(0,0)`,`(1,1)` white, `(3,3)` black.
  - `ImageDrawRectangleRec(dst, {1,1,2,2}, white)` → `(1,1)` white, `(0,0)` untouched (proves `Rectangle`-by-value).
  - `ImageDrawLine(dst, 0,0, 3,0, X)` on black 4×4 → `(0,0)` and `(3,0)` set to X.
  - `ImageDrawCircle(dst, 4,4, 3, X)` on black 8×8 → `(4,4)` set to X.
  - `ImageDrawTriangle(dst, {1,1},{6,1},{3,5}, X)` on 8×8 → an interior pixel (e.g. `(3,2)`) set.
  - `ImageDrawTriangleEx(dst, v1,v2,v3, red,green,blue)` on 8×8 → an interior pixel is non-background (a≠0).
  - `ImageDrawTriangleFan(dst, RVector2[]{...4 pts...}, 4, X)` and `ImageDrawTriangleStrip(dst, same, 4, X)` on 8×8 → array marshals; an interior pixel set. (Use a convex quad, e.g. `{1,1},{6,1},{6,6},{1,6}`.)
  - **`ImageDraw`**: `src` = solid red 2×2 (`GenImageColor(2,2,255,0,0,255)`), `dst` = black 4×4, `srcRec={0,0,2,2}`, `dstRec={0,0,2,2}`, tint white → `dst(0,0)`,`dst(1,1)` == red, `dst(3,3)` black. Proves the **by-value `Image` src** blit.
  - Run + one-pixel check for `DrawLineV`, `DrawLineEx`, `DrawCircleV`, `DrawCircleLines`, `DrawCircleLinesV`, `DrawRectangleV`, `DrawRectangleLines`, `DrawTriangleLines`.
  - Free every image (dst + `ImageDraw` src) via `Framework_UnloadImage`, ideally in `finally`.

- [ ] **Step 3: Run — GREEN with Skipped:0.** `dotnet test … --filter "FullyQualifiedName~RaylibImageDrawTests"` → `Passed: N, Skipped: 0`. If Skipped>0 → re-stage (Step 1), re-run.
- [ ] **Step 4: Commit.** `git add …RaylibImageDrawTests.cs; git commit -m "test(raylib): image-draw Batch 3c-ii correctness (GL-free, pixel assertions + by-value src)"`

---

## Task 6: IDE refresh + Definition-of-Done + finish

- [ ] **Step 1: Rebuild the VB wrapper.** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → EXIT 0.
- [ ] **Step 2: Refresh all three IDE artifacts:**
```powershell
robocopy "x64\Release" "IDE" VisualGameStudioEngine.dll VisualGameStudioEngine.lib /R:1 /W:1
robocopy "RaylibWrapper\bin\Release\net8.0" "IDE" RaylibWrapper.dll /R:1 /W:1
```
- [ ] **Step 3: DoD greps:**
```powershell
(Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "__declspec\(dllexport\)").Count   # == 2623
(Select-String -Path "RaylibWrapper\RaylibWrapper.vb" -Pattern "<DllImport\(").Count                  # == 2555
(Select-String -Path "RaylibWrapper\RaylibWrapper.vb" -Pattern "Framework_ImageDrawText").Count        # == 0 (deferred not bound)
```
- [ ] **Step 4: Full parity + correctness + fast subset:**
```
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibImageDraw"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```
Expect draw parity + correctness GREEN (Skipped:0); fast subset ~3312✓, 0 failures.
- [ ] **Step 5: Commit IDE refresh** (`chore: refresh prebuilt IDE binaries … Batch 3c-ii`), then invoke superpowers:finishing-a-development-branch. On "merge it" → ff-merge to master + push + delete branch. Update memory (`raylib-parity-textures-batch3c-i.md` roadmap → 3c-ii done; new/updated ledger; MEMORY.md pointer). **3c COMPLETE after this** → next is 3d Texture GPU (4, needs window) + the deferred font fns.

---

## Notes
- **DRY/YAGNI:** pure passthrough, no new structs, no helper layer. No null/empty-array guard on TriangleFan/Strip (faithful passthrough — raylib owns bounds).
- **TDD:** parity RED (Task 1) → GREEN (Task 3); correctness proven against the freshly built DLL (Task 5).
- **The correctness-critical details:** `ImageDraw` `src` **by value** (never `&src`); `ByRef dst` everywhere; `Vector2()` bare array for the two fans/strips; `ImageDrawTriangleEx`'s 12 color bytes in `c1/c2/c3` order.
