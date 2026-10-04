# Prompt — Ship the dual-target Visual Form Designer to 100%

> Paste everything below the line into a fresh Claude Code session opened on this repo.
> It is written to be self-contained: it names the artifacts to read, the bar for "done",
> the traps that make a green build lie, and the gates that are not optional.

---

## Your task

Ship the **Visual Form Designer** in this repository to **100% working completion**, for **both**
project targets, with drag-and-drop direct manipulation that feels like the Visual Studio form
designer.

The end state a user must be able to reach, without touching a text editor:

1. Create a new form in the IDE's Solution Explorer.
2. Drag controls from a toolbox onto a design surface — move them, resize them with grab handles,
   multi-select them, align them, nudge them with arrow keys, set their properties in a property grid,
   double-click one to get a handler stub.
3. Press F5 and have that form run — **as a real Windows Forms window** in a `.NET` project, and **as a
   real HTML/CSS/JavaScript page in a browser** in a web project, from the same designer surface and
   the same control grammar.

That second point is the whole feature. One designer, two outputs.

## Do not start by designing. The design already exists and is signed off.

This work has a complete, adversarially-verified design spec and a task-level implementation plan
already in the repo. They were written after a 31-agent verification pass and a 376-citation audit,
and all four open owner questions were answered on 2026-09-11.

Read these four, in this order, **before writing a line of code**:

| File | What it is |
|---|---|
| `docs/HANDOFF.md` | Current repo state, the standing test baseline, live traps |
| `CLAUDE.md` | Working conventions — shells, file encoding, build/test commands, the engine⇄wrapper invariant |
| `docs/superpowers/specs/2026-09-11-visual-form-designer-design.md` | The design. Decisions **D1–D13**, each with its rejected alternatives; both document formats shown in full; *Measured facts* records latent compiler defects that will silently miscompile generated code |
| `docs/superpowers/plans/2026-09-11-visual-form-designer.md` | Tasks 1–19 across Slices 0–4, each with its files, steps and gate |

**The spec and the plan are the contract.** Do not redesign them, do not relitigate the owner
decisions, and do not "improve" a decision because a different approach looks cleaner from inside one
task — every `Dn` records why the alternative was rejected. If you find a decision that is genuinely
wrong *because of something you measured*, stop, state the measurement, and ask before deviating.

**Current state: zero code is written.** `BasicLang/Forms/` does not exist and nothing in the solution
references `.blwebform`, `.blform`, `FormCanvas` or `FormDesigner`. You are starting at Task 1.

## What the plan already covers — work it as written

Slices 0 through 4, Tasks 1–19. Highlights you must not skip:

- **Slice 0** — `ProjectSerializer` stops destroying what it does not understand; the wizard's
  `TargetFramework` actually reaches the file; the two remaining silent-C# backend defaults throw.
- **Slice 1** — the form model in `BasicLang/Forms/`, the two-dialect recognizer, the `BL8xxx`
  diagnostic band and `basiclang design --check`, and a read-only canvas.
- **Slice 2** — `.blwebform` schema + structure-preserving reader/writer, extension registration on
  **both** compile routes, the region writer, the build-time markup/CSS emitter and F5.
- **Slice 3** — `.blform` schema, the WinForms control catalog and its CI gate, WinForms region writing.
- **Slice 4** — full suite, `IDE/` drop refresh, docs, and the follow-up defects filed as separate chips.

## What the plan does NOT cover — add it as Tasks 20–28

The plan builds a canvas that renders and hit-tests, a toolbox and a property grid. It does **not**
specify the direct-manipulation layer, and it does not specify how one form reaches two targets. Both
are required by the goal above. Add them as new tasks; keep the existing numbering intact.

### ⛔ Canvas rendering — owner decision, amending the plan's Task 7

**The spec's schematic canvas stands.** No proxy controls, no hosted browser, no hosted WinForms. The
canvas remains `FormCanvasControl : Control` overriding `Render(DrawingContext)` as Task 7 specifies.

**But it is a *property-faithful* schematic, not a wireframe.** Every visual property the user sets
must change how the shape draws:

- Draw from the **document's property values**, never from a fixed designer style. `BackColor`,
  `ForeColor`, `Font` (family, size, style), `Text`, `TextAlign`, `BorderStyle`, `Visible` and
  `Enabled` on `.blform`; the CSS equivalents on `.blwebform`. Set a control's background to red and
  the box on the canvas is red.
