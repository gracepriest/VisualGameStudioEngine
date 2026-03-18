# Project System Comparison: VS Code vs Visual Game Studio IDE

This document compares the workspace/project management capabilities of VS Code with the Visual Game Studio (VGS) IDE, based on actual source code analysis of the VGS codebase.

---

## Summary Table

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Project/workspace model** | Folder-based workspaces; optional `.code-workspace` multi-root files; no formal project system | Solution/project hierarchy (`.blsln` + `.blproj` XML files); `BasicLangSolution` contains `SolutionProject` refs; `BasicLangProject` holds items, references, build configs | VGS is more structured (VS-style); VS Code is more flexible for ad-hoc editing |
| **File tree / solution explorer** | Built-in Explorer shows raw filesystem; extensions can add virtual items | `SolutionExplorerViewModel` with `TreeNode` hierarchy (Project > Folder > File); groups items by directory from project manifest; supports add/rename/delete/open-in-explorer | VS Code shows all files automatically; VGS only shows files registered in `.blproj` -- no "show all files" toggle to reveal untracked files on disk |
| **Build system** | `tasks.json` for arbitrary build commands; no built-in compiler | `BuildService` calls BasicLang compiler API directly (lexer, parser, semantic analysis, IR, backend); multi-phase build with progress events; Debug/Release configurations; clean/rebuild | VGS has deeper integration (in-process compilation); VS Code is backend-agnostic but requires manual task setup |
| **Settings management** | `settings.json` at User/Workspace/Folder scopes; JSON with schema validation; rich GUI editor | `SettingsService` with four scopes (Default > User > Workspace > Folder); JSON storage at `%APPDATA%/VisualGameStudio/settings.json` and `.vgs/settings.json`; schema registration with types, ranges, enums | Architecturally equivalent. VGS lacks a GUI settings editor and JSON IntelliSense for settings files |
| **Source control (Git)** | Built-in SCM panel; stage/unstage/commit/diff; branch switching; gutter decorations; timeline; extensions for GitHub, GitLens, etc. | `GitService` wraps `git` CLI with full API: status, stage, unstage, commit, branches, merge, rebase, cherry-pick, revert, stash, tags, submodules, blame, clone, remotes, file history, ahead/behind tracking | Feature set is comparable at the service layer. VGS lacks inline gutter diff decorations, timeline view, and the extension ecosystem (GitLens, GitHub PR, etc.) |
| **Terminal** | Integrated terminal with multiple sessions; split panes; profiles; shell integration; link detection | `TerminalService` with multi-session support (`ConcurrentDictionary`); shell auto-detection (cmd/pwsh/bash); background execution; command history (10K lines); `ExecuteCommandAsync` for programmatic use | VGS has the core functionality. Missing: split panes, terminal profiles, shell integration (command detection), link detection, drag-drop |
| **Error list / problems panel** | Problems panel fed by language extensions via diagnostics; filterable by severity, source, file | `ErrorListViewModel` fed by `BuildService` diagnostics and LSP `textDocument/publishDiagnostics`; `DiagnosticItem` with Id, Message, Severity, FilePath, Line, Column | Functionally equivalent for BasicLang. VS Code aggregates diagnostics from any number of language extensions simultaneously |
| **Output panel** | Multi-channel output (per extension, tasks, Git, etc.); color support; smart scrolling | `OutputService` with `OutputCategory` enum (General, Build, Debug, LSP, Git); categorized message storage; error tagging | VGS has fixed categories vs VS Code's dynamic channels. Missing: ANSI color rendering, link detection in output |
| **File search (quick open)** | `Ctrl+P` quick open with fuzzy file matching; `Ctrl+Shift+F` full-text search with regex, glob filters, replace; `Ctrl+T` symbol search | `SearchService` with three search types: file (fuzzy + exact matching with scoring), text (regex, case/whole-word, context lines, replace), symbol (regex-based extraction); glob-based exclude patterns; search history (50 items) | Core functionality is present. VGS lacks the polished keyboard-driven quick-open overlay UX; file search requires explicit invocation rather than a persistent palette |
| **New project wizard** | No built-in wizard; `yo` generators or extension-provided commands; workspace trust dialog | `ProjectTemplateService` with 8 templates (Console, Game, WinForms, WPF, Avalonia, ClassLibrary, WebAPI, UnitTest); 4 solution types (.NET, MSIL, Native, LLVM); `CreateProjectView` dialog; validation; auto-generates `.blproj`, source files, `.gitignore`; solution folder option | VGS is significantly richer here. VS Code has no native project creation workflow |
| **Project templates** | None built-in; `dotnet new` or extension templates | 8 project templates x 4 backend targets = 32 combinations; each template generates scaffolded source (e.g., game template creates `Main.bas` + `Sprite.bas` + `Assets/` directories); recent templates tracking; custom template registration API | VGS is far ahead. VS Code defers entirely to external tooling for project scaffolding |

