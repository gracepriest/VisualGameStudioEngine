title: C++ backend and interop
lede: The two-layer standard library, passthrough to real C++ headers, mixed projects, and toolchain control.
---
The native backend is not a transpiler of convenience — it is a first-class target with
its own type model, its own capability checker, and a way out to hand-written C++ when
the abstraction runs out.

## The two-layer standard library

**Layer 1 — portable collections.** `List(Of T)`, `Dictionary(Of K, V)` and
`HashSet(Of T)` lower to `BasicLang::List<T>`, `BasicLang::Dictionary<K,V>` and
`BasicLang::HashSet<T>` with .NET-faithful semantics, including the throwing behaviour:

```vb
Dim scores As New Dictionary(Of String, Integer)()
scores.Add("alice", 10)
scores("bob") = 20               ' indexer set
Dim a = scores("alice")          ' missing key THROWS, like .NET
scores.Add("alice", 99)          ' duplicate key THROWS, like .NET

Dim names As New List(Of String)()
Dim n = names.Count              ' .Count, not .size()
```

Collections carry **reference semantics** through
`std::shared_ptr<BasicLang::List<T>>` — matching .NET. An earlier value-wrapper design
diverged from .NET behaviour and was replaced. `String` and `Structure` remain values.

**Layer 2 — passthrough.** Anything the portable layer does not cover, you reach
directly. This layer is C++-backend-only and completely unchecked: past the include you
get C++'s own compiler errors, not BasicLang diagnostics.

```vb
#CppInclude <mutex>                ' -> #include <mutex>
#CppInclude "grid.h"

Sub Main()
    Dim m As std::mutex            ' ::-qualified foreign type, opaque value
    m.lock()
    m.unlock()

    Dim q As std::deque(Of Integer)                  ' (Of ...) -> <...>
    Dim it As std::vector(Of Integer)::iterator      ' :: after the template scope
End Sub
```

- `#CppInclude <header>` / `"header.h"` emits a real `#include`. It is **not** `#Include`,
  which textually splices a BasicLang source file.
- `::`-qualified types are opaque foreign types: value semantics, `.` member access, no
  BasicLang member checking.
- `(Of ...)` becomes `<...>`; a trailing `::segment` after the generic scope is preserved.

## Reference vs value, at a glance

| BasicLang | C++ |
|---|---|
| `Class` / `Interface` | `std::shared_ptr<T>`, constructed with `make_shared`, accessed with `->` |
| `Structure` | value `struct` |
| `List(Of T)` etc. | `std::shared_ptr<BasicLang::List<T>>` — reference semantics |
| `String` | value |
| Generic type / method | real C++ template |
| Iterator (`Yield`) | C++20 coroutine — `Generator<T>` / `co_yield` |
| `Async` / `Await` | synchronous `Task<T>` emulation, no scheduler |
| `Throw` / `Try` | `IRThrow` lowering — `Return` inside `Try` bypasses `Finally` |

## Mixed BasicLang + C++ projects

A project can contain `.bas` files and hand-written `.cpp` / `.h` files sharing one
build. The IDE opens, edits, builds and debugs both. Supporting machinery lives in
`BasicLang/ProjectSystem/`:

| File | Role |
|---|---|
| `CppProjectBuilder.cs` | Drives the native build (~1,760 lines) |
| `CppToolchain.cs` | Detects and selects clang / gcc / MSVC |
| `CompileCommandsWriter.cs` | Emits `compile_commands.json` so clangd can do IntelliSense |
| `CppDiagnosticsParser.cs` | Parses compiler output into IDE diagnostics |
| `CppRuntimeDeployment.cs`, `EngineDeployment.cs` | Copies the runtime and engine DLL next to the output |
| `NativeEntryPoints.cs` | Entry-point synthesis for native executables |
| `IntelliSenseEmitter.cs` | Emits the artefacts IntelliSense needs |

## Toolchain control

Choose the C++ standard and toolchain (`llvm` / `gcc` / `msvc`) per project. Under
**Settings → C++** you can additionally pin an explicit compiler and debugger path per
backend for when your toolchain is not on `PATH`.

> [note] A pinned path is **authoritative**. A bad pinned path fails the build loudly
> rather than silently falling back to whatever is on `PATH` — this was a deliberate
> design decision, recorded in
> `docs/superpowers/specs/2026-07-21-cpp-per-backend-toolchain-overrides-design.md`.

`CppToolchainOverrides.cs`, `CppToolchainProbeService.cs` and `ToolchainPathValidator.cs`
in the IDE's ProjectSystem implement the probe and validation.

## Calling .NET from a native project

The C++ backend can reach .NET types through a generated shim — `NetShimGenerator.cs`
and `NetProxyEmitter.cs` under `BasicLang/Compiler/CodeGen/Net/`, fed by the type
discovery in `BasicLang/Net/`. `CppCodeGenerator.NetCalls.cs` emits the call sites. This
is the P2a workstream; see [.NET interop](#/net-interop) and
[Known gaps](#/roadmap) for its current state.

## Capability failures

`CppCapabilityChecker.cs` rejects constructs the backend cannot lower, with `BL6xxx`
diagnostics. Its own blob code defaults to `BL6001` — and the message text must never
repeat a code, because a message naming one code under a diagnostic carrying another is
exactly what made users see the wrong error.

## Debugging native builds

`lldb-dap` handles native debugging, and breakpoints set in `.bas` files still bind:
the backend emits `#line` directives mapping generated C++ back to BasicLang source. See
[Debugging](#/debugging).
