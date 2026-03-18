# Debug Stepping & Source Mapping Comparison

Comparison of stepping behavior and source mapping between VS Code and the Visual Game Studio (VGS) IDE.

## Stepping Commands

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| **Step Over (F10)** | Executes current statement, stays in same scope; skips function bodies | Sends DAP `next` request; uses `ICorDebugStepper.StepRange` for source-line-level stepping with PDB-derived IL ranges; falls back to single IL step if range unavailable |
| **Step Into (F11)** | Enters called function; respects `smartStep` and `skipFiles` settings | Sends DAP `stepIn` request; uses `ICorDebugStepper.Step(stepInto: true)` with StepRange when PDB data is available |
| **Step Out (Shift+F11)** | Runs until current function returns | Sends DAP `stepOut` request; uses `ICorDebugStepper.StepOut` via raw vtable call |
| **Run to Cursor (Ctrl+F10)** | Sets temporary breakpoint at cursor, continues execution, removes breakpoint on hit | Sets temporary breakpoint via `setBreakpoints`, preserves existing breakpoints, continues, then restores original breakpoints on stop |
| **Set Next Statement (Ctrl+Shift+F10)** | Uses DAP `goto`/`gotoTargets` when supported by the debug adapter | Sends `gotoTargets` then `goto` DAP requests; interpreter backend supports this (`supportsGotoTargetsRequest = true`); CLR backend does **not** (`supportsGotoTargetsRequest = false`) |
| **Step Into Targets** | Lets user choose which function call on a line to step into via `stepInTargets` DAP request | Not supported; both debug adapters report `supportsStepInTargetsRequest = false` |
| **Step Back (Reverse Debugging)** | Supported when debug adapter declares `supportsStepBack`; uses DAP `stepBack` request (e.g., Mozilla rr, UndoDB) | Not supported; both adapters report `supportsStepBack = false` |
| **Continue (F5)** | Resumes execution until next breakpoint or program end | Sends DAP `continue` request with `threadId = 1` |
| **Pause** | Sends DAP `pause` to suspend all threads | Calls `ICorDebugProcess.Stop()` (CLR backend) or sets `_paused = true` (interpreter backend) |

## Stepping Granularity

### VS Code
- Steps at the **statement** level by default.
- The `granularity` field in DAP step requests supports `statement`, `line`, and `instruction` levels.
- Multi-statement lines: Step Over stays on the same line until all statements are executed, then moves to the next line.
- The JavaScript debugger uses source maps to determine step boundaries in transpiled code.

### VGS IDE (CLR Backend - NetDebugAdapter)
- Steps at the **source line** level using `ICorDebugStepper.StepRange`.
- The stepper queries the SourceMapper for the IL byte range `[startOffset, endOffset)` corresponding to the current `.bas` source line.
- If PDB sequence point data is unavailable, falls back to single IL instruction stepping (`ICorDebugStepper.Step`).
- All stepping uses raw COM vtable calls (Marshal.GetDelegateForFunctionPointer) to bypass .NET Core QI issues with ICorDebug interfaces.

### VGS IDE (Interpreter Backend - DebuggableInterpreter)
- Steps at the **IR instruction** level, checking `instruction.SourceLine` at each instruction.
- Step Over: stops when `line != stepStartLine && callStack.Count <= stepDepth`.
- Step Into: stops when `line != stepStartLine` (any line change, including into called functions).
- Step Out: stops when `callStack.Count < stepDepth` (frame count decreases below the depth recorded at step initiation).
- Same-line skip: all step modes track `_stepStartLine` to avoid stopping on the same line where stepping was initiated.

## Source Mapping

### VS Code
- **Source maps (`.map` files)**: Standard mechanism for transpiled languages (TypeScript to JavaScript, minified code, etc.). The `sourceMaps` launch config attribute (default: `true`) controls this.
- **smartStep**: When enabled, automatically skips generated code that has no source map coverage, stepping through it until reaching mapped code again.
- **skipFiles**: Array of glob patterns for files to skip during stepping (e.g., `["<node_internals>/**"]` to skip Node.js core modules). Acts as a "Just My Code" filter.
- **Inline source maps**: Supports source maps embedded directly in the generated file via `//# sourceMappingURL=data:...`.
- Source maps provide bidirectional mapping: original source position to generated position and vice versa.

### VGS IDE (CLR Backend)
- **`#line` directives**: The CSharpBackend emits C# `#line N "file.bas"` directives in the generated `.cs` code, mapping each IR instruction back to its original `.bas` source line. This is the primary source mapping mechanism.
- **`#line hidden`**: Emitted for compiler-generated code (e.g., function prologues) so the debugger skips those regions during stepping.
- **Portable PDB**: The generated C# is compiled by `dotnet build`, which produces a Portable PDB file. The `#line` directives cause Roslyn to write sequence points referencing the original `.bas` file paths.
- **SourceMapper class**: Reads the Portable PDB at debug time using `System.Reflection.Metadata`. Indexes sequence points by both file path and method token for bidirectional lookup:
  - `GetILOffsetForLine(basFilePath, line)` -- used for setting breakpoints.
  - `GetSourceLocation(methodToken, ilOffset)` -- used for stack traces and step-stop location.
  - `GetILRangeForLine(methodToken, line)` -- used for StepRange boundaries.
  - `FindNearestExecutableLine(basFilePath, line)` -- snaps breakpoints to valid sequence points.
  - `GetMethodLines(methodToken)` -- enumerates all executable lines in a method.
