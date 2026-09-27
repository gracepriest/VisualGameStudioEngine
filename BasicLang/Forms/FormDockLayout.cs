namespace BasicLang.Forms;

/// <summary>A rectangle in form pixels, relative to its container's client origin.</summary>
public readonly record struct FormRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>The edge a docked thing takes — WinForms' DockStyle, minus None.</summary>
public enum FormDockEdge
{
    Top,
    Bottom,
    Left,
    Right,
    Fill
}

/// <summary>One docked thing, resolved.</summary>
/// <param name="Bounds">Where it sits, relative to its container's client origin.</param>
/// <param name="ContainerWidth">The client width it was docked within — the reference for its CSS insets.</param>
/// <param name="ContainerHeight">The client height it was docked within.</param>
public readonly record struct FormDockedBounds(
    FormControl Control, FormDockEdge Edge, FormRect Bounds, int ContainerWidth, int ContainerHeight);

/// <summary>Every docked thing in a document, and a lookup by control.</summary>
public sealed class FormDockLayoutResult
{
    private readonly Dictionary<FormControl, FormDockedBounds> _byControl;

    internal FormDockLayoutResult(List<FormDockedBounds> all)
    {
        All = all;
        _byControl = new Dictionary<FormControl, FormDockedBounds>(ReferenceEqualityComparer.Instance);
        foreach (var docked in all)
        {
            _byControl[docked.Control] = docked;
        }
    }

    /// <summary>Per sibling list in document order; a container's docked children after its siblings.</summary>
    public IReadOnlyList<FormDockedBounds> All { get; }

    public bool TryGet(FormControl control, out FormDockedBounds docked) => _byControl.TryGetValue(control, out docked);
}

/// <summary>
/// ⛔⛔ THE one answer to "where does every DOCKED thing sit" (spec 2026-09-27 §3, §4): the strips (a
/// <see cref="FormPlace.Docked"/> row's Dock PROPERTY) and the docked controls (<see cref="PixelGeometry.Dock"/>),
/// resolved as ONE sequence. The canvas (<c>FormCanvasTransform.Bands</c>/<c>BoundsOf</c>) and the page emitter
/// both call it — two copies of this stacking algebra is the <c>Tracks</c>/<c>ParseTracks</c> scar a third time.
///
/// <para>⛔ Docking order, in MODEL terms, stated this way on purpose given this repo's z-order history:
/// WinForms docks back-most first, and the model's DOCUMENT order is back-to-front ("last in the list is in
/// front", <see cref="FormDocument.BringToFront"/>) — so the FIRST in the document docks FIRST and takes the
/// outermost edge. A Dock=Top Panel that precedes the MenuStrip sits above it, on the canvas, on the page and in
/// WinForms alike.</para>
///
/// <para>⚠ A strip's band height is its row's <see cref="FormControlDef.DefaultHeight"/> (24/25/22), not a
/// measured content height (spec §3). ⚠ Fill takes what is left and does NOT consume it (WinForms'
/// DefaultLayout); what is left never goes negative. ⚠ A container's client area is taken to be its bounds —
/// a GroupBox's caption inset is a recorded gap (plan spec-claims #11). The WinForms reference harness is the
/// arbiter of all three.</para>
/// </summary>
public static class FormDockLayout
{
    private static readonly FormDockEdge[] Edges = Enum.GetValues<FormDockEdge>();

    /// <summary>Every docked thing in <paramref name="document"/>, at every depth, at its design size.</summary>
    public static FormDockLayoutResult Resolve(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var (width, height) = document.DesignSize;
        var all = new List<FormDockedBounds>();
        Walk(document.Controls, width, height, all);
        return new FormDockLayoutResult(all);
    }

