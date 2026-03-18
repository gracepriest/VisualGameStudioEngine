# Debug Configuration & Launch Features: VS Code vs Visual Game Studio IDE

This document compares the debug and launch configuration capabilities of VS Code with those of the Visual Game Studio (VGS) IDE.

## 1. Configuration File Format

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Configuration file | `.vscode/launch.json` (JSON) | No external config file; configuration built programmatically from project settings |
| Multiple configurations | Yes -- array of named configs in `launch.json` | No -- single implicit configuration derived from the open project |
| Configuration picker | Dropdown in debug toolbar | N/A |
| IntelliSense in config | Yes -- schema-driven autocomplete inside `launch.json` | N/A |

**Details**: VS Code stores all debug settings in a `launch.json` file inside the workspace `.vscode` folder. Users can define multiple named configurations and switch between them. VGS IDE constructs a `DebugConfiguration` object at launch time using the current project's build output path and project directory; there is no user-editable configuration file.

## 2. Launch Modes

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Launch (start new process) | Yes -- `"request": "launch"` | Yes -- `StartDebuggingAsync` launches DAP adapter which creates a new process |
| Attach (connect to running process) | Yes -- `"request": "attach"` with process picker | No -- not implemented; `IDebugService` has no attach API |
| Start Without Debugging | Yes -- Ctrl+F5 / "Run Without Debugging" | Yes -- `StartWithoutDebuggingAsync` runs the compiled executable directly (no DAP) |
| Run in External Console | Configurable via `"console"` attribute (`integratedTerminal`, `externalTerminal`, `internalConsole`) | Yes -- `RunInExternalConsoleAsync` creates a batch file and opens it with `UseShellExecute = true` |

## 3. Pre-Launch Build

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Build before debug | `"preLaunchTask"` references a task in `tasks.json` | Always builds before debug -- `SaveAllAsync()` then `BuildProjectAsync()` are called automatically |
| Configurable build task | Yes -- any task label can be referenced | No -- always uses the default project build |
| Skip build option | Yes -- omit `preLaunchTask` | No -- build is mandatory before every debug/run |
| Build failure handling | Task failure prevents launch (configurable) | Build failure shows dialog and aborts launch |

**Details**: In VS Code, users explicitly wire a build task to the launch configuration via `"preLaunchTask": "build"`. The VGS IDE always calls `SaveAllAsync()` followed by `_buildService.BuildProjectAsync()` inside both `StartDebuggingAsync` and `StartWithoutDebuggingAsync`, making the build step implicit and non-optional.

## 4. Launch Parameters

| Parameter | VS Code (`launch.json`) | VGS IDE (`DebugConfiguration`) |
|-----------|------------------------|-------------------------------|
| Program path | `"program"` | `Program` -- set to `buildResult.ExecutablePath` |
| Working directory | `"cwd"` | `WorkingDirectory` -- set to `ProjectDirectory` |
| Command-line arguments | `"args"` (string array) | `Arguments` (string array) -- exists in model but **not populated** by the IDE; always empty |
| Environment variables | `"env"` (object of key-value pairs) | `Environment` (Dictionary) -- exists in model but **not populated** by the IDE; always empty |
| Stop on entry | `"stopOnEntry"` (boolean) | `StopOnEntry` (boolean) -- exists in model but **not set** by the IDE; defaults to `false` |
| No-debug flag | Implicit from "Run Without Debugging" action | `noDebug` sent in DAP launch request (set to `false` for debug, run-without-debug bypasses DAP entirely) |

**Key gap**: The VGS IDE `DebugConfiguration` model supports arguments, environment variables, and stop-on-entry, but the `MainWindowViewModel` never populates these fields. There is no UI for the user to configure them.

