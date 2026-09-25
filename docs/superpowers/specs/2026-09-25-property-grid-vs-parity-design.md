# Form designer property grid — Visual Studio parity (design)

Status: approved in brainstorming 2026-09-25, pending written-spec review.
Reference (measured, not recalled): `2026-09-25-property-grid-winforms-reference.md` beside this file —
Visual Studio's Properties window features, and the browsable properties, categories, defaults and events
of 23 WinForms types, taken by `TypeDescriptor` reflection over the real `System.Windows.Forms.dll`
(Microsoft.WindowsDesktop.App 8.0.23) on the owner's machine.

## 1. Goal and decisions

Bring the designer's property grid to where Visual Studio's Properties window is, and give each control
and the Form the properties a Visual Studio user expects.

Owner decisions (do not relitigate):
- **D1 — the commonly used set first.** ~15–25 properties per control, ~20 on the Form; the remainder later
  in batches. Not every browsable property.
- **D2 — one vocabulary.** Web forms use the SAME Visual Studio property names wherever they map cleanly
  to CSS/HTML; a property with no web meaning is WinForms-only (absent on a web form). Web-only extras:
  `CssClass`, `Style` (raw CSS).
- **D3 — all four grid features**, in order: the grid itself; the editors; the Events tab; multi-select.
- **D4 — approach 1:** the catalog is extended BY HAND and stays the single source of truth; a committed
  snapshot of the reflected WinForms metadata is the TEST ORACLE, never the author.

Where we start (audit 2026-09-25): no categories, no sort toggle, no search, no bold, no reset, no object
selector, no Events tab, no multi-select; Color is a text box; no Font/Size/Padding/Image editors. The
Form has Name/Text/Width/Height (WinForms) or Name/Text/Cols/Rows/Gap (web), hard-coded in
`FormPropertyGridViewModel.AddFormRows`. Controls carry 2–6 properties + 4 shared; one event each.
Visual Studio: Form 54 properties / 80 events; Button 45/64; TextBox 46/66.

## 2. Data model

