# Handoff snapshot — 2026-09-11, updated 2026-09-19

**Why this file exists.** Working state for this repo normally lives in a per-machine
auto-memory directory (`~/.claude/projects/…/memory/`) that is **outside the repo and does not
travel**. This file is the in-repo subset a fresh checkout — a cloud session, another machine,
another person — actually needs. It is a dated snapshot, not a changelog: history is in
`git log`, rationale in `docs/superpowers/{plans,specs}/`, conventions in `CLAUDE.md`.

⚠ **Dated 2026-09-11.** Sections carry their own commit where it matters; anything without one
dates from `6139386`. Re-verify before relying on it. **The 2026-09-13 and 2026-09-14 sections are
newer than the rest of this file and supersede it wherever they disagree** — in particular about
whether a cloud container can build and test this repo.

**P2a-2 is COMPLETE.** Tasks 1-15 are done, Step 4 included — the `IDE/` refresh shipped
2026-09-14 in `fbb3694`. The work merged to master in `77e415b`, together with the blnet C++
facade (all five tasks of `2026-09-13-blnet-cpp-facade.md`).

---

## ⛔ THE FORM DESIGNER, 2026-09-18 — read this before touching `BasicLang/Forms/`

Branch `feat/form-designer`, at `f380186`. **89 commits ahead of master, 10 behind.**

### ⛔⛔ The plan's checkboxes are a LIE — do not start at Task 1

`docs/superpowers/plans/2026-09-11-visual-form-designer.md` reads **0 ticked / 98 open**. Tasks 1–19
are *done*; the file was simply never written to again after it was authored. A session briefed from
those checkboxes was told "zero code is written, start at Task 1" — which, followed literally, would
have re-implemented 111 commits over the top of themselves. Verify against `git log` and the
reference counts, never against the checkboxes.

| Task | State |
|---|---|
| 1–19 | done before 2026-09-18 |
| **20** direct manipulation | done — multi-select, rubber band, group drag, align/size, z-order, clipboard, undo |
| **22** double-click → handler | done |
| **23** catalog | done — **10 → 23 kinds**; 8 are WinForms-only by decision |
| **26** Anchor/Dock pickers | done — multi-edge, after the analyzer fix |
| **27** acceptance | done — both targets **built and RUN**, output recorded in the commit |
| **21** retarget | done 2026-09-19 — `FormRetarget`, `design --retarget`, "Retarget Form…"; see its section below |
| **24** menus · **25** tray · **28** closeout | NOT STARTED |

⚠ **24 is compiler-gated**: array-literal element typing has no base-class widening
(`SemanticAnalyzer.cs:6056-6061`), so `{mnuFile, sep1}` degrades to `Object[]`. Measure what csc
accepts before designing the fix — that is how the `Anchor` blocker turned out to be one line.

### ⛔ Two defects that only running the thing could find

`WinFormsCompile` says it in its own summary — *compile only, never run* — so until
`FormDesignerAcceptanceTests` **no form this designer produced had ever been executed** on either
target. Running them found both of these, with a green build throughout:

1. **Every web form was dead on load.** The JS backend emits an unqualified call to the enclosing
   class's own method as a bare global, so `InitializeComponent()` in `Public Sub New()` became a
   `ReferenceError`. `FormScaffolder` now emits `Me.InitializeComponent()`. **The backend defect is
   UNFIXED** and still hits hand-written user code.
2. **WinForms z-order was inverted.** `Controls` index 0 is the TOP of the z-order and
   `Controls.Add` appends, so the document's front-most control was reaching the very back. The
   region writer now emits sibling adds in REVERSE. Confirmed at run time.

Also found and fixed: the structure-preserving writer **never reordered elements**, so a z-order
change never reached the file — the command looked right until you reloaded.

### Task 21 — retarget (2026-09-19): one form, two targets

`FormRetarget.Convert` turns a `.blform` model into a `.blwebform` model and back. The shared
grammar crosses losslessly — kinds, ids, tab order, catalog properties that exist on both targets,
default-event binds (`Click` ⇄ `click`, `TextChanged` ⇄ `input`, from the catalog), unknown
content. Everything else is a **warning** in a new `BL8023..BL8026` block, one per thing:

| Code | What it names |
|---|---|
| `BL8023` | a kind with no row on the destination — removed, its children hoisted into its place |
| `BL8024` | a property the destination lacks, or an unknown attribute the destination would READ as layout |
| `BL8025` | the hard edge: per control, what it had and where it landed; once for the window / the page |
| `BL8026` | a bind on an event only one side can name — dropped, the handler named for hand-wiring |

