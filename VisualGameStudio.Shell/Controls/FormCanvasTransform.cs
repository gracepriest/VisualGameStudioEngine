using Avalonia;
using BasicLang.Forms;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Shell.Controls;

/// <summary>
/// The <b>one</b> mapping between form coordinates and canvas pixels, used by rendering,
/// hit-testing and selection alike.
///
/// <para>⛔⛔ <b>One object, deliberately — this is the whole reason the type exists.</b>
/// <c>MinimapControl</c> hand-duplicates its forward/inverse transform at three separate sites
/// (<c>MinimapControl.axaml.cs</c> around <c>:386</c>, <c>:439</c> and <c>:835</c>), and the copies
/// have already drifted. A drifted copy here does not look broken: the form renders perfectly and
/// clicks land on the wrong control, or on nothing, and only at some zoom levels. There is no
/// visual symptom to notice and no exception to catch, which is exactly why the mapping is one
/// object with tests rather than three expressions with none.</para>
///
/// <para>Immutable. Zooming or panning produces a new transform, so a half-updated one cannot be
/// observed by a render pass that is already running.</para>
/// </summary>
public sealed class FormCanvasTransform
{
    /// <summary>Below this, a form is a smudge; above it, one pixel of form is a tile.</summary>
    public const double MinZoom = 0.1;
    public const double MaxZoom = 8.0;

