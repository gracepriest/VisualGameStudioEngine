# Web forms laid out in pixels — piece 1 of "one form, either target" (design)

Status: approved section by section in brainstorming 2026-09-27; revised after spec review (grounding fixes).
Branch: `feat/web-pixel-layout`, based on `feat/property-grid` @ `6af0bea1` (both change the web page generator;
basing here avoids a large merge later).

## 0. The programme this belongs to (owner decisions — do not relitigate)

The owner wants a form designed ONCE, WinForms-style, that builds for either target:

- **P-D1 — design in pixels.** Every form is designed like a WinForms form: drag anywhere, resize with handles,
  Location/Size in the property grid.
- **P-D2 — the web page is exact on desktop, follows Anchor/Dock on resize, and stacks on phones.** Mostly desktop
  users; phones are a nice-to-have.
- **P-D3 — a portable control library.** WinForms' API recreated on the web (`Button`, `TextBox`, `.Text`,
  `.Enabled`, `Click`, `(sender As Object, e As EventArgs)`) so one form's code-behind compiles for both targets.
- **P-D4 — a Desktop | Web switch on the toolbar.** One project builds either way; F5 runs the chosen target.
- **P-D5 — retire `.blwebform`** (Grid/Flow) with a convert-on-open step.
- **P-D6 — desktop-only controls stay in the toolbox with a "desktop" badge**; a web build containing one stops
  with a clear error naming it; web versions arrive over time.

Four pieces, built in order, each with its own spec → plan → gate:
1. **This spec** — pixel layout for web forms (desktop-exact, Anchor/Dock resize, phone stacking), delivered through
   the web form's existing-but-unimplemented pixel layout so web projects get it immediately.
2. The portable control library (P-D3).
3. The Desktop | Web toolbar switch and the one-form build (P-D4, P-D6).
4. Retiring `.blwebform` (P-D5): a pixel web form already carries pixel geometry, so its conversion is nearly a rename;
   Grid/Flow forms convert cell → pixel from where the canvas draws them.

The property grid (`docs/superpowers/specs/2026-09-25-property-grid-vs-parity-design.md`) continues in parallel.

## 1. Goal and decisions (piece 1)

A web form can use pixel layout — the default for new web forms. It stores what a WinForms form stores and is designed
like one; its page is pixel-exact at the design size, follows each control's Anchor/Dock when the browser resizes,
scrolls rather than squashes below the design size, and stacks into one column below a phone breakpoint.

Decisions (owner, 2026-09-27, as revised by review):
- **D1 — implement the EXISTING `FormLayoutKind.Canvas`** ("the absolute-pixel escape hatch", `FormGeometry.cs:93-97`,
  already accepted by the reader as `Kind="Canvas"`, `FormDocumentReader.cs:325`, with a half-built emitter arm,
  `FormAssetEmitter.cs:422-424`, and a canvas stub, `FormCanvasTransform.cs:828-830`). No second enum member. The XML
  spelling stays `Kind="Canvas"` (no migration); the IDE may label it "Pixel". It is the default for NEW web forms.
  Existing Grid/Flow forms are unchanged (piece 4 converts them).
- **D2 — a Canvas web form stores WinForms geometry**: the design size (root `Width`/`Height`, the ClientSize) and, per
  positioned control, `X`/`Y`/`Width`/`Height`/`Anchor`/`Dock` read as `PixelGeometry` — the same attributes as a `.blform`.
- **D3 — the canvas, placement, toolbox and property grid behave as on a WinForms form.** ⚠ This is NOT free: today
  every web document is routed down the Grid path (§2.4 lists the sites). Each becomes (target, layout)-aware.
- **D4 — handler code is unchanged in piece 1** (`e As DomEvent`, `getElementById`); piece 2 changes it.
- **D5 — desktop-exact / anchor-resize / phone-stack** (§3–§5), with the REAL WinForms window as the reference for
  resize behaviour (§7).

## 2. The document and the code that decides by target

