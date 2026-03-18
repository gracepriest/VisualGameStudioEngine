# Variable Inspection & Watch Comparison: VS Code vs Visual Game Studio IDE

This document compares variable inspection and watch features during debugging between Visual Studio Code and the Visual Game Studio (VGS) IDE.

## Feature Matrix

| Feature | VS Code | VGS IDE | Notes |
|---------|---------|---------|-------|
| **Variables panel (locals)** | Yes | Yes | Both show locals scoped to current stack frame |
| **Variables panel (arguments)** | Yes | Yes | VGS separates locals and arguments into distinct scopes |
| **Variables panel (globals)** | Yes | Partial | VS Code shows via scope; VGS interpreter backend exposes globals but NetDebugAdapter only shows Locals + Arguments scopes |
| **Hierarchical tree view** | Yes | Yes | VGS has `VariableTreeItem` with lazy-load children on expand |
| **Flat list view** | No | Yes | VGS also maintains a parallel flat `VariableItem` list with indentation |
| **Watch panel** | Yes | Yes | Both support adding custom expressions |
| **Watch: add expression** | Yes | Yes | VGS has dedicated `WatchViewModel` with inline editable row |
| **Watch: remove expression** | Yes | Yes | VGS supports remove single + remove all |
| **Watch: edit expression** | Yes | Yes | VGS allows in-place editing of existing watch items |
| **Watch: auto-refresh on pause** | Yes | Yes | VGS refreshes all watches on `Stopped` and `StateChanged(Paused)` events |
| **Watch: error display** | Yes | Yes | VGS shows `<error: message>` with red color via `WatchErrorColorConverter` |
| **Watch: copy value** | Yes | Yes | VGS has `CopyValueAsync` using Avalonia clipboard API |
| **Watch: copy expression** | Partial | Yes | VGS has dedicated `CopyExpressionAsync`; VS Code uses generic copy |
| **Hover evaluation (data tips)** | Yes | Yes | VGS has full `DataTipPopup` with expression, value, type display |
| **Hover: add to watch** | Yes | Yes | VGS data tip has "Add to Watch" button that wires to `WatchViewModel` |
| **Hover: copy value** | Partial | Yes | VGS data tip has dedicated "Copy" button |
| **Hover: rich object expansion** | Yes | No | VS Code shows expandable tree in hover; VGS shows flat text only |
| **Debug Console (REPL)** | Yes | Yes | VGS has `ImmediateWindowViewModel` with `?expression` syntax |
| **Debug Console: command history** | Yes | Yes | VGS supports Up/Down arrow history navigation |
| **Debug Console: multi-line input** | Yes | No | VS Code supports Shift+Enter for multi-line; VGS is single-line |
| **Debug Console: help command** | No | Yes | VGS has built-in `help` command showing usage examples |
| **Debug Console: clear command** | Yes | Yes | Both support clearing output |
| **Inline values** | Yes | Yes | VGS has `InlineDebugValueRenderer` showing values after line end |
| **Variable modification (set value)** | Yes | No | Both DAP adapters report `supportsSetVariable = false` |
| **Expandable objects** | Yes | Yes | Both support drilling into object properties/fields |
| **Expandable arrays** | Yes | Yes | VGS supports array element expansion with 1000-element limit |
| **Stack frame selection** | Yes | Yes | VGS `SetFrameAsync` refreshes variables for selected frame |
| **Expression evaluation** | Yes | Partial | VS Code supports arbitrary expressions; VGS evaluator handles operators but NetDebugAdapter only does variable name lookup |
| **Completions in debug console** | Yes | No | VS Code supports `supportsCompletionsRequest`; VGS does not |
| **Value formatting options** | Yes | No | VS Code supports hex/decimal format toggle; VGS does not |
| **Lazy loading indicator** | Yes | Yes | VGS shows `IsLoading` state during child variable fetch |

## Detailed Comparison

### Variables Panel

**VS Code**: The VARIABLES section in the Run and Debug sidebar shows all variables organized by scope (Locals, Closure, Global, etc.). Variables are displayed in a tree structure where complex objects and arrays can be expanded by clicking the disclosure arrow. Each variable shows its name, current value, and type. The panel updates automatically whenever execution pauses (breakpoint, step, exception). Users can right-click a variable to copy its value, copy it as an expression, or add it to the Watch panel.

