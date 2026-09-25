using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Reproduction rig for the owner's two reports against the running IDE (2026-09-24, c8d3fe4c):
/// "the Type Here box shows up but keystrokes don't go into it", and "can't edit an existing item
/// at all" (second click / F2 do nothing visible). Every existing designer test hosts either the
/// canvas ALONE (<see cref="FormStripCanvasTests"/>), the overlay ALONE
/// (<see cref="FormTypeHereEditorTests"/>), or the view model with NO view
/// (<c>FormStripEditorTests</c>) — the spec (docs/superpowers/specs/2026-09-19-menus-toolbars-statusbars-design.md
/// §6) says outright that no test on this branch instantiates the real
/// <see cref="CodeEditorDocumentView"/>. This fixture is that missing instantiation: the real view,
/// the real view model, opened on a real .blform, driven with real mouse/keyboard input through the
/// hosting <see cref="Window"/> — as close to the IDE's own input pipeline as a headless test gets.
///
/// <para>This is a DIAGNOSTIC fixture, not a fix. Its job is to say which of the owner's steps
/// reproduce here and which do not, with a focus trace at every step, so the fix (if any is needed
/// in this harness's blind spots) is aimed at the right place.</para>
/// </summary>
[TestFixture]
public class FormDesignerRealViewTests
{
    private const string MenuStripDoc = """
        <Form Name="MenuForm" Version="1" Width="640" Height="480" Text="MenuForm">
          <Controls>
            <MenuStrip Id="menuStrip1" Dock="Top"/>
          </Controls>
          <Components/>
          <Resources/>
        </Form>
        """;

    private sealed class Rig
    {
        public required CodeEditorDocumentViewModel Vm { get; init; }
        public required CodeEditorDocumentView View { get; init; }
        public required Window Window { get; init; }
        public required FormCanvasControl Canvas { get; init; }
        public required FormTypeHereEditor TypeHereEditor { get; init; }

        public FormDocument Doc => Vm.DesignDocument!;

        /// <summary>Canvas-space centre of a <see cref="FormLayoutRole"/> entry, read live against
        /// the canvas's OWN current selection — never a hand-computed rectangle. Returned in the
        /// CANVAS's own local coordinate space; use <see cref="ToWindow"/> before dispatching input,
        /// since the real view nests the canvas many levels deep (toolbox/property-grid columns) and
        /// its own top-left is NOT the window's, unlike the isolated FormStripCanvasTests rig where
        /// the canvas IS the window's entire content.</summary>
        public Point CentreOfEntry(FormControl control, FormLayoutRole role)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            var entry = FormCanvasTransform.Layout(Doc, Canvas.SelectedControl)
                .Single(e => ReferenceEquals(e.Control, control) && e.Role == role);
            return fit.ToCanvas(entry.Bounds).Center;
        }

