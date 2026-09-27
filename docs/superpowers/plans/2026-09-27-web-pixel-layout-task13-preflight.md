# Task 13 pre-flight: the Edge headless harness

Plan: `docs/superpowers/plans/2026-09-27-web-pixel-layout.md`, "## Task 13". Spec: `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §3, §4, §5, §7, §7a.
This check was made against `feat/web-pixel-layout` @ **`ff415ec5`** (Tasks 0–12 landed, incl. the Task 12 review rounds). It was read-only apart from one throw-away Edge probe in the scratchpad (a `file://` page, fresh profile, deleted); nothing in the repo changed except this file.

Follow this document **instead of** the plan's Task 13 section. Where the two disagree, this document wins. Everything else in the plan (How to build/test, Commits, Traps) still applies.

**OWNER decisions needed: none up front.** ⛔ One conditional stop: if Edge disagrees with the model or the WinForms reference by more than 1px on a case that is NOT a recorded gap (below), the numbers go to the coordinator BEFORE any production code changes.

---

## A. Anchor verification (claim → today's file:line at ff415ec5 → verdict)

| # | Plan / task claim | Today | Verdict |
|---|---|---|---|
| 1 | `msedge.exe` at `Program Files (x86)`, then `Program Files` | x86 path exists, 154.0.4258.37; `Program Files` absent | holds |
| 2 | Loopback `HttpListener`, free port probed, retry on `HttpListenerException` | **Already shipped:** `VisualGameStudio.ProjectSystem/Services/WebPreviewServer.cs:99-131` (TcpListener port 0 → HttpListener, 5 attempts, `127.0.0.1` then `localhost`, never `+`/`*`), MIME map `:241-269` (`.js` text/javascript for the module), `no-store` | see C1 — reuse, not a second server |
| 3 | Task 12 output: `WinFormsReferenceHarness.Measure` / `Parse`, `LayoutComparison.Differences`, `PixelLayoutModel.Rects` | `WinFormsReferenceHarness.cs:135`, `:210`; `PixelLayoutModel.cs:114`, `:25`, `AnchorAxis` `:93`; `LayoutBox`/`LayoutSnapshot` `WinFormsReferenceHarness.cs:16-20` | holds |
| 4 | `PixelLayoutFixtures` holds the documents | `PixelLayoutFixtures.cs` — 15 builders, **all 400×300 with no `MobileBreakpoint`** | see B1 |
| 5 | Canvas page markup | `FormAssetEmitter.Html` `:101`; `AppendCanvasBody` `:196` (Literal wrapped in `.vgs-literal` `:209-214`, dock script `:220`) | holds |
| 6 | Canvas CSS | `CanvasCss` `:604`: `display: flow-root` `:620`, `min-width`/`min-height` `:622/624`, `[hidden]` rule `:630`; phone query `AppendStackedQuery` `:645-664` (`@media (width < Bpx)`); dropdown lift `:797-800`; `CanvasPlacement` `:810-850` | holds |
| 7 | The stretched-axis `calc` size (Task 10 I-1) | `FormAnchorCss.Stretched` `:141`, used by `Axis` `:170` and `Docked` `:121-122`; JS mirror `vgsDockCss` `FormDockScript.cs:62-73` | holds |
| 8 | The reflow script | `FormDockScript.Bootstrap` `:78-107`: hidden = `getComputedStyle(el).display === "none"` `:85`; observer filter `:106`; walks at the DESIGN size (`root.w/h`) | holds |
| 9 | MenuStrip dropdown | opens on `.vgs-MenuStrip li:hover>ul` `FormControlCatalog.cs:1836`; lifted by `#id ul { z-index: 1; }` | see B4 |
| 10 | PictureBox with a loaded image | web: `Image` → `src` `FormAssetEmitter.cs:357-361`; WinForms: `Image.FromFile` `FormControlCatalog.cs:1434-1435`, relative to the driver's working directory (`reference-app`, `CliTestHarness.RunProcess` `workingDir`) | see B5 |
| 11 | Real CLI JS build | `FormBuildEmissionTests.WriteProject` `:38-55`, `Build` `:87-90`, output `bin/Debug/net8.0` `:92`; the CLI is the one deployed next to the tests (`CliTestHarness.CliPath` `:32`), so it carries every production change after a test build | holds |
| 12 | Writing a `.blwebform` from a fixture | `FormDocumentWriter.Create(FormDocument)` `:80` | holds |
| 13 | Phone order oracle | `FormReadingOrder.Order` `:47`; the emitter's own ordering `AppendStacked` `:675-710` (top strips by Y, `Order` over Designer rects, bottom strips) | holds |
| 14 | Skip rule | `TestSkip.IgnoreEvenInsideMultiple` `TestSkip.cs:24` | holds |
| 15 | `--virtual-time-budget` with `--dump-dom` in new headless | PROBED (Edge 154, `--headless=new`, fresh `--user-data-dir`): 200 chained `setTimeout`s after `load` finished inside the dump; 1.6 s; `devicePixelRatio` 1; no process left with that profile | holds |

