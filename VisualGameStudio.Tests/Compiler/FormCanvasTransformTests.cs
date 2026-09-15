using Avalonia;
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 7: the canvas transform — the one mapping rendering, hit-testing and selection all share.
///
/// <para>⛔⛔ These tests exist because the failure has NO VISUAL SYMPTOM. A drifted copy of this
/// mapping renders the form perfectly and lands clicks on the wrong control, or on nothing, and
/// often only at some zoom levels. <c>MinimapControl</c> hand-duplicates its own transform three
/// times and the copies have already drifted; this is the same mapping written once, so the only
/// thing left to get wrong is the maths itself — which is what is pinned here.</para>
///
/// <para>No Avalonia application, window or headless harness is needed: <c>Point</c>, <c>Rect</c>
/// and <c>Vector</c> are plain value types. The canvas's APPEARANCE is not covered by anything
/// here and is not claimed to be.</para>
/// </summary>
[TestFixture]
public class FormCanvasTransformTests
{
    // ==================================================================
    // The mapping, and its inverse
    // ==================================================================

    [Test]
    public void Identity_MapsAPointToItself()
    {
        var t = new FormCanvasTransform();

        Assert.That(t.ToCanvas(new Point(96, 80)), Is.EqualTo(new Point(96, 80)));
    }

