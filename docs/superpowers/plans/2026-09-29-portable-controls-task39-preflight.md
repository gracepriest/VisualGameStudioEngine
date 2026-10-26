# Task 39 preflight: Timer, strips and strip items

Plan: `2026-09-29-portable-control-library.md`, Task 39. Spec A4.10/A4.11, A5.12, A6.2,
A10.3, G4/G12. Base: `feat/portable-controls` @ `238857c1`, with the Task 37 and Task 38
changes uncommitted. Every anchor below was re-read on this tree.

## What already exists

| Fact | Where |
|---|---|
| `Timer`, the three strip hosts and the five item shells are empty | `BasicLang/lib/js/forms/*.bas` |
| `ToolStripItemClickedEventArgs` is an empty class | `BasicLang/lib/js/forms/EventArgsTypes.bas` |
| The Timer catalog row is a tray component with `Interval` both-target and `Enabled` WinForms-only | `FormControlCatalog.cs:2266-2288` |
| Strips are `Place.Docked`; items are `Place.Item` and live in the host's `Children` | `FormControlCatalog.cs:2366-2487` |
| The host row's item verb is emitted in DOCUMENT order | `RegionWriter.cs:953-989` |
| Portable components are constructed as WinForms types, properties are emitted, then `AddHandler` | `RegionWriter.cs:1029-1192` |
| Portable strip/item markup already exists: `<nav>/<ul>/<li>`, `<menu>`, `<footer>`, roles, ids, item text | `FormAssetEmitter.cs:512-627` |
| A menu item's caption is a raw text node before its nested `<ul>` | `FormAssetEmitter.AppendPortableControl` |
| `Element` has `firstElementChild` but no `firstChild`, `nodeValue` or `nodeType` | `BasicLang/lib/js/dom-core.bli` and `IDE/lib/js/dom-core.bli` |
| `Control` supplies `Name`, `Visible`, `Dock`, `CssClass`, `Style`, attachment and the event base | `Control.bas` |
| `ObjectCollection` is the existing live-DOM collection pattern | `Core.bas` |
| The method manifest has 23 rows and the method gate pins that number | `PortableMembers.cs`, `PortableMethodCoverageTests.cs` |
| The coverage gate's `AllKinds` is the union of the Task 37/38 node-tier arrays and assumes every kind is an element control with `sut` geometry | `PortableCatalogCoverageTests.cs` |

## Decisions

1. **Timer keeps two implementations.** The DOM-style `w.setInterval` path is unchanged byte for
   byte. The portable Timer uses the library's own `::window.setInterval`/`clearInterval`; the
   generated portable init constructs `New System.Windows.Forms.Timer()` and writes `Interval`,
   `Enabled` and `AddHandler Tick`. `Enabled` becomes a both-target catalog row because the portable
   library can start and stop its own interval. This intentionally changes retarget and warning
   expectations.
2. **Timer runs only while enabled.** `Start` sets `Enabled = True`, `Stop` sets it false, and
   changing `Interval` while enabled restarts the interval. `Tick` is raised by the interval
   callback. The class has no element and is not a `Control`.
3. **Strips attach by id through the host's `Items` collection.** `ToolStrip` inherits `Control`
   for `Name`, `Dock`, `Visible`, `CssClass`, `Style` and `Form.Controls.Add`. `Items` is a
   `ToolStripItemCollection`; item elements are found by `Name` in the page.
4. **Items are not Controls.** `ToolStripItem` owns the element, text, visibility, class/style,
   tooltip, `Click`, and a parent/strip link. `ToolStripButton` adds the both-target `Enabled`
   row. Nested menu items resolve the root strip through their parent item.
5. **Menu item `Text` writes the item's own direct text node**, not `textContent`, so a nested
   `<ul>` survives a write. This needs `firstChild`, `nodeValue` and `nodeType` in both dom-core
   copies and in the recording stub.
6. **Accelerator markers are display text.** The emitter strips `&` for the page; the item `Text`
   setter does the same and the getter returns the displayed text. This is a recorded divergence
   from WinForms, which returns the original `&` text.
