# P2a-2 — The Flip and the Hard Lowerings — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents
> available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** Make .NET class access real on the native (BL+C++) path — typed exception matching,
the registry/checker flip, live surface collection, phase-5 shim publish, collections both
directions, delegates, and the parity + conformance gates — completing spec
`docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md` (P2a).

**Architecture:** P2a-1 built every transport-neutral component inert; this plan wires them in
the spec §17 order: §11.1 typed catch first (transport-independent, shrinks the flip), then
resolution/carriage/collection made real but still warning-only, then THE FLIP as one small
reviewable commit, then the lowerings (§8.5/§8.6/§8.4), then §12's gates. Every commit keeps the
full fast subset green; for programs that draw no .NET surface, USER-PROGRAM emission and
diagnostics stay identical throughout — the always-emitted runtime preamble grows by exactly two
enumerated splices (Task 1's `NetException`, Task 5's `NetRef`) and nothing else.

**Tech Stack:** C# (compiler), Roslyn 4.9.2 (`NetTypeResolver`/`NetOverloadProbe`), C++20
(generated native), .NET 8 Native AOT (`NetShimPublisher`), NUnit 4.

---

## Baseline and gates

- Start commit: `2752a96` (master). Fast subset baseline **3932 passed / 0 failed / 1 skipped**
  (`dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"`).
- Blnet gate: `--filter "FullyQualifiedName~Blnet"` = **391/0/0** (includes the 16 frozen P0
  conformance scenarios — they must stay green untouched through every task).