---

## Detailed Analysis

### 1. Project/Workspace Model

**VS Code** uses a folder-based model. Opening a folder makes it the workspace root. Multi-root workspaces are supported via `.code-workspace` JSON files listing multiple folder roots. There is no concept of a "project file" that enumerates source files -- the filesystem *is* the project.

**VGS IDE** uses a Visual Studio-style solution/project hierarchy:
- `BasicLangSolution` (`BasicLangSolution.cs`) holds a list of `SolutionProject` references, solution folders, and global properties. Solutions are serialized as JSON (`.blsln`).
- `BasicLangProject` (`BasicLangProject.cs`) holds `Items` (explicit file list with `ProjectItemType`: Compile, Content, Resource), `References`, `Configurations` (Debug/Release with OutputPath, DebugSymbols, Optimize), `TargetBackend` (CSharp/Cpp/LLVM/MSIL), and `OutputType` (Exe/Library/WinExe).
- `ProjectService` manages the lifecycle: create, open, save, close for both projects and solutions. Only one project and one solution can be open at a time (`CurrentProject`, `CurrentSolution`).

**Gap**: VGS requires files to be explicitly added to the project manifest. There is no "open folder" mode for ad-hoc editing. VS Code's folder model is more forgiving for exploration; VGS's model is more precise for build reproducibility.

### 2. File Tree / Solution Explorer

**VS Code** Explorer reflects the raw filesystem under the workspace root. Virtual file providers can augment this (e.g., Git showing changed files).

**VGS IDE** `SolutionExplorerViewModel` builds a `TreeNode` tree from `BasicLangProject.Items`:
- Groups items by directory path, creating intermediate folder nodes.
- Node types: Project, Folder, SourceFile, ContentFile, Resource, File.
- Commands: OpenFile, AddNewFile (`.bas` default), AddNewFolder, AddExistingFile, StartRename/ConfirmRename, Delete, OpenInExplorer, CopyPath.
- Listens to `ProjectOpened`, `ProjectClosed`, `ProjectChanged` events to refresh.

**Gap**: No "Show All Files" toggle to reveal files on disk that aren't in the project. No drag-and-drop reordering. No file nesting rules (e.g., `.designer.bas` under `.bas`).

### 3. Build System

**VS Code** has `tasks.json` for defining build/test/watch tasks that invoke external tools. The built-in terminal runs them. Problem matchers parse output for diagnostics.

**VGS IDE** `BuildService` compiles in-process using the BasicLang compiler API:
- Multi-phase: parse all files, collect symbols into `ProjectSymbolTable`, semantic analysis, IR generation, backend code generation.
- Progress events at each phase with percentage.
- Configuration-aware (Debug/Release with separate output paths).
- Solution build iterates projects (currently sequential, dependency ordering noted as TODO).
- Clean deletes output directories.
- Cancel via `CancellationTokenSource`.

**Gap**: VGS cannot invoke external build tools or run arbitrary tasks. VS Code cannot do in-process compilation but is infinitely flexible via tasks.

### 4. Settings Management

**VS Code** stores settings in `settings.json` at three scopes (User, Workspace `.vscode/settings.json`, Folder). A rich GUI editor with search, categories, and JSON schema validation is provided.