7. **The full collection surface is public.** `ToolStripItemCollection` implements `Add`, `Insert`,
   `Remove`, `RemoveAt`, `Clear`, `Count`, `Item`, `IndexOf` and `Contains`, with manifest and
   method-coverage rows.
8. **Event order is item `Click` first, then the root strip's `ItemClicked`** carrying
   `ClickedItem`. A nested item's click is ignored by its ancestor item listener by checking
   whether `e.target` is a descendant `data-vgs-control="Item"`, then the nested item itself
   raises the root strip event.
9. **The coverage gate gets kind-specific fixtures.** Timer is a tray component with a guarded
   one-shot Tick; strips are docked hosts with one hosted item; items are children of their
   canonical host. Strip `Enabled` is a known inherited-`Control.Enabled` collision like
   `ProgressBar.Style`, not a `WebUnavailableMember` refusal.

## Test plan

- Node tier: Timer fake-timer start/stop/interval restart and one `Tick`; strip item collection
  order, document-order clicks, `ClickedItem`, nested menu item click, and menu-item text
  preservation with a nested `<ul>`.
- Catalog coverage: add Timer, the three strips and the four items to `AllKinds`, with fixtures
  and the two collision exceptions.
- Method coverage: Timer `Start`/`Stop` and the nine collection methods.
- Page rendering: `<nav>/<ul>/<li>`, `<menu>`, `<footer>`, roles, item tags and nested submenu
  markup.
- IDE sync: copy the changed library sources to `IDE/lib/js/`.
- Fast subset and the portable integration trio, reporting the known baseline failures rather
  than claiming a full manual click-through.

## Execution record

- **Targeted node/catalog/method/page/control run.** `dotnet test
  VisualGameStudio.Tests/VisualGameStudio.Tests.csproj --no-build --filter
  "FullyQualifiedName~PortableTask39NodeTests|FullyQualifiedName~PortableCatalogCoverageTests|FullyQualifiedName~PortableMethodCoverageTests|FullyQualifiedName~PortablePageRenderingTests|FullyQualifiedName~PortableControlNodeTests"`
  -> 141 passed, 0 failed. The combined total is 2 Task 39 node cases + 71 catalog cases +
  35 method cases + 19 page-rendering cases + 14 control-node cases.
- **Related C# suites.** `dotnet test ... --filter
  "FullyQualifiedName~FormPortableCodegenTests|FullyQualifiedName~FormWebVocabularyTests|FullyQualifiedName~FormRetargetTests|FullyQualifiedName~FormComponentEmissionTests"`
  -> 192 passed, 0 failed. `FormComponentEmissionTests` alone is 19/19; the DOM Timer
  warning is still emitted by `RegionWriter.CheckTargetProperties`, while the portable path
  honors `Timer.Enabled`.
- **IDE drop sync.** `dotnet test ... --filter
  "FullyQualifiedName~IdeDropLibraryMatchesSourceTests"` -> 1 passed, 0 failed. The sync copied
  every changed `BasicLang/lib/js/forms/*.bas` file and `dom-core.bli`; the obsolete
  `ToolStrip.bas`/`ToolStripItem.bas` names are absent.
- **Portable integration trio.** `dotnet test ... --filter
  "FullyQualifiedName~PortableFormEndToEndTests|FullyQualifiedName~BehaviourTwinTests|FullyQualifiedName~PortableMethodCoverageTests"`
  -> 37 passed, 0 failed. The Task 38 `after.selection` selection divergence did not reproduce
  in this run; it remains a historical/flaky baseline note, not a Task 39 regression.
- **Fast subset.** `dotnet test ... --no-build --filter "TestCategory!=Integration"` ->
  10,768 passed, 6 failed, 19 skipped. The six failures are the known baseline, not Task 39:
  `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
  `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`,
  `SearchSnippets_EmptyQuery_ReturnsAll`, and
  `SearchSnippets_WhitespaceQuery_ReturnsAll`.
- **Scope note.** No full Task 36 manual click-through was executed or claimed. Responsive
  layout remains deferred. No commit or push was made.
