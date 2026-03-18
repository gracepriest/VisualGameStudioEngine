# IDE Feature Parity: VGS IDE vs VS Code

**Date:** 2026-03-18
**VGS Version:** Based on current master branch (commit 0a0343f)
**VS Code Baseline:** VS Code 1.96+ (latest stable as of early 2026)

---

## Overall Score: 72/100

---

### Category Breakdown

| Category | Max | VGS Score | % | Notes |
|----------|-----|-----------|---|-------|
| Code Editor | 15 | 13 | 87% | Strong AvaloniaEdit foundation, multi-cursor, minimap |
| IntelliSense / LSP | 15 | 12 | 80% | 30+ LSP handlers, full protocol coverage |
| Debugging | 15 | 11 | 73% | DAP client, breakpoints, variables; no hot reload |
| Project System | 10 | 8 | 80% | Solutions, templates, build configs |
| Build System | 10 | 7 | 70% | Multi-backend compiler; no task runner |
| UI/UX | 10 | 8 | 80% | Dock panels, welcome page, dialogs |
| Source Control | 5 | 4 | 80% | Full git CLI integration with views |
| Terminal | 5 | 3 | 60% | Basic shell sessions; no pty/xterm |
| Extensions | 5 | 2 | 40% | Framework exists; no real ecosystem |
| Accessibility | 5 | 1 | 20% | No screen reader, high contrast partial |
| Performance | 5 | 3 | 60% | .NET 8 + Avalonia; no web worker isolation |

---

### Detailed Feature Audit

#### 1. Code Editor (13/15)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Syntax highlighting | Yes | Yes | DONE | `HighlightingLoader.cs` registers BasicLang xshd definition |
| Code folding | Yes | Yes | DONE | `BasicLangFoldingStrategy.cs`, FoldingManager installed |
| Bracket matching | Yes | Yes | DONE | `BracketHighlighter.cs` as LineTransformer |
| Auto-close brackets | Yes | Yes | DONE | `AutoClosePairs` dict in `CodeEditorControl.axaml.cs` |
| Surround with brackets | Yes | Yes | DONE | `SurroundingPairs` dict wraps selected text |
| Multi-cursor editing | Yes | Yes | DONE | `MultiCursorManager.cs` + `MultiCursorRenderer.cs` + `MultiCursorInputHandler.cs` |
| Add cursor above/below | Yes | Yes | DONE | `AddCursorAbove()`, `AddCursorBelow()` |
| Select next occurrence (Ctrl+D) | Yes | Yes | DONE | `AddNextOccurrence()` in MultiCursorManager |
| Select all occurrences | Yes | Yes | DONE | `SelectAllOccurrences()` |
| Minimap | Yes | Yes | DONE | `MinimapControl.axaml.cs` with viewport indicator |
| Breadcrumb navigation | Yes | Yes | DONE | `BreadcrumbControl.axaml.cs` |
| Indentation guides | Yes | Yes | DONE | `IndentationGuideRenderer.cs` |
| Line numbers | Yes | Yes | DONE | `ShowLineNumbers` styled property |
| Word wrap | Yes | Yes | DONE | `WordWrap` styled property |
| Find and replace (inline) | Yes | Yes | DONE | `InlineFindReplaceControl.axaml.cs` |
| Find in files | Yes | Yes | DONE | `FindReplaceService.FindInFilesAsync()`, `FindInFilesView.axaml.cs` |
| Replace in files | Yes | Yes | DONE | `FindReplaceService.ReplaceInFilesAsync()` |
| Regex find/replace | Yes | Yes | DONE | `Options.UseRegex` in FindReplaceService |
| Preserve case replace | Yes | Yes | DONE | `ProcessReplacement()` handles case patterns |
| Go to line | Yes | Yes | DONE | `GoToLineDialog.axaml.cs`, Ctrl+G keybinding |
| Go to symbol | Yes | Yes | DONE | `GoToSymbolDialogViewModel.cs`, Ctrl+Shift+O |
| Quick open (Ctrl+P) | Yes | Yes | DONE | `QuickOpenViewModel.cs` with fuzzy search |
| Snippet insertion | Yes | Yes | DONE | `SnippetProvider.cs`, `SnippetCompletionData.cs` |
| Smart indentation | Yes | Yes | DONE | `BasicLangIndentationStrategy.cs`, `SmartIndentHandler.cs` |
| Move lines up/down | Yes | Yes | DONE | Keybindings Alt+Up/Down registered |
| Copy lines up/down | Yes | Yes | DONE | Keybindings Shift+Alt+Up/Down registered |
| Delete line | Yes | Yes | DONE | Ctrl+Shift+K keybinding |
| Comment/uncomment | Yes | Yes | DONE | Ctrl+/ keybinding registered |
| Undo/redo across tab switches | Yes | Yes | DONE | `TextDocument` preserved via `SetSharedDocument()` |
| Zoom in/out | Yes | Yes | DONE | Ctrl+=/- keybindings, `EditorFontSize` property |
| Search highlighting | Yes | Yes | DONE | `SearchHighlightRenderer.cs` |
| Linked editing ranges | Yes | Yes | DONE | `LinkedEditingRangeHandler.cs` in LSP |
| Column/box selection | Yes | Partial | GAP | No dedicated box selection mode |
| Diff editor | Yes | Partial | GAP | `DiffViewerViewModel.cs` exists but basic |

