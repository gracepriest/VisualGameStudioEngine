# raylib 5.5 Parity — Shapes Batch 1 Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the 37 raylib 5.5.0 `shapes`-module functions that are currently missing through the engine's C-ABI (`framework.h`/`framework.cpp`) and the VB.NET P/Invoke wrapper (`RaylibWrapper.vb`), validating the whole engine⇄wrapper pipeline end-to-end at the lowest-risk batch.

**Architecture:** Purely additive declaration work. Each raylib function gets one `__declspec(dllexport) Framework_<name>` thin forwarder inside the engine's single `extern "C"` block and one matching `<DllImport>` in the wrapper's `FrameworkWrapper` module — name-for-name (no `EntryPoint:=` remap), honoring the 1:1 sync invariant. No existing export/import is renamed or changed; hand-rolled siblings (`Framework_DrawSpline`, `Framework_DrawGradientRectV`, …) are left untouched.

**Tech Stack:** C++17 (engine, `PlatformToolset=v143`, raylib 5.5.0 static-linked via packages.config), VB.NET `net8.0` (wrapper), NUnit (parity + math tests), MSBuild via vswhere (native), `dotnet` (wrapper/tests).

---

## Key decisions (incl. divergences from the approved spec `2026-07-24-…-design.md`)

These are grounded in the actual codebase conventions (2467 existing exports / 2399 imports), which the spec did not sample. Following established patterns per writing-plans.

1. **⛔ Color is DECOMPOSED to `unsigned char r,g,b,a`, NOT passed by value.** *(Diverges from spec §2.3, which listed Color as a by-value struct.)* The engine has **zero** `Color`-by-value exports and the wrapper has **zero** `As Color` params — every existing site decomposes Color to four `unsigned char` / `As Byte`. All 37 functions that take a `Color` follow this: e.g. raylib `DrawRectangleRec(Rectangle, Color)` → engine `Framework_DrawRectangleRec(Rectangle rec, unsigned char r, unsigned char g, unsigned char b, unsigned char a)` → wrapper `Framework_DrawRectangleRec(rec As Rectangle, r As Byte, g As Byte, b As Byte, a As Byte)`. `DrawRectangleGradientEx` (4 colors) → 16 byte params, in raylib's TL, BL, TR, BR order.

2. **Vector2 by value and Vector2 return by value ARE used** (precedents: `Framework_CheckCollisionRecs(Rectangle,Rectangle)`, `Framework_GetMousePosition() -> Vector2`). Group A/C/D keep Vector2/Rectangle/Texture2D by value exactly as raylib declares them.

