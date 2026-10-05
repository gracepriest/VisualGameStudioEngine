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
/// press that begins the drag destroys it. Since property-grid slice 6 (D-13) the click PROMOTES the
/// control to primary and keeps the group, as VS does — there is no collapse on release either.</para>
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
            var bounds = FormCanvasTransform.BoundsOf(control, default, FormDockLayout.Resolve(Doc))!.Value;
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

        // ⚠ Offset: two presses at the IDENTICAL point arrive headless as a DOUBLE-click (ClickCount 2,
        // then DoubleTapped), which is a different gesture from two Ctrl-clicks. This test used to pass
        // only because that double-tap wrote the grid and not the selection store.
        rig.Click(rig.Centre(rig.B) + new Point(8, 0), RawInputModifiers.Control);

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Controls, Is.EqualTo(new[] { rig.A }));
            Assert.That(rig.Canvas.SelectedControl, Is.SameAs(rig.A), "and the grid's side agrees with the store");
        });
    }

    /// <summary>
    /// ⚠ Slice 6 Task 4b: this test used to click A — a MEMBER of {A, B} — and pass only through the collapse-on-release
    /// that D-13 removed (the pre-flight's "no test pins the collapse" was wrong: this one did, under a name that says
    /// "unselected"). It now clicks a control that really is unselected; a click on a member is
    /// <see cref="APlainClickOnAMember_PromotesItToPrimary_AndKeepsTheGroup"/>.
    /// </summary>
    [AvaloniaTest]
    public void APlainClickOnAnUnselectedControlReplacesTheSelection()
    {
        var (rig, c) = SurfaceOfThree();
        rig.Click(rig.Centre(rig.A));
        rig.Click(rig.Centre(rig.B), RawInputModifiers.Shift);

        rig.Click(rig.Centre(c));

        Assert.That(rig.Selection.Controls, Is.EqualTo(new[] { c }));
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
    // Slice 6 D-13: a plain click on a MEMBER promotes it to primary and keeps the group (VS)
    // ==================================================================

    /// <summary>The two-button surface plus a third Button <c>c</c>, well to the right.</summary>
    private static (Rig Rig, FormControl C) SurfaceOfThree()
    {
        var rig = Surface();
        var c = new FormControl
        {
            Kind = "Button", Id = "c",
            Geometry = new PixelGeometry { X = 200, Y = 70, Width = 80, Height = 24 }
        };
        rig.Doc.Controls.Add(c);
        rig.Canvas.InvalidateVisual();
        return (rig, c);
    }

    /// <summary>
    /// ⛔ D-13: select A, B, C with Ctrl+clicks, then a PLAIN click on A — the group stays {B, C, A} with A the primary
    /// (the old collapse-on-release picked A out of the group: VS does not, and the first click of a double-click on a
    /// member would have collapsed the group before the double-tap arrived). Align-lefts then lines up on A.
    /// </summary>
    [AvaloniaTest]
    public void APlainClickOnAMember_PromotesItToPrimary_AndKeepsTheGroup()
    {
        var (rig, c) = SurfaceOfThree();
        rig.Click(rig.Centre(rig.A));
        rig.Click(rig.Centre(rig.B), RawInputModifiers.Control);
        rig.Click(rig.Centre(c), RawInputModifiers.Control);
        Assert.That(rig.Selection.Controls, Is.EqualTo(new[] { rig.A, rig.B, c }), "precondition");

        rig.Click(rig.Centre(rig.A) + new Point(5, 0)); // offset: not a double-click with the first press

        Assert.Multiple(() =>
        {
            Assert.That(rig.Selection.Controls, Is.EqualTo(new[] { rig.B, c, rig.A }), "still all three, A promoted");
            Assert.That(rig.Canvas.SelectedControl, Is.SameAs(rig.A), "the primary is what the property grid shows");
        });

        FormArrange.Apply(rig.Doc, FormArrangeKind.AlignLeft, rig.Selection.Controls, rig.Selection.Primary);

        Assert.That(new[] { G(rig.B).X, G(c).X, G(rig.A).X }, Is.All.EqualTo(40), "aligned to A's left (40), not C's (200)");
    }

    /// <summary>D-13: a drag that starts on a member (promoting it) still moves the WHOLE group.</summary>
    [AvaloniaTest]
    public void DraggingFromANonPrimaryMember_MovesTheWholeGroup()
    {
        var (rig, c) = SurfaceOfThree();
        rig.Click(rig.Centre(rig.A));
        rig.Click(rig.Centre(rig.B), RawInputModifiers.Control);
        rig.Click(rig.Centre(c), RawInputModifiers.Control);

        var from = rig.Centre(rig.A) + new Point(3, 0);
        rig.Drag(from, from + new Point(32, 0));

        Assert.Multiple(() =>
        {
            Assert.That(new[] { G(rig.A).X, G(rig.B).X, G(c).X }, Is.EqualTo(new[] { 72, 72, 232 }), "every member by +32");
            Assert.That(rig.Selection.Controls, Has.Count.EqualTo(3), "the group survives the drag");
        });
    }

    // ==================================================================
    // Slice 6 D-11: arrow keys over the WHOLE selection — top-level members only, docked members skipped, one commit
    // ==================================================================

    private sealed class Counter : System.Windows.Input.ICommand
    {
        public int Executions { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Executions++;
    }

    private static void PressKey(Rig rig, Avalonia.Input.Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        rig.Canvas.Focus();
        rig.Window.KeyPress(key, modifiers);
        rig.Window.KeyRelease(key, modifiers);
    }

    [AvaloniaTest]
    public void AnArrow_NudgesEveryMember_WithOneCommit_AndCtrlCoarsens_AndShiftResizesThemAll()
    {
        var rig = Surface();
        var commits = new Counter();
        rig.Canvas.CommitGeometryCommand = commits;
        rig.Selection.SetRange(new[] { rig.A, rig.B });

        PressKey(rig, Avalonia.Input.Key.Right);
        Assert.Multiple(() =>
        {
            Assert.That(new[] { G(rig.A).X, G(rig.B).X }, Is.All.EqualTo(41), "both by 1");
            Assert.That(commits.Executions, Is.EqualTo(1), "ONE commit for the press (one undo step)");
        });

        PressKey(rig, Avalonia.Input.Key.Right, RawInputModifiers.Control);
        PressKey(rig, Avalonia.Input.Key.Right, RawInputModifiers.Shift);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { G(rig.A).X, G(rig.B).X }, Is.All.EqualTo(49), "Ctrl: one grid step (8) for both");
            Assert.That(new[] { G(rig.A).Width, G(rig.B).Width }, Is.All.EqualTo(81), "Shift: both grow by 1");
            Assert.That(commits.Executions, Is.EqualTo(3));
        });
    }

    [AvaloniaTest]
    public void OnAWebGridPage_AnArrow_MovesEveryMemberOneCell()
    {
        var doc = new FormDocument
        {
            Target = FormTarget.Web, Name = "T",
            Layout = new FormLayout { Kind = FormLayoutKind.Grid, Cols = "1fr,1fr,1fr", Rows = "auto,auto" }
        };
        var a = new FormControl { Kind = "Button", Id = "a", Geometry = new GridGeometry { Col = 0, Row = 0 } };
        var b = new FormControl { Kind = "Button", Id = "b", Geometry = new GridGeometry { Col = 1, Row = 1 } };
        doc.Controls.Add(a);
        doc.Controls.Add(b);
        var selection = new FormSelection();
        var commits = new Counter();
        var canvas = new FormCanvasControl { Document = doc, Selection = selection, CommitGeometryCommand = commits };
        var window = new Window { Width = 600, Height = 500, Content = canvas };
        try
        {
            window.Show();
            selection.SetRange(new[] { a, b });
            canvas.Focus();
            int Col(FormControl c) => ((GridGeometry)c.Geometry!).Col;

            window.KeyPress(Avalonia.Input.Key.Left, RawInputModifiers.None);
            Assert.Multiple(() =>
            {
                Assert.That(Col(a), Is.Zero, "a at column 0 clamps — it does not stop b");
                Assert.That(Col(b), Is.Zero, "b moved one cell left");
                Assert.That(commits.Executions, Is.EqualTo(1), "ONE commit for the press");
            });

            window.KeyPress(Avalonia.Input.Key.Right, RawInputModifiers.None);
            Assert.Multiple(() =>
            {
                Assert.That(new[] { Col(a), Col(b) }, Is.All.EqualTo(1), "both one cell right");
                Assert.That(commits.Executions, Is.EqualTo(2));
            });
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// ⛔ D-11 ancestor/descendant: a Panel and its own child Button both selected (Ctrl+click can) — one Right arrow moves
    /// the Panel by 1 and leaves the Button's CONTAINER-RELATIVE X alone: it moved once, with its container.
    /// </summary>
    [AvaloniaTest]
    public void ANudge_OfAPanelAndItsOwnChild_MovesTheChildOnlyWithItsContainer()
    {
        var rig = Surface();
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl", Geometry = new PixelGeometry { X = 200, Y = 150, Width = 150, Height = 100 }
        };
        var child = new FormControl
        {
            Kind = "Button", Id = "kid", Geometry = new PixelGeometry { X = 10, Y = 10, Width = 60, Height = 24 }
        };
        panel.Children.Add(child);
        rig.Doc.Controls.Add(panel);
        rig.Selection.SetRange(new[] { panel, child });

        PressKey(rig, Avalonia.Input.Key.Right);

        Assert.Multiple(() =>
        {
            Assert.That(G(panel).X, Is.EqualTo(201), "the Panel moved");
            Assert.That(G(child).X, Is.EqualTo(10), "the child is container-relative: it rode along, never moved twice");
        });
    }

    /// <summary>D-11: a docked member (a MenuStrip) is skipped by a nudge — its edge is a Dock property, not a rect.</summary>
    [AvaloniaTest]
    public void ANudge_SkipsADockedMember_AndMovesTheRest()
    {
        var rig = Surface();
        var strip = new FormControl { Kind = "MenuStrip", Id = "ms" };
        strip.Properties["Dock"] = "Top";
        rig.Doc.Controls.Add(strip);
        // A docked PIXEL member too: a Panel with Dock="Fill" has a rect, but its place comes from docking — FormGeometryEdit's
        // own docked guard (the one rule) refuses to move it.
        var filled = new FormControl
        {
            Kind = "Panel", Id = "fill",
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 400, Height = 300, Dock = "Fill" }
        };
        rig.Doc.Controls.Add(filled);
        rig.Selection.SetRange(new[] { filled, rig.A, strip }); // the STRIP is the primary

        PressKey(rig, Avalonia.Input.Key.Down);

        Assert.Multiple(() =>
        {
            Assert.That(G(filled).Y, Is.Zero, "the Dock=Fill Panel is not nudged");
            Assert.That(G(rig.A).Y, Is.EqualTo(41), "the Button moved down");
            Assert.That(strip.Geometry, Is.Null, "the strip gained no geometry");
            Assert.That(strip.Properties["Dock"], Is.EqualTo("Top"), "and keeps its Dock");
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
