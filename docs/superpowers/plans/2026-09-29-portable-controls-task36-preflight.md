# Task 36 preflight: the 2a mutation pass, gate, IDE drop, and owner click-through

Plan: `2026-09-29-portable-control-library.md`, Task 36. Spec §11.9, §12.
Base: `feat/portable-controls` @ `ade6b1fd`, working tree clean. Every anchor below was re-read on
this tree; the plan's line numbers had drifted.

## What already exists

| Fact | Where |
|---|---|
| The library is included for a JavaScript build only, by a token scan, when `System.Windows.Forms` or `System.Drawing` is named | `BasicLang/Compiler.cs:639-710` (`WithWebFormsLibrary`, `HasPortableNamespaceReferenceText`) |
| The method/property manifest is real and gated | `BasicLang/Forms/PortableMembers.cs:30-67`; `PortableMembersManifestTests` |
| The style marker is the only authority for generated-code style | `BasicLang/Forms/FormCodeStyle.cs:71-153`; `FormCodeStyleTests` |
| Client origin insets and dock padding are separate rules | `BasicLang/Forms/FormClientArea.cs`; `FormClientAreaTests` |
| `.Element` is refused on desktop only when the receiver's declared type names a library class | `BasicLang/SemanticAnalyzer.cs:12762-12773`, `PortableLibraryTypes.IsLibraryReceiver` |
| First kinds wire real DOM events | `BasicLang/lib/js/forms/Control.bas`, `CheckBox.bas`, `RadioButton.bas`; `PortableFirstKindsNodeTests`, `PortableControlNodeTests` |
| Anchor writes are mirrored with `FormAnchorCss` | `Control.bas:771-830`; `PortableAnchorLockStepTests` |
| The live dock script walks the live DOM and uses requested `data-vgs-w/h` | `BasicLang/Forms/FormDockScript.cs:82-166`; `FormDockScriptTests` |
| The E2E test drives the region writer, editor, BuildService, CLI, Edge, and WinForms | `VisualGameStudio.Tests/Shell/PortableFormEndToEndTests.cs` |
| The IDE has a tracked `dom-core.bli` copy but no `forms` copy yet | `IDE/lib/js/dom-core.bli`; `IDE/lib/js/forms/` is absent |
| The fast subset baseline is 10,761 passed, 6 known failures, 19 skipped | `fast-subset.log` from the Task 35A pass |

## Mutation matrix

Each mutant is applied with the Edit tool, then the test project is rebuilt and the named test is
run. Every mutant is reverted with the inverse Edit and a rebuild; `git checkout --` and
`Copy-Item` are not used for reverts.

