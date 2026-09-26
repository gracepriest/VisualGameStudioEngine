# Slice 2 pre-flight: corrections to the plan against the current code

Plan: `docs/superpowers/plans/2026-09-25-property-grid-vs-parity.md`, section "## Slice 2 — The grid" (plan lines 3630–5974, Tasks 11–16).
The plan's anchors were taken at f2b72dbb. This check was made against `feat/property-grid` @ **66b44484**. It was read-only; nothing in the repo was changed.

Follow this document **alongside** the plan. Where the two disagree, this document wins.

---

## BLOCKERS (4)

### B1: Task 11 Step 4 undoes slice 1's rule that a frozen row shows its raw text. `AFrozenRow_ShowsTheRawText_NotTheCanonicalSpelling` goes RED.
- **Evidence.** Today's `FormPropertyRow.StringValue` (`VisualGameStudio.Shell/ViewModels/Designer/FormPropertyRow.cs:192-196`) returns `RawValue` when the row `IsFrozen`. It returns the canonical form only for an editable row.
- The plan's replacement has `DisplayValue => IsPresent ? Canon(RawValue) : DefaultValue ?? ""`, and `StringValue => DisplayValue`, so a frozen row now shows the canonical form too.
- Pinned by `VisualGameStudio.Tests/Compiler/FormPropertyGridTests.cs:650-664`. That test sets web `BackColor="activecaption"` and expects the row to show `activecaption`. The plan's version returns `ActiveCaption`, because `FormPropertyDef.Canonical`'s colour arm (`BasicLang/Forms/FormControlCatalog.cs:237-241`) canonicalises system colour names.
- **Corrected instruction.** In the replacement file:
  ```csharp
  public string DisplayValue =>
      IsFrozen ? RawValue                      // ⛔ slice 1: a Degraded row shows the document's text exactly —
                                               // its reason quotes that text "preserved exactly as written".
      : IsPresent ? Canon(RawValue)
      : DefaultValue ?? "";
  ```
  Keep the slice-1 doc paragraph on `StringValue` (the "⛔ Except when FROZEN" note). `IsBold` and the no-op rule may keep reading `DisplayValue`. A frozen row's `Commit` returns before the no-op comparison, so the change is safe there.

### B2: Task 11 Step 4 undoes slice 1's culture-invariant Int parse in `IntValue`.
- **Evidence.** Today: `get => FormPropertyDef.TryParseInt(RawValue, out var parsed) ? parsed : 0;` (`FormPropertyRow.cs:212-216`, with its sv-SE/U+2212 doc comment).
- The plan: `get => int.TryParse(DisplayValue, out var parsed) ? parsed : 0;`. That uses the current culture, and it also accepts `\t`/`\r\n` around the digits, which `TryParseInt` deliberately refuses (`FormControlCatalog.cs:319-332`).
- Pinned by `FormPropertyGridTests.cs:298-323` (`ANegativeIntEdit_UnderAUnicodeMinusCulture_…`, `[SetCulture("sv-SE")]`). The row's reads and writes must agree with the catalog's tier judgement.
- **Corrected instruction.**
  ```csharp
  public int IntValue
  {
      get => FormPropertyDef.TryParseInt(DisplayValue, out var parsed) ? parsed : 0;
      set => Commit(value.ToString(CultureInfo.InvariantCulture));
  }
  ```
  Carry the slice-1 doc comment over verbatim. Parsing `DisplayValue` rather than `RawValue` is correct: the absent row must show `DefaultFor(target)`, as `OnTheWeb_TheRowShowsTheWebDefault` requires.