**Gap details:**
- Box/column selection: Multi-cursor exists but no dedicated rectangular selection mode
- Diff editor: ViewModel exists but no side-by-side diff rendering like VS Code's built-in diff

---

#### 2. IntelliSense / LSP (12/15)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Auto-completions | Yes | Yes | DONE | `CompletionHandler.cs`, `CompletionService.cs` |
| Hover information | Yes | Yes | DONE | `HoverHandler.cs` |
| Signature help | Yes | Yes | DONE | `SignatureHelpHandler.cs` |
| Go to definition (F12) | Yes | Yes | DONE | `DefinitionHandler.cs` |
| Go to implementation | Yes | Yes | DONE | `ImplementationHandler.cs` |
| Find all references | Yes | Yes | DONE | `ReferencesHandler.cs` |
| Peek definition (Alt+F12) | Yes | Yes | DONE | `PeekDefinitionViewModel.cs`, `PeekDefinitionControl.axaml.cs` |
| Rename symbol (F2) | Yes | Yes | DONE | `RenameHandler.cs` + `PrepareRenameHandler` |
| Code actions / Quick fix | Yes | Yes | DONE | `CodeActionHandler.cs`, Ctrl+. keybinding |
| Document formatting | Yes | Yes | DONE | `FormattingHandler.cs` + `RangeFormattingHandler` |
| On-type formatting | Yes | Yes | DONE | `OnTypeFormattingHandler.cs` |
| Semantic tokens | Yes | Yes | DONE | `SemanticTokensHandler.cs` |
| Document symbols | Yes | Yes | DONE | `DocumentSymbolHandler.cs` |
| Workspace symbols | Yes | Yes | DONE | `WorkspaceSymbolHandler.cs` |
| Code lens | Yes | Yes | DONE | `CodeLensHandler.cs`, `CodeLensRenderer.cs` with click regions |
| Inlay hints | Yes | Yes | DONE | `InlayHintsHandler.cs`, `InlayHintRenderer.cs` |
| Call hierarchy | Yes | Yes | DONE | `CallHierarchyHandler.cs` (Prepare/Incoming/Outgoing) |
| Type hierarchy | Yes | Yes | DONE | `TypeHierarchyHandler.cs` (Prepare/Supertypes/Subtypes) |
| Document links | Yes | Yes | DONE | `DocumentLinkHandler.cs` |
| Document highlights | Yes | Yes | DONE | `DocumentHighlightHandler.cs` |
| Selection ranges | Yes | Yes | DONE | `SelectionRangeHandler.cs` |
| Folding ranges | Yes | Yes | DONE | `FoldingRangeHandler.cs` |
| Execute command | Yes | Yes | DONE | `ExecuteCommandHandler.cs` |
| Diagnostic tags | Yes | Yes | DONE | `DiagnosticsService.cs` |
| Multi-language LSP | Yes | Yes | DONE | `LspClientManager.cs` supports 12 languages |
| TextMate grammars | Yes | Yes | DONE | `TextMateService.cs` for non-BasicLang files |
| Inline values (debug) | Yes | Yes | DONE | `InlineDebugValueRenderer.cs` |
| Copilot / AI assist | Yes | No | GAP | No AI completion integration |
| Multi-root workspace LSP | Yes | No | GAP | `WorkspaceManager.cs` single-workspace only |
| Notebook support | Yes | No | GAP | No notebook/cell editing |

