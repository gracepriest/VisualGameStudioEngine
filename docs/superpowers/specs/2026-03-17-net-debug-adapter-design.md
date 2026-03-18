# .NET Debug Adapter for BasicLang IDE

**Date:** 2026-03-17
**Status:** Approved
**Scope:** Replace interpreter-based debug adapter with a full .NET debugger that launches compiled executables

## Problem

The current debug adapter (`DebugSession.cs`) interprets BasicLang IR instructions via `DebuggableInterpreter`. This approach cannot:

- Run windowed game applications (Raylib window creation fails on background threads)
- Execute P/Invoke calls to native DLLs properly
- Provide accurate .NET runtime behavior (threading, GC, exceptions)
- Debug multi-threaded applications

The result: game projects exit immediately with code 0 when debugging; only console-only, single-file programs work.

## Solution

Build a DAP-compatible debug adapter that:

1. Compiles BasicLang to C# with `#line` directives (mapping generated C# lines to `.bas` source lines)
2. Builds with portable PDB debug symbols
3. Launches the compiled .exe as a real .NET process
4. Attaches via `ICorDebug` COM interfaces (through `dbgshim.dll`) for full debugging

The IDE side (`DebugService.cs`, all debug UI) stays **unchanged** — it already speaks DAP.

## Architecture

```
IDE (DebugService.cs)          NetDebugAdapter              Target .exe
    |                              |                           |
    |-- initialize -------------->|                           |
    |<-- capabilities ------------|                           |
    |-- launch ------------------>|-- Launch process -------->| (game window opens)
    |                              |-- Attach ICorDebug ----->| (debugger attached)
    |-- setBreakpoints ---------->|-- PDB lookup, set CLR bp->| (breakpoints active)
    |-- configurationDone ------->|-- Resume process -------->| (running)
    |                              |                           |
    |                              |<-- Breakpoint hit -------|
    |<-- stopped event -----------|                           |
    |-- stackTrace -------------->|-- Read CLR frames ------->|
    |<-- frames (.bas paths) -----|                           |
    |-- variables --------------->|-- Read CLR locals ------->|
    |<-- variable values ---------|                           |
    |-- continue ---------------->|-- Resume ---------------->|
```

## Compilation and Launch Workflow

The IDE orchestrates the build-then-debug flow. The debug adapter receives a pre-compiled `.exe`:

1. **IDE `MainWindowViewModel.StartDebuggingAsync()`:** calls `BuildService.BuildProjectAsync()` which compiles `.bas` → C# (with `#line` directives) → `dotnet build` → `.exe` + `.pdb`
2. **IDE `DebugService.StartDebuggingAsync()`:** launches `BasicLang.exe --debug-adapter`, sends DAP `launch` with `program = buildResult.ExecutablePath` (the `.exe` path, NOT the `.bas` path)
3. **Debug adapter `NetDebugAdapter.HandleLaunch()`:** receives `.exe` path, launches it, attaches ICorDebug

This means `BuildService.cs` must emit `#line` directives during the C# generation phase (it already calls the C# backend). The adapter does NOT compile — it only launches and debugs.

## Component Design

### 1. `#line` Directive Emission (CSharpBackend.cs)

**What:** Emit `#line N "filepath"` directives in generated C# code.

**How:** Every time the backend emits code from an IR instruction with `SourceLine > 0`, prepend `#line {SourceLine} "{absoluteBasPath}"`. Use `#line hidden` for non-executable compiler-generated boilerplate only (using statements, namespace/class declarations, closing braces). Never use `#line hidden` on executable code that could throw exceptions.

**Source file path tracking:** `IRFunction.ModuleName` stores a logical name (e.g., `"Main"`), not a file path. A new `SourceFilePath` property must be added to `IRFunction` and set during compilation. The `BasicCompiler.CompileUnit()` method already has access to `CompilationUnit.FilePath` when building IR via `IRBuilder`. The `IRBuilder` will propagate this to each `IRFunction` it creates. The `CSharpBackend` reads `_currentFunction.SourceFilePath` to emit the correct `#line` path.

