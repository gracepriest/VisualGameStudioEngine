using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The keyboard half of the designer, driven through the real control.
///
/// <para>⛔ Before this the canvas handled NO keys at all: a control could be dragged and could not
/// be deleted, and nothing could be nudged a pixel. Those are not advanced features — they are what
/// a designer is, and their absence is invisible to every test that only ever calls a view model.</para>
/// </summary>
[TestFixture]
public class FormCanvasKeyboardTests
{
    private sealed class Recorder : System.Windows.Input.ICommand
    {
        public object? LastParameter { get; private set; }
        public int Executions { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            LastParameter = parameter;
            Executions++;
        }
    }

    private static (FormCanvasControl Canvas, Window Window, FormControl Control, FormDocument Doc)
        Surface(int x = 40, int y = 40)
    {
        var doc = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "T", Width = 400, Height = 300
        };

        var control = new FormControl
        {
            Kind = "Button",
            Id = "btn",
            Geometry = new PixelGeometry { X = x, Y = y, Width = 80, Height = 24 }
        };
        doc.Controls.Add(control);

        var canvas = new FormCanvasControl { Document = doc, SelectedControl = control };
        var window = new Window { Width = 600, Height = 500, Content = canvas };
        window.Show();
        canvas.Focus();

        return (canvas, window, control, doc);
    }

    private static PixelGeometry Geometry(FormControl control) => (PixelGeometry)control.Geometry!;

    [AvaloniaTest]
    public void AnArrowKeyNudgesTheSelectionByOne()
    {
        var (_, window, control, _) = Surface();

        window.KeyPress(Key.Right, RawInputModifiers.None);
        window.KeyPress(Key.Down, RawInputModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That(Geometry(control).X, Is.EqualTo(41));
            Assert.That(Geometry(control).Y, Is.EqualTo(41));
        });
    }

    /// <summary>⚠ VS's binding: Ctrl coarsens the nudge to a grid step.</summary>
    [AvaloniaTest]
    public void CtrlArrowNudgesByAGridStep()
    {
        var (_, window, control, _) = Surface();

        window.KeyPress(Key.Right, RawInputModifiers.Control);

        Assert.That(Geometry(control).X, Is.EqualTo(48), "40 + one 8-unit grid step");
    }

    [AvaloniaTest]
    public void ShiftArrowResizesInsteadOfMoving()
    {
        var (_, window, control, _) = Surface();
        var before = Geometry(control);
        var startX = before.X;
        var startWidth = before.Width;

        window.KeyPress(Key.Right, RawInputModifiers.Shift);

        Assert.Multiple(() =>
        {
            Assert.That(Geometry(control).X, Is.EqualTo(startX), "resizing must not move it");
            Assert.That(Geometry(control).Width, Is.EqualTo(startWidth + 1));
        });
    }

    /// <summary>
    /// ⛔ Delete goes through a COMMAND. The canvas mutates geometry in place because the host owns
    /// the same object graph, but removing a control changes the document's SHAPE — the selection
    /// has to be cleared and the region regenerated, which is the host's job.
    /// </summary>
    [AvaloniaTest]
    public void DeleteAsksTheHostToRemoveTheSelection()
    {
        var (canvas, window, control, _) = Surface();
        var recorder = new Recorder();
        canvas.DeleteCommand = recorder;

        window.KeyPress(Key.Delete, RawInputModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That(recorder.Executions, Is.EqualTo(1));
            Assert.That(recorder.LastParameter, Is.SameAs(control),
                "the host is told WHICH control, not left to re-derive the selection");
        });
    }

    [AvaloniaTest]
    public void AKeyNudgeCommitsImmediately_UnlikeADragWhichWaitsForRelease()
    {
        var (canvas, window, _, _) = Surface();
        var commit = new Recorder();
        canvas.CommitGeometryCommand = commit;

        window.KeyPress(Key.Right, RawInputModifiers.None);

        Assert.That(commit.Executions, Is.EqualTo(1),
            "a keypress is already a discrete edit — there is no 'still holding it' state to wait " +
            "for, and not committing leaves the file behind the canvas");
    }

    [AvaloniaTest]
    public void NothingHappensWithNoSelection()
    {
        var (canvas, window, control, _) = Surface();
        canvas.SelectedControl = null;
        var commit = new Recorder();
        canvas.CommitGeometryCommand = commit;

        window.KeyPress(Key.Right, RawInputModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That(Geometry(control).X, Is.EqualTo(40));
            Assert.That(commit.Executions, Is.Zero);
        });
    }
}
