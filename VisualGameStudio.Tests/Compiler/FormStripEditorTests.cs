using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 18 (commit 24c) — driven through the document view model, the same way <c>FormTrayTests</c>
/// drives the tray: an Item kind (spec §1, <c>Place == Item</c>) has no place of its own on the
/// canvas or in the tray, so BOTH of the VM's drop paths must refuse it, naming the "Type Here" slot
/// it actually comes from (spec §6) — never place it, and never say something else instead.
///
/// <para>⛔ Both paths, not one. <c>TrayDropCommand</c> is the tray's own drop; the canvas's is
/// <c>PlaceDroppedControlCommand</c>, which <c>FormCanvasDropTests</c> calls "the seam" the AXAML
/// binds to. An Item kind reaches both today with the wrong outcome in each: <c>TrayDrop</c> already
/// has an Item-shaped guard, but its message is <c>"'{kind}' has a position; drop it on the form, not
/// the tray"</c> — false for an Item, which has no position anywhere — and
/// <c>PlaceDroppedControl</c> has no guard at all, so it SUCCEEDS through the unmodified
/// <see cref="FormPlacement.Place"/>, positioning a control that should not exist as one.</para>
/// </summary>
[TestFixture]
public class FormStripEditorTests
{
    private const string Dir = "/proj/";

    private const string WinFormsDoc = """
        <Form Name="MenuForm" Version="1" Width="640" Height="480" Text="MenuForm">
          <Controls/>
          <Components/>
          <Resources/>
        </Form>
        """;

    private Mock<IEventAggregator> _events = null!;

    [SetUp]
    public void SetUp() => _events = new Mock<IEventAggregator>();

    private CodeEditorDocumentViewModel Open(string text = WinFormsDoc, string fileName = "MenuForm.blform")
    {
        var vm = new CodeEditorDocumentViewModel(new Mock<IFileService>().Object, _events.Object)
        {
            FilePath = Dir + fileName
        };
        vm.SetContent(text);
        return vm;
    }

    [Test]
    public void ATrayDrop_OfAnItemKind_IsRefused_NamingTypeHere_AndPlacesNothing()
    {
        var vm = Open();

        vm.TrayDropCommand.Execute("ToolStripMenuItem");

        Assert.Multiple(() =>
        {
            Assert.That(vm.DesignDocument!.Controls, Is.Empty, "nothing placed");
            Assert.That(vm.Tray.Items, Is.Empty, "not a component either — it has no place at all");
        });

        _events.Verify(e => e.Publish(It.Is<DesignerDiagnosticsEvent>(ev =>
                ev.Diagnostics.Any(d => d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Type Here")))),
            Times.Once,
            "the refusal must name the Type Here slot — today's TrayDrop message ('has a position; " +
            "drop it on the form') is wrong for a kind that has no position anywhere");
    }

    [Test]
    public void ADropOfAnItemKind_OnTheCanvas_IsRefused_NamingTypeHere_AndPlacesNothing()
    {
        var vm = Open();

        vm.PlaceDroppedControlCommand.Execute(new FormControlDropRequest("ToolStripMenuItem", 10, 10));

        Assert.That(vm.DesignDocument!.Controls, Is.Empty,
            "today this SUCCEEDS — FormPlacement.Place has no Item arm yet, so it positions the item " +
            "like an ordinary control");

        _events.Verify(e => e.Publish(It.Is<DesignerDiagnosticsEvent>(ev =>
                ev.Diagnostics.Any(d => d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Type Here")))),
            Times.Once);
    }
}