### B3: Task 12 Step 5 undoes slice 1's invariant intrinsic `IntRow`. Three sv-SE tests go RED.
- **Evidence.** Today's `IntRow` (`VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs:199-216`) reads with `read().ToString(CultureInfo.InvariantCulture)` and writes with `FormPropertyDef.TryParseInt(text, out var parsed)`.
- The plan's wholesale replacement (plan ~4963-4977) has `() => read().ToString()` and `int.TryParse(text, out var parsed)`. It also drops `using System.Globalization;`.
- Under sv-SE, `RawValue` becomes `"−5"` (U+2212), so `ANegativeIntrinsicEdit_UnderAUnicodeMinusCulture_ShowsAnAsciiHyphen` fails (`FormPropertyGridTests.cs:325-344`, `Assert.That(x.RawValue, Is.EqualTo("-5"))`). `…_ReachesTheDocumentWithAnAsciiHyphen` (`:351-369`) is at risk as well.
- **Corrected instruction.** In the replacement file, keep `using System.Globalization;` and write `IntRow` as:
  ```csharp
  private static FormPropertyRow IntRow(
      string name, Func<int> read, Action<int> write, Action changed, string category, string description) =>
      new(name,
          FormPropertyType.Int,
          // ⛔ Invariant, and parsed by the catalog's own reader: see FormPropertyRow.IntValue.
          () => read().ToString(CultureInfo.InvariantCulture),
          text =>
          {
              // ⚠ An unparseable value is IGNORED rather than coerced to zero (mid-edit text).
              if (FormPropertyDef.TryParseInt(text, out var parsed))
              {
                  write(parsed);
              }
          },
          changed,
          category: category,
          description: description);
  ```
  This is the same body as today's, plus the two new parameters. Apply the same rule to Task 11 Step 5, which only adds the parameters and must not touch the body.

### B4: Task 12 Step 1's edit anchors in `FormPropertyGridTests.cs` point at the WRONG tests.
- **Evidence.** The plan says "`:301-302` → … `ClientSize`" and "`:381` → …". At 66b44484, line 301-302 is inside the sv-SE `ANegativeIntEdit_…` test, and line 381 is inside the doc comment of `AnIntRow_PushedBackTheSameNumber_…`. Editing those lines as written would corrupt two slice-1 culture pins.
- **Corrected instruction.** The two assertions that expect the Form's `Width`/`Height` rows are now:
  - `:423-424`, in `WithNoControlSelected_TheGridShowsTheFormsOwnProperties` (starts at :412). Replace
    `Does.Contain("Name").And.Contains("Text").And.Contains("Width").And.Contains("Height")` with
    `Does.Contain("Name").And.Contains("Text").And.Contains("ClientSize")`.
  - `:503`, in `SelectingAControlAndThenNothing_ReturnsToTheFormsProperties` (starts at :490). Replace
    `Does.Contain("Text").And.Contains("Width")` with `Does.Contain("Text").And.Contains("ClientSize")`.

  Find each by its quoted assertion, not by line number. `AWebPagesFormRows_AreItsGridTracks_NotAClientSize` (:456) keeps passing as is: FormRoot's web rows are Text/Cols/Rows/Gap.

---

## Per-task CORRECTIONS

### Task 11: row default, bold, reset, no-op, §7 refusal
- **Anchor moves**
  - `FormRetarget.cs` `SameValue`: plan says `:446-456`; it is now **`:526-536`** ("Equality in the property's own terms: `True` and `true` are one Bool"). Callers are at `:387` and `:515`.
  - `FormPropertyGridViewModel.Rebuild` row construction: plan says `:292-296`; it is now **`:294-298`**.
  - `AddIntrinsicRows`: plan says `:125-192`; it is now **`:126-193`**.
  - `FormPropertyDef.Canonical` ends at `FormControlCatalog.cs:244`. Insert `SameValue` after it.
  - Step 6 pins: `ABoolWritesTheDocumentsOwnLowercaseSpelling` is now **`:741`** and `ANoOpWrite_RaisesNothing` is now **`:754`** (the plan says :541/:554).
- **Step 3 (`SameValue`) is still needed.** Slice 1 did NOT add it under any name. Grep finds only the private copy in `FormRetarget`.
  - ⚠ One behaviour change: the old private copy compared non-Bool values `OrdinalIgnoreCase`. The catalog version is `Ordinal` for String/Int/Size.
  - Both callers compare against `FormWebScript.Implies` values, which are Bool (`Enabled=true`), so nothing changes in practice. Say so in the commit.
