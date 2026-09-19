using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Dragging a control INTO or OUT OF a container.
///
/// <para>⛔⛔ The hard part is not the list surgery, it is the coordinate space. A child's X/Y are
/// relative to its container, so the same control at the same place on screen has DIFFERENT numbers
/// depending on whose child it is. A reparent that moves the control without re-basing them draws
/// correctly for the rest of the drag and puts the control somewhere else entirely when the program
/// runs — which is why the drag arithmetic is done in absolute FORM space and converted once, at
/// the end, into whichever container the control landed in.</para>
///
/// <para>⛔ The other hard part is cycles. A Panel dropped into itself — or into its own child —
/// makes a loop in the tree, and every walker in this feature recurses: the writer, the canvas
/// layout, the region writer, <c>AllControls</c>. The symptom is a stack overflow that takes the
/// IDE down with no diagnostic, so the dragged subtree is excluded from the search for a target
/// rather than checked for afterwards.</para>
/// </summary>
[TestFixture]
public class FormReparentTests
{
    private static FormDocument Document(int width = 400, int height = 300)
        => new() { Target = FormTarget.WinForms, Name = "F", Width = width, Height = height };

    private static FormControl Control(string kind, string id, int x, int y, int w, int h)
        => new()
        {
            Kind = kind,
            Id = id,
            Geometry = new PixelGeometry { X = x, Y = y, Width = w, Height = h }
        };

    private static PixelGeometry Pixel(FormControl control) => (PixelGeometry)control.Geometry!;

    // ==================================================================
    // Into a container
    // ==================================================================

