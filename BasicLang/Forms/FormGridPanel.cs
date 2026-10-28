namespace BasicLang.Forms;

/// <summary>
/// The rules of a GridPanel container: its own track list, its children's cells, and the one
/// collision/range check every route asks (reader, build, designer).
///
/// <para>⛔ The GridPanel is a CONTAINER. Its children carry <see cref="FormControl.GridCell"/>,
/// not <see cref="FormControl.Geometry"/>: a GridPanel can sit on a Canvas page (its own rectangle
/// is pixels) or on a page Grid (its own rectangle is a page cell), and the child's cell is in the
/// PANEL either way.</para>
/// </summary>
public static class FormGridPanel
{
    public const string Kind = "GridPanel";

    /// <summary>True when <paramref name="control"/> is a GridPanel.</summary>
    public static bool Is(FormControl? control) =>
        control?.Definition?.Kind == Kind;

    /// <summary>The panel's own grid, from its catalog properties and their defaults.</summary>
    public static FormLayout LayoutOf(FormControl panel)
    {
        var definition = panel.Definition;
        return new FormLayout
        {
            Kind = FormLayoutKind.Grid,
            Cols = PropertyOr(panel, definition, "Columns"),
            Rows = PropertyOr(panel, definition, "Rows"),
            Gap = PropertyOr(panel, definition, "Gap")
        };
    }

    /// <summary>The number of explicit column tracks; a blank list is CSS's one implicit column.</summary>
    public static int ColumnCount(FormControl panel) =>
        Math.Max(1, FormTracks.Split(PropertyOr(panel, panel.Definition, "Columns")).Count);

    /// <summary>The number of explicit row tracks; a blank list is CSS's one implicit row.</summary>
    public static int RowCount(FormControl panel) =>
        Math.Max(1, FormTracks.Split(PropertyOr(panel, panel.Definition, "Rows")).Count);

    /// <summary>
    /// Every way this panel's children cannot be laid out as written: a missing cell, a non-positive
    /// span, a cell outside the track list, or two siblings whose rectangles intersect.
    ///
    /// <para>⛔ Empty for a valid panel. One diagnostic code (BL8043) covers all of them; the message
    /// names the controls and the cell.</para>
    /// </summary>
    public static IReadOnlyList<string> Validate(FormControl panel)
    {
        var problems = new List<string>();
        if (!Is(panel))
        {
            return problems;
        }

        var columns = ColumnCount(panel);
        var rows = RowCount(panel);
        var cells = new List<(FormControl Control, GridCell Cell)>();

        foreach (var child in panel.Children)
        {
            if (child.GridCell is not { } cell)
            {
                problems.Add($"'{child.Id}' has no grid cell inside '{panel.Id}'. Give it Col and Row.");
                continue;
            }

            if (child.Definition is { IsContainer: true } && !FormGridPanel.Is(child))
            {
                problems.Add(
                    $"'{child.Id}' is a {child.Kind} container inside '{panel.Id}'. A GridPanel cell " +
                    "currently holds a control or a nested GridPanel, not another container.");
                continue;
            }

            if (cell.ColSpan < 1 || cell.RowSpan < 1)
            {
                problems.Add($"'{child.Id}' has a non-positive span ({cell.ColSpan}x{cell.RowSpan}) inside '{panel.Id}'.");
                continue;
            }

            if (cell.Col < 0 || cell.Row < 0 ||
                cell.Col + cell.ColSpan > columns || cell.Row + cell.RowSpan > rows)
            {
                problems.Add(
                    $"'{child.Id}' names cell ({cell.Col},{cell.Row}) with span ({cell.ColSpan}x{cell.RowSpan}) " +
                    $"inside '{panel.Id}', whose explicit grid is {columns} column(s) by {rows} row(s).");
                continue;
            }

            cells.Add((child, cell));
        }

        for (var i = 0; i < cells.Count; i++)
        {
            for (var j = i + 1; j < cells.Count; j++)
            {
                if (Overlaps(cells[i].Cell, cells[j].Cell))
                {
                    problems.Add(
                        $"'{cells[i].Control.Id}' and '{cells[j].Control.Id}' overlap inside '{panel.Id}' " +
                        $"({Describe(cells[i].Cell)} and {Describe(cells[j].Cell)}).");
                }
            }
        }

        return problems;
    }

