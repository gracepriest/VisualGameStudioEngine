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

D1 — the shared `CollectReads` walk in `BasicLang/IROptimizer.cs`, feeding
both `Guard` (`BasicLang/IRVerifier.cs`) and `ReadsCallVisible` — is being
implemented in the same PR as this ADR, alongside the D1-sub rename and
mutant M6 in
`VisualGameStudio.Tests/Compiler/DynamicUseSPrimeTests.cs`/
`DynamicUseSPrimeHandBuiltIRTests`. D2 lands after D1, as pins only. An
implementation note, in the style of ADR-0006's and ADR-0007's, follows
once that work lands.
