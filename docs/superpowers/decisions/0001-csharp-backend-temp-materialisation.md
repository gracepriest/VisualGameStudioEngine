# ADR 0001: C# backend temp materialisation

- **Date:** 2026-09-21
- **Status:** Accepted
- **Decided by:** architect role, ruling made by Opus (see Provenance below) — the
  pinned architect model, Fable 5.1, has been rate-limited (HTTP 429) for this
  entire project.
- **Brief:** [`0001-brief.md`](0001-brief.md) — preserved in full beside this
  ADR. The architect answers only from the brief, so the brief is the entire
  basis of this ruling and an audit needs it.

**Amended by [ADR-0004](0004-family-111-rulings.md)** — fills the `IsReplicable`
whitelist's silent cases (locals, globals, ByRef, value stability), adds
Invariant S and the IR-verifier obligation, sets the temp-namespace contract
(reserve, don't rename) including the `ICodeGenerator.cs:203` bullet, orders the
`AlgebraicSimplification` gate after backend materialisation and MSIL coercion,
and carves a fenced exception for the D1 C++/MSIL interface-accessor batch.

## Question

Should the C# backend keep inlining IR temps into expressions unconditionally,
or materialise a temp as a declared local when its use count is greater than
one? Is a value with use count > 1 ever allowed to stay inlined, and is the
C++ backend (which shares the same bare-identifier policy via
`ICodeGenerator.cs:203`) in scope of this ruling now or deferred?

## Decision

**Evaluate-once.** A value whose definition is not in a closed *replicable*
whitelist must appear in emitted C# exactly once; if its use count is greater
than 1 **and it is not replicable** it becomes a declared local. Use count
<= 1 keeps today's inlining.

Both conditions must hold, so the full truth table is:

| | `IsReplicable` | not `IsReplicable` |
|---|---|---|
| **use count <= 1** | inline | inline |
| **use count > 1** | inline (duplication is harmless) | **declared local** |

(The `&& !IsReplicable(v)` conjunct is stated in the Contract's
`ShouldEmitInstruction` arm below and in E1's "unless `IsReplicable(I)`"; it is
repeated here because a summary that drops it reads as though a parameter
referenced five times would be materialised, which is not the ruling.)

C++ is bound by the same invariant, but its implementation is deliberately
deferred — this ruling does not authorize touching `CppCodeGenerator.cs`.

## Provenance

The `architect` role is pinned to Fable 5.1, which has been rate-limited
(HTTP 429) for the entire duration of this project. This is the first
architect ruling that has ever completed here. It was produced by **Opus**
operating under the role's normal constraints: no search tools, a two-file
Read cap, answers drawn only from the written brief. A reader relying on this
ADR six months from now should know which model actually decided, and that it
was not the pinned one.

## Because

- The C# backend is the reference oracle: ~6,800 tests assert the other four
  backends against what C# emits and runs. The oracle must not multiply
  observable effects. This has already happened by hand once and is
  permitted by policy today: `EmitRight` interpolated its receiver into both
  halves of `str.Substring(str.Length - n)`, so `Right(Tag(), 2)` emitted
  `Tag().Substring(Tag().Length - 2)` and called `Tag()` twice — fixed by
  binding the receiver once. `ShouldEmitInstruction`'s current inline-always
  branch for use count >= 1 (`CSharpBackend.cs:2871-2886`) permits the same
  class of defect by policy, not by accident.
- Triggering the materialisation on use count, rather than on block
  membership (the JavaScript backend's `Bound()` strategy), confines emitted-
  text churn to programs whose current output is already wrong — a value used
  more than once that is not provably replicable.
- The implementer cannot prove purity in general, so the gate must be a
  whitelist that defaults to "no" (`IsReplicable`), not a purity analysis or
  attribute. A wrong "pure" verdict from an analysis is silent
  miscompilation; a whitelist that defaults to false is at worst
  over-conservative.

## Premise status: MEASURED AND CONFIRMED (2026-09-21, HEAD 13411bf)

This decision was ruled on while its motivating premise was still unverified.
It has since been measured, and **the premise holds — but not through CSE.**
Both halves are recorded here because the ADR was written the other way round.

**CSE does NOT merge side-effecting calls.** Its only candidate arm is
`if (inst is IRBinaryOp binaryOp)` (`IROptimizer.cs:1191`), so an `IRCall` is
never a CSE candidate; and its key
`$"{binaryOp.Operation}_{binaryOp.Left.Name}_{binaryOp.Right.Name}"` (`:1193`)
cannot collide when an operand *is* a call, because every anonymous call result
gets a unique SSA temp name. Every multi-use value CSE does produce has
`IRVariable`/`IRConstant` operands — i.e. is replicable. **The shared-
`IsReplicable` obligation on CSE below is therefore defence in depth, not the
primary fix.**

**The hazard is real and was measured elsewhere.** `AlgebraicSimplificationPass`'s
`2 * x -> x + x` arm (`IROptimizer.cs:2456-2466`) writes the *same operand
object* into both slots with **no gate on what the operand is**. On
`Dim r As Integer = 2 * Tag()` it produces `Add(t0, t0)` where `t0` is the
`IRCall` — a use count of 2 on a side-effecting call. C#'s inline-always then
emits `r = Tag() + Tag();` and the program prints twice. **C# is the only one
of the four backends that is wrong**; C++, JS and MSIL all materialise
`t0 = Tag();` and print once. Reproduced on BOTH entry points (the single-file
CLI with `--optimize`, and a Release `.blproj` through `CompileProjectFiles`);
note `ProjectFile.cs:44` defaults `OptimizationsEnabled` to **true**.

So E1 is violated by the oracle, in a one-line program, today. This decision
is the primary fix for that instance.

⚠ Worth recording, because it argues the opposite way: that arm carries a
comment reasoning that the rewrite is "sound on both fronts" — IEEE 754
rounding and integer-overflow wrapping. Both fronts are about the **value**.
Nothing in it considers how many times `x` is *evaluated*. A value-preserving
rewrite is not automatically an effect-preserving one, and that is the whole
content of E1.

⚠ **A trap in the other direction**, measured at the same time. CSE has no
SSA/version check and will merge across a redefinition of an operand
(`p + q`, then `p = …`, then `p + q` again). There, C# is the backend that is
**right** — and it is right precisely *because* inline-always re-emits the
expression text rather than honouring the IR's bad merge, while C++, JS and
MSIL all print the wrong answer. Under this ADR that binop's use count is 1, so
it is not materialised and C# stays correct; but an implementer who "fixes"
materialisation more aggressively than E1 requires would make the oracle wrong
too. Do not widen past the contract.

## Contract

- **E1** — for every reachable IR instruction `I`, its defining expression
  text appears in the output exactly once unless `IsReplicable(I)`.
- **E2** — `GetValueName(v)` may only be called for a `v` already
  materialised as a declared local. Every other call site is a latent
  CS0103. The `IRForEach` (`CSharpBackend.cs:4097`) and `IRIndexerStore`
  (`:3859-3862`) defects fall out of E2 — they are not a separate task.
- `IsReplicable` is one predicate, structural, whitelist-only, default
  **false**. Replicable: literals, `Me`, parameter references, already-
  materialised temp names, pure operators over replicable operands. Never
  replicable: calls, instance-method calls, property gets, indexer loads,
  field loads, allocations.
- `ShouldEmitInstruction` gains the missing arm:
  `GetUseCount(v) > 1 && !IsReplicable(v)` → declared local.
- `GetOperands` must be **total** over IR node kinds; its default case must
  ASSERT, not return empty. A missing arm yields a silently-zero use count,
  which is a wrong answer, not an absent feature.
- Temp identifiers must live in a namespace users cannot enter (a prefix the
  lexer rejects, or uniquified against the function's declared names). Not
  optional — materialisation is what puts them in scope.

## Obligations

- ⭐ **`AlgebraicSimplificationPass`'s `2 * x -> x + x` arm
  (`IROptimizer.cs:2456-2466`) must not fire unless the operand is
  replicable.** This is the MEASURED instance (see Premise status) and the
  fix is one line: the same predicate, a third consumer. Duplicating an
  operand object is duplicating an evaluation.
- `IsReplicable` is shared with `CommonSubexpressionEliminationPass`
  (`IROptimizer.cs:1162`, registered `:1542`): CSE may only merge
  instructions it would call replicable. Merging two effecting calls
  *deletes* an effect, which no backend fix can repair. ⚠ Measured
  redundant today — the `:1191` candidate gate means CSE never sees a call —
  so this bullet is defence in depth against that gate being widened, not a
  live defect. One predicate, now three consumers — the same rule as
  `ModuleResolver`/`ModuleTypeWalker` in `CLAUDE.md`.
- **Fixture churn: yes, this churns emitted C#, and it is worth it.** Bounded
  to fixtures whose program contains a multi-use non-replicable value. Where
  `CSharpLoopExitTests.cs` strings change, update them — do not delete them;
  the three mutant kills they currently provide must survive the edit.
- C++ is bound by E1/E2 but implementation is deferred. Do not touch
  `CppCodeGenerator.cs` in this work: moving oracle and subject together
  destroys diff attribution, and C++ materialisation must first settle
  `auto` vs `shared_ptr<T>` under the two-layer std model.
- `ICodeGenerator.cs:203` stays as-is; it is a name source, not a policy.

## Rejected

- **Option A — minimal fix** (add the `IRForEach` arm to `GetOperands` and
  point the two known consumers at `EmitExpression`) alone — fixes the
  symptom using the exact mechanism that produced the `Right` bug.
- **Option B — unconditional materialise-everything** (MSIL-style) —
  maximal fixture churn, no correctness gain over the use-count trigger.
- **Option C — JavaScript's `Bound()` strategy** (name it if it is a
  statement in the block, else inline) — block membership is a proxy for
  the wrong property; a multi-use value absent from `block.Instructions`
  still duplicates.
- **Purity by analysis or a `[Pure]` attribute** — unprovable in general, and
  a wrong "pure" verdict is silent miscompilation rather than a compile
  error.
- **Fixing C++ in the same change** — destroys regression attribution.

## Revisit if

~~A measured program shows CSE merging a side-effecting call — then CSE's gate
is the primary fix and this decision becomes defence in depth rather than the
primary mitigation.~~ **SATISFIED 2026-09-21, and resolved the other way:** CSE
does not merge calls (gate quoted in Premise status), but
`AlgebraicSimplificationPass:2456-2466` duplicates an operand object without a
gate, which is the same hazard by a different route. This decision stands as
the primary fix; the pass-side gate is now an Obligation above rather than an
alternative to it.

Revisit if a value-duplicating or code-motion rewrite is added to any pass
without an effect gate. The lesson from the measured instance is that the arm
was reviewed for *value* preservation and shipped; evaluation count was never
considered. Any new arm that writes one operand object into two slots needs
`IsReplicable`, not a soundness argument about arithmetic.
