using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// In-place (inline) file rename in the Solution Explorer. StartRename puts the selected node
/// into edit mode (IsEditing + EditingName); the view swaps the name TextBlock for a TextBox.
/// ConfirmRename applies the move/project-update using EditingName; CancelRename discards.
/// </summary>
[TestFixture]
public class SolutionExplorerRenameTests
{
    private string _dir = null!;
    private BasicLangProject _project = null!;
    private Mock<IProjectService> _projectService = null!;
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

        _vm = new SolutionExplorerViewModel(
            _projectService.Object, new Mock<IFileService>().Object,
            new Mock<IDialogService>().Object, new Mock<ISolutionService>().Object);
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
    public void StartRename_EntersEditModeSeededWithCurrentName()
    {
        var node = SelectFileNode("main.cpp");

        _vm.StartRenameCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(node.IsEditing, Is.True);
            Assert.That(node.EditingName, Is.EqualTo("main.cpp"));
        });
    }

    [Test]
    public void StartRename_OnProjectOrSolutionNode_DoesNothing()
    {
        var proj = new TreeNode { Name = "App", NodeType = TreeNodeType.Project };
        _vm.SelectedNode = proj;

        _vm.StartRenameCommand.Execute(null);

        Assert.That(proj.IsEditing, Is.False);
    }

    [Test]
    public async Task ConfirmRename_MovesFileUpdatesProjectItemAndFiresEvent()
    {
        var node = SelectFileNode("main.cpp");
        _vm.StartRenameCommand.Execute(null);
        node.EditingName = "renamed.cpp";           // as if typed into the editor

        string? from = null, to = null;
        _vm.FileRenamed += (_, e) => { from = e.OldPath; to = e.NewPath; };

        await _vm.ConfirmRenameCommand.ExecuteAsync(node);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_dir, "renamed.cpp")), Is.True);
            Assert.That(File.Exists(Path.Combine(_dir, "main.cpp")), Is.False);
            Assert.That(_project.Items.Single().Include, Is.EqualTo("renamed.cpp"));
            Assert.That(node.IsEditing, Is.False, "edit mode ends after commit");
            Assert.That(from, Does.EndWith("main.cpp"));
            Assert.That(to, Does.EndWith("renamed.cpp"));
        });
    }

    [Test]
    public async Task ConfirmRename_UnchangedName_LeavesFileAndEndsEditMode()
    {
        var node = SelectFileNode("main.cpp");
        _vm.StartRenameCommand.Execute(null);       // EditingName stays "main.cpp"

        await _vm.ConfirmRenameCommand.ExecuteAsync(node);

        Assert.That(File.Exists(Path.Combine(_dir, "main.cpp")), Is.True);
        Assert.That(node.IsEditing, Is.False);
    }

    [Test]
    public void CancelRename_DiscardsWithoutMoving()
    {
        var node = SelectFileNode("main.cpp");
        _vm.StartRenameCommand.Execute(null);
        node.EditingName = "renamed.cpp";

        _vm.CancelRenameCommand.Execute(node);

        Assert.Multiple(() =>
        {
            Assert.That(node.IsEditing, Is.False);
            Assert.That(File.Exists(Path.Combine(_dir, "main.cpp")), Is.True, "cancel does not move the file");
            Assert.That(File.Exists(Path.Combine(_dir, "renamed.cpp")), Is.False);
        });
    }
}
