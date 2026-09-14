# Handoff snapshot — 2026-09-11, updated 2026-09-14

**Why this file exists.** Working state for this repo normally lives in a per-machine
auto-memory directory (`~/.claude/projects/…/memory/`) that is **outside the repo and does not
travel**. This file is the in-repo subset a fresh checkout — a cloud session, another machine,
another person — actually needs. It is a dated snapshot, not a changelog: history is in
`git log`, rationale in `docs/superpowers/{plans,specs}/`, conventions in `CLAUDE.md`.

⚠ **Everything below was true at `6139386` unless a section says otherwise. Re-verify before
relying on it.** The 2026-09-13 section immediately below is newer than the rest of this file
and supersedes it wherever they disagree — in particular about whether a cloud container can
build and test this repo.

### → START HERE

| If you want | Go to |
|---|---|
| **What to do next** | *What the next session should pick up* |
| Can this machine build and gate? | *READ FIRST — a cloud container CAN build and test* (next section) |
| How to run a trustworthy baseline | *Gates and expected numbers* — including three ways a baseline run LIES |
| Why a green suite proved nothing here | *FOUR REVIEW PASSES* — read before trusting one |
| Decisions waiting on a human | *AN OPEN DECISION: multi-edge `Anchor`*, and items 3–4 of the next-session list |

---

## ⛔ READ FIRST — 2026-09-13: a cloud container CAN build and test this repo

**This overturns the standing assumption that cloud sessions cannot gate.** A .NET 8 SDK installs
from the Ubuntu archive; the package index in a fresh container is just stale:

```bash
apt-get update && apt-get install -y --no-install-recommends dotnet-sdk-8.0   # ~1 min
```

`builds.dotnet.microsoft.com` IS blocked by the agent proxy, which is what made the earlier
"no compiler here" finding correct at the time and wrong now. Nothing else is needed: NuGet
restore works, the compiler and the test project both build, and the full suite runs.

**What that cost.** Twelve commits of form-designer work (`d8d6124`…`8c841f0`) were written,
reviewed by subagents, and reported as gated — with a compiler nobody had run. The first real
build found:

- **`VisualGameStudio.Tests` had not compiled since `0152506`** (CS0104: `MethodInfo` is
  ambiguous between `System.Reflection` and `VisualGameStudio.Core.Abstractions.Services`, which
  declares its own at `IRefactoringService.cs:131`). **Every "gate" reported in commits
  `0152506` through `8c841f0` was therefore never run.** Fixed in `e2c4c51`.
- Two tests that had never executed asserted the wrong thing — one demonstrated "an Int is bare"
  using a property the control's catalog row does not declare, the other counted `"Sub "`
  occurrences and expected the count to include `End Sub`, which has no trailing space.
- `CliTestHarness.CliPath()` hardcoded `BasicLang.exe`. The apphost is `BasicLang` with no
  extension off Windows, so **every spawned-CLI test in this suite was red on Linux**, and the
  failure read as "not deployed — project reference output changed?", which looks like a build
  layout problem rather than an unsupported platform. Fixing it turned 38 pre-existing failures
  green.

**Take the lesson, not just the fix:** subagent review is a decent proof-reader and is not a
compiler. It found 29 real defects across three passes and still missed a file that did not
compile.

### ⛔ WinForms can be TYPE-CHECKED here too — the catalog is falsifiable off Windows

The WinForms **reference assemblies** restore as an ordinary NuGet package, and reference-only
compilation is cross-platform (they are metadata, not code):

```xml
<PackageReference Include="Microsoft.WindowsDesktop.App.Ref" Version="8.0.31"
                  GeneratePathProperty="true" ExcludeAssets="all" PrivateAssets="all" />
```

So `csc` type-checks generated WinForms C# on Linux, where the WindowsDesktop **MSBuild SDK** does
not exist at all (`dotnet build` of a `net8.0-windows` project fails with MSB4019, and
`EnableWindowsTargeting` does not help — it needs those same missing targets). Running a WinForms
app still needs Windows; nothing the catalog gate checks needs the program to start.

