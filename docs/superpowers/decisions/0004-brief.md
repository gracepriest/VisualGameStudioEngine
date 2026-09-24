# BRIEF: Four batched questions from family #111 (BasicLang)

Date: 2026-09-23; HEAD 17faa4e; consulted on ADR-0001/0002 contract

## Questions

Q1. **ADR-0002's two-line fix (IRBuilder.cs:1695-1696) to set `HasGetter`/`HasSetter` correctly: ship with backend changes or defer?**
     IRBuilder builds `IRInterfaceProperty` with `HasGetter = prop.Getter != null` / `HasSetter = prop.Setter != null`. A bare `Property Slot As String` in an interface has no accessor bodies, so both are false. The fix sets them to true for interface properties and populates `IsReadOnly`/`IsWriteOnly` from the AST (not present in `IRInterfaceProperty` today; `IRProperty` has them at IRNodes.cs:1855-1856). But the fix exposes backend defects: C++ fails to compile (class accessor signature `const std::string& get_Slot() const` does not override `virtual std::string get_Slot() = 0`; CppCodeGenerator.cs:1117-1119 emits the abstract form), and MSIL raises TypeLoadException (implementing accessors not marked `virtual final newslot`). Interface-typed access stays broken on C++ (`h->Slot` field access) and MSIL (`ldfld`; property type built as `TypeInfo(name, Class)` → `object`). ADR-0001 fences `CppCodeGenerator.cs` from change. **Decision: batch C++/MSIL fixes with the flag, or defer the shape?**

