using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Task 20: multi-select on the canvas, driven through the real control.
///
/// <para>⛔ The gesture that is easy to get wrong is clicking a control that is ALREADY part of a
/// group. Collapsing the selection on PRESS makes a multi-selection impossible to drag, because the
/// press that begins the drag destroys it — so the collapse waits for release, and only happens if
/// the pointer never moved.</para>
/// </summary>
[TestFixture]
public class FormCanvasMultiSelectTests
{
    private sealed class Rig
    {
        public required FormCanvasControl Canvas { get; init; }
        public required Window Window { get; init; }
        public required FormDocument Doc { get; init; }
        public required FormSelection Selection { get; init; }
        public required FormControl A { get; init; }
        public required FormControl B { get; init; }

        public Point Centre(FormControl control)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            var bounds = FormCanvasTransform.BoundsOf(control, default)!.Value;
            return fit.ToCanvas(bounds).Center;
        }

        /// <summary>A point in FORM units, mapped the way the canvas maps it.</summary>
        public Point At(double x, double y)
        {
            var fit = FormCanvasControl.Fit(Doc, Canvas.Bounds.Size);
            return fit.ToCanvas(new Rect(x, y, 1, 1)).TopLeft;
        }

        public void Click(Point at, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.MouseDown(at, MouseButton.Left, modifiers);
            Window.MouseUp(at, MouseButton.Left, modifiers);
        }

