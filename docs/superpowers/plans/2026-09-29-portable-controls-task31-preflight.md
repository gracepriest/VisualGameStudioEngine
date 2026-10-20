# Task 31 pre-flight: first kinds

Plan: `docs/superpowers/plans/2026-09-29-portable-control-library.md`, "### Task 31".
Spec: `docs/superpowers/specs/2026-09-29-portable-control-library-design.md` §5.4-§5.6.

Follow this document instead of the plan's Task 31 section where they disagree.

## A. Anchors (checked at d0a21d59; reconciliation after interrupted edits)

| Claim | Today | Verdict |
|---|---|---|
| Seven first-kind classes | `BasicLang/lib/js/forms/{Button,Label,TextBox,CheckBox,RadioButton,Panel,GroupBox}.bas` are shells | implement in place |
| Common lifecycle/events | `Control.bas`: private element/delegates, `VgsAttach`, `VgsDetach`, private `VgsAttachListeners` | add small virtual kind-listener hooks and an element replacement seam; preserve common semantics |
| Caption markup exists | `FormAssetEmitter.AppendControl` still emits bare input controls and fieldset text | Task 32 owns markup; Node fixtures use the specified label/input/span and fieldset/legend shapes now |
| Catalog `DomMember` exists | no such field; Task 32 owns it | test the §5.4 mappings now; do not add a competing catalog/mapping manifest |
| All catalog rows belong here | Task 40 owns exhaustive slice-3/5 properties/events; Tasks 32/33 own rendering/geometry | implement first-kind §5.4 members, test current web events from the catalog; do not claim full coverage early |
| TextBox alignment type | `FormControlCatalog.HorizontalTextAlign` uses `HorizontalAlignment`, unlike Button/Label `ContentAlignment` | add the correct enum to the library |
| DOM surface suffices | `dom-core.bli` lacks text input properties, select, attribute enumeration and replacement | extend both copies in lock-step; no IDE forms drop |
| Node runner exists | `PortableControlNodeTests` compiles via `CompileProjectFiles` and runs a recording DOM | reuse its runner/stub; extend stub with real bubbling, native radio exclusion and element replacement semantics |
| Roster | `JsExecutionTierRosterTests` has 109 fixtures | add `PortableFirstKindsNodeTests`, pin 110 |
| Manifest | `PortableMembers.Manifest` lacks four owned methods | add `TextBox.Clear/AppendText/SelectAll`, `Button.PerformClick` |

## B. Decisions
1. Implement first-kind behavior in the library `.bas` files, inheriting `Control`.
2. Each kind overrides kind-specific properties/events and adds owned methods. TextBox uses value/readOnly/maxLength, a Char password marker, horizontal alignment, and an input listener. Programmatic TextChanged is change-only.
3. Add Task 31 owned methods to `PortableMembers.Manifest`.
4. Keep common Control behavior unchanged. Lifecycle hooks attach/remove kind-specific listeners with the same delegate identity. Element replacement removes old library listeners, swaps in place, and reattaches all library listeners; user event subscriptions remain on the control. Arbitrary escape-hatch DOM listeners on a discarded element are not inspectable by browser APIs.
5. Multiline copies ALL attributes, current value and effective state, preserves the parent position, and restores password type when returning to an input. It raises no TextChanged by itself. No private base-field access from TextBox.
6. CheckBox/RadioButton operate on the inner input, with caption text only in a direct span. GroupBox writes only a direct legend, never the whole container. Literal text always uses textContent, not innerHTML.
7. A wrapper click is counted only when its target is the inner input: the caption's first bubbled click is ignored; the browser's forwarded input click raises one Click. CheckedChanged uses a DOM data-vgs-checked snapshot, avoiding a second event from the subsequent change notification.
8. RadioButton keeps a lazy registry of attached radios (not a second store of values). Reconcile both the checked radio and a browser-unchecked peer against DOM snapshots, so both raise once. Default name is the container id; explicit GroupName is shared across containers. Detach rebuilds the registry (JS List.Remove is unavailable).
9. Button.PerformClick checks effective Visible/Enabled and raises one Click with sender = the button. TextBox.Clear/AppendText reuse the change-only Text setter; SelectAll calls select().
10. Alignment, border and overflow writes use the existing VgsSetStyle seam; Task 33 replaces its body. Full slice-3 style readback/value constructors stay with Task 40. Panel has no web Paint source, so it is not invented here.
11. Measured parser blocker: `Property ReadOnly`, DOM `Property type/readOnly` and `Sub select` fail in `ConsumeIdentifierLike`, and readOnly/select also fail after a dot. There is no escaped-identifier syntax. Use the existing member-name keyword predicate at unambiguous declaration sites and admit ReadOnly/Select after a dot. Do not widen variable/expression-head names. Pin ordinary and extern declarations with a parser regression test; Node exercises the declared calls.
12. Measured Panel enum spelling: `BorderStyle.None` inside its same-named property lowers through `Me.BorderStyle` recursively; a fully qualified enum expression degrades to Object. Use explicit enum casts of the declared 0/1/2 values inside Panel, as the library already does for keyword-colliding enum members. Keep the public BorderStyle type and its observable tests unchanged.

