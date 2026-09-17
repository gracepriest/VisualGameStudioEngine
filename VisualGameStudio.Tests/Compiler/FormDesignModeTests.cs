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

    // ==================================================================
    // The designer panels, and the round trip back to text
    // ==================================================================

    [Test]
    public void EnteringDesignMode_PointsThePanelsAtTheDocument()
    {
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;

        vm.ToggleDesignModeCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Toolbox.Target, Is.EqualTo(FormTarget.Web));

            // ⚠ This asserted IsEmpty "nothing is selected yet". With no control selected the grid
            // now shows the FORM's own properties, as VS does — so the panel being pointed at the
            // document is what "not empty" means here, and an empty one would be the defect.
            Assert.That(vm.PropertyGrid.IsEmpty, Is.False,
                "with nothing selected the grid shows the form's own properties");
            Assert.That(vm.PropertyGrid.SelectedControl, Is.Null, "and nothing is selected yet");
            Assert.That(vm.PropertyGrid.Header, Is.EqualTo("LoginForm"),
                "headed by the form, not by 'No selection'");
        });
    }

    [Test]
    public void APropertyGridEdit_ReachesTheText()
    {
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;
        vm.ToggleDesignModeCommand.Execute(null);

        vm.PropertyGrid.SelectedControl = vm.DesignDocument!.FindById("btnLogin");
        vm.PropertyGrid.Rows.Single(r => r.Name == "Text").StringValue = "Log in";

        Assert.That(vm.Text, Does.Contain("Log in"),
            "an edit in the property grid must be written back through the document writer");
    }

    [Test]
    public void APropertyGridEdit_KeepsTheSelection()
    {
        // ⛔⛔ Writing the document sets Text, and a Text change normally discards the parsed
        // model. Doing that here would re-parse into a FRESH object graph, so the control the user
        // has selected would no longer be in the document being drawn — the selection outline
        // would vanish and the grid would go empty on every committed edit.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;
        vm.ToggleDesignModeCommand.Execute(null);

        var button = vm.DesignDocument!.FindById("btnLogin");
        vm.PropertyGrid.SelectedControl = button;
        vm.PropertyGrid.Rows.Single(r => r.Name == "Text").StringValue = "Log in";

        Assert.Multiple(() =>
        {
            Assert.That(vm.PropertyGrid.SelectedControl, Is.SameAs(button), "still the same control");
            Assert.That(vm.PropertyGrid.Rows, Is.Not.Empty, "and the grid still shows it");
            Assert.That(vm.DesignDocument!.FindById("btnLogin"), Is.SameAs(button),
                "the canvas and the grid must still be looking at ONE object graph");
        });
    }

    [Test]
    public void APropertyGridEdit_TellsTheCanvasToRepaint()
    {
        // ⛔⛔ The grid edits FormControl.Properties IN PLACE — canvas, grid and writer share ONE
        // object graph on purpose, so the document REFERENCE never changes and Avalonia's
        // AffectsRender has nothing to notice. Without a revision counter the user renames a
        // button, the file updates, and the box on the canvas keeps the old caption.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;
        vm.ToggleDesignModeCommand.Execute(null);
        vm.PropertyGrid.SelectedControl = vm.DesignDocument!.FindById("btnLogin");

        var before = vm.DesignModelRevision;
        vm.PropertyGrid.Rows.Single(r => r.Name == "Text").StringValue = "Log in";

        Assert.That(vm.DesignModelRevision, Is.GreaterThan(before),
            "the canvas repaints off this counter; nothing else changes that it can see");
    }

    [Test]
    public void ANoOpEdit_DoesNotRepaintTheCanvas()
    {
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;
        vm.ToggleDesignModeCommand.Execute(null);
        vm.PropertyGrid.SelectedControl = vm.DesignDocument!.FindById("btnLogin");

        var before = vm.DesignModelRevision;
        var row = vm.PropertyGrid.Rows.Single(r => r.Name == "Text");
        row.StringValue = row.RawValue;

        Assert.That(vm.DesignModelRevision, Is.EqualTo(before));
    }

    [Test]
    public void AnEditInCodeView_StillResetsThePanels()
    {
        // The guard above must not swallow a real Code-view edit — that text did NOT come from
        // the designer and the cached model really is stale.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;
        vm.ToggleDesignModeCommand.Execute(null);
        vm.PropertyGrid.SelectedControl = vm.DesignDocument!.FindById("btnLogin");

        vm.Text = WebForm.Replace("btnLogin", "btnRenamed");

        Assert.Multiple(() =>
        {
            Assert.That(vm.PropertyGrid.SelectedControl, Is.Null, "the old selection is gone");
            Assert.That(vm.DesignDocument!.FindById("btnRenamed"), Is.Not.Null);
        });
    }

    [Test]
    public void TogglingDesignModeRepeatedly_DoesNotMultiplyTheWriteBack()
    {
        // ⚠ The Edited subscription is wired ONCE. Re-subscribing per toggle would write the
        // document two, three, four times over — each one re-entering the writer.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.Text = WebForm;

        for (var i = 0; i < 4; i++)
        {
            vm.ToggleDesignModeCommand.Execute(null);
        }

        vm.ToggleDesignModeCommand.Execute(null);
        vm.PropertyGrid.SelectedControl = vm.DesignDocument!.FindById("btnLogin");

        var texts = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Text)) texts.Add(vm.Text); };

        vm.PropertyGrid.Rows.Single(r => r.Name == "Text").StringValue = "Log in";

        Assert.That(texts, Has.Count.EqualTo(1), "exactly one write per edit");
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

    // ─── Opening a form document lands IN the designer ──────────────────────────────────────

    [Test]
    public void EnterDesignModeForFormDocument_PutsAFormDocumentStraightIntoTheDesigner()
    {
        // ⛔⛔ The user's report was "I can't see the form designer". Every piece of it existed and
        // worked; opening a .blform showed its raw XML with a Design button somebody had to know to
        // press. A form opens in the designer, the way a form does everywhere else.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blwebform";
        vm.SetContent(WebForm);

        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDesignMode, Is.True);
            // The panels must be wired on the way in, exactly as the toggle does it — a canvas
            // whose toolbox has no target offers no controls to drop.
            Assert.That(vm.Toolbox.Target, Is.EqualTo(FormTarget.Web));
        });
    }

    [Test]
    public void EnterDesignModeForFormDocument_LeavesASourceFileInCodeView()
    {
        var vm = NewViewModel();
        vm.FilePath = "/tmp/Program.bas";
        vm.SetContent("Sub Main()\nEnd Sub\n");

        Assert.That(vm.EnterDesignModeForFormDocument(), Is.False);
        Assert.That(vm.IsDesignMode, Is.False);
    }

    [Test]
    public void EnterDesignModeForFormDocument_LeavesARefusedDocumentInCodeView()
    {
        // ⚠ A refused document has no model, so the canvas would be blank. Code view is where the
        // user can see what is wrong with the file and fix it.
        var vm = NewViewModel();
        vm.FilePath = "/tmp/LoginForm.blform";
        vm.SetContent(WebForm); // a <WebForm> root in a .blform file — refused by name/root mismatch

        Assert.That(vm.EnterDesignModeForFormDocument(), Is.False);
        Assert.That(vm.IsDesignMode, Is.False);
    }

    [Test]
    public void OpenFile_CallsEnterDesignModeForFormDocument()
    {
        // ⛔⛔ The reachability gate. A method the open route never calls is the failure mode this
        // feature keeps producing — five pieces of it were finished, unit-tested and unreachable
        // with the suite green throughout. MainWindowViewModel is the only route that opens a file
        // from the tree, so it is the only place this can be called from.
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory.");
            return;
        }

        Assert.That(File.ReadAllText(path), Does.Contain("EnterDesignModeForFormDocument()"),
            "OpenFileAsync must put a form document into the designer as it opens it — otherwise " +
            "opening a .blform shows raw XML and the designer is one undiscoverable click away.");
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
