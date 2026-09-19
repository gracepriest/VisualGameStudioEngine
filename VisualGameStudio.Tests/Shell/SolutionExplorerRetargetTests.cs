using System.ComponentModel;
using System.Text.RegularExpressions;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// "Retarget Form…" in the Solution Explorer — Task 21's entry point INSIDE the IDE.
///
/// <para>⛔⛔ Every test drives the generated COMMAND and the last one reads the AXAML, for the
/// reason <c>SolutionExplorerNewFormTests</c> gives: five pieces of this feature have shipped
/// complete, unit-tested and unreachable, and a <c>[RelayCommand]</c> no menu binds is as
/// unreachable as a method nothing calls.</para>
///
/// <para>⚠ The retargeted pair is written into a folder the user CHOOSES and is NOT added to the
/// current project. A form's document and code-behind pair by base name, and its class name is the
/// form's name — so <c>LoginForm.bas</c> for the web beside <c>LoginForm.bas</c> for WinForms in one
/// project is a duplicate class. The pair belongs in the other target's project, one "Existing
/// File…" away.</para>
/// </summary>
[TestFixture]
public class SolutionExplorerRetargetTests
{
    private string _dir = null!;
    private BasicLangProject _project = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IDialogService> _dialogService = null!;
    private Mock<IEventAggregator> _events = null!;
    private SolutionExplorerViewModel _vm = null!;

    private const string WinFormsLogin = """
        <Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
          <Controls>
            <Label  Id="lblUser"  Text="User" X="20" Y="20" Width="60" Height="23" TabIndex="0"/>
            <Button Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="1">
              <Bind Event="Click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
        </Form>
        """;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-retarget-ide-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _project = new BasicLangProject { Name = "App", FilePath = Path.Combine(_dir, "App.blproj"), UseWindowsForms = true };

