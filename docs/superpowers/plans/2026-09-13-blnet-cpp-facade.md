# blnet C++ facade — an ergonomic header over the generated proxy slots

**Status:** Tasks 1-4 implemented; Task 5 open
**Date:** 2026-09-13
**Builds on:** `2026-07-29-p2a-dotnet-access-aot-shim-design.md` (P2a, Implemented) — §7.3
mangling, §8.3 wire forms, §9.1 generated artifacts.

## 1. The problem

C++ reaches .NET today by naming a mangled slot verbatim:

```cpp
BasicLang::net::bl_net_System_Text_RegularExpressions_Regex_Escape__System_String_095f897ff422192f("1+1");
```

That name is correct and has to be — §7.3 folds the declaring type, member, static-ness,
generic arity and per-parameter ref-kind into one flat C identifier because the export lives in
a flat symbol namespace with no overloading, and 37 public framework types contain member pairs
that collide without it. None of that is negotiable.

But it is a **terrible authoring surface**, and worse, a *fragile* one: the trailing hash changes
whenever the signature does, so hand-written C++ naming slots breaks silently on an unrelated
edit. The measured advice today is "read the name out of `obj/gen/blnet_proxies.g.hpp` after each
build", which is not something a person should have to do.

## 2. The shape

A second generated header, `blnet_facade.g.hpp`, rendering the same slots as ordinary C++:

```cpp
#include "blnet_facade.g.hpp"
using namespace BasicLang::netfx;

System::Console::WriteLine("hello");
auto r = System::Text::RegularExpressions::Regex("^\\d+$");
bool ok = r.IsMatch("123");
```

**It is a second rendering of the same `SlotPlan` list `NetProxyEmitter` already computes.** That
is the load-bearing design choice: the facade cannot disagree with the proxy table about a
signature, because both are projections of one plan. Only COVERAGE can drift, and §6 pins that.

## 3. Decisions

**D1 — Root namespace `BasicLang::netfx`, never a bare `namespace System`.**
Emitting `namespace System` at global scope would collide with any user type or namespace of
that name, in a header the user did not write and cannot edit. The root keeps it inert;
`using namespace BasicLang::netfx;` is one line and opt-in. A generated header must never make a
program that compiled stop compiling.

**D2 — One `struct` per .NET type, namespaces mirroring the .NET namespace.**
`System.Text.RegularExpressions.Regex` → `namespace System::Text::RegularExpressions { struct
Regex … }`. `struct` because members default public and there is no invariant to protect on a
static-only type; the types that DO hold a handle keep it private (see D4).

**D3 — Static members become `static` member functions; instance members become ordinary member
functions.** This is the whole ergonomic win: the receiver stops being an explicit first
argument.

**D4 — A type with any instance member holds its `NetRef` privately**, exposing `raw()`.

> ⚠ **Correction, made while implementing Task 2.** The original rationale here read "a public
> handle lets a caller copy it out and release it independently of the refcounting, which is a
> use-after-free with extra steps." **That is not true of this `NetRef`.** It holds a
> `std::shared_ptr<void>` whose deleter calls `g_netref_release`, so *copying* a `NetRef` shares
> ownership and the release runs exactly once, when the last copy dies. Copying is safe.
>
> The real hazard is rebuilding one from a raw handle: `NetRef(x.raw().get())` opens a **second
> control block** over the same managed object, and both will release it. `NetRef::Duplicate`
> exists precisely to do that correctly (checked addref first). Note that privacy does not
> prevent this either, since `NetRef::get()` is public — so this is not what privacy buys.
>
> What privacy actually buys is narrower and still worth having: the wrapper cannot be **rebound**
> to a different object behind its own back, so a `Regex` always wraps the handle it was built
> with. The generated header says this accurately rather than repeating the original claim.

**D5 — Constructors become real C++ constructors.** `Regex r("^a+$")` rather than a factory.
A type with a constructor slot but no default .NET constructor gets no default constructor.

*Resolved in Task 3 — the interaction Task 2 flagged.* The handle-adopting constructor now takes a
**tag**: `T(adopt_handle, h)` adopts, `T(...)` constructs. Task 2's plain `explicit T(NetRef)` could
not survive D5, because a .NET constructor whose single argument is a handle-typed value of a type
outside the surface renders as `T(const NetRef&)`, and an overload set containing both is ambiguous
for *every* call. `StreamReader(Stream)` is exactly that shape, so this was reachable rather than
theoretical. No .NET type maps to `adopt_handle_t`, so D5 owns the ordinary constructor space
outright.

**D6 — Properties become `get_X()` / `set_X()`, not operators or magic.**
Boring and explicit. An `operator=` overload that performs a cross-boundary call is a trap: it
looks like assignment and costs a shim round trip. `IsSettable == false` emits only the getter.