    /// <summary>
    /// The docked siblings in <paramref name="siblings"/>, in document order, inside a client area of
    /// <paramref name="width"/>×<paramref name="height"/>. Undocked siblings are skipped and consume nothing.
    /// </summary>
    public static IReadOnlyList<FormDockedBounds> ResolveSiblings(
        IReadOnlyList<FormControl> siblings, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(siblings);

        var clientWidth = Math.Max(0, width);
        var clientHeight = Math.Max(0, height);
        var remaining = new FormRect(0, 0, clientWidth, clientHeight);
        var placed = new List<FormDockedBounds>();

        foreach (var control in siblings)
        {
            if (EdgeOf(control) is not { } edge)
            {
                continue;
            }

            var (ownWidth, ownHeight) = OwnSize(control);
            FormRect bounds;

            switch (edge)
            {
                case FormDockEdge.Top:
                    bounds = new FormRect(remaining.X, remaining.Y, remaining.Width, ownHeight);
                    remaining = new FormRect(
                        remaining.X, remaining.Y + ownHeight, remaining.Width, Math.Max(0, remaining.Height - ownHeight));
                    break;

                case FormDockEdge.Bottom:
                    bounds = new FormRect(remaining.X, remaining.Bottom - ownHeight, remaining.Width, ownHeight);
                    remaining = remaining with { Height = Math.Max(0, remaining.Height - ownHeight) };
                    break;

                case FormDockEdge.Left:
                    bounds = new FormRect(remaining.X, remaining.Y, ownWidth, remaining.Height);
                    remaining = new FormRect(
                        remaining.X + ownWidth, remaining.Y, Math.Max(0, remaining.Width - ownWidth), remaining.Height);
                    break;

                case FormDockEdge.Right:
                    bounds = new FormRect(remaining.Right - ownWidth, remaining.Y, ownWidth, remaining.Height);
                    remaining = remaining with { Width = Math.Max(0, remaining.Width - ownWidth) };
                    break;

                default:
                    // Fill: what is left, left as it is (WinForms' DefaultLayout does not consume it).
                    bounds = remaining;
                    break;
            }

            placed.Add(new FormDockedBounds(control, edge, bounds, clientWidth, clientHeight));
        }

        return placed;
    }

    /// <summary>
    /// The edge <paramref name="control"/> docks to, or null when it does not dock. ⛔ The one answer: a strip
    /// through <see cref="FormControl.IsDockedToBottom"/> (its Dock PROPERTY, row default included); a
    /// positioned control through <see cref="PixelGeometry.Dock"/> — trimmed, case-insensitive; "None" and an
    /// unknown name do not dock. Tray components and items never do.
    /// </summary>
    public static FormDockEdge? EdgeOf(FormControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        switch (control.Definition?.Place ?? FormPlace.Positioned)
        {
            case FormPlace.Docked:
                return control.IsDockedToBottom ? FormDockEdge.Bottom : FormDockEdge.Top;

            case FormPlace.Positioned when control.Geometry is PixelGeometry { Dock: { } dock }:
                var name = dock.Trim();
                foreach (var edge in Edges)
                {
                    if (string.Equals(edge.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return edge;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static void Walk(IReadOnlyList<FormControl> siblings, int width, int height, List<FormDockedBounds> all)
    {
        var docked = ResolveSiblings(siblings, width, height);
        all.AddRange(docked);

        foreach (var control in siblings)
        {
            // Only a POSITIONED container holds controls: a strip's children are items, placed by the canvas's
            // band layout and by the page's markup, never docked.
            if (control.Definition?.Place is not (null or FormPlace.Positioned) || control.Children.Count == 0)
            {
                continue;
            }

            var own = docked.FirstOrDefault(d => ReferenceEquals(d.Control, control));
            var (innerWidth, innerHeight) = own.Control != null
                ? (own.Bounds.Width, own.Bounds.Height)
                : OwnSize(control);

            Walk(control.Children, innerWidth, innerHeight, all);
        }
    }

    /// <summary>A strip's band height (its row's DefaultHeight — spec §3), or a control's stored size.</summary>
    private static (int Width, int Height) OwnSize(FormControl control) =>
        control.Definition?.Place == FormPlace.Docked
            ? (0, control.Definition.DefaultHeight)
            : control.Geometry is PixelGeometry pixel
                ? (Math.Max(0, pixel.Width), Math.Max(0, pixel.Height))
                : (0, 0);
}
