# P2a — .NET class access from Native (BL+C++) projects, AOT shim transport

**Status:** Draft
**Date:** 2026-07-29
**Builds on:** `2026-07-26-dotnet-native-boundary-contract-design.md` (P0, Implemented),
`2026-07-27-p1-native-bcl-types-design.md` (P1, Implemented)
**Followed by:** P2b — CoreCLR hosting transport (not yet specced)

---

## 1. Goal and non-goals

### 1.1 The standing directive

BasicLang code **and** hand-written C++ inside a Native (BL+C++) project must be able to
consume .NET classes while still compiling to a native executable. Restated by the user on
2026-07-29:

> "if it's a class that C# **or** VB.NET could use, I want to be able to use it in BasicLang
> native + C++"

Scope explicitly includes the user's own C# libraries, the parts of the BCL P1 did not
nativize, NuGet packages, and arbitrary referenced assemblies.

### 1.2 Why this is P2**a** and not P2

That end state cannot be reached through Native AOT alone. Microsoft documents the ceiling as
structural, not a tuning problem
([Limitations of Native AOT deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment)):

- No `Assembly.Load` / `Assembly.LoadFile`
- No `System.Reflection.Emit`
- `Type.MakeGenericType` on an instantiation not pre-generated at publish **throws**; the docs
  state plainly that *"There aren't many workarounds for `RequiresDynamicCode`"*
- `Type.GetType(runtimeString)` + `Activator.CreateInstance` is cited as fundamentally
  unfixable — the prescribed remedy is to annotate it as broken
- `System.Linq.Expressions` is always interpreted
- Only reflection targets the compiler can statically prove survive trimming

Practical casualties: Newtonsoft.Json, EF Core, most ORMs and older serializers. What AOT
*does* deliver is everything reachable through **statically referenced** assemblies — and the
reference set is known from the `.blproj` at shim-build time, so that covers the user's own
libraries, most of the BCL, and AOT-clean packages.

The user's decision (2026-07-29) is therefore **both transports, AOT first**, behind one C ABI:

| | Transport A — AOT shim (this spec) | Transport B — CoreCLR hosting (P2b) |
|---|---|---|
| Reach | statically referenced assemblies | 100% of .NET, incl. reflection |
| Target machine | self-contained, nothing to install | requires an installed .NET runtime |
| Startup | fastest | runtime init cost |

> **Topology note.** Microsoft warns against combining an AOT binary with `hostfxr` (one
> runtime per process). That warning does **not** apply here: our executable is plain C++ from
> clang/MinGW, not an AOT-published .NET binary — only the *shim* is .NET. Both transports are
> individually viable behind the same ABI. They must never both be live in one process.

### 1.3 In scope for P2a

1. The transport-neutral foundation: reference closure, type resolution, surface discovery,
   IR/lowering, native proxy API, diagnostics.
2. **The transport seam** (§4.2) — the mechanism that makes P2b a second spec rather than a
   second rewrite.
3. Transport A: shim generation, AOT publish, native binding.
4. **Both consumers**: BasicLang lowering *and* hand-written C++.
5. Delegate **arguments** (`Action`/`Func`/comparers/predicates) — making P0's dormant callback
   machinery live.

### 1.4 Out of scope for P2a

- Transport B (CoreCLR hosting) — P2b.
- .NET **events** (`AddHandler`/`RemoveHandler`) — needs subscription lifetime management.
- BasicLang types implementing .NET interfaces or subclassing .NET types (full bidirectional).
- Blittable-by-value marshaling of arbitrary user structs (handles in v1; optimization later).
- COM interop — ruled out permanently by the user ("COM is dead", 2026-07-26).
- C++/CLI — ruled out in P0 (MSVC-only).

---

## 2. Decisions locked

These were settled with the user on 2026-07-29 and are inputs to this design, not open
questions.

| # | Decision | Consequence |
|---|---|---|
| D1 | All .NET classes are the end goal — own libs, BCL, NuGet, any assembly | Forces two transports; forces real metadata |
| D2 | **Both transports, AOT first** | This spec is transport A; the seam (§4.2) is mandatory |
| D3 | First spec = foundation + seam + transport A + **both consumers** | `<NetProxy>` declaration for C++ (§7.2) |
| D4 | **Fully transparent syntax** — nothing to declare beyond the assembly reference | Metadata reading mandatory; parity oracle becomes the gate (§12.1) |
| D5 | AOT-ceiling violations are a **build error mapped to `.bas` source** | Provenance map + BL6020 (§11.3) |
| D6 | Delegate **arguments** only | Events and interface implementation deferred |
| D7 | Publish is **inline with a content-hash cache** | Phase model + cache key (§10.1, §10.2) |
| D8 | Type knowledge comes from **Roslyn** | `Microsoft.CodeAnalysis.CSharp` in `BasicLang.csproj` |

