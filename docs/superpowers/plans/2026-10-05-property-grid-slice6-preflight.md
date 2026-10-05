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
| M1 | `MergablePropertyAttribute.AllowMerge` (⚠ .NET's real spelling is "Mergable", one e; our catalog field is spelled `Mergeable`) for the WinForms property behind EVERY catalog row of every WinForms kind, the Form's rows, and the intrinsic `Location`, `Size`, `TabIndex`, `Anchor`, `Dock`. **Expected** (to be confirmed): **`TabIndex` false** (`Control.TabIndex` carries `[MergableProperty(false)]`); `Items`/`Nodes`/`Columns`-shaped collections false; everything else true, `Location` true | D-2: which rows a multi-selection offers is VS's rule (`MergableProperty`), not a hand list. If `Location` turns out false, D-2's Location ruling flips with it. TabIndex false → TabIndex is NOT offered for a multi-selection (D-2 rule 3), and Task 2's row-set tests expect that | a throw-away `net8.0-windows` console in the scratchpad that reflects `TypeDescriptor.GetProperties(instance)` over the same kinds `tools/WinFormsMetadataDump/Program.cs:124-155` walks; print `kind.property = AllowMerge` |
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
   `PixelGeometry`; Col/Row iff every member has `GridGeometry`. **Name is never offered.** VS hides `(Name)` for a
   multi-selection, and a shared id is meaningless. **TabIndex is never offered either** (coordinator ruling, VS):
   `Control.TabIndex` is `[MergableProperty(false)]` — M1 confirms it, and `FormMultiSelectCatalogTests` pins the
   intrinsic decision against the snapshot's `Control.TabIndex` entry. If M1 measured it mergeable, that test fails
   and the decision is re-opened, never silently kept. A shared TabIndex would also hand every member the same tab
   stop.
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
- **Grey** (`IsDefaultShown`): every member absent and not mixed. A mixed row is never grey.
- **Bold** and **Reset** follow VS's merged descriptor (coordinator ruling): **bold = ANY member bold** (VS's merged
  `ShouldSerializeValue` is true when any object would serialize); **Reset offered = ALL members can reset** (VS's merged
  `CanResetValue` requires every object). Each member's own bold/reset still reads its ONE value (spec §2.7); only the
  combination differs. Consequence, stated so nobody "fixes" it: a selection where one member is set and one is absent
  is BOLD but offers NO Reset (the absent member cannot reset). **Lost — Reset when ANY member can reset** (my first
  draft): it is not VS's rule, and the owner's delegation is VS parity.
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
| Int (NumericUpDown) | ⚠ **the TEXT editor**, blank (`IsNumericUpDown` false and `IsTextBox` true while `IsMixed`; both raised when mixedness changes). `ITypedValueRow` unchanged. ⛔ A row that becomes UN-mixed while its text box has focus (another route wrote the members equal — an Arrange, a revision refresh) swaps editors under the focus: the dying TextBox's LostFocus pushes its stale `""` and that push must write NOTHING (blank on a no-longer-mixed row is not an edit: the guard is "a `""` push from the mixed editor is ignored", keyed on the editor having been the mixed one, not on today's mixedness) | the parsed number to all; unparseable text is ignored (as `IntRow` does today) |
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
- **Reset** on a merged row (offered only when EVERY member can reset, D-3) resets every member, then raises one
  `Edited`. A Reset VERDICT from a cleared editor follows the same rule: when some member cannot reset, the editor snaps
  back and nothing is written (all-or-nothing). A merged PART pre-judges
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
- `PropertyGrid.RefuseHandler(owner, evt, …)` (`FormPropertyGridViewModel.cs:189-194`, today matched on
  `r.Owner.Control`) finds the row whose `Owners` CONTAIN the refused owner — so a host refusal of a typed name in a
  multi-selection lands on the merged row's pane and reverts its cell, never in the Error List.