**VGS IDE**: The `VariablesViewModel` provides two parallel views of variables:

1. **Tree view** (`VariableTree`) -- Uses `VariableTreeItem` nodes arranged hierarchically. Scope nodes (Locals, Arguments) are auto-expanded. Each variable node stores a `VariablesReference` ID for lazy child loading. When a user expands a node (`OnIsExpandedChanged`), `LoadChildrenAsync` fetches children from the debug service. Scope nodes display a folder icon, expandable objects a package icon, and leaf variables show plain text.

2. **Flat list** (`Variables`) -- A parallel `ObservableCollection<VariableItem>` with indented names (4 spaces) and scope headers marked with a down-arrow character. This provides an alternative simpler view.

The panel listens to `IDebugService.Stopped` and `IDebugService.StateChanged` events. On pause, it calls `RefreshVariablesAsync` which fetches stack frames, selects the current frame (or a user-selected frame via `SetFrameAsync`), retrieves scopes, and then retrieves variables for each scope. When the debug session ends, both collections are cleared.

### Watch Panel

**VS Code**: The WATCH section lets users add custom expressions to monitor. Expressions are evaluated on every debug pause. Users can add watches by clicking the "+" icon or by right-clicking a variable and selecting "Add to Watch". Watches support arbitrary expressions, not just variable names. Values update in real-time as the user steps through code. Right-click context menu provides copy value, copy expression, remove, and remove all options.

**VGS IDE**: The `WatchViewModel` is a separate, full-featured panel with these capabilities:

- **Add expression**: Via text input field (`NewExpression`) or programmatically via `AddExpressionAsync` (used by data tip "Add to Watch" button). Duplicate expressions are rejected. A permanent editable placeholder row at the bottom allows inline entry.
- **Remove**: Single item (`RemoveWatch`) or all items (`RemoveAll`), both preserving the editable placeholder row.
- **Edit in place**: `EditExpression` command toggles an existing watch item back to editable mode.
- **Copy**: Separate `CopyValueAsync` and `CopyExpressionAsync` commands using the Avalonia clipboard API.
- **Auto-refresh**: All non-editable watches are re-evaluated on every `Stopped` event and `StateChanged(Paused)` transition.
- **Error handling**: Failed evaluations set `HasError = true` and display `<error: message>` text. The `WatchErrorColorConverter` renders errors in `#F48771` (red) vs normal values in `#CE9178` (orange).
- **Expandable children**: `WatchPanelItem` has a `Children` collection and `IsExpanded` property, enabling hierarchical display of complex watch results.

The `VariablesViewModel` also contains an embedded watch mechanism (`WatchExpressions` collection with `AddWatchAsync` / `RemoveWatch` / `EvaluateWatchExpressionsAsync`), providing watch functionality integrated directly into the Variables panel. This means VGS offers watch expressions in two places: the dedicated Watch panel and inline within the Variables panel.

### Hover Evaluation (Data Tips)

**VS Code**: When the debugger is paused, hovering over a variable in the editor shows a popup ("data tip") with the variable's current value. For complex objects, the popup contains an expandable tree view similar to the Variables panel, allowing deep inspection without switching panels. The hover uses debouncing to avoid excessive evaluation requests. VS Code reports the `supportsEvaluateForHovers` capability, and the debug adapter evaluates the hovered expression using the `evaluate` DAP request with context `"hover"`.

**VGS IDE**: Hover evaluation follows this event chain:

1. `CodeEditorControl.OnTextAreaPointerMoved` detects mouse movement and starts a debounce timer (`_hoverTimer`).
2. On timer elapsed (`OnHoverTimerElapsed`), the control extracts the word under the cursor using `GetWordAtOffset` and `GetWordAtOffset`.
3. A `DataTipRequested` event fires with the expression, screen coordinates, and line/column.
4. `CodeEditorDocumentView.OnDataTipRequested` forwards to `CodeEditorDocumentViewModel.RequestDataTipEvaluation`.
5. `MainWindowViewModel.OnDataTipEvaluationRequested` calls `IDebugService.EvaluateAsync(expression, frameId)`.
6. The result is emitted as a `DataTipResult` event.
7. `MainWindow.OnDataTipResult` creates and positions a `DataTipPopup` at the hover location.

