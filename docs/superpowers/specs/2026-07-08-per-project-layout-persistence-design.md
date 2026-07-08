# Per-Project Layout & Session Persistence — Design

**Date:** 2026-07-08
**Status:** Approved (design), implementing Phase 1
**Area:** IDE (VisualGameStudio.Shell / .ProjectSystem / .Core)

## Problem

The IDE rebuilds its docking layout from hard-coded defaults on every launch
(`DockFactory.CreateLayout()` — left `0.2`, documents `0.6`, bottom `0.35`).
Nothing is ever saved or read back, so any panel resize/move/close and the set of
open files revert the next time a project is opened. Users want each project to
reopen exactly the way they left it — the Visual Studio Code experience.

## How VS Code does it (reference)

- **Per-workspace state store**, `state.vscdb` (SQLite via `IStorageService`/Memento),
  under `…/Code/User/workspaceStorage/<hash>/` where `<hash>` derives from the folder
  path. **User-local, never inside the project folder** — personal layout stays out of git.
- Stores `workbench.grid.layout` (the whole workbench split-tree + sizes),
  visibility/position flags, and `editorpart.state` (which files are open per group,
  active/pinned tabs, per-editor cursor+scroll view state).
- **Save cadence:** marked dirty on change, flushed on a debounce **and** force-flushed
  on `onWillShutdown`.
- **Unsaved buffers** survive via a separate *Hot Exit* backup mechanism.
- **Reset:** `View: Reset View Locations` / `Developer: Reset Workbench Layout`.

## Decisions

- **Fidelity:** full layout (sizes, drag-drop region changes, tab order, active tab,
  visibility) **plus** reopen the documents that were open (with cursor position).
- **Storage:** user-local, keyed by project path — `~/.vgs/workspaceStorage/<hash>/state.json`
  (+ `workspace.json` recording the real path, for debuggability). VS Code's model.
- **Unsaved files (Hot Exit):** **deferred to Phase 2.** v1 restores layout + open
  *saved* files; unsaved changes still prompt on close as they do today.
- **Serializer:** a small **custom System.Text.Json** walker keyed by dockable `Id`,
  **not** `Dock.Serializer.Newtonsoft`. Rationale: the tool set is closed and known to
  `DockFactory`, `ViewLocator` resolves each panel by its concrete subclass + `ViewModel`
  property, and `Id` uniquely determines both — so we reconstruct the right subclass and
  wire its DI view-model *at construction* from an `Id → Func<IDockable>` map. Avoids a
  Newtonsoft dependency in an otherwise System.Text.Json codebase and avoids fragile
  post-load view-model rehydration. Floating tear-off windows are out of scope because
  `HostWindow` is currently an empty stub (the feature is non-functional today).

## Architecture

New units (each independently testable):

1. **`WorkspaceStateModel`** (Core/Models) — the on-disk DTO:
   `{ int version, double savedAtWidth, savedAtHeight, DockNode? dockLayout,
   List<OpenDocumentState> openDocuments, string? activeDocumentPath }`, where
   `OpenDocumentState = { string path, int caretLine, int caretColumn }` and `DockNode`
   is the serializable layout tree (below).

2. **`DockNode`** (Shell/Dock) — serializable layout tree node:
   `{ DockNodeKind kind, string? id, string? title, Orientation orientation,
   double proportion, string? activeDockableId, List<DockNode> children }`.
   `DockNodeKind ∈ { Root, Proportional, ToolDock, DocumentDock, Splitter, Tool }`.
   Tool leaves carry only `id` (+ `title`); `DocumentDock` is serialized as an **empty
   container** (its documents are restored from `openDocuments`, not the tree).

3. **`IWorkspaceStateStore` / `WorkspaceStateStore`** (Core abstraction + ProjectSystem
   impl) — `hash = Sha256Hex(NormalizePath(projectDir))`; `Load(projectDir)` /
   `Save(projectDir, WorkspaceStateModel)` / `Clear(projectDir)` against
   `~/.vgs/workspaceStorage/<hash>/state.json`. Writes `workspace.json` on first save.
   All IO wrapped so a corrupt/missing file returns `null` (never throws).

4. **`DockLayoutSerializer`** (Shell/Dock) — `DockNode? Capture(IRootDock)` and
   `IRootDock? Rebuild(DockNode, IReadOnlyDictionary<string, Func<IDockable>> toolMap)`.
   Rebuild constructs container docks and, for `Tool` leaves, looks up `id` in `toolMap`
   to build the correct concrete subclass with its VM wired. Unknown ids are skipped.

Wiring points (existing code):

5. **`DockFactory`** — add `GetToolFactoryMap()` (`Id → Func<IDockable>` reusing the held
   VM fields), `SerializeCurrentLayout()`, and `TryApplyLayout(DockNode)` that rebuilds
   via the serializer, runs `InitLayout`, and swaps `_rootDock`/`_documentDock`/dock refs.

6. **`MainWindowViewModel`**
   - `OnProjectOpened` (after `SetWorkspacePath`): load state → if present, apply layout,
     then reopen each existing `openDocuments` path (skip missing), restore caret, activate
     `activeDocumentPath`. Absent/corrupt/incompatible `version` → keep default layout.
   - `OnProjectClosed` (before `SetWorkspacePath(null)`): capture + save current state,
     then reset `Layout` to a fresh `CreateLayout()` default.
   - Save-on-change: subscribe to factory `DockableAdded/Removed/Moved` +
     `ActiveDockableChanged`, debounced (~750 ms) → capture + save. Proportions (splitter
     drags raise no event) are read live at capture time, so the close/exit flush always
     records the latest sizes.
   - `ResetLayoutCommand` — clear stored state + rebuild default (View menu + command palette).

7. **`App.axaml.cs`** — on `IClassicDesktopStyleApplicationLifetime.ShutdownRequested`,
   force a synchronous final capture+save of the current project's state.

8. **`ServiceConfiguration`** — register `IWorkspaceStateStore`.

## Error handling / degradation

- `version` constant hard-invalidates state after incompatible structural changes.
- Any deserialize failure / unknown ids / dropped panel → silent fallback to default
  (logged, never crashes).
- `openDocuments` entries whose files no longer exist are skipped.
- Multiple windows: last-writer-wins (acceptable for v1).
- Applies to folder mode too (`OpenFolder` already sets the workspace path).

## Testing (NUnit)

- **Serializer round-trip:** default layout → tweak proportions, move a tool between
  docks, change active tab → `Capture`→`Rebuild` yields an equal tree; every rebuilt tool
  is the correct concrete subclass with a non-null VM from the map.
- **Store keying:** hash stable for a path; `workspace.json` records the path; files land
  under `~/.vgs/workspaceStorage/<hash>/`; `Clear` removes them.
- **Fallback:** corrupt JSON / bumped `version` → `Load` returns null, IDE keeps default.
- **Lifecycle (VM + fake store):** open restores; close saves then resets to default.
- **Session:** missing-file entry skipped; caret restored for existing file.

## Phasing

- **Phase 1 (this spec):** store + serializer + factory map + save/restore lifecycle +
  fallback + reset command + tests. Shippable.
- **Phase 2 (deferred):** Hot Exit (unsaved-buffer backups); per-project window
  bounds/maximized; multi-editor-group document placement fidelity; floating windows once
  `HostWindow` is implemented.
