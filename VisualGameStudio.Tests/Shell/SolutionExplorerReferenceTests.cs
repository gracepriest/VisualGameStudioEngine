using System.Xml.Linq;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Covers SolutionExplorerViewModel.ApplyProjectReferencesAsync — the "Add Project Reference"
/// command's testable core. A reference must be written to TWO stores: the .blsln (via
/// ISolutionService.AddProjectReference + SaveSolutionAsync, driving build order) and the
/// target's .blproj (via BlprojReferenceWriter.AddReference, driving cross-project IntelliSense).
/// The dual-write test reloads the .blsln from disk with a fresh SolutionService — that is the
/// regression guard proving the view model saves via _solutionService, not the no-op
/// _projectService.SaveSolutionAsync (ProjectService.CurrentSolution is always null here because
/// the live solution is loaded through ISolutionService).
/// </summary>
[TestFixture]
public class SolutionExplorerReferenceTests
{
    private string _tempDir = null!;
    private string _slnPath = null!;
    private SolutionService _solutionService = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IDialogService> _dialogService = null!;
    private string? _lastDialogMessage;
    private SolutionExplorerViewModel _vm = null!;

    [SetUp]
    public async Task SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SolExpRefE2E_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        var git = new Mock<IGitService>();
        var templateService = new ProjectTemplateService(git.Object);

        // Seed an on-disk solution with projects A, B, C.
        var first = new CreateProjectOptions { Name = "A", SolutionType = SolutionTypes.DotNet, Template = ProjectTemplates.ConsoleApp };
        var solOpts = SolutionWizardMapper.BuildSolutionOptions("Sln", _tempDir, initGit: false, first);
        var sr = await templateService.CreateSolutionAsync(solOpts);
        Assert.That(sr.Success, Is.True, sr.Error);
        _slnPath = sr.SolutionPath!;

        foreach (var name in new[] { "B", "C" })
        {
            var loaded = await new SolutionService().LoadSolutionAsync(_slnPath);
            var opts = new CreateProjectOptions { Name = name, SolutionType = SolutionTypes.DotNet, Template = ProjectTemplates.ConsoleApp };
            SolutionWizardMapper.BuildAddToSolutionOptions(opts, loaded);
            var r = await templateService.CreateProjectAsync(opts);
            Assert.That(r.Success, Is.True, r.Error);
        }

        _solutionService = new SolutionService();
        var solution = await _solutionService.LoadSolutionAsync(_slnPath);

        _projectService = new Mock<IProjectService>();
        _dialogService = new Mock<IDialogService>();
        _lastDialogMessage = null;
        _dialogService
            .Setup(d => d.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DialogButtons>(), It.IsAny<DialogIcon>()))
            .Callback<string, string, DialogButtons, DialogIcon>((_, message, _, _) => _lastDialogMessage = message)
            .ReturnsAsync(DialogResult.Ok);

        var workspaceService = new Mock<IWorkspaceService>();

        _vm = new SolutionExplorerViewModel(
            _projectService.Object,
            new Mock<IFileService>().Object,
            _dialogService.Object,
            _solutionService,
            gitService: null,
            workspaceService: workspaceService.Object);

        _vm.LoadSolution(solution);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best-effort cleanup */ }
    }

    [Test]
    public async Task ApplyReferences_dual_writes_blsln_and_blproj()
    {
        await _vm.ApplyProjectReferencesAsync("B", new[] { "A" });

        var reloaded = await new SolutionService().LoadSolutionAsync(_slnPath);
        Assert.That(reloaded.GetProject("B")!.ProjectReferences, Does.Contain("A"));

        var bBlprojPath = reloaded.GetProject("B")!.GetFullPath(reloaded.SolutionDirectory);
        var bXml = XDocument.Load(bBlprojPath);
        Assert.That(bXml.Descendants("ProjectReference")
            .Any(e => ((string)e.Attribute("Include")!).Contains("A", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task ApplyReferences_rejects_cycle_and_surfaces_message()
    {
        await _vm.ApplyProjectReferencesAsync("B", new[] { "A" });
        await _vm.ApplyProjectReferencesAsync("A", new[] { "B" }); // would cycle

        Assert.That(_lastDialogMessage, Does.Contain("circular").IgnoreCase);

        var reloaded = await new SolutionService().LoadSolutionAsync(_slnPath);
        Assert.That(reloaded.GetProject("A")!.ProjectReferences, Does.Not.Contain("B"));
    }

    [Test]
    public void CanAddProjectReference_requires_project_node_and_two_projects()
    {
        // No selection -> false.
        _vm.SelectedNode = null;
        Assert.That(_vm.CanAddProjectReference, Is.False);

        // A non-project (solution) node selected -> false.
        var solutionNode = _vm.Nodes.First(n => n.IsSolution);
        _vm.SelectedNode = solutionNode;
        Assert.That(_vm.CanAddProjectReference, Is.False);

        // A project node selected, with >= 2 projects in the solution -> true.
        var projectNode = solutionNode.Children.First(n => n.IsProject);
        _vm.SelectedNode = projectNode;
        Assert.That(_vm.CanAddProjectReference, Is.True);
    }
}
