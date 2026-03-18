# Debug Output & Console Comparison: VS Code vs Visual Game Studio IDE

## Overview

This document compares the debug output and console features between VS Code and the Visual Game Studio (VGS) IDE, covering how each environment captures, routes, and displays program output, debug messages, and interactive expression evaluation during debugging sessions.

---

## 1. Output Panels / Channels

### VS Code

VS Code provides three distinct output destinations during debugging:

| Panel | Purpose |
|-------|---------|
| **Debug Console** | DAP `output` events (categories: `console`, `stdout`, `stderr`), interactive REPL for expression evaluation, logpoint messages |
| **Output Panel** | Extension host logs, language server logs, task output; selectable via dropdown per extension/channel |
| **Terminal (Integrated)** | Program stdin/stdout when `"console": "integratedTerminal"` is set in `launch.json`; full PTY with ANSI color support |

The `internalConsoleOptions` launch configuration attribute controls Debug Console visibility. Programs can be routed to either the Debug Console (`internalConsole`) or an integrated/external terminal, which is important for programs requiring interactive stdin.

### VGS IDE

The VGS IDE has two primary output-related panels plus a terminal:

| Panel | Purpose |
|-------|---------|
| **Output Panel** (`OutputPanelViewModel`) | All debug output (stdout, stderr, DAP events) routed here; switchable between Build and General categories; includes stdin input area during running sessions |
| **Immediate Window** (`ImmediateWindowViewModel`) | Interactive expression evaluation (REPL) during paused debug sessions |
| **Terminal** (`TerminalViewModel`) | Independent PowerShell/bash shell; not integrated with debug sessions |

**Key difference**: VS Code's Debug Console combines output display and REPL in one panel. The VGS IDE separates these into the Output Panel (display) and Immediate Window (REPL).

---

## 2. stdout / stderr Capture

### VS Code

- **Debug Console mode** (`"console": "internalConsole"`): stdout and stderr from the debuggee are captured via DAP `output` events and displayed in the Debug Console. stderr is typically shown in a different color (red/orange).
- **Integrated Terminal mode** (`"console": "integratedTerminal"`): stdout/stderr go directly to the terminal with full ANSI escape code support. The Debug Console still shows DAP-level messages.
- Output categories from DAP: `stdout`, `stderr`, `console`, `important`, `telemetry`.
- Each category can be styled differently (color, icon).

### VGS IDE

- **With debugging** (DAP mode): The `DebugService` communicates with `BasicLang.exe --debug-adapter` via DAP. Output events from the debug adapter are received in `ProcessMessage()` and forwarded through the `OutputReceived` event. The `MainWindowViewModel.OnDebugOutput()` handler routes all output to `OutputPanel.AppendOutput()`.
  - Categories received: `stdout`, `stderr`, `console` -- but **all are displayed identically** as plain monospace text with no color differentiation.
- **Without debugging** (direct run): `StartWithoutDebuggingAsync()` launches the target process directly, captures `OutputDataReceived` (stdout) and `ErrorDataReceived` (stderr), and emits them as `DebugOutputEventArgs` with category `"stdout"` or `"stderr"`.
  - Exit code is appended: `"Program exited with code N"`.
- **No ANSI escape code processing**: The output TextBox renders raw text.

**Gaps**:
- No visual distinction between stdout and stderr (no color coding).
- No ANSI escape sequence interpretation.
- No separate output channel for DAP diagnostic messages vs program output.

---

## 3. Debug Console / Interactive REPL

### VS Code

- **Location**: Built into the Debug Console panel (same panel as output display).
- **Expression evaluation**: Type any expression at the prompt; evaluated via DAP `evaluate` request with `context: "repl"`.
- **Autocomplete**: IntelliSense-style completions in the REPL input, powered by DAP `completions` request.
- **Multi-line input**: Shift+Enter for additional lines, Enter to submit.
- **Context**: Expressions evaluated in the context of the current stack frame.
- **Output interleaving**: REPL results are interleaved with program output in the same panel, preserving chronological order.
- **Rich formatting**: Objects can be expanded inline; hover for type information.

### VGS IDE

- **Location**: Separate **Immediate Window** panel (`ImmediateWindowView`).
- **Expression evaluation**: Prefix with `?` (optional) and press Enter. Sent via `IDebugService.EvaluateAsync()` which issues a DAP `evaluate` request with `context: "watch"`.
- **Autocomplete**: None -- plain TextBox input with no IntelliSense.
- **Multi-line input**: Not supported (single TextBox input).
- **Context**: Requires debugger to be in `Paused` state; does not specify `frameId` by default (uses top frame).
- **Command history**: Up/Down arrow keys navigate previous commands.
- **Built-in commands**: `clear`, `help`.
- **Result display**: Shows `result (Type)` format for evaluated expressions.
- **Error handling**: Displays `"Error: Not currently debugging"` or `"Error: Debugger must be paused"` when preconditions are not met.

