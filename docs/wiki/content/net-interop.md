title: .NET interop
lede: `Using` on the managed backend, and the shim-and-proxy machinery that lets native projects reach .NET types.
---
## On the C# backend

Interop is direct. `Using` brings in a real namespace, .NET types flow through as
themselves, and NuGet packages are available through the CLI package commands:

```vb
Using System.Collections.Generic
Using System.Text

Dim sb As New StringBuilder()
sb.Append("hello")
```

```powershell
IDE/BasicLang.exe add package Newtonsoft.Json
IDE/BasicLang.exe restore
IDE/BasicLang.exe list packages
```

`PackageManager.cs` and `ExternalLibraryLoader.cs` handle resolution and loading.

## On the C++ backend

This is the harder problem, and the repo's largest recent workstream (tracked as **P2a**).
**P2a-2 (.NET classes in native projects) has <span class="pill ok">shipped</span>** — merged at
`77e415b`, with the `blnet` C++ facade landing on top of it (all five tasks done, Windows-gated at
`ee3c086`).
A native executable has no CLR, so reaching a .NET type means discovering its surface at
compile time, generating a shim, and marshalling across the boundary.

### The discovery side — `BasicLang/Net/`

| File | Role |
|---|---|
| `NetTypeResolver.cs` | Resolves .NET types from references (~1,420 lines) |
| `NetSurface.cs`, `NetSurfaceCollector.cs` | Collects the exact member surface a program actually uses |
| `NetTypeDescriptors.cs` | Descriptor model for resolved types |
| `NetMarshalTable.cs` | How each type crosses the boundary |
| `NetOverloadProbe.cs` | Overload resolution against the real metadata |
| `NetDelegateDispatch.cs` | Delegates and callbacks across the boundary |
| `NetAccessorSynthesis.cs` | Property and field accessor synthesis |
| `NetNameMangler.cs` | Stable native symbol names |
| `NetAmbientNamespaces.cs` | Implicit namespace seeding |
| `NetReferenceResolver.cs` | Resolves the project's assembly closure — `<Reference>`, `<PackageReference>` and the always-present framework set (~490 lines); raises `BL6021` |
| `NetArrayCopy.cs`, `NetClaimPredicate.cs`, `NetAstAnnotations.cs` | Supporting passes |

### The emission side — `BasicLang/Compiler/CodeGen/Net/`

- **`Compiler/CodeGen/Net/NetShimGenerator.cs`** (~1,690 lines) generates the managed shim assembly.
- **`Compiler/CodeGen/Net/NetProxyEmitter.cs`** (~1,220 lines) emits the native-side proxies, and its
  partial **`NetProxyEmitter.Facade.cs`** (~780 lines) renders those same mangled slots as the
  ergonomic `blnet_facade.g.hpp`, under the `BasicLang::netfx` namespace. It is also where `BL6027`
  is raised.
- **`BasicLang/CppCodeGenerator.NetCalls.cs`** (~1,380 lines — in `BasicLang/`, not the `Net/` folder)
  emits the call sites.
- **`BasicLang/BoundaryTypeRegistry.cs`** (126 lines) and **`BasicLang/NativeBclSurface.cs`** (443
  lines) — beside the compiler rather than under `CodeGen/Net/` — hold the agreed contract of
  what may cross.

The boundary contract itself is specified in
`docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md`, with the
access and AOT-shim design in `2026-07-29-p2a-dotnet-access-aot-shim-design.md`.

### Diagnostics

Boundary failures surface as `BL6xxx` — for example `BL6019` / `BL6020` are raised at a
*use site* when a member is not available across the boundary, rather than silently
omitting it. That choice is deliberate: an omission would compile and then behave wrong.

> [note] Line attribution matters here. `NetAstAnnotations.cs` carries the originating
> `.bas` path and line so a `BL6020` points at the user's source, not at generated code.
> A `BL6020` that moves between builds is a bug in that attribution.

### Which BCL types work from C++

`<NetProxy Include="System.Console" />` declares a **type**; you get its members as callable
C++ functions through `blnet_facade.g.hpp`. Every facade name sits under `BasicLang::netfx` — a bare
`namespace System` at global scope would collide with any user type of that name — and the header is
emitted unconditionally but included by nobody, so `using namespace BasicLang::netfx;` is the one
opt-in line. What you get is a *subset* of the .NET member set —
§7.2's rule — and every omission is named in the build output as `BL6026` rather than silently
dropped.

The list below is **measured, not exhaustive.** 42 well-known types across the 17 ambient
namespaces were probed on .NET 8; 38 were admitted — the 32 tabulated below, plus the six that §8.3's
*ByRef handle ownership* resolution (2026-09-15) unblocked. Plenty of untested BCL types will work too —
try the one you want. What the list *is* good for is the failure column, because the blockers
share very few root causes.

| Namespace | Verified usable | Member count |
|---|---|---|
| `System` | `Console` 74, `Math` 94, `Convert` 186, `Environment` 38, `Random` 14, `String` 111, `TimeSpan` 58, `Object` 2 | |
| `System.Collections` | `ArrayList` 35, `Hashtable` 24 | |
| `System.Diagnostics` | `Stopwatch` 17, `Process` 88 | |
| `System.IO` | `Path` 33, `File` 87, `Directory` 50, `FileInfo` 43, `DirectoryInfo` 51, `StreamReader` 35, `StreamWriter` 63 | |
| `System.Net`, `.Http`, `.Sockets` | `HttpClient` 58, `TcpClient` 26 | |
| `System.Security.Cryptography` | `SHA256` 20, `MD5` 20, `RandomNumberGenerator` 11 | |
| `System.Text`, `.Json`, `.Json.Nodes`, `.RegularExpressions` | `Encoding` 57, `JsonSerializer` 3, `JsonDocument` 7, `JsonNode` 21, `Regex` 57, `Match` 14 | |
| `System.Threading`, `.Tasks` | `Monitor` 1, `Task` 55 | |

