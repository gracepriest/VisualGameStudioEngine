# VS Code vs Visual Game Studio IDE: Debug UX Comparison

This document provides a comprehensive feature-by-feature comparison of the debugging user experience between VS Code and the Visual Game Studio (VGS) IDE.

---

## 1. Debug Toolbar

### VS Code
- Floating toolbar appears at the top of the editor when a debug session starts.
- Buttons: **Continue** (F5), **Step Over** (F10), **Step Into** (F11), **Step Out** (Shift+F11), **Restart** (Ctrl+Shift+F5), **Stop** (Shift+F5).
- Toolbar is draggable and can be repositioned.
- Buttons are context-sensitive: grayed out when not applicable (e.g., stepping buttons disabled while running).

### VGS IDE
- Debug toolbar is embedded in the main toolbar area (not floating).
- Buttons: **Start Debugging** (F5), **Continue** (F5, visible when paused), **Pause** (visible when running), **Stop** (Shift+F5), **Step Over** (F10), **Step Into** (F11), **Step Out** (Shift+F11).
- Buttons show/hide based on state (e.g., Start button hidden during debugging, stepping buttons visible only when paused).
- Debug status text displayed inline next to toolbar buttons.

### Comparison

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Continue | Yes (F5) | Yes (F5) |
| Step Over | Yes (F10) | Yes (F10) |
| Step Into | Yes (F11) | Yes (F11) |
| Step Out | Yes (Shift+F11) | Yes (Shift+F11) |
| Restart | Yes (Ctrl+Shift+F5) | **No** -- `RestartAsync()` exists in IDebugService but no UI command or toolbar button |
| Stop | Yes (Shift+F5) | Yes (Shift+F5) |
| Pause | Yes | Yes |
| Floating/draggable toolbar | Yes | No (embedded in main toolbar) |
| Debug status indicator | Status bar | Inline text in toolbar + StatusText property |

---

## 2. Breakpoints

### 2.1 Line Breakpoints

#### VS Code
- Click in the editor gutter (margin) to toggle breakpoints.
- Red dot indicator in the gutter.
- Breakpoints panel shows all breakpoints across all files.
- Enable/disable individual breakpoints without removing them.
- Verified state shown (filled vs hollow dot) based on debugger confirmation.

#### VGS IDE
- Click in the breakpoint margin to toggle breakpoints.
- Red dot indicator in the gutter (custom `BreakpointMargin` renderer).
- Breakpoints panel (`BreakpointsViewModel`) shows all breakpoints.
- Enable/disable support (`IsEnabled` property, `ToggleBreakpoint` method).
- Verified state tracked (`IsVerified`, `Message` from debugger).
- Toggle via keyboard: F9.
- Visual distinction between normal, conditional, hit-count, and logpoint breakpoints (different glyph shapes via `BreakpointKind` enum).

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Gutter click toggle | Yes | Yes |
| Red dot glyph | Yes | Yes |
| Enable/disable | Yes | Yes |
| Verified state | Yes | Yes |
| Breakpoints panel | Yes | Yes |
| Different glyphs per kind | Yes | Yes (Normal, Conditional, HitCount, Logpoint) |

### 2.2 Conditional Breakpoints

#### VS Code
- Right-click gutter or use context menu to add a conditional breakpoint.
- Expression condition: breakpoint fires when expression evaluates to true.
- Hit count condition: breakpoint fires after N hits.
- Condition and hit count can be combined.

#### VGS IDE
- Edit condition via Breakpoints panel (`EditConditionRequested` event).
- Dedicated `BreakpointConditionDialogViewModel` with full UI.
- Supports expression conditions (`Condition` property).
- Supports hit count conditions (`HitCondition` property).
- Conditions sent to DAP debug adapter via `SourceBreakpoint`.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Expression condition | Yes | Yes |
| Hit count condition | Yes | Yes |
| Condition dialog | Inline in editor | Dedicated dialog |

### 2.3 Logpoints

#### VS Code
- A breakpoint variant that logs a message instead of breaking.
- Message can include expressions in curly braces: `Value is {x}`.
- Shown with a diamond-shaped glyph instead of a circle.
- Can be combined with conditions and hit counts.

#### VGS IDE
- Supported via `LogMessage` property on `BreakpointItem`.
- `BreakpointConditionDialogViewModel` has `IsLogMessage` mode with `LogMessage` field.
- Validation for log message syntax (`ValidateLogMessage()`).
- Rendered with distinct glyph (`BreakpointKind.Logpoint`).
- Sent to debugger via `SourceBreakpoint.LogMessage`.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Log message breakpoints | Yes | Yes |
| Expression interpolation | Yes ({expr}) | Yes (via DAP) |
| Distinct glyph | Yes (diamond) | Yes (via BreakpointKind.Logpoint) |
| Combined with conditions | Yes | Yes |

