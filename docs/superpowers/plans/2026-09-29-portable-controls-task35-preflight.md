# Task 35 preflight: the coverage gate and the behaviour twin

Plan: `2026-09-29-portable-control-library.md`, Task 35. Spec §11.1–§11.4, O9, review I9.
Base: `feat/portable-controls` @ `b34177ba`, working tree clean. Every anchor below was re-read
on this tree; the plan's line numbers had drifted.

## What already exists

| Fact | Where |
|---|---|
| The catalog is one list of 34 kinds | `BasicLang/Forms/FormControlCatalog.cs:1717` (`All`) |
| Seven kinds are real in 2a; the rest are shells | `Label:1719`, `TextBox:1726`, `Button:1770`, `CheckBox:1782`, `RadioButton:1802`, `Panel:1873`, `GroupBox:1890`; e.g. `ComboBox.bas` is 6 lines and `Timer.bas` is 5 |
| The first-kinds list already exists | `PortableFirstKindsNodeTests.cs:16` (`FirstKinds`) |
| The catalog adds web-only `CssClass`/`Style` to every web element | `FormControlCatalog.cs:1584`, `:1590`, `:1632` (`WithWebExtras`) |
| ⛔ The library does **not** declare those two rows | `BasicLang/lib/js/forms/Control.bas` has no `CssClass`/`Style`; the coverage gate is the first reader that would notice |
| The method manifest is 14 rows | `BasicLang/Forms/PortableMembers.cs:30-67` |
| `Add`/`Remove` carry `New Control()` as their sample | `PortableMembers.cs:41-44`; an unattached control is not a valid collection member on either target |
| The real CLI/csc WinForms app builder exists | `WinFormsReferenceHarness.cs:121` (`Measure`), `:325` (`DriverSource`) |
| The real web builder/site server exists | `EdgeLayoutHarness.cs` (`BuildSite`, `WebPreviewServer`, fresh profile) |
| The node stub-DOM runner exists | `PortableControlNodeTests.cs:18` (`StubDom`), `:318` (`Run`) |
| Generated portable code drops designer geometry/property lines | `RegionWriter` portable style; `FormScaffolder.cs:110` |
| The page dispatcher instantiates the code-behind class | `FormDispatch.cs`; verified with a real CLI build |
| A page can report back through `document.title` | verified in Edge after a real CLI build |
| No CDP client exists in the tree | no `ClientWebSocket`/`remote-debugging-port`/`Input.*` production or test code |

Known pre-existing warning, not this task: a web build with the library prints
`BL6017: .NET type 'System.Object' has no accessible member named 'vgsDock'`.
It is a warning and does not decide anything here.

## Decisions

1. **Scope is the seven implemented web kinds.** Task 35 covers exactly the kinds
   `PortableFirstKindsNodeTests.FirstKinds` names today. A catalog-wide gate now would demand a
   tested `ComboBox`, `ListBox`, `PictureBox`, … that are six-line shells owned by Tasks 37–39.
   The gate still iterates the catalog for every property and event; only the kind roster is
   the 2a slice. Task 42 replaces that roster with the full catalog. The roster is made
   `internal` (not copied) so there is one first-kinds list in the test suite.

2. **The gate fixes the two missing web-only library members.** `CssClass` and `Style` are
   catalog rows on every web element but `Control.bas` never declared them. Task 35 adds them
   to the library as `String` properties over `className` and `style.cssText`. They stay under
   `#If WEB` in every generated program; a desktop build must still report them through
   `WebUnavailableMember` (already the Task 34 arm). No IDE forms drop: Task 36 owns that.

