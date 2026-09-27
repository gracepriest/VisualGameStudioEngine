# Task 10 pre-flight: the emitter's Canvas page (expanded, with corrections)

Plan: `docs/superpowers/plans/2026-09-27-web-pixel-layout.md`, "## Task 10". Spec: `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §3, §4, §5, §7, §7a.
This check was made against `feat/web-pixel-layout` @ **`e1c3de72`** (Tasks 1–9 landed). It was read-only apart from one throw-away compile probe in the scratchpad; nothing in the repo changed except this file.

Follow this document **instead of** the plan's Task 10 section. Where the two disagree, this document wins. Everything else in the plan (How to build/test, Commits, Traps) still applies.

**OWNER decisions needed: none.** The owner's 2026-09-27 decisions (run-time Visible → a small reflow script; phone order = `FormReadingOrder`) are implemented as stated. Eleven reversible calls are made here and listed under "Decisions taken". Tell the owner about them at the Task 16 click-through.

---

## A. Anchor verification (claim → today's file:line at e1c3de72 → verdict)

| # | Plan / task claim | Today | Verdict |
|---|---|---|---|
| 1 | `FormAssetEmitter.Html` `:101-181` | `BasicLang/Forms/FormAssetEmitter.cs:101-181` | holds |
| 2 | "strips outside the div" comment `:116-119` | `:116-119` (`⛔ A Docked strip is PAGE CHROME…`) | holds; it becomes Grid/Flow-only (Step 5) |
| 3 | `Css` `:391-453`, Canvas arm `:422-424` | `:391-453`; the arm is `case FormLayoutKind.Canvas: sb.Append("  position: relative;\n");` at `:422-424` | holds |
| 4 | `AppendControlCss` `:455-502`, grid-only rule | `:455-502`; grid rule `:459` `control.Geometry is GridGeometry grid && layout.Kind == FormLayoutKind.Grid` | holds |
| 5 | Stale "escape hatch" doc ≈388-389 | `:387-390` ("`Canvas` is the explicitly-marked absolute-pixel escape") | holds; replaced in Step 5 |
| 6 | Strip row CSS `FormControlCatalog.cs:1813/1838/1859` | `WebCss` at `:1822`, `:1847`, `:1868` | **moved** (+9), holds |
| 7 | Band heights `:1808/1833/1854` (spec §3) | `DefaultHeight: 24` `:1817`, `25` `:1842`, `22` `:1863` | **moved** (+9), holds |
| 8 | `FormControlDef` gains a trailing positional `StretchesWhenStacked` | record `:1024-1042`, last parameter `string? WebCss = null` `:1042` | holds |
| 9 | Stretch rows: TextBox, ComboBox, ListBox, PictureBox | `:1336`, `:1378`, `:1387`, `:1421` | holds |
| 10 | `RegionWriter.CheckAnchors` `:186-213` gates on `Target == WinForms` | `:164-191`; the gate is `:167` `if (form.Target != FormTarget.WinForms) return;`; only caller `:115` | **moved** (−22), holds |
| 11 | Plan ≈3201/3217: "Task 10 lands the diagnostic" | plan `:3201`, `:3217` | holds; lands in Part 5 |
| 12 | `FormVocabulary.IsPixel(document)`, `LayoutOf` | `FormVocabulary.cs:39-43`, `:32-36` (null layout on the web = **Grid**) | holds |
| 13 | `FormDocument.DesignSize` | `FormDocument.cs:65-66` (400×300 fallback per axis) | holds |
| 14 | `FormDockLayout.Resolve(doc, Runtime)`, `TryGet`, `ClientSizeOf`, `RootClientSize` | `FormDockLayout.cs:132`, `:78`, `:90-98` (**throws** for a hidden container in Runtime), `:76` | holds. See B3 |
| 15 | Runtime skips `Visible=false`, a hidden container's children unresolved | `Participates` `:244-245`; Walk `:259-264` | holds |
| 16 | `FormAnchorCss.Positioned(geometry, W, H)` / `Docked(bounds)` | `FormAnchorCss.cs:93-107`, `:110-124` | holds |
| 17 | `FormLayout.EffectiveMobileBreakpoint` (0 = never) | `FormGeometry.cs:147-148`; `TryParseMobileBreakpoint` `:155-164` (≥ 0 only) | holds |
| 18 | `FormReadingOrder.Order(list, rectOf)` | `FormReadingOrder.cs:46-53` | holds; summary `:38-40` says "the emitter's to apply (Task 10)" → doc fix (Step 23) |
| 19 | `FormControl.IsHidden` also drives FormCss `display:none` | `FormControl.cs:101-104`; `FormCss.cs:31` | holds |
| 20 | Owner: "hook wherever the JS backend's control `Visible` setter lands" | **There is none.** A web control's field is `Element` (`RegionWriter.cs:1135-1143`, init `:754` `getElementById`); `dom-core.bli` has no `Visible` — only `Element.hidden` `:42`, `Element.style` `:46`, `CSSStyleDeclaration.display` `:64` | **FALSE premise**, see B1 |
| 21 | "The repo already runs emitted JS under node in FormBuildEmissionTests" | `FormBuildEmissionTests.RunEmittedScript` `:109-131` and `FormDesignerAcceptanceTests.RunPageUnderNode` `:277-346` run **App.js only**, never the page's HTML | holds, but see B2 |
| 22 | The strip's `li:hover>ul` dropdown "needs `overflow: visible` on the band" (plan Risk) | An absolutely positioned `<nav>` is `overflow: visible` by default. The real defect is z-order | **FALSE/incomplete**, see B4 |
| 23 | "`FormRegionWriterTests`/`FormDesignDiagnosticTests`: Anchor=Middle refused" | `DesignDiagnostic.Check*` (`DesignDiagnostic.cs:342-461`) never calls `CheckAnchors`; only `RegionWriter.Write` does | half **FALSE**: the test lives in `FormRegionWriterTests` only (follow-up recorded) |
| 24 | Existing emitter expectations that could move | Every test that calls `FormAssetEmitter.Html/Css/Emit` (41 calls, 8 files) builds a Grid/Flow/layout-less document; `Kind="Canvas"` appears only in 7 test files, none of which calls the emitter | holds: **the expected-change list is empty** |
| 25 | node on this machine | `node --version` → `v22.19.0` | holds |

---

## B. Blockers (following the plan text literally breaks something)

### B1: There is no run-time `Visible` setter on the web to hook. The hook is a `MutationObserver` on the form area
- **Evidence.** The region writer declares every web control `As Element` and initialises it with `doc.getElementById("id")` (`RegionWriter.cs:754`, `:1135-1143`). `dom-core.bli` (`BasicLang/lib/js/dom-core.bli`, load-bearing copy `IDE/lib/js/dom-core.bli`) declares no `Visible`. User code hides a control through the DOM directly.
- **Measured** (probe in the scratchpad, `IDE/BasicLang.exe p.bas --target=javascript`): `el.style.display = "none"` lowers to `const t1 = el.style; t1.display = "none";` and `el.hidden = True` to `el.hidden = true;`. Both are plain DOM writes; both change an ATTRIBUTE (`style`, `hidden`).
- **Corrected instruction.** The reflow script observes `.vgs-form` with `new MutationObserver(reflow).observe(form, { attributes: true, subtree: true, attributeFilter: ["style", "class", "hidden"] })`. That catches every spelling in use today (`style.display`, `hidden`, `classList`, `setAttribute("style", …)`) and piece 2's future portable `Visible` setter with no compiler change. A MutationObserver callback is a microtask, so the re-dock lands before the next paint (no flicker).
- ⚠ Record for piece 2: to SHOW a control that was designed `Visible=False`, the setter must write a non-empty `style.display` (e.g. `"block"`): the stylesheet's `#id { display: none; }` still applies under `style.display = ""`.