- **Step 4 (replace `FormPropertyRow.cs`).** Apply B1 and B2. The rest of the plan's file compiles against the current types, checked member by member:
  - `FormPropertyDef.DefaultFor(FormTarget)` exists at `:192`.
  - `Canonical(string)` exists at `:202`, with Enum/alias, Bool, Int and colour-name arms. `Canonical("")` returns `""` for every arm, so the old `RawValue.Length > 0` guard is not needed.
  - `Accepts(string?, FormTarget)` exists at `:491`.
  - `Category` is `FormPropertyCategory?` and `Description` is `string?`.
  - `ITypedValueRow` (`VisualGameStudio.Core/Abstractions/ViewModels/ITypedValueRow.cs`) is satisfied.
  - No other code constructs `FormPropertyRow` (grep: only the grid VM), so the new `target` constructor parameter breaks no caller.
- **What the plan re-implements that slice 1 already did.** Nothing to delete; the new rules are supersets of slice 1's:
  - Commit's null/frozen drop is kept.
  - Slice 1's two-step no-op (raw ordinal-equal, then canonical-equal when present) is replaced by "`Canon(value) == DisplayValue`". That covers every slice-1 case: `007`/` 7 `/`+7` because of the Int canonical arm (`FormPropertyGridTests.cs:384-405`), and the legacy `Left` pushed back as `MiddleLeft` (`:597-625`). It also adds the absent-default case.
  - The canonical display is kept, with B1's frozen exception.
- ⚠ **Optional consistency fix.** The `SameValue` doc says "FormRetarget and the grid's bold/no-op rules both ask it", but the plan's `Commit` no-op compares with `string.Equals(..., Ordinal)`, not `SameValue`. Either make Commit's check `_definition?.SameValue(value, DisplayValue) ?? string.Equals(value, DisplayValue, StringComparison.Ordinal)`, or reword the doc comment. With the plan's code as written, a case-only hex colour edit (`#ff0000` → `#FF0000`) is a write while `IsBold` calls the two equal. That is harmless, but the doc comment would be false.
- **Red tests that are already green (guards, not proofs).** The whole file is build-red at Step 2, so the "right reason" claim holds at build level. Once it compiles:
  - `ClearingAStringValue_WritesTheEmptyString` would pass on today's `Commit` too. Only mutant M8b proves it.
  - `AnAliasValue_…`'s first assertion (`StringValue == "MiddleCenter"`) is already true today (slice 1's canonical display). Only its `IsBold` half is new.
  - The rest are genuinely red on today's behaviour: absent Enabled shows False; `"maybe"`, `"Bogus"` and web `"ActiveCaption"` are written; clearing BackColor writes `""`; pushing the displayed default writes it.
- **Fixture facts checked.**
  - Label `TextAlign` default is `TopLeft` (`FormControlCatalog.cs:1156-1157`); Button's is `MiddleCenter` (`:1159-1160`).
  - `Enabled` default is `"true"` with Description exactly "Indicates whether the control is enabled." (`:1008-1009`).
  - Label `ForeColor`/`BackColor` have no default (`:1027-1034`).
  - `"12345"` is not a colour (`IsColor`/`IsColorName`, `:617-633`).
  - `FormControl.Properties` is `OrdinalIgnoreCase` (`FormControl.cs:70`).
- **Step 6.** Add `FormPropertyGridTests` explicitly to the filter's reason list: its sv-SE rows (`:298-369`) and `AFrozenRow_…` (`:650`) are the B1/B2/B3 guards. They are already in the plan's filter, so no command change is needed.

### Task 12: grid VM, categories, sort, search, object selector, FormRoot rows
- **Anchor moves**
  - `CodeEditorDocumentViewModel` constructor: still **`:1090-1124`**; its closing brace is at `:1124`. Unchanged.
  - `SelectInDesigner` is at `:301-305` (private; the lambda in Step 6 is inside the class, which is fine).
  - `FormPropertyGridTests.cs` edits: see **B4**.
