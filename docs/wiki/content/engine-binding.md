title: Engine ⇄ wrapper
lede: The invariant that keeps VB.NET in step with a C header that has thousands of exports and keeps moving.
---
## The rule

**Every `__declspec(dllexport)` in `VisualGameStudioEngine/framework.h` needs a matching
`<DllImport>` in `RaylibWrapper/RaylibWrapper.vb`.**

The declaration must agree on all four of these:

1. `extern "C"` linkage — no C++ name mangling.
2. `__cdecl` calling convention — note `framework.h` never spells it (0 occurrences); it inherits
   the vcxproj's default convention, while all 2,845 `DllImport`s state `CallingConvention.Cdecl`
   explicitly.
3. `LPStr` string marshaling for `const char*` — in practice either `CharSet:=CharSet.Ansi` on the
   `DllImport` (337 declarations) or an explicit `<MarshalAs(UnmanagedType.LPStr)>` on the
   parameter/return (49 sites).
4. Parameter and return types, exactly.

## Why it matters

The C# backend reaches the engine through this wrapper. A missing `DllImport` is not a
compile error in the engine or in the wrapper — it is a function that simply does not
exist for managed callers, discovered at runtime or, worse, never. A *wrong* signature is
worse still: it links, calls, and corrupts the stack.

The native backend links the import library directly and does not use the wrapper, which
means **a drift between header and wrapper shows up only on the managed path.** A feature
can look fine in a C++ build and be missing in a C# one.

## The counts drift

At the time of writing, `framework.h` has **2,913** `__declspec(dllexport)` declarations
and `RaylibWrapper.vb` has **2,845** `DllImport` attributes.

> [trap] **Never trust a cached export count — not this one either.** The surface is in the
> thousands and moves. Grep both sides before you reason about the gap:
>
> ```powershell
> (Select-String -Path VisualGameStudioEngine/framework.h -Pattern '__declspec\(dllexport\)').Count
> (Select-String -Path RaylibWrapper/RaylibWrapper.vb -Pattern 'DllImport').Count
> ```
>
> The two numbers are not required to be equal, and subtracting them badly understates the
> drift. Diff the two *name* sets instead, resolving `EntryPoint:=` aliases first. As measured
> today: **126** exported functions have no `DllImport` at all — whole subsystems, not strays
> (24 `Framework_Inventory_*`, 17 `Framework_Steer_*`, 14 `Framework_Skeleton_*`, 11 each of
> `Framework_Item_*` and `Framework_Equipment_*`, 8 `Framework_NavGrid_*`, 4 `Framework_Atlas_*`,
> and all four `Framework_UI_Set*Callback`) — while **61** `DllImport`s bind an entry point
> `framework.h` does not export (`Framework_Inventory_IsFull`, `Framework_Equipment_Clear`,
> `Framework_Shader_GetDefault`, …), each an `EntryPointNotFoundException` the first time managed
> code calls it. 2,784 names line up. Neither overloads nor commented regions explain any of it:
> all 2,845 `DllImport` entry points are distinct, `framework.h` has **0** commented-out exports
> and only 3 duplicated declarations (`Framework_Ecs_DestroyEntity`, `Framework_Ecs_SetEnabled`,
> `Framework_Prefab_Instantiate`, each declared twice). A *growing* gap after a header change is
> still the signal that a batch of exports was never wrapped.

## Adding an engine function

1. Declare it in `framework.h` inside the right section banner, `extern "C"`,
   `__declspec(dllexport)`. Plain C types for new `Framework_*` APIs; the raylib passthrough
   regions are the standing exception and pass raylib PODs by value (`Vector2`, `Image`,
   `Rectangle`, `Sound`, …), mirrored as `<StructLayout>` types in `RaylibWrapper/Utiliy.vb`.
2. Implement it in `framework.cpp`.
3. Add the matching `<DllImport>` to `RaylibWrapper.vb` with `CallingConvention.Cdecl`
   and `CharSet`/`MarshalAs(UnmanagedType.LPStr)` for any string.
4. Mirror any new enum or constant on both sides.
5. Rebuild the engine through VS 2022 MSBuild (`VisualGameStudioEngine.vcxproj`, x64/Release).
6. Smoke-test from both directions: `CPPengineTest/` natively, `TestVbDLL/` managed, and run the
   headless binding suite —
   `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~VisualGameStudio.Tests.Native"`.