        /// <summary>Canvas-space centre of the Type Here slot hosted by <paramref name="host"/>.</summary>
        public Point CentreOfSlot(FormControl host)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            var entry = FormCanvasTransform.Layout(Doc, Canvas.SelectedControl)
                .Single(e => e.Role == FormLayoutRole.TypeHere && ReferenceEquals(e.Host, host));
            return fit.ToCanvas(entry.Bounds).Center;
        }

        /// <summary>Canvas-space FULL RECTANGLE of the Type Here slot hosted by <paramref name="host"/>
        /// — the same computation <see cref="CentreOfSlot"/> uses, but the whole rect rather than just
        /// its centre, so a caller can compare it against the overlay's own rect rather than just a
        /// point inside both.</summary>
        public Rect SlotCanvasRect(FormControl host)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            var entry = FormCanvasTransform.Layout(Doc, Canvas.SelectedControl)
                .Single(e => e.Role == FormLayoutRole.TypeHere && ReferenceEquals(e.Host, host));
            return fit.ToCanvas(entry.Bounds);
        }

        /// <summary>Translates a point in the CANVAS's own local space into WINDOW space, the
        /// coordinate frame <c>Window.MouseDown</c>/<c>MouseUp</c> actually dispatch in.</summary>
        public Point ToWindow(Point canvasLocalPoint) =>
            Canvas.TranslatePoint(canvasLocalPoint, Window)
            ?? throw new InvalidOperationException("the canvas is not in the window's visual tree — cannot translate a point");

        /// <summary>Translates a RECTANGLE in the canvas's own local space into WINDOW space (top-left
        /// translated; width/height are already canvas pixels, so they carry over unchanged).</summary>
        public Rect ToWindowRect(Rect canvasLocalRect)
        {
            var topLeft = ToWindow(canvasLocalRect.TopLeft);
            return new Rect(topLeft, canvasLocalRect.Size);
        }

        /// <summary>The overlay TextBox's own rectangle, translated into WINDOW space — the frame
        /// dispatched input and <see cref="SlotCanvasRect"/> both live in.</summary>
        public Rect OverlayWindowRect()
        {
            var box = Box(TypeHereEditor);
            var topLeft = box.TranslatePoint(new Point(0, 0), Window)
                ?? throw new InvalidOperationException("the overlay box is not in the window's visual tree");
            return new Rect(topLeft, box.Bounds.Size);
        }

        private static readonly System.Reflection.FieldInfo RenameArmedForField =
            typeof(FormCanvasControl).GetField("_renameArmedFor", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("FormCanvasControl._renameArmedFor not found by reflection — field renamed?");

        private static readonly System.Reflection.FieldInfo PressIsSelectingField =
            typeof(FormCanvasControl).GetField("_pressIsSelecting", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("FormCanvasControl._pressIsSelecting not found by reflection — field renamed?");

        public FormControl? RenameArmedFor => (FormControl?)RenameArmedForField.GetValue(Canvas);
        public bool PressIsSelecting => (bool)PressIsSelectingField.GetValue(Canvas)!;

        public void Click(Point canvasLocalPoint)
        {
            var at = ToWindow(canvasLocalPoint);
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>Same offset (+8px, X) the implementer's probe used to make a second, GENUINELY
        /// SEPARATE click land within the double-click distance threshold's reach of a DIFFERENT
        /// point than the first press. Measured: two <see cref="Click"/> calls at the identical point
        /// with no time advance between them arrive at the control as a double-click — Avalonia's
        /// headless pointer pipeline stamps the second press with ClickCount=2 purely from proximity,
        /// with no virtual clock to advance past instead. Offsetting the point is what tells it these
        /// are two distinct single clicks rather than one double-click, while staying inside the same
        /// cell so the hit test still resolves to the same control.</summary>
        public Point OffsetWithinCell(Point canvasLocalPoint) => canvasLocalPoint + new Point(8, 0);

        public FormControl MenuStrip() => Doc.Controls.Single(c => c.Id == "menuStrip1");
    }

    /// <summary>Opens the real view + real VM on a .blform containing one empty MenuStrip, and pumps
    /// the dispatcher so any posted layout/focus work from construction has already run before the
    /// test's own steps begin.</summary>
    private static Rig Open(string text = MenuStripDoc, string fileName = "MenuForm.blform")
    {
        var vm = new CodeEditorDocumentViewModel(new Mock<IFileService>().Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = "/proj/" + fileName
        };
        vm.SetContent(text);

        var entered = vm.EnterDesignModeForFormDocument();
        Assert.That(entered, Is.True, "precondition: EnterDesignModeForFormDocument must succeed or nothing below is testing the designer at all");

        var view = new CodeEditorDocumentView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var canvas = view.FindControl<FormCanvasControl>("DesignCanvas")
            ?? throw new InvalidOperationException("DesignCanvas not found — named element missing or view not templated");
        var typeHere = view.FindControl<FormTypeHereEditor>("TypeHereEditor")
            ?? throw new InvalidOperationException("TypeHereEditor not found — named element missing or view not templated");

        return new Rig { Vm = vm, View = view, Window = window, Canvas = canvas, TypeHereEditor = typeHere };
    }

    private static TextBox Box(FormTypeHereEditor editor) => (TextBox)editor.Children[0];

    /// <summary>What has focus right now, plus whether the overlay's own inner TextBox is focused —
    /// the instrumentation the task calls for, captured after every step.</summary>
    private static string FocusTrace(Rig rig, string label)
    {
        var focused = TopLevel.GetTopLevel(rig.Window)?.FocusManager?.GetFocusedElement();
        var focusedDesc = focused switch
        {
            null => "null",
            Control c => $"{c.GetType().Name}\"{c.Name}\"",
            _ => focused.GetType().Name
        };
        var boxFocused = Box(rig.TypeHereEditor).IsFocused;
        var editTarget = rig.Vm.StripEditor.EditTarget?.Id ?? "null";
        var host = rig.Vm.StripEditor.Host is BasicLang.Forms.FormControl fc ? fc.Id : (rig.Vm.StripEditor.Host?.ToString() ?? "null");
        var selPrimary = rig.Vm.Selection.Primary?.Id ?? "null";
        var canvasSelected = rig.Canvas.SelectedControl?.Id ?? "null";
        var editingItem = rig.Canvas.EditingItem?.Id ?? "null";
        var renameArmedFor = rig.RenameArmedFor?.Id ?? "null";
        var trace = $"[{label}] Focused={focusedDesc}; TypeHereBox.IsFocused={boxFocused}; " +
                    $"StripEditor.IsActive={rig.Vm.StripEditor.IsActive}; StripEditor.Text=\"{rig.Vm.StripEditor.Text}\"; " +
                    $"EditTarget={editTarget}; Host={host}; " +
                    $"Selection.Primary={selPrimary}; Canvas.SelectedControl={canvasSelected}; " +
                    $"Canvas.EditingItem={editingItem}; " +
                    $"TypeHereEditor.IsVisible={rig.TypeHereEditor.IsVisible}; " +
                    $"TypeHereEditor.IsHitTestVisible={rig.TypeHereEditor.IsHitTestVisible}; " +
                    $"TypeHereBox.IsFocused={boxFocused}; " +
                    $"Canvas._renameArmedFor={renameArmedFor}; Canvas._pressIsSelecting={rig.PressIsSelecting}";
        TestContext.WriteLine(trace);
        return trace;
    }

    /// <summary>Asserts the overlay TextBox's WINDOW rect equals the CURRENT Type Here slot's window
    /// rect for <paramref name="host"/>, within 1px on each edge, recording both rects and every
    /// diagnostic field on failure. Used by the position-across-a-run repro (task step A).</summary>
    private static void AssertOverlayOnSlot(Rig rig, FormControl host, string label)
    {
        var expected = rig.ToWindowRect(rig.SlotCanvasRect(host));
        var actual = rig.OverlayWindowRect();
        var trace = FocusTrace(rig, label);
        var detail = $"[{label}] expected(TypeHereBounds->window)={expected}; actual(overlay box->window)={actual}; " +
                     $"canvas.TypeHereBounds={rig.Canvas.TypeHereBounds}; editor.SlotBounds={rig.TypeHereEditor.SlotBounds}; " +
                     $"StripEditor.Host={(rig.Vm.StripEditor.Host as FormControl)?.Id ?? "null"}; " +
                     $"Canvas.SelectedControl={rig.Canvas.SelectedControl?.Id ?? "null"}; " + trace;
        TestContext.WriteLine(detail);

        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(1), "overlay X != slot X. " + detail);
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1), "overlay Y != slot Y. " + detail);
            Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(1), "overlay Width != slot Width. " + detail);
            Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(1), "overlay Height != slot Height. " + detail);
        });
    }

    // ==================================================================
    // (a) click the strip, click its Type Here slot, type, Enter -> new item
    // ==================================================================

    [AvaloniaTest]
    public void ClickingTheStripThenItsTypeHereSlot_TypingAndEnter_CreatesAnItemWithThatText()
    {
        var rig = Open();
        var strip = rig.MenuStrip();

        // Step 1: click the strip itself (selects the band).
        rig.Click(rig.CentreOfEntry(strip, FormLayoutRole.Band));
        FocusTrace(rig, "after clicking the strip");
        TestContext.WriteLine($"[diag] Selection.Primary={(rig.Vm.Selection.Primary == strip ? "strip" : rig.Vm.Selection.Primary?.ToString() ?? "null")}; " +
            $"Canvas.SelectedControl={(rig.Canvas.SelectedControl == strip ? "strip" : rig.Canvas.SelectedControl?.ToString() ?? "null")}; " +
            $"PropertyGrid.SelectedControl={(rig.Vm.PropertyGrid.SelectedControl == strip ? "strip" : rig.Vm.PropertyGrid.SelectedControl?.ToString() ?? "null")}");

        // Step 2: click its Type Here slot.
        rig.Click(rig.CentreOfSlot(strip));
        var afterSlotClick = FocusTrace(rig, "after clicking the Type Here slot");

        Assert.That(rig.Vm.StripEditor.IsActive, Is.True,
            "clicking the Type Here slot must activate the strip editor — " + afterSlotClick);
        Assert.That(rig.TypeHereEditor.IsVisible, Is.True,
            "the overlay must be visible once the strip editor is active — " + afterSlotClick);

        // Step 3: type into it.
        rig.Window.KeyTextInput("&File");
        Dispatcher.UIThread.RunJobs();
        var afterTyping = FocusTrace(rig, "after KeyTextInput(\"&File\")");
        var typedIntoBox = Box(rig.TypeHereEditor).Text;

        // Step 4: Enter to commit.
        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterEnter = FocusTrace(rig, "after Enter");

        Assert.Multiple(() =>
        {
            Assert.That(Box(rig.TypeHereEditor).IsFocused, Is.True,
                "EXPECTED (per FormTypeHereEditorTests, isolated): the box focuses once IsActive flips true and the dispatcher is pumped. " +
                afterSlotClick);
            Assert.That(typedIntoBox, Is.EqualTo("&File"),
                "typed text must land IN THE BOX for it to ever reach StripEditor.Text — " + afterTyping);
            Assert.That(strip.Children, Has.Count.EqualTo(1),
                "committing must append exactly one item to the strip — " + afterEnter);
            if (strip.Children.Count == 1)
            {
                Assert.That(strip.Children[0].Properties.GetValueOrDefault("Text"), Is.EqualTo("&File"));
            }
        });
    }

    // ==================================================================
    // (b) select the item, click it again (separate click, not double-click) -> begins editing;
    //     type + Enter -> renamed
    // ==================================================================

    [AvaloniaTest]
    public void SecondClickOnAnAlreadySelectedItem_TypingAndEnter_RenamesIt()
    {
        var rig = Open();
        var strip = rig.MenuStrip();

        // Precondition: create one item the direct VM way (Task 21/23 unit-level path, already
        // proven green) so this test isolates the SECOND-CLICK gesture rather than re-testing (a).
        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("&File");
        Dispatcher.UIThread.RunJobs();
        var item = strip.Children.Single();

        // First click: selects the item (real input, through the real view).
        rig.Click(rig.CentreOfEntry(item, FormLayoutRole.Cell));
        var afterFirstClick = FocusTrace(rig, "after first click (select)");
        Assert.That(rig.Vm.Selection.Primary, Is.SameAs(item),
            "precondition: the first click must select the item — " + afterFirstClick);
        Assert.That(rig.Canvas.SelectedControl, Is.SameAs(item),
            "precondition: FormCanvasControl.SelectedControl (bound TwoWay to PropertyGrid.SelectedControl) " +
            "must ALSO reflect the item — OfferRename's second-click check compares hit against THIS property, " +
            "not against Selection.Primary directly. " + afterFirstClick);

        // Second click: a SEPARATE press, offset within the same cell so the headless pointer
        // pipeline does not read it as the second half of a double-click (see
        // Rig.OffsetWithinCell) — must begin editing.
        rig.Click(rig.OffsetWithinCell(rig.CentreOfEntry(item, FormLayoutRole.Cell)));
        var afterSecondClick = FocusTrace(rig, "after second click (begin edit)");

        Assert.That(rig.Vm.StripEditor.IsActive, Is.True,
            "OWNER'S REPORT: 'not able to edit at all' — a second single click on an already-selected " +
            "item must begin editing. " + afterSecondClick);
        Assert.That(rig.Vm.StripEditor.EditTarget, Is.SameAs(item),
            "must be an EDIT of the existing item, not a fresh Type Here create. " + afterSecondClick);

        rig.Window.KeyTextInput("&Save");
        Dispatcher.UIThread.RunJobs();
        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterEnter = FocusTrace(rig, "after typing '&Save' + Enter");

        Assert.That(item.Properties.GetValueOrDefault("Text"), Is.EqualTo("&Save"),
            "the item must be renamed in place. " + afterEnter);
    }

    /// <summary>
    /// DIAGNOSTIC (not one of the owner's four literal steps): isolates WHY the test above sees the
    /// edit begin on the wrong click. After <c>CommitTypeHere</c>, the just-created item is already
    /// <c>Selection.Primary</c> AND — via <c>PropertyGrid.SelectedControl</c>'s TwoWay binding to
    /// <c>FormCanvasControl.SelectedControl</c> — already the CANVAS's own notion of "selected", with
    /// no click having happened yet. <c>OnPointerPressed</c>'s second-click test is
    /// <c>ReferenceEquals(hit, SelectedControl)</c>, which cannot tell "the user's first-ever click on
    /// this item" apart from "the user's second click" when the canvas already agrees the item is
    /// selected before any click at all. Deselecting first (a genuine click on empty background) is
    /// the control: it clears <c>SelectedControl</c>, so the NEXT click is unambiguously a first click.
    /// </summary>
    [AvaloniaTest]
    public void DeselectingFirst_ThenSelectingTheItem_DoesNotImmediatelyBeginEditing()
    {
        var rig = Open();
        var strip = rig.MenuStrip();
        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("&File");
        Dispatcher.UIThread.RunJobs();
        var item = strip.Children.Single();

        FocusTrace(rig, "immediately after CommitTypeHere, before any click");

        // Click empty background, well below the docked band, to deselect.
        rig.Click(new Point(50, 300));
        var afterDeselect = FocusTrace(rig, "after clicking empty background to deselect");
        Assert.That(rig.Canvas.SelectedControl, Is.Null,
            "precondition for a clean test: the background click must actually deselect. " + afterDeselect);

        // Now a GENUINE first click on the item.
        rig.Click(rig.CentreOfEntry(item, FormLayoutRole.Cell));
        var afterGenuineFirstClick = FocusTrace(rig, "after a genuine first click on the item");

        Assert.That(rig.Vm.StripEditor.IsActive, Is.False,
            "a GENUINE first click on a previously-unselected item must only select it, per " +
            "FormStripCanvasTests.PressingAnUnselectedItemsCell_SelectsIt_ButDoesNotBeginEditing — it must " +
            "NOT immediately begin editing. " + afterGenuineFirstClick);
    }

    // ==================================================================
    // (c) select the item, press F2, type, Enter -> renamed
    // ==================================================================

    [AvaloniaTest]
    public void F2OnASelectedItem_TypingAndEnter_RenamesIt()
    {
        var rig = Open();
        var strip = rig.MenuStrip();
        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("&File");
        Dispatcher.UIThread.RunJobs();
        var item = strip.Children.Single();

        rig.Click(rig.CentreOfEntry(item, FormLayoutRole.Cell));
        var afterClick = FocusTrace(rig, "after clicking to select the item");
        Assert.That(rig.Canvas.IsFocused, Is.True,
            "F2 must reach FormCanvasControl.OnKeyDown, which requires the CANVAS to hold keyboard " +
            "focus after the click — " + afterClick);

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterF2 = FocusTrace(rig, "after F2");

        Assert.That(rig.Vm.StripEditor.IsActive, Is.True,
            "OWNER'S REPORT: 'F2 does nothing visible'. " + afterF2);
        Assert.That(rig.Vm.StripEditor.EditTarget, Is.SameAs(item), afterF2);

        rig.Window.KeyTextInput("&Renamed");
        Dispatcher.UIThread.RunJobs();
        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterEnter = FocusTrace(rig, "after typing '&Renamed' + Enter");

        Assert.That(item.Properties.GetValueOrDefault("Text"), Is.EqualTo("&Renamed"), afterEnter);
    }

    // ==================================================================
    // (c) variant: a window-level F2 KeyBinding exists, mirroring MainWindow.axaml:39
    // (<KeyBinding Gesture="F2" Command="{Binding NextBookmarkCommand}"/>) — the real environment
    // this view is normally hosted inside, which none of the isolated canvas/editor tests include.
    // ==================================================================

    [AvaloniaTest]
    public void F2_WithAWindowLevelKeyBinding_LikeMainWindowsOwn_StillReachesTheCanvas()
    {
        var rig = Open();
        var strip = rig.MenuStrip();
        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("&File");
        Dispatcher.UIThread.RunJobs();
        var item = strip.Children.Single();

        var bookmarkCommand = new RecorderCommand();
        rig.Window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F2),
            Command = bookmarkCommand
        });

        rig.Click(rig.CentreOfEntry(item, FormLayoutRole.Cell));
        FocusTrace(rig, "after clicking to select the item (window-level F2 binding present)");

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterF2 = FocusTrace(rig, "after F2, with a window-level F2 KeyBinding present");

        Assert.Multiple(() =>
        {
            Assert.That(rig.Vm.StripEditor.IsActive, Is.True,
                "a window-level F2 KeyBinding (mirroring MainWindow.axaml's NextBookmarkCommand) must not " +
                "steal F2 before FormCanvasControl.OnKeyDown gets a chance to act on it. " + afterF2);
            Assert.That(bookmarkCommand.Executions, Is.Zero,
                "if the canvas handles F2 (marks it Handled), the window-level KeyBinding must NOT also fire. " + afterF2);
        });
    }

    // ==================================================================
    // (d) MEASURED GAP: a mutant making RenameOnF2Command.CanExecute always true SURVIVED the whole
    // fast subset. CanExecute must be exactly "F2 would open a rename" (FormCanvasControl.cs's own
    // comment on RenameOnF2Command) — never looser — or the canvas's own KeyBinding marks F2 Handled
    // in cases it has no business claiming, and MainWindow's window-level NextBookmarkCommand (bound
    // the same way at MainWindow.axaml:39) silently never runs while the designer has focus. Each row
    // below reuses the window-level F2 recorder from
    // F2_WithAWindowLevelKeyBinding_LikeMainWindowsOwn_StillReachesTheCanvas, but asserts the OPPOSITE
    // outcome: the window's command must be the one that fires, exactly once, and no rename may begin.
    // ==================================================================

    [AvaloniaTest]
    public void F2_CanvasFocused_NothingSelected_FallsThroughToTheWindowCommand()
    {
        var rig = Open();

        var bookmarkCommand = new RecorderCommand();
        rig.Window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F2),
            Command = bookmarkCommand
        });

        // A click on empty background focuses the canvas (every OnPointerPressed calls Focus(),
        // unconditionally) while leaving nothing selected.
        rig.Click(new Point(50, 300));
        var afterClick = FocusTrace(rig, "after clicking empty background (canvas focused, nothing selected)");
        Assert.That(rig.Canvas.IsFocused, Is.True,
            "precondition: the canvas must hold focus, or this test is not exercising the fallthrough " +
            "path at all. " + afterClick);
        Assert.That(rig.Canvas.SelectedControl, Is.Null,
            "precondition: nothing must be selected. " + afterClick);

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterF2 = FocusTrace(rig, "after F2 with the canvas focused and nothing selected");

        Assert.Multiple(() =>
        {
            Assert.That(rig.Vm.StripEditor.IsActive, Is.False,
                "with nothing selected there is nothing to rename — F2 must fall through rather than " +
                "begin an edit. " + afterF2);
            Assert.That(bookmarkCommand.Executions, Is.EqualTo(1),
                "CanExecute must be false with no selection, so the window's own F2 binding (Next " +
                "Bookmark) must run exactly once. " + afterF2);
        });
    }

    [AvaloniaTest]
    public void F2_CanvasFocused_ASeparatorSelected_FallsThroughToTheWindowCommand()
    {
        var rig = Open();
        var strip = rig.MenuStrip();
        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("-");
        Dispatcher.UIThread.RunJobs();
        var separator = strip.Children.Single();
        Assert.That(separator.Kind, Is.EqualTo("ToolStripSeparator"),
            "precondition: the committed item must actually be a separator, or this test is not " +
            "testing the unrenamable-selection case it claims to.");

        var bookmarkCommand = new RecorderCommand();
        rig.Window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F2),
            Command = bookmarkCommand
        });

        // Deselect first (see DeselectingFirst_ThenSelectingTheItem_DoesNotImmediatelyBeginEditing):
        // CommitTypeHere already left the separator as SelectedControl before any click, so without
        // this the NEXT click would read as a "second click" on an already-selected item and offer a
        // rename through OfferRename before F2 is even involved — which would make this test about
        // the click gesture, not about F2's own CanExecute.
        rig.Click(new Point(50, 300));
        Assert.That(rig.Canvas.SelectedControl, Is.Null,
            "precondition: the background click must actually deselect.");

        rig.Click(rig.CentreOfEntry(separator, FormLayoutRole.Cell));
        var afterClick = FocusTrace(rig, "after a genuine first click selecting the separator");
        Assert.That(rig.Vm.StripEditor.IsActive, Is.False,
            "precondition: a genuine first click on a (just-deselected) separator must only select it, " +
            "never begin an edit — otherwise F2 is not what is being tested here. " + afterClick);
        Assert.That(rig.Canvas.SelectedControl, Is.SameAs(separator),
            "precondition: the separator must actually be selected. " + afterClick);
        Assert.That(rig.Canvas.IsFocused, Is.True,
            "precondition: the click must leave the canvas focused. " + afterClick);

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterF2 = FocusTrace(rig, "after F2 with a separator selected");

        Assert.Multiple(() =>
        {
            Assert.That(rig.Vm.StripEditor.IsActive, Is.False,
                "a separator has no Text — FormCanvasTransform.IsRenamableItem must refuse it, so F2 " +
                "must fall through rather than opening an editor with nothing to type into. " + afterF2);
            Assert.That(bookmarkCommand.Executions, Is.EqualTo(1),
                "CanExecute must be false for an unrenamable selection, so the window's own F2 binding " +
                "must run exactly once. " + afterF2);
        });
    }

    [AvaloniaTest]
    public void F2_CanvasNotFocused_ARenamableItemSelected_FallsThroughToTheWindowCommand()
    {
        var rig = Open();
        var strip = rig.MenuStrip();
        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("&File");
        Dispatcher.UIThread.RunJobs();
        var item = strip.Children.Single();

        // Deselect first (see DeselectingFirst_ThenSelectingTheItem_DoesNotImmediatelyBeginEditing):
        // CommitTypeHere already left the item as SelectedControl before any click, so without this
        // the next click would read as a "second click" on an already-selected item and OfferRename
        // would begin an edit right there — which would make this test about the click gesture, not
        // about F2 with the canvas out of focus.
        rig.Click(new Point(50, 300));
        Assert.That(rig.Canvas.SelectedControl, Is.Null,
            "precondition: the background click must actually deselect.");

        rig.Click(rig.CentreOfEntry(item, FormLayoutRole.Cell));
        var afterClick = FocusTrace(rig, "after a genuine first click selecting the item");
        Assert.That(rig.Canvas.SelectedControl, Is.SameAs(item),
            "precondition: the item must be selected. " + afterClick);
        Assert.That(rig.Vm.StripEditor.IsActive, Is.False,
            "precondition: a genuine first click must only select, never begin an edit. " + afterClick);

        // Move keyboard focus OFF the canvas onto another focusable element in the same window,
        // without touching the selection — the two are independent stores. A plain Button (not
        // ToolboxList — an unstyled headless ListBox does not reliably accept Focus() the way a
        // Button does; see FormTypeHereEditorTests.Surface's own "other" control for the same
        // convention) is added as a sibling of the real view inside the window.
        var other = new Button();
        var wrapper = new Grid();
        rig.Window.Content = null; // detach the view from the window before reparenting it below
        wrapper.Children.Add(rig.View);
        wrapper.Children.Add(other);
        rig.Window.Content = wrapper;
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        other.Focus();
        Dispatcher.UIThread.RunJobs();
        var afterRefocus = FocusTrace(rig, "after moving focus off the canvas onto another control");
        Assert.That(rig.Canvas.IsFocused, Is.False,
            "precondition: focus must have actually left the canvas, or this test proves nothing about " +
            "the fallthrough path. " + afterRefocus);
        Assert.That(rig.Canvas.SelectedControl, Is.SameAs(item),
            "precondition: moving focus elsewhere must not itself clear the selection. " + afterRefocus);

        var bookmarkCommand = new RecorderCommand();
        rig.Window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F2),
            Command = bookmarkCommand
        });

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterF2 = FocusTrace(rig, "after F2 with the canvas not focused");

        Assert.Multiple(() =>
        {
            Assert.That(rig.Vm.StripEditor.IsActive, Is.False,
                "the canvas's own F2 KeyBinding only participates while keyboard focus is inside the " +
                "canvas itself — with focus elsewhere it must not fire at all, renamable selection or " +
                "not. " + afterF2);
            Assert.That(bookmarkCommand.Executions, Is.EqualTo(1),
                "with the canvas out of the focused element's ancestor chain, the window's own F2 " +
                "binding must be the one (and only) thing that runs. " + afterF2);
        });
    }

    // ==================================================================
    // TASK A: OWNER'S REPORT #1 — "Allows me to add a new item but the text was typed NOT IN the
    // Type Here". The overlay TextBox receives the keystrokes but is not positioned over the slot the
    // canvas is drawing. Types four top-level items in one run, then opens &File's own dropdown slot
    // and types two more — checking the overlay's WINDOW rect against the CURRENT TypeHere slot's
    // window rect after every single commit, plus once more after a window resize.
    // ==================================================================

    [AvaloniaTest]
    public void OverlayStaysOnTheTypeHereSlot_AcrossAWholeRunAndIntoADropdown()
    {
        var rig = Open();
        var strip = rig.MenuStrip();

        // Step 0: select the strip, then click its Type Here slot to begin the run.
        rig.Click(rig.CentreOfEntry(strip, FormLayoutRole.Band));
        rig.Click(rig.CentreOfSlot(strip));
        AssertOverlayOnSlot(rig, strip, "run start: overlay over the strip's empty slot");

        foreach (var text in new[] { "&File", "&Edit", "&View", "&Help" })
        {
            rig.Window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
            rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            using (rig.Window.CaptureRenderedFrame()) { } // pump a real render pass, as the IDE would

            AssertOverlayOnSlot(rig, strip, $"after committing \"{text}\"");
        }

        Assert.That(strip.Children, Has.Count.EqualTo(4),
            "precondition for the dropdown half: all four top-level items must exist. " +
            FocusTrace(rig, "before opening &File's dropdown"));
        var fileItem = strip.Children.Single(c => c.Properties.GetValueOrDefault("Text") == "&File");

        // Close the strip's own run first (Escape), select &File, then click ITS Type Here slot —
        // the same real gesture the owner used to add a submenu item.
        rig.Window.KeyPress(Key.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        rig.Click(rig.CentreOfEntry(fileItem, FormLayoutRole.Cell));
        var afterSelectFile = FocusTrace(rig, "after selecting &File (to open its dropdown)");
        Assert.That(rig.Canvas.SelectedControl, Is.SameAs(fileItem),
            "precondition: &File must be selected so its dropdown (and Type Here slot) is on screen. " +
            afterSelectFile);

        rig.Click(rig.CentreOfSlot(fileItem));
        AssertOverlayOnSlot(rig, fileItem, "&File dropdown: overlay over its empty slot");

        foreach (var text in new[] { "&Open...", "E&xit" })
        {
            rig.Window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
            rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            using (rig.Window.CaptureRenderedFrame()) { }

            AssertOverlayOnSlot(rig, fileItem, $"&File dropdown: after committing \"{text}\"");
        }

        // Repeat one step after a resize — 1280x800 -> 1000x700 — to catch a TypeHereBounds that is
        // only recomputed on SOME renders (stale on a resize that doesn't otherwise touch selection).
        rig.Window.Width = 1280;
        rig.Window.Height = 800;
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AssertOverlayOnSlot(rig, fileItem, "&File dropdown: after growing the window to 1280x800");

        rig.Window.Width = 1000;
        rig.Window.Height = 700;
        rig.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AssertOverlayOnSlot(rig, fileItem, "&File dropdown: after shrinking the window back to 1000x700");
    }

    // ==================================================================
    // TASK B: OWNER'S REPORT #2 — "Allows me to rename once, then could not rename anything again".
    // Renames item 1, renames item 1 AGAIN, then item 2 — via the second-click gesture, then repeats
    // the same three steps via F2 — with the full diagnostic trace (StripEditor, EditingItem, overlay
    // IsVisible/IsHitTestVisible/IsFocused, focused element, and the canvas's private
    // _renameArmedFor/_pressIsSelecting) captured at every click/keypress.
    // ==================================================================

    [AvaloniaTest]
    public void RenamingTheSameItemTwiceThenADifferentItem_ViaSecondClick_AlwaysRenames()
    {
        var rig = Open();
        var strip = rig.MenuStrip();

        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("&File");
        rig.Vm.CommitTypeHere("&Edit");
        rig.Vm.CommitTypeHere("&View");
        Dispatcher.UIThread.RunJobs();
        var item1 = strip.Children[0];
        var item2 = strip.Children[1];

        RenameViaSecondClick(rig, item1, "&Save", "rename #1 on item1 (&File -> &Save)");
        RenameViaSecondClick(rig, item1, "&SaveAgain", "rename #2 on item1 (&Save -> &SaveAgain), SAME item");
        RenameViaSecondClick(rig, item2, "&EditRenamed", "rename #3 on item2 (&Edit -> &EditRenamed)");
    }

    [AvaloniaTest]
    public void RenamingTheSameItemTwiceThenADifferentItem_ViaF2_AlwaysRenames()
    {
        var rig = Open();
        var strip = rig.MenuStrip();

        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("&File");
        rig.Vm.CommitTypeHere("&Edit");
        rig.Vm.CommitTypeHere("&View");
        Dispatcher.UIThread.RunJobs();
        var item1 = strip.Children[0];
        var item2 = strip.Children[1];

        RenameViaF2(rig, item1, "&Save", "F2 rename #1 on item1 (&File -> &Save)");
        RenameViaF2(rig, item1, "&SaveAgain", "F2 rename #2 on item1 (&Save -> &SaveAgain), SAME item");
        RenameViaF2(rig, item2, "&EditRenamed", "F2 rename #3 on item2 (&Edit -> &EditRenamed)");
    }

    /// <summary>Select (click), separate second click (offset within the cell), type, Enter — the
    /// owner's exact gesture — with a full trace at every step and a render pumped between the two
    /// clicks, as the real app would draw between them.</summary>
    private static void RenameViaSecondClick(Rig rig, FormControl item, string newText, string label)
    {
        TestContext.WriteLine($"===== {label} =====");

        rig.Click(rig.CentreOfEntry(item, FormLayoutRole.Cell));
        var afterSelect = FocusTrace(rig, $"{label}: after select-click");
        using (rig.Window.CaptureRenderedFrame()) { }

        rig.Click(rig.OffsetWithinCell(rig.CentreOfEntry(item, FormLayoutRole.Cell)));
        var afterSecondClick = FocusTrace(rig, $"{label}: after second click");

        Assert.That(rig.Vm.StripEditor.IsActive, Is.True,
            $"{label}: second click must begin editing. select-click trace: {afterSelect}; " +
            $"second-click trace: {afterSecondClick}");
        Assert.That(rig.Vm.StripEditor.EditTarget, Is.SameAs(item),
            $"{label}: must be editing THIS item. " + afterSecondClick);

        rig.Window.KeyTextInput(newText);
        Dispatcher.UIThread.RunJobs();
        var afterTyping = FocusTrace(rig, $"{label}: after typing \"{newText}\"");

        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterEnter = FocusTrace(rig, $"{label}: after Enter");

        Assert.That(item.Properties.GetValueOrDefault("Text"), Is.EqualTo(newText),
            $"{label}: item must be renamed. typing trace: {afterTyping}; commit trace: {afterEnter}");
    }

    /// <summary>Click to select, F2, type, Enter — with a full trace at every step.</summary>
    private static void RenameViaF2(Rig rig, FormControl item, string newText, string label)
    {
        TestContext.WriteLine($"===== {label} =====");

        rig.Click(rig.CentreOfEntry(item, FormLayoutRole.Cell));
        var afterSelect = FocusTrace(rig, $"{label}: after select-click");
        using (rig.Window.CaptureRenderedFrame()) { }

        rig.Window.KeyPress(Key.F2, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterF2 = FocusTrace(rig, $"{label}: after F2");

        Assert.That(rig.Vm.StripEditor.IsActive, Is.True,
            $"{label}: F2 must begin editing. select-click trace: {afterSelect}; F2 trace: {afterF2}");
        Assert.That(rig.Vm.StripEditor.EditTarget, Is.SameAs(item),
            $"{label}: must be editing THIS item. " + afterF2);

        rig.Window.KeyTextInput(newText);
        Dispatcher.UIThread.RunJobs();
        var afterTyping = FocusTrace(rig, $"{label}: after typing \"{newText}\"");

        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var afterEnter = FocusTrace(rig, $"{label}: after Enter");

        Assert.That(item.Properties.GetValueOrDefault("Text"), Is.EqualTo(newText),
            $"{label}: item must be renamed. typing trace: {afterTyping}; commit trace: {afterEnter}");
    }

    // ==================================================================
    // Task 26e — the owed test for Render's POSTED TypeHereBounds write
    // (PublishTypeHereBoundsFromRender, FormCanvasControl.cs:1685). A surviving mutant made that write
    // SYNCHRONOUS (TypeHereBounds = bounds; directly, inside Render) — indistinguishable from the
    // posted version on every existing test because a render is always preceded by a bound-property
    // change that already ran the EAGER path (RefreshTypeHereBounds, OnPropertyChanged). This test
    // invalidates the canvas WITHOUT touching any bound property — a direct in-place mutation of an
    // EARLIER item's Text, which widens its cell and shifts the strip's Type Here slot to the right —
    // so Render's own publish is the ONLY path that can ever see the new bounds.
    // ==================================================================

    /// <summary>Asserts the overlay box's WINDOW rect matches <c>Canvas.TypeHereBounds</c>'s window
    /// rect within 1px — i.e. the overlay is actually where the canvas's OWN published value says it
    /// should be, not just wherever a fresh (independent) layout computation would put it.</summary>
    private static Rect AssertOverlayMatchesCanvasBounds(Rig rig, string label)
    {
        var expected = rig.ToWindowRect(rig.Canvas.TypeHereBounds);
        var actual = rig.OverlayWindowRect();
        TestContext.WriteLine($"[{label}] canvas.TypeHereBounds(window)={expected}; overlay(window)={actual}");

        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(1), $"[{label}] overlay X != TypeHereBounds X");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1), $"[{label}] overlay Y != TypeHereBounds Y");
            Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(1), $"[{label}] overlay Width != TypeHereBounds Width");
            Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(1), $"[{label}] overlay Height != TypeHereBounds Height");
        });

        return expected;
    }

    [AvaloniaTest]
    public void ARenderInvalidatedWithNoPropertyChange_StillMovesTheOverlayOntoTheNewSlot()
    {
        var rig = Open();
        var strip = rig.MenuStrip();

        rig.Vm.BeginTypeHere(strip);
        rig.Vm.CommitTypeHere("A");
        Dispatcher.UIThread.RunJobs();
        var item = strip.Children.Single();

        // Select the strip so its OWN Type Here slot (right after "A") is the one being tracked, then
        // let a normal render pass settle it — this is the EAGER path working correctly, establishing
        // the baseline the mutation below must move away from.
        rig.Click(rig.CentreOfEntry(strip, FormLayoutRole.Band));
        Dispatcher.UIThread.RunJobs();
        using (rig.Window.CaptureRenderedFrame()) { }

        AssertOverlayMatchesCanvasBounds(rig, "before widening the earlier item");
        var beforeX = rig.Canvas.TypeHereBounds.X;

        // Widen the EARLIER item's Text DIRECTLY on the model object — no ModelRevision bump, no
        // OnPropertyChanged trigger of any kind — so RefreshTypeHereBounds (the eager path) cannot
        // possibly have run before the render below. Only Render's own publish can ever see this.
        item.Properties["Text"] = "AAAAAAAAAAAAAAAAAAAA";
        rig.Canvas.InvalidateVisual();

        using (rig.Window.CaptureRenderedFrame()) { } // Render computes+posts; nothing drained yet
        Dispatcher.UIThread.RunJobs();                 // drains the posted write -> TypeHereBounds updates
        using (rig.Window.CaptureRenderedFrame()) { } // the overlay gets a chance to re-arrange onto it

        var afterX = rig.Canvas.TypeHereBounds.X;
        Assert.That(afterX, Is.GreaterThan(beforeX),
            "widening the earlier item must shift the strip's Type Here slot to the right — " +
            $"before X={beforeX}, after X={afterX}");

        AssertOverlayMatchesCanvasBounds(rig, "after widening the earlier item");
    }

    private sealed class RecorderCommand : System.Windows.Input.ICommand
    {
        public int Executions { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Executions++;
    }
}