- **Step 1 fixture facts.**
  - `FormTrayItem` exposes `Id` and `IsSelected` (bound at `CodeEditorDocumentView.axaml:296` and `:304`).
  - `FormDocument.RootElementName` is `"Form"` for a .blform (`FormDocument.cs:44`), so `"F  Form"` is right.
  - `Selection.Primary`, `Tray.Items`, `SetContent` and `EnterDesignModeForFormDocument` (`:163`) exist.
- **Step 4 (`FormRootValues.CanReset`).** Not present today. Add it as written.
  - Also update `FormRootValues`' class doc (`BasicLang/Forms/FormRootValues.cs:9-16`). It currently says "the property grid arrives in slice 2". The grid now reads `Get`/`Set` too, so name it as a reader.
- **Step 5 (replace the VM).** Apply **B3**. Also:
  - `FormRootValues.Set` **returns `bool`** (`FormRootValues.cs:43`; false means "does not parse, nothing changed"). The plan's `v => FormRootValues.Set(form, definition, v)` compiles as an `Action<string>`, but it throws that result away.
    - `ClientSize = "0, 300"` passes `Accepts` (because `TryParseSize` parses it) and is then refused by `Set` (width ≤ 0). `Commit` still calls `RaiseValueChanged()` + `_onChanged()`, so an `Edited` fires for an edit that changed nothing. The writer no-ops, and the editor snaps back only because of the re-raise.
    - **Suggested fix (small):** give the intrinsic constructor an optional `Func<string, bool>? tryWrite` alongside `write`. In `Commit`, when `tryWrite` returns false, take the same snap-back path as the §7 refusal: re-raise `StringValue`, and no `_onChanged`.
    - Pin it with `ANonPositiveClientSize_IsRefused_AndRaisesNoEdit`. It is not a blocker, because nothing wrong reaches the document; the only effect is a spurious `Edited`.
  - Object selector: prefer `model.AllComponents()` over `model.Components`. They are identical today (`FormDocument.cs:108`), but `AllComponents()` is the walk the rest of the VM uses (`CodeEditorDocumentViewModel.cs:622`).
  - Web form with **no `<Layout>`**: Cols/Rows/Gap rows now appear (before, they appeared only when `form.Layout != null`, `FormPropertyGridViewModel.cs:252`). The first write goes through `FormRootValues.Set`'s `form.Layout ??= new FormLayout()`. **Check** that `FormDocumentWriter.ApplyLayout` emits a valid `<Layout …>` (with `Kind`) for a layout created this way, and add a round-trip assertion if it does not.
  - Degraded `ClientSize`: `FormRootValues.Get` returns null unless both are positive, so the frozen row's `RawValue` is `""`, not the user's bad text. A frozen row that shows an empty box beside "preserved exactly as written" contradicts itself (the B1 principle). Either read the raw attribute for the frozen display, or note it for slice 3's Properties-stored root work. Not a blocker.
  - `_syncingObjects` / the identity guard: the analysis in M6 still holds against the current `CodeEditorDocumentViewModel`. `SelectInDesigner` → `Selection.Set` → `Changed` → `PropertyGrid.SelectedControl` → `Rebuild` → `RefreshObjects` sets the SAME item instance, so the nested set is a no-op.
- **Step 6.** Still valid at `:1124`. The one-store invariant holds: the grid only raises `SelectionRequested`, and `SelectInDesigner` stays the single writer, together with the constructor's `Selection.Changed` handler.
- **Red tests that are already green:**
  - `AWebPagesFormRows_…` (existing) stays green, as intended.
  - Every new `FormPropertyGridDisplayTests` row is genuinely red, because none of `DisplayItems`/`Objects`/`SelectedObject`/`SelectionRequested`/`SearchText`/`SelectedItem`/`IsCategorized` exist today.

### Task 13: `FormPropertyGridView` extraction
- **Anchors are all still exact.**
  - Property window `CodeEditorDocumentView.axaml` **`:313-444`**: comment `<!-- Property window, shaped like Visual Studio's:` at :313, closing `</Border>` at :444.
  - Grid-own bindings at `:323-342` (quoted in the `NoLongerCarries…` test summary).
  - Canvas `SelectedControl="{Binding PropertyGrid.SelectedControl, Mode=TwoWay}"` at **`:240`**.
  - Tray Delete `CommandParameter="{Binding PropertyGrid.SelectedControl}"` at **`:276`**, pinned by `FormTrayViewTests.cs:115`.
  - `xmlns:controls="using:VisualGameStudio.Shell.Views.Controls"` is already declared (`:7`).
