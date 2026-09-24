# Brief: what should the loop representation be in `BasicLang/ControlFlowGraph.cs`?

Measured 2026-09-22 at HEAD `ef69a2c`, branch `claude/jolly-pasteur-l4mpzs`. Tree clean;
all probes reverted and verified by `cmp`. No recommendation is offered below.

## Q1. Repair the back-edge predicate in place, or replace the loop representation?
## Q2. Does the fix ship alone, or must LICM / LoopUnrolling / LoopFusion move with it?
## Q3. Should `For Each` (no back edge at all) be brought into the same representation?

---

## F1. The mechanism is one inverted predicate

`ControlFlowGraph.cs:274-291` `FindBackEdges()`:

```csharp
foreach (var successor in block.Successors)
{
    // Back edge: successor dominates block
    if (successor.Dominators.Contains(block))      // :283
        backEdges.Add((block, successor));
}
```

`b.Dominators` = blocks that dominate `b` (`ComputeDominators`, `:120-171`).
So `successor.Dominators.Contains(block)` tests **"block dominates successor"** — a
FORWARD edge. A back edge tail→head requires **head dominates tail**, i.e.
`block.Dominators.Contains(successor)`. The predicate is inverted: it returns every
edge that is *not* a back edge, and never the real one.

- `ComputeDominators` is **CORRECT**. Measured: an independently recomputed dominator
  set matched `block.Dominators` on all 13 shapes; zero `DOMDIFF` rows.
- `IdentifyLoops` (`:296-324`) worklist body is **CORRECT** given correct back edges.
- `IsReducible` (`:443-458`) is inverted the same way; the two inversions cancel, so it
  is a tautology returning `true` always. **It has zero callers.**

## F2. Measured loop sets, SHOULD vs DOES (13 shapes, `raw` IR, function `Main`)

| Shape | blocks | SHOULD | DOES | Notes |
|---|---|---|---|---|
| single `For` | 5 | 1 | **4** | quoted figure reproduced verbatim |
| nested `For` | 9 | 2 | **8** | |
| two sibling `For` | 9 | 2 | **8** | |
| `While` | 4 | 1 | **3** | |
| `Do While` | 4 | 1 | **3** | |
| `For` + `Exit For` | 7 | 1 | **6** | |
| `For` + `If/Else` | 8 | 1 | **6** | |
| `For` + `Try/Catch` | 8 | 1 | **7** | |
| **`For Each`** | 3 | **0** | **2** | no back edge exists — see F6 |
| **no loop at all** | 4 | **0** | **2** | ⚠ reports loops in a loop-free function |
| single basic block | 1 | 0 | 0 | only correct row |
| counted, call-free | 5 | 1 | **4** | |
| two same-bound loops | 9 | 2 | **8** | |

Single `For`, verbatim — matches the reported figure exactly:
```
LOOPSHIP#0 = [for0.cond entry]
LOOPSHIP#1 = [for0.body for0.cond entry for0.inc]
LOOPSHIP#2 = [for0.end for0.cond entry for0.inc for0.body]
LOOPSHIP#3 = [for0.inc for0.body for0.cond entry]
```
Correct: `[for0.cond for0.inc for0.body]`, head `for0.cond`, latch `for0.inc`.

Rule: reported-loop count == (edge count − real back-edge count). Every loop contains
`entry`; `LOOPSHIP#2` contains the loop's own **exit** block.

Flipping `:283` to `block.Dominators.Contains(successor)` made reported == correct on
**13/13** shapes. That one token is the entire analysis defect.

## F3. What each consumer does with the result

Only **5** live call sites, all in `IROptimizer.cs`. Nothing outside it.

| site | pass | uses | registered? |
|---|---|---|---|
| `:816` | `DeadCodeElimination` | `Build()` + `RemoveUnreachableBlocks()` only — **no dominators, no loops** | standard |
| `:1338` | `LoopInvariantCodeMotion` | loop set, header, **preheader** (`:1355`, `:1360`) | aggressive |
| `:2710` | `LoopUnrolling` | loop set, `.cond`-named header, preheader (`:2801`), trip count | aggressive |
| `:3032` | `InductionVariable` | loop set | **disabled** `:1723` |
| `:3175` | `LoopFusion` | loop **list** (pairs), `.cond` headers, bounds | aggressive |

