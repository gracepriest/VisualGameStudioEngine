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
        Surface(double width = 600, double height = 500)
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
        var window = new Window { Width = width, Height = height, Content = canvas };
        window.Show();
        // The canvas's hit-test transform is the one its last RENDER fitted — pump a frame so it is this size's.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

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
        var bounds = FormCanvasTransform.BoundsOf(control, default, FormDockLayout.Resolve(doc))
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

    /// <summary>Two window sizes: the canvas fits the 400x300 form below zoom 1.0 in the small one, at 1.0 in the large.</summary>
    private static readonly (double Width, double Height)[] TwoZooms = { (380, 300), (900, 700) };

    private static void DoubleClick(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    /// <summary>
    /// Slice 5 D-9 (rewrites <c>DoubleClickingEmptyFormBackgroundOpensNothing</c>): a double-click on the FORM's surface,
    /// clear of every control, asks the host to open the FORM's handler (its Load) — <c>ActivateFormCommand</c> — and never
    /// <c>ActivateControlCommand</c>, which keeps meaning "this control" (never null-means-the-selection).
    /// </summary>
    [AvaloniaTest]
    public void DoubleClickingTheFormSurface_AsksTheHostToOpenTheFormsLoad()
    {
        foreach (var (w, h) in TwoZooms)
        {
            var (canvas, window, _) = Surface(w, h);
            var control = new Recorder();
            var form = new Recorder();
            canvas.ActivateControlCommand = control;
            canvas.ActivateFormCommand = form;

            // Well clear of the button at (40,40)-(120,64), inside the 400x300 form.
            var fit = FormCanvasControl.Fit(canvas.Document!, canvas.Bounds.Size);
            Assert.That(fit.Zoom, w < 500 ? Is.LessThan(1.0) : Is.EqualTo(1.0), "the two sizes are two zooms");
            DoubleClick(window, fit.ToCanvas(new Point(300, 240)));

            Assert.Multiple(() =>
            {
                Assert.That(form.Executions, Is.EqualTo(1), $"{w}x{h} (zoom {fit.Zoom:0.##})");
                Assert.That(control.Executions, Is.Zero, $"{w}x{h}");
            });
            window.Close();
        }
    }

    /// <summary>D-9's other half: the grey canvas OUTSIDE the form also hit-tests no control, and must open nothing.</summary>
    [AvaloniaTest]
    public void DoubleClickingTheCanvasOutsideTheForm_OpensNothing()
    {
        foreach (var (w, h) in TwoZooms)
        {
            var (canvas, window, _) = Surface(w, h);
            var control = new Recorder();
            var form = new Recorder();
            canvas.ActivateControlCommand = control;
            canvas.ActivateFormCommand = form;

            var fit = FormCanvasControl.Fit(canvas.Document!, canvas.Bounds.Size);
            var outside = fit.ToCanvas(new Point(400 + 12 / fit.Zoom, 150));
            Assert.That(outside.X, Is.LessThan(canvas.Bounds.Width), $"{w}x{h}: the point is still on the canvas");
            DoubleClick(window, outside);

            Assert.Multiple(() =>
            {
                Assert.That(form.Executions, Is.Zero, $"{w}x{h}: past the form's right edge");
                Assert.That(control.Executions, Is.Zero, $"{w}x{h}");
            });
            window.Close();
        }
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
