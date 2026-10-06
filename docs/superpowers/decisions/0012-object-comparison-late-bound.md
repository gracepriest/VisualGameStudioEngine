# ADR 0012: comparing an `Object` operand is VB's late-bound comparison

- **Date:** 2026-09-28
- **Status:** Accepted
- **Decided by:** the orchestrator, applying the language's no-Option-Strict rule.
- **Brief:** the orchestrator's implementer brief for task #177 (session scratchpad, not kept in
  the repo).
- **Task:** #177.

## Context

BasicLang has no `Option Strict` (`SymbolTable.cs:197`), so it follows plain VB.NET's binding
rules throughout. MSIL never boxed a value stored into an `Object` slot; fixing that boxing
exposed a comparison that was right only by accident — `ldloc o; ldc.i4.s 20; ceq` had compared a
raw `int32` with `20` and answered correctly, but once the slot legitimately holds a boxed
reference the same `ceq` compares a reference against `20` and silently answers wrong (`If o = 20`
turned `eq` into `ne`). The C# backend already relies on the VB runtime for its own text
(`CSharpBackend.VbConversionText`), so reaching for it here is not a new dependency, only the
first MSIL use of it.

## Decision

A comparison (`=`, `<>`, `<`, `<=`, `>`, `>=`) with a statically `Object` operand is VB's
late-bound comparison: both operands are boxed and
`Microsoft.VisualBasic.CompilerServices.Operators.ConditionalCompareObject*(a, b, TextCompare:=False)`
is called (`Option Compare Binary`, VB's default — two Strings compare ordinally). `Select Case`
values, ranges, `Case Is op` and `When` guards share the same comparison.

`Is` / `IsNot` and `Case Is Nothing` stay reference identity (ADR-0011 D2(2)) — never late-bound,
regardless of operand type. `Case Nothing` on an Object subject IS a value comparison (VB's
`subject = Nothing`), which differs from `Case Is Nothing` for an Object holding `0`, `""` or
`False`. The `Nothing` literal itself never makes a comparison late-bound: `i = Nothing` on an
Integer stays an Integer comparison.

## Consequences

- MSIL implements this ruling here (`EmitComparison`, `IsLateBoundComparison`,
  `EmitLateBoundCompareOpcodes`); the `.assembly extern Microsoft.VisualBasic.Core` declaration is
  conditional on at least one late-bound comparison being emitted.
- C# still owes the same ruling — today it refuses an Object comparison outright (CS0019) rather
  than emitting a call into the VB runtime the way MSIL now does. Filed as #211.
  *Update 2026-10-06: task #211 implements this ruling on C#. `CSharpBackend.IsLateBoundComparison`
  (the mirror of MSIL's: either operand statically Object, the `Nothing` literal excluded) makes
  `CompareText` emit `Microsoft.VisualBasic.CompilerServices.Operators.ConditionalCompareObject*(a, b,
  false)`; a Select Case label with such a comparison (value, range bound, `Case Is op`, an Or
  alternative, `Case Nothing` on an Object subject) is `case var _caseN when <test> [&& (guard)]:`
  (`IsLateBoundCase` / `LateBoundCaseTest`). `Is` / `IsNot` and `Case Is Nothing` stay reference
  identity. An Object holding a class instance compared with `=` now throws InvalidCastException, as
  vbc and MSIL do, instead of answering by reference. Tests:
  `CSharpLateBoundComparisonExecutionTests`. The text above is left as it was written.*
- JavaScript diverges from this ruling on `= Nothing`, `Case Nothing`, and a boxed `Is` — filed as
  #215.
- C++ has no `Object` mapping at all, so this ruling does not reach it.
- The optimizer's mixed-type constant fold (an Object holding one type compared against a literal
  of another) computes the wrong value on every backend that reaches it, independent of this
  ruling — filed as #214.
  *Update 2026-10-06: task #214 found the cause upstream of the fold. Both backends decide "late-bound"
  from the operand's IR type, and `CopyPropagationPass` erased it by replacing an Object variable with
  its recorded copy (`Dim s As Object = "20"` records the String constant, which carries its own
  type), so `s = 20` reached the backends as `"20" = 20`. Copy propagation must not erase an Object
  comparand: `CopyPropagationPass.KeepsLateBinding` leaves it in place unless the copy is itself an
  Object comparand or the `Nothing` literal. `ConstantFoldingPass.TryFoldCompare` is unchanged.
  Tests: `ObjectComparisonUnderOptimizerExecutionTests`. The text above is left as it was written.*

## Revisit if

A backend's own late-bound comparison (C#'s #211, or any future one) needs a DIFFERENT runtime
call than `Operators.ConditionalCompareObject*`, or a construct is found where VB itself does not
apply `Option Compare Binary` semantics to an Object comparison by default.
