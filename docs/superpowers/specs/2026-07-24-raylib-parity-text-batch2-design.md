# raylib 5.5 Parity — Text (Batch 2) — Design

**Status:** design
**Date:** 2026-07-24
**Scope:** the second sub-project of the raylib 5.5 parity effort (master spec
`2026-07-24-raylib-parity-engine-wrapper-design.md`, §5 lists text as batch 2). Adds the
missing **text/font module** functions to the engine C-ABI (`framework.h`/`framework.cpp`)
and the VB.NET P/Invoke wrapper (`RaylibWrapper.vb` + `Utiliy.vb`). Shared architecture,
conventions, and build/verify mechanics are inherited from the master spec and the shipped
[[raylib-parity-shapes-batch1]] (§1 there). This doc only covers what is text-specific.

---

## 1. Coverage inventory (measured against `framework.h`)

raylib 5.5 has **50** text-module functions. Current engine coverage:
- **6 already faithful** (skip): `DrawFPS`, `DrawText`, `DrawTextEx`, `LoadFontEx`, `UnloadFont`, `MeasureText`.
- **1 name collision**: `Framework_MeasureTextEx` already exists but **handle-based** —
  `void Framework_MeasureTextEx(int fontHandle, const char*, float, float, float* outWidth, float* outHeight)`
  (framework.h:4316). A raylib-faithful export cannot reuse the name (duplicate `extern "C"`
  symbol → link error). See §4.
- **29 absent-clean** + **14 absent-hazardous**.

This batch delivers **~38 functions**: 29 clean + 1 renamed-faithful MeasureTextEx + 8
custom-wrapped hazardous, plus a new `GlyphInfo` struct. It **excludes** `TextFormat` and
**defers** the font-atlas trio (see §6).

---

## 2. New struct: `GlyphInfo`

`GetGlyphInfo` returns `GlyphInfo` by value; it is the only new struct in this batch.
raylib (raylib.h:307): `int value; int offsetX; int offsetY; int advanceX; Image image;`.
`Image` is already defined in `Utiliy.vb`. Add to `Utiliy.vb` (module `Utiliy`):

```vbnet
<StructLayout(LayoutKind.Sequential)>
Public Structure GlyphInfo
    Public value As Integer       ' Unicode codepoint
    Public offsetX As Integer
    Public offsetY As Integer
    Public advanceX As Integer
    Public image As Image         ' already defined in Utiliy.vb
End Structure
```
`Font` is **already** by-value with opaque `recs`/`glyphs As IntPtr` (Utiliy.vb:123) — no Font change.

---

## 3. Group 1 — Clean passthroughs (29), Batch-1 style

Same conventions as Batch 1: Color→`unsigned char r,g,b,a`; Vector2/Rectangle/Font/Image/Texture2D
by value; return-by-value works; name-for-name; `CallingConvention.Cdecl`. Text-specific:
- **`const char*` string INPUT** → wrapper `text As String`, `<DllImport(..., CharSet:=CharSet.Ansi)>`
  (existing convention, ~256 sites).
- **`const char*` RETURN into a callee-owned/STATIC buffer** (`TextSubtext`, `TextToUpper/Lower/Pascal/Snake/Camel`,
  `CodepointToUTF8`; also the §5a/§5c wrappers) → **bind the P/Invoke as `As IntPtr` and expose a managed wrapper
  returning `Marshal.PtrToStringAnsi(ptr)`.** ⛔ Do NOT use `As <MarshalAs(LPStr)> String` for returns: the CLR
  then calls `CoTaskMemFree` on a pointer it did not `CoTaskMemAlloc` — undefined behavior for raylib's static
  buffers and the engine static buffers in §5a (it survives only by luck on the release CRT; crashes under
  PageHeap/AppVerifier/debug CRT). `PtrToStringAnsi` copies WITHOUT freeing, which is exactly right for a
  callee-owned buffer. This is the codebase's dominant convention (~68 sites: 60 `As IntPtr` returns + 8
  `PtrToStringAnsi` wrappers, e.g. `Ecs_GetName`, `AnimCtrl_GetCurrentStateName`); the 2 existing `LPStr String`
  return sites are latent bugs, not the pattern to copy. Shape:
  ```vbnet
  <DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
  Friend Function Framework_TextToUpper(text As String) As IntPtr
  End Function
  ' public managed wrapper — copies, never frees the callee buffer
  Public Function TextToUpper(text As String) As String
      Return Marshal.PtrToStringAnsi(Framework_TextToUpper(text))
  End Function
  ```