**VGS IDE** `SettingsService` implements the same four-scope cascade:
- Default > User (`%APPDATA%/VisualGameStudio/settings.json`) > Workspace (`.vgs/settings.json`) > Folder.
- Dot-notation keys with nested JSON support.
- Schema registration with `SettingsPropertySchema` (type, title, description, default, min/max, enum values).
- Import/Export of settings files.
- Registered schemas: Editor (font, tab, line numbers, word wrap, auto-save, minimap, bracket colorization), Appearance (theme), Terminal (font, shell), Git (auto-fetch, confirm sync).

**Gap**: No GUI settings editor. No JSON IntelliSense when editing settings files manually. The schema system is in place but has no consumer UI.

### 5. Source Control (Git)

**VS Code** has a built-in SCM panel with staging, committing, diffing, branch management, and gutter decorations. Extensions like GitLens add blame, history, and advanced visualization.

**VGS IDE** `GitService` is comprehensive at the API level (1184 lines):
- Core: init, clone, status, stage/unstage (single + all), commit, diff, discard changes
- Branching: list, create, checkout, delete, rename, merge (with conflict detection), rebase (with abort/continue), cherry-pick
- Remote: fetch, pull, push, get/add/remove remotes, ahead/behind tracking
- History: recent commits, file history, blame (line-level with porcelain parsing), log with ref ranges
- Stash: create, list, apply/pop, drop
- Tags: list, create (lightweight + annotated), delete (local + remote), push
- Submodules: list, update (init + recursive)
- Advanced: reset (soft/mixed/hard), clean, file content at commit, repository root
- UI ViewModels: GitChangesViewModel, GitBranchesViewModel, GitStashViewModel, GitBlameViewModel

**Gap**: No inline gutter diff decorations in the editor. No graph visualization for commit history. No merge conflict editor with 3-way view. No extension ecosystem equivalent (GitLens, GitHub PRs).

### 6. Terminal

**VS Code** terminal supports multiple sessions, split panes, profiles, shell integration (command detection, decoration), link detection, and `Ctrl+Click` to open files.

**VGS IDE** `TerminalService`:
- Multi-session with `ConcurrentDictionary<string, TerminalSession>`.
- Shell auto-detection: PowerShell > cmd.exe on Windows; `$SHELL` or `/bin/bash` on Unix.
- `SendInput` for interactive shell sessions.
- `ExecuteCommandAsync` for programmatic one-shot commands with stdout/stderr capture.
- `ExecuteInBackground` for background tasks.
- History buffer capped at 10,000 lines per session.
- Active session switching.

**Gap**: No split pane support. No terminal profiles. No shell integration (command detection, prompt markers). No clickable link detection. No environment variable inheritance configuration UI.

### 7. Error List / Problems Panel

Both platforms feed diagnostics from language tooling and build output. VS Code aggregates from all active language extensions. VGS aggregates from the BasicLang compiler (`BuildService` diagnostics) and LSP (`textDocument/publishDiagnostics`). Diagnostics include severity, file path, line, column, message, and diagnostic ID.

**Gap**: VS Code supports diagnostics from unlimited concurrent language servers. VGS is BasicLang-only.

### 8. Output Panel

**VS Code** creates dynamic output channels per extension/task. Supports ANSI color codes and clickable links.

**VGS IDE** `OutputService` has fixed categories via `OutputCategory` enum: General, Build, Debug, LSP, Git. Thread-safe with `ConcurrentDictionary`. Error messages are tagged with `[ERROR]` prefix.

**Gap**: No dynamic channel creation. No ANSI color rendering. No clickable link detection in output text.

### 9. File Search (Quick Open)

**VS Code** `Ctrl+P` provides instant fuzzy file search. `Ctrl+Shift+F` provides full-text search with regex, glob filters, include/exclude patterns, and replace-all. `Ctrl+T` provides workspace symbol search.

**VGS IDE** `SearchService` provides all three search types:
- **File search**: Fuzzy matching with scoring (consecutive character bonus, word boundary bonus, exact match priority). Configurable include/exclude extensions and glob patterns.
- **Text search**: Regex or literal, case-sensitive/insensitive, whole-word, context lines before/after, replace in file/across project. Binary file detection. Progress reporting.
- **Symbol search**: Regex-based extraction for classes, interfaces, structs, enums, methods, properties. Fuzzy matching on symbol names.
- Search history (50 items per type).
- Default exclude patterns: node_modules, bin, obj, .git, .vs, packages, BuildOutput, *.exe, *.dll, *.pdb.

