# ADR 0010: Lambdas and closures on the MSIL backend — closure conversion as an opt-in IR pass

- **Date:** 2026-09-26
- **Status:** Accepted
- **Amended by:** ADR-0019 (#140) — D1 only; see "Amendment A-140" at the end. No other decision here changes.
- **Decided by:** architect (Fable 5.1), in one ruling plus an AMENDMENT that replaced D2 after
  the orchestrator measured that the IR is flat (no block/declaration structure) and that C#
  and JavaScript print `6|6|6` for L15. Transcribed here; this file records the ruling as
  amended.
- **Brief:** the orchestrator's `brief-v2.md` for task #155 (session scratchpad, not kept in
  the repo). Probes L1–L16 are defined below.
- **Task:** #155.

## Question

MSIL had no lambda lowering at all: every lambda program failed (`Reference to undefined class
'Action'`, a stored `__lambda_N` with nothing pushed, `bump()` emitted as a call to a static
method nothing defines, `Func(Of Integer, Integer)` spelled as a bare `'Func'`), and
`AddressOf` was refused. Nine questions, each expensive to reverse or shared with the C++
backend's own capture problem (task #140): where closure conversion lives; the environment's
granularity; which variables are hoisted; by-value and ByRef parameters; nested lambdas;
`Me`; delegate types; invocation and `AddressOf`; the first-cut boundary.