## 5. Debug Adapter Protocol (DAP) Integration

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| DAP transport | stdin/stdout to debug adapter process | stdin/stdout to `dotnet BasicLang.dll --debug-adapter` |
| Initialization | Sends `initialize`, then `launch`/`attach`, then `configurationDone` | Same sequence: `initialize` -> `launch` -> `setBreakpoints` -> `configurationDone` |
| Debug adapters | Two: interpreter-based (`DebugSession`) and CLR-based (`NetDebugAdapter`) | Same two adapters (they are part of the BasicLang compiler) |
| Interpreter debugging | `DebugSession` compiles source to IR and runs via `DebuggableInterpreter` | Same -- used for `.bas` source-level debugging |
| CLR debugging | `NetDebugAdapter` uses ICorDebug via dbgshim for compiled .NET executables | Same -- used when debugging compiled EXEs with PDB files |

## 6. Breakpoint Features

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Line breakpoints | Yes | Yes -- `SetBreakpointsAsync` |
| Conditional breakpoints | Yes -- `"condition"` field | Yes -- `SourceBreakpoint.Condition` sent via DAP |
| Hit count breakpoints | Yes -- `"hitCondition"` field | Yes -- `SourceBreakpoint.HitCondition` sent via DAP |
| Log points (tracepoints) | Yes -- `"logMessage"` field | Yes -- `SourceBreakpoint.LogMessage` sent via DAP |
| Function breakpoints | Yes | Yes -- `SetFunctionBreakpointsAsync` (DAP `setFunctionBreakpoints`; CLR adapter returns empty, interpreter supports it) |
| Data breakpoints | Yes (adapter-dependent) | Yes -- `SetDataBreakpointsAsync` and `GetDataBreakpointInfoAsync` in the interface |
| Exception breakpoints | Yes -- configurable filters | Yes -- `SetExceptionBreakpointsAsync` with `"all"` and `"uncaught"` filters |
| Inline breakpoints | Yes -- column-level | Partial -- `SourceBreakpoint.Column` exists but column breakpoints are not exposed in the editor UI |

## 7. Execution Control

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Continue (F5) | Yes | Yes -- `ContinueAsync` |
| Step Over (F10) | Yes | Yes -- `StepOverAsync` (DAP `next`) |
| Step Into (F11) | Yes | Yes -- `StepIntoAsync` (DAP `stepIn`) |
| Step Out (Shift+F11) | Yes | Yes -- `StepOutAsync` (DAP `stepOut`) |
| Pause | Yes | Yes -- `PauseAsync` |
| Stop | Yes | Yes -- `StopDebuggingAsync` (sends DAP `disconnect` with `terminateDebuggee: true`) |
| Restart | Yes -- restarts debug session | Yes -- `RestartAsync` in `DebugService` (stops then re-launches with saved config/breakpoints) |
| Run to Cursor | Yes | Yes -- `RunToCursorAsync` (temporarily adds breakpoint, continues, then restores original breakpoints) |
| Set Next Statement (goto) | Limited (adapter-dependent) | Yes -- `SetNextStatementAsync` using DAP `gotoTargets`/`goto` |

**Note**: VGS IDE's `RestartAsync` is implemented in `DebugService` but no `RestartCommand` was found wired in `MainWindowViewModel`, suggesting it may not be exposed in the UI yet.

## 8. Variable Inspection

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Locals/Scopes | Yes -- Variables panel | Yes -- `GetScopesAsync` and `GetVariablesAsync` |
| Watch expressions | Yes -- Watch panel | Yes -- `EvaluateAsync` with `context: "watch"` |
| Hover evaluation | Yes | Yes -- DAP adapter reports `supportsEvaluateForHovers: true` |
| Call stack | Yes | Yes -- `GetStackTraceAsync` |
| Set variable value | Yes (adapter-dependent) | No -- `NetDebugAdapter` reports `supportsSetVariable: false` |
| Variable paging | Yes (adapter-dependent) | No -- initialize sends `supportsVariablePaging: false` |

