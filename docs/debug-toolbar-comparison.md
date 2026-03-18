# Debug Toolbar and Control Flow Comparison: VS Code vs VGS IDE

## Debug Toolbar Buttons

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Continue (F5)** | Play button; resumes execution to next breakpoint or end | `ContinueCommand` bound to F5; toolbar button visible when `IsPaused` | None |
| **Pause** | Pause button; suspends running program | `PauseCommand`; toolbar button visible when `IsDebugging` and enabled when `!IsPaused` | None |
| **Step Over (F10)** | Executes current line, skips into function calls | `StepOverCommand` bound to F10; toolbar button visible when `IsPaused` | None |
| **Step Into (F11)** | Executes current line, enters function calls | `StepIntoCommand` bound to F11; toolbar button visible when `IsPaused` | None |
| **Step Out (Shift+F11)** | Runs to end of current function and returns to caller | `StepOutCommand` bound to Shift+F11; toolbar button visible when `IsPaused` | None |
| **Restart (Ctrl+Shift+F5)** | Stops and re-launches debug session with same config | `IDebugService.RestartAsync()` exists in interface but **no toolbar button and no menu item** are wired | **Gap**: API exists but no UI or keybinding exposes it |
| **Stop (Shift+F5)** | Red square; terminates debug session | `StopDebuggingCommand` bound to Shift+F5; toolbar button visible when `IsDebugging` | None |
| **Start Debugging (F5)** | Launches debug session | `StartDebuggingCommand` bound to F5; toolbar button visible when `!IsDebugging` | None |
| **Start Without Debugging (Ctrl+F5)** | Runs program without attaching debugger | `StartWithoutDebuggingCommand` bound to Ctrl+F5; menu item present | None |

## Toolbar Appearance and Behavior

| Aspect | VS Code | VGS IDE | Gap |
|--------|---------|---------|-----|
| **Position** | Floating bar at top-center of editor; can be docked to Run/Debug view or hidden via `debug.toolBarLocation` setting | Fixed in the main toolbar strip (docked at top, inline with Build/Save buttons) | **Gap**: Not floating; no option to reposition or hide independently |
| **Visibility** | Appears only when a debug session is active; disappears when session ends | Buttons use `IsVisible` bindings: Start button hides during debugging; step/pause/stop buttons show during debugging | Comparable; both show/hide contextually |
| **Draggable** | Horizontally draggable (Ctrl+click+drag); limited vertical movement | Not draggable; fixed position in toolbar | **Gap**: No drag support |
| **Docking options** | Floating (default), docked in sidebar, hidden, or in command center | Always docked in top toolbar | **Gap**: No configurable placement |
| **Debug status indicator** | Status bar shows debug state; debug console shows output | `DebugStatusText` label in toolbar shows "Running"/"Paused"/"Stopped" | None |

## Extended Debug Commands

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Run to Cursor** | Right-click context menu; no default keybinding | `RunToCursorCommand` bound to Ctrl+F10; in Debug menu | **VGS advantage**: Has a dedicated keybinding |
| **Set Next Statement** | Not natively supported (some debug adapters may support via `goto` request) | `SetNextStatementCommand` bound to Ctrl+Shift+F10; in Debug menu | **VGS advantage**: Natively supported |
| **Run in External Console** | Configured via `launch.json` (`"console": "externalTerminal"`) | `RunInExternalConsoleCommand` bound to Ctrl+Shift+F5; in Debug menu | None; different approach |
| **Attach to Process** | Supported via `"request": "attach"` in launch.json | Not implemented | **Gap**: No attach-to-process support |
| **Multi-target / Compound Debugging** | Supports compound launch configs to debug multiple targets simultaneously | Not implemented | **Gap**: Single session only |
| **Hot Reload** | Supported for some runtimes (e.g., .NET Hot Reload) | Not implemented | **Gap**: No hot reload |

