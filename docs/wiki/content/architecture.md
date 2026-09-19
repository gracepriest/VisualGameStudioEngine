title: Architecture
lede: Two dataflows explain almost everything — one for editing and tooling, one for building and running.
---
The repository has a lot of surface, but only two diagrams. Everything else is detail
hanging off them.

## Editing and tooling

The IDE owns no language logic. It speaks Language Server Protocol and Debug Adapter
Protocol to swappable external processes, which is why BasicLang and C++ get identical
treatment inside it:

```text
              VisualGameStudio IDE (Avalonia shell)
                             |
       +---------------+-----+-----+----------------+
       v               v           v                v
 BasicLang.exe      clangd    BasicLang.exe      lldb-dap
     --lsp         (C++ LSP)  --debug-adapter  (native debug)
 (BasicLang LSP)              (managed debug)
```

Consequences worth internalising:

- **The compiler binary is also the language server and the debug adapter.** One
  executable, four modes — compile, `--lsp`, `--debug-adapter`, and an interactive REPL
  (`--repl` / `-i`). An IntelliSense
  bug is usually a compiler bug.
- **clangd and lldb-dap are downloaded on demand.** `ClangdInstaller` /
  `ClangdLocator` and `LldbDapInstaller` / `LldbDapLocator` in
  `VisualGameStudio.ProjectSystem/Services/` handle acquisition; users install nothing.
- **The adapter registry is open.** `IDebugAdapterRegistry` and
  `ILanguageServiceRegistry` let a language register a server and an adapter, which is
  how C++ arrived as a peer rather than a special case.

## Building and running

```text
BasicLang source (.bas / .cls / .mod)  +  hand-written C++
        |
        +-- C# backend  --> .NET assembly --> RaylibWrapper.vb (P/Invoke) --+
        |                                                                  |
        +-- C++ backend --> clang/gcc/msvc --> native .exe (links .lib) ---+
        |        |                                                         |
        |        +-- with <NetProxy>: Native-AOT .NET shim DLL             |
        |           beside the exe, bound at run time over the blnet ABI   |
        |                                                                  |
        +-- javascript / llvm / msil: real targets, no engine path         |
                                                                           |
                                                                           v
                                                    VisualGameStudioEngine.dll
                                                       (Raylib, stable C ABI)
```

The managed path reaches the engine through P/Invoke declarations in
`RaylibWrapper/RaylibWrapper.vb`; the native path links the import library directly.
On a successful build the compiler injects the wrapper reference and deploys the native
DLL for game apps, so neither path asks the user to wire anything up. A native project
that declares `<NetProxy>` items gets a *second* deployed DLL — the Native-AOT .NET shim
— copied beside the executable by the same build step.

The other three backends (`javascript`, `llvm`, `msil`) are real targets with real output,
but none of them reaches the engine; see [Backends](#/backends) for what each one is for
and which are maintained.

## The layers, top to bottom

<div class="pipe">
<div class="pipe-step"><span class="n">L1</span><span class="s">Shell</span><span class="f">VisualGameStudio.Shell</span></div>
<div class="pipe-step"><span class="n">L2</span><span class="s">Editor</span><span class="f">VisualGameStudio.Editor</span></div>
<div class="pipe-step"><span class="n">L3</span><span class="s">Services</span><span class="f">…ProjectSystem</span></div>
<div class="pipe-step"><span class="n">L4</span><span class="s">Abstractions</span><span class="f">VisualGameStudio.Core</span></div>
<div class="pipe-step"><span class="n">L5</span><span class="s">Compiler</span><span class="f">BasicLang</span></div>
<div class="pipe-step"><span class="n">L6</span><span class="s">Engine</span><span class="f">VisualGameStudioEngine</span></div>
</div>

`Core` holds interfaces and models and depends on nothing above it. `ProjectSystem`
implements those interfaces. `Editor` is a *library* — an Avalonia code editor control,
not an application. `Shell` composes all of it and is the build and run target. Getting
this backwards ("build the Editor project to run the IDE") is a common first mistake.

> [note] The stack is an ordering, not a dependency chain. `Editor` references only
> `Core`, never `ProjectSystem`; `ProjectSystem` references `Core` **and** `BasicLang`
> directly; `Core` has no `ProjectReference` at all; and no IDE project references the
> engine at build time — `VisualGameStudioEngine.dll` is loaded at run time, and the only
> `.vcxproj` reference in the tree is the `TestVbDLL` sample's.

## Three boundaries that do the heavy lifting

### The C ABI

`VisualGameStudioEngine/framework.h` declares every engine entry point as
`extern "C"` — one block, `framework.h:275–5029`, over all 2,913 exports — and C types
only: handles are `int`, strings are `const char*`, 367 exports pass or return raylib's
POD structs (`Vector2`, `Rectangle`, `Texture2D`, `Color`, `Mesh`, `RayCollision`, …)
**by value**, and 89 take a C function pointer as a callback. No C++ types cross the
boundary, which is exactly why VB.NET, C#, C++ and BasicLang can all call it. The price
is the [sync invariant](#/engine-binding) between the header and the wrapper.

> [trap] **The header never writes `__cdecl`.** The convention is asserted only on the
> managed side — `CallingConvention:=CallingConvention.Cdecl` on every one of the 2,845
> `<DllImport>` declarations in `RaylibWrapper/RaylibWrapper.vb`. Nothing in
> `framework.h` states a calling convention at all, so a new consumer that assumes the
> platform default is relying on luck, not on a contract.

### The IR

Every backend consumes the same intermediate representation built by `IRBuilder.cs`,
optimised by `IROptimizer.cs`. A language feature is "done" when it lowers to IR; the
backends then compete on how faithfully they reproduce it. This is why
[a fix must be validated through the optimizer](#/conventions), not just the unit-test
helper that skips it.

### The blnet ABI

Since **P2a-2** a native project can reach .NET types, and it does so across a *second*
stable C ABI rather than by linking anything. `BlnetContract.cs` pins exactly seven core
exports — `blnet_abi_version`, `blnet_initialize`, `blnet_addref`, `blnet_release`,
`blnet_alloc`, `blnet_free`, `blnet_last_error` — behind a version handshake that fails
with `BLNET_E_VERSION_MISMATCH`. The managed half is generated per project
(`NetShimGenerator.cs`), published by `dotnet publish -p:PublishAot=true
-p:NativeLib=Shared` into a self-contained native DLL (`NetShimPublisher.cs`), deployed
beside the executable, and bound at run time by `blnet_bind_core` over `LoadLibraryA` /
`dlopen`. `blnet_facade.g.hpp` (`NetProxyEmitter.Facade.cs`) renders those mangled slots
as ergonomic C++. Details in [.NET interop](#/net-interop).

> [note] Adding an eighth export to that list is an ABI change and bumps the version —
> the same discipline the engine's C ABI gets, for the same reason.

## Where state lives

| Kind of state | Home |
|---|---|
| Project and solution definition | `.blproj` / `.blsln` on disk — see [Projects](#/projects) |
| Per-project window layout, open documents | Workspace state store — user-local, `~/.vgs/workspaceStorage` keyed by project path; does not travel with the project |
| Breakpoints, bookmarks | JSON in the project tree — `{projectDir}/.vgs/breakpoints.json`, `.vgs/bookmarks.json` |
| Watches | In memory only — not persisted between sessions |
| Toolchain paths and preferences | Settings service, `Settings → C++` etc. |
| Compiler symbol tables | Rebuilt per compilation; the LSP keeps a document cache |
| Engine runtime state | Entirely inside the native DLL, reachable through introspection exports |
