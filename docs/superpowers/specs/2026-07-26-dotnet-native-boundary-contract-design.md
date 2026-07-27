# .NET ⇄ Native Boundary Contract (v1) — Design

**Date:** 2026-07-26
**Status:** Implemented (conformance suite: VisualGameStudio.Tests/Blnet/) — P1/P2 pending
**Owner feature:** .NET class access in `SolutionType.Native` projects (BL + C++ mixed, compiled to native via the C++ backend)

## Context

The BasicLang Native project type compiles BL to C++ and links hand-written C++
in the same project. Today it has **no** path to .NET: `CppCapabilityChecker`
rejects every unmapped .NET type ("no C++ mapping exists"), and `.blproj`
`<Reference>` items are parsed (`ProjectFile.cs` → `AssemblyReferences`) but
consumed only by the C# backend path (`Program.cs`). The user wants BL **and**
hand-written C++ in a native project to consume .NET classes — BCL types,
user assemblies, and NuGet packages — while still producing a native binary.

The work is decomposed into three sub-projects, built in this order:

1. **This spec — the boundary contract.** The rules every crossing obeys.
   Transport-agnostic: v1 transport will be a Native AOT shim library
   (`PublishAot` + `NativeLib=Shared` + `[UnmanagedCallersOnly]`), but nothing
   in the contract assumes AOT, so a hosted-CLR transport (hostfxr) can be
   added later for reflection-heavy libraries without changing the contract.
2. **P1 — native BCL types** (separate spec, next): pure-C++ implementations of
   `DateTime`, `TimeSpan`, `Guid`, `StringBuilder`, `Decimal`, `SByte`;
   removes them from the reject list. No managed runtime involved.
3. **P2 — .NET library access** (separate spec, last): the AOT shim, generated
   C++ proxies, build/`.blproj` integration, and hand-written C++ access to
   the same proxies.

P1 and P2 both read this contract; agreeing on it first prevents the central
collision (two unrelated `DateTime` types, one native and one managed) and
fixes the lifetime/threading/error rules before any code generation exists.

## Goals

- Define type ownership so native and managed representations never collide.
- Make lifetime bugs (use-after-release, double-release) **diagnosable errors**,
  not memory corruption.
- Define one call ABI: calling convention, string rule, error rule.
- Full duplex from day one: native→managed calls **and** managed→native
  callbacks (delegates/events), with explicit threading semantics.
- Be implementable incrementally and testable before P2 exists.

## Non-goals

- Which .NET APIs are surfaced, proxy generation, `.blproj` syntax → P2.
- How native BCL types are implemented → P1.
- Cross-project compilation, C# backend changes, COM (ruled out), MSIL/LLVM
  backends (out of scope per project direction).
- Generics instantiated across the boundary (v1 rejects them with a clear
  diagnostic; design slot reserved, see Open Extensions).
- Shim unload / hot-reload — Native AOT shared libraries cannot be unloaded
  (`FreeLibrary`/`dlclose` unsupported per Microsoft docs). Ruled out
  permanently, not deferred.

## Decisions and alternatives considered

| Decision | Chosen | Rejected alternatives |
|---|---|---|
| Handle representation | Generation-tagged table index | Raw `GCHandle` token (UB on misuse — same failure class as the rcore C8 dangling-pointer bug); debug-only checked handles (shipping config becomes the untested one); explicit user-written release (hostile, leak-by-default) |
| Call direction | Full duplex in v1 | One-way v1 (second migration later); polling-only compromise (kept, but as the *cross-thread* half of the duplex design, not the whole design) |
| String encoding | UTF-8, copy-at-edge | Engine wrapper's ANSI/`LPStr` convention (mangles non-ASCII; this is a new ABI with no legacy binds) |
| Transport | Contract is transport-agnostic; v1 transport = Native AOT shim | CLR hosting as v1 default (runtime dependency, slower startup); C++/CLI (MSVC-only — `CppProjectBuilder` drives clang/gcc/msvc directly and must stay toolchain-neutral) |

## The contract

