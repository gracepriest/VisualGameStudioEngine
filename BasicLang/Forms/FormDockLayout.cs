namespace BasicLang.Forms;

/// <summary>
/// A rectangle in form pixels, relative to its container's client origin. Width and Height are never negative.
/// ⚠ X and Y CAN be negative, or lie past the container's far edge, after an overflow (a far-edge dock after an
/// overflowing one measures from the unclamped remainder, scope call S9) — hit-testing and clipping must not
/// assume the rectangle lies inside its container.
/// </summary>
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
/// <param name="Bounds">Where it sits, relative to its container's client origin. ⚠ After an overflow its X/Y may
/// be negative or past the container's far edge (see <see cref="FormRect"/>); its size never is.</param>
/// <param name="ContainerWidth">The client width it was docked within — the reference for its CSS insets.</param>
/// <param name="ContainerHeight">The client height it was docked within.</param>
public readonly record struct FormDockedBounds(
    FormControl Control, FormDockEdge Edge, FormRect Bounds, int ContainerWidth, int ContainerHeight);

/// <summary>Which picture of the form the resolver is asked for.</summary>
public enum FormDockMode
{
    /// <summary>The designer canvas: every control is shown and docked, hidden ones included.</summary>
    Designer,

    /// <summary>
    /// The running form (the page, and WinForms at run time): a control whose <c>Visible</c> is false
    /// (<see cref="FormControl.IsHidden"/>) takes no part in docking — WinForms' <c>ParticipatesInLayout</c> —
    /// so the next docked control closes the gap. It gets no bounds, consumes nothing, and its children are not
    /// resolved either (they cannot be seen).
    /// </summary>
    Runtime
}

/// <summary>Every docked thing in a document, and a lookup by control.</summary>
public sealed class FormDockLayoutResult
{
    private readonly Dictionary<FormControl, FormDockedBounds> _byControl;
    private readonly Dictionary<FormControl, (int Width, int Height)> _clientSizes;

    internal FormDockLayoutResult(
        List<FormDockedBounds> all, Dictionary<FormControl, (int Width, int Height)> clientSizes,
        (int Width, int Height) rootClientSize)
    {
        All = all;
        _clientSizes = clientSizes;
        RootClientSize = rootClientSize;
        _byControl = new Dictionary<FormControl, FormDockedBounds>(ReferenceEqualityComparer.Instance);
        foreach (var docked in all)
        {
            _byControl[docked.Control] = docked;
        }
    }

    /// <summary>
    /// Every docked thing. ⚠ The order is part of the contract: each sibling list's docked controls in DOCUMENT
    /// order (their docking order), and a container's children only after every docked control of the list the
    /// container is in — breadth per list, then depth, container by container in document order.
    /// </summary>
    public IReadOnlyList<FormDockedBounds> All { get; }

    /// <summary>The form's own client size — <see cref="FormDocument.DesignSize"/>, the root every top-level control docks in.</summary>
    public (int Width, int Height) RootClientSize { get; }

    public bool TryGet(FormControl control, out FormDockedBounds docked) => _byControl.TryGetValue(control, out docked);

    /// <summary>
    /// ⛔ THE one answer to "how big is the area this container lays its children out in": its resolved bounds
    /// when it docks, else its stored size. The resolver docks children in it, and the page emitter anchors
    /// children against it (<c>FormAssetEmitter.CanvasPlacement</c>) — one source, filled while resolving. False for a control that is not a
    /// pixel-positioned control of this document, or that Runtime mode skipped as hidden.
    /// </summary>
    public bool TryGetClientSize(FormControl container, out (int Width, int Height) size) =>
        _clientSizes.TryGetValue(container, out size);

