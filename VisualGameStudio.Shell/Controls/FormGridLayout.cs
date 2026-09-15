using System.Globalization;
using Avalonia;
using BasicLang.Forms;

namespace VisualGameStudio.Shell.Controls;

/// <summary>
/// The grid a <c>.blwebform</c> is drawn on: where each cell is, and which cell a point is in.
///
/// <para>⛔⛔ <b>An approximation, and the honesty of it is the design.</b> <c>Cols</c>/<c>Rows</c>
/// are CSS <c>grid-template-*</c> track lists, stored verbatim because the browser is the renderer.
/// CSS sizes tracks from content, fonts and viewport — none of which the IDE has. So <c>fr</c>
/// weights and <c>px</c> tracks are honoured, and everything else (<c>auto</c>, <c>minmax()</c>,
/// percentages, <c>calc()</c>) is drawn as one flexible share.</para>
///
/// <para>What that buys is the thing the document actually records: WHICH CELL a control is in.
/// The canvas shows the cell you are dropping into and the cell each control occupies — it does not
/// claim to show what the page will look like, and per D-WYSIWYG it must not. Pressing F5 is still
/// the only renderer.</para>
/// </summary>
public static class FormGridLayout
{
    /// <summary>One parsed track: a fixed pixel size, or a weight to share what is left.</summary>
    private readonly record struct Track(double Fixed, double Weight)
    {
        public bool IsFixed => Weight <= 0;
    }

    /// <summary>
    /// Splits a <c>grid-template-*</c> list on commas. Always at least one track — a grid with no
    /// <c>Cols</c> is one column wide, and zero tracks would make every cell empty and the whole
    /// page undroppable.
    /// </summary>
    public static IReadOnlyList<string> ParseTracks(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return new[] { "1fr" };
        }

        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? new[] { "1fr" } : parts;
    }

    /// <summary>The gap between tracks, in form pixels. Non-pixel gaps are treated as none.</summary>
    public static double ParseGap(string? gap)
    {
        if (string.IsNullOrWhiteSpace(gap))
        {
            return 0;
        }

        var trimmed = gap.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^2];
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
               value > 0
            ? value
            : 0;
    }

    /// <summary>The rectangle a control at (col,row) spanning (colSpan,rowSpan) occupies.</summary>
    public static Rect CellRect(
        FormLayout? layout, Size surface, int col, int row, int colSpan, int rowSpan)
    {
        var columns = Resolve(ParseTracks(layout?.Cols), surface.Width, ParseGap(layout?.Gap));
        var rows = Resolve(ParseTracks(layout?.Rows), surface.Height, ParseGap(layout?.Gap));

        var (x, width) = Extent(columns, col, colSpan, ParseGap(layout?.Gap));
        var (y, height) = Extent(rows, row, rowSpan, ParseGap(layout?.Gap));

        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// The cell under a point in form space, or null when the point is off the surface.
    ///
    /// <para>⛔ A point in a GAP belongs to a cell, never to nothing. Gaps are a few pixels wide and
    /// invisible at canvas zoom, so refusing there would read as "the drop did nothing" — the exact
    /// failure this feature exists to remove.</para>
    /// </summary>
    public static (int Col, int Row)? CellAt(FormLayout? layout, Size surface, Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X > surface.Width || point.Y > surface.Height)
        {
            return null;
        }

        var gap = ParseGap(layout?.Gap);
        var columns = Resolve(ParseTracks(layout?.Cols), surface.Width, gap);
        var rows = Resolve(ParseTracks(layout?.Rows), surface.Height, gap);

        return (IndexAt(columns, gap, point.X), IndexAt(rows, gap, point.Y));
    }

    /// <summary>Every cell of the grid, for drawing the drop targets.</summary>
    public static IEnumerable<(int Col, int Row, Rect Bounds)> Cells(FormLayout? layout, Size surface)
    {
        var columns = ParseTracks(layout?.Cols).Count;
        var rows = ParseTracks(layout?.Rows).Count;

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                yield return (col, row, CellRect(layout, surface, col, row, 1, 1));
            }
        }
    }

    // ==================================================================
    // Track sizing
    // ==================================================================

    /// <summary>
    /// Sizes every track: fixed ones take their pixels, the rest share what is left by weight.
    /// </summary>
    private static double[] Resolve(IReadOnlyList<string> specs, double available, double gap)
    {
        var tracks = specs.Select(Parse).ToArray();
        var sizes = new double[tracks.Length];

        // ⛔ n tracks have n-1 gaps. One too many leaves an unexplained margin down the far edge;
        // one too few runs the last track off the surface.
        var free = Math.Max(0, available - (gap * Math.Max(0, tracks.Length - 1)));

        var fixedTotal = tracks.Where(t => t.IsFixed).Sum(t => t.Fixed);
        var weightTotal = tracks.Where(t => !t.IsFixed).Sum(t => t.Weight);

        // ⚠ A fixed track wider than the surface would otherwise hand the flexible ones a NEGATIVE
        // share, and a negative width is a rectangle nothing can be dropped into.
        var flexible = Math.Max(0, free - fixedTotal);

        for (var i = 0; i < tracks.Length; i++)
        {
            sizes[i] = tracks[i].IsFixed
                ? tracks[i].Fixed
                : weightTotal > 0 ? flexible * (tracks[i].Weight / weightTotal) : 0;
        }

        return sizes;
    }

    /// <summary>
    /// One track spec. <c>120px</c> is fixed, <c>3fr</c> is weight 3, and anything else — including
    /// <c>auto</c>, which CSS sizes from content the IDE cannot see — is one flexible share.
    /// </summary>
    private static Track Parse(string spec)
    {
        var text = spec.Trim();

        if (text.EndsWith("fr", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
        {
            return new Track(0, Math.Max(0, weight));
        }

        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
        {
            return new Track(Math.Max(0, pixels), 0);
        }

        return new Track(0, 1);
    }

    private static (double Offset, double Size) Extent(double[] sizes, int index, int span, double gap)
    {
        if (sizes.Length == 0)
        {
            return (0, 0);
        }

        var start = Math.Clamp(index, 0, sizes.Length - 1);
        var count = Math.Clamp(span, 1, sizes.Length - start);

        var offset = 0.0;
        for (var i = 0; i < start; i++)
        {
            offset += sizes[i] + gap;
        }

        // A span swallows the gaps BETWEEN the cells it covers, exactly as CSS grid does.
        var size = 0.0;
        for (var i = start; i < start + count; i++)
        {
            size += sizes[i];
        }

        size += gap * (count - 1);
        return (offset, size);
    }

    /// <summary>
    /// Which track a coordinate falls in. A point past the end of a track lands in that track until
    /// the next one starts, so the gaps belong to the track before them.
    /// </summary>
    private static int IndexAt(double[] sizes, double gap, double position)
    {
        var edge = 0.0;
        for (var i = 0; i < sizes.Length; i++)
        {
            edge += sizes[i] + gap;
            if (position < edge)
            {
                return i;
            }
        }

        return Math.Max(0, sizes.Length - 1);
    }
}
