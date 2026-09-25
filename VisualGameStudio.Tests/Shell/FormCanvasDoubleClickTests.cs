using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 22, the canvas half: a double-click asks the host to open the control's handler.
///
/// <para>⛔ The canvas does NOT create the handler itself. Creating one writes a second file — the
/// user's <c>.bas</c> — opens a document and moves a caret, none of which a drawing surface owns.
/// The same split as Delete: the canvas reports the gesture and the control it happened on, the host
/// decides what that means.</para>
/// </summary>
[TestFixture]
public class FormCanvasDoubleClickTests
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

    /// <summary>
    /// A canvas at a known origin with one button at a known place.
    ///
    /// <para>⚠ The window is sized and the canvas fills it, so a click at a computed point lands
    /// where the arithmetic says. A sized child would be CENTRED, and the press would hit chrome.</para>
    /// </summary>
    private static (FormCanvasControl Canvas, Window Window, FormControl Control)
        Surface()
    {
        var doc = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "T", Width = 400, Height = 300
        };

        var control = new FormControl
        {
            Kind = "Button",
            Id = "btn",
            Geometry = new PixelGeometry { X = 40, Y = 40, Width = 80, Height = 24 }
        };
        doc.Controls.Add(control);

        var canvas = new FormCanvasControl { Document = doc };
        var window = new Window { Width = 600, Height = 500, Content = canvas };
        window.Show();

        return (canvas, window, control);
    }

    /// <summary>
    /// The centre of a control, in canvas coordinates.
    ///
    /// <para>⛔ Through the canvas's OWN <c>Fit</c>, never a second copy of the maths. The form is
    /// centred at a fitted zoom, so canvas coordinates are not form coordinates — and a test that
    /// re-derived the centring would agree with the control today and drift from it silently.</para>
    /// </summary>
    private static Point CentreOf(FormCanvasControl canvas, FormDocument doc, FormControl control)
    {
        var fit = FormCanvasControl.Fit(doc, canvas.Bounds.Size);
        var bounds = FormCanvasTransform.BoundsOf(control, default)
                     ?? throw new InvalidOperationException("the fixture control has no geometry");
        return fit.ToCanvas(bounds).Center;
    }

    [AvaloniaTest]
    public void DoubleClickingAControlAsksTheHostToOpenItsHandler()
    {
        var (canvas, window, control) = Surface();
        var opened = new Recorder();
        canvas.ActivateControlCommand = opened;

        var at = CentreOf(canvas, canvas.Document!, control);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(opened.Executions, Is.EqualTo(1));
            Assert.That(opened.LastParameter, Is.SameAs(control),
                "the host is told WHICH control, not left to re-derive the selection");
        });
    }

    /// <summary>
    /// ⚠ Double-clicking the form's own background is not a gesture on a control. Passing null would
    /// make the host open a handler for whatever happened to be selected before — a handler on a
    /// control the user was not pointing at.
    /// </summary>
    [AvaloniaTest]
    public void DoubleClickingEmptyFormBackgroundOpensNothing()
    {
        var (canvas, window, _) = Surface();
        var opened = new Recorder();
        canvas.ActivateControlCommand = opened;

        // Well clear of the button at (40,40)-(120,64), still inside the form.
        var at = CentreOf(canvas, canvas.Document!, canvas.Document!.Controls[0]) + new Point(0, 120);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.That(opened.Executions, Is.Zero);
    }

    /// <summary>A double-click also selects, so the property grid follows the control just opened.</summary>
    [AvaloniaTest]
    public void DoubleClickingSelectsTheControlItOpens()
    {
        var (canvas, window, control) = Surface();
        canvas.ActivateControlCommand = new Recorder();

        var at = CentreOf(canvas, canvas.Document!, control);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.That(canvas.SelectedControl, Is.SameAs(control));
    }
}
