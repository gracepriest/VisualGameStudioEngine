using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.DAP;
using VisualGameStudio.Core.Extensions;

namespace VisualGameStudio.ProjectSystem.DAP;

/// <summary>
/// Generic DAP client implementation that can connect to any debug adapter
/// </summary>
public class DapClient : IDapClient
{
    private readonly DebugAdapterConfig _config;
    private Process? _adapterProcess;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _readerTask;
    private CancellationTokenSource? _cts;
    private int _sequenceNumber;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public DapCapabilities? Capabilities { get; private set; }

    public event EventHandler<DapEventArgs>? EventReceived;
    public event EventHandler<StoppedEventArgs>? Stopped;
    public event EventHandler<ContinuedEventArgs>? Continued;
    public event EventHandler<TerminatedEventArgs>? Terminated;
    public event EventHandler<OutputEventArgs>? Output;
    public event EventHandler<BreakpointEventArgs>? BreakpointHit;
    public event EventHandler<ThreadEventArgs>? ThreadEvent;

    public DapClient(DebugAdapterConfig config)
    {
        _config = config;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    public async Task<bool> InitializeAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Start the debug adapter process
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.StartInfo.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in _config.StartInfo.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (_config.StartInfo.Environment != null)
            {
                foreach (var (key, value) in _config.StartInfo.Environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            _adapterProcess = Process.Start(startInfo);
            if (_adapterProcess == null)
            {
                return false;
            }

            _writer = _adapterProcess.StandardInput;
            _reader = _adapterProcess.StandardOutput;

            // Start reading responses
            _cts = new CancellationTokenSource();
            _readerTask = ReadMessagesAsync(_cts.Token);

            // Send initialize request
            var initArgs = new
            {
                clientID = "visualgamestudio",
                clientName = "Visual Game Studio",
                adapterID = adapterId,
                locale = "en-US",
                linesStartAt1 = true,
                columnsStartAt1 = true,
                pathFormat = "path",
                supportsVariableType = true,
                supportsVariablePaging = true,
                supportsRunInTerminalRequest = false,
                supportsMemoryReferences = true,
                supportsProgressReporting = true,
                supportsInvalidatedEvent = true
            };

            var result = await SendRawRequestAsync("initialize", initArgs, cancellationToken);
            if (result != null)
            {
                Capabilities = ParseCapabilities(result.Value);
                IsConnected = true;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize DAP client: {ex.Message}");
            return false;
        }
    }

    private DapCapabilities ParseCapabilities(JsonElement element)
    {
        var caps = new DapCapabilities();

        if (element.TryGetProperty("supportsConfigurationDoneRequest", out var configDone))
            caps.SupportsConfigurationDoneRequest = configDone.GetBoolean();

        if (element.TryGetProperty("supportsFunctionBreakpoints", out var funcBp))
            caps.SupportsFunctionBreakpoints = funcBp.GetBoolean();

        if (element.TryGetProperty("supportsConditionalBreakpoints", out var condBp))
            caps.SupportsConditionalBreakpoints = condBp.GetBoolean();

        if (element.TryGetProperty("supportsHitConditionalBreakpoints", out var hitCondBp))
            caps.SupportsHitConditionalBreakpoints = hitCondBp.GetBoolean();

        if (element.TryGetProperty("supportsEvaluateForHovers", out var evalHover))
            caps.SupportsEvaluateForHovers = evalHover.GetBoolean();

        if (element.TryGetProperty("supportsStepBack", out var stepBack))
            caps.SupportsStepBack = stepBack.GetBoolean();

        if (element.TryGetProperty("supportsSetVariable", out var setVar))
            caps.SupportsSetVariable = setVar.GetBoolean();

        if (element.TryGetProperty("supportsRestartFrame", out var restartFrame))
            caps.SupportsRestartFrame = restartFrame.GetBoolean();

        if (element.TryGetProperty("supportsTerminateRequest", out var terminate))
            caps.SupportsTerminateRequest = terminate.GetBoolean();

        if (element.TryGetProperty("supportsLogPoints", out var logPoints))
            caps.SupportsLogPoints = logPoints.GetBoolean();

        return caps;
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;

        try
        {
            await SendRawRequestAsync("disconnect", new { restart = false });
        }
        catch
        {
            // Ignore errors during disconnect
        }

        IsConnected = false;
    }

    public async Task<TResponse?> SendRequestAsync<TResponse>(string command, object? arguments, CancellationToken cancellationToken = default)
    {
        var jsonResult = await SendRawRequestAsync(command, arguments, cancellationToken);
        if (jsonResult == null)
        {
            return default;
        }
        return JsonSerializer.Deserialize<TResponse>(jsonResult.Value.GetRawText(), _jsonOptions);
    }

    private async Task<JsonElement?> SendRawRequestAsync(string command, object? arguments, CancellationToken cancellationToken = default)
    {
        var seq = Interlocked.Increment(ref _sequenceNumber);
        var tcs = new TaskCompletionSource<JsonElement?>();
        _pendingRequests[seq] = tcs;

        try
        {
            var request = new
            {
                seq,
                type = "request",
                command,
                arguments
            };

            await SendMessageAsync(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _pendingRequests.TryRemove(seq, out _);
        }
    }

    private async Task SendMessageAsync(object message)
    {
        if (_writer == null) return;

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var content = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

        await _writer.WriteAsync(content);
        await _writer.FlushAsync();
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _reader != null)
        {
            try
            {
                // Read headers
                int contentLength = 0;

                while (true)
                {
                    var line = await _reader.ReadLineAsync(cancellationToken);
                    if (line == null) return;

                    if (string.IsNullOrEmpty(line))
                    {
                        break;
                    }

                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        contentLength = int.Parse(line.Substring(15).Trim());
                    }
                }

                if (contentLength == 0) continue;

                // Read content
                var buffer = new char[contentLength];
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var read = await _reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                    if (read == 0) return;
                    totalRead += read;
                }

                var json = new string(buffer);
                ProcessMessage(json);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading DAP message: {ex.Message}");
            }
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return;

            var type = typeProp.GetString();

            switch (type)
            {
                case "response":
                    HandleResponse(root);
                    break;
                case "event":
                    HandleEvent(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing DAP message: {ex.Message}");
        }
    }

    private void HandleResponse(JsonElement root)
    {
        if (!root.TryGetProperty("request_seq", out var seqProp))
            return;

        var seq = seqProp.GetInt32();
        if (_pendingRequests.TryRemove(seq, out var tcs))
        {
            if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
            {
                if (root.TryGetProperty("body", out var body))
                {
                    tcs.SetResult(body.Clone());
                }
                else
                {
                    tcs.SetResult(null);
                }
            }
            else
            {
                var message = "Unknown error";
                if (root.TryGetProperty("message", out var msgProp))
                {
                    message = msgProp.GetString() ?? message;
                }
                Debug.WriteLine($"DAP error: {message}");
                tcs.SetResult(null);
            }
        }
    }

    private void HandleEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventProp))
            return;

