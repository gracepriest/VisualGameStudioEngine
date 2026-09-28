# ADR 0014: Per-iteration loop-body `Dim` with copy-forward — the mechanism

- **Date:** 2026-09-28
- **Status:** Accepted
- **Decided by:** the architect role. Transcribed from the ruling; nothing under the Decision
  headings is editorialised.
- **Supersedes:** the ADR-0010 clauses named in D5.
- **Semantics:** set by the owner ("fix #172": VB's per-iteration loop-body `Dim`, with
  copy-forward, on every backend).
- **Brief:** the orchestrator's `architect-brief.md` for task #172 (session scratchpad, not kept
  in the repo).
- **Task:** #172.

## Question

VB gives a variable declared by `Dim` inside a loop body a FRESH instance per iteration, as far as
a lambda that captures it can observe, and a new iteration's variable starts with the previous
iteration's final value (copy-forward). Measured on master `4500ee3b` (probes L1–L7, lambdas
invoked by index):

| Probe | Shape | VB | C# | JS | MSIL | C++ |
|---|---|---|---|---|---|---|
| L1 | `For i: Dim x = i*10: fs.Add(Function() x)` | 10 20 30 | 30 30 30 | 30 30 30 | 30 30 30 | ✓ |
| L2 | `Dim x` with no initializer, `x = x + i`, captured | 1 3 6 | 6 6 6 | 6 6 6 | 6 6 6 | ✓ |
| L3 | same as L2, NO lambda | 1 3 6 | ✓ | ✓ | ✓ | ✓ |
| L4 | While and Do loops with a body `Dim` | 101 102 103 7 14 | last value repeated | same | same | ✓ |
| L5 | nested loops, a body `Dim` at each level | 11 21 12 22 | 22 ×4 | same | same | ✓ |
| L6 | For Each with a body `Dim` | 16 25 | 25 25 | same | same | ✓ |
| L7 | a lambda WRITES the iteration-1 variable after the loop | 101 2 | 102 102 | same | same | **1 2** (writes lost, #140) |

The IR is flat (a loop-body `Dim` is a function-level local), C# and JavaScript use native
closures over function-top declarations, MSIL uses `ClosureLowering` (ADR-0010), and C++ captures
by copy. Where do per-iteration identity and copy-forward live, for all four backends?

## Decision

### D1: where the per-iteration fact lives and who acts on it

**Decision: Option (A).** The IR stays flat. Every loop node records which function-level locals
are declared in its body, and each backend honours that list only for the locals the capture set
says are captured. Copy-forward is emitted by the backend or the lowering, never desugared in the
IR.

**Contract:**
```
// BasicLang/IRNodes.cs — on every loop node (For, For Each, While, Do pre/post)
List<IRVariable> BodyLocals;   // locals whose Dim statement executes in THIS loop's body (innermost loop only). Empty => inert.
// consumers:  perIter(loop) = loop.BodyLocals ∩ captureSet(function)
```
- **Where the variable lives:** it stays in `IRFunction.LocalVariables`. There is no scope node,
  and no back-pointer on `IRVariable`, so there is one source of the fact.
- **C# and JS:** a variable in `perIter` of any loop is NOT declared at function top.
  - It is declared at the top of that loop's body, from its carrier: `T x = carry_x;` in C#,
    `let x = carry_x;` in JS.
  - `carry_x` is an uncaptured, function-top local of the same type. It is default-initialised and
    never reset.
- **ClosureLowering (MSIL now, C++ under #140):**
  - A loop gets ONE per-iteration environment if and only if `perIter(loop)` is non-empty OR its
    declaring `For Each` variable is captured. The environment holds both.
  - The environment is created at the top of the body. Its parent is the innermost enclosing
    environment; the chain rule is unchanged.
  - The top of the body does `env.x = carry_x` for each `perIter` entry.
  - `carry_x` is a plain local of the creator function, never an environment field.
- **C++ before #140:** ignores `BodyLocals`. By-copy capture already yields L1–L6; L7 is #140's
  defect, not #172's.

**Obligations:**
- `IRBuilder` populates `BodyLocals`.
- The IR verifier checks that every entry is in the function's `LocalVariables`, and that each
  variable appears in at most one loop.
- `IROptimizer` drops the entry when it drops the local.
- The LSP is untouched, because the fact is post-semantic.

**Revisit if:** L1–L7 do not print the VB column on C#, JS and MSIL (and C++ after #140), or
`BASICLANG_VERIFY_IR` complains about the list.

### D2: copy-forward timing; Exit and Continue

**Decision:** the copied-forward value is the one the previous iteration's variable holds at that
iteration's CONTINUE TARGET, snapshotted into the carrier there. This holds on every backend.
Exact-at-start is not required.

**A recorded, deliberate, all-backend divergence from VB:** it is observable only when a loop
condition, step or `MoveNext` invokes a lambda that WRITES the previous iteration's variable.

**Contract:**
- **The continue target** is the single point reached both by normal body completion and by every
  `Continue` of that loop:
  - AFTER any `Finally` the `Continue` leaves;
  - BEFORE the step, the condition or `MoveNext`.

  The carrier writes (`carry_x = x` / `carry_x = env.x`) sit there, one per `perIter` entry.
- **So a `Continue` in a loop with a non-empty `perIter` may not compile to a bare `continue`:**
  - C#: `goto L_cont;`, with `L_cont:` immediately before the carrier writes. A `goto` out of a
    `try` runs the finally.
  - JS: the body goes inside a labelled block, and the `Continue` becomes `break L_cont;`.
  - MSIL: the existing continue label; `leave` runs the finally first.

  This also covers `Continue While` jumping out of an inner `For`.
- **Loops with an empty `perIter`** emit exactly what they emit today.
- **`Exit`:** no carrier write, because nothing after the loop can name the variable.
- **Carriers** are function-level and never reset, so copy-forward crosses the enclosing loop's
  iterations, as VB's hoisted local does.

**Revisit if:** probe L8 (`Do While f()`, where `f` is a lambda that increments the previous
iteration's captured `x`) prints differently on any two backends.

### D3: the scope of "loop body"

A `Dim` belongs to the innermost enclosing loop of its statement:
- looking through any non-loop block (`If`, `Select`, `Try`, `With`, `SyncLock`);
- a `Dim` in a nested loop belongs to the inner loop;
- a `Dim` inside a lambda body belongs to the lambda's own loops, never the creator's.

### D4: initialised vs uninitialised

The mechanism does not distinguish them. EVERY `perIter` variable is copied from its carrier at the
top of the body. An initializer (`= expr`, `As New`, array bounds) is then the ordinary assignment
the IR already emits, and it overwrites the copy. So `Dim x = x + 1` accumulates, as in VB, and
C#'s definite assignment is always satisfied.

### D5: interaction with ADR-0010, and the clauses superseded

ONE per-iteration environment per loop iteration. When a declaring `For Each` variable and body
locals are both captured, they share it; there is never a second environment chained to the first.
The chain stays: function env ← outer loop iteration env ← inner loop iteration env.

ADR-0010 clauses superseded:
1. **D2's L15 clause** ("L15 6|6|6 … the pass must not reset or copy-forward `y`") → L15 is 1|3|6
   on every backend, and the pass copies forward.
2. **"The IR is flat, and a loop-body `Dim` is function-level"** → amended: still flat, still in
   `LocalVariables`, but every loop node records its `BodyLocals`.
3. **"One per-iteration environment per DECLARING `For Each` whose variable is captured"** →
   generalised to any loop kind with a captured `For Each` variable OR a captured body local, with
   one environment for both.

Kept unchanged:
- D1 (the optimizer and verifier see un-lowered IR);
- L14 (a counted `For` control variable is one variable for the whole loop);
- the parent-chaining rule.

### D6: byte identity

Achieved, to the precision of the capture set. An empty `perIter` on every loop means every
backend takes its existing path unchanged. #122's over-approximation can mark an uncaptured
variable; that changes bytes but not behaviour.

**Revisit if:** a program with no lambda in the function changes output on any backend.

## Rejected

| Alternative | Why rejected |
|---|---|
| (B) every backend through `ClosureLowering` | C# and JS would lose native closures and readable output for a property block scope already gives them for free. |
| (C) an IR-level per-iteration cell or box | Changes what the optimizer sees, contradicting ADR-0010 D1, and duplicates `ClosureLowering`'s environment on MSIL and C++. |
| Exact-at-start copy on MSIL/C++ only | VB-exact on two backends but a per-backend divergence on the other two, which is what #172 exists to remove. |
| `try { body } finally { carry = x }` as the continue target | Also runs on `Exit` and on exceptions: harmless but imprecise, and it feeds the C++ Finally-copy rule for no reason. |
| A `CopiesForward` / `HasInitializer` flag on the entry | Unnecessary once the copy is unconditional, and wrong for `Dim x = x + 1`. |
| A flag or back-pointer on `IRVariable` | A second source of the same fact; the loop node is where every consumer acts. |
| Rewriting each `Continue` as `carry = x; continue` | A `Finally` between the `Continue` and the loop head that writes `x` would be missed. |

## Implementation notes (implementer, measured; within the ruling)

- **The "loop node" is the loop's body entry block.** Only `For Each` is an IR node
  (`IRForEach`); a counted `For`, a `While` and both `Do` forms are branches between blocks
  IRBuilder names `forN.body` / `whileN.body` / `doN.body`, which is how every backend already
  recognises its loops. `BasicBlock.BodyLocals` on the body entry block carries D1's list for all
  five kinds (`IRForEach.BodyBlock` for a For Each); `IRLoops.Of(function)` reads each loop's body,
  continue block (`forN.inc`, `whileN.cond`, `doN.cond`, a For Each's end) and end block off the IR.
- **BasicLang has no `Continue` statement** (the lexer has no such token; `Continue For` is a
  parse error). D2's continue target is therefore reached by normal body completion only, and the
  `goto L_cont` / labelled-block / continue-label rewrites have nothing to rewrite. The carrier
  write sits before a counted For's step block and at the end of every other loop's body (C#/JS),
  and before each body branch that ends an iteration normally (ClosureLowering).
- **A name with one declaration only.** The IR is flat and every backend identifies a local by
  NAME, so two `Dim x` in one function (sibling loops, a loop and an `If`, a loop and a `For x`
  control variable, a local and a parameter) are one `x` in the emitted C#, JavaScript and IL. A
  `Dim` is recorded in `BodyLocals` only when its name is declared once in the function, is no
  parameter's, and is not a local of a lambda the function creates (C# does not declare a lambda's
  locals, so those bind to the creator's spelling). Such a program keeps the function-level
  behaviour; this is what keeps D1's "at most one loop" true by construction.
- **`IROptimizer` never removes a local** (no pass writes `LocalVariables`), so D1's "drops the
  entry when it drops the local" has no site today; the verifier's membership check (Invariant B)
  is what would catch a pass that started to.
- **Measured, for the architect (not decided here):**
  - D2's `Exit` clause is observable when an ENCLOSING loop re-enters the loop: the carrier is never
    reset, so the next entry copies forward from the last iteration that completed normally, not
    the one that exited. `For r = 1 To 2 : For i = 1 To 3 : Dim x : x = x + 1 : fs.Add(Function() x)
    : If i = 2 Then Exit For : Next : Next` prints 1 2 2 3 on C#, JavaScript and MSIL; VB (vbc)
    prints 1 2 3 4, and the same program without the lambda prints 1 2 3 4 on every backend.
  - D4's "array bounds" initializer is not an IR instruction: every backend allocates a sized
    array local where it declares it (function top), so `Dim a(2) As Integer` in a loop body is one
    array for the whole function with or without a lambda (VB re-creates it per iteration). The
    carrier inherits that array; ADR-0014 changes nothing there.
