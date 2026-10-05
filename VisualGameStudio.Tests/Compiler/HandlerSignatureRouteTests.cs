using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BasicLang.Compiler;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using RecordingOutput = VisualGameStudio.Tests.Services.JavaScriptProjectBuildTests.RecordingOutput;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// §4.8 (portable-controls Task 10) on the library's real shape: <c>Control</c> (declares Click) and <c>Button</c>
/// (Inherits Control) in one file, the form wiring <c>AddHandler b.Click</c> on a <c>Button</c> field in ANOTHER —
/// through every build route (the project route in both file orders, the CLI, the IDE).
/// </summary>
[TestFixture]
[Category("Integration")]
public class HandlerSignatureRouteTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-hsig-" + Path.GetRandomFileName())).FullName;
        File.WriteAllText(Path.Combine(_dir, "Lib.bas"),
            "Public Class EventArgs2\nEnd Class\n" +
            "Public Class Control\n Public Event Click(sender As Object, e As EventArgs2)\nEnd Class\n" +
            "Public Class Button\n Inherits Control\nEnd Class\n");
        File.WriteAllText(Path.Combine(_dir, "Form1.bas"),
            "Public Class Form1\n Private b As Button\n Public Sub New()\n  b = New Button()\n  AddHandler b.Click, AddressOf H\n End Sub\n" +
            " Private Sub H(n As Integer)\n End Sub\nEnd Class\nModule Program\n Sub Main()\n  Dim f As New Form1()\n End Sub\nEnd Module\n");
        File.WriteAllText(Path.Combine(_dir, "Site.blproj"),
            "<Project>\n  <PropertyGroup>\n    <ProjectName>Site</ProjectName>\n    <TargetBackend>JavaScript</TargetBackend>\n" +
            "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Lib.bas\" />\n    <Compile Include=\"Form1.bas\" />\n  </ItemGroup>\n</Project>\n");
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    [TestCase(false)]
    [TestCase(true)]
    public void TheProjectRoute_BothFileOrders(bool reversed)
    {
        var files = new[] { Path.Combine(_dir, "Lib.bas"), Path.Combine(_dir, "Form1.bas") };
        if (reversed) files = files.Reverse().ToArray();
        var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(files);
        Assert.That(r.AllErrors.Select(e => e.Message), Has.Some.Contains("HandlerSignatureMismatch"));
    }

    [Test]
    public async Task TheCli()
    {
        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "build", "Site.blproj");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Not.Zero);
            Assert.That(stdout + stderr, Does.Contain("HandlerSignatureMismatch"));
        });
    }

    [Test]
    public async Task TheIde()
    {
        var project = await new ProjectSerializer().LoadAsync(Path.Combine(_dir, "Site.blproj"));
        var output = new RecordingOutput();
        var result = await new BuildService(output) { CurrentConfiguration = new BuildConfiguration { Name = "Debug" } }
            .BuildProjectAsync(project);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, output.Dump());
            Assert.That(result.Diagnostics.Select(d => d.Message), Has.Some.Contains("HandlerSignatureMismatch"), output.Dump());
        });
    }
}
