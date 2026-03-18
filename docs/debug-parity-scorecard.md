# Debug Feature Parity Scorecard: VGS IDE vs VS Code

**Date:** 2026-03-18
**VGS IDE Version:** Current (Avalonia-based IDE with DAP client)
**VS Code Baseline:** VS Code 1.96+ with built-in debug features

---

## Overall Score: 62/100

---

### Category Scores

| Category | VS Code | VGS IDE | Score |
|----------|---------|---------|-------|
| Breakpoints | 10/10 | 8/10 | 80% |
| Stepping | 10/10 | 8/10 | 80% |
| Variables | 10/10 | 7/10 | 70% |
| Call Stack | 10/10 | 5/10 | 50% |
| Output / Console | 10/10 | 6/10 | 60% |
| Launch / Config | 10/10 | 5/10 | 50% |
| Editor Integration | 10/10 | 7/10 | 70% |
| Exception Handling | 10/10 | 6/10 | 60% |
| Performance / Reliability | 10/10 | 4/10 | 40% |
| UX Polish | 10/10 | 6/10 | 60% |

---

### Detailed Feature Matrix

#### 1. Breakpoints (8/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Line breakpoints | Yes | Yes | Complete | Toggle via gutter click or F9; BreakpointMargin renders red dots |
| Conditional breakpoints | Yes | Yes | Complete | UI for condition editing, DAP sends `condition` field, adapter declares support |
| Hit count breakpoints | Yes | Yes | Complete | `hitCondition` field sent via DAP; adapter declares `supportsHitConditionalBreakpoints` |
| Logpoints (tracepoints) | Yes | Yes | Complete | `logMessage` field sent via DAP; adapter declares `supportsLogPoints` |
| Function breakpoints | Yes | Yes | Complete | Full UI: add/remove/toggle/condition; `setFunctionBreakpoints` DAP request wired |
| Data breakpoints (watchpoints) | Yes | Yes | Complete | Full UI with access type selection (read/write/readWrite); `setDataBreakpoints` DAP request |
| Inline breakpoints | Yes | No | Missing | VS Code supports column-level breakpoints within a line; VGS has no UI or DAP support |
| Breakpoint enable/disable | Yes | Yes | Complete | Per-breakpoint toggle, enable-all/disable-all commands |
| Breakpoint persistence | Yes | Yes | Complete | Saved to `.vgs/breakpoints.json` per project; loaded on project open |
| Breakpoint verification | Yes | Yes | Complete | DAP response updates `IsVerified` status on breakpoint items |
| Breakpoint gutter icons | Yes | Yes | Complete | Different icons for normal, conditional, hit-count, logpoint, and disabled states |
| Edit breakpoint conditions inline | Yes | Yes | Complete | `EditConditionRequested` event triggers condition dialog |
| Remove all breakpoints | Yes | Yes | Complete | `RemoveAllBreakpoints` command clears all files |

#### 2. Stepping (8/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Step Over (F10) | Yes | Yes | Complete | `next` DAP request with 5s timeout |
| Step Into (F11) | Yes | Yes | Complete | `stepIn` DAP request with 5s timeout |
| Step Out (Shift+F11) | Yes | Yes | Complete | `stepOut` DAP request with 5s timeout |
| Continue (F5) | Yes | Yes | Complete | `continue` DAP request |
| Pause | Yes | Yes | Complete | `pause` DAP request |
| Run to Cursor | Yes | Yes | Complete | Temporary breakpoint injected, originals restored after stop |
| Set Next Statement | Yes | Yes | Partial | UI wired to `gotoTargets` + `goto` DAP requests; adapter declares `supportsGotoTargetsRequest = false` so backend does not implement it |
| Step Back | Yes | No | Missing | Adapter explicitly declares `supportsStepBack = false`; no UI |
| Step Into Targets | Yes | No | Missing | No `stepInTargets` support; adapter does not declare it |
| Reverse debugging | Yes (with adapter) | No | Missing | No reverse continue or reverse step |

