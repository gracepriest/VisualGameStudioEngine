using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins what <see cref="OpenVsxClient"/> actually does, before it is promoted onto the path a user
/// touches.
///
/// <para>This client is the most complete Open VSX implementation in the repo and has never
/// executed in production — it is not DI-registered anywhere. Consolidating onto it means moving
/// ~450 lines of never-run code onto the install path, which is exactly the shape that produced the
/// twenty never-executed extension bugs. These tests are the safety net that has to exist first,
/// because today there are none: a repo-wide search for OpenVsxClient in the test project returns
/// only doc comments.</para>
///
/// <para>Everything runs against a loopback <see cref="HttpListener"/> — the client's constructor
/// takes a base URL for exactly this reason. No test here may touch a live registry.</para>
/// </summary>
[TestFixture]
public class OpenVsxClientCharacterizationTests
{
    /// <summary>
    /// A real Open VSX search response, camelCase as the registry actually sends it.
    ///
    /// <para>This is THE regression pin. The shipped panel bug was a bare
    /// <c>JsonSerializer.Deserialize</c> against this exact casing with PascalCase DTOs: it returned
    /// a NON-NULL result whose list was null, so nothing threw, the catch never fired, and the UI
    /// truthfully said "No extensions found" — indistinguishable from a network failure. If this
    /// binding ever regresses it will present the same way, so it must fail here instead.</para>
    /// </summary>
    private const string RealSearchPayload = """
        {
          "offset": 0,
          "totalSize": 731,
          "extensions": [
            {
              "url": "https://open-vsx.org/api/dbaeumer/vscode-eslint",
              "files": {
                "download": "https://open-vsx.org/api/dbaeumer/vscode-eslint/3.0.34/file/dbaeumer.vscode-eslint-3.0.34.vsix",
                "icon": "https://open-vsx.org/api/dbaeumer/vscode-eslint/3.0.34/file/eslint_icon.png"
              },
              "name": "vscode-eslint",
              "namespace": "dbaeumer",
              "version": "3.0.34",
              "timestamp": "2025-01-15T10:00:00Z",
              "displayName": "ESLint",
              "description": "Integrates ESLint JavaScript into VS Code.",
              "downloadCount": 41234567,
              "averageRating": 4.5
            }
          ]
        }
        """;

    [Test]
    public async Task Search_BindsTheRegistrysRealCamelCasePayload()
    {
        using var server = new LoopbackJson(RealSearchPayload);
        using var client = new OpenVsxClient(server.BaseUrl);

        var result = await client.SearchAsync("eslint");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Extensions, Is.Not.Null.And.Not.Empty,
            "a non-null result with a null/empty list is the exact shape of the shipped bug: the UI "
            + "reports 'No extensions found' and nothing anywhere throws");
        Assert.That(result.TotalSize, Is.EqualTo(731));

