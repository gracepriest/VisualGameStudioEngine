namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// What <see cref="FormPropertyDisplayList"/> needs from a row to group, sort and search it: its name and
/// its category. The Properties view's <see cref="FormPropertyRow"/> is one; slice 5's Events tab rows are
/// meant to be another, so the projection is shared rather than copied.
/// </summary>
public interface IFormDisplayRow
{
    /// <summary>Sorted and searched by (case-insensitive).</summary>
    string Name { get; }

    /// <summary>The header the row is listed under in the Categorized view.</summary>
    string Category { get; }

    /// <summary>
    /// The rows nested under this one — a COMPOSITE row's parts (spec §3: Font → Name/Size/Bold…, Size → Width/Height,
    /// Location → X/Y, Padding → All/Left/…). Shown right after it, in their own order, while <see cref="IsExpanded"/>.
    /// Empty for a plain row.
    /// </summary>
    IReadOnlyList<IFormDisplayRow> SubRows => Array.Empty<IFormDisplayRow>();

    /// <summary>Whether <see cref="SubRows"/> are shown. Always false for a plain row.</summary>
    bool IsExpanded => false;
}