⚠ Three assemblies ship in BOTH the base and desktop ref packs — `System.Drawing`, `WindowsBase`,
`Microsoft.VisualBasic` — and the DESKTOP copy must win, as it does under the real SDK. Pass both
and Roslyn sees two assemblies with the same simple name; the error it then produces points at the
innocent one.

### ⛔ Avalonia.Headless is restorable too — the third assumption to fall

`Avalonia.Headless` and `Avalonia.Headless.NUnit` 11.3.13 both restore. Task 7's plan entry says
"there is no `Avalonia.Headless` reference, so the canvas is verified by running the IDE — say so";
that constraint no longer holds, and whoever takes **Task 14** should know before pricing it.

Nothing on this branch uses it yet: Task 7's risky part is pure geometry and needs no UI thread.
But three inherited platform assumptions have now been measured and all three were wrong — no
compiler in a cloud container, no WinForms type-checking off Windows, no Avalonia headless. **Check
the next one before planning around it.**

### Measured on Linux, .NET 8.0.131 (this container)

| Run | Result | Time |
|---|---|---|
| Full suite, pre-branch baseline `6a6d224` | **174 failed / 5837 total** (5460 passed, 203 skipped) | ~8 min |
| Full suite, `feat/form-designer` tip | **174 failed / 6191 total** (5814 passed, 203 skipped) | ~8 min |
| Fast subset, baseline `6a6d224` | 90 failed / 4939 total | ~1 min |
| Fast subset, `feat/form-designer` tip | 90 failed / 5196 total | ~1 min |

⛔ **The two full-suite failure sets are IDENTICAL, compared by test name** — 174 names, no
regressions and no accidental fixes. That comparison, not the count, is the gate: run the
baseline in a `git worktree` and `comm -23` the sorted failure names. A raw count hides a
regression that lands as another test goes green.

