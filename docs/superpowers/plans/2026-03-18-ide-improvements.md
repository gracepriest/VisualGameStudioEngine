# IDE Improvements Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the top 20 gaps between VGS IDE and VS Code, raising parity from 72/100 to ~85/100.

**Architecture:** Each task is independent and targets a specific gap identified in the comparison reports. Tasks modify existing files following established patterns. Focus on IntelliSense/LSP (5 missing features), Code Editor (4 improvements), UI/UX (6 improvements), and Debugging (5 improvements).

**Tech Stack:** C#, Avalonia UI, AvaloniaEdit, LSP protocol, DAP protocol

---

## Task Map (20 tasks for 20 parallel agents)

### LSP/IntelliSense (Tasks 1-5)
| # | Feature | Files |
|---|---------|-------|
| 1 | Go to Implementation | `LanguageService.cs`, `MainWindowViewModel.cs` |
| 2 | On-type formatting | `LanguageService.cs`, `CodeEditorControl.axaml.cs` |
| 3 | Workspace symbols via LSP | `LanguageService.cs`, `MainWindowViewModel.cs` |
| 4 | Linked editing ranges | `LanguageService.cs`, `CodeEditorControl.axaml.cs` |
| 5 | LSP folding ranges | `LanguageService.cs`, `CodeEditorControl.axaml.cs` |

### Code Editor (Tasks 6-9)
| # | Feature | Files |
|---|---------|-------|
| 6 | Column/rectangular selection | `CodeEditorControl.axaml.cs` |
| 7 | Semantic token highlighting | `LanguageService.cs`, `CodeEditorControl.axaml.cs` |
| 8 | Snippet tab-stop cycling | `SnippetProvider.cs`, `CodeEditorControl.axaml.cs` |
| 9 | Whitespace rendering toggle | `CodeEditorControl.axaml.cs`, `MainWindow.axaml` |

### UI/UX (Tasks 10-15)
| # | Feature | Files |
|---|---------|-------|
| 10 | Toast notification system | New: `NotificationService.cs`, `NotificationView.axaml` |
| 11 | Light theme | `App.axaml`, theme resources |
| 12 | Breadcrumbs click navigation | `BreadcrumbBar` in editor area |
| 13 | Quick Open unified (Ctrl+P) | `CommandPaletteViewModel.cs` |
| 14 | Zen mode (distraction-free) | `MainWindowViewModel.cs`, `MainWindow.axaml` |
| 15 | Keyboard shortcut editor | New: `KeybindingsViewModel.cs` |

### Debugging (Tasks 16-20)
| # | Feature | Files |
|---|---------|-------|
| 16 | Conditional BP evaluation | `NetDebugAdapter.cs` |
| 17 | Logpoint interpolation | `NetDebugAdapter.cs` |
| 18 | Debug hover variable values | `MainWindowViewModel.cs`, `CodeEditorControl.axaml.cs` |
| 19 | Variables panel CLR wiring | `NetDebugAdapter.cs`, `DebugService.cs` |
| 20 | Step Over skip framework code | `NetDebugAdapter.cs` |

---

### Task 1: Go to Implementation via LSP

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs`
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`

- [ ] Add `GetImplementationAsync(string uri, int line, int column)` to LanguageService — send `textDocument/implementation` LSP request, parse LocationInfo response (same pattern as GetDefinitionAsync)
- [ ] Add `GoToImplementationAsync()` command to MainWindowViewModel — get active doc, call LanguageService, navigate to result
- [ ] Add menu item "Go to Implementation" in Edit or Navigate menu with keybinding Ctrl+F12
- [ ] Build and test

---

### Task 2: On-type formatting via LSP

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs`
- Modify: `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml.cs`

- [ ] Add `OnTypeFormattingAsync(string uri, int line, int column, string ch)` to LanguageService — send `textDocument/onTypeFormatting` request
- [ ] In CodeEditorDocumentView, subscribe to TextChanged or KeyDown for trigger chars (Enter, End Sub, End If, End Function, etc.)
- [ ] When trigger char typed, call OnTypeFormattingAsync and apply returned text edits
- [ ] Build and test

---

### Task 3: Workspace symbols via LSP

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs`
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`

- [ ] Add `GetWorkspaceSymbolsAsync(string query)` to LanguageService — send `workspace/symbol` request
- [ ] Replace the manual file-iterating symbol search in MainWindowViewModel with the LSP call
- [ ] Build and test

---

### Task 4: Linked editing ranges via LSP

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs`
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs`

- [ ] Add `GetLinkedEditingRangesAsync(string uri, int line, int column)` to LanguageService — send `textDocument/linkedEditingRange` request
- [ ] In CodeEditorControl, when the caret moves to a position with linked ranges, highlight all linked ranges and sync edits between them
- [ ] Build and test

---

### Task 5: LSP folding ranges

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs`
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs`

- [ ] Add `GetFoldingRangesAsync(string uri)` to LanguageService — send `textDocument/foldingRange` request
- [ ] In CodeEditorControl, after document opens or changes, fetch LSP folding ranges and apply them to AvaloniaEdit's folding manager (replacing or supplementing local folding logic)
- [ ] Build and test

---

### Task 6: Column/rectangular selection

**Files:**
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs`

