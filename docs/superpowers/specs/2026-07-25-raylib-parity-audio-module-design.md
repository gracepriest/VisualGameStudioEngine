# raylib 5.5 parity — raudio module (full raw parity) — Design-of-record

**Date:** 2026-07-25 · **Status:** design-of-record · **Scope decision:** user chose FULL raw raudio parity
(coexisting with the engine's pre-existing custom `Framework_Audio_*` high-level layer — groups/spatial/pools/
playlists/crossfade + handle-based `…H` sound/music). This adds the raw raylib-named API as a second, lower-level
surface. Follows [[raylib-parity-fileio-export-image-to-memory]]; built from the `.worktrees/raylib` worktree.

## Surface: 66 raudio fns (raylib.h 1627–1702)
- **Device (5):** InitAudioDevice, CloseAudioDevice, IsAudioDeviceReady, SetMasterVolume, GetMasterVolume.
- **Wave/Sound load (13):** LoadWave, LoadWaveFromMemory, IsWaveValid, LoadSound, LoadSoundFromWave,
  LoadSoundAlias, IsSoundValid, UpdateSound, UnloadWave, UnloadSound, UnloadSoundAlias, ExportWave, ExportWaveAsCode.
- **Wave/Sound mgmt (13):** PlaySound, StopSound, PauseSound, ResumeSound, IsSoundPlaying, SetSoundVolume,
  SetSoundPitch, SetSoundPan, WaveCopy, WaveCrop, WaveFormat, LoadWaveSamples, UnloadWaveSamples.
- **Music (16):** LoadMusicStream, LoadMusicStreamFromMemory, IsMusicValid, UnloadMusicStream, PlayMusicStream,
  IsMusicStreamPlaying, UpdateMusicStream, StopMusicStream, PauseMusicStream, ResumeMusicStream, SeekMusicStream,
  SetMusicVolume, SetMusicPitch, SetMusicPan, GetMusicTimeLength, GetMusicTimePlayed.
- **AudioStream (19):** LoadAudioStream, IsAudioStreamValid, UnloadAudioStream, UpdateAudioStream,
  IsAudioStreamProcessed, PlayAudioStream, PauseAudioStream, ResumeAudioStream, IsAudioStreamPlaying,
  StopAudioStream, SetAudioStreamVolume, SetAudioStreamPitch, SetAudioStreamPan, SetAudioStreamBufferSizeDefault,
  **SetAudioStreamCallback, AttachAudioStreamProcessor, DetachAudioStreamProcessor, AttachAudioMixedProcessor,
  DetachAudioMixedProcessor** (last 5 = `AudioCallback` fn-pointers → **DEFER**, need delegate-marshaling design).

**Net this module = 61 passthrough + 5 callback-deferred.**

## Structs (4 new; all blittable)
```c
Wave        { u32 frameCount, sampleRate, sampleSize, channels; void* data; }              // 24 B (x64)
AudioStream { rAudioBuffer* buffer; rAudioProcessor* processor; u32 sampleRate,sampleSize,channels; } // 24 B
Sound       { AudioStream stream; u32 frameCount; }                                        // 32 B
Music       { AudioStream stream; u32 frameCount; bool looping; int ctxType; void* ctxData; } // 40 B
```
VB mirrors: `<StructLayout(LayoutKind.Sequential)>`; pointers → `IntPtr`; `looping As Boolean` with
`<MarshalAs(UnmanagedType.I1)>` (C bool = 1 byte). Returned BY VALUE (proven pattern: Texture2D/Image/Font/Color).

## Marshaling conventions (reuse the module's established rules)
- **Struct returns BY VALUE** (Wave/Sound/Music/AudioStream) — single DllImport `As <Struct>`; struct params by value.
- **`Wave*` (WaveCrop, WaveFormat)** → `ByRef wave As Wave` (the shipped ByRef-struct mutation contract).
- **String inputs** (fileName/fileType) → `As String` + `CharSet:=CharSet.Ansi`.
- **`const void* data`** (UpdateSound, UpdateAudioStream) → `data As IntPtr` + count (caller pins its own buffer;
  no engine copy). **`float* LoadWaveSamples`** (malloc, freed by UnloadWaveSamples) → return `IntPtr` +
  `Framework_UnloadWaveSamples(IntPtr)` (mirrors the ExportImageToMemory IntPtr+free pattern; NOT a static buffer).