    [TestCase(1.0, 0, 0)]
    [TestCase(2.5, 0, 0)]
    [TestCase(0.25, 0, 0)]
    [TestCase(1.0, 40, -15)]
    [TestCase(3.0, -120, 200)]
    public void ToForm_IsExactlyTheInverseOfToCanvas(double zoom, double panX, double panY)
    {
        // ⛔ The property that matters. Render uses ToCanvas and hit-testing uses ToForm; if they
        // are not inverses, what is drawn and what is clickable disagree — and nothing looks wrong.
        var t = new FormCanvasTransform(zoom, new Vector(panX, panY));
        var form = new Point(37, 211);

        var roundTripped = t.ToForm(t.ToCanvas(form));

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.X, Is.EqualTo(form.X).Within(1e-9));
            Assert.That(roundTripped.Y, Is.EqualTo(form.Y).Within(1e-9));
        });
    }

    [Test]
    public void ARectangleScalesAndTranslates_AsItsCornersDo()
    {
        var t = new FormCanvasTransform(2.0, new Vector(10, 5));

        var canvas = t.ToCanvas(new Rect(20, 30, 100, 40));

        Assert.Multiple(() =>
        {
            Assert.That(canvas.X, Is.EqualTo(50));   // 20*2 + 10
            Assert.That(canvas.Y, Is.EqualTo(65));   // 30*2 + 5
            Assert.That(canvas.Width, Is.EqualTo(200));
            Assert.That(canvas.Height, Is.EqualTo(80));
        });
    }

    [Test]
    public void Zoom_IsClampedRatherThanAllowedToReachZero()
    {
        // ⛔ ToForm divides by Zoom. A zero or negative zoom is a divide-by-zero or a mirrored
        // canvas, and both arrive from an ordinary wheel gesture at the end of its range.
        Assert.Multiple(() =>
        {
            Assert.That(new FormCanvasTransform(0).Zoom, Is.EqualTo(FormCanvasTransform.MinZoom));
            Assert.That(new FormCanvasTransform(-4).Zoom, Is.EqualTo(FormCanvasTransform.MinZoom));
            Assert.That(new FormCanvasTransform(1e6).Zoom, Is.EqualTo(FormCanvasTransform.MaxZoom));
        });
    }

    [Test]
    public void ZoomAbout_KeepsThePointUnderTheCursorStill()
    {
        // ⚠ The gesture's whole contract. Zooming about the origin instead makes the form slide
        // away under the pointer, and the user chases it around the canvas.
        var t = new FormCanvasTransform(1.0, new Vector(13, 7));
        var anchor = new Point(250, 180);

        var zoomed = t.ZoomAbout(anchor, 2.75);
        var formUnderCursorBefore = t.ToForm(anchor);
        var formUnderCursorAfter = zoomed.ToForm(anchor);

        Assert.Multiple(() =>
        {
            Assert.That(zoomed.Zoom, Is.EqualTo(2.75).Within(1e-9));
            Assert.That(formUnderCursorAfter.X, Is.EqualTo(formUnderCursorBefore.X).Within(1e-9));
            Assert.That(formUnderCursorAfter.Y, Is.EqualTo(formUnderCursorBefore.Y).Within(1e-9));
        });
    }

    // ==================================================================
    // Hit-testing
    // ==================================================================

    private static FormControl Control(string id, int x, int y, int w, int h, params FormControl[] children)
    {
        var control = new FormControl
        {
            Kind = children.Length > 0 ? "Panel" : "Button",
            Id = id,
            Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h }
        };

        control.Children.AddRange(children);
        return control;
    }

    private static FormDocument FormWith(params FormControl[] controls)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "HitForm" };
        form.Controls.AddRange(controls);
        return form;
    }

    [Test]
    public void HitTest_FindsTheControlUnderThePoint()
    {
        var form = FormWith(Control("btn", 20, 30, 100, 40));
        var t = new FormCanvasTransform();

        Assert.Multiple(() =>
        {
            Assert.That(t.HitTest(form, new Point(25, 35))?.Id, Is.EqualTo("btn"));
            Assert.That(t.HitTest(form, new Point(5, 5)), Is.Null, "outside every control");
        });
    }

    [Test]
    public void HitTest_UsesTheSameMappingAsRendering_AtAnyZoomAndPan()
    {
        // ⛔⛔ The regression this whole type exists to prevent. If hit-testing carried its own
        // copy of the mapping, this would pass at zoom 1 and fail at 2.5 — and the form would look
        // perfectly correct the entire time.
        var form = FormWith(Control("btn", 20, 30, 100, 40));
        var t = new FormCanvasTransform(2.5, new Vector(-40, 60));

        // The centre of the button as RENDERING would place it.
        var drawn = t.ToCanvas(new Rect(20, 30, 100, 40));
        var centre = drawn.Center;

        Assert.That(t.HitTest(form, centre)?.Id, Is.EqualTo("btn"),
            "a click at the centre of where the control was DRAWN must select that control");
    }

    [Test]
    public void HitTest_PrefersTheTopmostControl_WhichIsTheLastInDocumentOrder()
    {
        // Document order is z-order, so a later sibling paints over an earlier one. Returning the
        // first match makes an overlapped control unselectable from the front, which reads as "the
        // canvas ignores my clicks".
        var form = FormWith(
            Control("under", 0, 0, 100, 100),
            Control("over", 40, 40, 100, 100));

        Assert.That(new FormCanvasTransform().HitTest(form, new Point(50, 50))?.Id, Is.EqualTo("over"));
    }

    [Test]
    public void HitTest_PositionsAChildRelativeToItsContainer()
    {
        // ⛔⛔ A child's X/Y are relative to ITS CONTAINER, not to the form. Treating them as
        // absolute puts every control inside a Panel at the wrong place the moment the Panel is
        // not at the origin — and the catalog ships Panel and GroupBox as containers, so this is
        // ordinary use, not a corner case.
        var form = FormWith(
            Control("pnl", 100, 100, 200, 200,
                Control("inner", 10, 10, 50, 50)));

        var t = new FormCanvasTransform();

        Assert.Multiple(() =>
        {
            Assert.That(t.HitTest(form, new Point(115, 115))?.Id, Is.EqualTo("inner"),
                "the child sits at 110,110 in form space — container origin plus its own offset");
            Assert.That(t.HitTest(form, new Point(105, 105))?.Id, Is.EqualTo("pnl"),
                "inside the panel but outside the child");
            Assert.That(t.HitTest(form, new Point(15, 15)), Is.Null,
                "the child's own coordinates are NOT form coordinates");
        });
    }

    [Test]
    public void HitTest_IgnoresAControlWithNoPixelGeometry()
    {
        // A web document uses grid geometry, which this canvas does not place. A control with no
        // geometry must not become a zero-size target sitting at the origin.
        var form = FormWith(new FormControl { Kind = "Button", Id = "nowhere" });

        Assert.That(new FormCanvasTransform().HitTest(form, new Point(0, 0)), Is.Null);
    }

    [Test]
    public void Layout_ReportsContainersBeforeTheirChildren_WithAbsoluteBounds()
    {
        // Render draws in this order, so a container must come first or it paints over its own
        // children.
        var form = FormWith(
            Control("pnl", 100, 100, 200, 200,
                Control("inner", 10, 10, 50, 50)));

        var laid = FormCanvasTransform.Layout(form).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(laid.Select(x => x.Control.Id), Is.EqualTo(new[] { "pnl", "inner" }));
            Assert.That(laid[1].Bounds, Is.EqualTo(new Rect(110, 110, 50, 50)));
        });
    }

    // ==================================================================
    // Resize handles — grabbed in CANVAS space, deliberately
    // ==================================================================

    [Test]
    public void APointOnACorner_GrabsThatCorner()
    {
        var bounds = new Rect(100, 100, 80, 40);

        Assert.Multiple(() =>
        {
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(100, 100)),
                Is.EqualTo(FormResizeHandle.TopLeft));
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(180, 140)),
                Is.EqualTo(FormResizeHandle.BottomRight));
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(180, 100)),
                Is.EqualTo(FormResizeHandle.TopRight));
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(100, 140)),
                Is.EqualTo(FormResizeHandle.BottomLeft));
        });
    }

    [Test]
    public void APointOnAnEdgeMidpoint_GrabsThatEdge()
    {
        var bounds = new Rect(100, 100, 80, 40);

        Assert.Multiple(() =>
        {
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(140, 100)),
                Is.EqualTo(FormResizeHandle.Top));
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(140, 140)),
                Is.EqualTo(FormResizeHandle.Bottom));
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(100, 120)),
                Is.EqualTo(FormResizeHandle.Left));
            Assert.That(FormCanvasTransform.HandleAt(bounds, new Point(180, 120)),
                Is.EqualTo(FormResizeHandle.Right));
        });
    }

    [Test]
    public void APointInTheMiddle_GrabsNoHandle()
    {
        // The middle is a MOVE, not a resize. Returning a handle here would make dragging the body
        // of a control silently stretch it.
        Assert.That(FormCanvasTransform.HandleAt(new Rect(100, 100, 80, 40), new Point(140, 120)),
            Is.EqualTo(FormResizeHandle.None));
    }

    [Test]
    public void APointWellOutsideTheControl_GrabsNoHandle()
    {
        Assert.That(FormCanvasTransform.HandleAt(new Rect(100, 100, 80, 40), new Point(300, 300)),
            Is.EqualTo(FormResizeHandle.None));
    }

    [Test]
    public void AHandleIsGrabbableJustOutsideTheControl()
    {
        // ⚠ Handles straddle the border — half in, half out — which is what makes a thin control
        // resizable at all. A handle entirely inside a 23-pixel-high Button would overlap its own
        // opposite edge.
        Assert.That(FormCanvasTransform.HandleAt(new Rect(100, 100, 80, 40), new Point(97, 97)),
            Is.EqualTo(FormResizeHandle.TopLeft));
    }

    [Test]
    public void HandlesAreTheSameSizeOnScreenAtEveryZoom()
    {
        // ⛔⛔ THE reason HandleAt takes CANVAS coordinates rather than form ones. A handle sized in
        // form pixels shrinks with the form: at the zoom the canvas picks to fit an 800x450 form
        // into a docked panel, a 6-pixel handle becomes barely two physical pixels and the user
        // cannot hit it — the control simply stops being resizable, with nothing on screen to
        // explain why. The bounds passed in are already in canvas space, so the grab area is fixed.
        var tiny = new Rect(10, 10, 12, 6);       // a control drawn small because the form is zoomed out
        var large = new Rect(10, 10, 400, 200);   // the same control at 1:1

        Assert.Multiple(() =>
        {
            Assert.That(FormCanvasTransform.HandleAt(tiny, new Point(10, 10)),
                Is.EqualTo(FormResizeHandle.TopLeft));
            Assert.That(FormCanvasTransform.HandleAt(large, new Point(10, 10)),
                Is.EqualTo(FormResizeHandle.TopLeft));
        });
    }
}
