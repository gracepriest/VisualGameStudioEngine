# Breakpoint Feature Comparison: VS Code vs Visual Game Studio IDE

This document compares breakpoint and debugging features between Visual Studio Code and the Visual Game Studio (VGS) IDE.

## Feature Matrix

| Feature | VS Code | VGS IDE | Notes |
|---------|---------|---------|-------|
| **Line breakpoints** | Yes | Yes | Both support click-gutter to toggle |
| **Enable/disable breakpoints** | Yes | Yes | VGS has per-breakpoint checkbox + bulk enable/disable all |
| **Conditional breakpoints (expression)** | Yes | Yes | VGS validates balanced parens, quotes, operator placement |
| **Hit count breakpoints** | Yes | Yes | VGS supports `N`, `>N`, `>=N`, `=N`, `%N` (modulo) formats |
| **Logpoints** | Yes | Yes | VGS supports `{expression}` interpolation in log messages |
| **Function breakpoints** | Yes | Yes | VGS has dedicated panel with add/remove/toggle/condition editing |
| **Data breakpoints (watchpoints)** | Yes | Yes | VGS model supports `DataId`, `VariableName`, `AccessType`, condition, hit count |
| **Exception breakpoints** | Yes | Yes | VGS has categorized tree: All, Runtime, IO, User-defined |
| **Inline breakpoints** | Yes | No | VS Code supports column-level breakpoints for minified code |
| **Breakpoint validation (verified/unverified)** | Yes | Yes | VGS tracks `IsVerified` and `Message` per breakpoint from DAP |
| **Edit breakpoint condition dialog** | Yes | Yes | VGS has tabbed dialog: Conditional / Hit Count / Log Message |
| **Breakpoint list panel** | Yes | Yes | VGS has dedicated panel with Function BPs, Line BPs, and context menus |
| **Breakpoint persistence** | Yes | Yes | VGS saves to `.vgs/breakpoints.json` per project |
| **Remove all breakpoints** | Yes | Yes | Separate "remove all" for line, function, and data breakpoints |
| **Go to source from breakpoint** | Yes | Yes | VGS supports double-click and context menu navigation |
| **Triggered breakpoints (dependent)** | Yes | No | VS Code can make BP X only activate after BP Y hits |
| **Wait for breakpoint** | Yes | No | VS Code condition type that waits for another breakpoint |

## Detailed Comparison

### Line Breakpoints

**VS Code**: Click the editor gutter (left margin) to toggle a red dot. Breakpoints can be set on any line; the debugger verifies whether the line maps to executable code and may adjust the position.

**VGS IDE**: Click the `BreakpointMargin` (16px-wide gutter, DPI-aware) to toggle. The margin renders distinct visual indicators per kind:
- Normal: filled red circle
- Conditional: filled red diamond (rotated square)
- Hit count: red circle with white "+" overlay
- Logpoint: red diamond with white horizontal line overlay
- Disabled: gray versions of the above
- Current execution line: yellow right-pointing arrow

All breakpoint types support an enabled/disabled state with distinct rendering.

### Conditional Breakpoints

**VS Code**: Right-click a breakpoint or use the inline widget to add an expression. The debugger evaluates the expression each time the breakpoint is hit; execution pauses only when the expression is truthy.

**VGS IDE**: The `BreakpointConditionDialogViewModel` provides a tabbed dialog with three modes:
1. **Conditional Expression** -- validates balanced parentheses, balanced brackets, balanced string quotes, rejects doubled operators (`++`, `== ==`), trailing binary operators (`x +`), and leading binary operators (`* x`).
2. **Hit Count** -- accepts plain number (`5`), comparison (`>5`, `>=5`, `=5`), or modulo (`%5`).
3. **Log Message** -- validates balanced `{}` braces, rejects nested braces and empty `{}` interpolation.

The condition, hit count, and log message are transmitted to the DAP server as `condition`, `hitCondition`, and `logMessage` fields in the `setBreakpoints` request.

### Hit Count Breakpoints

**VS Code**: Supports hit count conditions as a separate breakpoint type. Typical formats: a plain integer (break after N hits) or expressions like `>5`, `>=10`, `%3`.

**VGS IDE**: Same format support. The dialog validates:
- Plain integer (must be positive)
- `>=N` (non-negative)
- `>N`, `<N`, `=N` (non-negative)
- `%N` (positive, rejects modulo-by-zero)

Rendered in the gutter as a red circle with a white "+" symbol.

### Logpoints

**VS Code**: A logpoint logs a message to the Debug Console without pausing. Expressions inside `{curly braces}` are interpolated. The gutter shows a diamond-shaped icon.

**VGS IDE**: Same behavior. The condition dialog validates that braces are balanced, not nested, and not empty. Logpoints are rendered in the gutter as a red diamond with a white horizontal line. The `logMessage` field is sent to the DAP server via the standard `setBreakpoints` request.

### Function Breakpoints

**VS Code**: Created from the BREAKPOINTS section header "+" button by entering a function name. The debugger resolves the name to a code location.

**VGS IDE**: The Breakpoints panel has a dedicated "Function Breakpoints" section with:
- Text input field with Enter key and "+" button to add
- Per-breakpoint checkbox to enable/disable
- Context menu: Edit Condition, Enable/Disable, Remove
- Condition editing via `EditFunctionConditionCommand` (supports condition and hit count)
- "Remove All" button in the section header
- Function name validation: must start with letter or underscore, allows dots for qualified names (`Module.Function`)
- Synced to DAP via `setFunctionBreakpoints` request

**DAP backend limitation**: The `NetDebugAdapter.HandleSetFunctionBreakpoints` method currently returns an empty breakpoint list -- function breakpoints are accepted by the IDE but not yet bound by the CLR debugger.

