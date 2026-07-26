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
| C2 | Window/monitor query & clipboard | 14 | — | device-lite | render/monitor/DPI getters, clipboard (`IntPtr`+`PtrToStringAnsi`). |
| C3 | Drawing modes & VR | 10 | Camera3D✅, Matrix✅, VrDeviceInfo, VrStereoConfig | device | hardest marshaling (VR nested arrays). |
| C4 | Shaders | 8 | Matrix✅ | device | `LoadShader(FromMemory)`, `SetShaderValue*` (`const void*`→IntPtr). |
| **C5** | **Screen-space / camera math** | **8** | **Ray✅, Camera3D✅, Matrix✅** | **headless** | **SHIPPED — see below.** |
| C6 | Timing/frame + Random + Misc + 2 input stragglers | 15 | — | headless (partial) | `TraceLog` bound as fixed 2-arg `(int,const char*)` → `TraceLog(lvl,"%s",text)`. |
| **C7** | **File data I/O** | **7** | — | **headless** | **SHIPPED — see below.** |
| **C8** | **File-system path queries** | **15** | — | **headless** | **SHIPPED — see below.** |
| C9 | Directory listing & dropped files | 7 | FilePathList | headless | `char**` list marshaling. |
| **C10** | **Compression / Encoding** | **7** | — | **headless** | **SHIPPED — see below.** |
| C11 | Automation events | 8 | AutomationEvent, AutomationEventList | device-lite | record/play. |
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
