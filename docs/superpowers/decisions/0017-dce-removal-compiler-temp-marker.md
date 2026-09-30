# ADR 0017: DCE removes only COMPILER TEMPS — a minted-name marker, never spelling

- **Date:** 2026-09-29
- **Status:** Accepted. D2's by-name rule ("no `IRVariable` operand spells its name") and D4
  (#121) are superseded by ADR-0018 D4 and D2: the names are reserved, the rule became the
  verifier's Invariant T, and a pass mints through `IRFunction.DeclareTemp`, never
  `GetNextTempName`.
- **Decided by:**
  - The owner, bindingly: "fix #163" means switch `DeadCodeEliminationPass`'s instruction removal
    ON, guarded so that a user variable spelled like a temp is never removed. The guard is a real
    temp marker, not spelling. STOP if it proves UNSAFE.
  - The mechanism (D1–D4) is the implementer's, under that decision and the #163 implementer brief.
  - The orchestrator ruled on the measured findings ("Findings" below): 1 and 2 accepted, and 3
    settled by a witness search, which keeps the by-name rule.

  Nothing here was put to the architect. Measurements are marked as such.
- **Brief:** the orchestrator's `implementer-brief.md` for task #163 (session scratchpad, not kept
  in the repo).
- **Task:** #163. Builds on #118 (DCE's use analysis made total), ADR-0016 / #170 (`MyBase.New` is
  an instruction whose operands `UsesOf` returns) and ADR-0008 settled point 3. Interacts with
  #121 (D4).

## Question

`RemoveDeadInstructions` skipped every value whose name was non-empty and did not start with
`_tmp`. IRBuilder names its temps `t0`, `t1`, …, so no IRBuilder temp was ever removed. #118
measured the obvious switch-on, the spelling test `IsTempDestination(v.Name)`: 62 removals over
the suite, 37 of them USER variables spelled like temps (`Dim t5 As Integer = a + b` printed 12
for 82 in 12 of 12 cells), and 9 failures. How does DCE tell a compiler temp from user storage,
what may it delete, and how is ADR-0008 settled point 3 kept?

## Decision

### D1: The marker — `IRValue.IsCompilerTemp`

**Contract:**
- `IRFunction.GetNextTempName()` is the one minter of temp names, and it RECORDS each name it
  hands out. `IRFunction.IsMintedTempName(name)` answers from that record, ordinal: a user `T5` is
  not the minted `t5`.
- `IRBuilder.MarkCompilerTemps()` is the one place the flag is SET. It is the last step of
  `IRBuilder.Build`, after every rename IRBuilder makes (`TryRenameToVariable`,
  `SeparateTempsFromUserNames`). It walks every value reachable from every function body (block
  instructions and their operand trees) and sets the flag when all three hold:
  - the value is not an `IRVariable` or an `IRConstant`. A user's local, parameter, global or
    field is an `IRVariable`.
  - the value is not `NamedAfterVariable`. `Dim t5 = a + b` is ONE `IRBinaryOp` renamed `t5`,
    whose later reads are reads of the variable, never operand uses of the binop.
  - its function minted its name.
- It is never set on user storage. `SeparateTempsFromUserNames` runs first and renames every
  value, other than a variable, a constant or a renamed value, whose name the program declares. So
  a minted name that survives to the marking step is never one the program owns.
- An optimizer pass that REPLACES a value copies the flag, with `NamedAfterVariable` and
  `SourceLine`, in the one replace helper, `OptimizationPass.InheritIdentity`. The replacement
  keeps the original's minted name and its consumers (`ReplaceUses`), so it is the same temp.
- The default is false. A value built anywhere else is not a compiler temp, and DCE never deletes
  it. That covers hand-built IR, ClosureLowering's clones (which run after the optimizer) and the
  UNREGISTERED inliner, loop unroller and induction-variable passes. For those names
  (`_inline_…`, `_u…`, `_div_…`) the old guard gave the same answer.

### D2: The removal licence