        _projectService = new Mock<IProjectService>();
        _projectService.Setup(p => p.CurrentProject).Returns(_project);
        _projectService.Setup(p => p.SaveProjectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _dialogService = new Mock<IDialogService>();
        _events = new Mock<IEventAggregator>();

        _vm = new SolutionExplorerViewModel(
            _projectService.Object,
            new Mock<IFileService>().Object,
            _dialogService.Object,
            new Mock<ISolutionService>().Object,
            eventAggregator: _events.Object);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Out => Path.Combine(_dir, "web");

    private string SelectDocument(string name, string xml)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, xml);
        _vm.SelectedNode = new TreeNode { Name = name, NodeType = TreeNodeType.SourceFile, FullPath = path };
        return path;
    }

    private void ChooseFolder(string? folder) =>
        _dialogService.Setup(d => d.ShowFolderDialogAsync(It.IsAny<FolderDialogOptions>())).ReturnsAsync(folder);

    [Test]
    public async Task RetargetFormCommand_WritesThePairIntoTheChosenFolder_AndOpensTheDocument()
    {
        SelectDocument("LoginForm.blform", WinFormsLogin);
        ChooseFolder(Out);

        var opened = new List<string>();
        _vm.FileOpenRequested += (_, path) => opened.Add(path);

        await _vm.RetargetFormCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(Out, "LoginForm.blwebform")), Is.True, "the document");
            Assert.That(File.Exists(Path.Combine(Out, "LoginForm.bas")), Is.True, "its code-behind");
            Assert.That(File.ReadAllText(Path.Combine(Out, "LoginForm.bas")),
                Does.Contain("Private Sub btnLogin_Click(e As DomEvent)"), "with the handler stub");

            Assert.That(opened, Is.EqualTo(new[] { Path.Combine(Out, "LoginForm.blwebform") }),
                "the new DOCUMENT opens — in the designer, which is a mode on its editor");

            Assert.That(_project.Items, Is.Empty,
                "NOT added to this project: its class name collides with the source form's");
        });

        _projectService.Verify(p => p.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task RetargetFormCommand_PublishesEveryLoss_ToTheErrorList_AgainstTheNewCodeBehind()
    {
        SelectDocument("LoginForm.blform", WinFormsLogin);
        ChooseFolder(Out);

        await _vm.RetargetFormCommand.ExecuteAsync(null);

        // Keyed on the new code-behind, like every designer finding — the save path republishes on
        // the .bas, so a finding filed anywhere else would be a phantom for the rest of the session.
        _events.Verify(e => e.Publish(It.Is<DesignerDiagnosticsEvent>(ev =>
                ev.FilePath == Path.Combine(Out, "LoginForm.bas") &&
                ev.Diagnostics.Any(d => d.Id == "BL8025" && d.Message.Contains("'btnLogin'")) &&
                ev.Diagnostics.All(d => d.Severity == DiagnosticSeverity.Warning))),
            Times.Once,
            "the loss at the hard edge must reach the Error List, per control, as warnings");
    }

    [Test]
    public async Task RetargetFormCommand_TellsTheUserWhatHappened_InOneDialog()
    {
        SelectDocument("LoginForm.blform", WinFormsLogin);
        ChooseFolder(Out);

        await _vm.RetargetFormCommand.ExecuteAsync(null);

        _dialogService.Verify(d => d.ShowMessageAsync(
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains("LoginForm.blwebform") && m.Contains("not converted")),
                It.IsAny<DialogButtons>(), It.IsAny<DialogIcon>()),
            Times.Once,
            "the user must be told where the pair went and that the original code-behind was left alone");
    }

    [Test]
    public async Task RetargetFormCommand_RefusesWhenEitherHalfExistsInTheFolder_AndTouchesNothing()
    {
        SelectDocument("LoginForm.blform", WinFormsLogin);
        Directory.CreateDirectory(Out);
        var existing = Path.Combine(Out, "LoginForm.bas");
        File.WriteAllText(existing, "' mine\n");
        ChooseFolder(Out);

        await _vm.RetargetFormCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(existing), Is.EqualTo("' mine\n"), "never overwritten");
            Assert.That(File.Exists(Path.Combine(Out, "LoginForm.blwebform")), Is.False, "half a pair is worse than none");
        });

        _dialogService.Verify(
            d => d.ShowMessageAsync("Error", It.Is<string>(m => m.Contains("LoginForm.bas")), It.IsAny<DialogButtons>(), It.IsAny<DialogIcon>()),
            Times.Once, "the refusal names the file in the way");
    }

    [Test]
    public async Task RetargetFormCommand_CancelledAtTheFolderDialog_WritesNothing()
    {
        SelectDocument("LoginForm.blform", WinFormsLogin);
        ChooseFolder(null);

        await _vm.RetargetFormCommand.ExecuteAsync(null);

        Assert.That(Directory.Exists(Out), Is.False);
        _events.Verify(e => e.Publish(It.IsAny<DesignerDiagnosticsEvent>()), Times.Never);
    }

    [Test]
    public async Task RetargetFormCommand_OnARefusedDocument_ShowsTheReason_AndWritesNothing()
    {
        SelectDocument("LoginForm.blform", """
            <Form Name="LoginForm" Version="1">
              <Controls>
                <Button Id="b" X="8" Y="8" Width="75" Height="23" TabIndex="0"/>
                <Button Id="b" X="8" Y="40" Width="75" Height="23" TabIndex="1"/>
              </Controls>
            </Form>
            """);
        ChooseFolder(Out);

        await _vm.RetargetFormCommand.ExecuteAsync(null);

        Assert.That(Directory.Exists(Out), Is.False, "a document the designer refuses cannot be retargeted");
        _dialogService.Verify(
            d => d.ShowMessageAsync(It.IsAny<string>(), It.Is<string>(m => m.Contains("BL8017")), It.IsAny<DialogButtons>(), It.IsAny<DialogIcon>()),
            Times.Once, "the reader's own refusal is the reason shown");
    }

    [Test]
    public void SelectedIsFormDocument_IsTrueForAFormDocument_FalseForAnythingElse_AndNotifies()
    {
        var notified = new List<string>();
        _vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? "");

        _vm.SelectedNode = new TreeNode { Name = "LoginForm.blform", NodeType = TreeNodeType.SourceFile, FullPath = Path.Combine(_dir, "LoginForm.blform") };
        var form = _vm.SelectedIsFormDocument;

        _vm.SelectedNode = new TreeNode { Name = "LoginForm.blwebform", NodeType = TreeNodeType.SourceFile, FullPath = Path.Combine(_dir, "LoginForm.blwebform") };
        var webForm = _vm.SelectedIsFormDocument;

        _vm.SelectedNode = new TreeNode { Name = "Main.bas", NodeType = TreeNodeType.SourceFile, FullPath = Path.Combine(_dir, "Main.bas") };
        var source = _vm.SelectedIsFormDocument;

        _vm.SelectedNode = new TreeNode { Name = "App", NodeType = TreeNodeType.Project, FullPath = _dir };
        var project = _vm.SelectedIsFormDocument;

        Assert.Multiple(() =>
        {
            Assert.That(form, Is.True);
            Assert.That(webForm, Is.True);
            Assert.That(source, Is.False, "a .bas is the code-behind, not the document");
            Assert.That(project, Is.False);
            Assert.That(notified.Count(n => n == nameof(SolutionExplorerViewModel.SelectedIsFormDocument)), Is.EqualTo(4),
                "the menu item's IsVisible binding only follows the selection if the change is raised");
        });
    }

    [Test]
    public void SolutionExplorerView_BindsRetargetFormCommand_OnTheContextMenu()
    {
        // ⛔⛔ The reachability gate.
        var axaml = FindRepoFile("VisualGameStudio.Shell", "Views", "Panels", "SolutionExplorerView.axaml");
        if (axaml == null)
        {
            Assert.Ignore("SolutionExplorerView.axaml not found from the test base directory.");
            return;
        }

        var text = File.ReadAllText(axaml);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("RetargetFormCommand"),
                "the context menu must bind RetargetFormCommand — without it the retarget has no entry point in the IDE.");
            Assert.That(Regex.IsMatch(text, "Header=\"Retarget Form"), Is.True, "labelled so a user can find it");
            Assert.That(text, Does.Contain("SelectedIsFormDocument"),
                "and shown only on a form document, or it is an item that does nothing on every other node");
        });
    }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