- **The measured claim still holds.** No test reads the grid's own elements from the document view. Grep for `PropertyGrid.(Rows|SelectedRow|Header|Description)` in tests finds only VM-level use (`FormDesignModeTests.cs:147` reads `PropertyGrid.Header` on the VM, which the plan keeps).
- ⚠ **The extracted view no longer shows `Header`/`HeaderKind`**: the object selector replaces the caption. That is deliberate (spec §3), but say so in the commit, because `Header` stays on the VM for `FormDesignModeTests`.
- `FormPropertyGridView`, `FormObjectItem` and `FormPropertyCategoryHeader` do not exist yet; there are no name collisions.
- Step 2's "build succeeds": true only if Task 12 has landed (the test references `FormPropertyCategoryHeader`/`FormObjectItem`). The tasks are sequential, so this is fine.

### Task 14: real view, headless
- **Anchors**
  - `DesignerHeadlessApp.cs:31` (`UseHeadlessDrawing = false`) is exact.
  - `FormDesignerRealViewTests.cs:66-72` (`e.Control`/`e.Role`/`e.Bounds`) is exact.
  - The rig pattern (`Open`, :154-177) matches the plan's `Open`.
- **Resolve the plan's own ⚠ now.** `FormLayoutRole` (`VisualGameStudio.Shell/Controls/FormCanvasTransform.cs:900-913`) is `Control | Band | Cell | TypeHere`. In `ClickingAControlOnTheCanvas_MovesTheSelector`, change `.First(e => ReferenceEquals(e.Control, btn))` to `.Single(e => ReferenceEquals(e.Control, btn) && e.Role == FormLayoutRole.Control)`, matching `FormDesignerRealViewTests`.
- **Add one real-view test the plan lacks: §7 snap-back through the real TextBox.**
  - Task 11's refusal snaps the editor back by raising `PropertyChanged(StringValue)` from inside the binding's own source write. Whether Avalonia 11.3 re-reads the source during its own update is unmeasured.
  - Suggested test: type `maybe` into the ForeColor TextBox (select the row, focus its `TextBox`, `KeyTextInput("12345")`, move focus away), then assert that the TextBox's `Text` is back to `""` and the file is unchanged.
  - If it does not snap back, dispatch the re-raise with `Dispatcher.UIThread.Post` in `Commit`'s refusal branch. Spec §8 lists "every editor" under real view.
- The Rig record's `Control(string id)` method does not shadow `Avalonia.Controls.Control` in `Click(Control target)`, because type-name lookup ignores methods. It compiles.

### Task 15: mutation checks
- Every mutant still targets real code once Tasks 11–12 land as corrected.
- **Add two mutants that protect slice-1 behaviour slice 2 rewrote:**
  - **M9, frozen shows raw.** In `DisplayValue`, delete the `IsFrozen ? RawValue :` arm. Expected red: `FormPropertyGridTests.AFrozenRow_ShowsTheRawText_NotTheCanonicalSpelling`.
  - **M10, invariant `IntRow`.** In `IntRow`, change `read().ToString(CultureInfo.InvariantCulture)` to `read().ToString()`. Expected red: `ANegativeIntrinsicEdit_UnderAUnicodeMinusCulture_ShowsAnAsciiHyphen`. This requires `UnicodeMinusCulture.Require()` to pass on the machine; if it self-skips, record that the mutant is unkillable HERE rather than calling it killed.
- M1's text says `TheFormsRows_ComeFromFormRoot` "stays green". Correct: the Form's `Text="Hello"` differs from a null default.