3. **The runtime shape is one portable scaffold per case, not hand-built code.** For every
   catalog/method/behaviour case:
   - `FormDocument` + `FormScaffolder.Create(..., FormTarget.Web, portable: true)` +
     `RegionWriter.Write` creates the fields and `InitializeComponent`; controls are never
     created outside the init region (`RuntimeControlCreation`).
   - A generated `Cover`/`Run` method is appended after the regions. On web the constructor
     calls it (`#If WEB`); on desktop the runtime driver calls it after `Show`, so effective
     visibility and focus have the same meaning on both sides.
   - Output is one `Emit(line)` helper: `Console.WriteLine` on desktop, `document.title`
     append on web. `document.title` is already proven as a non-dialog return channel; no
     console scraping.

4. **Two runtime harnesses, one per reference window.** The existing layout harnesses stay
   layout-only; extending their step grammars with dialogs and input would make the pixel
   tests depend on a browser debugger.
   - `PortableWinFormsRuntimeHarness` reuses the Task 12 rules: real CLI `--target=csharp`,
     one app built by `dotnet build` (csc), `HighDpiMode.SystemAware`, one process, one form at
     a time, messages (`WM_LBUTTONDOWN`/`UP`, `WM_CHAR`, `WM_KEYDOWN`/`UP`) sent to the
     control handle. `GroupBox.Enter` is raised through the protected `OnEnter` by reflection
     only when focus cannot reach it; that step is named in the test.
   - `PortableEdgeRuntimeHarness` reuses the Task 13 rules: build a real JavaScript project,
     serve it with `WebPreviewServer`, launch Edge headless with a fresh `--user-data-dir`, a
     free `--remote-debugging-port`, `--force-device-scale-factor=1`, kill the process tree,
     and drive `Input.dispatchMouseEvent`/`Input.insertText`/`Input.dispatchKeyEvent`. Dialogs
     use `Page.javascriptDialogOpening` + `Page.handleJavaScriptDialog`. The client is a small
     `ClientWebSocket` JSON-RPC loop; no new package.
   - Both skip (never pass) unless Windows, Edge, Node, dotnet, and the Windows Desktop SDK
     are present.

5. **Catalog gate shape.** One form and one `sut` per kind.
   - **Both-target rows:** every catalog property whose `Targets` is null or contains both.
     Convert the sweep sample to a typed BasicLang expression (`"sample"`, `12`, `True`,
     `Color.Red`, the enum member, `New Font` fields, `New Padding` fields, `Cursors.Hand`,
     `"s"c` for `PasswordChar`). Assign, read back through the portable property, and `Emit`
     a normalized value (`Color` components, `Font` name/size×100/bold/italic, `Padding`
     components, enums as integers).
   - **Web-only rows:** `CssClass`/`Style` are assigned under `#If WEB`; `CssClass` is read
     back, `Style` is set to a valid declaration. Other web-only rows (`RadioButton.GroupName`)
     are set and read.
   - **WinForms-only rows:** compile-only. For every such row, a web build of shared code that
     touches it must fail with `WebUnavailableMember` naming `<Kind>.<Property>`; the same
     source compiles for desktop.
   - **Events:** every `FormEvents.WiredOn(def, FormTarget.Web)` row is wired and triggered
     once. Real input is used where it exists: click (`Label`, `Button`, `Panel`, `GroupBox`),
     typing (`TextBox`), click/change (`CheckBox`, `RadioButton`). `GroupBox.Enter` is a named
     synthetic `focusin` because a `fieldset` cannot be focused by CDP. `Panel.Paint` is
     WinForms-only; its web wiring is asserted as `WebUnavailableMember`, and the WinForms run
     wires it but does not require a paint count.
   - **Tiers:** the same generated **web** source is first run under Node against the recording
     stub DOM (fast tier; synthetic triggers, no layout claim), then in Edge (authority), and
     visually/input-wise mirrored in WinForms. Rendering/layout claims belong only to the Edge
     and WinForms runs, never to the node tier.