#### 3. Variables (7/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Local variables display | Yes | Yes | Complete | VariablesViewModel fetches scopes + variables via DAP |
| Variable tree expansion | Yes | Yes | Complete | Lazy-loading children via `variablesReference`; VariableTreeItem with expand on demand |
| Variable types shown | Yes | Yes | Complete | Type column populated from DAP response |
| Scope grouping (Locals, Args, etc.) | Yes | Yes | Complete | Scope nodes rendered as expandable headers |
| Watch expressions | Yes | Yes | Complete | Dedicated WatchViewModel with add/remove/evaluate/refresh; also watch in VariablesViewModel |
| Evaluate on hover | Yes | No | Missing | Adapter declares `supportsEvaluateForHovers = true` but IDE has no hover-to-evaluate wiring |
| Set variable value | Yes | No | Missing | Adapter declares `supportsSetVariable = false`; no in-place editing |
| Copy variable value | Yes | Yes | Complete | WatchViewModel has CopyValue/CopyExpression commands |
| Variable value formatting | Yes | No | Missing | Adapter declares `supportsValueFormattingOptions = false`; no hex/binary toggle |
| Lazy variable loading | Yes | Yes | Complete | `IsExpandable` flag triggers child load only when expanded |

#### 4. Call Stack (5/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Stack frame display | Yes | Yes | Complete | CallStackViewModel fetches frames via `stackTrace` DAP request |
| Navigate to frame source | Yes | Yes | Complete | `FrameSelected` event navigates editor to file:line |
| Frame display format | Yes | Yes | Complete | Shows `FunctionName at File:Line` |
| Multi-thread view | Yes | No | Missing | Only requests threadId=1; no thread picker or multi-thread panel |
| Thread switching | Yes | No | Missing | No UI for selecting threads; hardcoded to thread 1 |
| Restart frame | Yes | No | Missing | Adapter declares `supportsRestartFrame = false` |
| Loaded modules view | Yes | No | Missing | Adapter declares `supportsModulesRequest = false` |
| Call stack context menu | Yes | Partial | Partial | Frame selection works but no copy-frame, no restart-frame, no switch-thread |
| Delayed stack trace loading | Yes | No | Missing | Adapter declares `supportsDelayedStackTraceLoading = false` |
| Stack frame deemphasis | Yes | No | Missing | No deemphasis of external/library frames |

#### 5. Output / Console (6/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Debug output capture | Yes | Yes | Complete | `output` DAP events routed to OutputPanelViewModel |
| Stdout/stderr separation | Yes | Yes | Complete | Category-based output (stdout, stderr, console) |
| Debug console (REPL) | Yes | Yes | Complete | ImmediateWindowViewModel with `evaluate` DAP request; command history |
| Debug console completions | Yes | No | Missing | Adapter declares `supportsCompletionsRequest = false` |
| Program stdin input | Yes | Yes | Complete | OutputPanel has input field, `SendInputAsync` sends to process stdin |
| Integrated terminal | Yes | Yes | Partial | TerminalViewModel exists; external console launch supported |
| Output categories/channels | Yes | Yes | Complete | Build, Debug, General output categories with channel selector |
| Clickable output links | Yes | No | Missing | No file:line link detection in output |
| ANSI color support | Yes | No | Missing | Output is plain text, no ANSI escape processing |
| Clear output | Yes | Yes | Complete | Clear command available |

#### 6. Launch / Config (5/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Start debugging (F5) | Yes | Yes | Complete | Build-then-debug flow with breakpoint sync |
| Start without debugging | Yes | Yes | Complete | `StartWithoutDebuggingAsync` runs exe directly |
| Run in external console | Yes | Yes | Complete | Creates batch file, launches in separate cmd window |
| Launch configuration dialog | Yes | Yes | Complete | LaunchConfigurationDialog with name, args, env vars, working dir |
| Stop on entry | Yes | Yes | Complete | `stopOnEntry` passed in DAP launch request |
| Environment variables | Yes | Yes | Complete | KEY=VALUE pairs in launch config dialog |
| Working directory config | Yes | Yes | Complete | `${ProjectDir}` macro supported |
| Command-line arguments | Yes | Yes | Complete | Passed via launch config |
| launch.json equivalent | Yes | No | Missing | No JSON-based launch config file; only UI dialog, not persisted across sessions |
| Attach to process | Yes | No | Missing | Only launch mode; no attach-to-running-process support |
| Restart debug session | Yes | Partial | Partial | `RestartAsync` exists on IDebugService and DebugService but no UI command wired in MainWindowViewModel |
| Multiple debug configurations | Yes | Partial | Partial | LaunchConfigurationDialog supports multiple configs but no config selector in toolbar |
| Compound launch | Yes | No | Missing | No multi-target debug launch |
| Pre-launch tasks | Yes | Partial | Partial | Build runs automatically before debug, but no custom pre-launch task system |

