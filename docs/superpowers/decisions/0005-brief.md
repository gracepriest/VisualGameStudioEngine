# BRIEF: Integer division semantics and CSE's invalidation boundary

Date: 2026-09-24; HEAD 8b17c47; builds on ADR-0001 and ADR-0004 D2

## Questions

Q1. **Integer division (`\`) with floating operands — convert to Long or truncate?**
     MEASURED at HEAD (compiled and run; CLI, CLI `--optimize`, Release `.blproj` all agree):

     | program | MSIL | C++ | JavaScript | C# |
     |---|---|---|---|---|
     | `7.5 \ 2` | **4** (CLng(7.5)=8, 8\2) | **3** (trunc 3.75) | **3** | **3.75** |
     | `7 \ 2.5` | **3** (CLng(2.5)=2, 7\2) | **2** (trunc 2.8) | **2** | **2.8** |

     MSIL implements VB.NET's rule (each operand converted to Long, rounding half to even, then an integer divide) — since `f74b022`, whose ADR-0004 D4 contract converts operands to the IR result type. C++ and JS truncate the float quotient. C# emits IntDiv as `/`, plain float division, which is wrong under every option below. Operand is Single/Double in real code. Docs say `10 \ 3 = 3` (BasicLang-Reference.md:81); JS backend spec says `Math.trunc(a / b)` (2026-08-04-javascript-backend-design.md). **Decision: (A) VB semantics (convert to Long, round half-even), (B) truncate the quotient, or (C) reject floating operands?**

Q2. **ADR-0004 D2's Invariant S: does it also forbid writes to a shared value's DESTINATION?**
     S verbatim (ADR-0004 lines 102-104): "for any instruction with use count > 1, no variable reachable through its replicable operands is assigned between its definition and its last use." Measured defect: `Dim a = p + q : a = Seed(0) : l(0) = p + q` with p, q locals, expected [3, 0]. C++/JS/MSIL print [0, 0]; C# prints [3, 0] only by re-emitting text. CSE records only operand reads in `Candidate.For` (IROptimizer.cs:1350-1362: reads Left, Right only), then at Invalidate (1432-1501, loop lines 1486: checks `entry.Value.Reads` against killed names, never checks destination). C# backend's `IsNamedDestination` (CSharpBackend.cs:3059-3071) and `ComputeMaterialisedTemps` (2962: `if (IsNamedDestination(v)) continue`) depend on this boundary. **Decision: (A) extend S to destination writes and fix CSE + verifier, (B) forbid CSE from returning NamedAfterVariable binops at all, or (C) fix only CSE without widening S?**

Q3. **How is an interface property's TYPE resolved? (amends ADR-0002)**
     ADR-0002 ruled only on the accessor FLAGS (`HasGetter`/`HasSetter` mean "declares this accessor"). ADR-0004 D1 batched that flag fix with the C++/MSIL accessor-signature fixes it exposes. MEASURED while implementing D1 (uncommitted, step (f)): with the flags fixed and nothing else, a primitive-typed interface property goes from working to NOT compiling on C++ — `Property Count As Integer` makes the interface declare `const int32_t& get_Count() const`, which the class's `int32_t get_Count() const` does not override. Cause: IRBuilder builds every interface property's type as a stand-in `TypeInfo(name, Class)`, so every type is "class-kinded" (passed by `const&`, and for a user Structure mapped as `std::shared_ptr`). Only visible once the flags are true.
     The implementer's call, pending this ruling: resolve plain named types through the semantic analyzer's type lookup (`InterfacePropertyType` in IRBuilder); keep the old stand-in for arrays, generics, nullables, tuples, pointers and `::` types.
     MEASURED at that change vs HEAD (4 backends × 3 entry points, compiled and run): Integer, Double, Boolean, String, a user Structure and a user Enum interface property — C# goes CS0548 → correct for every one; C++ stays correct for all (the interface now declares `Pt get_P() const = 0` / `Color get_C() const = 0`, matching the class). No cell went right→wrong. Remaining failures reproduce identically in controls WITHOUT an interface (MSIL treats a Structure as a class → NullReferenceException; JS has no Structures; `Color.Blue` types as Object). An ARRAY-typed interface property does not parse at all (`'(' is not valid inside an Interface`), so the kept stand-in is unreachable today; UNVERIFIED what it would do.
     **Decision: (A) resolve plain named types through the analyzer as implemented, stand-in for the rest; (B) resolve every type form through the analyzer now (arrays and generics are unparseable today, so untestable); (C) keep the stand-in and fix C++ to declare interface accessors by value regardless of kind.**

## Current state

ADR-0001 (accepted 2026-09-21) requires materialise-once: values with use count > 1 and not replicable must be declared locals on C#. ADR-0004 D2 (accepted 2026-09-23) confirms `IsReplicable` whitelist: literals, `Me`, parameters, locals, materialised temps, pure operators over replicable operands; never globals (non-Const), ByRef, calls, property/field/indexer loads, allocations. Invariant S guards multi-use values; verifier obligation outstanding.

SemanticAnalyzer (lines 8285-8299) assigns IntDiv's result type: floating operands → `LongType` (line 8293); integral operands → `GetCommonType(left, right)` or `IntegerType` (lines 8297-8298).

CSE and AlgebraicSimplification both gate on IsReplicable (ADR-0001 Obligations). CSE's Invalidate (IROptimizer.cs:1432) kills entries on name redefinition (IRAssignment target, IRStore address, IRValue rename). Checks only operand reads, not destination name.

## Constraints already fixed

- ADR-0001 E1: defining expression appears in output exactly once unless replicable.
- ADR-0001 E2: `GetValueName(v)` only for materialised values.
- ADR-0004 D2 Invariant S: no write to operands between def and last use.
- Temp namespace: reserved against declared names (ADR-0004 D3).

## What breaks if wrong

Q1-A vs Q1-B: the two readings differ exactly when an operand has a fractional part that rounds differently from how the quotient truncates (`7.5 \ 2`: 4 vs 3). Whichever is not chosen leaves the backends that implement it printing a different number from the others, silently.

Q1-C: rejects programs that compile today. The analyzer's own comment (SemanticAnalyzer.cs:8288-8292) says the floating-operand relaxation is REQUIRED by the earlier change that made `/` return Double: `(a / b) \ c` would otherwise become a hard error.

Q2 wrong boundary: CSE merges binops whose destination is reassigned, producing use-count-2 with operand reads unchanged. Verifier passes (S as worded). C# materialises by accident (re-emits text); C++/JS/MSIL silently read stale values.

C# is right today only because it re-emits the expression text for a replicable value; C++, JS and MSIL honour the IR merge and read the reassigned variable.

## Options the team already sees

**Q1:**
- **A** (VB.NET): convert each operand to Long (CLng, half-to-even), then integer-divide. Matches the intent stated in the analyzer's comment (SemanticAnalyzer.cs:8287: "VB.NET rounds it to Long and then divides"); note :8293 only sets the RESULT type to Long and converts nothing — conversion is each backend's job. Changes: C++ and JS (convert operands, stop truncating the quotient), C# (convert operands, integer-divide). MSIL already does this.
- **B** (truncate the quotient): float-divide, truncate toward zero, result Long (the result type is unchanged). Changes: MSIL (revert to a float divide + truncation for floating operands), C# (truncate). C++ and JS already do this.
- **C**: Reject non-integral operands at compile time. SemanticAnalyzer:8285 currently rejects only if not numeric; would require error at 8285 instead of 8293.

**Q2:**
- **A**: Extend Invariant S to destination; CSE kills on destination write too; post-optimizer verifier checks both operands and destination.
- **B**: Forbid CSE from handing out NamedAfterVariable binops; replace from fresh temp always. Adds arm at IROptimizer.cs:1318.
- **C**: Fix CSE (kill on destination too, line 1436-1455); leave S as-is and post-optimizer verifier unchanged.

---
*Corrections by the orchestrator after review (2026-09-24): Q1's measured table was missing and its entry points misnamed (they are CLI, CLI --optimize, .blproj — not the IDE); "MSIL correct" prejudged the ruling and is removed; option A wrongly said :8293 converts operands (it sets only the result type); option B wrongly said the result type becomes Double/Single; Q1-C's claim is now attributed to the analyzer's own comment.*
