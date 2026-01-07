using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// DAP client that communicates with BasicLang debug adapter
/// </summary>
public class DebugService : IDebugService
{
    private Process? _debugProcess;
    private Process? _targetProcess;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private StreamWriter? _stdinWriter;
    private Task? _readTask;
    private CancellationTokenSource? _cts;
    private int _requestSeq;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly object _lock = new();
    private readonly string _compilerPath;
    private readonly IOutputService _outputService;

    // Run to cursor state
    private string? _runToCursorFile;
    private int _runToCursorLine;
    private List<SourceBreakpoint>? _originalBreakpoints;

    public DebugState State { get; private set; } = DebugState.NotStarted;
    public bool IsDebugging => State == DebugState.Running || State == DebugState.Paused;

    public event EventHandler<DebugStateChangedEventArgs>? StateChanged;
    public event EventHandler<StoppedEventArgs>? Stopped;
    public event EventHandler<DebugOutputEventArgs>? OutputReceived;
    public event EventHandler<BreakpointsChangedEventArgs>? BreakpointsChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DebugService(IOutputService outputService)
    {
        _outputService = outputService;
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

        if (IsDebugging) return false;

        try
        {
            SetState(DebugState.Initializing);
            _cts = new CancellationTokenSource();

            // Start the debug adapter
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{_compilerPath}\" --debug-adapter",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = config.WorkingDirectory,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            _debugProcess = new Process { StartInfo = startInfo };
            _debugProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _outputService.WriteLine($"[DAP Error] {e.Data}", OutputCategory.Debug);
                }
            };

            _debugProcess.Start();
            _debugProcess.BeginErrorReadLine();

            _writer = new StreamWriter(_debugProcess.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = false };
            _reader = new StreamReader(_debugProcess.StandardOutput.BaseStream, Encoding.UTF8);

            _readTask = Task.Run(() => ReadMessagesAsync(_cts.Token), _cts.Token);

