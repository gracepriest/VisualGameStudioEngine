# raylib Textures Batch 3d (Texture GPU + font-image) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 9 raylib 5.5 functions — 4 Texture GPU round-trips + 4 font→image fns + `GenImageText` — as C-ABI engine exports + VB.NET P/Invoke bindings, with a windowed `TestVbDLL --textures3d` smoke scene for the 8 GL-dependent fns and a headless NUnit test for `GenImageText`.

**Architecture:** Thin passthrough. By-value `Texture2D`/`TextureCubemap`/`Image` (proven 3a/3b); `Font` by value; `Color` params decomposed to bytes and reassembled; `ImageDrawText`/`ImageDrawTextEx` take `ByRef dst As Image` (the shipped 3c primitive); strings `CharSet.Ansi`. No new structs.

**Tech Stack:** C++20 (MSVC 14.44, VS 2022 Enterprise MSBuild), raylib 5.5.0 (static), VB.NET (net8.0), NUnit, TestVbDLL smoke harness.

**Spec:** `docs/superpowers/specs/2026-07-25-raylib-parity-textures-batch3d-design.md`

---

## File Structure

| File | Change |
|---|---|
| `VisualGameStudioEngine/framework.h` | +9 `__declspec(dllexport)` decls, new `Batch 3d` banner after the 3c-ii block |
| `VisualGameStudioEngine/framework.cpp` | +9 forwarders, same order |
| `RaylibWrapper/RaylibWrapper.vb` | +9 `<DllImport>` (6 `Function`, 2 `Sub`, +GenImageText `Function`) in a new `#Region` |
| `VisualGameStudio.Tests/Native/RaylibTexture3dParityTests.cs` | **new** — parity guard (9 names) |
| `VisualGameStudio.Tests/Native/RaylibGenImageTextTests.cs` | **new** — headless correctness for the one non-window fn |
| `TestVbDLL/SampleTextures3d.vb` | **new** — windowed smoke scene for the 8 GL fns |
| `TestVbDLL/Program.vb` | +`--textures3d` dispatch guard |
| `IDE/VisualGameStudioEngine.dll`, `.lib`, `IDE/RaylibWrapper.dll` | refreshed at finish |

**Conventions:** struct returns by value; `Font` by value; `Color`→`u8 r,g,b,a` (forwarder rebuilds `Color{r,g,b,a}`); `ImageDrawText*` → `ByRef dst As Image`; strings `CharSet:=CharSet.Ansi`; `layout As Integer` (no CubemapLayout enum). NEVER bind `UpdateTexture`/`LoadRenderTexture`/`ExportImageToMemory` (out of scope).

---

## Task 0: Baseline

- [ ] **Step 1:** `git branch --show-current` → `raylib-textures-3d-gpu-fonts`; tree clean (spec committed).
- [ ] **Step 2:** record baseline counts (PowerShell): `(Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "__declspec\(dllexport\)").Count` → **2623**; `(Select-String -Path "RaylibWrapper\RaylibWrapper.vb" -Pattern "<DllImport\(").Count` → **2555**. If not 2623/2555, STOP and reconcile. Target after: 2632 / 2564.
- [ ] **Step 3:** confirm the 9 targets absent — `Select-String -Path "VisualGameStudioEngine\framework.h" -Pattern "Framework_LoadTextureFromImage|Framework_LoadTextureCubemap|Framework_LoadImageFromTexture|Framework_LoadImageFromScreen|Framework_ImageText\(|Framework_ImageTextEx|Framework_ImageDrawText|Framework_GenImageText"` → 0 hits.

---

## Task 1: Parity guard test (RED)

**Files:** Create `VisualGameStudio.Tests/Native/RaylibTexture3dParityTests.cs` (model on `RaylibImageDrawParityTests.cs`):

```csharp
using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3d (Texture GPU round-trips + font-image).
/// Pure text scan — no engine load. Trailing '(' anchors near-name pairs (Framework_ImageText( !=
/// Framework_ImageTextEx(; Framework_ImageDrawText( != Framework_ImageDrawTextEx(;
/// Framework_LoadImageFromTexture( != Framework_LoadImageFromScreen().
/// </summary>
[TestFixture]
public class RaylibTexture3dParityTests
{
    private static readonly string[] Batch3d =
    {
        "LoadTextureFromImage", "LoadTextureCubemap", "LoadImageFromTexture", "LoadImageFromScreen",
        "ImageText", "ImageTextEx", "ImageDrawText", "ImageDrawTextEx", "GenImageText",
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
    public void Every_batch3d_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3d)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3d, Has.Length.EqualTo(9));
        });
    }
}
```

