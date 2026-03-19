# VS Code Git Integration UX -- Research & Recommendations for VGS IDE

Research date: 2026-03-18

## 1. VS Code Git Features Analysis

### 1.1 Inline Diff Editor

VS Code provides a rich diff viewing experience directly in the editor:

- **Side-by-side diff**: Opens two editor panes showing the original and modified versions with color-coded additions (green), deletions (red), and modifications highlighted inline within changed lines.
- **Inline diff mode**: A toggle (`View > Inline View`) collapses the side-by-side view into a single-pane unified diff with strikethrough for deletions and highlighted insertions.
- **Editable diff panels**: Users can edit files directly from within the diff view, not just read them.
- **Navigation**: "Next Change" (`Ctrl+F5`) and "Previous Change" (`Shift+F5`) buttons jump between differences within a file.
- **Gutter actions**: Small annotations in the gutter can be clicked/expanded to show inline diffs for individual hunks without opening a full diff view. Introduced in VS Code 1.18, modeled after WebStorm.
- **Per-hunk revert**: Each diff hunk in the gutter has a revert button to discard that specific change without discarding the whole file.

### 1.2 Gutter Change Indicators

The editor gutter (left margin, next to line numbers) shows real-time change decorations:

- **Green bar**: Added lines (new code since last commit)
- **Blue/yellow bar**: Modified lines (changed relative to HEAD)
- **Red triangle**: Deleted lines indicator (a small marker where lines were removed)
- **Configurable via `scm.diffDecorations`**: Options are `all` (gutter + overview ruler), `gutter` only, `overview` only, or `none`.
- **Overview ruler**: The minimap/scrollbar on the right also shows colored markers for changes, giving a file-level overview of where modifications exist.
- **Click to expand**: Clicking a gutter indicator opens an inline peek widget showing the original vs. modified content for that hunk, with stage/revert actions.

### 1.3 Source Control View -- Staging

The Source Control panel (`Ctrl+Shift+G`) provides:

- **File-level staging**: Click the `+` icon next to any changed file to stage it; `-` to unstage.
- **Stage All**: A button to stage all changes at once.
- **Hunk/line staging**: From the diff view of a file, users can stage specific hunks or even individual selected lines (not just whole files). This is crucial for creating focused commits.
- **Unstage specific hunks**: Similarly, staged hunks can be selectively unstaged from the staged diff view.
- **Tree vs. List view**: Toggle between flat file list and folder-tree grouping via `More Actions > View & Sort`.
- **Inline file actions**: Each file row shows Stage, Unstage, Discard, and Open File actions on hover.
- **Change count badges**: The Source Control icon in the activity bar shows a badge with the number of pending changes.
- **Multiple SCM providers**: The view supports multiple SCM providers simultaneously (e.g., Git + SVN).

### 1.4 Git Blame Annotations

VS Code does not ship built-in inline blame, but the ecosystem provides it:

- **GitLens** (17M+ downloads): The de facto standard. Shows inline blame at the end of the current line ("Author, time ago -- commit message"), blame annotations in the gutter for all lines, hover cards with full commit details, CodeLens showing recent changes above functions/classes, and a file/line history view.
- **Git Blame** (1.3M+ downloads): Lighter alternative showing blame for the current line in the status bar.
- **Better Git Line Blame**: Minimal inline annotation at the current cursor line.
- **Common UX pattern**: Dimmed/gray text at the end of the current line showing `Author Name, X days ago -- commit message`. Full blame view toggled via command palette or gutter click.

### 1.5 Merge Conflict Resolution

VS Code provides a dedicated 3-way merge editor (since v1.69):

