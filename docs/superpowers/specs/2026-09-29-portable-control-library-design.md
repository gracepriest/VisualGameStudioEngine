# The portable control library — piece 2 of "one form, either target" (design)

Status: REVISION 3, 2026-09-29 — revision 2 recorded the owner's answers (§0.3) and the first review (C1–C3, I1–I9);
revision 3 answers the re-review (R1 live docking, R2 the style cascade, R3 a plan-ready Decimal table, and its minors);
§11a lists where. Everything the decisions did not settle is
decided here as a reversible implementation choice (marked **[impl]**). Written against `origin/master` @ `14c2e17d`, which
contains piece 1 (PR #139, `6c62417e`).
Branch: `feat/portable-controls`, based on `origin/master` @ `14c2e17d`.

## 0. The programme and this piece

### 0.1 Programme decisions (owner — do not relitigate)
From `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §0: **P-D1** design in pixels · **P-D2** the web page
is desktop-exact, follows Anchor/Dock, stacks on phones · **P-D3 a portable control library — WinForms' API on the web
so one form's code-behind compiles for both targets (THIS PIECE)** · **P-D4** a Desktop | Web toolbar switch (piece 3)
· **P-D5** retire `.blwebform` with convert-on-open (piece 4) · **P-D6** desktop-only controls badged, a web build
containing one stops with an error naming it.

### 0.2 Piece-2 owner decisions (2026-09-29)
- **O1 — API scope = what the designer lists.** Every property and event the catalog lists for a control works at run
  time on the web. One list drives both targets.
- **O2 — old web code converts: OFFER ON OPEN, WRITE ON CONVERT.** New forms use the WinForms style from the start.
  Opening an old web form offers to convert; nothing is written until the user accepts. Converting rewrites the generated
  regions and simple handler signatures `(e As DomEvent)` → `(sender As Object, e As EventArgs)`; DOM-style lines inside
  handlers are NOT rewritten — each is listed in the Error List with a suggested replacement. Declining leaves the file
  byte-identical, and the designer does not regenerate it in the new style until accepted.
- **O3 — target-specific code.** `#If WEB` / `#If DESKTOP`, the symbol defined by the build's target; a web-only escape
  hatch `.Element` on every web control; `.Element` outside `#If WEB` is a DESKTOP-build error; a WinForms member not
  available on the web (e.g. `Form.ShowDialog`, an uncatalogued property) is a WEB-build error naming it and suggesting
  `#If DESKTOP`.
- **O4 — approach A.** A library of real BasicLang classes (`System.Windows.Forms`: a `Control` base, one class per
  catalog kind with a web tag, a web `Form`), shipped with the compiler and AUTO-INCLUDED in web builds by a new hook
  beside `WithJavaScriptDeclarations`. HAND-WRITTEN per kind, gated by a catalog-coverage test. Controls are real
  objects (passable, storable in lists).
- **O5 — one code shape on both targets.** `Private btnLogin As Button`; `AddHandler btnLogin.Click, AddressOf
  btnLogin_Click`; `Sub btnLogin_Click(sender As Object, e As EventArgs)`. On the web the generated init ATTACHES each
  control object to the element the page already rendered — the markup paints the initial design exactly as piece 1;
  code takes over after.
- **O6 — data flow.** Properties read and write the LIVE element (no shadow copy), following the catalog's web mapping.
  Run-time `Left/Top/Width/Height/Location/Size` respect Anchor like WinForms — FormAnchorCss's rules in the library, a
  MIRRORED PAIR with a lock-step test. Events map DOM → WinForms with `sender` = the control (click→Click;
  input→TextChanged; change→CheckedChanged/SelectedIndexChanged/ValueChanged; mouse→MouseDown/Up/Move with
  `MouseEventArgs`; keys→KeyDown/KeyUp (`KeyCode`) and KeyPress (`KeyChar`)). The library raises TextChanged/
  CheckedChanged itself on a programmatic set, matching WinForms' counts. GroupBox's default event is **Enter** → `focusin`.
  (O6's "Visible writes an explicit display" is SUPERSEDED by O10.)
- **O7 — rendering gaps fixed here.** CheckBox/RadioButton captions; GroupBox caption as a `<legend>` in the border;
  bordered Panels draw their border — the page matches WinForms, or the gap is re-recorded with measurements.
- **O8 — desktop-only controls.** Toolbox and property grid show a "desktop" badge; a web build using one stops with an
  error naming control and form.
- **O9 — testing** (§11): catalog-coverage gate; a BEHAVIOUR TWIN; an anchor lock-step test; rendering assertions in Edge;
  conversion; desktop-only; `#If`; `.Element`; end-to-end. Two reviews per task and a mutation pass, as piece 1.

### 0.3 Owner answers to revision 1's questions (2026-09-29) — decisions
- **O10 (Q1) — `Visible` uses the `hidden` ATTRIBUTE.** The phone layout (`.vgs-form [hidden] { display: none !important }`)
  and the reflow script (observes `hidden`) already honour it. Combined with §5.8's style-sheet rule (review I7).
- **O11 (Q2) — controls created at run time are OUT of this piece**: a clear web-build error, "not supported yet" (§5.10).
- **O12 (Q3) — methods on the web: all four groups.** `Show`/`Hide`/`Focus`/`BringToFront`; `TextBox.Clear`/`AppendText`/
  `SelectAll`; `MessageBox.Show` (the browser's dialog; buttons OK, OKCancel, YesNo only); `Form.Close` (hides the form
  area). Any other method is a web-build error.
- **O13 (Q4) — `Char` on the JavaScript backend** (a 2.0 item) so `KeyChar` is a real `Char` on both targets.
- **O14 (Q5) — NumericUpDown `Value` is `Decimal` on both targets**, converted EXACTLY to and from the page text (no
  floating point). Decimal is not supported on the JS backend today (M20) → a 2.0 item.
- **O15 (Q6) — a run-time `Dock` change RE-DOCKS LIVE** through piece 1's reflow script, which the library feeds CURRENT
  values (the script today reads emitted design sizes — review I7).
- **O16 (Q7) — `DESKTOP` is defined for every non-web backend.**
- **O17 (Q8) — the catalog's event lists DEPEND on property-grid slice 3 (in flight, `feat/property-grid-slice3`) and
  slice 5.** This spec does not add them (§10.1); slice 3's new rows (Font, Cursor, Padding, AcceptButton, CancelButton,
  TopMost, … — §10.2) are library scope under O1.
- **O18 (Q9) — editor support is IN 2.0**: the LSP understands `#If WEB`/`#If DESKTOP` (line-preserving blanking of
  inactive branches, a new feature) and knows the web controls (§4.12).
- **O19 (review I4) — `DEBUG` in Debug builds and `RELEASE` in Release builds**, like VB; a golden test over existing
  `#IfDef` fixtures and a release-notes line (§4.1).

### 0.4 What measuring found, and the sequencing it forces
The language is not yet able to host the library (§3): `#If` does not exist; no build defines a symbol; a class cannot
`Inherits` a class from another file on ANY backend; `Using` breaks `Me.Method()` on JavaScript; `RemoveHandler` is a
silent no-op on JavaScript; `MyBase.Property` recurses forever on JavaScript; a user `Enum` member types as `Object`;
derived-before-base dies at load; `Decimal` and `Char` are refused on JavaScript. These become sub-piece **2.0**.
**Sequencing:** `fix/js-cross-file-calls` (in flight, `wt-jsx`) rewrites the missing-member / .NET-fallback path that §4.2,
§4.3 and §5.11 depend on → **2.0 starts after it lands, and first re-measures M6, M7, M16 and the §3 listener rows on the
merged tree.** `fix/unknown-dock-diagnostic` (takes the next design code, adds `FormDock`) and property-grid slice 3 land
before 2a (§10).

## 1. Goal, and the delivery split

A form's code-behind — fields, wiring, handlers, and the property/event code users write — is the SAME text for a
WinForms build and a web build, apart from the designer's init region (piece 3 unifies that). On the web,
`btnLogin.Text = "Wait…"`, `chkRemember.Checked`, `AddHandler …Click`, `(sender As Object, e As EventArgs)` behave as they
do in the WinForms window: same values read back, same positions after a run-time move or re-dock, same events in the
same number. Existing web forms keep building unchanged until the user accepts a conversion.

The owner approved the whole design; the split is about delivery (each sub-piece: its own plan, per-task two-stage review,
mutation pass, gate).
**Order: [fix/js-cross-file-calls, fix/unknown-dock-diagnostic, slice 3 land] → 2.0a → 2a → 2b → 2c; 2.0b (exact
Decimal) runs in parallel with 2a and must land before 2b's NumericUpDown; 2d in parallel with 2b or 2c.** Slice 5 (event
lists) must land before 2b's gate is complete (§10.1).

| Sub-piece | Delivers | Why here |
|---|---|---|
| **2.0a Compiler + editor prerequisites** (§4.1–§4.10, §4.12–§4.13) | `#If`/`#ElseIf`/`#Else`/`#End If`; WEB/DESKTOP/DEBUG/RELEASE symbols on every route; the LSP's `#If` (line-preserving blanking) and web-library awareness; cross-file `Inherits`; JS: `Using` vs `Me.M()`, `RemoveHandler` identity, `MyBase.Property`, base-first class order, `Char`; Enum member typing; `AddHandler` signature check; BasicLang namespaces win on a web build; dotted `Namespace`; a library-idiom probe | Each is measured to break the library or the shared shape (§3). General fixes, each with its own tests. |
| **2.0b Exact Decimal on JavaScript** (§4.11) | The §4.11 table: every "in" row lowered and checked against a .NET-computed table, every "out" row refused by name; `D` literals and `CDec` typing on all targets | R3's table shows it is a numeric runtime of its own — its own plan and gate. Only NumericUpDown (2b) needs it. |
| **2a Library core + codegen + first kinds** (§5–§7) | Auto-include hook; `Control`, `Form`, event args, drawing types, collections, `MessageBox`; `Button`, `Label`, `TextBox`, `CheckBox`, `RadioButton`, `Panel`, `GroupBox`; the portable region style (marker, init, stubs); `.Element` and the web/desktop build errors; live geometry, Anchor, `hidden`, live Dock feed; the rendering fixes and client insets; the coverage gate and twin harness; **the opt-in route "Add Web Form (portable)"** (I1) | The login-form set proves every mechanism end to end, reachable from a shipping build. |
| **2b The remaining kinds** (§5.12) | `ComboBox`, `ListBox`, `PictureBox`, `LinkLabel`, `NumericUpDown`, `DateTimePicker`, `TrackBar`, `ProgressBar`, `Timer`, 3 strips, 4 item kinds; **then the default flips**: every new web form is portable and the opt-in command is retired | The default flips only when every web kind has a class. |
| **2c Conversion** (§8) | Offer on open; convert (regions, simple handlers, Using/Inherits, marker); DOM-line findings; decline = byte-identical; CLI verb | Needs the finished portable style to convert INTO. |
| **2d Desktop-only controls** (§9) | Badges, drop allowed on a web form, `DesktopOnlyKind` build error | Independent of the library. |

