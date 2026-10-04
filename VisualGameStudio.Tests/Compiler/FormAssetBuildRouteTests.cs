using System.Collections.Concurrent;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⛔ Slice 4 Task 9 — the build copy and BL8036 through BOTH entry points (CLAUDE.md: the IDE does NOT delegate to the CLI
/// process; <c>BuildService</c> mirrors it step for step). Each route, each backend that carries forms: the real
/// <c>BasicLang.exe build</c>, and the real <c>BuildService.BuildProjectAsync</c>.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class FormAssetBuildRouteTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-assetroute-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    /// <summary>A web project: the scaffolded form with a PictureBox naming Resources/logo.png, and that file.</summary>
    private void WebProject(string root)
    {
        var scaffold = FormScaffolder.Create("Pic", FormTarget.Web, FormLayoutKind.Grid);
        File.WriteAllText(P(scaffold.DocumentFileName), scaffold.DocumentText.Replace("<Controls />",
            "<Controls>\n    <PictureBox Id=\"pic\" Col=\"0\" Row=\"0\" TabIndex=\"0\" Image=\"Resources/logo.png\" />\n  </Controls>")
            .Replace("<Controls/>",
            "<Controls>\n    <PictureBox Id=\"pic\" Col=\"0\" Row=\"0\" TabIndex=\"0\" Image=\"Resources/logo.png\" />\n  </Controls>"));
        Assert.That(File.ReadAllText(P(scaffold.DocumentFileName)), Does.Contain("Resources/logo.png"),
            "precondition: the form references the image");
        File.WriteAllText(P(scaffold.CodeFileName), scaffold.CodeText);
        File.WriteAllText(P("Main.bas"), "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");
        Directory.CreateDirectory(P("Resources"));
        File.WriteAllBytes(P(Path.Combine("Resources", "logo.png")), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllText(P("App.blproj"), root == "cli"
            ? """
              <BasicLangProject Version="1.0">
                <PropertyGroup>
                  <ProjectName>App</ProjectName>
                  <OutputType>Exe</OutputType>
                  <TargetBackend>JavaScript</TargetBackend>
                  <StartupForm>Pic</StartupForm>
                </PropertyGroup>
              </BasicLangProject>
              """
            : """
              <Project>
                <PropertyGroup>
                  <ProjectName>App</ProjectName>
                  <TargetBackend>JavaScript</TargetBackend>
                </PropertyGroup>
                <ItemGroup>
                  <Compile Include="Main.bas" />
                  <Compile Include="Pic.bas" />
                  <Compile Include="Pic.blwebform" />
                </ItemGroup>
              </Project>
              """);
    }

    /// <summary>
    /// A C# project carrying a WinForms form DOCUMENT (no code-behind: the copy step reads the documents, and a console
    /// Main keeps the C# build itself independent of WinForms references). The real window run is Task 11's.
    /// </summary>
    private void CSharpProject(string root)
    {
        File.WriteAllText(P("Pic.blform"), """
            <Form Name="Pic" Version="1" Width="400" Height="300" Text="Pic">
              <Controls>
                <PictureBox Id="pic" X="8" Y="8" Width="50" Height="50" TabIndex="0" Image="Resources/logo.png" />
              </Controls>
            </Form>
            """);
        File.WriteAllText(P("Main.bas"), "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");
        Directory.CreateDirectory(P("Resources"));
        File.WriteAllBytes(P(Path.Combine("Resources", "logo.png")), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllText(P("App.blproj"), root == "cli"
            ? """
              <BasicLangProject Version="1.0">
                <PropertyGroup>
                  <ProjectName>App</ProjectName>
                  <OutputType>Exe</OutputType>
                  <TargetBackend>CSharp</TargetBackend>
                </PropertyGroup>
              </BasicLangProject>
              """
            : """
              <Project>
                <PropertyGroup>
                  <ProjectName>App</ProjectName>
                  <TargetBackend>CSharp</TargetBackend>
                </PropertyGroup>
                <ItemGroup>
                  <Compile Include="Main.bas" />
                  <Compile Include="Pic.blform" />
                </ItemGroup>
              </Project>
              """);
    }

    private (int Exit, string Out) CliBuild()
    {
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", P("App.blproj") }, _dir, timeoutMs: 300_000);
        return (exit, stdout + "\n" + stderr);
    }

    // ==================================================================
    // CLI
    // ==================================================================

    [Test]
    public void Cli_WebProject_CopiesTheImageBesideThePage_AndAMissingOneIsBL8036_ExitZero()
    {
        WebProject("cli");

        var (exit, output) = CliBuild();
        var copy = Path.Combine(_dir, "bin", "Debug", "net8.0", "Resources", "logo.png");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, output);
            Assert.That(File.Exists(copy), Is.True, $"the image beside the page.\n{output}");
        });

        File.Delete(P(Path.Combine("Resources", "logo.png")));
        File.Delete(copy);
        (exit, output) = CliBuild();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, "a missing image never fails the build");
            Assert.That(output, Does.Contain("Warning: BL8036:").And.Contain("'Pic.pic.Image'"));
        });
    }

    [Test]
    public void Cli_CSharpProject_CopiesTheImageBesideTheExe()
    {
        if (!CliTestHarness.DotnetOnPath())
        {
            Assert.Ignore("the dotnet SDK is not on PATH");
        }

        CSharpProject("cli");

        var (exit, output) = CliBuild();
        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, output);
            Assert.That(File.Exists(Path.Combine(outDir, "Resources", "logo.png")), Is.True, output);
            Assert.That(Directory.GetFiles(outDir, "App.dll"), Is.Not.Empty, "beside the built program");
        });
    }

    // ==================================================================
    // IDE (BuildService) — ⚠ the IDE's project lists its form documents as items (the IDE writes them), and its build reads
    // them from that list; the CLI reads them from its own glob.
    // ==================================================================

    private async Task<BuildResult> IdeBuild()
    {
        var project = await new ProjectSerializer().LoadAsync(P("App.blproj"));
        return await new BuildService(new SilentOutput()).BuildProjectAsync(project);
    }

    [Test]
    public async Task Ide_WebProject_CopiesTheImage_AndAMissingOneIsABL8036WarningOnTheErrorList()
    {
        WebProject("ide");

        var result = await IdeBuild();
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            Assert.That(File.Exists(Path.Combine(result.OutputPath!, "Resources", "logo.png")), Is.True, result.OutputPath);
        });

        File.Delete(P(Path.Combine("Resources", "logo.png")));
        result = await IdeBuild();
        var warning = result.Diagnostics.SingleOrDefault(d => d.Id == "BL8036");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "a missing image never fails the build");
            Assert.That(warning, Is.Not.Null, string.Join("\n", result.Diagnostics.Select(d => d.Id + " " + d.Message)));
            Assert.That(warning!.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(warning.FilePath, Does.EndWith("Pic.blwebform"), "names the form document");
        });
    }

    [Test]
    public async Task Ide_CSharpProject_CopiesTheImageBesideTheExe()
    {
        if (!CliTestHarness.DotnetOnPath())
        {
            Assert.Ignore("the dotnet SDK is not on PATH");
        }

        CSharpProject("ide");

        var result = await IdeBuild();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            Assert.That(File.Exists(Path.Combine(result.OutputPath!, "Resources", "logo.png")), Is.True, result.OutputPath);
        });
    }

    private sealed class SilentOutput : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();
        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
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
