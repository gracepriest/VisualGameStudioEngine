# ADR 0009: `For Each x In coll` over an already-declared `x` — reuse it, or shadow it?

- **Date:** 2026-09-26
- **Status:** Accepted
- **Decided by:** the owner, ruling directly (not the architect role). The ruling is
  recorded in the fix commit's own message
  (`a454a8cf`, "For Each over an existing variable reuses it, as VB does (#168)") and
  transcribed here; there is no separate brief document for it.
- **Task:** #168.

## Question

`For Each x In coll` with **no `As` clause**, where `x` already names a variable in
scope (a local, a parameter, a module global, a class field): does BasicLang REUSE
that variable (VB's own rule) or SHADOW it with a fresh, loop-scoped variable of the
same name (the rule commit `5783e642` took as BasicLang's own scoping)?

## The measured defect

`Dim x As Integer = 0 : l = {5, 9} : For Each x In l : Next : PrintLine(CStr(x))`
printed `0`. VB prints `9`. Wrong on **all four backends × three entry points** (CLI,
CLI `-O`, Release `.blproj`) for every shape where the control variable already
existed — the implementer's F-probe table, measured on master (`82848d0`) before the
fix:

| Probe | Existing variable | Shape | Expected (VB) | Got (BasicLang) |
|---|---|---|---|---|
| F1 | local | list | 9 | 0 |
| F2 | local | array, body sums `x` | `2,13` | `0,13` |
| F6 | Double local | Integer elements | 8.25 | 0.75 |
| F7 | module global `G` | | 9 | 0 |
| F8 | class field `K` in a method | | 9 | 0 |
| F11 | local | `Exit For` mid-loop | 9 | 0 |
| F12 | by-value parameter | | 9 | 1 |
| F13 | local | `a = x + q` before, `b = x + q` after | `3,11` | `3,3` (CopyPropagation folded the second read across the loop as if `x` were unchanged) |

Controls, correct on master and required to stay so: F3 (an empty collection leaves
`x` at 42), F4 (`For Each y As Integer` and a genuinely-declaring `For Each z`: 28).

**Mechanism.** `SemanticAnalyzer.Visit(ForEachLoopNode)` always entered a new scope and
DEFINED a fresh symbol for `node.Variable`, so the loop variable silently SHADOWED any
existing one of the same name; `IRBuilder.Visit(ForEachLoopNode)` emitted
`IRForEach(node.Variable, …)` straight from that, and every backend declared its own
loop variable from the IR node — nothing ever wrote the variable the program actually
named. The numeric `For i = 1 To n` already REUSED an existing `i` (it prints `6,4` on
all four backends, unaffected by this defect) — the two loop forms disagreed with each
other about BasicLang's own scoping rule before this ruling.

## Options

- **Reuse (VB's rule).** A bare `For Each x` over an existing variable writes through
  it, every iteration, via the ordinary assignment.
- **Shadow (BasicLang's own rule, as `5783e642` took it).** The loop always declares a
  fresh, body-scoped variable; the existing one is untouched.
- **Shadow, with a diagnostic (VB's own BC30616).** Shadow, but warn that an existing
  `x` is being hidden.

## Decision

**Reuse — VB's rule.** `For Each x In coll` with **no `As` clause**, where `x` names an
EXISTING variable in scope (a local, a parameter **including ByRef**, a module global,
or an own or inherited class field, read bare) **reuses** that variable:

- Every iteration assigns the element to it through the **ordinary assignment**, so the
  same coercion an explicit `x = expr` would use applies (task #27/#28's uniform
  assignment coercion) — a widening `Integer → Double`, a narrowing `Double → Integer`
  rounded exactly as `x = d` rounds it.
- After the loop it holds the **last element assigned**: the one being processed at an
  `Exit For`, and **unchanged** when the collection is empty (nothing was ever
  assigned).
- `For Each x As T In coll` **still declares** a new variable, exactly as before this
  ruling, and may still shadow an outer `x`. VB's BC30616 (warn on shadowing) is
  **deliberately not added** — no test in the suite relied on that shadowing, and this
  ruling does not touch the declaring form at all.
- `For Each x` naming **nothing** already in scope still **declares** one (unchanged —
  there is nothing to reuse).
- A **constant, a property, or an event** named as the control variable is now an
  **error** ("`'x' is a constant/property/event and cannot be used as a For Each
  control variable. Use a variable, or declare a new one with 'For Each x As
  <type>'"). Before this ruling each was silently SHADOWED — the loop declared a
  same-named variable and, after `Next`, the name meant the constant/property/event
  again, with no diagnostic either way.
- A **type or method** name still **declares** a new variable — unchanged, and for
  different reasons each:
  - A type is VB's own rule: Roslyn declares a fresh local when a name binds only to a
    type.
  - A method is a **deliberate departure from VB**: BasicLang's pass 1 flattens every
    procedure signature — class methods included — into the global scope, and the
    standard library places names like `Val`, `Day`, `Hour`, `Min`, `Str`, `Left`, …
    there too. Refusing "resolves to a method" as a control-variable name would refuse
    an ordinary program using one of those names as a loop variable. Measured directly:
    refusing it broke `For Each val In d.Values`
    (`CppCollectionTests.Cpp_DictionaryOperations_CompileAndRun`).
- Reusing the control variable of an **enclosing `For` or `For Each`** is now an
  **error** — VB's BC30069. `For Each n In l : For Each n In l` (nested, same name,
  bare) is refused, and so is a `For Each i` nested inside a counted `For i = 1 To n`
  reusing that `i`. This is not merely VB-illegal: the enclosing loop already emits its
  control variable as **its own** iteration variable on every backend, so a nested
  store into it does not compile (C#'s `CS1656`, "cannot assign to a foreach iteration
  variable") or does not run (JavaScript's `TypeError: Assignment to constant
  variable`) once reuse actually writes to it — measured with the check removed.

This **reverses** what commit `5783e642` took as BasicLang's own scoping ("a `For
Each` variable shadows an outer local, a parameter, or an enclosing loop's variable")
and brings the two loop forms into agreement: the numeric `For` already reused an
existing `i` before this ruling, and now `For Each` does too.

## Because

- VB is the language BasicLang models (ADR-0005 D1 already followed VB for a related
  question), and VB's own `For Each` reuses an existing control variable. BasicLang's
  own prior behaviour was not a considered design — it was silent shadowing nobody
  had ruled on, discovered only because it disagreed with the numeric `For`.
- Lowering through the **ordinary assignment** (a hidden loop variable, then `x =
  hidden` visited at the top of the body) means a field store, a global store, a
  ByRef-parameter store and the assignment coercion all come from the ONE lowering
  every other assignment already uses — no backend and no per-kind store path needed
  to change.
- Refusing a constant/property/event outright is strictly more informative than
  silently shadowing it (VB itself refuses a property this way, `BC30039`), and costs
  nothing measured to work correctly today.
- BC30069 is not optional once reuse is real: without it, three of four backends
  either refuse to compile or throw at run time the moment the nested store actually
  executes — this is not a style preference, it is what happens when the same name is
  emitted as two different things (an enclosing loop's own iteration variable, and a
  written-through local) on the same backend.

## Consequences

- **The 16 pins that encoded the old shadowing were rewritten**, not merely re-valued
  — several needed a new subject entirely once the mechanism they pinned (the C#
  `ForEachVariableCollides`/`FreshForEachVariableName` rename) stopped being reached
  by a bare `For Each`:
  - `VisualGameStudio.Tests/Compiler/ForEachVariableRenameFixTests.cs` (14 tests):
    every reuse-shape value flips from "the outer value, untouched" to "the last
    element assigned"; `NestedForEach_SameVariableName_*` now asserts the BC30069
    refusal, with a new `..._WithAsT_StillShadows` sibling keeping the ORIGINAL
    360-value coverage of the rename machinery under an explicit `As T`;
    `CaseDifferingCollision_*` keeps only the two backends that case-fold (C#, MSIL:
    `43,13`) and documents the other two as task #124's known gap (C++ fails to
    compile; JavaScript throws `ReferenceError: n is not defined` under this harness's
    strict-mode ES modules); the two "emits the plain name verbatim" text pins split
    into a genuinely-declaring case (kept, unaffected) and a reuse case (rewritten to
    assert behaviour, not text — the emitted C# for a reuse is a hidden variable plus
    an assignment, never a `foreach (T x …)` header at all); `OuterLoopVariablesPlainName_*`
    keeps the `_openForEachVariables` rename-machinery coverage alive by adding an
    explicit `As Integer` to both loops, reproducing its ORIGINAL 66,99 exactly.
  - `VisualGameStudio.Tests/Compiler/MsilForEachTests.cs` (2 tests, renamed):
    `LoopVariableName_IsReused_AndHoldsTheLastElement_AfterTheLoop` (was
    `..._ResolvesBackToItsOuterMeaning_AfterTheLoop`, 99 → 3) and
    `LoopVariableBinding_ReusesAModuleGlobalOfTheSameName_WhichHoldsTheLastElement_AfterTheLoop`
    (was `..._IsWithdrawn_SoAModuleGlobalOfTheSameNameIsVisibleAfterTheLoop`, 7 → 2).
- **New coverage**: `VisualGameStudio.Tests/Compiler/ForEachControlVariableReuseTests.cs`
  (reuse across every existing-variable kind, the unaffected declaring/control shapes,
  and G4's ByRef reuse on every backend that supports ByRef) and
  `ForEachControlVariableDiagnosticsTests` (the Const/property/event/BC30069
  refusals, front-end only, and the F1 IR-level shape: `IRForEach` iterates a hidden
  `__foreach_N`, never `x`, and the body's first instruction is the store into `x`).
- **Known gaps this ruling does not fix, and does not widen:**
  - **Task #124** — the C++ and JavaScript backends do not case-fold identifiers
    (`docs/HANDOFF.md`'s pre-existing "DO NOT CASE-FOLD IDENTIFIERS" note). A
    case-differing reuse (`Dim N … For Each n`) hits it exactly the way a plain `n = 5`
    assignment to a `Dim N` already did (the implementer's probes `G38`/`G39`): C++
    fails to compile (an undeclared identifier — only `N` was ever declared);
    JavaScript, run under this suite's strict-mode ES-module harness, throws
    `ReferenceError` rather than silently keeping a stale value.
  - **Tasks #136 / #140 / #155** — a lambda that captures a `For Each` control
    variable is a separate, pre-existing lambda-capture/backend-lowering gap on
    C++/MSIL, not this ruling's mechanism.
  - **G27 — no task filed.** `Dim c As Char : For Each c In "xyz"` is now a front-end
    error ("Cannot assign value of type 'Object' to 'Char'"), because a bare `For
    Each` over a `String`'s characters infers the element type as `Object` (`String`
    is neither an `Array` nor carries generic arguments the inference walk reads) — a
    pre-existing inference gap, exposed only now because reuse runs the element
    through an assignment coercion that a fresh declaration never needed to check
    against an already-typed variable. **Not fixed here**, and no existing task names
    it; filed as a finding for the orchestrator to number.

## Rejected

- **Shadow (BasicLang's own prior rule).** Reversed outright: it disagreed with the
  numeric `For`'s own reuse behaviour, and every measured wrong answer above is this
  option's direct cost.
- **Shadow with VB's BC30616 diagnostic.** Not adopted: the owner ruled for full reuse,
  not a warned shadow, and no test in the suite depended on the old shadowing in a way
  that a warning alone would have preserved.

## Revisit if

A BasicLang program is found where VB itself does NOT reuse a bare `For Each`'s
control variable (a VB construct this ADR's reading of the rule does not cover), or
where the BC30069 refusal is measured to block a shape that VB itself accepts.
