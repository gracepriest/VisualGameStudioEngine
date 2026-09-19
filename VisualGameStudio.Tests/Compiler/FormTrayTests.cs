using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 25 — the component tray, driven through the document view model: placing, selecting,
/// deleting and undoing a component, and what a drop on the tray does.
///
/// <para>⛔ Every test here drives the same commands the view binds — <c>PlaceControl</c>,
/// <c>DeleteControlCommand</c>, <c>UndoDesignerEditCommand</c>, <c>TrayDropCommand</c> — never the
/// tray view model in isolation. A tray that rebuilt itself correctly from a document nothing had
/// written would pass an isolated test and be empty in the IDE.</para>
/// </summary>
[TestFixture]
public class FormTrayTests
{
    private const string Dir = "/proj/";

    private const string WinFormsDoc = """
        <Form Name="LoginForm" Version="1" Width="800" Height="450" Text="LoginForm">
          <Controls>
            <Button Id="btnLogin" Text="Sign in" X="10" Y="10" Width="75" Height="23" TabIndex="0"/>
          </Controls>
          <Components/>
          <Resources/>
        </Form>
        """;

    private const string WebDoc = """
        <WebForm Name="LoginForm" Version="1">
          <Controls/>
          <Components/>
          <Resources/>
        </WebForm>
        """;

    private Mock<IEventAggregator> _events = null!;

    [SetUp]
    public void SetUp() => _events = new Mock<IEventAggregator>();

    private CodeEditorDocumentViewModel Open(string text = WinFormsDoc, string fileName = "LoginForm.blform")
    {
        var vm = new CodeEditorDocumentViewModel(new Mock<IFileService>().Object, _events.Object)
        {
            FilePath = Dir + fileName
        };
        vm.SetContent(text);
        return vm;
    }

    [Test]
    public void PlacingAComponent_ShowsItInTheTray_SelectsIt_AndWritesTheDocument()
    {
        var vm = Open();

        Assert.That(vm.PlaceControl("Timer", 0, 0), Is.Null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Tray.Items.Select(i => i.Id), Is.EqualTo(new[] { "Timer1" }));
            Assert.That(vm.Tray.Items[0].Kind, Is.EqualTo("Timer"));
            Assert.That(vm.Tray.Items[0].Glyph, Is.EqualTo("(t)"), "the toolbox's own mark, not a second table");
            Assert.That(vm.Tray.IsVisible, Is.True);
            Assert.That(vm.PropertyGrid.SelectedControl?.Id, Is.EqualTo("Timer1"), "a drop puts its properties in front of you");
            Assert.That(vm.Text, Does.Contain("<Timer Id=\"Timer1\""), "it reached the document, so it is undoable");
            Assert.That(vm.TextDocument.Text, Is.EqualTo(vm.Text), "and the editor's copy agrees");
        });
    }

    [Test]
    public void TheTray_IsHiddenForAFormWithNoComponents()
    {
        var vm = Open();

        Assert.That(vm.Tray.Items, Is.Empty);
        Assert.That(vm.Tray.IsVisible, Is.False, "an empty strip under every form is noise");
    }

    [Test]
    public void SelectingATrayItem_GoesThroughTheOneSelectionPath()
    {
        var vm = Open();
        vm.PlaceControl("Timer", 0, 0);
        vm.PlaceControl("ToolTip", 0, 0);

        vm.Tray.Select(vm.Tray.Items[0]);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Selection.Primary?.Id, Is.EqualTo("Timer1"), "Selection is the one store the canvas and the commands read");
            Assert.That(vm.Tray.Items[0].IsSelected, Is.True);
            Assert.That(vm.Tray.Items[1].IsSelected, Is.False);
        });

        vm.Tray.Select(vm.Tray.Items[1]);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Selection.Primary?.Id, Is.EqualTo("ToolTip1"));
            Assert.That(vm.Tray.Items[0].IsSelected, Is.False, "the highlight follows the selection, not the click");
            Assert.That(vm.Tray.Items[1].IsSelected, Is.True);
        });
    }

    [Test]
    public void DeletingATrayItem_RemovesIt_AndUndoBringsItBack()
    {
        var vm = Open();
        vm.PlaceControl("Timer", 0, 0);
        var tmr = vm.DesignDocument!.Components.Single();

        vm.DeleteControlCommand.Execute(tmr);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Tray.Items, Is.Empty);
            Assert.That(vm.Text, Does.Not.Contain("<Timer"));
            Assert.That(vm.DesignDocument!.Components, Is.Empty);
        });

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Tray.Items.Select(i => i.Id), Is.EqualTo(new[] { "Timer1" }), "undo rewinds the text, and the tray follows the document");
            Assert.That(vm.DesignDocument!.Components.Single().Id, Is.EqualTo("Timer1"));
        });
    }

    [Test]
    public void EditingAComponentsProperty_ReachesTheDocument()
    {
        var vm = Open();
        vm.PlaceControl("Timer", 0, 0);

        var row = vm.PropertyGrid.Rows.Single(r => r.Name == "Interval");
        row.StringValue = "250";

        Assert.That(vm.Text, Does.Contain("Interval=\"250\""));
    }

    [Test]
    public void ATrayDrop_OfAComponentKind_PlacesIt()
    {
        var vm = Open();

        vm.TrayDropCommand.Execute("BackgroundWorker");

        Assert.That(vm.Tray.Items.Select(i => i.Kind), Is.EqualTo(new[] { "BackgroundWorker" }));
        Assert.That(vm.DesignDocument!.Controls, Has.Count.EqualTo(1), "the button is still the only control");
    }

    [Test]
    public void ATrayDrop_OfAControlKind_IsRefusedAndReported_NeverPlacedAtTheOrigin()
    {
        // A FormControlDropRequest carries no origin, so the canvas's own command cannot tell a tray
        // drop from a canvas drop and would put the Button at (0,0). The tray's command refuses.
        var vm = Open();

        vm.TrayDropCommand.Execute("Button");

        Assert.Multiple(() =>
        {
            Assert.That(vm.DesignDocument!.Controls, Has.Count.EqualTo(1), "nothing placed");
            Assert.That(vm.Tray.Items, Is.Empty);
        });

        _events.Verify(e => e.Publish(It.Is<DesignerDiagnosticsEvent>(ev =>
                ev.Diagnostics.Any(d => d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Button")))),
            Times.Once, "a drop that does nothing and says nothing is indistinguishable from a broken IDE");
    }

    [Test]
    public void OnTheWeb_ATimerLandsInTheTray_WithoutALayout()
    {
        var vm = Open(WebDoc, "LoginForm.blwebform");

        Assert.That(vm.PlaceControl("Timer", 0, 0), Is.Null, "no cell is needed for a component");
        Assert.That(vm.Tray.Items.Single().Id, Is.EqualTo("Timer1"));
    }
}