### 2.1 Vocabulary is decided by (target, layout)
One rule, one helper — signature `FormVocabulary.IsPixel(FormTarget target, FormLayoutKind? layout)` (true for
WinForms, or web with `Canvas`; it takes values, not a document, because the reader calls it before any model exists) —
used by every site that today decides by target alone:
- `FormDocumentReader`: root `Width`/`Height` (`:113`, gated on WinForms today), known-root-attribute filtering
  (`:148-154`), `ReadGeometry` (`:643-677`), `IsStructural` use (`:506`).
- `FormDocumentWriter`: `Create` and `Apply` root size vs `<Layout>` (`:92-101`, `:186-193`).
- `FormControlCatalog.IsStructural(name, target)` (`:2031-2041`) → takes the layout too (else a Canvas control's `X`/
  `Width` also land in `UnknownAttributes`); its callers: reader `:506`, clipboard `FormDocument.cs:478`, retarget
  `FormRetarget.cs:422`.
- `FormRootValues.RowForAttribute(attribute, target)` (`:117`) → takes the layout.
- `FormClipboard`'s mirrored `ReadGeometry` (`FormDocument.cs:539-578`, keyed on `Target` at `:308`) → the same rule.
  ⛔ A paste between documents of DIFFERENT layout (Canvas ↔ Grid) is REFUSED with a message naming both layouts
  (the clipboard is reachable: `CodeEditorDocumentViewModel.cs:573`, `:626`).
- A web document with no `<Layout>` keeps Grid (`FormLayout.Kind` defaults to Grid) — unchanged.

### 2.2 The reader must know the layout first
The reader reads root attributes (`:113`, `:148-154`) and controls BEFORE it reaches `<Layout>` (`:162`, inside the
element loop), and `<Layout>` may appear after `<Controls>`. It PRE-SCANS the root's `<Layout>` child before reading
root attributes and controls.

### 2.3 Root rows are layout-aware, through one predicate
`FormControlCatalog.FormRoot` rows carry per-target `Targets` (`:1953-1969`). Add a per-row layout applicability
evaluated together with the target through ONE predicate (e.g. `FormRootValues.Applies(row, document)`) that the reader,
writer, region writer, grid (`FormPropertyGridViewModel.cs:479`, `:489`) and retarget all call:
- `ClientSize` → WinForms, or web Canvas. ⚠ `RegionWriter` (`:637`) must keep emitting `Me.ClientSize` for WinForms only
  — the web code-behind emits no geometry.
- `Cols`/`Rows` → web Grid only; `Gap` → web Grid AND Flow (the emitter writes `gap` for both, `FormAssetEmitter.cs:427-430`).
  (No `Dir` row exists in `FormRoot` today; none is added.)
- **New `MobileBreakpoint`** → web Canvas only. Int, default `600`, `0` = never stack, negative or unparseable →
  Degraded (frozen, preserved, explained, via the existing root-tier path). **Stored on the `<Layout>` element**
  (`<Layout Kind="Canvas" MobileBreakpoint="600"/>`), like Cols/Rows/Gap — `FormRootValues.StorageAttributes` answers
  that, and round-trip placement follows. Needs: `FormLayout` field + `Clone` (`FormGeometry.cs:124-127`), `ReadLayout`
  (`FormDocumentReader.cs:315-331`), `LayoutElement`/`ApplyLayout` (`FormDocumentWriter.cs:555-563`, `:260-285`).
  ⛔ Mirror the ClientSize preservation rule: an unparseable value keeps its RAW text and the writer NEVER removes the
  attribute because the model holds null (today `ApplyLayout`'s `SetAttributeIfChanged` removes on null — that would
  delete `MobileBreakpoint="abc"` on the first save). Needs a WinForms-oracle exemption (web-only row) in the parity test
  and a cross-or-name entry in the retarget sweep.

### 2.4 Sites that route every web document down the Grid path (all become layout-aware)
- `FormCanvasTransform.Layout` → `WebLayout` for every web document (`:377-379`); `WebLayout` places controls only for
  Grid (`:832-863`). A Canvas web form uses the pixel path.
- `FormPlacement.Place` → `PlaceOnWeb` for every web drop (`:101-104`), which refuses non-Grid (`:167-173`). A Canvas
  web form places like WinForms.
- `FormCanvasControl`: form resize grips (`:883`, drawing `:1566`), window chrome + alignment grid (`:1779`), cell guides
  (`:1816`). **A Canvas web form gets the alignment grid and the form resize grips, but NOT the WinForms title bar/window
  frame** (it is a page), and no cell guides.
