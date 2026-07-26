using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// File rename in the Solution Explorer (F2 / right-click → Rename). The rename logic lived in
/// the ViewModel but was never surfaced in the view (no inline editor, F2 unbound), so both
/// entry points did nothing. StartRename now prompts via IDialogService.ShowInputDialogAsync and
/// applies the rename through the existing move/project-update path.
/// </summary>
[TestFixture]
public class SolutionExplorerRenameTests
{
    private string _dir = null!;
    private BasicLangProject _project = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IDialogService> _dialogService = null!;
    private SolutionExplorerViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _project = new BasicLangProject { Name = "App", FilePath = Path.Combine(_dir, "App.blproj") };
        _project.Items.Add(new ProjectItem { Include = "main.cpp", ItemType = ProjectItemType.Compile });

        _projectService = new Mock<IProjectService>();
        _projectService.Setup(p => p.CurrentProject).Returns(_project);
        _projectService.Setup(p => p.SaveProjectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _dialogService = new Mock<IDialogService>();

        _vm = new SolutionExplorerViewModel(
            _projectService.Object, new Mock<IFileService>().Object,
            _dialogService.Object, new Mock<ISolutionService>().Object);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private TreeNode SelectFileNode(string name)
    {
        File.WriteAllText(Path.Combine(_dir, name), "// content");
        var node = new TreeNode { Name = name, FullPath = Path.Combine(_dir, name), NodeType = TreeNodeType.SourceFile };
        _vm.SelectedNode = node;
        return node;
    }

    [Test]
    public async Task StartRename_ConfirmedInDialog_MovesFileAndUpdatesProjectItem()
    {
        SelectFileNode("main.cpp");
        _dialogService
            .Setup(d => d.ShowInputDialogAsync("Rename", It.IsAny<string>(), "main.cpp"))
            .ReturnsAsync("renamed.cpp");

        string? renamedFrom = null, renamedTo = null;
        _vm.FileRenamed += (_, e) => { renamedFrom = e.OldPath; renamedTo = e.NewPath; };

        await _vm.StartRenameCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_dir, "renamed.cpp")), Is.True, "file moved to new name");
            Assert.That(File.Exists(Path.Combine(_dir, "main.cpp")), Is.False, "old file gone");
            Assert.That(_project.Items.Single().Include, Is.EqualTo("renamed.cpp"), "project item updated");
            Assert.That(renamedFrom, Does.EndWith("main.cpp"));
            Assert.That(renamedTo, Does.EndWith("renamed.cpp"));
        });
    }

    [Test]
    public async Task StartRename_CancelledDialog_DoesNothing()
    {
        SelectFileNode("main.cpp");
        _dialogService
            .Setup(d => d.ShowInputDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null); // user cancelled

        await _vm.StartRenameCommand.ExecuteAsync(null);

        Assert.That(File.Exists(Path.Combine(_dir, "main.cpp")), Is.True, "file untouched when cancelled");
        Assert.That(_project.Items.Single().Include, Is.EqualTo("main.cpp"));
    }

    [Test]
    public async Task StartRename_PrefillsDialogWithCurrentName()
    {
        SelectFileNode("main.cpp");
        string? passedDefault = null;
        _dialogService
            .Setup(d => d.ShowInputDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, def) => passedDefault = def)
            .ReturnsAsync((string?)null);

        await _vm.StartRenameCommand.ExecuteAsync(null);

        Assert.That(passedDefault, Is.EqualTo("main.cpp"), "dialog is prefilled with the current file name");
    }
}
