# The component tray — design (Task 25 of the form designer)

**Date:** 2026-09-19 · **Branch:** `feat/form-designer` · **Status:** designed; supersedes the one
line in the 2026-09-11 spec that says `<Components>` is "reserved and empty in v1".

## The brief, verbatim

> Non-visual components (Timer, ToolTip, ErrorProvider, BackgroundWorker, and their web
> equivalents) belong in a tray strip below the design surface, exactly as VS does it — they are
> part of the form but have no position on it. The `<Components/>` slot is already reserved and
> empty in both formats, so this is a designer surface plus emission, not a format break.
> Components are selectable, appear in the property grid, and participate in undo/redo and delete.
> Emission follows the same region rules as controls: declare field → construct → set properties →
> wire handlers. ⛔ Same trap list applies — never `With`, never `Handles`, handlers before regions.

## Measured facts this design rests on (2026-09-19, real CLI → csc → RUN; real CLI → node)

Every row below was generated, compiled and **executed**, not recalled. The probes live in the
recon transcript; the gates in §7 re-measure them on every run.

| # | Shape | BasicLang | csc | Ran |
|---|---|---|---|---|
| M1 | `tmr = New Timer()` · `Interval = 1` · `Enabled = True` · `AddHandler tmr.Tick, AddressOf tmr_Tick` | compiles, 4× BL6017 warnings (it resolved `Timer` to `System.Threading.Timer`) | ✅ | **TICK** fired under `Application.DoEvents()` |
| M2 | `tip = New ToolTip()` · `InitialDelay = 500` · `tip.SetToolTip(btn, "…")` | ✅ | ✅ | `GetToolTip` returned the text before and after `Show()` |
| M3 | `err = New ErrorProvider()` · `BlinkStyle = ErrorBlinkStyle.NeverBlink` · `BlinkRate = 250` · `SetError` | ✅ | ✅ | `GetError` returned it |
| M4 | `bw = New BackgroundWorker()` · `WorkerReportsProgress/WorkerSupportsCancellation` · `AddHandler bw.DoWork` with **either** `e As EventArgs` **or** `e As DoWorkEventArgs` | ✅ (needs `Using System.ComponentModel` in the source) | ✅ both | **WORK, DONE** |
| M5 | VS's shape: `components = New System.ComponentModel.Container()` into `IContainer`, `New Timer(components)` | ⛔ **refused**: *Cannot assign value of type 'Container' to 'IContainer'* | — | — |
| M6 | web: `Dim w As Window = ::window` · `id = w.setInterval(AddressOf tmr_Tick, 100)` with `Private Sub tmr_Tick()` — the TYPED route | ✅ | — | **TICK** under node; `w.clearInterval(id)` works |
| M7 | web: the typed route with `Private Sub tmr_Tick(e As DomEvent)` | ⛔ **refused**: *Argument 1: cannot convert from 'Action<DomEvent>' to 'Action'* | — | — |
| M7′ | web: the UNTYPED hatch `::window.setInterval(…)` with `(e As DomEvent)` | ✅ (the hatch checks nothing) | — | TICK — but so would a typo |
| M8 | web: `::setInterval(…)` unqualified, `w.setTimeout(…)`, and the handler declared BELOW the call | ✅ | — | **TICK** — a zero-parameter callback has nothing for the ordering rule to erase; `addEventListener` with a handler below still fails as BL8013 says |
| M9 | web: `btn.setAttribute("title", "…")` | ✅ | — | `getAttribute("title")` returned it |
| M10 | WinForms: M1 plus a user `Using System.Threading` | ✅ (no diagnostic) | ⛔ **CS0104** `'Timer' is an ambiguous reference between 'System.Windows.Forms.Timer' and 'System.Threading.Timer'` | — |
| M11 | WinForms: unqualified `BackgroundWorker` with only the scaffold's three `Using`s | ✅ (no diagnostic — the type is unknown, everything types as Object) | ⛔ **CS0246** | — |
| M12 | WinForms: `System.ComponentModel.BackgroundWorker` fully qualified, no `Using` | ✅ | ✅ | **WORK, DONE** |
| M14 | WinForms: `Private components As System.ComponentModel.Container` · `components = New …Container()` · `New Timer(components)` — the CLASS-typed field | ✅ | ✅ | **TICK** (VS's `IContainer` field is M5; this is the one shape of the idiom BasicLang accepts) |
| M15 | WinForms: `Dim s As String = tip.GetToolTip(btn)` / `Return err.GetError(btn)` from a `Function … As String` | ⛔ *Cannot assign value of type 'Object' to variable of type 'String'* — a compiler gap user code will meet | — | — |
| M13 | WinForms, the exact emitted shape: `Private tmr As System.Windows.Forms.Timer` · `tmr = New System.Windows.Forms.Timer()` · `Private bw As System.ComponentModel.BackgroundWorker` · handler `bw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs)` — in a `Public Class … Inherits Form` with only the scaffold's three `Using`s | ✅ (BL6016 warning: the WinForms type is not in the referenced assemblies; the C# keeps every qualified name verbatim, field and parameter alike) | ✅ 0 errors | **WORK, TICK** |

All web probes ran inside a `Public Class` constructed from `Main()` — the scaffold's shape, and
the shape the JS bare-global self-call defect is specific to; the emitted JS bound the callback
(`this.tmr_Tick.bind(this)`). M10–M12 are why **every component type is emitted fully qualified**: BasicLang reports nothing in
either failure, and the C# backend also adds `using System.Threading;` on its own whenever the
generated body contains the substring `Thread` (`CSharpBackend.cs:520`, `NetAmbientNamespaces.cs:55`).
M6/M7 are why **the web Timer's handler is parameterless and the emitted call is the typed one**:
`dom-core.bli` declares `Window.setInterval(handler As Action, milliseconds As Integer) As Integer`
and `clearInterval(handle As Integer)` (`dom-core.bli:105-108`) — `Action`, not
`Action(Of DomEvent)` — and only the typed call lets the compiler say so.

## 1. What a component IS in the model

**A component is a `FormControl` with no geometry, no children and no tab index, living in
`FormDocument.Components : List<FormControl>` (replacing today's `List<XElement>`).** Its catalog
row says `IsComponent: true`.

Why reuse rather than a new type — three alternatives were weighed:

- **Reuse `FormControl` (chosen).** The property grid, `FormPropertyRow`, binds, `Clone`, the
  clipboard, retarget's property/bind rules and the region writer's per-control body all take a
  `FormControl`. A component needs every one of those and nothing a control has that it lacks
  except position, children and tab order — which are already optional or absent on the class
  (`Geometry` is nullable; `Children` is a list; `TabIndex` is an int the writer can decline to
  write). The property grid already handles `Geometry == null` and pins it
  (`FormPropertyGridTests.Rows_OfferNoGeometry_…`).
- **A new `FormComponent` type.** Cleaner invariants, but `PropertyGrid.SelectedControl`,
  `FormSelection`, `DeleteControl`, `CopyControls`, `FormRetarget.ConvertControls` and
  `FormClipboard` are all typed on `FormControl`; a second type means an interface across Shell,
  compiler and tests, or duplicating each. Rejected as a refactor the feature does not need.
- **Keep XElements plus an ad-hoc tray model.** Two truths for one document. Rejected.

The invariant lives in ONE place, the reader (§3): a component element never acquires geometry or
a tab index, and a component kind can only be read from `<Components>`. Every walker then chooses
explicitly which lists it visits (§2), rather than discovering components by accident.

**The id namespace is shared.** Controls and components become fields of one class, so `FindById`,
`ListContaining`, `MakeUniqueId`'s callers and the reader's duplicate-id check (BL8017) cover both
lists. `AllControls()` keeps its meaning (the visual tree); `AllComponents()` is added; nothing
walks "all elements" implicitly.

## 2. Who visits components — decided per walker, not by default

| Walker | Components? | Why |
|---|---|---|
| `FormDocumentReader` `<Components>` | **read as components** | §3 |
| `FormDocumentWriter.Apply` | **yes — `ApplyComponents`** | today `<Components>` is write-never; without this nothing in the tray persists and undo (which is text) cannot see it |
| `FormDocumentWriter.Create` | **yes** | today it writes an empty `<Components/>` and drops `model.Components` — the retarget pair loses them |
| `RegionWriter` fields / init / `CheckTargetProperties` / `CheckHandlerOrdering` | **yes, first** | VS constructs components before controls; the `Controls.Add` run never sees them. Plus two checks of its own (review): `CheckComponentTargets` (BL8029, a kind the target lacks) and `CheckComponentBinds` (BL8028, a web bind the template cannot wire); `IsEmittedBind` is the one predicate the emitter, the ordering check and the warning share |
| Reader `CheckDuplicateIds`, `FindById`, `ListContaining` | **yes** | one class, one field namespace; Delete/Cut go through `ListContaining` |
| `RenumberTabIndexes`, `FormPlacement.NextTabIndex` | no | a component has no tab order |
| Canvas `Layout` / `HitTest` / `ContainerAt` / render | no | nothing to draw; a selected component draws no handles (measured by reading the guards; §7 adds the headless test) |
| `FormAssetEmitter` markup / CSS | no | a component has no element; the web Timer is script (§4) |
| `FormRetarget` | **yes** | same kind/property/bind rules; no geometry pass; WinForms-only components become BL8023. Plus one rule of its own (review): "wired means running" crosses by `FormWebScript.Implies` in both directions and is named BL8027 — a wired web Timer arrives on the window `Enabled=true`; a wired WinForms Timer that is not enabled is reported as one the page WILL run |
| `FormClipboard` | **yes** | copy/cut/paste route a component kind to `Components`, never to `Controls` |

## 3. The document

```xml
<Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
  <Controls>…</Controls>
  <Components>
    <Timer Id="tmrPoll" Interval="500" Enabled="true">
      <Bind Event="Tick" Handler="tmrPoll_Tick"/>
    </Timer>
    <ToolTip Id="tip" InitialDelay="300"/>
  </Components>
  <Resources/>
</Form>
```

- A `<Components>` child is read with the same element grammar as a control (Id, catalog
  properties, `<Bind>`, unknown attributes and children round-trip) **but** never gets geometry or
  a tab index: `X`/`Col`/`TabIndex` on a component element are unknown attributes and round-trip
  untouched. Concretely, the component read path does not take the `IsStructural(name, target)`
  skip at `FormDocumentReader.cs:355-358` (every structural name but `Id` routes to
  `UnknownAttributes`), and the component write path skips the unconditional `TabIndex` on BOTH
  routes — `Create`'s at `FormDocumentWriter.cs:501` and `Apply`'s `SetIntAttributeIfChanged` at
  `:357`, which would otherwise rewrite a component's `TabIndex="5"` (an unknown attribute) to
  `"0"` on the first real edit — and the geometry switch. The round-trip fixture carries exactly
  such an attribute so the `Apply` route is the one under test.
- **`BL8020 ComponentMisplaced`** (a refusal, from the free range): a component kind under
  `<Controls>`, or a non-component kind under `<Components>`. Both would generate code csc rejects
  (`Me.Controls.Add(tmr)`; a `Button` with no `Controls.Add`), and a refusal is the honest answer to
  a document that says two different things about where a thing lives.
- The writer edits `<Components>` in place exactly as `ApplyControlList` edits `<Controls>` —
  find by Id, set-if-changed, remove what the model dropped, reorder, insert the element before
  `<Resources>` when a document has none — so the D9 algebra (byte-identical round trip, no-op
  writes nothing, `Read∘Apply == Apply∘Read`) holds for a document with components. The first test
  written is a **non-empty** `<Components>` round trip, because today no fixture has one.
- The spec's "reserved and empty in v1" line is amended in the same change.

## 4. The catalog rows

`FormControlDef` gains `IsComponent` (bool), `WinFormsEventArgs` (the `e` type of the default
event's handler; null means `EventArgs`), `WebHandlerTakesEvent` (bool, default true; false when the
web callback is a plain `Action`, M6/M7), and `WebScript` — how a script-backed component is
constructed on the web, for kinds that have no element. `SupportsTarget(Web)` becomes `HtmlTag !=
null || WebScript != null`. Four new schematics — `Clock`, `Hint`, `Alert`, `Worker` — name the
tray glyphs, one per kind as every control kind has its own (four Timers in a tray must not look
like four of the same thing, and the toolbox's "every row wears its own mark" test says so); none
is ever drawn on the canvas.

| Kind | `WinFormsType` (qualified) | Properties (all csc-gated) | Default event | Web |
|---|---|---|---|---|
| **Timer** | `System.Windows.Forms.Timer` | `Interval` Int 100 · `Enabled` Bool false *(WinForms only)* | `Tick` / `tick` (`WebHandlerTakesEvent: false`) | **yes** — `WebScript`: field `Integer`, construct `w.setInterval(AddressOf {handler}, {Interval})` over the typed `Window` the init region declares |
| **ToolTip** | `System.Windows.Forms.ToolTip` | `InitialDelay` 500 · `AutoPopDelay` 5000 · `ReshowDelay` 100 · `ShowAlways` false · `IsBalloon` false · `ToolTipTitle` String | `Popup` (`PopupEventArgs`) | no |
| **ErrorProvider** | `System.Windows.Forms.ErrorProvider` | `BlinkStyle` Enum {BlinkIfDifferentError, AlwaysBlink, NeverBlink} (`ErrorBlinkStyle`) · `BlinkRate` 250 | `RightToLeftChanged` | no |
| **BackgroundWorker** | `System.ComponentModel.BackgroundWorker` | `WorkerReportsProgress` false · `WorkerSupportsCancellation` false | `DoWork` (`System.ComponentModel.DoWorkEventArgs`) | no |

- **No `Common(…)`.** `Visible`, `ForeColor` and `BackColor` do not exist on a component; a row
  written with the shared helper would compile green in BasicLang and fail only at csc.
- **Qualified type names** avoid the `System.Threading` ambiguity (M10) and the missing
  `System.ComponentModel` import (M11; the scaffold imports neither). M13 measured that BasicLang
  passes a qualified unresolvable name through unchanged in a field, a constructor and a handler
  parameter; the sweep re-measures it on every run.
- **Web equivalents, honestly:** a Timer IS `setInterval` (M6–M8). A ToolTip's only honest web
  form is the `title` attribute on OTHER controls (M9) — an extender property the catalog cannot
  express yet (§8) — so ToolTip is WinForms-only; an ErrorProvider and a BackgroundWorker have no
  honest single equivalent (a Web Worker is a separate script). The retarget reports each as
  BL8023, exactly as it does for the eight WinForms-only controls.
- **Every property name is unfalsifiable except through csc**, so `WinFormsCatalogSweepTests`
  learns the component shape (no geometry, in `Components`) and gates each row as it gates a
  control. `ErrorBlinkStyle` member names and `PopupEventArgs` are exactly the sort of thing that
  gate exists to catch.

## 5. Emission

**WinForms** (`RegionWriter`), components before controls in both regions, VS's order:

```vb
Private tmrPoll As System.Windows.Forms.Timer          ' controls region, before the controls
…
Private Sub InitializeComponent()
    Me.Text = "Sign in"
    Me.ClientSize = New Size(400, 300)
    tmrPoll = New System.Windows.Forms.Timer()          ' construct
    tmrPoll.Interval = 500                              ' properties, one statement each
    tmrPoll.Enabled = True
    AddHandler tmrPoll.Tick, AddressOf tmrPoll_Tick     ' wire — never Handles
    btnLogin = New Button()                             ' then the controls as today
    …
End Sub
```

No `components` container (M5 refuses it; every one of the four has a parameterless
constructor, M1–M4), no `Controls.Add`, no geometry. The handler stub takes the row's
`WinFormsEventArgs`: `Private Sub bw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs)`
— qualified, because the scaffold does not import it. Both stub shapes compile (M4); the typed one
is the useful one, since it reaches `e.Argument`.

**Web** (Timer only):

```vb
Private tmrPoll As Integer                              ' the interval handle
…
    Dim doc As Document = ::document                    ' as today
    Dim w As Window = ::window                          ' only when a script component exists
    tmrPoll = w.setInterval(AddressOf tmrPoll_Tick, 500)
```

The construct line is emitted only when the Timer has a `tick` bind — `setInterval` needs a
callback, and a Timer with no handler does nothing visible on either target. The FIELD is declared
unconditionally, so a user's `::window.clearInterval(tmrPoll)` in a handler compiles before the
handler is wired (`w` is a local of `InitializeComponent`, not a field). `Enabled` is WinForms-only: a JS interval is not a
thing that can exist disabled, so the honest web statement is "wired means running". The stub is
**parameterless**, `Private Sub tmrPoll_Tick()`: `Window.setInterval` takes an `Action`, the typed
call refuses `Action(Of DomEvent)` (M7), and the typed call is used precisely so that the compiler
— not a runtime `TypeError` — is what rejects a wrong handler. `FormHandlers` reads the row's
`WebHandlerTakesEvent` to write it; every element-backed kind keeps `(e As DomEvent)`. The
ordering check (BL8013) still runs over components; a parameterless callback is not bitten by the
erasure (M8), so the check is stricter than the compiler there, which is the safe side. Stopping the
timer is the user's `w.clearInterval(tmrPoll)` — `::window.clearInterval(tmrPoll)` in a handler.

`WebScript.Construct` is a template with two placeholder kinds — `{handler}` and a property name in
braces — filled from the bind and the properties (falling back to the catalog default, and
REPORTING a Degraded value as BL8009 the way `AppendProperties` does on WinForms, so one document's
Error List does not differ by target), and it may name `w`, the typed `Window` the region declares
once whenever any script component exists. The writer never switches on `Kind`.

**What the web cannot wire is named, never dropped (review, 2026-09-19).** A web component is wired
ONLY through its template on its default event, so a `<Bind>` on any other event — the reader
accepts any event name — is warned as BL8028 and is NOT collected by the BL8013 ordering check,
which used to refuse the write over a wiring the region never contained. A component whose kind has
no web row (a hand-edited `<ToolTip>` in a `.blwebform`) is warned as BL8029: its field stays
declared so code naming it builds, and nothing constructs it. `RegionWriter.IsEmittedBind` is the
one predicate all three — the emitter, the ordering check, the warning — share.

**"Wired means running" is a row-stated rule, and the retarget crosses it.** `FormWebScript.Implies`
names the property the other target uses to say what the construct says on the web — for the Timer,
`Enabled=true`. `FormRetarget` applies it both ways and names it (BL8027): web → WinForms sets the
property so the window's Timer runs as the page's did; WinForms → web with the property held and a
bind crossing reports the property as *the wiring itself* rather than a loss (no BL8024); WinForms →
web wired but NOT enabled reports that the page's Timer will run from load. Unwired, nothing runs on
either side and the property is a plain BL8024 loss, as before. The fixed-point round trip is now
byte-identical INCLUDING `Enabled`.

## 6. The surface

- **Tray.** Column 1 of the design view becomes a two-row grid: canvas above, a tray `Border`
  below, visible when the document has components. Items: glyph + id, laid out horizontally, the
  selected one highlighted. Click → `Selection.Set(component)`; the HOST view model's own `Changed`
  subscription pushes the primary into `PropertyGrid.SelectedControl` (one selection path, not two —
  and a DROP selects through the same store, `SelectInDesigner`: the review found a drop that wrote
  the grid alone, after which the tray's Delete, which passes the grid's control, removed the wrong
  component while the other was highlighted).
  Double-click → `ActivateControlCommand` (the default handler, Task 22). `Delete` on the tray →
  `DeleteControlCommand`. The tray is a drop target for COMPONENT kinds only, through its OWN
  command, `TrayDropCommand(kind)` — `FormControlDropRequest` carries no origin and
  `FormPlacement.Place` cannot tell a tray drop from a canvas drop, so the tray must not reuse the
  canvas's command: a component kind is placed (the point is irrelevant), and a control kind is
  refused through the same BL8019 path a bad canvas drop takes — "a Button has a position; drop it
  on the form" — never placed at (0,0).
- **`FormTrayViewModel`** owns an observable item list rebuilt from `DesignDocument.Components` on
  `SyncDesignerPanels` and on every `DesignModelRevision` bump, and highlights from `Selection`.
  Reachability is tested the way every UI seam on this branch is: a test drives the command and a
  test reads the AXAML for the binding.
- **Toolbox.** A third category, "Components", after Containers, from the same catalog loop — its
  own sort bucket, and a description rule for script-backed rows ("script", where an element row
  shows its tag; a WinForms component shows its qualified type verbatim, which is what it is).
- **Placement.** `FormPlacement.Place` on an `IsComponent` row adds to `Components` with a minted
  id and ignores the point, on either target the row supports. Dropping a Timer anywhere on the
  canvas therefore lands it in the tray, as VS does.
- **Property grid.** Name (frozen) and the catalog rows; no geometry rows (which `Geometry ==
  null` already gives) and no TabIndex row (which `AddIntrinsicRows` adds unconditionally today, so
  it gains an `IsComponent` branch).
- **Undo/redo/delete** cost nothing extra: delete goes through `ListContaining`, undo rewinds the
  text the writer now includes components in.

## 7. Verification — run, not read

- Reader/writer: non-empty `<Components>` byte-identical, no-op, `Read∘Apply == Apply∘Read`;
  add/remove/reorder persists; unknown content on a component round-trips; BL8020 both ways;
  duplicate id across the two lists is BL8017.
- Catalog gates, all catalog-driven: sweep (every component property through csc, in `Components`),
  default-event stub through csc, glyph, coverage, placement, property grid, retarget sweep. Two
  gates build each kind straight into `Controls` and must learn the shape rather than be bypassed:
  `FormCanvasRenderTests.EveryControlKindRendersDistinctly` excludes `IsComponent` rows (a component
  is never drawn, and BL8020 refuses one in `<Controls>` for the same reason) and gains a test that
  `Layout` never yields bounds for one; `FormRetargetTests.EveryCatalogKind_Retargets_…` builds a
  component row into `Components` and reads the crossed control from the list the row's
  `IsComponent` selects. The assertions themselves — what was lost, what crossed — are unchanged.
- Headless: a component selected on the canvas draws nothing — a PIXEL claim, the frame with it
  selected hashes equal to the frame with nothing selected — and throws nothing; Delete from the
  tray removes it; the tray shows the document's components; the AXAML binds every tray command
  WITH the grid's control as the Delete parameter.
- The review's repro, as a test: click a tray item, drop another component, click the first again,
  Delete through the grid's control — the clicked one goes, the selection empties. Every allowed
  value of every Enum row through csc, one control per value.
- **Acceptance, executed:** a WinForms form with a Timer (`Interval=1`, `Enabled`, a Tick handler
  that prints) through the real CLI, csc and a running window, asserting the tick; a web form with a
  Timer built by the real CLI and run under node with a `setInterval` stub, asserting the tick.
- Mutation kills for every new test, as on Tasks 21–26.

## 7a. Adversarial review after the four commits (2026-09-19) — what it found and what changed

Four finders over the diff, two skeptics per finding, majority to confirm: 14 raised, 8 confirmed.

| Finding | Disposition |
|---|---|
| A drop wrote only the property grid; a tray click wrote only `Selection`; `Set` is a no-op for the control already selected; the tray's Delete passes the grid's control → deleted the wrong component (HIGH) | `SelectInDesigner` + the host's `Changed` subscription (§6); Delete empties both stores |
| A freshly dropped component was never highlighted in the tray | same fix |
| Web component bind on a non-default event dropped silently yet drove BL8013 | BL8028 + `IsEmittedBind` (§5) |
| Web → WinForms: a wired Timer arrived `Enabled` absent and never fired, unnamed | `FormWebScript.Implies` + BL8027 (§5) |
| WinForms-only component in a `.blwebform` read with no finding | BL8029 (§5) |
| "DrawsNothing" never asserted (frame existence only) | frame-hash equality (§7) |
| `EmitsOnlyItsField` asserted neither "only" nor the `Window` local | pinned |
| csc sweep compiled only the first Enum value | every value, one control per value (§7) |

Refuted and left: a nested component through `FormClipboard.FromElement` (unreachable —
followups 21); the retarget sweep not asserting the other list empty (the sibling pins cover it);
the no-op Save test, the AXAML `CommandParameter` and the web Degraded template each split 1–1 and
were tidied anyway because each was one line.

## 8. Deliberately out of scope, and where it goes

- **Extender properties** — `ToolTip on tip` / `Error on err` per control, i.e.
  `tip.SetToolTip(btn, "…")`, which on the web is `title` (M9). Needs a `FormPropertyDef` facet
  that emits against another component; recorded as followup 19. Until then a ToolTip in the tray
  is what VS gives you before you set a tooltip on anything: present, inert, callable from code.
- **`components` disposal container** — BasicLang cannot express VS's `IContainer` field (M5); a
  `Container`-typed field works (M14) but diverges from the idiom for no gain in v1. The four
  components leak nothing observable in a form's lifetime. Followup 20 records the analyzer gap
  (`Container` is not known to implement `IContainer`) and the `Container`-typed alternative.
- **Reading a component's extender value back into a typed variable** is refused by BasicLang
  (M15) — a compiler gap the tray will be blamed for; followup 20 records it beside the container
  gap.
- **Per-kind event tables beyond the default event** — followup 18 already.

## Decisions taken without a human in the loop

The brief left three readings open and each was settled by measurement or an existing principle:
components are `FormControl`s (§1, by reuse cost); web equivalents follow the Task 23 rule — honest
or absent (§4); the disposal container is skipped because the compiler refuses it (M5). Any of the
three can be revisited from this document without unpicking the others.