    /// <summary><see cref="TryGetClientSize"/>, throwing when there is no answer.</summary>
    public (int Width, int Height) ClientSizeOf(FormControl container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return TryGetClientSize(container, out var size)
            ? size
            : throw new ArgumentException(
                $"'{container.Id}' has no client size here: it is not a pixel-positioned control of this document, " +
                "or it is hidden and the layout was resolved in Runtime mode.", nameof(container));
    }
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
/// DefaultLayout). ⚠ What is left is NOT clamped — WinForms subtracts each docked control's height/width from
/// it unclamped (<c>remainingBounds.Height -= element.Bounds.Height</c>), so after an overflowing Top a Bottom
/// still sits on the form's real bottom edge; only a size HANDED to a control is clamped at 0 (a Fill after an
/// overflow is 0-sized, never negative). ⚠ A container's client area is taken to be its bounds — a GroupBox's
/// caption inset and a bordered Panel's 1–2px are recorded gaps (plan spec-claims #11, Task 12 risks). The
/// WinForms reference harness is the arbiter of all of these (scope call S9: as read, not yet run).</para>
///
/// <para>⚠ Only a POSITIONED control with <see cref="PixelGeometry"/> has a client area, so only such a container's
/// children are resolved. A container with no pixel geometry (a Grid/Flow page's cell-placed Panel) does NOT have
/// its children resolved and has no <c>ClientSizeOf</c>. That is deliberate (changed on Task 6 review): its size
/// is not a number of pixels, and resolving its docked children against 0×0 gave meaningless bounds.</para>
///
/// <para>⛔⛔ MIRRORED in JavaScript by <see cref="FormDockScript.Core"/> (the page's run-time re-docking):
/// ResolveSiblings and Walk in Runtime mode. FormDockScriptTests runs both over one table under node — change them in
/// the SAME commit.</para>
/// </summary>
public static class FormDockLayout
{
    private static readonly FormDockEdge[] Edges = Enum.GetValues<FormDockEdge>();

    /// <summary>Every docked thing in <paramref name="document"/>, at every depth, at its design size.</summary>
    public static FormDockLayoutResult Resolve(FormDocument document, FormDockMode mode = FormDockMode.Designer)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.DesignSize;
        var all = new List<FormDockedBounds>();
        var clientSizes = new Dictionary<FormControl, (int Width, int Height)>(ReferenceEqualityComparer.Instance);
        Walk(document.Controls, root.Width, root.Height, mode, all, clientSizes);
        return new FormDockLayoutResult(all, clientSizes, root);
    }

