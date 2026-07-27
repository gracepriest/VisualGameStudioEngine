# raylib 5.5 rcore module — raw parity (design of record)

Follows the raudio module (`2026-07-25-raylib-parity-audio-module-design.md`) as the next raylib-parity target in
the parallel worktree grind. Same principle: add the raw raylib-named C-ABI passthroughs (`Framework_<rawname>`)
coexisting with the engine's pre-existing custom/handle layers — a second, lower-level surface, no name collisions.

## Recon (authoritative, computed 2026-07-26 against `packages/raylib.5.5.0/.../raylib.h` lines 960–1218)

- rcore declares **198** `RLAPI` functions (Window/Graphics + Input Handling, Modules: core / utils).
- **71** are already exported raw (`Framework_BeginDrawing`, `Framework_ClearBackground`, `Framework_IsKeyDown`,
  `Framework_GetMouseX`, `Framework_SetTargetFPS`, … — the game-loop essentials the engine always needed).
- **127** are the parity gap (matches the roadmap number exactly). **5** of those are `SetXxxCallback` fn-pointer
  functions → deferred to the shared callbacks batch (with the 5 audio callbacks). → **122 to bind + 5 deferred**.

The gap is unusually testable: 5 of the 11 sub-batches are headless pure-data / pure-math (unlike audio, where almost
everything needed a device), because the never-bound surface is the *tooling* API (shaders, VR, file-system utils,
compression, automation) rather than the per-frame API.

## New structs required (checked `Utiliy.vb`)

Already present: `Shader`, `Camera2D`, `Texture2D`, `Image`, `Vector2/3/4`, `Color`, `Rectangle`, `Font`, etc.
To add across the batches: `Matrix` (C5 ✅), `Camera3D` (C5 ✅), `Ray` (C5 ✅), `VrDeviceInfo`, `VrStereoConfig`
(C3 — nested fixed-size `Matrix`/float arrays, the trickiest marshaling in the module), `AutomationEvent`,
`AutomationEventList` (C11), `FilePathList` (C9).

## 🔑 Conventions (inherited from raudio A1–A4, locked)

- Struct returns/params BY VALUE; `Xxx*` in-place mutators → `ByRef`.
- C `bool` returns → `As <MarshalAs(UnmanagedType.I1)> Boolean`; C# test side `[return: MarshalAs(UnmanagedType.I1)]`.
- `const char*` / `char*` RETURNS → `IntPtr` + `PtrToStringAnsi` (NEVER `LPStr String`); string INPUTS → `As String`
  + `CharSet.Ansi`. `const void*` / heap-`unsigned char*` → `IntPtr` (caller pins) or `Byte()`; `int* out` → `ByRef Integer`.
- Structs live in `RaylibWrapper\Utiliy.vb`; DllImports in `RaylibWrapper.vb`.
- **⛔ Wrapper name-collision rule (from A2):** the engine C-ABI is clean, but `RaylibWrapper.vb` has legacy managed
  convenience helpers that squat some plain raylib names. **GREP THE ACTUAL NAME PER BINDING** (`(Function|Sub)\s+
  Framework_<name>\b`); a squatted name gets a `Raw` suffix + `<DllImport(EntryPoint:="Framework_<name>")>`, engine
  export stays unsuffixed. Do NOT trust a prediction. (C5: grep confirmed **zero** collisions — all 8 bind plain.)
- **⚠ raylib `Matrix` field order is scrambled** (`m0,m4,m8,m12 / m1,m5,m9,m13 / …`) — memory layout follows the
  declaration order, so the VB struct AND any C# test mirror must list the 16 floats in that exact sequence.

## Decomposition — 11 sub-batches + deferred callbacks (122 + 5 = 127)

