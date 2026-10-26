# Task 37 preflight: ComboBox, ListBox, PictureBox, LinkLabel and ObjectCollection

Plan: `2026-09-29-portable-control-library.md`, Task 37. Spec §5.4–§5.6.
Base: `feat/portable-controls` @ `238857c1`, working tree clean. Every anchor below was re-read on
this tree; the plan's line numbers have drifted.

## What already exists

| Fact | Where |
|---|---|
| The four kind files are one-line shells | `BasicLang/lib/js/forms/{ComboBox,ListBox,PictureBox,LinkLabel}.bas:1-6` |
| `ObjectCollection` is an empty class in the shared shell file | `BasicLang/lib/js/forms/Core.bas:4-5` |
| `ObjectCollection` is already a recognised portable library type | `BasicLang/Forms/PortableLibraryTypes.cs:13-17` |
| The portable event surface is declared on each kind and raised through `OnX` | `Control.bas:339-383`; `TextBox.bas:157-170` |
| Extra DOM listeners have an attach/detach seam with retained delegate fields | `Control.bas:385-437`, `896-912` |
| `Element` has no `selectedIndex` or `multiple` declaration | `BasicLang/lib/js/dom-core.bli:35-72` |
| Catalog rows exist for all four kinds, including `Items`, `SelectedIndex`, `MultiSelect`, `SizeMode`, `LinkClicked` | `FormControlCatalog.cs:1826-1858`, `1911-1937`, `1944-1956` |
| `SizeMode` has `DomMember = "style.objectFit"` but no `CssProperty`, so the page emits nothing | `FormControlCatalog.cs:1919-1923` |
| The portable ComboBox emits `Text` as `title=` and `SelectedIndex` as `selected` | `FormAssetEmitter.cs:569-583`, especially `:572` |
| The CSS converter table has no PictureBox mode arm | `FormCss.cs:26-53`; `FormCssTests.cs:182-210` |
| The method manifest is 14 rows and the method gate is pinned to 14 | `PortableMembers.cs:30-67`; `PortableMethodCoverageTests.cs:30-49`, `:156-263` |
| The catalog gate is driven by the seven first kinds | `PortableCatalogCoverageTests.cs:26-48`, `:95-135`, `:147-328` |
| The recording DOM has no `<select>`/`<option>` selection state | `PortableControlNodeTests.cs:46-263` |
| The IDE drop already contains all four shells and `dom-core.bli` | `IDE/lib/js/forms/`, `IDE/lib/js/dom-core.bli` |
| The fast-subset baseline is 10,762 passed, 6 known failures, 19 skipped | Task 36 pass |

## Decisions

1. **`Text` is the selected option.** `ComboBox.Text` reads the selected option's `textContent`
   and its setter selects the first option with that exact text, or `-1` when none matches. It is
   no longer literal select text. The portable emitter marks that option instead of writing
   `title=`; an explicit non-negative `SelectedIndex` wins, otherwise `Text` picks the option. The
   DOM-style page path is untouched, so the existing `title=` assertions stay green.
2. **`SelectedIndex` is change-only.** A programmatic set raises once only when the raw
   `selectedIndex` changes. A DOM `change` event uses a `data-vgs-selected` marker so a synthetic
   `change` at the same index raises nothing. The same marker is used after structural edits.
3. **`ObjectCollection` is owner-bound and DOM-backed.** Options are created with
   `document.createElement("option")` + `textContent`; no `innerHTML` and no parallel item list.
   `Count`/`Item`/`IndexOf`/`Contains` read the live `<option>` children.
4. **Structural edits preserve the selected option when it survives.** Inserting before the
   selected option shifts the raw index; removing an unselected option before it shifts it back.
   Neither raises. Removing the selected option clears the selection to `-1` (the native `<select>`
   and WinForms state) and raises `SelectedIndexChanged` once, per the plan's explicit rule;
   `Clear` does the same when anything was selected.
