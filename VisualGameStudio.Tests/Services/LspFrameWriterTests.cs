using System.Text;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Verifies the LSP frame writer bounds ALL waiting with the caller's token
/// (a wedged server that stops draining stdin blocks writes forever — callers
/// must time out, not hang), while never interleaving frames.
/// </summary>
[TestFixture]
public class LspFrameWriterTests
{
    /// <summary>
    /// TextWriter whose next write can be made to block until released,
    /// simulating a full OS pipe to a server that stopped reading stdin.
    /// </summary>
    private sealed class ControllableWriter : TextWriter
    {
        private TaskCompletionSource? _gate;
        private readonly StringBuilder _written = new();
        private readonly object _sync = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written
        {
            get { lock (_sync) return _written.ToString(); }
        }

        public void BlockNextWrite()
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseBlockedWrite()
        {
            _gate?.TrySetResult();
        }

        public override async Task WriteAsync(string? value)
        {
            var gate = _gate;
            if (gate != null)
            {
                // A released gate is completed and passes writes through
                await gate.Task;
            }
            lock (_sync) _written.Append(value);
        }

        public override Task FlushAsync() => Task.CompletedTask;
    }

    [Test]
    public async Task WriteFrame_WritesContentLengthHeaderWithByteCount()
    {
        var writer = new ControllableWriter();
        using var frameWriter = new LspFrameWriter();

        // 'é' is 1 char but 2 UTF-8 bytes — Content-Length must count BYTES
        var json = "{\"x\":\"é\"}";
        await frameWriter.WriteFrameAsync(writer, json);

        var expectedLength = Encoding.UTF8.GetByteCount(json);
        Assert.That(writer.Written, Is.EqualTo($"Content-Length: {expectedLength}\r\n\r\n{json}"));
    }

    [Test]
    public void WriteFrame_BlockedPipe_TimesOutViaToken_InsteadOfHangingForever()
    {
        var writer = new ControllableWriter();
        using var frameWriter = new LspFrameWriter();
        writer.BlockNextWrite();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
            await frameWriter.WriteFrameAsync(writer, "{}", cts.Token));
    }

    [Test]
    public async Task WriteFrame_QueuedBehindBlockedWrite_AlsoTimesOut()
    {
        // One blocked write must not hang every subsequent LSP call forever:
        // callers queued on the lock time out via their own tokens.
        var writer = new ControllableWriter();
        using var frameWriter = new LspFrameWriter();
        writer.BlockNextWrite();

        using var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var first = frameWriter.WriteFrameAsync(writer, "{\"first\":1}", cts1.Token);

        using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var second = frameWriter.WriteFrameAsync(writer, "{\"second\":2}", cts2.Token);

        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await first);
        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await second);
        await Task.CompletedTask;
    }

    [Test]
    public async Task WriteFrame_AbandonedWrite_NeverInterleavesWithNextFrame()
    {
        var writer = new ControllableWriter();
        using var frameWriter = new LspFrameWriter();
        writer.BlockNextWrite();

        // First frame: write blocks, caller abandons via timeout
        using var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try { await frameWriter.WriteFrameAsync(writer, "{\"first\":1}", cts1.Token); }
        catch (OperationCanceledException) { }

        // Second frame queues while the first write is STILL in flight
        var second = frameWriter.WriteFrameAsync(writer, "{\"second\":2}");
        await Task.Delay(50);
        Assert.That(second.IsCompleted, Is.False,
            "the second frame must wait for the abandoned in-flight write, not interleave with it");
        Assert.That(writer.Written, Is.Empty);

        // Pipe unblocks: the first frame completes IN FULL, then the second
        writer.ReleaseBlockedWrite();
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        var written = writer.Written;
        var firstIndex = written.IndexOf("{\"first\":1}", StringComparison.Ordinal);
        var secondIndex = written.IndexOf("{\"second\":2}", StringComparison.Ordinal);
        Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), "abandoned frame still completes in full");
        Assert.That(secondIndex, Is.GreaterThan(firstIndex), "frames stay whole and ordered");
    }

    [Test]
    public async Task WriteFrame_SequentialFrames_AllWritten()
    {
        var writer = new ControllableWriter();
        using var frameWriter = new LspFrameWriter();

        await frameWriter.WriteFrameAsync(writer, "{\"a\":1}");
        await frameWriter.WriteFrameAsync(writer, "{\"b\":2}");

        Assert.That(writer.Written, Does.Contain("{\"a\":1}"));
        Assert.That(writer.Written, Does.Contain("{\"b\":2}"));
    }
}
