# raylib 5.5 Parity — Text (Batch 2) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ~38 raylib 5.5 text/font functions to the engine C-ABI + VB.NET wrapper — 29 clean passthroughs, a new `GlyphInfo` struct, a faithful `MeasureTextExV`, and 8 custom-wrapped hazardous functions — safely marshaling strings, codepoint arrays, and malloc'd returns.

**Architecture:** Additive; same shared architecture as the shipped [[raylib-parity-shapes-batch1]]. Text-specific rules: string INPUT via `CharSet.Ansi`; **string RETURN via `Framework_<name>(...) As IntPtr` + a public `PtrToStringAnsi` wrapper** (NEVER `LPStr String` — that makes the CLR `CoTaskMemFree` a callee-owned buffer, UB); malloc'd returns copied into an engine static buffer then freed with raylib's `MemFree`/`Unload*`; codepoint arrays as bare `Integer()`. Spec: `docs/superpowers/specs/2026-07-24-raylib-parity-text-batch2-design.md`.

**Tech Stack:** C++17 engine (raylib 5.5 static-linked), VB.NET net8.0 wrapper, NUnit. Build via vswhere MSBuild (`-p:SolutionDir=<repo>\`), restore `-p:RestorePackagesConfig=true`.

---

## Key decisions (from the spec — read the spec for full rationale)

1. **String returns = `IntPtr` + `Marshal.PtrToStringAnsi`.** Each string-returning function gets TWO wrapper members: a `Public Function Framework_<name>(...) As IntPtr` DllImport (this is what the parity guard matches) and a `Public Function <Name>(...) As String` convenience wrapper (`If ptr = IntPtr.Zero Then Return "" ` then `Marshal.PtrToStringAnsi(ptr)`). Mirrors the existing `Ecs_GetName`/`GetQuestName` sites. Applies to: `TextSubtext`, `TextToUpper/Lower/Pascal/Snake/Camel`, `CodepointToUTF8`, `TextReplace`, `TextInsert`, `LoadUTF8`, `TextJoin` (11 functions).
2. **`GlyphInfo` struct** added to `Utiliy.vb` (4 ints + `Image`). `Font` unchanged (already by-value, opaque `IntPtr` pointers).
3. **`MeasureTextEx` collision** → the faithful `Vector2 MeasureTextEx(Font,…)` ships as `Framework_MeasureTextExV`; the existing handle-based `Framework_MeasureTextEx` is untouched.
4. **Custom engine wrappers** for malloc'd returns (copy-to-static-buffer-then-free) and array/buffer functions — see Tasks 6–7.
5. **Excluded:** `TextFormat` (variadic). **Deferred:** `LoadFontData`/`GenImageFontAtlas`/`UnloadFontData` (font-atlas trio), and the obviated `UnloadUTF8`/`UnloadCodepoints`.
6. **Color→`unsigned char r,g,b,a`** decomposition and the bare-`Integer()`/`Vector2()` array rule are exactly as shipped in Batch 1.

---

## File structure

| File | Change | Responsibility |
|---|---|---|
| `RaylibWrapper/Utiliy.vb` | Modify (+1 struct) | `GlyphInfo` struct |
| `VisualGameStudioEngine/framework.h` | Modify (+~40 decls) | export declarations before `// TEXT MEASUREMENT` banner (in `extern "C"`) |
| `VisualGameStudioEngine/framework.cpp` | Modify (+~40 forwarders/wrappers, +static buffers) | forwarders + the custom static-buffer/caller-buffer wrappers |
| `RaylibWrapper/RaylibWrapper.vb` | Modify (+~40 DllImports, +11 String wrappers) | bindings in a new `#Region "Raylib Text (Batch 2)"` |
| `VisualGameStudio.Tests/Native/RaylibTextParityTests.cs` | Create | parity scan (no engine) |
| `VisualGameStudio.Tests/Native/RaylibTextStringTests.cs` | Create | GL-free string/codepoint correctness via local `[DllImport]` (Integration) |
| `TestVbDLL/SampleTextBatch2.vb`, `TestVbDLL/Program.vb` | Create/Modify | `--text` smoke scene |
| `IDE/VisualGameStudioEngine.{dll,lib}`, `IDE/RaylibWrapper.dll` | Refresh | ship the new exports/bindings |

Full function tables in **Appendix A**.

---

## Task 0: Build baseline
Same as Batch 1 (the engine now builds since `8e6a364`). FOREGROUND commands.
- [ ] **Step 1: Restore** — `msbuild VisualGameStudioEngine.sln -t:restore -p:RestorePackagesConfig=true` (nuget not on PATH). Expect `packages\raylib.5.5.0\build\native\raylib.targets`.
- [ ] **Step 2: Engine build** — `& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" "VisualGameStudioEngine\VisualGameStudioEngine.vcxproj" -p:Configuration=Release -p:Platform=x64 "-p:SolutionDir=C:\Users\melvi\source\repos\VisualGameStudioEngine\" -v:minimal` → 0 errors, `x64\Release\VisualGameStudioEngine.dll`.
- [ ] **Step 3: Wrapper build** — `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 4: Record counts** — framework.h `__declspec(dllexport)` = **2504**; RaylibWrapper.vb `<DllImport` = **2436**. No commit.

## Task 1: GlyphInfo struct
**Files:** Modify `RaylibWrapper/Utiliy.vb`.
- [ ] **Step 1:** Add inside `Public Module Utiliy` (after the `Font` struct):
```vbnet
<StructLayout(LayoutKind.Sequential)>
Public Structure GlyphInfo
    Public value As Integer       ' Unicode codepoint
    Public offsetX As Integer
    Public offsetY As Integer
    Public advanceX As Integer
    Public image As Image
End Structure
```
- [ ] **Step 2:** `dotnet build RaylibWrapper/RaylibWrapper.vbproj -c Release` → 0 errors.
- [ ] **Step 3: Commit** `feat(engine): GlyphInfo struct for raylib text Batch 2`.

## Task 2: Parity guard (RED)
**Files:** Create `VisualGameStudio.Tests/Native/RaylibTextParityTests.cs` — same shape as `RaylibShapesParityTests.cs`, with the 38 names from Appendix A (Group-1 non-string 22 + Group-1 string 7 + `MeasureTextExV` + `TextReplace`,`TextInsert`,`LoadUTF8`,`LoadCodepoints`,`TextJoin`,`TextCopy`,`TextAppend`,`TextSplit`). Assert each `Framework_<name>(` in both framework.h and RaylibWrapper.vb; `Has.Length.EqualTo(38)`.
- [ ] **Step 1:** Write it. **Step 2:** Run `--filter "FullyQualifiedName~RaylibTextParityTests"` → RED (all 38 missing). **Step 3: Commit** `test(raylib): text Batch 2 parity guard (red)`.

## Task 3: Group 1 — clean non-string-return passthroughs (22)
**Files:** framework.h (before `// TEXT MEASUREMENT`), framework.cpp (same order), RaylibWrapper.vb (new `#Region "Raylib Text (Batch 2)"`). Banner `// ==== TEXT (raylib 5.5 passthrough — Batch 2) ====`.
Add the 22 Group-1a rows (Appendix A.1). Color→bytes; Font/Image/Vector2/Rectangle by value; `GlyphInfo` return (GetGlyphInfo); `int*` out-params `ByRef`; `const int*`/`const unsigned char*` inputs as bare `Integer()`/`Byte()`.
- [ ] Steps: add decls / forwarders / DllImports (same order) → `dotnet build RaylibWrapper -c Release` (0 errors) → **Commit** `feat(engine): raylib text Batch 2 Group 1a (font/draw/glyph/codepoint)`.

## Task 4: Group 1 — clean string-return passthroughs (7)
The 7 static-buffer returns (Appendix A.2): `TextSubtext`, `TextToUpper`, `TextToLower`, `TextToPascal`, `TextToSnake`, `TextToCamel`, `CodepointToUTF8`.
- Engine: `__declspec(dllexport) const char* Framework_<name>(<in>);` forwarder returns raylib's value directly (raylib's static buffer; the caller copies before any further raylib call).
- Wrapper: `Framework_<name>(...) As IntPtr` DllImport **+** a public `<Name>(...) As String` (`PtrToStringAnsi`, Zero-guard). `CodepointToUTF8` also has a `ByRef utf8Size As Integer` out-param.
- [ ] Steps: add → build → **Commit** `feat(engine): raylib text Batch 2 Group 1b (string returns via PtrToStringAnsi)`.

## Task 5: Faithful MeasureTextExV (1)
- Engine: `__declspec(dllexport) Vector2 Framework_MeasureTextExV(Font font, const char* text, float fontSize, float spacing);` → `return MeasureTextEx(font, text, fontSize, spacing);`
- Wrapper: `Function Framework_MeasureTextExV(font As Font, text As String, fontSize As Single, spacing As Single) As Vector2` (`CharSet:=Ansi`).
- [ ] Steps: add → build → **Commit** `feat(engine): raylib text Batch 2 faithful MeasureTextExV`.

## Task 6: Custom wrappers — malloc'd string returns (3)
`TextReplace`, `TextInsert`, `LoadUTF8`. Engine (framework.cpp) — one static buffer each; copy raylib's malloc'd result, free it, return the static buffer:
```cpp
static char g_textReplaceBuf[8192];
const char* Framework_TextReplace(const char* text, const char* replace, const char* by) {
    char* r = TextReplace((char*)text, (char*)replace, (char*)by);
    if (!r) { g_textReplaceBuf[0] = '\0'; return g_textReplaceBuf; }
    size_t n = 0; for (; r[n] && n < sizeof(g_textReplaceBuf) - 1; ++n) g_textReplaceBuf[n] = r[n];
    g_textReplaceBuf[n] = '\0'; MemFree(r); return g_textReplaceBuf;
}
static char g_textInsertBuf[8192];
const char* Framework_TextInsert(const char* text, const char* insert, int position) { /* same, MemFree(r) */ }
static char g_utf8Buf[8192];
const char* Framework_LoadUTF8(const int* codepoints, int length) { char* r = LoadUTF8(codepoints, length); /* copy */ UnloadUTF8(r); return g_utf8Buf; }
```
- .h: `const char* Framework_TextReplace(const char*, const char*, const char*);` etc.
- Wrapper: each = `Framework_<name>(...) As IntPtr` DllImport (`CharSet:=Ansi`) + public `<Name>(...) As String` (`PtrToStringAnsi`, Zero-guard). `LoadUTF8` input `codepoints As Integer(), length As Integer`.
- [ ] Steps: add → build → **Commit** `feat(engine): raylib text Batch 2 malloc-return string wrappers`.

## Task 7: Custom wrappers — arrays & caller buffers (5)
`LoadCodepoints`, `TextJoin`, `TextCopy`, `TextAppend`, `TextSplit` (Appendix A.5 for exact signatures + the `Framework_TextSplit` `'\n'`-packing wrapper). Engine wrappers per spec §5b–§5d; wrapper bindings use `Integer()` / `String()`+LPArray/LPStr / `StringBuilder` / `ByRef`.
- [ ] Steps: add → build → **Commit** `feat(engine): raylib text Batch 2 array/buffer wrappers`.

## Task 8: Rebuild, stage, parity GREEN
- [ ] **Step 1:** engine build (Task 0 Step 2 cmd) → 0 errors. **Step 2:** wrapper build → 0 errors. **Step 3:** counts framework.h == **2542**, RaylibWrapper.vb == **2474** (+38). **Step 4:** parity test GREEN.

## Task 9: String/codepoint correctness tests (automated, Integration)
**Files:** Create `VisualGameStudio.Tests/Native/RaylibTextStringTests.cs` — `[Category("Integration")]`, self-contained local `[DllImport("VisualGameStudioEngine.dll")]` (string returns declared `As IntPtr`, asserted via `Marshal.PtrToStringAnsi`), self-skip on DllNotFound/EntryPointNotFound. The test csproj already stages `IDE\VisualGameStudioEngine.dll` (refreshed in Task 10). Assertions (all GL-free) from spec §8.2:
`TextToUpper("aB3")=="AB3"`; `TextToLower`,`TextToPascal("hello_world")=="HelloWorld"`,`TextToSnake`,`TextToCamel`; `TextSubtext("hello",1,3)=="ell"`; `TextLength`,`TextIsEqual`,`TextFindIndex("abcbc","bc")==1`; `TextToInteger("42")==42`,`TextToFloat("3.5")≈3.5`; `TextReplace("a-b-c","-","+")=="a+b+c"`,`TextInsert("ac","b",1)=="abc"`; `GetCodepointCount`; `CodepointToUTF8`; `LoadCodepoints`+`LoadUTF8` round-trip `"hi"`; `TextJoin({"a","b","c"},3,"-")=="a-b-c"`; `Framework_TextSplit("a-b-c","-"c,buf,cap)` count==3; `Framework_MeasureTextExV(GetFontDefault(),"text",20,1)` returns positive Vector2.
> **Test-authoring note:** keep assertions **ASCII**. `CharSet.Ansi` marshals a `String` to the system codepage, so a `"héllo"` input does NOT reach the engine as UTF-8 — do not assert UTF-8 byte counts for non-ASCII (assert the ASCII round-trips, which are exact).
- [ ] Steps: write → (GREEN after Task 10 stages the fresh DLL) → **Commit** `test(raylib): text Batch 2 string/codepoint correctness (integration)`.

## Task 10: IDE refresh + smoke + DoD + finish
- [ ] **Step 1:** kill stray `--lsp`/testhost, `dotnet build-server shutdown`; robocopy `x64\Release\VisualGameStudioEngine.{dll,lib}` + `RaylibWrapper\bin\Release\net8.0\RaylibWrapper.dll` → `IDE\` (`/R:1 /W:1`). Commit `chore: refresh prebuilt IDE binaries (engine+wrapper) with raylib text Batch 2`.
- [ ] **Step 2:** Run Task 9 correctness tests → GREEN.
- [ ] **Step 3:** `TestVbDLL/SampleTextBatch2.vb` (`--text`): `GetFontDefault()` + `DrawTextEx`, `DrawTextPro`, `DrawTextCodepoint`, and `MeasureTextExV`-based centering; frame-capped self-close. Wire `--text` into `Program.vb`. Build TestVbDLL (compile-check). Commit `test(raylib): text Batch 2 VB.NET smoke scene`.
- [ ] **Step 4: DoD:** parity GREEN; fast subset (`TestCategory!=Integration`) no regression; correctness tests GREEN; grep guards (no `As Color`; no `<MarshalAs(LPStr)>` on a `String` RETURN in the new region; the 6 pre-existing faithful text exports + handle-based `Framework_MeasureTextEx` untouched). USER visual smoke (`--text`).
- [ ] **Step 5:** superpowers:finishing-a-development-branch → merge, push. Update memory.

---

## Appendix A — the 38 functions

`u8` = `unsigned char`. Engine export order = .cpp order = wrapper order. Color→`u8 r,g,b,a`; Font/Image/Vector2/Rectangle/GlyphInfo by value; `const int*`→`Integer()`, `const unsigned char*`→`Byte()`; `int*` out→`ByRef … As Integer`.

### A.1 Group 1a — clean, non-string-return (22)
| raylib 5.5 | engine `Framework_<name>` | wrapper return |
|--|--|--|
| `Font GetFontDefault(void)` | `Font Framework_GetFontDefault()` | `As Font` |
| `Font LoadFont(const char* fileName)` | `Font Framework_LoadFont(const char* fileName)` | `As Font` (CharSet Ansi) |
| `Font LoadFontFromImage(Image, Color key, int firstChar)` | `Font Framework_LoadFontFromImage(Image image, u8 r,g,b,a, int firstChar)` | `As Font` |
| `Font LoadFontFromMemory(const char* fileType, const unsigned char* fileData, int dataSize, int fontSize, int* codepoints, int codepointCount)` | `Font Framework_LoadFontFromMemory(const char* fileType, const unsigned char* fileData, int dataSize, int fontSize, int* codepoints, int codepointCount)` | `As Font`; `fileData As Byte(), codepoints As Integer()` |
| `bool IsFontValid(Font)` | `bool Framework_IsFontValid(Font font)` | `As <MarshalAs(I1)> Boolean` |
| `bool ExportFontAsCode(Font, const char* fileName)` | `bool Framework_ExportFontAsCode(Font font, const char* fileName)` | `As Boolean` |
| `void DrawTextPro(Font, const char*, Vector2 position, Vector2 origin, float rotation, float fontSize, float spacing, Color)` | `void Framework_DrawTextPro(Font font, const char* text, Vector2 position, Vector2 origin, float rotation, float fontSize, float spacing, u8 r,g,b,a)` | `Sub` |
| `void DrawTextCodepoint(Font, int codepoint, Vector2, float, Color)` | `void Framework_DrawTextCodepoint(Font font, int codepoint, Vector2 position, float fontSize, u8 r,g,b,a)` | `Sub` |
| `void DrawTextCodepoints(Font, const int* codepoints, int count, Vector2, float, float, Color)` | `void Framework_DrawTextCodepoints(Font font, const int* codepoints, int count, Vector2 position, float fontSize, float spacing, u8 r,g,b,a)` | `codepoints As Integer()` |
| `void SetTextLineSpacing(int)` | `void Framework_SetTextLineSpacing(int spacing)` | `Sub` |
| `int GetGlyphIndex(Font, int codepoint)` | `int Framework_GetGlyphIndex(Font font, int codepoint)` | `As Integer` |
| `GlyphInfo GetGlyphInfo(Font, int codepoint)` | `GlyphInfo Framework_GetGlyphInfo(Font font, int codepoint)` | `As GlyphInfo` |
| `Rectangle GetGlyphAtlasRec(Font, int codepoint)` | `Rectangle Framework_GetGlyphAtlasRec(Font font, int codepoint)` | `As Rectangle` |
| `int GetCodepointCount(const char*)` | `int Framework_GetCodepointCount(const char* text)` | `As Integer` |
| `int GetCodepoint(const char*, int* codepointSize)` | `int Framework_GetCodepoint(const char* text, int* codepointSize)` | `ByRef codepointSize As Integer` |
| `int GetCodepointNext(const char*, int* codepointSize)` | `int Framework_GetCodepointNext(const char* text, int* codepointSize)` | `ByRef` |
| `int GetCodepointPrevious(const char*, int* codepointSize)` | `int Framework_GetCodepointPrevious(const char* text, int* codepointSize)` | `ByRef` |
| `bool TextIsEqual(const char*, const char*)` | `bool Framework_TextIsEqual(const char* text1, const char* text2)` | `As Boolean` |
| `unsigned int TextLength(const char*)` | `unsigned int Framework_TextLength(const char* text)` | `As UInteger` |
| `int TextFindIndex(const char*, const char*)` | `int Framework_TextFindIndex(const char* text, const char* find)` | `As Integer` |
| `int TextToInteger(const char*)` | `int Framework_TextToInteger(const char* text)` | `As Integer` |
| `float TextToFloat(const char*)` | `float Framework_TextToFloat(const char* text)` | `As Single` |

### A.2 Group 1b — clean, string return (7) — engine returns `const char*`; wrapper = `Framework_<name>(...) As IntPtr` + public `<Name>(...) As String` (PtrToStringAnsi, Zero-guard)
`TextSubtext(const char* text, int position, int length)`, `TextToUpper(const char*)`, `TextToLower(const char*)`, `TextToPascal(const char*)`, `TextToSnake(const char*)`, `TextToCamel(const char*)`, `CodepointToUTF8(int codepoint, int* utf8Size)` (utf8Size → `ByRef`).

### A.3 MeasureTextExV (1) — `Vector2 Framework_MeasureTextExV(Font, const char*, float, float)` → `return MeasureTextEx(...)`.

### A.4 Malloc-return string wrappers (3) — see Task 6 code. `TextReplace`, `TextInsert`, `LoadUTF8(const int* codepoints, int length)`. Wrapper: IntPtr + PtrToStringAnsi.

### A.5 Array/buffer wrappers (5)
| fn | engine wrapper signature | wrapper binding |
|--|--|--|
| LoadCodepoints | `int Framework_LoadCodepoints(const char* text, int* outCodepoints, int outCapacity)` (copies, `UnloadCodepoints`, returns full count) | `(text As String, outCodepoints As Integer(), outCapacity As Integer) As Integer` |
| TextJoin | `const char* Framework_TextJoin(const char** textList, int count, const char* delimiter)` (passthrough; static return) | `Framework_TextJoin(<MarshalAs(LPArray,ArraySubType:=LPStr)> textList As String(), count As Integer, delimiter As String) As IntPtr` + public `TextJoin(textList, delimiter) As String` |
| TextCopy | `int Framework_TextCopy(char* dst, const char* src)` (passthrough) | `(dst As System.Text.StringBuilder, src As String) As Integer` |
| TextAppend | `void Framework_TextAppend(char* text, const char* append, int* position)` (passthrough) | `(text As System.Text.StringBuilder, append As String, ByRef position As Integer)` |
| TextSplit | `int Framework_TextSplit(const char* text, char delimiter, char* outBuf, int outCapacity)` (`'\n'`-packs pieces, returns count) — see spec §5d | `(text As String, delimiter As Byte, outBuf As System.Text.StringBuilder, outCapacity As Integer) As Integer` |

**Excluded:** `TextFormat`. **Deferred:** `LoadFontData`, `GenImageFontAtlas`, `UnloadFontData`, `UnloadUTF8`, `UnloadCodepoints`.
