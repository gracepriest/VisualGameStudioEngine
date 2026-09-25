# ADR 0006: kill-vocabulary totality, dynamic-use S′ regions, and call visibility

**Amended by [ADR-0007](0007-bare-name-property-lowering-fidelity.md)** — D1's Revisit clause replaced (lowering fidelity, invariant F, beside V).

- **Date:** 2026-09-24
- **Status:** Accepted
- **Decided by:** the architect role, dispatched on its pinned model with no
  per-invocation override.
- **Brief:** [`0006-brief.md`](0006-brief.md) — three batched questions on
  the optimizer's shared kill vocabulary. It was written by the brief role
  and corrected by the orchestrator before dispatch, with corrections
  listed at its foot.

## Questions

Three questions, batched from one consultation:

- **Q1 (→ D1):** Writes the vocabulary does not name — should the verifier
  keep sharing CSE's kill vocabulary, or get an independent one?
- **Q2 (→ D2):** Should S′ (ADR-0005 D2) also cover a value used inside a
  loop that does not contain its definition?
- **Q3 (→ D3):** Which rule decides whether a call can write a value's
  operand — the flag rule, or the declarations rule already used for
  destinations?

## D1: Writes the vocabulary does not name; does the verifier keep sharing it?

### Decision

Option B in effect, achieved with ONE vocabulary, not two. `NamesWrittenBy`
becomes *total*: it returns `Named(set)`, `None`, or `Universal`, and every
IR instruction kind is explicitly on one list. Unlisted kinds return
`Universal` (passes kill everything: weak, never wrong), and a new
verifier invariant V — "no reachable instruction kind is unclassified" —
fires on them in test builds. Extensions per Option A: `IRFieldStore`
names its member; a write through a `ByRef` parameter names every escaping
name (all `ByRef` params, non-`Const` globals, fields); `IRBaseMethodCall`
is a call; interim closure rule: in a function that contains a lambda,
every local is call-visible, until #122's capture set narrows "every
local" to "captured locals".

### Because

- A second, coarser verifier model either drifts from the pass model or
  false-positives on writes to distinct globals/fields. The closed-world
  default gives the verifier independence exactly where the shared model
  is blind (unknown kinds), at zero precision cost.
- Alias gaps inside a classified kind (A2b) are not structurally
  detectable by any verifier; they are fixed by the alias rule and caught
  by execution probes on the non-C# backends. The ruling says so rather
  than pretending the verifier covers them.
- The interim closure rule is sound today and #122 only narrows it, so
  #122 never blocks.

### Contract

