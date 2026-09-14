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
  executable, three modes, selected by `--lsp` and `--debug-adapter`. An IntelliSense
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
                                                                           |
                                                                           v
                                                    VisualGameStudioEngine.dll
                                                       (Raylib, stable C ABI)
```

The managed path reaches the engine through P/Invoke declarations in
`RaylibWrapper/RaylibWrapper.vb`; the native path links the import library directly.
On a successful build the compiler injects the wrapper reference and deploys the native
DLL for game apps, so neither path asks the user to wire anything up.

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

## Two boundaries that do the heavy lifting

### The C ABI

`VisualGameStudioEngine/framework.h` declares every engine entry point as
`extern "C"` with `__cdecl` and plain C types — handles are `int`, strings are
`const char*`. No C++ types cross the boundary, which is exactly why VB.NET, C#, C++
and BasicLang can all call it. The price is the [sync invariant](#/engine-binding)
between the header and the wrapper.

### The IR

Every backend consumes the same intermediate representation built by `IRBuilder.cs`,
optimised by `IROptimizer.cs`. A language feature is "done" when it lowers to IR; the
backends then compete on how faithfully they reproduce it. This is why
[a fix must be validated through the optimizer](#/conventions), not just the unit-test
helper that skips it.

## Where state lives

| Kind of state | Home |
|---|---|
| Project and solution definition | `.blproj` / `.blsln` on disk — see [Projects](#/projects) |
| Per-project window layout, open documents | Workspace state store, restored on reopen |
| Breakpoints, bookmarks, watches | Workspace state store, persisted per project |
| Toolchain paths and preferences | Settings service, `Settings → C++` etc. |
| Compiler symbol tables | Rebuilt per compilation; the LSP keeps a document cache |
| Engine runtime state | Entirely inside the native DLL, reachable through introspection exports |
