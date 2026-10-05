using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Property-grid slice 5 Task 6: the Events tab driven through the REAL document view — a real click on the bolt, real
/// double-clicks on a row, the real handler drop-down, typing and Enter — asserting what lands in the .bas on "disk" and in
/// the document text. ⛔ The grid VM was proven in Task 5; these prove the VIEW reaches it and the HOST answers it
/// (<c>HandlerRequested</c> → <c>ActivateHandlerAsync</c>), because a VM nobody's view calls is this repo's signature failure.
/// </summary>
public partial class FormPropertyGridRealViewTests
{
    private const string WebPanelDoc = """
        <WebForm Name="GridForm" Version="1">
          <Layout Kind="Grid" Cols="1fr" Rows="160px"/>
          <Controls>
            <Panel Id="pnl" Col="0" Row="0" TabIndex="0"/>
          </Controls>
        </WebForm>
        """;

    private static RadioButton EventsButton(Rig rig) => rig.Grid.FindControl<RadioButton>("EventsButton")!;

    private static FormEventRow EventRow(Rig rig, string name) => rig.GridVm.EventRows.Single(r => r.Name == name);

    /// <summary>The handler combo of an Events-tab row, its container realised first.</summary>
    private static ComboBox HandlerCombo(Rig rig, FormEventRow row) =>
        rig.Container(row).GetVisualDescendants().OfType<ComboBox>().Single();

