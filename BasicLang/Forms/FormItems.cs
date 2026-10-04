namespace BasicLang.Forms;

/// <summary>
/// A control's item list (ComboBox / ListBox / CheckedListBox <c>Items</c>) — ADR 0020. The document stores one
/// <c>&lt;Item&gt;</c> child per item; the MODEL keeps ONE encoding, the items joined by LF
/// (<c>Properties["Items"] = "Smith, John\nBeta"</c>). Every consumer — the region writer, the page emitter, the editor —
/// reads it through <see cref="Split"/>.
/// </summary>
public static class FormItems
{
    /// <summary>The child element one item is stored in.</summary>
    public const string ElementName = "Item";

    /// <summary>Whether <paramref name="row"/> is an item collection — the ONE question both writer paths ask.</summary>
    public static bool IsCollection(FormPropertyDef? row) => row is { IsItemCollection: true };

    /// <summary>
    /// The items of a model value: split on LF, a trailing CR stripped, empty and whitespace-only entries dropped; spaces
    /// around non-blank text are KEPT. (Cost, ADR 0020: an empty-string item is unrepresentable.)
    /// </summary>
    public static IReadOnlyList<string> Split(string value) =>
        value.Split('\n')
            .Select(line => line.EndsWith('\r') ? line[..^1] : line)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

    /// <summary>The model value for a list of items — <see cref="Split"/>'s inverse for non-blank items.</summary>
    public static string Join(IEnumerable<string> items) => string.Join("\n", items);

    /// <summary>
    /// A LEGACY <c>Items="a, b"</c> attribute, read with the rule it was written under: comma split, trim, drop empties.
    /// ⛔ Called only by the reader, on the attribute — never on <c>&lt;Item&gt;</c> text, which may contain commas.
    /// </summary>
    public static IReadOnlyList<string> FromLegacy(string attribute) =>
        attribute.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>An item text the model cannot hold (it contains the separator): such a list is Degraded, never coerced.</summary>
    public static bool HoldsLineBreak(string text) => text.Contains('\n') || text.Contains('\r');
}
