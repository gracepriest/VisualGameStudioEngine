# Handoff snapshot — 2026-09-11

**Why this file exists.** Working state for this repo normally lives in a per-machine
auto-memory directory (`~/.claude/projects/…/memory/`) that is **outside the repo and does not
travel**. This file is the in-repo subset a fresh checkout — a cloud session, another machine,
another person — actually needs. It is a dated snapshot, not a changelog: history is in
`git log`, rationale in `docs/superpowers/{plans,specs}/`, conventions in `CLAUDE.md`.

⚠ **Dated 2026-09-11.** Sections carry their own commit where it matters; anything without one
dates from `6139386`. Re-verify before relying on it.

**P2a-2 is functionally complete.** Tasks 1-15 are done bar Task 15 Step 4 (the `IDE/` binary
refresh, a Windows deployment chore). Work sits on branch `claude/jolly-pasteur-l4mpzs`, merged
up to date with master.

---

## ✅ READ FIRST — master is FULL-SUITE GREEN at `f54416b`, and Task 14 is PROVEN (2026-09-11)

`origin/master` == **`f54416b`**, which merged eight P2a-2 Task 14 commits (tip `87a6c5e`) into
master. **Full suite measured on Windows: 5826 tests, 4 failures — exactly the standing baseline
below, nothing new.**

Worth recording *because* it was in doubt: that merge combined two sides that had only ever been
gated apart. Task 14's commits are test-only (plus one small seam); the incoming master commits
changed `BasicLang/JavaScriptEmitter.cs`, `BasicLang/Program.cs` and
`BasicLang/ProjectSystem/TemplateEngine.cs`. They touch **no file in common**, and the full run
confirms the combination is clean.

✅ **`46fd2c5`'s 27 tests are no longer unproven** — that was job #1 here and it is done, on
branch `claude/jolly-pasteur-l4mpzs`. **Twelve mutations**, each applied to the product, full
suite run, reverted; kills computed as a set difference against a measured baseline. All 12
distinct new test methods went red at least once, on their own assertion, with their own
diagnostic. **Six are discriminating**; for the other six the assertion still fired for the right
reason (so it is not vacuous) but the regression is already caught elsewhere, making its value
vacuity-protection rather than new detection — recorded rather than glossed. The review found one
real defect, since fixed: `CheckerRejectedNamesAreNeverClaimed` had two arity-0 `[TestCase]`s, so
its ternary had an unreachable branch and its doc comment claimed coverage it did not have.

⚠ **The brief's six mutations were not sufficient.** `NoOtherRegistryName_…` cannot be killed by
the `MapTypeName`→`SanitizeName` mutation: a route that never answers `NetRef` makes a "must not
be `NetRef`" assertion trivially true. Six more were needed, and the test's own failure message
named the right one.

### The Linux picture, for cloud sessions