## C. Implementation and tests

1. Repair partial syntax/lifecycle edits; implement seven kinds and four manifest rows.
2. Extend both DOM declarations and the recording DOM; attach/detach/swap tests must run, not just parse.
3. Add catalog-driven web-event cases for these seven kinds (sender identity, one event per gesture), property mappings, defaults, no-op setters, literal captions, TextBox methods and repeated swaps, radio native exclusion, detach cleanup, caption click de-duplication and PerformClick ancestor suppression.
4. Run project-route builds (library copies must be refreshed after every .bas edit), the named gate, then the fast subset by failure NAME.
5. Review against the spec and for quality; mutation-check a load-bearing event/DOM rule and restore only the mutation.

## D. Gate

```
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --no-build --nologo --filter "FullyQualifiedName~PortableFirstKindsNodeTests|FullyQualifiedName~PortableControlNodeTests|FullyQualifiedName~PortableMembersManifestTests|FullyQualifiedName~JavaScriptLibraryIdiomTests|FullyQualifiedName~JsExecutionTierRosterTests|FullyQualifiedName~WebFormsLibraryInclusionTests|FullyQualifiedName~DomCoreLockStepTests|FullyQualifiedName~KeywordMemberNameTests"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --no-build --nologo --filter "TestCategory!=Integration"
```

Fast baseline: the six Task 30 failure names only. No push, stash or IDE forms drop. Stage by name; commit via `git commit -F`.

The extended DOM declarations require the rebuilt parser (keyword-spelled members).
As with the portable markers/library, the prebuilt IDE is not a supported test of this
in-progress slice until Task 36 refreshes its compiler and library together.

## E. Review and mutation record

- Spec review: first-kind §5.4 mappings, literal text, change-only events, sender identity and four owned methods are exercised through the project-route compiler (which runs the standard optimizer). DOM mappings not yet in catalog metadata, exhaustive slice-3/5 coverage, page rendering and layout remain Tasks 32/33/35/40, not silently claimed here. The CLI and IDE BuildService legs also execute the emitted first-kind program under Node.
- Quality review: kind listeners precede AddressOf uses; attach/detach and TextBox replacement share identity-preserving cleanup; captions never replace a container's child tree; radio state is DOM-backed and its registry is lazy and pruned on detach. CheckedChanged and multiline tests inspect native DOM effects rather than calling OnX. No IDE forms copies or old DOM output expectations changed.
- Mutation: omit notification of a browser-unchecked radio peer -> `RadioGroup_NotifiesTheBrowserUnselectedPeer_AndHonoursContainerGroups` fails on the missing event counts. Restored.
- Mutation: omit the TextBox attribute-copy loop's write -> `TextBox_MultilinePreservesAttributesPositionAndSubscriptions_AndUnhooksOldElements` fails on missing data/ARIA/style/maxLength/password state. Restored.

## F. Gate outcome

- Release test-project build: 0 errors; source library copies refreshed.
- Named gate: **84/84 passed**, including 43 first-kind Node cases and both shipping build routes.
- Fast subset: **10,712 passed, 19 skipped**, with only these unchanged Task 30 baseline failures (compared by name):
  - `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`
  - `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`
  - `Emit_ReplacesAScriptThatAnotherHandleHasMapped`
  - `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`
  - `SearchSnippets_EmptyQuery_ReturnsAll`
  - `SearchSnippets_WhitespaceQuery_ReturnsAll`
- Both mutations were restored before the final build/gates. No push or IDE forms drop.
