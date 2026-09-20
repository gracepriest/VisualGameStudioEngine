# Task 24 — Menus, toolbars and status bars: design

Branch `feat/form-designer`. Follows the Task 25 tray (`2026-09-19-component-tray-design.md`), whose
"who visits" table and gates this design extends. Written 2026-09-19 without a human in the loop;
every decision that a human would normally take is listed at the end with its reason.

## The brief, verbatim

> ⛔ This one is blocked on a compiler change. Do that first, as its own gated task.
>
> The spec scopes menus out of v1 for a measured reason: the canonical idiom
> `menuStrip.Items.AddRange(New ToolStripItem() { … })` is not expressible in BasicLang today. […]
> Fix one of the two, gate it, and only then build the designer surface:
>
> Preferred: give array-literal element typing a common-base-type widening rule, so `{mnuFile,
> sep1}` types as `ToolStripItem()`. Smaller and more generally useful than new syntax.
> Alternative: support `New T() { … }` array-creation-with-initializer in the parser.
> ⛔ Either is a SemanticAnalyzer/Parser change reaching shared compiler machinery — full suite
> required, and element typing is used well beyond menus, so check what else moves.
>
> Then the designer work: a menu/toolbar/status-bar editor is a nested, non-positional tree, not a
> positioned box on the canvas, so it needs its own in-place editing surface (the VS "Type Here"
> strip). On the web side, emit honest markup — a `<nav>`/`<ul>` menu and a status bar element —
> not a WinForms menu drawn in HTML.

## Measured facts this design rests on (2026-09-19, real CLI → csc → RUN)

Every row below was produced by generating a program, compiling it with the real
`BasicLang.exe --target=csharp`, building the C# with csc through a `net8.0-windows`
`UseWindowsForms` project, and — where it says RUN — running it. Scratch files under the session
scratchpad `t24/`.

| # | Shape | BasicLang | csc | RUN |
|---|---|---|---|---|
| M0 | a class named `F` | ⛔ **every `Me.` member lookup fails** — "Type 'F' does not have a member 'InitializeComponent'/'Text'/'Controls'"; renamed `MenuForm`, identical file compiles | — | — | 
| M1 | `menuStrip1.Items.AddRange({mnuFile, sep})`, `ToolStripMenuItem` + `ToolStripSeparator` | warning "Collection contains mixed types", emits `new object[2]` | **CS1503** object[] → ToolStripItem[] | — |
| M2 | `AddRange({mnuFile, mnuEdit})`, same type | emits `new ToolStripMenuItem[2]` | ✅ | ✅ |
| M3 | `Dim items() As ToolStripItem = {mnuFile, sep}` | ⛔ **refused** "Cannot assign value of type 'Object[]' to variable of type 'ToolStripItem()'" — a declared array type does NOT target-type the literal (`Dim items As ToolStripItem() = …` is a parse error: not BasicLang syntax) | — | — |
| M4 | `AddRange(New ToolStripItem() {mnuFile, sep})` | ⛔ parse error "Expected ')' after arguments but found LeftBrace". ⚠ Read against the parser (spec review): `New ToolStripItem()` is consumed by the `New` branch as a parameterless CONSTRUCTOR call (`Parser.cs:4286-4295`); `ParseTypeReference` has no array-suffix branch at all. The message comes from the enclosing `AddRange(` argument list meeting `{` (`:4183/:4201`, identical text) | — | — |
| M5 | per-item `Items.Add` / `DropDownItems.Add`, `Me.MainMenuStrip = menuStrip1`, `Me.Controls.Add(strip)`, `AddHandler mnuOpen.Click` | only BL6017 warnings (members type as Object) | ✅ | ✅ `PerformClick` on a menu item and a toolbar button both fire; `Items.Count` 1, `DropDownItems.Count` 3 |
| M6 | `mnuSave.ShortcutKeys = Keys.Control Or Keys.S` | ⛔ "Logical operator 'Or' requires Boolean operands" (the Anchor measurement, again) | — | — |
| M7 | `mnuOpen.ShortcutKeys = CType(131151, Keys)` | ✅ emits `(Keys)(131151)` | ✅ | ✅ |
| M8 | csc property probe, one assignment per line (`t24/probe/Probe.cs`) | — | **refused:** `ToolStripMenuItem.TabIndex` CS1061, `ToolStripMenuItem.Location` CS1061, `menuStrip.Controls.Add(item)` CS1503 (not a Control), `menuStrip.Items.Add(button)` CS1503. **Accepted:** every other line — Text/Enabled/Visible/Checked/CheckOnClick/ShortcutKeys/ToolTipText/ForeColor/BackColor/Name on a menu item; **Size, Anchor and Dock ALSO compile on a ToolStripItem** (so csc will not catch a stray one — the catalog must simply not offer them); MenuStrip Dock/Text/Enabled/Visible/TabIndex/Location/Size; ToolStrip.GripStyle; StatusStrip.SizingGrip; ToolStripButton.DisplayStyle/Checked/CheckOnClick/ToolTipText; ToolStripStatusLabel.Spring/ToolTipText; ToolStripSeparator.Visible/Enabled; `Items.Add("text")` (returns a ToolStripItem); a ToolStripButton in a StatusStrip and a ToolStripMenuItem in a ToolStrip (any ToolStripItem in any strip) | — |
| M9 | order and docking probe (`t24/order/Order.cs`), RUN | — | — | `Items.Add` order IS left-to-right display order (`Bounds.Left` 6, 43, 82); `DropDownItems.Add` order is top-to-bottom; `PerformClick` fires BEFORE the form is shown and after; with `Controls.Add` in VS's reverse order (status, tool, menu) the menu is at Top=0 (height 24), the toolbar at 24, the status strip at the bottom (height 22) |

**What the facts decide.**

- The brief's premise is true (M1, M3, M4) and its conclusion is not: **the designer's own emission
  needs no compiler change.** Per-item `Items.Add` — the shape the region writer already uses for
  a ComboBox's `Items` and the shape older VS designers emitted — compiles clean and RUNS (M5, M9).
  The compiler change is done anyway, as the brief directs and as its own gated task (§9), because a
  user pasting the VS idiom by hand meets M1/M4 today.
- **The "preferred" widening rule cannot produce the brief's example.** `mnuFile` and `sep` are two
  DIFFERENT unresolvable .NET types (the resolver cannot reach `System.Windows.Forms.dll`, see
  `CLAUDE.md`); the analyzer has no base-class chain for either, so there is nothing to widen to —
  `{mnuFile, sep1}` can never type as `ToolStripItem()` by inference. The other reading of
  "preferred" — target-typing a bare literal from a declared array type, the M3 shape — would serve
  `Dim items() As ToolStripItem = {…}` but not the brief's inline `AddRange({…})`, whose parameter
  type is unknown with WinForms resolution off. Widening is generally useful for BasicLang classes
  (whose `Inherits` the analyzer knows) and is filed as a followup; neither reading unblocks the
  idiom. **The alternative does**: `New ToolStripItem() {…}` names the element type explicitly,
  needs no resolution, and csc accepts the emitted `new ToolStripItem[] {…}` by implicit reference
  conversion (M2 is the same conversion one step down). §9.
- **A bare array literal does not run on the JavaScript backend today** (spec review, read: the
  backend throws `NotYet` for `IRArrayAlloc`/`IRArrayStore` at statement level,
  `JavaScriptBackend.cs:2379-2380`, and in the expression renderer, `:747-748`; measured: the CLI
  prints "Compilation successful!" for `Dim a() As Integer = {1, 2, 3}` with `--target=javascript`
  and writes NO output file). §9 scopes that emission in — a literal the checker approves and the
  backend then dies on is the green-build-dead-page class this branch keeps meeting.
- `ShortcutKeys` is expressible only as `CType(n, Keys)` (M6/M7). A key-combination editor is a
  property-grid type editor plus a name→value table; **deferred** (followup 22), not shipped as a
  raw integer row.