### B2: No existing harness runs a page's HTML, so an inline page script would be tested by nothing
- **Evidence.** Both node harnesses import `App.js` into a stub `document` (A#21). An inline `<script>` in `LoginForm.html` would never execute under any test, which is exactly the "green build is not a running page" scar (CLAUDE.md).
- **Corrected instruction.** Two new node harnesses in `FormDockScriptTests` (Part 4):
  1. **Lock-step:** the script's pure resolver (`FormDockScript.Core`) against `FormDockLayout.Resolve(…, Runtime)` + `FormAnchorCss.Docked` over one fixture table (every fixture, plus every "one docked control hidden" variant).
  2. **Glue:** the page's OWN inline script, EXTRACTED from the emitted HTML, run with a stub DOM, a stub `getComputedStyle` and a capturing `MutationObserver`; the test flips a docked control's `style.display`, fires the observer, and compares the live CSS with the C# answer.
  `FormBuildEmissionTests` then runs harness 2 on the page a REAL CLI build wrote (Part 6).

### B3: A hidden docked control and every child of a hidden container have NO Runtime answer; one `Resolve(form)` cannot place them
- **Evidence.** Runtime gives a hidden docked control no bounds (`TryGet` false) and does not walk a hidden container (`FormDockLayout.cs:259-264`), so `ClientSizeOf(hiddenPanel)` **throws** (`:90-98`). The plan says "from ONE `FormDockLayout.Resolve(form)`".
- **Corrected instruction.** The emitter resolves TWICE and asks Runtime first:
  - docked: `runtime.TryGet(c) || designer.TryGet(c)` → `FormAnchorCss.Docked`;
  - undocked: container size = root → `runtime.RootClientSize`; else `runtime.TryGetClientSize(parent) || designer.TryGetClientSize(parent)`.
  The page's FIRST state is Runtime (a hidden control takes no space — owner decision). Designer answers only fill the places Runtime has none; the reflow script overwrites them the moment the control (or its container) is shown, before paint. Both resolves are still the one resolver; nothing is re-derived.

### B4: Document-ordered strips put every open MenuStrip dropdown UNDER the controls that follow the strip
- **Evidence.** The dropdown is `.vgs-MenuStrip li ul{…position:absolute…}` (`FormControlCatalog.cs:1824`). On a Canvas page the `<nav>` is `position:absolute` with `z-index:auto`, so it opens no stacking context; the dropdown paints in DOM order among the form's positioned descendants, i.e. BEFORE every positioned control later in the document. Every menu over a form's controls opens underneath them. (WinForms opens a dropdown as its own top-level window.)
- **Corrected instruction.** Keep S12 for the bands themselves (document order, unchanged), and lift only the dropdown lists: for a `FormPlace.Docked` row that declares `HtmlChildrenWrapper` W, emit `#id W { z-index: 1; }`. The bar's own list is static (z-index does nothing there); every nested list is absolutely positioned and is lifted. Row-driven, never a Kind switch.

### B5: User-agent margins move controls off their designed X/Y
- **Evidence.** Chromium's UA sheet gives `input[type=checkbox|radio]` `margin: 3px 3px 0 5px`, and `fieldset` (GroupBox) `margin: 0 2px`. An absolutely positioned box is placed by its MARGIN edge, so a CheckBox at X=10 renders at 15. Task 13 asserts "within 1px of its stored X/Y".
- **Corrected instruction.** Every positioned control and strip on a Canvas page gets `position: absolute; box-sizing: border-box; margin: 0` before its geometry.

### B6: The JS mirror needs two rules that are private inside `FormDockLayout`
- **Evidence.** `OwnSize` is private (`:282-287`) and "has a client area" is inlined in `Walk` (`:259-261`). A data builder that re-implements them is a third copy.
- **Corrected instruction.** Expose them as `public static (int Width, int Height) OwnSizeOf(FormControl)` and `public static bool HasClientArea(FormControl)`; `Walk`/`ResolveSiblings` call them; the script's data builder calls them. No behaviour change (Part 1, Step 3).

### B7: On a phone, `display:flex` on a container overrides the `hidden` attribute
- **Evidence.** `[hidden]{display:none}` is a UA rule; the phone query's author rule `display: flex` on a container outranks it, so user code's `el.hidden = True` would not hide a Panel below the breakpoint.
- **Corrected instruction.** A Canvas stylesheet carries `.vgs-form [hidden] { display: none !important; }`.

---

## C. Corrections (not blocking)

- **C1. The vocabulary test goes FIRST in `Html` and `Css`** (`FormVocabulary.IsPixel(form)`), and the `case FormLayoutKind.Canvas` arm is deleted from the Grid/Flow switch. `FormLayout.Kind` defaults to **Grid** (`FormGeometry.cs:115`) and `Css` reads `form.Layout ?? new FormLayout()`, which agrees with `LayoutOf`, so a layout-less page stays Grid.
- **C2. "Recursing with each container's design size" (plan) is wrong.** A container's size is `ClientSizeOf` (its RESOLVED bounds when docked), per the Task 6/7 contract; a `Dock=Fill` Panel stored 10×10 is 640×456 under a menu. `ANestedControl_IsAnchoredAgainstItsContainersClientSize…` pins it.
- **C3. Bordered containers.** `box-sizing: border-box` makes the outer box the design `Width`×`Height` (WinForms' `Size`). A child's containing block is the container's PADDING box, i.e. inside any CSS border, which is also where WinForms positions children (its `DisplayRectangle`). Today only GroupBox (`<fieldset>`, UA 2px groove) has a border on the web: Panel's `BorderStyle` row has no `CssProperty` (`FormControlCatalog.cs:1402-1406`). So Left/Top-anchored and docked children match WinForms' inset *direction*, but the amounts differ (fieldset 2px vs WinForms' caption inset), and far-anchored/centred children of a bordered container are off by up to 2× the border because the anchor distance is computed from the outer size. Recorded; the Task 12/13 harness measures it; fixtures avoid GroupBox.
- **C4. Zero/negative size.** `FormAnchorCss` writes no size (spec §3), so a 0-wide control is CONTENT-sized on the page (its text shows; for a centred one its LEFT edge sits at the centred offset), where WinForms draws nothing. Accepted divergence, stated in the emitter comment; the Edge harness excludes such fixtures.
- **C5. Anchored siblings when a docked sibling hides (owner question): confirmed they do not move.** WinForms' `DefaultLayout` docks first, then lays anchored controls out against the container's `DisplayRectangle`, which a docked sibling's visibility does not change. The page's anchored CSS is relative to the container's box, which does not change either. What DOES move is a docked CONTAINER's size, and its anchored children follow it through their CSS insets, as WinForms re-anchors them. As read in WinForms' source; Task 12 adds the fixture that runs it (Carried).
- **C6. Strip heights** are `DefaultHeight` 24/25/22 at 96 DPI, through the resolver (`OwnSizeOf`); the id rule (`#id { … height: 24px }`) outranks the row's class rule. ⚠ The MenuStrip's inner `<ul>`/`<li>` (padding 4px) is ~26px tall inside the 24px `<nav>` and overflows it by ~2px visually; the nav's own rect is 24. Task 13 measures and records.
- **C7. S12, recorded and NOT changed for bands.** Bands are in document order on the page; the canvas paints them last. Only dropdown lists are lifted (B4).
- **C8. `Tracks` / `FormGridLayout.ParseTracks` stay untouched.** The Grid/Flow code path is moved into its own method verbatim; a golden-hash guard (Step 1) proves the Grid/Flow bytes do not change.
- **C9. `design --check` does not run `CheckAnchors`** on either target (A#23). Pre-existing; follow-up.

## Decisions taken (reversible; tell the owner, no decision needed)
1. **Phone sort rectangles are Designer-mode for every control.** CSS `order` is static; ordering by the Designer picture keeps it the same whichever controls user code has hidden. Hidden controls stay `display:none` on the phone (their order is irrelevant but sensible when shown).
2. **A hidden docked control's static CSS carries its Designer insets**, so it has a sensible place even if the script never runs; the script overwrites them when it is shown.
3. **Children of a hidden container** get Designer answers in the static CSS; the script re-resolves them when the container is shown (before paint).
4. **`margin: 0` + `box-sizing: border-box`** on every positioned control and strip (B5, C3).
5. **Phone sizing:** a non-stretch control keeps its designed width and height (its sort rectangle); a stretch row (`StretchesWhenStacked`) spans the column and keeps its height; a container's height is `auto`; every stacked control gets `max-width: 100%`, so a wide control (an 800px Fill panel) never forces a phone to scroll sideways.
6. **Strips span the column on a phone** (`align-self: stretch`), as they span the form. Place-driven.
7. **Open dropdowns are lifted** (`#id ul { z-index: 1; }`, B4).
8. **The reflow script is emitted only when the page docks something.**
9. **The script's live rules apply at and above the breakpoint only** (`@media (width >= Bpx)`), so phone stacking is never overridden; with breakpoint 0 they are unwrapped.
10. **Only visibility re-docks.** A run-time change to a docked control's SIZE (`style.height`) does not re-dock; the owner decision covers Visible. Recorded.
11. **The script observes attributes on `.vgs-form`'s subtree only.** A visibility change made purely through a stylesheet edit is not seen. Recorded.

## Follow-ups (out of scope; for the Task 16 record)
- `design --check` never runs `CheckAnchors` (C9).
- An unknown positioned `Dock` name ("Fil") silently does not dock on the canvas or the page, and is a csc failure on WinForms (`DockStyle.Fil`, `RegionWriter.cs:1013`). No diagnostic on any target.
- `FormCatalogShapes.Canonical` defaults a web document's control to `GridGeometry` even on a Canvas page (`FormCatalogShapes.cs:47-49`); callers must pass `geometry:`. It should ask `FormVocabulary.IsPixel(document)`.
- Piece 2's `Visible` setter must write a non-empty `style.display` to show a design-hidden control (B1).
- C3 (bordered containers), C4 (zero size), C6 (menu bar overflow) are for the harness to measure.

## Carried to Task 12/13 (write into their pre-flights)
- **Task 12:** a run-time toggle fixture: after `Show()`, set a docked Panel's `Visible = False`, `PerformLayout()`, read; then `True`, read. Compare with `Resolve(…, Runtime)` of the document with/without that `Visible=False`. Also an anchored sibling in the same container (C5: it must not move).
- **Task 13:** the Edge harness must also flip a docked control's `style.display` from its measuring script and re-measure, so the real browser runs the reflow script (the node glue test uses a stub DOM). And it must open a MenuStrip dropdown over a later control and check what is on top (B4).

---

## C (grep). Every emitter site that decides Grid/Flow/Canvas by `Target`/`Kind`, with a verdict

Grep: `Target (==|!=) FormTarget`, `LayoutKind\.`, `IsPixel`, `LayoutOf`, `FormAssetEmitter\.` across `BasicLang/` (`*.cs`), plus `FormDispatch.cs`, `FormDocumentLoader.cs`, `JavaScriptEmitter.cs`.

| Site | What it decides | Verdict |
|---|---|---|
| `FormAssetEmitter.cs:74` `form.Target != FormTarget.Web` (Emit) | whether a document has a page at all | legit (a window has no page) |
| `FormAssetEmitter.cs:116-176` Html: strips split top/bottom OUTSIDE the div, with no layout test | where strips go | **MOVE → `FormVocabulary.IsPixel(form)` branch**: Canvas = inside, document order (Step 5) |
| `FormAssetEmitter.cs:394` `form.Layout ?? new FormLayout()` + switch `:399-425` | the page's CSS vocabulary | **MOVE → `IsPixel` first** → `CanvasCss`; the Canvas case is deleted; Grid/Flow arms byte-identical (Step 5) |
| `FormAssetEmitter.cs:459` `GridGeometry && Kind == Grid` | the grid cell rule | legit (Grid path only) |
| `FormAssetEmitter.cs:212`, `:230`, `:268`, `:483` (`Place`, `AppliesTo(Web)`) | tab stops, accelerators, web attributes/CSS | legit (catalog, target) |
| `FormAssetEmitter.cs:556` `Tracks` | Grid track lists | untouched (mirrored pair with `FormGridLayout.ParseTracks`) |
| `JavaScriptEmitter.cs:75`, `:127-129` passes `forms` to `Emit` | nothing | legit (no target/layout decision) |
| `Forms/FormDispatch.cs` | nothing | no decision |
| `Forms/FormDocumentLoader.cs:42` `TargetOfExtension(path) != Web` | which documents are pages | legit |
| `RegionWriter.cs:167` CheckAnchors `Target != WinForms` | whether an unknown anchor edge is refused | **MOVE → `!FormVocabulary.IsPixel(form)`** (Part 5) |
| `RegionWriter.cs:263`, `:348`, `:426`, `:464` | web bind checks | legit (web events on every layout) |
| `RegionWriter.cs:590`, `:652`, `:710`, `:752`, `:789`, `:943`, `:1137` | init shape, `Controls.Add`, field types | legit (the web init is `getElementById` for every layout) |
| `FormHandlers.cs:157`, `:179` | handler signature/placement | legit |
| `FormCss.cs` | value → CSS | layout-agnostic; unchanged |
| `FormControl.IsDockedToBottom` summary `:107-119` | says the emitter puts the footer after the div | doc fix: Grid/Flow only (Step 23) |
| `Program.cs:525-527` retarget direction | retarget | legit / Task 11 |

**Summary: 3 sites move (Html's strip placement, Css's vocabulary, CheckAnchors). The Tracks pair is untouched. Everything else is legitimately target- or catalog-based.**

---

## The run-time reflow script (owner decision 1), designed

**Data.** Per page, one JSON tree built by `FormDockScript.DataJson(form)` from the resolver's OWN rules: the root `{w, h}` = `DesignSize`; per sibling list, every control that DOCKS (`FormDockLayout.EdgeOf != null`) or that has a client area (`FormDockLayout.HasClientArea`) holding something that docks. Node = `{i: id, e: edge|null, w, h: FormDockLayout.OwnSizeOf, c: HasClientArea, hd: IsHidden, k: children}`. Document order is preserved, so docking order is.

**JS.** `FormDockScript.Core` = three pure functions that MIRROR C#: `vgsDockSiblings` ↔ `FormDockLayout.ResolveSiblings` (document order, far edges from the UNCLAMPED remainder, sizes handed out floored at 0, Fill takes the remainder without consuming it), `vgsDockWalk` ↔ `FormDockLayout.Walk` in Runtime mode (a hidden control neither docks nor has its children walked), `vgsDockCss` ↔ `FormAnchorCss.Docked`. `FormDockScript.Bootstrap` = `vgsDockStart(root, breakpoint)`: hidden = `getComputedStyle(el).display === "none"` (falls back to `hd` when the element does not exist), and on every observed change it re-walks the whole tree and writes `#id{left:…;right:…;top:…;height:…;}` for every docked thing into one `<style id="vgs-dock-live">` appended to `<head>` (later in the cascade than the page stylesheet, same specificity, so it wins), wrapped in `@media (width >= Bpx)`. It rewrites only when the text changed.

**Hook.** A `MutationObserver` on `.vgs-form` (`attributes`, `subtree`, filter `style`/`class`/`hidden`) — B1. Installed by an inline CLASSIC `<script data-vgs="dock">` (an IIFE, no globals) between `</div>` and the module script, so it is observing before `App.js` runs `InitializeComponent`. It does nothing until something changes: the first state is the stylesheet, which is Runtime mode.

**Lock-step.** `FormDockScriptTests.TheScriptsResolver_AgreesWithFormDockLayout_OnEveryFixture` runs `Core` under node over a fixture table (each edge alone, strips stacking, a Top panel before the menu, Left then Top, Fill-not-consuming, both overflow cases, Fill after overflow, nesting in a docked Fill, docking in an undocked Panel, zero/negative sizes, a hidden container), each fixture as designed AND once per docked control with that control hidden, and requires the JSON of every result to equal C#'s `Resolve(…, Runtime)` + `FormAnchorCss.Docked`, byte for byte. The glue test runs the page's own script. `FormDockLayout`'s and `FormAnchorCss.Docked`'s summaries name the mirror and the test ("change both in one commit"). Mutations M10.9–M10.12 prove the gate can see a drift.

---

## D. Task 10, expanded

Base: `feat/web-pixel-layout` @ `e1c3de72`. `$sp` is your scratchpad. Build/test commands and commit rules are the plan's ("How to build and run tests", "Commits"). Every red/green claim means reading `$sp\run.txt`: counts plus failure names. No AXAML changes, so no `dotnet clean`.

Build: `dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`.

The fast filter used below (call it **F10**):

```
FullyQualifiedName~FormAssetEmitterTests|FullyQualifiedName~FormDockScriptTests|FullyQualifiedName~FormDockLayoutTests|FullyQualifiedName~FormAnchorCssTests|FullyQualifiedName~FormReadingOrderTests|FullyQualifiedName~FormCssTests|FullyQualifiedName~FormStripEmissionTests|FullyQualifiedName~FormHtmlAttributeTests|FullyQualifiedName~FormComponentEmissionTests|FullyQualifiedName~FormAcceleratorTests|FullyQualifiedName~FormRegionWriterTests|FullyQualifiedName~FormRootTests|FullyQualifiedName~FormRetargetTests|FullyQualifiedName~FormCatalogCoverageTests|FullyQualifiedName~FormMobileBreakpointTests|FullyQualifiedName~FormCanvasTransformTests|FullyQualifiedName~FormStripLayoutTests
```

The Integration filter (**I10**):

```
FullyQualifiedName~FormBuildEmissionTests|FullyQualifiedName~FormDesignerAcceptanceTests|FullyQualifiedName~FormComponentAcceptanceTests|FullyQualifiedName~FormMenuAcceptanceTests|FullyQualifiedName~FormAnchorEmissionTests|FullyQualifiedName~FormRetargetPairTests|FullyQualifiedName~WinFormsCatalogSweepTests|FullyQualifiedName~FormDockScriptTests
```

### Step 0: Base and checkpoint (5 min + run time)
- [ ] `git log --oneline -1` prints `e1c3de72 …`; `git status --porcelain` shows only `?? csc.dll`.
- [ ] Build. Record F10 and I10 at the base:

```powershell
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"<F10>`" > `"$sp\t10-base-fast.txt`" 2>&1"
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"<I10>`" > `"$sp\t10-base-int.txt`" 2>&1"
```

Record total/passed/failed/skipped and the sorted failure names **with the base (`e1c3de72`)**. F10: all green expected. I10: the JS rows may fail with `ERROR_USER_MAPPED_FILE`; record their names so later runs compare by NAME.

### Part 1: Guards and the two shared facts the emitter needs (no behaviour change)

#### Step 1: The Grid/Flow golden-hash guard, captured at the BASE (5 min)
⛔ Do this before any production edit. Edit `VisualGameStudio.Tests/Compiler/FormAssetEmitterTests.cs`: add `using System.Security.Cryptography;` and `using System.Text;` at the top, and insert this block before the `// The Main() dispatch (D7)` banner:

```csharp
    // ==================================================================
    // Task 10 guard (spec 2026-09-27 §3 "Grid/Flow paths unchanged"): the Grid, Flow and layout-less pages are
    // BYTE-identical to the emitter before Task 10. Hashes captured at e1c3de72.
    // ==================================================================

    private static FormDocument FlowWithStrips()
    {
        var form = new FormDocument
        {
            Target = FormTarget.Web, Name = "FlowPage",
            Layout = new FormLayout { Kind = FormLayoutKind.Flow, Dir = "Horizontal", Gap = "4px" }
        };
        var menu = new FormControl { Kind = "MenuStrip", Id = "menuStrip1" };
        var file = new FormControl { Kind = "ToolStripMenuItem", Id = "fileItem" };
        file.Properties["Text"] = "&File";
        menu.Children.Add(file);
        form.Controls.Add(menu);

        var button = new FormControl { Kind = "Button", Id = "btn", TabIndex = 0 };
        button.Properties["Visible"] = "False";
        button.Properties["BackColor"] = "#FF112233";
        form.Controls.Add(button);

        form.Controls.Add(new FormControl { Kind = "StatusStrip", Id = "statusStrip1" });
        var tool = new FormControl { Kind = "ToolStrip", Id = "toolStrip1" };
        tool.Properties["Dock"] = "Bottom";
        form.Controls.Add(tool);
        return form;
    }

    private static FormDocument LayoutlessWithPanel()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "Bare" };
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl", TabIndex = 0, Geometry = new GridGeometry { Col = 1, Row = 2, ColSpan = 2 }
        };
        panel.Children.Add(new FormControl { Kind = "Label", Id = "lbl", TabIndex = 0, Geometry = new GridGeometry() });
        form.Controls.Add(panel);
        form.Controls.Add(new FormControl { Kind = "MenuStrip", Id = "menuStrip1" });
        return form;
    }

    // ⚠ Captured by running this test at the BASE: each "CAPTURE" fails and prints the real hash; paste it here.
    private static readonly Dictionary<string, string> PreTask10Hashes = new()
    {
        ["GridLogin"] = "CAPTURE",
        ["FlowWithStrips"] = "CAPTURE",
        ["LayoutlessWithPanel"] = "CAPTURE"
    };

    [TestCase("GridLogin")]
    [TestCase("FlowWithStrips")]
    [TestCase("LayoutlessWithPanel")]
    public void AGridOrFlowPage_IsByteIdenticalToThePreTask10Emitter(string fixture)
    {
        var form = fixture switch
        {
            "GridLogin" => LoginForm(),
            "FlowWithStrips" => FlowWithStrips(),
            _ => LayoutlessWithPanel()
        };

        var text = (FormAssetEmitter.Html(form, "App.js") + "\n/* CSS */\n" + FormAssetEmitter.Css(form))
            .Replace("\r\n", "\n");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        Assert.That(hash, Is.EqualTo(PreTask10Hashes[fixture]),
            $"the {fixture} page changed. Task 10 must not touch a Grid/Flow page. The output was:\n{text}");
    }
```

- [ ] Build; run `FullyQualifiedName~AGridOrFlowPage_IsByteIdentical`. **Expected RED ×3** at the base, each message carrying the actual hash. Paste the three hashes into `PreTask10Hashes`. Rebuild and re-run: **GREEN ×3**. `git diff --stat` must show only the test file.

#### Step 2: `StretchesWhenStacked` facet and its catalog gate (4 min)
Failing tests first. In `FormAssetEmitterTests.cs`, add at the end of the class (before the closing brace):

```csharp
    // ==================================================================
    // Task 10 (spec 2026-09-27 §5) — the phone stretch flag is a CATALOG facet, never a Kind switch
    // ==================================================================

    [Test]
    public void StretchesWhenStacked_IsOnlyOnPositionedRowsTheWebHas()
    {
        var wrong = FormControlCatalog.All
            .Where(d => d.StretchesWhenStacked &&
                        (d.Place != FormPlace.Positioned || !d.SupportsTarget(FormTarget.Web)))
            .Select(d => d.Kind)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(wrong, Is.Empty, "a phone never stacks a strip, an item, a tray component or a WinForms-only row");
            Assert.That(FormControlCatalog.Find("TextBox")!.StretchesWhenStacked, Is.True, "spec §5: inputs stretch");
            Assert.That(FormControlCatalog.Find("Button")!.StretchesWhenStacked, Is.False, "spec §5: small controls keep their size");
        });
    }
```

- [ ] Build. **Expected RED:** CS1061 (`StretchesWhenStacked` does not exist).
- [ ] Implement in `BasicLang/Forms/FormControlCatalog.cs`:
  - In the `<param>` list above the record, after the `WebCss` param doc (ends `…two menus must not emit the rules twice.</param>`), add:

```csharp
/// <param name="StretchesWhenStacked">
/// Below a Canvas page's phone breakpoint (spec 2026-09-27 §5), the control spans the column instead of keeping its
/// designed width — inputs and pictures (TextBox, which covers multi-line text, ComboBox, ListBox, PictureBox). Only
/// on a Positioned row the web has (FormAssetEmitterTests pins it). ⛔ The emitter reads this; it never switches on
/// the kind.
/// </param>
```

  - Change the record's last parameter line `    string? WebCss = null)` to:

```csharp
    string? WebCss = null,
    bool StretchesWhenStacked = false)
```

  - On the four rows, add the named argument after the row's last existing argument:
    - TextBox: `…description: "Event raised when the value of the Text property is changed on Control."),` → keep, and the row's closing `)),` becomes `), StretchesWhenStacked: true),`. Concretely: replace
      `                description: "Event raised when the value of the Text property is changed on Control.")),`
      with
      `                description: "Event raised when the value of the Text property is changed on Control."),`
      `            StretchesWhenStacked: true),`
    - ComboBox: replace `                description: SelectedIndexChangedDescription)),` **inside the ComboBox row** (the first occurrence, after `DefaultWidth: 121`) with `                description: SelectedIndexChangedDescription),` + newline + `            StretchesWhenStacked: true),`.
    - ListBox: the same edit on the occurrence after `DefaultWidth: 120, DefaultHeight: 95`.
    - PictureBox: replace `            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription)),` **after `DefaultWidth: 100, DefaultHeight: 50, Schematic: FormSchematic.Image,`** with `            Events: Ev("Click", "click", category: FormEventCategory.Action, description: ClickedDescription),` + newline + `            StretchesWhenStacked: true),`.
    - ⚠ `description: SelectedIndexChangedDescription))` also appears on CheckedListBox and TabControl rows: edit by the anchor text around each row, never replace-all.
- [ ] Build; run `FullyQualifiedName~StretchesWhenStacked|FullyQualifiedName~FormCatalogCoverageTests|FullyQualifiedName~WinFormsCatalogParityTests`. **Expected GREEN.**

#### Step 3: `FormDockLayout.OwnSizeOf` and `HasClientArea` become the one answer (B6) (3 min)
Edit `BasicLang/Forms/FormDockLayout.cs`:
1. Replace the private `OwnSize` (`:281-287`, from `/// <summary>A strip's band height` through `: (0, 0);`) with:

```csharp
    /// <summary>
    /// ⛔ The one answer to "what size does this thing dock at": a strip's band height (its row's
    /// <see cref="FormControlDef.DefaultHeight"/> — spec §3), or a control's stored size floored at 0. Public because
    /// the page's run-time reflow script (<see cref="FormDockScript"/>) must dock exactly what this resolver docks.
    /// </summary>
    public static (int Width, int Height) OwnSizeOf(FormControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.Definition?.Place == FormPlace.Docked
            ? (0, control.Definition.DefaultHeight)
            : control.Geometry is PixelGeometry pixel
                ? (Math.Max(0, pixel.Width), Math.Max(0, pixel.Height))
                : (0, 0);
    }

    /// <summary>
    /// ⛔ The one answer to "does this control lay children out in pixels": a POSITIONED control with
    /// <see cref="PixelGeometry"/>. A strip's children are items and a cell-placed Panel has no pixel size (Task 6
    /// review). Visibility is NOT part of it — <see cref="FormDockMode.Runtime"/> adds that.
    /// </summary>
    public static bool HasClientArea(FormControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return (control.Definition?.Place is null or FormPlace.Positioned) && control.Geometry is PixelGeometry;
    }
```

2. In `ResolveSiblings`, `var (ownWidth, ownHeight) = OwnSize(control);` → `OwnSizeOf(control)`.
3. In `Walk`, replace the condition

```csharp
            if (control.Definition?.Place is not (null or FormPlace.Positioned) ||
                control.Geometry is not PixelGeometry ||
                !Participates(control, mode))
```

   with `if (!HasClientArea(control) || !Participates(control, mode))`, and `: OwnSize(control);` → `: OwnSizeOf(control);`.
- [ ] `FormDockScript` does not exist yet, so the `<see cref="FormDockScript"/>` would warn (CS1574). Write it as `<c>FormDockScript</c>` now; Step 17 turns it into a cref.
- [ ] Build; run `FullyQualifiedName~FormDockLayoutTests|FullyQualifiedName~FormCanvasTransformTests|FullyQualifiedName~FormStripLayoutTests`. **Expected GREEN** (refactor only).

#### Step 4: Commit Part 1 (2 min)
Stage by name: `FormAssetEmitterTests.cs`, `FormControlCatalog.cs`, `FormDockLayout.cs`. Message: `test+feat(forms): Grid/Flow golden guard, the StretchesWhenStacked facet, and FormDockLayout's own-size/client-area rules made public (Task 10 part 1)`. Body: B6, and "hashes captured at e1c3de72".

### Part 2: The Canvas page's markup and desktop stylesheet

#### Step 5: Failing tests (5 min)
In `FormAssetEmitterTests.cs`, add after the Step 2 block (add `using System.Globalization;` and `using System.Text.RegularExpressions;` at the top):

```csharp
    // ==================================================================
    // Task 10 (spec 2026-09-27 §3, §4) — a Canvas page: the WinForms client area, in pixels
    // ==================================================================

    private static FormDocument CanvasPage(int? width = 640, int? height = 480, string? breakpoint = "600") => new()
    {
        Target = FormTarget.Web, Name = "Page", Width = width, Height = height,
        Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = breakpoint }
    };

    private static FormControl At(string kind, string id, int x, int y, int width, int height,
        string? anchor = null, string? dock = null, params FormControl[] children)
    {
        var control = new FormControl
        {
            Kind = kind, Id = id,
            Geometry = new PixelGeometry { X = x, Y = y, Width = width, Height = height, Anchor = anchor, Dock = dock }
        };
        control.Children.AddRange(children);
        return control;
    }

    private static FormControl StripOf(string kind, string id) => new() { Kind = kind, Id = id };

    private const string PositionedPrefix = "position: absolute; box-sizing: border-box; margin: 0; ";

    /// <summary>The declarations of <c>#id { … }</c> OUTSIDE the phone query, or null. Rules start at a line start.</summary>
    private static string? DesktopRule(string css, string id) =>
        RuleIn(css.Split("@media (width <")[0], "\n#" + id + " { ");

    /// <summary>The declarations of <c>#id { … }</c> INSIDE the phone query, or null.</summary>
    private static string? PhoneRule(string css, string id)
    {
        var parts = css.Split("@media (width <");
        return parts.Length < 2 ? null : RuleIn(parts[1], "\n  #" + id + " { ");
    }

    private static string? RuleIn(string text, string opener)
    {
        var at = text.IndexOf(opener, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        at += opener.Length;
        return text.Substring(at, text.IndexOf(" }", at, StringComparison.Ordinal) - at);
    }

    private static string Declarations(IEnumerable<(string Property, string Value)> declarations) =>
        string.Join("; ", declarations.Select(d => $"{d.Property}: {d.Value}"));

    [Test]
    public void ACanvasPage_PutsItsStripsInsideTheFormArea_InDocumentOrder()
    {
        // ⛔ Spec §3: the coordinate space is the WinForms CLIENT AREA, strips included — a control at Y=30 is 6px
        // below a 24px menu. On a Grid/Flow page the strips stay chrome outside the div (FormStripEmissionTests).
        var page = CanvasPage();
        page.Controls.Add(StripOf("StatusStrip", "statusStrip1"));
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(At("Button", "btn", 10, 40, 75, 23));
        page.Controls.Add(StripOf("ToolStrip", "toolStrip1"));

        var html = FormAssetEmitter.Html(page, "App.js");
        var open = html.IndexOf("<div class=\"vgs-form\">", StringComparison.Ordinal);
        var close = html.LastIndexOf("</div>", StringComparison.Ordinal);
        int Where(string id) => html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        var ids = new[] { "statusStrip1", "menuStrip1", "btn", "toolStrip1" };

        Assert.Multiple(() =>
        {
            foreach (var id in ids)
            {
                Assert.That(Where(id), Is.GreaterThan(open).And.LessThan(close), $"{id} is inside the form area");
            }

            Assert.That(ids.Select(Where), Is.Ordered,
                "DOCUMENT order (S12): absolutely positioned siblings paint later-on-top, WinForms' z-order");
        });
    }

    [Test]
    public void ACanvasPagesLiteral_FlowsInsideTheFormArea()
    {
        var page = CanvasPage();
        page.Literal = """<p class="hint">Use your work account.</p>""";

        var html = FormAssetEmitter.Html(page, "App.js");

        Assert.That(html.IndexOf("<p class=\"hint\">", StringComparison.Ordinal),
            Is.GreaterThan(html.IndexOf("<div class=\"vgs-form\">", StringComparison.Ordinal))
              .And.LessThan(html.LastIndexOf("</div>", StringComparison.Ordinal)));
    }

    [TestCase(640, 480, 640, 480)]
    [TestCase(null, null, 400, 300)]
    public void TheFormArea_FillsTheWindow_WithTheDesignSizeAsItsMinimum(int? width, int? height, int w, int h)
    {
        var page = CanvasPage(width, height);
        Assert.That(page.DesignSize, Is.EqualTo((w, h)), "precondition: DesignSize is the one form size");

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("body { margin: 0; }"));
            Assert.That(css, Does.Contain(
                ".vgs-form {\n  position: relative;\n  width: 100%;\n" +
                $"  min-width: {w}px;\n  height: 100vh;\n  min-height: {h}px;\n  box-sizing: border-box;\n}}"));
            Assert.That(css, Does.Contain(".vgs-form [hidden] { display: none !important; }"),
                "B7: a phone's display:flex must not un-hide a control user code hid");
        });
    }

    [TestCase(null)]
    [TestCase("Top,Left")]
    [TestCase("Right")]
    [TestCase("Left,Right")]
    [TestCase("Bottom")]
    [TestCase("Top,Bottom")]
    [TestCase("None")]
    [TestCase("Top,Bottom,Left,Right")]
    public void APositionedControl_IsWhereFormAnchorCssPutsIt(string? anchor)
    {
        var page = CanvasPage();
        var button = At("Button", "btn", 500, 400, 100, 30, anchor);
        page.Controls.Add(button);

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "btn"), Does.StartWith(
            PositionedPrefix + Declarations(FormAnchorCss.Positioned((PixelGeometry)button.Geometry!, 640, 480))));
    }

    [Test]
    public void ARightAnchoredControl_KeepsItsDistanceFromTheRightEdge()
    {
        var page = CanvasPage();
        page.Controls.Add(At("Button", "btn", 500, 400, 100, 30, "Top,Right"));

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "btn"),
            Does.Contain("right: 40px; width: 100px; top: 400px; height: 30px"), "non-vacuity: 640 - 500 - 100");
    }

    [Test]
    public void ANestedControl_IsAnchoredAgainstItsContainersClientSize_FromFormDockLayout()
    {
        // ⛔ C2: a container's size is FormDockLayoutResult's — a docked Panel's RESOLVED bounds, never its stale
        // stored 10x10, and never the form's.
        var inner = At("Button", "inner", 200, 10, 50, 20, "Top,Right");
        var fill = At("Panel", "fill", 7, 7, 10, 10, dock: "Fill", children: inner);
        var boxed = At("Button", "boxed", 200, 10, 50, 20, "Top,Right");
        var panel = At("Panel", "pnl", 20, 300, 300, 150, children: boxed);
        var page = CanvasPage();
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(fill);
        page.Controls.Add(panel);

        var runtime = FormDockLayout.Resolve(page, FormDockMode.Runtime);
        var client = runtime.ClientSizeOf(fill);
        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(client, Is.EqualTo((640, 456)), "precondition: a Fill under a 24px menu");
            Assert.That(DesktopRule(css, "inner"), Does.Contain(
                Declarations(FormAnchorCss.Positioned((PixelGeometry)inner.Geometry!, client.Width, client.Height))));
            Assert.That(DesktopRule(css, "inner"), Does.Contain("right: 390px"), "640 - 200 - 50");
            Assert.That(DesktopRule(css, "boxed"), Does.Contain(
                Declarations(FormAnchorCss.Positioned((PixelGeometry)boxed.Geometry!, 300, 150))),
                "an undocked Panel's client size is its stored size");
        });
    }

    [Test]
    public void DockedThings_AreWhereFormDockLayoutPutsThem()
    {
        var page = CanvasPage();
        page.Controls.Add(At("Panel", "pnlTop", 300, 300, 10, 40, dock: "Top")); // stored X/Y/Width are stale by design
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        // ⚠ The status strip BEFORE the Fill: a Fill takes what is left WHEN IT DOCKS, so a later Bottom would overlap it.
        page.Controls.Add(StripOf("StatusStrip", "statusStrip1"));
        page.Controls.Add(At("Panel", "fill", 0, 0, 1, 1, dock: "Fill"));

        var css = FormAssetEmitter.Css(page);
        var runtime = FormDockLayout.Resolve(page, FormDockMode.Runtime);

        Assert.Multiple(() =>
        {
            foreach (var docked in runtime.All)
            {
                Assert.That(DesktopRule(css, docked.Control.Id),
                    Does.StartWith(PositionedPrefix + Declarations(FormAnchorCss.Docked(docked))), docked.Control.Id);
            }

            Assert.That(DesktopRule(css, "menuStrip1"), Does.Contain("top: 40px; height: 24px"),
                "non-vacuity: under the Dock=Top panel that precedes it (spec §4)");
            Assert.That(DesktopRule(css, "fill"), Does.Contain("left: 0px; right: 0px; top: 64px; bottom: 22px"));
        });
    }

    [Test]
    public void EveryStrip_IsItsRowsDefaultHeight()
    {
        var rows = FormControlCatalog.All
            .Where(d => d.Place == FormPlace.Docked && d.SupportsTarget(FormTarget.Web))
            .ToList();
        Assert.That(rows, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                var page = CanvasPage();
                FormCatalogShapes.Canonical(page, row, "strip");
                Assert.That(DesktopRule(FormAssetEmitter.Css(page), "strip"),
                    Does.Contain($"height: {row.DefaultHeight}px"), row.Kind);
            }
        });
    }

    [Test]
    public void AHiddenDockedPanel_GivesUpItsEdge_InThePagesFirstState()
    {
        // ⛔ Owner decision 2026-09-27: the page OPENS in FormDockMode.Runtime (a hidden control takes no space); the
        // reflow script (Part 4) re-docks on a change. The hidden panel keeps its DESIGNER insets (B3).
        var page = CanvasPage();
        var hidden = At("Panel", "pnlTop", 0, 0, 10, 40, dock: "Top");
        hidden.Properties["Visible"] = "False";
        page.Controls.Add(hidden);
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));

        var css = FormAssetEmitter.Css(page);
        Assert.That(FormDockLayout.Resolve(page, FormDockMode.Designer).TryGet(hidden, out var designed), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(DesktopRule(css, "menuStrip1"), Does.Contain("top: 0px; height: 24px"),
                "the menu closes the gap while the panel is hidden");
            Assert.That(DesktopRule(css, "pnlTop"),
                Does.Contain(Declarations(FormAnchorCss.Docked(designed))).And.Contain("display: none"));
        });
    }

    [Test]
    public void AChildOfAHiddenPanel_IsStillPositioned()
    {
        // B3: Runtime does not walk a hidden container, and ClientSizeOf would THROW for it.
        var child = At("Button", "child", 10, 10, 75, 23, "Top,Right");
        var hidden = At("Panel", "pnl", 20, 20, 300, 200, children: child);
        hidden.Properties["Visible"] = "False";
        var page = CanvasPage();
        page.Controls.Add(hidden);

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "child"), Does.StartWith(
            PositionedPrefix + Declarations(FormAnchorCss.Positioned((PixelGeometry)child.Geometry!, 300, 200))));
    }

    [Test]
    public void ANonPositiveSize_WritesNoSize()
    {
        // Spec §3 / C4: content-sized on the page, invisible on WinForms — an accepted divergence.
        var page = CanvasPage();
        page.Controls.Add(At("Label", "lbl", 10, 12, 0, -5));

        var rule = DesktopRule(FormAssetEmitter.Css(page), "lbl");

        Assert.Multiple(() =>
        {
            Assert.That(rule, Does.Not.Contain("width:"));
            Assert.That(rule, Does.Not.Contain("height:"));
            Assert.That(rule, Does.Contain("left: 10px; top: 12px"));
        });
    }

    [Test]
    public void AnItem_AndATrayComponent_AreNeverPositioned()
    {
        var page = CanvasPage();
        var menu = StripOf("MenuStrip", "menuStrip1");
        var item = new FormControl { Kind = "ToolStripMenuItem", Id = "fileItem" };
        item.Properties["Visible"] = "False"; // so the item HAS a rule to inspect
        menu.Children.Add(item);
        page.Controls.Add(menu);
        page.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(DesktopRule(css, "fileItem"), Is.EqualTo("display: none;"), "an item keeps only its catalog CSS");
            Assert.That(css, Does.Not.Contain("#tmr"), "a tray component has no element");
        });
    }

    [Test]
    public void TheCatalogsCss_IsStillEmitted_AfterTheGeometry()
    {
        var page = CanvasPage();
        var button = At("Button", "btn", 10, 10, 75, 23);
        button.Properties["BackColor"] = "#FF112233";
        page.Controls.Add(button);
        var expected = FormCss.Declaration(FormControlCatalog.Find("Button")!.Property("BackColor")!, "#FF112233")!.Value;

        Assert.That(DesktopRule(FormAssetEmitter.Css(page), "btn"),
            Does.EndWith($"height: 23px; {expected.Property}: {expected.Value};"));
    }

    [Test]
    public void AMenuStripsDropdowns_AreLiftedAboveTheControlsAfterIt()
    {
        // ⛔ B4: bands are in DOCUMENT order (S12), so a control after the strip paints over its open dropdown.
        // WinForms opens a dropdown as its own window. Only the row's children-wrapper lists are lifted: the bar's
        // own list is static (z-index does nothing there), every nested one is absolutely positioned.
        var page = CanvasPage();
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(StripOf("ToolStrip", "toolStrip1"));

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("\n#menuStrip1 ul { z-index: 1; }\n"));
            Assert.That(css, Does.Not.Contain("#toolStrip1 ul"), "a ToolStrip's row declares no children wrapper");
            Assert.That(DesktopRule(css, "menuStrip1"), Does.Not.Contain("z-index"), "the band itself stays in document order");
        });
    }
```

- [ ] Build; run `FullyQualifiedName~FormAssetEmitterTests`.

**Expected RED:** `ACanvasPage_PutsItsStripsInside…` (strips before the div / after it), `TheFormArea_Fills…` ×2, `APositionedControl_IsWhere…` ×8, `ARightAnchored…`, `ANestedControl…`, `DockedThings…`, `EveryStrip…`, `AHiddenDockedPanel…`, `AChildOfAHiddenPanel…`, `ANonPositiveSize…` (null rule), `TheCatalogsCss…` (no geometry before it), `AMenuStripsDropdowns…`.
**Already GREEN (guards, not proofs):** `ACanvasPagesLiteral…` (the literal was always inside the div), `AnItem_AndATrayComponent…`, the Step 1 golden ×3, and everything pre-existing.

#### Step 6: Implement the markup and the desktop stylesheet (5 min)
Edit `BasicLang/Forms/FormAssetEmitter.cs`. Find each piece by its quoted text.

1. **`Html`.** Replace from `        // ⛔ A Docked strip is PAGE CHROME, not form content (spec §4).` through the closing `        }` of the bottom loop (the line before `        sb.Append($"<script type=\"module\" …`) with:

```csharp
        // ⛔ ONE vocabulary test (spec 2026-09-27 §2.1): a Canvas page speaks pixels, Grid/Flow speak cells.
        if (FormVocabulary.IsPixel(form))
        {
            AppendCanvasBody(sb, form);
        }
        else
        {
            AppendGridOrFlowBody(sb, form);
        }
```

   Then add these methods after `Html` (the Grid/Flow body is the removed text, VERBATIM, apart from the first comment's opening and the literal moved into `AppendLiteral`):

```csharp
    /// <summary>
    /// A Grid/Flow page's body. ⛔ A Docked strip is PAGE CHROME, not form content (spec §4) — on a Grid/Flow page.
    /// It sits OUTSIDE <c>&lt;div class="vgs-form"&gt;</c> because that div is the layout container — a Grid or Flow
    /// box whose tracks the user authored for their own controls. A <c>&lt;nav&gt;</c> placed inside it would consume
    /// a cell nobody declared and push every control one place along, from a green build. (A Canvas page puts strips
    /// INSIDE: <see cref="AppendCanvasBody"/>.)
    /// </summary>
    private static void AppendGridOrFlowBody(StringBuilder sb, FormDocument form)
    {
        var top = new List<FormControl>();
        var bottom = new List<FormControl>();
        var rest = new List<FormControl>();

        foreach (var control in form.Controls)
        {
            if (control.Definition?.Place != FormPlace.Docked)
            {
                rest.Add(control);
            }
            else if (control.IsDockedToBottom)
            {
                bottom.Add(control);
            }
            else
            {
                top.Add(control);
            }
        }

        // Top chrome in DOCUMENT order: the first-documented strip is nearest the top edge, which on
        // the page means first.
        foreach (var control in top)
        {
            AppendControl(sb, control, indent: "");
        }

        sb.Append("<div class=\"vgs-form\">\n");

        foreach (var control in rest)
        {
            AppendControl(sb, control, indent: "  ");
        }

        AppendLiteral(sb, form);

        sb.Append("</div>\n");

        // ⛔ Bottom chrome in REVERSE document order. Same algebra as the canvas bands: the
        // FIRST-documented Bottom strip stacks nearest the true bottom edge, so on the page it is
        // emitted LAST, closest to the end of <body>. Forward order here would put the status bar
        // below a bottom toolbar on the page and above it in the designer — the same document
        // rendering two ways.
        for (var i = bottom.Count - 1; i >= 0; i--)
        {
            AppendControl(sb, bottom[i], indent: "");
        }
    }

    /// <summary>
    /// A Canvas page's body (spec 2026-09-27 §3). ⛔ The coordinate space is the WinForms CLIENT AREA, strips
    /// included: every strip is an absolutely positioned band INSIDE <c>.vgs-form</c> at
    /// <see cref="FormDockLayout"/>'s rectangle (written by <see cref="CanvasCss"/>), so a control at Y=30 sits 6px
    /// below a 24px menu exactly as in WinForms. Everything — strips too — in DOCUMENT order: absolutely positioned
    /// siblings paint later-on-top, which is WinForms' "last in the list is in front" (scope call S12).
    /// </summary>
    private static void AppendCanvasBody(StringBuilder sb, FormDocument form)
    {
        sb.Append("<div class=\"vgs-form\">\n");

        foreach (var control in form.Controls)
        {
            AppendControl(sb, control, indent: "  ");
        }

        // <Literal> flows at the form area's top-left, under the positioned controls (spec §3).
        AppendLiteral(sb, form);

        sb.Append("</div>\n");
    }

    private static void AppendLiteral(StringBuilder sb, FormDocument form)
    {
        if (!string.IsNullOrEmpty(form.Literal))
        {
            // ⛔ Passes through UNTOUCHED — the runat="server" inversion (D9). Not escaped, because
            // it is markup the user wrote to be markup; the canvas shows it read-only for the same
            // reason.
            sb.Append(form.Literal);
            if (!form.Literal.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.Append('\n');
            }
        }
    }
```

2. **`Css`.** Replace its summary (`:387-390`) and its body's head so it reads:

```csharp
    /// <summary>
    /// The layout, as CSS. Grid and Flow are the CELL vocabularies (D3); a <c>Canvas</c> page speaks PIXELS (spec
    /// 2026-09-27) and takes <see cref="CanvasCss"/>. ⛔ The one vocabulary test is
    /// <see cref="FormVocabulary.IsPixel(FormDocument)"/>, asked first — never the target, never the switch below.
    /// </summary>
    public static string Css(FormDocument form)
    {
        if (FormVocabulary.IsPixel(form))
        {
            return CanvasCss(form);
        }

        var sb = new StringBuilder();
```

   Delete the switch arm `            case FormLayoutKind.Canvas:` / `sb.Append("  position: relative;\n");` / `break;`. Replace the kind-CSS loop (from `        // Per-KIND chrome styling, appended ONCE` through its closing `}`) with `        AppendKindCss(sb, form);`, and add:

```csharp
    /// <summary>
    /// Per-KIND chrome styling, appended ONCE however many controls of that kind the page has (spec §4). A menu is the
    /// one control whose appearance is not optional — an unstyled <c>&lt;ul&gt;</c> of <c>&lt;li&gt;</c>s is a
    /// bulleted vertical list, not a menu bar, and its submenus are all open at once. Distinct() on the block itself,
    /// because the rule is "one block per kind present" and two ToolStrips are one kind.
    /// </summary>
    private static void AppendKindCss(StringBuilder sb, FormDocument form)
    {
        foreach (var css in form.AllControls()
                     .Select(c => c.Definition?.WebCss)
                     .Where(s => s != null)
                     .Distinct())
        {
            sb.Append(css).Append('\n');
        }
    }
```

3. **`AppendControlCss`.** Replace the catalog loop (from `        if (control.Definition is { } definition)` through its closing `}`, keeping the `⛔⛔ Driven from the CATALOG` comment above it) with `        rules.AddRange(CatalogDeclarations(control));`, and add:

```csharp
    /// <summary>
    /// The row-driven declarations for <paramref name="control"/> (spec §2.1), as <c>property: value</c>, in CATALOG
    /// order — the one walk both vocabularies use. <see cref="FormCss"/> converts each value.
    /// </summary>
    private static List<string> CatalogDeclarations(FormControl control)
    {
        var declarations = new List<string>();
        if (control.Definition is not { } definition)
        {
            return declarations;
        }

        foreach (var property in definition.Properties)
        {
            if (!property.AppliesTo(FormTarget.Web) ||
                !control.Properties.TryGetValue(property.Name, out var raw))
            {
                continue;
            }

            if (FormCss.Declaration(property, raw) is { } declaration)
            {
                declarations.Add($"{declaration.Property}: {declaration.Value}");
            }
        }

        return declarations;
    }
```

4. **The Canvas stylesheet.** Add after `AppendControlCss`:

```csharp
    // ==================================================================
    // A Canvas page (spec 2026-09-27 §3, §4, §5)
    // ==================================================================

    /// <summary>
    /// A Canvas page's stylesheet. The form area fills the window with the DESIGN SIZE as its minimum (larger →
    /// controls follow their anchors; smaller → the page scrolls, never squashes). Every positioned control and strip
    /// is absolutely placed by the pure deciders — <see cref="FormAnchorCss"/> for anchors,
    /// <see cref="FormDockLayout"/> for everything docked — and nothing here re-derives either.
    ///
    /// <para>⛔ The page's FIRST state is <see cref="FormDockMode.Runtime"/> (owner decision 2026-09-27): a hidden
    /// control takes no space, so the next docked control closes the gap. Where Runtime has no answer — a hidden docked
    /// control, a child of a hidden container — the Designer answer is written instead, and the page's reflow script
    /// (<c>FormDockScript</c>) overwrites it the moment that control is shown.</para>
    ///
    /// <para>⚠ A non-positive Width/Height writes no size (spec §3), so such a control is CONTENT-sized here and
    /// invisible on WinForms — an accepted divergence. ⚠ Children are positioned against their container's PADDING
    /// box, i.e. inside any CSS border (a GroupBox's fieldset), as WinForms positions them inside its
    /// DisplayRectangle; the amounts differ and the Task 12/13 harness measures them.</para>
    /// </summary>
    private static string CanvasCss(FormDocument form)
    {
        var sb = new StringBuilder();
        var runtime = FormDockLayout.Resolve(form, FormDockMode.Runtime);
        var designer = FormDockLayout.Resolve(form, FormDockMode.Designer);
        var (width, height) = runtime.RootClientSize;

        sb.Append($"/* Generated from {form.Name}{form.FileExtension}. Edits here are overwritten on build. */\n");
        sb.Append("body { margin: 0; }\n");
        sb.Append(".vgs-form {\n");
        sb.Append("  position: relative;\n");
        sb.Append("  width: 100%;\n");
        sb.Append($"  min-width: {Number(width)}px;\n");
        sb.Append("  height: 100vh;\n");
        sb.Append($"  min-height: {Number(height)}px;\n");
        sb.Append("  box-sizing: border-box;\n");
        sb.Append("}\n");

        // ⛔ The UA's [hidden]{display:none} loses to the phone query's display:flex on a container, so a control
        // user code hid with `el.hidden = True` would reappear below the breakpoint.
        sb.Append(".vgs-form [hidden] { display: none !important; }\n");

        AppendCanvasControls(sb, form.Controls, parent: null, runtime, designer);
        AppendKindCss(sb, form);
        return sb.ToString();
    }

    private static void AppendCanvasControls(
        StringBuilder sb, IReadOnlyList<FormControl> siblings, FormControl? parent,
        FormDockLayoutResult runtime, FormDockLayoutResult designer)
    {
        foreach (var control in siblings)
        {
            var rules = new List<string>();

            if (CanvasPlacement(control, parent, runtime, designer) is { } placement)
            {
                // ⛔ margin: 0 — an absolutely positioned box is placed by its MARGIN edge, and the UA gives a
                // checkbox/radio `margin: 3px 3px 0 5px` and a fieldset `0 2px`: the control would land off its X/Y.
                // box-sizing: the outer box IS the design Width x Height, WinForms' Size.
                rules.Add("position: absolute");
                rules.Add("box-sizing: border-box");
                rules.Add("margin: 0");
                rules.AddRange(placement.Select(d => $"{d.Property}: {d.Value}"));
            }

            rules.AddRange(CatalogDeclarations(control));

            if (rules.Count > 0)
            {
                sb.Append($"#{control.Id} {{ {string.Join("; ", rules)}; }}\n");
            }

            // ⛔ A strip's OPEN dropdown must not open under the controls after it (strips are in document order,
            // S12): WinForms opens it as its own window. Its row's children-wrapper lists are lifted — the bar's own
            // list is static, where z-index does nothing; every nested one is absolutely positioned.
            if (control.Definition is { Place: FormPlace.Docked, HtmlChildrenWrapper: { } wrapper })
            {
                sb.Append($"#{control.Id} {wrapper} {{ z-index: 1; }}\n");
            }

            AppendCanvasControls(sb, control.Children, control, runtime, designer);
        }
    }

    /// <summary>
    /// Where <paramref name="control"/> sits, or null when it has no pixel place (an item, a tray component, a
    /// positioned control with no pixel geometry, or a control inside something with no client area).
    /// </summary>
    private static IReadOnlyList<(string Property, string Value)>? CanvasPlacement(
        FormControl control, FormControl? parent, FormDockLayoutResult runtime, FormDockLayoutResult designer)
    {
        var place = control.Definition?.Place ?? FormPlace.Positioned;
        if (place is not (FormPlace.Positioned or FormPlace.Docked))
        {
            return null;
        }

        // Runtime first: the page opens as the running form. Designer only where Runtime has no answer.
        if (runtime.TryGet(control, out var docked) || designer.TryGet(control, out docked))
        {
            return FormAnchorCss.Docked(docked);
        }

        if (place == FormPlace.Docked || control.Geometry is not PixelGeometry pixel)
        {
            return null;
        }

        // ⛔ The container's size is FormDockLayoutResult's — ClientSizeOf's one answer (a docked Panel's RESOLVED
        // bounds), never re-derived here and never the stored size of a docked container.
        (int Width, int Height) client;
        if (parent == null)
        {
            client = runtime.RootClientSize;
        }
        else if (!runtime.TryGetClientSize(parent, out client) && !designer.TryGetClientSize(parent, out client))
        {
            return null;
        }

        return FormAnchorCss.Positioned(pixel, client.Width, client.Height);
    }
```

- [ ] Build; run `FullyQualifiedName~FormAssetEmitterTests|FullyQualifiedName~FormStripEmissionTests|FullyQualifiedName~FormCssTests|FullyQualifiedName~FormHtmlAttributeTests|FullyQualifiedName~FormComponentEmissionTests|FullyQualifiedName~FormAcceleratorTests|FullyQualifiedName~FormRootTests|FullyQualifiedName~FormRetargetTests`.

**Expected GREEN, all** — the golden ×3 in particular. If a golden goes red, the Grid/Flow refactor changed bytes: diff the printed text against the base (re-run the test at `e1c3de72` in a `git worktree add --detach`), never re-capture.

#### Step 7: Commit Part 2 (2 min)
Stage `FormAssetEmitter.cs`, `FormAssetEmitterTests.cs`. Message: `feat(emitter): a Canvas page's markup and desktop stylesheet — strips inside, positions, anchors and docks from the pure deciders (Task 10 part 2)`. Body: B3, B4, B5, B7, C1, C2, C4, "S12 unchanged for bands", "Grid/Flow byte-identical (golden guard)".

### Part 3: The phone query

#### Step 8: Failing tests (4 min)
Append to the Task 10 section of `FormAssetEmitterTests.cs`:

```csharp
    // ==================================================================
    // Task 10 (spec 2026-09-27 §5) — below the breakpoint: one column, in reading order
    // ==================================================================

    private static int OrderOf(string css, string id)
    {
        var rule = PhoneRule(css, id);
        Assert.That(rule, Is.Not.Null, $"no phone rule for {id}");
        var match = Regex.Match(rule!, @"order: (\d+);");
        Assert.That(match.Success, Is.True, rule);
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static FormRect Stored(FormControl control)
    {
        var pixel = (PixelGeometry)control.Geometry!;
        return new FormRect(pixel.X, pixel.Y, pixel.Width, pixel.Height);
    }

    [TestCase("480", 480)]
    [TestCase(null, 600)]
    [TestCase("-1", 600)]
    [TestCase("wide", 600)]
    public void ThePhoneQuery_UsesTheEffectiveBreakpoint(string? raw, int expected)
    {
        // ⛔ EffectiveMobileBreakpoint is the one rule: a Degraded value gives the page the default (D9).
        var page = CanvasPage(breakpoint: raw);
        page.Controls.Add(At("Button", "btn", 10, 10, 75, 23));
        Assert.That(page.Layout!.EffectiveMobileBreakpoint, Is.EqualTo(expected), "precondition");

        Assert.That(FormAssetEmitter.Css(page), Does.Contain(
            $"@media (width < {expected}px) {{\n" +
            "  .vgs-form { display: flex; flex-direction: column; gap: 8px; height: auto; min-width: 0; min-height: 0; }\n"));
    }

    [Test]
    public void ABreakpointOfZero_NeverStacks()
    {
        var page = CanvasPage(breakpoint: "0");
        page.Controls.Add(At("Button", "btn", 10, 10, 75, 23));

        Assert.That(FormAssetEmitter.Css(page), Does.Not.Contain("@media"));
    }

    [Test]
    public void ThePhoneOrder_IsFormReadingOrders()
    {
        // The owner's case (S8): a logo beside two label/box pairs — the pairs stay together.
        var page = CanvasPage();
        page.Controls.Add(At("Label", "userLabel", 130, 12, 80, 23));
        page.Controls.Add(At("PictureBox", "logo", 10, 10, 100, 100));
        page.Controls.Add(At("TextBox", "userBox", 220, 10, 150, 23));
        page.Controls.Add(At("Label", "passLabel", 130, 52, 80, 23));
        page.Controls.Add(At("TextBox", "passBox", 220, 50, 150, 23));

        var css = FormAssetEmitter.Css(page);
        var expected = FormReadingOrder.Order(page.Controls, Stored).Select(c => c.Id).ToList();
        var actual = page.Controls.OrderBy(c => OrderOf(css, c.Id)).Select(c => c.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected), "the order is FormReadingOrder's, never re-derived");
            Assert.That(actual, Is.EqualTo(new[] { "logo", "userLabel", "userBox", "passLabel", "passBox" }),
                "non-vacuity: S8's worked example");
        });
    }

    [Test]
    public void OnAPhone_TopStripsComeFirst_BottomStripsLast_AndADockedPanelJoinsTheRows()
    {
        var page = CanvasPage();
        page.Controls.Add(At("Panel", "pnlTop", 0, 0, 10, 40, dock: "Top")); // docks ABOVE the menu
        page.Controls.Add(StripOf("StatusStrip", "statusStrip1"));
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(StripOf("ToolStrip", "toolStrip1"));
        page.Controls.Add(At("Button", "btn", 10, 200, 75, 23));

        var css = FormAssetEmitter.Css(page);

        Assert.That(page.Controls.OrderBy(c => OrderOf(css, c.Id)).Select(c => c.Id),
            Is.EqualTo(new[] { "menuStrip1", "toolStrip1", "pnlTop", "btn", "statusStrip1" }),
            "spec §5/§7a: top strips first (as they stack), bottom strips last; the docked panel is an ordinary row");
    }

    [Test]
    public void AContainersChildren_AreOrderedInsideIt_InItsOwnCoordinates()
    {
        var c = At("Button", "c", 10, 80, 75, 23);
        var a = At("Button", "a", 10, 10, 75, 23);
        var b = At("Button", "b", 100, 10, 75, 23);
        var page = CanvasPage();
        page.Controls.Add(At("Panel", "pnl", 50, 50, 300, 200, children: new[] { c, a, b }));

        var css = FormAssetEmitter.Css(page);
        var panel = page.Controls[0];
        var expected = FormReadingOrder.Order(panel.Children, Stored).Select(x => x.Id);

        Assert.That(panel.Children.OrderBy(x => OrderOf(css, x.Id)).Select(x => x.Id), Is.EqualTo(expected));
    }

    private static IEnumerable<TestCaseData> PositionedWebKinds() =>
        FormControlCatalog.All
            .Where(d => d.Place == FormPlace.Positioned && d.SupportsTarget(FormTarget.Web))
            .Select(d => new TestCaseData(d.Kind).SetName($"{{m}}({d.Kind})"));

    [TestCaseSource(nameof(PositionedWebKinds))]
    public void OnAPhone_OnlyAFlaggedRowStretches(string kind)
    {
        var row = FormControlCatalog.Find(kind)!;
        var page = CanvasPage();
        FormCatalogShapes.Canonical(page, row, "ctl", geometry: new PixelGeometry { X = 10, Y = 10, Width = 140, Height = 40 });

        var rule = PhoneRule(FormAssetEmitter.Css(page), "ctl");

        Assert.That(rule, row.StretchesWhenStacked
            ? Does.Contain("align-self: stretch; width: auto")
            : Does.Contain("align-self: flex-start; width: 140px"));
    }

    [Test]
    public void OnAPhone_AHiddenContainerStaysHidden_AndAVisibleOneStacksItsChildren()
    {
        var hidden = At("Panel", "hiddenPanel", 10, 10, 200, 100, children: At("Button", "a", 5, 5, 75, 23));
        hidden.Properties["Visible"] = "False";
        var page = CanvasPage();
        page.Controls.Add(hidden);
        page.Controls.Add(At("Panel", "shownPanel", 10, 150, 200, 100, children: At("Button", "b", 5, 5, 75, 23)));

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(DesktopRule(css, "hiddenPanel"), Does.Contain("display: none"));
            Assert.That(PhoneRule(css, "hiddenPanel"), Does.Not.Contain("display"),
                "⛔ a later display:flex would un-hide it on phones");
            Assert.That(PhoneRule(css, "shownPanel"), Does.Contain("height: auto; display: flex; flex-direction: column; gap: 8px"));
            Assert.That(PhoneRule(css, "b"), Does.StartWith("position: static; order: 0"));
        });
    }

    [Test]
    public void OnAPhone_AStripSpansTheColumn_AndEveryControlIsCappedAtItsWidth()
    {
        var page = CanvasPage();
        page.Controls.Add(StripOf("MenuStrip", "menuStrip1"));
        page.Controls.Add(At("Panel", "wide", 0, 30, 800, 100));

        var css = FormAssetEmitter.Css(page);

        Assert.Multiple(() =>
        {
            Assert.That(PhoneRule(css, "menuStrip1"), Is.EqualTo("position: static; order: 0; align-self: stretch; max-width: 100%;"));
            Assert.That(PhoneRule(css, "wide"), Does.Contain("width: 800px; max-width: 100%"));
        });
    }
```

- [ ] Build; run `FullyQualifiedName~FormAssetEmitterTests`. **Expected RED:** every test in this block except `ABreakpointOfZero_NeverStacks` (already GREEN — no query is emitted at all today; a guard).

#### Step 9: Implement (4 min)
In `CanvasCss`, after `AppendKindCss(sb, form);` add `AppendStackedQuery(sb, form, designer);`. Add:

```csharp
    /// <summary>
    /// Below the phone breakpoint (spec §5) the form area is ONE flex column. Every control goes
    /// <c>position: static</c> (otherwise <c>order</c> does nothing), in <see cref="FormReadingOrder"/>'s order per
    /// sibling list; top strips first and bottom strips last; a docked control is an ordinary row. HTML order is
    /// unchanged. ⛔ The breakpoint is <see cref="FormLayout.EffectiveMobileBreakpoint"/> — the one rule; 0 never
    /// stacks, a Degraded value gives the default.
    /// </summary>
    private static void AppendStackedQuery(StringBuilder sb, FormDocument form, FormDockLayoutResult designer)
    {
        var breakpoint = form.Layout?.EffectiveMobileBreakpoint ?? FormLayout.DefaultMobileBreakpoint;
        if (breakpoint == 0)
        {
            return;
        }

        sb.Append($"@media (width < {Number(breakpoint)}px) {{\n");
        sb.Append("  .vgs-form { display: flex; flex-direction: column; gap: 8px; height: auto; min-width: 0; min-height: 0; }\n");
        AppendStacked(sb, form.Controls, designer);
        sb.Append("}\n");
    }

    /// <summary>
    /// One sibling list's stacked rules, then each container's children inside it.
    ///
    /// <para>⚠ Every rectangle here is the DESIGNER picture (a docked control's resolved rect; an undocked control's
    /// stored one). CSS <c>order</c> is static, so ordering by the design keeps it the same whichever controls user
    /// code has hidden; a hidden control stays <c>display:none</c> and its order only matters once shown.</para>
    /// </summary>
    private static void AppendStacked(
        StringBuilder sb, IReadOnlyList<FormControl> siblings, FormDockLayoutResult designer)
    {
        FormRect? RectOf(FormControl control) =>
            designer.TryGet(control, out var docked) ? docked.Bounds
            : control.Geometry is PixelGeometry pixel ? new FormRect(pixel.X, pixel.Y, pixel.Width, pixel.Height)
            : null;

        // A strip is always Top or Bottom here (FormDockLayout.EdgeOf); ordered by where it DOCKS, so the first
        // Top strip comes first and the first-documented Bottom strip (nearest the true bottom edge) comes last.
        var strips = siblings
            .Where(c => c.Definition?.Place == FormPlace.Docked && designer.TryGet(c, out _))
            .ToList();
        var top = strips.Where(c => FormDockLayout.EdgeOf(c) == FormDockEdge.Top).OrderBy(c => RectOf(c)!.Value.Y);
        var bottom = strips.Where(c => FormDockLayout.EdgeOf(c) == FormDockEdge.Bottom).OrderBy(c => RectOf(c)!.Value.Y);
        var positioned = siblings
            .Where(c => (c.Definition?.Place ?? FormPlace.Positioned) == FormPlace.Positioned && RectOf(c) != null)
            .ToList();

        var ordered = top
            .Concat(FormReadingOrder.Order(positioned, c => RectOf(c)!.Value))
            .Concat(bottom)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            AppendStackedRule(sb, ordered[i], i, RectOf(ordered[i])!.Value);
        }

        foreach (var container in positioned.Where(c => c.Children.Count > 0))
        {
            AppendStacked(sb, container.Children, designer);
        }
    }

    private static void AppendStackedRule(StringBuilder sb, FormControl control, int order, FormRect rect)
    {
        var definition = control.Definition;
        var isContainer = definition?.IsContainer == true;
        var rules = new List<string> { "position: static", $"order: {Number(order)}" };

        if (definition?.Place == FormPlace.Docked)
        {
            // A strip spans the column, as it spans the form.
            rules.Add("align-self: stretch");
        }
        else if (definition?.StretchesWhenStacked == true)
        {
            // ⛔ The catalog's facet (spec §5), never a Kind switch.
            rules.Add("align-self: stretch");
            rules.Add("width: auto");
            if (rect.Height > 0 && !isContainer)
            {
                rules.Add($"height: {Number(rect.Height)}px");
            }
        }
        else
        {
            // Small controls keep their designed size, left-aligned.
            rules.Add("align-self: flex-start");
            if (rect.Width > 0)
            {
                rules.Add($"width: {Number(rect.Width)}px");
            }

            if (rect.Height > 0 && !isContainer)
            {
                rules.Add($"height: {Number(rect.Height)}px");
            }
        }

        // A control wider than the phone never makes it scroll sideways.
        rules.Add("max-width: 100%");

        if (isContainer)
        {
            rules.Add("height: auto");

            // ⛔ Never write display for a control whose own catalog CSS writes it (Visible=false → display:none):
            // this later declaration would un-hide it on phones.
            if (!CatalogDeclarations(control).Any(d => d.StartsWith("display:", StringComparison.Ordinal)))
            {
                rules.Add("display: flex");
                rules.Add("flex-direction: column");
                rules.Add("gap: 8px");
            }
        }

        sb.Append($"  #{control.Id} {{ {string.Join("; ", rules)}; }}\n");
    }
```

- [ ] Build; run `FullyQualifiedName~FormAssetEmitterTests|FullyQualifiedName~FormReadingOrderTests|FullyQualifiedName~FormMobileBreakpointTests`. **Expected GREEN.**

#### Step 10: Commit Part 3 (2 min)
Stage `FormAssetEmitter.cs`, `FormAssetEmitterTests.cs`. Message: `feat(emitter): a Canvas page stacks into one column below its breakpoint, in FormReadingOrder's order (Task 10 part 3)`. Body: decisions 1, 5, 6; "hidden containers never get display".

### Part 4: The run-time reflow script (owner decision 1)

#### Step 11: Failing tests — `FormDockScriptTests.cs` (5 min)
Create `VisualGameStudio.Tests/Compiler/FormDockScriptTests.cs` (NEW file — Write is correct here):

```csharp
using System.Text;
using System.Text.Json;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 10, owner decision 2026-09-27: a Canvas page RE-DOCKS when user code shows or hides a docked control at run
/// time, as WinForms does — in both states. The page carries a small reflow script whose resolver MIRRORS
/// <see cref="FormDockLayout"/> (Runtime mode) and <see cref="FormAnchorCss.Docked"/>: a mirrored pair across two
/// languages. ⛔ These tests are its lock-step gate — the SAME fixtures go through both, under node, and must agree
/// byte for byte — plus the page's OWN script, extracted from the emitted HTML and run against a stub DOM.
/// </summary>
[TestFixture]
public class FormDockScriptTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-dockscript-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    // ==================================================================
    // Fixtures — situations, not kinds: every docking rule FormDockLayoutTests pins, plus nesting and hiding
    // ==================================================================

    private static FormControl Strip(string kind, string id, string? dock = null)
    {
        var strip = new FormControl { Kind = kind, Id = id };
        if (dock != null)
        {
            strip.Properties["Dock"] = dock;
        }

        return strip;
    }

    private static FormControl Box(string id, int width, int height, string? dock, params FormControl[] children)
    {
        var box = new FormControl
        {
            Kind = "Panel", Id = id,
            Geometry = new PixelGeometry { X = 3, Y = 5, Width = width, Height = height, Dock = dock }
        };
        box.Children.AddRange(children);
        return box;
    }

    private static FormControl Hidden(FormControl control)
    {
        control.Properties["Visible"] = "False";
        return control;
    }

    private static FormDocument Page(params FormControl[] controls) => Page(400, 300, "600", controls);

    private static FormDocument Page(int width, int height, string breakpoint, params FormControl[] controls)
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "Page", Width = width, Height = height,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = breakpoint }
        };
        page.Controls.AddRange(controls);
        return page;
    }

    private static readonly (string Name, Func<FormDocument> Build)[] Fixtures =
    {
        ("Top alone", () => Page(Box("a", 80, 50, "Top"))),
        ("Bottom alone", () => Page(Box("a", 80, 50, "Bottom"))),
        ("Left alone", () => Page(Box("a", 80, 50, "Left"))),
        ("Right alone", () => Page(Box("a", 80, 50, "Right"))),
        ("Fill alone", () => Page(Box("a", 80, 50, "Fill"))),
        ("strips stack", () => Page(Strip("MenuStrip", "m"), Strip("ToolStrip", "t"), Strip("StatusStrip", "s"),
            Strip("ToolStrip", "tb", "Bottom"))),
        ("a Top panel before the menu", () => Page(Box("p", 10, 40, "Top"), Strip("MenuStrip", "m"), Strip("StatusStrip", "s"),
            Box("f", 1, 1, "Fill"))),
        ("a Fill before a later Bottom overlaps it", () => Page(Box("f", 1, 1, "Fill"), Strip("StatusStrip", "s"))),
        ("Left then Top", () => Page(Box("l", 80, 10, "Left"), Box("t", 10, 40, "Top"), Box("r", 30, 10, "Right"))),
        ("Fill does not consume", () => Page(Box("f", 1, 1, "Fill"), Box("t", 10, 40, "Top"))),
        ("overflowing Top then Bottom", () => Page(Box("t", 10, 400, "Top"), Box("b", 10, 50, "Bottom"))),
        ("overflowing Left then Right", () => Page(Box("l", 500, 10, "Left"), Box("r", 50, 10, "Right"))),
        ("Fill after an overflow", () => Page(Box("t", 10, 400, "Top"), Box("f", 1, 1, "Fill"))),
        ("nested in a docked Fill", () => Page(Strip("MenuStrip", "m"),
            Box("f", 7, 7, "Fill", Strip("MenuStrip", "im"), Box("ib", 60, 20, "Bottom"), Box("il", 40, 5, "Left")))),
        ("docked inside an undocked panel", () => Page(Box("p", 200, 100, null, Box("c", 10, 30, "Top"), Box("d", 10, 10, "Fill")))),
        ("zero and negative sizes", () => Page(Box("z", 0, -5, "Top"), Box("n", -10, 20, "Left"), Box("f", 1, 1, "Fill"))),
        ("a hidden container", () => Page(Hidden(Box("p", 200, 100, "Top", Box("c", 10, 30, "Top"))), Box("f", 1, 1, "Fill")))
    };

    /// <summary>Every fixture as designed, then once per docked control (at any depth) with THAT control hidden.</summary>
    private static List<(string Name, FormDocument Document)> Cases()
    {
        var cases = new List<(string, FormDocument)>();
        foreach (var (name, build) in Fixtures)
        {
            cases.Add((name, build()));
            foreach (var id in build().AllControls().Where(c => FormDockLayout.EdgeOf(c) != null).Select(c => c.Id))
            {
                var document = build();
                document.FindById(id)!.Properties["Visible"] = "False";
                cases.Add(($"{name} / {id} hidden", document));
            }
        }

        return cases;
    }

    // ==================================================================
    // node
    // ==================================================================

    /// <summary>Runs <paramref name="script"/> under node and returns stdout; null when node is not on PATH.</summary>
    internal static string? RunNode(string dir, string fileName, string script)
    {
        File.WriteAllText(Path.Combine(dir, fileName), script);
        try
        {
            var (exit, stdout, stderr) = CliTestHarness.RunProcess("node", new[] { fileName }, dir, timeoutMs: 30_000);
            if (exit != 0 && (stderr.Contains("not recognized") || stderr.Contains("not found")))
            {
                return null;
            }

            Assert.That(exit, Is.Zero, $"node failed:\n{stdout}\n{stderr}");
            return stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>What the page's live dock stylesheet must hold for <paramref name="dock"/> — composed from the C# deciders.</summary>
    internal static string LiveCss(FormDockLayoutResult dock, int breakpoint)
    {
        var rules = string.Concat(dock.All.Select(d =>
            "#" + d.Control.Id + "{" +
            string.Concat(FormAnchorCss.Docked(d).Select(p => p.Property + ":" + p.Value + ";")) + "}"));
        return breakpoint > 0 ? "@media (width >= " + breakpoint + "px){" + rules + "}" : rules;
    }

    internal sealed record GlueRun(string Options, string Before, string Flipped, string Restored);

    /// <summary>
    /// Runs the page's OWN reflow script — extracted from <paramref name="html"/> — against a stub DOM: every control
    /// of <paramref name="model"/> is an element (hidden when designed hidden), the observer is captured, and
    /// <paramref name="toggleId"/>'s visibility is flipped and then restored, the observer firing after each.
    /// Null when node is absent.
    /// </summary>
    internal static GlueRun? RunGlue(string dir, string html, FormDocument model, string toggleId)
    {
        const string open = "<script data-vgs=\"dock\">";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "the page carries no reflow script");
        start += open.Length;
        var script = html.Substring(start, html.IndexOf("</script>", start, StringComparison.Ordinal) - start);

        var elements = string.Join("\n", model.AllControls().Select(c =>
            $"el({JsonSerializer.Serialize(c.Id)}, {(c.IsHidden ? "true" : "false")});"));
        var id = JsonSerializer.Serialize(toggleId);
        var flip = model.FindById(toggleId)!.IsHidden ? "\"block\"" : "\"none\"";

        var harness = $$"""
            const els = new Map();
            function el(id, designHidden) { els.set(id, { id: id, style: {}, hidden: false, designHidden: designHidden }); }
            {{elements}}
            const form = { className: "vgs-form" };
            let observer = null, options = null, target = null;
            globalThis.MutationObserver = function (callback) {
              observer = callback;
              this.observe = function (t, o) { target = t; options = o; };
            };
            globalThis.getComputedStyle = function (e) {
              return { display: e.style.display ? e.style.display : (e.hidden || e.designHidden ? "none" : "block") };
            };
            const head = { children: [], appendChild: function (c) { this.children.push(c); return c; } };
            globalThis.document = {
              head: head,
              querySelector: function (s) { return s === ".vgs-form" ? form : null; },
              getElementById: function (id) { return els.get(id) || null; },
              createElement: function (t) { return { tagName: t, id: "", textContent: "" }; }
            };
            function live() { return head.children.length ? head.children[0].textContent : "(none)"; }
            {{script}}
            console.log("OPTIONS " + JSON.stringify({ observedForm: target === form, options: options }));
            console.log("BEFORE " + live());
            els.get({{id}}).style.display = {{flip}}; observer([]);
            console.log("FLIPPED " + live());
            els.get({{id}}).style.display = ""; observer([]);
            console.log("RESTORED " + live());
            """;

        var stdout = RunNode(dir, "glue.cjs", harness);
        if (stdout == null)
        {
            return null;
        }

        string Line(string prefix) =>
            stdout.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith(prefix + " ", StringComparison.Ordinal))
                .Substring(prefix.Length + 1);

        return new GlueRun(Line("OPTIONS"), Line("BEFORE"), Line("FLIPPED"), Line("RESTORED"));
    }

    // ==================================================================
    // The data and the markup (fast)
    // ==================================================================

    private static IEnumerable<string> DockedIdsIn(JsonElement node)
    {
        foreach (var child in node.GetProperty("k").EnumerateArray())
        {
            if (child.GetProperty("e").ValueKind == JsonValueKind.String)
            {
                yield return child.GetProperty("i").GetString()!;
            }

            foreach (var nested in DockedIdsIn(child))
            {
                yield return nested;
            }
        }
    }

    [Test]
    public void TheData_NamesExactlyWhatTheResolverDocks()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, document) in Cases())
            {
                using var data = JsonDocument.Parse(FormDockScript.DataJson(document));
                Assert.That(DockedIdsIn(data.RootElement),
                    Is.EquivalentTo(FormDockLayout.Resolve(document, FormDockMode.Designer).All.Select(d => d.Control.Id)),
                    name + ": the script must be able to dock everything that COULD dock (Designer), whatever is hidden now");
            }
        });
    }

    [Test]
    public void APageWithNothingDocked_CarriesNoScript()
    {
        var page = Page(Box("b", 10, 10, null));

        Assert.Multiple(() =>
        {
            Assert.That(FormDockScript.PageScript(page), Is.Null);
            Assert.That(FormAssetEmitter.Html(page, "App.js"), Does.Not.Contain("data-vgs=\"dock\""));
        });
    }

    [Test]
    public void AGridPage_NeverCarriesTheScript()
    {
        var page = new FormDocument { Target = FormTarget.Web, Name = "G", Layout = new FormLayout { Kind = FormLayoutKind.Grid } };
        page.Controls.Add(Strip("MenuStrip", "m"));

        Assert.That(FormAssetEmitter.Html(page, "App.js"), Does.Not.Contain("data-vgs=\"dock\""));
    }

    [Test]
    public void ADockedPage_CarriesAClassicScript_AfterTheFormArea_BeforeTheModule()
    {
        var html = FormAssetEmitter.Html(Page(Strip("MenuStrip", "m")), "App.js");
        var dock = html.IndexOf("<script data-vgs=\"dock\">", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(dock, Is.GreaterThan(html.LastIndexOf("</div>", StringComparison.Ordinal)),
                "after the form area: the elements it observes exist");
            Assert.That(dock, Is.LessThan(html.IndexOf("<script type=\"module\"", StringComparison.Ordinal)),
                "before the module: it is observing when InitializeComponent runs");
        });
    }

    // ==================================================================
    // The lock-step gate and the page's own script (node)
    // ==================================================================

    [Test]
    [Category("Integration")]
    public void TheScriptsResolver_AgreesWithFormDockLayout_OnEveryFixture()
    {
        var cases = Cases();
        Assert.That(cases.Any(c =>
                FormDockLayout.Resolve(c.Document, FormDockMode.Runtime).All.Count !=
                FormDockLayout.Resolve(c.Document, FormDockMode.Designer).All.Count),
            Is.True, "non-vacuity: some case hides a docked control");

        var js = new StringBuilder(FormDockScript.Core);
        js.Append("\nconst cases = [\n");
        foreach (var (_, document) in cases)
        {
            js.Append(FormDockScript.DataJson(document)).Append(",\n");
        }

        js.Append("""
            ];
            process.stdout.write(JSON.stringify(cases.map(function (root) {
              var out = [];
              vgsDockWalk(root.k, root.w, root.h, function (n) { return n.hd; }, out);
              return out.map(function (d) { return { i: d.n.i, e: d.e, b: d.b, css: vgsDockCss(d) }; });
            })));
            """);

        var stdout = RunNode(_dir, "lockstep.cjs", js.ToString());
        if (stdout == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        using var actual = JsonDocument.Parse(stdout!);
        Assert.That(actual.RootElement.GetArrayLength(), Is.EqualTo(cases.Count));

        Assert.Multiple(() =>
        {
            for (var k = 0; k < cases.Count; k++)
            {
                var expected = JsonSerializer.Serialize(
                    FormDockLayout.Resolve(cases[k].Document, FormDockMode.Runtime).All.Select(d => new
                    {
                        i = d.Control.Id,
                        e = d.Edge.ToString(),
                        b = new[] { d.Bounds.X, d.Bounds.Y, d.Bounds.Width, d.Bounds.Height },
                        css = FormAnchorCss.Docked(d).Select(p => new[] { p.Property, p.Value })
                    }));
                Assert.That(actual.RootElement[k].GetRawText(), Is.EqualTo(expected), cases[k].Name);
            }
        });
    }

    private static FormDocument Glued(bool panelHidden = false, string breakpoint = "600")
    {
        var panel = Box("pnlTop", 10, 40, "Top");
        if (panelHidden)
        {
            Hidden(panel);
        }

        return Page(640, 480, breakpoint, panel, Strip("MenuStrip", "menuStrip1"), Strip("StatusStrip", "statusStrip1"),
            Box("fill", 1, 1, "Fill"));
    }

    [Test]
    [Category("Integration")]
    public void HidingADockedPanel_AtRunTime_ReDocksItsSiblings_AsWinFormsDoes()
    {
        var model = Glued();
        var run = RunGlue(_dir, FormAssetEmitter.Html(model, "App.js"), model, "pnlTop");
        if (run == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(run!.Options, Does.Contain("\"observedForm\":true"));
            Assert.That(run.Options, Does.Contain("\"subtree\":true").And.Contain("\"style\"")
                .And.Contain("\"hidden\"").And.Contain("\"class\""));
            Assert.That(run.Before, Is.EqualTo("(none)"),
                "the page's first state is its stylesheet (Runtime); the script runs only on a change");
            Assert.That(run.Flipped, Is.EqualTo(LiveCss(FormDockLayout.Resolve(Glued(panelHidden: true), FormDockMode.Runtime), 600)));
            Assert.That(run.Flipped, Does.Contain("#menuStrip1{left:0px;right:0px;top:0px;height:24px;}"),
                "non-vacuity: the menu closes the 40px gap");
            Assert.That(run.Restored, Is.EqualTo(LiveCss(FormDockLayout.Resolve(model, FormDockMode.Runtime), 600)));
            Assert.That(run.Restored, Does.Contain("#menuStrip1{left:0px;right:0px;top:40px;height:24px;}"));
        });
    }

    [Test]
    [Category("Integration")]
    public void ShowingADesignHiddenPanel_AtRunTime_MakesRoomForIt()
    {
        var model = Glued(panelHidden: true);
        var run = RunGlue(_dir, FormAssetEmitter.Html(model, "App.js"), model, "pnlTop");
        if (run == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(run!.Flipped, Is.EqualTo(LiveCss(FormDockLayout.Resolve(Glued(), FormDockMode.Runtime), 600)),
                "shown: the siblings move down exactly as WinForms re-docks them");
            Assert.That(run.Restored, Is.EqualTo(LiveCss(FormDockLayout.Resolve(model, FormDockMode.Runtime), 600)));
        });
    }

    [Test]
    [Category("Integration")]
    public void WithABreakpointOfZero_TheLiveRulesAreUnwrapped()
    {
        var model = Glued(breakpoint: "0");
        var run = RunGlue(_dir, FormAssetEmitter.Html(model, "App.js"), model, "pnlTop");
        if (run == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        Assert.That(run!.Flipped, Is.EqualTo(LiveCss(FormDockLayout.Resolve(Glued(panelHidden: true, breakpoint: "0"), FormDockMode.Runtime), 0)));
    }
}
```

- [ ] Build. **Expected RED:** CS0103 `FormDockScript` does not exist (the whole test project fails to build — that is the red for this step).

#### Step 12: Implement `FormDockScript` (5 min)
Create `BasicLang/Forms/FormDockScript.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BasicLang.Forms;

/// <summary>
/// ⛔⛔ A Canvas page's RUN-TIME re-docking (owner decision 2026-09-27). When user code shows or hides a docked control
/// (or a strip) — <c>el.style.display</c>, <c>el.hidden</c>, a class, or piece 2's portable <c>Visible</c> — WinForms
/// re-docks the siblings; the page does too, in BOTH states. The stylesheet is the page's first state
/// (<see cref="FormDockMode.Runtime"/>); this script runs only on a change.
///
/// <para>⛔⛔ <see cref="Core"/> is a MIRROR of <see cref="FormDockLayout.ResolveSiblings"/>, <c>FormDockLayout.Walk</c>
/// (Runtime) and <see cref="FormAnchorCss.Docked"/>, in JavaScript: a mirrored pair across two languages — the
/// <c>Tracks</c>/<c>ParseTracks</c> scar, a third time, unless it is gated. <c>FormDockScriptTests</c> runs both over one
/// fixture table under node and requires byte-identical answers. Change both in ONE commit.</para>
///
/// <para>The hook is a <c>MutationObserver</c> on <c>.vgs-form</c> (attributes <c>style</c>/<c>class</c>/<c>hidden</c>,
/// subtree): there is no <c>Visible</c> setter on the web to hook — a control's field is a DOM <c>Element</c> and user
/// code writes the DOM directly (measured). Its callback is a microtask, so the re-dock lands before the next paint.
/// The live rules go into ONE <c>&lt;style id="vgs-dock-live"&gt;</c> in <c>&lt;head&gt;</c> (later in the cascade
/// than the page's stylesheet, same specificity), inside <c>@media (width &gt;= breakpoint)</c> so the phone column is
/// never overridden.</para>
/// </summary>
public static class FormDockScript
{
    /// <summary>The pure resolver, mirrored from C#. ⚠ Line endings normalised: a raw literal takes the source file's.</summary>
    public static readonly string Core = """
        // vgs-dock: re-docks a Canvas page when a docked control is shown or hidden at run time, as WinForms does.
        // MIRROR of BasicLang FormDockLayout.ResolveSiblings / Walk (Runtime) and FormAnchorCss.Docked.
        // FormDockScriptTests runs this and the C# over one table; change both in one commit.
        function vgsDockSiblings(nodes, width, height, hidden) {
          var cw = Math.max(0, width), ch = Math.max(0, height);
          var x = 0, y = 0, w = cw, h = ch, placed = [];
          for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (node.e === null || hidden(node)) continue;
            var aw = Math.max(0, w), ah = Math.max(0, h), b;
            if (node.e === "Top") { b = [x, y, aw, node.h]; y += node.h; h -= node.h; }
            else if (node.e === "Bottom") { b = [x, y + h - node.h, aw, node.h]; h -= node.h; }
            else if (node.e === "Left") { b = [x, y, node.w, ah]; x += node.w; w -= node.w; }
            else if (node.e === "Right") { b = [x + w - node.w, y, node.w, ah]; w -= node.w; }
            else { b = [x, y, aw, ah]; }
            placed.push({ n: node, e: node.e, b: b, cw: cw, ch: ch });
          }
          return placed;
        }
        function vgsDockWalk(nodes, width, height, hidden, out) {
          var placed = vgsDockSiblings(nodes, width, height, hidden);
          for (var p = 0; p < placed.length; p++) out.push(placed[p]);
          for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (!node.c || hidden(node)) continue;
            var iw = node.w, ih = node.h;
            for (var q = 0; q < placed.length; q++) {
              if (placed[q].n === node) { iw = placed[q].b[2]; ih = placed[q].b[3]; break; }
            }
            if (node.k.length > 0) vgsDockWalk(node.k, iw, ih, hidden, out);
          }
        }
        function vgsDockCss(d) {
          var b = d.b;
          function px(v) { return v + "px"; }
          var right = px(d.cw - (b[0] + b[2])), bottom = px(d.ch - (b[1] + b[3]));
          if (d.e === "Top") return [["left", px(b[0])], ["right", right], ["top", px(b[1])], ["height", px(b[3])]];
          if (d.e === "Bottom") return [["left", px(b[0])], ["right", right], ["bottom", bottom], ["height", px(b[3])]];
          if (d.e === "Left") return [["left", px(b[0])], ["width", px(b[2])], ["top", px(b[1])], ["bottom", bottom]];
          if (d.e === "Right") return [["right", right], ["width", px(b[2])], ["top", px(b[1])], ["bottom", bottom]];
          return [["left", px(b[0])], ["right", right], ["top", px(b[1])], ["bottom", bottom]];
        }

        """.ReplaceLineEndings("\n");

    /// <summary>The DOM glue: observe, re-walk, write the live rules.</summary>
    public static readonly string Bootstrap = """
        function vgsDockStart(root, breakpoint) {
          var form = document.querySelector(".vgs-form");
          if (!form || typeof MutationObserver === "undefined") return;
          var live = null, last = null;
          function hidden(node) {
            var el = document.getElementById(node.i);
            return el ? getComputedStyle(el).display === "none" : node.hd;
          }
          function reflow() {
            var out = [];
            vgsDockWalk(root.k, root.w, root.h, hidden, out);
            var rules = "";
            for (var r = 0; r < out.length; r++) {
              var decls = vgsDockCss(out[r]), text = "";
              for (var d = 0; d < decls.length; d++) text += decls[d][0] + ":" + decls[d][1] + ";";
              rules += "#" + out[r].n.i + "{" + text + "}";
            }
            var css = breakpoint > 0 ? "@media (width >= " + breakpoint + "px){" + rules + "}" : rules;
            if (css === last) return;
            last = css;
            if (!live) {
              live = document.createElement("style");
              live.id = "vgs-dock-live";
              document.head.appendChild(live);
            }
            live.textContent = css;
          }
          new MutationObserver(reflow).observe(form, { attributes: true, subtree: true, attributeFilter: ["style", "class", "hidden"] });
        }

        """.ReplaceLineEndings("\n");

    /// <summary>
    /// The inline classic script for <paramref name="form"/>'s page, or null when nothing on it docks. An IIFE, so
    /// nothing leaks into the page's globals beside App.js.
    /// </summary>
    public static string? PageScript(FormDocument form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var root = Root(form);
        if (!Docks(root.Children))
        {
            return null;
        }

        var breakpoint = form.Layout?.EffectiveMobileBreakpoint ?? FormLayout.DefaultMobileBreakpoint;
        var sb = new StringBuilder();
        sb.Append("<script data-vgs=\"dock\">\n(function () {\n");
        sb.Append(Core).Append(Bootstrap);
        sb.Append("vgsDockStart(").Append(JsonSerializer.Serialize(root)).Append(", ")
          .Append(breakpoint.ToString(CultureInfo.InvariantCulture)).Append(");\n");
        sb.Append("})();\n</script>\n");
        return sb.ToString();
    }

    /// <summary>
    /// The script's data for <paramref name="form"/>: the root client size (<see cref="FormDocument.DesignSize"/>) and,
    /// per sibling list in DOCUMENT order, everything that docks or holds something that does — each node's edge
    /// (<see cref="FormDockLayout.EdgeOf"/>), own size (<see cref="FormDockLayout.OwnSizeOf"/>), whether it lays
    /// children out (<see cref="FormDockLayout.HasClientArea"/>) and whether it is designed hidden. ⛔ The resolver's
    /// own rules, never copies. ⚠ JSON-escaped, so no id can close the <c>&lt;script&gt;</c>.
    /// </summary>
    public static string DataJson(FormDocument form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return JsonSerializer.Serialize(Root(form));
    }

    private static FormDockNode Root(FormDocument form)
    {
        var (width, height) = form.DesignSize;
        return new FormDockNode("", null, width, height, true, false, Nodes(form.Controls));
    }

    private static List<FormDockNode> Nodes(IReadOnlyList<FormControl> siblings)
    {
        var nodes = new List<FormDockNode>();
        foreach (var control in siblings)
        {
            var edge = FormDockLayout.EdgeOf(control);
            var hasClientArea = FormDockLayout.HasClientArea(control);
            var children = hasClientArea ? Nodes(control.Children) : new List<FormDockNode>();

            // Neither docks nor holds anything that does: it consumes nothing, so the script need not know it.
            if (edge == null && children.Count == 0)
            {
                continue;
            }

            var (width, height) = FormDockLayout.OwnSizeOf(control);
            nodes.Add(new FormDockNode(control.Id, edge?.ToString(), width, height, hasClientArea, control.IsHidden, children));
        }

        return nodes;
    }

    private static bool Docks(IEnumerable<FormDockNode> nodes) =>
        nodes.Any(n => n.Edge != null || Docks(n.Children));
}

/// <summary>One node of <see cref="FormDockScript.DataJson"/>. Short names: this ships in every docked page.</summary>
internal sealed record FormDockNode(
    [property: JsonPropertyName("i")] string Id,
    [property: JsonPropertyName("e")] string? Edge,
    [property: JsonPropertyName("w")] int Width,
    [property: JsonPropertyName("h")] int Height,
    [property: JsonPropertyName("c")] bool HasClientArea,
    [property: JsonPropertyName("hd")] bool DesignHidden,
    [property: JsonPropertyName("k")] IReadOnlyList<FormDockNode> Children);
```

Then in `FormAssetEmitter.AppendCanvasBody`, after `sb.Append("</div>\n");` add:

```csharp
        // ⛔ Owner decision 2026-09-27: the page re-docks when user code shows or hides a docked control. A CLASSIC
        // script after the form area (the elements exist) and before the module (it observes InitializeComponent).
        if (FormDockScript.PageScript(form) is { } dock)
        {
            sb.Append(dock);
        }
```

In `CanvasCss`'s summary, turn `<c>FormDockScript</c>` into `<see cref="FormDockScript"/>`; in `FormDockLayout.OwnSizeOf`'s summary likewise (Step 3's placeholder).

- [ ] Build. ⚠ If `JsonSerializer` refuses the internal record, make `FormDockNode` public (it is data, not API). Run `FullyQualifiedName~FormDockScriptTests|FullyQualifiedName~FormAssetEmitterTests|FullyQualifiedName~FormDockLayoutTests` (this runs the Integration node rows too — they carry the category, not a filter exclusion).

**Expected GREEN, all.** The node rows must RUN, not skip: re-run `FullyQualifiedName~TheScriptsResolver_AgreesWithFormDockLayout` alone and check "Passed: 1, Skipped: 0". A skip here on Windows means node was not found — fix PATH, never accept it.

#### Step 13: The mirror is named on both C# sides (2 min)
Doc text only:
- `FormDockLayout`'s class summary, append a `<para>`: `⛔⛔ MIRRORED in JavaScript by <see cref="FormDockScript.Core"/> (the page's run-time re-docking): ResolveSiblings and Walk in Runtime mode. FormDockScriptTests runs both over one table under node — change them in the SAME commit.`
- `FormAnchorCss.Docked`'s summary, append: `⛔ MIRRORED by vgsDockCss in <see cref="FormDockScript.Core"/>; FormDockScriptTests gates the pair.`
- [ ] Build (no behaviour change).

#### Step 14: Commit Part 4 (2 min)
Stage `BasicLang/Forms/FormDockScript.cs` (new), `FormAssetEmitter.cs`, `FormDockLayout.cs`, `FormAnchorCss.cs`, `VisualGameStudio.Tests/Compiler/FormDockScriptTests.cs` (new). Message: `feat(emitter): a Canvas page re-docks when a docked control is shown or hidden at run time — a JS mirror of FormDockLayout, gated in lock-step under node (Task 10 part 4)`. Body: owner decision 1, B1, B2, the lock-step design (5 lines from "The run-time reflow script" above), decisions 2, 3, 8–11.

### Part 5: The web anchor diagnostic, and the docs Task 10 makes stale

#### Step 15: Failing tests (3 min)
In `VisualGameStudio.Tests/Compiler/FormRegionWriterTests.cs`, append at the end of the class:

```csharp
    // ==================================================================
    // Task 10 (spec 2026-09-27 §4) — a Canvas page reads Anchor too, so an unknown edge is refused there as well
    // ==================================================================

    private static FormDocument CanvasLoginFormAnchored(string anchor)
    {
        var form = WebLoginForm();
        form.Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = "600" };
        form.Width = 640;
        form.Height = 480;
        form.Controls[0].Geometry = new PixelGeometry { X = 10, Y = 10, Width = 75, Height = 23, Anchor = anchor };
        return form;
    }

    [Test]
    public void ACanvasPagesUnknownAnchorEdge_IsRefused()
    {
        // ⛔ Before Task 10, CheckAnchors returned early off WinForms, and FormAnchorCss silently dropped the unknown
        // edge: "Left,Rigth" anchored the control Left only, and "Rigth" alone CENTRED it — from a green build.
        var result = WriteWeb(CanvasLoginFormAnchored("Left,Rigth"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(result.Changed, Is.False, "a refused write leaves the user's file alone");
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.AnchorNotExpressible));
            Assert.That(string.Join("\n", result.Diagnostics.Select(d => d.Message)), Does.Contain("Rigth"));
        });
    }

    [Test]
    public void ACanvasPagesMultiEdgeAnchor_IsWritten()
    {
        var result = WriteWeb(CanvasLoginFormAnchored("Top,Right"));

        Assert.That(result.Refused, Is.False, string.Join("; ", result.Diagnostics.Select(d => d.Format())));
    }
