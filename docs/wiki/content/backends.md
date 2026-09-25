title: Backends
lede: Five targets, two of them maintained — and the differences between them that actually bite.
---
| Target | Flag | State | Output |
|---|---|---|---|
| C# | `--target=csharp` | <span class="pill ok">Maintained</span> | .NET assembly, full BCL interop |
| C++ | `--target=cpp` | <span class="pill ok">Maintained</span> | Native executable via clang / gcc / MSVC, `-std=c++20` |
| JavaScript | `--target=javascript` (or `js`) | <span class="pill warn">Active</span> | ES modules plus an HTML page |
| LLVM | `--target=llvm` | <span class="pill mute">Unmaintained</span> | LLVM IR |
| MSIL | `--target=msil` | <span class="pill mute">Unmaintained</span> | MSIL |

> [note] LLVM and MSIL are explicitly **out of scope**: do not test them, fix them, or
> file bugs against them. They still build, which is the only promise made.

## What each backend does differently

### C#

The primary managed target. Emits C# source, compiled to a .NET assembly. Full .NET
interop: `Using` pulls in real namespaces, and .NET types flow through as themselves.
Game projects get a `RaylibWrapper` reference injected automatically so engine calls
resolve.

Its distinguishing hazard is control-flow reconstruction. The backend rebuilds `If` /
`Else` from **block names** (`.then`, `.else`, `{prefix}.end`), so a CFG built with
different names silently emits both arms and no merge. Match `Visit(IfStatementNode)`
exactly when you add a new construct.

### C++

Emits C++20, compiled by clang, gcc or MSVC (selectable per project), linking the engine
import library. Control flow is `goto`-based, so it tolerates any CFG shape — which also
means a CFG bug that breaks C# can pass silently here.

Key semantic decisions, all deliberate:

- Classes and interfaces → `std::shared_ptr<T>` with `make_shared` and `->`.
- `Structure` → a value `struct`.
- Collections → `std::shared_ptr<BasicLang::List<T>>` — **reference** semantics to match
  .NET. Value wrappers were tried, diverged from .NET, and were wrong.
- `String` and structs stay values.
- Generics → real C++ templates.
- Iterators → real C++20 coroutines (`Generator<T>` / `co_yield`).
- Async → synchronous `Task<T>` emulation; there is no scheduler.
- Exceptions → the `IRThrow` node. A `Return` or `Exit` out of a `Try` runs its `Finally`: the
  jump carries its own copy of every `Finally` it leaves.

See [C++ backend and interop](#/cpp-interop) for `#CppInclude`, `::`-qualified foreign
types, and the toolchain controls.

### JavaScript

Emits ES modules with source maps, targeting the browser. `JsCapabilityChecker.cs`
rejects what cannot be lowered rather than emitting approximations — see
[JavaScript backend](#/js-backend).

Its CFG handling differs again: the merge block is derived by `FindMergeBlock`, so the
true branch target must never *be* the merge block.

## The dispatch trap

> [trap] **A missing switch arm does not fail — it silently builds C#.** Four separate
> backend-dispatch maps have defaulted to C# over the life of this repo. The most recent,
> `ProjectTemplateService.GenerateProjectFileContent`, would have written
> `<TargetBackend>CSharp</TargetBackend>` into a JavaScript project. Its default now
> throws, and `ProjectTemplateBackendMappingTests` pins every id in `SolutionTypes.All`.
>
> **When you add a backend or a solution type, grep for every map keyed on it.** The CLI
> has the same hardening: an unknown `--target` now errors with
> *"Valid targets: csharp, cpp, javascript (js), llvm, msil"* instead of quietly writing
> a `.cs` file.

## Choosing a backend in practice

| You want | Use |
|---|---|
| The full .NET library surface, fastest iteration | C# |
| A standalone native executable, no runtime dependency | C++ |
| Something that runs in a browser | JavaScript |
| To ship a game with the engine | Either C# or C++ — both reach the same DLL |

## Standard library shims

Each backend pairs with a shim under `BasicLang/StdLib/`, registered through
`StdLibRegistry.cs`, that maps BasicLang's built-in surface onto the target's:

`CSharpStdLib.cs` · `CppStdLib.cs` · `JavaScriptStdLib.cs` · `LLVMStdLib.cs` ·
`MSILStdLib.cs` · `FrameworkStdLib.cs` (the engine surface) · `IStdLib.cs` (the contract)

> [note] Known gap: the .NET API surface available on the C++ backend is narrower than on
> C# — parts of `List`, `Console` and `String` are missing. The catalogue is in
> `docs/superpowers/specs/2026-07-07-cpp-backend-preexisting-gaps.md`.