| # | Rule | Mutant (anchor) | Must turn red |
|---|---|---|---|
| M1 | Inclusion trigger: `System.Drawing` | Delete the `System.Drawing` arm (`Compiler.cs:702-706`) | `WebFormsLibraryInclusionTests.UsingSystemDrawing_IncludesTheDrawingTypes` |
| M2 | Inclusion trigger is token-based | Replace the token scan with a raw `source.Contains(...)` (`Compiler.cs:683-710`); removing only the comment/string filter did not change this fixture because the lexer collapses each comment or literal to one token | `WebFormsLibraryInclusionTests.CommentOrStringMention_DoesNotIncludeTheLibrary` |
| M3 | Manifest gate | Remove the `Button.PerformClick` row (`PortableMembers.cs:65-66`) | `PortableMembersManifestTests.EveryPublicMethodHasAManifestRow` |
| M4 | Style marker | Make `PortableValue` read as `FormStyle.Dom` (`FormCodeStyle.cs:115-120`) | `FormCodeStyleTests.Portable_OnAWebDocument_IsPortable` |
| M5 | Client insets | Force `inset = 0` in `FormClientArea.Of` (`FormClientArea.cs`) | `FormClientAreaTests.PanelBorder_IsTheSingleClientOriginAndSizeRule` |
| M6 | `.Element` receiver-name check | Make `PortableLibraryTypes.IsLibraryReceiver` true for every non-null type | `PortableBuildErrorTests.DesktopBuild_DoesNotNameElementOnAnObjectReceiver` (new guard) |
| M7 | Caption click de-duplication | Remove the `e.target IsNot VgsInputElement()` guard (`CheckBox.bas:41-44`) | `PortableFirstKindsNodeTests.CaptionForwarding_RaisesOneClickAndOneChangePerStateTransition` |
| M8 | Change-only events | Remove the `old = value` guard in the `Control.Text` setter (`Control.bas`) | `PortableFirstKindsNodeTests.Text_IsLiteralAndChangeOnly_WithoutDestroyingCaptionChildren` |
| M9 | relatedTarget guard | Always raise `OnEnter` in `VgsOnDomFocusIn` (`Control.bas:424-429`) | `PortableControlNodeTests.ClickMouseKeysAndEnter_CarryTheDomValues` |
| M10 | Anchor axis, both sides | Swap the horizontal or vertical axis booleans in `VgsWriteGeometry` (`Control.bas:787-790`) | `PortableAnchorLockStepTests.TheLibraryAxis_MatchesFormAnchorCss_ForAllSixteenCombinations` |
| M11 | Live-dock reads the live tree | Replace `vgsDockLive(form)` with the emitted `root` in `compute` (`FormDockScript.cs:131-134`) | `FormDockScriptTests.TheLiveWalk_ReadsTheDomEachReflow_AndCompletesSixProperties` |
| M12 | Live-dock reads requested size | Read `offsetWidth/offsetHeight` instead of `data-vgs-w/h` (`FormDockScript.cs:113-118`). `clientWidth/clientHeight` did not redden because the fixture's Panels have client size equal to requested size | `FormDockScriptTests.TheLiveWalk_ReadsTheDomEachReflow_AndCompletesSixProperties` |
| M13 | Reflow coalescing | Make the observer call `reflow` directly instead of `request` (`FormDockScript.cs:160-163`) | `FormDockScriptTests.OneScheduler_CoalescesTheObserverAndTheLibraryRequest` (new guard) |
| M14 | R2 cascade rule | Remove the docked-control early return in `VgsWriteGeometry` (`Control.bas:771-782`) | `PortableControlNodeTests.Dock_R2_LeavesGeometryToTheScript_AndNoneRestoresRequestedSize` |
| M15 | Phone-width anchor rule | Make `VgsDesktop` always true (`Control.bas:681-689`) | `PortableControlNodeTests.Geometry_BelowTheBreakpoint_ReadsAndWritesRequestedBounds_InTheMediaGuard` |
| M16 | E2E handler wiring | Skip `AppendBinds` for a portable Button (`RegionWriter.cs:1021`, `1194-1248`) | `PortableFormEndToEndTests` |
| M17 | Harness fidelity: never shown | Remove `form.Show()` from the generated WinForms driver (`PortableRuntimeHarnesses.cs:176`) | `PortableFormEndToEndTests` (desktop case) |
| M18 | Harness fidelity: wrong order | Run the desktop driver before `form.Show()`/layout (`PortableRuntimeHarnesses.cs:171-191`) | `PortableFormEndToEndTests` (desktop case) |
| M19 | Drop-vs-source | Run `IdeDropLibraryMatchesSourceTests` before creating `IDE/lib/js/forms` | `IdeDropLibraryMatchesSourceTests.EveryPortableLibraryFile_IsByteIdenticalInTheIdeDrop` |
| M20 | Drop-vs-source after the drop | Mutate one byte in `IDE/lib/js/forms/Control.bas`, then run the gate | `IdeDropLibraryMatchesSourceTests` |

M19/M20 are the guard's red and green proof, not source mutations. The 2.0a-owned rules (base-first
order, RemoveHandler identity, `#If` prefix traps, DEBUG/RELEASE, line-preserving blanking) were
already mutation-pinned in Tasks 6-17; Task 36 does not repeat them because their code and tests are
outside Tasks 28-35A. Decimal and decline byte-identity are 2.0b/2c-owned and are not in this gate.

New guards added by this task:

- `PortableBuildErrorTests.DesktopBuild_DoesNotNameElementOnAnObjectReceiver` proves M6's
  receiver-name check rather than only the presence of the diagnostic.
- `FormDockScriptTests.OneScheduler_CoalescesTheObserverAndTheLibraryRequest` proves M13; the
  existing tests prove the observer's attributes and the resulting rules, not the scheduler.
- `IdeDropLibraryMatchesSourceTests` compares every source form file and `dom-core.bli` byte for
  byte, and rejects extra files in `IDE/lib/js/forms`.

## Execution record

Baseline before mutations:

- Targeted fixtures: **131 passed, 0 failed, 0 skipped** (`WebFormsLibraryInclusionTests`,
  `FormCodeStyleTests`, `PortableMembersManifestTests`, `FormClientAreaTests`,
  `PortableBuildErrorTests`, `PortableFirstKindsNodeTests`, `PortableControlNodeTests`,
  `PortableAnchorLockStepTests`, `FormDockScriptTests`).
