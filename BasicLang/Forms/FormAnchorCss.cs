using System.Globalization;

namespace BasicLang.Forms;

/// <summary>
/// WinForms' <c>AnchorStyles</c> edges, with its numbers — verified against the official enum documentation.
/// ⛔ <c>DockStyle</c> numbers DIFFERENTLY (its Left is 3) and is not a flags enum; the two never share a
/// conversion — Dock keeps its named member.
/// </summary>
[Flags]
public enum FormAnchorEdges
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8
}

/// <summary>
/// ⛔ THE one reader of an <c>Anchor</c> attribute (plan scope call S10) — the region writer's refusal and
/// emission and the page's CSS all parse it here. Two parsers of one attribute is a mirrored pair.
/// </summary>
public static class FormAnchor
{
    /// <summary>What an absent Anchor means — WinForms' default.</summary>
    public const FormAnchorEdges Default = FormAnchorEdges.Top | FormAnchorEdges.Left;

    /// <summary>The edge names as written, trimmed.</summary>
    public static string[] Split(string anchor) =>
        anchor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The edges <paramref name="anchor"/> names, case-insensitively; null/blank is <see cref="Default"/>.
    /// A name that is no edge is returned in <paramref name="unknown"/> and contributes nothing — the region
    /// writer refuses it (<c>AnchorNotExpressible</c>) rather than emit a control anchored to less than the
    /// user wrote. ⚠ Edges are OR-ed, never summed: a repeated edge ("Left,Left") is still Left (plan S10).
    /// </summary>
    public static FormAnchorEdges Parse(string? anchor, out IReadOnlyList<string> unknown)
    {
        if (string.IsNullOrWhiteSpace(anchor))
        {
            unknown = Array.Empty<string>();
            return Default;
        }

        var edges = FormAnchorEdges.None;
        var bad = new List<string>();

        foreach (var name in Split(anchor))
        {
            var match = Enum.GetValues<FormAnchorEdges>()
                .Where(e => string.Equals(e.ToString(), name, StringComparison.OrdinalIgnoreCase))
                .Select(e => (FormAnchorEdges?)e)
                .FirstOrDefault();

            if (match is { } edge)
            {
                edges |= edge;
            }
            else
            {
                bad.Add(name);
            }
        }

        unknown = bad;
        return edges;
    }
}

/// <summary>
/// Anchor and Dock → CSS for a Canvas page (spec 2026-09-27 §4) — a PURE function; the emitter only consumes it.
///
/// <para>Per axis, with W the container's design size: near edge only → <c>near:offset; size</c>; far edge only
/// → <c>far:(W−offset−size); size</c>; both → <c>near:offset; far:(W−offset−size); size:calc(100% − (near+far))</c>
/// (the size follows the container, and is WRITTEN because an <c>&lt;img&gt;</c> ignores the two insets alone);
/// neither → centred relative to its original offset, as WinForms does:
/// new offset = offset + (W′−W)/2 → <c>calc(50% ± (offset − W/2)px)</c>.</para>
///
/// <para>⛔ The container size is a PARAMETER, never computed here: the emitter passes
/// <see cref="FormDockLayoutResult.ClientSizeOf"/> (or <see cref="FormDockLayoutResult.RootClientSize"/>), the
/// one answer to "how big is the box this control is positioned in".</para>
///
/// <para>A docked control's rectangle was resolved ONCE at the design size by <see cref="FormDockLayout"/>;
/// here it becomes fixed insets on the edges it follows.</para>
/// </summary>
public static class FormAnchorCss
{
    /// <summary>
    /// The declarations for an undocked positioned control, horizontal axis first. ⚠ A non-positive size writes
    /// no size declaration (spec §3) and counts as 0 in the far-edge inset.
    /// </summary>
    public static IReadOnlyList<(string Property, string Value)> Positioned(
        PixelGeometry geometry, int containerWidth, int containerHeight)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var edges = FormAnchor.Parse(geometry.Anchor, out _);
        var declarations = new List<(string Property, string Value)>();

        Axis(declarations, "left", "right", "width", geometry.X, geometry.Width, containerWidth,
            edges.HasFlag(FormAnchorEdges.Left), edges.HasFlag(FormAnchorEdges.Right));
        Axis(declarations, "top", "bottom", "height", geometry.Y, geometry.Height, containerHeight,
            edges.HasFlag(FormAnchorEdges.Top), edges.HasFlag(FormAnchorEdges.Bottom));