- Unaffected (verified): `SurfaceSize` (`FormCanvasTransform.cs:205-211`, reads `Width`/`Height` for any target — the ONE
  answer to "how big is the form"); the grid's intrinsic rows switch on geometry TYPE (`FormPropertyGridViewModel.cs:353-392`);
  the toolbox (`CodeEditorDocumentViewModel.cs:217`); `FormHandlers` (`:157`, `:179`); `FormGeometryEdit.MoveToCell`.

### 2.5 Scaffold and round trip
- `FormScaffolder` (`:101-117`) creates new web forms with `<Layout Kind="Canvas" MobileBreakpoint="600"/>` and the
  WinForms scaffold's design size.
- Existing tests that ASSUME a new web form is Grid (`auto,1fr` in `FormDesignerLayoutRealViewTests`,
  `FormCodeBehindWriteTests`, `FormHandlerGestureTests`, drop-into-cell acceptance paths) are made to pin Grid
  explicitly — not rewritten to Canvas.
- A Canvas web form reads and writes byte-for-byte; Grid/Flow forms are byte-for-byte unchanged.
- Foreign vocabulary on a control (a cell attribute on a Canvas form) goes to `UnknownAttributes` and round-trips, as
  today — no new diagnostic.
- `PixelGeometry`'s doc comments ("`.blform` geometry", `Target` returning WinForms, `FormGeometry.cs:39`) and
  `GridGeometry`'s "pixels are not modelled" note are corrected.

## 3. The page at the design size

`FormAssetEmitter` completes its Canvas arm (`:399-425`, `:147`, `:459`); Grid/Flow paths unchanged.
- **Coordinate space = the WinForms client area, strips INCLUDED.** In WinForms a docked strip sits inside the client
  area (a control at Y=30 is 6px below a 24px menu), and the canvas draws strips over the surface
  (`FormCanvasTransform.cs:375-379`). So on a Canvas page the strips are rendered INSIDE the form area as absolutely
  positioned bands, NOT as page chrome outside it (a real change to `FormAssetEmitter.Html`, `:116-176`, whose comment
  then describes Grid/Flow only) — every control's `X`/`Y` then lands exactly where it was designed. (Grid/Flow keep
  strips as chrome, unchanged.)
- **Band height = the strip row's `DefaultHeight`** (MenuStrip 24, ToolStrip 25, StatusStrip 22 — the heights the canvas
  already draws, `FormControlCatalog.cs:1808/1833/1854`), applied with `box-sizing: border-box`; the strip rows' CSS
  (`:1813`, `:1838`, `:1859`) must not override it on a Canvas page. Without this the page's content-sized strips drift
  1–3px from the canvas and every control under them follows.
- **ONE shared layout function in BasicLang** — e.g. `FormDockLayout.Resolve(document)` — owns where every DOCKED thing
  sits: strips (their `Dock` property) AND docked controls (`PixelGeometry.Dock`), resolved in ONE document-ordered
  sequence exactly as WinForms docks them (§4). The canvas's `FormCanvasTransform.Bands` (`:398-420`, which today "uniquely
  owns where each band SITS") and the page emitter BOTH call it — never two copies of the stacking algebra (the
  `Tracks`/`ParseTracks` mirrored-pair trap). The canvas's `BoundsOf` (`:344-358`, which ignores Dock today) also uses it,
  so a docked control is DRAWN where it will run.
- Every positioned control is absolutely positioned at `X`/`Y` with `Width`/`Height`.
- A container (`IsContainer`) is its own positioned box; its children use coordinates relative to it.
- **Stacking order**: the model's document order is WinForms' back-to-front ("last in the list is in front",
  `FormDocument.cs:190-192`), and absolutely-positioned DOM siblings paint later-on-top — so emitting in document order
  is correct with no reordering. (The WinForms-side inversion fix lives in `RegionWriter.AppendSiblings`, `:694-760`; the
  page needs none.)
