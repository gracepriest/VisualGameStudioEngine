# Handoff snapshot — 2026-09-11

**Why this file exists.** Working state for this repo normally lives in a per-machine
auto-memory directory (`~/.claude/projects/…/memory/`) that is **outside the repo and does not
travel**. This file is the in-repo subset a fresh checkout — a cloud session, another machine,
another person — actually needs. It is a dated snapshot, not a changelog: history is in
`git log`, rationale in `docs/superpowers/{plans,specs}/`, conventions in `CLAUDE.md`.

⚠ **Everything below was true at `6139386` unless a section says otherwise. Re-verify before
relying on it.**

---

## ✅ RESOLVED — the `87a6c5e` gate was run (2026-09-11, Linux cloud session)

**The warning below is DISCHARGED, with one caveat.** A full suite ran on `f54416b`
(= `87a6c5e` + the handoff docs commit): **5454 passed / 173 failed / 203 skipped of 5830**,
both streams captured. The total reconciles as 5799 + `46fd2c5`'s 27 + 4 from the other Task 14
commits, so nothing crashed and nothing was lost. **All four fixtures `46fd2c5` touched are
green, and all 173 failures are environmental to Linux** — nothing indicates the untested
combination is a problem. `46fd2c5`'s 27 tests have since been PROVEN by 12 mutation kills and
reviewed; see the Task 14 section below.

