# Slice 5 pre-flight: the Events tab, expanded and corrected against today's tree

Plan: `docs/superpowers/plans/2026-09-25-property-grid-vs-parity.md`, section "## Slice 5 — The Events tab (TASK
granularity)" (Tasks 5.1–5.6, its two ⚠ CARRIED notes), plus its Traps and "Tests to re-check" sections. Spec:
`docs/superpowers/specs/2026-09-25-property-grid-vs-parity-design.md` §2.3, §2.5, §5, §7, §8. Shape and rigour copied
from `2026-10-04-property-grid-slice4-preflight.md` (incl. its §8 lessons). Consumer: piece 2 (portable control library,
`origin/feat/portable-controls` @ `c722b644`, plan `2026-09-29-portable-control-library.md` Tasks 30/35/40, spec
`2026-09-29-portable-control-library-design.md` §5.6, §10.1, §11.1) — its 2b gate depends on this slice's event lists.

Made against **`2fa64789`** (slice 4 merged with master). Branch `feat/property-grid-slice5`, worktree
`scratchpad\wt-pg5`. Follow this document **alongside** the plan. Where the two disagree, **this document wins**.

Every file:line below was read on `2fa64789`. Line numbers drift: re-anchor by the quoted symbol, not the number.

---

## 0. Measured before writing (scratch probes, nothing in the repo)

| # | What | Result | How |
|---|---|---|---|
| M1 | Root listeners on the JavaScript backend: `doc.body.addEventListener("click", AddressOf F_Click)` and `w.addEventListener("resize", AddressOf F_Resize)` inside a class's `InitializeComponent` | ✅ Emitted `t0 = doc.body; t0.addEventListener("click", this.F_Click.bind(this))` and `w.addEventListener("resize", …)`; under node with `document.body`/`window` as `EventTarget`s, a dispatched `click` and `resize` each ran their handler once. No `dom-core.bli` change needed: `Document.body` (`:22`), `Element.addEventListener` (`:57`), `Window.addEventListener` (`:110`) already exist in BOTH copies (`BasicLang/lib/js/` and `IDE/lib/js/`) | `scratchpad\m5\LoginForm.bas`, prebuilt `IDE\BasicLang.exe --target=javascript` (a stale drop), node 2-line stub |
| M2 | Web `Load` as a call: `Me.LoginForm_Load()` as the last statement of `InitializeComponent`, parameterless Sub | ✅ `this.LoginForm_Load();`, ran at construction BEFORE any dispatched event. A `Me.Later()` call to a Sub declared BELOW `InitializeComponent` also emitted `this.Later()` and ran — so a direct call is NOT bitten by the BL8013 erasure (that is `AddressOf`-only) | same probe |
| M3 | WinForms root wiring through BasicLang → C#: `AddHandler Me.Load, AddressOf LoginForm_Load`, `AddHandler Me.FormClosing, …` | ✅ `this.Load += LoginForm_Load;`, `this.FormClosing += LoginForm_FormClosing;`. ⚠ csc acceptance NOT measured here — Task 4's sweep is the gate | `scratchpad\m5w\LoginForm.bas`, `--target=csharp` |
| M4 | A qualified args type outside the scaffold's imports: `e As System.ComponentModel.CancelEventArgs` (Validating) | ✅ emitted verbatim `System.ComponentModel.CancelEventArgs e`. (The single-file route printed the known BL6017 for `Me.Controls.Add` — the project route skips .NET resolution for `UseWindowsForms`; slice 4 M7) | same probe |
| M5 | The oracle has EVERY browsable event per kind (name, `argsType`, `argsFullName`, category, description) and the Form's (`types[0]`, 80 events, `defaultEvent: Load`) | ✅ — the per-kind table in D-1 was built from it. Facts that constrain the lists: **Button, CheckBox, RadioButton, ComboBox have NO `DoubleClick`**; **Label, PictureBox, ProgressBar, LinkLabel have NO `KeyDown/KeyUp/KeyPress`**; **TrackBar and DateTimePicker have NO `Click`**; **GroupBox has no mouse events** except `MouseHover` (its Click is already exempt). Scratch dumps: `scratchpad\events-by-kind.txt`, `events-own.txt` | `[IO.File]::ReadAllText` + `ConvertFrom-Json` (read-only) on `VisualGameStudio.Tests/Data/winforms-metadata.json` |
| M7 | **The filtered-listener shape (D-12), on the BRANCH CLI** (`wt-pg5\BasicLang\bin\Release\net8.0\BasicLang.exe`, built from `2fa64789`+`dabbdb90`): two generated wrapper Subs declared ABOVE `InitializeComponent` — `VgsOn_txt_KeyPress(e As DomEvent)` (`Dim k As String = e.key`; `If k.Length = 1 OrElse k = "Enter" OrElse k = "Backspace" OrElse k = "Escape" Then Me.txt_KeyPress(e)`) and `VgsOn_pnl_Enter(e As DomEvent)` (`If e.relatedTarget Is Nothing OrElse Not e.currentTarget.contains(e.relatedTarget) Then Me.pnl_Enter(e)`), wired `txt.addEventListener("keydown", AddressOf VgsOn_txt_KeyPress)` / `pnl.addEventListener("focusin", AddressOf VgsOn_pnl_Enter)` | ✅ Compiles; emits `k.length === 1`, `t0 === null \|\| t0 === undefined`, `t3.contains(t4)`, `this.txt_KeyPress(e)` (a NAMED method — no lambda, so neither the `Me.`-in-lambda hard error nor the unqualified-call `ReferenceError` can arise). Under node: keydown `a`/`Enter`/`Backspace`/`Escape` → the handler ran once each; `Shift`/`ArrowLeft`/`F1` → never. focusin with `relatedTarget` outside → ran; from a CHILD → did NOT run; `null` → ran. ⚠ Found: `DomEvent.relatedTarget` and `Element.contains` are NOT declared in `dom-core.bli`, and the compiler accepted them UNTYPED with no diagnostic — an Extern class's undeclared member passes silently (a typo would too). The node/Edge runs (Task 4) are therefore the gate for this text, not the compiler | `scratchpad\m6\KeyForm.bas` + `run.js` (node `EventTarget`s with a `contains` stub) |
| M6 | BasicLang identifiers are case-INSENSITIVE | ✅ `SymbolTable.cs:141`, `:631`, `:776` (`StringComparer.OrdinalIgnoreCase`). So the two handler scanners' `Ordinal` match (§1) is a latent defect: a hand-written `btnlogin_click` is not found and the gesture writes a SECOND `btnLogin_Click` — a duplicate member | read |

## 1. Re-anchored facts (every claim the slice-5 plan text makes)

