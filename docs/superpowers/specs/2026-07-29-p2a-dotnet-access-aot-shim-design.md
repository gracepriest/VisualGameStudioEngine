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
run natively with no .NET involved. `BoundaryTypeRegistry` categorizes **exactly these six** as
`NativeOwned` (`BoundaryTypeRegistry.cs:67-70`).

`String`, `Console`, `List(Of T)` and `Dictionary` were already native *before* P1, but they are
categorized differently and P2a must not conflate the two ideas: `String` is **`Bridged`**
(`BoundaryTypeRegistry.cs:50`), while `Console`, `List` and `Dictionary` are **absent from the
registry entirely** (`Unknown`) and are accepted by name at `CppCapabilityChecker.cs:620-623`.

> "Runs natively" and "`NativeOwned`" are not the same set. §6.4's conversion-pair rule keys on
> the `NativeOwned` six; anything keyed on "is it native" instead would silently mishandle
> `String`.

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
| Generated artifacts unreachable from pure C++ | The **blocking** gate is `CppProjectBuilder.cs:267` — the whole codegen + `obj/gen` create/clean/write block sits inside `if (blSources.Count > 0)` (`:267-341`), so a zero-`.bas` project emits nothing at all. The include path at `:419-420` is only the last of four gated items (§9.5). |
| No cross-project compilation | `<ProjectReference>` is parsed (`ProjectFile.cs:203-208`) and round-tripped (`:338-341`), but its only compiler-side consumer is `LSP/WorkspaceManager.cs:147-157`; the generated csproj emits only `<Reference>`/`<PackageReference>` (`Program.cs:598-629`); the IDE filters project refs out (`BuildService.cs:723-724`). Out of scope — §5. |
| Shim scaffolding lives only in the test assembly | `HandleTable.cs` exists solely at `VisualGameStudio.Tests/TestAssets/BlnetTestShim/`, unreachable from the shipped compiler (§8.1). |
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
| `NetProxyEmitter` | `BasicLang/Compiler/CodeGen/Net/` | yes | proxy header + binding table + the `BlnetCallScope` at every proxy site (§9.2) |
| `BlnetShimSources` | `BasicLang/Compiler/CodeGen/Net/` | yes | the shim's fixed C# scaffolding as string constants, mirroring `BlnetRuntimeSources` (§8.1) |
| `NetShimGenerator` | `BasicLang/Compiler/CodeGen/Net/` | **no** | emits the shim C# project, incl. the managed delegate dispatcher (§8) |
| `NetShimPublisher` | `BasicLang/Compiler/CodeGen/Net/` | **no** | AOT publish (§10) |
| `AotDiagnosticMapper` | `BasicLang/Compiler/CodeGen/Net/` | **no** | ILC diagnostics → BL6020 (§11.3) |

`CppCodeGenerator` additionally owns the native half of §8.4: lowering a BasicLang lambda or
`AddressOf` to a `NativeCallbackFn` with computed `BlnetSlotDesc[]`/`CallbackFlags`, and emitting
`blnet_callback_release` at the end of the registration's lifetime.

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
| `<ProjectReference>` | **not supported in P2a — BL6021** (see below) |
| (implicit) framework | targeting pack for net8.0 |

Changes required:

- `Program.cs:436` and `BuildService.cs:449` must stop returning before reference resolution on
  the native path. The comment asserting "C++ projects have no NuGet dependencies and skip
  restore entirely" is now false and must go.
- A reference that cannot be resolved is **BL6021**, not silence.
- `NetReferenceResolver` rejects `<ProjectReference>` with BL6021 naming the workaround.

> **Why `<ProjectReference>` is out.** "Build the referenced project first, then use its output
> assembly" is *cross-project compilation*, and it does not exist on any BasicLang build path.
> The element is parsed (`ProjectFile.cs:203-208`) and round-tripped (`:338-341`), but its only
> compiler-side consumer is `LSP/WorkspaceManager.cs:147-157`; the generated csproj emits only
> `<Reference>`/`<PackageReference>` (`Program.cs:598-629`); the IDE filters project references
> out (`BuildService.cs:723-724`); `CppProjectBuilder` reads no reference item at all; and
> `BuildService.GetBuildOrder` (`:242-288`) orders a `.blsln` but wires no assembly and is
> unreachable from `BasicLang/`. Pulling that subsystem in would roughly double P2a.
>
> **Workaround today:** reference the sibling project's built assembly with `<Reference>` +
> `<HintPath>`. Recorded as §14 limitation 9 and §15 open item 15.9.

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

### 6.5 Name binding and namespace context

Resolution needs an input on both sides — which namespaces an unqualified name is searched in,
and what Roslyn type a BasicLang argument presents for overload resolution. Neither is inferable
from §6.1 alone, and getting the first one wrong silently breaks working programs.

**Unqualified .NET type names resolve against, in order:**

1. The source file's `Using` directives, including aliases — `IRModule.NetUsings`
   (`IRNodes.cs:1166`), populated at `IRBuilder.cs:208-211`.
2. **The same ambient set the C# backend auto-imports.** `CSharpBackend.cs:184` unconditionally
   seeds `System.Text.RegularExpressions`, and a substring-triggered table at `:490` maps `Regex`
   to that namespace. Without this step §6.3's "valid programs behave identically on both
   backends" is **false**: `Dim r As New Regex("a")` with no `Using` compiles on the C# backend
   today and would become BL6016 natively.
