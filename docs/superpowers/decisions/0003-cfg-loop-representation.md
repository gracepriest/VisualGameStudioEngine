# ADR 0003: loop representation in `ControlFlowGraph.cs`, and disabling the loop passes

- **Date:** 2026-09-22
- **Status:** Accepted
- **Decided by:** architect role, ruling made by Opus — the pinned architect model, Fable 5.1,
  has been rate-limited (HTTP 429) for this entire project. Same arrangement as ADR-0001;
  see that ADR's Provenance section.
- **Brief:** [`0003-brief.md`](0003-brief.md) — 188 lines, preserved in full beside this ADR.
  The architect answers only from the brief, so the brief is the entire basis of this ruling.

## Question

`ControlFlowGraph.IdentifyLoops` reports FOUR "natural loops" for a function containing one
loop, every one of them containing the `entry` block — and TWO for a function containing no
loop at all. Should the back-edge predicate be repaired in place, or the loop representation
replaced? Does the fix ship alone? And is it acceptable that `For Each` and `Try` lower to
structured IR nodes rather than branches, so the CFG holds no cycle for them at all?

## The measurement that decided it

The defect is ONE INVERTED PREDICATE (`ControlFlowGraph.cs:283`): `successor.Dominators
.Contains(block)` asks whether the TAIL dominates the HEAD. A back edge requires the reverse.
It is not a missing dominator computation — `ComputeDominators` is measured correct on all 13
shapes. Flipping that one token makes the reported loop sets correct 13/13.

⛔ **And flipping it alone was measured to make the compiler STRICTLY WORSE**: 8 of 8 loop
programs break on all four backends, emitting things like `while (0 <= 7) { i = 1; }`.

The reason is a SECOND, independent defect that the broken sets currently mask.
`IsValueInvariant` (`IROptimizer.cs:1456`) treats EVERY local as loop-invariant: a bare
`IRVariable` operand has `ParentBlock == null`, so `!loop.Contains(null)` is true. Today the
only usable bogus set is `[for0.cond, entry]`, which contains no body block, so LICM can only
reach the loop condition. **The broken analysis is acting as an accidental safety limiter on a
worse bug.**

## Decision

**D1 — repair the predicate in place.** Flip `:283` to `block.Dominators.Contains(successor)`.
Do NOT build a dominator-tree / loop-nesting-forest model now; its only advantage is
per-consumer incremental migration, and D2 leaves zero consumers. Synthesising preheaders in
`IRBuilder` is NOT authorized — it does not fix the predicate, and it changes user-visible
emitted block shape across every backend.

**D2 — the fix does NOT ship alone.** It ships as ONE commit together with unregistering
`LoopInvariantCodeMotionPass`, `LoopUnrollingPass` and `LoopFusionPass` from
`AddAggressivePasses()`, AND with the `IsValueInvariant` fix.

**D3 — two loop representations stay.** `For Each` and `Try` lower to structured IR nodes;
reporting zero loops for them is the CORRECT answer for a natural-loop analysis, not a gap.

**D4 — delete `IsReducible`.** **D5 — delete the dead CFG surface** (`PostDominatorTree`,
`DominatorTree`, `ComputeDominanceFrontier`, `ComputeBlockDepths`). Keep `ComputeDominators`.

## Because

- ⭐ **Losing loop optimization entirely is the right trade today, because today's loop
  optimization has NEGATIVE value, not zero.** LICM is the only loop pass that fires, and it
  silently miscompiles two sibling loops on **C#, the reference oracle**, to 65 where 29 is
  correct, and zeroes out iteration counts on C++ and MSIL. **An optimization that changes the
  answer is not an optimization.** "Do nothing" is therefore not neutral — it leaves a known
  wrong answer on the oracle in every Release build.
