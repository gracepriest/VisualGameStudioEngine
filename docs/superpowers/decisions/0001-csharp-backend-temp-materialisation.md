# ADR 0001: C# backend temp materialisation

- **Date:** 2026-09-21
- **Status:** Accepted
- **Decided by:** architect role, ruling made by Opus (see Provenance below) — the
  pinned architect model, Fable 5.1, has been rate-limited (HTTP 429) for this
  entire project.
- **Brief:** [`0001-brief.md`](0001-brief.md) — preserved in full beside this
  ADR. The architect answers only from the brief, so the brief is the entire
  basis of this ruling and an audit needs it.

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

## Premise status (unverified)

This decision is motivated by the risk that `CommonSubexpressionEliminationPass`
(`IROptimizer.cs:1162`, registered into the default pipeline at `:1542`)
produces multi-use values out of side-effecting calls, which the current
inline-always policy would then evaluate more than once. **As of this
ruling, nobody has measured whether CSE actually fires on a side-effecting
call in a program that can be written today.** The hazard is structurally
real — the code has no use-count > 1 branch today, and CSE runs by default —
but it is not yet demonstrated end to end. A measurement is queued but has
not landed; an earlier claim that a characterization agent was already
measuring this was inaccurate — no such measurement had actually been
dispatched. Until a measurement lands, treat this premise as open, and see
Revisit if below.

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

- `IsReplicable` is shared with `CommonSubexpressionEliminationPass`
  (`IROptimizer.cs:1162`, registered `:1542`): CSE may only merge
  instructions it would call replicable. Merging two effecting calls
  *deletes* an effect, which no backend fix can repair. One predicate, two
  consumers — the same rule as `ModuleResolver`/`ModuleTypeWalker` in
  `CLAUDE.md`.
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

A measured program shows CSE merging a side-effecting call — then CSE's gate
is the primary fix and this decision becomes defence in depth rather than
the primary mitigation.
