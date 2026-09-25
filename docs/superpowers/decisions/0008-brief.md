# BRIEF (v2, corrected and measured by the orchestrator): what S′ and the call kill may assume about a non-replicable value

S = session scratchpad. Every code fact cites `file:line` at `fc08b7f` (PR #96 head: ADR-0006 D2 landed).

## Context

- **S′** (ADR-0005 D2, as ADR-0006 D2 amends it): for a value `v` with dynamic use count > 1, no variable in `Guard(v)` is written between `v`'s definition and a use.
  - `Guard(v)` = the variables reached through `v`'s REPLICABLE operands ∪ `v`'s named destination.
  - Non-replicable operands (a call, a field load, a `ByRef` parameter, a non-Const global: `IRReplicability.IsReplicable`) are left out. ADR-0005 D2's stated reason: "a non-replicable operand is evaluated once and read back by name".
- **ADR-0006 D2** (merged in PR #96) counts a use as repeated when its block lies on a cycle that avoids the definition's block. That targets the shape LICM leaves: one hoisted value, one static use, run every iteration.
- **`ReadsCallVisible(value)`** (`IROptimizer.cs:401-431`) is the call arm of the kill vocabulary for a value's READS. CSE uses it for its records; CopyPropagation uses it for a fact's value (since #146). It walks pure operators; a variable is visible per `IsCallVisible`; a named operand is asked by its name; and every other value (a call, a field or indexer load, an allocation) returns TRUE (`:425-429`).

## How each backend materialises a value (MEASURED from code)

| Backend | Rule | Where |
|---|---|---|
| C# | Declares a local ONLY for a value with use count > 1 that is non-replicable and has no named destination. Every other value is INLINED at its use, including a single-use call ("for a used call, emit nothing" at the definition). | `CSharpBackend.cs:2957-2987`, `:2845-2851` |
| C++ | Every non-constant value without a named destination is a temp declared at function entry and assigned at its definition. | `CppCodeGenerator.cs:1806-1812`, `:2027` |
| MSIL | Every named non-variable value gets a local slot. | `MSILBackend.cs:2780-2788` |
| JavaScript | Every value instruction binds `const <name>` at its definition; later uses refer to the name. | `JavaScriptBackend.cs:1854-1872`, `:901-913` |

A value WITH a named destination is read back by the variable's name on every backend. For C#, see `IsNamedDestination` in `ComputeMaterialisedTemps`. So **C# is the only backend that re-evaluates a single-use value at its use, and only when the value has no named destination.**

## Q1 (#157): should `Guard(v)` include non-replicable operands for a value shared only DYNAMICALLY?

- ADR-0005 D2's reason for leaving out non-replicable operands ("evaluated once") holds for a value with MULTIPLE static uses, which C# materialises. It is FALSE for a value with ONE static use that repeats in a loop: C# inlines that one use and re-evaluates it every iteration, while C++, MSIL and JavaScript read the preheader value once.
- **MEASURED** (probe `S/adr6-d2/probes/L4r.bas`: `Sub Work(ByRef n)`, where a call in the loop bumps the global aliased as `n`; loop body `s = s + n * 2`; correct output 12). Mutate LICM so it hoists `n * 2` over the call:
  - C++ `-O` prints 6, and MSIL `-O` and Release print 6. C# prints 12, because it re-reads `n` inline. JavaScript refuses `ByRef` (BL7002).
  - The shipped verifier (D2) is SILENT, because `n` is outside `Guard(v)` (`S/adr6-d2/probes/matrix-L4r-licmcv.txt`).
  - With today's LICM there is no hoist, and all backends print 12 (`matrix-L4r-after.txt`).
- **Option E** (`S/adr6-d2/mutsrc/IRVerifier.E.cs`): a verifier-only change. For a value shared ONLY dynamically, `Guard(v)` also takes its non-replicable operands. MEASURED:
  - It FIRES on that wrong hoist in every `-O` cell, including C#'s, where the output happens to be right, and in MSIL Release (`matrix-E-licmcv.txt`).
  - It fires ZERO times with today's LICM, on L1–L7, the samples and the fast subset (`matrix-E.txt`; the ADR-0006 D2 note).
  - E changes no emitted code; it only decides what the verifier reports.
- **The framing question the evidence raises:** is S′ a "backends agree on the value" invariant, or a "the optimizer kept source semantics for a moved value" invariant?
  - For a REPLICABLE operand, D2 already catches a wrong hoist, because the operand is in Guard.
  - For a non-replicable one, the only thing that differs across backends is C#'s inlining.
  - A backend-side alternative ("B") exists: make C# materialise a dynamically-shared value the way C++, MSIL and JavaScript do. That removes the divergence, but a wrong hoist then becomes consistently wrong everywhere, and S′ without E stays silent.
- **Sub-question: confirm D2's CYCLE reading.** A definition on a branch the loop can skip counts as a repeated use (hand-built shapes (d)/(d2) fire). It fires zero times in the suite and samples. The natural-loop reading would exempt it. The ADR-0006 D2 implementation note records this as pending your confirmation.

## Q2 (#156): should `ReadsCallVisible` stop treating a non-replicable value as call-visible?

- **MEASURED cost of the current rule** (`S/cp146/analysis.txt`): #146 lost 74 CopyPropagation facts and gained 5.
  - 8 losses are correct kills (CP1).
  - 1 is correct under ADR-0006 D3.
  - About 60 are in 9 BCL value-type programs (`New DateTime(...)`, `.AddDays(...)`, `New TimeSpan`, `New Guid`, `New DateTimeOffset`), all through the value half.
  - Program output was unchanged in every case.
- **MEASURED today by the orchestrator** (a scratch mutant: `ReadsCallVisible`'s default arm returns FALSE; `S/arch-batch/probes/`):
  - Q2a/Q2b (`Dim x = Seed()` + a call + a use): the IR is identical to HEAD. The call lowers straight into `x`, so there is no copy and no fact to relax.
  - Copy facts arise only from `New …` and from instance-method calls (`t1 = b.Twice() ; x = t1`).
  - Q2d/Q2e: the relaxed rule DOES substitute across the intervening call (IR: `add t1 = b.Twice(), 1` for `add %x, 1`).
  - Output is still right on every backend that builds, 12/12 cells for Q2e. Q2d fails on JS/MSIL for lack of DateTime/TimeSpan, identically to HEAD.
  - Emitted code: Q2a–Q2c are byte-identical to HEAD on every backend. **Q2d/Q2e CHANGE on every backend**, and not for the better. The substituted value now has TWO uses (the copy `x = t1` and the substituted operand), so each backend keeps it as a separate temp computed before the call:
    - C#: `t1 = b.Twice(); x = t1; … t1 + 1`, with an extra `int t1` local, where HEAD emits `x = b.Twice(); … x + 1`.
    - C++: `t1 + 1`. JavaScript: `(t1 + 1)`. MSIL: `ldloc.3` for `ldloc.1`.
  - **So the propagations the current rule loses buy nothing measurable: no output change, and where the emitted code changes it gains an extra temp and a copy.** #146's 60 lost facts left output unchanged; their emitted-code effect was not diffed.
- **The tension with Q1, as measured for CopyPropagation:** a relaxed substitution is correct TODAY only because the substituted value always has ≥ 2 uses (the copy that created the fact, plus the substitution), and C# materialises a multi-use non-replicable value. If a pass ever deletes that copy while the substituted use remains, the value becomes single-use, and C# inlines the call AFTER the intervening call: a stale read or a reordered side effect.
  - DeadCodeEliminationPass is latent today (#118, "behind an unreachable guard"). INFERRED: fixing it could expose this.
- CSE is the other consumer of `ReadsCallVisible`. Not measured separately. INFERRED: a record over a non-replicable operand `t` means `t` has ≥ 2 uses, so C# materialises it; after the merge the use count can drop back to 1.

## Options as the evidence suggests them (not ranked)

- **Q1:**
  - A: keep the gap, documented.
  - E: widen `Guard(v)` for dynamically-shared values (verifier-only, 0 fires today).
  - B: C# materialises dynamically-shared values (a backend change; removes the divergence, not the wrong hoist).
  - E + B.
- **Q2:**
  - Keep: over-kill, with no measured cost in output or emitted code.
  - Relax the value half for non-replicable values: an extra temp per propagated use, safe only while the originating copy survives. It would need a guard or invariant against deleting that copy (INFERRED: checkable structurally).
  - Relax for CopyPropagation only, or for both consumers.
- **Cycle reading:** confirm or replace it with natural loops.

## UNVERIFIED

- CSE-side safety of a Q2 relaxation (no dedicated probe).
- Whether any live pass today can remove a copy that a substituted use relies on (DCE is latent; nothing else inspected).
- MSIL's reading of a named destination is inferred from its slot rule, not traced.

---
*Corrections by the orchestrator after review (2026-09-25), each re-measured. This file replaces the brief role's first draft:*
- *The draft said option E "fixes L4r on all backends". E is a verifier-only change: it fires on the wrongly hoisted L4r (every `-O` cell, C# included) and changes no output.*
- *The draft said relaxing `ReadsCallVisible` "recovers 74 CP facts". Of #146's 74 lost facts, 8 are correct kills and 1 is correct under ADR-0006 D3; about 60 come from the value half.*
- *The draft left the C# inlining rule, JavaScript's materialisation, and whether a relaxed substitution can go stale UNVERIFIED. All three were read from code or measured (probes Q2a–Q2e).*
- *The orchestrator's own first reading, that a relaxed substitution is read back through the named destination and leaves emitted code byte-identical, was wrong. The emitted-code diff shows an extra temp on every backend (Q2d/Q2e).*