| # | Batch | Fns | New structs | Test | Notes |
|---|---|---|---|---|---|
| **C1** | **Window state & control** | **24** | — | **device+headless** | **SHIPPED — see below** (24 not 23: `GetWindowHandle` was genuinely unbound). |
| **C2** | **Window/monitor query & clipboard** | **13** | — | **device-lite** | **SHIPPED — see below** (13 not 14: 7 screen/monitor getters were already bound). |
| **C3** | **Drawing modes & VR** | **10** | **Camera3D✅, Matrix✅, VrDeviceInfo, VrStereoConfig** | **device** | **SHIPPED — see below** (10 new of 16; nested-array structs — the module's hardest marshaling). |
| **C4** | **Shaders** | **8** | **Shader✅, Matrix✅, Texture2D✅** | **device** | **SHIPPED — see below** (8 new: `GetShaderLocation`+`UnloadShader` were already bound raw; 10-fn family total). |
| **C5** | **Screen-space / camera math** | **8** | **Ray✅, Camera3D✅, Matrix✅** | **headless** | **SHIPPED — see below.** |
| **C6** | **Timing/frame + Random + Misc + 2 input stragglers** | **15** | — | **headless (partial)** | **SHIPPED — see below.** `TraceLog` fixed 2-arg `(int,const char*)`→`TraceLog(lvl,"%s",text)`; ⛔ `WaitTime` needs InitWindow's timer. |
| **C7** | **File data I/O** | **7** | — | **headless** | **SHIPPED — see below.** |
| **C8** | **File-system path queries** | **15** | — | **headless** | **SHIPPED — see below.** |
| **C9** | **Directory listing & dropped files** | **7** | **FilePathList ✅** | **headless (partial)** | **SHIPPED — see below.** First by-value struct-with-`char**`-array return. |
| **C10** | **Compression / Encoding** | **7** | — | **headless** | **SHIPPED — see below.** |
| **C11** | **Automation events** | **8** | **AutomationEvent, AutomationEventList** | **device-lite** | **SHIPPED — see below** (all 8 new; retained-pointer SetAutomationEventList → IntPtr). |
| — | Deferred callbacks | +5 | — | — | `SetTraceLogCallback` + 4 file-I/O callbacks; fold into the audio-callback batch. |

Recommended order (front-load headless exact-correctness): **C5 → C10 → C7 → C8 → C1 → C2 → C6 → C9 → C4 → C3 → C11**,
callbacks last.

## Verification split

- **Parity guard** (headless text scan of `framework.h` + `RaylibWrapper.vb`): `Framework_<name>(` for plain names,
  `EntryPoint:="Framework_<name>"` for any squatted ones.
- **Correctness:** headless batches → real NUnit unit tests in the fast subset (local `[DllImport]` + struct mirrors,
  `Guard`/`OneTimeSetUp` self-skip when the engine DLL isn't staged). device/device-lite batches →
  `[Category("Integration")]`, init window/device + Ignore-if-headless (this workstation has a display + audio).
- **⛔ Staging trap:** stage a fresh `x64\Release\VisualGameStudioEngine.dll` into
  `VisualGameStudio.Tests\bin\Release\net8.0\` before running, else the correctness tests self-skip (false green).

## Progress

- **C5 — Screen-space / camera math (8) 🏁 SHIPPED** (see the parity/counts in the memory topic). Defines `Ray`,
  `Camera3D`, `Matrix` (reused by C3/C4). Fully headless, exact-math correctness in the fast subset:
  `GetCameraMatrix2D`→identity, `GetWorldToScreen2D` offset/zoom + `GetScreenToWorld2D` inverse round-trip,
  `GetCameraMatrix` view matrix (`m14 == -eye.z` = the Matrix-marshaling proof), `GetWorldToScreenEx` target→center,
  `GetScreenToWorldRayEx` origin=camera.position + dir=-Z, and the two non-Ex variants' deterministic headless behavior
  (`GetScreenToWorldRay` copies `camera.position`; `GetWorldToScreen`→NaN). Zero wrapper collisions (all 8 plain).
- **C10 — Compression / Encoding (7) 🏁 SHIPPED** (counts in the memory topic). `CompressData`/`DecompressData`/
  `EncodeDataBase64`/`DecodeDataBase64` return a raylib-malloc'd buffer + size via `int*` → wrapper returns `IntPtr` +
  `ByRef Integer`, caller `Marshal.Copy` + `Framework_MemFree` (reuses the existing `ExportImageToMemory`/`MemFree`
  ownership pattern). ⚠ `DecodeDataBase64` scans its input to a NUL → that ONE input marshals as an Ansi `String`.
  `ComputeCRC32`→`UInteger`; `ComputeMD5`/`ComputeSHA1` return a pointer to a STATIC `int[4]`/`int[5]` (never freed).
  Headless fast-subset correctness: DEFLATE round-trip (+shrinks repetitive data), Base64 `Man`⇄`TWFu` + all-byte
  round-trip, CRC-32 `0xCBF43926`, MD5 byte-for-byte vs .NET. **⚠ raylib 5.5 `ComputeSHA1` has UPSTREAM undefined
  behaviour (CHANGELOG #5957, fixed after 5.5) → does NOT yield the standard digest; we FAITHFULLY pass it through and
  contract-test it (deterministic, input-sensitive 20-byte static buffer) rather than pin the buggy value.** A raylib
  bump past 5.5 makes it standard for free. Zero wrapper collisions (all 7 plain).
- **C7 — File data I/O (7) 🏁 SHIPPED** (counts in the memory topic). `LoadFileData` → `IntPtr` + `ByRef Integer`
  (freed by `Framework_UnloadFileData`); `LoadFileText` → `IntPtr` + `PtrToStringAnsi` (freed by
  `Framework_UnloadFileText`) — heap buffers use raylib's OWN `Unload*`, never the caller's allocator.
  `SaveFileData`/`SaveFileText`/`ExportDataAsCode` → `bool` (I1); path/text/data inputs marshal as Ansi `String` /
  `Byte()`. Headless fast-subset correctness (temp-file round-trips, cross-checked with .NET's `File` API):
  Save↔Load bytes, Save↔Load ASCII text (no newline → immune to text-mode CRLF translation), and `ExportDataAsCode`
  emits a real C header. ⚠ raylib 5.5 `ExportDataAsCode` formats bytes with `%x` (byte 1 → `0x1`, not `0x01`) — the
  test keys on `static unsigned char` / `_DATA_SIZE` and bytes ≥ 0x10. Zero wrapper collisions (all 7 plain).
- **C8 — File-system path queries (15) 🏁 SHIPPED** (counts in the memory topic). `FileExists`/`DirectoryExists`/
  `IsFileExtension`/`ChangeDirectory`/`IsPathFile`/`IsFileNameValid` → `bool` (I1); `GetFileLength`/`MakeDirectory` →
  `Integer`; the 7 path getters → `const char*` returned as `IntPtr` + `PtrToStringAnsi` (never freed). Zero collisions.
  Fully headless, cross-checked vs .NET's `File`/`Directory`. **⛔ REAL BUG FOUND + FIXED: `GetFileName`/`GetFileExtension`
  raylib-return a pointer INTO the input string (an offset), not a static buffer — across P/Invoke the CLR frees the
  marshaled input right after the call, so the returned pointer DANGLES → `PtrToStringAnsi` reads freed memory (empty).**
  Fix: the `.cpp` forwarder copies raylib's result into an engine-side `static char[4096]` while the input is still
  alive (value identical to raylib; `GetFileExtension`'s NULL preserved). The other 5 getters use raylib's own static
  buffer and are unaffected. ⛔ raylib `GetDirectoryPath` prepends `./` to a relative path (faithful). ⛔
  `ChangeDirectory` mutates process cwd → its test is `[NonParallelizable]` + restores the original dir. Observed the
  real getter outputs via a PowerShell P/Invoke against the staged DLL before diagnosing (same tactic as C7's `%x`).
- **C1 — Window state & control (24) 🏁 SHIPPED** (counts 2754/2686, both +24 — one MORE than the estimated 23:
  `GetWindowHandle`, last in raylib.h's window-control section (968–996), was genuinely unbound). Bound the 24
  functions the engine did not already export raw; the 5 pre-existing window utilities (`ToggleFullscreen`,
  `ToggleBorderlessWindowed`, `SetWindowSize`, `SetWindowMinSize`, `SetWindowTitle`) are intentionally NOT re-declared,
  and a parity-guard test asserts each is still declared exactly once (duplicate-export guard). Zero wrapper collisions
  (all 24 plain). ⛔ These raw window forwarders COEXIST with the engine's MANAGED lifecycle: `Framework_Initialize`
  wraps `InitWindow` + camera/timing setup, `Framework_ShouldClose` wraps `WindowShouldClose`, `Framework_Shutdown`
  wraps `CloseWindow` after tearing systems down. A raw consumer drives `Framework_InitWindow`/`Framework_CloseWindow`
  itself and must not mix the two window paths in one process (exact raudio-A1 precedent: raw `InitAudioDevice`
  coexisting with the engine's audio init). Marshaling: `void`/`bool`(I1)/`int`, unsigned-int state flags → `UInteger`,
  `SetWindowOpacity`(float)→`Single`, `SetWindowIcon(Image)` BY VALUE, `SetWindowIcons(Image*, int)` → `IntPtr` + count,
  `GetWindowHandle` → `void*` → `IntPtr`. **Correctness is `[Category("Integration")]` + `[NonParallelizable]`** (needs a
  real GL window, unlike C5/C7/C8/C10): `InitWindow(320,240)` → hide immediately via `SetWindowState(FLAG_WINDOW_HIDDEN)`
  → assert the HIDDEN flag round-trips through `Is/SetWindowHidden`+`IsWindowState`, a non-visible `FLAG_WINDOW_RESIZABLE`
  round-trips through `Set`/`Clear`/`IsWindowState`, `GetWindowHandle` ≠ 0, `GetScreenWidth/Height` == 320/240 (oracle via
  the already-bound C2 getters), `WindowShouldClose` == false, then the remaining setters run under `DoesNotThrow` purely
  to prove their P/Invoke ABI (a wrong signature → AccessViolation). Self-`Ignore`s the whole test if no GL context can
  be created (headless CI); on this workstation the window WAS created and every assertion ran (fully runtime-verified).
  The parity guard (2 headless tests) runs in the fast subset. `SetWindowIcon(s)` covered by the parity guard only — the
  by-value `Image` marshaling is already exercised by the rtextures fixtures.
- **C2 — Window/monitor query & clipboard (13) 🏁 SHIPPED** (counts 2767/2699, both +13 — 13 not the estimated 14: the
  query subsection is raylib.h 997–1016 = 20 functions, but 7 screen/monitor getters (`GetScreenWidth`/`Height`,
  `GetMonitor{Width,Height,Count,RefreshRate}`, `GetCurrentMonitor`) were ALREADY exported in the managed monitor
  section, so only 13 were new — the memory's cached "14" was stale, confirmed by grep). Bound: 6 plain `int`/void getters
  (`GetRenderWidth`/`Height`, `GetMonitorPhysicalWidth`/`Height`, `EnableEventWaiting`/`DisableEventWaiting`), 3
  `Vector2`-by-value returns (`GetMonitorPosition`, `GetWindowPosition`, `GetWindowScaleDPI` — the module's FIRST raw
  `Vector2` returns; plain `As Vector2`, a `<StructLayout(Sequential)>` two-`Single` struct, exactly the pre-existing
  `GetMousePosition` idiom), 1 `Image`-by-value return (`GetClipboardImage`), 2 `const char*` returns (`GetMonitorName`,
  `GetClipboardText`) → raw export `As IntPtr` + a managed `As String` helper doing `PtrToStringAnsi` with an
  `IntPtr.Zero` guard (NEVER an `LPStr String` return — that would free raylib-owned memory), and 1 Ansi string input
  (`SetClipboardText`, `CharSet:=CharSet.Ansi`). Zero wrapper collisions (all 13 plain; the 2 String helpers are also
  collision-free). Duplicate-export guard asserts the 7 already-bound getters each still appear exactly once.
  **Correctness is `[Category("Integration")]` + `[NonParallelizable]`** (needs a real GL window): `InitWindow(320,240)` →
  hide → `GetRenderWidth/Height ≥ GetScreenWidth/Height`, `GetMonitorCount ≥ 1`, current-monitor index in range, positive
  monitor video size, non-negative physical size, `GetWindowScaleDPI` ≥ 1 on both axes (proves the `Vector2` return ABI),
  finite monitor/window positions, a non-empty `GetMonitorName` String, an ASCII clipboard `Set`→`Get` round-trip, and
  `GetClipboardImage`/event-waiting toggles under `DoesNotThrow`. Self-`Ignore`s when headless; on this workstation the
  window WAS created and every assertion ran (fully runtime-verified). The parity guard (3 headless tests incl. a
  type-aware wrapper scan + a raylib.h completeness cross-check over the 997–1016 range) runs in the fast subset.
- **C6 — Timing/frame + Random + Misc + 2 input stragglers (15) 🏁 SHIPPED** (counts 2782/2714, both +15). The 15 span
  four raylib.h groups: frame control (`SwapScreenBuffer`, `PollInputEvents`, `WaitTime`), random (`SetRandomSeed`,
  `GetRandomValue`, `LoadRandomSequence`, `UnloadRandomSequence`), misc (`SetConfigFlags`, `OpenURL`, `TraceLog`,
  `SetTraceLogLevel`, `MemAlloc`, `MemRealloc`), and 2 input stragglers (`SetGamepadVibration` — a raylib-5.5 addition;
  `GetTouchPosition` — `Vector2` return), which are the ONLY rcore input-section functions the engine hadn't already
  bound. 6 timing/misc functions were already exported (`SetTargetFPS`, `GetFrameTime`, `GetTime`, `GetFPS`,
  `TakeScreenshot`, `MemFree`) and are not re-declared. Marshaling: `WaitTime(double)`→`Double`; `unsigned int`
  seed/flags/size→`UInteger`; `LoadRandomSequence`→raylib-heap `int*` bound `As IntPtr` + a managed `LoadRandomSequence`
  `As Integer()` helper that `Marshal.Copy`s then frees via `UnloadRandomSequence`; `MemAlloc`/`MemRealloc`→`IntPtr`
  (freed with `MemFree`); `OpenURL`/`TraceLog` inputs Ansi. ⛔ **`TraceLog` is variadic** — bound as a FIXED 2-arg
  forwarder `TraceLog(level,"%s",text)` that routes the caller's text through `%s`, dodging cross-P/Invoke varargs AND
  format-string injection (the correctness test passes literal `%s %d %x` text and asserts no AV). ⛔ **`WaitTime`
  busy-waits on `GetTime()`, whose time base is only initialized by `InitWindow`** — called headlessly it spins forever
  (1 core, unkillable), so it is exercised ONLY in the integration fixture under a live window, never headlessly.
  ⛔ **`OpenURL` is never invoked at runtime** (it would launch a browser) — parity-guard name/type coverage only.
  **Split correctness:** a HEADLESS fast-subset fixture with real oracles — `SetRandomSeed` determinism (same seed →
  identical sequence), `GetRandomValue` inclusive-range + both-bounds-reachable, `LoadRandomSequence` distinct-in-range
  permutation, a `MemAlloc`→write→`MemRealloc`→read-back round-trip (AVs if the alloc is bogus), and `TraceLog`/
  `SetTraceLogLevel`/`SetConfigFlags` ABI-smoke — PLUS an `[Integration]`+`[NonParallelizable]` fixture that, under a
  hidden window, reads `GetTouchPosition` (finite `Vector2`) and ABI-smokes `PollInputEvents`/`SwapScreenBuffer`/
  `SetGamepadVibration`/`WaitTime`. Parity guard (3 headless tests): name 3-way for all 15, a completeness cross-check
  that {13 non-input}∪{6 already-bound} exactly covers raylib's `Timing..MemFree` range, a SECOND cross-check that the
  ENTIRE rcore input section is now bound (proving only these 2 stragglers remained), the type-aware wrapper scan, and
  the duplicate-export guard.
- **C9 — Directory listing & dropped files (7) 🏁 SHIPPED** (counts 2789/2721, both +7). `LoadDirectoryFiles`,
  `LoadDirectoryFilesEx`, `UnloadDirectoryFiles`, `IsFileDropped`, `LoadDroppedFiles`, `UnloadDroppedFiles`,
  `GetFileModTime`. The headline is the module's **FIRST by-value struct-with-pointer-array**: raylib's
  `FilePathList { unsigned int capacity; unsigned int count; char** paths }` (new struct in `Utiliy.vb`, `<Sequential>`
  {UInteger, UInteger, IntPtr} = 16 B) is RETURNED by value from the three `Load*` and PASSED by value to the two
  `Unload*`. Managed `String()` helpers (`LoadDirectoryFiles`/`Ex`/`LoadDroppedFiles`) walk the `char**` with
  `Marshal.ReadIntPtr(paths, i*IntPtr.Size)` + `PtrToStringAnsi` and copy the strings out BEFORE calling the matching
  `Unload` (which frees them) inside a `Try/Finally`. `LoadDirectoryFiles(Ex)` allocate a fresh list; `LoadDroppedFiles`
  aliases the window's internal drop buffer — both freed via their `Unload`. ⛔ **`GetFileModTime` returns C `long`, which
  is 32-bit on Win64** → bound `As Integer` (a `Long` would misread 8 bytes; a completeness assertion forbids `As Long`).
  `scanSubdirs`/`IsFileDropped` → I1 bool; path inputs Ansi. Zero collisions (all 7 plain + 3 helpers). **Split
  correctness:** HEADLESS fast-subset with a REAL oracle — a temp dir with 3 known files listed and compared
  (`LoadDirectoryFiles` count+names, `LoadDirectoryFilesEx` extension filter), and `GetFileModTime` within 5 s of .NET's
  `File.GetLastWriteTimeUtc` (proves the 32-bit `long`); the local mirror walks the `char**` itself, so a wrong struct
  layout or bad pointer surfaces as garbage/AV. PLUS an `[Integration]`+`[NonParallelizable]` fixture that ABI-smokes the
  `FilePathList` by-value round-trip (`IsFileDropped` false, `LoadDroppedFiles`→empty→`UnloadDroppedFiles`) under a hidden
  window. Parity guard (3 headless): name 3-way, a cross-check that raylib's `LoadDirectoryFiles..GetFileModTime` sub-range
  is exactly the 7, a SECOND cross-check that the ENTIRE file-I/O surface (`LoadFileData..GetFileModTime` = C7+C8+C9) is
  now bound, the type-aware scan (FilePathList by-value, I1, `As Integer` not `Long`, struct field order, helper
  `ReadIntPtr`+`PtrToStringAnsi`+`Unload`-free), and the duplicate-export guard.

- **C4 — Shader management (8) 🏁 SHIPPED** (counts 2797/2729, both +8). `LoadShader`, `LoadShaderFromMemory`,
  `IsShaderValid`, `GetShaderLocationAttrib`, `SetShaderValue`, `SetShaderValueV`, `SetShaderValueMatrix`,
  `SetShaderValueTexture`. raylib's shader family is **10** functions, but **`GetShaderLocation` + `UnloadShader` were
  already exported as raw passthroughs** (the pre-existing `Framework_*` struct-based shader layer), so C4 adds the other
  **8** — no re-declaration, no collision with the typed convenience wrappers (`SetShaderValue{1..4}f/1i`, `LoadShaderF`)
  or the handle-based `Framework_Shader_*` layer. All three structs were already correct: `Shader {Integer id, IntPtr locs}`
  (16 B on x64), `Matrix` (16 `Single` in raylib's scrambled `m0,m4,m8,m12/...` declaration order, from C5), `Texture2D`
  (from rtextures). Marshaling: `Shader` returned/passed BY VALUE; `IsShaderValid`→I1; `const void* value`→**IntPtr** on
  `SetShaderValue(V)` (caller pins/marshals the payload + a `ShaderUniformDataType` enum); `Matrix` BY VALUE (64 B);
  `Texture2D` BY VALUE (20 B); the three `const char*` inputs Ansi. **Split correctness:** the parity guard (3 headless)
  does name 3-way for all 10, a raylib.h cross-check that the `LoadShader..UnloadShader` RLAPI range is exactly those 10,
  a type-aware scan (Shader/Matrix structs, by-value returns/params, I1, void*→IntPtr, Matrix/Texture2D by value, Ansi
  inputs), and a duplicate-export guard on the 8 new names. Correctness is a genuine `[Integration]`+`[NonParallelizable]`
  end-to-end (needs a live GL context): it compiles a custom GLSL-330 fragment shader via `LoadShaderFromMemory`, asserts
  `IsShaderValid`, resolves `GetShaderLocation`/`GetShaderLocationAttrib` (`vertexPosition` is the reliable built-in
  anchor), and drives every setter with real payloads — a float, a vec2, an identity `Matrix` by value, and a **real
  GPU-loaded** `Texture2D` (`GenImageColor`→`LoadTextureFromImage`, so `SetShaderValueTexture` exercises a non-zero id
  rather than raylib's early-return-on-0 no-op). A wrong by-value struct ABI would AV under the setters, not silently pass.

- **C3 — Drawing modes & VR simulator (10) 🏁 SHIPPED** (counts 2807/2739, both +10). `BeginMode3D`, `EndMode3D`,
  `BeginBlendMode`, `EndBlendMode`, `BeginScissorMode`, `EndScissorMode`, `BeginVrStereoMode`, `EndVrStereoMode`,
  `LoadVrStereoConfig`, `UnloadVrStereoConfig`. The drawing-mode/VR block is **16** functions; 6 were already bound
  (`BeginMode2D`/`EndMode2D`, `BeginTextureMode`/`EndTextureMode`, `BeginShaderMode`/`EndShaderMode`) so C3 adds the other
  **10**, no re-declaration. **The module's hardest marshaling**: two brand-new structs with **nested fixed-size arrays**
  (`<MarshalAs(UnmanagedType.ByValArray, SizeConst:=N)>`) — `VrDeviceInfo` (2 int + 5 float + two `float[4]` = 60 B) and
  `VrStereoConfig` (`Matrix[2]` + `Matrix[2]` + six `float[2]` = **304 B, NON-BLITTABLE**). `LoadVrStereoConfig` **returns
  the non-blittable VrStereoConfig BY VALUE** — the sharpest ABI risk in the module (a non-blittable struct return can
  throw `MarshalDirectiveException`). ⭐ **It works on .NET 8**: the by-value return marshals cleanly, and
  `<ByValArray SizeConst:=2> Matrix()` correctly inlines two 64-byte Matrix structs (not a pointer). `Camera3D`/`Matrix`
  already existed; `BeginBlendMode`/`BeginScissorMode` take plain ints. **Split correctness:** a HEADLESS fast-subset
  **canary** proves the non-blittable return JITs + round-trips structurally (raylib zeroes the config with no GL context,
  so it asserts every ByValArray came back at its `SizeConst` length + `UnloadVrStereoConfig` round-trips the by-value
  param) — this is the `MarshalDirectiveException` tripwire and it runs on CI. Then an `[Integration]`+`[NonParallelizable]`
  **value oracle** under a live window: `LoadVrStereoConfig` now computes real data, so it asserts `viewOffset` is a per-eye
  translation matrix — diagonal `== 1`, and `m12 == +IPD/2` (eye 0) / `-IPD/2` (eye 1), **exactly ±0.035** = 0.07·0.5. That
  one assertion proves, in a single shot, (a) `VrDeviceInfo` marshalled IN (the IPD was used), (b) the nested `Matrix[2]`
  marshalled OUT at correct element+field offsets, and (c) the scrambled Matrix field order (`m12` at field index 3). Plus
  the draw-mode calls (`BeginMode3D` Camera3D-by-value, blend/scissor, `BeginVrStereoMode` VrStereoConfig-by-value) run
  inside `BeginDrawing`/`EndDrawing` under `DoesNotThrow`. ⚠ raylib 5.5's eye-sign convention is **eye 0 = +IPD/2** (my first
  guess was flipped; the test caught it — magnitude was exact, only the sign assumption was wrong). Parity guard (3 headless):
  name 3-way for all 16 + raylib.h `BeginMode2D..UnloadVrStereoConfig` range == 16 cross-check + a full-order type-scan of
  both VR structs (every ByValArray SizeConst + field sequence — the headless defense against a struct reorder) + the
  binding types (Camera3D/VrStereoConfig/VrDeviceInfo by value, int params, VrStereoConfig return) + dup-export guard on the 10.

- **C11 — Automation events (8) 🏁 SHIPPED** (counts 2815/2747, both +8). `LoadAutomationEventList`,
  `UnloadAutomationEventList`, `ExportAutomationEventList`, `SetAutomationEventList`, `SetAutomationEventBaseFrame`,
  `StartAutomationEventRecording`, `StopAutomationEventRecording`, `PlayAutomationEvent`. All 8 new (zero pre-existing
  automation bindings). Two new structs: `AutomationEvent` (`{UInteger frame, UInteger type, <ByValArray SizeConst:=4>
  Integer() params}` = 24 B, **non-blittable**, passed by value to PlayAutomationEvent) and `AutomationEventList`
  (`{UInteger capacity, UInteger count, IntPtr events}` = 16 B, **blittable** — the `events` pointer is raylib-owned,
  allocated by Load, freed by Unload; returned/passed by value). ⛔ **The sharpest point: `SetAutomationEventList` takes
  `AutomationEventList*` and raylib RETAINS the pointer** (writes `list->events[list->count++]` during recording), so the
  binding is `IntPtr` — a `ByRef`/by-value binding would marshal a temporary copy that dangles the instant the call returns.
  The caller owns the lifetime: pin the list at a stable address. `params` is a legal VB identifier; `event` (the raylib
  param name) is a VB keyword so PlayAutomationEvent's param is `evt`. **Split correctness:** HEADLESS fast-subset (list mgmt
  + export + play are pure CPU/file) — `LoadAutomationEventList(NULL)` returns an allocated empty list by value (capacity>0
  & <1e6 [rejects a pointer-misread], count==0, events≠NULL → oracle for the 16-B by-value return), `Export` writes a real
  `.rae` file (+I1), and `PlayAutomationEvent` marshals the 24-B non-blittable event by value using a benign type-0 (no-op)
  event; + INTEGRATION (window) records under a **pinned unmanaged `AutomationEventList`** (`AllocHGlobal`+`StructureToPtr`),
  `Set`/`BaseFrame`/`Start`/3 frames/`Stop`, reads the struct back (capacity/events preserved, count==0 empty-run), then
  detaches with `SetAutomationEventList(IntPtr.Zero)` before freeing — proving the retained-pointer contract. Parity guard
  (3 headless): genuine 3-way (header + **framework.cpp forwarder** + wrapper) + raylib.h `LoadAutomationEventList..
  PlayAutomationEvent` range==8 + type-scan of both structs + bindings (IntPtr for Set, I1 Export, by-value list/event,
  Ansi) + dup-export guard.
