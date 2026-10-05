using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 6 Task 6 (D-11): Delete over the WHOLE selection, through the REAL document view model — every top-level member in
/// ONE write (one undo step); a child whose container is also selected goes WITH its container, never on its own.
/// </summary>
public partial class FormPropertyGridMultiSelectTests
{
    private static string PanelDoc => MultiDoc.Replace("</Controls>",
        """
          <Panel Id="pnl" X="300" Y="100" Width="150" Height="100" TabIndex="5">
            <Button Id="kid" X="10" Y="10" Width="60" Height="23" TabIndex="0" Text="Kid"/>
          </Panel>
        </Controls>
        """);

    [Test]
    public void DeleteWithThreeSelected_RemovesAllThree_AndOneUndoRestoresThem()
    {
        var vm = OpenVm(MultiDoc, "btn", "btn2", "lbl");
        var before = vm.Text;

        vm.DeleteControlCommand.Execute(vm.Selection.Primary); // what the canvas's Delete key passes

        Assert.Multiple(() =>
        {
            Assert.That(new[] { "btn", "btn2", "lbl" }.Select(id => vm.DesignDocument!.FindById(id)), Is.All.Null, "all three gone");
            Assert.That(vm.DesignDocument!.FindById("txt"), Is.Not.Null, "the unselected one stays");
            Assert.That(vm.Selection.IsEmpty, Is.True);
        });

        vm.UndoDesignerEditCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Is.EqualTo(before), "ONE undo restores all three");
            Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False, "it was ONE step");
        });
    }

    [Test]
    public void DeleteOfAPanelAndItsOwnChild_RemovesBoth_AndOneUndoRestoresTheChildInsideThePanel()
    {
        var vm = OpenVm(PanelDoc, "kid", "pnl");
        var before = vm.Text;

        vm.DeleteControlCommand.Execute(vm.Selection.Primary);

        Assert.Multiple(() =>
        {
            Assert.That(vm.DesignDocument!.FindById("pnl"), Is.Null);
            Assert.That(vm.DesignDocument!.FindById("kid"), Is.Null, "the child went WITH its container");
        });

        vm.UndoDesignerEditCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Is.EqualTo(before));
            Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False, "ONE step");
            Assert.That(vm.DesignDocument!.FindById("pnl")!.Children.Select(c => c.Id), Is.EqualTo(new[] { "kid" }),
                "the Button is back INSIDE the Panel");
        });
    }

    [Test]
    public void TheTopLevelHelper_DropsAMemberWhoseAncestorIsSelected_InSelectionOrder()
    {
        var file = BasicLang.Forms.Serialization.FormDocumentReader.Read("GridForm.blform", PanelDoc);
        var pnl = file.Model.FindById("pnl")!;
        var kid = file.Model.FindById("kid")!;
        var btn = file.Model.FindById("btn")!;

        Assert.Multiple(() =>
        {
            Assert.That(FormSelectionTopLevel.Of(file.Model, new[] { kid, btn, pnl }), Is.EqualTo(new[] { btn, pnl }));
            Assert.That(FormSelectionTopLevel.Of(file.Model, new[] { kid, btn }), Is.EqualTo(new[] { kid, btn }),
                "a child whose container is NOT selected is top-level");
        });
    }
}