### C1. Type-ownership registry

Every type name resolvable in a native project belongs to exactly one category:

| Category | Meaning | Examples |
|---|---|---|
| `NativeOwned` | Pure C++ implementation; never crosses as a handle | After P1: `DateTime`, `TimeSpan`, `Guid`, `StringBuilder`, `Decimal`, `SByte` |
| `ManagedOwned` | Lives in the GC heap; crosses only as a handle | `Regex`, `Stream`, `Uri`, `FileInfo`, `DirectoryInfo`, all user-assembly / NuGet types |
| `Bridged` | Value-converted at the edge; both sides have a native representation | `String`, numeric primitives, blittable structs |
| `Rejected` | Known to the registry, no permitted use in native projects; category-aware diagnostic | `Object` (has a legacy `void*` mapping, but erasure is unsound) |

**Single source of truth.** One registry class in `BasicLang` replaces the three
hand-synchronized sets that exist today: `CppCapabilityChecker.MappedTypeNames`,
`CppCapabilityChecker.UnmappedNetTypes`, and the key set of
`CppTypeMapper._typeMap` (whose must-stay-in-sync invariant is already
documented as a hazard in `CppCapabilityChecker.cs`). All three consumers —
capability checker, type mapper, code generator — read the registry; the P2
proxy generator becomes the fourth consumer. The registry is data plus small
query methods; it contains no codegen logic.

Two migration details the registry must preserve explicitly:

- **Every current `UnmappedNetTypes` entry gets a category.** That includes
  `DateTimeOffset`, which is in today's reject list but not in P1's headline
  six — P1's spec assigns its category (expected `NativeOwned` alongside
  `DateTime`); the migration is not complete until no reject-list entry is
  unaccounted for.
- **`Object` keeps its deliberate asymmetry**: it is a `_typeMap` key (mapped
  to `void*`) yet rejected by the capability checker because `void*` erasure
  is unsound. In registry terms it is category `Rejected` — the fourth
  category exists precisely so this case (and truly unknown types) needs no
  ad-hoc exception; the existing rationale comment moves into the registry.

**Collision rule.** When a `NativeOwned` type appears in a managed signature
(e.g. `System.DateTime` in a NuGet API), generated boundary code
**value-converts** at the edge. A managed handle to a `NativeOwned` type never
exists. Conversion pairs are part of each type's P1 definition.

**Diagnostics.** `CppCapabilityChecker` messages become category-aware:
- `ManagedOwned` type used without the project's .NET surface enabled → actionable "enable X / add reference Y" diagnostic (exact wording in P2).
- Truly unknown type → today's rejection, unchanged.

### C2. Handle model

A managed-object handle is a single 64-bit value packed as
`{index: low 32 bits, generation: high 32 bits}`.

- The managed side owns a table: `index → { GCHandle, generation, refcount }`.
  Access is thread-safe. Free slots are reused; **the generation increments
  when a slot is freed** — i.e. when its refcount reaches zero — not on every
  `blnet_release` decrement. The table grows without bound (amortized append);
  it is deliberately **not** a fixed-size table.
- Every table operation validates the caller's generation first. A stale
  handle (use-after-release, double-release) fails with
  `BLNET_E_STALE_HANDLE` — a clean, localized error at the call site, never
  memory corruption.
- The C++ side never touches raw handles in user-visible code. Generated code
  wraps every handle in `BasicLang::NetRef` — a `shared_ptr`-based RAII type
  whose deleter calls `blnet_release(handle)` — the same reference-semantics
  pattern as the existing `shared_ptr<BasicLang::List<T>>` collection layer.
- A freshly created handle has **refcount 1**, owned by the receiving
  `NetRef`. Handles are not raw-copyable. Duplicating a reference goes through
  `blnet_addref` (table refcount), so aliasing cannot bypass lifetime tracking.
  `NetRef` copy = `shared_ptr` copy (no ABI call); a *new independent* `NetRef`
  for the same object = `blnet_addref`.

