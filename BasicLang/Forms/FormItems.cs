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

    /// <summary>
    /// The item list of one control element into the model — the ONE reading the document reader and the clipboard share
    /// (ADR 0020). <c>&lt;Item&gt;</c> text verbatim, in order; a legacy attribute through <see cref="FromLegacy"/>.
    ///
    /// <para>⛔ Degraded, never coerced (returns the reason): an item holding a line break (the model's separator), an
    /// <c>&lt;Item&gt;</c> carrying attributes or markup (flattening it would silently drop what it says), or BOTH forms on
    /// one control. Nothing goes into <c>Properties</c>; the raw elements go to <c>UnknownChildren</c> and the attribute to
    /// <c>UnknownAttributes</c>, so every path that carries unknown content carries them.</para>
    /// </summary>
    /// <returns>Null when the list was read; otherwise the Degraded reason.</returns>
    public static string? Read(
        FormControl control, FormPropertyDef row, System.Xml.Linq.XAttribute? legacy,
        IReadOnlyList<System.Xml.Linq.XElement> items)
    {
        string? reason = null;
        if (legacy != null && items.Count > 0)
        {
            reason = $"'{control.Id}' carries its {row.Name} twice — an {row.Name}=\"…\" attribute AND <{ElementName}> " +
                     "children — and which one is meant cannot be decided. The items are preserved exactly as written; " +
                     "remove one of the two forms to edit them.";
        }
        else if (items.Any(i => i.HasAttributes || i.Nodes().Any(n => n is not System.Xml.Linq.XText)))
        {
            reason = $"An <{ElementName}> of '{control.Id}' carries attributes or markup, which an item cannot hold (an " +
                     "item is text). The items are preserved exactly as written and not written into the generated code.";
        }
        else if (items.Any(i => HoldsLineBreak(i.Value)))
        {
            reason = $"An <{ElementName}> of '{control.Id}' contains a line break, which an item cannot hold. The items " +
                     "are preserved exactly as written and not written into the generated code.";
        }

        if (reason != null)
        {
            if (legacy != null)
            {
                control.UnknownAttributes[legacy.Name.LocalName] = legacy.Value;
            }

            control.UnknownChildren.AddRange(items.Select(i => new System.Xml.Linq.XElement(i)));
            return reason;
        }

        if (legacy != null)
        {
            control.Properties[row.Name] = Join(FromLegacy(legacy.Value));
        }
        else if (items.Count > 0)
        {
            control.Properties[row.Name] = Join(items.Select(i => i.Value));
        }

        return null;
    }

    /// <summary>The model value as <c>&lt;Item&gt;</c> elements (blank items not written) — Create's and the clipboard's.</summary>
    public static IEnumerable<System.Xml.Linq.XElement> Elements(string modelValue) =>
        Split(modelValue).Select(item => new System.Xml.Linq.XElement(ElementName, item));
}
