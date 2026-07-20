using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// DAP transport + protocol core (Phase 4): adapter process, BOM-less UTF-8 stdio,
/// Latin1 re-decode framing, request/response correlation, raw event dispatch.
/// Owns NO IDE debug state — DebugService orchestrates on top of this.
/// The stream ctor is the test seam (no InternalsVisibleTo in this assembly —
/// public on purpose, like ParseCompletions).
/// </summary>
public sealed class DapSession : IDisposable
{
    private readonly ProcessStartInfo? _startInfo;
    private readonly Stream? _readStream;
    private readonly Stream? _writeStream;
    private readonly IOutputService _outputService;

    private Process? _debugProcess;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _readTask;
    private CancellationTokenSource? _cts;
    private int _requestSeq;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _cleanupLock = new();
    private bool _started;
    private bool _disposed;
    private int _closedRaised;
    // Armed by InitializeAndLaunchAsync BEFORE anything is sent, completed by the
    // read loop on the `initialized` event (which may fire before launch goes out).
    private TaskCompletionSource<bool>? _initializedTcs;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Production: spawn the adapter process described by <paramref name="startInfo"/>.
    /// startInfo MUST carry the BOM-less encodings — <see cref="BuildStartInfo"/> sets them.
    /// </summary>
    public DapSession(ProcessStartInfo startInfo, IOutputService outputService, DapTimeoutProfile? timeouts = null)
    {
        _startInfo = startInfo;
        _outputService = outputService;
        Timeouts = timeouts ?? DapTimeoutProfile.Managed;
    }

    /// <summary>
    /// Test seam: drive the protocol over in-proc streams; no process is spawned.
    /// </summary>
    public DapSession(Stream readFromAdapter, Stream writeToAdapter, IOutputService outputService, DapTimeoutProfile? timeouts = null)
    {
        _readStream = readFromAdapter;
        _writeStream = writeToAdapter;
        _outputService = outputService;
        Timeouts = timeouts ?? DapTimeoutProfile.Managed;
    }

    /// <summary>Per-adapter timeout budgets (spec §8); from Task 6 the descriptor supplies them.</summary>
    public DapTimeoutProfile Timeouts { get; }

    /// <summary>
    /// What the adapter disclosed in its initialize response, retained for the life of
    /// the session (spec §3.3.3). Null until <see cref="InitializeAndLaunchAsync"/> has
    /// its initialize response.
    /// </summary>
    public DapCapabilities? Capabilities { get; private set; }

    /// <summary>
    /// Builds the ProcessStartInfo with the load-bearing encodings for a DAP adapter
    /// spoken to over stdio.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(string fileName, string arguments, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // MUST be BOM-less: accessing Process.StandardInput sets AutoFlush=true,
            // which flushes the wrapper StreamWriter and writes the encoding preamble.
            // With Encoding.UTF8 (BOM) that injects EF BB BF into the adapter's stdin,
            // corrupting the first Content-Length header — the adapter never replies.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        if (workingDirectory != null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }

    /// <summary>
    /// Process ID of the spawned adapter process; null for the stream ctor.
    /// Deliberately retained after Dispose so callers can verify the process exited.
    /// </summary>
    public int? AdapterProcessId { get; private set; }

    /// <summary>Raw DAP event dispatch: (EventType, Body), no interpretation.</summary>
    public event EventHandler<DapEventArgs>? EventReceived;

    /// <summary>
    /// Session death is an EVENT, not a hang (spec §8): raised exactly once when the
    /// adapter process exits OR the read loop hits EOF. Carries the exit code when known.
    /// Not raised for a user-initiated stop (cancellation via Dispose).
    /// </summary>
    public event EventHandler<DapSessionClosedEventArgs>? Closed;

    /// <summary>
    /// Spawn the adapter (process ctor) and start the read loop. Returns false if the
    /// session was already started or disposed. Spawn failures propagate as exceptions,
    /// exactly as the pre-extraction inline spawn did.
    /// </summary>
    public bool Start()
    {
        if (_started || _disposed) return false;
        _started = true;

        _cts = new CancellationTokenSource();

        Stream readStream;
        Stream writeStream;

        if (_startInfo != null)
        {
            _debugProcess = new Process { StartInfo = _startInfo };
            _debugProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _outputService.WriteLine($"[DAP Error] {e.Data}", OutputCategory.Debug);
                }
            };
            // Session death is an event, not a hang (spec §8): watch the process
            // directly — a crashed adapter never sends the DAP `terminated` event.
            // Wired before Start so an instant exit cannot be missed.
            _debugProcess.EnableRaisingEvents = true;
            _debugProcess.Exited += (s, e) => RaiseClosed();

            _debugProcess.Start();
            AdapterProcessId = _debugProcess.Id;
            _debugProcess.BeginErrorReadLine();