```

- [ ] Build; run `FullyQualifiedName~FormRegionWriterTests`. **Expected RED:** `ACanvasPagesUnknownAnchorEdge_IsRefused` (not refused). `ACanvasPagesMultiEdgeAnchor_IsWritten` is already GREEN (a guard).

#### Step 16: Implement (2 min)
`BasicLang/Forms/RegionWriter.cs`, in `CheckAnchors`, replace

```csharp
        if (form.Target != FormTarget.WinForms)
        {
            return;
        }
```

with

```csharp
        // ⛔ By VOCABULARY (spec 2026-09-27 §4): a Canvas page reads Anchor too (FormAnchorCss), and would otherwise
        // anchor an unknown edge to less than written — a misspelt "Rigth" alone CENTRES the control on the page.
        if (!FormVocabulary.IsPixel(form))
        {
            return;
        }
```

In its summary add the sentence: `Asked of every PIXEL document (FormVocabulary.IsPixel): a .blform and a Canvas page.`
- [ ] Build; run `FullyQualifiedName~FormRegionWriterTests|FullyQualifiedName~FormAnchorDockPickerTests|FullyQualifiedName~FormPixelVocabularyTests`. **Expected GREEN.** (If a Canvas fixture elsewhere writes an unknown anchor through `RegionWriter`, it goes red on purpose; none was found by grep at `e1c3de72`.)

#### Step 17: Doc fixes (3 min)
Text only:
- `BasicLang/Forms/FormReadingOrder.cs:38-40`: replace "Top strips first / bottom strips last, and a docked control's resolved rect (spec §7a), are the emitter's to apply before and around this call (Task 10)." with "Top strips first / bottom strips last, and a docked control's resolved rect (spec §7a), are applied around this call by the emitter (<c>FormAssetEmitter.AppendStacked</c>), which orders by the DESIGNER picture so the order does not depend on what user code has hidden."
- `BasicLang/Forms/FormControl.cs` `IsDockedToBottom` summary: "the web emitter (<c>FormAssetEmitter.Html</c>, which puts a Bottom strip's <c>&lt;footer&gt;</c> after the form div)" → "the web emitter on a Grid/Flow page (<c>FormAssetEmitter.Html</c>, which puts a Bottom strip's <c>&lt;footer&gt;</c> after the form div; a Canvas page places strips through <c>FormDockLayout</c>, which calls this)".
- `BasicLang/Forms/FormDockLayout.cs` `TryGetClientSize` summary: "(Task 10)" → "(<c>FormAssetEmitter.CanvasPlacement</c>)".
- [ ] Build.

#### Step 18: Commit Part 5 (2 min)
Stage `RegionWriter.cs`, `FormRegionWriterTests.cs`, `FormReadingOrder.cs`, `FormControl.cs`, `FormDockLayout.cs`. Message: `feat(forms): an unknown anchor edge on a Canvas page is refused as on a .blform; docs after Task 10 (Task 10 part 5)`. Body: plan ≈3201/3217 carried item landed; C9 follow-up (`design --check`).

### Part 6: A real build, run; mutations; gate

#### Step 19: `FormBuildEmissionTests` — a Canvas page built by the real CLI and RUN (4 min)
Edit `VisualGameStudio.Tests/Compiler/FormBuildEmissionTests.cs`; append at the end of the class:

```csharp
    // ==================================================================
    // Task 10 — a Canvas page, built by the REAL CLI and RUN (a green build is not a running page)
    // ==================================================================

    private const string CanvasLoginForm = """
        <WebForm Name="LoginForm" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas" MobileBreakpoint="600"/>
          <Controls>
            <Panel Id="pnlTop" X="0" Y="0" Width="10" Height="40" Dock="Top" TabIndex="0"/>
            <MenuStrip Id="menuStrip1" Dock="Top"/>
            <Label Id="lblUser" Text="User" X="12" Y="80" Width="100" Height="23" TabIndex="1"/>
            <Button Id="btnLogin" Text="Sign in" X="520" Y="80" Width="75" Height="23" Anchor="Top,Right" TabIndex="2">
              <Bind Event="click" Handler="btnLogin_Click"/>
            </Button>
            <StatusStrip Id="statusStrip1" Dock="Bottom"/>
          </Controls>
          <Components/>
          <Resources/>
        </WebForm>
        """;

    [Test]
    public void ACanvasPage_BuiltByTheRealCli_RunsAndCarriesItsPixelLayout()
    {
        WriteProject("Main.bas", "LoginForm.blwebform", "LoginForm.bas");
        Write("LoginForm.blwebform", CanvasLoginForm);
        Write("Main.bas",
            "Sub Main()\n" +
            "    Console.WriteLine(\"App loaded\")\n" +
            $"    {BasicLang.Forms.FormAssetEmitter.DispatchCall}\n" +
            "End Sub\n");

        var diagnostics = WriteDesignedCodeBehind(CanvasLoginForm);
        Assert.That(diagnostics.Where(d => !d.IsWarning), Is.Empty,
            string.Join("; ", diagnostics.Select(d => d.Message)));

        var (exit, stdout, stderr) = Build();
        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var html = File.ReadAllText(Path.Combine(OutputDir, "LoginForm.html"));
        var css = File.ReadAllText(Path.Combine(OutputDir, "LoginForm.css"));
        var model = BasicLang.Forms.Serialization.FormDocumentReader
            .Read(Path.Combine(_dir, "LoginForm.blwebform"), CanvasLoginForm).Model;

        Assert.Multiple(() =>
        {
            var open = html.IndexOf("<div class=\"vgs-form\">", StringComparison.Ordinal);
            var close = html.LastIndexOf("</div>", StringComparison.Ordinal);
            Assert.That(html.IndexOf("id=\"menuStrip1\"", StringComparison.Ordinal), Is.GreaterThan(open).And.LessThan(close),
                "the real build writes the Canvas markup: strips inside the form area (spec §3)");
            Assert.That(html.IndexOf("id=\"statusStrip1\"", StringComparison.Ordinal), Is.GreaterThan(open).And.LessThan(close));
            Assert.That(html, Does.Contain("<script data-vgs=\"dock\">"), "the page carries its reflow script");
            Assert.That(css, Is.EqualTo(BasicLang.Forms.FormAssetEmitter.Css(model)),
                "the build writes exactly the emitter's Canvas stylesheet");
            Assert.That(css, Does.Contain("min-width: 640px;").And.Contain("@media (width < 600px)"));
        });

        var ran = RunPage();
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("ReferenceError"), "the page threw on load");
            Assert.That(ran, Does.Not.Contain("LOAD ERROR"));
            Assert.That(ran, Does.Contain("App loaded"), "the script did not run at all");
            Assert.That(ran, Does.Contain("HANDLER FIRED"), "the designer wired something that does not run");
        });

        // ⛔ And the page's OWN reflow script — which no App.js harness ever runs (B2).
        var glue = FormDockScriptTests.RunGlue(_dir, html, model, "pnlTop");
        Assert.That(glue, Is.Not.Null, "node ran App.js above, so it must run the page script too");

        var hidden = BasicLang.Forms.Serialization.FormDocumentReader
            .Read(Path.Combine(_dir, "LoginForm.blwebform"), CanvasLoginForm).Model;
        hidden.FindById("pnlTop")!.Properties["Visible"] = "False";

        Assert.That(glue!.Flipped, Is.EqualTo(FormDockScriptTests.LiveCss(
            BasicLang.Forms.FormDockLayout.Resolve(hidden, BasicLang.Forms.FormDockMode.Runtime), 600)));
    }
