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
| M4 | `AddRange(New ToolStripItem() {mnuFile, sep})` | ⛔ parse error "Expected ')' after arguments but found LeftBrace" — `ToolStripItem()` parses as an ARRAY TYPE reference, then the argument list sees `{` | — | — |
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
  `{mnuFile, sep1}` can never type as `ToolStripItem()` by inference. Widening is generally useful
  for BasicLang classes (whose `Inherits` the analyzer knows) and is filed as a followup; it does
  not unblock the idiom. **The alternative does**: `New ToolStripItem() {…}` names the element type
  explicitly, needs no resolution, and csc accepts the emitted `new ToolStripItem[] {…}` by implicit
  reference conversion (M2 is the same conversion one step down). §9.
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
not derived from the verb's spelling — "ask the row, never the shape of the string".

**The nesting invariant, enforced in ONE place (the reader), refused as BL8032:** an `Item` may
appear only under a host whose rule lists its kind; a non-`Item` may not appear under a host. Both
directions generate code csc rejects (M8: `menuStrip.Controls.Add(item)` CS1503, `Items.Add(button)`
CS1503), exactly BL8020's argument. WinForms itself accepts any ToolStripItem in any strip (M8), so
"a ToolStripButton in a MenuStrip" is refused by the DESIGNER's rule, not csc's — stated so nobody
"fixes" the refusal to match csc.

## 2. Who visits strips and items — decided per walker, from the recon

Three readers and a critic mapped every walker (workflow `wf_3d3e64ff-7d0`, 2026-09-19). Each row
below is a decision; "as-is" means the recon read the code and nothing changes.

