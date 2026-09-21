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
- ⛔ **`MsilHarness.Run` is a `GenerateFailed` oracle for CODEGEN refusals ONLY** — a
  `ForeignFeatureException` out of `MSILCodeGenerator`. It is **not** one for a parse or
  semantic error. `MsilHarness.CompileToIl` asserts `Assert.That(analyzer.Analyze(ast),
  Is.True)` internally, and NUnit 4 records that assertion failure against the CURRENT test
  even when the calling code catches the exception — so a fixture that expects a front-end
  rejection fails while appearing to assert the opposite. Assert front-end rejections against
  `Parser.Errors` / `SemanticAnalyzer.Errors` directly. (Found by test-writer, 2026-09-21,
  writing the String-property refusal cases.)
- ⚠ **A mutation sweep restores the SOURCE but does not rebuild.** `sweep.sh` ends with
  `mut.py restore`, which rewrites the `.cs` file; the last binary on disk is still the LAST
  MUTANT'S. A `dotnet run --no-build` straight afterwards runs that mutant — measured
  2026-09-21, it produced a phantom `InvalidProgramException` from a clean tree and cost real
  time. **Always `dotnet build` after a sweep, before any `--no-build` run.**

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

### ⭐ 2026-09-21 — defects measured on all four backends during the C# `CS0103` characterization

Measured at `a36262c` via an out-of-process four-backend emit/compile/run loop (60 shapes, one per
compile). **Not fixed. Each is a real shape with a real number, not a guess.**

- ⛔ **NEW, JavaScript, SILENT WRONG ANSWER.** A `For Each` variable whose name collides with a
  **class field** of the same name: JS emits `for (const n of l) { s = s + this.n; }` — the body
  reads the FIELD, not the loop variable. **JS prints 0 where C#, C++ and MSIL all print 7.** C# is
  correct here. Nobody had listed this; it was found by characterizing outward from a C# defect.
- ⛔ **NEW, C#: an iterator's return type is emitted doubled** — `IEnumerable<IEnumerable<int>>` →
  `CS0029`/`CS0266`. Distinct from the temp family, and it will block a clean promotion of the
  `Yield` shape below.
- **C#, a third `GetValueName`-on-a-use site nobody had listed:** `IRYield.Value`
  (`CSharpBackend.cs:3889`) emits `yield return t0;` → `CS0103`. Same mechanism as
  `IRForEach.Collection` (`:4097`) and `IRIndexerStore` (`:3859/:3861/:3862`). Governed by ADR-0001.
- **Broken on ALL FOUR backends (front-end level, do NOT sweep into a backend family):**
  `b.Items(0) = 5` — an indexer store through a field receiver is parsed as a method call. C#
  `CS0103` + `CS1955`; C++ `does not provide a call operator`; JS `TypeError: b.Items is not a
  function`; MSIL `MissingMethodException`.
- **NOT C#-only, contrary to a note we shipped:** a computed `MyBase.New` argument
  (`MyBase.New(New Tag())`, `MyBase.New(x + 1)`) fails on C# **and C++** identically
  (`use of undeclared identifier 't0'`); JS and MSIL refuse it by design. `MsilClassTypeTests.cs:218`
  blamed this on the C# backend alone — corrected in place.
- **C++ cannot compile ANY property with an explicit `Get`/`Set` accessor** (`no member named …`),
  including one with no locals at all. Auto-properties work. This caps the property-accessor
  fixtures at THREE backends, not four.

### Traps this characterization cost us, recorded so nobody pays twice

- ⛔ **Constant folding silently destroys control shapes.** `l(0) = x + 1` folds to `l[0] = 5;` and
  looks like a PASSING control proving the defect is narrow. It proves nothing. Route any operand
  you need preserved through a parameter or a call. The honest twin `l(0) = p + 1` fails.
- ⛔ **A predicate stated from whichever cases are in the fixture will be wrong.** The indexer-store
  claim has now been wrong TWICE — first "any write inside a `For Each`", then "read-modify-write".
  Measured: any ONE of Collection / Index / Value being a temp fails on its own, with no read-back
  anywhere (`l(Zero()) = 5` is `CS0103`). Strip the shape until it stops failing, then report THAT.
