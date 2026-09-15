using BasicLang.Forms;

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
/// <para>⚠ Neither operation reparents. Dragging a control out of a Panel moves it to the Panel's
/// edge and stops; it does not become a child of the form. Reparenting mid-drag changes which
/// coordinate space the control is in — every pixel of the drag would have to be re-based — and
/// silently rewrites the generated <c>Controls.Add</c> target. Worth doing, worth doing
/// deliberately, and not smuggled into this.</para>
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
    private static (int Width, int Height) SurfaceFor(FormDocument document, FormControl control)
    {
        if (ParentOf(document, control)?.Geometry is PixelGeometry parent)
        {
            return (parent.Width, parent.Height);
        }

        return (document.Width is > 0 ? document.Width.Value : 400,
                document.Height is > 0 ? document.Height.Value : 300);
    }

    private static FormControl? ParentOf(FormDocument document, FormControl control) =>
        document.AllControls().FirstOrDefault(c => c.Children.Contains(control));
}
