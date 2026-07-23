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
/// End-to-end proof (no UI) that the Add Project To Solution path — from the
/// wizard's <see cref="CreateProjectOptions"/> through
/// <see cref="SolutionWizardMapper.BuildAddToSolutionOptions"/> and the real
/// <see cref="ProjectTemplateService.CreateProjectAsync"/> — registers a second
/// project into an existing .blsln without re-initializing git.
/// </summary>
[TestFixture]
public class AddProjectToSolutionEndToEndTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AddProjectE2E_{Guid.NewGuid()}");
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

    [Test]
    public async Task AddProject_registers_second_project_without_git_reinit()
    {
        var git = new Mock<IGitService>();
        var svc = new ProjectTemplateService(git.Object);

        // arrange: an on-disk solution with project "A"
        var first = new CreateProjectOptions { Name = "A", SolutionType = SolutionTypes.DotNet, Template = ProjectTemplates.ConsoleApp };
        var solOpts = SolutionWizardMapper.BuildSolutionOptions("Sln", _tempDir, initGit: true, first);
        var created = await svc.CreateSolutionAsync(solOpts);
        Assert.That(created.Success, Is.True, created.Error);

        var solSvc = new SolutionService();
        var loaded = await solSvc.LoadSolutionAsync(created.SolutionPath!);

        git.Invocations.Clear();   // ignore the solution-create git init

        // act: add project "B" via the unified mapper + CreateProjectAsync path
        var opts = new CreateProjectOptions { Name = "B", SolutionType = SolutionTypes.DotNet, Template = ProjectTemplates.ConsoleApp };
        SolutionWizardMapper.BuildAddToSolutionOptions(opts, loaded);
        var r = await svc.CreateProjectAsync(opts);
        Assert.That(r.Success, Is.True, r.Error);

        // assert: B registered in the reloaded .blsln, its .blproj exists, git NOT re-inited
        git.Verify(g => g.InitRepositoryAsync(It.IsAny<string>()), Times.Never);
        var reloaded = await new SolutionService().LoadSolutionAsync(loaded.FilePath);
        Assert.That(reloaded.Projects.Select(p => p.Name), Does.Contain("B"));
        var bProj = reloaded.GetProject("B")!;
        Assert.That(File.Exists(bProj.GetFullPath(reloaded.SolutionDirectory)), Is.True);
    }
}
