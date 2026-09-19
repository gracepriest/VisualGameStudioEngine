title: Debugging
lede: Two adapters behind one Debug Adapter Protocol seam — managed CLR debugging for C# builds, `lldb-dap` for native ones.
---
The IDE has no debugger of its own. It speaks DAP, and a registry decides which adapter
answers for a given project.

Routing is **one predicate**, `BasicLangProject.IsNativeBuild` (`Language == Cpp || TargetBackend == Cpp`) — the same line that routes the *build* picks the *debugger*, so the two can never disagree about what a project is.

> [trap] `!IsNativeBuild` is broader than "debuggable". MSIL and LLVM projects route to the managed adapter, but `BuildService` stops those backends at source — a `.il` or `.ll` file and a toolchain hint, no executable — so F5 gets as far as "Error: No executable found after build." Despite the MSIL backend's overhaul, an MSIL project is **not** debuggable from the IDE. JavaScript never reaches the registry at all: F5 branches to the preview server first.

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
| `DebugSession.cs` | The legacy interpreter-backed DAP server — the alternative `--dap-legacy` selects instead of `NetDebugAdapter` |
| `SourceMapper.cs` | Reads portable PDBs to map `.bas` lines ⇄ IL offsets (breakpoint binding, stack traces) |
| `DebuggableInterpreter.cs` | IR-level debugging via the interpreter |
| `EngineBindings.cs` | P/Invoke bindings to `VisualGameStudioEngine.dll`, so the debuggable interpreter can call the engine |

Launch it standalone with `IDE/BasicLang.exe --debug-adapter` (`--dap` is an accepted alias).
Adding `--dap-legacy` swaps in the older interpreter-backed `DebugSession` instead, which steps
IR in-process rather than debugging a compiled `.exe` through ICorDebug.
previous implementation.

## Native debugging

`lldb-dap` drives native builds. It is **located first, downloaded only if that fails**
(`LldbDapLocator.cs` / `LldbDapInstaller.cs` / `VisualGameStudio.Shell/Services/LldbDapDownloadFlow.cs`).
The locator chain, first answer wins: the `cpp.lldbDap.path` override → the IDE-installed copy
under `~/.vgs/tools` → PATH → known LLVM install directories → nothing. Only on "nothing" does
F5 offer the one-click download.

> [trap] **The download is not live yet.** `LldbDapInstaller.ExpectedSha256` is still
> `"REPLACE-AT-RELEASE-TIME"`, so `IsReleasePinned` is false and the flow reports "not yet
> published" instead of fetching. Today lldb-dap must already be on the machine.

> [trap] Resolution runs at **every session start**, never cached — lldb-dap may be installed
> mid-session, and a descriptor that resolved once at startup would answer "not installed"
> forever. Never probe it by spawning it: `lldb-dap --version` parks on stdin and hangs.
(`LldbDapInstaller.cs` / `LldbDapLocator.cs`).

The part worth knowing: **breakpoints set in `.bas` files still bind in a native build.**
The C++ backend emits `#line` directives into the generated C++ **in Debug configurations**, so the debugger maps
native stops back to BasicLang source. You debug the language you wrote, not the language
it became.

Design: `docs/superpowers/plans/2026-07-19-cpp-lldb-dap-phase4.md`.

## The IDE side

| Piece | File |
|---|---|
| Session transport | `VisualGameStudio.ProjectSystem/Services/DapSession.cs` |
| Orchestration | `DebugService.cs` |
| Adapter selection | `DebugAdapterRegistry.cs` + `IDebugAdapterRegistry` |
| Protocol types | `VisualGameStudio.Core/Abstractions/Services/DapProtocol.cs` (the client seam is `VisualGameStudio.Core/DAP/IDapClient.cs`) |
| Launch configurations | `LaunchConfigurationService.cs` |

## What the UI gives you

<div class="card-grid">
<div class="card" data-pillar="ide"><span class="card-k">Breakpoints</span><span class="card-t">More than line stops</span><span class="card-d">Persistent across sessions, visually distinct when bound vs unbound, plus conditional, function, and data breakpoints, and exception filter settings.</span></div>
<div class="card" data-pillar="ide"><span class="card-k">Stepping</span><span class="card-t">Nearly full control</span><span class="card-d">Step over / into / out and restart all work. Set Next Statement is wired in the IDE but <span class="pill warn">inert on the default adapter</span> — <code>NetDebugAdapter</code> declares <code>supportsGotoTargetsRequest = false</code> and has no <code>gotoTargets</code> handler at all, so it only lands under <code>--dap-legacy</code>.</span></div>
<div class="card" data-pillar="ide"><span class="card-k">Inspection</span><span class="card-t">Four ways to look</span><span class="card-d">Variables, Watch, Call Stack and Threads panels, plus inline debug values shown next to the code.</span></div>
<div class="card" data-pillar="ide"><span class="card-k">Evaluation</span><span class="card-t">Type at it</span><span class="card-d">The Immediate window and Debug Console evaluate expressions in the stopped frame.</span></div>
</div>

Panel view models live in `VisualGameStudio.Shell/ViewModels/Panels/`:
`BreakpointsViewModel`, `CallStackViewModel`, `VariablesViewModel`, `WatchViewModel`,
`ThreadsViewModel`, `DebugConsoleViewModel`, `ImmediateWindowViewModel`. Dialogs:
`BreakpointConditionDialogViewModel`, `FunctionBreakpointDialogViewModel`,
`ExceptionSettingsViewModel`, `AttachToProcessViewModel`.

> [note] Attach (`Ctrl+Alt+P`) is **managed-only**. On a native project it refuses with
> "Native attach is out of scope for v1 — launch (F5) instead."

## Keys

| Key | Action |
|---|---|
| `F5` | Start / continue with debugging |
| `Ctrl+F5` | Run without debugging |
| `F9` | Toggle breakpoint |
| `F10` | Step over |
| `F11` | Step into |
| `Shift+F11` | Step out |
| `Shift+F5` | Stop debugging |
| `Ctrl+Shift+F5` | Restart debugging |
| `Ctrl+F10` | Run to cursor |
| `Ctrl+Shift+F10` | Set next statement |
| `Ctrl+Shift+F9` | New function breakpoint |
| `Ctrl+Alt+P` | Attach to process |

## Parity research

Debugger behaviour was designed against a written comparison with Visual Studio and
VS Code, one document per area, all under `docs/`:

`debug-breakpoints-comparison.md` · `debug-callstack-comparison.md` ·
`debug-launch-comparison.md` · `debug-output-comparison.md` ·
`debug-stepping-comparison.md` · `debug-toolbar-comparison.md` ·
`debug-variables-comparison.md` · `debug-ux-comparison.md` ·
`debug-parity-scorecard.md`

Start with the scorecard — it scores each area 1-10 and summarises the rest, though it links to nothing.

> [trap] The scorecard is dated **2026-03-18** and is a snapshot, not current state. Several of its listed weaknesses have since been fixed: "No attach mode" (there is now `AttachToProcessViewModel` + `Ctrl+Alt+P`) and "No debug configuration persistence (launch.json equivalent)" (`LaunchConfigurationService` reads and writes `.vgs/launch.json`). Read it for shape, not for status.