- [ ] Run → FAIL (all 9 absent). Commit: `test(raylib): texture-3d Batch 3d parity guard (RED)`.

---

## Task 2: 9 engine exports

**Files:** Modify `framework.h`, `framework.cpp`.

- [ ] **Step 1: decls to framework.h** — after the `Batch 3c-ii` block (grep `Batch 3c-ii`), inside `extern "C"`:

```cpp
// ==== TEXTURE GPU ROUND-TRIPS + FONT-IMAGE (raylib 5.5 passthrough — Batch 3d) ====
__declspec(dllexport) Texture2D Framework_LoadTextureFromImage(Image image);
__declspec(dllexport) TextureCubemap Framework_LoadTextureCubemap(Image image, int layout);
__declspec(dllexport) Image Framework_LoadImageFromTexture(Texture2D texture);
__declspec(dllexport) Image Framework_LoadImageFromScreen(void);
__declspec(dllexport) Image Framework_ImageText(const char* text, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) Image Framework_ImageTextEx(Font font, const char* text, float fontSize, float spacing, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawText(Image* dst, const char* text, int posX, int posY, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) void Framework_ImageDrawTextEx(Image* dst, Font font, const char* text, Vector2 position, float fontSize, float spacing, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) Image Framework_GenImageText(int width, int height, const char* text);
```

- [ ] **Step 2: forwarders to framework.cpp** — after the `Batch 3c-ii` forwarders, same order:

```cpp
// ==== TEXTURE GPU ROUND-TRIPS + FONT-IMAGE (raylib 5.5 passthrough — Batch 3d) ====
Texture2D Framework_LoadTextureFromImage(Image image) { return LoadTextureFromImage(image); }
TextureCubemap Framework_LoadTextureCubemap(Image image, int layout) { return LoadTextureCubemap(image, layout); }
Image Framework_LoadImageFromTexture(Texture2D texture) { return LoadImageFromTexture(texture); }
Image Framework_LoadImageFromScreen(void) { return LoadImageFromScreen(); }
Image Framework_ImageText(const char* text, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; return ImageText(text, fontSize, c); }
Image Framework_ImageTextEx(Font font, const char* text, float fontSize, float spacing, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; return ImageTextEx(font, text, fontSize, spacing, c); }
void Framework_ImageDrawText(Image* dst, const char* text, int posX, int posY, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawText(dst, text, posX, posY, fontSize, c); }
void Framework_ImageDrawTextEx(Image* dst, Font font, const char* text, Vector2 position, float fontSize, float spacing, unsigned char r, unsigned char g, unsigned char b, unsigned char a) { Color c = { r, g, b, a }; ImageDrawTextEx(dst, font, text, position, fontSize, spacing, c); }
Image Framework_GenImageText(int width, int height, const char* text) { return GenImageText(width, height, text); }
```

- [ ] **Step 3:** header count == baseline + 9 (no build yet). Commit: `feat(engine): raylib texture GPU + font-image Batch 3d (9 exports)`.

---

## Task 3: 9 wrapper imports (parity GREEN)

**Files:** Modify `RaylibWrapper/RaylibWrapper.vb`.

- [ ] **Step 1:** add a new region after the Batch 3c-ii region. 6 `Function` (Image/Texture returns), 2 `Sub` (`ImageDrawText*`, `ByRef dst`), +GenImageText `Function`. Strings → `CharSet:=CharSet.Ansi`.

```vbnet
#Region "Raylib Texture GPU + font-image (Batch 3d)"
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LoadTextureFromImage(image As Image) As Texture2D
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LoadTextureCubemap(image As Image, layout As Integer) As TextureCubemap
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LoadImageFromTexture(texture As Texture2D) As Image
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
    Public Function Framework_LoadImageFromScreen() As Image
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_ImageText(text As String, fontSize As Integer, r As Byte, g As Byte, b As Byte, a As Byte) As Image
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_ImageTextEx(font As Font, text As String, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte) As Image
    End Function
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_ImageDrawText(ByRef dst As Image, text As String, posX As Integer, posY As Integer, fontSize As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Sub Framework_ImageDrawTextEx(ByRef dst As Image, font As Font, text As String, position As Vector2, fontSize As Single, spacing As Single, r As Byte, g As Byte, b As Byte, a As Byte)
    End Sub
    <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
    Public Function Framework_GenImageText(width As Integer, height As Integer, text As String) As Image
    End Function
#End Region
```

