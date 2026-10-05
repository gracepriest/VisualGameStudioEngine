# ADR 0019: C++ closures — ClosureLowering by default, with a W2-gated by-copy fallback

- **Date:** 2026-10-01
- **Status:** Accepted. The one question inside it the architect left to the owner, E16 (D4), was
  decided by the owner on 2026-10-01: **admit** (the architect's default).
- **Decided by:** the architect's ruling for #140 (D1–D5, binding), transcribed from its saved text.
  Nothing under the Decision headings is editorialised. The two implementer interpretations
  ("Implementation: what the ruling left to the implementer") and the measurements are marked as
  not part of the ruling.
- **Brief:** the orchestrator's `architect-brief.md`, `ruling.md` and `implementer-brief-2.md` for
  task #140 (session scratchpad, not kept in the repo), with the Phase-0 census
  (`phase0/matrix-H.md`, `phase0/transitions-P03.txt`).
- **Task:** #140. Amends ADR-0010 D1 (a second consumer, a contract with options and a result,
  patch 01's fix), ADR-0016 D3's C++ arm (W2 is demoted to the fallback's soundness proof) and ADR-0014
  D2's revisit-if (L8 now prints the same on C++, JavaScript and MSIL). Builds on ADR-0018 (names are
  minted through the reservation path, never by spelling).

## Question

C++ lowered every lambda as `[=]`, a COPY taken where the lambda is created, so a write to a captured
variable that the copy never sees was a silent wrong answer. #170 refused the programs where that
happens (`CheckLambdaCaptureWrites`, "W2", ADR-0016 D3 as amended), and said #140 would delete the
rule: C++ would run the same closure conversion MSIL runs (`ClosureLowering`, ADR-0010) and capture by
reference. Five questions, each expensive to reverse:

- D1: which roots go through the lowering — all of them (W), only the ones W2 refuses (S), or every
  root with a by-copy fallback (H, H′)?
- D2: what must `ClosureLowering` change to serve a second backend?
- D3: what does "#140 deletes W2" become, when the lowering still refuses shapes (ADR-0010 D9)?
- D4: what happens to the two programs ADR-0014 D2 records as a divergence (L8, L8b) and to E16
  (#229)?
- D5: what bites in a design with two paths, and which falsifiers must be pinned?

## Decision

### D1: opt-in scope — H′

**Decision:** H′. Every root is lowered by default; a root `ClosureLowering` refuses is emitted by today's
`[=]` path only if W2 holds for it; otherwise it is refused with both reasons. The arity cap becomes a
backend option (C++ unbounded). The rows where the fallback only reproduces a clang failure (Async
D02/D17; generic-typed capture D04/D16) STAY fallback — no derived "clean refusal".

**Because:**
- W breaks rule two (14 run→refused, including E20, the fence). Disqualified, not a trade-off.
- S keeps `[=]` as the DEFAULT, so every future lambda shape lands on the unsound-by-default path first,
  and it leaves 8 fixable clang failures (L13/L13b/L15, AddressOf of a method / `obj.M`, P6/P10/E5).
- H′ over H: 8/9 is a .NET delegate-facade fact, not a lowering fact. Under H a 10-arg lambda that writes
  a capture is refused for a limit C++ does not have. Cost: one option field, 2 more byte-changed
  programs, 0 regressions.
- The Async rows fail on `Task.Result`, not the lambda. A lowering refusal there would name the wrong
  construct AND make a future `Task.Result` fix unable to unlock them (the lowering would still refuse).
  Fallback keeps them one fix away.

**Contract:**
- Path is chosen per ROOT (outermost non-lambda function), in this order:
  1. lowering accepts → lowered;
  2. lowering refuses AND W2 holds → `[=]`, byte-identical to master;
  3. both refuse → `ForeignFeatureException`: W2's text, then the lowering's reason.

  No third representation. (See "Implementation" for the exception type actually raised.)
- Both representations are `std::function<R(Args)>`; a delegate value never reveals which path produced it.
- Fallback eligibility is decided ONLY by W2 — never by the lowering's refusal kind, message or shape.

**Obligations (test writer):**
- Pin the H′ fallback set (10: iterator ×3, `MyBase.M()` ×2, D15, D07b, the N9 pair incl. E20, two-type
  Catch) as (a) running with VB's output and (b) C++ byte-identical to master.
- Pin the both-refused set (D07/R15, D14, M07/R12, E09, K13) as refused with BOTH reasons, W2 first.
- Pin D10/D11 as LOWERED under H′ (not fallback), and add D10's shape with a captured-variable write — must
  run.
- Pin D17 (async, read-only) as fallback + clang failure, named for the `Task.Result` gap: when that gap
  closes, this pin must flip to running with no #140 code touched. Same for D04.
- Falsifier for rule one: a both-refused program must never emit C++ — D07 under cli, `--optimize` and
  project build writes no .cpp.

**Revisit if:** the fallback set grows by a shape that is neither a D9 refusal nor Async/generic — the
lowering regressed and `[=]` silently caught it.

### D2: ClosureLowering contract changes

**Decision:** One options record, one result record; the pass owns per-root refusal and skips a root
atomically; MSIL's output is byte-identical under its defaults. No refusal KIND. The 1-line
double-processing fix is the FIRST commit of #140, alone, MSIL measurement in its message.

**Because:**
- A `lowerRoot` predicate forces the backend to pre-know the lowering's refusals — a second copy of D9.
  The pass is the only correct oracle for "can this root be lowered".
- "MSIL:" in a shared pass is wrong today and visible the day C++ prints it.
- The fix is inseparable: C++ cannot ship with a dead duplicate method (clang checks it; ilasm does not),
  and it is a measured MSIL correctness fix (3 ilasm failures → vbc output; N3/N4). A separate task is a
  dependency with no isolation benefit.

**Contract:**
```
sealed record ClosureLoweringOptions(
    string BackendName,                        // refusal text prefix: "MSIL" / "C++"
    int? MaxActionArity, int? MaxFuncArity,    // null = unbounded; MSIL passes 8 / 9
    UnloweredRootPolicy Policy);               // Throw (MSIL) | Skip (C++)
sealed record ClosureLoweringResult(
    IRModule Module,
    IReadOnlyList<(IRFunction Root, ForeignFeatureException Reason)> SkippedRoots);
static ClosureLoweringResult ClosureLowering.Run(IRModule module, ClosureLoweringOptions options);
```
Invariants:
1. all refusal checks for a root run BEFORE any mutation of that root — a root is lowered entirely or not
   at all (D2's one-environment-per-function makes a partial root unsound);
2. post-condition: no `IsLambda` remains outside `SkippedRoots`; under `Throw`, `SkippedRoots` is empty and
   ADR-0010 D1's post-condition holds verbatim;
3. a skipped root's IR is untouched in the clone;
4. an already-lowered root is never processed twice.

Environment/method names stay the lowering's (MSIL-shaped); the C++ backend mangles them. The spelling is
the implementer's call; the collision rule is not — mangled names are minted through ADR-0018's
reserved-name path, never by spelling.

**Obligations:**
- MSIL IL for the 938-subset is byte-identical with default options, except the 16 fix programs.
- Fix commit flips X22, X25, E01_nested_lambda from ilasm-failure pins to vbc-output pins; pins N3/N4; the
  other 13 pinned same-output. Implementer confirms the ilasm text against #241 before linking/closing it.
- Atomicity falsifier: a root with two lambdas, first lowerable, second hitting a D9 refusal — the clone's
  root has zero environment classes and both lambdas still `IsLambda`.
- Throw-policy falsifier: MSIL on D07 still throws, text unchanged with the prefix now coming from
  `BackendName`.

**Revisit if:** a backend needs a per-LAMBDA decision — re-argue root atomicity, not the records.

### D3: what ADR-0016's "deletes W2" becomes

**Decision:** Amend ADR-0016 D3's C++ arm: W2 is demoted from "the refusal rule for lambdas" to "the
soundness proof for the by-copy fallback". Still ONE rule, ONE function (`CheckLambdaCaptureWrites`), ONE
call site — now reached only for roots in `SkippedRoots`. It is deleted together with the `[=]` path, in
one future task, when `SkippedRoots` is empty for every program C++ accepts.

**Because:**
- It is the only thing that makes `[=]` sound; it cannot go while `[=]` exists.
- Ownership does not move: a C++ capability-checker rule over the shared CFG successor function.
  `ClosureLowering` never learns about W2.
- Each D9 shape the lowering learns to lower shrinks the fallback set; deletion is a consequence, not a
  task.

**Contract:** W2 is never evaluated for a lowered root; W2's verdict never influences whether a root is
lowered.

**Obligations:**
- ADR-0016's 10-program fence stays as written and ALSO asserts the path each root took (fallback vs
  lowered as H′ measured), so a path shift is visible.
- The census fallback set is pinned BY NAME and may only shrink; growth fails. How the backend exposes
  "which path" is the implementer's call, but it must exist (see D5.4).
- When a D9 shape is lifted in the lowering, its fallback pins flip to lowered pins in the same commit.

**Revisit if:** a lowered root ever needs W2 — the two-layer model has leaked.

### D4: L8/L8b (ADR-0014 D2) and E16 (#229)

**Decision:** L8/L8b: admitted. They move from refused to D2's RECORDED output, now identical on
C++/JS/MSIL; C# (#136) remains the outlier, so this narrows D2's revisit-if. E16: OWNER DECISION — rule one
does not fix its own unit. Default while waiting: admit, pinned to the #229 output in a test NAMED for
#229, one test over all four backends.

**Because:**
- D2 is a semantics decision the owner recorded, not a wrong answer; converging to it is what D2 asks.
- #229 is a filed defect. ADR-0016's own revisit-if says "a wrong answer … a NEW CLASS", which reads rule
  one per-class: E16 joins an existing filed class on three backends, so one fix flips four pins. If the
  owner reads rule one per-BACKEND, E16 must be refused by name on C++ until #229 — but a hand-written
  predicate for a shape I cannot see is the N9 over-refusal again. That trade-off is the owner's.
- On master E16 is a clang failure (loud). The default turns loud into filed; the named pin is what keeps
  it loud.

**Obligations:** L8/L8b on C++ pinned at 11|21|31 in the SAME test that pins JS/MSIL (D2's revisit-if
becomes one cross-backend assertion). E16 pinned on C++ at the #229 output, test name carries #229. L6
pinned as a clang failure named for the `List.ForEach` gap, not as a lambda defect.

**Revisit if:** #229 is fixed on one backend and E16's four-backend pin cannot flip together.

**E16 — OWNER DECISION (2026-10-01): ADMIT.** The owner chose the architect's default: C++ runs E16 and
prints 20|20|20|20, the #229 output, as C#, JavaScript and MSIL do (VB prints 1|2|10|20). Rule one is read
per CLASS of wrong answer, not per backend: E16 joins the filed #229 class, and one fix flips all four
backends together. The test is
`PerIterationLoopBodyDimExecutionTests.E16_SiblingLoopsSameName_KnownWrongOnAllFourBackends_PinnedForTask229`,
one `[TestCase]` row per backend. The rejected alternative — C++ refuses E16 by name until #229 is fixed —
is recorded for a future revisit. The implementer's estimate of it (not part of the ruling): the predicate already exists as a
DECISION in `IRBuilder.AssignBodyLocals`, which leaves a loop-body `Dim` out of `BodyLocals` when its name is
declared more than once in the function; recording that omission (one list beside `BodyLocals`, copied by
`ModuleCloner`/`ClosureLowering` and read by the verifier's membership check) and refusing in the C++
lowering when a lowered root's per-iteration candidate is captured AND was omitted for that reason is, by the implementer's
estimate, 10–15 lines to record it plus about 15 to refuse, and it must NOT key on "a lambda's local" (E20, a
fence program, runs). It would be a per-backend divergence from
C#/JavaScript/MSIL's #229 output.

### D5: what bites in the two-path design — falsifiers to pin

**Decision:** Four mandatory pins, two cheap ones.
1. **Root identity.** W2 and the lowering must agree on what a root is. Falsifier: outer lambda read-only,
   nested inner lambda hits a D9 refusal (When guard), and the OUTER lambda writes a capture → must be
   both-refused. If it runs, the inner refusal let the outer write slip onto `[=]`. Mirror: inner writes,
   outer hits the refusal.
2. **Per-iteration env + Exit (#226 rule in `ComputeInlineRegion`).** Lambda declared inside `For Each`,
   `Exit For`, inside a `Try`/`Finally`, statements after the loop — tail executes, Finally runs once.
   Second shape: `Return` from the loop body with the lambda alive.
3. **Captured Catch variable as value.** Lambda captures `ex`, invoked after the Catch exits, reads
   `ex.Message`; and `Throw` of the captured `ex` inside the lambda rethrows the same message (no slicing to
   the base). Two Catch blocks of different types with lambdas in both is the fallback shape — pin it as
   fallback.
4. **Path observability.** The backend exposes which path each root took (test-only is fine). Without it a
   lowering regression that pushes roots onto `[=]` is invisible whenever W2 happens to accept them — right
   output today, the L13 clang-failure class back tomorrow.
5. Verifier runs on the lowered clone in CI (`BASICLANG_VERIFY_IR`); D09 stays the ONLY pre-existing fire.
6. A user class/method spelled like a mangled environment/lambda name must compile (ADR-0018 path, not
   spelling).

**Revisit if:** pin 1 or 4 fails — that is the "soundness depends on the root's path" failure the brief
feared, and S (no fallback as default) is the retreat.

## Implementation: what the ruling left to the implementer (not part of the ruling)

Two interpretations were made where the ruling's words and the code's constraints met. Both are recorded
so a later reader does not mistake them for the architect's text.

1. **The both-refused case is raised as `CppCapabilityException`, C++'s refusal channel**, not as the
   `ForeignFeatureException` D1's contract names. `CppProjectBuilder` (the IDE's C++ build, which calls
   `GenerateSplit`) catches only `CppCapabilityException` and reports it as BL6001; a
   `ForeignFeatureException` would escape it as a crash and the "no .cpp is written" falsifier would have
   no channel to travel. The exception carries exactly D1's text: W2's refusal first, then
   `closure lowering cannot lower '<root>' either (#140): C++: <the lowering's reason>`. The lowering's own
   reasons (`SkippedRoots[i].Reason`) are still `ForeignFeatureException`s, as D2's contract says.
2. **Atomicity comes from restarting on a fresh clone**, not from running every refusal check of a root
   before its first mutation (D2 invariant 1 as worded). The lowering's refusals sit inside the rewrite
   (the checks need the environments the rewrite is building), so `Run` lowers a fresh clone; when a root is
   refused it records the root and starts again from a fresh clone of the INPUT that leaves every recorded
   root alone, until a clone lowers with no refusal. A skipped root's IR in the result is therefore exactly
   the input's (invariant 3), no refusal check ever runs against a partly lowered root (invariant 1), and
   each attempt removes at least one root (a program costs one attempt per refused root, plus one). The
   cost is those repeated clones.

Other mechanism choices, within the ruling:
- **Root identity has one definition**, `ClosureLowering.CreatorsOf` / `ClosureLowering.RootOf` (the one
  function whose IR — operand trees and `When` guards included — names a lambda; a root is the end of that
  chain; a lambda nothing creates, a field or module initializer's, is its own root). The lowering's
  discovery and `CheckLambdaCaptureWrites` both read it. Master's W2 used
  `OptimizationPass.LambdaReferences` over `module.Functions`, a cached scan that misses interface default
  bodies, so there were two definitions that happened to agree; there is one now (D5.1).
- **The test seam** is `CppCodeGenerator.ClosurePaths`, `IReadOnlyList<CppClosureRootPath(Root, Lowered|ByCopy)>`,
  filled by `Generate` and `GenerateSplit`, in function order, listing every root that creates a lambda
  (`Class.Member`, `Class.New`, or a module procedure's own name; a field or module initializer's lambda
  is its own root).
- **Emission.** Environment classes are nested in the class whose member created them (chains of nested
  classes included; a module procedure's in one holder struct emitted after every class); inline member
  bodies of a nested class are a complete-class context, so there is no declaration-order problem to solve.
  `IRDelegateCreate` becomes a `std::function` built from a closure that holds the target's `shared_ptr`
  and forwards to the method; copying the delegate shares the environment. A captured Catch variable is an
  environment field of type `std::exception_ptr` set from `std::current_exception()` in the handler:
  `.Message` rethrows it and takes `what()`, and `Throw` rethrows the same object, so the exception keeps
  its dynamic type (D5.3). `ComputeInlineRegion` takes MSIL's #226 rule — an `Exit` that leaves the region
  ends it — so a per-iteration environment's try/finally does not swallow the loop's exit (D5.2).
  The lowering's names (`<>c__EnvN`, `__lambda_N`) are minted once per module into C++ spellings that avoid
  every name the program owns (D5.6).

## Measured at landing (implementer — not part of the ruling)

- **C++, 460 lambda and AddressOf programs × {CLI, CLI `-O`}, against vbc:** 286 run right (master: 214),
  0 regressions; 64 refused programs now run right; 8 clang failures now run right (L13, L13b, L15, P6,
  P10, AddressOf of an instance method, E9e, E5); 4 stay refused with both reasons (K13, R12, R15, E09); L8
  and L8b print D2's recorded 11|21|31; E16 prints the #229 output, 20|20|20|20; L6 goes from refused to a
  clang failure on `List.ForEach`; E26 (no vbc oracle) goes from a clang failure to printing 3; 2 programs W2
  refused are refused by an existing rule instead (E07_lambda_boxing, NameReservationTests' AllKinds:
  "'Object' has no C++ mapping"). The CLI `-O` column has the same transitions.
- **Byte compare against master, C++, 1,141 programs × {CLI, CLI `-O`, Release `.blproj`}:** the 2,043 cells
  of programs with no lambda are identical; the programs on the `[=]` path are identical in all three modes;
  267 programs master accepts change bytes, all lowered; verifier fires: none beyond D09's.
- **Fallback set (the ten of D1, plus X1 and X3 and the async/generic/module-initializer rows)** byte-identical
  to master in cli, cli-O and project: R2, R14, E20, E12_later_sibling, E12_two_clauses, X1, X3, R3_async,
  R10_generic; probes D01, D13, D06, D15, D07b, D02, D04, D17, D09. D16 (a lambda parameter typed `T`) is
  lowered, differs, and fails clang before and after.
- **MSIL against the previous commit:** 3,423 of 3,423 cells emit identical IL; the 951 refused or failing
  cells have byte-identical build logs (249 of them carry an `MSIL:` refusal). The first commit changed 16
  programs × 3 modes against master, all of them the "creator lambda placed after its creator" shape: 13 run
  identically, 3 (X22, X25, E01_nested_lambda) went from the `ilasm` failure to vbc's output. #241's own
  shapes — a nested lambda inside an instance method (N3), a constructor (N4), a Shared method (N5) and a
  property getter (N6) — fail `ilasm` before and print vbc's output after (403, 12, 203, 8; CLI and `-O`);
  module-level nesting ran before and still does.
- **Tests that pin the old behaviour, moved with the commit (70):** 59 refusal pins, 2 E16 pins, 3 #201
  compile-failure pins, 6 text pins (see the commit message for the list).

## Because (summary)

- A two-path design is only as sound as the proof that gates the second path. W2 is that proof, it already
  exists and is measured, and the by-copy path it gates is byte-identical to the one C++ has always run.
- The lowering is the only correct oracle for "can this root be lowered". Making the backend predict it is a
  second copy of D9.
- Per-root atomicity is what keeps D2's one-environment-per-function sound.

## Rejected

- **W (lower everything; refuse what the lowering refuses)** — 14 run→refused incl. E20; violates rule two
  and ADR-0016's own fence.
- **S (lower only what W2 refuses)** — `[=]` as default keeps 8 fixable clang failures and puts every new
  shape on the unsound path first.
- **H without the arity option** — refuses/falls back on a .NET facade limit C++ lacks; the option is one
  field.
- **A refusal KIND to derive clean refusals for Async/generic** — names the lambda for a gap that is not the
  lambda's; makes a `Task.Result` fix unable to unlock the Async rows.
- **`Run(module, lowerRoot)` predicate / probe** — the backend would need a second copy of D9.
- **A separate task for the 1-line fix** — C++ cannot ship without it; a dependency with no isolation.
- **Deleting W2 in #140** — it is the fallback's only soundness proof; goes with `[=]`, not before.
- **Refusing E16 by name on C++** — not rejected; parked as the owner's alternative in D4.

## Revisit if

- The fallback set grows by a shape that is neither a D9 refusal nor Async/generic (D1).
- A backend needs a per-LAMBDA decision (D2).
- A lowered root ever needs W2 (D3).
- #229 is fixed on one backend and E16's four-backend pin cannot flip together (D4).
- Root-identity (D5.1) or path-observability (D5.4) fails: S is the retreat.

## Follow-ups (not blocking #140, filed separately)

- A delegate local named like a VB builtin function (`second`, `minute`, `hour`, `year`, `month`, `day`,
  `len`, `chr`), called with no arguments, crashes the compiler on C# and C++ ("Index was outside the
  bounds of the array") — pre-existing on master; JavaScript and MSIL emit.
- A lambda that captures nothing still allocates an empty environment (on C++ as on MSIL). Performance only.
- What remains of #201 on C++: the AddressOf shapes on the by-copy FALLBACK path (an instance method; a
  branch that returns an AddressOf result) are CLOSED by #201 — the fallback now binds a class method in the
  lowered path's own forwarding closure and declares the AddressOf temp with the other temps, and those roots
  still take the by-copy path. `List(Of Action)`'s element lowering and the module-initializer lambda still
  fail the C++ compiler on the fallback path, which is otherwise byte-identical to before; they run wherever
  the root is lowered. C#'s `__lambda_0` on a `MyBase.New` lambda argument (E13) is untouched.
