using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// AddExistingFileAsync used to hardcode ProjectItemType.Compile for every selected file,
/// even ones picked via the "All Files" dialog filter (e.g. a stray .txt). That means a
/// non-source file added to the project would get lexed/compiled as if it were BasicLang or
/// C++ source. It must instead classify each file by its own extension, same as the
/// paste/drop/new-file code paths already do via GetItemTypeForExtension.
/// </summary>
[TestFixture]
public class SolutionExplorerViewModelTests
{
    private string _dir = null!;
    private BasicLangProject _project = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IDialogService> _dialogService = null!;
    private SolutionExplorerViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-solexp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _project = new BasicLangProject
        {
            Name = "App",
            FilePath = Path.Combine(_dir, "App.blproj")
        };

        _projectService = new Mock<IProjectService>();
        _projectService.Setup(p => p.CurrentProject).Returns(_project);
        _projectService.Setup(p => p.SaveProjectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _dialogService = new Mock<IDialogService>();

        _vm = new SolutionExplorerViewModel(_projectService.Object, new Mock<IFileService>().Object, _dialogService.Object, new Mock<ISolutionService>().Object);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Test]
    public async Task AddExistingFileAsync_ClassifiesEachFileByItsOwnExtension_NotHardcodedCompile()
    {
        var txtPath = Path.Combine(_dir, "notes.txt");
        var cppPath = Path.Combine(_dir, "engine.cpp");
        File.WriteAllText(txtPath, "notes");
        File.WriteAllText(cppPath, "// cpp");

        // AddExistingFileAsync calls the IDialogService.ShowOpenFileDialogAsync(title, filters, allowMultiple: true)
        // extension method, which for allowMultiple:true delegates to ShowOpenFilesDialogAsync.
        _dialogService
            .Setup(d => d.ShowOpenFilesDialogAsync(It.IsAny<FileDialogOptions>()))
            .ReturnsAsync(new[] { txtPath, cppPath });

        await _vm.AddExistingFileCommand.ExecuteAsync(null);

        var txtItem = _project.Items.Single(i => i.Include == "notes.txt");
        var cppItem = _project.Items.Single(i => i.Include == "engine.cpp");

        Assert.That(txtItem.ItemType, Is.Not.EqualTo(ProjectItemType.Compile),
            "adding a stray .txt via Add Existing File must not make it a Compile item the build would try to lex");
        Assert.That(cppItem.ItemType, Is.EqualTo(ProjectItemType.Compile),
            "a .cpp file added via Add Existing File must still be classified as Compile");
    }

    /// <summary>
    /// Project nodes are structural. Open/Rename/Delete were visible on them but every command
    /// silently returned, producing dead context-menu entries. Solution-only actions must also
    /// be hidden for a standalone .blproj.
    /// </summary>
    [Test]
    public void ProjectNode_HidesTheActionsThatCannotApply()
    {
        _vm.SelectedNode = new TreeNode
        {
            Name = "App",
            NodeType = TreeNodeType.Project,
            FullPath = Path.Combine(_dir, "App.blproj")
        };

        Assert.Multiple(() =>
        {
            Assert.That(_vm.SelectedIsProject, Is.True);
            Assert.That(_vm.SelectedIsFile, Is.False, "Open is a file-only action");
            Assert.That(_vm.SelectedCanRenameOrDelete, Is.False, "Rename/Delete reject project nodes");
            Assert.That(_vm.SelectedIsSolutionProject, Is.False,
                "Add Reference / Set Startup / Remove from Solution need a solution");
        });
    }

    [Test]
    public async Task BuildProjectCommand_OnAStandaloneProject_RaisesTheBuildRequest()
    {
        var projectPath = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(projectPath, "<BasicLangProject Version=\"1.0\"></BasicLangProject>");
        _vm.SelectedNode = new TreeNode
        {
            Name = "App",
            NodeType = TreeNodeType.Project,
            FullPath = projectPath
        };

        string? requested = null;
        _vm.BuildProjectRequested += (_, path) => requested = path;

        await _vm.BuildProjectCommand.ExecuteAsync(null);

        Assert.That(requested, Is.EqualTo(projectPath),
            "the menu command must reach the real build route, not only re-open the project");
    }

    /// <summary>
    /// Project/solution nodes have a FILE FullPath but TreeNode.IsFile is false. The old
    /// OpenInExplorer only checked IsFile and then Directory.Exists, so it silently did
    /// nothing on projects and solutions. A missing target must now at least explain itself.
    /// </summary>
    [Test]
    public async Task OpenInExplorer_OnAProjectNode_ReportsAMissingTarget()
    {
        var missingProject = Path.Combine(_dir, "Missing.blproj");
        _vm.SelectedNode = new TreeNode
        {
            Name = "Missing",
            NodeType = TreeNodeType.Project,
            FullPath = missingProject
        };

        await _vm.OpenInExplorerCommand.ExecuteAsync(null);

        _dialogService.Verify(
            d => d.ShowMessageAsync(
                "Open in Explorer",
                It.Is<string>(m => m.Contains("no longer exists")),
                DialogButtons.Ok,
                DialogIcon.Warning),
            Times.Once);
    }
}
