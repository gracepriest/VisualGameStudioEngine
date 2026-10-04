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

Enter/Leave are left OFF Label, PictureBox, ProgressBar and LinkLabel: the snapshot lists them, but a `<label>`/`<img>`/
`<progress>`/an `<a>` without `href` never receives focus, so a web bind would register and never run — and on WinForms
those four are not selectable either.

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
| KeyPress | `keypress` | the DOM's character-producing key event (deprecated in the spec text, supported by every engine). See D-8 for the reconciliation with piece 2's "a character-producing keydown" |
| Enter / Leave | `focusin` / `focusout` | bubbling, so a container's Enter fires for its children as WinForms' does — the GroupBox rule (slice-3 O3) applied everywhere, never `focus` on some kinds and `focusin` on others |
| existing defaults | unchanged (`input`, `change`, `click`, `tick`) | |
| MouseClick, MouseDoubleClick, MouseHover, Validating/Validated, Paint, Resize (on a control), every `…Changed` that is not a default, every kind-specific event above marked (W) | none | `NoRow_HasTwoEventsWithOneWebName` (`FormEventsTests.cs:272-282`) forbids a second `click`/`dblclick`; the rest have no honest page event |

**Lost — KeyPress WinForms-only.** Piece 2 implements KeyPress on the web, and game code wants it. **Lost —
`beforeinput`** for KeyPress: it fires only on editable elements. **Lost — `focus`/`blur`** for Enter/Leave: they do not
bubble, so a Panel's Enter would never fire for its children, and GroupBox already shipped `focusin`.

### D-3 — The Form's events and the web wiring (plan 5.1/5.2, spec §2.3/§5)