    /// <summary>True when two cells' rectangles intersect. Spans are inclusive of every covered cell.</summary>
    public static bool Overlaps(GridCell a, GridCell b) =>
        a.Col < b.Col + b.ColSpan &&
        b.Col < a.Col + a.ColSpan &&
        a.Row < b.Row + b.RowSpan &&
        b.Row < a.Row + a.RowSpan;

    /// <summary>
    /// Resizes one explicit track to <paramref name="pixels"/> (written as a <c>px</c> track). Other
    /// tracks keep their own text. Returns false for a missing track or a non-positive size.
    /// </summary>
    public static bool ResizeTrack(FormControl panel, bool columns, int index, int pixels)
    {
        if (!Is(panel) || pixels < 1)
        {
            return false;
        }

        var name = columns ? "Columns" : "Rows";
        var tracks = FormTracks.Split(PropertyOr(panel, panel.Definition, name)).ToList();
        if (index < 0 || index >= tracks.Count)
        {
            return false;
        }

        var next = $"{pixels}px";
        if (string.Equals(tracks[index], next, StringComparison.Ordinal))
        {
            return false;
        }

        tracks[index] = next;
        panel.Properties[name] = string.Join(",", tracks);
        return true;
    }

    /// <summary>
    /// Inserts a default track at <paramref name="index"/> and shifts the cells at or after it. Returns
    /// false when the panel is not a GridPanel.
    /// </summary>
    public static bool InsertTrack(FormControl panel, bool columns, int index)
    {
        if (!Is(panel))
        {
            return false;
        }

        var name = columns ? "Columns" : "Rows";
        var tracks = FormTracks.Split(PropertyOr(panel, panel.Definition, name)).ToList();
        index = Math.Clamp(index, 0, tracks.Count);
        tracks.Insert(index, columns ? "1fr" : "auto");

        foreach (var child in panel.Children)
        {
            if (child.GridCell is not { } cell)
            {
                continue;
            }

            if (columns && cell.Col >= index)
            {
                cell.Col++;
            }
            else if (!columns && cell.Row >= index)
            {
                cell.Row++;
            }
        }

        panel.Properties[name] = string.Join(",", tracks);
        return true;
    }

    /// <summary>
    /// Removes one track, shifts later cells down, and refuses when a child occupies or spans the
    /// removed track. Returns false with <paramref name="refusal"/> when it cannot be removed.
    /// </summary>
    public static bool RemoveTrack(FormControl panel, bool columns, int index, out string? refusal)
    {
        refusal = null;
        if (!Is(panel))
        {
            refusal = "This control is not a GridPanel.";
            return false;
        }

        var name = columns ? "Columns" : "Rows";
        var tracks = FormTracks.Split(PropertyOr(panel, panel.Definition, name)).ToList();
        if (index < 0 || index >= tracks.Count)
        {
            refusal = $"There is no {(columns ? "column" : "row")} {index + 1}.";
            return false;
        }

        if (tracks.Count <= 1)
        {
            refusal = "A grid must keep at least one track.";
            return false;
        }

        foreach (var child in panel.Children)
        {
            if (child.GridCell is not { } cell)
            {
                continue;
            }

            var start = columns ? cell.Col : cell.Row;
            var span = columns ? cell.ColSpan : cell.RowSpan;
            if (index >= start && index < start + span)
            {
                refusal = $"'{child.Id}' occupies {Describe(cell)}; remove or move it first.";
                return false;
            }
        }

        tracks.RemoveAt(index);
        foreach (var child in panel.Children)
        {
            if (child.GridCell is not { } cell)
            {
                continue;
            }

            if (columns && cell.Col > index)
            {
                cell.Col--;
            }
            else if (!columns && cell.Row > index)
            {
                cell.Row--;
            }
        }

        panel.Properties[name] = string.Join(",", tracks);
        return true;
    }

    private static string Describe(GridCell cell) =>
        $"Col={cell.Col}, Row={cell.Row}, ColSpan={cell.ColSpan}, RowSpan={cell.RowSpan}";

    private static string? PropertyOr(FormControl panel, FormControlDef? definition, string name) =>
        panel.Properties.TryGetValue(name, out var value) ? value : definition?.Property(name)?.Default;
}