## Deployment

On a successful build the compiler auto-injects the wrapper reference and deploys the
native DLL for game applications. Implementation:
`BasicLang/ProjectSystem/EngineDeployment.cs` — it injects the `RaylibWrapper` reference, copies
`VisualGameStudioEngine.dll` next to the game, and `LocateImportLib()` hands
`VisualGameStudioEngine.lib` to `BasicLang/ProjectSystem/CppProjectBuilder.cs` for the native link.
(`CppRuntimeDeployment.cs` is a separate job: it copies the MinGW C++ runtime —
`libstdc++-6.dll`, `libgcc_s_seh-1.dll`, `libwinpthread-1.dll` — and is a no-op under MSVC.)

> [trap] `IDE/` is a hand-committed xcopy drop and **no build step regenerates the engine DLL or
> its import library there**. Refresh with `robocopy <Shell bin> IDE /E` — never `/MIR`,
> which deletes them. The only other committed copies are `TestVbDLL/`'s
> (`VisualGameStudioEngine.dll` / `.lib` / `.exp` plus `raylib.dll`), kept in step by hand too.

## Test projects

| Project | Language | What it proves |
|---|---|---|
| `CPPengineTest/` | C++ | The DLL works natively — smoke test and game loop |
| `TestVbDLL/` | VB.NET | The wrapper works — a full sample game, plus `FrameworkTests.vb` smoke-testing exports |
| `TestVbDLL/SampleA_FrameworkOnly.vb` | VB.NET | "Catch the Falling Blocks" — window, loop, input, drawing, audio, pause/resume |
| `TestVbDLL/SampleShapesBatch1.vb`, `SampleTextBatch2.vb`, `SampleTextures3d.vb` | VB.NET | Windowed smoke scenes for the raylib 5.5 shapes / text / textures bindings — the GL-dependent struct-by-value paths a headless suite cannot cover |
| `VisualGameStudio.Tests/Native/` | C# | The headless binding suite, and the real guard on this invariant — 80 files, 77 `[TestFixture]`s, 245 `[Test]` methods (31 files gated `[Category("Integration")]`) |

`EngineAgent/` is the Claude Agent SDK application dedicated to keeping this sync
maintained; its `CLAUDE.md` documents the profiles and tools it uses.

## Types that cross the boundary

| C side | VB.NET side | Note |
|---|---|---|
| `int` (handle) | `Integer` | Entities, textures, sounds, timers — everything is a handle |
| `float` / `double` | `Single` / `Double` | |
| `bool` | `Boolean` | **Never the default.** Every `DllImport`ed Boolean param and return carries an explicit `<MarshalAs(UnmanagedType.I1)>` (499 attribute sites across 491 declarations) — the P/Invoke default for `Boolean` is the 4-byte Win32 `BOOL`, which does not match a 1-byte C++ `bool` |
| `const char*` | `String` | `MarshalAs(UnmanagedType.LPStr)` |
| `unsigned char` r/g/b/a | `Byte` ×4 | The house style for `Framework_*` — 201 exports splay colour into four params. But 14 raylib passthroughs still use the struct: 12 return `Color` by value (`Framework_GetImageColor`, `Framework_Fade`, `Framework_ColorFromNormalized`, `Framework_GetColor`, …) and 2 fill a `Color*` buffer (`Framework_LoadImageColors`, `Framework_LoadImagePalette`) |
| raylib PODs (`Vector2`, `Image`, `Rectangle`, `Texture2D`, `Font`, `Sound`, `Color`, …) | `Structure` | By value, and not "a few": `Vector2` appears in 89 export signatures, `Image` in 86, `Rectangle` 29, `Texture2D` 23. The 36 `<StructLayout>` mirrors live in `RaylibWrapper/Utiliy.vb`, **not** `RaylibWrapper.vb` |
| `SceneCallbacks` | `Structure` | The one hand-rolled struct passed by value, to `Framework_CreateScriptScene`. The "ABI-safe introspection" structs `Transform2DData` / `Velocity2DData` / `BoxCollider2DData` are declared in `framework.h` but used by no export |
| function pointer typedefs | `Delegate` | Keep a managed reference alive or it will be collected |

> [note] The last row is a real hazard. A delegate passed to `Framework_SetDrawCallback`
> or a scene callback must be rooted on the managed side for as long as native code can
> call it, or the GC will collect it and the next callback will crash.
