title: Panels and dialogs
lede: 23 tool windows, three document types, and a refactoring surface of more than 30 dialogs.
---
Every panel is a view model in `VisualGameStudio.Shell/ViewModels/Panels/` with a
matching AXAML view in `Views/Panels/`. Docking is handled under `Shell/Dock/`; layout
is persisted per project.

## Tool windows

### Project and navigation

| Panel | What it shows |
|---|---|
| Solution Explorer | Solution, projects, files; add/remove/rename |
| Document Outline | Symbols in the active file, from LSP document symbols |
| Call Hierarchy | Incoming and outgoing calls for a symbol |
| Type Hierarchy | Base types and derived types |
| Bookmarks | Marked locations across the workspace |
| Timeline | File history view |

### Diagnostics and output

| Panel | What it shows |
|---|---|
| Error List | Compiler diagnostics with code, file and position |
| Problems | The LSP-published problem set |
| Output | Build output, LSP traffic, service logs — channel-based |
| Find in Files | Workspace search results, grouped by file with per-match rows |

### Debugging

Breakpoints · Call Stack · Variables · Watch · Threads · Debug Console · Immediate Window.
See [Debugging](#/debugging).

### Source control

| Panel | What it shows |
|---|---|
| Git Changes | Working-tree status, stage, commit |
| Git Branches | Branch list, checkout, create |
| Git Stash | Stash list and apply |
| Git Blame | Per-line authorship for the active file |

Backed by `GitService.cs` and `GitAutoFetchService.cs`.

### Environment

| Panel | What it shows |
|---|---|
| Terminal | Integrated shell sessions; profile detection via `ShellProfileDetector.cs` |
| Extensions | Installed and available extensions — see [Extension host](#/ide-extensions) |

## Documents

`ViewModels/Documents/` has three document kinds:

- **`CodeEditorDocumentViewModel`** — the editor tab, one per open file.
- **`WebViewDocumentViewModel`** — embedded web view, used by the JavaScript preview.
- **`WelcomeDocumentViewModel`** — the start page with recent projects.

## The refactoring surface

Refactorings are dialog-driven, and the catalogue is large. All in
`ViewModels/Dialogs/`, backed by `RefactoringService.cs` and the LSP's
`CodeActionHandler.cs`.

**Extract** — Method · Interface · Constant
**Inline** — Method · Variable · Field · Constant
**Introduce** — Variable · Constructor generation
**Signature** — Change Signature · Add / Remove / Reorder Parameters · Rename Parameter ·
Change Parameter Type · Make Parameter Optional / Required · Convert to Named or
Positional Arguments
**Type shape** — Pull Members Up · Push Members Down · Override Method ·
Implement Interface · Convert to Interface · Use Base Type · Encapsulate Field ·
Move Type to File · Safe Delete

## Other dialogs

| Dialog | Purpose |
|---|---|
| New Project Wizard, New Solution, Create Project | Project creation — see [Projects](#/projects) |
| Command Palette | `Ctrl+Shift+P`, ~50 commands with fuzzy search |
| Quick Open, Go to Symbol, Go to Line | Navigation |
| Find & Replace | Including workspace-wide |
| Peek Definition | Inline definition preview |
| Diff Viewer | Side-by-side comparison |
| Keyboard Shortcuts | Bound through `KeyboardShortcutRegistry.cs` |
| Settings | Backed by `SettingsService.cs`; includes the C++ toolchain page |
| Build Configuration, Launch Configuration | Build and run settings |
| Attach to Process, Exception Settings, Breakpoint Condition, Function Breakpoint | Debugging |
| Add Project Reference | Writes through `BlprojReferenceWriter.cs` |

## Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+N` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` | New · Open · Save · Save All |
| `Ctrl+Z` / `Ctrl+Y` / `Ctrl+D` | Undo · Redo · Duplicate line |
| `Ctrl+G` / `F12` | Go to line · Go to definition |
| `Ctrl+-` / `Ctrl+Shift+-` | Navigate back · forward |
| `Ctrl+F` / `Ctrl+H` / `Ctrl+Shift+F` | Find · Replace · Find in files |
| `F3` / `Shift+F3` | Find next · previous |
| `Ctrl+Shift+P` | Command palette |
| `Ctrl+B` | Build |
| `F5` / `Ctrl+F5` / `F9` / `F10` / `F11` | Debug · Run · Breakpoint · Step over · Step into |

Bindings are editable; `KeybindingService.cs` and `IKeybindingService` own the mapping.
