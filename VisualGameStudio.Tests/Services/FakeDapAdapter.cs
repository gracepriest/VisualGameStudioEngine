using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Scripted in-proc DAP adapter (Phase 4 Task 3): speaks the byte-exact wire contract
/// (<c>Content-Length: N\r\n\r\n</c> + N BYTES of UTF-8 JSON) over two anonymous-pipe
/// pairs, so <c>DapSession</c>'s stream ctor can be exercised against the three
/// real-world <c>initialized</c>-timing regimes without spawning a process:
/// lldb-dap-shaped (initialized after the launch REQUEST, launch RESPONSE deferred to
/// configurationDone), managed-shaped (initialized during launch, before its response),
/// and legacy <c>--dap-legacy</c>-shaped (initialized right after the initialize response).
/// Every request is answered immediately except where those knobs say otherwise.
/// </summary>
public sealed class FakeDapAdapter : IDisposable
{
    public enum InitializedTiming
    {
        /// <summary>lldb-dap-shaped: `initialized` fires once the launch request is received.</summary>
        AfterLaunchRequestReceived,
        /// <summary>Managed-shaped: `initialized` fires while launch is handled, before its response.</summary>
        DuringLaunchBeforeItsResponse,
        /// <summary>Legacy `--dap-legacy`-shaped: `initialized` fires right after the initialize response.</summary>
        RightAfterInitializeResponse
    }

    private const int FirstBytesCaptureLimit = 256;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // session -> adapter: the fake reads the server (In) end; SessionWrites is the client (Out) end.
    private readonly AnonymousPipeServerStream _fromSession;
    // adapter -> session: the fake writes the server (Out) end; SessionReads is the client (In) end.
    private readonly AnonymousPipeServerStream _toSession;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _failNext = new();
    private readonly ConcurrentDictionary<string, object> _bodyNext = new();
    private readonly object _captureLock = new();
    private readonly List<byte> _firstBytes = new();
    private readonly Queue<byte> _incoming = new();
    private readonly byte[] _readChunk = new byte[4096];
    private readonly Task _pumpTask;
    private int _adapterSeq;
    private int _deferredLaunchSeq = -1;   // -1 = no launch response parked
    private volatile bool _closed;

    public InitializedTiming Timing { get; set; }

    /// <summary>lldb-dap-shaped: hold the launch response until configurationDone arrives.</summary>
    public bool DeferLaunchResponseUntilConfigurationDone { get; set; }

    /// <summary>Returned verbatim as the initialize response body.</summary>
    public object CapabilitiesBody { get; set; } = new { supportsConfigurationDoneRequest = true };

    /// <summary>Every request the fake parsed off the wire, in arrival order.</summary>
    public ConcurrentQueue<(string Command, JsonElement Arguments)> Received { get; } = new();

    /// <summary>
    /// Raw capture of the session's first bytes BEFORE any framing parse — the place a
    /// BOM (EF BB BF) would land if the session's write side ever regressed to one.
    /// </summary>
    public byte[] FirstBytesFromSession
    {
        get { lock (_captureLock) { return _firstBytes.ToArray(); } }
    }

    /// <summary>adapter -> session: hand this to DapSession as its read stream.</summary>
    public Stream SessionReads { get; }

    /// <summary>session -> adapter: hand this to DapSession as its write stream.</summary>
    public Stream SessionWrites { get; }

    public FakeDapAdapter()
    {
        _fromSession = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        _toSession = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        // Accessing ClientSafePipeHandle marks the handle "exposed", so disposing the
        // server stream later closes only ITS OWN end — exactly what CloseFromAdapterSide
        // needs to surface a clean EOF (not a ripped handle) on the session's reader.
        SessionWrites = new AnonymousPipeClientStream(PipeDirection.Out, _fromSession.ClientSafePipeHandle);
        SessionReads = new AnonymousPipeClientStream(PipeDirection.In, _toSession.ClientSafePipeHandle);

        _pumpTask = Task.Run(PumpAsync);
    }

    // ------------------------------------------------------------------
    // Regime factories
    // ------------------------------------------------------------------

