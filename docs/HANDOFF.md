# Handoff snapshot — 2026-09-11

**Why this file exists.** Working state for this repo normally lives in a per-machine
auto-memory directory (`~/.claude/projects/…/memory/`) that is **outside the repo and does not
travel**. This file is the in-repo subset a fresh checkout — a cloud session, another machine,
another person — actually needs. It is a dated snapshot, not a changelog: history is in
`git log`, rationale in `docs/superpowers/{plans,specs}/`, conventions in `CLAUDE.md`.

⚠ **Dated 2026-09-11.** Sections carry their own commit where it matters; anything without one
dates from `6139386`. Re-verify before relying on it.

**P2a-2 is COMPLETE.** Tasks 1-15 are done, Step 4 included — the `IDE/` refresh shipped
2026-09-14 in `fbb3694`. The work merged to master in `77e415b`, together with the blnet C++
facade (all five tasks of `2026-09-13-blnet-cpp-facade.md`).

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
- ⛔ **Adding a file to the blnet artifact set has THREE consumers, and the suffix filter is
  the one that bites.** `CppProjectBuilder.CleanGeneratedDir` matches the suffixes `.g.cpp` and
  `.g.h` plus a list of EXACT names — and **`.g.hpp` does not end in `.g.h`**. A new `.g.hpp`
  artifact that is not added to `NetArtifactFileNames` survives cleaning and stays on the
  include path after a project stops using .NET, where user C++ can still `#include` a removed
  member's header. Measured when `blnet_facade.g.hpp` was added: six drift tests went red, five
  were stale expectations and one was this real bug. Update together:
  `NetProxyEmitterTests.ExpectedArtifacts`, `CppProjectBuilder.NetArtifactFileNames`, and
  `NetBuildPipelineTests`' two merged-set lists. A *header* must NOT go into
  `TranslationUnitFileNames` — that list is translation units only.
- ⛔ **`&` against a FOREIGN `::` call had no type to recognise, so the KNOWN side lost its
  wrap too.** `CppCodeGenerator.StringifyForText` coerces each operand so `+` means
  concatenation rather than pointer arithmetic, but it required BOTH operands to be recognised
  and a foreign call has no BasicLang type. The pair being all-or-nothing then emitted
  `"text " + demo::GetName()` — a bare `const char*` on the left. **Fixed:** one recognised side
  is enough, and the unrecognised side is handed to C++ overload resolution, which is the only
  place a foreign return type is knowable (`const char*`/`std::string` concatenate; an integer
  has no operator and becomes a build break). Note the asymmetry that made this worth fixing:
  the same fall-through is SAFE for Single/Double, which fail to build loudly, and unsafe for a
  foreign integer, which compiles with at most `-Wstring-plus-int` and walks off the literal.
  **A build break is the intended outcome for the numeric case** — do not "fix" it by reaching
  for `std::to_string` on an operand whose type you do not know.
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
| Full suite at **`ee3c086`** (branch `claude/jolly-pasteur-l4mpzs`, **Windows**) | **5893 passed / 4 failed / 2 skipped of 5899 — all 4 baseline, zero new** | 55m42s |
| Full suite at **`f54416b`** (Windows) | **5826 total, 4 failures — all baseline** | ~2h |
| Full suite at `6139386` | 5792 passed / 5 failed / 2 skipped of 5799 | ~2h |
| Fast subset at `6139386` | 4897 passed / 2 failed / 1 skipped | ~2 min |

The 5799 → 5826 move is P2a-2 Task 14's additions; 5826 → 5899 is this branch's six new test
files (the facade, delegate-wire, admissibility, coverage-drift, foreign-concat and toolchain-args
suites). The 5th failure in the `6139386` run was a
**contention timeout**, not a defect — `NothingInAHandleSlot_…_PinnedDivergence` "failed" after
8m49s in a loaded full run and passed alone in 34s.

**Four pre-existing failures are baseline and are not yours:** two `SearchSnippets_*`,
`Cli_Build_CppProject_ProjectReference_Warns…`, and `NonEx_variants…` (which passes alone and
fails only when the Native tier runs alongside it). The fast subset shows the two
`SearchSnippets` ones.

