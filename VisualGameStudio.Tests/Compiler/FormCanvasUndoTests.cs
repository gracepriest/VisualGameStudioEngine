using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Undo for designer edits — and the text-sync defect that made it impossible.
///
/// <para>⛔⛔ The designer and the code editor keep the document in TWO places: the view model's
/// <c>Text</c>, and the <c>TextDocument</c> the editor is bound to and owns the undo stack for.
/// Designer edits used to write only the first. Nothing reconciled them, so the editor still held
/// the pre-edit text — and the FIRST keystroke in Code view pushed that stale text back over every
/// drop, move and resize the user had made. The fix is that a designer edit goes through the same
/// <c>ReplaceContent</c> the refactoring tools use, which writes the editor's document and puts the
/// change on its undo stack. Undo then falls out of it, rather than needing a second undo stack
/// that could disagree with the text.</para>
/// </summary>
[TestFixture]
public class FormCanvasUndoTests
{
    private const string Dir = "/proj/";

    private const string LoginForm = """
        <Form Name="LoginForm" Version="1" Width="800" Height="450" Text="LoginForm">
          <Controls>
            <Button Id="btnLogin" Text="Sign in" X="10" Y="10" Width="75" Height="23" TabIndex="0"/>
          </Controls>
        </Form>
        """;

    private static CodeEditorDocumentViewModel Open(
        string documentText = LoginForm, string fileName = "LoginForm.blform")
    {
        var files = new Mock<IFileService>();
        var vm = new CodeEditorDocumentViewModel(files.Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + fileName
        };

        // SetContent is what a real open does: it seeds BOTH stores and clears the undo stack, so
        // the file as loaded is never itself an undoable step.
        vm.SetContent(documentText);
        return vm;
    }

    // ==================================================================
    // The defect underneath: one document, two stores
    // ==================================================================

    [Test]
    public void ADesignerEdit_ReachesTheEditorsDocument()
    {
        var vm = Open();

        vm.PlaceControl("Button", 120, 64);

        Assert.That(vm.TextDocument.Text, Is.EqualTo(vm.Text),
            "the editor's document and the view model's text are the same document");
        Assert.That(vm.TextDocument.Text, Does.Contain("Button1"));
    }

    [Test]
    public void TypingInCodeViewAfterADrop_DoesNotDiscardTheDrop()
    {
        // ⛔⛔ THE bug. The editor syncs its text back on every keystroke. While the designer wrote
        // only to Text, that sync pushed the editor's STALE copy over the designer's work: drop
        // three buttons, switch to Code, type one character, and all three are gone — with no error
        // and nothing to undo, because as far as the editor was concerned they never existed.
        var vm = Open();
        vm.PlaceControl("Button", 120, 64);

        // What the view does on every keystroke in Code view.
        vm.UpdateTextFromEditor(vm.TextDocument.Text);

        Assert.That(vm.Text, Does.Contain("Button1"));
    }

    // ==================================================================
    // Undo
    // ==================================================================