| Plan claim | Where it is on `2fa64789` | Holds? |
|---|---|---|
| `FormEvents.WiredOn(definition, target)` is the one seam, signature fixed | `BasicLang/Forms/FormEvents.cs:69-85` (`NameOn` `:88-93`; web tray rule `:76-82`). `FormEventDef` record `:43-51` (Name, WinFormsArgs, WebEvent, Category, Description, IsDefault, OracleExemption, IsWebDefault) | ✅ The signature stays. ⚠ **Corrected:** the RECORD must grow (D-3: root events need a wiring mode — `Load` is a call, `Resize` listens on the window), so "the rows change, never the shape" holds for `WiredOn` only |
| Every row has one event | Not any more: Panel/FlowLayoutPanel/TableLayoutPanel (`PanelEvents`, `FormControlCatalog.cs:1861-1867`), GroupBox (`:2059-2064`), TrackBar (`:2210-2215`), DataGridView (`:2320-2325`) carry two (slice 3). Every other row uses `Ev(...)` (`:1874-1877`), whose doc comment (`:1869-1873`) still says "every row today" | ⚠ stale comment — fix in Task 1 |
| The Form has no events | `FormControlCatalog.FormRoot` `:2659-2784` passes no `Events:` | ✅ |
| `FormEventsTests.WiredOn_TheFormRoot_IsEmpty_UntilFormEventsExist` | `VisualGameStudio.Tests/Compiler/FormEventsTests.cs:150-158` | ✅ rewrite in Task 1. ⚠ NOT the only `FormEventsTests` row that breaks: `WiredOn_TheWeb_IsExactlyTheEventsWithAWebName` (`:125-136`, Button web = exactly `click`), `AGroupBox_…` (`:166-183`, `EquivalentTo {Enter, Click}`), `APanelKind_…` (`:189-203`), `AWebPanel_…` (`:209-220`), `ADataGridView_…` (`:259-270`) all pin today's exact lists |
| `FormRootTests.ARootBind_IsWarned_NotEmitted_UntilFormEventsExist` | `FormRootTests.cs:366-382` | ✅ rewrite in Task 2 |
| Root bind warning | `RegionWriter.CheckRootBinds` `:512-528` (BL8028, called `:122`) | ✅ replaced in Task 2 |
| `FormHandlers.PlanDefault` | `FormHandlers.cs:100-130`; `PlanBind` `:140-152`; private `EventOn` `:155-157`; `Plan` `:159-191`; `Insert` `:202-250` (signature `:217-222`, web ABOVE / WinForms BELOW the init region `:239-241`); `EnsureBind(FormControl, …)` `:82-91`; `NameFor` `:68-69` | ✅ All take a `FormControl` — no root owner exists |
| `FindDeclarationLine` | `FormHandlers.cs:285-311` (private). ⚠ **A MIRRORED copy** `RegionWriter.FindHandlerDeclarationLine` `:620-645` (used by BL8013). Both match `Ordinal` (M6 defect) and find `"Sub "` ANYWHERE in a line (only a line STARTING with `'` is skipped), so `x = 1 ' see Sub btn_Click` counts as a declaration | ⚠ unify + fix in Task 3 |
| RegionWriter bind emission | `AppendBinds` `:1096-1139` (web: `CanonicalWebEvent` spelling `:1125-1127`; WinForms: the document's own spelling `:1135`). `GenerateInit` `:700-771`: root rows `:732-735`, components `:739-742`, controls `:747-748`, `MainMenuStrip` `:756-760`, reference rows `:763-766`, `End Sub` `:769`. `Dim w As Window` only when a script component exists `:719-722` | ✅ |
| Web checks | BL8013 `CheckHandlerOrdering` `:578-617` (controls + components ONLY, `:591-592`); BL8028 `CheckComponentBinds` `:378-417`; BL8029 `CheckComponentTargets` (called `:119`); BL8032 `CheckControlBinds` `:463-504`; `DeclaredEvents` `:431-432`; `CanonicalWebEvent` `:442-444` | ✅ ⚠ spec §5's cite "`CheckControlBinds` (`RegionWriter.cs:330-409`)" is stale. ⚠ BL8032's message joins the declared list with `" and "` (`:496`) — unreadable at 12 events |
| `RegionWriter.IsEmittedBind` | `:537-552` — `true` for every control bind and every WinForms component bind; web components through the seam | ✅ |
| CARRIED 1: "`ConvertBinds` and `WiredRunState` still read the DEFAULT event" | ⚠ **Half false.** `FormRetarget.ConvertBinds` `:525-565` ALREADY crosses through `WiredOn` on both sides (slice-3 pre-flight B1 pulled it forward). Only `WiredRunState` `:578-592` reads `definition.DefaultEvent(_from)` directly | correct the note |
| CARRIED 2: two crossing rules side by side | ✅ `ConvertRootBinds` `:348-377` (called `:300`) beside `ConvertBinds`; both carry `⚠ SLICE 5` markers (`:350`, `:530`). The crossing branch of `ConvertRootBinds` (`:366-370`) is unreachable today | ✅ |
| (not in the plan) the retarget PAIR stubs root binds | ❌ **New blocker.** `ConvertToPair` plans a stub per bind for `AllControls().Concat(AllComponents())` ONLY (`FormRetarget.cs:135-150`). The moment a root `Load` crosses (Task 7), the pair wires `AddHandler Me.Load, AddressOf Login_Load` with no `Sub` behind it — the retargeted form stops compiling on both targets | fix in Task 7 |
| Existing retarget sweep | `FormRetargetTests.EveryEventWiredOnBothTargets_Crosses_UnderTheDestinationsName` `:581-613` — the CROSSING half only, controls only, catalog-driven | ✅ extend (the LOST half + FormRoot) in Task 7 |
| `FormRootRetargetTests.ARootBind_WithNoFormEventOnTheDestination_IsDroppedAndNamed` | `:185-200` (uses `Load`); also `ARootBind_ReadFromTheFile_IsDroppedAndNamed_…` `:202-217` (uses `Load`) | ⚠ BOTH break when Load crosses — rewrite on a WinForms-only event (`FormClosing`) |
| Parity oracle | `CatalogParity.CompareEvent` `VisualGameStudio.Tests/Compiler/CatalogParity.cs:81-120` (args compared on the LAST segment, `:97-104`; an exemption covers name and description, never args/category); `WinFormsCatalogParityTests` `:62-79` iterates `definition.Events` per kind, `FormRoot` included (`:95`, `:105`); the default event vs the snapshot's `defaultEvent` `:241` | ✅ ⚠ **Not Integration** — the fixture carries no category (`:17`), so parity runs in the FAST subset (slice-4 pre-flight said "Integration"; it is not). Every new event is judged automatically |
| `TheDefaultEvent_OfEveryControl_…` | `WinFormsCatalogSweepTests.cs:217-248` (Integration, real CLI + csc, via `PlanDefault`) | ✅ keep; Task 4 adds the every-event twin |
| The gesture | `CodeEditorDocumentViewModel.ActivateControlAsync` `:747-810` (`[RelayCommand]` `:747`; reads the open tab or disk `:772`; writes through `WriteCodeBehindAsync` `:844-856`; `EnsureBind` → `WriteDesignerEditBack` `:788-791`; `NavigateToFileEvent` `:794`; BL8035 notice `:798-802`) | ✅ generalise in Task 6 |
| Canvas double-click | `FormCanvasControl.OnCanvasDoubleTapped` `:702-743` returns on the background (`:715-718`); pinned by `FormCanvasDoubleClickTests.DoubleClickingEmptyFormBackgroundOpensNothing` `:110` | ⚠ D-9 changes it |
| The grid | `FormPropertyGridViewModel` (`Rows` `:57`, `DisplayItems` `:67`, one `FormPropertyDisplayList _display` `:39`). `IFormDisplayRow` (`ViewModels/Designer/IFormDisplayRow.cs:8-25`) already anticipates "an Events-tab row in slice 5" (`FormPropertyDisplayList.cs:12-13`). View: toolbar `FormPropertyGridView.axaml:86-107` (two UNNAMED radios grouped by their parent `StackPanel` — `:91-95`), list `:133-136`, templates `:137-327`; code-behind: `DoubleTapped` `:34`/`:342`, the popup `RequestBringIntoView` guard on ROW CONTAINERS `:38-54` | ✅ |
| Web `Load` "last in InitializeComponent", the constructor is the user's | `FormScaffolder.cs:221-230` writes `Public Sub New()` → `Me.InitializeComponent()` (web scaffold, the `Me.` fix) | ✅ M2 |
| Codes | `DesignDiagnostic.cs:54-89`: next free **BL8037**; BL8026 doc (`:230-236`) still says "only a kind's default event has a measured name on both sides"; BL8032 doc (`:314-318`) "one event per kind per target … followup 18" | ⚠ two stale doc comments |
| `FormDocument.Binds` doc | `FormDocument.cs:103-108` "WARNS rather than emitting until slice 5" | ⚠ stale after Task 2 |
| ADRs | `docs/superpowers/decisions/` holds 0001–0020; none about events | ✅ next is **0021** (⚠ piece 2 may claim it — re-check at merge) |

## 2. DECISIONS (owner delegated: "go with your recommendations"; each names what lost, so it can be overruled)

### D-1 — Per-kind event lists (plan 5.1)

**Rule.** A kind lists its default event, the kind-specific events a VS user actually wires, and from the common set
**M** = Click, DoubleClick, MouseDown, MouseUp, MouseMove, MouseEnter, MouseLeave · **K** = KeyDown, KeyUp, KeyPress ·
**F** = Enter, Leave — only those the SNAPSHOT lists for that kind (M5). Args, category and description come from the
snapshot through the parity run (the slice-1 procedure: write the name, args and web name; run
`WinFormsCatalogParityTests`; paste the category/description it prints). Args outside `System`/`System.Windows.Forms`
are written FULLY QUALIFIED (`System.ComponentModel.CancelEventArgs`, …) — the WinForms scaffold imports neither
`System.ComponentModel` nor anything else (CLAUDE.md, M4). `(W)` = WinForms-only (no `WebEvent`).

| Kind | Events (default first) |
|---|---|
| Label | Click · DoubleClick · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · TextChanged (W) · Paint (W) |
| TextBox | TextChanged (`input`) · M · K · F · Validating (W, `System.ComponentModel.CancelEventArgs`) · Validated (W) |
| Button | Click · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · K · F · TextChanged (W) · Paint (W) |
| CheckBox | CheckedChanged (`change`) · CheckStateChanged (W) · Click · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · K · F |
| RadioButton | CheckedChanged (`change`) · Click · MouseDown · MouseUp · MouseEnter · MouseLeave · K · F |
| ComboBox | SelectedIndexChanged (`change`) · SelectedValueChanged (W) · DropDown (W) · DropDownClosed (W) · TextChanged (W) · Click · MouseDown · MouseUp · MouseEnter · MouseLeave · K · F |
| ListBox | SelectedIndexChanged (`change`) · SelectedValueChanged (W) · M · K · F |
| Panel | Paint (W, default) · Click (`click`, IsWebDefault) · DoubleClick · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · F · Resize (W) · Scroll (W) |
| GroupBox | Enter (`focusin`) · Leave (`focusout`) · Click (exempt, `click`) · TextChanged (W) · Paint (W) · Resize (W) |
| PictureBox | Click · DoubleClick · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · Paint (W) · Resize (W) · LoadCompleted (W, `System.ComponentModel.AsyncCompletedEventArgs`) |
| LinkLabel | LinkClicked (`click`) · Click (W — `click` is taken) · DoubleClick · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · TextChanged (W) |
| NumericUpDown | ValueChanged (`input`) · Click · DoubleClick · MouseDown · MouseUp · K · F · Validating (W) · Validated (W) |
| DateTimePicker | ValueChanged (`change`) · DropDown (W) · CloseUp (W) · MouseDown · MouseUp · MouseEnter · MouseLeave · K · F · Validating (W) |
| TrackBar | Scroll (`input`) · ValueChanged (`change`) · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · K · F |
| ProgressBar | Click · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · Resize (W) |
| CheckedListBox (W kind) | SelectedIndexChanged · ItemCheck · SelectedValueChanged · Click · DoubleClick · MouseDown · MouseUp · K · F |
| ListView (W kind) | SelectedIndexChanged · ItemActivate · ItemSelectionChanged · ItemCheck · ItemChecked · ColumnClick · Click · DoubleClick · MouseDown · MouseUp · KeyDown · KeyUp · F |
| TreeView (W kind) | AfterSelect · BeforeSelect · AfterCheck · BeforeExpand · AfterExpand · AfterCollapse · NodeMouseClick · NodeMouseDoubleClick · Click · DoubleClick · KeyDown · KeyUp · F |
| DataGridView (W kind) | CellContentClick · CellClick · CellDoubleClick · CellContentDoubleClick · CellValueChanged · CellBeginEdit · CellEndEdit · CellValidating · CellFormatting · CellMouseClick · SelectionChanged · RowEnter · DataError · KeyDown |
| TabControl (W kind) | SelectedIndexChanged · Selected · Selecting · Deselecting · Click · DoubleClick · MouseDown · MouseUp · KeyDown · KeyUp · F |
| SplitContainer (W kind) | SplitterMoved · SplitterMoving · Paint · Click · DoubleClick · MouseDown · MouseUp · MouseMove · Resize · F |
| FlowLayoutPanel / TableLayoutPanel (W kinds) | Paint · Click · DoubleClick · MouseDown · MouseUp · MouseMove · MouseEnter · MouseLeave · Resize · Scroll · F (+ TableLayoutPanel CellPaint) |
| Timer | Tick (unchanged — the snapshot has only Tick) |
| ToolTip | Popup · Draw (W) |
| ErrorProvider | RightToLeftChanged (unchanged — its only event) |
| BackgroundWorker | DoWork · ProgressChanged · RunWorkerCompleted (both `System.ComponentModel.*`, each with the row's existing `BackgroundWorkerHasNoMetadata`-style exemption for the description — O4 text) |
| MenuStrip | ItemClicked (`click`) · MenuActivate (W) · MenuDeactivate (W) · Click (W — `click` taken) · MouseDown · MouseUp · MouseEnter · MouseLeave · Paint (W) |
| ToolStrip / StatusStrip | ItemClicked (`click`) · Click (W) · MouseDown · MouseUp · MouseEnter · MouseLeave · Paint (W) |
| ToolStripMenuItem | Click · DoubleClick · MouseDown · MouseUp · MouseEnter · MouseLeave · CheckedChanged (W) · DropDownOpening (W) · DropDownOpened (W) · DropDownClosed (W) · DropDownItemClicked (W) · TextChanged (W) |
| ToolStripSeparator | Click (unchanged) |
| ToolStripButton | Click · DoubleClick · MouseDown · MouseUp · MouseEnter · MouseLeave · CheckedChanged (W) · CheckStateChanged (W) · TextChanged (W) |
| ToolStripStatusLabel | Click · DoubleClick · MouseDown · MouseUp · MouseEnter · MouseLeave · TextChanged (W) |

Enter/Leave are left OFF Label, PictureBox, ProgressBar and LinkLabel because **on WinForms those four are not
selectable** (`ControlStyles.Selectable` is off; Enter never fires for them in practice), so a VS user does not wire them.
(Not because the page cannot focus them: every positioned control gets a `tabindex`, `FormAssetEmitter.cs:315-318`.)

**Lost — every browsable event** (VS shows 48–172 per kind). Each listed event costs a parity row, a csc sweep entry, a
node run and (piece 2 Task 40) a library member; D1 says ~8–15. **Lost — the same list on every kind**: the snapshot
refuses it (M5), and a non-browsable event fails parity naming it.

### D-2 — Web names (plan 5.1; spec §7 "a web form never shows a WinForms-only event")

| WinForms | DOM `WebEvent` | Why |
|---|---|---|
| Click / DoubleClick | `click` / `dblclick` | the DOM's own |
| MouseDown / MouseUp / MouseMove | `mousedown` / `mouseup` / `mousemove` | piece-2 §5.6 table, verbatim |
| MouseEnter / MouseLeave | `mouseenter` / `mouseleave` | non-bubbling, as WinForms' are per-control |
| KeyDown / KeyUp | `keydown` / `keyup` | piece-2 §5.6 |
| KeyPress | stored `keypress`; **EMITTED as a filtered `keydown`** (D-12) | ✅ **COORDINATOR RULING (recorded verbatim in ADR 0021):** the bind's STORED identity stays `keypress` (so `NoRow_HasTwoEventsWithOneWebName` stays valid beside KeyDown's `keydown`); what is emitted is a `keydown` listener filtered to WinForms' KeyPress keys — `key.length === 1 \|\| key === "Enter" \|\| key === "Backspace" \|\| key === "Escape"` (WinForms raises KeyPress for `\b`, Esc, and Enter as `'\r'`). Mapping `e` to a `KeyChar` is piece 2's job; a classic handler receives the `DomEvent` |
| Enter / Leave | stored `focusin` / `focusout`; **EMITTED filtered** to "`relatedTarget` outside the element" (D-12) | ✅ **COORDINATOR RULING:** WinForms semantics — focus moving BETWEEN two children of a Panel raises no Enter/Leave on the Panel. Matches piece 2's §5.6 Enter rule. The same wrapper on every kind (on a leaf element the filter is a no-op; observable on containers) |
| existing defaults | unchanged (`input`, `change`, `click`, `tick`) | |
| MouseClick, MouseDoubleClick, MouseHover, Validating/Validated, Paint, Resize (on a control), every `…Changed` that is not a default, every kind-specific event above marked (W) | none | `NoRow_HasTwoEventsWithOneWebName` (`FormEventsTests.cs:272-282`) forbids a second `click`/`dblclick`; the rest have no honest page event |

**Lost — KeyPress WinForms-only.** Piece 2 implements KeyPress on the web, and game code wants it. **Lost — a real
`keypress` listener** (the previous revision): deprecated, and it does not fire for Backspace/Escape, which WinForms
raises. **Lost — `beforeinput`**: editable elements only. **Lost — `focus`/`blur`** for Enter/Leave: they do not bubble, so
a Panel's Enter would never fire for its children; **lost — unfiltered `focusin`**: it fires on the Panel for every
child-to-child move, which WinForms does not.

**Divergence table — classic (DOM-style) emission vs WinForms.** Each row: *classic emission diverges; piece 2's library is
responsible for WinForms parity.* Copied into `docs/form-designer-followups.md` in Task 9.

| Behaviour | WinForms | Classic page |
|---|---|---|
| Mouse events on a DISABLED control | none raised | browser-dependent (disabled form elements swallow some, not all, mouse events) |
| A double-click | Click once, then DoubleClick | `click`, `click`, `dblclick` → the Click handler runs TWICE |
| RadioButton CheckedChanged when it becomes UNchecked | raised on both radios | `change` fires only on the newly checked radio |
| TextChanged on a programmatic `Text` set | raised | `input` is not fired by setting `.value` |
| MouseLeave on a container when the pointer enters a child | raised (the child is another window) | `mouseleave` does not fire (the child is inside the element) |
| Form Click on a click that lands on a control | not raised | `body` receives the bubbled `click` (D-3) |
| Form KeyDown/KeyUp/KeyPress while a control has focus | only with `KeyPreview=True` | always (bubbled to `body`) (D-3) |
| A Load handler that throws | routed to `Application.ThreadException`; the form still shows | escapes the constructor (Load runs at the end of `InitializeComponent`); the page's dispatch dies (Task 2 review) |

### D-3 — The Form's events and the web wiring (plan 5.1/5.2, spec §2.3/§5)

`FormRoot.Events` (default **Load**, as the snapshot's `defaultEvent`):

| Event | Args | Web | Web wiring |
|---|---|---|---|
| Load | EventArgs | `load` | **AfterInit**: `Me.<Form>_Load()` is the LAST statement of the generated `InitializeComponent`; the handler is PARAMETERLESS |
| Shown, Activated | EventArgs | — | |
| FormClosing / FormClosed | FormClosingEventArgs / FormClosedEventArgs | — | |
| Resize | EventArgs | `resize` | **Window**: `w.addEventListener("resize", AddressOf …)` |
| Click | EventArgs | `click` | **Element** = `doc.body` |
| KeyDown / KeyUp / KeyPress | KeyEventArgs / KeyEventArgs / KeyPressEventArgs | `keydown` / `keyup` / stored `keypress` (emitted as the filtered `keydown`, D-12) | **Element** = `doc.body` |

**The representation:** `FormEventDef` gains two fields with defaults —
- `FormWebWiring WebWiring = FormWebWiring.Element`, enum `{ Element, Window, AfterInit }` in `FormEvents.cs`. Element is
  "the control's element" for a control and `doc.body` for the Form (its `HtmlTag` is `body`, `FormControlCatalog.cs:2662`).
- `FormWebFilter WebFilter = FormWebFilter.None`, enum `{ None, KeyPressKeys, FromOutside }` — the CATALOG states which
  events need the D-12 wrapper (every KeyPress row → `KeyPressKeys`; every Enter/Leave row → `FromOutside`); the emitter
  reads the field and never switches on an event name. `FormEvents.ListenType(evt)` is the ONE answer to "what DOM type is
  `addEventListener`ed": `keydown` for `KeyPressKeys`, else `WebEvent`.

`WiredOn`'s signature does not change. Invariants (Task 1 tests): `Window`/`AfterInit` only on `FormRoot` events; at most
one `AfterInit` event, and it is the row's default; `KeyPressKeys` exactly on the events stored `keypress`; `FromOutside`
exactly on `focusin`/`focusout`; a filter only on an event with a `WebEvent`.

**Lost — the form's layout `<div class="vgs-form">` as the root's Element** instead of `body`: the Docked strips are page
chrome OUTSIDE `.vgs-form` (`FormAssetEmitter.cs:175`), so a click or key on a MenuStrip/StatusStrip would never reach the
Form's handler, and the div carries no id the region could `getElementById`; `body` is where `data-form` and the form's
own CSS already live (`:156`).

**Emission (Task 2):** WinForms — `AddHandler Me.<Event>, AddressOf <h>` for every root bind, AFTER the reference rows
(`RegionWriter.cs:763-766`), immediately before `End Sub` — VS's place for `this.Load += …`. Web — the root listeners
after the controls (the elements exist), then `Me.<h>()` for the AfterInit bind as the very last line; `Dim w As Window
= ::window` is declared when a script component exists (today, `:719-722`) **or** a root bind is Window-wired, and never
otherwise (every existing page's region stays byte-identical). ⛔ `Me.`-qualified (the JS unqualified-self-call trap).

**User-facing consequence** (spec: "goes into the docs"): placed where the user meets it — a comment line in the WEB
scaffold beside `Me.InitializeComponent()` in `New()` (`FormScaffolder.cs:230`): *"On a web page, Form Load runs at the
end of InitializeComponent: code after this line runs after Load. On WinForms Load runs later, when the form is shown."*
Plus an entry in `docs/form-designer-followups.md`. RE-CHECK: `FormScaffolderTests` goldens for the web scaffold.

**Recorded divergences** (the last two rows of D-2's divergence table): the page's `click` on `body` bubbles from every
control; key events reach `body` from any focused control, as if `KeyPreview=True`. Classic emission diverges; piece 2's
library is responsible for WinForms parity.

**Lost — Load as a `window` `load` listener:** the D7 dispatch constructs the form after the page is ready, so the
window's `load` can already have fired — a handler that registers cleanly and silently never runs. **Lost — Shown on the
web** (the same moment as Load; two names for one instant would make the twin's ordering meaningless). **Lost —
FormClosing → `beforeunload`:** browsers discard work and custom prompts there, so the handler would look wired and do
nothing. **Lost — Activated → window `focus`:** a page has no window activation among windows.

### D-4 — Handler fitting (plan 5.4)

✅ **COORDINATOR RULING — ONE parameterised rule.** `FormHandlers.Shape(owner, evt, target, style)` →
`FormHandlerShape(Parameters, Placement)` is the ONLY place a handler's signature and its side of the init region are
decided. The stub writer (`Insert`) and `Fits` both READ it; neither restates it. Today the only style is
`FormCodeStyle.Classic`, and every rule below is a CLASSIC-style rule (the web's `(e As DomEvent)`/`()` and the AfterInit
call included). Piece 2's Task 30 ADDS `FormCodeStyle.Portable` as one arm of `Shape` (`(sender As Object, e As
<WinFormsArgs ?? EventArgs>)` on both targets, placement after the region) — never a second signature site. Pinned in ADR
0021; `FormCodeStyle` is the type piece 2's plan names (`BasicLang/Forms/FormCodeStyle.cs`, its "ONE reader of a file's
style") — created here with the single member `Classic`, so piece 2 extends it rather than inventing it.

`FormHandlers.FittingHandlers(form, owner, evt, codeText)` returns the Subs of the code-behind that FIT `Shape(…)`, in
document order, from ONE text scanner (`FormCodeScan`, D-11):
- **WinForms:** exactly two parameters; the first `Object` (or untyped); the second's type `T` fits an event with args
  `A` iff `T` is `EventArgs`, or `A`, or a .NET BASE of `A` — compared on the last segment, ignoring case. The base
  chains live in ONE table, `FormEvents.ArgsBases` (e.g. `CancelEventArgs` ⊃ FormClosingEventArgs,
  TreeViewCancelEventArgs, TabControlCancelEventArgs, DataGridViewCellCancelEventArgs,
  DataGridViewCellValidatingEventArgs, SplitterCancelEventArgs, DoWorkEventArgs; `MouseEventArgs` ⊃
  TreeNodeMouseClickEventArgs, DataGridViewCellMouseEventArgs; `AsyncCompletedEventArgs` ⊃ RunWorkerCompletedEventArgs).
  ⛔ The table is falsified EXHAUSTIVELY by in-process Roslyn (Task 3): for every event of every row and every args type
  the catalog names, `Fits` must equal csc's verdict on `ctl.E += H` with `H(object, T)`.
- **Web (classic):** a listener event (Element/Window, filtered or not) fits exactly one parameter typed `DomEvent`; an
  AfterInit event, or an event of a row with `WebHandlerTakesEvent: false` (Timer), fits exactly zero parameters.
- Never offered: Functions, `New`, `InitializeComponent`, anything inside the designer regions.

**Lost — the plan's "MouseEventArgs only mouse events" (by category):** wrong — `MouseClick` is in the Action category and
`NodeMouseClick`'s args derive from `MouseEventArgs`; fitting is a type rule. **Lost — fitting by compiling the
code-behind:** the drop-down opens on files midway through editing, which do not compile (the `FindDeclarationLine`
rationale).

### D-5 — The Events tab (plan 5.5; VS behaviour where in doubt)

- **Toolbar:** a second pair of radios, **Properties | Events**, in their OWN `StackPanel` — an unnamed radio groups by
  its PARENT (`:91-95`), so a second pair inside the existing panel would join Categorized/A-Z's group and uncheck it.
  Events shows a vector lightning bolt (`Path` geometry — no glyph, so neither a font nor the Edit-tool escape trap is
  involved) with `ToolTip`/`AutomationProperties.Name="Events"`; Properties keeps text. The mode survives a selection
  change (VS).
- **Rows:** a new `FormEventRow : IFormDisplayRow` per `FormEvents.WiredOn(definition, target)` — ⛔ the seam, never
  `definition.Events` (a web Panel never shows Paint; a web Timer shows only Tick). Definition = the selected control's
  row, or `FormRoot` with nothing selected. Categorized by `FormEventCategory` or A–Z; search by name. A SEPARATE
  `FormPropertyDisplayList` instance per mode, so collapsing "Behavior" among events does not collapse it among
  properties. The description pane shows the event's Description.
- **Value cell:** an editable `ComboBox` — the drop-down lists `FittingHandlers`; the text is the bound handler.
  - **Pick** an item → bind (`<Bind>` in the target's vocabulary — the DOM name on the web), ONE `Edited`, the `.bas`
    untouched.
  - **Clear** the text and commit → unbind (the `<Bind>` removed), ⛔ code never deleted.
  - **Type** a new legal identifier and commit → create the stub with THAT name and bind it (VS). An illegal identifier,
    or an existing Sub that does not fit, is REFUSED in the description pane and nothing is written (spec §7's pattern).
  - **Double-click** the name or an EMPTY value → create-or-navigate `<Id>_<Event>` / `<FormName>_<Event>` (WinForms
    event name on both targets — slice-3 owner decision). Double-click a BOUND value → navigate to its handler.
  - ⛔ `Choices` returns the SAME list instance while its items are unchanged (slice-4 Task 3 finding: a new instance
    during a pick undoes the pick).
- **Freshness:** the document view model pushes the code-behind text into the grid (`PropertyGridViewModel.CodeBehindText`)
  on every `SyncDesignerPanels` and after each designer write of the `.bas`; the view asks for a refresh on the combo's
  `DropDownOpened` (an async re-read: open tab first, then disk — `ReadCodeBehindAsync` `:827-828`). ✅ Coordinator
  ruling: pinned by a real-view test — a Sub TYPED into the open, UNSAVED code-behind tab appears in the handler
  drop-down on the next open — with the mutation that removes the refresh (Task 6).
- **Lost — a read-only handler column with double-click only** (VS lets you pick and type). **Lost — a separate Events
  window** (VS uses the same grid).

### D-6 — Retarget: ONE crossing rule (plan 5.6, both CARRIED notes)

- One private `CrossBinds(string owner, FormControlDef definition, IEnumerable<FormBind> from, List<FormBind> into)`
  used for controls, components AND the root: a bind crosses iff the event it names in `WiredOn(def, from)` is also in
  `WiredOn(def, to)`; it crosses under the DESTINATION's name with the user's HANDLER name kept; otherwise BL8026 names
  the owner (`'btn'` / `'form'`), the event, the handler, and the both-sides list. Reserved data binding is cloned as
  today.
- **A non-default bind crosses exactly as a default one does** (MouseDown on a Button: `MouseDown` ⇄ `mousedown`, handler
  kept). The pair's code-behind gets a stub in the destination's signature for EVERY crossed bind — the root's included
  (the `ConvertToPair` blocker, §1).
- `WiredRunState` through the seam: the implied property applies iff a source bind resolves (through `WiredOn(def,
  from)`) to an event in `WiredOn(def, Web)` — a component's web wiring IS its template's default event.
- **Lost — crossing only the default event** (the slice-1 rule; it dropped working binds, B1). **Lost — carrying an
  unwired bind as an unknown child** (dead data the grid would show).

### D-7 — Diagnostics: NO new code claimed

- A WEB root bind on an event the Form does not wire on the web (`Shown`, a typo) → **BL8032** (refused, owner `'form'`) —
  the controls' rule, through the same seam. It used to be a BL8028 warning; the stricter answer is consistent and no
  shipping path writes such a bind.
- BL8013 extends to the root's LISTENER binds (body/window, via `AddressOf`); an AfterInit bind and the user's handler of a
  WRAPPED bind (D-12) are reached by a direct call and are exempt (M2) — the `AddressOf` names the generated wrapper, which
  is always above the region's `InitializeComponent`.
- BL8028 is no longer raised for root binds (components keep it). BL8026 for retarget loss. BL8035 unchanged.
- BL8032's message lists the declared events comma-separated.
- **BL8037 stays free for piece 2.** **Lost — a "handler does not fit its event" code:** both failures are already LOUD
  (csc CS0123 on WinForms; BasicLang's delegate-conversion / arity error on the web), and the designer never writes a
  misfit. Follow-up if the messages prove unreadable.

### D-8 — The representation piece 2 consumes (ADR 0021, written FIRST in Task 1)

Piece 2 reads, per its plan Task 40 and spec §5.6/§10.1/§11.1: "every event slice 5 listed gets a library member … the
coverage gate picks them up from the catalog". What it gets:
1. `FormControlDef.Events` / `FormRoot.Events` — `FormEventDef(Name, WinFormsArgs, WebEvent, Category, Description,
   IsDefault, OracleExemption, IsWebDefault, WebWiring, WebFilter)`. `WebEvent` stays THE one list of stored DOM
   identities (its §10.1); null = WinForms-only (its `WebUnavailableMember` in shared code). The DOM type actually
   listened to is `FormEvents.ListenType(evt)` (`keydown` for KeyPress) — its library listens to the same.
2. `FormEvents.WiredOn(definition, target)` — unchanged signature.
3. `FormEvents.DomInterfaceOf(webEvent)` — a **GATES-ONLY** table (the emitter never reads it), keyed by the STORED name,
   stated explicitly: `click`, `dblclick`, `mousedown`, `mouseup`, `mousemove`, `mouseenter`, `mouseleave` → `MouseEvent`;
   `keydown`, `keyup`, `keypress` → `KeyboardEvent` (a KeyPress is DISPATCHED as a `keydown` carrying `key`, through
   `ListenType`); `focusin`, `focusout` → `FocusEvent`; `input`, `change`, `resize`, `load` → `Event`. **`tick` is
   excluded** — a Timer is a `setInterval` callback, never a dispatched DOM event. Slice 5's node/Edge tiers and piece 2's
   coverage gate both read it; no second table.
4. `WebWiring` — where the DOM source is for the Form (body / window / a call at the end of init); `WebFilter` — which key
   set (the ruling's four) or focus rule (relatedTarget outside) a WinForms event means.
5. `FormHandlers.Shape(owner, evt, target, style)` and `FormCodeStyle` (D-4) — the ONE signature/placement rule its Task 30
   extends with `Portable`. ⚠ Its plan creates `FormCodeStyle.cs`; after this slice it EXTENDS it (an add/add conflict if it
   is created on its branch first — re-check at its Task 30 pre-flight).

What piece 2's Task 40 must add beyond its §5.6 table: **DoubleClick (`dblclick`), MouseEnter/MouseLeave, Leave
(`focusout`, relatedTarget outside), Form Load (AfterInit), Resize (window), Form Click/KeyDown/KeyUp/KeyPress (body)**.
KeyPress is ALREADY consistent with its §5.6 ("a character-producing `keydown`"): the ruling's key set is the definition of
"character-producing" both sides use; mapping `e` to `KeyChar` (`'\r'` for Enter, `'\b'`, `ChrW(27)`) is its job. Its Task 30
rewrites the stub signature arm that Task 3 here centralises — re-anchor on this slice's merge.

### D-9 — Double-clicking the form's background opens `<Form>_Load` (VS)

✅ **IN (coordinator ruling).** `FormCanvasControl.OnCanvasDoubleTapped` (`:715-718`) — when the hit test finds no
control — runs a NEW `ActivateFormCommand` (a new StyledProperty, bound in `CodeEditorDocumentView.axaml` beside
`ActivateControlCommand` `:247`) **only when the point is on the FORM SURFACE** (inside `FormCanvasTransform.SurfaceSize`,
the one answer to "how big is the form", in canvas coordinates through the transform). ⚠ The canvas OUTSIDE the form also
hit-tests null, and must still do nothing — both are tested. `ActivateControlCommand(null)` stays a no-op, so no existing
caller's meaning changes. **Lost — leaving the root reachable only through the Events tab** (VS users double-click the
form for Load by habit).

### D-12 — The filtered listener: a GENERATED wrapper Sub (KeyPress, Enter, Leave)

For an event whose `WebFilter` is not `None`, the web init region gets, ABOVE `Private Sub InitializeComponent()`, one
generated Sub per (owner, filtered event) — calling every handler bound to that event — and the listener names IT
(review of Task 2: "per bind" would emit two Subs of one name). ⚠ Amended by the Task 2 review: the KeyPress length is
counted in CODE POINTS (`Dim n As Integer = ::Array.from(k).length`), and the listener order per owner puts every plain
listener before every wrapper's (KeyDown before KeyPress). The text below is the original M7 measurement:
```vb
Private Sub VgsOn_txt_KeyPress(e As DomEvent)
    Dim k As String = e.key
    If k.Length = 1 OrElse k = "Enter" OrElse k = "Backspace" OrElse k = "Escape" Then
        Me.txt_KeyPress(e)
    End If
End Sub
Private Sub VgsOn_pnl_Enter(e As DomEvent)
    If e.relatedTarget Is Nothing OrElse Not e.currentTarget.contains(e.relatedTarget) Then
        Me.pnl_Enter(e)
    End If
End Sub
' … inside InitializeComponent:
txt.addEventListener("keydown", AddressOf VgsOn_txt_KeyPress)
pnl.addEventListener("focusin", AddressOf VgsOn_pnl_Enter)
```
Measured exactly as written on the branch CLI (M7). Rules:
- **Named Subs, never a lambda:** `Me.` inside a lambda hard-errors on the JS backend and an unqualified call is a
  `ReferenceError`; a named wrapper calls `Me.<handler>(e)` outside any lambda (M7 emitted `this.txt_KeyPress(e)`).
- **Above `InitializeComponent`:** `AddressOf` a Sub declared later erases its parameter types (BL8013's measured rule, and
  piece 2's M25). The wrapper's own call to the user's handler is a direct call, which the erasure does not bite (M2) — so
  BL8013 checks the WRAPPER's position (always above, by construction) and exempts the user's handler for a wrapped bind.
- **Naming:** `VgsOn_<owner prefix>_<WinForms event>` (owner prefix = the control Id, or the form name for the root); the
  `VgsOn_` prefix is reserved, documented in the region's generated comment; a user Sub of the same name is a duplicate
  member BasicLang reports loudly.
- **One rule:** the wrapper body is built from `WebFilter` in one function (`RegionWriter.Wrapper`), never from the
  event's name. Web only — WinForms raises KeyPress/Enter/Leave itself and wires the handler directly.
- `relatedTarget`/`contains` are NOT added to `dom-core.bli` (M7: accepted untyped; piece 2's Task 28 declares both — adding
  them here would collide with that commit on the two load-bearing copies). The node and Edge runs gate the text.

**Lost — a helper in `dom-core.bli`:** that file is `Extern Class` declarations only and EMITS NOTHING (its header, `:4-6`) —
it cannot hold the filter's code; a new emitting library file would be a third load-bearing copy for the IDE drop.
**Lost — an inline lambda** (`Sub(e) If … Then Me.h(e)`): the `Me.`-in-lambda hard error (CLAUDE.md). **Lost — filtering
inside the user's handler** (a generated `If` in their Sub): the designer never writes inside a user's Sub.

### D-10 — Owners

`FormBindOwner` (new, `BasicLang/Forms/FormBindOwner.cs`): `(FormDocument Form, FormControl? Control)` →
`Definition` (`Control?.Definition ?? FormRoot`), `Binds` (`Control?.Binds ?? Form.Binds`), `Prefix`
(`Control?.Id ?? Form.Name`), `Label` (`'btn'` / `'form'`). Every FormHandlers entry point and the retarget's
`CrossBinds` take it. Stubs go where they go today (web ABOVE the init region, WinForms BELOW, `FormHandlers.cs:231-241`).

### D-11 — One case-insensitive handler scanner

`FormCodeScan` (new, `BasicLang/Forms/FormCodeScan.cs`): `DeclaredSubs(codeText)` → name, line, parameters
(name, type) — modifiers allowed, implicit line continuation inside the parameter list, a `'` comment ends the line,
names compared OrdinalIgnoreCase (M6), Subs inside the designer regions excluded. `FormHandlers.FindDeclarationLine`
and `RegionWriter.FindHandlerDeclarationLine` both DELETE and call it (a mirrored pair is the `Tracks`/`ParseTracks` scar).

## 3. Escalations

None sent to the architect: nothing here touches backends, IR, the C++ std model, the engine sync or IDE layering. The
one cross-feature contract (D-8, consumed by piece 2) is recorded as **ADR 0021** before code (Task 1).

## 4. The tasks (TDD, one commit per task)

Every task: red test(s) first, run them, see them fail for the RIGHT reason; implement; green. Mutation-check each new
branch: apply with the **Edit** tool and **REBUILD** (a mutant survives a revert until you rebuild); record killed or
EQUIVALENT. Stage by name (never `git add -A`, never `csc.dll`); message via a scratchpad file + `git commit -F`. Every
real-view test: `[AvaloniaTest]`, the rig's `TwoZooms` (800×560 below zoom 1.0, 1400×900 at 1.0), stable fixture ids,
REAL clicks (`PickInCombo` opens the drop-down and clicks the item in its popup), buttons LEFT of editors (the overlay
scrollbar), every window closed. **After any AXAML change: `dotnet clean` before building.** Catalog-driven
`TestCaseSource` only — never a hand-written `[TestCase]` list beside the catalog.

### Task 0 — Baseline and re-measure
Build `VisualGameStudio.Tests` Release. Fast subset at `2fa64789`, BOTH streams captured; record total/passed/failed/
skipped and the **sorted failure names** with the base SHA (expected: only the known machine rows, §5). Re-measure M1/M2
on the BRANCH build through the PROJECT route (a scratch web project, `BasicLang.exe build`), since M1/M2 used the stale
`IDE\` drop.

### Task 1 — The event lists, `WebWiring`/`WebFilter`, `DomInterfaceOf`, ADR 0021 (plan 5.1)
First: `docs/superpowers/decisions/0021-form-event-lists-and-web-wiring.md` + the README index. It records: D-2 (incl.
the coordinator's KeyPress ruling — stored `keypress`, emitted filtered `keydown`, the four-key set, `e`→`KeyChar` is piece
2's — and the Enter/Leave relatedTarget ruling), D-3, D-8, D-12, the divergence table, and D-4's ONE parameterised
`Shape(owner, evt, target, style)` rule with `Fits` and AfterInit stated as CLASSIC-style rules piece 2 extends.
Files: `FormEvents.cs` (`FormWebWiring`, `FormWebFilter`, the two record fields, `ListenType`, `DomInterfaceOf` — the
explicit gates-only table of D-8 item 3 — and an `ArgsBases` stub filled in Task 3; its doc comment `:53-66` updated);
new `FormCodeStyle.cs` (`Classic` only, D-4); `FormControlCatalog.cs` (every row per D-1, `FormRoot.Events` per D-3; the stale `Ev`/
`Events` comments `:1396-1410`, `:1869-1873`); `DesignDiagnostic.cs` (BL8026/BL8032 doc text `:230-236`, `:314-318`).
Procedure: names/args/web names first, then the parity run prints the category and description of each, pasted verbatim.
Tests (`FormEventsTests`, fast):
- REWRITE `WiredOn_TheFormRoot_IsEmpty_UntilFormEventsExist` → `WiredOn_TheFormRoot_IsItsTenEvents_OnWinForms_AndSix_OnTheWeb`
  (web: Load, Resize, Click, KeyDown, KeyUp, KeyPress).
- REWRITE the exact-list pins (`:125-136`, `:166-183`, `:189-203`, `:209-220`, `:259-270`) as catalog-derived or
  `Contains` assertions that keep their point (Enter is GroupBox's default; Paint is never on a web Panel; …).
  ✅ `APanelKind_DefaultsToPaint_AndKeepsClick`'s hand-written `[TestCase("Panel")]`/`FlowLayoutPanel`/`TableLayoutPanel`
  list (`:189-191`) becomes a `TestCaseSource` over the catalog — every row whose `DefaultEventDef.Name == "Paint"` — with a
  floor assertion that the source is non-empty (never "a hand-written list beside the catalog").
- `NoRow_HasTwoEventsWithOneWebName` extended to `FormRoot` (`All.Append(FormRoot)`).
- NEW invariants, each over `All` + `FormRoot`: `Window`/`AfterInit` only on FormRoot; ≤ 1 AfterInit and it is the
  default; `KeyPressKeys` exactly on the events stored `keypress` and `ListenType` = `keydown` for them; `FromOutside`
  exactly on `focusin`/`focusout`; a filter only where `WebEvent` is set; **every `WebEvent` other than `tick` is a key of
  `DomInterfaceOf`'s explicit table, and `tick` is not in it** (D-8 item 3 — a gates-only table, asserted never read by
  `RegionWriter`: a grep-style test over `RegionWriter.cs` for `DomInterfaceOf`); every `WinFormsArgs` whose snapshot
  namespace is not `System`/`System.Windows.Forms` is written qualified (reads the oracle's `argsFullName`); every row that
  declares events has exactly one default (exists, `:14-24`).
- Parity (automatic, fast): `WinFormsCatalogParityTests` per kind and `TheFormRoot_MatchesTheSnapshotsForm`.
Mutations: FormRoot Load's `WebEvent` null (the root WiredOn test); MouseClick given `click` (NoRow…); an event's
`WebWiring = Window` on a control (invariant); a TextBox KeyPress with `WebFilter.None` (invariant); `tick` added to the
interface table (invariant); `System.ComponentModel.` dropped from Validating (qualification test, and
Task 4's csc sweep); a listed event that is not browsable (parity names it).
RE-CHECK: the six `FormEventsTests` rows above; `FormRegionWriterTests.Write_Web_RefusesAnUnknownEvent_NotOnTheRow`
(`:1040`, `mouseenter` is now ON the row → change the case to `mouseover`, still not on the row; `:1051` still finds
`'click'`); `FormRetargetTests.ToWeb_ABindOnAnEventTheCatalogCannotName_IsDroppedAndReported` (`:484-512`, MouseEnter
now crosses → use Button `Paint`, WinForms-only); `FormRetargetTests.EveryEventWiredOnBothTargets_…` (`:581-613`,
automatically larger — note its run time); `FormComponentEmissionTests` (`:204`, Timer unchanged); `FormHandlerPlanTests`
(`:69`, `:98` — default events unchanged); `WinFormsCatalogParityTests.…DefaultEvent…` (`:241`).

### Task 2 — Root bind emission (plan 5.2)
Files: `RegionWriter.cs` — `CheckRootBinds` becomes the web BL8032 refusal for the root through the seam; a root arm in
`AppendBinds` (Element → `doc.body.addEventListener`, Window → `w.addEventListener`, the CATALOG's spelling);
`GenerateInit` emits WinForms `AddHandler Me.<Event>` after the reference rows, web listeners after the controls and
`Me.<h>()` last; the `Dim w` condition widened; `CheckHandlerOrdering` includes root LISTENER binds and exempts the user's
handler of a WRAPPED bind (D-12); the BL8032 message comma-joined; **D-12's wrappers** — `GenerateInit` emits one
`VgsOn_…` Sub per filtered web bind (controls AND root) above `Private Sub InitializeComponent()`, built by ONE
`RegionWriter.Wrapper(owner, evt, handler)`, and the listener is `addEventListener(ListenType(evt), AddressOf VgsOn_…)`. `FormDocument.cs:103-108` doc. `FormScaffolder.cs` web comment (D-3).
Tests (`FormRootTests`, fast):
- REWRITE `ARootBind_IsWarned_NotEmitted_UntilFormEventsExist` → `ARootBind_IsWired_AsTheLastStatement_OnWinForms`
  (`AddHandler Me.Load, AddressOf F_Load` is the line before `End Sub`, after `Me.AcceptButton = …`).
- `TheWebLoad_IsAMeQualifiedCall_LastInInitializeComponent` (the exact `Me.F_Load()`, after every `getElementById`).
- `AWebRootKeyBind_ListensOnTheBody`, `AWebRootResizeBind_ListensOnTheWindow_AndDeclaresW`.
- `APageWithoutWindowBinds_DeclaresNoW` — every existing region-writer golden stays byte-identical.
- `AWebRootBind_OnShown_IsRefusedAsBL8032_NamingTheForm` (refused, nothing written).
- `AWebRootKeyHandler_DeclaredBelowTheRegion_IsBL8013` and `AWebLoadHandler_DeclaredBelow_IsNot` (M2).
- `FormScaffolderTests`: the web scaffold carries the Load comment.
- `FormRegionWriterTests` (D-12): a web TextBox KeyPress bind emits exactly M7's `VgsOn_txt_KeyPress` text ABOVE
  `InitializeComponent` and `txt.addEventListener("keydown", AddressOf VgsOn_txt_KeyPress)` — never `"keypress"`; a Panel
  Enter/Leave emits the relatedTarget wrapper on `focusin`/`focusout`; a root KeyPress wraps on `doc.body`; a page with no
  filtered bind emits NO `VgsOn_` (existing goldens byte-identical); the user's KeyPress handler declared BELOW the region is
  not BL8013 (wrapped, a direct call), a Click handler declared below still is; a WinForms KeyPress is a plain
  `AddHandler txt.KeyPress` (no wrapper).
Mutations: Load called before the controls; the KeyPress listener emitted as `keypress` (exact text, and Task 4's
Backspace/Escape dispatch); the wrapper placed BELOW `InitializeComponent` (exact text, and the JS build's delegate
error); the relatedTarget test dropped (Task 4's child-to-child step); `Me.` dropped (the exact-text test, and Task 4's node run: `ReferenceError`);
`w` not declared for a Resize bind (unit test, and Task 4's JS compile); the root BL8032 check removed; BL8013 skipping
root listeners.
RE-CHECK: `FormRootTests.cs:183`, `:249` (root-bind read/write fixtures); every `FormRegionWriterTests` golden;
`FormComponentEmissionTests` (the `w` line with a Timer — byte-identical); `FormScaffolderTests` web goldens;
`FormBuildEmissionTests`.

### Task 3 — Owners, the shared scanner, fitting (plan 5.3/5.4)
Files: new `FormBindOwner.cs`, new `FormCodeScan.cs`; `FormHandlers.cs` — `Plan(form, owner, evt, code, handlerName?)`,
`PlanDefault` delegating (unchanged signature and behaviour), `PlanBind(form, owner, bind, code)` (the control overload
kept, delegating), `EnsureBind(owner, …)`, `Unbind(owner, eventName)`, `FittingHandlers`, `Fits`; **`Shape(owner, evt, target, style)` →
`FormHandlerShape`** (D-4) replaces the signature site (`:217-222`) and the placement choice (`:239-241`): `Insert` and
`Fits` both read it; `()` for AfterInit and `WebHandlerTakesEvent: false` live there only; `FindDeclarationLine` deleted.
`RegionWriter.FindHandlerDeclarationLine` deleted → `FormCodeScan`. `FormEvents.ArgsBases` filled.
Tests:
- `FormHandlerPlanTests`: root Load → `Private Sub LoginForm_Load(sender As Object, e As EventArgs)` BELOW the region
  (WinForms) / `Private Sub LoginForm_Load()` ABOVE (web); `btn_MouseDown(sender As Object, e As MouseEventArgs)`;
  `pnl_Resize`-style web Window stub `(e As DomEvent)`; an existing bind's handler wins; **a hand-written
  `btnlogin_click` is NAVIGATED to, never duplicated** (red on the old `Ordinal` scan — the M6 kill); a mid-line comment
  `' Sub btn_Click` is not a declaration; a Sub inside the designer region is not offered.
- `FormHandlerFitTests` (new, fast, `TestCaseSource` over every event of every row incl. FormRoot × target): an
  `EventArgs` handler fits every WinForms event; the event's own args fit; a `KeyEventArgs` handler does not fit
  MouseDown; `MouseEventArgs` fits `NodeMouseClick`; web `(e As DomEvent)` fits every listener event and nothing
  parameterless; `()` fits Load and Timer Tick only; Functions/`New`/`InitializeComponent` never; document order.
- `FormHandlerFitCscTests` (new, FAST — in-process Roslyn via `WinFormsCompile.Errors`, which uses the reference PACKAGE
  so it runs on Linux too): per WinForms kind + Form, ONE C# source with a line per (event, T) pair, T ∈ {EventArgs} ∪
  every args type the catalog names; each line's verdict read from the diagnostics' line numbers; `Fits` ⇔ csc, both
  directions. Measure the run time; if over ~10 s split by kind.
- `FormHandlerShapeTests` (new, fast, `TestCaseSource` over every event × target, Classic style): the stub `Insert` writes
  is exactly the one `Fits` accepts (a stub planned then scanned FITS its own event — the "one site" pin), and a structural
  test that `FormHandlers.cs` builds a parameter list in `Shape` only (no `"(sender As Object"` / `"(e As DomEvent)"`
  literal outside it).
Mutations: `Insert` given its own signature literal again (the shape pin red); an `ArgsBases` entry removed (CancelEventArgs ⊃ FormClosingEventArgs) → the csc test; fit by exact name only
→ the csc test; the scanner back to `Ordinal` → the case test; `()` accepted for a web listener → the fit test;
`PlanDefault` no longer delegating (a second signature site) → the existing gesture tests.
RE-CHECK: `FormHandlerPlanTests` (26 call sites), `FormHandlerGestureTests`, `FormHandlerReachabilityTests`,
`FormCanvasDoubleClickTests`, `FormComponentEmissionTests`, `FormDesignerCommandTests`, the BL8013 tests in
`FormRegionWriterTests` (now through `FormCodeScan`).

### Task 4 — Every event through csc and through a running page (Integration)
- `WinFormsCatalogSweepTests.EveryEvent_OfEveryControl_WiresIntoCSharpThatCscAccepts` (`TestCaseSource`: every WinForms
  kind, plus a FormRoot case): plan EVERY event through `FormHandlers.Plan(owner, evt)`, `EnsureBind` each, the region
  writer, the real CLI to C#, csc. ONE compile per kind. `TheDefaultEvent_…` (`:217-248`) stays — it gates the gesture
  path.
- New `FormEventWebRunTests` (Integration; ⚠ `JsExecutionTierRosterTests.RosterIsPinned` +1 — it collides SILENTLY on
  merge, read it): per web kind with a web event, and the Form: a `.blwebform` + code-behind made by `FormHandlers.Plan`
  + `EnsureBind` + `RegionWriter`, built by the real CLI on the PROJECT route (`dom-core.bli`, the D7 dispatch), each
  handler printing its own name; run under node with a stub DOM (`getElementById` → `EventTarget`s, `document.body`,
  `window`, each element with a `contains` stub that knows its children); dispatch every wired event once with
  `ListenType(evt)` as the type (`new Event(type, { bubbles: true })` — node has no `MouseEvent`/`KeyboardEvent`, so the
  node tier proves wiring BY NAME). Key events carry `key`: a **KeyPress** is dispatched as `keydown` with `key` ∈ {`a`,
  `Enter`, `Backspace`, `Escape`} → its handler ran FOUR times, and with `Shift`, `ArrowLeft`, `F1` → never (the ruling's
  key set, M7); a KeyDown handler on the same control runs for all seven. Enter/Leave carry `relatedTarget`: from OUTSIDE
  → the Panel's Enter ran once; **from one child of the Panel to another → the Panel's Enter and Leave did NOT run**
  (coordinator ruling 2); `null` → ran. Assert each other handler ran exactly once, and `Load` ran once at construction
  BEFORE any dispatch.
- Edge, where installed (SKIP otherwise, never fail): one page (Button with Click/MouseEnter/KeyDown, a TextBox with
  KeyPress, a Panel with two TextBoxes and Enter, the Form's Load and Resize) through the loopback-served CLI-built site
  (slice-4 Part F shape); a new `EdgeStep.Dispatch(label, target, type, key?, relatedTarget?)` dispatching
  `new (DomInterfaceOf(webEvent))(ListenType(evt), { bubbles, key, relatedTarget })` — the real interfaces; a KeyPress is
  SENT as a `KeyboardEvent("keydown", { key })` (`DomInterfaceOf("keypress")` = `KeyboardEvent`); a real `.focus()` from
  one Panel child to the other must not raise the Panel's Enter. The page writes each handler's name into a label read back
  by `HasText`. ⚠ Synthetic dispatch, not real input — real input (CDP `Input.*`) is piece 2 Task 35.
Mutations: the web emitter writes the WinForms spelling (`"Click"`) → node red; AfterInit unqualified → node
`ReferenceError`; Load emitted as a `load` listener → "Load ran once at construction" red; Window bind on `doc.body` →
the resize dispatch never runs; `Backspace` dropped from the key set → the four-count red; the relatedTarget test dropped →
the child-to-child step red; KeyPress listening on `keypress` → node red (the dispatch is a `keydown`).

### Task 5 — The grid's Events mode (VM; plan 5.5)
Files: new `ViewModels/Designer/FormEventRow.cs` (`IFormDisplayRow`; `Handler` text; `Choices` same-instance; commit →
Bind / Unbind / HandlerRequested / refusal); `FormPropertyGridViewModel.cs` (`[ObservableProperty] _isEventsMode` — one
attribute per field; `IsPropertiesMode` the other face; a second display list; `CodeBehindText`; event
`HandlerRequested(FormBindOwner, FormEventDef, string?)`; rows from `WiredOn`).
Tests (`FormEventGridTests`, new, fast): rows ≡ `WiredOn(definition, target)` for every kind × target, FormRoot with
nothing selected (`TestCaseSource`); a web Panel has no Paint; a web Timer only Tick; category headers are
`FormEventCategory` names; search; picking a fitting handler → ONE `Edited`, the `<Bind>` in the target's vocabulary
(`click` on the web, `Click` on WinForms), `.bas` text untouched; clearing removes the Bind and leaves code alone; a typed
new legal name raises `HandlerRequested` with that name; an illegal or non-fitting typed name is refused in the
description pane, nothing written; the mode survives a selection change; Properties mode unchanged (the slice-2/3/4
display tests stay green); collapse memory is per mode.
Mutations: rows from `definition.Events` (Paint offered on a web Panel); the WinForms name stored on the web; a new
`Choices` instance per read (VM same-instance step, and Task 6's real pick); unbind also edits the `.bas`.
RE-CHECK: `FormPropertyGridDisplayTests`, `FormPropertyGridTests`, the selector tests.

### Task 6 — The view, the gestures, both entry points into a handler
Files: `FormPropertyGridView.axaml` (the Properties|Events pair in its own panel, the bolt `Path`, the `FormEventRow`
`DataTemplate`: name | editable combo, automation name "Handler for <event>"); `FormPropertyGridView.axaml.cs`
(double-tap on an event row → `HandlerRequested`; `DropDownOpened` → refresh; ⚠ confirm the popup `RequestBringIntoView`
guard on row containers covers the new rows); `CodeEditorDocumentViewModel.cs` (`ActivateControlAsync` generalised to
ONE `ActivateHandlerAsync(FormBindOwner, FormEventDef?, string?)` — `ActivateControl` and a new `[RelayCommand]`
`ActivateFormAsync` delegate, each attribute directly above its method; grid `HandlerRequested` wired; `CodeBehindText`
pushed); `FormCanvasControl.cs` + `CodeEditorDocumentView.axaml` (`ActivateFormCommand`, D-9). `dotnet clean`.
Tests:
- `FormPropertyGridViewTests`: the binding walker (automatic, the new template); automation names (Properties, Events,
  the handler combo); `TwoGridViewsInOneWindow_…` extended: clicking Events leaves Categorized checked, and two views do
  not share a mode.
- Real view (new `FormPropertyGridEventsRealViewTests.cs`, a part of the `partial` rig; temp project folder through the
  rig's `Open(dir:)`), at `TwoZooms`: (a) a real click on Events → `btn`'s rows are the seam's; (b) double-click the
  EMPTY Click value → the `.bas` ON DISK gains `Private Sub btn_Click(sender As Object, e As EventArgs)` below the
  region, the `.blform` gains `<Bind Event="Click" Handler="btn_Click"/>`, a `NavigateToFileEvent` with the caret line;
  (c) MouseDown → `e As MouseEventArgs`; (d) two existing Subs (`EventArgs`, `KeyEventArgs`): the real drop-down lists
  only the first; `PickInCombo` binds it; `.bas` byte-identical; (e) clearing unbinds, `.bas` byte-identical, ONE Ctrl+Z
  restores the Bind; (f) WEB fixture: a Panel shows no Paint; double-click Click → `Private Sub pnl_Click(e As DomEvent)`
  ABOVE the region and `<Bind Event="click" …>`; (g) the Form (selector's root entry): double-click Load →
  `LoginForm_Load`; (h) typing `DoIt` + Enter → a `DoIt` stub, bound; **(i) freshness (coordinator ruling 5):** the
  code-behind is OPEN in a second tab (`OpenDocumentLookup`), the user types `Private Sub Typed(sender As Object, e As
  EventArgs)` into it WITHOUT saving, then opens `btn`'s Click drop-down with a real click → `Typed` is listed (the disk
  never had it); picking it binds it, and the disk `.bas` is still untouched.
- `FormCanvasDoubleClickTests`: REWRITE `DoubleClickingEmptyFormBackgroundOpensNothing` into TWO tests —
  `DoubleClickingTheFormSurface_AsksTheHostToOpenTheFormsLoad` (`ActivateFormCommand` run, `ActivateControlCommand` not)
  and `DoubleClickingTheCanvasOutsideTheForm_OpensNothing` (a point past `SurfaceSize`: neither command runs) — at more
  than one zoom; plus an AXAML read that `CodeEditorDocumentView.axaml` BINDS `ActivateFormCommand` (an unbound command is
  unreachable — CLAUDE.md).
- `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…`: in Events mode the handler combo is swept and REQUIRED seen.
Mutations: the pair in the existing panel (the group test); `HandlerRequested` unwired ((b) red); the background
double-click not routed (the surface test); the surface bounds check removed (the outside-the-form test); **the
`DropDownOpened` refresh removed / `CodeBehindText` not pushed from the open tab** ((i) red); the Bind written before a failed stub write (a refused gesture must write
nothing — a read-only `.bas`); the guard not covering event rows (if it does not reproduce headless, record EQUIVALENT
with the measurement).
RE-CHECK: `TheDocumentView_NoLongerCarriesTheGridsOwnBindings`, `TheToolbar…AutomationName`, `FormTrayViewTests`
(tray double-click still `ActivateControl`), `FormStripCanvasTests`, `FormTypeHereEditorTests` (item double-click
forwarding).

### Task 7 — Retarget: one rule, the root, the pair (plan 5.6)
Files: `FormRetarget.cs` (`CrossBinds` for controls, components, root; `ConvertRootBinds`/`ConvertBinds` deleted;
`WiredRunState` via the seam; `ConvertToPair` stubs the ROOT's crossed binds too, through `PlanBind(owner)`).
Tests:
- `FormRootRetargetTests`: NEW `ARootLoadBind_Crosses_BothWays_KeepingItsHandler` (`Load` ⇄ `load`) — the crossing
  branch made non-vacuous; REWRITE `:185-200` and `:202-217` on `FormClosing` (WinForms → web: dropped and named);
  web → WinForms: every web root event exists on WinForms, so its "lost" arm uses an unknown web event (`beforeunload`).
- `FormRetargetTests`: NEW `EveryEventWiredOnlyOnTheSource_IsDroppedAndNamed` (catalog-driven, both directions, FormRoot
  included); `EveryEventWiredOnBothTargets_…` extended to FormRoot.
- `FormRetargetPairTests`: a WinForms form with root Load + `btn` MouseDown → `ConvertToPair(Web)`: the code-behind has
  `Private Sub Login_Load()` and `Private Sub btn_MouseDown(e As DomEvent)` above the region, nothing refused; Integration:
  the pair BUILDS through the CLI and RUNS under node (Load prints); the reverse pair passes csc.
Mutations: crossing on the default event only (the new sweep); the root crossing removed (the Load test); the root
stubs skipped in `ConvertToPair` (the pair build); the source name kept instead of the destination's.
`WiredRunState` back on `DefaultEvent(_from)`: likely EQUIVALENT on today's catalog (every script component wires only
its default) — record it as such, honestly.
RE-CHECK: `FormRootRetargetTests` doc `:9`; `FormRetargetTests` `:484-512` (Task 1), `:581-613`; the Timer BL8027 tests;
the IDE "Retarget Form…" command tests.

### Task 8 — RUN, not compile, on both targets and both build entry points
New Integration `FormEventAcceptanceTests`: a form made through the real document view model and the grid's Events rows
(the `HandlerRequested` route, not a constructed plan): Form Load writes `lbl.Text = "loaded"`; Button Click and
MouseDown; TextBox KeyPress; saved via `SaveAsync`; built by the real CLI **and** by `BuildService.BuildProjectAsync`.
- WinForms (Windows-gated): the driver `Show()`s the form (⚠ `PerformClick` needs it visible and enabled — CLAUDE.md),
  raises Click/MouseDown/KeyPress through the protected `On…` methods by reflection, and prints an ordered log: `Load`
  first, then each handler once, `lbl.Text` = `loaded`.
- Web: node (and Edge where present): `load` first, `lbl` text `loaded` — which fails if Load runs before the controls
  are fetched — then each dispatched handler once; the TextBox KeyPress is driven as `keydown` events carrying `key`
  (`x` and `Enter` → two KeyPress runs, `Shift` → none), never a `keypress` event.
- The KeyPress handler's `e` differs by target (a `KeyPressEventArgs` on WinForms, a `DomEvent` on the page); the shared
  assertion is the COUNT and order, not the character — `KeyChar` mapping is piece 2's (ruling 1).
Mutations: the WinForms `AddHandler Me.Load` dropped (the log); web Load moved before the controls (`lbl` is null);
the IDE route alone skipping the region write (the IDE half red, the CLI half green — the "both entry points" kill).

### Task 9 — Records, mutation ledger, gate (§5), IDE drop, click-through
Execution notes per task in §6 below (as slice 4's §8). `docs/form-designer-followups.md`: **D-2's divergence table,
copied whole** (each row "classic emission diverges; piece 2's library is responsible for WinForms parity"), the
"handler does not fit" code follow-up (D-7), the KeyPress key-set ruling and its `KeyChar` hand-off to piece 2, the
reserved `VgsOn_` prefix, and M7's finding that an Extern class's undeclared member compiles untyped and silently
(`relatedTarget`/`contains` until piece 2's Task 28 declares them).

## 5. Gate for the slice
- **Fast subset** (`--filter "TestCategory!=Integration"`, Release, both streams captured): the **sorted failure NAMES**
  equal Task 0's, base SHA stated. Known machine rows only: `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind` (intermittent), `SearchSnippets_EmptyQuery_ReturnsAll`,
  `SearchSnippets_WhitespaceQuery_ReturnsAll`, `ReadingAnMvidTakesNoLockOnTheFile`. A count alone proves nothing; re-run
  named rows with `--no-build --filter`.
- **Named Integration set**, 0 unexpected skips on Windows: `WinFormsCatalogSweepTests`, `FormEventWebRunTests`,
  `FormEventAcceptanceTests`, `FormDesignerAcceptanceTests`, `FormMenuAcceptanceTests`, `FormComponentAcceptanceTests`,
  `FormBuildEmissionTests`, `FormPropertyBatchAcceptanceTests`, `FormItemsAcceptanceTests`, `FormImageAcceptanceTests`,
  `FormRetargetPairTests`, `WebMainStartupTests`, `JavaScriptProjectBuildTests`, `BuildServicePipelineTests`. A new
  failure: re-run alone, then A/B on a `git worktree add --detach` of `origin/master` (never `git merge-tree`). ⚠ The JS
  web-build rows' `ERROR_USER_MAPPED_FILE` on this machine — compare by NAME.
- `dotnet clean` (Shell) after Task 6's AXAML before the gate build.
- **Merge check:** trial merge of `origin/master` (and of `origin/feat/portable-controls`, to size the piece-2 conflict)
  in detached worktrees. Expect conflicts in `FormControlCatalog.cs` (rows), `FormHandlers.cs` (piece 2 Task 30),
  `RegionWriter.cs`, `DesignDiagnostic.cs` (band table), `JsExecutionTierRosterTests` (the pin), the ADR number.
- **IDE drop** (Windows): `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E` (never `/MIR`); `IDE\lib\js\dom-core.bli`
  still present (unchanged by this slice). Run from `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe`.
- **Owner click-through:**
  1. Select a Button; click the lightning bolt: its events show, Action first; Categorized/A-Z and search work; back to
     Properties and the property rows are as before.
  2. Double-click the empty Click cell: the code opens at `btnX_Click(sender As Object, e As EventArgs)`; F5 — clicking
     the button runs it.
  3. Double-click MouseDown: `e As MouseEventArgs`.
  4. Write `Private Sub AnyClick(sender As Object, e As EventArgs)`; open another Button's Click drop-down: `AnyClick`
     is offered; a `KeyEventArgs` Sub is not offered for Click but is for KeyDown. Pick it — the code is unchanged.
  5. Clear the cell: the wiring goes, the Sub stays. Ctrl+Z restores the wiring.
  6. Type `DoIt` in a cell and Enter: a `DoIt` handler is created and opened.
  7. Double-click the form's background: `<Form>_Load` opens; F5 — Load runs before the window shows. Double-click the
     grey canvas OUTSIDE the form: nothing happens.
  7a. On a web form, wire a TextBox KeyPress that appends to a label: typing letters, Enter, Backspace, Esc each run it;
     Shift and the arrow keys do not. Wire a Panel's Enter: tabbing between two TextBoxes inside the Panel does not run it;
     tabbing in from outside does.
  8. On a WEB form: a Panel shows no Paint; Click creates `pnl_Click(e As DomEvent)` above the region. The Form's Events
     show Load/Resize/Click/KeyDown/KeyUp/KeyPress only. Load writes a label; open the page: the label shows it; resize
     the browser: Resize runs.
  9. Retarget a WinForms form that has a Load and a MouseDown handler to the web: the new pair builds and the page runs
     both; FormClosing is reported as not crossing.

## 6. Execution notes
(Filled per task as slice 4's §8: base SHA, deviations, red evidence, RE-CHECK results, the mutation table.)

### Task 0 — baseline (base `158e1947`, code identical to `2fa64789`)
- Fast subset (`TestCategory!=Integration`, Release, both streams to `scratchpad\pg5-fast-base.txt`): **Total 11083 —
  Passed 11058, Failed 6, Skipped 19** (4 m 11 s). Sorted failure names, all known machine rows:
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind` (intermittent), `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll`. (`ReadingAnMvidTakesNoLockOnTheFile`
  passed this run — it is in §5's list as a known intermittent.)
- **M1/M2 re-measured on the BRANCH CLI through the PROJECT route** (`wt-pg5\BasicLang\bin\Release\net8.0\BasicLang.exe build
  scratchpad\m5p\Site.blproj`, `<TargetBackend>JavaScript</TargetBackend>`, the M1 class verbatim): build succeeded; emitted
  `t0.addEventListener("click", t1)`, `w.addEventListener("resize", t2)`, `this.LoginForm_Load();`, `this.Later();`. Under node
  (`document.body`/`window` as `EventTarget`s): `load`, `later` printed at construction, then `click`, `resize` on dispatch —
  each once. ✅ Both hold on the branch build.

### Task 1 — event lists, `WebWiring`/`WebFilter`, `DomInterfaceOf`, ADR 0021
- ADR `0021-form-event-lists-and-web-wiring.md` written first (+ README index row). ADR number 0021 was free on this branch.
- **Procedure:** the D-1 names/args/web names were written into a scratch generator (`scratchpad\m5gen\gen.js`) that READS the
  oracle for each event's category, description and `argsFullName` (qualifying any namespace other than `System` /
  `System.Windows.Forms`) and emits one `private static IReadOnlyList<FormEventDef> <Kind>Events()` per kind; the parity run then
  judged every pasted event (green first time). Rows point at their method (`Events: LabelEvents()`); `Ev` stays for the three
  single-event kinds (Timer, ErrorProvider, ToolStripSeparator). Dead `StripItemClicked`, `PanelEvents(web)`,
  `SelectedIndexChangedDescription`, `ControlValueChangedDescription` removed.
- **Deviations (each the recommended / VS-matching option, owner-delegated):**
  1. `FormEventCategory.Display` added — the snapshot files DataGridView `CellFormatting` under "Display" (the parity comparer
     says "add one").
  2. **ToolStripMenuItem `DropDownItemClicked` dropped from D-1.** WinForms carries NO description for it — empty on .NET 8 (the
     snapshot) AND on .NET Framework 4.8 (measured: `TypeDescriptor.GetEvents` in Windows PowerShell 5.1) — and
     `EveryRowAndEvent_DeclaresACategoryAndADescription` refuses an empty one; inventing text is what O4 forbids.
  3. BackgroundWorker `ProgressChanged`/`RunWorkerCompleted` descriptions are .NET Framework 4.8's own, MEASURED the same way
     (`Raised when the worker thread indicates that some progress has been made.` / `Raised when the worker has completed
     (either through success, failure, or cancellation).`); one shared exemption text `BackgroundWorkerEventHasNoMetadata`.
  4. GroupBox `Enter`/`Leave` carry `FromOutside` (the invariant: every `focusin`/`focusout`) — previously Enter had no filter.
  5. **`FormRootRetargetTests` `:185-200`/`:202-217` rewritten HERE, not in Task 7:** they went red the moment the Form had
     events (Load crosses through the existing `ConvertRootBinds`). Rewritten on `FormClosing` (WinForms → web) and
     `beforeunload` (web → WinForms), as Task 7 specifies. Task 7 still owns `ARootLoadBind_Crosses_BothWays_…`.
- **Red before:** with the types added and the catalog untouched, `FormEventsTests` = 3 failed / 27 passed:
  `WiredOn_TheFormRoot_IsItsTenEvents_…` (root empty), `KeyPressKeys_…` (no KeyPress anywhere — the non-vacuity floor),
  `FromOutside_…` (GroupBox Enter `focusin` unfiltered). The remaining new invariants are vacuously green on the old catalog and
  are proven by the mutations below.
- **Green:** `(Form|WinFormsCatalog) & !Integration` = 3069 passed, 1 failed (`EveryTextRoute_…`, the known machine row whose
  name contains "Form"), 1 skipped. `WinFormsCatalogParityTests` judged every new event — green. `EveryEventWiredOnBothTargets_…`
  is in the fast set and stayed green (now ~20 events per web kind).
- **RE-CHECK:** `Write_Web_RefusesAnUnknownEvent_NotOnTheRow` → `mouseover` (still finds `'click'`); `ToWeb_ABindOnAnEventTheCatalogCannotName_…`
  → Button `Paint`; `FormComponentEmissionTests`, `FormHandlerPlanTests`, `TheDefaultEvent_IsWinFormsOwn_…` green unchanged.

| Mutation (Edit + rebuild) | Killed by |
|---|---|
| FormRoot Load `WebEvent` null | `WiredOn_TheFormRoot_IsItsTenEvents_…` |
| Label MouseLeave `WebWiring = Window` | `WindowAndAfterInitWiring_AreOnTheFormRootOnly` |
| TextBox KeyPress `WebFilter.None` | `KeyPressKeys_IsExactlyOnTheEventsStoredKeypress_…` |
| `tick` added to the DOM-interface table | `EveryWebEventButTick_IsInTheDomInterfaceTable_AndTickIsNot` |
| `System.ComponentModel.` dropped from TextBox Validating | `EveryArgsTypeOutsideSystemAndWinForms_IsWrittenQualified` (parity alone stays green — last segment) |
| Button MouseDown given `click` (the plan's "MouseClick given click": a second `click` on one row) | `NoRow_HasTwoEventsWithOneWebName` |
| Non-browsable `DoubleClick` added to Button | `EveryWinFormsRowAndEvent_MatchesTheSnapshot(Button)` names it |

  The first five ran as one build and the last two as another; each mutant is killed by a DIFFERENT test, and each failing
  test's message names its own mutant.

### Task 2 — root bind emission and the D-12 wrappers (base `83304779`)
- `RegionWriter`: `CheckRootBinds` is now the web BL8032 refusal for the Form through the seam (`WebEventOf(FormRoot, bind)`),
  sharing ONE message builder with the controls (`UnknownWebEvent`) — comma-joined (`QuotedList`). WinForms root binds are
  emitted `AddHandler Me.<Event>, AddressOf <h>` after the reference rows, immediately before `End Sub`. Web root binds
  (`AppendRootWebBinds`) after the controls: Element → `doc.body.addEventListener`, Window → `w.addEventListener`, AfterInit
  → `Me.<h>()` as the very last line. `Dim w` widened to "script component OR a Window-wired root bind". Every web listener
  (controls and root) goes through ONE `AppendWebListener` that writes `FormEvents.ListenType(evt)` and, for a filtered event,
  `AddressOf VgsOn_<prefix>_<Event>`. The wrappers are built by ONE `Wrapper(...)` switch on `WebFilter`, emitted by
  `AppendWrappers` ABOVE `Private Sub InitializeComponent()` with one comment line naming the reserved `VgsOn_` prefix.
  `CheckHandlerOrdering` adds the root's LISTENER binds and exempts AfterInit and every WRAPPED bind's user handler.
  `FormDocument.Binds` doc updated; the web scaffold carries the D-3 Load comment under `Me.InitializeComponent()`.
- **Decision taken (not in the plan):** two binds on ONE filtered event of one owner share ONE wrapper (it calls each handler
  in document order) and ONE listener — two wrappers of the same name would be a duplicate member, two listeners would run the
  handlers twice. Pinned by `TwoHandlersOnOneFilteredEvent_ShareOneWrapper_AndOneListener`.
- **Deviation:** the tests live in a new fixture `FormEventEmissionTests` (root + D-12 + scaffold comment together) rather
  than split across `FormRootTests`/`FormRegionWriterTests`/`FormScaffolderTests`; `FormRootTests.ARootBind_IsWarned_…` is
  deleted there with a pointer comment (its rewrite is `ARootBind_IsWired_AsTheLastStatement_OnWinForms`).
- **Red before:** 15 of the 18 new tests failed on `83304779` for the right reasons (no emission, BL8028 warning instead of
  BL8032, " and "-joined message, no wrappers, no scaffold comment). The three already green are guards that the change must
  keep true (`APageWithoutWindowBinds_DeclaresNoW_AndNoWrapper`, `AWinFormsKeyPress_IsAPlainAddHandler`,
  `AWebLoadHandler_DeclaredBelow_IsNot` — the last is made non-vacuous by the AfterInit-exemption mutation below).
- **Run, not just compile (scratch probe, nothing committed):** a page written by `RegionWriter` with TextBox KeyPress +
  KeyDown, Panel Enter, Form Load/Resize/KeyPress, built by the branch CLI on the PROJECT route, under node with
  `EventTarget` stubs (`contains` knows the Panel's children): `F_Load` at construction; `a`/`Enter`/`Backspace`/`Escape` →
  KeyPress ×4 and KeyDown for all 7 keys (`Shift`/`ArrowLeft`/`F1` → no KeyPress); Panel `focusin` from outside → Enter ran,
  from a child (relatedTarget inside) → did NOT, `null` → ran; body `keydown` `x` → `F_KeyPress`, `Shift` → nothing; `resize` →
  `F_Resize`. Task 4 turns this into the committed node tier.
- **RE-CHECK:** every `FormRegionWriterTests` golden, `FormComponentEmissionTests` (the Timer `w` line), `FormScaffolderTests`
  web goldens, `FormRootTests` `:183`/`:249` — all green in the `(Form|WinFormsCatalog) & !Integration` run (3085 passed, 1
  failed = `EveryTextRoute_…`, the known machine row). `FormBuildEmissionTests`: see the gate below.

| Mutation (Edit + rebuild) | Killed by |
|---|---|
| A listener emits `evt.WebEvent` instead of `ListenType` (KeyPress listens to `keypress`) | `AWebKeyPressBind_…`, `AWebRootKeyPress_WrapsOnTheBody` |
| `FromOutside` wrapper's test replaced by `If True` (relatedTarget dropped) | `AWebPanelEnterAndLeave_…` |
| `Dim w` condition back to script components only | `AWebRootResizeBind_ListensOnTheWindow_AndDeclaresW` |
| Root BL8032 check disabled on the web (guard inverted) | `AWebRootBind_OnShown_IsRefusedAsBL8032_NamingTheForm` (+ the WinForms FormClosing row, refused) |
| AfterInit no longer exempt from BL8013 | `AWebLoadHandler_DeclaredBelow_IsNot` |
| Root web binds (Load included) emitted BEFORE the controls | `TheWebLoad_IsAMeQualifiedCall_…`, `AWebRootKeyBind_ListensOnTheBody` |
| Wrappers emitted BELOW `InitializeComponent` | `AWebKeyPressBind_…` (position) |
| BL8013 skips root listeners | `AWebRootKeyHandler_DeclaredBelowTheRegion_IsBL8013` |
| A second bind on a filtered event emits its own listener | `TwoHandlersOnOneFilteredEvent_…` |
| WinForms `AddHandler Me.…` before the reference rows | `ARootBind_IsWired_AsTheLastStatement_OnWinForms` |
| `Me.` dropped from the Load call | `TheWebLoad_IsAMeQualifiedCall_…` |
| Wrapped handlers no longer exempt from BL8013 | `AWrappedHandlerBelowTheRegion_IsNotBL8013_…` |

  Three builds (5 + 5 + 2 mutants); each mutant's expected test failed, and no test was claimed by two mutants of one build
  except where listed together on one row.

### Gate after Task 2 (working tree = Task 2 on `83304779`)
- **Fast subset:** Total 11112 — Passed 11087, Failed 6, Skipped 19. Sorted failure NAMES identical to Task 0's six
  (`Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll`); +29 tests are the new ones.
- **Integration `(Form|WinFormsCatalog)`:** 282 — 281 passed, 1 failed, 0 skipped (23 m 29 s), including
  `WinFormsCatalogSweepTests` (csc), `FormBuildEmissionTests`, `FormDesignerAcceptanceTests`, `FormComponentAcceptanceTests`,
  `FormMenuAcceptanceTests`, `FormRetargetPairTests`. The one failure, `CppDoubleFormattingTests.Expected_IsWhatDotNetPrints`,
  is caught by the filter only because "Formatting" contains "Form": the emitted C# printed `∞` where the test expects
  `Infinity` (a machine-culture symbol), in code this slice does not touch. Not A/B'd against master — recorded as
  unrelated by construction, to be confirmed at the slice gate.

### Task 2 review fixes (base `0805ccfa`; coordinator rulings under the owner's delegation)
1. **KeyDown before KeyPress.** Both listen to `keydown` on one target, and the DOM runs them in the order they were ADDED —
   document order before this fix, so `keypress` stored first ran KeyPress first (reviewer reproduced under node). ONE rule,
   `RegionWriter.WebRaiseOrder`: per owner, every plain listener before every wrapper's, each run in document order (a stable
   sort); used by the control and the root emission. Different DOM types are ordered by the browser, so this is the only
   ordering the emitter controls. Tests: `KeyDownsListener_PrecedesTheKeyPressWrapper_…` (text) and
   `FormEventWebRunTests.KeyDown_RunsBeforeKeyPress_WhenTheBindsAreStoredInReverse` (CLI build + node: prints
   `txt_KeyDown, txt_KeyPress, KeyForm_KeyDown, KeyForm_KeyPress`).
2. **A control named like the form is refused — BL8017** (the duplicate-id code fits: an Id is a member of the form's class).
   `FormDocument.ControlsNamedLikeTheForm()` (OrdinalIgnoreCase, as BasicLang names) + ONE message, asked by the READER's
   duplicate-id check AND by the region writer (a model reaches the writer without a reader: designer edits, retarget, tests).
   No new code claimed; BL8037 stays piece 2's. Tests on both targets, both routes. ⚠ RE-CHECK found one incidental fixture:
   `FormDocumentRoundTripTests.Write_StillAddsAPropertyWhoseValueIsNotTheDefault` read a nameless form from `b.blwebform`
   (the name defaults to the file's) with a control `b` — now refused; renamed the file to `Page.blwebform`, with a comment.
3. **Code points.** Measured on the branch CLI + node: `::Array.from(k).length` emits `Array.from(k)` and gives 1 for
   U+1F600 (built with `String.fromCodePoint`), 5 for `Enter`, 2 for `F1`; `Char.IsHighSurrogate` and `ChrW(…) & ChrW(…)` do
   not type-check on this backend. The wrapper is now `Dim k As String = e.key` / `Dim n As Integer = ::Array.from(k).length`
   / `If n = 1 OrElse …`. Test: `AnAstralCharacterKey_RunsKeyPressOnce_AndANamedKeyDoesNot` (U+1F600 from its code point →
   KeyPress once; `F1` → never).
4. ADR 0021: the Load-throws row added to the divergence table (also D-2's table here); §3 and D-12 say "one generated Sub per
   (owner, filtered event)"; §2's KeyPress row states the code-point rule and the listener order.
5. Catalog: `KeyEvents(web)` / `FocusEvents(web)` factories replace 27 pasted runs (9 web Key, 10 web Focus, 1 + 7 WinForms-
   only); the event-list methods became collection expressions so the factories spread in place. Parity and every
   `FormEventsTests` invariant green unchanged.
- `FormEventWebRunTests` is created HERE (Task 4 extends it) and added to `JsExecutionTierRosterTests` (pin 103 → 104).
- **Red before:** 9 failed for the right reasons (the reverse-order run printed KeyPress first; nothing refused the `F`
  control on either route; the emoji never reached KeyPress; the old wrapper text).

| Mutation | Killed by |
|---|---|
| `WebRaiseOrder` returns document order | the text test + the node run (print order) |
| Name match `Ordinal` instead of ignoring case | `(Web,"f")` + the reader's `.blwebform` row |
| KeyPress filter back to `k.Length = 1` (alone in its build) | the node emoji run + the exact wrapper text |
| `KeyEvents(web)` KeyPress without its filter | `KeyPressKeys_IsExactlyOnTheEventsStoredKeypress_…` |
| Region writer's named-like-the-form check removed | the three `AControlNamedLikeTheForm_…` rows |
| Reader's check removed | both `TheReader_RefusesAControlNamedLikeTheForm` rows |

  Gate on the fix (`--no-build` of the fix build): fast subset 11119 — 11095 passed, 5 failed, 19 skipped; failure names
  ⊂ Task 0's (`Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, the intermittent one, passed). Committed `3df80418`.

### Task 3 — owners, the shared scanner, fitting (base `3df80418`)
- New `FormBindOwner` (D-10: `Definition` = the control's row or `FormRoot`, `Binds`, `Prefix`, `Label`, `KindName`), new
  `FormCodeScan` (D-11: modifiers, a parameter list continued over lines or with `_`, `'` ends a line outside a string,
  OrdinalIgnoreCase, Subs inside ANY designer region skipped, Functions/`End Sub`/lambdas never declarations).
  `FormHandlers`: `Shape(owner, evt, target, style)` → `FormHandlerShape(Parameters, Placement)` is the only signature and
  placement site; `Insert` writes `shape.ParameterList` on `shape.Placement`'s side; `Fits` reads `Shape` parameter by
  parameter; `FittingHandlers` (document order, never `New`/`InitializeComponent`/a Function/region Subs); `Plan(form, owner,
  evt, code, handlerName?)`; `PlanDefault` (control overload delegates to a new owner overload — the Form's Load);
  `PlanBind(form, owner, …)` (control overload delegates); `EnsureBind(owner, …)` (control overload delegates); `Unbind`.
  Both `FindDeclarationLine` copies deleted — `RegionWriter`'s BL8013 asks `FormCodeScan`. `FormEvents.ArgsBases` filled (13
  derived types).
- **Decisions taken:** (a) an untyped or `Object` second parameter FITS a WinForms event — csc accepts `H(object, object)` by
  parameter contravariance, and BasicLang emits an untyped parameter as `object`; D-4's text listed only `EventArgs`/`A`/bases
  (no csc cell exercises `object`; the rule follows the language). (b) A navigated plan returns the user's own SPELLING as
  `Handler` (`btn_click`), so a bind written from it names the real Sub. (c) `ArgsBases` lists only bases that are catalog
  args types or `CancelEventArgs`.
- **TDD shape — honest:** the production code was written BEFORE its tests (while the review-fix gate was running), so there
  is no red-before run on `3df80418`; the evidence is the mutation table below, every row killed. The M6 case pin was shown
  red by the scanner-`Ordinal` mutant.
- `FormHandlerFitCscTests` (fast, in-process Roslyn): 35 kinds incl. the Form, one compile each, ~36 args types × every
  WinForms event; `Fits` ⇔ csc in both directions on the FIRST run (the `ArgsBases` table was right as written). csc total
  1.9 s (the first compile 1.6 s of it, warm-up) — no split needed.
- **RE-CHECK:** `FormHandlerPlanTests`, `FormHandlerGestureTests`, `FormHandlerReachabilityTests`, `FormCanvasDoubleClickTests`,
  `FormDesignerCommandTests`, `FormComponentEmissionTests`, the BL8013 rows of `FormRegionWriterTests` — all green;
  `(Form|WinFormsCatalog) & !Integration` = 4194 passed, 1 failed (`EveryTextRoute_…`, the known machine row), 1 skipped.

| Mutation (Edit + rebuild) | Killed by |
|---|---|
| `Insert` writes its own `(sender As Object, e As EventArgs)` literal | `ThePlanner_SpellsNoSignatureLiteral`, 183 shape-sweep cells, the web stub/gesture rows |
| `FindSub` back to `Ordinal` | `AHandWrittenHandler_InAnotherCase_IsNavigatedTo_NeverDuplicated` |
| `()` accepted for any web event | 181 `OnTheWeb_AListenerTakesExactlyADomEvent_…` cells |
| `ArgsBases` loses `FormClosingEventArgs ⊃ CancelEventArgs` | `Fits_AgreesWithCsc_…(Form)` + `AKeyEventArgsHandler_…` |
| `PlanDefault` plans with the args stripped (a second, args-blind signature site) | `AWinFormsPanel_OpensPaint_WithItsPaintEventArgs_AndNoNotice` |
| The scanner no longer skips the designer regions | `TheScanner_ReadsModifiers_…` (InitializeComponent returned) |
| Fit by exact name only (no `EventArgs`/bases) | 32 csc kinds, 165 WinForms fit cells, `FittingHandlers_…` |
| The scanner keeps comments | ⚠ first SURVIVED `AMidLineComment_…` (the anchored declaration regex already refuses `Dim x … ' Sub btn_Click`) — VACUOUS for this mutant, so `ACommentInsideAContinuedParameterList_IsNotPartOfIt` was added; it kills it. The mid-line test stays: it pins the anchoring |
| BL8013's lookup `Ordinal` | new `BL8013_FindsAHandlerDeclaredInAnotherCase` |

### Task 4 — every event through csc and through a running page (base `e7662369`; Integration)
- `WinFormsCatalogSweepTests.EveryEvent_OfEveryControl_WiresIntoCSharpThatCscAccepts` — every WinForms kind AND the Form
  (`EveryWinFormsControlAndTheForm`): every WinForms event planned through `FormHandlers.Plan` (the stub `Shape` writes),
  `EnsureBind`, the region writer, the real CLI to C#, csc. ONE compile per kind. `TheDefaultEvent_…` kept. All 35 green
  first run.
- `FormEventWebRunTests` (created by the review-fix commit) gains: `EveryWebEvent_OfTheKind_RunsItsHandlerOnce_AndTheFiltersHold`
  (TestCaseSource over every web ELEMENT kind with a web event — 22; components excluded, a Timer's tick is no dispatched
  event and an interval would keep node alive), `TheFormsWebEvents_Run_AndLoadRunsOnceAtConstruction`, and the Edge tier
  `InEdge_TheRealInterfaces_RunTheHandlers_AndAChildToChildFocusMoveRaisesNoEnter`. Every page is planned through the
  designer's own `Plan` + `EnsureBind` + `RegionWriter` (`PlannedCode` fills each stub body with a print), built by the CLI on
  the project route, and run.
  - node: each DOM type `ListenType` names dispatched once (with `key` and an outside `relatedTarget`) → every handler once;
    KeyPress ×3 for Enter/Backspace/Escape and ×0 for Shift/ArrowLeft/F1 while KeyDown ×6; a child→child focus move → no
    Enter/Leave; `null` relatedTarget → each once. Form: Load once BEFORE the first dispatch; Resize on `window`;
    Click/KeyDown/KeyPress/KeyUp on `body`, KeyDown before KeyPress.
  - Edge (ran on this machine — 0 skipped): one Canvas page; every dispatch is `new (DomInterfaceOf(webEvent))(ListenType(evt),
    …)` — the gates-only table's first reader; KeyPress sent as `KeyboardEvent("keydown")` incl. U+1F600; focus moved with
    REAL `.focus()`: outside → Panel child raises Enter once (the positive control — focus events do fire in headless Edge),
    child → child raises none. Served from `WebPreviewServer`, throw-away profile (`EdgeLayoutHarness.WithThrowawayProfile`).
  - ⚠ Deviation: the plan's `EdgeStep.Dispatch` on the layout harness was NOT added — the layout harness measures rectangles
    per iframe case; an event log needs a console capture before the page's module and a driver after it, so the tier has
    its own small `RunInEdge` reusing the harness's Edge path, profile and server.
- `JsExecutionTierRosterTests` already counts the fixture (fix commit, pin 104).
- **Red before:** none observable — the emitter was complete (Task 2 + fixes) before these tiers existed; all 62 passed on
  their first run. Their value is shown by the mutations, each killed by a RUN, not a string.

| Mutation (Edit + rebuild) | Killed by |
|---|---|
| The web emitter writes the WinForms spelling (`addEventListener("Click", …)`) | 26 of 27 `FormEventWebRunTests` (every run) |
| `Me.` dropped from the Load call | ⚠ **EQUIVALENT at run time on this branch**: measured — the JS backend now emits `this.LoginForm_Load()` for an UNQUALIFIED self-call (the CLAUDE.md row "an unqualified call … is a runtime ReferenceError" is stale here; master #57). Still killed by the fast text pin `TheWebLoad_IsAMeQualifiedCall_…` (Task 2) |
| Load emitted as a `load` listener on the body | `TheFormsWebEvents_Run_…` and the Edge test — the CLI build REFUSES it (a parameterless Sub cannot be an `Action(Of DomEvent)`) |
| The Window bind emitted on `doc.body` | `TheFormsWebEvents_Run_…` (resize never runs) |
| `Backspace` dropped from the key set | the 9 KeyPress kinds' key counts |
| The relatedTarget test dropped | 11 kinds' child→child step AND the Edge real-focus step |
| `System.ComponentModel.` dropped from TextBox Validating | `EveryEvent_OfEveryControl_…(TextBox)`: CS0246 + CS0123 |

  Four node builds (2 + 2 + 2 + 1 mutants), the kills of each pair disjoint by test (and by message where a test was shared).

### Correction to review fix 2, found by the Task 4 gate (base `e7662369` + Task 4)
- The Task 4 Integration run failed FIVE image-copy acceptance rows (`Cli_/Ide_…CopiesTheImage…`, `Ide_WhenTheProjectFileCannotBeLoaded_…`):
  their fixture is a form `Pic` holding a control `pic`, and fix 2's case-INSENSITIVE match refused it (BL8017) — a document
  that builds and runs on both targets (C# is case-sensitive; BasicLang accepts a member named like its class in another
  case). The ruling's stated basis is CS0542, which is exact-case.
- Corrected (the recommended reading of the ruling, recorded as a deviation): `ControlsNamedLikeTheForm` matches EXACTLY
  (Ordinal) — the CS0542 case — on both routes; the one real case-variant hazard, two generated wrappers whose names differ only
  in case (`VgsOn_f_KeyPress` / `VgsOn_F_KeyPress` — one member to BasicLang), is refused (BL8017) by a new
  `RegionWriter.CheckWrapperNames` over the wrappers the page will really get. Tests: `(Web,"f")` still refused (both wrap
  KeyPress); new `ACaseVariantId_WithNoCollidingWrapper_IsAccepted_ByTheReaderAndTheWriter` (both targets); the reader row now
  uses an exact `F`. Mutations: `CheckWrapperNames` removed → `(Web,"f")` red; the match back to OrdinalIgnoreCase → the new
  case-variant rows red (and the five acceptance rows, measured). The five rows re-run by name: green. Committed `8997278e`.

### Gate after Task 4 (fresh build of Task 4 on `e7662369`, before the correction above)
- **Fast subset:** Total 12221 — Passed 12197, Failed 5, Skipped 19. Sorted names ⊂ Task 0's six:
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`, `SearchSnippets_EmptyQuery_ReturnsAll`,
  `SearchSnippets_WhitespaceQuery_ReturnsAll` (the intermittent `Emit_ReplacingAnImportedModule_…` passed).
- **Integration `(Form|WinFormsCatalog)`:** 344 — 338 passed, 6 failed, 0 skipped (25 m 50 s). Five were the image-copy rows
  fixed by `8997278e` (re-run by name: green); the sixth is `CppDoubleFormattingTests.Expected_IsWhatDotNetPrints` (`∞`
  vs `Infinity`, machine culture; caught by "Formatting" containing "Form"), as at the Task 2 gate. The Edge tier RAN (not
  skipped).

### Review round 2 (of `3df80418`/`8997278e`/`e7662369`/`a6a7dcae`) — fixes (base `a6a7dcae`)
1. `FormCodeScan` walks the block structure (`Class/Structure/Interface/Module/Enum/Namespace … End`) and returns only
   Subs that are DIRECT members of the form class (named after the form, else the first top-level class; a namespace is
   transparent); skips `#If False`/`#If 0` blocks (their `#Else` live; any other `#If` read as live on every branch — full
   evaluation is a FOLLOW-UP for piece 2's `ProcessForEditor`); strips same-line attributes; skips `(Of T)`; follows a
   `_` continuation after any whitespace (a tab too), before the list as well as inside it. Every caller passes `form.Name`.
2. `FormCodeParameter.IsByRef`; a ByRef parameter never fits (CS0123).
3. ONE style read: `FormHandlers.StyleOf(form)` (Classic today; piece 2 replaces its body), asked only by `Shape`; the
   `style` parameter was REMOVED from `Shape`/`Fits`/`FittingHandlers`, so no caller can pass a different one (the plan
   threads it nowhere because nothing can disagree). `FormHandlerShape.Call(handler)` is the call shape — the wrapper's
   `Me.<h>(e)` and Load's `Me.<h>()` now come from it (`RegionWriter` spells no handler call; a structural test pins it).
4. `PlanHandler`: a Sub matching only ignoring case and bound by ANOTHER owner is a conflict → `<name>_1`, `_2`… (VS); an
   exact match, or a case variant no other owner binds, is navigated as before. The wrapper-collision message names
   "the form 'X'" only when the form is involved; two controls `f`/`F` are told to "Rename one".
5. `Unbind` (and `EnsureBind`'s "already bound" test) ignore reserved data-binding binds. Byte-exact Unbind test: the saved
   document equals the original minus that `<Bind>` line; the code-behind loses exactly the wrapper comment, the wrapper's
   unique lines and the listener, and changes only the region marker (its hash). ⚠ Found while writing it: ANY document
   change re-serialises every empty element as ` />` (XmlWriter) — pre-existing, unrelated; the fixture is written canonically.
6. Wrapper locals `vgsKey`/`vgsLen`/`vgsComposing`; KeyPress skips `isComposing`. MEASURED: `Not e.isComposing` is refused
   ("Logical NOT requires Boolean operand" — an undeclared member is Object) and `As Boolean = e.isComposing` too;
   `Dim vgsComposing As Boolean = ::Boolean(e.isComposing)` compiles to `Boolean(e.isComposing)` (absent → false). IME
   committed-text gap added to ADR 0021's divergence table. Node test with a TextBox named `k` and one named `n`.
7. `WrapperOwners` is the ONE list; `AppendWrappers` and `CheckWrapperNames` both consume it.
8. ADR 0021 §3/§4 updated (exact-name rule + wrapper-collision rule; `Call`; `StyleOf`; ByRef; the unqualified-self-call
   ReferenceError claim REMOVED). MEASURED on the branch CLI 2026-10-04: an unqualified self-call emits `this.X()` and runs;
   `Me.` inside a lambda COMPILES (`f = () => { this.Check(null); }`). ⚠ CLAUDE.md was NOT edited: the instruction came
   from an agent message, and a CLAUDE.md change needs the owner's own request — the measured text is handed to the owner.
- **TDD shape — honest:** the implementation was written before the red run; evidence = mutations (11), all killed:
  nested-class guard loosened → nested test; attributes not stripped → attribute test; ByRef ignored → ByRef test; Unbind
  removes reserved → reserved test; `#If False` not dead → `#If` test; `(Of T)` not skipped → generic test; wrapper call
  spelled `Me.{handler}(e)` in RegionWriter → `EveryCallSite_AsksTheOneRule`; collision suffix off → both-target collision
  test; form wording always → the two-controls message test; `isComposing` guard dropped → node IME test + Unbind exact
  text; continuation `" _"` only → ⚠ first SURVIVED (the test checked types only); the Tabbed assertion now checks names
  and kills it. Committed `cfb94a29`.

### Task 5 — the grid's Events mode, view model (base `cfb94a29`)
- New `FormEventRow : IFormDisplayRow` (`Name` = the WinForms event, `Category` = its `FormEventCategory`, `Description`,
  `EventName` in the target's vocabulary, `Handler`, `Choices` = `FormHandlers.FittingHandlers` — the SAME instance until its
  items change — `Commit(text)`, `RequestHandler()`, `Refusal`) and `FormHandlerRequest(Owner, Event, Handler?)`.
  `FormPropertyGridViewModel`: `[ObservableProperty] IsEventsMode` (+ `IsPropertiesMode`), `EventRows` rebuilt from
  `FormEvents.WiredOn(definition, target)` with every selection (the Form's with nothing selected), a SECOND
  `FormPropertyDisplayList` (own collapse memory) behind the mode-aware `DisplayItems`, `CodeBehindText` (pushed by the host;
  setting it refreshes every row's choices), `HandlerRequested`, and an events-aware description pane.
- Commit rules: empty → `Unbind` (never the code); a Sub that exists and FITS → bound (its own spelling) in the target's
  vocabulary, ONE `Edited`, code untouched; a new legal name → `HandlerRequested` (the host writes the stub and binds — Task 6);
  illegal / `VgsOn_` / an existing Sub that does not fit → `Refusal` shown in the description pane, nothing written.
- **Scope note:** the coordinator's message described Task 5 as "the Events tab UI … real-view tests with real clicks at two
  sizes, AXAML binding checks". In this pre-flight those are TASK 6 (view, gestures, the real-view rig); Task 5 is the view
  model with fast tests. Task 5 was executed AS WRITTEN; nothing in the AXAML changed yet, so there was no `dotnet clean`.
- **TDD shape — honest:** tests and implementation went into the same first build (no separate red run); evidence =
  mutations below, every one killed.
- RE-CHECK: `FormPropertyGrid*`, `FormObjectSelector*`, `FormDesigner*` fast + Integration (448) green.

| Mutation (Edit + rebuild) | Killed by |
|---|---|
| Rows from `definition.Events` instead of the seam | 19 `TheEventRows_AreExactlyTheSeams_…` cells, `AWebPanel_ShowsNoPaint_…`, the Form rows |
| The WinForms name stored on the web | `PickingAFittingHandler_…(Web)` |
| A new `Choices` instance per read | `Choices_IsTheSameInstance_…` |
| One display list for both modes | `CollapseMemory_IsPerMode` |
| The mode reset by a rebuild | `TheMode_SurvivesASelectionChange_…` |
| "Unbind also edits the code" | not applicable in the VM: the grid only READS `CodeBehindText` (no write path exists); Task 6's real-view (e) checks the disk |

### Gate after Task 5 (fresh build: round-2 fixes + Task 5 on `cfb94a29`)
- **Fast subset:** 12309 — 12285 passed, 5 failed, 19 skipped; names ⊂ Task 0's (`Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
  `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll`).
- **Integration `(Form|WinFormsCatalog)`:** 345 — 344 passed, 1 failed (`CppDoubleFormattingTests.Expected_IsWhatDotNetPrints`,
  the standing machine-culture row matched by "Formatting"), 0 skipped (Edge ran). Committed `b5a5022d`.

### Review round 3 (of `cfb94a29`/`b5a5022d`) — fixes (base `b5a5022d`)
- **Red FIRST this time:** `FormHandlerReview3Tests` (23) written and run on `b5a5022d` + a `ScanCount` stub: **20 failed** for
  the right reasons; the 3 already green are pins (`#End If` two-word, which the old regex matched; `_2` after a taken
  `_1`; EnsureBind's reserved-binding guard — the spec reviewer's surviving mutant, now pinned).
1. **CRITICAL — BasicLang's directive table, read from the compiler:** `BasicLangLexer.ScanDirective` (#If, #ElseIf, #Else,
   **#EndIf** one word, #Define, #Undef/#Undefine, #Include, #Const, #Region, #End Region) and `Preprocessor.Process`
   (#IfDef, #IfNDef, #Else, #EndIf). `#EndIf` and VB's `#End If` both close; `#IfDef`/`#IfNDef` are pushed LIVE so their
   own `#Else`/`#EndIf` never pop an enclosing `#If False`; `#End Region` is not an `#EndIf`.
2. Only a COMPUTED name (`<Prefix>_<Event>`, no bind, nothing typed) is suffixed; a name from a bind (`Plan`) or `PlanBind`
   (the retarget) is navigated (ignoring case) and never renamed. A computed name another owner binds is taken even when
   its Sub is not written yet. `OtherOwnersHandlers` is computed once per plan.
3. `FormHandlers.DescribeUnusableHandler` — ONE answer for the Events tab and (Task 6) the host: illegal identifier; the
   constructor (`New`, checked before the keyword test — it IS a keyword); a keyword (new `Lexer.IsKeyword`, the lexer's own
   table); `VgsOn_`; `InitializeComponent`; a control Id or the form name; another member (`FormCodeScanResult.OtherMembers`:
   Function/Property/Event/Dim/Const); an existing Sub that is Shared ("Shared"), takes a ByRef parameter ("ByRef") or does
   not fit. `New`/`InitializeComponent`/Shared Subs never appear in the drop-down. ⚠ A Task-5 test used a handler named
   `Shared` — a keyword; renamed `Common`.
   - **Shared — measured, decided:** `Me.H(e)` on a Shared Sub emits `P.H(e)` and RUNS on JavaScript, but `AddressOf H` (a
     listener) emits a bare `H` → `ReferenceError` (node, branch CLI). Shared Subs are therefore never offered and are
     refused, on both targets (one rule; the designer's wiring is instance-shaped).
4. Tests: `_2` when `_1` exists; a colliding bind whose Sub is missing; EnsureBind ignoring a reserved data binding.
5. ONE scan per refresh: `FormCodeScan.Scan` → `FormCodeScanResult(Subs, OtherMembers)`; the grid caches it per
   (text, form) and every row reads it; `FormCodeScan.ScanCount` lets a test pin "pushing the code-behind scans once".
6. Minors: `Distinct` in `FittingHandlers`; logical lines — a trailing ` _` (any whitespace) joins the next line before
   matching, so `Sub G(Of T) _` and a split `Partial Public Class _ / LoginForm` header read; retyping the bound name in another
   case is a no-op (no Edited); `EventName` throws instead of falling back to the WinForms name; ByRef/Shared reasons named;
   a selection with no events says so.

| Mutation | Killed by |
|---|---|
| `#EndIf` not recognised | both `#EndIf`/`#endif` rows (+ the nested test) |
| `#IfDef` not pushed (alone in its build) | `AnIfDefNestedInIfFalse_DoesNotCloseIt` |
| Every name treated as computed | both bind-named rows + the retarget pair row |
| EnsureBind counts reserved binds | `EnsureBind_IgnoresAReservedDataBindingOnTheSameEvent` |
| `Distinct` removed | `ASubInBothBranches_IsOfferedOnce` |
| Logical-line joining removed | `AGenericSubContinuedAfterOfT_AndAClassHeaderSplitWithUnderscore_AreRead` |
| Keyword check removed | the `Class` row |
| A scan per row read (cache bypassed) | `PushingTheCodeBehind_ScansItOnce_ForAllRows` |
| Retype compared Ordinal | `RetypingTheBoundNameInAnotherCase_ChangesNothing` |
| Shared not excluded/refused | the `Stat` row |
| No-events text removed | `ASelectionWithNoEvents_SaysSo` |
| Collision only when the Sub exists | `AComputedName_BoundByAnotherOwner_WhoseSubIsMissing_…` |
| Suffix ignores existing Subs | `TheSuffix_SkipsATaken_1` |

## 7. Tests to re-check (consolidated)

| Test | Why | Task |
|---|---|---|
| `FormEventsTests.WiredOn_TheFormRoot_IsEmpty_…` (`:150-158`) | the Form gains events | 1 (rewrite) |
| `FormEventsTests` `:125-136`, `:166-183`, `:189-203`, `:209-220`, `:259-270` | exact event lists | 1 (rewrite, keep the point) |
| `FormEventsTests.NoRow_HasTwoEventsWithOneWebName` (`:272-282`) | must include FormRoot | 1 |
| `FormRegionWriterTests.Write_Web_RefusesAnUnknownEvent_NotOnTheRow` (`:1040`) | `mouseenter` is now declared | 1 (→ `mouseover`) |
| `FormRetargetTests.ToWeb_ABindOnAnEventTheCatalogCannotName_…` (`:484-512`) | MouseEnter now crosses | 1 (→ Paint) |
| `FormRetargetTests.EveryEventWiredOnBothTargets_…` (`:581-613`) | grows; FormRoot added | 1, 7 |
| `FormRootTests.ARootBind_IsWarned_NotEmitted_…` (`:366-382`) | replaced by emission | 2 (rewrite) |
| `FormRootTests` `:183`, `:249` | root-bind fixtures | 2 |
| every `FormRegionWriterTests` golden; `FormComponentEmissionTests` (`:204`, the `w` line) | must stay byte-identical | 2 |
| `FormScaffolderTests` web goldens | the Load comment | 2 |
| `FormHandlerPlanTests`, `FormHandlerGestureTests`, `FormHandlerReachabilityTests`, `FormDesignerCommandTests` | FormHandlers restructured | 3 |
| BL8013 tests in `FormRegionWriterTests` | through `FormCodeScan` | 3 |
| `FormCanvasDoubleClickTests.DoubleClickingEmptyFormBackgroundOpensNothing` (`:110`) | D-9 | 6 (rewrite) |
| `FormPropertyGridViewTests.TwoGridViewsInOneWindow_…`, `TheToolbar…AutomationName`, `EveryBindingInTheGridView_…`, `TheDocumentView_NoLongerCarriesTheGridsOwnBindings` | new radios/template | 6 |
| `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…` | the handler combo | 6 |
| `FormRootRetargetTests` `:185-200`, `:202-217` | Load now crosses | 7 (rewrite on FormClosing) |
| `JsExecutionTierRosterTests.RosterIsPinned` | +1 fixture (+1 more if `FormEventAcceptanceTests` runs node in-fixture) | 4, 8 |
| `WinFormsCatalogParityTests` (fast) | judges every new event | 1 |

## 8. Plan-text errors found (each corrected above)
1. CARRIED note 1: `ConvertBinds` already crosses through `WiredOn` (slice-3 B1); only `WiredRunState` reads the default.
2. `ConvertToPair` stubs only controls and components (`FormRetarget.cs:135-150`) — a crossed root Load would wire a
   Sub the pair never declares. Not in the plan; Task 7.
3. "Signature does not change" is true of `WiredOn` only — `FormEventDef` must grow (`WebWiring`): Load is a call and
   Resize listens on the window.
4. 5.4's "MouseEventArgs only mouse events" — fitting is a TYPE rule (MouseClick is Action; NodeMouseClick derives).
5. Two mirrored, case-SENSITIVE handler scanners in a case-insensitive language (`FormHandlers.cs:285-311`,
   `RegionWriter.cs:620-645`) — a latent duplicate-Sub defect.
6. Six `FormEventsTests` rows, not one, pin exact lists; two `FormRootRetargetTests` rows (not one) break; the BL8032
   `mouseenter` case and the retarget MouseEnter case break.
7. Spec §5's `RegionWriter.cs:330-409`/`:418-432` cites are stale (`:463-504`, `:537-552`).
8. Parity is a FAST-subset fixture, not Integration (the slice-4 pre-flight said otherwise).
9. "Click, KeyDown…" on the web Form mean `body` listeners that see bubbled events — a divergence the plan does not
   mention (D-3).
10. Stale doc comments: `Ev` (`FormControlCatalog.cs:1869-1873`), BL8026/BL8032 (`DesignDiagnostic.cs`),
    `FormDocument.Binds` (`:103-108`).

## 9. Plan review of `dabbdb90` — dispositions (this revision; coordinator rulings, owner delegated)

| # | Item | Disposition |
|---|---|---|
| 1 | KeyPress: stored `keypress`, emitted filtered `keydown`; where the filter lives; measure first | Ruling recorded in D-2 and ADR 0021 (Task 1). Generated wrapper Sub in the init region, above `InitializeComponent` (D-12); `dom-core.bli` rejected (declarations only, emits nothing). MEASURED on the branch CLI (M7). D-3, D-8, Tasks 2/4/8 updated; "passes either way" deleted |
| 2 | Enter/Leave filtered to relatedTarget outside, same mechanism | D-2 + D-12 (`FromOutside`); Task 4 asserts child→child does NOT raise the Panel's Enter/Leave (node + a real `.focus()` in Edge); measured in M7 |
| 3 | Divergence table | Added to D-2 (seven rows, each "classic diverges; piece 2 responsible"); copied to `docs/form-designer-followups.md` in Task 9 |
| 4 | One parameterised signature rule | D-4: `FormHandlers.Shape(owner, evt, target, style)` + `FormCodeStyle.Classic`; `Fits`/AfterInit are CLASSIC rules; piece 2 adds `Portable` as an arm; ADR 0021; Task 3 `FormHandlerShapeTests` |
| 5 | Freshness from the unsaved tab | D-5 note; Task 6 real-view (i) + the refresh-removed mutation |
| 6 | D-1 reason | Rests on WinForms selectability; the tabindex fact (`FormAssetEmitter.cs:315-318`) recorded |
| 7 | `.vgs-form` vs `body` | D-3's losing alternative (the Docked strips sit outside `.vgs-form`; no id) |
| 8 | D-9 IN, surface only | "Optional" removed; `SurfaceSize` bounds; two canvas tests (surface / outside), more than one zoom |
| 9 | Explicit DOM-interface table, no `tick`, gates-only | D-8 item 3 lists it; Task 1 invariant + a test that `RegionWriter` never reads it |
| 10 | `APanelKind_…` list | Task 1: catalog-driven `TestCaseSource` (default event Paint) with a non-empty floor |
| 11 | Record the key-set ruling | D-2 row, ADR 0021, Task 9's followups entry |