### Task 16: gate, commit, click-through
- **Step 2 base.** Compare failure NAMES against slice 1's gate at **e0a645eb**: fast 7806 / 5 failed = `Emit_Replaces…AnotherHandleHasMapped` ×2, the intermittent `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, and `SearchSnippets` ×2 (`docs/HANDOFF.md:46-50`). The new tests raise the count, so compare names, not counts.
- **Step 5 staging list:** add `BasicLang/Forms/FormRootValues.cs` (already listed) for the doc-comment update above.
- **Step 6 click-through additions** (each is a risk left open above):
  - Paste/cut/undo, then check that the selector lists the new or removed control. `RefreshObjects` runs only on a selection change or `Load`.
  - A web form with no `<Layout>`: set Cols, save, reopen.
  - Type an invalid colour and tab away, and check that the box snaps back.

---

## Slice-1 review follow-ups already in the plan that touch slice 2
- There is no "CARRIED FROM" or "EXECUTION NOTE" inside Slice 2 (3630–5974). All carried notes sit in Slice 1's body or in Slices 3 and 5.
  - `:1604` / `:2907` (Task 4 review): FormAssetEmitter and `FormRetarget.ConvertProperties`. Emission and retarget, not the grid.
  - `:5978-5994` (slice 3 backlog): culture and geometry diagnostics, web refused-value diagnostic. Not slice 2.
  - `:6020-6030`: slice 5 binds.
- **Implicit follow-ups this pre-flight found**, which slice 2 must not regress: slice 1's frozen-raw display (B1), invariant Int read/write (B2, B3), and the canonical-equal no-op. These are guarded by `FormPropertyGridTests.cs:298-405`, `:597-664`, `:741-766`.
- `DescribeRefusal` now **throws** for an accepted value (`FormControlCatalog.cs:502-508`). The plan's `Commit` never calls it. If execution adds a refusal message to the description pane, call it only after `!Accepts(value, target)`.

---

## EXECUTION NOTES (added while executing slice 2)

- **Task 11 (99e3f6f9): the B2 mutant is EQUIVALENT on every editable row** — every Int a row displays is already
  invariant ASCII text (Canonical/TryParseInt), and .NET accepts an ASCII hyphen under sv-SE, so `int.TryParse`
  vs `TryParseInt` in `IntValue` differ only on a FROZEN row's raw text, which no editor shows. Recorded, not tested.
