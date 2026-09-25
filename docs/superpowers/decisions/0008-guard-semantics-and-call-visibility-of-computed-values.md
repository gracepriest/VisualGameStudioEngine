# ADR 0008: Guard's replicability blindness, the S′ cycle reading, and call-visible copy propagation

- **Date:** 2026-09-25
- **Status:** Accepted
- **Decided by:** the architect role, dispatched on its pinned model with no
  per-invocation override.
- **Brief:** [`0008-brief.md`](0008-brief.md). Tasks #157 and #156 in
  one batched consultation. It was written by the brief role and replaced by
  the orchestrator after review, with the corrections listed at its foot.

## Context

S′ (ADR-0005 D2, as ADR-0006 D2 amends it) as it stood going into this
consultation:

- For a value `v` with dynamic use count > 1, no variable in `Guard(v)` is
  written between `v`'s definition and a use.
- `Guard(v)` = the variables reached through `v`'s REPLICABLE operands ∪
  `v`'s named destination. Non-replicable operands (a call, a field load, a
  `ByRef` parameter, a non-`Const` global — `IRReplicability.IsReplicable`)
  were left out, on ADR-0005 D2's stated reason: "a non-replicable operand
  is evaluated once and read back by name."
- ADR-0006 D2 counts a use as repeated when its block lies on a cycle that
  avoids the definition's block — targeting the shape LICM leaves: one
  hoisted value, one static use, run every iteration. Its implementation
  note left two items pending the architect: whether that cycle reading is
  right, and a known gap where a value shared only dynamically escapes
  `Guard(v)` — both filed as task #157.
- `ReadsCallVisible` (`IROptimizer.cs:401-431`), the call arm of the kill
  vocabulary for a value's reads, is shared by CSE (its own records) and,
  since task #146, CopyPropagation (a fact's value). It walks pure
  operators; a variable is visible per `IsCallVisible`; every other value
  (a call, a field or indexer load, an allocation) returns TRUE.

**How each backend materialises a value** (measured from code, `fc08b7f`):

| Backend | Rule | Where |
|---|---|---|
| C# | Declares a local ONLY for a value with use count > 1 that is non-replicable and has no named destination. Every other value is inlined at its use, including a single-use call. | `CSharpBackend.cs:2957-2987`, `:2845-2851` |
| C++ | Every non-constant value without a named destination is a temp declared at function entry and assigned at its definition. | `CppCodeGenerator.cs:1806-1812`, `:2027` |
| MSIL | Every named non-variable value gets a local slot. | `MSILBackend.cs:2780-2788` |
| JavaScript | Every value instruction binds `const <name>` at its definition; later uses refer to the name. | `JavaScriptBackend.cs:1854-1872`, `:901-913` |

A value with a named destination is read back by name on every backend. C#
is the only backend that re-evaluates a single-use value at its use, and
only when that value has no named destination.

**The L4r evidence** (`S/adr6-d2/probes/L4r.bas`: `Sub Work(ByRef n)`, a
call in the loop bumps the global aliased as `n`, loop body `s = s + n *
2`, correct output `12`). Mutating LICM to hoist `n * 2` over the call: C++
`-O`, MSIL `-O` and MSIL Release print `6`; C# prints `12` because it
re-reads `n` inline; JavaScript refuses the `ByRef` (BL7002). The shipped
verifier is silent, because `n` sits outside `Guard(v)`
(`S/adr6-d2/probes/matrix-L4r-licmcv.txt`). With today's LICM there is no
hoist and every backend prints `12` (`matrix-L4r-after.txt`).

**Option E's measurement** (`S/adr6-d2/mutsrc/IRVerifier.E.cs`, a
verifier-only change: for a value shared only dynamically, `Guard(v)` also
takes its non-replicable operands): fires on the L4r wrong hoist in every
`-O` cell, including C#'s (where the output happens to be right) and MSIL
Release (`matrix-E-licmcv.txt`); fires zero times with today's LICM, on
L1–L7, the samples and the fast subset (`matrix-E.txt`).

