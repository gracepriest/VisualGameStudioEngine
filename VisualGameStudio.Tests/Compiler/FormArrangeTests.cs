using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 20: align and make-same-size over a multi-selection.
///
/// <para>⛔ <b>The primary never moves.</b> It is the reference — "align left" means "align to the
/// primary's left". A command that also moved the reference would make the result depend on which
/// control happened to be first in the list, and pressing it twice would give two different answers.</para>
///
/// <para>⛔⛔ <b>Pixel geometry only (D3).</b> A <c>.blwebform</c> lays out by CELL; its controls have
/// no X, Y, Width or Height to align. Unifying the two vocabularies into pixels is exactly what D3
/// rejects, so these commands refuse on a web form rather than inventing coordinates for it.</para>
/// </summary>
[TestFixture]
public class FormArrangeTests
{
    private static FormControl Pixel(string id, int x, int y, int w = 50, int h = 20) => new()
    {
        Kind = "Button",
        Id = id,
        Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h }
    };

    private static PixelGeometry G(FormControl control) => (PixelGeometry)control.Geometry!;

    /// <summary>A form with three controls at deliberately different positions and sizes.</summary>
    private static FormDocument Form(out FormControl a, out FormControl b, out FormControl c)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        a = Pixel("a", 10, 10, 50, 20);
        b = Pixel("b", 100, 60, 80, 40);
        c = Pixel("c", 200, 120, 30, 60);
        form.Controls.Add(a);
        form.Controls.Add(b);
        form.Controls.Add(c);
        return form;
    }

    private static bool Apply(FormDocument form, FormArrangeKind kind, params FormControl[] controls) =>
        FormArrange.Apply(form, kind, controls, controls[^1]);

    // ==================================================================
    // Align
    // ==================================================================

    [Test]
    public void AlignLeftPutsEveryLeftEdgeOnThePrimarys()
    {
        var form = Form(out var a, out var b, out var c);

        Apply(form, FormArrangeKind.AlignLeft, a, b, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).X, Is.EqualTo(200));
            Assert.That(G(b).X, Is.EqualTo(200));
            Assert.That(G(c).X, Is.EqualTo(200), "the primary is already there");
        });
    }

    [Test]
    public void AlignLeftDoesNotChangeVerticalPosition()
    {
        var form = Form(out var a, out _, out var c);

        Apply(form, FormArrangeKind.AlignLeft, a, c);

        Assert.That(G(a).Y, Is.EqualTo(10));
    }

    /// <summary>⚠ Right alignment is about EDGES, so a wider control moves further.</summary>
    [Test]
    public void AlignRightPutsEveryRightEdgeOnThePrimarys()
    {
        var form = Form(out var a, out var b, out var c);

        // primary c: X=200 W=30 → right edge 230
        Apply(form, FormArrangeKind.AlignRight, a, b, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).X + G(a).Width, Is.EqualTo(230));
            Assert.That(G(b).X + G(b).Width, Is.EqualTo(230));
            Assert.That(G(a).Width, Is.EqualTo(50), "aligning must not resize");
        });
    }

    [Test]
    public void AlignTopPutsEveryTopEdgeOnThePrimarys()
    {
        var form = Form(out var a, out var b, out var c);

        Apply(form, FormArrangeKind.AlignTop, a, b, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).Y, Is.EqualTo(120));
            Assert.That(G(b).Y, Is.EqualTo(120));
        });
    }

    [Test]
    public void AlignBottomPutsEveryBottomEdgeOnThePrimarys()
    {
        var form = Form(out var a, out var b, out var c);

        // primary c: Y=120 H=60 → bottom 180
        Apply(form, FormArrangeKind.AlignBottom, a, b, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).Y + G(a).Height, Is.EqualTo(180));
            Assert.That(G(b).Y + G(b).Height, Is.EqualTo(180));
        });
    }

    [Test]
    public void AlignCentresHorizontallyLinesUpVerticalCentrelines()
    {
        var form = Form(out var a, out _, out var c);

        // primary c: X=200 W=30 → centre 215. a is W=50 → X = 215 - 25 = 190
        Apply(form, FormArrangeKind.AlignCentresHorizontally, a, c);

        Assert.That(G(a).X, Is.EqualTo(190));
    }

    [Test]
    public void AlignMiddlesVerticallyLinesUpHorizontalCentrelines()
    {
        var form = Form(out var a, out _, out var c);

        // primary c: Y=120 H=60 → middle 150. a is H=20 → Y = 150 - 10 = 140
        Apply(form, FormArrangeKind.AlignMiddlesVertically, a, c);

        Assert.That(G(a).Y, Is.EqualTo(140));
    }

    // ==================================================================
    // Size
    // ==================================================================

    [Test]
    public void SameWidthTakesThePrimarysWidthAndLeavesHeight()
    {
        var form = Form(out var a, out _, out var c);

        Apply(form, FormArrangeKind.SameWidth, a, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).Width, Is.EqualTo(30));
            Assert.That(G(a).Height, Is.EqualTo(20));
        });
    }

    [Test]
    public void SameHeightTakesThePrimarysHeightAndLeavesWidth()
    {
        var form = Form(out var a, out _, out var c);

        Apply(form, FormArrangeKind.SameHeight, a, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).Height, Is.EqualTo(60));
            Assert.That(G(a).Width, Is.EqualTo(50));
        });
    }

    [Test]
    public void SameSizeTakesBoth()
    {
        var form = Form(out var a, out _, out var c);

        Apply(form, FormArrangeKind.SameSize, a, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).Width, Is.EqualTo(30));
            Assert.That(G(a).Height, Is.EqualTo(60));
        });
    }

    [Test]
    public void SizingDoesNotMoveAnything()
    {
        var form = Form(out var a, out _, out var c);

        Apply(form, FormArrangeKind.SameSize, a, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).X, Is.EqualTo(10));
            Assert.That(G(a).Y, Is.EqualTo(10));
        });
    }

    // ==================================================================
    // The primary
    // ==================================================================

    [Test]
    public void ThePrimaryIsNeverMovedOrResized()
    {
        var form = Form(out var a, out var b, out var c);
        var before = (G(c).X, G(c).Y, G(c).Width, G(c).Height);

        Apply(form, FormArrangeKind.AlignLeft, a, b, c);
        Apply(form, FormArrangeKind.SameSize, a, b, c);

        Assert.That((G(c).X, G(c).Y, G(c).Width, G(c).Height), Is.EqualTo(before));
    }

    // ==================================================================
    // Refusals and no-ops
    // ==================================================================

    /// <summary>⛔ D3: a web form's controls are in CELLS and have no edges to align.</summary>
    [Test]
    public void AWebFormIsRefusedRatherThanGivenInventedCoordinates()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "W" };
        var a = new FormControl { Kind = "Button", Id = "a", Geometry = new GridGeometry { Col = 1, Row = 1 } };
        var b = new FormControl { Kind = "Button", Id = "b", Geometry = new GridGeometry { Col = 3, Row = 2 } };
        form.Controls.Add(a);
        form.Controls.Add(b);

        var changed = FormArrange.Apply(form, FormArrangeKind.AlignLeft, new[] { a, b }, b);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(((GridGeometry)a.Geometry!).Col, Is.EqualTo(1), "nothing was touched");
        });
    }

    [Test]
    public void ASelectionOfOneChangesNothing()
    {
        var form = Form(out var a, out _, out _);

        var changed = FormArrange.Apply(form, FormArrangeKind.AlignLeft, new[] { a }, a);

        Assert.That(changed, Is.False);
    }

    /// <summary>⚠ So the host can skip writing the document and leave the form unmarked as dirty.</summary>
    [Test]
    public void AligningAlreadyAlignedControlsReportsNoChange()
    {
        var form = Form(out var a, out _, out var c);
        FormArrange.Apply(form, FormArrangeKind.AlignLeft, new[] { a, c }, c);

        var again = FormArrange.Apply(form, FormArrangeKind.AlignLeft, new[] { a, c }, c);

        Assert.That(again, Is.False);
    }

    /// <summary>
    /// ⛔⛔ X and Y are PARENT-RELATIVE. Setting two controls in different containers to the same X
    /// puts them in two different places on screen — the numbers match and the picture does not.
    /// Controls outside the primary's container are left alone.
    /// </summary>
    [Test]
    public void ControlsInAnotherContainerAreLeftAlone()
    {
        var form = Form(out var a, out _, out var c);
        var panel = new FormControl
        {
            Kind = "Panel",
            Id = "pnl",
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 300, Height = 200 }
        };
        var nested = Pixel("nested", 5, 5);
        panel.Children.Add(nested);
        form.Controls.Add(panel);

        FormArrange.Apply(form, FormArrangeKind.AlignLeft, new[] { a, nested, c }, c);

        Assert.Multiple(() =>
        {
            Assert.That(G(a).X, Is.EqualTo(200), "a shares the primary's container");
            Assert.That(G(nested).X, Is.EqualTo(5), "nested does not, so it is untouched");
        });
    }
}