    [Test]
    public void UndoingADrop_TakesTheControlBackOut()
    {
        var vm = Open();
        vm.PlaceControl("Button", 120, 64);
        Assume.That(vm.Text, Does.Contain("Button1"));

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Not.Contain("Button1"));
            Assert.That(vm.Text, Is.EqualTo(LoginForm), "back to exactly the document we started from");
        });
    }

    [Test]
    public void UndoingADrop_TakesItOffTheCanvasToo()
    {
        // ⛔ The canvas draws DesignDocument. If undo restored the text but left the model alone,
        // the control would stay on screen and come back on the next save — the file and the
        // picture disagreeing, which is the one thing a designer must never do.
        var vm = Open();
        vm.PlaceControl("Button", 120, 64);

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.That(vm.DesignDocument!.FindById("Button1"), Is.Null);
    }

    [Test]
    public void UndoingAMove_PutsTheControlBack()
    {
        var vm = Open();
        var button = vm.DesignDocument!.FindById("btnLogin")!;
        FormGeometryEdit.MoveTo(vm.DesignDocument!, button, 200, 150);
        vm.CommitGeometryCommand.Execute(null);

        vm.UndoDesignerEditCommand.Execute(null);

        var pixel = (PixelGeometry)vm.DesignDocument!.FindById("btnLogin")!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(10));
            Assert.That(pixel.Y, Is.EqualTo(10));
        });
    }

    [Test]
    public void OneDrag_IsOneUndo()
    {
        // ⛔⛔ A drag mutates the model on every pointer move and commits ONCE on release. If each
        // move committed, undoing one nudge of a control would take dozens of presses — the undo
        // stack would be unusable and the user would learn not to trust it.
        var vm = Open();
        var button = vm.DesignDocument!.FindById("btnLogin")!;

        // Ten pointer-moves worth of dragging, then one release.
        for (var i = 1; i <= 10; i++)
        {
            FormGeometryEdit.MoveTo(vm.DesignDocument!, button, 10 + (i * 5), 10 + (i * 5));
        }

        vm.CommitGeometryCommand.Execute(null);
        vm.UndoDesignerEditCommand.Execute(null);

        Assert.That(vm.Text, Is.EqualTo(LoginForm), "one press is enough to undo the whole drag");
    }

    [Test]
    public void TwoEdits_UndoOneAtATime()
    {
        var vm = Open();
        vm.PlaceControl("Button", 120, 64);
        vm.PlaceControl("Label", 20, 200);

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Not.Contain("Label1"), "the second edit is gone");
            Assert.That(vm.Text, Does.Contain("Button1"), "the first is not");
        });
    }

    [Test]
    public void RedoPutsBackWhatUndoTookAway()
    {
        var vm = Open();
        vm.PlaceControl("Button", 120, 64);
        vm.UndoDesignerEditCommand.Execute(null);

        vm.RedoDesignerEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Contain("Button1"));
            Assert.That(vm.DesignDocument!.FindById("Button1"), Is.Not.Null, "and it is back on the canvas");
        });
    }

    [Test]
    public void AReparentingDrag_NestsTheControlInTheFile()
    {
        // ⛔⛔ The caller test for reparenting. The writer is structure-preserving and works one
        // container at a time: a reparent is a REMOVE from the old container's element list and an
        // INSERT into the new one's. If it could only do one of those, the control would end up in
        // both places or neither, and the model and the file would disagree from then on.
        var vm = Open("""
            <Form Name="LoginForm" Version="1" Width="800" Height="450" Text="LoginForm">
              <Controls>
                <Panel Id="pnlSide" X="50" Y="40" Width="200" Height="150" TabIndex="0"/>
                <Button Id="btnLogin" Text="Sign in" X="10" Y="300" Width="75" Height="23" TabIndex="1"/>
              </Controls>
            </Form>
            """);

        var button = vm.DesignDocument!.FindById("btnLogin")!;
        FormGeometryEdit.MoveToForm(vm.DesignDocument!, button, 100, 90);
        vm.CommitGeometryCommand.Execute(null);

        var reread = BasicLang.Forms.Serialization.FormDocumentReader.Read(vm.FilePath!, vm.Text);
        var panel = reread.Model.FindById("pnlSide")!;
        Assert.Multiple(() =>
        {
            Assert.That(panel.Children.Select(c => c.Id), Does.Contain("btnLogin"),
                "the Button is inside the Panel element now");
            Assert.That(reread.Model.Controls.Select(c => c.Id), Does.Not.Contain("btnLogin"),
                "and no longer a sibling of it");
        });

        var pixel = (PixelGeometry)panel.Children.Single(c => c.Id == "btnLogin").Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(50), "re-based into the Panel's coordinate space");
            Assert.That(pixel.Y, Is.EqualTo(50));
        });
    }

    [Test]
    public void UndoingAReparent_PutsTheControlBackWhereItLived()
    {
        const string before = """
            <Form Name="LoginForm" Version="1" Width="800" Height="450" Text="LoginForm">
              <Controls>
                <Panel Id="pnlSide" X="50" Y="40" Width="200" Height="150" TabIndex="0"/>
                <Button Id="btnLogin" Text="Sign in" X="10" Y="300" Width="75" Height="23" TabIndex="1"/>
              </Controls>
            </Form>
            """;
        var vm = Open(before);

        var button = vm.DesignDocument!.FindById("btnLogin")!;
        FormGeometryEdit.MoveToForm(vm.DesignDocument!, button, 100, 90);
        vm.CommitGeometryCommand.Execute(null);

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.That(vm.Text, Is.EqualTo(before), "one press puts the whole reparent back");
        Assert.That(vm.DesignDocument!.FindById("pnlSide")!.Children, Is.Empty,
            "and the canvas shows it outside the Panel again");
    }

    [Test]
    public void AWebControlDraggedToAnotherCell_ReachesTheFile()
    {
        // ⛔⛔ The caller test for a cell move. The canvas mutates Col/Row as the pointer crosses
        // cells and commits ONCE on release — the same shape as a WinForms drag, against a
        // completely different geometry.
        var vm = Open("""
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid" Cols="1fr,1fr" Rows="1fr,1fr" Gap="0px"/>
              <Controls>
                <Button Id="btnLogin" Text="Sign in" Col="0" Row="0" TabIndex="0"/>
              </Controls>
            </WebForm>
            """, "LoginForm.blwebform");

        var button = vm.DesignDocument!.FindById("btnLogin")!;
        FormGeometryEdit.MoveToCell(vm.DesignDocument!, button, 300, 250);
        vm.CommitGeometryCommand.Execute(null);

        var reread = BasicLang.Forms.Serialization.FormDocumentReader.Read(vm.FilePath!, vm.Text);
        var grid = (GridGeometry)reread.Model.FindById("btnLogin")!.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(grid.Col, Is.EqualTo(1));
            Assert.That(grid.Row, Is.EqualTo(1));
        });
    }

    [Test]
    public void UndoingAWebCellMove_PutsTheControlBackInItsCell()
    {
        const string before = """
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid" Cols="1fr,1fr" Rows="1fr,1fr" Gap="0px"/>
              <Controls>
                <Button Id="btnLogin" Text="Sign in" Col="0" Row="0" TabIndex="0"/>
              </Controls>
            </WebForm>
            """;
        var vm = Open(before, "LoginForm.blwebform");

        var button = vm.DesignDocument!.FindById("btnLogin")!;
        FormGeometryEdit.MoveToCell(vm.DesignDocument!, button, 300, 250);
        vm.CommitGeometryCommand.Execute(null);

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.That(vm.Text, Is.EqualTo(before), "one press puts the cell back");
    }

    [Test]
    public void UndoWithNothingToUndo_DoesNothing()
    {
        // The file as opened is not an undoable step — SetContent clears the stack. Undo here must
        // not empty the document, which is what an unguarded Undo on a fresh stack can do.
        var vm = Open();

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.That(vm.Text, Is.EqualTo(LoginForm));
    }

    [Test]
    public void ANoOpCommit_DoesNotAddAnUndoStep()
    {
        // A click that selects without moving still ends in a release and still commits. If that
        // pushed an undo step, the user would press Ctrl+Z and watch nothing happen — twice.
        var vm = Open();
        vm.PlaceControl("Button", 120, 64);

        vm.CommitGeometryCommand.Execute(null);   // the model asked for nothing
        vm.UndoDesignerEditCommand.Execute(null);

        Assert.That(vm.Text, Is.EqualTo(LoginForm), "one press still undoes the drop");
    }
}