            writeStream = _debugProcess.StandardInput.BaseStream;
            readStream = _debugProcess.StandardOutput.BaseStream;
        }
        else
        {
            // Test seam: the caller supplied the streams.
            readStream = _readStream!;
            writeStream = _writeStream!;
        }

        _writer = new StreamWriter(writeStream, new UTF8Encoding(false)) { AutoFlush = false };
        // Latin1 maps every byte 1:1 to a char, so Content-Length (a BYTE count)
        // can be honoured exactly; the body is re-decoded as UTF-8 afterwards.
        // A UTF-8 StreamReader here would over-read whenever a message contains
        // multi-byte characters, corrupting the framing of subsequent messages.
        _reader = new StreamReader(readStream, Encoding.Latin1);

        _readTask = Task.Run(() => ReadMessagesAsync(_cts.Token), _cts.Token);
        return true;
    }

    /// <summary>
    /// Send one DAP request and await its response. The timeout budget defaults to
    /// <see cref="Timeouts"/>.Request; pass <paramref name="timeout"/> for requests
    /// with their own budget (launch, steps, disconnect).
    /// </summary>
    public async Task<JsonElement> SendRequestAsync(string command, object arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var seq = Interlocked.Increment(ref _requestSeq);
        // RunContinuationsAsynchronously keeps awaiter continuations from
        // running inline on the read-loop thread while it holds _lock.
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _pendingRequests[seq] = tcs;
        }

        try
        {
            var request = new { seq, type = "request", command, arguments };
            await SendMessageAsync(request);

            // Budgeted timeout: if the adapter dies (SendMessageAsync swallows
            // pipe errors) the response never arrives, and callers that pass
            // no token would otherwise await forever.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout ?? Timeouts.Request);
            using var ctr = timeoutCts.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            lock (_lock) { _pendingRequests.Remove(seq); }
        }
    }

    /// <summary>
    /// Spec-correct DAP startup (spec §3.3.1). The exact sequence, in order, no reordering:
    ///  1. ARM the initialized listener BEFORE anything is sent — DAP allows emission any
    ///     time after the initialize response; the legacy --dap-legacy adapter fires it
    ///     ~50ms after initialize without waiting for launch. Arming late is a lost-event race.
    ///  2. initialize request -> response; RETAIN capabilities.
    ///  3. Send launch/attach WITHOUT awaiting its response. lldb-dap emits `initialized`
    ///     only while processing launch: awaiting the launch response here (the old client)
    ///     OR awaiting `initialized` before sending launch BOTH deadlock against it.
    ///  4. Await `initialized` (may have completed already — legacy).
    ///  5. Configuration: pushBreakpoints callback, then configurationDone.
    ///  6. NOW await the launch response — completed long ago (managed) or only now
    ///     (lldb-dap defers it past configurationDone). Neither timing is an error.
    /// </summary>
    public async Task InitializeAndLaunchAsync(
        string launchCommand,                 // "launch" | "attach"
        object launchArguments,
        object initializeArguments,
        Func<Task> pushConfigurationAsync,    // setBreakpoints / setExceptionBreakpoints
        CancellationToken cancellationToken)
    {
        var initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _initializedTcs = initialized;                                   // completed by the read loop

        var initResponse = await SendRequestAsync("initialize", initializeArguments,
            cancellationToken: cancellationToken);
        Capabilities = DapCapabilities.Parse(initResponse);

        var launchTask = SendRequestAsync(launchCommand, launchArguments,
            timeout: Timeouts.Launch, cancellationToken: cancellationToken);

        try
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(Timeouts.Launch);
                using var ctr = cts.Token.Register(() => initialized.TrySetCanceled());
                await initialized.Task;
            }

            await pushConfigurationAsync();
            await SendRequestAsync("configurationDone", new { }, cancellationToken: cancellationToken);
            await launchTask;
        }
        catch
        {
            // The launch request is still in flight on every failure path after step 3
            // (initialized timeout, breakpoint push, configurationDone). Never leave it
            // un-awaited — an unobserved fault would surface as UnobservedTaskException
            // long after the session died. Observe and swallow; the original failure wins.
            _ = launchTask.ContinueWith(t => _ = t.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            throw;
        }
    }

    /// <summary>Cancel every pending request TCS so no caller is left awaiting.</summary>
    public void CancelPending()
    {
        lock (_lock)
        {
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }

        // A handshake parked on `initialized` must not ride out the full launch
        // budget when the session is already dead/stopped.
        _initializedTcs?.TrySetCanceled();
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
                else
                {
                    // DELIBERATE behavior change in the Phase 4 extraction (spec §8),
                    // the only one: the original loop spun on a null read, so a crashed
                    // adapter (which never sends the DAP `terminated` event) was a
                    // silent hang. A null read means the adapter's stdout hit EOF or
                    // produced a malformed frame (an absent/unparseable Content-Length
                    // also reads back as null) — treat either as session death: break
                    // out and raise Closed (exactly once, guard shared with the
                    // Process.Exited path).
                    RaiseClosed();
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // User-initiated stop (_cts) — not a session death: no Closed.
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
                if (!int.TryParse(line.Substring(15).Trim(), out contentLength))
                    contentLength = 0;
            }
        }

        // Malformed/absent Content-Length: the same null → Closed path as EOF.
        if (contentLength == 0) return null;

        // Read content — contentLength is a BYTE count. The reader uses Latin1
        // (1 byte == 1 char), so reading contentLength chars reads exactly the
        // message body; re-decode those bytes as UTF-8 to get the real JSON.
        var buffer = new char[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var chunk = await _reader.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken);
            if (chunk == 0) return null;
            read += chunk;
        }

        var bytes = Encoding.Latin1.GetBytes(buffer);
        var json = Encoding.UTF8.GetString(bytes);
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
                    // TrySet* — the request may have been cancelled (timeout /
                    // stop) concurrently; Set* would throw on the read loop.
                    if (message.TryGetProperty("success", out var s) && s.GetBoolean())
                    {
                        tcs.TrySetResult(message.TryGetProperty("body", out var body) ? body : default);
                    }
                    else
                    {
                        var errorMsg = message.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                        tcs.TrySetException(new Exception(errorMsg));
                    }
                }
            }
        }
        else if (type == "event")
        {
            var eventType = message.TryGetProperty("event", out var e) ? e.GetString() : null;
            var body = message.TryGetProperty("body", out var b) ? b : default;

            // The handshake awaits this one (spec §3.3.1 step 4); it still flows
            // through EventReceived below like every other event.
            if (eventType == "initialized")
            {
                _initializedTcs?.TrySetResult(true);
            }

            EventReceived?.Invoke(this, new DapEventArgs { EventType = eventType ?? "", Body = body });
        }
    }

    private async Task SendMessageAsync(object message)
    {
        if (_writer == null) return;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var content = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {content.Length}\r\n\r\n";

        await _writeLock.WaitAsync();
        try
        {
            var writer = _writer;
            if (writer == null) return;
            await writer.WriteAsync(header);
            await writer.WriteAsync(json);
            await writer.FlushAsync();
        }
        catch (IOException)
        {
            _writer = null;
        }
        catch (ObjectDisposedException)
        {
            _writer = null;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Raise Closed exactly once, whether we got here from Process.Exited or from
    /// the read loop hitting EOF. Carries the exit code when the process is known
    /// to have exited; null when only the stream ended.
    /// </summary>
    private void RaiseClosed()
    {
        if (Interlocked.Exchange(ref _closedRaised, 1) != 0) return;

        int? exitCode = null;
        var process = _debugProcess;
        if (process != null)
        {
            try
            {
                if (process.HasExited) exitCode = process.ExitCode;
            }
            catch { }
        }

        Closed?.Invoke(this, new DapSessionClosedEventArgs { ExitCode = exitCode });
    }

    public void Dispose()
    {
        // A user-initiated Dispose is not a session death (mirrors the read loop's
        // OperationCanceledException arm): claim the Closed guard up front so the
        // Exited event fired by our own kill below stays silent. If the adapter
        // already died, Closed has fired and the exchange is a no-op.
        Interlocked.Exchange(ref _closedRaised, 1);

        // Cancel the read loop before tearing the streams down.
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }

        // Can be invoked concurrently (session-death handling on a background
        // thread racing user Stop / Dispose on the UI thread). Atomically claim
        // the fields under a lock so the process is killed/disposed exactly once,
        // and HasExited is never called on a disposed Process.
        Process? debugProcess;

        lock (_cleanupLock)
        {
            if (_disposed) return;
            _disposed = true;

            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            _writer = null;
            _reader = null;

            // Stream ctor: the wrappers above own the streams once Start ran; if
            // Start never ran, dispose the raw streams directly. (Stream.Dispose
            // is idempotent, so hitting both is harmless.)
            try { _readStream?.Dispose(); } catch { }
            try { _writeStream?.Dispose(); } catch { }

            debugProcess = _debugProcess;
            _debugProcess = null;
        }

        // Release any callers still awaiting a DAP response so they don't
        // hang after disposal.
        CancelPending();

        // Kill the debug adapter process and its entire process tree
        // (the game/debuggee is a child process of the adapter)
        if (debugProcess != null)
        {
            try
            {
                if (!debugProcess.HasExited)
                {
                    debugProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            try { debugProcess.Dispose(); } catch { }
        }
    }
}

/// <summary>Raw DAP event as read off the wire: type string + unparsed body.</summary>
public sealed class DapEventArgs : EventArgs
{
    public string EventType { get; init; } = "";
    public JsonElement Body { get; init; }
}

/// <summary>Payload for <see cref="DapSession.Closed"/>.</summary>
public sealed class DapSessionClosedEventArgs : EventArgs
{
    /// <summary>Null when only the stream ended (test seam / EOF before exit).</summary>
    public int? ExitCode { get; init; }
}
