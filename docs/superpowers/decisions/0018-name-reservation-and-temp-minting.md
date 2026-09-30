# ADR 0018: One reservation set per function, and one door for an optimizer temp

- **Date:** 2026-09-30
- **Status:** Accepted
- **Decided by:**
  - The architect's ruling for #121 (D1–D4, the byte-identity expectation, the falsifiers), binding.
  - The orchestrator's clarifications C1 and C2 (binding), and decision 1 on the implementer's STOP:
    E1 (amends C1), E2 (the implementer's four assumptions, with obligations) and E3 (new).
  - The mechanism below the decisions is the implementer's. Measurements are marked as such.
- **Brief:** the orchestrator's `architect-brief.md`, `ruling.md`, `implementer-brief.md` and
  `decision-1.md` for task #121 (session scratchpad, not kept in the repo).
- **Task:** #121. Supersedes ADR-0017's D2 by-name rule and D4. Builds on ADR-0013 (case-insensitive
  names), ADR-0014 (`BodyLocals ⊆ LocalVariables`) and ADR-0017 (the compiler-temp marker).

## Question

IRBuilder left For Each, Catch, pattern and LINQ range variables out of `LocalVariables`, and
`IRTempNames.UserOwned` read only `LocalVariables`. So a user variable spelled like a temp (`t0`) in
one of those positions was invisible to both minters that had to avoid it: IRBuilder's renamer
(`SeparateTempsFromUserNames`) and every backend's temp counter. One name then meant two things:
- at IR level, the orphan of `-(-t0)` WAS the minted `t0`;
- at backend level, the C++ counter's `t0` overwrote the loop variable.

ADR-0017 fenced the cells (`CompilerTempCollisionFenceTests`) and kept a by-name DCE rule that held
one of them compiling. How does the program's set of names become complete, who consults it, and
how does an optimizer pass mint a temp without repeating the three unregistered passes' defect
("minted a name, declared nothing")?

## Decision

### D1: one reservation set per function, separate from `LocalVariables`

`IRFunction.ReservedNames : ISet<string>` (OrdinalIgnoreCase) holds every name a user or a
lowering declares in the function, whatever its shape. It is NOT a declaration list:
- `LocalVariables` keeps its one meaning, "what the backend declares at the top of the function";
- no backend reads `ReservedNames`.

`IRTempNames.UserOwned` is the one reader that filters by shape. It reads the union
`ReservedNames ∪ LocalVariables ∪ Parameters ∪ module-level names`, so a declaration that bypassed
reservation still cannot collide, and the verifier names the gap. `SeparateTempsFromUserNames` and
every backend's `NextTempName` are unchanged.

**The writers.** Every declaration kind and the one site that reserves it (`IRBuilder.cs` unless
noted; line numbers as of this change):