- `:816` DCE is **insensitive** to a loop fix — it never calls `ComputeDominators`
  or `IdentifyLoops`. It is the only standard-pipeline consumer.
- **Nothing outside `IROptimizer.cs` uses the CFG or its loop data.** `CppCodeGenerator.cs`
  and `MSILBackend.cs` name it only in comments (verified: zero code references).
  `BasicLang/LSP/` has zero references. No IDE project references it. `IRPipelineDemo.cs`
  is dead code — the class is never referenced and `RunDemo` is never invoked.
- `IsReducible`, `PostDominatorTree`, `DominatorTree`, `ComputeDominanceFrontier`,
  `ComputeBlockDepths` have **zero production consumers** (only the dead demo).
- `LICM` preheader today resolves to the loop's own **latch** (`for0.inc`); on the
  `Exit For` shape it resolves to header=`for0.end` (the exit) / preheader=`if0.then`.

## F4. Reach: the aggressive pipeline is the Release default

Loop passes are in `AddAggressivePasses()` only (`:1602-1639`); `AddStandardPasses()`
(`:1590-1600`) has none. `ProjectFile.cs:44` `OptimizationsEnabled = true` by default;
`:125` Release sets it true; `BuildService.cs:629` `OptimizeAggressive = config.Optimize`;
`Compiler.cs:291/458` → `AddAggressivePasses()`. **Every Release project build reaches
these passes.** Default (non-optimize) output measured correct on all shapes.

## F5. ⛔ Turn-on analysis — fixing the substrate changes 3 passes; 2 have NEVER run

**Today (shipping, `--optimize`), only LICM fires.** `LoopUnrolling` and `LoopFusion`
report 0 modifications on every shape measured, including shapes built to satisfy every
gate they name. Measured refusal reasons, replayed gate by gate:
- `LoopUnrolling` refuses at `CanUnroll`'s trip-count gate (`:2749`): `FindInitialValue`
  (`:2796-2824`) needs a predecessor outside the loop; `entry` is inside every bogus set.
- `LoopFusion` refuses at `GetLoopBounds` (`:3281`) returning null.

**Baseline damage (shipping, `--optimize`, four backends out of process):**

| shape | C# | C++ | JS | MSIL |
|---|---|---|---|---|
| single `For` | ✓ | **0 iterations** | ✓ | **0 iterations** |
| nested `For` | ✓ | **0 iter** | **RUNFAIL `t5` undefined** | **0 iter** |
| `For`+`If` | ✓ | **0 iter** | ✓ | **0 iter** |
| `For`+`Try` | ✓ | **0 iter** | **RUNFAIL `t2` undefined** | **0 iter** |
| `For`+`Exit For` | ✓ | **0 iter** | **RUNFAIL `t3` undefined** | **0 iter** |
| counted call-free | 29 ✓ | **1** | 29 ✓ | **1** |
| two sibling loops | **65** ✗ | **1,1** | **65** ✗ | **1,1** |

Two rows beyond the reported defect list: `For`+`Try` and `Exit For` break JavaScript;
and **two sibling loops give a wrong VALUE (65, correct 29) on C# — the reference
oracle — and on JS.** Mechanism: bogus sets `LOOPSHIP#4..#7` span *both* loops, so LICM
moves loop 0's body `a = a + i` into loop **1**'s latch `for1.inc`, where it runs 8 times
with `i` frozen at 8: `1 + 8×8 = 65`.