**Overhead stance:** one array index + one integer compare per call, on calls
that already cross into managed code (orders of magnitude more expensive).
The boundary is for orchestration, not per-frame inner loops; hot paths stay
native.

### C3. Call ABI

- Every crossing is `extern "C"`, `__cdecl` — deliberately identical to the
  engine⇄wrapper convention so there is **one** ABI discipline in the repo,
  greppable the same way. Portability note: `__cdecl` is only meaningful on
  Windows x86; x64/ARM64 each have a single canonical convention. The C header
  therefore spells it through a macro (`BLNET_CALL`, empty on non-Windows /
  non-x86 toolchains so clang/gcc compile cleanly), and shim exports declare
  `[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]`.
- Every cross-call returns `int32_t` status (`BLNET_OK = 0`). Logical return
  values come back via out-parameters.
- **Blittable data** (numeric primitives, `Boolean` as `uint8_t`, structs of
  blittables) crosses by value.
- **Strings** cross as UTF-8, always copied at the edge. Two ownership rules,
  no exceptions:
  - **In-parameters are borrow-and-copy**: the caller retains ownership of its
    own buffer; the receiver copies at the edge and must not retain the
    pointer past the call.
  - **Buffers allocated for transfer** (out-strings, last-error strings) are
    owned by the receiver, who frees them via the contract's own `blnet_free`
    — never the CRT `free`, never keeping a pointer into the other side's
    memory. (Law derived from the rcore C8 bug: a returned pointer into
    marshaled input dangled across P/Invoke.)
- Everything else crosses as a handle (C2).
- Out-string parameters: callee allocates via the shim allocator, caller
  receives `char*` + owns it, frees via `blnet_free`.

### C4. Errors and exceptions

Exceptions never unwind across the ABI in either direction.

- **Managed → native:** every shim export wraps its body in a catch-all.
  A managed exception becomes `{ status = BLNET_E_MANAGED_EXCEPTION,
  exception_type_name, message }` (strings per C3, retrieved via a
  per-thread `blnet_last_error` accessor to keep signatures uniform).
  The same accessor serves **all** non-OK statuses on the current thread —
  including retrieving a `BLNET_E_NATIVE_EXCEPTION` message after an inline
  callback failure — so there is exactly one error-detail channel in the C
  header, not per-direction variants.
  Generated native code checks status and rethrows through the existing
  `IRThrow` machinery as BL `NetException` carrying the original .NET type
  name and message. Hand-written C++ checks status codes directly or uses the
  provided `BasicLang::NetCheck(status)` helper that throws the C++-side
  exception type.
- **Native → managed (inside callbacks):** the universal callback thunk (C5)
  wraps invocation in `try/catch`; a native exception becomes
  `BLNET_E_NATIVE_EXCEPTION` + message. For **inline** dispatch (same-thread
  or `Immediate`), the managed dispatcher rethrows it as a .NET
  `BasicLangNativeException`. For **queued** dispatch there is no invoker left
  to rethrow into: the failure is captured and surfaced on the pump thread.
  Precisely: `blnet_pump()` **continues draining** the queue on a failure,
  invokes the error hook registered via `blnet_set_error_hook` (if any)
  **once per failure** with the status and message, and returns the
  **first** failure's status (`BLNET_OK` if none). Queued-callback failures
  are never silently dropped.
- **The catch-all handler is the last line of defense and must itself be
  non-throwing.** Under Native AOT, an exception escaping an
  `[UnmanagedCallersOnly]` export is a process fail-fast. Per-thread
  last-error storage is preallocated or bounded; on allocation failure the
  handler degrades to status-code-only (type name/message unavailable, status
  still correct).
- **One status enum**, defined once in the registry source and emitted into
  both the generated C header and the C# shim — no dual maintenance.

### C5. Callbacks (managed → native), full duplex

The reverse direction mirrors C2 exactly:

- The **native** side owns a generation-tagged callback table:
  `index → { context (BL lambda + captures, or C++ fn ptr + user data), generation }`.
  Managed code receives a `{index, generation}` callback handle plus one
  **universal C thunk** — a single `extern "C"` entry:
  `int32_t blnet_invoke_callback(uint64_t callback_handle, const uint64_t* args, int32_t argc, uint64_t* result)`
  — so the AOT shim needs exactly **one** unmanaged-function-pointer signature
  total, independent of callback shape or instance. Each argument occupies one
  64-bit slot, encoded as follows:
  - **Blittable scalars** widen to 64 bits — integers zero/sign-extended per
    signedness, floating point as its raw bit pattern in the low bits.
  - **Blittable structs ≤ 8 bytes** are bit-copied into the slot (high bytes
    zero).
  - **Blittable structs > 8 bytes** cross as a pointer occupying one slot,
    borrowed for the duration of the invocation under C3's in-parameter rule
    — the callee copies, never retains the pointer.
  - **Strings** cross as UTF-8 `char*` per C3's ownership rules;
    **everything else** as a C2 handle.
  - **Out/ByRef parameters** occupy one slot holding a caller-provided
    pointer to their storage; the callee writes the value (encoded per this
    section) through it before returning. This is sound only because
    result-bearing callbacks are inline-only (see Callback shapes): the
    pointer is live for the duration of the call and, per C6, never retained
    past it.
  - **`result`** is one slot carrying the **return value proper**, encoded
    the same way; a struct return > 8 bytes goes through a caller-allocated
    buffer whose pointer occupies the result slot. `result` is `NULL` for
    void-returning callbacks even when out-parameters are present.
  - **Queued invocations copy at enqueue**: > 8-byte structs into queue-owned
    storage, strings per C3, and handle arguments are **addref'd at enqueue**
    — the queue owns a reference, transferred to the callee at execution, so
    a queued handle can never dangle; a handle already stale at enqueue fails
    the invocation immediately with `BLNET_E_STALE_HANDLE`. The pump frees
    queue-owned argument storage (string and struct copies, the queue's
    handle reference) after each invocation completes.

  No type tags are needed: a callback's shape is fixed at registration, and
  both sides' generated (or hand-written) code agree on the slot sequence at
  compile time. **This slot encoding is part of the ABI and is covered by
  C7's version number.** Native code releases a callback registration via
  `blnet_callback_release(callback_handle)`; the handle is valid until
  released (C6).
- Invoking a released callback fails with `BLNET_E_STALE_CALLBACK` —
  symmetric with C2, one diagnosis story in both directions.
- **Callback shapes.** Every callback is classified at registration:
  - **Notification**: `void` return, no out-parameters. May be dispatched
    inline or queued.
  - **Result-bearing**: any return value or out-parameter. May only be
    dispatched inline (same-thread, or cross-thread with `Immediate`).
    Cross-thread invocation of a result-bearing callback *not* marked
    `Immediate` fails immediately with `BLNET_E_CROSS_THREAD_RESULT`,
    surfaced managed-side as an exception naming the callback.
    **Blocking-queued dispatch is explicitly forbidden** — the invoking
    managed thread never waits on the pump thread, which eliminates the
    pump-self-wait deadlock (pump thread inside a synchronous managed call
    that transitively needs the pump) by construction.

**Threading rule (the sharp edge, decided):**

- A callback invoked on the **same thread** that entered managed code fires
  **inline** — synchronous delegate parameters (`Action<T>` passed to a
  method that invokes it before returning) just work.
- A **notification** callback invoked from **any other thread** (threadpool,
  async completions, event sources) is **queued, fire-and-forget** — the
  invoking managed thread does not observe completion. Native code drains the
  queue via `blnet_pump()`; game templates emit one pump call per frame in
  the main loop. This preserves full-duplex semantics without ever letting a
  foreign thread reenter engine/game state mid-frame. (Result-bearing
  callbacks are never queued — see Callback shapes above.)
- Per-callback opt-in flag `Immediate` for the rare subscriber that wants
  cross-thread inline dispatch and accepts reentrancy; the default is safe.