3. **Array params: bare managed array, NO `<MarshalAs(UnmanagedType.LPArray)>`.** *(Diverges from spec §2.3's explicit-attribute recommendation.)* The one existing precedent — `Framework_CheckCollisionPointPoly(point As Vector2, points As Vector2(), pointCount As Integer)` — omits the attribute; default P/Invoke marshals a blittable-struct array to `LPArray` identically. Match the proven prior art. Engine side takes `const Vector2 *points, int pointCount` (precedent: same function).

4. **Return-by-value for Group C** (`Vector2 Framework_GetSplinePoint*`), not the engine's alternative `float* outX,outY` style — matches raylib and the `GetMousePosition` return precedent, and makes the wrapper binding a clean `Function … As Vector2`.

5. **C++17, not C++20** — engine is `LanguageStandard=stdcpp17`. All forwarders are brace-init + passthrough (C++17-safe). *(Spec §2 said `-std=c++20`; irrelevant to these trivial bodies but do not rely on C++20.)*

6. **Insertion sites (keep .h and .cpp in the same order):** add one banner-commented block `// ==== SHAPES (raylib 5.5 passthrough — Batch 1) ====`.
   - `framework.h`: immediately before the `// TEXT MEASUREMENT` banner at **framework.h:4267** (end of the `// ADDITIONAL SHAPE DRAWING` section, `:4253-4265`), inside the single `extern "C"` block (`:275-4386`).
   - `framework.cpp`: immediately before the `// TEXT MEASUREMENT` section at **framework.cpp:28117** (end of `// ADDITIONAL SHAPE DRAWING` forwarders `:28073-28115`), inside the main `extern "C"` span (`:923-28289`).
   - `RaylibWrapper.vb`: a new `#Region "Raylib Shapes (Batch 1)"` inside `Public Module FrameworkWrapper` (`:9`), before `End Module` (`:11163`); e.g. right after `#Region "Additional Shape Drawing"` (`:10703`).

7. **Test strategy** (honors spec §4.4/§4.5; keeps the 2400-test NUnit suite decoupled from the native engine):
   - **Parity check — automated NUnit, no engine load:** a text-scan test asserting each of the 37 `Framework_<name>` exports in `framework.h` has a matching `<DllImport>` binding of the same name in `RaylibWrapper.vb`. Runs in the normal (and fast) suite. Guards the sync invariant (spec §4.4 step 3).
   - **Group C math — automated NUnit `[Category("Integration")]`, self-contained local `[DllImport("VisualGameStudioEngine.dll")]`:** calls the 5 `Framework_GetSplinePoint*` through a P/Invoke declared *in the test itself* (NO `RaylibWrapper` project reference — keeps coupling out of the suite), asserting known values. `Assert.Ignore` on `DllNotFoundException`/`EntryPointNotFoundException`. The engine DLL is staged next to the test binary via a csproj copy from `IDE\` (refreshed in Task 8 before this runs). Integration-tagged ⇒ excluded from the fast subset.
   - **Groups A/B/D drawing — VB.NET smoke program (TestVbDLL):** a scene opening a window and calling `DrawRectangleRec`, `DrawRectanglePro`, `DrawLineStrip` (array path), `DrawRectangleGradientEx`, plus the int/float coexistence check that the pre-existing `Framework_DrawRectangle` still resolves. Manual/visual (needs a GL context), per spec §4.4. Later batches may promote wrapper P/Invoke tests into a dedicated `RaylibWrapper.Tests` project — out of scope here (YAGNI).

8. **Build reality (from recon):** `packages\` is gitignored/absent, so a `nuget restore VisualGameStudioEngine.sln` is REQUIRED before any native build (the vcxproj hard-errors without `raylib.targets`). Build the **.sln target** (not the bare vcxproj) so `$(SolutionDir)` → repo root and output lands at repo-root `x64\Release\VisualGameStudioEngine.{dll,lib}` where consumers (`TestVbDLL`, `EngineDeployment.LocateImportLib`) expect it. The IDE refresh must copy **all three** artifacts — `VisualGameStudioEngine.dll`, `VisualGameStudioEngine.lib` (both currently months-stale in `IDE\`), and `RaylibWrapper.dll` — because the engine DLL changes when exports are added.

---

## File structure

| File | Change | Responsibility |
|---|---|---|
| `VisualGameStudioEngine/framework.h` | Modify (+37 export decls) | C-ABI export declarations, inside the `extern "C"` block before `:4267` |
| `VisualGameStudioEngine/framework.cpp` | Modify (+37 forwarders) | Thin forwarders reconstructing `Color{r,g,b,a}` and calling raw raylib, before `:28117` |
| `RaylibWrapper/RaylibWrapper.vb` | Modify (+37 `<DllImport>`) | 1:1 P/Invoke bindings in a new `#Region`, name-for-name |
| `VisualGameStudio.Tests/Native/RaylibShapesParityTests.cs` | Create | Automated export↔import parity scan (no engine) |
| `VisualGameStudio.Tests/Native/RaylibShapesSplineMathTests.cs` | Create | Group C math correctness via local `[DllImport]` (Integration, self-skipping) |
| `VisualGameStudio.Tests/VisualGameStudio.Tests.csproj` | Modify | Stage `IDE\VisualGameStudioEngine.dll` next to the test binary (condition Exists) |
| `TestVbDLL/SampleShapesBatch1.vb` | Create | VB.NET smoke scene for Groups A/B/D + int/float coexistence |
| `IDE/VisualGameStudioEngine.dll`, `IDE/VisualGameStudioEngine.lib`, `IDE/RaylibWrapper.dll` | Refresh | Ship the new exports/imports to the prebuilt IDE + game-app link |

**The complete 37-function mapping is in Appendix A** — every task references it. Column meaning: raylib signature → engine export → forwarder body → wrapper import.

---

## Task 0: Build baseline — prove the native+wrapper toolchain BEFORE writing code

**Rationale:** native builds are slow and the raylib restore is a known gate; confirm the pipeline is green first so a broken environment isn't discovered after 37 functions are written. (Standing lesson: subagent background commands die on turn-end — run these in the FOREGROUND.)

**Files:** none modified.

- [ ] **Step 1: Restore raylib** — `nuget restore VisualGameStudioEngine.sln` (populates `packages\raylib.5.5.0\…`). raylib here is wired via **packages.config**, NOT PackageReference, so a bare `msbuild -t:restore` is a NO-OP — if `nuget.exe` is unavailable use `msbuild VisualGameStudioEngine.sln -t:restore -p:RestorePackagesConfig=true`, or open the solution in VS once (auto-restore). Expected: `packages\raylib.5.5.0\build\native\raylib.targets` now exists.
- [ ] **Step 2: Locate MSBuild** via vswhere (mirror `EngineAgent/engine_agent.py::_find_msbuild`):
  `& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`
- [ ] **Step 3: Build the native engine (unmodified)** via the .sln target so output → repo-root `x64\Release\`:
  `& <msbuild> VisualGameStudioEngine.sln -t:VisualGameStudioEngine -p:Configuration=Release -p:Platform=x64 -verbosity:minimal`
  Expected: `x64\Release\VisualGameStudioEngine.dll` and `.lib` produced, 0 errors.
- [ ] **Step 4: Build the wrapper (unmodified)** — `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release`. Expected: `RaylibWrapper\bin\Release\net8.0\RaylibWrapper.dll`, 0 errors.
- [ ] **Step 5: Record baseline counts** (grep, don't trust cached): `__declspec(dllexport)` in framework.h = **2467**; `<DllImport` in RaylibWrapper.vb = **2399**. After this batch they must be **2504** and **2436**.
- [ ] **Step 6:** No commit (no changes). If any step fails, STOP and fix the environment before proceeding.

---

## Task 1: Automated parity test (RED first)

**Files:** Create `VisualGameStudio.Tests/Native/RaylibShapesParityTests.cs`

- [ ] **Step 1: Write the failing test.** Locate `framework.h` and `RaylibWrapper.vb` by walking up from `TestContext.CurrentContext.TestDirectory` (or `AppContext.BaseDirectory`) to the repo root (the dir containing `VisualGameStudioEngine.sln`). For each of the 37 names (Appendix A), assert `framework.h` contains a `Framework_<name>(` export AND `RaylibWrapper.vb` contains a `Framework_<name>(` binding. Use `Assert.Multiple` and report every missing side by name.

```csharp
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Enforces the engine⇄wrapper sync invariant for the raylib shapes Batch 1: every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; of the
/// same name in RaylibWrapper.vb (spec §4.4 step 3). Pure text scan — no engine load.
/// </summary>
[TestFixture]
public class RaylibShapesParityTests
{
    private static readonly string[] Batch1 =
    {
        // Group A (21)
        "DrawPixelV","DrawLineV","DrawLineEx","DrawLineBezier","DrawCircleGradient","DrawCircleV",
        "DrawCircleLinesV","DrawRectangleV","DrawRectangleRec","DrawRectanglePro","DrawRectangleGradientV",
        "DrawRectangleGradientH","DrawRectangleGradientEx","DrawRectangleLinesEx","DrawRectangleRoundedLinesEx",
        "DrawPolyLinesEx","DrawSplineSegmentLinear","DrawSplineSegmentBasis","DrawSplineSegmentCatmullRom",
        "DrawSplineSegmentBezierQuadratic","DrawSplineSegmentBezierCubic",
        // Group B (8)
        "DrawLineStrip","DrawTriangleFan","DrawTriangleStrip","DrawSplineLinear","DrawSplineBasis",
        "DrawSplineCatmullRom","DrawSplineBezierQuadratic","DrawSplineBezierCubic",
        // Group C (5)
        "GetSplinePointLinear","GetSplinePointBasis","GetSplinePointCatmullRom","GetSplinePointBezierQuad",
        "GetSplinePointBezierCubic",
        // Group D (3)
        "SetShapesTexture","GetShapesTexture","GetShapesTextureRectangle",
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
    public void Every_batch1_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch1)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch1, Has.Length.EqualTo(37));
        });
    }
}
```

- [ ] **Step 2: Run — expect RED.** `dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~RaylibShapesParityTests"` → FAIL, listing all 37 missing on both sides.
- [ ] **Step 3: Commit.** `test(raylib): parity guard for shapes Batch 1 (red)`.