- [ ] AvaloniaEdit already supports rectangular selection via Alt+Shift+Arrow or Alt+Click. Enable it by setting `TextArea.Options.EnableRectangularSelection = true`
- [ ] Add Alt+Shift+Arrow keybindings if not already present
- [ ] Verify selection rendering works (column highlight)
- [ ] Build and test

---

### Task 7: Semantic token highlighting from LSP

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/LanguageService.cs`
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs`

- [ ] The LSP server already sends semantic tokens. Add `GetSemanticTokensAsync(string uri)` to LanguageService — send `textDocument/semanticTokens/full` request
- [ ] Parse the response (encoded as delta arrays of token type + modifier + line + startChar + length)
- [ ] Create a `DocumentColorizingTransformer` that applies colors based on semantic token types (class=green, function=yellow, parameter=italic, etc.)
- [ ] Register the transformer on the editor's TextArea
- [ ] Build and test

---

### Task 8: Snippet tab-stop cycling

**Files:**
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs`
- Modify: `VisualGameStudio.Editor/Completion/SnippetProvider.cs` (or similar)

- [ ] After a snippet is inserted, parse $1, $2, etc. placeholders
- [ ] Track placeholder positions in the document
- [ ] On Tab, move caret to next placeholder; on Shift+Tab, move to previous
- [ ] On Escape or typing outside placeholders, exit snippet mode
- [ ] Build and test

---

### Task 9: Whitespace rendering toggle

**Files:**
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs`
- Modify: `VisualGameStudio.Shell/Views/MainWindow.axaml`

- [ ] Add a View menu item "Show Whitespace" (toggle)
- [ ] When toggled, set `TextEditorOptions.ShowSpaces = true`, `ShowTabs = true`, `ShowEndOfLine = true`
- [ ] Persist the setting in user preferences
- [ ] Build and test

---

### Task 10: Toast notification system

**Files:**
- Create: `VisualGameStudio.Shell/Views/Controls/NotificationToast.axaml` + `.cs`
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`
- Modify: `VisualGameStudio.Shell/Views/MainWindow.axaml`

- [ ] Create a notification toast overlay (positioned bottom-right like VS Code)
- [ ] Support Info, Warning, Error severity with icons and colors
- [ ] Auto-dismiss after 5 seconds, or click to dismiss
- [ ] Add `ShowNotification(string message, NotificationSeverity severity)` to MainWindowViewModel
- [ ] Use it for: build complete, debug started, errors found
- [ ] Build and test

---

### Task 11: Light theme

**Files:**
- Modify: `VisualGameStudio.Shell/App.axaml` (or theme resources)
- Create: `VisualGameStudio.Shell/Themes/LightTheme.axaml`

- [ ] Define a light color palette (white background, dark text, light blue accents)
- [ ] Create LightTheme.axaml with Avalonia ResourceDictionary overriding key brushes
- [ ] Add theme switching in Settings (Dark/Light toggle)
- [ ] Apply theme at runtime via `Application.Current.RequestedThemeVariant`
- [ ] Build and test

---

### Task 12: Breadcrumbs click navigation

**Files:**
- Modify: `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml` + `.cs`

- [ ] Check if breadcrumbs bar exists. If so, make each segment clickable
- [ ] File path segments: clicking opens the parent folder in Solution Explorer
- [ ] Symbol segments: clicking scrolls to that symbol in the document
- [ ] If breadcrumbs don't exist, add a simple TextBlock-based breadcrumb showing `File > Module > Function`
- [ ] Build and test

---

### Task 13: Quick Open unified (Ctrl+P = file search)

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs`