- **`bool` returns** (Is*/Export*) → `As Boolean`. **ExportWave/ExportWaveAsCode** → bool + string filename.
- **⛔ Name collisions — engine C-ABI is clean; the VB wrapper is NOT (corrected in A2).** The raw engine exports
  (`Framework_InitAudioDevice`/`LoadSound`/`PlaySound`) never collide with the engine's H-suffix layer
  (`Framework_LoadSoundH`, `Framework_Audio_*`), so framework.h/.cpp always use the plain raylib names. BUT
  `RaylibWrapper.vb` has a legacy **"Sound/Music Convenience Functions"** region of *managed* helpers (regular VB,
  not DllImports) that squat some plain names as handle-based (`As Integer`). A raw import with the same name →
  BC30301 (differ only by return type) or a confusing dual-dispatch overload. **CONVENTION:** for each squatted name,
  the raw struct binding takes a **`Raw` suffix** and binds its export via `<DllImport(EntryPoint:="Framework_<rawname>")>`;
  the engine ABI stays unsuffixed. Non-squatted names keep raylib's exact spelling. **Grep `RaylibWrapper.vb` for
  `(Function|Sub)\s+Framework_<name>\b` per name before adding** — do not trust a prediction; check the actual name.
  - **A2 (Sound) HIT it:** `Framework_LoadSound`/`PlaySound`/`StopSound`/`SetSoundVolume` are squatted → those four
    took the `Raw` suffix + EntryPoint; the other 11 kept raylib's spelling.
  - **A3 (Music) did NOT (prediction was wrong about the mechanism).** The convenience region squats the *un-suffixed*
    `Framework_LoadMusic`/`PlayMusic`/`StopMusic`, but raylib's **raw** Music names all carry a **`Stream`** suffix
    (`LoadMusicStream`/`PlayMusicStream`/`StopMusicStream`), so they are distinct names — **zero collisions**, all 16
    bind unsuffixed with raylib's exact spelling. (`SetMusicVolume`/`SetMusicPitch` only collide with the H-suffixed
    `…VolumeH`/`…PitchH`, which are different names too.) The per-name grep is what caught this — heed it over memory.

## Decomposition — 4 auto-merged sub-batches (each: parity guard + correctness + build + ff-push)
- **A1 — Device + Wave (16):** 5 device + 11 Wave (LoadWave, LoadWaveFromMemory, IsWaveValid, UnloadWave,
  ExportWave, ExportWaveAsCode, WaveCopy, WaveCrop, WaveFormat, LoadWaveSamples, UnloadWaveSamples). Defines the
  **Wave** struct. Wave-data fns are **HEADLESS** (pure PCM in RAM) → strong correctness tests (gen a Wave, crop/
  format/copy, read back frameCount/sampleRate; LoadWaveSamples float round-trip). Device fns verified in the smoke.
- **A2 — Sound (15):** LoadSound, LoadSoundFromWave, LoadSoundAlias, IsSoundValid, UpdateSound, UnloadSound,
  UnloadSoundAlias, PlaySound, StopSound, PauseSound, ResumeSound, IsSoundPlaying, SetSoundVolume, SetSoundPitch,
  SetSoundPan. Defines **AudioStream + Sound** structs. Playback needs a device → smoke; struct-marshaling headless.
- **A3 — Music (16):** the full Music group. Defines **Music** struct (adds a `looping` C bool → I1, plus a
  ctxType/ctxData decoder tail). Needs a device → device-backed correctness + smoke. **🏁 SHIPPED** (counts 2679/2611).
  Two Music-specific facts encoded in the test: (1) the stream keeps the SOURCE format — the mixer resamples per-buffer
  (LoadSoundFromWave, by contrast, resamples to the device up-front); (2) raylib **streams from the caller's buffer**
  (`drwav_init_memory` keeps a pointer, no copy) → the input bytes must stay pinned for the whole Music lifetime.
- **A4 — AudioStream (14):** the non-callback AudioStream fns. Needs a device → smoke. The 5 callback fns stay
  deferred to a dedicated "audio callbacks" batch (delegate/GC-pinning design).

## Verification (per sub-batch)
- **Parity guard** (headless text scan) per sub-batch — the sub-batch's names present as `Framework_<name>(` in
  both framework.h and RaylibWrapper.vb.
- **Headless correctness** (Integration, local DllImport, `RWave`/`RSound`/… mirrors, `Guard<T>` self-skip):
  Wave-data ops (A1) fully; struct-marshaling round-trips (A2–A4, without asserting device playback).
  ⚠ stage fresh DLL into IDE\ + beside the test host or it self-skips → false green.
- **`TestVbDLL --audio` smoke** (device-dependent playback across A1–A4): InitAudioDevice → IsAudioDeviceReady,
  load a generated/short asset, PlaySound / PlayMusicStream + UpdateMusicStream loop, PlayAudioStream with a
  generated sine buffer; `[audio] PASS/FAIL`. The user's audible/exit-code checkpoint (mirrors `--textures3d`).
  ⚠ headless NUnit host may lack an audio device → device fns can't be asserted there; that's what the smoke is for.

## Counts
Start 2634/2566. A1 +16 → 2650/2582 · A2 +15 → 2665/2597 · A3 +16 → 2681/2613 · A4 +14 → 2695/2627.
(Callbacks deferred: −5 vs the full 66.)
