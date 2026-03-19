# VS Code Terminal Feature Parity: Improvement Recommendations

**Date**: 2026-03-18
**Current parity estimate**: ~60%
**Target parity**: ~85%
**Scope**: VGS IDE integrated terminal vs VS Code integrated terminal

---

## Current VGS IDE Terminal Inventory

### What exists today

**TerminalService** (`VisualGameStudio.ProjectSystem/Services/TerminalService.cs`):
- Multi-session management via `ConcurrentDictionary<string, TerminalSession>`
- Session create/close/switch with events (`SessionCreated`, `SessionClosed`, `ActiveSessionChanged`)
- Input routing to active or specific sessions
- One-shot command execution (`ExecuteCommandAsync`) with cancellation support
- Background command execution (`ExecuteInBackground`)
- Output history with 10,000-line cap
- Output type classification: `StandardOutput`, `StandardError`, `Input`, `System`
- Default shell detection: `cmd.exe` on Windows (prefers PowerShell if `PWSH_PATH` set), `$SHELL` or `/bin/bash` on Unix
- Custom environment variables per session
- `CommandCompleted` event (declared but never raised)

**TerminalViewModel** (`VisualGameStudio.Shell/ViewModels/Panels/TerminalViewModel.cs`):
- Single-process shell (PowerShell on Windows, bash on Unix)
- Plain-text output buffer with 100KB cap and line-boundary truncation
- Start/Stop/Clear/SendInput commands
- Working directory support

**TerminalView** (`VisualGameStudio.Shell/Views/Panels/TerminalView.axaml`):
- Plain `TextBlock` for output (no rich rendering)
- Single input `TextBox` with `>` prompt
- Start/Stop/Clear toolbar buttons
- Auto-scroll to bottom on new output
- Enter key sends input
- Monospace font (Cascadia Code / Consolas)

### What is missing

The TerminalViewModel and TerminalView only expose a fraction of the TerminalService's capabilities. The service supports multiple sessions, but the UI shows a single terminal with no session switching, no tabs, no split panes, and no rich output rendering.

---

## VS Code Terminal Feature Matrix

| # | Feature | VS Code | VGS IDE | Gap |
|---|---------|---------|---------|-----|
| 1 | Multiple terminal instances (tabs) | Yes | Service only, no UI | **High** |
| 2 | Split panes (side-by-side terminals) | Yes | No | **High** |
| 3 | Terminal profiles (configurable shells) | Yes | Hardcoded | **High** |
| 4 | Shell auto-detection | PowerShell, Git Bash, WSL, cmd, etc. | cmd.exe or PowerShell only | **Medium** |
| 5 | Clickable file path links | Yes (Ctrl+Click) | No | **High** |
| 6 | Clickable URL links | Yes | No | **Medium** |
| 7 | ANSI color rendering | Full 256-color + truecolor | No (plain text) | **Critical** |
| 8 | ANSI escape sequence processing | Bold, italic, underline, dim, etc. | No | **High** |
| 9 | Shell integration (command detection) | Yes (via shell scripts) | No | **Low** |
| 10 | Command decorations (success/fail icons) | Yes | No | **Low** |
| 11 | Command navigation (Ctrl+Up/Down) | Yes | No | **Medium** |
| 12 | Sticky scroll (pinned command header) | Yes | No | **Low** |
| 13 | Find in terminal (Ctrl+F) | Yes | No | **High** |
| 14 | Copy/paste support | Yes | Basic (TextBlock) | **Medium** |
| 15 | Text selection in output | Yes | No (TextBlock) | **High** |
| 16 | Terminal rename | Yes | No | **Low** |
| 17 | Drag-and-drop reordering | Yes | No | **Low** |
| 18 | Custom key bindings | Yes | Enter only | **Low** |
| 19 | Working directory tracking | Yes (via shell integration) | Manual only | **Medium** |
| 20 | Environment variable inheritance | Yes | Partial | **Low** |
| 21 | Right-click context menu | Yes | No | **Medium** |
| 22 | Terminal Quick Fix (suggested actions) | Yes | No | **Low** |
| 23 | Unicode/emoji rendering | Yes (xterm.js) | Depends on TextBlock | **Low** |
| 24 | Scrollback buffer (configurable) | Default 1000 lines, configurable | 100KB text buffer | **Low** |
| 25 | Resize handling (columns/rows) | Yes (PTY) | No (redirected stdio) | **Medium** |
| 26 | Signal support (Ctrl+C) | Yes (via PTY) | No (redirected stdio) | **High** |

---

## Priority Recommendations (60% to 85%)

