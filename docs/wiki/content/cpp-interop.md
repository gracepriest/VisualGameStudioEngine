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
- An inline `cpp{ ... }` block passes statements through verbatim. Like the other two forms
  it is C++-backend-only: a `cpp{}` block on C#, LLVM, MSIL or JavaScript is a refusal, not a
  no-op — it used to be silently dropped, which is a do-nothing program from a build that
  reported success.

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
| `CppProjectBuilder.cs` | Drives the native build (~1,770 lines) |
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

> [trap] Every native project built with clang / gcc links `-pthread` — **unconditionally**,
> not only when .NET is on. It is there for the .NET-enabled case: the generated
> `blnet_runtime.hpp` guards its callback and invocation-queue tables with `std::mutex` /
> `std::lock_guard` / `std::atomic`. Leaving it out is invisible on Linux — since glibc 2.34
> the pthread symbols live in libc, so the same project links clean with no flag — and fatal
> on MinGW, which routes them through winpthreads and does not put that on the link line by
> default (measured: a wall of `undefined reference to pthread_mutex_init` from every
> translation unit). MSVC needs nothing; its standard library threading is built in.

`CppToolchainOverrides.cs`, `CppToolchainProbeService.cs` and `ToolchainPathValidator.cs`
in the IDE's ProjectSystem implement the probe and validation.

## Calling .NET from a native project

The C++ backend can reach .NET types through a generated shim — `NetShimGenerator.cs`
and `NetProxyEmitter.cs` under `BasicLang/Compiler/CodeGen/Net/`, fed by the type
discovery in `BasicLang/Net/`. `CppCodeGenerator.NetCalls.cs` emits the call sites. This
is the P2a workstream, and it is <span class="pill ok">Complete</span> — the P2a-2 plan
recorded "Genuinely open work: NONE" on 2026-09-14. See [.NET interop](#/net-interop)
for which BCL types are measured usable.

### The facade — write ordinary C++, not mangled slots

The proxy header `blnet_proxies.g.hpp` names every slot under a §7.3 mangled identifier
(declaring type + member + static-ness + generic arity + per-parameter ref-kind, plus a
SHA-256 signature hash) because the export lives in a flat symbol namespace with no
overloading. Correct, but a terrible — and *fragile* — authoring surface: the hash moves
whenever the signature does.

So `NetProxyEmitter.Facade.cs` emits a second header, `blnet_facade.g.hpp`, rendering the
same slots as ordinary C++ under `BasicLang::netfx`:

```cpp
#include "blnet_facade.g.hpp"     // always emitted, never auto-included
using namespace BasicLang::netfx;

System::Console::WriteLine("hello");
auto r = System::Text::RegularExpressions::Regex("^\\d+$");
bool ok = r.IsMatch("123");
```

| Decision | Shape |
|---|---|
| Root namespace | `BasicLang::netfx` — never a bare `namespace System`, which would collide with a user type in a header they cannot edit |
| One type | one `struct`, namespaces mirroring the .NET namespace |
| Static / instance members | `static` member functions / ordinary member functions |
| Constructors | real C++ constructors; `T(adopt_handle, h)` is the separate handle-adopting form |
| Properties | `get_X()` / `set_X()`, never `operator=` |
| Handle-typed parameter | the wrapper type when it has a handle, raw `NetRef` otherwise |

> [note] The facade is a **second rendering of the same `SlotPlan` list** the proxy emitter
> already computes, not an independent walk of the surface — so it cannot disagree with the
> proxy table about a signature. Only *coverage* can drift, which `NetFacadeCoverageDriftTests`
> pins over a real framework surface as a set identity plus a coverage floor.

> [trap] `set_X()` is usually **absent**, and that is upstream of the facade. A `<NetProxy>`
> declared type draws only property READ slots; a setter descriptor is synthesized only where
> some BasicLang code in the project actually writes the member. Measured over `System.Console`
> and `Regex`: zero `set_` slots. The facade can only render slots that exist.

> [trap] Two slots that render to the *same* C++ signature are **both** omitted, with a
> `BL6027` warning naming them — §8.3 maps every handle-represented type onto one wire form,
> so `F(Regex)` and `F(Uri)` collide. Never silently picked: the mangled slots stay callable as
> the escape hatch. `BL6027` is always a warning, never an error.

## Capability failures

`CppCapabilityChecker.cs` rejects constructs the backend cannot lower, with `BL6xxx`
diagnostics. Its own blob code defaults to `BL6001` — and the message text must never
repeat a code, because a message naming one code under a diagnostic carrying another is
exactly what made users see the wrong error.

## Debugging native builds

`lldb-dap` handles native debugging, and breakpoints set in `.bas` files still bind:
the backend emits `#line` directives mapping generated C++ back to BasicLang source. See
[Debugging](#/debugging).