- `<Literal>` markup, if any, flows at the form area's top-left under the positioned controls (documented; not reordered).
- Styling from the catalog CSS walk (`FormCss`), unchanged; tray components emit no markup, unchanged.
- A non-positive `Width`/`Height` is read as a `.blform` reads it (no new strictness); the emitter writes no pixel size
  for it (it counts as 0 in the far-edge inset). ⚠ Exception (Task 10 review): on an axis anchored to BOTH edges the
  size is always written as `calc(100% − (near+far)px)` (§4), which for a non-positive extent is 0 at the design size
  and grows with the container.
- The Canvas form area is its own block formatting context (`display: flow-root`; Task 10 review, measured in
  Chromium): otherwise a `<Literal>` whose first element has a top margin collapses it through the form area and moves
  every control down.

## 4. Resizing: Anchor and Dock → CSS

**The form area fills the browser** (100% width; height fills the viewport) with a **minimum size equal to the design
size**: larger → controls follow anchors; smaller but above the breakpoint → the form keeps its design size and the page
scrolls; never squashed.

Per control, with `W`/`H` the container's design width/height (Anchor default `Top, Left`):

| Anchor | CSS |
|---|---|
| Top, Left (default) | `left:X; top:Y; width; height` |
| Right (not Left) | `right:(W−X−width); width` |
| Left + Right | `left:X; right:(W−X−width); width:calc(100% − (W−width)px)`; width follows |
| Bottom (not Top) | `bottom:(H−Y−height); height` |
| Top + Bottom | `top:Y; bottom:(H−Y−height); height:calc(100% − (H−height)px)`; height follows |
| neither on an axis | **centred relative to its original offset, as WinForms does**: new left = X + (W′−W)/2 → `left: calc(50% + (X − W/2)px)`; same for top with H |

⛔ **A stretched axis WRITES its size** (Task 10 review, measured in Chromium 152): an `<img>` with a loaded `src` under
`position:absolute` with left+right (or top+bottom) and no width KEEPS ITS INTRINSIC SIZE, while inputs, selects,
buttons, textareas and divs stretch — and an `<img>` without a `src` stretches too, which hides the defect from any
test with no image. So both insets are followed by `size: calc(100% − (near+far)px)`, the same box for every element
under `box-sizing: border-box`; a negative sum (a control larger than its container) is written `calc(100% + Npx)`.
The same rule applies to every docked axis that spans the container (Top/Bottom → width, Left/Right → height,
Fill → both), in `FormAnchorCss` and its JavaScript mirror alike.

**Dock** is resolved ONCE at the design size, by the shared `FormDockLayout` (§3), into edges with fixed insets. It takes
strips (their `Dock` property) and docked controls (`PixelGeometry.Dock`: Top/Bottom/Left/Right/Fill) as ONE sequence.
**Docking order in model terms: WinForms docks back-most first, which is the model's DOCUMENT order (first in the list
docks first)** — stated in these terms on purpose, given this repo's z-order inversion history. So a `Dock=Top` Panel that
precedes the MenuStrip in the document takes the top edge and the menu sits below it, on the canvas, on the page and in
WinForms alike. Example (strips first in the document): a Fill between a 24px menu and a 22px status strip →
`top:24; left:0; right:0; bottom:22` (plus the written `width`/`height` of the rule above). The resolver is a pure
function (§7).

## 5. Phones: stacking below the breakpoint

Below `MobileBreakpoint` (default 600px; 0 disables), one media query switches the form area to a single column:
- The form area becomes a flex column; **every control switches to `position: static`**, containers get `height: auto`
  (otherwise `order` does nothing and heights clip).
- **Reading order** (pure function, computed at build time): controls grouped into ROWS by vertical overlap; rows top to
  bottom; left to right within a row; a container stacks as one block with its children stacked inside it the same way.
  Applied with CSS `order` inside the media query; HTML order unchanged.
  **Owner decision 2026-09-27: a tall sibling must not turn the controls beside it into columns.** Within a row, members
  are removed tallest first (equal heights in document order) until the rest falls into two or more rows; a removed
  member whose span wholly contains at least one of those rows (a logo or list beside a column of fields) is SPANNING,
  and one that does not (a link in a staggered two-column chain) is not. Spanning members are placed by X, and the other
  members of that row are ordered by the same rule again, in the gaps between them by X, so they form their own rows
  (label/box pairs stay together; a tall control on the right comes after them). Exact rule: plan scope call S8 and
  `FormReadingOrder`'s summary.