    public FormCanvasTransform(double zoom = 1.0, Vector pan = default)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        Pan = pan;
    }

    /// <summary>Canvas pixels per form pixel. Never zero — dividing by it is the inverse map.</summary>
    public double Zoom { get; }

    /// <summary>Canvas-space offset of the form's origin.</summary>
    public Vector Pan { get; }

    public FormCanvasTransform WithZoom(double zoom) => new(zoom, Pan);

    public FormCanvasTransform WithPan(Vector pan) => new(Zoom, pan);

    /// <summary>
    /// Zooms about a fixed canvas point — the gesture a mouse wheel performs.
    ///
    /// <para>⚠ The point under the cursor must not move. Zooming about the origin instead is the
    /// classic mistake: the form slides away under the pointer and the user chases it.</para>
    /// </summary>
    public FormCanvasTransform ZoomAbout(Point canvasAnchor, double zoom)
    {
        var formAnchor = ToForm(canvasAnchor);
        var zoomed = new FormCanvasTransform(zoom, Pan);

        // Re-pan so formAnchor maps back to canvasAnchor under the new zoom.
        var moved = zoomed.ToCanvas(formAnchor);
        return zoomed.WithPan(zoomed.Pan + (canvasAnchor - moved));
    }

    // ==================================================================
    // The mapping
    // ==================================================================

    public Point ToCanvas(Point form) => new((form.X * Zoom) + Pan.X, (form.Y * Zoom) + Pan.Y);

    public Point ToForm(Point canvas) => new((canvas.X - Pan.X) / Zoom, (canvas.Y - Pan.Y) / Zoom);

    public Rect ToCanvas(Rect form) => new(ToCanvas(form.TopLeft), new Size(form.Width * Zoom, form.Height * Zoom));

    public Rect ToForm(Rect canvas) => new(ToForm(canvas.TopLeft), new Size(canvas.Width / Zoom, canvas.Height / Zoom));

    // ==================================================================
    // Hit-testing — the same mapping, so it cannot disagree with what was drawn
    // ==================================================================

    /// <summary>
    /// The control under <paramref name="canvasPoint"/>, or null.
    ///
    /// <para>⛔ <b>Topmost wins, and topmost is the LAST match in document order</b> — document
    /// order is z-order, so a later sibling paints over an earlier one. Returning the first match
    /// makes overlapping controls unselectable from the front, which reads as "the canvas ignores
    /// my clicks" rather than as an ordering bug.</para>
    ///
    /// <para>⛔ A child's position is relative to ITS CONTAINER, not to the form. Hit-testing
    /// against absolute coordinates puts every control inside a Panel at the wrong place the moment
    /// the Panel is not at the origin — and the catalog ships Panel and GroupBox as containers, so
    /// this is reachable from ordinary use rather than a corner case.</para>
    /// </summary>
    public FormControl? HitTest(FormDocument document, Point canvasPoint)
    {
        ArgumentNullException.ThrowIfNull(document);
        return HitTest(document.Controls, ToForm(canvasPoint), new Point(0, 0));
    }

    private static FormControl? HitTest(
        IReadOnlyList<FormControl> controls, Point formPoint, Point containerOrigin)
    {
        for (var i = controls.Count - 1; i >= 0; i--)
        {
            var control = controls[i];
            var bounds = BoundsOf(control, containerOrigin);
            if (bounds == null || !bounds.Value.Contains(formPoint))
            {
                continue;
            }

            // A container's children sit on top of it, and are positioned relative to it.
            var child = HitTest(control.Children, formPoint, bounds.Value.TopLeft);
            return child ?? control;
        }

        return null;
    }

    /// <summary>
    /// Half the side of a resize handle's grab area, in CANVAS pixels. The handle straddles the
    /// border, so this much of it lies outside the control and this much inside.
    /// </summary>
    public const double HandleReach = 4;

    /// <summary>
    /// Which handle of <paramref name="canvasBounds"/> the point grabbed, or
    /// <see cref="FormResizeHandle.None"/> for the body (a move) or a miss.
    ///
    /// <para>⛔⛔ Takes CANVAS coordinates, and that is the whole design. A grab area measured in
    /// FORM pixels shrinks with the form: the canvas zooms to fit, so an 800x450 form in a docked
    /// panel draws at well under 1:1 and a handle sized in form pixels becomes one or two physical
    /// pixels. The control silently stops being resizable, with nothing on screen to explain why
    /// and nothing failing anywhere. In canvas space the grab area is the same size on screen at
    /// every zoom.</para>
    ///
    /// <para>⚠ Corners are tested before edges. They overlap, and a corner is the more specific
    /// gesture — testing edges first makes the corners unreachable, so a control can be stretched
    /// in one direction at a time but never scaled.</para>
    /// </summary>
    public static FormResizeHandle HandleAt(Rect canvasBounds, Point canvasPoint)
    {
        var onLeft = Math.Abs(canvasPoint.X - canvasBounds.X) <= HandleReach;
        var onRight = Math.Abs(canvasPoint.X - canvasBounds.Right) <= HandleReach;
        var onTop = Math.Abs(canvasPoint.Y - canvasBounds.Y) <= HandleReach;
        var onBottom = Math.Abs(canvasPoint.Y - canvasBounds.Bottom) <= HandleReach;

        var withinX = canvasPoint.X >= canvasBounds.X - HandleReach &&
                      canvasPoint.X <= canvasBounds.Right + HandleReach;
        var withinY = canvasPoint.Y >= canvasBounds.Y - HandleReach &&
                      canvasPoint.Y <= canvasBounds.Bottom + HandleReach;

        if (!withinX || !withinY)
        {
            return FormResizeHandle.None;
        }

        return (onLeft, onRight, onTop, onBottom) switch
        {
            (true, _, true, _) => FormResizeHandle.TopLeft,
            (_, true, true, _) => FormResizeHandle.TopRight,
            (true, _, _, true) => FormResizeHandle.BottomLeft,
            (_, true, _, true) => FormResizeHandle.BottomRight,
            (true, _, _, _) => FormResizeHandle.Left,
            (_, true, _, _) => FormResizeHandle.Right,
            (_, _, true, _) => FormResizeHandle.Top,
            (_, _, _, true) => FormResizeHandle.Bottom,
            _ => FormResizeHandle.None
        };
    }

    /// <summary>
    /// The innermost CONTAINER a drop at <paramref name="formPoint"/> lands in, with its origin in
    /// form space — or null for the form surface itself.
    ///
    /// <para>⛔⛔ Lives here, beside <see cref="HitTest"/>, and walks the tree the same way for the
    /// same reason the transform is one object: if placement decided containment differently from
    /// selection, you would drop a Button onto a Panel the canvas agrees you are over and it would
    /// land on the form behind it — or worse, the reverse. Two rules, one picture, no symptom.</para>
    ///
    /// <para>⚠ A NON-container under the point swallows it: dropping on a Button means dropping on
    /// the form at that spot, not into whatever sits behind the Button. Walking past it to a
    /// container underneath would nest controls into a Panel the user cannot see at that point.</para>
    /// </summary>
    /// <param name="ignore">
    /// A control to skip, together with everything inside it.
    ///
    /// <para>⛔⛔ Two separate reasons, both load-bearing while DRAGGING. First, the pointer is over
    /// the dragged control — that is what dragging is — so without skipping it a Button swallows
    /// its own point and reports "no container here", and nothing can ever be dragged into
    /// anything. Second, and worse: a Panel whose target is itself or one of its own descendants
    /// makes a LOOP in the tree, and every walker in this feature recurses — the writer, the canvas
    /// layout, the region writer, <c>AllControls</c>. That is a stack overflow that takes the IDE
    /// down with no diagnostic, so the illegal targets are excluded from the search rather than
    /// detected after the fact.</para>
    /// </param>
    public static (FormControl Container, Point Origin)? ContainerAt(
        FormDocument document, Point formPoint, FormControl? ignore = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ContainerAt(document.Controls, formPoint, new Point(0, 0), ignore);
    }

    private static (FormControl, Point)? ContainerAt(
        IReadOnlyList<FormControl> controls, Point formPoint, Point containerOrigin, FormControl? ignore)
    {
        // Topmost first, exactly as HitTest does — document order is z-order.
        for (var i = controls.Count - 1; i >= 0; i--)
        {
            var control = controls[i];
            if (ReferenceEquals(control, ignore))
            {
                continue;
            }

            var bounds = BoundsOf(control, containerOrigin);
            if (bounds == null || !bounds.Value.Contains(formPoint))
            {
                continue;
            }

            if (control.Definition?.IsContainer != true)
            {
                return null;
            }

            var nested = ContainerAt(control.Children, formPoint, bounds.Value.TopLeft, ignore);
            return nested ?? (control, bounds.Value.TopLeft);
        }

        return null;
    }

    /// <summary>
    /// A control's rectangle in FORM space, or null when it carries no pixel geometry.
    ///
    /// <para>⚠ Null rather than an empty rect: a control with no geometry has no position, and an
    /// empty rect at the origin would silently make it a zero-size target sitting in the corner.
    /// Web documents use grid geometry, which this canvas does not place.</para>
    /// </summary>
    public static Rect? BoundsOf(FormControl control, Point containerOrigin)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control.Geometry is not PixelGeometry pixel)
        {
            return null;
        }

        return new Rect(
            containerOrigin.X + pixel.X,
            containerOrigin.Y + pixel.Y,
            Math.Max(0, pixel.Width),
            Math.Max(0, pixel.Height));
    }

    /// <summary>Every control with its FORM-space rectangle, containers before their children.</summary>
    public static IEnumerable<(FormControl Control, Rect Bounds)> Layout(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Layout(document.Controls, new Point(0, 0));
    }

    private static IEnumerable<(FormControl, Rect)> Layout(
        IReadOnlyList<FormControl> controls, Point containerOrigin)
    {
        foreach (var control in controls)
        {
            var bounds = BoundsOf(control, containerOrigin);
            if (bounds == null)
            {
                continue;
            }

            yield return (control, bounds.Value);

            foreach (var nested in Layout(control.Children, bounds.Value.TopLeft))
            {
                yield return nested;
            }
        }
    }
}
