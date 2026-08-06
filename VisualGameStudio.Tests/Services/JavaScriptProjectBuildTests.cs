using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Plan task 28 — the IDE half of the JavaScript backend, through the SECOND entry point.
///
/// <para>CLAUDE.md requires both: "the IDE build delegates to the CLI engine; a fix verified
/// only through the test helper can still break via the IDE or the CLI." Here that is more
/// than a rule of thumb — the IDE does NOT share the CLI's backend dispatch. BuildService
/// hand-rolls its own switch whose <c>default:</c> is C#, and <c>GetBackendId</c> has the same
/// shape, so a JavaScript project could reach codegen and still emit C# from a missing arm.</para>
///
/// <para>Before this task the IDE could not even REPRESENT a JavaScript project:
/// <c>TargetBackend</c> had no such member, so the serializer's <c>Enum.TryParse</c> failed and
/// the value silently reverted to CSharp.</para>
/// </summary>
[TestFixture]
public class JavaScriptProjectBuildTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BasicLang_IdeJs_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a locked temp dir must not fail a passing test */ }
    }

    // ---------------------------------------------------------------- representation

    [TestCase("javascript")]
    [TestCase("JavaScript")]
    [TestCase("js")]
    public void ResolveDefaultBackend_AcceptsJavaScript(string configured)
        => Assert.That(ProjectService.ResolveDefaultBackend(configured),
            Is.EqualTo(TargetBackend.JavaScript));

    /// <summary>
    /// The serializer parses by NAME, so the enum member is the whole fix — without it the
    /// value silently became CSharp and the project built the wrong language with no warning.
    /// </summary>
    [Test]
    public async Task Project_RoundTripsTheJavaScriptBackend()
    {
        var path = Path.Combine(_dir, "Site.blproj");
        await File.WriteAllTextAsync(path,
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>Site</ProjectName>\n" +
            "    <TargetBackend>JavaScript</TargetBackend>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");

        var project = await new ProjectSerializer().LoadAsync(path);

        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.JavaScript));
    }

    /// <summary>A JavaScript project is NOT a native build — it must not route to CppProjectBuilder.</summary>
    [Test]
    public void JavaScriptProject_IsNotANativeBuild()
        => Assert.That(new BasicLangProject { TargetBackend = TargetBackend.JavaScript }.IsNativeBuild,
            Is.False);

    // ---------------------------------------------------------------- building

    private async Task<BasicLangProject> CreateProject()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "Sub Main()\nConsole.WriteLine(1)\nEnd Sub\n");

        var path = Path.Combine(_dir, "Site.blproj");
        await File.WriteAllTextAsync(path,
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>Site</ProjectName>\n" +
            "    <TargetBackend>JavaScript</TargetBackend>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"Main.bas\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");

        return await new ProjectSerializer().LoadAsync(path);
    }

    private static async Task<(BuildResult result, RecordingOutput output)> Build(BasicLangProject project)
    {
        var output = new RecordingOutput();
        var result = await new BuildService(output).BuildProjectAsync(project);
        return (result, output);
    }

    /// <summary>THE REGRESSION. A missing arm in either switch silently emits C# instead.</summary>
    [Test]
    public async Task Build_EmitsJavaScript_NotCSharp()
    {
        var (result, output) = await Build(await CreateProject());

        Assert.That(result.Success, Is.True, output.Dump());
        Assert.That(result.GeneratedFileName, Does.EndWith(".js"), output.Dump());
        Assert.That(result.GeneratedCode, Does.Not.Contain("using System"),
            "a JavaScript project must never emit C#");
    }

    [Test]
    public async Task Build_WritesTheHarnessBesideTheScript()
    {
        var (result, output) = await Build(await CreateProject());

        Assert.That(result.OutputPath, Is.Not.Null.And.Not.Empty, output.Dump());
        var harness = Path.Combine(result.OutputPath!, "index.html");
        Assert.That(File.Exists(harness), Is.True, output.Dump());
        Assert.That(await File.ReadAllTextAsync(harness),
            Does.Contain("src=\"" + result.GeneratedFileName + "\""));
    }

    /// <summary>
    /// Output lands in bin/… while the .bas stays in the project directory, so a browser
    /// resolving `sources` against the map's own URL would 404 on every one.
    /// </summary>
    [Test]
    public async Task Build_WritesASourceMapWithInlinedSources()
    {
        var (result, output) = await Build(await CreateProject());

        var mapPath = Path.Combine(result.OutputPath!, result.GeneratedFileName + ".map");
        Assert.That(File.Exists(mapPath), Is.True, output.Dump());

        var map = JsonDocument.Parse(await File.ReadAllTextAsync(mapPath)).RootElement;
        Assert.That(map.GetProperty("version").GetInt32(), Is.EqualTo(3));
        Assert.That(map.GetProperty("sourcesContent")[0].GetString(),
            Does.Contain("Console.WriteLine"));
    }

    /// <summary>
    /// A JS build has NO executable, and that is not a failure. The other run paths guard on
    /// File.Exists(ExecutablePath); reusing that field to carry a URL would be a type lie.
    /// </summary>
    [Test]
    public async Task Build_ReportsNoExecutable_ButStillSucceeds()
    {
        var (result, _) = await Build(await CreateProject());

        Assert.That(result.Success, Is.True);
        Assert.That(result.ExecutablePath, Is.Null.Or.Empty);
    }

    /// <summary>The built directory must be servable end to end — this is what F5 does.</summary>
    [Test]
    public async Task Build_ProducesADirectoryThePreviewServerCanServe()
    {
        var (result, _) = await Build(await CreateProject());

        using var server = new WebPreviewServer();
        var url = server.Start(result.OutputPath!);

        using var http = new System.Net.Http.HttpClient();
        var page = await http.GetAsync(url);
        var script = await http.GetAsync(url + result.GeneratedFileName);

        Assert.That(page.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        Assert.That(page.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(script.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        Assert.That(await script.Content.ReadAsStringAsync(), Does.Contain("console.log"));
    }

    /// <summary>
    /// A BL70xx capability rejection must fail the build with a diagnostic, not escape as an
    /// unhandled exception out of the IDE's build pipeline.
    /// </summary>
    [Test]
    public async Task Build_CapabilityRejection_IsADiagnosticNotACrash()
    {
        var project = await CreateProject();
        // ByRef is BL7002 — refused by design on this backend.
        await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
            "Sub Bump(ByRef n As Integer)\nn = n + 1\nEnd Sub\n" +
            "Sub Main()\nDim x As Integer = 1\nBump(x)\nEnd Sub\n");

        var (result, output) = await Build(project);

        Assert.That(result.Success, Is.False, output.Dump());
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True,
            output.Dump());
    }

    private sealed class RecordingOutput : IOutputService
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