The probes (C# and JavaScript are the oracle):

| Probe | Shape | Expected |
|---|---|---|
| L1 | `Dim f = Sub() Console.WriteLine("hi") : f() : f()` | hi, hi |
| L2 | `Function(x As Integer) x * x`, `sq(5)` | 25 |
| L3 | read-only capture `Function(x) x + k` | 8 |
| L4 | write capture `Sub() n = n + 100` | 3,103 |
| L5 | a lambda passed to `Sub Twice(f As Action)` / `Function Apply(g As Func(Of Integer, Integer), v)` | 7, 12 |
| L7 | nested lambda writing a creator local two levels out | 3,103 |
| L8 | a lambda in a class method reading a field (`Me`) | 15 |
| L9 | `Dim a As Action = AddressOf Hello : a()` | hello |
| L11 | a lambda called in a loop writing a captured local | 30 |
| L13 | declaring `For Each x As Integer`, a lambda per iteration capturing `x` | 1, 2, 3 |
| L14 | `For i As Integer = 1 To 3`, a lambda per iteration capturing `i` | 4, 4, 4 |
| L15 | `Dim y` in a loop body, `y = y + i`, captured | 6, 6, 6 (C#/JS; VB says 1, 3, 6) |
| L16 | a `Catch ex` captured and invoked after the `Try` | boom |

## Decision

- **D1 — where.** A separate IR→IR pass, `ClosureLowering`, invoked only by a backend that opts
  in (MSIL now; C++ may for #140), after the optimizer and the verifier. Its output is ordinary
  IR — synthesised environment classes, fields, instance methods, `New`, field loads and stores —
  plus ONE new node, `IRDelegateCreate`. C# and JavaScript never call it.
  Contract: `IRModule ClosureLowering.Run(IRModule)`. Post-condition (asserted, else throw): no
  `IRFunction.IsLambda` remains, and no `IRVariable` in a former lambda body names a creator
  variable. The lowered form is invisible to other backends (a clone). The lowered IR is
  verifier-clean; the MSIL path re-runs the verifier on it. The verifier learns
  `IRDelegateCreate`; the C#, JavaScript and C++ visitors throw on it.
- **D2 (amended) — environments.** One environment per creator FUNCTION holding every captured
  variable (locals, by-value parameters, `Me`), allocated at function entry; plus one environment
  per ITERATION of a DECLARING `IRForEach` whose loop variable is captured, allocated at the top
  of its body, holding that variable and chained (`parent`) to the enclosing environment (the
  function's, or an outer iteration's). A non-declaring `For Each` (#168's `__foreach_N` plus the
  assignment) gets NO iteration environment — its variable is function-level. No scope recovery
  in IRBuilder: the IR is flat, and a loop-body `Dim` is function-level.
  Contract: L13 1|2|3; L14 4|4|4 (one function-level binding); L15 6|6|6 (matches C#/JS — VB's
  1|3|6 is a recorded, deliberate, all-backend divergence; the pass must not reset or
  copy-forward `y`); nested declaring `For Each`s each see their own pair; a non-declaring
  `For Each x` over an existing `x` shows every lambda the final `x`; a `Catch` variable is
  function-level and the backend spills the exception to its slot before the environment store.
  ⚠ **Superseded in part by ADR-0014** — the L15 clause (L15 is 1|3|6 on every backend; see its D5).
- **D3 — what is hoisted.** (creator's locals ∪ by-value parameters) ∩ (the transitive union of
  its lambdas' capture sets, task #122's over-approximation). A null capture set is refused.
  Binding is the front end's job: the pass never re-resolves a name, and refuses — naming the
  variable — a lambda that declares a name it also uses from the creator (N9, VB BC30616) and a
  name matching a lambda parameter case-insensitively but not exactly (#169).
- **D4 — parameters.** By-value parameters are copied into the environment at entry; every later
  read and write goes through it. A captured ByRef parameter is refused (IL cannot keep a managed
  pointer in a field; VB BC36639 belongs to the front end — to be filed).
- **D5 — nesting.** An environment chain. A lambda is an instance method on the environment of the
  innermost scope it captures from; its own body is lowered by the same rule, recursively.
- **D6 — `Me`.** A field of the environment; in the first cut a lambda is never an instance method
  on the creator's class. `Me` captured in a `Structure` method is refused (VB BC36638).
- **D7 — delegate types.** `Action`, `Action(Of …)` and `Func(Of …)` are the BCL generic
  delegates through the existing `[mscorlib]` reference; user `Delegate` declarations stay
  nominal. A lambda or `AddressOf` is TARGET-TYPED: its delegate type is the declared type of the
  slot it is assigned to or passed as. The signature must match EXACTLY after generic
  substitution, or the construct is refused — no VB relaxed delegate conversion, and no
  conversion of a delegate VALUE between delegate types (VB itself requires
  `AddressOf a.Invoke`). The arity is capped at what was measured to assemble and run.
- **D8 — invocation and `AddressOf`.** One canonical form. `IRCall.CalleeValue != null` →
  `callvirt instance … Invoke`; a name resolving to a declared method → `call`; neither → throw
  (the "call a nonexistent static" path dies). The pass canonicalises a name-form call whose name
  is a variable of delegate type (a local, a parameter, an environment field; a variable shadows
  a method — VB's rule) into `CalleeValue` form. Lambda values and `AddressOf` both lower to
  `IRDelegateCreate(DelegateType, Target?, Method, IsVirtual)` → `ldnull | <target>; ldftn |
  dup + ldvirtftn; newobj .ctor(object, native int)`. IRBuilder keeps emitting both call forms
  for C#/JS; making `CalleeValue` the only form for every backend is a separate task.
- **D9 — the first-cut boundary.** Supported: L1–L5, L7–L9, L11, L13–L16. Refused, with a
  `ForeignFeatureException` naming the construct: ByRef capture; a lambda inside an Iterator or
  Async function, or an iterator/async lambda; `Me` in a `Structure`; a null capture set; the N9
  and #169 shapes; delegate relaxation or delegate-to-delegate conversion; an arity above the
  measured cap; a captured variable typed by a generic parameter of the creator. L6/L12
  (`List.ForEach`, `Where`) stay the existing "outside the supported collection surface"
  refusals — the List surface is a separate question.

## Because

- Optimizer and verifier keep seeing un-lowered IR by construction; nothing upstream changes.
  #140 needs the same hoist-and-rewrite, and a pass is testable on IR alone, without ilasm.
- The function environment plus a per-iteration environment for a declaring `For Each`
  reproduces the C#/JS oracle exactly on L13/L14/L15; the flat IR has exactly one scope
  construct, and a declaration-site change in IRBuilder would alter C# and JavaScript output.
- Over-hoisting is semantically neutral; under-hoisting mis-emits. #122's set is already relied
  on as a superset by the optimizer, and the post-condition is the safety net.
- A per-lambda copy loses writes (L4/L7/L11); a ref-cell per variable needs a generic box through
  the facade and a closure object anyway.
- BCL parameters (the future of L6/L12) need the real BCL types; a synthesised `Action` never
  unifies with them. The C# backend already gives users BCL identity.
- One emission path for delegate calls; the name-versus-value distinction belongs in the IR, not
  in a backend's name lookup.

## Contract

- `ClosureLowering.Run(IRModule)` returns the module ITSELF when there is nothing to lower (no
  lambda, no `AddressOf`, no call through a delegate value), so a program without delegates
  reaches the backend byte-for-byte as before; otherwise a lowered CLONE, leaving its input
  untouched.
- The environment class is named `<>c__EnvN` (`<>` is not a BasicLang identifier character, so
  no user type can collide), and is NESTED in the class whose member created the lambda (see the
  implementation notes); a module procedure's environment is top-level.
- `IRDelegateCreate` is a definition of its own name and writes nothing else: building a delegate
  runs no user code (`OptimizationPass.NamesWrittenBy`). `MapUses`/`UsesOf` see its `Target`.

## Implementation notes (implementer, measured; within the ruling)

- **Where MSIL runs it.** At the top of `MSILCodeGenerator.Generate`, after
  `ForeignFeatureChecker` and before any emission. Every MSIL entry point calls `Generate` on the
  optimized module and nothing else — the CLI and a `.blproj` build (both through
  `Program.GenerateCode`), the IDE (`BuildService`), and `MsilHarness.CompileToIl` — so this one
  seam reaches all of them. When the pass changed anything, `IRVerifier.VerifyAfterOptimization`
  is run again on the lowered module.
- **The measured arity cap (D7).** Through the generated `[mscorlib]` reference on .NET 8:
  `Action`1`..`Action`8` and `Func`1`..`Func`9` assemble and run; `Action`9` and `Func`10` (and
  every higher one tried: `Action`16`/`17`, `Func`17`/`18`) assemble and then die with
  `TypeLoadException` — the `mscorlib` facade does not forward them. `Action(Of …)` with more
  than 8 type arguments and `Func(Of …)` with more than 9 are refused.
- **Environments are NESTED in the creator's class.** D6 puts `Me` in the environment, and a
  lambda then reaches the creator's members through it. Measured on .NET 8: a TOP-LEVEL class
  reading another class's `Private` field dies with `FieldAccessException`; a NESTED class may
  (ECMA-335: a nested type has access to everything its enclosing type has). So
  `IRClass.EnclosingClass` names the class an environment is declared inside, and MSIL writes it
  as `.class nested public` inside that class and names it `'Box'/'<>c__Env0'`. This is where
  the class is declared, not what it holds; the model is D2/D5/D6 unchanged.
- **Every bare call name in a lambda is added to its capture set.** A delegate VARIABLE invoked
  by name (`greet(s)`) is, semantically, a READ of that variable — D8's canonical form is a
  `CalleeValue` read of it — but the name-form call IRBuilder still emits carries the name as
  `IRCall.FunctionName`, which #122's recorded capture set does not list at all (it only lists
  names read as `IRVariable`s). Without this, a lambda that calls a captured delegate-typed local
  by name would not hoist it. Added for every bare call in the lambda and in every lambda nested
  in it: a name that turns out not to be one of the creator's own variables is dropped by D3's
  intersection with the creator's declarations, and one that is but is not delegate-typed is only
  over-hoisted (D3: over-hoisting is neutral).
- **Member reads through the captured `Me` are storage.** A lambda that read a creator's field
  bare (`K`) read an `IRVariable` before lowering and reads `IRFieldAccess(Me.__me, K)` after it.
  The kill vocabulary classifies every `IRFieldAccess`/`IRFieldStore` as a call (it may run a
  property accessor), which would make the verifier re-run see calls where none were. The pass
  therefore marks the accesses it KNOWS are storage — environment fields, a creator's plain field
  or plain auto-property (ADR-0007's "storage") — with `IsStorageAccess`, and
  `NamesWrittenBy`/`CollectReads` treat those as a named read/write rather than a call. Only
  `ClosureLowering` sets the flag, so the optimizer never sees one.
- **In a lambda, a bare member of the creator's class** — a field, a property, a method call,
  `AddressOf` of a method — is reached through the captured `Me` (a `Shared` one through the
  class name). This is what the MSIL backend used to resolve for the lambda from the creator's
  own context; it is not a re-binding of a captured variable.
- **`AddressOf`** lowers for a module procedure, a method of the enclosing class, and
  `obj.Method` on an object of a class the program declares (the phantom `IRFieldAccess` IRBuilder
  emits for `obj.Method` is removed). A virtual method binds with `dup; ldvirtftn`.
- **Refused beyond D9's list, each because MSIL would otherwise mis-emit it, never silently:** a
  captured variable passed `ByRef` (it lives in a field; the write-back would land in a temp); a
  `Select Case` `When` guard that reads a captured variable (a guard is rendered inline, where no
  environment load can be placed); a captured `Select Case` pattern variable; `MyBase.M()` inside
  a lambda (the environment has no base to call non-virtually); a lambda in a field or module
  initializer or in `MyBase.New(...)` arguments (no creator function to hold its environment);
  a lambda inside a GENERIC class (its nested environment would have to be generic — the same
  family as D9's generic refusal).
- **Delegates and "neither → throw".** A parameterless user `Delegate Sub D()` never assembled on
  MSIL (`BeginInvoke(, class …)`, a syntax error); fixed with the delegate work.

## D8's consequences, measured

D8's "neither a declared procedure nor a delegate value" check (`MSILBackend.Visit(IRCall)`) is
unconditional — it runs on every module-level call, whether or not `ClosureLowering` touched the
module — so it also catches names the OLD code silently mis-emitted for reasons that have nothing
to do with lambdas. Three shapes, re-measured on this working tree against the pre-fix binary
(`.worktrees/p2a1base`, commit `6a6d224e`) and the post-fix one:

- **`SampleGames/Pong` and `SampleGames/SpaceShooter`.** Both call `GameInit(...)`, an engine
  entry point that resolves through neither the stdlib table nor a declared module procedure, so
  it now fails at COMPILE time with the D8 message naming `'GameInit'`. Before, MSIL never
  reached that check: it assembled `Main.bas`/`Main.bl` past the front end and failed at `ilasm`
  on an UNRELATED pre-existing defect elsewhere in the same file — `Local var slot 7: type
  conflict` for Pong, `Local var slot 8: type conflict` for SpaceShooter. Neither sample ever ran
  on MSIL before or after this fix; D8 just moves the failure earlier and makes it legible.
- **A `RaiseEvent` with no subscriber** (`Public Event Clicked(n As Integer)` raised by a `Fire`
  method nothing ever `AddHandler`s). `IRBuilder` lowers the raise to a call named
  `'raise_Clicked'`, which resolves to neither a declared procedure nor a delegate — now refused
  at compile time, naming `'raise_Clicked'`. Before, it assembled and `ilasm` refused the
  generated IL itself: `Illegal use of type 'void'` / `Illegal local var type: 'void'` (line 69 of
  the generated `.il` — the raise's plumbing typed a slot `void`). This has never run on MSIL.
- **A call to an undeclared name** (`Ghost()`, nothing in the program named `Ghost`) — the
  general case D8 exists for, independent of events or engine calls. Now refused at compile time.
  Before, this is the one shape that DID assemble (`ilasm` does not resolve member references)
  and only failed at RUN time: `Unhandled exception. System.MissingMethodException: Method not
  found: 'System.Object Combined.Ghost()'`.

None of these three ever produced a correct, running MSIL program either before or after #155;
D8 changes only WHEN and HOW LOUDLY the failure is reported — compile time, naming the construct,
instead of an assemble-time or run-time surprise.

## Follow-ups (not blocking #155, filed separately)

- **#140** — the C++ backend's own lambda lowering (capture by copy, `[=]`) is the second
  consumer D1 designed `ClosureLowering` for; #140 still needs its own opt-in call and its own
  measurement, C++'s environment model (`shared_ptr<Env>`) is not yet wired up.
- **#169** — a name spelled in a different case from a lambda parameter binds to the creator's
  variable, not the parameter; D3 refuses this on MSIL rather than guess, but the front end still
  needs its own diagnostic (VB has none today).
- **#170** — a lambda written inside `MyBase.New(...)` arguments is invisible to #122's capture
  analysis; the pass refuses it here (see the field/module-initializer refusal in the
  implementation notes) but the front end should diagnose it directly.
- **#172, #173, #174** — the three refusals this task found beyond D9's own list and had to add
  while implementing it (a `Select Case` `When` guard or pattern variable that reads a capture; a
  lambda inside a `Try`/`Catch`'s own filter expression if one is ever added; a lambda inside a
  GENERIC class) each want either a front-end diagnostic of their own or a considered decision
  about generic environments — neither is #155's to make.
- **Events and engine calls on MSIL.** D8's consequences above show two shapes — `RaiseEvent` and
  an engine entry point like `GameInit` — that have NEVER run on MSIL, before or after this task,
  and are unrelated to closures. Making them work is its own task: `RaiseEvent`/`AddHandler` need
  a lowering (there is none today, on any backend's MSIL path), and engine calls need the same
  `[DllImport]`/native-callback treatment the C# backend and `RaylibWrapper.vb` already have.

## Obligations

- File front-end diagnostics, none blocking #155: BC36639 (ByRef captured), BC36638 (`Me` in a
  Structure lambda), N9 (BC30616) and #169 (case-mismatched parameter).
  - **Done by #174**: BC36639 and N9 (BC30616), plus BC30734/BC36667 (a lambda `Dim` hiding a
    procedure's own parameter, or an enclosing lambda's) — all four reported by
    `SemanticAnalyzer` before any backend runs, so `ClosureLowering`'s own D4 (ByRef) and N9
    (declares-while-captured) refusals are now backstops, reachable only from a shape #174 does
    not cover (a sibling-block hiding, a `For Each`/`Catch` variable inside a lambda) or from IR
    that bypasses the front end's own gate. #217 (lambda parameters) is untouched.
  - **BC36638 was waiting on #230 — now done by #230** (it was unreachable: `ParseStructure` accepted
    only fields, so no Structure method could hold a lambda at all; #174 deliberately added no dead
    code for it). A Structure now declares methods, and `SemanticAnalyzer` reports BC36638 for a
    lambda in one that uses `Me` or an instance member, before any backend runs. D6's refusal in
    `ClosureLowering` (`Me` captured in a Structure method) stays as the MSIL backstop, reachable
    only from IR that bypasses the front end's own gate.
- **L15 is a recorded, all-backend divergence from VB.** BasicLang binds a loop-body `Dim` at
  FUNCTION level on every backend (C# and JavaScript print 6|6|6; VB prints 1|3|6). #155
  reproduces the oracle, not VB, so MSIL agrees with C#/JS. Fixing it is a FRONT-END task
  (IRBuilder declaration sites), filed separately; when it lands the closure pass adds one
  environment level through the existing chain. Nobody "fixes" MSIL alone toward VB.
- If the JavaScript backend gives a per-iteration `For i` binding, that is a pre-existing JS/VB
  divergence — file it; do not bend MSIL toward it.

## Rejected

- Backend-local rewrite inside MSILBackend — #140 would re-derive it; untestable without ilasm.
- One environment per function only — wrong per iteration for a declaring `For Each`.
- One environment per lambda (a copy) — loses writes (L4/L7/L11).
- A ref-cell per variable — needs a generic box through the facade or per-type boxes, plus a
  closure object anyway; more machinery, no semantic gain.
- Flattened nested captures — more classes, no benefit over the chain.
- A lambda as an instance method on the creator's class — a second target model; an
  optimisation only (so is "no captures → static method + cached delegate").
- Synthesised `Action`/`Func` via `GenerateDelegate` — never unifies with BCL parameters.
- Blocking #155 on front-end diagnostics — right home, wrong sequencing.
- Refusing escaping closures — the heap environment exists precisely to make them safe.
- Refusing `For Each` captures — the per-iteration environment answers it.
- Fixing the call canonical form in IRBuilder now — touches every backend; a separate task.
- Scope recovery in IRBuilder for #155 — changes C#/JS output; the VB rule for L15 is a separate
  decision the owner should make once for every backend.
- MSIL-only VB semantics on L15 — would make MSIL the one backend disagreeing with the oracle.

## Revisit if

- The front end gains declaration-site scoping (the L15 fix): add the block-scope environment
  level through the existing chain; do not reopen the model.
- C# and JavaScript disagree with each other on L14.

## Amendment A-140 (ADR-0019, 2026-10-01): D1 gains a second consumer, a contract with options and a result

*Appended, not edited in place: D1's text above is what was ruled for #155. What follows is what the #140
ruling (D2) added; the environment model (D2–D9) is untouched.*

- **C++ is the second backend that opts in** (`CppCodeGenerator.LowerClosures`, reached from both `Generate`
  and `GenerateSplit`). D1's "C++ may for #140" is now "C++ does". C# and JavaScript still never call it.
- **The entry point takes the backend's limits.**
  ```
  sealed record ClosureLoweringOptions(string BackendName, int? MaxActionArity, int? MaxFuncArity,
                                       UnloweredRootPolicy Policy);          // Throw | Skip
  sealed record ClosureLoweringResult(IRModule Module,
      IReadOnlyList<(IRFunction Root, ForeignFeatureException Reason)> SkippedRoots);
  static ClosureLoweringResult ClosureLowering.Run(IRModule module, ClosureLoweringOptions options);
  ```
  `BackendName` prefixes every refusal text ("MSIL:", "C++:"); the Action/Func arity caps are options
  (`null` = unbounded: D7's "capped at what was measured" is a .NET delegate-facade fact, so it is MSIL's
  option, 8 / 9, not the pass's); `UnloweredRootPolicy.Throw` is MSIL (the first refusal is thrown;
  `SkippedRoots` is empty; D1's post-condition holds verbatim) and `Skip` is C++ (a root the pass cannot
  lower is left un-lowered and reported, every other root is lowered).
- **`ClosureLowering.Run(IRModule)` is kept and means `ClosureLoweringOptions.Msil`.** MSIL's IL is
  byte-identical under it: 3,423 of 3,423 corpus cells.
- **Per-root atomicity.** A ROOT — a non-lambda function with every lambda it creates, transitively — is
  lowered entirely or not at all (D2's one environment per function makes a partial root unsound); a skipped
  root's IR in the result is the input's. Root identity is `ClosureLowering.CreatorsOf` / `RootOf`, the one
  definition the lowering and C++'s by-copy soundness rule (ADR-0019 D3) share.
- **A function is lowered once (patch 01, fixes #241).** The root loop used to meet a creator lambda it had
  already lowered under its creator and process it a second time as a root, adding a dead duplicate of every
  lambda it creates as a method of a new environment nested inside the first. MSIL never looks at a dead
  method, except that the nested class name it writes (`D/<>c__Env0/<>c__Env2`) is not a class `ilasm` knows:
  a lambda nested in a lambda inside a class member failed `ilasm`. The root loop now skips a function that
  has been lowered. The 16 programs whose IL changed (the "creator lambda placed after its creator" shape) run
  identically (13) or went from the `ilasm` failure to vbc's output (X22, X25, E01_nested_lambda); N3-N6 print
  vbc's answer.