- Render text with Avalonia `FormattedText` using the control's **real font**, so text genuinely
  measures. A caption that does not fit its box must visibly clip or overflow on the canvas. This is
  the single thing a bare wireframe cannot show, and it is the reason this amendment exists.
- Each catalog row **declares which of its properties are visual**, so the renderer switches on data,
  not a hand-written `if` per control kind.
- ⛔ **This is still not WYSIWYG and must not be described as such.** Avalonia's text stack is not
  GDI+ and not the browser's; metrics will differ. Label the surface as a schematic in the UI, keep F5
  as the renderer of record, and do not let that claim drift as fidelity improves.

### Task 20 — Direct manipulation on the canvas

Everything here goes through the **one** shared transform object mandated by Task 7 (`Render`,
hit-testing and selection must never compute their own copy — the minimap's three hand-duplicated
transforms are the anti-pattern the plan calls out).

- Drag a control from the toolbox onto the canvas to create it; click-then-draw to create at a size.
- Drag a selected control to move it. Drag an 8-point grab-handle frame to resize it.
- Rubber-band (marquee) multi-select; Shift/Ctrl click to extend a selection; Esc to clear.
- Arrow keys nudge by 1px, Shift+arrows by the grid step. Delete removes the selection.
- Snap lines and a snappable grid on the WinForms pixel canvas; snap to grid cells and gutters on the
  web Grid/Flow canvas. The two layout vocabularies are **D3** and are deliberately different — do not
  unify them into pixels.
- Z-order commands (bring to front / send to back) writing child order in the document.
- Align and make-same-size commands over a multi-selection.
- Ctrl+C / Ctrl+X / Ctrl+V using the **serialize-subtree / deserialize-subtree-with-rename** that
  Task 4 requires you to build on day one. Paste must rename to avoid `Id` collisions.
- **Undo/redo** covering every one of the above, integrated with the IDE's existing undo stack so a
  designer gesture and a Code-view edit do not desynchronize.

Gate: `dotnet clean` first (AXAML). Fast subset plus the `Shell` suites. There is no
`Avalonia.Headless` reference in this solution, so the gestures themselves are verified by **running
the IDE** — say exactly that, and do not claim automated coverage you do not have. Put the logic that
*can* be tested headlessly (hit-testing, snap resolution, selection algebra, the undo stack, subtree
copy/paste rename) behind a plain class and unit-test it properly.

### Task 21 — One form, two targets

`.blwebform` and `.blform` are two formats by deliberate decision (**D2**), sharing one control
grammar. Make that shared grammar do the work the goal demands:

- One designer surface, one toolbox, one property grid, driven by the shared grammar — the target
  decides the layout vocabulary, the catalog and the emission, not the UI.
- A **retarget** operation: convert a form between `.blwebform` and `.blform`, mapping the shared
  properties losslessly, reporting every property that cannot cross with a `BL8xxx` diagnostic rather
  than dropping it silently. Absolute-pixel ⇄ Grid/Flow is the hard edge; make the loss explicit and
  reviewable, never silent.
- Round-trip test the retarget both directions and assert the shared subset is byte-stable.

### Task 22 — The double-click gesture

Double-clicking a control in the Visual Studio designer creates or navigates to its default handler.
Implement it: create the stub if absent, navigate to it if present, and respect **D8's ordering rule** —
handlers are emitted **before** the region that wires them, on **both** targets.

### Task 23 — The control catalog, widened

The plan specifies ten control kinds. Ten is a proof of concept, not a designer. Widen both catalogs
toward the common-controls set a user expects to find in a toolbox — on the WinForms side the usual
`Label`/`TextBox`/`Button`/`CheckBox`/`RadioButton`/`ComboBox`/`ListBox`/`GroupBox`/`Panel`/
`PictureBox`/`NumericUpDown`/`DateTimePicker`/`ProgressBar`/`TabControl`/`TrackBar`/`ListView`/
`TreeView`/`DataGridView`/`SplitContainer`/`FlowLayoutPanel`/`TableLayoutPanel` tier, and on the web
side their honest HTML equivalents.

