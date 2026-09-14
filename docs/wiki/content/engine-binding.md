title: Engine ⇄ wrapper
lede: The invariant that keeps VB.NET in step with a C header that has thousands of exports and keeps moving.
---
## The rule

**Every `__declspec(dllexport)` in `VisualGameStudioEngine/framework.h` needs a matching
`<DllImport>` in `RaylibWrapper/RaylibWrapper.vb`.**

The declaration must agree on all four of these:

1. `extern "C"` linkage — no C++ name mangling.
2. `__cdecl` calling convention.
3. `LPStr` string marshaling for `const char*`.
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
> The two numbers are not required to be equal — overloads, helper declarations and
> commented regions account for some of the difference — but a *growing* gap after a
> header change is the signal that a batch of exports was never wrapped.

## Adding an engine function

1. Declare it in `framework.h` inside the right section banner, `extern "C"`,
   `__declspec(dllexport)`, plain C types only.
2. Implement it in `framework.cpp`.
3. Add the matching `<DllImport>` to `RaylibWrapper.vb` with `CallingConvention.Cdecl`
   and `CharSet`/`MarshalAs(UnmanagedType.LPStr)` for any string.
4. Mirror any new enum or constant on both sides.
5. Rebuild the engine through VS 2022 MSBuild (`VisualGameStudioEngine.vcxproj`, x64/Release).
6. Smoke-test from both directions: `CPPengineTest/` natively, `TestVbDLL/` managed.

## Deployment

On a successful build the compiler auto-injects the wrapper reference and deploys the
native DLL for game applications. Implementation:
`BasicLang/ProjectSystem/EngineDeployment.cs` and `CppRuntimeDeployment.cs`.

> [trap] `IDE/` is a hand-committed xcopy drop, and **the engine DLL and its import
> library live only there**. Refresh with `robocopy <Shell bin> IDE /E` — never `/MIR`,
> which deletes them.

## Test projects

| Project | Language | What it proves |
|---|---|---|
| `CPPengineTest/` | C++ | The DLL works natively — smoke test and game loop |
| `TestVbDLL/` | VB.NET | The wrapper works — a full sample game, plus `FrameworkTests.vb` smoke-testing exports |
| `TestVbDLL/SampleA_FrameworkOnly.vb` | VB.NET | "Catch the Falling Blocks" — window, loop, input, drawing, pause/resume |

`EngineAgent/` is the Claude Agent SDK application dedicated to keeping this sync
maintained; its `CLAUDE.md` documents the profiles and tools it uses.

## Types that cross the boundary

| C side | VB.NET side | Note |
|---|---|---|
| `int` (handle) | `Integer` | Entities, textures, sounds, timers — everything is a handle |
| `float` / `double` | `Single` / `Double` | |
| `bool` | `Boolean` | Marshalled as the platform default |
| `const char*` | `String` | `MarshalAs(UnmanagedType.LPStr)` |
| `unsigned char` r/g/b/a | `Byte` ×4 | Colours are four separate parameters, not a struct |
| `Transform2DData` etc. | `Structure` | The few ABI-safe PODs, by value |
| function pointer typedefs | `Delegate` | Keep a managed reference alive or it will be collected |

> [note] The last row is a real hazard. A delegate passed to `Framework_Ui_SetCallback`
> or a scene callback must be rooted on the managed side for as long as native code can
> call it, or the GC will collect it and the next callback will crash.