The `DataTipPopup` is a custom Avalonia `UserControl` with:
- Expression name in yellow (`#DCDCAA`)
- Type in teal (`#4EC9B0`)
- Value in a dark inset border with wrapping text
- "Add to Watch" button (blue, `#0E639C`) that forwards to the Watch panel
- "Copy" button (gray, `#3C3C3C`) for clipboard copy
- Error state: hides "Add to Watch", shows value in red (`#F48771`)

**Key difference**: VS Code's hover popup supports expanding complex objects into a tree. VGS shows a flat text value only -- no drill-down into object properties from the hover popup.

### Debug Console / Immediate Window

**VS Code**: The Debug Console provides a full REPL (Read-Eval-Print Loop) for evaluating expressions during debugging. It is opened with `Ctrl+Shift+Y`. Users can type any expression and press Enter to evaluate it. The console supports:
- Arbitrary expression evaluation (not just variable names)
- Multi-line input via Shift+Enter
- Suggestion completions as you type (if the debug adapter supports `completionsRequest`)
- Full output from the debugged program (stdout/stderr interleaved with evaluation results)
- Object inspection in results (expandable trees)

**VGS IDE**: The `ImmediateWindowViewModel` provides a text-based Immediate Window with:
- **Expression evaluation**: Type an expression (optionally prefixed with `?`) and press Enter. Calls `IDebugService.EvaluateAsync`.
- **Command history**: Up/Down arrow navigation through previous commands (`_commandHistory` list with `_historyIndex` cursor).
- **Built-in commands**: `clear` (clears output), `help` (shows usage reference with examples like `?counter`, `?x + y`, `?CalculateSum(5)`).
- **Session state display**: Shows `[Debugger paused]`, `[Debugger running...]`, `[Debug session ended]` messages.
- **Error handling**: Displays `Error: Not currently debugging` or `Error: Debugger must be paused` when evaluation is not possible.
- **Output format**: Results display as `value (type)` with type information when available.

**Key differences**:
- VS Code supports multi-line input (Shift+Enter); VGS is single-line only.
- VS Code supports auto-completions in the debug console; VGS does not (both adapters report `supportsCompletionsRequest = false`).
- VGS has a dedicated `help` command with examples; VS Code relies on documentation.
- VS Code interleaves program output with evaluation results; VGS keeps them separate.

### Inline Debug Values

**VS Code**: The inline values feature shows variable values directly in the editor, rendered at the end of each code line during debugging. This is controlled by the `debug.inlineValues` setting (off by default, requires language support or the "Inline Values" extension). Values appear as dimmed, colored annotations after the source code on each line.

**VGS IDE**: The `InlineDebugValueRenderer` implements the same concept as an `IBackgroundRenderer` for the AvaloniaEdit text editor:
- Values are drawn at the end of each line, 20 pixels after the line's text content.
- Multiple variables on the same line are grouped and displayed as comma-separated pairs: `x = 5, name = "hello"`.
- Styling: blue text (`rgba(86, 156, 214, 200)`) on a light blue background (`rgba(86, 156, 214, 30)`), using `Cascadia Code` / `Consolas` font at 85% of the editor's font size.
- Values are provided via `ShowInlineDebugValues(IEnumerable<InlineDebugValue>)` and cleared via `ClearInlineDebugValues()`.
- Each `InlineDebugValue` has a 1-based `Line`, `Name`, and `Value`.

Both implementations render inline values similarly. The VGS implementation is always available when the debug service provides values (no separate extension needed).

### Variable Modification (Set Value)

**VS Code**: When a debug adapter supports `supportsSetVariable`, users can double-click a variable's value in the Variables or Watch panel and type a new value. The new value is sent to the debug adapter via the `setVariable` DAP request. This allows live modification of variables during debugging without restarting.

**VGS IDE**: Not supported. Both debug adapters (`NetDebugAdapter` and `DebugSession`) explicitly report `supportsSetVariable = false` in their initialize response. The `IDapClientService` interface defines `SetVariableAsync(int variablesReference, string name, string value)` and a `SetVariableResult` class, indicating the plumbing exists at the service layer, but neither debug adapter implements the handler.

