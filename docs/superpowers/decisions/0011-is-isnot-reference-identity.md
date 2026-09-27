# ADR 0011: `Is` / `IsNot` — reference identity on every backend

- **Date:** 2026-09-27
- **Status:** Accepted
- **Decided by:** the architect role. Transcribed verbatim from the ruling; nothing below
  the headings is editorialised.
- **Brief:** the orchestrator's `architect-brief.md` for task #185 (session scratchpad, not kept
  in the repo). Its measurements were accepted as-is.
- **Task:** #185.

## Question

`x Is Nothing`, `x IsNot Nothing` and `a Is b` did not parse anywhere except `Case Is Nothing`.
Five rulings were needed: the grammar (D1), the operand rule (D2), what C++ `Is Nothing` means on
a String or an array, which have no null state there (D3), identity whose meaning is not portable
— String and delegate (D4) — and the IR shape (D5).

Governing principle: a BasicLang program that compiles means the same thing on every backend, or
the front end refuses it. A construct that silently locks a program to a subset of targets is the
failure mode this repo already paid for (the module-call matrix).

## Decision

### D1: Grammar

`Is`/`IsNot` are binary operators at the `=`/`<>` level in BOTH parsers; `IsNot` becomes a
keyword. `x IsNot Nothing` is the supported negation. `Not` precedence is filed separately, not
moved here.

### D2: Operand rule

An operand is the `Nothing` literal or a type for which #173's classification admits `Nothing`;
value types are refused (BC30020-style); two non-`Nothing` operands must be related (one converts
to the other, or one is `Object`). With two additions: a nullable operand is admitted ONLY against
the `Nothing` literal (VB BC32127); `Nothing Is Nothing` is legal and folds (D5).

### D3: C++ `Is Nothing` on String / array

(a) emptiness, for both String and array, and this is the ONE answer for `Case Is Nothing` (#189).
(c) — a real null state for `Array<T>` — is the correct end state for arrays and is filed
separately as an `Array<T>` runtime-model change.

### D4: Non-portable identity

(a) front-end refusal on every backend when EITHER non-`Nothing` operand is statically String or a
delegate type. `Object Is Object` is admitted (the Object hatch).

### D5: IR shape

New node `IRIdentityCompare { Left, Right, Negated }` — NOT a `BinaryOpKind`. `Is Nothing` is the
same node with the `Nothing` literal as an operand; the null test is shared with
`IRNothingPatternCase` at the BACKEND (D3's helper), not by re-lowering.

## Because

- D1: moving `Not` retypes every existing `Not i = 3` — a language change smuggled into an operator
  PR. No `.bas` uses `IsNot` as a name, so the keyword is free now and expensive later.
- D2: #173's `JudgeNothingConversion` is the one classification; a second list drifts. Refusing
  unrelated operands is cheap: an always-False identity is a bug, and unrelated `shared_ptr`s do
  not compile on C++.
- D3: #173 already merged null and empty on the WRITE side (`Nothing` → `""` / `Array<T>{}`);
  `Is Nothing` is the read side of that convention, and any other read answer contradicts a merged
  write. (b) refuses the most common VB idiom with no alternative spelling on C++. (c) is right for
  a handle type, but it means null-guarding every `Array<T>` member, `ReDim`, and unsized `Dim` —
  an `Array<T>` decision, not an `Is` decision. When it lands it changes ONE helper.
- D4: no correct program depends on String reference identity even on C#/MSIL — interning makes it
  non-deterministic — so the refusal costs nothing real and buys "compiles here → compiles
  everywhere". (b) is the silent subset lock-in; (c) is a wrong answer under a green build.
- D5: a new enum member falls into every existing `default:` over `BinaryOpKind` and emits `==`
  silently; a new node is unreachable until each backend implements it — loud. Keeping
  `IRNothingPatternCase` avoids re-lowering `Select Case` on four backends.

## Contract

- **D1.** (1) Same source → identical IR from either parser: cover `If x Is Nothing Then`
  (statement-leading) and `Dim b = x Is y`, `Return x IsNot y`, argument position. (2)
  `Case Is Nothing` / `Case Is > 5` keep their dispatch; `Case Is Nothing` still lowers to
  `IRNothingPatternCase`. (3) `Not x Is Nothing` is never SILENTLY `(Not x) Is Nothing` — a
  `Not`-shaped left operand of `Is`/`IsNot` gets a diagnostic naming `x IsNot Nothing`. (It is
  loud on a class today and coincidentally right on `Integer?`; on `Object` it need not be either.)
- **D2.** (1) The predicate IS `JudgeNothingConversion`/`NothingAdviceFor` — no parallel list. (2)
  The same predicate governs `Case Is Nothing`; if applying it flips an existing test, that test
  baselined a defect. (3) Every refusal names the fix (`=` for value comparison, `.HasValue` for
  nullables).
- **D3.** (1) The C++ backend has ONE `EmitNullTest(expr, type)` used by BOTH the identity node with
  a `Nothing` operand and `IRNothingPatternCase`; #189 closes by routing through it. (2)
  Assign-`Nothing`-then-test prints identically on all four backends for every row of the brief's
  kind table. (3) The two divergences (`"" Is Nothing`, `{} Is Nothing` → True on C++ only) are
  asserted by a NAMED divergence test — measured, not accidental, and visibly flipped when (c)
  lands. (4) `Object` stays refused on C++ (pre-existing).
- **D4.** (1) With a class whose user `Operator =` always returns True, `a Is b` on two distinct
  instances is False on all four backends — emission never reaches user `=`,
  `Delegate.op_Equality`, or string value equality. (2) `s Is Nothing` / `d IsNot Nothing` for
  String/delegate stay legal everywhere.
- **D5 (optimizer).** (1) Fold only `Nothing Is/IsNot Nothing`; never rewrite `IRIdentityCompare`
  ⇄ `BinaryOp Eq/Ne` in either direction. (2) Pure: a read of both operands, no kills under
  ADR-0006; CSE-keyed on node kind + operands + `Negated`, never merged with an `Eq` of the same
  operands (which may call user code). (3) Validate via CLI, `-O`, and `.blproj` on all four
  backends.

## Obligations

Four backends + `CppCapabilityChecker` implement the node (C++: `shared_ptr ==`, empty-
`std::function` test, D3 helper; MSIL: `ldnull`/`ceq`; JS: `===` plus the existing
null-or-undefined test; C#: any spelling that cannot reach a user operator — implementer's call).
Typing lands once in `SemanticAnalyzer` and reaches the LSP for free; `IsNot` joins the keyword
list. Filed separately: `Not` precedence; `Array<T>` null state; #193; `TypeOf`.

## Rejected

- `Not` to VB precedence here — retypes `Not i = 3` repo-wide; a language change, not an operator.
- D3(b) refuse String/array `Is Nothing` on C++ — no alternative spelling; contradicts #173's write
  side.
- D3(c) in this PR — an `Array<T>` runtime decision (null guards everywhere); lands later through
  the one helper.
- D4(b) per-backend refusal — silent subset lock-in, the module-call failure mode.
- D4(c) value equality as identity — wrong answer, green build.
- `BinaryOpKind.RefEq/RefNe` — lands in `default:` cases as `==`; overloads and const-folding can
  intercept.
- Lowering `IRNothingPatternCase` to the new node — re-lowers `Select Case` on four backends for no
  semantic gain.

## Revisit if

`Array<T>` gains a real null state (D3(c)): the C++ `EmitNullTest` array arm then becomes a null
test and the named divergence test flips for arrays. If `Not` moves to VB precedence, D1's `Not`
diagnostic is retired.