Declaring all 32 at once emits an 8,781-line / 439 KB facade. Nothing is included by default —
a project with no `<NetProxy>` emits no proxy artifacts at all.

> [trap] A `<NetProxy>` declared type draws only property **read** slots. A `set_X` descriptor is
> synthesized only where a BasicLang program actually *writes* the member
> (`BasicLang/Net/NetAccessorSynthesis.cs`), so a declared-only surface carries none — measured as
> zero `set_` slots over `System.Console` and `Regex`. C++ can read such a property but not write
> it, and the facade cannot fix it: it can only render slots that exist.

**Six more became usable on 2026-09-15**, when §8.3 specified ByRef handle ownership. Each had
been failing the whole build on a `ref`/`out`/`in` parameter whose wire form is a handle; all six
now emit with zero `BL6019`. Counted as **proxy slots in a single-type project**, which is a
different measurement from the column above — it is the whole surface, before the facade decides
what it can render ergonomically:

| Type | Proxy slots | `BL6026` |
|---|---|---|
| `System.DateTime` | 97 | 13 |
| `System.Threading.Thread` | 75 | 7 |
| `System.Uri` | 72 | 2 |
| `System.Net.IPAddress` | 35 | 8 |
| `System.Text.Json.Nodes.JsonObject` | 32 | 4 |
| `System.Diagnostics.Debug` | 28 | 11 |

Declaring all 38 together emits a 10,329-line / 509 KB facade — 420 `BL6026` and 63 `BL6027`.

> [trap] Those counts were measured at `dfbfcee` (2026-09-15, 02:59). `46b289d` landed the same
> day and closed §8.4's dispatcher gap, admitting handle- and string-shaped **delegate** slots
> across these same 38 types — `Action<Task>`, `Func<Task>` and `MatchEvaluator` in both
> directions, which is what the `BL6006` on a 32-type project actually was.
> `ParameterizedThreadStart`'s `Object` parameter stays refused permanently (`Object` has no
> marshal row, and "no row" *is* the handle rule). Re-measure before quoting these numbers.

#### The four that are refused, and why

These raise **`BL6019` and fail the build** — a hard refusal, not a silent omission:

| Type | Cause |
|---|---|
| `System.Net.Sockets.Socket` | a ByRef **enum** |
| `System.Runtime.InteropServices.Marshal` | a ByRef **by-value-pointer** (§6.4) |
| `System.Guid`, `System.Text.StringBuilder` | a §6.4 by-value-pointer **result** |

This list used to have ten entries, and six of them shared one cause: a `ref`/`out`/`in`
parameter whose wire form is a handle. That was refused because §8.3 left the ownership
undefined — writing a new handle over the caller's looked like it could release one the callee
had returned unchanged, a double release.

**It could not.** `HandleTable.Create` allocates a fresh slot, `GCHandle` and refcount on every
call and has no identity map, so re-handling an object the caller already holds produces a
*second independent* table reference — two independent releases, not a double free. §8.3 now
states the rule normatively (the managed side always writes a freshly created handle, never an
incoming one) and those six emit.

What is left is **three separate unresolved questions**, not one, so none of them is unblocked by
analogy with the handle row: a ByRef `String` has opposite ownership per direction and one
`char**` cannot carry both; a ByRef enum crosses as a by-value integral and writing back needs a
widening contract; a ByRef §6.4 row points at a buffer the managed side holds a *copy* of, so
"the callee writes through your pointer" is not what happens.

> [note] Two of the refused types — `Guid` and `StringBuilder` — you do not need proxied. P1
> ships **native C++ implementations** of `DateTime`, `DateTimeOffset`, `TimeSpan`, `Decimal`,
> `Guid` and `StringBuilder`, compiled into the program with no boundary crossing at all. Note they are different types with the same name: a facade call returning a managed
> `DateTime` hands back a `NetRef` handle, not the native struct.

#### Two more things the build tells you

**`BL6026`, one per omitted member.** The 32-type facade produced 375, of which the three buckets
below account for 374 — an off-by-one carried from the original measurement and not re-checked
since: 279 where a parameter or
return type has no §8.3 wire form (`Span<T>`, `ref struct` enumerators, `System.Object`), 55
generic methods whose type parameter cannot cross, and 40 marked `[RequiresUnreferencedCode]` or
`[RequiresDynamicCode]`, which cannot run under Native AOT. The 38-type facade produces 420.

**`BL6027`, one per collided name** — a warning, never an error. 62 of them at 32 types, almost
all `System.Convert`: .NET distinguishes `Char` from `UInt16`, C++ does not, so `ToBoolean(Char)`
and `ToBoolean(UInt16)` become one signature. The facade omits **both** rather than binding one
silently; the members stay callable under their mangled slot names in `blnet_proxies.g.hpp`.

## COM interop

Ruled out. It is a settled scope decision, recorded in `docs/HANDOFF.md` — do not add it.

## Known front-end gaps

These are all open. The first three are front-end defects that affect every backend; the last two are backend-specific:

- `Inherits ArgumentException` — inheriting from a BCL exception type.
- Assigning an inherited field from a derived class.
- Module-level initializers that need code to run (`Helper()`, `New List(Of Integer)()`) — refused
  with a diagnostic naming the variable, since no backend has a module initializer. Constant-foldable
  ones, `7 / 2` included, now fold in the IR builder and emit on C#, C++, MSIL and JavaScript; they
  used to crash the compiler outright.
- C++ `raise_X()` taking no parameters.
- `For Each … In items.Select(…)` inside a class method fails on C#.