5. **PictureBox `SizeMode` is catalog-driven CSS.** Add a `PictureBoxSizeMode` converter to
   `FormCss` (one arm, several declarations where needed), mark the row `PortableCssOnly` so
   DOM-style byte-identity is preserved, and keep the library setter/getter exact through a
   `data-vgs-sizemode` attribute. Mapping:

   | Mode | `object-fit` | `object-position` |
   |---|---|---|
   | Normal | `none` | `left top` |
   | StretchImage | `fill` | `center` |
   | AutoSize | `none` | `left top` |
   | CenterImage | `none` | `center` |
   | Zoom | `contain` | `center` |

6. **LinkLabel always prevents navigation.** Its click override calls `preventDefault()` first,
   suppresses both `Click` and `LinkClicked` while disabled, and otherwise raises the base `Click`
   and then `LinkClicked` with `LinkLabelLinkClickedEventArgs`.
7. **ListBox `MultiSelect` is the HTML `multiple` property.** Markup already emits the attribute
   from the row; the library property reads/writes the same boolean state.
8. **PictureBox `Image` is not swept by the WinForms half of the catalog gate.** The document row
   is a String (`src`), while desktop `PictureBox.Image` is a `System.Drawing.Image`; the generated
   init uses `Image.FromFile`. Coverage asserts the web mapping and emits the desktop literal
   without assigning a string to `Image`.

## Measured divergences recorded here

- **Removing the selected item.** A Windows Forms 8.0 probe (`ComboBox` and `ListBox`,
  `Items.RemoveAt(0)` after selecting 0) sets `SelectedIndex = -1` and raises
  `SelectedIndexChanged` **zero** times; `Items.Clear()` likewise. The plan requires the portable
  collection to raise once. The library follows the plan; the method twin measures the resulting
  item count, not the removal event, and no Edge/WinForms coverage action removes a selected item.
- **Editable ComboBox `Text`.** WinForms `DropDown` accepts arbitrary text without changing the
  selection. A `<select>` cannot; `Text = "missing"` selects none and reads back `""`. The
  portable emitter/library mapping is the spec's selected-option mapping, not literal text.
- **Initial selection.** A WinForms ComboBox starts at `-1` after `Items.Add`; a single HTML
  `<select>` with no `selected` option exposes `0`. The portable emitter writes `selected` for a
  non-negative `SelectedIndex`, and the tests always initialize selection explicitly where the
  result matters.

## Test decisions

| Tier | New/updated guard |
|---|---|
| Node per kind | `PortableRemainingKindsNodeTests`: common styled properties for all four; ComboBox/ListBox `Items` and selection; every `ObjectCollection` member; PictureBox image/mode; LinkLabel prevention/disabled/event args |
| Node stub | `PortableControlNodeTests.StubDom`: real single-select `options`, `selectedIndex`, `multiple`, `dispatchEvent`; no stub-only library branch |
| Portable markup | `PortablePageRenderingTests`: `Text` selects an option and writes no `title`; `SelectedIndex` still wins; PictureBox `src`/`alt` and `SizeMode` CSS |
| Catalog gate | Extend the existing one to a `FirstKinds + RemainingKinds` union; special-case `Items` (get-only), ComboBox `Text`, ComboBox/ListBox `SelectedIndex`, PictureBox `Image`, PictureBox `SizeMode`, ListBox `MultiSelect`, and the two selection events |
| Method gate | 23 manifest rows; one generated program with a ComboBox and a ListBox, each pre-populated; one case per `ObjectCollection` member |
| Edge | The catalog and method gates run the four kinds in real Edge; selection is exercised with a real DOM `change` event |
| WinForms | Both gates run the four kinds in a real window; LinkLabel is clicked inside its link area, selection is changed through `SelectedIndex` |
| IDE drop | `IdeDropLibraryMatchesSourceTests` after refreshing every changed file under `IDE/lib/js/forms/` and `IDE/lib/js/dom-core.bli` |

## Implementation order