    /// <summary>lldb-dap-shaped: initialized after the launch request; launch response deferred.</summary>
    public static FakeDapAdapter LldbShaped() => new()
    {
        Timing = InitializedTiming.AfterLaunchRequestReceived,
        DeferLaunchResponseUntilConfigurationDone = true,
        CapabilitiesBody = new
        {
            supportsConfigurationDoneRequest = true,
            exceptionBreakpointFilters = new object[]
            {
                new { filter = "cpp_throw", label = "C++ Throw", @default = false },
                new { filter = "cpp_catch", label = "C++ Catch", @default = false }
            }
        }
    };

    /// <summary>Managed-shaped: initialized during launch handling, before the launch response.</summary>
    public static FakeDapAdapter ManagedShaped() => new()
    {
        Timing = InitializedTiming.DuringLaunchBeforeItsResponse,
        CapabilitiesBody = new
        {
            supportsConfigurationDoneRequest = true,
            exceptionBreakpointFilters = new object[]
            {
                new { filter = "all", label = "All Exceptions", @default = false },
                new { filter = "uncaught", label = "Uncaught Exceptions", @default = true },
                new { filter = "thrown", label = "Thrown Exceptions", @default = false }
            }
        }
    };

    /// <summary>Legacy --dap-legacy-shaped: initialized emitted right after the initialize response.</summary>
    public static FakeDapAdapter LegacyShaped() => new()
    {
        Timing = InitializedTiming.RightAfterInitializeResponse,
        CapabilitiesBody = new { supportsConfigurationDoneRequest = true }
    };

    // ------------------------------------------------------------------
    // Scripting surface
    // ------------------------------------------------------------------

    /// <summary>Push an adapter event at the session, e.g. stopped {reason, threadId}.</summary>
    public void EmitEvent(string eventType, object body)
        => SendEventAsync(eventType, body).GetAwaiter().GetResult();

    /// <summary>The next request with this command gets {success:false, message} (one-shot).</summary>
    public void RespondToNextRequestWithFailure(string command, string message)
        => _failNext[command] = message;

    /// <summary>
    /// The next request with this command gets {success:true, body} (one-shot) — for
    /// requests whose caller needs a real body (e.g. gotoTargets' targets array), which
    /// the generic empty-body responder cannot satisfy.
    /// </summary>
    public void RespondToNextRequestWithBody(string command, object body)
        => _bodyNext[command] = body;

    /// <summary>
    /// Simulates adapter death: closes the fake's ends of both pipes. The session's
    /// reader sees a clean EOF (broken pipe reads as 0 bytes); its writes start failing.
    /// </summary>
    public void CloseFromAdapterSide()
    {
        _closed = true;
        try { _toSession.Dispose(); } catch { }
        try { _fromSession.Dispose(); } catch { }
    }

    public void Dispose()
    {
        CloseFromAdapterSide();
        try { SessionReads.Dispose(); } catch { }
        try { SessionWrites.Dispose(); } catch { }
        // Bounded courtesy wait; the pump dies on the torn-down pipes either way.
        try { _pumpTask.Wait(500); } catch { }
    }

    // ------------------------------------------------------------------
    // Adapter loop — byte-exact wire contract
    // ------------------------------------------------------------------

    private async Task PumpAsync()
    {
        try
        {
            while (!_closed)
            {
                var message = await ReadFrameAsync();
                if (message == null) return;   // session closed its write end
                await DispatchAsync(message.Value);
            }
        }
        catch
        {
            // Torn-down pipe (CloseFromAdapterSide / Dispose) — the fake dies quietly.
        }
    }

    /// <summary>Pull one byte, refilling from the pipe; every raw byte read feeds the first-bytes capture.</summary>
    private async Task<int> NextByteAsync()
    {
        if (_incoming.Count == 0)
        {
            int n = await _fromSession.ReadAsync(_readChunk, 0, _readChunk.Length);
            if (n == 0) return -1;

            lock (_captureLock)
            {
                int room = FirstBytesCaptureLimit - _firstBytes.Count;
                for (int i = 0; i < Math.Min(n, room); i++) _firstBytes.Add(_readChunk[i]);
            }

            for (int i = 0; i < n; i++) _incoming.Enqueue(_readChunk[i]);
        }

        return _incoming.Dequeue();
    }

