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
One rule, one helper (e.g. `FormVocabulary.IsPixel(document)` = WinForms, or web with `Layout.Kind == Canvas`), used by
every site that today decides by target alone:
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
- `Cols`/`Rows`/`Gap` → web Grid only; flow's `Dir` → web Flow only.
- **New `MobileBreakpoint`** → web Canvas only. Int, default `600`, `0` = never stack, negative → Degraded (frozen,
  preserved, explained, via the existing root-tier path). **Stored on the `<Layout>` element**
  (`<Layout Kind="Canvas" MobileBreakpoint="600"/>`), like Cols/Rows/Gap — `FormRootValues.StorageAttributes` answers
  that, and round-trip placement follows. Needs a WinForms-oracle exemption (web-only row) in the parity test and a
  cross-or-name entry in the retarget sweep.

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
  positioned bands (top strips at the top edge in document order, bottom strips at the bottom edge, full width), NOT as
  page chrome outside it — every control's `X`/`Y` then lands exactly where it was designed. (Grid/Flow keep strips as
  chrome, unchanged.)
- Every positioned control is absolutely positioned at `X`/`Y` with `Width`/`Height`.
- A container (`IsContainer`) is its own positioned box; its children use coordinates relative to it.
- **Stacking order**: the model's document order is WinForms' back-to-front ("last in the list is in front",
  `FormDocument.cs:190-192`), and absolutely-positioned DOM siblings paint later-on-top — so emitting in document order
  is correct with no reordering. (The WinForms-side inversion fix lives in `RegionWriter.AppendSiblings`, `:694-760`; the
  page needs none.)
- `<Literal>` markup, if any, flows at the form area's top-left under the positioned controls (documented; not reordered).
- Styling from the catalog CSS walk (`FormCss`), unchanged; tray components emit no markup, unchanged.
- A non-positive `Width`/`Height` is read as a `.blform` reads it (no new strictness); the emitter writes no size for it.

## 4. Resizing: Anchor and Dock → CSS

**The form area fills the browser** (100% width; height fills the viewport) with a **minimum size equal to the design
size**: larger → controls follow anchors; smaller but above the breakpoint → the form keeps its design size and the page
scrolls; never squashed.

Per control, with `W`/`H` the container's design width/height (Anchor default `Top, Left`):

| Anchor | CSS |
|---|---|
| Top, Left (default) | `left:X; top:Y; width; height` |
| Right (not Left) | `right:(W−X−width); width` |
| Left + Right | `left:X; right:(W−X−width)`; width follows |
| Bottom (not Top) | `bottom:(H−Y−height); height` |
| Top + Bottom | `top:Y; bottom:(H−Y−height)`; height follows |
| neither on an axis | **centred relative to its original offset, as WinForms does**: new left = X + (W′−W)/2 → `left: calc(50% + (X − W/2)px)`; same for top with H |

**Dock** (`PixelGeometry.Dock`: Top/Bottom/Left/Right/Fill — not the strips' `Dock` property) is resolved ONCE at the
design size into edges with fixed insets. **Docking order in model terms: WinForms docks back-most first, which is the
model's DOCUMENT order (first in the list docks first)** — stated in these terms on purpose, given this repo's z-order
inversion history. Because the strips are inside the form area (§3), their heights are real insets, e.g. a Fill between
a 24px menu and a 22px status strip → `top:24; left:0; right:0; bottom:22`. The resolver is a pure function (§7).

## 5. Phones: stacking below the breakpoint

Below `MobileBreakpoint` (default 600px; 0 disables), one media query switches the form area to a single column:
- The form area becomes a flex column; **every control switches to `position: static`**, containers get `height: auto`
  (otherwise `order` does nothing and heights clip).
- **Reading order** (pure function, computed at build time): controls grouped into ROWS by vertical overlap; rows top to
  bottom; left to right within a row; a container stacks as one block with its children stacked inside it the same way.
  Applied with CSS `order` inside the media query; HTML order unchanged.
- Inputs stretch to full width (TextBox, ComboBox, ListBox, multi-line text, PictureBox — from a per-row CATALOG flag,
  never a `control.Kind` switch); small controls keep their designed size, left-aligned.
- Top strips first, bottom strips last; anchors/Dock ignored; hidden controls stay hidden; a small fixed gap.
- Known limit (accepted): no per-control phone override yet.

## 6. Retarget (reachable today)

"Retarget Form…" and `design --retarget` accept any web source, and `ToPixels` places controls by cell/flow
(`FormRetarget.cs:624-739`), discarding a Canvas form's exact positions; root `Width`/`Height` handling in `ConvertRoot`
is target-gated. In piece 1:
- **Canvas web → WinForms copies geometry and the design size exactly** (lossless; no BL8025 for those controls);
  `MobileBreakpoint` is web-only and dropped-and-named (BL8024).
- WinForms → web keeps producing Grid (unchanged) — piece 4 replaces retarget.
- The retarget catalog sweep and the root retarget sweep cover the new row.

## 7. Testing

- **The WinForms window is the reference for resize behaviour.** For the same pixel document, a test builds and RUNS the
  WinForms form (the acceptance harness: csc + a driver), sets its ClientSize to W′×H′ (larger, and at the design size),
  and prints every control's `Bounds`. The Edge check (below) at the same viewport must match those bounds (±1px) —
  anchors, docking order and the no-anchor centring are then proven against WinForms itself, not against our reading of
  it.
- **Edge headless layout check** (`msedge.exe`, present at `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`):
  - one HARNESS page iframes the generated form page at each width (design size, wider, narrower, phone) — media queries
    evaluate against the iframe; one launch, deterministic;
  - the test injects its own CLASSIC measuring script into a COPY of the page (the generated `<script type="module">`
    fails under `file://` — harmless for layout) that writes every control's `getBoundingClientRect()` into the DOM;
  - flags: `--headless=new --user-data-dir=<fresh temp dir>` (mandatory — without it a launch can hand off to the
    owner's running Edge and exit) `--hide-scrollbars --force-device-scale-factor=1 --virtual-time-budget=<ms>
    --dump-dom`;
  - kill with `Process.Kill(entireProcessTree: true)` on the test's own PID (Edge spawns children); never by name;
  - where Edge is absent (Linux/cloud) the tests SKIP with a reason (the repo's rule; never a pass-by-absence).
  Assertions: at the design size every control within 1px of `X`/`Y`/`Width`/`Height` (strips included, per §3); wider
  matches the WinForms reference; narrower-than-design scrolls (form keeps its size); below the breakpoint the order is
  the reading order, inputs span the width, strips first/last.
- **Pure functions, table-tested:** reading-order grouping (overlapping rows, a label slightly higher than its box,
  nested containers, ties); the Dock resolver (each value, document-order docking, nesting); Anchor → CSS (every
  combination, including the centring formula).
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

## 8. Out of scope (piece 1)

The portable control library (piece 2) and any handler-code change; the Desktop | Web toolbar switch (piece 3);
converting existing Grid/Flow forms and retiring `.blwebform` (piece 4); WinForms → Canvas retarget (piece 4 replaces
retarget); per-control phone overrides; percentage/relative sizing; a breakpoint preview in the canvas.
