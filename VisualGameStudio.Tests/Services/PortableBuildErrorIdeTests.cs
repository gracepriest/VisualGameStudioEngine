using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Task 34 — the IDE build route is <see cref="BuildService"/>, which drives the same
/// <c>BasicCompiler</c> as the CLI. These rows pin that the four named errors survive the
/// service's diagnostic mapping, not just the compiler tests.
/// </summary>
[TestFixture]
public class PortableBuildErrorIdeTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "bl-t34-ide-" + Path.GetRandomFileName())).FullName;
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private async Task<BuildResult> BuildAsync(string backend, string source)
    {
        File.WriteAllText(Path.Combine(_dir, "Main.bas"), source);
        var projectPath = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(projectPath,
            "<BasicLangProject Version=\"1.0\">\n" +
            "  <PropertyGroup>\n" +
            "    <ProjectName>App</ProjectName>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            $"    <TargetBackend>{backend}</TargetBackend>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"Main.bas\" />\n" +
            "  </ItemGroup>\n" +
            "</BasicLangProject>\n");

        var project = await new ProjectSerializer().LoadAsync(projectPath);
        var output = new Mock<IOutputService>();
        var service = new BuildService(output.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        return await service.BuildProjectAsync(project, cts.Token);
    }

    private static string Errors(BuildResult result) =>
        string.Join(" | ", result.Errors.Select(e => e.Message));

    [Test]
    public async Task IdeBuild_ReportsWebUnavailableMember()
    {
        var result = await BuildAsync("JavaScript",
            "Using System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Inherits Form\n" +
            "    Private btn As Button\n" +
            "    Public Sub Test()\n" +
            "        btn.ShowDialog()\n" +
            "    End Sub\n" +
            "End Class\n");

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("WebUnavailableMember: 'Button.ShowDialog'"));
    }

    [Test]
    public async Task IdeBuild_ReportsRuntimeControlCreation()
    {
        var result = await BuildAsync("JavaScript",
            "Using System.Windows.Forms\n\n" +
            "Module Program\n" +
            "    Sub Main()\n" +
            "        Dim b As New Button()\n" +
            "    End Sub\n" +
            "End Module\n");

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("RuntimeControlCreation"));
    }

    [Test]
    public async Task IdeBuild_ReportsMixedCodeStyle()
    {
        var result = await BuildAsync("JavaScript",
            "Using System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    ' <vgs:designer region=\"controls\" form=\"LoginForm.blwebform\" hash=\"abcd\">\n" +
            "    Private btn As Button\n" +
            "    ' </vgs:designer>\n" +
            "End Class\n");

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("MixedCodeStyle"));
    }

    [Test]
    public async Task IdeBuild_ReportsElementOnDesktop()
    {
        var result = await BuildAsync("CSharp",
            "Using System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Inherits Form\n" +
            "    Private btn As Button\n" +
            "    Public Sub Test()\n" +
            "        Dim e = btn.Element\n" +
            "    End Sub\n" +
            "End Class\n");

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("ElementOnDesktop"));
    }
}