    [Test]
    public void AControlDraggedOntoAPanel_BecomesItsChild()
    {
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        var button = Control("Button", "Button1", 10, 200, 75, 23);
        document.Controls.Add(panel);
        document.Controls.Add(button);

        FormGeometryEdit.MoveToForm(document, button, 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Children, Does.Contain(button));
            Assert.That(document.Controls, Does.Not.Contain(button));
        });
    }

    [Test]
    public void AControlDraggedOntoAPanel_KeepsItsPlaceOnScreen()
    {
        // ⛔ THE reparenting bug. Form point (100,90) inside a Panel at (50,40) is LOCAL (50,50).
        // Storing (100,90) leaves the control drawn where the pointer dropped it and running at
        // panel origin plus 100,90 — off the bottom-right of the Panel.
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        var button = Control("Button", "Button1", 10, 200, 75, 23);
        document.Controls.Add(panel);
        document.Controls.Add(button);

        FormGeometryEdit.MoveToForm(document, button, 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(50));
            Assert.That(Pixel(button).Y, Is.EqualTo(50));
        });
    }

    [Test]
    public void TheDraggedControl_DoesNotHideTheContainerFromItself()
    {
        // ⛔ The pointer is OVER the control being dragged — that is what dragging is. If the search
        // for a target sees the dragged control, a Button swallows its own point and reports "no
        // container here", so a control can never be dragged into anything.
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        var button = Control("Button", "Button1", 90, 80, 75, 23);   // already sitting over the Panel
        document.Controls.Add(panel);
        document.Controls.Add(button);

        FormGeometryEdit.MoveToForm(document, button, 100, 90);

        Assert.That(panel.Children, Does.Contain(button));
    }

    [Test]
    public void AReparentedControl_GoesOnTopInItsNewParent()
    {
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        var existing = Control("Label", "Label1", 0, 0, 50, 20);
        panel.Children.Add(existing);
        var button = Control("Button", "Button1", 10, 200, 75, 23);
        document.Controls.Add(panel);
        document.Controls.Add(button);

        FormGeometryEdit.MoveToForm(document, button, 100, 90);

        Assert.That(panel.Children[^1], Is.SameAs(button));
    }

    [Test]
    public void AReparentedControl_IsClampedToItsNewContainer()
    {
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 100, 60);
        var button = Control("Button", "Button1", 10, 200, 75, 23);
        document.Controls.Add(panel);
        document.Controls.Add(button);

        // Bottom-right corner of the Panel: the Button cannot fit past it.
        FormGeometryEdit.MoveToForm(document, button, 145, 95);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(100 - 75));
            Assert.That(Pixel(button).Y, Is.EqualTo(60 - 23));
        });
    }

    [Test]
    public void AContainerTakesItsChildrenWithIt()
    {
        var document = Document();
        var outer = Control("Panel", "Outer", 200, 20, 180, 200);
        var inner = Control("Panel", "Inner", 10, 10, 80, 60);
        var label = Control("Label", "Label1", 5, 5, 40, 20);
        inner.Children.Add(label);
        document.Controls.Add(outer);
        document.Controls.Add(inner);

        FormGeometryEdit.MoveToForm(document, inner, 210, 30);

        Assert.Multiple(() =>
        {
            Assert.That(outer.Children, Does.Contain(inner));
            Assert.That(inner.Children, Does.Contain(label), "the subtree moved as one");
            Assert.That(Pixel(label).X, Is.EqualTo(5), "a child's own coordinates are unchanged");
        });
    }

    // ==================================================================
    // Out of a container
    // ==================================================================

    [Test]
    public void AControlDraggedOffItsPanel_GoesBackToTheForm()
    {
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 100, 60);
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        panel.Children.Add(button);
        document.Controls.Add(panel);

        FormGeometryEdit.MoveToForm(document, button, 250, 200);

        Assert.Multiple(() =>
        {
            Assert.That(document.Controls, Does.Contain(button));
            Assert.That(panel.Children, Does.Not.Contain(button));
            Assert.That(Pixel(button).X, Is.EqualTo(250), "form-space coordinates now that the form is the parent");
            Assert.That(Pixel(button).Y, Is.EqualTo(200));
        });
    }

    // ==================================================================
    // ⛔⛔ Cycles — a loop in the tree is a stack overflow, not a wrong picture
    // ==================================================================

    [Test]
    public void APanelCannotBeDroppedIntoItself()
    {
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        document.Controls.Add(panel);

        // The pointer is inside the Panel's own rectangle — which it always is while dragging it.
        FormGeometryEdit.MoveToForm(document, panel, 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Children, Does.Not.Contain(panel));
            Assert.That(document.Controls, Does.Contain(panel));
            Assert.That(document.AllControls().Count(), Is.EqualTo(1), "still a tree, not a loop");
        });
    }

    [Test]
    public void APanelCannotBeDroppedIntoItsOwnChild()
    {
        var document = Document();
        var outer = Control("Panel", "Outer", 50, 40, 200, 150);
        var inner = Control("Panel", "Inner", 20, 20, 120, 100);
        outer.Children.Add(inner);
        document.Controls.Add(outer);

        // A point inside Inner, which is inside Outer: the only container there is Outer's own
        // descendant, so there is nowhere legal to go.
        FormGeometryEdit.MoveToForm(document, outer, 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(inner.Children, Is.Empty);
            Assert.That(document.Controls, Does.Contain(outer));
            Assert.That(outer.Children, Does.Contain(inner), "unchanged");
        });
    }

    // ==================================================================
    // Not reparenting
    // ==================================================================

    [Test]
    public void MovingWithinTheSameParent_DoesNotDisturbTheTree()
    {
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        var sibling = Control("Label", "Label1", 0, 120, 50, 20);
        panel.Children.Add(button);
        panel.Children.Add(sibling);
        document.Controls.Add(panel);

        FormGeometryEdit.MoveToForm(document, button, 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Children, Is.EqualTo(new[] { button, sibling }),
                "no z-order churn from a move that changed no parent");
            Assert.That(Pixel(button).X, Is.EqualTo(50));
            Assert.That(Pixel(button).Y, Is.EqualTo(50));
        });
    }

    [Test]
    public void AMoveToWhereItAlreadyIs_SaysNothingChanged()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveToForm(document, button, 10, 10), Is.False);
    }

    [Test]
    public void AControlWithNoPixelGeometry_IsNotReparented()
    {
        var document = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var button = new FormControl { Kind = "Button", Id = "Button1", Geometry = new GridGeometry() };
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveToForm(document, button, 10, 10), Is.False);
    }

    [Test]
    public void ADropOnANonContainer_LandsOnTheFormBehindIt()
    {
        // A Button is not a container: dragging one control over another must not nest them.
        var document = Document();
        var target = Control("Button", "Target", 50, 40, 200, 150);
        var dragged = Control("Label", "Label1", 10, 250, 50, 20);
        document.Controls.Add(target);
        document.Controls.Add(dragged);

        FormGeometryEdit.MoveToForm(document, dragged, 100, 90);

        Assert.Multiple(() =>
        {
            Assert.That(target.Children, Is.Empty);
            Assert.That(document.Controls, Does.Contain(dragged));
        });
    }
}
