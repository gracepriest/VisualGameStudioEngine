# Task 9 pre-flight: the canvas and placement become layout-aware (expanded, with corrections)

Plan: `docs/superpowers/plans/2026-09-27-web-pixel-layout.md`, "## Task 9". Spec: `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §2.4, §7, §7a.
The plan's anchors were taken at `47797002`. This check was made against `feat/web-pixel-layout` @ **`4376789a`** (Tasks 1–8 landed). It was read-only; nothing in the repo was changed except this file.

Follow this document **instead of** the plan's Task 9 section. Where the two disagree, this document wins. Everything else in the plan (How to build/test, Commits, Traps) still applies.

**OWNER decisions needed: none.** Three reversible calls are made here and listed under "Decisions taken (reversible)". Tell the owner about them at the Task 16 click-through.

---

## A. Anchor verification (claim → today's file:line at 4376789a → verdict)

| # | Plan / task claim | Today | Verdict |
|---|---|---|---|
| 1 | `FormCanvasTransform.Layout` `:371-380` routes every web doc to `WebLayout` | `VisualGameStudio.Shell/Controls/FormCanvasTransform.cs:376-385`; the target test is `:382` `document.Target == FormTarget.Web` | **moved** (+5), holds |
| 2 | `WebLayout` `:832-863`, Grid-only, "Canvas is the unimplemented pixel escape hatch" | `:837-868`; stale text `:833-835`; it ALSO yields `Bands` itself at `:864-867` | **moved**, holds. See C3: the Bands call moves out |
| 3 | `Bands` `:412-514`, "uniquely owns where each band SITS" | `:417-519`; summary `:387-416`; its own stacking `:419-446` walks `document.Controls` only (ROOT strips) | **moved**, holds. See B5 (root-only) |
| 4 | `BoundsOf` `:344-358` ignores Dock | `:342-363`, public, 2 args | **moved**, holds. See B2 (two test callers) |
| 5 | private `Layout(controls, origin)` `:865-893` | `:870-898` | **moved**, holds |
| 6 | `ContainerAt` `:297-335` | `:295-340` (public `:295-300`, private `:302-340`, BoundsOf call `:324`) | **moved**, holds |
| 7 | `ControlsIn` / marquee `:168-193` | `:168-193`, reads `Layout` | holds; gets resolved bounds for free |
| 8 | S12: canvas paints bands LAST `:375-376` | `:380-384` (comment + `Concat(Bands…)`) | **moved**, holds |
| 9 | `SurfaceSize` adapts `DesignSize` (Task 6) | `:208-216` → `document.DesignSize` | holds |
| 10 | `FormPlacement.Place` `:99-104` calls `PlaceOnWeb` for every web drop | `VisualGameStudio.Shell/ViewModels/Designer/FormPlacement.cs:99-104`, test `:101` `document.Target != FormTarget.WinForms` | holds |
| 11 | `PlaceOnWeb` refusal `:167-173` names Canvas | `:167-173`; text is `$"A {document.Layout.Kind} layout positions controls by document order…"` (interpolated) | holds. See C4: already reads "A Flow layout…" once Canvas stops arriving |
| 12 | (not in plan) `FormPlacement.SurfaceOf` | `:292-301`, a container's **stored** `pixel.Width/Height` | **gap**, see B3 |
| 13 | Grip press `:883`, grip drawing `:1566` gate on `Target == WinForms` | `FormCanvasControl.cs:883`, `:1566` | holds |
| 14 | `DrawSurface` `:1779-1811`; `isWindow` title bar | method `:1771-1832`; `isWindow` `:1779`; alignment grid is ALSO under `isWindow` (`:1808-1811`) | holds |
| 15 | Cell guides `:1816` Grid-only | `:1816-1831`; ⚠ also draws the PAGE CAPTION above the surface (`:1824-1830`) | holds. See B6 |
| 16 | `OnPointerPressed` pixel branch `:983-1008` | `:983-1008`; `_dragStarts` fill `:997-1004` | holds |
| 17 | Keyboard nudge "check FormCanvasKeyboardTests' route" | `OnKeyDown` `:647-674` → `FormGeometryEdit.MoveTo` / `.Resize` | resolved: see B4 |
| 18 | `HandleUnder` | `:1266-1279`; Render handles `:1550-1554` | holds; both gate on `is PixelGeometry` only |
| 19 | `FormCanvasControl.FormBoundsOf` changes with BoundsOf | `:1342-1353` reads `FormCanvasTransform.Layout`, NOT BoundsOf | **FALSE** (harmless): it needs no change; it gets resolved bounds through Layout |
| 20 | `FormGeometryEdit`: "confirm nothing writes X/Y for a docked control" | `MoveTo` `:50-73`, `MoveToForm` `:92-129` (`ContainerAt(…, ignore: control)` at `:102`), `Resize` `:172-225` all write a docked control's X/Y; so does `FormArrange.Move` `:82-141` | **FALSE today**, see B4 |
| 21 | (not in plan) `FormGeometryEdit.SurfaceOf` | `:251-260`, a container's **stored** size, a mirrored copy of #12 | **gap**, see B3 |
| 22 | ONE selection store `SelectInDesigner` | `CodeEditorDocumentViewModel.cs:301-305`; grid follows `Selection.Changed` `:1113-1116` | holds. Task 9 adds no grid write (chip `task_15ea19e4` covers `:595`, `:705`) |
| 23 | Negative MobileBreakpoint snaps back with no reason, `FormPropertyRow` ≈600-603 | `FormPropertyRow.cs:599-608` (`if (!_write(value)) { RaiseEditorRefresh(value); return; }`, `Refusal` stays null) | holds. ⚠ ClientSize `0, 300` takes the same path and is **pinned silent** by a test: see B1 |
| 24 | Stale "escape hatch" doc `FormCanvasTransform.cs` ≈829; `FormAssetEmitter.cs` ≈388 (Task 10) | `:833-835`; `FormAssetEmitter.cs:388` | holds |
| 25 | Spec-claims #8: no positioned-Dock canvas fixture | Re-grepped every test for `Dock="Top|Bottom|Left|Right|Fill"` and `Dock = "…"`. Positioned hits only `FormPixelVocabularyTests.cs:26`, `:315` (reader/clipboard, not canvas). Every other hit is a strip's `Dock` property. `FormStripLayoutTests.ContainerAt_TheExplicitDockedSkip…` (`:291-321`) hand-builds a strip WITH `PixelGeometry`; it still passes (ContainerAt skips `Place == Docked` before BoundsOf) | holds: **the expected-change list is empty** |
| 26 | `FormVocabulary` summary says "the canvas and placement move onto it in piece 1's Task 9" | `BasicLang/Forms/FormVocabulary.cs:9-12` | holds; update the text in Step 18 |
| 27 | Plan's real-view rig `FormPropertyGridRealViewTests.cs` | `Rig` `:86-160`, `Open` `:163-207`; `[AvaloniaTest]` via `DesignerHeadlessApp.cs:5` | holds |
| 28 | Canvas-level rigs | `FormCanvasMultiSelectTests.cs:24-89`, `FormCanvasKeyboardTests.cs:37-59`, `FormCanvasRenderTests.RenderHash` `:61-74`, `PixelAt` in `FormDesignerLayoutRealViewTests.cs:365-377` | holds |

---

## B. Blockers (following the plan text literally breaks something)

### B1: Surfacing the MobileBreakpoint reason also names ClientSize's, and a real-view test pins ClientSize SILENT
- **Evidence.** `FormPropertyGridRealViewTests.AClientSizeTheStoreRefuses_SnapsTheRealEditorBack` (`VisualGameStudio.Tests/Shell/FormPropertyGridRealViewTests.cs:420-452`) asserts `rig.Row("ClientSize").Refusal, Is.Null, "no reason is given today"` and `DescriptionBody, Does.Not.Contain("0, 300")` (`:446-450`).
- Negative `MobileBreakpoint` and `ClientSize = "0, 300"` share one route. Both pass `FormPropertyDef.Judge` (`FormControlCatalog.cs:329-348`: Int and Size `Accepts` only check the parse). Both are refused by `FormRootValues.Set` (`FormRootValues.cs:65-68`, `:98-101`). Both come back through `FormPropertyRow.Commit` `:604-607` with no reason.
- Any fix that names the MobileBreakpoint refusal on that route names ClientSize's too. So that test goes RED, which is **intended**. Slice 2's pre-flight already listed "name the store refusal" as a follow-up (see the comment at `:446-448`).
- **Corrected instruction.** Surface it (do NOT record it as a gap). Use ONE rule, `FormRootValues.RefusalOf(row, value)`: `Set` asks it and the grid shows its text. The route is `ForStoredValue(…, refusal:)` → `FormPropertyRow.Commit`. Step 13 has the code.
- Edit `:446-450` so it asserts the reason (Step 12). Record it in the commit as INTENDED.
- Why the store route and not "move the positivity rule into the catalog": a catalog range facet changes `Accepts` for every Int/Size row and the reader's tiers. The store already owns the rule, so the text belongs beside it.

### B2: `BoundsOf` is public and has two test callers. The plan's extra "container client size" parameter is not needed
- **Evidence.** `FormCanvasMultiSelectTests.cs:36` and `FormCanvasDoubleClickTests.cs:78` call `FormCanvasTransform.BoundsOf(control, default)`. Changing the signature breaks the test build.
- The resolver already stores each docked control's rect relative to its container's client origin (`FormDockedBounds.Bounds`). The origin is the only other input BoundsOf needs, so "the container's client size" is redundant.
- **Corrected instruction.** The new signature is `BoundsOf(FormControl control, Point containerOrigin, FormDockLayoutResult dock)`.
  - Do NOT keep a 2-argument overload. It would draw a docked control at its stale rect for any caller that picked it.
  - Update both test callers to `FormCanvasTransform.BoundsOf(control, default, FormDockLayout.Resolve(Doc))` and `…(control, default, FormDockLayout.Resolve(doc))`. Their fixtures are undocked, so the result is unchanged.

### B3: A drop, drag or nudge into a DOCKED container clamps against the container's stale stored size
- **Evidence.** `FormPlacement.SurfaceOf` (`:292-301`) and `FormGeometryEdit.SurfaceOf` (`:251-260`) both return a container's `PixelGeometry.Width/Height`. A `Dock=Fill` Panel stored as 10×10 and drawn as 640×456 therefore clamps every drop and every drag into it to X=Y=0.
- The plan's own risk (Task 9 "Risks", third bullet) says a container's client size is `FormDockLayoutResult.ClientSizeOf`, "never a re-derived 'resolved bounds or stored size'". But neither site is in the plan's file list.
- The two methods are also a mirrored pair (identical bodies, one comment each claiming to match `Fit`).
- **Corrected instruction.** `FormGeometryEdit.SurfaceOf` becomes `internal` and asks `FormDockLayout.Resolve(document).TryGetClientSize(container, out var size)`, falling back to `SurfaceSize`. `FormPlacement` deletes its copy and calls that one. Steps 5 and 6 have the code and the tests that pin it: a drop into a Fill panel, and a MoveTo inside one.

### B4: "Nothing writes X/Y for a docked control" is false. The rule belongs in `FormGeometryEdit`, plus one canvas guard
- **Evidence.** `MoveTo`, `MoveToForm` and `Resize` write any `PixelGeometry`. They are called by the drag (`FormCanvasControl.cs:1124`, `:1150`, `:1161`) and by the keyboard (`:665-668`).
- The plan lists guards in the pointer branch, in `_dragStarts` and in the keyboard: three canvas sites, and the keyboard would still reach `FormGeometryEdit`.
- **Corrected instruction.** Split it into three pieces:
  - **Model.** `FormGeometryEdit.MoveTo`, `MoveToForm` and `Resize` return false for a control where `FormDockLayout.EdgeOf(control) != null`. This covers the keyboard, the group loop and any future caller.
  - **Canvas, handles.** ONE predicate, `HasHandles(control)` (pixel geometry and not docked), is asked by both `HandleUnder` and Render. So no handles are drawn and no resize is armed.
  - **Canvas, drag.** The pointer's pixel branch arms nothing for a docked primary. Without this, a drag that starts on a docked primary of a multi-selection moves the OTHER selected controls through the group loop (`:1134-1151`). The primary refuses and the rest follow, which contradicts §7a's "a drag starting on it selects only". `ADragStartingOnADockedPrimary_MovesNoOtherSelectedControlEither` pins it.
- Out of scope (recorded): `FormArrange` (align/size) still writes a docked control's X/Y and uses a docked primary's stale X/Y as its reference. It is reachable today on WinForms. See "Follow-ups".

### B5: `Bands` walks ROOT strips only, and reading them from the resolver at every depth needs each container's drawn origin
- **Evidence.**
  - `Bands` iterates `document.Controls.Where(Place == Docked)` (`:432`).
  - The private `Layout` skips `Place == Docked` at EVERY depth (`:880-883`).
  - So a strip inside a Panel (the reader accepts one, and ResolveSiblings docks it) is drawn nowhere today.
  - The resolver's rect is relative to its container's CLIENT origin (`FormDockLayout.cs:26-27`). Bands must add the container's DRAWN origin, which only the control walk computes.
- **Corrected instruction.**
  - The control walk records `origins[control] = bounds.TopLeft` for every positioned control.
  - Bands iterates `dock.All` (strips only) and offsets each rect by `origins[parent]`, or by (0,0) at the root.
  - ⚠ Materialise the control walk (`.ToList()`) before calling Bands. A lazy `Concat` happens to enumerate the walk first today. A consumer that stops early (`FormBoundsOf` returns at its first match) or a later reordering would hand Bands an empty map, and every nested strip would vanish silently.
  - Step 3 has the code. `BandsAndDockedControls_AreWhereTheResolverPutsThem_AtEveryDepth` pins it.

### B6: The Grid page draws its CAPTION above the surface, so a naive "pixel above the surface equals the background" sample can hit text
- **Evidence.** `FormCanvasControl.cs:1824-1830` draws `document.Text ?? document.Name` at `(surface.X + 4, surface.Y - 16)` for a Grid page.
- **Corrected instruction.**
  - A Canvas page is a PAGE: it gets the Grid page's outline and caption, but no cell guides. Flow keeps what it has today (neither).
  - The no-title-bar sample is taken at `(surface.Center.X, surface.Y - 9)`, the middle of where an 18px title bar would be. There it lands in navy on a window, and on the canvas background on a page. The caption stays at the left.
  - The canvas-level test uses WinForms as its control group, so the sampler is proven able to see a title bar.

### B7: The real view cannot pin the zoom, so the alignment-grid check cannot live there
- **Evidence.** `DrawAlignmentGrid` (`:1892-1898`) draws nothing when `GridStep * zoom < 4` (zoom < 0.5). The real view's canvas size depends on the toolbox and grid columns, so its zoom for an 800×450 page is not controllable.
- **Corrected instruction.** The chrome pixel checks (title bar, alignment dots, form grips, docked handles) live in a new canvas-level file, `VisualGameStudio.Tests/Shell/FormCanvasPixelPageTests.cs`. It uses windows of 700×500 (zoom 0.815) and 900×600 (zoom 1.0, clamped).
  - The real-view file repeats only the title-bar and grip samples, plus the grip DRAG that writes the file.
  - The plan's "every test at two window sizes / zoom ≠ 1" is kept. Each real-view gesture test asserts that at least one of its sizes is not 1:1.

### B8: No test in the repo drives a real headless drag-and-drop
- **Evidence.** A grep for `DragDrop(` in `VisualGameStudio.Tests` finds only `DockFactoryReopenTests`. Every drop test executes `DropCommand` / `PlaceDroppedControlCommand` directly (`FormCanvasDropTests.cs:231-248`).
- **Corrected instruction.** The real-view drop uses Avalonia.Headless's `HeadlessWindowExtensions.DragDrop(topLevel, point, RawDragEventType, IDataObject, DragDropEffects)` with DragEnter, DragOver and Drop. The data object is `new DataObject()` plus `Set(FormCanvasControl.ControlKindFormat, "Button")`, which is exactly what the toolbox builds (`CodeEditorDocumentView.axaml.cs:2180-2181`).
- **If it does not compile or deliver on 11.3.13**, fall back. Execute the canvas's bound `DropCommand` with the request the canvas computes: `new FormControlDropRequest(kind, Snap(p.X), Snap(p.Y))` with `p = Fit.ToForm(canvasPoint)`, snapped to multiples of 8. Say so in the test's summary and the commit. The pointer → form mapping is then covered by the Fit/HitTest tests.

---

## C. Corrections (not blocking)

- **C1. S12, decided: recorded, NOT changed.** The canvas keeps painting bands last (on top). After Task 9 the disagreement shrinks:
  - docked controls no longer overlap a band (the resolver stacks them);
  - what remains is an UNDOCKED control overlapping a band;
  - and a nested strip, which is painted over later ROOT siblings. WinForms paints those siblings in front.
  - Keep the `⚠ Bands LAST` comment in `Layout` and name S12 in it (Step 3 code).
- **C2. The canvas asks `FormDockMode.Designer`** (hidden controls docked and drawn). The mutant `Runtime` is killed by `AHiddenDockedPanel_IsStillDockedAndDrawn_OnTheCanvas` (Step 1).
- **C3. `WebLayout` stops yielding Bands.** `Layout` appends `Bands` for EVERY document (pixel or not), so a Flow page's menu bar is still laid out (`FormStripLayoutTests.Layout_YieldsBandsOnAWebPage_RegardlessOfLayoutKind` stays green). `WebLayout` loses its `selected` parameter and becomes Grid cells only.
- **C4. `PlaceOnWeb`'s refusal text needs no change.** It interpolates `{document.Layout.Kind}`, and only Flow reaches it now. Only its summary changes. `FormPlacementTests.AFlowLayout_IsStillRefused_BecauseItHasNoCells` (`:485-505`, `Does.Contain("Flow")`) already pins the wording.
- **C5. Docs made stale by Task 9** (doc-only edits, Step 18):
  - `FormCanvasTransform.WebLayout`'s "escape hatch" text (`:833-835`), `Bands`' "uniquely owns" and "Top strips stack…" paragraphs (`:396-416`), and `BoundsOf`'s "Web documents use grid geometry" note;
  - `FormVocabulary.cs:9-12`;
  - `FormPropertyGridViewModel.cs:361-368` ("a .blwebform control lives in a grid CELL and has no edges to anchor to"; a Canvas page's controls have Anchor/Dock);
  - `FormArrange.cs:26-30` ("these commands refuse on a web form"; on a Canvas page they work, because its controls are PixelGeometry);
  - `FormPlacement.cs:99-100` and `:143-155` ("A .blwebform positions controls by CELL").
- **C6. `ControlsIn`** (marquee) reads `Layout`, so it sees resolved bounds with no change. A marquee can select a docked control (it is Positioned). A group drag then skips it: `MoveTo` refuses.
- **C7. The property grid's X/Y/Width/Height rows still write a docked control's stored values.** That is legitimate: the user typed them. A docked Panel's HEIGHT (Dock=Top) or WIDTH (Dock=Left) is only editable there once the canvas offers no handle. See "Decisions taken".
- **C8. Performance.** `Layout` now resolves once per call. It is called per pointer move (`UpdateCursor` → `HandleUnder` → `FormBoundsOf`) and per selected member in Render. The cost is O(n) extra on an O(n) walk, which is acceptable for designer-sized forms. Do not cache across calls: the model is mutated in place and a cache is a staleness bug.
- **C9. The toolbox, the property rows, retarget and new-form choice stay target-based** (verdicts in §C of the grep table). Only the vocabulary sites move.

## Decisions taken (reversible; tell the owner, no decision needed)
1. **A selected docked control is drawn with the dashed secondary-selection outline instead of handles.** §7a removes the handles, and without some indication a click on a docked Panel shows nothing at all. `ASelectedDockedControl_IsStillShownSelected` pins "something changes". The outline's look is cosmetic.
2. **A Canvas page shows the page outline and the caption above the surface**, as a Grid page does (no title bar, no cell guides).
3. **A strip inside a Panel is now drawn** (as a band inside the Panel, from the resolver). It was drawn nowhere before.

## Follow-ups (out of scope; for the Task 16 record)
- `FormArrange` on a docked control. Align/Size writes its X/Y (ignored at run time), and a docked PRIMARY's stale X/Y becomes the alignment reference. Reachable on WinForms today. It needs its own spec line.
- A docked Panel's size can only change through the grid (§7a removes the handles; VS shows the inner-edge handle). Mention it at the click-through.

---

## C (grep). Every Shell site that decides pixel-vs-cell, with a verdict

Grep: `Target == FormTarget.WinForms|Web`, `Target != …`, `FormTarget.Web ?`, `is GridGeometry`, `is PixelGeometry`, `is not PixelGeometry`, `LayoutKind.`, `\.Target\b` across `VisualGameStudio.Shell/`.

| Site | What it decides | Verdict |
|---|---|---|
| `Controls/FormCanvasTransform.cs:382` `document.Target == FormTarget.Web ? WebLayout` | pixel walk vs grid cells | **MOVE → `FormVocabulary.IsPixel(document)`** (Step 3) |
| `FormCanvasTransform.cs:353` `is not PixelGeometry` (BoundsOf) | a control's own geometry shape | legit; gains the resolver (Step 3) |
| `FormCanvasTransform.cs:839` `Layout?.Kind == Grid`, `:844` `is GridGeometry` | cells are a Grid idea | legit (inside WebLayout, now Grid/Flow only) |
| `Controls/FormCanvasControl.cs:883` grip press `Target == WinForms` | form-resize grips | **MOVE → IsPixel** (Step 9) |
| `FormCanvasControl.cs:1566` grip drawing `Target == WinForms` | form-resize grips | **MOVE → IsPixel** (Step 9) |
| `FormCanvasControl.cs:1779` `isWindow = Target == WinForms` | title bar, frame, window buttons | **STAYS target-only** (plan Traps: it is about being a WINDOW) |
| `FormCanvasControl.cs:1808-1811` alignment grid under `isWindow` | alignment dots | **MOVE → IsPixel** (Step 9) |
| `FormCanvasControl.cs:1816` `Target == Web && Layout?.Kind == Grid` | cell guides + page outline/caption | legit for cells; outline/caption extended to Canvas (B6, Step 9) |
| `FormCanvasControl.cs:650`, `:663` keyboard `GridGeometry` / `PixelGeometry` arms | per-control shape | legit; docked handled in `FormGeometryEdit` (B4) |
| `FormCanvasControl.cs:972`, `:983`, `:1000`, `:1110`, `:1137`, `:1156` drag arms | per-control shape | legit; `:983` also refuses a docked primary (B4, Step 9) |
| `FormCanvasControl.cs:1272` HandleUnder, `:1550` Render handles | handles | legit shape test → **`HasHandles`** predicate (B4, Step 9) |
| `ViewModels/Designer/FormPlacement.cs:48`, `:51` `SupportsTarget(document.Target)` | whether the kind exists on the target | legit (catalog, target) |
| `FormPlacement.cs:101` `Target != WinForms` → PlaceOnWeb | pixel vs cell drop | **MOVE → `!FormVocabulary.IsPixel`** (Step 6) |
| `FormPlacement.cs:167` `Layout.Kind != Grid` | Flow refusal inside PlaceOnWeb | legit |
| `FormPlacement.cs:294` `container?.Geometry is PixelGeometry` (SurfaceOf) | clamp box | **REPLACE → `FormGeometryEdit.SurfaceOf`** (B3, Step 6) |
| `ViewModels/Designer/FormGeometryEdit.cs:55`, `:97`, `:178` `is not PixelGeometry` | shape guard | legit; plus the docked guard (B4, Step 6) |
| `FormGeometryEdit.cs:148` MoveToCell Grid | cells | legit (spec §2.4 "unaffected") |
| `FormGeometryEdit.cs:253` SurfaceOf stored size | clamp box | **REPLACE → resolver client size** (B3, Step 6) |
| `ViewModels/Designer/FormArrange.cs:54`, `:66` `is not PixelGeometry` | align/size | legit (works on a Canvas page automatically); doc fix (C5); docked is out of scope (Follow-ups) |
| `ViewModels/Designer/FormPropertyGridViewModel.cs:353-392` switch on geometry type | intrinsic rows | legit (spec §2.4, verified); comment `:361-368` stale (C5) |
| `FormPropertyGridViewModel.cs:489` `form.Target` into `ForStoredValue` | whose default / value rules | legit |
| `FormPropertyGridViewModel.cs:531` `_file?.Model.Target ?? FormTarget.Web` | which control properties exist | legit (per target, by csc) |
| `ViewModels/Designer/FormToolboxViewModel.cs:40`, `:84` | the toolbox's type-name description | legit |
| `ViewModels/Documents/CodeEditorDocumentViewModel.cs:217` `Toolbox.Target` | which kinds are offered | legit |
| `CodeEditorDocumentViewModel.cs:573`, `:626` clipboard `(Target, LayoutOf)` | paste vocabulary | already vocabulary-aware (Task 3) |
| `ViewModels/Panels/SolutionExplorerViewModel.cs:1332-1333` | new form's target | legit |
| `SolutionExplorerViewModel.cs:1417-1444` | retarget direction | legit / out of scope (Task 11, piece 4) |
| `Views/Documents/CodeEditorDocumentView.axaml.cs:279-281` `e.Target` | unrelated (not a FormTarget) | n/a |

**Summary: 6 sites move to `FormVocabulary` (the layout, the grip press, the grip drawing, the alignment grid, the placement route, and the page chrome's extension). 2 clamp sites move to the resolver. 1 (`isWindow`) stays target-only on purpose. Everything else is legitimately target- or shape-based.**

---

## D. Task 9, expanded

Base: `feat/web-pixel-layout` @ `4376789a`. `$sp` is your scratchpad. The build/test commands and commit rules are the plan's ("How to build and run tests", "Commits"). Every red/green claim means reading `$sp\run.txt`: counts plus failure names.

Build: `dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release` (it builds Shell and BasicLang too). No AXAML changes in this task, so no `dotnet clean`.

### Step 0: Base and checkpoint (3 min)
- [ ] `git log --oneline -1` prints `4376789a …`, and `git status --porcelain` shows only `?? csc.dll`.
- [ ] Build. Run the named fixtures below to record today's names with the base (`4376789a`). They are the fixtures this task touches:

```powershell
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~FormCanvas|FullyQualifiedName~FormPlacementTests|FullyQualifiedName~FormGeometryEditTests|FullyQualifiedName~FormStrip|FullyQualifiedName~FormDesignerRealViewTests|FullyQualifiedName~FormDesignerLayoutRealViewTests|FullyQualifiedName~FormPropertyGridRealViewTests|FullyQualifiedName~FormPropertyGridDisplayTests|FullyQualifiedName~FormPropertyGridTests|FullyQualifiedName~FormTrayViewTests|FullyQualifiedName~FormDesignModeTests|FullyQualifiedName~FormAnchorDockPickerTests|FullyQualifiedName~FormMobileBreakpointTests|FullyQualifiedName~FormRootTests|FullyQualifiedName~FormRootLayoutTests|FullyQualifiedName~FormZOrderTests|FullyQualifiedName~FormMenuEditorDefectsTests|FullyQualifiedName~FormTypeHereEditorTests|FullyQualifiedName~FormSchematicPinTests|FullyQualifiedName~FormDockLayoutTests`" > `"$sp\t9-base.txt`" 2>&1"
```

Expected: all green (these are not in the known machine-failure list). Record total/passed/failed/skipped **with the base**.

### Part 1: `FormCanvasTransform` is layout-aware and reads `FormDockLayout`

#### Step 1: Failing tests in `FormCanvasTransformTests` (5 min)
Edit `VisualGameStudio.Tests/Compiler/FormCanvasTransformTests.cs`. Insert the block below before the `// Web documents — laid out on the grid` banner (`:312`), after `HandlesAreTheSameSizeOnScreenAtEveryZoom`.

```csharp
    // ==================================================================
    // Task 9 (spec 2026-09-27 §2.4, §3, §7a) — a Canvas page speaks pixels, and every docked thing sits where
    // FormDockLayout puts it: one answer, two consumers (the canvas here, the page emitter in Task 10)
    // ==================================================================

    private static FormControl Panel(string id, int x, int y, int w, int h, string? dock = null, params FormControl[] children)
    {
        var panel = new FormControl
        {
            Kind = "Panel",
            Id = id,
            Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h, Dock = dock }
        };
        panel.Children.AddRange(children);
        return panel;
    }

    private static FormControl DockedButton(string id, int x, int y, int w, int h, string dock) => new()
    {
        Kind = "Button",
        Id = id,
        Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h, Dock = dock }
    };

    private static FormControl StripOf(string kind, string id) => new() { Kind = kind, Id = id };

    private static FormDocument CanvasPage(params FormControl[] controls)
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web,
            Name = "Page",
            Width = 640,
            Height = 480,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        page.Controls.AddRange(controls);
        return page;
    }

    private static FormDocument DockForm(params FormControl[] controls)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "DockForm", Width = 640, Height = 480 };
        form.Controls.AddRange(controls);
        return form;
    }

    [Test]
    public void ACanvasPage_IsLaidOutInPixels_NotRoutedDownTheGridPath()
    {
        // ⛔ Spec §2.4: Layout sent EVERY web document to WebLayout, which places Grid cells only — a Canvas page's
        // controls were drawn nowhere.
        var page = CanvasPage(Control("btn", 20, 30, 100, 40));

        var laid = FormCanvasTransform.Layout(page).ToList();

        Assert.That(laid.Select(e => (e.Control!.Id, e.Bounds, e.Role)),
            Is.EqualTo(new[] { ("btn", new Rect(20, 30, 100, 40), FormLayoutRole.Control) }));
    }

    [Test]
    public void ACanvasPagesControl_IsHitWhereItIsDrawn_AtAnyZoom()
    {
        var page = CanvasPage(Control("btn", 20, 30, 100, 40));
        var t = new FormCanvasTransform(1.75, new Vector(12, -8));

        Assert.That(t.HitTest(page, t.ToCanvas(new Rect(20, 30, 100, 40)).Center)?.Id, Is.EqualTo("btn"));
    }

    [Test]
    public void ContainerAt_OnACanvasPage_FindsThePanel_WithItsOrigin()
    {
        var page = CanvasPage(Panel("pnl", 100, 100, 200, 200));

        var found = FormCanvasTransform.ContainerAt(page, new Point(150, 150));

        Assert.Multiple(() =>
        {
            Assert.That(found?.Container.Id, Is.EqualTo("pnl"));
            Assert.That(found?.Origin, Is.EqualTo(new Point(100, 100)));
        });
    }

    /// <summary>
    /// ⛔⛔ ONE answer, two consumers (spec §3, §7a; plan mutation M5): every band and every docked control the canvas
    /// lays out is exactly FormDockLayout.Resolve's rectangle, offset by its container's DRAWN origin — at every
    /// depth. Bands used to derive their own stacking from the root strips alone, and BoundsOf ignored Dock.
    /// </summary>
    [Test]
    public void BandsAndDockedControls_AreWhereTheResolverPutsThem_AtEveryDepth()
    {
        var innerMenu = StripOf("MenuStrip", "innerMenu");
        var innerButton = DockedButton("innerBtn", 5, 5, 60, 20, "Bottom");
        var fill = Panel("fill", 7, 7, 10, 10, "Fill", innerMenu, innerButton);
        var topPanel = Panel("topPanel", 300, 300, 50, 40, "Top"); // stored X/Y/Width are stale by design
        var menu = StripOf("MenuStrip", "menuStrip1");
        var status = StripOf("StatusStrip", "statusStrip1");
        var form = DockForm(topPanel, menu, status, fill);

        var dock = FormDockLayout.Resolve(form);
        var laid = FormCanvasTransform.Layout(form).ToList();

        Rect Entry(FormControl c, FormLayoutRole role) =>
            laid.Single(e => ReferenceEquals(e.Control, c) && e.Role == role).Bounds;

        Rect Resolved(FormControl c, Point origin)
        {
            Assert.That(dock.TryGet(c, out var d), Is.True, $"precondition: {c.Id} is docked");
            return new Rect(origin.X + d.Bounds.X, origin.Y + d.Bounds.Y, d.Bounds.Width, d.Bounds.Height);
        }

        var root = new Point(0, 0);
        var fillOrigin = Entry(fill, FormLayoutRole.Control).TopLeft;

        Assert.Multiple(() =>
        {
            Assert.That(Entry(topPanel, FormLayoutRole.Control), Is.EqualTo(Resolved(topPanel, root)));
            Assert.That(Entry(menu, FormLayoutRole.Band), Is.EqualTo(Resolved(menu, root)));
            Assert.That(Entry(status, FormLayoutRole.Band), Is.EqualTo(Resolved(status, root)));
            Assert.That(Entry(fill, FormLayoutRole.Control), Is.EqualTo(Resolved(fill, root)));
            Assert.That(Entry(innerMenu, FormLayoutRole.Band), Is.EqualTo(Resolved(innerMenu, fillOrigin)),
                "a strip inside a Panel is a band too, relative to the Panel's drawn origin");
            Assert.That(Entry(innerButton, FormLayoutRole.Control), Is.EqualTo(Resolved(innerButton, fillOrigin)));

            // Non-vacuity — the numbers the agreement is about, on a 640x480 client:
            Assert.That(Entry(menu, FormLayoutRole.Band), Is.EqualTo(new Rect(0, 40, 640, 24)),
                "the Dock=Top Panel precedes the menu, so it takes the top edge and the menu sits below it");
            Assert.That(Entry(fill, FormLayoutRole.Control), Is.EqualTo(new Rect(0, 64, 640, 394)));
            Assert.That(Entry(innerButton, FormLayoutRole.Control), Is.EqualTo(new Rect(0, 64 + 374, 640, 20)));
            Assert.That(laid.Count(e => e.Role == FormLayoutRole.Band), Is.EqualTo(3), "three strips, three bands");
        });
    }

    [Test]
    public void AHiddenDockedPanel_IsStillDockedAndDrawn_OnTheCanvas()
    {
        // ⚠ The canvas is FormDockMode.Designer (plan C2): a Visible=false control is shown, and docks. The page
        // (Task 10) asks Runtime, where it would give up its edge.
        var hidden = Panel("hidden", 300, 300, 50, 40, "Top");
        hidden.Properties["Visible"] = "False";
        var menu = StripOf("MenuStrip", "menuStrip1");
        var form = DockForm(hidden, menu);

        var laid = FormCanvasTransform.Layout(form).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(laid.Single(e => ReferenceEquals(e.Control, hidden)).Bounds, Is.EqualTo(new Rect(0, 0, 640, 40)));
            Assert.That(laid.Single(e => ReferenceEquals(e.Control, menu)).Bounds.Y, Is.EqualTo(40));
        });
    }

    [Test]
    public void ADockedControl_IsHitWhereItIsDrawn_NotAtItsStaleStoredRect()
    {
        var form = DockForm(Panel("topPanel", 300, 300, 50, 40, "Top"));
        var t = new FormCanvasTransform(2.0, new Vector(30, 10));

        Assert.Multiple(() =>
        {
            Assert.That(t.HitTest(form, t.ToCanvas(new Point(600, 20)))?.Id, Is.EqualTo("topPanel"),
                "resolved: the whole top edge, 40 high");
            Assert.That(t.HitTest(form, t.ToCanvas(new Point(320, 320))), Is.Null,
                "its stored X/Y is stale by design — nothing is drawn there");
        });
    }

    [Test]
    public void ContainerAt_ADockedFillPanel_ReportsTheOriginItIsDrawnAt()
    {
        var fill = Panel("fill", 7, 7, 10, 10, "Fill");
        var form = DockForm(StripOf("MenuStrip", "menuStrip1"), fill);

        var found = FormCanvasTransform.ContainerAt(form, new Point(100, 200));

        Assert.Multiple(() =>
        {
            Assert.That(found?.Container, Is.SameAs(fill));
            Assert.That(found?.Origin, Is.EqualTo(new Point(0, 24)), "under the 24px menu — not the stale (7,7)");
        });
    }
```

Also edit `VisualGameStudio.Tests/Shell/FormCanvasRenderTests.cs`. Add after `AComponent_IsNeverLaidOut` (ends `:131`):

```csharp
    /// <summary>
    /// Task 9 (spec 2026-09-27 §2.4): a Canvas page draws every kind the web has — before Task 9 it went down the Grid
    /// path and drew none of its positioned controls. Catalog-driven. NOT a distinctness gate (two web-only rows may
    /// share a schematic): only "something is drawn".
    /// </summary>
    [AvaloniaTest]
    public void EveryWebKind_IsDrawnOnACanvasPage()
    {
        FormDocument CanvasPage() => new()
        {
            Target = FormTarget.Web, Name = "T", Width = FormWidth, Height = FormHeight,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        var empty = RenderHash(CanvasPage());
        var webKinds = FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.Web) && d.Place != FormPlace.Tray && d.Place != FormPlace.Item)
            .ToList();
        Assert.That(webKinds, Is.Not.Empty, "the catalog has no web kinds to draw");

        var blank = new List<string>();
        foreach (var definition in webKinds)
        {
            var page = CanvasPage();
            FormCatalogShapes.Canonical(page, definition, SharedId, hostId: SharedId,
                geometry: new PixelGeometry { X = 20, Y = 20, Width = 140, Height = 40 });
            if (RenderHash(page) == empty)
            {
                blank.Add(definition.Kind);
            }
        }

        Assert.That(blank, Is.Empty, "these kinds draw NOTHING on a Canvas page: " + string.Join(", ", blank));
    }
```

- [ ] Build (it compiles: only existing public API is used). Run:

```powershell
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~FormCanvasTransformTests|FullyQualifiedName~FormCanvasRenderTests`" > `"$sp\run.txt`" 2>&1"
```

**Expected RED**
- `ACanvasPage_IsLaidOutInPixels…`: empty sequence.
- `ACanvasPagesControl_IsHit…`: null.
- `BandsAndDockedControls…`: `Single` throws for `innerMenu`, or the menu is at Y 0.
- `AHiddenDockedPanel…`: stored rect, and the menu at Y 0.
- `ADockedControl_IsHit…`.
- `ContainerAt_ADockedFillPanel…`: null.
- `EveryWebKind_IsDrawnOnACanvasPage`: every positioned kind is blank. Strips are drawn even today, because Bands ran on the web.

**Already GREEN (a guard, not a proof):** `ContainerAt_OnACanvasPage…`. ContainerAt never asked the target.

#### Step 2: Implement `FormCanvasTransform` (5 min)
Edit `VisualGameStudio.Shell/Controls/FormCanvasTransform.cs`. Find each piece by its quoted text.

1. **Public `Layout`.** Replace the body from `ArgumentNullException.ThrowIfNull(document);` through `: Layout(document.Controls, new Point(0, 0)).Concat(Bands(document, selected));` (`:378-384`) with:

```csharp
        ArgumentNullException.ThrowIfNull(document);

        // ⛔⛔ ONE dock resolve per layout pass (spec 2026-09-27 §3, §7a), shared by the control walk (a docked
        // control's rectangle) and Bands (every strip's band, at every depth) — never a second stacking loop here.
        // Designer mode: a Visible=false control is still drawn, and still docks.
        var dock = FormDockLayout.Resolve(document, FormDockMode.Designer);

        // ⚠ MATERIALISED, not lazy: Bands reads each container's drawn client origin, which the control walk records
        // as it goes. A lazy Concat happens to run the walk first; a consumer that stopped at its first match, or a
        // reordering, would hand Bands an empty map and drop every nested strip with nothing failing.
        var origins = new Dictionary<FormControl, Point>(ReferenceEqualityComparer.Instance);
        var placed = FormVocabulary.IsPixel(document)
            ? Layout(document.Controls, new Point(0, 0), dock, origins).ToList()
            : WebLayout(document).ToList();

        // ⚠ Bands LAST, so a band paints over — and out-hit-tests — anything that overlaps it. Document order is
        // z-order everywhere in this class, and chrome is on top of the surface. ⚠ Scope call S12, recorded and NOT
        // changed: the page and WinForms paint a strip in DOCUMENT order, so an UNDOCKED control overlapping a band
        // (or a later root sibling over a nested strip) is drawn differently here. Docked controls no longer overlap
        // a band at all — the resolver stacks them.
        return placed.Concat(Bands(document, selected, dock, origins));
```

2. **`BoundsOf`.** Replace the whole method and its summary (`:342-363`):

```csharp
    /// <summary>
    /// A control's rectangle in FORM space, or null when it carries no pixel geometry.
    ///
    /// <para>⛔ A DOCKED control's rectangle is <paramref name="dock"/>'s — where it RUNS — never its stored X/Y, which
    /// docking ignores and which are stale by design (spec 2026-09-27 §3, §7a). Its place depends on its siblings,
    /// which is why this takes the pass's one resolved layout rather than resolving per control.</para>
    ///
    /// <para>⚠ Null rather than an empty rect: a control with no geometry has no position, and an empty rect at the
    /// origin would silently make it a zero-size target sitting in the corner. A strip carries no pixel geometry, so it
    /// never comes through here — Bands places it; a Grid page's cell geometry is WebLayout's.</para>
    ///
    /// <para>⚠ The rectangle may lie partly or wholly OUTSIDE its container: an overflowing dock gives a negative X/Y
    /// (<see cref="FormRect"/>). Nothing here clips it, and hit-testing must not assume it is inside.</para>
    /// </summary>
    public static Rect? BoundsOf(FormControl control, Point containerOrigin, FormDockLayoutResult dock)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(dock);

        if (control.Geometry is not PixelGeometry pixel)
        {
            return null;
        }

        if (dock.TryGet(control, out var docked))
        {
            var b = docked.Bounds;
            return new Rect(containerOrigin.X + b.X, containerOrigin.Y + b.Y, b.Width, b.Height);
        }

        return new Rect(
            containerOrigin.X + pixel.X,
            containerOrigin.Y + pixel.Y,
            Math.Max(0, pixel.Width),
            Math.Max(0, pixel.Height));
    }
```

3. **`ContainerAt`.** In the public method replace `return ContainerAt(document.Controls, formPoint, new Point(0, 0), ignore);` with:

```csharp
        // ⛔ The SAME resolved picture Layout draws: a docked Panel is a drop target where it is DRAWN.
        var dock = FormDockLayout.Resolve(document, FormDockMode.Designer);
        return ContainerAt(document.Controls, formPoint, new Point(0, 0), ignore, dock);
```

   In the private overload, add the parameter `FormDockLayoutResult dock` (after `FormControl? ignore`). Change `var bounds = BoundsOf(control, containerOrigin);` to `BoundsOf(control, containerOrigin, dock)`. Change the recursion to `ContainerAt(control.Children, formPoint, bounds.Value.TopLeft, ignore, dock)`.

4. **`Bands`.** Replace from `private static IEnumerable<FormLayoutEntry> Bands(FormDocument document, FormControl? selected)` through `yield return new FormLayoutEntry(strip, band, FormLayoutRole.Band);` (`:417-448`) with the text below. Everything after it (the cells, the Type Here slot, the dropdowns) stays exactly as it is.

```csharp
    private static IEnumerable<FormLayoutEntry> Bands(
        FormDocument document, FormControl? selected, FormDockLayoutResult dock,
        IReadOnlyDictionary<FormControl, Point> origins)
    {
        var parents = ParentMap(document);
        var activeStrip = ActiveStrip(selected, parents);
        var expanded = ExpansionPath(selected, parents);

        // Where each EXPANDED host was laid out. Filled in as the walk reaches it, never looked up:
        // the outermost expanded host is a band cell produced by the loop just below, and every
        // deeper one is a dropdown row produced by the host before it in `expanded`. So the chain
        // is always populated before it is read, and no rectangle is computed twice.
        var cellOf = new Dictionary<FormControl, Rect>();

        // ⛔⛔ Where each band SITS is FormDockLayout's answer (spec 2026-09-27 §3), read here and never re-derived.
        // The resolver docks strips AND docked controls in ONE document-ordered sequence, in ANY sibling list (a strip
        // inside a Panel too), so a Dock=Top Panel before the MenuStrip pushes the band down exactly as WinForms and
        // the page place it. Its All is breadth-per-list: every root band comes before any nested one.
        foreach (var docked in dock.All)
        {
            var strip = docked.Control;

            // A docked CONTROL is drawn by the control walk (BoundsOf); only a strip is a band.
            if (strip.Definition?.Place != FormPlace.Docked)
            {
                continue;
            }

            // The resolver's rectangle is relative to its container's CLIENT origin: the form's at the root, the
            // container's drawn origin (recorded by the control walk) inside one.
            Point origin;
            if (!parents.TryGetValue(strip, out var container))
            {
                origin = new Point(0, 0);
            }
            else if (!origins.TryGetValue(container, out origin))
            {
                // Its container is not drawn (no pixel geometry), so neither is the strip inside it.
                continue;
            }

            var b = docked.Bounds;
            var band = new Rect(origin.X + b.X, origin.Y + b.Y, b.Width, b.Height);

            yield return new FormLayoutEntry(strip, band, FormLayoutRole.Band);
```

   In the Bands summary (`:387-416`), replace the second `<para>` ("Top strips stack DOWNWARD…") with:

   `<para>⛔ Where each band sits — which edge, and in what order — is <see cref="FormDockLayout.Resolve"/>'s, the one resolver the page emitter asks too. The strip's edge comes from <see cref="FormControl.IsDockedToBottom"/>, called inside the resolver: two copies of either would let this canvas draw the status band on one edge while the page puts its footer on the other.</para>`

   In the third `<para>`, replace "This loop uniquely owns where each band SITS — the stacking from each edge is derived here and nowhere else — and a cell's rectangle is that band's own Y and Height." with "This loop is where each band's RESOLVED rectangle becomes an entry, and a cell's rectangle is that band's own Y and Height."

5. **`WebLayout`.** Replace the method and its summary (`:826-868`) with:

```csharp
    /// <summary>
    /// A Grid page's controls, each in the grid cell it names. (Its strips are Bands, which Layout appends for EVERY
    /// document: a band is page chrome, not a cell, so a Flow page's menu bar is laid out too.)
    ///
    /// <para>⚠ TOP LEVEL only, and that is the model's limit rather than a shortcut:
    /// <see cref="FormLayout"/> is a property of the DOCUMENT, so a Panel's children carry Col/Row
    /// against a grid that is not described anywhere. Laying them out would mean inventing one.</para>
    ///
    /// <para>⚠ Grid only. <c>Flow</c> positions by document order, so there is no cell a point could mean, and drawing a
    /// guess is exactly the preview this canvas must not pretend to be. A <c>Canvas</c> page never comes here: it
    /// speaks pixels (<see cref="FormVocabulary.IsPixel(FormDocument)"/>) and takes the pixel walk.</para>
    /// </summary>
    private static IEnumerable<FormLayoutEntry> WebLayout(FormDocument document)
    {
        if (document.Layout?.Kind != FormLayoutKind.Grid)
        {
            yield break;
        }

        var surface = SurfaceSize(document);
        foreach (var control in document.Controls)
        {
            if (control.Geometry is GridGeometry grid)
            {
                yield return new FormLayoutEntry(
                    control,
                    FormGridLayout.CellRect(document.Layout, surface, grid.Col, grid.Row, grid.ColSpan, grid.RowSpan),
                    FormLayoutRole.Control);
            }
        }
    }
```

6. **Private `Layout`.** Replace it (`:870-898`) with:

```csharp
    private static IEnumerable<FormLayoutEntry> Layout(
        IReadOnlyList<FormControl> controls, Point containerOrigin, FormDockLayoutResult dock,
        Dictionary<FormControl, Point> origins)
    {
        foreach (var control in controls)
        {
            // ⛔ A Docked strip is yielded by Bands() and by nothing else — never through BoundsOf,
            // which knows only pixel geometry and would drop it silently (or, if one ever acquired
            // geometry, yield it TWICE, once as a Control and once as a Band). Skipping the strip
            // also skips its ITEMS, which carry no pixel geometry either: Bands() places them, as
            // cells derived from the band's own rectangle, and that is their only route here.
            if (control.Definition?.Place == FormPlace.Docked)
            {
                continue;
            }

            var bounds = BoundsOf(control, containerOrigin, dock);
            if (bounds == null)
            {
                continue;
            }

            // Its CLIENT origin, for Bands: a strip docked inside this control is placed from here.
            origins[control] = bounds.Value.TopLeft;

            yield return new FormLayoutEntry(control, bounds.Value, FormLayoutRole.Control);

            foreach (var nested in Layout(control.Children, bounds.Value.TopLeft, dock, origins))
            {
                yield return nested;
            }
        }
    }
```

7. **The two test callers (B2).**
   - `VisualGameStudio.Tests/Shell/FormCanvasMultiSelectTests.cs:36`: `FormCanvasTransform.BoundsOf(control, default)!.Value` → `FormCanvasTransform.BoundsOf(control, default, FormDockLayout.Resolve(Doc))!.Value`.
   - `VisualGameStudio.Tests/Shell/FormCanvasDoubleClickTests.cs:78`: `FormCanvasTransform.BoundsOf(control, default)` → `FormCanvasTransform.BoundsOf(control, default, FormDockLayout.Resolve(doc))`.
   - Both files already have `using BasicLang.Forms;` (check; add it if absent).

- [ ] Build. Run Step 1's filter plus `FullyQualifiedName~FormStrip|FullyQualifiedName~FormCanvasMultiSelectTests|FullyQualifiedName~FormCanvasDoubleClickTests|FullyQualifiedName~FormMenuEditorDefectsTests|FullyQualifiedName~FormZOrderTests|FullyQualifiedName~FormDockLayoutTests`.

**Expected GREEN**, all. The strip fixtures carry no positioned Dock, so their bands are byte-identical (A#25). If any strip test moves, stop: that is a stacking difference, not an intended change.

#### Step 3: Commit (2 min)
- Stage by name: `FormCanvasTransform.cs`, `FormCanvasTransformTests.cs`, `FormCanvasRenderTests.cs`, `FormCanvasMultiSelectTests.cs`, `FormCanvasDoubleClickTests.cs`.
- Message: `feat(designer): the canvas lays a Canvas page out in pixels and draws every docked thing where FormDockLayout puts it (Task 9)`. In the body: B2, B5, C1 (S12 recorded), C3, and "no positioned-Dock canvas fixture existed (spec-claims #8), so no expectation changed".

### Part 2: Placement and geometry edits

#### Step 4: Failing tests (5 min)
1. `VisualGameStudio.Tests/Compiler/FormGeometryEditTests.cs`: add at the end of the class.

```csharp
    // ==================================================================
    // Task 9 (spec 2026-09-27 §7a) — a docked control's place comes from docking; a docked container's box is the
    // one it is DRAWN as (FormDockLayoutResult's client size)
    // ==================================================================

    private static FormControl DockedFill(string id)
    {
        var fill = Control("Panel", id, 7, 7, 10, 10); // stored 10x10 — stale by design
        Pixel(fill).Dock = "Fill";
        return fill;
    }

    [Test]
    public void ADockedControl_IsNeitherMovedNorResized()
    {
        var document = Document();
        var panel = Control("Panel", "pnl", 30, 40, 100, 50);
        Pixel(panel).Dock = "Top";
        document.Controls.Add(panel);

        Assert.Multiple(() =>
        {
            Assert.That(FormGeometryEdit.MoveTo(document, panel, 60, 70), Is.False, "MoveTo");
            Assert.That(FormGeometryEdit.MoveToForm(document, panel, 60, 70), Is.False, "MoveToForm");
            Assert.That(FormGeometryEdit.Resize(document, panel, FormResizeHandle.BottomRight, 10, 10), Is.False, "Resize");
            Assert.That((Pixel(panel).X, Pixel(panel).Y, Pixel(panel).Width, Pixel(panel).Height),
                Is.EqualTo((30, 40, 100, 50)), "a drag would write X/Y the runtime ignores");
        });
    }

    [Test]
    public void ADockNamedNone_StillMoves()
    {
        var document = Document();
        var button = Control("Button", "btn", 10, 10, 75, 23);
        Pixel(button).Dock = "None";
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveTo(document, button, 40, 40), Is.True,
            "\"None\" does not dock (FormDockLayout.EdgeOf)");
    }

    [Test]
    public void DraggingIntoADockedFillPanel_UsesWhereItIsDrawn_AndItsResolvedSize()
    {
        var document = Document(640, 480);
        var fill = DockedFill("fill");
        var button = Control("Button", "btn", 500, 5, 75, 23);
        document.Controls.Add(new FormControl { Kind = "MenuStrip", Id = "menuStrip1" });
        document.Controls.Add(fill);
        document.Controls.Add(button);

        var changed = FormGeometryEdit.MoveToForm(document, button, 100, 200);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(fill.Children, Is.EqualTo(new[] { button }), "the Panel is drawn under the point");
            Assert.That((Pixel(button).X, Pixel(button).Y), Is.EqualTo((100, 176)),
                "relative to (0, 24) and not clamped to the stale 10x10");
        });
    }

    [Test]
    public void AChildOfADockedFillPanel_IsClampedToThePanelsResolvedSize()
    {
        var document = Document(640, 480);
        var fill = DockedFill("fill");
        var button = Control("Button", "btn", 10, 10, 75, 23);
        fill.Children.Add(button);
        document.Controls.Add(fill);

        FormGeometryEdit.MoveTo(document, button, 300, 300);

        Assert.That((Pixel(button).X, Pixel(button).Y), Is.EqualTo((300, 300)),
            "the Fill Panel is 640x480 here, so (300, 300) fits; the stale 10x10 would clamp it to (0, 0)");
    }
```

2. `VisualGameStudio.Tests/Compiler/FormPlacementTests.cs`: add after `ARefusal_LeavesTheDocumentExactlyAsItWas` (`:537`).

```csharp
    // ==================================================================
    // Task 9 — a Canvas page places like a WinForms form (spec 2026-09-27 §2.4)
    // ==================================================================

    private static FormDocument CanvasPage(int width = 800, int height = 450) => new()
    {
        Target = FormTarget.Web,
        Name = "LoginForm",
        Width = width,
        Height = height,
        Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = "600" }
    };

    [Test]
    public void ADropOnACanvasPage_LandsInPixels_AtThePoint()
    {
        var document = CanvasPage();
        var row = FormControlCatalog.Find("Button")!;

        var result = FormPlacement.Place(document, "Button", 96, 80);

        Assert.That(result.Refusal, Is.Null, "a Canvas page used to be refused as a page with no cells");
        var pixel = result.Control!.Geometry as PixelGeometry;
        Assert.Multiple(() =>
        {
            Assert.That(pixel, Is.Not.Null, "a Canvas page speaks pixels, never cells");
            Assert.That((pixel!.X, pixel.Y, pixel.Width, pixel.Height),
                Is.EqualTo((96, 80, row.DefaultWidth, row.DefaultHeight)));
            Assert.That(result.Control.TabIndex, Is.EqualTo(0));
            Assert.That(result.Control.Properties["Text"], Is.EqualTo("Button1"));
            Assert.That(document.Controls, Is.EqualTo(new[] { result.Control }));
        });
    }

    [Test]
    public void ADropNearACanvasPagesEdge_IsPulledBackOntoTheDesignSize()
    {
        var document = CanvasPage();
        var row = FormControlCatalog.Find("Button")!;

        var pixel = (PixelGeometry)FormPlacement.Place(document, "Button", 795, 445).Control!.Geometry!;

        Assert.That((pixel.X, pixel.Y), Is.EqualTo((800 - row.DefaultWidth, 450 - row.DefaultHeight)));
    }

    [Test]
    public void ADropInsideAPanelOnACanvasPage_IsAChild_PositionedRelativeToIt()
    {
        var document = CanvasPage();
        var panel = Existing("Panel", "pnl", 100, 100, 300, 200);
        document.Controls.Add(panel);

        var result = FormPlacement.Place(document, "Button", 150, 140);

        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children, Is.EqualTo(new[] { result.Control }));
            Assert.That((pixel.X, pixel.Y), Is.EqualTo((50, 40)));
        });
    }

    [Test]
    public void ADropIntoADockedFillPanel_IsRelativeToWhereItIsDrawn_AndClampedToItsResolvedSize()
    {
        var document = WinFormsDocument(640, 480);
        document.Controls.Add(new FormControl { Kind = "MenuStrip", Id = "menuStrip1" });
        var fill = Existing("Panel", "fill", 7, 7, 10, 10);
        ((PixelGeometry)fill.Geometry!).Dock = "Fill";
        document.Controls.Add(fill);

        var result = FormPlacement.Place(document, "Button", 104, 200);

        var pixel = (PixelGeometry)result.Control!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(fill.Children, Is.EqualTo(new[] { result.Control }), "the Panel is drawn under the point");
            Assert.That((pixel.X, pixel.Y), Is.EqualTo((104, 176)),
                "relative to (0, 24), and not clamped to the stale 10x10 (B3)");
        });
    }
```

3. `VisualGameStudio.Tests/Compiler/FormCanvasDropTests.cs`: add after `AWebDropSurvivesAReRead_InTheSameCell` (`:194`).

```csharp
    [Test]
    public void ADropOnANewWebForm_WhichIsACanvasPage_ReachesTheFileInPixels()
    {
        // ⛔⛔ Spec 2026-09-27 D1/§2.4: a NEW web form is a Canvas page (Task 5), and PlaceOnWeb refused every drop on it
        // until Task 9 — the scaffold's default shape could not be designed at all.
        var (vm, _) = Open(FormTarget.Web);

        var refusal = vm.PlaceControl("Button", 96, 80);

        Assert.That(refusal, Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Contain("<Layout Kind=\"Canvas\""), "precondition: the scaffold is a Canvas page");
            Assert.That(vm.Text, Does.Contain("Id=\"Button1\""));
            Assert.That(vm.Text, Does.Contain("X=\"96\""));
            Assert.That(vm.Text, Does.Contain("Y=\"80\""));
            Assert.That(vm.Text, Does.Not.Contain("Col=\""), "a Canvas page has no cells");
        });

        var reread = BasicLang.Forms.Serialization.FormDocumentReader.Read(vm.FilePath!, vm.Text);
        Assert.That(reread.Model.FindById("Button1")?.Geometry, Is.InstanceOf<PixelGeometry>());
    }
```

- [ ] Build. Run `FullyQualifiedName~FormGeometryEditTests|FullyQualifiedName~FormPlacementTests|FullyQualifiedName~FormCanvasDropTests`.

**Expected RED**
- `ADockedControl_IsNeitherMovedNorResized`: MoveTo returns true.
- `DraggingIntoADockedFillPanel…`: ContainerAt now finds the Fill panel (Part 1), but the clamp uses the stale 10×10, so X/Y = (0, 0).
- `AChildOfADockedFillPanel…`: (0, 0).
- The 3 Canvas `FormPlacementTests`: refused, and the message names Canvas.
- `ADropIntoADockedFillPanel…`: (0, 0).
- `ADropOnANewWebForm…`: refused.

**GREEN guard:** `ADockNamedNone_StillMoves`.

#### Step 5: Implement `FormGeometryEdit` (4 min)
Edit `VisualGameStudio.Shell/ViewModels/Designer/FormGeometryEdit.cs`:
- `MoveTo` `:55` and `MoveToForm` `:97`: change `if (control.Geometry is not PixelGeometry pixel)` to `if (control.Geometry is not PixelGeometry pixel || IsDocked(control))`.
- `Resize` `:178`: change to `if (handle == FormResizeHandle.None || control.Geometry is not PixelGeometry pixel || IsDocked(control))`.
- Add after `Clamp` (`:239-240`):

```csharp
    /// <summary>
    /// ⛔ A DOCKED control is never moved or resized here (spec 2026-09-27 §7a — Visual Studio's rule): its rectangle
    /// comes from docking, and X/Y written by a drag, a nudge or a group move would be numbers the runtime ignores.
    /// Asked HERE, the one place every canvas gesture writes geometry through, so the keyboard and the group loop cannot
    /// forget it. "None" and an unknown name do not dock (<see cref="FormDockLayout.EdgeOf"/>).
    /// </summary>
    private static bool IsDocked(FormControl control) => FormDockLayout.EdgeOf(control) != null;
```

- Replace `SurfaceOf` (`:250-260`) with:

```csharp
    /// <summary>
    /// The usable box inside a container, or the form's client size when there is none.
    ///
    /// <para>⛔ A container's box is <see cref="FormDockLayoutResult.TryGetClientSize"/>'s answer — its RESOLVED size
    /// when it docks (a Fill Panel is as big as what is left, not its stale stored Width/Height), else its stored
    /// size — the one rule the resolver and the page emitter share (plan 2026-09-27 Task 9, B3). ⚠ Internal because
    /// <see cref="FormPlacement"/> clamps a drop with it too: the drop's box and the drag's box were a mirrored
    /// pair.</para>
    /// </summary>
    internal static (int Width, int Height) SurfaceOf(FormDocument document, FormControl? container)
    {
        if (container != null && FormDockLayout.Resolve(document).TryGetClientSize(container, out var client))
        {
            return client;
        }

        var surface = FormCanvasTransform.SurfaceSize(document);
        return ((int)surface.Width, (int)surface.Height);
    }
```

- Change the `SurfaceFor` summary's "The form fallbacks match `FormCanvasControl.Fit`'s" to "The form's own size is `SurfaceSize`'s, the one answer `Fit` draws with".

#### Step 6: Implement `FormPlacement` (3 min)
Edit `VisualGameStudio.Shell/ViewModels/Designer/FormPlacement.cs`:
- Replace `:99-104` (`// ⛔ D3. A .blwebform positions controls by CELL…` through the `PlaceOnWeb` return) with:

```csharp
        // ⛔ D3, by VOCABULARY (spec 2026-09-27 §2.4): a Grid or Flow page positions controls by CELL, so its path
        // produces a GridGeometry from the cell the pointer is in. A Canvas page speaks PIXELS and places exactly as a
        // .blform does, below — it used to be refused here because this asked the TARGET.
        if (!FormVocabulary.IsPixel(document))
        {
            return PlaceOnWeb(document, definition, x, y);
        }
```

- `PlaceOnWeb` summary (`:143-155`): change "A drop on a `<c>.blwebform</c>`" to "A drop on a Grid or Flow page". Add a `<para>`: "⚠ A Canvas page never arrives here — it speaks pixels (`FormVocabulary.IsPixel`) — so the Kind refusal below can only ever say Flow." The refusal text itself is unchanged (C4).
- Delete `FormPlacement.SurfaceOf` (`:287-301`) and change its call (`:115`) to `var (surfaceWidth, surfaceHeight) = FormGeometryEdit.SurfaceOf(document, container?.Container);`.
- [ ] Build. Run Step 4's filter plus `FullyQualifiedName~FormCanvasTransformTests|FullyQualifiedName~FormCanvasUndoTests|FullyQualifiedName~FormDesignerCommandTests`.

**Expected GREEN**, all.

#### Step 7: Commit (2 min)
- Stage `FormGeometryEdit.cs`, `FormPlacement.cs`, `FormGeometryEditTests.cs`, `FormPlacementTests.cs`, `FormCanvasDropTests.cs`.
- Message: `feat(designer): a Canvas page places in pixels; a docked control is never moved, and a docked container clamps to the size it is drawn (Task 9)`. In the body: B3, B4 (model half), C4.

### Part 3: The canvas control (page chrome, grips, docked gestures)

#### Step 8: Failing canvas-level tests (5 min)
Create `VisualGameStudio.Tests/Shell/FormCanvasPixelPageTests.cs` (a NEW file, so Write is allowed):

```csharp
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Spec 2026-09-27 §2.4 / §7a, driven through the real <see cref="FormCanvasControl"/> and its real pixels: a Canvas web
/// page gets the alignment grid and the form grips but no window frame, and a DOCKED control has no handles and cannot
/// be dragged. Canvas-level on purpose (pre-flight B7): the real document view cannot pin the zoom, and the alignment
/// dots vanish below 0.5. The two windows below fit an 800x450 page at 0.815 and at 1:1.
/// </summary>
[TestFixture]
public class FormCanvasPixelPageTests
{
    private static readonly Color Navy = Color.FromRgb(0x00, 0x00, 0x80);
    private static readonly Color Face = Color.FromRgb(0xD4, 0xD0, 0xC8);

    /// <summary>(700,500) fits 800x450 at 0.815; (900,600) at 1.0 (Fit never enlarges past 1:1).</summary>
    private static readonly (double Width, double Height)[] TwoZooms = { (700, 500), (900, 600) };

    private sealed class Recorder : System.Windows.Input.ICommand
    {
        public int Executions { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Executions++;
    }

    private static FormDocument Page(FormLayoutKind kind) => new()
    {
        Target = FormTarget.Web,
        Name = "P",
        Width = 800,
        Height = 450,
        Layout = kind == FormLayoutKind.Grid
            ? new FormLayout { Kind = kind, Cols = "1fr,1fr", Rows = "1fr,1fr", Gap = "0px" }
            : new FormLayout { Kind = kind }
    };

    private static FormDocument AWindow() => new() { Target = FormTarget.WinForms, Name = "P", Width = 800, Height = 450 };

    /// <summary>A Canvas page: a MenuStrip, a Dock=Top Panel (stored X/Y stale by design) and an undocked Button.</summary>
    private static (FormDocument Doc, FormControl Panel, FormControl Button) DockedPage()
    {
        var doc = Page(FormLayoutKind.Canvas);
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl",
            Geometry = new PixelGeometry { X = 300, Y = 300, Width = 120, Height = 40, Dock = "Top" }
        };
        var button = new FormControl
        {
            Kind = "Button", Id = "btn",
            Geometry = new PixelGeometry { X = 40, Y = 120, Width = 75, Height = 23 }
        };
        doc.Controls.Add(new FormControl { Kind = "MenuStrip", Id = "menuStrip1" });
        doc.Controls.Add(panel);
        doc.Controls.Add(button);
        return (doc, panel, button);
    }

    private sealed class Surface : IDisposable
    {
        public required FormCanvasControl Canvas { get; init; }
        public required Window Window { get; init; }
        public required FormDocument Doc { get; init; }

        private FormCanvasTransform Fit => FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);

        /// <summary>The form's client rectangle in canvas (= window) pixels, through the canvas's own Fit.</summary>
        public Rect SurfaceRect
        {
            get
            {
                var size = FormCanvasTransform.SurfaceSize(Doc);
                return Fit.ToCanvas(new Rect(0, 0, size.Width, size.Height));
            }
        }

        /// <summary>A FORM point in canvas (= window) pixels.</summary>
        public Point At(double x, double y) => Fit.ToCanvas(new Point(x, y));

        public T Frame<T>(Func<WriteableBitmap, T> read)
        {
            using var frame = Window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("No rendered frame — Skia is required (DesignerHeadlessApp).");
            return read(frame);
        }

        public void Drag(Point from, Point to)
        {
            Window.MouseDown(from, MouseButton.Left);
            Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(to, MouseButton.Left);
        }

        public void Dispose() => Window.Close();
    }

    private static Surface Open(
        FormDocument doc, double width, double height, FormControl? selected = null, FormSelection? selection = null)
    {
        var canvas = new FormCanvasControl { Document = doc, SelectedControl = selected, Selection = selection };
        var window = new Window { Width = width, Height = height, Content = canvas };
        window.Show();
        canvas.Focus();
        return new Surface { Canvas = canvas, Window = window, Doc = doc };
    }

    private static Color PixelAt(WriteableBitmap frame, Point at)
    {
        using var fb = frame.Lock();
        var scale = frame.Size.Width > 0 ? fb.Size.Width / frame.Size.Width : 1;
        var px = new byte[4];
        Marshal.Copy(fb.Address + ((int)(at.Y * scale) * fb.RowBytes) + ((int)(at.X * scale) * 4), px, 0, 4);
        return fb.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(px[3], px[0], px[1], px[2])
            : Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    private static int CountOtherThan(WriteableBitmap frame, Rect region, Color colour)
    {
        using var fb = frame.Lock();
        var scale = frame.Size.Width > 0 ? fb.Size.Width / frame.Size.Width : 1;
        var row = new byte[fb.RowBytes];
        var count = 0;
        for (var y = (int)Math.Ceiling(region.Y * scale); y < (int)(region.Bottom * scale); y++)
        {
            Marshal.Copy(fb.Address + (y * fb.RowBytes), row, 0, fb.RowBytes);
            for (var x = (int)Math.Ceiling(region.X * scale); x < (int)(region.Right * scale); x++)
            {
                var c = fb.Format == PixelFormat.Rgba8888
                    ? Color.FromArgb(row[x * 4 + 3], row[x * 4], row[x * 4 + 1], row[x * 4 + 2])
                    : Color.FromArgb(row[x * 4 + 3], row[x * 4 + 2], row[x * 4 + 1], row[x * 4]);
                if (c != colour)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string Hash(WriteableBitmap frame)
    {
        using var stream = new MemoryStream();
        frame.Save(stream);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    // ==================================================================
    // The page's chrome
    // ==================================================================

    [AvaloniaTest]
    public void ACanvasPage_HasNoTitleBar_WhileAWindowDoes_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            // ⚠ The MIDDLE of where an 18px title bar would be: a page's caption is drawn at the left (B6).
            Color window, page;
            using (var s = Open(AWindow(), w, h))
            {
                window = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Center.X, s.SurfaceRect.Y - 9)));
            }

            using (var s = Open(Page(FormLayoutKind.Canvas), w, h))
            {
                page = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Center.X, s.SurfaceRect.Y - 9)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(window, Is.EqualTo(Navy), $"{w}x{h}: control — the sample lands in a WINDOW's title bar");
                Assert.That(page, Is.Not.EqualTo(Navy), $"{w}x{h}: a page is not a window (spec §2.4)");
            });
        }
    }

    [AvaloniaTest]
    public void ACanvasPage_IsDesignedOnTheAlignmentGrid_AFlowPageIsNot_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            int canvasDots, flowDots;
            using (var s = Open(Page(FormLayoutKind.Canvas), w, h))
            {
                canvasDots = s.Frame(f => CountOtherThan(f, s.SurfaceRect.Deflate(6), Face));
            }

            using (var s = Open(Page(FormLayoutKind.Flow), w, h))
            {
                flowDots = s.Frame(f => CountOtherThan(f, s.SurfaceRect.Deflate(6), Face));
            }

            Assert.Multiple(() =>
            {
                Assert.That(flowDots, Is.Zero,
                    $"{w}x{h}: control — an empty Flow page's face is plain, so the counter can see absence");
                Assert.That(canvasDots, Is.GreaterThan(100), $"{w}x{h}: the alignment dots (spec §2.4)");
            });
        }
    }

    [AvaloniaTest]
    public void ACanvasPage_HasTheFormGrips_AGridPageDoesNot_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            Color canvasGrip, gridGrip;
            using (var s = Open(Page(FormLayoutKind.Canvas), w, h))
            {
                canvasGrip = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Right, s.SurfaceRect.Center.Y)));
            }

            using (var s = Open(Page(FormLayoutKind.Grid), w, h))
            {
                gridGrip = s.Frame(f => PixelAt(f, new Point(s.SurfaceRect.Right, s.SurfaceRect.Center.Y)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(canvasGrip, Is.EqualTo(Navy), $"{w}x{h}: the right-hand form grip's centre");
                Assert.That(gridGrip, Is.Not.EqualTo(Navy), $"{w}x{h}: a Grid page has no design size to drag");
            });
        }
    }

    // ==================================================================
    // A docked control (spec §7a): no handles, no drag
    // ==================================================================

    [AvaloniaTest]
    public void ADockedControl_GetsNoHandles_ButAnUndockedOneDoes_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            var (doc, panel, button) = DockedPage();
            using var s = Open(doc, w, h, selected: panel);

            // The left-middle handle's centre, where the Panel is DRAWN: (0, 24, 800, 40).
            var docked = s.Frame(f => PixelAt(f, s.At(0, 24 + 20)));
            s.Canvas.SelectedControl = button;
            var undocked = s.Frame(f => PixelAt(f, s.At(40, 120)));   // the Button's top-left handle's centre

            Assert.Multiple(() =>
            {
                Assert.That(undocked, Is.EqualTo(Navy), $"{w}x{h}: control — the sampler sees a handle");
                Assert.That(docked, Is.Not.EqualTo(Navy), $"{w}x{h}: a docked control has no handles");
            });
        }
    }

    [AvaloniaTest]
    public void ASelectedDockedControl_IsStillShownSelected()
    {
        var (doc, panel, _) = DockedPage();
        using var s = Open(doc, 900, 600);

        var unselected = s.Frame(Hash);
        s.Canvas.SelectedControl = panel;
        var selected = s.Frame(Hash);

        Assert.That(selected, Is.Not.EqualTo(unselected),
            "no handles (spec §7a) must not mean no sign of the selection at all (pre-flight decision 1)");
    }

    [AvaloniaTest]
    public void ADragStartingOnADockedControl_SelectsIt_AndMovesNothing_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            var (doc, panel, _) = DockedPage();
            using var s = Open(doc, w, h, selection: new FormSelection());
            var commits = new Recorder();
            s.Canvas.CommitGeometryCommand = commits;

            var from = s.At(600, 44);
            s.Drag(from, from + new Point(60, 40));

            var g = (PixelGeometry)panel.Geometry!;
            Assert.Multiple(() =>
            {
                Assert.That(s.Canvas.SelectedControl, Is.SameAs(panel), $"{w}x{h}: the press selects it");
                Assert.That((g.X, g.Y, g.Width, g.Height), Is.EqualTo((300, 300, 120, 40)), $"{w}x{h}: and moves nothing");
                Assert.That(doc.Controls.IndexOf(panel), Is.EqualTo(1), $"{w}x{h}: nor re-parents it");
                Assert.That(commits.Executions, Is.Zero, $"{w}x{h}: nothing to commit");
            });
        }
    }

    [AvaloniaTest]
    public void ADragStartingOnADockedPrimary_MovesNoOtherSelectedControlEither()
    {
        // ⛔ B4: the model refuses the docked primary, but a drag armed on it would still move every OTHER selected
        // control through the group loop. "A drag starting on it selects only" (spec §7a).
        var (doc, panel, button) = DockedPage();
        var selection = new FormSelection();
        selection.SetRange(new[] { button, panel });   // the Panel is the primary (the LAST member)
        using var s = Open(doc, 900, 600, selected: panel, selection: selection);

        var from = s.At(600, 44);
        s.Drag(from, from + new Point(64, 48));

        var g = (PixelGeometry)button.Geometry!;
        Assert.That((g.X, g.Y), Is.EqualTo((40, 120)), "the drag started on a docked control, so nothing moves");
    }

    [AvaloniaTest]
    public void TheArrowKeys_NeitherNudgeNorResizeADockedControl()
    {
        var (doc, panel, _) = DockedPage();
        using var s = Open(doc, 900, 600, selected: panel);

        s.Window.KeyPress(Key.Right, RawInputModifiers.None);
        s.Window.KeyPress(Key.Down, RawInputModifiers.Shift);
        s.Window.KeyPress(Key.Right, RawInputModifiers.Control);

        var g = (PixelGeometry)panel.Geometry!;
        Assert.That((g.X, g.Y, g.Width, g.Height), Is.EqualTo((300, 300, 120, 40)));
    }
}
```

(`FormSelection.SetRange(IEnumerable<FormControl>)`, `FormSelection.cs:96`, so the array is fine.)

- [ ] Build. Run `FullyQualifiedName~FormCanvasPixelPageTests`.

**Expected RED**
- `…IsDesignedOnTheAlignmentGrid…`: canvasDots 0.
- `…HasTheFormGrips…`: canvasGrip is not navy.
- `ADockedControl_GetsNoHandles…`: docked is navy.
- `ADragStartingOnADockedPrimary…`: the button moves by the delta.

**Already GREEN (guards).** Mutations M9.9 and M9.7 make them bite:
- `…HasNoTitleBar…`: the page never had one.
- `ASelectedDockedControl_IsStillShownSelected`: it gets handles today.
- `ADragStartingOnADockedControl_SelectsIt…`: Part 2's model guard.
- `TheArrowKeys…`: Part 2's model guard.

#### Step 9: Implement `FormCanvasControl` (5 min)
Edit `VisualGameStudio.Shell/Controls/FormCanvasControl.cs`:

1. **Grip press** `:879-883`. Replace the comment tail "Pixel forms only — a web page has no client size to drag." with "Pixel documents only (`FormVocabulary.IsPixel` — a .blform or a Canvas page): a Grid/Flow page has no design size to drag." Replace `document.Target == FormTarget.WinForms` with `FormVocabulary.IsPixel(document)`.

2. **Pixel drag branch** `:983`. Change `else if (SelectedControl?.Geometry is PixelGeometry pixel)` to:

```csharp
        // ⛔ Not for a DOCKED primary (spec §7a, pre-flight B4): "a drag starting on it selects only". FormGeometryEdit
        // refuses to move it anyway, but a drag armed here would still carry every OTHER selected control along.
        else if (SelectedControl?.Geometry is PixelGeometry pixel && FormDockLayout.EdgeOf(SelectedControl) == null)
```

3. **`HandleUnder`** `:1266-1279`. Replace the guard `if (SelectedControl?.Geometry is not PixelGeometry || CanvasBoundsOf(document, SelectedControl) is not { } bounds)` with `if (!HasHandles(SelectedControl) || CanvasBoundsOf(document, SelectedControl!) is not { } bounds)`. Then add after the method:

```csharp
    /// <summary>
    /// ⛔ ONE predicate for "does this control get resize handles", asked by <see cref="HandleUnder"/> (the gesture)
    /// and by Render (the picture), so the two can never disagree. Pixel geometry only — a Grid page's control IS its
    /// cell — and never a DOCKED control (spec 2026-09-27 §7a): its rectangle comes from docking, and
    /// <c>FormGeometryEdit</c> refuses to move or resize it.
    /// </summary>
    private static bool HasHandles(FormControl? control) =>
        control?.Geometry is PixelGeometry && FormDockLayout.EdgeOf(control) == null;
```

4. **Render handles** `:1550-1554`. Replace them with:

```csharp
        if (HasHandles(SelectedControl) && CanvasBoundsOf(document, SelectedControl!) is { } selection)
        {
            DrawHandles(context, selection);
        }
        else if (SelectedControl?.Geometry is PixelGeometry && CanvasBoundsOf(document, SelectedControl) is { } docked)
        {
            // A DOCKED primary (spec §7a): selected, but not draggable — outlined like a secondary member, never
            // handled. Without this a click on a docked Panel shows nothing at all (pre-flight decision 1).
            context.DrawRectangle(null, SecondarySelectionPen, docked);
        }
```

5. **Render grips** `:1566`. Change `if (document.Target == FormTarget.WinForms)` to `if (FormVocabulary.IsPixel(document))`. Change the comment above it to: "The form's own grips — on every document that has a design size (a .blform or a Canvas page)."

6. **`DrawSurface`** `:1771-1832`. Replace the body from `var isWindow = document.Target == FormTarget.WinForms;` through the end of the method with:

```csharp
        // ⛔ TARGET-only, deliberately (plan Traps): the title bar and frame say "this is a WINDOW", which a page never
        // is, however it is laid out. Every other decision below is about the VOCABULARY.
        var isWindow = document.Target == FormTarget.WinForms;

        // A document that speaks PIXELS (a .blform, or a Canvas page — FormVocabulary) is designed on the alignment
        // grid (spec 2026-09-27 §2.4).
        var isPixel = FormVocabulary.IsPixel(document);

        // Cells are a Grid-only idea.
        var isGrid = !isWindow && document.Layout?.Kind == FormLayoutKind.Grid;

        // A PAGE is outlined and captioned ABOVE its surface — never given a title bar. ⚠ A Flow page keeps what it had
        // (neither): unchanged by this task.
        var isOutlinedPage = !isWindow && (isGrid || isPixel);

        if (isWindow)
        {
            // … the existing title bar / bevel / buttons / caption block, UNCHANGED (:1783-1803) …
        }

        context.FillRectangle(SurfaceBrush, surface);

        if (isPixel)
        {
            DrawAlignmentGrid(context, size, surface);
        }

        if (isOutlinedPage)
        {
            context.DrawRectangle(null, ShadowPen, surface);
        }

        // ⛔ The web cell guides, UNDER the controls. Without them a Grid page is a blank rectangle with no clue where
        // a drop will land — the cells are the only thing on screen that says what its geometry even means.
        if (isGrid)
        {
            foreach (var (_, _, cell) in FormGridLayout.Cells(document.Layout!, size))
            {
                context.DrawRectangle(null, GridPen, _transform.ToCanvas(cell));
            }
        }

        if (isOutlinedPage)
        {
            var pageCaption = document.Text ?? document.Name;
            if (!string.IsNullOrEmpty(pageCaption))
            {
                context.DrawText(
                    Text(pageCaption, LabelBrush),
                    new Point(surface.X + 4, Math.Max(0, surface.Y - 16)));
            }
        }
```

   (Copy the `if (isWindow) { … }` block verbatim from `:1781-1804`. Only the lines around it change.) Update the method summary's first sentence to: "The form itself: a WINDOW is drawn the way VB6 draws it (title bar, raised frame, alignment grid); a Canvas PAGE gets the alignment grid and an outline, and no frame (spec 2026-09-27 §2.4)."

   Grid output is unchanged: same outline, cells and caption, in the same order.

- [ ] Build. Run `FullyQualifiedName~FormCanvasPixelPageTests|FullyQualifiedName~FormCanvas|FullyQualifiedName~FormStrip|FullyQualifiedName~FormDesignerRealViewTests|FullyQualifiedName~FormDesignerLayoutRealViewTests|FullyQualifiedName~FormTypeHereEditorTests|FullyQualifiedName~FormMenuEditorDefectsTests|FullyQualifiedName~FormSchematicPinTests`.

**Expected GREEN**, all. `FormDesignerLayoutRealViewTests.InDesignMode_NoCodeEditorIsVisible…` samples the canvas gap bottom-left on a Grid page, which is unchanged.

#### Step 10: Commit (2 min)
- Stage `FormCanvasControl.cs`, `FormCanvasPixelPageTests.cs`.
- Message: `feat(designer): a Canvas page gets the alignment grid and form grips but no title bar; a docked control has no handles and a drag on it only selects (Task 9)`. In the body: B4 (canvas half), B6, B7, decisions 1–2.

### Part 4: A store refusal is SAID (MobileBreakpoint, and ClientSize with it)

#### Step 11: Failing VM tests (4 min)
1. `VisualGameStudio.Tests/Compiler/FormPropertyGridDisplayTests.cs`: add after `ANonPositiveClientSize_IsRefused_AndRaisesNoEdit` (`:379-397`).

```csharp
    private const string CanvasPageDoc = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas" MobileBreakpoint="600"/>
          <Controls/>
        </WebForm>
        """;

    /// <summary>
    /// ⛔ Plan 2026-09-27 Task 9 (pre-flight B1): a value the STORE refuses used to snap back with no reason — the
    /// catalog accepted it, so no refusal was ever named. The store's own rule (FormRootValues.RefusalOf) names it now.
    /// </summary>
    [Test]
    public void ANonPositiveClientSize_IsRefused_AndTheRowSaysWhy()
    {
        var (_, grid) = Open(select: null);
        var row = grid.Rows.Single(r => r.Name == "ClientSize");

        row.StringValue = "0, 300";

        Assert.Multiple(() =>
        {
            Assert.That(row.Refusal, Does.Contain("'0, 300'").And.Contain("greater than 0").And.Contain("not applied"));
            Assert.That(grid.DescriptionTitle, Is.EqualTo("ClientSize"));
            Assert.That(grid.DescriptionBody, Does.Contain("'0, 300'"));
        });
    }

    [Test]
    public void ANegativeMobileBreakpoint_IsRefused_ChangesNothing_AndTheRowSaysWhy()
    {
        var (file, grid) = Open(select: null, doc: CanvasPageDoc, name: "F.blwebform");
        var edits = 0;
        grid.Edited += (_, _) => edits++;
        var row = grid.Rows.Single(r => r.Name == "MobileBreakpoint");

        row.IntValue = -5;

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero, "a refusal is not an edit");
            Assert.That(file.Model.Layout!.MobileBreakpoint, Is.EqualTo("600"), "never written");
            Assert.That(row.Refusal, Does.Contain("'-5'").And.Contain("0 means never stack").And.Contain("not applied"));
        });
    }

    [Test]
    public void ARespellingTheStoreIgnores_IsNotARefusal()
    {
        var (_, grid) = Open(select: null);
        var row = grid.Rows.Single(r => r.Name == "ClientSize");

        row.StringValue = "400,300";

        Assert.That(row.Refusal, Is.Null, "the same size again changed nothing, and nothing is wrong with it");
    }
```

2. Edit `VisualGameStudio.Tests/Shell/FormPropertyGridRealViewTests.cs` `:446-450` (B1, **INTENDED**). Replace the `⚠ PINNED SILENCE` comment and its two assertions with:

```csharp
            // ⛔ Plan 2026-09-27 Task 9 (pre-flight B1): the store's refusal is NAMED now — this pinned silence was the
            // follow-up slice 2 recorded, and naming MobileBreakpoint's refusal names this one on the same route.
            Assert.That(rig.Row("ClientSize").Refusal, Does.Contain("'0, 300'").And.Contain("not applied"));
            Assert.That(rig.GridVm.DescriptionBody, Does.Contain("'0, 300'"), "the pane says why");
```

   Also change the test's summary to end: "…must snap back to the size the form still has, and the pane must say why."

- [ ] Build. Run `FullyQualifiedName~FormPropertyGridDisplayTests|FullyQualifiedName~FormPropertyGridRealViewTests`.

**Expected RED:** `ANonPositiveClientSize_IsRefused_AndTheRowSaysWhy`, `ANegativeMobileBreakpoint…` (Refusal null), and `AClientSizeTheStoreRefuses_SnapsTheRealEditorBack` (edited).

**GREEN guard:** `ARespellingTheStoreIgnores_IsNotARefusal`.

#### Step 12: Failing `RefusalOf` table (3 min)
1. `VisualGameStudio.Tests/Compiler/FormMobileBreakpointTests.cs`: add after `Set_AcceptsZeroAndPositive_RefusesNegativeAndText_AndNullRemoves` (`:115-133`).

```csharp
    [TestCase("-1", true)]
    [TestCase("wide", true)]
    [TestCase("0", false)]
    [TestCase("0720", false)]
    public void RefusalOf_IsExactlyWhatSetRefuses(string value, bool refused)
    {
        var file = FormDocumentReader.Read("F.blwebform", Page("480"));

        var reason = FormRootValues.RefusalOf(Row, value);

        Assert.Multiple(() =>
        {
            Assert.That(reason != null, Is.EqualTo(refused));
            Assert.That(FormRootValues.Set(file.Model, Row, value), Is.EqualTo(!refused), "ONE rule: Set asks RefusalOf");
            if (refused)
            {
                Assert.That(reason, Does.Contain($"'{value}'").And.EndWith("It was not applied; MobileBreakpoint is unchanged."));
            }
        });
    }
```

2. `VisualGameStudio.Tests/Compiler/FormRootTests.cs`: add at the end of the class.

```csharp
    [TestCase("0, 300", true)]
    [TestCase("640, -1", true)]
    [TestCase("abc", true)]
    [TestCase("640, 480", false)]
    public void RefusalOf_ClientSize_IsExactlyWhatSetRefuses(string value, bool refused)
    {
        var row = FormControlCatalog.FormRoot.Property("ClientSize")!;
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };

        var reason = FormRootValues.RefusalOf(row, value);

        Assert.Multiple(() =>
        {
            Assert.That(reason != null, Is.EqualTo(refused));
            Assert.That(FormRootValues.Set(form, row, value), Is.EqualTo(!refused), "ONE rule: Set asks RefusalOf");
            if (refused)
            {
                Assert.That(reason, Does.Contain($"'{value}'").And.EndWith("It was not applied; ClientSize is unchanged."));
            }
        });
    }

    [Test]
    public void RefusalOf_AnswersForEveryFormRootRow_AndNeverRefusesFreeText()
    {
        Assert.Multiple(() =>
        {
            foreach (var row in FormControlCatalog.FormRoot.Properties)
            {
                Assert.DoesNotThrow(() => FormRootValues.RefusalOf(row, "1"), $"{row.Name} is mapped in RefusalOf");
            }

            foreach (var name in new[] { "Text", "Cols", "Rows", "Gap" })
            {
                Assert.That(FormRootValues.RefusalOf(FormControlCatalog.FormRoot.Property(name)!, "anything at all"), Is.Null, name);
            }
        });
    }
```

   (Check that `FormRootTests.cs` has `using BasicLang.Forms;`. It tests `FormRootValues` already.)

- [ ] Build. **Expected build RED**: `CS0117 'FormRootValues' does not contain a definition for 'RefusalOf'`. That is the red for this table.

#### Step 13: Implement (5 min)
1. `BasicLang/Forms/FormRootValues.cs`: add after `Set`.

```csharp
    /// <summary>
    /// Why <see cref="Set"/> would refuse <paramref name="value"/> for <paramref name="row"/>, or null when it would store
    /// it. ⛔ THE one rule: <see cref="Set"/> asks it, and the property grid shows its text (plan 2026-09-27 Task 9 — a
    /// store refusal used to snap the editor back with no reason, because the catalog accepts the parse). The ending
    /// matches <see cref="FormPropertyDef.DescribeRefusedEdit"/>'s.
    /// </summary>
    /// <exception cref="InvalidOperationException">A FormRoot row this map does not know — map it here.</exception>
    public static string? RefusalOf(FormPropertyDef row, string value)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(value);

        return row.Name switch
        {
            "ClientSize" when !FormPropertyDef.TryParseSize(value, out var width, out var height) || width <= 0 || height <= 0 =>
                $"'{value}' is not a usable size — ClientSize needs a width and a height, both greater than 0. " +
                "It was not applied; ClientSize is unchanged.",
            "MobileBreakpoint" when !FormLayout.TryParseMobileBreakpoint(value, out _) =>
                $"'{value}' is not a usable phone breakpoint — MobileBreakpoint must be a whole number of pixels from 0 " +
                "to 2147483647, where 0 means never stack. It was not applied; MobileBreakpoint is unchanged.",
            "Text" or "ClientSize" or "Cols" or "Rows" or "Gap" or "MobileBreakpoint" => null,
            _ => throw new InvalidOperationException(
                $"FormRoot row '{row.Name}' has no storage in FormRootValues — every root row must be mapped here.")
        };
    }
```

   In `Set`:
   - Replace the ClientSize parse block (`:65-72`) with:

```csharp
                if (RefusalOf(row, value) != null)
                {
                    return false;
                }

                FormPropertyDef.TryParseSize(value, out var width, out var height);
                form.Width = width;
                form.Height = height;
                return true;
```

   - Replace the MobileBreakpoint block (`:97-105`) with:

```csharp
                // ⛔ Refused, never coerced (spec §7): the grid cannot manufacture a Degraded value of its own.
                if (RefusalOf(row, value) != null)
                {
                    return false;
                }

                FormLayout.TryParseMobileBreakpoint(value, out var pixels);
                (form.Layout ??= new FormLayout()).MobileBreakpoint =
                    pixels.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
```

   Add `<see cref="RefusalOf"/>` to the class summary's "What actually reads it TODAY" list (as the grid's reason).

2. `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyRow.cs`:
   - After `_write` (`:77`) add:

```csharp
    /// <summary>
    /// Why the STORE refused a value, for a stored-value row (<see cref="ForStoredValue"/>), or null. ⛔ Asked only when
    /// <see cref="_write"/> returns false, which also means "the same value again" — so it answers null for that, and
    /// non-null only for a genuine refusal (<c>FormRootValues.RefusalOf</c>).
    /// </summary>
    private readonly Func<string, string?>? _storeRefusal;
```

   - Private stored-value constructor (`:125-155`): add the parameter `Func<string, string?>? storeRefusal` after `Action onChanged`, and `_storeRefusal = storeRefusal;` after `_write = write;`.
   - `ForStoredValue` (`:174-183`): add `Func<string, string?>? refusal = null` as the LAST parameter, with the `<param name="refusal">` doc "Names a value the store refuses (shown in the description pane); null for a store that refuses nothing.". Pass it: `new(definition, target, read, write, remove, frozenReason, frozenText, onChanged, refusal)`.
   - `Commit` `:604-608`: replace `if (!_write(value)) { RaiseEditorRefresh(value); return; }` with:

```csharp
            if (!_write(value))
            {
                // ⛔ A store refusal is SAID (plan 2026-09-27 Task 9): a value the catalog accepts and the store refuses
                // (ClientSize "0, 300", MobileBreakpoint -5) used to vanish with no reason. "The same value again"
                // has none, so this stays null for it.
                Refusal = _storeRefusal?.Invoke(value);
                RaiseEditorRefresh(value);
                return;
            }
```

3. `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs` `AddFormRows` (`:487-505`): add `refusal: value => FormRootValues.RefusalOf(definition, value)` after `frozenText: degraded?.Value`.

- [ ] Build. Run `FullyQualifiedName~FormPropertyGridDisplayTests|FullyQualifiedName~FormPropertyGridRealViewTests|FullyQualifiedName~FormPropertyGridTests|FullyQualifiedName~FormMobileBreakpointTests|FullyQualifiedName~FormRootTests|FullyQualifiedName~FormRootLayoutTests|FullyQualifiedName~FormRootRetargetTests`.

**Expected GREEN**, all.

#### Step 14: Commit (2 min)
- Stage `FormRootValues.cs`, `FormPropertyRow.cs`, `FormPropertyGridViewModel.cs`, `FormPropertyGridDisplayTests.cs`, `FormPropertyGridRealViewTests.cs`, `FormMobileBreakpointTests.cs`, `FormRootTests.cs`.
- Message: `feat(designer): a value the form's store refuses (MobileBreakpoint -5, ClientSize 0,300) is refused with its reason (Task 9)`. In the body, one line: `INTENDED: FormPropertyGridRealViewTests.AClientSizeTheStoreRefuses_SnapsTheRealEditorBack now asserts the reason it pinned as absent (pre-flight B1).`

### Part 5: The real document view on a Canvas page

#### Step 15: `FormPixelPageRealViewTests.cs` (5 min)
Create `VisualGameStudio.Tests/Shell/FormPixelPageRealViewTests.cs` (NEW file):

```csharp
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Controls;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Spec 2026-09-27 §7 "Canvas (headless, real IDE view)": a Canvas web page driven through the REAL
/// <see cref="CodeEditorDocumentView"/> (AppStyles loaded, the real view model, a dictionary "disk") — because every
/// piece-level designer test passed while the owner hit real bugs (MEMORY: host the REAL view).
///
/// <para>⛔ Gestures run at TWO window sizes and each asserts one of them is not 1:1 (<c>ToForm</c> is the identity at
/// 1.0, where a missing mapping hides). ⛔ Every window is CLOSED. ⛔ Repeated presses are at different points (two
/// presses at one point with no time between are a double-click). The chrome's pixel rules are proven at the canvas
/// level (<see cref="FormCanvasPixelPageTests"/>, pre-flight B7); here only the title-bar and grip samples repeat.</para>
/// </summary>
[TestFixture]
public class FormPixelPageRealViewTests
{
    private const string Dir = "/proj/";

    private static readonly Color Navy = Color.FromRgb(0x00, 0x00, 0x80);

    private static readonly (double Width, double Height)[] TwoSizes = { (800, 560), (1400, 900) };

    /// <summary>A MenuStrip (24), a Dock=Top Panel (resolved 0,24,800,40 — its stored X/Y stale by design), a Button.</summary>
    private const string CanvasDoc = """
        <WebForm Name="PixelPage" Version="1" Width="800" Height="450" Text="PixelPage">
          <Layout Kind="Canvas" MobileBreakpoint="600"/>
          <Controls>
            <MenuStrip Id="menuStrip1" Dock="Top"/>
            <Panel Id="pnl" X="300" Y="300" Width="120" Height="40" Dock="Top" TabIndex="0"/>
            <Button Id="btn" X="40" Y="120" Width="75" Height="23" TabIndex="1" Text="Go"/>
          </Controls>
        </WebForm>
        """;

    private const string GridDoc = """
        <WebForm Name="PixelPage" Version="1" Text="PixelPage">
          <Layout Kind="Grid" Cols="1fr,1fr" Rows="1fr,1fr" Gap="0px"/>
          <Controls>
            <Button Id="btn" Col="0" Row="0" TabIndex="0" Text="Go"/>
          </Controls>
        </WebForm>
        """;

    private sealed class Files
    {
        public readonly Dictionary<string, string> Contents = new(StringComparer.Ordinal);

        public IFileService Service
        {
            get
            {
                var mock = new Mock<IFileService>();
                mock.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, CancellationToken _) => Task.FromResult(Contents[p]));
                mock.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
                    .Returns((string p) => Task.FromResult(Contents.ContainsKey(p)));
                mock.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string p, string text, CancellationToken _) =>
                    {
                        Contents[p] = text;
                        return Task.CompletedTask;
                    });
                return mock.Object;
            }
        }
    }

    private sealed class Rig : IDisposable
    {
        public required CodeEditorDocumentViewModel Vm { get; init; }
        public required Window Window { get; init; }
        public required FormPropertyGridView Grid { get; init; }
        public required FormCanvasControl Canvas { get; init; }

        public FormPropertyGridViewModel GridVm => Vm.PropertyGrid;
        public ListBox List => Grid.FindControl<ListBox>("PropertyList")!;
        public TextBox Search => Grid.FindControl<TextBox>("SearchBox")!;
        public FormDocument Doc => Vm.DesignDocument!;
        public FormControl Control(string id) => Doc.FindById(id)!;
        public FormPropertyRow Row(string name) => GridVm.Rows.Single(r => r.Name == name);
        private FormCanvasTransform Fit => FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
        public double Zoom => Fit.Zoom;

        /// <summary>A FORM point, in window coordinates — through the canvas's own Fit.</summary>
        public Point InWindow(double formX, double formY) =>
            Canvas.TranslatePoint(Fit.ToCanvas(new Point(formX, formY)), Window)
            ?? throw new InvalidOperationException("the canvas is not in the window");

        public Rect SurfaceInWindow()
        {
            var size = FormCanvasTransform.SurfaceSize(Doc);
            var onCanvas = Fit.ToCanvas(new Rect(0, 0, size.Width, size.Height));
            return new Rect(Canvas.TranslatePoint(onCanvas.TopLeft, Window)!.Value, onCanvas.Size);
        }

        public void Click(Point at)
        {
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public void Click(Visual target, double dx = 0) =>
            Click(target.TranslatePoint(new Point(target.Bounds.Width / 2 + dx, target.Bounds.Height / 2), Window)
                  ?? throw new InvalidOperationException($"{target} is not in the window"));

        /// <summary>A left-button drag by FORM units — scaled by the zoom, so it means the same at every size.</summary>
        public void Drag(Point from, double formDx, double formDy)
        {
            var to = from + new Point(formDx * Zoom, formDy * Zoom);
            Window.MouseDown(from, MouseButton.Left);
            Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// A toolbox drop, as the toolbox packs it (CodeEditorDocumentView.axaml.cs:2180-2181). ⚠ Avalonia.Headless's
        /// DragDrop (pre-flight B8) — if 11.3.13 lacks it, execute Canvas.DropCommand with the snapped request instead
        /// and say so here.
        /// </summary>
        public void DropFromToolbox(string kind, Point at)
        {
            var data = new DataObject();
            data.Set(FormCanvasControl.ControlKindFormat, kind);
            Window.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
            Window.DragDrop(at, RawDragEventType.DragOver, data, DragDropEffects.Copy);
            Window.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy);
            Dispatcher.UIThread.RunJobs();
        }

        public ListBoxItem Container(object item)
        {
            List.ScrollIntoView(item);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            return (ListBoxItem?)List.ContainerFromItem(item)
                   ?? throw new InvalidOperationException($"{item} is not realised in the real grid");
        }

        /// <summary>Types into a row's real NumericUpDown and clicks away (the LostFocus commit).</summary>
        public void TypeInto(FormPropertyRow row, string value, double dx)
        {
            var spinner = Container(row).GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.IsEffectivelyVisible);
            var text = spinner.GetVisualDescendants().OfType<TextBox>().First();
            Click(text, dx);
            Window.KeyPress(Key.A, RawInputModifiers.Control);
            Window.KeyRelease(Key.A, RawInputModifiers.Control);
            Window.KeyTextInput(value);
            Dispatcher.UIThread.RunJobs();
            Click(Search, dx);
            Window.UpdateLayout();
        }

        public void Dispose() => Window.Close();
    }

    private static Rig Open(string doc, double width = 1000, double height = 700)
    {
        var scaffold = FormScaffolder.Create("PixelPage", FormTarget.Web);
        var files = new Files();
        files.Contents[Dir + scaffold.DocumentFileName] = doc;
        files.Contents[Dir + scaffold.CodeFileName] = scaffold.CodeText;

        var vm = new CodeEditorDocumentViewModel(files.Service, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.SetContent(doc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer must open");

        var view = new CodeEditorDocumentView { DataContext = vm };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Styles.Add(new StyleInclude(new Uri("avares://VisualGameStudio/"))
        {
            Source = new Uri("avares://VisualGameStudio/Resources/Styles/AppStyles.axaml")
        });

        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var grid = view.FindControl<FormPropertyGridView>("PropertyGridView")
                       ?? throw new InvalidOperationException("PropertyGridView not found");
            var canvas = view.FindControl<FormCanvasControl>("DesignCanvas")
                         ?? throw new InvalidOperationException("DesignCanvas not found");
            return new Rig { Vm = vm, Window = window, Grid = grid, Canvas = canvas };
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private static Color PixelAt(WriteableBitmap frame, Point at)
    {
        using var fb = frame.Lock();
        var scale = frame.Size.Width > 0 ? fb.Size.Width / frame.Size.Width : 1;
        var px = new byte[4];
        Marshal.Copy(fb.Address + ((int)(at.Y * scale) * fb.RowBytes) + ((int)(at.X * scale) * 4), px, 0, 4);
        return fb.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(px[3], px[0], px[1], px[2])
            : Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    private static PixelGeometry Pixel(FormControl control) => (PixelGeometry)control.Geometry!;

    [AvaloniaTest]
    public void TheControlsSitAtTheirPixels_AndTheDockedPanelWhereItRuns_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);

            rig.Click(rig.InWindow(40 + 37, 120 + 11));   // the Button's STORED rectangle
            var afterButton = rig.Vm.Selection.Primary?.Id;
            rig.Click(rig.InWindow(600, 44));              // the Panel as DOCKED: (0,24,800,40)
            var afterPanel = rig.Vm.Selection.Primary?.Id;
            rig.Click(rig.InWindow(360, 320));             // the Panel's stale STORED rectangle — nothing there
            var afterStale = rig.Vm.Selection.Primary;

            Assert.Multiple(() =>
            {
                Assert.That(afterButton, Is.EqualTo("btn"), $"{w}x{h}: drawn at its pixels");
                Assert.That(afterPanel, Is.EqualTo("pnl"), $"{w}x{h}: the docked Panel is drawn where it runs");
                Assert.That(afterStale, Is.Null, $"{w}x{h}: nothing is drawn at the stale stored X/Y");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void AToolboxDrop_LandsUnderThePointer_InPixels_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);

            rig.DropFromToolbox("Button", rig.InWindow(200, 208));

            var dropped = rig.Doc.FindById("Button1");
            Assert.Multiple(() =>
            {
                Assert.That(dropped?.Geometry, Is.InstanceOf<PixelGeometry>(), $"{w}x{h}: a Canvas page drops in pixels");
                Assert.That((Pixel(dropped!).X, Pixel(dropped!).Y), Is.EqualTo((200, 208)), $"{w}x{h}: under the pointer");
                Assert.That(rig.Vm.Text, Does.Contain("Id=\"Button1\"").And.Contain("X=\"200\"").And.Contain("Y=\"208\""));
                Assert.That(rig.Vm.Selection.Primary, Is.SameAs(dropped), $"{w}x{h}: and is selected");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void DraggingMovesAControl_AndItsHandleResizesIt_AndBothReachTheFile_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);
            var btn = rig.Control("btn");

            rig.Drag(rig.InWindow(77, 131), 40, 16);                  // snapped: (80, 136)
            var moved = (Pixel(btn).X, Pixel(btn).Y);

            rig.Drag(rig.InWindow(80 + 75, 136 + 23), 24, 8);          // its bottom-right handle; resizes are not snapped
            var sized = (Pixel(btn).X, Pixel(btn).Y, Pixel(btn).Width, Pixel(btn).Height);

            Assert.Multiple(() =>
            {
                Assert.That(moved, Is.EqualTo((80, 136)), $"{w}x{h}: the drag moved it");
                Assert.That(sized, Is.EqualTo((80, 136, 99, 31)), $"{w}x{h}: the handle resized it");
                Assert.That(rig.Vm.Text, Does.Contain("X=\"80\"").And.Contain("Y=\"136\"").And.Contain("Width=\"99\""),
                    $"{w}x{h}: both were committed to the file");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void GridEditsOfLocationAnchorAndDock_RedrawAndReachTheFile()
    {
        using var rig = Open(CanvasDoc);
        rig.Click(rig.InWindow(77, 131));
        Assume.That(rig.GridVm.SelectedControl?.Id, Is.EqualTo("btn"), "precondition: btn selected on the canvas");
        var btn = rig.Control("btn");

        rig.TypeInto(rig.Row("X"), "200", 0);
        var afterX = FormCanvasTransform.Layout(rig.Doc).Single(e => ReferenceEquals(e.Control, btn)).Bounds;

        // ⚠ Set through the rows (their pickers' own gestures are FormAnchorDockPickerTests'); this is the redraw.
        rig.Row("Anchor").StringValue = "Top, Right";
        rig.Row("Dock").StringValue = "Bottom";
        Dispatcher.UIThread.RunJobs();

        Assert.That(FormDockLayout.Resolve(rig.Doc).TryGet(btn, out var docked), Is.True);
        var afterDock = FormCanvasTransform.Layout(rig.Doc).Single(e => ReferenceEquals(e.Control, btn)).Bounds;
        rig.Click(rig.InWindow(400, 438));

        Assert.Multiple(() =>
        {
            Assert.That(afterX, Is.EqualTo(new Rect(200, 120, 75, 23)), "X typed in the real editor moved it");
            Assert.That(afterDock, Is.EqualTo(new Rect(docked.Bounds.X, docked.Bounds.Y, docked.Bounds.Width, docked.Bounds.Height)));
            Assert.That(afterDock, Is.EqualTo(new Rect(0, 427, 800, 23)), "Dock=Bottom: the bottom edge, full width");
            Assert.That(rig.Vm.Selection.Primary, Is.SameAs(btn), "and it is hit where it is now drawn");
            Assert.That(rig.Vm.Text, Does.Contain("X=\"200\"").And.Contain("Dock=\"Bottom\"").And.Contain("Anchor=\"Top, Right\""));
        });
    }

    [AvaloniaTest]
    public void ThePageHasGripsButNoTitleBar_AndAGripDragResizesTheDesignSize_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);
            var surface = rig.SurfaceInWindow();
            var bg = (ISolidColorBrush)rig.Window.FindResource(ThemeVariant.Dark, "IdeBg")!;

            Color above, grip;
            using (var frame = rig.Window.CaptureRenderedFrame()!)
            {
                above = PixelAt(frame, new Point(surface.Center.X, surface.Y - 9));
                grip = PixelAt(frame, new Point(surface.Right, surface.Center.Y));
            }

            rig.Drag(rig.InWindow(800, 225), 80, 0);

            Assert.Multiple(() =>
            {
                Assert.That(above, Is.EqualTo(bg.Color), $"{w}x{h}: no title bar above a page (spec §2.4)");
                Assert.That(grip, Is.EqualTo(Navy), $"{w}x{h}: the right-hand form grip");
                Assert.That(rig.Doc.Width, Is.EqualTo(880), $"{w}x{h}: the grip resized the design size");
                Assert.That(rig.Vm.Text, Does.Contain("Version=\"1\" Width=\"880\" Height=\"450\""),
                    $"{w}x{h}: and the page's ClientSize reached the file");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void ADockedPanel_ShowsNoHandles_AndADragOnItOnlySelectsIt_AtTwoSizes()
    {
        var zooms = new List<double>();
        foreach (var (w, h) in TwoSizes)
        {
            using var rig = Open(CanvasDoc, w, h);
            zooms.Add(rig.Zoom);
            var before = rig.Vm.Text;
            var pnl = rig.Control("pnl");

            rig.Drag(rig.InWindow(600, 44), 60, 80);

            Color handle;
            using (var frame = rig.Window.CaptureRenderedFrame()!)
            {
                handle = PixelAt(frame, rig.InWindow(0, 44));   // its left-middle handle's centre, where it is DRAWN
            }

            Assert.Multiple(() =>
            {
                Assert.That(rig.Vm.Selection.Primary, Is.SameAs(pnl), $"{w}x{h}: the press selected it");
                Assert.That((Pixel(pnl).X, Pixel(pnl).Y), Is.EqualTo((300, 300)), $"{w}x{h}: the drag moved nothing");
                Assert.That(rig.Vm.Text, Is.EqualTo(before), $"{w}x{h}: nothing was written");
                Assert.That(handle, Is.Not.EqualTo(Navy), $"{w}x{h}: no handles on a docked control (spec §7a)");
            });
        }

        Assert.That(zooms, Has.Some.Not.EqualTo(1.0), "at least one size is not 1:1");
    }

    [AvaloniaTest]
    public void TheFormsRows_AreClientSizeAndMobileBreakpoint_OnACanvasPage_AndTheTracks_OnAGridPage()
    {
        List<string> canvasRows, gridRows;
        bool breakpointRealised;
        using (var rig = Open(CanvasDoc))
        {
            canvasRows = rig.GridVm.Rows.Select(r => r.Name).ToList();
            rig.Container(rig.Row("MobileBreakpoint"));
            breakpointRealised = rig.List.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "MobileBreakpoint");
        }

        using (var rig = Open(GridDoc))
        {
            gridRows = rig.GridVm.Rows.Select(r => r.Name).ToList();
        }

        Assert.Multiple(() =>
        {
            Assert.That(canvasRows, Does.Contain("ClientSize").And.Contain("MobileBreakpoint"));
            Assert.That(canvasRows, Does.Not.Contain("Cols").And.Not.Contain("Rows").And.Not.Contain("Gap"));
            Assert.That(breakpointRealised, Is.True, "the row is in the REAL list, not only the view model");
            Assert.That(gridRows, Does.Contain("Cols").And.Contain("Rows").And.Contain("Gap"));
            Assert.That(gridRows, Does.Not.Contain("ClientSize").And.Not.Contain("MobileBreakpoint"));
        });
    }

    [AvaloniaTest]
    public void ANegativeMobileBreakpoint_SnapsTheRealEditorBack_AndThePaneSaysWhy()
    {
        using var rig = Open(CanvasDoc);
        var row = rig.Row("MobileBreakpoint");
        var before = rig.Vm.Text;
        var edits = 0;
        rig.GridVm.Edited += (_, _) => edits++;

        rig.TypeInto(row, "-5", 0);

        var spinner = rig.Container(row).GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.IsEffectivelyVisible);
        Assert.Multiple(() =>
        {
            Assert.That(spinner.Value, Is.EqualTo(600m), "snapped back to what the page holds");
            Assert.That(rig.Vm.Text, Is.EqualTo(before), "never written");
            Assert.That(edits, Is.Zero, "a refusal is not an edit");
            Assert.That(rig.GridVm.DescriptionTitle, Is.EqualTo("MobileBreakpoint"));
            Assert.That(rig.GridVm.DescriptionBody, Does.Contain("'-5'").And.Contain("not applied"),
                "the plan's review note: the reason is SHOWN, not silently dropped");
        });
    }
}
```

- [ ] Build. Run `FullyQualifiedName~FormPixelPageRealViewTests`.

**Expected GREEN.** These are acceptance tests over Parts 1–4. Their falsifiability comes from the mutation list (M9.1, M9.6, M9.9, M9.11–M9.13 each turn at least one of them red).

**If one is red, read it before touching production code:**
- `DragDrop`: see B8 for the fallback.
- `IdeBg` resolved under a different theme variant: use the variant `FormDesignerLayoutRealViewTests.cs:323-324` resolves.
- The Width attribute order: read `rig.Vm.Text` and assert the root line as the writer leaves it.

#### Step 16: Commit (2 min)
- Stage `FormPixelPageRealViewTests.cs`.
- Message: `test(designer): a Canvas page through the real document view — pixels, drops, drags, grips, docked, rows, refusal (Task 9)`. If the drop used the fallback, say so in the body.

### Part 6: Docs, mutations and gate

#### Step 17: Doc fixes (C5) (4 min)
Edit the text only, no code:
- `BasicLang/Forms/FormVocabulary.cs:9-12`: "the canvas and placement move onto it in piece 1's Task 9" → "the canvas's layout, chrome and grips (`FormCanvasTransform.Layout`, `FormCanvasControl`) and placement (`FormPlacement.Place`) ask it too (Task 9); the canvas's title bar alone stays target-only, because it is about being a WINDOW".
- `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs:361-368`: replace "PIXEL GEOMETRY ONLY, and that is D3 rather than an oversight. Anchor and Dock are WinForms layout vocabulary; a .blwebform control lives in a grid CELL and has no edges to anchor to." with "PIXEL GEOMETRY ONLY, and that is D3 rather than an oversight: Anchor and Dock are the PIXEL vocabulary — a .blform's, and a Canvas page's (spec 2026-09-27) — and a Grid/Flow page's control lives in a CELL with no edges to anchor to." Keep the rest.
- `VisualGameStudio.Shell/ViewModels/Designer/FormArrange.cs:26-30`: replace the "Pixel geometry only (D3)" paragraph body with "A Grid/Flow page is laid out by CELL: its controls have no X, Y, Width or Height, so these commands do nothing there (the PixelGeometry test below). A Canvas page's controls are pixels and align like a .blform's. ⚠ A DOCKED control is not excluded yet (pre-flight follow-up): its X/Y are ignored at run time."
- `FormCanvasTransform.cs` HitTest comment `:102` ("BOTH targets read Layout now"): no change needed.
- [ ] Build (no behaviour change). Stage the three files, but COMMIT only after Steps 18–19: `docs(designer): the vocabulary's consumers after Task 9`. The body carries the mutation red names and the gate numbers with their base.

#### Step 18: Mutations (Edit → build → run the named tests → see red → Edit back → build → green; never `git checkout --`, never Copy-Item) (15 min total)

| # | Rule | Mutant (exact edit) | Must go red (at least) |
|---|---|---|---|
| M9.1 | Layout by vocabulary | `FormCanvasTransform.Layout`: `FormVocabulary.IsPixel(document)` → `document.Target == FormTarget.WinForms` | `ACanvasPage_IsLaidOutInPixels…`, `ACanvasPagesControl_IsHit…`, `EveryWebKind_IsDrawnOnACanvasPage`, real-view `TheControlsSitAtTheirPixels…` |
| M9.2 | BoundsOf reads the resolver | delete the `if (dock.TryGet(control, out var docked)) { … }` block | `BandsAndDockedControls…`, `ADockedControl_IsHitWhereItIsDrawn…`, real-view `ADockedPanel_ShowsNoHandles…` (press misses; the selection stays null) |
| M9.3 | Bands consume the resolver (plan M5's canvas half) | in `Bands`, replace `foreach (var docked in dock.All)` with `foreach (var docked in dock.All.Where(d => !parents.ContainsKey(d.Control)).OrderBy(d => d.Edge == FormDockEdge.Bottom ? 1 : 0))`, and set `band` to `new Rect(0, b.Y, SurfaceSize(document).Width, b.Height)` | `BandsAndDockedControls…` (the nested band is gone and the widths differ) |
| M9.4 | Designer mode | `FormDockMode.Designer` → `FormDockMode.Runtime` in `Layout` | `AHiddenDockedPanel_IsStillDockedAndDrawn…` |
| M9.5 | ContainerAt resolves | public `ContainerAt`: `var dock = FormDockLayout.Resolve(new FormDocument { Target = document.Target }, FormDockMode.Designer);` | `ContainerAt_ADockedFillPanel…`, `DraggingIntoADockedFillPanel…`, `ADropIntoADockedFillPanel…` |
| M9.6 | Place by vocabulary | `FormPlacement.Place`: `!FormVocabulary.IsPixel(document)` → `document.Target != FormTarget.WinForms` | 3 Canvas `FormPlacementTests`, `ADropOnANewWebForm…`, real-view `AToolboxDrop…` |
| M9.7 | A docked control is never moved | `FormGeometryEdit.IsDocked` returns `false` | `ADockedControl_IsNeitherMovedNorResized`, `ADragStartingOnADockedControl_SelectsIt…`, `TheArrowKeys…` |
| M9.8 | Clamp to the drawn size | `FormGeometryEdit.SurfaceOf`: `return container?.Geometry is PixelGeometry p ? (p.Width, p.Height) : …` (the old body) | `AChildOfADockedFillPanel_IsClamped…`, `ADropIntoADockedFillPanel…` |
| M9.9 | Title bar is target-only | `DrawSurface`: `isWindow = FormVocabulary.IsPixel(document)` | `ACanvasPage_HasNoTitleBar…`, real-view `ThePageHasGripsButNoTitleBar…` |
| M9.10 | Alignment grid on vocabulary | `if (isPixel) DrawAlignmentGrid` → `if (isWindow)` | `ACanvasPage_IsDesignedOnTheAlignmentGrid…` |
| M9.11 | Grips drawn on vocabulary | Render grips gate → `document.Target == FormTarget.WinForms` | `ACanvasPage_HasTheFormGrips…`, real-view grip sample |
| M9.12 | Grip press on vocabulary | the press gate `:883` → `document.Target == FormTarget.WinForms` | real-view `…AGripDragResizesTheDesignSize…` (Width stays 800) |
| M9.13 | No handles on docked | `HasHandles` → `control?.Geometry is PixelGeometry` | `ADockedControl_GetsNoHandles…`, real-view `ADockedPanel_ShowsNoHandles…` |
| M9.14 | Drag on a docked primary arms nothing | delete `&& FormDockLayout.EdgeOf(SelectedControl) == null` from the pixel branch | `ADragStartingOnADockedPrimary_MovesNoOtherSelectedControlEither` |
| M9.15 | Store refusal named | `Commit`: `Refusal = _storeRefusal?.Invoke(value);` → `Refusal = null;` | `ANonPositiveClientSize_…SaysWhy`, `ANegativeMobileBreakpoint…`, `AClientSizeTheStoreRefuses…`, real-view `ANegativeMobileBreakpoint…` |
| M9.16 | Set asks RefusalOf | Set's MobileBreakpoint arm: delete the `if (RefusalOf(…) != null) return false;` guard | `RefusalOf_IsExactlyWhatSetRefuses("-1")`, `("wide")` |

A mutant that turns nothing red is a finding. Add the missing test, or record why the rule is covered elsewhere. Record each red test name for the gate commit.

#### Step 19: Gate (10 min + run time)
- [ ] Build.
- [ ] Fast subset, both streams:

```powershell
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter TestCategory!=Integration > `"$sp\t9-fast.txt`" 2>&1"
```

  Compare the sorted FAILURE NAMES with the Task 8 checkpoint (`baseline-fast.txt` from Task 0). Allowed: exactly the known machine rows (`Emit_Replaces…AnotherHandleHasMapped` ×2, intermittent `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, `SearchSnippets_*` ×2). Any other name is a regression. State the base with every number.

- [ ] Named re-run: Step 0's filter plus `FullyQualifiedName~FormCanvasPixelPageTests|FullyQualifiedName~FormPixelPageRealViewTests`, into `$sp\t9-named.txt`. Every row passes, and the count equals Step 0's count plus the new tests. That proves they RAN.
- [ ] Integration (they place through `FormPlacement` / `ContainerAt`):

```powershell
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~FormDesignerAcceptanceTests|FullyQualifiedName~FormMenuAcceptanceTests|FullyQualifiedName~FormComponentAcceptanceTests`" > `"$sp\t9-int.txt`" 2>&1"
```

  Compare names with `baseline-int.txt`. The JS rows may fail with `ERROR_USER_MAPPED_FILE`. If they do, A/B on `4376789a` before calling it a regression.
- [ ] Record the gate. Do NOT make an `--allow-empty` commit for it. The mutation red names and the gate numbers (with their base) go in the Step 17 docs commit message: make that commit AFTER Steps 18–19, or `git commit --amend` it only if it is still unpushed and still HEAD. If neither is possible, put them in the Task 9 report to the plan owner.

---

## E. What the executor should expect to trip over
1. **Headless `DragDrop`.** It is unproven in this repo (B8). Try it first and fall back loudly.
2. **The `Concat` laziness trap (B5).** If you "simplify" `.ToList()` away, `FormBoundsOf`'s early return will one day drop nested bands. The agreement test pins the nested band. Keep the materialisation.
3. **`AClientSizeTheStoreRefuses…` goes red on purpose (B1).** It is an Edit of an existing test, never a Write over the file.
4. **Theme variant for `IdeBg`.** Copy exactly how `FormDesignerLayoutRealViewTests` resolves it. The headless app is Fluent-only, and the window's styles add AppStyles.
5. **Title-bar sample position.** It must be the MIDDLE of the title-bar row (B6). A sample near the left lands on the page caption.
6. **Zoom.** Real-view zooms come from the live layout. The `Has.Some.Not.EqualTo(1.0)` assertion is the guard. If both sizes happen to be 1.0 on this machine, shrink the first window, not the assertion.
7. **The docs commit (Step 17) carries the gate record**, so make it after the mutations and the gate (Step 19 note).
8. **A strip test moving in Part 1 is a stacking bug, not an intended change** (A#25: no positioned-Dock fixture exists).
9. **Two mirrored-pair traps closed here** (`SurfaceOf` ×2, Bands vs resolver). Do not reopen either by "keeping the old helper for compatibility".
10. **Never set `PropertyGrid.SelectedControl` from the view model.** Task 9 needs no such write. The drop selects through `PlaceControl` → `SelectInDesigner`.