- Inputs stretch to full width (TextBox, ComboBox, ListBox, multi-line text, PictureBox — from a per-row CATALOG flag,
  never a `control.Kind` switch); small controls keep their designed size, left-aligned.
- Top strips first, bottom strips last; anchors/Dock ignored; hidden controls stay hidden; a small fixed gap.
- Known limit (accepted): no per-control phone override yet.

## 6. Retarget (reachable today)

"Retarget Form…" and `design --retarget` accept any web source, and `ToPixels` places controls by cell/flow
(`FormRetarget.cs:624-739`), discarding a Canvas form's exact positions; root `Width`/`Height` handling in `ConvertRoot`
is target-gated. In piece 1:
- **Canvas web → WinForms copies geometry and the design size exactly** (lossless). It emits NO layout warning (today's
  "layout dropped" BL8025 text, `FormRetarget.cs:635-641`, does not apply) — the only finding is BL8024 for
  `MobileBreakpoint`, which is web-only and dropped-and-named. This retarget is also what produces the WinForms program
  for the §7 reference test.
- WinForms → web keeps producing Grid (unchanged) — piece 4 replaces retarget.
- The retarget catalog sweep and the root retarget sweep cover the new row.

## 7. Testing

- **The WinForms window is the reference for resize behaviour.** For the same pixel document, a test builds and RUNS the
  WinForms form and compares it with Edge (±1px) — anchors, docking order and the no-anchor centring are then proven
  against WinForms itself, not our reading of it. It extends the acceptance driver (`FormDesignerAcceptanceTests.cs:408-456`)
  with what that driver lacks:
  1. **The WinForms program** comes from the §6 Canvas → WinForms retarget (`FormRetarget.ConvertToPair`) — so §6 lands
     first.
  2. **A recursive walk in FORM-CLIENT coordinates** on both sides: WinForms `form.PointToClient(c.PointToScreen(Point.Empty))`
     + `Size` for every control at every depth; Edge rect minus the form area's rect.
  3. **A resize step**: after `Show()`, set `ClientSize` to W′×H′, `PerformLayout()`, `Application.DoEvents()`, then read.
     Keep W′ inside the screen's working area (Windows clamps an oversized window).
  4. **Pinned DPI**: `Application.SetHighDpiMode(HighDpiMode.DpiUnaware)` (or pinned in the test csproj) to match Edge's
     `--force-device-scale-factor=1` on a scaled display.
  5. **Content-sized controls pinned**: WinForms strips are AutoSize (content-sized); the driver sets each strip
     `AutoSize = false` with the row's `DefaultHeight`, and fixtures avoid other AutoSize rows. (Shipping WinForms strips
     stay AutoSize, so a real WinForms menu may be 1–3px off the design — accepted and documented.)
  6. **Gates**: `[Category("Integration")]` + the Windows check the WinForms walkthrough already uses (`:366-369`).
  Docked controls are compared ONLY against this reference; undocked controls also against their stored geometry (below).
