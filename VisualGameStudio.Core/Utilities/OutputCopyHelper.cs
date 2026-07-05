namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Pure line-joining/ordering logic for copying lines out of the Output panel
/// (Ctrl+C, context-menu Copy / Copy All). Kept UI-free so it is unit-testable.
/// </summary>
public static class OutputCopyHelper
{
    /// <summary>
    /// Returns the text of the selected lines joined with <see cref="Environment.NewLine"/>,
    /// in DISPLAY order (the order of <paramref name="linesInDisplayOrder"/>), regardless of
    /// the order items were selected in. Selected items are matched by reference identity,
    /// so duplicate line text is handled correctly. Selected items that are not present in
    /// the display list are ignored; duplicate selection entries are copied once.
    /// Returns an empty string when there is nothing to copy.
    /// </summary>
    public static string BuildCopyText<T>(
        IEnumerable<T>? linesInDisplayOrder,
        IEnumerable<T>? selectedLines,
        Func<T, string?> getText) where T : class
    {
        if (linesInDisplayOrder is null || selectedLines is null)
            return string.Empty;

        var selected = new HashSet<T>(selectedLines, ReferenceEqualityComparer.Instance);
        if (selected.Count == 0)
            return string.Empty;

        var texts = linesInDisplayOrder
            .Where(selected.Contains)
            .Select(line => getText(line) ?? string.Empty);

        return string.Join(Environment.NewLine, texts);
    }

    /// <summary>
    /// Returns the text of every line joined with <see cref="Environment.NewLine"/>,
    /// in the given order. Returns an empty string when there are no lines.
    /// </summary>
    public static string BuildCopyAllText<T>(IEnumerable<T>? lines, Func<T, string?> getText) where T : class
    {
        if (lines is null)
            return string.Empty;

        return string.Join(Environment.NewLine, lines.Select(line => getText(line) ?? string.Empty));
    }
}
