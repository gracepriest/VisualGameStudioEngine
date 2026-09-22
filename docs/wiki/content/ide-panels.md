title: Panels and dialogs
lede: 23 panel view models (22 of them dockable), three document types, and a refactoring surface of 29 dialogs.
---
All 23 panels are view models in `VisualGameStudio.Shell/ViewModels/Panels/`, 22 of them with a
matching AXAML view in `Views/Panels/`. Docking is handled under `Shell/Dock/`; layout
is persisted per project.

## Tool windows

### Project and navigation

| Panel | What it shows |
|---|---|
| Solution Explorer | Solution, projects, files; add/remove/rename |
| Document Outline | Symbols in the active file, from LSP document symbols |
| Call Hierarchy | Incoming and outgoing calls for a symbol |
| Type Hierarchy | Supertypes and subtypes — <span class="pill warn">view model only</span>: no `TypeHierarchyView.axaml`, no entry in `DockFactory`'s tool map, and `ShowTypeHierarchyCommand` never activates a tool, so the data loads with nowhere to show it |
| Bookmarks | Marked locations across the workspace |
| Timeline | Git commit history for the active file — hash, author, date, message — via `IGitService.GetFileHistoryAsync` |

### Diagnostics and output

| Panel | What it shows |
|---|---|
| Error List | Compiler diagnostics with code, file and position |
| Problems | Diagnostics aggregated from LSP, the build system and the runtime, grouped by file |
| Output | Six fixed categories (`OutputCategory`): General, Build, Debug, LanguageServer, Git, Extensions — one shown at a time, not open-ended channels |
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
- **`WebViewDocumentViewModel`** — extension-contributed webview panels, created when an extension calls `vscode.window.createWebviewPanel()`. The JavaScript preview does *not* use it: that path builds the site, serves it on loopback and opens the system browser.
- **`WelcomeDocumentViewModel`** — the start page with recent projects.

## The refactoring surface

Refactorings are dialog-driven, and the catalogue is large. All in
`ViewModels/Dialogs/`, backed by `RefactoringService.cs` and the LSP's
`CodeActionHandler.cs`.

**Rename** — Rename Symbol (`RenameDialogViewModel`, `Ctrl+R`)
**Extract** — Method · Interface · Constant
**Inline** — Method · Variable · Field · Constant
**Introduce** — Variable · Constructor generation
**Signature** — Change Signature · Add / Remove / Reorder Parameters · Rename Parameter ·
Change Parameter Type · Make Parameter Optional / Required · Convert to Named or
Positional Arguments
**Type shape** — Pull Members Up · Push Members Down · Override Method ·
Implement Interface · Convert to Interface · Use Base Type · Encapsulate Field ·
Move Type to File · Safe Delete
**No dialog — palette-driven** — Introduce Field · Invert If · Convert to Select Case · Split Declaration

> [note] These four are implemented in `RefactoringService` but have no `*DialogViewModel`; they run
> straight from the command palette's Refactor category on the caret position, with no options prompt.

## Other dialogs

| Dialog | Purpose |
|---|---|
| New Project Wizard, New Solution, Create Project | Project creation — see [Projects](#/projects) |
| Command Palette | `Ctrl+Shift+P`, 178 commands across 15 categories (View 40, Edit 30, Debug 25, Refactor 23, File 16, Git 11, …), fuzzy matching with MRU, history and match highlighting |
| Quick Open, Go to Symbol, Go to Line | Navigation |
| Find & Replace | Including workspace-wide |
| Peek Definition | Inline definition preview |
| Diff Viewer | Side-by-side comparison |
| Keyboard Shortcuts | `F1`; a searchable, **read-only** list built from `KeyboardShortcutRegistry.cs`, which mirrors `MainWindow.axaml`'s real `Window.KeyBindings` (a unit test fails if the two drift) rather than binding anything |
| Settings | Backed by `SettingsService.cs`; includes the C++ toolchain page |
| Build Configuration, Launch Configuration | Build and run settings |
| Attach to Process, Exception Settings, Breakpoint Condition, Function Breakpoint | Debugging |
| Add Project Reference | Writes through `BlprojReferenceWriter.cs` |

## Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+S` / `Ctrl+Shift+S` | Save · Save All — File ▸ New and File ▸ Open have **no** key binding; the menu's `InputGesture` text is display-only in Avalonia |
| `Ctrl+Z` / `Ctrl+Y` / `Ctrl+D` | Undo · Redo · Duplicate line |
| `Ctrl+G` / `F12` | Go to line · Go to definition |
| `Ctrl+-` / `Ctrl+Shift+-` | Zoom out · Change Signature — navigate back / forward have **no** binding (the palette shows Alt+Left / Alt+Right as labels, which nothing handles) |
| `Ctrl+F` / `Ctrl+H` / `Ctrl+Shift+F` | Find · Replace · Find in files |
| `F3` / `Shift+F3` | Find next · previous |
| `Ctrl+Shift+P` | Command palette |
| `Ctrl+Shift+B` | Build (`Ctrl+B` binds nothing — it is only the palette's display label for Toggle Side Bar) |
| `F5` / `Ctrl+F5` / `F9` / `F10` / `F11` | Debug · Run · Breakpoint · Step over · Step into |

Bindings are **not** editable in the IDE. The 66 real gestures live in `MainWindow.axaml`'s `Window.KeyBindings`; `KeyboardShortcutRegistry.cs` mirrors them read-only and `KeyboardShortcutRegistryTests` parses the AXAML at test time and fails on drift. Editor-scoped keys (Undo, Redo, Cut/Copy/Paste, Duplicate Line, Toggle Comment) are owned by the editor control, not the window.

> [trap] `KeybindingService.cs` / `IKeybindingService` exist and persist a `keybindings.json`
> under `%AppData%/VisualGameStudio`, but nothing in the Shell routes key input through them —
> the only consumer in the tree is `ExtensionService.cs`, which takes one as an optional
> constructor argument. Changing a shortcut means editing `MainWindow.axaml` and the registry.