**Gap**: No keyboard-driven quick-open overlay (Command Palette style). Symbol search uses regex heuristics rather than LSP `workspace/symbol`. No incremental/streaming results display.

### 10. New Project Wizard

**VS Code** has no built-in project creation wizard. Users rely on CLI tools (`dotnet new`, `npm init`, `yo`) or extension-provided commands.

**VGS IDE** `ProjectTemplateService` + `CreateProjectView` dialog:
- 8 project templates: ConsoleApp, GameApp, WinFormsApp, WpfApp, AvaloniaApp, ClassLibrary, WebAPI, UnitTest.
- 4 solution types: .NET (CSharp backend), MSIL, Native (C++ backend), LLVM.
- Full project scaffolding: creates directory structure, `.blproj` XML file, source files with template content, optional `.blsln` solution file.
- Game template additionally creates `Assets/Textures`, `Assets/Sounds`, `Assets/Fonts` directories and a `Sprite.bas` helper file.
- Optional git repository initialization with backend-appropriate `.gitignore`.
- Validation: project name (invalid chars, length), location existence, duplicate detection, template/solution-type compatibility.
- Recent templates tracking (last 10).
- Custom template registration API.

**Gap**: None -- VGS is ahead of VS Code here. VS Code defers to external tools entirely.

### 11. Project Templates

**VGS IDE** templates generate complete, runnable code:
- **ConsoleApp**: .NET imports, Console.WriteLine, DateTime, Environment, File I/O.
- **GameApp**: Full game loop with GameInit/GameBeginFrame/GameEndFrame/GameShutdown, input handling, sprite drawing.
- **WinFormsApp**: Form with Label + Button, event handling, Application.Run.
- **WpfApp**: Window with StackPanel, TextBlock, Button, event handling.
- **AvaloniaApp**: AppBuilder configuration, Window with Avalonia controls.
- **ClassLibrary**: Module with utility functions.
- **WebAPI**: HTTP endpoint skeleton with Console output.
- **UnitTest**: Test runner with AssertEqual, pass/fail counting.

**VS Code** has no equivalent. The closest analogue is `dotnet new` templates or Yeoman generators, which are external tools.

---

## Key Architectural Differences

| Aspect | VS Code | VGS IDE |
|--------|---------|---------|
| **Project file** | None (filesystem-based) | XML `.blproj` with explicit item list |
| **Solution file** | `.code-workspace` (optional, JSON) | `.blsln` (JSON, contains project references) |
| **Settings location** | `%APPDATA%/Code/User/settings.json` | `%APPDATA%/VisualGameStudio/settings.json` |
| **Workspace settings** | `.vscode/settings.json` | `.vgs/settings.json` |
| **Build integration** | External (tasks.json) | In-process (BasicLang compiler API) |
| **Git integration** | Built-in + extensions | Built-in CLI wrapper (`GitService`) |
| **Extensibility** | Extension API (TypeScript) | `ExtensionService` + `MarketplaceService` (present in codebase, details TBD) |
| **File watching** | Built-in with `FileSystemWatcher` | `FileService.WatchDirectory` using `FileSystemWatcher` |

---

## Recommendations for Closing Gaps

1. **Show All Files toggle** in Solution Explorer -- display filesystem contents alongside project items, with "include in project" right-click.
2. **GUI Settings Editor** -- leverage existing `SettingsSchema` infrastructure to render a searchable settings page.
3. **Quick Open overlay** (`Ctrl+P`) -- keyboard-driven palette using `SearchService.SearchFilesAsync` with streaming results.
4. **Inline gutter diff decorations** -- use `GitService.GetDiffAsync` to show added/modified/deleted line markers in the editor margin.
5. **Terminal split panes and profiles** -- extend `TerminalService` session model to support layout splitting.
6. **ANSI color rendering** in Output panel.
7. **LSP-based symbol search** -- replace regex heuristics in `SearchService.SearchSymbolsAsync` with `workspace/symbol` request to the BasicLang LSP server.

---

*Generated from VGS IDE source code analysis, March 2026.*