**Gap details:**
- No AI-assisted completions (Copilot, Codeium, etc.)
- WorkspaceManager is single-root; VS Code supports multi-root workspaces with per-folder LSP
- No Jupyter/notebook support

---

#### 3. Debugging (11/15)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Start/stop debugging | Yes | Yes | DONE | `DebugService.StartDebuggingAsync()` |
| Run without debugging | Yes | Yes | DONE | `DebugService.StartWithoutDebuggingAsync()` |
| Line breakpoints | Yes | Yes | DONE | `SetBreakpointsAsync()`, `BreakpointMargin.cs` |
| Conditional breakpoints | Yes | Yes | DONE | `SourceBreakpoint.Condition` sent to DAP |
| Hit count breakpoints | Yes | Yes | DONE | `SourceBreakpoint.HitCondition` sent to DAP |
| Log points (tracepoints) | Yes | Yes | DONE | `SourceBreakpoint.LogMessage` sent to DAP |
| Function breakpoints | Yes | Yes | DONE | `SetFunctionBreakpointsAsync()`, `FunctionBreakpointDialog.axaml.cs` |
| Data breakpoints | Yes | Yes | DONE | `SetDataBreakpointsAsync()`, `GetDataBreakpointInfoAsync()` |
| Exception breakpoints | Yes | Yes | DONE | `SetExceptionBreakpointsAsync()`, `ExceptionSettingsDialog.axaml.cs` |
| Step over/into/out | Yes | Yes | DONE | `StepOverAsync()`, `StepIntoAsync()`, `StepOutAsync()` |
| Continue/pause | Yes | Yes | DONE | `ContinueAsync()`, `PauseAsync()` |
| Restart debugging | Yes | Yes | DONE | `RestartAsync()` saves config and restarts |
| Call stack view | Yes | Yes | DONE | `GetStackTraceAsync()`, `CallStackView.axaml.cs` |
| Variables view | Yes | Yes | DONE | `GetVariablesAsync()`, `VariablesView.axaml.cs` |
| Watch expressions | Yes | Yes | DONE | `EvaluateAsync()`, `WatchView.axaml.cs` |
| Scopes (local/global) | Yes | Yes | DONE | `GetScopesAsync()` returns scope list |
| Run to cursor | Yes | Yes | DONE | `RunToCursorAsync()` with temp breakpoint + restore |
| Set next statement (goto) | Yes | Yes | DONE | `SetNextStatementAsync()` using DAP gotoTargets |
| Immediate window | Yes | Yes | DONE | `ImmediateWindowViewModel.cs`, `ImmediateWindowView.axaml.cs` |
| Inline debug values | Yes | Yes | DONE | `InlineDebugValueRenderer.cs` |
| Breakpoints view | Yes | Yes | DONE | `BreakpointsView.axaml.cs` |
| Debug console (stdin) | Yes | Yes | DONE | `SendInputAsync()` writes to target stdin |
| DAP protocol | Yes | Yes | DONE | Full Content-Length framed JSON protocol |
| Generic DAP client | Yes | Yes | DONE | `DapClient.cs` in DAP/ for any debug adapter |
| Launch configurations | Yes | Partial | DONE | `LaunchConfigurationDialogViewModel.cs` |
| Multi-target debug | Yes | No | GAP | Single debug session only |
| Hot reload | Yes | No | GAP | No edit-and-continue |
| Debug visualizers | Yes | No | GAP | No custom variable visualizers |
| Remote debugging | Yes | No | GAP | Local only |

