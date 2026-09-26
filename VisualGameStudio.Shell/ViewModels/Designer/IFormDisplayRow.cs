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
}