## Breakpoint Types

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Line Breakpoints** | Click gutter or F9 | `ToggleBreakpointCommand` bound to F9 | None |
| **Conditional Breakpoints** | Right-click gutter > "Conditional Breakpoint"; expression-based | Full condition dialog (`BreakpointConditionDialog`) with expression field and validation | None |
| **Hit Count Breakpoints** | Right-click gutter > "Conditional Breakpoint" > Hit Count tab | Supported via `HitCondition` field in condition dialog | None |
| **Logpoints** | Right-click gutter > "Logpoint"; logs message without pausing | Supported via `LogMessage` field in condition dialog with `{expression}` interpolation and validation | None |
| **Function Breakpoints** | Via "Add Function Breakpoint" in Breakpoints view | `NewFunctionBreakpointCommand` bound to Ctrl+Shift+F9; with condition support | None |
| **Data Breakpoints** | Supported for some debug adapters; break on variable read/write | `IDebugService.SetDataBreakpointsAsync()` and `GetDataBreakpointInfoAsync()` with read/write/readWrite access types; `DataBreakpointItem` in Breakpoints panel | None (API parity) |
| **Exception Breakpoints** | Exception Breakpoints section in Run/Debug sidebar | `ShowExceptionSettingsCommand` (Ctrl+Alt+X); dedicated `ExceptionSettingsDialog` with filter configuration | None |
| **Inline Breakpoints** | Shift+F9 to set breakpoint at specific column within a line | Not implemented | **Gap**: No inline (column-level) breakpoints |

## Debug Panels / Views

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Variables Panel** | Locals, closure, and global variable groups in sidebar | Variables panel (Ctrl+Alt+V); shows scopes with `GetScopesAsync` / `GetVariablesAsync` | None |
| **Watch Panel** | Add expressions to watch; evaluated on each stop | Watch panel (Ctrl+Alt+W); `WatchViewModel` with expression evaluation | None |
| **Call Stack Panel** | Shows all threads and their stack frames; click to navigate | Call Stack panel (Ctrl+Alt+C); `CallStackViewModel` with frame navigation | None |
| **Breakpoints Panel** | Lists all breakpoints with enable/disable toggles | Breakpoints panel (Ctrl+Alt+B); line, function, and data breakpoints with enable/disable | None |
| **Debug Console / Immediate** | REPL-style console for evaluating expressions during debugging | Immediate Window (Ctrl+Alt+I); `ImmediateWindowViewModel` with `EvaluateAsync` | None |
| **Loaded Modules** | Shows loaded modules/DLLs | Not implemented | **Gap**: No modules view |
| **Threads Panel** | Shows all threads when multi-threaded | Not implemented as separate panel (single-thread `threadId=1` default) | **Gap**: No multi-thread view |
| **Inline Debug Values** | Via "Inline Values" extension or built-in (editor.inlineSuggest) | Built-in `ShowInlineDebugValuesAsync`; displays variable values inline at each paused line | None |

## Debug Output

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Debug Console** | Dedicated panel; shows program output and debug adapter messages | Output panel with `Debug` category; `OutputReceived` event handler | None |
| **Output categories** | stdout, stderr, console, telemetry | `DebugOutputEventArgs.Category` ("console" default) | Comparable |
| **Program input (stdin)** | Supported via integrated/external terminal | `SendInputAsync()` on `IDebugService` | None |

## Summary of Gaps

### VGS IDE is missing (relative to VS Code):
1. **Restart button** in toolbar (API exists at `IDebugService.RestartAsync()` but no UI)
2. **Floating/draggable toolbar** -- toolbar is fixed in the main toolbar strip
3. **Configurable toolbar position** (floating, docked, hidden, command center)
4. **Attach to process** debugging
5. **Multi-target / compound** debug sessions
6. **Hot reload** during debugging
7. **Inline (column-level) breakpoints**
8. **Loaded Modules** panel
9. **Multi-thread** panel and thread switching

### VGS IDE advantages over VS Code:
1. **Run to Cursor** has a dedicated keybinding (Ctrl+F10) and menu item
2. **Set Next Statement** (Ctrl+Shift+F10) -- move execution pointer without executing; not natively available in VS Code
3. **Run in External Console** as a first-class menu command (Ctrl+Shift+F5)
4. **Data breakpoints** with a dedicated UI panel section (VS Code support varies by debug adapter)
