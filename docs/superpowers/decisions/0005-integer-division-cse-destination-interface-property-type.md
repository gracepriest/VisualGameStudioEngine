# ADR 0005: integer division with floating operands, CSE's destination boundary, and interface property type resolution

**Amended by [ADR-0006](0006-kill-vocabulary-totality-dynamic-use-call-visibility.md)** — D2's Revisit clause replaced, S′'s use count and region made dynamic, and call-write coverage unified to one predicate.

- **Date:** 2026-09-24
- **Status:** Accepted
- **Decided by:** the architect role, dispatched on its pinned model with no
  per-invocation override.
- **Brief:** [`0005-brief.md`](0005-brief.md) — three batched questions. It was
  written by the brief role and corrected by the orchestrator before dispatch
  (corrections listed at its foot), with Q3 added by the orchestrator from the
  implementer's measurements.

## Questions

Three questions, batched from one consultation:

- **Q1 (→ D1):** `\` (integer division) with floating operands — convert each
  operand to Long (VB.NET semantics), truncate the quotient, or reject the
  program at compile time?
- **Q2 (→ D2):** Does ADR-0004 D2's Invariant S also forbid writes to a shared
  value's named *destination*, not just its operands?
- **Q3 (→ D3):** How is an interface property's TYPE resolved? (amends
  ADR-0002)

## D1: Integer division (`\`) with floating operands

### Decision

Option A: VB.NET semantics. Each floating operand is converted to `Long` with
round-half-to-even, then integer-divided (truncating toward zero). The
conversion is inserted ONCE, in the IR — `IRBuilder` emits the same conversion
node `CLng(x)` lowers to on each floating operand of `IntDiv` — so every
backend sees an integral `IntDiv` and never implements the rounding rule
itself.

### Because

- The language is VB-like by charter and the analyzer already encodes this
  rule (result type `Long`, comment at `SemanticAnalyzer.cs:8287`); Option B
  would make the analyzer's stated intent a lie.
- One IR conversion beats four backend copies of a banker's-rounding rule —
  the C++/JS/C# divergence measured at HEAD is exactly what per-backend
  copies produce.
- Option C is ruled out by the analyzer's own note: `(a / b) \ c` must stay
  legal.

### Contract

On every backend and every entry point (CLI, CLI `--optimize`, `.blproj`):
`7.5 \ 2` = 4, `7 \ 2.5` = 3, `8.5 \ 2` = 4 (8.5→8), `-7.5 \ 2` = -4 (-7.5→-8),
`-7 \ 2` = -3 (truncation, not floor). Post-conversion divide-by-zero
(`7 \ 0.4`) behaves exactly like integral `7 \ 0` on that backend. After
`IRBuilder`, no `IntDiv` node has a floating operand. Prerequisite test:
`CLng(7.5) = 8` and `CLng(8.5) = 8` on every backend.

### Obligations

Order (no commit right→wrong):

1. Per backend, fix `CLng` rounding and integral-operand `IntDiv` if either is
   wrong — these shapes already exist, so fixes are bug fixes only.
2. `ConstantFolding`'s `Convert` folding uses the same half-even rule.
3. ONE IR commit inserting the conversions: flips C++/JS/C# wrong→right;
   MSIL's D4 operand coercion becomes a Long→Long no-op (verify, don't
   assume).
4. Remove the now-dead quotient truncation in C++/JS — output churn, so the
   implementer decides whether it's inside the ADR-0001 fence.

Docs: `BasicLang-Reference.md` states the rule; the JS spec's
`Math.trunc(a/b)` line is superseded.

### Rejected

- **Q1-B, truncate:** contradicts the analyzer's stated rule and VB identity;
  keeps three divergent copies.
- **Q1-C, reject:** breaks `(a / b) \ c`, which the Double-`/` change made
  routine.
- **Per-backend conversion:** the rounding rule copied four times; that is
  the HEAD defect.

### Revisit if

The project owner declares BasicLang unbound from VB.NET numeric semantics,
or a shipped sample/test asserts truncation on fractional operands.

### Amends

None (new rule; the analyzer's stated intent was already this).

## D2: Does Invariant S also protect a shared value's named destination?

### Decision

Option A. S is widened to every variable a backend may read to obtain the
value: replicable operands (re-emission) AND the named destination
(materialised read). CSE kills on destination writes; the verifier checks
both.

### Because

- S must state what backends actually rely on; C++/JS/MSIL read the
  destination, so a verifier built on the narrower S certifies a miscompile —
  the exact failure D2 exists to prevent.
- Option B (always a fresh temp) hides the invariant instead of stating it
  and churns IR/fixtures for every CSE hit on all backends.
- Option C leaves the verifier blind to the measured defect class.

### Contract

S′: for any instruction `v` with use count > 1, no variable in `Guard(v)` is
assigned between `v`'s definition and its last use, where `Guard(v)` = vars
reachable through `v`'s replicable operands ∪ {`v`'s named destination, if
any}. "Assigned" uses the identical kill vocabulary `Invalidate` already
applies to operands (`IRAssignment` target, `IRStore` address, rename; if
ByRef passing is missing there, it is missing for both — flag, don't fix
silently).

CSE: `Candidate` records `Reads ∪ {Destination}`; `Invalidate` kills on
either. Verifier: asserts S′ — the destination check is unconditional on
replicability.

Tests: the brief's program prints `[3, 0]` on all four tested backends, both
pipelines; a hand-built IR with the destination reassigned between two uses
fails the verifier; `Dim a = p+q : l(0) = p+q` (no reassignment) still merges
to one binop. C#'s `IsNamedDestination`/`ComputeMaterialisedTemps` are
unchanged: under S′, reading `a` and re-emitting `p+q` are both correct, so
the accidental-correctness disappears without touching the backend.

### Rejected

- **Q2-B, fresh temp always:** hides the invariant, churns every CSE fixture,
  leaves S still narrower than what backends read.
- **Q2-C, CSE-only fix:** verifier blind to the measured defect class.

### Revisit if

The verifier fires on a destination write that CSE cannot see (e.g. a write
through an alias) — then Option B becomes the primary fix.

### Amends

ADR-0004 D2. Invariant S widened to S′ (destination added to `Guard(v)`).

## D3: How is an interface property's TYPE resolved?

### Decision

Option A as implemented: plain named types resolve through the analyzer's
type lookup; the stand-in survives only for forms the parser cannot yet
produce in an interface, and that fallback must fail loudly (assert in test
builds), never silently emit class-kinded types.

### Because

- The stand-in is the IR carrying a lie (every type class-kinded), the same
  defect D1 refused to defer; the measurement shows zero right→wrong cells.
- Option C fixes the symptom on one backend and leaves the IR wrong for every
  future consumer.
- Option B is untestable today; Option A with a loud fallback reaches
  Option B's end-state the day the parser grows.

### Contract

`IRInterfaceProperty.Type` is structurally equal to the implementing class
property's `Type` for the same declared text — test on Integer, Double,
Boolean, String, user Structure, user Enum.

ASSUMPTION: a type parameter `T` in `IFoo(Of T)` resolves through the
analyzer as a type parameter, not the stand-in — add `Property Value As T` to
the C++ test; if it hits the stand-in, it is in scope for this change.

NOTE (clarifying, not a ruling change): this assumption cannot be tested
today. Generic interfaces don't parse at all (`'(' is not valid inside an
Interface`, measured 2026-09-24) — the same parser gap as array-typed
interface properties. The `Property Value As T` test therefore falls under
the Obligation below, on whoever teaches the parser generic interfaces.

### Obligations

The type-resolution commit lands BEFORE the D1 flag-fix commit (it is
unobservable on C++ while flags are false; flags-true-with-stand-in is the
measured break). Whoever teaches the parser interface array/generic
properties owns removing the stand-in in that same change. MSIL
Structure-as-class, JS no-Structures, `Color.Blue` as Object are pre-existing
controls — separate briefs.

### Rejected

- **Q3-B, resolve everything now:** unparseable, untestable.
- **Q3-C, by-value C++ declarations over the stand-in:** backend-local patch
  on an IR lie.

### Revisit if

A plain named type resolves differently through `InterfacePropertyType` than
through the class-property path (proves a parallel resolver, which this
ruling forbids).

### Amends

ADR-0002 (type resolution, not the accessor-flags ruling); also amends D1's
ordering (the type-resolution commit precedes the D1 flag-fix commit — see
Obligations).