6. **Method gate shape.** One generated form per `PortableMembers` row (or a small grouped
   form where the row needs siblings). Arguments come from `SampleArguments`, with one named
   substitution: `Add`/`Remove`'s `New Control()` placeholder becomes the fixture's attached
   `btnSample`, because an unattached control is invalid on both targets. The member is called,
   then the observable is printed:
   - `Show`/`Hide`: `Visible`;
   - `Focus`: the `Enter` event (sender name) and the active element; this avoids the missing
     `Focused` member on web;
   - `BringToFront`: last child / last element id;
   - `Add`/`Remove`/`Item`/`Count`: count and returned name;
   - `MessageBox.Show`: return mapping with the harness answering the dialog (CDP
     `Page.handleJavaScriptDialog`, WinForms `WM_COMMAND` to the dialog button); the missing
     browser title is recorded as the spec's divergence, not compared;
   - `Form.Close`: `Visible` false;
   - `TextBox.Clear`/`AppendText`/`SelectAll`: text, and selection length via the target's own
     selection surface;
   - `Button.PerformClick`: `Click` event count and sender.
   Desktop and web outputs are normalized line-for-line and asserted equal; a differing effect
   is a failure, not an exemption.

7. **Behaviour twin shape.**
   - Build the pixel `.blform` with `Button`, `TextBox`, `CheckBox`, `Label`, a `Panel`, and a
     docked child; retarget it to a Canvas `.blwebform` with `FormRetarget.ConvertToPair`.
   - Build both code-behinds with `FormScaffolder` + `RegionWriter`; strip marker-delimited
     regions and assert the remaining bytes are identical **before** any build.
   - Append one shared scenario to both non-region tails. It exposes `Step(name)`, `Snapshot(label)`,
     and a `Log` string; values and rectangles are read through the same portable properties
     (`Text`, `Visible`, `Enabled`, `Checked`, `Left`/`Top`/`Width`/`Height`) on both sides.
   - Steps: set Text; toggle Visible/Enabled/Checked; click; type; select; move a right-anchored
     control; change a Dock; resize a container with docked children; resize the window.
   - Real input steps use Win32 messages on desktop and CDP `Input.*` on Edge. Non-input steps
     call `Step` on the form instance (reflection on desktop, `window.__vgsTwin` on web). Any
     step CDP cannot express is named `synthetic:<step>` in the event log.
   - Compare values exactly, control rectangles relative to the form client at ±1 px, and the
     event log ordered by sender/event/key arguments.

8. **Skip contract.** All three fixtures are `[Category("Integration")]`,
   `[NonParallelizable]`, and skip through `TestSkip.IgnoreEvenInsideMultiple` when Windows,
   Edge, Node, dotnet, or the Windows Desktop SDK is missing. A runtime failure after the
   prerequisite check is a failure, never a skip. The prebuilt IDE is not used.

## Tests

New files under `VisualGameStudio.Tests/Compiler/`:

- `PortableCatalogCoverageTests` — catalog rows over the seven kinds; fast node tier plus Edge
  and WinForms runtime tiers; web-only and WinForms-only row checks; event triggers.
- `PortableMethodCoverageTests` — one row per manifest member; real desktop and Edge runs;
  dialog answering; outputs equal.
- `BehaviourTwinTests` — non-region bytes first, then the neutral scenario over the pixel and
  Canvas pair; values, rectangles, log; real input.
- `PortableRuntimeHarnesses.cs` (test infrastructure) — the shared WinForms driver and Edge CDP
  client. `PortableFirstKindsNodeTests.FirstKinds` becomes `internal`.
- `Control.bas` — `CssClass`/`Style` web-only properties; no IDE forms drop.

## Gate

1. `PortableCatalogCoverageTests`, `PortableMethodCoverageTests`, `BehaviourTwinTests`
   (Integration, Windows).
2. `PortableFirstKindsNodeTests`, `PortableMembersManifestTests`, `PortableBuildErrorTests`,
   `FormPortableCodegenTests`, `WinFormsReferenceHarnessTests`, `EdgeLayoutHarnessTests`.
3. Fast subset `TestCategory!=Integration` against the known baseline (six failures named in
   Task 34 preflight; no new name).
4. Commit by name. No push (Tasks 29–34, and this stayed inside the same slice).