- Drive it from the **one source-of-truth table** the plan already mandates (`ProjectTemplateBackendMappingTests.cs:25-33`'s
  *"add a row, never widen the default"* shape), with a completeness guard that **fails** on a missing row.
- The plan's Task 17 CI gate — generate every catalog control with every property set and require the
  real CLI to exit 0 — is `TestCaseSource`-driven off that table, so it scales with the catalog for
  free. ⛔ It is also the **only** thing standing in for a type system here: `EnableNetResolution`
  returns early for `UseWindowsForms`, so every `Form`/`Button`/`Point` member access types `Object`
  with no diagnostic. A catalog row with a misspelled property name is invisible without that gate.
- Where the web equivalent is not honest — a `DataGridView` has no single HTML tag — say so in the
  catalog rather than faking it, and let the retarget of Task 21 report it as explicit loss.

**Every catalog row carries its full property set**, because the property grid must show what the real
control has — `Name`, `Text`, `BackColor`, `ForeColor`, `Font`, `Size`, `Location`, `Enabled`,
`Visible`, `TabIndex`, `Anchor`, `Dock` and the rest, per control kind. Each property declares:

| Field | Why |
|---|---|
| Name and type | Drives the editor and the emitted statement |
| **Default value** | See the serialization rule below |
| **Visual or not** | Feeds the property-faithful renderer above |
| Editor kind | String, bool, enum dropdown, integer, `Color`, `Font`, `Point`/`Size`, `Anchor`/`Dock` |

- ⛔ **Write only non-default values** — to the document *and* to the region. A form that emits every
  property of every control produces an unreadable region and a bloated document. This is exactly what
  VS does with `DefaultValue`/`ShouldSerialize`; drive it from the catalog's declared defaults.
- ⛔ **Avalonia 11.3 ships no ColorPicker and no font dialog.** The plan already says to reuse
  `VisualGameStudio.Editor/Controls/ColorPickerPopup.cs:15` for colour rows and to price every other
  type editor as hand-built. With a full property set a **`Font` editor is now required, not
  optional** — budget for it.
- ⛔⛔ **The property list is unfalsifiable data, and this is the highest-risk part of the task.**
  `EnableNetResolution` returns early for `UseWindowsForms` (`Compiler.cs:145`), so a misspelled
  property name in the catalog types as `Object`, compiles green, and silently does nothing at
  runtime. The plan's Task 17 CI gate — generate every catalog control with every property set and
  require the real CLI to exit 0 — is the **only** thing that catches this, and it must now cover
  **every property in the grid**, not merely every control. Drive it from the catalog via
  `TestCaseSource`.
- Include the **Events tab** — VS's lightning-bolt list. It is the same catalog data, it feeds Task
  22's double-click gesture, and double-clicking a row there creates the handler the same way.

### Task 24 — Menus, toolbars and status bars

⛔ **This one is blocked on a compiler change. Do that first, as its own gated task.**

The spec scopes menus out of v1 for a measured reason: the canonical idiom
`menuStrip.Items.AddRange(New ToolStripItem() { … })` **is not expressible in BasicLang today**.
`New` parses a type reference plus an optional *positional* argument list and returns, on **both**
`New` paths (`Parser.cs:4298`; `Dim x As New T(…)` at `:2504-2524`) — so array-creation-with-initializer
does not parse. A bare-brace array literal *does* exist (`Parser.cs:4446-4461`), so `AddRange({a, b})`
parses, but element typing is **exact-equality with no base-class widening**
(`SemanticAnalyzer.cs:6056-6061`), so a menu mixing `ToolStripMenuItem` and `ToolStripSeparator`
degrades to `Object[]` plus a warning.

Fix one of the two, gate it, and only then build the designer surface:

- **Preferred:** give array-literal element typing a common-base-type widening rule, so
  `{mnuFile, sep1}` types as `ToolStripItem()`. Smaller and more generally useful than new syntax.
- **Alternative:** support `New T() { … }` array-creation-with-initializer in the parser.

⛔ Either is a **`SemanticAnalyzer`/`Parser` change reaching shared compiler machinery — full suite
required**, and element typing is used well beyond menus, so check what else moves.

