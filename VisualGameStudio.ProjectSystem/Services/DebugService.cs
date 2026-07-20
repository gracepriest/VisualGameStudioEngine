using System.Diagnostics;
using System.Text.Json;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// DAP client that communicates with BasicLang debug adapter
/// </summary>
public class DebugService : IDebugService
{
    // Phase 4: the DAP transport (adapter process, framing, correlation) lives in
    // DapSession; this service orchestrates IDE debug state on top of it.
    private DapSession? _session;
    private Process? _targetProcess;
    private StreamWriter? _stdinWriter;
    private readonly string _compilerPath;
    private readonly IOutputService _outputService;
    private readonly Func<ProcessStartInfo, DapSession> _sessionFactory;
    // TODO(Task 8): upgrade to the debugger descriptor's DisplayName.
    private string _adapterDisplayName = "debug adapter";

    // Run to cursor state
    private string? _runToCursorFile;
    private int _runToCursorLine;
    private List<SourceBreakpoint>? _originalBreakpoints;

    // The threadId of the most recent stopped event; 1 until the first stop of
    // each session (reset at session start — DAP offers nothing better pre-stop).
    // The Step-0 gate observed lldb-dap stopping on threadId 6908 — hardcoding 1
    // breaks every native-path continue/step/pause/goto/stackTrace.
    private int _currentThreadId = 1;

    // Restart state
    private DebugConfiguration? _lastConfig;
    private Dictionary<string, IEnumerable<SourceBreakpoint>>? _lastBreakpoints;

    // Exception filters the UI armed, retained across sessions: the handshake's
    // configuration phase replays them, so filters armed BEFORE a session exists
    // are no longer silently dropped.
    private List<string>? _armedExceptionFilters;
    private List<ExceptionFilterOption>? _armedExceptionFilterOptions;

    public DebugState State { get; private set; } = DebugState.NotStarted;
    public bool IsDebugging => State == DebugState.Running || State == DebugState.Paused;

    /// <summary>
    /// What the adapter disclosed in its initialize response; null until a session's
    /// initialize response arrives (and again once the session is torn down).
    /// </summary>
    public DapCapabilities? Capabilities => _session?.Capabilities;

    /// <summary>
    /// Process ID of the most recently started debug-adapter process, or null if an
    /// adapter was never started. Deliberately retained after Stop/Dispose so callers
    /// (diagnostics, integration tests) can verify the adapter process actually exited.
    /// </summary>
    public int? AdapterProcessId { get; private set; }

    public event EventHandler<DebugStateChangedEventArgs>? StateChanged;
    public event EventHandler<StoppedEventArgs>? Stopped;
    public event EventHandler<DebugOutputEventArgs>? OutputReceived;
    public event EventHandler<BreakpointsChangedEventArgs>? BreakpointsChanged;