### 2.4 Function Breakpoints

#### VS Code
- Set breakpoints on function names rather than specific lines.
- Added via the Breakpoints panel "+" button.
- Supports conditions and hit counts.

#### VGS IDE
- Full support via `FunctionBreakpointItem` and `AddFunctionBreakpointAsync()`.
- Dedicated UI: Ctrl+Shift+F9 opens function breakpoint dialog.
- Supports conditions and hit counts.
- Synced to DAP via `SetFunctionBreakpointsAsync()`.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Function breakpoints | Yes | Yes |
| Conditions on function BPs | Yes | Yes |
| Keyboard shortcut | N/A (panel button) | Ctrl+Shift+F9 |

### 2.5 Data Breakpoints (Watchpoints)

#### VS Code
- Break when a variable's value changes (write), is read, or both.
- Set from the Variables panel by right-clicking a variable.
- Depends on debug adapter support (e.g., supported by C/C++ and some .NET adapters).

#### VGS IDE
- Full support via `DataBreakpointItem` and `AddDataBreakpointAsync()`.
- Queries debug adapter for capability: `GetDataBreakpointInfoAsync()`.
- User can choose access type (write, read, readWrite) via dialog.
- Synced to DAP via `SetDataBreakpointsAsync()`.
- `DataBreakpoint` stop reason recognized.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Data breakpoints | Yes | Yes |
| Access type selection | Yes | Yes (dialog with choices) |
| Set from Variables panel | Yes | Yes (select variable, then command) |

### 2.6 Inline Breakpoints (Column Breakpoints)

#### VS Code
- Set breakpoints at specific columns within a line (Shift+F9).
- Useful for minified code or multiple statements on one line.
- Shown as inline markers within the line.

#### VGS IDE
- `SourceBreakpoint` model has a `Column` property, suggesting DAP-level support.
- **No UI** for setting column-specific breakpoints.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Column/inline breakpoints | Yes | **No** (DAP model supports it, no UI) |

### 2.7 Triggered Breakpoints

#### VS Code
- A breakpoint that only activates after another breakpoint is hit.
- Set via right-click context menu on gutter.

#### VGS IDE
- **Not implemented.** No triggered breakpoint support in the breakpoint model or UI.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Triggered breakpoints | Yes | **No** |

### 2.8 Exception Breakpoints

#### VS Code
- Break on all exceptions, uncaught exceptions, or specific exception types.
- Configured in the Breakpoints panel under "Exception Breakpoints" section.
- Language-specific filters (e.g., caught/uncaught for JavaScript).

#### VGS IDE
- Full support via `ShowExceptionSettingsAsync()` command.
- Dedicated exception settings dialog (`ShowExceptionSettingsDialogAsync`).
- Supports categories: All Exceptions, Runtime, IO, User exceptions.
- Supports individual exception types with `thrown` and `uncaught` filters.
- `ExceptionFilterOption` with per-type conditions.
- Keyboard shortcut: Ctrl+Alt+X.
- Menu: Debug > Exception Settings...

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Break on all exceptions | Yes | Yes |
| Break on uncaught exceptions | Yes | Yes |
| Per-type exception filters | Yes | Yes |
| Dedicated settings UI | Yes (inline in panel) | Yes (dialog) |
| Keyboard shortcut | N/A | Ctrl+Alt+X |

---

## 3. Variables Panel

### VS Code
- Shows variables organized by scope (Locals, Globals, Closure, etc.).
- Expandable tree for objects/arrays -- drill into nested properties.
- Right-click to **Set Value** (modify variable during debugging).
- Right-click to **Copy Value** or **Copy as Expression**.
- Variables update automatically when stepping or hitting breakpoints.
- Variable values are relative to the selected stack frame.

### VGS IDE
- `VariablesViewModel` with both tree view (`VariableTree`) and flat list (`Variables`).
- Organized by scope (auto-expanded scope nodes with `IsScope` flag).
- Expandable tree: `ExpandVariableAsync()` lazily loads children via `GetVariablesAsync()`.
- Shows Name, Value, Type for each variable.
- Variables refresh on breakpoint hit and step events.
- Frame-relative: `SetFrameAsync()` changes context to selected frame.
- **No Set Value** -- no UI to modify variable values during debugging.
- **No Copy Value** context menu.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Scope-organized display | Yes | Yes |
| Expandable nested objects | Yes (tree) | Yes (tree with lazy loading) |
| Set Value (modify variables) | Yes | **No** |
| Copy Value/Expression | Yes | **No** |
| Auto-refresh on stop | Yes | Yes |
| Frame-relative display | Yes | Yes |

