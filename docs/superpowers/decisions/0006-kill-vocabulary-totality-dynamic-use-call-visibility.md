# ADR 0006: kill-vocabulary totality, dynamic-use S′ regions, and call visibility

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

### Amends

ADR-0005 D2. Its Revisit clause is replaced by the one above. "Assigned
uses CSE's vocabulary" stands, with the vocabulary now total and
completeness-checked.

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