D4 is the load-bearing one. Transparency means the analyzer must resolve a .NET member
*before* codegen — which rules out staying permissive, rules out a csc round-trip (IntelliSense
would need a compile), and makes D8 follow.

---

## 3. What exists today

Verified by an 8-agent reconnaissance sweep (`wf_e056eecd-017`, 2026-07-29). File:line anchors
are from that sweep and should be re-confirmed before edits.

### 3.1 P0 — the contract (Implemented)

`AbiVersion = 1` (`BasicLang/Compiler/CodeGen/CPlusPlus/BlnetContract.cs:12`), a dense 9-code
status table (`BLNET_OK=0` … `BLNET_E_ALLOC=8`), and two C/C++ sources carried as string
constants in `BlnetRuntimeSources.cs`: `blnet.h` and `blnet_runtime.hpp`.

**The ABI is asymmetric, and this is the single most consequential fact for P2a.**

- **Managed → native is fully generic**: one universal thunk
  ```c
  typedef int32_t (BLNET_CALL *BlnetInvokeCallbackFn)(
      uint64_t callback_handle, const uint64_t* args, int32_t argc, uint64_t* result);
  ```
  plus per-callback `BlnetSlotDesc[]` fixed at registration, a `BlnetCallScope` depth counter,
  inline/queued dispatch, `BLNET_E_CROSS_THREAD_RESULT`, deep-copy at enqueue, and
  `blnet_pump()` with drain-past-failures semantics. All built, all tested, **all currently
  dead code with no production consumer.**
- **Native → managed is not generic**: exactly seven shim exports
  (`blnet_abi_version` … `blnet_last_error`), and **none of them invokes a managed method**.
  There is **no type id, member id, or signature descriptor anywhere in the contract**.

P2a therefore builds the calling direction from scratch. P0 left the seam deliberately:

```cpp
/* ---- Shim binding (filled by the host: harness now, generated startup in P2) ---- */
inline ShimApi g_shim;
```

Settled and not to be redesigned: the handle model (`uint64_t` = `{generation:hi32 | index:lo32}`,
index 0 burned, `GCHandle` + generation + refcount, born at refcount 1, generation bumps on
free, stale use → `BLNET_E_STALE_HANDLE`); the seven export names and `cdecl`; the status table;
UTF-8 string ownership (in-params borrow-and-copy, transfers via `blnet_alloc`/`blnet_free`);
the `[ThreadStatic]` error channel; `NetRef` RAII; the 2-slot positional vtable; and the publish
recipe (net8.0 — required both for NU1201 and as the documented floor for AOT analysis
warnings — `PublishAot`, `NativeLib=Shared`, `-r win-x64`, `InvariantGlobalization`,
`AllowUnsafeBlocks`).

### 3.2 P1 — native BCL types (Implemented)

`DateTime`, `TimeSpan`, `Guid`, `Decimal`, `DateTimeOffset`, `StringBuilder` are pure C++ and
run natively with no .NET involved. `String`, `Console`, `List(Of T)` and `Dictionary` were
already native. `BoundaryTypeRegistry` categorizes these as `NativeOwned`.

This substantially shrinks P2a: everyday BasicLang programs never touch the boundary at all.

### 3.3 Gaps P2a must close

| Gap | Evidence |
|---|---|
| No metadata reading anywhere | Zero repo hits for `MetadataLoadContext`, `PEReader`, Cecil, dnlib. `System.Reflection.Metadata` 8.0.0 is referenced but used only for Portable PDBs. Roslyn 4.9.2 exists only in `VisualGameStudio.Tests.csproj`. |
| Analyzer is deliberately permissive | `IsNetType` accepts any PascalCase identifier without an underscore (`SemanticAnalyzer.cs:2047-2052`); `ResolveTypeName` returns a `TypeInfo` with an empty `Members` dict, so `New Regex(1,2,3)` type-checks clean. |
| Type registry never populated on the compile path | `ConfigureTypeRegistry` (`SemanticAnalyzer.cs:727`) has exactly one caller — `LSP/DocumentManager.cs:571`. |
| References silently dropped on the native path | `Program.cs:436` returns before restore; `BuildService.cs:449` mirrors it. **No diagnostic.** The XML element is `<Reference Include>` + `<HintPath>`; there is no `<AssemblyReference>` element. |
| Contract headers never emitted | `blnet.h` / `blnet_runtime.hpp` are referenced only by test files. |
| All .NET rejections collapse to one code | `CppCapabilityChecker.cs` (repo **root** of `BasicLang/`) throws `CppCapabilityException`; `CppProjectBuilder.cs:300` reports a single BL6001. Catch-all at :627-631. Next free code: **BL6016**. |
| `ManagedOwned` is inert | Empty private HashSet (`BoundaryTypeRegistry.cs:72`) with zero consumers. |
| Build pipeline has no phases | `CppProjectBuilder.EmitCore` is straight-line: no phase model, no up-to-date check, no `CancellationToken`, one compiler invocation for all TUs, `dotnet` never invoked. |
| Generated headers unreachable from pure C++ | `obj/gen` joins the include path only when `blSources.Count > 0` (`CppProjectBuilder.cs:419-420`). |
| AOT publish cost unknown | **Unmeasured anywhere in the repo.** Task 1 of the plan must measure it. |
| `Assembly.LoadFrom` in the LSP is defective | `TypeRegistry.cs` loads into the compiler process (file locks, runs module initializers, cannot unload) and swallows every failure in a bare `catch {}`. |