    /// <summary>
    /// The optional factory is the test seam for injecting stream-backed
    /// <see cref="DapSession"/>s. MS.DI honors defaulted parameters, so the plain
    /// AddSingleton registration keeps resolving.
    /// </summary>
    public DebugService(IOutputService outputService,
        Func<ProcessStartInfo, DapSession>? sessionFactory = null)
    {
        _outputService = outputService;
        _sessionFactory = sessionFactory ?? (startInfo => new DapSession(startInfo, outputService));
        var baseDir = AppContext.BaseDirectory;
        _compilerPath = Path.Combine(baseDir, "BasicLang.dll");

        if (!File.Exists(_compilerPath))
        {
            // Try to find BasicLang.dll in the solution's BasicLang project
            var solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            var possiblePaths = new[]
            {
                Path.Combine(solutionDir, "BasicLang", "bin", "Release", "net8.0", "BasicLang.dll"),
                Path.Combine(solutionDir, "BasicLang", "bin", "Debug", "net8.0", "BasicLang.dll"),
                Path.Combine(solutionDir, "BasicLang", "binReleaseFinal", "BasicLang.dll"),
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _compilerPath = path;
                    break;
                }
            }
        }
    }

    public Task<bool> StartDebuggingAsync(DebugConfiguration config, CancellationToken cancellationToken = default)
    {
        return StartDebuggingAsync(config, new Dictionary<string, IEnumerable<SourceBreakpoint>>(), cancellationToken);
    }

    public async Task<bool> StartDebuggingAsync(DebugConfiguration config, Dictionary<string, IEnumerable<SourceBreakpoint>> breakpoints, CancellationToken cancellationToken = default)
    {
        // Save for restart
        _lastConfig = config;
        _lastBreakpoints = breakpoints;

        if (IsDebugging) return false;

        try
        {
            SetState(DebugState.Initializing);
            // This service is a DI singleton outliving sessions: without the reset, a
            // pre-first-stop pause in session 2 would carry session 1's thread id.
            _currentThreadId = 1;

            // Start the debug adapter
            var startInfo = DapSession.BuildStartInfo("dotnet", $"\"{_compilerPath}\" --debug-adapter", config.WorkingDirectory);
            _adapterDisplayName = startInfo.FileName;

            var session = _sessionFactory(startInfo);
            _session = session;
            // Subscribe before Start so no early event (or an instant adapter death)
            // can be missed by the read loop.
            session.EventReceived += OnAdapterEvent;
            session.Closed += OnSessionClosed;
            session.Start();
            AdapterProcessId = session.AdapterProcessId;

            // Spec-correct startup lives in the session (arm -> initialize -> launch
            // in flight -> initialized -> configuration -> configurationDone -> launch
            // response). Breakpoints ride the configuration phase, between
            // `initialized` and configurationDone.
            await session.InitializeAndLaunchAsync(
                "launch",
                new
                {
                    program = config.Program,
                    cwd = config.WorkingDirectory,
                    args = config.Arguments,
                    stopOnEntry = config.StopOnEntry,
                    noDebug = false
                },
                InitializeArguments(),
                () => PushConfigurationAsync(breakpoints),
                cancellationToken);

            SetState(DebugState.Running);
            _outputService.WriteLine("Debugging started", OutputCategory.Debug);
            return true;
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to start debugging: {ex.Message}", OutputCategory.Debug);
            await StopDebuggingAsync();
            return false;
        }
    }

    public async Task<bool> AttachToProcessAsync(int processId, Dictionary<string, IEnumerable<SourceBreakpoint>>? breakpoints = null, CancellationToken cancellationToken = default)
    {
        if (IsDebugging) return false;

        try
        {
            SetState(DebugState.Initializing);
            // Same cross-session staleness guard as StartDebuggingAsync.
            _currentThreadId = 1;

            // Start the debug adapter process (attach: no WorkingDirectory)
            var startInfo = DapSession.BuildStartInfo("dotnet", $"\"{_compilerPath}\" --debug-adapter", null);
            _adapterDisplayName = startInfo.FileName;

            var session = _sessionFactory(startInfo);
            _session = session;
            // Subscribe before Start so no early event (or an instant adapter death)
            // can be missed by the read loop.
            session.EventReceived += OnAdapterEvent;
            session.Closed += OnSessionClosed;
            session.Start();
            AdapterProcessId = session.AdapterProcessId;

            // Same spec-correct startup as launch — "attach" instead of "launch".
            await session.InitializeAndLaunchAsync(
                "attach",
                new
                {
                    processId = processId
                },
                InitializeArguments(),
                () => PushConfigurationAsync(breakpoints),
                cancellationToken);

            SetState(DebugState.Running);
            _outputService.WriteLine($"Attached to process {processId}", OutputCategory.Debug);
            return true;
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to attach to process: {ex.Message}", OutputCategory.Debug);
            await StopDebuggingAsync();
            return false;
        }
    }

    /// <summary>The initialize arguments both launch and attach send, verbatim.</summary>
    private static object InitializeArguments() => new
    {
        clientID = "visualgamestudio",
        clientName = "Visual Game Studio",
        adapterID = "basiclang",
        pathFormat = "path",
        linesStartAt1 = true,
        columnsStartAt1 = true,
        supportsVariableType = true,
        supportsVariablePaging = false,
        supportsRunInTerminalRequest = false
    };

    /// <summary>
    /// The configuration phase of the handshake (between `initialized` and
    /// configurationDone): source breakpoints, then any armed exception filters.
    /// </summary>
    private async Task PushConfigurationAsync(Dictionary<string, IEnumerable<SourceBreakpoint>>? breakpoints)
    {
        if (breakpoints != null)
        {
            foreach (var kvp in breakpoints)
            {
                var filePath = kvp.Key;
                var bps = kvp.Value.ToList();
                if (bps.Any())
                {
                    _outputService.WriteLine($"Setting {bps.Count} breakpoint(s) in {Path.GetFileName(filePath)}", OutputCategory.Debug);
                    await SetBreakpointsAsync(filePath, bps);
                }
            }
        }

        // Filters armed before the session existed used to be dropped (the setter
        // no-ops without a live session); replay them here. Best-effort — a filter
        // problem must not kill the launch.
        if (_armedExceptionFilters is { Count: > 0 })
        {
            try
            {
                await SendExceptionBreakpointsCoreAsync(_armedExceptionFilters, _armedExceptionFilterOptions);
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"Failed to set exception breakpoints: {ex.Message}", OutputCategory.Debug);
            }
        }
    }

    public async Task<bool> StartWithoutDebuggingAsync(DebugConfiguration config, CancellationToken cancellationToken = default)
    {
        if (IsDebugging) return false;

        try
        {
            SetState(DebugState.Running);

            // Run the executable directly
            var startInfo = new ProcessStartInfo
            {
                FileName = config.Program,  // The executable path
                Arguments = config.Arguments != null ? string.Join(" ", config.Arguments) : "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = config.WorkingDirectory
            };

            _targetProcess = new Process { StartInfo = startInfo };
            _targetProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    OutputReceived?.Invoke(this, new DebugOutputEventArgs { Category = "stdout", Output = e.Data + "\n" });
                }
            };
            _targetProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    OutputReceived?.Invoke(this, new DebugOutputEventArgs { Category = "stderr", Output = e.Data + "\n" });
                }
            };
            _targetProcess.EnableRaisingEvents = true;
            _targetProcess.Exited += (s, e) =>
            {
                // Wait a short time to ensure all output is captured before signaling completion
                Thread.Sleep(100);
                var exitCode = 0;
                try { exitCode = _targetProcess?.ExitCode ?? 0; } catch { }
                OutputReceived?.Invoke(this, new DebugOutputEventArgs { Category = "stdout", Output = $"\nProgram exited with code {exitCode}\n" });
                SetState(DebugState.Stopped);
            };

            _targetProcess.Start();
            _targetProcess.BeginOutputReadLine();
            _targetProcess.BeginErrorReadLine();
            _stdinWriter = _targetProcess.StandardInput;

            _outputService.WriteLine("Program started (no debugging)", OutputCategory.Debug);
            return true;
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to run program: {ex.Message}", OutputCategory.Debug);
            SetState(DebugState.Stopped);
            return false;
        }
    }

    public async Task SendInputAsync(string input)
    {
        if (_stdinWriter != null && _targetProcess != null && !_targetProcess.HasExited)
        {
            try
            {
                await _stdinWriter.WriteLineAsync(input);
                await _stdinWriter.FlushAsync();
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"Failed to send input: {ex.Message}", OutputCategory.Debug);
            }
        }
    }

    public async Task RestartAsync()
    {
        if (_lastConfig == null) return;

        var config = _lastConfig;
        var breakpoints = _lastBreakpoints;

        await StopDebuggingAsync();

        // Brief delay for cleanup
        await Task.Delay(200);

        if (breakpoints != null)
            await StartDebuggingAsync(config, breakpoints);
        else
            await StartDebuggingAsync(config);
    }

    public async Task StopDebuggingAsync()
    {
        // Try to send disconnect within the profile's grace budget before killing
        var session = _session;
        if (session != null && State != DebugState.Stopped)
        {
            try
            {
                await session.SendRequestAsync("disconnect", new { terminateDebuggee = true },
                    timeout: session.Timeouts.DisconnectGrace);
            }
            catch (OperationCanceledException)
            {
                // Grace expired - proceed with the kill-tree cleanup below
                _outputService.WriteLine("Disconnect request timed out, forcing stop", OutputCategory.Debug);
            }
            catch
            {
                // Ignore other errors during disconnect
            }
        }

        // Cancel all pending DAP requests to avoid leaked tasks (the read loop
        // is cancelled by the session's Dispose inside CleanupProcesses)
        _session?.CancelPending();

        CleanupProcesses();
        SetState(DebugState.Stopped);
        _outputService.WriteLine("Debugging stopped", OutputCategory.Debug);
    }

    private readonly object _cleanupLock = new();

    private void CleanupProcesses()
    {
        // Can be invoked concurrently (terminated event on the read-loop
        // thread racing user Stop / Dispose on the UI thread). Atomically
        // claim the fields under a lock so each is torn down exactly once,
        // and HasExited is never called on a disposed Process.
        DapSession? session;
        Process? targetProcess;

        lock (_cleanupLock)
        {
            try { _stdinWriter?.Dispose(); } catch { }
            _stdinWriter = null;

            session = _session;
            targetProcess = _targetProcess;
            _session = null;
            _targetProcess = null;
        }

        // The adapter transport half (writer/reader dispose + kill of the adapter
        // process tree — the game/debuggee is a child of the adapter) lives in
        // DapSession.Dispose now.
        session?.Dispose();

        // Kill the target process tree (used in "Run without debugging" mode)
        if (targetProcess != null)
        {
            try
            {
                if (!targetProcess.HasExited)
                {
                    targetProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            try { targetProcess.Dispose(); } catch { }
        }
    }

    public async Task ContinueAsync()
    {
        if (State != DebugState.Paused) return;
        SetState(DebugState.Running);
        await SendRequestAsync("continue", new { threadId = _currentThreadId });
    }

    public async Task StepOverAsync()
    {
        _outputService.WriteLine($"[DAP] StepOver called, State={State}", OutputCategory.Debug);
        if (State != DebugState.Paused)
        {
            _outputService.WriteLine($"[DAP] StepOver SKIPPED - not paused (State={State})", OutputCategory.Debug);
            return;
        }
        SetState(DebugState.Running);
        await SendStepRequestAsync("next");
    }

    public async Task StepIntoAsync()
    {
        _outputService.WriteLine($"[DAP] StepInto called, State={State}", OutputCategory.Debug);
        if (State != DebugState.Paused)
        {
            _outputService.WriteLine($"[DAP] StepInto SKIPPED - not paused (State={State})", OutputCategory.Debug);
            return;
        }
        SetState(DebugState.Running);
        await SendStepRequestAsync("stepIn");
    }

    public async Task StepOutAsync()
    {
        _outputService.WriteLine($"[DAP] StepOut called, State={State}", OutputCategory.Debug);
        if (State != DebugState.Paused)
        {
            _outputService.WriteLine($"[DAP] StepOut SKIPPED - not paused (State={State})", OutputCategory.Debug);
            return;
        }
        SetState(DebugState.Running);
        await SendStepRequestAsync("stepOut");
    }

    /// <summary>
    /// Send a step request (next/stepIn/stepOut) within the profile's step budget.
    /// Step commands don't need the response body — the stopped event is what matters.
    /// </summary>
    private async Task SendStepRequestAsync(string command)
    {
        var session = _session;
        if (session == null) return;

        try
        {
            await session.SendRequestAsync(command, new { threadId = _currentThreadId }, timeout: session.Timeouts.Step);
            _outputService.WriteLine($"[DAP] {command} response received, State={State}", OutputCategory.Debug);
        }
        catch (OperationCanceledException)
        {
            _outputService.WriteLine($"[DAP] {command} response timed out ({session.Timeouts.Step.TotalSeconds:F0}s) - continuing anyway", OutputCategory.Debug);
        }
        catch (Exception ex)
        {
            _outputService.WriteLine($"[DAP] {command} error: {ex.Message}", OutputCategory.Debug);
        }
    }

    public async Task PauseAsync()
    {
        if (State != DebugState.Running) return;
        await SendRequestAsync("pause", new { threadId = _currentThreadId });
    }

    public async Task RunToCursorAsync(string filePath, int line, IEnumerable<SourceBreakpoint>? existingBreakpoints = null)
    {
        if (State != DebugState.Paused) return;

        _runToCursorFile = filePath;
        _runToCursorLine = line;

        // Store original breakpoints so we can restore them after stopping
        _originalBreakpoints = existingBreakpoints?.ToList() ?? new List<SourceBreakpoint>();

        try
        {
            // Combine existing breakpoints with the temporary run-to-cursor breakpoint
            var allBreakpoints = _originalBreakpoints
                .Where(bp => bp.Line != line) // Remove any existing at same line
                .Concat(new[] { new SourceBreakpoint { Line = line } })
                .Select(bp => new { line = bp.Line, condition = bp.Condition, hitCondition = bp.HitCondition, logMessage = bp.LogMessage })
                .ToArray();

            await SendRequestAsync("setBreakpoints", new
            {
                source = new { path = filePath },
                breakpoints = allBreakpoints
            });

            _outputService.WriteLine($"Run to cursor: line {line} in {Path.GetFileName(filePath)}", OutputCategory.Debug);

            // Continue execution
            SetState(DebugState.Running);
            await SendRequestAsync("continue", new { threadId = _currentThreadId });
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Run to cursor failed: {ex.Message}", OutputCategory.Debug);
            ClearRunToCursorState();
        }
    }

    private void ClearRunToCursorState()
    {
        _runToCursorFile = null;
        _runToCursorLine = 0;
        _originalBreakpoints = null;
    }

    public async Task SetExceptionBreakpointsAsync(IEnumerable<string> filters, IEnumerable<ExceptionFilterOption>? filterOptions = null)
    {
        var filtersList = filters.ToList();
        var optionsList = filterOptions?.ToList();

        // Retain even without a live session — the next handshake's configuration
        // phase replays armed filters (PushConfigurationAsync).
        _armedExceptionFilters = filtersList;
        _armedExceptionFilterOptions = optionsList;

        if (!IsDebugging) return;

        try
        {
            await SendExceptionBreakpointsCoreAsync(filtersList, optionsList);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to set exception breakpoints: {ex.Message}", OutputCategory.Debug);
        }
    }

    /// <summary>The wire half of setExceptionBreakpoints, shared by the mid-session setter and the handshake replay.</summary>
    private async Task SendExceptionBreakpointsCoreAsync(List<string> filters, List<ExceptionFilterOption>? filterOptions)
    {
        var args = new Dictionary<string, object>
        {
            ["filters"] = filters
        };

        if (filterOptions != null)
        {
            var options = filterOptions.Select(fo => new
            {
                filterId = fo.FilterId,
                condition = fo.Condition
            }).ToArray();

            if (options.Length > 0)
            {
                args["filterOptions"] = options;
            }
        }

        await SendRequestAsync("setExceptionBreakpoints", args);

        var filterNames = string.Join(", ", filters);
        _outputService.WriteLine($"Exception breakpoints set: {(filters.Count > 0 ? filterNames : "none")}", OutputCategory.Debug);
    }

    public async Task<bool> SetNextStatementAsync(string filePath, int line)
    {
        if (State != DebugState.Paused) return false;

        try
        {
            // First, get goto targets for the specified location
            var targetsResult = await SendRequestAsync("gotoTargets", new
            {
                source = new { path = filePath },
                line
            });

            // Check if we got any valid targets
            if (targetsResult.TryGetProperty("targets", out var targets) &&
                targets.ValueKind == JsonValueKind.Array)
            {
                var targetArray = targets.EnumerateArray().ToArray();
                if (targetArray.Length > 0)
                {
                    // Get the first (or closest) target
                    var target = targetArray[0];
                    if (target.TryGetProperty("id", out var targetId))
                    {
                        // Execute the goto request
                        await SendRequestAsync("goto", new
                        {
                            threadId = _currentThreadId,
                            targetId = targetId.GetInt32()
                        });

                        _outputService.WriteLine($"Set next statement to line {line} in {Path.GetFileName(filePath)}", OutputCategory.Debug);
                        return true;
                    }
                }
            }

            _outputService.WriteError($"Cannot set next statement to line {line} - no valid target found", OutputCategory.Debug);
            return false;
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Set next statement failed: {ex.Message}", OutputCategory.Debug);
            return false;
        }
    }

    private async Task RestoreBreakpointsAfterRunToCursorAsync()
    {
        if (_runToCursorFile == null || _originalBreakpoints == null) return;

        var filePath = _runToCursorFile;
        var originalBps = _originalBreakpoints;
        ClearRunToCursorState();

        try
        {
            // Restore only the original breakpoints (without the temporary run-to-cursor one)
            var breakpoints = originalBps
                .Select(bp => new { line = bp.Line, condition = bp.Condition, hitCondition = bp.HitCondition, logMessage = bp.LogMessage })
                .ToArray();

            await SendRequestAsync("setBreakpoints", new
            {
                source = new { path = filePath },
                breakpoints
            });

            _outputService.WriteLine("Run to cursor completed, breakpoints restored", OutputCategory.Debug);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to restore breakpoints: {ex.Message}", OutputCategory.Debug);
        }
    }

    public async Task<IReadOnlyList<BreakpointInfo>> SetBreakpointsAsync(string filePath, IEnumerable<SourceBreakpoint> breakpoints)
    {
        if (_session == null) return Array.Empty<BreakpointInfo>();

        var bpList = breakpoints.ToList();

        try
        {
            var result = await SendRequestAsync("setBreakpoints", new
            {
                source = new { path = filePath },
                breakpoints = bpList.Select(bp => new
                {
                    line = bp.Line,
                    column = bp.Column,
                    condition = bp.Condition,
                    hitCondition = bp.HitCondition,
                    logMessage = bp.LogMessage
                }).ToArray()
            });

            var verified = new List<BreakpointInfo>();
            if (result.TryGetProperty("breakpoints", out var bps) && bps.ValueKind == JsonValueKind.Array)
            {
                foreach (var bp in bps.EnumerateArray())
                {
                    verified.Add(new BreakpointInfo
                    {
                        Id = bp.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        Verified = bp.TryGetProperty("verified", out var v) && v.GetBoolean(),
                        Message = bp.TryGetProperty("message", out var m) ? m.GetString() : null,
                        Line = bp.TryGetProperty("line", out var l) ? l.GetInt32() : 0,
                        Column = bp.TryGetProperty("column", out var c) ? c.GetInt32() : null
                    });
                }
            }

            BreakpointsChanged?.Invoke(this, new BreakpointsChangedEventArgs { FilePath = filePath, Breakpoints = verified });
            return verified;
        }
        catch
        {
            return Array.Empty<BreakpointInfo>();
        }
    }

    public async Task<IReadOnlyList<FunctionBreakpointInfo>> SetFunctionBreakpointsAsync(IEnumerable<FunctionBreakpoint> breakpoints)
    {
        if (_session == null) return Array.Empty<FunctionBreakpointInfo>();

        // Spec §3.3.3: skip what the adapter disclaims — never send the request.
        if (Capabilities?.Supports("supportsFunctionBreakpoints") != true)
            return Array.Empty<FunctionBreakpointInfo>();

        var bpList = breakpoints.ToList();

        try
        {
            var result = await SendRequestAsync("setFunctionBreakpoints", new
            {
                breakpoints = bpList.Select(bp => new
                {
                    name = bp.Name,
                    condition = bp.Condition,
                    hitCondition = bp.HitCondition
                }).ToArray()
            });

            var verified = new List<FunctionBreakpointInfo>();
            if (result.TryGetProperty("breakpoints", out var bps) && bps.ValueKind == JsonValueKind.Array)
            {
                foreach (var bp in bps.EnumerateArray())
                {
                    verified.Add(new FunctionBreakpointInfo
                    {
                        Id = bp.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        Verified = bp.TryGetProperty("verified", out var v) && v.GetBoolean(),
                        Message = bp.TryGetProperty("message", out var m) ? m.GetString() : null
                    });
                }
            }

            return verified;
        }
        catch
        {
            return Array.Empty<FunctionBreakpointInfo>();
        }
    }

    public async Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId = 0)
    {
        if (_session == null) return Array.Empty<StackFrameInfo>();

        // 0 is the sentinel for "the thread of the most recent stopped event"
        // (documented on IDebugService) — DAP thread ids are adapter-assigned
        // and never 0.
        if (threadId == 0) threadId = _currentThreadId;

        try
        {
            var result = await SendRequestAsync("stackTrace", new { threadId, startFrame = 0, levels = 100 });

            var frames = new List<StackFrameInfo>();
            if (result.TryGetProperty("stackFrames", out var sf) && sf.ValueKind == JsonValueKind.Array)
            {
                foreach (var frame in sf.EnumerateArray())
                {
                    var info = new StackFrameInfo
                    {
                        Id = frame.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        Name = frame.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Line = frame.TryGetProperty("line", out var l) ? l.GetInt32() : 0,
                        Column = frame.TryGetProperty("column", out var c) ? c.GetInt32() : 0
                    };

                    if (frame.TryGetProperty("source", out var src) && src.TryGetProperty("path", out var path))
                    {
                        info.FilePath = path.GetString();
                    }

                    frames.Add(info);
                }
            }

            return frames;
        }
        catch
        {
            return Array.Empty<StackFrameInfo>();
        }
    }

    public async Task<IReadOnlyList<ScopeInfo>> GetScopesAsync(int frameId)
    {
        if (_session == null) return Array.Empty<ScopeInfo>();

        try
        {
            var result = await SendRequestAsync("scopes", new { frameId });

            var scopes = new List<ScopeInfo>();
            if (result.TryGetProperty("scopes", out var sc) && sc.ValueKind == JsonValueKind.Array)
            {
                foreach (var scope in sc.EnumerateArray())
                {
                    scopes.Add(new ScopeInfo
                    {
                        Name = scope.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        VariablesReference = scope.TryGetProperty("variablesReference", out var vr) ? vr.GetInt32() : 0,
                        Expensive = scope.TryGetProperty("expensive", out var e) && e.GetBoolean()
                    });
                }
            }

            return scopes;
        }
        catch
        {
            return Array.Empty<ScopeInfo>();
        }
    }

    public async Task<IReadOnlyList<VariableInfo>> GetVariablesAsync(int variablesReference)
    {
        if (_session == null) return Array.Empty<VariableInfo>();

        try
        {
            var result = await SendRequestAsync("variables", new { variablesReference });

            var variables = new List<VariableInfo>();
            if (result.TryGetProperty("variables", out var vars) && vars.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in vars.EnumerateArray())
                {
                    variables.Add(new VariableInfo
                    {
                        Name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Value = v.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "",
                        Type = v.TryGetProperty("type", out var t) ? t.GetString() : null,
                        VariablesReference = v.TryGetProperty("variablesReference", out var vr) ? vr.GetInt32() : 0
                    });
                }
            }

            return variables;
        }
        catch
        {
            return Array.Empty<VariableInfo>();
        }
    }

    public async Task<EvaluateResult> EvaluateAsync(string expression, int? frameId = null, string? context = null)
    {
        if (_session == null) return new EvaluateResult { Result = "Error: Not debugging" };

        try
        {
            var args = new Dictionary<string, object> { ["expression"] = expression, ["context"] = context ?? "watch" };
            if (frameId.HasValue) args["frameId"] = frameId.Value;

            var result = await SendRequestAsync("evaluate", args);

            return new EvaluateResult
            {
                Result = result.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "",
                Type = result.TryGetProperty("type", out var t) ? t.GetString() : null,
                VariablesReference = result.TryGetProperty("variablesReference", out var vr) ? vr.GetInt32() : 0
            };
        }
        catch (Exception ex)
        {
            return new EvaluateResult { Result = $"Error: {ex.Message}" };
        }
    }

    public async Task<IReadOnlyList<DataBreakpointInfo>> SetDataBreakpointsAsync(
        IReadOnlyList<DataBreakpoint> breakpoints,
        CancellationToken cancellationToken = default)
    {
        if (_session == null) return Array.Empty<DataBreakpointInfo>();

        // Spec §3.3.3: skip what the adapter disclaims — never send the request.
        if (Capabilities?.Supports("supportsDataBreakpoints") != true)
            return Array.Empty<DataBreakpointInfo>();

        try
        {
            var result = await SendRequestAsync("setDataBreakpoints", new
            {
                breakpoints = breakpoints.Select(bp => new
                {
                    dataId = bp.DataId,
                    accessType = bp.AccessType,
                    condition = bp.Condition,
                    hitCondition = bp.HitCondition
                }).ToArray()
            }, cancellationToken: cancellationToken);

            var verified = new List<DataBreakpointInfo>();
            if (result.TryGetProperty("breakpoints", out var bps) && bps.ValueKind == JsonValueKind.Array)
            {
                foreach (var bp in bps.EnumerateArray())
                {
                    verified.Add(new DataBreakpointInfo
                    {
                        Id = bp.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        Verified = bp.TryGetProperty("verified", out var v) && v.GetBoolean(),
                        Message = bp.TryGetProperty("message", out var m) ? m.GetString() : null
                    });
                }
            }

            return verified;
        }
        catch
        {
            return Array.Empty<DataBreakpointInfo>();
        }
    }

    public async Task<DataBreakpointAccessInfo?> GetDataBreakpointInfoAsync(
        int variablesReference, string name,
        CancellationToken cancellationToken = default)
    {
        if (_session == null) return null;

        // Spec §3.3.3: skip what the adapter disclaims — never send the request.
        if (Capabilities?.Supports("supportsDataBreakpoints") != true)
            return null;

        try
        {
            var result = await SendRequestAsync("dataBreakpointInfo", new
            {
                variablesReference,
                name
            }, cancellationToken: cancellationToken);

            var dataId = result.TryGetProperty("dataId", out var did) ? did.GetString() : null;
            if (dataId == null) return null;

            var accessTypes = new List<string>();
            if (result.TryGetProperty("accessTypes", out var at) && at.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in at.EnumerateArray())
                {
                    var val = item.GetString();
                    if (val != null) accessTypes.Add(val);
                }
            }

            return new DataBreakpointAccessInfo
            {
                DataId = dataId,
                Description = result.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                AccessTypes = accessTypes,
                CanPersist = result.TryGetProperty("canPersist", out var cp) && cp.GetBoolean()
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync()
    {
        if (_session == null) return Array.Empty<ThreadInfo>();

        try
        {
            var result = await SendRequestAsync("threads", new { });

            var threads = new List<ThreadInfo>();
            if (result.TryGetProperty("threads", out var threadsArray) && threadsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in threadsArray.EnumerateArray())
                {
                    var id = t.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                    var name = t.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                    threads.Add(new ThreadInfo
                    {
                        Id = id,
                        Name = name,
                        Status = "Paused"
                    });
                }
            }

            // Try to get call stack preview for each thread (top frame name)
            foreach (var thread in threads)
            {
                try
                {
                    var frames = await GetStackTraceAsync(thread.Id);
                    if (frames.Count > 0)
                    {
                        thread.CallStackPreview = frames[0].Name;
                    }
                }
                catch
                {
                    // Stack trace may not be available for all threads
                }
            }

            return threads;
        }
        catch
        {
            return Array.Empty<ThreadInfo>();
        }
    }

    private void SetState(DebugState newState)
    {
        var oldState = State;
        if (oldState == newState) return;
        State = newState;
        StateChanged?.Invoke(this, new DebugStateChangedEventArgs { OldState = oldState, NewState = newState });
    }

    /// <summary>
    /// UI half of spec §8 ("session death is an event, not a hang"): when the still-active
    /// session's adapter dies unexpectedly, end the debug session instead of leaving the
    /// IDE hung. A normal Stop/disconnect must NOT double-report — no-op when the session
    /// is no longer the active one or the state is already Stopped.
    /// </summary>
    private void OnSessionClosed(object? sender, DapSessionClosedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session)) return;
        if (State == DebugState.Stopped) return;

        var codeClause = e.ExitCode is int exitCode ? $" (code {exitCode})" : "";
        _outputService.WriteError($"[Debug] Adapter '{_adapterDisplayName}' exited unexpectedly{codeClause} — debug session ended.", OutputCategory.Debug);
        CleanupProcesses();
        SetState(DebugState.Stopped);
    }

    /// <summary>
    /// Adapter events, raised raw by <see cref="DapSession"/>; the handling below is the
    /// pre-extraction ProcessMessage event switch, logic unchanged.
    /// </summary>
    private void OnAdapterEvent(object? sender, DapEventArgs args)
    {
        var eventType = args.EventType;
        var body = args.Body;
        _outputService.WriteLine($"[DAP] Event received: {eventType}", OutputCategory.Debug);

        switch (eventType)
        {
            case "stopped":
                var reason = body.TryGetProperty("reason", out var r2) ? r2.GetString() : "unknown";
                _outputService.WriteLine($"[DAP] Stopped event: reason={reason}, setting state to Paused", OutputCategory.Debug);
                SetState(DebugState.Paused);
                var stoppedArgs = new StoppedEventArgs
                {
                    Reason = ParseStopReason(body.TryGetProperty("reason", out var r) ? r.GetString() : null),
                    ThreadId = body.TryGetProperty("threadId", out var tid) ? tid.GetInt32() : 1,
                    Description = body.TryGetProperty("description", out var d) ? d.GetString() : null,
                    Text = body.TryGetProperty("text", out var txt) ? txt.GetString() : null
                };

                // Every later continue/step/pause/goto/stackTrace targets this thread.
                // Set BEFORE raising Stopped — handlers fetch stacks synchronously.
                _currentThreadId = stoppedArgs.ThreadId;

                // Restore original breakpoints after run-to-cursor
                if (_runToCursorFile != null && _originalBreakpoints != null)
                {
                    _ = RestoreBreakpointsAfterRunToCursorAsync();
                }

                Stopped?.Invoke(this, stoppedArgs);
                break;

            case "output":
                OutputReceived?.Invoke(this, new DebugOutputEventArgs
                {
                    Category = body.TryGetProperty("category", out var cat) ? cat.GetString() ?? "console" : "console",
                    Output = body.TryGetProperty("output", out var o) ? o.GetString() ?? "" : ""
                });
                break;

            case "terminated":
                _outputService.WriteLine("Debug session terminated", OutputCategory.Debug);
                CleanupProcesses();
                SetState(DebugState.Stopped);
                break;

            case "breakpoint":
                HandleBreakpointEvent(body);
                break;

            case "exited":
                var exitCode = body.TryGetProperty("exitCode", out var ec) ? ec.GetInt32() : 0;
                _outputService.WriteLine($"Program exited with code {exitCode}", OutputCategory.Debug);
                // Note: terminated event should follow and trigger cleanup
                break;
        }
    }

    /// <summary>
    /// Handles DAP "breakpoint" events sent when a breakpoint's status changes
    /// (e.g., verified after module load, or moved to a valid line).
    /// </summary>
    private void HandleBreakpointEvent(JsonElement body)
    {
        var reason = body.TryGetProperty("reason", out var r) ? r.GetString() : "unknown";
        if (!body.TryGetProperty("breakpoint", out var bp)) return;

        var bpInfo = new BreakpointInfo
        {
            Id = bp.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Verified = bp.TryGetProperty("verified", out var v) && v.GetBoolean(),
            Message = bp.TryGetProperty("message", out var m) ? m.GetString() : null,
            Line = bp.TryGetProperty("line", out var l) ? l.GetInt32() : 0,
            Column = bp.TryGetProperty("column", out var c) ? c.GetInt32() : null
        };

        var filePath = "";
        if (bp.TryGetProperty("source", out var src) && src.TryGetProperty("path", out var path))
        {
            filePath = path.GetString() ?? "";
        }

        _outputService.WriteLine($"[DAP] Breakpoint event: reason={reason}, id={bpInfo.Id}, verified={bpInfo.Verified}, line={bpInfo.Line}", OutputCategory.Debug);

        // Fire BreakpointsChanged so the IDE can update verified/unverified display
        BreakpointsChanged?.Invoke(this, new BreakpointsChangedEventArgs
        {
            FilePath = filePath,
            Breakpoints = new List<BreakpointInfo> { bpInfo }
        });
    }

    private static StopReason ParseStopReason(string? reason)
    {
        return reason switch
        {
            "step" => StopReason.Step,
            "breakpoint" => StopReason.Breakpoint,
            "exception" => StopReason.Exception,
            "pause" => StopReason.Pause,
            "entry" => StopReason.Entry,
            "goto" => StopReason.Goto,
            "function breakpoint" => StopReason.FunctionBreakpoint,
            "data breakpoint" => StopReason.DataBreakpoint,
            _ => StopReason.Step
        };
    }

    /// <summary>
    /// All DAP requests flow through the active session — the transport (framing,
    /// correlation, budgeted timeout) lives in <see cref="DapSession"/> now. The
    /// timeout defaults to the profile's Request budget.
    /// </summary>
    private Task<JsonElement> SendRequestAsync(string command, object arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session == null)
            throw new InvalidOperationException("No active DAP session");
        return session.SendRequestAsync(command, arguments, timeout, cancellationToken);
    }

    public void Dispose()
    {
        // Fast path: if never started or already stopped, nothing to do
        if (State == DebugState.NotStarted || State == DebugState.Stopped)
        {
            return;
        }

        // Use synchronous cleanup to avoid thread pool starvation deadlocks
        // when many tests run in parallel. The session's Dispose (inside
        // CleanupProcesses) cancels the read loop; release any callers still
        // awaiting a DAP response first so they don't hang after disposal.
        _session?.CancelPending();

        CleanupProcesses();
        State = DebugState.Stopped;
    }
}