**Gap details:**
- Single debug session at a time (VS Code supports compound launch configs)
- No hot reload / edit-and-continue
- No custom debug visualizers (e.g., image preview for textures)
- No remote/SSH debugging

---

#### 4. Project System (8/10)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Solution explorer | Yes | Yes | DONE | `SolutionExplorerView.axaml.cs`, `SolutionExplorerViewModel.cs` |
| Create project from template | Yes | Yes | DONE | `ProjectTemplateService.cs`, `CreateProjectView.axaml.cs` |
| Project templates (Console, Lib, WinForms, WPF) | Yes | Yes | DONE | `ProjectTemplateKind` enum, 4+ templates |
| Open/close project | Yes | Yes | DONE | `ProjectService.cs` with serialization |
| Solution with multiple projects | Yes | Yes | DONE | `BasicLangSolution.cs`, `SolutionSerializer.cs` |
| Build configurations (Debug/Release) | Yes | Yes | DONE | `BuildConfigurationDialogViewModel.cs` |
| Recent projects | Yes | Yes | DONE | `RecentProjectsService.cs` |
| New project dialog | Yes | Yes | DONE | `NewProjectDialog.axaml.cs` |
| File add/remove/rename | Yes | Yes | DONE | `ProjectService` manages `ProjectItem` list |
| Document outline | Yes | Yes | DONE | `DocumentOutlineView.axaml.cs` |
| Multi-root workspaces | Yes | No | GAP | Single solution/project only |
| Task auto-detection | Yes | No | GAP | No tasks.json equivalent |

---