- Repairing the substrate and leaving the passes on turns on two passes that have **never once
  executed correctly on any program**, and that were measured to emit undeclared names
  (`LoopUnrolling` mints `_u0__u0_i`) and dangling branch targets (`LoopFusion` removes blocks
  that branches still target, and is **silently wrong on C#**). That is not a shipping
  decision; it is a bug intake.
- ⭐ `IsValueInvariant` is fixed NOW **precisely because it is inert now**. Leaving a
  known-wrong invariance test behind a disabled pass is the trap that produced this whole
  incident: the next person re-registers LICM and reproduces the failure from scratch.
- Unverified analysis code that *looks* authoritative is exactly the failure mode being
  repaired here. The four dead CFG methods have never been executed by production code and
  have no tests; keeping them "for later" preserves the trap, not the capability. A correct
  `IsReducible` with no callers would be worse than a broken one — it would return `false` for
  irreducible graphs with nobody written to handle it.
- Unifying the two loop representations is the largest change in the option space — it reaches
  `IRBuilder` and every backend, where C++ emits C++20 coroutines and `Generator<T>` for
  iteration and C# emits a real `foreach` — bought for a consumer set that is now empty.

## Contract

- **INV-1** — `FindBackEdges()` yields `(tail, head)` iff `head ∈ tail.Dominators`. Checkable
  on the 13 shapes: reported count == SHOULD, **including the two shapes that must report 0**;
  no loop contains `entry`; no loop contains its own `.end`.
- **INV-2** — no loop pass is registered in any pipeline. ⛔ **A diff that satisfies INV-1 but
  not INV-2 must not merge.**
- **INV-3** — for all 13 shapes on all four backends, `--optimize` output is behaviourally
  identical to default output.
- **INV-4** — structured IR nodes are **opaque** to CFG loop analysis: they contribute no cycle
  and no `NaturalLoop` entry. Any future loop pass must be correct under the premise that
  unseen loops exist in the function, and **may not treat "this block is in no loop" as "this
  block executes once."** That sentence belongs in the `NaturalLoops` doc comment; it is the
  whole of D3 for the next reader.

## Obligations — stated as cost, not as free

- **Release builds lose all loop optimization.** No measured performance regression, because
  only LICM fired and it fired wrongly — but the loss is real and permanent until D2's revisit
  condition is met.
- Fixtures asserting hoisted / unrolled / fused output must be re-baselined. ⚠ The coordinator
  expects this to be largely INVERTED in practice — the aggressive fixture added in `3338dfd`
  deliberately did not pin LICM-damaged output, it EXCLUDED C++/MSIL from aggressive loop cases
  with docstrings naming the defect, so those exclusions likely become PROMOTABLE. To be
  measured, not assumed.
- `InductionVariablePass` stays disabled; this change does not re-enable it.
- SSA construction, if ever attempted, re-implements `ComputeDominanceFrontier` from scratch.

## Rejected

- **A dominator-tree / loop-nesting-forest model now** — correct destination, wrong time.
- **Synthesised preheaders in `IRBuilder`** — does not fix the predicate; changes emitted block
  shape across every backend; orthogonal to the actual defect.
- **The predicate fix alone** — measured strictly worse than doing nothing (8/8 programs break
  on 4/4 backends).
- **Predicate + `IsValueInvariant` with the passes left on** — fixes 6/8 shapes while shipping
  two passes that have never correctly executed. Trading a known wrong answer for two unknown.
- **Do nothing** — not neutral. Leaves a silent wrong value on the reference oracle.
- **Unifying `For Each`/`Try` into the branch CFG** — largest blast radius, empty consumer set.
- **Fixing `IsReducible` rather than deleting it** — a correct function with no caller prepared
  for its `false`.
- **Keeping the dead CFG surface "for later"** — it is unverified; keeping it preserves the trap.

## Revisit if

- **D1:** a second consumer of loop data appears outside `IROptimizer.cs` (a backend, the LSP).
  Then the header/latch/preheader/exit accessors become mandatory before that consumer ships —
  "the preheader resolves to the loop's own latch" is exactly what a shared accessor prevents.
- **D2:** a pass has (a) the `IsValueInvariant` class of defect closed, and (b) a differential
  harness running the 13 shapes across all four backends comparing VALUES, not exit codes.
  Re-register **one pass at a time**, each behind its own commit. LICM is the only candidate
  with a plausible path; `LoopUnrolling` and `LoopFusion` need their name-minting and
  block-removal defects fixed first and should be treated as unwritten.
- **D3:** profiling shows `For Each` is the dominant loop form in real BasicLang game code —
  then lowering it to branches in `IRBuilder` (so there remains exactly ONE loop representation)
  is worth a separate ADR with backend sign-off. Adding a parallel structured-loop analysis is
  not the answer.
- **D4/D5:** a pass needs a reducibility guard, or a dominance frontier — write it then, against
  the now-correct predicate, with a caller and a test.
