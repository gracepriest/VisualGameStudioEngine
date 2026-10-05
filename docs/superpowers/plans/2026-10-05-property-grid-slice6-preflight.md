# Slice 6 pre-flight: multi-select, expanded and corrected against today's tree

Plan: `docs/superpowers/plans/2026-09-25-property-grid-vs-parity.md`, section "## Slice 6 — Multi-select (TASK
granularity)" (Tasks 6.1–6.4), "## Closing (after slice 6)", its Traps, and the "Tests to re-check" row for slice 6.
Spec: `docs/superpowers/specs/2026-09-25-property-grid-vs-parity-design.md` §6, §8, §9. Shape and rigour copied from
`2026-10-04-property-grid-slice4-preflight.md` and `2026-10-04-property-grid-slice5-preflight.md`, including their
execution lessons:
- real-click `PickInCombo`;
- the popup `RequestBringIntoView` guard on ROW CONTAINERS;
- the overlay scrollbar, which puts buttons LEFT of the editors;
- the ONE selection store;
- rows refreshed after a host write (slice 5's CRITICAL: a stale cell's LostFocus unbound the stub just written);
- one undo step per gesture;
- the Events-tab gesture queue;
- `FormCodeScan` on the lexer.

Made against **`86897991`** (slice 5 merged to master). Branch `feat/property-grid-slice6`, worktree `scratchpad\wt-pg6`.
Follow this document **alongside** the plan. Where the two disagree, **this document wins**. The owner delegated every
decision ("go with your recommendations"; match Visual Studio's multi-select property grid). Each decision below names the
alternative that lost and why, so the owner can overrule it.

Every file:line below was read on `86897991`. Line numbers drift, so re-anchor by the quoted symbol, not the number.

---

## 0. To measure in Task 0 (before any code; scratch probes, nothing in the repo)

| # | What | Why it decides something | How |
|---|---|---|---|
| M1 | `MergablePropertyAttribute.AllowMerge` for the WinForms property behind EVERY catalog row of every WinForms kind, the Form's rows, and the intrinsic `Location`, `Size`, `TabIndex`, `Anchor`, `Dock`. **Expected** (to be confirmed): `Items`/`Nodes`/`Columns`-shaped collections false, everything else true, `Location` true | D-2: which rows a multi-selection offers is VS's rule (`MergableProperty`), not a hand list. If `Location` turns out false, D-2's Location ruling flips with it | a throw-away `net8.0-windows` console in the scratchpad that reflects `TypeDescriptor.GetProperties(instance)` over the same kinds `tools/WinFormsMetadataDump/Program.cs:124-155` walks; print `kind.property = AllowMerge` |
| M2 | Single selection today: drag a selected control on the canvas, then nudge it with an arrow key. Does the grid's X row show the new number without reselecting? | `OnDesignModelRevisionChanged` (`CodeEditorDocumentViewModel.cs:1455-1461`) refreshes only Reference choices. If X goes stale, D-9's `RefreshValues` fixes a pre-existing single-select defect as well as the multi one (Arrange while three are selected) | a scratch `[AvaloniaTest]` on the real-view rig (`FormPropertyGridRealViewTests.Open`) |
| M3 | A real Ctrl+click on the real document view's canvas (rig + `RawInputModifiers.Control`, the `FormCanvasMultiSelectTests.cs:128` pattern) leaves `Selection.Controls` = [lbl, btn], `PropertyGrid.SelectedControl` = btn, and the grid rebuilt ONCE (count `Rows` resets) | D-1's ordering argument: the view model's `Selection.Changed` handler runs before the canvas's, and the canvas's TwoWay echo is a no-op | same rig |
| M4 | A real `ComboBox` (the Bool/Enum typed editor) bound to a `StringValue` of `""` (no matching item): does it push `null`, push `""`, or push nothing on bind, and on a real click away? | D-4: a mixed Bool/Enum row must never write on bind. `Commit` ignores null (`FormPropertyRow.cs:951`) but not `""`, and `""` on an Enum is Judge's RESET verdict | same rig, a hand-made mixed fixture |
| M5 | The real `NumericUpDown` (`TypedValueEditor.axaml:44-47`, `Value="{Binding IntValue}"`) for a row whose text is `""`: what does it show (expected `0`, `IntValue` reads `""` as 0, `FormPropertyRow.cs:779-783`), and does a focus-and-leave push it? | D-4: a mixed Int row becomes a TEXT box. A `0` shown for "mixed" is false, and a pushed 0 would clamp every Width to 1 | same rig |

Record each result with the base SHA in §6 before Task 1. ⛔ A decision below that a measurement contradicts is
re-decided under the same delegation and the change is recorded. Never code around a measurement.

## 1. Re-anchored facts (every claim the slice-6 plan and spec text make)

| Plan/spec claim | Where it is on `86897991` | Holds? |
|---|---|---|
| "today only `Selection.Primary` via `PropertyGrid.SelectedControl`, `CodeEditorDocumentViewModel.cs:1106`" (spec: `:1104-1106`) | The grid follows the store in the constructor's `Selection.Changed` handler, `CodeEditorDocumentViewModel.cs:1412-1431` (`PropertyGrid.SelectedControl = Selection.Primary` at `:1414`). The other writer is `SelectInDesigner` `:323-327` (`Selection.Set(control); PropertyGrid.SelectedControl = control;`) | ✅ claim; ❌ cite (stale) |
| `Selection.Controls` / `Selection.Primary` | `ViewModels/Designer/FormSelection.cs`: `Controls` `:25` (selection order, primary LAST), `Primary` `:28`, `Set` `:45-61`, `Add` `:64-73`, `Toggle` `:82-93`, `SetRange` `:96-110`, `Clear` `:112-121`. `Changed` is raised only on a real change (`:39`) | ✅ |
| "still fed only by the one store" | The grid's selector only REQUESTS (`SelectionRequested` `FormPropertyGridViewModel.cs:51`, answered at `CodeEditorDocumentViewModel.cs:1436`) | ✅ |
| (not in the plan) **the canvas's `SelectedControl` is TwoWay-bound to `PropertyGrid.SelectedControl`** | `CodeEditorDocumentView.axaml:257`. The canvas sets it to `Selection?.Primary` on every store change (`FormCanvasControl.cs:409-415`) and reads it for handles, the open dropdown's layout, hit-testing and rename arming (`:372-388`, `:965`, `:986-989`) | ❌ **blocker for a naive 6.1.** The grid cannot show "a multi-selection" by setting `SelectedControl` to null: the binding would push null into the canvas and drop its handles and open menu. `SelectedControl` must STAY the primary. A second property carries the set (D-1) |
| Tray Delete passes the grid's control | `CodeEditorDocumentView.axaml:292-293` (`CommandParameter="{Binding PropertyGrid.SelectedControl}"`), pinned by `FormPropertyGridViewTests.TheDocumentView_KeepsTheCanvasAndTrayBindings` `:1055` | ✅ unchanged: the tray selects one component at a time (`FormTrayViewModel.Select` `:81-85` is `Set`) |
| "geometry-backed Size" / "Name and Location not offered" | Intrinsic rows: `FormPropertyGridViewModel.AddIntrinsicRows` `:549-646`. Name `:568-575` (frozen); Location/Size composites over `PixelGeometry` `:586-593` (`GeometryComposite` `:653-680`); Anchor `:604-611`; Dock `:613-620`; Col/Row for `GridGeometry` `:624-625`; TabIndex for positioned controls `:640-645`. ⚠ Every intrinsic row's change callback is the local `Changed()` → `RaiseEdited` (`:551`), not a parameter | ✅ shape; ⚠ the callback must become a parameter (Task 2). Location: see D-2 (re-decided) |
| "same name AND same type" | `FormPropertyDef` (`BasicLang/Forms/FormControlCatalog.cs:230-249`). ⚠ `TextAlign` is `Enum` on BOTH a Button (`WinFormsEnumType: "ContentAlignment"`, `:1713-1714`) and a TextBox (`"HorizontalAlignment"`, `:1795-1796`). A `FormPropertyType` comparison would MERGE them, and writing `MiddleCenter` into a TextBox is a Judge refusal at best | ❌ **insufficient.** "Same type" must include the enum type and member set (D-2) |
| "value shown when equal on all; blank when mixed" | A row's shown value is `DisplayValue` (`FormPropertyRow.cs:688-691`: frozen → raw; catalog → `Displayed(present ? raw : null, target)`, so an ABSENT row shows its kind's default). ⚠ Defaults differ PER KIND (each kind's row carries its own `Default`), so "equal" must compare what each member SHOWS | ✅ with that reading (D-3) |
| "One edit = ONE undo step … `TextDocument.UndoStack`, one write → one replace, `:907-943`" (spec: `:931-961`) | `WriteDesignerEditBack` `CodeEditorDocumentViewModel.cs:1202-1238`: `Text = written` `:1223`, then `ReplaceContent(written)` `:1231`, which is `TextDocument.BeginUpdate(); Replace(0, len, …); EndUpdate()` (`:2116-2129`), ONE undoable operation. `Edited` → `OnDesignerEdited` → `WriteDesignerEditBack` (`:210`, `:251`). Undo `:1248-1257`, Redo `:1260-1269`, both through `AdoptDocumentText` `:1284-1303` | ✅ mechanism; ❌ cites stale |
| (not in the plan) **an undo re-parses and CLEARS the selection** | `AdoptDocumentText` → `SyncDesignerPanels` (`:1301`) → `Selection.Clear()` (`:235`, deliberate: the old parse's objects are ghosts) | ⚠ "ONE Ctrl+Z restores all" leaves NOTHING selected and the grid shows the Form. Tests assert the DOCUMENT, not the selection. VS keeps the selection across an undo → follow-up §44 (D-12) |
| `Edited` per row | Every row's commit ends `_onChanged()` (`FormPropertyRow.cs:1039`; `Reset` `:667`). The grid passes `RaiseEdited` (`FormPropertyGridViewModel.cs:425-435`), which raises `Edited` | ✅ so N per-control rows built with `RaiseEdited` = N writes = N undo steps. That is exactly the defect 6.3 forbids (D-5) |
| Composite parts | `FormCompositeRows.Attach` `:24-42`. A part writes the WHOLE value through its parent (`Part` `:44-56`, `parent.CommitFromPart`; the part's own `onChanged` is a no-op `:53`). Font `:83-110`, Padding `:121-140`, Size `:146-160` | ✅ per-member parts compose per member (D-3) |
| The slice-2 selector tests "assume a single selection" | `FormPropertyGridDisplayTests.TheObjectSelector_ListsTheFormEveryControlAndEveryComponent` `:259` and `PickingAnObject_RequestsTheSelection_AndNeverWritesSelectedControlItself` `:273` (also `PickingTheForm_RequestsNull` `:292`, `AnOutsideSelectionChange_…` `:304`, `ReselectingWhatIsAlreadySelected_…` `:325`) | ⚠ **They do not break.** Their fixture selects ONE control, and single-selection behaviour is unchanged. The RE-CHECK means: keep them green and add the multi twins (Task 2). Not a rewrite |
| Events-mode VM (slice 5) | `FormPropertyGridViewModel.RebuildEventRows` `:231-261` builds ONE `FormBindOwner(form, SelectedControl)` (`:247`). `FormEventRow` (`ViewModels/Designer/FormEventRow.cs`) holds one `Owner` (`:57`), its `Bind` (`:83-85`), `Commit` (`:106-151`), `RequestHandler` (`:154`). `FormHandlerRequest(Owner, Event, Handler)` `:11` | ✅ all single-owner. 6.4 needs an owner LIST (D-8) |
| (not in the plan) **a handler gesture collapses the selection** | `RunHandlerGestureAsync` ends with `SelectInDesigner(owner.Control)` (`CodeEditorDocumentViewModel.cs:910`), which is `Selection.Set`. The gesture queue's de-dupe key is `(owner.Control, owner.Form, evt, handlerName)` (`:786`) | ❌ in a multi-selection, a double-click in the Events tab would collapse the selection to one control. Task 5 fixes both |
| Keyboard / mouse multi-select exists | Ctrl+click and Shift+click both EXTEND (`FormCanvasControl.cs:965-967`) → `Toggle` (`ApplyClickSelection` `:806-837`); clicking a member of the group keeps the group and collapses on release only if the pointer did not move (`:831-833`, `:1271-1279`); a marquee selects POSITIONED controls only (`FormCanvasTransform.ControlsIn` `:174-190`; release `FormCanvasControl.cs:1247-1261`); Esc clears (`:580-595`). Tested: `FormSelectionTests` (16), `FormCanvasMultiSelectTests` | ✅ VS's Shift-adds / Ctrl-toggles difference and Ctrl+A are not implemented → follow-up §44 |
| Cut/Copy/Arrange/Reorder act on the selection | Arrange `:534-548` (reference = `Selection.Primary` `:544`, VS's rule), Reorder `:561-583`, Copy `:585-596`, Cut `:598-620` (one write), Paste selects all pasted `:729` | ✅ |
| (not in the plan) **canvas Delete and arrow keys act on the PRIMARY only** | Delete: `FormCanvasControl.cs:631-641` → `DeleteControl(control)` `CodeEditorDocumentViewModel.cs:282-302`, one control. Arrows: `:643-707` move/resize `SelectedControl` alone | ❌ VS deletes and nudges the whole selection. Task 6 (D-11) |
| Description pane / header | `Header` `:292`, `HeaderKind` `:298-299`, neither bound in `FormPropertyGridView.axaml` (only `DescriptionTitle`/`DescriptionBody` `:145-146`). `Header` titles a carried refusal (`_shownOwner` `:906`) | ✅ D-6 |
| Mixed Int rows | `IsNumericUpDown` `FormPropertyRow.cs:334`, `IsTextBox` `:349`; the NumericUpDown binds `int IntValue` (`TypedValueEditor.axaml:44`), which cannot say "empty". `ITypedValueRow` is a CORE interface (`VisualGameStudio.Core.Abstractions.ViewModels`), shared beyond the designer | ⚠ D-4: a mixed Int row renders the TEXT editor. The interface does not change |
| Codes | Slice 5: next free BL8039; BL8037 reserved for piece 2 | ✅ multi-select claims NO design code: nothing new reaches a build |
| ADRs | `docs/superpowers/decisions/` through 0021 | ✅ no ADR: nothing here is cross-backend or cross-feature (§3) |
| Follow-ups | `docs/form-designer-followups.md` ends at §43 | ✅ slice 6 records start at **§44** |

## 2. DECISIONS (owner delegated; VS behaviour where it is known; each names what lost)

### D-1 — The grid takes the SET; `SelectedControl` stays the primary (plan 6.1)

- `FormPropertyGridViewModel` gains `IReadOnlyList<FormControl> SelectedControls` (selection order, primary LAST, as
  `FormSelection.Controls`), `bool IsMultiSelection => SelectedControls.Count > 1`, and ONE entry point
  `SetSelection(IReadOnlyList<FormControl> controls)`. It copies the list, sets `SelectedControl = controls.LastOrDefault()`,
  and rebuilds EXACTLY ONCE.
  - It is a no-op when the list is element-wise reference-equal to the one the rows were built for.
  - `SelectedControl`'s own setter (the canvas's TwoWay echo) with a value that is not the current primary means
    "exactly that one control": `SelectedControls = [value]` (or `[]` for null). That keeps `Load`'s
    `SelectedControl = null` (`:516`) and any single-control path meaning what it means today.
- **Host:** the `Selection.Changed` handler (`:1414`) and `SelectInDesigner` (`:326`) both call
  `PropertyGrid.SetSelection(Selection.Controls)`. The view model no longer assigns `PropertyGrid.SelectedControl`
  anywhere; only the canvas binding does, and its echo is a no-op (M3). The `SelectInDesigner` doc comment's
  "load-bearing when Set is a no-op" case still holds: after `Load` the grid's list is `[]`, so `SetSelection([c])` rebuilds.
- A selection of ONE runs today's code path unchanged. Every existing grid test is the regression net.
- **Lost — `SelectedControl = null` meaning "several"** (the canvas binding would drop its handles: §1). **Lost — the grid
  subscribing to `FormSelection` itself** (a second subscriber whose order against the canvas's is not ours to control,
  and a grid that READS the store is one short step from writing it).

### D-2 — Which rows a multi-selection offers (plan 6.2; VS's merge rule)

A row is offered iff ALL hold, for every selected control:
1. **The same row.** Same `Name` (ordinal), same `FormPropertyType`, same `WinFormsEnumType`, the same `AllowedValues`
   sequence, and it applies on the document's target (`AppliesTo`). Kept in ONE predicate, `FormPropertyDef.SharesShapeWith(other)`.
   This is VS's "same name and same property TYPE": `ContentAlignment` and `HorizontalAlignment` are different types,
   so a Button + TextBox selection offers no `TextAlign`.
2. **Mergeable.** VS hides a property marked `[MergableProperty(false)]` in a multi-selection. That becomes catalog data:
   `FormPropertyDef.Mergeable` (default true), set false where the oracle says so. The oracle gains the measured
   attribute (M1, Task 1), and the parity test compares the two, so it cannot drift.
3. **Intrinsic rows** follow the same two rules, by geometry: Location/Size/Anchor/Dock iff every member has
   `PixelGeometry`; Col/Row iff every member has `GridGeometry`; TabIndex iff every member is positioned (the `:640`
   filter). **Name is never offered.** VS hides `(Name)` for a multi-selection, and a shared id is meaningless.
4. **Location IS offered** (re-decided against the spec's "not offered" — the spec line was a design guess, and the
   owner's delegation is VS parity). VS lists Location for a multi-selection because `Control.Location` is mergeable
   (confirmed or refuted by M1). And it is useful: X alone is "line these three up at 96", the gesture the grid's own
   comment (`:527-529`) says dragging is wrong for. Setting the whole pair stacks the controls, as VS does.
   **Lost — the spec's "Location not offered":** no VS behaviour supports it, and it removes the exact-coordinate
   alignment gesture. If M1 measures `AllowMerge=false` for Location, rule 2 removes it and this paragraph is struck.
5. Order: the PRIMARY's catalog order, intrinsic rows first (as today).

**Lost — "same name and same `FormPropertyType`"** (merges `TextAlign` across enum types). **Lost — a hand-written
"not in multi-select" list in the shell:** a second list beside the catalog (CLAUDE.md's rule). The attribute is
measured, so the gate can see a wrong row.

### D-3 — Values: shown when every member SHOWS the same; blank otherwise (plan 6.2)

- A merged row's text is the members' `DisplayValue` when they are all equal (ordinal, after each member's own canonical
  display), else `""` with `IsMixed = true`. So a Button and a Label with no `BackColor` show `Control` when both kinds
  default to `Control`, and blank when their defaults differ.
- **Grey** (`IsDefaultShown`): every member absent and not mixed. **Bold** and **Reset**: true when ANY member is bold /
  can reset. Spec §2.7's "bold, grey and reset read ONE value" holds across the set: Reset is offered whenever it would
  remove something, and bold says the same thing. A mixed row is never grey.
- **Frozen**: if any member's row is frozen (D9 Degraded), the merged row is frozen, its reason names the member
  (`'btn2': <reason>`), and it shows the shared raw text or blank. Nothing may coerce a preserved value, even in company.
- **Composites** (Font, Padding, Size, Location): the merged parent is blank when the WHOLE values differ. Each merged
  PART is computed the same way over the members' own parts. Two Labels with different fonts but both Bold show Name
  blank, Size blank, Bold `True`.
- **Lost — show the primary's value with a "mixed" marker:** VS shows blank, and a primary-coloured value invites a
  no-op edit that silently writes the primary's value onto everyone.

### D-4 — Editors on a mixed selection (VS behaviour; no editor ever writes on bind)

| Editor | Mixed shows | A commit writes |
|---|---|---|
| Text | blank | the typed text to every member, each JUDGED on its own (D-5) |
| Int (NumericUpDown) | ⚠ **the TEXT editor**, blank (`IsNumericUpDown` false and `IsTextBox` true while `IsMixed`; both raised when mixedness changes). `ITypedValueRow` unchanged | the parsed number to all; unparseable text is ignored (as `IntRow` does today) |
| Bool / Enum / Cursor combo | blank (no item selected). A `null` push is ignored today (`:951`). A `""` push from a mixed combo is ALSO ignored while mixed (M4 decides whether it can happen). ⛔ Never Judge's Reset verdict by accident | the picked member to all |
| Bool double-click | — | mixed → `true` for all (VS cycles to its first standard value); not mixed → flips, as today |
| Colour drop-down | no swatch, blank text | the picked colour to all |
| Font `…` dialog | starts from the shared font, or the PRIMARY's effective font when mixed | the dialog's whole font to all |
| Font/Padding/Size/Location PARTS | blank when the members' parts differ | the part ON EACH MEMBER's own value (each member's parent composes its own whole): Bold on two different fonts keeps each family and size |
| Anchor / Dock pop-ups | summary blank. The Anchor box starts from WinForms' default (Top, Left), which is what `FormAnchor.Parse("")` already answers (`:853`). Dock: no region lit | the WHOLE value to all (VS's editors edit the whole value) |
| Items `…` | not offered (expected non-mergeable, M1). If M1 says otherwise: starts empty when mixed, OK writes all | — |
| Image/Icon `…` | blank | the picked path to all (one copy into `Resources/`) |

**Lost — the Font dialog starting from the default font** (what VS's merged `null` value would give): starting from the
primary's font is the more useful start, and OK writes one whole font either way. **Lost — per-member Anchor edge
toggles:** VS's AnchorEditor is a whole-value editor. Per-member toggling is a second rule beside it.

### D-5 — One edit = ONE `Edited` = ONE write = ONE undo step, and all-or-nothing (plan 6.3)

- A merged row is a `FormPropertyRow` in a new MODE, not a new class. The factory is
  `FormPropertyRow.Merged(IReadOnlyList<FormPropertyRow> members, FormEditTally tally, Action onChanged)`. The view's
  templates, the display list, search, sort and collapse then work unchanged.
- Members are ordinary rows built with `onChanged: tally.Mark`, never `RaiseEdited`. That includes the intrinsic rows:
  `AddIntrinsicRows`/`IntRow`/`GeometryComposite` take the callback as a parameter.
- **Commit** (merged mode, after the null/frozen guards):
  1. **Pre-judge every member** (`member.Preview(value)`, the same `Judge`/`ToDocument` path `Commit` takes, writing
     nothing). If ANY member refuses, NOTHING is written. `Refusal` names the members (`'txt' (TextBox): <reason>`), and
     the editor snaps back (`RaiseEditorRefresh`). Example: a translucent BackColor on {Label, TextBox} is refused on the
     TextBox (`OpaqueOnWinForms`, per kind), so it is refused for the set.
  2. `tally.Reset()`, then each member commits (its own NoOp/Reset/Write verdict against its own present value).
  3. If `tally.Count > 0`: `RaiseValueChanged()` on the merged row (parts follow), then `onChanged()` **once**.
- **Reset** on a merged row resets every member that can reset, then raises one `Edited`. A merged PART pre-judges
  each member's composed whole, then commits through each member's part, with the same tally.
- **Lost — partial apply** (write where accepted, skip the refusals). VS cancels the whole designer transaction when one
  object throws, and a half-applied edit is a state the user cannot see from the grid.
  **Lost — writing each member and raising `Edited` per member, then coalescing undo steps:** the undo stack is the
  editor's (`ReplaceContent` = one operation). There is nothing to coalesce with, and N writes regenerate the
  document N times.

### D-6 — Selector, header, description (plan 6.2 "object selector blank")

- Multi: `SelectedObject = null`, so the combo is blank. `OnSelectedObjectChanged` already ignores a null push (`:489`).
  Picking an object in the blank selector REQUESTS that one control, which collapses the selection, as VS does.
  `Header`/`HeaderKind` = `""`. A carried refusal is titled `btn, btn2.BackColor` (`_shownOwner` = the ids, comma-joined).
- Description pane: unchanged text for a selected row. With no row selected:
  - "Select a property to see what it does." (Properties);
  - "The selected controls share no events on <target>." when the Events intersection is empty.

### D-7 — The Form, tray components, strips/items and mixed kinds in one selection

- **The Form is never in a multi-selection.** "Nothing selected" IS the Form (`SelectInDesigner(null)`), and the canvas
  clears on a background click. Same in VS. No special case.
- **Components** cannot join a multi-selection today: the tray only `Set`s, and the canvas never hits a component.
  The rules still hold generically, so a future tray Ctrl+click (§44) needs no grid change.
- **Strips and items** CAN join through Ctrl+click (the marquee excludes them). They have no geometry, so the
  intersection drops Location/Size/Anchor/Dock/TabIndex: D-2 rule 3, not a special case.
- **Different kinds**: the intersection by D-2. A Button + a Label shares Text, Font, BackColor, ForeColor, Enabled,
  Visible, Cursor, … and not TextAlign's enum mismatch cases. An empty intersection (it cannot be empty while two
  positioned controls share Location) shows the existing empty-grid text.

### D-8 — The Events tab for a multi-selection (plan 6.4; VS)

- **Rows:** events shared by every member — same WinForms `Name` AND same `WinFormsArgs`, each wired on the target
  through the seam (`FormEvents.WiredOn(definition, target)` per member, then intersected, never `definition.Events`).
  Order and category from the primary.
- `FormEventRow` takes `IReadOnlyList<FormBindOwner> Owners` (primary LAST, as the selection). `Owner` stays as the
  PRIMARY, so every single-owner caller and test keeps its meaning.
  - **Handler** shows the shared bound handler, or blank when the members differ (one unbound, two different).
  - **Choices** are the intersection of each owner's `FormHandlers.FittingHandlers`, in the primary's order, the SAME
    list instance while unchanged (the slice-4/5 rule).
- **Pick** a handler: bind it on EVERY owner (replacing each owner's existing bind of that event), ONE `Edited`.
- **Clear**: unbind on every owner, ONE `Edited`, code never deleted.
- **Type a new name**: `DescribeUnusableHandler` is asked for EVERY owner (the name of any member's id is refused). Then
  `HandlerRequested` carries all the owners; the host writes ONE stub and binds every owner.
- **Double-click**: the host plans for the PRIMARY exactly as a single selection does. It navigates to the primary's
  bound handler, or creates `<primaryId>_<Event>`, then binds that handler on every other owner. ONE stub, ONE
  `WriteDesignerEditBack`, ONE undo step. The handler's signature is the shared event's args, so it fits every member
  (web: `e As DomEvent`; the `VgsOn_` wrappers are generated per owner by the region writer and are unaffected).
- `FormHandlerRequest` gains `IReadOnlyList<FormBindOwner> Owners` (`Owner` stays = the primary). The host's de-dupe key
  includes every owner. The host's closing `SelectInDesigner(owner.Control)` (`:910`) becomes "keep the selection
  when the owner is already in it". A gesture must not collapse a multi-selection.
- **Lost — a neutral handler name** (`Buttons_Click`): VS names it after a component, and this codebase's reference
  control is the primary (Arrange already uses it). **Lost — "double-click navigates only when every member shares
  the handler":** the single-selection rule applied to the primary is one rule, and the user sees where they landed.

### D-9 — The grid follows the document while the selection stands

`PropertyGrid.RefreshValues()`: every row (and every merged row's members) raises `RaiseValueChanged`, and every Events
row raises `HandlerChanged`. It is called from `OnDesignModelRevisionChanged` beside `RefreshReferenceChoices` (`:1461`).
Arrange, a drag, an arrow nudge or a Paste while three controls are selected then shows the new shared Location
immediately. M2 says whether single selection had the same staleness; either way this is the one fix.
**Lost — rebuilding the rows on every revision:** a rebuild drops focus and the expanded-part state mid-edit, and would
re-run slice 5's stale-LostFocus hazard.

### D-10 — Web vs WinForms

No difference in the rules. The differences are data:
- a web Grid/Flow page offers Col/Row (geometry);
- a Canvas page and a `.blform` offer Location/Size/Anchor/Dock;
- the catalog's `Targets`/web defaults decide the rows and the values;
- the web Events intersection is over the web `WiredOn` lists.

Multi-select emits nothing new: no region writer, emitter or CSS change. The acceptance test proves the EDIT reaches a
running program on both targets.

### D-11 — Canvas gestures over the whole selection (beyond the plan; VS)

- **Delete** on the canvas deletes EVERY selected control in ONE write. `DeleteControl(control)`: a control that is part
  of a multi-selection deletes the whole selection, through `FormDocument.RemoveControl` each, then one
  `WriteDesignerEditBack`, so one undo step. The tray's Delete (always a single selection) is unchanged, so
  `TheDocumentView_KeepsTheCanvasAndTrayBindings` stays as it is.
- **Arrow nudge / Shift+arrow resize / grid-cell move** apply to every selected control that has geometry, each
  parent-relative (`MoveTo`, never re-parenting, the existing rule), with ONE `CommitGeometry`.
- **Lost — leaving both primary-only:** the grid makes a multi-selection visible and editable. Deleting one control of
  three is the surprise VS users will hit first.

### D-12 — Deferred, recorded as follow-ups (§44), not done

- The selection surviving undo/redo (VS keeps it; here an undo re-parses, `:235`).
- Tray Ctrl+click.
- Ctrl+A select-all.
- Shift-adds vs Ctrl-toggles (VS distinguishes; here both toggle).
- VS's white-handled primary marker.

## 3. Escalations

None sent to the architect. Nothing here touches a backend, the IR, the C++ std model, the engine sync invariant or the
layering between IDE projects. The one catalog change, `FormPropertyDef.Mergeable`, is an additive field judged by the
existing oracle machinery. No ADR.

## 4. The tasks (TDD, one commit per task)

Every task:
- Red test(s) first. Run them and see them fail for the RIGHT reason. Implement. Green.
- Mutation-check each new branch: apply with the **Edit** tool and **REBUILD** (a mutant survives a revert until you
  rebuild). Record each as killed or EQUIVALENT, with the measurement.
- Stage by name (never `git add -A`, never `csc.dll`). Message via a scratchpad file + `git commit -F`.
- Every real-view test:
  - `[AvaloniaTest]`, looping the rig's `TwoZooms` (800×560 below zoom 1.0, 1400×900 at 1.0);
  - REAL clicks: canvas Ctrl+click via `Window.MouseDown(at, MouseButton.Left, RawInputModifiers.Control)`, combos via
    `PickInCombo`, Ctrl+Z as a real key on the canvas;
  - every window closed.
- **One multi fixture, the same ids everywhere** (CLAUDE.md: ids differing only by text make a rendering test vacuous):
  `MultiDoc` = `GridForm` with `btn` and `btn2` (Buttons), `lbl` (Label), `txt` (TextBox), `tmr` (Timer, tray).
- Catalog-driven `TestCaseSource` only.
- **After any AXAML change: `dotnet clean` (Shell) before building.**
- Other agents build in other worktrees: build `VisualGameStudio.Tests` once per task, then run with `--no-build --filter`.

### Task 0 — Baseline and measurements

- Build `VisualGameStudio.Tests` Release at `86897991`.
- Run the fast subset with BOTH streams captured. Record the total/passed/failed/skipped counts and the **sorted failure
  NAMES** with the base SHA. Expected: only the known machine rows (§5).
- Run M1–M5 (§0), record them in §6, and re-decide anything they contradict.
- No commit, unless a decision changed (then this document, alone).

### Task 1 — The oracle measures mergeability; the catalog carries it (D-2 rule 2)

Files:
- `tools/WinFormsMetadataDump/Program.cs`: `DescribeProperty` emits `mergeable` from
  `p.Attributes[typeof(MergablePropertyAttribute)]`, true when absent.
- `tools/WinFormsMetadataDump/README.md`: what `mergeable` means.
- `VisualGameStudio.Tests/Data/winforms-metadata.json`: REGENERATED by the tool, never hand-edited.
- `VisualGameStudio.Tests/Compiler/WinFormsMetadata.cs`: `WinFormsPropertyEntry.Mergeable`.
- `BasicLang/Forms/FormControlCatalog.cs`: `FormPropertyDef.Mergeable = true`, and `SharesShapeWith(other)` (D-2 rule 1),
  set false on exactly the rows M1 names.
- `VisualGameStudio.Tests/Compiler/CatalogParity.cs`: `CompareProperty` reports a Mergeable disagreement. An
  `OracleExemption` covers it as it covers the default.

Tests (Edit the existing files; any new file is NEW):
- `WinFormsCatalogParityTests`: automatic, per kind, FormRoot included.
- New `FormMultiSelectCatalogTests` (fast):
  - every WinForms catalog row's `Mergeable` equals the snapshot's (catalog-driven);
  - the intrinsic `Location`/`Size`/`TabIndex`/`Anchor`/`Dock` vs the snapshot's `Control` entries, which D-2 rule 3
    relies on;
  - `SharesShapeWith` over EVERY pair of same-named rows across kinds (catalog-driven): `TextAlign`
    ContentAlignment × HorizontalAlignment is false, Button.BackColor × Label.BackColor is true;
  - the predicate is symmetric.

Mutations:
- the dump always writes `true` (the parity row for an `Items` row goes red);
- `SharesShapeWith` ignores `WinFormsEnumType` (the TextAlign pair);
- `Mergeable` dropped from the comparer (a flipped catalog row survives — must be killed by the parity cell).

RE-CHECK: `WinFormsCatalogParityTests.TheSnapshot_CoversEveryWinFormsKindInTheCatalog_AndTheForm`, `EveryOracleExemption_StillSuppressesAFinding`, the README's "When to regenerate".

### Task 2 — The grid takes the set: plumbing, the row set, values (VM; plan 6.1/6.2, D-1/D-2/D-3/D-6/D-7)

Files:
- `ViewModels/Designer/FormPropertyGridViewModel.cs`:
  - `SelectedControls`, `IsMultiSelection`, `SetSelection`;
  - `Rebuild` branches: one control → today's path; several → merged rows;
  - `AddIntrinsicRows`/`IntRow`/`GeometryComposite` take the change callback;
  - selector/header per D-6;
  - `RebuildEventRows` shows no rows in multi until Task 5, with the D-6 text.
- `ViewModels/Designer/FormPropertyRow.cs`:
  - the merged MODE (`Merged` factory, `IsMixed`, `DisplayValue`/`RawValue`/`IsPresent`/`IsBold`/`IsDefaultShown`/
    `CanReset`/`FrozenReason` over members);
  - merged children over the members' children by index;
  - **commit NOT yet**: a merged row is frozen "multi-edit lands in Task 3" in this commit only.
- New `ViewModels/Designer/FormEditTally.cs`.
- `ViewModels/Documents/CodeEditorDocumentViewModel.cs`: the `Selection.Changed` handler and `SelectInDesigner` call
  `PropertyGrid.SetSelection(Selection.Controls)`, and nothing else in the class writes `PropertyGrid.SelectedControl`.

Tests (new `FormPropertyGridMultiSelectTests`, fast, `MultiDoc`):
- the row set for {btn, btn2}, {btn, lbl}, {btn, txt} (no `TextAlign`; no `Name`; `Location` present per D-2);
- catalog-driven: for EVERY pair of kinds in `FormControlCatalog.For(target)` × both targets, the offered names equal
  the D-2 intersection computed independently from the catalog (`SharesShapeWith` + `Mergeable` + geometry). The
  merged rows' order is the primary's;
- equal values shown, a differing value blank + `IsMixed`, two absent rows of kinds with different defaults blank;
- grey only when all absent; bold/Reset when any;
- a frozen member freezes the set with the member named;
- a strip in the selection drops the geometry rows;
- `SetSelection` rebuilds once (count `Rows` resets) and is a no-op for the same list;
- the canvas echo (`SelectedControl = primary`) does not rebuild; `SelectedControl = other` means `[other]`;
- selector blank in multi and a pick requests one control;
- the single-selection grid is unchanged (the existing `FormPropertyGrid*` suites green, named in the gate).
- `FormPropertyGridDisplayTests` twins: `TheObjectSelector_IsBlank_ForAMultiSelection`,
  `PickingAnObject_InAMultiSelection_RequestsOnlyThatControl`.

Mutations:
- intersection by name only (the TextAlign pair; the catalog sweep);
- `Mergeable` ignored (an Items row offered, if M1 found one);
- blank-when-mixed removed (shows the primary's value);
- grey when mixed;
- `SetSelection` rebuilds twice;
- the host still writes `SelectedControl` directly (a source test: `CodeEditorDocumentViewModel.cs` contains no
  `PropertyGrid.SelectedControl =`).

RE-CHECK: `FormPropertyGridDisplayTests` (all five selector tests), `FormPropertyGridTests`, `FormPropertyRowDefaultTests`, `FormEventGrid*`, `FormSelectionTests`, `FormTrayViewTests`.

### Task 3 — The multi-edit: ONE Edited, all-or-nothing, parts, editors (VM; plan 6.3, D-4/D-5/D-9)

Files:
- `FormPropertyRow.cs`:
  - `Preview(value)` (Judge + `ToDocument`, no write);
  - merged `Commit`/`Reset`/parts per D-5;
  - mixed editor flags (`IsNumericUpDown`/`IsTextBox` while `IsMixed`) and the mixed-combo `""` guard (D-4/M4);
  - Bool double-click on mixed;
  - Font dialog start / Anchor / Dock whole value.
- `FormPropertyGridViewModel.cs`: `RefreshValues()`.
- `CodeEditorDocumentViewModel.cs`: call it from `OnDesignModelRevisionChanged`.

Tests (`FormPropertyGridMultiSelectTests` + `FormCompositeRowTests`, fast):
- **`Edited` raised exactly once** for a Text, Int, Bool, Enum, Color, Font (whole), Font PART, Padding part, Size
  part, Location X, Anchor, Dock and Reset multi-edit. A catalog-driven case over every shared row type of
  {btn, btn2, lbl}: commit a valid non-default value and count `Edited == 1`, and every member then holds it;
- through the REAL document view model: one multi-edit → `TextDocument.UndoStack` grows by ONE, and ONE
  `UndoDesignerEditCommand` restores every member's attribute byte-for-byte (document text equal to the pre-edit text);
- no-op: a value every member already shows raises nothing and writes nothing (undo depth unchanged);
- all-or-nothing: translucent BackColor on {lbl, txt} → refused, the reason names `txt`, neither written, no `Edited`;
- per-member judging: a value equal to btn's present value and different from btn2's writes btn2 only, still ONE `Edited`;
- the Bold part on two different fonts keeps each family/size;
- a mixed Int row is a text box and `""`/unparseable writes nothing;
- a mixed Bool/Enum combo push of `""`/null writes nothing (VM half of M4);
- `RefreshValues` after an `Arrange` → the merged Location shows the new value.

Mutations:
- **Edited raised per member** (`onChanged: RaiseEdited` on members): the count tests and undo depth 2;
- pre-judge skipped (partial apply: the {lbl, txt} test);
- merged NoOp judged on the primary only (the per-member test);
- parts compose from the primary's whole (the Bold-part test: btn2's family lost);
- the mixed-Int text-box switch removed (the 0 push clamps Widths: the real-view test in Task 4 too);
- `RefreshValues` not called (the Arrange test).

RE-CHECK: `FormCompositeRowTests` (`ABoldPart_PushedItsOwnShownItem_…`), `FormPropertyGridTests` refusal rows, `FormPropertyRowDefaultTests`.

### Task 4 — The real view: multi-select edits at two sizes, one Ctrl+Z (plan "Tests"; spec §8 "Real view")

Files: `FormPropertyGridView.axaml`, only if a mixed-state visual is needed (e.g. the text-box switch already bound;
no new template expected). `dotnet clean` if touched.

Tests (new partial `FormPropertyGridMultiSelectRealViewTests.cs` of the `FormPropertyGridRealViewTests` rig, `MultiDoc`,
`TwoZooms`):
- (a) a real click on `btn`, a real Ctrl+click on `btn2` → the store holds both, the selector is blank, and the list
  shows `Location` and not `Name`;
- (b) type `Go` into the merged Text row and click away (real input) → both buttons carry `Text="Go"` in the document,
  the undo stack grew by ONE, and a REAL Ctrl+Z on the canvas restores both (document text byte-identical to before);
- (c) `PickInCombo` on the merged `Enabled` → `False` on both, one undo step;
- (d) Ctrl+click `lbl` too: `BackColor` blank when they differ; the colour drop-down's Web → Red applies to all three;
- (e) a mixed `Width` (btn 75, lbl 100): the editor is a TEXT box showing blank. Focus it and click away → nothing
  written (no dirty flag, undo depth unchanged). Type `90` → all three are 90;
- (f) the merged Font: expand, real-click the Bold part's combo → each control keeps its own family;
- (g) Arrange (align lefts) while selected → the merged Location shows the new X without reselecting (D-9);
- (h) selecting a mixed Bool/Enum row and clicking away writes nothing (M4's real half).

Plus:
- `FormPropertyGridViewTests.EveryBindingInTheGridView_ResolvesAgainstTheTypeInScope` (automatic; any new binding);
- `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…` extended: every merged row's editor of a {btn, lbl} selection
  fits between divider and edge at two sizes.

Mutations:
- the Task-3 "Edited per member" mutant must also turn (b) red (undo depth 2, one Ctrl+Z restores only one);
- the selector not blanked ((a));
- the text-box switch removed ((e): 0 pushed);
- the popup guard skipping merged rows (the second drop-down in (d)). If it does not reproduce headless, record it
  EQUIVALENT with the measurement, as slice 5 did.

RE-CHECK: `FormPropertyGridRealViewTests` (all), `FormPropertyGridEditorRealViewTests`, `TheDocumentView_NoLongerCarriesTheGridsOwnBindings`.

### Task 5 — The Events tab for a multi-selection (plan 6.4, D-8)

Files:
- `FormEventRow.cs` (`Owners`, shared handler, intersected choices with the same instance, commit over owners with ONE
  `Edited`, `FormHandlerRequest.Owners`);
- `FormPropertyGridViewModel.RebuildEventRows` (the intersection through `WiredOn` per member);
- `CodeEditorDocumentViewModel.RunHandlerGestureAsync`/`ActivateHandlerAsync`: plan for the primary, bind every owner,
  ONE `WriteDesignerEditBack`, the de-dupe key over all owners, no selection collapse (`:910`);
- `FormHandlers.DescribeUnusableHandler` asked per owner (a loop at the call sites, not a signature change).

Tests:
- `FormEventGridTests` (fast):
  - rows = the intersection of `WiredOn` per member for every pair of kinds × target (catalog-driven);
  - a web {Panel, Button} has no Paint;
  - mixed handlers blank;
  - choices = the intersection, the same instance;
  - pick → both bound, ONE `Edited`, `.bas` untouched;
  - clear → both unbound, code untouched;
  - a typed name that is `btn2`'s id is refused;
  - a typed new name raises one request carrying both owners.
- `FormHandlerGestureTests`: double-click with {btn, btn2} → ONE stub `btn2_Click` (primary = last selected),
  `<Bind Event="Click" Handler="btn2_Click"/>` on BOTH, ONE undo step, the selection still holds both; a primary already
  bound to `AnyClick` → navigate, and btn gets `AnyClick` too.
- Real view (`FormPropertyGridEventsRealViewTests`, `TwoZooms`):
  - Ctrl+select two Buttons, click the bolt, double-click the empty Click value → the disk `.bas` gains ONE
    `Private Sub btn2_Click(sender As Object, e As EventArgs)`, both binds, the cell shows it after a click on the canvas
    (slice 5's CRITICAL, re-run in multi);
  - one real Ctrl+Z removes both binds;
  - a web pair: `btn2_Click(e As DomEvent)` above the region, both bound `click`.

Mutations:
- rows from the primary only (a {Button, TrackBar} selection offers Click);
- bind on the primary only (both-bound asserts);
- one `Edited` per owner (undo depth);
- the selection collapses after the gesture;
- the de-dupe key on the primary only (two different multi requests: one dropped).

RE-CHECK: `FormEventGrid*`, `FormHandlerGestureTests`, `FormHandlerPlanTests`, the slice-5 real-view rows (single selection unchanged).

### Task 6 — Canvas gestures over the whole selection (D-11)

Files:
- `CodeEditorDocumentViewModel.DeleteControl` (multi → every selected control, one write);
- `Controls/FormCanvasControl.cs` arrow/Shift+arrow/cell nudge over `SelectedSet` (`:421-426`) with one
  `CommitGeometry`.

Tests:
- `FormCanvasKeyboardTests` / `FormCanvasMultiSelectTests`: Delete with three selected removes three; ONE
  `UndoDesignerEdit` restores three; arrow moves both by 1 and Ctrl+arrow by the grid step, parent-relative, one undo
  step; Shift+arrow resizes both; a web Grid page moves both one cell;
- `FormTrayViewTests` unchanged (tray Delete removes the one component).

Mutations: Delete primary-only; nudge primary-only; one commit per control (undo depth).

RE-CHECK: `FormCanvasKeyboardTests`, `FormCanvasMultiSelectTests`, `FormTrayViewTests`, `FormStripCanvasTests` (Delete on an item).

### Task 7 — RUN, not compile: a multi-edit on both targets and both build entry points

New Integration `FormMultiSelectAcceptanceTests` (Compiler folder; model `FormEventAcceptanceTests` and
`FormPropertyBatchAcceptanceTests`):

- **Design.** The form is made through the REAL document view model on a real temp folder. `btn` and `btn2` are placed
  and selected TOGETHER through the store (`Selection.SetRange`, the marquee's route). Through the grid's MERGED rows:
  - `Text = "Go"`;
  - `BackColor = Red`;
  - Font Bold (a part edit);
  - `Width = 90` (the Size part).

  The Events tab's merged Click double-click → ONE `btn2_Click` bound on both, whose user line appends to a Label.
  Then `SaveAsync`.
- **Build:** the CLI (`BasicLang.exe build`) and `BuildService.BuildProjectAsync`.
- **WinForms run** (`[Platform(Include="Win")]`, skips without dotnet): the reflection driver `Show()`s the form (⚠
  `PerformClick` needs visible+enabled), prints both buttons' `Text`, `BackColor`, `Font.Bold`, `Width`, raises
  `OnClick` on each, and prints the Label. Assert both buttons are identical, and the handler ran twice.
- **Web run:** node with `FormEventWebRunTests.Harness` (Edge where present): both elements' text and computed
  `background-color`/`font-weight`/`width` from the page CSS; dispatching `click` on each runs the handler twice; the page
  exists on BOTH routes.

Mutations:
- members written with per-member `Edited` — the design step asserts the undo depth first, so the run is not the only
  witness;
- bind on the primary only (one click does nothing: count 1);
- the IDE route alone passing no forms (`BuildService`: web/IDE "no page" while CLI is green — the both-entry-points kill).

⚠ `JsExecutionTierRosterTests.RosterIsPinned`: +1 if the fixture runs node in-fixture (roster it, and re-read the pin
at merge: it collides SILENTLY, MEMORY). The roster guard also sweeps an `Integration` fixture named `*ExecutionTests`;
this one is `*AcceptanceTests`.

### Task 8 — Slice gate, records, mutation ledger

- Execution notes per task in §6. Each records: base SHA, deviations, red evidence, RE-CHECK results, the mutation table.
- `docs/form-designer-followups.md` §44: D-12's deferred list, plus anything found.
- The slice gate: §5. `dotnet clean` (Shell) first.

### Task 9 — Closing the property-grid programme (plan "## Closing (after slice 6)", made concrete)

1. **Records.**
   - `docs/HANDOFF.md` NEWEST section: slice 6 plus "the property-grid programme is COMPLETE". It names where each
     slice's record lives (slice 2 `2026-09-26-…-slice2-preflight.md`, slice 3 `2026-09-29-…`, slice 4 `2026-10-04-…`,
     slice 5 `2026-10-04-…-slice5-…`, slice 6 this file) and the gates.
   - The parent plan's slice-6 heading gets the "EXPANDED AND EXECUTED" banner, as slice 3's has (`:5985-5986`).
   - The auto-memory line for the programme is updated by the coordinator (memory does not travel; HANDOFF is the record).
   - ⚠ HANDOFF NEWEST sections conflict on EVERY merge (slice-4 lesson): write this LAST, after the trial merge.
2. **IDE drop** (Windows): `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E`, never `/MIR`. Confirm
   `IDE\lib\js\dom-core.bli` is present and byte-identical (LOAD-BEARING). Run from
   `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe`.
3. **FULL suite** (`dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`, no filter, `--blame`,
   both streams to a file, clean Release build; about 6 h on this machine):
   - Compare **sorted failure NAMES**, never counts, against master's own. The known list at this base:
     - the machine rows `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
       `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`
       (intermittent), `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`, `SearchSnippets_EmptyQuery_ReturnsAll`,
       `SearchSnippets_WhitespaceQuery_ReturnsAll`, `ReadingAnMvidTakesNoLockOnTheFile`;
     - the culture row `CppDoubleFormattingTests.Expected_IsWhatDotNetPrints` ("∞" vs "Infinity");
     - the flaky `NonEx_variants_marshal_and_are_screen_size_dependent`;
     - the inherited master-alone rows of the `758f1e0d` full run (HANDOFF "Failing on Windows on MASTER ALONE", 14
       names, `:1104-1114`), as far as they still fail;
     - **#134's C++ Release-pipeline rows that need MSVC** (HANDOFF `:98` "Windows still owes the MSVC leg":
       `CppProjectOptimizerPipelineRouteTests` `Cli_*`/`Ide_*`, `CppReleaseProjectExecutionTests`). They are NOT
       measured on Windows yet, so any of them failing is A/B'd, never assumed inherited.
   - Any name not on that list: re-run alone (`--no-build --filter`), then A/B on a `git worktree add --detach` of
     `origin/master` (never `git merge-tree`). Only a failure that fails identically on master is inherited.
4. **Real merge check:** `git worktree add --detach <dir> <branch sha>`, `git merge origin/master` in it, read the result,
   build + the fast subset on the merged tree, remove the worktree (`git worktree prune` after "Filename too long").
   Expect conflicts:
   - `FormPropertyGridViewModel.cs`/`FormPropertyRow.cs` (piece 2 if it lands first);
   - `winforms-metadata.json` (any regeneration);
   - `JsExecutionTierRosterTests` (the pin);
   - HANDOFF NEWEST.
5. **Merge only with the owner.** Never push from this task without the coordinator.
6. **The owner's click-through for the WHOLE programme**, in the IDE above. Each step names its slice.

   Slice 2 — the grid:
   1. Select a Button: categories with +/− headers, Categorized ⇄ A-Z, search "back" filters to BackColor; the
      description pane explains the highlighted row.
   2. An unset property shows its default greyed; set Text → bold; right-click → Reset removes it (the `.blform` loses
      the attribute); clearing a Color row resets; typing `Bogus` into BackColor is refused, the reason in the pane, the
      box snaps back.
   3. Click the form background: the Form's rows (ClientSize, Text, FormBorderStyle, …); the object selector picks any
      control and the canvas follows.

   Slice 3 — the D1 batches and composites:

   4. Form FormBorderStyle/StartPosition/Opacity (type `80%`), Font; F5 — the window shows them.
   5. A TextBox's Multiline/ReadOnly/PasswordChar, a CheckBox's CheckState/ThreeState, a ComboBox's DropDownStyle; F5 on
      WinForms and on a web form (CssClass/Style present only on the web).
   6. Expand Font (Name/Size/Bold/Italic/Underline), Padding (All/sides), Size and Location (Width/Height, X/Y): each
      part edits one value, one undo step each.

   Slice 4 — the editors (HANDOFF `:157-169`, abridged):

   7. Colour: Custom / Web / System; a TextBox offers no transparency, a Label does; a web form's System tab shows 8.
   8. The Font `…` dialog with live preview, then one Ctrl+Z; Bool True/False and double-click toggles; Anchor/Dock
      pop-ups (Esc closes); AcceptButton lists the Buttons and `(none)`.
   9. ComboBox Items `…` with `Smith, John` and `Beta` → two items on both targets; PictureBox Image from outside the
      project → copied into Resources, shown on both targets; Form Icon; delete the png and build → BL8036 at the
      `Image=` line, and the build succeeds.

   Slice 5 — the Events tab (HANDOFF `:51-70`, abridged):

   10. The bolt lists a Button's events; double-click Click → `btnX_Click` opens and stays wired after clicking the canvas;
       F5 runs it. MouseDown gives `MouseEventArgs`. An existing fitting Sub is offered and picking it leaves the code
       untouched. Clearing unbinds and Ctrl+Z restores. Type `DoIt` + Enter creates it; `Dim` is refused.
   11. Double-click the form surface → `<Form>_Load` (outside the form: nothing). A web TextBox KeyPress runs for
       letters/Enter/Backspace/Esc only. A web Panel's Enter ignores tabbing between its own children.
   12. Retarget a WinForms form with Load + MouseDown to the web: it builds, both run, FormClosing is reported.

   Slice 6 — multi-select:

   13. Click `button1`, Ctrl+click `button2`: the object selector goes blank; Name disappears; Location, Size, Text,
       BackColor, Font… remain; a property that differs shows blank.
   14. Type `Go` into Text: both change; ONE Ctrl+Z restores both.
   15. Ctrl+click a Label too: BackColor → Web → Red colours all three; Font → Bold keeps each control's own family;
       Width `90` sizes all three; a blank Width box left without typing changes nothing.
   16. A Button + a TextBox: no TextAlign row (their alignments are different types).
   17. Set Location X to `96` on three controls: their left edges line up; align-lefts from the toolbar and the X row
       follows without reselecting.
   18. Events with two Buttons selected: double-click Click → ONE `button2_Click` (the last one clicked), both wired;
       F5, both buttons run it; clear the cell → both unwired, the Sub stays; Ctrl+Z restores both.
   19. Delete with three selected removes all three; Ctrl+Z brings all three back. Arrow keys move the group together.
   20. A web form: steps 13–15 and 18 again; open the page, both elements styled, both clicks run the handler.

## 5. Gate for the slice (Task 8)

- **Fast subset** (`--filter "TestCategory!=Integration"`, Release, both streams captured): the **sorted failure NAMES**
  equal Task 0's, base SHA stated. Known machine rows only: `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind` (intermittent), `SearchSnippets_EmptyQuery_ReturnsAll`,
  `SearchSnippets_WhitespaceQuery_ReturnsAll`, `ReadingAnMvidTakesNoLockOnTheFile`. Re-run named rows with
  `--no-build --filter`, because a passing test prints nothing.
- **Named Integration set**, 0 unexpected skips on Windows:
  - new: `FormMultiSelectAcceptanceTests`;
  - slice 5's 14 classes: `WinFormsCatalogSweepTests`, `FormEventWebRunTests`, `FormEventAcceptanceTests`,
    `FormDesignerAcceptanceTests`, `FormMenuAcceptanceTests`, `FormComponentAcceptanceTests`, `FormBuildEmissionTests`,
    `FormPropertyBatchAcceptanceTests`, `FormItemsAcceptanceTests`, `FormImageAcceptanceTests`, `FormRetargetPairTests`,
    `WebMainStartupTests`, `JavaScriptProjectBuildTests`, `BuildServicePipelineTests`.

  ⚠ The inverse-gated `Build_CppLanguageProject_NoToolchain_…` skip is expected. ⚠ JS web-build rows'
  `ERROR_USER_MAPPED_FILE`: compare by NAME. ⚠ A filter term matching "Form" also matches `…Formatting` (the culture
  row): name it, don't count it.
- Then Task 9 (the programme's Closing): IDE drop, FULL suite, real merge check, records, click-through.

## 6. Execution notes

(Filled per task as slices 4/5 did: base SHA, M1–M5 results, deviations, red evidence, RE-CHECK results, the mutation
table. A decision a measurement re-decided is recorded here with the measurement.)

## 7. Tests to re-check (consolidated)

| Test | Why | Task |
|---|---|---|
| `WinFormsCatalogParityTests` (fast) | judges the new `Mergeable` per row; the regenerated oracle | 1 |
| `FormPropertyGridDisplayTests` selector rows `:259`, `:273`, `:292`, `:304`, `:325` | single-selection fixtures, STAY green (the plan's "assume a single selection" is not a break); multi twins added | 2 |
| `FormPropertyGridTests`, `FormPropertyRowDefaultTests`, `FormCompositeRowTests` | `FormPropertyRow` gains a mode; `AddIntrinsicRows` takes a callback | 2, 3 |
| `FormSelectionTests`, `FormCanvasMultiSelectTests` | the store and its gestures unchanged; Task 6 adds rows | 2, 6 |
| `FormPropertyGridViewTests.TheDocumentView_KeepsTheCanvasAndTrayBindings` `:1055` | the canvas TwoWay + tray Delete bindings must NOT change (D-1, D-11) | 2, 6 |
| `FormPropertyGridViewTests.EveryBindingInTheGridView_…` `:181`, `TheDocumentView_NoLongerCarriesTheGridsOwnBindings` `:410` | any AXAML touched | 4 |
| `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…` `:265` | the merged rows' editors | 4 |
| `FormPropertyGridRealViewTests.SelectOnCanvas` `:273-278` (asserts `GridVm.SelectedControl`) | still the primary (D-1) | 2, 4 |
| `FormEventGridTests`, `FormHandlerGestureTests`, `FormHandlerPlanTests`, `FormPropertyGridEventsRealViewTests` | `FormEventRow.Owners`, the host's owner list, no selection collapse | 5 |
| `FormCanvasKeyboardTests`, `FormTrayViewTests`, `FormStripCanvasTests` | Delete/nudge over the set; tray Delete single | 6 |
| `JsExecutionTierRosterTests.RosterIsPinned` | +1 if Task 7 runs node in-fixture | 7 |

## 8. Plan-text errors found (each corrected above)

1. 6.1's cite `CodeEditorDocumentViewModel.cs:1106` (spec `:1104-1106`) is stale: the grid follows the store at
   `:1412-1414` and in `SelectInDesigner` `:323-327`.
2. 6.3's cite `:907-943` (spec `:931-961`) is stale: `WriteDesignerEditBack` `:1202-1238`, `ReplaceContent` `:2116-2129`,
   undo/redo `:1248-1269`, `AdoptDocumentText` `:1284-1303`.
3. **6.1 cannot be done by changing what `SelectedControl` holds.** The canvas's `SelectedControl` is TwoWay-bound to it
   (`CodeEditorDocumentView.axaml:257`), so the grid needs a second property, and `SelectedControl` stays the primary (D-1).
4. **"Same name AND same type" merges different enum types**: `TextAlign` is `Enum` on a Button (ContentAlignment) and a
   TextBox (HorizontalAlignment). "Same type" must include `WinFormsEnumType` and the member set (D-2).
5. **"Name and Location not offered"**: Location is offered by VS (`Control.Location` is mergeable, to be confirmed by
   M1) and is the exact-alignment gesture. Re-decided (D-2). Only Name is never offered.
6. VS's rule also hides `[MergableProperty(false)]` properties (collections such as Items), and the plan's rule does not
   mention it. It is now catalog data judged by the oracle (D-2, Task 1).
7. Per-kind defaults and per-kind translucency rules mean one value can be a no-op on one member, a write on another, and
   a refusal on a third. The plan's "one edit applies to all" needed per-member judging + all-or-nothing (D-5).
8. "One Ctrl+Z restores all" is true of the DOCUMENT, but an undo re-parses and clears the selection (`:235`); tests
   assert the document. VS keeps the selection (follow-up).
9. 6.4 is not just "binds all": `FormHandlerRequest` has one owner, the gesture queue keys on one control (`:786`), and
   the host collapses the selection after a gesture (`:910`) (D-8).
10. The plan's RE-CHECK of the slice-2 selector tests implies a rewrite. They select ONE control and stay valid; multi
    twins are added instead.
11. Not in the plan: canvas Delete and arrow nudges act on the primary only while Cut/Copy/Arrange act on the whole
    selection (D-11). The grid does not refresh values on a model revision (D-9; M2 decides whether single selection was
    also stale).
12. Not in the plan: a mixed Int row cannot be shown by the NumericUpDown (`int IntValue`; `ITypedValueRow` is a Core
    interface), so it becomes a text box (D-4).
