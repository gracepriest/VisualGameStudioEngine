using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class SolutionWizardMapperTests
{
    [Test]
    public void BuildSolutionOptions_carries_args_and_single_initial_project()
    {
        var first = new CreateProjectOptions { Name = "App", SolutionType = SolutionTypes.Llvm };
        var s = SolutionWizardMapper.BuildSolutionOptions("MySln", @"C:\src", initGit: false, first);

        Assert.That(s.Name, Is.EqualTo("MySln"));
        Assert.That(s.Location, Is.EqualTo(@"C:\src"));
        Assert.That(s.CreateGitRepository, Is.False);
        Assert.That(s.SolutionType, Is.EqualTo(SolutionTypes.Llvm));   // preserves LLVM
        Assert.That(s.InitialProjects, Has.Count.EqualTo(1));
        Assert.That(s.InitialProjects[0], Is.SameAs(first));
    }

    [Test]
    public void BuildAddToSolutionOptions_pins_location_and_disables_folder_and_git()
    {
        var sln = new BasicLangSolution { FilePath = @"C:\src\S\S.blsln", SolutionName = "S" };
        var opts = new CreateProjectOptions { Name = "Lib", Location = @"C:\wrong" };

        var a = SolutionWizardMapper.BuildAddToSolutionOptions(opts, sln);

        Assert.That(a.Location, Is.EqualTo(sln.SolutionDirectory));
        Assert.That(a.CreateSolutionFolder, Is.False);
        Assert.That(a.CreateGitRepository, Is.False);
        Assert.That(a.AddToExistingSolution, Is.True);
        Assert.That(a.ExistingSolutionPath, Is.EqualTo(sln.FilePath));
    }

    [Test]
    public void ToProjectResult_maps_first_project_path()
    {
        var sr = new SolutionCreationResult { Success = true, SolutionPath = "s.blsln",
            ProjectPaths = { "p.blproj" }, FilesToOpen = { "a.bas" } };
        var r = SolutionWizardMapper.ToProjectResult(sr);
        Assert.That(r.Success, Is.True);
        Assert.That(r.ProjectPath, Is.EqualTo("p.blproj"));
        Assert.That(r.SolutionPath, Is.EqualTo("s.blsln"));
        Assert.That(r.FilesToOpen, Is.EquivalentTo(new[] { "a.bas" }));
    }

    [Test]
    public void ToProjectResult_empty_projects_is_failure_not_silent_success()
    {
        var sr = new SolutionCreationResult { Success = true, SolutionPath = "s.blsln" };
        var r = SolutionWizardMapper.ToProjectResult(sr);
        Assert.That(r.Success, Is.False);
        Assert.That(r.Error, Is.Not.Null.And.Contains("first project"));
        Assert.That(r.ProjectPath, Is.Null);
    }

    [Test]
    public void ToProjectResult_propagates_failure()
    {
        var r = SolutionWizardMapper.ToProjectResult(new SolutionCreationResult { Success = false, Error = "boom" });
        Assert.That(r.Success, Is.False);
        Assert.That(r.Error, Is.EqualTo("boom"));
    }
}