**Gaps**:
- No autocomplete/IntelliSense in the expression input.
- No multi-line expression support.
- No object expansion or rich formatting of results.
- Cannot select a specific stack frame for evaluation context.
- REPL results are not interleaved with program output (separate panels).

---

## 4. Logpoint Output

### VS Code

- **Definition**: Set via the breakpoint gutter context menu or by editing a breakpoint and choosing "Log Message".
- **Format**: Plain text with `{expression}` interpolation. Example: `Value of x is {x}` evaluates `x` in the current scope and logs the result.
- **Display**: Logpoint output appears in the Debug Console with a distinctive style (often preceded by a diamond icon in the gutter).
- **Behavior**: Does not pause execution; the program continues running.
- **Source location**: Clicking the log message navigates to the source line.

### VGS IDE

- **Definition**: Supported in the DAP layer. `SourceBreakpoint.LogMessage` is passed through `SetBreakpointsAsync()`. The `DebugSession` creates `BreakpointType.Logpoint` entries.
- **Format**: Same `{expression}` interpolation format. `Breakpoint.FormatLogMessage()` evaluates expressions within braces using `DebuggableInterpreter.EvaluateExpression()`.
- **Display**: The `DebugSession.OnLogpointHit()` emits a DAP `output` event with `category: "console"` and format `"[Logpoint] message\n"`, including source file path and line number. This arrives at the Output Panel as plain text.
- **Behavior**: Does not pause execution (`Breakpoint.ShouldBreak()` returns `false` for logpoints).
- **Source location**: Source path and line are included in the DAP event but **the Output Panel does not render these as clickable links**.

**Gaps**:
- No clickable source location in logpoint output.
- No visual distinction between logpoint output and regular program output (no icon, no special formatting).

---

## 5. Exception Output Formatting

### VS Code

- **Exception stop**: When breaking on an exception, the Debug Console shows the exception type, message, and stack trace with syntax highlighting.
- **Exception widget**: An inline widget appears in the editor at the throw site showing exception details.
- **Configurable filters**: "Caught Exceptions", "Uncaught Exceptions", and per-type filters via the Breakpoints panel.
- **Output category**: Exceptions use the `"stderr"` or dedicated exception styling.

### VGS IDE

- **Exception stop reasons**: The `DebugService` recognizes `StopReason.Exception` from DAP stopped events. The `StoppedEventArgs` includes `Description` and `Text` fields.
- **Exception filters**: `SetExceptionBreakpointsAsync()` sends DAP `setExceptionBreakpoints` with filter IDs like `"all"`, `"uncaught"`. The interpreter-based `DebugSession` supports `ExceptionBreakMode` (Never, Always, Unhandled, UserUnhandled).
- **Exception display**: Exception information is delivered through DAP output events with `category: "stderr"`, appearing as plain text in the Output Panel.
- **No inline exception widget**: The editor does not show an inline exception details overlay.
- **Exception breakpoint types**: Supported in `Breakpoint.cs` with `BreakpointType.Exception`, `ExceptionType` (specific type or `"*"` for all), and `ExceptionBreakMode`.

**Gaps**:
- No inline exception widget in the editor.
- No syntax-highlighted stack trace rendering.
- No distinct visual treatment for exception output vs regular stderr.

---

## 6. Terminal Integration

### VS Code

- **Debug terminal modes**: `internalConsole` (Debug Console), `integratedTerminal` (shared terminal), `externalTerminal` (new window).
- **Full PTY**: Integrated terminal supports full pseudo-terminal capabilities -- ANSI colors, cursor control, interactive input (e.g., password prompts).
- **Automatic terminal creation**: A dedicated debug terminal is created per session.
- **Terminal IntelliSense**: Fully enabled as of VS Code 1.108+ (December 2025).

### VGS IDE

- **Terminal panel**: Independent `TerminalViewModel` that launches PowerShell (Windows) or bash (other). Uses `Process` with redirected streams -- **not a PTY**.
- **Not linked to debug sessions**: The terminal does not automatically start or switch when debugging begins.
- **Debug stdin**: Program input during "Run without Debugging" is handled via the Output Panel's input area (visible only when `IsInputEnabled` is true). Input is sent via `DebugService.SendInputAsync()` which writes to the process stdin.
- **Buffer management**: Terminal has a 100KB buffer limit with automatic truncation at line boundaries.

**Gaps**:
- No PTY support (no ANSI colors, no interactive terminal programs).
- Terminal is not integrated with debug sessions.
- No automatic debug terminal creation.
- No terminal IntelliSense.

---

## 7. Output Categories and Routing

### VS Code