The following improvements are ordered by impact-to-effort ratio. Implementing items 1-8 would bring parity from ~60% to ~85%.

### Priority 1: ANSI Color Rendering (Critical)

**Gap**: All terminal output is rendered as plain white text in a `TextBlock`. ANSI escape sequences appear as garbage characters or are silently stripped by `DataReceived` events.

**VS Code approach**: Uses xterm.js, a full terminal emulator that processes all ANSI/VT100 escape sequences and renders styled text with color, bold, underline, etc. Colors are theme-aware via `terminal.ansiBlack` through `terminal.ansiBrightWhite` customization keys.

**Recommendation**:
1. Replace the plain `TextBlock` output with a custom Avalonia control (or `RichTextBlock` / `SelectableTextBlock` with inline runs).
2. Create an `AnsiParser` class that processes ANSI escape sequences (`\x1b[...m`) and produces styled text segments.
3. Support at minimum: 8 standard colors + 8 bright colors (foreground/background), bold, underline, reset.
4. Map ANSI color indices to theme-aware color properties so dark/light themes work correctly.
5. Consider the `terminal.integrated.minimumContrastRatio` concept for accessibility.

**Effort**: Medium-High (2-3 days)
**Impact**: High -- without this, PowerShell prompts, compiler errors, `dotnet` output, and test results are unreadable or garbled.

**Implementation sketch**:
```csharp
public class AnsiTextSegment
{
    public string Text { get; set; }
    public IBrush? Foreground { get; set; }
    public IBrush? Background { get; set; }
    public bool Bold { get; set; }
    public bool Underline { get; set; }
}

public static class AnsiParser
{
    // Parse "\x1b[31mError\x1b[0m" into segments
    public static List<AnsiTextSegment> Parse(string text) { ... }
}
```

### Priority 2: Text Selection and Copy Support

**Gap**: Output is a non-selectable `TextBlock`. Users cannot select text, copy error messages, or copy file paths from terminal output.

**VS Code approach**: Full text selection with mouse drag, Ctrl+C to copy, right-click context menu.

**Recommendation**:
1. Replace `TextBlock` with `SelectableTextBlock` (built into Avalonia) for immediate selection support.
2. If using a custom ANSI-rendered control, implement `Pointer` event handlers for click-drag selection.
3. Add Ctrl+C handling: if text is selected, copy to clipboard; if no selection, send SIGINT/Ctrl+C to process.

**Effort**: Low (if using `SelectableTextBlock`) to Medium (if custom control)
**Impact**: High -- this is a basic usability requirement.

### Priority 3: Terminal Tabs (Multiple Instances in UI)

**Gap**: The `TerminalService` supports multiple sessions, but `TerminalViewModel` manages only a single process. The UI has no tab bar or session switcher.

**VS Code approach**: Tab bar at the top of the terminal panel showing all terminal instances. New Terminal button (+), dropdown to select shell profile, close button per tab.

**Recommendation**:
1. Create `TerminalPanelViewModel` that owns a collection of `TerminalViewModel` instances.
2. Add a tab strip above the terminal output area with:
   - Tab per session showing name and shell type
   - "+" button to create new terminal
   - "x" button per tab to close
   - Click to switch active terminal
3. Wire `TerminalPanelViewModel` to `ITerminalService.Sessions`, `SessionCreated`, `SessionClosed`, `ActiveSessionChanged`.
4. Each `TerminalViewModel` should use `ITerminalService` instead of managing its own `Process` directly.

**Effort**: Medium (1-2 days)
**Impact**: High -- power users need multiple terminals (build output, running server, git operations simultaneously).

### Priority 4: Split Panes

**Gap**: No ability to view two terminals side by side.

**VS Code approach**: Split button next to tab list. Terminals in a group are rendered side by side with a draggable divider. Navigate with Alt+Left/Right.

**Recommendation**:
1. Add a "Split" button to the terminal toolbar.
2. When splitting, create a new `TerminalViewModel` and place both in a horizontal `Grid` with a `GridSplitter`.
3. Track split groups as a list of `TerminalViewModel` within the tab.
4. Support unsplitting (closing one side returns to single view).

**Effort**: Medium (1-2 days)
**Impact**: Medium-High -- common workflow is build in one pane, run in another.

### Priority 5: Clickable File Links

**Gap**: File paths in compiler output, stack traces, and error messages are plain text. Users must manually copy and open them.

**VS Code approach**: Regex-based link detection scans output for file paths with optional line:column suffixes. Ctrl+Click opens the file in the editor at the specified location. Supports formats like `file.cs:42`, `file.cs(42,5)`, `file.cs line 42 column 5`.

