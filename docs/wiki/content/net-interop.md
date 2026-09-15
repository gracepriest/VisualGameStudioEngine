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
| `NetArrayCopy.cs`, `NetClaimPredicate.cs`, `NetAstAnnotations.cs` | Supporting passes |

### The emission side — `BasicLang/Compiler/CodeGen/Net/`

- **`NetShimGenerator.cs`** (~1,550 lines) generates the managed shim assembly.
- **`NetProxyEmitter.cs`** (~1,200 lines) emits the native-side proxies.
- **`CppCodeGenerator.NetCalls.cs`** (~1,280 lines) emits the call sites.
- **`BoundaryTypeRegistry.cs`** and **`NativeBclSurface.cs`** hold the agreed contract of
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
C++ functions through `blnet_facade.g.hpp`. What you get is a *subset* of the .NET member set —
§7.2's rule — and every omission is named in the build output as `BL6026` rather than silently
dropped.

The list below is **measured, not exhaustive.** 42 well-known types across the 17 ambient
namespaces were probed on .NET 8; 31 were admitted. Plenty of untested BCL types will work too —
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

Declaring all 32 at once emits an 8,781-line / 439 KB facade that compiles clean. Nothing is
included by default — a project with no `<NetProxy>` emits no proxy artifacts at all.

#### The eleven that are refused, and why

These raise **`BL6019` and fail the build** — a hard refusal, not a silent omission:

| Type | Cause |
|---|---|
| `System.DateTime`, `System.Uri`, `System.Diagnostics.Debug`, `System.Net.IPAddress`, `System.Text.Json.Nodes.JsonObject`, `System.Threading.Thread` | a `ref`/`out`/`in` parameter whose wire form is a **handle** |
| `System.Net.Sockets.Socket` | a ByRef **enum** |
| `System.Runtime.InteropServices.Marshal` | a ByRef **by-value-pointer** (§6.4) |
| `System.Guid`, `System.Text.StringBuilder` | a §6.4 by-value-pointer **result** |

Nine of the eleven are the same unspecified rule: §8.3 pins ByRef slots to by-value scalars and
leaves ByRef handle ownership undefined, so the emitter refuses rather than guessing a double
release. **Specifying that one rule in §8.3 would unblock most of this list at once.**

> [note] Three of the refused types — `DateTime`, `Guid`, `StringBuilder` — you do not need
> proxied. P1 ships **native C++ implementations** of `DateTime`, `DateTimeOffset`, `TimeSpan`,
> `Decimal`, `Guid` and `StringBuilder`, compiled into the program with no boundary crossing at
> all. Note they are different types with the same name: a facade call returning a managed
> `DateTime` hands back a `NetRef` handle, not the native struct.

#### Two more things the build tells you

**`BL6026`, one per omitted member.** The 32-type facade produced 375: 279 where a parameter or
return type has no §8.3 wire form (`Span<T>`, `char*`, a handle in ByRef position), 55 generic
methods whose type parameter cannot cross, and 40 marked `[RequiresUnreferencedCode]` or
`[RequiresDynamicCode]`, which cannot run under Native AOT.

**`BL6027`, one per collided name** — a warning, never an error. 62 of them at 32 types, almost
all `System.Convert`: .NET distinguishes `Char` from `UInt16`, C++ does not, so `ToBoolean(Char)`
and `ToBoolean(UInt16)` become one signature. The facade omits **both** rather than binding one
silently; the members stay callable under their mangled slot names in `blnet_proxies.g.hpp`.

## COM interop

Ruled out. It is a settled scope decision, recorded in `docs/HANDOFF.md` — do not add it.

## Known front-end gaps

These affect every backend and are open:

- `Inherits ArgumentException` — inheriting from a BCL exception type.
- Assigning an inherited field from a derived class.
- Module-level non-constant initializers.
- C++ `raise_X()` taking no parameters.
- `For Each … In items.Select(…)` inside a class method fails on C#.
