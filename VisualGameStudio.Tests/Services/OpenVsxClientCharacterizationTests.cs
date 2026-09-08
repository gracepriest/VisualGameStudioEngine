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

    // ------------------------------------------------------------------ helpers

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