**Recommendation**:
1. After ANSI parsing (Priority 1), run a link detection pass over each output line.
2. Use regex patterns to detect:
   - Absolute paths: `[A-Z]:\\[^\s:]+` (Windows), `/[^\s:]+` (Unix)
   - With line numbers: `path:line`, `path:line:col`, `path(line,col)`
   - URLs: `https?://[^\s]+`
3. Render detected links as underlined, colored spans with pointer cursor.
4. On Ctrl+Click, open the file in the editor via `IEditorService.OpenFileAsync(path, line, col)`.
5. On plain click for URLs, open in default browser via `Process.Start`.

**Effort**: Medium (1-2 days, depends on Priority 1)
**Impact**: High -- directly accelerates the edit-compile-fix cycle for BasicLang development.

**Detection patterns for BasicLang compiler output**:
```
// BasicLang compiler errors:   "Program.bas(12,5): error BL001: ..."
// C# compiler errors:          "Program.cs(12,5): error CS0001: ..."
// Stack traces:                "at Method() in C:\path\file.cs:line 42"
// MSBuild:                     "C:\path\file.bas(12,5,12,10): error ..."
```

### Priority 6: Shell Profile Detection and Selection

**Gap**: `TerminalService.GetDefaultShell()` returns `cmd.exe` (or PowerShell if `PWSH_PATH` is set). No UI for choosing shells. `TerminalViewModel.Start()` hardcodes `powershell.exe`.

**VS Code approach**: Auto-detects installed shells (PowerShell, PowerShell Core, Git Bash, WSL distributions, cmd.exe, Windows Terminal). Users can configure profiles with custom args, icons, and environment variables. A dropdown next to the "+" button lets users pick which shell to launch.

**Recommendation**:
1. Create a `ShellProfileService` that detects available shells:
   - `cmd.exe` (always available on Windows)
   - `powershell.exe` (Windows PowerShell 5.1, always available)
   - `pwsh.exe` (PowerShell 7+, check `PATH` and `%ProgramFiles%\PowerShell\*`)
   - Git Bash (`%ProgramFiles%\Git\bin\bash.exe`)
   - WSL distributions (`wsl.exe -l -q`)
   - Windows Terminal profiles (optional, advanced)
2. Add a dropdown button next to "+" in the terminal tab bar.
3. Allow users to set a default profile in IDE settings.
4. Store profiles in `TerminalOptions.Shell` when creating sessions.

**Effort**: Medium (1 day)
**Impact**: Medium -- developers frequently use Git Bash or WSL.

### Priority 7: Find in Terminal (Ctrl+F)

**Gap**: No search functionality in terminal output.

**VS Code approach**: Ctrl+F opens a search bar overlaying the terminal. Supports regex, case sensitivity, whole word. Highlights all matches and navigates between them.