The same suite on a Linux container reports **5454 passed / 173 failed / 203 skipped of 5830** —
all 173 environmental, none a real defect. 82 are `BasicLang.exe not deployed` (no `.exe` suffix
off Windows), ~60 are hardcoded `C:\` / PATHEXT / MSVC-vcvars assertions, 22 are Blnet
integration rows needing the ILC/AOT shim publish (`Cross-OS native compilation is not
supported` — win-x64 only), 6 are native-engine `DllNotFound`.

⚠ **Two things follow.** Those 22 rows are exactly §12.5's integration set, **including
`EveryProxyTableSlotResolvesInThePublishedShim`** — so a Linux run cannot speak for them, and
anything touching the shim needs a Windows pass. And the TOTALS differ, 5826 vs 5830: four tests
exist on one platform and not the other (a differing total, not a skip). Nobody has chased which
four; if a count ever fails to reconcile, start there.


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

| Run | Count | Time |
|---|---|---|
| Full suite at **`f54416b`** (current master, Windows) | **5826 total, 4 failures — all baseline** | ~2h |
| Full suite at `6139386` | 5792 passed / 5 failed / 2 skipped of 5799 | ~2h |
| Fast subset at `6139386` | 4897 passed / 2 failed / 1 skipped | ~2 min |

The 5799 → 5826 move is P2a-2 Task 14's additions. The 5th failure in the `6139386` run was a
**contention timeout**, not a defect — `NothingInAHandleSlot_…_PinnedDivergence` "failed" after
8m49s in a loaded full run and passed alone in 34s.

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

## P2a-2 Task 14 — what shipped, and how it was proven

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
- ✅ **`AddressOf` as a .NET delegate argument — FIXED 2026-09-11.** It used to draw
  `BL6017 … has no .NET type the analyzer can present for overload resolution (its static type
  is 'Func')` while the identical call with a lambda built and ran, contradicting spec §8.4:694.
  The refusal was never a marshaling limit: the analyzer's argument-presentation loop
  target-typed a `LambdaExpressionNode` and had **no arm for `AddressOf`**, so it fell through to
  the static-type mapping, which cannot map a structural `Func` — real .NET delegate parameters
  are NAMED types. `DelegateTypeOf` had been building the right type all along. The fix mirrors
  the lambda arm (native-only guard included); the pinned row is promoted to the runtime row
  `AddressOfAsADotNetDelegateArgument_LowersAndRuns`, asserting `Fold(10, AddressOf Minus)` = 7.
  ⚠ That row is Integration, so its RUN half still needs a Windows pass; the Linux proof is
  `NetDelegateSlotWireTests.AnAddressOfArgumentCrossesLikeALambda`, which reds with exactly that
  BL6017 when the arm is removed.

**DONE 2026-09-11 (Linux cloud session) — items 1-3 below are closed:**

1. ✅ **Full suite run** — Windows, 5826 tests / 4 baseline failures. See the top of this file.
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

**Also closed since, on the same branch:**

4. ✅ **Chip `task_75064f2e` — the delegate wire.** A `Double` crossing a delegate slot was
   silently truncated (1.5 arrived as 1; the program built clean, exit 0, and printed 2 where
   .NET says 3). `Single` had the identical defect and no test. ⚠ The recon diagnosis was HALF
   the bug: `NetShimGenerator` packed with `unchecked((ulong)a)` too, so ALL FOUR conversion
   sites were value casts and the truncation began on the MANAGED side — making a native-only
   fix strictly worse (`bit_cast<double>(1ULL)` is 4.9e-324). Fixed as ONE SEAM PER SIDE
   (`wire_to`/`wire_from`, `WirePack`/`WireUnpack`) rather than four casts, which is why it
   existed: four sites answering one question four ways.
5. ✅ **Chip: the admissibility⇄wire-form tie** (Task 8 Step 2b, as specified). Before it,
   `NetSurfaceCollector.FirstUnmarshalable` had NO test assertions at all. Both contrapositives
   now exist, with part 2's probe GENERATED from `NetMarshalTable.WireRows` so a new §8.3 row
   forces a probe member. Mutation-proven (rejecting `Double` is discriminating; admitting
   everything is not — three declared-surface rows already cover part of it).
6. ✅ **Chip: `AddressOf` as a .NET delegate argument.** See the finding above — a missing
   target-typing arm, not a marshaling limit. The pinned row is PROMOTED to the runtime row.
7. ✅ **The plan's checkboxes.** 59 unchecked boxes on shipped tasks read as a work list. The
   TASK HEADINGS now carry verified status and a note says the boxes are unmaintained. They were
   deliberately NOT mass-ticked: that asserts verification nobody did, and Task 2's Step 5 is not
   done but CANCELLED.

**Windows verification — reported green 2026-09-11.** The branch's two run-level rows were run
on Windows and passed: `AddressOfAsADotNetDelegateArgument_LowersAndRuns` (must print `7`) and
the flipped `ADoubleDelegateSlot_TruncatesOnTheWire_PinnedDivergence` (must print `3`).

⚠ **Attribution, because this file is supposed to be measurements:** that is the user's report,
not a run captured in this session — the per-row outputs were not recorded here. A Linux
container cannot produce them (both rows die at ILC's `Cross-OS native compilation is not
supported`, which is the SHIM PUBLISH failing — the analyzer and the managed shim compile fine,
itself evidence the `AddressOf` fix works through the real pipeline). If either row ever needs
re-establishing, the failure signatures are: a **2** on the double row means a half-revert to
value casts; a **denormal (~4.9e-324)** means the wire's two halves were split apart, which is
worse than the original defect; a **BL6017 naming a static type of `Func`** on the AddressOf row
means the target-typing arm was lost.

**Still open — the one remaining item, and it needs Windows:**

- **Task 15 Step 4 — the `IDE/` binary refresh.** The prebuilt `IDE/` binaries ship the compiler,
  so they are stale against all of P2a-2. Procedure per `aada862`, including the deps.json
  closure check via `dotnet exec --depsfile`. `robocopy <Shell bin> IDE /E` — **never `/MIR`**,
  which deletes the engine DLL and import lib that live only there. A deployment chore, not
  development.

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
