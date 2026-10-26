# Task 38 preflight: NumericUpDown, DateTimePicker, TrackBar, ProgressBar

Plan: `2026-09-29-portable-control-library.md`, Task 38. Spec §4.11, §5.4, §5.6, O14, C8/C9.
Base: `feat/portable-controls` @ `238857c1`, with the Task 37 changes uncommitted. Every
anchor below was re-read on this tree.

## What already exists

| Fact | Where |
|---|---|
| The four kind files are one-line shells | `BasicLang/lib/js/forms/{NumericUpDown,DateTimePicker,TrackBar,ProgressBar}.bas` |
| `Element.value` is declared; there is no `valueAsDate` | `BasicLang/lib/js/dom-core.bli:35-81` |
| NumericUpDown's four numeric rows are catalog `Int`, with `min`/`max`/`value`/`step` attributes | `FormControlCatalog.cs:1963-1994` |
| NumericUpDown also carries web `ReadOnly` and `TextAlign` | `FormControlCatalog.cs:1988-1991` |
| DateTimePicker has no web property rows and no `Value` catalog row; its only event is `ValueChanged`/`change` | `FormControlCatalog.cs:1996-2020` |
| TrackBar has `Minimum`/`Maximum`/`Value`, `Scroll`/`input` as default, and `ValueChanged`/`change` | `FormControlCatalog.cs:2022-2064` |
| ProgressBar has `Maximum`/`Value`; `Minimum` and `Style` are WinForms-only; its only event is `Click` | `FormControlCatalog.cs:2066-2086` |
| The portable emitter already writes every web `HtmlAttribute` and `readonly` | `FormAssetEmitter.cs:512-585` |
| The recording DOM has no `valueAsDate` | `PortableControlNodeTests.cs:46-293` |
| The catalog gate is driven by the seven first kinds plus the four Task 37 kinds; Task 38 adds the four value kinds | `PortableCatalogCoverageTests.cs:33-36` |
| The coverage gate assumed every kind has a web `Style` row; ProgressBar is the exception | `PortableCatalogCoverageTests.cs:136-153` |
| The method manifest has 23 rows; Task 38 adds no methods | `PortableMembers.cs` |

## Decisions

1. **Keep the catalog rows `Int`.** The catalog is design-time whole-number data and the
   existing tests deliberately pin `Int` over `Decimal` (`FormEditVerdictTests`,
   `FormAssetEmitterTests`, `WinFormsCatalogParityTests`). The runtime library uses
   `Decimal` for `Minimum`, `Maximum`, `Value` and `Increment` per O14. A fractional
   design-time value therefore remains unsupported; the library still reads and writes
   fractions exactly at run time.
2. **NumericUpDown reads the element text exactly.** `Minimum`/`Maximum` read their
   `min`/`max` attributes, `Increment` reads `step`, and `Value` reads the live `value`
   property. Missing or empty attributes use the WinForms defaults (0, 100, 0, 1).
   `Decimal.Parse` is the exact invariant parser; `Decimal.ToString()` is the exact
   invariant writer, including scale (`1.10D` stays `1.10`).
3. **NumericUpDown's programmatic `Value` change is change-only.** The setter compares the
   old Decimal value with the new one and raises `ValueChanged` once only when they differ.
   The DOM listener is `input`, so a browser keystroke raises once per keystroke — the
   recorded divergence from WinForms' commit-time event.
4. **DateTimePicker maps `Value` to `valueAsDate`.** The getter returns the property
   directly; an empty date input returns `Nothing`, which WinForms cannot represent and is
   recorded here. The setter writes `valueAsDate` and compares the element's `value` string
   before/after, because DateTime equality is not a supported JS-backend construct.
5. **TrackBar follows the catalog's two DOM events.** `Scroll` listens to `input` and
   `ValueChanged` listens to `change`. A programmatic `Value` set raises `ValueChanged`
   only when the integer changes; it does not raise `Scroll`.
6. **ProgressBar has no web `Minimum` member.** The WinForms-only row stays in the catalog
   and is refused by `WebUnavailableMember`; shared web code that uses it does not compile.
   `Maximum` and `Value` use the `max` attribute and the live `value` property. ProgressBar
   has no `ValueChanged` event because WinForms does not declare one.
7. **The catalog gate skips ProgressBar's web `Style` assertion.** `Style` is already a
   WinForms-only row on ProgressBar, so the existing `WithWebExtras` rule deliberately does
   not add the raw `Style` row. `CssClass` is still web-only and is swept. The WinForms-only
   `Style` row also collides with the inherited web `Style As String`, so the refusal gate
   accepts the type-mismatch diagnostic for that one row instead of `WebUnavailableMember`;
   the program is still refused.

## Test decisions

| Tier | New/updated guard |
|---|---|
| Node per kind | `PortableValueKindsNodeTests`: common mappings; NumericUpDown defaults, exact Decimal scale, change-only event, per-keystroke `input`, `ReadOnly`/`TextAlign`; DateTimePicker empty value and `valueAsDate`; TrackBar programmatic vs DOM events; ProgressBar `Maximum`/`Value` |
| Node stub | `PortableControlNodeTests.StubDom`: `valueAsDate` getter/setter backed by the date input's `value` |
| Portable markup | `PortablePageRenderingTests`: number/date/range/progress tags and their catalog attributes; NumericUpDown `readonly` and text-align |
| Catalog gate | Add the four kinds to `AllKinds`; seed no unknown `Text`; order TrackBar's `Maximum` before `Minimum`; special-case the four Edge actions and WinForms drivers; skip ProgressBar's `Style` assertion |
| Edge | Real `input`/`change` events for NumericUpDown, DateTimePicker and TrackBar; a real click for ProgressBar |
| WinForms | Programmatic `Value` changes for NumericUpDown and TrackBar (with a widened bound first), a date change for DateTimePicker, a reflected `OnScroll` for TrackBar, and a click for ProgressBar |
| IDE drop | `IdeDropLibraryMatchesSourceTests` after refreshing every changed file under `IDE/lib/js/forms/` and `IDE/lib/js/dom-core.bli` |