### 2.1 `FormPropertyDef` gains
- `Category` — enum `FormPropertyCategory { Accessibility, Appearance, Behavior, Data, Design, Focus,
  Layout, Misc, WindowStyle }` (Visual Studio's groups; `Asynchronous` is PictureBox-only and out of D1).
- `Description` — the description-pane text, taken from WinForms' own `[Description]` via the snapshot.
- `CssProperty` — the web mapping (`BackColor` → `background-color`). `FormAssetEmitter` walks it
  generically, exactly as it walks `HtmlAttribute` today, so a row with a clean CSS meaning needs no
  emitter code. Rows whose web form is not one CSS declaration (Font, Padding, Cursor, Visible, Enabled)
  get a named converter, owned in one place.

### 2.2 New property types
Each is three answers: stored form, WinForms emission, web emission.

| Type | Stored (document) | WinForms | Web |
|---|---|---|---|
| `Font` | `Segoe UI, 9pt, style=Bold, Italic` (WinForms `FontConverter` text) | `New Font("Segoe UI", 9F, FontStyle.Bold Or FontStyle.Italic)` | `font-family`, `font-size`, `font-weight`, `font-style`, `text-decoration` |
| `Size` | `75, 23` | `New Size(75, 23)` | `width` / `height` |
| `Point` | `4, 4` | `New Point(4, 4)` | (layout-owned; see 2.4) |
| `Padding` | `4` or `4, 2, 4, 2` | `New Padding(4)` / `New Padding(4, 2, 4, 2)` | `padding` |
| `Image` | project-relative path | `Image.FromFile("…")` (today's factory) | `src="…"` / `url("…")` |
| `Cursor` | a `Cursors` member name | `Cursors.Hand` | `cursor: pointer` (mapping table) |

⛔ Composite values are emitted as ONE statement (`X = New Size(…)`), never member-wise — the fan-in rule
(`Location.X = …` is CS1612 and BasicLang reports nothing).
⛔ Every new type's `Accepts` decides Canon vs Degraded (D9): a malformed stored value is frozen,
preserved and explained, never coerced.
The `Color` type gains **system colours** (`Control`, `ControlText`, `Window`, …): WinForms
`SystemColors.X`; web the CSS system colour (`ButtonFace`, `ButtonText`, `Canvas`, …) — a mapping table;
a system colour with no CSS equivalent is WinForms-only as a VALUE (refused on a web form with a reason).

### 2.3 The Form is a catalog row
A new `FormPlace.Root`; the Form's properties move out of `AddFormRows` into the catalog. D1 set (~20):
Text, FormBorderStyle, StartPosition, Size/ClientSize, WindowState, MinimumSize, MaximumSize, ControlBox,
MaximizeBox, MinimizeBox, ShowIcon, ShowInTaskbar, TopMost, Icon, AcceptButton, CancelButton, KeyPreview,
BackColor, ForeColor, Font, Opacity. Web: Text (the page `<title>`), BackColor, ForeColor, Font, plus the
existing layout rows (Cols, Rows, Gap). `AcceptButton`/`CancelButton` are a new reference-to-control
editor (a drop-down of the form's buttons) — WinForms-only.
Every walker that asks "is this a control" must keep answering for the TREE (`AllControls()` never
includes the root); the root row is looked up by the grid and the region writer only.

### 2.4 Location and Size
Location/Size are shown as composite rows OVER the geometry the canvas already owns (`PixelGeometry`),
never a second copy of it. On a web form they are absent (the cell is the position).

### 2.5 Events
`FormControlDef.Events` — a list of `FormEventDef(Name, WinFormsArgs, WebEvent?, Category, Description,
IsDefault)`. Today's `WinFormsEvent`/`WebEvent`/`WinFormsEventArgs` become the `IsDefault` entry; readers
of the old fields keep their behaviour (derived accessors) until migrated. D1 set: ~8–15 common events
per control; Form: Load, Shown, Activated, FormClosing, FormClosed, Resize, Click, KeyDown, KeyPress,
KeyUp.

### 2.6 The oracle
`VisualGameStudio.Tests/Data/winforms-metadata.json` — the reflected browsable properties (name,
category, type, default, description) and events (name, args type, category, default event) for every
WinForms type in the catalog, plus `tools/WinFormsMetadataDump/` (a small net8.0-windows console app)
that regenerates it on Windows. Tests read the JSON, so parity runs on Linux too.

⛔ **Known finding the oracle must adjudicate first:** catalog defaults already disagree with WinForms in
places (catalog `ToolStrip.GripStyle` default `Hidden`; WinForms' is `Visible`). The writer OMITS a
value equal to the CATALOG default, so a strip drawn grip-less may run with a grip. Rule: the catalog
`Default` MUST equal the WinForms default; a designer PREFERENCE different from it is written explicitly
at creation (placement sets the property), never encoded as a false default. Every existing row is
checked in slice 1, and each disagreement is fixed or recorded with its reason.

## 3. The grid

- Moves out of `CodeEditorDocumentView.axaml` into its own `FormPropertyGridView` user control bound to
  the same `FormPropertyGridViewModel`. The real-view tests keep hosting the real document view.
- Top to bottom: **object selector** (every control, tray component and the form, `Name  Kind`;
  choosing one goes through the ONE selection store — `SelectInDesigner` — so canvas, tray and grid never
  disagree) → **toolbar** (Categorized | Alphabetical | Properties | Events) → **search** (by name, both
  sort modes) → rows (collapsible category headers, or flat A–Z) → **description pane** (name, then
  `Description`; a frozen row keeps its Degraded reason).
- **Bold** = present in the document AND different from the catalog default.
- **Reset** = remove the property from the document (the target's own default then applies; the file
  stays clean). Offered on bold rows only.
- **Composite rows** expand (Font → Name/Size/Bold/Italic/Underline; Size → Width/Height; Location → X/Y;
  Padding → All/Left/Top/Right/Bottom); the parent also accepts typed text.

## 4. Editors

| Type | Editor |
|---|---|
| Color | Drop-down with Custom (Avalonia `ColorView`, package `Avalonia.Controls.ColorPicker` at the repo's Avalonia version, with its theme include) / Web (named colours) / System tabs |
| Font | The composite row + a `…` dialog with family list (installed fonts), size, style toggles and a live preview |
| Bool | True/False drop-down (replaces the ToggleSwitch); double-click toggles |
| Enum, Cursor | Drop-down |
| Anchor, Dock | Today's visual pickers, as drop-down pop-ups |
| Image, Icon | `…` file picker; a file outside the project is offered a copy into `Resources\`; stored project-relative |
| Items | `…` dialog: multi-line text, one item per line (Visual Studio's String Collection Editor) |
| Reference (AcceptButton/CancelButton) | Drop-down of the form's buttons |

⛔ **Image requires a build change.** Today `Image.FromFile("path")` is emitted and NOTHING copies the file;
it works only if the path happens to resolve at run time. The build copies every referenced image/icon to
the output (beside the exe; into the site folder), and a missing file is a named build WARNING, never a
silent blank. An acceptance test runs a form with an image on both targets.

## 5. Events tab

- Rows: the control's catalog `Events` (categorized or A–Z); value cell = the bound handler.
- The cell's drop-down lists existing handlers in the code-behind whose signature FITS (WinForms' rule:
  a handler taking a base `EventArgs` fits any event; `MouseEventArgs` only mouse events). Picking one binds.
- Double-click an empty cell → creates `<Id>_<Event>` with the right signature and opens it (generalises
  `FormHandlers.PlanDefault` / `EnsureBind` from the default event to any event).
- Clearing a cell unbinds; code is never deleted.
- ⛔ On a web form the list contains ONLY events the region writer will actually wire — the same answer as
  `RegionWriter.IsEmittedBind` — so the grid can never offer a bind that silently does nothing.
- Form `Load` on the web = the page ready; Form events with no web meaning are WinForms-only.

## 6. Multi-select

- Rows: properties shared by every selected control (same name AND same type). Value shown when equal on
  all; blank when mixed.
- One edit applies to all as ONE undo step (the designer's existing undo stack).
- Object selector blank. Name and Location not offered; Size is.
- Events tab: shared events; picking a handler binds it on all.

## 7. Errors

Malformed stored value → Degraded (frozen, preserved, explained). Invalid typed value → refused in the
editor, never written. Missing image at build → named warning. A web form never shows a WinForms-only
property or event.

## 8. Testing

- **Parity** — every WinForms row's category, default, type and event args equal the snapshot.
- **Compile** — `WinFormsCatalogSweepTests` covers new rows automatically; plus one control per new value
  shape (Font, Size, Padding, system colour, Cursor) and every Enum member (existing sweep).
- **Run, not compile** — an acceptance test builds a form using the new properties on both targets and
  RUNS it: the WinForms driver prints back what the live window reports (`BackColor`, `Font`, `Cursor`,
  `StartPosition`, …); the web page's CSS is asserted and its handlers fire under node; the image copy on
  both targets. (⛔ "It compiles" was the ceiling for this feature once and hid two defects.)
- **Real view** — headless tests host the REAL document view and drive sort, search, bold, reset, the
  selector, every editor, Events-tab double-click, multi-select edit + undo — at more than one zoom.
- **Bindings** — every binding path in the new AXAML resolved by reflection (no compiled bindings here).
- **Round-trip / retarget** — reader/writer for every new type; the retarget catalog sweep proves new
  properties cross or are reported lost.
- Mutation-check the load-bearing rules (bold rule, reset-removes, IsEmittedBind filter, multi-select one
  undo step, default-equals-WinForms).

## 9. Delivery

Six slices, each its own commit, gated before the next:
1. Data model: `Category`/`Description`/`CssProperty`, new types, the Form as a catalog row, `Events`,
   the snapshot + tool + parity test, the default-disagreement sweep.
2. The grid: extracted view, categories, sort, search, bold, reset, object selector.
   **→ owner click-through in the IDE.**
3. The D1 property batches for every control and the Form, emitted on both targets.
4. The editors: colour, font, image (+ the build copy), items, reference.
5. The Events tab.
6. Multi-select.
Then records, the IDE drop, the owner's full click-through, the full suite, merge.

## 10. Out of scope

Every remaining browsable property (later batches, same machinery); `(DataBindings)`,
`(ApplicationSettings)`, `Modifiers`/`Locked`/`GenerateMember`; collection editors beyond a string list
(`TabPages`, `Columns`); extender properties (ToolTip on …) — `docs/form-designer-followups.md` 19;
resources/`.resx`.