---

## 4. Architecture

### 4.1 Layer map

```
 .bas source                      hand-written .cpp
      │                                   │
      │ analyzer resolves the member      │ includes the proxy header
      │ (NetTypeResolver, §6)             │ (surface declared via <NetProxy>, §7.2)
      ▼                                   │
 IR call carrying a resolved target       │
      │                                   │
      ▼                                   ▼
 obj/gen/blnet_proxies.g.hpp   ── typed inline C++ proxies ──
      │
      ▼
 obj/gen/blnet_bindings.g.hpp  ── struct of function pointers ──  ← THE SEAM
      │
   ┌──┴───────────────────────┬─────────────────────────────┐
   │ Transport A (this spec)  │ Transport B (P2b)           │
   │ GetProcAddress on the    │ load_assembly_and_get_      │
   │ AOT-published shim DLL   │ function_pointer (hostfxr)  │
   └──────────────────────────┴─────────────────────────────┘
```

### 4.2 The transport seam

One generated struct of function pointers, filled at startup:

```cpp
// obj/gen/blnet_bindings.g.hpp   — generated, transport-agnostic
struct BlnetProxyTable {
    int32_t (*Customer_Recalculate)(uint64_t self, uint64_t order, uint64_t* result);
    int32_t (*Regex_Match__string)(uint64_t self, const char* input, uint64_t* result);
    /* one slot per member in the discovered surface */
};
extern BlnetProxyTable g_net;
void blnet_bind_all(void* module);
```

Everything above this struct — the analyzer, the IR, the lowering, the typed proxies, and both
consumers — is written once and never varies by transport. Only *filling* the table is
transport-specific.

**Why a struct of pointers rather than direct `extern "C"` declarations.** Direct declarations
bind at link time, which would force MinGW to consume an MSVC-format import library — recon
flagged that as unproven, and a link-time binding cannot be swapped for `hostfxr` delegates at
all. `GetProcAddress` is toolchain-neutral and is already how the P0 conformance harness binds.

**Invariant.** `NetShimGenerator` and `NetShimPublisher` are the *only* components permitted to
know which transport is in use. If anything else needs transport knowledge, the seam is in the
wrong place and the design is wrong.

### 4.3 Components

| Component | Location | Transport-neutral? | Job |
|---|---|---|---|
| `NetReferenceResolver` | `BasicLang/Net/` | yes | `.blproj` → assembly closure (§5) |
| `NetTypeResolver` | `BasicLang/Net/` | yes | Roslyn-backed type/member resolution (§6) |
| `NetSurfaceCollector` | `BasicLang/Net/` | yes | discovers the used member set (§7) |
| `NetNameMangler` | `BasicLang/Net/` | yes | deterministic export names (§7.3) |
| `NetProxyEmitter` | `BasicLang/Compiler/CodeGen/Net/` | yes | proxy header + binding table (§9) |
| `NetShimGenerator` | `BasicLang/Compiler/CodeGen/Net/` | **no** | emits the shim C# project (§8) |
| `NetShimPublisher` | `BasicLang/Compiler/CodeGen/Net/` | **no** | AOT publish (§10) |
| `AotDiagnosticMapper` | `BasicLang/Compiler/CodeGen/Net/` | **no** | ILC warnings → BL6020 (§11.3) |

Modified: `SemanticAnalyzer`, `IRBuilder`, `IRNodes`, `CppCodeGenerator`(+`.Split`),
`CppCapabilityChecker`, `CppProjectBuilder`, `ProjectFile`, `Program`, `BuildService`,
`BoundaryTypeRegistry`, `TypeMapper`, and the LSP's `TypeRegistry`/`DocumentManager`.

---

## 5. Reference closure

`NetReferenceResolver` turns a `.blproj` into the assembly set the resolver and the shim both
need.

| Element | Resolution |
|---|---|
| `<Reference Include="X">` + optional `<HintPath>` | direct path; HintPath resolved **relative to the project file** |
| `<PackageReference>` | existing `PackageManager` restore, then the package's lib assemblies |
| `<ProjectReference>` | built first, then its output assembly |
| (implicit) framework | targeting pack for net8.0 |

Changes required:

- `Program.cs:436` and `BuildService.cs:449` must stop returning before reference resolution on
  the native path. The comment asserting "C++ projects have no NuGet dependencies and skip
  restore entirely" is now false and must go.
