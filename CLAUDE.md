# Visual Game Studio Engine — Claude Code Guide

Operating guide for this repo. **This is not a changelog.** History lives in
`git log`, design rationale in `docs/superpowers/{plans,specs}/`, and current work
status in the auto-memory (`MEMORY.md`, loaded automatically each session). Keep
this file durable — don't append dated bug-fix logs.

## What this is

BasicLang (a VB-like language + compiler), a cross-platform IDE (Avalonia), and a
2D game engine (C++/Raylib) with a VB.NET P/Invoke binding layer.

## Projects (`VisualGameStudioEngine.sln`)

**Compiler**

| Project | Lang | Role |
|---|---|---|
| BasicLang | C# | Compiler: preprocess → lex → parse → semantic → IR → optimize → backends (C#, C++, JavaScript, LLVM, MSIL); also runs as `--lsp` server and `--debug-adapter` |

**IDE**

| Project | Lang | Role |
|---|---|---|
| VisualGameStudio.Core | C# | Abstractions / service interfaces / models |
| VisualGameStudio.Editor | C# | Avalonia code editor (highlighting, IntelliSense, folding) |
| VisualGameStudio.ProjectSystem | C# | Projects, build service, LSP client, debugging |
| VisualGameStudio.Shell | C# | IDE app shell — **the build/run target for the IDE** |
| VisualGameStudio.Tests | C# | NUnit suite (~2,400 tests) |
| BasicLang.VisualStudio | C# (VSIX) | VS 2022 CPS extension — details in `docs/vs-extension-notes.md` |

**GameEngine**

| Project | Lang | Role |
|---|---|---|
| VisualGameStudioEngine | C++ | 2D engine DLL on Raylib (C-ABI exports in `framework.h`) |
| RaylibWrapper | VB.NET | P/Invoke bindings to the engine DLL |
| CPPengineTest | C++ | Native engine smoke / game-loop test |
| TestVbDLL | VB.NET | Sample game exercising engine + wrapper |

Not in the `.sln`: `vscode-basiclang` (VS Code extension). Removed (do not resurrect
from old docs): the legacy `VisualGameStudio` VB.NET IDE and the `VS.BasicLang` VSIX.

## Build / test / run

```powershell
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release   # the IDE
dotnet build BasicLang/BasicLang.csproj -c Release                             # compiler alone
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release                            # full suite (~39 min; integration tests compile/run native code, spawn clangd/DAP)
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"  # fast subset (~2 min; skips the [Category("Integration")] compile/run/spawn tests)
IDE/VisualGameStudio.exe                                                        # run prebuilt IDE
IDE/BasicLang.exe MyFile.bas --target=csharp                                   # CLI compile a file (pass source directly — there is no `compile` subcommand)
IDE/BasicLang.exe build MyProject.blproj                                        # CLI build a project
IDE/BasicLang.exe --lsp                                                         # language server
IDE/BasicLang.exe --debug-adapter                                              # debug adapter (--dap-legacy = old)
```

The native C++ engine builds via VS 2022 MSBuild on `VisualGameStudioEngine.vcxproj`
(x64/Release), auto-discovered through vswhere.

**On Linux / in a cloud container** the SDK installs from the distro archive — the package index in
a fresh container is stale, so update first:

```bash
apt-get update && apt-get install -y --no-install-recommends dotnet-sdk-8.0
```

