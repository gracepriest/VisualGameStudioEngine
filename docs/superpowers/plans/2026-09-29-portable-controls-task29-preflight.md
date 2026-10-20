# Task 29 pre-flight: `Control`, `Form`, attaching, data flow, methods, events core

Plan: `docs/superpowers/plans/2026-09-29-portable-control-library.md`, "### Task 29".
Spec: `docs/superpowers/specs/2026-09-29-portable-control-library-design.md` §5.3–§5.6, O10–O12.
Checked against `feat/portable-controls` @ **`4f98c7a6`** (Task 28 implementation landed; clean tree).
Read-only pre-flight; the only file created by this step is this document.

Follow this document **instead of** the plan's Task 29 section where the two disagree.
**OWNER decisions needed: none.**

---

## A. Anchors (claim -> today's file:line at 4f98c7a6 -> verdict)

| # | Claim | Today | Verdict |
|---|---|---|---|
| 1 | Task 29 creates `Control.bas`/`Form.bas`/`MessageBox.bas`/`EventArgs.bas` | `Control`, `Form`, `ControlCollection`, `MessageBox`, and all three event-argument shells are together in `BasicLang/lib/js/forms/Core.bas:7-38`; `System.EventArgs` is in `System.bas:3-4` | **split the shells first**; no new duplicate classes |
| 2 | `Control` can be attached and can hold a `Controls` collection | `Control` and `ControlCollection` are empty shells (`Core.bas:7-15`) | implement; `ControlCollection` needs a public `Vgs`-prefixed seam because the classes are separate |
| 3 | `Form` attaches to the page | `Form` is an empty `Control` subclass (`Core.bas:10-12`); the page emits `<div class="vgs-form">` (`FormAssetEmitter.cs:200`, `:231`) and keys the page with `<body data-form="...">` (`:147`) | use `document.querySelector(".vgs-form")`, not a `data-form` element lookup |
| 4 | `EventArgs.Empty` is usable | `Empty` is an uninitialised shared auto-property (`System.bas:4`) | initialise the singleton in `EventArgs.bas` |
| 5 | Event arguments carry the fields the library raises | `MouseEventArgs`, `KeyEventArgs`, and `KeyPressEventArgs` are empty shells (`Core.bas:32-38`); `Point` is empty (`Drawing.bas:6-7`) | add the Task 29 fields, constructors, and `Point.X`/`Y` |
| 6 | The DOM has what common members need | `Document` has `getElementById`/`querySelector` and `Element` has `textContent`, `hidden`, `style`, attributes, `contains`, `remove`, and listeners (`dom-core.bli:20-67`); `CSSStyleDeclaration` has `color`, `backgroundColor`, `fontSize`, `fontWeight`, and `padding` (`:69-87`) | enough for Text/Enabled/Visible; add `fontFamily`, `fontStyle`, and `cursor` for Font/Cursor |
| 7 | The library will use a shared style-rule writer | No writer exists in the library. Task 33 owns "the library's style-rule writer (one `<style>`, `#id` rules inside … `@media`)" (`plan:3448`); Task 40 owns slice 3's exhaustive Font/Cursor/Padding rows (`plan:3491`) | **do not create a second rule writer**; Task 29 defines the public style-backed members over one private write seam, and Task 33 replaces that seam's body |
| 8 | `Enabled` is driven by "the row's enable target" | The catalog has properties/events but no `EnableTarget`, `DomMember`, or caption fields; those land in Task 32 (`plan:3441`) | derive the target from the element's tag for now; Task 32 replaces the derivation with the catalog row |
| 9 | A method manifest exists | `BasicLang/Forms/PortableMembers.cs` does not exist (`BasicLang/Forms/`) | create it; it is the only method list |
| 10 | The catalog can drive the method manifest | The catalog is a property/event table (`FormControlCatalog.cs:202`, `:1362`); the plan's own Task 29 says "the catalog lists no methods" (`plan:3412`) | keep methods in the manifest; do not invent catalog rows for them |
| 11 | A node fixture verifies the library | No `PortableControlNodeTests`; the roster is pinned at `108` (`JsExecutionTierRosterTests.cs:529`) | add one fixture and pin `109` |
| 12 | The node tier has a shared runner | `JavaScriptExecutionTests.RunNodeScript`/`RunNodeScriptForOutcome` are `internal` (`:41-53`), and the idiom fixture compiles through the project route and prepends a stub DOM (`JavaScriptLibraryIdiomTests.cs:19-36`) | reuse the runner and the project route; do not bypass the inclusion hook |
| 13 | The project route auto-includes the library | `WithWebFormsLibrary` runs after phase-1 parsing (`Compiler.cs:623-646`); the csproj copies every forms file (`BasicLang.csproj:47`) | compile through `BasicCompiler.CompileProjectFiles` in the node fixture |
| 14 | Listener ordering is load-bearing | `M25` proves a listener declared below its attaching method is refused as `Action<DomEvent>` (`JavaScriptLibraryIdiomTests.cs:85-97`); `M24` proves the above-declaration idiom (`:40-47`) | every `VgsOnDom*` listener is declared above `VgsAttach`/`VgsAttachListeners` |
| 15 | Optional parameters and `MyBase` property overrides work | `M21` (`:57-61`) and `M4` (`:75-83`) | use `Optional` for `MessageBox.Show`; override `Form.Text` through `MyBase` |
| 16 | `Char`, enum typing, and delegate identity are available | Tasks 14/9/12 landed; `KeyPressEventArgs.KeyChar` can be a real `Char`, and `Keys` can be typed | use `ChrW`/`AscW` for the printable-key path; do not lower a second keystroke path |
| 17 | The caption double-click de-duplication belongs to this task | There is no caption wrapper or synthetic click path; Task 31 creates CheckBox/RadioButton caption spans (`plan:3434`) and Task 32 emits the caption wrapper (`plan:3441`) | **move that one test row to Task 31/32**; Task 29 tests the core `Click` path only |
| 18 | `MessageBox.Show` can carry the caption | `Window` has `alert`/`confirm` and no dialog-title argument (`dom-core.bli:118-125`) | prefix the caption to the message; record the browser-dialog title divergence |
| 19 | `Controls.Remove` can remove DOM state | `Element.remove`, `contains`, `parentElement`, and `appendChild` exist (`dom-core.bli:44-62`) | implement `Add`/`Remove`/`Count`/`Item`; `Remove` detaches listeners and removes the element |
| 20 | `Form.Close` hides rather than destroys the form area | `hidden` exists (`dom-core.bli:43`); the page CSS respects it (`FormAssetEmitter.cs:746`) | set `hidden = True` on the form area |

