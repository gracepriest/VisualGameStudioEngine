# Call Stack & Thread Debugging: VS Code vs Visual Game Studio IDE

## Feature Comparison Table

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Call Stack Panel** | Built-in CALL STACK section in Debug sidebar | CallStackView panel (Avalonia ListBox) | Partial |
| **Multiple stack frames** | Full call chain from current frame to entry point | Interpreter: full chain; CLR: active frame only | Yes |
| **Click frame to navigate** | Click any frame to open source at that location | SelectedFrame fires FrameSelected event; MainWindowViewModel navigates to top frame on stop | Partial |
| **Frame-relative variables** | Variables/Watch scoped to selected frame | GetScopesAsync/GetVariablesAsync accept frameId | Comparable |
| **Thread list panel** | Dedicated section showing all threads with names/IDs | No Threads panel exists | Yes |
| **Thread switching** | Click a thread to switch context; call stack updates | Hardcoded to threadId=1 everywhere | Yes |
| **Multi-session debugging** | Sessions as top-level elements in CALL STACK | Single session only | Yes |
| **Grayed-out external frames** | presentationHint "subtle"/"deemphasize" for library code | No presentationHint support; no external code distinction | Yes |
| **Step Out** | Returns to caller frame | StepOutAsync sends DAP "stepOut" request | Comparable |
| **Step Over / Step Into** | Standard DAP stepping | StepOverAsync / StepIntoAsync implemented | Comparable |
| **Inline frame indicator** | Yellow arrow on current execution line | SetExecutionLine highlights current line in editor | Comparable |
| **Lazy stack frame loading** | Supports `startFrame`/`levels` for paginated loading | Requests `startFrame=0, levels=100` (no pagination UI) | Minor |
| **Call stack copy/export** | Right-click to copy call stack to clipboard | Not implemented | Yes |
| **Stack frame source hints** | Shows module/assembly name for frames without source | StackFrameInfo has ModuleName field but not populated by adapters | Yes |

## Detailed Analysis

### 1. Call Stack Panel

**VS Code**: The CALL STACK section is part of the Debug sidebar. It shows a tree of all active threads, each containing their full stack of frames. Clicking any frame navigates the editor to that source location and re-scopes the Variables and Watch panels to that frame's context. Expression evaluation in the Debug Console also respects the selected frame.

**VGS IDE**: The `CallStackViewModel` maintains an `ObservableCollection<StackFrameItem>` populated via `RefreshStackTraceAsync()`. Each item stores Id, Name, FilePath, Line, Column, and a formatted DisplayText (e.g., `"Main at Program.bas:5"`). The view is an Avalonia `ListBox` with monospace font. Selecting a frame fires `FrameSelected`, but the MainWindowViewModel currently only auto-navigates to the **top frame** when the debugger stops (`OnDebugStopped`). There is no wiring from frame selection back to editor navigation or variable re-scoping.

**Key files**:
- `VisualGameStudio.Shell/ViewModels/Panels/CallStackViewModel.cs`
- `VisualGameStudio.Shell/Views/Panels/CallStackView.axaml`
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (lines 1098-1138)

### 2. Stack Frame Walking

**VS Code**: Through DAP, debug adapters typically return the complete call chain -- from the current instruction all the way back to the program entry point. The `stackTrace` response includes all frames with source locations, and VS Code renders them in order.

**VGS IDE -- Interpreter mode** (`DebugSession.cs` + `DebuggableInterpreter.cs`): The interpreter maintains a `Stack<CallFrame>` that is pushed on function entry and popped on return. `GetStackFrames()` iterates the entire stack and returns all frames with function names and current line numbers. This provides a complete call chain. However, all frames report the same source file (`_currentFile`) regardless of which file the function is actually defined in.

**VGS IDE -- CLR mode** (`NetDebugAdapter.cs`): The adapter calls `GetActiveFrame` via ICorDebug vtable and returns only the **single active frame**. It does not walk the caller chain (the `GetCaller` slot at index 5 on ICorDebugFrame is never called). This means the call stack panel shows at most one entry when debugging compiled executables.

**Key files**:
- `BasicLang/Debugger/DebuggableInterpreter.cs` (lines 414-429, 611-633)
- `BasicLang/Debugger/DebugSession.cs` (lines 743-772)
- `BasicLang/Debugger/NetDebugAdapter.cs` (lines 372-494)

### 3. Thread Support

