using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins <see cref="FileDownloader"/> — the standalone streamed-download helper lifted from
/// <c>OpenVsxClient.DownloadVsixToFileAsync</c>, fixing that original's three hazards:
/// (1) a 30s whole-operation <c>HttpClient.Timeout</c> that aborts long transfers mid-stream
/// (replaced by an infinite client timeout plus a per-call deadline), (2) streaming straight
/// into the destination so failures leave truncated files (replaced by a <c>.partial</c>
/// stage-then-move), and (3) an <c>Accept: application/json</c> header that is wrong for binaries.
///
/// <para>Everything runs against a local <see cref="HttpListener"/> bound to a dynamically
/// chosen loopback port — no test here may ever touch a live external URL.</para>
/// </summary>
[TestFixture]
public class FileDownloaderTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "vgs-filedownloader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        // Bounded-retry delete: try, catch, wait 250ms, one retry, swallow.
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            Thread.Sleep(250);
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    // ---------------------------------------------------------------- happy path

    [Test]
    public async Task Download_WritesTheExactBytes_AndReportsProgress()
    {
        var payload = DeterministicBytes(1024 * 1024); // 1MB of i % 256
        using var server = new LoopbackServer(async ctx =>
        {
            ctx.Response.ContentLength64 = payload.Length;
            await WriteInChunksAsync(ctx.Response.OutputStream, payload, chunkSize: 64 * 1024);
        });

        var destination = Path.Combine(_tempDir, "asset.bin");
        var progress = new RecordingProgress();
        using var downloader = new FileDownloader();

        await downloader.DownloadAsync(
            server.BaseUrl + "asset.bin", destination, TimeSpan.FromSeconds(30), progress);

        var actual = await File.ReadAllBytesAsync(destination);
        Assert.That(actual.Length, Is.EqualTo(payload.Length));
        Assert.That(actual.SequenceEqual(payload), Is.True, "downloaded bytes must match the served bytes exactly");

        var reports = progress.Snapshot();
        Assert.That(reports, Is.Not.Empty);
        Assert.That(reports[^1], Is.EqualTo(((long)payload.Length, (long)payload.Length)),
            "final progress report must be (total, total)");
        Assert.That(reports.Any(r => r.bytesDownloaded < payload.Length), Is.True,
            "an 80KB read buffer over a 1MB body must yield at least one intermediate report");

        Assert.That(File.Exists(destination + ".partial"), Is.False,
            "the staging file must be moved into place, not left beside the destination");
    }

    [Test]
    public async Task Download_WithoutContentLength_ReportsMinusOneTotal()
    {
        var payload = DeterministicBytes(200 * 1024);
        using var server = new LoopbackServer(async ctx =>
        {
            ctx.Response.SendChunked = true; // no Content-Length header
            await WriteInChunksAsync(ctx.Response.OutputStream, payload, chunkSize: 32 * 1024);
        });

        var destination = Path.Combine(_tempDir, "chunked.bin");
        var progress = new RecordingProgress();
        using var downloader = new FileDownloader();

        await downloader.DownloadAsync(
            server.BaseUrl + "chunked.bin", destination, TimeSpan.FromSeconds(30), progress);

        var actual = await File.ReadAllBytesAsync(destination);
        Assert.That(actual.SequenceEqual(payload), Is.True, "bytes must still be exact without a Content-Length");

        var reports = progress.Snapshot();
        Assert.That(reports, Is.Not.Empty);
        Assert.That(reports[^1].totalBytes, Is.EqualTo(-1L), "unknown length must surface as -1, like the original");
        Assert.That(reports[^1].bytesDownloaded, Is.EqualTo((long)payload.Length));
    }

    // ---------------------------------------------------------------- failure hygiene

    [Test]
    public async Task Failure_MidStream_LeavesNoPartialFile()
    {
        var promised = 400 * 1024;
        var payload = DeterministicBytes(promised / 2);
        using var server = new LoopbackServer(async ctx =>
        {
            ctx.Response.ContentLength64 = promised; // promise 400KB…
            await WriteInChunksAsync(ctx.Response.OutputStream, payload, chunkSize: 32 * 1024);
            ctx.Response.Abort(); // …deliver half, then kill the connection
        });

        var destination = Path.Combine(_tempDir, "truncated.bin");
        using var downloader = new FileDownloader();

        var ex = Assert.CatchAsync(async () => await downloader.DownloadAsync(
            server.BaseUrl + "truncated.bin", destination, TimeSpan.FromSeconds(30)));

        Assert.That(ex, Is.Not.Null);
        Assert.That(File.Exists(destination), Is.False, "a failed download must not produce the destination file");
        Assert.That(File.Exists(destination + ".partial"), Is.False, "a failed download must not leave a .partial file");
    }

    [Test]
    public async Task Cancellation_AbortsAndCleansUp()
    {
        using var server = new LoopbackServer(async ctx =>
        {
            ctx.Response.SendChunked = true;
            var chunk = DeterministicBytes(4 * 1024);
            for (var i = 0; i < 200; i++) // bounded: at most 200 * 50ms = 10s even if never aborted
            {
                await ctx.Response.OutputStream.WriteAsync(chunk);
                await ctx.Response.OutputStream.FlushAsync();
                await Task.Delay(50);
            }
        });

        var destination = Path.Combine(_tempDir, "cancelled.bin");
        using var cts = new CancellationTokenSource();
        // Deterministic: cancel as soon as the first chunk lands (Report runs synchronously in the copy loop).
        var progress = new RecordingProgress(_ => cts.Cancel());
        using var downloader = new FileDownloader();

        Assert.CatchAsync<OperationCanceledException>(async () => await downloader.DownloadAsync(
            server.BaseUrl + "drip.bin", destination, TimeSpan.FromSeconds(30), progress, cts.Token));

        Assert.That(File.Exists(destination), Is.False, "a cancelled download must not produce the destination file");
        Assert.That(File.Exists(destination + ".partial"), Is.False, "a cancelled download must not leave a .partial file");
    }

    [Test]
    public async Task Deadline_AbortsALongTransfer()
    {
        using var server = new LoopbackServer(async ctx =>
        {
            ctx.Response.SendChunked = true;
            var oneByte = new byte[] { 0x42 };
            for (var i = 0; i < 50; i++) // bounded: at most 50 * 200ms = 10s even if never aborted
            {
                await ctx.Response.OutputStream.WriteAsync(oneByte);
                await ctx.Response.OutputStream.FlushAsync();
                await Task.Delay(200);
            }
        });

        var destination = Path.Combine(_tempDir, "slow.bin");
        var deadline = TimeSpan.FromSeconds(1);
        using var downloader = new FileDownloader();

        // Contract choice, asserted exactly: when the DEADLINE (not the caller's token) aborts the
        // transfer, FileDownloader surfaces TimeoutException naming the deadline — a plain OCE would
        // be indistinguishable from a caller cancellation.
        var ex = Assert.ThrowsAsync<TimeoutException>(async () => await downloader.DownloadAsync(
            server.BaseUrl + "slow.bin", destination, deadline));

        Assert.That(ex!.Message, Does.Contain(deadline.ToString()),
            "the deadline that fired must be named in the message");
        Assert.That(File.Exists(destination), Is.False, "a timed-out download must not produce the destination file");
        Assert.That(File.Exists(destination + ".partial"), Is.False, "a timed-out download must not leave a .partial file");
    }

    // ---------------------------------------------------------------- request headers

    [Test]
    public async Task UserAgent_IsSent_AndAcceptJsonIsNot()
    {
        string? capturedUserAgent = null;
        string? capturedAccept = null;
        using var server = new LoopbackServer(async ctx =>
        {
            capturedUserAgent = ctx.Request.Headers["User-Agent"];
            capturedAccept = ctx.Request.Headers["Accept"];
            var body = new byte[] { 1, 2, 3 };
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body);
        });

        var destination = Path.Combine(_tempDir, "tiny.bin");
        using var downloader = new FileDownloader();

        await downloader.DownloadAsync(server.BaseUrl + "tiny.bin", destination, TimeSpan.FromSeconds(30));

        // GitHub rejects requests without a User-Agent; pin the exact product string we send.
        Assert.That(capturedUserAgent, Is.EqualTo("VisualGameStudio/1.0"));

        // The original's `Accept: application/json` is wrong for binaries: absent-or-not-json here.
        Assert.That(capturedAccept, Is.Null.Or.Not.EqualTo("application/json"),
            "FileDownloader must not advertise application/json for binary downloads");
    }

    // ---------------------------------------------------------------- helpers

    private static byte[] DeterministicBytes(int count)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++)
        {
            bytes[i] = (byte)(i % 256);
        }
        return bytes;
    }

    private static async Task WriteInChunksAsync(Stream output, byte[] payload, int chunkSize)
    {
        for (var offset = 0; offset < payload.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, payload.Length - offset);
            await output.WriteAsync(payload.AsMemory(offset, length));
            await output.FlushAsync();
        }
    }

    /// <summary>
    /// Records progress tuples synchronously (unlike <see cref="Progress{T}"/>, whose posts race
    /// the awaiting test through the SynchronizationContext), optionally invoking a callback per
    /// report so tests can trigger cancellation at a deterministic point in the transfer.
    /// </summary>
    private sealed class RecordingProgress : IProgress<(long bytesDownloaded, long totalBytes)>
    {
        private readonly List<(long bytesDownloaded, long totalBytes)> _reports = new();
        private readonly Action<(long bytesDownloaded, long totalBytes)>? _onReport;

        public RecordingProgress(Action<(long bytesDownloaded, long totalBytes)>? onReport = null)
            => _onReport = onReport;

        public void Report((long bytesDownloaded, long totalBytes) value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
            _onReport?.Invoke(value);
        }

        public (long bytesDownloaded, long totalBytes)[] Snapshot()
        {
            lock (_reports)
            {
                return _reports.ToArray();
            }
        }
    }

    /// <summary>
    /// A one-fixture loopback HTTP server: binds a dynamically chosen free port (never a fixed
    /// one — the suite runs on arbitrary dev machines), serves every request through the supplied
    /// handler, and shuts the accept loop down with a bounded wait on dispose.
    /// </summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _acceptLoop;

        public string BaseUrl { get; }

        public LoopbackServer(Func<HttpListenerContext, Task> handler)
        {
            _listener = Bind(out var baseUrl);
            BaseUrl = baseUrl;
            _acceptLoop = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        break; // listener stopped/disposed — loop is done
                    }

                    try
                    {
                        await handler(context).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Client aborted mid-write, or the handler aborted deliberately.
                    }
                    finally
                    {
                        try { context.Response.Close(); } catch { }
                    }
                }
            });
        }

        private static HttpListener Bind(out string baseUrl)
        {
            Exception? lastFailure = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                // Let the OS hand us a free port, then bind HttpListener to it. The tiny window
                // between TcpListener.Stop and HttpListener.Start can lose the port to another
                // process, hence the retry loop.
                var tcp = new TcpListener(IPAddress.Loopback, 0);
                tcp.Start();
                var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                tcp.Stop();

                // 127.0.0.1 first; fall back to localhost (some machines ACL-restrict the
                // numeric loopback prefix for non-elevated processes).
                foreach (var host in new[] { "127.0.0.1", "localhost" })
                {
                    var prefix = $"http://{host}:{port}/";
                    var listener = new HttpListener();
                    listener.Prefixes.Add(prefix);
                    try
                    {
                        listener.Start();
                        baseUrl = prefix;
                        return listener;
                    }
                    catch (HttpListenerException ex)
                    {
                        lastFailure = ex;
                        try { listener.Close(); } catch { }
                    }
                }
            }

            throw new InvalidOperationException("Could not bind a loopback HttpListener on any candidate port.", lastFailure);
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            try { _acceptLoop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }
}