---

## B. Decisions taken (plan corrections)

1. **File split first.** Move the shells out of `Core.bas` and `System.bas` into the four Task 29 files. `Core.bas` keeps the non-Control shared shells (`ObjectCollection`, `Padding`, `Cursor`, `Cursors`); `Drawing.bas` keeps the drawing shells. `System.bas` is removed rather than left as a second `EventArgs` declaration.
2. **`EventArgs.Empty` is a real singleton.** A private shared field plus a lazy `Empty` getter; no program depends on a new object per read.
3. **Unattached access is checked at the one DOM seam.** `VgsElement()` throws `InvalidOperationException` naming the control and the member that needed the element. `Name`, `Parent`, and `Controls` remain readable before attach because attach needs them.
4. **`Enabled` uses a derived target until Task 32.** Form elements (`BUTTON`, `INPUT`, `SELECT`, `TEXTAREA`, `FIELDSET`) use the `disabled` property; other elements use `aria-disabled` plus `vgs-disabled`. The control's own value lives in `data-vgs-enabled`; the getter also walks ancestors, and the setter walks descendants so propagation is visible immediately. Task 32's catalog `EnableTarget` replaces only the tag test.
5. **`Visible` is `hidden` plus ancestors.** The getter walks `parentElement`; the setter writes only the control's own `hidden`.
6. **Change-only events.** `Text` and `Enabled`/`Visible` setters compare the live value before writing and raise only on change. The base `Text` writes `textContent`, never `innerHTML`; the `<b>x</b>` test therefore pins literal text.
7. **Style-backed members use one seam, not a second writer.** Task 29 declares `ForeColor`, `BackColor`, `Font`, `Cursor`, and `Padding` and routes each through `VgsSetStyle`. The Task 29 body writes the element's inline style; Task 33 replaces that body with the one `<style>`/media-rule writer without changing the public members or the node assertions (which read `getComputedStyle`). Only the value surface Task 29 needs is added here: named `Color`s used by defaults, `Font` family/size/style, `Cursor` CSS keywords, and `Padding` sides; Task 40 owns slice 3's exhaustive rows and constructors.
8. **`Form` attaches on construction.** `Sub New()` calls `MyBase.New()` and attaches the result of `document.querySelector(".vgs-form")`; a missing form area is an `InvalidOperationException` naming the selector. `Form.Text` reads/writes `document.title`; `Form.Close` hides the form area. `data-form` stays the page dispatch key, not the form-area locator.
9. **`MessageBox.Show` has one optional-parameter signature.** `Show(text, Optional caption = "", Optional buttons = MessageBoxButtons.OK)`. `OK` uses `alert` and returns `DialogResult.OK`; `OKCancel`/`YesNo` use `confirm` and map `True`/`False` to `OK`/`Cancel` or `Yes`/`No`. A non-empty caption prefixes the message with `vbCrLf` because the browser API has no title argument.
10. **Manifest shape.** `PortableMembers` is a static list of rows: class name, member name, kind (`Method`/`Property`), WinForms signature text, and one sample argument per parameter (empty for properties). Task 29 rows are `Control.Show`/`Hide`/`Focus`/`BringToFront`, `ControlCollection.Add`/`Remove`/`Count`/`Item`, `MessageBox.Show`, and `Form.Close`. The gate parses the library with `Parser` and compares every public METHOD declaration (excluding constructors, property/event accessors, and every `Vgs`-prefixed internal) against the manifest's method rows; it resolves every manifest property row (`ControlCollection.Count`/`Item`) but does not demand manifest rows for catalog-backed properties/events (`Text`, `Enabled`, `Visible`, …). A manifest row without a declaration and a public method without a row both fail. Task 35's method gate and Task 34's `WebUnavailableMember` consume the same list.
11. **The caption de-duplication test moves to Task 31/32** (anchor 17). Task 29's `Click` test pins one DOM click → one event, sender = the control.
12. **DOM lock-step grows by three names.** `fontFamily`, `fontStyle`, and `cursor` on `CSSStyleDeclaration` in both `dom-core.bli` copies. No `Document.head`/`Element.sheet` yet: Task 33 adds them with the rule writer, so Task 29 does not ship unused DOM surface.