---

## 4. Watch Expressions

### VS Code
- WATCH panel in the debug sidebar.
- Add expressions via "+" button or by typing.
- Expressions re-evaluated on every pause/step.
- Expandable results for objects.
- Edit or remove existing watches.
- Expressions persist across debug sessions.

### VGS IDE
- **Two implementations:**
  1. `WatchViewModel` -- standalone Watch panel with `WatchPanelItem` collection.
  2. `VariablesViewModel.WatchExpressions` -- watch expressions embedded in Variables panel.
- Both support add, remove, and auto-evaluation on pause.
- `EvaluateAsync()` called for each expression when debugger pauses.
- Error display: `<error: message>` for failed evaluations.
- Editable input field for new expressions.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Watch panel | Yes | Yes (two implementations) |
| Add/remove expressions | Yes | Yes |
| Auto-evaluate on pause | Yes | Yes |
| Expandable results | Yes | Partial (via VariablesReference) |
| Edit existing watches | Yes | Yes |
| Persist across sessions | Yes | **No** (not persisted) |

---

## 5. Call Stack Panel

### VS Code
- Shows the current call stack with function names, file paths, and line numbers.
- Click a frame to navigate to that location and update Variables/Watch context.
- Multi-thread support: shows threads as top-level items.
- Multi-process support: separate session entries.
- "Restart Frame" option to re-execute from a specific frame.

### VGS IDE
- `CallStackViewModel` with `StackFrameItem` collection.
- Shows frame name, file path, line, column, and module name.
- `FrameSelected` event for navigation on frame click.
- Auto-refreshes on breakpoint hit and step events.
- Formatted display: `FormatFrameDisplay()` for human-readable output.
- Clears on debug stop.
- **No multi-thread display** (uses threadId=1 default).
- **No Restart Frame.**

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Call stack display | Yes | Yes |
| Navigate to frame | Yes | Yes (FrameSelected event) |
| Update variables for frame | Yes | Yes (SetFrameAsync) |
| Multi-thread support | Yes | **No** (single thread assumed) |
| Restart Frame | Yes | **No** |

---

## 6. Debug Console (REPL)

### VS Code
- Dedicated Debug Console panel.
- Evaluate arbitrary expressions while paused.
- Autocomplete/IntelliSense for expressions.
- Syntax highlighting matching the active language.
- Multi-line input with Shift+Enter.
- Program output also appears in the console.
- Keyboard shortcut: Ctrl+Shift+Y.

### VGS IDE
- `ImmediateWindowViewModel` serves as the debug REPL.
- Evaluate expressions with optional `?` prefix.
- Command history with Up/Down arrow navigation.
- Special commands: `clear`, `help`.
- Shows expression result with type information.
- Error handling for failed evaluations.
- Only works when debugger is paused.
- **No autocomplete** in the immediate window.
- **No syntax highlighting** in input.
- **No multi-line input.**
- Program output goes to the Output panel, not the Immediate Window.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Expression evaluation REPL | Yes | Yes (Immediate Window) |
| Autocomplete in console | Yes | **No** |
| Syntax highlighting | Yes | **No** |
| Multi-line input | Yes (Shift+Enter) | **No** |
| Command history | Not built-in | Yes (Up/Down arrows) |
| Special commands (clear/help) | No | Yes |
| Program output in console | Yes | No (separate Output panel) |

---

## 7. Inline Variable Values

### VS Code
- `debug.inlineValues` setting enables inline display of variable values as editor decorations.
- Values shown at the end of lines next to where variables are referenced.
- Provided by the `InlineValuesProvider` API for debug adapter extensions.
- Cleared when execution resumes.

### VGS IDE
- Full implementation via `InlineDebugValueRenderer`.
- `ShowInlineDebugValuesAsync()` in `MainWindowViewModel`:
  - Gets scopes and variables from the debugger.
  - Scans source code for variable name references near the stopped line.
  - Whole-word matching to find correct variable usage lines.
  - Truncates long values to 80 characters.
  - Avoids duplicates on the same line.
- `ClearAllInlineDebugValues()` called on resume and stop.
- Values displayed as editor background decorations.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Inline variable values | Yes (via setting) | Yes (automatic when paused) |
| Shows near variable usage | Yes | Yes (scans 50 lines above stop point) |
| Auto-clear on resume | Yes | Yes |
| Truncation for long values | Varies | Yes (80 char limit) |

---