        public void Drag(Point from, Point to)
        {
            Window.MouseDown(from, MouseButton.Left);
            Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(to, MouseButton.Left);
        }
    }

    /// <summary>Two buttons, well apart, on a 400x300 form.</summary>
    private static Rig Surface()
    {
        var doc = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "T", Width = 400, Height = 300
        };

        var a = new FormControl
        {
            Kind = "Button", Id = "a",
            Geometry = new PixelGeometry { X = 40, Y = 40, Width = 80, Height = 24 }
        };
        var b = new FormControl
        {
            Kind = "Button", Id = "b",
            Geometry = new PixelGeometry { X = 40, Y = 100, Width = 80, Height = 24 }
        };
        doc.Controls.Add(a);
        doc.Controls.Add(b);

        var selection = new FormSelection();
        var canvas = new FormCanvasControl { Document = doc, Selection = selection };
        var window = new Window { Width = 600, Height = 500, Content = canvas };
        window.Show();
        canvas.Focus();

        return new Rig { Canvas = canvas, Window = window, Doc = doc, Selection = selection, A = a, B = b };
    }

    private static PixelGeometry G(FormControl control) => (PixelGeometry)control.Geometry!;

    // ==================================================================
    // Extending
    // ==================================================================

    [AvaloniaTest]
    public void APlainClickSelectsOneControl()
    {
        var rig = Surface();

        rig.Click(rig.Centre(rig.A));

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Controls, Is.EqualTo(new[] { rig.A }));
            Assert.That(rig.Canvas.SelectedControl, Is.SameAs(rig.A),
                "the primary is what the property grid shows");
        });
    }

    [AvaloniaTest]
    public void ShiftClickAddsToTheSelection()
    {
        var rig = Surface();
        rig.Click(rig.Centre(rig.A));

        rig.Click(rig.Centre(rig.B), RawInputModifiers.Shift);

        Assert.That(rig.Selection.Controls, Is.EquivalentTo(new[] { rig.A, rig.B }));
    }

    [AvaloniaTest]
    public void CtrlClickTogglesAControlBackOut()
    {
        var rig = Surface();
        rig.Click(rig.Centre(rig.A));
        rig.Click(rig.Centre(rig.B), RawInputModifiers.Control);

        rig.Click(rig.Centre(rig.B), RawInputModifiers.Control);

        Assert.That(rig.Selection.Controls, Is.EqualTo(new[] { rig.A }));
    }

    [AvaloniaTest]
    public void APlainClickOnAnUnselectedControlReplacesTheSelection()
    {
        var rig = Surface();
        rig.Click(rig.Centre(rig.A));
        rig.Click(rig.Centre(rig.B), RawInputModifiers.Shift);

        rig.Click(rig.Centre(rig.A));

        Assert.That(rig.Selection.Controls, Is.EqualTo(new[] { rig.A }));
    }

    [AvaloniaTest]
    public void EscapeClearsTheSelection()
    {
        var rig = Surface();
        rig.Click(rig.Centre(rig.A));
        rig.Click(rig.Centre(rig.B), RawInputModifiers.Shift);

        rig.Window.KeyPress(Key.Escape, RawInputModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.IsEmpty, Is.True);
            Assert.That(rig.Canvas.SelectedControl, Is.Null);
        });
    }

    // ==================================================================
    // Dragging a group
    // ==================================================================

    /// <summary>
    /// ⛔⛔ The whole point of multi-select. Dragging one member moves EVERY member by the same
    /// delta — a group that moves one control is just a selection that looks wrong.
    /// </summary>
    [AvaloniaTest]
    public void DraggingOneMemberMovesEveryMemberByTheSameDelta()
    {
        var rig = Surface();
        rig.Click(rig.Centre(rig.A));
        rig.Click(rig.Centre(rig.B), RawInputModifiers.Shift);

        // Primary is B after the shift-click; drag it.
        var from = rig.Centre(rig.B);
        rig.Drag(from, from + new Point(32, 0));

        Assert.Multiple(() =>
        {
            Assert.That(G(rig.B).X, Is.EqualTo(72), "40 + 32");
            Assert.That(G(rig.A).X, Is.EqualTo(72), "moved by the same delta");
            Assert.That(G(rig.A).Y, Is.EqualTo(40), "and not vertically");
        });
    }

    // ==================================================================
    // The rubber band
    // ==================================================================

    [AvaloniaTest]
    public void ARubberBandSelectsTheControlsItCovers()
    {
        var rig = Surface();

        rig.Drag(rig.At(20, 20), rig.At(200, 140));

        Assert.That(rig.Selection.Controls, Is.EquivalentTo(new[] { rig.A, rig.B }));
    }

    /// <summary>A band that catches nothing clears, which is how you deselect everything by hand.</summary>
    [AvaloniaTest]
    public void ARubberBandOverEmptySpaceClearsTheSelection()
    {
        var rig = Surface();
        rig.Click(rig.Centre(rig.A));

        rig.Drag(rig.At(200, 200), rig.At(300, 260));

        Assert.That(rig.Selection.IsEmpty, Is.True);
    }

    /// <summary>
    /// ⛔⛔ A band across a container selects the CONTAINER, never it and its children both.
    ///
    /// <para>Selecting both is actively harmful rather than merely redundant: a group move would
    /// translate the Panel — which carries its children — and then translate each child again, so
    /// they would end up twice as far from where they started as the Panel they live in.</para>
    ///
    /// <para>⚠ The band starts on the FORM's background at (10,10). It cannot start inside the
    /// Panel: a press there hits the Panel and begins a drag, which is what a press on a control
    /// means everywhere else on this canvas.</para>
    /// </summary>
    [AvaloniaTest]
    public void ABandAcrossAContainerSelectsTheContainerAndNotItsChildren()
    {
        var rig = Surface();
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl",
            Geometry = new PixelGeometry { X = 150, Y = 150, Width = 200, Height = 120 }
        };
        var inner = new FormControl
        {
            Kind = "Button", Id = "inner",
            Geometry = new PixelGeometry { X = 10, Y = 10, Width = 60, Height = 20 }
        };
        panel.Children.Add(inner);
        rig.Doc.Controls.Add(panel);

        rig.Drag(rig.At(10, 10), rig.At(260, 200));

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Contains(panel), Is.True);
            Assert.That(rig.Selection.Contains(inner), Is.False,
                "the container already carries it — selecting both would move it twice");
        });
    }
}
