using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 7: the Design|Code mode on the existing document view model.
///
/// <para>⛔ A MODE, not a second document type. Two document types for one file would mean two
/// tabs, two undo stacks, and two things that both believe they own the text — and D1 puts the
/// designer's output inside the user's own file precisely so there is one of each.</para>
///
/// <para>⚠ This covers the model behind the Design view. The canvas's APPEARANCE is not covered
/// here or anywhere — it is checked by running the IDE, as the plan says. What IS covered, in
/// <c>FormCanvasTransformTests</c>, is the mapping that decides where clicks land, because that one
/// fails without any visual symptom.</para>
/// </summary>
[TestFixture]
public class FormDesignModeTests
{
    private static CodeEditorDocumentViewModel NewViewModel() =>
        new(new Mock<IFileService>().Object, new Mock<IEventAggregator>().Object);

    private const string WebForm = """
        <WebForm Name="LoginForm" Version="1">
          <Controls>
            <Button Id="btnLogin" Text="Sign in" Col="0" Row="0" TabIndex="0"/>
          </Controls>
        </WebForm>
        """;

    [TestCase("LoginForm.blwebform", true)]
    [TestCase("MainForm.blform", true)]
    [TestCase("Program.bas", false)]
    [TestCase("notes.txt", false)]
    public void IsFormDocument_DecidesWhetherTheDesignToggleIsOffered(string fileName, bool expected)
    {
        var vm = NewViewModel();
        vm.FilePath = "/tmp/" + fileName;

        Assert.That(vm.IsFormDocument, Is.EqualTo(expected));
    }

    [Test]
    public void IsFormDocument_IsFalse_ForAnUnsavedDocument()
    {
        // A brand-new tab has no path. Offering a Design toggle there would open a canvas on a
        // document that does not exist yet.
        Assert.That(NewViewModel().IsFormDocument, Is.False);
    }

    [Test]
    public void DesignDocument_ReadsTheModelFromTheCurrentText()
    {
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;

        var document = vm.DesignDocument;

        Assert.That(document, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(document!.Name, Is.EqualTo("LoginForm"));
            Assert.That(document.Controls.Single().Id, Is.EqualTo("btnLogin"));
        });
    }

    [Test]
    public void DesignDocument_FollowsAnEditMadeInCodeView()
    {
        // ⚠ Read on demand, never cached. The text is the truth and the user can edit it in Code
        // view at any moment; a cached model would draw a form that no longer matches the file.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;
        Assert.That(vm.DesignDocument!.Controls, Has.Count.EqualTo(1), "sanity");

        vm.Text = WebForm.Replace("""<Button Id="btnLogin" Text="Sign in" Col="0" Row="0" TabIndex="0"/>""", "");

        Assert.That(vm.DesignDocument!.Controls, Is.Empty,
            "the canvas must follow the text, not a snapshot of it");
    }

    [Test]
    public void DesignDocument_IsNull_ForARefusedDocument()
    {
        // ⛔ A refusal returns a deliberately UNPOPULATED model. Drawing it would tell the user
        // their form has no controls, which is worse than drawing nothing — it looks like data
        // loss that has already happened.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = """<WebForm Name="F" Version="99"><Controls/></WebForm>""";

        Assert.That(vm.DesignDocument, Is.Null);
    }

    [Test]
    public void DesignDocument_IsNull_WhenTheFileIsNotAFormDocument()
    {
        var vm = NewViewModel();
        vm.FilePath = "/tmp/Program.bas";
        vm.Text = "Sub Main()\nEnd Sub\n";

        Assert.That(vm.DesignDocument, Is.Null);
    }

    [Test]
    public void DesignDocument_DoesNotThrow_OnTextThatIsNotXmlAtAll()
    {
        // Half-typed content is the normal state of a file being edited. A canvas that throws
        // takes the tab down with it.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";

        Assert.DoesNotThrow(() => { vm.Text = "<WebForm"; _ = vm.DesignDocument; });
        Assert.That(vm.DesignDocument, Is.Null);
    }

    [Test]
    public void ToggleDesignMode_FlipsTheMode()
    {
        var vm = NewViewModel();
        Assert.That(vm.IsDesignMode, Is.False, "documents open in Code view");

        vm.ToggleDesignModeCommand.Execute(null);
        Assert.That(vm.IsDesignMode, Is.True);

        vm.ToggleDesignModeCommand.Execute(null);
        Assert.That(vm.IsDesignMode, Is.False);
    }

    [Test]
    public void EditingTheText_RaisesAChangeForTheCanvas()
    {
        // The canvas redraws off a property-changed notification. Without one, an edit in Code
        // view leaves a stale picture.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Text = WebForm;

        Assert.That(raised, Does.Contain(nameof(CodeEditorDocumentViewModel.DesignDocument)));
    }
}