`FormRoot.Events` (default **Load**, as the snapshot's `defaultEvent`):

| Event | Args | Web | Web wiring |
|---|---|---|---|
| Load | EventArgs | `load` | **AfterInit**: `Me.<Form>_Load()` is the LAST statement of the generated `InitializeComponent`; the handler is PARAMETERLESS |
| Shown, Activated | EventArgs | — | |
| FormClosing / FormClosed | FormClosingEventArgs / FormClosedEventArgs | — | |
| Resize | EventArgs | `resize` | **Window**: `w.addEventListener("resize", AddressOf …)` |
| Click | EventArgs | `click` | **Element** = `doc.body` |
| KeyDown / KeyUp / KeyPress | KeyEventArgs / KeyEventArgs / KeyPressEventArgs | `keydown` / `keyup` / `keypress` | **Element** = `doc.body` |

**The representation:** `FormEventDef` gains `FormWebWiring WebWiring = FormWebWiring.Element`, a new enum
`{ Element, Window, AfterInit }` in `FormEvents.cs`. Element means "the control's element" for a control and
`doc.body` for the Form (its `HtmlTag` is `body`, `FormControlCatalog.cs:2662`). `WiredOn`'s signature does not change.
Invariants (Task 1 tests): `Window`/`AfterInit` only on `FormRoot` events; at most one `AfterInit` event, and it is the
row's default; an `AfterInit` event's web handler takes no parameter.

**Emission (Task 2):** WinForms — `AddHandler Me.<Event>, AddressOf <h>` for every root bind, AFTER the reference rows
(`RegionWriter.cs:763-766`), immediately before `End Sub` — VS's place for `this.Load += …`. Web — the root listeners
after the controls (the elements exist), then `Me.<h>()` for the AfterInit bind as the very last line; `Dim w As Window
= ::window` is declared when a script component exists (today, `:719-722`) **or** a root bind is Window-wired, and never
otherwise (every existing page's region stays byte-identical). ⛔ `Me.`-qualified (the JS unqualified-self-call trap).

**User-facing consequence** (spec: "goes into the docs"): placed where the user meets it — a comment line in the WEB
scaffold beside `Me.InitializeComponent()` in `New()` (`FormScaffolder.cs:230`): *"On a web page, Form Load runs at the
end of InitializeComponent: code after this line runs after Load. On WinForms Load runs later, when the form is shown."*
Plus an entry in `docs/form-designer-followups.md`. RE-CHECK: `FormScaffolderTests` goldens for the web scaffold.

**Recorded divergences (not fixed here — same class as the shipped web Panel Click):** the page's `click` on `body`
bubbles from every control, where WinForms' Form.Click excludes clicks on child controls; key events reach `body` from
any focused control, as if `KeyPreview=True`. Both are piece 2's to match in the portable library if it chooses
(`e.target == e.currentTarget`; `KeyPreview`).

**Lost — Load as a `window` `load` listener:** the D7 dispatch constructs the form after the page is ready, so the
window's `load` can already have fired — a handler that registers cleanly and silently never runs. **Lost — Shown on the
web** (the same moment as Load; two names for one instant would make the twin's ordering meaningless). **Lost —
FormClosing → `beforeunload`:** browsers discard work and custom prompts there, so the handler would look wired and do
nothing. **Lost — Activated → window `focus`:** a page has no window activation among windows.

### D-4 — Handler fitting (plan 5.4)

`FormHandlers.FittingHandlers(form, owner, evt, codeText)` returns the Subs of the code-behind that FIT, in document
order, from ONE text scanner (`FormCodeScan`, D-11):
- **WinForms:** exactly two parameters; the first `Object` (or untyped); the second's type `T` fits an event with args
  `A` iff `T` is `EventArgs`, or `A`, or a .NET BASE of `A` — compared on the last segment, ignoring case. The base
  chains live in ONE table, `FormEvents.ArgsBases` (e.g. `CancelEventArgs` ⊃ FormClosingEventArgs,
  TreeViewCancelEventArgs, TabControlCancelEventArgs, DataGridViewCellCancelEventArgs,
  DataGridViewCellValidatingEventArgs, SplitterCancelEventArgs, DoWorkEventArgs; `MouseEventArgs` ⊃
  TreeNodeMouseClickEventArgs, DataGridViewCellMouseEventArgs; `AsyncCompletedEventArgs` ⊃ RunWorkerCompletedEventArgs).
  ⛔ The table is falsified EXHAUSTIVELY by in-process Roslyn (Task 3): for every event of every row and every args type
  the catalog names, `Fits` must equal csc's verdict on `ctl.E += H` with `H(object, T)`.
- **Web:** a listener event (Element/Window) fits exactly one parameter typed `DomEvent`; an AfterInit event, or an event
  of a row with `WebHandlerTakesEvent: false` (Timer), fits exactly zero parameters.
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
  `DropDownOpened` (an async re-read: open tab first, then disk — `ReadCodeBehindAsync` `:827-828`).
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
- BL8013 extends to the root's LISTENER binds (body/window, via `AddressOf`); an AfterInit bind is a direct call and is
  exempt (M2).
- BL8028 is no longer raised for root binds (components keep it). BL8026 for retarget loss. BL8035 unchanged.
- BL8032's message lists the declared events comma-separated.
- **BL8037 stays free for piece 2.** **Lost — a "handler does not fit its event" code:** both failures are already LOUD
  (csc CS0123 on WinForms; BasicLang's delegate-conversion / arity error on the web), and the designer never writes a
  misfit. Follow-up if the messages prove unreadable.

### D-8 — The representation piece 2 consumes (ADR 0021, written FIRST in Task 1)

Piece 2 reads, per its plan Task 40 and spec §5.6/§10.1/§11.1: "every event slice 5 listed gets a library member … the
coverage gate picks them up from the catalog". What it gets:
1. `FormControlDef.Events` / `FormRoot.Events` — `FormEventDef(Name, WinFormsArgs, WebEvent, Category, Description,
   IsDefault, OracleExemption, IsWebDefault, WebWiring)`. `WebEvent` stays THE one DOM list (its §10.1); null =
   WinForms-only (its `WebUnavailableMember` in shared code).
2. `FormEvents.WiredOn(definition, target)` — unchanged signature.
3. `FormEvents.DomInterfaceOf(evt)` — NEW, one table keyed by the DOM name: `click`/`dblclick`/`mouse*` → `MouseEvent`,
   `key*` → `KeyboardEvent`, `focusin`/`focusout` → `FocusEvent`, else `Event`. Slice 5's node/Edge tiers and piece 2's
   coverage gate both dispatch through it — no second table.
4. `WebWiring` — where the DOM source is for the Form (body / window / a call at the end of init).

What piece 2's Task 40 must add beyond its §5.6 table: **DoubleClick (`dblclick`), MouseEnter/MouseLeave, Leave
(`focusout`, relatedTarget outside — the mirror of its Enter rule), Form Load (AfterInit), Resize (window),
Form Click/KeyDown/KeyUp/KeyPress (body)**. ⚠ Its §5.6 says KeyPress is "a character-producing `keydown`"; the catalog
says `keypress`. Both fire for the same real input (CDP `Input.dispatchKeyEvent` with text raises both), so its gate
passes either way; its pre-flight must pick one and record it. Its Task 30 rewrites the stub signature site that Task 3
here restructures (`FormHandlers.Insert`) — re-anchor on this slice's merge.

### D-9 — Double-clicking the form's background opens `<Form>_Load` (VS)

`FormCanvasControl.OnCanvasDoubleTapped` (`:715-718`) runs a NEW `ActivateFormCommand` (a new StyledProperty, bound in
`CodeEditorDocumentView.axaml` beside `ActivateControlCommand` `:247`) instead of returning. `ActivateControlCommand(null)`
stays a no-op — no existing caller's meaning changes. Optional: dropping D-9 affects nothing else. **Lost — leaving the
root reachable only through the Events tab** (VS users double-click the form for Load by habit).

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

### Task 1 — The event lists, `WebWiring`, `DomInterfaceOf`, ADR 0021 (plan 5.1)
First: `docs/superpowers/decisions/0021-form-event-lists-and-web-wiring.md` (D-2, D-3, D-8) + the README index.
Files: `FormEvents.cs` (`FormWebWiring`, the record field, `DomInterfaceOf`, `ArgsBases` stub — filled in Task 3, its
doc comment `:53-66` updated); `FormControlCatalog.cs` (every row per D-1, `FormRoot.Events` per D-3; the stale `Ev`/
`Events` comments `:1396-1410`, `:1869-1873`); `DesignDiagnostic.cs` (BL8026/BL8032 doc text `:230-236`, `:314-318`).
Procedure: names/args/web names first, then the parity run prints the category and description of each, pasted verbatim.
Tests (`FormEventsTests`, fast):
- REWRITE `WiredOn_TheFormRoot_IsEmpty_UntilFormEventsExist` → `WiredOn_TheFormRoot_IsItsTenEvents_OnWinForms_AndSix_OnTheWeb`
  (web: Load, Resize, Click, KeyDown, KeyUp, KeyPress).
- REWRITE the exact-list pins (`:125-136`, `:166-183`, `:189-203`, `:209-220`, `:259-270`) as catalog-derived or
  `Contains` assertions that keep their point (Enter is GroupBox's default; Paint is never on a web Panel; …).
- `NoRow_HasTwoEventsWithOneWebName` extended to `FormRoot` (`All.Append(FormRoot)`).
- NEW invariants, each over `All` + `FormRoot`: `Window`/`AfterInit` only on FormRoot; ≤ 1 AfterInit and it is the
  default; every `WebEvent` is a key of `DomInterfaceOf`'s table (no DOM name the dispatch tiers cannot fire); every
  `WinFormsArgs` whose snapshot namespace is not `System`/`System.Windows.Forms` is written qualified (reads the oracle's
  `argsFullName`); every row that declares events has exactly one default (exists, `:14-24`).
- Parity (automatic, fast): `WinFormsCatalogParityTests` per kind and `TheFormRoot_MatchesTheSnapshotsForm`.
Mutations: FormRoot Load's `WebEvent` null (the root WiredOn test); MouseClick given `click` (NoRow…); an event's
`WebWiring = Window` on a control (invariant); `System.ComponentModel.` dropped from Validating (qualification test, and
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
`Me.<h>()` last; the `Dim w` condition widened; `CheckHandlerOrdering` includes root LISTENER binds; the BL8032 message
comma-joined. `FormDocument.cs:103-108` doc. `FormScaffolder.cs` web comment (D-3).
Tests (`FormRootTests`, fast):
- REWRITE `ARootBind_IsWarned_NotEmitted_UntilFormEventsExist` → `ARootBind_IsWired_AsTheLastStatement_OnWinForms`
  (`AddHandler Me.Load, AddressOf F_Load` is the line before `End Sub`, after `Me.AcceptButton = …`).
- `TheWebLoad_IsAMeQualifiedCall_LastInInitializeComponent` (the exact `Me.F_Load()`, after every `getElementById`).
- `AWebRootKeyBind_ListensOnTheBody`, `AWebRootResizeBind_ListensOnTheWindow_AndDeclaresW`.
- `APageWithoutWindowBinds_DeclaresNoW` — every existing region-writer golden stays byte-identical.
- `AWebRootBind_OnShown_IsRefusedAsBL8032_NamingTheForm` (refused, nothing written).
- `AWebRootKeyHandler_DeclaredBelowTheRegion_IsBL8013` and `AWebLoadHandler_DeclaredBelow_IsNot` (M2).
- `FormScaffolderTests`: the web scaffold carries the Load comment.
Mutations: Load called before the controls; `Me.` dropped (the exact-text test, and Task 4's node run: `ReferenceError`);
`w` not declared for a Resize bind (unit test, and Task 4's JS compile); the root BL8032 check removed; BL8013 skipping
root listeners.
RE-CHECK: `FormRootTests.cs:183`, `:249` (root-bind read/write fixtures); every `FormRegionWriterTests` golden;
`FormComponentEmissionTests` (the `w` line with a Timer — byte-identical); `FormScaffolderTests` web goldens;
`FormBuildEmissionTests`.

### Task 3 — Owners, the shared scanner, fitting (plan 5.3/5.4)
Files: new `FormBindOwner.cs`, new `FormCodeScan.cs`; `FormHandlers.cs` — `Plan(form, owner, evt, code, handlerName?)`,
`PlanDefault` delegating (unchanged signature and behaviour), `PlanBind(form, owner, bind, code)` (the control overload
kept, delegating), `EnsureBind(owner, …)`, `Unbind(owner, eventName)`, `FittingHandlers`, `Fits`; the signature site
(`:217-222`) chooses `()` for AfterInit and `WebHandlerTakesEvent: false`; `FindDeclarationLine` deleted.
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
Mutations: an `ArgsBases` entry removed (CancelEventArgs ⊃ FormClosingEventArgs) → the csc test; fit by exact name only
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
  `window`); dispatch every wired event once (`new Event(type, { bubbles: true })` — node has no `MouseEvent`/
  `KeyboardEvent`, so the node tier proves wiring BY NAME); assert each handler ran exactly once, and `Load` ran once at
  construction BEFORE any dispatch.
- Edge, where installed (SKIP otherwise, never fail): one page (Button with Click/MouseEnter/KeyDown, the Form's Load and
  Resize) through the loopback-served CLI-built site (slice-4 Part F shape); a new `EdgeStep.Dispatch(label, target,
  type)` dispatching `new (DomInterfaceOf)(type)` — the real interfaces; the page writes each handler's name into a label
  read back by `HasText`. ⚠ Synthetic dispatch, not real input — real input (CDP `Input.*`) is piece 2 Task 35.
Mutations: the web emitter writes the WinForms spelling (`"Click"`) → node red; AfterInit unqualified → node
`ReferenceError`; Load emitted as a `load` listener → "Load ran once at construction" red; Window bind on `doc.body` →
the resize dispatch never runs.

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
  `LoginForm_Load`; (h) typing `DoIt` + Enter → a `DoIt` stub, bound.
- `FormCanvasDoubleClickTests`: REWRITE `DoubleClickingEmptyFormBackgroundOpensNothing` → `…AsksTheHostToOpenTheFormsLoad`
  (`ActivateFormCommand` run, `ActivateControlCommand` not); an AXAML read that `CodeEditorDocumentView.axaml` BINDS
  `ActivateFormCommand` (an unbound command is unreachable — CLAUDE.md).
- `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…`: in Events mode the handler combo is swept and REQUIRED seen.
Mutations: the pair in the existing panel (the group test); `HandlerRequested` unwired ((b) red); the background
double-click not routed ((g)/canvas test); the Bind written before a failed stub write (a refused gesture must write
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
  are fetched — then each dispatched handler once.
Mutations: the WinForms `AddHandler Me.Load` dropped (the log); web Load moved before the controls (`lbl` is null);
the IDE route alone skipping the region write (the IDE half red, the CLI half green — the "both entry points" kill).

### Task 9 — Records, mutation ledger, gate (§5), IDE drop, click-through
Execution notes per task in §6 below (as slice 4's §8). `docs/form-designer-followups.md`: the two D-3 divergences, the
"handler does not fit" code follow-up (D-7), and the piece-2 KeyPress reconciliation.

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
  7. Double-click the form's background: `<Form>_Load` opens; F5 — Load runs before the window shows.
  8. On a WEB form: a Panel shows no Paint; Click creates `pnl_Click(e As DomEvent)` above the region. The Form's Events
     show Load/Resize/Click/KeyDown/KeyUp/KeyPress only. Load writes a label; open the page: the label shows it; resize
     the browser: Resize runs.
  9. Retarget a WinForms form that has a Load and a MouseDown handler to the web: the new pair builds and the page runs
     both; FormClosing is reported as not crossing.

## 6. Execution notes
(Filled per task as slice 4's §8: base SHA, deviations, red evidence, RE-CHECK results, the mutation table.)

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