- [ ] **Step 2:** run parity guard → PASS (text scan; also compiles the 9 VB bindings). Import count == baseline + 9. Commit: `feat(wrapper): raylib texture GPU + font-image Batch 3d (9 DllImports)`.

---

## Task 4: Rebuild native engine

- [ ] Restore + build the vcxproj (shapes-batch1 incantation):
```powershell
$msb = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msb "VisualGameStudioEngine.sln" -t:restore -p:RestorePackagesConfig=true -v:minimal
& $msb "VisualGameStudioEngine\VisualGameStudioEngine.vcxproj" -p:Configuration=Release -p:Platform=x64 "-p:SolutionDir=C:\Users\melvi\source\repos\VisualGameStudioEngine\" -v:minimal
```
Expect `BUILD EXIT: 0` (9 fns compiled; benign C4190 warnings). New error → fix Task 2 files, rebuild.

---

## Task 5: GenImageText headless correctness (GREEN, Skipped:0)

**Files:** Create `VisualGameStudio.Tests/Native/RaylibGenImageTextTests.cs`.

- [ ] **Step 1: stage fresh DLL FIRST:** `Copy-Item "x64\Release\VisualGameStudioEngine.dll" "IDE\VisualGameStudioEngine.dll" -Force` (+`.lib`).
- [ ] **Step 2: write the test** — model on `RaylibImageMutatorTests.cs` (`[Category("Integration")]`, local `[DllImport("VisualGameStudioEngine.dll")]`, `RImage` mirror, `Guard<T>` self-skip). Only `GenImageText` is exercised here (the other 8 need a window → smoke scene, Task 6). Assertions:
  - `GenImageText(16, 16, "AB")` → `img.width == 16`, `img.height == 16`, `img.mipmaps >= 1`, `img.data != IntPtr.Zero`, `img.format == 1` (PIXELFORMAT_UNCOMPRESSED_GRAYSCALE).
  - `Framework_GetPixelDataSize(16, 16, 1)` == 256 (sanity, if convenient — GetPixelDataSize shipped in 3a).
  - Free with `Framework_UnloadImage`.
- [ ] **Step 3: run** `dotnet test … --filter "FullyQualifiedName~RaylibGenImageTextTests"` → `Passed: N, Skipped: 0`. Skipped>0 → re-stage, re-run. Commit: `test(raylib): GenImageText Batch 3d headless correctness`.

---

## Task 6: TestVbDLL `--textures3d` smoke scene (the 8 GL fns)

**Files:** Create `TestVbDLL/SampleTextures3d.vb`; modify `TestVbDLL/Program.vb`.

⚠ **The headless NUnit suite CANNOT cover these 8 (no GL context in the test host).** This scene is the verification; the visual run is the USER's checkpoint (Task 7).

- [ ] **Step 1: study the harness** — Read `TestVbDLL/Program.vb` (the `If args(0) = "--shapes"/"--text" Then … .Run() : Return` dispatch) and an existing scene (`TestVbDLL/SampleTextBatch2.vb` or `SampleShapesBatch1.vb`) for the exact window/game-loop API (`Framework_Initialize`/`SetDrawCallback`/`ShouldClose`/`BeginDrawing`/`EndDrawing`/`DrawTexture`/`UnloadTexture`/`UnloadImage`, and how a `Font` is obtained — `Framework_GetFontDefault`/`LoadFontEx` if present). Model the new scene on them.
- [ ] **Step 2: write `SampleTextures3d.vb`** implementing spec §5.3, all GL work after window init:
  1. Init window; source `Image` via `Framework_GenImageColor`/`GenImageChecked` (asset-free).
  2. `tex = Framework_LoadTextureFromImage(img)` → assert `tex.id <> 0` and `tex.width/height == img.width/height`.
  3. Draw the texture each frame (`Framework_DrawTexture`/`DrawTexturePro`).
  4. `img2 = Framework_LoadImageFromTexture(tex)` → assert `img2.width == tex.width`, `img2.height == tex.height`, `img2.data <> IntPtr.Zero`.
  5. `ImageText("Hi", 20, RED)` → Image; if a Font is obtainable, `ImageTextEx(font, "Hi", 20, 1, WHITE)` → Image; a blank Image + `ImageDrawText(ByRef img3, "Hi", 2, 2, 10, WHITE)`; if Font available `ImageDrawTextEx(ByRef img3, font, …)`. Assert each result Image has `data <> IntPtr.Zero` + expected dims. Upload one to a texture and draw it for the visual check.
  6. `LoadTextureCubemap(synthLayoutImg, 0)` — call, log `cubemap.id`, do NOT hard-fail on id==0 (bind-only, no cubemap asset).
  7. After a drawn frame: `shot = Framework_LoadImageFromScreen()` → assert `data <> IntPtr.Zero` + dims == screen; `Framework_ExportImage(shot, "textures3d_capture.png")` (3b) for eyeballing.
  8. Print a `PASS/FAIL` summary line for the mechanical asserts; bounded auto-close loop (e.g. `While Not Framework_ShouldClose() AndAlso frames < 600`); unload every texture + image before shutdown.
