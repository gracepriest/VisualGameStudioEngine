using Moq;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// End-to-end proof (no UI) that the New Solution creation path — from the
/// wizard's <see cref="CreateProjectOptions"/> through
/// <see cref="SolutionWizardMapper.BuildSolutionOptions"/> and the real
/// <see cref="ProjectTemplateService.CreateSolutionAsync"/> — produces a
/// .blsln that <see cref="SolutionService.LoadSolutionAsync"/> can open,
/// with the first project scaffolded on disk and git initialized exactly
/// once at the solution level.
/// </summary>
[TestFixture]
public class NewSolutionEndToEndTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"NewSolutionE2E_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [TestCase("dotnet")]   // in-scope C# backend
    public async Task NewSolution_creates_loadable_solution_with_first_project(string backendId)
    {
        var git = new Mock<IGitService>();
        var svc = new ProjectTemplateService(git.Object);
        var first = new CreateProjectOptions
        {
            Name = "App",
            SolutionType = SolutionTypes.All.First(t => t.Id == backendId),
            Template = ProjectTemplates.ConsoleApp
        };
        var solOpts = SolutionWizardMapper.BuildSolutionOptions("MySln", _tempDir, initGit: true, first);

        var sr = await svc.CreateSolutionAsync(solOpts);
        Assert.That(sr.Success, Is.True, sr.Error);

        var solution = await new SolutionService().LoadSolutionAsync(sr.SolutionPath!);
        Assert.That(solution.Projects.Select(p => p.Name), Does.Contain("App"));
        Assert.That(File.Exists(solution.Projects[0].GetFullPath(solution.SolutionDirectory)), Is.True);
        git.Verify(g => g.InitRepositoryAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task NewSolution_initGit_false_does_not_init()
    {
        var git = new Mock<IGitService>();
        var svc = new ProjectTemplateService(git.Object);
        var first = new CreateProjectOptions { Name = "App", SolutionType = SolutionTypes.DotNet, Template = ProjectTemplates.ConsoleApp };
        var solOpts = SolutionWizardMapper.BuildSolutionOptions("NoGit", _tempDir, initGit: false, first);

        var sr = await svc.CreateSolutionAsync(solOpts);
        Assert.That(sr.Success, Is.True, sr.Error);
        git.Verify(g => g.InitRepositoryAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task NewSolution_cpp_first_project_travels_cpp_standard()
    {
        var git = new Mock<IGitService>();
        var svc = new ProjectTemplateService(git.Object);
        var first = new CreateProjectOptions
        {
            Name = "CppApp",
            SolutionType = SolutionTypes.Cpp,
            Template = ProjectTemplates.CppConsoleApp,
            CppStandard = "c++20"
        };
        var solOpts = SolutionWizardMapper.BuildSolutionOptions("CppSln", _tempDir, initGit: false, first);

        var sr = await svc.CreateSolutionAsync(solOpts);
        Assert.That(sr.Success, Is.True, sr.Error);

        var solution = await new SolutionService().LoadSolutionAsync(sr.SolutionPath!);
        Assert.That(solution.Projects.Select(p => p.Name), Does.Contain("CppApp"));
        // assert the created .blproj carries the C++ standard
        var blproj = solution.Projects[0].GetFullPath(solution.SolutionDirectory);
        var xml = await File.ReadAllTextAsync(blproj);
        Assert.That(xml, Does.Contain("<CppStandard>c++20</CppStandard>"));
    }
}
