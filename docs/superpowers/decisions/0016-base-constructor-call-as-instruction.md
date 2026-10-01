# ADR 0016: `MyBase.New(...)` is an instruction — `IRBaseConstructorCall`

- **Date:** 2026-09-29
- **Status:** Accepted
- **Amended by:** ADR-0019 (#140) — D3's C++ arm only; see "Amendment A-140" at the end. D1, D2, D4, D5 and the C#, JavaScript and MSIL arms of D3 stand.
- **Decided by:** the architect role, in one ruling (D1–D5), with the orchestrator's binding
  clarifications C1–C6, and a binding amendment to D3's C++ arm (adopting "W2", after the first
  landing measured the rule it replaces). Transcribed from all three; nothing under the Decision
  headings is editorialised. Implementation notes and measurements are marked as such.
- **Brief:** the orchestrator's `architect-brief.md` / `implementer-brief.md` for task #170
  (session scratchpad, not kept in the repo).
- **Task:** #170, which absorbs #240 (D2). Unblocks #163 (DCE removal) and #140 (C++ closures).

## Question

A lambda written in `MyBase.New(...)`'s arguments lived outside the IR instruction stream, in
`IRConstructor.BaseConstructorArgs`, a list no block held. The capture scan, `UsesOf`, DCE, CSE
and the verifier never saw it, so a lambda there that wrote a captured parameter appeared to
capture nothing, and every backend was wrong: C# `CS0103 '__lambda_0'` (even read-only), C++ a
silent wrong answer, JavaScript a TDZ `ReferenceError` (`const p = …`), MSIL a refusal. A COMPUTED
argument (#240: `MyBase.New(p + 1)`, `MyBase.New(Twice(p))`) was the same fault: its temp was
evaluated into the entry block while the call site lived in the list — C# `CS0103 't0'`,
JavaScript and MSIL refused, only C++ right (ADR-0015 E11). And the front end accepted any
reference to the object under construction there (VB's BC31095/BC31096). How should the base
call be represented, lowered per backend, diagnosed and verified?

## Decision

### D1: Representation — the base call becomes an instruction (option B)

- `IRConstructor.BaseConstructorArgs` is deleted.
- Argument evaluation is ordinary instructions in the constructor's entry block, terminated by a
  new `IRBaseConstructorCall` instruction.
- C# renders that instruction's operands as *expressions* into `: base(...)`. It never emits the
  prologue as statements.

**Contract:**
- `sealed class IRBaseConstructorCall : IRInstruction { IReadOnlyList<IRValue> Args; }` — no
  result value, `HasSideEffects = true`, never removable and never reordered (a barrier like
  `IRThrow`), `UsesOf` returns `Args`.
- Built at both current sites (IRBuilder's `Visit(ConstructorNode)` and the synthesized
  constructor), **only for an explicit `MyBase.New(...)`**; the implicit parameterless base call
  stays backend-side exactly as today. (ASSUMPTION in the ruling: generalising to "always
  present" is deferred.)
- The **prologue** is the instructions that precede `IRBaseConstructorCall` in the entry block.
  Every prologue instruction is a value-producing expression (no store, no branch or phi, no
  throw, no `Me` as a value); every use of a prologue-defined value is another prologue
  instruction or the base call; field-initializer stores come after the base call.
- C#: `: base(e1..en)`, each `ei` rendered by substituting single-use prologue definitions — a
  parameter by its name, a constant as a literal, a temp by its (parenthesised) defining
  expression, a lambda creation as an inline C# lambda through the existing body emitter. A
  prologue spanning more than one block is refused by name, on C# only.
- JavaScript: the prologue is emitted as statements before `super(...)`.
- MSIL: the prologue is emitted before `call Base::.ctor`.
- C++: the instruction is emitted in place inside `ctor_` (ADR-0015 unchanged).

**Obligations:** delete ClosureLowering's clone of the list and its base-args refusal, the
JavaScript refusal and the MSIL refusal; `CppCapabilityChecker`'s D2a purity check reads the
prologue; `ScanForLambdas`, `UsesOf`, DCE, CSE/CopyProp and `IsCallVisible` need no new arms
(confirmed by test, not by adding code).

**Revisit if:** a backend needs the base call somewhere other than "after its own argument
evaluation, before the body".

### D2: Scope vs #240

#170 absorbs #240: one fault, one fix (D1). Close #240 as a duplicate; W1 and W2 become #170
acceptance probes. **Revisit if** the C# renderer needs a shape for #240 the lambda case does
not — then split, keeping D1's IR.

### D3: Per-backend lowering of a base-args lambda

C#, JavaScript and MSIL lower it fully and match VB. C++ refuses it by name, independent of
position, until #140 — under the AMENDED rule below.

**Contract (C#, JavaScript, MSIL — unchanged by the amendment):**
- MSIL / ClosureLowering: the environment allocation and the captured-parameter hoists precede
  the prologue; the `Me` store into the environment is emitted **immediately after**
  `IRBaseConstructorCall`; before that point `ldarg.0` appears only as the receiver of the base
  `call`. Where to place things when the prologue captures nothing is the implementer's call,
  subject to byte identity.

**Revisit if:** a base constructor invoking the delegate before the derived environment's `Me`
store is observable in valid VB (it is not: D4 forbids `Me` in that lambda).

#### D3, C++ arm (AMENDED): which writes to a captured variable make `[=]` a silent wrong answer

*As first ruled*, C++ refused "a lambda assigns a captured variable (#140)", and a read-only
base-args lambda (B2, B5) was to run correctly. B2 does not: its body writes `p` after the lambda
captured it, and it printed 2 for VB's 12 (measured). The amendment replaces this arm; the C#,
JavaScript and MSIL arms, D1, D2, D4 and D5 stand.

**Decision:** adopt W2. C++ refuses a lambda when either
- **(a)** the lambda writes a variable it captures (D3 as first ruled); or
- **(b)** the creator writes a captured variable at a point reachable in the creator's CFG from
  the lambda-creation instruction, that instruction included.

**Because:**
- D3 as first ruled left 15 measured silent wrong answers (B2 and the ADR-0014, t155 and t185
  legs of falsifier 5a). Owner rule one forbids that, even for three tasks.
- W1 (syntactic: any write other than the Dim initializer) is cheaper but regresses 9 programs C++
  gets right — a copy taken at creation equals the per-iteration instance — which breaks owner
  rule two with no cause.
- W2 measured 0 regressions, all 15 wrong answers caught, and the same verdict in cli, cli-O and
  proj-Release. Its one real cost is the edge model; the Contract makes that part durable, so
  #140 deletes a rule, not an analysis.

**Contract:**
- **Searched variables:** the lambda's captures, transitively through nested lambdas, that are
  the creator's own parameters or locals; the creator may itself be a lambda.
- **Hit vocabulary:** D3's, unchanged — assignment, rename, store, `++`/`--`, ByRef argument.
- **One shared successor function** in `BasicLang`, not private to `CppCapabilityChecker`:
  explicit branches; For Each end of body → body entry (except on Exit); every try and catch
  block → the Catch and Finally blocks; Finally → the end block. If the verifier or optimizer
  already computes successors, extend that; migrating existing passes onto it is not part of
  #170.
- **Per-iteration cut:** the search for `v` does not enter the body entry of a loop whose
  `BodyLocals` contain `v`, nor a For Each whose control variable is `v`; a `Dim v` initializer
  is a declaration and ends that path. (That second rule works around `BodyLocals` omitting a
  name declared twice — filed against ADR-0014 as #242, not fixed here: changing `BodyLocals`
  changes per-iteration lowering on every backend.)
- **The refusal** names the variable, the lambda, and the write that makes the copy stale, citing
  #140. Arms (a) and (b) are one rule with one deletion point.

**Obligations:**
- #140 deletes W2 as one rule and flips B1–B4, C1 and the 15 to running.
- #140 must ALSO keep the nine W1-shape programs (t172 L2, E05, E06, E07, E07e, E07w, E07x, E10,
  E13) and E20 running — by-reference capture must bind the per-iteration instance, not a hoisted
  local. Those ten are #140's regression fence.
- Each edge family, and the per-iteration cut, carries a pinned witness (5d), so an edit to the
  shared helper cannot silently drop one.

**Revisit if:** a real program is refused by arm (b) before #140 lands — the answer is to pull
#140 forward, not to carve an exception into the rule; or a wrong answer escapes W2 — a new class,
which comes back here, not to the implementer to widen.

**Rejected:** keeping D3 as first ruled until #140 (15 silent wrong answers); W1 (9 regressions);
W2 on `BodyLocals` alone (refuses E20, which runs today); fixing `BodyLocals` in #170 (changes
ADR-0014's lowering on all five backends for a refusal that lives three tasks); a private
successor function in the checker (a mirrored pair that drifts, and the whole analysis dies with
#140); pulling #140 forward instead (sequencing, out of scope — W2 binds until #140 lands);
refusing only when the lambda is invoked after the write (needs an invocation/escape analysis
nobody has, and W2's over-approximation measured 0 regressions).

#### Falsifier 5 (corrected by the amendment)

- **5a.** Refused by name citing #140 — arm (a): B1–B4, C1; arm (b): B2, E01, E02, E08, E09;
  `PerIterationLoopBodyDimTests` E03, E04, E07f, E12, E18; t155 P1, P2, R12_byrefarg; t185 E3,
  E3b.
- **5b.** Runs on C++ and equals the VB oracle: B5, t172 L2, E05, E06, E07, E07e, E07w, E07x, E10,
  E13, E20. E16 stays a named clang failure — neither refused nor run.
- **5c.** Corpus of 854 programs × {cli, cli-O, proj-Release}: exactly D3's 49 + 17 refused, the
  same set in all three modes, zero programs correct today become refused.
- **5d.** Mutants, each failing a pinned test: remove the Try edges (E03, E04, E07f escape);
  remove the For Each back-edge (a witness the implementer names — none, and the edge is
  dropped); exclude the creation instruction itself (`f = Function() f()` escapes); remove the
  per-iteration cut (E20 and the nine wrongly refused); remove the Dim-initializer rule (E20
  wrongly refused).
- **5e.** Review item: the checker imports its successor function from the shared helper; a
  private copy is a reject.

### D4: BC31095 / BC31096

In `SemanticAnalyzer` (shared by the compiler and the LSP), refuse any reference to the object
under construction in `MyBase.New` arguments, a lambda written there included: explicit
`Me`/`MyClass`/`MyBase` is BC31095, an implicit instance member BC31096. **Revisit if** VB
accepts some `Me` reference in that position — then narrow to match the oracle; never widen.

### D5: Verifier invariant

`IRVerifier` enforces: (a) def-before-use for `IRBaseConstructorCall`'s operands; (b) at most one
`IRBaseConstructorCall` per function, only in a constructor, only in the entry block; (c) D1's
prologue invariants — expression-only, and closed (no use of a prologue value after the call);
(d) every IR lambda function is referenced by exactly one lambda-creation instruction in some
block (no orphans). **Obligations:** #163 runs the B/W probe suite with removal on before
merging. **Revisit if** a legitimate pass needs a prologue value after the base call (then C#
needs a statement-capable lowering, a new ruling).

### Byte identity (the ruling's expectation)

Byte-identical on all five backends for every program with no explicit `MyBase.New`, or whose
`MyBase.New` arguments are parameters and literals only. C1 byte-identical on C#, JavaScript and
MSIL. C++ byte-identical for W1 and W2. Programs that change: B1–B5 and W1/W2 (broken → running on
C#, JavaScript, MSIL); V1–V4 (→ BC31095/BC31096); any C++ lambda that writes a captured variable
— or, under the amended D3, whose creator writes one after capturing it — (→ refused).

### The orchestrator's clarifications (binding)

- **C1, a multi-block prologue.** The *prologue region* is the blocks from the entry up to and
  including the block holding `IRBaseConstructorCall`; nothing outside the region branches into
  it; branch/phi are admitted inside it only as the lowering of the argument expressions
  (`AndAlso`/`OrElse`/`If(...)`); the verifier checks the region form; JavaScript, MSIL and C++
  emit a multi-block region; C# refuses it by name. None of these shapes may go from running
  correctly to refused.
- **C2.** CSE, CopyProp and every forward dataflow pass treat `IRBaseConstructorCall` as a FULL
  barrier: no expression or copy available before it is available after it — its entry in
  ADR-0006's total kill vocabulary.
- **C3.** The C++ refusal is position-independent ("any lambda that assigns a captured
  variable"); before landing it, every C++ cell that runs with VB's output today and would be
  refused is measured; any such cell is a STOP.
- **C4.** BC31095/BC31096 reach the LSP; the vbc code is matched per shape, a lambda and a
  nested lambda included; a Shared member, a module function, a constant, `MyBase.New`'s own
  parameters and a Shared member reached through the class name are NOT refused.
- **C5.** The D2a purity check reads the prologue; ADR-0015's "THE ARGUMENT LIST CAN BE STALE"
  becomes moot.
- **C6.** C++ stays byte-identical for W1/W2 and for every constructor with simple arguments;
  `ctor_`'s order (argument temporaries, then `Base::ctor_`, E11) is unchanged.

## Because

- The defect is "a use with no home in any block". Walking an open list of consumers (option A)
  is the per-consumer fix the next pass forgets; option B removes the second home. The argument
  instructions were already in the entry block — only the call site was off-stream.
- C# cannot run statements before `: base(...)` but can take any expression there, a lambda
  included, and such a lambda shares its captured parameters with the body: VB's semantics.
- Once the lambda creation is an operand in the stream, `LambdaReferences` finds it, the
  parameter is captured, and the body write lowers as an assignment on every backend — the
  `const p` / `t0` symptom is the same blind spot, fixed once.
- The MSIL refusal's IL premise was false: building an environment and a delegate before the base
  `call` is what csc and vbc emit; only `this` may not enter the environment before it (D4).
- C++ `[=]` copies, so a lambda that writes a captured variable is a silent wrong answer; a named
  refusal beats it, and one keyed on position would be a second list for #140 to delete.
- BC31095/6 are the precondition of D3's MSIL ordering (a null `Me` otherwise) and turn four
  backend-specific outcomes into one front-end diagnostic.
- D5(a) plus non-removability is what #163 needs: with removal on, a DCE bug shows up as an
  undefined operand, not a run-time defect; D5(c) stops CSE or hoisting from creating a
  cross-region use C# cannot express.

## Implementation notes (implementer, measured — not part of the ruling)

- **Where.** `IRNodes.cs` (`IRBaseConstructorCall`; `IRConstructor.BaseCall`, and a READ-ONLY
  `BaseConstructorArgs` view over it — see below), `IRBuilder.cs` (`EmitBaseConstructorCall`),
  `IROptimizer.cs` (`MapUses` arm; kill vocabulary `WriteSet.Everything`), `IROperandWalker.cs`,
  `IRVerifier.cs` (Invariant P), `ClosureLowering.cs` (`PlanBaseConstructorCall`,
  `InsertPrologues`, the base call's slot typing), `CSharpBackend.cs` (`BaseArguments`),
  `JavaScriptBackend.cs` (`SuperCall`, `Visit(IRBaseConstructorCall)`), `MSILBackend.cs`,
  `CppCodeGenerator.cs`, `CppCapabilityChecker.cs` (`CheckLambdaCaptureWrites`, the D2a check),
  `Compiler/CodeGen/CPlusPlus/CppObjectModel.cs` (`IsPurePrologue`; `PlanBaseArguments` deleted),
  `LLVMBackend.cs` (a documented no-op: that backend never chained to a base constructor),
  `SemanticAnalyzer.cs` (D4), `ICodeGenerator.cs`/`IRNodes.cs` (the visitor method, abstract on
  `CodeGeneratorBase` and throwing by default on `IIRVisitor`).
- **"Deleted" is realised as "no storage".** The list is gone; `IRConstructor.BaseConstructorArgs`
  survives only as a derived, read-only view of `BaseCall.Args`, because
  `BaseConstructorDiagnosticTests.TheSynthesizedConstructor_IsShapedLikeADeclaredOne` reads it
  and tests are not edited in this task. No pass or backend reads it; it can go when that test
  reads `BaseCall`.
- **"Only for an explicit `MyBase.New`" is realised as "only when the argument list is
  non-empty".** An IMPLICIT call to a base constructor whose parameters are all `Optional` has
  arguments too (the filled defaults, formerly in the list, including the synthesized
  constructor's). With the list deleted they need a home, so the instruction is built whenever
  there are arguments; the parameterless call (implicit or `MyBase.New()`) stays backend-side, so
  every constructor that had an empty list is emitted exactly as before.
- **C2, the barrier.** `NamesWrittenBy` classifies the instruction as `WriteSet.Everything`
  (classified, like `IRInlineCode`): CSE's and CopyProp's `Invalidate` clear on it, LICM never
  sees it (it is never in a loop), Invariant V is quiet. Probes E06/E06b compute `p * q + 1` on
  both sides of the call, and E06 has the base constructor invoke a lambda that writes `p`
  first: VB's `4 34 7` on C#, JavaScript and MSIL in all three modes.
- **C1, measured.** `If(c, x, y)` does not parse in BasicLang at all (`Unexpected token in
  expression: 'If'`, every backend, before and after), and `IIf` is not a builtin anywhere, so
  `AndAlso`/`OrElse` are the only multi-block prologues. Before: refused on C++ (E11), MSIL
  ("COMPUTED `__sc0`") and JavaScript (TDZ `ReferenceError`), CS0103 on C#. After: VB's answer on
  C++, JavaScript and MSIL; C# refused by name. Nothing went from running to refused.
- **Invariant P** (`IRVerifier.CheckInvariantP`, run by `VerifyAfterOptimization` on un-lowered
  IR only — ClosureLowering's output legitimately holds environment stores before the call).
  The region form of (b) and (c) admits, as the lowering of an argument itself: a branch that
  terminates a region block, an assignment to a carrier local nothing after the call mentions
  (`AndAlso`'s `__scN`), and a store into an array the prologue allocated (an array literal —
  ADR-0015's E16 runs on C++ and must keep running). A value that writes a variable, and a call
  with no result, are statements and fail (c).
- **D3 on MSIL, "the prologue captures nothing".** ClosureLowering puts the whole environment
  right AFTER the base call (where MSIL always wrote it relative to the call), and the prologue
  reads the parameters themselves through synthetic copies the rewrite and the post-condition
  leave alone — sound, because no lambda exists before the environment does. A constructor with
  a base-args lambda allocates the environment first and stores `Me` right after the call. MSIL
  writes the base call first (exactly as before) when nothing precedes it in the IR, and in place
  otherwise.
- **JavaScript** renders `super(...)` inside the constructor's own scope: rendered before it, the
  arrow function saw no declared `p` and emitted `const p = …` (#170's TDZ). A base call with
  nothing before it keeps the old spelling (`super` first); otherwise it is written in place.
- **C++** writes E11's prologue (base `ctor_`, field initializers) at the instruction, with no
  `#line` of its own — the text E11 wrote after its anchor.
- **C5.** `CppObjectModel.PlanBaseArguments` is deleted with its three refusals ("control flow",
  "statements in between", "still being filled after") — the placement is the IR's now. The
  "argument list can be STALE" note is moot: the arguments are operands that every pass
  re-points (strength reduction's `a * 2` → `a << 1` included).
- **C3 / D3's rule, precisely.** "Assigns" is an assignment target, a value renamed after a
  variable, a store to a variable, `++`/`--`, a variable passed to a ByRef parameter — not the
  kill vocabulary, which counts a constructor's every variable argument as written. A For Each,
  Catch or pattern variable inside the lambda DECLARES its own variable (C++ emits a new one); a
  For Each over an existing variable reaches the IR as #168's hidden variable plus an ordinary
  assignment. "Captured" is a parameter or local of the creator chain the lambda does not declare.
- **D3 amended, where.** One method, `CppCapabilityChecker.CheckLambdaCaptureWrites`, holds both
  arms (with `WriteReachedAfter`, the arm-(b) search, and `NamesAssignedBy`); #140 deletes those.
  The edges are `ControlFlowGraph.ExecutionSuccessors(function)`, next to and built on
  `SuccessorsOf` — a separate analysis edge set, because `Build`, DCE's reachability and
  `IdentifyLoops` must keep the laid-out edges (a back edge there makes a natural loop they do not
  expect). A `Dim` initializer is marked `IRInstruction.IsDimInitializer` by IRBuilder (the
  assignment, the renamed value, the array-slot store, the tuple element); no pass reads it.
- **D3 amended, what the capture set misses.** Arm (b)'s searched names are
  `LambdaCapturesOf(lambda)` (ADR-0006's capture set) plus every name the lambda CALLS as a
  delegate — `f(n - 1)` is an `IRCall` whose `FunctionName` is the variable, not an operand, so the
  capture set does not list it, while C++'s `[=]` copies it. Without that,
  `f = Function(n) … f(n - 1)` escapes and throws `bad_function_call` (measured). When the capture
  set cannot enumerate a lambda's names (raw inline code: `LambdaCapturesOf` is null) the fallback
  is the names its operands mention — "every creator local" refused t155 R11 for nothing.
- **D3 amended, the refusal.** "the lambda created at line 13 captures 'p' of 'D.New', and
  'D.New' writes it at line 15 — not supported on C++ (#140): …" (arm (b)); arm (a) reads "…and
  the lambda itself writes it at line 16 …". One refusal per variable per lambda and arm.
- **D4.** `SemanticAnalyzer` sets `_inBaseConstructorArguments` around the argument visit; the
  identifier arm reports `Me` (BC31095) and a bare name that resolves to an instance member of
  the class or of a BasicLang base (BC31096); `MyBase` reports BC31095. Which members are `Shared`
  is recorded per class from its `ClassNode` (`RecordSharedMembers`) — a field's or method's
  symbol does not carry it; a member of a .NET base is never judged.
- **C# and an array literal.** `MyBase.New(New Integer() {a, 2, 3})` is refused on C# by name
  ("needs a statement before the base call (IRArrayAlloc)"): the C# backend has no expression form
  for an array literal (it emits `new int[3]` plus element stores), and D1 admits only expressions
  into `: base(...)`. It was CS0103 (`t0`) before, so nothing regressed; C++, JavaScript and MSIL
  run it.

## Measured at landing (implementer — not part of the ruling)

- **Byte compare**, 854 programs × 14 backend/mode cells (11,956), before vs after, classified
  by a script: 11,382 identical, 574 different, and every difference falls in a ruled class —
  an explicit computed or lambda `MyBase.New` argument (39 programs; on C++ only argument
  folding such as `2 + 3` → `5` and `a * 2` → `a << 1` now reaching the operand, and a pure
  prologue temp placed before the field initializers), BC31095/6 (16), the C++ #140 refusal
  (48), the C# multi-block / array-literal refusal (4). Nothing unexplained; the verifier fired
  0 times in either tree. W1/W2 are byte-identical on C++, C1 on C#, JavaScript and MSIL.
- **C3.** Every C++ cell the first-landed rule refused — 48 corpus programs and 16 test pins —
  ran a WRONG answer, failed to compile or was already refused before. None ran VB's output.
- **The amendment's measurement (W1 vs W2)**, both candidates in log mode over the 854-program
  corpus × {cli, cli-O, proj-Release} and the whole test suite's C++ legs (176 distinct lambda
  programs, none outside the corpus's refused set), "correct" meaning equal to vbc:

  | Rule | Refused beyond first-landed D3 | Correct today → refused | Wrong / failing today → refused | Same in all 3 modes |
  |---|---|---|---|---|
  | W1, syntactic | 27 programs (81 cells) | **9** (t172 L2, E05, E06, E07, E07e, E07w, E07x, E10, E13) | 18 | yes |
  | W2, flow-sensitive | 17 programs (51 cells) | **0** | 17 (15 wrong answers; t155 L13c/L14 C++ compile failures) | yes |
  | W2 on `BodyLocals` alone | 19 programs | 1 (E20) | 18 (+ E16, a clang failure) | yes |

  Without the Try edges W2 missed E03, E04 and E07f (a Finally is reached from the Try node's
  block only).
- **The amended rule as landed** (5a–5d): the corpus refusal set is exactly first-landed D3's 49
  + the 17, identical in cli, cli-O and proj-Release, with no program that is correct today
  refused; every 5a program is refused by its named arm (B2 by arm (b) only — its lambda does not
  write); every 5b program runs VB's output on C++ in all three modes. Byte compare against the
  pre-#170 build: every difference in a ruled class; against the first landing: exactly the 17
  programs' C++ cells. `BASICLANG_VERIFY_IR`: 0 fires. The mutants and their witnesses:

  | Mutant | Escapes / wrongly refused (measured) | Witness |
  |---|---|---|
  | no Try edges | E03, E04, E07f escape | the three pins, re-pointed to "refused" |
  | no For Each back edge | a function-level `x` written in a For Each body before the lambda (VB 6 6 6, C++ 1 3 6) escapes; so does t155 L13c | `FE1_foreach_backedge` (new witness — the edge has a cause) |
  | creation instruction excluded | `f = Function(n) … f(n - 1)` escapes (C++ throws `bad_function_call`) | `CR1_self_reference` (new witness) |
  | no per-iteration cut | the nine refused (and t155 L15); E20 is NOT — the Dim rule still covers it | `PerIterationLoopBodyDimTests` E05/E10 headline cases, `E07w_…PinnedForTask227` |
  | no Dim-initializer rule | E20 refused (and E16, a clang failure today) | `E20_LambdaOwnLocalSameNameAsCapturedBodyDim_…`, `E16_SiblingSameName_CppDoesNotCompile` |
- **Pre-existing, now reachable:** a lambda nested in a lambda inside a class member fails
  `ilasm` on MSIL ("undefined class `D/<>c__Env0/<>c__Env2`") — measured identically before this
  change on a constructor-BODY shape. Base-args shapes that nest (E01, X22, X25) used to stop at
  the old named refusal and now reach that defect.
- **Detectable only by ILVerify:** storing `Me` into the environment BEFORE the base call runs
  correctly on the CLR but is `UninitStack` under ILVerify; the run-time tests cannot see it.
- **Language gaps met along the way:** `If(c, x, y)` does not parse, `IIf` is not a builtin, and
  `MyClass` is not supported in a base argument (it reports a type mismatch, not BC31095).

## Amendment A-140 (ADR-0019, 2026-10-01): W2 is the fallback's soundness proof, deleted with `[=]`

*Appended, not edited in place: the "D3, C++ arm (AMENDED)" text above is what #170 landed. #140 changed
what that rule is FOR; the rule itself, its contract, its message text and its single deletion point are
unchanged.*

- **W2 is demoted** from "the refusal rule for lambdas" to "the soundness proof for the by-copy fallback".
  Every C++ root goes through `ClosureLowering` first (ADR-0019 D1). A root the lowering refuses (ADR-0010 D9
  and beyond) is emitted by today's `[=]` path ONLY if W2 holds for it, byte-identical to before #140;
  otherwise the program is refused with W2's text first, then the lowering's reason. It is still ONE rule in
  ONE function (`CppCapabilityChecker.CheckLambdaCaptureWrites`) with ONE call site
  (`CppCodeGenerator.LowerClosures`), now reached only for the roots in `ClosureLoweringResult.SkippedRoots`.
- **W2 is never evaluated for a lowered root, and its verdict never influences whether a root is lowered.**
  Ownership does not move: it stays a C++ capability-checker rule over the shared CFG successor function, and
  `ClosureLowering` never learns about it. A lowered root that needs W2 means the two-layer model has leaked.
- **It is deleted together with the `[=]` path**, in one future task, when `SkippedRoots` is empty for every
  program C++ accepts. Each D9 shape the lowering learns to lower shrinks the fallback set; the fallback set
  is pinned by name (`CppClosurePathTests`) and may only shrink.
- **The obligations above are met, not deleted.** "#140 deletes W2 as one rule and flips B1–B4, C1 and the 15
  to running" became: B1–B4, C1 and the 15 (E09 and R12 excepted) run, lowered; E09 and R12 are refused by
  BOTH paths. "#140 must keep the nine W1-shape programs and E20 running" holds, and the fence ALSO asserts the
  path each root takes (ten lowered, E20 the one by-copy fallback), so a path shift is visible.
- **The refusal text.** W2's text is unchanged. A both-refused program's message continues with
  `closure lowering cannot lower '<root>' either (#140): C++: <reason>`.
- **5b's "E16 stays a named clang failure" no longer holds:** E16 runs and prints the #229 output
  (20|20|20|20) — ADR-0019 D4, the owner's decision pending.