| Kind | Site |
|---|---|
| `Dim` local; tuple deconstruction | `PushVariableVersion` from `Visit(VariableDeclarationNode)` (1288), `Visit(TupleDeconstructionNode)` (1380) |
| `Const` local | `PushVariableVersion`, `Visit(ConstantDeclarationNode)` (1430) |
| Parameters: Function, Sub, interface default body, constructor, operator | `PushVariableVersion` (1134, 1215, 2135, 2384, 2815; the operator's only after its function is made current, see Measured) |
| Property setter `value`, and its declared alias (`nv`, ADR-0013 D5) | `PushVariableVersion` (2507, 2520); the push records BOTH the key and the variable's name |
| Lambda parameter | `PushVariableVersion` with the lambda as the current function (2642) |
| Counted `For` control variable, and its per-iteration version | `PushVariableVersion` (3954, 4003) |
| `For Each` control variable, including #168's hidden `__foreach_N` | `PushVariableVersion` (4139) |
| `Catch` variable | `PushVariableVersion` (4380). It used to write `_variableVersions` directly; it is now routed through the push, with the same effect on the stack |
| Pattern binding (`Case x As T`) and its re-push for the clause body | `PushVariableVersion` in `RegisterPatternBinding` (3781) and at 3633. The brief expected this kind to bypass the push; in this tree it never did |
| LINQ range variable | `PushVariableVersion` (3333) |
| A name `GetOrCreateVariable` creates (an implicit or unbound name) | its inline push is now `PushVariableVersion` (785) |
| `AndAlso` / `OrElse` carrier `__scN` | `PushVariableVersion` (5253) |
| `With` carrier `__with` (never pushed) | `ReserveInCurrentFunction`, explicitly (4176) |
| A lambda: every name of its creator, transitively | `CompleteReservations`, at the end of the build |
| ClosureLowering's clone of a function | `ModuleCloner.CloneFunction` copies the set, the shared module set, the flag and the minted record (`ClosureLowering.cs` 512–515) |
| ClosureLowering's environment locals and carriers | at each `LocalVariables` write: the loop-level env local (2355), carriers (2365), the function env local (2451, the `Insert(0, …)` the ruling did not list) |
| ClosureLowering's hoisted lambda | its parameters plus the creator's CURRENT set, env locals and carriers included, before `Process(lctx)` (984–985) |
| Module-level names (globals, class fields, properties, methods, every function's name) | one SHARED set per module, `IRFunction.ModuleReservedNames` (E3), published by `IRTempNames.PublishModuleNames` from `CompleteReservations` and from `Compiler.CombineIRModules` (1051) |

`Me` and `MyBase` are not reserved: they are keywords, never temp-shaped. `TryFoldInitializerToConstant`'s
scratch function is never part of a module.

**When the set becomes visible (E1).** The push RECORDS each name (`_pendingReservations`), and
`CompleteReservations` PUBLISHES the records into `ReservedNames`. That happens once the walk is
over and before anything reads the set: before `SeparateTempsFromUserNames`, the optimizer and
every backend. During the walk nothing is published, so `GetNextTempName` skips nothing and the
renamer separates every collision, exactly as it always has. See "Rejected" for why publishing at
the push was measured and refused.

**The invariant** (`IRVerifier.CheckInvariantR`, run by `VerifyAfterOptimization` on every compile
when verification is enabled, and on ClosureLowering's lowered module). In every function with
`TracksReservedNames` set:
- every parameter name is reserved;
- every `LocalVariables` name is reserved, except a temp the function minted and declared itself.
  The exemption is exactly `IsCompilerTemp && IsMintedTempName(name)` (E2.1). D2 requires that such
  a temp be in `LocalVariables` but not in `ReservedNames`, so the ruling's "LocalVariables ⊆
  ReservedNames" needs this one exemption;
- every name a declaring construct in its blocks introduces is reserved: an `IRForEach` variable,
  an `IRCatchClause` variable, and an `IRPatternCase.BindingVariable` (through Or and tuple
  alternatives).

A LINQ range variable has no declaring IR node, so the verifier cannot name one. It is reserved at
the same push as every other kind, and a test pins it (E2.3).

**Scope of the invariant (E2.2).** `IRFunction.TracksReservedNames` is set by IRBuilder
(`CompleteReservations`, on every function it built) and copied by ClosureLowering's clone. The test
suite runs the verifier in Throw mode, and its hand-built IR tracks nothing, so the invariant does
not apply to it. Production creates `IRFunction`s only in IRBuilder and in ClosureLowering's clone,
and `CombineIRModules` moves the functions themselves. The test-writer pins that every function
reaching a backend has the flag.

### D2: `IRFunction.DeclareTemp(type)`, the only door for an optimizer pass

`DeclareTemp` does four things:
- mints a name through `GetNextTempName`, which skips `IsReserved(name)` (the function's
  `ReservedNames` plus the shared module-level names, ignoring case) and every name it already
  handed out;
- sets `IsCompilerTemp = true`;
- records the name as minted;
- adds the variable to `LocalVariables`, so every backend declares it.

Post-condition: `v.IsCompilerTemp && v ∈ LocalVariables && IsMintedTempName(v.Name) && !IsReserved(v.Name)`.
Minted names never enter `ReservedNames`, so D4 can rely on the two being disjoint.

It works at function level only. A pass that needs a per-iteration temp adds it to `BodyLocals`
itself, as ADR-0014 obliges.

**C1 as amended by E1.** `GetNextTempName` stays available to IRBuilder, which names its SSA values
through it. It skips `IsReserved ∪ minted`. That skip bites from the moment of publication, so it
covers every minter after the walk: the renamer's own re-mint and `DeclareTemp`. An optimizer pass
never calls `GetNextTempName`. `TempMintingDoorTests.GetNextTempName_IsCalledOnlyByIRBuilderAndDeclareTemp`
reads the IL of every method in the compiler assembly, with closures, local functions and state
machines attributed to their declaring type. Its only callers are IRBuilder and
`IRFunction.DeclareTemp`.

**E3: module-level names.** `ReservedNames` is per function, so without E3 a pass could mint `t9`
in a function that calls a module function `t9()`. The resulting local would hide the function
(C#: "method name expected"; C++: the local shadows it). The names `UserOwned` collects at module
level are therefore published as ONE set, and every function holds a reference to it
(`ModuleReservedNames`). The implementer chose a shared reference over copying the names into every
function's set, which would be quadratic. `IRFunction.IsReserved` reads both sets, and so do the
skip and D4's refusal.
- `IRTempNames.ModuleLevelNames` is the one list both readers use: `UserOwned` filters it by shape,
  and `PublishModuleNames` takes it unfiltered.
- A multi-file build publishes again on the combined module. One unit cannot see another unit's
  names, and the optimizer runs on the combined module.

**The minted record travels with a clone.** `IRFunction.InheritTempRecordFrom` copies the minted
record and the counter onto ClosureLowering's clone. MEASURED: without it, a lambda's `DeclareTemp`'d
temp was an unreserved, unminted local on the MSIL clone, and Invariant R fired (see "Found by the measurements", 2).

### D3: reserve, never mangle

Adopted as ruled. No backend changes.

### D4: the fence flips, and the by-name keep becomes a verifier refusal

`LC_t0` (MSIL), `R11` (C++, both loops), `CT_wbr_t0` (C++) and every #163 collision cell flip to
VB's answer, because the names are reserved. In the ruled order:
1. the fence and the ADR-0017 Findings 3 witness search were re-run with the keep in place;
2. the keep was removed and the same search run again.

Both are measured below. ADR-0017's rule was `namesRead.Contains(value.Name)` in
`RemoveDeadInstructions`, over the names of every `IRVariable` operand `UsedValues` met, compared
ignoring case (C2, confirmed against ADR-0017 D2 and the code). It is gone from
`DeadCodeEliminationPass`. `IRVerifier.CheckInvariantT` refuses `IsCompilerTemp && IsReserved(name)`
for every value reachable from a function's blocks and every `LocalVariables` entry. By E3, that
covers module-level names too.

## Because

- `LocalVariables` drives five backends' declarations. Adding clause-scoped names to it would
  double-declare them and widen their scope (the ruling's Rejected (A)).
- Reservation only needs to be a SUPERSET of what is declared, so a flat function-wide set needs no
  clause-scope tracking.
- Both old consumers already read `UserOwned`, so completing its input fixes both at one root.
- Deferred publication (E1): inside the walk the renamer already separates every collision. The
  mint-time skip exists for minters that run AFTER the builder, and publishing earlier only
  renumbers temps in programs whose names were already reserved.

## Measured (implementer)

All numbers are from Linux (g++/clang++, node and ilasm present; no MSVC).
- "Before" is `7eae6d54` (master + #170 + #163).
- "After" is this change.
- The CLI cells use `BASICLANG_VERIFY_IR` in log mode.

**Byte compare.**
- The corpus is ADR-0017's (t172's, t174, t200, t170, the #163 probes, `probes2` and the 42 witness
  programs), plus the five samples, the 42 witness controls (`tK` renamed `vK`) and the 24 D2
  templates. That is 982 programs × 14 cells (C#, C++, JavaScript and MSIL × {CLI, CLI `-O`,
  Release `.blproj`}; LLVM × {CLI, CLI `-O`}) = 13,748 cells.
- **13,486 are identical. 262 differ, in 27 programs, and every one is in the ruled set.** A
  mechanical classifier checked that each such program spells `t\d+` in a newly reserved position:
  For Each (R11 ×2, FE ×6, LC ×4), Catch (R12 ×2, CT ×7), LINQ range (LQ ×4) and pattern (PB ×2).
- **0 cells differ outside the ruled set.** Return-code changes: 0. Verifier fires: 0 in either
  tree, the new Invariants R and T included.
- The samples are byte-identical.
- E3 on its own, on the 50 programs that spell `t\d+`: 728 of 728 cells identical.

**The fence and the witness search (execution; 42 witness programs and 42 controls; 5 backends; 3
builds: before, reservation with the keep, reservation without it).**
- Collision cells, meaning a witness cell whose status differs from its control's: **before 99 →
  with the keep 0 → without the keep 0.**
- All 99 moved to VB's answer, and none moved away:

  | Backend | Before → VB |
  |---|---|
  | C++ | COMPILE-FAIL 6, WRONG 9 |
  | C# | COMPILE-FAIL 12, WRONG 21, RUN-FAIL 3 |
  | JavaScript | RUN-FAIL 24 |
  | MSIL | WRONG 12, RUN-FAIL 12 |

- The keep against no keep: identical in all 1,176 execution cells (status and output), and
  identical in all 1,540 emitted-text cells of the 108 `t\d+`-spelling programs and controls. The
  by-name rule has no witness left, so D4 converted it.
- The fence, before → after, in all three modes each:
  - `LC_t0`: C++ WRONG → VB; JavaScript RUN-FAIL → VB; MSIL RUN-FAIL → VB (`7|3|7|4`);
  - `R11`: C#, C++ and MSIL WRONG → VB; JavaScript RUN-FAIL → VB (`3|4|3|4`);
  - `CT_wbr_t0`: C++ still matches VB, now by reservation; C# COMPILE-FAIL → VB.
- The controls `LC_x`, `R11_yz` and `CT_wbr_k` are unchanged.
- The cells that are still not OK fail exactly as their controls do: LLVM links nothing, LINQ,
  patterns off C#, ReDim on MSIL, `RD2`.

**D2's proof** (`TempMintingFacilityTests`, 119 tests).
- The test pass, registered with `OptimizationPipeline.AddPass` after the standard or aggressive
  passes, mints in eight positions. Each position is tested with its variable spelled exactly the
  name the pass would otherwise take, `tN` and `TN`:
  - Dim, parameter, For Each, Catch, pattern (C# only), lambda parameter, and a variable captured
    by a lambda: each compiles and runs to VB's answer on every backend;
  - the module function `tN()` (E3): the same;
  - the LINQ range variable: at IR level only.
- The expectations were re-run with `vbc`; the pattern and LINQ forms have no VB equivalent.
- `TempMintingDoorTests` adds the IL guard and the LINQ rows.

**Found by the measurements and fixed here.**
1. **Variant A** (see Rejected).
2. **A clone carried marked temps but not the record behind them.** On MSIL, a lambda's
   `DeclareTemp`'d temp tripped Invariant R. `InheritTempRecordFrom` fixes it.
3. **Operator parameters were reserved in the enclosing function.** `Visit(OperatorDeclarationNode)`
   pushed them before switching `_currentFunction`. Invariant R named `Box.op_Equality`'s `a` and
   `b` on the first test run. The function is now switched first.

**Tests** (the `wt` tree).
- The fast subset: 9,357 tests, **3 failures**: `DeadCodeRemovalLicenceTests` `ByName_*` ×3 (the keep
  is gone).
- The brief's filter (`CompilerTemp|DeadCode|Temp|Collision|Verifier|Closure|Lambda|ForEach|Catch|Pattern|Linq|NameBinding`,
  Integration included): 1,602 tests, **10 failures**: `CompilerTempCollisionFenceTests` `LC_t0` ×3
  and `R11` ×4, plus the same `ByName_*` ×3. All are moved pins that assert the old behaviour.
  21 tests skipped (Windows, MSVC or the engine).

**Mutants.** Each mutant was built in a detached worktree, and measured on these tests and on 8
probes × 4 backends × 3 modes. **All 16 are killed.**

| Mutant | Killed by |
|---|---|
| For Each not reserved | 37 D2 rows, Invariant R in 45 probe cells, `LC_t0` MSIL RUN-FAIL |
| Catch not reserved | 16 D2 rows; `CT_wbr_t0` C++ COMPILE-FAIL (ADR-0017's witness returns); `CT_rbw_t1` C# COMPILE-FAIL and JavaScript SyntaxError |
| Pattern binding not reserved | 4 D2 rows, Invariant R |
| LINQ range variable not reserved | the 2 LINQ IR rows only |
| Lambda parameter not reserved | 16 D2 rows, Invariant R |
| ClosureLowering does not seed the hoisted lambda | only a direct assertion: a name ClosureLowering adds to the creator is in the lambda's set (E2.4) |
| `GetNextTempName` ignores reserved names | 118 of 122 |
| `DeclareTemp` does not declare | 116 |
| `DeclareTemp` does not flag | 118 (Invariant R's exact exemption fires) |
| D4 refusal dropped | only a direct assertion: a flagged temp under a reserved name is refused with Invariant T |
| Case-sensitive reservation | 51 (every `TN` row) |
| IRBuilder does not set `TracksReservedNames` | only a direct assertion: every function, before and after ClosureLowering, has the flag (E2.2) |
| ClosureLowering does not copy the flag | the same assertion |
| Module names not published (E3) | 16 `ModuleFunction` rows |
| Clone without the minted record | 8 MSIL rows |
| IRBuilder does not seed lambdas | 16 `CapturedByLambda` rows, and the direct assertion |

The direct assertions exist only as the implementer's probes. The test-writer owns them.

## Rejected

| Alternative | Why dropped |
|---|---|
| (A) Complete `LocalVariables` | Changes emitted declarations on five backends; clause scopes become function scope (CS0136); collides with ADR-0014's `BodyLocals ⊆ LocalVariables`. |
| (C) Lazy query at mint time | IRBuilder is gone after construction and ClosureLowering adds names later, so there is no single owner; it cannot serve `SeparateTempsFromUserNames`. |
| Mangle user temp-shaped names | Cannot reach the IR-level collision; a five-consumer fix; alters debugger-visible names. |
| Change the temp prefix (`__t0`) | Renames every temp in every program; a user can still spell the new prefix. |
| Reserve only `t\d+`-shaped names | Shape is the reader's concern; the next minter with another prefix would need a second set. |
| A public `GetNextTempName` for passes beside `DeclareTemp` | Two doors, and LoopUnrolling's bare-name, no-declaration defect returns. |
| Delete the by-name rule outright, or keep it as a keep | Leaves the disjointness invariant with no caller, or hides a reservation leak as a wrong answer. |
| `DeclareTemp` taking a loop scope | ADR-0014 already places `BodyLocals` on the pass. |
| **Variant A: publish each reservation AT the push** (C1 read literally, the implementer's first build) | MEASURED: on the 50 corpus programs that spell `t\d+`, × 14 cells, it changed emitted text in **6 programs / 30 cells OUTSIDE the ruled set**. Every one is a temp renumbered in a program whose temp-shaped name was already reserved: U8 (`Dim t1`), JavaScript `const t2 = ((i + 1) \| 0)` → `t3`; U5 (fields `t0`/`t1` read in a method), JavaScript `t2`/`t3` swapped; `RD_t0`…`RD_t3` (`Dim t0()`), C# `int t13` → `t14` and the JavaScript counterparts. Behaviour, return codes and verifier fires were unchanged, but the ruling makes any diff outside the ruled set a regression. The deferred variant (adopted, E1) gave 0 cells outside the set and the same 27 programs / 262 cells inside it. |
| Copy the module-level names into every function's `ReservedNames` (E3, the other permitted option) | Quadratic in functions × module names, and every `UserOwned` call walks each copy. One shared set per module carries the same fact. |

## Revisit if

- A backend is found to READ `ReservedNames` or `ModuleReservedNames` to decide what to emit.
- A pass needs a minted name in a function it did not receive (cross-function inlining). Then the
  facility grows a target-function form.
- Invariant R or T fires on any compile. A missed declaration site is a named refusal; fix the
  writer, never the invariant.