- **3-pane layout**: Incoming changes (left), Current changes (right), Result (bottom). The result pane is editable.
- **Base view**: Optional toggle to show the common ancestor (base) version, making it a true 3-way diff.
- **Checkbox-based resolution**: Each conflict has checkboxes to accept Incoming, Current, or Both.
- **Accept Combination**: An intelligent action that merges both changes when they don't overlap.
- **Inline conflict markers**: In the traditional single-file view, conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`) are highlighted with CodeLens actions: "Accept Current Change", "Accept Incoming Change", "Accept Both Changes", "Compare Changes".
- **Conflict navigation**: Toolbar buttons to jump between conflicts.
- **Complete Merge button**: Stages the resolved file and closes the merge editor.
- **Layout toggle**: Switch between horizontal (side-by-side) and vertical layout.

---

## 2. Current VGS IDE Git Implementation -- Gap Analysis

### 2.1 What VGS IDE Already Has (Backend)

The `GitService` (1184 lines) is comprehensive. It already supports:

| Feature | Backend Method | UI Wired? |
|---------|---------------|-----------|
| Status (staged/unstaged) | `GetStatusAsync()` | Yes |
| Stage/Unstage file | `StageFileAsync()`, `UnstageFileAsync()` | Yes |
| Stage all | `StageAllAsync()` | Yes |
| Commit | `CommitAsync()` | Yes |
| Pull/Push/Fetch | `PullAsync()`, `PushAsync()`, `FetchAsync()` | Yes |
| Branch list/create/delete/rename | Multiple methods | Yes |
| Checkout branch | `CheckoutBranchAsync()` | Yes |
| Discard changes | `DiscardChangesAsync()` | Yes |
| **File diff** | `GetDiffAsync()` | **No UI** |
| **Blame** | `GetBlameAsync()` | **No UI** |
| **File history** | `GetFileHistoryAsync()` | **No UI** |
| **File content at commit** | `GetFileContentAtCommitAsync()` | **No UI** |
| Merge | `MergeBranchAsync()` | Partial |
| Rebase | `RebaseAsync()`, `RebaseContinueAsync()` | Partial |
| Cherry-pick | `CherryPickAsync()` | Partial |
| Revert commit | `RevertCommitAsync()` | Partial |
| Stash | `StashAsync()`, `ApplyStashAsync()` | Partial |
| Tags | `GetTagsAsync()`, `CreateTagAsync()` | Partial |
| Submodules | `GetSubmodulesAsync()` | Partial |
| Remotes | `GetRemotesAsync()` | Partial |
| Clone | `CloneAsync()` | Partial |
| Log | `GetLogAsync()` | Partial |
| Ahead/Behind | `GetAheadBehindAsync()` | Yes (status bar) |

### 2.2 What VGS IDE Is Missing (No Backend or UI)

| VS Code Feature | Backend Exists? | UI Exists? |
|----------------|----------------|------------|
| Inline diff viewer (side-by-side or unified) | `GetDiffAsync()` returns raw diff | No diff viewer control |
| Gutter change indicators | No (needs line-level diff parsing) | No |
| Per-hunk staging | No (only whole-file staging) | No |
| Per-hunk revert | No | No |
| Inline blame annotations | `GetBlameAsync()` returns data | No editor integration |
| 3-way merge editor | No | No |
| Conflict marker detection/actions | `Conflicted` status exists | No inline resolution UI |
| File history timeline | `GetFileHistoryAsync()` returns data | No UI |
| Commit graph visualization | No | No |
| Overview ruler change markers | No | No |

---

## 3. Recommendations -- Prioritized

### Priority 1: Gutter Change Indicators (High Impact, Medium Effort)

**Why**: This is the single most visible Git UX feature in any modern editor. Users see it every time they edit a file. It requires no panel switching or explicit action.

**What to build**:
- Parse `git diff` output to extract line-level change ranges (added, modified, deleted).
- Render colored bars in the editor gutter: green (added), blue (modified), red triangle (deleted).
- Clicking a gutter bar opens an inline peek showing the original line(s) with Revert and Stage Hunk buttons.
- Add a setting `Editor.GitGutterIndicators` with options: `All`, `Gutter`, `Overview`, `None`.

**Backend needed**:
- New method: `GetLineDiffsAsync(string filePath)` returning `List<DiffHunk>` with line ranges and change types.
- Parse unified diff format (`@@ -start,count +start,count @@`) into structured data.
- File watcher integration to re-compute diffs when the file is saved.

**Files to modify**:
- `IGitService.cs` -- add `GetLineDiffsAsync()` and `DiffHunk` model
- `GitService.cs` -- implement diff parsing
- `CodeEditorControl.axaml.cs` -- add gutter rendering layer
- New: `GitGutterMargin.cs` -- custom Avalonia margin control for the editor

### Priority 2: Inline Diff Viewer (High Impact, High Effort)

**Why**: Users currently have no way to see what changed in a file. The backend already returns raw diff text via `GetDiffAsync()`, but there is no visual presentation.

**What to build**:
- A diff viewer control with two modes: side-by-side and unified/inline.
- Syntax highlighting in both panes (reuse existing TextMate integration).
- Line-level and word-level diff highlighting.
- Navigation buttons: Next/Previous Change.
- Editable right pane (working copy).
- Open from Source Control panel by clicking a changed file.

**Implementation approach**:
- Use AvaloniaEdit's dual-editor layout: two `TextEditor` instances side by side with synchronized scrolling.
- Parse unified diff to map line numbers between original and modified versions.
- Use `GetFileContentAtCommitAsync()` (already implemented) to get the original file content for the left pane.
- Apply background decorations to highlight added/removed/modified lines.

**Files to modify**:
- New: `DiffViewerControl.axaml` / `.cs` -- Avalonia UserControl with two editors
- New: `DiffParser.cs` -- Parse unified diff into structured line mappings
- `GitChangesViewModel.cs` -- add `OpenDiffCommand` that opens the diff viewer for a selected file

### Priority 3: Hunk/Line-Level Staging (High Impact, Medium Effort)

**Why**: This is the feature that separates basic Git GUIs from professional ones. Developers need to create focused commits by staging only relevant hunks.

**What to build**:
- In the diff viewer (Priority 2), add Stage Hunk and Unstage Hunk buttons next to each `@@` hunk header.
- Support selecting specific lines and staging just those lines.
- Show staged vs. unstaged state visually (e.g., green background for staged hunks).

**Backend needed**:
- New method: `StageHunkAsync(string filePath, string hunkPatch)` -- uses `git apply --cached` with a partial patch.
- New method: `UnstageHunkAsync(string filePath, string hunkPatch)` -- uses `git apply --cached --reverse`.
- New method: `StageLinesAsync(string filePath, int startLine, int endLine)` -- constructs a partial patch for the selected line range.

**Files to modify**:
- `IGitService.cs` -- add hunk staging methods
- `GitService.cs` -- implement `git apply --cached` integration
- `DiffViewerControl` (from Priority 2) -- add hunk action buttons

### Priority 4: Inline Blame Annotations (Medium Impact, Low Effort)

**Why**: The backend `GetBlameAsync()` already returns full blame data with commit hash, author, date, and line content. Only the UI integration is missing.

**What to build**:
- Show dimmed annotation at the end of the current line: `AuthorName, 3 days ago -- commit message`.
- Toggle via menu: `View > Toggle Git Blame`.
- Hover tooltip showing full commit details (hash, author, date, full message, changed files).
- Optional: full-file blame gutter showing author colors for all lines.

**Implementation approach**:
- Use AvaloniaEdit's `IBackgroundRenderer` or `IVisualLineTransformingLineTransformer` to render inline text decorations.
- Cache blame data per file; invalidate on save or Git status change.
- Lazy-load: only fetch blame for the visible viewport initially.

**Files to modify**:
- New: `BlameAnnotationService.cs` -- manages blame data fetching and caching
- New: `BlameGutterMargin.cs` -- renders blame info in the editor gutter
- `CodeEditorControl.axaml.cs` -- wire up blame rendering toggle
- `MainWindowViewModel.cs` -- add View menu command

### Priority 5: 3-Way Merge Editor (High Impact, Very High Effort)

**Why**: Merge conflict resolution is currently manual (edit conflict markers by hand). A visual merge tool is a major productivity feature.

**What to build**:
- 3-pane layout: Incoming (left), Current (right), Result (bottom, editable).
- Optional base view toggle.
- Per-conflict checkboxes: Accept Current, Accept Incoming, Accept Both.
- Conflict navigation buttons.
- "Complete Merge" button that stages the file and marks it resolved (`git add`).

**Backend needed**:
- New method: `GetMergeConflictDataAsync(string filePath)` -- parses conflict markers to extract Incoming, Current, and Base sections.
- New method: `MarkResolvedAsync(string filePath)` -- runs `git add` to mark the file as resolved.
- New method: `GetBaseContentAsync(string filePath)` -- runs `git show :1:<path>` to get the base version.

**Implementation approach**:
- Three synchronized AvaloniaEdit editors with custom conflict highlighting.
- Parse `<<<<<<<`, `=======`, `>>>>>>>` markers to identify conflict regions.
- CodeLens-style action buttons above each conflict region.

**Files to modify**:
- New: `MergeEditorControl.axaml` / `.cs` -- the 3-pane merge editor
- New: `MergeConflictParser.cs` -- parses conflict markers
- `IGitService.cs` -- add merge resolution methods
- `GitService.cs` -- implement conflict-related git commands
- `GitChangesViewModel.cs` -- detect conflicted files and offer "Open in Merge Editor"

### Priority 6: File History Timeline (Medium Impact, Medium Effort)

**Why**: `GetFileHistoryAsync()` and `GetFileContentAtCommitAsync()` already exist but have no UI. Users want to see when and why a file changed.

**What to build**:
- A panel showing the commit history for the currently open file.
- Each entry shows: short hash, message, author, relative date.
- Click a commit to open a diff between that commit and the previous one (or working copy).
- Right-click context menu: "Compare with Working Copy", "Restore This Version", "Copy Commit Hash".

**Files to modify**:
- New: `FileHistoryViewModel.cs` -- ViewModel for the file history panel
- New: `FileHistoryPanel.axaml` -- UI for the timeline
- Wire into the editor's context menu: "Show File History"

### Priority 7: Commit Graph Visualization (Low Impact, High Effort)

**Why**: Nice-to-have for repository exploration. Lower priority because most users rely on external tools (GitKraken, git log --graph) for this.

**What to build**:
- A graphical commit history with branch/merge lines.
- Show commit nodes with branch labels, tags, and HEAD marker.
- Click a commit to see its diff.

**Backend needed**:
- New method: `GetGraphLogAsync()` -- runs `git log --graph --format=...` and parses the graph characters.

---

## 4. Implementation Roadmap

| Phase | Features | Estimated Effort | Dependencies |
|-------|----------|-----------------|--------------|
| **Phase 1** | Gutter change indicators | 2-3 days | AvaloniaEdit gutter API |
| **Phase 2** | Inline diff viewer (side-by-side + unified) | 3-5 days | Phase 1 (diff parsing) |
| **Phase 3** | Hunk/line staging | 2-3 days | Phase 2 (diff viewer) |
| **Phase 4** | Inline blame annotations | 1-2 days | AvaloniaEdit decorations |
| **Phase 5** | File history panel | 2-3 days | Phase 2 (diff viewer for comparison) |
| **Phase 6** | 3-way merge editor | 5-7 days | Phase 2 (diff viewer foundation) |
| **Phase 7** | Commit graph | 3-5 days | Standalone (custom drawing) |

Total estimated effort: 18-28 days for full VS Code Git parity.

---

## 5. Quick Wins (Can Ship Immediately)

These require minimal code and use existing backend methods:

1. **"Show Diff" in Source Control panel**: When a user clicks a changed file, call `GetDiffAsync()` and display the raw unified diff in an output panel. Not ideal but immediately useful. (~1 hour)

2. **Blame in status bar**: Show `GetBlameAsync()` result for the current line in the IDE status bar (author + date). (~2 hours)

3. **File history in context menu**: Add "Show Git History" to the editor's right-click menu, opening a simple list dialog of commits from `GetFileHistoryAsync()`. (~2 hours)

4. **Stash UI in Source Control panel**: Add Stash/Pop buttons. Backend methods `StashAsync()`, `GetStashesAsync()`, `ApplyStashAsync()` already exist. (~3 hours)

---

## Sources

- [Source Control in VS Code](https://code.visualstudio.com/docs/sourcecontrol/overview)
- [Staging and committing changes](https://code.visualstudio.com/docs/sourcecontrol/staging-commits)
- [Resolve merge conflicts in VS Code](https://code.visualstudio.com/docs/sourcecontrol/merge-conflicts)
- [New in VS Code: Inline Change Review (Medium)](https://medium.com/fhinkel/new-in-vs-code-inline-change-review-d43df04ea264)
- [3-Column Merge Editor in VS Code (JavaScript in Plain English)](https://javascript.plainenglish.io/finally-released-3-column-merge-editor-in-vs-code-8490ef694b3a)
- [GitLens Extension](https://marketplace.visualstudio.com/items?itemName=eamodio.gitlens)
- [Git Blame Extension](https://marketplace.visualstudio.com/items?itemName=waderyan.gitblame)
- [Stage Lines of Code in Visual Studio (Microsoft Learn)](https://learn.microsoft.com/en-us/visualstudio/version-control/git-line-staging?view=vs-2022)
- [VS Code Git gutter decorations issue #41642](https://github.com/microsoft/vscode/issues/41642)
- [Explore UX for three-way merge issue #146091](https://github.com/microsoft/vscode/issues/146091)