- **`const int* codepoints` INPUT array** (`DrawTextCodepoints`) → wrapper `codepoints As Integer(), count As Integer`
  (bare array, no LPArray attr — same rule as Batch 1's `Vector2()`).
- **`int* codepointSize` OUT-param** (`GetCodepoint`, `GetCodepointNext/Previous`, `CodepointToUTF8`) →
  wrapper `ByRef codepointSize As Integer` (existing ByRef convention, 67 sites).

The 29: `GetFontDefault`, `LoadFont`, `LoadFontFromImage`, `LoadFontFromMemory`, `IsFontValid`,
`ExportFontAsCode`, `DrawTextPro`, `DrawTextCodepoint`, `DrawTextCodepoints`, `SetTextLineSpacing`,
`GetGlyphIndex`, `GetGlyphInfo`, `GetGlyphAtlasRec`, `GetCodepointCount`, `GetCodepoint`,
`GetCodepointNext`, `GetCodepointPrevious`, `CodepointToUTF8`, `TextIsEqual`, `TextLength`,
`TextSubtext`, `TextFindIndex`, `TextToUpper`, `TextToLower`, `TextToPascal`, `TextToSnake`,
`TextToCamel`, `TextToInteger`, `TextToFloat`.

Note `LoadFontFromMemory(const char* fileType, const unsigned char* fileData, int dataSize, …)`:
`fileData` is an INPUT byte buffer → wrapper `fileData As Byte(), dataSize As Integer` (bare array).
`LoadFontFromImage(Image image, Color key, int firstChar)` → `image As Image, r,g,b,a As Byte, firstChar As Integer`.

---

## 4. Group 2 — Faithful `MeasureTextEx` (collision rename)

raylib `Vector2 MeasureTextEx(Font font, const char* text, float fontSize, float spacing)`.
The name is taken (handle-based), so export the faithful form as **`Framework_MeasureTextExV`**
(V = returns Vector2; the pre-existing handle-based `Framework_MeasureTextEx` is retained
untouched). Wrapper: `Function Framework_MeasureTextExV(font As Font, text As String, fontSize As Single, spacing As Single) As Vector2`.

---

## 5. Group 3 — Custom engine wrappers for the 8 wrappable hazardous functions

These are **not** passthroughs — each is a small `framework.cpp` wrapper that makes the C
ownership/marshaling safe for P/Invoke. All new names are raylib's exact names (none collide).

### 5a. Malloc'd string returns → engine static buffer + free (3)
`TextReplace`, `TextInsert`, `LoadUTF8` return a heap `char*` the caller must free. The wrapper
copies into a per-function engine-owned static buffer, frees raylib's allocation with raylib's
own `MemFree`, and returns the static buffer as `const char*` (the VB side binds `As IntPtr` +
`Marshal.PtrToStringAnsi` per §3 — never `LPStr String`). A single static buffer per function is
safe because `PtrToStringAnsi` copies the result before the next call overwrites it.

```cpp
// framework.cpp — one static buffer per wrapper (documented cap; truncates longer results)
static char g_textReplaceBuf[8192];
const char* Framework_TextReplace(const char* text, const char* replace, const char* by) {
    char* r = TextReplace((char*)text, (char*)replace, (char*)by);   // raylib mallocs
    if (!r) { g_textReplaceBuf[0] = '\0'; return g_textReplaceBuf; }
    // copy up to cap-1, NUL-terminate, then free raylib's buffer
    size_t n = 0; for (; r[n] && n < sizeof(g_textReplaceBuf) - 1; ++n) g_textReplaceBuf[n] = r[n];
    g_textReplaceBuf[n] = '\0';
    MemFree(r);
    return g_textReplaceBuf;
}
```
`Framework_TextInsert(const char* text, const char* insert, int position)` — identical shape.
`Framework_LoadUTF8(const int* codepoints, int length)` — `char* r = LoadUTF8(codepoints, length);`
copy into `g_utf8Buf`, then `UnloadUTF8(r);` (raylib's own paired free), return the static buffer.
The C++ side returns `const char*` into its static buffer; the **VB side binds `As IntPtr` + a
`Marshal.PtrToStringAnsi` managed wrapper** (per §3 — never `LPStr String`, which would make the CLR
`CoTaskMemFree` the engine's global buffer). **`UnloadUTF8` is NOT exported** — the wrapper already
freed raylib's allocation; exposing it would double-free.

### 5b. Malloc'd array return → caller buffer (1)
`int* LoadCodepoints(const char* text, int* count)` mallocs an int array. Wrapper copies into a
caller-provided buffer and frees raylib's:
```cpp
int Framework_LoadCodepoints(const char* text, int* outCodepoints, int outCapacity) {
    int count = 0; int* cps = LoadCodepoints(text, &count);
    int n = count < outCapacity ? count : outCapacity;
    for (int i = 0; i < n; ++i) outCodepoints[i] = cps[i];
    UnloadCodepoints(cps);          // raylib's paired free
    return count;                   // full count so caller can detect truncation
}
```
Wrapper: `Function Framework_LoadCodepoints(text As String, outCodepoints As Integer(), outCapacity As Integer) As Integer`.
VB sizes via `GetCodepointCount(text)` first. **`UnloadCodepoints` is NOT exported** (nothing exposed to free).

### 5c. Array-of-strings input (1)
`const char* TextJoin(const char** textList, int count, const char* delimiter)` returns a raylib
static buffer (no malloc) — near-passthrough. The `const char**` input marshals from a VB `String()`;
the return uses the §3 IntPtr + `PtrToStringAnsi` pattern (NOT `LPStr String`):
```vbnet
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Friend Function Framework_TextJoin(<MarshalAs(UnmanagedType.LPArray, ArraySubType:=UnmanagedType.LPStr)> textList As String(),
    count As Integer, delimiter As String) As IntPtr
End Function
Public Function TextJoin(textList As String(), delimiter As String) As String
    Return Marshal.PtrToStringAnsi(Framework_TextJoin(textList, textList.Length, delimiter))
End Function
```

### 5d. Mutable caller buffers (3)
`TextCopy(char* dst, const char* src) -> int` and `TextAppend(char* text, const char* append, int* position)`
take a mutable caller buffer → wrapper `dst/text As System.Text.StringBuilder` (caller pre-sizes
capacity), `TextAppend`'s cursor `ByRef position As Integer`. Near-passthrough (raylib writes in place).
`TextSplit(const char* text, char delimiter, int* count) -> const char**` returns pointers into a
static buffer; the wrapper packs the pieces `'\n'`-separated into a caller buffer and returns the count:
```cpp
int Framework_TextSplit(const char* text, char delimiter, char* outBuf, int outCapacity) {
    int count = 0; const char** parts = TextSplit(text, delimiter, &count);
    int w = 0;
    for (int i = 0; i < count; ++i) {
        if (i > 0 && w < outCapacity - 1) outBuf[w++] = '\n';
        for (const char* p = parts[i]; *p && w < outCapacity - 1; ++p) outBuf[w++] = *p;
    }
    if (outCapacity > 0) outBuf[w < outCapacity ? w : outCapacity - 1] = '\0';
    return count;
}
```
Wrapper: `Function Framework_TextSplit(text As String, delimiter As Byte, outBuf As StringBuilder, outCapacity As Integer) As Integer`
(VB splits `outBuf.ToString()` on `vbLf`). Documented: pieces containing `'\n'` are not round-trippable — acceptable for a splitter.

---

## 6. Excluded / deferred (with reasons)

- **EXCLUDED — `TextFormat(const char*, ...)`**: C variadic; no portable P/Invoke marshaling. VB uses
  `String.Format`/`&`. Not shippable at any effort.
- **DEFERRED — font-atlas trio**: `LoadFontData` (malloc'd `GlyphInfo*`), `GenImageFontAtlas`
  (nested `Rectangle**` out-param), `UnloadFontData`. Highest marshaling risk, niche (games use
  `LoadFont`/`LoadFontEx`). A later "text-advanced" batch with caller-buffer `GlyphInfo()`/`Rectangle()` wrappers.
- **OBVIATED — `UnloadUTF8`, `UnloadCodepoints`**: the §5a/§5b wrappers free raylib's allocation
  internally; exposing these would enable a double-free. Not exported.

---

## 7. The additive-only guarantee (unchanged from master spec §3)

No existing export/import is renamed or changed. The 6 already-faithful text exports and the
handle-based `Framework_MeasureTextEx`, `Framework_DrawTextExH`, `Framework_AcquireFontH`,
`Framework_DrawTextCentered/Right` etc. are all left exactly as-is; the new raylib-named exports coexist.

---

## 8. Verification

Text has a far richer GL-free surface than Batch 1's 5 math functions, so most correctness is
automatable.

1. **Parity guard** (automated NUnit text-scan, no engine load): every new `Framework_<name>`
   export ↔ matching `<DllImport>`. Names include the renamed `MeasureTextExV`.
2. **String/codepoint correctness** (automated NUnit `[Category("Integration")]`, self-contained
   local `[DllImport("VisualGameStudioEngine.dll")]`, self-skip on DllNotFound/EntryPointNotFound —
   the Batch-1 pattern). **String-returning P/Invokes are declared `... As IntPtr` and asserted via
   `Marshal.PtrToStringAnsi(ptr)`** (same reason as §3 — never a `String` return marshaler that would
   free the callee buffer). All GL-free and deterministic:
   - `TextToUpper("aB3")` == "AB3"; `TextToLower`, `TextToPascal("hello_world")`, `TextToSnake`, `TextToCamel`.
   - `TextSubtext("hello",1,3)` == "ell"; `TextLength("héllo")` (byte length); `TextIsEqual`, `TextFindIndex("abcbc","bc")`==1.
   - `TextToInteger("42")`==42; `TextToFloat("3.5")`≈3.5.
   - `TextReplace("a-b-c","-","+")`=="a+b+c"; `TextInsert("ac","b",1)`=="abc" (proves the malloc-free wrapper).
   - `GetCodepointCount("héllo")`; `CodepointToUTF8` for a known codepoint; `LoadCodepoints`/`LoadUTF8`
     round-trip (`"hi"` → codepoints → back to `"hi"`), proving the caller-buffer + static-buffer wrappers.
   - `TextJoin({"a","b","c"}, 3, "-")`=="a-b-c"; `Framework_TextSplit("a-b-c","-"c, …)` count==3.
   - `Framework_MeasureTextExV(GetFontDefault(), "text", 20, 1)` returns a positive Vector2.
3. **Drawing + font-load smoke** (GL, USER visual): a TestVbDLL `--text` scene using `GetFontDefault`
   (and `LoadFont` if a .ttf is available) with `DrawTextEx`, `DrawTextPro`, `DrawTextCodepoint`, and
   `MeasureTextExV`-based centering. Building TestVbDLL is itself a compile-check that the bindings are VB-callable.
4. **IDE refresh** ships the rebuilt `VisualGameStudioEngine.dll` + `.lib` + `RaylibWrapper.dll` (per the
   [[raylib-parity-shapes-batch1]] playbook — build vcxproj with `-p:SolutionDir`, restore with
   `-p:RestorePackagesConfig=true`).

---

## 9. Risks

- **Static-buffer truncation** (§5a): results longer than the 8 KB cap are truncated. Text ops on
  game-sized strings never approach this; documented, matches raylib's own `TextFormat` model.
- **Engine drift**: the native engine was resurrected in Batch 1 (`8e6a364`); watch for further
  pre-existing breaks when the rebuild touches new code (none expected in the text area, which
  already compiles).
- **`GlyphInfo` layout**: must match raylib's field order exactly (4 ints + `Image`), else
  `GetGlyphInfo` marshals garbage. Covered by an `Image`-field sanity check in the correctness tests.
