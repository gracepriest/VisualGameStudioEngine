# ADR 0014: Per-iteration loop-body `Dim` with copy-forward — the mechanism

- **Date:** 2026-09-28
- **Status:** Accepted; amended the same day by A1 and A2 (below), which replace D2's Contract
  and add to D1's Obligations.
- **Amended by:** ADR-0019 (#140) — D2's revisit-if only; see "Amendment A-140" at the end. #136 (C# writes a lambda body with the function-body emitter) — D2's revisit-if again; see "Amendment A-136" at the end. #228 (a sized array `Dim` in a loop body allocates at the statement) — D4 made true for array bounds; see "Amendment A-228" at the end. #229 (two `Dim`s of one spelling are two variables) — the implementation note "A name with one declaration only"; see "Amendment A-229" at the end.
- **Decided by:** the architect role, in a ruling (D1–D6) and an amendment (A1, A2) answering two
  findings the first implementation measured. Transcribed from both; nothing under the Decision
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
- *(Added by A2.)* LICM treats each `perIter(L)` variable as WRITTEN at L's body entry.
- *(Added by A2.)* The IR verifier checks invariant S″ after every pass on un-lowered IR.

**Revisit if:** L1–L7 do not print the VB column on C#, JS and MSIL (and C++ after #140), or
`BASICLANG_VERIFY_IR` complains about the list.

### D2: copy-forward timing; Exit and Continue

**Decision:** the copied-forward value is the one the previous iteration's variable holds at that
iteration's CONTINUE TARGET, snapshotted into the carrier there. This holds on every backend.
Exact-at-start is not required.

**A recorded, deliberate, all-backend divergence from VB:** it is observable only when a loop
condition, step or `MoveNext` invokes a lambda that WRITES the previous iteration's variable.

**Contract:** ⚠ *Replaced in full by A1* — the continue-target definition, the C# `goto` / JS
labelled-block / MSIL `leave` bullets for `Continue`, and "`Exit`: no carrier write" no longer
hold. These two bullets STAND:
- **Loops with an empty `perIter`** emit exactly what they emit today.
- **Carriers** are function-level and never reset, so copy-forward crosses the enclosing loop's
  iterations, as VB's hoisted local does.

**Revisit if:** probe L8 (`Do While f()`, where `f` is a lambda that increments the previous
iteration's captured `x`) prints differently on any two backends.

### A1: Exit and the carrier (replaces D2's Contract)

**Decision:** (E2). The carrier is written whenever the iteration is LEFT, by any route. The body
of a loop with a non-empty `perIter` is wrapped in a try/finally whose finally holds the carrier
writes. No route is enumerated.

**Because:**
- VB's copy-constructed closure carries the value the variable had when the iteration was last
  left.
- A finally is the one construct every backend already has that fires on every route, after every
  user `Finally`, in nesting order. For example, `Exit While` out of an inner `For` crosses both.
- E1 needs a per-backend list of exit routes and their nested combinations, which would be redone
  for #140. E2 reuses Finally machinery that user code already tests.
- The exception route comes free. What remains is D2's timing divergence only.

**Contract:**
- **C#:** `T x = carry_x; try { body } finally { carry_x = x; }`. JS is the same with `let`. The
  declaration goes before the try; the whole finally is inside the body block.
- **ClosureLowering (MSIL now, C++ under #140):**
  - After `env = new Env(parent); env.x = carry_x` (D1), the body goes inside the existing IR
    try/finally node, with an empty Catch list, whose Finally is `carry_x = env.x` for each entry.
  - It is emitted after the optimizer, so the optimizer never sees it; ADR-0010 D1 is kept.
  - On MSIL, `Exit` is a `leave`.
  - C++ under #140 gets the Exit and Return copies from its existing "carry every Finally it
    leaves" rule, and the exception route from its existing Finally handling. There is no new C++
    rule.
- **Retained divergence (D2's):** observable only when a lambda writes the previous iteration's
  variable between leaving the iteration and re-entering the loop.

**Revisit if:**
- E07 prints a non-VB column on any backend;
- a probe with `Exit` from inside a user `Try…Finally` that writes `x` prints a non-VB column on any
  backend;
- the verifier rejects a lowering-emitted try node.

### A2: code motion across the loop-body scope (amends D1's Obligations)

**Decision:** (S1).
- New verifier invariant **S″**: every IR reference (read or write) to a variable in `perIter(L)`
  lies in a block of L's body.
- LICM honours S″ by treating each `perIter(L)` variable as WRITTEN at L's body entry.
- No pass may move such a reference across a loop boundary.

**Because:**
- The variable is fresh each iteration, so a read outside the body reads nothing. S″ says that in
  the optimizer's own terms, without desugaring.
- S2 slides into option (C): once the copy-in is IR, the carrier writes must be too, or copy
  propagation folds `x = carry_x` to the default, and the optimizer then sees the whole mechanism.
- S3 breaks D6. Restricted to `perIter`, it IS the LICM half of S1.

**Contract:**
- For the optimizer, `perIter(L)` is `BodyLocals ∩ captureSet`, computed from the IR as it stands
  when the pass runs. Nesting falls out of this, because an inner body is inside the outer body.
- `BASICLANG_VERIFY_IR` checks S″ after every pass on un-lowered IR. The existing S′ stays as the
  post-lowering check of the same truth.
- ClosureLowering and #140: its `env.x = carry_x` and A1's finally both sit in the body, so S″
  holds by construction. Obligation: never emit the copy or the finally outside the body.

**Revisit if:**
- S″ fires from any pass other than LICM. That would mean the capture set is not monotone under
  optimization; freeze `perIter` before the optimizer instead.
- The Finding 2 program (K1/K3/K5) still throws on JS `-O`.

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
| ~~`try { body } finally { carry = x }` as the continue target~~ | ~~Also runs on `Exit` and on exceptions: harmless but imprecise, and it feeds the C++ Finally-copy rule for no reason.~~ **REVERSED by A1:** it is the mechanism. |
| A `CopiesForward` / `HasInitializer` flag on the entry | Unnecessary once the copy is unconditional, and wrong for `Dim x = x + 1`. |
| A flag or back-pointer on `IRVariable` | A second source of the same fact; the loop node is where every consumer acts. |
| ~~Rewriting each `Continue` as `carry = x; continue`~~ | ~~A `Finally` between the `Continue` and the loop head that writes `x` would be missed.~~ **Struck by A1:** moot, BasicLang has no `Continue`. |
| E1, per-route carrier writes (A1) | Enumerates routes per backend, misses exceptions, and must be redone under #140. |
| E3, recording E07 as a divergence (A1) | The owner's rule is VB's semantics, and a cheap VB-exact mechanism exists. |
| Exact-at-start copy (A1) | Needs the previous iteration's cell to be nameable, i.e. explicit closures on C#/JS (option B). |
| S2, an explicit copy in the IR (A2) | Option (C) by another door. |
| S3, all `BodyLocals` treated as written (A2) | Changes bytes for lambda-free programs. |
| S″ over `BodyLocals` rather than `perIter` (A2) | The same D6 breach as S3. |
| LICM hoisting to the body entry instead of refusing (A2) | That is not LICM; it would be a new pass, for no measured gain. |

## Implementation notes (implementer, measured; within the ruling)

- **The "loop node" is the loop's body entry block.** Only `For Each` is an IR node
  (`IRForEach`); a counted `For`, a `While` and both `Do` forms are branches between blocks
  IRBuilder names `forN.body` / `whileN.body` / `doN.body`, which is how every backend already
  recognises its loops. `BasicBlock.BodyLocals` on the body entry block carries D1's list for all
  five kinds (`IRForEach.BodyBlock` for a For Each); `IRLoops.Of(function)` reads each loop's body,
  continue block (`forN.inc`, `whileN.cond`, `doN.cond`, a For Each's end) and end block off the IR.
- **BasicLang has no `Continue` statement** (the lexer has no such token; `Continue For` is a
  parse error), which is why A1 strikes the `Continue` row as moot.
- **A1 as emitted.** C# and JavaScript: the declaration, then `try { body } finally { carry = x; }`
  inside the loop's braces; a counted For's step block is emitted AFTER the finally (a branch to it
  from inside the body emits nothing), so the step is never part of the try. C# peels the first
  iteration of a `Do … Loop While` into its own braces, which get their own try/finally.
  ClosureLowering: the body entry block keeps its identity and its `BodyLocals`, and holds the
  prologue plus the IR try node (IRBuilder's own shape for a Try statement); the original body
  moves to the try's entry block, and every branch that ends an iteration normally is retargeted to
  the try's continuation, which branches on to the step, the condition or the next `MoveNext`.
- **A1 needed #226 on MSIL.** A Try arm's blocks were collected by reachability, so an `Exit` inside
  a Try out of a loop ENCLOSING the Try dragged the loop's end block — and everything after the loop —
  into `.try { }` (InvalidProgramException). A1 puts every `Exit` of a wrapped loop inside a Try, so
  `MSILBackend.CollectRegionBlocks` no longer follows an `Exit` out of a loop whose body the arm's
  walk has not reached (the same rule as `IRLoops.BodyRegion`); the exit is then a `leave`.
- **A2 as implemented.** "An IR reference" is `IRLoops.VariableMentions` (assignment and variable-
  store targets, values renamed after a variable, an `x_addr` slot, every non-global `IRVariable`
  operand); "L's body" is `IRLoops.BodyRegion` (reachable from the body entry without passing the
  continue or end block or following an `Exit` out of an enclosing loop). `OptimizationPipeline.Run`
  checks S″ before the first pass (IRBuilder's output) and after every pass that changed something.
- **A name with one declaration only.** The IR is flat and every backend identifies a local by
  NAME, so two `Dim x` in one function (sibling loops, a loop and an `If`, a loop and a `For x`
  control variable, a local and a parameter) are one `x` in the emitted C#, JavaScript and IL. A
  `Dim` is recorded in `BodyLocals` only when its name is declared once in the function, is no
  parameter's, and is not a local of a lambda the function creates (C# does not declare a lambda's
  locals, so those bind to the creator's spelling). Such a program keeps the function-level
  behaviour; this is what keeps D1's "at most one loop" true by construction. It is also recorded
  only when NO mention of its name lies outside the loop's body (a class field read bare is a
  variable of the same spelling in the IR), so S″ holds for IRBuilder's output by construction and a
  breach is a pass's doing. The sibling same-name case (probe E16) is tracked separately as
  **#229**.
- **`IROptimizer` never removes a local** (no pass writes `LocalVariables`), so D1's "drops the
  entry when it drops the local" has no site today; the verifier's membership check (Invariant B)
  is what would catch a pass that started to.
- **Measured findings of the first implementation, and what answered them:**
  - *(Answered by A1.)* D2's `Exit` clause was observable when an ENCLOSING loop re-enters the loop:
    with the carrier written only at the continue target, the next entry copied forward from the
    last iteration that completed normally. `For r = 1 To 2 : For i = 1 To 3 : Dim x : x = x + 1 :
    fs.Add(Function() x) : If i = 2 Then Exit For : Next : Next` printed 1 2 2 3 on C#, JavaScript
    and MSIL; VB (vbc) prints 1 2 3 4, as does the same program without the lambda.
  - *(Answered by A2.)* LICM hoisted a pure read of a captured-but-never-written loop-body local out
    of a loop with no call (K1/K3/K5): JavaScript `-O` threw `ReferenceError: x is not defined`, and
    MSIL `-O` tripped Invariant S′.
  - D4's "array bounds" initializer is not an IR instruction: every backend allocates a sized array
    local where it declares it (function top), so `Dim a(2) As Integer` in a loop body is one array
    for the whole function with or without a lambda (VB re-creates it per iteration; probe E15). The
    carrier inherits that array; ADR-0014 changes nothing there. Tracked separately as **#228**.
    ⚠ FIXED by #228: see "Amendment A-228" at the end.

## Amendment A-140 (ADR-0019, 2026-10-01): L8 now prints the same on C++, JavaScript and MSIL

*Appended, not edited in place: D2's text above (a recorded, deliberate, all-backend divergence from VB, with
its revisit-if "probe L8 prints differently on any two backends") is unchanged.*

- C++ used to print 10 20 30 for L8 (capture by copy), then was refused by name (#170). #140 runs it through
  `ClosureLowering`: L8 and L8b print 11|21|31 on C++, which is **exactly D2's recorded output**, the one
  JavaScript and MSIL already print. (VB prints 11 22 33; the divergence is D2's, unchanged.)
- So D2's revisit-if is narrowed: C# (#136, its own multi-statement-lambda-body defect) remains the one backend
  that prints differently on L8; C++, JavaScript and MSIL agree. The pin is ONE cross-backend assertion —
  `PerIterationLoopBodyDimExecutionTests.L8_DoWhileConditionReassignsToALambdaThatWritesX_D2Divergence` pins
  JavaScript, MSIL and C++ together (and L8b the same), so a fix of D2's divergence flips all three at once.
- A1's "C++ under #140 gets the Exit and Return copies from its existing 'carry every Finally it leaves'
  rule" holds, with one addition the first run of the C++ lowering measured: `ComputeInlineRegion` takes
  MSIL's #226 rule (an `Exit` that leaves the region ends it), because the lowered per-iteration try's entry
  block is created after the loop's end block and the creation-order test alone let the walk follow the `Exit`
  into everything after the loop (a goto into the try).

## Amendment A-136 (#136, 2026-10-02): L8 now prints the same on ALL FOUR backends

*Appended, not edited in place: D2's text above (a recorded, deliberate, all-backend divergence from VB, with its
revisit-if "probe L8 prints differently on any two backends") is unchanged, and so is Amendment A-140.*

- C# printed 10 20 30 for L8, and that was not D2's divergence: it wrote `f`'s second incarnation as the EXPRESSION lambda
  `() => n < 3`. Its `x = x + 1` before the `Return` is an IRBinaryOp renamed `x`, which the old lambda emitter took for a temp
  and skipped, so `f` never wrote the captured `x`. #136 writes a lambda body with the function-body emitter
  (`CSharpBackend.EnterLambdaScope` / `GenerateLambdaBlockBody` / `ExitLambdaScope`; no IR change, no `ClosureLowering` on C#,
  ADR-0010 D1 stands), and C# prints 11|21|31 — **exactly D2's recorded output**, the one JavaScript, MSIL and, since #140,
  C++ print. (VB prints 11 22 33; the divergence is D2's, unchanged.)
- So D2's revisit-if is back to its original wording: A-140's narrowing ("C# remains the one backend that prints differently
  on L8") no longer holds. The pin is ONE cross-backend assertion over all four backends:
  `PerIterationLoopBodyDimExecutionTests.L8_DoWhileConditionReassignsToALambdaThatWritesX_D2DivergenceOnAllFourBackends`
  pins C# (standard and aggressive pipelines), JavaScript, MSIL and C++ together, so a fix of D2's divergence flips all four at once.
  L8b (which C# already printed as 11|21|31) is in the same fixture, as is `cl`, the non-loop control.
- The sentence under "A name with one declaration only" that says C# "does not declare a lambda's locals, so those bind to the
  creator's spelling" is no longer true: since #136 C# declares a lambda's own locals inside the lambda. The recording rule it
  gives is unchanged — a `Dim` is still recorded in `BodyLocals` only when it is not a local of a lambda the function creates.
  E20 is still #229 (C# now prints JavaScript's 50|2|2: `h()` is right, the loop's `y` is not).

## Amendment A-228 (#228, 2026-10-07): D4's array bounds are an IR instruction in a loop body

*Appended, not edited in place: D1–D6, A1, A2, A-140 and A-136 are unchanged. This is D4 as written ("an initializer (`= expr`,
`As New`, array bounds) is then the ordinary assignment the IR already emits"), made true for array bounds.*

- A sized local `Dim a(2) As Integer` with no initializer, inside a loop of its own function (D3's innermost-loop rule, the one
  `RecordBodyLocal` reads), lowers to `IRCall(IRBuilder.SizedArrayDimIntrinsic)` — no arguments, typed as the declared array,
  NAMED AFTER the variable as a ReDim's call is, `IsDimInitializer` set — at the statement. Each backend renders it with the
  helper its function-top declaration already uses (C#/C++ `SizedArrayInitializer`, JavaScript `LocalInitializer`, MSIL
  `TryArrayAllocation`), so every execution of the statement gets a fresh default-filled array, with or without a lambda.
- It is independent of `BodyLocals` and the capture set: a per-iteration variable (E15) is copied from its carrier and then
  overwritten by this assignment, exactly as D4 says; an uncaptured one (E15n) and a name declared twice (#229) are simply
  re-assigned. A call, so no pass folds, hoists or merges it, and S″ holds (it is a write inside the body).
- Byte identity: a sized `Dim` outside every loop emits nothing new; the function-top allocation stays on every backend (the
  first iteration allocates twice, harmlessly). LLVM, which allocates no sized array anywhere, renders it as a call to an
  undefined `@__BLDimArray`, as it does `@__BLReDim`.

## Amendment A-229 (#229, 2026-10-07): two `Dim`s of one spelling are two variables

*Appended, not edited in place: D1–D6, A1, A2, A-140, A-136 and A-228 are unchanged. This narrows the implementation note
"A name with one declaration only" above, whose sibling case (probe E16) it names as #229.*

- **The rule.** A local `Dim` (or `Dim (a, b) = …`) whose spelling, ignoring case, is already a local where it would meet it —
  in its own function, in a function enclosing it, or in a lambda created inside it — gets an IR name of its own,
  `{spelling}_{k}`: the first k that is no name the program can name (`SemanticAnalyzer.SpellingsInUse`: every symbol of every
  scope and every identifier the analyzer saw), no module-level IR name (the owner-qualified shared globals included) and no
  local it would meet (`IRBuilder.EmittedLocalName`). The earlier declaration keeps its spelling. Two sibling lambdas do not
  meet. The version stack stays keyed by the declared spelling (ADR-0013 D1); a reference reaches the variable through its
  declaration (`_localsByDeclaration`, by reference), and a counted `For` that drives or reuses such a local writes it back
  under its own name. The name is reserved by the push like any declaration (ADR-0018 D1), and nothing later recognises it
  by its shape.
- **What follows for this ADR.** Two `Dim`s are two names, so each is judged on its own by `AssignBodyLocals`: E16 and E20
  print vbc's answer on all four backends (E16 1|2|10|20, E20 50|1|2), and so does a captured pair in an If and its Else
  inside one loop body (#242's shape for two `Dim`s). On C++ E20 and t174 E12_later_sibling leave the by-copy fallback for
  `ClosureLowering`; on MSIL they no longer reach the N9 backstop. What can still share a spelling is a `Dim` with a
  declaration of another kind (a counted `For` control variable declared later, a parameter, a For Each or Catch variable):
  that residue keeps the function-level behaviour.
- **ADR-0013's rejected "uniquify IR names per declaration".** That row rejected renaming every declaration, which changes
  every program's bytes and every user-visible name. This renames only a second declaration of a spelling, so a procedure
  without such a pair is byte-identical; the second variable does appear under its IR name in the generated source and the
  debugger.
- **Measured** (Linux; probes `S/t229/probes` p01–p20 with vbc expectations, C#/C++/JavaScript/MSIL x CLI / `--optimize` /
  Release `.blproj`): 219 of 240 cells print vbc's answer, from 84; the rest are p13 (a `For x` with no `As` after two
  sibling `Dim x` of different types reuses the second's storage: compile failure before and after, MSIL now a run failure)
  and p15 (#231's hiding shape, which vbc refuses: 2|2 became 2|1, a reference reaching its own declaration). Byte compare over
  the t124 corpora + the t228/t229 probes, 5 backends x CLI / `-O`: 246 of 11,360 cells differ, all in the 27 programs with a
  same-named local pair, 0 outside; 6 return-code changes (MSIL E20 / E12_later_sibling / p08 now build); 0 verifier fires.
