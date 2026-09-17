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
  ⚠ **A module-level initializer may only be a literal or another module-level `Const`.** Anything
  else — `Dim b As Integer = a * 3` with `a` a `Dim`, `= 2 + 3 * 4`, `= SomeFunc()` — crashes the
  FRONT END with a NullReferenceException before any backend runs, on C# as well as MSIL. That is
  a pre-existing compiler gap, not an MSIL one; don't chase it in the backend.
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
  ⚠ **Still failing for unrelated reasons, all pre-existing**: a `Shared` method on a user class
  has no JS lowering and emits an undeclared identifier on C++; MSIL fails any ByRef call with
  InvalidProgramException.
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
  ⛔ **Known gap, pinned:** `Dim G As Double = 7 / 2` is still refused although it is
  arithmetically constant. `/` promotes both operands, so the block is `IRCast, IRCast,
  IRBinaryOp` (measured) and the pass does not fold a cast. `7.0 / 2.0` and `8 \ 2` both fold.
  Closing it means folding a cast of a constant — a NUMERIC-CONVERSION change, not a crash fix:
  widening is lossless but narrowing must agree with each backend at run time, and VB's `CInt`
  rounds half-to-even where a C# cast truncates. Deserves its own characterization.
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
