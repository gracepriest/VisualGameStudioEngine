using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Plan task 27 — the static server that hosts a built JavaScript project for F5.
///
/// <para><b>Not marked Integration, deliberately.</b> Binding a loopback port is already an
/// accepted fast-subset operation in this suite — FileDownloaderTests spins up a real
/// HttpListener across eight tests with no category at all. Gating these would hide them from
/// the run that actually gets used.</para>
/// </summary>
[TestFixture]
public class WebPreviewServerTests
{
    private string _root;
    private WebPreviewServer _server;
    private static HttpClient Http;

    [OneTimeSetUp]
    public void OneTimeSetUp() => Http = new HttpClient();

    [OneTimeTearDown]
    public void OneTimeTearDown() => Http?.Dispose();

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "BasicLang_Preview_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!DOCTYPE html><html></html>");
        File.WriteAllText(Path.Combine(_root, "app.js"), "console.log(1);");
        File.WriteAllText(Path.Combine(_root, "app.js.map"), "{\"version\":3}");
    }

    [TearDown]
    public void TearDown()
    {
        _server?.Dispose();
        _server = null;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* a locked temp dir must not fail a passing test */ }
    }

    private string Start()
    {
        _server = new WebPreviewServer();
        return _server.Start(_root);
    }

    private static async Task<HttpResponseMessage> Get(string url) =>
        await Http.GetAsync(url);

    // ---------------------------------------------------------------- serving

    [Test]
    public async Task ServesIndexHtml()
    {
        var response = await Get(Start() + "index.html");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
    }

    [Test]
    public async Task ServesTheScriptAsJavaScript()
    {
        var response = await Get(Start() + "app.js");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/javascript"));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("console.log(1);"));
    }

    [Test]
    public async Task ServesTheSourceMapAsJson()
    {
        var response = await Get(Start() + "app.js.map");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
    }

    /// <summary>The root must serve the harness, or the URL F5 opens shows a 404.</summary>
    [Test]
    public async Task ServesIndexHtmlAtTheRoot()
    {
        var response = await Get(Start());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
    }

    [Test]
    public async Task MissingFileIs404()
        => Assert.That((await Get(Start() + "missing.js")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));

    /// <summary>
    /// WASM streaming instantiation REFUSES a wrong MIME type — it is the one content type
    /// where getting this wrong breaks the feature rather than merely looking untidy. The
    /// WASM work reuses this server.
    /// </summary>
    [Test]
    public async Task ServesWasmWithTheStreamingMimeType()
    {
        File.WriteAllBytes(Path.Combine(_root, "mod.wasm"), new byte[] { 0, 0x61, 0x73, 0x6D });

        var response = await Get(Start() + "mod.wasm");

        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/wasm"));
    }

    [Test]
    public async Task UnknownExtensionFallsBackToOctetStream()
    {
        File.WriteAllText(Path.Combine(_root, "data.zzz"), "x");

        var response = await Get(Start() + "data.zzz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
    }

    // ---------------------------------------------------------------- containment

    /// <summary>
    /// THE traversal guard. A served directory is not a sandbox by itself — without an
    /// explicit containment check, `..` walks straight out of it and the server hands over
    /// whatever the IDE process can read.
    /// </summary>
    [Test]
    public async Task RejectsParentTraversal()
    {
        var secret = Path.Combine(Path.GetDirectoryName(_root)!, "outside_" + Path.GetRandomFileName() + ".txt");
        File.WriteAllText(secret, "SECRET");
        try
        {
            var response = await Get(Start() + "../" + Path.GetFileName(secret));

            Assert.That(response.StatusCode,
                Is.EqualTo(HttpStatusCode.NotFound).Or.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("SECRET"));
        }
        finally { try { File.Delete(secret); } catch { } }
    }

    /// <summary>
    /// A URL-encoded `..` decodes AFTER the client sends it, so a guard that only inspects
    /// the raw string misses this while the file system does not.
    /// </summary>
    [Test]
    public async Task RejectsEncodedTraversal()
    {
        var response = await Get(Start() + "%2e%2e%2fsomewhere.txt");

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound).Or.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// A sibling directory whose name merely STARTS WITH the root's is not inside it.
    /// A naive `fullPath.StartsWith(root)` says otherwise.
    /// </summary>
    [Test]
    public void ContainmentIsByDirectoryBoundary_NotStringPrefix()
    {
        var sibling = _root + "_evil";
        Directory.CreateDirectory(sibling);
        try
        {
            File.WriteAllText(Path.Combine(sibling, "x.txt"), "SECRET");

            Assert.That(WebPreviewServer.IsUnder(_root, Path.Combine(sibling, "x.txt")), Is.False);
            Assert.That(WebPreviewServer.IsUnder(_root, Path.Combine(_root, "x.txt")), Is.True);
        }
        finally { try { Directory.Delete(sibling, true); } catch { } }
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Loopback only. A preview server must never be reachable from the network.</summary>
    [Test]
    public void BindsLoopbackOnly()
    {
        var url = Start();

        Assert.That(new Uri(url).Host, Is.EqualTo("127.0.0.1").Or.EqualTo("localhost"));
    }

    [Test]
    public void TwoServersGetDifferentPorts()
    {
        var first = Start();
        using var second = new WebPreviewServer();
        var secondUrl = second.Start(_root);

        Assert.That(secondUrl, Is.Not.EqualTo(first));
    }

    /// <summary>Restarting on a new root must not leave the old one served.</summary>
    [Test]
    public async Task RestartingRebindsAndServesTheNewRoot()
    {
        Start();
        var other = Path.Combine(Path.GetTempPath(), "BasicLang_Preview2_" + Path.GetRandomFileName());
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "app.js"), "console.log(2);");
        try
        {
            var url = _server.Start(other);

            Assert.That(await (await Get(url + "app.js")).Content.ReadAsStringAsync(),
                Is.EqualTo("console.log(2);"));
        }
        finally { try { Directory.Delete(other, true); } catch { } }
    }

    [Test]
    public void StopIsIdempotent()
    {
        Start();

        Assert.DoesNotThrow(() => { _server.Stop(); _server.Stop(); _server.Dispose(); });
    }

    [Test]
    public async Task StoppedServerNoLongerAnswers()
    {
        var url = Start();
        _server.Stop();

        Assert.That(async () => await Get(url + "app.js"), Throws.TypeOf<HttpRequestException>());
        await Task.CompletedTask;
    }

    [Test]
    public void StartRejectsAMissingRoot()
        => Assert.That(() => new WebPreviewServer().Start(Path.Combine(_root, "nope")),
            Throws.TypeOf<DirectoryNotFoundException>());

    [Test]
    public void UrlIsNullBeforeStart()
        => Assert.That(new WebPreviewServer().Url, Is.Null);
}
