title: Debugging
lede: Two adapters behind one Debug Adapter Protocol seam — managed CLR debugging for C# builds, `lldb-dap` for native ones.
---
The IDE has no debugger of its own. It speaks DAP, and a registry decides which adapter
answers for a given project.

```text
 IDE (DapSession / DebugService)
        |
        +---- BasicLang.exe --debug-adapter  ->  managed (CLR) debugging
        |
        +---- lldb-dap                       ->  native debugging
```

## Managed debugging

`BasicLang/Debugger/` implements a full CLR debug adapter:

| File | Role |
|---|---|
| `NetDebugAdapter.cs` | The DAP adapter itself (~4,960 lines) |
| `NetDebugProcess.cs` | Launches and owns the debuggee |
| `CorDebugWrappers.cs` | ICorDebug COM interop (~2,170 lines) |
| `DbgShim.cs` | Startup/attach shim |
| `ClrBreakpointManager.cs`, `Breakpoint.cs` | Breakpoint binding and state |
| `VariableInspector.cs` | Locals, fields, evaluation (~2,120 lines) |
| `DebugSession.cs` | Session state machine |
| `SourceMapper.cs` | Generated code ⇄ `.bas` source mapping |
| `DebuggableInterpreter.cs` | IR-level debugging via the interpreter |
| `EngineBindings.cs` | Engine-aware inspection |

Launch it standalone with `IDE/BasicLang.exe --debug-adapter`; `--dap-legacy` selects the
previous implementation.

## Native debugging

`lldb-dap` drives native builds. It is downloaded on demand
(`LldbDapInstaller.cs` / `LldbDapLocator.cs`).

The part worth knowing: **breakpoints set in `.bas` files still bind in a native build.**
The C++ backend emits `#line` directives into the generated C++, so the debugger maps
native stops back to BasicLang source. You debug the language you wrote, not the language
it became.

Design: `docs/superpowers/plans/2026-07-19-cpp-lldb-dap-phase4.md`.

## The IDE side

| Piece | File |
|---|---|
| Session transport | `VisualGameStudio.ProjectSystem/Services/DapSession.cs` |
| Orchestration | `DebugService.cs` |
| Adapter selection | `DebugAdapterRegistry.cs` + `IDebugAdapterRegistry` |
| Protocol types | `VisualGameStudio.Core/DAP/DapProtocol.cs` |
| Launch configurations | `LaunchConfigurationService.cs` |

## What the UI gives you

<div class="card-grid">
<div class="card" data-pillar="ide"><span class="card-k">Breakpoints</span><span class="card-t">More than line stops</span><span class="card-d">Persistent across sessions, visually distinct when bound vs unbound, plus conditional, function, and data breakpoints, and exception filter settings.</span></div>
<div class="card" data-pillar="ide"><span class="card-k">Stepping</span><span class="card-t">Full control</span><span class="card-d">Step over / into / out, restart, and Set Next Statement to move the instruction pointer.</span></div>
<div class="card" data-pillar="ide"><span class="card-k">Inspection</span><span class="card-t">Four ways to look</span><span class="card-d">Variables, Watch, Call Stack and Threads panels, plus inline debug values shown next to the code.</span></div>
<div class="card" data-pillar="ide"><span class="card-k">Evaluation</span><span class="card-t">Type at it</span><span class="card-d">The Immediate window and Debug Console evaluate expressions in the stopped frame.</span></div>
</div>

Panel view models live in `VisualGameStudio.Shell/ViewModels/Panels/`:
`BreakpointsViewModel`, `CallStackViewModel`, `VariablesViewModel`, `WatchViewModel`,
`ThreadsViewModel`, `DebugConsoleViewModel`, `ImmediateWindowViewModel`. Dialogs:
`BreakpointConditionDialogViewModel`, `FunctionBreakpointDialogViewModel`,
`ExceptionSettingsViewModel`, `AttachToProcessViewModel`.

## Keys

| Key | Action |
|---|---|
| `F5` | Start / continue with debugging |
| `Ctrl+F5` | Run without debugging |
| `F9` | Toggle breakpoint |
| `F10` | Step over |
| `F11` | Step into |

## Parity research

Debugger behaviour was designed against a written comparison with Visual Studio and
VS Code, one document per area, all under `docs/`:

`debug-breakpoints-comparison.md` · `debug-callstack-comparison.md` ·
`debug-launch-comparison.md` · `debug-output-comparison.md` ·
`debug-stepping-comparison.md` · `debug-toolbar-comparison.md` ·
`debug-variables-comparison.md` · `debug-ux-comparison.md` ·
`debug-parity-scorecard.md`

Start with the scorecard; it indexes the rest.