Then the designer work: a menu/toolbar/status-bar editor is a **nested, non-positional tree**, not a
positioned box on the canvas, so it needs its own in-place editing surface (the VS "Type Here" strip).
On the web side, emit honest markup — a `<nav>`/`<ul>` menu and a status bar element — not a WinForms
menu drawn in HTML.

### Task 25 — The component tray

Non-visual components (`Timer`, `ToolTip`, `ErrorProvider`, `BackgroundWorker`, and their web
equivalents) belong in a tray strip below the design surface, exactly as VS does it — they are part of
the form but have no position on it.

- The `<Components/>` slot is **already reserved and empty in both formats**, so this is a designer
  surface plus emission, not a format break.
- Components are selectable, appear in the property grid, and participate in undo/redo and delete.
- Emission follows the same region rules as controls: declare field → construct → set properties →
  wire handlers. ⛔ Same trap list applies — never `With`, never `Handles`, handlers before regions.

### Task 26 — The Anchor/Dock visual picker

The VS property-grid widget where you click the edges of a little box to set `Anchor`, and the
nine-region picker for `Dock`. Small, self-contained, and it is one of the most recognisable pieces of
the VS designer.

- It is a property-grid **row editor**, so it slots into the typed-row editor the plan's Task 14
  extracts from the Settings dialog (`SearchableSettingItem` + `SettingControlKind`) rather than being
  a new surface.
- ⛔ `Anchor`/`Dock` are `.blform` vocabulary (**D3**). The web side has no equivalent and must not
  grow a fake one — the picker is hidden for `.blwebform`, and Task 21's retarget reports the loss.
- Avalonia 11.3 ships no such control; price it as hand-built, like every other type editor here.

### Task 27 — End-to-end acceptance, both targets

Two scripted walkthroughs, each performed against a **real build**, not a unit-test helper:

- **Web:** new web project → new form → drag a Label, a TextBox and a Button → set properties →
  double-click the Button → write one line in the handler → F5 → the page loads in the browser at
  `/<StartupForm>.html`, the controls render, and the click handler fires.
- **WinForms:** new `.NET` project → the same gestures → F5 → a real window appears with the controls
  laid out where the designer put them, and the click handler fires.

Record what you actually observed. Screenshots or captured stdout, not a claim.

### Task 28 — Closeout

Fold into the plan's Task 19: full suite, `IDE/` drop refresh, `docs/HANDOFF.md` and `CLAUDE.md`
updated, follow-up defects filed as their own chips.

## Non-negotiable traps

These are measured, recorded in the spec's *Measured facts*, and every one of them produces a **clean
build that does the wrong thing**. A green test suite does not protect you from any of them.

1. **Never emit `With … End With`.** `IRBuilder.cs:3356` has no final `else` — every `.Prop = value`
   inside one is silently dropped. The form builds and has no properties set.
2. **Never emit `Handles`.** Hard parse error at `Parser.cs:4464`; the file never builds.
3. **Never emit `AddHandler` against a DOM receiver.** `JavaScriptBackend.cs:2182` emits
   `{recv}.add(handler)` unconditionally → runtime `TypeError`.
4. **Emit handlers BEFORE the region that wires them.** `AddressOf` to a later-declared `Sub` erases
   parameter types to `Action(Of Object)`. This is the opposite of the shipped WinForms template's
   order, and handlers-first is correct for **both** targets.
5. **Geometry fans in on WinForms.** `X="96" Y="80"` emits **one** statement,
   `btnLogin.Location = New Point(96, 80)`. `Location` returns a struct; assigning `.X` fails at `csc`
   and BasicLang will not catch it.
6. **Any PascalCase name without an underscore types as `Object` with no diagnostic**
   (`SemanticAnalyzer.cs:2395-2400`, `:7920`). Wrong property names, missing handlers and undeclared
   DOM members all compile green. **Every correctness claim needs a CLI gate, not a unit test.**
7. **`Text` and `TextDocument` are two hand-synced stores** and `SaveAsync` writes the string
   (`CodeEditorDocumentViewModel.cs:618`, `:701-720`). A writer touching `TextDocument` alone saves
   stale text to disk. Go through `Text`.
8. **Three web-target shapes build green and fail at runtime:** a lambda calling an unqualified method
   of the enclosing class; `Me.Method()` inside a lambda; a qualified module call. The spec lists the
   correct emission for each.