**PROBE A — substrate fix ALONE (one token at `:283`).** Loop sets become correct on
13/13. End-to-end it is **strictly worse**: 8/8 loop programs break on all four backends
(C#/JS hang; C++ `undeclared identifier`; MSIL `InvalidProgramException`). Emitted C# for
the counted loop:
```csharp
acc = Seed(); i = 0;
while (0 <= 7) { i = 1; }      // condition hoisted+folded; body and increment hoisted out
Show(acc);
```
Compiler itself runs in 0.25s — an emitted-program defect, **not** a compiler hang or OOM.

**Why:** a second, independent LICM defect that the broken sets currently MASK.
`IsValueInvariant` (`:1437-1459`): a local `IRVariable` is not a constant, not a
parameter, not global, so it falls through to
`return !loop.Contains(inst.ParentBlock) || …` (`:1456`). `IRValue : IRInstruction`
(`IRNodes.cs:90`, `:130`), and a bare `IRVariable` **operand** has `ParentBlock == null`
(measured: `IsParam=False IsGlobal=False ParentBlock=<null>` for every such operand).
`!loop.Contains(null)` → **true**, so *every local reads loop-invariant*. Today the only
usable bogus set is `[for0.cond, entry]`, which holds no body, so LICM can only reach the
condition. Correct sets expose the body and it hoists the induction variable itself.

**PROBE B — substrate fix + conservative `IsValueInvariant` (locals not invariant).**
6 of 8 shapes become **correct on all four backends**, including `#114`'s single and
nested `For` and the `Exit For` / `Try` JS failures. The 2 that break are exactly the
2 where the never-run passes now fire:

| shape | pass that fires | result |
|---|---|---|
| counted call-free | `LoopUnrolling` | CS0103 / C++ undeclared / JS ReferenceError / MSIL InvalidProgram — emits **doubly-prefixed undeclared** names `_u0__u0_i`, `_u0__u1_acc` (pass re-runs over its own output; `CloneVariable` `:2995` mints names, and the optimizer has no facility to declare a minted variable) |
| two sibling loops | `LoopFusion` | C++ `undeclared label 'for0_inc'`; MSIL `Unable to find forward reference label 'for0inc'`; JS **REFUSED** ("a loop header whose branch does not target the loop's own .end block"); C# **silently wrong: 29,37** (correct 29,29). `FuseLoops` `:3375-3430` removes loop2's blocks from `function.Blocks` while branches still target them |

**Summary figure: fixing the substrate changes behaviour in 3 registered passes, of
which 2 (`LoopUnrolling`, `LoopFusion`) have never executed on any program. A 4th
consumer, `InductionVariablePass`, is disabled at `:1723` and unaffected unless re-enabled.**

## F6. `For Each` is a second, structurally different loop representation

`For Each` lowers to a structured `IRForEach` node, not to branches. `Build()` wires
`BodyBlock`/`EndBlock` as forward edges (`:90-94`); the CFG has **no cycle**. Measured:
`SHOULD 0 / DOES 2`. A correct natural-loop analysis also reports **0** — so `For Each`
is invisible to every loop pass before and after any fix. Today one of its two bogus
loops gives LICM header=`foreach0.end`, preheader=`foreach0.body`, i.e. hoisting *into*
the loop body. Same for `Try` (`IRTryCatch`, `:95-102`).

## F7. Option space, measured

| | blast radius | turns on | reversible | consumers migrate |
|---|---|---|---|---|
| **A. Repair `:283` in place** | 1 token, 1 file | all 3 passes at once | yes (1 token) | must move **together** — one shared predicate, no per-consumer opt-out |
| **B. Dominator tree + loops from back edges** (dominators already correct; adds loop-nesting forest, header/latch/preheader/exit accessors) | `ControlFlowGraph.cs`; consumers opt in as they adopt accessors | whatever each consumer adopts | yes | **independently** — old `NaturalLoops` can stay until each consumer moves |
| **C. Synthesise preheaders in `IRBuilder`** | `IRBuilder` + every backend (new block per loop) + fixture churn; does **not** fix the inverted predicate, so sets stay wrong | nothing by itself | block shape is user-visible in emitted output | consumers keep searching; C is orthogonal to A/B, not a substitute |
| **D. Gate/disable the loop passes** | 3 `AddPass` lines `:1605`, `:1638`, `:1639`; precedent `:1594`, `:1634`, `:1723` | nothing | yes | n/a |

Measured constraints on any option:
- **A alone does not ship** (PROBE A: 8/8 programs break on 4/4 backends).
- **A + `IsValueInvariant`** fixes `#114` on all four backends for 6/8 shapes but
  simultaneously turns on two passes that are broken on 4/4 and 3/4 backends respectively.
- **D is the only option that removes the measured wrong-value defect on C#/JS (65) and
  the C++/MSIL zero-iteration defect without turning anything on**, because LICM is the
  only pass that fires today.
- Suite baseline: 195/6569/203/6967, 170 names. No suite run was performed for this brief.
