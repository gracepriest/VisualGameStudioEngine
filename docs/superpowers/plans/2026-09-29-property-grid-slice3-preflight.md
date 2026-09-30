# Slice 3 pre-flight: the D1 property batches, expanded and corrected against today's tree

Plan: `docs/superpowers/plans/2026-09-25-property-grid-vs-parity.md`, section "## Slice 3 — The D1 property batches, both
targets" (plan lines 5983–6012, Tasks 3.1–3.6, plus the two CARRIED backlog notes at its top and the "Tests to re-check"
table at plan lines 6084–6100). Spec: `docs/superpowers/specs/2026-09-25-property-grid-vs-parity-design.md` §1 (D1, D2),
§2.2, §2.3, §2.4, §3, §7, §8.

The plan's slice 3 was written at TASK granularity against `feat/property-grid` @ `f2b72dbb`. This pre-flight was made
against **master @ `14c2e17d`** (PR #139 merged slices 1–2 and web pixel layout piece 1; PR #140 and the name-binding /
lambda-diagnostic work landed after). Branch `feat/property-grid-slice3`, worktree under the session scratchpad.

Follow this document **alongside** the plan. Where the two disagree, **this document wins**.

---

## 0. Baseline (measured)

- Build: `dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release` — 0 errors.
- Fast subset (`TestCategory!=Integration`) @ `14c2e17d`: **9111 total / 9086 passed / 6 failed / 19 skipped**. Failure
  names, sorted: `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll` — all on the known machine list.

## 1. OWNER DECISIONS (2026-09-29) — implemented exactly

