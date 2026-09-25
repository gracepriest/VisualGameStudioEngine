<!--
Preserved verbatim as the sole input to ADR-0001 and ADR-0002. The architect role
has no search tools and a two-file Read cap by design, so this file is the entire
basis of those rulings — a reader auditing them needs it, and it originally lived
only in a session scratchpad that does not survive the container.

Written by the coordinator, who verified every fact first-hand at HEAD a36262c.
An earlier brief by the `brief` agent asserted two of these wrongly; both are
marked "## CORRECTION" below and were the two facts the decision turned on.
-->

# BRIEF — C# backend temp materialisation, and interface property accessor flags

Compiler: BasicLang. Pipeline Parser -> SemanticAnalyzer -> IRBuilder -> IROptimizer -> five backends
(C#, C++, JavaScript, LLVM, MSIL). The C# backend is the REFERENCE ORACLE: ~6,800 tests assert the other
backends against what C# emits and runs. A defect in it corrupts the standard, so its policy is not a
private matter.

Every fact below was verified first-hand by the coordinator by reading the source at HEAD a36262c.
An earlier brief asserted two of these wrongly; both are corrected here and flagged ##.

## Q1 — should the C# backend keep inlining IR temps, or materialise them as declared locals?

### Stated policy
`CSharpBackend.cs:20-26`: the backend "intentionally avoids emitting compiler-temporary locals
(t0, t1, ...) by inlining SSA IR values into C# expressions."

### The deciding branch, quoted verbatim (`CSharpBackend.cs:2871-2886`)
```csharp
if (instruction is IRCall call) {
    var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", ...);
    if (IsNamedDestination(call)) return true;
    if (!hasReturn || GetUseCount(call) == 0) return true;   // statement
    return false;                                            // inline into expressions
}
```
Use count 0 -> emitted as a statement. Use count >= 1 -> inlined. **There is no branch for use count > 1.**
A value used N times is inlined N times, i.e. EVALUATED N times. `IRInstanceMethodCall` has the identical
shape immediately below it. `_useCounts` is `Dictionary<IRValue,int>` incremented once per operand
occurrence (`:1804-1805`), so counts above 1 are representable, and `GetUseCount` (`:2911`) exists to read them.

## THE SUSPECTED MULTI-USE SOURCE IS IN THE DEFAULT PIPELINE
`IROptimizer.cs:1162 CommonSubexpressionEliminationPass`, registered at `IROptimizer.cs:1542` via
`AddPass(...)`. CSE's purpose is to replace N identical computations with ONE value used N times —
precisely the input this policy inlines N times. UNVERIFIED: whether CSE in fact fires on a
side-effecting call in a program we can write today (a characterization agent is measuring this now).
If it does, an "optimization" pass silently multiplies observable side effects on the oracle backend.

### This defect class is already MEASURED, not theoretical
Shipped in a36262c: `EmitRight` interpolated its receiver into both halves of
`str.Substring(str.Length - n)`, so `Right(Tag(), 2)` emitted `Tag().Substring(Tag().Length - 2)` and
**called `Tag()` twice**. Fixed by binding the receiver once. That was one emitter reproducing, by hand,
what `ShouldEmitInstruction` does by policy.

### The immediate symptom to be fixed (why this is being asked now)
Some consumers call `GetValueName` (`:3011`, returns the raw spelling, e.g. `t0`, and NEVER declares
anything) instead of `EmitExpression` (`:3074`, inlines the definition from `_tempDefsByName`, `:45`).
The emitted identifier was never declared -> `CS0103`. Sites: `Visit(IRForEach)` `:4097`;
`Visit(IRIndexerStore)` `:3859-3862` (collection, each index, and the value). Separately `GetOperands`
(`:3300-3358`) is a 17-case switch with **no `IRForEach` case**, so that collection's use count is 0 and
`ShouldEmitInstruction` also emits the producing call as a discarded statement.

### What the other backends do with the same IR
| backend | strategy | evidence |
|---|---|---|
| **MSIL** | materialises every temp as a real local | `_tempIndices` IRValue->slot; `EmitLoadValue` -> `ldloc` (`MSILBackend.cs:3827-3829`) |
| **JavaScript** | hybrid: refer by name if the node is in `block.Instructions`, else inline | `Bound()` (`JavaScriptBackend.cs:769,790`) |
| **C++** | same bare-identifier policy as C#, inherited from the shared base | `ICodeGenerator.cs:203` returns `t{_tempCounter++}` |
| **C#** | inline everything | above |
So the four do not agree: one materialises, one is hybrid, and TWO share the base's bare-identifier
policy. C++ shares C#'s exposure. Whether C++ is in scope of your ruling is part of the question.

### Blast radius of switching C# to declared locals
- ~34 `GetValueName` call sites in `CSharpBackend.cs`.
- ## CORRECTION: the earlier brief said the suite has no text assertions on emitted C#. **FALSE.**
  110 files under `VisualGameStudio.Tests/Compiler/` use `Does.Contain`. `CSharpLoopExitTests.cs`
  (committed at a36262c) has 7 assertions on EXACT emitted C#, e.g.
  `Does.Contain("while (i <= 4) { t = t + 1; break; }")` and
  `Does.Contain("if (n == 3) { break; }")`. Introducing declarations changes those strings. The cost is
  real and bounded: they are our own fixtures, written this week, and we can update them — but they are
  also the ONLY thing that can kill three mutants in the current suite, so they cannot simply be deleted.
- Name-collision hazard: a known open defect is `foreach` reusing a colliding name (CS0136). If temps
  become real locals they enter the same namespace as user variables. UNVERIFIED how `t{N}` names are
  guaranteed distinct from user identifiers.
- ## CORRECTION: the earlier brief reasoned "SSA should guarantee single use per value". That confuses
  single DEFINITION with single USE. SSA gives one definition; uses are unbounded, which is the whole point.

### Options as we see them (do not feel bound by these)
- **A. Minimal**: add the `IRForEach` arm to `GetOperands` and point the two consumers at `EmitExpression`.
  Cheap, no emitted-text churn. Leaves the use-count>1 hole open, and is the same mechanism as the `Right` bug.
- **B. Materialise**: declare temps as locals like MSIL. Closes the class of defect; ~34 sites; churns
  emitted text and our week-old fixtures; raises the CS0136 collision question.
- **C. Hybrid**: adopt JavaScript's `Bound()` rule — name it if it is a statement in the block, else inline.
  Middle cost; introduces a third distinct strategy across five backends unless C++ follows.

### What we need from you
Which policy the C# backend should have, stated as an invariant an implementer can check a diff against;
whether a value with use count > 1 may EVER be inlined, or only when provably side-effect-free; and
whether C++ (same base policy) is in scope now or deliberately deferred.

## Q2 — interface property accessor flags (small; included to batch the consultation)

`IRBuilder.cs:1689-1697` builds `IRInterfaceProperty` and sets
`HasGetter = prop.Getter != null`, `HasSetter = prop.Setter != null`. A bare `Property Name As String`
in an INTERFACE has no accessor bodies, so BOTH are false. Measured: C# emits `string Name { }` -> CS0548;
C++ fails to compile; MSIL RunFailed; only JavaScript works.

Verified: that construction site never sets `IsReadOnly`/`IsWriteOnly` at all, although the AST node
carries them — the CLASS-property site at `IRBuilder.cs:1332-1333` does read `propNode.IsReadOnly` /
`propNode.IsWriteOnly`. Verified the ONLY readers of `HasGetter`/`HasSetter` anywhere in `BasicLang/`
(including `BasicLang/LSP/`) are the three backends: `CSharpBackend.cs:751-752`,
`MSILBackend.cs:1026-1043`, `CppCodeGenerator.cs:1117-1119` — and all three want the same answer.

So this appears to be a two-line change at ONE construction site
(`HasGetter = prop.Getter != null || !prop.IsWriteOnly`, and the mirror), not a cross-backend semantics
change as it was previously escalated. **Confirm or reject that reading.** The reason to ask at all: it
makes the flags mean "this property HAS this accessor" rather than "this property has a BODY for this
accessor", and an interface is exactly the place where those two readings diverge.