## 9. Console & I/O

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Integrated terminal output | Yes -- `"console": "integratedTerminal"` | Yes -- output captured via `OutputDataReceived` events, displayed in Output panel |
| External terminal | Yes -- `"console": "externalTerminal"` | Yes -- `RunInExternalConsoleAsync` launches a batch file in a new cmd window |
| Debug console (REPL) | Yes -- evaluate expressions while paused | Partial -- `EvaluateAsync` exists but no dedicated REPL UI panel |
| stdin input | Yes | Yes -- `SendInputAsync` writes to redirected stdin of the target process (run-without-debug mode only) |

## 10. Advanced Features

| Feature | VS Code | VGS IDE |
|---------|---------|---------|
| Compound launch configs | Yes -- launch multiple debug sessions in parallel | No |
| Server ready action | Yes -- `"serverReadyAction"` opens browser when port detected | No |
| Post-debug task | Yes -- `"postDebugTask"` | No |
| Source maps | Yes (language-dependent) | Yes -- `SourceMapper` maps PDB data to BasicLang source lines |
| Hot Reload | Yes -- `csharp.experimental.debug.hotReload` for C# | No |
| Multi-target debugging | Yes -- multiple simultaneous debug sessions | No -- single session only |
| Launch configuration variables | Yes -- `${workspaceFolder}`, `${file}`, `${env:VAR}`, etc. | No -- paths are computed programmatically |
| User input variables | Yes -- `${input:variableName}` with prompt/pick | No |
| Remote debugging | Yes -- via SSH, containers, WSL | No |
| Step back (reverse debugging) | Yes (adapter-dependent) | No -- `supportsStepBack: false` |
| Restart frame | Yes (adapter-dependent) | No -- `supportsRestartFrame: false` |

## 11. Summary of Gaps

The following VS Code features have no equivalent in the VGS IDE:

| Gap | Impact |
|-----|--------|
| **No configuration file** | Users cannot save/share debug settings, switch between configurations, or customize launch parameters |
| **No attach mode** | Cannot debug already-running processes |
| **No UI for arguments/env/stopOnEntry** | `DebugConfiguration` model supports these but they are never populated |
| **No compound configurations** | Cannot launch multiple processes together (e.g., client + server) |
| **No hot reload** | Code changes require stop-rebuild-relaunch cycle |
| **No remote debugging** | Debugging limited to local machine |
| **No restart command in UI** | `RestartAsync` exists in service layer but is not wired to a menu item or keyboard shortcut |
| **No debug console REPL** | Expression evaluation API exists but no interactive panel |
| **No variable modification** | Cannot change variable values during debugging |
| **No post-debug tasks** | No automated cleanup after debug sessions |

## 12. Source Files Referenced

- `VisualGameStudio.Core/Abstractions/Services/IDebugService.cs` -- Debug service interface and `DebugConfiguration` model
- `VisualGameStudio.ProjectSystem/Services/DebugService.cs` -- DAP client implementation
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` -- IDE commands (`StartDebuggingAsync`, `StartWithoutDebuggingAsync`, `RunInExternalConsoleAsync`)
- `BasicLang/Debugger/NetDebugAdapter.cs` -- CLR debug adapter (ICorDebug-based)
- `BasicLang/Debugger/DebugSession.cs` -- Interpreter debug adapter

Sources:
- [VS Code Debug Configuration](https://code.visualstudio.com/docs/debugtest/debugging-configuration)
- [VS Code Debugging Overview](https://code.visualstudio.com/docs/debugtest/debugging)
- [VS Code C# Debugger Settings](https://code.visualstudio.com/docs/csharp/debugger-settings)
- [VS Code C# Debugging](https://code.visualstudio.com/docs/csharp/debugging)
- [VS Code Tasks Integration](https://code.visualstudio.com/docs/debugtest/tasks)
- [VS Code Variables Reference](https://code.visualstudio.com/docs/reference/variables-reference)