---

## C. Implementation steps

1. **Split the library shells.**
   - `Control.bas`: `Control`, `ControlCollection`.
   - `Form.bas`: `Form`.
   - `MessageBox.bas`: `MessageBox`.
   - `EventArgs.bas`: `System.EventArgs` plus `MouseEventArgs`, `KeyEventArgs`, `KeyPressEventArgs`, and the existing link/item argument shells.
   - Remove the moved classes from `Core.bas` and delete the now-empty `System.bas`.
2. **Complete the event/value shells Task 29 raises.**
   - `EventArgs.Empty` singleton.
   - `MouseEventArgs(button, clicks, x, y, delta)` with `Button`, `Clicks`, `X`, `Y`, `Delta`, `Location`.
   - `KeyEventArgs(keyCode, Optional modifiers)` with `KeyCode`, `Modifiers`, `KeyData`, `Shift`, `Control`, `Alt`, `Handled`, `SuppressKeyPress`.
   - `KeyPressEventArgs(keyChar)` with `KeyChar As Char`, `Handled`.
   - `Point.X`/`Y` and a constructor.
   - Minimal `Color`, `Font`, `Padding`, `Cursor`/`Cursors` members used by the common properties.
   - Add `None`, `ShiftKey`, `ControlKey`, and keypad names to `Keys` if the mapping needs them.
3. **Implement `ControlCollection`.**
   - Owner field + `List(Of Control)`.
   - `Add(child)`: `getElementById(child.Name)`, containment check when the owner is attached, `child.VgsAttach(el, owner)`, then add to the list.
   - `Remove(child)`: remove from the list, detach listeners, remove the element.
   - `Count` and `Item(index)`.
4. **Implement `Control`.**
   - `Name` field/property; `_element`/`_parent`; `Controls` created in `Sub New`.
   - `Element`, `Parent`, `Controls` properties; `VgsElement`, `VgsIsAttached`, `VgsAttach`, `VgsAttachRoot`, `VgsDetach` public internals.
   - `Text`, `Enabled`, `Visible`, `ForeColor`, `BackColor`, `Font`, `Cursor`, `Padding`.
   - `Click`, `TextChanged`, `MouseDown`/`Up`/`Move`, `KeyDown`/`Up`, `KeyPress`, `Enter`; all `Protected Overridable OnX(e)` raisers.
   - `Show`, `Hide`, `Focus`, `BringToFront`.
   - DOM listeners above `VgsAttachListeners`; map `code`/`key` to the fixed `Keys` table, mouse button numbers to `MouseButtons`, and `focusin`/`relatedTarget` to `Enter`.
5. **Implement `Form`.**
   - Constructor attaches `.vgs-form`; `Text` override writes `document.title`; `Close` hides the form area.
6. **Implement `MessageBox`.**
   - `Shared Show` per decision 9.
7. **Add the manifest and gate.**
   - `BasicLang/Forms/PortableMembers.cs`.
   - `VisualGameStudio.Tests/Compiler/PortableMembersManifestTests.cs`: parse `lib/js/forms/*.bas`; compare public members; check unique rows, class existence, and sample-argument counts.