## 8. Debug Hover (Data Tips)

### VS Code
- Hover over a variable in the editor during debugging to see its value.
- Expandable tooltip for objects -- drill into nested properties.
- Shows type information.
- Works alongside LSP hover information.

### VGS IDE
- Implemented via `DataTipRequested` event chain:
  - Editor detects hover via `_hoverTimer` (500ms debounce).
  - `OnDataTipEvaluationRequested` in `MainWindowViewModel` calls `_debugService.EvaluateAsync()`.
  - `DataTipResult` event delivers result back to the editor.
- Shows expression, value, and type.
- Error results displayed for invalid expressions.
- Only active when debugger is paused.
- **No expandable tree** in the hover tooltip for nested objects.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Hover shows variable value | Yes | Yes |
| Type information | Yes | Yes |
| Expandable nested objects | Yes | **No** |
| Debounced hover | Yes | Yes (500ms timer) |

---

## 9. Execution Control

### VS Code
- **Run to Cursor**: Right-click context menu or Ctrl+F10 (implementation varies by adapter).
- **Jump to Cursor / Set Next Statement**: Move execution point without executing intervening code (adapter-dependent).
- **Step Back / Reverse Debugging**: Supported by some adapters.

### VGS IDE
- **Run to Cursor**: `RunToCursorAsync()` -- Ctrl+F10. Preserves existing breakpoints while adding a temporary one.
- **Set Next Statement**: `SetNextStatementAsync()` -- Ctrl+Shift+F10. Moves execution to cursor line via DAP `goto` request.
- **Start Without Debugging**: Ctrl+F5. Runs without debugger.
- **Run in External Console**: Opens program in a new console window.
- **No Step Back / Reverse Debugging.**

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Run to Cursor | Yes | Yes (Ctrl+F10) |
| Set Next Statement | Adapter-dependent | Yes (Ctrl+Shift+F10) |
| Start Without Debugging | Yes (Ctrl+F5) | Yes (Ctrl+F5) |
| Run in External Console | Yes (launch config) | Yes (dedicated command) |
| Step Back / Reverse Debug | Some adapters | **No** |

---

## 10. Current Execution Line Highlighting

### VS Code
- Yellow background highlight on the current stopped line.
- Arrow indicator in the gutter.
- Line scrolled into view when execution stops.

