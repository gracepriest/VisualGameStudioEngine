# The portable control library — piece 2 of "one form, either target" (design)

Status: DRAFT for spec review, 2026-09-29. Implements the owner's piece-2 decisions of 2026-09-29 (§0.2) exactly;
everything the decisions did not settle is either decided here as a reversible implementation choice (marked
**[impl]**) or listed as an owner question (§13). Written against `origin/master` @ `14c2e17d`, which contains piece 1
(PR #139, `6c62417e`).
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
- **O2 — old web code converts on save, by offer.** New forms use the WinForms style from the start. Opening an old web
  form OFFERS to convert: generated regions rewritten; simple handler signatures `(e As DomEvent)` →
  `(sender As Object, e As EventArgs)`; DOM-style lines inside handlers are NOT rewritten — each is listed in the Error
  List with a suggested replacement. Declining leaves the file byte-identical, and the designer does not regenerate it
  in the new style until accepted.
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
  control object to the element the page already rendered — the markup still paints the initial design exactly as
  piece 1; code takes over after.
- **O6 — data flow.** Properties read and write the LIVE element (no shadow copy), following the catalog's existing web
  mapping. `Visible` writes an explicit non-empty display. Run-time `Left/Top/Width/Height/Location/Size` respect Anchor
  like WinForms — FormAnchorCss's rules in the library, a MIRRORED PAIR with a lock-step test. Events map DOM →
  WinForms with `sender` = the control (click→Click; input→TextChanged; change→CheckedChanged/SelectedIndexChanged/
  ValueChanged; mouse→MouseDown/Up/Move with `MouseEventArgs`; keys→KeyDown/KeyUp (`KeyCode`) and KeyPress
  (`KeyChar`)). The library fires TextChanged/CheckedChanged itself on a programmatic set, matching WinForms' counts.
  GroupBox's default event is **Enter** → web `focusin`.
- **O7 — rendering gaps fixed here.** CheckBox/RadioButton captions rendered; GroupBox caption as a `<legend>` in the
  border; bordered Panels draw their border — the page matches WinForms, or the gap is re-recorded with measurements.
- **O8 — desktop-only controls.** Toolbox and property grid show a "desktop" badge; a web build using one stops with an
  error naming control and form.
- **O9 — testing** (§11): catalog-coverage gate; a BEHAVIOUR TWIN (the same form code in the real WinForms window and in
  Edge); an anchor lock-step test; rendering assertions in Edge; conversion; desktop-only; `#If`; `.Element`;
  end-to-end. Two reviews per task and a mutation pass, as piece 1.

### 0.3 What this spec found that changes the shape of the work
Measuring before designing (§3) found that **the language is not yet able to host the library**: `#If` does not exist
(only `#IfDef`, and no build defines any symbol); a class cannot `Inherits` a class from another file on ANY backend; any
`Using` line breaks `Me.Method()` on JavaScript; `RemoveHandler` is a silent no-op on JavaScript; `MyBase.Property`
recurses forever on JavaScript; a user `Enum` member types as `Object`; and a derived class declared before its base
dies at load. Each is a green build or a clear refusal of the exact shape the library and the shared code-behind need.
They become sub-piece **2.0** (§4), which lands first and is valuable on its own.

## 1. Goal, and the delivery split

A form's code-behind — fields, wiring, handlers, and the property/event code users write in handlers — is the SAME text
for a WinForms build and a web build. On the web, `btnLogin.Text = "Wait…"`, `chkRemember.Checked`, `AddHandler …Click`,
`(sender As Object, e As EventArgs)` behave as they do in the WinForms window: same values read back, same positions after
a run-time move, same events in the same number. Existing web forms keep building unchanged until the user accepts a
conversion.

The owner approved the whole design; the split is about delivery (each sub-piece: its own plan, per-task two-stage review,
mutation pass, gate). **Order: 2.0 → 2a → 2b → 2c; 2d may run in parallel with 2b or 2c.**

| Sub-piece | Delivers | Why here |
|---|---|---|
| **2.0 Compiler prerequisites** (§4) | `#If`/`#ElseIf`/`#Else`/`#End If` + WEB/DESKTOP symbols on every entry point; cross-file `Inherits`; `Using` vs `Me.M()` on JS; `RemoveHandler` identity on JS; `MyBase.Property` on JS; Enum member typing; base-first class order on JS; `AddHandler` signature check; BasicLang namespaces win over .NET for `Using`/qualified names on a web build; dotted `Namespace` declarations | Every one is measured to break the library or the shared code shape (§3). All are general compiler fixes with their own tests, independent of forms. |
| **2a Library core + codegen + first kinds** (§5–§7, §10) | Auto-include hook; `Control`, `Form`, event-args family, `Color`/`Point`/`Size`/`Image`, collections; `Button`, `Label`, `TextBox`, `CheckBox`, `RadioButton`, `Panel`, `GroupBox`; the portable region-writer style; `.Element` + both build errors; run-time geometry with Anchor + lock-step; catalog event lists + GroupBox Enter + `DomMember`; the three rendering gaps + client insets; the coverage gate and behaviour-twin harness | The login-form set, end to end, proves every mechanism once. |
| **2b The remaining kinds** (§5.8) | `ComboBox`, `ListBox`, `PictureBox`, `LinkLabel`, `NumericUpDown`, `DateTimePicker`, `TrackBar`, `ProgressBar`, `Timer`, the three strips and four item kinds; **then** the web scaffold's default flips to the portable style | Mechanical once 2a's gate exists; each kind is one gated row. The default flips only when every web kind has a class (else a new form's ComboBox would be a web-build error). |
| **2c Conversion** (§8) | Style detector, convert-on-open offer, handler rewrite, DOM-line findings, decline = byte-identical, CLI verb | Needs the finished portable style to convert INTO. |
| **2d Desktop-only controls** (§9) | Badges, drop allowed on a web form, the web-build error | Independent of the library. |

## 2. Grounded facts (today's master, `14c2e17d`)

Paths relative to the repo root. FCC = `BasicLang/Forms/FormControlCatalog.cs`, RW = `BasicLang/Forms/RegionWriter.cs`,
FAE = `BasicLang/Forms/FormAssetEmitter.cs`, JSB = `BasicLang/JavaScriptBackend.cs`.

### 2.1 Code-behind generation
| # | Fact | Where |
|---|---|---|
| G1 | Field type: web `WebScript?.FieldType ?? "Element"`, WinForms `WinFormsType ?? "Control"` | RW:1183-1191 (web :1187) |
| G2 | Fields written components first, then controls, `Private {Id} As {DeclaredType}` | RW:579-598 |
| G3 | Web init opens with `Dim doc As Document = ::document` UNCONDITIONALLY; `Dim w As Window = ::window` only when a component has a web script | RW:612, :619-621 |
| G4 | Per control: web `{Id} = doc.getElementById("{Id}")`; WinForms `New`, geometry, properties | RW:769-791 (web :775) |
| G5 | WinForms geometry fans in: `Location = New Point`, `Size = New Size`, `Dock = DockStyle.X`, `Anchor` | RW:1021-1068 |
| G6 | `Controls.Add` reversed (z-order); returns early on the web | RW:721-757 (web :731, reversal :753-756) |
| G7 | Wiring: web `addEventListener("{dom}", AddressOf H)`, WinForms `AddHandler {Id}.{Event}, AddressOf H` | RW:987, :993 |
| G8 | BL8013 handler-ordering is web-only, matched by NAME only (`Sub <name>` text scan) | RW:478-517 (web gate :482), :520-545 |
| G9 | `IsEmittedBind`: a web component is wired only on its declared web events | RW:437-452 |
| G10 | Regions: `' <vgs:designer region="controls|init" form="…" hash="…">` … `' </vgs:designer>`; hash-guarded; hand edit → BL8011, malformed → BL8012 | `BasicLang/Forms/RegionMarkers.cs:53-176`; RW:44-103 |
| G11 | Scaffold: WinForms writes `Using System` / `System.Drawing` / `System.Windows.Forms` + `Inherits Form`; web writes neither; both `Me.InitializeComponent()`; web puts the init region LAST (BL8013) | `BasicLang/Forms/FormScaffolder.cs:201-212`, :221-231, :234-250 |
| G12 | Handler stubs: web `(e As DomEvent)` (or `()` when `WebHandlerTakesEvent` is false — the Timer), WinForms `(sender As Object, e As {WinFormsArgs ?? "EventArgs"})`; web stub ABOVE the init region, WinForms after | `BasicLang/Forms/FormHandlers.cs:157-162`, :179-181 |
| G13 | Timer on the web: field `Integer`, construct `w.setInterval(AddressOf {handler}, {Interval})`, implied `Enabled=true` (the "wired means running" rule); `Enabled` is WinForms-only | FCC:1756-1778 (Enabled :1764), `FormWebScript` :1003 |
| G14 | Dispatch `Public Class VgsForms` with once-per-page guard; auto-start after `Main` unless user code calls/references it (decided on the IR) | FAE:931-969; JSB:1063-1150 |
| G15 | Diagnostics reach the Error List via `DesignerDiagnosticsEvent`, source "Form designer" | `VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs:1729-1768`; `MainWindowViewModel.cs:582`, :1598-1619 |
| G16 | Next free design code is **BL8033**; BL8018 retired (never reuse); BL8031 reserved | `BasicLang/Forms/DesignDiagnostic.cs:54-84` |
| G17 | Nothing checks a web handler's PARAMETERS; the only old-dialect recogniser is the import `DomDialect` (`::document`/`createElement`, `addEventListener`) | RW:520-545; `BasicLang/Forms/Recognizer/DomDialect.cs:114-115`, :145-147 |

### 2.2 The catalog
| # | Fact | Where |
|---|---|---|
| C1 | **Every row declares exactly ONE event, its default** (`Ev(...)` "declares only its DEFAULT one — every row today (the D1 event lists arrive in slice 5)") | FCC:1350-1358 |
| C2 | `FormEventDef(Name, WinFormsArgs, WebEvent, Category, Description, IsDefault, OracleExemption)`; a null `WebEvent` = WinForms-only | `BasicLang/Forms/FormEvents.cs:37-44`, :28 |
| C3 | `FormPropertyDef` fields incl. `Targets` (null = both), `HtmlAttribute`, `CssProperty`, `CssConverter`, `WinFormsFactory`, `IsItemCollection`, `WebDefault`, `OracleExemption` | FCC:154-171, `AppliesTo` :195 |
| C4 | `FormControlDef` fields incl. `WinFormsType`, `HtmlTag`, `HtmlInputType`, `IsContainer`, `Place`, `WebScript`, `WebHandlerTakesEvent`, `HtmlChildrenWrapper`; `SupportsTarget(Web)` = `HtmlTag != null || WebScript != null` | FCC:1062-1110 |
| C5 | `Common(...)` appends Enabled, Visible, ForeColor, BackColor to every kind's own rows | FCC:1323-1333 |
| C6 | GroupBox's default event is still **Click**, with an oracle exemption naming VS's Enter | FCC:1454-1462 |
| C7 | 15 positioned web kinds, 3 strips, 4 items, 1 web component (Timer); 11 WinForms-only kinds (8 controls + ToolTip, ErrorProvider, BackgroundWorker), pinned by `FormCatalogCoverageTests.TheControlsWithNoHonestHtmlEquivalentAreWinFormsOnly` | FCC:1370-1973; tests `FormCatalogCoverageTests.cs:186` |
| C8 | Web-only rows: RadioButton `GroupName`, ListBox `MultiSelect`; WinForms-only rows incl. NumericUpDown `DecimalPlaces`, DateTimePicker `Format`/`CustomFormat`/`ShowUpDown`, TrackBar `TickFrequency`/`Orientation`, ProgressBar `Minimum`/`Style` | FCC:1411-1414, :1435-1438, :1515-1588 |
| C9 | TextBox `Multiline` and `PasswordChar` have no web mapping and the emitter never reads them (a TextBox is always `<input type="text">`) | FCC:1377-1392 |
| C10 | Panel `BorderStyle` has no `CssProperty` | FCC:1443-1448 |
| C11 | FormRoot: Text (both), ClientSize (WinForms + web Canvas), Cols/Rows (Grid), Gap (Grid/Flow), MobileBreakpoint (Canvas); **no events** (no `Load`) | FCC:1991-2039 |
| C12 | Toolbox shows only `FormControlCatalog.For(Target)` — desktop-only kinds are HIDDEN on a web form, no badge; a drop of one is refused BL8019 | `VisualGameStudio.Shell/ViewModels/Designer/FormToolboxViewModel.cs:64`; `FormPlacement.cs:48-51` |
| C13 | Parity oracle: `WinFormsCatalogParityTests` vs `VisualGameStudio.Tests/Data/winforms-metadata.json` (per-event args/category/description; stale exemptions fail); compile sweep `WinFormsCatalogSweepTests` (every property, every enum value, every default-event stub + wiring, via the real CLI and csc) | tests `WinFormsCatalogParityTests.cs:64-189`; `WinFormsCatalogSweepTests.cs:139-248`, samples :601-610 |

### 2.3 The page
| # | Fact | Where |
|---|---|---|
| P1 | CheckBox/RadioButton `Text` → `value=` on a bare `<input>`: the caption is never rendered | FAE:332-336 |
| P2 | GroupBox `Text` → a bare text node inside `<fieldset>`; no `<legend>` anywhere in `Forms/` | FAE:436-438 |
| P3 | `Enabled=False` → ` disabled` on ANY tag — on a Panel's `<div>` it does nothing | FAE:344 |
| P4 | Radio `name=` only from `GroupName`: two radios in a Panel with no GroupName are NOT mutually exclusive on the web, while WinForms groups them by container | FAE:383-388 |
| P5 | Visible=False → CSS `display:none` from the catalog row, keyed on `#id` | FCC:1167-1169; `BasicLang/Forms/FormCss.cs:31` |
| P6 | A kind with no tag → `<!-- id: 'Kind' has no web catalog row -->`, no diagnostic | FAE:277-284 |
| P7 | The reflow script observes `.vgs-form`'s subtree for `style`/`class`/`hidden` | `BasicLang/Forms/FormDockScript.cs:106` |
| P8 | `FormAnchorCss.Positioned(geometry, W, H)` / `Docked(bounds)`; `Docked` is mirrored by `vgsDockCss`, gated by `FormDockScriptTests.TheScriptsResolver_AgreesWithFormDockLayout_OnEveryFixture` | `BasicLang/Forms/FormAnchorCss.cs:94-132`; tests `FormDockScriptTests.cs:316` |
| P9 | A container's client size is its resolved/stored OUTER size — no border or caption inset ("the one rule") | `BasicLang/Forms/FormDockLayout.cs:268-274` |
| P10 | Recorded gaps (Task 12/13): WinForms Panel FixedSingle insets children 1px, Fixed3D 2px (positioned AND docked); GroupBox docks inside +3/+19/+3/+3 but does NOT inset positioned children; the web fieldset insets every child 2px; web Panels draw no border | `docs/superpowers/plans/2026-09-27-web-pixel-layout-task12-preflight.md:128-135`; `…-task13-preflight.md:140-141` |

### 2.4 The compiler and the web build
| # | Fact | Where |
|---|---|---|
| K1 | `WithJavaScriptDeclarations` APPENDS the one hard-coded `lib/js/dom-core.bli` (beside the running exe) on a JS target; every route (CLI file, CLI project, IDE build, debugger) passes through `CompileProjectFiles` | `BasicLang/Compiler.cs:538-559`, :237-238, :354 |
| K2 | No mechanism includes a BasicLang SOURCE library; the LSP never includes `dom-core.bli` | Compiler.cs (sole `DomDeclarationsPath` users) |
| K3 | `dom-core.bli` is copied by the csproj; `IDE/lib/js/dom-core.bli` is a hand-committed, load-bearing copy; **no test keeps the two equal** | `BasicLang/BasicLang.csproj:43`; `docs/wiki/content/js-backend.md:46-48` |
| K4 | `dom-core.bli` `Element` has no `getBoundingClientRect`/`clientWidth`/`offsetParent`; `CSSStyleDeclaration` has no `position`/`left`/`top`/`right`/`bottom`; `DomEvent` has no `button`/`buttons`/`relatedTarget`/`ctrlKey`/`shiftKey`/`altKey`/`code`/`detail` | `BasicLang/lib/js/dom-core.bli:32-95` |
| K5 | Preprocessor knows `#Include`, `#Define`, `#IfDef`, `#IfNDef`, `#Else`, `#EndIf`, `#CppInclude`, `#JsImport` — **no `#If`/`#ElseIf`/`#End If`**; nothing predefines a symbol (`Preprocessor.Define` has no production caller); `<DefineConstants>` is parsed but never reaches the compiler; no CLI `--define` | `BasicLang/Preprocessor.cs:221-308`, :194-197; `Compiler.cs:186`; `ProjectFile.cs:313-315` |
| K6 | Any dotted or `System*`/`Microsoft*`/`Windows*` `Using`/`Imports` is marked a .NET namespace at PARSE time | `BasicLang/Parser.cs:714-720` |
| K7 | A base class is resolved through the type manager only; a sibling file's class is registered as a SYMBOL shell, so `Inherits` of it fails; an unresolved base is assumed .NET when any `Using` is present | `BasicLang/SemanticAnalyzer.cs:5907-5924`, :636-643 |
| K8 | JS: events are a per-instance `new Set()`; `RaiseEvent` iterates it; `AddHandler`/`RemoveHandler` → `add`/`delete`; `AddressOf` on an instance method → `.bind(recv)` (a NEW function each time) | JSB:802-807, :3113-3132, :1505-1522 |
| K9 | JS: classes emitted in `module.Classes.Values` order, no base-first sort; classes keyed by SIMPLE name, namespace dropped; first class of a name wins in the merge | JSB:208-225; `IRBuilder.cs:1532`; Compiler.cs:986-991 |
| K10 | JS refusals: ByRef BL7002, Long BL7003, **Char BL7004**, value **Structure BL7005**, operator overloading BL7006, .NET BCL types BL7007; no method overloading (duplicate name is an analyzer error) | `BasicLang/JsCapabilityChecker.cs:34-50`, :304-308 |
| K11 | Parser: `Friend` not accepted on class members; `Delegate` only unmodified at top level; no `Handles`/`WithEvents`; non-constant field initializers refused ("assign it in a constructor instead") | `Parser.cs:866-942`, :118-119, :275-277; `IRBuilder.cs:1930-1933` |
| K12 | Web forms are discovered by `FormDocumentLoader.LoadWebForms` (skips non-web documents), dispatch written by `FormDispatch.Write`, passed to `JavaScriptEmitter.Emit(forms:)` — IDE and CLI mirror each other | `VisualGameStudio.ProjectSystem/Services/BuildService.cs:614-651`, :1017; `BasicLang/Program.cs:807-832`, :953-962; `BasicLang/Forms/FormDocumentLoader.cs:30,42` |
| K13 | A WinForms build compiles the same `.bas` through `CompileProjectFiles` with `UseWindowsForms`; `EnableNetResolution` returns early, so every WinForms member types as `Object` with no diagnostic | Compiler.cs:145-146; Program.cs:1037-1051 |
| K14 | Opening a form: `OpenFileAsync` → `EnterDesignModeForFormDocument`; the document VM gets no dialog service; toasts with action buttons exist (`ShowNotification(..., actions)`), and `IDialogService.ConfirmAsync` | `MainWindowViewModel.cs:2473-2526`, :2140-2146; `VisualGameStudio.Core/Abstractions/Services/IDialogService.cs:9`, :133-137 |

### 2.5 Claims found FALSE (in the brief, the docs, or the premises of a decision)
| # | Claim | Truth |
|---|---|---|
| F1 | CLAUDE.md: "conditional compilation (`#If`/`#IfDef`/`#Else`/`#EndIf`)"; O3: "conditional compilation exists — the build defines the symbol by target" | `#If` does not exist and no build defines any symbol (K5, M9). O3's mechanism is BUILT here (§4.1); its product behaviour is unchanged. |
| F2 | O1 "every event the designer's catalog lists" covers the O6 mouse/key events | The catalog lists ONE event per kind (C1). The O6 events must become catalog rows (§10.1) for "one list drives both" to hold. |
| F3 | "GroupBox's default event is Enter" | Still Click in the catalog (C6); changed here (§10.2). |
| F4 | Brief: WinForms handlers are "user-written" | The designer writes the stub with the row's `WinFormsArgs` (G12). |
| F5 | O7 lists three rendering gaps | O1 implies more: TextBox `Multiline`/`PasswordChar` do nothing on the web (C9), radios without GroupName don't group (P4), a disabled Panel disables nothing (P3). Fixed here (§7). |
| F6 | Research note: "`Me.X()` is the safe spelling on the JS backend" | Not once any `Using` line is present — then `Me.X()` fails to build (M7), and the WinForms scaffold always has `Using` lines. |
| F7 | The piece-1 note "`Visible` must write a non-empty `style.display`" (task-10 pre-flight B1) is a complete rule | It conflicts with phone stacking for CONTAINERS (§5.4, Q1). |

## 3. Measured facts (2026-09-29)

Compiler built from this worktree (`dotnet build BasicLang/BasicLang.csproj -c Release`), probes under the session
scratchpad `p2probe\*`, JavaScript run with `node` v22.19.0. Single-file probes: `BasicLang.exe p.bas --target=javascript`;
multi-file: a `.blproj` with `<TargetBackend>JavaScript</TargetBackend>` and `BasicLang.exe build Site.blproj`.

| # | Shape | Result |
|---|---|---|
| M1 | One file: `Public Event Click(sender As Object, e As EventArgs2)` on a base `Control`, `RaiseEvent Click(Me, …)` from a base method, `AddHandler btn.Click, AddressOf btn_Click` on a derived `Button` instance, handler `(sender As Object, e As …)` | ✅ builds and runs; `sender` is the control; adding the same handler twice fires it twice (matches .NET) |
| M2 | …then `RemoveHandler btn.Click, AddressOf btn_Click` | ❌ **silent no-op**: the next raise still fires both (4 calls where .NET makes 3). Emitted `t1 = this.btn_Click.bind(this); t0.delete(t1)` — a new function never in the Set (K8) |
| M3 | `Overridable`/`Overrides` Sub and Function, `MyBase.OnTextChanged()`, `MyBase.Describe()`, a call through a base-typed variable | ✅ `super.` calls and prototype dispatch run correctly |
| M4 | `Public Overrides Property Text` whose getter/setter use `MyBase.Text` | ❌ builds green; emits `this.Text` → `RangeError: Maximum call stack size exceeded` at run time |
| M5 | `Public Class Form1 : Inherits Form` declared ABOVE `Public Class Form` in one file | ❌ builds green; `ReferenceError: Cannot access 'Form' before initialization` at load (K9) |
| M6 | `Inherits Base` where `Base` is in ANOTHER file of the project | ❌ "Unknown base class 'Base'" + "MyBase can only be used…" + upcast refused — on **JavaScript AND C#** (K7). Cross-file `New Base()`/member calls work |
| M7 | Any `Using` line (`System`, `System.Drawing` or `System.Windows.Forms`) + a class calling `Me.Init()` (Private Sub), with or without `Inherits` | ❌ JS: "no lowering for 'Me.Init'" — build fails. Without the `Using` line: ✅ runs |
| M8 | `Namespace System.Windows.Forms` | ❌ parse error "Unexpected token at top level: '.'"; nested `Namespace System`/`Windows`/`Forms` parses, but `System.Windows.Forms.Button` then resolves as a .NET type (three BL6016 warnings) and `Dim c As …Control = btn` is refused |
| M9 | `#If WEB Then` / `#Else` / `#End If` | ❌ "Unexpected token in expression: '#If'", "#Else without matching #IfDef or #IfNDef". `#IfDef WEB` parses (symbol undefined, so its else-branch is taken) |
| M10 | `Public Structure Pt` with `Public Sub New(x, y)` | ❌ parse error "Expected member name but found Sub" (any target). Without the constructor, on JS: ❌ BL7005 (value Structure refused) |
| M11 | `Using System` + a handler `(sender As Object, e As EventArgs)` | ❌ BL7007 "'EventArgs' is not available on the JavaScript backend" |
| M12 | `Enum Shade … End Enum` then `Dim k As Shade = Shade.Dark` (top level or inside a Module) | ❌ "Cannot assign value of type 'Object' to variable of type 'Shade'" on JS **and C#**; same on the older `IDE\BasicLang.exe` drop. `CType(Shade.Dark, Shade)` compiles |
| M13 | `AddHandler btn.Click, AddressOf btn_Click` where `btn_Click(n As Integer)` does not match the event | ❌ builds green and runs, passing `sender` as `n` — no signature check |
| M14 | `AddHandler btn.Click, Sub(s As Object, e As EventArgs) …` | ✅ runs |
| M15 | Handler declared AFTER the `InitializeComponent` that wires it with `AddHandler` to a BasicLang `Event` | ✅ builds and runs on JS — BL8013's cause (an erased handler failing `Action(Of DomEvent)`, `docs/HANDOFF.md:1173-1188`) does not arise |
| M16 | A class named `F` calling `Me.Init()` | ❌ "Type 'F' does not have a member 'Init'" — the existing chip `task_ef845b99`, reproduced |
| M17 | `BasicLang.exe a.bas b.bas` | refused: "compiling a file directly takes exactly one" — multi-file probes must use a `.blproj` |

## 4. Sub-piece 2.0 — compiler prerequisites

Each item: the rule, then the test that pins it. All are front-end or JS-backend fixes, verified through the CLI AND the
IDE build (`CompileProjectFiles`) and, where the optimizer runs, through it too.

### 4.1 `#If` and the target symbols (O3)
- The preprocessor accepts VB's form: `#If <cond> Then`, `#ElseIf <cond> Then`, `#Else`, `#End If`, nestable, alongside
  the existing `#IfDef`/`#IfNDef`/`#EndIf` (kept). `<cond>` is a defined-symbol name, `Not`, `And`, `Or`, `AndAlso`,
  `OrElse`, parentheses, and `True`/`False` **[impl]** — symbols only, no values or comparisons (VB's `#Const X = 1`
  values are out of scope).
- ⚠ Prefix traps (K5): `#ElseIf` must not match the `#Else` arm and `#End If` must not fall to the default arm.
- **Symbols by target:** `WEB` when the backend is JavaScript; `DESKTOP` for every other backend **[impl — Q7]**.
  Defined in ONE place (the compiler's options → `Preprocessor.Define`) so the CLI file route, the CLI project route, the
  IDE build and the debugger all get them (K1's routes); a test per route. `<DefineConstants>` (already parsed, K5) is
  passed through as additional symbols **[impl]** — cheap, and it is what users of `#If` will expect.
- The LSP defines the project's target symbol, so an inactive `#If` branch produces no editor diagnostics.
- Fix the recorded line-number shift: a `#Define` line is not echoed, so every later line is off by one (K5, :235-238).
- Tests: every directive shape; nesting; unknown symbol = false; `#If WEB` chooses by target through CLI + IDE builds on
  JS and C#; `#ElseIf`/`#Else` prefix traps; line numbers after `#Define` and inside skipped branches.

### 4.2 Cross-file `Inherits` (all backends)
A class may inherit a class declared in another file of the project, including across `.bas`/`.cls`/`.mod` and the
library (§5.1). The base must be resolved against the sibling class shells, not only the type manager (K7). Tests (JS and
C#, CLI + IDE): cross-file `Inherits`, `MyBase`, upcast, override dispatch, a three-level chain split over three files.

### 4.3 `Using` must not break `Me.M()` on JavaScript
M7's failure mode is a build error today; after the fix `Me.PrivateMethod()` lowers to `this.PrivateMethod()` whatever
`Using` lines the file has. Test: M7's matrix (each `Using`, with/without `Inherits`) builds and runs under node.

### 4.4 `RemoveHandler` removes (JavaScript)
`AddressOf recv.M` must produce the SAME function for the same `(recv, M)` pair, so `RemoveHandler` finds what
`AddHandler` added, while adding twice still fires twice (M1) and removing once leaves one. **[impl]** the event store
becomes a list of `{target, method}` entries (or a per-receiver memo of bound functions); a `Set` of bound functions
cannot both dedupe identity and count duplicates. Tests: M2's sequence yields 3; remove of a never-added handler is a
no-op; lambdas are removed only by the same delegate variable.

### 4.5 `MyBase.Property` (JavaScript)
`MyBase.P` in a property getter/setter lowers to `super.P` (M4). Test: M4 prints `B:hi`; a three-level chain.

### 4.6 Base-first class emission (JavaScript)
Classes are emitted base before derived (topological over `Inherits`, stable otherwise), fixing M5 for user code and
making library placement irrelevant. Test: M5 runs; a cycle is already an analyzer error.

### 4.7 Enum member typing (all backends)
`Shade.Dark` has type `Shade` (M12). The library's `Keys`, `MouseButtons`, `DockStyle`, `AnchorStyles`,
`BorderStyle`, `ContentAlignment`, `PictureBoxSizeMode` depend on it. Test: M12 on JS and C#, member access in
assignment, comparison, `Select Case`, and as an argument.

### 4.8 `AddHandler` checks the handler against the event (all backends, BasicLang events)
When the event is a BasicLang-declared `Event` (including the library's), a handler whose parameter list is not
assignment-compatible with the event's is an error naming both signatures (M13). .NET events (typed `Object` on a
WinForms build, K13) are unchanged — csc still checks those. This is what makes an unconverted `(e As DomEvent)`
handler a build error on the web instead of a page receiving the control as `e` (§8).

### 4.9 BasicLang namespaces win on a web build
- `Namespace System.Windows.Forms` (dotted) parses as the nested form (M8).
- On a JavaScript build, a `Using`/`Imports` or a qualified type name that names a namespace DECLARED IN THE PROGRAM
  (the library's `System`, `System.Drawing`, `System.Windows.Forms`) resolves to it, before the parse-time .NET marking
  (K6) applies. `System.Windows.Forms.Timer` and `Timer` under `Using System.Windows.Forms` are then the library's class
  (the designer writes components FULLY QUALIFIED — CLAUDE.md, tray components). C# builds are unchanged (the real .NET
  namespace always wins there; the library is never included).
- Test: M8's program builds and runs on JS; the same text builds on C# against real WinForms.

### 4.10 Not fixed here (recorded, worked around)
- **Structure constructors / value Structures on JS (M10, K10):** `Point`, `Size`, `Color` are CLASSES in the library
  (§5.6).
- **`Char` on JS (K10):** blocks `KeyPressEventArgs.KeyChar As Char` — §5.5, Q4.
- **Chip `task_ef845b99` (class `F`, M16):** not on this path; left as filed.

## 5. The library

### 5.1 Shape, location, inclusion
- Source files `BasicLang/lib/js/forms/*.bas` **[impl]**: one file per class family (`Control.bas`, `Form.bas`,
  `EventArgs.bas`, `Drawing.bas`, one per kind). Copied to the output beside `dom-core.bli` by the csproj, and to
  `IDE/lib/js/forms/` in the IDE drop — load-bearing exactly as `IDE/lib/js/dom-core.bli` is (CLAUDE.md). A test asserts
  the csproj copies every library file (as `BliDeclarationFileTests` does for `dom-core.bli`).
- Declared in `Namespace System` (`EventArgs`), `Namespace System.Drawing` (`Color`, `Point`, `Size`, `Image`) and
  `Namespace System.Windows.Forms` (everything else) — so the scaffold's three `Using` lines (G11) mean the same thing on
  both targets (§4.9).
- **The hook:** `WithWebFormsLibrary(files)` beside `WithJavaScriptDeclarations` (K1), called on the same route. It adds
  the library **only when the target is JavaScript AND some compiled source file has `Using`/`Imports
  System.Windows.Forms`** **[impl]**. Consequences: every existing web project — including every DOM-style web form, which
  never has that line (G11) — compiles byte-for-byte as today (a golden test pins this), and a JS program with its own
  class named `Timer` or `Label` is not affected unless it opts in.
- **Name collisions:** JS drops namespaces and the first class of a name wins silently (K9). When the library is
  included, a program class with the simple name of a library class is an ERROR naming both (new compiler code, next free
  in its range) — never a silent merge.
- The library is ordinary BasicLang: it compiles through the same front end, IR, optimizer and `JsCapabilityChecker`
  as user code. It reaches the DOM through `dom-core.bli`'s typed surface, extended (both copies, in lock-step — a new
  test compares `BasicLang/lib/js/dom-core.bli` with `IDE/lib/js/dom-core.bli`, K3) with what the library needs:
  `getBoundingClientRect`, `clientWidth`/`clientHeight`/`clientLeft`/`clientTop`, `closest`, `contains`; style
  `position`/`left`/`top`/`right`/`bottom`/`objectFit`/`objectPosition`/`removeProperty`/`getPropertyValue`; event
  `button`, `buttons`, `detail`, `deltaY`, `relatedTarget`, `code`, `ctrlKey`, `shiftKey`, `altKey`; `Window.getComputedStyle`.
  Untyped `::` is used only where no typed declaration is honest.
- Library constraints from the language (K10, K11): no overloading (optional parameters instead, e.g.
  `Color.FromArgb(r, g, b, Optional a = 255)` **[impl]**), no `Friend` (library-internal members are `Public` with a
  `Vgs` prefix, excluded from the coverage gate and documented as internal), no non-constant field initializers
  (constructors assign), no Structures.

### 5.2 Classes
`System.EventArgs` (+ `Empty`); `System.Drawing.Color`, `Point`, `Size`, `Image` (`Image.FromFile(path)`);
`System.Windows.Forms.Control`, `Form`, `ControlCollection`, `ObjectCollection` (Items), `MouseEventArgs`,
`KeyEventArgs`, `KeyPressEventArgs`, `LinkLabelLinkClickedEventArgs`, `ToolStripItemClickedEventArgs`, enums `Keys`,
`MouseButtons`, `DockStyle`, `AnchorStyles`, `BorderStyle`, `ContentAlignment`, `PictureBoxSizeMode`; one class per web
kind (C7): `Label`, `TextBox`, `Button`, `CheckBox`, `RadioButton`, `ComboBox`, `ListBox`, `Panel`, `GroupBox`,
`PictureBox`, `LinkLabel`, `NumericUpDown`, `DateTimePicker`, `TrackBar`, `ProgressBar`, `Timer`, `MenuStrip`,
`ToolStrip`, `StatusStrip`, `ToolStripMenuItem`, `ToolStripSeparator`, `ToolStripButton`, `ToolStripStatusLabel`. The
hierarchy follows WinForms where it matters to shared code (`CheckBox`/`RadioButton`/`Button` are `Control`s;
`LinkLabel Inherits Label`; strip items share a `ToolStripItem` base, which is not a `Control`, as in WinForms).
**The WinForms-only kinds have NO class** (§9).

### 5.3 Attaching: the object and its element
- A control object holds exactly two things of its own: its `Name` and a reference to its element. Everything else is
  read from and written to the element (O6, "no shadow copy"). Where WinForms has state the DOM has no slot for, the
  state lives ON THE ELEMENT as a `data-vgs-*` attribute (e.g. `PasswordChar`, a child's own `Enabled` under a disabled
  parent) — still the live element, never a field.
- `New Button()` creates an UNATTACHED object. `parent.Controls.Add(child)` attaches it: the element is
  `document.getElementById(child.Name)` (ids are unique on a page — one form per page), and, when the parent is
  attached, the element must be inside the parent's element. A child may be attached before its parent is (the WinForms
  region adds children to a Panel before the Panel to the form, G6).
- Reading or writing a property of an unattached control throws an `InvalidOperationException` naming the control and
  "not attached to a page element" **[impl]**. A `Controls.Add` whose `Name` has no element on the page throws naming the
  name — the case of a control CREATED at run time (`Dim b As New Button(): Me.Controls.Add(b)`) is Q2.
- `Form`'s own constructor attaches the form object to the page's form area (the element `FormAssetEmitter.Html` writes
  for the form) **[impl — the attach key is chosen in the plan: the `data-form` name the dispatch already reads]**.
- `Element As Element` (read-only) on `Control`, `Form` and `ToolStripItem` returns the live element (O3).

### 5.4 Data flow (the catalog's web mapping, made live)
Common to every control (C5 plus geometry):

| Member | Web, read / write the live element |
|---|---|
| `Text` | per kind (table below); setting a DIFFERENT value raises `TextChanged` once |
| `Enabled` | the kind's focusable element `disabled`; **a container propagates**: its descendants' elements are disabled while it is, and each child's OWN value is kept in `data-vgs-enabled`; `child.Enabled` reads False while any ancestor is disabled — WinForms semantics (fixes P3) |
| `Visible` | see below; `child.Visible` reads False while an ancestor is hidden (WinForms semantics) |
| `ForeColor` / `BackColor` | `style.color` / `style.backgroundColor`; read from the computed style, returned as a `Color` |
| `Left`/`Top`/`Width`/`Height`/`Location`/`Size` | §5.7 |
| `Name` | the element id (read-only once attached) |
| `Parent`, `Controls` | the attached tree |
| `Focus()` | `element.focus()` |

**`Visible` (O6, and the piece-1 note).** Hiding writes `style.display = "none"`. Showing writes an explicit NON-EMPTY
display — the row's `WebDisplay` value (new catalog field, default `block`; the CheckBox/RadioButton wrapper's is the
wrapper's display) — because `style.display = ""` leaves a design-hidden control hidden by its `#id { display: none }`
rule (task-10 pre-flight B1). Either write is a `style` mutation, so the piece-1 reflow script re-docks (P7).
⚠ **Measured conflict to resolve (Q1):** below the phone breakpoint the stylesheet switches a CONTAINER to
`display: flex` (piece-1 §5); an inline `display: block` from `Visible = True` overrides it and the container's children
stop stacking. The spec's default follows O6 for every control and asserts the conflict as a recorded gap in Edge for
containers; Q1 offers the alternative (the `hidden` attribute, which the reflow script and the phone rule
`.vgs-form [hidden] { display: none !important }` already honour).

Per kind (the "Text" column follows the catalog's existing web mapping, P1/P2 corrected by §7):

| Kind | Properties → element |
|---|---|
| Label, Button, LinkLabel | `Text` → `textContent`; `TextAlign` → `style.textAlign` (horizontal part, as `FormCssConverter.ContentAlignmentHorizontal`) |
| TextBox | `Text` → `value`; `ReadOnly` → `readOnly`; `MaxLength` → `maxLength` (reads 32767 when absent, WinForms' cap); `PasswordChar` → `type="password"` when non-empty, the char kept in `data-vgs-passwordchar`; `Multiline` → the element IS a `<textarea>` when True (§7.4); a run-time change swaps the element in place, keeping id, classes, inline style and the library's listeners **[impl]** |
| CheckBox | `Text` → the caption span; `Checked` → the inner input's `checked` (raises `CheckedChanged` when it changes) |
| RadioButton | as CheckBox, plus `GroupName` → the input's `name` (web-only row); checking one raises `CheckedChanged` on the one that became unchecked as well — the DOM fires `change` only on the newly checked radio, so the library raises the other itself |
| ComboBox | `Items` → `<option>`s; `SelectedIndex` → `selectedIndex` (raises `SelectedIndexChanged` when it changes); `Text` → the selected option's text; setting it selects the first option with that text, or none **[impl — a `<select>` cannot hold free text; WinForms' editable DropDown can: recorded divergence]** |
| ListBox | `Items`, `SelectedIndex` as ComboBox; `MultiSelect` → `multiple` (web-only row) |
| Panel | `BorderStyle` → the border (§7.3) |
| GroupBox | `Text` → the `<legend>` text (§7.2) |
| PictureBox | `Image` → `src` (`Image.FromFile(path)` carries the path); `SizeMode` → `object-fit`/`object-position` (Normal: none + top left; StretchImage: fill; Zoom: contain; CenterImage: none + center; AutoSize: the image's natural size) |
| NumericUpDown, TrackBar | `Minimum`/`Maximum`/`Value`/`Increment` → `min`/`max`/`valueAsNumber`/`step`; `Value` is a Double on the web (WinForms Decimal — recorded; Q5) |
| DateTimePicker | `Value` → `valueAsDate`, a `DateTime` |
| ProgressBar | `Maximum`/`Value` → `max`/`value` (`Minimum` is WinForms-only, C8) |
| Timer | `Interval`, `Enabled` (now a web row too, §10.4); `Start()`/`Stop()`; `Tick(sender, e)`; runs only while Enabled, as WinForms |
| MenuStrip / ToolStrip / StatusStrip | `Visible`; `Dock` read-only; `ItemClicked` with `ToolStripItemClickedEventArgs.ClickedItem` |
| ToolStrip items | per their catalog rows (FCC:1913-1973): `Text`, `ToolTipText` → `title`, `Visible`; `Click` |
| Form | `Text` → `document.title`; `ClientSize` read-only (the form area's client size); `Controls` |

**Methods.** The catalog lists no methods. The library ships the ones the generated code and the twin need —
`Controls.Add/Remove/Count/Item`, `Items.Add/Insert/Remove/RemoveAt/Clear/Count/Item/IndexOf/Contains`, `Focus`,
`Timer.Start/Stop`, `Button.PerformClick` — **[impl]**; any other method is a web-build error (§5.9). Whether more ship
(Show/Hide/BringToFront/`TextBox.Clear`, `MessageBox.Show`) is Q3.

### 5.5 Events
- Declared on the library class that WinForms declares them on (Control's Click/TextChanged/Mouse*/Key*/Enter on
  `Control`), raised through `Protected Overridable Sub OnX(e)` — the VB pattern, and the one M1/M3 measured working
  (an event can only be raised from its declaring class, JSB:745-746).
- **`sender` is always the control.** The DOM listener lives in the library, attached once per element at attach time
  (and moved on a `Multiline` swap); user code only ever sees BasicLang events.
- DOM → WinForms mapping (the catalog's `WebEvent` column — §10.1 makes it the one list):

| WinForms event | DOM source | Args |
|---|---|---|
| Click | `click` (a wrapped caption's second, synthetic `click` is not counted — a `<label>` wrapping an `<input>` dispatches click twice) | `EventArgs` |
| TextChanged | `input` (TextBox), and any programmatic `Text` change | `EventArgs` |
| CheckedChanged | `change`, programmatic `Checked` change, and the radio that became unchecked | `EventArgs` |
| SelectedIndexChanged / ValueChanged | `change` / `input` per the row's `WebEvent` (C1), and programmatic change | `EventArgs` |
| MouseDown / MouseUp / MouseMove | `mousedown` / `mouseup` / `mousemove` | `MouseEventArgs`: `Button` (`button`/`buttons` → `MouseButtons`), `X`/`Y` relative to the control's CLIENT area (border excluded), `Location`, `Clicks` (`detail`), `Delta` (0) |
| KeyDown / KeyUp | `keydown` / `keyup` | `KeyEventArgs`: `KeyCode` (`code`/`key` → `Keys`, a fixed table), `Shift`/`Control`/`Alt`, `Modifiers`, `KeyData`, `Handled`, `SuppressKeyPress` (→ `preventDefault` on the keydown) |
| KeyPress | a character-producing `keydown` (`key` of length 1, or Enter) | `KeyPressEventArgs`: `KeyChar`, `Handled` (→ the character is not inserted) — ⚠ `Char` is refused on JS (K10): Q4 |
| Enter (GroupBox default, and every Control) | `focusin` whose `relatedTarget` is OUTSIDE the control — WinForms raises Enter when focus enters from outside, not on every move between children | `EventArgs` |
| Tick | the library's `setInterval` | `EventArgs` |
| LinkClicked | `click` (navigation prevented) | `LinkLabelLinkClickedEventArgs` |
| ItemClicked | `click` on an item | `ToolStripItemClickedEventArgs` |

- **Counts and order match WinForms**, measured by the behaviour twin (§11.2): e.g. a CheckBox click raises
  CheckedChanged then Click (WinForms' `OnClick` toggles first); a programmatic set to the SAME value raises nothing; a
  TextBox raises TextChanged per keystroke (as WinForms does). A divergence the browser forces (NumericUpDown's `input`
  per keystroke vs WinForms' commit — the catalog already chose `input`, FCC:1520) is asserted as a recorded literal.
- The web event name the generated code no longer uses directly (`addEventListener`) stays in the catalog: the library's
  mapping and the §10 gate read it.

### 5.6 Drawing types
`Point`, `Size`, `Color` are value types in .NET and classes here (M10, K10). To keep .NET's copy semantics observable
in shared code, **every getter returns a NEW object and every setter copies** (`Dim p = btn.Location: p.X = 5` does not
move the button, as in WinForms; `btn.Location.X = 5` is already CS1612 on desktop, CLAUDE.md). `Color`: the named
colours the catalog accepts (FCC `Color` rows) as `Shared ReadOnly` properties, `FromArgb`, `R`/`G`/`B`/`A`, `Name`,
`Equals`; computed-style `rgb()`/`rgba()` parse to a Color whose `Name` is the matching named colour when one matches.

### 5.7 Run-time geometry and Anchor — the mirrored pair
- `Left`/`Top`/`Width`/`Height`/`Location`/`Size`/`Right`/`Bottom` READ the element's rectangle in its container's CLIENT
  coordinates (`getBoundingClientRect` minus the container's client origin — the same measurement piece 1's harness
  uses, spec §7 item 2).
- WRITING a position or size re-derives the control's CSS **by the rule `FormAnchorCss.Positioned` uses**, with two
  differences that are WinForms' own semantics: the container size is its CURRENT client size (WinForms captures anchor
  distances when bounds are SET, against the container's size at that moment), and the offsets are the NEW bounds. So a
  Right-anchored control moved to `Left = 50` writes `right: (Wc − 50 − w)px` and keeps its right distance from there on
  (O6); Left+Right writes both insets and the `calc(100% − (near+far)px)` size (piece-1 §4, the `<img>` rule); a centred
  axis writes `calc(50% ± …)` from the current size.
- `Anchor` is readable and writable (WinForms `AnchorStyles`); the element carries it as `data-vgs-anchor` (written by
  the page emitter for every positioned control whose Anchor is not the default) **[impl]**; writing it re-derives the
  CSS from the current bounds, as WinForms re-captures.
- A DOCKED control ignores position/size writes on the docked axis, as WinForms' layout overrides them; `Dock` is
  read-only on the web in this piece (a run-time `Dock` change → web-build error; Q6).
- ⛔ **Mirrored pair:** the axis rule now exists in C# (`FormAnchorCss.Axis`) and in the library (BasicLang). Like
  `FormDockLayout`/`FormDockScript.Core`, the two are held in lock-step by a test (§11.3) over one fixture table.
  `FormAnchorCss`'s number formatting rules (invariant digits, sign outside the length) apply to both.

### 5.8 The kinds in 2b
Each 2b kind is added by the same recipe as 2a's: its class, its rows in the coverage gate (§11.1), its twin fixture, its
Edge assertions. `Timer` replaces the web template (G13) for PORTABLE forms: its field is `System.Windows.Forms.Timer`,
constructed in the init region like WinForms'; the `FormWebScript` path stays for DOM-style forms until they convert.
Strips/items attach by id like controls; `Controls.Add` of a strip and the host's item verb (`Items.Add`/`DropDownItems.Add`,
the rows' `FormItemRule`) attach in DOCUMENT order, as the region writer already emits them (CLAUDE.md, strips).

### 5.9 The two build errors (O3)
- **Web build, unavailable member.** A member access on a library type that the library does not declare, or a type in
  `System.Windows.Forms`/`System.Drawing` that the library does not declare (e.g. `MessageBox`, `DataGridView`), is an
  error: *"'Form.ShowDialog' is a WinForms member that is not available on the web. Put desktop-only code inside
  `#If DESKTOP Then … #End If`."* Implemented as a rewording of the analyzer's existing missing-member error (M16's
  message shape) when the receiver is a library type, and of the JS "not available" refusal (BL7007) for an undeclared
  `System.Windows.Forms` type. New compiler code(s), next free in range.
- **Desktop build, `.Element`.** On a non-JS build, a member access named `Element` on an expression whose static type
  NAME is a library class name (`Button`, `Form`, …) is an error: *"'.Element' is web-only; wrap it in `#If WEB Then …
  #End If`."* ⚠ It must fire although WinForms members type as `Object` there (K13): the check is on the declared type
  NAME of the receiver (a field `As Button`, a parameter, `Me` in a class that `Inherits Form`), not on member
  resolution. A receiver typed `Object` is not diagnosed (csc will say CS1061) — recorded.
- Both are ordinary compile errors: they appear in the Error List, the CLI output and the LSP.

## 6. Code generation — the portable style

### 6.1 One detector for "which style is this file" **[impl]**
`FormCodeStyle.Of(codeBehindText)` → `Dom` | `Portable` | `Unknown`. A web init region containing
`Dim doc As Document = ::document` is DOM style — the region writer writes that line unconditionally on the web (G3) and
the region is hash-guarded (G10), so its content is trustworthy. A file with no regions is `Unknown` (BL8014 today).
Read by the region writer, the designer (whether to offer conversion), the conversion, and the page emitter (§7.6).
WinForms files are always portable-shaped and need no detection.

### 6.2 The portable web code-behind
The WinForms shape (G11), on the web:
```
Using System
Using System.Drawing
Using System.Windows.Forms

Public Class LoginForm
    Inherits Form
    ' <vgs:designer region="controls" …>
    Private btnLogin As Button
    Private txtUser As TextBox
    ' </vgs:designer>
    Public Sub New()
        Me.InitializeComponent()
    End Sub
    ' <vgs:designer region="init" …>
    Private Sub InitializeComponent()
        btnLogin = New Button()
        txtUser = New TextBox()
        btnLogin.Name = "btnLogin"
        txtUser.Name = "txtUser"
        Me.Controls.Add(txtUser)
        Me.Controls.Add(btnLogin)
        AddHandler btnLogin.Click, AddressOf btnLogin_Click
    End Sub
    ' </vgs:designer>
    Private Sub btnLogin_Click(sender As Object, e As EventArgs)
    End Sub
End Class
```
- **The web scaffold `Inherits Form`** (O5's "decide and state": yes). One shape; `Me.Controls`, `Me.Text` work alike.
- **Field types** are the WinForms types (`DeclaredType` → the row's `WinFormsType` for a portable file, G1); tray
  components FULLY QUALIFIED as today (`System.Windows.Forms.Timer`).
- **Init = construct → `Name` → `Controls.Add` (reversed, as G6) → components → `AddHandler`.** It writes NO geometry and
  NO properties on the web: the markup paints the design (O5), and re-applying geometry at init would capture anchor
  distances against the browser's CURRENT size rather than the design size (§5.7; piece-1 §4's designer-size rule).
- **`x.Name = "x"` is written on BOTH targets** — the web attaches by it; on WinForms it is what Visual Studio writes and
  closes chip `task_fa51e644` ("control.Name never emitted"). The WinForms sweep gate re-runs over it.
- **The seam for piece 3:** the portable web init is a strict SUBSET of the WinForms init — the same statements, in the
  same order, minus geometry and property lines. Piece 3 (one file building both ways) can wrap exactly those lines in
  `#If DESKTOP` (§4.1); nothing else in the file differs by target. A test pins the subset relation for every catalog
  fixture.
- **Handler stubs** use the WinForms signature on both targets (`FormHandlers`, G12) for a portable file; the Timer's
  becomes `(sender As Object, e As EventArgs)`.
- **BL8013 (handler ordering) does not apply to a portable file** (M15: `AddHandler` to a BasicLang event is not the
  `Action(Of DomEvent)` conversion that motivated it). It stays for DOM-style files. The portable web scaffold puts the
  init region where WinForms does (right after `New()`, handlers below).
- The dispatch (G14) is unchanged: `Dim f As New LoginForm()` runs `Form`'s constructor (attach the form) then
  `InitializeComponent` (attach the controls).

### 6.3 What stays exactly as today
DOM-style files: regions, stubs, BL8013, the Timer template — byte-for-byte (a golden test over every existing web
fixture). WinForms files: unchanged except the added `Name` line.

## 7. Page rendering fixes (O7, and F5)

All are catalog-driven — a row field read by the emitter, never a `control.Kind` switch (CLAUDE.md) — and apply to
PORTABLE-style pages (§7.6).

### 7.1 CheckBox / RadioButton captions
The rows gain a caption wrapper **[impl: a catalog field, e.g. `HtmlCaptionWrapper: "label"`]**: the positioned element
(carrying the id, the geometry CSS and `class="vgs-CheckBox"`) is `<label>`, containing the `<input>` (margin 0) and a
`<span>` with `Text`. The id stays on the element that has the control's bounds, so piece 1's invariants hold (CSS by id,
the reflow script's ids, the harness's measurement, the reading order). `Text` is no longer written as `value=` for these
rows. Assertions: the caption is present, inside the control's box, the glyph's left edge within 1px of the control's
left (WinForms' glyph sits at the left for the default `CheckAlign`), and a click on the caption toggles once and counts
one Click.

### 7.2 GroupBox caption in the border
The `<fieldset>` has `border: 0; padding: 0; margin: 0` so positioned children land at their stored X/Y RELATIVE TO THE
BOX — WinForms does not inset a GroupBox's positioned children (P10). The frame is drawn by a pseudo-element inset from
the box, and the caption is a `<legend>` positioned over the frame's top line **[impl; exact offsets measured against the
WinForms window in the harness]**. Docked children dock inside the DOCK AREA (§7.5).

### 7.3 Bordered Panels
`BorderStyle` gains its CSS: FixedSingle `1px solid`, Fixed3D `2px inset` **[impl; the Fixed3D colours are matched to
the WinForms window within the harness's tolerance or recorded]**. With `box-sizing: border-box`, a positioned child's
containing block is inside the border — which is where WinForms positions it (+1 / +2, P10).

### 7.4 TextBox Multiline and PasswordChar
`Multiline = True` renders `<textarea>` (a second tag on the row, chosen by the property — a catalog field, not a Kind
switch); `PasswordChar` non-empty renders `type="password"`. The phone-stacking and CSS rules treat the textarea as the
TextBox.

### 7.5 Client area — one rule, now with insets
Today a container's client size is its outer size (P9). For the page to match WinForms (O7) the model learns two
catalog-driven insets **[impl: e.g. `FormClientArea.Of(control)`]**:
- **Origin inset** — where a child's (0,0) is: Panel by `BorderStyle` (None 0, FixedSingle 1, Fixed3D 2); GroupBox 0.
  `ClientSizeOf(container)` = outer − 2 × origin inset. This is the size anchored children's insets are computed against
  (piece-1 §4), so a Right-anchored child of a FixedSingle Panel stays exactly where WinForms puts it.
- **Dock padding** — GroupBox 3/19/3/3 (its `DisplayRectangle` at 96 DPI with the default font, measured, P10); others 0.
  Docked children dock inside it.
Consumers — the ONE rule, called by all: `FormDockLayout` (client sizes and dock origins, Designer and Runtime), the
canvas (so the designer draws children where they run, as Visual Studio does), the page emitter, the reflow script's JS
mirror (`FormDockScript.Core` — lock-step fixtures gain bordered and GroupBox containers), the library's run-time anchor
math (§5.7), and the WinForms region writer's Designer-resolved docked `Size` (piece-1 §4). ⚠ Like piece 1's `BoundsOf`
change, this MOVES children of bordered Panels and docked children of GroupBoxes on the `.blform` canvas by 1–19px —
intended; the plan lists every changed expectation. The 19px is font-dependent in WinForms; the page's legend is styled to
fit it, and a different WinForms font or DPI is a recorded divergence.

### 7.6 Radios group like WinForms; disabled containers
- A RadioButton with no `GroupName` gets `name` = its CONTAINER's id (the form area's for top-level radios), so radios are
  mutually exclusive per container as in WinForms (P4). A set `GroupName` still wins (web-only row).
- A design-time `Enabled=False` container renders its descendants disabled (P3), with their own design value in
  `data-vgs-enabled`, matching §5.4.

### 7.7 DOM-style pages are unchanged
A DOM-style form's page is emitted byte-for-byte as piece 1 emits it (golden hash, as piece 1's C8 guard). Reason: its
code addresses elements directly (`chkRemember.checked`), and moving the id onto a `<label>` would make that code read
`undefined` from a green build — the decline path of O2 must not change behaviour. Conversion (§8) is the gate to the
fixed rendering. The emitter learns the style through §6.1 (the build already knows each form's code-behind path,
`FormCodeBehind.PathFor`).

## 8. Conversion (O2) — sub-piece 2c

### 8.1 When it is offered
Opening a web form whose code-behind is DOM style (§6.1) in the designer (`EnterDesignModeForFormDocument`, K14) shows a
notification with actions **Convert** / **Not now** **[impl: the existing `ShowNotification(…, actions)` toast; no new
dialog service in the document VM]**. It is offered on OPEN only, once per open (not on reload — CLAUDE.md's open-route
rule). A refused document is not offered.

### 8.2 What Convert does (one pure function, one write)
`FormCodeConversion.Convert(document, codeBehindText)` → new text + findings. The IDE writes the result once; the CLI
exposes the same function as `design --convert <form>` **[impl]** so the conversion is testable through both entry points.
1. Adds the three `Using` lines and `Inherits Form` when absent (a different existing `Inherits` → refused with a
   finding; nothing written).
2. Regenerates both regions in the portable style (§6.2).
3. For each handler NAMED BY A BIND of the document whose declaration is SIMPLE — a `Sub` (any access) with exactly one
   parameter typed `DomEvent`, or no parameter for a Timer — rewrites ONLY its parameter list to
   `(sender As Object, <name> As <args>)`, keeping the user's parameter name and choosing `<args>` from the event's
   `WinFormsArgs` (`EventArgs` by default). Anything else (a Function, extra parameters, a lambda bind, a handler shared by
   binds of different args) is not rewritten and gets a finding.
4. Moves nothing else. Handler BODIES are never edited.
5. Timer: a wired DOM-style Timer implied `Enabled=true` (G13); the conversion sets `Enabled="true"` on the component in
   the document when it had a wired Tick, so the converted form still runs its timer (the "wired means running" rule,
   BL8027, applied as retarget applies it).

### 8.3 Findings in the Error List
Every line inside a handler body (and anywhere else in the file outside the regions) that uses the DOM directly is listed
as a warning with a suggested replacement — **new design code BL8034** (G16). Suggestions come from the catalog's
`DomMember` column (§10.3), never a hand list:
- `txtUser.value` → `txtUser.Text`; `lblMsg.textContent` → `lblMsg.Text`; `chkRemember.checked` → `chkRemember.Checked`;
  `btn.disabled = True` → `btn.Enabled = False`; `x.style.display = "none"` → `x.Visible = False`; `x.hidden` →
  `x.Visible` (inverted); `e.target` → `sender`; `doc.getElementById("x")` → `x`.
- A DOM use with no catalog equivalent → *"uses the DOM directly: wrap it in `#If WEB Then` and use `x.Element`"*.
Findings are recomputed whenever the designer regenerates the file and disappear when the line changes. They do not
block the designer; the BUILD will fail on most of them anyway (a `TextBox` has no `value`, and §4.8 rejects a handler
left as `(e As DomEvent)`), which is the point: nothing silently runs differently.

### 8.4 Declining
**Not now** writes nothing — the file stays byte-identical — and the designer keeps generating that file in the DOM style
(§6.3), and its page keeps piece-1 markup (§7.7), until the user converts. A file that is later converted by hand into the
portable shape is detected as portable (§6.1) and handled as such.

## 9. Desktop-only controls (O8, P-D6) — sub-piece 2d
- **Toolbox:** on a web form the toolbox lists every kind; desktop-only kinds (`SupportsTarget(Web)` false, C4) carry a
  "desktop" badge (C12 changes from hide to badge). Dropping one is ALLOWED (the BL8019 refusal for a kind that exists on
  WinForms but not the web is removed; kinds that exist on neither target are unaffected).
- **Property grid:** the selected control's header carries the same badge.
- **Canvas:** draws it from its catalog schematic, as on a WinForms form.
- **Web build:** a web form containing one stops with **BL8033** (error) — *"LoginForm: 'dgvUsers' is a DataGridView, which
  is desktop-only; a web build cannot include it."* — raised where web forms are loaded for a build (K12), on both the IDE
  and CLI routes. The HTML comment (P6) is no longer reached by a successful build.
- A component (Timer aside) keeps BL8029's existing warning path for tray kinds — no change there.

## 10. Catalog changes (one list drives both)

### 10.1 Event lists (F2)
The events O6 names become catalog rows on every positioned web kind: Click, TextChanged, MouseDown, MouseUp, MouseMove,
KeyDown, KeyUp, KeyPress, Enter — each a `FormEventDef` with WinForms' args/category/description (the parity oracle, C13,
checks them against the WinForms snapshot) and its `WebEvent`. The row's existing default event keeps `IsDefault`. This is
the shape the property grid's slice 5 ("D1 event lists", C1) was going to introduce; landing it here must be coordinated
with that plan (Q8). `FormEvents.WiredOn` (tray rule) is unchanged.

### 10.2 GroupBox's default event → Enter
`Enter`, `WebEvent: "focusin"` (with the relatedTarget rule of §5.5), `IsDefault: true`; its oracle exemption is
removed (a stale exemption FAILS the parity test, C13). **Click stays listed** (non-default) so existing documents that
bind it keep working **[impl]**.

### 10.3 `DomMember` per property
Each property row records the live DOM member its web value lives in (`value`, `textContent`, `checked`, …). Read by the
conversion's suggestions (§8.3) and by a gate that runs the library against a recording stub DOM and checks that writing
the property writes that member (§11.1) — which is what makes the HAND-WRITTEN library (O4) falsifiable against the
catalog.

### 10.4 Other row changes
Timer `Enabled` applies to the web (C8/G13); `WebDisplay` (§5.4); the caption wrapper (§7.1); the Multiline tag (§7.4);
`BorderStyle` CSS (§7.3); client-area insets (§7.5).

## 11. Testing

Every Edge / WinForms-window test: `[Category("Integration")]`, Windows-gated, and SKIPS with a reason where Edge or the
Windows Desktop SDK is absent (CLAUDE.md). Node-tier tests run everywhere.

### 11.1 Catalog-coverage gate (O4, O9)
Driven from the catalog (never a hand `[TestCase]` list): for every web kind, one generated shared program that sets
every property that applies to BOTH targets to the sweep's sample value (C13, :601-610), reads it back and prints it, and
wires every listed event.
- **Compiles** for desktop (real CLI `--target=csharp` + csc, as `WinFormsCatalogSweepTests`) and for the web (real CLI,
  JavaScript).
- **Runs** on both and reads the values back: desktop in the WinForms reference window (Task 12 harness); web in Edge
  (Task 13 harness) — the authority — and under node with a recording stub DOM (fast tier) that also asserts each
  property's `DomMember` write (§10.3).
- **Events:** each listed event is triggered once on each side and its handler runs once with `sender` = the control.
- **Web-only rows** (GroupName, MultiSelect) compile and run on the web only, inside `#If WEB`; **WinForms-only rows**
  referenced from shared code are the §5.9 web-build error, asserted by name.
- A new catalog row with no library member fails this gate — "add a row" stays the whole workflow.

### 11.2 The behaviour twin (O9)
The same form (a pixel `.blform` and its Canvas `.blwebform` twin, piece 1's retarget) and the SAME code-behind text, run
in the real WinForms window and in Edge, driven by one neutral scenario: set Text; toggle Visible/Enabled/Checked; click;
type; select; move a Right-anchored control at run time, then resize the window. Compared: property values read back, every
control's rectangle (±1px, form-client coordinates, piece 1's walk), and the EVENT LOG (sender, event, key args) — order
and counts.
- Input is real on both sides **[impl — the riskiest harness decision]**: WinForms by window messages
  (`WM_LBUTTONDOWN`/`UP`, `WM_KEYDOWN`/`WM_CHAR`/`WM_KEYUP` to the control handle — what Task 8 used for Click); Edge by
  the DevTools protocol's `Input.dispatchMouseEvent`/`dispatchKeyEvent`/`insertText` over `--remote-debugging-port`
  (trusted events). Synthetic `dispatchEvent` is the fallback only where CDP cannot express a step, and every step that
  uses it is named in the test.
- Recorded divergences are asserted as literals (a change turns the test red): NumericUpDown per-keystroke ValueChanged,
  the centring half pixel (piece 1), GroupBox caption offsets beyond ±1px if any.

### 11.3 Anchor lock-step (O6)
The library's axis rule (§5.7) run under node against `FormAnchorCss.Positioned` over one fixture table (all 16 edge
combinations × near/far/centred offsets × negative sums × sizes ≤ 0), in the style of `FormDockScriptTests`. The client
inset rule (§7.5) gets the same treatment against `FormDockScript.Core`.

### 11.4 Rendering (Edge)
Caption present and toggling once; legend in the frame; bordered Panel border and child offsets equal to the WinForms
window (P10's table becomes EQUALITY, or each residual gap is re-recorded as a literal with its measurement); GroupBox
positioned children uninset and docked children inside 3/19/3/3; radios exclusive per container; a disabled Panel's
children disabled; `<textarea>`/`type=password`; Visible toggle incl. the phone-container case (Q1).

### 11.5 Compiler (2.0)
Per §4, on JS and C#, through the CLI and the IDE build; `CompileToCppOptimized` where a front-end change can reach C++.

### 11.6 Code generation and conversion
Portable region golden files per kind; the web-init ⊂ WinForms-init subset relation (§6.2) per catalog fixture; DOM-style
regions, stubs and pages byte-for-byte unchanged; the library-inclusion trigger (a JS project without the `Using` line
is byte-identical); conversion: convert (regions, simple handlers, Using/Inherits, Timer Enabled), each finding kind with
its suggestion, non-simple handlers left alone and listed, decline = byte-identical, CLI and IDE produce the same bytes.

### 11.7 Build errors
BL8033 on both routes; `.Element` in a desktop build (field, parameter, `Me`); an unavailable member and an undeclared
WinForms type in a web build, each message naming the member and suggesting `#If DESKTOP`; `#If WEB`/`#If DESKTOP` choosing
correctly on each target; the library-collision error.

### 11.8 End to end
Design a form in the IDE (real view, headless) → double-click a Button (stub `(sender As Object, e As EventArgs)`) → write
a handler body that uses only the portable API → build web (CLI and IDE) and desktop → run both: the page (Edge) and the
window show the same result of the click. And the owner's question "who calls it in a shipping build" (CLAUDE.md) for
each new piece: the hook, the conversion offer (the real `OpenFileAsync` route), the CLI verb, the badges.

### 11.9 Reviews and mutations
Per task: implementer, spec review, quality review, mutation checks on the load-bearing rules — the inclusion trigger;
base-first order; `RemoveHandler` identity; `#If` prefix traps; the anchor axis (both sides); client insets; the
`relatedTarget` rule; caption click de-duplication; programmatic-set events only on change; the style detector; decline
byte-identity; the `.Element` receiver-name check.

## 11a. Notes for the plan (reviewers)

*(Left for the spec reviewers.)*

## 12. Risks
- **The JS backend's traps are the library's substrate.** Every class of defect measured in §3 hits library code first;
  2.0 fixes them, but the library will find more. Mitigation: the library is built and RUN under node from its first
  commit, and every behaviour is pinned by the twin, never by string assertions (CLAUDE.md, "a green build is not a
  running page").
- **A hand-written library drifts from the catalog.** Mitigation: §11.1's gate, `DomMember`, and the collision error.
- **Two mirrored pairs grow** (anchor axis; client insets in `FormDockScript.Core`). Mitigation: lock-step tests from the
  first commit.
- **Harness fidelity.** CDP and window-message input are new; a wrong harness can make both sides agree for the wrong
  reason. Mitigation: mutation checks on the harness itself (as Task 12 did: never `Show()`n, measured before the resize).
- **Canvas expectations move** (§7.5) on WinForms documents too; must be listed and intended, not rubber-stamped.
- **`Char` (Q4) and `Decimal` (Q5)** may leave KeyPress and NumericUpDown with no spelling that compiles on both targets.
- **The IDE drop**: the library under `IDE/lib/js/forms/` is load-bearing and hand-refreshed; a stale drop ships an old
  library with a green build. Mitigation: the drop refresh step lists it; an Integration test compares drop and source
  on the gate run.
- **LSP**: the editor does not include `dom-core.bli` today (K2) and will not see the library either; completions on a
  web form's controls will show nothing library-specific until that is added (Q9).
- **Size**: the whole library is emitted into every opted-in page; no tree-shaking in this piece.

## 13. Open questions for the owner
1. **Q1 — `Visible` on containers below the phone breakpoint.** O6's explicit `display` overrides the phone rule's
   `display: flex` for a container (§5.4). Keep O6's rule and accept the gap for containers on phones, or hide/show with
   the `hidden` attribute (which the reflow script and the phone rule already honour, and which never writes `display`)?
2. **Q2 — controls created at run time** (`Dim b As New Button(): Me.Controls.Add(b)`): out of scope (clear run-time
   error), or in scope (the library creates the element — a second emitter of markup, mirrored with `FormAssetEmitter`)?
3. **Q3 — methods.** Beyond §5.4's list, which WinForms methods should work on the web: `Show`/`Hide`, `BringToFront`/
   `SendToBack`, `TextBox.Clear`/`SelectAll`, `MessageBox.Show` (→ the browser's alert), `Form.Close`? Default: none —
   each is the §5.9 web-build error.
4. **Q4 — `KeyPress.KeyChar`.** WinForms types it `Char`; the JS backend refuses `Char` (BL7004). Add `Char` support to
   the JS backend (a one-character string), or ship KeyPress with `KeyChar As String` on the web (then shared code
   comparing it to a `Char` literal compiles only on desktop)?
5. **Q5 — NumericUpDown `Value`** is `Decimal` in WinForms and a Double on the web. Accept, or restrict the web control to
   whole numbers as the catalog's defaults already are (FCC:1498-1501)?
6. **Q6 — run-time `Dock` changes** on the web: web-build error in this piece (default), or re-dock through the reflow
   script (its data would have to become live)?
7. **Q7 — `DESKTOP` on C++/native targets**: defined for every non-web backend (default), or only for WinForms/C# builds?
8. **Q8 — the catalog's event lists** (§10.1) are the property grid's slice-5 shape; land them here (default), or wait
   for slice 5?
9. **Q9 — editor support**: should the LSP include the library (and `dom-core.bli`) for web projects in this piece, or is
   it a follow-up?

## 14. Out of scope, and seams left
- **Piece 3** (toolbar switch, one file building both ways): the seam is §6.2's subset relation — the only target-specific
  lines a portable file has are the WinForms init's geometry/property lines, ready to sit under `#If DESKTOP`.
- **Piece 4** (retire `.blwebform`): the conversion function (§8.2) and the style detector are reusable by
  convert-on-open; nothing here assumes `.blwebform` persists.
- WinForms → web retarget still produces Grid (piece 4); run-time-created controls (Q2); tree-shaking; per-control phone
  overrides; LSP library awareness (Q9); a `MessageBox` (Q3); DataGridView and the other desktop-only kinds on the web
  (P-D6's "web versions arrive over time").