**Recommendation**:
1. Add a search overlay bar (similar to the editor's find bar) at the top of the terminal output area.
2. Search through `_outputBuffer` or the history list.
3. Highlight matches in the rendered output.
4. Provide Next/Previous navigation buttons.
5. Keyboard shortcut: Ctrl+F to open, Escape to close, Enter/F3 for next match.

**Effort**: Medium (1-2 days)
**Impact**: Medium -- useful when scrolling through long build output.

### Priority 8: Ctrl+C Signal Handling

**Gap**: The terminal uses redirected stdio (`Process.RedirectStandardInput`), which does not support sending Ctrl+C (SIGINT). Users cannot interrupt long-running commands without clicking Stop (which kills the entire shell).

**VS Code approach**: Uses a pseudo-terminal (PTY) layer (conpty on Windows, Unix PTY on Linux/macOS) which supports full signal handling, including Ctrl+C, Ctrl+Z, and Ctrl+D.

**Recommendation**:
1. On Windows, use the ConPTY API (`CreatePseudoConsole`) instead of `Process.RedirectStandardInput/Output`. This provides proper terminal emulation including signal handling, correct column/row sizing, and ANSI escape processing.
2. Consider using the `Pty.Net` NuGet package (MIT license) which wraps ConPTY/Unix PTY for .NET.
3. As a simpler interim step, intercept Ctrl+C in the input handler and call `GenerateConsoleCtrlEvent` via P/Invoke to send SIGINT to the shell process group.

**Effort**: High for full PTY (3-5 days), Low for P/Invoke interim (0.5 day)
**Impact**: Medium-High -- Ctrl+C is muscle memory for every developer.

**Interim P/Invoke approach**:
```csharp
[DllImport("kernel32.dll")]
static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

// In key handler:
if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
{
    // Send Ctrl+C to process group
    GenerateConsoleCtrlEvent(0 /* CTRL_C_EVENT */, (uint)_shellProcess.Id);
}
```

---

## Lower Priority Items (Beyond 85%)

These features would push parity above 85% but are less critical for a game development IDE:

| Feature | Effort | Notes |
|---------|--------|-------|
| Shell integration (command detection via escape sequences) | High | Requires injecting scripts into shell init files |
| Command decorations (success/fail gutter icons) | Medium | Depends on shell integration |
| Command navigation (Ctrl+Up/Down between commands) | Medium | Depends on shell integration |
| Sticky scroll (pinned command header) | Medium | Depends on shell integration |
| Terminal Quick Fix (suggested next actions) | High | Requires pattern matching on error output |
| Right-click context menu | Low | Copy, Paste, Select All, Clear, Split |
| Terminal rename | Low | Double-click tab to rename |
| Drag-and-drop tab reordering | Low | Standard tab control behavior |
| Configurable scrollback buffer size | Low | Setting in IDE preferences |
| Full PTY support (ConPTY/Unix PTY) | High | Proper terminal emulation, correct resize handling |

---

## Implementation Roadmap

### Phase 1: Foundation (Week 1) -- 60% to 70%
1. ANSI color parser and styled output rendering
2. Replace `TextBlock` with `SelectableTextBlock` or custom control
3. Wire `TerminalViewModel` to `ITerminalService` instead of raw `Process`

### Phase 2: Multi-Terminal UI (Week 2) -- 70% to 78%
4. Terminal tab bar with create/close/switch
5. Split pane support
6. Shell profile detection and dropdown

### Phase 3: Productivity Features (Week 3) -- 78% to 85%
7. Clickable file path and URL links
8. Find in terminal (Ctrl+F)
9. Ctrl+C signal handling (P/Invoke interim)
10. Right-click context menu (Copy, Paste, Clear)

---

## Key Files to Modify

| File | Changes |
|------|---------|
| `VisualGameStudio.Shell/ViewModels/Panels/TerminalViewModel.cs` | Refactor to use `ITerminalService`, add ANSI parsing |
| `VisualGameStudio.Shell/Views/Panels/TerminalView.axaml` | Replace TextBlock, add tab bar, split pane layout, search bar |
| `VisualGameStudio.Shell/Views/Panels/TerminalView.axaml.cs` | Link click handlers, Ctrl+C, Ctrl+F, selection logic |
| `VisualGameStudio.ProjectSystem/Services/TerminalService.cs` | Shell profile detection, fire `CommandCompleted` event |
| `VisualGameStudio.Core/Abstractions/Services/ITerminalService.cs` | Add shell profile types, link detection interface |

**New files to create**:
| File | Purpose |
|------|---------|
| `VisualGameStudio.Shell/ViewModels/Panels/TerminalPanelViewModel.cs` | Manages terminal tabs and split groups |
| `VisualGameStudio.Shell/Controls/AnsiParser.cs` | ANSI escape sequence parser |
| `VisualGameStudio.Shell/Controls/TerminalLinkDetector.cs` | File path and URL regex detection |
| `VisualGameStudio.ProjectSystem/Services/ShellProfileService.cs` | Shell auto-detection and profiles |

---

## Sources

- [VS Code Terminal Basics](https://code.visualstudio.com/docs/terminal/basics)
- [VS Code Terminal Profiles](https://code.visualstudio.com/docs/terminal/profiles)
- [VS Code Terminal Appearance](https://code.visualstudio.com/docs/terminal/appearance)
- [VS Code Terminal Shell Integration](https://code.visualstudio.com/docs/terminal/shell-integration)
- [VS Code Terminal Advanced](https://code.visualstudio.com/docs/terminal/advanced)
- [Terminal UI and Layout - DeepWiki](https://deepwiki.com/microsoft/vscode/9.6-terminal-ui-and-layout)
- [Terminal Profiles and Configuration - DeepWiki](https://deepwiki.com/microsoft/vscode/7.7-terminal-profiles-and-configuration)
- [Terminal Shell Integration and Suggestions - DeepWiki](https://deepwiki.com/microsoft/vscode/6.3-terminal-shell-integration-and-suggestions)
- [VS Code July 2025 Release Notes (v1.103)](https://code.visualstudio.com/updates/v1_103)
- [Handle links in terminal from VS Code extension - Elio Struyf](https://www.eliostruyf.com/handle-links-in-the-terminal-from-your-vscode-extension/)
