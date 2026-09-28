# Task 14 pre-flight: the Canvas acceptance twin

Plan: `docs/superpowers/plans/2026-09-27-web-pixel-layout.md`, "## Task 14". Spec: `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §7 ("Edge headless layout check", "End to end").
Checked against `feat/web-pixel-layout` @ **`01f1db30`** (Tasks 0–13 landed, incl. the Task 13 review). Read-only; nothing in the repo changed except this file.

Follow this document **instead of** the plan's Task 14 section where the two disagree. **OWNER decisions needed: none.**

---

## A. Anchors (claim → today's file:line at 01f1db30 → verdict)

| # | Claim | Today | Verdict |
|---|---|---|---|
| 1 | The Grid walkthrough is pinned (Task 5) | `FormDesignerAcceptanceTests.cs:114-115` — `FormScaffolder.Create("LoginForm", target, FormLayoutKind.Grid)` | holds; the twin calls `Create("LoginForm", FormTarget.Web)` with NO layout argument, so it takes the default |
| 2 | New web forms scaffold as Canvas | `FormScaffolder.cs:104-105` default `webLayout = FormLayoutKind.Canvas`; `:118-127` 800×450, `MobileBreakpoint` = the default 600 | holds — 800 > 600, so the design size and anything wider are desktop (no Task 13 B1 problem) |
| 3 | Drops through `vm.PlaceControl` | `CodeEditorDocumentViewModel.cs:894`; `FormPlacement.Place` pixel path `:102-143` (containment by `FormCanvasTransform.ContainerAt` over the DESIGNER dock layout, child coordinates relative to the container, ids `Kind1`) | holds |
| 4 | A MenuStrip "placed through `PlaceControl`" | strips are top-level, `Dock` from the row's default, point ignored (`FormPlacement.cs:83-97`) | holds; its one item goes through the real Type Here commands (`BeginTypeHere`/`CommitTypeHere`/`CancelTypeHere`, `:333-402`) |
| 5 | "Sets properties through the real grid" | the existing helper `SetProperty` (`FormDesignerAcceptanceTests.cs:168-178`) writes `vm.PropertyGrid.SelectedControl` directly | see C1 |
| 6 | Anchor/Dock rows take `StringValue` | `FormPixelPageRealViewTests.cs:330-331` (`"Top, Right"`, `"Bottom"`) | holds |
| 7 | Double-click wires | `ActivateControlAsync` `:726-773` → `ActivateControlCommand` | holds |
| 8 | Real CLI JS build, `BL8018` absent | as the Grid walkthrough `:194-224` | holds |
| 9 | `RunPageUnderNode` | `FormDesignerAcceptanceTests.cs:277` (`clickId` clicks one element, prints `NO ELEMENT` on a miss) | holds — see B1 on ORDER |
| 10 | Task 13 harness | `EdgeLayoutHarness.Measure(siteDir, cases)` `:178` (skips without Edge via `TestSkip.IgnoreEvenInsideMultiple`), `EdgeCase.Of` `:40`, `Differences` ±1 `:115`; `PixelLayoutModel.Rects` `PixelLayoutModel.cs:25` | holds; `Measure` accepts ANY built site directory, so the CLI output of the twin's own project is measured directly (no `BuildSite`) |
| 11 | WinForms reference | `WinFormsReferenceHarness.Measure(workDir, fixtures)` `:135`; retargets through `FormRetarget.ConvertToPair`, which writes a stub for every crossed handler (`FormRetarget.cs:14`, `:139-147`) | holds — see B2 |

## B. Blockers (the plan text, followed literally, breaks)

### B1: node must run BEFORE Edge
`RunPageUnderNode` imports `Directory.GetFiles(outDir, "*.js").FirstOrDefault()`; `EdgeLayoutHarness.Measure` writes `vgs-measure.js` into the same directory. Run in the other order, node may import the measuring script (which touches `parent`) instead of the app. **Correction:** build → node → Edge → WinForms.

### B2: the WinForms reference must not write into the twin's own directory
`WinFormsReferenceHarness.Measure` writes `<workDir>/<Form>.bas` from the retargeted pair; the twin's `_dir` already holds the designer's `LoginForm.bas`. **Correction:** `workDir = _dir/winforms`.

## C. Corrections and decisions (not blocking)

- **C1. Selection through the ONE store.** CLAUDE.md: never set `PropertyGrid.SelectedControl` from outside the store. `SelectInDesigner` is private; the public store is `vm.Selection`, and the constructor makes the grid follow `Selection.Changed` (`:1114-1116`). The twin selects with `vm.Selection.Set(control)` and asserts the grid now shows that control before touching a row. (The Grid walkthrough's helper is left as it is — out of scope; noted.)
- **C2. The designed form.** MenuStrip (+ a "File" item) → a Panel dropped at (100, 100), then `Dock = Fill` → Label (24, 48), TextBox (120, 48), Button (120, 96) dropped OVER the Fill panel, so they land as its CHILDREN at (24, 24), (120, 24), (120, 72) — the drop's containment walk over the resolved dock layout is exercised too. TextBox `Anchor = "Top, Left, Right"` (stretches), Button `Anchor = "Top, Right"` (moves). Ids are the designer's own deterministic `MenuStrip1`, `Panel1`, `Label1`, `TextBox1`, `Button1`.
- **C3. Measured.** Edge at 800×450 (design) and 1000×600 (wider). Each against the model (`PixelLayoutModel.Rects` of the SAVED document read back from disk — what the CLI built) and against the WinForms window (`design`, `grow` 1000×600) of the same saved document retargeted. Plus: no page error in Edge, form area at (0, 0), the MenuStrip exactly 24 tall, Panel1 = the client area below the strip, and the explicit anchored positions (so a model/harness defect cannot make both sides agree vacuously).
- **C4. The handler.** Proven the way the Grid walkthrough proves it: node, `clickId: "Button1"` → `HANDLER FIRED`, and no `LOAD ERROR` / `ReferenceError` / `CLICK ERROR` / `NO ELEMENT`. (Edge's `--dump-dom` cannot send input; the real-browser load is proven by Edge reporting no page error.)
- **C5. Skips.** node absent → `Assert.Ignore` (outside `Assert.Multiple`, as the Grid walkthrough); Edge absent / not Windows → `EdgeLayoutHarness.Measure` skips; WinForms off Windows / no dotnet → `WinFormsReferenceHarness.Measure` skips. On this machine the twin must RUN (0 skipped).

## D. Steps
1. Commit this file alone.
2. Add `Web_Canvas_DesignedForm_BuildsRunsAndLaysOut` (`[Category("Integration")]`) to `FormDesignerAcceptanceTests`; build; run it; read counts AND skips.
3. Mutations (Edit, rebuild, run, restore): (a) scaffold default → Grid; (b) `FormPlacement.Place` refuses Canvas (sends it down the Grid path); (c) the emitter's Canvas arm dropped; (d) `Dock=Fill` ignored in the emitted CSS. Each must turn the twin red.
4. Gate: fast subset vs 01f1db30 (8321; the five known names) + `FormDesignerAcceptanceTests`, `FormComponentAcceptanceTests`, `FormMenuAcceptanceTests`, `FullyQualifiedName~PixelLayout`; compare sorted failure NAMES.

---

### Execution notes (owner's Windows 11 machine, Edge 154, display scale 1.0)

**First run green, 0 skipped, 7 s** (design + CLI build + node + Edge + one WinForms build/run). No production change was needed. Edge, the model and the WinForms window agree EXACTLY (0px), at both sizes:

| Control | 800×450 (design) | 1000×600 (wider) |
|---|---|---|
| MenuStrip1 | (0, 0, 800×24) | (0, 0, 1000×24) |
| Panel1 (Fill) | (0, 24, 800×426) | (0, 24, 1000×576) |
| Label1 | (24, 48, 100×23) | (24, 48, 100×23) |
| TextBox1 (Top,Left,Right) | (120, 48, 100×23) | (120, 48, 300×23) |
| Button1 (Top,Right) | (120, 96, 75×23) | (320, 96, 75×23) |

node: `App loaded`, `HANDLER FIRED`; no LOAD/CLICK ERROR. Edge: no page error; form area at (0,0).
The WinForms TextBox's auto-height at Segoe UI 9pt is 23, the catalog's default — so, unlike the Phone fixture, the TextBox is comparable.

**Mutations (Edit, rebuild, run, restore; `git diff` empty of production files after):**

| # | Mutant | Result |
|---|---|---|
| a | `FormScaffolder.Create` default `webLayout` → Grid | killed — "the property grid has no 'Dock' row for 'Panel1'" |
| b | `FormPlacement.Place`: `!FormVocabulary.IsPixel(document)` → `document.Target == FormTarget.Web` (the pre-Task-9 rule) | killed — "placing the Panel was refused" |
| c | `FormAssetEmitter.Css`: the Canvas arm skipped (`false &&`) | killed — 16 failures (form area at (8,8), every rect) |
| d | `FormAnchorCss.Docked`: the Fill arm writes no declarations | killed — 11 failures (Panel1 at its stored 200×100, its children with it, all four comparisons). ⚠ So the reflow script does NOT re-dock on load; the static CSS is what lays a Fill out. |

**Gate (clean rebuild after every mutant was restored):** fast subset 8321 / 8315 passed / 5 failed / 1 skipped, which is the same total as base 01f1db30 because the twin is Integration. The five failures are the known names: `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, `SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll`. Integration `FormDesignerAcceptanceTests | FormComponentAcceptanceTests | FormMenuAcceptanceTests | ~PixelLayout`: 104/104 passed, 0 skipped.