        var first = result.Extensions![0];
        Assert.That(first.Name, Is.EqualTo("vscode-eslint"));
        Assert.That(first.Namespace, Is.EqualTo("dbaeumer"));
        Assert.That(first.Version, Is.EqualTo("3.0.34"));
        Assert.That(first.DisplayName, Is.EqualTo("ESLint"));
        Assert.That(first.DownloadCount, Is.EqualTo(41234567));
    }

    /// <summary>
    /// The download URL lives in a nested <c>files</c> map. Losing it means an extension that is
    /// found but cannot be installed — and the value is reached by dictionary key, so a binding
    /// change surfaces as a null reference at install time rather than a build error.
    /// </summary>
    [Test]
    public async Task Search_BindsTheNestedDownloadUrl()
    {
        using var server = new LoopbackJson(RealSearchPayload);
        using var client = new OpenVsxClient(server.BaseUrl);

        var result = await client.SearchAsync("eslint");
        var files = result.Extensions![0].Files;

        Assert.That(files, Is.Not.Null);
        Assert.That(files!.ContainsKey("download"), Is.True, "the install path reads files[\"download\"]");
        Assert.That(files["download"], Does.EndWith(".vsix"));
    }

    /// <summary>
    /// ⚠ CHARACTERIZES A KNOWN DEFECT rather than endorsing it.
    ///
    /// <para><c>SearchAsync</c> catches every exception and returns an empty result, so a 500, a DNS
    /// failure and a genuinely empty result are indistinguishable to the caller. That is
    /// structurally the SAME defect the panel just shipped and fixed, now sitting in the client we
    /// are about to promote. This test exists so the behaviour is recorded and so the step that adds
    /// an error channel has something to deliberately break.</para>
    /// </summary>
    [Test]
    public async Task Search_SwallowsServerErrors_KNOWN_DEFECT()
    {
        using var server = new LoopbackStatus(HttpStatusCode.InternalServerError);
        using var client = new OpenVsxClient(server.BaseUrl);

        var result = await client.SearchAsync("anything");

        Assert.That(result, Is.Not.Null, "it returns a result rather than throwing");
        Assert.That(result.Extensions, Is.Null.Or.Empty,
            "KNOWN DEFECT: a server error is reported to the caller as 'no results'. When the error "
            + "channel lands, this test should be REPLACED, not loosened");
    }

    /// <summary>Malformed JSON degrades the same way — recorded for the same reason.</summary>
    [Test]
    public async Task Search_SwallowsMalformedJson_KNOWN_DEFECT()
    {
        using var server = new LoopbackJson("{ this is not json");
        using var client = new OpenVsxClient(server.BaseUrl);

        var result = await client.SearchAsync("anything");

        Assert.That(result.Extensions, Is.Null.Or.Empty);
    }

    // -------------------------------------------------------------- download leg

    /// <summary>
    /// A failed transfer must leave NOTHING at the destination.
    ///
    /// <para>The original streamed straight into the final path via <c>File.Create</c>, so a
    /// mid-transfer failure left a TRUNCATED .vsix sitting where a valid one belongs — which then
    /// extracts as a corrupt archive rather than reporting a download failure. Staging through
    /// <c>.partial</c> and moving only on success is what makes the failure honest.</para>
    /// </summary>
    [Test]
    public void Download_LeavesNoFileBehindWhenTheTransferFails()
    {
        var destination = Path.Combine(Path.GetTempPath(), $"vgs-dl-{Guid.NewGuid():N}.vsix");

        // Declares 4096 bytes, sends 16, then drops the connection.
        using var server = new LoopbackTruncated(declaredLength: 4096, actuallySend: 16);
        using var client = new OpenVsxClient(server.BaseUrl);

        Assert.That(async () => await client.DownloadVsixToFileAsync(server.BaseUrl + "/x.vsix", destination),
            Throws.Exception, "a truncated body must surface as a failure, not a short file");

        Assert.That(File.Exists(destination), Is.False,
            "a truncated .vsix at the destination extracts as a corrupt archive instead of "
            + "reporting that the download failed");
        Assert.That(File.Exists(destination + ".partial"), Is.False, "the staging file must be cleaned up");
    }

    /// <summary>
    /// The binary GET must not advertise <c>Accept: application/json</c>. The client sets that
    /// header for the whole HttpClient because its other calls are API queries; carrying it onto a
    /// .vsix fetch is wrong and can make a strict CDN answer 406.
    /// </summary>
    [Test]
    public async Task Download_DoesNotAskForJsonWhenFetchingABinary()
    {
        var destination = Path.Combine(Path.GetTempPath(), $"vgs-dl-{Guid.NewGuid():N}.vsix");
        using var server = new LoopbackBinary(new byte[] { 1, 2, 3, 4 });
        using var client = new OpenVsxClient(server.BaseUrl);

        try
        {
            await client.DownloadVsixToFileAsync(server.BaseUrl + "/x.vsix", destination);

            var accept = server.LastAccept ?? "";
            Assert.That(accept, Does.Not.Contain("application/json"),
                "this request fetches a binary; the JSON Accept belongs to the API calls only");
            Assert.That(server.LastUserAgent, Is.Not.Null.And.Not.Empty,
                "a User-Agent is load-bearing — some hosts reject requests without one");
        }
        finally
        {
            try { File.Delete(destination); } catch { }
        }
    }

    [Test]
    public async Task Download_WritesTheExactBytes()
    {
        var payload = Enumerable.Range(0, 5000).Select(i => (byte)(i % 256)).ToArray();
        var destination = Path.Combine(Path.GetTempPath(), $"vgs-dl-{Guid.NewGuid():N}.vsix");

        using var server = new LoopbackBinary(payload);
        using var client = new OpenVsxClient(server.BaseUrl);

        try
        {
            await client.DownloadVsixToFileAsync(server.BaseUrl + "/x.vsix", destination);
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(payload));
        }
        finally
        {
            try { File.Delete(destination); } catch { }
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Serves a byte payload and records the request headers it saw.</summary>
    private sealed class LoopbackBinary : LoopbackBase
    {
        private readonly byte[] _payload;
        public string? LastAccept { get; private set; }
        public string? LastUserAgent { get; private set; }

        public LoopbackBinary(byte[] payload) => _payload = payload;

        protected override void Respond(HttpListenerContext ctx)
        {
            LastAccept = ctx.Request.Headers["Accept"];
            LastUserAgent = ctx.Request.Headers["User-Agent"];
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength64 = _payload.Length;
            ctx.Response.OutputStream.Write(_payload, 0, _payload.Length);
        }
    }

    /// <summary>Promises more bytes than it sends, then drops the connection mid-body.</summary>
    private sealed class LoopbackTruncated : LoopbackBase
    {
        private readonly int _declared;
        private readonly int _send;

        public LoopbackTruncated(int declaredLength, int actuallySend)
        {
            _declared = declaredLength;
            _send = actuallySend;
        }

        protected override void Respond(HttpListenerContext ctx)
        {
            ctx.Response.ContentLength64 = _declared;
            ctx.Response.OutputStream.Write(new byte[_send], 0, _send);
            ctx.Response.OutputStream.Flush();
            ctx.Response.Abort();
        }
    }

    /// <summary>Serves one fixed JSON body on loopback, on a port the OS chooses.</summary>
    private sealed class LoopbackJson : LoopbackBase
    {
        private readonly string _body;
        public LoopbackJson(string body) => _body = body;

        protected override void Respond(HttpListenerContext ctx)
        {
            var bytes = Encoding.UTF8.GetBytes(_body);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>Serves one fixed status code.</summary>
    private sealed class LoopbackStatus : LoopbackBase
    {
        private readonly HttpStatusCode _status;
        public LoopbackStatus(HttpStatusCode status) => _status = status;

        protected override void Respond(HttpListenerContext ctx) =>
            ctx.Response.StatusCode = (int)_status;
    }

    private abstract class LoopbackBase : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();

        public string BaseUrl { get; }

        protected LoopbackBase()
        {
            var port = FreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        protected abstract void Respond(HttpListenerContext ctx);

        private async Task Loop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                try { Respond(ctx); }
                catch { /* the test asserts on the client, not the stub */ }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            _cts.Dispose();
        }
    }
}