### VGS IDE
- `SetCurrentExecutionLine()` highlights the line and shows yellow arrow in `BreakpointMargin`.
- `CurrentLineBrush` is yellow (#FFCC00).
- Auto-scroll to execution line via `ScrollToLine()`.
- Execution line cleared on resume and stop.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Line highlight | Yes (yellow) | Yes (yellow #FFCC00) |
| Gutter arrow | Yes | Yes |
| Auto-scroll to line | Yes | Yes |
| Clear on resume | Yes | Yes |

---

## 11. Debug Output

### VS Code
- Debug Console shows program stdout/stderr and debug adapter messages.
- Output categories: console, stdout, stderr, telemetry.
- ANSI color code support in some terminals.

### VGS IDE
- `OutputPanel` receives debug output via `OnDebugOutput` event handler.
- `OutputCategory` switching: Debug category selected during debugging.
- Build-before-debug output also shown.
- Breakpoint diagnostic logging during session start.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Debug output display | Yes | Yes (Output panel) |
| Category filtering | Yes | Yes (OutputCategory) |
| ANSI color support | Partial | **No** |

---

## 12. Launch Configuration

### VS Code
- `launch.json` file with configurable launch profiles.
- Supports multiple configurations (launch, attach, etc.).
- Environment variables, arguments, working directory, pre-launch tasks.
- Configuration picker in the debug toolbar.

### VGS IDE
- `DebugConfiguration` class with Program, WorkingDirectory, Arguments, Environment, StopOnEntry.
- Configuration built dynamically from project settings.
- Build-before-debug flow: `StartDebuggingAsync()` builds first, then launches.
- Debug/Release configuration selector in toolbar.
- **No launch.json equivalent** -- configuration is project-driven.
- **No attach to process** support.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Configurable launch profiles | Yes (launch.json) | No (project-driven) |
| Multiple configurations | Yes | Debug/Release only |
| Environment variables | Yes | Yes (in DebugConfiguration) |
| Arguments | Yes | Yes |
| Pre-launch tasks (build) | Yes (preLaunchTask) | Yes (automatic build) |
| Attach to process | Yes | **No** |
| StopOnEntry | Yes | Yes (in DebugConfiguration) |

---

## 13. Disassembly View

### VS Code
- Disassembly view for stepping through assembly instructions.
- Supports breakpoints at the instruction level.
- Available when the debug adapter supports the `disassemble` request.

### VGS IDE
- **Not implemented.** No disassembly view or instruction-level debugging.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Disassembly view | Yes (adapter-dependent) | **No** |

---

## 14. Memory Inspector

### VS Code
- Memory viewer for inspecting raw memory.
- Configurable display format.
- Address navigation.
- In-place memory editing (if adapter supports `WriteMemoryRequest`).

### VGS IDE
- **Not implemented.** No memory inspection UI.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Memory inspector | Yes (adapter-dependent) | **No** |

---

## 15. Multi-Session / Multi-Target Debugging

### VS Code
- Compound launch configurations to debug multiple targets simultaneously.
- Each session shown as a top-level item in the Call Stack.
- Independent stepping per session.

### VGS IDE
- **Not implemented.** Single debug session only.

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Multi-session debugging | Yes | **No** |
| Compound launch configs | Yes | **No** |

---

## Summary Scorecard

| Category | VS Code | VGS IDE | VGS Parity |
|----------|---------|---------|------------|
| Debug Toolbar | 7 buttons + floating | 6 buttons, embedded | ~85% (missing Restart button) |
| Line Breakpoints | Full | Full | 100% |
| Conditional Breakpoints | Full | Full | 100% |
| Logpoints | Full | Full | 100% |
| Function Breakpoints | Full | Full | 100% |
| Data Breakpoints | Full | Full | 100% |
| Inline/Column Breakpoints | Yes | No UI | 20% |
| Triggered Breakpoints | Yes | No | 0% |
| Exception Breakpoints | Full | Full | 100% |
| Variables Panel | Full + Set Value | Full, no Set Value | 85% |
| Watch Expressions | Full + persist | Full, no persist | 85% |
| Call Stack | Full + multi-thread | Single thread | 70% |
| Debug Console/REPL | Full + autocomplete | Basic REPL | 60% |
| Inline Variable Values | Yes | Yes | 95% |
| Debug Hover (Data Tips) | Expandable tree | Flat value only | 70% |
| Execution Control | Full | Full | 95% |
| Execution Line Highlight | Full | Full | 100% |
| Debug Output | Full | Full | 90% |
| Launch Configuration | launch.json | Project-driven | 50% |
| Disassembly View | Yes | No | 0% |
| Memory Inspector | Yes | No | 0% |
| Multi-Session Debug | Yes | No | 0% |

### Overall Estimated Parity: ~72%

### Strengths of VGS IDE Debug UX
- Breakpoint system is very complete: line, conditional, hit count, logpoint, function, data, and exception breakpoints are all implemented.
- Inline debug values with intelligent source scanning (finding variable references near stop line).
- Debug hover with expression evaluation (DataTip system).
- Run to Cursor and Set Next Statement both implemented.
- Dedicated Immediate Window with command history.
- Automatic build-before-debug workflow.
- Rich breakpoint condition dialog with validation.

### Key Gaps to Address
1. **Restart Debugging** -- IDebugService has `RestartAsync()` but no toolbar button or menu command.
2. **Set Variable Value** -- No UI to modify variables during debugging.
3. **Debug Hover Expansion** -- Cannot drill into nested objects in hover tooltips.
4. **Debug Console Autocomplete** -- Immediate Window lacks IntelliSense.
5. **Multi-Thread Call Stack** -- Only uses default thread ID 1.
6. **Watch Persistence** -- Watch expressions not saved across sessions.
7. **Attach to Process** -- No ability to attach debugger to a running process.
8. **Column Breakpoints** -- DAP model supports it but no UI to set them.
9. **Triggered Breakpoints** -- Not supported at all.
10. **Disassembly / Memory** -- Advanced low-level debugging not available.

---

*Sources:*
- [Debug code with Visual Studio Code](https://code.visualstudio.com/docs/debugtest/debugging)
- [Debugger Extension API](https://code.visualstudio.com/api/extension-guides/debugger-extension)
- [Continue, Step Over, Step Into and Step Out explained](https://pawelgrzybek.com/continue-step-over-step-into-and-step-out-actions-in-visual-studio-code-debugger-explained/)
- [Watch And Call Stack | VS Code Tutorial](https://www.swiftorial.com/tutorials/development_tools/vs_code/debugging/watch_and_call_stack)
- [VS Code Inline Debug Values Issue #267564](https://github.com/microsoft/vscode/issues/267564)
- [How to Use Breakpoints in VS Code](https://www.alphr.com/vs-code-use-breakpoints/)
- [Debugging with VS Code's Built-In Tools](https://blog.openreplay.com/debugging-vs-code-tools/)