            // Initialize
            await SendRequestAsync("initialize", new
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
            }, cancellationToken);

            // Launch
            await SendRequestAsync("launch", new
            {
                program = config.Program,
                cwd = config.WorkingDirectory,
                args = config.Arguments,
                stopOnEntry = config.StopOnEntry,
                noDebug = false
            }, cancellationToken);

            // Set breakpoints AFTER launch but BEFORE configurationDone
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

            // Configuration done
            await SendRequestAsync("configurationDone", new { }, cancellationToken);

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

    public async Task<bool> StartWithoutDebuggingAsync(DebugConfiguration config, CancellationToken cancellationToken = default)
    {
        if (IsDebugging) return false;

        try
        {
            SetState(DebugState.Running);
            _cts = new CancellationTokenSource();

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

    public async Task StopDebuggingAsync()
    {
        // Try to send disconnect request with timeout before cancelling
        if (_writer != null && State != DebugState.Stopped)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendRequestAsync("disconnect", new { terminateDebuggee = true }, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout - proceed with cleanup
                _outputService.WriteLine("Disconnect request timed out, forcing stop", OutputCategory.Debug);
            }
            catch
            {
                // Ignore other errors during disconnect
            }
        }

        // Cancel the read loop
        _cts?.Cancel();

        CleanupProcesses();
        SetState(DebugState.Stopped);
        _outputService.WriteLine("Debugging stopped", OutputCategory.Debug);
    }

    private void CleanupProcesses()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _stdinWriter?.Dispose();
        _stdinWriter = null;

        if (_debugProcess != null && !_debugProcess.HasExited)
        {
            try { _debugProcess.Kill(); } catch { }
        }
        _debugProcess?.Dispose();

        if (_targetProcess != null && !_targetProcess.HasExited)
        {
            try { _targetProcess.Kill(); } catch { }
        }
        _targetProcess?.Dispose();

        _debugProcess = null;
        _targetProcess = null;
        _writer = null;
        _reader = null;
    }

    public async Task ContinueAsync()
    {
        if (State != DebugState.Paused) return;
        await SendRequestAsync("continue", new { threadId = 1 });
        SetState(DebugState.Running);
    }

    public async Task StepOverAsync()
    {
        if (State != DebugState.Paused) return;
        await SendRequestAsync("next", new { threadId = 1 });
        SetState(DebugState.Running);
    }

    public async Task StepIntoAsync()
    {
        if (State != DebugState.Paused) return;
        await SendRequestAsync("stepIn", new { threadId = 1 });
        SetState(DebugState.Running);
    }

    public async Task StepOutAsync()
    {
        if (State != DebugState.Paused) return;
        await SendRequestAsync("stepOut", new { threadId = 1 });
        SetState(DebugState.Running);
    }

    public async Task PauseAsync()
    {
        if (State != DebugState.Running) return;
        await SendRequestAsync("pause", new { threadId = 1 });
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
            await SendRequestAsync("continue", new { threadId = 1 });
            SetState(DebugState.Running);
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
        if (!IsDebugging) return;

        try
        {
            var filtersList = filters.ToList();
            var args = new Dictionary<string, object>
            {
                ["filters"] = filtersList
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

            var filterNames = string.Join(", ", filtersList);
            _outputService.WriteLine($"Exception breakpoints set: {(filtersList.Count > 0 ? filterNames : "none")}", OutputCategory.Debug);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"Failed to set exception breakpoints: {ex.Message}", OutputCategory.Debug);
        }
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
                            threadId = 1,
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

    public async Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId = 1)
    {
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

    public async Task<EvaluateResult> EvaluateAsync(string expression, int? frameId = null)
    {
        try
        {
            var args = new Dictionary<string, object> { ["expression"] = expression, ["context"] = "watch" };
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

    private void SetState(DebugState newState)
    {
        var oldState = State;
        if (oldState == newState) return;
        State = newState;
        StateChanged?.Invoke(this, new DebugStateChangedEventArgs { OldState = oldState, NewState = newState });
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _reader != null)
        {
            try
            {
                var message = await ReadMessageAsync(cancellationToken);
                if (message != null)
                {
                    ProcessMessage(message.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _outputService.WriteLine($"[DAP] Read error: {ex.Message}", OutputCategory.Debug);
            }
        }
    }

    private async Task<JsonElement?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        if (_reader == null) return null;

        int contentLength = 0;
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line == null) return null;
            if (line.Length == 0) break;

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line.Substring(15).Trim());
            }
        }

        if (contentLength == 0) return null;

        var buffer = new char[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var chunk = await _reader.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken);
            if (chunk == 0) return null;
            read += chunk;
        }

        var json = new string(buffer);
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
    }

    private void ProcessMessage(JsonElement message)
    {
        var type = message.TryGetProperty("type", out var t) ? t.GetString() : null;

        if (type == "response")
        {
            var reqSeq = message.TryGetProperty("request_seq", out var rs) ? rs.GetInt32() : 0;
            lock (_lock)
            {
                if (_pendingRequests.TryGetValue(reqSeq, out var tcs))
                {
                    _pendingRequests.Remove(reqSeq);
                    if (message.TryGetProperty("success", out var s) && s.GetBoolean())
                    {
                        tcs.SetResult(message.TryGetProperty("body", out var body) ? body : default);
                    }
                    else
                    {
                        var errorMsg = message.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                        tcs.SetException(new Exception(errorMsg));
                    }
                }
            }
        }
        else if (type == "event")
        {
            var eventType = message.TryGetProperty("event", out var e) ? e.GetString() : null;
            var body = message.TryGetProperty("body", out var b) ? b : default;

            switch (eventType)
            {
                case "stopped":
                    SetState(DebugState.Paused);
                    var stoppedArgs = new StoppedEventArgs
                    {
                        Reason = ParseStopReason(body.TryGetProperty("reason", out var r) ? r.GetString() : null),
                        ThreadId = body.TryGetProperty("threadId", out var tid) ? tid.GetInt32() : 1,
                        Description = body.TryGetProperty("description", out var d) ? d.GetString() : null,
                        Text = body.TryGetProperty("text", out var txt) ? txt.GetString() : null
                    };

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

                case "exited":
                    var exitCode = body.TryGetProperty("exitCode", out var ec) ? ec.GetInt32() : 0;
                    _outputService.WriteLine($"Program exited with code {exitCode}", OutputCategory.Debug);
                    // Note: terminated event should follow and trigger cleanup
                    break;
            }
        }
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

    private async Task<JsonElement> SendRequestAsync(string command, object arguments, CancellationToken cancellationToken = default)
    {
        var seq = Interlocked.Increment(ref _requestSeq);
        var tcs = new TaskCompletionSource<JsonElement>();

        lock (_lock)
        {
            _pendingRequests[seq] = tcs;
        }

        var request = new { seq, type = "request", command, arguments };
        await SendMessageAsync(request);

        using var ctr = cancellationToken.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    private async Task SendMessageAsync(object message)
    {
        if (_writer == null) return;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var content = Encoding.UTF8.GetBytes(json);

        var header = $"Content-Length: {content.Length}\r\n\r\n";
        await _writer.WriteAsync(header);
        await _writer.WriteAsync(json);
        await _writer.FlushAsync();
    }

    public void Dispose()
    {
        // Fast path: if never started or already stopped, nothing to do
        if (State == DebugState.NotStarted || State == DebugState.Stopped)
        {
            return;
        }

        try
        {
            // Use Task.Run to avoid deadlocks when called from sync context
            Task.Run(async () => await StopDebuggingAsync()).Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Ignore exceptions during dispose
        }
        catch (TimeoutException)
        {
            // Timeout is acceptable during dispose
        }
    }
}
