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

## COM interop

Ruled out. It is a settled scope decision, recorded in `docs/HANDOFF.md` — do not add it.

## Known front-end gaps

These affect every backend and are open:

- `Inherits ArgumentException` — inheriting from a BCL exception type.
- Assigning an inherited field from a derived class.
- Module-level non-constant initializers.
- C++ `raise_X()` taking no parameters.
- `For Each … In items.Select(…)` inside a class method fails on C#.