- `PortableFormEndToEndTests`: **passed** (13 s on the final run).
- `IdeDropLibraryMatchesSourceTests` before the drop: **failed** as designed with
  `the IDE drop has no portable library`.

Fixture repair, not a production change: Task 34 correctly refuses `New Form()`/`New Button()` on
the web outside a designer `init` region, but the Task 29/31 behaviour fixtures predated that rule.
`PortableControlNodeTests.Run` now wraps its generated source in a portable `init` region, and the
shipping-build fixture in `PortableFirstKindsNodeTests` carries the same marker. This removed the
unrelated 59 integration failures and let the mutation anchors start green.

Every mutant below turned its named test red, then was reverted with Edit and rebuilt:

| Mutant | Observed red |
|---|---|
| M1 | `Color` is absent from `CombinedIR.Classes` |
| M2 | `Button` is present in `CombinedIR.Classes` despite a comment/string-only mention |
| M3 | `Button.PerformClick is public but has no PortableMembers row` |
| M4 | `read.Style` is `Dom`, expected `Portable` |
| M5 | FixedSingle/Fixed3D cases get inset 0 instead of 1/2 |
| M6 | an Object receiver's `.Element` is named `ElementOnDesktop` |
| M7 | caption forwarding raises 2 clicks / 2 changes instead of 1 / 1 |
| M8 | `changes=2`, expected `changes=1` |
| M9 | `enters=2`, expected `enters=1` |
| M10 | all sixteen anchor combinations disagree with `FormAnchorCss` |
| M11 | live-walk CSS falls back to the emitted seed |
| M12 | live-walk CSS sizes come from `offsetWidth/offsetHeight` (0 in the stub) |
| M13 | coalescing test reports `writes=2`, expected 1 |
| M14 | a docked control gets library geometry declarations |
| M15 | phone-width reads return live desktop coordinates |
| M16 | Edge label stays `Label1`, expected `Ada!` |
| M17 | WinForms label stays `Label1`, expected `Ada!` |
| M18 | WinForms label stays `Label1` when the driver runs before `Show()` |
| M19 | drop gate reports the missing `IDE/lib/js/forms` |
| M20 | drop gate reports `differs from source: forms\Control.bas` |

Post-drop gates:

- `IdeDropLibraryMatchesSourceTests`: **passed**, then red on M20, then **passed** after the Edit
  revert.
- Fast subset (`TestCategory!=Integration`): **10,762 passed, 6 failed, 19 skipped**. The six
  failure names are exactly the Task 0 baseline:
  `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
  `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`,
  `SearchSnippets_EmptyQuery_ReturnsAll`, and
  `SearchSnippets_WhitespaceQuery_ReturnsAll`.
- Owner click-through remains the one manual gate; the checklist is below.

## Commands

Build once after each mutation:

```powershell
dotnet build VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --nologo
```

Targeted named test, no build:

```powershell
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --nologo `
  --filter "FullyQualifiedName~<FixtureName>"
```

The E2E mutant uses the Task 35A command:

```powershell
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --nologo `
  --filter "FullyQualifiedName~PortableFormEndToEndTests"
```

## Fast subset

After all mutants are reverted and the task's guards are green:

```powershell
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --nologo `
  --filter "TestCategory!=Integration"
```

The six known failures are the Task 0 baseline names recorded in the Task 35A preflight. Compare
the failure NAMES, not only the count.

## IDE drop

1. Build the Shell in Release.
2. `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E` (never `/MIR`). This includes
   `IDE\lib\js\dom-core.bli` and creates `IDE\lib\js\forms\`.
3. Run `IdeDropLibraryMatchesSourceTests` after the drop; it must be green and must be excluded
   from the fast subset by its `Integration` category.
4. Do not commit a partial drop. If the drop is interrupted, rerun the same robocopy.

## Owner click-through

The owner is the only one who can exercise the real IDE gestures. The checklist is the plan's
Task 36 list:

1. Add Web Form (portable): the code-behind has three `Using` lines, `Inherits Form`, and the init
   call after `New()`.
2. Drop Button, Label, TextBox, CheckBox, RadioButton, Panel, GroupBox; double-click the Button:
   the stub is `(sender As Object, e As EventArgs)` below the region.
3. Handler: `lblMsg.Text = txtUser.Text`, `chkRemember.Checked = True`,
   `btnLogin.Left = btnLogin.Left + 20`.
4. Build web, open in Edge: captions, legend, and borders are visible; the click works; a
   right-anchored control moved at run time stays anchored on resize.
5. Retarget to WinForms, build, run: the same behaviour.

The automated gate is not a substitute for this click-through; record the owner's result before
calling Task 36 complete.
