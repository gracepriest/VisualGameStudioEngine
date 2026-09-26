using System.Collections.ObjectModel;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// What a property list SHOWS (spec §3): collapsible category headers with their rows, or one flat A–Z
/// list; filtered by a name search; with the user's collapsed categories remembered by name. The grid's
/// Properties view uses it, and slice 5's Events tab is meant to reuse it rather than copy it.
///
/// <para>⚠ A projection only. The rows are the caller's model and are never changed here; the
/// <see cref="Items"/> it produces are <see cref="FormPropertyCategoryHeader"/>s and
/// <see cref="FormPropertyRow"/>s.</para>
///
/// <para>⛔ A rebuild clears <see cref="Items"/>, and a list bound to it pushes a null selection when it is
/// cleared — so <see cref="Refresh"/> takes the item that WAS selected and answers what should be selected
/// now: the same row while it is still shown, the rebuilt header of the same name, or null (deliberately)
/// when the search or the flat view hid it.</para>
/// </summary>
public sealed class FormPropertyDisplayList
{
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private IReadOnlyList<FormPropertyRow> _rows = Array.Empty<FormPropertyRow>();
    private string _searchText = "";

    /// <summary>Headers and rows, in display order.</summary>
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>A header was collapsed and rows left <see cref="Items"/> — a selected one may be gone.</summary>
    public event EventHandler? RowsHidden;

    /// <summary>Whether the user collapsed <paramref name="category"/> (a search does not change it).</summary>
    public bool IsCollapsed(string category) => _collapsed.Contains(category);

    /// <summary>
    /// Rebuilds <see cref="Items"/> and returns what should be selected afterwards (see the class remarks).
    /// </summary>
    public object? Refresh(IEnumerable<FormPropertyRow> rows, string searchText, bool isCategorized, object? selected)
    {
        _rows = rows.ToList();
        _searchText = searchText;
        Items.Clear();
        var visible = VisibleRows().ToList();

        if (!isCategorized)
        {
            foreach (var row in ByName(visible))
            {
                Items.Add(row);
            }
        }
        else
        {
            // ⚠ A category with no visible row gets no header: a search that matches nothing in it hides it.
            // ⚠ Case-insensitive, the same comparer the rows sort by.
            foreach (var group in visible.GroupBy(r => r.Category).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                // ⛔ While searching, a matching category is SHOWN EXPANDED — a collapsed one would show a
                // lone header for a match the user asked to see. Display only: the collapse set is not
                // touched, so clearing the search brings the collapse back.
                var expanded = _searchText.Length > 0 || !_collapsed.Contains(group.Key);
                var header = new FormPropertyCategoryHeader(group.Key, expanded, OnHeaderToggled);
                Items.Add(header);

                if (expanded)
                {
                    foreach (var row in ByName(group))
                    {
                        Items.Add(row);
                    }
                }
            }
        }

        return selected switch
        {
            FormPropertyRow row when Items.Contains(row) => row,
            FormPropertyCategoryHeader header =>
                Items.OfType<FormPropertyCategoryHeader>().FirstOrDefault(h => h.Name == header.Name),
            _ => null
        };
    }

    private IEnumerable<FormPropertyRow> VisibleRows() =>
        _searchText.Length == 0
            ? _rows
            : _rows.Where(r => r.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<FormPropertyRow> ByName(IEnumerable<FormPropertyRow> rows) =>
        rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Expands or collapses ONE category in place. ⚠ Incremental, not a full refresh: the header's own
    /// toggle button is mid-click, and recreating the item under it would drop the gesture.
    /// </summary>
    private void OnHeaderToggled(FormPropertyCategoryHeader header)
    {
        if (header.IsExpanded)
        {
            _collapsed.Remove(header.Name);
        }
        else
        {
            _collapsed.Add(header.Name);
        }

        var at = Items.IndexOf(header);
        if (at < 0)
        {
            return;
        }

        if (!header.IsExpanded)
        {
            var removed = false;
            while (at + 1 < Items.Count && Items[at + 1] is FormPropertyRow)
            {
                Items.RemoveAt(at + 1);
                removed = true;
            }

            if (removed)
            {
                RowsHidden?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var insert = at + 1;
        foreach (var row in ByName(VisibleRows().Where(r => r.Category == header.Name)))
        {
            Items.Insert(insert++, row);
        }
    }
}