**Multi-file projects:** In a multi-file project (Main.bas + func.bas), each `IRFunction` has its own `SourceFilePath`. When the backend emits code for functions from different modules, the `#line` directive switches file paths automatically. All functions compile into a single assembly, but the PDB records different source documents.

**Example output:**
```csharp
#line hidden
using System;
using RaylibWrapper;

namespace game6
{
#line hidden
    public static class Main
    {
#line 3 "C:\\Users\\...\\Main.bas"
        public static void Main()
        {
#line 4 "C:\\Users\\...\\Main.bas"
            FrameworkWrapper.Framework_Initialize(800, 600, "My Game");
#line 5 "C:\\Users\\...\\Main.bas"
            while (!FrameworkWrapper.Framework_ShouldClose())
            {
#line 6 "C:\\Users\\...\\Main.bas"
                FrameworkWrapper.Framework_Update();
            }
        }
    }

#line hidden
    public static class func
    {
#line 2 "C:\\Users\\...\\func.bas"
        public static void Draw()
        {
#line 3 "C:\\Users\\...\\func.bas"
            FrameworkWrapper.Framework_DrawText("Hello", 10, 10, 24, 255, 255, 255, 255);
        }
    }
}
```

**Build change:** `BuildService.cs` already uses `dotnet build -c Debug` which produces portable PDBs by default. No change needed.

### 2. NetDebugAdapter.cs (NEW — DAP Server)

Replaces `DebugSession.cs` as the `--debug-adapter` handler. Reads DAP JSON-RPC from stdin, writes to stdout (same protocol as existing adapter).

**DAP command handlers:**

| Command | Action |
|---------|--------|
| `initialize` | Report capabilities (breakpoints, stepping, variables, exceptions) |
| `launch` | Receive .exe path + cwd + args. Launch process, attach ICorDebug via RegisterForRuntimeStartup |
| `setBreakpoints` | Store breakpoints. If module loaded, bind via PDB sequence points. Otherwise store as pending |
| `configurationDone` | Resume the debuggee (process was paused after CLR attach) |
| `threads` | Enumerate ICorDebugThread objects |
| `stackTrace` | Walk ICorDebugFrame chain, map IL offsets to .bas lines via PDB |
| `scopes` | Return Locals, Arguments, Module Globals scopes per frame |
| `variables` | Read ICorDebugValue hierarchy, format for display |
| `evaluate` | Variable name lookup in locals/args/statics. Expression evaluation (e.g., `x + 1`, `arr.Length`) is out of scope for v1; the adapter returns the value if it matches an existing variable name, otherwise returns an error |
| `continue` | ICorDebugProcess.Continue(false) |
| `next` | Create ICorDebugStepper, call Step(bStepIn=false) with SetRangeIL for current sequence point range |
| `stepIn` | Create ICorDebugStepper, call Step(bStepIn=true) |
| `stepOut` | Create ICorDebugStepper, call StepOut() |
| `pause` | ICorDebugProcess.Stop(0) |
| `disconnect` | Detach debugger, terminate process |
| `setExceptionBreakpoints` | Configure first-chance / unhandled exception stops |

### 3. NetDebugProcess.cs (NEW — Process Launch + ICorDebug Bootstrap)

**Correct .NET Core / .NET 5+ bootstrap sequence using `dbgshim.dll`:**

The .NET Core debugger attach model differs from .NET Framework. `ICorDebug.CreateProcess()` does NOT exist for .NET Core. Instead:

1. Find `dbgshim.dll`: search `%ProgramFiles%\dotnet\shared\Microsoft.NETCore.App\{version}\`, next to target .exe (self-contained), or via `dotnet --info`
2. Launch the target process normally via `Process.Start()` (NOT suspended)
3. Call `RegisterForRuntimeStartup(processId, callback, parameter)` from dbgshim
4. dbgshim monitors the process; when the CLR loads, it invokes the callback with the runtime version string
5. In the callback: call `CreateDebuggingInterfaceFromVersion3(version, ...)` → get `ICorDebug`
6. `ICorDebug.Initialize()`
7. `ICorDebug.SetManagedHandler(managedCallback)` — wire up our callback object
8. `ICorDebug.DebugActiveProcess(processId, win32Attach=false)` → attach to the running process
9. The process is now paused (CLR stops all managed threads on attach)
10. Set initial breakpoints, then `ICorDebugProcess.Continue(false)` in `configurationDone`

**Error handling:** If `dbgshim.dll` is not found, the adapter sends a DAP error response with message: "Cannot start debugging: .NET SDK debugging tools not found. Ensure .NET SDK is installed." and terminates.

**Finding dbgshim.dll — search order:**
1. Next to the target .exe (self-contained app)
2. `DOTNET_ROOT` environment variable
3. `%ProgramFiles%\dotnet\shared\Microsoft.NETCore.App\{latest-version}\`
4. `dotnet --list-runtimes` output (parse version paths)

**ManagedCallback implementation** (implements `ICorDebugManagedCallback`, `ICorDebugManagedCallback2`):

All callback methods that we don't explicitly handle MUST call `ICorDebugProcess.Continue(false)` — the CLR requires this or the process deadlocks.

| Callback | Action |
|----------|--------|
| `Breakpoint(appDomain, thread, breakpoint)` | Call `ICorDebugProcess.Stop(0)` to freeze all threads, then send DAP `stopped` event reason=breakpoint |
| `StepComplete(appDomain, thread, stepper, reason)` | Call `ICorDebugProcess.Stop(0)`, send DAP `stopped` event reason=step |
| `Exception(appDomain, thread, unhandled)` | Check exception filters; if should stop: `Stop(0)` + send `stopped` reason=exception. Otherwise `Continue(false)` |
| `ExitProcess(process)` | Send DAP `exited` + `terminated` events |
| `LoadModule(appDomain, module)` | Read PDB for this module, bind any pending breakpoints, then `Continue(false)` |
| `CreateThread(appDomain, thread)` | Track thread ID, `Continue(false)` |
| `ExitThread(appDomain, thread)` | Remove thread, `Continue(false)` |
| `LoadAssembly`, `UnloadAssembly`, `UnloadModule`, `DebuggerError`, etc. | `Continue(false)` |
| **ICorDebugManagedCallback2 stubs:** `MDANotification`, `FunctionRemapOpportunity`, `FunctionRemapComplete`, `CreateConnection`, `ChangeConnection`, `DestroyConnection`, `Exception2` | `Continue(false)` |

### 4. SourceMapper.cs (NEW — PDB Sequence Point Reader)

Reads portable PDB files using `System.Reflection.Metadata` to:

- Map `.bas` file + line → IL offset (for setting breakpoints)
- Map IL offset → `.bas` file + line (for stack traces)
- Find nearest executable line when breakpoint is on a non-executable line
- Read local variable names and scopes

**Key method signatures:**
```csharp
int? GetILOffsetForLine(string basFilePath, int line, MethodDefinitionHandle method)
(string file, int line)? GetSourceLocation(int ilOffset, MethodDefinitionHandle method)
int FindNearestExecutableLine(string basFilePath, int line)
IReadOnlyList<LocalVariableInfo> GetLocalsForMethod(MethodDefinitionHandle method, int ilOffset)
```

### 5. ClrBreakpointManager.cs (NEW — Breakpoint State Machine)

Manages breakpoint lifecycle:

| State | Meaning |
|-------|---------|
| `pending` | Received from IDE, module not loaded yet |
| `bound` | CLR breakpoint set via `ICorDebugCode.CreateBreakpoint()` |
| `verified` | Reported to IDE as `verified=true` |
| `invalid` | No matching sequence point (non-executable line) |

**Single-assembly model:** BasicLang compiles all `.bas` files into a single assembly (one `.exe`). There is typically one `LoadModule` callback for the user code module. All pending breakpoints for all `.bas` files are bound at that point. The pending mechanism still exists for robustness (e.g., if the runtime loads assemblies lazily), but in practice all breakpoints bind on the first `LoadModule` for the main assembly.

**Edge cases:**
- Breakpoint on blank/comment line → find nearest executable line via PDB, report adjusted line back to IDE
- Remove breakpoint while running → `ICorDebugBreakpoint.Activate(false)`

### 6. VariableInspector.cs (NEW — CLR Value Reader)

Reads `ICorDebugValue` hierarchy and converts to DAP variable format:

| CLR Type | Read Method | DAP Display |
|----------|------------|-------------|
| `ICorDebugGenericValue` (int, float, bool) | `GetValue()` → raw bytes → BitConverter | `42`, `3.14`, `True` |
| `ICorDebugStringValue` | `GetString()` | `"Hello World"` |
| `ICorDebugObjectValue` (class) | `GetClass()` → enumerate fields | expandable tree |
| `ICorDebugArrayValue` | `GetCount()`, `GetElement(i)` | `Integer(3) [1, 2, 3]` |
| `ICorDebugReferenceValue` | `IsNull()` / `Dereference()` | `Nothing` or inner value |
| `ICorDebugBoxedValue` | `GetObject()` → unwrap | show inner value |

**Variable references:** DAP uses integer `variablesReference` IDs for expandable nodes. `VariableInspector` maintains `Dictionary<int, ICorDebugValue>` so the IDE can request children of objects/arrays. References are cleared on each `continue` / step (values become invalid after resuming).

**Scopes per stack frame:**
1. **Locals** — `ICorDebugILFrame.EnumerateLocalVariables()` + PDB names
2. **Arguments** — `ICorDebugILFrame.EnumerateArguments()` + metadata parameter names
3. **Module Globals** — static fields on the generated class

### 7. DbgShim.cs + CorDebugWrappers.cs (NEW — COM Interop)

**DbgShim.cs:** P/Invoke declarations for `dbgshim.dll`:
- `RegisterForRuntimeStartup(processId, callback, parameter)` — async notification when CLR loads
- `UnregisterForRuntimeStartup(handle)` — cleanup
- `CreateDebuggingInterfaceFromVersion3(version, ...)` — get ICorDebug after runtime load
- `CLRCreateInstance` — alternative bootstrap

**CorDebugWrappers.cs:** Managed C# wrappers for ICorDebug COM interfaces. This is the largest file (~1500-2000 lines) as it declares 20+ COM interfaces:

Core interfaces:
- `ICorDebug`, `ICorDebugProcess`, `ICorDebugProcess2`
- `ICorDebugAppDomain`
- `ICorDebugThread`, `ICorDebugThread2`
- `ICorDebugFrame`, `ICorDebugILFrame`, `ICorDebugILFrame2`
- `ICorDebugFunction`, `ICorDebugFunction2`
- `ICorDebugCode`
- `ICorDebugModule`, `ICorDebugModule2`, `ICorDebugAssembly`

Value types:
- `ICorDebugValue`, `ICorDebugValue2`
- `ICorDebugGenericValue`, `ICorDebugStringValue`
- `ICorDebugObjectValue`, `ICorDebugArrayValue`
- `ICorDebugReferenceValue`, `ICorDebugBoxedValue`

Control:
- `ICorDebugStepper`, `ICorDebugStepper2`
- `ICorDebugBreakpoint`, `ICorDebugFunctionBreakpoint`

Callbacks:
- `ICorDebugManagedCallback` (14 methods, all must be implemented)
- `ICorDebugManagedCallback2` (7 methods, all must be implemented)

Each interface requires `[ComImport]`, `[Guid("...")]`, `[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]` attributes and exact vtable-order method declarations.

## Stepping Implementation

**Step operations:**

| DAP Command | CLR Action |
|-------------|-----------|
| `continue` | `ICorDebugProcess.Continue(false)` — resume all threads |
| `next` (step over) | Create `ICorDebugStepper` on current thread, call `Step(bStepIn=false)`. Use `SetRangeIL(true)` + PDB sequence point ranges to avoid stopping multiple times on one `.bas` line |
| `stepIn` | `ICorDebugStepper.Step(bStepIn=true)` — step into function calls |
| `stepOut` | `ICorDebugStepper.StepOut()` — run until current function returns |
| `pause` | `ICorDebugProcess.Stop(0)` — break all threads immediately |

**Step range filtering:** A single `.bas` line may compile to multiple IL instructions. Without filtering, stepping would stop multiple times on one line. Solution: use PDB sequence points to get the IL range for the current `.bas` line, then `ICorDebugStepper.StepRange(bStepIn, ranges, cRanges)`. The CLR only stops when execution leaves these IL ranges.

**Thread behavior:** Breakpoint and step-complete callbacks fire on the callback thread but do NOT automatically freeze other threads. The callback handler must call `ICorDebugProcess.Stop(0)` before sending the DAP `stopped` event to ensure all managed threads are frozen for inspection. `Continue(false)` resumes all threads. When stopped, the game window freezes — this is expected debugging behavior.

## Integration Points

### Changes to existing files:

| File | Change |
|------|--------|
| `BasicLang/CSharpBackend.cs` | Emit `#line` directives using `IRInstruction.SourceLine` and `IRFunction.SourceFilePath` |
| `BasicLang/IRBuilder.cs` | Set `SourceFilePath` on each `IRFunction` from `CompilationUnit.FilePath` |
| `BasicLang/IRNodes.cs` | Add `SourceFilePath` property to `IRFunction` |
| `BasicLang/Program.cs` | `--debug-adapter` routes to `NetDebugAdapter` instead of `DebugSession` |
| `MainWindowViewModel.cs` | Pass compiled `.exe` path in launch config instead of `.bas` path |

