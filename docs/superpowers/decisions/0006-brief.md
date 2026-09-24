# BRIEF: The optimizer's shared kill vocabulary — what it cannot see, and who checks it

Date: 2026-09-24; HEAD `6168628`; builds on ADR-0004 D2 and ADR-0005 D2.
Every table was compiled AND run at `6168628` on 4 backends × 3 entry points: CLI, CLI `--optimize`, Release `.blproj`. Each table gives one verdict per backend, because the three entry points agreed in every cell. ⚠ A C++ Release `.blproj` runs the STANDARD pipeline (task #134). CSE is a standard pass, so the straight-line shapes in Q1 and Q3 are exercised by all three entry points; LICM (Q2) is aggressive-only and is not.

## The common thread

The optimizer has ONE shared answer to "what does this instruction write": `OptimizationPass.NamesWrittenBy` (IROptimizer.cs:180) plus `IsCallVisibleDestination` (:134). Its consumers are CSE's `Invalidate` (:1608), `IRVerifier`'s Invariant S′ (IRVerifier.cs:246), and, since `6168628`, LICM's `VariablesWrittenIn` (IROptimizer.cs:1836). Its doc comment (:164) already lists KNOWN GAPS. A write it misses is invisible to all three at once. So ADR-0005 D2's "Revisit if the verifier fires on a destination write CSE cannot see" can never fire as written: the verifier sees exactly what CSE sees.

## Questions

**Q1. Writes the vocabulary does not name.** Expected output is from each probe's `.exp` file; "✓" means it matches.

| probe | expected | C# | C++ | JavaScript | MSIL |
|---|---|---|---|---|---|
| A2b: `n = p + q : m = 0 : l(0) = p + q`, called as `Work(v, v)` (two `ByRef` params alias one variable) | `3,0` | ✓ | `0,0` | refused, BL7002 (ByRef) | `0,0` |
| A5b: `K = p + q : Me.K = 0 : l(0) = p + q` in a class method (`Me.K =` lowers to `IRFieldStore`, which is not in the vocabulary) | `3,0` | ✓ | `0,0` | run fails: `ReferenceError: K is not defined` | `0,0` |
| A6 (operand side): `a = K + q : Me.K = 10 : l(0) = K + q` | `12,3` | ✓ | `3,3` | `3,3` | `3,3` |
| A1: `a = p + q : clr = Sub() a = 0 : clr() : l(0) = p + q` | `3,0` | **`3,3`** | does not build | **`0,0`** | does not build (`Reference to undefined class 'Action'`) |

A1 shows TWO different defects. JavaScript's `l(0)` is wrong, and it is the vocabulary gap: the emitted JS is `a = ((p + q) | 0); … clr(); l[0] = a;`, so CSE merged `p + q` into `a` and read `a` after the lambda cleared it. C#'s `a` is wrong for an UNRELATED reason: the emitted C# lambda is `clr = () => { ; };`, so its `a = 0` is gone before any merge question arises. The cause is UNVERIFIED (C# lambda emission, or a pass deleting the store as dead) and is tracked separately. The related capture hazard CSE cannot see is documented at IROptimizer.cs:1653-1659 (task #122: it needs a capture set on `IRFunction`). The operand-side twin of A6 was measured in #111 step 13 NOT to be fixed by ADR-0005 D2's option B (always a fresh temp).
`IRBaseMethodCall` is not a call in the vocabulary: the call arm (:211-216) matches `IRCall`, `IRInstanceMethodCall` and `IRNewObject` only, and `IRBaseMethodCall` (IRNodes.cs:2040) carries `Arguments` but no `ByRefArguments`. **UNVERIFIED** as a wrong answer; no probe was built.
**Decision: how should the vocabulary cover writes through field stores, aliases and closures, and should the verifier keep sharing CSE's vocabulary or get an independent one?**

**Q2. S′ does not see values LICM moves out of a loop.** S′ is checked for a value with more than one static use, or a named destination with one (IRVerifier.cs:260). A value LICM hoists has ONE static use, which runs every iteration. So a hoist across a write the vocabulary misses is invisible to the verifier. At `6168628` the LICM probes L1–L4, L6 and L7 are correct on every backend that builds them. L5 is wrong (a lambda-captured local written in the loop: C++ prints 6 at every level; JavaScript prints 6 under `--optimize` and 12 without it; MSIL does not build it). Its cause is the closure gap in Q1/A1, not S′.
Measured BEFORE `6168628`: a verifier mutant using "≥ 1 use" had ZERO hits over the fast subset and flagged L1. Not re-run since. **UNVERIFIED** whether it is still zero-hit, and whether it flags anything now.
**Decision: should S′ (and the verifier) also cover a value used inside a loop that does not contain its definition?**

**Q3. A call's write to a class field read BARE.** MEASURED (probe below). This is the DEFAULT pipeline, so ordinary class code is affected:

```
Class Box
    Public K As Integer
    Sub Inc() : K = K + 10 : End Sub
    Sub Work(q As Integer)
        K = Seed(1) : Dim a As Integer = K + q : Inc() : l(0) = K + q   ' prints l(0) & "," & a
```

| probe | expected | C# | C++ | JavaScript | MSIL |
|---|---|---|---|---|---|
| Q3, `Inc()` | `13,3` | ✓ | `3,3` | `3,3` | `3,3` |
| Q3m, `Me.Inc()` | `13,3` | ✓ | `3,3` | `3,3` | `3,3` |

The emitted C++ is `a = K + q; Inc(); (*l)[0] = a;`. The mechanism: a bare field read lowers to an `IRVariable` with `IsGlobal = false` and `IsByRef = false`. CSE decides whether a call kills a record with `ReadsCallVisible` on the OPERANDS (:1661, a flag rule: `(IsGlobal && !IsConst) || IsByRef`, so false for `K`). On the DESTINATION it uses `IsCallVisibleDestination` (:1527, a declarations rule: a by-value parameter or a declared non-global local is private, and any other non-temp name, which includes a class member, is reachable). **So one function asks the same question of two things with two different rules, and the operand rule misses fields.** LICM uses the declarations rule for its reads (:1853), which is how `6168628` fixed L3. The verifier's call arm checks only the destination (IRVerifier.cs:284), never call-visible operands, so it did not flag Q3.
**Decision: which rule decides whether a call can write a value's operand?**

## Current state

- `NamesWrittenBy` names: an `IRAssignment` target; an `IRStore` address; a named value instruction (a rename); a call's `ByRef` arguments (`IRCall`, `IRInstanceMethodCall`).
- CSE kills a record when an instruction writes one of its reads or its destination, or when a call occurs and the record `ReadsCallVisibleStorage` (:1608).
- `ReadsCallVisible`'s own comment (:1653-1659) says it does not close by-reference lambda captures; the ADR-0004 D2 contract forbids `IsReplicable` from delegating to it.
- CSE's `ReadsCallVisible` exemption of `Const` globals is what keeps the 11 merges measured across the sample games (:1647-1651).

## Constraints already fixed

- ADR-0001 E1: a defining expression appears in the output once unless replicable.
- ADR-0004 D2: `IsReplicable` is structural and separate from `ReadsCallVisible`; value stability is an IR invariant owned by the pass that creates sharing, checked by a verifier.
- ADR-0005 D2: S′, where `Guard(v)` = replicable operands ∪ named destination; "assigned" uses the same kill vocabulary CSE applies; "a gap is flagged, not patched on one side".
- CLAUDE.md: shared resolver logic changes once, not per consumer.

## What breaks if wrong

Too narrow: C++, JavaScript and MSIL read a stale value and print a wrong number with no diagnostic (every WRONG cell above), while C# stays right by re-emitting text, so the oracle hides it. A verifier that shares the gap certifies it.
Too wide: CSE loses merges. Killing on every call would lose all 11 sample-game merges. LICM loses hoists; the `OverKillWitness` fixture pins one that must survive a call.

## Options the team already sees

**Q1:**
- **A:** extend the shared vocabulary once. `IRFieldStore` names its member (aimed at A5b and A6). A `ByRef`-parameter alias rule: a write through any `ByRef` parameter kills every `ByRef` parameter name (aimed at A2b). Neither is measured. `IRBaseMethodCall` becomes a call. Closure writes wait for a capture set built by `IRBuilder` (#122).
- **B:** as A for CSE and LICM, but give the verifier an INDEPENDENT, deliberately conservative write model, so a gap in the shared vocabulary can fire it.
- **C:** coarse kills with no names. Any `IRFieldStore`, any write through a `ByRef` parameter, and any call that may invoke a closure kills every record that reads or names a field, a `ByRef` parameter or a captured local.

**Q2:**
- **A:** widen S′ to count a use inside a loop that does not contain the value's definition as multi-use.
- **B:** keep S′ static, and add a separate LICM postcondition to the verifier: no name in a hoisted value's `Guard` is written anywhere in the loop.
- **C:** keep S′ static; LICM owns hoist correctness through the shared vocabulary (`6168628`), with no independent check.

**Q3:**
- **A:** the operand side uses the declarations rule too. `ReadsCallVisible` treats an `IRVariable` the function neither declares as a local nor takes by value as call-visible, the same as `IsCallVisibleDestination`.
- **B:** `IRBuilder` marks field reads (a flag on `IRVariable`, or lowering a bare field read to an explicit `Me` field access), and the flag rule checks the mark.
- **C:** kill every record on every call (measured to lose all 11 sample-game merges).

---
*Corrections by the orchestrator after review (2026-09-24), all re-measured or re-read at `6168628`:*
- *A1's C# cell said `3,0 ✓`; it measured `3,3`. A1 is now split into its two defects, and the emitted JS and C# were read to attribute each one.*
- *Q3 was left UNVERIFIED with an inference that `ReadsCallVisible`'s default arm "returns true for field accesses". A bare field read is an `IRVariable`, not a field access, so it never reaches the default arm. The probe was built and measured: wrong on C++, JavaScript and MSIL at every entry point.*
- *L5 was placed under Q2; it is a closure (Q1) defect.*
- *Q1's option "force materialisation" was replaced: it does not fix the operand side (A6).*
- *The verifier's destination-only call arm (IRVerifier.cs:284) was added, along with the #134 note beside the tables.*