⛔ **The pixel ⇄ cell edge is derived by a rule the finding can state, never guessed.** Going to the
web: one column per distinct X, one row per distinct Y among siblings, all-`auto` tracks, the
scaffolder's gap. Going to WinForms: catalog sizes, cells pitched to the largest sibling + 8,
origin 16, containers grown to hold their children, window never smaller than a new form. A form
laid out AT that rule's fixed point round-trips **byte-identical** (`FormRetargetTests`); any other
form round-trips byte-identical on the shared subset and moves only its geometry — and says so.

⛔⛔ **A retargeted form is a PAIR and lives in its own directory, outside the source project.**
`ConvertToPair` scaffolds a fresh code-behind on the destination, writes the regions into it and
adds an empty stub per crossed handler, so a CLI user who never opens the IDE still gets a form
that constructs its controls. It is never written beside the source and never added to the same
project: document and code-behind pair by BASE NAME (`FormCodeBehind.PathFor`) and the class is
named after the form, so `LoginForm.blwebform` beside `LoginForm.blform` would pair with the
WinForms class and the designer's next save would write web regions into it. `design --retarget`
therefore REQUIRES `--out <dir>`, and "Retarget Form…" asks for a folder. Neither ever overwrites.

Gated by running, not reading: the retargeted web pair is built by the real CLI and executed under
node (handler fires); the retargeted WinForms pair goes through the real compiler and csc.
⚠ Not run: the WinForms pair as a live window — csc is where its layout ints are checked.

Left for later (`docs/form-designer-followups.md` 18): only a kind's DEFAULT event has a measured
name on both sides, so a `MouseEnter` bind is dropped-and-named rather than mapped.

### ⛔ Master is NOT healthy — 4 of this branch's failures are inherited

Verified 2026-09-18 in a detached worktree at plain `origin/master`, with no designer code present:
every game template fails to build with `CS1503: cannot convert from 'float' to 'int'`
(`Build_GameAppTemplate_*`, `CliTemplate("game")`, `Template("game-app")`, plus two `cpp-game`
siblings). The likely cause is master's own *"Round every narrowing conversion half-to-even"* /
*"Fold lossless widening casts"* work. It escaped because those tests are all
`[Category("Integration")]`, which the fast subset skips.

⛔ **Task 28 cannot honestly claim a clean baseline until this is fixed** — the alternative is
quietly re-baselining around someone else's regression, which is how a known-bad build becomes the
new normal.

### Current gates on this branch

| Gate | Result |
|---|---|
| Full suite (2026-09-19, Task 21 tree) | **7088 passed / 8 failed / 2 skipped of 7098**, 59m |
| Fast subset + the designer's Integration fixtures | **5815 passed / 2 failed / 1 skipped of 5818** |
| `WinFormsCatalogSweepTests` | 57/57 through real `csc` |

The 8: 2 standing `SearchSnippets` · 1 pre-existing `Cli_Build_CppProject` · **4 inherited from
master** · 1 `RaylibScreenSpaceMath` NaN row that is display-dependent. (The previous run's 9th, an
Anchor test, was rewritten with Task 26.) ⚠ This branch has no clean full-suite baseline of its own, and the old "5826 / 4 known
failures" number was measured on a *different branch* 100+ commits ago. Do not quote it.

---

### → START HERE

| If you want | Go to |
|---|---|
| **What to do next** | *What the next session should pick up* |
| Can this machine build and gate? | *READ FIRST — a cloud container CAN build and test* (next section) |
| How to run a trustworthy baseline | *Gates and expected numbers* — including three ways a baseline run LIES |
| Why a green suite proved nothing here | *FOUR REVIEW PASSES* — read before trusting one |
| **Form designer: what is done and what is left** | *THE FORM DESIGNER, 2026-09-18* — read before touching `BasicLang/Forms/` |
| **Is master healthy right now?** | *No* — see the game-template regression in that section |
| Decisions waiting on a human | items 3–4 of the next-session list. ⚠ Multi-edge `Anchor` is **RESOLVED**, see its section |

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
| File the follow-up chips | ⚠ **Written up, not filed** — `docs/form-designer-followups.md` has all **seventeen**, filable verbatim. Opening issues is outward-facing and nobody asked. |
| Refresh the `IDE/` drop | ✅ **Done on Windows** — landed with `claude/jolly-pasteur-l4mpzs`, in master at `77e415b`. It must never be refreshed from a Linux build: `IDE/BasicLang.exe` is a **PE32+ Windows binary** and a Linux refresh swaps the Windows executables for ELF apphosts. `robocopy` on Windows — never `/MIR`. |

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

