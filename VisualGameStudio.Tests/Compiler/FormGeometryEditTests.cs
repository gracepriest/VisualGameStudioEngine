using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Moving and resizing a control — the arithmetic behind dragging one around the canvas.
///
/// <para>⛔ UI-free on purpose. A drag is a pointer state machine plus this; only the state machine
/// needs a running IDE, and it is the part no test here claims to cover. What a move or a resize
/// DOES to the geometry is decidable from numbers, and it is where the bugs live — an off-by-one in
/// a corner handle makes a control creep every time it is grabbed, which nobody notices until the
/// form no longer matches the mock-up.</para>
/// </summary>
[TestFixture]
public class FormGeometryEditTests
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
    // Move
    // ==================================================================

    [Test]
    public void MovingAControl_PutsItWhereItWasDropped()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        document.Controls.Add(button);

        var changed = FormGeometryEdit.MoveTo(document, button, 120, 64);

        Assert.That(changed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(120));
            Assert.That(Pixel(button).Y, Is.EqualTo(64));
        });
    }

    [Test]
    public void MovingAControl_DoesNotResizeIt()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.MoveTo(document, button, 120, 64);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).Width, Is.EqualTo(75));
            Assert.That(Pixel(button).Height, Is.EqualTo(23));
        });
    }

    [Test]
    public void AControlDraggedOffTheRightEdge_StopsAtIt()
    {
        var document = Document(width: 400, height: 300);
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.MoveTo(document, button, 390, 295);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(400 - 75));
            Assert.That(Pixel(button).Y, Is.EqualTo(300 - 23));
        });
    }

    [Test]
    public void AControlDraggedOffTheTopLeft_StopsAtTheOrigin()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.MoveTo(document, button, -40, -40);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(0));
            Assert.That(Pixel(button).Y, Is.EqualTo(0));
        });
    }

    [Test]
    public void AChildIsBoundedByItsPanel_NotByTheForm()
    {
        // ⛔ A child's X/Y are relative to its CONTAINER, so the limit is the container's size. Using
        // the form's would let a Button be dragged out of the Panel it belongs to and keep a
        // coordinate that puts it somewhere else entirely at run time.
        var document = Document(width: 400, height: 300);
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        panel.Children.Add(button);
        document.Controls.Add(panel);

        FormGeometryEdit.MoveTo(document, button, 300, 300);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(200 - 75));
            Assert.That(Pixel(button).Y, Is.EqualTo(150 - 23));
        });
    }

    [Test]
    public void AMoveThatChangesNothing_SaysSo()
    {
        // The canvas commits on release. A click that moved the pointer by nothing must not report
        // a change, or every stray click writes the document and marks the file dirty.
        var document = Document();
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveTo(document, button, 10, 10), Is.False);
    }

    [Test]
    public void AControlWithNoPixelGeometry_IsNotMoved()
    {
        // Web controls are positioned by the layout, not by X/Y. There is nothing to set.
        var document = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var button = new FormControl { Kind = "Button", Id = "Button1", Geometry = new GridGeometry() };
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveTo(document, button, 10, 10), Is.False);
    }

    // ==================================================================
    // Resize — one edge at a time
    // ==================================================================

    [Test]
    public void TheRightHandle_ChangesWidthOnly()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 20, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.Right, 25, 999);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(10));
            Assert.That(Pixel(button).Y, Is.EqualTo(20));
            Assert.That(Pixel(button).Width, Is.EqualTo(100));
            Assert.That(Pixel(button).Height, Is.EqualTo(23), "a horizontal handle ignores dy");
        });
    }

    [Test]
    public void TheBottomHandle_ChangesHeightOnly()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 20, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.Bottom, 999, 17);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).Width, Is.EqualTo(75), "a vertical handle ignores dx");
            Assert.That(Pixel(button).Height, Is.EqualTo(40));
        });
    }

    [Test]
    public void TheLeftHandle_MovesTheLeftEdgeAndKeepsTheRightOneStill()
    {
        // ⛔ The whole point of a left handle. Growing the width while leaving X alone is what a
        // careless implementation does, and it makes the control appear to jump right as you drag
        // its left edge left.
        var document = Document();
        var button = Control("Button", "Button1", 100, 20, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.Left, -20, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(80));
            Assert.That(Pixel(button).Width, Is.EqualTo(95));
            Assert.That(Pixel(button).X + Pixel(button).Width, Is.EqualTo(175), "the right edge held still");
        });
    }

    [Test]
    public void TheTopLeftHandle_MovesBothEdgesAtOnce()
    {
        var document = Document();
        var button = Control("Button", "Button1", 100, 100, 75, 40);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.TopLeft, -10, -10);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(90));
            Assert.That(Pixel(button).Y, Is.EqualTo(90));
            Assert.That(Pixel(button).Width, Is.EqualTo(85));
            Assert.That(Pixel(button).Height, Is.EqualTo(50));
        });
    }

    [Test]
    public void TheBottomRightHandle_GrowsWithoutMoving()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 20, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.BottomRight, 30, 12);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(10));
            Assert.That(Pixel(button).Y, Is.EqualTo(20));
            Assert.That(Pixel(button).Width, Is.EqualTo(105));
            Assert.That(Pixel(button).Height, Is.EqualTo(35));
        });
    }

    // ==================================================================
    // Resize — the limits
    // ==================================================================

    [Test]
    public void AControlCannotBeResizedToNothing()
    {
        // ⛔ A zero-size control is invisible on the canvas AND unclickable, so it cannot be undone
        // by pointer — it is gone until the user edits the XML by hand. A negative one would also
        // round-trip into geometry the running form rejects.
        var document = Document();
        var button = Control("Button", "Button1", 10, 20, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.BottomRight, -500, -500);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).Width, Is.EqualTo(FormGeometryEdit.MinimumSize));
            Assert.That(Pixel(button).Height, Is.EqualTo(FormGeometryEdit.MinimumSize));
        });
    }

    [Test]
    public void ATopHandleDraggedPastTheBottomEdge_StopsAtTheMinimum()
    {
        // The opposite edge is the anchor, so the moving edge has to stop short of it rather than
        // pass through and invert the rectangle.
        var document = Document();
        var button = Control("Button", "Button1", 10, 100, 75, 40);   // bottom edge at 140
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.Top, 0, 500);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).Height, Is.EqualTo(FormGeometryEdit.MinimumSize));
            Assert.That(Pixel(button).Y, Is.EqualTo(140 - FormGeometryEdit.MinimumSize));
            Assert.That(Pixel(button).Y + Pixel(button).Height, Is.EqualTo(140), "the bottom edge held still");
        });
    }

    [Test]
    public void AControlCannotBeResizedOffTheRightEdgeOfItsSurface()
    {
        var document = Document(width: 400, height: 300);
        var button = Control("Button", "Button1", 300, 20, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.Right, 500, 0);

        Assert.That(Pixel(button).X + Pixel(button).Width, Is.EqualTo(400));
    }

    [Test]
    public void ALeftHandleDraggedPastTheOrigin_StopsAtIt()
    {
        var document = Document();
        var button = Control("Button", "Button1", 0, 20, 75, 23);
        document.Controls.Add(button);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.Left, -50, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X, Is.EqualTo(0));
            Assert.That(Pixel(button).Width, Is.EqualTo(75), "the right edge still held still");
        });
    }

    [Test]
    public void AChildIsResizedWithinItsPanel()
    {
        var document = Document();
        var panel = Control("Panel", "Panel1", 50, 40, 200, 150);
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        panel.Children.Add(button);
        document.Controls.Add(panel);

        FormGeometryEdit.Resize(document, button, FormResizeHandle.BottomRight, 500, 500);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(button).X + Pixel(button).Width, Is.EqualTo(200));
            Assert.That(Pixel(button).Y + Pixel(button).Height, Is.EqualTo(150));
        });
    }

    [Test]
    public void AResizeThatChangesNothing_SaysSo()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 20, 75, 23);
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.Resize(document, button, FormResizeHandle.Right, 0, 0), Is.False);
    }

    [Test]
    public void TheNoneHandle_ChangesNothing()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 20, 75, 23);
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.Resize(document, button, FormResizeHandle.None, 50, 50), Is.False);
    }

    // ==================================================================
    // Web pages — a move changes the CELL, not a coordinate
    // ==================================================================

    private static FormDocument WebGrid()
        => new()
        {
            Target = FormTarget.Web,
            Name = "Page",
            Layout = new FormLayout
            {
                Kind = FormLayoutKind.Grid, Cols = "1fr,1fr", Rows = "1fr,1fr", Gap = "0px"
            }
        };

    private static FormControl InCell(string id, int col, int row, int colSpan = 1, int rowSpan = 1)
        => new()
        {
            Kind = "Button",
            Id = id,
            Geometry = new GridGeometry { Col = col, Row = row, ColSpan = colSpan, RowSpan = rowSpan }
        };

    [Test]
    public void DraggingAWebControl_MovesItToTheCellUnderThePointer()
    {
        // ⛔ The POINTER's cell, not the control's origin plus a delta. A grid control fills its
        // cell, so origin-plus-delta lands a half-cell away from where the user is pointing and the
        // target becomes a guess. On a grid you point AT the cell you want.
        var document = WebGrid();
        var button = InCell("btn", 0, 0);
        document.Controls.Add(button);

        var changed = FormGeometryEdit.MoveToCell(document, button, 300, 250);

        Assert.That(changed, Is.True);
        var grid = (GridGeometry)button.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(grid.Col, Is.EqualTo(1));
            Assert.That(grid.Row, Is.EqualTo(1));
        });
    }

    [Test]
    public void AWebMoveKeepsTheSpan()
    {
        var document = WebGrid();
        var button = InCell("btn", 0, 0, colSpan: 2, rowSpan: 1);
        document.Controls.Add(button);

        FormGeometryEdit.MoveToCell(document, button, 300, 250);

        var grid = (GridGeometry)button.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(grid.ColSpan, Is.EqualTo(2), "a move is not a resize");
            Assert.That(grid.RowSpan, Is.EqualTo(1));
        });
    }

    [Test]
    public void AWebMoveToTheSameCell_SaysNothingChanged()
    {
        var document = WebGrid();
        var button = InCell("btn", 1, 1);
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveToCell(document, button, 300, 250), Is.False);
    }

    [Test]
    public void AWebMoveOffThePage_LeavesTheControlWhereItWas()
    {
        // Dragging past the edge must not move the control to a cell that does not exist, and must
        // not be a silent no-op that loses the drag either — the geometry simply holds.
        var document = WebGrid();
        var button = InCell("btn", 1, 1);
        document.Controls.Add(button);

        var changed = FormGeometryEdit.MoveToCell(document, button, -50, -50);

        var grid = (GridGeometry)button.Geometry!;
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(grid.Col, Is.EqualTo(1));
            Assert.That(grid.Row, Is.EqualTo(1));
        });
    }

    [Test]
    public void AWebMove_DoesNotReorderTheDocument()
    {
        // ⚠ Unlike a WinForms reparent, a cell move changes no parent and no z-order. Reordering
        // would churn the file for nothing and move the control behind its neighbours.
        var document = WebGrid();
        var first = InCell("first", 0, 0);
        var second = InCell("second", 1, 0);
        document.Controls.Add(first);
        document.Controls.Add(second);

        FormGeometryEdit.MoveToCell(document, first, 300, 250);

        Assert.That(document.Controls, Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void APixelControl_IsNotMovedByCell()
    {
        var document = Document();
        var button = Control("Button", "Button1", 10, 10, 75, 23);
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveToCell(document, button, 300, 250), Is.False);
    }

    [Test]
    public void AFlowPage_HasNoCellsToMoveBetween()
    {
        var document = WebGrid();
        document.Layout!.Kind = FormLayoutKind.Flow;
        var button = InCell("btn", 0, 0);
        document.Controls.Add(button);

        Assert.That(FormGeometryEdit.MoveToCell(document, button, 300, 250), Is.False);
    }
}
