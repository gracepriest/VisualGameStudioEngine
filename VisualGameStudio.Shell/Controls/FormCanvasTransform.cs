using Avalonia;
using BasicLang.Forms;

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