A2b `3,0`, A5b `3,0`, A6 `12,3` on all four backends, all three entry
points (C# is the control). A1 prints `3,0` on JavaScript; C#'s `a` defect
(empty lambda body) stays separately tracked (task #136).

NOTE (clarifying, not a ruling change): the ruling's own wording for this
cell is "A1 `l(0)` = 0 on JavaScript" — that names the wrong, currently
measured value, not the target. The probe's expected output is `3,0`
(l(0) = 3, a = 0); JavaScript measures `0,0` today, i.e. l(0) is the half
this decision fixes. Transcribed above as the corrected target. C#'s `a`
column is task #136 (the emitted lambda is `() => { ; }`), a separate
defect this contract does not cover.

Build the `IRBaseMethodCall` probe (base method writes a field the caller
reads bare) and require the correct value. Hand-built IR with an
unregistered node kind fails V. Const-global exemption unchanged: the
corpus pins as they stand at `6168628` are preserved; `OverKillWitness`
survives.

NOTE (clarifying, not a ruling change): the ruling states this as "the 11
sample-game merges pinned by count." That figure is a code comment
(IROptimizer.cs:1647-1651) that predates commit 8b17c47, which
deliberately gave up one SpaceShooter merge (ADR-0001 / ADR-0004 D2). The
count pinned at `6168628` is 10: `Corpus_Platformer_Makes6Merges` and
`Corpus_SpaceShooter_Makes4Merges` in
`VisualGameStudio.Tests/Compiler/CseInvalidationAndKeyTests.cs`. The brief
carried the stale 11 forward uncorrected; the contract above states it as
the corpus pins as they stand, not a specific count.

### Obligations

Lands after D3 (the closure rule lives in the predicate D3 unifies).
#122: replace "every local" with the capture set, a pure precision gain.
JavaScript `Me.K` ReferenceError, MSIL `Action` undefined, C# `{ ; }`
lambda: three separate briefs, not this one.

### Rejected

- **B-literal, two models:** drift, or false positives on distinct fields.
- **C, no names:** loses field-precise merges, answers nothing about the
  verifier.
- **A alone:** verifier stays tautological.

### Revisit if

V is quiet and a probe on a *classified* kind prints a stale value. That
means the vocabulary is wrong, not incomplete, and the verifier then needs
a real may-alias model.

Replaced by ADR-0007 (its first settled point): F takes the fidelity arm, V
the completeness arm.

### Amends

ADR-0005 D2. Its Revisit clause is replaced by the one above. "Assigned
uses CSE's vocabulary" stands, with the vocabulary now total and
completeness-checked.

## Implementation note (D1)

- Five implementer choices sit beyond the ruling's own letter, each RATIFIED
  by the orchestrator, each ONLY ADDING kills (none loosens an existing
  one), and each costing 0 merges or hoists across the suite:
  - the CONVERSE of rule (b) — a write to storage a `ByRef` parameter may
    ALIAS also names every such parameter, not only the reverse direction
    the ruling states. MEASURED fix: A2b/A2c/A2f/A2m (`n = p + q : G = 0 :
    l(0) = p + q`, aliased through a global/field/`Me.`field/a second
    `ByRef` parameter, called as `Work(v, v)` or `Work(G, …)`).
  - the closure rule's BY-VALUE-PARAMETER reading — a parameter is a local
    of the frame too, so a lambda may capture it the same way a `Dim`'d
    local can. MEASURED fix: A1p (a `Sub`'s own by-value parameter,
    captured and written by a lambda the `Sub` then calls). A1/A1o
    exercise the ruling's own LOCAL-variable letter (a `Dim`'d local), not
    this addition.
  - `IRBaseMethodCall` naming its VARIABLE ARGUMENTS, on the theory a base
    constructor/method may take one `ByRef` — the node records no `ByRef`
    flags at all, unlike an ordinary call. MEASURED fix: B2
    (`MyBase.SetIt(p)`). B1/B1r/B1L exercise the ruling's OWN "is a call"
    text (no variable argument to name), not this addition.
  - the 6a/6b/6c classification audit, closing kinds the ruling's own text
    never named at all: `For Each`'s loop variable and its call status,
    `Catch` variables, `Select Case` pattern bindings and a `When` guard's
    call status, `++`/`--`'s extra operand, `IRInlineCode` as Universal,
    an alloca's `_addr`-suffixed store also naming the stripped local, an
    element store escaping to a `ByRef` parameter, a constructor's
    variable arguments (6a); `IRAwait`/`IRYield` as calls (6b);
    `IRFieldAccess`/`IRFieldStore` ALSO acting as calls (not only naming
    their member — the member a Property Setter/Getter actually touches
    may be a DIFFERENT name than the one the store/access node itself
    names), and a resolved indexer accessor as a call (6c). MEASURED fix:
    P1/P3 (a Property Setter/Getter that writes a DIFFERENTLY-named
    backing field than the one `Me.P`/`Me.Tick` itself names — the naming
    half alone, already in the ruling's letter, cannot see this; only the
    call half can); Y1 (`Yield` inside an `Iterator`); IN_javascript/
    IN_cpp (an inline-code block); W1L (a `Select Case` `When` guard
    inside a loop, for LICM).
  - the CSE self-exemption (`Candidate.ReadsCallVisibleStorage` split into
    `OperandsCallVisible`/`DestinationCallVisible` so the defining
    instruction is exempt on its OWN destination without also exempting
    its operands) — a structural precision fix, not a measured-wrong-value
    one: `n = p + q : l(0) = p + q` (`n` `ByRef`) still makes exactly 1
    merge (`KillVocabularyPrecisionPinTests.SelfExemption_NByRef_
    StillMakesOneMerge`).
- Measured across the whole suite, steps 1-7: of 139 CSE merges and LICM
  hoists, 13 are lost — every one of the 13 is a shape this ADR pins as a
  now-CORRECT value (the family task #133/#122 used to pin known-wrong);
  zero are lost anywhere else. Zero Invariant V or S′ fires anywhere in the
  suite. Summed over the per-step probe matrices (`S/adr6-d1/probes/
  matrix-step*.txt`, each step measured against the one before), 102 cells
  flip from a measured wrong answer to the correct one, and zero flip the
  other way at any step. A single base-vs-final diff counts fewer (82–96,
  depending on the base file), because later steps added probes (B1, P1,
  Y1, the inline-code probes, …) that have no base cell.
- The L5 attribution ADR-0005 D2's own NOTE left UNVERIFIED is now settled:
  C++'s L5 failure is the C++ BACKEND's own lambda lowering (task #140) —
  MEASURED present even with NO optimizer pass running at all (`bump =
  [=]() { int32_t t0 = {}; t0 = x + 1; return; };`, a capture BY COPY where
  BasicLang's semantics need capture by reference) — not a kill-vocabulary
  or LICM defect this ADR could ever have closed. D2's L5 contract
  therefore applies to JavaScript, per that NOTE's own contingency:
  JavaScript now prints `12` under `--optimize` and in a Release
  `.blproj` build, closed by this ADR's interim closure rule.
- This ADR's own Revisit trigger is MET, not closed here. A property used
  by its BARE name inside its own class (`P = 10` lowers to a plain
  `IRAssignment` that happens to run a Setter; `t = Tick` lowers to a plain
  `IRVariable` read that happens to run a Getter) runs user code behind a
  CLASSIFIED kind — `IRAssignment`/`IRVariable`, not `IRFieldStore`/
  `IRFieldAccess` — and V stays quiet while JavaScript and MSIL print a
  stale value. The `Me.`-qualified form of the SAME property (P1/P3,
  above) IS correct, because IRBuilder lowers that form to `IRFieldStore`/
  `IRFieldAccess`, which ARE classified as calls. The same is true of a
  user-defined `Operator`/`CType` applied to class operands. Closing
  either needs a declaration the IR node does not carry (which bare name
  is a property; which operand types overload an operator) — an open
  question for the architect, not a per-kind answer this vocabulary can
  give. Escalated as task #147 for a new ruling; NOT fixed here, per this
  ADR's own Contract (D1 fixes the CLASSIFIED-kind gaps, not this one).
- Separate defects found and tracked while measuring the above, each
  UNRELATED to the kill vocabulary and left exactly as found: task #139
  (the C# backend drops a STATEMENT-level `MyBase` call entirely — B1,
  B1L; B1r's `Dim r = MyBase.Bump()` form is unaffected since it is not a
  bare statement); task #140 (C++'s lambda capture-by-copy, above); task
  #141 (C++ cannot build a BasicLang `Property` at all — P1's "no member
  named 'P' in 'Box'"; P3's "cannot assign to non-static data member
  within const member function"); task #142 (MSIL RUN-FAILs a virtual
  `MyBase` call to an INHERITED method — B2 — with
  `MissingMethodException`, unrelated to the `ByRef` argument B2 exists to
  test); task #143 (C++ does not lower a `Select Case` `When` guard's
  pattern/tuple shape under the aggressive pipeline — W1L's `t5`
  undeclared-identifier error); task #144 (MSIL has no IL lowering for a
  `When` guard node that is itself an `IRCall` — the same W1L shape); task
  #145 (neither MSIL nor JavaScript fully lowers a closure/iterator shape
  outside C#: MSIL has no lowering for the delegate type a `Sub()` lambda
  gets typed as — `Action` undefined, A1/A1o/A1p/L5 — nor for
  `IEnumerable` — Y1's `Iterator Function`; JavaScript's own `Iterator`/
  `Yield` lowering rejects Y1's shape with a strict-mode `SyntaxError`);
  task #146, per the orchestrator's own label — `CopyPropagationPass`
  keeps its OWN kill rules and is not a consumer of `NamesWrittenBy` at
  all: a copy fact for a field survives a call that writes the field,
  MEASURED wrong on all four backends including C#, out of this ADR's
  scope.
- `IRVerifier.CheckInvariantV`'s "reachable" is implemented as EVERY
  instruction of every block a function's `Blocks` collection holds — a
  SUPERSET of what a pass can actually reach at runtime (it does not
  exclude a block no predecessor ever branches to), matching how
  `CheckInvariantSPrime` already treats "reachable" for the same reason:
  a pass does not skip an unreachable block either, so neither does the
  verifier.
- MUTATION-TESTED: every arm this note's first bullet lists, the ByRef
  aliasing rule and its converse, the closure rule's parameter half, the
  Universal short-circuit in CSE/LICM/the verifier, `CheckInvariantV`
  itself, and the CSE self-exemption — 22 mutants, each removed singly,
  rebuilt, and run against the fast unit-level `KillVocabulary*`/`Cse*`/
  `Licm*`/`CallVisibility*`/`IRVerifier*` fixtures; ALL 22 were KILLED, 0
  survived.

## D2: Should S′ cover a value used in a loop that does not contain its definition?

### Decision

Option A, defined precisely: a use counts as repeated when it lies in a
loop that does not contain the definition, and `region(v)` is every
instruction on any path from the definition to any use, including the
full body of such a loop (the back-edge). One invariant; no separate LICM
postcondition.

### Because

- Option B is the same check restricted to LICM; a CSE record carried from
  before a loop into it has the identical hazard, and B would miss it.
- A linear "between def and last use" misses a write textually after the
  use but dynamically before the next iteration; the region definition is
  the actual fix whichever option is named.
- "≥ 1 use" (the mutant) is the wrong widening: it fires on correct L1.

### Contract

L1-L4, L6, L7 unchanged and verifier-quiet under CLI `--optimize`; L5
correct (via D1's closure rule) and verifier-quiet; hand-built IR with a
definition in a preheader and a Guard-name write placed after the in-loop
use fails the verifier. Because a C++ Release `.blproj` runs the standard
pipeline (#134), these probes are exercised only through CLI `--optimize`;
the verifier must run after the aggressive pipeline in test builds.

NOTE (clarifying, not a ruling change): L5 (a lambda-captured local
written in the loop) prints 6 on C++ at EVERY entry point, including the
default pipeline, while C# and JavaScript without `--optimize` print 12.
C++ fails even on the default (standard) pipeline, where LICM never runs,
and C# and JavaScript are right there. So C++'s failure may be the C++
backend's own lambda capture rather than an optimizer kill this decision
controls. That attribution is UNVERIFIED. If it proves to be a backend defect, it is a
separate brief, like the three the ruling already lists under D1
Obligations, and D2's L5 contract then applies to JavaScript under
`--optimize` (where the closure gap does reach the optimizer). MSIL does
not build L5.

### Obligations

Lands last (after D1), so every fire is a real defect. ASSUMPTION (the
UNVERIFIED mutant): the dynamic-use rule is zero-hit on the fast subset.
Measurement: fast subset plus L1-L7 under `--optimize`. A fire on a
correct probe means the region wrongly includes exit-path writes and
needs tightening, not that A is wrong.

### Rejected

- **B:** LICM-only, misses cross-loop CSE sharing.
- **C:** certifies nothing.

### Revisit if

Keeping the verifier quiet requires loop-carried dataflow (phi reasoning);
then S′ has outgrown a structural check.

### Amends

ADR-0005 D2 (use count is dynamic; region is path-based).

## Implementation note (D2)

- **What changed is the USE COUNT only; the region was already path-based.** `IRVerifier.CheckFunction`'s
  gate, `useSites.Count > 1 || (destination != null && useSites.Count >= 1)`, was a STATIC count —
  an anonymous value with exactly one static use was never checked, whatever its dynamic behaviour.
  `InstructionsBetween` (now `DefUsePaths.Region()`) was ALREADY the region the ruling asks for: a
  use sitting on a cycle that avoids the definition's block already pulled in the full loop body,
  including the back edge. Evidence the count, not the region, was the gap: at HEAD (pre-D2), a
  NAMED value with one use in such a loop and a Guard-name write after the use already FIRED (the
  static rule's "a named destination is itself a reader" clause already caught it — ADR-0005 D2);
  its exact anonymous twin, same shape, no destination, was QUIET (`S/adr6-d2/hand-before.txt`: (a10)
  FIRES, (a) QUIET). D2 adds `DefUsePaths.UseRepeats`, computed in the SAME walk `Region()` already
  needed — one walk now answers both "is this use repeated" and "what lies between it and the
  definition" — so the loop that makes a use count as repeated is, by construction, the loop
  `Region()` walks.
- **The rule reads a CYCLE, not a natural loop — the implemented interpretation, not yet
  architect-confirmed (task #157).** A definition inside a loop but on a branch the loop CAN skip
  (`If c Then t = p + q` … a use of `t` after `End If`, still inside the loop) sits on a cycle that
  avoids its OWN block: one execution of the definition reaches the use this iteration and again
  next iteration if that iteration skips the arm. So the use counts as repeated. MEASURED:
  `DynamicUseSPrimeHandBuiltIRTests.D_DefInLoopOnAnAvoidableBranch_UseAtMerge_WriteAfterUse_Fires_PendingTask157`
  and its `D2_...WriteOnTheOtherArm_...` sibling both FIRE. A natural-loop reading ("the loop
  lexically contains the definition") would exempt both instead — a real behavioural fork the
  architect has not ruled on. Filed as task #157, alongside the known gap below.
- **Measured, per the Obligations:** `S/adr6-d1/probes/L1..L7.bas` identical before and after,
  zero verifier fires, at all three entry points (CLI, CLI `--optimize`, Release `.blproj` —
  `S/adr6-d2/probes/matrix-before.txt` vs `matrix-after.txt`). The fast subset fires ZERO new violations —
  the ASSUMPTION the ruling flagged UNVERIFIED is now measured TRUE (`S/adr6-d2/trace-before.txt` ==
  `S/adr6-d2/trace-after.txt`: the same three deliberate fixtures both times —
  `IRVerifierModeResolutionTests`'s own known-S′ probe, `InvariantFWiringTests`'s Invariant F probe,
  `KillVocabularyVInvariantHandBuiltIRTests`'s Invariant V probe — none of them D2's dynamic count).
  All five sample-game paths fire zero, before and after (`S/adr6-d2/samples/summary.txt`; three of the
  five — `SampleGames/SpaceShooter`, `Samples/Pong`, `Samples/SpaceShooter` — do not currently
  compile on any backend, unrelated to this change; `fires=0` is reported for every row regardless).
  The verifier DOES run after the aggressive
  pipeline in the fast subset, per the Contract's own requirement: 86 of the 542 verified modules
  recorded an aggressive-pipeline compile, 454 a standard one (`S/adr6-d2/trace-after.txt.pipelines`) —
  confirming a test-host run actually reaches aggressive-pipeline IR, not only standard.
- **The probes discriminate — this is not a vacuous "nothing ever fires" measurement.** With
  `LoopInvariantCodeMotionPass`'s ByRef written-set reverted to the pre-fix version (an
  `IRInstanceMethodCall` ByRef argument no longer counts as a write), L1 and L2 print `seed\n6` for
  the correct `seed\n12` on C++ `--optimize` and on MSIL (`--optimize` and Release `.blproj`) — and
  D2 FIRES on exactly those cells, where the pre-D2 verifier stayed silent
  (`S/adr6-d2/probes/matrix-licmmut-d2.txt`'s `[VERIFY x1]` markers vs `matrix-licmmut-head.txt`'s none).
  D2 catches a real LICM regression over the same shape an execution-only probe already covers for
  a different reason — a structural check that would have caught the defect even before it was
  measured wrong at runtime.
- ⚠ **CLARIFYING (not a ruling change): the "'≥ 1 use' fires on correct L1" premise under Because is
  STALE.** It was measured before `6168628`, when LICM wrongly hoisted a value named `t3` in this
  shape; L1 has been correct since `6168628`. Two different mutants both answer to "the wrong
  widening" today, and they do NOT agree. The LITERAL "≥ 1 use" mutant (`useSites.Count >= 1`,
  collapsing the STATIC/DYNAMIC distinction rather than widening what counts as repeated) is QUIET
  on L1-L7, both under a direct `CheckInvariantSPrime` call and under the aggressive pipeline; it is
  killed only by the anonymous-single-use-with-a-non-repeating-write shapes ((c3)/(e)/(f)). The
  ADR's OWN wrong-widening model — "a use in any loop is repeated, and the region is that whole
  loop, regardless of whether the loop contains the definition" — DOES fire on correct L1: `t3`
  (`x * 2` hoisted to `for0.body`, `x` written by `b.Bump(x)`'s `IRInstanceMethodCall`), plus
  `t2`/`t5` on the loop's own induction variable `i`. So the mutant that matches this ADR's prose is
  the "any loop, whole-loop region" one, not the literal use-count one; both are real, distinct, and
  both are killed by the suite (see MUTATION-TESTED below).
- **Known gap, pending the architect — task #157, alongside the cycle-reading question above.** A
  value shared only DYNAMICALLY (one static use, in a repeating loop) is materialised differently by
  different backends: C# inlines it at its one syntactic use regardless of replicability, so a
  hoisted `n * 2` over a `ByRef` parameter or a non-Const global is RE-READ every iteration on C#,
  while C++/MSIL read the hoisted value once (computed in the preheader). Such an operand sits
  outside `Guard(v)` (`IRReplicability.IsReplicable` excludes a `ByRef` parameter and a non-Const
  global outright), so a wrong hoist over it goes unreported. MEASURED, with LICM's call-visible
  read check ALSO disabled: C++ and MSIL print `seed\n6` for `L4r` (a `ByRef` parameter aliasing the
  same global L4 hoists over) while this verifier stays quiet
  (`S/adr6-d2/probes/matrix-L4r-licmcv.txt`). "Option E" (for a value shared only dynamically,
  `Guard(v)` also takes its NON-replicable operands, so the C#-only re-read becomes visible too;
  `S/adr6-d2/mutsrc/IRVerifier.E.cs`) was explored but NOT adopted here: whether `Guard(v)` should depend on which BACKEND will read the
  value is a real ruling question, not an implementation detail this task can decide.
- `For Each` bodies are not cycles (`ControlFlowGraph`, ADR-0003 D3 — no back edge), so a use inside
  one is never seen as repeated by D2 either. No pass moves a value into a `For Each` body today
  (CSE is block-local; LICM hoists only out of natural loops, which a `For Each` is not), so this is
  a noted boundary, not yet a measured gap.
- **MUTATION-TESTED** (`VisualGameStudio.Tests/Compiler/DynamicUseSPrimeTests.cs`, task #137): five
  mutants, applied singly to the working tree, rebuilt, run, and restored by md5
  (`BasicLang/IRVerifier.cs` — `e3dad903420b0dc657354525ce4425bb` before, between and after every
  one). **M1** (revert the dynamic gate to the static count alone: `if (!staticallyShared) continue;`)
  killed by (a)/(a2)/(a3)/(a4)/(a5)/(a7)/(g) as predicted, PLUS (d)/(d2) — two more than the
  implementer's own contract, since those definitions are likewise anonymous single-static-use
  values the reverted gate would skip. **M2** (region without the back-edge body:
  `int end = _use.Index;` unconditionally) killed by (a)/(a4)/(a5)/(g)/(a10)/(d), NOT by (a2) or by
  the pre-existing `IRVerifierHandBuiltIRTests.DestinationWrittenLaterInALoopBody_ThatReReachesTheUse_Fails`
  — exactly as predicted: (a2)'s write sits in a separate LATCH block, reached by the forward walk
  regardless of whether the use's own block is truncated. **M3a** (the literal "≥ 1 use":
  `useSites.Count >= 1`) killed by (c3)/(e)/(f), plus the pre-existing
  `AnonymousTemp_OneUse_OperandWrittenInBetween_IsExempt`. **M3b** (the ADR's actual wrong-widening
  model: any cycle repeats, region is the whole loop) killed by (c)/(c2)/(c3)/(a6), plus — the
  direct evidence for the CLARIFYING bullet above — the L1-L7 structural check itself and
  `IRVerifierOutputIdentityTests.EmittedOutput_IsByteIdentical_WithTheVerifierOnAndOff`: a real
  compile throws under this mutant. A fifth, own mutant dropped the backward walk's "without
  re-entering the definition's block" boundary inside `DefUsePaths.Between` — killed by
  (a6)/(c2)/(c3). The brief's own suggested example, dropping `!ReferenceEquals(d, u)` from
  `bool repeats = !ReferenceEquals(d, u) && backward.Contains(u);`, is an EQUIVALENT MUTANT: the
  backward walk never adds the definition's block (it stops on it), so when `d == u`,
  `backward.Contains(u)` is already false and the guard is redundant. (A same-block use AFTER the
  definition never reaches that line at all: it takes the straight-line early return.) MEASURED: 0
  of 39 tests (this file plus every `IRVerifier*` fixture) fail under it.
- **Revisit is NOT triggered.** ("Keeping the verifier quiet requires loop-carried dataflow, i.e.
  phi reasoning.") Every FIRES shape measured is a structural CFG property — a cycle avoiding the
  definition's block, and a write reachable within it — and every fire measured across the fast
  subset and the sample games is zero. No case needed reasoning about a value's actual data-flow-
  merged identity across iterations to stay quiet.

## D3: Which rule decides whether a call can write a value's operand?

### Decision

One predicate, the declarations rule: `IsCallVisible(IRVariable v,
IRFunction f)` is true unless v is Const, a by-value parameter of f, or a
declared local of f that D1's closure rule does not make visible.
`ReadsCallVisible`, `IsCallVisibleDestination`, LICM's read check and the
verifier's call arm all delegate to it; the flag rule `(IsGlobal &&
!IsConst) || IsByRef` is retired. The verifier's call arm checks all of
`Guard(v)`, not the destination only.

### Because

- The flag rule's polarity is wrong: unknown implies private. The
  declarations rule defaults unknown to visible, the only safe default
  for a kill vocabulary.
- Option B adds IR shape (a flag on `IRVariable`, with obligations on five
  backends) to encode a fact the function's declarations already carry.
- One predicate is the CLAUDE.md "change once, not per consumer" rule
  applied to the optimizer.

### Contract

Q3 and Q3m print `13,3` on all four backends, all three entry points.
`OverKillWitness` still merges/hoists across a call. The corpus pins as
they stand at `6168628` are preserved. Hand-built IR: a shared value
reading an undeclared name with a call between definition and use fails
the verifier.

NOTE (clarifying, not a ruling change): as in D1's Contract, the ruling's
"11 sample-game merges" is a stale count carried forward from a code
comment (IROptimizer.cs:1647-1651) that predates commit 8b17c47's
deliberate loss of one SpaceShooter merge (ADR-0001 / ADR-0004 D2). At
`6168628` the pinned count is 10 (`Corpus_Platformer_Makes6Merges` +
`Corpus_SpaceShooter_Makes4Merges`). Stated above as the corpus pins as
they stand, not a specific count.

### Obligations

Lands FIRST: smallest change, default pipeline (ordinary class code is
wrong today), and D1's closure rule must be added to one predicate, not
two. ASSUMPTION: `IRFunction`'s declared-locals set is complete. An
omission only loses merges (the name is treated as visible), never prints
wrong.

### Rejected

- **B:** IR change for a fact declarations already carry, and still wrong
  polarity when the flag is unset.
- **C:** measured to lose every sample-game merge (11 when measured; 10
  are pinned today, see the NOTE under Contract).

### Revisit if

The declarations rule loses a merge a sample game measurably depends on;
then B's mark is a precision layer on top of A, not a replacement.

### Amends

ADR-0005 D2: "assigned" by a call now spans all of `Guard(v)`.

NOTE (clarifying, not a ruling change): across D1, D2 and D3, this ADR
amends ADR-0005 D2 three separate times — its Revisit clause is replaced
by D1's, its use count and region are made dynamic and path-based by D2,
and a call's "assigned" is widened to span all of `Guard(v)` by D3.
ADR-0005 carries a one-line "Amended by ADR-0006" callout under its
title, the way ADR-0004 carries "Amended by ADR-0005".

## Implementation note (D3)

- The corpus pin deviation: `Corpus_SpaceShooter_Makes4Merges` is now
  `Corpus_SpaceShooter_Makes0Merges` (4 → 0). The 4 merges read
  `SCREEN_WIDTH` / `SCREEN_HEIGHT`, whose `Const X = 800` declarations (no
  `As` clause) the parser drops, so the names reach the IR undeclared
  (`IsGlobal=false, IsConst=false`) and are visible under the declarations
  rule. SpaceShooter does not build (7 parse errors) and a well-formed copy
  with typed `Const`s makes 0 merges at this same base too — its `/` casts
  both operands to `Double` through fresh temps, so the shape was never a
  merge candidate once typed. D3's Revisit clause ("a merge a sample game
  measurably depends on") is therefore not met. DECIDED by the orchestrator,
  recorded here, not by the architect.
- The ASSUMPTION "`IRFunction`'s declared-locals set is complete" does not
  hold: `IRBuilder` omits a `For Each` loop variable, a `Catch` variable and
  the `With` carrier from `LocalVariables`. MEASURED to cost one merge on one
  probe (a `For Each` variable read across a call, still printing the
  correct answer either way); zero hits in the suite. Tracked on task #121.
- Three implementer choices beyond the ruling's own wording, each only
  ADDING kills or checks, never removing one: a NAMED-OPERAND arm in
  `ReadsCallVisible`, needed so CSE and the widened verifier arm agree — it
  fixed Q3n (`Dim a = (p + q) * 2` reading a renamed, undeclared `K`), wrong
  on all four backends, C# included, before it existed; temp-SPELLED names
  are no longer exempt from `NamedDestination` — a user field spelled `t1`
  is still a variable; `IsCallVisibleDestination` now takes the value, not a
  bare name.
- Measured across the whole suite: of 138 CSE merges and LICM hoists, 134
  survive the fix; the 4 lost are exactly SpaceShooter's.

## The three settled points

1. ADR-0005 D2's Revisit clause is replaced (see D1's Revisit). The
   verifier's independence is completeness (invariant V), not a second
   write model.
2. One rule answers both operand and destination (D3).
3. The D1 ruling includes the interim closure rule now; #122's capture
   set is an Obligation that narrows it and never blocks.

## Ordering

D3, then D1, then D2. Every step only widens kills or adds checks, so no
step can move a measured cell right to wrong. Verifier fires that appear
during D1/D2 on existing tests are wrong-to-flagged: count them, do not
baseline them.