| Walker | Decision |
|---|---|
| `FormDocumentReader.ReadControl` | nests under any catalog kind today (`:473`, no `IsContainer` check). Gains the `Place` branch: `Docked`/`Item` never acquire geometry or TabIndex (structural attributes on them → unknown attributes, the component rule); BL8032 for the nesting invariant; `Dock` on a strip is a catalog PROPERTY, not `PixelGeometry.Dock` — so `<MenuStrip Dock="Top"/>` reads `Geometry == null`, never the `{0,0,0,0,Dock}` zero rect the recon found |
| `CheckDuplicateIds`, `FindById`, `ListContaining`, `AllControls` | as-is — items are in the visual tree; Delete/Cut/rename cover them |
| `RenumberTabIndexes`, `FormPlacement.NextTabIndex` | skip `Place != Positioned` — items and strips are inside `AllControls()`, so exclusion is per ROW, unlike the tray's per-list exclusion |
| `FormDocumentWriter.Create` / `ControlElement` | no TabIndex and no geometry for `Place != Positioned` (today `:557` writes TabIndex unconditionally on every non-component — the recon's invented-content wound); children recurse as today |
| `FormDocumentWriter.Apply` / `ReorderToMatchModel` | as-is for nesting and ORDER (order is what a menu is; the model-order write is exactly right); the TabIndex set-if-changed gains the same `Place` guard |
| `RegionWriter.GenerateControls` | as-is — `Private mnuFile As ToolStripMenuItem` is VS's own shape |
| `RegionWriter.AppendSiblings` | receives the PARENT control (today only its id string); asks the parent row for the add verb and the order: `Items` rule → its `Add`, document order; else `Controls.Add` reversed. ⛔ Recon rank 1: forget the verb and csc says CS1503; forget the order and `File/Edit/Help` runs as `Help/Edit/File` from a green build |
| `RegionWriter.AppendControlInit` / `AppendProperties` / `AppendBinds` | as-is; a strip's `Dock` is a property row with `WinFormsEnumType: "DockStyle"` and emits `menuStrip1.Dock = DockStyle.Top` |
| `RegionWriter.GenerateInit` | after the strips' siblings: `Me.MainMenuStrip = {id}` for the FIRST MenuStrip in document order (row flag `FormProperty = "MainMenuStrip"`; VS emits it; measured harmless without it, M9, kept because Alt-key menu navigation needs it) |
| `RegionWriter` checks | `CheckHandlerOrdering`, `CheckTargetProperties` cover items as controls; BL8029 gains the control-side twin for an `Item`/`Docked` kind with no row on the target (none exists — every strip and item row has both targets, pinned by coverage) |
| `FormHandlers.PlanDefault` | as-is; item rows declare `WinFormsEvent: "Click"` / `WebEvent: "click"` (a separator too — `Click` is inherited, honest) |
| `FormClipboard` / `PasteControls` | copy of a strip carries its items (as-is). A pasted ITEM root goes into the selection's primary when that is a host accepting the kind, else the paste is refused-and-reported (BL8019 path); never into `Controls` |
| `FormPlacement.Place` | `Docked`: into `Controls`, no geometry, `Dock` from the row's default, the point ignored (like the tray); `Item`: a NEW entry point `PlaceItem(document, host, kind, text)` — a parent by identity, never a point |
| `FormRetarget.ConvertControls` | as-is for nesting; `Dock` is a shared property (both targets) so it crosses; `Place` (web→pixels) and `DeriveCells` (pixels→cells) skip `Docked`/`Item` — neither has geometry on either side. `Hoist` never sees a strip: every host and item row has both rows (coverage pin) |
| `FormAssetEmitter.Html` | `Docked` strips are PAGE CHROME, not cells: Top-docked strips are written BEFORE the `.vgs-form` div and Bottom-docked ones AFTER, each in document order; items nest inside. No `tabindex` for `Place != Positioned` |
| `FormAssetEmitter.AppendControl` | row-declared `HtmlChildrenWrapper` ("ul" on MenuStrip and ToolStripMenuItem) wraps the children; row-declared `HtmlRole` ("toolbar", "status", "separator") is a fixed attribute; a row's `WebCss` is appended once per kind present (the horizontal bar, hidden submenus shown on hover) |
| `FormControlCatalog.FindByHtmlTag` | ToolStripButton is `<input type="button">` (Text → `value`), NOT `<button>`: a second row owning `button` makes the tag ambiguous and the DOM recognizer would stop naming a plain Button (`FormRecognizerTests` web-template pin). `li` is shared by ToolStripMenuItem and ToolStripSeparator — both new, so no regression; the recognizer names a bare `createElement("li")` BL8004, recorded |
| `WinFormsDialect.MarkParented` (recognizer) | learns `<host>.Items.Add(<id>)` and `<host>.DropDownItems.Add(<id>)` as parenting shapes, or `design --check` reports every item BL8006 "never added" |
| `FormCanvasTransform.BoundsOf` / `Layout` / `HitTest` / `ContainerAt` | `Docked`: a BAND — full surface width, the row's `DefaultHeight`, Top strips stacked from y=0 in document order, Bottom strips stacked up from the bottom; never a 0x0 rect (recon rank 2: phantom grips at the origin). `Item`: the strip editor's cells (§6). `ContainerAt` returns null for a strip (a Button drop lands on the form, never in a strip) |
| `FormCanvasControl.DrawControl` | three new schematics (`MenuBar`, `ToolBar`, `StatusBar`) draw the band and its top-level items' captions; item schematics (`MenuItem`, `Separator`, `ToolButton`, `StatusLabel`) draw a cell, never a box |
| `FormGeometryEdit` / `FormArrange` | a `Docked` strip is never moved, resized or re-parented (its geometry is null, so every edit already early-returns — pinned) |
| `FormPropertyGridViewModel.AddIntrinsicRows` | no TabIndex row for `Place != Positioned`; no geometry rows (null geometry already); the strip's `Dock` is a catalog row like any other |
| `FormToolboxViewModel` | a fourth category, **Menus & Toolbars**, holding the three strips. Item kinds are NOT toolbox rows (VS does not list them either); they are created by Type Here |
| `FormTrayViewModel` | unchanged; the tray is the MODEL for "rebuild from the document, select through the one store", not the surface (a menu is a tree) |
| `CodeEditorDocumentViewModel` | `PlaceControl` unchanged for strips (the point is ignored); new `StripEditor` (§6) with `BeginTypeHere` / `CommitTypeHere` / `CancelTypeHere`; `SelectInDesigner` for every selection (the 25e rule) |
| Gates (`WinFormsCatalogSweepTests` ×3, `FormCanvasRenderTests`, `FormRetargetTests` sweep) | each chooses its shape by `IsComponent` alone today. They move onto ONE helper, `FormCatalogShapes.Canonical(kind, target)`: Positioned → geometry in `Controls`; Tray → `Components`; Docked → `Controls` with the row's Dock; Item → the FIRST host row listing it, in `Controls`, with the item as its child. A fifth shape cannot be forgotten by three gates again |
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
- BL8032 `ItemMisplaced`, a REFUSAL both ways: an item outside a host that lists its kind
  ("'mnuOpen' is a ToolStripMenuItem, which lives inside a MenuStrip or a menu item — under
  <Controls> it would be added with Me.Controls.Add, which does not compile"), and a non-item under
  a host ("'btnGo' is a Button, but it sits under 'menuStrip1'. A MenuStrip holds only
  ToolStripMenuItem, ToolStripSeparator").
- The D9 algebra (byte-identical round trip, no-op writes nothing, `Read∘Apply == Apply∘Read`) is
  restated over a document with a nested strip, because no fixture has one.

## 4. The rows (every name below is csc-gated, M8)

| Kind | WinForms type | Place | Properties | Default event | Web |
|---|---|---|---|---|---|
| MenuStrip | `MenuStrip` | Docked, `DefaultHeight 24` | `Dock` Enum Top/Bottom = Top (`DockStyle`), `Enabled`, `Visible` | none (a strip is not double-clicked) | `<nav>`, children in `<ul>` |
| ToolStrip | `ToolStrip` | Docked, 25 | `Dock` = Top, `GripStyle` Enum Hidden/Visible = Hidden (`ToolStripGripStyle`), `Enabled`, `Visible` | none | `<menu role="toolbar">` |
| StatusStrip | `StatusStrip` | Docked, 22 | `Dock` = Bottom, `SizingGrip` Bool = false, `Enabled`, `Visible` | none | `<footer role="status">` |
| ToolStripMenuItem | `ToolStripMenuItem` | Item, host of menu items | `Text`, `Enabled`, `Visible`, `Checked`, `CheckOnClick`, `ToolTipText` | `Click` / `click` | `<li>`, children in `<ul>` |
| ToolStripSeparator | `ToolStripSeparator` | Item | `Visible` | `Click` / `click` | `<li role="separator">` |
| ToolStripButton | `ToolStripButton` | Item | `Text`, `Enabled`, `Visible`, `Checked`, `CheckOnClick`, `ToolTipText`, `DisplayStyle` Enum None/Text/Image/ImageAndText = Text (`ToolStripItemDisplayStyle`) | `Click` / `click` | `<input type="button">` (Text → `value`) |
| ToolStripStatusLabel | `ToolStripStatusLabel` | Item | `Text`, `Enabled`, `Visible`, `Spring` Bool, `ToolTipText` | `Click` / `click` | `<span>` |

- No `Common()`: `Visible`/`Enabled` compile on every row above (M8) but `ForeColor`/`BackColor`
  are noise on a strip and `Text` on a strip is meaningless; rows list what they mean.
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
        Me.MainMenuStrip = menuStrip1                ' first MenuStrip in document order
        Me.Controls.Add(statusStrip1)                ' siblings REVERSED, as today: docking order
        Me.Controls.Add(btnGo)
        Me.Controls.Add(toolStrip1)
        Me.Controls.Add(menuStrip1)
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
<div class="vgs-form" data-form="MainForm">…positioned controls, in their cells…</div>
<footer id="statusStrip1" class="vgs-StatusStrip" role="status"><span id="lblStatus" class="vgs-ToolStripStatusLabel">Ready</span></footer>
```

The init region is unchanged in shape: `mnuOpen = doc.getElementById("mnuOpen")` then
`mnuOpen.addEventListener("click", AddressOf mnuOpen_Click)` — an `<li>` has an id and
`Element.addEventListener` (dom-core.bli). The `&` in a WinForms caption is an accelerator; on the
web it is text and is escaped as such — the retarget names nothing for it (it is the same document).

## 6. The surface — the "Type Here" strip, in place

The brief asks for the VS in-place editor, not a positioned box and not a side tree.

- **Bands.** A `Docked` strip draws as a band across the surface at its dock edge (`BoundsOf`, §2),
  on both targets (on the web the band is the page chrome the emitter writes). Its top-level items
  draw as CELLS left to right inside the band: a caption cell `8 + 7·len(Text) + 8` px wide
  (schematic, deterministic, no font measurement), a separator as a thin vertical rule, a toolbar
  button as a small box with its caption, a status label as text. After the last cell, a **Type
  Here** slot (greyed, italic) — shown only while the strip, one of its items, or one of their
  descendants is selected, as VS does.
- **Dropdowns.** A selected menu item (or any ancestor of the selection) shows its children as a
  vertical dropdown below its cell — drawn LAST, over later controls, exactly as handles are — with
  its own Type Here slot at the bottom. `Layout` yields these cells, so `HitTest`, `Render` and the
  marquee agree: a click on a cell selects the item through `SelectInDesigner`; a click on Type Here
  begins editing; a click elsewhere collapses.
- **Editing.** `FormCanvasControl : Control` hosts no children, so the editor is a TextBox overlay
  in the design-view Grid cell above the canvas, positioned from `StripEditor.Bounds` (canvas
  space). `BeginTypeHere(host)` sets `StripEditor.IsActive`, focuses the box; `Enter` commits:
  `CommitTypeHere(text)` places `host.Items.Kinds[0]` with `Text = text` — or a `ToolStripSeparator`
  when the text is exactly `-` (VS's own convention) — through `FormPlacement.PlaceItem`, writes the
  document (undoable), selects the new item, and re-opens Type Here on the same host so a menu can
  be typed in one run, as in VS. `Escape` cancels. The reachability gate reads the AXAML for the
  overlay's bindings; a headless test drives `BeginTypeHere` and `KeyTextInput` if
  `Avalonia.Headless` 11.3 accepts text into a focused TextBox (to be measured in the plan — if it
  does not, the commit path is pinned at the view model AND the AXAML gate reads the KeyBindings).
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

- **Reader/writer:** the D9 algebra over the §3 document; BL8032 both ways; structural attributes
  on a strip/item round-trip as unknown attributes; a strip written by `Create` carries no zeros.
- **Catalog gates, driven from `FormCatalogShapes.Canonical`:** the csc sweep (every property,
  every enum value, the default-event stub) builds each new row in its canonical shape and csc
  accepts it — an item alone at top level would fail for `Controls.Add`, which is the sweep proving
  the shape, not a row; coverage (`ExpectedWinFormsKinds` + 7, both rows per kind, event per
  target, glyph per schematic); the render gate hashes every non-item kind distinctly and pins
  `AnItem_IsNeverLaidOutAsABox` plus "a strip WITH items renders differently from one without";
  the retarget sweep builds the canonical shape and asserts a strip crosses docked and an item
  crosses nested, with no geometry either way.
- **Emission:** the region writer's per-item `Add` in document order (a mutant that reverses it
  must fail), the host verb per row, `MainMenuStrip` once; the web page's nesting
  (`<nav><ul><li id="mnuFile">…<ul><li id="mnuOpen">`), chrome placement before/after the form
  div, no `tabindex` on items, `role` attributes.
- **Headless:** click a cell → selection through the one store; Type Here commit → the item exists
  in the document under the host, in order; Delete of an item; the Dock band renders at the top for
  a document with a MenuStrip and a Button (frame differs from the same document without the strip,
  and the Button's own pixels are unchanged); the AXAML gate for the overlay and its commands.
- **Acceptance, RUN on both targets** (`FormMenuAcceptanceTests`, the Timer test's shape): the
  designer's own commands build File → Open, `-`, Exit plus a toolbar button and a status label;
  double-click on Open writes the stub; `Console.WriteLine("CLICK")` in it; save; the real CLI.
  WinForms: csc + a driver that prints `form.MainMenuStrip.Items` captions in order, the dropdown's
  items in order with their types, `PerformClick`s Open, and reads the status text — asserting
  order, `CLICK`, and `Ready`. Web: `basiclang build`, the page's nesting asserted as text, then the
  node harness clicks ONE named element (`RunPageUnderNode` gains an optional element id; today it
  clicks every registered element and cannot say which handler fired) — `CLICK` for `mnuOpen`.
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

## 9. The compiler change, first and gated on its own: `New T() { … }`

**Syntax.** `New <Type>() { e1, e2, … }` — the `()` is the array-type suffix `ParseTypeReference`
already consumes (M4 shows `New ToolStripItem()` parses as an array type today), so the parser's
`New` branch, on `Type.IsArray && Check(LeftBrace)`, parses the brace list into the EXISTING
`CollectionInitializerNode` with its (declared, unused) `ElementType` set to the element type. Both
`New` paths (`Parser.cs:4280` expression, `:2504` `Dim x As New T`) — the second refuses with a
clear message ("`Dim x As New T() {…}` is not a form; write `Dim x() As T = New T() {…}`") because
VB has no such form either. `New T(n) {…}` with a stated size is refused ("the initializer sets the
size; write `New T() {…}`").

**Typing.** `Visit(CollectionInitializerNode)`: when `ElementType` is present, resolve it with
`ResolveTypeReference`, type the node as `T[]` with element `T`, and check each element: a
resolvable element type must be assignable to a resolvable `T` (error "cannot put a 'Square' in a
'Circle()'"); an unresolvable .NET type on either side is accepted — csc decides, the
`RejectImpossibleConversion` exemption's argument. No "mixed types" warning for a typed literal.

**Lowering.** Unchanged: `IRBuilder` reads the analyzer's node type; `IRArrayAlloc(elementType)`;
the C# backend emits `new ToolStripItem[2]` + stores (M2 shows the shape); the C++ backend
`std::vector<T>(n)`; the JavaScript backend an untyped array. `ASTPrettyPrinter` prints the type.

**Gate.** Parser tests (both paths, the two refusals); analyzer tests (typed `ToolStripItem[]`, the
user-class assignability error, no warning); emission through the real CLI: the M4 program builds
under csc and RUNS with its menu populated (`WinFormsCompile` + the M5 driver); `New Integer() {1,
2, 3}` summed on C++ (MSVC, `CompileAndRun`) and on JavaScript (node) through the optimizing CLI
path; the full suite, because the analyzer is shared machinery. Its own commit: `feat(compiler):
New T() { … } array creation with initializer`.

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
10. **Four commits:** 24a compiler; 24b catalog + model + format + emission (both targets) +
    recognizer; 24c the surface (bands, cells, Type Here, toolbox, grid, placement, clipboard);
    24d retarget + the canonical-shape gate refactor + acceptance + records.