### Data Breakpoints (Watchpoints)

**VS Code**: Set from the VARIABLES view context menu with "Break on Value Change/Read/Access". The debugger pauses when the variable's value changes, is read, or is accessed.

**VGS IDE**: The `BreakpointsViewModel` has full data breakpoint infrastructure:
- `DataBreakpointItem` model with `DataId`, `VariableName`, `AccessType` (write/read/access), condition, hit count
- Add, remove, toggle, remove-all commands
- Sync to DAP via `SetDataBreakpointsAsync`
- Persistence in `.vgs/breakpoints.json`
- Verified/unverified status tracking

**Current limitation**: The Breakpoints panel AXAML (`BreakpointsView.axaml`) does not yet include a UI section for data breakpoints -- only function breakpoints and line breakpoints are shown. The data breakpoint infrastructure exists in the ViewModel but lacks a corresponding View section.

### Exception Breakpoints

**VS Code**: The BREAKPOINTS panel shows exception breakpoint filters (typically "All Exceptions" and "Uncaught Exceptions"). Some debuggers support per-exception-type configuration.

**VGS IDE**: A dedicated `ExceptionSettingsDialog` provides:
- Categorized tree view: All Exceptions, Runtime Exceptions (11 types), IO Exceptions (4 types), User Exceptions
- Per-exception two toggles: "Break When Thrown" (first-chance) and "Break When User-Unhandled"
- Search/filter across exception names and descriptions
- Add/remove custom user-defined exceptions
- Enable All / Disable All / Reset to Defaults buttons
- Propagation: enabling "break when thrown" on a parent category propagates to all children

The DAP adapter declares two exception filters: `all` ("All Exceptions", default off) and `uncaught` ("Uncaught Exceptions", default on). The IDE maps its richer category model to these DAP filters.

### Inline Breakpoints

**VS Code**: Shift+F9 or the command palette to set a breakpoint at a specific column within a line. Useful for minified code or lines with multiple statements separated by semicolons/commas.

**VGS IDE**: Not implemented. There is no column-level breakpoint support in the margin rendering, data model, or DAP adapter. The `BreakpointItem` model tracks only `Line`, not column.

### Breakpoint Validation

**VS Code**: Breakpoints that cannot be mapped to executable code are shown as gray/hollow circles (unverified). Once the debugger confirms binding, they become solid (verified).

**VGS IDE**: The `BreakpointItem` has `IsVerified` and `Message` properties. When `setBreakpoints` returns, the `OnBreakpointsChanged` handler updates each breakpoint's verified status. The margin renders disabled/unverified breakpoints in gray (`#808080`) with a gray border (`#505050`). The `Message` property can convey why a breakpoint was not verified.

### Breakpoint Persistence

**VS Code**: Breakpoints are persisted in the workspace `.vscode` folder as part of launch configuration state.

**VGS IDE**: All breakpoint types (line, function, data) are serialized to `.vgs/breakpoints.json` inside the project directory. The JSON includes:
- Source breakpoints: file path, line, enabled state, condition, hit count, log message
- Function breakpoints: name, enabled, condition, hit count
- Data breakpoints: data ID, variable name, access type, enabled, condition, hit count

Breakpoints are loaded on project open and saved on every change.

### Triggered / Dependent Breakpoints

**VS Code**: A breakpoint can be configured to only activate after another specific breakpoint has been hit. This is set via the "Wait for Breakpoint" condition type.

**VGS IDE**: Not implemented. There is no dependency or triggering mechanism between breakpoints.

## Summary

| Metric | VS Code | VGS IDE |
|--------|---------|---------|
| Breakpoint types supported | 8 | 6 |
| Missing features | -- | Inline breakpoints, triggered/dependent breakpoints |
| Unique strengths | Inline breakpoints, triggered breakpoints, mature ecosystem | Richer condition validation, categorized exception tree with per-type control, DPI-aware custom gutter rendering, distinct visual indicators per breakpoint kind |
| DAP protocol compliance | Full | High (function breakpoints accepted but not yet bound at CLR level; data breakpoint UI incomplete) |
| Persistence | `.vscode/` workspace state | `.vgs/breakpoints.json` with full serialization of all types |

The VGS IDE covers the six most commonly used breakpoint types (line, conditional, hit count, logpoint, function, exception) with feature parity or richer UI than VS Code. The two gaps are inline (column-level) breakpoints and triggered/dependent breakpoints. The data breakpoint backend is implemented but awaits a View in the Breakpoints panel.

## Key Source Files

- `VisualGameStudio.Shell/ViewModels/Panels/BreakpointsViewModel.cs` -- All breakpoint management, persistence, DAP sync
- `VisualGameStudio.Editor/Margins/BreakpointMargin.cs` -- Gutter rendering with 4 breakpoint kinds
- `VisualGameStudio.Shell/ViewModels/Dialogs/BreakpointConditionDialogViewModel.cs` -- Condition/hit count/logpoint editing with validation
- `VisualGameStudio.Shell/ViewModels/Dialogs/FunctionBreakpointDialogViewModel.cs` -- Function name input with validation
- `VisualGameStudio.Shell/ViewModels/Dialogs/ExceptionSettingsViewModel.cs` -- Categorized exception breakpoint configuration
- `VisualGameStudio.Shell/Views/Panels/BreakpointsView.axaml` -- Breakpoints panel UI (function + line breakpoints)
- `BasicLang/Debugger/NetDebugAdapter.cs` -- DAP server handling setBreakpoints, setFunctionBreakpoints, setExceptionBreakpoints

Sources:
- [VS Code Debugging Documentation](https://code.visualstudio.com/docs/debugtest/debugging)
