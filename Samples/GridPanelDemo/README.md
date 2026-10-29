# GridPanel demo

A small web-form project showing the `GridPanel` container:

- `Columns="220px,1fr"` — one fixed column and one flexible column.
- `Rows="auto,auto,auto"` — three content-sized rows.
- `Gap="12px"` — space between cells.
- The title and status label span both columns (`ColSpan="2"`).

## Open it

1. Open `GridPanelDemo.blproj` in Visual Game Studio.
2. Press F5, or build and open the generated `GridPanelDemo.html`.
3. Select the grid on the canvas:
   - drag a column/row boundary to resize;
   - click a header label to select a track;
   - use the `+` / `−` badges beside the selected header;
   - drag a header label to reorder tracks.

The generated page is plain HTML/CSS Grid. The designer canvas is a schematic; the browser is the final renderer.