- **Edge headless layout check** (`msedge.exe`, present at `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`):
  - the generated site is SERVED from a loopback `HttpListener` on `127.0.0.1` (ephemeral port) — NOT opened as
    `file://`, whose opaque per-file origins stop a harness page reading its iframes and stop the generated module script
    loading; served, both work;
  - one HARNESS page iframes the form page at each width (design size, wider, narrower, phone) — media queries evaluate
    against the iframe; one launch, deterministic; the harness reads each iframe's rects (same origin) and writes them
    into its own DOM for `--dump-dom`;
  - a small measuring script (added to a served copy of the page) records every control's `getBoundingClientRect()`;
  - flags: `--headless=new --user-data-dir=<fresh temp dir>` (mandatory — without it a launch can hand off to the
    owner's running Edge and exit) `--hide-scrollbars --force-device-scale-factor=1 --virtual-time-budget=<ms>
    --dump-dom`;
  - kill with `Process.Kill(entireProcessTree: true)` on the test's own PID (Edge spawns children); never by name;
  - where Edge is absent (Linux/cloud) the tests SKIP with a reason (the repo's rule; never a pass-by-absence).
  Assertions: at the design size every UNDOCKED control within 1px of its `X`/`Y`/`Width`/`Height` (form-client
  coordinates, strips inside per §3) and every strip at its `DefaultHeight`; docked controls and every control at the
  wider size match the WinForms reference; narrower-than-design scrolls (form keeps its size); below the breakpoint the
  order is the reading order, inputs span the width, strips first/last.
- **Pure functions, table-tested:** reading-order grouping (overlapping rows, a label slightly higher than its box,
  nested containers, ties); `FormDockLayout` (each dock value, strips + docked controls in one document-ordered
  sequence — incl. a `Dock=Top` Panel before a MenuStrip — band heights, nesting); Anchor → CSS (every combination,
  including the centring formula). The canvas's `Bands` and `BoundsOf` are tested to agree with `FormDockLayout` (one
  answer, two consumers).
- **Documents:** Canvas round trip byte-for-byte; Grid/Flow unchanged; the `<Layout>` pre-scan (Layout after
  Controls); `IsStructural`/`RowForAttribute`/clipboard by (target, layout); a cross-layout paste refused; the root rows'
  layout applicability via the one predicate (catalog-driven sweep); `MobileBreakpoint` storage + Degraded negative.
- **Canvas (headless, real IDE view):** a Canvas web form draws its controls, accepts drops, drags, resizes, edits
  Location/Size and Anchor/Dock, shows the alignment grid and form grips but no title bar; the grid shows ClientSize and
  MobileBreakpoint on a Canvas web form and Cols/Rows/Gap only on Grid.
- **Retarget:** Canvas → WinForms lossless both for geometry and design size; MobileBreakpoint named.
- **End to end:** a Canvas twin of the web acceptance tests — a designer-built Canvas form, built by the real CLI, its
  page RUN under node (handler fires, no LOAD ERROR) AND laid out in Edge per the checks above.
- **Mutation checks** on the load-bearing rules: vocabulary by (target, layout); the layout predicate; the `<Layout>`
  pre-scan; anchor edges and the centring formula; docking order; strips inside the form area; reading-order grouping;
  the breakpoint query; cross-layout paste refusal.

## 7a. Notes for the plan (from the final spec review)

- **`BoundsOf` through `FormDockLayout` is a WinForms canvas change too.** `BoundsOf(control, containerOrigin)`
  (`FormCanvasTransform.cs:344-358`) is per-control; a docked control's rect depends on its siblings, so it takes the
  resolved layout, and its callers (hit testing, handles, drag in `FormCanvasControl`) change with it. Docked controls on
  `.blform` documents will then MOVE on the canvas to where they run — existing `FormCanvasTransformTests` /
  `FormCanvasRenderTests` expectations for docked fixtures change INTENTIONALLY; record each as intended.
  **Decision: a docked control cannot be dragged or resized on the canvas** (Visual Studio's behaviour — its position
  comes from docking; a drag would write X/Y the runtime ignores): no move/resize handles for it, a drag starting on it
  selects only.
- **Loopback server:** on Windows a non-admin `HttpListener` can register `http://localhost:{port}/` but a literal
  `http://127.0.0.1:{port}/` prefix can fail (access denied without a URL ACL). Register `localhost` and navigate Edge to
  it (or use a minimal `TcpListener` server); `HttpListener` has no port-0 bind, so probe for a free port and retry on
  conflict.
- **The root-row predicate takes values:** `FormRootValues.Applies(row, target, layout)` (the reader has no document
  yet — same reason as §2.1), with a convenience overload for a document; `RowForAttribute` calls it, never a copy.
- Phone mode: top strips first, bottom strips last; docked controls, now `position: static`, join the ordinary
  reading-order grouping.
- WinForms reference driver: pin strip heights (§7 item 5) before the resize step — anchor distances are captured in
  `InitializeComponent` and do not depend on the strips.

## 8. Out of scope (piece 1)

The portable control library (piece 2) and any handler-code change; the Desktop | Web toolbar switch (piece 3);
converting existing Grid/Flow forms and retiring `.blwebform` (piece 4); WinForms → Canvas retarget (piece 4 replaces
retarget); per-control phone overrides; percentage/relative sizing; a breakpoint preview in the canvas.