- [ ] **Step 3: wire dispatch** — add to `Program.vb`: `If args.Length > 0 AndAlso args(0) = "--textures3d" Then Call New SampleTextures3d().Run() : Return` (match the existing guard style).
- [ ] **Step 4: build TestVbDLL** (⚠ engine already rebuilt in Task 4; TestVbDLL copies `x64\Release\VisualGameStudioEngine.dll`):
```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" "TestVbDLL\TestVbDLL.vbproj" -p:Configuration=Release -p:Platform=x64 "-p:SolutionDir=C:\Users\melvi\source\repos\VisualGameStudioEngine\" -v:minimal
```
Expect `BUILD EXIT: 0` — this **compile-checks all 9 VB bindings are callable + the scene is valid VB**. Exe → `TestVbDLL\bin\x64\Release\net8.0\TestVbDLL.exe`. (Do NOT run the GUI here — the visual run is the user's Task 7 checkpoint. If the build environment supports it and the scene auto-closes, an optional headless-ish run to capture the PASS/FAIL line is a bonus, not required.)
- [ ] **Step 5: commit** — `feat(smoke): TestVbDLL --textures3d scene for raylib texture GPU + font-image`.

---

## Task 7: IDE refresh + Definition-of-Done + finish

- [ ] **Step 1:** rebuild the VB wrapper: `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → EXIT 0.
- [ ] **Step 2:** refresh IDE artifacts:
```powershell
robocopy "x64\Release" "IDE" VisualGameStudioEngine.dll VisualGameStudioEngine.lib /R:1 /W:1
robocopy "RaylibWrapper\bin\Release\net8.0" "IDE" RaylibWrapper.dll /R:1 /W:1
```
- [ ] **Step 3: DoD greps:** exports == 2632, imports == 2564; `Select-String RaylibWrapper.vb -Pattern "Framework_UpdateTexture\("` unchanged (not our concern), `Framework_LoadRenderTexture` == 0 (out of scope, not bound).
- [ ] **Step 4:** `dotnet test … --filter "FullyQualifiedName~RaylibTexture3d"` (parity) + `…~RaylibGenImageText` (correctness) GREEN Skipped:0; fast subset `--filter "TestCategory!=Integration"` ~3313✓ 0 failures.
- [ ] **Step 5:** commit IDE refresh (`chore: refresh prebuilt IDE binaries … Batch 3d`), then invoke superpowers:finishing-a-development-branch. **⚠ USER VISUAL CHECKPOINT:** before/at finish, the user runs `TestVbDLL\bin\x64\Release\net8.0\TestVbDLL.exe --textures3d` — confirm the texture + text-images render, the PASS/FAIL line is PASS, and `textures3d_capture.png` is a faithful readback. On "merge it" → ff-merge to master + push + delete branch. Update memory (new 3d ledger; 3c-ii/roadmap → textures COMPLETE except ExportImageToMemory; MEMORY.md pointer). **This closes the textures module** (bar ExportImageToMemory → file-I/O batch).

---

## Notes
- **DRY/YAGNI:** pure passthrough, no new structs, no CubemapLayout enum, no null guards.
- **TDD:** parity RED (Task 1) → GREEN (Task 3); GenImageText proven headless (Task 5); the 8 GL fns proven via the smoke scene's self-asserts + the user's visual run (Task 6/7).
- **Correctness-critical:** by-value struct returns (`Texture2D`/`Image`); `Font` by value; `ImageDrawText*` `ByRef dst`; `LoadImageFromScreen` only after a drawn frame; Color reassembly order.
