using Avalonia;
using BasicLang.Forms;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// Which part of a selected control the pointer grabbed. <see cref="None"/> is the body — a move.
/// </summary>
public enum FormResizeHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left
}

/// <summary>
/// Moving and resizing a control — the arithmetic a canvas drag performs.
///
/// <para>⛔ Both operations clamp to the control's own SURFACE: its container's box, or the form's
/// client size at the top level. Not tidiness — a control dragged outside its parent keeps a
/// coordinate that puts it somewhere else entirely when the program runs, and one resized to
/// nothing becomes invisible AND unclickable, so it cannot be recovered with the pointer that lost
/// it. The canvas has no undo yet, which makes both of those one-way trips.</para>
///
/// <para>⚠ Two move operations, and the difference matters. <see cref="MoveTo"/> takes the
/// control's OWN coordinates and keeps its parent; <see cref="MoveToForm"/> takes absolute form
/// coordinates and re-parents into whatever container is under them. A canvas drag uses the second,
/// because a drag can cross a Panel boundary and the coordinate space changes when it does.
/// <see cref="Resize"/> never reparents: a resize moves an edge, not the control.</para>
/// </summary>
public static class FormGeometryEdit
{
    /// <summary>
    /// The smallest a control may be dragged to, in form pixels. Below roughly this, a control is
    /// too small to grab a handle on, so the resize that shrank it cannot be reversed by pointer.
    /// </summary>
    public const int MinimumSize = 8;

    /// <summary>
    /// Moves a control to a position in ITS OWN coordinate space — relative to its container, which
    /// is what <see cref="PixelGeometry"/> stores. Returns whether anything changed.
    /// </summary>
    public static bool MoveTo(FormDocument document, FormControl control, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(control);

        if (control.Geometry is not PixelGeometry pixel)
        {
            return false;
        }

        var (surfaceWidth, surfaceHeight) = SurfaceFor(document, control);

        var newX = Clamp(x, pixel.Width, surfaceWidth);
        var newY = Clamp(y, pixel.Height, surfaceHeight);

        if (newX == pixel.X && newY == pixel.Y)
        {
            return false;
        }

        pixel.X = newX;
        pixel.Y = newY;
        return true;
    }

    /// <summary>
    /// Moves a control to an ABSOLUTE point in form space, re-parenting it into whatever container
    /// is there — or out of the one it is in. Returns whether anything changed.
    ///
    /// <para>⛔⛔ Form space, not the control's own space, and that is the whole reason this exists
    /// beside <see cref="MoveTo"/>. A child's X/Y are relative to its container, so the same place
    /// on screen is different numbers depending on whose child the control is — and a drag that
    /// crosses a Panel boundary changes that mid-gesture. Tracking the drag in absolute coordinates
    /// and converting ONCE, into whichever container the control ended up in, is what keeps the
    /// control under the pointer across the boundary. Doing it the other way round draws correctly
    /// for the rest of the drag and puts the control somewhere else when the program runs.</para>
    ///
    /// <para>⚠ The target search excludes the dragged control and everything inside it — see
    /// <see cref="FormCanvasTransform.ContainerAt"/>. Without that a control hides its target from
    /// itself, and a container dropped into its own subtree makes a loop that every recursive
    /// walker in this feature follows off the end of the stack.</para>
    /// </summary>
    public static bool MoveToForm(FormDocument document, FormControl control, int formX, int formY)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(control);

        if (control.Geometry is not PixelGeometry pixel)
        {
            return false;
        }

        var target = FormCanvasTransform.ContainerAt(document, new Point(formX, formY), ignore: control);
        var newParent = target?.Container;
        var origin = target?.Origin ?? new Point(0, 0);

        var currentParent = ParentOf(document, control);
        var reparented = !ReferenceEquals(currentParent, newParent);

        var (surfaceWidth, surfaceHeight) = SurfaceOf(document, newParent);
        var newX = Clamp((int)(formX - origin.X), pixel.Width, surfaceWidth);
        var newY = Clamp((int)(formY - origin.Y), pixel.Height, surfaceHeight);

        if (!reparented && newX == pixel.X && newY == pixel.Y)
        {
            return false;
        }

        if (reparented)
        {
            // ⚠ Remove THEN add, and append: the control arrives on top in its new parent, which is
            // where a thing you just dropped belongs. Document order is z-order.
            (currentParent?.Children ?? document.Controls).Remove(control);
            (newParent?.Children ?? document.Controls).Add(control);
        }