- `blnet_pump()` is callable from exactly one thread at a time (the thread the
  contract calls the *pump thread*); queued callbacks execute on it.
  Concurrent entry from a second thread is a **defined failure**,
  `BLNET_E_PUMP_REENTRY` — diagnosable error over corruption, per the
  contract's philosophy.

### C6. Threading and GC

- The handle table and callback table are thread-safe; any thread may call
  through the boundary.
- The GC may move objects freely: handles are table indirections, not pinned
  addresses. Nothing in the contract requires pinning; if a specific P2 API
  needs pinned memory (buffer spans), that API's proxy does the pinning
  internally and it never leaks into the contract.
- Managed code must not retain a native pointer beyond the call that received
  it, except a callback handle (C5), which is valid until released.

### C7. Versioning

- The shim exports `int32_t blnet_abi_version()`. Generated native startup
  code calls it before any other boundary call and fails fast with a clear
  diagnostic on mismatch. The version is a single constant in the registry
  source, emitted into both sides (same mechanism as the status enum) —
  the engine⇄wrapper drift lesson, made mechanical.

## Testing strategy

The contract is testable **before P2 exists**, via a minimal hand-written test
shim (a small C# project exercising the table + string + thunk rules) and a
native test harness, wired as `[Category("Integration")]` NUnit tests:

1. Handle round-trip: create → call → release.
2. Stale handle → `BLNET_E_STALE_HANDLE` (no crash, correct call site).
3. Double release → clean error; addref/release refcount correctness.
4. Generation reuse: release slot, reoccupy it, old handle still fails.
5. String round-trip with non-ASCII content; `blnet_free` ownership.
6. Managed exception → status + type name + message → rethrow.
7. Native exception inside an **inline** callback → managed
   `BasicLangNativeException`.
8. Same-thread callback fires inline; result-bearing inline callback returns
   its value through the thunk's result slot, and a callback with a return
   value **plus an out-parameter** writes both correctly (out-slot pointer
   writeback).
9. A callback taking a 16-byte blittable struct argument and returning a
   struct result round-trips bit-exact through the thunk (pointer-in-slot
   encoding both directions).
10. Cross-thread notification callback is queued and fires only on
    `blnet_pump()`; a handle argument addref'd at enqueue stays live even if
    the enqueuing side's reference is released before the pump runs.
11. Cross-thread invocation of a result-bearing, non-`Immediate` callback →
    `BLNET_E_CROSS_THREAD_RESULT` (immediate, managed-side exception; nothing
    queued).
12. Queued callbacks that throw natively → pump continues draining, error
    hook fires once per failure, `blnet_pump()` returns the first failure's
    status.
13. Deadlock guard: pump thread inside a synchronous managed call while a
    cross-thread notification is queued → no hang; defined ordering (the
    notification fires on the *next* pump).
14. ABI version mismatch → fail-fast diagnostic.
15. Concurrency: parallel create/release hammering the table (thread-safety).
16. Invoking a callback after `blnet_callback_release` →
    `BLNET_E_STALE_CALLBACK` (the C5 symmetry claim, exercised).

These conformance tests become the acceptance gate P2's real shim must pass
unchanged.

## Consumers and follow-on specs

| Consumer | Uses |
|---|---|
| P1 spec (native BCL types) | C1 registry (`NativeOwned` entries + conversion pairs) |
| P2 spec (AOT shim + proxies) | Everything; conformance suite is its acceptance gate |
| `CppCapabilityChecker` / `CppTypeMapper` / `CppCodeGenerator` | Read C1 registry instead of private lists |
| Hand-written C++ in mixed projects | `NetRef`, `NetCheck`, generated proxy headers (P2) |

## Open extensions (reserved, not designed)

- **Generics across the boundary** (`List(Of ManagedType)` etc.): v1 rejects
  with a category-aware diagnostic; slot reserved for a P2+ design
  (likely: closed instantiation set discovered at compile time).
- **Hosted-CLR transport** for reflection-heavy libraries AOT rejects:
  same contract, second transport; opt-in per project.
- **Async/Task bridging** to BL `Async`: queued-callback machinery (C5) is
  the intended substrate.