✅ **`ee3c086` is WINDOWS-GATED (2026-09-14).** The four failures are exactly the baseline four
named above — so everything a Linux container structurally cannot exercise ran and passed, which
is most of what matters for this branch: the **22 §12.5 blnet integration rows** needing the
win-x64 ILC shim publish, `EveryProxyTableSlotResolvesInThePublishedShim` among them, and
`AddressOfAsADotNetDelegateArgument_LowersAndRuns`. Every Linux gate in this branch's history was
taken WITHOUT those rows; this run is the one that covers them.
⭐ Those two rows were then re-run ALONE against the same gated binaries — `Passed: 2, Total: 2` in
52s, with a real `cl.exe` compile and a real Native AOT shim publish in the log. A passing test
prints nothing at normal verbosity, so "absent from the failure list" is not by itself evidence it
ran; the two rows the claim rests on were measured, not inferred. The run's 2 skips are unrelated
(`Build_CppLanguageProject_NoToolchain_…`, `ReleasePins_MatchTheRunbookOnceFilled`).

⛔ **The fast subset is not a gate for codegen work** — execution tests are
`[Category("Integration")]`. Four fixes once gated green on it, then the first full run found
17 failures and two real regressions already pushed.

---

## Open work

- ~~**P2a-2 (.NET classes in native projects)**~~ — **DONE and merged (`77e415b`).** Kept here
  only as a pointer: plan `docs/superpowers/plans/2026-08-02-p2a2-dotnet-native-flip.md`, spec
  `docs/superpowers/specs/2026-07-29-p2a-dotnet-access-aot-shim-design.md` (§12.4 drift
  invariants, §12.5 the integration set a Linux run cannot exercise).