## Implementation order

1. `dom-core.bli`: add `valueAsDate As DateTime`.
2. `NumericUpDown.bas`, `DateTimePicker.bas`, `TrackBar.bas`, `ProgressBar.bas`.
3. Node stub and `PortableValueKindsNodeTests`.
4. Catalog coverage gate and runtime harness actions.
5. Portable page-rendering assertions.
6. Targeted runs, then the fast subset; sync the IDE drop and run the drop gate.
7. Compare fast-subset failure names with the Task 36 baseline. Do not commit or push.

## Execution record

Implemented on `feat/portable-controls` from `238857c1`; all Task 38 edits remain uncommitted and
unpushed. The Task 37 edits are also still uncommitted on the same tree.

- **DOM declaration.** `valueAsDate As DateTime` was added to both
  `BasicLang/lib/js/dom-core.bli` and `IDE/lib/js/dom-core.bli`; the two copies remain identical.
- **NumericUpDown.** `Minimum`, `Maximum`, `Value` and `Increment` are `Decimal` at run time and
  use the exact invariant parser/writer. Missing attributes use the WinForms defaults (0, 100, 0,
  1). `Value` reads the live `value` property; a programmatic set raises `ValueChanged` only when
  the Decimal changes. `ReadOnly` maps to the input's `readOnly` and `TextAlign` to `text-align`.
  The `input` listener raises per keystroke, the recorded divergence from WinForms' commit-time
  event.
- **DateTimePicker.** `Value` reads and writes `valueAsDate`; an empty date input can return
  `Nothing`, which WinForms cannot represent. The setter compares the input's `value` strings
  before/after and raises `ValueChanged` only on a change; the DOM `change` listener does the
  same for user input.
- **TrackBar.** `Minimum`, `Maximum` and `Value` are Int and map to `min`, `max` and `value`.
  `Scroll` listens to `input`; `ValueChanged` listens to `change`. A programmatic `Value` set
  raises `ValueChanged` only when the integer changes and does not raise `Scroll`.
- **ProgressBar.** `Maximum` and `Value` are Int and map to `max` and `value`. There is no
  portable `Minimum` member and no `ValueChanged` event; the WinForms-only `Minimum` row remains
  refused by the web compiler.
- **Catalog gate.** `AllKinds` now covers the seven first kinds, the four Task 37 kinds and the
  four Task 38 kinds. The gate seeds no unknown `Text` on the new kinds, sets TrackBar's
  `Maximum` before `Minimum` (the WinForms default Maximum is 10), skips ProgressBar's web
  `Style` assertion, and special-cases the WinForms-only `ProgressBar.Style` row: the inherited
  web `Style As String` makes the refusal a type mismatch rather than `WebUnavailableMember`,
  but the program is still refused.
- **Runtime harness.** `PortableWebAction` gained an optional `Event` field, and the WinForms
  driver gained `RaiseScroll` and `RaiseClick`. In this non-interactive desktop, direct
  `WM_LBUTTON*` messages did not raise `Click` for ButtonBase or the simple non-button controls,
  so `Click` uses `BM_CLICK` for ButtonBase and `RaiseClick` (reflected `Control.OnClick`) for
  Label, Panel, PictureBox and ProgressBar; GroupBox combines `RaiseClick` with its reflected
  `OnEnter`. LinkLabel still uses the real `ClickAt` path. Edge remains the authority.
- **IDE drop.** The four new `.bas` files were copied into `IDE/lib/js/forms/` and
  `IdeDropLibraryMatchesSourceTests` passed.

| Run | Result |
|---|---|
| `PortableValueKindsNodeTests` | 9 passed |
| `PortablePageRenderingTests` | 18 passed |
| `PortableCatalogCoverageTests` | 47 passed |
| `IdeDropLibraryMatchesSourceTests` | 1 passed |
| `BasicLang` Release build | 0 warnings, 0 errors |
| Test project Release build | succeeded with analyzer warnings only |
| Fast subset (`TestCategory!=Integration`) | 10,767 passed, 6 failed, 19 skipped |
| Integration trio (`PortableFormEndToEndTests`, `BehaviourTwinTests`, `PortableMethodCoverageTests`) | 25 passed, 1 failed (`BehaviourTwinTests`) |

The fast subset has the same six failure names as the Task 36 baseline and the Task 37 record:
`EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
`Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
`Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
`Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`,
`SearchSnippets_EmptyQuery_ReturnsAll`, and `SearchSnippets_WhitespaceQuery_ReturnsAll`.
The pass count is one higher than Task 37's 10,766 because the new page-rendering test is outside
the Integration category; there is no new failure.

The integration trio was also run because the click harness changed. `PortableFormEndToEndTests`
and `PortableMethodCoverageTests` pass. `BehaviourTwinTests` fails only at `after.selection`:
Edge reports `1/0`, WinForms `0/5`, after the final button click has moved focus away from
`txtTwin`. This is a focus/selection divergence in the existing twin scenario, not one of the
four Task 38 kinds, and is reported rather than papered over. With the old direct
`WM_LBUTTON*` click the scenario cannot reach that point in this non-interactive desktop because
`btnFinish` never raises `Click`; `BM_CLICK` is what lets the run complete.

Responsive layout was not implemented; it remains deferred. No full Task 36 manual click-through
was executed or claimed. No commit or push was made.