**I1 — how 2a is reachable [impl, the option chosen of the two the review offered].** 2a ships a bound command **"Add Web
Form (portable)"** beside the existing Add New Form route: it scaffolds a portable web form (§6.2) and is the only way to
make one in 2a. An entry-point test drives the real generated command and reads the AXAML for its binding (CLAUDE.md: a
`[RelayCommand]` no menu binds is unreachable). In 2b the ordinary route scaffolds portable and this command is removed.
The rejected option (flipping the default in 2a behind 2d's error) would have made a new web form's ComboBox a build error
for the length of 2b.

## 2. Grounded facts (today's master, `14c2e17d`)

Paths relative to the repo root. FCC = `BasicLang/Forms/FormControlCatalog.cs`, RW = `BasicLang/Forms/RegionWriter.cs`,
FAE = `BasicLang/Forms/FormAssetEmitter.cs`, JSB = `BasicLang/JavaScriptBackend.cs`.

### 2.1 Code-behind generation
| # | Fact | Where |
|---|---|---|
| G1 | Field type: web `WebScript?.FieldType ?? "Element"`, WinForms `WinFormsType ?? "Control"` | RW:1183-1191 |
| G2 | Fields written components first, then controls, `Private {Id} As {DeclaredType}` | RW:579-598 |
| G3 | Web init opens with `Dim doc As Document = ::document`; `Dim w As Window = ::window` only when a component has a web script | RW:612, :619-621 |
| G4 | **WinForms init order:** FormRoot rows as `Me.X = …` (:630-658) → components (:661-665) → controls via `AppendSiblings`, where each control is `New` → geometry → properties → `AddHandler` (binds) → its children (recursively) (:769-791), and after a run of siblings their `Controls.Add` in REVERSE (:753-756) or a host's item verb in document order (:736-750) → the `Me.<FormProperty>` line (MainMenuStrip) after the whole add run (:673-684) | RW:600-791 |
| G5 | Web per control: `{Id} = doc.getElementById("{Id}")`, then binds, then children; no adds (`AppendSiblings` returns early) | RW:773-775, :731 |
| G6 | Wiring: web `addEventListener("{dom}", AddressOf H)`, WinForms `AddHandler {Id}.{Event}, AddressOf H` | RW:987, :993 |
| G7 | BL8013 handler ordering is web-only, matched by NAME only | RW:478-517, :520-545 |
| G8 | `IsEmittedBind`: a web component is wired only on its declared web events | RW:437-452 |
| G9 | Regions: `' <vgs:designer region="controls|init" form="…" hash="…">` … `' </vgs:designer>`, regexes, hash, `Scan` (Canon/HashMismatch/Malformed); hand edit → BL8011, malformed → BL8012 | `BasicLang/Forms/RegionMarkers.cs:53-176`; RW:44-103 |
| G10 | Scaffold: WinForms writes three `Using` lines + `Inherits Form`; web writes neither; both `Me.InitializeComponent()`; web puts the init region LAST. It writes EMPTY regions, then runs `RegionWriter.Write` to fill them | `BasicLang/Forms/FormScaffolder.cs:195-254`, :172-185 |
| G11 | Handler stubs: web `(e As DomEvent)` (`()` when `WebHandlerTakesEvent` is false), WinForms `(sender As Object, e As {WinFormsEventArgs ?? "EventArgs"})`; placement web ABOVE the init region, WinForms AFTER it | `BasicLang/Forms/FormHandlers.cs:157-162`, :179-181 |
| G12 | Timer on the web: field `Integer`, construct `w.setInterval(AddressOf {handler}, {Interval})`, implied `Enabled=true`; `Enabled` is WinForms-only | FCC:1756-1778 (Enabled :1764) |
| G13 | Dispatch `Public Class VgsForms` with once-per-page guard; auto-start after `Main` unless user code calls/references it | FAE:931-969; JSB:1063-1150 |
| G14 | Design diagnostics reach the Error List via `DesignerDiagnosticsEvent`, source "Form designer" | `CodeEditorDocumentViewModel.cs:1729-1768`; `MainWindowViewModel.cs:582`, :1598-1619 |
| G15 | Design codes are allocated in `DesignDiagnostic.cs:54-84`; on master today the next free is BL8033 — but `fix/unknown-dock-diagnostic` takes BL8033 and slice 3 the next (C1). **This spec names its codes; numbers are assigned in the plan from `DesignDiagnostic.cs` on master that day.** | `BasicLang/Forms/DesignDiagnostic.cs:54-84` |
| G16 | Nothing checks a web handler's PARAMETERS; the only old-dialect recogniser is the import `DomDialect` | RW:520-545; `Forms/Recognizer/DomDialect.cs:114-115`, :145-147 |

### 2.2 The catalog
| # | Fact | Where |
|---|---|---|
| C1 | Every row declares exactly ONE event, its default ("the D1 event lists arrive in slice 5") | FCC:1350-1358 |
| C2 | `FormEventDef(Name, WinFormsArgs, WebEvent, Category, Description, IsDefault, OracleExemption)`; null `WebEvent` = WinForms-only | `Forms/FormEvents.cs:37-44` |
| C3 | `FormPropertyDef` incl. `Targets` (null = both), `HtmlAttribute`, `CssProperty`, `CssConverter`, `WinFormsFactory`, `IsItemCollection`, `WebDefault`, `OracleExemption` | FCC:154-171 |
| C4 | `FormControlDef` incl. `WinFormsType`, `HtmlTag`, `HtmlInputType`, `IsContainer`, `Place`, `WebScript`, `WebHandlerTakesEvent`, `HtmlChildrenWrapper`; `SupportsTarget(Web)` = `HtmlTag != null || WebScript != null` | FCC:1062-1110 |
| C5 | `Common(...)` appends Enabled, Visible, ForeColor, BackColor to every kind | FCC:1323-1333 |
| C6 | GroupBox's default event is Click on master; slice 3 changes it to Enter/`focusin` and keeps Click non-default WITH its oracle exemption (`[Browsable(false)]`) | FCC:1454-1462; `wt-pg3/docs/superpowers/plans/2026-09-29-property-grid-slice3-preflight.md:42-45`, :157 |
| C7 | 15 positioned web kinds, 3 strips, 4 items, 1 web component (Timer); 11 WinForms-only kinds | FCC:1370-1973; tests `FormCatalogCoverageTests.cs:186` |
| C8 | Web-only rows: RadioButton `GroupName`, ListBox `MultiSelect`; WinForms-only incl. NumericUpDown `DecimalPlaces`, DateTimePicker `Format`/`CustomFormat`/`ShowUpDown`, TrackBar `TickFrequency`/`Orientation`, ProgressBar `Minimum`/`Style` | FCC:1411-1438, :1515-1588 |
| C9 | NumericUpDown's rows are modelled Int (WinForms Decimal; "a decimal default written as 0.00 would not round-trip through Int") | FCC:1497-1511 |
| C10 | TextBox `Multiline`/`PasswordChar` have no web mapping | FCC:1377-1392 |
| C11 | Panel `BorderStyle` has no `CssProperty` | FCC:1443-1448 |
| C12 | FormRoot: Text, ClientSize, Cols/Rows, Gap, MobileBreakpoint; no events; slice 3 adds 18 root rows (FormBorderStyle, AcceptButton/CancelButton as a new `Reference` type, …) | FCC:1991-2039; slice-3 pre-flight :184-197 |
| C13 | Toolbox shows only `FormControlCatalog.For(Target)` — desktop-only kinds hidden on a web form; a drop refused BL8019 | `Shell/ViewModels/Designer/FormToolboxViewModel.cs:64`; `FormPlacement.cs:48-51` |
| C14 | Parity oracle `WinFormsCatalogParityTests` (stale exemptions fail) and compile sweep `WinFormsCatalogSweepTests` (every property/enum/default stub via real CLI + csc) | tests `WinFormsCatalogParityTests.cs:64-189`; `WinFormsCatalogSweepTests.cs:139-248`, samples :601-610 |

### 2.3 The page
| # | Fact | Where |
|---|---|---|
| P1 | CheckBox/RadioButton `Text` → `value=` on a bare `<input>`: caption never rendered | FAE:332-336 |
| P2 | ComboBox `Text` → `title=` | FAE:338-342 |
| P3 | GroupBox `Text` → a bare text node in `<fieldset>`; no `<legend>` | FAE:436-438 |
| P4 | `Enabled=False` → ` disabled` on ANY tag (does nothing on `<div>`, `<label>`, `<img>`, `<a>`, `<progress>`) | FAE:344 |
| P5 | Radio `name=` only from `GroupName`: radios in a Panel with no GroupName are not exclusive | FAE:383-388 |
| P6 | Visible=False → CSS `display:none` keyed on `#id` | FCC:1167-1169; `Forms/FormCss.cs:31` |
| P7 | A kind with no tag → an HTML comment, no diagnostic | FAE:277-284 |
| P8 | The reflow script: pure core `FormDockScript.Core` (:29-75), bootstrap (:78-109), observer on `.vgs-form` for `style`/`class`/`hidden` (:106); its data is `DataJson` (:142) of `FormDockNode`s built from the DOCUMENT (design sizes) | `Forms/FormDockScript.cs` |
| P9 | `FormAnchorCss.Positioned`/`Docked`; `Docked` mirrored by `vgsDockCss` and gated by `FormDockScriptTests.TheScriptsResolver_AgreesWithFormDockLayout_OnEveryFixture` | `Forms/FormAnchorCss.cs:94-132`; tests `FormDockScriptTests.cs:316` |
| P10 | A container's client size is its outer size ("the one rule") | `Forms/FormDockLayout.cs:268-274` |
| P11 | Recorded gaps: Panel FixedSingle insets children 1px, Fixed3D 2px (positioned AND docked); GroupBox docks inside +3/+19/+3/+3 but does not inset positioned children; web fieldset insets 2px; web Panels no border | `plans/2026-09-27-web-pixel-layout-task12-preflight.md:128-135`; `…-task13-preflight.md:140-141` |
| P12 | `FormDock` (in `fix/unknown-dock-diagnostic`) is THE parser of a positioned control's `DockStyle` (trimmed, case-insensitive, canonical spelling) | `wt-dock/BasicLang/Forms/FormDock.cs:3-40` |

### 2.4 The compiler and the web build
| # | Fact | Where |
|---|---|---|
| K1 | `WithJavaScriptDeclarations` APPENDS the one hard-coded `lib/js/dom-core.bli` on a JS target; every route passes through `CompileProjectFiles` | `BasicLang/Compiler.cs:538-559`, :237-238, :354 |
| K2 | No mechanism includes a BasicLang SOURCE library; the LSP never includes `dom-core.bli` and has NO preprocessor (no `Preprocessor` use under `BasicLang/LSP/`) | Compiler.cs; `BasicLang/LSP/` |
| K3 | `IDE/lib/js/dom-core.bli` is a hand-committed, load-bearing copy; no test keeps it equal to the source | `BasicLang/BasicLang.csproj:43`; `docs/wiki/content/js-backend.md:46-48` |
| K4 | `dom-core.bli` lacks `getBoundingClientRect`, `clientWidth`…, style `position`/`left`/…, event `button`/`relatedTarget`/`ctrlKey`/`code`…, and style-sheet access | `BasicLang/lib/js/dom-core.bli:32-95` |
| K5 | Preprocessor: `#Include`, `#Define`, `#IfDef`, `#IfNDef`, `#Else`, `#EndIf`, `#CppInclude`, `#JsImport` — no `#If`/`#ElseIf`/`#End If`; nothing predefines a symbol; `<DefineConstants>` never reaches the compiler; no CLI `--define`; a `#Define` line is not echoed (line numbers shift) | `BasicLang/Preprocessor.cs:221-308`, :194-197, :235-238; `Compiler.cs:186`; `ProjectFile.cs:313-315` |
| K6 | DORMANT `#If` machinery: the lexer produces `TokenType.PreprocessorIf` (`BasicLangLexer.cs:229`, :867) and a `PreprocessorIfNode` exists with visitors in the analyzer, IR builder and printer (`ASTNodes.cs:112`, :1864-1871; `SemanticAnalyzer.cs:8884`; `IRBuilder.cs:3222`; `ASTPrettyPrinter.cs:1174`) — but the parser never builds it (M9) | as cited |
| K7 | Any dotted or `System*`/`Microsoft*`/`Windows*` `Using`/`Imports` is marked .NET at PARSE time | `BasicLang/Parser.cs:714-720` |
| K8 | A base class is resolved through the type manager only; a sibling file's class is a symbol shell | `SemanticAnalyzer.cs:5907-5924`, :636-643 |
| K9 | JS events: a per-instance `new Set()`; `AddressOf` on an instance method → `.bind(recv)`, a NEW function each time | JSB:802-807, :3113-3132, :1505-1522 |
| K10 | JS classes emitted in dictionary order, no base-first sort; keyed by SIMPLE name; first class of a name wins in the merge | JSB:208-225; `IRBuilder.cs:1532`; Compiler.cs:986-991 |
| K11 | JS refusals: ByRef BL7002, Long BL7003, Char BL7004, value Structure BL7005, operator overloading BL7006, .NET BCL types BL7007 (Decimal among them, M20); no overloading | `BasicLang/JsCapabilityChecker.cs:34-50`, :304-308 |
| K12 | Parser: no `Friend` on class members; `Delegate` only unmodified at top level; no `Handles`; non-constant field initializers refused | `Parser.cs:866-942`, :118-119; `IRBuilder.cs:1930-1933` |
| K13 | Web forms: `FormDocumentLoader.LoadWebForms` → `FormDispatch.Write` → `JavaScriptEmitter.Emit(forms:)`, IDE and CLI mirrored | `ProjectSystem/Services/BuildService.cs:614-651`, :1017; `Program.cs:807-832`, :953-962 |
| K14 | A WinForms build: `EnableNetResolution` returns early, every WinForms member types as `Object` | Compiler.cs:145-146 |
| K15 | Opening a form: `OpenFileAsync` → `EnterDesignModeForFormDocument`; toasts with actions (`ShowNotification(…, actions)`), `IDialogService.ConfirmAsync` | `MainWindowViewModel.cs:2473-2526`, :2140-2146; `Core/Abstractions/Services/IDialogService.cs:9`, :133-137 |
| K16 | No `.bas`/`.cls`/`.mod` in the repo uses `#IfDef`/`#IfNDef`; the existing `#IfDef` coverage is in the test suite (e.g. `CppPassthroughTests.cs:205`, the only `Preprocessor.Define` caller) | repo grep |

### 2.5 Claims found FALSE
| # | Claim | Truth |
|---|---|---|
| F1 | CLAUDE.md "conditional compilation (`#If`/…)"; O3 "the build defines the symbol by target" | `#If` does not exist and no build defines a symbol (K5, M9). Built in §4.1. |
| F2 | O1's "every event the catalog lists" covers the O6 mouse/key events | One event per kind (C1) until slice 5 (O17). |
| F3 | "GroupBox's default is Enter" | Click on master; slice 3 changes it (C6). |
| F4 | Brief: WinForms handlers are user-written | The designer writes the stub (G11). |
| F5 | O7 lists every rendering gap | Also `Multiline`/`PasswordChar` (C10), radios (P5), disabled non-form elements (P4), ComboBox `title=` (P2). Fixed in §7. |
| F6 | "`Me.X()` is the safe spelling on JS" | Not once any `Using` line is present (M7). |
| F7 | Piece 1's "Visible must write a non-empty `style.display`" | Conflicts with phone stacking for containers; superseded by O10. |
| F8 | CLAUDE.md: an unqualified self-call is a bare global on JS; `Me.` inside a lambda hard-errors | NOT reproduced on `14c2e17d` (M22, M23) — likely fixed by #57/#200-era work. The library still uses `Me.` (§5.1); re-measured after `fix/js-cross-file-calls` (§0.4). |

## 3. Measured facts (2026-09-29)

Compiler built from this worktree (`dotnet build BasicLang/BasicLang.csproj -c Release`); probes under the session
scratchpad `p2probe\*`; node v22.19.0. Single file: `BasicLang.exe p.bas --target=javascript`; multi-file: a `.blproj`
(`<TargetBackend>JavaScript</TargetBackend>`) and `BasicLang.exe build Site.blproj`. Listener probes run with a small
prelude defining `document.getElementById` over node's `EventTarget`.

| # | Shape | Result |
|---|---|---|
| M1 | One file: `Event Click(sender As Object, e As …)` on a base `Control`, `RaiseEvent` from a base method, `AddHandler` on a derived `Button` instance | ✅ runs; `sender` is the control; added twice fires twice (as .NET) |
| M2 | …then `RemoveHandler btn.Click, AddressOf btn_Click` | ❌ silent no-op (4 calls where .NET makes 3): `delete` of a fresh `.bind` (K9) |
| M3 | `Overridable`/`Overrides`, `MyBase.Method()`, call through a base-typed variable | ✅ |
| M4 | `Overrides Property Text` using `MyBase.Text` | ❌ green; emits `this.Text` → `RangeError: Maximum call stack size exceeded` |
| M5 | Derived class declared above its base in one file | ❌ green; `ReferenceError: Cannot access 'Form' before initialization` |
| M6 | `Inherits Base` where `Base` is in another project file | ❌ "Unknown base class" on **JavaScript AND C#** (K8); cross-file `New`/calls ✅ |
| M7 | Any `Using` line + `Me.PrivateMethod()` | ❌ JS build fails ("no lowering for 'Me.Init'"); without `Using` ✅ |
| M8 | `Namespace System.Windows.Forms` | ❌ parse error; nested `Namespace System/Windows/Forms` parses, but the qualified name resolves as .NET (BL6016) |
| M9 | `#If WEB Then` / `#Else` / `#End If` | ❌ "Unexpected token '#If'"; "#Else without matching #IfDef". `#IfDef` parses |
| M10 | `Structure` with `Sub New` | ❌ parse error (any target); without it, on JS ❌ BL7005 |
| M11 | `EventArgs` on JS | ❌ BL7007 |
| M12 | `Enum Shade …`, `Dim k As Shade = Shade.Dark` | ❌ "Cannot assign … 'Object' … 'Shade'" on JS and C#; also the older IDE drop |
| M13 | `AddHandler` with a non-matching handler signature | ❌ green, runs with the wrong arguments |
| M14 | `AddHandler btn.Click, Sub(s, e) …` | ✅ |
| M15 | Handler declared AFTER the `AddHandler` wiring (BasicLang event) | ✅ builds and runs |
| M16 | A class named `F` calling `Me.Init()` | ❌ "Type 'F' does not have a member" (chip `task_ef845b99`) |
| M17 | `BasicLang.exe a.bas b.bas` | refused — use a `.blproj` |
| M18 | `Char` local `"a"c` on JS | ❌ BL7004 |
| M19 | `0.1D` (Decimal literal suffix) | ❌ parse error "End of statement expected, found 'D'" (any target) |
| M20 | `Dim d As Decimal = 0.1` on JS | ❌ BL7007 "'Decimal' is not available on the JavaScript backend"; on C# ✅. `CDec("0.1")` types as `Object` ("Cannot assign … 'Object' … 'Decimal'") |
| M21 | `Optional` parameters on a class method (`Describe()`, `Describe("y")`, `Describe("z", 7)`) | ✅ `x3`, `y3`, `z7` |
| M22 | Unqualified self-call `Unq()` in a constructor | ✅ emits `this.Unq();` — F8 |
| M23 | `el.addEventListener("click", Sub(e As DomEvent) Me.Hit())` and the unqualified `Hit()` form | ✅ both run — F8 |
| M24 | `el.addEventListener("click", AddressOf Me.VgsOnDomClick)` (and unqualified `AddressOf VgsOnDomClick`) with the Sub declared ABOVE | ✅ runs, handler sees `Me` |
| M25 | …with the Sub declared BELOW its use | ❌ "Argument 2: cannot convert from 'Pointer To Pointer To Object' to 'Action<DomEvent>'" — the BL8013 erasure, inside the library too |

## 4. Sub-pieces 2.0a and 2.0b — compiler and editor prerequisites

(§4.11 is sub-piece 2.0b; every other item here is 2.0a.)

Each item: the rule, then the test that pins it; verified through the CLI AND the IDE build (`CompileProjectFiles`), and
through the optimizer (`CompileToCppOptimized`) where a front-end change can reach C++. **2.0 begins by re-measuring M6,
M7, M16, M22–M25 on master after `fix/js-cross-file-calls` lands** (§0.4); an item that no longer reproduces is dropped with
the measurement recorded.

### 4.1 `#If` and the build symbols (O3, O16, O19)
- VB's form: `#If <cond> Then`, `#ElseIf <cond> Then`, `#Else`, `#End If`, nestable, beside the kept `#IfDef`/`#IfNDef`/
  `#EndIf`. `<cond>`: symbol names, `Not`, `And`, `Or`, `AndAlso`, `OrElse`, parentheses, `True`/`False` **[impl]** — no
  values or comparisons.
- Prefix traps: `#ElseIf` must not match `#Else`; `#End If` must not fall to the default arm (K5).
- **Symbols:** `WEB` when the backend is JavaScript, `DESKTOP` for every other backend (O16); `DEBUG` in a Debug
  configuration, `RELEASE` in a Release configuration (O19); plus `<DefineConstants>` **[impl]**. Defined in ONE place
  (compiler options → `Preprocessor.Define`) so the CLI file route, CLI project route, IDE build and debugger all get
  them; a test per route.
- **Dormant machinery (K6) [impl — DELETE]:** the preprocessor removes inactive branches before lexing, so the lexer's
  `PreprocessorIf` token, `PreprocessorIfNode` and its four visitors can never be reached; they are deleted (a green build
  proves no caller), rather than half-revived into a second, parse-time conditional-compilation path.
- Fix the `#Define` line-number shift (K5).
- **O19's golden test:** every `#IfDef`/`#IfNDef` fixture in the suite (K16) produces byte-identical output before and
  after the symbols exist, except where it names `DEBUG`/`RELEASE`/`WEB`/`DESKTOP`, which is listed. Release notes: "`DEBUG`
  and `RELEASE` are now defined by the build configuration, `WEB`/`DESKTOP` by the target — code under `#IfDef DEBUG` now
  compiles in Debug builds."
- Tests: every directive; nesting; unknown symbol = false; each symbol on each target/configuration through CLI and IDE;
  prefix traps; line numbers after `#Define` and inside skipped branches.

### 4.2 Cross-file `Inherits` (all backends)
A class may inherit a class declared in another file (incl. `.cls`/`.mod` and the library). Tests (JS + C#, CLI + IDE):
cross-file `Inherits`, `MyBase`, upcast, override dispatch, a three-file chain.

### 4.3 `Using` must not break `Me.M()` on JavaScript
M7's matrix builds and runs under node.

### 4.4 `RemoveHandler` removes (JavaScript)
`AddressOf recv.M` yields the SAME delegate identity for the same `(recv, M)` pair; added twice fires twice; removed once
leaves one. **[impl]** the event store is a list of `{receiver, method}` entries (a `Set` of bound functions cannot both
match identity and count duplicates). Tests: M2 yields 3; remove-never-added is a no-op; a lambda is removed only by the
same delegate value.

### 4.5 `MyBase.Property` (JavaScript)
Lowers to `super.P`. Test: M4 prints `B:hi`; a three-level chain.

### 4.6 Base-first class emission (JavaScript)
Topological over `Inherits`, stable otherwise. Test: M5 runs.

### 4.7 Enum member typing (all backends)
`Shade.Dark` has type `Shade` (M12) — in assignment, comparison, `Select Case`, arguments. The library's `Keys`,
`MouseButtons`, `DockStyle`, `AnchorStyles`, `BorderStyle`, `ContentAlignment`, `DialogResult`, `MessageBoxButtons`,
`FontStyle` depend on it.

### 4.8 `AddHandler` checks the handler against a BasicLang event (all backends)
A handler whose parameters are not assignment-compatible with the event's is an error naming both signatures
(`HandlerSignatureMismatch`, new compiler code). .NET events (typed `Object` on WinForms builds, K14) are unchanged.

### 4.9 BasicLang namespaces win on a web build
Dotted `Namespace` declarations parse (M8). On a JavaScript build, a `Using`/`Imports` or a qualified type name that names
a namespace DECLARED IN THE PROGRAM resolves to it before K7's .NET marking — so `System.Windows.Forms.Timer` and `Timer`
under `Using System.Windows.Forms` are the library's. C# builds unchanged.

### 4.10 `Char` on JavaScript (O13)
`Char` lowers to a one-character JS string; `"a"c` literals, `AscW`/`ChrW`/`Asc`/`Chr`, comparison (ordinal), `CStr`,
string concatenation, `Char` parameters/fields/`List(Of Char)`; conversions Char ↔ numeric only through the conversion
functions, as VB. BL7004 is retired (its tests become lowering tests). Test: a Char table run under node against the same
program's C# output.

### 4.11 `Decimal` on JavaScript, exactly (O14) — sub-piece 2.0b
**Representation [impl]:** a runtime class in the backend's prelude (`VgsDecimal`): a sign, a BigInt mantissa `< 2^96` and a
scale `0…28` — .NET's `System.Decimal` model, so trailing zeros are REPRESENTED (1.10 has scale 2). Generated code never
sees a raw BigInt: every operation is a prelude call.

**The rule for every row:** "in" = lowered, with a test whose expected values are COMPUTED BY .NET inside the test (the same
BasicLang source run on the C# target, or the C# expression evaluated in-process, under `InvariantCulture`) and compared
with node's output; "out" = refused at compile time with the named JS diagnostic `DecimalNotSupportedOnWeb` (message names
the construct and, where one exists, the supported spelling) — **never lowered wrong**.

| # | Construct | In / out | Rule and test |
|---|---|---|---|
| D1 | Literal `1.10D`, `5D`; an untyped numeric literal in a Decimal context (`Dim d As Decimal = 0.1`) | in | Front end, all targets: `D` suffix parses (M19). **C# output:** in a Decimal context C# already emits `0.1m` (measured, `p2probe\j3\p.cs`), so `0.1D` there emits the SAME bytes (asserted); in an inferred context (`Dim x = 0.1D`) the suffix makes `x` Decimal (`0.1m`) where `0.1` stays Double — intended, asserted. Scale is kept: `1.10D` has scale 2 |
| D2 | `Const c As Decimal = 1.5D` | in | folded as a Decimal constant |
| D3 | `+ − * /`, `Mod`, unary `−` | in | .NET semantics: `+`/`−` align scale; `*` adds scales then rounds to 28; `/` 28 significant digits, banker's rounding where .NET rounds (e.g. `1D / 3D`). Table ≥ 200 cases incl. scale edges, max/min values, negative zero |
| D4 | Integer division `\` on Decimal | **out** | VB converts `\` operands to `Long`, which the JS backend refuses (BL7003); suggestion `Math.Truncate(a / b)` |
| D5 | `=`, `<>`, `<`, `>`, `<=`, `>=` | in | VALUE comparison, scale-insensitive (`1.10D = 1.1D` is True) — never `===`. Table |
| D6 | `Select Case` on a Decimal | in | lowered through D5's comparison (values and `To`/`Is` ranges). Table |
| D7 | Implicit widening Byte/Short/Integer → Decimal | in | the analyzer inserts an exact conversion; mixed `Integer + Decimal` is Decimal |
| D8 | Decimal ↔ Double/Single, Decimal → Integer/Short/Byte | in, by the analyzer's EXISTING rule for the C# target (the same code is implicit or explicit on both targets — a JS-only rule would be a second type system) | `CDbl`, `CSng` nearest double; `CInt`/`CShort`/`CByte` banker's rounding as .NET (`CInt(2.5D) = 2`), out-of-range → `OverflowException`; Double → Decimal as .NET (`CDec(0.1)` = 0.1). Table |
| D9 | Decimal ↔ `Long` | out | BL7003 already refuses Long |
| D10 | Mixing that would reach BigInt/Number together | never reachable | a grid of every numeric type × every operator asserts each cell either lowers (and matches .NET) or is an analyzer error; no cell may produce a JS `TypeError` at run time |
| D11 | `ToString()`, `CStr`, `&`, interpolation `$"{d}"`, `String.Format("{0}", d)`, `Console.WriteLine(d)` | in | the SCALE is printed: `1.10D` → `1.10`, `1D/3D` → `0.3333333333333333333333333333`. ⚠ JS prints invariant (the backend's policy for Double); .NET's no-arg `ToString` uses the CURRENT culture, so the table runs .NET under `InvariantCulture` and the culture divergence is recorded (it is the Double divergence already) |
| D12 | Format specifiers (`d.ToString("N2")`, `{0:F2}`, `Format(d, …)`) | **out** | named, with "format the text yourself or use `Math.Round(d, 2).ToString()`" |
| D13 | `Math.Round(d)`, `Math.Round(d, n)` (banker's), `Math.Abs`, `Math.Min`, `Math.Max`, `Math.Floor`, `Math.Ceiling`, `Math.Truncate` | in | Table |
| D14 | `Math.Round` with a `MidpointRounding` argument; any other `Math`/`Decimal` member | out | named |
| D15 | `Decimal.Parse(s)`, `CDec(s)` | in | exact invariant parse (what NumericUpDown reads from the page); bad text → `FormatException`, out of range → `OverflowException`. Table |
| D16 | `Decimal.TryParse(s, result)` | out | its second parameter is ByRef, refused on JS (BL7002) — the existing diagnostic, with `Decimal.Parse` inside `Try` suggested |
| D17 | Decimal in `List(Of Decimal)`/arrays: `Add`, index, `For Each`, `Count` | in | |
| D18 | Decimal as a `Dictionary`/`HashSet` KEY; `List.Contains`/`IndexOf`/`Remove`/`Array.IndexOf` on Decimal | **out** | JS `Map`/`===` would compare object identity (`1.1D` ≠ `1.10D` ≠ another `1.1D`); refused rather than wrong |
| D19 | Boxing to `Object`, `CType(o, Decimal)`, `TypeOf o Is Decimal`, `o1 = o2` / `o.Equals(x)` with boxed Decimals | in | the backend's Object-typed `=`/`<>`/`Equals` route through a prelude helper that recognizes `VgsDecimal` (value comparison); unboxing checks the class (`InvalidCastException` otherwise). Table |
| D20 | Overflow (a result beyond ±79,228,162,514,264,337,593,543,950,335), division by zero | in | `OverflowException`, `DivideByZeroException` through the backend's exception prelude. Table |
| D21 | IR optimizer constant folding of Decimal expressions | in | the optimizer already folds Decimal constants (the C# probe shows `s = 0.1m + 0.2m` folded into one statement); it must fold EXACTLY (System.Decimal) or not at all — never through Double. Verified with the optimizing helpers and the CLI (CLAUDE.md) |
| D22 | Decimal fields, parameters, returns, properties, `Optional` Decimal parameters | in | |

BL7007 stops listing `Decimal` only for the "in" rows. Mutation checks: scale alignment, rounding mode (banker's vs away),
28-digit cut, `=` via `===`, overflow boundary, the invariant parse. **Why its own sub-piece:** 22 rows, a runtime numeric
class, an analyzer grid and an optimizer rule — as large as the rest of 2.0 together, and needed only by NumericUpDown.

### 4.12 The editor: `#If` and the web controls in the LSP (O18)
- **Line-preserving blanking** (new feature, own tests): the LSP runs the same preprocessor with the project's symbols
  and replaces every inactive-branch line with an EMPTY line, so every position the editor reports still maps to the
  user's file. Diagnostics, completion, hover and go-to-definition see only the active branch; the inactive branch is
  reported to the client for dimming **[impl: the existing semantic-tokens/decoration path, or none if the client has
  none]**.
- **Web projects:** the LSP includes `dom-core.bli` and, under §5.1's trigger, the portable library, so `btnLogin.`
  completes the library's members and `.Element` is known inside `#If WEB`.
- Tests: an LSP session over a file with `#If WEB`/`#Else` reports diagnostics at the right lines on a web and a desktop
  project; completion on a library control.

### 4.13 Library-idiom probe (review I6)
A committed probe program exercising the idioms the library relies on, run under node on every 2.0 change: a DOM
listener to an instance method via `AddressOf Me.VgsOnDom…` declared ABOVE its use (M24), a lambda with `Me.` (M23),
`Optional` parameters (M21), `RaiseEvent` from a base-class `OnX` (M1), a property override through `MyBase` (M4 fixed).
It also pins M25 (a later-declared Sub is refused) so the library's ordering rule stays grounded.

### 4.14 Not fixed here
Structure constructors / value Structures on JS (M10) — `Point`, `Size`, `Color`, `Padding`, `Font` are classes (§5.7);
chip `task_ef845b99` (M16).

## 5. The library

### 5.1 Shape, location, inclusion, idioms
- Source `BasicLang/lib/js/forms/*.bas` **[impl]**, copied beside `dom-core.bli` by the csproj and into `IDE/lib/js/forms/`
  (load-bearing, like `IDE/lib/js/dom-core.bli`); a test asserts the csproj copies every file.
- Namespaces `System` (`EventArgs`), `System.Drawing` (`Color`, `Point`, `Size`, `Image`, `Font`, `FontStyle`),
  `System.Windows.Forms` (the rest) — so the scaffold's three `Using` lines mean the same thing on both targets (§4.9).
- **The hook** `WithWebFormsLibrary(files)`, beside `WithJavaScriptDeclarations` on the same route, adds the library when
  the target is JavaScript AND a compiled source file has `Using`/`Imports System.Windows.Forms` or `System.Drawing`, **or
  a qualified reference `System.Windows.Forms.`/`System.Drawing.`** (a token scan) — the review's widening. Every existing
  web project, including every DOM-style form, compiles byte-for-byte as today (golden test).
- **Name collisions:** a program class with a library class's simple name, when the library is included, is an error
  naming both (`PortableLibraryNameCollision`) — never K10's silent first-wins.
- **DOM surface:** `dom-core.bli` extended (both copies in lock-step, a new equality test, K3) with rect/client metrics,
  `closest`/`contains`, positional style properties, event fields (`button`, `buttons`, `detail`, `relatedTarget`, `code`,
  `ctrlKey`/`shiftKey`/`altKey`), `getComputedStyle`, `alert`/`confirm`, and CSS-rule access for §5.8 (`CSSStyleSheet.
  insertRule`/`deleteRule`, `CSSMediaRule`, the rule's `style`).
- **Language constraints:** no overloading (Optional parameters, M21); no `Friend` (internals `Public` with a `Vgs` prefix,
  excluded from the gate); constructors assign (no non-constant initializers); no Structures.
- **Idioms (review I6), enforced by §4.13's probe:** a DOM listener that calls the instance is `AddressOf Me.VgsOnDom<Event>`
  to a `Private Sub VgsOnDom<Event>(e As DomEvent)` **declared ABOVE the method that attaches it** (M24; M25 refuses the
  reverse); every self-call is `Me.`-qualified (defensive, F8); lambdas are used only where M23 measured them. User code
  never sees a DOM listener.
- **Security:** `Text`, captions, legends, `Items.Add` and `MessageBox` text go through `textContent`/`createElement`/
  `createTextNode` — never `innerHTML`. A test sets `Text = "<b>x</b>"` on every text-bearing kind and asserts the literal
  characters are shown and no `<b>` element exists.

### 5.2 Classes
`System.EventArgs` (+ `Empty`); `System.Drawing.Color`, `Point`, `Size`, `Image` (`Image.FromFile`), `Font`, `FontStyle`;
`System.Windows.Forms.Control`, `Form`, `ControlCollection`, `ObjectCollection`, `Padding`, `Cursor`/`Cursors`,
`MessageBox`, `MessageBoxButtons` (OK, OKCancel, YesNo), `DialogResult`, `MouseEventArgs`, `KeyEventArgs`,
`KeyPressEventArgs`, `LinkLabelLinkClickedEventArgs`, `ToolStripItemClickedEventArgs`, enums `Keys`, `MouseButtons`,
`DockStyle`, `AnchorStyles`, `BorderStyle`, `ContentAlignment`, `PictureBoxSizeMode`; one class per web kind (C7). The
hierarchy follows WinForms where shared code can see it (`LinkLabel Inherits Label`; strip items share `ToolStripItem`,
which is not a `Control`). The WinForms-only kinds have NO class (§9). Slice 3's types (`Font`, `Padding`, `Cursor`) are
included for the rows slice 3 adds (§10.2).

### 5.3 Attaching
- A control object holds exactly its `Name` and its element reference; everything else is read from and written to the
  element (O6). State the DOM has no slot for lives ON the element as a `data-vgs-*` attribute (e.g. `PasswordChar`, a
  child's own `Enabled` under a disabled parent, `Anchor`, `Dock`).
- `New Button()` makes an unattached object; `Name` is assigned next (§6.2). `parent.Controls.Add(child)` attaches:
  `document.getElementById(child.Name)`, which, when the parent is attached, must lie inside the parent's element. A child
  may attach before its parent (G4: a Panel's children are added before the Panel). DOM listeners are attached here.
- Reading or writing a property of an unattached control throws `InvalidOperationException` naming it **[impl]**.
- `Form`'s constructor attaches to the page's form area (keyed by the `data-form` name the dispatch reads **[impl]**).
- `Element As Element` (read-only) on `Control`, `Form`, `ToolStripItem` (O3).

### 5.4 Data flow (the catalog's web mapping, made live)
Common members:

| Member | Web |
|---|---|
| `Text` | per kind; a DIFFERENT value raises `TextChanged` once |
| `Enabled` | the row's **enable target** (new catalog field, §7.8): `disabled` on the focusable element for form elements (`button`, `input`, `select`, `textarea`, `fieldset`, the CheckBox wrapper's inner input); for non-form elements (Label's `label`, LinkLabel's `a`, PictureBox's `img`, ProgressBar's `progress`, Panel's `div`) `aria-disabled="true"` + class `vgs-disabled` (greyed by the page CSS; LinkLabel's click is suppressed). A container propagates to its descendants, each child's own value kept in `data-vgs-enabled`; `child.Enabled` reads False while an ancestor is disabled (WinForms) |
| `Visible` | the `hidden` attribute (O10); `child.Visible` reads False while an ancestor is hidden (WinForms) |
| `ForeColor` / `BackColor` / `Font` / `Cursor` / `Padding` | §5.8's per-id style rules, through slice 3's CSS converters (one table, never a second); read back from the computed style |
| geometry | §5.9 |
| `Name`, `Parent`, `Controls` | the attached tree |

Per kind:

| Kind | Properties → element |
|---|---|
| Label, Button, LinkLabel | `Text` → `textContent`; `TextAlign` → `text-align` (horizontal, as `FormCssConverter.ContentAlignmentHorizontal`) |
| TextBox | `Text` → `value`; `ReadOnly` → `readOnly`; `MaxLength` → `maxLength` (reads 32767 when absent); `PasswordChar` (a `Char`, O13) → `type="password"` when set, kept in `data-vgs-passwordchar`; `Multiline` → the element is a `<textarea>` (§7.4); a run-time change swaps the element in place keeping id, classes, attributes and listeners **[impl]** |
| CheckBox | `Text` → the caption span; `Checked` → the inner input (raises `CheckedChanged` when it changes) |
| RadioButton | as CheckBox, plus `GroupName` → `name` (web-only row); checking one also raises `CheckedChanged` on the radio that became unchecked (the DOM fires `change` only on the new one) |
| ComboBox | `Items` → `<option>`s; `SelectedIndex` → `selectedIndex`; `Text` → the selected option's text (**a change from today's `title=`, P2**); setting `Text` selects the first matching option or none **[impl — a `<select>` holds no free text; recorded divergence]** |
| ListBox | `Items`, `SelectedIndex`; `MultiSelect` → `multiple` (web-only) |
| Panel | `BorderStyle` → the border (§7.3) |
| GroupBox | `Text` → the `<legend>` (§7.2) |
| PictureBox | `Image` → `src`; `SizeMode` → `object-fit`/`object-position` (Normal: none/top left; StretchImage: fill; Zoom: contain; CenterImage: none/center; AutoSize: natural size) |
| NumericUpDown | (needs 2.0b) `Minimum`/`Maximum`/`Value`/`Increment` are **`Decimal`** (O14): read by exact parse of the element's text, written by exact invariant format; `min`/`max`/`step` attributes likewise |
| TrackBar, ProgressBar | `Minimum`/`Maximum`/`Value` (Integer) → `min`/`max`/`value` (ProgressBar `Minimum` WinForms-only) |
| DateTimePicker | `Value` → `valueAsDate` |
| Timer | `Interval`, `Enabled` (a web row, §10.3); `Start`/`Stop`; `Tick`; runs only while Enabled |
| strips | `Visible`, `Dock` (read); `ItemClicked` (`ClickedItem`) |
| strip items | per their rows (FCC:1913-1973): `Text`, `ToolTipText` → `title`, `Visible`; `Click` |
| Form | `Text` → `document.title`; `ClientSize` (read); `Controls`; slice-3 root rows per §10.2 |

### 5.5 Methods (O12)
`Show()`/`Hide()` (= `Visible`), `Focus()`, `BringToFront()` (moves the element last among its siblings — DOM order is
paint order; a DOCKED control's z-order is also its docking order in WinForms, so BringToFront on one re-docks through
§5.10's feed); `TextBox.Clear()`, `AppendText(s)` (raises TextChanged once), `SelectAll()`; `MessageBox.Show(text,
Optional caption, Optional buttons)` → OK: `alert`, OKCancel/YesNo: `confirm` returning `DialogResult.OK`/`Cancel` or
`Yes`/`No` (browser dialogs have no title: the caption is the first line **[impl]**); `Form.Close()` → `hidden` on the form
area. Plus what the generated code and collections need: `Controls.Add/Remove/Count/Item`, `Items.Add/Insert/Remove/
RemoveAt/Clear/Count/Item/IndexOf/Contains`, `Timer.Start/Stop`, `Button.PerformClick`. Every other method — and every
`MessageBoxButtons` member other than the three — is §5.11's web-build error.

### 5.6 Events
- Declared on the class WinForms declares them on, raised through `Protected Overridable Sub OnX(e)` (M1, M3; an event
  can only be raised from its declaring class, JSB:745-746). `sender` is always the control.
- DOM → WinForms (the catalog's `WebEvent` column is the one list, §10.1):

| WinForms event | DOM source | Args |
|---|---|---|
| Click | `click` (a caption `<label>`'s second, synthetic click is not counted) | `EventArgs` |
| TextChanged | `input` (TextBox) and programmatic change | `EventArgs` |
| CheckedChanged | `change`, programmatic change, the radio that became unchecked | `EventArgs` |
| SelectedIndexChanged / ValueChanged | the row's `WebEvent`, and programmatic change | `EventArgs` |
| MouseDown / MouseUp / MouseMove | `mousedown` / `mouseup` / `mousemove` | `MouseEventArgs`: `Button`, `X`/`Y` in the control's client area, `Location`, `Clicks`, `Delta` |
| KeyDown / KeyUp | `keydown` / `keyup` | `KeyEventArgs`: `KeyCode` (a fixed `code`/`key` → `Keys` table), `Shift`/`Control`/`Alt`, `Modifiers`, `KeyData`, `Handled`, `SuppressKeyPress` (→ `preventDefault`) |
| KeyPress | a character-producing `keydown` | `KeyPressEventArgs`: `KeyChar As Char` (O13), `Handled` (→ not inserted) |
| Enter | `focusin` whose `relatedTarget` is outside the control | `EventArgs` |
| Tick | the library's `setInterval` | `EventArgs` |
| LinkClicked | `click` (navigation prevented) | `LinkLabelLinkClickedEventArgs` |
| ItemClicked | `click` on an item | `ToolStripItemClickedEventArgs` |

- **Counts and order match WinForms**, measured by the twin (§11.2): a CheckBox click raises CheckedChanged then Click; a
  programmatic set to the same value raises nothing; TextChanged per keystroke. A browser-forced divergence (NumericUpDown's
  per-keystroke `input`, FCC:1520) is asserted as a literal.
- **Until slice 5 lists them (O17)** the Mouse/Key/Enter events are implemented on `Control` per O6 and proven by the
  twin; the coverage gate (§11.1), which reads the catalog, covers each one the day it is listed — no library-side list is
  kept.

### 5.7 Value-like types
`Point`, `Size`, `Color`, `Padding`, `Font` are classes (M10); every getter returns a NEW object and every setter copies, so
.NET's copy semantics hold in shared code (`Dim p = btn.Location: p.X = 5` does not move the button). `Color`: the catalog's
named colours as `Shared ReadOnly`, `FromArgb(r, g, b, Optional a = 255)`, `R`/`G`/`B`/`A`, `Name`, `Equals`.

### 5.8 How the library writes style — rules, not inline style (review I7, O10)
Inline style would beat the phone media query (every run-time move would un-stack a phone layout) and every write would
fire the reflow script's MutationObserver (P8) — a full reflow per `Left` write. So:
- The library owns ONE `<style>` element on the page, holding **one `#id { … }` rule per touched control INSIDE the same
  `@media (width >= <breakpoint>px)` block the reflow script's live rules use** (unwrapped when the breakpoint is 0, piece-1
  task-10 decision 9). Run-time geometry, colours, font, cursor and padding are written into that rule. Below the
  breakpoint the phone layout wins, exactly as for the designed geometry.
- Visibility is the `hidden` attribute (O10) — honoured at every width by `.vgs-form [hidden] { display: none !important }`,
  which every PORTABLE page carries (Grid/Flow included; piece 1 put it only on Canvas pages). A design-time `Visible=False`
  is therefore emitted as `hidden` on a portable page (not `#id { display: none }`), so showing it is removing the attribute.
- Rule edits mutate no attribute, so they do not wake the observer. When a write can change docking — `Visible`/`Dock`, the
  requested size of a docked control, the size of a container that has docked children, `BringToFront` of a docked control
  — the library calls the page's reflow hook `request()` (§5.10). **One scheduler, one reflow:** the MutationObserver ALSO
  calls `request()` instead of reflowing directly, and `request()` schedules at most one reflow per microtask. So a
  `Visible` write — which wakes the observer (the `hidden` attribute) AND asks for a reflow — reflows ONCE; a test counts
  it.
- ⛔ **The cascade rule (R2): the library never writes a geometry property (`left`/`top`/`right`/`bottom`/`width`/`height`)
  for a control that is docked.** It records the request (§5.10's `data-vgs-w`/`data-vgs-h`) and asks for a reflow; the
  script's `vgs-dock-live` rule owns every geometry property of a docked control. The two `<style>` elements therefore never
  both write geometry for the same id, and their document order (both appended to `<head>`, `vgs-dock-live` lazily) does not
  matter. Non-geometry properties of a docked control (colours, font) are the library's; the script never writes them. A
  test docks, resizes and undocks a control in both orders of creation of the two style elements.
- **Performance assertion (Edge):** 100 `Left` writes in one Timer tick → at most one reflow, the tick completes in under a
  stated budget **[impl: the plan measures and fixes the number]**, and the final position is exact.

### 5.9 Run-time geometry and Anchor — the mirrored pair
- `Left`/`Top`/`Width`/`Height`/`Location`/`Size`/`Right`/`Bottom` READ the element's rectangle in its container's CLIENT
  coordinates (piece 1's harness measurement).
- WRITING re-derives the control's rule **by `FormAnchorCss.Positioned`'s axis rule**, with WinForms' semantics: the
  container size is its CURRENT client size and the offsets are the NEW bounds. A Right-anchored control moved to
  `Left = 50` gets `right: (Wc − 50 − w)px` and keeps its right distance from there; Left+Right writes both insets and the
  `calc(100% − …)` size; a centred axis `calc(50% ± …)`.
- `Anchor` is read/write; the element carries it as `data-vgs-anchor` (the page emitter writes it for every non-default
  Anchor) **[impl]**; writing it re-derives from current bounds.
- **Below the breakpoint** the page is stacked and anchors mean nothing (piece-1 §5), so a live measurement would compute
  insets against a phone column. There, geometry READS return the desktop-layout value (the control's rule / its requested
  bounds, `data-vgs-x`/`-y`/`-w`/`-h`), and WRITES compute the insets against the container's DESIGN client size (emitted
  as `data-vgs-cw`/`data-vgs-ch` on every container and the form area) and land in the media-guarded rule, taking effect
  when the viewport returns to desktop width **[impl]**. Recorded divergence: WinForms has no phone mode; the twin runs at
  desktop width, and an Edge test pins the phone-width behaviour.
- ⛔ **Mirrored pair** (C# `FormAnchorCss.Axis` ↔ the library), gated by §11.3. Invariant number formatting on both sides.

### 5.10 Live docking (O15, reviews I7 and R1)
Piece 1's script cannot re-dock a run-time change as it stands (measured in `BasicLang/Forms/FormDockScript.cs`):
`PageScript` returns null when nothing docks at design time (:115-123); `Nodes` drops every control that neither docks nor
holds something that docks (:163-167), so a design-time-undocked control has no node to dock; the script is a private IIFE
(:127-131), so nothing outside it can ask for a reflow; and its sizes are the EMITTED design sizes (`OwnSizeOf`, :169). For a
PORTABLE page (DOM-style pages keep piece 1's script byte-for-byte, §7.7):
- **Always emitted.** A portable page carries the script whether or not anything docks at design time.
- **The walk reads the LIVE tree.** Each reflow enumerates the live CHILD ELEMENTS of the form area and of every container
  (the positioned controls, in DOM order — DOM order is document order, and `BringToFront` changes it as WinForms' z-order
  changes docking order), reading per element: `data-vgs-dock` (the edge, spelled by **`FormDock`'s canonical rule**, P12 —
  never a second parser), the `hidden` attribute, the REQUESTED own size `data-vgs-w`/`data-vgs-h`, and the client-area
  facts (`data-vgs-client`, the §7.5 insets). The emitted JSON only SEEDS the first reflow (piece 1's B3 answers before the
  script has run); after that it is not consulted.
- **The requested size is the docking input, never the measured one.** A docked control's rendered box IS the reflow's
  output, so measuring it would be circular. The page emitter writes `data-vgs-w`/`-h` (and `-x`/`-y`) from the design; the
  library's size/position writes update them (and, for an undocked control, its §5.8 rule). `Dock = None` restores the
  requested bounds as a positioned rule — the twin checks that WinForms does the same.
- **Complete live rules.** A control docked at RUN time still has its designed positioned CSS in the page stylesheet
  (e.g. a stale `left` that, with `right` + `width` from a Right dock, over-constrains the box). So every live dock rule
  writes all six geometry properties, `auto` for those its edge does not set **[impl: a live-only completion step in both
  halves of the pair]**.
- **A named hook.** The bootstrap exposes `window.vgsDock = { request, reflow }` (no other global). The library reaches it
  through a typed declaration in a library-owned `lib/js/forms/vgs-page.bli` (`Extern Class VgsDockHook`), looked up
  LAZILY at the first call (`::window.vgsDock`) — so the order in which the page's inline script and App.js run does not
  matter, and a page without the hook (never for a portable page) fails loudly naming it.
- ⛔ **Mirror discipline.** `FormDockLayout` (C#) and the script's core change in ONE commit, gated by `FormDockScriptTests`'
  lock-step test. Its fixture table gains live cases, each compared with `FormDockLayout.Resolve(…, Runtime)` of the
  correspondingly edited document: **undocked at design time, docked at run time**; docked → `None`; a docked control's
  requested size changed; a container resized with docked children; a docked sibling hidden and shown; `BringToFront` of a
  docked control; each on a bordered Panel and a GroupBox (§7.5).

### 5.11 The build errors (O3, O11)
- **Web build, unavailable member** (`WebUnavailableMember`): a member on a library type the library does not declare, or an
  undeclared type in `System.Windows.Forms`/`System.Drawing` (e.g. `DataGridView`), or a `MessageBoxButtons` member beyond
  the three: *"'Form.ShowDialog' is a WinForms member that is not available on the web. Put desktop-only code inside
  `#If DESKTOP Then … #End If`."* A rewording of the analyzer's missing-member error and of BL7007 for these receivers —
  on the path `fix/js-cross-file-calls` rewrites (§0.4).
- **Web build, control created at run time** (`RuntimeControlCreation`, O11): `New` of a library control class anywhere
  except inside the designer's init region: *"Creating a Button at run time is not supported on the web yet."* A run-time
  throw on an unmatched `Controls.Add` remains as the backstop.
- **Desktop build, `.Element`** (`ElementOnDesktop`): on a non-JS build, `.Element` on an expression whose static type NAME
  is a library class name (a field `As Button`, a parameter, `Me` in a class that `Inherits Form`): *"'.Element' is web-only;
  wrap it in `#If WEB Then … #End If`."* Checked on the declared type name because WinForms members type as `Object` (K14);
  a receiver typed `Object` is left to csc (recorded).
- All are compile errors: Error List, CLI, LSP (§4.12). Numbers assigned in the plan (G15).

### 5.12 The kinds in 2b
Each by 2a's recipe (class, gate rows, twin fixture, Edge assertions). **Timer** replaces the web template (G12) for
portable forms: field `System.Windows.Forms.Timer`, constructed in the init region with its properties (§6.2's component
exception); the `FormWebScript` path stays for DOM-style forms. Strips and items attach by id; the host's item verb attaches
in DOCUMENT order, as G4. After the last kind: the scaffold's default flips (§1).

## 6. Code generation — the portable style

### 6.1 The style is RECORDED, never guessed (review C2)
- The hash-guarded region open marker carries it: `' <vgs:designer region="init" form="LoginForm.blwebform" style="portable"
  hash="…">` (both regions **[impl]**). A marker WITHOUT `style` is the DOM style — every existing web file. An unknown
  `style` value, or the two regions disagreeing, is `RegionMarkersMalformed` (BL8012) — the existing refusal, never a guess.
  `RegionMarkers`' regexes (`RegionMarkers.cs:60-66`) and `FormatOpen` learn the attribute; the hash is unchanged (it covers
  content, not the marker).
- The scaffold passes the style EXPLICITLY (portable from the §1 opt-in command in 2a, and by default from 2b) into the
  EMPTY regions it writes before calling `RegionWriter.Write` (G10) — so a brand-new form with no controls is portable
  although its regions contain nothing to detect.
- ⚠ **Older IDE drops refuse the new marker.** The open-marker regex takes the attributes in a STRICT order with no room
  for another (`RegionMarkers.cs:60-62`: `region=… form=… hash=…`), so an IDE or CLI from before 2a reads a
  `style="portable"` marker as `RegionMarkersMalformed` (BL8012) and refuses to regenerate — refusing, not corrupting.
  Recorded for the release notes ("a portable form needs this version or later"); the new regex accepts `style` in that one
  position only, and a test pins that a pre-2a marker (no `style`) still reads as today.
- WinForms files carry no `style` (always the WinForms shape). One reader, `FormCodeStyle.Of(file)`, is used by the region
  writer, the handler stubs, the designer's conversion offer, the conversion and the page emitter (§7.7).

### 6.2 The portable web code-behind (review C3, I5)
**Definition: the portable web init is the WinForms init walk (G4) with the geometry lines and the property lines deleted**
— FormRoot `Me.X = …` rows, per-control `Location`/`Size`/`Dock`/`Anchor`, per-control property lines, and the
`Me.<FormProperty>`/reference-row lines after the add run. **`x.Name = "x"` is written immediately after `New` on BOTH
targets.** Everything else — order included — is the WinForms walk. **Exception (I5): a tray component keeps its property
lines on the web** (a Timer has no markup; `Interval`/`Enabled` exist only in code).
```
Using System
Using System.Drawing
Using System.Windows.Forms

Public Class LoginForm
    Inherits Form

    ' <vgs:designer region="controls" form="LoginForm.blwebform" style="portable" hash="…">
    Private txtUser As TextBox
    Private btnLogin As Button
    ' </vgs:designer>

    Public Sub New()
        Me.InitializeComponent()
    End Sub

    ' <vgs:designer region="init" form="LoginForm.blwebform" style="portable" hash="…">
    Private Sub InitializeComponent()
        txtUser = New TextBox()
        txtUser.Name = "txtUser"
        btnLogin = New Button()
        btnLogin.Name = "btnLogin"
        AddHandler btnLogin.Click, AddressOf btnLogin_Click
        Me.Controls.Add(btnLogin)
        Me.Controls.Add(txtUser)
    End Sub
    ' </vgs:designer>

    Private Sub btnLogin_Click(sender As Object, e As EventArgs)
    End Sub
End Class
```
(The WinForms init for the same document is these lines plus `Me.ClientSize = …`, each control's `Location`/`Size`/property
lines after its `Name` line.)
- **The web scaffold `Inherits Form`** (O5: yes). **Field types** are the WinForms types for a portable file; components
  FULLY QUALIFIED as today.
- **Why no geometry/properties on the web:** the markup paints the design (O5), and re-applying geometry at init would
  capture anchor distances against the browser's current size, not the design size (§5.9; piece-1 §4).
- **The seam for piece 3:** the portable web init equals the WinForms init minus a set of lines that are each tagged, by
  the region writer, as geometry or property lines (component property lines excepted). Piece 3 wraps exactly that set in
  `#If DESKTOP`. A test pins the relation per catalog fixture (§11.6).
- **Handler stubs** (G11): the WinForms signature (`WinFormsEventArgs ?? "EventArgs"`) AND the WinForms placement (after the
  init region) for a portable file; the DOM signature and placement for a DOM-style file — switched by §6.1's style, not by
  target. The portable Timer stub is `(sender As Object, e As EventArgs)`.
- **BL8013 does not apply to a portable file** (M15); it stays for DOM-style files. The portable scaffold places the init
  region as WinForms does.
- The dispatch (G13) is unchanged.

### 6.3 Changed and unchanged expectations
- DOM-style files: regions, stubs, BL8013, the Timer template — byte-for-byte (golden test).
- **WinForms files: the new `Name` line changes every existing WinForms region's content, so its hash is rewritten on the
  next designer save** (not a hand edit — the old region is Canon, so it is regenerated). Every test with an expected WinForms
  region changes intentionally; the plan lists them. The WinForms sweep re-runs over it; it also closes chip
  `task_fa51e644` ("control.Name never emitted").

## 7. Page rendering fixes (O7, F5)

Catalog-driven — a row field read by the emitter, never a `control.Kind` switch — and applied to PORTABLE pages (§7.7).

### 7.1 CheckBox / RadioButton captions
A caption-wrapper field on the rows: the positioned element (id, geometry, `class="vgs-CheckBox"`) is a `<label>`
containing the `<input>` (margin 0) and a `<span>` caption (via `textContent`). Piece 1's invariants hold (id on the element
with the control's bounds). Assertions: caption present and inside the box; glyph left edge within 1px of the control's
left; a click on the caption toggles once and counts one Click.

### 7.2 GroupBox caption in the border
`<fieldset>` with `border: 0; padding: 0; margin: 0` (positioned children at stored X/Y relative to the box — WinForms does
not inset them, P11); the frame drawn by a pseudo-element; the caption a `<legend>` over the frame line **[impl; offsets
measured against the WinForms window]**. Docked children dock inside the dock padding (§7.5).

### 7.3 Bordered Panels
`BorderStyle` CSS: FixedSingle `1px solid`, Fixed3D `2px inset` **[impl; colours matched or recorded]**; with
`border-box`, children sit inside the border as WinForms places them (+1/+2, P11).

### 7.4 TextBox Multiline and PasswordChar
`Multiline = True` renders `<textarea>` (a second tag chosen by the property — a catalog field); `PasswordChar` set renders
`type="password"`.

### 7.5 Client area — one rule, with insets
Catalog-driven **[impl: `FormClientArea.Of(control)`]**: an **origin inset** (Panel by `BorderStyle` 0/1/2; GroupBox 0;
`ClientSizeOf` = outer − 2 × inset — the anchor reference) and a **dock padding** (GroupBox 3/19/3/3, P11; others 0).
Consumers, all calling the one rule: `FormDockLayout` (Designer and Runtime), the canvas, the page emitter, the reflow
script's core (§5.10), the library's anchor math (§5.9), and the WinForms region writer's Designer-resolved docked `Size`.
⚠ This MOVES children of bordered Panels and docked children of GroupBoxes on the `.blform` canvas by 1–19px —
intended; the plan lists every changed expectation. The 19px is WinForms' default-font value; another font/DPI is a recorded
divergence. (Slice 3's container `Padding` is excluded there because `FormDockLayout` does not model it — slice-3 pre-flight
:91-92; if slice 3 later adds it, it joins this rule.)

### 7.6 Radios, disabled containers, security
- A RadioButton with no `GroupName` gets `name` = its container's id (the form area's at top level), matching WinForms'
  per-container grouping (P5).
- A design-time `Enabled=False` renders per the enable-target rule (§5.4, §7.8), and a disabled container renders its
  descendants disabled with their own value in `data-vgs-enabled`.
- The emitter already escapes text; the §5.1 `<b>x</b>` test covers markup and run time alike.

### 7.7 DOM-style pages are unchanged
A DOM-style form's page is emitted byte-for-byte as piece 1 emits it (golden hash): its code addresses elements directly
(`chkRemember.checked`), and moving the id onto a `<label>` would make it read `undefined` from a green build. Conversion is
the gate to the new rendering. The emitter reads the style through §6.1 (the build knows each form's code-behind,
`FormCodeBehind.PathFor`).

### 7.8 Catalog fields added
`WebDisplay` is NOT needed (O10 replaced it). Added: the caption wrapper (§7.1), the Multiline tag (§7.4), `BorderStyle` CSS
(§7.3), client insets (§7.5), the **enable target** (§5.4), `DomMember` (§10.4).

## 8. Conversion (O2 — offer on open, write on Convert) — sub-piece 2c

### 8.1 The offer
Opening a DOM-style web form in the designer (`EnterDesignModeForFormDocument`, K15) shows a notification with **Convert** /
**Not now** (the existing `ShowNotification(…, actions)` toast) **[impl]**. On OPEN only, once per open, never on reload; a
refused document is not offered. Nothing is written by the offer.

### 8.2 Convert (one pure function, one write)
`FormCodeConversion.Convert(document, codeBehindText)` → new text + findings; the IDE writes once; the CLI exposes it as
`design --convert <form>` **[impl]** (both entry points tested).
1. Adds the three `Using` lines and `Inherits Form` if absent (a different `Inherits` → refused, nothing written).
2. Regenerates both regions in the portable style, with `style="portable"` on the markers (§6.1).
3. Rewrites the parameter list of each SIMPLE handler named by a bind — a `Sub` with exactly one `DomEvent` parameter, or
   none for a Timer — to `(sender As Object, <name> As <WinFormsEventArgs ?? EventArgs>)`, keeping the user's parameter
   name. Non-simple handlers (Function, extra parameters, lambda binds, a handler shared across different args) are left and
   reported.
4. Never edits a handler body; moves nothing.
5. A wired DOM-style Timer implied `Enabled=true` (G12): the conversion writes `Enabled="true"` on the component when it had
   a wired Tick (the BL8027 rule, as retarget applies it).

### 8.3 Findings (`DomUsageFinding`, warning)
Every DOM use outside the regions is listed with a suggestion derived from the catalog's `DomMember` (§10.4): `txtUser.value`
→ `txtUser.Text`; `lblMsg.textContent` → `lblMsg.Text`; `chk.checked` → `chk.Checked`; `x.disabled = True` → `x.Enabled =
False`; `x.style.display = "none"` / `x.hidden = True` → `x.Visible = False`; `e.target` → `sender`; `doc.getElementById("x")`
→ `x`; anything else → *"uses the DOM directly: wrap it in `#If WEB Then` and use `x.Element`"*. Recomputed on every
regeneration; they do not block the designer; the build will reject most of them anyway (§4.8 rejects an unconverted
`(e As DomEvent)` handler), so nothing runs differently in silence.

### 8.4 Declining
**Not now** writes nothing: the file stays byte-identical, the designer keeps generating it in the DOM style (§6.3), its page
keeps piece-1 markup (§7.7).

**A half-converted file is never built half-and-half.** The marker decides the style (§6.1), so a file whose markers say DOM
but which the user hand-edited toward the portable shape — detected by `Using`/`Imports System.Windows.Forms` or
`Inherits Form` in a web code-behind whose markers carry no `style` — would get a DOM-style region, a piece-1 page AND the
library (§5.1's trigger). That combination is refused: the designer reports `MixedCodeStyle` (error) naming the file and
offering Convert, the region writer does not regenerate it, and a web build stops with the same error. The reverse (a
`style="portable"` marker over a region hand-edited back to DOM calls) is already BL8011 (hash mismatch).

## 9. Desktop-only controls (O8, P-D6) — sub-piece 2d
- **Toolbox:** on a web form every kind is listed; desktop-only kinds carry a "desktop" badge (C13 changes from hide to
  badge). Dropping one is allowed (BL8019's refusal for a WinForms-but-not-web kind is removed).
- **Property grid:** the badge on the selected control's header, **read through the ONE selection store (`Selection`)** —
  never by setting `PropertyGrid.SelectedControl` from the view model (CLAUDE.md).
- **Canvas:** draws it from its catalog schematic.
- **Web build:** `DesktopOnlyKind` (error) — *"LoginForm: 'dgvUsers' is a DataGridView, which is desktop-only; a web build
  cannot include it."* — raised where web forms load for a build (K13), IDE and CLI. P7's comment is no longer reached by a
  successful build.

## 10. Catalog dependencies and changes (one list drives both)

### 10.1 Event lists — a DEPENDENCY (O17, review I2)
The catalog's per-kind event lists (Click, TextChanged, Mouse*, Key*, Enter, …) are property-grid **slice 5**'s work; this
spec adds none. Slice 3's pre-flight B1 (a non-default bind is dropped by `ConvertBinds`/retarget) is slice 3's to fix, and
matters here because every new event is non-default.

### 10.2 Slice 3 lands first — its rows are library scope (O1, O17)
Slice 3 (`feat/property-grid-slice3`) brings: GroupBox's default event Enter/`focusin` with Click kept non-default and
exempt (C6 — **this spec no longer changes GroupBox; it implements slice 3's rows**, incl. §5.6's relatedTarget rule for
Enter); new types `Font`, `Padding`, `Cursor` and their rows (Font/Cursor per kind as the snapshot lists them; Padding on
Label/Button/CheckBox/RadioButton/LinkLabel); the 18 FormRoot rows (FormBorderStyle, AcceptButton/CancelButton as
`Reference`, TopMost, Opacity, BackColor/ForeColor/Font, …); `Double` and `Reference` types; the CSS list API
(`FormCss.Declaration` returning several declarations). **Under O1, every such row that APPLIES TO THE WEB gets a library
member**, whose run-time mapping reuses slice 3's CSS converters and `FormCursors.CssFor` (one table); a row slice 3 marks
WinForms-only is `WebUnavailableMember` in shared code. A web root row with no honest page meaning (e.g. a web `TopMost`)
is slice 3's call to make WinForms-only, not this spec's to fake. AcceptButton/CancelButton, if slice 3 makes them web rows,
map to Enter/Escape inside the form area clicking the referenced button (the twin checks it).

### 10.3 Timer `Enabled` on the web
The Timer's `Enabled` row applies to the web (G12), so a portable Timer runs only when enabled, as WinForms.

### 10.4 `DomMember` per property
Each property row records the live DOM member its web value lives in (`value`, `textContent`, `checked`, …). Read by the
conversion's suggestions (§8.3) and by the node stub tier of the gate (§11.1). ⚠ That tier asserts ONLY what `DomMember`
states; **Edge remains the gate that can fail** on real rendering and behaviour.

## 11. Testing

Edge / WinForms-window tests: `[Category("Integration")]`, Windows-gated, SKIP with a reason where absent. Node-tier tests run
everywhere.

### 11.1 Catalog-coverage gate (O4, O9)
Catalog-driven (no hand `[TestCase]` list): per web kind, a generated shared program that sets every property applying to
BOTH targets to the sweep's sample value (C14), reads it back and prints it, and wires every listed event.
- **Compiles** for desktop (real CLI `--target=csharp` + csc) and web (real CLI, JavaScript).
- **Runs** both and reads values back: WinForms reference window (Task 12 harness) and **Edge (Task 13 harness) — the
  authority**; plus a fast node tier with a recording stub DOM that asserts only each property's `DomMember` write.
- **Events:** each listed event triggered once per side; handler runs once, `sender` = the control.
- Web-only rows run on the web inside `#If WEB`; WinForms-only rows in shared code are `WebUnavailableMember`, asserted by name.
- Slice 3's and slice 5's rows join automatically when they land.

### 11.2 The behaviour twin (O9, review I9)
A pixel `.blform` and its Canvas `.blwebform` twin (piece 1's retarget), each with a code-behind whose **NON-REGION text is
byte-identical** (asserted first — the regions legitimately differ until piece 3), run in the WinForms window and in Edge by
one neutral scenario: set Text; toggle Visible/Enabled/Checked; click; type; select; move a Right-anchored control; change a
Dock; resize a container with docked children; resize the window. Compared: values read back, every control's rectangle
(±1px, form-client coordinates), and the EVENT LOG (sender, event, key args) — order and counts.
- Real input both sides **[impl — the riskiest harness decision]**: WinForms by window messages (`WM_LBUTTONDOWN`/`UP`,
  `WM_KEYDOWN`/`WM_CHAR`/`WM_KEYUP`); Edge by the DevTools protocol (`Input.dispatchMouseEvent`/`dispatchKeyEvent`/
  `insertText`, trusted). Synthetic `dispatchEvent` only where CDP cannot express a step, each such step named.
- Recorded divergences asserted as literals (NumericUpDown per-keystroke ValueChanged; the centring half pixel; MessageBox's
  missing title).

### 11.3 Lock-step (O6, O15)
The library's anchor axis rule under node vs `FormAnchorCss.Positioned` over one table (16 edge combinations × near/far/
centred × negative sums × sizes ≤ 0); the client-inset rule and every §5.10 live case — **undocked at design time, docked at
run time** first among them — vs `FormDockLayout` through `FormDockScriptTests`, plus the script's glue harness on a real
CLI-built portable page (a page with NOTHING docked at design time still carries the script and the `vgsDock` hook); the
one-reflow-per-`Visible`-write count; the R2 cascade test.

### 11.4 Rendering and style (Edge)
Caption, legend, borders and child offsets vs the WinForms window (P11's table becomes equality, or each residual gap a
literal); radios exclusive per container; enable targets; `<textarea>`/password; `hidden` at desktop and phone widths
(incl. a design-hidden Panel shown on a phone); run-time geometry below the breakpoint does not un-stack; the §5.8
performance assertion; `<b>x</b>` shown literally.

### 11.5 Compiler and editor (2.0)
Per §4, on JS and C#, CLI and IDE; the Decimal .NET-computed table; the Char table; the LSP blanking session; the §4.13 probe;
O19's golden test.

### 11.6 Code generation and conversion
Portable region golden files per kind; the style marker (scaffold writes it; malformed → BL8012; absent → DOM); the
web-init = WinForms-init-minus-tagged-lines relation per catalog fixture, component exception included; DOM-style regions,
stubs and pages byte-for-byte; the inclusion trigger (with and without `Using`, qualified-only); the WinForms `Name` line;
conversion (convert, each finding, non-simple handlers left, decline byte-identical, CLI = IDE bytes).

### 11.7 Build errors
`DesktopOnlyKind` on both routes; `ElementOnDesktop` (field, parameter, `Me`); `WebUnavailableMember` (member, type,
MessageBoxButtons member); `RuntimeControlCreation`; `HandlerSignatureMismatch`; `PortableLibraryNameCollision`; `MixedCodeStyle` (designer and web
build); `DecimalNotSupportedOnWeb` (one test per "out" row of §4.11); `#If`
choosing correctly per target and configuration.

### 11.8 End to end, and reachability
Through the real IDE view (headless): "Add Web Form (portable)" (2a; the ordinary route in 2b) → drop controls →
double-click a Button → write a handler using only the portable API → build web (CLI and IDE) and desktop → run both: page
(Edge) and window show the same result. For each new piece, the test drives its shipping entry point: the hook (a real
build), the command (generated command + AXAML binding), the conversion offer (`OpenFileAsync`), the CLI verb, the badges.

### 11.9 Reviews and mutations
Per task: implementer, spec review, quality review, mutation checks — inclusion trigger; base-first order; RemoveHandler
identity; `#If` prefix traps; DEBUG/RELEASE by configuration; line-preserving blanking; Decimal rounding/scale; the anchor
axis (both sides); live-dock reads (a walk over the emitted JSON instead of the live tree; measured instead of requested
size); reflow coalescing (observer + library = one); the R2 cascade rule; the phone-width anchor rule; client insets; relatedTarget; caption click de-dup; change-only
events; the style marker; decline byte-identity; the `.Element` receiver-name check; the harness itself (never shown, wrong
order of input).

## 11a. Review resolution (revision 2)
| Finding | Where resolved |
|---|---|
| C1 code collisions | G15; codes named throughout, numbers in the plan |
| C2 style detector | §6.1 (marker `style="portable"`, BL8012, scaffold passes it) |
| C3 init order | G4; §6.2 definition + corrected sample; `Name` after `New` on both |
| I1 reachability | §1 (opt-in command in 2a, default flip in 2b); §11.8 |
| I2 slice-3 overlap | §10.1–10.2 (dependency; GroupBox left to slice 3) |
| I3 LSP | §4.12 |
| I4 DEBUG/RELEASE | O19; §4.1 |
| I5 components | §6.2 exception; §5.12 |
| I6 library traps | §3 M21–M25; §4.13; §5.1 idioms; F8 |
| I7 phone + reflow | §5.8, §5.10; O10, O15 |
| I8 sequencing | §0.4; §4 preamble |
| I9 twin text | §11.2 |
| R1 live docking | §5.10 (always emitted; live-tree walk; requested size `data-vgs-w/h`; complete rules; named lazy hook; lock-step incl. undocked→docked); §11.3 |
| R2 style cascade | §5.8 (the library never writes geometry for a docked control) |
| R3 Decimal | §4.11 table D1–D22, `DecimalNotSupportedOnWeb`; split out as 2.0b (§1) |
| Re-review minors | one scheduler per reflow (§5.8); anchor writes below the breakpoint (§5.9); pre-2a drops read the marker as BL8012 (§6.1); half-converted files refused, `MixedCodeStyle` (§8.4) |
| Minors | G11 (`WinFormsEventArgs`, placement by style §6.2); P2/§5.4 ComboBox `title=`; §5.4 enable targets; §5.1 security; §5.1 trigger widened; §6.3 WinForms rehash; §5.10 `FormDock`; §9 `Selection`; §10.4/§11.1 Edge authority; O2 wording |

## 11b. Notes for the plan (from the final spec review — approved, non-blocking)

1. **The live walk visits CONTROLS only** (§5.10): an element counts when it carries a control id (or a `vgs-<Kind>`
   class) — never the CheckBox/RadioButton `<input>` or caption `<span>` inside its `<label>`, the GroupBox `<legend>`, or a
   strip's `<ul>`/`<li>`, which are parts of one control, not siblings. Lock-step fixture: a GroupBox holding a docked child,
   proving the legend is not counted as a docked sibling (the child docks inside the 3/19/3/3 padding exactly as
   `FormDockLayout` says).
2. **The "complete rules" step lives in the CSS pair** `FormAnchorCss.Docked` ↔ `vgsDockCss` (§5.10). The same-commit rule
   and the lock-step test name those two as well as `FormDockLayout` and the script's core — four files, one commit, one
   gate.
3. **The token scans skip comments and string literals**: §5.1's library-inclusion trigger and §8.4's `MixedCodeStyle`
   detection read tokens, not text, so `' Using System.Windows.Forms` or `"System.Drawing."` inside a string triggers
   neither. A test for each.
4. **Intended divergence, recorded:** below the breakpoint a geometry READ returns the stored requested bounds
   (`data-vgs-x/-y/-w/-h`, §5.9), while at desktop width after a window resize it MEASURES the live box. The two differ for an
   anchored control (the live box has followed its anchors; the stored request has not). Documented in the library's
   geometry members and pinned by an Edge test at both widths.

## 12. Risks
- **The JS backend is the library's substrate**; 2.0 fixes what was measured, the library will find more. The library runs
  under node from its first commit and every behaviour is pinned by the twin, never by strings.
- **In-flight branches move the ground**: `fix/js-cross-file-calls` (the missing-member path), `fix/unknown-dock-diagnostic`
  (codes, `FormDock`), slice 3 (rows, types, GroupBox), slice 5 (events). §0.4's re-measure step; codes by name; slice-3 rows
  consumed, not duplicated.
- **Exact Decimal on JS** (2.0b) is a numeric runtime of its own; wrong rounding would be a silent miscompile. The
  .NET-computed tables, the "out" rows refused by name, and the mutations are the guard; the IR optimizer's folding (D21) is
  a second place it can go wrong.
- **The reflow script becomes live** (§5.10): one half of piece 1's mirrored pair changes, it is now on every portable page,
  and it gains a global (`window.vgsDock`); its lock-step fixtures must grow in the same commit.
- **Two style owners** (`vgs-dock-live` and the library's `<style>`): safe only while §5.8's R2 rule holds — a mutation
  check writes a docked control's `left` from the library and must turn a test red.
- **A hand-written library drifts from the catalog** — §11.1, `DomMember`, the collision error.
- **Harness fidelity** — CDP and window messages are new; mutation checks on the harness itself.
- **Canvas expectations move** (§7.5), on WinForms documents too — listed, intended.
- **Every WinForms region rehashes** once (§6.3) — a large, intentional expectation diff.
- **The IDE drop**: `IDE/lib/js/forms/` is load-bearing and hand-refreshed; a stale drop ships an old library green. The drop
  step lists it; an Integration test compares drop and source on the gate run.
- **Size**: the whole library ships in every opted-in page (no tree-shaking).

## 13. Remaining open questions
None blocking. Two reversible choices the owner may want to see: the opt-in command name "Add Web Form (portable)" for 2a
(§1), and `MessageBox`'s caption shown as the dialog's first line (§5.5).

## 14. Out of scope, and seams left
- **Piece 3**: §6.2's tagged-line relation is the seam — the only target-specific lines a portable file has are the WinForms
  init's geometry/property lines.
- **Piece 4**: the conversion function and the style marker are reusable by convert-on-open; nothing assumes `.blwebform`
  persists.
- Run-time-created controls (O11); WinForms → web retarget still producing Grid; tree-shaking; per-control phone overrides;
  desktop-only kinds on the web (P-D6's "web versions arrive over time"); the catalog event lists themselves (slice 5).