### ✅ RESOLVED 2026-09-18: multi-edge `Anchor` now works — the premise below was incomplete

**Do not act on this section as an open decision.** It is kept because the measurements are correct
and the reasoning is worth reading; only the conclusion was wrong.

The two options offered at the bottom — "teach the parser a bitwise `Or`" or "ship single-edge only"
— both rested on *BasicLang cannot express a combined flags value*. Re-measured 2026-09-18: all
three spellings below still fail, **but csc was never asked what it would accept**, and a fourth
route was never tried:

| BasicLang source | BasicLang | csc |
|---|---|---|
| `btn.Anchor = 7` | **compiles** | `CS0266` — needs a cast |
| `CType(7, AnchorStyles)` | was refused | **accepted** as `(AnchorStyles)7` |

So the cast was the entire gap, and it was refused by the **semantic analyzer**, not the parser:
`AnchorStyles` registers as a Class-kind handle (the resolver cannot reach `System.Windows.Forms`),
so it never reached the `TypeKind.Enum` exemption sitting three lines above it in
`RejectImpossibleConversion`.

**What shipped** (`b7c6699`): that check now exempts scalar → an *unresolvable* .NET type — **one arm
only**. ⛔ The reference→scalar arm is untouched; it is what closed chip `task_0c803e75`, whose worst
row is silent (a reference cast to `Boolean` compiles *and runs* on C++, binding to the handle's
`explicit operator bool()`). The exemption cannot reach a type the analyzer can see, so
`CType(7, Widget)` is still refused. Both pinned by tests in `CastLegalityTests`.

`RegionWriter` emits multi-edge as `CType(13, AnchorStyles)   ' Left, Top, Right`, and `BL8015` now
means only *an edge name `AnchorStyles` does not have* — still refused, because summing it as zero
would silently anchor the control to nothing. The Anchor/Dock pickers (Task 26) use it.

---

*The original section follows, for its measurements.*

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

**FIVE pieces of this feature were complete, unit-tested and unreachable. The suite was green
through every one.**

| Dead thing | What the user actually got |
|---|---|
| `JavaScriptEmitter.Emit(forms:)` — optional, no caller passed it | a `.blwebform` built green and wrote **no `.html`, no `.css`** |
| `RegionWriter.Write` — **no production caller at all** | scaffold a form, drop a button, save, build → **the build fails on the `InitializeComponent` the scaffold itself calls**. Canvas drew, grid edited, document round-tripped byte for byte, program missing a member |
| `FormAssetEmitter.DispatchSource` — no production caller | every page carried `<body data-form="…">` and **nothing read it** |
| The **default project shape** (no explicit `<Compile>` items) | emitted no pages at all — the source glob cannot yield a `.blwebform` by design |
| `SolutionExplorerViewModel.AddNewFormAsync` — **its `[RelayCommand]` bound to the wrong method, and no menu item** | **the entry point to the whole designer.** There was no way to create a form in the IDE at all: no Add ▸ New Form, nothing. Found by the owner opening the IDE and asking where the designer was. ⚠ The attribute was THERE — a doc comment for `SaveProjectOrReportAsync` had been inserted between it and the method it was written for, and an attribute binds to the next DECLARATION (a doc comment in between is trivia). So the toolkit generated a `SaveProjectOrReportCommand` nothing binds and no `AddNewFormCommand`; it compiles either way |

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
**The lesson is not "read more carefully": a bullet in a seventeen-entry list is not a safeguard.**
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
   number to beat is 5893 passed / 4 failed at `ee3c086`. On Linux, **re-gated against the current
   master `77e415b`** (2026-09-14): baseline **213 failed / 5876**, `feat/form-designer` **175 /
   6273 — 0 new, 38 fixed**, `fix/js-module-qualified-call` **213 / 5882 — 0 new**. Both PRs now
   carry master merged in, so those are the numbers a Windows run should be checked against.