#### 7. Editor Integration (7/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Breakpoint gutter margin | Yes | Yes | Complete | BreakpointMargin with click-to-toggle; DPI-aware rendering |
| Current execution line highlight | Yes | Yes | Complete | Yellow arrow + line highlight in BreakpointMargin; scrolls to line |
| Inline debug values | Yes | Yes | Complete | ShowInlineDebugValuesAsync renders variable values next to code lines |
| Inline value truncation | Yes | Yes | Complete | Values >80 chars truncated with "..." |
| Editor focus on break | Yes | Yes | Complete | MainWindow.Activate() + Topmost toggle on stopped event |
| Navigate to stopped location | Yes | Yes | Complete | Opens file, scrolls to line, highlights execution line |
| Debug hover tooltips | Yes | No | Missing | No debug-time hover evaluation despite adapter support |
| Breakpoint decoration in scrollbar | Yes | No | Missing | No minimap or scrollbar breakpoint indicators |
| Exception widget | Yes | No | Missing | Exception text shown in status bar only, no inline exception widget |
| Run/Debug CodeLens | Yes | No | Missing | No CodeLens-based run/debug above functions |

#### 8. Exception Handling (6/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Break on all exceptions | Yes | Yes | Complete | `all` filter sent via `setExceptionBreakpoints` |
| Break on uncaught exceptions | Yes | Yes | Complete | `uncaught` filter (default enabled) |
| Exception settings dialog | Yes | Yes | Complete | ShowExceptionSettingsAsync with per-type thrown/user-unhandled toggles |
| Exception filter options | Yes | Yes | Complete | `ExceptionFilterOption` with condition per exception type |
| Exception info in stopped event | Yes | Yes | Complete | `StopReason.Exception` with text description |
| Exception categories | Yes | Yes | Complete | Runtime, IO, User exception categories in dialog |
| Exception conditions | Yes | Yes | Complete | Individual exception type conditions via filterOptions |
| First-chance exception handling | Yes | Partial | Partial | NetDebugProcess handles first-chance via ICorDebugManagedCallback; continues by default |
| Exception callstack in dialog | Yes | No | Missing | No dedicated exception detail view with stack trace |
| Caught/uncaught granularity | Yes | Yes | Complete | Separate thrown vs user-unhandled per exception type |

#### 9. Performance / Reliability (4/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Stable debug sessions | Yes | Partial | Partial | ICorDebug-based debugging via dbgshim works but relies on COM interop with raw vtable calls |
| Fast breakpoint binding | Yes | Partial | Partial | Breakpoints set after launch but before configurationDone; pending breakpoints bound on module load |
| Responsive stepping | Yes | Partial | Partial | 5-second timeout on step requests to prevent hangs |
| Graceful disconnect | Yes | Yes | Complete | Disconnect with 2-second timeout, then force kill |
| Process cleanup | Yes | Yes | Complete | CleanupProcesses kills debug and target processes |
| Deadlock prevention | Yes | Yes | Complete | DebugService.Dispose() uses synchronous cleanup to avoid thread pool starvation |
| Large variable trees | Yes | Partial | Partial | Lazy loading works but no paging (supportsVariablePaging=false) |
| Source mapping accuracy | Yes | Yes | Complete | SourceMapper reads portable PDB, indexes sequence points by file and method |
| Hot reload | Yes | No | Missing | No edit-and-continue or hot reload support |
| Debug session recovery | Yes | No | Missing | No recovery from adapter crashes |

#### 10. UX Polish (6/10)

