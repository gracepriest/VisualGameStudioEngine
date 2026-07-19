using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Phase 4 Task 3 — the transport unit ring the wire never had. Exercises the REAL
/// <see cref="DapSession"/> stream ctor against <see cref="FakeDapAdapter"/> (a scripted
/// in-proc adapter speaking the byte-exact framing contract): BOM-less first bytes,
/// byte-count framing across multibyte bodies, failure responses faulting the request
/// task, adapter death as an event (Closed exactly once, pendings released, never a
/// hang), Dispose suppressing Closed, and raw event dispatch. One test drives the UI
/// half: DebugService over the sessionFactory seam returns to edit mode on adapter crash.
/// Every await is budget-bounded — a hung transport must FAIL, not hang the suite.
/// </summary>
[TestFixture]
public class DapSessionTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>Grace window for NEGATIVE assertions (an event that must NOT fire).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(300);

    // ------------------------------------------------------------------
    // The transport ring — DapSession over the fake, stream seam
    // ------------------------------------------------------------------

    [Test]
    public async Task FirstBytesFromSession_AreAContentLengthHeader_NoBomEver()
    {
        using var fake = FakeDapAdapter.LegacyShaped();
        using var session = new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService());
        session.Start();

        // One full round-trip proves the fake parsed the frame the session framed.
        await WithTimeout(session.SendRequestAsync("initialize", new { adapterID = "test" }),
            "initialize response");

        var first = fake.FirstBytesFromSession;
        Assert.That(first, Has.Length.GreaterThanOrEqualTo(15),
            "the fake captured no bytes from the session");
        // EF BB BF pinned out — the exact regression BuildStartInfo's BOM-less
        // encodings and the UTF8Encoding(false) writer exist to prevent.
        Assert.That(first.Take(3).ToArray(), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }),
            "the session wrote a UTF-8 BOM before its first Content-Length header");
        Assert.That(Encoding.ASCII.GetString(first, 0, 15), Is.EqualTo("Content-Length:"),
            $"the session's first bytes are not a Content-Length header: " +
            $"[{string.Join(" ", first.Take(20).Select(b => b.ToString("X2")))}]");
    }

    [Test]
    public async Task MultibyteBody_DoesNotCorruptFramingOfTheNextMessage()
    {
        const string multibyte = "héllo — ✓";   // 2-byte é, 3-byte em dash, 3-byte check mark

        using var fake = FakeDapAdapter.LegacyShaped();
        using var session = new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService());

        var outputEvent = NewTcs<DapEventArgs>();
        session.EventReceived += (_, e) => { if (e.EventType == "output") outputEvent.TrySetResult(e); };
        session.Start();

        // Frame 1: a body whose char count != byte count. A UTF-8 reader honouring
        // Content-Length in CHARS would over-read here and desync every later frame —
        // the Latin1 byte-count re-decode is what this test pins.
        fake.EmitEvent("output", new { category = "console", output = multibyte });

        var evt = await WithTimeout(outputEvent.Task, "the multibyte output event");
        Assert.That(evt.Body.GetProperty("output").GetString(), Is.EqualTo(multibyte),
            "the multibyte body did not survive the framing round-trip intact");

        // Frame 2 must still parse off the same stream: request/response round-trip.
        var body = await WithTimeout(session.SendRequestAsync("threads", new { }),
            "the threads response AFTER the multibyte event — framing desync if this times out");
        Assert.That(body.ValueKind, Is.EqualTo(JsonValueKind.Object),
            "the response following the multibyte event arrived corrupted");
    }

    [Test]
    public async Task FailureResponse_FaultsTheRequestTask()
    {
        using var fake = FakeDapAdapter.LegacyShaped();
        using var session = new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService());
        session.Start();

        fake.RespondToNextRequestWithFailure("evaluate", "the fake says no");

        var sendTask = session.SendRequestAsync("evaluate", new { expression = "x" });
        Observe(sendTask);

        var completed = await Task.WhenAny(sendTask, Task.Delay(Budget));
        Assert.That(completed, Is.SameAs(sendTask),
            $"the evaluate request did not complete within {Budget.TotalSeconds:F0}s of its failure response");
        Assert.That(sendTask.IsFaulted, Is.True,
            $"a success:false response must FAULT the request task (status was {sendTask.Status})");
        Assert.That(sendTask.Exception!.InnerException!.Message, Does.Contain("the fake says no"),
            "the adapter's error message was not carried onto the faulted task");
    }

    [Test]
    public async Task AdapterDeath_EndsTheReadLoopQuietly_AndPendingRequestsDoNotHangForever()
    {
        using var fake = FakeDapAdapter.LldbShaped();   // defers the launch response — a genuinely pending request
        var output = new RecordingOutputService();
        using var session = new DapSession(fake.SessionReads, fake.SessionWrites, output);

        var closed = NewTcs<bool>();
        session.Closed += (_, __) =>
        {
            // The production reaction, composed at the transport level: DebugService's
            // Closed handler tears the session down, which releases pending awaiters.
            session.CancelPending();
            closed.TrySetResult(true);
        };
        session.Start();

        await WithTimeout(session.SendRequestAsync("initialize", new { adapterID = "test" }),
            "initialize response");

        var launchTask = session.SendRequestAsync("launch", new { program = "x.exe" });
        Observe(launchTask);
        await WaitUntilAsync(() => fake.Received.Any(r => r.Command == "launch"),
            "the fake to receive the launch request");
        Assert.That(launchTask.IsCompleted, Is.False,
            "the lldb-shaped fake answered launch immediately — the pending-request scenario never existed");

        fake.CloseFromAdapterSide();

        await WithTimeout(closed.Task, "Closed after the adapter died");

        var completed = await Task.WhenAny(launchTask, Task.Delay(Budget));
        Assert.That(completed, Is.SameAs(launchTask),
            $"the pending launch request was still hanging {Budget.TotalSeconds:F0}s after adapter death");
        Assert.That(launchTask.IsCanceled || launchTask.IsFaulted, Is.True,
            $"the pending request must complete canceled/faulted, not {launchTask.Status}");

        // QUIETLY: adapter death is the clean EOF arm (null read -> Closed -> break),
        // not the exception arm spamming read errors.
        Assert.That(output.Dump(), Does.Not.Contain("[DAP] Read error"),
            "adapter death took the exception arm instead of the clean EOF arm:\n" + output.Dump());
    }

    [Test]
    public async Task AdapterDeath_RaisesClosedExactlyOnce()
    {
        using var fake = FakeDapAdapter.LegacyShaped();
        using var session = new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService());

        int closedCount = 0;
        var closed = NewTcs<bool>();
        session.Closed += (_, __) =>
        {
            Interlocked.Increment(ref closedCount);
            closed.TrySetResult(true);
        };
        session.Start();

        await WithTimeout(session.SendRequestAsync("initialize", new { adapterID = "test" }),
            "initialize response");

        fake.CloseFromAdapterSide();
        await WithTimeout(closed.Task, "Closed after the adapter died (EOF path)");

        // A subsequent Dispose must NOT re-raise through its own guard claim.
        session.Dispose();
        await Task.Delay(Grace);

        Assert.That(Volatile.Read(ref closedCount), Is.EqualTo(1),
            "Closed must be raised exactly once across the EOF and Dispose paths");
    }

    [Test]
    public async Task UserStop_DoesNotRaiseClosed()
    {
        using var fake = FakeDapAdapter.LegacyShaped();
        using var session = new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService());

        int closedCount = 0;
        session.Closed += (_, __) => Interlocked.Increment(ref closedCount);
        session.Start();

        await WithTimeout(session.SendRequestAsync("initialize", new { adapterID = "test" }),
            "initialize response");

        // The session's own stop path: Dispose cancels the read loop (OCE arm / guard
        // claim). The adapter is still perfectly alive — this is a user stop, not a death.
        session.Dispose();

        await Task.Delay(Grace);
        Assert.That(Volatile.Read(ref closedCount), Is.Zero,
            "a user-initiated stop must never present as a session death (Closed fired)");
    }

    [Test]
    public async Task DisposeSuppressesClosed()
    {
        using var fake = FakeDapAdapter.LegacyShaped();
        var session = new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService());

        int closedCount = 0;
        session.Closed += (_, __) => Interlocked.Increment(ref closedCount);
        session.Start();

        // A completed exchange first — the session is mid-flight, read loop parked.
        await WithTimeout(session.SendRequestAsync("initialize", new { adapterID = "test" }),
            "initialize response");

        // Dispose FIRST (claims the Closed guard before stream teardown) …
        session.Dispose();
        // … adapter-side teardown AFTER: the EOF this produces lands on a disposed
        // session and must stay silent.
        fake.CloseFromAdapterSide();

        await Task.Delay(Grace);
        Assert.That(Volatile.Read(ref closedCount), Is.Zero,
            "stream teardown after Dispose must not raise Closed — the guard is claimed first");
    }

    [Test]
    public async Task Events_AreRaisedWithTypeAndBody()
    {
        using var fake = FakeDapAdapter.LegacyShaped();
        using var session = new DapSession(fake.SessionReads, fake.SessionWrites, new RecordingOutputService());

        var stoppedEvent = NewTcs<DapEventArgs>();
        session.EventReceived += (_, e) => { if (e.EventType == "stopped") stoppedEvent.TrySetResult(e); };
        session.Start();

        // threadId deliberately not 1 — proves the body is plumbed, not defaulted.
        fake.EmitEvent("stopped", new { reason = "breakpoint", threadId = 7, allThreadsStopped = true });

        var evt = await WithTimeout(stoppedEvent.Task, "the stopped event");
        Assert.That(evt.EventType, Is.EqualTo("stopped"));
        Assert.That(evt.Body.GetProperty("reason").GetString(), Is.EqualTo("breakpoint"));
        Assert.That(evt.Body.GetProperty("threadId").GetInt32(), Is.EqualTo(7));
    }

    // ------------------------------------------------------------------
    // The UI half — DebugService over the sessionFactory seam
    // ------------------------------------------------------------------

    [Test]
    public async Task AdapterCrashMidSession_ReturnsToEditMode_AndWritesDiagnostic()
    {
        var output = new RecordingOutputService();
        using var fake = FakeDapAdapter.ManagedShaped();
        var session = new DapSession(fake.SessionReads, fake.SessionWrites, output);
        var service = new DebugService(output, _ => session);

        try
        {
            var stopped = NewTcs<bool>();
            service.StateChanged += (_, e) =>
            {
                if (e.NewState == DebugState.Stopped) stopped.TrySetResult(true);
            };

            // The CURRENT handshake order (initialize -> await launch -> configurationDone).
            // The managed-shaped fake tolerates it BY DESIGN: initialized during launch,
            // launch answered immediately. (Task 4 rewrites the order; this ring must
            // stay green before AND after.)
            var started = await WithTimeout(service.StartDebuggingAsync(new DebugConfiguration
            {
                Program = "FakeApp.exe",
                WorkingDirectory = Path.GetTempPath()
            }), "StartDebuggingAsync handshake against the managed-shaped fake");

            Assert.That(started, Is.True,
                "the handshake against the managed-shaped fake failed:\n" + output.Dump());
            Assert.That(service.State, Is.EqualTo(DebugState.Running),
                "the debug session did not reach Running:\n" + output.Dump());

            // Mid-session crash.
            fake.CloseFromAdapterSide();

            await WithTimeout(stopped.Task,
                "DebugState.Stopped after the adapter crash (the IDE must return to edit mode, not hang)");
            Assert.That(service.State, Is.EqualTo(DebugState.Stopped));
            Assert.That(output.Dump(), Does.Contain("exited unexpectedly"),
                "the crash diagnostic was not written to the output pane:\n" + output.Dump());
        }
        finally
        {
            service.Dispose();
            session.Dispose();   // belt-and-braces; DebugService's cleanup already did this
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static TaskCompletionSource<T> NewTcs<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<T> WithTimeout<T>(Task<T> task, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Budget));
        if (completed != task)
            Assert.Fail($"Timed out after {Budget.TotalSeconds:F0}s waiting for: {what}");
        return await task;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Budget;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Timed out after {Budget.TotalSeconds:F0}s waiting for: {what}");
    }

    /// <summary>
    /// A test can bail (Assert.Fail on timeout) before awaiting a request task; if that
    /// task later faults on teardown it must not surface as an UnobservedTaskException.
    /// </summary>
    private static void Observe(Task task)
        => _ = task.ContinueWith(static t => _ = t.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>
    /// Thread-safe IOutputService that records everything, so test failures can include
    /// the real DAP output instead of a bare assertion message. Duplicated per suite
    /// convention (the siblings in IdeInAngerTests/NativeDebugGateTests are private).
    /// </summary>
    private sealed class RecordingOutputService : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Dump() => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue("ERROR: " + message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => _lines.ToArray();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }
}