### New files:

| File | Purpose | Est. Lines |
|------|---------|-----------|
| `BasicLang/Debugger/NetDebugAdapter.cs` | DAP server, command routing | ~500 |
| `BasicLang/Debugger/NetDebugProcess.cs` | Process launch + dbgshim bootstrap + ManagedCallback | ~400 |
| `BasicLang/Debugger/SourceMapper.cs` | PDB sequence point reader | ~200 |
| `BasicLang/Debugger/ClrBreakpointManager.cs` | Breakpoint state machine | ~150 |
| `BasicLang/Debugger/VariableInspector.cs` | ICorDebugValue → DAP variables | ~300 |
| `BasicLang/Debugger/DbgShim.cs` | P/Invoke for dbgshim.dll | ~80 |
| `BasicLang/Debugger/CorDebugWrappers.cs` | COM interface declarations (20+ interfaces) | ~1800 |

**Total: ~3430 lines across 7 new files**

### Unchanged:

- `DebugService.cs` — already speaks DAP, no changes
- `BreakpointsViewModel.cs` — breakpoint UI unchanged
- All debug panels (call stack, variables, watch, breakpoints)
- `DebugSession.cs` / `DebuggableInterpreter.cs` — kept as fallback for projects without compiled .exe

## NuGet Dependencies

