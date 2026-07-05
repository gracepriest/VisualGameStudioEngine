using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Ordering helpers for LSP completion items.
/// </summary>
public static class CompletionItemOrdering
{
    /// <summary>
    /// Orders completion items by the server-provided SortText (ordinal
    /// comparison, matching the LSP convention), falling back to the Label
    /// when SortText is missing. The sort is stable, so server order is
    /// preserved for equal keys.
    /// </summary>
    public static IReadOnlyList<CompletionItem> OrderByServerRank(IEnumerable<CompletionItem> items)
    {
        return items
            .OrderBy(i => i.SortText ?? i.Label, StringComparer.Ordinal)
            .ToList();
    }
}