⚠ **The caveat: a Linux run is not the Windows gate.** 82 of the 173 are `BasicLang.exe not
deployed` (no `.exe` suffix off Windows), ~60 are hardcoded `C:\` / PATHEXT / MSVC-vcvars
assertions, 22 are Blnet integration rows needing the ILC/AOT shim publish, 6 are native-engine
`DllNotFound`. Those 22 are exactly §12.5's integration set, **including
`EveryProxyTableSlotResolvesInThePublishedShim`** — the runtime backstop this task's best find
rests on. **Re-run the full suite on Windows before trusting the combination end to end.**

<details><summary>Original warning, kept for the record</summary>

## ⛔ READ FIRST — `87a6c5e` went to master WITHOUT a full-suite gate (2026-09-11)

`origin/master` == `origin/feat/p2a2-t11-delegates` == **`87a6c5e`**, SHA-verified. That commit
merged eight P2a-2 Task 14 commits into master. **The required full-suite run was started and
then stopped ~48 minutes in, at build-green with no test summary, by an explicit decision to
push without it.** So master currently carries two things nothing has verified together:

1. **The combination.** Task 14's commits are test-only (plus one small seam, below); the
   incoming master commits changed `BasicLang/JavaScriptEmitter.cs`, `BasicLang/Program.cs` and
   `BasicLang/ProjectSystem/TemplateEngine.cs`. The two sides touch **no file in common**, and
   each was gated on its own branch — but never together, and never by a full suite.
2. **`46fd2c5`, a WIP commit.** 27 new tests that PASS but have **no mutation kills and no
   review**: none has been shown to fail for the right reason, or to fail at all. It also
   carries the one product change — `BasicLang/CSharpBackend.cs`, where the test seam
   `AmbientNamespacesForTest` (a `static` alias that compared a constant with itself and could
   not fail even if the seeding loop were deleted) becomes an instance view
   `CandidateUsingsForTest => _usings`. Emission was measured unmoved (parity battery 22/0/0),
   which is evidence, not proof.

**FIRST JOB: run the full suite on `87a6c5e`** and compare against the numbers below plus this
branch's additions. If it is red, suspect the untested combination before either side alone.

</details>

---

## Where things stand

Most recent work — a **JavaScript project type in the IDE**. The compiler could already emit a
web site, the build service could build one, and F5 could preview one, but the New Project
wizard had no way to *create* one. Shipped:

| Piece | Where |
|---|---|
| `SolutionTypes.JavaScript` (id `javascript`) and the `web-site` template | `VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs` |
| The `.blproj` writer and the page generator | `VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs` |
| The wizard's "JavaScript (Web)" backend option | `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` |
| CLI `basiclang new web` | `BasicLang/ProjectSystem/TemplateEngine.cs` |

Full detail, including the three defects found reviewing it, is in
`docs/superpowers/plans/2026-08-06-javascript-backend-interop-and-dom.md` under **Plan 2d**.

---

## Traps that cost real time here

These are measured, not cautionary. Each one shipped a green build that did the wrong thing.

- ⛔⛔ **A missing switch arm does not fail — it silently builds C#.** Four separate
  backend-dispatch maps have defaulted to C#. The most recent
  (`ProjectTemplateService.GenerateProjectFileContent`) would have written
  `<TargetBackend>CSharp</TargetBackend>` for a JavaScript project. Its default now throws, and
  `ProjectTemplateBackendMappingTests` pins every id in `SolutionTypes.All`. **When you add a
  backend or solution type, grep for every map keyed on it.**
- ⛔⛔ **The C# backend reconstructs control flow by matching block NAMES, not graph shape.**
  `IsIfThenElse` wants `.then`/`.else` and looks up `{prefix}.end`. A CFG named anything else
  falls to a path that emits both arms and **never the merge** — a program prints its first
  operand and stops. Name new CFGs exactly like `Visit(IfStatementNode)` does. The JS backend
  differs again (it derives the merge via `FindMergeBlock`, so the true target must never *be*
  the merge); C++ is goto-based and tolerates any shape.
- ⛔ **Every shipping route runs the IR optimizer; the unit-test helper does not.** A fixture
  can be green while the CLI and the IDE both miscompile. Validate codegen through the CLI or
  an optimizer-running helper. **stdout is the only valid oracle.**
- ⛔ **`IDE/` is a hand-committed xcopy drop and goes stale.** A stale drop has been mistaken
  for a code bug more than once. Deploy with `robocopy <Shell bin> IDE /E` — **never `/MIR`**,
  since the engine DLL and import lib live only there. **`IDE/lib/js/dom-core.bli` is
  load-bearing**: the deployed compiler auto-includes it for every JavaScript build, and
  without it the typed DOM does not resolve. Verify a refresh against the deployed files
  (`IDE/BasicLang.exe new --list`), never timestamps.
- ⛔ **A "Passed!" summary line does not mean the suite passed.** A crashed test host still
  prints a per-assembly summary; the abort goes to **stderr**. Capture both streams and check
  the total against the expected count, not just `Failed: 0`.
- ⛔ **Build contention looks exactly like a test failure.** A native test failing with
  `C++ compile timed out after 240s` is a load artifact. Measured: one such test "failed" after
  8m49s in a full run and passed alone in 34s. **Re-run in isolation before investigating; only
  an assertion failure is evidence.**
- ⛔ **Windows/PowerShell:** never round-trip repo files through `Get-Content`/`Set-Content`
  (it corrupts the BOM-less UTF-8 files here). Write commit messages to a file and use
  `git commit -F`. PowerShell 5.1 reports a native command's stderr as failure, so **verify a
  push by comparing SHAs, never by exit code**.

---

## Gates and expected numbers

```
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```

| Run | Count at `6139386` | Time |
|---|---|---|
| Full suite | 5792 passed / 5 failed / 2 skipped | ~2h |
| Fast subset | 4897 passed / 2 failed / 1 skipped | ~2 min |

**Four pre-existing failures are baseline and are not yours:** two `SearchSnippets_*`,
`Cli_Build_CppProject_ProjectReference_Warns…`, and `NonEx_variants…` (which passes alone and
fails only when the Native tier runs alongside it). The fast subset shows the two
`SearchSnippets` ones.

⛔ **The fast subset is not a gate for codegen work** — execution tests are
`[Category("Integration")]`. Four fixes once gated green on it, then the first full run found
17 failures and two real regressions already pushed.

---

## Open work

- **P2a-2 (.NET classes in native projects) — Task 14 is most of the way done; see below.**
  Task 15 is the closeout. Plan: `docs/superpowers/plans/2026-08-02-p2a2-dotnet-native-flip.md`
  (Task 14 at :1834, Task 15 at :1862); spec:
  `docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md` (§12.4 at :1256,
  §12.5 at :1290).
- **VS Code extension host** — roughly 24 unimplemented requests, enumerated and enforced by
  `ExtensionHostRequestCoverageTests.KnownUnimplemented` (a second test fails once an entry is
  implemented, so the list must shrink). A missing `sendNotification` handler is a silent
  no-op; a missing `sendRequest` handler rejects inside `activate()` and kills the extension.
  Webviews still render as source text.
- **JavaScript backend** — the `lib.dom.d.ts` → `.bli` generator was never built
  (`dom-core.bli` is hand-curated). Known front-end gaps affecting all backends:
  `Inherits ArgumentException`, assigning an inherited field from a derived class,
  module-level non-constant initializers, C++ `raise_X()` taking no parameters, and
  `For Each … In items.Select(…)` inside a class method failing on C#.
- **The New Project wizard's JavaScript path has not been clicked through by a human.** Tests
  cover the view model and the template service; nothing here can drive the Avalonia window.
- Scope decisions already made — **MSIL and LLVM are out of scope** (do not test, fix, or file
  bugs on them), and **COM interop is ruled out**.

---

## P2a-2 Task 14 — what is done, and exactly what is left

**Done and gated at commit time** (eight commits, `8fae4f6`…`87a6c5e`):

- **Spec §12.5's five integration rows, complete.** A mixed BasicLang + hand-written C++ project
  where both sides call the same .NET library through the same generated proxies; a zero-`.bas`
  `<NetProxy>` project proving the startup TU is compiled, linked and initialised (with a
  shim-deleted negative asserting exit 3 and the load-failure line); the delegate round-trip;
  Console-only inertness at both emit and build level; and cold-then-warm caching hardened three
  ways, including the typed `CacheHit` outcome that was asserted nowhere.
- **§12.4's V1 and V4 invariants**: a golden cross-build mangle pin; slots ≡ exports over REAL
  collected surfaces and as the set identity `Exports = Slots ∪ CoreSeven`; the published shim's
  exports checked at EXECUTION level; the generated shim's scaffolding compared ON DISK; and
  `AbiVersion = 1` pinned directly (spec §13).

**Two findings worth keeping:**

- ⭐ **A shim missing one member export still BUILDS.** The C++ side links against the proxy
  *table*, not the shim's exports, so nothing at build time notices; the program passes the §9.3
  handshake and dies at its first .NET call. Only the new
  `EveryProxyTableSlotResolvesInThePublishedShim` (which `NativeLibrary.TryGetExport`s every slot
  against the deployed DLL) catches it. That is the runtime backstop chip `task_68a7198a` lacked.
- **`AddressOf` as a .NET delegate argument does not work**, though spec §8.4:694 promises it
  alongside lambdas. Measured through the shipping pipeline: `BL6017 … Argument 2 of
  'Aot.Probe.Callbacks.Fold' has no .NET type the analyzer can present for overload resolution
  (its static type is 'Func')`. The identical call with a lambda builds and runs. It is finished
  as a **pinned divergence** asserting that exact refusal, with a *replace, do not delete* note.

**DONE 2026-09-11 (Linux cloud session) — items 1-3 below are closed:**

1. ✅ **Full suite run** — see the resolved notice at the top of this file.
2. ✅ **§12.4's V2 and V3 are PROVEN.** Twelve mutations, each applied, full suite run, reverted;
   kills computed as a set difference against the 173-failure baseline. All 12 distinct new test
   methods went red at least once on their own assertion. **Six are discriminating** (M2
   `MapTypeName`→`SanitizeName`, M3 drop an ambient namespace, M7 `MapTypeName`→`!= Unknown`,
   M10 drop the generic-`IEnumerable` arm, M11 `IEnumerable` ignores arity, M12 `CheckType` drops
   the `::` return). For the other six the assertion still fired for the right reason — so it is
   not vacuous — but the regression is already caught elsewhere, so its value is
   vacuity-protection, not new detection. M5 alone reds 22 pre-existing lowering tests.
   ⚠ The brief's six were not enough: **M2 cannot kill `NoOtherRegistryName_…`**, because a
   `MapTypeName` that never answers `NetRef` makes a "must not be `NetRef`" assertion trivially
   true. M7 is its mirror and exists for that reason.
   Honest gap: 16 of the 17 `BareNameResolvesThroughItsAmbientNamespace` rows are proven by
   mechanism, not individually.
3. ✅ **Reviewed**, one finding fixed: `CheckerRejectedNamesAreNeverClaimed` had two arity-0
   `[TestCase]`s, so its `? :` had an unreachable generic branch and its doc comment claimed
   coverage of "both halves" it did not have. The arity-0 constraint is now an enforced
   assertion. **Spec status and the stale-prose sweep are done** — see Task 15 Steps 2 and 3 in
   the plan, which now carry the measured results.

**Still open — needs a WINDOWS machine, none of it doable in a Linux container:**

- **The full suite on Windows.** The Linux run leaves 22 Blnet integration rows unexercised,
  including `EveryProxyTableSlotResolvesInThePublishedShim`. Also the 20-program parity battery
  and `TemplateBuildSweepTests`.
- **The game template's inertness stdout.** Its codegen and build log are measured and clean
  (zero user-program TU changes); it cannot LINK here because `VisualGameStudioEngine.lib`
  needs the VS 2022 engine build, so BL6009 fires on both sides identically.
- **Task 15 Steps 4 and 5** — the `IDE/` binary refresh (per `aada862`, including the deps.json
  closure check) and the memory/closeout commit.

⚠ **Two corrections the inertness measurement produced, both now in the plan:** there are
**THREE** runtime splices, not two — `485bbe1` adds a `BasicLang::String` alias — and `2752a96`
is no longer a clean baseline, because 291 commits separate it from master and at least one
(`d682f5a`, a non-P2a-2 concat memory-safety fix) changes user-program TUs.

**The failure mode this task kept finding — check for it in any test you write or review.**
Assertions that pass for structural reasons rather than because the property holds: a guard
asserting on text the test itself wrote; a comparison the product feeds both sides of (change one
*comment* in `BlnetShimSources.HandleTable` and the "verbatim" test stays green, because both its
sides move together); a hand-typed list standing in for a derived one; a guard against *empty*
that is not a guard against the *shape* that made the row worth having; a pin on a shape nothing
emits (the golden mangle literal once described an instance method as static); and a doc comment
describing a test that does not exist (`RowBAgreesWithTheCapabilityCheckersEarlyReturns` still
claims to drive the capability checker — the test project never constructs one).
