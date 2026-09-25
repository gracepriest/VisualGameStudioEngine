using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>The align and size commands VS puts on its Format menu and designer toolbar.</summary>
public enum FormArrangeKind
{
    AlignLeft,
    AlignRight,
    AlignTop,
    AlignBottom,
    AlignCentresHorizontally,
    AlignMiddlesVertically,
    SameWidth,
    SameHeight,
    SameSize
}

/// <summary>
/// Aligns and sizes a multi-selection against its primary (Task 20).
///
/// <para>⛔ <b>The primary is the reference and never moves.</b> Any other rule — leftmost wins,
/// first in document order wins — makes the result depend on something the user cannot see, and
/// pressing the same command twice can give two different answers.</para>
///
/// <para>⛔⛔ <b>Pixel geometry only (D3).</b> A <c>.blwebform</c> is laid out by CELL: its controls
/// have no X, Y, Width or Height, and the grid is an approximation of a page the browser renders.
/// Unifying the two layout vocabularies into pixels is precisely what D3 rejects, so these commands
/// refuse on a web form rather than inventing coordinates that the document cannot express and the
/// page would not honour.</para>
///
/// <para>⛔⛔ <b>X and Y are PARENT-RELATIVE</b>, so only controls sharing the primary's container can
/// be aligned to it. Setting a control inside a Panel and one outside it to the same X puts them in
/// two different places on screen — the numbers agree and the picture does not, with nothing looking
/// wrong. Controls elsewhere are left untouched.</para>
/// </summary>
public static class FormArrange
{
    /// <summary>
    /// Applies <paramref name="kind"/> to <paramref name="controls"/> against
    /// <paramref name="primary"/>. Returns whether anything actually moved or resized, so the caller
    /// can skip writing the document — and so an already-aligned selection does not mark the form
    /// dirty.
    /// </summary>
    public static bool Apply(
        FormDocument form,
        FormArrangeKind kind,
        IReadOnlyList<FormControl> controls,
        FormControl? primary)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(controls);

        if (primary?.Geometry is not PixelGeometry anchor || controls.Count < 2)
        {
            return false;
        }

        // Only siblings of the primary: see the parent-relative note above.
        var siblings = form.ListContaining(primary);
        var changed = false;

        foreach (var control in controls)
        {
            if (ReferenceEquals(control, primary) ||
                control.Geometry is not PixelGeometry geometry ||
                !ReferenceEquals(form.ListContaining(control), siblings))
            {
                continue;
            }

            changed |= Move(kind, geometry, anchor);
        }

        return changed;
    }

    /// <summary>
    /// ⚠ Returns whether it changed anything, rather than assigning unconditionally. A caller that
    /// wrote the document on every invocation would add an undo step for a command that did nothing.
    /// </summary>
    private static bool Move(FormArrangeKind kind, PixelGeometry geometry, PixelGeometry anchor)
    {
        var (x, y, width, height) = (geometry.X, geometry.Y, geometry.Width, geometry.Height);

        switch (kind)
        {
            case FormArrangeKind.AlignLeft:
                x = anchor.X;
                break;

            case FormArrangeKind.AlignRight:
                // Edges, not origins — a wider control travels further.
                x = anchor.X + anchor.Width - geometry.Width;
                break;

            case FormArrangeKind.AlignTop:
                y = anchor.Y;
                break;

            case FormArrangeKind.AlignBottom:
                y = anchor.Y + anchor.Height - geometry.Height;
                break;

            case FormArrangeKind.AlignCentresHorizontally:
                x = anchor.X + ((anchor.Width - geometry.Width) / 2);
                break;

            case FormArrangeKind.AlignMiddlesVertically:
                y = anchor.Y + ((anchor.Height - geometry.Height) / 2);
                break;

            case FormArrangeKind.SameWidth:
                width = anchor.Width;
                break;

            case FormArrangeKind.SameHeight:
                height = anchor.Height;
                break;

            case FormArrangeKind.SameSize:
                width = anchor.Width;
                height = anchor.Height;
                break;

            default:
                return false;
        }

        if (x == geometry.X && y == geometry.Y &&
            width == geometry.Width && height == geometry.Height)
        {
            return false;
        }

        geometry.X = x;
        geometry.Y = y;
        geometry.Width = width;
        geometry.Height = height;
        return true;
    }
}
