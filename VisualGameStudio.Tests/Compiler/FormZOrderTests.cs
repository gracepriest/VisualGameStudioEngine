using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 20: bring-to-front / send-to-back, and the one thing they depend on — that "front" means the
/// same thing in the document, on the canvas, and in the program that runs.
///
/// <para>⛔⛔ <b>The two targets disagree about z-order, natively and irreconcilably.</b> The DOM
/// paints in document order, so the LAST element is on top. WinForms is the exact opposite:
/// <c>Controls</c> index 0 is the TOP of the z-order (measured against the official API docs for
/// <c>GetChildIndex</c>, <c>SetChildIndex</c> and <c>BringToFront</c>, which say so three times), and
/// <c>Controls.Add</c> APPENDS — so the first control added ends up in front.</para>
///
/// <para>The document therefore has to pick one meaning and make each emitter honour it. It picks the
/// web's, because the web's is not negotiable: <b>last in the document is in FRONT</b>, which is also
/// what the canvas draws and hit-tests. The WinForms emitter adds siblings in REVERSE so that the
/// document's front-most control is added first and lands at index 0.</para>
///
/// <para>⚠ Without that reversal a "Bring to Front" button would send the control to the BACK in the
/// running program — a designer command doing the opposite of its own label, visible only when two
/// controls overlap.</para>
/// </summary>
[TestFixture]
public class FormZOrderTests
{
    private static FormControl Pixel(string id, int x = 0, int y = 0) => new()
    {
        Kind = "Button",
        Id = id,
        Geometry = new PixelGeometry { X = x, Y = y, Width = 60, Height = 20 }
    };

    private static FormDocument Form(out FormControl a, out FormControl b, out FormControl c)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        a = Pixel("a");
        b = Pixel("b");
        c = Pixel("c");
        form.Controls.Add(a);
        form.Controls.Add(b);
        form.Controls.Add(c);
        return form;
    }

    private static IEnumerable<string> Ids(IEnumerable<FormControl> controls) =>
        controls.Select(c => c.Id);

    // ==================================================================
    // The document operation
    // ==================================================================

    [Test]
    public void BringToFrontMovesAControlToTheEndOfItsList()
    {
        var form = Form(out var a, out _, out _);

        var changed = form.BringToFront(a);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(Ids(form.Controls), Is.EqualTo(new[] { "b", "c", "a" }));
        });
    }

    [Test]
    public void SendToBackMovesAControlToTheStartOfItsList()
    {
        var form = Form(out _, out _, out var c);

        var changed = form.SendToBack(c);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(Ids(form.Controls), Is.EqualTo(new[] { "c", "a", "b" }));
        });
    }

    [Test]
    public void TheOtherControlsKeepTheirRelativeOrder()
    {
        var form = Form(out var a, out _, out _);

        form.BringToFront(a);

        Assert.That(Ids(form.Controls).Take(2), Is.EqualTo(new[] { "b", "c" }));
    }

    /// <summary>⚠ So the host does not write the document and add an undo step for a no-op.</summary>
    [Test]
    public void BringingTheFrontControlToTheFrontReportsNoChange()
    {
        var form = Form(out _, out _, out var c);

        Assert.That(form.BringToFront(c), Is.False);
    }

    [Test]
    public void SendingTheBackControlToTheBackReportsNoChange()
    {
        var form = Form(out var a, out _, out _);

        Assert.That(form.SendToBack(a), Is.False);
    }

    /// <summary>
    /// ⛔ Reorders within the list the control actually LIVES in. Reordering
    /// <c>Document.Controls</c> unconditionally would silently do nothing for a control inside a
    /// Panel — the command would look broken for nested controls only, which is the same trap Delete
    /// had.
    /// </summary>
    [Test]
    public void AControlInsideAContainerIsReorderedWithinThatContainer()
    {
        var form = Form(out _, out _, out _);
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl",
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 200, Height = 100 }
        };
        var first = Pixel("first");
        var second = Pixel("second");
        panel.Children.Add(first);
        panel.Children.Add(second);
        form.Controls.Add(panel);

        var changed = form.BringToFront(first);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(Ids(panel.Children), Is.EqualTo(new[] { "second", "first" }));
            Assert.That(Ids(form.Controls), Is.EqualTo(new[] { "a", "b", "c", "pnl" }),
                "the top level is untouched");
        });
    }

    [Test]
    public void AControlNotInTheDocumentIsRefused()
    {
        var form = Form(out _, out _, out _);

        Assert.That(form.BringToFront(Pixel("stranger")), Is.False);
    }

    // ==================================================================
    // The canvas agrees
    // ==================================================================

    /// <summary>
    /// The picture and the document must mean the same thing: the control brought to the front is
    /// the one a click on the overlap selects.
    /// </summary>
    [Test]
    public void TheCanvasHitTestsTheFrontControlFirst()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        var under = Pixel("under", 10, 10);
        var over = Pixel("over", 10, 10);
        form.Controls.Add(under);
        form.Controls.Add(over);

        var transform = new FormCanvasTransform();

        Assert.That(transform.HitTest(form, new Avalonia.Point(20, 15)), Is.SameAs(over),
            "last in the document is in front");

        form.BringToFront(under);

        Assert.That(transform.HitTest(form, new Avalonia.Point(20, 15)), Is.SameAs(under));
    }

    // ==================================================================
    // ⛔⛔ The program that runs agrees — the whole point
    // ==================================================================

    /// <summary>
    /// ⛔⛔ <b>The correspondence that makes the command honest.</b> WinForms' <c>Controls</c> index 0
    /// is the TOP of the z-order and <c>Controls.Add</c> appends, so the document's FRONT-most
    /// control has to be added FIRST. Emitting in document order would put it last — at the highest
    /// index, the very back — and "Bring to Front" would visibly send it behind everything.
    /// </summary>
    [Test]
    public void TheWinFormsRegionAddsTheFrontControlFirst()
    {
        var form = Form(out _, out _, out var c);

        var written = RegionWriter.Write(
            "F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");

        Assert.That(written.Refused, Is.False);

        var text = written.Text;
        var frontAt = text.IndexOf("Controls.Add(c)", StringComparison.Ordinal);
        var middleAt = text.IndexOf("Controls.Add(b)", StringComparison.Ordinal);
        var backAt = text.IndexOf("Controls.Add(a)", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(frontAt, Is.GreaterThan(-1), "the front control must be added at all");
            Assert.That(frontAt, Is.LessThan(middleAt),
                "'c' is front-most in the document, so it must reach Controls index 0");
            Assert.That(middleAt, Is.LessThan(backAt));
        });
    }

    /// <summary>A container is still populated before it is added to its own parent.</summary>
    [Test]
    public void AContainerIsStillPopulatedBeforeItIsAdded()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl",
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 200, Height = 100 }
        };
        panel.Children.Add(Pixel("inner"));
        form.Controls.Add(panel);

        var written = RegionWriter.Write(
            "F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");

        var innerAt = written.Text.IndexOf("pnl.Controls.Add(inner)", StringComparison.Ordinal);
        var panelAt = written.Text.IndexOf("Me.Controls.Add(pnl)", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(innerAt, Is.GreaterThan(-1));
            Assert.That(panelAt, Is.GreaterThan(innerAt));
        });
    }
}