    /// <summary>An Events-tab row's NAME cell (the property rig's NameCell takes a property row).</summary>
    private static TextBlock EventNameCell(Rig rig, FormEventRow row) =>
        rig.Container(row).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == row.Name);

    /// <summary>A real click on the bolt (Events), then the layout settles.</summary>
    private static void ShowEvents(Rig rig)
    {
        rig.Click(EventsButton(rig));
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.That(rig.GridVm.IsEventsMode, Is.True, "precondition: the real click reached the Events toggle");
    }

    /// <summary>A real double-click at a visual's centre (two press/release pairs, no time between).</summary>
    private static void DoubleClick(Rig rig, Visual target)
    {
        var at = rig.CentreInWindow(target);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Opens an Events-tab row's drop-down with a real click on its ARROW and returns the open combo, re-read through the
    /// row. ⚠ Measured: an EDITABLE combo opens only from the glyph — its centre is the text box (focus, no drop-down), and
    /// 8px in from the right edge, past the 12px glyph, is the background border, which opens nothing either.
    /// </summary>
    private static ComboBox OpenHandlerDropDown(Rig rig, FormEventRow row)
    {
        var combo = HandlerCombo(rig, row);
        var glyph = combo.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "DropDownGlyph");
        var glyphCentre = glyph.TranslatePoint(new Point(glyph.Bounds.Width / 2, glyph.Bounds.Height / 2), combo)!.Value;
        rig.Click(combo, glyphCentre.X - combo.Bounds.Width / 2);
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        combo = HandlerCombo(rig, row);
        Assert.That(combo.IsDropDownOpen, Is.True, $"{row.Name}: the real click on the arrow opened the drop-down");
        return combo;
    }

    /// <summary>Picks <paramref name="item"/> from an Events-tab row's drop-down with real input (slice 4's PickInCombo).</summary>
    private static void PickHandler(Rig rig, FormEventRow row, string item) => PickFromOpen(rig, OpenHandlerDropDown(rig, row), item);

    /// <summary>
    /// Clicks <paramref name="item"/> in an ALREADY-open drop-down. ⚠ A second open by the same glyph click straight after a
    /// first is a DOUBLE-click to the headless pipeline — which, on an Events row, asks for the handler (measured: it wrote
    /// <c>btn_Click</c>) — so a test that reads the items picks from the same open.
    /// </summary>
    private static void PickFromOpen(Rig rig, ComboBox combo, string item)
    {
        try
        {
            var index = combo.Items.Cast<object?>().ToList().IndexOf(item);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"'{item}' is one of the drop-down's items");
            var container = combo.ContainerFromIndex(index) as ComboBoxItem
                            ?? throw new InvalidOperationException($"'{item}' has no realised item in the open drop-down");
            var popupTop = TopLevel.GetTopLevel(container) ?? throw new InvalidOperationException("the item has no top level");
            var at = container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), popupTop)!.Value;
            popupTop.MouseDown(at, MouseButton.Left);
            popupTop.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();
        }
        finally
        {
            combo.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Puts Subs inside the form class of the .bas on "disk" (before its <c>End Class</c>).</summary>
    private static void AddSubs(Rig rig, string subs)
    {
        var path = Dir + "GridForm.bas";
        var code = rig.Files.Contents[path];
        var end = code.LastIndexOf("End Class", StringComparison.Ordinal);
        rig.Files.Contents[path] = code[..end] + subs + code[end..];
    }

    private static int RegionClose(string code) => code.LastIndexOf("' </vgs:designer>", StringComparison.Ordinal);

    // ==================================================================
    // The tab
    // ==================================================================

    /// <summary>A real click on the bolt shows the seam's events for the selection — the Button's, then the form's.</summary>
    [AvaloniaTest]
    public void ClickingTheBolt_ListsTheSeamsEvents_ForTheSelection_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            SelectOnCanvas(rig, "btn");
            ShowEvents(rig);

            var seam = FormEvents.WiredOn(new FormBindOwner(rig.Doc, rig.Control("btn")).Definition!, FormTarget.WinForms)
                .Select(e => e.Name).ToList();
            var shown = rig.List.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToHashSet();
            Assert.Multiple(() =>
            {
                Assert.That(rig.GridVm.EventRows.Select(r => r.Name), Is.EqualTo(seam), $"{w}x{h}: exactly the seam's events");
                Assert.That(shown, Does.Contain("Click"), $"{w}x{h}: the Click row is realised in the real list");
                Assert.That(shown, Does.Not.Contain("BackColor"), $"{w}x{h}: no property row in the Events tab");
            });

            // Back to the form: its events (Load among them).
            rig.ClickOnCanvas(new Point(4, 4));
            Assert.That(rig.GridVm.EventRows.Select(r => r.Name), Does.Contain("Load"), $"{w}x{h}: the form's events");
        }
    }

    // ==================================================================
    // Double-click → the host writes, binds, opens
    // ==================================================================

    /// <summary>
    /// A real double-click on the EMPTY Click value cell: the .bas gains the classic stub BELOW the generated region, the
    /// document text gains the Bind, and the cell then shows the handler. At two zooms.
    /// </summary>
    [AvaloniaTest]
    public void DoubleClickingAnEmptyClickValue_WritesTheStub_AndBindsIt_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            SelectOnCanvas(rig, "btn");
            ShowEvents(rig);

            DoubleClick(rig, HandlerCombo(rig, EventRow(rig, "Click")));

            var stub = rig.Code.IndexOf("Private Sub btn_Click(sender As Object, e As EventArgs)", StringComparison.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(stub, Is.GreaterThanOrEqualTo(0), $"{w}x{h}: the stub is in the .bas on disk");
                Assert.That(stub, Is.GreaterThan(RegionClose(rig.Code)), $"{w}x{h}: WinForms puts it below the region");
                Assert.That(rig.Vm.Text, Does.Match("<Bind Event=\"Click\" Handler=\"btn_Click\"\\s?/>"),
                    $"{w}x{h}: the document text gains the Bind");
                Assert.That(EventRow(rig, "Click").Handler, Is.EqualTo("btn_Click"), $"{w}x{h}: the cell shows it");
            });
        }
    }

    /// <summary>The stub's signature is the EVENT's: MouseDown gets <c>MouseEventArgs</c>, never the default's EventArgs.</summary>
    [AvaloniaTest]
    public void DoubleClickingMouseDown_WritesAMouseEventArgsStub()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "btn");
        ShowEvents(rig);

        DoubleClick(rig, EventNameCell(rig, EventRow(rig, "MouseDown")));

        Assert.Multiple(() =>
        {
            Assert.That(rig.Code, Does.Contain("Private Sub btn_MouseDown(sender As Object, e As MouseEventArgs)"));
            Assert.That(rig.Vm.Text, Does.Match("<Bind Event=\"MouseDown\" Handler=\"btn_MouseDown\"\\s?/>"));
        });
    }

    /// <summary>With nothing selected the rows are the FORM's: double-clicking Load writes <c>GridForm_Load</c>.</summary>
    [AvaloniaTest]
    public void WithNothingSelected_DoubleClickingLoad_WritesTheFormsLoad()
    {
        using var rig = Open();
        ShowEvents(rig);

        DoubleClick(rig, EventNameCell(rig, EventRow(rig, "Load")));

        Assert.Multiple(() =>
        {
            Assert.That(rig.Code, Does.Contain("Private Sub GridForm_Load(sender As Object, e As EventArgs)"));
            Assert.That(rig.Doc.Binds.Any(b => b is { Event: "Load", Handler: "GridForm_Load" }), Is.True,
                "the form's own Bind");
        });
    }

    /// <summary>
    /// D-9 end to end: a real double-click on the CANVAS, on the form's surface clear of every control, writes and binds
    /// the form's Load — through the real view's <c>ActivateFormCommand</c> binding — at two zooms. Outside the form,
    /// nothing.
    /// </summary>
    [AvaloniaTest]
    public void DoubleClickingTheFormSurfaceOnTheCanvas_WritesTheFormsLoad_AndOutsideItNothing_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            var fit = FormCanvasControl.Fit(rig.Doc, rig.Canvas.Bounds.Size);
            var before = rig.Code;

            // Outside: just past the form's right edge, still on the canvas.
            var outside = rig.Canvas.TranslatePoint(fit.ToCanvas(new Point(640 + 10 / fit.Zoom, 240)), rig.Window)!.Value;
            for (var i = 0; i < 2; i++)
            {
                rig.Window.MouseDown(outside, MouseButton.Left);
                rig.Window.MouseUp(outside, MouseButton.Left);
            }

            Dispatcher.UIThread.RunJobs();
            Assert.That(rig.Code, Is.EqualTo(before), $"{w}x{h}: outside the form, nothing is written");

            // On the surface: well clear of lbl (16,16) and btn (16,56).
            var surface = rig.Canvas.TranslatePoint(fit.ToCanvas(new Point(400, 300)), rig.Window)!.Value;
            for (var i = 0; i < 2; i++)
            {
                rig.Window.MouseDown(surface, MouseButton.Left);
                rig.Window.MouseUp(surface, MouseButton.Left);
            }

            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(rig.Code, Does.Contain("Private Sub GridForm_Load(sender As Object, e As EventArgs)"),
                    $"{w}x{h} (zoom {fit.Zoom:0.##}): the form's Load");
                Assert.That(rig.Doc.Binds.Any(b => b is { Event: "Load", Handler: "GridForm_Load" }), Is.True, $"{w}x{h}: bound");
            });
        }
    }

    // ==================================================================
    // The drop-down
    // ==================================================================

    /// <summary>
    /// The drop-down offers ONLY the Subs that fit Click (a Sub with the wrong parameters, a Function, a ByRef Sub are not
    /// offered); a real pick binds it and leaves the .bas byte-identical. Clearing the cell unbinds — the code still
    /// untouched. At two zooms.
    /// </summary>
    [AvaloniaTest]
    public void TheDropDown_OffersOnlyFittingSubs_APickBinds_AndClearingUnbinds_TheCodeUntouched_AtTwoZooms()
    {
        foreach (var (w, h) in TwoZooms)
        {
            using var rig = Open(w, h);
            AddSubs(rig, """
                    Private Sub Fits(sender As Object, e As EventArgs)
                    End Sub

                    Private Sub Wrong(x As Integer)
                    End Sub

                    Private Sub ByRefOne(sender As Object, ByRef e As EventArgs)
                    End Sub

                    Private Function NotASub(sender As Object, e As EventArgs) As Integer
                        Return 0
                    End Function

                """);
            var before = rig.Code;
            SelectOnCanvas(rig, "btn");
            ShowEvents(rig);

            var row = EventRow(rig, "Click");
            var combo = OpenHandlerDropDown(rig, row);
            var offered = combo.Items.Cast<object?>().ToList();
            Assert.That(offered, Is.EqualTo(new object[] { "Fits" }), $"{w}x{h}: only the Sub that fits Click");

            PickFromOpen(rig, combo, "Fits");
            Assert.Multiple(() =>
            {
                Assert.That(row.Handler, Is.EqualTo("Fits"), $"{w}x{h}: the pick bound it");
                Assert.That(rig.Vm.Text, Does.Match("<Bind Event=\"Click\" Handler=\"Fits\"\\s?/>"), $"{w}x{h}: in the document");
                Assert.That(rig.Code, Is.EqualTo(before), $"{w}x{h}: a pick writes no code");
            });

            // Clear the cell: select its text, delete, Enter.
            var boundText = rig.Vm.Text;
            var box = HandlerCombo(rig, row).GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
            rig.Click(box);
            rig.Window.KeyPress(Key.A, RawInputModifiers.Control);
            rig.Window.KeyPress(Key.Delete, RawInputModifiers.None);
            rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(row.Handler, Is.Empty, $"{w}x{h}: clearing unbinds");
                Assert.That(rig.Control("btn").Binds.Any(b => b.Event == "Click"), Is.False, $"{w}x{h}: the Bind is gone");
                Assert.That(rig.Code, Is.EqualTo(before), $"{w}x{h}: the Sub is never deleted");
            });

            // ONE undo restores the Bind, byte-identical.
            rig.Vm.UndoDesignerEditCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(rig.Vm.Text, Is.EqualTo(boundText), $"{w}x{h}: one undo brings the Bind back");
        }
    }

    /// <summary>
    /// ⛔ The slice-4 popup trap on the Events tab: the SECOND drop-down opened in the window, on a row the list had to
    /// SCROLL to, must stay open long enough to pick from. Opening a combo brings its item into view; without the row
    /// containers' guard that request scrolls the property list back to the top, every container is recycled, and the
    /// combo closes under the pointer. At the small size, where the Button's events do not fit.
    /// </summary>
    [AvaloniaTest]
    public void ASecondDropDown_OnARowScrolledIntoView_StaysOpen_AndItsPickBinds()
    {
        using var rig = Open(TwoZooms[0].Width, TwoZooms[0].Height);
        AddSubs(rig, """
                Private Sub Fits(sender As Object, e As EventArgs)
                End Sub

                Private Sub Other(sender As Object, e As EventArgs)
                End Sub

            """);
        SelectOnCanvas(rig, "btn");
        ShowEvents(rig);

        // The first drop-down (it also pushes the code-behind, so every row's choices are current).
        var first = OpenHandlerDropDown(rig, EventRow(rig, "Click"));
        first.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();

        // ⚠ The target is already BOUND (set up through the row, not the view): an editable combo with nothing SELECTED
        // brings nothing into view on open, so only a combo showing a chosen item can spring the trap (measured: with the
        // target unbound the guard's mutant survived).
        var target = rig.GridVm.DisplayItems.OfType<FormEventRow>().Last(r => r.Choices.Contains("Fits"));
        target.Commit("Fits");
        rig.Container(target);
        var scroller = rig.List.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Multiple(() =>
        {
            Assert.That(scroller.Offset.Y, Is.GreaterThan(0), $"precondition: {target.Name} needed the list to scroll");
            Assert.That(HandlerCombo(rig, target).SelectedItem, Is.EqualTo("Fits"), "precondition: the combo shows its choice");
        });

        PickHandler(rig, target, "Other");

        Assert.That(target.Handler, Is.EqualTo("Other"), $"{target.Name}: the pick on the scrolled row bound it");
    }

    /// <summary>
    /// Typing a NEW legal name and Enter asks the host for that stub: the .bas gains <c>DoIt</c> with Click's signature,
    /// and it is bound. A refused name (a keyword) writes nothing and says why in the description pane.
    /// </summary>
    [AvaloniaTest]
    public void TypingANewName_AndEnter_CreatesTheStub_AndBindsIt_AKeywordIsRefused()
    {
        using var rig = Open();
        SelectOnCanvas(rig, "btn");
        ShowEvents(rig);
        var row = EventRow(rig, "Click");

        var box = HandlerCombo(rig, row).GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
        rig.Click(box);
        var before = rig.Code;
        rig.Window.KeyTextInput("Dim");
        Assert.That(HandlerCombo(rig, row).Text, Is.EqualTo("Dim"), "precondition: the typing reached the cell");
        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.That(row.Refusal, Is.Not.Null, $"a keyword is refused (handler now '{row.Handler}')");
        Assert.Multiple(() =>
        {
            Assert.That(rig.GridVm.DescriptionBody, Does.Contain(row.Refusal!), "and the pane says why");
            Assert.That(row.Handler, Is.Empty, "nothing bound");
            Assert.That(rig.Code, Is.EqualTo(before), "nothing written");
        });

        box = HandlerCombo(rig, row).GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
        rig.Click(box);
        rig.Window.KeyPress(Key.A, RawInputModifiers.Control);
        rig.Window.KeyTextInput("DoIt");
        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Code, Does.Contain("Private Sub DoIt(sender As Object, e As EventArgs)"), "the typed stub");
            Assert.That(row.Handler, Is.EqualTo("DoIt"), "bound");
            Assert.That(rig.Vm.Text, Does.Match("<Bind Event=\"Click\" Handler=\"DoIt\"\\s?/>"));
        });
    }

    /// <summary>
    /// D-5 freshness: a Sub typed into the code-behind's OPEN, UNSAVED tab is offered the moment the drop-down opens — the
    /// view asks the host on open, and the host reads the tab, never only the disk.
    /// </summary>
    [AvaloniaTest]
    public void ASubTypedIntoTheOpenUnsavedTab_IsOfferedWhenTheDropDownOpens()
    {
        using var rig = Open();
        var codePath = Dir + "GridForm.bas";
        var bas = new CodeEditorDocumentViewModel(rig.Files.Service, new Mock<IEventAggregator>().Object) { FilePath = codePath };
        bas.SetContent(rig.Files.Contents[codePath]);
        rig.Vm.OpenDocumentLookup = path => string.Equals(path, codePath, StringComparison.OrdinalIgnoreCase) ? bas : null;

        SelectOnCanvas(rig, "btn");
        ShowEvents(rig);
        var row = EventRow(rig, "Click");
        Assert.That(row.Choices, Is.Empty, "precondition: nothing fits yet");

        var end = bas.Text.LastIndexOf("End Class", StringComparison.Ordinal);
        bas.Text = bas.Text[..end] + "    Private Sub Typed(sender As Object, e As EventArgs)\n    End Sub\n\n" + bas.Text[end..];
        Assert.That(rig.Files.Contents[codePath], Does.Not.Contain("Typed"), "precondition: the disk does not have it");

        var diskBefore = rig.Files.Contents[codePath];
        var combo = OpenHandlerDropDown(rig, row);
        var offered = combo.Items.Cast<object?>().ToList();
        Assert.That(offered, Does.Contain("Typed"), "the unsaved tab's Sub is offered on open");

        PickFromOpen(rig, combo, "Typed");
        Assert.Multiple(() =>
        {
            Assert.That(row.Handler, Is.EqualTo("Typed"), "picking it binds it");
            Assert.That(rig.Files.Contents[codePath], Is.EqualTo(diskBefore), "the disk .bas is untouched");
            Assert.That(bas.IsDirty, Is.True, "the tab keeps its unsaved edit");
        });
    }

    // ==================================================================
    // Web
    // ==================================================================

    /// <summary>
    /// A web Panel: no Paint row (no page equivalent — the seam's answer); double-clicking Click writes
    /// <c>pnl_Click(e As DomEvent)</c> ABOVE the region that wires it, and binds the DOM name <c>click</c>.
    /// </summary>
    [AvaloniaTest]
    public void OnAWebPanel_ThereIsNoPaint_AndClickWritesADomEventStubAboveTheRegion()
    {
        using var rig = Open(doc: WebPanelDoc, target: FormTarget.Web);
        SelectOnCanvas(rig, "pnl");
        ShowEvents(rig);

        Assert.That(rig.GridVm.EventRows.Select(r => r.Name), Does.Not.Contain("Paint"), "no Paint on a page");

        DoubleClick(rig, EventNameCell(rig, EventRow(rig, "Click")));

        var stub = rig.Code.IndexOf("Private Sub pnl_Click(e As DomEvent)", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(stub, Is.GreaterThanOrEqualTo(0), "the DomEvent stub");
            Assert.That(stub, Is.LessThan(RegionClose(rig.Code)), "the web puts a handler above the region that wires it");
            Assert.That(rig.Control("pnl").Binds.Any(b => b is { Event: "click", Handler: "pnl_Click" }), Is.True,
                "the DOM name is what a web Bind stores");
        });
    }
}