2. **Open the IDE and look at the designer and the Settings dialog** — see *Still unverified* above.
   ⚠ Now also: **save a form and confirm the `.bas` is regenerated**, and that a hand-edited region
   puts BL8011 in the Error List. That path is covered by caller tests driving the real `SaveAsync`,
   but nobody has watched it happen.
3. **Decide the multi-edge `Anchor` question above.** Until then anchoring is single-edge or `Dock`.
4. **Decide follow-up 13**: nothing makes `Main()` call the dispatch. BL8018 warns, which is the
   honest minimum, but a warning is not the feature working. Both ways to close it edit the user's
   code, which is why neither was done unilaterally.
5. **File the seventeen chips** in `docs/form-designer-followups.md`. Several are runtime failures
   from clean builds, which is the highest-severity shape this repo tracks. Entries 3 and 14 are
   the SAME compiler bug — file them together. Entry 15's residue (Win32 `*.bas` matching `.basic`)
   is unverified on Linux; confirm it during the Windows run.
6. **Finish Task 19.** (The `IDE/` drop refresh is DONE — it landed with
   `claude/jolly-pasteur-l4mpzs` in master `77e415b`.)
7. **PR #3 (`claude/busy-newton-gsispd`)** is still open carrying a superseded spec/plan pair.
8. **Decide the C# and C++ halves of the module-call bug.** PR #6 fixes JavaScript only. Re-measured
   on `77e415b` (2026-09-14): **C++ is still broken** — `M.Go()` gives clang *"use of undeclared
   identifier 'M'"*, `IRBuilder.cs` untouched by the facade merge — and C# still rejects the
   unqualified `Go()` with CS0103. Matrix and per-backend fix shapes in
   `docs/form-designer-followups.md` 14.

⛔ **Do not take a green suite as evidence the feature works.** FIVE separate pieces of this
branch were complete, unit-tested and unreachable, and the suite was green through every one — the
last of them the Add ▸ New Form command, the only way into the designer at all. When you add
anything here, the question that matters is *who calls it in a shipping build* — and the answer has
to be a test that drives the real entry point, not one that constructs the thing. For UI, that
means a test that reads the AXAML for the binding: a `[RelayCommand]` no menu binds is exactly as
unreachable as the un-attributed method was.

---

## ✅ master is FULL-SUITE GREEN, and Task 14 is PROVEN (2026-09-11)

⚠ **`origin/master` has since moved to `77e415b`** (2026-09-14) — `claude/jolly-pasteur-l4mpzs`
merged, carrying the `ee3c086` Windows gate (5893 passed / 4 failed, all baseline) and a refreshed
`IDE/` drop. The record below is the `f54416b` state it built on.

At `f54416b`, master merged eight P2a-2 Task 14 commits (tip `87a6c5e`). **Full suite measured on
Windows: 5826 tests, 4 failures — exactly the standing baseline below, nothing new.**

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
- ⛔⛔ **`git merge-tree` gave FALSE NEGATIVES on conflict detection — repeatedly.** Across several
  check-ins it reported both open PRs as conflict-free against master; the real
  `git merge origin/master` conflicted in `docs/HANDOFF.md` every time. The check said "clean" while
  the merge did not, so the branch looked mergeable for days. **Test-merge for real** — add a
  DETACHED worktree at the branch tip (`git worktree add --detach <dir> <sha>`; without `--detach`
  it refuses with *"already used by worktree"*), run the actual merge there, read the conflicts,
  then throw the worktree away. Nothing else answers the question.
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

**Re-gated 2026-09-14 against the CURRENT master `77e415b`** (Linux container, this box). The
earlier numbers below were taken against `6a6d224`; master has moved twice since, so these are the
ones that count for the two open PRs:

| Run (Linux, `77e415b` baseline) | Failed / Total | Names vs baseline | Time |
|---|---|---|---|
| **Baseline `77e415b`** (worktree) | 213 / 5876 | — | 7m31s |
| **`feat/form-designer`** merged to `77e415b` (`05f736f`, PR #4) | **175 / 6273** | **0 new, 38 fixed** | 11m40s |
| **`fix/js-module-qualified-call`** merged to `77e415b` (`7cbff64`, PR #6) | **213 / 5882** | **0 new, 0 fixed** | 7m05s |

Both diffs are by sorted failure NAME (`comm -13`), not by count — and the form-designer row is
exactly why: its count moved 213 → 175 for two reasons at once. PR #6's six new tests pass (5876 →
5882 total) without disturbing the set, which is the shape a compiler fix should have.

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