- **The canvas double-click on a MEMBER of a multi-selection** (coordinator ruling, VS; the same rule as the Events
  tab): `OnCanvasDoubleTapped` (`FormCanvasControl.cs:717-744`) calls `SelectForGesture(control)`, which already keeps
  the group for a member (`:864-868`) — and with D-13 the first press made the clicked control the PRIMARY. Then
  `ActivateControlAsync` (`CodeEditorDocumentViewModel.cs:750-754`) passes, when the control is in a multi-selection,
  EVERY selected owner whose `WiredOn` list contains the clicked control's DEFAULT event (same `Name` and
  `WinFormsArgs`), primary first-planned: ONE stub named `<clickedId>_<DefaultEvent>` (or navigation to the clicked
  control's bound handler), bound on all of those owners, ONE write, and the selection NOT collapsed (`:910`). A
  selected member whose kind lacks that event (a TrackBar has no Click) is left unbound — VS wires only the components
  that have the event. A double-click on a control OUTSIDE the selection selects it alone first (`SelectForGesture`
  `:864-866`), exactly as today.
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

⛔ **Re-entrancy.** The grid's OWN edit bumps the revision (`WriteDesignerEditBack` `:1213`), so `RefreshValues` runs
INSIDE the row's `Commit` → `_onChanged` → `Edited` → write chain, and again around a refused value's posted two-step
echo (`RaiseEditorRefresh` `FormPropertyRow.cs:1094-1111`, which sets `_editorEcho` and relies on the binding seeing a
CHANGED value). Rules: `RefreshValues` raises notifications only — it never commits, never sets `_editorEcho`, and a
row whose `_editorEcho` is set is skipped (its posted step owns the refresh). Tested through the real editor path in
Task 3/4: a refused value on a merged row still snaps the real TextBox back (the slice-2 echo test's shape, multi), an
accepted value raises `Edited` exactly once with `RefreshValues` running inside it, and no second write/undo step
appears.

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
- **Ancestor/descendant rule** (coordinator ruling, VS): a selected control that lies INSIDE another selected control
  (a Button in a selected Panel — Ctrl+click can select both; the marquee cannot, `ControlsIn` takes the container
  only) is EXCLUDED from Delete and from a nudge. Deleting the Panel already removes it (removing it first, then the
  Panel, would be harmless but is two model paths for one intent); nudging both would move the child twice (once with
  its container, once itself). One helper, `FormSelectionTopLevel(document, controls)` (via
  `FormGeometryEdit.ParentOf`, `CodeEditorDocumentViewModel.cs:506`'s walk), used by both gestures.
- **Docked members are skipped by a nudge** (a strip's edge is a `Dock` property, not a rect; `FormDockLayout.EdgeOf`
  ≠ null, the same test the drag uses at `FormCanvasControl.cs:1042`). Items too (no geometry). Delete removes them
  normally.
- **Lost — leaving both primary-only:** the grid makes a multi-selection visible and editable. Deleting one control of
  three is the surprise VS users will hit first.

### D-13 — Clicking an already-selected member makes it the PRIMARY and keeps the group (coordinator ruling, VS)

- `FormSelection` gains `Promote(FormControl control)`: when the control is selected and not already last, it moves to
  the END (the primary) and `Changed` is raised; otherwise a no-op that raises nothing. The store's own API — never a
  `Toggle` + `Add` pair (two Changed, a transient state where the grid rebuilds for a selection without it).
- `ApplyClickSelection`'s already-selected branch (`FormCanvasControl.cs:829-834`) calls `selection.Promote(hit)`, so the
  grid, Arrange's reference and the D-8 handler name all follow the control the user pointed at.
- ⚠ **The collapse-on-release is REMOVED** (`_collapseTo`, `:833`, `:1271-1279`). VS keeps the group when you click a
  member without a modifier (you click empty canvas or an unselected control to start over). With collapse kept, the
  first click of a double-click on a member would collapse the selection on its RELEASE — before the double-tap
  arrives — and D-8's canvas route could never see a multi-selection. Measured fact to state in the test: no existing
  test pins the collapse (grep `_collapseTo` behaviour: none in `FormCanvasMultiSelectTests`/`FormSelectionTests`).
  **Lost — keep the collapse and defer it past the double-click time:** a timer-driven selection change is
  untestable-by-construction headless and still not VS.
- A drag that starts on a member still moves the whole group (`DraggingOneMemberMovesEveryMemberByTheSameDelta`
  stays green).

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

**Test cadence (owner instruction 2026-10-05 — "we're running too many tests"; this governs every task below).**
- **Per task:** build `VisualGameStudio.Tests` once, then run ONLY the fixtures the task touches or names in its
  RE-CHECK line, with `--no-build --filter "FullyQualifiedName~<Fixture>|…"`. **No fast subset and no Integration set
  per task.** Task 7's own new Integration fixture is run by itself (it is the task's test), nothing else Integration.
- **Mutations per task: 1–2 meaningful ones**, the first listed (★) and at most one more. The other lines under
  "Mutations" are candidates for a reviewer, not obligations. A mutant is applied with the **Edit** tool and the
  project REBUILT (a mutant survives a revert until you rebuild); record killed or EQUIVALENT, with the measurement.
- **ONE gate before the PR** (Task 9 step 3, which is both the slice gate and the programme's Closing gate): the fast
  subset ONCE plus the property-grid `Form*`/`WinFormsCatalog*` Integration fixtures ONCE, both on the MERGED tree
  (master merged in, in a detached worktree), compared by failure NAMES against master, any new name A/B'd.
- **No full suite at all** (owner, 2026-10-05: "no full test suite after the fast subset has passed — not even in
  Task 9's Closing").
- This is about how often big sets run, not about writing fewer tests: every real-view and acceptance test below stays.

Every task:
- Red test(s) first. Run them and see them fail for the RIGHT reason. Implement. Green.
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

- Build `VisualGameStudio.Tests` Release at `86897991`. **No fast-subset baseline run** (cadence rule): the baseline
  names are slice 5's gate on master's code (`2813205d`, HANDOFF `:42-48`, listed in §5). A name the slice gate shows
  that is not on that list is A/B'd then, on a detached `origin/master` worktree.
- Run M1–M5 (§0), record them in §6, and re-decide anything they contradict.
- No commit, unless a decision changed (then this document, alone).

### Task 1 — The oracle measures mergeability; the catalog carries it (D-2 rule 2)

Files:
- `tools/WinFormsMetadataDump/Program.cs`: `DescribeProperty` emits `mergeable` from
  `p.Attributes[typeof(MergablePropertyAttribute)]`, true when absent.
- `tools/WinFormsMetadataDump/README.md`: what `mergeable` means.
- `VisualGameStudio.Tests/Data/winforms-metadata.json`: REGENERATED by the tool, never hand-edited. ⛔ **Regenerate
  only at the SAME WindowsDesktop runtime the committed file names** (its header records the runtime version,
  `Program.cs:113`). ⛔ **The diff must be ONLY added `"mergeable": …` keys** — check it (`git diff --stat` plus a read of
  the diff: every changed line an added `mergeable` key, no reordered/changed default, description or event line). Any
  other change means the runtime or the tool differs: STOP, do not commit the file, record what differed in §6 and ask
  the coordinator.
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
    relies on: Location/Size/Anchor/Dock mergeable, **TabIndex NOT mergeable** (so the multi row set omits it);
  - `SharesShapeWith` over EVERY pair of same-named rows across kinds (catalog-driven): `TextAlign`
    ContentAlignment × HorizontalAlignment is false, Button.BackColor × Label.BackColor is true;
  - the predicate is symmetric.

Mutations:
- ★ `SharesShapeWith` ignores `WinFormsEnumType` (the TextAlign pair);
- ★ `Mergeable` dropped from the comparer (a flipped catalog row survives — must be killed by the parity cell);
- the dump always writes `true` (the parity row for an `Items` row goes red).

RE-CHECK: `WinFormsCatalogParityTests.TheSnapshot_CoversEveryWinFormsKindInTheCatalog_AndTheForm`, `EveryOracleExemption_StillSuppressesAFinding`, the README's "When to regenerate".

### Task 2 — The grid takes the set: plumbing, the row set, values (VM; plan 6.1/6.2, D-1/D-2/D-3/D-6/D-7)

Files:
- `ViewModels/Designer/FormPropertyGridViewModel.cs`:
  - `SelectedControls`, `IsMultiSelection`, `SetSelection`;
  - `Rebuild` branches: one control → today's path; several → merged rows;
  - `AddIntrinsicRows`/`IntRow`/`GeometryComposite` take the change callback, and `AddIntrinsicRows` RETURNS the
    control's intrinsic rows (a list) instead of appending to `Rows`: the single path appends them, the multi path
    builds them PER MEMBER (with `tally.Mark`) and merges by name. One builder, two consumers — never a second copy of
    the geometry switch;
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
- the row set for {btn, btn2}, {btn, lbl}, {btn, txt} (no `TextAlign`; no `Name`; **no `TabIndex`**; `Location` and
  `Size` present per D-2);
- catalog-driven: for EVERY pair of kinds in `FormControlCatalog.For(target)` × both targets, the offered names equal
  the D-2 intersection computed independently from the catalog (`SharesShapeWith` + `Mergeable` + geometry). The
  merged rows' order is the primary's;
- equal values shown, a differing value blank + `IsMixed`, two absent rows of kinds with different defaults blank;
- grey only when all absent; bold when ANY member is bold; Reset offered only when ALL members can reset (one set + one
  absent: bold, no Reset — D-3);
- a frozen member freezes the set with the member named;
- a strip in the selection drops the geometry rows;
- `SetSelection` rebuilds once (count `Rows` resets) and is a no-op for the same list;
- the canvas echo (`SelectedControl = primary`) does not rebuild; `SelectedControl = other` means `[other]`;
- selector blank in multi and a pick requests one control;
- the single-selection grid is unchanged (the existing `FormPropertyGrid*` fixtures green, run by `--filter` in this task).
- `FormPropertyGridDisplayTests` twins: `TheObjectSelector_IsBlank_ForAMultiSelection`,
  `PickingAnObject_InAMultiSelection_RequestsOnlyThatControl`.
- **The one-store invariant** (new, real view, `FormPropertyGridMultiSelectRealViewTests`, `TwoZooms`): after EVERY
  canvas gesture — a click, a Ctrl+click adding, a Ctrl+click REMOVING THE PRIMARY, a Shift+click, a marquee, a click
  on an already-selected member (D-13's promotion; the old collapse-on-release is gone), Esc, a click on empty canvas,
  a right-click on a member and on a non-member, a Delete — `grid.SelectedControls` equals `Selection.Controls`
  element-wise IN ORDER and `grid.SelectedControl` equals `Selection.Primary` (and the canvas's `SelectedControl`).
  One data-driven test over the gesture list, so a new gesture is one row. The promotion row and the Delete row are
  added by Tasks 4b and 6 (they change those gestures); every other row lands here.
  Mutation: the host's `Selection.Changed` handler passes `Selection.Primary` only (the Ctrl+click rows go red); the
  grid keeps a stale list when the primary is removed (the removing-the-primary row).

Mutations:
- ★ intersection by name only (the TextAlign pair; the catalog sweep);
- ★ blank-when-mixed removed (shows the primary's value);
- `Mergeable` ignored (TabIndex offered);
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
- `RefreshValues` after an `Arrange` → the merged Location shows the new value;
- **re-entrancy (D-9), through the real document view model:** an accepted merged edit raises `Edited` once with
  `RefreshValues` running inside the revision bump, undo depth +1, no second write; a refused merged value leaves
  `_editorEcho` handling to its posted step (`RefreshValues` skips that row) — the real-view half is Task 4 (i);
- Reset with one member absent is not offered and a cleared editor writes nothing (D-3/D-5).

Mutations:
- ★ **Edited raised per member** (`onChanged: RaiseEdited` on members): the count tests and undo depth 2;
- ★ pre-judge skipped (partial apply: the {lbl, txt} test);
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
- (h) selecting a mixed Bool/Enum row and clicking away writes nothing (M4's real half);
- (i) re-entrancy through the editor echo: type a refused value (`Bogus`) into the merged BackColor and click away → the
  real TextBox snaps back to blank, the pane names the reason, nothing written — with `RefreshValues` wired (D-9);
- (j) un-mixed under focus (D-4): focus the mixed Width text box, then make the members equal by another route (Arrange
  "make same width" through the command), then click away → the dying text box's LostFocus writes nothing (undo depth
  unchanged; the document holds the arranged widths).

Plus:
- `FormPropertyGridViewTests.EveryBindingInTheGridView_ResolvesAgainstTheTypeInScope` (automatic; any new binding);
- `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…` extended: every merged row's editor of a {btn, lbl} selection
  fits between divider and edge at two sizes.

Mutations:
- ★ the Task-3 "Edited per member" mutant must also turn (b) red (undo depth 2, one Ctrl+Z restores only one);
- ★ the mixed-editor `""` guard keyed on today's mixedness instead of the editor that pushed it ((j), below);
- the selector not blanked ((a));
- the text-box switch removed ((e): 0 pushed);
- the popup guard skipping merged rows (the second drop-down in (d)). If it does not reproduce headless, record it
  EQUIVALENT with the measurement, as slice 5 did;
- `RefreshValues` does not skip a row with `_editorEcho` set ((i): the box keeps `Bogus`);
- (the ★ guard mutant above, in detail: (j)'s stale `""` is judged against the now-equal value → Reset verdict →
  attributes removed).

RE-CHECK: `FormPropertyGridRealViewTests` (all), `FormPropertyGridEditorRealViewTests`, `TheDocumentView_NoLongerCarriesTheGridsOwnBindings`.

### Task 4b — Primary promotion (D-13)

Files: `ViewModels/Designer/FormSelection.cs` (`Promote`); `Controls/FormCanvasControl.cs` (`ApplyClickSelection`'s
already-selected branch `:829-834` calls `Promote`; `_collapseTo` and its release arm `:1271-1279` deleted).

Tests:
- `FormSelectionTests` (fast): `Promote` moves a selected member to the end and raises `Changed` ONCE; a no-op (already
  primary, or not selected) raises nothing; membership and the others' order unchanged.
- `FormCanvasMultiSelectTests`: select A, B, C (Ctrl+click); a plain click on A → the selection is still {B, C, A}, A
  primary; a drag that starts on A moves all three (the existing drag test, green); Arrange "align lefts" now aligns to
  A's left.
- The one-store invariant gains its promotion row (the grid's primary follows, the selector stays blank).
- Real view: after the promotion click, the double-click route (Task 5) sees the group — covered there.

Mutations: ★ the branch left as `_collapseTo` (the still-three-selected assert); ★ `Promote` via Toggle+Add (two
`Changed`: the count test); candidate: `Promote` a no-op (Arrange aligns to C).

RE-CHECK: `FormCanvasMultiSelectTests` (all nine), `FormCanvasPixelPageTests`, `FormStripCanvasTests` (the second-click
rename on an item reads `Selection.Controls.Count <= 1`, `:989` — unchanged for a single selection; a rename never arms
in a multi-selection, as today).

### Task 5 — The Events tab for a multi-selection (plan 6.4, D-8)

Files:
- `FormEventRow.cs` (`Owners`, shared handler, intersected choices with the same instance, commit over owners with ONE
  `Edited`, `FormHandlerRequest.Owners`);
- `FormPropertyGridViewModel.RebuildEventRows` (the intersection through `WiredOn` per member);
- `CodeEditorDocumentViewModel.RunHandlerGestureAsync`/`ActivateHandlerAsync`: plan for the primary, bind every owner,
  ONE `WriteDesignerEditBack`, the de-dupe key over all owners, no selection collapse (`:910`);
- `FormHandlers.DescribeUnusableHandler` asked per owner (a loop at the call sites, not a signature change);
- `FormPropertyGridViewModel.RefuseHandler` finds the row by `Owners` containing the owner (D-8);
- `ActivateControlAsync`: a control in a multi-selection → the owners sharing its default event (D-8 canvas route).

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
  bound to `AnyClick` → navigate, and btn gets `AnyClick` too. A host refusal of a typed name with {btn, btn2} reaches
  the merged row (`RefuseHandler` by `Owners`): its pane says why, the cell reverts, nothing in the Error List.
- **The canvas double-click route** (`FormCanvasDoubleClickTests` + the real view, `TwoZooms`): Ctrl+select btn and btn2,
  then a real double-click on `btn` (the first press promotes it, D-13) → ONE stub `btn_Click`, bound on BOTH, ONE undo
  step, the selection still {btn2, btn}. With a TrackBar also selected → the TrackBar gets no bind (no Click on its row),
  the two Buttons do. A double-click on an UNSELECTED control → it alone is selected and wired, as today.
- Real view (`FormPropertyGridEventsRealViewTests`, `TwoZooms`):
  - Ctrl+select two Buttons, click the bolt, double-click the empty Click value → the disk `.bas` gains ONE
    `Private Sub btn2_Click(sender As Object, e As EventArgs)`, both binds, the cell shows it after a click on the canvas
    (slice 5's CRITICAL, re-run in multi);
  - one real Ctrl+Z removes both binds;
  - a web pair: `btn2_Click(e As DomEvent)` above the region, both bound `click`.

Mutations:
- ★ bind on the primary only (both-bound asserts, Events tab AND canvas route);
- ★ the selection collapses after the gesture (`:910` left as is);
- rows from the primary only (a {Button, TrackBar} selection offers Click);
- one `Edited` per owner (undo depth);
- the de-dupe key on the primary only (two different multi requests: one dropped);
- the canvas route binds the clicked control only (the btn2 bind missing).

RE-CHECK: `FormEventGrid*`, `FormHandlerGestureTests`, `FormHandlerPlanTests`, the slice-5 real-view rows (single selection unchanged).

### Task 6 — Canvas gestures over the whole selection (D-11)

Files:
- `CodeEditorDocumentViewModel.DeleteControl` (multi → every TOP-LEVEL selected control, one write);
- `Controls/FormCanvasControl.cs` arrow/Shift+arrow/cell nudge over the TOP-LEVEL members of `SelectedSet`
  (`:421-426`) that have geometry and are not docked, with one `CommitGeometry`;
- the one shared helper `FormSelectionTopLevel` (D-11), used by both.

Tests:
- `FormCanvasKeyboardTests` / `FormCanvasMultiSelectTests`: Delete with three selected removes three; ONE
  `UndoDesignerEdit` restores three; arrow moves both by 1 and Ctrl+arrow by the grid step, parent-relative, one undo
  step; Shift+arrow resizes both; a web Grid page moves both one cell;
- **ancestor/descendant, Delete:** a Panel and its child Button both selected (Ctrl+click) → Delete removes the Panel
  (the Button with it), the document has neither, ONE undo step restores both with the Button still inside the Panel;
- **ancestor/descendant, nudge:** the same selection, one Right arrow → the Panel's X +1 and the Button's
  container-relative X UNCHANGED (it moved once, with its container);
- **docked member skipped:** a MenuStrip (Ctrl+clicked) + a Button, one Down arrow → the Button's Y +1, the strip has no
  geometry change and its `Dock` attribute is untouched;
- `FormTrayViewTests` unchanged (tray Delete removes the one component);
- the one-store invariant gains its Delete row.

Mutations: ★ nudge without the top-level filter (the child's relative X moves too); ★ Delete primary-only (two of three
remain); candidates: one commit per control (undo depth), docked members nudged.

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
- ★ bind on the primary only (one click does nothing: count 1, on both targets);
- ★ the IDE route alone passing no forms (`BuildService`: web/IDE "no page" while CLI is green — the both-entry-points kill);
- candidate: members written with per-member `Edited` (the design step asserts the undo depth first).

Run: this fixture alone (`--filter FullyQualifiedName~FormMultiSelectAcceptanceTests`), plus
`JsExecutionTierRosterTests` if the roster changed. No other Integration fixture in this task (cadence rule).

⚠ `JsExecutionTierRosterTests.RosterIsPinned`: +1 if the fixture runs node in-fixture (roster it, and re-read the pin
at merge: it collides SILENTLY, MEMORY). The roster guard also sweeps an `Integration` fixture named `*ExecutionTests`;
this one is `*AcceptanceTests`.

### Task 8 — Records and mutation ledger (no test run)

- Execution notes per task in §6. Each records: base SHA, deviations, red evidence, the filtered fixtures run, the
  mutation table.
- `docs/form-designer-followups.md` §44: D-12's deferred list, plus anything found.
- No gate here. The slice's ONE gate runs in Task 9, on the merged tree (cadence rule; the slice gate and the
  programme's Closing are the same run, so nothing big runs twice).

### Task 9 — Closing the property-grid programme (plan "## Closing (after slice 6)", made concrete)

⛔ **Order (coordinator ruling 1, slice-4 lesson).** A trial merge taken before the LAST commit does not count, and
HANDOFF's NEWEST section conflicts on every merge. So: commit the records and the IDE drop FIRST, then do the real
merge check, then run the gate ON THE MERGED TREE. ⛔ **No full suite** (owner instruction 2026-10-05: no full test suite
after the fast subset has passed, not even here). The gate is step 3.

1. **Records + IDE drop — commit these FIRST** (one commit, staged by name):
   - `docs/HANDOFF.md` NEWEST section: slice 6 plus "the property-grid programme is COMPLETE". It names where each
     slice's record lives (slice 2 `2026-09-26-…-slice2-preflight.md`, slice 3 `2026-09-29-…`, slice 4 `2026-10-04-…`,
     slice 5 `2026-10-04-…-slice5-…`, slice 6 this file), and says the gate numbers are in THIS file's §6. ⛔ HANDOFF is
     not touched again after this commit: the gate numbers (step 3) go into §6 of this pre-flight — a file only this
     branch edits, so that later commit cannot conflict and the merge check of step 2 stays valid — and into the PR body.
   - The parent plan's slice-6 heading gets the "EXPANDED AND EXECUTED" banner, as slice 3's has (`:5985-5986`).
   - **IDE drop** (Windows): `dotnet clean` (Shell), Release build, `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E`,
     never `/MIR`. Confirm `IDE\lib\js\dom-core.bli` is present and byte-identical (LOAD-BEARING). The owner runs from
     `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe`.
   - The auto-memory line for the programme is updated by the coordinator (memory does not travel; HANDOFF is the record).
2. **Real merge check, after that commit:** `git fetch origin` (both directions), then
   `git worktree add --detach <dir> <branch HEAD sha>`, `git merge origin/master` in it, and read the result (never
   `git merge-tree`). Expect conflicts:
   - `FormPropertyGridViewModel.cs`/`FormPropertyRow.cs` (piece 2 if it lands first);
   - `winforms-metadata.json` (any regeneration);
   - `JsExecutionTierRosterTests` (the pin — collides SILENTLY: read `RosterIsPinned`);
   - HANDOFF NEWEST.

   A conflict is resolved on the BRANCH (a merge commit of `origin/master`), never only in the scratch worktree, and
   step 2 is repeated on the new HEAD.
3. **The gate — ON THE MERGED TREE** (the detached worktree, clean Release build, both streams captured to a file):
   - **Fast subset** (`--filter "TestCategory!=Integration"`) ONCE;
   - **the property-grid Integration fixtures** ONCE: every `Form*` and `WinFormsCatalog*` Integration fixture
     (`--filter "TestCategory=Integration&(FullyQualifiedName~.Form|FullyQualifiedName~WinFormsCatalog)"`, which includes
     the new `FormMultiSelectAcceptanceTests` and §5's named set), plus `WebMainStartupTests`,
     `JavaScriptProjectBuildTests`, `BuildServicePipelineTests`, `JsExecutionTierRosterTests`.
   - Compare **sorted failure NAMES**, never counts, against master's own. The known list at this base:
     - the machine rows `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
       `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`
       (intermittent), `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`, `SearchSnippets_EmptyQuery_ReturnsAll`,
       `SearchSnippets_WhitespaceQuery_ReturnsAll`, `ReadingAnMvidTakesNoLockOnTheFile`;
     - the culture row `CppDoubleFormattingTests.Expected_IsWhatDotNetPrints` ("∞" vs "Infinity"; matched by "Form" in
       "Formatting") and `ModuleScopeInitializerTests.AFoldedComparison_ReachesEveryBackend_ModuloBooleanFormatting` (same
       reason);
     - the inverse-gated skip `Build_CppLanguageProject_NoToolchain_…`.
   - **Any name not on that list:** re-run it alone (`--no-build --filter`), then A/B it on a
     `git worktree add --detach` of `origin/master`. Only a failure that fails identically on master is inherited; anything
     else blocks the merge.
   - Remove both worktrees afterwards (`git worktree prune` after "Filename too long").
   - The gate numbers (base = the merged tree's sha) go into §6 of this file and the PR body (step 1), never HANDOFF.
4. **Merge only with the owner.** Never push from this task without the coordinator.
5. **The owner's click-through for the WHOLE programme**, in the IDE above. Each step names its slice.

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

   13. Click `button1`, Ctrl+click `button2`: the object selector goes blank; Name and TabIndex disappear; Location,
       Size, Text, BackColor, Font… remain; a property that differs shows blank. Click `button1` again (no modifier):
       both stay selected and `button1` becomes the primary (align-lefts now lines up on `button1`).
   14. Type `Go` into Text: both change; ONE Ctrl+Z restores both.
   15. Ctrl+click a Label too: BackColor → Web → Red colours all three; Font → Bold keeps each control's own family;
       Width `90` sizes all three; a blank Width box left without typing changes nothing.
   16. A Button + a TextBox: no TextAlign row (their alignments are different types).
   17. Set Location X to `96` on three controls: their left edges line up; align-lefts from the toolbar and the X row
       follows without reselecting.
   18. Events with two Buttons selected: double-click Click → ONE handler named after the PRIMARY — the button you
       Ctrl+clicked last, or the one you last clicked inside the selection (step 13) — e.g. `button2_Click`, both wired;
       F5, both buttons run it; clear the cell → both unwired, the Sub stays; Ctrl+Z restores both.
   18a. On the canvas, with both Buttons selected, double-click `button1`: ONE `button1_Click`, wired on both, and both
       stay selected. Add a TrackBar to the selection and repeat on a fresh form: the TrackBar is not wired (it has no
       Click).
   19. Delete with three selected removes all three; Ctrl+Z brings all three back. Arrow keys move the group together.
       Select a Panel AND a Button inside it: an arrow moves the Panel and the Button rides along once; Delete removes
       both, one Ctrl+Z restores both, the Button still inside. A MenuStrip in the selection stays docked when the arrows
       move the rest.
   20. A web form: steps 13–15 and 18 again; open the page, both elements styled, both clicks run the handler.

## 5. Gate for the slice (run ONCE, in Task 9 step 3, on the MERGED tree — cadence rule)

There is no separate slice gate and no full suite: Task 9 step 3 is the one gate for both the slice and the programme
(owner instructions 2026-10-05). What it must show:

- **Fast subset** (`--filter "TestCategory!=Integration"`, Release, both streams captured): the **sorted failure NAMES**
  are on the known list (slice 5's gate on master's code, `2813205d`), base SHA = the merged tree. Known machine rows only: `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind` (intermittent), `SearchSnippets_EmptyQuery_ReturnsAll`,
  `SearchSnippets_WhitespaceQuery_ReturnsAll`, `ReadingAnMvidTakesNoLockOnTheFile`. Re-run named rows with
  `--no-build --filter`, because a passing test prints nothing.
- **The property-grid Integration fixtures** (every `Form*`/`WinFormsCatalog*` Integration fixture, Task 9 step 3's
  filter), 0 unexpected skips on Windows; at least these must appear in the run:
  - new: `FormMultiSelectAcceptanceTests`;
  - slice 5's 14 classes: `WinFormsCatalogSweepTests`, `FormEventWebRunTests`, `FormEventAcceptanceTests`,
    `FormDesignerAcceptanceTests`, `FormMenuAcceptanceTests`, `FormComponentAcceptanceTests`, `FormBuildEmissionTests`,
    `FormPropertyBatchAcceptanceTests`, `FormItemsAcceptanceTests`, `FormImageAcceptanceTests`, `FormRetargetPairTests`,
    `WebMainStartupTests`, `JavaScriptProjectBuildTests`, `BuildServicePipelineTests`.

  ⚠ The inverse-gated `Build_CppLanguageProject_NoToolchain_…` skip is expected. ⚠ JS web-build rows'
  `ERROR_USER_MAPPED_FILE`: compare by NAME. ⚠ A filter term matching "Form" also matches `…Formatting` (the culture
  row): name it, don't count it.
- A name not on the known list: re-run alone, then A/B on a detached `origin/master` worktree. Only identical-on-master
  is inherited.

## 6. Execution notes

(Filled per task as slices 4/5 did: base SHA, M1–M5 results, deviations, red evidence, RE-CHECK results, the mutation
table. A decision a measurement re-decided is recorded here with the measurement.)

### Task 0 — measurements (base `74059bad`, Windows, WindowsDesktop 8.0.23 — the runtime the committed oracle names)

No decision changed, so no commit of its own; recorded here with Task 1.

- **M1** (the tool itself, extended as Task 1 extends it, run to a scratch file): `AllowMerge=false` on exactly
  `TabIndex` (every positioned kind and the strips), `Items` (ComboBox, ListBox, CheckedListBox, ListView, MenuStrip,
  ToolStrip, StatusStrip), `Columns` (ListView, DataGridView), `DataSource` (ComboBox, ListBox, DataGridView,
  ErrorProvider), `FormatString` (ComboBox, ListBox, CheckedListBox), `Groups` (ListView), `Lines` (TextBox),
  `MdiWindowListItem` (MenuStrip), `Nodes` (TreeView), `TabPages` (TabControl). Everything else true — **`Location`,
  `Size`, `Anchor`, `Dock` true** (Button measured; the test pins every positioned kind). The Form has none. Of these,
  the catalog carries only the three `Items` rows (ComboBox, ListBox, CheckedListBox) — the strips' items are children,
  not a row — so exactly those three are `Mergeable: false`. D-2 rules 2–4 stand as written (TabIndex not offered,
  Location offered).
- **M2** (real view, both zooms): single selection IS stale today. After a real drag the model's X went 16→88 (800×560)
  / 16→56 (1400×900) and the X row's `DisplayValue` followed, but the real NumericUpDown still showed **16**, and no
  PropertyChanged reached the row; the same after an arrow nudge (89 / 57, editor 16). So D-9's `RefreshValues` fixes a
  pre-existing single-select defect too.
- **M3** (real view, both zooms): a real Ctrl+click on `btn` with `lbl` selected → `Selection.Controls` = [lbl, btn],
  `PropertyGrid.SelectedControl` = btn, the canvas's `SelectedControl` = btn, the grid's `Rows` reset **once**. D-1's
  ordering argument holds.
- **M4** (the real `TypedValueEditor` hosted alone over a logging `ITypedValueRow`, Bool and Enum items, value `""`):
  nothing pushed on bind (SelectedIndex −1); nothing on focus + click away; nothing on a real click that opens the
  drop-down and a click far outside it (or Esc); a value going from a member back to `""` pushes **null**, never `""`.
  (A first probe that "clicked away" onto the control under the combo landed on the open overlay's first item and pushed
  it — a probe artefact, re-measured with a far click.) D-4's `""` guard stays as a cheap defence; the null guard
  (`Commit`'s first line) is the one that fires.
- **M5** (same rig, `IsNumericUpDown`, value `""`): the NumericUpDown shows **`0`** (Value 0, text "0") — the false
  value D-4 predicted — and a focus + click away pushes nothing. D-4 (a mixed Int row renders the TEXT editor) stands:
  the push risk is low, the display is wrong.
- Baseline: no fast-subset run (cadence rule); slice 5's gate list in §5 is the known list.

### Task 1 — the oracle measures mergeability (base `74059bad`)

- `tools/WinFormsMetadataDump` records `mergeable` (the attribute, true when absent), placed after `isCollection` so the
  regeneration only ADDS lines. Regenerated at WindowsDesktop **8.0.23** (the committed header's runtime): `git diff
  --stat` = 1308 insertions, **0 deletions**; every added line is `"mergeable": true|false,` (checked by script: 0
  other added lines, 0 removed). Stop rule satisfied.
- `WinFormsPropertyEntry.Mergeable` is `bool?`: a snapshot without the key reads "not measured — regenerate", never
  true. `CatalogParity.CompareProperty` reports a disagreement or a missing measurement, after the exemption check (an
  exemption covers it as it covers the default).
- `FormPropertyDef.Mergeable = true` (new last parameter); `false` on the three `Items` rows. `SharesShapeWith` = same
  name, type, `WinFormsEnumType`, `AllowedValues` sequence. The sweep found the plan's TextAlign pair AND
  `CheckBox.Appearance` × `TabControl.Appearance` (different enums) — both now different rows.
- **Red** (before the regeneration and with `SharesShapeWith` = name + type): every parity cell "the snapshot carries
  no 'mergeable'"; the Items test; the five intrinsic cases (`Location=` null…); TextAlign/Appearance pairs
  `SharesShapeWith=True, expected False`.
- ⚠ **Deviation — the pair sweep is ONE test over a loop, not a TestCaseSource.** The first version yielded ~15k cases;
  with it, `--filter "FullyQualifiedName~FormMultiSelectCatalogTests"` started running the unrelated
  `NetGeneratedShimConformanceTests` (MSVC + AOT builds, >10 min) — the filter stopped selecting. As a loop the filter is
  exact again (the run takes ~100 ms). Lesson for the next sweep author: keep TestCaseSources small.
- Green: `FormMultiSelectCatalogTests` + `WinFormsCatalogParityTests` (RE-CHECK names included) **74/74**.
- Mutations (each applied with Edit, project rebuilt):
  | Mutant | Result |
  |---|---|
  | ★ `SharesShapeWith` ignores `WinFormsEnumType` | KILLED by `TwoEnumRows_WithTheSameMembers_ButDifferentEnumTypes_…`. ⚠ The catalog sweep alone does NOT kill it: every same-named enum pair in today's catalog also differs in members — so the synthetic case is the pin |
  | ★ Mergeable dropped from the comparer | KILLED by `TheInstrument_CatchesAMergeableDisagreement_AndAnExemptionCoversIt` |

### Task 2 — the grid takes the set (base `a75b50f2`)

- `FormPropertyGridViewModel`: `SelectedControls` (a COPY, primary last), `IsMultiSelection`, `SetSelection` (no-op for an
  element-wise equal list; sets `SelectedControl` = primary under a guard; ONE rebuild). A direct `SelectedControl` set
  means exactly that control (`[value]`/`[]`), so `Load` and the canvas echo keep their meaning. `AddIntrinsicRows` became
  `IntrinsicRows(control, changed)` returning the list (one builder; `GeometryComposite` takes the callback); the catalog
  loop became `CatalogRows(control, changed)`; the multi path `MergedRows` builds both PER MEMBER with one
  `FormEditTally.Mark` and merges by D-2 (intrinsic by name + `OffersIntrinsicForMultiSelection` — never Name/TabIndex;
  catalog by `SharesShapeWith` + `Mergeable` on every member), in the primary's order. Selector `null` in multi and a pick
  of ANY object (the primary too) requests that one control; `Header`/`HeaderKind` `""`; a carried refusal is titled with
  the comma-joined ids; the Events tab shows no rows in multi with "The selected controls share no events on WinForms."
  (or "the web") until Task 5.
- `FormPropertyRow`: the merged MODE (`Merged(members, owners, tally, onChanged)`; parts merged by index); `IsMixed`;
  `DisplayValue`/`RawValue` shared-or-blank; `IsPresent` any; `IsDefaultShown` all-and-not-mixed; `IsBold` any;
  `CanReset` all (VS's merged descriptor, D-3); a frozen member freezes the row and is named (`'btn2': …`).
- Host: the `Selection.Changed` handler and `SelectInDesigner` call `PropertyGrid.SetSelection(Selection.Controls)`; the
  class no longer assigns `PropertyGrid.SelectedControl` (pinned by a source test).
- ⚠ **Deviations.** (1) `Merged` takes the members' `owners` as well as the plan's three parameters: a frozen member is
  named here, and Task 3's all-or-nothing refusal names the refusing member (`'txt' (TextBox): …`) from the same list.
  (2) Transitional until Task 3, as the plan asks: every merged row is frozen with `MultiEditNotYetReason` (no editor that
  silently does nothing). Its `CanReset` already reads the members (so the D-3 rule is tested now), which means the Reset
  menu item can be ENABLED on a merged row in this commit while `Reset()` does nothing for it — Task 3 makes it act.
  (3) The plan's catalog-driven pair sweep is one test over a loop (Task 1's TestCaseSource lesson): 1,000+ pairs, both
  targets, documents built with `FormCatalogShapes.Canonical`.
- **Red** (API present, `Rebuild` still single-path): 15 of 54 — Name/TabIndex offered, TextAlign offered for
  Button+TextBox, OK-vs-Hello showing `Hello`, selector `btn2`, header `btn2`, the primary's Events rows, the sweep
  (`Label+Label: offered [Name,…,TabIndex,…]`), and both real-view tests (selector not blank; no merged rows). (One false
  red on the way, a test bug: `Open(doc, …)` vs `Open(params ids)` overload resolution read `"btn"` as the document —
  renamed `OpenDoc`.)
- Green: `FormPropertyGridMultiSelectTests` (18), `FormPropertyGridDisplayTests` (incl. the two D-6 twins), the two
  real-view tests (`AfterEveryCanvasGesture_…`, `ARealCtrlClick_…`, both zooms), `FormMultiSelectCatalogTests` — **66/66**.
  RE-CHECK (`TestCategory!=Integration` and `FormPropertyGrid*`, `FormPropertyRowDefaultTests`, `FormEventGrid*`,
  `FormSelectionTests`, `FormTrayViewTests`, `FormCompositeRowTests`) — **498/498**.
- Mutations:
  | Mutant | Result |
  |---|---|
  | ★ intersection by name only (`r.Name == row.Name` for `SharesShapeWith`) | KILLED: `AButtonAndATextBox_OfferNoTextAlign_…`, the pair sweep |
  | ★ blank-when-mixed removed (`Shared` returns the primary's value) | KILLED: `AValueEveryMemberShows_…`, `TwoAbsentRows_…`, `AFrozenMember_…` |
- Review of `a75b50f2` + `55facb58`: approved. Its minors are folded into Task 3's commit: the pair sweep's comment now
  says it MIRRORS `SharesShapeWith` (the synthetic enum test is the real pin); the source pin is a regex over every
  assignment spelling (`PropertyGrid!.SelectedControl=`, `?.`, spaces; never `==`), with its own pattern self-checked;
  the grid raises `SelectedControls`/`IsMultiSelection` on every rebuild (for a view that binds them); and Reset on a
  merged row acts (the interim freeze is gone).

### Task 3 — the multi-edit (base `55facb58`)

- `FormPropertyRow`: `Preview(value)` (the same Judge / intrinsic no-op decision `Commit` takes, writing nothing; a part
  asks its parent about the whole it COMPOSES — `FormCompositeRows.Part` now records `Compose`). Merged `Commit` =
  `CommitMerged`: pre-judge every member → any Refuse writes nothing, `Refusal` = `'id' (Kind): reason` per refusing
  member, editor snaps back; any Reset verdict is the Reset gesture (all members, or nothing when one cannot reset);
  otherwise each member commits on its own value, counted by the tally, and ONE `Edited` when the tally moved. Merged
  `Reset` resets every member, one `Edited`. The Task-2 interim freeze (`MultiEditNotYetReason`) is DELETED.
- D-4 editors: a mixed Int row is a text box (`IsNumericUpDown` false / `IsTextBox` true while mixed; both raised on
  every value change); an EMPTY `StringValue` push into a merged row is ignored when the row is mixed, and ALWAYS for an
  Int row (only the mixed text box pushes an Int row's StringValue — the "keyed on the editor" rule, so a text box dying
  after an un-mix is still ignored); a mixed Bool's double-click sets True on all; the Font dialog starts from the
  primary's effective font; a mixed Anchor's summary is blank and its box starts at Top, Left (whole value to all); a
  mixed Dock lights nothing and its summary is blank.
- D-9: `FormPropertyGridViewModel.RefreshValues()` (every row, parts and members, `RaiseOwnValueChanged`; every Events
  row `HandlerChanged`; a row with `_editorEcho` set skipped), called from `OnDesignModelRevisionChanged`. This also
  fixes M2's single-selection staleness.
- `FormPropertyRow.Definition` is public now (the tests judge each member through its own catalog row; the Shell grants
  tests no internals).
- **Red** (merged rows still frozen): 23 of 45 — every Edited-once case (0 edits), the undo test, Reset, all-or-nothing,
  per-member, the mixed text-box switch, Anchor/Dock summaries, the Font start, Arrange's refresh, re-entrancy.
- Green: `FormPropertyGridMultiSelectTests` (45, incl. a catalog-driven sweep of every row {btn, btn2, lbl} share) +
  `FormMultiSelectCatalogTests` — **57/57**. RE-CHECK (`TestCategory!=Integration` over `FormPropertyGrid*`,
  `FormPropertyRowDefaultTests`, `FormEventGrid*`, `FormSelectionTests`, `FormTrayViewTests`, `FormCompositeRowTests`,
  `FormMultiSelectCatalogTests`, `FormCanvasUndoTests`) — **551/551**.
- ⚠ Deviation: the Bold-part-on-two-fonts test lives in `FormPropertyGridMultiSelectTests` (it needs the merged grid),
  not `FormCompositeRowTests`; that fixture is in the RE-CHECK run and unchanged.
- Mutations:
  | Mutant | Result |
  |---|---|
  | ★ Edited raised per member (member callback = `Mark` + `RaiseEdited`) | KILLED: 20 tests incl. every Edited-once case and `AMultiEdit_IsOneUndoStep_…` (the real document view model) |
  | ★ pre-judge skipped (partial apply) | KILLED: `AValueOneMemberRefuses_…`, `TheRevisionRefreshInsideAMergedEdit_…` |

### Task 4 — the real view (base `9fe0d153`)

- No AXAML change (the mixed text-box switch rides on the existing `IsTextBox`/`IsNumericUpDown` bindings), so no
  `dotnet clean`. Tests only: (a)–(j) in `FormPropertyGridMultiSelectRealViewTests.cs` (real clicks, Ctrl+click,
  `PickInCombo`, the colour pop-up's Web tab, a REAL Ctrl+Z on the canvas, both sizes), the layout sweep
  `FormDesignerLayoutRealViewTests.EveryMergedRowsEditor_OfAButtonAndALabel_…` (a {Button, Label} selection, every
  composite expanded: TextBox 12, ComboBox 7, NumericUpDown 6, colour 2, Font `…` 1 per size, the mixed Width a text
  box), and one VM test, `AStaleEmptyPushIntoAnUnMixedIntRow_WritesNothing`.
- **Measured (j):** after Arrange "same width" under the focus, the dying text box was hidden and unfocused with its
  text ALREADY repainted to `100` by the revision refresh (both sizes) — so its LostFocus pushes `100` (a no-op), never
  the stale `""`. The real view cannot show the guard; the VM test pins it on a catalog Int (MaxLength), where a stale
  `""` would be Judge's Reset on both members.
- **Red before:** these are acceptance tests of Task 3's code (written after it, in the same session); their red is
  the mutation table — the Task-3 ★ mutant turns (b) red at the real Ctrl+Z.
- Green: the 12 new real-view/layout tests; RE-CHECK `FormPropertyGridRealViewTests` (all partials, incl. the editor
  and Events real-view files), `FormPropertyGridMultiSelectTests`, `FormPropertyGridViewTests` (binding gates),
  `FormDesignerLayoutRealViewTests.Every*` — **159/159**.
- Mutations:
  | Mutant | Result |
  |---|---|
  | ★ Task 3's Edited-per-member mutant | KILLED by (b): after one real Ctrl+Z the text still differs (539 vs 534 chars — one member's write remained) |
  | ★ the "" guard keyed on today's mixedness | KILLED by `AStaleEmptyPushIntoAnUnMixedIntRow_WritesNothing`; (j) is EQUIVALENT for it (measured above) |

### Task 4b — primary promotion (base `af4db0f2`)

- `FormSelection.Promote(control)`: a selected non-primary control moves LAST, one `Changed`; a no-op raising nothing
  for the primary or an unselected control. `FormCanvasControl.ApplyClickSelection`'s already-selected branch calls it;
  `_collapseTo` and its release arm are DELETED (a click on a member keeps the group, as VS does).
- ⚠ **Pre-flight fact corrected:** D-13 said "no existing test pins the collapse". One did:
  `FormCanvasMultiSelectTests.APlainClickOnAnUnselectedControlReplacesTheSelection` clicked A — a MEMBER of {A, B} —
  and passed only through the collapse-on-release (it went red: `[B, A]`, expected `[A]`). D-13 is the delegated VS
  ruling, so the TEST was corrected, not the code: it now clicks a genuinely unselected third control (its doc comment
  says why), and the member click is the new promotion test.
- Tests: `FormSelectionTests` +2 (promote moves/raises once; no-op cases); `FormCanvasMultiSelectTests` +2 (plain click
  on a member of {A, B, C} → {B, C, A}, A primary, and `FormArrange.Apply` align-lefts lines up on A's 40, not C's 200;
  a drag starting on a non-primary member moves all three by +32); the one-store invariant gained its promotion row.
- **Red** (`Promote` a no-op stub, collapse still in place): the promote test (order unchanged) and the canvas test
  (the group collapsed to 1). The invariant sweep stayed green on the stub — expected: store and grid agree either way.
- Green: `FormSelectionTests` + `FormCanvasMultiSelectTests` + the invariant — **30/30**; RE-CHECK
  (`TestCategory!=Integration` over `FormCanvas*` (incl. `FormCanvasPixelPageTests`), `FormDesignerRealView*`,
  `FormPixelPageRealView*`, `FormStripCanvasTests`) — **136/136**.
- Mutations:
  | Mutant | Result |
  |---|---|
  | ★ the branch left as `_collapseTo` (the pre-change code) | KILLED — that is the red run above: `APlainClickOnAMember_…` got a selection of 1 |
  | ★ `Promote` as `Toggle` + `Add` | KILLED by `Promote_MovesAMemberToThePrimary_…_AndRaisesChangedOnce` (two Changed) |

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
| `FormCanvasKeyboardTests`, `FormTrayViewTests`, `FormStripCanvasTests` | Delete/nudge over the TOP-LEVEL set, docked skipped; tray Delete single | 6 |
| `FormCanvasMultiSelectTests` (all nine), `FormSelectionTests` | D-13: promotion replaces the collapse-on-release (no existing test pinned the collapse) | 4b |
| `FormCanvasDoubleClickTests` | the canvas double-click on a member of a multi-selection (D-8) | 5 |
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
13. Not in the plan: clicking an already-selected member only arms a collapse-on-release (`FormCanvasControl.cs:829-834`,
    `:1271-1279`). VS promotes it to primary and keeps the group; the collapse would also break a double-click on a
    member before the double-tap arrives (D-13). Nothing tested the collapse.
14. Not in the plan: Ctrl+click can select a container AND its child; Delete/nudge over both would double-move the child
    (D-11's top-level rule).

## 9. Plan review of `086096a4` — dispositions (coordinator rulings + two owner instructions, 2026-10-05)

| # | Item | Disposition |
|---|---|---|
| 1 | Task 9 order: records + IDE drop first, then merge check, then the gate on the merged tree | Task 9 rewritten in that order; HANDOFF written once (step 1), gate numbers go to §6/PR body so the post-gate commit cannot conflict |
| 2 | Regenerate the oracle only at the same runtime; diff must be ONLY added `mergeable` keys; TabIndex `[MergableProperty(false)]` | Task 1 stop-rule added; M1 expects TabIndex false; D-2 rule 3 drops TabIndex; Task 1 and Task 2 tests expect it; the real spelling "Mergable" noted |
| 3 | Canvas double-click on a member: one handler after the clicked/primary control, wire all sharing the event | D-8 canvas route (SelectForGesture keeps the group, ActivateControl passes the sharing owners, `:910` no collapse, a member without the event left unbound); tested in Task 5 + click-through 18a |
| 4 | Primary promotion | D-13 + new Task 4b (`FormSelection.Promote`, ApplyClickSelection `:829-834`); collapse-on-release REMOVED (VS; needed by item 3); click-through 13/18 reworded |
| 5 | D-11 ancestor/descendant + docked | D-11 extended (`FormSelectionTopLevel`, docked skipped via `FormDockLayout.EdgeOf`); Task 6 tests for each (Delete, nudge, docked) |
| m1 | D-3 Reset/bold = VS | Adopted: bold ANY, Reset ALL (`CanResetValue` all); recorded with the consequence (bold, no Reset); Task 2/3 tests |
| m2 | D-9 re-entrancy through the editor echo path | D-9 rules (notify only, skip a row with `_editorEcho`); Task 3 VM test + Task 4 (i) real view |
| m3 | Mixed Int un-mixed while focused | D-4 rule; Task 4 (j) real view + its ★ mutation |
| m4 | RefuseHandler by Owners | D-8 bullet; Task 5 file + test |
| m5 | AddIntrinsicRows returns rows per member | Task 2 file list (one builder, two consumers) |
| m6 | One-store invariant after every canvas gesture | Task 2 data-driven real-view test; promotion and Delete rows added in Tasks 4b and 6 (the collapse-on-release row is gone with D-13) |
| O1 | Owner: test cadence | §4 preamble: per task only the touched fixtures + 1–2 ★ mutations; Task 0 baseline run dropped; ONE gate |
| O2 | Owner: no full suite, not even in Task 9 | Full-suite step removed; Task 9 step 3 = fast subset + property-grid Integration fixtures on the merged tree, names vs master, A/B any new name |
