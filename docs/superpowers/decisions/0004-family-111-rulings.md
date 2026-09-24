# ADR 0004: family #111 rulings — interface accessor batching, IsReplicable whitelist, temp namespace, and the AlgebraicSimplification gate

**Amended by [ADR-0005](0005-integer-division-cse-destination-interface-property-type.md)** — Invariant S widened to the named destination (S').

- **Date:** 2026-09-23
- **Status:** Accepted
- **Decided by:** architect role, dispatched on its pinned model (the `fable` alias in
  `.claude/agents/architect.md`) with no per-invocation override. This is the first ruling in this project that was **not**
  substituted — ADR-0001 through ADR-0003 were all Opus substitutions made after the
  pinned model returned HTTP 429; see ADR-0001's Provenance section for that
  arrangement.
- **Brief:** [`0004-brief.md`](0004-brief.md) — four batched questions from family
  #111, preserved in full beside this ADR. It was written by the brief role and
  corrected by the orchestrator before dispatch; the orchestrator's corrections
  (listed at the brief's foot) were: Q2's attribution of `ReadsCallVisible` to
  ADR-0001 was wrong and the value-stability point was missing; MSVC error codes
  were cited for a clang measurement; Q4's "correct answer ~2.0" was a guess; and
  option Q4-C was added.

## Questions

Four questions, batched from one consultation, each amending ADR-0001 and/or
ADR-0002:

- **Q1 (→ D1):** Ship ADR-0002's `HasGetter`/`HasSetter` flag fix batched with the
  C++/MSIL accessor defects it exposes, or defer the backend fixes?
- **Q2 (→ D2):** What is the `IsReplicable` whitelist default, and does value
  stability (no write to an operand between a multi-use value's definition and its
  last use) belong inside `IsReplicable` or elsewhere?
- **Q3 (→ D3):** Fix the IR-temp/user-local namespace collision with a C#-only
  uniquifier now and an IR-level rename later, or rename at the IR level now?
- **Q4 (→ D4):** Gate `AlgebraicSimplification`'s `2*x → x+x` rewrite on
  `IsReplicable` before or after fixing the MSIL binary-op coercion bug it exposes?

## D1: Ship ADR-0002's flag fix batched with C++/MSIL accessor fixes, or defer?

### Decision

Batch (Option B), ordered so every commit is green: land the C++ abstract-accessor
signature fix (`CppCodeGenerator.cs:1117-1119`) and the MSIL `virtual final newslot`
marking FIRST (both are no-ops while the flags are false), then the two-line flag fix
plus `IsReadOnly`/`IsWriteOnly` on `IRInterfaceProperty`. Interface-TYPED access
(`h->Slot`, `ldfld`) is explicitly out of this batch.

### Because

- A commit that turns working programs (class-typed access) broken on two backends
  has negative attribution value; ADR-0001's fence exists to protect diff
  attribution, and this edit is in a region the materialisation work never touches.
- Deferring leaves the IR carrying a lie (`HasGetter=false` for a declared getter)
  that every future consumer inherits.

### Contract

The C++ interface accessor declaration must be produced by the SAME helper that
produces the class accessor signature (one function, two call sites, differing only
in `= 0` / `override`). Test: interface `Property Slot As String`, implementing
class, access through a class-typed variable compiles and runs identically on all
five backends at every commit in the series. Interface-typed access gets a
known-failing test, not a fix.

### Rejected

- **Q1-A, defer:** knowingly regresses working shapes on two backends.
- **Fix interface-typed access in the same batch:** separate defect, separate
  attribution.

### Revisit if

The C++ signature helper cannot be unified without touching materialisation-adjacent
code.

### Amends

ADR-0001 (fence carve-out, this defect only) and ADR-0002 (batching + ordering).
Interface-typed access needs its own brief.

## D2: IsReplicable whitelist default and value stability

### Decision

Confirm the implementer's list (Option A): literals, `Me`, parameters, locals,
materialised temps, pure operators over replicable operands; never globals
(non-`Const`), `ByRef`, calls, method calls, property/indexer/field loads,
allocations, or any unrecognised node. Value stability is NOT `IsReplicable`'s job.
It is an IR invariant owned by whichever pass creates a multi-use value, enforced by
a verifier.

### Because

- A dataflow "no write between def and last use" check inside a structural predicate
  is a liveness analysis in disguise; a wrong answer there is silent miscompilation,
  the exact failure ADR-0001 forbids.
- Multi-use anonymous values come only from optimizer passes (CSE, `x+x`); user code
  never produces them. The pass that manufactures the sharing knows whether an
  operand is redefined; the backend does not.

### Contract

- `IsReplicable` is a separate predicate; it may NOT delegate to `ReadsCallVisible`
  (different question — kill vs replicate; coupling lets a future CSE tweak silently
  change codegen). Borrowing the boundary as a list is fine; calling the function is
  not.
- **Invariant S:** for any instruction with use count > 1, no variable reachable
  through its replicable operands is assigned between its definition and its last
  use. `x + x` satisfies S trivially (both uses in one instruction).
- A post-optimizer `IRVerifier` pass (debug/test builds, on by default in the suite)
  asserts S. Passes fix violations; the whitelist is never widened with dataflow.
- Captured locals: if the IR marks them, exclude from the whitelist; if not, OUT OF
  SCOPE for now.

**Measurement that changes this ruling:** run the verifier over the suite's IR at
HEAD. Zero hits confirms the `7154a88` upstream guarantee. Hits that cannot be fixed
at the pass → composite ops leave the whitelist (leaves-only replicable), forcing
materialisation of CSE'd binops.

### Rejected

- **Q2-B, locals non-replicable:** over-materialises with no correctness gain once S
  holds.
- **Q2-C, `[Pure]` attribute:** ADR-0001 already rejected; unchanged.
- **Dataflow inside `IsReplicable`:** wrong layer; silent-miscompile risk.
- **Delegating to `ReadsCallVisible`:** different question, hidden coupling.

### Revisit if

The verifier fires and the fix would require pass reordering.

### Amends

ADR-0001 (fills the silent cases; adds Invariant S and the verifier obligation). CSE
and `AlgebraicSimplificationPass` own S for what they create.

## Implementation note (D3)

D3 was implemented on master by `2d84743` ("Keep compiler temps from sharing a
name with the user's t1, t2, ..."), merged into this branch in `8a42796`. It
used a module-level reserved set, `IRTempNames.UserOwned`, consumed by
`IRBuilder.SeparateTempsFromUserNames` and `CodeGeneratorBase.NextTempName`,
rather than the ruling's `IRFunction.ReservedNames` / `IRVariable.IsTemp`
flag. Measured: T1 and T2 are correct on all four backends. D3's contract
(temps never share a user's name; existing output byte-identical when
nothing collides) is met; its mechanism differs.

## D3: Temp namespace — C# uniquifier now, or IR rename now?

### Decision

Option B, in the zero-churn form: reserve, don't rename. Temps stay `t{n}`; the mint
skips names the function declares, and temp-ness becomes a flag rather than a regex.

### Because

- ADR-0001's "namespace users cannot enter" is a compiler-wide property; fixing it
  per-backend leaves C++/MSIL measured-broken and invites three divergent copies.
- Reservation instead of renaming means fixtures without a colliding local are
  byte-identical — no observable C++ output churn, so the fence is not implicated.

### Contract

- `IRFunction.ReservedNames : ISet<string>` populated by `IRBuilder` from the AST
  (all params and locals in the body) BEFORE lowering; `GetNextTempName()` never
  returns a member.
- `IRVariable.IsTemp` set at mint; `IsTempDestination` (`IROptimizer.cs:115`) and
  `ConstantFolding`'s private copy consult the flag, not the name.
  `NamedAfterVariable` folds into this.
- `ICodeGenerator.cs:203` fallback honours the same reserved set (amends ADR-0001's
  "stays as-is" bullet).
- Test: locals named `t0..t3` compile and run correctly on all five backends;
  existing fixtures unchanged.

### Rejected

- **Q3-A, C# uniquifier:** fixes one of three measured-broken backends.
- **Full IR rename (e.g. `__t0`):** fixture churn across five backends for no gain
  over reservation.

### Revisit if

Any pass mints a temp before `ReservedNames` is populated.

### Amends

ADR-0001 (temp-namespace contract, the `:203` bullet).

## D4: Gate 2*x → x+x on IsReplicable — fix MSIL first, or ship the gate?

### Decision

Option C. Order: (1) backend materialisation + `IsReplicable` (D2), (2) MSIL
binary-op coercion, (3) the gate. Rule applied: no commit may move any shape from
right to wrong.

### Because

- Under ADR-0001's truth table, `Add(t0,t0)` with `t0` a call is use-count 2,
  non-replicable → declared local. Materialisation fixes the C# oracle by itself;
  the gate is defence in depth, exactly as ADR-0001 already classifies it.
- The UNMEASURED expectation is resolved by the ADR's own contract — measure it at
  step 1: `2 * Tag()` prints once on C#.
- MSIL coercion is backend-local and reversible; it neither blocks nor is blocked by
  the IR work.

### Contract

MSIL binary op emits a numeric conversion of each operand to the IR-assigned result
type before the opcode (`MSILBackend.cs:3948-3992`). Test: `2 * <Double call>` yields
the rounded product on MSIL under both pipelines; after step 3, the IR for that
program contains no use-count-2 call.

**Consistency with D2:** same predicate; the arm is S-safe by construction, so the
gate needs only replicability. Materialisation lands before the gate, so the oracle
is fixed by E1, not the gate — matching ADR-0001's classification of the gate as an
Obligation, not the fix.

### Rejected

- **Q4-A, MSIL-first:** sequences reversible work ahead of the oracle fix.
- **Q4-B, gate-first:** ships a right-to-wrong commit.

### Revisit if

Step 1 does not fix the C# double-print — then the gate is primary and moves to
step 2.

### Amends

ADR-0001 (obligation ordering). MSIL coercion is a bug fix, no ADR.

## Note: "all five backends" and current test coverage

D1's and D3's contracts require their tests to pass "on all five backends"
(D3's Rejected list also reasons about "five backends"). The test suite as it stands exercises four of the five (C#, C++,
JavaScript, MSIL) — LLVM is deprioritized by the project owner, so LLVM has no
coverage in this family's tests. This is a clarifying note on the ruling's text, not
a change to it: the ruling says five: the test suite covers four.