> ⚠ **Measured while implementing Task 3: `set_X()` will usually be ABSENT, and not because of
> `IsSettable`.** A `<NetProxy>` declared type draws only property READ slots. A `set_X` descriptor
> is *synthesized* (`NetSyntheticKind.Setter`) only where a BasicLang program actually writes the
> member, so a declared surface has none — a real build over `System.Console` and `Regex` produced
> **zero** `set_` slots. The facade can only render slots that exist; it cannot invent an export.
> So C++ can read `Console.ForegroundColor` through the facade but cannot write it unless some
> BasicLang code in the same project writes it too.
>
> This is a property of the SURFACE, not of the facade, and fixing it means making the collector
> emit setter slots for declared settable members — a §7.2 surface question, deliberately not
> decided here.

**D7 — A handle-typed parameter or return is the WRAPPER type when that type is in the surface,
and raw `NetRef` otherwise.** The surface is the whole world the facade can name; a handle to a
type nobody declared has no wrapper to be.

*Refined in Task 2:* "in the surface" is too loose — the precondition is **having a wrapper with a
handle**, i.e. having at least one rendered instance member (D4). A static-only type like
`System.Console` is in the surface and must still NOT be offered as a parameter type: a `Console`
value would be a constructible object with no identity.

**D8 — Two slots that render to the SAME C++ signature are BOTH omitted, with a BL6027 warning
naming them.** Never silently pick one. This is reachable: §8.3 maps distinct .NET types onto one
wire form (every handle-represented type is `NetRef`), so `F(Regex)` and `F(Uri)` are one C++
signature. Omitting both keeps the mangled slots as the escape hatch; picking one would make
`F(someUri)` silently call the `Regex` overload.

**D10 — The header is emitted in three phases: forward declarations, type declarations, then
out-of-line `inline` definitions.** *(Added in Task 2.)* Not a style choice. D7 lets one facade
type appear in another's signature, and .NET name order says nothing about which must come first —
`Fac.Probe.Api` sorts before `Fac.Probe.Counter` and returns one. A member body needs its parameter
and return types COMPLETE, which a forward declaration is not, so bodies cannot live inside the
struct as they did in Task 1 while every signature was still a scalar. Proven by mutation: removing
the forward declarations fails the compile with *"no type named 'Counter' in namespace
BasicLang::netfx::Fac::Probe"*.

**D9 — The header is always emitted and never auto-included.** Cost is zero when unused (inline
functions, no ODR presence), and unconditional emission keeps the drift test simple — there is no
"was it on?" axis.

## 4. What v1 does NOT do

- No operator overloading, no implicit conversions, no `ToString`.
- No generic types beyond what the surface already admits (§8.3 rejects open type parameters).
- No delegate/callback ergonomics — §8.4's `CallbackRef` stays as it is.
- No IntelliSense/doc-comment generation.
- **No detection of a TYPE whose name equals a NAMESPACE segment.** D8 covers two slots sharing
  one C++ *signature*; it does not cover a .NET type named `A.B` coexisting with a namespace
  `A.B`, which would emit both `struct B` and `namespace B` into the same enclosing scope — a
  redeclaration error in the generated header. Not reachable on today's surface (`System.Console`
  is a type and there is no `System.Console` namespace), and deliberately left alone rather than
  guessed at: the fix belongs with Task 4's collision machinery, which is where the reporting
  path (BL6027) already lives. The same applies to two distinct types that sanitize to one
  identifier — e.g. a nested `A.B+C` flattening to `A.B_C` alongside a real type of that name.

## 5. Tasks

- [x] **Task 1 — `NetFacadeEmitter`, static methods only.** *Done.* Rendered as
  `NetProxyEmitter.Facade.cs` (a `partial` of the existing emitter, not a new class — see the
  note below on why). Five tests in `NetFacadeEmitterTests`: the set-identity coverage
  invariant, the three rendered shapes, D8's both-omitted rule, D1's namespace containment, and
  a compile-AND-RUN oracle.

  The compile oracle went further than planned: rather than only compiling, it RUNS against
  `NetStubHarness`'s stub table, because compiling proves less than it appears to. Proven by two
  discriminating mutations — dropping the `return` keyword (caught only by the compile/run test,
  the four text tests stayed green), and forwarding a constant instead of the caller's argument,
  which **compiles cleanly** and is caught only by the run assertion (`0` instead of `210`).
  The stub multiplies by ten rather than returning its argument, so an identity-preserving
  facade bug cannot pass by accident.
- [x] **Task 2 — instance members and the handle.** *Done.* Private `NetRef` + `raw()` on any
  type with a rendered instance member, instance members as ordinary `const` member functions with
  the receiver supplied from the handle, and D7's wrapper-typed parameters and returns in both
  directions.

  Four new tests, each proven by a discriminating mutation (dropping the receiver, making the
  handle public, disabling D7, and removing the forward declarations); every mutation was killed
  by its own test and none by an unrelated one. The run oracle was extended so `Bump`'s stubbed
  result depends on BOTH the receiver and the argument — dropping the receiver and shifting the
  arguments left therefore changes the number rather than staying plausible.

  Verified beyond the probe: a real build over `System.Console`, `System.Text.RegularExpressions.Regex`
  and `System.Object` emits a 610-line facade that compiles clean, instance calls included.
