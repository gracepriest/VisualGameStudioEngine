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

## C. Implementation and tests

1. Repair partial syntax/lifecycle edits; implement seven kinds and four manifest rows.
2. Extend both DOM declarations and the recording DOM; attach/detach/swap tests must run, not just parse.
3. Add catalog-driven web-event cases for these seven kinds (sender identity, one event per gesture), property mappings, defaults, no-op setters, literal captions, TextBox methods and repeated swaps, radio native exclusion, detach cleanup, caption click de-duplication and PerformClick ancestor suppression.
4. Run project-route builds (library copies must be refreshed after every .bas edit), the named gate, then the fast subset by failure NAME.
5. Review against the spec and for quality; mutation-check a load-bearing event/DOM rule and restore only the mutation.

## D. Gate

```
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --no-build --nologo --filter "FullyQualifiedName~PortableFirstKindsNodeTests|FullyQualifiedName~PortableControlNodeTests|FullyQualifiedName~PortableMembersManifestTests|FullyQualifiedName~JavaScriptLibraryIdiomTests|FullyQualifiedName~JsExecutionTierRosterTests|FullyQualifiedName~WebFormsLibraryInclusionTests|FullyQualifiedName~DomCoreLockStepTests"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --no-build --nologo --filter "TestCategory!=Integration"
```

Fast baseline: the six Task 30 failure names only. No push, stash or IDE forms drop. Stage by name; commit via `git commit -F`.