- A reference that cannot be resolved is **BL6021**, not silence.

> **HintPath hazard (pre-existing, must be pinned).** Recon found the generated csproj lands in
> `bin/<config>/<TFM>` with `HintPath` copied verbatim, so a relative HintPath resolves against
> the *output* directory. No test pins this. P2a resolves HintPath relative to the project file
> and adds a test; if that diverges from the C# backend's behavior, the C# backend is the bug.

---

## 6. Type resolution

### 6.1 `NetTypeResolver`

Wraps a Roslyn `CSharpCompilation` created from the reference closure via
`MetadataReference.CreateFromFile`. It answers exactly three questions:

1. Does this type exist, and what is its full name / kind / accessibility?
2. What members does it have (including inherited)?
3. **Given this call site with these argument types, which overload wins?**

Question 3 is why Roslyn is here. Overload resolution — with generics, inheritance, optional
parameters, `params`, and implicit conversions — is the part that cannot be approximated, and
it is exactly where "any .NET assembly" applies maximum pressure.

`MetadataReference.CreateFromFile` reads metadata **without loading assemblies into the
process**: no file locks, no module initializers, no unload problem. That is a correctness
improvement over the status quo, not merely a convenience.

### 6.2 One resolver, three consumers

Per CLAUDE.md's shared-resolver rule, `NetTypeResolver` serves the analyzer, the LSP, and
codegen. It **replaces** `TypeRegistry.cs`'s `Assembly.LoadFrom` path rather than sitting
beside it.

`ConfigureTypeRegistry` — today reachable only from `LSP/DocumentManager.cs:571` — becomes part
of `CompileUnit` construction so the compile path is configured identically to the LSP path.

### 6.3 Strictness, phased by backend

Today's permissiveness is load-bearing on the C# backend and, per recon, **untested** —
tightening it everywhere at once is unnecessary blast radius.

| Backend | Unresolved .NET type or member |
|---|---|
| **C++ (native)** | **hard error** (BL6016/BL6017/BL6018) — we cannot emit a proxy for an unresolved member |
| **C#** | **warning**, preserving today's late-`csc` behavior |

Valid programs behave identically on both backends, so D4's transparency and the parity oracle
are unaffected. Only *broken* programs differ, and only in how early they are caught.

This split is also what allows the resolver to be built and proven on the C# backend, green at
every commit, before any native code depends on it — the phase ordering that worked for P1.

### 6.4 The P1 `NativeOwned` collision rule

The six P1 types are native values and must **never** become handles. But a .NET signature can
take or return one, so each needs a conversion pair at the edge:

| Native (P1) | Managed |
|---|---|
| `BasicLang::DateTime` (uint64 dateData = 62-bit ticks \| 2-bit Kind) | `System.DateTime` |
| `BasicLang::TimeSpan` (int64 ticks) | `System.TimeSpan` |
| `BasicLang::Decimal` (96-bit `{lo,mid,hi,flags}`) | `System.Decimal` |
| `BasicLang::Guid` | `System.Guid` |
| `BasicLang::DateTimeOffset` | `System.DateTimeOffset` |
| `BasicLang::StringBuilder` | `System.Text.StringBuilder` (by value → `String`) |

**Zero conversion pairs exist on either side today.** Tick epochs and the `Decimal` bit layout
must agree exactly, or the failure mode is a silently wrong value rather than a crash — which
is precisely what the parity oracle is for (§12.1).

`Object` remains permanently `Rejected` (P0: "void* erasure is unsound").

---

## 7. Surface discovery

### 7.1 BasicLang — inferred

`NetSurfaceCollector` walks the resolved program and collects every .NET member actually
reached: **used-only**, never the whole reference closure. Generating proxies for all of
`System.Private.CoreLib` is not viable, and used-only is what keeps the shim — and therefore the
publish — small.

Zero ceremony, honoring D4.

### 7.2 Hand-written C++ — declared

Nothing walks a `.cpp`, so C++ usage cannot be inferred. The surface gains a second source:

```xml
<ItemGroup>
  <NetProxy Include="MyLib.Customer" />
  <NetProxy Include="System.Text.RegularExpressions.Regex" />
</ItemGroup>
```

Each entry generates that type's **full public surface**. An unknown type is **BL6022**.

D4's transparency promise was about BasicLang *source*, and it stays intact there. The
asymmetry is honest: infer where inference is possible, declare where it is not.

### 7.3 Mangling

`NetNameMangler` produces a legal, unique C identifier from
(declaring type, member name, parameter types), e.g. `bl_net_Regex_Match__string_int32`.

Requirements:

- **Deterministic and stable across builds** — the mangled set is part of the cache key (§10.2).
- Total on overload sets: two overloads never collide.
- Independent of collection order.

A mechanical test pins determinism (§12.4).

---

## 8. Shim generation

### 8.1 Project shape

`NetShimGenerator` emits a csproj under `obj/gen/shim/`:

```xml
<TargetFramework>net8.0</TargetFramework>          <!-- NU1201 + AOT-analysis floor -->
<PublishAot>true</PublishAot>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
<InvariantGlobalization>true</InvariantGlobalization>
<IsAotCompatible>true</IsAotCompatible>            <!-- enables the four analyzers -->
```

plus the project's reference closure (§5). `NativeLib=Shared` and `-r win-x64` are passed on the
publish command line, matching P0's proven recipe.

Sources: `HandleTable.cs` and `BlnetStatus.cs` copied verbatim from the P0 template (fixed
scaffolding — `BlnetStatus.cs` is generated from `BlnetContract.GenerateStatusEnumCs()`, keeping
the single source), plus one generated `Exports.g.cs`.

> **AOT export rule.** Every export must physically live in the shim project — Microsoft:
> *"Methods in project references or NuGet packages won't be exported."* This is why the
> generated wrappers are thin forwarders rather than any attempt to re-export library methods
> directly. ILC auto-exports every `[UnmanagedCallersOnly]` EntryPoint, so no `.def` file is
> ever authored.

### 8.2 Export pattern

The generator emits exactly the shape the hand-written shim already proves
(`Exports.cs:82-93`): `TryGet` → early return → downcast → call → status, inside `catch`/`Fail`.

```csharp
[UnmanagedCallersOnly(EntryPoint = "bl_net_Customer_Recalculate",
                      CallConvs = new[] { typeof(CallConvCdecl) })]
static int Customer_Recalculate(ulong self, ulong order, ulong* result) {
    try {
        var st = Table.TryGet(self, out var o);   if (st != BlnetStatus.BLNET_OK) return (int)st;
        var st2 = Table.TryGet(order, out var a); if (st2 != BlnetStatus.BLNET_OK) return (int)st2;
        *result = Table.Create(((Customer)o!).Recalculate((Order)a!));
        return (int)BlnetStatus.BLNET_OK;
    } catch (Exception ex) { return Fail(ex); }
}
```

`[UnmanagedCallersOnly]` constrains every wrapper to be `static`, take **blittable arguments
only**, use no generic type parameters, and live outside any generic class. The marshaling table
(§8.3) exists to satisfy exactly that.

**The generator does not compute conversions.** It emits C# and lets `csc` resolve the call —
implicit conversions, optional parameters and `params` included. Roslyn resolving the overload
up front (§6.1) is sufficient; no separate marshaling calculus is needed.

### 8.3 Marshaling

| At the boundary | Wire form | Notes |
|---|---|---|
| Primitives, enums | by value | enum → underlying integral |
| `Boolean` | `int32` 0/1 | `bool` is not blittable for `UnmanagedCallersOnly` |
| `String` | UTF-8 `const char*` in; transfer buffer out | P0 rules: in-params borrow-and-copy; out via `blnet_alloc`, receiver frees with `blnet_free` |
| P1 `NativeOwned` | native value struct + conversion pair | §6.4 |
| Reference types, arrays | `uint64_t` handle | arrays are reference types, so this is free |
| `Nothing` / null | handle `0` | index 0 is already burned |
| Delegate parameters | callback handle via P0's thunk | §8.4 |
| Other value types | handle (boxed) | blittable-by-value is a later optimization |
| `ref` / `out` | pointer slot | `IRCall.ByRefArguments` today is populated only for resolved *user* functions; extending it is required work |

Anything outside this table is **BL6019** — a clean build error, never silence. Transparency
obliges the compiler to be explicit about what it cannot carry.

**Returned reference types** are registered with `Table.Create(...)` at refcount 1, transferring
ownership to the native `NetRef`. This rule is implied by P0's `blnet_test_create_list` but was
never written down generally; it is normative here.

### 8.4 Delegate arguments

A BasicLang lambda or `AddressOf` passed where .NET expects `Action`/`Func`/`Comparison`/
`Predicate` becomes a native callback handle, registered through P0's existing machinery. The
missing piece is entirely shim-side: a generated managed dispatcher that wraps the callback
handle in a real .NET delegate of the required type and invokes the universal thunk.

Everything native-side — the thunk, `BlnetSlotDesc` encoding, `BlnetCallScope`, inline vs queued
dispatch, `blnet_pump()` — already exists and is tested. P2a is what finally makes it live.

---

## 9. Native artifacts

### 9.1 Emitted files

```
obj/gen/  blnet.h                 P0 contract header  (first time it is ever emitted)
          blnet_runtime.hpp       P0 runtime: handle table, thunk, queue/pump
          blnet_bindings.g.hpp    proxy table struct + blnet_bind_all
          blnet_proxies.g.hpp     typed inline C++ proxies  ← the public API
          blnet_startup.g.cpp     load, handshake, bind
          shim/                   generated shim csproj + Exports.g.cs
```