**The Q2 probes** (`S/arch-batch/probes/`, a scratch mutant making
`ReadsCallVisible`'s default arm return FALSE): Q2a/Q2b (`Dim x = Seed()`
then a call then a use) are IR-identical to HEAD — the call lowers straight
into `x`, no copy, no fact to relax. Copy facts arise only from `New …`
and instance-method calls. Q2d/Q2e substitute across the intervening call
and still print right output on every backend that builds (12/12 for
Q2e); but emitted code changes on every backend for Q2d/Q2e, and not for
the better — the substituted value now has two uses (the copy and the
substitution), so each backend keeps it as a separate temp computed before
the call (an extra `int t1` local on C#, an extra local slot on MSIL, and
so on). Q2a–Q2c stay byte-identical to HEAD.

**#146's cost** (`S/cp146/analysis.txt`): CopyPropagation's existing rule
(treating a non-replicable value as call-visible) lost 74 facts and gained
5. 8 losses are correct kills (CP1); 1 is correct under ADR-0006 D3; about
60 are in 9 BCL value-type programs (`New DateTime(...)`, `.AddDays(...)`,
`New TimeSpan`, `New Guid`, `New DateTimeOffset`), all through the value
half. Program output was unchanged in every case; the emitted-code effect
of those 60 losses was not diffed.

The framing question these measurements raised: is S′ a "backends agree on
the value" invariant, or a "the optimizer kept source semantics for a
moved value" invariant? For a replicable operand, D2 already catches a
wrong hoist. For a non-replicable one, the only thing that differs across
backends is C#'s inlining.

## Questions

Three rulings from one batched consultation:

- **Q1 (→ D1, task #157):** Should `Guard(v)` include non-replicable
  operands; what does S′ certify?
- **Q1-sub (→ D1-sub, task #157):** Confirm or replace ADR-0006 D2's cycle
  reading, left pending the architect's confirmation in that ADR's
  implementation note.
- **Q2 (→ D2, task #156):** Should `ReadsCallVisible` stop treating a
  non-replicable value as call-visible?

## D1 (#157): Should `Guard(v)` include non-replicable operands; what does S′ certify?

### Decision

S′ is the "moved value keeps source semantics" invariant, judged under IR
semantics: a value is computed once per execution of its definition, and a
named destination materialises it. Adopt Option E, generalised: `Guard(v)`
is replicability-blind. It collects every `IRVariable` reached through
pure operators from v's operands, stops at call-shaped nodes (the kinds
ADR-0006 D1 classifies as calls or acting as calls: call, instance/base
call, allocation, field/indexer load, await), and adds the named
destination. It applies to every value S′ checks, statically or
dynamically shared. B is rejected as a #157 fix.

### Because

- "Evaluated once and read back by name" is a materialisation claim;
  materialisation is a backend property. A verifier that prunes its guard
  by what a backend will inline certifies backend agreement, not the
  optimizer. C#'s 12 on the mutated L4r is an accident: the IR is wrong,
  and E says so in every cell.
- `IsReplicable` and `IsCallVisible` disagree about the same names. A
  declared `ByRef` parameter is pruned from Guard as non-replicable yet is
  exactly what the call arm exists to check; an *undeclared* name survives
  pruning and fires (D3's hand-built contract passes through that
  inconsistency). The walk that builds Guard must be the walk
  `ReadsCallVisible` already does.
- Dynamic-only E is measured zero-fire; the static twin (a CSE merge over a
  `ByRef`/global read with a call between) has the identical blind spot.
  Under this framing there is no principled line between them.

### Contract

- L4r under the LICM double-mutant: S′ FIRES on C++ `-O`, MSIL `-O` and
  Release, and C# `-O` — C# included, though it prints 12. This cell is
  the discriminator between the two framings; pin it. With today's LICM:
  zero fires, 12 on every backend that builds, all three entry points.
- L1–L7, the five sample paths, the fast subset: zero fires; emitted
  output byte-identical with verifier on/off (existing pin).
- Hand-built IR, fires: a preheader value reading (i) a `ByRef` parameter,
  (ii) a non-`Const` global, one in-loop use, a call in the loop; (iii)
  same with a direct store instead of a call. Static twin: two static uses
  reading a `ByRef` parameter, a call between them.
- Hand-built IR, quiet: `t = Foo(n); v = t + 1` shapes — Guard stops at
  `t`; `n` is not collected.
- ASSUMPTION (the static extension is unmeasured): the uniform rule fires
  zero on the fast subset, L1–L7 and the samples. If it fires on IR no
  pass touched (an IRBuilder shape), land dynamic-only E (measured) and
  record the shape under Revisit. Do not reintroduce replicability either
  way.

### Obligations

Verifier-only; lands first in this batch, before anything on #156 or
#118. Implement ONE operand walk in `IROptimizer.cs` that both consumers
use, so Guard and `ReadsCallVisible` cannot drift:

```
static (IReadOnlySet<IRVariable> Names, bool HitCallShaped) CollectReads(IRValue v)
ReadsCallVisible(v) == HitCallShaped || Names.Any(n => IsCallVisible(n, f))
Guard(v)            == Names ∪ { v.NamedDestination }
```

A kind on neither the pure list nor the call-shaped list sets
`HitCallShaped` (the safe default, matching D1's Universal). Test-writer: a
mutant that restores the `IsReplicable` prune in Guard, killed by the
ByRef/global hand-built shapes and the L4r-mutant matrix.

### Rejected

- **A (keep the gap, documented):** leaves a wrong hoist over the commonest
  non-replicable operand unreported on three of four backends.
- **B alone (C# materialises dynamically-shared values):** makes a wrong
  hoist uniformly wrong; the verifier stays blind. A backend change for a
  verifier gap.
- **E + B:** B buys nothing once E fires.
- **Guard keyed on the target backend:** IR must have one semantics; the
  verifier must not know the backend.

### Revisit if

The uniform rule fires on IRBuilder-produced IR no pass touched; then
"shared" needs pass provenance (a moved/merged bit on the value), not a
replicability filter.

### Amends

ADR-0005 D2 — the Guard definition; "non-replicable operands are left out"
is struck. ADR-0006 D2 — the known-gap note in its implementation note is
closed.

## D1-sub: the CYCLE reading

### Decision

Confirm CYCLE. A use is repeated iff its block lies on a cycle that avoids
the definition's block. A definition on a skippable arm inside the loop is
dynamically shared.

### Because

- One execution of the definition is live across the back edge into the
  next iteration when the arm is skipped; that is the exact hazard S′
  names. Lexical containment is irrelevant.
- Natural-loop exempts a shape that can be wrong; cycle flags it and
  nothing correct (zero fires measured). Sound beats lexical.
- No pass produces the shape today, so the reading costs nothing now and
  is the right default for any future PRE/GVN.

### Contract

(d)/(d2) keep firing and drop `_PendingTask157`. Test-writer adds mutant
M6, the natural-loop reading (exempt when the definition's block lies
inside the loop), killed by (d)/(d2). Fast subset and samples: zero fires
(already measured).

### Rejected

- **Natural-loop reading:** exempts a value live across a back edge.
- **Dominance ("definition dominates the use"):** the same exemption by
  another name.

### Revisit if

A correct IRBuilder shape fires (d)-style; then IRBuilder itself creates
anonymous values live across a back edge and the region needs a
per-iteration cut.

### Amends

ADR-0006 D2 implementation note — "pending architect confirmation" is
resolved.

## D2 (#156): Should `ReadsCallVisible` stop treating a non-replicable value as call-visible?

### Decision

Keep. A call-shaped value is call-visible for both consumers, CSE and
CopyPropagation. No relaxation, no per-consumer split.

### Because

- Measured benefit is zero: no output change, and where emitted code
  differs the relaxed rule is worse (an extra temp and a surviving copy on
  every backend).
- The relaxed substitution is correct only while the originating copy
  survives — a liveness invariant no pass owns, and DCE (#118) is the pass
  that would break it. A rule that is safe only because another pass is
  disabled is not safe.
- ADR-0006 D3 unified the predicate; a CP-only relaxation would re-split it
  for nothing.

### Contract

Q2a–Q2e emitted code byte-identical to HEAD on all four backends, three
entry points; Q2e 12/12 cells right. Unit pins: `ReadsCallVisible` returns
TRUE for `IRCall`, `IRInstanceMethodCall`, allocation, `IRFieldAccess`,
indexer load; a CP fact from a `New` or an instance-method call is killed
by an intervening call (the CP1 family). The ~60 BCL value-type "losses"
are not a regression and need no pin beyond output.

### Obligations

Lands after D1 as pins only; no code change. #118 inherits settled point
3: before DCE is un-gated, the C# backend materialises a non-replicable
value whose single use is not adjacent to its definition (B restricted to
that shape). Its emitted-code cost is measured then, not now. The CSE-side
and copy-removal UNVERIFIED points are moot under Keep.

### Rejected

- **Relax both:** needs a copy-survival invariant to be safe; buys
  nothing.
- **Relax CP only:** splits the unified predicate for zero gain.
- **Relax plus a structural "copy still present" guard:** a guard on
  another pass's future behaviour, unowned.

### Revisit if

A relaxed mutant shows an output-visible or emitted-size-visible win on a
real program (sample game or suite corpus), measured as emitted-code diff,
not fact counts.

### Amends

None. Records #146's rule as intended, not incidental.

## Settled points

1. **S′'s semantics.** Judged under IR semantics: a value is computed once
   per execution of its definition; named destinations materialise; every
   backend must implement that. Backend divergence is an IR-to-backend
   fidelity defect, never an S′ concern. Fact counts (#146's 74 lost / 5
   gained) are not a cost metric; output and emitted-code diff are.
2. **Replicability is a backend concept** (C#'s "may I inline?"). It
   appears in neither the verifier nor the kill vocabulary. One operand
   walk (`CollectReads`) feeds both Guard and `ReadsCallVisible`.
3. **Adjacency rule (IR-to-backend fidelity, C# specifically).** A backend
   may evaluate a non-replicable value away from its definition only when
   the use is adjacent (next instruction, same block — IRBuilder's
   expression-tree shape) or the value is dynamically shared and S′ has
   certified its Guard. C# satisfies this today by shape: CSE and CP
   create ≥2 uses (materialised), LICM creates the dynamically-shared
   hoist (now certified under D1), DCE is latent. The first pass that can
   leave a non-replicable value single-use and non-adjacent (#118)
   triggers restricted B on C#. C++, MSIL and JavaScript already satisfy
   it.
4. **ASSUMPTION on CP's own kill rule:** a direct store to any variable the
   fact's value reads kills the fact (not only a call). Required already
   for replicable facts on C#. Measurement: an IR-level pin that the fact
   table drops the fact after the store — output cannot observe it today
   because the substituted value is multi-use and materialised. If it
   fails, that is a pre-existing CP defect, separate from this ruling, and
   blocks #118.
5. **Ordering:** D1 (verifier) → D1-sub (rename + mutant M6) → D2 (pins).
   No emitted code changes anywhere in this batch; the byte-identity pin
   must hold across all three.

Files this ruling touches by name: `BasicLang/IRVerifier.cs` (Guard),
`BasicLang/IROptimizer.cs` (`ReadsCallVisible`, the shared walk),
`VisualGameStudio.Tests/Compiler/DynamicUseSPrimeTests.cs` and
`DynamicUseSPrimeHandBuiltIRTests` (the (d)/(d2) rename, M6),
`docs/superpowers/decisions/0005-*.md` and `0006-*.md` (Amends callouts).

## Ordering

D1 (verifier) → D1-sub (the cycle-reading rename and mutant M6) → D2 (pins
only). No emitted code changes anywhere in this batch; the byte-identity
pin must hold across all three steps.

## Implementation status

D1, D1-sub and D2 are IMPLEMENTED. `BasicLang/IROptimizer.cs`'s shared
`CollectReads` walk feeds both `Guard` (`BasicLang/IRVerifier.cs`) and
`ReadsCallVisible`; the D1-sub rename (dropping `_PendingTask157`) and
mutant M6 live in
`VisualGameStudio.Tests/Compiler/DynamicUseSPrimeTests.cs`'s
`DynamicUseSPrimeHandBuiltIRTests`; D2 lands as pins only, no code change.

## Implementation note (D1, D1-sub, D2)

- **What changed.** `IRVerifier.cs`'s `CheckFunction` used to build
  `Guard(v)`'s operand half with a private `CollectOperandGuard` walk that
  pruned by `IRReplicability.IsReplicable` — a `ByRef` parameter and a
  non-`Const` global were excluded outright (ADR-0005 D2). That walk is
  deleted. `IROptimizer.cs` gains one new walk, `CollectReads(IRValue) →
  (Names, HitCallShaped)`: it descends the same four pure operators
  (`IRBinaryOp`/`IRUnaryOp`/`IRCompare`/`IRCast`), collects every
  `IRVariable` and every named-destination instruction it reaches
  (blind to replicability), and stops at a call-shaped node (a call,
  instance/base call, allocation, field/indexer load, await) or any kind
  on neither list, setting `HitCallShaped`. `Guard(v)` is now
  `CollectReads(v).Names ∪ { v's destination }`; `ReadsCallVisible(v, f)`
  is now `HitCallShaped || Names.Any(r => IsCallVisible(r, f))` — the same
  walk, so the two consumers cannot drift (the ADR's own obligation).
- **Three choices the implementer made, each worth stating on its own:**
  - **A named nested operand DESCENDS.** `t0 = a * 2` where `a` is itself
    a named pure operator (`a = p + 1`) collects both `a`'s name AND `a`'s
    own operand `p` — not just `a`. The rejected alternative (stop at a
    named operand without descending into it) is exactly mutant G4 below;
    it is killed by
    `Adr0008GuardReplicabilityBlindHandBuiltIRTests.NamedNestedOperand_DescendsIntoItsOwnOperands_WriteToNestedOperand_Fires`,
    which would go QUIET under that alternative (measured, this session).
  - **Guard has NO call-hit arm.** A pure value's walk hitting a
    call-shaped node (`HitCallShaped`) does not, itself, add anything to
    `Guard(v)` — the call-shaped operand is evaluated once where it is
    defined, so `t = Foo(n) : v = t + 1` guards only `t`'s own name (if
    named) and `v`'s destination, never `n`. **Variant B** (give Guard a
    synthetic `<call-shaped operand>` entry whenever a pure value's walk
    hits one, so ANY later call fires it) was measured and REJECTED: it
    turns every one of the ruling's quiet shapes FIRES — `t = Foo(n)` then
    a call (`q1`), `t` renamed to a declared local then a call (`q1c`), a
    field-load operand then a call (`q2`) all flip to FIRES under B,
    contradicting the ruling's own "Guard stops at `t`; `n` is not
    collected" contract. (This session's own G2/G4 mutants target
    `CollectReads`/`ReadsCallVisible` specifically; Variant B is a
    THIRD, separate alternative — a Guard-only widening — already
    measured and rejected by the implementer, not re-mutated here, since
    the ruling itself rejects it outright and the quiet-shape hand-built
    tests already pin against it by construction.)
  - **A nameless variable's `StorageRead.Name` is `""`, not `null`, and is
    call-visible.** `StorageRead.Name`'s own doc comment calls this out;
    `IsCallVisible(IRVariable, f)`'s own words are "An IRVariable with no
    name is not a declaration of anything — unknown, so visible" — TRUE,
    matching every other undeclared name. Pinned in
    `Adr0008CollectReadsAgreementTests.PerNodeKind_AgreementAndPinnedValues`
    (this session's first draft assumed FALSE here and was corrected
    against the live behaviour before shipping).
- **The zero-fire ASSUMPTION, measured true.** From the implementer's own
  instrumented full/fast-subset sweeps (`S/adr8/full-after-instr.trace*`,
  `fast-final-instr.trace*` — cross-checked this session for staleness:
  the instrumented tree's core `CollectReads`/Guard/`ReadsCallVisible`
  logic diffs identically to the current landed code, comment-only and
  one behaviourally-inert reordering aside, and its RCV-call count
  matches the ADR's own headline figure below exactly): the fast subset
  (5,962 tests, 5,887 passed, 75 skipped, 0 failed) swept 557 compiled
  modules through `CheckInvariantSPrime` under the CURRENT Guard
  construction AND, separately, under the pre-ADR8 (`IsReplicable`-pruned)
  Guard, the G4-style (named-nested-stop) Guard, and Variant B — all four
  produced the exact SAME single S′ violation, from
  `IRVerifierModeResolutionTests`'s own deliberate known-S′ probe (a
  named destination's own unconditional self-guard, unrelated to
  replicability). The full suite (8,301 tests, 8,050 passed, 251 skipped,
  0 failed, 3,397 modules swept) shows the identical result: one S′ fire,
  one F fire, one V fire — the same three deliberate fixtures ADR-0006 D2
  already named, none of them new. No IRBuilder shape anywhere in the
  suite fires under the replicability-blind rule that did not already
  fire under the old one. (This session's OWN mutation-prove, below, is
  the independently-reproduced half of this same claim: G1/G2/G3/G4/M6
  each applied to a scratch tree, confirmed to change nothing on the
  ~132-test filtered set except the shapes each targets.)
- **`ReadsCallVisible` equivalence.** `OptimizationPass.ReadsCallVisible`
  was called 76,167 times sweeping the full suite's compiled IR
  (`S/adr8/full-after-instr.trace.rcvcount`); every call answers exactly
  `HitCallShaped || Names.Any(IsCallVisible)` by construction (the method
  IS that formula now), and `Adr0008CollectReadsAgreementTests` pins the
  formula directly, per node kind and swept over L1-L7/L4r/Q2a-Q2e's
  aggressive-pipeline IR, so a future edit that lets the two drift again
  is caught structurally, not only by corpus luck.
- **Byte identity.** Re-verified live this session (`bytecmp.py`,
  `S/adr8/probes/out-before` vs `out-final`): **222/222 emitted probe
  files byte-identical** across L1-L7, L4r and Q2a-Q2e, all four backends,
  all three entry points (CLI, CLI `--optimize`, Release `.blproj`). The
  five sample games, same three entry points, four backends (60 cells):
  the 45 non-C# cells are byte-identical outright; the 15 C# cells differ
  ONLY in a `#line` directive's embedded scratch-output path (confirmed by
  direct diff — every other byte matches), an artifact of the comparison
  script's two output roots having different names, not a codegen change.
  `fires=0` on all 60 cells, both before and after. (Three of the five
  sample paths do not currently compile on any backend, unrelated to this
  task — ADR-0006 D2's implementation note already flags this; `fires=0`/
  byte-identity is reported for every row regardless, matching that
  note's own convention.)
- **The L4r discriminator matrix — this batch's own contract cell.**
  Under TODAY's LICM (no mutant): zero fires, `12` on every backend that
  builds (C++, MSIL, C# at all three entry points; JavaScript refuses the
  `ByRef` parameter, BL7002) — shipped as
  `DynamicUseSPrimeAggressivePipelineStructuralTests`/`ExecutionTests`.
  Under the LICM DOUBLE MUTANT (`S/adr6-d2/mutsrc/IROptimizer.licmbyref.cs`'s
  reading applied to a SCRATCH tree, never this repo): re-run live this
  session (`bin-licm-after`, confirmed byte-identical to the current
  `IRVerifier.cs` and differing from the current `IROptimizer.cs` in
  EXACTLY the two mutated lines) — SIX cells fire (`[VERIFY x1]`): C++
  `-O`, MSIL `-O`, MSIL `Release`, C# `-O`, C# `Release`, and (a fifth
  backend cell the ruling's own contract sentence does not name) the
  JavaScript `-O` CLI leg — the verifier runs on the optimized,
  backend-agnostic IR before JavaScript's own BL7002 `ByRef` refusal is
  raised at code-generation time, so it fires on the same wrong hoist
  before the backend ever gets a chance to refuse it. Two cells stay
  quiet: `cli` (unoptimized, no LICM at all) on every backend, and C++
  `Release` (the native project build path does not route through the
  same `--optimize` aggressive pipeline the mutant targets). C# `-O`/
  `Release` fire even though they PRINT 12 (C# re-reads `n` inline) — the
  exact discriminator the ruling calls for: D1 says so in every `-O`
  cell regardless of whether a backend's own accident hides the bug.
- **The flipped test.** `A4g_PreheaderDefOverModuleGlobal_InGuardSinceAdr8_CallAfterUse_Fires`
  (was `..._NonReplicable_OutsideGuard_Quiet`): a non-`Const` module
  global, one in-loop use, a call in the loop — QUIET before ADR-0008
  (pruned as non-replicable), FIRES after (D1's contract shape (ii)).
  Kept, per the brief.
- **Settled point 4 (direct stores do kill) — measured true, plus a new
  pre-existing gap this task found.** A direct `IRStore` through a
  variable's address kills a `CopyPropagation` fact that reads it, not
  only a call (`Adr0008D2Pins.SettledPoint4_DirectStoreThroughAnAddress_KillsTheFact`,
  MEASURED live). But: a direct `IRAssignment` to a NAMED NESTED
  operand's OWN NAME (`u = a + 1` renamed `u`, `t0 = u * 2`, `x := t0`,
  then `u = 5`) does NOT kill `x`'s fact — `CollectReads(t0)` correctly
  reports `t0` reads storage named `u` (ADR-0008's own walk gets this
  right), but `CopyPropagationPass.Invalidate`'s `Mentions()` walks the
  recorded VALUE structurally for an `IRVariable` named `u` and has no
  case for "a named pure-operator instruction whose destination happens
  to be `u`", so it never finds it. Pinned as a KNOWN-WRONG regression
  test,
  `Adr0008D2Pins.KnownGap_DirectStoreToANamedNestedOperandsOwnName_DoesNotKillTheFact`,
  NOT fixed here (test-writer scope). This is a genuine, separate defect
  in `CopyPropagationPass`'s own kill rule — filed as task #161, outside
  ADR-0008's scope, and it blocks #118
  (DCE) the same way settled point 4 does: DCE cannot safely remove `u`'s
  own defining instruction while a stale fact might still reference it.
- **The mutation table** (each mutant built in a SCRATCH tree, its
  `BasicLang.dll` swapped into
  `VisualGameStudio.Tests/bin/Release/net8.0/`, the filtered suite
  (`IRVerifier*`/`DynamicUseSPrime*`/`CopyPropagation*`/`Adr0008*`, 132
  tests) run, then the CLEAN dll restored. The repo's own
  `IROptimizer.cs`/`IRVerifier.cs` were confirmed at their reviewed md5s
  after the last mutant):

  | Mutant | What it does | Killed by | Result |
  |---|---|---|---|
  | G1 | Restores the `IsReplicable` prune in Guard | (i)/(ii)/(iii)/(iii-g)/static-twin×2/A4g | 7 failures |
  | G2 | `ReadsCallVisible` reverts to a private recursion that ALSO drops the named-destination check (the brief's own suggested drift — the implementer's own verbatim-restore G2 is mathematically equivalent and measures 0 mismatches, so it needs this drift to have anything to kill) | the named-operand agreement-pin entry | 1 failure |
  | G3 | `CollectReads`'s default arm sets `HitCallShaped = false` | the default-arm `HitCallShaped` pins (`IRLoad`/`IRPhi`/`IRGetElementPtr`/`IRArrayAlloc`) and the per-kind table | 2 failures |
  | G4 | `CollectReads` stops at a NESTED named operand without descending (root unaffected — an existing statically-shared named value, `A10`, still fires normally) | `NamedNestedOperand_DescendsIntoItsOwnOperands_WriteToNestedOperand_Fires` | 1 failure |
  | M6 | `UseRepeats`'s natural-loop-participation reading (exempt when the definition's own block sits on any cycle), not the confirmed CYCLE reading | (d)/(d2), PLUS `A5` (its definition's block, `outer.body`, also loops back to itself — a broader kill than the contract requires, not a violation of it) | 3 failures |

  L4r's LICM-double-mutant discriminator is not a shipped test — see the
  matrix above; recorded for this note.
- **Revisit is NOT triggered.** No IRBuilder-produced shape anywhere in
  the fast subset, the full suite, or the five sample paths fires under
  the replicability-blind rule that did not already fire under the old
  one — the corpus measurement above is the direct evidence.
- Implementer data: `S/adr8/` (harness, probes, matrices, the full/fast
  instrumented traces this note's numbers are drawn from).