3. Fully-qualified names, which bypass both.

The ambient set becomes **one shared constant** consumed by both `NetTypeResolver` and
`CSharpBackend`, with a §12.4 invariant asserting the two are equal — otherwise the backends
drift apart exactly where parity is being claimed.

**Precedence.** `String` (`Bridged`) and `List`/`Dictionary`/`HashSet` (accepted by name at
`CppCapabilityChecker.cs:620-623`) must **never** be captured by closure-wide resolution and
routed through the shim. Native handling wins; the resolver is consulted only for names native
handling does not claim.

**Superseded lexical mechanisms.** Three existing heuristics are replaced on the native path and
retained (warning-only) on the C# path, per §6.3: `IsNetType`'s PascalCase catch-all
(`SemanticAnalyzer.cs:2047-2052`), `CommonNetTypes` (`:68-87`), and the `_netNamespaces.Count > 0`
unresolved-base gate (`:2522`).

**Argument side.** The admissible BasicLang static types for overload resolution are exactly
§8.3's rows plus §6.4's conversion pairs, projected through `TypeMapper`. `Nothing` participates
as a null literal. A user-defined BasicLang class is **never** an admissible .NET argument under
P2a (§1.4; `Object` is `Rejected`) and yields BL6017/BL6019.

**Ambiguity.** Roslyn's `GetTypeByMetadataName` returns null when two referenced assemblies
declare the same full name, which would otherwise degrade into a misleading BL6016. An ambiguous
.NET **type** reference is **BL6023** (BL6018 covers ambiguous *overloads* only).

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

An unknown type is **BL6022**.

**"Full public surface" means, precisely:** public constructors, methods, properties and fields
declared on the type **and on its base types**, static and instance — **excluding**
`System.Object`'s members unless overridden and marshalable. `[Obsolete]` members are included
(omitting them would silently diverge from what the C# backend can call).

**Unmarshalable and AOT-hostile members are omitted, not errors.** A member whose signature
contains a type §8.3 cannot carry, or which ILC reports as AOT-incompatible, is skipped with an
informational note naming type, member and offending type. It is **not** BL6019 and **not**
BL6020. Two consequences worth stating plainly:

- The generated proxy overload set is a **subset** of the .NET overload set.
- Without this rule the feature is unusable: one `RequiresDynamicCode` member would make an
  entire declared type unbuildable, and `<NetProxy Include="…Regex" />` — the spec's own example
  — would fail on `IsMatch(ReadOnlySpan<char>)` and on inherited `Equals(Object)`.

BL6019/BL6020 fire only when BasicLang source, or a member actually reached, *uses* the
unmarshalable thing (§8.3).

D4's transparency promise was about BasicLang *source*, and it stays intact there. The
asymmetry is honest: infer where inference is possible, declare where it is not.

### 7.3 Mangling

`NetNameMangler` produces a legal, unique C identifier from
(declaring type, member name, parameter types), e.g. `bl_net_Regex_Match__string_int32`.

Requirements:

- **Deterministic and stable across builds** — the mangled set is part of the cache key (§10.2).
- **Collision-free over the fully-qualified declaring type**, not the short name. Both worked
  examples above use short names and would collide across namespaces (`MyLib.Customer` vs
  `OtherLib.Customer`); the mangler must incorporate the namespace.
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
<TrimmerSingleWarn>false</TrimmerSingleWarn>       <!-- REQUIRED - see below -->
```

plus the project's reference closure (§5). `NativeLib=Shared` and `-r win-x64` are passed on the
publish command line, matching P0's proven recipe.

> **`TrimmerSingleWarn=false` is load-bearing for D5.** The SDK default (`true`) collapses every
> trim/AOT warning from a packaged assembly into a *single* assembly-level IL2104/IL3053 with no
> member name — leaving `AotDiagnosticMapper` (§11.3) nothing to attribute. Without this
> property the mapped-to-`.bas` diagnostic D5 promises is unachievable for exactly the packages
> that need it most.

**Sources — and where they live.** The shim needs `HandleTable.cs` and `BlnetStatus.cs` as fixed
scaffolding, plus one generated `Exports.g.cs`. Today `HandleTable.cs` exists **only** at
`VisualGameStudio.Tests/TestAssets/BlnetTestShim/HandleTable.cs`, which the shipped compiler
cannot reach — "copied verbatim from the P0 template" is not executable. P2a adds
`BasicLang/Compiler/CodeGen/Net/BlnetShimSources.cs`, mirroring `BlnetRuntimeSources.cs`, to carry
that scaffolding as string constants in the product. `BlnetStatus.cs` continues to be generated
from `BlnetContract.GenerateStatusEnumCs()`, preserving the single source, and §12.4 gains an
invariant so the generated handle model cannot drift from the hand shim that the frozen P0 suite
validates.

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
        object? o = null, a = null;
        if (self != 0) { var st = Table.TryGet(self, out o);  if (st != BlnetStatus.BLNET_OK) return (int)st; }
        if (order != 0) { var st = Table.TryGet(order, out a); if (st != BlnetStatus.BLNET_OK) return (int)st; }
        var rv = ((Customer)o!).Recalculate((Order?)a);
        *result = rv is null ? 0UL : Table.Create(rv);
        return (int)BlnetStatus.BLNET_OK;
    } catch (Exception ex) { return Fail(ex); }
}
```

**Handle `0` means null and must never reach the table.** `HandleTable.Validate` rejects index 0
(`HandleTable.cs:80`), so an unconditional `TryGet` would turn `Nothing` into
`BLNET_E_STALE_HANDLE` instead of passing null; and `Table.Create(null)` would mint a live
non-zero handle for a null return. Hence the guarded decode and the `rv is null ? 0UL` encode
above. The rule applies to **every** handle-shaped slot: §8.3's `ref`/`out` pointer slots (a null
`out` writes 0; a 0 read from a `ref` slot decodes to null) and the §8.4 dispatcher's
handle-typed arguments and results.