- **Deduplication**: `EmitLineDirective` tracks `_lastEmittedSourceLine` and `_lastEmittedSourceFile` to avoid emitting redundant `#line` directives for consecutive instructions on the same source line.

### VGS IDE (Interpreter Backend)
- **Direct mapping**: No source map translation is needed. The interpreter walks IR instructions that carry `SourceLine` and `SourceFilePath` directly from the parser/IR builder. The `CheckBreakpoint` method reads `instruction.SourceLine` at every step.
- Each `CallFrame` on the stack tracks `CurrentLine` for stack trace display.

## Source Mapping Pipeline (CLR Path)

```
  .bas source          CSharpBackend           Roslyn (csc)          NetDebugAdapter
  ┌──────────┐   #line N "file.bas"   ┌────────────┐  PDB    ┌──────────────┐
  │ Program.bas│ ────────────────────> │ Program.cs │ ──────> │ Program.pdb  │
  └──────────┘                        └────────────┘         └──────┬───────┘
                                                                    │
                                                              SourceMapper
                                                              reads PDB at
                                                              debug start
                                                                    │
                                                                    ▼
                                                           IL offset <-> .bas line
                                                           (breakpoints, stepping,
                                                            stack traces)
```

## "Just My Code" / Code Skipping

### VS Code
- **skipFiles**: Glob-based file exclusion list in launch config. Stepping into a skipped file continues automatically until reaching non-skipped code.
- **smartStep**: Automatically skips code without source map coverage (transpiled code only).
- **Toggle at runtime**: Right-click a call stack frame to toggle "Skip this file" for the current session.
- **Node.js `<node_internals>`**: Special glob prefix to skip Node.js built-in modules.

### VGS IDE
- **`#line hidden`**: The CSharpBackend emits `#line hidden` for compiler-generated code. The .NET debugger recognizes this and skips those regions during stepping.
- **No user-configurable skip list**: There is no equivalent to VS Code's `skipFiles` or `smartStep`. Users cannot define glob patterns or toggle file skipping at runtime.
- **P/Invoke auto-skip**: `ICorDebugStepper` with `StepRange` naturally steps over P/Invoke calls into native code, staying within the managed IL range.

## Keyboard Shortcuts

| Action | VS Code | VGS IDE |
|--------|---------|---------|
| Step Over | F10 | F10 |
| Step Into | F11 | F11 |
| Step Out | Shift+F11 | Shift+F11 |
| Continue | F5 | F5 |
| Stop Debugging | Shift+F5 | Shift+F5 |
| Run to Cursor | Right-click menu / Ctrl+F10 (some setups) | Ctrl+F10 |
| Set Next Statement | Not built-in (adapter-dependent) | Ctrl+Shift+F10 |
| Toggle Breakpoint | F9 | F9 |
| Function Breakpoint | Via UI panel | Ctrl+Shift+F9 |
| Pause | F6 (varies) | No dedicated shortcut (menu only) |

## UI Feedback on Step

### VS Code
- Yellow arrow/highlight on the current execution line.
- Call stack panel updates to show all threads and frames.
- Variables panel refreshes with current scope values.
- Debug console available for expression evaluation.
- Inline values shown via `inlineValues` debug adapter capability.

### VGS IDE
- `SetExecutionLine(line)` highlights the current line in the editor and scrolls to it via `NavigateTo(line)`.
- Brings the IDE window to front (`Activate()` + `Topmost` toggle) on every stop.
- Updates status bar text: "Step complete", "Breakpoint hit", "Exception: ...", or "Paused".
- Fetches stack trace from DAP and displays the top frame.
- Calls `ShowInlineDebugValuesAsync` to render variable values inline near their last reference before the stopped line (searches up to 50 lines above the stop point).
- Variables, Call Stack, Watch, and Immediate Window panels available.

## Gap Summary

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| Step Over | Yes | Yes | None |
| Step Into | Yes | Yes | None |
| Step Out | Yes | Yes | None |
| Run to Cursor | Yes | Yes | None |
| Set Next Statement | Adapter-dependent | Interpreter only | CLR backend lacks `gotoTargets` support |
| Step Into Targets | Yes (adapter-dependent) | No | Not implemented in either adapter |
| Step Back / Reverse | Yes (adapter-dependent) | No | Not implemented |
| Just My Code / skipFiles | Yes | Partial (`#line hidden` only) | No user-configurable file skip list |
| smartStep (auto-skip unmapped) | Yes | No | No equivalent |
| Stepping granularity choice | statement / line / instruction | Line-level (CLR), IR instruction (interpreter) | No user-selectable granularity |
| Source maps (.map files) | Native support | N/A (uses `#line` + PDB) | Different mechanism, equivalent result for BasicLang |
| Multi-statement line stepping | Steps through each statement on the line | Steps the entire line as one unit | VGS treats each source line as atomic |
| Runtime skip file toggle | Yes | No | Not implemented |