- **blnet C++ facade (`blnet_facade.g.hpp`)** — an ergonomic C++ rendering of the proxy slots,
  so hand-written C++ can say `System::Console::WriteLine("hi")` instead of naming a mangled
  slot whose trailing hash moves whenever the signature does. Plan:
  `docs/superpowers/plans/2026-09-13-blnet-cpp-facade.md`. **All five tasks are done and proven** —
  methods (static and instance), constructors, and properties, so `Regex r("^a+$"); r.IsMatch(s)`
  works from C++; name and signature collisions are omitted rather than guessed at, and reported
  as **BL6027 (always a warning — never fails a build)**; coverage is pinned against a REAL
  framework surface at 223 of 234 slots. **Windows-gated at `ee3c086`** — 4 failures, all
  baseline.
  ⚠ **The MSIL backend is a MAINTAINED target as of 2026-09-15** (it was not before; the old
  "MSIL/LLVM are not maintained" policy now covers LLVM only). It has a round-trip harness —
  `VisualGameStudio.Tests/Msil/MsilHarness.cs`: source → `.il` → `ilasm` → a real process →
  stdout. **Never assert on emitted IL text alone here.** The defect that motivated the harness
  was a `Select Case` that assembles, runs, and answers `Case Else` for every input; a text
  assertion would have had to already know `beq` was missing to catch it. **That one is fixed
  (2026-09-15)** — `Select Case` now lowers to an ordered comparison chain (the shape
  `CppCodeGenerator` uses for the same IR), because IL's `switch` is *index*-based and the parser
  routes every case value into `IRSwitch.PatternCases` while the old emitter read only
  `IRSwitch.Cases`. Constant / multi-value / range / comparison / `Nothing` / `Or` patterns and
  `When` guards all run; type, tuple and binding patterns are **refused** rather than dropped,
  because dropping one reproduces the original silent-`Case Else` failure exactly. `ilasm` is located,
  not required — Windows ships one in-box under `%WINDIR%\Microsoft.NET\Framework64`, elsewhere
  restore `runtime.<rid>.Microsoft.NETCore.ILAsm` or set `BASICLANG_ILASM`; a machine with none
  gets `Assert.Ignore`. Known gaps are pinned as `_PinnedDivergence` tests that each name a root
  cause and go RED when fixed — read those before starting MSIL work.
  ⚠ **`Try`/`Catch` is real EH regions as of 2026-09-16**, and the fix was FIVE defects, not one.
  The emitter inlined only the try block's straight-line instructions into `.try { }` while
  `GenerateBasicBlock` emitted those same blocks again as ordinary labelled blocks — so the real
  work ran OUTSIDE the protected region and a `Try` around an `If` printed the right answer while
  protecting nothing. On top of that: the catch variable got no `.locals` slot (`stloc 0` in a
  method with no locals, or a store onto an unrelated variable), `FinallyBlock` was ignored
  entirely, a catch type was spelled `[mscorlib]System.` + the clause name (so a user exception
  named a BCL type that does not exist), and — the one that hid the rest — **`Throw` emitted
  NOTHING**: `ICodeGenerator` declares `Visit(IRThrow)` as an empty virtual and MSIL never
  overrode it, so nothing could ever reach a handler. Now supported: multiple typed catches,
  `Finally` (nested region, because IL forbids catch and finally on one `.try`), `Return` inside a
  region (lowered to a result slot plus `leave` to one exit, which is also what runs the finally),
  nested and sibling `Try`s, rethrow, user-defined exception types, and `ex.Message`/`StackTrace`/
  `Source` through a narrow recorded table — anything outside it is refused, not guessed.
  ⚠ **Arrays allocate as of 2026-09-16.** `Dim a(2) As String` declared the local and stopped —
  `.locals init` zeroes a slot, it does not construct anything, so every access dereferenced null.
  Allocation now happens at both declaration sites (locals in the method prologue, fields in every
  constructor — doing locals only is the trap the C++ backend's own note records). ⛔ **The
  declared number is an element COUNT, not a VB upper bound**: `Dim a(3)` holds 3 elements at
  0..2, matching what C#/C++ read from the same `TypeInfo.ArrayDimensionSizes`; `a(3)` is out of
  range and that is the language's decision, not an off-by-one. **Multi-dimensional arrays are
  REFUSED**, not allocated: a rank-2 declaration collapses to a rank-1 IL type and indexing emits
  `ldelema` with `Indices[0]` alone, so `g(i, j)` would silently read and write `g(i)` — allocating
  it would trade a loud NullReferenceException for a quiet wrong answer.
  ⛔ **Allocating arrays exposed three defects that had been unreachable behind the null**, which
  is the pattern to expect when unblocking any path here: an `IRGetElementPtr` temp was typed as
  the ELEMENT while `ldelema` pushes a managed pointer (`stind` then treated an integer as an
  address — and the reference-typed half of this *looked like it worked*, printing right answers
  from unverifiable IL); `.field public Integer[] Cells` carried the BasicLang type name; and the
  array-literal emitter wrote `stloc t0`, an IR value NAME where IL wants a slot index.
  ⛔ **A variable-less `Catch` still needs its `pop` even though the obvious test cannot see it**:
  `leave` empties the evaluation stack, so a straight-line handler runs correctly with the
  exception left underneath. It only becomes an invalid program when a branch join inside the
  handler has to carry the leftover — which is the shape
  `ACatchWithNoVariable_PopsTheException` pins.
  ⚠ **A `BlnetSlotDesc[]` kind that lies fails SILENTLY** (§8.4, 2026-09-15). The array is what
  `blnet_invoke_callback` reads to decide what to deep-copy when a callback is QUEUED rather than
  run inline: HANDLE addrefs at enqueue, STRING deep-copies, VALUE does neither. Label a handle
  slot VALUE and it compiles, links and passes every INLINE test — then the object can be
  collected before the pump runs. Nothing on that path is a compile error, which is why the
  classification is derived ONCE (`NetDelegateDispatch.TryClassifySlot`) and the managed
  dispatcher, the native adapter and the descriptor array all project from it. Never re-decide it
  locally.
  ⚠ **A coverage set identity needs a FLOOR beside it** — skipping every slot satisfies
  `rendered ∪ skipped == all` perfectly. Measured, not theorised: mutating the classifier to skip
  everything left the identity test green. `NetFacadeCoverageDriftTests` asserts both.
  ⚠ `StringBuilder` and `Guid` **cannot be `<NetProxy>` types at all** — `Emit` throws BL6019 for
  each (a §6.4 by-value-pointer RESULT). Upstream of the facade; a surface containing one cannot
  be emitted. `DateTime` and `Uri` used to be on this list for a ByRef HANDLE parameter; §8.3's
  *ByRef handle ownership* resolution (2026-09-15) specified that shape and they emit now. The header is emitted unconditionally and included by nobody —
  `using namespace BasicLang::netfx;` is the one opt-in line.
  ⚠ **A property's `set_X()` is usually absent**, and that is the SURFACE, not the facade: a
  `<NetProxy>` declared type draws only property READ slots, because a setter descriptor is
  synthesized only where a BasicLang program actually writes the member. Measured on a real build
  over `System.Console` and `Regex`: zero `set_` slots. C++ can read such a property but not write
  it. The facade cannot fix this — it can only render slots that exist.
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

**Nothing is open.** Task 15 Step 4 — the `IDE/` binary refresh — shipped 2026-09-14 in
`fbb3694`: redeployed with `robocopy /E` (never `/MIR`; ten files live only in `IDE/`) and
verified by deployed BYTES rather than timestamps — `BasicLang.dll` 3057152 → 3089408 with a new
hash, `BL6027` found as UTF-16 and `EmitFacade` as UTF-8, `IDE\BasicLang.exe new --list`
returning 11 templates. Searching one encoding would have missed half the surface.

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