Emission happens in **both** `CppCodeGenerator` modes (combined `Generate` and split
`EmitRuntimeHeader`), matching the P1 splice precedent.

### 9.2 Proxy API

```cpp
inline NetRef Customer_Recalculate(const NetRef& self, const NetRef& order) {
    uint64_t r = 0;
    NetCheck(g_net.Customer_Recalculate(self.get(), order.get(), &r));
    return NetRef(r);
}
```

`NetCheck` converts a non-`OK` status into a C++ exception carrying the managed type and message
from P0's `blnet_last_error` channel. Both consumers call these identical inline proxies — C++
consumption costs an include-path fix, not a parallel API.

### 9.3 Startup, binding, shutdown

```cpp
void blnet_startup() {
    void* m = blnet_load_module("<app>.Net.dll");        // LoadLibrary / dlopen
    if (!m)                          throw std::runtime_error("...");
    if (blnet_bind_core(m) != BLNET_OK)                  throw ...;   // P0's seven exports
    if (g_shim.abi_version() != BLNET_ABI_VERSION)       throw ...;   // handshake
    g_shim.initialize(&g_native_vtable);
    blnet_bind_all(m);                                   // the generated proxy table
}
```

Runs before user code; an ABI mismatch or a missing slot fails loudly at startup rather than at
first call. `blnet_shutdown()` runs at exit.

Emitted only when the discovered surface is non-empty — a project that uses no .NET pays
nothing and links no shim.

### 9.4 Lifetime

P0's `NetRef` RAII is reused unchanged. A BasicLang local holding a .NET object is a `NetRef`;
scope exit releases the handle and the managed `GCHandle` drops. Deterministic, with no GC
involvement on the native side.

**Known limitation:** a reference cycle spanning the boundary (native holds a handle, the
managed object holds a callback into native) leaks. Inherent to refcounting; documented, not
solved.

### 9.5 C++ consumers

`CppProjectBuilder.cs:419-420` adds `obj/gen` to the include path only when
`blSources.Count > 0`. That gate is removed so a pure-C++ project can `#include` the generated
proxies.

---

## 10. Build pipeline

### 10.1 Phase model

`EmitCore` is straight-line today and gains explicit phases:

| # | Phase | IntelliSense runs it? |
|---|---|---|
| 1 | Resolve references | yes |
| 2 | BL → IR | yes |
| 3 | Collect .NET surface | yes |
| 4 | Emit native (incl. proxy headers) | yes |
| 5 | **Generate + publish shim** | **no** |
| 6 | Compile + link | no |
| 7 | Deploy | no |

**Phases 1–4 give full C++ IntelliSense at zero publish cost.** The proxy header is pure C++
declarations and does not require the shim to exist, so clangd resolves proxies and offers
completions on a project that has never been published. F5 is the only thing that ever waits.

### 10.2 Content-hash cache

Phase 5 is keyed on a hash of:

- reference identities (path + timestamp + size, or assembly MVID)
- the resolved used-member set (mangled names — hence §7.3's determinism requirement)
- the shim template version
- TFM + RID + toolchain identity

Hit → skip entirely. The ordinary edit-run loop pays nothing. The cache manifest lives beside
the published output; a missing or unparsable manifest is a miss, never a silent stale hit.

`Clean` today deletes `bin/<config>` but not `obj/`; it must also drop the shim cache.

### 10.3 Cancellation

`CppProjectBuilder.Build` takes no `CancellationToken` today. Phase 5 can run for minutes, so
one is threaded through and honored between phases and around the publish process.

### 10.4 Deployment

The published shim is a self-contained native DLL (`NativeLib=Shared`). It is copied next to the
executable using the same `File.Copy` deployment the engine and MinGW runtime DLLs already use.
**No .NET runtime is required on the target machine** — that is transport A's whole value.

### 10.5 Environment requirements

- The **.NET SDK must be on PATH** on the developer machine (not on the end user's).
- The **VS-Installer PATH workaround must move from test code into the product.** This box sets
  `NoDefaultCurrentDirectoryInExePath=1`, which breaks `VsDevCmd.bat`'s bare `vswhere.exe` call
  inside ILCompiler's linker discovery (MSB3073 exit 123, corrupted `CppLinker` path). The fix —
  appending `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to the *child* process PATH,
  guarded by `Directory.Exists` — currently lives only in `BlnetShimPublisher` in the test
  assembly. Without it the first native build on this machine fails and looks like a P2a bug.
- First publish downloads the ILCompiler package (~1 min, network).

---

## 11. Error handling and diagnostics

### 11.1 Managed exceptions reaching BasicLang

A throwing .NET method returns `BLNET_E_MANAGED_EXCEPTION` with type and message in P0's
`[ThreadStatic]` channel; `NetCheck` rethrows as a C++ exception.

D4 requires BasicLang `Try`/`Catch` to catch it. Typed catches work because P0 already carries
the **exception type name**, so `Catch ex As ArgumentException` matches on that string.

Unchanged known limitation: a `Return` inside a `Try` still bypasses its `Finally` on the C++
backend.

### 11.2 Native exceptions inside callbacks

P0's C4 requires a native exception raised inside a callback to be rethrown into the managed
frame; recon found the test dispatcher only records a synthetic type string. Since P2a ships
delegate arguments (§8.4), a BasicLang lambda that throws inside a .NET call must surface
correctly. This is real work, not a wiring exercise.

### 11.3 The AOT ceiling → BL6020

Per D5. ILC reports IL2026 (`RequiresUnreferencedCode`) and IL3050 (`RequiresDynamicCode`)
against **generated C#**, not against `.bas` source. `NetShimGenerator` therefore emits a
**provenance map** from each mangled wrapper name to its originating BasicLang source location.
`AotDiagnosticMapper` parses ILC output, resolves the wrapper name through that map, and reports:

```
BL6020: 'Newtonsoft.Json.JsonConvert.DeserializeObject(Of T)' cannot be used under the
        AOT shim transport (IL3050: requires runtime code generation).
        Switch this project to the CoreCLR hosting transport.
        MyGame.bas(42,17)
```

Unmappable warnings are still reported, attributed to the project rather than dropped.

Until P2b ships, the suggested remedy names a transport that does not yet exist. That is
accepted deliberately: naming the real reason and the real fix is more useful than a vaguer
message, and it makes the ceiling discoverable rather than mysterious.

### 11.4 Diagnostic codes

Next free code is BL6016 (grep-verified).

| Code | Condition |
|---|---|
| BL6016 | .NET type not found |
| BL6017 | .NET member not found / no matching overload |
| BL6018 | ambiguous overload |
| BL6019 | unsupported marshaling at the boundary |
| BL6020 | AOT-incompatible member (mapped from IL2026/IL3050) |
| BL6021 | reference could not be resolved |
| BL6022 | `<NetProxy>` names an unknown type |

`CppCapabilityChecker`'s catch-all at :627-631 stops rejecting .NET types that now resolve.
`BoundaryTypeRegistry.ManagedOwned` — an empty set with zero consumers today — becomes the live
category for handle-represented types, and gains a drift invariant like `NativeOwned`'s.

---

## 12. Testing

### 12.1 Parity oracle extension — the headline gate

P1's differential oracle extends to .NET-using programs: identical `.bas` source compiled by the
C# backend and by the native path, asserting **byte-identical stdout**. This validates D4's
transparency directly, with no hand-written expectations.

Parity programs inherit P1's ten mandatory constraints — ASCII-only output, no raw `Boolean`
prints (1/0 vs True/False), no `t<N>` locals, `CType` not `CInt`, `.ToString()` not
`CStr(native)`, no module-qualified `Sub` calls, no culture-sensitive string operations (the C#
leg runs under `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, which disables ICU wholesale).

> **Documented blind spot, carried forward from P1:** a differential oracle cannot catch a bug
> that is identical on both backends. Green ≠ correct.

The §6.4 conversion pairs are the highest-value parity targets: a tick-epoch or `Decimal`
bit-layout mismatch produces a silently wrong value, which is exactly what byte-identical stdout
catches and what a unit test asserting "it returns a DateTime" does not.

### 12.2 P0's 16 scenarios stay frozen

The user framed the existing conformance suite as P2's acceptance gate; this spec reads that
deliberately narrowly, for a reason.

Recon found the harness `bind()`s all seven `blnet_test_*` literal names unconditionally and
`exit(2)`s on a miss — so *every* scenario, including the pure-contract scenario 14, requires a
shim exporting those names. Scenario 4 additionally asserts a **pristine** handle table (index 0
burned, next create reuses index 1), which any generated shim allocating at startup would break.

So the 16 scenarios remain **exactly as they are**, driven by the hand-written shim, as a P0
contract regression suite. Retrofitting a generated shim into them would make a red test
ambiguous between "the contract regressed" and "the generator emitted something wrong." Frozen,
the P0 suite only ever answers the first question.

### 12.3 A second, generated-shim conformance suite

P2a adds its own suite over a purpose-built C# test library, covering: instance and static
calls; constructors; properties; overload selection; generics; inheritance; every marshaling row
in §8.3; null/`Nothing`; `ref`/`out`; exception propagation both directions; delegate arguments;
handle lifetime and release; startup handshake failure modes.

One inversion to encode: `ShimPublishHasNoAotAnalysisWarnings` asserts
`Does.Not.Contain("warning IL")`. That assertion **scopes to the hand shim only**. For generated
shims those same warnings are *inputs* to BL6020 — the identical string means "build failure" for
one shim and "diagnostic to map" for the other, decided by which shim produced it.

### 12.4 Mechanical drift invariants

In P1's style — cheap tests that fail loudly when two things drift apart:

- mangling is deterministic and collision-free over an overload set
- the generated proxy table's slot list ≡ the generated shim's export list
- `BoundaryTypeRegistry.ManagedOwned` ≡ the handle-represented category actually used by codegen
- `BlnetStatus.cs` in the generated shim ≡ `BlnetContract.GenerateStatusEnumCs()`
- `AbiVersion` in the generated shim ≡ `BlnetContract.AbiVersion`

### 12.5 Unit and integration

Unit: resolver (overloads, generics, inheritance, accessibility), mangler, surface collector,
cache key (including that an irrelevant edit does **not** invalidate it), `AotDiagnosticMapper`
parsing real ILC output.

Integration: a BasicLang program and a hand-written `.cpp` both calling the same C# test
library; a delegate round-trip; a cold-cache then warm-cache build proving phase 5 is skipped.

---

## 13. ABI stability

**P2a holds `AbiVersion = 1`.** Every new export is a new name, not a change to an existing
one, and the proxy table is generated rather than part of `blnet.h`'s fixed surface. Nothing in
this design forces a bump.

This is stated as a goal because a bump is expensive: it breaks conformance scenario 14 plus
three drift tests until both sides move in lockstep. If P2a discovers a genuine need, the bump
is made deliberately and the cost is paid explicitly — never as a side effect.

Accepted consequence: a wrong-typed handle hits the blind downcast and surfaces as
`InvalidCastException` → `BLNET_E_MANAGED_EXCEPTION`, not a distinct status. A dedicated status
would cost a bump, and this condition can only arise from a compiler bug — the resolver already
proved the type — so it is not worth the version.

---

## 14. Known limitations shipped by P2a

1. **Reflection-heavy libraries do not work** — structural to AOT (§1.2). Reported as BL6020,
   fixed by P2b.
2. **Boundary-spanning reference cycles leak** (§9.4).
3. **Events and interface implementation are absent** (D6) — a program using them fails to build.
4. **Arbitrary value types cross as boxed handles**, not by value (§8.3).
5. **`Return` inside `Try` bypasses `Finally`** on the C++ backend — pre-existing (§11.1).
6. **C++ consumers must declare their surface** via `<NetProxy>` (§7.2).
7. **`InvariantGlobalization=true`** is inherited from P0's recipe: anything the shim formats is
   invariant-culture only.
8. **A wrong-typed handle has no distinct status** (§13).

---

## 15. Open items — resolve during planning

| # | Item |
|---|---|
| 15.1 | **AOT publish wall-clock is unmeasured anywhere in the repo.** Measure it in plan task 1; the number decides whether §10.2's cache is sufficient or a background pre-warm is needed sooner. |
| 15.2 | Roslyn version alignment: 4.9.2 matches the test project, but the compiler is shipped in `IDE/`. Confirm no conflict with `OmniSharp.Extensions.LanguageServer` 0.19.9 and measure the size delta to `BasicLang.exe`. |
| 15.3 | Whether tightening `IsNetType` breaks an existing test — recon could not determine whether any test pins the analyzer's permissiveness. |
| 15.4 | `blnet_initialize` is `const BlnetNativeVtable*` in the header but `void*` in the shim (`Exports.cs:36`). Deliberate for AOT blittability, or drift? Untested either way. |
| 15.5 | Generic instantiation strategy under AOT: `MakeGenericType` throws for non-pregenerated instantiations, so every generic instantiation the program uses must appear statically in the shim. Confirm the surface collector captures instantiations, not just open generic definitions. |
| 15.6 | `[ThreadStatic]` last-error is never cleared on `BLNET_OK`, so a caller reading it after success gets a stale unrelated error. Pre-existing; decide whether P2a tightens it. |
| 15.7 | Whether a capability rejection also breaks IntelliSense emission (codegen still runs at `CppProjectBuilder.cs:293-296` with `forIntelliSense: true`) — inferred by recon, not verified. |
| 15.8 | Latent bug spotted in passing: `WorkspaceManager.cs:186` builds the package path without lowercasing the version while `PackageManager.GetPackagePath` lowercases both — LSP package-type loading may silently miss. Out of scope; chip it. |

---

## 16. What P2b inherits

Everything in §5 through §7, §9.1–§9.4, §10.1–§10.3, §11.1–§11.2, §11.4 and §12.1 is
transport-neutral and is reused unchanged. P2b replaces only:

- `NetShimGenerator` → a fixed generic reflection dispatcher (no per-project codegen)
- `NetShimPublisher` → `hostfxr` bootstrap; no publish step in the build at all
- `blnet_bind_all` → `load_assembly_and_get_function_pointer` per slot
- `AotDiagnosticMapper` → dropped; the ceiling it reports does not exist under hosting

and adds runtime discovery (`DOTNET_ROOT` handling), a "no .NET runtime installed" diagnostic,
and `.runtimeconfig.json` deployment via `<EnableDynamicLoading>`.

If P2b requires a change to anything else listed above, the seam was in the wrong place.
