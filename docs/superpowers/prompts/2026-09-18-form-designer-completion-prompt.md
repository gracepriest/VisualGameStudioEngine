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

## What the plan does NOT cover — add it as Tasks 20–24

The plan builds a canvas that renders and hit-tests, a toolbox and a property grid. It does **not**
specify the direct-manipulation layer, and it does not specify how one form reaches two targets. Both
are required by the goal above. Add them as new tasks; keep the existing numbering intact.

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

### Task 23 — End-to-end acceptance, both targets

Two scripted walkthroughs, each performed against a **real build**, not a unit-test helper:

- **Web:** new web project → new form → drag a Label, a TextBox and a Button → set properties →
  double-click the Button → write one line in the handler → F5 → the page loads in the browser at
  `/<StartupForm>.html`, the controls render, and the click handler fires.
- **WinForms:** new `.NET` project → the same gestures → F5 → a real window appears with the controls
  laid out where the designer put them, and the click handler fires.

Record what you actually observed. Screenshots or captured stdout, not a claim.

### Task 24 — Closeout

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
- [ ] Tasks 20–24 complete.
- [ ] Both acceptance walkthroughs performed and recorded: a web form running in a browser, a WinForms
      form running as a window, each built by dragging controls onto the designer.
- [ ] Retarget works both directions with explicit, diagnosed loss.
- [ ] Full suite at the standing baseline — 5826 / 4 known failures — plus this work's additions, with
      no new failures.
- [ ] `IDE/` drop refreshed (`robocopy <Shell bin> IDE /E`, never `/MIR`; verify with
      `IDE/BasicLang.exe new --list`, not timestamps; `IDE/lib/js/dom-core.bli` is load-bearing).
- [ ] `docs/HANDOFF.md` and `CLAUDE.md` updated; follow-up compiler defects filed as their own chips.