| DAP Category | Destination | Styling |
|-------------|-------------|---------|
| `stdout` | Debug Console or Terminal | Default text color |
| `stderr` | Debug Console | Red/error color |
| `console` | Debug Console | Dimmed/gray |
| `important` | Debug Console | Bold/highlighted |
| `telemetry` | Hidden (extension telemetry) | N/A |

### VGS IDE

| Source | Destination | Styling |
|--------|-------------|---------|
| DAP `stdout` events | Output Panel | Monospace, #CCCCCC on #1E1E1E |
| DAP `stderr` events | Output Panel | Same as stdout (no distinction) |
| DAP `console` events | Output Panel | Same as stdout |
| `IOutputService` Build messages | Output Panel (Build tab) | Same styling |
| `IOutputService` Debug messages | Output Panel (category filter) | Same styling |
| Logpoint output | Output Panel | Prefixed with `[Logpoint]` |
| Error messages | Output Panel | Prefixed with `[ERROR]` (in OutputService storage only; no visual difference in rendering) |

**Gaps**:
- Single text color for all output types -- no red for errors, no dimming for console messages.
- `OutputEventArgs.IsError` flag exists but is not used by the view layer for styling.
- Only two selectable categories in the UI (Build, General) despite four defined in `OutputCategory` enum (General, Build, Debug, LanguageServer).

---

## 8. Summary Table

| Feature | VS Code | VGS IDE | Status |
|---------|---------|---------|--------|
| Program stdout capture | Yes | Yes | Parity |
| Program stderr capture | Yes | Yes | Parity |
| stdout/stderr color distinction | Yes (red for stderr) | No (same color) | Gap |
| Debug Console REPL | Yes (in Debug Console) | Yes (Immediate Window) | Partial |
| REPL autocomplete | Yes (DAP completions) | No | Gap |
| Multi-line REPL input | Yes (Shift+Enter) | No | Gap |
| REPL command history | Yes | Yes (Up/Down arrows) | Parity |
| Output + REPL in same panel | Yes | No (separate panels) | Different design |
| Logpoint support | Yes | Yes | Parity |
| Logpoint {expression} interpolation | Yes | Yes | Parity |
| Clickable source location in logpoint output | Yes | No | Gap |
| Exception breakpoint filters | Yes (caught/uncaught/per-type) | Yes (all/uncaught/per-type) | Parity |
| Inline exception widget | Yes | No | Gap |
| Exception stack trace formatting | Yes (syntax highlighted) | No (plain text) | Gap |
| ANSI escape code support | Yes (terminal) | No | Gap |
| PTY terminal | Yes | No (redirected streams) | Gap |
| Debug-integrated terminal | Yes (integratedTerminal) | No (independent terminal) | Gap |
| Terminal for program stdin | Yes | Partial (Output Panel input) | Partial |
| Object expansion in output | Yes (expandable trees) | No | Gap |
| Output category filtering | Yes (per-extension dropdown) | Partial (Build/General only) | Gap |
| Output text search/filter | Yes (Ctrl+F in panels) | No | Gap |
| Auto-scroll to latest output | Yes | Partial (no explicit auto-scroll) | Gap |
| Copy output text | Yes | Yes (read-only TextBox) | Parity |
| Clear output | Yes | Yes | Parity |
| Word wrap toggle | Yes | No (NoWrap fixed) | Gap |

---

## 9. Recommendations

### High Priority
1. **Color-code stderr output**: Use the existing `IsError` flag on `OutputEventArgs` or the DAP category to render stderr in red/orange and console messages in a dimmer color.
2. **Expose Debug and LanguageServer output categories**: The `OutputCategory` enum already defines Debug and LanguageServer but the UI only shows Build and General tabs.
3. **Add auto-scroll**: The Output Panel and Immediate Window should auto-scroll to the bottom when new output arrives.

### Medium Priority
4. **REPL autocomplete**: Send DAP `completions` requests from the Immediate Window to provide IntelliSense-style suggestions.
5. **Clickable source locations**: Parse logpoint output and compilation error messages for file:line patterns and make them navigable.
6. **Stack frame selector for REPL**: Allow selecting which call stack frame to evaluate expressions in.
7. **Word wrap toggle**: Add a button to toggle `TextWrapping` between `NoWrap` and `Wrap`.

### Lower Priority
8. **Multi-line REPL input**: Support Shift+Enter for multi-line expressions.
9. **Inline exception widget**: Show exception details inline in the editor at the throw site.
10. **Object expansion**: Render complex evaluation results as expandable trees using `variablesReference` from DAP.
11. **PTY terminal**: Replace redirected-stream terminal with a proper PTY-based terminal emulator for ANSI support.
12. **Debug-integrated terminal**: Automatically launch debug targets in the terminal panel when stdin interaction is needed.
