namespace BasicLang.Forms;

/// <summary>
/// The order a Canvas page's controls stack in below its phone breakpoint (spec 2026-09-27 §5) — PURE, computed
/// at build time and applied with CSS <c>order</c> inside the media query (the HTML order is unchanged).
///
/// <para>The rule (plan scope call S8, refined by owner decision 2026-09-27), stated exactly because the table
/// tests pin it. A control's vertical span is <c>[top, max(bottom, top + 1))</c> and its height is that span's
/// length: a zero- (or negative-) height control counts as 1px tall, so it can still share a row.</para>
/// <list type="number">
/// <item><b>Rows.</b> Walk the controls by top (document order among equal tops). A control joins the current
/// ROW when its top is above the row's running bottom (the union of the row's spans so far); otherwise it starts
/// a new row. Rows come out top to bottom. A control that only TOUCHES the row (top == running bottom) starts a
/// new one.</item>
/// <item><b>Spanning members (greedy).</b> Within a row, remove members one at a time, TALLEST first (equal
/// heights: document order, earliest first), until the members left fall into two or more rows under rule 1.
/// The removed members are CANDIDATES, and a candidate is a SPANNING member only if its span wholly contains at
/// least one row of the members left (that row's lowest top ≥ its top and highest bottom ≤ its bottom) — a tall
/// logo or list that was holding the controls beside it together as one row, not one link in a staggered
/// two-column chain. A candidate that does not qualify goes back among the others. If the members left never split
/// (down to one), or no candidate qualifies, the row has NO spanning member.</item>
/// <item><b>A row with no spanning member</b> is ordered left to right, then top, then document order.</item>
/// <item><b>A row with spanning members:</b> the spanning members are sorted by X, then top, then document order.
/// Every OTHER member is put in the gap its X falls in — before the first spanning member whose X is greater
/// than its own; an equal X goes AFTER that spanning member. Each gap is ordered by this whole function again
/// (so the fields beside a tall control form their own rows, pairs staying together), and the output is gap 0,
/// spanning 1, gap 1, spanning 2, … . So a tall control on the LEFT comes before the fields beside it, one on
/// the RIGHT after them, and two side by side are both placed by X.</item>
/// </list>
/// <para>This terminates: rule 4 recurses only when at least one member is KEPT as spanning, and the gaps hold the
/// row minus the kept members, so each gap holds strictly fewer members than the row it came from (and a row never
/// holds more than the list being ordered). With none kept, rule 3 applies and nothing recurses. Every recursion
/// orders strictly fewer controls.</para>
///
/// <para>⚠ Per sibling list. A container is ordered among its siblings by its own rectangle and stacks as one
/// block; the caller orders its children with this same function, in the container's coordinates.</para>
///
/// <para>⚠ The caller chooses WHAT is ordered and by WHICH rectangle: this function never looks at Dock, strips or
/// visibility. Top strips first / bottom strips last, and a docked control's resolved rect (spec §7a), are applied
/// around this call by the emitter (<c>FormAssetEmitter.AppendStacked</c>), which orders by the DESIGNER picture so
/// the order does not depend on what user code has hidden.</para>
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
        foreach (var row in Rows(entries))
        {
            ordered.AddRange(OrderRow(row));
        }

        return ordered;
    }

    /// <summary>Rule 1. By top only: OrderBy is stable, so equal tops stay in document order; row membership does
    /// not depend on the order among equal tops, and every row is re-sorted by <see cref="OrderRow{T}"/>.</summary>
    private static List<List<Entry<T>>> Rows<T>(IEnumerable<Entry<T>> entries)
    {
        var rows = new List<List<Entry<T>>>();
        var rowBottom = 0;

        foreach (var entry in entries.OrderBy(e => e.Bounds.Y))
        {
            if (rows.Count == 0 || entry.Bounds.Y >= rowBottom)
            {
                rows.Add(new List<Entry<T>>());
                rowBottom = Extent(entry.Bounds);
            }
            else
            {
                rowBottom = Math.Max(rowBottom, Extent(entry.Bounds));
            }

            rows[^1].Add(entry);
        }

        return rows;
    }

    private static int Extent(FormRect bounds) => Math.Max(bounds.Bottom, bounds.Y + 1);

    private static int HeightOf(FormRect bounds) => Extent(bounds) - bounds.Y;

    private static IEnumerable<Entry<T>> ByXThenYThenDocument<T>(IEnumerable<Entry<T>> entries) =>
        entries.OrderBy(e => e.Bounds.X).ThenBy(e => e.Bounds.Y).ThenBy(e => e.Index);

    private static List<Entry<T>> OrderRow<T>(List<Entry<T>> row)
    {
        var spanningIndices = SpanningIndices(row);
        if (spanningIndices.Count == 0)
        {
            return ByXThenYThenDocument(row).ToList();
        }

        var spanning = ByXThenYThenDocument(row.Where(e => spanningIndices.Contains(e.Index))).ToList();
        var gaps = Enumerable.Range(0, spanning.Count + 1).Select(_ => new List<Entry<T>>()).ToList();
        foreach (var member in row.Where(e => !spanningIndices.Contains(e.Index)))
        {
            gaps[spanning.Count(s => s.Bounds.X <= member.Bounds.X)].Add(member);
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

    /// <summary>Rule 2: remove tallest-first (document order on ties) until the rest splits into 2+ rows, then keep
    /// only the removed members whose span wholly contains at least one of the remaining rows.</summary>
    private static HashSet<int> SpanningIndices<T>(List<Entry<T>> row)
    {
        var removalOrder = row.OrderByDescending(e => HeightOf(e.Bounds)).ThenBy(e => e.Index).ToList();
        var removed = new List<Entry<T>>();

        foreach (var candidate in removalOrder)
        {
            removed.Add(candidate);
            var left = row.Where(e => !removed.Any(r => r.Index == e.Index)).ToList();
            if (left.Count < 2)
            {
                break;
            }

            var rows = Rows(left);
            if (rows.Count >= 2)
            {
                return removed
                    .Where(r => rows.Any(leftRow => ContainsRow(r.Bounds, leftRow)))
                    .Select(r => r.Index)
                    .ToHashSet();
            }
        }

        return new HashSet<int>();
    }

    private static bool ContainsRow<T>(FormRect span, List<Entry<T>> leftRow) =>
        leftRow.Min(e => e.Bounds.Y) >= span.Y && leftRow.Max(e => Extent(e.Bounds)) <= Extent(span);
}