- Line numbers in a prior report drifted 10–30 lines. Re-locate by symbol, never trust a cited line.


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
  cause and go RED when fixed — read those before starting MSIL work. ⚠ **As of 2026-09-16 there
  are none left**: the last one (`ASharedMethodOnAUserClass_IsAPhantomCall_PinnedDivergence`) went
  red when Shared members started working. That is not a claim the backend is complete — the gaps
  below are real, and the ones living in the FRONT END cannot be pinned in this fixture at all
  because they fail before the backend runs.
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
  ⚠ **The dotted static surface emits DIRECT IL as of 2026-09-16**, and ⛔ **the choice this
  backend appeared to have does not exist** — an earlier pin here claimed MSIL could route
  `Math.Sqrt` through the .NET proxy like C++ or emit it directly like C#. It cannot do the first.
  The proxy is a NATIVE C ABI bridge: `[UnmanagedCallersOnly]` exports on a Native AOT shim
  reached through a function-pointer table, and managed code cannot call an
  `UnmanagedCallersOnly` method at all. MSIL could only reach it by P/Invoking the native export
  so it could call BACK into the CLR, for members the CLR already offers, and every emitted binary
  would then depend on the shim being built. (`ResolvedNetTarget`, which drives proxy lowering, is
  also null on this path — the resolver is not engaged for a plain compilation.) Don't re-litigate
  it. Members come from `MSILCodeGenerator.NetStaticMembers`, **keyed on the FULL dotted name**
  because matching the member alone routes `Decimal.Round` onto `Math.Round` — a silent wrong
  answer. Overloads match on argument TYPES, exact before widened; only LOSSLESS widenings are
  allowed (`int64`→`float64` is refused: it rounds above 2^53).
  ⛔ **The wrong overload is invisible at run time.** Measured: with the float64 row declared
  first and the exact-match pass removed, `Math.Abs(-7)` binds `Abs(float64)` and still prints
  `7`. Every round-trip assertion stays green while the call returns a Double where an Integer was
  asked for, so that property is pinned in the IL text — the third such case in this fixture,
  beside the Select Case default branch and the variable-less `Catch`'s `pop`.
  ⚠ **Instance methods know about `Me` as of 2026-09-16**, and the pin that covered this named
  the WRONG cause — it said "a CALL-side defect", but the call was always fine (a method touching
  nothing runs), and the stack trace pointed inside the callee. The emitter simply had no notion
  that an instance member is handed its receiver in argument slot 0. ⛔ **The worst consequence
  was silent**: parameters were numbered from 0, so the first one read the OBJECT REFERENCE —
  `Add(20, 22)` returned 872452332 instead of 42, and no test was watching. Also fixed by the same
  notion: bare field reads (pushed nothing), bare field writes (landed in a temporary and were
  dropped — `stfld` wants the object UNDER the value, so the store goes through a scratch slot
  because IL has no swap), `Me.X`, and sibling self-calls (emitted a static `call` on a phantom
  `Program` class). Constructors are instance members too and never emitted a `.locals` directive
  at all. **The class-member paths now share the module path's state** — exception-handling
  locals, the emitted-block set, and the lowered-return exit block — so a `Try` inside a class
  method works; keeping those per-path is what made each of them separately wrong.
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
  ⚠ **Module-level variables exist as of 2026-09-16.** Before this, `MSILBackend.cs` never read
  `IRModule.GlobalVariables` — the identifier did not appear in the file — so a module-level
  `Dim n As Integer = 7` had no storage anywhere: reads emitted `// WARNING: Unknown local 'n'`
  and pushed NOTHING, writes emitted `// WARNING: Cannot store to 'n'` and abandoned the value.
  ⛔ **The abandoned-value half printed right answers.** In `s = "SET" : PrintLine(s)` the dropped
  `ldstr` was consumed by the `PrintLine` that followed, so the program printed `SET` — correct
  output from a stack accident that the next statement destroys. Globals are now `assembly static`
  fields on the module class (⛔ **not `private`** — IL's `private` is "declaring type only", so a
  user-class method reading a module global would get FieldAccessException; `Public` widens to
  `public`), with initializers and sized-array storage in a `.cctor`.
  ⛔ **The initializer is nowhere in the function IR.** `Main`'s instruction list for that program
  is just `t0 = call CStr(@n)`; nothing in any method body ever assigns the 7. Emitting the field
  without a type initializer is not a build error, it is a program that prints 0.
  ⛔ **`stsfld` does not coerce and nothing complains.** Measured: `Dim d As Double = 7` carries an
  int32 constant, ilasm assembles `ldc.i4.7` / `stsfld float64` without a diagnostic and the JIT
  runs it, copying the bit pattern into the low half of the slot — `d` becomes 3.5E-323 and
  `d + 1.5` prints `1.5`. The widening is emitted from the field's declared type, not left to a
  verifier that never objects.
  ⛔ **The `.cctor` is emitted LAST, after every module procedure**, so it inherits their local and
  temp tables unless they are cleared — and a global initializer CAN name another global
  (`Dim b As Integer = K` emits `ldsfld`). Without the reset, a procedure with a local `K` makes
  the type initializer resolve the global `K` to `ldloc.0`, a slot it does not declare, and the
  program dies with TypeInitializationException. `beforefieldinit` is dropped from the module class
  whenever a `.cctor` exists, as the C# compiler does; that property is invisible at run time and
  is pinned in IL text.
  ⚠ **STALE, corrected 2026-09-18.** This used to read "a module-level initializer may only be a
  literal or another module-level `Const`", with anything else — including `= 2 + 3 * 4` —
  "crashing the FRONT END with a NullReferenceException". The module-scope fold closed that; nothing
  crashes any more. Re-measured: `= 2 + 3 * 4` folds and runs **14**; a bare `= K` naming a module
  `Const` runs **9**; `= K + 1` is REFUSED with a diagnostic, because the folder substitutes no
  named constants. So the old line was both too narrow (arithmetic folds now) and too generous (a
  Const inside an expression does not).
  ⚠ **`Shared` members on user classes work as of 2026-09-16**, and the pin that covered this
  (`ASharedMethodOnAUserClass_IsAPhantomCall_PinnedDivergence`, now deleted) named ONE defect where
  there were TWO, entangled so that fixing either alone makes things worse.
  ⛔ **(1) The dotted name was flattened.** `MathUtil.Twice` reached the emitter whole and
  `SanitizeName` strips dots — it lives in `ICodeGenerator` and is shared by every backend, so it
  cannot be changed here — producing `call Combined::MathUtilTwice`: the module's own class, a
  method nothing defines.
  ⛔ **(2) Every class member was ALSO emitted as a static on the module class.**
  `IRModule.Functions` holds them: `IRBuilder` does `member.Accept(this)`, which appends to
  `Functions`, then stores that SAME `IRFunction` as `IRMethod.Implementation`. Nothing to do with
  `Shared` — instance methods too. Two consequences: two classes declaring a same-named method
  collided on the module class and **ilasm refused the whole file** ("Duplicate method
  declaration"), and an unqualified sibling call to a `Shared` method bound to the DUPLICATE and
  printed the right answer for the wrong reason. Fix (1) alone and the duplicates stay; fix (2)
  alone and the sibling call becomes MissingMethodException. `IsClassMember` filters on class
  MEMBERSHIP by reference identity, mirroring `CSharpBackend.IsClassMethod` — the two must not
  disagree about what a standalone function is.
  Now supported: `Type.Method(...)`, unqualified sibling calls (from instance AND `Shared`
  members), a `Shared` method qualified by its own class, `instance.SharedMethod()` (legal
  BasicLang, illegal IL — the receiver is evaluated then `pop`ped, because it may have side
  effects), `Type.SharedField` reads and writes as `ldsfld`/`stsfld`, bare `Shared` field names
  inside the declaring class, sized `Shared` arrays, and inherited `Shared` methods.
  ⛔ **A call must name the DECLARING class and be spelled from the DECLARATION.**
  `Derived.Tag()` where `Tag` is Shared on `Base` needs `Base::Tag()`; and the call site is not a
  usable source for the signature — that one arrives typed `object` where the method returns
  `string`. `GenerateClassMethod` writes `MapType(method.ReturnType)` and `IlTypeSpec(p.Type)`, so
  the call site must too. ilasm does not resolve member references, so a mismatch fails at RUN
  time.
  ⛔ **Receiver shadowing is INVISIBLE in a write-then-read program.** A local named after a class
  (`Dim Counter As New Holder()` beside `Class Counter`) must resolve `Counter.Total` to the
  local's field. Getting it backwards writes AND reads the same wrong location, so
  `Counter.Total = 4` then printing it gives 4 either way. The test reads a value the program did
  not put there (a constructor's 4 versus the class's `Shared = 9`); the first version of it
  proved nothing.
  ⚠ **An inherited Shared FIELD is mistyped by the FRONT END.** `Derived.Tally` (declared on
  `Base`) comes through typed `object`: the read crashes in the boxing chain and
  `Derived.Tally + 34` is rejected outright with "Arithmetic operator '+' requires numeric
  operands" — identically on C#, which survives only because it re-emits the text and lets C#
  re-resolve. MSIL's `ldsfld` names the right class. Naming the declaring class (`Base.Tally`)
  works. Don't chase it in the backend.
  ⚠ **Return coercion is inserted as of 2026-09-16** — `IRBuilder.CoerceToDeclaredReturnType`.
  VB's `/` is ALWAYS floating-point division, so `Return v / 2` from a `Function … As Integer`
  handed back a Double and nothing converted it. ⛔ **The claim that this broke "all five
  backends" was an INFERENCE and it was wrong** — measured, each did something different:
  C# emitted `return (double)(v) / (double)(2);` from an `int` method, which is **CS0266 and does
  not compile** (the BasicLang build still said "successful", because it only writes source —
  nothing invokes csc); MSIL emitted `ret` with a float64 from an int32 method and returned **0**;
  JavaScript returned **3.5** from a function declared `As Integer`, looking correct only when the
  quotient was already whole; C++ was right, and not because the compiler did anything — it
  narrows implicitly on return.
  The fix is one `IRCast` at the return site, the same seam and the same reasoning as
  `WidenDivisionOperand` ("one insertion moves every consumer"), guarded to NUMERIC PRIMITIVES on
  both sides so `Object`, String, class, `Task(Of T)` and generic returns are untouched.
  ⛔ **The JavaScript backend had to learn narrowing casts to make this land.** It deliberately
  threw on them ("Narrowing is not a no-op in JS either — it needs `Math.trunc`"), so inserting an
  `IRCast` broke the whole backend until `TryNumericCast` existed. Both the statement visitor AND
  the inline renderer need it; patching one leaves the other throwing.
  ⚠ **All four backends now TRUNCATE, which is not VB.NET's answer and not self-consistent on C#.**
  `Return 7 / 2 As Integer` gives 3 everywhere. VB narrows with banker's rounding (4), and this
  compiler's own `CInt(3.5)` gives **4 on C#** (`Convert.ToInt32`) but **3 on C++/JS/MSIL** — so C#
  disagrees with itself between an implicit return and an explicit `CInt`. Reconciling that means
  changing every `IRCast` rendering on four backends; it is a decision about the whole narrowing
  surface and was deliberately NOT taken here. `ReturnCoercionTests` pins the current answer so the
  day someone takes it, the test goes red instead of the behaviour drifting.
  ⚠ **Assignment coercion landed too, as of 2026-09-16** — the same
  `CoerceToDeclaredType`, now applied at the declaration site and once in
  `Visit(AssignmentStatementNode)` ahead of all four target arms (identifier, field, array element,
  indexer). ⛔ **It was characterized separately rather than assumed to mirror the return case, and
  it did not mirror it.** Measured for `Dim d As Integer = 7/2`, `e = 7/2`, `a(0) = 7/2` and a
  module-level `G = 7/2`: C# gave FIVE CS0266s, MSIL gave `dim=1074528256 asn=0 arr=0 glob=0` and
  then **SEGFAULTED**, JS gave `3.5` at all four sites, and C++ was right at all four.
  ⛔ **The declared type must come from the TARGET NODE**, not the target variable:
  `GetOrCreateVariable` is handed `value.Type`, and `TryRenameToVariable` then renames the Double
  temp to the target outright, so the local's declared Integer never enters the picture.
  `_semanticAnalyzer.GetNodeType(node.Target)` answers for all four target kinds at once.
  ⛔ **`n /= 4` needed a SECOND fix and the coercion alone did nothing for it.** The compound path
  typed its `IRBinaryOp` from the TARGET, so the result claimed Integer, the coercion saw no
  mismatch — and the optimizer then folded Integer 10 ÷ 4 to the **Double** 2.5. VB's `/=` is
  floating division exactly as `/` is, so its operands are widened the same way
  `WidenDivisionOperand` does for the binary form. **`/=` ONLY**: measured with the widening
  applied to every compound operator, `a = 14;` becomes `a = (int)((double)(a) * (double)(2));` —
  constant folding lost and an exact integer multiply turned into a run-time Double round trip.
  ⚠ **`\=` cannot be tested**: `n \= 2` does not PARSE ("Unexpected token in expression: '\'")
  even though `IRBuilder`'s compound switch has a `\=` → `IntDiv` case. That case is dead until the
  parser learns the operator.
  ⛔ **A numeric LITERAL is re-typed in place, not wrapped in a cast.** Wrapping regressed
  `PropertySet_LowersToTheSynthesizedSetterSlot` (`st.Position = 5` into an Int64 property turned
  the pinned proxy call `…(st, 5)` into a call on a cast temp; it is now `…(st, 5LL)`, which is the
  more faithful emission for an int64 slot). Re-typing is also load-bearing: measured on the
  previous commit, `Dim w As Double = 7` on MSIL stored the int32 bit pattern and printed
  **3.5E-323**. Constant narrowing TRUNCATES, to match the run-time cast — rounding would make
  `Dim a As Integer = 7.9` answer 8 while the same value through a variable answered 7.
  ⛔ **The coercion is restricted to Integer/Long/Single/Double, and that restriction is
  load-bearing.** `IROptimizer`'s constant folders are written against those four CLR types and
  nothing else — its own comment says `CompareLt`/`CompareGt` "blindly report false for type pairs
  outside int/long/float/double". Handing them an `sbyte` is not a missing optimization, it is a
  MISCOMPILE: measured, re-typing `Dim lo As SByte = -3` folded `lo < hi` to `if (false)` and
  silently dropped the branch body, breaking
  `BytePrinting_IsNumeric_NotCharacter_OnEveryPrintSurface`. So a Byte/SByte/Short/unsigned target
  keeps exactly what it did before the coercion existed — nothing. That leaves
  `Dim b As Byte = 7.9` unnarrowed, which is a real gap; closing it means teaching the optimizer's
  folders every numeric CLR type.
  ⚠ **Argument coercion landed as of 2026-09-16**, completing the three sites (return, store,
  argument). ⛔ **The earlier note here said it "needs overload resolution". That was wrong twice
  over**: the analyzer ALREADY resolves the callee and records its parameter list — the same
  `Symbol.Parameters` both argument loops were reading `IsByRef` from — and BasicLang has no
  overloading to resolve at all (a second `Sub Show` is "already defined in this scope").
  Measured before: **eight CS1503s** on C# (does not build), `MissingMethodException: Take(Double)`
  on MSIL (the call site spells its signature from the ARGUMENT's type), `3.5` everywhere on JS,
  and C++ right by narrowing implicitly.
  ⛔ **FIVE call shapes reach THREE different arms** of `Visit(CallExpressionNode)`, plus
  `Visit(NewExpressionNode)`. Patching the obvious two left `Box.Shr(7 / 2)` pushing a float64 at a
  correctly-spelled `Box::Shr(int32)` — the CLR rejects that as an invalid program. A **static
  member** call reaches neither the plain-identifier arm nor the instance arm.
  ⛔ **A constructor has NO resolved symbol** — measured, `GetNodeSymbol` is null on a
  `NewExpressionNode` — so its parameter types come from the IR class's own constructors, selected
  by ARGUMENT COUNT. The same-arity ambiguity branch is UNREACHABLE (the analyzer does not resolve
  constructor overloads: it binds to the LAST declared one and then rejects the argument) and is
  kept anyway, because it makes the coercion do NOTHING there — a fail-safe branch cannot give a
  wrong answer, unlike a speculative one that acts.
  ⛔ **ByRef is skipped, and the MISMATCHED shape is the only one that shows why.** With matching
  types the coercion is a no-op and the guard never fires. With `ByRef n As Double` and an Integer
  argument, removing the guard turns the C++ call site from `Bump(v)` into `Bump(t0)` — and where
  `Bump(v)` does not compile (a pre-existing ByRef type-mismatch gap), `Bump(t0)` **compiles, runs
  and prints 41**: the write-back landing in a temporary nobody reads. A build error traded for a
  silently dropped mutation.
  ⚠ **`ParamArray` does not parse** in either spelling (`ParamArray xs() As Integer` and
  `ParamArray xs As Integer()` are both syntax errors), so a guard clause for it was written and
  then removed — untestable, and redundant anyway since an array-typed parameter is already
  rejected by the Integer/Long/Single/Double restriction.
  ⚠ **STALE, corrected 2026-09-21: MSIL ByRef is FIXED.** This used to read "still failing for
  unrelated reasons, all pre-existing: MSIL fails any ByRef call with InvalidProgramException".
  Two separate defects lived behind that: `MSILCodeGenerator.EmitStoreLocal` had NO `starg` arm
  at all, so writing to ANY parameter — ByRef or not — fell off the end of the
  local/field/property/static-field ladder and left a value on the stack for `ret` to reject
  (`Sub Bump(n As Integer) : n = n + 1`, no ByRef anywhere, threw the same
  `InvalidProgramException`); ByRef's own half needed `&` in the signature, `ldind`/`stind`, and
  an ADDRESS at the call site for whichever argument kinds have one (a local, a caller's ByVal or
  ByRef parameter, an instance/`Shared` field, a module global, an array element — a literal, an
  expression or a property is refused, loudly, not silently passed by value). Both are fixed;
  `MsilByRefTests` and `MsilParameterWriteTests` cover them, and the two rows this note used to
  pin — `ModuleProcedureCallTests.ByRef_ThroughAQualifiedCall_IsMarked` and
  `CountedForVariableTests.CountedFor_OverAParameter_RunsOnEveryBackend_IncludingMsil` — now run
  MSIL with the rest instead of pinning it. ⛔ **Still open, a SEPARATE shared front-end gap, not
  this fix's**: a ByRef parameter on a CONSTRUCTOR loses its marker in
  `IRBuilder.Visit(ConstructorNode)` (`IRBuilder.cs:1885`), which never copies `IsByRef` for a
  ctor parameter unlike every other parameter site in that file — MSIL and C++ both print 41/42
  instead of 42/42 for it, identically, pinned in
  `MsilByRefTests.ConstructorByRefParameter_IsAPinnedSharedFrontEndGap_NotThisFamilys`.
  ⚠ **Omitted `Optional` arguments are filled at the CALL as of 2026-09-16** —
  `IRBuilder.AppendOmittedOptionalArguments`, at the same three arms the argument coercion uses.
  ⛔ **One backend of four was right, and it was right by accident.** C# emits the default into the
  SIGNATURE (`int b = 5`) and lets csc fill it, so nothing in the compiler ever produced the value.
  Measured for `Sub One(a As Integer, Optional b As Integer = 5)` called as `One(1)`: JS printed
  `one:1,undefined` (and a Function returning `a + b` printed **0**, because `CStr(NaN)` is 0),
  C++ did not build ("too few arguments to function"), MSIL could not bind
  (`MissingMethodException: Void Combined.One(Int32)`). The declaration side would have been three
  separate per-backend mechanisms; the call site is one.
  ⛔ **FOUR analyzer sites record `Symbol.DefaultValueExpression`, and ABLATION proved every one
  load-bearing** — which one a call reads depends on where the callee is declared, so patching the
  obvious one leaves the rest silently broken. `Visit(ParameterNode)` covers a callee declared
  BEFORE the caller and every class member; `RegisterSubSignature` / `RegisterFunctionSignature`
  cover one declared AFTER it (a forward reference binds to the PRE-PASS symbol, and the
  declaration's own visit installs a different object — measured by identity, call site #47891719
  vs the rebuilt #958745); `BuildSiblingSignatureParameters` covers a callee in another FILE.
  ⛔ **The default is on the SYMBOL, not looked up from the callee's `IRFunction`.**
  `IRVariable.DefaultValue` carries the same fact at the declaration, but `IRModule.Functions` is
  appended as each function is visited, so a call to one defined further down the file finds
  nothing — a fix built on that lookup works for one declaration order and silently not the other.
  ⚠ **Constructors take an omitted `Optional` too, as of 2026-09-16** —
  `SemanticAnalyzer.ResolveConstructor`, ONE rule for all three construction sites. Constructors are
  keyed by ARITY (`.ctor1`, `.ctor2`) in the type's member table and every site looked up an EXACT
  key, so the analyzer refused the program before the IR builder saw it. Measured before:
  `New Box(4)` → "No constructor for 'Box' takes 1 argument(s)"; `New Box()` against an
  all-Optional constructor → "takes 0 argument(s)"; `MyBase.New(7)` → "No constructor for base
  class 'Base' takes 1 argument(s)". An EXACT arity still wins, so nothing that resolved before
  resolves differently; only when no exact key exists is the unique longer constructor with an
  all-Optional tail accepted, and two candidates answer null and keep the existing diagnostic.
  ⛔ **`UnambiguousConstructorParameters` is GONE** — the analyzer now RECORDS the bound constructor
  (`ConstructorBindings`, keyed by AST node) and the IR builder reads it, so the IR can no longer
  coerce against a different constructor than the analyzer type-checked, and it gets
  `IsOptional`/`DefaultValueExpression` that an `IRVariable` list does not carry.
  ⛔⛔ **A class declared AFTER the code that uses it used to get NO constructor checking at all —
  FIXED 2026-09-17.** The cause was worse than a missing key: `ResolveTypeName` fell through every
  user channel to its **.NET fallback** and returned `new TypeInfo(name, TypeKind.Class)`, a
  SYNTHETIC member-less type that is not the user's class. Measured, `New Box(…)` in that order saw
  `members=0`, so the arity check was SKIPPED rather than failed (`hasAnyConstructor` is false with
  no `.ctor` key — not even an error) and nothing was there to coerce or fill against. It was a
  LIVE MISCOMPILE: `New Box(7 / 2)` emitted `new Box((double)(7) / (double)(2))` — **CS1503, does
  not build** — while the same program with the class first emitted the cast and ran. The
  constructor argument coercion had been half-working since it shipped and nothing noticed, because
  every test and sample in the repo declares classes first.
  ⚠ **Fixed by TWO sweeps in pass 1, and the order of the sweeps is load-bearing.**
  `RegisterClassTypes` gives every class its real `TypeInfo` first; only then does
  `RegisterDeclaration` record `.ctorN` via `RegisterConstructorSignature`, so a class-typed
  constructor parameter resolves to the real class instead of degrading to Object. Collapsing them
  into one walk reintroduces that degradation for any class declared later.
  ⛔ **`DefineType` answering NULL *is* the duplicate-class signal**, so pass 1 pre-registering a
  name would have made every class "already defined". `Visit(ClassNode)` therefore CONSUMES the
  pass-1 record (`_preRegisteredClasses.Remove`): the first declaration reuses the type, a genuine
  second `Class Box` finds nothing to consume and reports at its own line with the message it
  always had. Peeking instead of consuming silently disables duplicate detection.
  ⚠ **MSIL passes `BaseConstructorArgs` as of 2026-09-17** — `EmitBaseConstructorCall`. It used to
  emit a fixed `call instance void Base::.ctor()` whatever was written, because the generator never
  read the list at all, so every base constructor taking arguments died with
  `MissingMethodException: Void Base..ctor()`. MSIL-only: C#, JavaScript and C++ all passed them.
  ⛔ **A COMPUTED base argument is REFUSED, not emitted**, and that is the whole design decision.
  IL requires the base call before the constructor body, so a value the body produces does not
  exist yet: measured, `MyBase.New(v + 1)` hands the generator an `IRBinaryOp` temp, and loading it
  would read an uninitialized local and pass a silent **0** — worse than the exception it replaces.
  ⛔ **That shape is an IR-level gap, not an MSIL one**: the same program does not build on C#
  either, which emits `: base(t0)` naming a temp that is not in scope (**CS0103**). Whoever makes
  `BaseConstructorArgs` self-contained (evaluate into the base call rather than the body) fixes
  both; `MsilBaseConstructorTests.AComputedBaseArgument_IsRefused_NotSilentlyZero` pins both halves.
  ⚠ **A base that cannot be constructed with no arguments is REJECTED as of 2026-09-17** —
  `ResolveImplicitBaseConstructor`. Such a program used to compile and then break on EVERY backend
  (MSIL `MissingMethodException`, C# CS7036, JavaScript `base:undefined`), because none of them can
  invent the arguments — which is what makes it the front end's to catch.
  ⚠ **TWO shapes, ONE condition, and both are checked**: a class declaring no constructor at all
  (VB's **BC30387**, in `Visit(ClassNode)`) and a constructor that never calls `MyBase.New` (VB's
  **BC30148**, in `Visit(ConstructorNode)`). Both get an implicit no-argument base call, so both are
  unbuildable for the same reason; checking one leaves half the defect.
  ⛔ **"Callable with no arguments" is asked through `ResolveConstructor`**, the same helper a `New`
  site uses, so an all-`Optional` base constructor COUNTS — its defaults fill. A check written
  against "is there a `.ctor0` key" rejects that legal program, which is the mutation that proves
  this matters.
  ⛔ **Accepting the all-Optional base forced a second fix**: it was a legal program every backend
  miscompiled, because the implicit base call passed nothing to a constructor declaring a parameter.
  The implicit call now FILLS the base's optional defaults (the analyzer records the bound base
  constructor even with no arguments written), and `base:3` runs on MSIL, C# and JavaScript.
  ⚠ **A class declaring NO constructor whose base is all-`Optional` works as of 2026-09-17** —
  `IRBuilder.SynthesizeImplicitConstructor`. The analyzer rightly accepted such a program, but
  there was no `IRConstructor` to hang the filled defaults on, so each backend invented a bare
  no-argument base call: C# emitted `class Derived : Base` with no constructor and got **CS7036**,
  MSIL threw `MissingMethodException`. `base:3` now runs on MSIL, C#, JavaScript and C++.
  ⚠ **The synthesized constructor is a REAL one** — its own `IRFunction` with an entry block and a
  return — not an `IRConstructor` with a null `Implementation`. Only MSIL has a
  synthesize-a-default path at all (`GenerateDefaultCtorForClass`); C#, JavaScript and C++ lean on
  their target language's implicit constructor, so handing them a shape no DECLARED constructor
  ever produces is how one of them breaks uncovered. Mutating `Implementation` to null kills a test.
  ⚠ **`_currentFunction` is pointed at the synthesized function BEFORE the fill**, so a default
  that is an expression emits into that constructor's body rather than whatever function happened
  to be current.
  ⛔ **Nothing is synthesized when there is nothing to fill** — a parameterless base, or no base —
  and the backends' own default still applies, which is what every existing class relies on.
  ⛔ **A base `Optional` default that is an EXPRESSION (`= 2 + 3`) is still broken**, and it is the
  computed-argument gap above rather than a new one: the filled value is a temp the body computes
  and the base call must precede the body, so MSIL refuses it and C# emits `: base(t0)` (CS0103).
  NOT a regression — that shape was already broken, just with a different message. Closing
  `BaseConstructorArgs`'s self-containment closes both. Pinned.
  ⚠ **The `_currentFunction` clear at the end of `SynthesizeImplicitConstructor` is load-bearing.**
  `Visit(VariableDeclarationNode)` decides global-versus-local on `_currentFunction == null` and
  nothing else, so leaking the synthesized function sends the NEXT module-level `Dim` down the
  local-variable branch: no static field is emitted and the program dies with
  `InvalidProgramException`. Held by
  `MsilBaseConstructorTests.TheSynthesizedConstructor_DoesNotLeakIntoTheNextModuleGlobal`.
  ⚠ **The module-scope initializer crash is FIXED as of 2026-09-17** —
  `IRBuilder.BuildModuleScopeInitializer`, `ModuleScopeInitializerTests`. It crashed the compiler
  with a `NullReferenceException` (`GetNextTempName()` on the null `_currentFunction`), surfacing
  as `Error at line 0: ... Object reference not set to an instance of an object`, for ANY
  initializer needing a temp — `40 + 2`, `"a" & "b"`, `7 / 2`, `1 < 2`, `(1 + 2) * 3`, `Helper()`,
  `New List(Of Integer)()` — identically on all four backends, because it happened in the builder
  before any of them ran. TWO call sites had it, the global `Dim` branch and the global `Const`
  branch; both now route through the one helper.
  ⚠ **It FOLDS rather than refuses where it can.** A scratch (deliberately unregistered)
  `IRFunction` gives lowering somewhere to emit, then the optimizer's own `ConstantFoldingPass`
  reduces it. Folding must happen in the BUILDER: a global's `InitialValue` has to be a constant
  for any backend to emit it, and the optimizer does not run on every path.
  `BinaryOpKind.Concat` was added to that pass so `"a" & "b"` folds — `FoldAdd`'s string branch
  already was concatenation. Mixed operands (`"a" & 5`) still do not fold, so VB's coercion is
  never guessed at.
  ⛔ **What cannot fold is REFUSED with a diagnostic**, not guessed at: `Helper()` and
  `New List(Of Integer)()` need code to run first, i.e. a module initializer no backend has (the
  JS backend already refused a non-constant global outright). One refusal, where foldability is
  decided, so the builder and the backends cannot disagree.
  ⚠ **`Dim G As Double = 7 / 2` FOLDS as of 2026-09-17** — `WideningCastFoldingPass`. `/`
  promotes both operands, so the block is `IRCast, IRCast, IRBinaryOp` and neither operand was a
  constant until the casts reduced. The helper now alternates that pass with `ConstantFoldingPass`
  to a FIXPOINT, because two shapes need opposite orders: `7 / 2` needs the casts folded first,
  `(1 + 2) / 4` needs the addition folded first.
  ⛔ **WIDENING ONLY — Integer→Long, Integer→Double, Single→Double**, the three exact ones.
  Integer→Single is inexact past 2^24, Long→Double past 2^53.
  ⚠ **The CInt divergence is FIXED as of 2026-09-18** — `ConversionRoundingTests`. It was measured
  on `CInt(7.5)`, `CInt(8.5)`, `CInt(7.9)`, `CInt(-7.5)`: **C# printed `8,8,8,-8`** while **MSIL,
  JavaScript and C++ all printed `7,8,7,-7`**. One language, two answers, silently wrong on three
  backends out of four.
  ⚠ **C# was the right one.** It emits `Convert.ToInt32`, which is exactly
  `Math.Round(x, MidpointRounding.ToEven)` — banker's rounding, what VB's `CInt` specifies.
  Verified against `Convert.ToInt32` on ten values BEFORE changing anything, because the fix
  direction depended on it; the midpoints are what separate ToEven from both truncation and
  AwayFromZero (`8.5`→8 not 9, `2.5`→2 not 3, `-8.5`→-8 not -9).
  ⚠ The other three were changed to agree: MSIL emits `Convert::ToInt32(float64)`, C++ wraps the
  cast in `std::nearbyint` (default FE_TONEAREST matches on all ten values — `round()` would NOT,
  it is AwayFromZero), and JavaScript gets an emitted `__blCInt` helper because it has no built-in
  (`Math.round` is half-up toward +Infinity and answers -7 for -7.5).
  ⛔ **An INTEGRAL argument keeps its plain conversion.** On MSIL that is not style: `Convert::ToInt32`
  is overloaded per CLR type and IL names one exact overload, so an int32 through the `float64`
  signature would not verify. On C++ an Integer through a double loses precision above 2^53.
  ⚠ The JS helper is selected by SCANNING the module, not a flag set while lowering — the prelude
  is emitted before any function body, so a flag is still false there. Measured: the first attempt
  emitted every call site and no definition, and Node died with "__blCInt is not defined".
  ⚠ **ASSIGNMENT narrowing rounds too, as of the same change** — the whole narrowing surface now
  agrees. `Dim i As Integer = 7.5` is 8, `7 / 2` into an Integer is **4**, `CInt(19.99)` is 20.
  Four backend sites plus the constant fold: `CSharpBackend.EmitCastText` (→ `Convert.ToXxx`),
  `MSILBackend.Visit(IRCast)` (→ `Math::Round(float64)` before the conv),
  `CppCodeGenerator.Visit(IRCast)` (→ `std::nearbyint`), `JavaScriptBackend.TryNumericCast`
  (→ the same `__blCInt` helper), and `IRBuilder.TryConvertConstant` (`Math.Truncate` →
  `MidpointRounding.ToEven`).
  ⛔ **This moved 30 existing tests**, every one of which encoded truncation. Each was checked
  individually against VB semantics rather than bulk-updated: `7.9`→8, `3.5`→4, `CInt(-3.7)`→-4,
  `CInt(19.99)`→20, and the C# emission `(int)(x)`→`Convert.ToInt32(x)`. Several were RENAMED,
  because their names asserted the old behaviour —
  `TheBackendsAgreeOnTruncation_WhichIsNotYetVbsBankersRounding` →
  `TheBackendsAgreeOnVbsBankersRounding`, `CInt_Truncates` → `CInt_Rounds`,
  `ANumericLiteral_..._AndNarrowsByTruncating` → `...AndNarrowsByRounding`,
  `NarrowingConversion_DisagreesAcrossBackends_Pinned` → `NarrowingConversion_AgreesAcrossBackends`.
  ⚠ Two of those tests had ASKED for this in their own comments — the return-coercion one said it
  should "go red and get revisited rather than drifting" the day someone took the decision, and
  the JS cast note called it "a pre-existing decision about the whole narrowing surface and not
  this function's to make". Both now record that it was taken.
  ⚠ `CInt(...)` / `CDbl(...)` are NOT casts — they lower to an `IRCall`, so neither pass touches
  them and they stay refused at module scope.
  ⚠ **The widening-only restriction is currently UNREACHABLE**, measured: adding a Double→Integer
  arm leaves every test passing, because no narrowing `IRCast` reaches the pass (assignment
  narrowing is folded earlier by `TryConvertConstant` without a cast). Kept as a fail-safe no test
  can hold, for the divergence above.
  ⚠ **The pass is NOT in the default pipeline, and that is a SCOPE decision, not a safety one.**
  The tempting rationale — that a folded Double constant renders as `7` via `Value.ToString()` and
  would turn `(double)7 / x` into integer division — was measured and is WRONG: with the pass in
  the pipeline C# still prints 3.5 for both `7 / 2` and `7 / x`, because the optimizer loops to a
  fixpoint and a surviving operand keeps its own cast.
  ⚠ **That miscompile is FIXED as of 2026-09-18**, and the ROOT CAUSE was not the sub-int type
  table an earlier note blamed: **a module-scope declaration was never coerced to its DECLARED
  type at all.** The local branch of `Visit(VariableDeclarationNode)` has always called
  `CoerceToDeclaredType`; the global branch never did, so a Double literal went straight into a
  narrower global and each backend reinterpreted its bits. Measured on MSIL, `Dim v As T = 7.9` at
  module scope: Byte **154**, SByte **-102**, Short and UShort an **empty string**, Integer
  **-1717986918**, UInteger **2576980378**, Long and ULong **4620580627691444634** (the IEEE-754
  bits of 7.9). The SAME declarations as LOCALS printed 7 throughout — which is what identified
  the missing coercion, since Integer and Long are types `TryConvertConstant` has always handled.
  ⚠ **Two parts.** `CoerceToDeclaredType` in `BuildModuleScopeInitializer` fixes
  Integer/Long/Single/Double. `NarrowModuleScopeConstant` (module-scope only) then handles
  Byte/SByte/Short/UShort/UInteger, which `CoerceToDeclaredType` declines on purpose — admitting
  them there would put an `IRCast` in front of LOCAL declarations that already work.
  ⛔ **The narrowed constant keeps an `int`/`long` CLR value and carries the narrow type in its
  `TypeInfo`.** That split is the safety argument: measured and STILL TRUE, `IROptimizer.CompareLt`
  answers **false** for any CLR pair outside int/long/float/double, so handing the folders a real
  `byte` silently folds `lo < hi` to false. UInteger takes an `int` when the value fits, because a
  `long` makes the JavaScript backend refuse the program (BL7003) — measured, that turned
  `Dim G As UInteger = 7.9` into a build failure there.
  ⛔ **ULong is REFUSED**, not narrowed: its range does not fit the `long` the folders can carry.
  A clean diagnostic beats the 4620580627691444634 it printed before.
  ⚠ **Out of range is REFUSED as of 2026-09-18 — VB's BC30439** — `ConstantRangeTests`,
  `SemanticAnalyzer.CheckConstantFitsNumericTarget`. It used to WRAP at module scope
  (`Byte = 300` → **44**, `Byte = -1` → **255**, `SByte = 200` → **-56**, `Short = 40000` →
  **-25536**, `UShort = 70000` → **4464**), which `NarrowModuleScopeConstant` still does for any
  value that now reaches it.
  ⛔ **The note this replaces was WRONG about locals.** It recorded the wrap as "measured on
  both" scopes; module scope was the only place the backends agreed. Re-measured at LOCAL scope
  for `Dim b As Byte = 300` — four backends, THREE answers, one of them not a program:
  **C#** CS0031, the emitted source DOES NOT BUILD; **JavaScript** **300**, since a JS number has
  no width to overflow; **C++** **44**; **MSIL** **44** (`ldc.i4 300` into a `uint8` slot — RUN
  through ilasm, which this container does have via the
  `runtime.linux-x64.microsoft.netcore.ilasm` package `MsilHarness` looks for). Same split at
  five more sites —
  `b = 300`, `a(0) = 300`, `x.F = 300`, `Return 300`, `Take(300)` — where C# adds CS0221 and
  CS1503. Agreeing on a wrap was never worth having; the check is in the FRONT END, ahead of all
  four backends and both scopes.
  ⚠ **Five call sites, one helper**: the `Dim` declaration (local AND module), `Const`,
  assignment (which covers a variable, an array element and a field), `Return`, and an argument.
  A language that refuses `Dim b As Byte = 300` but accepts `b = 300` has no rule at all.
  ⚠ **It checks AFTER half-to-even rounding**, because that is what the narrowing itself does
  (`IRBuilder.TryConvertConstant`): `Byte = 255.4` is legal, `= 255.6` is not, and `= -0.5` is
  legal because -0.5 rounds to -0. Those last two are the mutation kills — comparing the raw
  value refuses `255.4`, and AwayFromZero refuses `-0.5`, both legal programs.
  ⛔ **Constant EXPRESSIONS fold, non-constants do not.** `Dim b As Byte = 100 + 200` diverged
  exactly as the bare literal did, so `TryFoldConstantDouble` folds literals, `Const` references
  and `+ - * /` over them. `Dim b As Byte = someInteger` is a run-time conversion and is NOT
  reported — VB does not report BC30439 for it either — and nor is `c += 300`, whose value
  depends on `c`.
  ⚠ **A sibling of `TryFoldConstantInt`, not a replacement.** That one sizes array declarations
  and must refuse anything it cannot size with, so it rejects floating values and any integer
  outside `int` — which are exactly the cases this has to keep. The integral-only operators
  (`\ Mod << >>`) are deliberately NOT folded here: a fold that DISAGREES with the IR produces a
  false error, the one outcome worse than the wrap this replaces, and declining costs only a
  diagnostic.
  ⚠ **`Long`/`ULong` bounds are imprecise at the boundary on purpose** — `(double)long.MaxValue`
  rounds up to 2^63 — so a constant of exactly 2^63 is accepted. A missed diagnostic, never a
  false one.
  ⛔ **`Single` is a RANGE check, not a precision one.** `Dim s As Single = 1.0E+40` printed
  **Infinity** on JavaScript and made the C++ backend emit `s = Infinityf;`, which does not
  compile. `= 0.1` loses bits and stays legal.
  ⚠ **Two tests in `ModuleScopeInitializerTests` pinned the old behaviour and were rewritten.**
  `AnOutOfRangeInitializer_WrapsLikeTheLocalPath` asserted the wrap AND agreed with it; it is now
  `AnOutOfRangeInitializer_IsRefusedAtBothScopes` and is no longer an Integration test, since a
  diagnostic compiles nothing. `AFoldedNarrowInitializer_IsNarrowedToo` used
  `Dim H As Byte = 500 - 200`, which is refused outright now, and uses `100.5 + 100.2` (= 200.7,
  narrowing to **201**) instead — a strictly better probe: it also separates narrowing the SUM
  from narrowing the OPERANDS (100 + 100 = 200), and dropping the narrowing is caught on BOTH
  backends (C# CS0266, MSIL **102**) where the old integer shape was caught only by C#, MSIL's
  `stsfld uint8` having truncated 300 to 44 by itself.
  ⛔ **An unrelated MSIL gap surfaced while writing these** — a local named `neg` emits
  `[2] int8 neg` and ilasm rejects it ("syntax error at token 'neg'"). Worked around by renaming
  in the test at the time; **FIXED as of 2026-09-18**, see the IL-quoting entry below. The
  `minS`/`maxS` names in `ConstantRangeTests` are left as they are — renaming them back would buy
  a duplicate of `MsilIdentifierQuotingTests`.

  ⚠ **An IL keyword as a BasicLang name is QUOTED as of 2026-09-18 — VB has no such rule, ILAsm
  does** — `MsilIdentifierQuotingTests`, `MSILCodeGenerator.SanitizeName`. The MSIL backend writes
  IL TEXT and never sees ilasm's verdict, so `Dim neg As Integer = 1` compiled "successfully" and
  then would not assemble.
  ⛔ **SCANNED, not guessed at.** Of 90 IL keyword candidates written as a plain
  `Dim … As Integer`, 8 are not BasicLang identifiers at all and **64 of the remaining 82 made
  ilasm reject the program**. They are not exotic: `value`, `add`, `call`, `method`, `field`,
  `filter`, `handler`, `custom`, `break`, `switch`, `box`, `literal`, `native`, `sealed`. The 14
  that passed — `file`, `hash`, `ldc`, `tail`, `volatile`, `constrained`, `corflags`, `culture`,
  `exeloc`, `locale`, `pinned`, `subsystem`, `unaligned`, `ver` — are the argument against a
  hand-written keyword list: the set is large, context-sensitive and not readable as data, so a
  list drifts and being wrong by one word costs a program that does not assemble.
  ⚠ **EVERY position was affected**, measured with `value`: local, parameter, method name, class
  name, field, module-level global and property.
  ⛔ **Quoting is PURELY LEXICAL, and that is what makes "quote everything" safe** rather than a
  matching hazard — measured: a method DECLARED `'Twice'` and CALLED as `Twice` in the same file
  assembles and runs. A quoted name and a bare one are the SAME identifier, so a missed site still
  resolves and no reference has to be kept in step with its declaration. The emitted
  `[mscorlib]System.Math::'Abs'(int32)` binding mscorlib's unquoted `Abs` is the same property in
  the compiler's own output, and is what the test pins.
  ⚠ **Only USER-CHOSEN names are quoted.** Compiler-generated ones are not, because they provably
  cannot collide and quoting them is output churn no test could justify: branch LABELS (every
  block name is `if{n}.then`, `switch{n}.default`, `for{n}.cond` … so it always carries a digit —
  and `Visit(IRLabel)` is dead, `new IRLabel` is never constructed and the lexer has no `GoTo`),
  `.module Combined.exe` (the module name is a driver constant), and the prefixed names
  `get_X`/`set_X`/`add_X`/`remove_X`/`fld_scratch_X`/`eh_result_N`.
  ⛔ **A COMPOSED name is one identifier** — `get_'Alpha'` is not one, so the quotes go around the
  whole thing or nowhere. That is why `RawName` exists beside the override.
  ⛔ **TWO things broke on the first attempt and the suite caught both**, which is the reason this
  is not a one-line change: (1) `MapTypeName` is reached with names that are ALREADY IL spellings,
  harmless only while its fallback was the identity — once it quoted, `int32` came back `'int32'`
  and `Dim n As Integer` declared `[0] class 'int32' 'n'`, a local typed as a class that does not
  exist; (2) `isMain` compared the PRINTED name, so `'Main'` never equalled `"Main"` and ilasm
  refused every assembly with "No entry point declared".
  ⚠ **`_localIndices` is keyed by the IR's name, never the printed one**, and the two lookups that
  got this wrong fail in opposite ways: a catch variable throws at generation time ("the catch
  variable 'ex' has no local slot"), while an array local is **SILENT** — `EmitArrayLocalAllocations`
  just `continue`s, no `newarr` is emitted, and the program dies at run time with a
  NullReferenceException on first use.
  ⛔ **A property named after a keyword was STILL refused** when that change landed, for a
  pre-existing and unrelated reason — the `.property` block named accessor methods the backend
  never emitted. **FIXED as of 2026-09-18**, see the next entry; the accessor-composition property
  is still pinned on the IL TEXT rather than a run, because that is what it is about.

  ⚠ **Properties WORK on MSIL as of 2026-09-18 — they did not, in ANY shape** —
  `MsilPropertyTests`, `MSILCodeGenerator.GenerateProperty` + the field-access visitors. Measured
  before, compile → ilasm → run: an AUTO property (`Public Property Alpha As Integer`) made ilasm
  refuse the file ("Invalid Set method of property 'Alpha'"); an EXPLICIT one assembled and died
  with `MissingFieldException: Field not found: 'Box.N'`; `ReadOnly` with a computed getter the
  same; an auto property touched from inside its own class was refused; a `Shared` one was
  refused. Every other backend ran the same program (JavaScript 7, C++ 7, C# emits a real
  `public int Alpha { get; set; }`).
  ⛔ **THREE independent defects, each masking the next.** (1) The `.property` block was written
  UNCONDITIONALLY while each accessor METHOD was gated on `prop.Getter != null`; an auto property
  carries neither in the IR, so the block named methods that did not exist — and nothing declared
  storage for the value either. (2) Every property ACCESS lowered to `ldfld`/`stfld` on the
  property's own name, bypassing the accessors; proved by deleting the `.property` block from the
  emitted IL by hand, after which it assembled and died with MissingFieldException. (3) An
  explicit accessor's body never got a `.locals init`, because `.maxstack` was written BEFORE
  `InitializeMethodContext` and the slot tables do not exist until then — so
  `Set(value As Integer)` emitted `stloc.0` into a method with no locals directive and the CLR
  answered **InvalidProgramException**. Fixing (3) is a REORDERING, not an added line.
  ⚠ **An auto property gets `'<Name>k__BackingField'`** plus two synthesized accessors over it —
  the spelling the C# and VB compilers use, so it reads as generated and cannot collide with a
  user field. The angle brackets are not identifier characters, which is what makes it safe and
  also why it must be quoted.
  ⛔ **The `.property` block and the methods are now decided by ONE pair of conditions.** The
  shape that forces this is a property with only a `Get` block and NO `ReadOnly` keyword:
  `IsReadOnly` is false, so asking IT whether to declare `.set` answers yes and names a `set_N`
  that is never emitted. A `ReadOnly` property does NOT hold that — there both conditions agree —
  which is why the mutation survived the first pass and needed its own test.
  ⚠ **A bare property name inside its own class is a CALL, not a load**, so `_currentClassProperties`
  sits beside `_currentClassFields` rather than in it. Without it `Alpha = Alpha + 1` emitted
  `// WARNING: Unknown local 'Alpha'`, pushed nothing, ran the `add` an operand short and stored
  the result into a temporary. The setter also needs the same scratch slot the `stfld` path uses —
  IL has no swap — so `AllocateFieldStoreScratch` reserves one for an instance property too.
  ⛔ **TWO gaps here are PRE-EXISTING and deliberately NOT fixed or asserted**, both verified on
  the parent commit `3011ab0`:
  (a) **Instance field initializers were never emitted** — `Public N As Integer = 5` printed **0**
  with no property anywhere. **FIXED as of 2026-09-18 on MSIL**, see the next entry. The
  computed-getter test still seeds through a method, because that is what it was written against
  and re-pointing it at a field initializer would only duplicate `MsilFieldInitializerTests`.

  ⚠ **Instance field initializers RUN on MSIL as of 2026-09-18** —
  `MsilFieldInitializerTests`, `MSILCodeGenerator.EmitInstanceFieldInitialization` (the helper that
  was `EmitArrayFieldAllocations`). `Public N As Integer = 5` emitted the field and threw the 5
  away, so the program ran and read **0** — nothing failed to assemble and nothing threw.
  ⛔ **Measured before**: implicit constructor **0** (and a `String` field came out null); explicit
  constructor **0**; a constructor that BUILDS on the value — `N = N + 3` over `= 5` — answered
  **3** rather than 8, because it started from the zero; two constructors **0,3** rather than
  **5,8**. `Shared` was the ONE shape that already worked, through
  `GenerateClassStaticConstructor`; this is the same loop on the instance side.
  ⚠ **The hook already existed**: `EmitArrayFieldAllocations` was called from BOTH constructor
  paths (the explicit one and the generated default), after the base call — which is exactly where
  VB runs field initializers, so a base constructor observes its own fields already set. Only the
  initializer half was missing.
  ⚠ **C++ had the SAME GAP and it is FIXED too, as of 2026-09-18** — see the C++ entry below.
  JavaScript (`5,hi`) and C# (`public int N = 5;`) were correct all along.
  ⚠ **A NON-LITERAL initializer was dropped in the IR, for every backend — FIXED as of
  2026-09-18**, see the entry below. It is why every test in the two field-initializer fixtures
  uses a plain literal.
  ⚠ **An auto-property initializer does not PARSE**: `Public Property X As Integer = 5` is
  "Unexpected token in class: '='".
  ⚠ **The initializer-before-array-sizing precedence is UNREACHABLE, not load-bearing** — measured:
  the analyzer refuses an initializer on an array-typed field at all ("Cannot assign value of type
  'Integer' to variable of type 'Integer[]'"), so no field carries both and swapping the two arms
  changes nothing. That mutation survives and is recorded as equivalent rather than papered over.
  Both arms are live for DIFFERENT fields; only their order is arbitrary.
  (b) **Inherited members do not resolve.** `Derived.Tag` where `Tag` is on `Base` types its
  temporary `object` and boxes as `System.Object` — on master too, for a plain FIELD
  (`ldfld object 'Derived'::'Tag'`). The front end does not walk the base chain for a member's
  type. The property variant fails the same way and this change neither fixes nor worsens it.
  ⚠ **Still NOT reported: the sub-int narrowing gap this sits next to.** `Dim b As Byte = 255.4`
  is accepted and then prints **255.4** on JavaScript and **255** on C++, because
  `TryConvertConstant` declines Byte/SByte/Short/UShort on purpose (see above). In range is not
  the same as narrowed, and the tests assert acceptance rather than a value for that shape.
  ⚠ `Dim G As Single = 7 / 2` is a FRONT-END diagnostic ("Cannot assign value of type 'Double' to
  variable of type 'Single'"), not a folding gap.
  ⚠ **The C++ global-initializer gap is FIXED as of 2026-09-17** — `CppCodeGenerator`, globals
  loop. It emitted `{}` for every global that is not a sized array and never consulted
  `InitialValue`, so `Dim G As Integer = 42` became `int32_t G = {};` and the program printed
  **0** — a build with the right answer nowhere in it, no diagnostic and no crash, while C#, MSIL
  and JavaScript all carried the value. Now routed through `ValueText`, the helper the static
  field path already uses.
  ⚠ **A non-constant initializer (`Dim I As Integer = H`) emits the referenced global's NAME**,
  which is valid C++ only because the loop writes globals in DECLARATION ORDER and C++ initializes
  namespace-scope objects in that order within a translation unit. Held by a mutation that
  reverses the loop. ⛔ JavaScript REFUSES that shape outright ("a module-level initializer ...
  that is not a constant"), so the backends do NOT agree on it and JS is the strict one.
  ⛔ **`CStr(Double)` prints `3.500000` on C++** where C# and MSIL print `3.5` — pre-existing and
  nothing to do with globals (measured on a plain LOCAL). Pinned as C++ actually behaves rather
  than normalised away.
  ⚠ **`ValueText` vs `GetValueName` at that site is a WASH**, measured: the base `GetValueName`
  (`ICodeGenerator`) already routes an `IRConstant` to `EmitConstant`, so swapping them passes
  every test. `ValueText` is there for consistency with its sibling sites, not protection — an
  earlier comment claiming it guards a Decimal disagreement was wrong and has been corrected.
  ⚠ **The same wash holds at the INSTANCE field site** (`FieldInitializer`, below) — measured
  there too, and recorded in its comment rather than dressed up as load-bearing.

  ⚠ **Instance field initializers RUN on C++ as of 2026-09-18** — `CppFieldInitializerTests`,
  `CppCodeGenerator.FieldInitializer` (the helper that was `FieldArrayInitializer`). The MSIL half
  of this same defect is the entry above; the two fixes are shaped DIFFERENTLY on purpose.
  ⛔ **Measured before**: every instance field initializer was dropped, at every access level and
  for every type — a class with five initialized public fields printed `0,,0.000000,0.000000,False`
  where JavaScript printed `5,hi,2.5,1.5,true`. A constructor that BUILDS on the value inherited
  the zero (`_n = _n + 3` over `= 5` answered **3**, not 8); a sized array field beside an
  initialized one gave `7,0` — the array worked, the initializer did not.
  ⚠ **The cause was one helper with a narrower job than its callers assumed**: all three field
  loops in `GenerateClass` (one per access level — they are separate copies) asked
  `FieldArrayInitializer`, which only ever produced a SIZED-ARRAY form and never consulted
  `IRField.Initializer`. The STATIC path (`EmitStaticMemberInitializationsCore`) did read it, and
  is where the expression to emit now comes from.
  ⚠ **IN-CLASS member initializers, not a constructor member-initializer list** — the emitted class
  often has no constructor at all (just `~Box() = default;`), and C++ runs in-class initializers
  before any constructor body, in declaration order, which is VB's rule too. On MSIL the same
  values go in the CONSTRUCTOR after the base call, because IL has no such thing.
  ⛔ **A `Shared` field must NOT get one** — an in-class initializer on a non-const static is not
  legal C++ — so all three call sites guard on `IsStatic` and the out-of-class definition carries
  the value.
  ⛔ **THREE shapes cannot be run end to end on this backend, each PRE-EXISTING** and verified
  before the change, which is why those cases are pinned on the emitted TEXT: a `Shared` field
  ACCESS does not compile (`Box.Total` emits `t0 = Box->Total;` — "'Box' does not refer to a
  value"); a `Protected` field is not visible from a derived class ("Undefined identifier"), as the
  analyzer does not inherit Protected members into scope; a `Structure` field initializer does not
  PARSE ("Expected member name but found Assignment").
  ⚠ **`CStr(Double)` → `2.500000` and `CStr(Boolean)` → `True` on C++** are the long-recorded
  divergences above, not this fix's; the tests assert C++'s own spelling rather than normalising
  it away.

  ⚠ **A NON-LITERAL field initializer FOLDS as of 2026-09-18** — `FieldInitializerFoldTests`,
  `IRBuilder.BuildConstantFieldInitializer` + the extracted `TryFoldInitializerToConstant`.
  ⛔ **Measured before**: every constant EXPRESSION was dropped silently and the field read its
  type's zero. `2 + 3`, `2 * 3 + 1`, `(1 + 2) * 3` and `8 \ 2` each emitted a bare
  `public int N;` on C# and printed **0** on JavaScript; `"a" & "b"` gave `public string N;` and
  an empty string; `True And False` and `1 < 2` gave `public bool N;` and False. Only a bare
  literal and unary +/- on one ever survived.
  ⚠ **ONE foldability decision, shared with module scope.** The scratch-function + fixpoint-fold
  core came OUT of `BuildModuleScopeInitializer` into `TryFoldInitializerToConstant`, which both
  call. Measured, a field and a global now agree shape for shape — including agreeing to REFUSE
  `Long = 3000000000 + 1`. They differ only in what they do with a null answer.
  ⛔ **A non-constant initializer is now REFUSED, not dropped** — "the field 'N' has an
  initializer that cannot be computed at compile time … assign it in a constructor instead". This
  is the one BEHAVIOUR CHANGE for programs that used to compile: `= Helper()` and `= CInt(2.5)`
  built before and read **0**. Running initializer code would mean lowering it into every
  constructor on every backend, which none of them does; the refusal is the honest answer until
  that exists. ⚠ **The LOCAL path still accepts both** (`Dim h As Integer = Helper()` computes
  4) — that divergence was already true of module-scope globals and is now shared by fields.
  ⛔ **Deleting the literal fast path FIXED A SECOND, UNRELATED BUG.** It coerced a Decimal
  field's literal to a double, so `Public M As Decimal = 1.5` emitted `public decimal M = 1.5;`
  and the real C#-backend build failed with **CS0664** ("use an 'M' suffix"). The general
  lowering emits `1.5m`. The shortcut was kept at first to make the change additive, then removed
  once measurement showed no test could tell it from the fold and the one shape where they DID
  differ was the shortcut being wrong. `CoerceConstantToType` went with it, and so did
  re-stamping the declared type onto the result — both measured inert by diffing the emitted C#
  for fifteen literal and seven folded shapes.
  ⚠ **A CLASS DECLARED AFTER THE MODULE did not resolve its members' TYPES — FIXED as of
  2026-09-18**, for a top-level class AND for one nested in a Module or Namespace; see the entry
  below. Fixtures order the class first out of habit from when this was broken.
  ⚠ **A TOP-LEVEL class declared AFTER its use resolves its members as of 2026-09-18** —
  `ClassDeclarationOrderTests`, `SemanticAnalyzer.RegisterClassMemberSignatures` + the shared
  `PopulateClassMemberSignatures` (which was `PopulateSiblingClassMembers`).
  ⛔ **Measured before**: the class TYPE resolved (an earlier change gives every class its
  `TypeInfo` in pass 1) but its `Members` stayed empty until pass 2 reached the declaration, so a
  use site above it read every member as Object. FIELD, METHOD and PROPERTY all three: C++ emitted
  `void* t1; t1 = c->N;` and failed with "incompatible integer to pointer conversion" plus "no
  matching function for call to 'to_string'"; MSIL threw `MissingFieldException: Field not found:
  'Box.N'`. A `Private` member read from the class's own method broke the same way, and a
  class-typed member whose type is declared later failed EARLIER with
  `BL6017: .NET type 'System.Object' has no accessible member named 'V'`.
  ⚠ **Constructors were NOT the gap**: their signatures have been pre-registered since an earlier
  change, so `New Box(5)` resolved its arity in either order — what broke was reading `c.N`
  afterwards. Both constructor shapes measured 2 C++ errors before, 0 after.
  ⚠ **ONE sweep, shared with the cross-file path.** Members are registered in pass 1 between the
  class-TYPE sweep and the signature sweep, through the same helper the sibling path uses, so the
  two cannot drift. Pass 2 overwrites every entry, so pass 1 is a forward-reference stand-in.
  ⚠ **A class NESTED IN A MODULE is covered too**, by the same sweep's Module/Namespace recursion —
  `ClassDeclarationOrderTests.AClassNestedInAModule_ResolvesItsMembers_WhicheverOrder` and its
  Namespace sibling, added 2026-09-18.
  ⛔ **CORRECTION, recorded because the first version of this entry was WRONG.** It said the nested
  shape was STILL OPEN — `void* t1`, 2 C++ errors "before AND after" — and that dropping the
  recursion therefore changed nothing. Both halves were false. That measurement was taken against a
  compiler binary still carrying the no-recursion mutation, because the mutation harness restores
  the SOURCE without rebuilding. Re-measured on a clean build at `a9bc7f8`: nested resolves its
  members in either order and runs, and removing the recursion emits `void* t1` with 2 C++ errors
  for the nested shape while every top-level case stays green. What was actually missing was a
  TEST — which is the only reason that mutation survived the suite. **Lesson for the harness: a
  probe run straight after a mutation cycle must rebuild first.**
  ⚠ **Two mutations SURVIVE and are recorded rather than papered over**: letting the sweep write
  constructors (the shape that would distinguish the two parameter builders, an ARRAY constructor
  parameter, does not parse); and exposing private members (access is not enforced on a member read
  at all — reading `c._n` from outside compiles in BOTH orders, a separate pre-existing gap). The
  third, dropping the Module/Namespace recursion, is now KILLED by the nested tests above.
  ⚠ **A `Const` inside a Class PARSES and works as of 2026-09-18** — `ClassConstantTests`,
  `Parser.ParseClassMember` (new Const arm) + `IRBuilder`'s class-member loop (new
  ConstantDeclarationNode arm).
  ⛔ **Measured before**: `Private Const K As Integer = 9` inside a Class was a PARSE ERROR,
  "Unexpected token in class: 'Const'" — while the suggestion that same error throws has always
  listed Const among a Class's valid members. `ParseClassMember` had arms for Property, Event,
  Operator, Function, Sub, Dim, a bare-identifier field and every nested type, and none for Const.
  ⛔ **A PARSER-ONLY fix is WORSE than the error, measured**: with only the parser arm, the constant
  reached `Visit(ConstantDeclarationNode)` with no current function, took its MODULE-SCOPE branch
  and was emitted as a GLOBAL — on C++ `int32_t K = 9;` landed after the class ("use of undeclared
  identifier 'K'"), and two classes each declaring `Const K` emitted two globals of that name
  ("redefinition of 'K'").
  ⚠ **Lowered to a STATIC FIELD** carrying the folded value, which is what a VB class Const is —
  reusing the static-member path every backend already has rather than teaching each a new member
  kind, and folding through the SAME `BuildConstantFieldInitializer` every field uses.
  ⚠ **Kept as a ConstantDeclarationNode, NOT desugared to a Shared field in the parser**, because
  constness is enforced: assigning to a module or local Const is already "Cannot assign to
  constant", and a class Const that became a writable static field would be the one scope where
  that check vanished. Asserted.
  ⛔ **TWO shapes inherited PRE-EXISTING `Shared` defects**, each verified on a plain Shared field
  with the change stashed. **The JavaScript one is FIXED as of 2026-09-18** (see the JS `Shared`
  entry below): it read as `undefined` because the class emitted `static K = 9;` and the method read
  `this.K`, and that was the backend's static lowering exactly as recorded — so a class Const now
  emits `return Box.K;` and runs 9, and `AClassConstant_IsReadableFromAMethod` asserts all FOUR
  backends rather than three. **The C++ one is FIXED as of 2026-09-19** (see the C++ `Shared`-access
  entry below): reading it from outside as `Box.K` emitted `t0 = Box->K;`, "'Box' does not refer to
  a value", and now emits `t0 = Box::K;` and runs 9. `AClassConstant_IsReadableFromOutside` asserts
  it.
  ⚠ **Referencing the named constant from another initializer (`= K + 1`) is still refused** — the
  folder substitutes no named constants. A SHARED limit, not a class one: module scope refuses the
  identical shape.
  ⚠ **`Shared` FIELDS and PROPERTIES lower to `Owner.X` on JavaScript as of 2026-09-18** —
  `JavaScriptSharedMemberTests`, `JavaScriptBackend.StaticMemberOwners` + `MemberReference`.
  ⛔ **Measured before**: the class emitted `static K = 9;` and every method emitted `this.K` — and
  `this.K` is `undefined` for a JS static. A READ answered `undefined`; a WRITE silently created an
  INSTANCE property and never touched the static. **A single-instance probe hides the write half**
  (the object reads its own new property back and looks right), so it takes two: `a.Bump()` then
  `b.Read()` printed `undefined` on JS where C++ printed 7. Shared did not mean shared.
  ⛔ **The worst shape prints a PLAUSIBLE NUMBER, not an error.** `K = K + 1` emitted
  `this.K = ((this.K + 1) | 0)`, which is `undefined + 1` = NaN and `NaN | 0` = **0** — a counter
  that reads 0 forever. Measured 0 where every other backend gives 2.
  ⛔ **There are TWO write sites, and `K = 7` exercises only one.** A compound assignment produces
  an IRBinaryOp that IRBuilder renames after the variable, so it lands in `Bind`'s member arm
  instead of the lvalue path. Found because the write mutation SURVIVED the fixture's first draft.
  ⚠ **Both halves go through ONE helper (`MemberReference`)**, so a read and a write cannot disagree
  about where a member lives, and it names the DECLARING class, not the current one: JS resolves a
  static READ up the prototype chain, but `Derived.K = 7` would create a NEW static on Derived and
  leave Base's untouched.
  ⛔ **The declaring-class walk is UNREACHABLE from source today and its mutation SURVIVES** — a
  recorded survivor, not an oversight. An inherited member is not nameable at all: `Return K` for a
  Base's `Shared K` is "Symbol 'K' is undefined", *identically* to a `Protected` instance field
  ("Symbol 'P' is undefined"), both measured. It is kept because the sibling it must agree with,
  `MemberNames`, walks the same chain — if only that one did, the day inherited members resolve
  `_memberNames` would hold the inherited static while the owners map did not, and the read would
  fall back to `this.K`, silently reintroducing this exact bug for inherited statics.
  ⚠ **A `Shared` METHOD call was a separate gap, untouched there — FIXED 2026-09-19**, see the
  JS Shared-method entry below.
  ⚠ **`Shared` METHOD calls work on JavaScript as of 2026-09-19** — `JavaScriptSharedMethodTests`,
  `JavaScriptBackend`: `_staticMethodOwners` + `MethodReference` + `DeclaringClassOfStaticMethod`,
  at three call sites.
  ⛔ **THIS WAS THREE DEFECTS AND THE LOUD ONE HID THE OTHER TWO.** Measured over nine call shapes
  against MSIL (which has all of it right) and C++:
  (1) a qualified `Box.Read()` was REFUSED — `CallTarget`'s dotted arm knew only `Console.WriteLine`
  and `Console.Write` and threw on everything else. Loud, so safe.
  (2) an unqualified SIBLING call COMPILED and emitted the BARE name — `Helper()` — a
  `ReferenceError`, because a member body is not a top-level function.
  (3) `obj.SharedMethod()` COMPILED and emitted `obj.Read()` — a `TypeError`, because a JS static
  is not on the instance. **(2) and (3) were SILENT**: clean build, crash only at run time.
  ⛔ **(2) IS NOT SHARED-SPECIFIC.** An unqualified call to an INSTANCE sibling was equally broken,
  same path, same symptom — measured. Fixing only the Shared half would have left that hole open
  for every instance method, so both are fixed and both are asserted.
  ⚠ **Cross-checked against MSIL, not C++, for the qualified shapes**: C++ has the SAME gap there
  (`Box.Read()` → "use of undeclared identifier", the file does not compile), so it cannot be the
  oracle. C++ IS asserted on the sibling and instance-receiver shapes, which it gets right.
  ⚠ **`Derived.Tag()` now works HERE and is still broken on MSIL** (`NullReferenceException`), so
  that case asserts JavaScript alone — deliberately, rather than pinning a defect as the contract.
  ⚠ **The receiver is still EVALUATED** for `Make().Read()`: a bare identifier cannot have side
  effects so the common case stays clean, and anything else rides a comma expression
  (`(this.B, Box.Read())`, measured reachable through a field receiver).
  ⛔ **FOUR PRE-EXISTING DEFECTS FOUND WHILE DOING THIS, none fixed here, each measured:**
  - **An explicit `Shared` PROPERTY emits a non-static accessor — FIXED 2026-09-19**, see the JS
    Shared-property entry below. `EmitProperty` did not consult `prop.IsStatic` for the
    getter/setter (it does for an auto-property), so `Public Shared ReadOnly Property P` emitted
    `get P()` and `Box.P` read **undefined**.
  - **The front end ACCEPTS an unqualified INSTANCE call from a `Shared` member** — invalid VB
    (BC30469). MSIL compiles it and dies with `MissingMethodException`. The JS backend deliberately
    does NOT rewrite it to `this.Inst()` (inside a static, `this` is the class, so that would be a
    TypeError wearing the shape of working code); the gap belongs in the front end and is pinned.
  - **A module function whose name collides with a class method is DROPPED from emission.**
    Measured identically before and after this change (zero `function Tag` emitted) and broken on
    MSIL too (`MissingMethodException`). Only the in-class resolution is asserted.
  - **MSIL cannot assemble a module function returning a user class**: it emits `Box 'Make'()`
    where ilasm requires `class Box`, and rejects the file with a syntax error.
  ⚠ **`Shared` PROPERTY accessors are `static` on JavaScript as of 2026-09-19** —
  `JavaScriptSharedPropertyTests`, `JavaScriptBackend.EmitProperty`.
  ⛔ **Measured before**: the auto-property arm had always emitted `static`, but the explicit
  accessors never did, so a `Shared` property got INSTANCE accessors and nothing reached them.
  Reading `Box.P` answered **undefined** (the getter lives on the prototype, not the class), and
  `Box.P = 7` never called the setter — it quietly created a plain own-property on the class.
  ⛔ **A READ-WRITE Shared property LOOKED CORRECT WHILE DOING NOTHING.** The write created `Box.P`
  and the read handed that same value back, so a value-only test passes on a completely broken
  property. Only a setter with an OBSERVABLE EFFECT separates them: value / backing field / setter
  call count measured **`8|0|0`** before and **`8|8|2`** after and on MSIL. The leading 8 is the
  whole trap — it is the accidental own-property, not the property. The fixture counts setter calls
  for exactly this reason.
  ⚠ **MSIL is the oracle and agrees on every shape** (8/9; only the INHERITED one is broken there,
  `NullReferenceException`, so that case asserts JavaScript alone). **C++ is not asserted at all**:
  it does not emit an explicit property as a member — `no member named 'P' in 'Box'` — a
  pre-existing gap, so it cannot serve as an oracle.
  ⛔ **A SEPARATE AND MORE SEVERE DEFECT FOUND HERE — FIXED 2026-09-19**, see the
  strength-reduction identity entry below. It was the OPTIMIZER'S.
  An assignment whose RHS computes something and does not mention the target field is SILENTLY
  DISCARDED: `_v = value * 2` emits `const _v = (value << 1);`, a fresh local, so the write goes
  nowhere and the field keeps its old value. Nothing fails; a plausible number is printed.
  - **NOT a property bug**: measured in an ordinary method on an INSTANCE field as well as a Shared
    one (`K = p * 2` → `const K = (p << 1)`, prints the old value).
  - **The NON-OPTIMIZING path is CORRECT (14), the OPTIMIZED path is not (old value).** Strength
    reduction rewrites `* 2` to `<< 1`, the rewritten value loses the marking that says it is named
    after a variable, and `Bind` then treats it as a temp. This is exactly the CLAUDE.md hazard —
    a green suite built on the non-optimizing helper cannot see it. Both paths are asserted in the
    pin BECAUSE THEY DISAGREE.
  - **Precise trigger**: `K = 7`, `K = p`, `K = 3 * 2` and `K = K + 1` all lower correctly; only a
    computed RHS that survives folding and does not name the target is lost.
  - **Not JavaScript-only**: C++ (14) and MSIL (14) are both correct; JavaScript discards the write
    under the optimizer, and **C# emits an EMPTY METHOD BODY** — the statement vanishes on both
    paths. Two backends right, two wrong in different ways.
  ⚠ **A REWRITTEN VALUE KEEPS ITS IDENTITY as of 2026-09-19** —
  `StrengthReductionIdentityTests`, `IROptimizer.OptimizationPass.InheritIdentity` applied at the
  two value-replacement sites.
  ⛔ **The mechanism**: `StrengthReductionPass` rewrites `x * 2` to `x << 1` by constructing a NEW
  IRValue. It carried the old `Name` and `SourceLine` but NOT `NamedAfterVariable` — the flag that
  tells a backend "this result IS the assignment to K" rather than "a temp sharing K's name". With
  it false, **JavaScript emitted `const K = (p << 1);`** (a fresh local, write thrown away) and
  **C# emitted an EMPTY METHOD BODY**. The field silently kept its old value; nothing failed to
  compile.
  ⛔ **ONLY THE OPTIMIZED PATH WAS WRONG**, which is why the suite never saw it: the non-optimizing
  helper lowers the same source correctly, so every test written against it passed. The pass is in
  `AddStandardPasses`, so every shipping route hit the broken path. Textbook CLAUDE.md hazard — the
  fixture asserts the optimized pipeline throughout.
  ⛔ **TWO BACKENDS RIGHT, TWO SILENTLY WRONG.** C++ and MSIL never consult the flag and always
  emitted the store, so this is ONE omission in the IR rather than two backend bugs — the fix
  belongs in the pass, not in either backend.
  ⚠ **Trigger, measured and narrow**: multiplication by a POWER OF TWO. `p * 3` (`Math.imul`),
  `p * p`, `p + 1`, `p - 1`, `p \ 2`, `p Mod 4` and `-p` were all correct before and after; the Div
  and Mod strength-reduction arms were removed long ago as unsound, and Peephole's rewrites build an
  `IRAssignment` with an explicit target, which never depended on the flag. Only CLASS MEMBERS were
  affected — a module global and a local resolve through their own arms first.
  ⛔ **A SECOND SITE with the identical omission** is fixed too: `AlgebraicSimplificationPass`
  (`2 * x -> x + x`), which is AGGRESSIVE-only. Strength reduction does not fire for `2 * p` (its
  arm matches a constant on the RIGHT), so that shape reached the algebraic pass and broke through a
  different rewrite — measured at 1 under `--optimize`. Fixing only the standard site would have
  left it broken by the same missing line.
  ⚠ **The NAME is deliberately not copied** by the helper: every call site already passes it to the
  constructor. Measured — REMOVING the assignment left all 18 tests green while CORRUPTING it failed
  11, so the tests are name-sensitive without the line being needed. It came out rather than staying
  as an assignment that acts and changes nothing.
  ⛔ **TWO PRE-EXISTING `--optimize` DEFECTS FOUND WHILE DOING THIS:**
  - **`FunctionInliningPass` emits undeclared garbage — DISABLED 2026-09-19**, see the entry below.
    For `Dim x = 2 * p : Return x + x` called from `Main`, the aggressive pipeline emitted
    `_inline_t1_0 = 12;` and `_inline_t1_1 = ((_inline_t1_x + _inline_t1_x) | 0);` and
    `t1 = ((x + x) | 0);` into `Main` — all undeclared — **and still called `F(6)` afterwards**.
    `ReferenceError` at run time.
  - **`AlgebraicSimplificationPass` never calls `ReplaceUses` — FIXED 2026-09-19**, see the entry
    below. Its own base-class contract says a pass that swaps an instruction MUST do so. Not
    triggered by the shapes measured here (the value's consumer is a field read).
  ⚠ **`AlgebraicSimplificationPass` calls `ReplaceUses`, and its three UNSOUND arms are GONE, as of
  2026-09-19** — `AlgebraicSimplificationTests`.
  ⛔ **Measured before**: `Return (a + b) - b` under `--optimize` was a `ReferenceError`.
  `const t0 = ((a + b) | 0); t1 = a; return ((((a + b) | 0) - b) | 0);` — an undeclared `t1`, and a
  consumer that RE-MATERIALISED THE WHOLE ORIGINAL EXPRESSION because it still held the discarded
  node.
  ⛔ **ADDING `ReplaceUses` ALONE DOES NOT FIX THE CRASH — measured, not assumed.** With the arm
  restored and `ReplaceUses` working, the consumer IS correctly re-pointed (`return t1;` rather than
  the re-materialised expression), but the emission is still `t1 = a;` with `t1` UNDECLARED: those
  arms introduce a brand-new `IRVariable` target that nothing adds to the function's
  `LocalVariables`. The same defect that sank `FunctionInliningPass`. So the crash is fixed by
  DELETING the arms, and `ReplaceUses` is the separate contract fix.
  ⛔ **The three arms were UNSOUND, and the missing `ReplaceUses` was the only reason nobody saw a
  wrong answer** — the same story recorded for the Div and Mod arms removed from
  `StrengthReductionPass`. Correct answer first: `(a+b)-b` with a=1e-19, b=1e18 is **0**, the arm
  gives `a` (catastrophic cancellation); `(a*b)/b` with b=0 is **NaN**, the arm gives `a` — its own
  comment claimed "when b != 0" and **the code never checked it**; `(a*b)/b` with a=0.1, b=3 is
  **0.10000000000000002**, the arm gives 0.1.
  ⚠ **`2 * x -> x + x` is KEPT** — sound on both fronts (`x + x` is exactly `2 * x` in IEEE 754, and
  wraps identically on integer overflow), and it is the only arm that ever worked, because its
  replacement is a VALUE carrying the same name so the orphaned consumer resolved by NAME
  COINCIDENCE. The base-class doc warns that carrying the name is not enough; this pass was the
  demonstration.
  ⚠ **With the arms gone, `ReplaceUses` is OBSERVABLY INERT in emitted text** — measured, every
  end-to-end shape passes with the call removed. It is kept because the contract requires it and
  because the surviving arm's escape is a coincidence, and it is made TESTABLE by an IR-level test
  asserting the consumer holds the REPLACEMENT INSTANCE (reference identity). That test is what
  kills the drop-the-call mutation; without it the call would have been an untestable survivor.
  ⚠ **`FunctionInliningPass` is DISABLED as of 2026-09-19** — `FunctionInliningDisabledTests`,
  commented out of `AddAggressivePasses` with the measurements beside it.
  ⛔ **It never produced correct output for any function it actually inlined**, and it miscompiled
  SILENTLY — clean build, `ReferenceError` at run time. On
  `Function F(p As Integer) As Integer : Return p * 2` called as `F(6)`:
  `_inline_t1_0 = 12;` (undeclared, and nothing reads it), `t1 = (p << 1);` (undeclared, and the
  CALLEE'S PARAMETER `p` leaked in), then `const t0 = String(F(6));` — **the original call still
  happens**. SIX of seven call shapes failed at run time; the seventh passed only because
  `IsInlineable` REFUSES it for block count, so there was no shape where inlining succeeded. All
  seven are correct without the pass.
  ⛔ **FIVE separate defects, which is why this is a rewrite and not a patch**: (1) inlined locals
  are never added to the caller's `LocalVariables`, so each is emitted undeclared; (2) a definition
  is renamed by `tempCounter` while its USES are renamed by `prefix + name`, two schemes that can
  never agree; (3) `RemapValue` rewrites only an `IRVariable` and returns any nested operand tree
  untouched, leaking the callee's variables and parameters; (4) `InlineCallsInBlock` never calls
  `ReplaceUses`, so consumers still reference the removed `IRCall` and the callee is called anyway;
  (5) `depth` is passed 0 and never incremented, so `_maxInlineDepth` is dead.
  ⚠ **The PASS CLASS IS KEPT, not deleted.** `CloneAndRemap` is still the only clone path an
  `IRCall` can reach, and four `NetIrCarriageTests` guard the .NET resolution carriage through it.
  They now add the pass EXPLICITLY. Verified load-bearing: removing those four lines makes all four
  fail their own "did not inline Helper … this test proves nothing" guards, so the protection is
  intact rather than vacuous. **Deleting the class would silently delete that coverage.**
  ⚠ **Precedent**: `ConstantPropagationPass` is already commented out of `AddStandardPasses` in the
  same file ("incorrectly propagates across control flow merges"). Inlining buys nothing here
  anyway — clang, the CLR JIT and V8 all inline far better downstream.
  ⚠ **The re-enable mutation kills 7 of 9 tests**; the two survivors are the branchy callee (never
  inlined) and the pin that runs the pass directly either way. If someone repairs the pass,
  `RunDirectly_ThePassStillMiscompiles…` goes RED — that is the signal to re-enable it and delete
  that test, not to weaken it.

  ⚠ **QUALIFIED `Shared` access works on C++ as of 2026-09-19** — `CppSharedAccessTests`,
  `CppCodeGenerator`: `StaticMemberQualifier` + `StaticCallTarget` over
  `DeclaringClassOfStaticMember` / `DeclaringClassOfStaticMethod`, at three call sites
  (`Visit(IRFieldAccess)`, `Visit(IRFieldStore)`, `Visit(IRCall)`).
  ⛔ **EVERY qualified form treated the class name as an OBJECT; every unqualified and in-class
  form already worked.** Measured before: `Box.K` → `Box->K` ("'Box' does not refer to a value");
  `Box.K = 5` → `Box->K = 5` ("cannot use arrow operator on a type"); `Box.Read()` → `Read()`, the
  qualifier DROPPED ("use of undeclared identifier 'Read'"). The class DECLARATION was always
  right (`static int32_t K;`), so only the USE site was ever wrong.
  ⛔ **The CALL had a DIFFERENT cause, and it is the sharp one.** `ResolveFlattenedFunctionName`
  exists to align a cross-module call (`Helpers.Print`) with the flattened free function the
  backend emits. Class member bodies ALSO live in `_module.Functions` under their bare names, so
  `Box.Read` found a free function `Read`, concluded it was a flattened module procedure, and threw
  the qualifier away. The helper's premise — a qualifier naming a MODULE — does not hold for a
  class, so the fix goes at the CALL SITE and leaves that helper alone.
  ⛔ **THE SEGMENTS MUST BE SANITIZED SEPARATELY.** `ICodeGenerator.SanitizeName` strips every
  non-alphanumeric character, so handing it `"Box::Read"` yields **`BoxRead`** — a name that exists
  nowhere, and a SILENT mis-emission rather than a compile error. The first draft returned the
  qualified string from `ResolveFlattenedFunctionName` and would have hit exactly that; the
  JavaScript backend hit the same trap with dotted names. Caught by reading `SanitizeName` before
  shipping, and pinned by the `sanitize-whole` mutation (kills 5).
  ⚠ **MSIL is asserted alongside throughout** — the property is "C++ now agrees with the backend
  that has this right", not "C++ prints 9".
  ⛔ **A SHADOW TEST CAN PASS WHILE THE GUARD IS GONE, and this one did.** `Dim Box As Integer = 3`
  shadowing the class name is handled by `_declaredIdentifiers`; the first draft wrote through the
  shadow and read it straight back (`Box::K = 3; t0 = Box::K;`), which agrees with itself whichever
  location it picked. It took a read from an UNSHADOWED scope (`Function PeekBoxK() As Integer :
  Return Box.K`, asserting `3,9`) to kill the mutation. **This is the identical weakness recorded
  for the JS Shared-property write** — found there, then reproduced here.
  ⛔ **INHERITED access is STILL BROKEN, for FRONT-END reasons, in TWO guises, both pinned.** The
  lowering is correct in both — the base walk resolves to the DECLARING class.
  - A **read**: the IR types an inherited `Shared` read as `Object`, so the temp is declared
    `void*` and C++ rejects the assignment ("incompatible integer to pointer conversion"). Measured
    contrast: a DIRECT read declares `int32_t t0`, an inherited one `void* t0`, identical access
    expression.
  - A **Sub call**: the front end does not carry the inherited signature, so it builds an
    EXPRESSION call and the backend binds the result — `t0 = Base::Bump();`, "void value not
    ignored as it ought to be". The direct `Box.Bump()` emits a bare `Box::Bump();` statement and
    runs. Same root cause, second face; found only because the Sub shape was probed at all.
  ⚠ **The base walk is NOT speculative, and is proven by EXECUTION rather than by text.** An
  inherited **WRITE** is the one inherited shape that runs today (it has no result temp to mistype):
  `Derived.K = 7` then reading back through `Base.K` prints 7. Dropping the walk (`no-base-walk-
  field`) kills that test AND the read pin.
  ⛔ **`Derived::K` versus `Base::K` is a FORM choice, not a behaviour one — stated rather than
  dressed up.** Emitting the WRITTEN class was measured to compile and give the SAME answer, because
  C++ resolves a qualified static through the base. The `written-class` mutation is therefore killed
  by a FORM PIN only, and the test says so. The declaring-class form is chosen because the walk must
  run anyway to decide whether to qualify AT ALL (that part IS behavioural) and because it is what
  the other backends emit.
  ⚠ **RECORDED SURVIVOR — `no-isstatic`** (drop the `IsStatic` filter on the field lookup). With
  the shadow guard running first, it only changes WHICH compile error an INVALID program produces:
  `Box.N` for an instance field — which the front end wrongly accepts — gives "'Box' does not refer
  to a value" with the filter and "invalid use of non-static data member 'N'" without. Verified it
  cannot reach a VALID program either: modules are not in `_module.Classes`, so a qualified module
  variable is unaffected (measured identical, below). Kept with the rationale rather than deleted.
  ⛔ **TWO measurement traps hit while proving this, both worth knowing.** (1) `written-class`
  looked like a survivor because the grep matched the out-of-line static DEFINITION
  (`int32_t Base::K = 4;`), which contains `Base::K` no matter what the ACCESS site emits — the pin
  had the same hole and now matches the access STATEMENT. (2) The same mutant was first compared
  against the METHOD fixture while it patches only the FIELD arm. **Both were "no difference"
  readings from a probe that could not have shown one.**
  ⛔ **A SEPARATE C++-ONLY GAP FOUND HERE, NOT FIXED — a qualified MODULE variable.**
  `Helpers.Value` emits `Helpers.Value` (a dot, not `::`) — "'Helpers' was not declared in this
  scope". The UNQUALIFIED `Value` runs (11), and C# and JavaScript both emit the qualified form
  fine. The same SHAPE of defect this entry fixes for classes, one scope over; left out because it
  is not a `Shared` member. Next obvious candidate.
  ⛔ **THE PARAGRAPH ABOVE IS WRONG IN TWO PLACES — see the next entry.** It is not C++-only, and
  "emit the qualified form fine" was checked by emission, not by running: JavaScript and MSIL
  both failed at run time, and the cause is the front end. Kept verbatim as the record of what a
  compile-only probe reports.
  ⚠ **QUALIFIED and CROSS-MODULE `Module` VARIABLE ACCESS resolves on every backend as of
  2026-09-19** — `ModuleMemberAccessTests` (24 cases, all four backends run in process),
  `SemanticAnalyzer._moduleMembers` + `TryResolveModuleMember` + `TryResolveUnqualifiedModuleMember`,
  `Symbol.OwningModule`, `IRBuilder.GlobalReference` + `CollectSharedModuleGlobalNames`,
  `Compiler.CollectExportedSymbols` (module scopes), `CSharpBackend.QualifyCrossModuleGlobal`,
  `MSILBackend.CollectModuleGlobals` (refusal).
  ⛔ **THIS ENTRY CORRECTS THE ONE ABOVE IT.** The C++ Shared-access entry closed with "a qualified
  MODULE variable ... C# and JavaScript both emit the qualified form fine" and called it a
  C++-only gap. That was measured by EMISSION, not by RUNNING, and it was wrong on both counts:
  JavaScript died with `ReferenceError: Helpers is not defined`, MSIL with
  `MissingFieldException: Field not found: 'System.Object.Value'`, and C# alone ran — by
  re-emitting the text and letting csc resolve it. "Emitted OK" is not an oracle. Recorded here
  rather than edited away.
  ⛔ **THE CAUSE WAS THE FRONT END, not any backend.** A `Module`'s `Dim`/`Const` live in the
  Module's OWN scope, which a sibling Module's lexical chain never reaches, and pass 1 registered
  only procedure signatures. So `Helpers.Value` fell through every channel to the permissive "any
  PascalCase identifier could be a .NET type" fallback (`IsNetType`) and was typed **Object**; the
  IR builder then lowered it to an `IRFieldAccess` on a phantom variable named `Helpers`.
  Three symptoms, one cause: `Helpers.Value + 1` refused as "requires numeric operands",
  `Return Helpers.Value` refused as "Cannot return type 'Object'", and each backend's own failure
  on the untyped read.
  ⛔ **THE UNQUALIFIED CROSS-MODULE FORM WAS WORKING BY TWO COINCIDENCES.** A bare `Value` from
  another module took the same fallback — typed as a phantom class named `Value`, NO error — and
  the IR builder minted a fresh LOCAL of that name (`GetOrCreateVariable`, which finds a global
  only if its declaration was already visited). It printed 11 on C++ and JavaScript only because
  the emitted bare global shared the name; `Value + 1` was refused; and C# failed with CS0103
  whenever the using module came FIRST. Measured, all of it.
  ⛔ **TWO MODULES WITH THE SAME VARIABLE NAME WERE A SILENT WRONG ANSWER ON MSIL.** `A.GetA()`
  printed B's 2: `_moduleGlobals` was keyed by bare name and kept the last one. C++ said
  "redefinition of 'int32_t Value'", JavaScript refused; only MSIL was quiet. The prior
  collision fix (`ModuleGlobalCollisionTests`) had qualified only the dictionary KEY — enough for
  C#, which groups by `ModuleName`, and for nobody else, because the other three spell a global
  by its bare `Name`.
  ⚠ **The fix, in four layers, one mechanism each:**
  - **Analyzer**: pass-1 sweep 3 registers every Module's `Dim`/`Const` (typed from the
    declaration; an inferred one is Object until pass 2 swaps in the real symbol). A qualified
    `Module.Member` on a same-unit Module resolves there FIRST, before the cross-unit channels.
    A bare name that scope cannot resolve is looked up across the OTHER modules' Public/Friend
    members BEFORE the .NET-type fallback: one match binds; two is "ambiguous between modules
    'A', 'B'. Qualify it"; a Private match is "'Hidden' is Private to module 'Helpers'". All
    three are errors now where before the first was a phantom and the other two were silent
    reads of the wrong or private global.
  - **Compiler**: `CollectExportedSymbols` also exports Public/Friend `Dim`/`Const` from Module
    child scopes, stamped with their owner. This is the MULTI-FILE half: it used to say
    "Module 'Helpers' does not have a public member 'Value'. Did you mean 'Val'?" — a clean
    diagnostic and a wrong one — while `Helpers.Twice()` resolved, and a Const was unreachable
    even unqualified ("Undefined identifier 'K'"). (The LSP's own collector already had them; only
    the compiler's export lacked them.)
  - **IR builder**: a resolved module member — qualified read, qualified write, or bare
    cross-module reference — lowers to `GlobalReference(name, owner)`: the declared global when
    its declaration has been visited, else a forward reference carrying the same IR name, owner and
    `IsGlobal`, which is all any backend spells it by. Never an `IRFieldAccess`/`IRFieldStore` on
    a phantom receiver. Module Consts are registered like Dims so a reference binds to the
    instance rather than a look-alike local.
  - **Same-name globals get an IR NAME qualified by owner** — `A_Value`, `B_Value` — decided from
    the AST before any declaration is lowered (`CollectSharedModuleGlobalNames`). So every
    backend, every by-name table, and every value the builder RENAMES after its target
    (`Value = Value + 10` inside A) stay distinct BY CONSTRUCTION; no backend needed a collision
    special case, which would also have had to thread the owner through the renamed-value path
    or lose the write. An uncontested name stays bare. `ModuleGlobalCollisionTests` re-pins the
    representation: its six `Name == "Scale"` asserts were pinning the very spelling that let MSIL
    merge them.
  ⛔ **C# THEN FAILED ALONE on the compound form, and it was new.** `Helpers.Value = Helpers.Value
  + 1` renames the result after its target, and a renamed destination carries only the NAME —
  the cross-module qualification an `IRVariable` gets in `EmitExpression` never reached it, so
  the write was spelled bare inside a class with no `Value`: CS0103. Reachable only once the
  front end accepted the shape. `GetValueName` now qualifies a named destination that IS another
  module's global, cached per instance so the IRVariable arm cannot qualify it twice.
  ⚠ **The C# oracle in the new fixture runs IN PROCESS** (Roslyn: emit a console assembly to
  memory, load, invoke the entry point, capture Console.Out). `CliTestHarness.CompileRunCSharp`
  spawns `BasicLang.exe`, a Windows apphost that is not deployed on Linux — which is why the 18
  `_CSharp` rows that use it sit in this machine's 195-row baseline failure set. Measured, not
  assumed: the first draft used it and all 13 running cases failed with Win32Exception.
  ⚠ **RECORDED LIMITATIONS, each pinned or measured:**
  - **Inferred module types do not flow across declaration order**: `Public Value = 5` used
    before its Module is Object at the use (refused "requires numeric operands"); with the
    declaring module first it is Integer and runs (6). A declared `As Integer` has no such
    dependence. Pinned both ways.
  - **A qualified module Const as an ARRAY SIZE is refused** ("must be a compile-time
    constant") — the size folder consults `ConstantValue` through lexical resolution only. The
    same Const in an expression is fine (14). Not chased here.
  - **Cross-FILE same-name globals** meet only in `CombineIRModules`, after each unit's IR is
    built, so their names stay bare. C# is fine (per-module classes), C++ and JavaScript were
    already loud, and MSIL now REFUSES ("declared by more than one module ... across files")
    instead of keeping the last one. Pinned with a two-file compile.
  - **A qualified module CALL in a single file was STILL a phantom-receiver call — FIXED
    2026-09-19**, see the module-procedure-call entry below. (It was why this fixture's write
    read-backs use file-scope functions rather than a `Peek()` inside the module; they still do,
    and the pin that recorded the gap is promoted to a running case.)
  - **And its mirror on C# — FIXED in the same entry**: a BARE cross-module call was CS0103
    there, and there alone.
  - Module = file is still the multi-file resolver's assumption (`FindModuleByName` matches
    unit names, i.e. file stems). Unchanged.
  ⛔ **TWO MUTATIONS SURVIVED THE FIRST SWEEP, and both exposed an uncovered shape.** "Prefer the
  current module's copy in `GetOrCreateVariable`" and "register a Const where it looks" were both
  unreachable from every fixture case, because every MODULE-member reference is intercepted
  before `GetOrCreateVariable` runs. They ARE reachable from a FILE-SCOPE global whose name a
  Module also declares — no owner is stamped at file scope, so that reference still takes the
  bare-keyed table, which holds whichever declaration came LAST: `Scale = Scale + 10` in `Main`
  would silently bump `Beta.Scale` (12 / 12 instead of 11 / 2). Two tests for that shape kill
  both. The Const twin also pins something older: a file-scope Const was never registered where
  `GetOrCreateVariable` looks, so a reference minted a fresh LOCAL of the same name — fine only
  while both were spelled identically, broken the moment the colliding Const is renamed.
  Sixteen mutations, sixteen kills after that.

  ⚠ **CALLS TO A `Module`'s PROCEDURES — qualified, bare and imported — run on every backend as
  of 2026-09-19** — `ModuleProcedureCallTests` (23 cases), `FourBackends` (the shared in-process
  four-backend harness), `SemanticAnalyzer.RecordModuleProcedure` + `PreferModuleProcedure` +
  `StampProcedureOwner`, `IRBuilder.EmitProcedureCall` + `ProcedureCallTarget` + `ProcedureIrName`,
  `IRCall.CalleeModule`, `CSharpBackend.UserCallTarget`, `Compiler.CombineIRModules` (refusal).
  ⛔ **A QUALIFIED CALL LOWERED TO AN INSTANCE CALL ON A PHANTOM RECEIVER.** The analyzer resolved
  `Helpers` to its Module symbol (no type), typed the access Object, and the IR builder's
  static-vs-instance heuristic — "is the receiver's name exactly a class?" — said instance:
  `t0 = Helpers.Twice(4);` on C++ ("'Helpers' was not declared"), ReferenceError on JavaScript,
  `callvirt ... System.Object::'Twice'` (MissingMethodException) on MSIL, 8 on C# by re-emitting
  the text. With the declaring module SECOND, MSIL did not even assemble (`'Helpers'` undefined
  class). Every shape — Function, Sub, self-qualified, nested, from file scope, ByRef, Optional —
  and the MULTI-FILE path identically. **Measured, not assumed, this time: the previous entry's
  "multi-file resolves fine" was the front end only.**
  ⛔ **THE BARE FORM HAD THE MIRROR DEFECT ON C#.** One static class per Module, cross-module
  VARIABLES qualified, CALLS never: `Twice(4)` from Module M was emitted bare inside
  `static class M` — CS0103, on C# alone. No single call form ran on all four backends.
  ⛔ **TWO MODULES WITH THE SAME PROCEDURE NAME LOST ONE, SILENTLY.** Pass 1 flattens procedure
  signatures into the global scope first-wins, so B's `F` had no symbol; a bare `F()` from a
  third module bound to A's; and `CombineIRModules` — which the single-file CLI path ALSO goes
  through (`CompileFile` → `CompileProjectFiles`) — deduplicated IR functions by bare name and
  dropped B's `F` from the output, body and all. Three backends printed A's value for B's caller;
  C# emitted no class B at all. The same first-wins defect this file records for class member
  bodies and for module globals, in its third home.
  ⛔ **THE IMPORTED-CALL WIRE FORM WAS HONOURED BY ONE BACKEND.** A cross-unit bare call went out
  as the dotted IRCall name `"Helpers.Twice"`: C++ stripped it back off
  (`ResolveFlattenedFunctionName`), JavaScript refused it ("no lowering for 'Helpers.Twice'"),
  MSIL sanitised the dot away into `Combined::HelpersTwice` — a method nothing defines. So the
  multi-file bare call was broken on two backends too; nobody had run it.
  ⚠ **The fix mirrors the module-variable one, layer for layer:**
  - **Analyzer**: pass 1 records every Module's procedures in `_moduleMembers` beside its
    variables — ALWAYS, not only when the global scope was free — stamped with `OwningModule`;
    `Module.Proc` resolves through the same `TryResolveModuleMember`. A BARE call prefers the
    enclosing Module's own procedure whatever the declaration order, and a bare name two OTHER
    modules declare is refused ("'F' is ambiguous between modules 'A', 'B'. Qualify it"). ⛔ The
    preferred symbol is WRITTEN BACK onto the callee node: corrected only in the call visitor's
    local, the call was typed against A's F and lowered to B's — measured "2" for "1" on all four.
  - **IR builder**: every module-procedure call — bare, qualified, imported — goes through ONE
    `EmitProcedureCall`, so a qualified call cannot lose what a bare one has (ByRef markers,
    Optional fill, argument coercion). The IR name is bare, or owner-qualified (`A_F` / `B_F`)
    when contested — decided from the AST up front, the same `_sharedGlobalNames` walk as
    variables — and the owner rides on `IRCall.CalleeModule`. One wire form, no dotted names.
  - **C#**: `UserCallTarget` qualifies a call whose `CalleeModule` differs from the emitting
    function's module, "Main" spelled "Program" (a Module literally named `Main` is tested in
    both directions).
  - **Compiler**: `CombineIRModules` REFUSES a cross-file same-named module procedure, naming
    both modules and files, instead of dropping the second; same-named METHODS of different
    classes stay exempt.
  ⚠ **Access WAS enforced for a Module's variables and constants ONLY, not its procedures** —
  parity with `CollectExportedSymbols` ("procedures are always visible"), and because the PARSER
  defaulted a procedure with no modifier to `Private`, the opposite of the language. Enforcing it
  would have refused every plain `Function` on all four. That was a **PINNED DIVERGENCE**: a
  no-modifier module Function ran on C++/JavaScript/MSIL (8) and C# refused it through csc
  (`private static`; CS0122 once the call was qualified, CS0103 before). **FIXED the same day** —
  see the "NO-MODIFIER PROCEDURE IS PUBLIC" entry below: the parser's default, and procedure
  access enforced by the front end on all four.
  ⚠ **WAS PINNED, pre-existing and UNMASKED rather than caused — FIXED the same day, see the
  "CLASS BODY CAN REFERENCE ANY FREE FUNCTION OR GLOBAL ON C++" entry below**: a CLASS method
  calling a module procedure by bare name. The front end used to refuse the whole program
  ("Cannot return type 'Object'"); once it resolved, it ran on JavaScript, MSIL and C#, while C++
  emitted the class BEFORE the free-function prototypes — `'Twice' was not declared`. An
  emission-order gap; the call text was right. Also pre-existing and untouched: a class declared INSIDE a Module block
  (`Helpers.Box`) is broken on all four.
  ⚠ **STALE, corrected 2026-09-21**: this used to read "MSIL fails any ByRef call
  (InvalidProgramException) … the qualified-ByRef case asserts both as they are". **MSIL ByRef
  is FIXED** (see the ByRef entry below): `ByRef_ThroughAQualifiedCall_IsMarked`'s MSIL leg now
  asserts `"5"` with C++ and C# instead of pinning a failure. JavaScript still refuses ByRef by
  design (BL7002) and that leg is unchanged.
  ⚠ **`FourBackends` is the shared harness now** (`Norm`, `RunsOnEveryBackend`,
  `RunEmittedCSharp`, `RunEmittedCSharpText`) — `ModuleMemberAccessTests` and this fixture both
  use it; the multi-file case runs the COMBINED IR through all four generators and executes
  three of them (MSIL's IL is asserted by text: two `call int32 'Combined'::'Twice'`, no
  `HelpersTwice`, no `System.Object::'Twice'`).
  ⛔⛔ **`FourBackends.RunEmittedCSharp` HAS NO TIMEOUT.** It is in-process Roslyn: emit to memory,
  `Assembly.Load`, invoke the entry point. A program that loops forever HANGS THE TEST HOST —
  there is no failure, no name, no output, just a run that never ends. ⚠ **Any shape whose
  failure mode is a non-terminating loop must be asserted on the emitted TEXT
  (`ReturnCoercionTests.EmitCSharpForTest`), not through this harness**, and run on JS / C++ /
  MSIL, whose harnesses all time out. Measured 2026-09-21: `For i = 1 To 4 / t = t + 1 / Exit For
  / Next` and `Exit Sub` in the same position both hang at f20435d. `CSharpLoopExitTests` marks
  every such case.
  ⚠ **One shape per test, and one shape per C++ COMPILE.** A characterization probe that compiled
  five different programs in one test reported the FIRST program's compile error for all five —
  `BclE2E.CompileToCppOptimized`/`CompileRun` reuse one temp directory within a test.
  ⛔ **THIRTEEN MUTATIONS, THIRTEEN KILLS — TWO SURVIVED THE FIRST SWEEP, and one was WRONGLY
  REMOVED before the full suite caught it.** (1) The member-body exemption in `CombineIRModules`'
  collision lookup survived a method-vs-method test, because a class method from the SECOND
  file is added before the lookup ever runs; the shape that reaches it is a class METHOD in the
  first file and a module FUNCTION of the same name in the second. (2) Re-stamping a procedure's
  owner in pass 2 (`AttachOwningModule` in `Visit(FunctionNode)`) survived; a probe on PARAMETER
  types found no distinguishing shape (array parameters do not parse, a generic parameter widens
  to Object either way), so it was removed as unobservable — and the full suite failed three
  `TaskResultTests` rows: "Cannot assign value of type 'Object' to variable of type 'Task'". The
  observable is the RETURN type: pass 1 types `Task(Of Integer)` by bare name and lands on
  Object, and a Module's call to its own procedure BELOW the declaration resolves through the
  record that stamp swaps the fully typed symbol into. Restored, with a fixture test for the
  shape; killed by four now. **"No shape distinguishes it" is a claim about the shapes that were
  TRIED** — the by-name suite comparison is what makes it a fact.
  ⚠ **`NativeEntryPointTests`' duplicate-`Sub Main` probe is re-pinned**: it documented the
  combiner silently keeping one Main (first-wins) as the hazard BL6012's per-unit counting
  exists for; that drop is now a refusal naming both files, and the per-unit design stays right.
  ⛔ **Also in the table**: "never record procedures in pass 1" did NOT kill the plain qualified
  call — with the declaring module first, pass 2's visit still records it — so pass-1
  registration is load-bearing precisely for the reversed order, the bare forms and the
  ambiguity check. Counts are in the commit message.
  ⛔ **THE FIRST FULL-SUITE RUN CAUGHT A REGRESSION THE FIXTURE COULD NOT** — two green C++ tests
  (`Cpp_ModuleLevelConstSizedArray_Allocates…`, `…TwoDimensionalArray_ConstSized…`) went red with
  "Array size must be a compile-time constant". A FILE-SCOPE `Const K` with `Dim g(K)` inside a
  Module: the pass-1 sweep typed the Dim through `ResolveTypeReference`, which FOLDS the declared
  array size — in pass 1, before any Const exists in scope. It now types through
  `ResolveSiblingSignatureType` (`report: false` on dimensions), the resolver the sibling pre-pass
  already uses for this reason. Same lesson as every entry in this file: the fixture proves the
  change, only the full suite proves what it broke — compared BY NAME.

  ⚠ **A NO-MODIFIER PROCEDURE IS PUBLIC, and a procedure's access is enforced on every backend
  as of 2026-09-19** — `ModuleProcedureAccessTests` (fixture), `Parser.ImplicitProcedureAccess` /
  `ImplicitMemberAccess`, `FunctionNode`'s constructor default, `SemanticAnalyzer.IsAccessChecked`
  + `PreferModuleProcedure` + `RefuseHiddenProcedure` + `BindCrossUnitProcedure`, the
  pending-sibling pre-pass (`RegisterSiblingContainerMemberSignatures(…, applyDeclaredAccess)`),
  `LspModuleSymbolCollector.AddMember`, `MsilHarness.RunIl`. The pinned divergence in
  `ModuleProcedureCallTests` is promoted to `ANoModifierModuleFunction_RunsOnEveryBackend`.
  ⛔ **THE PARSER DEFAULTED A NO-MODIFIER `Function`/`Sub` TO `Private`, THE OPPOSITE OF THE
  LANGUAGE** (VB: a Module's or a file's procedures are Public; its Dim/Const Private). Measured
  per parser path before the change: Module member Function/Sub/Async/Iterator → Private; bare
  file-scope `Function` → Private but bare `Sub` → PUBLIC (the two AST constructors disagreed —
  `FunctionNode` said "Private for multi-file", `SubroutineNode` Public); the top-level modifier
  branch (`Iterator Function`, `Shared Function`, no access word) → Private; interface methods and
  extension methods → Private (the constructor default). Class members were Public already. Only
  C# ever noticed, because csc is the one backend that enforces the `private static` the C#
  backend emits per Module class: a plain `Function Twice` ran from any other module on C++,
  JavaScript and MSIL (8) and was CS0122 on C# — single-file, multi-file with Import, multi-file
  WITHOUT Import in either compile order, `.mod`, and a bare file-scope function from another file.
  ⛔ **AND BECAUSE OF THAT DEFAULT, THE FRONT END ENFORCED NOTHING FOR PROCEDURES** (`IsAccessChecked`
  was variables and constants only — enforcing Private would have refused every plain Function
  called across modules). So an explicit `Private Function` / `Private Sub` — qualified, bare,
  statement call, from a class method, from another FILE (every channel), from a `.mod` — ran on
  three backends and was refused by csc alone; a Private `F` beside a Public `F` made a bare call
  from a third module "'F' is ambiguous between modules 'A', 'B'" on all four, because the Private
  one counted as a candidate.
  ⚠ **The fix, layer by layer:**
  - **Parser**: two constants, applied per arm. `ImplicitProcedureAccess` = Public for
    Function/Sub (plain, Async/Iterator, the top-level modifier branch); `ImplicitMemberAccess` =
    Private for Dim/Const/Class/Enum/Structure/Dim-less field — those keep exactly what they had.
    `FunctionNode`'s constructor defaults to Public like `SubroutineNode`, so a bare file-scope
    Function, an interface method, an extension method and a template function all parse Public.
  - **Analyzer**: `IsAccessChecked` covers procedures, so `A.F` on a Private F is refused where
    `A.V` on a Private V is. `PreferModuleProcedure` decides among VISIBLE candidates: the enclosing
    Module's own procedure whatever its access; of the other modules' only Public/Friend — one is
    the answer (so Private-beside-Public binds to the Public one, in either declaration order), two
    are ambiguous and the message names only the visible owners, none with a Private present is
    "'F' is Private to module 'A' and cannot be accessed from here". Cross-unit: exports ALWAYS
    carried every procedure with its declared access (`CollectExportedSymbols`, `Kind == Function`
    arm) — that is kept, deliberately, so the refusal at the binding can name the module instead
    of "Undefined identifier": `BindCrossUnitProcedure` (stamp + refuse) at the three qualified
    channels, `RefuseHiddenProcedure` on an imported bare callee and on the IDE's Import channel.
    ⛔ **The pending-sibling pre-pass registered a Module's procedures WITHOUT their declared
    access** ("applyDeclaredAccess: false", with a comment that misdescribed the compiled path —
    pass 1 does set `symbol.Access`). Measured: with the CALLER's file listed first, the Private
    procedure was callable (9 on three backends); listed second, refused. Module and Namespace
    containers now apply it; a class's methods keep the default, untouched.
  - **LSP**: `LspModuleSymbolCollector` no longer maps a `.mod` procedure's Private to Public
    (`EffectiveAccess`) — that was a workaround for the parser's default and would now hide the
    user's own `Private`. Other member kinds still get the `.mod` promotion. The IDE's
    `ProjectSymbolTable` channel gives the same refusal (tested through `ConfigureProjectSymbols`).
  - **`.mod` promotion** (`Visit(ModuleNode)`) still promotes every procedure to the unit's global
    scope, now WITH its access — the same surface pass 1 flattens for an explicit Module — so a
    `.mod` `Private Sub` is refused by name from another file, Import or not.
  ⚠ **One message for all of it**, the one Private variables already had: "'Hidden' is Private to
  module 'Helpers' and cannot be accessed from here". A Module declared in a differently NAMED file
  (`Module Helpers` in `Util.bas`) is reported as Private to module 'Util' — the unit's name, which
  is what a cross-unit symbol carries (`SourceModule`); `OwningModule` is not copied onto imported
  clones, and copying it would change `IRCall.CalleeModule` for that shape, unmeasured. Recorded.
  ⚠ **Pre-existing and unrelated, surfaced by the probe**: an `Iterator Function` — in a Module or
  at file scope, Public or not — fails to ASSEMBLE on MSIL ("syntax error at token") and is CS0029
  on C# ("Cannot implicitly convert type 'int' to IEnumerable<int>"), C++ and JavaScript run it;
  an Enum declared inside a Module is unresolvable from another module ("Cannot assign value of
  type 'Object' to variable of type 'Color'"); and an UNKNOWN member of a PENDING sibling
  (`Helpers.Nope()`, caller listed first) takes the documented "permissive path WITHOUT erroring"
  and lowers to the phantom instance call on all four (a completed sibling gives "does not have a
  public member"). None touched.
  ⛔ **SIXTEEN MUTATIONS, SIXTEEN KILLS, NO SURVIVORS** (49 kills in all; per-mutant counts in
  the commit message). Two are worth knowing: "bind the lexical symbol instead of the visible
  candidate" is killed by ONE test — the Private-FIRST declaration order, where pass 1's
  first-wins global is A's Private `F` and only the candidate walk reaches B's; and "register a
  pending sibling's procedures without their access" is killed by exactly the two CALLER-FIRST
  cases, which is why the multi-file refusals are asserted in both orders. The parser's Dim arm
  was mutated to Public as a guard and died to one new parse case and two existing
  module-variable tests. Full suite in place: 195 / 6069 / 203 / 6467 against the 195 / 6015 /
  203 / 6413 baseline at `e486382` — 195 reported = 195 anchored lines, the same 170 failing
  names, nothing new and nothing newly passing; the +54 are the fixture's 54 cases.

  ⚠ **A CLASS BODY CAN REFERENCE ANY FREE FUNCTION OR GLOBAL ON C++ as of 2026-09-19** —
  `CppEmissionOrderTests` (fixture), `CppCodeGenerator.EmitDeclarationsClassBodiesNeed` (shared
  by `Generate` and `CppCodeGenerator.Split.EmitAggregateHeader`), the KEEP-IN-SYNC section
  order in both. The pinned ordering gap in `ModuleProcedureCallTests` is promoted to
  `AClassMethodCallingAModuleProcedure_RunsOnEveryBackend`.
  ⛔ **A CLASS'S METHODS ARE DEFINED INLINE IN ITS BODY, AND AN INLINE MEMBER BODY SEES ONLY THE
  NAMESPACE-SCOPE NAMES DECLARED BEFORE THE CLASS.** The emitter wrote forward decls → enums →
  delegates → interfaces → CLASSES → static inits → globals → externs → prototypes → bodies. So
  every free function and every global a method touched was "use of undeclared identifier" on
  this backend alone — measured, compiled and run on all four, before the change: a Module's
  `Twice(4)` and `Helpers.Twice(5)`, its Sub, a file-scope function (declared before OR after
  the class), a constructor's call, a property getter's, a Shared method's, an Optional and a
  ByRef callee, a Module global (bare and qualified), a Const, a sized array, a string, a
  file-scope global, a struct global, a global initialized from an earlier global — and the
  SPLIT header identically. JavaScript, MSIL and C# ran every one of the module-scoped ones.
  ⚠ **The fix**: prototypes of the standalone functions and `extern` declarations of the
  globals go out after the interfaces and BEFORE any class body, from one helper both emitters
  call; the definitions keep their places (a struct global needs its complete type; a global's
  initializer needs the globals declared before it — `Dim I As Integer = H` is asserted to
  still hold). Placement is measured, not assumed: a prototype naming an ENUM must follow the
  enums, one naming an INTERFACE must follow the interfaces (interfaces are not forward-declared;
  classes and structs are, and a forward declaration is enough for a prototype even by value).
  `extern T g;` followed by the split header's `inline T g = …;` is one inline variable —
  measured on g++ and clang++ across two translation units before writing it.
  ⛔ **A SECOND, DISTINCT DEFECT, found while pairing the split declarations with their
  definitions: the split header DROPPED A GLOBAL'S DECLARED INITIALIZER.** `Public Count As
  Integer = 5` built through a `.blproj` was `inline int32_t Count = {};` and read 0 — the
  wrong number from a build that reported success, on that path alone. The combined emission had
  this exact bug fixed on 2026-09-17 ("a DECLARED initializer wins") and the split site never
  got it; `Split_AModuleGlobalWithADeclaredInitializer_IsInitialized_CompilesAndRuns` is its
  own test, with no class involved. Fifth declaration site of the same helper, second home of
  the same drop.
  ⚠ **C# HAD ITS OWN GAP HERE — FIXED the same day, see "A FILE-SCOPE PROCEDURE OR GLOBAL IS
  REACHABLE FROM ANY CONTEXT ON C#" below**: it qualified a call or a global only when the
  callee's module name differed from the emitting function's, and a class body — or a MODULE
  BLOCK — is never inside the file module's static class. So a FILE-SCOPE function or global
  used from a class method, or from `Module M`'s `Sub Main`, was CS0103 on C# while the other
  three ran it. The three pins here are promoted to `_RunsOnEveryBackend`.
  ⛔ **FOUR C++ GAPS MEASURED AND PINNED, none this change's**: (1) a class using a LATER class's
  member — "member access into incomplete type"; the reverse order runs on all four. Needs
  out-of-line member definitions (or dependency-ordered classes); the prototype fix cannot
  reach it. (2) A `ReadOnly Property … Get` is not reachable as `b->Doubled` ("no member
  named"); the other three print 12. (3) `Me` passed to a free function taking the class —
  `this` is a raw pointer where the prototype wants `shared_ptr<Box>`. (4) A GENERIC free
  function is "unknown type name 'T'" even from `Main`. **And one JavaScript gap**: a
  constructor writing a call result STRAIGHT to a field (`V = Twice(21)`) prints 0; the
  local-first form prints 42 on all four. Each pinned with the exact compiler message.
  ⚠ **The front end refuses these shapes on all four, so no test could use them**: an enum
  member as a call ARGUMENT (`Code(Color.Green)` — "cannot convert from 'Object' to 'Color'";
  the multi-file front end ACCEPTS it and C++ then fails "'Color' does not refer to a value"),
  `New Sq()` as an interface-typed argument, `AddressOf` to a Delegate parameter, a global of
  class type declared AFTER the class that reads it, a non-constant static field initializer.
  The enum and interface placement tests use a typed local instead.
  ⛔ **ELEVEN MUTATIONS, ELEVEN KILLS (94 in all) — ONE SURVIVED THE FIRST SWEEP AND TWO DID
  NOT BUILD.** The survivor: the SPLIT header's block placed BEFORE the enums passed every test,
  because no split test carried an enum-typed prototype (the single-file placement test did).
  Two split tests now mirror the single-file enum and interface ones; it dies to both, and the
  interfaces twin dies to exactly one. The two that did not build were ill-formed MUTANTS, not
  survivors: moving the call above the enums referenced `standaloneFunctions` before its
  declaration; corrected to compute the list inline, they die to the enum test (and the
  interface test, which is also after the enums) and to the interface test alone. Discrimination
  held everywhere else: "after the classes" in one emitter never touched the other's tests,
  "no global declarations" never touched a function-only test, the `extern` keyword dropped
  in the split header was a duplicate symbol across two translation units (the pre-existing
  fixed-size-array split test died too), and the initializer drop died to its own class-free
  test. Full suite in place: 195 / 6101 / 203 / 6499 against the 195 / 6069 / 203 / 6467 baseline
  at `d2ad064` — 195 reported = 195 anchored lines, the same 170 failing names, nothing new and
  nothing newly passing; the +32 are the fixture's 32 cases.

  ⚠ **A FILE-SCOPE PROCEDURE OR GLOBAL IS REACHABLE FROM ANY CONTEXT ON C# as of 2026-09-19** —
  `CsFileScopeQualificationTests` (fixture), `IRBuilder.ProcedureCallTarget` +
  `IsFileScopeProcedure` + `IsCurrentClassProcedure`, `SemanticAnalyzer.LookupType`,
  `CSharpBackend._currentModuleClass` + `EmittedInsideModuleClass` + `ModuleMemberAccess`. The
  three C# pins in `CppEmissionOrderTests` are promoted to `_RunsOnEveryBackend`.
  ⛔ **EVERY FILE-SCOPE NAME USED FROM OUTSIDE THE FILE MODULE'S STATIC CLASS WAS CS0103 ON C#
  ALONE** — measured, compiled and run on all four, before the change: a file-scope function
  (declared before or after the class), Sub, Optional and ByRef callee, global (read and
  write), Const, sized array and class-typed global from a METHOD; a function from a
  constructor, a property getter, a Shared method, a lambda in a method; a function from a
  `Module` block's `Sub Main` and from its procedure (either declaration order); and, in a
  multi-file build, a class calling a function of ITS OWN file (an IMPORTED file's ran — that
  callee arrived with its source module). C++, JavaScript and MSIL ran every one.
  ⚠ **Two causes, both fixed.** (1) The IR builder gave a file-scope callee NO owner:
  `ProcedureCallTarget` stamped a Module's procedure with its Module and an import with its
  source module and left everything else `(Name, null)`. It now stamps a file-scope procedure
  with the file's module — `(GlobalIrName(_module.Name, name), _module.Name)` — the same wire
  form a Module's procedure has. What is NOT file scope, each measured: a stdlib procedure
  (registered at line 0 — its IR name must stay the one the backends' tables know), a
  `Declare` (externs are emitted into whichever module class comes first, so no owner is right;
  a Declare from a class body stays CS0103, out of scope), and a method of the class being built
  or of a base — decided by asking the analyzer's class type (`LookupType`, complete after
  analysis), because pass 1 flattens every
  method signature into the global scope first-wins, so a method declared BELOW its caller, or
  one sharing a name with a file-scope function declared ABOVE the class, arrives bound to a
  global-scope symbol; every backend resolves the bare spelling to the member (the probe
  printed the member's 3, never the function's 100, on all four) and the stamp must not turn
  that into `Program.Helper()`. (2) The C# backend decided "bare or qualified" by comparing
  MODULE NAMES, the member's against the emitting function's; a class's methods carry the file
  module's name too, so the names compared equal and the reference went out bare inside a class
  that has no such member. It now records WHICH module's static class it is writing
  (`_currentModuleClass`, set around each one) and qualifies unless the reference lands there —
  a class body, an interface, a class in a named namespace are outside every module class.
  ⛔ **A THIRD DEFECT, unmasked by qualification: a file-scope `Dim` or `Const` is Private by
  default and was `private static` in the file's class, so `Program.Total` from a class or a
  Module block was CS0122 the moment it was spelled right** (a Module block reading a
  file-scope global ALREADY failed that way — qualified, then inaccessible — measured before the
  change). A file-scope Private is private to its FILE, a Module's Private to its Module, and
  the front end enforces both; a module static class's members now map Public → `public`,
  else `internal` (`ModuleMemberAccess`: constants, globals and the standalone functions), as
  MSIL already did (`assembly`). Class members keep `MapAccessModifier`. A Module's own Private
  global and procedure are asserted still reachable inside it.
  ⚠ **Also fixed by (1), broken on ALL FOUR before**: a file-scope `F` beside `Module A`'s `F`
  was DECLARED owner-qualified (`Main_F`, from `ProcedureIrName`) but CALLED as the bare `F` —
  a function nothing defined ("undeclared identifier 'F'" on C++, "F is not defined" on
  JavaScript, MissingMethod on MSIL, CS0103 on C#). The call now goes out under the declared
  name. Same for the contest against a Module VARIABLE of that name.
  ⚠ **Pre-existing, measured here, untouched**: a class in a NAMED NAMESPACE does not run on
  C# — the module classes go into the default namespace and the class into `App`, with no using
  between them (CS0246 `Box` from the Module's `Main`, CS0103 the file class from `App`); the
  other three run it, and the qualified spelling is pinned on the text. A property getter as a
  member on C++, an inherited method on MSIL (MissingMethod), `Func` on MSIL, a class-returning
  callee on MSIL (ilasm syntax error), ByRef on JavaScript — each case runs on the
  backends without that gap and names it. (⚠ **corrected 2026-09-21**: this row used to read
  "ByRef on JavaScript/MSIL". MSIL ByRef is fixed — see the ByRef entry below — so only the
  JavaScript refusal remains.) `Public Total As Integer` at file scope (no `Dim`)
  does not parse.
  ⛔ **SEVENTEEN MUTATIONS, SIXTEEN KILLS, ONE SURVIVOR REMOVED** (164 kills in all on the
  final code; per-mutant counts in the commit message). Every kill set is discriminating: no
  file-scope stamp died to the 21 call shapes and nothing global; the contested name called
  bare to exactly the two contested tests; the `Declare` and stdlib exclusions to the wire-form
  pin alone; the class lookup disabled to the five own-method and inherited cases, and the base
  walk skipped to the inherited one alone; the module-class record never set — everything
  qualified, which COMPILES — to the two "stays bare" pins alone, and never cleared to the
  namespace pin alone; the old module-name comparison to the 20 class-body shapes and no
  Module-block one; the global qualification dropped to 32 global shapes (the pre-existing
  module-global tests included) and the call qualification dropped to 57 call shapes; the
  named-destination lookup to the five writes; each of the three `internal` sites to exactly
  its shapes (Const 2, global 7, function 2); the analyzer lookup returning null to the same
  five as the class lookup. **The survivor**: a check that the callee's DECLARING SCOPE is the
  global or namespace scope passed every test — every class-scope symbol the class lookup
  already excludes, and every module-scope one carries its owner, so nothing it refused ever
  reached it. Removed rather than tested around; the five mutants that had run before the
  removal were re-run on the final code (same kills, and the class lookup now also catches the
  two "declared above" own-method cases the removed check used to, five kills where it had
  three).
  **Full suite in place: 195 / 6136 / 203 / 6534 against the 195 / 6101 / 203 / 6499 baseline
  at `045477d`** — 195 reported = 195 anchored lines, the same 170 failing names, nothing new
  and nothing newly passing; the +35 are the fixture's 35 cases.

  ⚠ **A DERIVED CLASS CAN SEE ITS BASE as of 2026-09-20** — `InheritedMemberTests` (33 cases),
  `SymbolTable.TypeInfo.ResolveMember` (the walk), `SemanticAnalyzer.ResolveClassMember` plus
  the member-access, `With`-member and assign-to-constant sites,
  `CppCodeGenerator.InitializeFunctionContext`, `MSILBackend._currentClassFieldOwner` +
  `FieldOwnerToken` + `DeclaringFieldToken` + `DeclaringClassOfInstanceMethod`,
  `IRBuilder.Visit(MyBaseExpressionNode)`. The two deliberate tripwires in
  `CppSharedAccessTests` are promoted from pins to compile-and-run.
  ⛔ **ESSENTIALLY NO INHERITED DATA MEMBER WORKED, ON ANY OF THE FOUR BACKENDS, and the
  language's own inheritance was unusable because of it** — measured, compiled and run before
  the change: a derived method naming an inherited field, Protected field, `Const`,
  auto-`Property`, Get/Set property or `Shared` field was REFUSED; so was reading or writing one
  through an instance (`b.Total + 1` came out as "Arithmetic operator '+' requires numeric
  operands", and `Dim n As Integer = b.Total` as a conversion error); `Me.Field` was refused;
  `MyBase.Field` emitted a reference to an undeclared `__base` on every backend; a three-level
  chain was refused; `With b : .InheritedMember` was "does not have a member"; and an inherited
  member sharing a name with a module global already DIVERGED SILENTLY — C++ read the field, the
  other three the global. 22 shapes went from refused-on-all-four to running-on-all-four.
  ⚠ **Only METHODS appeared to work, and not by inheritance**: pass 1 flattens every procedure
  signature into the GLOBAL scope by bare name, so a bare inherited call found it there. That
  accident is also why ⛔ **the defect had THREE FACES BY SPELLING** — a one-character or
  lowercase name reached "Undefined identifier", while an ordinary PascalCase name was swallowed
  by the deliberately permissive "any PascalCase identifier could be a .NET type" arm into a
  phantom type with NO DIAGNOSTIC AT ALL. The common case was the silent one; a fixture with one
  face and not the other pins half the defect, which is why both are `TestCase` rows.
  ⚠ **One missing walk was the whole cause.** `TypeInfo.Members` holds a type's OWN members, a
  class scope's parent is the scope the class was DECLARED in (never its base's), and no lookup
  consulted `BaseType`. The bare-name call goes AHEAD of every other channel in
  `Visit(IdentifierExpressionNode)`: below the .NET-type arm it would fix only one-character
  names, and class scope is nearer than module scope, which the IR builder already assumes.
  ⚠ **Private has TWO halves and both are load-bearing.** A base's Private member is skipped —
  pass 1 filters Private out of the member table and pass 2 does not, so without the explicit
  skip whether one resolved would depend on DECLARATION ORDER, and admitting one turns a
  front-end acceptance into a CS0122 or a clang private-access failure in the emitted code. The
  class's OWN Private member is honoured, and the only shape that observes it is `Me.Secret`: a
  bare name finds one lexically without the walker ever running.
  ⚠ **The depth guard is not decoration.** A base is a NAME and nothing validates the chain is
  acyclic, so `Class A Inherits B` against `Class B Inherits A` spins forever. ⛔ **The lookup
  has to happen AFTER the cycle closes** — pass 2 binds each class's `BaseType` as it visits the
  class, so inside A's own body B's base is still unset and the walk ends after one step. The
  test puts the miss in `Main`, on a worker with a 30-second wait, because a hang is the one
  failure a suite cannot report on its own. (`TypeInfo.IsAssignableFrom` has the same unguarded
  walk, pre-existing and untouched; nothing reaches it with a cyclic pair today.)
  ⛔ **A PRE-EXISTING MSIL DEFECT turned up in the emitted IL and is fixed here** because the
  walk reaches it: a MODULE-level function was never given a fresh class-member context, so it
  kept the LAST EMITTED CLASS's field tables. A module function naming something that class also
  declares took the bare-FIELD path and emitted `ldarg.0` in a STATIC method —
  `InvalidProgramException` at load. It needs only a name collision, no inheritance at all.
  ⚠ **Six gaps are PINNED WITH A CONTROL proving each is not inheritance's**: the same shape
  against the class's OWN member fails identically. A base declared BELOW its derived class (C++
  and JavaScript emit classes in declaration order; it fails with no member access at all), a
  Get/Set property by bare name on C++, a `With` block over a class instance (unimplemented on
  every backend), a case-different bare spelling, a JavaScript field write whose right-hand side
  is a call, and a cross-file base class.
  ⛔ **SIXTEEN MUTATIONS, SIXTEEN KILLS, 172 KILLS IN ALL — FOUR REMOVALS AND TWO TESTS ADDED.**
  The first sweep left FIVE survivors and none were accepted. Three were redundant code, deleted:
  a re-resolution of each base BY NAME through the analyzer's type table (`BaseType` already holds
  the registered type object, so it could only return what the walk already had — and in the one
  case it was meant for, a synthetic member-less stand-in minted for an unresolved base, the name
  is not in the table either), the C++ registration of inherited PROPERTIES as declared
  identifiers (a property access never takes the decayed-temp path a field write does), and the
  MSIL property-owner table (a property is reached through its accessor, which already names its
  declaring class). A fourth came out of reading the final diff rather than the sweep —
  `ResolveMember`'s `includeSelf` parameter, which no caller ever passed `false`. Two were weak
  tests, strengthened: the depth guard and the own-Private exemption, each now killed by exactly
  the one test written for it. ⚠ **Every kill set is discriminating**: no base step → 21; a base's
  Private admitted → the one refusal test; the walk starting at the base instead of the type → 91
  (it breaks every OWN-member lookup, which is the point); the bare-name site → 16 and the
  member-access site → 6; the `With` site, the const-guard site, the cycle guard and the
  depth-zero exemption → 1 each, their own test; C++ own-members-only → the one COMPUTED write (a
  write that compiles clean and loses the value); `MyBase` back to `__base` → the `MyBase` test;
  MSIL seeding own fields only → 11 and the field token taken from the class being emitted → the
  same 11; the receiver's static type → 5; self-calls resolved own-only → the two bare inherited
  calls; the module-function reset dropped → the two name-collision tests, one of which has no
  inheritance in it.
  **Full suite in place: 195 / 6169 / 203 / 6567 against the 195 / 6136 / 203 / 6534 baseline at
  `045477d`** — 195 reported = 195 anchored lines, the same 170 failing names, nothing new and
  nothing newly passing; the +33 are the fixture's 33 cases.

  ⚠ **AN OVERRIDABLE PROPERTY DISPATCHES as of 2026-09-20** — `OverridablePropertyTests` (21
  cases), `PropertyNode.IsVirtual`/`IsOverride`, `Parser.cs` property arm,
  `IRProperty.IsVirtual`/`IsOverride`, `IRBuilder` property build,
  `CSharpBackend.GenerateProperty`, `MSILBackend.GenerateProperty`.
  ⛔ **AN OVERRIDDEN PROPERTY SILENTLY ANSWERED THE BASE'S VALUE ON MSIL AND C#** — measured,
  compiled and run before the change: reading one through a base-typed variable gave `base`
  where `derived` is correct, with NO diagnostic from either backend, and a three-level chain
  gave the TOPMOST value. JavaScript was right by accident (JS class members always dispatch
  dynamically). The same programs with a METHOD were correct on all four, which is what makes it
  a property defect and not an inheritance one.
  ⚠ **THE MODIFIER WAS PARSED AND THEN THROWN AWAY.** `Parser.cs` reads Overridable/Overrides
  into `isVirtual`/`isOverride` locals for EVERY class member; the `FunctionNode` and
  `SubroutineNode` arms copy them onto the node, and the PROPERTY arm copied Access, IsStatic,
  IsReadOnly and IsWriteOnly and dropped the two it already held — because `PropertyNode` had no
  field for them, and neither did `IRProperty`, while `IRMethod` carried IsVirtual, IsOverride,
  IsAbstract and IsSealed. Four layers, one omission at each.
  ⛔ **READ OUT OF THE EMITTED CODE, NOT INFERRED.** The C# was `public string Name { get {…} }`
  on BOTH classes — no `virtual`, no `override`, not even `new` — and C# hiding is only a
  WARNING, so it compiled and returned the base's value. The IL emitted both getters as `.method
  public hidebysig specialname instance string get_Name()` with no `virtual newslot`; the CALL
  SITE was already `callvirt instance string 'Animal'::get_Name()`, and callvirt against a
  non-virtual method binds statically.
  ⚠ **A REGRESSION INTRODUCED BY THE FIX AND THEN FIXED.** `Public Shared Overridable Property`
  is ACCEPTED by the front end — measured; VB refuses it (BC30503), a separate front-end gap not
  decided here. Marking it virtual emitted `public static virtual int N`, which does not compile
  (CS0112), where before it was a plain static property that did; `static virtual` does not
  assemble either. Both emitters drop the modifier for a Shared property and the shape is pinned
  on BOTH — a guard on one backend alone leaves the other emitting a file that cannot be built.
  ⚠ **RUNS ON THREE, NOT FOUR, and the reason is pinned WITH A CONTROL**: C++ cannot emit a
  property as a reachable member at all — the control has ONE class, no inheritance and no
  Overridable, and still fails ("returning reference to local temporary object").
  `CppEmissionOrderTests` pins the same gap from the other side. The base-typed-PARAMETER shape
  runs on TWO, because MSIL keys the callee signature on the argument's DYNAMIC type and calls a
  `Report(Dog)` nobody declared; its control is the identical shape with a method, which fails
  the same way. Both pins go RED when the gap closes.
  ⛔ **FIFTEEN MUTATIONS, FIFTEEN KILLS, 99 KILLS IN ALL**, discriminating by LAYER and by
  BACKEND: the parser arm → 12 / 11 INCLUDING the parser test; the IR-builder copy → 11 / 10
  EXCLUDING it; the C# modifier dropped, spelled `new`, or `virtual` for an override → 9 each,
  C# rows plus the emitted-C# pin and never the IL pin; the MSIL modifier dropped or `newslot`
  on an override → 8 each, MSIL rows plus the IL pin and never the C# pin; the explicit getter
  site → 7; and five 1-kill mutants each dying to exactly the one test written for it (both
  Shared guards → the Shared pin, both auto sites → the auto case, the explicit setter → the
  setter case).
  ⛔ **THE SWEEP FOUND A TEST GAP RATHER THAN CONFIRMING THE FIX.** Isolating each of MSIL's four
  accessor sites showed the explicit SETTER's modifier SURVIVING: marking only the getter
  virtual left every test green, because each one only READ a property. A write through a
  base-typed variable binds to the static type's accessor when the setter is not virtual, so
  `a.Tag = "x"` silently ran the BASE's setter. The gap hid behind a flaw in the harness itself
  — a bulk "keep only the first site" mutant whose replace-the-last-N logic was off by one,
  stripping two sites rather than three and keeping BOTH auto sites. Deleted rather than
  repaired; the four per-site mutants cover every site exactly.
  **Full suite in place: 195 / 6190 / 203 / 6588 against the 195 / 6169 / 203 / 6567 baseline at
  `1b7f32e`** — 195 reported = 195 anchored lines, the same 170 failing names, nothing new and
  nothing newly passing; the +21 are the fixture's 21 cases.

  ⚠ **A USER CLASS WORKS AS A TYPE ON MSIL as of 2026-09-20** — `MsilClassTypeTests` (33 cases),
  `MSILBackend.cs`: ten `IlTypeSpec` spec positions, `DeclaredParamList` +
  `DeclaredFunctionParams`/`DeclaredMethodParams`/`DeclaredCtorParams`/`DeclaredFieldType`,
  `ImplementsInterfaceMember`, `DeclaredInterfaceMethod`, `IsIlValueType`.
  ⛔ **MSIL COULD NOT COMPILE A PROGRAM THAT PASSES OBJECTS AROUND** — 4 of 30 shapes ran before,
  28 after, each asserted against C# COMPILED AND RUN rather than against a literal.
  ⚠ **TWO INDEPENDENT DEFECTS, and the controls separate them.** (1) WRONG RENDERER: a spec
  position rendered with `MapType` (bare) instead of `IlTypeSpec` (which adds the `class` prefix),
  so `stfld Tag 'Box'::'Item'` came out bare and ilasm refused the whole FILE — ASSEMBLE time, and
  a one-class control with no inheritance fails identically, so it is not about polymorphism.
  (2) WRONG SOURCE: the type taken from the VALUE at the site rather than the DECLARATION, so
  `Report(New Dog())` against `Report(a As Animal)` called a method nobody declared — it
  ASSEMBLES (ilasm does not resolve member references) and dies at RUN time with
  MissingMethodException. **An exactly-typed argument hides (2) completely**, which is the trap
  below.
  ⚠ **An interface needed three more things**: the implementation emitted `newslot virtual final`
  (a non-virtual method cannot fill an interface slot — the type would not even LOAD); the call
  signature taken from the INTERFACE (an interface receiver is not a class, so every class-side
  lookup missed it and fell back to the call site, where the front end types the call `Object`);
  and then the IR and the IL disagree in BOTH directions — a Function returning Integer leaves an
  int32 where the destination temp is an object slot (stored unboxed → NullReferenceException
  inside `Console.WriteLine`, pointing at the PRINT not the call, so box it), and a SUB returns
  nothing while the call is still typed `Object` (a store after a call that pushes nothing →
  `callvirt instance void …` then `stloc.2`, InvalidProgramException and the CLR names no line).
  ⛔ **THE MUTATION SWEEP FOUND A DEAD HELPER THAT HAD ALREADY BEEN SHIPPED.**
  `DeclaredCtorParams` read `IRConstructor.Parameters`, which `IRBuilder` NEVER fills — it sets
  only `Access` and `Implementation` — so it always returned null and every constructor call
  silently fell back to spelling the ARGUMENT types, the exact defect the helper exists to
  prevent. Measured: `newobj instance void 'Shelter'::.ctor(class 'Dog')` against a constructor
  declared `.ctor(class 'Animal')`. **`JavaScriptBackend` already carried a comment saying that
  field is always empty**, and C#, C++, LLVM and this file's own property site all read
  `Implementation.Parameters`. ⚠ **Two mutants survived the first sweep NOT because the tests
  were weak but because they were EQUIVALENT MUTANTS OVER BROKEN CODE**: nulling out a helper
  that already returns null changes nothing. A surviving mutant can mean the code under it is
  dead — check that before blaming the fixture.
  ⛔ **23 MUTANTS, 21 KILLS, 0 BUILD BREAKS**, LINE-anchored because several anchor texts are not
  unique in this file (`var returnType = IlTypeSpec(method.ReturnType);` appears 3×,
  `var propType = IlTypeSpec(prop.Type);` 4×) — a text replace would hit the wrong site and the
  mutant's NAME WOULD LIE. A first sweep of 20 left SEVEN alive; five were converted by adding
  the shape that distinguishes them, and **those five shapes are where the last five tests came
  from**: a base ctor, a self call and a `newobj` each handed a DERIVED argument against a
  base-typed parameter, an interface member returning a class, and an interface `Sub`.
  ⚠ **SPLITTING ONE TERNARY INTO TWO MUTANTS IS WHAT EXPOSED THE SELF-CALL GAP** — the two arms
  of one line behaved oppositely (free-function call → 5 kills, self call → SURVIVED). Mutated as
  a unit, the free arm's kills would have masked the self arm entirely.
  ⚠ **`IlPrimitives` IS NOT A VALUE-TYPE TEST** — it carries `string`, `object` and `void`. A
  guard written `IlPrimitives.Contains(returnType) && !IlPrimitives.Contains(MapType(…))` SILENTLY
  NEVER FIRES, because `MapType` is `object` for an interface call and `object` is in the set.
  That version WAS shipped mid-task. `IsIlValueType` now says what it means. ⛔ **Mutating only
  the FIRST half is NEAR-EQUIVALENT and proves nothing**: for `string` the extra `box` is a no-op
  on a reference type (ECMA-335 III.4.1), so only a VOID-returning member distinguishes it — the
  interface `Sub` case is what kills it.
  ⚠ **TWO MUTANTS SURVIVE AND THE CODE IS KEPT, because they are UNREACHABLE, not untested.**
  A `Delegate` returning a class: the FRONT END does not implement user Delegate types
  (`AddressOf` yields 'Func', not the declared type; calling it types as 'Void'), so no legal
  program reaches `GenerateDelegate`. An interface PROPERTY: broken on BOTH .NET backends
  independently — C# emits an accessor-less property (**CS0548**) and MSIL lowers the access to a
  FIELD load (**MissingFieldException**), both newly characterized here and a different family.
  Both lines are correct and identical to their eight proven siblings; reverting one to the
  spelling known to be wrong, to buy a mutation score, would re-introduce the bug the day either
  feature starts working.
  ⛔ **Still out of scope on MSIL, each measured and each a different family**: `For Each` over a
  collection (the loop variable is never declared, the enumerator overwrites the list's own local,
  and the body is emitted TWICE — `List(Of String)` fails identically) — **FIXED 2026-09-20, see
  the entry below** — and `Dim x(n)` bounds (C# throws IndexOutOfRange on the same program).
  ⭐ **`ABaseTypedParameter_IsAPreExistingMsilGap_Pinned` WENT RED**, which is what it was written
  for — MSIL now runs a base-typed parameter. Promoted to three backends, pin deleted.
  **Full suite in place: 195 / 6222 / 203 / 6620 against the 195 / 6190 / 203 / 6588 baseline at
  `f2727f4`** — 195 reported = 195 anchored lines, the same 170 failing names, nothing new and
  nothing newly passing; the +32 are the fixture's 33 cases less the deleted pin.
  ⚠ **Comparing failing NAMES needs the same normalization on both sides** — the recorded
  baseline strips parameterized arguments, so a raw `sort -u` reads 195 distinct names against
  its 170 and looks like 25 regressions. It is 3 bare names versus their 28 parameterized forms.
  Normalize, then compare.

  ⚠ **`For Each` RUNS ON MSIL as of 2026-09-20** — `MsilForEachTests` (40 cases),
  `MSILBackend.cs`: `AllocateForEachLocals`, `EmitForEachBody`, `Visit(IRForEach)`,
  `EmitRegionAwareBranch`, `IsIterationBranch`,
  `_foreachSlots`/`_foreachContinueLabels`/`_consumedBlocks`. Before: `InvalidProgramException`.
  ⛔ **FOUR DEFECTS IN ONE CONSTRUCT, NOT ONE.**
  - **(A)** the enumerator local came from `_localCounter++`, unrelated to `_localIndices`, so it
    **overwrote the collection's own slot** — the same counter defect the catch-variable and
    indexer sites already record.
  - **(B)** the loop variable got **no `.locals` slot at all** — `IRBuilder` deliberately keeps it
    out of `IRFunction.LocalVariables` ("the foreach statement declares it"), which is right for
    the three text-emitting backends and leaves IL with no storage. Emitted
    `// WARNING: Unknown local 'n'` and an `add` with ONE operand.
  - **(C)** the body was **emitted TWICE** — `ControlFlowGraph.Build` wires `IRForEach.BodyBlock`
    in as a CFG successor and `GenerateBasicBlock` walks successors. ⚠ **It is the same bug
    Try/Catch had (~line 219), and `Visit(IRTryCatch)`'s `_visitedBlocks` fix is necessary but
    NOT sufficient** — `EmitRegionBody` collects its block list UP FRONT, so a `For Each` inside
    a `Try` needs both `_visitedBlocks` and `_consumedBlocks`.
  - **(D)** ⛔ **`Exit For` ran as `Continue For`** — `IRBuilder` gives a loop's break and continue
    targets ONE block and `IRBranch.IsLoopExit` is the only discriminator; C++ and JS have read it
    since task_4cc381f1, MSIL never did. Measured on `{1,2,3,4}` exiting at 3: total **7** instead
    of **3**, **from a program that ran clean**. A wrong answer, not a crash.
  ⛔ **THE C# BACKEND WAS WRONG ON FOUR OF THESE SHAPES** — `Exit For` in a `For Each` (correct 3,
  C# **10** — `Exit For` was a **no-op on C#**); nested (60 vs 200); inside a `Try` (3 vs 10); as
  the last statement (1 vs 2) — so those four asserted against C++/JS instead of C#.
  ⭐ **FIXED (C#-backend Exit/Right batch, 2026-09-21) and all four PROMOTED to
  `MsilAgreesWithCSharp`.** Re-measured: 3 / 60 / 3 / 1 on C#. `Exit Sub` was a no-op on C# too and
  is fixed in the same batch. The one shape still not promotable is `For Each n In Make()` — MSIL
  gives 7, **C# does not compile** (`CS0103 't0'`).
  ⭐ **What the mutation sweep taught, worth recording as method.**
  - **`a1`/`a2` killed DISJOINT sets summing to exactly 40, and so did `c2`/`c3`.** Mutated as one
    site each, the 34-kill arm would have masked the 4-kill arm and the nested arm would have
    masked the Try arm. Line-anchored splitting was load-bearing — the same lesson this file
    already records for splitting a ternary.
  - **`d6` killed by infinite loop** — every shape hit the harness's 30s timeout; that one mutant
    took 17m21s.
  - ⛔ **A mistyped `.locals` slot is INVISIBLE TO A ROUND TRIP.**
    `a3-loopvar-type-from-collection` (the loop variable typed from the COLLECTION, not the
    element) **assembles and prints the correct answer** — .NET Core does not verify IL for
    fully-trusted code. Not cosmetic: **a reference-typed slot is a GC ROOT**, so the collector
    traces it as an object pointer while it holds a raw integer. Latent, not absent. The FOURTH
    property in this backend invisible at run time (the file already records the Select Case
    default branch, the variable-less `Catch`'s `pop`, and the wrong overload); the remedy is the
    same — an IL-TEXT pin via `MsilHarness.CompileToIl`.
  - ⚠ **A test can prove its own name and still not discriminate.**
    `ExitFor_InANestedForEach_LeavesOnlyTheInnerLoop` with inner `{5,10,15}` exiting at 15 totals
    60 under BOTH exit and continue — nothing after the exit point for them to diverge on.
    `{5,10,15,20}` makes them diverge (60 vs 140) and took the two `IsLoopExit` mutants
    (`d3`/`d4`) from 3 kills of the fixture's 4 `Exit For` tests to 4. Found only because those
    two mutants each killed 3 of 4 instead of all 4.
  - **`e2-name-binding-never-withdrawn` needed a DIFFERENT shape than the obvious one.** Shadowing
    a real LOCAL only exercises `EmitForEachBody`'s restore-to-prior-index arm; the
    withdraw-with-no-prior-binding arm needs a name with no local meaning but a MODULE-level one,
    so the read after the loop is satisfied only by falling through `_localIndices` to
    `_moduleGlobals` — which happens only if the binding was genuinely removed. Measured:
    `ldloc.1` (the stale loop slot) instead of `ldsfld int32 'Combined'::'n'`, printing the loop's
    last element (2) instead of the module global (7).
  ⚠ **TWO MUTANTS SURVIVE AND THE CODE IS KEPT, both unreachable by measurement.**
  - **`d2`** — the iteration redirect in `Visit(IRConditionalBranch)`. Measured over 9 body
    shapes: the redirect fires 13×, a `brtrue` targets a `For Each` continuation ZERO times,
    because `IRBuilder` always gives an `If` a dedicated merge block and it is the merge block
    that carries the edge. Kept: it is the structural sibling of the reachable
    `LeavesRegion(trueTarget)` arm below it, and deleting it plants a silent wrong answer the day
    anything threads `if0end`'s sole `br` into the condbr.
  - **`d7`** — the fall-out branch for an unterminated body block. It IS reached (only when a
    block ends with a nested `For Each` or a `Try`) but in both measured cases the structured
    visitor has already emitted an unconditional transfer, so what it writes is unreachable.
    Kept: that deadness is a property of the OTHER visitors, which nothing at this site can
    check; if it lapses, control falls into `loopExit:` and the loop ends after one iteration.
  ⚠ **Not fixed, out of family, each measured — added to the open list:**
  - **MSIL: `For i = 1 To n` with NO explicit `As Type` throws `InvalidProgramException`
    completely on its own**, no `For Each` involved. Root cause confirmed at source:
    `IRBuilder.Visit(ForLoopNode)` (`IRBuilder.cs:3155-3166`) adds the induction variable to
    `LocalVariables` ONLY inside `if (!string.IsNullOrEmpty(node.VariableType))` — the
    inline-declaration arm — so the inferred form never gets a slot.
    ⛔ **CORRECTED 2026-09-21 — BOTH HALVES OF THE NEXT SENTENCE WERE WRONG, see the counted-`For`
    entry below.** It read "that is defect (B) of this family, on the `IRFor` node". There is no
    `IRFor` node (a counted `For` lowers to ordinary blocks), and it was never MSIL-only: the one
    omission broke **all four** backends. Left here with its correction rather than deleted,
    because the wrong reading is what the next session would otherwise re-derive.
  - **C# backend: a property `Get` accessor emits NO local declarations AT ALL** (`CS0103`).
    ⛔ **CORRECTED 2026-09-21** — this read "never hoists locals declared inside a loop". The loop
    is irrelevant: measured at f20435d and again after the C#-backend Exit/Right batch, a `Get`
    whose whole body is `Dim sum As Integer = 5` / `Return sum + 1`, with no loop anywhere, is the
    same `CS0103`, while a `Get` that declares nothing (`Return 6`) compiles and prints correctly.
    `CSharpBackend.GenerateProperty` is the site.
  - **C# backend: an emitted `foreach` reuses the source loop-variable name even when it collides
    with an outer local** (`CS0136`).
  - `For Each` over a `Dictionary` — front-end (C# `CS0030` too), not MSIL's.
  - Two same-named `For Each` loops of DIFFERENT element types — the IR variable in the second
    body carries the FIRST loop's type. Shared-IR/analyzer defect; only MSIL can observe it
    because the text-emitting backends re-resolve the identifier. Deliberately not papered over
    in the backend.
  ⭐ **The top of the remaining MSIL worklist, so the next session starts here** — **FIXED
  2026-09-21, see the `IRIndexerStore` entry below**: **`l(0) = 42` on
  a List writes NOTHING and runs clean, printing the OLD value** — `MSILBackend` never overrides
  `Visit(IRIndexerStore)` and `ICodeGenerator`'s is a `virtual { }` no-op, the same hazard that
  lost `IRThrow`. `a(i) = v` is fine (`IRArrayStore` IS overridden) — only the collection indexer
  path is silent.
  **18 of 20 mutants killed, 2 kept as unreachable-with-recorded-reason, 0 build breaks.** The
  two new tests each killed exactly one mutant — the one written for it, no collateral.
  **Full suite in place: 195 / 6262 / 203 / 6660 against the master `7ce1200` baseline
  195 / 6222 / 203 / 6620** — +40 passed, +40 total, +0 failed; 195 reported = 195 anchored
  lines, 170 normalized failing names, `diff` clean; nothing new, nothing newly passing.
  Filtered `FullyQualifiedName~Msil` reference: `Failed: 0, Passed: 228`.

  ⚠ **INDEXED COLLECTION WRITES RUN ON MSIL as of 2026-09-21** — `MsilIndexerStoreTests`
  (18 cases), `MSILBackend.cs`: `Visit(IRIndexerStore)`. Before: `l(0) = 42` on a
  `List(Of Integer)` and `d("k") = 9` on a `Dictionary` **RAN CLEAN AND PRINTED THE OLD VALUE**.
  ⛔ **THE OVERRIDE DID NOT EXIST.** `CodeGeneratorBase.Visit(IRIndexerStore)`
  (`ICodeGenerator.cs:172`) is a `virtual { }`, so every indexed write emitted **nothing at
  all** — not the call, not the indices, not even the evaluation of the value. The emitted IL
  for `l.Add(1); l.Add(2); l(0) = 42` contained no `set_Item` and no `ldc.i4 42`; it went
  straight from the second `Add` to the `get_Item` of the read. A clean run with a wrong
  answer, which is the worst failure mode this backend has. On a `Dictionary` the dropped
  write surfaced later and louder: `KeyNotFoundException` on the next read of that key.
  `a(i) = v` was never affected — an array write is `IRArrayStore`, which is `abstract`.
  ⭐ **THE ENUMERATION, worth more than the fix.** This is the THIRD instruction lost to a base
  no-op (`IRThrow` was the identical shape — see the `Try`/`Catch` entry above, "the one that hid
  the rest"; and `JavaScriptBackend.cs:29` names the hazard by name), so the whole surface was
  counted rather than guessed:
  - `CodeGeneratorBase` declares **35 `abstract` `Visit` methods** (`ICodeGenerator.cs:128-162`)
    and **exactly TWO `virtual { }` ones** — `Visit(IRThrow)` (165) and `Visit(IRIndexerStore)`
    (172). 35 + 2 = 37 = the whole `IIRVisitor` surface (`IRNodes.cs:37-80`). **Every other
    visitor is `abstract`, so the compiler makes forgetting it impossible** — no third
    instruction can be lost this way until someone adds another `virtual { }`.
  - ⚠ **`IIRVisitor` itself carries the same two as DEFAULT INTERFACE METHODS** (`IRNodes.cs:75`
    and `:80`, both `{ }`). Two copies of the hazard, the same two nodes. A new node added with
    a default body is silently optional for every backend at once; add it `abstract`/undefaulted
    instead and the compiler names each backend that has not handled it.
  - MSIL overrode all 35 abstract + `IRThrow` (5025) and was missing **only** this one.
  - **Per backend, measured:** C++ overrides both (`CppCodeGenerator.cs:5015`, `:5149`); C# and
    JavaScript implement `IIRVisitor` DIRECTLY rather than deriving from `CodeGeneratorBase`,
    and both wrote the pair anyway (`CSharpBackend.cs:3466`/`:3663`,
    `JavaScriptBackend.cs:3312`/`:3409`); MSIL had `IRThrow` only, now has both.
  - ⛔ **`LLVMBackend` overrides NEITHER** — it derives from `CodeGeneratorBase`, so it inherits
    both no-ops and silently drops every `Throw` *and* every indexed collection write today.
    Same defect class, unfixed, never swept. On the open list below.
  - Not the same class: four empty `Visit(...) { }` bodies (`IRFunction`, `BasicBlock`,
    `IRConstant`, `IRVariable`) are deliberate — C++ (2294-2297), C# (3195-3198, as plain
    `public void`) and LLVM (1301-1304) all carry the identical four, because those nodes are
    driven or consumed by their parents. `Visit(IRAwait)`/`Visit(IRYield)` emit only a warning
    comment: degraded, but visible in the IL.
  **The lowering is operand order and one table lookup.** `set_Item` is an ordinary instance
  call, so IL wants receiver, then every index, then the value, and the signature comes from the
  RECEIVER's own type through `CollectionMembers` — `List`1<T>::set_Item(int32, !0)` indexes by
  an integer and takes a generic element, `Dictionary`2<K,V>::set_Item(!0, !1)` takes both from
  the instantiation. Spelling either as the other assembles and then dies at run time, which is
  why neither is inferred. The table already carried both rows; only the override was missing.
  **Insert-or-update falls out, it is not special-cased** — `Dictionary::set_Item` adds an absent
  key, which is what .NET means by `d(k) = v` and what C#, C++ and JS all do; `Add` would throw.
  ⭐ **A SURVIVOR CAUGHT A DEAD FIELD IN THE FIX ITSELF.** The emission first spelled the method
  name literally (`::set_Item(...)`) beside a table lookup, leaving `CollectionMember.Il` unread
  on that path. `i8-dict-row-calls-add` — Dictionary's row rewritten to call `Add`, which throws
  `ArgumentException` on an existing key instead of updating it — **SURVIVED the entire
  fixture**, because nothing consulted the field. Now `::{collSig.Il}(...)`; the mutant kills 3
  tests. ⚠ **The READ path (`Visit(IRIndexerAccess)`) still spells `get_Item` literally** — both
  rows happen to agree so it emits the same text today, but it carries the identical latent
  hazard. Left alone deliberately, to keep this diff to one family.
  ⚠ **ONE MUTANT SURVIVES AND THE CODE IS KEPT.** `i10-fallback-dropped` — the non-collection
  `IList`1` fallback. Measured one probe per way into it: `IList(Of T)`/`IReadOnlyList(Of T)` as
  a parameter or a field are **semantic errors** and never reach IR; a `.NET`-handle write takes
  the primary path; the only shape that reaches the arm is `Dim l As New List()` (no generic
  argument), and **ilasm already refuses that whole file** — `Reference to undefined class
  'List'` — because the receiver's own `.locals` entry is a bare `List`. Reachable, but by no
  program that can run. Kept because it is the exact mirror of the fallback the READ path has
  shipped with; dropping it on the write side alone would make one indexer's two halves
  disagree about which type they call on.
  ⚠ **DELIBERATELY NOT MUTATED:** `_currentStack -= 2 + Indices.Count`. `_currentStack` is
  **write-only across the whole backend** — filtered for non-mutating uses, `grep` returns
  exactly one line, the field declaration at `MSILBackend.cs:50`. `.maxstack` comes from a
  DIFFERENT field, `_maxStack = Math.Max(8, _localIndices.Count + _tempIndices.Count + 4)`
  (`MSILBackend.cs:1347`), which never reads `_currentStack`. A mutant there cannot change one
  byte of IL. The line stays for consistency with every other visitor — and ⚠ **`_currentStack`
  being dead means no visitor's stack arithmetic is checked by anything**; do not trust it as a
  verification mechanism.
  ⛔ **C# CANNOT BE THE ORACLE for a read-modify-write through an indexer** — `l(0) = l(1)`,
  `l(i) = l(i) * 10`, `d("a") = d("a") + 1`, and **any** indexer write inside a `For Each` body
  all give `CS0103: The name 'tN' does not exist in the current context`. Those cases assert
  against JavaScript or C++. Also `List(Of Boolean)` does not compile on **C++**
  (`std::vector<bool>`'s bit-reference will not bind to the generated `T&`). Both on the open
  list below.
  ⚠ **Writing a List while enumerating it now throws `InvalidOperationException: Collection was
  modified` on MSIL, matching C#** — correct .NET behaviour that the dropped write used to hide
  (it ran clean and printed stale values). JavaScript legitimately diverges, so that shape is
  not a four-backend pin.
  **10 of 11 mutants killed, 1 kept as unreachable-with-recorded-reason, 0 build breaks**
  (`i0` 18, `i1` 17, `i2` 17, `i3` 17, `i4` 16, `i5` 18, `i6` 18, `i7` 4, `i8` 4, `i9` 15;
  `i10-fallback-dropped` survives, declared above). Every mutant is LINE-anchored with a
  per-mutant assertion that the expected text is on that line, so a shifted line fails the run
  instead of silently mutating nothing.

  ⚠ **`For i = 1 To n` WITH NO EXPLICIT `As Type` RUNS as of 2026-09-21** —
  `CountedForVariableTests` (19 cases), `IRBuilder.cs`: `Visit(ForLoopNode)`,
  `ResolvesToExistingStorage`. **SHARED IR, read by all five backends.**
  ⛔ **IT WAS NEVER MSIL-ONLY.** The previous entry above filed this as "defect (B) of the
  `For Each` family, on the `IRFor` node". That framing was wrong in two ways: there is **no
  `IRFor` node** (a counted `For` lowers to ordinary blocks plus `IRAssignment`/`IRCompare`),
  and the omission broke **all four backends**, because every one of them writes its
  declarations from `IRFunction.LocalVariables`. One omission, four symptoms, all measured
  compiled-and-run:
  - **C#** — `CS0103: The name 'i' does not exist in the current context` (×5)
  - **C++** — `error: use of undeclared identifier 'i'` at `i = 1;`
  - **JavaScript** — `ReferenceError: i is not defined` at `i = 1;`
  - **MSIL** — `InvalidProgramException` (`// WARNING: Unknown local 'i'` in the IL)
  The registration lived INSIDE the `if (!string.IsNullOrEmpty(node.VariableType))` arm, so
  `For i As Integer = 1 To n` worked and the ordinary VB spelling did not. The ONE inferred-form
  shape that worked anywhere was `Dim i As Integer = 100` followed by `For i = 1 To 3` — the
  discriminator, because the name already had storage.
  ⚠ **NOT the `For Each` situation.** There `IRBuilder` deliberately withholds the element
  variable because `foreach`/`for(:)` declares it in the target language. A counted `For` has no
  such construct; each backend emits a bare assignment.
  ⛔ **WE INTRODUCED A REGRESSION HERE AND CAUGHT IT BEFORE COMMIT. READ THIS BEFORE TOUCHING
  THE GUARD.** The first fix put BOTH spellings behind one storage-resolving guard and then —
  on the strength of an overlap mutant — deleted the guard's module-global arms as "provably
  redundant". Measured on three builds, all four backends:

  | shape | PRE-FIX `23666d6` | REGRESSED | NOW |
  |---|---|---|---|
  | module global, loop in a DIFFERENT `Sub` | **`4`** | **`0`** | **`4`** |
  | module global, read back in BOTH `Sub`s | **`4 / 4`** | **`4 / 0`** | **`4 / 4`** |
  | module global assigned before the loop | **`4`** | **`0`** | **`4`** |
  | module global, loop in the SAME `Sub` | `4` | `4` | `4` |
  | class FIELD (control) | `4` | `4` | `4` |
  | **explicit** `For g As Integer`, other `Sub` | `0` | `0` | `0` |

  `_variableVersions` only holds a version for a name in the function that has already touched
  it — not in every function that can reach a module global through `_moduleGlobals`. So `Bump`
  saw "no storage", registered a local, and every backend's declaration of that local shadowed
  the global for the rest of `Bump`. A plain non-loop `g = 5` from another `Sub` was unaffected;
  it was specific to the counted-`For` registration path.
  ⭐ **WHY THE PROBE COULD NOT SEE IT, and the rule that came out.** The probe that "proved" the
  arms redundant put the loop and the read-back in the **same function** — where the spurious
  shadowing local happens to hold the right value when the loop ends, so the read returns `4`
  and the shadowing is invisible. Only a read from a **different** function observes it. The
  overlap mutant therefore ran against a fixture in which **no shape COULD kill those arms**:
  **a mutant no shape can kill is UNTESTED, not redundant.** The question is whether a shape
  exists that would observe the difference, not whether the current fixture contains one. Found
  by test-writer reading the code, not by any run.
  ⚠ **THE TWO SPELLINGS ARE DIFFERENT STATEMENTS AND MUST NOT SHARE A GUARD.**
  `For i As Integer = 1 To 3` **declares** `i` — it introduces a loop-scoped variable that
  SHADOWS a same-named field or module global, which is VB's rule and what all four backends
  already did (measured `0`, correctly). `For i = 1 To 3` declares nothing and drives whatever
  `i` already denotes (measured `4`). ⛔ **Fixing only the global arm would have flipped the
  explicit form from `0` to `4` — a new regression in the opposite direction, and every test
  would still have passed.** The fix is two changes: restore the asymmetry (explicit always
  declares, with its own self-dedupe; inferred resolves first), and restore the module-global
  arm keyed `ModuleGlobalKey(_currentModuleName ?? _module?.Name, name)` **exactly as
  `GetOrCreateVariable` keys it** (`IRBuilder.cs:479`) — the guard and that resolver must agree
  about what a bare name denotes.
  ⚠ **`f13-explicit-shares-inferred-guard` — which collapses the two arms back into one guard,
  i.e. reproduces the regression — SURVIVED all 18 of the other tests.** Measured twice: against
  the 18-test fixture it survived outright; with
  `ExplicitlyTypedCountedFor_OverAModuleGlobalsName_DeclaresAShadow_NotThePair` added it fails
  exactly that one test and nothing else. That test exists solely to kill it. **Do not remove it**
  — without it the two arms can be collapsed again and every test still passes.
  ⚠ **The guard's arms are deliberately NOT minimal.** `f9` (module-global) and `f10`
  (bare-global) each survive ALONE because the other covers them; disabling both
  (`f15-no-global-arms-at-all`) kills `InferredCountedFor_OverAModuleGlobal_...` on all three
  of its legs with `But was: "0"` — **the regression's exact signature**. ⛔ Note what that
  means: while the pin still asserted the regressed `0`, `f15` SURVIVED, i.e. the mutation
  that REPRODUCES the regression was blessed by a green fixture. The flip to `4` is what makes
  the pair killable at all. The two arms mirror the two lookups `GetOrCreateVariable` performs
  in order and are kept as a pair. Same story one arm up: `f5` (declared-local) survives alone,
  `f7` (live-SSA-version) kills 1 alone, and `f11` (BOTH off) kills 3 — the extra two,
  `CountedFor_OverAnExistingLocal_KeepsOneSlot` and `TwoInferredLoopsSharingAName_DoNotDoubleRegister`,
  die only when neither arm is present. **Each survivor is half of a load-bearing pair, proved by
  the pair-mutant, not asserted.** Do not "simplify" any of them away on the strength of a single
  green mutant — that is precisely the reasoning that produced the regression above.
  ⛔ **`Exit For` in an `If … End If` is NOT a no-op on a counted `For`** — measured `6` on C#,
  C++, JS and MSIL alike. ⛔ **CORRECTED 2026-09-21 — this used to say "`Exit For` is NOT a no-op
  on a counted `For`" without qualification, and that was too broad.** The `If` shape worked; the
  SAME `Exit For` written as the body's LAST statement made the emitted C# **loop forever** (the
  `break` was dropped and the loop's `.inc` block is unreachable, so the optimizer deletes it),
  and inside a `Select Case` arm it totalled **7** instead of 3 (a C# `break` leaves the SWITCH).
  Both fixed in the C#-backend Exit/Right batch and pinned in `CSharpLoopExitTests`.
  ⚠ **A counted loop in a property `Get` accessor still does not compile on C#** — but NOT
  because of the loop: `GenerateProperty` emits no local declarations at all (see the corrected
  open-list entry above). MSIL and JS both give the right answer, so that shape is pinned against
  them.
  ⚠ **Pre-existing, pinned, NOT ours:** `For i = 1 To n` where `i` is a **PARAMETER** throws
  `InvalidProgramException` on MSIL. Verified identical against the pre-change build; C# and
  JavaScript both give the right answer, so that case asserts against those two.
  ⚠ **Not fixed, out of these two families, each measured compiled-and-run — added to the open
  list:**
  - ⛔ **`LLVMBackend` overrides NEITHER base no-op visitor** — not `Visit(IRThrow)`, not
    `Visit(IRIndexerStore)`. It silently drops every `Throw` and every indexed collection write
    today, the same clean-run-wrong-answer failure mode MSIL had. Never swept; LLVM is still
    out of scope, so this is filed, not fixed.
  - **`g.Items(0) = 42` — an indexer write through a member access — is broken on ALL FOUR
    backends**, not just MSIL: it never reaches `IRIndexerStore`. Front end, upstream of every
    emitter. `Dim l = g.Items` then `l(0) = 42` works everywhere, which is the discriminator.
  - **MSIL: `For i = 1 To n` where `i` is a PARAMETER throws `InvalidProgramException`.**
    Pre-existing — verified identical against the pre-change build — and pinned in
    `CountedForVariableTests` against C# and JavaScript, which both answer correctly.
  - **C# backend: a READ-MODIFY-WRITE through an indexer fails** with
    `CS0103: The name 'tN' does not exist in the current context` — `l(0) = l(1)`,
    `l(i) = l(i) * 10`, `d("a") = d("a") + 1`. C# is not a valid oracle for those three shapes;
    they assert against JavaScript.
    ⛔ **CORRECTED 2026-09-21 — this read "ANY indexer write inside a `For Each` body fails", and
    that was an over-generalization from the read-modify-write cases.** The loop is incidental:
    measured at f20435d and again after the C#-backend Exit/Right batch, a plain `l(0) = n` inside
    a `For Each` over a DIFFERENT collection compiles and prints **8** on C#, while `l(0) = l(1)`
    with no loop anywhere is `CS0103`. `MsilIndexerStoreTests.Write_InsideAForEach_OverADifferentCollection`
    has been promoted to `MsilAgreesWithCSharp` accordingly.
  - **C++ backend: `List(Of Boolean)` does not compile** — `std::vector<bool>`'s proxy
    bit-reference will not bind to the `T&` the generated code takes. Element-type-specific;
    `List(Of Integer)` / `String` / a user class are all fine.
  - **Parser: `Public Items As New List(Of Integer)()` does not parse as a FIELD declaration.**
    The inline collection initializer is accepted on a `Dim` inside a method but not on a class
    member; the field has to be declared and then assigned in the constructor.
  - **`Catch ex As System.Exception` fails on JavaScript and MSIL** while the bare
    `Catch ex As Exception` works on both. The dotted BCL spelling is not resolved on the catch
    clause path.
  **10 of 13 mutants killed, 3 survive ALONE and each is killed by its pair-mutant, 0 build
  breaks** — `f1` 14, `f2` 14, `f3` 5, `f4` 17, `f13` 1 (all three legs), `f14` 2, `f7` 1,
  `f8` 1, `f15` 1 (all three legs), `f11` 3; survivors `f5`, `f9`, `f10`. ⚠ **A sweep script
  that classifies a mutant by `grep "error CS"` is WRONG here** — a fixture with a C#-backend
  leg prints `error CS…` inside the TEST OUTPUT whenever a mutant correctly breaks the EMITTED
  C#, and that heuristic reported four genuine kills as build breaks. A real test-project build
  failure emits no run-summary line at all, so test for the ABSENCE of `^(Failed|Passed)!`.
  **Full suite in place for BOTH families: 195 / 6299 / 203 / 6697 against the `23666d6`
  baseline 195 / 6262 / 203 / 6660** — +37 passed, +37 total, +0 failed, +0 skipped, which is
  exactly the two new fixtures (18 + 19) and nothing else. 195 reported = 195 anchored
  `^  Failed ` lines; 170 normalized failing names, `diff` against the baseline list clean —
  nothing new, nothing newly passing.

  ⚠ **THE VB STRING INTRINSICS RUN ON MSIL as of 2026-09-21** — `MsilStringIntrinsicTests`
  (54 cases), `MSILBackend.cs`: `TryEmitStdLibCall` arms at `4456-4553`, `_stdLibResultSpec`
  (`4192-4211`, consumed `3735-3742`), `RequireChrArgument`/`RequireAscArgument`
  (`4213-4281`). Before: `Mid`, `Left`, `Right`, `UCase`, `LCase`, `Trim`, `Replace`, `InStr`,
  `Chr` and `Asc` each died at RUN time with e.g.
  `MissingMethodException: Method not found: 'System.String MsilProbe.Mid(System.String, Int32, Int32)'`
  — **naming the MODULE class**. They fell out of `TryEmitStdLibCall`'s switch, and
  `Visit(IRCall)` then reached the emit-a-call-on-the-current-class default
  (`MSILBackend.cs:3799`, `call {ret} {_moduleName ?? "Program"}::{name}(...)`): the phantom
  self-call already recorded for `Console.WriteLine`. **ilasm accepts a MemberRef with no
  definition**, so every one of them assembled cleanly and failed only when run.
  ⭐ **`Len` WORKED, and that was the discriminator** — it has a `case "len"` arm emitting
  `callvirt instance int32 [mscorlib]System.String::get_Length()`. The mechanism existed and
  the table was incomplete, so the ten new arms copy it exactly rather than inventing a second
  path. `BasicLang/StdLib/MSILStdLib.cs` is NOT that path (see below).
  ⛔ **THE SEMANTIC FINDING, worth more than the arms. THERE IS NO SINGLE "BasicLang
  semantics" FOR THESE FUNCTIONS.** Measured on all four backends, compiled and run, before
  writing one line of emitter:
  - The **C# backend's emissions are raw BCL with NO clamping** — `EmitMid` is
    `str.Substring(start - 1, length)`, `EmitLeft` is `str.Substring(0, length)`
    (`BasicLang/StdLib/CSharpStdLib.cs:352-364`) — so C# **THROWS** where real VB clamps:
    `Mid("abcdef",5,10)`, `Mid("abc",5,2)`, `Mid("",1,1)`, `Mid("abc",0,2)`,
    `Left("abcdef",10)`, `Left("",1)`, `Left("abc",-1)`, `Right("abcdef",10)`, `Right("",1)`
    are all `ArgumentOutOfRangeException`; `Replace("banana","","o")` is `ArgumentException`;
    `Asc("")` is `IndexOutOfRangeException`.
  - **JavaScript CLAMPS** every one of them (`[ef]`, `[]`, `[]`, `[c]`, `[abcdef]`, `[]`, `[]`,
    `[abcdef]`, `[]`, `[boaonoaonoa]`, `NaN`).
  - ⚠ **`BasicLang.Runtime.BasicLangRuntime.Mid`/`Left`/`Right` DO clamp — and are DEAD CODE
    that no backend calls.** `BasicLangRuntime.cs:19-65`. Do not read it as the specification;
    grep for a caller before believing any of it.
  So the choice was never "VB or not"; it was "match the other .NET backend, or invent a third
  answer for MSIL alone". **MSIL now emits the IL the C# backend's output compiles to,
  instruction for instruction, including the throwing inputs — same exception type, same
  parameter name** — on the precedent the `cint` arm already set in this file (C# and MSIL
  diverged on `CInt(7.5)`; MSIL was changed to match C#).
  ⛔ **THE CLAMPING QUESTION IS A FIVE-BACKEND SEMANTIC AND IS DELIBERATELY LEFT UNDECIDED.**
  Nothing here settles it. Changing to clamping later touches five backends and every pinned
  shape; that is an architect's call, not an emitter's. ⛔ Do **not** "fix" one intrinsic into
  clamping on its own — a clamp on MSIL alone turns an exception the two .NET backends agree
  on into a silent different answer on one of them, which is strictly worse than the gap.
  ⭐ **`Chr` AND `Asc` ARE ABSENT FROM `SemanticAnalyzer.RegisterStdLibFunctions`**
  (`SemanticAnalyzer.cs:1130-1210` has rows for the other nine and none for these two). Two
  consequences, both load-bearing:
  - Their results are typed **Object**, so `Asc`'s `int32` lands in an `object` slot. Bridged
    with `_stdLibResultSpec` + the existing `NeedsBoxingInto`, exactly as the .NET-static arm
    does. The spec is RESET at the top of every `TryEmitStdLibCall` — without the reset it
    leaks into the next call and boxes a `string` as an `Int32`.
  - ⛔ **Their ARGUMENTS are type-checked by nothing.** Measured on the CLI with the arms in
    and the guards out: **`Chr("x")` ran clean and printed `Ԙ`**; **`Chr(Asc("A"))` printed
    `鍀`** (the argument arrives boxed, and `conv.u2` narrows the box pointer); **`Asc(5)`**
    emitted `ldc.i4.5; ldc.i4.0; callvirt String::get_Chars` and died with
    NullReferenceException. **Shipping the arms unguarded would have converted a LOUD FAILURE
    (MissingMethodException) into a SILENT WRONG ANSWER** — a regression dressed as a feature,
    and the worst failure mode this backend has. `RequireChrArgument`/`RequireAscArgument`
    refuse them with a named diagnostic. ⚠ `Asc` still accepts an **Object**-typed argument and
    must: `Asc(Chr(66))` answers `66`, because the `callvirt` dispatches on the real string.
  ⚠ **`Right` uses `dup`, and that WAS a deliberate divergence from the C# backend.**
  `CSharpStdLib.EmitRight` interpolated `{str}` twice, so the receiver EXPRESSION was evaluated
  twice: measured on `Right(Tag(), 2)` where `Tag` prints, **C# printed `tag` TWICE** while
  JavaScript, C++ and MSIL printed it once. ⭐ **FIXED (C#-backend Exit/Right batch, 2026-09-21)** —
  `EmitRight` now emits `({str})[^({length})..]`, one evaluation, and
  `MsilStringIntrinsicTests.RightWithAnEffectfulReceiver_IsEvaluatedOnce` has been promoted to
  `MsilAgreesWithCSharp`. There are now NO deliberate divergences in that fixture.
  ⚠ **`BasicLang/StdLib/MSILStdLib.cs` IS DEAD CODE.** `MSILStdLibProvider` is registered in
  `StdLibRegistry.cs:36` and **referenced by nothing** — `MSILBackend.cs` has zero hits for it.
  It is also wrong where it is most tempting to reuse: its `EmitMid` emits
  `"ldc.i4.1\nsub\n…Substring(int32,int32)"`, which with `(str, start, length)` on the stack
  subtracts 1 from the **LENGTH**, not the start; `EmitRight` is a comment saying it "needs
  stack manipulation". Two tables claiming to be the MSIL stdlib is exactly the drift hazard
  `CollectionMembers`/`ExceptionMembers` are narrow to avoid. Delete it or wire it — do not
  quietly copy from it. On the open list.
  ⚠ **C++ IS NOT A USABLE ORACLE for this family outside `Replace`.** `CppCodeGenerator.cs:3180-3186`
  calls `.substr`/`.find` directly on `{args[0]}`, so a **string-literal receiver** is a bare
  `const char*` with no such member and the program does not compile at all. `Replace` goes
  through a lambda taking `string` and does compile.
  ⚠ **`Mid` has no two-argument form** — registered with exactly three parameters, so
  `Mid(s, 3)` is `Function 'Mid' expects 3 argument(s), got 2` on every backend and never
  reaches an emitter. Pinned as the refusal.
  ⚠ **`Right(s, n)` with n = half the string length is a DEGENERATE shape** — with a
  6-character string, a correct `Right(s, 3)` and a `Substring(3)` that forgot the length
  subtraction both answer `def`. Use `n = 2`. Cost a mutant that should have died.
  **27 of 27 mutants killed against the committed fixture, 0 survivors, 0 build breaks.**
  ⭐ **`g1-chr-no-conv` SURVIVED the first sweep and is UNTESTED, not equivalent.** Dropping
  the `conv.u2` in front of `call string Char::ToString(char)` changes nothing for any INTEGER
  argument — on the CLR stack `char` IS `int32`, so the call verifies and the ABI truncates
  either way; `Chr(65)`, `Chr(65601)`, `Chr(0)`, `Chr(931)` and `Chr(-191)` are all identical
  with and without it. It is what makes the call legal IL for a **non-integer** argument, which
  nothing upstream rejects: `ChrOfADouble_NeedsTheNarrowingConversion` (`Dim d As Double = 65.0`
  then `Chr(d)`) answers `[A]` and gives **InvalidProgramException** under the mutant. That one
  test is the only thing in 54 that kills it — **do not remove it**, and note the rule it
  illustrates again: *a mutant no shape can kill is UNTESTED, not redundant; the question is
  whether a shape EXISTS.* `Len(Chr(-1))` is not that shape — the FRONT END rejects it
  (`cannot convert from 'Object' to 'String'`), because `Chr` is typed Object and `Len` takes
  String.

  ⚠ **`s.Length` RUNS ON MSIL as of 2026-09-21** — `MsilStringPropertyTests` (21 cases),
  `MSILBackend.cs`: `StringMembers` (`2566-2582`), `TryStringMember` (`2584-2610`), and the arm
  in `Visit(IRFieldAccess)` (`5619-5643`). Before: `s.Length` on a String emitted
  `ldfld int32 [mscorlib]System.String::'Length'` — a .NET **PROPERTY** read lowered to a field
  load — which assembled (ilasm does not resolve member references) and died with
  `MissingFieldException: Field not found: 'System.String.Length'`. Now
  `callvirt instance int32 [mscorlib]System.String::get_Length()`.
  ⭐ **THE METHOD PATH BESIDE IT WAS NEVER BROKEN, and that is what made this hard to see.**
  `s.ToUpper()` and `s.Substring(1, 3)` both ran before and after — `Visit(IRInstanceMethodCall)`
  renders the receiver through `IlReceiverToken` and emits a real `callvirt`. Only a member
  reaching `Visit(IRFieldAccess)` was broken, and `Length` is String's only property, so the
  break was total for the one member everybody uses and invisible everywhere else.
  ⭐ **`List.Count` and `Dictionary.Count` SHARE THE SITE; `Array.Length` IS A DIFFERENT PATH
  ENTIRELY.** Measured, all correct before and after: Count reaches `TryCollectionMember` two
  arms above the `ldfld` and emits `callvirt … List`1<int32>::get_Count()`; **`a.Length` is the
  dedicated `ldlen` + `conv.i4` opcode pair**, not an accessor call at all. They are CONTROLS in
  the fixture, not siblings — and they are what kills `s5-receiver-test-removed`, the mutant
  where the String arm claims every receiver. `HashSet.Count` is unobservable: `h.Add(1)` is
  refused first by `TryCollectionMember`.
  ⛔ **An unrecorded String member is REFUSED, not emitted as an `ldfld`, and that is stronger
  than the `CollectionMembers`/`ExceptionMembers` convention on purpose:** `System.String` has
  **no public instance fields at all**, so a field load on a string receiver cannot be right
  whatever it names. Verified this breaks nothing that worked — on the parent tree `s.Foo`,
  `s.Chars`, `s.Empty`, `s.ToUpper` (no parentheses) and `s.Trim` (no parentheses) all gave
  `MissingFieldException` at RUN time; they now give a named `GenerateFailed`. ⚠ A
  parenthesis-free String METHOD name reaches this arm too, so the refusal fires for it.
  ⛔ **THE INTERFACE-PROPERTY READ IS STILL BROKEN and was deliberately NOT widened to.**
  `h.Slot` on an `IHolder` still gives `MissingFieldException: Field not found: 'IHolder.Slot'`:
  `TryResolveProperty` (`MSILBackend.cs:3104`) resolves only through `TryFindClass`, so an
  interface receiver misses every arm. **It needs an interface-property resolver alongside the
  existing `DeclaredInterfaceMethod` — a different lookup, not a table row**, which is why the
  String fix does not reach it. ⚠ **C# cannot be its oracle either**: it emits an accessor-less
  interface property and does not compile (**CS0548** + CS0200). JavaScript answers `5`. On the
  open list.
  **9 of 10 mutants killed against the committed fixture; 1 survivor, declared EQUIVALENT.**
  `s7-unknown-member-falls-through` survived the scratch sweep and dies against the committed
  fixture on all five refusal shapes — it was UNTESTED, not dead. ⚠ **`s8-call-not-callvirt`
  SURVIVES and the `callvirt` is KEPT.** Unlike `g1` above the candidate space here is CLOSED,
  not merely unexplored: `System.String` is sealed and `get_Length` is not virtual (nothing for
  `callvirt` to find), ilasm accepts both, and the only receiver value where the two opcodes'
  definitions differ — null — was measured under both and gives **byte-identical**
  `NullReferenceException`. Kept because every other accessor emission in this file spells it
  `callvirt` (`EmitPropertyGet`, the collection-member arm, the exception-member arm) and
  `callvirt`'s null check is guaranteed by the CLI spec where `call`'s fault is a JIT
  implementation detail. **Do not "simplify" it to `call`.**
  ⚠ **Not fixed, out of these two families, each measured compiled-and-run — added to the open
  list:**
  - ⭐ **FIXED 2026-09-21 (C#-backend Exit/Right batch): `Right` EVALUATED ITS RECEIVER TWICE.**
    `EmitRight` interpolated `{str}` twice and parenthesized neither operand, so besides the
    double evaluation `Right(Ab() & "cdef", 2)` did not COMPILE (`CS0019`) and
    `Right("abcdef", Len(Ab()) + 1)` printed `[f]` instead of `[def]`. Now
    `({str})[^({length})..]`; pinned in `CSharpRightReceiverTests`. ⚠ A folded receiver
    (`Right("ab" & "cdef", 2)`) cannot see any of this — the optimizer collapses it to a literal.
  - ⛔ **C# backend: an interface property emits an accessor-less property** —
    `CS0548: 'IHolder.Slot': property or indexer must have at least one accessor`, plus CS0200.
    The program does not compile, so C# is not a valid oracle for any interface-property shape.
  - **C# backend: `Dim s As String` with no initializer then `s.Length` prints `0`** — the local
    is initialised to `""`. MSIL gives `NullReferenceException`; every other backend agrees with
    MSIL that the local is null.
  - **C++ backend: `Mid`/`Left`/`Right`/`InStr` with a STRING-LITERAL receiver do not compile** —
    `.substr`/`.find` called on a bare `const char*` (`CppCodeGenerator.cs:3180-3186`).
    `Replace` is fine; it goes through a lambda taking `string`.
  - ⛔ **C++ backend: `Replace(s, "", "o")` HANGS** — measured **exit 137, killed**. The replace
    lambda's `pos += to.length()` never escapes an empty needle. C# throws `ArgumentException`
    and JavaScript returns a string; C++ loops forever.
  - **JavaScript backend: `s.ToUpper()` — a .NET method on a String — is
    `TypeError: s.ToUpper is not a function`.** The `.NET`-method-on-a-primitive path is not
    lowered; the `UCase(s)` intrinsic spelling works.
  - ⛔ **Front end: `Chr` and `Asc` are not registered in
    `SemanticAnalyzer.RegisterStdLibFunctions`** — results typed `Object` and arguments
    unchecked, **on all five backends**. Registering them is the proper fix for both the boxing
    bridge and the argument guards added here; it changes what C#, C++, JavaScript and LLVM
    emit, so it was not done from a backend.
  - ⚠ **`BasicLang/StdLib/MSILStdLib.cs` is dead code** registered in `StdLibRegistry.cs:36`
    and referenced by nothing, with a wrong `EmitMid`. Delete or wire.
  - **MSIL: an INTERFACE property read is still `MissingFieldException`**, needing an
    interface-property resolver rather than a table row (above).
  **Full suite in place for BOTH families: 195 / 6374 / 203 / 6772 against the `2608272`
  baseline 195 / 6299 / 203 / 6697** — +75 passed, +75 total, +0 failed, +0 skipped, which is
  exactly the two new fixtures (54 + 21) and nothing else. 195 reported = 195 anchored
  `^  Failed ` lines; 170 normalized failing names, `diff` against the baseline list clean.
  Filtered `FullyQualifiedName~Msil` reference: `Failed: 0, Passed: 322`; the two new fixtures
  alone `Failed: 0, Passed: 75`. **Both entry points exercised** — `MsilHarness` (optimizer on)
  and the CLI (`--target=msil` → ilasm → `dotnet`), which funnel through
  `MSILCodeGenerator.Generate(irModule)` at `Program.cs:1317` and `Program.cs:4017`.

  ⚠ **ByRef RUNS ON MSIL as of 2026-09-21 — and so does writing ANY parameter** —
  `MsilByRefTests` (28 cases) and `MsilParameterWriteTests` (7 cases). `MSILBackend.cs`:
  `_byRefParams` / `_byRefStoreScratch` (`202-212`), `RegisterByRefParameters` (`1816`),
  `AllocateByRefStoreScratch` (`1833`), `ParamSpec` (`1874`) and the five sites that spell a
  signature through it (`331`, `1413`, `1585`, `2293`, `3419`), the `ldind` on a ByRef read
  (`2948-2951`), `ByRefTarget` / `TryResolveByRefTarget` / `EmitByRefTarget` (`3014-3157`),
  `EmitByRefArgument` + `RequireByRefSpec` (`3159-3234`), `EmitCallArguments` (`3236-3260`,
  called from all three call arms — `3410`, `4259`, `5824`), the parameter arm of
  `EmitStoreLocal` (`3635-3690`) and `EmitStarg` (`3790`).
  ⭐ **THE WORKLIST ROW NAMED THE SYMPTOM, NOT THE DEFECT, AND THAT COST THE WHOLE
  DIAGNOSIS.** "Any ByRef call → InvalidProgramException" is true and is not what was broken.
  `EmitStoreLocal` had **no `starg` arm at all** and never consulted `_paramIndices`: it walked
  locals → instance fields → properties → `Shared` fields → module globals and fell off the
  end, so a store to ANY parameter emitted `// WARNING: Cannot store to 'n'` and left the
  computed value on the evaluation stack for `ret` to reject. **The minimal discriminator has
  no ByRef and no loop in it**: `Sub Bump(n As Integer) : n = n + 1 : PrintLine(CStr(n))` threw
  the same `InvalidProgramException`. A parameter that is only READ always worked, ByRef or not
  — `Sub Show(ByRef n As Integer) : PrintLine(CStr(n))` printed `41` before the fix. **Only a
  WRITE failed**, which is why two families that looked unrelated are one.
  ⭐ **`For <parameter> = 1 To n` WAS THE SAME DEFECT** — `CountedForVariableTests`'
  `CountedFor_OverAParameter_…MsilIsAPinnedPreexistingGap` and `ModuleProcedureCallTests`'
  `ByRef_ThroughAQualifiedCall_IsMarked` were BOTH pinning it from different directions, and
  both are now promoted (to `"4"` on every backend, and to `"5"` on MSIL). The identical
  `// WARNING: Cannot store to 'n'` marker appeared in both families' IL — twice in the counted
  `For` (loop init and loop increment), once in `n = n + 1`. Adding the parameter arm closes
  the whole `MsilParameterWriteTests` group on its own; ByRef's own half is a **second,
  separable** defect that is not even reachable until stores can be emitted.
  ⛔ **THE STORE MUST WALK THE LADDER THE LOAD WALKS, IN THE SAME ORDER.** `EmitLoadLocal`
  resolves a parameter immediately after a local, so the new arm goes there too — putting it
  below the field/global arms (where it naturally wants to go) makes `N = N + 1` READ the
  argument and WRITE the module global. Pinned from both sides by
  `AByValParameter_ShadowsAModuleGlobal_ForTheStoreAsWellAsTheLoad` and its instance-field
  twin, which every backend answers `6 / 100`.
  ⚠ **ByRef's own half**: `&` in the signature, `ldind.<w>` to read, `stind.<w>` to write
  (`GetIndirectSuffix`, the same table `IRLoad` already used), and the **ADDRESS** at the call
  site. `stind` wants the address UNDER the value and this backend arrives with the value on
  the stack, so a ByRef parameter that is WRITTEN gets a scratch slot and the park-and-re-push
  `_fieldStoreScratch` already existed for — IL has no swap. A ByRef parameter that is only
  read gets no slot, so such a program emits not a byte more than before.
  ⛔ **THE ADDRESS IS A DIFFERENT OPCODE PER ARGUMENT KIND, and one of them is a trap.** A
  local → `ldloca`; the caller's own ByVal parameter → `ldarga`; an instance field → `ldarg.0`
  + `ldflda`; a `Shared` field or module global → `ldsflda`; an array element → the `ldelema`
  pointer the IR **already parks in a temp** beside the load (`a(0)` lowers to
  `IRGetElementPtr` → `stloc int32&` then `IRLoad` → `ldind.i4`; passing the loaded copy
  instead compiles, runs, and writes into the temporary). ⛔ **A ByRef parameter passed ON to
  another ByRef call is a bare `ldarg`, NOT `ldarga`** — the slot already holds the caller's
  pointer, and `ldarga` hands the callee a pointer to THIS frame's argument slot: it
  **assembles, runs, and writes one level short**, leaving the original variable untouched.
  That is the silent-wrong-answer mutant of this family; `CallersByRefParameterArgument_
  StaysABareLdarg_NestedOnce` and its recursive twin are the only two shapes that catch it.
  ⚠ **WHAT HAS NO ADDRESS IS REFUSED, LOUDLY, NOT PASSED BY VALUE** — a literal, an
  expression's temporary, and a property (both spellings: `h.X` arrives as a temp from
  `callvirt get_X()`, a bare in-class `X` as a name that resolves to an accessor call, and they
  reach two different arms). The alternative in every case ASSEMBLES AND RUNS and quietly drops
  the write-back, which is strictly worse than not compiling. A TYPE MISMATCH is refused for
  the same reason: a managed pointer cannot be converted, so `ByRef n As Double` given an
  Integer would mean writing the callee's change into a temporary of the parameter's type —
  C# rejects that program too (**CS1503**), and so does C++.
  ⚠ **⭐ THE ONE WORTH AN ADR: REAL VB PERMITS `Bump(41)`**, by creating a temporary and
  throwing the write away. This sides with the C# backend, which refuses it (**CS1510**),
  against C++, which accepts it (`Bump(41)` runs, `Bump(v + 1)` prints `41`). Reversing the
  decision means implementing copy-in/copy-out — which is also exactly what a PROPERTY argument
  would need — so it is one decision, not four. `docs/superpowers/decisions/` is still empty
  apart from the template and the architect role has never run, so this was decided from the
  backend and is recorded here rather than resolved.
  ⛔ **THE DECLARATION DECIDES WHICH ARGUMENT BECOMES AN ADDRESS — NOT `IRCall.ByRefArguments`,
  and the difference is measurable.** `EmitCallArguments` reads the same list `DeclaredParamList`
  spells the signature from, with the same "is there one at all" test. Keying it on the IR's
  call-site marker instead looks equivalent and is not: for `Util.Bump(v)` against a
  `Public Shared Sub Bump(ByRef n As Integer)` the front end records **no** by-ref marker, so
  the signature came out `(int32&)` from the declaration while the argument came out an `int32`
  from the call site — an invalid program. One source for both halves is the only arrangement
  in which they cannot drift.
  ⛔ **`Optional ByRef` is no longer "no `&` at all" on MSIL** (the note further down this file
  is corrected in place): the declaration now emits `void 'Bump'(int32& 'n')` and
  `Bump(v)` with a supplied argument RUNS and prints `42`. **`Bump()` with the argument OMITTED
  is a named refusal** — `AppendOmittedOptionalArguments` fills a LITERAL, and a literal has no
  address. That is the correct answer for the shape, not a gap: the fill cannot manufacture
  caller storage. The DECLARATION side still needs fixing on C# (`ref int n = 5`, CS1741).
  ⛔ **NOT FIXED — A SHARED FRONT-END GAP, AND A SILENT WRONG ANSWER ON TWO BACKENDS TODAY.**
  A ByRef parameter on a **CONSTRUCTOR** never reaches any backend:
  `IRBuilder.Visit(ConstructorNode)` (`IRBuilder.cs:1885`) builds each ctor parameter as
  `new IRVariable(param.Name, paramType) { IsParameter = true }` and **never copies
  `IsByRef`**, unlike every other parameter site in that file (`711`, `790`, `1637`, `1816`,
  `1862`, `2850`). Measured: `Public Sub New(ByRef n As Integer)` emits
  `instance void .ctor(int32 'n')` on MSIL and `H(int32_t n)` on C++, and BOTH print `41 / 42`
  where `42 / 42` is correct. Deliberately not fixed from a backend — it moves C#, C++,
  JavaScript and LLVM together. Pinned as the shared gap it is in
  `MsilByRefTests.ConstructorByRefParameter_IsAPinnedSharedFrontEndGap_NotThisFamilys`.
  **28 of 31 mutants killed against the committed fixtures, 0 build breaks; three survivors,
  each resolved rather than accepted.** (31 of 31 against the implementer's own kill probe;
  probe counts do not carry over, which is why the re-sweep is the number that matters.)
  ⭐ **`c2-one-scratch-slot-for-every-parameter` took FIVE candidate shapes to kill, and two
  ByRef parameters is not one of them.** Giving every ByRef parameter the same scratch NAME
  leaves the emitted IL byte-identical apart from a duplicated name in `.locals init` — the
  indices still come out distinct, because the overwrite does not advance `_localIndices.Count`.
  What breaks is the accounting for whatever is allocated NEXT: a plain swap, Integer+Double
  and Integer+String all pass, and it takes two written ByRef parameters **plus a TEMPORARY in
  the same method** for `AllocateTemporaries` to hand a temp the same index —
  `ilasm: Local var slot 1: type conflict`. `TwoWrittenByRefParametersPlusATemporary_
  DoNotCollideOnALocalsSlot` is the only test in 35 that kills it; **do not remove it**.
  ⭐ **`ByRefLong_UsesTheI8IndirectSuffix`'s ARITHMETIC IS DELIBERATELY BOUNDARY-CROSSING — do
  not "simplify" it back to `n = n + 1`.** ⛔ **It reads `Dim v As Long = 9000000` /
  `n = n * 1000000` / `9000000000000` because that is the ONLY thing in the fixture that pins
  the `stind` WIDTH deterministically.** Hardcoding the write suffix to `i4` originally read as
  a mutation SURVIVOR, and both halves of why are traps worth keeping written down. A small
  increment on a `ByRef Long` never touches the high word, so a truncating `stind.i4` writes
  the right answer anyway — the old `41 + 1` version never once killed the mutant. And
  `ByRefString_…`, the only other test that catches it, kills by truncating an OBJECT
  REFERENCE to four bytes, which succeeds or not depending on where the GC happened to put the
  new string: measured **15 kills in 17 runs**, with the clean passes including one across the
  whole 35-test fixture — which is exactly what made the mutant read as a survivor. With the
  boundary-crossing arithmetic in place the mutant now dies **4 runs out of 4** on the full
  fixture, `Long` firing every time and `String` in 3 of those 4 — i.e. the `Long` case is the
  one carrying the decision and the `String` case is a bonus that cannot be relied on. Same
  lesson as `Right(s, n)` with n = half the length, one entry above: *a shape that cannot tell
  the wrong answer from the right one is not coverage* — and a shape that only usually can is
  not either.
  ⭐ **`c3-scratch-typed-as-the-pointer` SURVIVED and the candidate space is NOT closed** —
  declaring the scratch slot `int32&` instead of `int32` assembles and runs. Eight shapes were
  measured against it (Integer, Long, Double written twice, Boolean, String, String under
  400-iteration allocation pressure, a class reference reseated 500 times under pressure, and
  mixed-width pairs); none distinguish it, because the JIT round-trips the slot regardless.
  Unlike the `call`/`callvirt` equivalence one entry above, this is **UNTESTED, not
  equivalent**: `.locals init` is what tells the **GC** how to trace a slot, and a byref-typed
  slot holding a non-pointer is a reporting hazard the runtime is not obliged to tolerate. The
  value type is kept, and the failure to find a shape is recorded rather than argued away.
  ⭐ **`d4-constructor-signature-no-amp` SURVIVED and is DEAD, with direct evidence** — not
  inferred from the survival. A probe asserting `instance void .ctor(int32& 'n')` in the
  emitted IL FAILS, because `IRBuilder` never marks a ctor parameter ByRef (above), so
  `ParamSpec` at that site can never see one. The line is kept: it is a fail-safe that costs
  nothing and becomes correct the moment the IR is fixed.
  ⚠ **`Single` ByRef is NOT covered by the committed fixture.** It works — measured through
  the CLI, `void 'Bump'(float32& 'n')` with `ldind.r4`/`stind.r4`, printing `42.5` — but no
  test pins it, so `GetIndirectSuffix`'s `r4` arm is unexercised. A fixture case needs
  `CSng(...)` on both sides: `Dim v As Single = 41.0` with `n = n + 1.5` is refused by the
  SEMANTIC ANALYZER (Double into Single) and the C++ backend emits an invalid literal `41f`,
  two unrelated pre-existing bugs that have nothing to do with ByRef.
  ⚠ **Not fixed, out of this family, measured compiled-and-run — added to the open list:**
  - ⛔ **C# backend: a `Shared` ByRef method is CALLED WITHOUT `ref`.** `Util.Bump(v)` against
    `Public Shared Sub Bump(ByRef n As Integer)` emits the parameter correctly
    (`public static void Bump(ref int n)`) and then calls it without the keyword —
    **CS1620: Argument 1 must be passed with the 'ref' keyword**, a hard build failure. C++ and
    MSIL both run it and print `42`, so `ByRefOnASharedMethod_OracleIsCppOnly` drops the C#
    leg and says why. Same root as the missing `IRCall.ByRefArguments` marker for a
    `Type.SharedMethod(x)` call.
  - ⛔ **Front end: `IRBuilder.Visit(ConstructorNode)` drops `IsByRef`** (`IRBuilder.cs:1885`)
    — the constructor gap above, a silent wrong answer on C++ and MSIL alike.
  **Full suite in place: 195 / 6409 / 203 / 6807 against the `aea5b8d` baseline
  195 / 6374 / 203 / 6772** — +35 passed, +35 total, **+0 failed**, +0 skipped, which is exactly
  the two new fixtures (28 + 7) and nothing else. The two promoted pins are **flipped, not
  added**: they passed before (asserting the gap) and pass now (asserting the fix), so they
  move no count. 195 reported = 195 anchored `^  Failed ` lines; 170 normalized failing names,
  `diff` against the baseline list CLEAN. **Both entry points exercised** — `MsilHarness`
  (optimizer on) and the CLI (`--target=msil` → ilasm → `dotnet`), the latter on one program
  covering a local, a nested ByRef, a module global, an array element, a ByRef `Function` and a
  counted `For` over a parameter: `42 43 42 11 41 40 4`.

  ⚠ **Narrowing shapes are refused by the SEMANTIC ANALYZER, before any of this** —
  `Public N As Single = 1.5 + 1.0` is "Cannot assign value of type 'Double' to variable of type
  'Single'", before and after. Not a folding gap.
  ⛔ **Also pre-existing and unrelated: `CStr(Boolean)` prints `true` on JavaScript** where C# and
  MSIL print `True`. Measured on a plain local, no module scope involved. Pinned as each backend
  actually behaves rather than normalised away.
  ⚠ **A class with TWO constructors cannot be lowered to JavaScript at all** ("SyntaxError: A class
  may only have one constructor"), measured with a pair that has no Optional anywhere — so
  constructor-overload shapes are asserted on MSIL.
  ⛔ **`Optional ByRef` is broken on every backend, before and after, and there is deliberately NO
  by-ref guard in the fill** — a guard would change nothing observable anywhere and no test could
  kill it. Measured: C# emits `ref int n = 5` (CS1741), JS refuses ByRef outright (BL7002), MSIL
  emits no `&` at all and the CLR rejects the program, and C++ trades "too few arguments" for
  "cannot bind non-const lvalue reference … to an rvalue". The DECLARATION side has to be fixed
  first. Pinned.
  ⚠ **The MSIL half is STALE as of 2026-09-21 — re-measured, and it is no longer "no `&` at
  all".** The declaration emits `void 'Bump'(int32& 'n')` and `Bump(v)` with a SUPPLIED
  argument runs and prints `42`. Only the OMITTED call is refused, by name
  (`a literal has no address`), because `AppendOmittedOptionalArguments` fills a literal and a
  literal has no caller storage to point at — the right answer for the shape rather than a gap.
  C#, C++ and JavaScript are unchanged, so `Optional ByRef` is still broken overall and the
  DECLARATION side is still what has to be fixed first.
  ⚠ **A cross-file call reaches only C# today**, so that one test is structural rather than a run:
  JS refuses it ("no lowering for 'Helpers.Greet'") and MSIL emits
  `call void Combined::HelpersGreet(int32, object)` against a method declared
  `void Greet(int32 a, int32 b)` — the qualified name mangled into the method name and the
  signature spelled from the arguments. Both pre-existing cross-file gaps, unrelated to Optionals.
  ⚠ **Three mutations SURVIVE and the code is kept anyway, all fail-safe**: filling a non-Optional
  parameter, `continue` instead of `return` at the first unfillable one, and filling a resolved
  .NET target. Each is unreachable today — `DefaultValueExpression` is populated only from a
  `ParameterNode`, and the parser marks a parameter Optional whenever it parses a default (the one
  exception, a `ParamArray` WITH a default, does not parse at all). Each makes the fill do NOTHING
  rather than act, which is the same rule the constructor-ambiguity branch above is kept under.
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