- [x] **Task 3 — constructors (D5) and properties (D6).** *Done.* Real C++ constructors
  initialising the handle from the constructor slot; `get_X()` for properties and fields; the
  adopting constructor moved behind an `adopt_handle_t` tag (see D5) so it can never join a real
  constructor's overload set.

  **Every member CATEGORY now renders** — the `ClassifyForFacade` gate on `Kind != Method` is gone,
  and what remains skippable is shape only (multi-slot result or argument, ByRef parameter).

  One correctness fix the change forced: D8's collision key moved onto the RENDERED name. D6 makes
  a property `X` render as `get_X`, so it can now collide with a method literally named `get_X` —
  two different .NET names, one C++ name — which keying on `Member.Name` would have let through as
  two overloads of one signature.

  Four new tests, each proven by a discriminating mutation (a constructor that runs its slot and
  discards the handle, an untagged adopting constructor, no `get_` prefix, and restoring the
  category skip). The run oracle covers both new paths; the discarding-constructor mutation reads
  back `CTOR:0 / VAL:0` rather than anything plausible.

  Verified past the probe: a real build over `System.Console`, `Regex` and `System.Object` emits a
  788-line facade that compiles with `Regex r("^\\d+$")` — the plan's own §2 example — plus
  `Console::get_BufferHeight()` and tagged adoption.
- [x] **Task 4 — D8's collision rule + BL6027.** *Done.* BL6027 is a WARNING, raised from
  `NetProxyEmitter.FacadeDiagnostics` and merged by `CppProjectBuilder.EmitCore` through the same
  channel BL6022/6023/6026 use. Never an error: the proxy table is complete and every colliding
  member stays callable under its mangled name, so the build is correct and merely less ergonomic
  — failing it would let a convenience header stop a working project from building.

  The two NAME collisions from §4 are now detected AND acted on, because unlike a signature
  collision (whose damage is a wrong binding) these emit a header that does not compile:
  two types sanitizing to one identifier are both dropped; a type whose name is also a namespace
  segment is dropped and the namespace wins, since other types live inside it.

  **A latent inconsistency this closed.** `FacadeRendered` used to report every shape-renderable
  slot, including ones `EmitFacade` then dropped for colliding — so §6's set identity was being
  satisfied by a "rendered" set that overstated what the header actually contained. Classification
  is now ONE pass (`LayOutFacade`) whose order runs one way only: shape → type-name collisions →
  handle types → signature collisions. Handles are computed after the type drops on purpose, or a
  D7 signature could name a type the header no longer defines.

  Five new tests, each proven by a discriminating mutation: disabling type-name collision
  detection, making BL6027 an error, returning no diagnostics, counting collided slots as
  rendered, and removing the builder wiring entirely. The last one matters most — the emitter's own
  test proves the findings are computed, and nothing there proves the builder ever asks.

  Measured: the real `System.Console` + `Regex` surface has **zero** collisions, because D7's
  wrapper types already disambiguate most handle overloads and the rest differ in arity. So BL6027
  is expected to be rare in practice, which is exactly why it needed a test that constructs the
  collision rather than hoping to find one.
- [ ] **Task 5 — the coverage drift test (§6) and wiring into `NetProxyEmitter.Emit`.**

### Task 1 — two findings worth carrying into Tasks 2-5

**The facade is a `partial` of `NetProxyEmitter`, not a separate `NetFacadeEmitter`.** The plan's
own load-bearing claim is that the facade is a second rendering of the SAME `SlotPlan` list. A
separate class would have needed `Plan(surface)` made public, which is precisely the seam through
which the two could start disagreeing. Sharing the private plan keeps §2's guarantee structural
instead of conventional.

**Adding an artifact has THREE consumers, not one.** `blnet_facade.g.hpp` made six drift tests go
red, and one of them was a genuine bug rather than a stale expectation:
`CppProjectBuilder.CleanGeneratedDir` filters on the suffixes `.g.cpp` and `.g.h` plus a list of
exact names — and **`.g.hpp` does not end in `.g.h`**, so a stale facade would have survived a
project that stopped using .NET and stayed on the include path, the exact hazard that exact-name
list exists to prevent. The three consumers to update together:

1. `NetProxyEmitterTests.ExpectedArtifacts` — the §9.1 set.
2. `CppProjectBuilder.NetArtifactFileNames` — the clean filter (the one that was a real bug).
3. `NetBuildPipelineTests`' two merged-set expectations.

`TranslationUnitFileNames` needed NO change, and that is the right answer: the facade is a header
and must never be compiled as a TU.

## 6. The invariant that keeps it honest

**Every `SlotPlan` is either rendered in the facade or on a skip list that states why.**

Asserted as a set difference over the slot names, not a count — a count passes while one slot
swaps for another. The skip list is data, so a newly-unrenderable slot fails the test rather
than quietly vanishing from the facade, which is the §7.2 lesson: an omission nobody is told
about leaves the surface quietly meaning less than it says.

⚠ **The trap to avoid, from P2a-2's own history.** Do not assert "the facade compiles" and call
it covered — an empty facade compiles perfectly. Coverage must be the set identity, and the
compile test is the SECOND oracle, for shape.