- M0 is a compiler bug unrelated to menus, filed as chip `task_ef845b99`. Every fixture in this task
  names its form `MenuForm`, never `F`.

## 1. What a strip and an item ARE in the model

The Task 25 argument holds again: every command, the selection, the grid, the clipboard, the
retarget and both writers are typed on `FormControl`, so a strip item is a `FormControl`. The
catalog row says what SHAPE of control it is. Today the row has two shape flags (`IsContainer`,
`IsComponent`) and a strip needs a shape neither expresses — *no geometry BUT children* — so the
shape becomes ONE enum, `FormControlDef.Place`:

| `Place` | Geometry | TabIndex | Children | Lives in | Added to its parent by |
|---|---|---|---|---|---|
| `Positioned` (default) | pixels / cell | yes | if `IsContainer` | `Controls` | `Controls.Add`, REVERSED (z-order) |
| `Tray` (= today's `IsComponent`) | none | no | no | `Components` | never |
| `Docked` — MenuStrip, ToolStrip, StatusStrip | none; a `Dock` PROPERTY (Top/Bottom) | no | its `Items` rule | `Controls` | `Controls.Add`, REVERSED (docking order — measured M9: reverse add puts the first strip on top) |
| `Item` — ToolStripMenuItem, ToolStripSeparator, ToolStripButton, ToolStripStatusLabel | none | no | its `Items` rule, if any | its host's `Children` | the HOST row's `Items.Add` verb, in DOCUMENT order (measured M9: add order is display order) |

`IsComponent` stays as a derived property (`Place == Tray`) so the eleven sites that read it do not
churn; new consumers ask `Place`. A `FormItemRule(IReadOnlyList<string> Kinds, string Add)` on a
HOST row states which item kinds it holds and the verb that adds one:

- MenuStrip: `Kinds = {ToolStripMenuItem, ToolStripSeparator}`, `Add = "{parent}.Items.Add({child})"`
- ToolStripMenuItem: the same kinds, `Add = "{parent}.DropDownItems.Add({child})"`
- ToolStrip: `{ToolStripButton, ToolStripSeparator}`, `Items.Add`
- StatusStrip: `{ToolStripStatusLabel}`, `Items.Add`

The verb lives on the PARENT's row because the same ToolStripMenuItem is added with `Items.Add`
under a strip and `DropDownItems.Add` under a menu item. Document order is a property of the rule,
not derived from the verb's spelling — "ask the row, never the shape of the string". Two contracts
the rows carry implicitly, stated: a host row keeps `IsContainer = false` (`ContainerAt` and the
toolbox category both branch on it — a host set true becomes a drag-reparent target and sorts under
Containers); and `Kinds[0]` is the DEFAULT kind Type Here creates, which is why the separator is
listed second on every rule.

**The nesting invariant, enforced in ONE place (the reader), refused as BL8030** (the band's one
free number; BL8031 stays reserved): an `Item` may appear only under a host whose rule lists its
kind; a non-`Item` may not appear under a host; and a `Docked` strip may appear only at the top
level of `<Controls>` — a strip docks to the FORM (VS allows one inside a Panel; the band model and
the web chrome do not, and v1 says so rather than answering differently in three places). The first
two directions generate code csc rejects (M8: `menuStrip.Controls.Add(item)` CS1503,
`Items.Add(button)` CS1503), exactly BL8020's argument. WinForms itself accepts any ToolStripItem
in any strip (M8), so "a ToolStripButton in a MenuStrip" is refused by the DESIGNER's rule, not
csc's — stated so nobody "fixes" the refusal to match csc. `FormClipboard.FromElement` is the
reader's MIRROR and carries the same `Place` branch in the same commit (it branches on
`IsComponent` alone today, `FormDocument.cs:425-434`, and would hand a pasted strip the zero rect
and drop its `Dock`).

## 2. Who visits strips and items — decided per walker, from the recon

Three readers and a critic mapped every walker (workflow `wf_3d3e64ff-7d0`, 2026-09-19). Each row
below is a decision; "as-is" means the recon read the code and nothing changes.