| Feature | VS Code | VGS IDE | Status | Notes |
|---------|---------|---------|--------|-------|
| Debug toolbar | Yes | Yes | Complete | Continue, Step Over, Step Into, Step Out, Restart, Stop buttons |
| Debug status in status bar | Yes | Yes | Complete | "Running", "Paused", "Stopped" text + breakpoint hit / step complete messages |
| Debug menu | Yes | Yes | Complete | Full Debug menu with all commands |
| Keyboard shortcuts | Yes | Yes | Complete | F5, F10, F11, Shift+F11, F9 standard shortcuts |
| Debug panels auto-show | Yes | Partial | Partial | Panels can be activated via commands but don't auto-show on debug start |
| Command palette integration | Yes | Yes | Complete | CommandPaletteViewModel registers debug commands |
| Breakpoints panel | Yes | Yes | Complete | Dedicated BreakpointsViewModel with list, navigate, edit condition |
| Function breakpoints panel | Yes | Yes | Complete | Add/remove/toggle in breakpoints panel |
| Data breakpoints panel | Yes | Yes | Complete | Separate section in breakpoints panel |
| Debug view container (sidebar) | Yes | No | Missing | No unified debug sidebar; panels are separate dock tools |
| Floating debug toolbar | Yes | No | Missing | Debug controls only in menu/toolbar, not a floating overlay |
| Debug session picker | Yes | No | Missing | Single session only; no multi-session switcher |

---

### Summary of Key Gaps

#### High Priority (Major missing features)

| Gap | Impact | Effort |
|-----|--------|--------|
| Debug hover tooltips | Users expect to see variable values on hover during debugging | Medium |
| Multi-thread support | Only thread 1 is accessible; multi-threaded programs cannot be debugged effectively | High |
| Attach to process | Cannot debug already-running processes | High |
| Restart debug session (UI) | RestartAsync exists but no toolbar button or menu command wired | Low |
| launch.json persistence | Launch configurations are not saved/loaded between IDE sessions | Medium |

#### Medium Priority (Polish and completeness)

| Gap | Impact | Effort |
|-----|--------|--------|
| Debug panels auto-show on debug start | Users must manually open panels | Low |
| Set Next Statement backend | UI sends gotoTargets request but adapter returns `supportsGotoTargetsRequest = false` | Medium |
| Debug console completions | No autocomplete in Immediate Window during debug | Medium |
| Inline breakpoints (column-level) | Cannot set breakpoints at specific columns | Medium |
| Step Into Targets | Cannot choose which function to step into on multi-call lines | Medium |
| Clickable output links | File:line references in output are not clickable | Low |

#### Low Priority (Nice-to-have)

| Gap | Impact | Effort |
|-----|--------|--------|
| Step Back / Reverse debugging | Niche feature, few adapters support it | Very High |
| Hot reload | Edit-and-continue requires deep CLR integration | Very High |
| ANSI color support in output | Cosmetic improvement | Low |
| Floating debug toolbar | UI convenience | Low |
| Debug sidebar container | Layout preference | Medium |
| Variable value formatting (hex/binary) | Developer convenience | Low |
| Exception detail widget | Better exception UX during debugging | Medium |

---

### Architecture Assessment

**Strengths:**
- Full DAP protocol implementation in both client (DebugService) and server (NetDebugAdapter)
- Native CLR debugging via ICorDebug/dbgshim (not a managed debugger wrapper)
- Comprehensive breakpoint model: source, function, data, conditional, hit count, logpoint
- Portable PDB source mapping with sequence point indexing
- Variable tree with lazy child loading
- Breakpoint persistence across sessions
- Clean separation: IDebugService interface, DebugService DAP client, NetDebugAdapter DAP server

**Weaknesses:**
- Single-thread assumption throughout (threadId=1 hardcoded)
- COM interop uses raw vtable slot offsets (fragile, version-sensitive)
- No hover evaluation despite adapter support
- RestartAsync implemented but not exposed in UI
- Set Next Statement UI exists but adapter does not implement gotoTargets
- No debug configuration persistence (launch.json equivalent)
- No attach mode

---

### Scoring Methodology

Each category is scored 1-10 based on:
- **Feature completeness**: What percentage of VS Code's features are implemented?
- **Functional correctness**: Do implemented features actually work end-to-end?
- **UI wiring**: Is the backend capability exposed through the IDE's UI?

A feature marked "Complete" means both the DAP protocol handling and the IDE UI are implemented and connected. "Partial" means either the UI exists without backend support, or the backend exists without UI exposure. "Missing" means neither UI nor backend implementation exists.
