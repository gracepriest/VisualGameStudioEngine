using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// DAP client implementation for connecting to debug adapters.
/// </summary>
public class DapClientService : IDapClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private readonly List<DapThread> _threads = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _threadLock = new();

    private Process? _adapterProcess;
    private TcpClient? _tcpClient;
    private Stream? _inputStream;
    private Stream? _outputStream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _sequenceNumber;
    private bool _disposed;

    /// <inheritdoc/>
    public DapConnectionState State { get; private set; } = DapConnectionState.Disconnected;

    /// <inheritdoc/>
    public DapCapabilities? Capabilities { get; private set; }

    /// <inheritdoc/>
    public bool IsStopped => State == DapConnectionState.Paused;

    /// <inheritdoc/>
    public IReadOnlyList<DapThread> Threads
    {
        get
        {
            lock (_threadLock)
            {
                return _threads.ToList();
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<DapStateChangedEventArgs>? StateChanged;
    public event EventHandler<DapStoppedEventArgs>? Stopped;
    public event EventHandler<ContinuedEventArgs>? Continued;
    public event EventHandler<ThreadEventArgs>? ThreadStarted;
    public event EventHandler<ThreadEventArgs>? ThreadExited;
    public event EventHandler<DapOutputEventArgs>? OutputReceived;
    public event EventHandler<TerminatedEventArgs>? Terminated;
    public event EventHandler<BreakpointEventArgs>? BreakpointChanged;
    public event EventHandler<ModuleEventArgs>? ModuleChanged;

    /// <inheritdoc/>
    public async Task<bool> StartAdapterAsync(string adapterPath, string? arguments = null, CancellationToken cancellationToken = default)
    {
        if (State != DapConnectionState.Disconnected)
        {
            await DisconnectAsync();
        }

        SetState(DapConnectionState.Connecting);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = adapterPath,
                Arguments = arguments ?? "",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _adapterProcess = new Process { StartInfo = startInfo };
            _adapterProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    OutputReceived?.Invoke(this, new DapOutputEventArgs("stderr", e.Data + Environment.NewLine));
                }
            };

            _adapterProcess.Start();
            _adapterProcess.BeginErrorReadLine();

            _inputStream = _adapterProcess.StandardOutput.BaseStream;
            _outputStream = _adapterProcess.StandardInput.BaseStream;

            StartReading();

            return await InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SetState(DapConnectionState.Error, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (State != DapConnectionState.Disconnected)
        {
            await DisconnectAsync();
        }

        SetState(DapConnectionState.Connecting);

        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, cancellationToken);

            var stream = _tcpClient.GetStream();
            _inputStream = stream;
            _outputStream = stream;

            StartReading();

            return await InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SetState(DapConnectionState.Error, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        if (State == DapConnectionState.Disconnected)
        {
            return;
        }

        SetState(DapConnectionState.Terminating);

        try
        {
            await SendRequestAsync("disconnect", new { terminateDebuggee = true }, CancellationToken.None);
        }
        catch
        {
            // Ignore errors during disconnect
        }

        Cleanup();
        SetState(DapConnectionState.Disconnected);
    }

    /// <inheritdoc/>
    public async Task<bool> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        var launchArgs = new Dictionary<string, object?>
        {
            ["program"] = request.Program,
            ["args"] = request.Args,
            ["cwd"] = request.Cwd,
            ["env"] = request.Env,
            ["noDebug"] = request.NoDebug,
            ["stopOnEntry"] = request.StopOnEntry
        };

        if (request.AdditionalProperties != null)
        {
            foreach (var prop in request.AdditionalProperties)
            {
                launchArgs[prop.Key] = prop.Value;
            }
        }

        var result = await SendRequestAsync("launch", launchArgs, cancellationToken);

        if (result != null)
        {
            await ConfigurationDoneAsync(cancellationToken);
            SetState(DapConnectionState.Running);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> AttachAsync(AttachRequest request, CancellationToken cancellationToken = default)
    {
        var attachArgs = new Dictionary<string, object?>
        {
            ["processId"] = request.ProcessId,
            ["name"] = request.ProcessName,
            ["port"] = request.Port,
            ["host"] = request.Host
        };

        if (request.AdditionalProperties != null)
        {
            foreach (var prop in request.AdditionalProperties)
            {
                attachArgs[prop.Key] = prop.Value;
            }
        }

        var result = await SendRequestAsync("attach", attachArgs, cancellationToken);

        if (result != null)
        {
            await ConfigurationDoneAsync(cancellationToken);
            SetState(DapConnectionState.Running);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DapBreakpoint>> SetBreakpointsAsync(DapSource source, IEnumerable<DapSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("setBreakpoints", new
        {
            source = new
            {
                name = source.Name,
                path = source.Path,
                sourceReference = source.SourceReference
            },
            breakpoints = breakpoints.Select(b => new
            {
                line = b.Line,
                column = b.Column,
                condition = b.Condition,
                hitCondition = b.HitCondition,
                logMessage = b.LogMessage
            })
        }, cancellationToken);

        if (result == null) return Array.Empty<DapBreakpoint>();

        var body = result.Value.GetProperty("breakpoints");
        return JsonSerializer.Deserialize<List<DapBreakpoint>>(body.GetRawText(), JsonOptions) ?? new List<DapBreakpoint>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DapBreakpoint>> SetFunctionBreakpointsAsync(IEnumerable<DapFunctionBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("setFunctionBreakpoints", new
        {
            breakpoints = breakpoints.Select(b => new
            {
                name = b.Name,
                condition = b.Condition,
                hitCondition = b.HitCondition
            })
        }, cancellationToken);

        if (result == null) return Array.Empty<DapBreakpoint>();

        var body = result.Value.GetProperty("breakpoints");
        return JsonSerializer.Deserialize<List<DapBreakpoint>>(body.GetRawText(), JsonOptions) ?? new List<DapBreakpoint>();
    }

    /// <inheritdoc/>
    public async Task SetExceptionBreakpointsAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("setExceptionBreakpoints", new
        {
            filters = filters.ToArray()
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ContinueAsync(int? threadId = null)
    {
        await SendRequestAsync("continue", new { threadId = threadId ?? 0 }, CancellationToken.None);
        SetState(DapConnectionState.Running);
    }

    /// <inheritdoc/>
    public async Task PauseAsync(int? threadId = null)
    {
        await SendRequestAsync("pause", new { threadId = threadId ?? 0 }, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task NextAsync(int threadId)
    {
        await SendRequestAsync("next", new { threadId }, CancellationToken.None);
        SetState(DapConnectionState.Running);
    }

    /// <inheritdoc/>
    public async Task StepInAsync(int threadId)
    {
        await SendRequestAsync("stepIn", new { threadId }, CancellationToken.None);
        SetState(DapConnectionState.Running);
    }

    /// <inheritdoc/>
    public async Task StepOutAsync(int threadId)
    {
        await SendRequestAsync("stepOut", new { threadId }, CancellationToken.None);
        SetState(DapConnectionState.Running);
    }

    /// <inheritdoc/>
    public async Task TerminateAsync()
    {
        SetState(DapConnectionState.Terminating);
        await SendRequestAsync("terminate", null, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task RestartAsync()
    {
        await SendRequestAsync("restart", null, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<StackTraceResult> GetStackTraceAsync(int threadId, int startFrame = 0, int levels = 20, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("stackTrace", new
        {
            threadId,
            startFrame,
            levels
        }, cancellationToken);

        if (result == null) return new StackTraceResult();

        return JsonSerializer.Deserialize<StackTraceResult>(result.Value.GetRawText(), JsonOptions) ?? new StackTraceResult();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DapScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("scopes", new { frameId }, cancellationToken);

        if (result == null) return Array.Empty<DapScope>();

        var scopes = result.Value.GetProperty("scopes");
        return JsonSerializer.Deserialize<List<DapScope>>(scopes.GetRawText(), JsonOptions) ?? new List<DapScope>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DapVariable>> GetVariablesAsync(int variablesReference, int? start = null, int? count = null, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("variables", new
        {
            variablesReference,
            start,
            count
        }, cancellationToken);

        if (result == null) return Array.Empty<DapVariable>();

        var variables = result.Value.GetProperty("variables");
        return JsonSerializer.Deserialize<List<DapVariable>>(variables.GetRawText(), JsonOptions) ?? new List<DapVariable>();
    }

    /// <inheritdoc/>
    public async Task<SetVariableResult> SetVariableAsync(int variablesReference, string name, string value, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("setVariable", new
        {
            variablesReference,
            name,
            value
        }, cancellationToken);

        if (result == null) return new SetVariableResult { Value = value };

        return JsonSerializer.Deserialize<SetVariableResult>(result.Value.GetRawText(), JsonOptions) ?? new SetVariableResult { Value = value };
    }

    /// <inheritdoc/>
    public async Task<DapEvaluateResult> EvaluateAsync(string expression, int? frameId = null, string? context = null, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("evaluate", new
        {
            expression,
            frameId,
            context
        }, cancellationToken);

        if (result == null) return new DapEvaluateResult { Result = "" };

        return JsonSerializer.Deserialize<DapEvaluateResult>(result.Value.GetRawText(), JsonOptions) ?? new DapEvaluateResult { Result = "" };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CompletionTarget>> GetCompletionsAsync(string text, int column, int? frameId = null, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("completions", new
        {
            text,
            column,
            frameId
        }, cancellationToken);

        if (result == null) return Array.Empty<CompletionTarget>();

        var targets = result.Value.GetProperty("targets");
        return JsonSerializer.Deserialize<List<CompletionTarget>>(targets.GetRawText(), JsonOptions) ?? new List<CompletionTarget>();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cleanup();
    }

    #region Private Methods

    private async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        SetState(DapConnectionState.Initializing);

        try
        {
            var result = await SendRequestAsync("initialize", new
            {
                clientID = "visualgamestudio",
                clientName = "VisualGameStudio IDE",
                adapterID = "unknown",
                pathFormat = "path",
                linesStartAt1 = true,
                columnsStartAt1 = true,
                supportsVariableType = true,
                supportsVariablePaging = true,
                supportsRunInTerminalRequest = false,
                supportsMemoryReferences = true,
                supportsProgressReporting = true,
                supportsInvalidatedEvent = true
            }, cancellationToken);

            if (result != null)
            {
                Capabilities = JsonSerializer.Deserialize<DapCapabilities>(result.Value.GetRawText(), JsonOptions);
            }

            SetState(DapConnectionState.Ready);
            return true;
        }
        catch (Exception ex)
        {
            SetState(DapConnectionState.Error, ex.Message);
            return false;
        }
    }

    private async Task ConfigurationDoneAsync(CancellationToken cancellationToken)
    {
        if (Capabilities?.SupportsConfigurationDoneRequest == true)
        {
            await SendRequestAsync("configurationDone", null, cancellationToken);
        }
    }

    private async Task<JsonElement?> SendRequestAsync(string command, object? arguments, CancellationToken cancellationToken)
    {
        var seq = Interlocked.Increment(ref _sequenceNumber);
        var tcs = new TaskCompletionSource<JsonElement?>();
        _pendingRequests[seq] = tcs;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var message = new
            {
                seq,
                type = "request",
                command,
                arguments
            };

            await WriteMessageAsync(message);

            using var registration = cts.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(seq, out _);
        }
    }

    private void StartReading()
    {
        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var headerBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _inputStream != null)
            {
                // Read headers
                headerBuffer.Clear();
                int contentLength = -1;

                while (true)
                {
                    var b = _inputStream.ReadByte();
                    if (b == -1) return;

                    headerBuffer.Append((char)b);
                    var headers = headerBuffer.ToString();

                    if (headers.EndsWith("\r\n\r\n"))
                    {
                        foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                contentLength = int.Parse(line.Substring(15).Trim());
                            }
                        }
                        break;
                    }
                }

                if (contentLength <= 0) continue;

                // Read content
                var content = new byte[contentLength];
                var read = 0;
                while (read < contentLength)
                {
                    var chunk = await _inputStream.ReadAsync(content, read, contentLength - read, cancellationToken);
                    if (chunk == 0) return;
                    read += chunk;
                }

                var json = Encoding.UTF8.GetString(content);
                ProcessMessage(json);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            SetState(DapConnectionState.Error, ex.Message);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "response":
                    ProcessResponse(root);
                    break;
                case "event":
                    ProcessEvent(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, new DapOutputEventArgs("console", $"Failed to process message: {ex.Message}\n"));
        }
    }

    private void ProcessResponse(JsonElement root)
    {
        var requestSeq = root.GetProperty("request_seq").GetInt32();

        if (_pendingRequests.TryRemove(requestSeq, out var tcs))
        {
            var success = root.GetProperty("success").GetBoolean();

            if (success)
            {
                if (root.TryGetProperty("body", out var body))
                {
                    tcs.TrySetResult(body.Clone());
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            }
            else
            {
                var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                tcs.TrySetException(new DapException(message ?? "Unknown error"));
            }
        }
    }

    private void ProcessEvent(JsonElement root)
    {
        var eventName = root.GetProperty("event").GetString();
        root.TryGetProperty("body", out var body);

        switch (eventName)
        {
            case "initialized":
                // Ready to configure
                break;

            case "stopped":
                SetState(DapConnectionState.Paused);
                var reason = body.GetProperty("reason").GetString() ?? "unknown";
                body.TryGetProperty("description", out var desc);
                body.TryGetProperty("threadId", out var tid);
                body.TryGetProperty("allThreadsStopped", out var allStopped);
                Stopped?.Invoke(this, new DapStoppedEventArgs(
                    reason,
                    desc.ValueKind == JsonValueKind.String ? desc.GetString() : null,
                    tid.ValueKind == JsonValueKind.Number ? tid.GetInt32() : null,
                    allThreadsStopped: allStopped.ValueKind == JsonValueKind.True
                ));
                break;

            case "continued":
                SetState(DapConnectionState.Running);
                var contThreadId = body.GetProperty("threadId").GetInt32();
                Continued?.Invoke(this, new ContinuedEventArgs(contThreadId));
                break;

            case "thread":
                var threadReason = body.GetProperty("reason").GetString()!;
                var threadId = body.GetProperty("threadId").GetInt32();
                if (threadReason == "started")
                {
                    lock (_threadLock)
                    {
                        _threads.Add(new DapThread { Id = threadId, Name = $"Thread {threadId}" });
                    }
                    ThreadStarted?.Invoke(this, new ThreadEventArgs(threadReason, threadId));
                }
                else if (threadReason == "exited")
                {
                    lock (_threadLock)
                    {
                        _threads.RemoveAll(t => t.Id == threadId);
                    }
                    ThreadExited?.Invoke(this, new ThreadEventArgs(threadReason, threadId));
                }
                break;

            case "output":
                var category = body.TryGetProperty("category", out var cat) ? cat.GetString() ?? "console" : "console";
                var output = body.GetProperty("output").GetString() ?? "";
                OutputReceived?.Invoke(this, new DapOutputEventArgs(category, output));
                break;

            case "terminated":
                var restart = body.ValueKind == JsonValueKind.Object &&
                              body.TryGetProperty("restart", out var r) && r.ValueKind == JsonValueKind.True;
                SetState(DapConnectionState.Disconnected);
                Terminated?.Invoke(this, new TerminatedEventArgs(restart));
                break;

            case "exited":
                SetState(DapConnectionState.Disconnected);
                Terminated?.Invoke(this, new TerminatedEventArgs());
                break;

            case "breakpoint":
                var bpReason = body.GetProperty("reason").GetString()!;
                var bp = JsonSerializer.Deserialize<DapBreakpoint>(body.GetProperty("breakpoint").GetRawText(), JsonOptions)!;
                BreakpointChanged?.Invoke(this, new BreakpointEventArgs(bpReason, bp));
                break;

            case "module":
                var modReason = body.GetProperty("reason").GetString()!;
                var module = JsonSerializer.Deserialize<DapModule>(body.GetProperty("module").GetRawText(), JsonOptions)!;
                ModuleChanged?.Invoke(this, new ModuleEventArgs(modReason, module));
                break;
        }
    }

    private async Task WriteMessageAsync(object message)
    {
        if (_outputStream == null) return;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var content = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {content.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await _writeLock.WaitAsync();
        try
        {
            await _outputStream.WriteAsync(headerBytes);
            await _outputStream.WriteAsync(content);
            await _outputStream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void SetState(DapConnectionState newState, string? error = null)
    {
        var oldState = State;
        State = newState;
        StateChanged?.Invoke(this, new DapStateChangedEventArgs(oldState, newState, error));
    }

    private void Cleanup()
    {
        _readCts?.Cancel();

        try { _inputStream?.Close(); } catch { }
        try { _outputStream?.Close(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        try
        {
            if (_adapterProcess != null && !_adapterProcess.HasExited)
            {
                _adapterProcess.Kill();
            }
            _adapterProcess?.Dispose();
        }
        catch { }

        _inputStream = null;
        _outputStream = null;
        _tcpClient = null;
        _adapterProcess = null;

        lock (_threadLock)
        {
            _threads.Clear();
        }

        foreach (var pending in _pendingRequests.Values)
        {
            pending.TrySetCanceled();
        }
        _pendingRequests.Clear();
    }

    #endregion
}

/// <summary>
/// Exception for DAP errors.
/// </summary>
public class DapException : Exception
{
    public DapException(string message) : base(message) { }
}