- [ ] When Command Palette opens without `>` prefix, switch to file search mode (like VS Code Ctrl+P)
- [ ] List all files in the project with fuzzy matching
- [ ] On Enter, open the selected file
- [ ] When `>` is typed, switch to command mode (existing behavior)
- [ ] When `@` is typed, switch to symbol search mode
- [ ] When `:` is typed, switch to go-to-line mode
- [ ] Build and test

---

### Task 14: Zen mode (distraction-free)

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`
- Modify: `VisualGameStudio.Shell/Views/MainWindow.axaml`

- [ ] Add `ToggleZenMode()` command with keybinding Ctrl+K Z (or Ctrl+Shift+Z)
- [ ] When activated: hide sidebar, hide bottom panels, hide menu bar, hide status bar, maximize editor area
- [ ] Store previous visibility state to restore on exit
- [ ] Show a subtle "Press Escape to exit Zen mode" tooltip
- [ ] Build and test

---

### Task 15: Keyboard shortcut editor

**Files:**
- Create: `VisualGameStudio.Shell/Views/Dialogs/KeybindingsDialog.axaml` + `.cs`
- Create: `VisualGameStudio.Shell/ViewModels/Dialogs/KeybindingsDialogViewModel.cs`

- [ ] Show a table of all commands with their current keybindings
- [ ] Allow searching/filtering by command name or key
- [ ] Allow clicking a keybinding cell to record a new key combination
- [ ] Save custom keybindings to a JSON file
- [ ] Add "Keyboard Shortcuts" to Edit menu and Command Palette
- [ ] Build and test

---

### Task 16: Conditional breakpoint evaluation

**Files:**
- Modify: `BasicLang/Debugger/NetDebugAdapter.cs`

- [ ] Replace the stub `EvaluateConditionExpression` with real evaluation
- [ ] Use the active frame to look up variable names and compare values
- [ ] Support simple conditions: `x > 5`, `count == 0`, `flag`, `name == "test"`
- [ ] Parse the condition, look up variable values via HandleEvaluate logic, evaluate
- [ ] If condition false, call RawContinueProcess and skip the stopped event
- [ ] Build and test

---

### Task 17: Logpoint message interpolation

**Files:**
- Modify: `BasicLang/Debugger/NetDebugAdapter.cs`

- [ ] Replace the stub `InterpolateLogMessage` with real interpolation
- [ ] Parse `{expression}` patterns in the message string
- [ ] For each `{expr}`, look up the variable value in the current frame
- [ ] Replace with the value string, or `<undefined>` if not found
- [ ] Build and test

---

### Task 18: Debug hover variable values

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`
- Modify: `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml.cs`

- [ ] In the DataTip/hover handler, check if debugging is active and paused
- [ ] If so, send DAP `evaluate` request with the hovered word
- [ ] Show the result in the hover tooltip (override LSP hover during debug)
- [ ] If evaluate fails, fall back to LSP hover
- [ ] Build and test

---

### Task 19: Variables panel CLR wiring

**Files:**
- Modify: `BasicLang/Debugger/NetDebugAdapter.cs`
- Modify: `VisualGameStudio.ProjectSystem/Services/DebugService.cs`

- [ ] In HandleScopes, return Locals and Arguments scopes with proper variablesReference IDs
- [ ] In HandleVariables, use VariableInspector.GetLocals/GetArguments on the stored ICorDebugILFrame
- [ ] The frame must be obtained via QI from the active frame (same pattern as GetIP)
- [ ] If COM calls fail, return variable names from PDB with "unavailable" values
- [ ] Build and test

---

### Task 20: Step Over skips framework code (JMC)

**Files:**
- Modify: `BasicLang/Debugger/NetDebugAdapter.cs`

- [ ] In OnStepCompleted, check if the current frame maps to a .bas source file
- [ ] Add `IsUserCode(methodToken, ilOffset)` helper that checks SourceMapper
- [ ] If NOT user code, create a new stepper and step again automatically
- [ ] Limit auto-step retries to 100 to prevent infinite loops
- [ ] Build and test

---

## Build & Test Commands

```bash
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release --nologo -v q
dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q
```

## Copy IDE Binaries

```bash
cp BasicLang/bin/Release/net8.0/BasicLang.dll IDE/BasicLang.dll
cp BasicLang/bin/Release/net8.0/BasicLang.exe IDE/BasicLang.exe
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.dll IDE/VisualGameStudio.dll
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.exe IDE/VisualGameStudio.exe
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.ProjectSystem.dll IDE/VisualGameStudio.ProjectSystem.dll
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.Core.dll IDE/VisualGameStudio.Core.dll
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.Editor.dll IDE/VisualGameStudio.Editor.dll
```
