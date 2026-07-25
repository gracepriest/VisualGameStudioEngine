# raylib 5.5 Parity — Textures Batch 3a (Color / pixel) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the 17 raylib 5.5 color/pixel functions to the engine C-ABI + VB wrapper — establishing the **Color-struct-by-value return** convention (and `Vector3`/`Vector4` structs) that the rest of the textures module inherits.

**Architecture:** Additive, same shared architecture as shipped [[raylib-parity-shapes-batch1]] / [[raylib-parity-text-batch2]]. Color **params** decompose to `unsigned char r,g,b,a` (house rule); Color **returns** come back as the 4-byte `Color` struct BY VALUE (new — proven-safe ABI: `Vector2` register-return + `Rectangle` sret-return already work in-repo). `void*` pixel pointers → `IntPtr`. All 17 are GL-free → **no GUI smoke; the automated correctness suite is complete coverage.**

**Tech Stack:** C++17 engine (raylib 5.5 static-linked), VB.NET net8.0 wrapper, NUnit. Build via vswhere MSBuild (`-p:SolutionDir=<repo>\`, restore `-p:RestorePackagesConfig=true`); TestVbDLL only via VS MSBuild `-p:Platform=x64`. Spec: `docs/superpowers/specs/2026-07-24-raylib-parity-textures-batch3a-design.md`.

---

## Key decisions (from the spec)
1. **Color RETURN = `As Color` by value** (single DllImport, no two-part wrapper — unlike text's string returns). Color PARAMS stay `u8 r,g,b,a`.
2. **New structs `Vector3`/`Vector4`** in `Utiliy.vb` (3/4 `Single`, `<StructLayout(Sequential)>`) for `ColorNormalize`/`ColorFromNormalized`/`ColorToHSV`.
3. **`void*`** (GetPixelColor/SetPixelColor pixel pointers) → `IntPtr`; **bool** (`ColorIsEqual`) → `<MarshalAs(I1)>`.
4. **No collisions**: faithful `Framework_ColorTint`/`ColorFromHSV`/… coexist with the existing underscore `Framework_Color_*` helpers (framework.h:4384-4389), which are left untouched.
5. **Insertion**: new banner block `// ==== COLOR/PIXEL (raylib 5.5 passthrough — Batch 3a) ====` immediately AFTER the existing `Framework_Color_*` helpers (framework.h ~:4390, framework.cpp at the matching spot), inside the `extern "C"` block; wrapper in a new `#Region "Raylib Color/Pixel (Batch 3a)"`.

---

## File structure
| File | Change | Responsibility |
|---|---|---|
| `RaylibWrapper/Utiliy.vb` | Modify (+2 structs) | `Vector3`, `Vector4` |
| `VisualGameStudioEngine/framework.h` | Modify (+17 decls) | export declarations |
| `VisualGameStudioEngine/framework.cpp` | Modify (+17 forwarders) | forwarders (reassemble Color params, return Color/Vector by value) |
| `RaylibWrapper/RaylibWrapper.vb` | Modify (+17 DllImports) | bindings, new `#Region` |
| `VisualGameStudio.Tests/Native/RaylibColorParityTests.cs` | Create | parity scan (no engine) |
| `VisualGameStudio.Tests/Native/RaylibColorTests.cs` | Create | GL-free correctness (local `[DllImport]`, Integration) |
| `IDE/VisualGameStudioEngine.{dll,lib}`, `IDE/RaylibWrapper.dll` | Refresh | ship exports/bindings |

The 17 functions are in the spec §3 table (authoritative). **No GUI smoke / TestVbDLL scene** (all GL-free).

---

## Task 0: Build baseline
FOREGROUND. (Engine builds since Batch 1.)
- [ ] **Step 1:** `msbuild VisualGameStudioEngine.sln -t:restore -p:RestorePackagesConfig=true` (idempotent).
- [ ] **Step 2:** engine build → `& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" "VisualGameStudioEngine\VisualGameStudioEngine.vcxproj" -p:Configuration=Release -p:Platform=x64 "-p:SolutionDir=C:\Users\melvi\source\repos\VisualGameStudioEngine\" -v:minimal` → 0 errors.
- [ ] **Step 3:** wrapper build → `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 4:** counts framework.h `__declspec(dllexport)` = **2542**; RaylibWrapper.vb `<DllImport` = **2474**. No commit.

## Task 1: Vector3 / Vector4 structs
**Files:** Modify `RaylibWrapper/Utiliy.vb` (after the existing `GlyphInfo`/`Font` structs).
- [ ] **Step 1:** add:
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
- [ ] **Step 2:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 3: Commit** `feat(engine): Vector3/Vector4 structs for raylib textures Batch 3a`.

## Task 2: Parity guard (RED)
**Files:** Create `VisualGameStudio.Tests/Native/RaylibColorParityTests.cs` — same shape as `RaylibTextParityTests.cs`, 17 names: `ColorIsEqual`, `Fade`, `ColorToInt`, `ColorNormalize`, `ColorFromNormalized`, `ColorToHSV`, `ColorFromHSV`, `ColorTint`, `ColorBrightness`, `ColorContrast`, `ColorAlpha`, `ColorAlphaBlend`, `ColorLerp`, `GetColor`, `GetPixelColor`, `SetPixelColor`, `GetPixelDataSize`. Assert each `Framework_<name>(` in framework.h AND RaylibWrapper.vb; `Has.Length.EqualTo(17)`.
> ⚠ Parity token boundary: `Framework_ColorTint(` must not false-match; and note `Framework_Color_*` (underscore) helpers are DIFFERENT tokens — `Framework_ColorToHSV(` ≠ `Framework_Color_ToHSV(`. The trailing `(` + exact name handles this.
- [ ] **Step 1:** write it. **Step 2:** run `--filter "FullyQualifiedName~RaylibColorParityTests"` → RED (17 missing). **Step 3: Commit** `test(raylib): color/pixel Batch 3a parity guard (red)`.

## Task 3: The 17 color/pixel functions (engine + wrapper)
**Files:** framework.h (after :4389), framework.cpp (matching spot), RaylibWrapper.vb (new `#Region`). Add all 17 per spec §3, `.h`/`.cpp`/`.vb` in identical order.

**Worked examples (one per return shape):**
```cpp
// framework.h
__declspec(dllexport) Color   Framework_GetColor(unsigned int hexValue);
__declspec(dllexport) Color   Framework_ColorTint(unsigned char r, unsigned char g, unsigned char b, unsigned char a, unsigned char tr, unsigned char tg, unsigned char tb, unsigned char ta);
__declspec(dllexport) Vector4 Framework_ColorNormalize(unsigned char r, unsigned char g, unsigned char b, unsigned char a);
__declspec(dllexport) Color   Framework_GetPixelColor(void* srcPtr, int format);
__declspec(dllexport) void    Framework_SetPixelColor(void* dstPtr, unsigned char r, unsigned char g, unsigned char b, unsigned char a, int format);
__declspec(dllexport) bool    Framework_ColorIsEqual(unsigned char r, unsigned char g, unsigned char b, unsigned char a, unsigned char r2, unsigned char g2, unsigned char b2, unsigned char a2);
```
```cpp
// framework.cpp
Color   Framework_GetColor(unsigned int hexValue) { return GetColor(hexValue); }
Color   Framework_ColorTint(unsigned char r, unsigned char g, unsigned char b, unsigned char a, unsigned char tr, unsigned char tg, unsigned char tb, unsigned char ta) { return ColorTint(Color{r,g,b,a}, Color{tr,tg,tb,ta}); }
Vector4 Framework_ColorNormalize(unsigned char r, unsigned char g, unsigned char b, unsigned char a) { return ColorNormalize(Color{r,g,b,a}); }
Color   Framework_GetPixelColor(void* srcPtr, int format) { return GetPixelColor(srcPtr, format); }
void    Framework_SetPixelColor(void* dstPtr, unsigned char r, unsigned char g, unsigned char b, unsigned char a, int format) { SetPixelColor(dstPtr, Color{r,g,b,a}, format); }
bool    Framework_ColorIsEqual(unsigned char r, unsigned char g, unsigned char b, unsigned char a, unsigned char r2, unsigned char g2, unsigned char b2, unsigned char a2) { return ColorIsEqual(Color{r,g,b,a}, Color{r2,g2,b2,a2}); }
```
```vbnet
' RaylibWrapper.vb — #Region "Raylib Color/Pixel (Batch 3a)"
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GetColor(hexValue As UInteger) As Color
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_ColorTint(r As Byte, g As Byte, b As Byte, a As Byte, tr As Byte, tg As Byte, tb As Byte, ta As Byte) As Color
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_ColorNormalize(r As Byte, g As Byte, b As Byte, a As Byte) As Vector4
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_GetPixelColor(srcPtr As IntPtr, format As Integer) As Color
End Function
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Sub Framework_SetPixelColor(dstPtr As IntPtr, r As Byte, g As Byte, b As Byte, a As Byte, format As Integer)
End Sub
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Function Framework_ColorIsEqual(r As Byte, g As Byte, b As Byte, a As Byte, r2 As Byte, g2 As Byte, b2 As Byte, a2 As Byte) As <MarshalAs(UnmanagedType.I1)> Boolean
End Function
```
Remaining 11 follow the same shapes (spec §3): `Fade`/`ColorBrightness`/`ColorContrast`/`ColorAlpha`/`ColorLerp`/`ColorFromHSV`/`ColorFromNormalized`/`ColorAlphaBlend` → `As Color`; `ColorToHSV` → `As Vector3`; `ColorToInt`/`GetPixelDataSize` → `As Integer`. `ColorFromHSV(h As Single, s As Single, v As Single) As Color`; `ColorFromNormalized(normalized As Vector4) As Color`.
- [ ] **Step 1–3:** add 17 decls / forwarders / DllImports (same order).
- [ ] **Step 4:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 5: Commit** `feat(engine): raylib color/pixel Batch 3a (17 fns, Color-by-value returns)`.

## Task 4: Rebuild, stage, parity GREEN
- [ ] **Step 1:** engine build (Task 0 Step 2 cmd) → 0 errors. **Step 2:** wrapper build → 0 errors.
- [ ] **Step 3:** counts framework.h == **2559**, RaylibWrapper.vb == **2491** (+17).
- [ ] **Step 4:** parity test GREEN.

## Task 5: Correctness suite (automated, Integration) — the go/no-go for Color-by-value
**Files:** Create `VisualGameStudio.Tests/Native/RaylibColorTests.cs` — `[Category("Integration")]`, self-contained local `[DllImport("VisualGameStudioEngine.dll")]`, self-skip on DllNotFound/EntryPointNotFound. Declare a local `[StructLayout(Sequential)] struct RColor { byte r,g,b,a; }`, `RVector4 { float x,y,z,w; }`, `RVector3 { float x,y,z; }`. **The FIRST assertion proves the 4-byte Color-struct return marshals** — if it fails, STOP and switch the design to a packed `unsigned int` return before continuing (spec §6).

Assertions (spec §5.2; all GL-free, deterministic):
- `ColorToInt(255,0,0,255)` == unchecked((int)0xFF0000FF); `GetColor(0xFF0000FF)` → r=255,g=0,b=0,a=255 (**proves Color return**).
- `ColorNormalize(255,128,0,255)` → x≈1, y≈0.502, z≈0, w≈1 (Within 1e-2); `ColorFromNormalized({1,0,0,1})` → {255,0,0,255}.
- `ColorToHSV(255,0,0,255)` → x(hue,**degrees**)≈0, y≈1, z≈1; `ColorFromHSV(0,1,1)` → {255,0,0,255}.
- `Fade(255,255,255,255, 0.5f)`.a == 127; `ColorAlpha(255,0,0,255, 0.5f)`.a == 127.
- `ColorTint(255,255,255,255, 128,128,128,255)`.r == 128; `ColorLerp(0,0,0,255, 255,255,255,255, 0.5f)`.r within [126,129].
- `ColorBrightness(100,100,100,255, 0f)` → r==100 (factor 0 = unchanged); `ColorAlphaBlend(...)` runs, a>0.
- `ColorContrast(128,128,128,255, 0f)` → r==128 (contrast 0 = unchanged).
- `ColorIsEqual((255,0,0,255),(255,0,0,255))` == true; `((255,0,0,255),(255,0,0,254))` == false.
- `GetPixelDataSize(4,4, 7)` == 64.
- Pixel path: `var p = Marshal.AllocHGlobal(4); try { Framework_SetPixelColor(p, 10,20,30,40, 7); var c = Framework_GetPixelColor(p, 7); Assert {10,20,30,40}; } finally { Marshal.FreeHGlobal(p); }` (format 7 = UNCOMPRESSED_R8G8B8A8).
- [ ] **Step 1:** write it. **Step 2:** (runs GREEN after Task 6 stages the fresh DLL; or stage `x64\Release\VisualGameStudioEngine.dll` for an immediate run). **Step 3: Commit** `test(raylib): color/pixel Batch 3a correctness incl. Color-by-value return (integration)`.

## Task 6: IDE refresh + DoD + finish
- [ ] **Step 1:** clear locks (`dotnet build-server shutdown`, kill stray `--lsp`/testhost), robocopy `x64\Release\VisualGameStudioEngine.{dll,lib}` + `RaylibWrapper\bin\Release\net8.0\RaylibWrapper.dll` → `IDE\` (`/R:1 /W:1`). Commit `chore: refresh prebuilt IDE binaries (engine+wrapper) with raylib color Batch 3a`.
- [ ] **Step 2:** run correctness suite → GREEN (incl. the Color-by-value first assertion).
- [ ] **Step 3: DoD:** parity GREEN (17; counts 2559/2491); fast subset (`TestCategory!=Integration`) no regression; correctness GREEN; grep guards (no `As Color` PARAM added — Color only as a RETURN; the underscore `Framework_Color_*` helpers untouched). **No GUI smoke — all GL-free.**
- [ ] **Step 4:** superpowers:finishing-a-development-branch → merge to master, push. Update memory.