1. `dom-core.bli`: add `selectedIndex As Integer` and `multiple As Boolean` to `Element`.
2. `Core.bas`: implement `ObjectCollection`; `Control.bas`: add the selection notification seam.
3. `ComboBox.bas`, `ListBox.bas`, `PictureBox.bas`, `LinkLabel.bas`.
4. `FormControlCatalog.cs` + `FormCss.cs` + `PortableMembers.cs`.
5. `FormAssetEmitter.cs` portable select branch.
6. Tests and harness/stub, then targeted runs, then the fast subset.
7. Sync the IDE drop, run the drop gate, compare the fast-subset failure names with the Task 36
   baseline. Do not push.

## Execution record

Implemented on `feat/portable-controls` from `238857c1`; all Task 37 edits remain uncommitted and
unpushed.

- **Manifest count.** `PortableMembers` now has **23** rows: 14 existing rows plus 9
  `ObjectCollection` rows. The method gate carries the same 23 rows; the 24th test is the
  MessageBox divergence check, not a manifest row.
- **DOM declarations.** `dom-core.bli` gained `selectedIndex`, `multiple` and
  `childElementCount` (the preflight's “what already exists” row describes the base tree).
  `Core.bas` reads the live `<option>` children, so markup, `Items.Add` and a user's DOM edit
  agree.
- **ObjectCollection helpers.** The helper functions are `Public` because BasicLang did not
  resolve `Private Function` return types. They are `Vgs`-prefixed and excluded from the
  manifest. The JS
  backend cannot lower the original `While child IsNot Nothing` loops, so the implementation uses
  bounded `For` loops plus `VgsElementAt`; `Clear` repeatedly removes the first live option.
- **Portable initializers.** Portable form initializers deliberately omit control properties.
  The catalog and method gates therefore seed ComboBox/ListBox items in `Cover` under
  `#If WEB Then ... #Else`: Edge gets them from the page markup, the desktop gets them from the
  body. This is why those gates do not depend on the document initializer for `Items`.
- **Catalog node tier.** The node tier uses a plain `NodeCover*` class carrying the `Cover` body,
  not the generated form source. Nesting the generated designer regions inside
  `PortableControlNodeTests.Run`'s init region hid the inner init region from the runtime-creation
  check, so `New Button()` looked like an unsupported runtime creation.
- **LinkLabel input.** `PortableRuntimeHarnesses.ClickAt` sends `WM_MOUSEMOVE` before the button
  pair, and LinkLabel is clicked at `(10, 10)` inside the padded link area. A click at the
  top-left corner missed the link text and raised no `LinkClicked` in WinForms.
- **PictureBox design-time mode.** The portable emitter writes `data-vgs-sizemode` from the
  document's `SizeMode` (the catalog's allowed-value order is the WinForms enum order), so the
  library getter reads the designed mode, not only the CSS. The page test and the node fixture
  cover that seam.

| Run | Result |
|---|---|
| `PortableRemainingKindsNodeTests` + `PortablePageRenderingTests` + `FormCssTests` | 110 passed |
| `PortableFirstKindsNodeTests` + `PortableControlNodeTests` | 58 passed |
| `PortableCatalogCoverageTests` | 35 passed |
| `PortableMethodCoverageTests` | 24 passed (23 manifest rows + the MessageBox divergence check) |
| `FormDockScriptTests` + `FormPortableCodegenTests` + `FormAssetEmitterTests` | 146 passed |
| `IdeDropLibraryMatchesSourceTests` | 1 passed |
| Fast subset (`TestCategory!=Integration`) | 10,766 passed, 6 failed, 19 skipped |

The fast subset has the same six failure names as the Task 36 baseline: `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`, `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`, `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, `SearchSnippets_EmptyQuery_ReturnsAll`, and `SearchSnippets_WhitespaceQuery_ReturnsAll`. The Task 36 baseline was 10,762 passed, 6 failed, 19 skipped; Task 37 adds four passing tests and no new failure.

Responsive layout was not implemented; it remains deferred. No commit or push was made.