```

- [ ] Build; run `FullyQualifiedName~FormBuildEmissionTests`. **Expected GREEN** (this is the RUN proof of Parts 2–4; it has no red phase of its own, so M10.1 below is its mutation). If the JS rows fail with `ERROR_USER_MAPPED_FILE`, A/B on `e1c3de72` before calling it a regression (plan Traps).
- [ ] Commit: stage `FormBuildEmissionTests.cs`. Message: `test(emitter): a Canvas page built by the real CLI runs, and its own reflow script runs (Task 10)`.

#### Step 20: Mutations (Edit → build → run the named tests → see red → Edit back → build → green; never `git checkout --`, never Copy-Item) (20 min total)

| # | Rule | Mutant (exact edit) | Must go red (at least) |
|---|---|---|---|
| M10.1 | Strips inside the form area (plan M6) | `Html`: `if (FormVocabulary.IsPixel(form))` → `if (false)` | `ACanvasPage_PutsItsStripsInside…`, `ADockedPage_CarriesAClassicScript…`, `ACanvasPage_BuiltByTheRealCli…` |
| M10.2 | Css by vocabulary | `Css`: `if (FormVocabulary.IsPixel(form))` → `if (form.Target == FormTarget.WinForms)` | `TheFormArea_Fills…` ×2, `APositionedControl_IsWhere…` ×8, `DockedThings…` |
| M10.3 | Container size from the resolver | `CanvasPlacement`: replace the whole `if (parent == null) … else if (…) { return null; }` block with `client = runtime.RootClientSize;` | `ANestedControl_IsAnchoredAgainstItsContainersClientSize…`, `AChildOfAHiddenPanel…` |
| M10.4 | Runtime first | `CanvasPlacement`: swap to `designer.TryGet(control, out var docked) \|\| runtime.TryGet(…)` | `AHiddenDockedPanel_GivesUpItsEdge…` |
| M10.5 | margin reset | delete `rules.Add("margin: 0");` | every `PositionedPrefix` test |
| M10.6 | Dropdown lift | delete the `HtmlChildrenWrapper` block | `AMenuStripsDropdowns…` |
| M10.7 | Breakpoint query (plan M8) | `if (breakpoint == 0) return;` → `if (breakpoint < 0) return;` | `ABreakpointOfZero_NeverStacks`; and drop the query (`return;` first line) → `ThePhoneQuery_Uses…` ×4 |
| M10.8 | Phone order is FormReadingOrder's | `AppendStacked`: `FormReadingOrder.Order(positioned, …)` → `positioned` | `ThePhoneOrder_IsFormReadingOrders`, `AContainersChildren_AreOrdered…` |
| M10.9 | JS far edge unclamped | `Core`: in the Bottom branch delete `h -= node.h;` | `TheScriptsResolver_AgreesWithFormDockLayout…` ("strips stack", "overflowing Top then Bottom") |
| M10.10 | JS Walk uses the resolved container size | `Core`: delete the inner `for (var q …)` loop | `TheScriptsResolver…` ("nested in a docked Fill") |
| M10.11 | JS css mirror | `Core`: `vgsDockCss` Bottom → `["top", px(b[1])]` in place of `["bottom", bottom]` | `TheScriptsResolver…` (every case with a Bottom) |
| M10.12 | JS hidden = computed display | `Bootstrap`: `hidden` returns `node.hd` | `HidingADockedPanel_AtRunTime…`, `ShowingADesignHiddenPanel…` |
| M10.13 | Observer sees style changes | `Bootstrap`: `attributeFilter: ["class", "hidden"]` | `HidingADockedPanel_AtRunTime…` (the options assertion) |
| M10.14 | Hidden container never gets display | `AppendStackedRule`: delete the `if (!CatalogDeclarations…)` guard (always add display) | `OnAPhone_AHiddenContainerStaysHidden…` |
| M10.15 | Stretch from the catalog | `AppendStackedRule`: `definition?.StretchesWhenStacked == true` → `false` | `OnAPhone_OnlyAFlaggedRowStretches(TextBox\|ComboBox\|ListBox\|PictureBox)` |
| M10.16 | Anchor check by vocabulary | `CheckAnchors`: `!FormVocabulary.IsPixel(form)` → `form.Target != FormTarget.WinForms` | `ACanvasPagesUnknownAnchorEdge_IsRefused` |
| M10.17 | Data from the resolver's rules | `FormDockScript.Nodes`: `FormDockLayout.OwnSizeOf(control)` → `(0, 0)` | `TheScriptsResolver…` |

⚠ M10.9–M10.13 edit a C# raw string literal: keep the indentation of the surrounding lines exactly, or the literal fails to compile (CS8999) — that is not a kill. A mutant that turns nothing red is a finding: add the missing test or record why the rule is covered elsewhere. Record each red name for the gate commit.

#### Step 21: Gate (10 min + run time)
- [ ] Build.
- [ ] Fast subset, both streams:

```powershell
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter TestCategory!=Integration > `"$sp\t10-fast.txt`" 2>&1"
```

  Compare the sorted FAILURE NAMES with the Task 9 gate's run (and `baseline-fast.txt`). Allowed: exactly the known machine rows (`Emit_Replaces…AnotherHandleHasMapped` ×2, intermittent `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, `SearchSnippets_*` ×2). Any other name is a regression. State the base with every number.
