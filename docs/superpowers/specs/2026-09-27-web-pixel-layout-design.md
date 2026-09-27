# Web forms laid out in pixels — piece 1 of "one form, either target" (design)

Status: approved section by section in brainstorming 2026-09-27, pending written-spec review.
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
1. **This spec** — the web page generator lays out a pixel form (desktop-exact, Anchor/Dock resize, phone stacking),
   delivered through a new **Pixel** layout for web forms so web projects get it immediately.
2. The portable control library (P-D3).
3. The Desktop | Web toolbar switch and the one-form build (P-D4, P-D6).
4. Retiring `.blwebform` (P-D5) — with a Pixel web form already carrying pixel geometry, the conversion is nearly a
   rename; Grid/Flow forms convert cell → pixel from where the canvas draws them.

The property grid (`docs/superpowers/specs/2026-09-25-property-grid-vs-parity-design.md`) continues in parallel.

## 1. Goal and decisions (piece 1)

A web form can use a new layout, **Pixel**, which is the default for new web forms. It stores exactly what a
WinForms form stores and is designed exactly like one; the web page generated from it is pixel-exact at the design
size, follows each control's Anchor/Dock when the browser resizes, scrolls rather than squashes when the browser is
smaller than the design, and stacks into one column below a phone breakpoint.

Decisions (owner, 2026-09-27):
- **D1 — Pixel is a third web layout** next to Grid and Flow, and the default for NEW web forms. Existing Grid/Flow
  forms are unchanged (piece 4 converts them).
- **D2 — a Pixel web form stores WinForms geometry**: the form's design size (`Width`/`Height` on the root, the
  ClientSize), and per control `X`/`Y`/`Width`/`Height`/`Anchor`/`Dock` — the same attributes and the same
  `PixelGeometry` as a `.blform`.
- **D3 — the canvas, toolbox and property grid treat it exactly like a WinForms form** (drag, resize handles,
  Location/Size and Anchor/Dock rows). No new canvas code: the pixel path already exists.
- **D4 — handler code is unchanged in piece 1.** A Pixel web form's code-behind keeps today's web style
  (`e As DomEvent`, `getElementById`). Piece 2 changes it.
- **D5 — desktop-exact / anchor-resize / phone-stack** as specified in §3–§5.

## 2. The document

- `FormLayoutKind` gains **`Pixel`** (`BasicLang/Forms/FormGeometry.cs:85`).
- On a `.blwebform` whose `<Layout Kind="Pixel"/>`:
  - the ROOT carries `Width`/`Height` (design size) — today these are WinForms-only root attributes, and on a
    `.blwebform` root `Width` is treated as layout vocabulary that the retarget drops-and-names (BL8024);
  - each positioned control carries `X`/`Y`/`Width`/`Height`/`Anchor`/`Dock`, read as `PixelGeometry`;
  - `Col`/`Row`/`ColSpan`/`RowSpan` are foreign vocabulary there (as `X`/`Y` are on a Grid form).
- **The geometry vocabulary is decided by (target, layout kind), not by target alone.** `FormDocumentReader.ReadGeometry`
  (`FormDocumentReader.cs:643`) decides by target today; it becomes: WinForms or web-Pixel → pixel attributes; web-Grid →
  cell attributes; web-Flow → none. The writer mirrors it. ⛔ The reader's existing rule stands: a document is never half
  one vocabulary and half the other (the comment above `ReadGeometry`), so a foreign attribute on a control is
  Unknown/preserved and named, never silently converted.
- **Root rows become layout-aware.** `FormControlCatalog.FormRoot` rows carry per-TARGET applicability today
  (`Targets`); a Pixel web form needs `ClientSize` (today WinForms-only) and a Grid form needs `Cols/Rows/Gap`. Add a
  per-row LAYOUT applicability (e.g. `Layouts: Pixel`, `Layouts: Grid`) evaluated together with the target, through ONE
  predicate every consumer uses (reader, writer, region writer, grid, retarget) — never a second `if` per consumer.
  `FormRootValues` maps the new/extended rows.
- **New root row `MobileBreakpoint`** (Int, web-only, Pixel-only, default `600`, `0` = never stack), stored as a typed
  `FormDocument` field mapped by `FormRootValues` (the slice-1 pattern; `FormDocument.Properties` is not needed).
- `FormScaffolder` creates new web forms with `<Layout Kind="Pixel"/>`, a default design size (the WinForms scaffold's
  size), and no cell vocabulary.
- Round trip: a Pixel web form reads and writes byte-for-byte; Grid/Flow forms are byte-for-byte unchanged.

## 3. The page at the design size

`FormAssetEmitter` gains a Pixel path (Grid/Flow paths unchanged):
- The form area (`.vgs-form`) is a positioned box; its design size is the form's `Width`×`Height`.
- Every positioned control is absolutely positioned at its `X`/`Y` with its `Width`/`Height`.
- A container (Panel, GroupBox, … — `IsContainer`) is its own positioned box; its children's coordinates are relative
  to it, exactly as WinForms child coordinates are.
- **Stacking order matches WinForms** — reuse the emitter's existing z-order rule (the WinForms z-order inversion was a
  real defect once; the rule that fixed it applies here).
- Docked strips (MenuStrip/ToolStrip/StatusStrip) stay page chrome before/after the form area, unchanged.
- Styling (BackColor, ForeColor, Font, …) comes from the catalog CSS walk (`FormCss`), unchanged.
- Tray components emit no markup, unchanged.

## 4. Resizing: Anchor and Dock → CSS