- C++ fixtures + parity: the P2a-1 closeout filter = 181/0/0.
- **Standing inertness rule until Task 7b:** any program whose collected surface is empty must
  produce identical diagnostics and identical USER-PROGRAM generated code vs `2752a96` — the
  runtime PREAMBLE is exempted for exactly two enumerated additions (Task 1's `NetException`
  splice, Task 5's `NetRef` splice), and any inertness diff must be shown to consist of exactly
  those splices and nothing else. `NetInertnessTests` enforces the diagnostics half; Task 5 (the
  flip) is where its severity assertions are deliberately churned — nowhere else.

## Environment laws (violations have caused real damage — repeat to every implementer)

- Windows PowerShell 5.1: no `&&`/`||`. Timeout 600000 on builds/tests. Never build the `.sln`.
- Read/Edit/Write/Grep tools for ALL file ops; NEVER `Get-Content`/`Set-Content` on repo files
  (BOM-less UTF-8 mojibake). Multi-line commit messages via scratchpad file + `git commit -F`.
- NUnit 4 constraint asserts. Never `new TypeRegistry()` parameterless in tests (clobbers the
  user's real LSP cache) — use the `internal TypeRegistry(string)` seam.
- `dotnet publish /p:PublishAot=true` child processes need the VS-Installer PATH hardening
  (`NetShimPublisher.HardenChildPath`) — this box sets `NoDefaultCurrentDirectoryInExePath=1`.
- C++20: `u8"..."` is `char8_t*` — use `\xNN` narrow escapes. Compile-and-run helpers:
  `CppCompile.FindRunCompiler()`, `CompileAndRun(src, compiler, extraFiles)`.
- MSIL/LLVM backends are OUT OF SCOPE. Do not test, fix, or report on them.

## Decisions made in this plan (spec left them to planning — do NOT reopen silently)

| # | Decision |
|---|---|
| D-P1 | **§14.15 (`ToString`/`GetHashCode` on non-overriding types): admit by explicit two-name allowlist.** `NetTypeResolver`'s candidate set admits `System.Object.ToString()` and `System.Object.GetHashCode()` (both nullary, both §8.3-marshalable) even when not overridden. NOT the general "any marshalable nullary Object member" rule — `GetType()` stays excluded (reflection root), `Equals(Object)` stays excluded (§8.3, `Object` is `Rejected`). **Implemented by Task 4 Step 2a** (recon: `NetTypeResolver.cs:354` stops the base-chain walk before `System.Object`, so this is NEW work in `CandidateMembers`, not existing behavior). The §12.1 parity row becomes a passing row in Task 13. Spec §14.15 is updated to "Resolved" in Task 15. |
| D-P2 | **§15.11 (shim TFM vs newer references): stays pinned `net8.0`.** `NetReferenceResolver` gains a BL6021 diagnostic when a resolved reference's `TargetFrameworkAttribute` names `.NETCoreApp,Version=v9.0` or higher (Task 4, one rule + one test). Floating the TFM is P2b-adjacent work. |
| D-P3 | **Diagnostic shape: .NET errors come from the ANALYZER with real positions; `CppCapabilityChecker` keeps its positionless BL6001 blob for residual non-.NET rejections.** Recon: the checker emits plain strings with no line info, joined into one BL6001 at `CppProjectBuilder.cs:566-570`. Threading positions through the checker is out of scope; the flip instead prevents resolved-.NET shapes from ever reaching it. |
| D-P4 | **§15.6 (`[ThreadStatic]` last-error not cleared on OK): unchanged.** `NetCheck` reads the channel only on non-OK status, so staleness is unobservable through generated proxies. Recorded, not fixed. |
| D-P5 | **Task count is 15, vs spec §17's "~10-14".** The vertical slice split (7a codegen / 7b phase 5) and the closeout task account for the overage; nothing was scope-added. |
| D-P6 | **`AddressOf` gets a native lowering in Task 11** (spec §8.4 names it; recon: `UnaryOpKind.AddressOf` has zero C++ lowering today — it is new work, not wiring). Lambdas already lower (inlined `[=]`), so the callback thunk work covers both from one mechanism. |
| D-P8 | **Task 11 supports SYNCHRONOUSLY-CONSUMED delegates only; a callee-RETAINED delegate is refused with a diagnostic.** (User decision, 2026-08-05, answering the recon's blocking Q1.) Registration lives in `NetArgEmission.Prologue` as an RAII guard, so release fires at the end of the call's brace scope — correct for `List.Sort(Comparison)`, wrong for an event handler or a stored comparer. Nothing in the P0 ABI distinguishes the two, and `blnet_callback_release` does not wait for in-flight invocations (it marks the entry dead and bumps the generation, `BlnetRuntimeSources.cs:249-257`), so a retained delegate would invoke a released entry against possibly-destroyed `[=]` captures. Supporting retention is new ABI surface and belongs to a later task. |
| D-P9 | **A parameter's delegate-ness and its invoke signature are carried as an ADDITIVE field on `NetParameterDescriptor`.** (User decision, 2026-08-05, answering the recon's blocking Q2.) Populated at the single point where a descriptor is built from a Roslyn symbol, `NetTypeResolver.Describe(IEnumerable<IParameterSymbol>)`. Neither consumer can re-derive it: `NetShimGenerator` imports no `Microsoft.CodeAnalysis` at all and its `Emit` signature takes no resolver, and `NetProxyEmitter.WireOf` sees only a type NAME. ⛔ Must be additive-only — the field must be excluded from `NetParameterDescriptor.ToString()` and from `NetNameMangler.CanonicalIdentity`, or every mangled export name and every shim cache key shifts. |
| D-P10 | **Task 11 supports delegate returns of ≤ 8 bytes only.** (User decision, 2026-08-05, answering the recon's blocking Q3.) `BlnetSlotDesc[]` describes ARGUMENTS only; the ABI has no return-value descriptor and the result travels through the bare uninterpreted `uint64_t* result`. The ">8-byte return = caller pre-loads `*result` with an out-buffer address" convention exists only inside P0 conformance scenario 9 and is NOT contract text. Formalizing it would be a slot-encoding change, which bumps `AbiVersion` under contract rule C7 and mechanically breaks the 16 frozen P0 scenarios (scenario 14 compares `BLNET_ABI_VERSION` on both sides). `AbiVersion` stays **1**. This covers the plan's own mandatory shape — `Comparison(Of T)` returns `Integer`. |
| D-P11 | **A BasicLang lambda is TARGET-TYPED against each candidate overload; it is never given an independent .NET type.** (User decision, 2026-08-05.) A lambda types structurally as `Func`/`Action`, while real .NET delegate parameters are NAMED types (`MatchEvaluator`, `Comparison<T>`, `ThreadStart`) — nominal matching admits none of the APIs anyone actually calls, including `List.Sort`, this plan's own mandatory test shape. Implemented as a reserved argument spelling `lambda(N)` alongside `NullArgumentSpelling`: screened out of the well-formed-type-name validation, never declared as a probe local, and emitted at the call site as `(p0, …) => throw null!` so the real C# compiler performs the conversion. ⛔ The body is `throw null!` and **not** `default` — a throw expression converts to any delegate return type **including void**, so one spelling serves `Action`-shaped and `Func`-shaped targets alike; `default` silently fails to convert to every void-returning delegate and answers "no such overload" for `ThreadStart`. Both facts are mutation-verified by `NetDelegateTests`. |
| D-P7 | **Post-flip, `ManagedOwned` types are legal in every DECLARATION position by composition:** `List(Of Regex)` → `BasicLang::List<NetRef>`, `Func(Of Regex)` → `std::function<NetRef()>`, fields/locals/returns/generic args likewise — `BareCollectionType` and the `Func`/`Action` mapping already route element/argument types through `MapType`, so Task 5's `NetRef` arm composes with zero extra mapping code. To make declaration-only programs COMPILE AND RUN without a shim, **`BasicLang::NetRef` moves into the always-emitted native runtime** (null-slot-safe: addref/release on handle 0 are no-ops — P0's zero-handle rule — and a declaration-only program can never obtain a non-zero handle). `blnet_runtime.hpp` keeps using the same type (single definition; `BlnetRuntimeSources` text changes, which changes `TemplateIdentity` and invalidates the shim cache — expected and harmless). Crossing the boundary stays governed by §8.5/§8.6: a native collection of handle elements outbound is BL6019 regardless of this decision. |

## Recon anchors (verified 2026-08-02 — re-verify before editing, lines drift)

- `CppProjectBuilder.cs`: phase enum `:37-46`; checkpoints `:262/:401/:475/:531` (+`:150/:162` in
  `Build`); **the surface stub `:476`** (`surfaceOverride ?? NetSurface.Empty`); reference
  resolution `:299-332`; `NetResolverFactory` set `:410`; NetDiagnostics merge `:420-446`; BL6025
  gate `:514-523`; `GenerateSplit` `:538-578`; `NetProxyEmitter.Emit` call `:589-591`;
  `hasGeneratedArtifacts` gate `:607`; obj/gen write `:608-635`; TU union `:643-651`; include
  path `:732-736`; capability catch → BL6001 `:566-570`; `ShimAssemblyName` `:966`.
  `CppEmitOutcome` `:63-90` (no surface / shim-path fields yet).
- `SemanticAnalyzer.cs`: net region `:2141-2398`; `ConfigureNetResolution` `:2152`; `NetWarning`
  `:2187-2192`; `ProbeNetTypeReference` `:2313` (callers `:1913`, `:1938`);
  `ProbeNetMemberAccess` `:2347` (single caller `:5914`); BL6023 `:2329`, BL6016 `:2335`,
  BL6017 `:2385`; evidence bar `IsExplicitlyNetQualified` `:2304-2307`; claim-predicate consults
  `:2317/:2356/:2357`; `LookupNetTypeMember` fallbacks `:2090-2102`; catch-clause visit
  `:5141-5157` (no probe).
- `CppCapabilityChecker.cs`: `CheckType` `:571-632`; Rejected arm `:614-618`; unknown-class arm
  `:627-631`; `IRNewObject` closure `:322-336`; `Object` hard check `:606-610`;
  `CppExceptionTypes.Names` `:15-21` (12 names); `IRTryCatch` recursion without
  `CheckType(cc.ExceptionType)` `:228-236`.
- `BoundaryTypeRegistry.cs` (repo-root `BasicLang\`): categories `:4-16` (5 incl. `Unknown`);
  Rejected = 6 names `:56-59` (`Object` + the five to move); `ManagedOwned` empty `:72`;
  `Categorize` order `:78-81`; `Normalize` `:96-101`.
- `IRNodes.cs`: `IRCall.ResolvedNetTarget` `:612` (never written in production) /
  `NetCategory` `:625` (`= Unknown` is load-bearing, `NativeOwned == 0`);
  `IRInstanceMethodCall` `:1548-1572` and `IRBaseMethodCall` `:1577` carry NEITHER field;
  `IRTryCatch` `:957`, `IRCatchClause` `:1008` (typed catch IS carried in IR);
  `IRDelegate` `:1395`.
- `IRBuilder.cs`: fused static-call arm + `NetCategory` write `:3339-3364`; instance/else arm
  `:3365-3369`; catch var never enters `LocalVariables` `:2698-2708`; typed-catch IR build
  `:2657-2669`; `AddressOf` → `UnaryOpKind.AddressOf` `:3633`.
- `CppCodeGenerator.cs`: `Visit(IRTryCatch)` `:3471-3529`; `MapCatchType` `:3698-3705` (two
  C++ types only — typed catch semantically collapsed); `IRThrow` lowering `:3707-3724`;
  finally suffixes `_fex`/`_fnorm`; `MapType` collection branch `:500-504`;
  `BareCollectionType` `:577-587`; `IsCollectionType` `:595-602`; Func/Action → `std::function`
  `:519-531`; lambda inlining `:650-657`; `IsNativeOwnedBclType` `:612-614`.
  **`ex.Message` has NO lowering (`what()` appears nowhere in the generator).**
- Net components: `NetProxyEmitter` (5 artifacts; empty surface writes NOTHING;
  `NotSupportedException` on ByRef handle/String — currently unreachable and unmapped);
  `NetShimGenerator.Emit(surface, safeProjectName, referenceAssemblyPaths, valueTypeReceiverNames)`
  (zero production callers; **reverse dependency**: `:171` calls
  `CppProjectBuilder.ShimAssemblyName`); `NetShimPublisher.Publish` (zero production callers);
  `NetShimCache` (only `CacheRoot` has a production caller — `BuildService.cs:335`);
  `AotDiagnosticMapper.Map(ilcOutput, provenance, referencedAssemblyNames)` (zero production
  callers; `NetProvenanceMap` never populated); `NetTypeResolver.ResolveOverload` +
  `CandidateMembers` (zero production callers).
- Collections runtime: `CppCollectionsRuntime.cs:34` const string spliced into
  `BasicLangRuntime.g.h` (split, unconditional) / inline combined (conditional). `List<T>`
  surface: `Add/Count()/operator[]/Contains/IndexOf/Remove/RemoveAt/Insert/Clear/begin/end`.
- Test churn candidates — **Task 5 item 7 is the authoritative, complete list**; headline
  entries: `NetInertnessTests.cs:381-402/:422-451/:489`; `BlnetContractTests.cs:253-255` +
  `:190-197/:218-219/:228-231` (Rejected pins incl. the `:231` normalization row);
  the SIX C++ fixture pins on the moved names (`CppCollectionTests.cs:194/1050/1064/1081/1096`,
  `CppBackendTests.cs:285`); `CppBackendTests.cs:433-448` (`Cpp_TryCatchTyped_MapsExceptionType`,
  Task 1). Keep-stable: `NetClaimPredicateTests.PredicateIsStableAcrossTheP2a2RegistryFlip`
  (`:56-70`) must NOT change.
- Parity harness: `BclBackendParityTests.cs` — `record ParityProgram(Name, Source)` `:143`,
  13 programs `:786-801`, driver `:811`. P1's Task-13 constraint list applies verbatim.

---

## File structure (created / significantly modified)

| File | Role |
|---|---|
| Create `BasicLang/Compiler/CodeGen/CPlusPlus/CppNetExceptionRuntime.cs` | `BasicLang::NetException` + chain matching as a const-string runtime splice (Task 1) |
| Create `BasicLang/Net/NetSurfaceCollector.cs` | IR walk + `<NetProxy>` → `NetSurface` (Task 3, extended Task 9) |
| Create `BasicLang/Net/NetAstAnnotations.cs` | side-channel: analyzer-resolved member descriptors keyed by AST node (Task 2) |
| Create `BasicLang/Compiler/CodeGen/CPlusPlus/CppNetMarshal.cs` | §6.4 conversion pairs, native side, const-string splice (Task 6) |
| Create `VisualGameStudio.Tests/Blnet/NetConformanceTests.cs` + `TestAssets/BlnetGenLib/` | §12.3 generated-shim suite (Task 12) |
| Modify `SemanticAnalyzer.cs`, `IRBuilder.cs`, `IRNodes.cs`, `IROptimizer.cs`, `Compiler.cs` | resolution → carriage (Tasks 2/4/5) |
| Modify `CppCodeGenerator.cs` (+`.Split.cs`), `TypeMapper.cs` | typed catch, NetRef lowering, collections, delegates (Tasks 1/5/7a/8/9/10/11) |
| Modify `CppCapabilityChecker.cs`, `BoundaryTypeRegistry.cs` | the flip (Task 5) |
| Modify `CppProjectBuilder.cs`, `NetShimGenerator.cs`, `NetShimCache.cs`, `AotDiagnosticMapper.cs`, `NetProxyEmitter.cs` | phase 5 live (Task 7b) |
| Modify `ProjectFile.cs`, `NetReferenceResolver.cs` | `<NetProxy>` parsing, TFM rule, ProjectReference promotion (Tasks 3/4/5) |
| Modify `Program.cs`, `BuildService.cs`, `MultiTargetCompiler.cs` | §6.3 C# warning row (Task 4) |
| Modify `BclBackendParityTests.cs`, `NetInertnessTests.cs`, `BlnetContractTests.cs`, `CppBackendTests.cs`, `CppCollectionTests.cs` | gates + enumerated churn (Tasks 1/5/13) |

---

### Task 1: §11.1 typed catch — `NetException`, the per-`Try` ladder, and `ex.Message`

**Files:**
- Create: `BasicLang/Compiler/CodeGen/CPlusPlus/CppNetExceptionRuntime.cs`
- Modify: `BasicLang/CppCodeGenerator.cs` (`Visit(IRTryCatch)` `:3471-3529`, `MapCatchType`
  region `:3698-3705`, member-access lowering for `Message`), `BasicLang/CppCodeGenerator.Split.cs`
  (runtime splice, near `:396-397`), `BasicLang/CppCodeGenerator.cs:318-319` (combined splice)
- Test: `VisualGameStudio.Tests/Compiler/CppTypedCatchTests.cs` (new)

**Why now:** transport-independent; §17 orders it first, while every surface is empty and the
leading handler is provably dead code for existing programs. It also closes two recon holes:
`ex.Message` has no lowering at all, and typed catch is semantically collapsed (all non-`Exception`
names → `catch (const std::runtime_error&)`, second clause dead).

**Design (spec §11.1, all normative):**
- `BasicLang::NetException : std::runtime_error`, carrying the `;`-separated fully-qualified
  inheritance chain (most-derived first) + message. Member `bool Matches(const char* fqName)`
  does `;`-delimited **element equality** (never substring). Declared in the **always-emitted**
  runtime — spliced unconditionally in BOTH emission modes (the four existing typed-catch test
  files — `CppBclEndToEndTests`, `CppBackendTests`, `CppCollectionTests`, `BclBackendParityTests`
  — must compile with a `Try` and no .NET surface).
- Per-`Try`, not per-clause: when a `Try` has ≥1 .NET-typed clause (= any clause whose type name
  is in `CppExceptionTypes.Names` — which is essentially every typed clause today — or resolves
  as a .NET exception type), emit ONE leading `catch (const BasicLang::NetException& __nex)`
  containing an if/else-if ladder over ALL clauses in source order (each arm in its own braces
  with its own catch variable initialized from `__nex`), ending `throw;`. The existing
  `MapCatchType`-derived per-clause handlers follow unchanged.
- Chain matching maps a BL clause type name to its fully-qualified .NET name for `Matches`
  (`Exception` → `System.Exception`, `ArgumentNullException` → `System.ArgumentNullException`,
  …). `Catch ex As Exception` matches ANY chain (every chain ends `System.Exception`).
- **Ladder body labels:** emit under `_regionLabelSuffix = "_nex"` (set before, reset after the
  whole ladder), exactly as the finally path does with `_fex`/`_fnorm` (`:3400-3402`-adjacent in
  the current tree). One suffix for the entire ladder.
- **Ordering:** the `NetException` handler precedes both `MapCatchType` handlers and the
  `catch (...)` finally handler — `NetException` IS a `std::runtime_error`, so any later position
  gets swallowed.
- BL-thrown `Throw New ArgumentException(...)` stays `std::runtime_error` (`:3707-3724`
  untouched) and falls through the ladder to the per-clause handler — the dual-shape behavior.
- **`Message` lowering (new):** a member access `<catchVar>.Message` where `<catchVar>` is a
  catch variable lowers to `BasicLang::String(<var>.what())` in per-clause handlers and to the
  same via `what()` on `NetException` in ladder arms (NetException's message IS its `what()`).
  Scope strictly to catch variables (track the active catch-variable names during
  `Visit(IRTryCatch)`); do not build a general exception-object model.

**Steps:**

- [ ] **Step 1: failing emit-level tests.** In the new fixture, compile (emit-only) programs and
  assert on generated C++: (a) a `Try` with `Catch a As ArgumentNullException` +
  `Catch b As Exception` emits ONE `catch (const BasicLang::NetException&` before any
  `catch (const std::` handler, with an if/else-if ladder containing both clauses and a trailing
  `throw;`; (b) a catch body with `If ... Then` control flow emits ladder labels suffixed `_nex`
  and per-clause labels unsuffixed (grep the emitted text for duplicate label definitions —
  assert none); (c) `ex.Message` emits `.what()`. Run: expect all red (`MapCatchType` shape today).
- [ ] **Step 2: runtime + ladder implementation.** Add `CppNetExceptionRuntime.Source` (class +
  `Matches`); splice unconditionally both modes; implement the ladder in `Visit(IRTryCatch)`;
  implement `Message` on catch vars. Re-run step-1 tests green.
- [ ] **Step 3: compile-and-run fixtures (the real proof).** Using `CompileAndRun` + a
  `#CppInclude` foreign helper header (pattern: `CppPassthroughTests.cs`) that does
  `throw BasicLang::NetException("System.ArgumentNullException;System.ArgumentException;System.SystemException;System.Exception", "boom")`:
  (a) subclass match — `Catch e As ArgumentException` catches it, prints the message;
  (b) exact match preferred in source order; (c) no clause matches → `throw;` propagates out;
  (d) `Catch e As Exception` catches any chain; (e) the dual-shape program: BL
  `Throw New ArgumentException("x")` caught by its own typed clause in the same method that has
  the ladder. Each must run and print expected output.
- [ ] **Step 4: existing-shape regression.** Run
  `--filter "FullyQualifiedName~CppBackendTests|FullyQualifiedName~CppBclEndToEndTests|FullyQualifiedName~CppCollectionTests|FullyQualifiedName~BclBackendParityTests"`
  — all green. `Cpp_TryCatchTyped_MapsExceptionType` (`CppBackendTests.cs:433-448`) may need its
  assertion extended for the new leading handler; if so, the update asserts the FULL new shape
  (leading NetException handler + unchanged `std::exception` per-clause), not a weakened contains.
- [ ] **Step 5: fast subset** (expect baseline + new tests, zero failures) **and commit**
  (`feat(p2a2): typed catch — NetException chain ladder + ex.Message lowering`).

### Task 2: carriage completion + `ConfigureTypeRegistry` into `CompileUnit`

**Files:**
- Create: `BasicLang/Net/NetAstAnnotations.cs`
- Modify: `BasicLang/IRNodes.cs` (`IRInstanceMethodCall` `:1548`, `IRBaseMethodCall` `:1577`),
  `BasicLang/IRBuilder.cs` (`:3339-3369`), `BasicLang/IROptimizer.cs` (clone paths `:1409`,
  `:2290` region), `BasicLang/Compiler.cs` (`CompileUnit`, `:584` region),
  `BasicLang/SemanticAnalyzer.cs` (annotation writes in the probe region)
- Test: extend `VisualGameStudio.Tests/Blnet/NetIrCarriageTests.cs`; new
  `VisualGameStudio.Tests/Blnet/TypeRegistryFallbackPinningTests.cs`

**Why:** P2a-1 recorded both gaps explicitly: instance calls (`IRInstanceMethodCall` AND
`IRBaseMethodCall`) carry no `.NET` fields, and `ResolvedNetTarget` has no production writer.
The flip needs the analyzer's resolution to reach codegen; the analyzer and IRBuilder walk the
AST separately, so the hand-off is an annotation side table.

**Design:**
- `NetAstAnnotations`: a per-compilation `Dictionary<ExpressionNode, NetMemberDescriptor>`
  (reference-keyed) owned by `SemanticAnalyzer`, exposed
  `internal IReadOnlyDictionary<...> NetResolvedMembers`, handed to `IRBuilder` via
  `CompilerOptions`/`CompileUnit` plumbing (same route `NetResolverFactory` took).
- `SemanticAnalyzer.ProbeNetMemberAccess` (`:2347-2388`) already computes `fullName` and the
  full `GetMembers(fullName)` list and THROWS THE DESCRIPTOR AWAY (recon §6): on a name match it
  now records the descriptor in the annotation table (still warning-only — recording is not
  reporting). Overload-precise selection replaces this in Task 4; a name-unique member is exact
  already.
- `IRBuilder`: in the fused static arm (`:3339-3364`) and the instance/else arm (`:3365-3369`),
  read the annotation for the source AST node and set `ResolvedNetTarget` + `NetCategory` on the
  produced node. Add both fields to `IRInstanceMethodCall` and `IRBaseMethodCall` with the SAME
  `= BoundaryTypeCategory.Unknown` initializer (`NativeOwned == 0` — the P2a-1 trap).
- `IROptimizer`: every clone/rewrite path for the two node types copies both fields; extend the
  existing round-trip test pattern (`NetIrCarriageTests`).
- `ConfigureTypeRegistry` into `CompileUnit` (**the P2a-1 deferral with its named risk**): wiring
  it un-deadens `SemanticAnalyzer.cs:2075-2088`, which shadows the String/common fallbacks at
  `:2090-2102`. FIRST write `TypeRegistryFallbackPinningTests` over `LookupNetTypeMember` for the
  non-P1 fallback set (String members, the common fallbacks — enumerate from `:2090-2102`), run
  them green against TODAY's behavior, THEN wire, and the pins must still pass. Any pin that
  flips is a real behavior change — stop and re-derive, do not update the pin.

**Steps:**

- [ ] **Step 1:** write the fallback pinning tests against current behavior; green.
- [ ] **Step 2:** failing carriage tests — build IR for `Dim r As New Regex("a") : r.IsMatch("x")`
  shapes via the test compiler with a resolver factory; assert the instance-call node carries a
  non-null `ResolvedNetTarget` with `DeclaringTypeFullName = "System.Text.RegularExpressions.Regex"`
  (annotation recorded because `Regex` resolves; note it is still Rejected/unclaimed — recording
  is severity-independent). Red.
- [ ] **Step 3:** implement annotations + fields + IRBuilder reads + optimizer clones. Green.
- [ ] **Step 4:** optimizer round-trip test for both new node types (mutation: comment one clone
  copy → test must go red; restore; record).
- [ ] **Step 5:** wire `ConfigureTypeRegistry` into `CompileUnit`; pinning tests still green;
  fast subset; commit (`feat(p2a2): instance-call .NET carriage + ConfigureTypeRegistry`).

### Task 3: `NetSurfaceCollector` + `<NetProxy>` — the surface goes live (still unclaimable)

**Files:**
- Create: `BasicLang/Net/NetSurfaceCollector.cs`
- Modify: `BasicLang/ProjectSystem/ProjectFile.cs` (parse `<NetProxy Include>` — element does
  not exist today), `BasicLang/ProjectSystem/CppProjectBuilder.cs:476` (the one-line stub),
  `BasicLang/Net/NetReferenceResolver.cs` (only if closure access needs widening)
- Test: `VisualGameStudio.Tests/Blnet/NetSurfaceCollectorTests.cs` (new)

**Design:**
- **BL-inferred (§7.1):** walk the optimized IR modules; every node carrying a non-null
  `ResolvedNetTarget` whose `NetCategory ∉ {NativeOwned, Bridged}` contributes its descriptor.
  Used-only, deduped by mangled name.
- **Declared (§7.2):** for each `<NetProxy Include="Full.Type.Name">`: unknown type → **BL6022**;
  otherwise collect the full public surface with the CORRECTED rules: methods/properties/fields
  from the whole base chain minus `System.Object` (plus the D-P1 two-name allowlist),
  **constructors from the queried type ONLY**, signature-complete identity
  `(kind, name, isStatic, arity, [refkind+type]…)` — `NetTypeResolver.CandidateMembers` is the
  seam and already encodes ctor type-locality and signature-complete identity (P2a-1 Task 4/5);
  the collector consumes it, never re-derives. ⚠ The D-P1 two-name `System.Object` allowlist is
  NOT in `CandidateMembers` today (`NetTypeResolver.cs:354` stops before `System.Object`) — it
  lands in Task 4 Step 2a; this task's `<NetProxy>` surfaces simply inherit it once it exists.
- **Omission (§7.2):** a member is skipped with **BL6026 warning** (named type+member+offending
  type) when its signature contains a type outside §8.3's rows, or it/its accessors/declaring
  type carry `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` — read via Roslyn at phase 3,
  NEVER from ILC output (the phase-circularity trap, spec §7.2). The omission set is final
  before any proxy header is emitted.
- Wire: `CppProjectBuilder.cs:476` becomes
  `surfaceOverride ?? NetSurfaceCollector.Collect(compilation, project, netResolverFactory, diagnostics)`.
  Phase-3 checkpoint already exists (`:475`).
- **Inertness holds:** claimed names never carry annotations (claim predicate runs before the
  probe records), `Regex`/`Uri`/… are still `Rejected` so nothing survives to the surface, and
  every existing program still collects `NetSurface.Empty`. `NetInertnessTests` is the gate.

**Steps:**

- [ ] **Step 1:** failing collector unit tests (hand-built IR + descriptors): used-only dedup;
  claimed-category exclusion; `<NetProxy>` full surface incl. ctor type-locality (the
  `FileNotFoundException` 5-vs-15 measurement is the oracle — assert 5); BL6022; BL6026 for a
  `[RequiresDynamicCode]` member and for a `Span<char>` signature.
- [ ] **Step 2:** `ProjectFile` parsing + round-trip test (`<NetProxy>` survives load/save —
  follow `:211-223`'s `<Reference>` pattern and the write pattern at `:348-351`).
- [ ] **Step 3:** implement; unit tests green.
- [ ] **Step 4:** wire `:476`; run `NetInertnessTests` (must be UNTOUCHED and green — the
  empty-surface proof) + `NetBuildPipelineTests`; BL6025 now reachable: add one test — a Library
  `.blproj` with a `<NetProxy>` fails BL6025.
- [ ] **Step 5:** fast subset; commit (`feat(p2a2): NetSurfaceCollector + <NetProxy>, surfaces live`).

### Task 4: strict resolution — overloads, BL6018/BL6023/BL6024/BL6019(arg), C# warning row

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs` (probe region `:2141-2398` + call-expression visitor),
  `BasicLang/Net/NetReferenceResolver.cs` (D-P2 TFM rule), `BasicLang/Program.cs` (`:506`,
  `:1022`), `VisualGameStudio.ProjectSystem/Services/BuildService.cs` (`:624`),
  `BasicLang/MultiTargetCompiler.cs` (`:237`)
- First mechanical step (Task-3 quality-review Important #1): extract the now-TRIPLICATED
  closure-rebuild + diagnostic-mapping block in `CppProjectBuilder.EmitCore` (~`:341-368`,
  `:419-445`, `:499-531`) into one helper (e.g. `MergeNetDiagnostics(...)`) that preserves the
  warning/`Fail` severity split internally — do it BEFORE adding this task's fourth consumer.
- Test: `VisualGameStudio.Tests/Blnet/NetStrictResolutionTests.cs` (new)

**Design:**
- **Argument-side typing → `ResolveOverload`.** At the call-expression sites that today reach
  `ProbeNetMemberAccess` (single caller `:5914`) add an invocation-aware probe: gather the
  already-computed static types of arguments (admissible set = §8.3 rows + §6.4 pairs projected
  through `TypeMapper`; `Nothing` = null literal; a user-defined BL class → BL6017/BL6019 per
  §6.5), map to metadata names, call `NetOverloadProbe.ResolveOverload` (zero production callers
  today — this is its wiring). Outcomes: `NoMatch` → BL6017 (the "no matching overload" half);
  `Ambiguous` → **BL6018** (new); winner → record THE WINNING descriptor in the Task-2 annotation
  table (replacing name-only recording).
- **BL6024:** a .NET-annotated call inside a BL generic body (the analyzer knows the enclosing
  function's generic-ness) → BL6024, native path only.
- **Evidence bar replacement:** `IsExplicitlyNetQualified` (`:2304-2307`) is replaced by real
  resolution against `NetAmbientNamespaces` + `IRModule.NetUsings` per §6.5 — bare `Queue(Of T)`
  now resolves (churns `BareUnclaimedGenericsAreBelowTheEvidenceBar` — Task 5 owns the severity
  churn; THIS task keeps everything `IsWarning: true` so the only visible change is MORE warnings
  on programs naming unresolvable/ambiguous .NET types).
- **Ladder-trigger completion (Task 1 spec-review finding):** Task 1 implemented the typed-catch
  ladder trigger for the 12 `CppExceptionTypes` names only; spec §11.1's trigger ALSO covers a
  clause type that "resolves as a .NET exception type". Once resolution is live (this task /
  Task 5), extend the trigger + `TryGetNetFullName` so resolved exception types outside the
  12-name set (e.g. `FileNotFoundException`) get their ladder arm with the resolver-supplied FQ
  name — otherwise they silently bind to a later `Exception` clause. Add a test with exactly
  that shape. While touching the ladder, also convert the THREE `_regionLabelSuffix`
  literal-assign/reset sites (`_fex`/`_fnorm`/`_nex`) to save/restore (`var saved = …; …;
  _regionLabelSuffix = saved;`) — retires the whole nested-copy label-collision class incl. the
  pre-existing finally-inside-finally variant (Task 1 quality-review item).
- **Severity stays warning-only in this task on BOTH backends** — §6.3's native-error promotion
  is the flip (Task 5), keeping this commit's churn reviewable.
- **C# warning row (§6.3):** set `CompilerOptions.NetResolverFactory` on the C#-backend paths
  (`Program.cs:506/:1022`, `BuildService.cs:624`, `MultiTargetCompiler.cs:237` — the enumerated
  P2a-1 deferral). The claim predicate + ambient sets make existing-program warnings vanishingly
  rare, but run the FULL template + parity sweeps and treat ANY new warning on an existing
  green program as a defect in the wiring, not acceptable churn.
- **D-P2:** `NetReferenceResolver` reads `TargetFrameworkAttribute` of each resolved reference;
  ≥ v9.0 → BL6021 naming the TFM and the pinned net8.0 shim.

**Steps:**

- [ ] **Step 1:** failing tests: `Math.Max(1, 2)`-shaped resolvable overloads on a REAL
  reference closure (framework paths from `NetReferenceResolver`) select the right descriptor;
  ambiguous → BL6018; no-match → BL6017 with argument list in the message; generic-body → BL6024;
  net9-ref → BL6021 (build a tiny net9-attributed assembly via Roslyn emit in the fixture).
- [ ] **Step 2:** implement; green. Mutation: revert the annotation write to name-only → the
  winner-descriptor assertion goes red; restore; record.
- [ ] **Step 2a (D-P1):** the two-name `System.Object` allowlist in
  `NetTypeResolver.CandidateMembers`: when walking a type whose chain excludes `System.Object`
  (`NetTypeResolver.cs:354`), additionally admit `ToString()` (nullary → `String`) and
  `GetHashCode()` (nullary → `Int32`) as callable members of every reference type that does not
  override them. ⚠ ALSO lift the probe's `ObjectMemberNames` early-out
  (`SemanticAnalyzer.cs:2368/:2393-2396`) for exactly these two names — it currently suppresses
  both the warning AND the Task-2 annotation recording, so without this the allowlisted members
  are never annotated, never collected into the surface, and never get a proxy slot (Task 2
  quality-review finding: the recording gaps also include NativeBclSurface-owned members and
  unresolvable types — the collector must not assume "resolved ⇒ annotated"). Unit tests: `System.IO.Stream` (non-overriding — the spec's clean case) resolves
  both; `GetType()` still `NoMatch`; `Equals(Object)` still `NoMatch`; `StringBuilder.ToString()`
  (overriding) resolves to the OVERRIDE, not the allowlist entry (assert declaring type).
  Mutation: comment the allowlist → the Stream tests go red; restore; record.
- [ ] **Step 3:** wire the three C#-path factories; run the FULL `NetInertnessTests`,
  `TemplateBuildSweepTests` (Integration), and the 13-program parity battery — zero new findings
  anywhere (this is the §6.3 "valid programs behave identically" proof).
- [ ] **Step 4:** fast subset; commit (`feat(p2a2): strict overload resolution, warning-only`).

### Task 5: THE FLIP — registry move, checker un-rejection, native errors, enumerated churn

**Files:**
- Modify: `BasicLang/BoundaryTypeRegistry.cs` (`:56-72`), `BasicLang/CppCapabilityChecker.cs`
  (`:322-336`, `:614-618`, `:627-631`), `BasicLang/SemanticAnalyzer.cs` (severity switch),
  `BasicLang/Net/NetReferenceResolver.cs` (`<ProjectReference>` warning → error),
  `BasicLang/CppCodeGenerator.cs` (`MapType` ManagedOwned → `NetRef` arm, before `:500-504`),
  `BasicLang/TypeMapper.cs` (if the native type spelling routes through it),
  `BasicLang/Compiler/CodeGen/CPlusPlus/BlnetRuntimeSources.cs` + the unconditional runtime
  splice (`CppNetExceptionRuntime.cs` or a sibling) — the D-P7 `NetRef` extraction (item 6)
- Test: enumerated churn (below) + `VisualGameStudio.Tests/Blnet/NetFlipTests.cs` (new)

**Design (kept deliberately small — everything that could precede it already landed):**
1. Registry: `Regex, Uri, Stream, FileInfo, DirectoryInfo` move `Rejected → ManagedOwned`
   (`:56-59` → `:72`). `Object` stays `Rejected`. §12.4's `ManagedOwned ∩ Rejected = ∅`
   invariant test added.
2. Checker: the three cited sites accept `ManagedOwned` (and any type whose IR node carries a
   resolved annotation — D-P3: resolved-.NET shapes never reach the checker's string blob):
   `:614-618` adds a `ManagedOwned` early return; `:627-631`'s unknown-class arm excludes names
   the surface resolved; `:322-336`'s `IRNewObject` arm accepts `ManagedOwned` ctors.
   `Object` hard check `:606-610` unchanged.
2a. Task-4 carry-forward: the `_netNamespaces.Count > 0` unresolved-base gate is NOT probed
   (classes aren't pre-registered; a forward-referenced user base under a `Using` would draw a
   false BL6016) — verify during this task's severity promotion that native `Inherits` of a
   .NET class stays checker-rejected and the false-BL6016 shape has a test proving it does NOT
   fire as an error.
3. Severity: on the native backend, BL6016/BL6017/BL6018/BL6019/BL6023/BL6024 become
   **errors** (`IsWarning: false`, routed through the same channel — `CppProjectBuilder.cs:420-446`
   already fails the build for non-warning closure diagnostics; verify and extend). C# backend
   stays warnings (§6.3).
4. `<ProjectReference>` on the native path: warning → **error** BL6021 (P2a-1's
   `:483` deferral; the "IDE creates such projects" concern is now resolved BY reporting, since
   a real error names the workaround). ⚠ Task-4 review flag: ALSO promote the D-P2 TFM-rule
   BL6021 (net9+ reference) on the native path — post-flip a net9 reference breaks the shim
   publish (NU1201) whenever a surface is non-empty; promote here, or surface-gate it at 7b,
   but decide explicitly (it is enumerated nowhere else).
   ⚠ Task-4 review flag #2: BL6024 covers member-access annotations only — a ManagedOwned
   CONSTRUCTOR in a BL generic body (`New FileStream(...)`) bypasses it once this task's
   `IRNewObject` acceptance lands, handing Task 7a a call inside a C++ template. Extend BL6024
   to ctor invocations in generic bodies in THIS task (the checker rejection that covers it
   pre-flip disappears with the flip).
5. `MapType`: `ManagedOwned` names (and Task-2-annotated resolved types) map to `NetRef` BEFORE
   the collection branch `:500-504`. Declaration-level only — call lowering is Task 7a. §12.4's
   "every `ManagedOwned` name → `NetRef`, no other registry name does" invariant test added.
6. **`NetRef` moves into the always-emitted runtime (D-P7).** Extract the `NetRef` RAII type
   from `BlnetRuntimeSources`' `blnet_runtime.hpp` text into the unconditional runtime splice
   (beside Task 1's `NetException`), null-slot-safe (addref/release no-op on handle 0 and on a
   null vtable — a declaration-only program can never hold a non-zero handle).
   `blnet_runtime.hpp` keeps a single definition (include-guard or emitted-before ordering — do
   not duplicate the type). The frozen P0 conformance suite must stay green (it compiles against
   both headers); `TemplateIdentity` changes → cache invalidation is expected.
7. **Enumerated test churn — update in THIS commit, each with a one-line comment naming the flip:**
   - `BlnetContractTests.ManagedOwned_StillEmpty` (`:253-255` → asserts the five);
     Rejected pins `:190-197/:218-219/:228-231` (→ ManagedOwned expectations — note `:231`'s
     `System.Text.RegularExpressions.Regex → Rejected` normalization row churns too);
   - `NetInertnessTests.TheGateIsArmed…` (`:381-402`) and `NetFindingsAreNeverErrors`
     (`:422-451`, incl. the `:446-450` sanity assert) — both flip to asserting native-ERROR
     behavior; `BareUnclaimedGenericsAreBelowTheEvidenceBar` (`:489`) — re-pin to resolved
     behavior; `CppProjectBuilderCleanTests.cs:115-120` if the BL6001 blob no longer carries
     .NET names.
   - **The six C++ fixture pins on the moved names — all `Assert.Throws<CppCapabilityException>`
     today, all re-pin to D-P7 ACCEPTANCE (program compiles; declaration maps to the composed
     `NetRef` shape, asserted on emitted text; compile-and-run where the fixture already runs):**
     `CppCollectionTests.Cpp_ListOfUnmappedType_StillRejected` (`:194`, `List(Of Regex)` →
     `BasicLang::List<NetRef>`); `Cpp_ModuleGlobalUnmappedType_StillRejected` (`:1050`,
     `Dim g As Regex` → `NetRef g`); `Cpp_InterfaceMethodUnmappedReturnType_StillRejected`
     (`:1064` → `NetRef` return); `Cpp_RejectedLocal_StillRejected` (`:1081`, `Stream` local);
     `Cpp_InterfaceMethodRejectedReturnType_StillRejected` (`:1096`, `Stream` return);
     `CppBackendTests.Cpp_InterfaceReturn_FuncOfUnmappedArg_ThrowsCapabilityError` (`:285`,
     `Func(Of Regex)` → `std::function<NetRef()>`). Rename each to its post-flip meaning
     (`…_StillRejected` → `…_MapsToNetRef` etc.).
   **`NetClaimPredicateTests.PredicateIsStableAcrossTheP2a2RegistryFlip` must pass UNCHANGED —
   it was written as this commit's invariant.**

**Steps:**

- [ ] **Step 1:** write `NetFlipTests` red first: `Dim r As New Regex("ab")` + `r.IsMatch("x")`
  on the native path passes the analyzer + checker (no BL6016/17, no "no C++ mapping" blob) and
  declares `NetRef r`; an unresolvable `System.Nope` is a native ERROR failing the build; the
  same program on the C# backend still compiles with a warning.
- [ ] **Step 2:** apply changes 1-6 (incl. the D-P7 `NetRef` relocation; run the 16 frozen P0
  conformance scenarios immediately after — they compile against the touched headers).
  Step-1 tests green.
- [ ] **Step 3:** the enumerated churn (item 7) — every listed fixture updated incl. the six
  C++ pins re-pinned to D-P7 acceptance, full Blnet filter green, the predicate-stability test
  untouched-and-green.
- [ ] **Step 4:** emission-identity check on claimed-name programs: the 13 parity programs + the
  game/console templates emit C++ whose diff vs the pre-flip commit consists of EXACTLY the
  D-P7 `NetRef` runtime-preamble splice and nothing else — no user-program TU line changes, no
  diagnostic changes (script the diff and assert the residue is empty after subtracting the
  known splice; this is the "flip changes nothing that was native" proof).
- [ ] **Step 5:** fast subset + Blnet + C++ fixtures; commit (`feat(p2a2): the flip`).

### Task 6: §6.4 conversion pairs — native ↔ managed value bridging

**Files:**
- Create: `BasicLang/Compiler/CodeGen/CPlusPlus/CppNetMarshal.cs` (const-string splice:
  `to_net_datetime`/`from_net_datetime` etc. for DateTime, TimeSpan, Decimal, Guid,
  DateTimeOffset, StringBuilder-by-value→String)
- Modify: `BasicLang/Compiler/CodeGen/Net/BlnetShimSources.cs` or `NetShimGenerator.cs`
  (managed-side conversion helpers in `Exports.g.cs` scaffolding)
- Test: `VisualGameStudio.Tests/Blnet/NetConversionPairTests.cs` (new)

**Design:** wire forms are the P1 layouts verbatim: DateTime = uint64 dateData
(62-bit ticks | 2-bit Kind), TimeSpan = int64 ticks, Decimal = 96-bit `{lo,mid,hi,flags}`,
Guid = 16 bytes, DateTimeOffset = ticks + offset-minutes, StringBuilder crosses BY VALUE as
String (§6.4). Managed side: `DateTime.FromBinary`-compatible reconstruction, `decimal` from
`int[4]` bits, `Guid(byte[])`. **The failure mode is a silently wrong value** — the oracle is
bit-pattern pinning on BOTH sides against hard-coded known vectors (e.g. the .NET-computed
`dateData` for `2026-08-02T00:00:00Z`), never round-trip-only tests (a symmetric bug passes a
round trip).

**Steps:**

- [ ] **Step 1:** hard-coded vector tests red on both sides (native via `CompileAndRun` printing
  hex; managed via direct unit tests on the generator-emitted helper source compiled in-test).
- [ ] **Step 2:** implement both sides; green. Mutation: flip the Kind-bit shift → red; restore.
- [ ] **Step 3:** fast subset; commit (`feat(p2a2): §6.4 conversion pairs`).

### Task 7a: call lowering — resolved calls become proxy invocations

> ⚠ Task-4 quality-review carry-forward: BEFORE this task's marshaling work, lift the
> §8.3+§6.4 argument-admissibility projection (`NetArgumentSpellings`/`TryMapNetArgumentType`,
> private in `SemanticAnalyzer`) into `BasicLang/Net/` beside `NetClaimPredicate` — or add an
> invariant test tying the §6.4 rows across the three encodings (analyzer table, `NetProxyEmitter`
> wire types, `NetShimGenerator` C# types) — so this task consumes rather than re-derives it.
> Also consider storing the canonical member name in `_netResolvedReceivers` (kills the second
> `GetMembers` walk and carries the member-kind guard naturally).

**Files:**
- Modify: `BasicLang/CppCodeGenerator.cs` (+`.Split.cs`): new lowering arm for
  `IRCall`/`IRInstanceMethodCall`/`IRNewObject` nodes carrying `ResolvedNetTarget`
- Modify: `BasicLang/Compiler/CodeGen/Net/NetProxyEmitter.cs` only if the proxy signature shapes
  need adjustment (they were proven on hand-fed surfaces — prefer adapting the caller)
- Test: `VisualGameStudio.Tests/Blnet/NetCallLoweringTests.cs` (new; emit-level — no publish)

**Design:**
- A resolved static call lowers to the typed inline proxy (`blnet_proxies.g.hpp` name =
  `NetNameMangler.Mangle`-derived proxy), receiver-less; instance call passes the `NetRef`
  receiver; `New` on a `ManagedOwned`/resolved type lowers to the ctor proxy returning `NetRef`.
- Argument marshaling at the call site per §8.3: primitives by value; `Boolean` → int32;
  `String` → UTF-8 `const char*` (borrow — the proxy copies); P1 `NativeOwned` values through
  Task 6's converters; `Nothing` → 0-handle (never `Table`-reaching, §8.2); returned strings
  arrive via the transfer buffer and become `BasicLang::String` with `blnet_free`.
- Properties: ⛔ **CORRECTED after implementation (2026-08-03)** — Roslyn does NOT model
  accessors as separate methods; `GetMembers` returns ONE member per property. 7a therefore uses
  the property descriptor itself AS the getter slot and synthesizes ONLY the setter
  (`NetAccessorSynthesis`, single synthesis point) — one export per read-only property instead
  of two. Task 9's indexer work must reuse that synthesis point, not re-derive the wrong
  assumption. Scope also grew beyond the plan's three node types: `IRFieldAccess`/`IRFieldStore`
  lower too, so §8.3 work now has FIVE arms to extend.
- **This task is emit-level only** — generated C++ is asserted textually and compiled against a
  STUB `blnet_bindings` (a test-emitted fake `g_net` whose slots are C++ lambdas recording
  calls), the same trick the P0 harness uses. No AOT publish in this task's tests.
- ⛔ **Name-only descriptors must NOT be trusted (Task-4 concern #1).** Task 4 left these call
  shapes name-only recorded with no overload probe: `Nothing` arguments (probe grammar has no
  null spelling), Object-degraded args, lambda/delegate args, **.NET-enum-valued args**
  (`File.Open(p, FileMode.Open)` — the member access types as Object today), method-level
  generic args. Pinned by `NothingArgument_LeavesTheCallNameOnlyRecorded_WithNoFinding`. THIS
  task must decide per shape: extend `NetOverloadProbe` with null/enum spellings (spec §6.5
  says "`Nothing` participates as a null literal" — the probe gap contradicts it; enum member
  accesses should resolve+type via the resolver), or refuse to lower a name-only descriptor
  with a BL6017-class error at emission. Silently lowering a name-matched-first-overload
  descriptor is a miscompile, not a fallback. Delegate-arg shapes may defer to Task 11.
- `NetProxyEmitter`'s `NotSupportedException` (ByRef Handle/String wire forms) becomes reachable:
  map it to BL6019 at the `CppProjectBuilder.cs:589` call site (try/catch → diagnostic, not a
  crash). ref/out themselves land in Task 8.

**Steps:**

- [ ] **Step 1:** red emit tests: static `Regex.IsMatch("a","b")` → proxy call with two UTF-8
  args; instance `r.IsMatch("x")` → receiver handle + arg; `New Regex("p")` → ctor proxy into a
  `NetRef` local; property get/set; `Nothing` argument → `NetRef()` (0-handle).
  Task-5 review carry-forwards to fold into this fixture: a direct BL6023-native-error pin
  (currently covered only transitively via the shared seam), and coverage for the ctor probe's
  resolved-ARBITRARY-name arm (`New FileStream(...)` in a generic body → BL6024 — the
  ManagedOwned arm is mutation-killed, this arm has zero coverage).
- [ ] **Step 2:** implement the lowering arm. Green.
- [ ] **Step 3:** stub-runtime run tests: `CompileAndRun` with the fake `g_net` — the recorded
  call sequence and arguments match; a non-OK status from the stub surfaces as a catchable
  `NetException` (ties Task 1 end-to-end at the native level).
- [ ] **Step 4:** BL6019 mapping test for the emitter throw. Fast subset; commit
  (`feat(p2a2): resolved-call lowering to g_net proxies`).

### Task 7b: phase 5 live — generate, cache, publish, map, deploy

**Files:**
- Modify: `BasicLang/ProjectSystem/CppProjectBuilder.cs` (phase-5 block between the obj/gen
  write `:608-635` and toolchain resolve `:653`; `CppEmitOutcome` gains `Surface` and
  `ShimDllPath`; `Build` deploys), `BasicLang/Compiler/CodeGen/Net/NetShimGenerator.cs`
  (provenance map population from Task-2 annotations; **move `ShimAssemblyName` here from
  `CppProjectBuilder.cs:966`** to break the reverse dependency — recon flag #4),
  `NetShimCache.cs` (no API change expected), `AotDiagnosticMapper.cs` (severity per §15.10)
- Test: `VisualGameStudio.Tests/Blnet/NetShimPipelineTests.cs` (new, `[Category("Integration")]`)

**Design:**
- Phase-5 block (build path only, `forIntelliSense` skips it — §10.1 promises IntelliSense
  never publishes): `Checkpoint(GenerateAndPublishShim)` → compute `NetShimCache.KeyFor` →
  `TryGetHit` → on miss: `NetShimGenerator.WriteTo(obj/gen/shim/…)` → `NetShimPublisher.Publish`
  (child PATH hardened) → `AotDiagnosticMapper.Map(result.Output, provenance, refNames)` →
  BL6020s merged per §15.10 severity (IL3xxx error / IL2xxx warning / aggregates warning) →
  `Commit` on success. Hit: reuse `PublishDirectory`'s DLL. Empty surface: the whole block is a
  no-op (phase skipped — §12.5's `Console.WriteLine`-only guard).
- Deploy: `Build` copies the shim DLL next to the exe (same `File.Copy` pattern as the engine
  DLL deploy). `CppEmitOutcome.ShimDllPath` carries it from `EmitCore` to `Build`.
- Task-3 review carry-forward: duplicate `<NetProxy>` declarations survive verbatim in
  `DeclaredTypeNames` and would churn the cache key (`NetShimCache.cs:283-284`) for an identical
  member set — dedup on the key side (or normalize DeclaredTypeNames) when wiring the cache.
- CancellationToken honored around the publish (`Publish` already has the 10-min guard).
- Provenance: `NetShimGenerator` gets the (mangled name → `NetWrapperOrigin`) pairs — BL call
  sites from the annotation table's node positions, `<NetProxy>` members as
  `NetProxyDeclaration` origins.
- ⚠ **Task-7a handoff (verify early in this task):** `NetCheckTyped` expects the last-error TYPE
  field to carry the full `;`-separated inheritance chain — the stub planted it, but the
  GENERATED shim's `Fail(ex)` must actually produce it or typed catch silently stops matching
  on real calls. Also (7a concern 4): a write to a READ-ONLY .NET property synthesizes a `set_X`
  slot that fails loudly in csc here — catch it at the analyzer/collector instead (BL6017-class,
  positioned) rather than as a shim-compile failure.

**Steps:**

- [x] **Step 1 (the milestone):** ✅ **GREEN 2026-08-03.** `NetShimPipelineTests.Milestone_…`:
  `Dim Rx As New Regex("^a+$")` + `Rx.IsMatch(...)` builds end-to-end and RUNS.
  **Wall time: 23.4 s cold** through `CppProjectBuilder.Build`, **24.4 s** through the CLI
  (`BasicLang.exe build`), both including the C++ compile+link.
  ⛔ **The exe prints `True` through an `If`, not through `Console.WriteLine(bool)`** — printing a
  Boolean shows `1`/`0` on the C++ backend (item 8 of
  `specs/2026-07-07-cpp-backend-preexisting-gaps.md`), reproduced on a program with no .NET in it,
  so it is a Boolean-FORMATTING gap in the C++ BCL layer and not the boundary's. The `If` form is
  also the stronger oracle (a wrong answer changes the BRANCH), and the raw form is pinned on the
  negative case (`IsMatch("bbb")` → `0`) so the day the gap is fixed this test says so.
- [x] **Step 2:** cold-then-warm — `SecondIdenticalBuild_HitsTheCacheAndSkipsPhaseFive`.
  Manifest + message channel + elapsed: **23.8 s cold → 10.0 s warm**, and the warm build's
  program still runs (the reused DLL must still be deployed).
- [x] **Step 3:** `AotHostileMemberReachedFromBasicLang_IsABl6020ErrorAtTheBasLine` — a Roslyn-emitted
  probe assembly with a `[RequiresDynamicCode]` member, reached by a BasicLang CALL (a `<NetProxy>`
  cannot carry one: §7.2's filter BL6026-omits AOT-hostile members, and only a call site has a
  `.bas` line to attribute to). ILC's IL3050 lands as a BL6020 **error** at `Program.bas(5)`.
  Aggregate tier-3 + the §15.10 split asserted through the phase-5 MERGE in
  `NetShimPhaseTests.PhaseFiveMerge_…`, on captured ILC text.
  ⛔ The probe MUST be compiled against the net8.0 **reference** pack: built against the shared
  framework's implementation assemblies it carries a direct `System.Private.CoreLib` reference and
  every use of its types in the shim is CS0012.
- [x] **Step 4:** `EmptySurface_SkipsPhaseFiveEntirely`, `ForIntelliSense_NeverPublishes_EvenWithARealSurface`,
  `ZeroBasProjectWithANetProxy_PublishesAndLinksTheStartupTu` (which RUNS the exe — reaching the
  user's own `main()` is what proves the startup TU was linked and the §9.3 handshake passed).
- [x] **Step 5:** fast subset **4062/0/1** (baseline 4052/0/1 → +10, zero regressions); Blnet
  filter incl. the 16 frozen P0 scenarios; commit
  (`feat(p2a2): phase 5 — generate, cache, publish, map, deploy`).

**⚠ Carried out of Task 7b — read before Task 8:**
- **`ShimAssemblyName` now lives on `NetShimGenerator`**, not `CppProjectBuilder` (the dependency
  is one-way `ProjectSystem` → `CodeGen.Net` again).
- **`EmitCore` gained a `publishShim` test seam.** Any fixture that drives the BUILD path with a
  hand-built `surfaceOverride` — or with a resolvable-but-fake toolchain and a real `<NetProxy>` —
  must pass `publishShim: false` or it spawns a real ~27 s `dotnet publish` in the FAST subset.
  `NetBuildPipelineTests` and `NetSurfaceCollectorTests` are already switched.
- **`valueTypeReceiverNames` is fed for real** (from `NetTypeResolver.TypeSymbol(...).IsValueType`)
  — Task 8's row for it is already satisfied; do not re-do it.
- **`NetMemberDescriptor.IsSettable`** exists and is populated by the resolver; it is NOT part of
  the CLR signature and must stay out of the mangler and the duplicate-collapse key.
- **The analyzer resolves against IMPLEMENTATION assemblies while the shim compiles against
  REFERENCE assemblies.** A member present only in the implementation set would be admitted at
  phase 3 and rejected by `csc` at phase 5. Pre-existing (`NetReferenceResolver`'s framework set),
  not observed on any real surface yet, but it is the shape of a late failure.
- **`Directory.Build.props` above `obj/gen/shim` can still rewrite §8.1's properties** — the hazard
  `NetShimGenerator`'s header documents. Not addressed; the fix, if it ever bites, is publishing
  from a staged copy outside the user tree.

### Task 8: ref/out, boxed value types, `Unsafe.Unbox`, `Char`

**Files:**
- Modify: `BasicLang/CppCodeGenerator.cs` (pointer-slot call shapes), `BasicLang/IRBuilder.cs`
  (`IRCall.ByRefArguments` population for resolved .NET targets — today user-functions only),
  `NetShimGenerator.cs` (`valueTypeReceiverNames` fed for real; `Unsafe.Unbox<T>` receiver
  bodies — the CS0445 rule), `NetProxyEmitter.cs` (ByRef wire forms — replaces the
  `NotSupportedException`), `SemanticAnalyzer.cs` (BL6019 for statically-known `Char` > U+00FF
  and for `ref struct` in a used signature)
- Test: extend `NetCallLoweringTests` + `NetConformanceTests` rows (Task 12 completes them)

**Design:** §8.3 rows verbatim: `ref`/`out` = pointer slots (null out writes 0; 0 read from ref
decodes to null); other non-ref value types = boxed handles with `Unsafe.Unbox<T>` receivers
(mutable-struct correctness, not an optimization); `Char` = uint16 wire, outbound zero-extends,
inbound narrows with the §14.10 divergence documented at the lowering site; `ref struct` = BL6019.

⛔ **Task-7a inherited scope (concerns 1-2) — THIS TASK OWNS ALL OF IT:**
- **The remaining four §6.4 wire rows.** 7a shipped DateTime + TimeSpan single-slot scalar rows
  in both emitters; **Decimal, Guid, DateTimeOffset and StringBuilder currently BL6019 at
  resolved call sites** (multi-slot / by-value-String shapes). ⚠ **Task 13's parity program #1
  (all six pairs round-tripped through a .NET call — the plan's highest-value oracle) CANNOT
  PASS until these land.**
- **Enum arguments.** 7a extended the probe so enum-valued args RESOLVE exactly, but enum
  PARAMETERS refuse BL6019 — neither emitter can recover an enum's underlying type from a name.
  Needs underlying-type carriage in the descriptor + both emitters. `File.Open(p, FileMode.Open)`
  is the canonical shape and is currently a precise refusal, not a miscompile.

> ### ⛔ CARRIED FORWARD out of Task 8 (2026-08-03) — the two items above did NOT land
>
> Everything else in Task 8 shipped. These two are deferred with the design worked out and
> **measured**, so the next session starts from here rather than re-deriving it. Both remain
> PRECISE REFUSALS today (positioned BL6019), never miscompiles, and both are still Task 13's
> blockers.
>
> **A. The four §6.4 rows — "multi-slot" is THREE different complications, not one.**
> The seam is ready (`NetArgEmission {Prologue, Expressions, Epilogue}` exists and
> `Expressions` is already a LIST for exactly this), but the emitters plan one C slot per
> parameter and one `result` out-pointer, and the four rows break that in three distinct ways:
> - **arity > 1, scalar slots** — Decimal (`uint32_t` ×4, the `GetBits` quad) and
>   DateTimeOffset (`int64_t utcTicks`, `int16_t offsetMinutes` — the DECLARED scalar pair;
>   `NetDateTimeOffsetWire` is `{int64,int16}` = sizeof 16 with 6 bytes of padding and must
>   NEVER cross by value). Parameter side is easy with the emission shape: prologue
>   `auto t = to_net_decimal(x);`, expressions `t.lo, t.mid, t.hi, t.flags`. The RESULT side is
>   the real work — one `result` out-pointer becomes several. **Cheapest shape found: give the
>   proxy OUT-REFERENCES for the result slots and return `void`** (`inline void slot(args…,
>   uint32_t& r_lo, …)`), then the call site does prologue-declare → call → `dest =
>   from_net_decimal(r_lo, …)`. This avoids inventing POD wire structs in the proxies header,
>   which would need layout agreement with `blnet_marshal.hpp` — a header the proxies header
>   deliberately cannot include (the P1 include-order contract).
> - **direction-dependent C type, arity 1** — Guid. `const uint8_t*` in / `uint8_t*` out
>   (a pointer to 16 bytes, exactly what `to_net_guid(v, out[16])` / `from_net_guid(in[16])`
>   already take). This is the SAME shape String already has, not a new one. Do **not** pack it
>   into two `uint64_t`: that makes the wire host-endian-dependent, and the managed side would
>   have to reverse it identically.
> - **one-way** — StringBuilder. Crosses as the String wire, to-net only; a StringBuilder
>   RESULT must keep refusing (`NetWireRow.NativeFromNet` is null for it ON PURPOSE, and
>   `NetConversionPairTests` pins the absence on both sides). Call site wants a prologue temp
>   (`std::string t = to_net_stringbuilder(x);` then `t.c_str()`) rather than `.c_str()` on a
>   temporary.
>
> Managed side per row (all already exist in `BlnetShimSources.MarshalCs`): `DecimalFromWire`
> takes **`int`**, not `uint` — cast at the call site; `DecimalToWire` returns `int[]` and
> `DateTimeOffsetToWire` has two `out` parameters, so the shim's RESULT direction needs
> STATEMENTS, not just an expression (`EmitWrapper` is expression-shaped today);
> `GuidFromWire`/`GuidToWire` are `byte[]`-based (`new ReadOnlySpan<byte>(a0, 16).ToArray()`
> inline avoids touching the byte-pinned `Prologue` const).
>
> `NetShimGeneratorTests.ExportSignaturesMatchTheProxyTableSlotSignatures` compares POSITIONALLY
> by (type, name), so multi-slot works there for free **provided both emitters emit the same
> slot names in the same order**. ⚠ **Correction (Task-8 review): only ONE `CsTypeFor` row is
> actually missing.** `uint32_t`→`uint` (`NetShimGeneratorTests.cs:527`), `int16_t`→`short`
> (`:524`) and `uint8_t*`→`byte*` (via the trailing-`*` fallback at `:534`) all EXIST — do not
> go looking for them. Only **`const uint8_t*`** needs adding: `const ` has no general strip,
> which is why `const char*` is a hand-written special case at `:532`. And add members for all
> four rows to `NetProxyEmitterTests.WireShapeSurface` (+ `ShapeCount`) or the oracle stays blind
> to them, which is the exact gap Step 0's I3 closed for DateTime/TimeSpan — and which Task 8's
> own review found still open for NINE more rows (see Task 8c's step list).
>
> **B. Enum arguments — the plan's framing above is INCOMPLETE, measured.** Underlying-type
> carriage in the descriptor + both emitters is necessary but **not sufficient**: there is no
> enum VALUE to pass. Probed on this tree —
> ```
> Dim m = FileMode.Open      →  BL6001: 'Object' (local 'm' in 'Main') — 'Object' has no C++ mapping
> ```
> `TryTypeNetEnumArgument` produces a SPELLING for overload resolution and records no
> annotation, so `FileMode.Open` types as `Object` and lowers to nothing at all. Un-refusing
> enum PARAMETERS without fixing this turns a precise refusal into a broken program. The
> missing piece is enum-member-constant lowering: expose the member's constant from the
> resolver (`GetMembers` gives fields but not values), type the member access as the underlying
> BasicLang integral, and stamp the constant so `IRBuilder` emits an `IRConstant`. Only then do
> the descriptor carriage and the two emitter arms have anything to carry.
> `NetTypeResolver.EnumUnderlyingTypeFullName` and the `NetTypeEnvironment` capability slot for
> it landed in Task 8's Step 0 (M2) and are ready for it.

**Steps:**

- [ ] **Step 0 (MECHANICAL, do these FIRST — Task-7a quality review; all behavior-preserving,
  all cheaper now than after four §6.4 rows and this task's arms land):**
  - **I4 — emission shape:** `BuildNetProxyCall` returns a bare expression string, so no
    marshaling decision can emit statements. THIS task needs `int32_t tmp = n; proxy(&tmp);
    n = tmp;`; Task 10 needs copy-out/release; Task 11 needs thunk register/unregister. Change
    `MarshalNetArgument` to return `NetArgEmission { Prologue, Expression, Epilogue }`,
    accumulate in `BuildNetProxyCall`, emit prologue → call → epilogue in `EmitNetResult`.
    All current emissions have empty prologue/epilogue ⇒ behavior-preserving, covered by the
    existing 21 tests.
  - **I2 — one row table:** the §8.3 rows now live in FIVE encodings (two maps in
    `NetMarshalTable`, `NetProxyEmitter.WireOf`, `NetShimGenerator.WireOf`, and the two
    hard-coded switches in `CppCodeGenerator.NetCalls.cs`), only 3↔4 tied by a test. Introduce
    one `NetWireRow` per row (`BlSpelling, CsSpelling, CWire, CsWire, ToNetExpr, FromNetExpr,
    IsMultiSlot`) in `NetMarshalTable` and project all consumers from it. Minimum viable: move
    the two call-site switches onto the table — a row present in the emitters but missing at
    the call site is a SILENT wire mismatch, not a compile error.
  - **I5 — extract the stub harness:** `CompileWithSurface`/`StubSlot`/`StubTranslationUnit`/
    `RunWithStub`/`Winner` are private to `NetProxyStubRunTests` and `Winner` is ALREADY
    duplicated into `NetCallLoweringTests`. Move to a shared `NetStubHarness.cs` (with
    `SharedResolver`/`RequireCompiler` — each fixture currently pays its own ~170-assembly
    resolver build). Tasks 9/10/11 all need run-level proofs.
  - **M1 — carriage interface:** six node types × three .NET fields is past the payoff point.
    `internal interface INetCarrying { ResolvedNetTarget; NetCategory; ResolvedNetTargetIsExact }`
    (auto-properties satisfy it as-is) collapses the collector's six `case` arms into one and
    gives the optimizer comments a single anchor.
  - **M2 — capability env:** `NetMarshalTable.TryMapArgumentType` is a 5-param method re-passing
    two delegates through two recursion sites; this task adds a THIRD judgment (enum underlying
    type). Wrap them in a `readonly struct NetTypeEnvironment` constructed once in the analyzer.
    Also make `ArgumentSpellings` private (its ordering rules — the `Object` exclusion and
    user-defined-before-metadata — are load-bearing and invisible from the raw map).
  - **I3 — extend the drift oracle:** add DateTime (and TimeSpan) members to
    `NetProxyEmitterTests.SixShapeSurface()` and bump its count guard — the cross-emitter
    signature oracle is currently BLIND to the two rows 7a added, i.e. blind to exactly the
    class of row this task adds four more of.

  **Task-7b quality-review additions (same Step 0 — all cheaper now than after Task 9;
  the first two are HARD PREREQUISITES for Task 9's indexer work):**
  - ⛔ **7b-I5 — the synthesized-setter predicate is TRIPLICATED.** `Exact && (Property || Field)
    && Parameters.Count == 0` is spelled in `IRBuilder.cs:2912-2916` (produces it),
    `NetAstAnnotations.CallSiteOrigins` (attributes it), and `SemanticAnalyzer`'s
    `RefuseWriteToUnsettableNetMember` (refuses it). The `Parameters.Count == 0` clause IS the
    indexer refusal. **Miss the analyzer copy in Task 9 and a read-only indexer write re-opens
    the CS0200-after-27s failure 7b just closed.** Extract one shared predicate into
    `NetAccessorSynthesis` — its own docstring already says the two-parameter descriptor belongs
    "HERE rather than at a call site".
  - ⛔ **7b-I8 — `ValueTypeReceiverNames` has NO test**, and its failure modes are the most
    expensive this pipeline has: CS0445 (whole shim fails to compile) and §8.5's
    mutate-the-temporary infinite `MoveNext`. It is a by-name lookup, which makes
    `List<T>.Enumerator`/`Dictionary<K,V>.Enumerator` — both structs, both arriving in Tasks
    9/10 — the highest-risk spellings on the highest-risk path. Add a fast test asserting the
    derived set over a hand-built surface with a known framework struct.
  - **7b-I7 — `valueTypeReceiverNames` determines shim CONTENT but is not in the cache key**,
    and degrades silently (`TypeSymbol(name)?.IsValueType == true` answers "reference type" for
    any name the resolver can't resolve). Reference-closure failures are protected by accident
    (an unreadable assembly also fails `TryReadMvid` → null key); **framework paths are
    deliberately excluded from the key, so a framework struct receiver that fails to resolve
    once yields a wrong-but-COMPILING shim that is then `Commit`ed and hit forever** — violating
    the stated invariant "the cache is allowed to be absent, never allowed to be wrong". Treat
    "resolver could not answer for a receiver in the surface" as key-poisoning (null key ⇒
    publish unconditionally), the established shape here.
  - **7b-I6 — `obj/gen/shim` has no stale-file discipline** and `dotnet publish` globs `**/*.cs`
    rooted there. Latent only while the emitted file set is fixed — Tasks 9/10/11 make it
    surface-dependent, so a removed member's orphaned `.g.cs` gets compiled into the shim.
    Prune unrecognized `*.cs`/`*.csproj` in `NetShimGenerator.WriteTo` (keep `obj/`/`bin/`).
  - **7b-I9 — the Integration fixture's publish cost is strictly linear** (fresh `_dir` +
    one `Build()` per test; 5 publishes today, +1 per scenario for Tasks 9-12). A shared
    directory does NOT help (the key hashes the mangled member set, so a different program is a
    guaranteed miss) — the only lever is FEWER, RICHER programs, and the fixture offers no
    affordance for it. Add a `BuildOnce(source)` memo (or `OneTimeSetUp`-scoped project) before
    Task 9 lands; mirror `NetShimPhaseTests.SharedResolver`'s `Lazy` pattern.
- [ ] **Step 1:** red tests: `Integer.TryParse("42", n)` shape (out slot) through the stub
  runtime; a mutable-struct property set through a boxed receiver (the CS0445 shape) — assert
  the generated C# uses `Unsafe.Unbox`; `Char` narrowing emit; `Span<char>` overload use → BL6019.
- [ ] **Step 2:** implement; green; mutation: swap `Unsafe.Unbox` back to a cast → the generated
  shim must FAIL to compile in the test (CS0445 pinned as the oracle); restore.
- [ ] **Step 2b (Task-3 review carry-forward):** add the §8.3 drift test — every signature type
  the collector ADMITS (`FirstUnmarshalable` returns null) must get a wire form from
  `NetShimGenerator.WireForm`/`NetProxyEmitter.WireOf`; the three §8.3 encodings are currently
  linked only by doc comments. Also add the cross-reference comments in both emitter tables
  naming `FirstUnmarshalable`. Task-6 review addendum: the drift test needs SIX §6.4 rows
  tying wire type ↔ `to_net_*`/`from_net_*` signature ↔ shim parameter type (the DTO ABI form
  is the SCALAR pair, declared in the marshal header). Also adopt the `(void)r.ClockDateTime();`
  range-check idiom in native `from_net_datetimeoffset` next time the marshal header is touched.
- [ ] **Step 3:** fast subset; commit (`feat(p2a2): ref/out slots, boxed receivers, Char`).

**Task 8 outcome (2026-08-03).** Step 0 landed all eleven items, in three commits
(`fd5599b` I4+I2, `74a157d` M1+M2+7b-I5+I3, `c6866ed` I5+7b-I6/I7/I8/I9 plus the three Task-7b
final-review trivia). ⚠ **NINE of the eleven were behavior-preserving; two were deliberate
BEHAVIOR CHANGES** — sanctioned by this plan, but they are not "no observable difference" and a
bisect should know it: **7b-I6** now DELETES stale top-level `*.cs`/`*.csproj` from
`obj/gen/shim` that previously survived a rebuild, and **7b-I7** adds a new never-cache path
(an unresolvable receiver poisons §10.2's key, so such a project re-publishes every build).

The feature landed as `7ae95a5` (ref/out pointer slots + the Char ≥0x80 run proof) and `7b197e6`
(ref-struct BL6019 + the CS0445 mutation oracle + the `ClockDateTime` range check).

⚠ **Step 2b's drift test did NOT land, despite `7b197e6`'s subject line saying "§8.3's drift
test".** What shipped is `EverySection64Row_MatchesTheNativeConverterItNames` — a real oracle,
but a NARROWER one: it ties `NetMarshalTable.WireRows`' six §6.4 rows to the converters
`blnet_marshal.hpp` defines, plus `OnlySingleSlotByValueRows_CarryACallSiteWireType` and the
cross-reference comments in both emitter tables. **Step 2b asked for something else**: "every
signature type the collector ADMITS (`FirstUnmarshalable` returns null) gets a wire form from
BOTH emitters." `FirstUnmarshalable` appears in no test at all. That half moved to **Task 8c**.

The four §6.4 rows and enum arguments are CARRIED FORWARD — see the blockquote above and
Task 8c for the measured designs.

⛔ **Found by Step 0's 7b-I8 and owed to TASK 9** — ✅ **FIXED by Task 8b below; kept for the
record.** `NetTypeResolver.TypeName` builds a nested
type's spelling from `QualifiedName`, which walks containing types by NAME and **drops their
generic arity** — `List<T>.Enumerator` spells `System.Collections.Generic.List.Enumerator`,
while the metadata name its own `Lookup` needs is
`System.Collections.Generic.List`1+Enumerator`. Verified both ways
(`ValueTypeReceiverNames_CannotSeeANestedGenericStruct_Task9Prerequisite`), and the collector
admits `GetEnumerator()` quite happily because the nested type has arity 0 of its OWN so the
open-type-parameter check never fires. Task 9 hits it twice: the value-type receiver set answers
"reference type" for every enumerator struct, so the shim casts instead of `Unsafe.Unbox` and
§8.5's mutate-the-temporary `MoveNext`-forever loop appears with **no diagnostic**; and
`NetShimGenerator.Qualified` would emit `global::System.Collections.Generic.List.Enumerator`,
which is not valid C#. Fix either by teaching `TypeName`/`CandidateMetadataNames` the arity form
(⚠ `TypeName` feeds `NetNameMangler`, so this MOVES export names — §12.4 identity) or by deriving
the value-type set from the `ITypeSymbol` the COLLECTOR holds, which `NetShimGenerator.Emit`'s own
parameter docs already call the only thing that can really know. Until then 7b-I7's key poisoning
bounds the damage: the answer reports INCOMPLETE, so the wrong shim is never cached.

### Task 8b: `TypeName` generic-arity fix — **HARD BLOCKER for Task 9** ✅ DONE

Found by Task 8's Step-0 `ValueTypeReceiverNames` test (7b-I8), which existed for exactly this.

> **DONE.** `QualifiedName` now spells EVERY nesting level with its own type-argument list
> (`System.Collections.Generic.List<T>.Enumerator`) — the same per-level shape `ReceiverSyntax`
> already built for the overload probe — and `CandidateMetadataNames` derives the metadata form
> (`List`1+Enumerator`) back out of it, so **a name the resolver PRODUCES is a name it RESOLVES**.
> The two spellings are deliberately different strings and both are pinned: the C# form is what
> `csc` accepts behind `NetShimGenerator`'s `global::` and what §7.3 must hash (a metadata name
> cannot express a CONSTRUCTED generic, so it would merge `List<int>.Enumerator` with
> `List<string>.Enumerator` into one export slot); the metadata form is what
> `GetTypeByMetadataName` wants. A constructed generic therefore resolves to its DEFINITION —
> correct for every question asked through a name (existence, accessibility, kind, members,
> value-type-ness), and the reason the mangler keeps the spelling rather than re-deriving it.
>
> **Export-name analysis (measured at `9bb301c`, not argued).** A mangled name can only move when
> the DECLARING type or a PARAMETER type is nested inside a generic — the return type is in
> neither `CanonicalIdentity` nor the stem. Over the ManagedOwned five (292 members) and the
> 43-type mangler corpus (3,079 members): **0 export names move**; 7 RETURN spellings change
> (`List<T>.GetEnumerator`, `Dictionary`'s `GetEnumerator`/`Keys`/`Values`, `HashSet`/`Queue`/
> `Stack` `GetEnumerator`). The `<NetProxy>`/wire-shape fixtures are hand-built over
> `MyLib.Widget`-style names with no generics at all. So §12.4 identity is UNCHANGED for every
> surface that exists today — no churn commit was needed. Had one moved, `NetShimCache`'s key
> hashes the mangled member set (component 2), so a moved name is a MISS, never a stale hit.
>
> **The publish cliff is gone too.** Same measurement: of 49 distinct declaring-type spellings the
> corpus produces, **8 were `NotFound`** — every generic one (`List<T>`, `Dictionary<TKey,
> TValue>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, `Task<TResult>`, `Nullable<T>`,
> `ValueTuple<T1, T2>`) — each of which made `ValueTypeReceiverNames` report INCOMPLETE and nulled
> §10.2's key: an unconditional ~25 s publish on every build. Now 0.
>
> Tests: `NetShimPipelineTests.ValueTypeReceiverNames_SeesANestedGenericStruct_TheTask9Prerequisite`
> (the flipped 7b-I8 pin), `NetTypeResolverTests.NestedGenericSpellingRoundTripsBackThroughLookup`
> / `ADoublyNestedGenericSpellingAlsoRoundTrips` / `EveryDeclaringTypeSpellingTheResolverProducesResolvesBack`
> / `AConstructedGenericResolvesToItsDefinition`,
> `NetNameManglerTests.CollisionFreedomOverTwoConstructionsOfOneNestedGeneric`.
> Gates: fast 4083/0/1 (from 4078/0/1, +5) · Blnet fast 500/0/0 (from 495) · frozen P0 16/16 ·
> `NetShimPipelineTests` Integration 5/5 · §12.4 drift suites 98/0/0.
>
> ✅ **Follow-up (review item 3), same task, second commit: open-generic receivers now omit with
> BL6026 instead of failing in `csc`.** The collector ADMITTED a member whose signature names a
> type nested in an OPEN generic (`List<T>.GetEnumerator()`), because `FirstUnmarshalable` hunted
> open type parameters only in the type's OWN arguments and `List<T>.Enumerator` has none — it
> inherits `T` from its container. It now walks the CONTAINING chain as well, so the member is
> omitted with a positioned §7.2 BL6026 naming `T`, which is this project's standard over a late
> csc failure (the 7b read-only-property precedent). A CONSTRUCTED container is untouched:
> `List<Int32>.Enumerator` has a closed argument in the chain and stays admissible.
>
> ⚠ **This changes what a `<NetProxy Include="System.Collections.Generic.List`1" />` surface
> collects** — `GetEnumerator()` leaves it (and `Dictionary`2` loses `Keys`/`Values`/
> `GetEnumerator`). Those members could never have compiled: no repo fixture declared one, and
> the ManagedOwned five are unaffected (0 of 292 members).
>
> ⛔ **Mechanism correction to the note this replaces** (the conclusion was right, the reason was
> not — the original wording is preserved in commit `402de90`'s message and stands as history).
> `NetShimGenerator.Qualified` is applied to the DECLARING type (`:585`, `:621-625`) and to
> handle-form PARAMETERS (`:648`) — **never to the return type**. So
> `List<T>.GetEnumerator()` never emitted `global::…List<T>.Enumerator`; it failed on its own
> declaring type `global::…List<T>`. The enumerator spelling only reaches `Qualified` when the
> enumerator's OWN `MoveNext` is collected — which is exactly the §8.5 receiver path Task 8b's
> resolver fix exists for, and which the containing-chain check above does NOT block (a
> constructed enumerator stays admissible).

**The bug:** `NetTypeResolver.TypeName` drops a CONTAINING generic's arity, so
`List<T>.Enumerator` spells `System.Collections.Generic.List.Enumerator` — a name its own
`Lookup` returns **NotFound** for (the metadata form is ``List`1+Enumerator``). The collector
admits `GetEnumerator()` regardless. Task 9 hits it twice: the value-type-receiver set answers
"reference type" for every enumerator struct (⇒ §8.5's mutate-the-temporary **infinite
`MoveNext`, with no diagnostic**), and a qualified spelling would emit invalid C#.

**Why Task 8 didn't fix it:** `TypeName` feeds `NetNameMangler`, so changing it MOVES EXPORT
NAMES — a §12.4 identity change, not a behavior-preserving Step-0 edit. Currently bounded by
7b-I7's key poisoning (a wrong shim can never be cached), which is containment, not a fix.

⚠ **The blast radius is WIDER than the nested case, and the containment is NARROWER than it
sounds. Both were verified, not assumed:**
- **Any generic receiver poisons the key today, not just a nested one.** `TypeName` spells a
  constructed generic as `System.Collections.Generic.List<System.Int32>`, and
  `ResolveTypeDetailed` answers **NotFound** for that too (lookup goes through
  `GetTypeByMetadataName`, which wants ``List`1``). So a program whose surface has ANY
  generic-typed receiver reports INCOMPLETE and pays an unconditional **~25 s publish on every
  build, forever** — a standing performance cliff, not just a Task-9 correctness risk.
- **7b-I7 covers RECEIVERS ONLY.** A nested generic appearing solely as a PARAMETER or RESULT
  type is never looked up by `ValueTypeReceiverNames`, so it does not poison anything; it fails
  loudly later, in `csc`, on the emitted `global::…List.Enumerator` spelling.
- **It bounds PERSISTENCE, not the failure.** The wrong shim is still generated, published,
  deployed and RUN — freshly, every build. "Never cached" means the mistake is not made
  permanent, not that it is not made.

**Scope:** correct the nested-generic spelling; decide and document whether mangled names change
(they will for any nested-generic member — check whether any is in a surface today, and whether
`NetShimCache`'s template identity absorbs it); re-run the §12.4 slots≡exports invariants, the
mangler determinism/collision tests, and the frozen P0 16. Add a `List<T>.Enumerator` resolution
test AND a receiver-set test proving it now answers "value type".

### Task 8c: the four remaining §6.4 rows + enum arguments — **blocks Task 13 program #1**

> ⛔ **STATUS 2026-08-05 — NOT STARTED. MEASURED, correcting an earlier annotation in this
> same file that called it "partially landed".**
>
> That earlier note reasoned from `NetMarshalTable.cs:274-287`, which does carry all four rows
> with `NativeToNet`/`NativeFromNet` converter names and `IsMultiSlot: true`. **That table holds
> the DESIGN, not the lowering.** Every row is still refused at the call site, with purpose-built
> messages Task 8 left deliberately. Measured by driving one program per row through
> parse → analyze → lower:
>
> | Shape | Outcome |
> |---|---|
> | control (`Integer`) | LOWERED ok — the probe itself is sound |
> | `Decimal` | BL6019 *"whose §6.4 wire form is not a single slot — it is not lowered at the native boundary yet"* |
> | `Guid` | BL6019, same message |
> | `StringBuilder` | BL6019, same message |
> | enum member (`FileMode.Open`) | BL6019 *"§8.3's underlying-integral enum marshaling is not lowered at the native boundary yet"* |
> | `DateTimeOffset` | not measured — the probe used `DateTimeOffset.Empty`, which does not exist (only `Guid` has `.Empty`) |
>
> So this is real work, not wiring: multi-slot marshaling (Decimal = four scalars, Guid = sixteen
> bytes, DateTimeOffset = the declared scalar pair, StringBuilder = directional String) plus
> enum-member-constant lowering in the front end. `NetMarshalTable.cs:100-152` exposes
> `ResolveEnumUnderlyingType` for the enum half to build on.
>
> ⚠ **Durable lesson from the wrong annotation:** a table of converter NAMES is not evidence that
> anything calls them. Grep for callers, not definitions — the same rule this repo already
> records from the extensions work.

> ### ✅ 8c-1 DONE (`c3f65f7`) + regression fix (`d51c155`) — Guid & StringBuilder
>
> Both lower in the **argument direction**. Results refuse. `IsMultiSlot` is now a derived
> `SlotCount > 1`; the pointer rows carry `COutWire` + `ConverterForm` + `NativeTempDecl`.
>
> ⛔ **`c3f65f7` shipped a regression that no gate caught (suite was 636/636 over it).** Making
> the rows `WireKind.Scalar` opened two positions the call-site refusal does not reach, because
> a `<NetProxy>` DECLARED TYPE projects every member it has, called or not — and **`PlanMember`
> validated PARAMETERS only** on both sides. Fixed with a distinct `WireKind.ByValuePointer`
> plus an explicit result guard in both `PlanMember`s. **Durable rule: adding a wire row adds
> shapes no fixture builds. Ask not "did tests pass" but "which POSITIONS can this row now
> reach" — parameter, result, ByRef, delegate parameter, delegate return.**
>
> ### ⛔ 8c-2 (Decimal + DateTimeOffset) — MEASURED 2026-08-06 by a 10-agent recon+adversarial sweep
>
> **The premise below (and in `c29b4ca`) is REFUTED on two counts.**
>
> 1. **"A proxy has ONE result out-pointer" is false of the ABI.** §8.6's readback slot already
>    is `int32_t (*read)(uint64_t self, int32_t capacity, T* dst, int32_t* result)` —
>    `NetProxyEmitter.cs:507-508`, mirrored `NetShimGenerator.cs:640-641`. The drift oracle's
>    `SlotLine` regex is arity-agnostic (`NetShimGeneratorTests.cs:518-520`). The limit is in the
>    member-proxy PLANNER, not the transport. (The refusal text I wrote in `c3f65f7` overstates
>    it.)
> 2. **The stated reason for out-references — "a POD would need layout agreement with
>    `blnet_marshal.hpp`" (plan `:710-712`) — is wrong.** A POD returned by value from an
>    `inline` proxy never crosses the C ABI. Out-references are still preferred, but for a
>    DIFFERENT reason: a generated wire struct creates a new cross-producer field-name contract
>    (`NetProxyEmitter` declares, `NetCalls` reads `t.lo`) that **no oracle covers** — the drift
>    oracle compares slot LINES to exports and never sees proxy bodies or call-site text.
> 3. **`CsTypeFor` is NOT missing `const uint8_t*`** (plan `:733-737`) — the general const-strip
>    arm exists at `NetShimGeneratorTests.cs:572-573`.
>
> **⚠ THE DANGEROUS SEAM, and the reason a new `WireKind.MultiScalar` is mandatory:**
> `RequireBlittableScalar` (`NetShimGenerator.cs:574-586`) admits on `Kind == Scalar` ALONE.
> `unchecked((ulong)someDecimal)` is a **legal C# numeric conversion — it compiles and
> truncates**. A `Kind = Scalar` multi-slot row opens §8.4's gate to silent wrong numbers.
> (DateTimeOffset would instead fail CS0030 inside generated source.) This is the same class of
> bug as `d51c155`, one row over.
>
> **Chosen shape.** C slot gains N trailing scalar out-pointers `result0..resultN-1`; the C++
> proxy returns `void` and takes N out-REFERENCES; the call site declares N locals in `Prologue`,
> passes them, converts in `WriteBack`. A multi-slot PARAMETER is one `Prologue` temp
> (`auto t = to_net_X(v);`) plus N field expressions — `NetArgEmission.Expressions` is already a
> LIST for exactly this. Decimal → 4×`uint32_t`, fields `lo, mid, hi, flags`; DateTimeOffset →
> `int64_t`+`int16_t`, fields `utcTicks, offsetMinutes`. **`CsTypeFor` needs no new rows.**
>
> **Keep refused, deliberately:** `CWire` stays **null** on both rows → `HasByValueScalarSlot`
> false → ByRef stays refused for its own reason. `SlotCount` stays 4/2 — it is DATA, not a flag;
> the messages interpolate it.
>
> **Measured facts that matter.** Native converters are READY — no `CppNetMarshal.cs` edit
> (`from_net_decimal(uint32_t lo,mid,hi,flags)` `:103-110`; `from_net_datetimeoffset(int64_t,
> int16_t)` `:142-161`). Managed: **`DecimalFromWire` takes `int`, signed** (`BlnetShimSources.cs
> :141-142`); `DateTimeOffsetToWire` has **two `out`s** so the result arm needs STATEMENTS —
> `EmitWrapper` is already statement-capable, but `ToWire` is `string→string` and **cannot** be
> routed through. Wire-struct fields carry **NO trailing underscore** (`lo` not `lo_`; the P1
> fields do — `CppDecimalRuntime.cs:207`). `NetDateTimeOffsetWire` is **sizeof 16 with 6 padding
> bytes and must never cross by value** — the struct-taking `from_net_*` overloads exist for
> hand-written code only. Naming is arity-blind (`NetNameMangler.CanonicalIdentity:219-235` never
> reads the return type), so extra out-params cannot move a name. `EmitNetCallStatements` already
> honours a result arm that appends to `Prologue`, braces included — and those braces are
> **load-bearing** against `error: jump to label … crosses initialization`.
>
> **Traps, ranked:** (1) `Kind = Scalar` → silent truncation via §8.4; (2) landing the `WireOf`
> arms and the three refusals in SEPARATE commits — either window miscompiles, so **one commit**;
> (3) not adding Decimal/DateTimeOffset to `WireShapeSurface` (**append** — `slots[0..5]` are
> index-pinned at `NetProxyEmitterTests.cs:625-634`), leaving the oracle blind to width/arity/name
> divergence, which through a function pointer is **stack corruption, not a warning**;
> (4) passing the padded DTO struct by value; (5) inventing a `CWire` to satisfy a test;
> (6) emitting `t.lo_`; (7) dropping the brace region — every fixture for this seam is
> straight-line, so the suite stays green over it; (8) deleting `(void)r.ClockDateTime();`
> (`CppNetMarshal.cs:159`) which looks dead and is the only range check;
> (9) removing `if (row.IsMultiSlot) continue;` at `NetConversionPairTests.cs:408` without
> generalizing → NullReferenceException, not an assert.
>
> ⚠ **A Decimal PROPERTY has zero parameters**, and the analyzer's gate lives inside the
> parameter loop — so it is analyzer-clean and refuses positionlessly today. Any result-direction
> regression surfaces with NO source position.
>
> **Open:** fate of `MultiSlotConversionPairs` (zero production consumers once the analyzer arm
> goes — a second `IsSingleSlotValue`, which also has none); replacement vehicle for
> `LoweringRefusal_CarriesItsRealDiagnosticCode` (`Convert.ToDecimal` will start lowering —
> a Guid result is the candidate but its overload-probe path is UNVERIFIED).
> Full 13-edit map + 9 new tests with their mutations: recon `wf_90fc13e8-724`.

> ### ⛔ 8c-3 (ENUM) — MEASURED 2026-08-06 through the REAL CLI. Three claims in this file are FALSE.
>
> A 9-agent recon re-ran every enum claim as an actual compile. Do not trust the prose below it.
>
> | # | Probe | Result |
> |---|---|---|
> | **M1** | `System.IO.File.Open(…)` fully qualified | `BL6017 … 'System.Object' has no member 'File'` — **the qualified spelling is broken for EVERY .NET member**, so the bare-identifier detector misses nothing |
> | **M2** | `Dim n As Integer = CType(FileMode.Open, Integer)` | **compiles**, emitting `t0 = BasicLang::net::bl_net_System_IO_FileMode_Open__…();` |
> | **M3** | that same C++ compiled standalone | `error: invalid static_cast from type 'NetRef' to 'int32_t'` — **a green BasicLang build emitting uncompilable C++** (chipped) |
> | **M4** | `fi.Attributes = FileAttributes.ReadOnly` | **`Compilation successful!`** — an enum parameter ALREADY crosses end-to-end on the handle wire, ungated |
> | **M5** | `fi.Attributes = 1` | `Cannot assign value of type 'Integer' to 'FileAttributes'` |
> | **M6** | `Dim s = File.OpenRead(…)` | refused — `FileStream` is not ManagedOwned |
> | **M7/M8** | `File.SetAttributes(p, FileAttributes.ReadOnly)`, `New Regex(s, RegexOptions…)` | BL6019 on cpp, clean on csharp |
>
> **⛔ CORRECTION 1 — "`FileMode.Open` types as `Object` and lowers to NOTHING" is FALSE at this HEAD.**
> It lowers to a REAL shim export returning a GCHandle (M2). That sentence (from an earlier
> session, repeated in this file and in auto-memory) drove the whole "front-end constant
> lowering" framing. The framing survives, but for a different reason: the fold must also
> SUPPRESS an export that exists today.
>
> **⛔ CORRECTION 2 — enums are NOT uniformly refused.** M4 ships today through the synthesized
> setter, whose value parameter comes from `NetAccessorSynthesis`, not from Roslyn. Any design
> that retypes enum member access to the underlying integral turns M4 into M5 — **a regression on
> a currently-clean program.** `NetMemberResultTypeInfo` is ALSO the result-direction function,
> so that edit would additionally write a `uint64_t` handle into an `int32_t` destination: a
> wrong value, not a compile error. **Rejected on measurement, not taste.**
>
> **⛔ CORRECTION 3 — `File.Open(path, FileMode.Open)`, this plan's canonical shape, will NOT work
> after this change either** (M6, `FileStream` is not ManagedOwned). The real end-to-end targets
> are `File.SetAttributes` (void result) and `New Regex(s, RegexOptions.IgnoreCase)`.
>
> **CHOSEN SHAPE — "fold at the SLOT, not at the expression."** The analyzer, inside the
> already-native-gated `ReportUnlowerableWinnerParameters`, folds an enum-LITERAL argument to an
> `IRConstant` of the underlying primitive **only when the winner's parameter at that index is
> enum-typed**, recording it in a 4th `NetAstAnnotations` side table. `IRBuilder.Visit(MemberAccess)`
> mints the constant and **returns before emitting**, so no `IRFieldAccess` exists, so the
> collector mints no export — which is what removes M2's export. Three consequences are
> load-bearing: the analyzer's TYPING of the member access is unchanged (forces M4 to keep
> working); the underlying type rides ONLY on `NetParameterDescriptor` and is deliberately NOT
> propagated by `NetAccessorSynthesis` (so the setter's value parameter keeps the handle wire —
> positions with NO analyzer gate); and the C#-path preservation comes free because the recording
> site is already `if (!_netNativeBackend) return false;`.
>
> **Enum VARIABLES are OUT OF SCOPE** and every omitted shape keeps an existing measured refusal
> (`fi.Attributes` as an argument · `Dim m As FileMode` · flag `Or` · ByRef · arrays · results ·
> delegate signatures). The `:3121` arm is **NARROWED, never deleted** — deleting it is the exact
> miscompile (`NetRef` into an `int32_t` slot, and M3 proves `NetRef` has no integral conversion).
>
> ⚠ **`TryMapArgumentType` must KEEP spelling the enum** — it feeds `NetOverloadProbe`, which
> synthesizes C# and requires it to compile; `File.Open(a0, System.Int32)` is CS1503 and the call
> would stop resolving. Wiring `ResolveEnumUnderlyingType` in there (an obvious-looking one-liner)
> BREAKS the feature.
>
> ⭐ **T15 is the only test that proves the VALUE crossed** rather than the shape compiling:
> `New Regex("A", RegexOptions.IgnoreCase)` prints True, `RegexOptions.None` prints False.
> Everything else is satisfied by a wire carrying a wrong number.
> Full 15-edit map + 15 tests with mutations: recon `wf_d88ef1ba-49e`.

Detailed designs are recorded in the plan above (commit `c29b4ca`): three distinct
complications (arity>1 scalars via out-references returning `void`; direction-dependent C type
at arity 1 for Guid — the same shape String already has; one-way StringBuilder), the managed-side
traps (`DecimalFromWire` takes `int`; `DateTimeOffsetToWire`'s two `out`s force STATEMENTS in an
expression-shaped `EmitWrapper`), and the `CsTypeFor`/`WireShapeSurface` rows the drift oracle
needs or it stays blind. **Enum is worse than originally framed and was MEASURED:** `FileMode.Open`
types as `Object` and lowers to nothing, so descriptor carriage alone would turn today's precise
refusal into a broken program — the missing piece is enum-member-constant lowering in the front
end. May run any time before Task 13; does not block Task 9.

**Also inherited from Task 8's Step 2b — the drift test that did NOT land.** Task 8 shipped a
narrower oracle (the six §6.4 rows vs `blnet_marshal.hpp`); Step 2b's actual ask is still open:

- [ ] **The admissibility⇄wire-form tie.** Assert that every signature type
  `NetSurfaceCollector.FirstUnmarshalable` ADMITS gets a real wire form from BOTH emitters.
  `FirstUnmarshalable` is referenced by no test today. Note the shape of the claim: because both
  `WireOf`s default to Handle, "gets a wire form" is trivially true — so the test worth writing
  is the CONTRAPOSITIVE pair. (1) Every type the collector REJECTS (pointer, open type
  parameter, `Object`, `ref struct`, error type) must be unreachable as a slot — drive a surface
  containing one and assert the collector drops it before either emitter sees it. (2) Every
  §8.3 row that is NOT the handle default must be admitted. Otherwise a row silently demoted to
  Handle — the exact §6.4 "a native value must never become a handle" failure — still passes.
- [ ] **`WireShapeSurface` is blind to nine §8.3 rows** and its own docstring states the rule it
  breaks. Task 8 added `System.Char` coverage to the LOWERING but not to the drift oracle;
  `SByte`, `Byte`, `Int16`, `UInt16`, `UInt32`, `Int64`, `UInt64`, `Single` were never in it.
  Task 8's review commit closes this one — leave it closed.

### Task 9: §8.5 — consuming handle-represented collections ✅ DONE

> **DONE.** The wild pointer is dead and the boxed mutable-struct enumerator never happens.
>
> **The category marker.** `TypeInfo.NetHandleTypeFullName` (the resolver's C# spelling —
> `System.Collections.Generic.List<System.Int32>`, `System.Int32[]`) is tested FIRST in
> `MapType`, `BareCollectionType`, `IsCollectionType` **and `GetDefaultValue`** (that fourth
> site was found by emission, not by reading: a handle-typed `String()` is NAMED `"String"` and
> was initialized to `""`, a `std::string` initializer on a `BasicLang::NetRef`).
> **Mutation recorded:** disabling the `MapType` marker arm so the array/collection branches run
> first turns **5 of 15** new tests red — including both dedicated mutation targets and both
> end-to-end emit proofs. Restored; suite green again.
>
> **The producer.** `SemanticAnalyzer.NetHandleResultTypeInfo` lifts the flip's
> "Consuming arbitrary .NET objects lands with §8.5" refusal: every §8.3 handle row now types
> (admissibility mirrors `FirstUnmarshalable` from the name side — `Object`, `ref struct`,
> pointer, open generic still refuse). `NetResultStatement`'s gate moved from a NAME list to the
> DESTINATION's representation, so the two can no longer disagree.
>
> **For Each is interface-driven, end to end.** `IRNetEnumeration` (a FOUR-member bundle —
> `IEnumerable<T>.GetEnumerator` / `IEnumerator.MoveNext` / `IEnumerator<T>.Current` /
> `IDisposable.Dispose`) rides analyzer → `NetAstAnnotations` → `IRForEach` → collector →
> `CppCodeGenerator`, which emits a braced `while` with an RAII `NetRef` enumerator rather than a
> range-for. All four interfaces are REFERENCE types, so the shim reaches the BOX and
> `Unsafe.Unbox` never enters the picture.
>
> **The symbol-carrying seam LANDED** and is contained to `NetTypeResolver`:
> `ConstructedTypeSymbol` (parses the argument list back out of the spelling `TypeName` produced
> and re-applies `Construct`), `EnumerableElementTypeName`, `ConstructedIndexer`. No change to
> `NetSurfaceCollector`'s entry contract.
>
> ⚠ **Precisely what became reachable (review item 8a):** CONSTRUCTED spellings *in general*.
> The new test drives `IEnumerable<Int32>` vs `IEnumerable<String>` — two constructions of ONE
> generic — through `NetShimGenerator.Plan`'s `if (!seen.Add(name)) continue;` and asserts TWO
> exports survive. That is a real collision axis that did not exist before. The NESTED-generic
> shape `CollisionFreedomOverTwoConstructionsOfOneNestedGeneric` actually names
> (`List<int>.Enumerator` vs `List<string>.Enumerator`) **stays theoretical**: Task 9 never
> spells a type nested inside a generic — the enumeration goes through the INTERFACES, whose
> declaring types are top-level — and review item 2's guard now makes `ConstructedTypeSymbol`
> refuse the open nested shape outright.
>
> ⛔ **Review item 2, and why the obvious guard was wrong.** `ConstructedTypeSymbol`'s arity-0
> early return silently answered OPEN: a type nested in a generic declares no arguments of its
> own, so `List<T>.Enumerator` has `Arity == 0` and was returned unchanged. `ConstructedIndexer`
> would then have described an open-T indexer → wrong descriptor → wrong export → CS0246/CS0012
> after a ~25 s publish. **A round-trip test does not catch it** — measured, not assumed:
> `TypeName` spells `List<T>.Enumerator` back byte-identically, because the spelling is faithful,
> it is merely open. The guard is an OPENNESS walk over the containing chain (the same shape
> `FirstUnmarshalable` uses), with the round trip kept alongside for the other way in — a
> spelling `Lookup` accepts but `TypeName` would not produce.
>
> **Intent is carried, not guessed.** `NetSyntheticKind` on `NetMemberDescriptor`
> (`Setter`/`ArrayGet`/`ArraySet`/`ArrayLength`) closes the hole `IsSyntheticSetterShape`'s own
> caveat records. Deliberately outside `CanonicalIdentity`, so **no export name moved**.
> `SetterFor`'s indexer refusal is lifted in the one shared predicate, which is what carried the
> 7b read-only guard along with it.
>
> **`ByRefArguments` (review I5): carried, not scoped.** `IRCall.NetArgumentRefKinds` (internal,
> parallel) lets `CSharpBackend` emit `out` for a .NET `out` and nothing for `in`/`RefReadOnly` —
> the CS1620 is gone rather than confined to the native path. Pinned by
> `ANetOutParameterEmitsOutNotRefOnTheCSharpBackend`, which also pins that a VB user call (no
> entry recorded) still emits `ref`.
>
> ⚠ **I5 is closed for ONE call shape of two (review item 5).** `ByRefArguments` is populated
> only on the FUSED static arm (`IRBuilder.cs:~3561`); `IRInstanceMethodCall` carries no
> `ByRefArguments` list at all, so `r.TryGet(out x)` on a resolved .NET instance member still
> reaches neither backend's by-reference path. The native side refuses such shapes anyway
> (`MarshalNetByRefArgument`), so this is a C#-backend gap rather than a miscompile — but it is
> the remaining half of I5 and belongs with whichever task first lowers a ByRef instance call.
>
> **Gates:** fast subset **4100/0/1** (from 4085/0/1, +15) · Blnet fast **517/0/0** (from 502) ·
> four-suite C++ filter **121/0/0** (22 m 38 s) · frozen P0 **16/16** · `NetShimPipelineTests`
> Integration **6/6** — including the mandatory
> `Section85_ConcreteListIteratesAndANetArrayRoundTrips`, which publishes a real AOT shim and
> prints `10 20 30 7 42`: a CONCRETE `List(Of Integer)` iterating to completion with the right
> elements, plus the array read/write round trip. With the boxed-struct bug that test HANGS
> rather than fails, which is the whole reason it is not the array test.
>
> ⚠ **Known scope limit, not a gap in this task:** an arbitrary .NET type in DECLARATION position
> (`Dim b As New Bag()` for a non-registry type) is still BL6001 "no C++ mapping exists" —
> `MapType`'s handle arm for a NAMED type is still scoped to the `ManagedOwned` five per §12.4,
> and the marker only reaches INFERRED types. The §8.5 integration program therefore reaches its
> collections through STATIC members. Widening declaration-position .NET types is its own
> decision (it touches `ResolveTypeReference` and the §12.4 registry-scoping invariant) and
> belongs with §8.6/Task 10 or later, not here.
>
> ✅ **`arr.Length` — review item 1, fixed. The original note ("no producer") UNDERSTATED it: it
> MIS-ROUTED.** `Visit(IRFieldAccess)`'s `.Length` arm is name/`Kind`-keyed and did not consult
> the marker, and a handle `System.String[]` is `TypeInfo(Name: "String", Kind: Array)` — so
> BOTH of that arm's branches claimed it and emitted `parts.length()` / `parts.size()` on a
> `BasicLang::NetRef`. It is the very next thing a user writes after `parts(0)`. Now:
> `SemanticAnalyzer.NetArrayLengthFor` stamps §8.5's synthesized `get_Length` onto the node
> (there is no annotation to find — a .NET array declares no members in metadata at all, which
> is why §8.5 synthesizes them), and the marker is additionally tested BEFORE every name/`Kind`-
> keyed arm in `Visit(IRFieldAccess)` so any OTHER member on a handle receiver is a loud BL6019
> instead of native code over a handle. `ModuleUsesCollections` is also name-keyed and is
> deliberately left alone — it decides only whether to splice the collection runtime preamble,
> so a false positive costs an unused preamble, never a wild pointer; a comment records that.

> 📎 **Naming note on the bullet below (review item 8b).** The literal spellings
> `bl_net_Array_Get__<T>__int32` / `_Set` / `_Length` do NOT exist as export names and never
> could: §7.3 makes `NetNameMangler` the single naming scheme, and it derives every name from
> (declaring type, kind, name, static-ness, arity, parameters) plus a SHA-256 suffix. The three
> synthetics are ordinary descriptors on a `T[]` declaring type, so they mangle like everything
> else — e.g. `bl_net_System_Int32___get_Item__System_Int32_<16 hex>`. The spec's spelling is
> indicative shape, exactly as §4.2's `Regex_Match__string` example already is (the mangler's own
> header, design decision 6, records that it is not a binding format).

**Files:**
- Modify: `BasicLang/Net/NetSurfaceCollector.cs` (synthetic exports: an array Get/Set/Length
  trio per used element type — spelled by the mangler, see the note above; `get_Item`/`set_Item`
  collection for indexer access;
  `IEnumerable<T>`/`IEnumerator<T>` members for `For Each` — obtained through the INTERFACE,
  never the concrete struct enumerator), `NetShimGenerator.cs` (synthetic export bodies),
  `BasicLang/CppCodeGenerator.cs`: **`MapType` `:500-504` + `BareCollectionType` `:577-587` +
  `IsCollectionType` `:595-602` test the managed marker FIRST and return `NetRef`/null/false for
  managed-marked types** (the wild-pointer kill — the consumer sites `:3662-3703`/`:3634-3660`
  merely follow), `IRIndexerAccess`/`IRIndexerStore`/`IRForEach` lowering arms for `NetRef`
- Test: `VisualGameStudio.Tests/Blnet/NetCollectionConsumptionTests.cs` (new)

> ⛔ **ARCHITECTURAL INPUT from Task 8b's review — read before designing this task's collection.**
> **The surface is collected strictly BY NAME** (`NetSurfaceCollector.cs:~357` →
> `CandidateMembers(typeName)`), and **a name resolves to the DEFINITION**. So
> `GetMembers("…List<System.Int32>")` returns descriptors whose declaring type is the OPEN
> `System.Collections.Generic.List<T>` — no current path can produce a CONSTRUCTED spelling at
> all. Two consequences for this task:
> - `List(Of Integer)` work needs a **symbol-carrying collection seam** (construct the
>   `INamedTypeSymbol`, then describe) rather than the by-name door. Task 8b's per-level
>   `TypeName` is already the right OUTPUT format for constructed spellings — what's missing is
>   a way to hand the collector a symbol instead of a string.
> - Until that seam exists, the construction-collision Task 8b protects against
>   (`List<int>.Enumerator.MoveNext` vs `List<string>.Enumerator.MoveNext` mangling alike and
>   the second being silently dropped by `Plan`'s `if (!seen.Add(name)) continue;`) is
>   *unreachable* — which is why "0 export names moved" was internally consistent. **The moment
>   this task adds the symbol seam, that collision becomes reachable** and the mangler test
>   `CollisionFreedomOverTwoConstructionsOfOneNestedGeneric` stops being theoretical.

**The trap this task exists for (spec §8.5, verbatim intent):** implementing only the consumer
sites and not `MapType`/`BareCollectionType` ships the wild pointer — a managed `List<T>` local
declared as `std::shared_ptr<BasicLang::List<…>>`. And the two obvious iteration tests (array,
iterator-class `IEnumerable<T>`) BOTH pass with the boxed-struct-enumerator infinite-loop bug
present — the CONCRETE `List<T>` iteration test is mandatory.

**Steps:**

- [ ] **Step 1:** red emit tests: `Dim a = obj.GetValues()` (inferred → keeps handle, `NetRef`);
  `a(0)` → `bl_net_Array_Get__…`; `list(0)` → `get_Item`; `For Each x In netList` → the
  IEnumerable-interface enumerator protocol; a managed-marked `List` type maps to `NetRef` and
  `BareCollectionType` returns null (mutation: reorder the `MapType` arms → red; restore; record).
- [ ] **Step 2:** implement collector synthetics + generator bodies + lowering. Green.
- [ ] **Step 3:** Integration (real publish): iterate a CONCRETE `List(Of Integer)` from a .NET
  call — terminates with correct elements (the mutable-struct-enumerator guard); array
  read/write round-trip.
- [ ] **Step 4:** fast subset; commit (`feat(p2a2): §8.5 collection consumption`).

> ⛔ **EMISSION-SEAM CONTRACT — decide this BEFORE building on `NetArgEmission` (Task-8 quality
> review I1).** `EmitNetResult` writes prologue → call → epilogue as straight-line C++, and the
> call statement contains `NetCheckTyped`, **which throws**. So **`Epilogue` is SUCCESS-PATH
> ONLY**: a thrown managed exception skips every epilogue line. That is *correct* for ref/out
> write-back (.NET wouldn't write back either) and is why nothing is broken today — but §8.6's
> copy-out/release and §8.4's delegate register/unregister are exactly the statements that MUST
> run on the throwing path, and a leak-on-exception discovered in Task 11 is the expensive way to
> learn this. Secondary: epilogues currently append in argument order, while releases want
> REVERSE order. **Required disposition before this task emits its first release:** document
> `Epilogue` as success-path-only AND add an RAII slot (a guard object emitted in the prologue
> whose destructor does the release), rather than extending the epilogue list. Also fold in
> quality-review M1 while you are there: the three-part write loop is duplicated verbatim in
> `EmitNetResult` and `EmitNetFieldStore` — extract one `EmitNetCallStatements(...)` so this
> change is made once and the compute-before-write ordering becomes structural rather than
> conventional.

### Task 10: §8.6 — native collections crossing outbound

**Files:**
- Modify: `NetShimGenerator.cs` (per-element-wire-form array helpers:
  `bl_net_array_new_int32(int32_t count, const int32_t* src, uint64_t* out)`, String variant,
  mirrored readback), `BasicLang/CppCodeGenerator.cs` (declared-native-array assignment
  materialization; by-value param copy-in; ref/out copy-in-and-read-back),
  `SemanticAnalyzer.cs` (BL6019: `List`/`Dictionary`/`HashSet` outbound, nested element types,
  handle element types)
- Test: `VisualGameStudio.Tests/Blnet/NetOutboundCopyTests.cs` (new)

**Representation rule (stated once, §8.6):** a .NET `T[]` VALUE is always a handle; copying
happens only when a NATIVE array sits on the other side of an assignment or parameter.
`Dim a = obj.GetValues()` keeps the handle; `Dim a() As Integer = obj.GetValues()` materializes
by copy; by-value param copies in (one-way — the §14.11 divergence); ref/out array slots read
back (EXEMPT from the divergence — the parity program depends on this exact scoping).

**Steps:**

- [ ] **Step 1:** red tests for each table row + each BL6019 row.
- [ ] **Step 2:** implement; green.
- [ ] **Step 3:** Integration: pass a native `Integer()` to a .NET method that sums it; ref-slot
  readback visible, by-value mutation NOT visible (both asserted — this is §14.11 pinned early).
- [ ] **Step 4:** fast subset; commit (`feat(p2a2): §8.6 outbound array copy`).

---

## ✅ TASK-10 REVIEW FINDINGS (2026-08-04) — **ALL RESOLVED 2026-08-05, DO NOT REDO**

> ⛔⛔ **STATUS 2026-08-05.** Findings **1, 2 and 3 are CLOSED**, each verified in the working
> tree rather than from memory. This section is retained for its *reasoning*, **not as a work
> list** — Task 11 must not spend a single step re-doing any of it. The only live item left here
> is finding **4** (enum arrays), which belongs to Task 8c.
>
> The heading used to read *"fix these FIRST, before Task 11"*, which is exactly how a fresh
> implementer would be sent to redo finished work.

Task 10 shipped `5844df8` → `c833fde` → `cfb9f34` → `a10a689` (all pushed). Combined review:
**❌ Issues found — feature correct and well-built, ONE reachable codegen break + doc drift.**
Everything else verified ✅: the four §8.6 rows, all four BL6019 rows with anti-vacuity partners,
the §14.11 opposite-directions proof, the RAII/reverse-destruction claim, the M1 three-site
extraction, all four self-found bug fixes, `NetArrayCopy`'s shape, and no new collector arm.

### 1. BLOCKING — the RAII guard is a block-scope declaration in goto-lowered C++

`CppCodeGenerator.NetCalls.cs:596-600` writes `BasicLang::NetRef blnet_tN = …;` straight into the
current block. This backend lowers ALL control flow to labels + `goto` in one flat function scope
(`CppCodeGenerator.cs:2959-3004`, `Visit(IRLabel)` `:3188`, `EmitInlineRegion` `:3862`), so a
forward `goto` that skips the region **crosses the guard's initialization**. Reproduced by the
reviewer with g++ 13 `-std=c++20` on real generator output for
`If N > 0 Then Dim S = Convert.ToBase64String(B)`:
`error: jump to label 'if0_end' … crosses initialization of 'NetRef blnet_t0'`.

**Pre-existing class, materially widened.** §8.6's copy-in fires on **any by-value native array
argument** (`Convert.ToBase64String(bytes)` inside an `If` is ordinary code). Every fixture test
and the Integration pin uses a straight-line `Main`, so nothing catches it.

⚠ **This is the single biggest thing that will fight Task 11** — the Step-0 contract routes
callback register/unregister into the same prologue slot, and `If x Then list.Sort(AddressOf Cmp)`
is the natural first delegate program.

**Fix once, at the seam:** have `EmitNetCallStatements` wrap prologue + call + write-back in a
`{ … }` block (guards then release at the end of the call's own scope — tighter than today, and
it preserves the reverse-destruction property). Alternative: hoist guard *declarations* into
`DeclareLocalsAndTemporaries` and emit only the assignment inline. **Add a test with a copy-in
inside an `If` and inside a loop** — the whole fixture is straight-line today.

#### ✅ RESOLVED 2026-08-04 — and one claim above was too broad

`EmitNetCallStatements` now braces the region **when, and only when, there is a prologue** (no
prologue means no declaration, so no jump can cross anything, and unconditional braces would have
churned every existing expected-output). The C++ rule was re-proved against this backend's exact
`If` lowering — `if (c) { goto L; } else { goto E; } L: ; Guard g(1); goto E; E: ;` — with
g++ 14.2: `error: jump to label 'E' … crosses initialization`, and the braced form compiles.

⛔ **Correction to the sentence this block replaced.** It claimed Task 8's ref/out scalar prologue
(`NetCalls.cs:712`) "is ill-formed the same way". **Measured: it is not, for the row that was
named.** A plain `Integer` ByRef emits **no prologue at all** — §8.3's pointer slot hands the
proxy the native local directly (`…TryParse("42", n)`), because for that row the native variable
already IS an lvalue of the wire type. Only rows whose native representation DIFFERS from the wire
(Char, §6.4's pairs) reach `MarshalNetScalarByRef` and emit `int32_t blnet_t0{};`. The seam fix
covers those for free — they share `Prologue` — but the "Task 8 is broken too" framing was wrong
and should not be carried into Task 11's planning.

**Tests:** `NetOutboundCopyTests.Row3_CopyInInsideABranch_IsBraceScopedForGotoLowering` pins the
braced region for both an `If` and a loop, and **fails when the brace is disabled** (verified by
mutation). `NetProxyStubRunTests.OutParameter_InsideBranchAndLoop_WritesBackEveryIteration` is
kept as branch/loop write-back coverage with a comment recording that it canNOT catch the goto
regression, for the reason above — it was written believing it could.

### 2. ✅ CLOSED — §12.4 exemption text corrected in all three places

The invariant itself was always holding (both emitters `.Concat(NetArrayCopy.RequiredExportNames(
surface))`, `blnet_bind_all` binds them, both sides derive from one `RequiredForms`). The stale
*prose* has now been fixed everywhere it lived — **re-verified in the tree 2026-08-05:**

- ✅ `NetArrayCopy.cs:119` — reframed ("P2a-1's header called these §12.4-exempt; making them
  ordinary slots is strictly better, because the exemption would have been an invariant hole
  nothing tested").
- ✅ `NetShimGenerator.cs:68-74` — now states the array helpers **ARE** emitted and are **NOT**
  exempt, and closes with: *"Task 11 should read this before deciding how to carry the delegate
  dispatcher: '§12.4-exempt' is not an available answer."* Exactly the file Task 11 opens.
- ✅ `NetShimGeneratorTests.cs:191-195` — the drift test's failure message now teaches the right
  rule and says outright that a failure over an array helper means real drift, not a missing
  exemption.
- ✅ **Spec §12.4** (`…aot-shim-design.md:1271-1274`) — carries the improvement **normatively**,
  with the same Task-11 warning.

⚠ Only the HISTORICAL P2a-1 plan (`2026-07-29-p2a1-dotnet-native-foundation.md:1664-1665`) still
contains the original claim. That is a completed-phase record of what was true then; leave it.

### 3. ✅ CLOSED — the false "measured" claim is retracted in place

`NetCalls.cs:311-315` now carries an explicit retraction: the earlier comment claiming the
`t = call(); v = t;` fusion "takes the whole `CppProjectBuilder` → `CompileProjectFiles` path"
and that "the fast subject cannot construct the shape" **was wrong and is withdrawn** — the plain
CLI single-file path (`BasicLang.exe f.bas --target=cpp`) runs the same
`OptimizationPipeline.AddStandardPasses` and fuses it too.

### 4. Task-8c friction (record now, act in 8c)

`NetArrayCopy.Forms` is keyed on exact .NET element full name, which cannot express "any enum".
When 8c lands `enum → underlying integral`, an **enum array** falls through to the handle-element
BL6019 arm and tells the user their enum "is itself a .NET handle" — wrong explanation for the
right refusal. Either add an enum arm to the message or give enum arrays a copy form keyed on the
underlying integral.

### 5. Noted, no action

A guard currently lives to the end of the enclosing block (a handle minted for argument 1 of a
call at the top of `Main` is held until `Main` returns) — fixed for free by finding 1's braces.
And `TypeInfo.Equals` now treats a handle array and a native array of the same element as
identical; the only reachable pairings are assignment/store seams that `NetMaterializedValue`
covers, because user `Sub`/`Function` array parameters **do not parse** in this compiler today
(`Sub Take(A As Byte())` → *"Expected ')' after parameters but found LeftParen"*). It becomes a
hole the day array parameters land.

---

### Task 11: §8.4 delegates + §11.2 callback exceptions + `AddressOf` lowering

**Files:**
- Modify: `NetShimGenerator.cs` (managed dispatcher: callback handle → real
  `Action`/`Func`/`Comparison`/`Predicate` via the universal thunk; `immediate = false` always),
  `NetProxyEmitter.cs` (verify/ensure `BlnetCallScope` wraps every `g_net` call — P2a-1 emitted
  the proxy template, confirm the scope + the §15.12 depth-0 `blnet_pump()` after `NetCheck`),
  `BasicLang/CppCodeGenerator.cs` (lambda → `NativeCallbackFn` + computed `BlnetSlotDesc[]` +
  `CallbackFlags` + registration + `blnet_callback_release` at end of registration lifetime;
  **`AddressOf` lowering — NEW, `UnaryOpKind.AddressOf` has zero C++ handling today** — route a
  named-function reference through the same callback-registration machinery), §11.2: the native
  side of the dispatcher rethrows a native exception raised inside a callback into the managed
  frame per P0's C4 (recon: the test dispatcher only records a synthetic type string — real work)
- Test: `VisualGameStudio.Tests/Blnet/NetDelegateTests.cs` (new)

**The mandatory non-obvious test (spec §12.3):** a RESULT-BEARING delegate (`Comparison(Of T)`
inside `List.Sort`) invoked synchronously, asserting it ran INLINE and returned its value — an
`Action`-only test passes even with `BlnetCallScope` missing (queued Actions eventually run).

⚠ Task-5 carry-forward: delegate SIGNATURES are never checker-walked (`module.Delegates` —
pre-existing gap the flip's `MapTypeName` row only partially closes for ManagedOwned names);
while touching delegate lowering here, add the checker walk (or a documented decision not to)
so arbitrary unmapped names in delegate params stop reaching raw C++.

**Steps:**

- [ ] **Step 1:** red: emit tests for lambda-arg lowering (slot desc computation for
  `(Integer, Integer) → Integer`) and `AddressOf Handler` producing the same registration;
  scope-presence assert on every emitted proxy body; pump-at-depth-0 assert.
- [ ] **Step 2:** implement native side + managed dispatcher. Green.
- [ ] **Step 3:** Integration: `List(Of Integer).Sort(AddressOf CompareDescending)` sorts
  descending (inline result proof); a throwing BL lambda inside `Sort` surfaces as a catchable
  exception on the BL side (§11.2 both directions); `blnet_callback_release` fires (leak assert
  via the P0 handle-count channel).
- [ ] **Step 4:** fast subset; commit (`feat(p2a2): delegate arguments + callback exceptions`).

### Task 12: §12.3 generated-shim conformance suite

**Files:**
⛔ **Task-7b review finding — BlnetGenLib MUST be compiled against the net8.0 REFERENCE pack**
(reuse `NetShimPipelineFixture.ReferencePackAssemblies`). Built against the shared framework's
implementation assemblies it carries a direct `System.Private.CoreLib` reference and every use
of its types inside the generated shim is **CS0012** — the whole conformance suite would fail
for a reason unrelated to conformance. (Same root as the recorded latent asymmetry: the analyzer
resolves against implementation assemblies while the shim compiles against reference assemblies.)

- Create: `VisualGameStudio.Tests/TestAssets/BlnetGenLib/` (purpose-built C# library:
  instance/static/ctors/properties/**an indexer-bearing type** (§12.3's "indexer read/write on a
  user type")/overloads/generics/inheritance/every §8.3 row/throwing members/delegate-taking
  members), `VisualGameStudio.Tests/Blnet/NetConformanceTests.cs`
  (`[Category("Integration")]`, `[NonParallelizable]`, P0-harness pattern: fresh process per
  scenario, `PASS <name>`/`FAIL <name>: detail`, exit 0/1/2, async-read-before-WaitForExit)

**Scenario list (each a named scenario, spec §12.3):** instance + static calls; constructors;
properties; overload selection; .NET generics from non-generic BL; inheritance; every §8.3
marshaling row incl. `Char` and a `ref struct` BL6019; null/`Nothing` as argument/return/receiver
(handle-0 rule); `ref`/`out`; exception propagation BOTH directions incl. typed + subclass catch;
handle lifetime + release; startup handshake failure modes (bad ABI, missing export, missing
DLL — each with its specified message/stream/exit code, fixed HERE per §9.3); the four
called-out rows: result-bearing inline delegate; concrete-`List<T>` iteration; empty-surface
`Try/Catch ex As Exception` program; `<NetProxy>` omitted-member BL6026-and-still-builds.

⚠ Task-3 review carry-forwards for this suite: a `ref`-returning declared member (e.g.
`CollectionsMarshal`) is ADMITTED by the Task-3 omission filter (`SignatureTypes` checks
`ReturnType`, not `ReturnsByRef`) — decide reject-vs-omit here with a scenario; and the BL6022
unknown-type message suggests `` List`1 `` spelling, but declaring an OPEN generic BL6026-omits
nearly every member — consider the message tweak or closed-generic spelling support here.

**The §12.2/§12.3 boundary:** the 16 frozen P0 scenarios stay EXACTLY as they are (hand shim).
`ShimPublishHasNoAotAnalysisWarnings` keeps its hand-shim scope; generated-shim ILC warnings are
BL6020 INPUTS, never that assertion's subject.

**Steps:**

- [ ] **Step 1:** build `BlnetGenLib` + harness skeleton; first 5 scenarios red-then-green.
- [ ] **Step 2:** remaining scenarios in 2-3 batches, each batch red-then-green.
- [ ] **Step 3:** full suite green twice consecutively (publish cache makes run 2 cheap — also
  implicitly re-proves the cache); commit (`test(p2a2): generated-shim conformance suite`).

### Task 13: §12.1 parity oracle extension

**Files:**
- Modify: `VisualGameStudio.Tests/Compiler/BclBackendParityTests.cs` (new `ParityProgram` rows
  after `:786-801`; driver unchanged)
- Modify (if D-P1 needs it): `NetTypeResolver` allowlist finalized in Task 4 — this task only
  PROVES it

**The seven programs (spec §12.1 table, each pinning a would-be-silent divergence):**
1. the six §6.4 conversion pairs round-tripped through a .NET call (tick epochs + Decimal bits).
   ⛔ **BLOCKED until Task 8 lands the Decimal/Guid/DateTimeOffset/StringBuilder wire rows** —
   7a shipped only DateTime + TimeSpan; the other four BL6019 at call sites today. If Task 8
   slips them, this program must be split (DateTime+TimeSpan now, the rest pinned-expected-fail
   with the §6.4 gap named) rather than silently dropped;
2. multiple `Catch` clauses incl. a SUBCLASS match around a throwing .NET call — **with control
   flow in at least one catch body** (the `_nex` label-redefinition guard, spec §11.1).
   ⛔ **C2312 constraint (Task 1 finding):** at most ONE non-`Exception` typed clause per `Try` —
   two collapse to duplicate `catch (const std::runtime_error&)` per-clause handlers (MSVC
   C2312, pre-existing `MapCatchType` behavior). Author as derived-then-`Exception` (the
   `Cpp_Run_TypedCatch_SourceOrderPreference` shape) unless per-clause dedup has landed;
3. `Throw New ArgumentException` caught locally in a file that also calls .NET (dual shape);
4. an array mutated inside a .NET call (§14.11 one-way copy — expected output documents the
   divergence, ref-slot variant shows the readback);
5. `Char` round-trip incl. a value above U+00FF (§14.10 — expected output documents narrowing);
6. `Nothing` passed and returned across the boundary (§8.2 handle-0);
7. `ToString()` on `System.IO.Stream` (non-overriding — D-P1 makes this a PASSING row; if it
   fails, D-P1's implementation is wrong, not the program).

✅ **NOT blocked by the Task-7b Boolean finding** (`Console.WriteLine(bool)` → `1`/`0` natively,
chip `task_6fd2c7e4`): the inherited constraint list below ALREADY forbids raw Boolean prints, so
these programs dodge it by construction. Print through `If`/string instead — and note the `If`
form is the stronger oracle anyway, since a wrong answer changes the branch.

P1's Task-13 constraint list applies verbatim (no raw Boolean prints, no `t<N>` locals, `CType`
not `CInt`, ASCII-only, invariant-culture — see `2026-07-27-p1-native-bcl-types.md` Task 13).
The C# leg runs under `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; the shim runs
`InvariantGlobalization=true` — same regime by construction, but program 1's formatting must
still avoid culture-sensitive output.

**Steps:**

- [ ] **Step 1:** program 1 first (highest value), red-then-green; then 2-7 one at a time.
- [ ] **Step 2:** full parity battery (13 P1 + 7 new = 20 programs) green ×2.
- [ ] **Step 3:** commit (`test(p2a2): .NET parity programs`).

### Task 14: integration tests + §12.4 invariants completion

**Files:**
- Modify: `VisualGameStudio.Tests/Blnet/NetBuildPipelineTests.cs` + new
  `NetIntegrationTests.cs` (`[Category("Integration")]`); drift invariants wherever the P2a-1
  siblings live (`BlnetContractTests.cs` et al.)

**Integration set (spec §12.5):** a BL program and a hand-written `.cpp` both calling the same
C# test library (the `.cpp` includes `blnet_proxies.g.hpp` — the §9.5 merge proof); **a
zero-`.bas` project** whose only surface is `<NetProxy>` — obj/gen populated,
`blnet_startup.g.cpp` compiled+linked, shim initializes (the path §9.5's four gates used to
block); a delegate round-trip; the `Console.WriteLine`-only EMPTY-surface program — no blnet
artifacts, phase 5 skipped (the claim-predicate regression guard); cold-then-warm cache.

**§12.4 invariants completed:** proxy-table slots ≡ surface-derived subset of shim exports
(scoped — core seven + array helpers excluded); `ManagedOwned → NetRef` and no other registry
name; `ManagedOwned ∩ Rejected = ∅`; resolver exclusion set ≡ claim set at both granularities
(name-granular + per-call: `Console.WriteLine` native, `File.ReadAllText`/`Console.ReadKey`
shim-routed); generated `HandleTable` ≡ `BlnetShimSources` ≡ hand shim; status enum ≡
`GenerateStatusEnumCs`; `AbiVersion` ≡ contract (several exist from P2a-1 — extend, do not
duplicate; grep `BlnetContractTests`/`NetShimGeneratorTests` first).

**Steps:**

- [ ] **Step 1:** each integration test red-then-green individually.
- [ ] **Step 2:** invariant gap-fill after a grep audit of existing coverage.
- [ ] **Step 3:** fast subset; commit (`test(p2a2): integration + drift invariants`).

### Task 15: full verification + closeout

- [ ] **Step 1:** full gates: fast subset; Blnet filter (16 frozen scenarios + all new suites);
  C++ fixtures + the 20-program parity battery; `TemplateBuildSweepTests` (Integration); one
  full-suite run (`~39 min`, redirected to scratchpad).
- [ ] **Step 2:** empty-surface inertness: a console AND a game template project emit generated
  code + build logs whose diff vs `2752a96` consists of EXACTLY the two enumerated runtime
  splices (`NetException`, `NetRef`) and nothing else — zero user-program TU changes, zero
  diagnostic changes, identical runtime stdout (the P2a-1 methodology, scripted diff with the
  known-splice subtraction).
- [ ] **Step 3:** spec status updates: §14.15 → Resolved (D-P1); §15.11 → Decided (D-P2);
  §15.6 → Recorded-unchanged (D-P4); spec header status → Implemented (P2a complete);
  `AbiVersion` still 1 (assert, §13). Stale-prose sweep: `NetInertnessTests` fixture header
  still claims "NetResolverFactory set at exactly ONE site repo-wide" (stale since Task 4's
  C# warning row); grep the Blnet fixtures for other Task-4/5-staled prose.
- [ ] **Step 4:** IDE binary refresh if the session's rules call for it (the prebuilt `IDE/`
  binaries ship the compiler — same procedure as commit `aada862`, including the deps.json
  closure check via `dotnet exec --depsfile`).
- [ ] **Step 5:** memory updates (MEMORY.md + the dotnet-in-native topic file); final commit
  (`feat(p2a2): closeout — P2a complete`); push per user instruction.

---

## What P2a-2 deliberately does NOT do

- No events, no interface implementation by BL types (D6 — P2b+).
- No `<ProjectReference>` support (BL6021 error names the `<Reference>`+`<HintPath>` workaround).
- No library-output .NET surfaces (BL6025), no .NET calls in BL generic bodies (BL6024).
- No LSP squiggles for BL60xx (findings remain CLI/IDE build output; editor wiring is follow-on).
- No CoreCLR hosting (P2b); `NetShimGenerator`/`NetShimPublisher` remain the only
  transport-aware components — if any other component needs transport knowledge, STOP: the seam
  is wrong (spec §4.2 invariant).
- No `AddressOf` on the C# backend changes; no MSIL/LLVM work of any kind.
- `ExternalLibraryLoader.cs:169`'s `Assembly.LoadFrom` channel stays untouched.
- §15.7/§15.8/§15.9's items stay open (chip-class, not P2a-2).
- **`ConfigureTypeRegistry` into `CompileUnit` — REMOVED from Task 2 by measurement
  (2026-08-02).** The wiring changes type inference for existing programs (`String.Split` →
  synthetic `"String()"` class; four gap-fill canaries stop answering null), contradicting spec
  §6.3's C#-preservation row — see the dated correction in spec §6.2. The
  `TypeRegistryFallbackPinningTests` fixture (11 tests: 10 pins + the canary) holds the
  divergence mechanically;
  the native path needs none of it. Deferred as a follow-up outside P2a-2 (chipped).

## Execution notes for the controller

- Fresh implementer subagent per task + spec-compliance review + code-quality review + fix
  loops, per superpowers:subagent-driven-development. Mutation-test every guard — this session's
  standard: a test that never went red proves nothing.
- Tasks 1→15 are strictly ordered except: Task 6 may run any time after Task 2; Tasks 12/13 may
  interleave after Task 11. Never two implementers concurrently (shared suite).
- Expected publish costs in Integration tests: cold ~27s, warm ~11s, cache-hit ~0s (measured,
  spec §15.1) — budget test timeouts accordingly (600000 ms ceilings).
- If any task discovers the spec is wrong (P2a-1 found 14 plan defects and several spec slips),
  correct the SPEC in the same commit with a dated annotation, as P2a-1 did — never code around
  a known-wrong spec silently.

