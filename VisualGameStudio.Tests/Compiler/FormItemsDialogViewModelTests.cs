using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Slice 4 Task 7: VS's String Collection Editor's view model, and the Items row that opens it.</summary>
[TestFixture]
public class FormItemsDialogViewModelTests
{
    [TestCase("Smith, John\r\nBeta")]
    [TestCase("Smith, John\nBeta")]
    [TestCase("Smith, John\rBeta")]
    [TestCase("Smith, John\r\n\r\n   \r\nBeta\r\n")]
    public void EveryLineBreak_GivesTheSameItems(string typed)
    {
        var vm = new FormItemsDialogViewModel(null) { Text = typed };

        Assert.Multiple(() =>
        {
            Assert.That(vm.Items, Is.EqualTo(new[] { "Smith, John", "Beta" }), "no CR left on any item, blanks dropped");
            Assert.That(vm.Accept(), Is.EqualTo("Smith, John\nBeta"), "the model's ONE encoding");
        });
    }

    [Test]
    public void TheBox_StartsWithOneItemPerLine_AndCancelProducesNothing()
    {
        var vm = new FormItemsDialogViewModel("Alpha\nBeta");

        Assert.That(vm.Text, Is.EqualTo("Alpha" + Environment.NewLine + "Beta"));
        vm.Text = "changed";
        vm.Cancel();
        Assert.That(vm.Result, Is.Null);
    }

    private static (FormFile File, FormPropertyGridViewModel Grid, FormPropertyRow Items) Open(string comboAttributes)
    {
        var file = FormDocumentReader.Read("F.blform",
            $"<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\"><Controls>" +
            $"<ComboBox Id=\"cmb\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"{comboAttributes}/></Controls></Form>");
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("cmb");
        return (file, grid, grid.Rows.Single(r => r.Name == "Items"));
    }

    [Test]
    public void TheItemsRow_IsTheCollectionEditor_NeverAOneLineTextBox()
    {
        var (_, _, items) = Open("");

        Assert.Multiple(() =>
        {
            Assert.That(items.IsCollectionEditor, Is.True);
            Assert.That(items.IsTextBox, Is.False);
            Assert.That(items.CollectionSummary, Is.EqualTo("(Collection)"));
        });
    }

    [Test]
    public void AnEmptyResult_Resets_NeverWritesAnEmptyList()
    {
        var (file, grid, items) = Open(" Items=\"Alpha, Beta\"");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        items.ApplyItems("");

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("cmb")!.Properties.ContainsKey("Items"), Is.False, "Reset removes it");
            Assert.That(FormDocumentWriter.Write(file), Does.Not.Contain("Items"));
            Assert.That(edits, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplyingTheSameList_IsANoOp_AndANewOneWritesOnce()
    {
        var (file, grid, items) = Open(" Items=\"Alpha, Beta\"");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        items.ApplyItems("Alpha\nBeta");
        Assert.That(edits, Is.Zero, "the same list again changes nothing");

        items.ApplyItems("Smith, John\nBeta");
        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(FormDocumentWriter.Write(file), Does.Contain("<Item>Smith, John</Item>"));
        });
    }
}