An unused value instruction is deleted only when all of these hold:
- it is a compiler temp (`IsCompilerTemp && !NamedAfterVariable`);
- its kind is pure and cannot trap:
  - `IRBinaryOp`, except `/`, `\` and `Mod`: an integer division by zero throws, and LICM keeps
    the same exclusion;
  - `IRUnaryOp`, except `++` and `--`, which write their operand;
  - `IRCompare` and `IRIdentityCompare`;
  - `IRLoad` of a local's own storage (an `IRVariable` or `IRAlloca` address). An element read
    goes through an `IRGetElementPtr`, which C++ folds INTO the load.

  A call, a store, an `IRBaseConstructorCall`, a throw and an await are never among these kinds;
- the one kill vocabulary agrees: `NamesWrittenBy` is classified, not Universal, not a call, and
  names nothing but the value's own name;
- it is unused by identity (`UsedValues`: `UsesOf` over every block, through operand trees), AND
  no `IRVariable` operand spells its name. This "by-name rule" only ever KEEPS a value, and matters
  only when a temp-spelled user variable sits where IRBuilder does not reserve it: a For Each,
  Catch, pattern or LINQ range variable, the #121 gap. Its witness is recorded under Findings, 3.

### D3: ADR-0008 settled point 3 is enforced in the pass

`DeadCodeEliminationPass.KeepsOperandMaterialisation` refuses a deletion that would leave an
operand of the deleted value with at most ONE operand use in the function's blocks, when that
operand is an instruction (not a variable or a constant) and not replicable
(`IRReplicability.IsReplicable`). The counts are updated as deletions happen within a run.

- **Two uses to one** is settled point 3 itself. The C# backend declares a local for a
  non-replicable value with more than one use and inlines every other value. After the deletion it
  would evaluate the value AT its one remaining use, away from its definition.
- **One use to none** is its twin. C# had inlined the value into the dead temp, which it never
  emits, so the value was never evaluated. After the deletion C# would emit it as a statement.

This is deliberately stricter than "single use and not adjacent". An adjacent single use still
changes C#'s text: a declared local becomes an inline expression.

### D4: #121 — the optimizer cannot declare a variable it mints

Not solved here, and not made harder. No REGISTERED pass mints a temp name today. The ones that do
(`FunctionInliningPass`, `LoopUnrollingPass`, `InductionVariablePass`) are unregistered for
defects that include exactly that. Their values carry no marker, so DCE leaves them alone, as the
old guard did.

When #121 gives the optimizer a minting facility, it must:
- mint through `IRFunction.GetNextTempName`, so the name is recorded, and skip names the program
  owns (`IRTempNames.UserOwned`). `SeparateTempsFromUserNames` ran long before, and the counter
  does not consult it;
- set `IsCompilerTemp` on the value instruction that carries the name, if DCE should be allowed
  to clean it up.

A minted `IRVariable` is never a compiler temp in this sense.

## Because

- The owner's rule is about provenance: "did the compiler make this name up?". The minter is the
  only code that knows, so the fact is recorded where the name is made, and read once the name is
  final.
- `NamedAfterVariable` already marks the one way a minted name becomes user storage. D1 excludes
  it at the source, and D2 re-checks it, so a single slip in either place cannot delete a store.
- The pass's own kind list and the kill vocabulary answer the same question ("does this do
  anything but define its own name?"). D2 requires both, so neither can drift alone.
- Settled point 3 is a property of a REMOVAL, not of the IR a removal leaves behind. IRBuilder's
  own expression trees already leave non-replicable values single-use and non-adjacent
  (`F() + G()`: `F`'s temp, then `G`'s, then the add). A post-hoc IR check cannot tell those
  apart, so the property is enforced where the removal is decided, and pinned by a test.

## Rejected

- **The spelling test (`IsTempDestination`, `t\d+`):** #118's measurement above. It is the
  owner's explicit "never".
- **Setting the flag at each `GetNextTempName` call site in IRBuilder** (about 60 sites): these
  are ad-hoc spots. A missed site is an unmarked temp: safe, but invisible, and a new lowering
  would have to remember it.
- **A reference-identity record of the minted string objects:** equivalent in practice, but its
  soundness would rest on string object identity, which no reader expects.
- **Settled point 3 as an `IRVerifier` invariant:** see Because. The verifier sees the IR after
  the fact, and the hazard is about which value LOST a use.
- **Removing every pure kind without the trap exclusions:** a division by zero, an out-of-range
  element read on C++ and a `++` are not pure. Deleting them deletes behaviour.

## Revisit if

- A pass starts minting temps (#121): D4's two obligations apply.
- A backend stops materialising every value at its definition (C++, MSIL and JavaScript do today),
  or C#'s materialisation rule changes: D3's threshold is C#'s.
- The other spelling-based decisions — `NamedDestination`, CSE's merge arm, Peephole's
  `ApplyRewrite` temp path, ConstantFolding's `IsNamedVariable` — are moved onto the marker. Each
  is a separate, measured behaviour change. None is made here.

## Findings (implementer, measured; ruled by the orchestrator)

All of these are in probes written for #163. The 854 programs of the scratch corpus are
byte-identical, and DCE removes nothing in them. (The in-repo test programs are a different set,
where two do lose an orphan: see "Measured at landing".) The brief's STOP list was stricter than the
owner's "STOP if unsafe". The orchestrator ruled findings 1 and 2 not unsafe, since they are
output changes toward VB (the oracle is `vbc`) with no regression, and settled 3 by a witness
search.

1. **ACCEPTED. Today's guard is not latent for a user variable named `_tmp…`.** It removes every unused
   value whose name starts with `_tmp`. A user's `Dim _tmp1 As Integer = a + b` is such a value:
   its renamed binop has no operand use, since later reads go through the variable. Before this
   change, `U2_tmp1` printed `0` for VB's `67` in 12 of 12 cells; after it, `67` in 12 of 12. This
   is the owner's rule taking effect, and contract item 3 requires it. The brief's premise
   ("today the removal is latent") holds only for names IRBuilder minted.
2. **ACCEPTED. A dead-store cascade.** With the orphan temp gone, two stores to one variable can become
   adjacent, and the peephole's existing dead-store arm drops the first. `R7`:
   `Dim n As Integer = 0 : n = (a * 3) * 0` loses one `n = 0` on all five backends, C# included.
   Output is identical (`0 | 8 | 7`, VB's). The byte diff falls outside "removed unused compiler
   temp", so the classifier names it `DEADSTORE-CASCADE`.
3. **SETTLED BY A WITNESS: the rule is KEPT. A backend temp that aliases a For Each
   variable.** IRBuilder leaves `For Each`, `Catch` and
   pattern variables out of `LocalVariables` (the #121 gap), so `IRTempNames.UserOwned` does not
   reserve them. The C++ backend's own temp counter then emitted the orphan of
   `(t1 * 2) * 0 + t1` as `t1 = t1 << 1`, overwriting the user's loop variable `t1`. `R11_foreach_t0`
   (VB `3 | 4 | 3 | 4`) printed `-3 | -4 | 6 | 8` on C++ before, and `-3 | -4 | 3 | 4` after.
   C#, MSIL and JavaScript are unchanged (`-3 | -4 | 3 | 4`, or a JavaScript `ReferenceError`).
   The first loop's `-3 | -4` is the same collision at IR level: the orphan of `-(-t0)` IS the
   minted `t0`. D2's by-name rule keeps that orphan, and so keeps the loop wrong.
   - WITHOUT the by-name rule (mutant M8), `R11` prints VB's `3 | 4 | 3 | 4` in 12 of 12 cells.
   - No registered pass builds a variable that names a temp. So the by-name rule's only measured
     effect is to hold such a collision byte-stable.

   **The witness search.** The orchestrator's rule: keep the by-name rule only if removing a temp
   whose name a variable spells changes an output, a compile result or a verifier result on some
   backend and mode.
   - **The probes** (`S/t163/witness`, 42 programs):
     - one user variable named `t0`, `t1`, `t2` or `t3` in each position the #121 gap leaves
       unreserved: For Each, Catch, a pattern binding (`Case t0 As Integer`), a lambda
       capturing a For Each variable, and a LINQ range variable;
     - For Each and Catch in both orders: the variable read before the colliding temp's
       definition, and after it;
     - two positions that turned out to be unreachable: ReDim without Dim is rejected
       ("Undefined identifier"), and BasicLang has no `Static` local;
     - lambda parameters, which are reserved as parameters.
   - **The method:** the shipped build against the no-rule build (M8, from a detached worktree),
     on all five backends × three modes (588 cells), both text and execution.
   - **The witness: `CT_wbr_t0`** (`Catch t0`, with the orphans before the Try). Without the rule
     the orphan `t0 = -a` goes. The C++ backend's own temp counter renumbers, and the surviving
     string temp becomes `t0` inside the catch: `t0 = BasicLang::String(t0.what())`, which is
     "no viable overloaded '='". That is **OK before #163, OK with the rule, COMPILE-FAIL
     without it**, in C++ CLI, CLI `-O` and Release. So the rule is kept. It is pinned for the
     test-writer by `CT_wbr_t0` on C++.
   - **The price:** the rule holds `R11` and the other collision probes at their pre-#163 answers.
     Without it, C#, C++, JavaScript and MSIL reach VB's output in 72 cells that are wrong
     or failing today (C++ 9, C# 30, JavaScript 15, MSIL 18), while `CT_wbr_t0`'s 3 C++ cells
     regress. Name collisions stay #121's to fix, by reserving these names.
   - **Controls:** the same three shapes with ordinary names (`x`, `k`, `y`/`z`) are VB-correct
     and identical before and after (42 cells). Every change in this family needs a
     temp-spelled, unreserved user name.
   - **Before → after with the rule, on the witness set:**
     - toward VB: C++ COMPILE-FAIL → OK in 12 cells and WRONG → OK in 9; C# WRONG → OK in 3;
       MSIL RUN-FAIL → OK in 3;
     - **one change that is NOT toward VB: `LC_t0` on MSIL**, WRONG (`7|-3|7|-4`) → RUN-FAIL
       (prints `7`, then fails), in 3 cells. The program's For Each `t0` is captured by a
       lambda. With `-a`'s orphan gone, the MSIL output conflates the delegate's local with
       another slot (`ldloc.3` where `stloc.s 7 / ldloc.s 7` stood). The program is already
       wrong on C#, C++ and MSIL before #163, and JavaScript throws a `ReferenceError`.
     - The by-name rule does not reach this case: no variable in `Run` spells the removed
       temp's name. It is recorded as OPEN, and belongs to #121: reserving the name removes
       the collision.

## Measured at landing (implementer — not part of the ruling)

- **Byte compare.** Before is `b0f12d90`; after is this change. The corpus is t172's, extended as
  #170 did (t170 and t200 included), plus the five samples and 18 #163 probes: 872 programs × 14
  cells (C#, C++, JavaScript and MSIL × {CLI, CLI `-O`, Release `.blproj`}; LLVM × {CLI, CLI `-O`})
  = 12,208 cells.
  - **The final re-run** adds `probes2` (R11, R12) and the 42 witness programs: 12,824 cells.
    - 12,400 identical and 424 differing, **all in the #163 probe sets**. The 854 pre-existing
      programs (11,956 cells) are identical.
    - Of the 424: `REMOVED-TEMP` 269, `DEADSTORE-CASCADE` 14 (R7), and `OTHER` 141 (U2, plus the
      temp-spelled-collision programs, where the classifier rightly refuses to call a line that
      writes a user-named identifier a temp).
    - Verifier fires: 0 in either tree. Return-code changes: 0.
    - Execution, before → after, across all 62 new probes: every change is toward VB except
      `LC_t0` on MSIL (Findings, 3). That leaves U2 at 12 cells to VB; R11's C++ second loop to
      VB; and, in the witness set, 27 cells to VB and 3 WRONG → RUN-FAIL.
  - 12,125 cells are identical, and 83 differ, all in 7 of the #163 probes.
  - The classifier (scratch `classify163.py`) proves, per file, that after = before minus deleted
    lines, up to a per-function bijective renaming of backend temps. Every deleted line defines or
    declares a removed temp. JavaScript source maps are decoded and compared line by line, and an
    MSIL `.maxstack` may only shrink. A negative control (before and after swapped) and mutant M1
    are both caught.
  - The 83 cells split as: `REMOVED-TEMP` 55 (R1, R2, R8, U4, U5, on C++, JavaScript, LLVM and
    MSIL; C# never differs), `DEADSTORE-CASCADE` 14 (R7), and `OTHER` 14 (U2: STOP finding 1).
  - Return codes changed: 0. Verifier (`BASICLANG_VERIFY_IR`, log mode): 0 fires in either tree.
- **Removals** (per-cell trace, scratch build): 140, all in the #163 probes. By kind: `Neg` 56,
  `Not` 56, `Mul` 14, `Shl` 14. The counts are identical on every backend and mode, since the IR is
  backend-independent. **0 removals in the 854 pre-existing scratch-corpus programs.**
- **The in-repo test programs (test-writer, measured).** A harvest of the 674 buildable program
  strings in the test assembly finds DCE removing orphans in exactly two:
  - `NotPrecedenceExecutionTests.Program` loses one `Not`;
  - `OptimizerOrphanedTempTests.FoldProgram` loses one `Neg` and one `Not`.

  Both are marked, minted, pure temps with no use, which is the licence working as intended, and
  both tests still pass. `DeadCodeRemovalOnRealIrTests` pins exactly these two and 0 everywhere
  else.
- **Kept on purpose.**
  - Every unused value spelled like a temp that is user storage (t118's `T5`; U1–U8's `t5`, `T5`,
    `_tmp1`, `_t3`, `_t0`, fields `t0`/`t1`, global `t3`) carries no marker. The spelling guard
    would have taken all of them.
  - Marked temps refused by kind: R6's `\` (traps), R9's `++` (writes its operand) and R10's
    element load.
- **Settled point 3.** 0 occurrences in the pre-existing corpus. In the probes, D3 refused 3
  removals, each in all 14 cells (42 refusals in all):
  - R3: `Tag()`'s one remaining use adjacent;
  - R5: one adjacent refusal, and one strict "single use, NOT adjacent" refusal (`F()` in
    `Show2(-(-F()), G())`), settled point 3's own shape, built to hit it.

  Without D3 (mutant M6), C# re-emits `int t0 = F(); Show2(t0, G())` as `Show2(F(), G())`. That is
  a text change, and in these shapes the order survives, so output is unchanged.
- **Execution.** Every probe was re-run on both builds: C#, C++, JavaScript and MSIL × 3 modes,
  plus LLVM via clang, which fails to link on every probe in both builds (pre-existing). The only
  changes are STOP findings 1 and 3. `U1`–`U8`, the user-temp spellings, print VB's output in
  every executable cell. The expectations are checked against `vbc`; `R9`'s `++` has no VB
  equivalent.
- **Tests.** Fast subset: 9,142, of which 3 fail. The brief's filter (1,200 tests, Integration
  included): 3 fail, the same 3. All are the hand-built "unused value is removed" controls in
  `DeadCodeEliminationUseAnalysisTests`, whose values are named `_tmp…` or unnamed and carry no
  marker (lines 549, 560, 573). The use-analysis KEPT tests there still pass, but now VACUOUSLY:
  their values are never removable, so they would stay green with a broken use walker.
- **Mutants** (built in a separate worktree, measured on the 18 probes against this change; LLVM
  excluded from execution):

  | Mutant | Emitted text | Execution |
  |---|---|---|
  | M1: licence reverted to spelling (`IsTempDestination`) | U5, U6, U7 | U5, U6 wrong in 12/12 cells each. U1's `Dim t5` is NOT caught: D2's by-name rule keeps it (the same function reads `t5`) |
  | M2: flag not copied on replace | R2: the strength-reduced orphan `a << 1` stays | unchanged (code size only) |
  | M3: a call made removable | R3, R5, R6, R10 | R3 (the `tag` print lost), R5, R6, R10 wrong |
  | M4a: flag set on a user `Dim` | none | none: D2's own `!NamedAfterVariable` re-check masks it. Only an IR-level assertion on the flag sees it |
  | M4b: M4a, and the guard trusts the flag alone | U5, U6, U7 | U5, U6 wrong in 12/12 cells each |
  | M5: the `IRBaseConstructorCall`'s operands invisible to DCE | R4 | Invariant P(a) fires in 12/12 cells; C++ does not compile, MSIL throws `InvalidProgramException` |
  | M6: settled point 3 not enforced | R3, R5 (C# materialisation flips) | unchanged |
  | M7: `\` / `/` / `Mod` removable | R6 | R6 wrong in 9 cells (the division's exception is lost; C# was already wrong) |
  | M8: no by-name use | none on the 18 probes | `CT_wbr_t0` stops compiling on C++ (the WITNESS: OK becomes COMPILE-FAIL in 3/3 modes); `R11` and the other collision probes become VB-correct (Findings, 3) |
  | M9: `++`/`--` removable | none | none: the kill-vocabulary cross-check still refuses (it writes `a`) |
  | M10: element load removable | none | none: D3 refuses (the element pointer would lose its only use) |
