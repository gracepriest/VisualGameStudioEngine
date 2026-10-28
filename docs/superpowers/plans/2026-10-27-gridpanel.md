# GridPanel — web-first grid container (implementation plan)

**Status:** ready for implementation. Owner decisions accepted 2026-10-27.

## Goal

Add a first-class **GridPanel** container to the web form designer:

- drag it onto a form;
- size it on a Canvas page;
- set `Columns`, `Rows`, `Gap` in Properties;
- add/remove/reorder rows and columns;
- drag other controls into its cells;
- assign cell spans;
- have the build emit native CSS Grid markup and refuse cell collisions.

The browser lays the grid out. The compiler validates and emits; it never computes a second layout.

## Locked decisions

| # | Decision | Value |
|---|---|---|
| D1 | Platform scope | Web only for the first pass. WinForms retarget warns; no `TableLayoutPanel` mapping yet. |
| D2 | Grid engine | Native CSS Grid (`display: grid`, `grid-template-*`). No Bootstrap. |
| D3 | Placement | Explicit cells. Auto-place into the next free cell only when a drop has no cell target. |
| D4 | Overlap | Refused by default. Same cell or intersecting spans is a build/designer error. |
| D5 | Track editing | Property grid first: structured list editor writing the existing comma-separated track text. Visual headers/resize later. |
| D6 | Mobile | Keep the GridPanel’s own columns; no stack-on-mobile in pass 1. |
| D7 | Theme | Deferred until the grid works. |
| D8 | Relationship to page Grid | Complementary. Document-level `<Layout Kind="Grid">` remains the page skeleton; GridPanel is a nested container. |

## Model

### New catalog kind

`FormControlCatalog` gains:

```text
Kind:             GridPanel
WinFormsType:     null
HtmlTag:          div
IsContainer:      true
Place:            Positioned
DefaultWidth:     320
DefaultHeight:    180
```

Properties:

| Property | Type | Default | Notes |
|---|---|---|---|
| `Columns` | String | `1fr,1fr` | CSS track list stored verbatim; emitted as `grid-template-columns`. |
| `Rows` | String | `auto,auto` | CSS track list stored verbatim; emitted as `grid-template-rows`. |
| `Gap` | String | `8px` | CSS `gap`. |

`DropValues` sets `Columns="1fr,1fr"` and `Rows="auto,auto"` on a fresh drop, so the first drop is immediately usable. The defaults live in the catalog rows; there is no second copy in the designer.

### Child cell model

Do **not** reuse `Geometry` for children of a GridPanel: on a Canvas page every positioned control already has `PixelGeometry`, and on a page Grid it may have `GridGeometry` for the page. A GridPanel child needs a cell independent of both.

Add a nullable value to `FormControl`:

```csharp
public GridCell? GridCell { get; set; }
```

```csharp
public sealed class GridCell
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public GridCell Clone() => ...;
}
```

- Only a child whose **direct parent is a GridPanel** has `GridCell`.
- A GridPanel itself keeps normal geometry: `PixelGeometry` on a Canvas page, `GridGeometry` on a page Grid.
- A child inside a GridPanel may itself be a container (Panel, another GridPanel). Its own children follow the existing rules for that container.

### Serialization

The existing container shape nests children directly:

```xml
<GridPanel Id="grid1" X="20" Y="80" Width="600" Height="300"
           Columns="1fr,1fr" Rows="auto,auto" Gap="8px" TabIndex="0">
  <Button Id="btnSave" Col="0" Row="0" TabIndex="1" Text="Save" />
  <TextBox Id="txtName" Col="1" Row="0" TabIndex="2" />
</GridPanel>
```

- The reader is **parent-aware**: a positioned child under a GridPanel reads `Col`/`Row`/`ColSpan`/`RowSpan` into `GridCell`; it does not create `GridGeometry`.
- The writer is parent-aware for the same reason.
- A GridPanel child without a `GridCell` gets `Col=0, Row=0` when read; the model should always carry one for a new drop.
- `ColSpan`/`RowSpan` of 1 are omitted, matching `GridGeometry`.
- `GridCell` is cloned by `FormControl.Clone` and copied by any clipboard subtree code.

## Emission

For a GridPanel, emit:

```html
<div id="grid1" class="vgs-GridPanel" ...>
  <button id="btnSave" ...>Save</button>
  <input id="txtName" ...>
</div>
```

```css
#grid1 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  grid-template-rows: auto auto;
  gap: 8px;
}
#btnSave { grid-column: 1; grid-row: 1; }
#txtName { grid-column: 2; grid-row: 1; }
```

Rules:

- `FormAssetEmitter.Tracks` and `FormGridLayout.ParseTracks` must split on **top-level commas only**, so `minmax(100px, 1fr)`/`repeat(2, 1fr)` survive. This is a prerequisite for both GridPanel and the existing page Grid.
- A GridPanel's own CSS comes from its `Columns`/`Rows`/`Gap` properties, never from the document `Layout`.
- A child's `grid-column`/`grid-row` is written only when its parent is a GridPanel. Today `AppendControlCss` writes those rules for **any** control with `GridGeometry` when the document is Grid; that must become direct-child-aware so a nested GridPanel child does not also receive page coordinates.
- Page-level Grid children keep their existing `GridGeometry` rules.
- A GridPanel on a Canvas page is positioned by `CanvasCss` like any other pixel container; its children are laid out by its own grid rules.
- A GridPanel on a page Grid is a page item; its own grid is nested inside it.
- Menus/toolbars stay outside `.vgs-form` as page chrome, as today.
- No `<table>` markup.

## Validation and diagnostics

Add **BL8043 `GridCellConflict`** (next free code after BL8042) for:

- two sibling controls whose cells intersect (same cell, or overlapping spans);
- a cell outside `Columns`/`Rows` track counts;
- a span that runs past the last track;
- a GridPanel child with no cell.

The compiler refuses the page; the designer marks the offending cell and names both controls. Overlap is not a layering feature in pass 1.

The same rule is exposed as a pure helper in `BasicLang/Forms` so the reader, build routes and designer all ask one answer.

## Designer behaviour

### Property grid

When a **GridPanel** is selected:

- `Columns`, `Rows`, `Gap` rows (catalog rows, so validation/reset are shared).
- A structured track editor for `Columns`/`Rows`:
  - add / remove / move up / move down;
  - each track is a size cell (`120px`, `1fr`, `auto`, plus a raw text escape hatch);
  - writes the joined comma list in one undoable edit.
- Existing `X`/`Y`/`Width`/`Height` rows on a Canvas page.

When a **child of a GridPanel** is selected:

- `Col`, `Row`, `ColSpan`, `RowSpan` intrinsic rows;
- values clamped to the parent grid’s track counts;
- one undo entry per edit.

The property grid finds the parent through `FormDocument.ParentOf(control)`; it never infers it from a kind list.

### Canvas layout

`FormCanvasTransform` gains a nested-grid branch:

- a GridPanel’s rectangle is resolved normally (pixels or page cell);
- its children are placed by `FormGridLayout.CellRect` using the GridPanel’s own `Columns`/`Rows`/`Gap`;
- hit-testing returns the child or the GridPanel plus the cell under the pointer;
- `FormPlacement.Place` detects a GridPanel parent and writes a `GridCell` instead of `PixelGeometry`;
- a drop on an occupied cell is refused with a readable reason;
- moving a child between cells rewrites `Col`/`Row`;
- deleting a row/column either clamps children or refuses when a child would be orphaned. Pass 1: refuse and name the controls, so no control silently jumps.

The canvas’s cell outlines are schematic; the browser is still the final renderer.

### First-pass UI scope

In:
- drag/drop into cells;
- property-grid track editing;
- `Col`/`Row`/spans;
- cell highlight and collision display;
- undo/redo for all of the above.

Out:
- column/row header chrome;
- drag-resize tracks;
- mobile stack option;
- nested-grid visual guides beyond one level;
- Bootstrap or any theme.

Headers and drag-resize are phase 2 after the model and property editor are stable.

## Retargeting

GridPanel is web-only in this pass:

- web → WinForms: report the existing “kind not on target” warning for `GridPanel` and its cell metadata; do not pretend a `TableLayoutPanel` equivalent exists.
- WinForms → web: unchanged in this pass. `TableLayoutPanel` remains desktop-only.
- Opening a `.blwebform` with a GridPanel and switching it to Canvas: the GridPanel keeps its pixel rectangle and its children keep their cells.
- Canvas ↔ page Grid switch: a GridPanel is treated like any container; it keeps its own internal grid.