8. **Add the node fixture.**
   - `VisualGameStudio.Tests/Compiler/PortableControlNodeTests.cs`: project-route compile, stub DOM, `RunNodeScript`; cover attach/unattached, hidden/ancestor, enable propagation, change-only Text, one-click sender, mouse/key mapping, `relatedTarget` Enter, MessageBox mappings, BringToFront order, Form.Close/Text, Controls.Remove/Count/Item, literal markup, and the style seam.
   - Add `typeof(PortableControlNodeTests)` to `ExecutionTier` by hand and change the pin to `109`.
9. **Lock-step `dom-core.bli`:** add the three CSS members to `BasicLang/lib/js/dom-core.bli` and copy the same bytes to `IDE/lib/js/dom-core.bli`.

---

## D. Test rows

### `PortableControlNodeTests` (Integration; node)

| Row | Assertion |
|---|---|
| Attach | `Controls.Add` finds the id, sets `Element`, `Parent`, `Count=1`, `Item(0)`; a missing id/outside-parent id throws |
| Unattached | `Text`/`Visible`/`Enabled` access before attach throws `InvalidOperationException` naming the member |
| Text | writes `textContent`; `"<b>x</b>"` stays literal; same value raises no second `TextChanged`, changed value raises one |
| Visible | `False` writes `hidden`; a child reads `False` while an ancestor is hidden and `True` when it is shown |
| Enabled | a form element gets `disabled`; a panel gets `aria-disabled`/`vgs-disabled`; a child follows its ancestor; the child's own `data-vgs-enabled="true"` does not override a disabled ancestor |
| Click | one dispatched click raises `Click` once with sender = the control |
| Mouse | down/up/move map `button`, `clientX/Y`, and `Location` |
| Keys | `KeyDown` maps `code` (`Enter`, `KeyA`, arrows, function/digit keys) and modifiers; `KeyPress` carries the printable `Char`; `Handled` prevents the default |
| Enter | `focusin` with `relatedTarget` outside raises `Enter`; a target inside does not |
| MessageBox | OK → one `alert`, returns `OK`; OKCancel/YesNo → `confirm` true/false maps as specified; caption prefixes the text |
| BringToFront | element becomes the parent's last child |
| Form | constructor attaches the form area, `Text` writes `document.title`, `Close` sets `hidden` on the form area |
| Controls.Remove | removes from the list, detaches listeners, removes the element, decrements `Count` |
| Style seam | setting the common style-backed properties is visible through `getComputedStyle`; no `innerHTML` write |

Caption double-click de-duplication: **moved to Task 31/32** (anchor 17).

### `PortableMembersManifestTests` (fast)

- Every manifest row has a matching public declaration in the named library class (method rows and `ControlCollection.Count`/`Item` property rows).
- Every public library method (excluding `Vgs*`, constructors, accessors) has a row. Catalog-backed properties/events are not manifest rows.
- No duplicate rows; every row's sample count equals its parsed parameter count; every named class exists.

---

## E. Gate

1. `dotnet build VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release`
2. `dotnet test ... --filter "FullyQualifiedName~PortableControlNodeTests|FullyQualifiedName~PortableMembersManifestTests|FullyQualifiedName~JavaScriptLibraryIdiomTests"`
3. `dotnet test ... --filter "FullyQualifiedName~JsExecutionTierRosterTests"`
4. Fast subset by name if the task's named gate is green; record the unchanged six baseline failures.
5. No push. No IDE forms drop (`IDE/lib/js/dom-core.bli` remains lock-step with source; Task 36 owns the drop).

## F. Risks / traps

- `Form.New`/`MyBase.New()` ordering and cross-file inheritance: compile a tiny Form fixture first; the generated entry point must see classes before `Main`.
- `List(Of Control)`/`Item(i)` lowering and `Count` property dispatch on JavaScript.
- `ChrW`/`AscW` with `e.key`; fall back to an explicit `KeyA`–`KeyZ` map if string→Char conversion is refused.
- `Keys` bitwise tests (`Modifiers And Keys.Shift`) and enum casts for dynamic letter/digit codes; avoid a second enum table.
- Listener declaration order (M25); all DOM listeners above the attach helper.
- Property overrides (`Form.Text`) and `EventArgs.Empty` shared initialization.
- The style seam is transitional: Task 33 must replace only `VgsSetStyle`, not the public properties or tests.
- The manifest gate must not count auto-property/event accessors or `Vgs*` internals, or every library member will look missing.
- The node stub must provide `EventTarget`-style listeners, `getElementById`/`querySelector(".vgs-form")`, attributes/classList/style/hidden/disabled, parent/child links, `contains`, `getComputedStyle`, `alert`/`confirm`, and `document.title`; no layout is needed.
- The caption de-duplication remains an explicit Task 31/32 row, not a silently dropped plan requirement.