        var eventName = eventProp.GetString();
        JsonElement? body = null;

        if (root.TryGetProperty("body", out var bodyProp))
        {
            body = bodyProp.Clone();
        }

        // Raise generic event
        EventReceived?.Invoke(this, new DapEventArgs
        {
            Event = eventName ?? "",
            Body = body
        });

        // Handle specific events
        switch (eventName)
        {
            case "stopped":
                HandleStoppedEvent(body);
                break;
            case "continued":
                HandleContinuedEvent(body);
                break;
            case "terminated":
                HandleTerminatedEvent(body);
                break;
            case "exited":
                HandleTerminatedEvent(body);
                break;
            case "output":
                HandleOutputEvent(body);
                break;
            case "breakpoint":
                HandleBreakpointEvent(body);
                break;
            case "thread":
                HandleThreadEvent(body);
                break;
            case "initialized":
                // Send configurationDone if supported
                if (Capabilities?.SupportsConfigurationDoneRequest == true)
                {
                    _ = SendRawRequestAsync("configurationDone", null);
                }
                break;
        }
    }

    private void HandleStoppedEvent(JsonElement? body)
    {
        var args = new StoppedEventArgs();

        if (body.HasValue)
        {
            var b = body.Value;
            if (b.TryGetProperty("reason", out var reason))
                args.Reason = reason.GetString() ?? "";
            if (b.TryGetProperty("threadId", out var threadId))
                args.ThreadId = threadId.GetInt32();
            if (b.TryGetProperty("allThreadsStopped", out var all))
                args.AllThreadsStopped = all.GetBoolean();
            if (b.TryGetProperty("description", out var desc))
                args.Description = desc.GetString();
            if (b.TryGetProperty("text", out var text))
                args.Text = text.GetString();
        }

        Stopped?.Invoke(this, args);
    }

    private void HandleContinuedEvent(JsonElement? body)
    {
        var args = new ContinuedEventArgs();

        if (body.HasValue)
        {
            var b = body.Value;
            if (b.TryGetProperty("threadId", out var threadId))
                args.ThreadId = threadId.GetInt32();
            if (b.TryGetProperty("allThreadsContinued", out var all))
                args.AllThreadsContinued = all.GetBoolean();
        }

        Continued?.Invoke(this, args);
    }

    private void HandleTerminatedEvent(JsonElement? body)
    {
        var args = new TerminatedEventArgs();

        if (body.HasValue)
        {
            var b = body.Value;
            if (b.TryGetProperty("restart", out var restart))
                args.Restart = restart.GetBoolean();
        }

        Terminated?.Invoke(this, args);
        IsConnected = false;
    }

    private void HandleOutputEvent(JsonElement? body)
    {
        var args = new OutputEventArgs();

        if (body.HasValue)
        {
            var b = body.Value;
            if (b.TryGetProperty("category", out var category))
                args.Category = category.GetString() ?? "";
            if (b.TryGetProperty("output", out var output))
                args.Output = output.GetString() ?? "";
            if (b.TryGetProperty("line", out var line))
                args.Line = line.GetInt32();
            if (b.TryGetProperty("column", out var column))
                args.Column = column.GetInt32();
            if (b.TryGetProperty("source", out var source))
                args.Source = ParseSource(source);
        }

        Output?.Invoke(this, args);
    }

    private void HandleBreakpointEvent(JsonElement? body)
    {
        var args = new BreakpointEventArgs();

        if (body.HasValue)
        {
            var b = body.Value;
            if (b.TryGetProperty("reason", out var reason))
                args.Reason = reason.GetString() ?? "";
            if (b.TryGetProperty("breakpoint", out var bp))
                args.Breakpoint = ParseBreakpoint(bp);
        }

        BreakpointHit?.Invoke(this, args);
    }

    private void HandleThreadEvent(JsonElement? body)
    {
        var args = new ThreadEventArgs();

        if (body.HasValue)
        {
            var b = body.Value;
            if (b.TryGetProperty("reason", out var reason))
                args.Reason = reason.GetString() ?? "";
            if (b.TryGetProperty("threadId", out var threadId))
                args.ThreadId = threadId.GetInt32();
        }

        ThreadEvent?.Invoke(this, args);
    }

    private DapSource ParseSource(JsonElement element)
    {
        var source = new DapSource();

        if (element.TryGetProperty("name", out var name))
            source.Name = name.GetString();
        if (element.TryGetProperty("path", out var path))
            source.Path = path.GetString();
        if (element.TryGetProperty("sourceReference", out var srcRef))
            source.SourceReference = srcRef.GetInt32();
        if (element.TryGetProperty("presentationHint", out var hint))
            source.PresentationHint = hint.GetString();
        if (element.TryGetProperty("origin", out var origin))
            source.Origin = origin.GetString();

        return source;
    }

    private Breakpoint ParseBreakpoint(JsonElement element)
    {
        var bp = new Breakpoint();

        if (element.TryGetProperty("id", out var id))
            bp.Id = id.GetInt32();
        if (element.TryGetProperty("verified", out var verified))
            bp.Verified = verified.GetBoolean();
        if (element.TryGetProperty("message", out var message))
            bp.Message = message.GetString();
        if (element.TryGetProperty("line", out var line))
            bp.Line = line.GetInt32();
        if (element.TryGetProperty("column", out var column))
            bp.Column = column.GetInt32();
        if (element.TryGetProperty("endLine", out var endLine))
            bp.EndLine = endLine.GetInt32();
        if (element.TryGetProperty("endColumn", out var endColumn))
            bp.EndColumn = endColumn.GetInt32();
        if (element.TryGetProperty("source", out var source))
            bp.Source = ParseSource(source);

        return bp;
    }

    #region Debug Operations

    public async Task<bool> LaunchAsync(LaunchRequestArguments args, CancellationToken cancellationToken = default)
    {
        var launchArgs = new Dictionary<string, object?>
        {
            ["noDebug"] = args.NoDebug,
            ["program"] = args.Program,
            ["args"] = args.Args,
            ["cwd"] = args.Cwd,
            ["env"] = args.Env,
            ["stopOnEntry"] = args.StopOnEntry
        };

        if (args.AdditionalProperties != null)
        {
            foreach (var kvp in args.AdditionalProperties)
            {
                launchArgs[kvp.Key] = kvp.Value;
            }
        }

        var result = await SendRawRequestAsync("launch", launchArgs, cancellationToken);
        return result != null;
    }

    public async Task<bool> AttachAsync(AttachRequestArguments args, CancellationToken cancellationToken = default)
    {
        var attachArgs = new Dictionary<string, object?>
        {
            ["processId"] = args.ProcessId,
            ["name"] = args.Name
        };

        if (args.AdditionalProperties != null)
        {
            foreach (var kvp in args.AdditionalProperties)
            {
                attachArgs[kvp.Key] = kvp.Value;
            }
        }

        var result = await SendRawRequestAsync("attach", attachArgs, cancellationToken);
        return result != null;
    }

    public async Task<SetBreakpointsResponse?> SetBreakpointsAsync(string sourcePath, IReadOnlyList<SourceBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("setBreakpoints", new
        {
            source = new { path = sourcePath },
            breakpoints = breakpoints.Select(bp => new
            {
                line = bp.Line,
                column = bp.Column,
                condition = bp.Condition,
                hitCondition = bp.HitCondition,
                logMessage = bp.LogMessage
            })
        }, cancellationToken);

        if (result == null) return null;

        var response = new SetBreakpointsResponse();
        if (result.Value.TryGetProperty("breakpoints", out var bps))
        {
            foreach (var bp in bps.EnumerateArray())
            {
                response.Breakpoints.Add(ParseBreakpoint(bp));
            }
        }

        return response;
    }

    public async Task<bool> ContinueAsync(int threadId, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("continue", new { threadId }, cancellationToken);
        return result != null;
    }

    public async Task<bool> PauseAsync(int threadId, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("pause", new { threadId }, cancellationToken);
        return result != null;
    }

    public async Task<bool> NextAsync(int threadId, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("next", new { threadId }, cancellationToken);
        return result != null;
    }

    public async Task<bool> StepInAsync(int threadId, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("stepIn", new { threadId }, cancellationToken);
        return result != null;
    }

    public async Task<bool> StepOutAsync(int threadId, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("stepOut", new { threadId }, cancellationToken);
        return result != null;
    }

    public async Task<IReadOnlyList<DapThread>?> GetThreadsAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("threads", null, cancellationToken);
        if (result == null) return null;

        var threads = new List<DapThread>();
        if (result.Value.TryGetProperty("threads", out var threadsArray))
        {
            foreach (var t in threadsArray.EnumerateArray())
            {
                var thread = new DapThread();
                if (t.TryGetProperty("id", out var id))
                    thread.Id = id.GetInt32();
                if (t.TryGetProperty("name", out var name))
                    thread.Name = name.GetString() ?? "";
                threads.Add(thread);
            }
        }

        return threads;
    }

    public async Task<IReadOnlyList<DapStackFrame>?> GetStackTraceAsync(int threadId, int? startFrame = null, int? levels = null, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object>
        {
            ["threadId"] = threadId
        };
        if (startFrame.HasValue)
            args["startFrame"] = startFrame.Value;
        if (levels.HasValue)
            args["levels"] = levels.Value;

        var result = await SendRawRequestAsync("stackTrace", args, cancellationToken);
        if (result == null) return null;

        var frames = new List<DapStackFrame>();
        if (result.Value.TryGetProperty("stackFrames", out var framesArray))
        {
            foreach (var f in framesArray.EnumerateArray())
            {
                var frame = new DapStackFrame();
                if (f.TryGetProperty("id", out var id))
                    frame.Id = id.GetInt32();
                if (f.TryGetProperty("name", out var name))
                    frame.Name = name.GetString() ?? "";
                if (f.TryGetProperty("line", out var line))
                    frame.Line = line.GetInt32();
                if (f.TryGetProperty("column", out var column))
                    frame.Column = column.GetInt32();
                if (f.TryGetProperty("endLine", out var endLine))
                    frame.EndLine = endLine.GetInt32();
                if (f.TryGetProperty("endColumn", out var endColumn))
                    frame.EndColumn = endColumn.GetInt32();
                if (f.TryGetProperty("source", out var source))
                    frame.Source = ParseSource(source);
                if (f.TryGetProperty("presentationHint", out var hint))
                    frame.PresentationHint = hint.GetString();

                frames.Add(frame);
            }
        }

        return frames;
    }

    public async Task<IReadOnlyList<Scope>?> GetScopesAsync(int frameId, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("scopes", new { frameId }, cancellationToken);
        if (result == null) return null;

        var scopes = new List<Scope>();
        if (result.Value.TryGetProperty("scopes", out var scopesArray))
        {
            foreach (var s in scopesArray.EnumerateArray())
            {
                var scope = new Scope();
                if (s.TryGetProperty("name", out var name))
                    scope.Name = name.GetString() ?? "";
                if (s.TryGetProperty("presentationHint", out var hint))
                    scope.PresentationHint = hint.GetString();
                if (s.TryGetProperty("variablesReference", out var varsRef))
                    scope.VariablesReference = varsRef.GetInt32();
                if (s.TryGetProperty("namedVariables", out var namedVars))
                    scope.NamedVariables = namedVars.GetInt32();
                if (s.TryGetProperty("indexedVariables", out var indexedVars))
                    scope.IndexedVariables = indexedVars.GetInt32();
                if (s.TryGetProperty("expensive", out var expensive))
                    scope.Expensive = expensive.GetBoolean();
                if (s.TryGetProperty("line", out var line))
                    scope.Line = line.GetInt32();
                if (s.TryGetProperty("column", out var column))
                    scope.Column = column.GetInt32();
                if (s.TryGetProperty("source", out var source))
                    scope.Source = ParseSource(source);

                scopes.Add(scope);
            }
        }

        return scopes;
    }

    public async Task<IReadOnlyList<Variable>?> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
    {
        var result = await SendRawRequestAsync("variables", new { variablesReference }, cancellationToken);
        if (result == null) return null;

        var variables = new List<Variable>();
        if (result.Value.TryGetProperty("variables", out var varsArray))
        {
            foreach (var v in varsArray.EnumerateArray())
            {
                var variable = new Variable();
                if (v.TryGetProperty("name", out var name))
                    variable.Name = name.GetString() ?? "";
                if (v.TryGetProperty("value", out var value))
                    variable.Value = value.GetString() ?? "";
                if (v.TryGetProperty("type", out var type))
                    variable.Type = type.GetString();
                if (v.TryGetProperty("presentationHint", out var hint))
                    variable.PresentationHint = hint.GetString();
                if (v.TryGetProperty("evaluateName", out var evalName))
                    variable.EvaluateName = evalName.GetString();
                if (v.TryGetProperty("variablesReference", out var varsRef))
                    variable.VariablesReference = varsRef.GetInt32();
                if (v.TryGetProperty("namedVariables", out var namedVars))
                    variable.NamedVariables = namedVars.GetInt32();
                if (v.TryGetProperty("indexedVariables", out var indexedVars))
                    variable.IndexedVariables = indexedVars.GetInt32();
                if (v.TryGetProperty("memoryReference", out var memRef))
                    variable.MemoryReference = memRef.GetString();

                variables.Add(variable);
            }
        }

        return variables;
    }

    public async Task<EvaluateResponse?> EvaluateAsync(string expression, int? frameId = null, string? context = null, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["expression"] = expression
        };
        if (frameId.HasValue)
            args["frameId"] = frameId.Value;
        if (context != null)
            args["context"] = context;

        var result = await SendRawRequestAsync("evaluate", args, cancellationToken);
        if (result == null) return null;

        var response = new EvaluateResponse();
        if (result.Value.TryGetProperty("result", out var resultProp))
            response.Result = resultProp.GetString() ?? "";
        if (result.Value.TryGetProperty("type", out var type))
            response.Type = type.GetString();
        if (result.Value.TryGetProperty("presentationHint", out var hint))
            response.PresentationHint = hint.GetString();
        if (result.Value.TryGetProperty("variablesReference", out var varsRef))
            response.VariablesReference = varsRef.GetInt32();
        if (result.Value.TryGetProperty("namedVariables", out var namedVars))
            response.NamedVariables = namedVars.GetInt32();
        if (result.Value.TryGetProperty("indexedVariables", out var indexedVars))
            response.IndexedVariables = indexedVars.GetInt32();
        if (result.Value.TryGetProperty("memoryReference", out var memRef))
            response.MemoryReference = memRef.GetString();

        return response;
    }

    public async Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        if (Capabilities?.SupportsTerminateRequest == true)
        {
            await SendRawRequestAsync("terminate", null, cancellationToken);
        }
        await DisconnectAsync();
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();

        try
        {
            _writer?.Dispose();
            _reader?.Dispose();

            if (_adapterProcess != null && !_adapterProcess.HasExited)
            {
                _adapterProcess.Kill();
            }
            _adapterProcess?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