`HandleTable` itself is unchanged — it still reports `BLNET_E_STALE_HANDLE` for handle 0, so
P0's `ZeroHandle_IsAlwaysStale` stays green. Consequence worth stating: a null *receiver* reaches
the managed cast and surfaces as `NullReferenceException` → `BLNET_E_MANAGED_EXCEPTION`, which
matches what the C# leg does — it must not surface as a stale-handle error.

`[UnmanagedCallersOnly]` constrains every wrapper to be `static`, take **blittable arguments
only**, use no generic type parameters, and live outside any generic class. The marshaling table
(§8.3) exists to satisfy exactly that.

**The generator does not compute conversions.** It emits C# and lets `csc` resolve the call —
implicit conversions, optional parameters and `params` included. Roslyn resolving the overload
up front (§6.1) is sufficient; no separate marshaling calculus is needed.

### 8.3 Marshaling

| At the boundary | Wire form | Notes |
|---|---|---|
| Primitives (**numeric**), enums | by value | enum → underlying integral |
| `Boolean` | `int32` 0/1 | `bool` is not blittable for `UnmanagedCallersOnly` |
| `Char` | `uint16_t` (UTF-16 code unit) | native `Char` is a **1-byte** C++ `char` (`TypeMapper.cs:215`, `CppCodeGenerator.cs:1803`); .NET's is 2 bytes, and neither is blittable at 1 byte. Outbound zero-extends; inbound narrows — see the divergence below |
| `String` | UTF-8 `const char*` in; transfer buffer out | P0 rules: in-params borrow-and-copy; out via `blnet_alloc`, receiver frees with `blnet_free` |
| P1 `NativeOwned` | native value struct + conversion pair | §6.4 |
| .NET reference type **already held as a `NetRef`** | `uint64_t` handle | pass-through; genuinely free. Consuming it is §8.5 |
| **Native** BasicLang array / `List` / `Dictionary` / `HashSet` | materialize by copy, or BL6019 | §8.6 — these have **no handle**: `std::vector<T>` (`CppCodeGenerator.cs:476-480`) and `shared_ptr<BasicLang::…>` (`:580-585`) |
| `Nothing` / null | handle `0` | never reaches the table — §8.2 |
| Delegate parameters | callback handle via P0's thunk | §8.4 |
| Other **non-`ref`** value types | handle (boxed) | blittable-by-value is a later optimization |
| `ref struct` — `Span<T>`, `ReadOnlySpan<T>`, `Regex.ValueMatchEnumerator`, … | **not marshalable — BL6019** | cannot be boxed; `GCHandle.Alloc(object)` (`HandleTable.cs:26`) cannot take one |
| `ref` / `out` | pointer slot | `IRCall.ByRefArguments` today is populated only for resolved *user* functions; extending it is required work |

**When BL6019 fires.** A type outside this table is an error **only when it is actually used** —
reached from a BasicLang call site, or required by a member the program calls. A `<NetProxy>`
declaration (§7.2) instead *omits* the unmarshalable member with an informational note. Without
this scoping the spec is self-contradictory: `Regex` alone would fail the build on
`IsMatch(ReadOnlySpan<char>)`, `Count(ReadOnlySpan<char>)` and `EnumerateMatches`, and every
type would fail on its inherited `Equals(Object)` since `Object` is permanently `Rejected`.

> **`Char` divergence (§14.10).** A .NET method returning a code unit above `U+00FF` cannot fit
> BasicLang's 1-byte native `Char`. Where the value is statically known the compiler reports
> BL6019; otherwise the narrowing is lossy and is recorded as a shipped divergence. This is
> exactly the silent-wrong-value class §12.1 exists to catch, so it gets a parity program.

**Returned reference types** are registered with `Table.Create(...)` at refcount 1, transferring
ownership to the native `NetRef`. This rule is implied by P0's `blnet_test_create_list` but was
never written down generally; it is normative here.

### 8.4 Delegate arguments

A BasicLang lambda or `AddressOf` passed where .NET expects `Action`/`Func`/`Comparison`/
`Predicate` becomes a native callback handle, registered through P0's existing machinery.

P0 built the *transport* for this, not the whole feature. Three pieces are new:

| Piece | Owner | What |
|---|---|---|
| managed dispatcher | `NetShimGenerator` | wraps a callback handle in a real .NET delegate of the required type and invokes the universal thunk |
| **call scope at every proxy site** | `NetProxyEmitter` | see §9.2 — without it every callback misdispatches |
| lambda lowering + release | `CppCodeGenerator` | BasicLang lambda/`AddressOf` → `NativeCallbackFn` plus computed `BlnetSlotDesc[]`/`CallbackFlags`; `blnet_callback_release` at end of registration lifetime |

Generated callbacks register with `immediate = false`; `Immediate` remains P0's rare opt-in and
is never set by codegen.

Everything else — the thunk, `BlnetSlotDesc` encoding, `BlnetCallScope`, inline vs queued
dispatch, `blnet_pump()` — already exists and is tested.

