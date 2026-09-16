using System.Text.RegularExpressions;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// "Add ▸ New Form" — the ENTRY POINT to the whole form designer.
///
/// <para>⛔⛔ <c>AddNewFormAsync</c> was written complete and careful and then left with no
/// <c>[RelayCommand]</c>, no menu item and no caller: the fifth piece of this feature to be
/// finished, unit-tested and unreachable, with a green suite throughout (the others:
/// <c>RegionWriter.Write</c>, <c>FormAssetEmitter.DispatchSource</c>,
/// <c>JavaScriptEmitter.Emit(forms:)</c>, the default project shape). Every test here therefore
/// drives the generated COMMAND rather than the method, and the last one reads the AXAML, because
/// a command nothing binds is exactly as unreachable as a method nothing calls.</para>
/// </summary>
[TestFixture]
public class SolutionExplorerNewFormTests
{
    private string _dir = null!;
    private BasicLangProject _project = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IDialogService> _dialogService = null!;
    private SolutionExplorerViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-newform-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>PromptAsync is an extension over ShowInputDialogAsync — mock the interface method.</summary>
    private void AnswerThePrompt(string? name) =>
        _dialogService
            .Setup(d => d.ShowInputDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(name);

    [Test]
    public async Task AddNewFormCommand_OnAWinFormsProject_WritesTheBlformPairAndListsBoth()
    {
        _project.UseWindowsForms = true;
        AnswerThePrompt("LoginForm");

        var opened = new List<string>();
        _vm.FileOpenRequested += (_, path) => opened.Add(path);

        await _vm.AddNewFormCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_dir, "LoginForm.blform")), Is.True,
                "the .blform document must be written to disk");
            Assert.That(File.Exists(Path.Combine(_dir, "LoginForm.bas")), Is.True,
                "the code-behind must be written to disk");

            Assert.That(_project.Items.Any(i => i.Include == "LoginForm.blform"), Is.True,
                "the document must be listed in the project, or the build cannot find it");
            Assert.That(_project.Items.Any(i => i.Include == "LoginForm.bas"), Is.True,
                "the code-behind must be listed in the project");

            // ⛔⛔ The DOCUMENT, and only it. The design view is a mode on the form document's own
            // editor, so opening the code-behind — which is what this did before it was wired up —
            // creates a form and leaves the user looking at Basic source with no designer
            // anywhere: precisely the "I can't see the form designer" report. Raising both would
            // not fix it either, because the open handler is `async void` and the two reads race
            // for the active tab.
            Assert.That(opened, Is.EqualTo(new[] { Path.Combine(_dir, "LoginForm.blform") }),
                "the form document must be the file that opens");
        });

        _projectService.Verify(p => p.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Once,
            "the project file must be saved, or the new items are lost on restart");
    }

    [Test]
    public async Task AddNewFormCommand_OnANonWinFormsProject_WritesTheWebPair()
    {
        _project.UseWindowsForms = null;
        AnswerThePrompt("Signup");

        await _vm.AddNewFormCommand.ExecuteAsync(null);

        // ⛔ The TARGET comes from the project. Scaffolding a WinForms document here would produce
        // a form with no runtime to run on; scaffolding a web document into a WinForms project
        // produces a code-behind with no `Inherits Form`, which cannot compile.
        Assert.That(File.Exists(Path.Combine(_dir, "Signup.blwebform")), Is.True,
            "a project that is not UseWindowsForms must get the web document");
        Assert.That(File.Exists(Path.Combine(_dir, "Signup.blform")), Is.False);
    }

    [Test]
    public async Task AddNewFormCommand_MaterialisesTheGlobBeforeListingItsFirstCompileItem()
    {
        // ⛔⛔ The compiler globs **/*.bas ONLY while a project has no explicit <Compile> item. The
        // first one flips it to the explicit list, so adding a form to a globbed project would drop
        // every other source from the build with no diagnostic.
        File.WriteAllText(Path.Combine(_dir, "Main.bas"), "Sub Main()\nEnd Sub\n");
        File.WriteAllText(Path.Combine(_dir, "Helper.cls"), "Option Public\n");

        _project.UseWindowsForms = true;
        AnswerThePrompt("LoginForm");

        await _vm.AddNewFormCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_project.Items.Any(i => i.Include == "Main.bas"), Is.True,
                "the pre-existing source must be listed explicitly before the form is added");
            Assert.That(_project.Items.Any(i => i.Include == "Helper.cls"), Is.True);
        });
    }

    [Test]
    public async Task AddNewFormCommand_RefusesANameThatCannotBeAClassName()
    {
        // A form name becomes a CLASS name. An underscore falls out of the compiler's .NET-type
        // heuristic and every Me.<inherited member> becomes a hard error, which reads as a compiler
        // bug rather than a naming rule — so it is refused at the prompt.
        _project.UseWindowsForms = true;
        AnswerThePrompt("Login_Form");

        await _vm.AddNewFormCommand.ExecuteAsync(null);

        Assert.That(Directory.GetFiles(_dir), Is.Empty,
            "an illegal form name must write nothing at all");
        _dialogService.Verify(
            d => d.ShowMessageAsync("Error", It.IsAny<string>(), It.IsAny<DialogButtons>(), It.IsAny<DialogIcon>()),
            Times.Once,
            "the user must be told why the name was refused");
    }

    [Test]
    public async Task AddNewFormCommand_CancelledAtThePrompt_WritesNothing()
    {
        _project.UseWindowsForms = true;
        AnswerThePrompt(null);

        await _vm.AddNewFormCommand.ExecuteAsync(null);

        Assert.That(Directory.GetFiles(_dir), Is.Empty);
        _projectService.Verify(p => p.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void SolutionExplorerView_BindsAddNewFormCommand_InTheAddSubmenu()
    {
        // ⛔⛔ The reachability gate. A [RelayCommand] no menu binds is as unreachable as the
        // un-attributed method was — and the suite would stay just as green about it.
        var axaml = FindRepoFile("VisualGameStudio.Shell", "Views", "Panels", "SolutionExplorerView.axaml");
        if (axaml == null)
        {
            Assert.Ignore("SolutionExplorerView.axaml not found from the test base directory.");
            return;
        }

        var text = File.ReadAllText(axaml);

        Assert.That(text, Does.Contain("AddNewFormCommand"),
            "the Add submenu must bind AddNewFormCommand — without it the form designer has no " +
            "entry point in the IDE at all.");
        Assert.That(Regex.IsMatch(text, "Header=\"New Form"), Is.True,
            "the menu item must be labelled so a user can find it.");
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