    private async Task<string?> ReadHeaderLineAsync()
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = await NextByteAsync();
            if (b < 0) return null;
            if (b == '\n') break;
            if (b != '\r') sb.Append((char)b);
        }
        return sb.ToString();
    }

    private async Task<JsonElement?> ReadFrameAsync()
    {
        int contentLength = -1;
        while (true)
        {
            var line = await ReadHeaderLineAsync();
            if (line == null) return null;
            if (line.Length == 0) break;

            // A BOM-prefixed header would NOT match here — the frame is dropped and the
            // session's request times out, which is exactly how a real adapter behaves.
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.Substring("Content-Length:".Length).Trim(), out var parsed))
            {
                contentLength = parsed;
            }
        }

        if (contentLength < 0) return null;

        // Content-Length counts BYTES of the UTF-8 body — read exactly that many.
        var body = new byte[contentLength];
        for (int i = 0; i < contentLength; i++)
        {
            int b = await NextByteAsync();
            if (b < 0) return null;
            body[i] = (byte)b;
        }

        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(body), JsonOptions);
    }

    private async Task DispatchAsync(JsonElement message)
    {
        var type = message.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type != "request") return;

        var command = message.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "";
        var seq = message.TryGetProperty("seq", out var s) ? s.GetInt32() : 0;
        var args = message.TryGetProperty("arguments", out var a) ? a.Clone() : default;
        Received.Enqueue((command, args));

        if (_failNext.TryRemove(command, out var failMessage))
        {
            await SendResponseAsync(seq, command, success: false, body: null, message: failMessage);
            return;
        }

        if (_bodyNext.TryRemove(command, out var scriptedBody))
        {
            await SendResponseAsync(seq, command, success: true, body: scriptedBody);
            return;
        }

        switch (command)
        {
            case "initialize":
                await SendResponseAsync(seq, command, success: true, body: CapabilitiesBody);
                if (Timing == InitializedTiming.RightAfterInitializeResponse)
                    await SendEventAsync("initialized", new { });
                break;

            case "launch":
                if (Timing is InitializedTiming.AfterLaunchRequestReceived
                           or InitializedTiming.DuringLaunchBeforeItsResponse)
                {
                    await SendEventAsync("initialized", new { });
                }

                if (DeferLaunchResponseUntilConfigurationDone)
                    _deferredLaunchSeq = seq;   // parked until configurationDone
                else
                    await SendResponseAsync(seq, command, success: true, body: new { });
                break;

            case "configurationDone":
                await SendResponseAsync(seq, command, success: true, body: new { });
                var deferred = Interlocked.Exchange(ref _deferredLaunchSeq, -1);
                if (deferred >= 0)
                    await SendResponseAsync(deferred, "launch", success: true, body: new { });
                break;

            default:
                await SendResponseAsync(seq, command, success: true, body: new { });
                break;
        }
    }

    private Task SendResponseAsync(int requestSeq, string command, bool success, object? body, string? message = null)
        => WriteFrameAsync(new
        {
            seq = Interlocked.Increment(ref _adapterSeq),
            type = "response",
            request_seq = requestSeq,
            success,
            command,
            message,
            body
        });

    private Task SendEventAsync(string eventType, object body)
        => WriteFrameAsync(new
        {
            seq = Interlocked.Increment(ref _adapterSeq),
            type = "event",
            @event = eventType,
            body
        });

    private async Task WriteFrameAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);   // Content-Length is a BYTE count
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");

        await _writeLock.WaitAsync();
        try
        {
            if (_closed) return;
            await _toSession.WriteAsync(header, 0, header.Length);
            await _toSession.WriteAsync(bytes, 0, bytes.Length);
            await _toSession.FlushAsync();
        }
        catch (IOException)
        {
            // Session tore its end down mid-write — the fake shrugs, like a real adapter would.
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