### 8.5 Consuming handle-represented collections

A .NET array or collection arrives as an opaque handle, and **a handle supports no operation the
surface collector did not emit an export for.** Indexing, iteration and `Length` are not free
just because the transport is.

| Shape | Rule |
|---|---|
| `T[]` | .NET arrays expose **no indexer in metadata**, so the collector emits **synthetic** exports per element type: `bl_net_Array_Get__<T>__int32`, `_Set`, `_Length`. `Array.GetValue(int)` is not a fallback — it returns `Object`, which is permanently `Rejected` (`BoundaryTypeRegistry.cs:58`) |
| indexer property | an `ArrayAccessExpressionNode` on a resolved .NET type lowers to `get_Item`/`set_Item`, which the collector must collect **even though the source never names it** |
| `For Each` | the collector must synthesize `GetEnumerator`/`MoveNext`/`get_Current`/`Dispose` |

**Managed vs native `List` must not be decided by name.** `CppCodeGenerator.cs:3676-3678` and
`:3698-3700` key on the bare string `"List"`, and `MapType` sends `TypeKind.Array` to
`std::vector<T>` (`:479-480`). A managed `List<T>` handle reaching those paths would be
dereferenced as a `shared_ptr` — a wild pointer, not a diagnostic. Codegen therefore branches on
a **category marker carried on the IR type**, never on a type name.

Lowering sites needing a handle branch: `Visit(IRIndexerAccess)` (`CppCodeGenerator.cs:3662-3681`),
`Visit(IRIndexerStore)` (`:3683-3703`), `Visit(IRForEach)` (`:3634-3660`), and the `MapType`
array branch (`:479-480`).

### 8.6 Native BasicLang collections crossing outbound

The reverse direction of §8.5, and a different problem: a BasicLang array is a `std::vector<T>`
and `List`/`Dictionary`/`HashSet` are `shared_ptr<BasicLang::…>`. There is no `GCHandle`, no
handle, and `BoundaryTypeRegistry.Categorize` returns `Unknown` for all of them.

**v1 rule — materialize by copy, one-way, for simple element types:**

- `T[]` where `T` is a by-value row of §8.3 or `String` → copied into a fresh managed array by
  generated shim helpers (`bl_net_array_new_int32(int32_t count, const int32_t* src, uint64_t* out)`
  and a `String` variant per element wire form). A `T[]` **return** or `ref`/`out` array slot is
  read back by the mirrored helper.
- Everything else — `List`/`Dictionary`/`HashSet`, nested element types such as
  `List(Of List(Of Integer))`, element types that are themselves handles — is **BL6019** naming
  the parameter and the offending type.

> **Divergence (§14.11):** the copy is **one-way**. Mutations a .NET callee makes to the array
> are not visible in the caller's native `std::vector`, where the C# backend would see them.
> §12.1 gets a parity program that mutates an array inside a .NET call, precisely so this
> divergence is pinned rather than discovered.

Generalizing §6.4's principle: **any type with a native C++ representation and no handle cannot
cross as a handle.** That is not limited to the `NativeOwned` six — arrays and the collection
wrappers fall under it too, even though they are `Unknown` to the registry
(`BoundaryTypeRegistry.cs:74-83`).

---

## 9. Native artifacts

### 9.1 Emitted files

```
obj/gen/  blnet.h                 P0 contract header  (first time it is ever emitted)
          blnet_runtime.hpp       P0 runtime: handle table, thunk, queue/pump
          blnet_bindings.g.hpp    proxy table struct + blnet_bind_all
          blnet_proxies.g.hpp     typed inline C++ proxies  ← the public API
          blnet_startup.g.cpp     load, handshake, bind
          shim/                   generated csproj + Exports.g.cs + the delegate dispatcher,
                                  plus HandleTable.cs / BlnetStatus.cs from BlnetShimSources
```

`NetProxyEmitter` **owns** this artifact set and produces it keyed on the discovered surface
(§7.1 + §7.2) — **not** on the presence of BasicLang sources. `EmitCore` merges it with
`GenerateSplit`'s output when `.bas` files exist; see §9.5 for the four gates this requires
moving.

Emission happens in **both** `CppCodeGenerator` modes (combined `Generate` and split
`EmitRuntimeHeader`), matching the P1 splice precedent.

### 9.2 Proxy API

```cpp
inline NetRef Customer_Recalculate(const NetRef& self, const NetRef& order) {
    uint64_t r = 0;
    int32_t st;
    {
        BasicLang::blnet::BlnetCallScope scope;          // REQUIRED - see below
        st = g_net.Customer_Recalculate(self.get(), order.get(), &r);
    }
    NetCheck(st);                                        // outside the scope: it throws
    return NetRef(r);
}
```

**Every call through `g_net` must hold a `BlnetCallScope` across the managed call.** P0's thunk
classifies a callback as cross-thread when `g_call_depth == 0` (`BlnetRuntimeSources.cs:221-225`,
scope at `:103-104`). Without the scope, a result-bearing delegate fails
`BLNET_E_CROSS_THREAD_RESULT` and an `Action` is silently **queued for a later `blnet_pump()`**
instead of running inside `List.Sort` — so every delegate argument P2a ships would misdispatch.
This is normative for generated proxies *and* for hand-written C++ calling `g_net` directly.

`NetCheck` sits outside the scope because it throws; unwinding through the scope's destructor
while it is still counted would corrupt the depth counter.