#### 5. Build System (7/10)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Build project | Yes | Yes | DONE | `BuildService.BuildProjectAsync()` |
| Build solution | Yes | Yes | DONE | `BuildService.BuildSolutionAsync()` |
| Clean build | Yes | Yes | DONE | `BuildService.CleanAsync()` |
| Rebuild | Yes | Yes | DONE | `BuildService.RebuildAsync()` |
| Build progress | Yes | Yes | DONE | `BuildProgress` event with percentage |
| Error list | Yes | Yes | DONE | `ErrorListViewModel.cs`, `ErrorListView.axaml.cs` |
| Build cancel | Yes | Yes | DONE | CancellationTokenSource pattern |
| Multiple backends (C#, LLVM, MSIL, C++) | N/A | Yes | DONE | Compiler supports 4 backends |
| Task runner (npm, gulp, make) | Yes | No | GAP | No generic task runner |
| Problem matchers | Yes | No | GAP | No regex-based error parsing for external tools |
| Pre/post build events | Yes | No | GAP | No build event hooks |

---

#### 6. UI/UX (8/10)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Tabbed documents | Yes | Yes | DONE | Dock-based document tabs via DockFactory |
| Dockable panels | Yes | Yes | DONE | `DockFactory.cs` manages layout |
| Welcome page | Yes | Yes | DONE | `WelcomeDocumentView.axaml.cs` |
| Command palette | Yes | Yes | DONE | `view.commandPalette` keybinding Ctrl+Shift+P |
| Settings UI | Yes | Yes | DONE | `SettingsDialog.axaml.cs`, `SettingsViewModel.cs` |
| Settings JSON editing | Yes | Yes | DONE | `SettingsService` reads/writes JSON files |
| Settings scopes (User/Workspace/Folder) | Yes | Yes | DONE | 4 scopes in `SettingsService` |
| Customizable keybindings | Yes | Yes | DONE | `KeybindingService.cs` with JSON persistence |
| When-clause contexts | Yes | Yes | DONE | `EvaluateContext()` supports &&, ||, !, == |
| Theme support | Yes | Partial | GAP | `SettingsKeys.Theme` with dark/light/high-contrast; no custom themes |
| Status bar | Yes | Yes | DONE | Branch info, line/col display |
| Activity bar / sidebar | Yes | Yes | DONE | Explorer, Search, Git, Debug panels |
| Notifications | Yes | Partial | GAP | Output window but no toast notifications |
| Zen mode / distraction-free | Yes | No | GAP | No full-screen zen mode |

---

#### 7. Source Control (4/5)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Git status | Yes | Yes | DONE | `GetStatusAsync()` parses porcelain output |
| Stage/unstage files | Yes | Yes | DONE | `StageFileAsync()`, `UnstageFileAsync()`, `StageAllAsync()` |
| Commit | Yes | Yes | DONE | `CommitAsync()` with message |
| Push/pull/fetch | Yes | Yes | DONE | `PushAsync()`, `PullAsync()`, `FetchAsync()` |
| Branch management | Yes | Yes | DONE | `CreateBranchAsync()`, `CheckoutBranchAsync()`, `DeleteBranchAsync()`, `RenameBranchAsync()` |
| Merge | Yes | Yes | DONE | `MergeBranchAsync()` with conflict detection |
| Rebase | Yes | Yes | DONE | `RebaseAsync()`, `RebaseAbortAsync()`, `RebaseContinueAsync()` |
| Cherry-pick | Yes | Yes | DONE | `CherryPickAsync()` |
| Revert | Yes | Yes | DONE | `RevertCommitAsync()` |
| Stash | Yes | Yes | DONE | `StashAsync()`, `ApplyStashAsync()`, `DropStashAsync()`, `GetStashesAsync()` |
| Git blame | Yes | Yes | DONE | `GetBlameAsync()` with porcelain parsing, `GitBlameView.axaml.cs` |
| File history | Yes | Yes | DONE | `GetFileHistoryAsync()` |
| Diff view | Yes | Yes | DONE | `GetDiffAsync()`, `DiffViewerViewModel.cs` |
| Tags | Yes | Yes | DONE | `GetTagsAsync()`, `CreateTagAsync()`, `DeleteTagAsync()`, `PushTagsAsync()` |
| Submodules | Yes | Yes | DONE | `GetSubmodulesAsync()`, `UpdateSubmodulesAsync()` |
| Remotes | Yes | Yes | DONE | `GetRemotesAsync()`, `AddRemoteAsync()`, `RemoveRemoteAsync()` |
| Clone | Yes | Yes | DONE | `CloneAsync()` |
| Reset | Yes | Yes | DONE | `ResetAsync()` with Soft/Mixed/Hard modes |
| Clean | Yes | Yes | DONE | `CleanAsync()` |
| File content at commit | Yes | Yes | DONE | `GetFileContentAtCommitAsync()` |
| Changes view | Yes | Yes | DONE | `GitChangesView.axaml.cs`, `GitChangesViewModel.cs` |
| Branches view | Yes | Yes | DONE | `GitBranchesView.axaml.cs`, `GitBranchesViewModel.cs` |
| Stash view | Yes | Yes | DONE | `GitStashView.axaml.cs`, `GitStashViewModel.cs` |
| Git graph | Yes | No | GAP | No commit graph visualization |
| Merge conflict editor | Yes | No | GAP | Detects conflicts but no inline resolution UI |

---

#### 8. Terminal (3/5)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Integrated terminal | Yes | Yes | DONE | `TerminalService.cs`, `TerminalView.axaml.cs` |
| Multiple terminal sessions | Yes | Yes | DONE | `ConcurrentDictionary<string, TerminalSession>` |
| Terminal naming | Yes | Yes | DONE | `TerminalOptions.Name` |
| Shell detection (cmd/bash/pwsh) | Yes | Yes | DONE | `GetDefaultShell()` per platform |
| Execute command | Yes | Yes | DONE | `ExecuteCommandAsync()` with exit code |
| Background execution | Yes | Yes | DONE | `ExecuteInBackground()` |
| Command history | Yes | Yes | DONE | `GetHistory()` up to 10000 entries |
| Clear terminal | Yes | Yes | DONE | `Clear()` |
| Environment variables | Yes | Yes | DONE | `TerminalOptions.EnvironmentVariables` |
| Pseudo-terminal (pty) | Yes | No | GAP | Uses stdin/stdout redirection, not a real pty |
| ANSI color support | Yes | No | GAP | Raw text output, no terminal emulation |
| Terminal split | Yes | No | GAP | Multiple sessions but no split view |
| Terminal profiles | Yes | No | GAP | No saved shell profiles |
| Shell integration (cwd tracking) | Yes | No | GAP | No shell integration API |

**Gap details:**
- The terminal uses `Process.RedirectStandardInput/Output` rather than a pseudo-terminal. This means no ANSI colors, no cursor movement, no full-screen terminal apps (vim, top, etc.).
- This is the single biggest UX gap compared to VS Code's xterm.js-based terminal.

---

#### 9. Extensions (2/5)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Extension install/uninstall | Yes | Yes | DONE | `ExtensionService.InstallFromFileAsync()`, `.vsix`/`.zip` |
| Extension enable/disable | Yes | Yes | DONE | `EnableAsync()`, `DisableAsync()` |
| Extension activate/deactivate | Yes | Yes | DONE | `ActivateAsync()`, `DeactivateAsync()` |
| Extension marketplace UI | Yes | Yes | DONE | `MarketplaceService.cs` with search, categories |
| VS Code extension compatibility | Yes | Partial | DONE | Reads `package.json` manifest format |
| Install from URL | Yes | Yes | DONE | `InstallFromUrlAsync()` |
| Check for updates | Yes | Stub | GAP | `CheckForUpdatesAsync()` returns empty list |
| Extension API | Yes | No | GAP | No runtime extension API; `LoadContributions()` is a stub |
| Extension host / sandbox | Yes | No | GAP | No isolated extension host process |
| Marketplace (live) | Yes | No | GAP | `marketplace.visualgamestudio.com` does not exist; returns mock data |
| Theme extensions | Yes | No | GAP | No theme loading from extensions |
| Language extension packs | Yes | No | GAP | No mechanism to add new languages via extension |

**Gap details:**
- The extension framework has the right shape (install, enable, activate, marketplace) but lacks a real extension runtime API. Extensions cannot contribute commands, views, or language support at runtime.
- The marketplace returns mock/fallback data since the server does not exist.

---

#### 10. Accessibility (1/5)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| High contrast theme | Yes | Partial | GAP | `high-contrast` in theme enum but no full implementation |
| Screen reader support | Yes | No | GAP | No ARIA-equivalent in Avalonia |
| Keyboard navigation | Yes | Yes | DONE | Comprehensive keybindings in KeybindingService |
| Focus indicators | Yes | Partial | GAP | Standard Avalonia focus but not enhanced |
| Accessible names/roles | Yes | No | GAP | No AutomationProperties set |
| Reduced motion | Yes | No | GAP | No animation preferences |
| Font ligature support | Yes | Yes | DONE | Cascadia Code is default font |

**Gap details:**
- Avalonia has basic accessibility support but VGS has not invested in it. No `AutomationProperties`, no screen reader testing, no accessible tree structure.

---

#### 11. Performance (3/5)

| Feature | VS Code | VGS | Status | Evidence |
|---------|---------|-----|--------|----------|
| Large file handling | Yes | Partial | GAP | AvaloniaEdit handles moderate files; no virtual scrolling for 100K+ lines |
| Incremental parsing | Yes | Partial | DONE | LSP re-parses on change; debounced folding (500ms timer) |
| Background processing | Yes | Yes | DONE | Async throughout; LSP and DAP in separate processes |
| Startup time | Good | Good | DONE | .NET 8 AOT-ready, Avalonia is fast |
| Memory efficiency | Good | Okay | GAP | No worker process isolation; all in one process |
| File watcher | Yes | No | GAP | No file system watcher for external changes |
| Lazy loading | Yes | Partial | GAP | All panels created at startup |

---

### Code Analysis & Refactoring (Bonus -- not scored, but notable)

VGS has features that go **beyond** what VS Code provides out of the box (without extensions):

| Feature | VS Code (built-in) | VGS | Evidence |
|---------|-------------------|-----|----------|
| Code metrics (LOC, complexity, nesting) | No | Yes | `CodeMetricsService.cs` |
| Cyclomatic + cognitive complexity | No | Yes | `CalculateCyclomaticComplexity()`, `CalculateCognitiveComplexity()` |
| Code smell detection | No | Yes | `CodeAnalysisService.GetCodeSmells()` - long functions, deep nesting, magic numbers, empty catch |
| Security analysis | No | Yes | `GetSecurityIssues()` - hardcoded creds, command injection, path traversal |
| Duplicate code detection | No | Yes | `GetDuplicates()` |
| Unused code detection | No | Yes | `GetUnusedCode()` |
| Refactoring suggestions | No | Yes | `GetRefactoringSuggestions()` |
| Maintainability index | No | Yes | `CodeMetricsService.cs` |
| Extract method dialog | No (ext) | Yes | `ExtractMethodDialog.axaml.cs` |
| Extract interface dialog | No (ext) | Yes | `ExtractInterfaceDialog.axaml.cs` |
| Change signature dialog | No (ext) | Yes | `ChangeSignatureDialog.axaml.cs` |
| Encapsulate field dialog | No (ext) | Yes | `EncapsulateFieldDialog.axaml.cs` |
| Pull members up/down | No (ext) | Yes | `PullMembersUpDialog.axaml.cs`, `PushMembersDownDialogViewModel.cs` |
| Inline method/variable/constant | No (ext) | Yes | `InlineMethodDialog.axaml.cs`, `InlineVariableDialogViewModel.cs`, `InlineConstantDialogViewModel.cs` |
| Introduce variable | No (ext) | Yes | `IntroduceVariableDialog.axaml.cs` |
| Convert to interface | No (ext) | Yes | `ConvertToInterfaceDialog.axaml.cs` |
| Override method dialog | No (ext) | Yes | `OverrideMethodDialog.axaml.cs` |
| Implement interface dialog | No (ext) | Yes | `ImplementInterfaceDialog.axaml.cs` |
| Generate constructor | No (ext) | Yes | `GenerateConstructorDialog.axaml.cs` |
| Named/positional argument conversion | No | Yes | `ConvertToNamedArgumentsDialog.axaml.cs` |
| Safe delete | No | Yes | `SafeDeleteDialogViewModel.cs` |
| Move type to file | No | Yes | `MoveTypeToFileDialogViewModel.cs` |
| Bookmarks | No (ext) | Yes | `BookmarkService.cs`, `BookmarksView.axaml.cs`, `BookmarkMargin.cs` |
| Task list (TODO tracking) | No (ext) | Yes | `TaskListService.cs` |

---

### Top 20 Missing Features (Priority Order)

| # | Feature | Category | Impact | Effort |
|---|---------|----------|--------|--------|
| 1 | **Pseudo-terminal (pty) emulation** | Terminal | High -- current terminal cannot run interactive apps | Large |
| 2 | **File system watcher** | Performance | High -- external edits are not detected | Medium |
| 3 | **Git merge conflict editor** | Source Control | High -- conflicts require manual file editing | Large |
| 4 | **Multi-root workspaces** | Project System | Medium -- limits multi-project workflows | Large |
| 5 | **Extension runtime API** | Extensions | High -- extensions cannot contribute functionality | Very Large |
| 6 | **Hot reload / edit-and-continue** | Debugging | Medium -- requires stop/restart for code changes | Very Large |
| 7 | **Screen reader accessibility** | Accessibility | High for affected users | Large |
| 8 | **Custom color themes** | UI/UX | Medium -- only 3 built-in themes | Medium |
| 9 | **Git commit graph** | Source Control | Low -- visual history is useful but not essential | Medium |
| 10 | **Task runner integration** | Build | Medium -- no tasks.json or npm script detection | Medium |
| 11 | **Compound debug sessions** | Debugging | Medium -- cannot debug frontend+backend together | Medium |
| 12 | **ANSI color in terminal** | Terminal | Medium -- no colored output | Medium |
| 13 | **Toast/notification system** | UI/UX | Low -- output window suffices | Small |
| 14 | **Zen mode** | UI/UX | Low -- distraction-free editing | Small |
| 15 | **Side-by-side diff editor** | Editor | Medium -- current diff is basic | Medium |
| 16 | **Column/box selection** | Editor | Low -- multi-cursor covers most cases | Small |
| 17 | **Remote/SSH debugging** | Debugging | Low for target audience | Large |
| 18 | **Debug visualizers** | Debugging | Low -- nice to have for game dev (textures) | Medium |
| 19 | **Lazy panel loading** | Performance | Low -- affects startup time only | Small |
| 20 | **AI-assisted completions** | IntelliSense | Growing importance -- no Copilot equivalent | Large |

---

### Top 10 VGS Advantages Over VS Code

| # | Advantage | Details |
|---|-----------|---------|
| 1 | **Built-in code analysis engine** | Cyclomatic complexity, cognitive complexity, code smell detection, security scanning, duplicate detection, and unused code analysis -- all without extensions. VS Code requires SonarLint, ESLint, or similar extensions for comparable analysis. |
| 2 | **25+ refactoring dialogs built-in** | Extract method, extract interface, change signature, encapsulate field, pull members up/down, inline method/variable/constant, introduce variable, convert to interface, safe delete, move type to file -- all as first-class UI dialogs. VS Code relies on language extensions for refactoring. |
| 3 | **Integrated 4-backend compiler** | C#, LLVM, MSIL, and C++ code generation from a single language, with backend switching at build time. No equivalent in VS Code. |
| 4 | **Native game engine integration** | Direct access to ~548 game engine functions through BasicLang, with the IDE understanding game-specific types and APIs. VS Code has no built-in game engine awareness. |
| 5 | **Full DAP debug adapter built-in** | The debug adapter for BasicLang ships with the IDE. VS Code requires downloading separate debug extensions for each language. |
| 6 | **Single-binary IDE distribution** | The IDE ships as a self-contained package in the `IDE/` folder with compiler, LSP server, debug adapter, and engine runtime. No dependency on a marketplace to become functional. |
| 7 | **Git operations beyond VS Code built-in** | Rebase, cherry-pick, stash management, submodule management, tag management, blame view, file history, reset (soft/mixed/hard), and clean are all built into the IDE. VS Code's built-in git is more limited; most of these require GitLens or similar extensions. |
| 8 | **Code metrics dashboard** | Lines of code, method count, class count, average complexity, max nesting depth, maintainability index -- all computed locally without external tools. |
| 9 | **Bookmark system with margin gutter** | First-class bookmark support with a visual gutter margin, persistent across sessions. VS Code requires an extension for bookmarks. |
| 10 | **Task list / TODO tracking** | Built-in TODO/HACK/FIXME comment scanning with `TaskListService.cs`. VS Code requires the Todo Tree extension. |

---

### Summary

VGS IDE is a **remarkably complete** custom IDE scoring 72/100 against VS Code. Its strongest areas are the code editor (87%), source control (80%), and IntelliSense/LSP (80%), where it matches or exceeds VS Code's built-in capabilities. The built-in code analysis engine and 25+ refactoring dialogs give VGS genuine advantages over VS Code for BasicLang development.

The three areas requiring the most investment to close the gap are:

1. **Terminal** (60%) -- the lack of pty emulation is the most user-visible gap
2. **Extensions** (40%) -- the framework exists but cannot deliver real value without a runtime API
3. **Accessibility** (20%) -- minimal investment so far; important for inclusivity

For a single-language game development IDE, VGS is exceptionally feature-rich and delivers a cohesive experience that VS Code can only match by assembling dozens of extensions.
