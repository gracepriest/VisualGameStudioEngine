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
  ⚠ **Still failing for unrelated reasons, all pre-existing**: MSIL fails any ByRef call with
  InvalidProgramException. **The C++ half of this note is now STALE and is corrected here**: a
  `Shared` method on a user class emitted an undeclared identifier on C++, and both that and the
  JavaScript equivalent were FIXED 2026-09-19 (see the JS Shared-method entry and the C++
  `Shared`-access entry). Fixing them did NOT move this row — it still fails, on MSIL, which is
  why the row's name is unchanged in the by-name suite comparison.
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
