# raylib 5.5 parity — File-I/O straggler: `ExportImageToMemory` (+ `MemFree`) — Design

**Date:** 2026-07-25
**Status:** design-of-record
**Follows:** the textures module (3a/3b/3c-i/3c-ii/3d) — this clears the lone deferred textures fn.

## Goal

Bind raylib 5.5's `ExportImageToMemory` — the one textures function deferred from Batch 3b as
"unknown-size `unsigned char*` return → file-I/O batch" — plus the `MemFree` primitive it requires,
so a VB caller can encode an in-RAM `Image` to PNG/BMP/etc. bytes in memory and free them without leaking.

## Exact raylib signatures

```c
RLAPI unsigned char *ExportImageToMemory(Image image, const char *fileType, int *fileSize); // rtextures — malloc'd, MemFree()
RLAPI void MemFree(void *ptr);                                                              // rmem — raylib's internal free
```

`ExportImageToMemory` encodes `image` (CPU RAM) to the container named by `fileType` (`".png"`, `".bmp"`,
`".qoi"`, …) using stb_image_write — **no GL context, no window** → headless. It returns a heap buffer of
`*fileSize` bytes that the caller must release with `MemFree` (raylib's allocator; there is no dedicated
`Unload*` for it, unlike `LoadFileData`).

## Marshaling decision

**IntPtr + `ByRef size` + `Marshal.Copy` + `MemFree` passthrough.** Rejected alternatives:

- **Static engine buffer** (the `Framework_TextReplace` string trick): encoded images can be multi-MB;
  a fixed `char[8192]` is fragile and truncating. The 3b spec explicitly rejected this for this fn.
- **Caller-buffer + size probe** (the `Framework_LoadImageColors` pattern): the encoded size is unknowable
  without actually encoding, so a probe-then-fill sequence **double-encodes** (zlib/deflate runs twice).

The chosen shape is the idiomatic .NET-interop pattern for a raylib malloc-return and encodes exactly once.
`MemFree` is a genuine raylib `rmem` public export, so binding it is faithful parity — and it is the reusable
free primitive for the upcoming file-I/O / compression module (`LoadFileData`, `CompressData`,
`DecompressData`, `EncodeDataBase64` all document "memory must be `MemFree()`").

## Engine exports (framework.h / framework.cpp)

```cpp
__declspec(dllexport) unsigned char* Framework_ExportImageToMemory(Image image, const char* fileType, int* fileSize);
__declspec(dllexport) void            Framework_MemFree(void* ptr);
```
```cpp
unsigned char* Framework_ExportImageToMemory(Image image, const char* fileType, int* fileSize) {
    return ExportImageToMemory(image, fileType, fileSize);   // returns raylib's malloc'd buffer; caller frees via Framework_MemFree
}
void Framework_MemFree(void* ptr) { MemFree(ptr); }
```

## Wrapper bindings (RaylibWrapper.vb)

```vb
<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
Public Function Framework_ExportImageToMemory(image As Image, fileType As String, ByRef fileSize As Integer) As IntPtr
End Function

<DllImport(ENGINE_DLL, CallingConvention:=CallingConvention.Cdecl)>
Public Sub Framework_MemFree(ptr As IntPtr)
End Sub
```

Caller pattern: `Dim p = Framework_ExportImageToMemory(img, ".png", size)` → `Marshal.Copy(p, bytes, 0, size)`
→ `Framework_MemFree(p)`.

## Verification (2 surfaces — HEADLESS, no GUI smoke)

1. **Parity guard** (`RaylibExportImageMemoryParityTests.cs`, headless text scan): `ExportImageToMemory` and
   `MemFree` each appear as `Framework_<name>(` in **both** framework.h and RaylibWrapper.vb; batch length == 2.
2. **Correctness** (`RaylibExportImageToMemoryTests.cs`, `[Category("Integration")]`, local `[DllImport]` +
   `RImage`/`RColor` mirrors, `Guard<T>` self-skip on stale DLL):
   - Build a known `Image` via the shipped `Framework_GenImageColor(w,h,color)` (3b).
   - `.png` export → `fileSize > 0`, ptr ≠ 0, first 8 bytes == PNG magic `89 50 4E 47 0D 0A 1A 0A`; then `MemFree`.
   - `.bmp` export → **ptr == 0, fileSize == 0** — raylib's `ExportImageToMemory` is **PNG-only** (only the stb
     PNG to-memory writer is wired up; non-PNG names return NULL by design). Faithful passthrough documents the
     real contract; this is intended behavior, **not** a bug to correct (contrast the TextInsert upstream bug).
   - Free the `Image` via `Framework_UnloadImage`. The PNG case proves **real encoding**, not just non-null.

   ⚠ Stage a fresh `x64\Release\VisualGameStudioEngine.dll`+`.lib` into `IDE\` before running, or the test
   self-skips on `EntryPointNotFound` and falsely reads green — confirm **Passed**, not **Skipped**.

## Counts

framework.h `__declspec(dllexport)` 2632 → **2634** (+2); RaylibWrapper.vb `<DllImport(` 2564 → **2566** (+2).

## Out of scope (defer to the file-I/O / compression module)

`LoadFileData`/`UnloadFileData`/`SaveFileData`/`ExportDataAsCode`, `CompressData`/`DecompressData`,
`EncodeDataBase64`/`DecodeDataBase64`, `MemAlloc`/`MemRealloc`. Named here only to bound this batch to the two fns above.