- [ ] Named: F10 into `$sp\t10-named.txt`. Every row passes; the count equals Step 0's F10 count plus the new tests. That proves they RAN.
- [ ] Integration: I10 into `$sp\t10-int.txt`. Compare names with `t10-base-int.txt`. The new `FormDockScriptTests` node rows and `ACanvasPage_BuiltByTheRealCli…` must PASS, not skip. JS rows failing with `ERROR_USER_MAPPED_FILE`: A/B on `e1c3de72` first.
- [ ] Record the gate in the commit below. Do NOT make an `--allow-empty` commit.

#### Step 22: The gate commit (2 min)
The doc edits of Step 17 are already committed, so the gate record goes into a final commit that carries a real change: if Step 20 added a missing test, commit it with the record; otherwise put the mutation red names and the gate numbers (with their base) in the Task 10 report to the plan owner, and `git commit --amend` Step 19's commit only if it is still unpushed and still HEAD.

---

## E. What the executor should expect to trip over
1. **Capture the golden hashes at the BASE (Step 1), before any production edit.** Re-capturing after a red is how a Grid regression gets baselined.
2. **The Grid/Flow body moves verbatim.** `AppendGridOrFlowBody` is the old text; `AppendLiteral` and `CatalogDeclarations` are extractions whose output must be byte-identical (the golden proves it).
3. **`LastIndexOf("</div>")`, never `IndexOf`,** in every "inside the form area" assertion: a Panel is a `<div>`.
4. **Raw string literals take the source file's line endings** (CRLF on a Windows checkout): `Core`/`Bootstrap` call `ReplaceLineEndings("\n")`. The harness strings in the tests may be CRLF; node does not care.
5. **The node rows must RUN.** A skip on this machine means node was not found (`v22.19.0` is on PATH at `e1c3de72`). Check "Skipped: 0" on a named re-run.
6. **Two resolves, one resolver (B3).** Do not "simplify" to one Runtime resolve: `ClientSizeOf` throws for a hidden container, and a hidden docked control has no Runtime rect.
7. **Never a Kind switch.** Stretch is `StretchesWhenStacked`; the dropdown lift is the row's `HtmlChildrenWrapper`; strips are `FormPlace.Docked`.
8. **The C#/JS pair is changed in ONE commit.** If you touch `FormDockLayout.ResolveSiblings`/`Walk` or `FormAnchorCss.Docked` for any reason, touch `FormDockScript.Core` too and re-run the lock-step test.
9. **`ClientSizeOf` vs stored size.** A docked container's children are anchored against its RESOLVED size (C2); the stored 10×10 of a `Dock=Fill` Panel is stale by design.
10. **The Edge harness (Task 13) is where the real browser runs this.** The node glue test proves the script's logic against a stub DOM; carry the toggle and dropdown checks into Task 13's pre-flight (Carried).