## Tasks

### Task 1 — track splitting prerequisite

- Implement one top-level comma splitter used by `FormGridLayout` and `FormAssetEmitter`.
- Tests: `minmax(100px, 1fr)`, `repeat(2, 1fr)`, gaps, empty tracks, existing simple lists.

### Task 2 — model and serialization

- Add `GridPanel` to the catalog, toolbox and schematic.
- Add `GridCell` to `FormControl`; clone it.
- Parent-aware reader/writer; update `FormClipboard` to carry cells.
- Round-trip tests: Canvas and page Grid; spans; absent cells.

### Task 3 — emission and validation

- Emit GridPanel HTML/CSS.
- Scope page-grid rules to direct children of `.vgs-form`.
- Add `GridCellConflict` BL8043 and the pure collision helper.
- Tests: exact HTML/CSS, nested grids, collision cases, out-of-range, build refusal.

### Task 4 — property grid and track editor

- Add catalog rows for `Columns`, `Rows`, `Gap`.
- Add child `Col`/`Row`/`ColSpan`/`RowSpan` rows.
- Add structured track editor with add/remove/reorder/raw fallback.
- Tests: writes, no-ops, reset, undo, clamps, real-view editing.

### Task 5 — canvas placement

- Nested cell layout in `FormCanvasTransform`.
- GridPanel-aware `FormPlacement` and drop target.
- Move between cells, selection, collision highlight, one-undo edits.
- Tests: drop, move, refuse occupied cell, two zooms, Canvas and page Grid.

### Task 6 — retarget and gates

- Web-only retarget warning.
- Catalog coverage, render/hash tests, real-view interaction tests.
- CLI/IDE build round-trip; page CSS checked against the document.
- Human click-through: create, set rows/columns, drop controls, span, build/run.

## Risks

| Risk | Mitigation |
|---|---|
| Two geometry shapes on one control | `GridCell` is independent of `Geometry`; parent decides which one is meaningful. |
| Page grid leaking into nested children | `AppendControlCss` and canvas layout both receive the parent; direct-child rule is tested. |
| CSS functions break on commas | Top-level splitter in one shared helper, tested before any UI. |
| Removing a row orphans controls | Refuse the removal and name the controls; never silently clamp. |
| Drop hits the panel, not a cell | Hit-test returns the cell; placement writes `GridCell`, never `PixelGeometry`. |
| Web-only control surprises retarget users | Emit the existing kind-not-on-target warning and document it. |
| Canvas looks like the final page | Keep the schematic note; test that only the browser determines `auto`/`fr` sizing. |

## Definition of done

- A user can drag a GridPanel onto a Canvas web form, set its rows/columns, drop a Button and a TextBox into two cells, build, and see the two controls in those cells in the browser.
- Two controls in one cell are refused with a clear reason.
- A GridPanel nested in a page-Grid cell works; a page-level Grid still works unchanged.
- The same form survives save/reopen, undo/redo, copy/paste and a Canvas/page-Grid switch without losing a control or its cell.
- The focused designer and build-route tests are green.

## Implementation status (2026-10-27)

Implemented in this slice:

- the top-level track splitter shared by the emitter and canvas;
- `GridPanel` catalog kind and `FormControl.GridCell`;
- parent-aware reader/writer and clipboard round-trip;
- CSS Grid emission on Canvas and page-Grid layouts, including child cell rules;
- property-grid track editing with add/remove;
- column/row header labels, track selection, direct +/− insert/delete, and drag-resize on the canvas;
- drop into a cell on Canvas and page-Grid layouts, including occupied-cell refusal;
- dragging a child between cells;
- collision/range validation (BL8043) through the reader and both build routes;
- dragging an outside control into a cell (it becomes a cell child);
- web-only retarget removal with a named loss.

Deliberately not in this slice:

- reordering tracks from the property editor (the text field is the escape hatch);
- nested `Panel` containers inside a GridPanel (a nested `GridPanel` is allowed);
- Bootstrap or any theme.