    /// <summary>
    /// The docked siblings in <paramref name="siblings"/>, in document order, inside a client area of
    /// <paramref name="width"/>×<paramref name="height"/> (a negative size is taken as 0). Undocked siblings are
    /// skipped and consume nothing; in <see cref="FormDockMode.Runtime"/> so are hidden ones.
    /// </summary>
    public static IReadOnlyList<FormDockedBounds> ResolveSiblings(
        IReadOnlyList<FormControl> siblings, int width, int height, FormDockMode mode = FormDockMode.Designer)
    {
        ArgumentNullException.ThrowIfNull(siblings);

        var clientWidth = Math.Max(0, width);
        var clientHeight = Math.Max(0, height);

        // ⛔ UNCLAMPED on purpose (scope call S9): WinForms subtracts each docked control from the remaining
        // rectangle without a floor, so its far edges (Bottom, Right) stay on the container's real edges even
        // after an overflow. Only the sizes handed OUT below are clamped at 0.
        var remaining = new FormRect(0, 0, clientWidth, clientHeight);
        var placed = new List<FormDockedBounds>();

        foreach (var control in siblings)
        {
            if (!Participates(control, mode) || EdgeOf(control) is not { } edge)
            {
                continue;
            }

            var (ownWidth, ownHeight) = OwnSizeOf(control);
            var acrossWidth = Math.Max(0, remaining.Width);
            var acrossHeight = Math.Max(0, remaining.Height);
            FormRect bounds;

            switch (edge)
            {
                case FormDockEdge.Top:
                    bounds = new FormRect(remaining.X, remaining.Y, acrossWidth, ownHeight);
                    remaining = new FormRect(remaining.X, remaining.Y + ownHeight, remaining.Width, remaining.Height - ownHeight);
                    break;

                case FormDockEdge.Bottom:
                    bounds = new FormRect(remaining.X, remaining.Bottom - ownHeight, acrossWidth, ownHeight);
                    remaining = remaining with { Height = remaining.Height - ownHeight };
                    break;

                case FormDockEdge.Left:
                    bounds = new FormRect(remaining.X, remaining.Y, ownWidth, acrossHeight);
                    remaining = new FormRect(remaining.X + ownWidth, remaining.Y, remaining.Width - ownWidth, remaining.Height);
                    break;

                case FormDockEdge.Right:
                    bounds = new FormRect(remaining.Right - ownWidth, remaining.Y, ownWidth, acrossHeight);
                    remaining = remaining with { Width = remaining.Width - ownWidth };
                    break;

                default:
                    // Fill: what is left, left as it is (WinForms' DefaultLayout does not consume it) — but never
                    // a negative size handed to the control.
                    bounds = new FormRect(remaining.X, remaining.Y, acrossWidth, acrossHeight);
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

    /// <summary>
    /// Takes part in docking in <paramref name="mode"/>: always in the designer; at run time only when not hidden.
    /// </summary>
    private static bool Participates(FormControl control, FormDockMode mode) =>
        mode == FormDockMode.Designer || !control.IsHidden;

    private static void Walk(
        IReadOnlyList<FormControl> siblings, int width, int height, FormDockMode mode,
        List<FormDockedBounds> all, Dictionary<FormControl, (int Width, int Height)> clientSizes)
    {
        var docked = ResolveSiblings(siblings, width, height, mode);
        all.AddRange(docked);

        foreach (var control in siblings)
        {
            // Only a POSITIONED pixel control has a client area: a strip's children are items, placed by the
            // canvas's band layout and by the page's markup, never docked. A hidden control at run time has
            // none either — nothing inside it can be seen.
            if (!HasClientArea(control) || !Participates(control, mode))
            {
                continue;
            }

            // ⛔ The one rule for a container's client size (exposed as ClientSizeOf): resolved bounds when it
            // docks, else its stored size.
            var own = docked.FirstOrDefault(d => ReferenceEquals(d.Control, control));
            var inner = own.Control != null
                ? (own.Bounds.Width, own.Bounds.Height)
                : OwnSizeOf(control);
            clientSizes[control] = inner;

            if (control.Children.Count > 0)
            {
                Walk(control.Children, inner.Item1, inner.Item2, mode, all, clientSizes);
            }
        }
    }

    /// <summary>
    /// ⛔ The one answer to "what size does this thing dock at": a strip's band height (its row's
    /// <see cref="FormControlDef.DefaultHeight"/> — spec §3), or a control's stored size floored at 0. Public because
    /// the page's run-time reflow script (<see cref="FormDockScript"/>) must dock exactly what this resolver docks.
    /// </summary>
    public static (int Width, int Height) OwnSizeOf(FormControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.Definition?.Place == FormPlace.Docked
            ? (0, control.Definition.DefaultHeight)
            : control.Geometry is PixelGeometry pixel
                ? (Math.Max(0, pixel.Width), Math.Max(0, pixel.Height))
                : (0, 0);
    }

    /// <summary>
    /// ⛔ The one answer to "does this control lay children out in pixels": a POSITIONED control with
    /// <see cref="PixelGeometry"/>. A strip's children are items and a cell-placed Panel has no pixel size (Task 6
    /// review). Visibility is NOT part of it — <see cref="FormDockMode.Runtime"/> adds that.
    /// </summary>
    public static bool HasClientArea(FormControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return (control.Definition?.Place is null or FormPlace.Positioned) && control.Geometry is PixelGeometry;
    }
}