9. **Assembly placement is load-bearing (D5).** Model, readers/writers, recognizer and markup emitter
   go in `BasicLang/Forms/` because `basiclang build` must reach them with no IDE present. The canvas
   goes in `VisualGameStudio.Shell`. `VisualGameStudio.Editor` **cannot** see them. Putting the model
   in `VisualGameStudio.Core` looks natural and is wrong.
10. **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** — it corrupts the
    BOM-less UTF-8 files here. Use the editing tools. For a multi-line commit message, write a file and
    `git commit -F`.

## Gates

```
dotnet build BasicLang/BasicLang.csproj -c Release
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
```

- **Baseline:** 5826 tests, 4 known-failing, all pre-existing. Fast subset ≈ 4897/2/1. No re-baseline.
- **Gate proportionally.** Each task runs the fast subset plus its own touched suites and **states
  which it ran and why**. The full suite is required for the plan's Task 15, Tasks 17–18, and closeout.
- ⛔ **The fast subset is not a gate for codegen work.** Execution tests are `[Category("Integration")]`.
  Anything that changes what is emitted must also run its Integration fixtures **and a real CLI build**.
- ⛔ **A "Passed!" line does not mean the suite passed.** Capture both streams and check the total.
- ⛔ **Exit 0 is not a gate when the deliverable is files.** Assert the files exist, carry the expected
  ids, and contain no unlowered BasicLang (`End Sub`) or unsubstituted `$safeprojectname$` placeholders.
- ⛔ **Test both entry points.** The IDE build delegates to the CLI engine; a fix verified only through
  the test helper can still break via `BasicLang.exe build X.blproj` or via the IDE's `BuildService`.
- ⛔ After AXAML changes, `dotnet clean` before building.
- **Validate codegen through the CLI and the IR optimizer**, not only the non-optimizing unit-test
  helper. The plan's Task 8 adds `CompileToCSharpOptimized` because the C# backend has no such helper
  today — every existing `CompileToCSharp` fixture is green on IR that never ships.

## How to work

- Work the plan **in order**. Slice 0 before Slice 1; the read-only canvas before any write; web before
  WinForms (owner decision). Tasks 20–22 land after the canvas and toolbox exist.
- Tick the plan's `- [ ]` checkboxes as you complete steps, and commit per task with a message naming
  the task and the gate you ran.
- One task, one commit, one gate. Do not batch four tasks and run the suite once at the end.
- If a task's premise turns out to be false when you measure it, **say so and stop** rather than
  working around it quietly — several of this plan's traps exist because an earlier assumption was
  overturned by measurement.
- Do not add NuGet packages and do not add project references beyond the one Task 4 explicitly allows
  after verifying the transitive path.

## Reporting

For every task, state:

- what you changed, by file;
- which gates you ran, with the actual totals;
- what you verified by running versus what you verified by test;
- anything you could not verify, named plainly.

**Never report a task complete on the strength of a build succeeding.** In this codebase a clean build
is compatible with a form that sets no properties, a handler that is never wired, and a page that
throws `ReferenceError` on load. stdout from a real run is the only oracle that counts.

## Definition of done

- [ ] Plan Tasks 1–19 complete, checkboxes ticked, each with its stated gate run and recorded.
- [ ] Tasks 20–28 complete.
- [ ] The canvas is property-faithful: changing `BackColor`, `Font`, `Text` or `ForeColor` in the
      property grid visibly changes the shape on the canvas, and an oversized caption visibly clips.
- [ ] The property grid shows the full declared property set per control, writes only non-default
      values, and every property in it is covered by the Task 17 CLI gate.
- [ ] Both acceptance walkthroughs performed and recorded: a web form running in a browser, a WinForms
      form running as a window, each built by dragging controls onto the designer.
- [ ] Retarget works both directions with explicit, diagnosed loss.
- [ ] Full suite at the standing baseline — 5826 / 4 known failures — plus this work's additions, with
      no new failures.
- [ ] `IDE/` drop refreshed (`robocopy <Shell bin> IDE /E`, never `/MIR`; verify with
      `IDE/BasicLang.exe new --list`, not timestamps; `IDE/lib/js/dom-core.bli` is load-bearing).
- [ ] `docs/HANDOFF.md` and `CLAUDE.md` updated; follow-up compiler defects filed as their own chips.
