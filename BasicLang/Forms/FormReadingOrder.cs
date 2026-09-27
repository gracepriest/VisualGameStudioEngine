namespace BasicLang.Forms;

/// <summary>
/// The order a Canvas page's controls stack in below its phone breakpoint (spec 2026-09-27 §5) — PURE, computed
/// at build time and applied with CSS <c>order</c> inside the media query (the HTML order is unchanged).
///
/// <para>The rule (plan scope call S8), stated exactly because the table tests pin it: sort by top, then left,
/// then document order; a control joins the current ROW when its top is above the row's running bottom (the
/// union of the row so far), otherwise it starts a new row; within a row, left to right, then top, then document
/// order. A zero-height control counts as 1px tall, so it can still share a row.</para>
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
    public static IReadOnlyList<T> Order<T>(IReadOnlyList<T> items, Func<T, FormRect> boundsOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(boundsOf);

        var entries = items
            .Select((item, index) => (Item: item, Bounds: boundsOf(item), Index: index))
            .OrderBy(e => e.Bounds.Y)
            .ThenBy(e => e.Bounds.X)
            .ThenBy(e => e.Index)
            .ToList();

        var ordered = new List<T>(entries.Count);
        var row = new List<(T Item, FormRect Bounds, int Index)>();
        var rowBottom = 0;

        foreach (var entry in entries)
        {
            if (row.Count > 0 && entry.Bounds.Y >= rowBottom)
            {
                Flush(row, ordered);
            }

            rowBottom = row.Count == 0 ? Extent(entry.Bounds) : Math.Max(rowBottom, Extent(entry.Bounds));
            row.Add(entry);
        }

        Flush(row, ordered);
        return ordered;
    }

    private static int Extent(FormRect bounds) => Math.Max(bounds.Bottom, bounds.Y + 1);

    private static void Flush<T>(List<(T Item, FormRect Bounds, int Index)> row, List<T> ordered)
    {
        ordered.AddRange(row
            .OrderBy(e => e.Bounds.X)
            .ThenBy(e => e.Bounds.Y)
            .ThenBy(e => e.Index)
            .Select(e => e.Item));
        row.Clear();
    }
}