**The form area fills the browser** (width 100% of the page; height fills the viewport below/above the chrome) with a
**minimum size equal to the design size**. So:
- browser LARGER than the design → controls follow their anchors (below);
- browser SMALLER than the design but wider than the breakpoint → the form keeps its design size and the page scrolls
  (like a fixed-size window); controls never squash onto each other.

Per control, from its `Anchor` (the WinForms default is `Top, Left`), with `W`/`H` the container's design width/height:

| Anchor | CSS |
|---|---|
| Top, Left (default) | `left:X; top:Y; width; height` |
| Right (not Left) | `right:(W−X−width); width` |
| Left + Right | `left:X; right:(W−X−width)`; width follows |
| Bottom (not Top) | `bottom:(H−Y−height); height` |
| Top + Bottom | `top:Y; bottom:(H−Y−height)`; height follows |
| none on an axis | centred proportionally on that axis, as WinForms does (offset as a percentage of the container, fixed size) |

**Dock** (`Top`/`Bottom`/`Left`/`Right`/`Fill`) is resolved ONCE at the design size, in WinForms' docking order
(reverse z-order), into edges with fixed insets — e.g. a Fill between a 24px menu and a 22px status strip becomes
`top:24; left:0; right:0; bottom:22`. Resizing then behaves as WinForms docking does, because docked insets are fixed
sizes. The resolver is a pure function (§7).

⚠ The spec's §2 says `PixelGeometry.Dock` (a positioned control's DockStyle) — not the strips' `Dock` PROPERTY, which
stays page chrome.

## 5. Phones: stacking below the breakpoint

When the browser is narrower than `MobileBreakpoint` (default 600px; 0 disables), one CSS media query switches the form
area to a single column:
- **Reading order**, computed at build time by a pure function: controls are grouped into ROWS by vertical overlap (two
  controls whose top-to-bottom spans overlap are the same row); rows top to bottom; within a row, left to right. A
  container stacks as one block with its own children stacked inside it the same way.
- Inputs stretch to full width: TextBox, ComboBox, ListBox, multi-line text, PictureBox (the set comes from the CATALOG —
  a per-row "stretches when stacked" flag — never a `control.Kind` switch).
- Small controls keep their designed size, left-aligned: Button, CheckBox, RadioButton, Label, … (the rest).
- A small fixed gap between items. Anchors and Dock are ignored in stacked mode; menu/tool/status strips stay top/bottom.
- Hidden controls stay hidden.
- The stacking position is applied with CSS `order` inside the media query; the page's HTML order is NOT changed, so
  the desktop stacking order stays correct.
- Known limit (accepted): no per-control phone override yet; the phone layout follows the desktop design and the
  breakpoint.

## 6. Errors and edge cases

- Controls partly outside the form area are shown (overflow visible), not clipped — as WinForms shows them.
- Zero/negative `Width`/`Height` → refused like any invalid value (Degraded, preserved, named), never emitted.
- Overlapping controls are allowed (WinForms allows them); z-order decides.
- An empty Pixel form emits a valid page.
- A Pixel form with a foreign cell attribute on a control → Unknown, preserved, named (never converted).
- ⛔ Every value that reaches CSS goes through the catalog's accepted values and the existing CSS-safety rules (no raw
  user text in a style — see the property-grid slice-1 review).

## 7. Testing

- **Real browser layout check (the main oracle).** Tests generate a Pixel form's page and open it in Microsoft Edge
  headless (`msedge.exe`, present on the owner's machine) at several widths — the design size, wider, narrower,
  phone-sized; a script in the page records every control's `getBoundingClientRect()` into the DOM, the test reads it
  back and compares with the design: at the design size each control is within 1px of where it was put; wider, a
  Left+Right control grew by exactly the difference and a Right-anchored one moved by it; Dock Fill filled the gap
  between strips; below the breakpoint the controls are in reading order and inputs span the width. Where Edge is absent
  (Linux/cloud) these SKIP with a reason (the repo's rule for Windows-only prerequisites; never a pass-by-absence).
  ⛔ Close every Edge process the test starts (kill by the test's own PID, never by name).
- **Pure functions, table-tested:** the reading-order grouping (overlapping rows, a label slightly higher than its box,
  nested containers, ties); the Dock resolver (each dock value, order, nesting); the Anchor → CSS mapping (every
  combination).
- **Documents:** Pixel web form round trip byte-for-byte; Grid/Flow unchanged; foreign attributes named; the root rows'
  layout applicability through the one predicate (a catalog-driven sweep).
- **Canvas (headless, real IDE view):** a Pixel web form drags, resizes and edits Location/Size and Anchor/Dock, like
  the WinForms tests; the grid shows ClientSize and MobileBreakpoint on a Pixel web form and Cols/Rows/Gap only on Grid.
- **End to end:** a Pixel twin of the web acceptance tests — a designer-built Pixel form, built by the real CLI, its page
  RUN under node (handler fires, no LOAD ERROR) AND rendered in Edge with the layout checks above.
- **Mutation checks** on the load-bearing rules: vocabulary by (target, layout); the layout-applicability predicate;
  anchor edges; dock order; reading-order row grouping; the breakpoint query; z-order.

## 8. Out of scope (piece 1)

The portable control library (piece 2) and any change to handler code; the Desktop | Web toolbar switch (piece 3);
converting existing Grid/Flow forms and retiring `.blwebform` (piece 4); retargeting WinForms → Pixel web (piece 4
subsumes retarget); per-control phone overrides; percentage/relative sizing; a visual breakpoint preview in the canvas.