The full suite runs in ~8 minutes there, but **~170 tests fail for environmental reasons** (hardcoded
`C:\` paths, clang, MSVC). The raw number therefore means nothing on its own: build the merge-base
in a `git worktree`, run both, and compare the sorted FAILURE NAMES with `comm -23`. A count alone
hides a regression that lands as another test goes green. Windows remains the release gate.

## Working conventions — READ THIS, these prevent real mistakes

- **PowerShell is the primary shell.** Use the dedicated tools (Read/Edit/Write/
  Grep/Glob) for files — a PreToolUse hook (`.claude/hooks/prefer-native-tools.js`)
  blocks reflexive `grep`/`cat`/`find`/`sed`/… through the Bash tool.
- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** — it
  corrupts the BOM-less UTF-8 files here (has caused mojibake more than once). Use
  Edit/Write. For a multi-line git commit message, write a file and use `git commit -F`.
- **After AXAML changes, `dotnet clean` before building** — stale build cache causes crashes.
- **`IDE/` is a WINDOWS xcopy drop.** `IDE/BasicLang.exe` is a PE32+ binary — **never refresh it
  from a Linux build**, which would swap the Windows executables for ELF apphosts. Refresh with
  `robocopy <Shell bin> IDE /E` on Windows — **never `/MIR`**.
- **Validate codegen through the CLI *and* the IR optimizer**, not only the non-optimizing
  unit-test helper — the green suite has hidden bugs the optimizer/CLI exposed. Run the CLI,
  or use the optimizer-running test helper (`CompileToCppOptimized` in `CppCollectionTests.cs`).
- **Test both entry points.** The IDE build delegates to the CLI engine
  (`CompileProjectFiles`); a fix verified only through the test helper can still break
  via the IDE or the CLI. Exercise both.
- **Some resolver source is shared across consumers — change it once, not per-consumer.**
  `ModuleResolver.cs` backs both the compiler and the LSP; `ModuleTypeWalker.cs` is shared
  across the compiler and the C++ backend / capability checkers. ⚠ Also MIRRORED rather than shared:
  `ProjectFile.GetSourceFiles`' default glob and `ProjectGlobSafety.MaterialiseGlobbedSources` —
  the second exists to reproduce the first exactly, and a guard added to one and not the other let
  the IDE write an explicit `<Compile>` item for a file the compiler's glob rejects. Once the list
  is explicit, `GetSourceFiles` takes its explicit branch, which does **no** extension filtering.
- ⛔ **`git merge-tree` is not a conflict check here — it has given false negatives repeatedly.**
  It reported branches as clean against master while the real merge conflicted. To answer "will this
  merge", do the merge: `git worktree add --detach <dir> <sha>` (the `--detach` is required, or it
  refuses with *"already used by worktree"*), `git merge origin/master` in it, read the result, then
  remove the worktree.
- ⚠ **Win32 globbing over-matches THREE-character extensions**: `*.bas` matches `.basic` and `*.cls`
  matches `.class`, so both globs above need an exact-extension check or those files are swept twice.
  Not reproducible on Linux — confirm it on the Windows run.

## Compiler layout (`BasicLang/`)

Pipeline: `Preprocessor.cs` → `BasicLangLexer.cs` → `Parser.cs` → `SemanticAnalyzer.cs`
→ `IRBuilder.cs` (`IRNodes.cs`) → `IROptimizer.cs` → backends.
Backends: `CSharpBackend.cs`, `LLVMBackend.cs`, `MSILBackend.cs`, `CppCodeGenerator.cs`
(+ `CppCapabilityChecker.cs`). Resolution/types: `ModuleResolver.cs`,
`ModuleTypeWalker.cs`, `TypeMapper.cs`. LSP: `BasicLang/LSP/` (server +
per-feature handlers, `CompletionService.cs`).

## Form designer (`BasicLang/Forms/`, `VisualGameStudio.Shell/Controls/`)

Two document formats, one reader and one writer. `.blform` (WinForms, absolute pixels) and
`.blwebform` (web, Grid/Flow) share the element grammar and diverge in layout vocabulary and
catalog; `FormDocumentReader`/`FormDocumentWriter` pick the vocabulary from the ROOT element, and a
file whose name disagrees with its root is refused. `FormControlCatalog` is the single source of
truth for which controls exist and what can be set on them — toolbox, property grid, emitters and
the CI gate all read it. **Add a row; never hand-write a `[TestCase]` list beside it.**

The designer writes into two marked regions inside the user's own `.bas` (`RegionWriter`), refusing
rather than overwriting anything hand-edited. Design view is a MODE on the existing code editor, not
a second document type.

- ⛔ **A WinForms catalog row is unfalsifiable without `csc`.** `EnableNetResolution` returns early
  for `UseWindowsForms` and the resolver cannot reach `System.Windows.Forms.dll`, so every
  `Form`/`Button`/`Point` member types as `Object` with no diagnostic — **a misspelled property name
  compiles green**. `WinFormsCatalogSweepTests` generates every control with every property and
  requires csc to accept it; that gate has already caught six wrong catalog rows.
- ⛔ **Never emit `With` from a generator** — the IR builder silently drops every `.Prop = value`
  inside it (see `docs/form-designer-followups.md`).
- ⛔ **Geometry fans in**: `Location = New Point(x, y)` as ONE statement. `Location.X = 96` is
  CS1612, and BasicLang reports nothing because the member degrades to `Object`.
- ⚠ The handler-ordering rule (a handler must precede the region that wires it) is **web-only** —
  measured. On WinForms the same shape compiles, which is why the shipped VSIX template does it.
- The canvas is a **schematic**, not a preview: the IDE has no browser and no WinForms surface. F5
  to the real target is the renderer. ⚠ "Schematic" has never meant "all kinds look alike" —
  `DrawControl` reads `FormSchematic` off the **catalog row**, so a Button, a CheckBox and a Panel
  draw differently. Never switch on `control.Kind` in the canvas: that is a second list of controls,
  and it falls to its default the day a row is added — a new control silently drawing as a plain box.
- ⛔ **The canvas IS testable, and "verified by running the IDE" is no longer true.**
  `Avalonia.Headless` + `Avalonia.Skia` drive the real control and render real pixels;
  `FormCanvasRenderTests` renders every catalog kind and hashes the frames, and killed the
  one-grey-box defect on a mutation. Two of the three designer bugs the owner found in one week were
  in view code that had no tests because this was believed impossible. When you add view behaviour
  here, a headless test is available — use it.
  - ⚠ **Skia is required.** `UseHeadlessDrawing = true` is the default and makes
    `CaptureRenderedFrame` throw: a renderless platform draws nothing to compare.
  - ⚠ **`[AvaloniaTest]`** (Avalonia.Headless.NUnit) runs the body on the dispatcher thread. Without
    it the control constructs on a worker thread and fails in ways that look like product bugs.
  - ⛔ **Give every fixture control the SAME id.** The canvas labels a control `Text ?? Id`, so
    ids like `Button1`/`TextBox1` make each frame differ by its text alone and a rendering test
    passes with the defect fully present.
  - ⚠ A `ListBox` with `Width`/`Height` set is **centred** in its window, so a fixed press point
    lands on empty chrome — a layout problem wearing a routing problem's error message.
- ⛔⛔ **"It compiles" was the ceiling here for the whole feature, and it hid two defects.**
  `WinFormsCompile` says so in its own summary — *compile only, never run* — so until
  `FormDesignerAcceptanceTests` no form this designer produced had ever been EXECUTED on either
  target. Running them found that every web form died on load, and that the WinForms z-order was
  inverted. Both builds were green throughout. **When you change what the designer emits, run it.**
  - ⚠ `Button.PerformClick()` checks `CanSelect`, which needs the control and every parent **visible
    and enabled** — so on a form that was never `Show()`n it silently does nothing and the handler
    looks unwired when it is not.
  - ⚠ The generated C# lands in `namespace GeneratedCode`.
- ⛔⛔ **A thing with no caller is the failure mode here — FIVE separate pieces of this feature were
  complete, unit-tested and unreachable, with a green suite throughout.** `RegionWriter.Write`
  never ran, so a scaffolded form's `InitializeComponent` was never generated; `DispatchSource`
  never ran, so every page's `data-form` was read by nothing; `JavaScriptEmitter.Emit(forms:)` was
  optional and only tests passed it, so no page was ever written; the default project shape emitted
  no pages at all; and `AddNewFormAsync` reached no menu, so **the IDE had no way to create a form**
  — the entry point to the entire designer, found by the owner opening the IDE and asking where it
  was. ⚠ Its `[RelayCommand]` was PRESENT but bound to the wrong method: a doc comment for
  `SaveProjectOrReportAsync` had been inserted between the attribute and the method it was written
  for, and **an attribute binds to the next DECLARATION — an intervening doc comment is trivia**. The
  toolkit generated a `SaveProjectOrReportCommand` nothing binds and no `AddNewFormCommand` at all,
  it compiles either way, and the only symptom is a menu item that cannot exist. Keep the attribute
  adjacent to its method. When you add one, the question is **who calls it in a shipping
  build** — and the answer must be a test that drives the real entry point (`SaveAsync`, the CLI,
  the generated command), not one that constructs the thing. For UI, that test must also read the
  AXAML for the binding: a `[RelayCommand]` no menu binds is as unreachable as an un-attributed
  method. `FormClipboard` is still unreachable; see `docs/form-designer-followups.md`.
- ⚠ **A form document OPENS in the designer.** `MainWindowViewModel.OpenFileAsync` calls
  `CodeEditorDocumentViewModel.EnterDesignModeForFormDocument`; without it a `.blform` opens as raw
  XML with a Design button nobody knows to press, which is how "I can't see the form designer"
  happened with every piece of it working. It is called on the OPEN route only — a later reload must
  not throw a user who chose Code view back into the canvas — and a refused document stays in Code
  view, because its model is empty and Code view is where the problem is visible.
- ⛔⛔ **A retargeted form (`FormRetarget`, `design --retarget`, "Retarget Form…") is a PAIR that
  lives in its OWN directory and is NOT added to the source project.** Document and code-behind
  pair by BASE NAME (`FormCodeBehind.PathFor`) and the class is named after the form, so
  `LoginForm.blwebform` written beside `LoginForm.blform` pairs with the WinForms class already
  there — the designer's next save writes web regions into it — and a second `LoginForm.bas` in
  one project is a duplicate class. `--out` is required and the IDE asks for a folder; neither
  ever overwrites. ⚠ The pixel ⇄ cell edge is DERIVED by a stated rule and reported per control
  (`BL8025`); an unknown attribute the destination would read as layout (`Col` on a `.blform`
  control, `Width` on a `.blwebform` root) is dropped-and-named (`BL8024`), never carried —
  `Create()` writes unknown attributes after the modelled ones, and a stale one would overrule the
  derived position with nothing looking wrong.
- ⛔ **A tray component is a `FormControl` with no place** — `Geometry == null`, no children, no tab
  index, in `FormDocument.Components`, catalog row `IsComponent` — and **every walker chooses its
  lists explicitly**: the region writer, the id namespace (`FindById`, `ListContaining`, duplicate
  ids) and the retarget visit the tray; the canvas, the markup emitter and tab renumbering do not.
  `AllControls()` is the visual tree and never includes components. The reader is the only place
  the invariant is enforced (a component element never acquires geometry or TabIndex; BL8020
  refuses a component under `<Controls>` or a control under `<Components>`). ⛔ Component types
  are emitted FULLY QUALIFIED (`System.Windows.Forms.Timer`, `System.ComponentModel.BackgroundWorker`)
  because a user `Using System.Threading` makes a bare `Timer` CS0104, the C# backend adds that
  using itself whenever the body contains "Thread", and the scaffold never imports
  `System.ComponentModel` — BasicLang silent on all three (measured). ⛔ The web Timer's handler is
  PARAMETERLESS and the emitted call is the TYPED `w.setInterval` over `Dim w As Window = ::window`:
  `Window.setInterval` takes an `Action` and refuses `Action(Of DomEvent)`; the untyped `::window`
  hatch accepts it and would also accept any typo. ⚠ `WinFormsCatalogSweepTests` builds a component
  row into `Components` with no geometry — measured before that change: csc rejects
  `Location`/`Size`/`Anchor` and `Controls.Add` for every component and nothing else, so the gate
  can see a component placed as a control. ⚠ It compiles only the FIRST value of an Enum row;
  `EveryEnumValue_OfEveryControl_…` compiles every member, one control per value.
- ⛔⛔ **The designer has ONE selection store, and every write to the property grid goes through
  it.** `CodeEditorDocumentViewModel.SelectInDesigner` sets `Selection` and the grid together, and
  the constructor makes the grid follow `Selection.Changed`. A drop that wrote the grid ALONE while
  a tray click wrote `Selection` alone let the two disagree after the first click — `Set` is a no-op
  for the control already selected — and the tray's Delete, which passes the GRID's control,
  removed the wrong component with the other one highlighted (found by review, never by a test,
  because the tray tests asserted one store each). Never set `PropertyGrid.SelectedControl` from
  the view model directly.
- ⚠ **"Wired means running" is a catalog rule, not a Timer special case.** A web script component
  runs the moment it is wired; WinForms says that with a property (`FormWebScript.Implies` =
  `Enabled=true`). `FormRetarget` applies it BOTH ways and names it (BL8027) — before it, a wired
  web Timer arrived on the window with `Enabled` absent and never fired, silently. On the web a
  component is wired ONLY through its template on its default event: any other bind is warned
  (BL8028) and excluded from the BL8013 ordering check, and a kind with no web row is warned
  (BL8029) — `RegionWriter.IsEmittedBind` is the one answer the emitter and both checks share.
- ⛔⛔ **A drag that re-parents must exclude the dragged subtree from the search for a target.**
  Two failures, one fix. The pointer is over the control being dragged, so without excluding it a
  non-container swallows its own point and nothing can ever be dragged into anything. And a
  container dropped into ITSELF or its own descendant makes a loop in the tree — which every walker
  here recurses through (`FormDocumentWriter`, `FormCanvasTransform.Layout`, `RegionWriter`,
  `AllControls`), so the symptom is a stack overflow that takes the IDE down with no diagnostic.
  `FormCanvasTransform.ContainerAt` takes an `ignore` for exactly this.
- ⛔ **Canvas drags are tracked in ABSOLUTE form space, not the control's own coordinates.** A
  child's X/Y are relative to its container, so the same point on screen is different numbers on
  each side of a Panel boundary; a drag that re-bases halfway through jumps. `MoveToForm` converts
  once, into whichever container the control landed in. `MoveTo` (parent-relative, no reparent) is
  the other one — don't mix them up.
- ⛔ **A `.blwebform` is laid out by CELL, and the canvas grid is an approximation that says so.**
  `Cols`/`Rows` are CSS `grid-template-*` lists kept verbatim because the browser is the renderer;
  `FormGridLayout` honours `fr` and `px` and draws everything else (`auto`, `%`, `calc()`) as one
  flexible share. It shows WHICH CELL a control is in — never what the page will look like.
  ⚠ `FormAssetEmitter.Tracks` and `FormGridLayout.ParseTracks` are a MIRRORED pair: both split the
  track list on commas, and a canvas that split differently would draw a grid the page does not
  have. Change them in the same commit (`docs/form-designer-followups.md` 16).
- ⚠ **`SurfaceSize` on `FormCanvasTransform` is the ONE answer to "how big is the form".** Four
  places had their own `Width ?? 400, Height ?? 300` and the web grid would have been a fifth; a
  drifted copy puts the grid lines somewhere other than the page they divide, so a drop lands in a
  cell the user did not aim at with nothing looking wrong.
- ⛔ **Ask the CATALOG what a value means, never the shape of the string.** A `Type.Member` regex
  used to decide "is this already source?" and was wrong in both directions:
  `Text="config.json"` emitted unquoted (form stops building), and
  `TextAlign="ContentAlignment.Bogus"` sailed past the Degraded check into CS0117 with no
  diagnostic. `FormPropertyDef.IsSourceForm` is the answer.
- ⛔⛔ **A GREEN BUILD IS NOT A RUNNING PAGE.** The D7 dispatch was generated from a
  `Public Module`; it compiled clean, every string a test looked for was there, and every page died
  on load with `ReferenceError: VgsForms is not defined` — the JavaScript backend **flattens a
  module's members to bare globals while emitting the call site qualified**, so the script referenced
  an object that appears nowhere in the file. No string assertion can see that. `FormBuildEmissionTests`
  now RUNS the emitted script under node. The dispatch is generated as `Public Class` + `Public Shared
  Sub`, which emits a real `class` with a `static` member. The backend bug itself is UNFIXED
  (`docs/form-designer-followups.md` 14) and will bite anyone calling a module across files.
- ⛔⛔ **An UNQUALIFIED call to the enclosing class's own method is a runtime `ReferenceError` on the
  JavaScript backend** — it emits a **bare global** where it must emit `this.M()`. Green build,
  *"Compilation successful!"*, dead page. `Me.M()` emits `this.M()` and is correct.
  ⚠ **This is BROADER than the spec's Measured-facts row**, which scoped it to *a lambda* calling an
  unqualified method. Measured 2026-09-18 from an ordinary **constructor**, no lambda involved:
  `Unqualified()` → `Unqualified();`, `Me.Qualified()` → `this.Qualified();`. Any unqualified
  self-call in a class is affected. ⚠ The `Me.` workaround holds only OUTSIDE a lambda — inside one
  the spec measured `Me.` hard-erroring, so the two rows are both true and neither generalises.
  ⛔ Every scaffolded web form carried exactly this shape (`InitializeComponent()` in `Public Sub
  New()`) and **every one of them was dead on load**; nothing caught it because no test ran a
  generated FORM, only the dispatch. `FormScaffolder` now emits `Me.InitializeComponent()`
  (`FormScaffolderTests.TheScaffoldQualifiesItsInitializeComponentCall`). The backend defect is
  UNFIXED and hits hand-written user code.
- ⚠ A bare top-level `Sub` in one `.bas` is not callable from another at all
  (*"no lowering for 'Helper.Helper'"*). Between that and the above, **a class is the only shape that
  works across files on the JavaScript backend** — both are compiler gaps, not designer ones.
- ⛔⛔ **A MODULE member call is broken on all three backends, in DIFFERENT directions** — measured
  by generating, compiling and RUNNING the same eight-line program on each:

  | | `M.Go()` qualified | `Go()` unqualified |
  |---|---|---|
  | JavaScript | builds clean, `ReferenceError` at RUN time | ✅ |
  | C# | ✅ | `CS0103: The name 'Go' does not exist` |
  | C++ | clang: `undeclared identifier 'M'` | ✅ |

  **No spelling works everywhere**, so a program calling a module member is silently locked to a
  subset of targets. JS/C++ hoist members to bare top-level functions
  (`IRBuilder.Visit(ModuleNode)`, `IRBuilder.cs:385`) and emit the call qualified anyway; C# gets the
  container right (`public static class M`) and breaks the unqualified call instead. Repro, matrix
  and per-backend fix shapes in `docs/form-designer-followups.md` 14. **PR #6 fixes JavaScript
  only** — C# and C++ still need theirs.

## BasicLang language

VB-like syntax; classes / interfaces / modules; generics; pattern matching (`When`
guards); LINQ; Async/Await; conditional compilation (`#If`/`#IfDef`/`#Else`/`#EndIf`);
multi-file projects (Import/Using); .NET interop via `Using`; five backends. Source
files: `.bas` (also `.mod`, `.cls`). In a `.cls` file, a
first code-line `Option Public` marks the implicit class public (legacy bare `Public`
still works but warns).

## C++ backend (`CppCodeGenerator.cs` ↔ engine)

- **Two-layer std:** collections lower to `std::shared_ptr<BasicLang::List<T>>`
  (**reference** semantics, matching .NET — value wrappers diverged and were wrong);
  `String`/structs stay values. Foreign C++ via `#CppInclude` / `::`. Targets `-std=c++20`.
- **Reference vs value:** classes/interfaces → `shared_ptr<T>` + `make_shared` + `->`;
  `Structure` → value `struct`. Generics → real C++ templates.
- Exceptions via the `IRThrow` node (known limitation: a `Return` inside a `Try` bypasses
  its `Finally` on the C++ backend); iterators are real C++20 coroutines (`Generator<T>` /
  `co_yield`); async is synchronous `Task<T>` emulation (no scheduler).
- Plans/specs in `docs/superpowers/`. Known gap: broad .NET API surface
  (List/Console/String methods) on the C++ backend —
  `docs/superpowers/specs/2026-07-07-cpp-backend-preexisting-gaps.md`.

## Engine ⇄ wrapper sync invariant

Every `__declspec(dllexport)` in `VisualGameStudioEngine/framework.h` needs a matching
`<DllImport>` in `RaylibWrapper/RaylibWrapper.vb` — `extern "C"`, `__cdecl`, `LPStr`
string marshaling. The export count is in the thousands and drifts — grep to confirm,
never trust a cached number. On a successful build the compiler auto-injects the
wrapper reference and deploys the native DLL for game apps.

## Where things live (don't duplicate it here)

- **On a machine with no auto-memory** (a cloud session, a fresh clone) → start at
  `docs/HANDOFF.md`: the in-repo snapshot of state, gates and traps. The auto-memory
  below lives outside the repo and does not travel.
- **Current work / status** → auto-memory `MEMORY.md` (loaded each session)
- **History / rationale** → `git log` and `docs/superpowers/{plans,specs}/`
- **Authoritative engine API** → `framework.h` + `RaylibWrapper.vb` (+ `docs/`)
- **Per-area subagent guides** → `BasicLangAgent/`, `IDEAgent/`, `EngineAgent/`,
  `VSExtensionAgent/` each have a focused CLAUDE.md