`NetCheck` converts a non-`OK` status into a C++ exception carrying the managed type and message
from P0's `blnet_last_error` channel. Both consumers call these identical inline proxies — C++
consumption costs an include-path fix, not a parallel API.

Each proxy also **guards its slot**: a null function pointer means `blnet_startup()` never ran,
and must produce a clear diagnostic rather than a null-pointer jump.

### 9.3 Startup, binding, shutdown

```cpp
void blnet_startup() {
    void* m = blnet_load_module("<app>.Net.dll");        // NEW - LoadLibrary / dlopen
    if (!m)                                              throw ...;  // BL-runtime error
    if (blnet_bind_core(m) != BLNET_OK)                  throw ...;  // NEW - P0's seven exports
    if (g_shim.abi_version() != BLNET_ABI_VERSION)       throw ...;  // handshake
    g_shim.initialize(BLNET_ABI_VERSION, &g_native_vtable);          // g_native_vtable is NEW
    blnet_bind_all(m);                                   // the generated proxy table
}
```

`initialize` takes **two** arguments — its frozen P0 signature is
`initialize(int32_t expected_abi, const BlnetNativeVtable*)` (`BlnetRuntimeSources.cs:66,93`).
`blnet_load_module`, `blnet_bind_core` and `g_native_vtable` **do not exist in the repo today**
and are new work in P2a; only `g_shim` and its member signatures are pre-existing.

Each failure path has a specified message, stream and exit code so §12.3's handshake tests have
something to assert against; the plan fixes the exact text.

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

Removing the include-path line alone does **not** make a pure-C++ project work. **Four** things
sit inside `if (blSources.Count > 0)` in `CppProjectBuilder.EmitCore` (`:267-341`):

| Anchor | What is gated |
|---|---|
| `:323-325` | `Directory.CreateDirectory(objGenDir)` + `CleanGeneratedDir` |
| `:326-327` | the `obj/gen` writes |
| `:338-340`, `:414` | `generatedTus` population feeding `request.SourceFiles` — so `blnet_startup.g.cpp` would be emitted but **never compiled or linked** |
| `:419-420` | the include path |

All four change to `surface.IsNonEmpty || blSources.Count > 0`.

`NetProxyEmitter` **owns** the six `obj/gen` artifacts (§9.1) and produces them keyed on the
discovered surface, not on the presence of BasicLang sources; `EmitCore` merges that set with
`GenerateSplit`'s output when BL sources exist. The P1 splice precedent applies to the **merge**,
not to the gating — neither `GenerateSplit` nor `EmitRuntimeHeader` runs at all with zero `.bas`
files.

**Who calls `blnet_startup()`.** Two entry-point owners exist: generated `main` when
`emitMain == true` (`CppProjectBuilder.cs:262`), and a user-written C++ `main()` when it is false.
A static-initializer object in `blnet_startup.g.cpp` covers both without the user having to
remember anything, at the cost of a static-initialization-order constraint: it must not be
touched by another translation unit's static initializer. That constraint is documented, the
proxies' null-slot guard (§9.2) turns any violation into a clear error rather than a crash, and
`blnet_shutdown()` runs from the same object's destructor.

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

- reference identities as **assembly MVID** — not path + timestamp + size, which can collide on
  a rebuilt assembly whose content changed but whose size and stamp did not