        return declarations;
    }

    /// <summary>
    /// The fixed insets for a docked thing, from its resolved rectangle and container. ⛔ MIRRORED by vgsDockCss in
    /// <see cref="FormDockScript.Core"/>; FormDockScriptTests gates the pair.
    /// </summary>
    public static IReadOnlyList<(string Property, string Value)> Docked(FormDockedBounds docked)
    {
        var b = docked.Bounds;
        var right = Px(docked.ContainerWidth - b.Right);
        var bottom = Px(docked.ContainerHeight - b.Bottom);

        // Near + far inset on a stretched axis is the container size minus the control's (see Stretched).
        var width = Stretched(docked.ContainerWidth - b.Width);
        var height = Stretched(docked.ContainerHeight - b.Height);

        return docked.Edge switch
        {
            FormDockEdge.Top => new[] { ("left", Px(b.X)), ("right", right), ("width", width), ("top", Px(b.Y)), ("height", Px(b.Height)) },
            FormDockEdge.Bottom => new[] { ("left", Px(b.X)), ("right", right), ("width", width), ("bottom", bottom), ("height", Px(b.Height)) },
            FormDockEdge.Left => new[] { ("left", Px(b.X)), ("width", Px(b.Width)), ("top", Px(b.Y)), ("bottom", bottom), ("height", height) },
            FormDockEdge.Right => new[] { ("right", right), ("width", Px(b.Width)), ("top", Px(b.Y)), ("bottom", bottom), ("height", height) },
            _ => new[] { ("left", Px(b.X)), ("right", right), ("width", width), ("top", Px(b.Y)), ("bottom", bottom), ("height", height) }
        };
    }

    /// <summary>
    /// ⛔ The size of an axis pinned to BOTH edges: <c>calc(100% - (near + far)px)</c>. Written even though the two
    /// insets already imply it, because an <c>&lt;img&gt;</c> with a loaded src ignores left+right (top+bottom) and keeps
    /// its intrinsic size (Task 10 review, measured in Chromium 152); under <c>box-sizing: border-box</c> this is the
    /// same box for every element. ⚠ The sum is negative when the control is larger than its container (or after a
    /// dock overflow): <c>calc(100% - -5px)</c> is not CSS, so the sign goes outside. Invariant digits.
    /// </summary>
    public static string Stretched(int insets)
    {
        var magnitude = Math.Abs((long)insets).ToString(CultureInfo.InvariantCulture);
        return insets < 0 ? $"calc(100% + {magnitude}px)" : $"calc(100% - {magnitude}px)";
    }

    /// <summary>
    /// The centred offset: <c>offset + (W′−W)/2</c> is <c>50%·W′ + (offset − W/2)</c>. Written with the sign
    /// outside the length (<c>calc(50% - 190px)</c>) because CSS requires spaces around a binary +/−.
    /// </summary>
    public static string Centred(int offset, int container)
    {
        var shift = offset - (container / 2.0);
        return shift < 0
            ? $"calc(50% - {Number(-shift)}px)"
            : $"calc(50% + {Number(shift)}px)";
    }

    private static void Axis(
        List<(string Property, string Value)> declarations, string near, string far, string size,
        int offset, int extent, int container, bool toNear, bool toFar)
    {
        var used = Math.Max(0, extent);
        var farInset = container - offset - used;

        if (toNear && toFar)
        {
            declarations.Add((near, Px(offset)));
            declarations.Add((far, Px(farInset)));
            declarations.Add((size, Stretched(offset + farInset)));
            return;
        }

        if (toFar)
        {
            declarations.Add((far, Px(farInset)));
        }
        else if (toNear)
        {
            declarations.Add((near, Px(offset)));
        }
        else
        {
            declarations.Add((near, Centred(offset, container)));
        }

        if (extent > 0)
        {
            declarations.Add((size, Px(extent)));
        }
    }

    /// <summary>⛔ Invariant: sv-SE spells a negative with U+2212, which CSS does not read as a minus.</summary>
    private static string Px(int value) => value.ToString(CultureInfo.InvariantCulture) + "px";

    /// <summary>⛔ Invariant: sv-SE writes a decimal comma, which CSS does not read.</summary>
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