---

## Task 2: Group A — 21 simple functions (engine + wrapper)

**Files:** Modify `framework.h` (before `:4267`), `framework.cpp` (before `:28117`), `RaylibWrapper.vb` (new `#Region`).

Add all 21 Group A rows from **Appendix A**. Color→`unsigned char r,g,b,a`; Vector2/Rectangle by value. Keep `.h` and `.cpp` in identical order under a `// ==== SHAPES (raylib 5.5 passthrough — Batch 1) : Group A ====` banner.

**Worked example (DrawRectangleRec):**
```cpp
// framework.h  (declaration)
__declspec(dllexport) void Framework_DrawRectangleRec(Rectangle rec, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
```
```cpp
// framework.cpp  (forwarder)
void Framework_DrawRectangleRec(Rectangle rec, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    DrawRectangleRec(rec, Color{ r, g, b, a });
}
```
```vbnet
' RaylibWrapper.vb  (binding)
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Sub Framework_DrawRectangleRec(rec As Rectangle, r As Byte, g As Byte, b As Byte, a As Byte)
End Sub
```

- [ ] **Step 1:** Add the 21 declarations to `framework.h`.
- [ ] **Step 2:** Add the 21 forwarders to `framework.cpp` (same order).
- [ ] **Step 3:** Add the 21 `<DllImport>` bindings to `RaylibWrapper.vb` (same names, `r,g,b,a As Byte` for colors).
- [ ] **Step 4:** Build the wrapper only (fast syntax check): `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors. (Native build deferred to Task 6 to avoid repeated slow builds.)
- [ ] **Step 5: Commit.** `feat(engine): raylib shapes Batch 1 Group A (21 simple draws)`.

---

## Task 3: Group B — 8 array functions (engine + wrapper)

**Files:** same three, under a `: Group B` sub-banner.

Add the 8 Group B rows from Appendix A. Engine: `const Vector2 *points, int pointCount` (+ float thick where present) + `unsigned char r,g,b,a`. Wrapper: `points As Vector2(), pointCount As Integer` (bare array, **no** LPArray attr) + colors.

**Worked example (DrawLineStrip):**
```cpp
// framework.h
__declspec(dllexport) void Framework_DrawLineStrip(const Vector2 *points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a);
// framework.cpp
void Framework_DrawLineStrip(const Vector2 *points, int pointCount, unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    DrawLineStrip(points, pointCount, Color{ r, g, b, a });
}
```
```vbnet
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Sub Framework_DrawLineStrip(points As Vector2(), pointCount As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
End Sub
```

- [ ] **Step 1–3:** Add the 8 declarations / forwarders / bindings (same order across files).
- [ ] **Step 4:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 5: Commit.** `feat(engine): raylib shapes Batch 1 Group B (8 Vector2[] draws)`.

---

## Task 4: Group C — 5 spline-point functions returning Vector2 (engine + wrapper)

**Files:** same three, under a `: Group C` sub-banner. No Color. Return `Vector2` by value.

**Worked example (GetSplinePointLinear):**
```cpp
// framework.h
__declspec(dllexport) Vector2 Framework_GetSplinePointLinear(Vector2 startPos, Vector2 endPos, float t);
// framework.cpp
Vector2 Framework_GetSplinePointLinear(Vector2 startPos, Vector2 endPos, float t) {
    return GetSplinePointLinear(startPos, endPos, t);
}
```
```vbnet
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GetSplinePointLinear(startPos As Vector2, endPos As Vector2, t As Single) As Vector2
End Function
```

- [ ] **Step 1–3:** Add the 5 declarations / forwarders / bindings (Appendix A Group C).
- [ ] **Step 4:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 5: Commit.** `feat(engine): raylib shapes Batch 1 Group C (5 spline-point evals)`.

---

## Task 5: Group D — 3 shapes-texture-state functions (engine + wrapper)

**Files:** same three, under a `: Group D` sub-banner. Texture2D/Rectangle by value; return Texture2D/Rectangle by value.

```cpp
// framework.h
__declspec(dllexport) void      Framework_SetShapesTexture(Texture2D texture, Rectangle source);
__declspec(dllexport) Texture2D Framework_GetShapesTexture();
__declspec(dllexport) Rectangle Framework_GetShapesTextureRectangle();
// framework.cpp
void      Framework_SetShapesTexture(Texture2D texture, Rectangle source) { SetShapesTexture(texture, source); }
Texture2D Framework_GetShapesTexture() { return GetShapesTexture(); }
Rectangle Framework_GetShapesTextureRectangle() { return GetShapesTextureRectangle(); }
```
```vbnet
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Sub Framework_SetShapesTexture(texture As Texture2D, source As Rectangle)
End Sub
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GetShapesTexture() As Texture2D
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GetShapesTextureRectangle() As Rectangle
End Function
```

- [ ] **Step 1–3:** Add the 3 declarations / forwarders / bindings.
- [ ] **Step 4:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 5: Commit.** `feat(engine): raylib shapes Batch 1 Group D (shapes-texture state)`.

---

## Task 6: Rebuild engine + wrapper, stage DLLs, parity test GREEN

**Files:** none (build/stage only).

- [ ] **Step 1: Rebuild the native engine** (FOREGROUND): `& <msbuild> VisualGameStudioEngine.sln -t:VisualGameStudioEngine -p:Configuration=Release -p:Platform=x64 -verbosity:minimal` → 0 errors, fresh `x64\Release\VisualGameStudioEngine.dll` (+`.lib`).
- [ ] **Step 2: Rebuild the wrapper**: `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 3: Confirm new export count** — `__declspec(dllexport)` in framework.h == **2504**; `<DllImport` in RaylibWrapper.vb == **2436**.
- [ ] **Step 4: Run parity test** → GREEN: `dotnet test … --filter "FullyQualifiedName~RaylibShapesParityTests"` → Passed.
- [ ] **Step 5:** No commit (build artifacts staged in Task 8).

---

## Task 7: Group C math correctness (automated, Integration, self-contained P/Invoke)

**Files:** Create `VisualGameStudio.Tests/Native/RaylibShapesSplineMathTests.cs`; Modify `VisualGameStudio.Tests/VisualGameStudio.Tests.csproj`.

**csproj change** — stage the engine DLL next to the test binary (skips cleanly if absent):
```xml
<ItemGroup Condition="Exists('$(MSBuildThisFileDirectory)..\IDE\VisualGameStudioEngine.dll')">
  <None Include="..\IDE\VisualGameStudioEngine.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>VisualGameStudioEngine.dll</Link>
  </None>
</ItemGroup>
```
> NOTE: This copies the **IDE\** engine DLL, which Task 8 refreshes with the new exports. Run this task's test AFTER Task 8, or temporarily copy `x64\Release\VisualGameStudioEngine.dll` next to the test binary. Sequencing: implement the test here, but its GREEN run is gated on the refreshed DLL (verify in Task 8/DoD).

- [ ] **Step 1: Write the test** with a local `[DllImport]` (no RaylibWrapper reference). Known values use endpoint-interpolation / partition-of-unity identities that are exact:

```csharp
using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Group C spline-point evaluators are pure math (no GL), so they are genuinely
/// unit-testable through P/Invoke. Declares its OWN [DllImport] so the main suite keeps
/// no RaylibWrapper coupling. Integration-tagged + self-skipping: needs the freshly built
/// VisualGameStudioEngine.dll staged next to the test binary (Task 8 refresh).
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibShapesSplineMathTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct V2 { public float x, y; public V2(float x, float y) { this.x = x; this.y = y; } }

    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointLinear(V2 a, V2 b, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointCatmullRom(V2 p1, V2 p2, V2 p3, V2 p4, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointBasis(V2 p1, V2 p2, V2 p3, V2 p4, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointBezierQuad(V2 p1, V2 c2, V2 p3, float t);
    [DllImport("VisualGameStudioEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern V2 Framework_GetSplinePointBezierCubic(V2 p1, V2 c2, V2 c3, V2 p4, float t);

    private static V2 Call(Func<V2> f)
    {
        try { return f(); }
        catch (DllNotFoundException)   { Assert.Ignore("VisualGameStudioEngine.dll not staged; rebuild engine + refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore("engine DLL predates Batch 1 exports; refresh IDE\\ first."); throw; }
    }

    private static void AssertClose(V2 got, float x, float y)
    {
        Assert.That(got.x, Is.EqualTo(x).Within(1e-3), "x");
        Assert.That(got.y, Is.EqualTo(y).Within(1e-3), "y");
    }

    [Test] public void Linear_midpoint()
        => AssertClose(Call(() => Framework_GetSplinePointLinear(new V2(0,0), new V2(10,0), 0.5f)), 5, 0);

    [Test] public void CatmullRom_interpolates_p2_at_t0_and_p3_at_t1()
    {
        var p1 = new V2(0,0); var p2 = new V2(1,2); var p3 = new V2(3,4); var p4 = new V2(5,6);
        AssertClose(Call(() => Framework_GetSplinePointCatmullRom(p1,p2,p3,p4,0f)), p2.x, p2.y);
        AssertClose(Call(() => Framework_GetSplinePointCatmullRom(p1,p2,p3,p4,1f)), p3.x, p3.y);
    }

    [Test] public void Basis_partition_of_unity_returns_the_point_when_all_equal()
        => AssertClose(Call(() => Framework_GetSplinePointBasis(new V2(2,3), new V2(2,3), new V2(2,3), new V2(2,3), 0.5f)), 2, 3);

    [Test] public void BezierQuad_hits_endpoints()
    {
        var p1 = new V2(0,0); var c2 = new V2(5,9); var p3 = new V2(10,0);
        AssertClose(Call(() => Framework_GetSplinePointBezierQuad(p1,c2,p3,0f)), p1.x, p1.y);
        AssertClose(Call(() => Framework_GetSplinePointBezierQuad(p1,c2,p3,1f)), p3.x, p3.y);
    }

    [Test] public void BezierCubic_hits_endpoints()
    {
        var p1 = new V2(0,0); var c2 = new V2(2,8); var c3 = new V2(8,8); var p4 = new V2(10,0);
        AssertClose(Call(() => Framework_GetSplinePointBezierCubic(p1,c2,c3,p4,0f)), p1.x, p1.y);
        AssertClose(Call(() => Framework_GetSplinePointBezierCubic(p1,c2,c3,p4,1f)), p4.x, p4.y);
    }
}
```
> Verify the `Framework_GetSplinePointBasis` partition-of-unity identity against raylib's actual implementation (raylib 5.5 basis point uses `(1/6)`-weighted sums; four equal control points sum to the point). If raylib's basis does not return the point for equal inputs, replace with a value computed directly from raylib's formula in `raylib.h`/`rshapes.c`.

- [ ] **Step 2: Add the csproj copy item** (above).
- [ ] **Step 3: Run (after Task 8's refresh, or with `x64\Release` DLL staged)** → GREEN: `dotnet test … --filter "FullyQualifiedName~RaylibShapesSplineMathTests"`. If the engine DLL isn't staged yet it self-`Ignore`s (not a failure).
- [ ] **Step 4: Commit.** `test(raylib): Group C spline-point math via P/Invoke (integration)`.

---

## Task 8: IDE refresh (all three artifacts) + smoke program

**Files:** Refresh `IDE\VisualGameStudioEngine.dll`, `IDE\VisualGameStudioEngine.lib`, `IDE\RaylibWrapper.dll`; Create `TestVbDLL/SampleShapesBatch1.vb`.

- [ ] **Step 1: Clear locks** — no IDE running; kill stray `dotnet … --lsp`/testhost (verify command line first); `dotnet build-server shutdown`.
- [ ] **Step 2: Copy the three fresh artifacts into `IDE\`** (`robocopy … /R:1 /W:1`, no `/MIR`):
  - `x64\Release\VisualGameStudioEngine.dll` → `IDE\`
  - `x64\Release\VisualGameStudioEngine.lib` → `IDE\`
  - `RaylibWrapper\bin\Release\net8.0\RaylibWrapper.dll` → `IDE\`
- [ ] **Step 3: Write the smoke scene** `TestVbDLL/SampleShapesBatch1.vb` — a minimal window + draw loop calling `Framework_DrawRectangleRec`, `Framework_DrawRectanglePro`, `Framework_DrawLineStrip` (build a `Vector2()` array), `Framework_DrawRectangleGradientEx` (16 bytes), and the pre-existing int-based `Framework_DrawRectangle` (int/float coexistence). Model window init / loop / `Framework_*` calls on `TestVbDLL/SampleA_FrameworkOnly.vb`. Exit after N frames or on key.
- [ ] **Step 4: Build + run the smoke program** against the fresh engine DLL: `dotnet build TestVbDLL/TestVbDLL.vbproj -c Release` then run its exe (it deploys `VisualGameStudioEngine.dll` from `x64\Release\` via its `CopyNativeDll` target). Visually confirm the shapes render and no `EntryPointNotFound` occurs. **(Manual/visual — this is a USER smoke step; present it for the user to run in the IDE too.)**
- [ ] **Step 5: Run the Group C math test** now that `IDE\VisualGameStudioEngine.dll` is fresh → GREEN.
- [ ] **Step 6: Commit** in two commits: (a) `chore: refresh prebuilt IDE binaries (engine+wrapper) with raylib shapes Batch 1`; (b) `test(raylib): shapes Batch 1 VB.NET smoke scene`.

---

## Task 9: Definition of done + finish branch

- [ ] **Step 1: Parity** — `RaylibShapesParityTests` GREEN; counts 2504 / 2436.
- [ ] **Step 2: Fast subset** — `dotnet test … --filter "TestCategory!=Integration"` → no regressions (~3300 passed), confirming the parity test and csproj change didn't destabilize the suite.
- [ ] **Step 3: Group C math** — `RaylibShapesSplineMathTests` GREEN (engine staged).
- [ ] **Step 4: Smoke** — user visually confirms the shapes render (Groups A/B/D) and int/float `DrawRectangle` coexistence.
- [ ] **Step 5: Grep guards** — no `As Color` added to RaylibWrapper.vb; no `Color ` by-value param added to framework.h exports; hand-rolled siblings (`Framework_DrawSpline`, `Framework_DrawGradientRect*`, `Framework_DrawBezier*`, `Framework_SplinePoint`) unchanged.
- [ ] **Step 6:** Use superpowers:finishing-a-development-branch → merge to master, push, refresh already committed. Update memory.

---

## Appendix A — the 37 functions (raylib 5.5 → engine export → forwarder → wrapper)

Color always decomposes to `unsigned char r,g,b,a` (engine) / `r,g,b,a As Byte` (wrapper). `u8` below = `unsigned char`. Wrapper name == engine name, `CallingConvention.Cdecl`, in `#Region "Raylib Shapes (Batch 1)"`.

### Group A — 21 simple (Vector2/Rectangle by value; Color→bytes)
| # | raylib 5.5 | engine export params (Framework_<name>) | forwarder call |
|--|--|--|--|
| A1 | `DrawPixelV(Vector2 position, Color)` | `Vector2 position, u8 r,g,b,a` | `DrawPixelV(position, {r,g,b,a})` |
| A2 | `DrawLineV(Vector2 s, Vector2 e, Color)` | `Vector2 startPos, Vector2 endPos, u8 r,g,b,a` | `DrawLineV(startPos,endPos,{r,g,b,a})` |
| A3 | `DrawLineEx(Vector2 s, Vector2 e, float thick, Color)` | `Vector2 startPos, Vector2 endPos, float thick, u8 r,g,b,a` | `DrawLineEx(startPos,endPos,thick,{r,g,b,a})` |
| A4 | `DrawLineBezier(Vector2 s, Vector2 e, float thick, Color)` | `Vector2 startPos, Vector2 endPos, float thick, u8 r,g,b,a` | `DrawLineBezier(...)` |
| A5 | `DrawCircleGradient(int cX,int cY,float radius,Color inner,Color outer)` | `int centerX, int centerY, float radius, u8 ir,ig,ib,ia, u8 or_,og,ob,oa` | `DrawCircleGradient(centerX,centerY,radius,{ir,ig,ib,ia},{or_,og,ob,oa})` |
| A6 | `DrawCircleV(Vector2 c, float radius, Color)` | `Vector2 center, float radius, u8 r,g,b,a` | `DrawCircleV(center,radius,{r,g,b,a})` |
| A7 | `DrawCircleLinesV(Vector2 c, float radius, Color)` | `Vector2 center, float radius, u8 r,g,b,a` | `DrawCircleLinesV(...)` |
| A8 | `DrawRectangleV(Vector2 pos, Vector2 size, Color)` | `Vector2 position, Vector2 size, u8 r,g,b,a` | `DrawRectangleV(position,size,{r,g,b,a})` |
| A9 | `DrawRectangleRec(Rectangle rec, Color)` | `Rectangle rec, u8 r,g,b,a` | `DrawRectangleRec(rec,{r,g,b,a})` |
| A10 | `DrawRectanglePro(Rectangle rec, Vector2 origin, float rot, Color)` | `Rectangle rec, Vector2 origin, float rotation, u8 r,g,b,a` | `DrawRectanglePro(rec,origin,rotation,{r,g,b,a})` |
| A11 | `DrawRectangleGradientV(int x,int y,int w,int h,Color top,Color bottom)` | `int posX,posY,width,height, u8 tr,tg,tb,ta, u8 br,bg,bb,ba` | `DrawRectangleGradientV(posX,posY,width,height,{tr,tg,tb,ta},{br,bg,bb,ba})` |
| A12 | `DrawRectangleGradientH(int x,int y,int w,int h,Color left,Color right)` | `int posX,posY,width,height, u8 lr,lg,lb,la, u8 rr,rg,rb,ra` | `DrawRectangleGradientH(...,{lr,lg,lb,la},{rr,rg,rb,ra})` |
| A13 | `DrawRectangleGradientEx(Rectangle rec,Color TL,Color BL,Color TR,Color BR)` | `Rectangle rec, u8 tlr,tlg,tlb,tla, u8 blr,blg,blb,bla, u8 trr,trg,trb,tra, u8 brr,brg,brb,bra` | `DrawRectangleGradientEx(rec,{tlr..},{blr..},{trr..},{brr..})` |
| A14 | `DrawRectangleLinesEx(Rectangle rec, float lineThick, Color)` | `Rectangle rec, float lineThick, u8 r,g,b,a` | `DrawRectangleLinesEx(rec,lineThick,{r,g,b,a})` |
| A15 | `DrawRectangleRoundedLinesEx(Rectangle rec,float roundness,int segments,float lineThick,Color)` | `Rectangle rec, float roundness, int segments, float lineThick, u8 r,g,b,a` | `DrawRectangleRoundedLinesEx(rec,roundness,segments,lineThick,{r,g,b,a})` |
| A16 | `DrawPolyLinesEx(Vector2 c,int sides,float radius,float rot,float lineThick,Color)` | `Vector2 center, int sides, float radius, float rotation, float lineThick, u8 r,g,b,a` | `DrawPolyLinesEx(center,sides,radius,rotation,lineThick,{r,g,b,a})` |
| A17 | `DrawSplineSegmentLinear(Vector2 p1,Vector2 p2,float thick,Color)` | `Vector2 p1, Vector2 p2, float thick, u8 r,g,b,a` | `DrawSplineSegmentLinear(p1,p2,thick,{r,g,b,a})` |
| A18 | `DrawSplineSegmentBasis(Vector2 p1,p2,p3,p4,float thick,Color)` | `Vector2 p1,p2,p3,p4, float thick, u8 r,g,b,a` | `DrawSplineSegmentBasis(p1,p2,p3,p4,thick,{r,g,b,a})` |
| A19 | `DrawSplineSegmentCatmullRom(Vector2 p1,p2,p3,p4,float thick,Color)` | `Vector2 p1,p2,p3,p4, float thick, u8 r,g,b,a` | `DrawSplineSegmentCatmullRom(...)` |
| A20 | `DrawSplineSegmentBezierQuadratic(Vector2 p1,Vector2 c2,Vector2 p3,float thick,Color)` | `Vector2 p1, Vector2 c2, Vector2 p3, float thick, u8 r,g,b,a` | `DrawSplineSegmentBezierQuadratic(p1,c2,p3,thick,{r,g,b,a})` |
| A21 | `DrawSplineSegmentBezierCubic(Vector2 p1,Vector2 c2,Vector2 c3,Vector2 p4,float thick,Color)` | `Vector2 p1, Vector2 c2, Vector2 c3, Vector2 p4, float thick, u8 r,g,b,a` | `DrawSplineSegmentBezierCubic(...)` |

> `or` is a C++ keyword-like token in some contexts; the forwarder uses a local like `or_` only if you introduce a named `Color` variable — with brace-init `{ir,ig,ib,ia}` no such variable is needed. Wrapper param names are free (`outerR` etc.).

### Group B — 8 array (`const Vector2 *points, int pointCount`; Color→bytes; bare `Vector2()` in wrapper)
| # | raylib 5.5 | engine export params | forwarder call |
|--|--|--|--|
| B1 | `DrawLineStrip(const Vector2*, int, Color)` | `const Vector2 *points, int pointCount, u8 r,g,b,a` | `DrawLineStrip(points,pointCount,{r,g,b,a})` |
| B2 | `DrawTriangleFan(const Vector2*, int, Color)` | `const Vector2 *points, int pointCount, u8 r,g,b,a` | `DrawTriangleFan(points,pointCount,{r,g,b,a})` |
| B3 | `DrawTriangleStrip(const Vector2*, int, Color)` | `const Vector2 *points, int pointCount, u8 r,g,b,a` | `DrawTriangleStrip(...)` |
| B4 | `DrawSplineLinear(const Vector2*, int, float thick, Color)` | `const Vector2 *points, int pointCount, float thick, u8 r,g,b,a` | `DrawSplineLinear(points,pointCount,thick,{r,g,b,a})` |
| B5 | `DrawSplineBasis(const Vector2*, int, float thick, Color)` | `const Vector2 *points, int pointCount, float thick, u8 r,g,b,a` | `DrawSplineBasis(...)` |
| B6 | `DrawSplineCatmullRom(const Vector2*, int, float thick, Color)` | `const Vector2 *points, int pointCount, float thick, u8 r,g,b,a` | `DrawSplineCatmullRom(...)` |
| B7 | `DrawSplineBezierQuadratic(const Vector2*, int, float thick, Color)` | `const Vector2 *points, int pointCount, float thick, u8 r,g,b,a` | `DrawSplineBezierQuadratic(...)` |
| B8 | `DrawSplineBezierCubic(const Vector2*, int, float thick, Color)` | `const Vector2 *points, int pointCount, float thick, u8 r,g,b,a` | `DrawSplineBezierCubic(...)` |

Wrapper form (all B): `Public Sub Framework_<name>(points As Vector2(), pointCount As Integer[, thick As Single], r As Byte, g As Byte, b As Byte, a As Byte)`.

### Group C — 5 return Vector2 (no Color)
| # | raylib 5.5 | engine export | wrapper |
|--|--|--|--|
| C1 | `Vector2 GetSplinePointLinear(Vector2 s, Vector2 e, float t)` | `Vector2 Framework_GetSplinePointLinear(Vector2 startPos, Vector2 endPos, float t)` | `Function … As Vector2` |
| C2 | `Vector2 GetSplinePointBasis(Vector2 p1,p2,p3,p4, float t)` | `Vector2 Framework_GetSplinePointBasis(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float t)` | `… As Vector2` |
| C3 | `Vector2 GetSplinePointCatmullRom(Vector2 p1,p2,p3,p4, float t)` | `Vector2 Framework_GetSplinePointCatmullRom(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float t)` | `… As Vector2` |
| C4 | `Vector2 GetSplinePointBezierQuad(Vector2 p1, Vector2 c2, Vector2 p3, float t)` | `Vector2 Framework_GetSplinePointBezierQuad(Vector2 p1, Vector2 c2, Vector2 p3, float t)` | `… As Vector2` |
| C5 | `Vector2 GetSplinePointBezierCubic(Vector2 p1, Vector2 c2, Vector2 c3, Vector2 p4, float t)` | `Vector2 Framework_GetSplinePointBezierCubic(Vector2 p1, Vector2 c2, Vector2 c3, Vector2 p4, float t)` | `… As Vector2` |
Forwarder: `return <raylibName>(<args>);`

### Group D — 3 shapes-texture state
| # | raylib 5.5 | engine export | wrapper |
|--|--|--|--|
| D1 | `void SetShapesTexture(Texture2D texture, Rectangle source)` | `void Framework_SetShapesTexture(Texture2D texture, Rectangle source)` | `Sub … (texture As Texture2D, source As Rectangle)` |
| D2 | `Texture2D GetShapesTexture(void)` | `Texture2D Framework_GetShapesTexture()` | `Function … As Texture2D` |
| D3 | `Rectangle GetShapesTextureRectangle(void)` | `Rectangle Framework_GetShapesTextureRectangle()` | `Function … As Rectangle` |

**Total: 21 + 8 + 5 + 3 = 37.**