**VS Code**: The CALL STACK section groups frames by thread. All threads are listed with their names and states (running, paused, stopped). Clicking a different thread switches the debugging context. Multi-process debugging shows processes as top-level groups containing their threads.

**VGS IDE**: There is **no Threads panel** in the IDE (no `ThreadsViewModel` or `ThreadsView` exists). Both DAP adapters hardcode a single "Main Thread" with id=1:

- `DebugSession.HandleThreads()` returns one thread: `{ id: 1, name: "Main Thread" }`
- `NetDebugAdapter.HandleThreads()` returns one thread: `{ id: 1, name: "Main Thread" }`
- `NetDebugAdapter.MapToDapThreadId()` maps all OS thread IDs to 1

The `DebugService` sends `threadId = 1` in all continue/step/pause requests. The `StoppedEventArgs` does include a `ThreadId` property (parsed from DAP events), but since the adapters only ever report thread 1, it is unused.

**Key files**:
- `BasicLang/Debugger/DebugSession.cs` (lines 726-741)
- `BasicLang/Debugger/NetDebugAdapter.cs` (lines 352-370, line 1096)
- `VisualGameStudio.ProjectSystem/Services/DebugService.cs` (lines 336, 384, 400, 647)
- `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs` (lines 176-183)

### 4. Frame Navigation and Source Highlighting

**VS Code**: Clicking any stack frame opens the corresponding file and highlights the line. A yellow arrow gutter icon marks the current execution point. Frames without source (e.g., native or library code) are shown but grayed out and are not navigable.

**VGS IDE**: When the debugger stops, `OnDebugStopped` in MainWindowViewModel fetches the stack trace, takes the first frame, opens its file, calls `SetExecutionLine()` to highlight it, and calls `NavigateTo()` to scroll to it. However, selecting a **different** frame in the Call Stack panel does not trigger navigation. The `FrameSelected` event is raised but is not subscribed to anywhere in MainWindowViewModel for opening files or re-scoping variables.

### 5. External/Library Code Distinction

**VS Code**: The DAP protocol supports `presentationHint` on stack frames with values like `"normal"`, `"label"`, `"subtle"`. Debug adapters mark library/framework frames as `"subtle"` or `"deemphasize"`, and VS Code renders them in a muted color. Users can toggle "Show External Code" to expand or collapse these frames.

**VGS IDE**: Neither DAP adapter includes `presentationHint` in stack frame responses. The `StackFrameInfo` model has no field for this. The `StackFrameItem` UI model and `CallStackView` XAML have no styling differentiation between user code and external code. All frames are rendered identically in `#CCCCCC` foreground.

### 6. Step Out Behavior

**VS Code**: Step Out (Shift+F11) continues execution until the current function returns, then stops in the caller. The call stack updates to show the new position.

**VGS IDE**: `StepOutAsync()` sends a DAP `stepOut` request. In interpreter mode, the `DebuggableInterpreter` tracks step depth: `_stepDepth = _callStack.Count - 1` and stops when `_callStack.Count < _stepDepth`. In CLR mode, the `NetDebugAdapter` creates an ICorDebugStepper with `StepOut` flag. Both modes work correctly for single-threaded programs.

## Gaps Summary

### Critical Gaps (functional limitations)

1. **CLR mode returns only the active frame** -- The NetDebugAdapter does not walk the ICorDebugFrame caller chain, so the call stack panel shows at most one frame when debugging compiled executables.

2. **No thread panel or thread switching** -- The IDE has no UI for viewing or switching threads. All DAP communication is hardcoded to thread 1. Multi-threaded BasicLang programs cannot be debugged at the thread level.

3. **Frame selection does not navigate** -- Clicking a frame in the Call Stack panel raises `FrameSelected` but nothing subscribes to it for editor navigation or variable re-scoping.

### Moderate Gaps (missing polish)

4. **No external code distinction** -- No `presentationHint` support; all frames look identical.

5. **Single source file for all frames** (interpreter mode) -- `DebugSession.HandleStackTrace` uses `_currentFile` for every frame, so multi-file programs show incorrect source paths for non-top frames.

6. **No copy call stack** -- No clipboard export of the call stack.

7. **No multi-session debugging** -- Only one debug session at a time.

### Minor Gaps

8. **No lazy frame loading UI** -- The IDE requests 100 frames at once; no "Load More" mechanism.

9. **ModuleName field unused** -- `StackFrameInfo.ModuleName` exists in the model but is never populated by either adapter.