- **Task 11 review:** carried to Task 12 — the intrinsic constructor's unused `definition/target/isPresent/reset`
  parameters need a dedicated catalog-row-outside-the-bag constructor/factory with `target` REQUIRED and
  `isPresent`/`reset` passed together; and an intrinsic write that changes nothing must not raise `Edited` (let the
  write report whether it changed anything — the same shape as the pre-flight's `tryWrite` for `FormRootValues.Set`).
  Carried to Task 14 — decide whether a refused value gets a visible message (spec §7 "refused in the editor"),
  and MEASURE whether Avalonia re-reads a binding during its own push (if not, post the refresh).
- **Task 12: `FormPropertyRow`'s constructors changed shape** — Task 13/14 code must use these, not the plan's:
  - The intrinsic constructor no longer takes `definition/target/isPresent/reset`, and its `write` is
    `Func<string, bool>` — true only when the model CHANGED. `IntRow` reports it by re-reading; Anchor/Dock go
    through `Changing(read, write)`. A false return takes the §7 snap-back path (editor refresh, no `Edited`).
  - A catalog row stored outside the bag is `FormPropertyRow.ForStoredValue(definition, target, read, write,
    remove, onChanged, frozenReason, frozenText)`: `target` required; `read` returns null for ABSENT, so presence
    cannot disagree with the value; `remove` null = no Reset. `frozenText` (a Degraded root's
    `DegradedProperty.Value`, e.g. `Width="abc" Height="300"`) is what a frozen row shows, and makes it PRESENT.
  - The FormRoot `write` is `Set(...) && Get changed` — so `0, 300` (refused) and `400,300` over `400, 300`
    (same numbers) raise no `Edited`.
  - `Rebuild` clears BOTH `SelectedItem` and `SelectedRow`: today's AXAML still binds `SelectedRow` TwoWay until
    Task 13 rebinds the list to `SelectedItem`.
  - Mutants run (all killed): identity guard, grid-writes-its-own-selection, the constructor wiring, collapse
    memory, frozen raw text, root same-value, refusal path, `IntRow` change report, search, header clears the
    described row, and M10 (`UnicodeMinusCulture` did NOT self-skip here).
- **Task 13: the sort toolbar is two `RadioButton`s wearing the ToggleButton theme, NOT the plan's bare
  `ToggleButton`s.** A ToggleButton unchecks on a second click, so clicking the mode already shown flipped to the
  other one. ⛔⛔ And **no `GroupName`**: a named group spans the whole visual ROOT, so every grid view in the IDE
  window (two form tabs, a split) is one group. Measured headless: two views sharing a name LOCKED THE DISPATCHER
  (an endless check/uncheck loop between their bindings; `--blame-hang-timeout` caught it). Unnamed radios group
  by parent panel. Pinned by `FormPropertyGridViewTests.TwoGridViewsInOneWindow_SortIndependently` and
  `TheSortButtons_ClickingTheModeAlreadyShown_KeepsIt`.
  - ⚠ UNEXPLAINED once: the sort test (then with `GroupName`, and repeat clicks at the SAME point) went red in
    one full fast run, green alone, and did not reproduce in the next full run. Cause NOT proven — a same-point
    repeat can be stamped a double-click (FormDesignerRealViewTests measured that), so repeats are now offset.
    It stayed green in the full run after both changes. Task 14: offset repeat clicks.
  - The extracted view no longer shows `Header`/`HeaderKind` (the object selector replaces the caption);
    `Header` stays on the VM for `FormDesignModeTests`.

## CONFIRMED SOUND
- Slice 1 has not already implemented anything slice 2 adds: `SameValue`, `IsPresent`/`IsDefaultShown`/`IsBold`/`CanReset`/`ResetCommand`, `Category`/`Description` on the row, `FormRootValues.CanReset`, `DisplayItems`/`Objects`/`SelectionRequested`, and FormRoot-driven form rows. Today's `AddFormRows` is still hand-written, `FormPropertyGridViewModel.cs:228-267`.
- All slice-1 APIs slice 2 calls exist with the signatures it assumes: `DefaultFor(target)`, `WebDefault` (`""` means no web default), `Canonical`, `Accepts(value, target)`, `TryParseInt`/`TryParseSize`, `FormControlCatalog.FormRoot` (Text / ClientSize [WinForms] / Cols·Rows·Gap [Web], `:1832-1864`), `FormRootValues.Get/Set`, `FormFile.DegradedReasonOfRoot` (`FormFile.cs:93`).
- Clearing means reset, versus slice 1's no-op rule: no conflict.
  - Clearing an absent row is a no-op (`""` equals the displayed `""`); a null default displays as `""`.
  - Clearing a present typed row resets it.
  - Clearing a present String writes `""`.
  - Pushing the displayed default is a no-op. That is the rule `PushingTheDisplayedDefaultBack_WritesNothing` needs, and it does not conflict with any slice-1 pin.
- Bold/default versus `DefaultFor`/`WebDefault`: `DefaultValue` reads `DefaultFor(_target)` then `Canonical`, so there is one source.
- One selection store: the selector only requests; `SelectInDesigner` is the single writer.
- `[RelayCommand]` adjacency is correct in the plan's `FormPropertyRow` (`Reset`, `SetDockRegion`).
- `[ObservableProperty]` is one per field, and `IsAlphabetical` is a computed mirror notified from `OnIsCategorizedChanged`.
- The binding-reflection test design (a DataTemplate re-scopes to its DataType; a missing file FAILS) matches `FormStripViewTests`' approach. `ResetCommand` (generated public) and every other plan binding path resolve against the planned types.
- `FormDesignerRealViewTests` stays unaffected: the canvas binding and the tray Delete binding stay in the document view.
- FormRoot `Text` reset removes the attribute: `FormDocumentWriter.cs:181` `SetAttributeIfChanged(root, "Text", model.Text)` treats null as removal.
- No existing test asserts `DescriptionBody`/`DescriptionTitle`/`SelectedRow`, so moving the description to the catalog `Description` breaks nothing.