| # | Decision | Where it lands |
|---|---|---|
| O1 | A newly dropped TableLayoutPanel starts 2 columns × 2 rows, as VS does | New catalog facet `FormControlDef.DropValues` (a row states what a drop writes — never a kind switch in placement); `FormPlacement.Place` applies it. Task 2. |
| O2 | HIDE the colour properties WinForms hides (non-browsable) — our grid shows what VS shows | The nine `HiddenInWinForms(...)` rows are REMOVED from both targets (PictureBox.ForeColor, DateTimePicker.ForeColor/BackColor, TrackBar.ForeColor, DataGridView.ForeColor/BackColor, TabControl.ForeColor/BackColor) and the helper deleted. Task 2. |
| O3 (corrected by the owner the same day) | GroupBox's DEFAULT event (what double-click creates) is **Enter**, as in VS; a Button's is Click. Web mapping: DOM `focusin` on the fieldset. Keep Click as a NON-default event. Check the other containers against the oracle, change nothing without evidence. | GroupBox `Events` = `Enter` (default, `EventArgs`, Focus, WinForms' text, web `focusin`) + `Click` (non-default, web `click`, its existing OracleExemption). No exemption on Enter (the snapshot agrees). Task 2. |
| O4 | BackgroundWorker rows' descriptions use WinForms' own text | Measured on this machine with Windows PowerShell 5.1 (`TypeDescriptor` over .NET Framework 4.8's `System.dll`): WorkerReportsProgress "Whether the worker will report progress.", WorkerSupportsCancellation "Whether the worker supports cancellation.", DoWork "Event handler to be run on a different thread when the operation begins." The .NET 8 snapshot records NO description (that is why the exemption exists), so the OracleExemption STAYS, its reason reworded to say where the text comes from. Category stays Misc (what VS shows on .NET 8; Framework says Asynchronous, and the parity test judges Category). Task 2. |

**O3 container check (report, no change — the oracle is the evidence, the owner decides):** oracle `defaultEvent` vs
catalog default: Panel **Paint** vs Click; FlowLayoutPanel **Paint** vs Click; TableLayoutPanel **Paint** vs Click;
SplitContainer SplitterMoved = SplitterMoved ✓; TabControl SelectedIndexChanged = SelectedIndexChanged ✓. Outside the
containers the oracle also differs on TrackBar (**Scroll** vs ValueChanged) and DataGridView (**CellContentClick** vs
CellClick). All five are left as they are and reported for an owner decision (Panel's own comment records why Click was
chosen over Paint).

## 2. BLOCKERS (found while anchoring — the plan's task text would fail or regress as written)

### B1: GroupBox Enter (O3) would silently DROP every existing GroupBox `Click` bind on retarget.
- **Evidence.** `FormRetarget.ConvertBinds` (`BasicLang/Forms/FormRetarget.cs:482-511`) crosses a control bind ONLY on
  `definition.DefaultEvent(_from)`; any other bind is `RetargetBindLost`. Today GroupBox's default is Click, so a
  `<Bind Event="Click">` on a GroupBox crosses. With Enter as the default it would be dropped.
- **Correction.** Pull the plan's slice-5 Task 5.6 rule forward for CONTROLS only: a bind crosses when its event is in
  `FormEvents.WiredOn(definition, _from)` AND that same event is in `WiredOn(definition, _to)`; the destination's name is
  `FormEvents.NameOn(evt, _to)`. `ConvertRootBinds` already uses this rule (`:305-334`); the two stay separate methods
  (their messages differ) and the slice-5 ⚠ marker is updated. Retarget sweep test over every event of every row.

### B2: `FormCss.Declaration` returns ONE declaration; the spec's Font converter needs five.
- **Evidence.** `BasicLang/Forms/FormCss.cs:16-39` returns `(string Property, string Value)?`; its caller
  `FormAssetEmitter.CatalogDeclarations` (`FormAssetEmitter.cs:592-615`) adds one string per row.
- **Correction.** `FormCss.Declarations(property, value)` returns `IReadOnlyList<(string Property, string Value)>`
  (empty = none). `Declaration` stays as a thin wrapper for the single-declaration tests (returns the only element, or
  null; throws if a converter yields several — a caller that needs them must use the list). `CatalogDeclarations` and the
  new body rule use the list.

### B3: The plan says the Form's web CSS targets `body` — but the Canvas stylesheet ALREADY writes a `body` rule.
- **Evidence.** `FormAssetEmitter.CanvasCss` writes `body { margin: 0; display: flex; … }` (`FormAssetEmitter.cs:663`);
  Grid/Flow `Css` writes none.
- **Correction.** The root's declarations are a SEPARATE `body { … }` rule appended after the layout rules on both
  stylesheets (CSS merges rules; the layout rule is not rewritten). One emitter method, `AppendRootCss`, both paths.

### B4: `AcceptButton = btnOk` emitted where the plan puts root rows (BEFORE the controls) assigns Nothing.
- **Evidence.** `RegionWriter.GenerateInit` emits every FormRoot row before the components and controls
  (`RegionWriter.cs:624-659`). `btnOk` is a field assigned later in the same method, so `Me.AcceptButton = btnOk` there
  stores null — csc accepts it, the running form has no accept button. VS writes `Me.AcceptButton = Me.btnOk` after the
  controls.
- **Correction.** Reference-typed root rows are emitted AFTER the add run, beside the `FormProperty` (MainMenuStrip) line
  (`RegionWriter.cs:673-684`). The acceptance test RUNS it (Enter presses the accept button).

### B5: A single-letter class name breaks `Me.` member resolution (measured while measuring the new literals).
- A probe class named `M` failed with "Type 'M' does not have a member 'Controls'"; renamed `SweepForm` it compiled. This
  is chip `task_ef845b99` ("a class named F breaks Me.") — not this slice's; every fixture here uses `SweepForm`/`LoginForm`.

## 3. Per-task CORRECTIONS to the plan

- **Point is NOT added as a FormPropertyType** (plan 3.1 lists it). No D1 row stores a Point: Location is the canvas's
  `PixelGeometry` (spec §2.4, "never a second copy"), and TabControl.Padding (a Point) is not D1. Scope call S1's own
  rule — "a type with no row cannot be put through the sweep" — forbids adding it. The Location composite (Task 7) is
  over the intrinsic X/Y rows.
- **`Or` on flags is not expressible; `CType(n, FontStyle)` is** (the plan's ⚠ asked to measure FIRST). Measured today
  through the CLI (`--target=csharp`): `New Font("Segoe UI", 9F)` → `new Font("Segoe UI", 9.0f)`; `9.75F` → `9.75f`;
  `CType(3, FontStyle)` → `((FontStyle)(3))`; `9.75!` does not parse ("Expected ')'… found Bang"); `New Padding(4)`,
  `New Padding(4, 2, 4, 2)`, `Cursors.Hand`, `Me.Opacity = 0.85`, `Me.AcceptButton = btn` all emit as written. So one
  style is `FontStyle.Bold`, several are `CType(n, FontStyle)   ' Bold, Italic` (the AnchorExpression shape).
- **Font / Cursor are not on every kind.** The snapshot has NO browsable `Font` on PictureBox, TrackBar, ProgressBar,
  DataGridView; TextBox's Cursor default is `IBeam` (reset) where every other kind's is ambient. `Common(...)` therefore
  gains Font and Cursor through an explicit per-kind choice, never a blanket add (parity would name every miss).
- **Padding rows: content controls only (Label, Button, CheckBox, RadioButton, LinkLabel), not containers.** A container's
  Padding shrinks WinForms' DisplayRectangle, which moves DOCKED children — `FormDockLayout` does not model it, so a padded
  Panel would dock differently in the designer and at run time. Recorded as a follow-up.
- **Opacity is a new `Double` type** (plan: "a new type or an Int percent? decide"): stored invariant `0`–`1` (`0.85`),
  WinForms' own property type; emitted as the parsed number; web: WinForms-only. The grid shows `0.85`, not VS's `85 %`
  (a click-through item).
- **AcceptButton/CancelButton are a new `Reference` type** (plan: "decide the shape"): the document stores a control Id; the
  row states which kinds it may name (`FormPropertyDef.ReferenceKinds`, Button here — the catalog's only
  `IButtonControl`); `Accepts` checks the Id is a legal identifier (document-free); the region writer, which HAS the
  document, warns **BL8033** `ReferenceNotFound` and emits nothing when the Id names no control of those kinds (renamed,
  deleted, or a Label). Emitted after the add run (B4). WinForms-only.
- **CssClass / Style (D2 web-only extras).** `Style` is `HtmlAttribute: "style"` (the emitter's `Attr` escaping already
  handles quotes; an inline style is exactly "raw CSS" and cannot break out of the attribute). `CssClass` is appended to
  the element's own `class="vgs-Kind …"` — the emitter reads it exactly as it reads `GroupName` (a second `class=` would be
  invalid HTML); its value must be class-token characters or it is Degraded. Both on every positioned/strip/item web kind,
  never a tray component (no element).
- **The D2 test (plan 3.4) is directional:** "no WinForms-only row reaches a page" — for every web kind, set every
  `Targets: WinForms` row to a sample and the page (HTML + CSS) must be byte-identical to the page without them. The
  REVERSE direction (every web-applicable row reaches the page) is NOT asserted: pre-existing rows fail it today
  (TextBox.Multiline, TextBox.PasswordChar, Panel.BorderStyle, PictureBox.SizeMode — the web harness records the border gap
  as a literal) and fixing them is piece 2's portable library. Recorded as a follow-up, not hidden.
- **`FormRootValues` default arm = `form.Properties`** — FormRootValues.cs's doc comment says the typed-field map is the
  one answer; a Properties-stored row (every slice-3 root row) is `Get`/`Set`/`StorageAttributes` = its own name,
  `RefusalOf` = null except where a store rule exists, `CanReset` = true.
- **The web refused-value diagnostic (CARRIED backlog, plan :5996)** lands in `RegionWriter` for a WEB document (it already
  runs for both targets on save and build): every control/component property whose row applies to the web and whose value
  the web refuses is BL8009 "…It is not written into the page.", composed from `DescribeRefusal` exactly as the WinForms
  site is. (FormAssetEmitter has no diagnostics channel; the region writer is where the same document is already judged.)
- **Degraded geometry / TabIndex (CARRIED backlog (1)) — decided:** Degraded tier + warning; U+2212 is NOT accepted on
  read (the rule since 951fcf46 — a document means one number on every machine). The reader adds a `DegradedProperty`
  (control id, `X`/`Y`/`Width`/`Height`/`TabIndex`, or `Col`/`Row`/`ColSpan`/`RowSpan`) whose reason names a U+2212 when it
  sees one; `DesignCheck` reports it (it already lists `Degraded`), and ALSO starts listing `DegradedRoot`, which it never
  did (found here: a Degraded ClientSize was invisible to `design --check`). The grid's intrinsic row freezes with that
  reason and shows the raw text (the slice-2 B1 rule). The writer already never overwrites unreadable text.
- **Root Width/Height no-op (backlog (3))**: `ApplyFormAttributes` uses `SetIntAttributeIfChanged`-style parse-then-compare
  (never overwrite text the reader could not parse; keep `"0400"` spelling). There is no `absentMeans` for a root size, so
  a dedicated `SetParsedIntIfChanged(element, name, value)` (write when absent; when parseable write only on a different
  number; when unparseable never — the model holds null for unparseable, and that path is not reached).
- **`ExpandWebScript` (backlog (2))**: an Int row's value is re-emitted from `TryParseInt` (invariant); non-Int rows
  unchanged. `Interval=" 250"` must emit `250`.
- **Backlog (4) + the RegionWriter root-Degraded branch (plan :6100)**: both become reachable with the first Properties-
  stored root row (FormBorderStyle="Bogus"): Task 4 adds a test for each.

## 4. The tasks (TDD, one commit per task)

Every task: red test(s) first, run, see them fail for the right reason; implement; green; mutation-check each new branch
(revert with the Edit tool and REBUILD; record killed/equivalent); commit by name.

### Task 1 — Backlog fold-in
Files: `FormDocumentReader.cs`, `DesignDiagnostic.cs` (DesignCheck), `FormDocumentWriter.cs`, `RegionWriter.cs`,
`FormPropertyGridViewModel.cs` (intrinsic freeze). Tests: `FormDocumentRoundTripTests` (tighten
`Read_AUnicodeMinus_IsNotANumber_…` to assert the value is 0 AND a Degraded entry naming U+2212), new
`FormDegradedGeometryTests` (X/Y/Width/Height/TabIndex/Col/Row degraded; a no-op save keeps the text; design --check lists
it; DegradedRoot listed), `FormRootTests` (root `Width="0400"` no-op save byte-identical), `FormComponentEmissionTests`
(`Interval=" 250"` → `250`), `FormRegionWriterTests` (web `BackColor="ActiveCaption"` → BL8009 naming the page),
`FormPropertyGridTests` (a frozen X row shows the raw text and the reason).
Mutations: drop the degraded add; DesignCheck root loop; parse-compare → text compare; ExpandWebScript raw; web BL8009
loop; intrinsic freeze.

### Task 2 — Owner decisions O1–O4 (+ B1)
Files: `FormControlCatalog.cs` (DropValues facet + TableLayoutPanel `{ColumnCount=2, RowCount=2}`; remove
HiddenInWinForms rows + helper; GroupBox events; BackgroundWorker descriptions/exemption text), `FormPlacement.cs`,
`FormRetarget.cs` (ConvertBinds via WiredOn). Tests: `FormPlacementTests` (a dropped TableLayoutPanel carries
ColumnCount=2/RowCount=2; no other kind gets DropValues it did not declare — catalog-driven), `FormCatalogCoverageTests`
or `FormEventsTests` (GroupBox default is Enter on WinForms and `focusin` on the web; Click still wired, non-default),
`FormRetargetTests` (a GroupBox Click bind crosses both ways; sweep: every event wired on both crosses under the
destination's name), parity + sweeps stay green (Integration: the default-event csc sweep builds GroupBox1_Enter).
Update the re-check pins: any test asserting GroupBox→Click, the exemption sweep count (≥4 still true).
Mutations: DropValues not applied; ConvertBinds back to DefaultEvent-only; GroupBox IsDefault swap.

### Task 3 — New types Font, Padding, Cursor (plan 3.1, minus Point) + their first rows
Files: `FormControlCatalog.cs` (`FormPropertyType.Font/Padding/Cursor`; Accepts, Canonical, WinFormsLiteral,
SourceLiteral, SameValue; new `FormFont`, `FormPadding`, `FormCursors` value types/tables in their own files), `FormCss.cs`
(converters `Font`, `Padding`, `Cursor`; the list API — B2), `FormAssetEmitter.cs` (list), rows: Font + Cursor on the kinds
the snapshot lists (TextBox's own IBeam Cursor), Padding on Label/Button/CheckBox/RadioButton/LinkLabel.
Stored forms: Font `Family, Size[pt][, style=A, B]` (WinForms FontConverter invariant text; family = letters, digits,
spaces, `-`; size a positive invariant decimal with at most two places; unit `pt` or none; styles ⊂ Bold, Italic,
Underline, Strikeout; anything else Degraded). Canonical: `Segoe UI, 9pt, style=Bold, Italic` (styles in FontStyle flag
order). WinForms: `New Font("Segoe UI", 9F)` / `…, FontStyle.Bold)` / `…, CType(3, FontStyle))   ' Bold, Italic`.
Web: `font-family: "Segoe UI"; font-size: 9pt; font-weight: bold; font-style: italic; text-decoration: underline
line-through`. Padding `4` or `4, 2, 4, 2` (Left, Top, Right, Bottom; non-negative ints); canonical uniform → `4`;
WinForms `New Padding(4)` / `New Padding(4, 2, 4, 2)`; web `padding: 2px 4px 2px 4px` (CSS order top right bottom left).
Cursor: one of the 28 `Cursors` members (reflected above); WinForms `Cursors.Hand`; web through ONE table
(`FormCursors.CssFor`) — a member with no CSS equivalent (UpArrow, NoMove*, Pan*) is refused on the web as a VALUE with a
reason (the system-colour rule). Parity `CatalogParity.TypeFits`/`SameDefault` arms for the three.
Tests: `FormPropertyDefTests` per type (accept/degrade/canonical/literal/source-form round trip), `FormCssTests` per
converter (+ `EveryConverter_…` samples, `NoDeclarationIsEmittedTwice` extended to Font's five), sweep `SampleValue` arms +
`FormRetargetTests.Sample` + `FormRootRetargetTests.Sample`, one csc compile per new shape (Integration: every Cursors
member in one compile; a Font with 0/1/3 styles; Padding 1 and 4 values), parity green.
Mutations: CType vs single style; padding order; cursor web refusal; font-family safety; unit check.

### Task 4 — Root Properties storage + the Form's D1 rows (plan 3.2 + 3.3)
Files: `FormDocument.cs` (`Properties`, ordinal), `FormRootValues.cs` (default arm), `FormDocumentReader.cs` (root
attribute → Properties + target-aware Degraded into DegradedRoot), `FormDocumentWriter.cs` (Apply set-if-changed +
remove-dropped for Properties-stored rows; Create in catalog order), `FormRetarget.cs` (Properties-stored rows cross when
they apply on the destination, else `RetargetPropertyLost 'form.X'`; a value the destination refuses crosses preserved
and named), `FormFile.cs` (TierOfRoot already generic — verify), `RegionWriter.cs` (reference rows after the add run;
BL8033), `FormAssetEmitter.cs` (`AppendRootCss` on both stylesheets — B3), `FormControlCatalog.cs` (`Double`, `Reference`
types; the 18 FormRoot rows with WinForms' metadata; web: BackColor/ForeColor/Font with `WebDefault: ""`).
Tests: `FormRootTests` (round trip; Degraded FormBorderStyle frozen + preserved + BL8009 at emission — the unreachable
branch now reached; reset removes; Create order), `FormRootRetargetTests` (sweep's RetargetPropertyLost arm now
non-vacuous; the degraded-on-both-targets branch — backlog (4)), root csc sweep (automatic per row), `FormCssTests`/
`FormAssetEmitterTests` (`body { background-color… }` on Grid and Canvas pages; a docked strip inherits it — asserted as
the rule being on body, which every descendant inherits), `FormRegionWriterTests` (AcceptButton after the adds; BL8033 for
a missing id and for a Label id), `FormPropertyGridDisplayTests.TheFormsRows_ComeFromFormRoot` (expected set grows).
Mutations: default arm → throw; reader Degraded check; writer remove-dropped; reference emitted before controls; BL8033
kind check; body rule not emitted on Canvas.

### Task 5 — The controls' D1 batches + CssClass/Style + the D2 test (plan 3.4)
Rows (WinForms metadata from the snapshot, verbatim; web mapping where clean, else `Targets: WinForms`):
Label AutoSize, BorderStyle · TextBox UseSystemPasswordChar, ScrollBars, WordWrap, PlaceholderText (web `placeholder`),
TextAlign (HorizontalAlignment; web `text-align` via the horizontal converter) · Button DialogResult, FlatStyle ·
CheckBox CheckState, CheckAlign, ThreeState, AutoCheck, Appearance · RadioButton CheckAlign, AutoCheck, Appearance ·
ComboBox DropDownStyle, Sorted, MaxDropDownItems · ListBox SelectionMode, Sorted, MultiColumn · Panel AutoScroll (web
`overflow: auto` via a new `AutoScrollToOverflow` converter) · PictureBox BorderStyle · LinkLabel LinkColor, LinkBehavior,
AutoSize · NumericUpDown Hexadecimal, ReadOnly (web `readonly` — the emitter's existing flag), TextAlign (web text-align) ·
DateTimePicker ShowCheckBox · TrackBar TickStyle, LargeChange, SmallChange · CheckedListBox Sorted · TreeView CheckBoxes ·
TabControl Appearance (TabAppearance) · SplitContainer FixedPanel · ToolStripMenuItem ShortcutKeyDisplayString. Web-only
CssClass + Style on every web element kind.
Tests: parity/csc sweep/every-Enum sweep/retarget sweep are automatic; new `FormWebOnlyAndWinFormsOnlyTests` (the D2
direction above; CssClass/Style reach the page and a hostile CssClass is Degraded), `FormAssetEmitterTests` (placeholder,
class merge, style attribute, overflow).
Mutations: D2 — give a WinForms-only row a CssProperty; class merge dropped; CssClass validation.

### Task 6 — Composite rows in the grid (plan 3.5)
`FormPropertyRow` gains `Children` + `IsExpanded` (`IFormDisplayRow` carries both); `FormPropertyDisplayList` inserts an
expanded parent's children (indented, in the parent's category, excluded from the name sort and matched by search through
the parent); children read/write through the parent's composite parse/format (one write per child edit — the fan-in rule
holds because the parent writes the whole value). Composites: Font (Name, Size, Bold, Italic, Underline), Padding (All,
Left, Top, Right, Bottom), any Size row (Width, Height), and the intrinsic Location (X, Y) / Size (Width, Height) parents
that REPLACE the flat X/Y/Width/Height rows in `Rows` (14 test references updated). The parent still accepts typed text.
AXAML: an expander glyph on the name cell and a left indent for children (`dotnet clean` after the AXAML change); binding
reflection test extended. Real-view tests at two window sizes (expand Font, toggle Bold, the document carries
`style=Bold`; expand Location, type X; collapse survives reselection).
Mutations: child write not reaching the parent; expanded children not inserted; search through parent.

### Task 7 — RUN, not compile (plan 3.6)
New Integration fixture `FormPropertyBatchAcceptanceTests`: one WinForms form using FormBorderStyle, StartPosition,
BackColor, Font (bold), Opacity, TopMost, AcceptButton, a TextBox with PlaceholderText/TextAlign, a Button with a Cursor
and Padding, a CheckBox with CheckAlign, built through the real designer VM + grid rows, compiled by the real CLI, RUN:
the driver prints what the live window reports (`FormBorderStyle`, `StartPosition`, `BackColor`, `Font`, `Opacity`,
`TopMost`, `AcceptButton.Name`, each control's `Font`/`Cursor`/`Padding`/`TextAlign`) and presses Enter through
`ProcessDialogKey` (AcceptButton → the handler fires). The web twin: the same form as a Canvas page, built as a JavaScript
project, run under node (the handler fires on click), its CSS asserted (`body` colours/font, the placeholder, the
cursor), and — when Edge is present — computed styles read back from the real browser through the existing PixelLayout
harness. Windows-only / node-missing / Edge-missing SKIP, never fail.

### Task 8 — Gate
Fast subset (compare sorted failure NAMES with §0), then the named Integration fixtures: `WinFormsCatalogParityTests`,
`WinFormsCatalogSweepTests`, `FormDesignerAcceptanceTests`, `FormMenuAcceptanceTests`, `FormComponentAcceptanceTests`,
`FormBuildEmissionTests`, `FormAnchorEmissionTests`, `FormPropertyBatchAcceptanceTests`. Any new failure: re-run alone,
then A/B on a detached `origin/master` worktree.

## 5. Tests to re-check (from the plan's table, today's status)

| Test | Status at `14c2e17d` | Task |
|---|---|---|
| `FormRootRetargetTests.EveryFormRootRow_CrossesOrIsNamed` — RetargetPropertyLost arm | Reached only by MobileBreakpoint (Canvas); Properties-stored rows make it broad | 4 |
| `FormCssTests.NoDeclarationIsEmittedTwice` | ForeColor only | 3 (Font's five) |
| `FormPropertyGridDisplayTests.TheFormsRows_ComeFromFormRoot` | exact set | 4 |
| `FormPropertyGridTests.Rows_ForAComponent_…` (Name, Interval, Enabled) | Timer unchanged by slice 3 | none |
| `WinFormsCatalogSweepTests.SampleValue` / `FormRetargetTests.Sample` / `FormRootRetargetTests.Sample` | need arms | 3, 4 |
| `RegionWriter.GenerateInit` root-Degraded branch | unreachable | 4 |
| `WinFormsCatalogParityTests.TheSnapshot_Covers…` | no new kind in slice 3 | none |
| `FormPropertyGridViewTests.TheDocumentView_NoLongerCarriesTheGridsOwnBindings` | must stay green | 6 |

## 6. Traps that apply (repo-wide, restated where slice 3 touches them)
Catalog is the single source of truth (no hand lists); a WinForms row is unfalsifiable without csc (sweep + parity green);
fan-in (one statement per composite: `New Font(…)`, `New Padding(…)`); never `With`; ask the catalog what a value means;
ONE selection store; `[AvaloniaTest]` + Skia + same fixture id + a zoom ≠ 1.0 for real-view tests, windows disposed;
`dotnet clean` after AXAML; Edit/Write can store a backslash-u escape as the raw character — build such characters from
code points; mutation reverts with Edit then REBUILD; stage by name, never `git add -A`, never `csc.dll`.
