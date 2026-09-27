namespace BasicLang.Forms;

/// <summary>
/// The order a Canvas page's controls stack in below its phone breakpoint (spec 2026-09-27 §5) — PURE, computed
/// at build time and applied with CSS <c>order</c> inside the media query (the HTML order is unchanged).
///
/// <para>The rule (plan scope call S8, refined by owner decision 2026-09-27), stated exactly because the table
/// tests pin it. A control's vertical span is <c>[top, max(bottom, top + 1))</c>: a zero- (or negative-) height
/// control counts as 1px tall, so it can still share a row.</para>
/// <list type="number">
/// <item><b>Rows.</b> Walk the controls by top (document order among equal tops). A control joins the current
/// ROW when its top is above the row's running bottom (the union of the row's spans so far); otherwise it starts
/// a new row. Rows come out top to bottom. A control that only TOUCHES the row (top == running bottom) starts a
/// new one.</item>
/// <item><b>Spanning members.</b> Within a row, a member is SPANNING when its span contains every other member's
/// top (<c>its top ≤ their top &lt; its bottom</c>) — a tall logo or list beside a column of fields.</item>
/// <item><b>A row with no spanning member</b> (or one member) is ordered left to right, then top, then document
/// order.</item>
/// <item><b>A row with spanning members:</b> the spanning members are sorted by X, then top, then document order.
/// Every OTHER member is put in the gap its X falls in — before the first spanning member whose X is greater
/// than its own; an equal X goes AFTER that spanning member. Each gap is ordered by this whole function again
/// (so the fields beside a tall control form their own rows, pairs staying together), and the output is gap 0,
/// spanning 1, gap 1, spanning 2, … . So a tall control on the LEFT comes before the fields beside it, one on
/// the RIGHT after them, and two side by side are both placed by X. When EVERY member is spanning (an ordinary
/// row of equal-height controls), there are no gaps and this is rule 3's order.</item>
/// </list>
/// <para>This terminates: a gap holds only non-spanning members, so each recursion orders strictly fewer.</para>
///
/// <para>⚠ Per sibling list. A container is ordered among its siblings by its own rectangle and stacks as one
/// block; the caller orders its children with this same function, in the container's coordinates.</para>
///
/// <para>⚠ The caller chooses WHAT is ordered and by WHICH rectangle: this function never looks at Dock, strips or
/// visibility. Top strips first / bottom strips last, and a docked control's resolved rect (spec §7a), are the
/// emitter's to apply before and around this call (Task 10).</para>
/// </summary>
public static class FormReadingOrder
{
    private readonly record struct Entry<T>(T Item, FormRect Bounds, int Index);

    public static IReadOnlyList<T> Order<T>(IReadOnlyList<T> items, Func<T, FormRect> boundsOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(boundsOf);

        var entries = items.Select((item, index) => new Entry<T>(item, boundsOf(item), index)).ToList();
        return OrderEntries(entries).Select(e => e.Item).ToList();
    }

    private static List<Entry<T>> OrderEntries<T>(List<Entry<T>> entries)
    {
        var ordered = new List<Entry<T>>(entries.Count);
        var row = new List<Entry<T>>();
        var rowBottom = 0;

        // By top only: OrderBy is stable, so equal tops stay in document order. Row membership does not depend on
        // the order among equal tops, and every row is fully re-sorted by OrderRow, so no further key is needed.
        foreach (var entry in entries.OrderBy(e => e.Bounds.Y))
        {
            if (row.Count > 0 && entry.Bounds.Y >= rowBottom)
            {
                ordered.AddRange(OrderRow(row));
                row = new List<Entry<T>>();
            }

            rowBottom = row.Count == 0 ? Extent(entry.Bounds) : Math.Max(rowBottom, Extent(entry.Bounds));
            row.Add(entry);
        }

        if (row.Count > 0)
        {
            ordered.AddRange(OrderRow(row));
        }

        return ordered;
    }

    private static int Extent(FormRect bounds) => Math.Max(bounds.Bottom, bounds.Y + 1);

    private static IEnumerable<Entry<T>> ByXThenYThenDocument<T>(IEnumerable<Entry<T>> entries) =>
        entries.OrderBy(e => e.Bounds.X).ThenBy(e => e.Bounds.Y).ThenBy(e => e.Index);

    private static List<Entry<T>> OrderRow<T>(List<Entry<T>> row)
    {
        var spanning = ByXThenYThenDocument(row.Where(s => IsSpanning(s, row))).ToList();
        if (spanning.Count == 0)
        {
            return ByXThenYThenDocument(row).ToList();
        }

        var gaps = Enumerable.Range(0, spanning.Count + 1).Select(_ => new List<Entry<T>>()).ToList();
        foreach (var member in row)
        {
            if (!spanning.Contains(member))
            {
                gaps[spanning.Count(s => s.Bounds.X <= member.Bounds.X)].Add(member);
            }
        }

        var ordered = new List<Entry<T>>(row.Count);
        for (var i = 0; i < gaps.Count; i++)
        {
            ordered.AddRange(OrderEntries(gaps[i]));
            if (i < spanning.Count)
            {
                ordered.Add(spanning[i]);
            }
        }

        return ordered;
    }

    private static bool IsSpanning<T>(Entry<T> candidate, List<Entry<T>> row) =>
        row.All(other => other.Index == candidate.Index
            || (candidate.Bounds.Y <= other.Bounds.Y && other.Bounds.Y < Extent(candidate.Bounds)));
}
