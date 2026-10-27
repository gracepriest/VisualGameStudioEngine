# Web form Grid and rows — draft plan (2026-10-27)

**Status:** draft for owner decisions. Nothing here is implemented yet.

## What already exists

`.blwebform` is not missing a grid backend:

- `<Layout Kind="Grid" Cols="120px,1fr" Rows="auto,auto" Gap="8px"/>` is read and written by
  `FormDocumentReader` / `FormDocumentWriter`.
- A web control already carries `GridGeometry` with `Col`, `Row`, `ColSpan`, `RowSpan`
  (`BasicLang/Forms/FormGeometry.cs`).
- `FormAssetEmitter` writes `grid-template-columns` / `grid-template-rows` and cell placement.
- `FormGridLayout` resolves cell rectangles and `CellAt` for drops; the canvas draws cells and
  drop targets on a Grid page (`FormCanvasTransform.WebLayout`).
- The property grid exposes `Cols`, `Rows` and `Gap` **as text** when the Form itself is selected;
  a selected control shows `Col` and `Row` only (`FormPropertyGridViewModel`).
- `FormRetarget` derives a Grid page from a WinForms pixel form; a Canvas page is the default new
  web form (`FormScaffolder.Create(..., webLayout: Canvas)`).

## The actual gaps

1. **No way to make or switch a Grid page in the IDE.** `FormScaffolder` accepts
   `webLayout: Grid`, but Add New Form never passes it, and the property grid has no Layout-kind
   row. A user can only get a Grid page by editing XML or retargeting from WinForms.
2. **Rows and columns are raw strings.** There is no add/remove/reorder/resize UI. Typing
   `1fr,1fr` is the only way to add a column; a typo is only caught by the browser.
3. **The comma format breaks legal CSS.** `FormGridLayout.ParseTracks` and
   `FormAssetEmitter.Tracks` split on every comma, so `minmax(100px, 1fr)` becomes three
   fragments. The current test documents this as a known limitation.
4. **No row/column chrome or gestures.** There are no headers, insert/delete affordances, track
   resize handles, or a visible selected cell.
5. **Spans are model-only.** `ColSpan` / `RowSpan` exist and the emitter uses them, but the
   property grid does not expose them and no drag gesture sets them.
6. **Nested layout is undefined.** CSS grid applies to the document; a Panel's children currently
   stay in the panel's flow. The canvas deliberately lays out top-level controls only.

## Recommended decisions

| # | Decision | Recommendation |
|---|---|---|
| D1 | What is a “row”? | A CSS grid track in `<Layout Rows>`, not a DataGridView/list row. |
| D2 | Track storage | Keep the existing raw `Cols`/`Rows` strings. Fix the splitter to respect parentheses instead of introducing a second model. |
| D3 | Track editor | Structured list with common units (`px`, `fr`, `auto`) plus a raw text escape hatch. Editing rewrites the same comma list. |
| D4 | New-form default | Keep **Canvas** as the default; add a layout choice at New Form and a “Convert layout…” command. This avoids changing every existing form and keeps Canvas/Grid both reachable. |
| D5 | Scope of the visual editor | Phase 1 handles top-level controls only. Nested grids/panels are a separate decision; do not promise what the renderer does not lay out. |
| D6 | Lossy switches | A Canvas↔Grid switch is allowed but reports a warning when geometry cannot be represented; it never silently drops a control. |

## Phases

### Phase 1 — make tracks correct and editable

- Add a top-level comma splitter shared by `FormGridLayout` and `FormAssetEmitter` so
  `minmax(100px, 1fr)` and `repeat(2, 1fr)` survive as one track.
- Add a `GridTracks` row editor: list of tracks with **Add**, **Remove**, **Move up/down**, and a
  size cell. Write the joined string back through the existing FormRoot rows, so undo and save stay
  one edit.
- Add `ColSpan` / `RowSpan` rows to a Grid control’s property grid, with the same no-op/refusal
  rules as `Col`/`Row`.
- Tests: splitter (nested parentheses, spaces, empty), round-trip of each track kind, span edits,
  no-op/undo, real-view add/remove/reorder at two sizes.

### Phase 2 — canvas grid chrome

- Draw column headers (A, B, C…) and row headers (1, 2, 3…) around a Grid page.
- Hover/select a header → highlight the whole track; offer **Insert before/after** and **Delete**.
- Drag a track edge → rewrite its size (with a live preview and one undo entry on release).
- Show the selected control’s occupied cells, including spans, not only its outline.
- Tests: header hit areas at two zooms, insert/delete rewrites `Cols`/`Rows`, track resize is one
  edit, a control in a deleted track is clamped (never lost), keyboard focus/undo.

### Phase 3 — placing and moving cells

- Drop from the toolbox on a cell → writes `Col`/`Row`; drop on an occupied cell either refuses or
  asks, matching the existing placement rules.
- Drag a control between cells; dragging over a cell highlights it.
- Alt/Shift drag or a property-grid edit sets a span; clamped to the grid.
- Tests: drop/move/span, undo, Grid page and Canvas page separately, web build bytes unchanged.

### Phase 4 — create and convert layouts

- Add a layout choice to New Form: **Canvas (default)** or **Grid**; pass `webLayout` through.
- Add a Form-level **Layout** row / command to switch Canvas ↔ Grid. The switch:
  - preserves control order and ids;
  - derives `Cols`/`Rows` from the source geometry for Canvas→Grid (reuse the derivation logic
    already in `FormRetarget`);
  - emits a warning code for every lossy approximation (`FormLayoutCrossed` already exists for
    retarget);
  - is one undo step.
- Tests: new-form pair on disk, switch both directions, no control lost, warning text, build
  round-trip, CLI/e2e page with a converted grid.

### Phase 5 — gates and click-through

- Headless real-view tests for headers, add/remove, resize, drag/drop and spans.
- A Node/DOM check that the emitted page’s CSS matches the document’s tracks.
- A human click-through on a machine with a display: create a Grid page, add a row/column, resize,
  span two controls, switch to Canvas and back, build/run.

## Explicit non-goals for the first pass

- A full CSS layout engine in the canvas. The canvas stays a schematic; F5 remains the renderer.
- Nested grids inside Panels.
- Free-form CSS track functions in the visual editor beyond a raw text escape hatch.
- Changing the default layout for existing forms.

## Open questions for the owner

1. Should **Grid** become the default for a new `.blwebform`, or stay opt-in as recommended?
2. Is the intended “rows” feature the CSS grid track list, or also a row-based data/list editor?
3. Do you want the first pass to expose only top-level controls (recommended), or nested Panels too?
4. Should track resize write `px` sizes only, or preserve `fr`/`auto` and expose those choices?