⚠ **The 174 are environmental, not the Windows baseline of 4.** They are Windows-only tests on
Linux: 23 assert on hardcoded `C:\` paths, 10 need clang, 8 need MSVC/`vcvars`. **Do not treat
174 as "the number" on Windows** — re-measure there. The Windows baseline in the table further
down (5826 total / 4 failures at `f54416b`) is still the number that matters for a release.

⚠ The full suite takes **~7 minutes here, not ~2 hours**. That is not a faster machine: the
native/clang/MSVC integration tests fail fast instead of running. A green-looking short run on
Linux has not exercised codegen end-to-end.

### Form designer — where it actually is

Plan: `docs/superpowers/plans/2026-09-11-visual-form-designer.md` (19 tasks).
Spec: `docs/superpowers/specs/2026-09-11-visual-form-designer-design.md`.
Branch: **`feat/form-designer`** (PR #4). Not merged.

**Done and now genuinely gated:** Tasks **1–18**. Task **19** (closeout) is partial — two of its
four items cannot be done off Windows.

| Task | What landed |
|---|---|
| 1 | `ProjectSerializer` preserves the `.blproj` in place instead of rebuilding it from the model |
| 2–3 | TFM reaches the file; DPI mode emitted; two silent C# backend defaults now throw |
| 4 | Form model — geometry, controls, catalog, document, clipboard |
| 5 | The recognizer (importer) — WinForms and DOM dialects, values kept as raw source text |
| 6 | `DesignDiagnostic`, the `BL8xxx` band, `basiclang design --check` |
| 8 | `CSharpTestSupport` with the false-green guard |
| 9 | `.blwebform` reader/writer, D9 tiers, the algebra |
| 10 | Form documents ride as `<Compile>` and are skipped on both compile routes |
| 11 | Designer-owned marked regions — hashing, refusal, handler ordering |
| 12 | Markup/CSS/JS emission, the two-step `data-form` dispatch |
| 13 | Creating a form — the document + `.bas` pair, and the glob guard |
| **15** | **A missing handler is a hard error (D8)** |
| **16** | **`.blform` — the same reader and writer, not a second one** |
| **17** | **The WinForms catalog gate — every control, every property, through the real chain to `csc`** |
| **18** | **The VSIX shape promoted across IDE and CLI, geometry fan-in, build + equivalence gates** |
| **7** | **`FormCanvasControl`, the one shared transform, and the Design\|Code mode** |
| **14** | **Toolbox and property grid — D9's tiers reaching the UI, on one extracted row editor** |
| **19** | **Closeout — partial. See below.** |

**Not done:** Task 7 and 14 (Avalonia canvas + property grid — they build here, but there is no
`Avalonia.Headless` package so nothing can drive them), 19 (closeout).

### Task 19 — what is left, and why

| Item | State |
|---|---|
| Full suite, both entry points | ✅ Run every commit; see the table above |
| Update `docs/HANDOFF.md` and `CLAUDE.md` | ✅ This file, plus a durable *Form designer* section in `CLAUDE.md` |
| File the follow-up chips | ⚠ **Written up, not filed** — `docs/form-designer-followups.md` has all **thirteen**, filable verbatim. Opening issues is outward-facing and nobody asked. |
| Refresh the `IDE/` drop | ❌ **Must not happen here.** `IDE/BasicLang.exe` is a **PE32+ Windows binary**; refreshing it from a Linux build would swap the Windows executables for ELF apphosts and break the drop for everyone. Do it on Windows with `robocopy` — never `/MIR`. |

⛔ `docs/MULTI_FILE_SYSTEM_PLAN.md:21` is **untouched**, per owner decision 2 — `.frm` stays reserved
for a user-authored form file and is not obsoleted by `.blform`.

### ⛔ Still unverified: everything you can only see

Three pieces of UI shipped without anyone looking at them. Their LOGIC is tested; their APPEARANCE
is not, and no test claims otherwise:

- the **canvas** (`FormCanvasControl`) — its transform has 15 tests because a drift there lands
  clicks on the wrong control with no visual symptom, but what it draws is unchecked;
- the **property grid and toolbox** — the rows, tiers and commit semantics have 18 tests;
- the **Settings dialog**, whose duplicated row editor was extracted into `TypedValueEditor` and
  which now renders through it. 165 settings tests still pass, and the AXAML compiles with its
  bindings resolved, but nobody has opened the dialog.

**Open the IDE before merging.** A build proves Avalonia compiled the markup and the compiled
bindings resolved. It does not prove anything is visible, laid out, or the right size.

### ⛔⛔ D8's handler-ordering rule is real but WEB-ONLY

Measured 2026-09-13, three ways, because applying it to both targets made the designer refuse the
very shape Owner decision 3 calls canonical:

| Shape, handler declared AFTER the wiring | Result |
|---|---|
| Web: `addEventListener("click", AddressOf H)` | **FAILS** — "cannot convert from `Action(Of Object)` to `Action(Of DomEvent)`". The DOM signature declares the parameter type, so the erased handler has something concrete to fail against. |
| WinForms: `AddHandler btn.Click, AddressOf H` | **Compiles**, through BasicLang *and* csc, and binds with full parameter types (`object sender, EventArgs e`). The event is an unresolvable .NET member typed as `Object` — there is no declared delegate to mismatch. |
| Module-level `Sub` into a declared `Action(Of Integer)` | **Compiles.** |

The shipped VSIX template declares `btnClick_Click` **below** the `InitializeComponent` that wires
it. `RegionWriter` was refusing that on both targets (BL8013), so the designer rejected the template
it is modelled on and blocked the D12 import route for every existing WinForms file. The check is
now web-only, and the scaffolder emits the init region in the canonical position on WinForms and
last on the web.

### ⛔ `basiclang a.bas b.bas` silently compiled only the first file

`FirstOrDefault` over the file arguments. It printed *"Compilation successful!"*, *"Files compiled:
1"* and exit 0, while the emitted C# referenced a class that was never compiled and failed at csc
with *"The type or namespace name 'MainForm' could not be found"*. Found on the shipped VSIX
template, which is exactly two files. Extra source files are now **refused** with a message pointing
at the project route — single-file is the documented contract and multi-file is what a `.blproj` is
for.

### ⛔⛔ Task 17's gate found six defects the whole toolchain was blind to

Every one compiled **green** through BasicLang and would have shipped. This is the clearest
evidence in the repo for why the catalog needs csc rather than review:

| Catalog claim | What WinForms actually has | csc |
|---|---|---|
| `TextBox.PasswordChar` is a String | a `char` | CS0029 |
| `RadioButton.GroupName` | **does not exist** (grouping is by container) | CS1061 |
| `ComboBox.Items` assignable | get-only collection | CS0200 |
| `ListBox.Items` assignable | get-only collection | CS0200 |
| `ListBox.MultiSelect` | **does not exist** (it is `SelectionMode`, an enum) | CS1061 |
| `PictureBox.Image` is a path String | a `System.Drawing.Image` | CS0029 |
| `TextAlign = Center` | `ContentAlignment` has no `Center` — it has `MiddleCenter` | CS0103 |

`GroupName` and `MultiSelect` are now **web-only** — a platform fact, not a preference. The rest
gained a WinForms enum type, a member mapping, a value factory (`Convert.ToChar`,
`Image.FromFile`), or a collection marker. Completeness guards now FAIL on an Enum row with no
enum type, an allowed value that maps to no member, and a WinForms control with no type name.

⚠ `Convert.ToChar`, **not** `CChar` — measured. BasicLang passes `CChar` through to the C# backend
verbatim and C# has no such function (CS0103). The same is true of anything VB-shaped: that backend
is a passthrough for names it does not know, so "it compiled" means nothing on its own.

### ⛔ AN OPEN DECISION: multi-edge `Anchor` is not expressible in BasicLang

`Anchor="Left,Top,Right"` — an ordinary WinForms thing — cannot be generated. Measured three ways
on 2026-09-13:

| Attempt | Result |
|---|---|
| `AnchorStyles.Left Or AnchorStyles.Top` | *"Logical operator 'Or' requires Boolean operands"* |
| `CType(7, AnchorStyles)` | *"Cannot convert 'Integer' to 'AnchorStyles': no such conversion exists"* — the enum is an unresolvable .NET type |
| `AnchorStyles.Left \| AnchorStyles.Top` | `\|` **lexes** (`TokenType.BitwiseOr`, `BasicLangLexer.cs:754`) but the parser never consumes it: *"Unexpected token in expression"* |

The designer currently **refuses** such a document (`BL8015`) rather than emitting one flag (which
puts geometry on screen the running program will not reproduce — the exact D9 divergence) or all of
them (which does not compile). Reachable today only from a hand-authored `.blform`, because the
canvas that would offer multiple anchors is Task 14.

**The decision someone has to make:** teach the parser a bitwise `Or`/`|` (a language change with a
full-suite blast radius, and the semantic analyzer would also have to stop demanding Boolean
operands for an unresolvable enum type), or keep refusing and ship single-edge anchors plus `Dock`.
Not this writer's call, so it refuses and says why.

### Contradictions found against the spec and the briefing

1. **The briefing's platform table is wrong** — see the top of this section. Tasks marked
   "build but unverifiable" and "needs the full suite" were both doable here.
2. **`FormClipboard` used `(int?)` casts on XML attributes**, which throw `FormatException` on a
   non-integer. The clipboard is precisely where unvetted text arrives; a paste carrying
   `X="20px"` took the IDE down rather than declining the paste. Now `int.TryParse`, like every
   other reader in the feature.
3. **"Structural attribute" is not a property of the attribute NAME.** The two formats overlap in
   spelling and not in meaning — `Width` is a `.blform` control's pixel width and is read into its
   geometry, while on a `.blwebform` control nothing reads it. Under one flat list it was neither
   a property nor an unknown attribute: absent from the model entirely, and dropped by anything
   rebuilding the document from it. `IsStructural` now takes a `FormTarget`.
4. **The spec does not say what happens when a file's NAME disagrees with its ROOT element.** A
   `.blform` containing `<WebForm>` is now REFUSED. Neither side can be believed over the other:
   trust the root and the writer emits one format's geometry into a file the project system
   compiles as the other; trust the extension and every `X`/`Y` reads as an unknown attribute and
   the canvas comes up empty. Both are invisible until the user saves.
5. **Task 15 could not be implemented as the plan words it.** The plan says to error whenever
   `GetNodeSymbol(operand)` is null. That would reject every correct WinForms program, this
   designer's generated output included, because `EnableNetResolution` returns early for
   `UseWindowsForms` (`Compiler.cs:145`) and the resolver closure cannot reach
   `System.Windows.Forms.dll` — so every `AddressOf Me.Handler` has a null symbol and always
   will. **Implemented narrower:** the only new error is a BARE NAME that resolved to no symbol
   at all. A member access is left alone. `IsNetType` is NOT narrowed, per the plan's own warning.
   The `AddHandler`/`RemoveHandler` validation is likewise silent whenever the event side is
   unresolved, and reports only a resolved symbol that is plainly not an event, a parameter-count
   disagreement, or two *primitive* parameter types that differ. There is no assignability helper
   in `SemanticAnalyzer` to widen that last one with, and comparing class names would flag
   `EventArgs` against `MouseEventArgs` — the ordinary correct shape of a handler.

### ⛔⛔ 2026-09-14 — FOUR REVIEW PASSES, ~30 DEFECTS. READ THIS BEFORE TRUSTING A GREEN SUITE.

*(Blow-by-blow is in `git log 0d3e7c3..313da82`. What follows is only what stays true.)*

**The question that found nearly all of it**, asked of every pass:

> a targeted audit for **functionality reachable ONLY from tests** — optional parameters no
> production caller passes, public methods whose only callers are tests, wiring that exists but is
> never invoked from a shipping path.

**Four pieces of this feature were complete, unit-tested and unreachable. The suite was green
through every one.**

| Dead thing | What the user actually got |
|---|---|
| `JavaScriptEmitter.Emit(forms:)` — optional, no caller passed it | a `.blwebform` built green and wrote **no `.html`, no `.css`** |
| `RegionWriter.Write` — **no production caller at all** | scaffold a form, drop a button, save, build → **the build fails on the `InitializeComponent` the scaffold itself calls**. Canvas drew, grid edited, document round-tripped byte for byte, program missing a member |
| `FormAssetEmitter.DispatchSource` — no production caller | every page carried `<body data-form="…">` and **nothing read it** |
| The **default project shape** (no explicit `<Compile>` items) | emitted no pages at all — the source glob cannot yield a `.blwebform` by design |

⚠ **`FormClipboard` is the one still standing** — complete, tested, and the canvas has no
Copy/Cut/Paste to reach it. Follow-up 11.

#### ⛔⛔ The one that shipped: a green build is not a running page

The dispatch was generated as a `Public Module`. It compiled clean, every string the tests looked
for was present, and every page died on load with **`ReferenceError: VgsForms is not defined`** —
the JavaScript backend FLATTENS a module's members to bare globals while emitting the call site
QUALIFIED, so the script referenced an object appearing nowhere in the file. I read that exact
output and called it success: one line said `VgsForms.VgsDispatchForm();` and another said
`function VgsDispatchForm() {`, two lines that contradict each other.

Generated as `Public Class` + `Public Shared Sub` now. **The backend bug is UNFIXED and belongs to
the compiler** (follow-ups 3 and 14) — anyone calling a module across files gets a clean build and
a dead page.

✅ **`node` v22 is on PATH here, and `FormBuildEmissionTests` RUNS the emitted script** against a
stub `document`. That gate is what catches this class. Keep it green.

⛔⛔ **And that bug was already in our own notes** — `docs/form-designer-followups.md` entry 3,
third bullet, measured three days earlier: *"a qualified module call emits a reference to a
container JS does not have → ReferenceError"*. Nobody reread it, including the person who wrote it.
**The lesson is not "read more carefully": a bullet in a fifteen-entry list is not a safeguard.**
What caught this was running the output and comparing failure sets — mechanisms, not memory. A
finding that matters needs a gate or a filed issue.

#### The four rules that came out of it

1. **Ask the CATALOG what a value means, never the SHAPE of the string.** A `Type.Member` regex
   answering *"is this already source?"* was wrong both ways: `Text="config.json"` emitted unquoted
   (form stops building), and `TextAlign="ContentAlignment.Bogus"` sailed past the Degraded check
   into CS0117 with no diagnostic. `FormPropertyDef.IsSourceForm` is the answer.
2. **A throw needs a catcher before it is an improvement.** Making `ProjectSerializer` refuse a
   namespaced save was right — it used to lie — but eleven `SaveProjectAsync` call sites caught
   nothing, so it traded a silent failure for an unhandled exception *after* both new files were on
   disk. Now `ProjectSaveRefusedException`, reported everywhere.
3. **Mirrored code is not shared code.** `ProjectGlobSafety.MaterialiseGlobbedSources` exists to
   reproduce `GetSourceFiles`' glob exactly. A guard added to one and not the other let the IDE
   write an explicit `<Compile>` item for a file the compiler's glob rejects — and the explicit
   branch does no extension filtering at all.
4. **Writing that a test runs something is not it running.** A two-form test was described in its
   own commit message as executing the result; it built and string-asserted. Check the body.

#### Diagnostics

**BL8018** claimed and in the band table (form pages exist, nothing calls the dispatch).
**BL8031 left alone** — the spec and plan reserve it for one specific `--check` collision.

### What the next session should pick up

1. **Re-run the full suite on Windows.** Everything above is a Linux measurement. The Windows
   number to beat is 5826 total / 4 failures at `f54416b`; this branch adds ~390 tests. On Linux:
   branch **174 failed / 6234**, baseline `6a6d224` **212 failed / 5837** — zero regressions, 38
   turned green.
2. **Open the IDE and look at the designer and the Settings dialog** — see *Still unverified* above.
   ⚠ Now also: **save a form and confirm the `.bas` is regenerated**, and that a hand-edited region
   puts BL8011 in the Error List. That path is covered by caller tests driving the real `SaveAsync`,
   but nobody has watched it happen.
3. **Decide the multi-edge `Anchor` question above.** Until then anchoring is single-edge or `Dock`.
4. **Decide follow-up 13**: nothing makes `Main()` call the dispatch. BL8018 warns, which is the
   honest minimum, but a warning is not the feature working. Both ways to close it edit the user's
   code, which is why neither was done unilaterally.
5. **File the fifteen chips** in `docs/form-designer-followups.md`. Several are runtime failures
   from clean builds, which is the highest-severity shape this repo tracks. Entries 3 and 14 are
   the SAME compiler bug — file them together. Entry 15's residue (Win32 `*.bas` matching `.basic`)
   is unverified on Linux; confirm it during the Windows run.
6. **Refresh the `IDE/` drop on Windows**, and finish Task 19.
7. **PR #3 (`claude/busy-newton-gsispd`)** is still open carrying a superseded spec/plan pair.

⛔ **Do not take a green suite as evidence the feature works.** Three separate pieces of this
branch were complete, unit-tested and unreachable, and the suite was green through every one. When
you add a generator here, the question that matters is *who calls it in a shipping build* — and the
answer has to be a test that drives the real entry point, not one that constructs the generator.

---

## ✅ 2026-09-11 — master is FULL-SUITE GREEN at `f54416b`

*(Still true, and still the Windows number that matters. Read the 2026-09-13 section above
first: it is newer, and it contradicts this file's assumption about cloud sessions.)*

`origin/master` == **`f54416b`**, which merged eight P2a-2 Task 14 commits (tip `87a6c5e`) into
master. **Full suite measured on Windows: 5826 tests, 4 failures — exactly the standing baseline
below, nothing new.**

Worth recording *because* it was in doubt: that merge combined two sides that had only ever been
gated apart. Task 14's commits are test-only (plus one small seam); the incoming master commits
changed `BasicLang/JavaScriptEmitter.cs`, `BasicLang/Program.cs` and
`BasicLang/ProjectSystem/TemplateEngine.cs`. They touch **no file in common**, and the full run
confirms the combination is clean.

⚠ **One thing is still unproven, and it is not about the build.** `46fd2c5` is a WIP commit whose
**27 tests pass but have no mutation kills and no review** — none has been shown to fail for the
right reason, or to fail at all. Green is not the same as proven, and these are exactly the kind
of assertion this task repeatedly found passing for structural reasons. It also carries the one
product change: `BasicLang/CSharpBackend.cs`, where the test seam `AmbientNamespacesForTest` (a
`static` alias that compared a constant with itself and could not fail even if the seeding loop
were deleted) becomes an instance view `CandidateUsingsForTest => _usings`. Emission was measured
unmoved (parity battery 22/0/0). **Proving those 27 tests is job #1 below.**

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

⚠ These are **Windows** numbers. For Linux/cloud numbers — and for the worktree-diff method that
should be used instead of comparing raw counts — see the 2026-09-13 section at the top.

### ⛔ Three ways a baseline run lies, all three hit in one session

1. **`git stash` without `-u` leaves new UNTRACKED files behind.** Nothing compiles, the run
   produces zero test results, and you read it as "0 failures" — a baseline that looks perfect
   because it never ran. Use a `git worktree`, or `git stash -u`.
2. **A worktree run can die mid-suite** (the C++ end-to-end tests are where it happened), leaving a
   truncated file with failures in it and **no summary line**. Always confirm a `Failed!`/`Passed!`
   line exists before trusting any count you grepped out.
3. **Take totals from the FINAL run of a session.** Two runs a few commits apart differ by whatever
   tests landed between them, and quoting the earlier one into a document is how a corrected number
   gets un-corrected.

⛔ And the rule those serve: **compare sorted FAILURE NAMES with `comm -23`, never counts.** A
count hides a regression that lands as another test goes green — which is not hypothetical here:
this branch turns 38 tests green while introducing none, so its count moved for two reasons at once.

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

**Left to do, in order:**

1. **§12.4's V2 and V3 are UNPROVEN** — the WIP commit's 27 tests need their mutation kills, and
   **each must be DISCRIMINATING**: if a mutation also reds a pre-existing test it has proved
   nothing about the new one, so record the split ("1 red of 20"). The six: empty the `Rejected`
   registry set · make `MapTypeName`'s default arm skip the `NetRef` handle · remove one entry
   from `NetAmbientNamespaces.All` · delete the C# backend's seeding loop · flip one
   `CppCapabilityChecker.CheckType` early return · make `NetClaimPredicate` claim
   `File.ReadAllText`. Then review that commit properly.
3. **Task 15, the closeout.** Its inputs are already gathered: a detached worktree at
   `.worktrees/p2a1base` sits at `2752a96` for the empty-surface inertness diff (materialise the
   console and game templates, flip `<TargetBackend>` to Cpp, build at both commits, diff
   `obj/gen` + build log + stdout, subtract the two known splices `NetException` and `NetRef`).
   Spec status updates: header `Draft` → `Implemented`; §14.15 → Resolved; §15.11 → Decided;
   §15.6 → Recorded-unchanged. **Stale prose to sweep:** `NetInertnessTests`'s header still says
   `NetResolverFactory` is set "at exactly ONE site repo-wide" — false since Task 4
   (`EnableNetResolution` is also called at `BasicLang/Program.cs` :511 and :1076 and
   `BuildService.cs` :645; only the LSP leaves it null), plus dated "pre-flip" prose in
   `NetIrCarriageTests` and `NetFlipTests`.

**The failure mode this task kept finding — check for it in any test you write or review.**
Assertions that pass for structural reasons rather than because the property holds: a guard
asserting on text the test itself wrote; a comparison the product feeds both sides of (change one
*comment* in `BlnetShimSources.HandleTable` and the "verbatim" test stays green, because both its
sides move together); a hand-typed list standing in for a derived one; a guard against *empty*
that is not a guard against the *shape* that made the row worth having; a pin on a shape nothing
emits (the golden mangle literal once described an instance method as static); and a doc comment
describing a test that does not exist (`RowBAgreesWithTheCapabilityCheckersEarlyReturns` still
claims to drive the capability checker — the test project never constructs one).
