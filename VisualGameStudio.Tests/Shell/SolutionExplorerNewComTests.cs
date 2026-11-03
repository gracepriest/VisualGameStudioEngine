using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// "Add ▸ NewCOM Component..." creates the same three-file shape as the NewCOM project
/// template, but inside an existing C++ project. The menu item must reach a generated command
/// and the files must be listed in the project, not merely written to disk.
/// </summary>
[TestFixture]
public class SolutionExplorerNewComTests
{
    private string _dir = null!;
    private BasicLangProject _project = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IDialogService> _dialogService = null!;
    private SolutionExplorerViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-newcom-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _project = new BasicLangProject
        {
            Name = "Native",
            FilePath = Path.Combine(_dir, "Native.blproj"),
            Language = ProjectLanguage.Cpp
        };

        _projectService = new Mock<IProjectService>();
        _projectService.Setup(p => p.CurrentProject).Returns(_project);
        _projectService.Setup(p => p.SaveProjectAsync(It.IsAny<System.Threading.CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dialogService = new Mock<IDialogService>();
        _vm = new SolutionExplorerViewModel(
            _projectService.Object,
            new Mock<IFileService>().Object,
            _dialogService.Object,
            new Mock<ISolutionService>().Object);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void AnswerThePrompt(string? name) =>
        _dialogService
            .Setup(d => d.ShowInputDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(name);

    [Test]
    public async Task AddNewComComponentCommand_WritesTheThreeFiles_AndListsThem()
    {
        AnswerThePrompt("Greeter");
        var opened = new System.Collections.Generic.List<string>();
        _vm.FileOpenRequested += (_, path) => opened.Add(path);

        await _vm.AddNewComComponentCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_dir, "Greeter.h")), Is.True);
            Assert.That(File.Exists(Path.Combine(_dir, "Greeter.cpp")), Is.True);
            Assert.That(File.Exists(Path.Combine(_dir, "Greeter.bas")), Is.True);

            Assert.That(File.ReadAllText(Path.Combine(_dir, "Greeter.h")),
                Does.Contain("NEWCOM_COMPONENT(Greeter)"));
            Assert.That(File.ReadAllText(Path.Combine(_dir, "Greeter.bas")),
                Does.Contain("Class Greeter"));

            Assert.That(_project.Items.Select(i => i.Include), Is.EquivalentTo(new[]
            {
                "Greeter.h", "Greeter.cpp", "Greeter.bas"
            }));

            Assert.That(opened, Is.EqualTo(new[] { Path.Combine(_dir, "Greeter.h") }),
                "the component header is the thing the user needs to edit");
        });

        _projectService.Verify(p => p.SaveProjectAsync(It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task AddNewComComponentCommand_OnANonCppProject_ExplainsAndWritesNothing()
    {
        _project.Language = ProjectLanguage.BasicLang;
        AnswerThePrompt("Greeter");

        await _vm.AddNewComComponentCommand.ExecuteAsync(null);

        Assert.That(File.Exists(Path.Combine(_dir, "Greeter.h")), Is.False);
        _dialogService.Verify(
            d => d.ShowMessageAsync(
                "NewCOM Component",
                It.Is<string>(m => m.Contains("native C++")),
                It.IsAny<DialogButtons>(),
                It.IsAny<DialogIcon>()),
            Times.Once);
    }

    [Test]
    public void SolutionExplorerView_BindsNewComComponentCommand_OnTheAddMenu()
    {
        var axaml = FindRepoFile(
            "VisualGameStudio.Shell", "Views", "Panels", "SolutionExplorerView.axaml");
        if (axaml == null)
        {
            Assert.Ignore("SolutionExplorerView.axaml not found from the test base directory.");
            return;
        }

        var text = File.ReadAllText(axaml);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("AddNewComComponentCommand"));
            Assert.That(text, Does.Contain("NewCOM Component..."));
        });
    }

    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