Q2. **What is the `IsReplicable` whitelist default for ADR-0001?**
     ADR-0001 requires `IsReplicable` for the materialisation gate (use count > 1 && !IsReplicable → declared local). The whitelist must default false. ADR-0001 names "parameter references" as replicable but is **silent on locals, globals and ByRef parameters**. (ADR-0001 predates `ReadsCallVisible` and says nothing about it; `ReadsCallVisible` is CSE's *kill* predicate — "may a call change what this reads" — not a replicability predicate. The implementer proposes borrowing its boundary.) Implementer proposes: replicable = parameters + locals + literals + `Me` + materialised temps + pure ops over replicable operands; never = globals (non-Const), ByRef parameters, calls, method calls, property gets, indexer loads, field loads, allocations. **Confirm this default, or modify?**
     ⚠ **Value stability, which ADR-0001 does not address:** replicable-by-purity is not enough. A multi-use `p + q` whose uses are separated by a write to `p` has no side effect, yet inlining it at each use reads the NEW `p` at the later use — a wrong answer. Whether such a multi-use value can reach the backend after `7154a88` (CSE now kills a record on redefinition) is **UNMEASURED**. The answer must say whether `IsReplicable` also requires "no write to any operand between definition and last use", or whether that is guaranteed upstream.

Q3. **IR temp namespace: C# uniquifier now + IR rename later, or IR-level rename now?**
     IR temp names `t{n}` (IRNodes.cs:1388 `GetNextTempName`, ICodeGenerator.cs:203 `SanitizeName` fallback) collide with user locals named `t0`. Measured on C# (CS0128), C++ (redefinition), and MSIL (InvalidProgramException). Options: (A) add C# uniquifier (`_declaredIdentifiers`-based) now, defer IR-level fix (changes C++ output without editing `CppCodeGenerator`; interacts with `IsTempDestination` at IROptimizer.cs:115, and ConstantFolding's private copy of the same predicate), (B) IR rename now (all backends benefit, decouples problem from C#'s emission strategy, but forces IR output churn). **Pick A or B?**

Q4. **Gating `AlgebraicSimplification`'s `2*x → x+x` (IROptimizer.cs:2995-3005) on `IsReplicable`: fix MSIL first or ship the gate?**
     The rewrite at lines 2997-3004 duplicates the operand object if it is replicable; ADR-0001 Obligation requires gating non-replicable operands. However, gating exposes a pre-existing MSIL bug: MSIL backend (MSILBackend.cs:3976 calls `MapBinaryOperator` without type conversion) multiplies `int32 × float64` without casting to common type. Test case: `2 * <Double-returning call>` prints `1E-323` on current pipeline (wrong; correct answer ~2.0); the `x+x` rewrite currently hides this by duplicating the call's result. Gating turns the gate on and exposes the MSIL bug. **Decision: fix MSIL arithmetic coercion first, or ship the gate and accept the regression in those shapes?**

## Current state

**ADR-0001** (accepted, HEAD 13411bf measured) established evaluate-once semantics: values used > 1 time and not replicable must be materialised as declared locals. `IsReplicable` is a whitelist defaulting false; CSE and AlgebraicSimplification both must respect it. C++ implementation deferred.

**ADR-0002** (accepted, same consultation) confirmed interface property accessor flags as a two-line construction-site fix: `HasGetter`/`HasSetter` mean "declares this accessor" (not "has a body"). Flagged that `IsReadOnly`/`IsWriteOnly` must be populated at the same site.

**Backend consumers** read `HasGetter`/`HasSetter`:
- CSharpBackend.cs:751-752: emits `get;`/`set;` in interface syntax
- CppCodeGenerator.cs:1117-1119: emits `virtual <type> get_<name>() = 0;` / setter
- MSILBackend.cs:1026-1043: emits `.get`/`.set` method references

**IRInterfaceProperty** (IRNodes.cs:1696-1702) carries only `Name`, `Type`, `HasGetter`, `HasSetter`. Missing: `IsReadOnly`, `IsWriteOnly`.

**IR temps** generated by two callers:
- IRFunction.GetNextTempName() (IRNodes.cs:1388): `"t{_nextTempId++}"`
- ICodeGenerator.SanitizeName fallback (ICodeGenerator.cs:203): `"t{_tempCounter++}"`

**User locals** enter C# via `_declaredIdentifiers.Add(localName)` at declaration. C# backend's `IsNamedDestination` (CSharpBackend.cs:2920-2932) matches against this set to distinguish "this temp is a field shadow" from "this is an ordinary t0 temp"; currently relies on `NamedAfterVariable` flag to avoid false positives, but collision still occurs when a user names a local `t0`.

**CSE's ReadsCallVisible** (IROptimizer.cs:1507-1529) returns true for: globals (non-const), ByRef variables, calls, field/indexer loads, allocations, and any unrecognized IR shape. False for constants and local variables.

**AlgebraicSimplification** (IROptimizer.cs:2995-3005) rewrites `2 * x` to `x + x` unconditionally. If `x` is a call, use count on the operand object doubles but no gate prevents inlining both instances (pre-ADR-0001 oracle defect, measured; fixed by use-count materialisation once `IsReplicable` gate lands).

**MSIL binary op** (MSILBackend.cs:3948-3992) loads left and right operands, emits mapped operator, stores result. No intermediate type conversions on the stack. `MapBinaryOperator` (TypeMapper.cs:63) returns MSIL opcode only (e.g., `mul`); operand type coercion is caller's responsibility (unimplemented today).

## Constraints already fixed

- **ADR-0001 contract E1**: every reachable instruction's defining expression appears in output exactly once unless `IsReplicable(I)`. C++ bound by same invariant; implementation deferred.
- **ADR-0001 contract E2**: `GetValueName(v)` only for values already materialised as declared locals.
- **ADR-0001 obligations**: CSE and AlgebraicSimplification must gate non-replicable operands; use-count gate on materialisation confined to multi-use non-replicable values.
- **ADR-0002 contract**: on `IRInterfaceProperty`, `HasGetter`/`HasSetter` mean "declares this accessor"; `IsReadOnly`/`IsWriteOnly` are source of truth and must be set at construction site (IRBuilder.cs:1689-1697). Class properties untouched. Assumption (stated, not verified): read only through interface path.
- **Temp identifiers** (ADR-0001 contract): must live in a namespace users cannot enter (prefix lexer rejects, or uniquified against declared names). Materialisation puts them in scope.

## What breaks if wrong

**Q1 / C++ backend**: class properties trying to override interface properties emit non-matching signatures and fail C++ compilation (measured with clang++ -std=c++20: the class stays abstract because `const std::string& get_Slot() const` does not override `virtual std::string get_Slot() = 0`). ⚠ This case (access through the CLASS-typed variable) **works today** on C++, JS and MSIL — the flag fix alone turns it from working to broken on C++ and MSIL. **MSIL backend**: property override TypeLoadException (accessor metadata mismatch). **Interface-typed access**: C++ field access through interface reference (`h->Slot`) and MSIL ldfld both stay broken (property type inference broken).

**Q2 / IsReplicable semantics**: wrong default (true vs false) or wrong predicate silently miscompiles multi-use values (inlines without materialising, duplicates effects). Bounds CSE and AlgebraicSimplification on all four backends.

**Q3 / temp collision**: user local named `t0` is treated as temp or shadows class field (C# wrong assignment), or redeclares in C++ (C2374), or InvalidProgramException on MSIL. All four backends compile affected code to run incorrect user programs.

**Q4 / MSIL arith**: unmasked int32 × float64 prints a wrong answer (`1E-323` instead of the correct product — the bit pattern is being reinterpreted, not rounded); gating the rewrite exposes this on the aggressive pipeline (CLI `--optimize`, and a Release `.blproj`, which defaults to aggressive for C#/JS/MSIL). It is ALREADY wrong today on the standard pipeline (CLI without `--optimize`). Also relevant, **UNMEASURED**: the implementer expects the C# backend's use-count materialisation alone (ADR-0001's `ShouldEmitInstruction` arm) to fix the C# double-print of `2 * Tag()` even with the rewrite ungated, which would make the gate defence in depth for C# rather than the fix. No fix = gate ships a regression; fix-MSIL-first = blocking other optimizations on completing coercion logic.

## Options the team already sees

**Q1:** 
- **A** (defer): ship ADR-0002's two-line fix only, accept interface property breakage on C++/MSIL until CppCodeGenerator fence lifts.
- **B** (batch): fix C++ accessor override and MSIL virtual/final flags in same change, lifting fence only for this measured defect.

**Q2:**
- **A** (measured default): params + locals = true, globals + ByRef = false, matching CSE's ReadsCallVisible.
- **B** (stricter): only params = true, locals = false (more conservative, less inlining).
- **C** (broader): add user-provided `[Pure]` attribute (contradicts ADR-0001's "unprovable in general; whitelist defaults false").

**Q3:**
- **A** (C# localizer): add uniquifier pass in CSharpBackend before emission (scoped fix, defers IR-level churn, leaves C++/JS/MSIL collision in place).
- **B** (IR rename): rename temps during IR construction or optimization (all backends benefit, no per-backend workarounds, requires ConstantFolding and IsTempDestination updates, changes observable C++ output without CppCodeGenerator edit).

**Q4:**
- **A** (fix MSIL first): implement type coercion in MSILBackend.EmitBinaryOp or MapBinaryOperator, gate the rewrite after fixing, accept delay.
- **B** (ship gate + regression): add IsReplicable gate to AlgebraicSimplification now; document MSIL int×double as a pre-existing defect (already wrong on the standard pipeline), unblock materialisation.
- **C** (neither blocks the other): ship backend materialisation first (fixes C# without touching the rewrite), then the MSIL coercion fix, then the gate — ordering so no shape goes from right to wrong at any commit.

---
*Corrections by the orchestrator after review (2026-09-23): Q2's attribution of `ReadsCallVisible` to ADR-0001 was wrong and the value-stability point was missing; MSVC error codes were cited for a clang measurement; Q4's "correct answer ~2.0" was a guess; option Q4-C added.*