        pixel.X = newX;
        pixel.Y = newY;
        return true;
    }

    /// <summary>
    /// Moves a web control into the grid cell under a form-space point. Returns whether it moved.
    ///
    /// <para>⛔ The POINTER's cell, not the control's origin plus a drag delta. A grid control fills
    /// its cell, so origin-plus-delta lands a half-cell from where the user is pointing and the
    /// target becomes a guess — on a grid you point AT the cell you want. That is also why this is
    /// a separate operation from <see cref="MoveToForm"/> rather than a branch inside it: the two
    /// take different points and mean different things.</para>
    ///
    /// <para>⚠ Changes the cell and nothing else — not the span, not document order. A cell move
    /// has no parent to change and no z-order to churn, unlike a WinForms reparent.</para>
    /// </summary>
    public static bool MoveToCell(FormDocument document, FormControl control, int formX, int formY)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(control);

        if (control.Geometry is not GridGeometry grid || document.Layout?.Kind != FormLayoutKind.Grid)
        {
            return false;
        }

        var cell = FormGridLayout.CellAt(
            document.Layout, FormCanvasTransform.SurfaceSize(document), new Point(formX, formY));

        // ⚠ Off the page holds the control where it is. Snapping it to the nearest edge cell would
        // move it somewhere the user never pointed at, and they would have to undo to find out.
        if (cell == null || (cell.Value.Col == grid.Col && cell.Value.Row == grid.Row))
        {
            return false;
        }

        grid.Col = cell.Value.Col;
        grid.Row = cell.Value.Row;
        return true;
    }

    /// <summary>
    /// Drags one handle of a control by (<paramref name="dx"/>, <paramref name="dy"/>) form pixels.
    /// Returns whether anything changed.
    /// </summary>
    public static bool Resize(
        FormDocument document, FormControl control, FormResizeHandle handle, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(control);

        if (handle == FormResizeHandle.None || control.Geometry is not PixelGeometry pixel)
        {
            return false;
        }

        var (surfaceWidth, surfaceHeight) = SurfaceFor(document, control);

        // The edges the handle does NOT touch are anchors and must not move. Working in edges
        // rather than in position-plus-size is what keeps that true: with X and Width, a left-edge
        // drag has to change both in opposite directions and any clamp applied to one of them
        // silently drags the other edge along.
        var left = pixel.X;
        var top = pixel.Y;
        var right = pixel.X + pixel.Width;
        var bottom = pixel.Y + pixel.Height;

        if (MovesLeftEdge(handle))
        {
            left = Math.Clamp(left + dx, 0, right - MinimumSize);
        }

        if (MovesRightEdge(handle))
        {
            right = Math.Clamp(right + dx, left + MinimumSize, Math.Max(left + MinimumSize, surfaceWidth));
        }

        if (MovesTopEdge(handle))
        {
            top = Math.Clamp(top + dy, 0, bottom - MinimumSize);
        }

        if (MovesBottomEdge(handle))
        {
            bottom = Math.Clamp(bottom + dy, top + MinimumSize, Math.Max(top + MinimumSize, surfaceHeight));
        }

        if (left == pixel.X && top == pixel.Y &&
            right - left == pixel.Width && bottom - top == pixel.Height)
        {
            return false;
        }

        pixel.X = left;
        pixel.Y = top;
        pixel.Width = right - left;
        pixel.Height = bottom - top;
        return true;
    }

    private static bool MovesLeftEdge(FormResizeHandle handle) =>
        handle is FormResizeHandle.Left or FormResizeHandle.TopLeft or FormResizeHandle.BottomLeft;

    private static bool MovesRightEdge(FormResizeHandle handle) =>
        handle is FormResizeHandle.Right or FormResizeHandle.TopRight or FormResizeHandle.BottomRight;

    private static bool MovesTopEdge(FormResizeHandle handle) =>
        handle is FormResizeHandle.Top or FormResizeHandle.TopLeft or FormResizeHandle.TopRight;

    private static bool MovesBottomEdge(FormResizeHandle handle) =>
        handle is FormResizeHandle.Bottom or FormResizeHandle.BottomLeft or FormResizeHandle.BottomRight;

    private static int Clamp(int value, int size, int surface) =>
        Math.Max(0, Math.Min(value, surface - size));

    /// <summary>
    /// The box this control's coordinates are measured against: its container's, or the form's.
    /// The form fallbacks match <c>FormCanvasControl.Fit</c>'s, so a control cannot be clamped to a
    /// surface different from the one the canvas drew.
    /// </summary>
    private static (int Width, int Height) SurfaceFor(FormDocument document, FormControl control) =>
        SurfaceOf(document, ParentOf(document, control));

    /// <summary>The usable box inside a container, or the form's client size when there is none.</summary>
    private static (int Width, int Height) SurfaceOf(FormDocument document, FormControl? container)
    {
        if (container?.Geometry is PixelGeometry parent)
        {
            return (parent.Width, parent.Height);
        }

        var surface = FormCanvasTransform.SurfaceSize(document);
        return ((int)surface.Width, (int)surface.Height);
    }

    /// <summary>The control whose <c>Children</c> holds <paramref name="control"/>, or null for a root.
    /// Internal for the Type Here leave-rule (<c>CodeEditorDocumentViewModel.IsInside</c>).</summary>
    internal static FormControl? ParentOf(FormDocument document, FormControl control) =>
        document.AllControls().FirstOrDefault(c => c.Children.Contains(control));
}