| Walker | Decision |
|---|---|
| `FormDocumentReader.ReadControl` | nests under any catalog kind today (`:473`, no `IsContainer` check). Gains the `Place` branch: `Docked`/`Item` never acquire geometry or TabIndex; their attribute loop skips ONLY `Id`, then the catalog lookup, then unknown attributes (the component branch, `:395-397`) — so `Dock` reaches `definition.Property("Dock")` instead of being skipped as a structural NAME; BL8030 for the nesting invariant; `Dock` on a strip is a catalog PROPERTY, not `PixelGeometry.Dock` — so `<MenuStrip Dock="Top"/>` reads `Geometry == null`, never the `{0,0,0,0,Dock}` zero rect the recon found. ⚠ The pin `FormDocumentTests.Catalog_NoPropertyCollidesWithAStructuralAttribute` (`:79-92`) refuses ANY property named like a structural attribute; it exempts rows whose `Place != Positioned`, with the reason beside it (the write-twice hazard it guards exists only where geometry is written) |
| `FormClipboard.FromElement` / `ToElement` | the reader's and `ControlElement`'s MIRROR: the same `Place` branch (no geometry, no TabIndex, `Dock` as a property, children kept), same commit; pinned by a paste test (§7) |
| `CheckDuplicateIds`, `FindById`, `ListContaining`, `AllControls` | as-is — items are in the visual tree; Delete/Cut/rename cover them |
| `RenumberTabIndexes`, `FormPlacement.NextTabIndex` | skip `Place != Positioned` — items and strips are inside `AllControls()`, so exclusion is per ROW, unlike the tray's per-list exclusion |
| `FormDocumentWriter.Create` / `ControlElement` | no TabIndex and no geometry for `Place != Positioned` (today `:557` writes TabIndex unconditionally on every non-component — the recon's invented-content wound); children recurse as today |
| `FormDocumentWriter.Apply` / `ReorderToMatchModel` | as-is for nesting and ORDER (order is what a menu is; the model-order write is exactly right); the TabIndex set-if-changed gains the same `Place` guard |
| `RegionWriter.GenerateControls` | as-is — `Private mnuFile As ToolStripMenuItem` is VS's own shape |
| `RegionWriter.AppendSiblings` | receives the PARENT control (today only its id string); asks the parent row for the add verb and the order: `Items` rule → its `Add`, document order; else `Controls.Add` reversed. ⛔ Recon rank 1: forget the verb and csc says CS1503; forget the order and `File/Edit/Help` runs as `Help/Edit/File` from a green build |
| `RegionWriter.AppendControlInit` / `AppendProperties` / `AppendBinds` | as-is; a strip's `Dock` is a property row with `WinFormsEnumType: "DockStyle"` and emits `menuStrip1.Dock = DockStyle.Top`. ⚠ The ORDER rule, stated: a host's items are constructed, set, wired and added to the host (host verb, document order) BEFORE the host is added to its own parent — which is what the existing `AppendControlInit → AppendSiblings(children)` recursion already does; nobody restructures it |
| `RegionWriter.GenerateInit` | WinForms ONLY (a web class has no such member): after the top-level `AppendSiblings` call returns — i.e. AFTER the `Controls.Add` run, one line after `:533`, inside the WinForms branch: `Me.MainMenuStrip = {id}` for the FIRST MenuStrip in document order (row flag `FormProperty = "MainMenuStrip"`; VS emits it; measured harmless in either position, M9, kept because Alt-key menu navigation needs it). The add run itself stays after the whole sibling construct run, as today |
| `RegionWriter` checks | `CheckHandlerOrdering`, `CheckTargetProperties` cover items as controls; BL8029 gains the control-side twin for an `Item`/`Docked` kind with no row on the target (none exists — every strip and item row has both targets, pinned by coverage) |
| `FormHandlers.PlanDefault` | as-is; item rows declare `WinFormsEvent: "Click"` / `WebEvent: "click"` (a separator too — `Click` is inherited, honest); strip rows declare `ItemClicked` (`WinFormsEventArgs: "ToolStripItemClickedEventArgs"`, VS's own default for a strip) / `click` — two catalog gates require an event per supported target (`FormCatalogCoverageTests:142-158`, the csc default-event sweep) and a real, csc-gated event is better than exempting a shape from both |
| `PasteControls` | copy of a strip carries its items. A pasted ITEM root goes into the selection's primary when that is a host accepting the kind, else the paste is refused-and-reported (BL8019 path); never into `Controls`. A pasted strip lands top-level, geometry-less, `Dock` kept, no offset |
| `FormPlacement.Place` | `Docked`: into `Controls`, no geometry, `Dock` from the row's default, the point ignored (like the tray); `Item`: a NEW entry point `PlaceItem(document, host, kind, text)` — a parent by identity, never a point |
| `FormRetarget.ConvertControls` | as-is for nesting; `Dock` is a shared property (both targets) so it crosses. The web→pixels `Place` rule, stated once: `Docked`/`Item` subtrees are EXCLUDED from the sizing and pitch pass — never recursed into, never given a `PixelGeometry` — and a sibling list with no positioned control returns (0,0) BEFORE `Max` is reached (`:610-611` throws on an empty dictionary; a page whose top level is a MenuStrip alone is exactly the sweep's canonical shape). `DeriveCells` needs no change (its else-branch already leaves a control with no source geometry at null). `Hoist` never sees a strip: every host and item row has both rows (coverage pin) |
| `FormAssetEmitter.Html` | `Docked` strips are PAGE CHROME, not cells: Top-docked strips are written BEFORE the `.vgs-form` div in document order, and Bottom-docked ones AFTER it in REVERSE document order — because WinForms docks the LAST-added child first and the region writer adds siblings in reverse (M9), the first Bottom strip in document order sits on the bottom EDGE and a second one stacks above it; forward order after the div would invert that, and window, page and canvas must agree. Items nest inside. No `tabindex` for `Place != Positioned`. `&` in a caption is a WinForms accelerator and is STRIPPED from an Item row's web text (the honest web caption of `&File` is `File`) |
| `FormAssetEmitter.AppendControl` | row-declared `HtmlChildrenWrapper` ("ul" on MenuStrip and ToolStripMenuItem) wraps the children; row-declared `HtmlRole` ("toolbar", "status", "separator") is a fixed attribute; a row's `WebCss` is appended once per kind present (the horizontal bar, hidden submenus shown on hover) |
| `FormControlCatalog.FindByHtmlTag` | ToolStripButton is `<input type="button">` (Text → `value`), NOT `<button>`: a second row owning `button` makes the tag ambiguous and the DOM recognizer would stop naming a plain Button (`FormRecognizerTests` web-template pin). `li` is shared by ToolStripMenuItem and ToolStripSeparator — both new, so no regression; the recognizer names a bare `createElement("li")` BL8004, recorded |
| `WinFormsDialect.MarkParented` (recognizer) | learns `<host>.Items.Add(<id>)` and `<host>.DropDownItems.Add(<id>)` as parenting shapes, or `design --check` reports every item BL8006 "never added" |
| `FormCanvasTransform.Layout` — the ONE place | bands and cells are computed in ONE method that has everything they depend on: `Layout(document, selected)` receives the document (surface width, sibling order) and the SELECTED control (a strip, an item, a positioned control, or null) and yields `(control, bounds, role)` entries in paint order — positioned controls as today; then every `Docked` strip as a BAND (full surface width, the row's `DefaultHeight`, Top strips stacked from y=0 in document order, Bottom strips stacked from the bottom EDGE upward in document order — the first Bottom strip in the document is on the edge, matching the window (M9) and the page; never a 0x0 rect — recon rank 2, phantom grips at the origin) with its top-level items as CELLS; then, derived from `selected`, the EXPANSION PATH: the active strip (the selected strip, or the selected item's strip) gets ONE `TypeHere` slot entry (no control) after its cells; then EVERY item ancestor of the selection, plus the selected item itself when it is a host, is expanded — its dropdown cells and its own slot — yielded outermost-first and LAST so they paint over later controls. A dropdown under a BAND cell opens below it; a NESTED dropdown (a menu item's children) opens to the RIGHT of its cell at the same y, as VS does, so it never overlaps its parent's remaining cells or slot — with one placement rule the two would paint on top of each other and `TypeHereAt` would return the wrong slot. A path, not one item: after `Commit("Open")` selects the new `mnuOpen`, `mnuFile`'s dropdown (its parent) is still open with its slot, which is the slot the editor re-opens on; with one expanded item the second commit had no cell to draw under and no slot to publish. No other slot exists — a freshly dropped strip is selected, so its slot is there; nothing else's is. Entry shape: `(FormControl? Control, Rect Bounds, FormLayoutRole Role)` — a 3-tuple with a nullable control, so EVERY consumer is updated deliberately (`FormBoundsOf`, `ControlsIn`, the two render loops at `FormCanvasControl.cs:993/:1108`, the transform tests); `selected` defaults to null so the existing call sites compile, and the parameter is named `selected` everywhere (the expansion path is DERIVED from it; a caller passing an expanded item instead loses the path). `HitTest(document, canvasPoint, selected)` and `TypeHereAt(document, canvasPoint, selected)` take the selection explicitly — a click on a dropdown cell exists only relative to it — and all three canvas callers pass it: the press, the right-click and `OnCanvasDoubleTapped` (`:498`, `:583`, `:617`), or a double-click on a dropdown cell opens nothing. ⚠ Unifying the WinForms `HitTest` onto `Layout` changes one edge: the recursive walk descended into a child only inside its container, while `Layout` yields child rects unclipped, so a child overflowing its Panel becomes hittable where it is PAINTED — which agrees with the picture; `ContainerAt` stays on `BoundsOf`, so a drop into that overflow can still disagree, and a test pins whichever the plan chooses. `BoundsOf` stays what it is (a positioned control's rect); it is never asked about a strip. ⛔ WinForms `HitTest` and `ContainerAt` walk `BoundsOf` recursively today (`:105-126`, `:266-294`) while the web branch reads `Layout` — both targets now read `Layout`, so a click, a drop and a paint cannot disagree (the transform's founding rule). Every consumer of `Layout` — both `HitTest` branches, `ControlsIn`, `FormBoundsOf`, the render pass — skips an entry with no control; `TypeHereAt` is the one method that returns it. `ContainerAt` never returns a strip or an item. `ControlsIn` (the marquee) excludes `Place == Item` AND `Place == Docked` — a rubber band is for positioned controls; VS band-selects neither a menu item nor, in practice, a bar. `WebLayout` yields the chrome bands regardless of the page's layout kind (a Flow page has a menu too) |
| `FormCanvasControl.DrawControl` / `Render` | drawing is routed through ONE seam, `DrawSchematic(context, schematic, bounds, label, …)`, which `DrawControl` calls with the row's schematic — so the per-value pin exists in 24b, before any row: `Enum.GetValues<FormSchematic>()` → every value's frame hashed through the seam at one bounds, ALL PAIRWISE DISTINCT (not only "differs from the `Input` fallback" — once items leave the document-level hash nothing else asserts a MenuItem and a Separator paint differently, and ten kinds once drew as one grey box), and `GlyphFor` is not `?` and all distinct (the existing glyph gates iterate TOOLBOX rows and would never see an item schematic's missing arm). Three new schematics (`MenuBar`, `ToolBar`, `StatusBar`) draw the band; item schematics (`MenuItem`, `Separator`, `ToolButton`, `StatusLabel`) draw a cell, never a box; the `TypeHere` entry draws the greyed slot |
| `FormCanvasControl.OnPointerPressed` | a `TypeHere` entry under the pointer is tested BEFORE the marquee branch (`:614-631` starts a rubber band whenever `HitTest` returns null) and raises the canvas's bindable `BeginTypeHereCommand` with the host; a cell press selects the item through `Selection` and arms NO drag |
| `FormCanvasControl.TypeHereHost` / `TypeHereBounds` | the canvas OWNS the form→canvas mapping (`_transform`, recomputed by `Fit` on every render), so it needs an INPUT naming the slot to measure: a styled property `TypeHereHost` (bound to `StripEditor.Host`, null when the editor is closed) — with an expanded item there are TWO visible slots, and after Enter the editor re-opens with no click. From it the canvas publishes `TypeHereBounds`, the active slot's CANVAS-space rect, set at the end of each render pass from the same `Layout` entries it painted (so it moves with a resize, a zoom and the one-cell shift after a commit); `TypeHereBounds` is NOT in `AffectsRender` (a property written during Render must not schedule another). The view binds the overlay to it; the view model never computes a rectangle |
| `FormGeometryEdit` / `FormArrange` | a `Docked` strip is never moved, resized or re-parented (its geometry is null, so every edit already early-returns — pinned) |
| `FormPropertyGridViewModel.AddIntrinsicRows` | no TabIndex row for `Place != Positioned`; no geometry rows (null geometry already); the strip's `Dock` is a catalog row like any other |
| `FormToolboxViewModel` | a fourth category, **Menus & Toolbars**, holding the three strips. Item kinds are NOT toolbox rows (VS does not list them either); they are created by Type Here. ⚠ Two pins assert the toolbox offers EVERY `For(target)` kind — `FormPropertyGridTests.TheToolbox_OffersOnlyKindsThatExistOnTheTarget` and `FormToolboxGlyphTests.TheToolboxOffersEveryCatalogKindForItsTarget` — and `Rebuild` iterates `For(Target)` with no exclusion; both pins and `Rebuild` learn `Place != Item` in 24b, BEFORE the rows land |
| `FormTrayViewModel` | unchanged; the tray is the MODEL for "rebuild from the document, select through the one store", not the surface (a menu is a tree) |
| `CodeEditorDocumentViewModel` | `PlaceControl` unchanged for strips (the point is ignored); new `StripEditor` (§6) with `BeginTypeHere` / `CommitTypeHere` / `CancelTypeHere`; `SelectInDesigner` for every selection (the 25e rule) |
| Gates (`WinFormsCatalogSweepTests` ×3, `FormCanvasRenderTests`, `FormRetargetTests` sweep) | each chooses its shape by `IsComponent` alone today. They move onto ONE helper, `FormCatalogShapes`, BEFORE any row is added (commit 24b, so no gate is ever red between commits): `Canonical(document, definition, id, hostId = null) → FormControl` adds the row's canonical shape and returns the control it added — Positioned → geometry in `Controls`; Tray → `Components`; Docked → top-level in `Controls`, geometry-less, `Dock` from the row; Item → the first DOCKED host row listing it (deterministic: never a menu item hosting a menu item, so catalog row order cannot turn the canonical shape into an item under nothing) is added first (its own canonical shape, id `hostId ?? id + "Host"`), the item as its child; `Locate(document, definition) → FormControl?` finds that control again on the crossed side for the retarget sweep (an item is `Controls[0].Children[0]`, not a top-level `Single()`); the enum sweep's N-controls case is N hosts each with one item (`ctl{i}Host`/`ctl{i}`). The render fixture passes `hostId: SharedId` so host and item share the one id its rule requires, and a band draws no caption anyway (§6). A fifth shape cannot be forgotten by three gates again |
| `FormDocumentLoader` (build) | as-is: a refused document skips its page with a warning, as BL8020 does today |

## 3. The document

```xml
<Form Name="MainForm" Version="1" Width="640" Height="480" Text="Main">
  <Controls>
    <MenuStrip Id="menuStrip1" Dock="Top">
      <ToolStripMenuItem Id="mnuFile" Text="&amp;File">
        <ToolStripMenuItem Id="mnuOpen" Text="&amp;Open...">
          <Bind Event="Click" Handler="mnuOpen_Click"/>
        </ToolStripMenuItem>
        <ToolStripSeparator Id="sep1"/>
        <ToolStripMenuItem Id="mnuExit" Text="E&amp;xit"/>
      </ToolStripMenuItem>
    </MenuStrip>
    <ToolStrip Id="toolStrip1" Dock="Top" GripStyle="Hidden">
      <ToolStripButton Id="tsbOpen" Text="Open"/>
    </ToolStrip>
    <Button Id="btnGo" Text="Go" X="16" Y="80" Width="75" Height="23" TabIndex="0"/>
    <StatusStrip Id="statusStrip1" Dock="Bottom">
      <ToolStripStatusLabel Id="lblStatus" Text="Ready" Spring="true"/>
    </StatusStrip>
  </Controls>
  <Components/>
  <Resources/>
</Form>
```

- Strips live under `<Controls>` (they ARE controls, docked); items nest under their host. The
  `.blwebform` shape is identical — `Dock` is a shared property, Top/Bottom only.
- No `X`/`Y`/`Width`/`Height`/`TabIndex` on a strip or an item: written by neither route, and on
  read they fall through to unknown attributes (D9, the component rule), never to geometry.
- BL8030 `StripMisplaced`, a REFUSAL three ways: an item outside a host that lists its kind
  ("'mnuOpen' is a ToolStripMenuItem, which lives inside a MenuStrip or a menu item — under
  <Controls> it would be added with Me.Controls.Add, which does not compile"), a non-item under a
  host ("'btnGo' is a Button, but it sits under 'menuStrip1'. A MenuStrip holds only
  ToolStripMenuItem, ToolStripSeparator"), and a strip below the top level ("'menuStrip1' is a
  MenuStrip, which docks to the form — it sits under 'pnl'"). The band comment in
  `DesignDiagnostic.cs` takes BL8030, its last ENUMERATED free number, and says the next claim
  starts at BL8032 (BL8031 stays reserved; BL8032..BL8999 are simply unclaimed — "full" would send
  the next task to another band).
- The D9 algebra (byte-identical round trip, no-op writes nothing, `Read∘Apply == Apply∘Read`) is
  restated over a document with a nested strip, because no fixture has one.

## 4. The rows (every name below is csc-gated, M8)

| Kind | WinForms type | Place | Properties | Default event | Web |
|---|---|---|---|---|---|
| MenuStrip | `MenuStrip` | Docked, `DefaultHeight 24` | `Dock` Enum Top/Bottom = Top (`DockStyle`), `Enabled`, `Visible` | `ItemClicked` (`ToolStripItemClickedEventArgs`) / `click` | `<nav>`, children in `<ul>` |
| ToolStrip | `ToolStrip` | Docked, 25 | `Dock` = Top, `GripStyle` Enum Hidden/Visible = Hidden (`ToolStripGripStyle`), `Enabled`, `Visible` | `ItemClicked` / `click` | `<menu role="toolbar">` |
| StatusStrip | `StatusStrip` | Docked, 22 | `Dock` = Bottom, `SizingGrip` Bool = false, `Enabled`, `Visible` | `ItemClicked` / `click` | `<footer role="status">` |
| ToolStripMenuItem | `ToolStripMenuItem` | Item, host of menu items | `Text`, `Enabled`, `Visible`, `Checked`, `CheckOnClick`, `ToolTipText` | `Click` / `click` | `<li>`, children in `<ul>` |
| ToolStripSeparator | `ToolStripSeparator` | Item | `Visible` | `Click` / `click` | `<li role="separator">` |
| ToolStripButton | `ToolStripButton` | Item | `Text`, `Enabled`, `Visible`, `Checked`, `CheckOnClick`, `ToolTipText`, `DisplayStyle` Enum None/Text/Image/ImageAndText = Text (`ToolStripItemDisplayStyle`) | `Click` / `click` | `<input type="button">` (Text → `value`) |
| ToolStripStatusLabel | `ToolStripStatusLabel` | Item | `Text`, `Enabled`, `Visible`, `Spring` Bool, `ToolTipText` | `Click` / `click` | `<span>` |

- No `Common()`: `Visible`/`Enabled` compile on every row above (M8) but `ForeColor`/`BackColor`
  are noise on a strip and `Text` on a strip is meaningless; rows list what they mean.
- **Targets, per property** (Task 25 decided `Enabled` per property; so here): shared by both
  targets — `Text` (content, or `value` on the input), `Visible` (the stylesheet's `display:
  none`), `Dock`, `ToolTipText` (the ONE property with an honest attribute form, `HtmlAttribute:
  "title"` — the record's constructor parameter; `HtmlAttributeName` is its read-only view), and
  `Enabled` ONLY on ToolStripButton, whose `<input>` honours the emitter's ` disabled`. On the six
  other rows `Enabled` is WinForms-only: the emitter appends ` disabled` by NAME to whatever tag a
  row declares (`FormAssetEmitter.cs:180`), and a browser ignores it on `<nav>`, `<menu>`,
  `<footer>`, `<li>` and `<span>` — a disabled menu item would stay clickable on the page from a
  green build, and no gate could see it. WinForms-only too (`Targets: WinForms`) — `Checked`,
  `CheckOnClick`, `DisplayStyle`, `Spring`, `GripStyle`, `SizingGrip`: the emitter appends `
  checked` by name as well (`:181`), and the retarget sweep derives crossed/lost EXACTLY from
  `AppliesTo`. A row's web absence is the property grid's too. ⚠ `Flag()` and the reader are
  target-blind, so a HAND-WRITTEN `Enabled="false"` on a web `<li>` still reaches `:180` — inert in
  the browser, and the sweep honestly reports it lost.
- `Dock` here is a catalog PROPERTY that shares its NAME with the structural attribute positioned
  controls carry in their geometry. When the attribute is ABSENT on a hand-written strip, the
  emitter's before/after split and `Layout`'s band edge read the row's `Default`
  (`FormPlacement` stamps it on a drop; a hand edit need not), the `ExpandWebScript` precedent. The structural-name pin exempts non-positioned rows (§2), and
  on a strip the reader routes the attribute to the property, never to geometry; the property grid
  therefore shows ONE `Dock` row for a strip (the property) and the picker (Task 26) is not
  involved. `ItemClicked` on the strips is unprobed by M8 and is gated by the default-event sweep
  the day the rows land, like every other event name.
- Unqualified type names: the scaffold imports `System.Windows.Forms`, and no `ToolStrip*` name
  contains "Thread" (the Timer trap). Pinned by the csc sweep either way.
- `ShortcutKeys` (followup 22), images, `ContextMenuStrip`, `ToolStripDropDownButton`,
  `ToolStripComboBox`/`TextBox`, `ToolStripProgressBar`: out (§8).
- `FormSchematic` gains `MenuBar`, `ToolBar`, `StatusBar`, `MenuItem`, `Separator`, `ToolButton`,
  `StatusLabel` — each with a `GlyphFor` arm AND a `DrawControl` arm (the recon: one without the
  other ships a `?` or a grey box undetected).

## 5. Emission

**WinForms** (per-item Add — measured to run, M5/M9; no `AddRange`, no array literal):

```basic
    Private menuStrip1 As MenuStrip
    Private mnuFile As ToolStripMenuItem
    Private mnuOpen As ToolStripMenuItem
    Private sep1 As ToolStripSeparator
    …
    Private Sub InitializeComponent()
        Me.Text = "Main"
        Me.ClientSize = New Size(640, 480)
        menuStrip1 = New MenuStrip()
        menuStrip1.Dock = DockStyle.Top
        mnuFile = New ToolStripMenuItem()
        mnuFile.Text = "&File"
        mnuOpen = New ToolStripMenuItem()
        mnuOpen.Text = "&Open..."
        AddHandler mnuOpen.Click, AddressOf mnuOpen_Click
        sep1 = New ToolStripSeparator()
        mnuExit = New ToolStripMenuItem()
        mnuExit.Text = "E&xit"
        mnuFile.DropDownItems.Add(mnuOpen)          ' the HOST row's verb, DOCUMENT order
        mnuFile.DropDownItems.Add(sep1)
        mnuFile.DropDownItems.Add(mnuExit)
        menuStrip1.Items.Add(mnuFile)
        …
        Me.Controls.Add(statusStrip1)                ' siblings REVERSED, as today: docking order
        Me.Controls.Add(btnGo)
        Me.Controls.Add(toolStrip1)
        Me.Controls.Add(menuStrip1)
        Me.MainMenuStrip = menuStrip1                ' AFTER the add run: first MenuStrip in document order
    End Sub
```

Never `With`, never `Handles`, geometry fans in (none here), a Degraded value never reaches source.

**Web** — page chrome, honest markup, no script for the menu (CSS `:hover` opens submenus; the
node harness has no `classList`, and a hover-only menu is what a static page honestly is):

```html
<nav id="menuStrip1" class="vgs-MenuStrip"><ul>
  <li id="mnuFile" class="vgs-ToolStripMenuItem">&amp;File<ul>
    <li id="mnuOpen" class="vgs-ToolStripMenuItem">&amp;Open...</li>
    <li id="sep1" class="vgs-ToolStripSeparator" role="separator"></li>
    <li id="mnuExit" class="vgs-ToolStripMenuItem">E&amp;xit</li>
  </ul></li>
</ul></nav>
<menu id="toolStrip1" class="vgs-ToolStrip" role="toolbar"><input id="tsbOpen" class="vgs-ToolStripButton" type="button" value="Open"></menu>
<div class="vgs-form">…positioned controls, in their cells…</div>
<footer id="statusStrip1" class="vgs-StatusStrip" role="status"><span id="lblStatus" class="vgs-ToolStripStatusLabel">Ready</span></footer>
```

The chrome is written inside `<body>` and outside the `.vgs-form` div. ⛔ `data-form` stays on
`<body>` exactly where the emitter writes it today (`FormAssetEmitter.cs:112`) — the generated
dispatch reads it from `doc.body` and the node harness stubs it there; a listing that showed it on
the div would have moved the one attribute every page's construction hangs on.

The init region is unchanged in shape: `mnuOpen = doc.getElementById("mnuOpen")` then
`mnuOpen.addEventListener("click", AddressOf mnuOpen_Click)` — an `<li>` has an id and
`Element.addEventListener` (dom-core.bli). The `&` in a WinForms caption is an accelerator; on the
web it is text and is escaped as such — the retarget names nothing for it (it is the same document).

## 6. The surface — the "Type Here" strip, in place

The brief asks for the VS in-place editor, not a positioned box and not a side tree.

- **Bands.** A `Docked` strip draws as a band across the surface at its dock edge (a `Layout`
  entry, §2 — never `BoundsOf`), on both targets (on the web the band is the page chrome the
  emitter writes). A band draws NO caption (its id would otherwise be the only pixel difference a
  render gate sees). Its top-level items
  draw as CELLS left to right inside the band: a caption cell `8 + 7·len(Text) + 8` px wide
  (schematic, deterministic, no font measurement), a separator as a thin vertical rule, a toolbar
  button as a small box with its caption, a status label as text. After the last cell, a **Type
  Here** slot (greyed, italic) — shown only while the strip, one of its items, or one of their
  descendants is selected, as VS does.
- **Dropdowns.** The expanded item — the selection's item or its nearest item ancestor — shows its
  children as a vertical dropdown below its cell, with its own Type Here slot at the bottom.
  `Layout(document, expanded)` yields the dropdown entries LAST, so they paint over later controls
  (as handles do), and both targets' `HitTest` read the same entries: a click on a cell selects the
  item through `SelectInDesigner`; a click on a `TypeHere` entry raises the canvas's
  `BeginTypeHereCommand` (tested before the marquee branch — today a click on nothing starts a
  rubber band); a click elsewhere collapses. Items are never marquee-selected.
- **Editing.** `FormCanvasControl : Control` hosts no children, so the editor is a TextBox overlay
  in the design-view Grid cell above the canvas. Ownership, stated: the CANVAS publishes
  `TypeHereBounds` (canvas space, from its own `_transform`, republished on render, resize and
  model revision); the VIEW binds the overlay's margin/size to it and its text to
  `StripEditor.Text`; the VIEW MODEL's `StripEditor` holds only `IsActive`, the host and the text.
  `BeginTypeHere(host)` activates the editor, and the CONTROL focuses its box on EVERY Begin — on
  each change of `Host` while active as well as on `IsActive`'s rising edge — because the canvas
  takes focus on every pointer press before it hit-tests, so an editor already open on strip A
  when strip B's slot is clicked would otherwise keep `IsActive` true, never re-focus, and send the
  keystrokes to the canvas (where Delete removes the selected item); a view model cannot focus
  anything. **Lifecycle:** the editor is cancelled when the selection leaves its host — the view
  model's `Selection.Changed` handler calls `CancelTypeHere` when the editor is active and the new
  primary is neither the host nor inside it — so there is never an open editor whose slot `Layout`
  no longer yields, and `TypeHereBounds` never has to describe a slot that is not painted. `Enter`
  commits: `CommitTypeHere(text)` places
  `host.Items.Kinds[0]` with `Text = text` — or a `ToolStripSeparator` when the text is exactly `-`
  (VS's own convention; on a host whose rule has no separator, `-` is refused-and-reported through
  the BL8019 path rather than writing a document BL8030 refuses on reload) — through
  `FormPlacement.PlaceItem`, writes the document (undoable), selects the new item, and re-opens Type
  Here on the same host so a menu can be typed in one run, as in VS. `Escape` cancels.
  **`PlaceItem`'s id rule**, VS's: the caption camel-cased and sanitised plus the kind
  (`openToolStripMenuItem`, `saveAsToolStripMenuItem`, `exitToolStripMenuItem`;
  `toolStripSeparator1` for `-`) — `&` and every non-identifier character dropped, a leading digit
  or an empty result falling back to `kind + N` — then `FormDocument.MakeUniqueId` and
  `IsLegalControlId`, so the acceptance test can click a PREDICTABLE element and the grid's frozen
  Name is readable. **The editor is its own control**, `FormTypeHereEditor` (a TextBox-hosting
  overlay panel with styled `IsActive`/`Host`/`Text`/`SlotBounds` and
  `CommitCommand`/`CancelCommand`), so the view only BINDS it — the tray's lesson, applied. It
  ARRANGES its own TextBox at `SlotBounds` (a `Rect`, bound to the canvas's `TypeHereBounds`), so
  there is no Rect→Thickness converter to get wrong; a headless test of the control sets
  `SlotBounds` and asserts the TextBox's bounds follow, then types into it with `KeyTextInput`,
  presses Enter with `KeyPress`, asserts `CommitCommand` fired with the text and, after a `Host`
  change while active, that the box is focused again; Escape fires `CancelCommand`. **What each
  other test proves** (no test on this branch instantiates the real
  `CodeEditorDocumentView`): the CANVAS test presses the slot's pixels and asserts
  `BeginTypeHereCommand` fired with the host, presses a band cell AND a dropdown cell (and
  double-clicks a dropdown cell) and asserts the selection each time; the VIEW-MODEL test drives
  `BeginTypeHere` → `CommitTypeHere("Open")` → `CommitTypeHere("-")` → `CancelTypeHere` and asserts
  the document, the order, the separator rule, the ids and the re-open — lays the result out with
  the new item selected, asserting the parent's dropdown cells, the parent's slot and the new
  item's slot are all entries and the nested dropdown sits to the right — and selects a Button
  while the editor is open, asserting it cancelled; the AXAML gate asserts the editor element's
  `IsActive`/`Host`/`Text`/`SlotBounds`/`CommitCommand`/`CancelCommand` bindings and the canvas's
  `TypeHereHost`/`BeginTypeHereCommand` bindings by name (a binding absent from the AXAML is this
  feature's failure mode, and a text gate is what the tray uses).
- **Gestures that already exist and keep working:** Delete (`ListContaining`), undo (text),
  double-click → `ActivateControlCommand` (the default `Click` handler, stub below/above the region
  per target), `BringToFront`/`SendToBack` on a selected item move it to the end/start of its list —
  which for an item is "move to last/first in the menu", a bonus VS does with Move Up/Down. Drag
  reorder of items is out (§8).
- **Toolbox.** Strips under **Menus & Toolbars**; dropping one anywhere on the canvas docks it (the
  point is ignored — the tray precedent). Item kinds are not offered; a drop of one is refused
  BL8019 ("'ToolStripMenuItem' is created from its menu's Type Here slot").
- **Property grid.** Strips: `Dock` and the rows above; items: their rows; neither gets
  TabIndex/geometry. Name is frozen as always.

## 7. Verification — run, not read

- **Reader/writer:** the D9 algebra over the §3 document; BL8030 three ways; structural attributes
  on a strip/item round-trip as unknown attributes; a strip written by `Create` carries no zeros;
  the clipboard mirror: paste a strip → geometry null, `Dock` kept, no TabIndex; paste an item root
  with a host selected → it lands in the host; with none → refused-and-reported.
- **Catalog gates, driven from `FormCatalogShapes`:** the csc sweep (every property, every enum
  value, the default-event stub) builds each new row in its canonical shape and csc accepts it — an
  item alone at top level would fail for `Controls.Add`, which is the sweep proving the shape, not a
  row; coverage (`ExpectedWinFormsKinds` + 7, both rows per kind, event per target, glyph per
  schematic, and a per-value pin that every new `FormSchematic` reaches a NON-default `DrawControl`
  arm); the render gate hashes every non-item kind distinctly — items are EXCLUDED from the hash
  the moment the rows land (24c), or two item kinds canonicalise to the same band and collide —
  (fixture rule extended: every control gets the same id AND, for items, the same `Text`, since a
  cell's width is a function of its caption) and pins, catalog-driven per Item kind, "its first
  host WITH one of it renders differently from the host without" — the one gate the four item
  arms have, so it is a MUTATION KILL by construction: blank the item's `DrawControl` arm and the
  two frames must become equal (24d, when cells exist); the retarget sweep builds the canonical
  shape, locates the crossed control with `Locate`, scopes its `reportedLost` set to THAT control's
  id (today it derives the set from every BL8024 message with `Single`, which a finding about the
  canonical host would break), and asserts a strip crosses docked and an item crosses nested, with
  no geometry either way (24e, with the `Place` exclusion).
- **Emission:** the region writer's per-item `Add` in document order (a mutant that reverses it
  must fail), the host verb per row, `MainMenuStrip` once; the web page's nesting
  (`<nav><ul><li id="mnuFile">…<ul><li id="mnuOpen">`), chrome placement before/after the form
  div, no `tabindex` on items, `role` attributes.
- **Headless and view model** (the split §6 states): press on a cell's pixels → selection through
  the one store; press on the Type Here slot's pixels → `BeginTypeHereCommand` with the host (the
  `FormCanvasMultiSelectTests` rig: `MouseDown`/`MouseUp`); the view model's Begin → Commit →
  Commit `-` → Cancel sequence → the items exist in the document under the host, in order, the
  second a separator, and the slot re-opens; Delete of an item; the Dock band renders at the top
  for a document with a MenuStrip and a Button (frame differs from the same document without the
  strip, and the Button's own pixels are unchanged); the AXAML + code-behind gate for the overlay,
  its bindings, the canvas's two bindings and the handler name.
- **Acceptance, RUN on both targets** (`FormMenuAcceptanceTests`, the Timer test's shape; the form
  is named `MenuForm` on both targets, and §3's `MainForm` and hand-written ids are illustrative —
  the designer mints `fileToolStripMenuItem`, `openToolStripMenuItem`, `toolStripSeparator1`,
  `exitToolStripMenuItem`, and the assertions use those): the designer's own commands build File →
  Open, `-`, Exit plus a toolbar button and a status label;
  double-click on Open writes the stub; `Console.WriteLine("CLICK")` in it; save; the real CLI.
  WinForms: csc + a driver that prints `form.MainMenuStrip.Items` captions in order, the dropdown's
  items in order WITH THEIR TYPES (so the `-` → separator rule is asserted, not only captions),
  `PerformClick`s Open, and reads the status text — asserting order, `CLICK`, and `Ready`. Web:
  `basiclang build`, the page's nesting asserted as text, then the node harness clicks ONE named
  element — `RunPageUnderNode` gains a form-name parameter (it hard-codes `data-form="LoginForm"`,
  `FormDesignerAcceptanceTests.cs:266-270`, so a `MenuForm` page would construct nothing and the
  failure would look like a wiring defect) and an optional element id (today it clicks every
  registered element and cannot say which handler fired) — `CLICK` for `openToolStripMenuItem`
  only.
- **Mutation kills** for every new test, as on Tasks 21–25.

## 8. Deliberately out of scope, and where it goes

- `ShortcutKeys` — a Keys-combination editor plus a name→value table (M6/M7): followup 22.
- Images on items, `ContextMenuStrip`, `ToolStripDropDownButton`/`SplitButton`, `ToolStripComboBox`,
  `ToolStripTextBox`, `ToolStripProgressBar`, `ToolStripLabel` in a ToolStrip: further rows, each
  csc-gated by the same sweep the day it is added.
- Drag-reorder of items on the canvas; multi-select of items; a strip docked Left/Right.
- Script-driven submenus on the web (`classList` toggling) — CSS `:hover` is what a page without a
  runtime honestly has.
- Array-literal common-base widening for BasicLang classes: followup 23, with the M1/M3 measurement
  that it cannot serve WinForms types.
- The C++ capability checker's missing `IRArrayAlloc` arm (§9): followup 24.

## 9. The compiler change, first and gated on its own: `New T() { … }`

**Syntax — the measured mechanism.** Today `New ToolStripItem()` is a parameterless CONSTRUCTOR
call: the `New` branch (`Parser.cs:4280-4298`) parses a type reference and an optional `(args)`;
`ParseTypeReference` has no array-suffix branch (it consumes `(` only for `(Of …)`), and M4's
message is the enclosing call's argument list meeting `{`. So the new form is recognised by a `{`
FOLLOWING the `New` branch's argument list: on `Check(LeftBrace)` the branch parses the brace list
into the EXISTING `CollectionInitializerNode`, sets its (declared, never-set) `ElementType =
newExpr.Type`, and RETURNS that node — the `NewExpressionNode` is discarded, so
`Visit(NewExpressionNode)` and `IRNewObject` are never involved. The parentheses are REQUIRED, as
in VB. Refusals, each with the fix in the message: `New T {…}` with no parentheses ("write `New T()
{…}`"); `New T(n) {…}` when `newExpr.Arguments.Count > 0` ("the initializer sets the size; write
`New T() {…}`" — VB's `New T(2) {…}` states an UPPER BOUND while this compiler's array sizes are
ELEMENT COUNTS, so accepting it would be a silent off-by-one); and `Dim x As New T() {…}` on the
`Dim … As New` path (`:2504-2525`, the same `Check(LeftBrace)` placed OUTSIDE the `if
(Match(LeftParen))` block so that `Dim x As New T {…}` takes the same named refusal instead of a
generic statement error: "write `Dim x() As T = New T() {…}`"), because VB has no such form either.

**Typing.** `Visit(CollectionInitializerNode)`: when `ElementType` is present, resolve it with
`ResolveTypeReference`, type the node as `T[]` with element `T`, and check each element:
- both resolvable, element NOT a literal → the element must be `T`, or WIDEN to it:
  `IsAssignableFrom` with its WHOLE permissive arm excluded (`SymbolTable.cs:195`, the single `if`
  that admits integral←floating AND integral←integral narrowing) — so `New Integer() {aDouble}`,
  `New Integer() {aLong}` and `New Shape() {aString}` are errors ("cannot put a 'Double' in an
  'Integer()'", "… a 'Long' …", "cannot put a 'String' in a 'Shape()'"); an element whose node type
  is null (an unresolved expression) is skipped as the untyped visitor already skips it (`:6647`);
- `Nothing` has its OWN arm — it is NOT typed null but `Object` (`Visit(LiteralExpressionNode)`'s
  default, `:8405-8410`), so the non-literal rule would refuse `New String() {"a", Nothing}`: an
  `IsNothingLiteral` element (`:4210`) is admitted without a check into any reference or synthetic
  .NET `T`; into a value-type `T` it is refused ("Nothing has no value of type 'Integer'; write 0")
  — the honest reading, rather than VB's silent default. `New String() {"a", Nothing}` and `New
  Shape() {c, Nothing}` are admitted gate rows;
- both resolvable, element a NUMERIC LITERAL → this rule, OWNED here and deliberately stricter than
  the declaration path's `IsNumericLiteralAssignable` (`:1582-1602`), which admits `Dim i As
  Integer = 1.5` today and must not be "harmonised" back: an integral literal is admitted into any
  numeric target (`New Byte() {65}`, `New Long() {1}`, `New Double() {1}`; Decimal through
  `TryRetypeLiteralToDecimal`); a floating literal is admitted into Single/Double, into Decimal
  through the same retype, and NEVER into an integral target (`New Integer() {1.5}` is an error);
  and `CheckConstantFitsNumericTarget` (`:1633`, the BC30439 check every other store site already
  calls — Dim `:5729`, Const, Return, assignment, argument) runs per element, so `New Byte() {300}`
  is refused exactly as `Dim b As Byte = 300` is today. For Byte, Short, SByte and the unsigned
  types the analyzer admits the literal and `CoerceToDeclaredType` leaves the Integer constant in
  place (`IsFoldableNumeric` is Integer/Long/Single/Double only, `IRBuilder.cs:3759-3763`) — the
  emission `Dim b As Byte = 65` has today; Long IS foldable, so `New Long() {1}` stores a Long
  constant, like the Double example below;
- either side an unresolvable .NET type → accepted, csc decides. The predicate, SPELLED (a
  `TypeInfo` carries no synthetic marker, and synthetic handles are minted at four sites — `:2278`
  generic, `:2587`, `:4522`, `:8484` — so a flag is not the answer): re-derived from the NAME in
  `ResolveTypeName`'s own order, `!IsUserDefinedTypeName(name) && _typeManager.GetType(name) ==
  null && IsNetType(name)` — `IsUserDefinedTypeName` (`:4221-4231`) is exactly the scope check that
  makes `RejectImpossibleConversion`'s spelling (`:9437-9438`) wrong for a sibling-file BasicLang
  class on the CLI (a two-file CLI row pins `New Shape() {aString}` with `Shape` in another `.bas`
  as an error); a re-resolve cannot double-report BL6016 because `NetWarning` de-dups on
  code+message (`:2775`). Applied to BOTH `T` and the element, so a synthetic GENERIC element (`New
  Shape() {aList}`, a `List(Of T)` handle) is accepted too — pinned, so the spelling cannot drift.
  (`IsAssignableFrom` is false for two DIFFERENT-named synthetic handles, so this is what makes the
  M4 program pass; two of the SAME name are `Equals`, which is why M2 passes today.) ⚠ This is a
  THIRD conversion policy, on purpose: it is not the Dim path's check (`:5713`, which exempts
  nothing — the tray spec's M5 `Container`→`IContainer` refusal) and not `RejectImpossibleConversion`
  (which exempts only the target, only in its scalar→reference arm); the element check is never
  routed through either;
- no "mixed types" warning for a typed literal; the UNTYPED path is untouched and gains its first
  pin (bare `{1, 2, 3}` is still `Integer[]`; bare mixed is still `Object[]` + the warning — no
  test asserts either today).

**Lowering.** `IRBuilder.Visit(CollectionInitializerNode)` reads the analyzer's node type as
today, and — new — coerces each element through `CoerceToDeclaredType` (`IRBuilder.cs:3501`) when
`ElementType` is present: a LITERAL is re-typed in place (`New Double() {1}` stores a Double
constant — the helper never wraps a literal, its own comment records the regression that did), a
NON-literal is wrapped in an `IRCast` (`New Double() {i}` for an Integer `i`); `New Integer()
{1.5}` never reaches here. The C# backend emits
`new ToolStripItem[2]` + stores (M2 shows the shape); the C++ backend `std::vector<T>(n)`.
**JavaScript is NOT unchanged:** the backend throws `NotYet` for `IRArrayAlloc` and `IRArrayStore`
in both its statement and expression arms today (`JavaScriptBackend.cs:2379-2380`, `:747-748`), so
no array literal runs there. 24a adds both arms (`[]` of `n` slots, index stores), because a literal
the capability checker approves and the backend then dies on is the green-build-dead-page class.
⛔ The expression arm returns the BOUND name for an `IRArrayAlloc` that already appeared in
`block.Instructions` (the `Bound(n) ? SanitizeName(n.Name) : …` pattern at `:709-729`, whose comment
records a constructor printed twice): the M4 shape passes the array temp as a call ARGUMENT after its
stores, and re-rendering it inline would allocate a second, empty array. `ASTPrettyPrinter` prints
the type. `Visit(NewExpressionNode)`'s abstract/Extern refusals are bypassed for the typed literal
(an array OF an abstract type is legal) and `New T()` with no brace still yields a
`NewExpressionNode` — both pinned.

**Gate.** Parser tests (the expression path; the three refusals with their messages; `New T()`
alone still a constructor call); analyzer tests (typed `ToolStripItem[]` with no warning; `New
Shape() {c, s}` over BasicLang classes, the two-file `Shape` error through the CLI and the
synthetic-generic element accepted; the `Nothing` rows; the literal rule's admitted and refused
rows incl. `New Byte() {300}`; the non-literal errors; the untyped-path pins); emission through
the real CLI, optimizer on: the M4 program
builds under csc and RUNS with its menu populated (`WinFormsCompile` + the M5 driver); the M3 shape
`Dim items() As ToolStripItem = New ToolStripItem() {mnuFile, sep}` through CLI + csc (it exercises
the array `Equals` path the argument shape does not); `New Integer() {1, 2, 3}` summed and `New
Double() {1, i}` summed on C# (run), C++ (MSVC, `CompileAndRun`) and JavaScript (node) — and the
bare `{1, 2, 3}` on JavaScript, which the new arms make run for the first time. ⛔ The JavaScript
rows are the first tests of an arm that has never run, so they ASSERT node is on the PATH and fail
loudly without it — never `Assert.Ignore` (a pass-by-absence, per the project's own rule); this box
has node. The full suite, because the analyzer is shared machinery. Recorded, not widened: the C++
capability checker has no `IRArrayAlloc` arm, so a typed literal of an unresolvable .NET element
type passed straight into a foreign `::` call on a native build would slip past it (a declared
local or field still catches it) — followup 24. Its own commit: `feat(compiler): New T() { … }
array creation with initializer`.

## Decisions taken without a human in the loop

1. **The alternative compiler form, not the preferred one** — measured: widening cannot type
   `{mnuFile, sep1}` because both are unresolvable .NET types with no base chain (M1, M3); the
   explicit form does (M2's conversion). Widening for BasicLang classes is a followup, not dropped.
2. **The designer emits per-item `Add`**, not `AddRange` — measured to compile and run (M5, M9),
   the shape the writer already uses for `Items`, and it removes the designer's dependency on the
   compiler change entirely.
3. **`Place` is an enum**, `IsComponent` a derived property — a third bool would have to be
   consulted or deliberately ignored at eleven sites; an enum forces the decision at each.
4. **Strips are geometry-less with a `Dock` PROPERTY**, never `PixelGeometry{0,0,0,0,Dock}` — the
   zero rect is what produced the recon's phantom grips and the retarget's invented column.
5. **Strips are page chrome on the web** (before/after the form div), not grid cells — a menu bar
   at the top of the page is what a `<nav>` honestly is; the canvas draws the same band.
6. **ToolStripButton is `<input type="button">`** — `<button>` would make the tag ambiguous and
   silently break the DOM recognizer for a plain Button.
7. **Items are not toolbox rows** — created from Type Here, as in VS; `-` makes a separator.
8. **`ShortcutKeys` deferred** — only expressible as `CType(n, Keys)`; needs a type editor.
9. **`MainMenuStrip` emitted** — VS does, and keyboard menu navigation needs it.
10. **A strip inside a container is refused** (BL8030) — VS allows it; the band model, the web
    chrome and the emitter would each have answered differently, so v1 answers once.
11. **Strips declare `ItemClicked`** rather than exempting a shape from two event gates.
12. **Five commits, each green on the fast subset and its Integration fixtures** (the review
    showed the earlier four left three gates red between the rows landing and the gates learning
    them): **24a** the compiler change, incl. the JavaScript array arms; **24b** the shape —
    `Place`, `FormItemRule`, `FormCatalogShapes` with the three catalog gates moved onto it, the
    seven `FormSchematic` values with both arms, the structural-name pin's exemption — with NO new
    row yet — the three shape gates, the two toolbox-membership pins, the `DrawSchematic` seam
    with its enum-driven pins, and `FormToolboxViewModel.Rebuild`'s item exclusion all learn
    `Place` here — so every gate stays green by construction; **24c** the seven rows + reader/writer/
    clipboard/region writer/emitter/recognizer + the band layout and drawing (the render gate sees
    bands, not boxes; items excluded from its hash) + EVERYTHING the toolbox and a drop touch the
    moment a row exists — `FormPlacement.Place`'s Docked branch, `PlaceItem` and the BL8019 refusal
    of an Item drop, the toolbox's Menus & Toolbars category and its item exclusion, the grid's
    TabIndex/geometry suppression — because the toolbox is catalog-driven and would otherwise offer
    a kind the surface mishandles from a green build; **24d** the editing surface — cells,
    dropdowns, Type Here, the overlay, paste, the per-item render pin; **24e** retarget (with the
    sweep's no-geometry pin) + acceptance on both targets + records (HANDOFF, CLAUDE.md, followups
    22/23/24, memory) + the IDE drop. 24c is large BY DESIGN — the toolbox is catalog-driven, so a
    row cannot land without everything a drop touches — and its plan orders the work so each gate
    goes green in sequence (rows + reader/writer + clipboard; region writer + emitter + recognizer;
    layout + drawing; placement + toolbox + grid); it is not split into a state where a gate is red.
    Plan-level notes carried from review: `TypeHereHost` IS in `AffectsRender`, so a Begin with no
    selection change still republishes the slot; `FormCatalogShapes.Canonical`'s Positioned shape
    keeps the property sweep's `Anchor="Top"` so per-kind Anchor coverage does not silently drop;
    `RunPageUnderNode`'s hard-coded name is at `FormDesignerAcceptanceTests.cs:303` and its other
    two callers need the new parameter's default.
13. **`&` in a caption is stripped on the web** — it is a WinForms accelerator, and `File` is the
    honest web caption of `&File`; the document is one, the two targets read it differently.
14. **Recorded for a chip, not fixed here:** `RejectImpossibleConversion`'s own unresolved-.NET
    spelling (`SemanticAnalyzer.cs:9437-9438`) has the same sibling-file hole §9 avoids — `CType(7,
    Shape)` with `Shape` in another `.bas` on the CLI is exempted. Shared machinery, different task.