| Package | Purpose |
|---------|---------|
| `System.Reflection.Metadata` | Read portable PDB files for sequence points and local variable info |

No other new NuGet packages required. `dbgshim.dll` is part of the .NET runtime (already installed). COM interfaces are declared via `[ComImport]` attributes directly.

## Testing Strategy

1. **`#line` emission tests:** Compile a BasicLang program, verify generated C# contains correct `#line` directives with `.bas` paths and line numbers
2. **PDB verification test:** Compile with `#line`, run `dotnet build`, read the PDB with `System.Reflection.Metadata`, verify sequence points reference `.bas` files
3. **SourceMapper unit tests:** Given a test PDB, verify line-to-IL and IL-to-line mappings
4. **Breakpoint state machine tests:** Verify pending → bound → verified transitions, invalid line handling, nearest-line adjustment
5. **COM interop smoke test:** Load dbgshim.dll, call `RegisterForRuntimeStartup` on a simple .NET console app, verify callback fires
6. **DAP protocol integration test:** Launch adapter process, send initialize/launch/setBreakpoints/configurationDone sequence, verify correct DAP responses
7. **Regression test:** Verify existing interpreter-based `DebugSession` still works for non-game console projects
8. **Manual end-to-end test:** Set breakpoint in game project, hit F5, verify: game window opens, breakpoint hits, call stack shows `.bas` lines, variables are readable, stepping works