---

## B. Blockers (following the plan text literally breaks something)

### B1: Every shared fixture is 400 wide, BELOW the default 600px breakpoint
- **Evidence.** `FormGeometry.cs:130` `DefaultMobileBreakpoint = 600`; every `PixelLayoutFixtures` form is 400×300 with no `MobileBreakpoint`. Measured at "design W" the page would be the PHONE column, and every desktop comparison would be meaningless.
- **Correction.** The web copy of each shared fixture gets `MobileBreakpoint="200"` (the WinForms side gets the unmodified document: the breakpoint is web-only, BL8024). At 200 the desktop sizes (400, 600, 601, 341 wide) are all desktop, and the reflow script's live rules still go through the `@media (width >= 200px)` wrapper (the shipping shape). Phone stacking gets its OWN fixture at a realistic 640×480 with the default 600 breakpoint, measured at 400 wide.

### B2: "Narrower than design" is not comparable with WinForms' shrink
- The plan lists W−100 as a comparison width. WinForms squashes (Task 12's `shrink` 341×251 shrinks stretched controls); the page never squashes (spec §4: min size = design, the page scrolls). **Correction.** Narrower cases assert: form area exactly W×H, document scroll size ≥ W×H, every control at its DESIGN rect (the model at the design size). No WinForms comparison there, by design.

### B3: Subtracting the form area's origin HIDES the `flow-root` defect
- **Evidence.** Task 10 N-1: a `<Literal>` starting with `<p>` collapsed its margin through `.vgs-form` and moved the WHOLE form area down 16px. Every control rect is measured relative to the form area's client origin (Task 12 B8), so the controls look unchanged while the form area itself moved.
- **Correction.** Every desktop case also asserts the form area's border box is at viewport (0, 0) and its client origin is (0, 0). That is what turns red when `display: flow-root` is reverted.

### B4: A `:hover` dropdown cannot be opened with `--dump-dom`
- No input can be sent (no DevTools session). **Correction.** The measuring script opens it by writing the SAME declaration the hover rule applies (`display: flex`) inline on `#<item> > ul`, then asks `document.elementFromPoint` at a point inside the dropdown that lies over a later control. The box, its stacking and its lift are the real ones; only the trigger is simulated. The hit must be inside the MenuStrip (id of the nearest ancestor with an id is an item or the strip).

### B5: The PictureBox fixture needs its image on BOTH sides
- Web: the image file sits beside the pages (served). An `<img>` that FAILED to load stretches like one with no src and hides the bug, so the measuring script reports `complete`/`naturalWidth` and the test requires a loaded image. WinForms: `Image.FromFile("pic.png")` is relative to the driver's working directory, so the same PNG is written into `<workDir>/reference-app/` before `Measure` (it creates that directory idempotently). The PNG is generated in code (1×1 → the intrinsic size is nothing like the box, so the defect cannot hide).

### B6: A `<Literal>` does not exist on WinForms
- **Correction.** The Literal fixture is a TWIN: `LiteralP` (web, with `<p>…</p>`) and `LiteralPlain` (same controls, no literal). Edge's `LiteralP` is compared with the model and the WinForms reference of `LiteralPlain`.

---

## C. Corrections and decisions (not blocking)

- **C1. The server is the shipping `WebPreviewServer`, not a new one.** It already probes a free loopback port with retries and binds only loopback (`127.0.0.1`, falling back to `localhost`), with the module MIME type. A second server in the test assembly would be a mirrored pair of the product's own. ⚠ Deviation from the plan's "localhost, never 127.0.0.1": the product prefers `127.0.0.1` and falls back to `localhost` when the ACL refuses it; both are loopback-only, and Edge navigates to whichever `Url` it returns.
- **C2. One CLI build, one Edge process, one WinForms build per run.** Every web fixture is a `.blwebform` of ONE JavaScript project (distinct names); the harness page loads the cases ONE AT A TIME into one iframe positioned at (0,0): offscreen iframes can be render-throttled, and sequential loads are deterministic. Waits are `setTimeout(0)`, never `requestAnimationFrame` (a throttled frame never fires it).
- **C3. The measuring script is a CLASSIC script** (`vgs-measure.js`) appended before `</body>` of a served COPY (`<Form>.measure.html`), after the page's module tag. It measures on `load` + two task turns, then applies each step and waits a task turn (the MutationObserver's microtask runs first) before each snapshot. It also records `error`/`unhandledrejection` from the page (App.js must not throw), `innerWidth`/`innerHeight` (the iframe's size took), `devicePixelRatio` (must be 1), the UA string (M-6: Edge's version is far above the 104 floor).
- **C4. A control's rect** = `getBoundingClientRect()` (BORDER box) minus the form area's CLIENT origin (`rect.left + clientLeft`, `rect.top + clientTop`) — Task 12 B8. Visible = `getClientRects().length > 0` (a hidden ancestor hides it). A requested id with no element is REFUSED by the parser, never skipped.
- **C5. Toggle mechanisms (owner decision: the real reflow script re-docks).** `HiddenDock` in Edge: `pnlA.style.display = "block"` (a design-hidden control needs a NON-EMPTY display — Task 10 B1) → `showA`; `"none"` → `hideA`; `pnlB.hidden = true` → `hideB`, `false` → `showB`; a class `vgs-harness-hide` (`display:none !important`, injected into `<head>`) on pnlB → `hideB` / `showB` again. The WinForms reference gets steps `showA`, `hideA`, `hideB`, `showB`. `HiddenSiblingAnchor`: `hid.style.display = "block"` → `showHid`.
- **C6. M-5 (the reflow script's cost), measured not decided:** the measuring script counts `getComputedStyle` calls (the reflow script calls it once per docked node per walk) around N colour writes to an UNDOCKED control. Recorded as walks-per-write; the filter decision stays with the coordinator.
- **C7. Recorded gaps become literals** (as Task 12): the GroupBox `<fieldset>` inset, the bordered Panel (no CSS border on the web → web children at the model, WinForms 1–2px in), the centring half pixel (Edge keeps x.5, WinForms floors), and the CheckBox caption (a bare `<input>`: no text box exists for its `Text`). A change in any of them turns its test red.
- **C8. Zero/negative sizes are excluded** (Task 10 C4: content-sized on the page, invisible on WinForms).

## D. Fixtures and cases

Web documents = the Task 12 builders (+ `MobileBreakpoint=200`) and four new builders in `PixelLayoutFixtures`:
- `Picture` 400×300: `picLR` PictureBox `Image="pic.png"` Anchor `Left,Right`; `picTB` Anchor `Top,Bottom`; a Panel `frame` holding `picFill` Dock=Fill.
- `MenuOver` 400×300: MenuStrip `menu` with `mnuFile` holding two items; a Panel `under` at (4, 26) 200×80 AFTER the menu.
- `LiteralP` / `LiteralPlain` 400×300: a MenuStrip, a Top,Left Panel, a Bottom,Right Panel, a centred Panel; `LiteralP` adds `<p>Use your work account.</p>`.
- `Phone` 640×480, breakpoint 600: MenuStrip; label/TextBox pairs (a label 2px above its box); a tall ListBox beside them; a hidden Button; a CheckBox with a caption; a Dock=Bottom Panel; a StatusStrip; a Literal.

Cases (label = WinForms snapshot compared, tolerance 1 unless recorded):

| Form | Edge viewport(s) | Compared with |
|---|---|---|
| SelfTest | 400×300, 600×300 | model + WinForms `design`/`wide` |
| Anchors | 400×300, 601×401; 341×251 (narrow) | model + WinForms `design`/`grow`; narrow = scroll-not-squash vs model design; the centred half pixel recorded |
| DockStrips | 400×300, 600×400 | model + WinForms |
| TopBeforeMenu, OverflowV, OverflowH, HiddenBox, StripsPinned | 400×300 | model + WinForms `design` (strips at 24/25/22) |
| HiddenDock | 400×300 + toggles (C5) | WinForms `design`/`showA`/`hideA`/`hideB`/`showB` |
| Bordered | 400×300 | RECORDED literals (C7) |
| DockedBox, DockedAnchor, NestedDock | 400×300, 500×360 | model + WinForms `design`/`grow` |
| HiddenSiblingAnchor | 400×300 + `showHid` | model + WinForms |
| Picture | 400×300, 600×400 | model + WinForms; every image loaded |
| MenuOver | 400×300 + open | model + WinForms positions; `elementFromPoint` inside the dropdown |
| LiteralP | 400×300 | model/WinForms of `LiteralPlain`; form area at (0,0) |
| Phone | 640×480 (desktop), 400×900 (phone) | desktop: model (+ WinForms); phone: order, stretch, strips first/last, hidden, Literal last |

## E. TDD steps
1. **Parser RED** (`EdgeLayoutParsingTests`, fast): `EdgeLayoutHarness.Parse(dump, cases)` over synthetic dumps — refuses no `#out`, an unfinished run, a missing case, an id with no rect, a viewport other than asked, a DPR ≠ 1, and reports page errors; parses a good dump into snapshots in FORM-CLIENT px.
2. **Harness GREEN**: `EdgeLayoutHarness.cs` (find Edge, build the site through the real CLI, write measure copies + `vgs-measure.js` + `harness.html`, serve with `WebPreviewServer`, run Edge, parse, clean up), fixtures.
3. **Self-test RED→GREEN** (`PixelPageLayoutTests`, Integration, Windows + Edge): SelfTest at 400 and 600 against the model.
4. **The carried cases** per D, each first against the model; a non-recorded disagreement = STOP.
5. **Records** in this file's execution notes and the commit message.

## F. Mutation list (apply, rebuild, run, restore)
1. Measure before `load` (at script run) → Picture "image loaded" red.
2. Snapshot straight after a toggle, no task turn → HiddenDock `showA` red.
3. `Differences` tolerance widened to +5 in the Edge comparison helper → the parser/tolerance unit test red.
4. Parser skips an id with no rect → parsing test red.
5. Harness never applies the iframe width → viewport refusal red.
6. `--force-device-scale-factor=2` (the flag's value lost) → DPR refusal red. (Dropping the flag is equivalent on a 100% display — recorded.)
7. **Task 10 regression, img:** `FormAnchorCss.Stretched` sizes not written on stretched axes (and the JS mirror) → Picture red.
8. **Task 10 regression, flow-root:** drop `display: flow-root` → LiteralP red (B3).
9. Dropdown lift removed → MenuOver hit-test red.

## G. Gate
Fast subset vs `ff415ec5` (8308 / 8302 / 5 / 1; the five known names) + `EdgeLayoutParsingTests`; Integration `FullyQualifiedName~PixelLayout` (both harnesses; must RUN here, 0 skipped), `FormBuildEmissionTests`, `FormDockScriptTests`.