- the resolved used-member set (mangled names — hence §7.3's determinism requirement)
- the shim template version (`BlnetShimSources` + `BlnetContract.AbiVersion`)
- TFM + RID + toolchain identity + **.NET SDK and ILCompiler version**

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

D4 requires BasicLang `Try`/`Catch` to catch it — and **typed catch does not work today.**
`MapCatchType` (`CppCodeGenerator.cs:3589-3593`) emits `std::exception` for `Exception` and
`std::runtime_error` for *every other* type name, so all typed catches collapse into a single
handler. A bare type-name comparison would not fix this either, because
`Catch ex As Exception` must also catch an `ArgumentNullException` — that is subclass matching,
which a string equality test cannot do.

P2a therefore specifies exception-type matching as real work:

1. The shim reports the thrown exception's **full inheritance chain**, most-derived first, as a
   `;`-separated string — `ArgumentNullException;ArgumentException;SystemException;Exception` —
   through P0's existing error channel.
2. `NetCheck` throws a `BasicLang::NetException` carrying that chain plus the message.
3. A BasicLang `Catch ex As T` where `T` resolves to a .NET type lowers to a handler that
   catches `NetException` and **rethrows unless `T`'s full name appears in the chain**. Catch
   clauses naming non-.NET types keep today's behavior unchanged.

**A BasicLang-thrown exception of a .NET-named type is *not* a `NetException`.**
`Throw New ArgumentException(...)` written in BasicLang lowers to `std::runtime_error`
(`CppCodeGenerator.cs:3596-3612`), never to a `NetException` carrying a chain. So a
`Catch ex As ArgumentException` handler must match **both** shapes: a `NetException` whose chain
contains `ArgumentException`, *and* a locally-thrown BasicLang exception of that name. The
generated handler tests the chain first and falls through to the existing behavior — otherwise
adding .NET interop to a file would silently stop its own `Throw`s being caught.

**This costs no ABI change.** It alters the *content* of an existing field, not any signature,
and it is backward compatible: the hand-written shim keeps sending a bare type name, which is a
valid one-element chain. §12.2's frozen P0 suite — which asserts on
`BasicLangNativeException` — therefore stays green untouched.

Unchanged known limitation: a `Return` inside a `Try` still bypasses its `Finally` on the C++
backend.

### 11.2 Native exceptions inside callbacks

P0's C4 requires a native exception raised inside a callback to be rethrown into the managed
frame; recon found the test dispatcher only records a synthetic type string. Since P2a ships
delegate arguments (§8.4), a BasicLang lambda that throws inside a .NET call must surface
correctly. This is real work, not a wiring exercise.

### 11.3 The AOT ceiling → BL6020

Per D5. ILC reports its trim/AOT diagnostics against **generated C#**, not against `.bas` source,
so `NetShimGenerator` emits a **provenance map** from each mangled wrapper name to its
originating BasicLang source location, and `AotDiagnosticMapper` scans **all** `ILxxxx` trim/AOT
diagnostics — not a two-code allowlist.

**Attribution is three-tier, because full `.bas` attribution is not always achievable:**

| Tier | When | Report |
|---|---|---|
| 1 | the warning's origin member **is** a generated wrapper | resolve through the provenance map; report at the `.bas` call site |
| 2 | origin is inside a **referenced assembly** | name assembly + origin member, attributed to the project — a wrapper-keyed map cannot resolve it |
| 3 | assembly-level aggregate (IL2104 / IL3053) | name the assembly only |

Only tier 1 exists in any form today. A tier-1 example:

```
BL6020: 'System.Type.MakeGenericType' cannot be used under the AOT shim transport
        (IL3050: requires runtime code generation).
        Switch this project to the CoreCLR hosting transport.
        MyGame.bas(42,17)
```

and a tier-3 example:

```
BL6020: assembly 'Newtonsoft.Json' is not AOT-compatible (IL3053, aggregated).
        Switch this project to the CoreCLR hosting transport.
```

**The runtime backstop matters.** A library whose incompatibility ILC cannot *see* — un-annotated
reflection — produces **no build diagnostic at all** and fails at runtime as a managed exception,
surfacing through §11.1. D5's promise is therefore "reported as BL6020 at whatever granularity
ILC attributes the warning", not "always at your `.bas` line". §14.1 states this plainly rather
than overselling it.

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
| BL6020 | AOT-incompatible member (mapped from any ILC trim/AOT diagnostic — §11.3) |
| BL6021 | reference could not be resolved, or `<ProjectReference>` used (§5) |
| BL6022 | `<NetProxy>` names an unknown type |
| BL6023 | ambiguous .NET **type** reference (§6.5) |

**Un-rejection takes three sites, and the catch-all is not the blocking one.** `CheckType`
returns at `CppCapabilityChecker.cs:614-618` before ever reaching the `:627-631` catch-all, and
`New Regex("x")` is separately rejected by the `IRNewObject` closure at `:322-331`. All three
must change or §4.2's `Regex_Match__string` slot and §7.2's `Regex` example cannot build.
`Object` stays rejected under any category change — it is hard-checked by name at `:606-610`, so
§6.4's promise holds mechanically.

**`ManagedOwned`'s population rule.** `BoundaryTypeRegistry` **remains a static, curated,
simple-name-keyed table**; it does not become a per-compilation registry. `Normalize`
(`BoundaryTypeRegistry.cs:96-101`) keys on simple names, so an arbitrary per-project type set
cannot be represented without collisions (`MyLib.Customer` vs `OtherLib.Customer`). Division of
authority:

- **`NetTypeResolver`** is the authority for arbitrary .NET types. They stay `Unknown` to the
  registry **by design** and are handle-represented by §8.3's wire-form rule.
- **The registry** changes only by moving the five P2-territory names in `Rejected`
  (`BoundaryTypeRegistry.cs:59` — `Regex`, `Uri`, `Stream`, `FileInfo`, `DirectoryInfo`) into
  `ManagedOwned`. `Object` stays permanently `Rejected` (`:58`).

`Categorize` evaluates `NativeOwned → ManagedOwned → Bridged → Rejected` (`:78-81`), so an
overlap between `ManagedOwned` and `Rejected` would resolve silently — §12.4 therefore asserts
they are **disjoint**.

**Known test churn** (P1-style, to be updated in the same task, not discovered later):
`BlnetContractTests.ManagedOwned_StillEmpty` (`:169-173`), `TodaysRejectList_IsRejected`
(`:111-115`), plus `:136-137` and `:149`; and `CppCollectionTests.cs:194/1050/1064` and
`CppBackendTests.cs:292`, which pin the exact types §4.2 and §7.2 use as worked examples.

---

## 12. Testing

### 12.1 Parity oracle extension — the headline gate

P1's differential oracle extends to .NET-using programs: identical `.bas` source compiled by the
C# backend and by the native path, asserting **byte-identical stdout**. This validates D4's
transparency directly, with no hand-written expectations.

Parity programs inherit **P1's mandatory constraint list verbatim** — see
`docs/superpowers/plans/2026-07-27-p1-native-bcl-types.md`, Task 13. It is cited rather than
re-enumerated here deliberately: a partial restatement is how the two lists drift.

**Required parity programs specific to P2a**, each pinning something that would otherwise be a
silent wrong answer rather than a crash:

| Program | Pins |
|---|---|
| the six §6.4 conversion pairs round-tripped through a .NET call | tick epochs and `Decimal` bit layout — the highest-value target |
| multiple `Catch` clauses around a throwing .NET call, **including a subclass match** (`Catch ex As Exception` catching an `ArgumentNullException`) | §11.1's new inheritance-chain lowering, which otherwise ships unpinned |
| a `Throw New ArgumentException` caught locally in a file that also calls .NET | §11.1's dual-shape handler — that interop does not break a file's own throws |
| an array mutated inside a .NET call | §8.6's one-way-copy divergence (§14.11) |
| a `Char` round-trip, including a value above `U+00FF` | §8.3's narrowing divergence (§14.10) |
| `Nothing` passed and returned across the boundary | §8.2's handle-`0` rule |

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
calls; constructors; properties; overload selection; generics; inheritance; **every** marshaling
row in §8.3 including `Char` and a `ref struct` rejection; null/`Nothing` as argument, return and
receiver; `ref`/`out`; exception propagation both directions **including typed and subclass
catch**; handle lifetime and release; startup handshake failure modes.

Two rows need stating explicitly because the obvious version of the test passes with the bug
present:

- **Delegate arguments must include a result-bearing delegate** (`Comparison`/`Func`/`Predicate`)
  invoked synchronously inside a managed call, asserting it ran **inline** and returned its
  value. An `Action`-only test passes even when §9.2's `BlnetCallScope` is missing, because a
  queued `Action` still eventually runs.
- **Collection consumption** (§8.5): element read/write on a .NET `T[]`; indexer read/write on a
  `List<T>` and on a user type; `For Each` over a .NET array and over an `IEnumerable<T>`; and
  the managed-vs-native `List` disambiguation case, which is the one that currently produces a
  wild pointer rather than an error.

One inversion to encode: `ShimPublishHasNoAotAnalysisWarnings` asserts
`Does.Not.Contain("warning IL")`. That assertion **scopes to the hand shim only**. For generated
shims those same warnings are *inputs* to BL6020 — the identical string means "build failure" for
one shim and "diagnostic to map" for the other, decided by which shim produced it.

### 12.4 Mechanical drift invariants

In P1's style — cheap tests that fail loudly when two things drift apart:

- mangling is deterministic and collision-free over an overload set **and over fully-qualified
  declaring types** (§7.3)
- the generated proxy table's slot list ≡ the generated shim's export list
- for every name in `ManagedOwned`, codegen's type mapping yields the handle representation
  (`NetRef`), and no other registry name does. *(Scoped to registry names deliberately —
  arbitrary resolved .NET types are handle-represented by §8.3's rule and are `Unknown` to the
  registry by design, per §11.4.)*
- `ManagedOwned ∩ Rejected = ∅` — `Categorize` checks `ManagedOwned` first, so an overlap would
  resolve silently
- the ambient namespace set (§6.5) used by `NetTypeResolver` ≡ the one used by `CSharpBackend`
- the generated shim's `HandleTable` ≡ `BlnetShimSources`' copy ≡ the hand shim the frozen P0
  suite validates
- `BlnetStatus.cs` in the generated shim ≡ `BlnetContract.GenerateStatusEnumCs()`
- `AbiVersion` in the generated shim ≡ `BlnetContract.AbiVersion`

### 12.5 Unit and integration

Unit: resolver (overloads, generics, inheritance, accessibility), mangler, surface collector,
cache key (including that an irrelevant edit does **not** invalidate it), `AotDiagnosticMapper`
parsing real ILC output.

Integration:

- a BasicLang program and a hand-written `.cpp` both calling the same C# test library
- **a project with zero `.bas` files** whose only surface source is `<NetProxy>`, asserting
  `obj/gen` is populated, `blnet_startup.g.cpp` is compiled and linked, and the shim initializes.
  The mixed case above does not cover this — it is the path §9.5's four gates block
- a delegate round-trip
- a cold-cache then warm-cache build proving phase 5 is skipped

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

1. **Reflection-heavy libraries do not work** — structural to AOT (§1.2). Reported as BL6020
   **at whatever granularity ILC attributes the warning** (§11.3's three tiers), which may be
   assembly-level rather than a `.bas` line. A library whose incompatibility ILC cannot see fails
   at **runtime** as a managed exception, with no build diagnostic at all. Fixed by P2b.
2. **Boundary-spanning reference cycles leak** (§9.4).
3. **Events and interface implementation are absent** (D6) — a program using them fails to build.
4. **Arbitrary value types cross as boxed handles**, not by value (§8.3).
5. **`Return` inside `Try` bypasses `Finally`** on the C++ backend — pre-existing (§11.1).
6. **C++ consumers must declare their surface** via `<NetProxy>` (§7.2), and a declared type's
   proxy surface is a **subset** of its .NET surface — unmarshalable and AOT-hostile members are
   silently omitted with a note.
7. **`InvariantGlobalization=true`** is inherited from P0's recipe. Its real reach is wider than
   "formatting": `PredefinedCulturesOnly` is implied, string comparison is ordinal, casing is
   ASCII-only, and `TimeZoneInfo` collapses toward UTC. Anything in a referenced library that
   depends on culture behaves differently under the shim than under the C# backend.
8. **A wrong-typed handle has no distinct status** (§13).
9. **`<ProjectReference>` is not supported** (§5) — use `<Reference>` + `<HintPath>` to the
   sibling project's built assembly.
10. **`Char` narrows on the way in** — a .NET code unit above `U+00FF` does not fit BasicLang's
    1-byte native `Char` (§8.3).
11. **Native arrays crossing outbound are copied one-way** (§8.6) — mutations a .NET callee makes
    are not visible to the caller, where the C# backend would show them. Native `List`,
    `Dictionary` and `HashSet` cannot cross outbound at all (BL6019).

> Limitations 10 and 11 are *divergences from the C# backend*, not merely missing features. They
> are the reason §12.1 requires a parity program for each — a divergence that is pinned is a
> documented behavior; one that is not is a bug waiting to be found by a user.

---

## 15. Open items — resolve during planning

| # | Item |
|---|---|
| 15.1 | **AOT publish wall-clock is unmeasured anywhere in the repo.** Measure it in plan task 1; the number decides whether §10.2's cache is sufficient or a background pre-warm is needed sooner. |
| 15.2 | Roslyn version alignment: 4.9.2 matches the test project, but the compiler is shipped in `IDE/`. Confirm no conflict with `OmniSharp.Extensions.LanguageServer` 0.19.9 and measure the size delta to `BasicLang.exe`. |
| 15.3 | Whether tightening `IsNetType` breaks an existing test — recon could not determine whether any test pins the analyzer's permissiveness. |
| 15.4 | `blnet_initialize` is `const BlnetNativeVtable*` in the header but `void*` in the shim (`Exports.cs:36`). Deliberate for AOT blittability, or drift? Untested either way. |
| 15.5 | **Generic instantiation is a decision, not a confirmation.** `MakeGenericType` throws for non-pregenerated instantiations, so every instantiation the program uses must appear statically in the shim — but BasicLang generics emit **real C++ templates** and are never monomorphized in the front end, so instantiations do not exist at phase 3 to be collected. Choose one and record it in §14: (a) an instantiation-enumeration pass over generic bodies (its own plan task), or (b) a diagnostic rejecting .NET calls inside BasicLang generic bodies. |
| 15.6 | `[ThreadStatic]` last-error is never cleared on `BLNET_OK`, so a caller reading it after success gets a stale unrelated error. Pre-existing; decide whether P2a tightens it. |
| 15.7 | Whether a capability rejection also breaks IntelliSense emission (codegen still runs at `CppProjectBuilder.cs:293-296` with `forIntelliSense: true`) — inferred by recon, not verified. |
| 15.8 | Latent bug spotted in passing: `WorkspaceManager.cs:186` builds the package path without lowercasing the version while `PackageManager.GetPackagePath` lowercases both — LSP package-type loading may silently miss. Out of scope; chip it. |
| 15.9 | Whether `<ProjectReference>` (§5, §14.9) should be promoted into P2a after all, or stay a separate cross-project-compilation feature. It is the single largest deferred item and the workaround (`<Reference>` + `<HintPath>`) is workable but manual. |
| 15.10 | **Error-vs-warning policy per ILC diagnostic class.** Promoting every trim/AOT diagnostic to a hard BL6020 over-rejects — Microsoft documents that some runtime-library warnings are not actionable by end developers, and an assembly-level IL2104/IL3053 does not prove the program breaks. Currently ambiguous enough to be built two different ways. |
| 15.11 | Shim TFM vs user assemblies: a `net8.0` shim cannot reference a `net9.0+` user assembly, which collides with D1's "any assembly". Decide whether the shim TFM floats to the highest referenced TFM or stays pinned with a BL6021-class diagnostic. |
| 15.12 | Who calls `blnet_pump()` in a generated program, if anyone. §9.3 emits a pump call nowhere; with §8.4's `immediate = false` registration, any callback that *does* get queued would never run. Either emit a pump point or record it as a limitation. |

---

## 16. What P2b inherits

**Reused unchanged:** §5–§8.6 (reference closure, resolution, surface discovery, marshaling,
collections), §9.1–§9.2 and §9.4–§9.5 (artifacts, proxy API, lifetime, consumers), §11.1–§11.2
and §11.4 (exceptions, diagnostics), and §12.1 (the parity oracle).

**Replaced by P2b:**

- `NetShimPublisher` → `hostfxr` bootstrap; **no publish step in the build at all**
- `blnet_bind_all` → `load_assembly_and_get_function_pointer` per slot
- `AotDiagnosticMapper` → dropped; the ceiling it reports does not exist under hosting
- §9.3 (startup/handshake), §10.1 phase 5, and §10.2's cache — all transport-A-specific, since
  there is nothing to publish or cache

**Partly reused:** `NetShimGenerator`. P2b still generates **per-member managed entry points** —
`load_assembly_and_get_function_pointer` binds one function pointer per (assembly, type, method,
delegate type), so the per-slot table shape survives. What P2b drops is
`[UnmanagedCallersOnly]`, the blittable-argument constraint, and the AOT publish; the entry
points become ordinary statics reached through custom delegate types.

> This is a correction worth being explicit about: per-slot typed function pointers are *not*
> an AOT-only shape, but they are also not free under hosting — a "one generic reflection
> dispatcher, no codegen" P2b would **not** satisfy this seam. If P2b wants that simpler shape,
> it must add a single generic dispatch entry point (`invoke(method_id, uint64_t* args, …)`)
> alongside the typed slots, and the typed slots become an AOT fast path. That decision belongs
> to P2b's spec, not this one.

P2b additionally adds runtime discovery (`DOTNET_ROOT` handling), a "no .NET runtime installed"
diagnostic, and `.runtimeconfig.json` deployment via `<EnableDynamicLoading>`.

If P2b requires a change to anything in the "reused unchanged" list, the seam was in the wrong
place.