### Expandable Objects and Arrays

**VS Code**: Complex objects in the Variables panel, Watch panel, and hover popup are displayed with a disclosure triangle. Clicking it sends a `variables` DAP request with the object's `variablesReference` to fetch child properties. Arrays show indexed elements `[0]`, `[1]`, etc. Nested objects can be expanded recursively to any depth.

**VGS IDE**: Object and array expansion is fully supported in the Variables panel through the tree view:

**Interpreter backend** (`DebugSession`):
- Objects: Expands all public instance properties (via `PropertyInfo.GetValue`) and all public instance fields (via `FieldInfo.GetValue`). Errors during property reads display `<error reading property>`.
- Arrays/Collections: Iterates `IEnumerable` elements as `[0]`, `[1]`, etc., with a 1000-element cap (displays `"... (N more items)"` beyond that).
- Display: Primitives and strings show formatted values; arrays show `TypeName (N items)`; objects show `{TypeName}`.

**Native (.NET) backend** (`NetDebugAdapter` + `VariableInspector`):
- Uses `ICorDebug` COM interfaces to inspect CLR values directly in the debugged process.
- Handles: reference values (null check + dereference), boxed values (unbox), generic values (raw byte read with `Marshal`), strings (Unicode buffer read), arrays (`GetCount` + `GetElementType`), and objects (field enumeration via `ICorDebugObjectValue`).
- Primitive formatting via `ReadPrimitiveValue` for all CLR element types (`Boolean`, `Char`, `I1`-`I8`, `U1`-`U8`, `R4`, `R8`).
- Each expandable value is registered in `_variableReferences` dictionary with a stable reference ID for on-demand child fetching.

The VGS tree supports lazy loading: `VariableTreeItem.OnIsExpandedChanged` triggers `LoadChildrenAsync` only on first expansion, with `IsLoading`/`IsLoaded` state tracking and error fallback (`<error loading>` child node).

### Expression Evaluation Capabilities

**VS Code**: Expression evaluation in watch, hover, and debug console uses the `evaluate` DAP request. The debug adapter determines what expressions are supported -- typically any valid expression in the debugged language, including function calls, property access, arithmetic, and comparison operators.

**VGS IDE**: Expression evaluation differs between the two debug backends:

**Interpreter backend** (`DebugSession` + `DebugExpressionEvaluator`):
- Full recursive-descent expression evaluator supporting: `Or`/`OrElse`/`||`, `And`/`AndAlso`/`&&`, `Not`/`!`, comparisons (`<`, `>`, `<=`, `>=`, `=`, `==`, `<>`, `!=`), addition/subtraction, multiplication/division/modulo, unary negation, and member access (dot notation).
- Supports string literals, numeric literals, boolean literals, and variable lookups from current scope and globals.
- Falls back to simple variable name lookup if the expression parser throws an exception.

**Native backend** (`NetDebugAdapter`):
- `HandleEvaluate` only performs variable name lookup -- it searches locals and arguments for a case-insensitive name match. No expression parsing or function call evaluation.
- This means hover evaluation and watch expressions in the native backend are limited to single variable names (no `x + 1`, no `obj.Property`, no function calls).

## Summary of Gaps

| Gap | Impact | Implementation Effort |
|-----|--------|----------------------|
| No variable modification (set value) | Cannot change variable values during debugging | Medium -- requires `setVariable` handler in both debug adapters |
| No rich hover expansion | Cannot drill into objects from hover popup | Medium -- requires tree view in `DataTipPopup` |
| No debug console completions | No auto-complete while typing expressions | Medium -- requires `completionsRequest` handler |
| No multi-line debug console input | Cannot evaluate multi-line expressions | Low -- add Shift+Enter handling in Immediate Window |
| Limited native backend evaluation | Watch/hover in .NET debug mode only resolves variable names | High -- requires expression parser over ICorDebug values |
| No value formatting options | Cannot toggle hex/decimal display | Low -- add format flag to variable display |
| No globals scope in native backend | NetDebugAdapter only exposes Locals + Arguments | Medium -- requires static field enumeration via ICorDebug |
