using System.Globalization;
using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// The PARTS of a composite catalog row (spec §3, slice 3 Task 6) — what VS shows under a Font, a Size and a Padding
/// when you expand it. Chosen by the row's catalog TYPE, never by its name: a new Font-typed row gets its parts the day
/// it is added.
///
/// <para>⛔ A part never stores anything of its own. It reads its piece of the parent's DISPLAYED value (so an absent
/// row's parts show the default) and writes the WHOLE value back through <see cref="FormPropertyRow.CommitFromPart"/>
/// — the parent's own Commit, so its Judge (no-op, reset, §7 refusal) decides, and the document gets ONE value: the
/// fan-in rule (<c>New Font(…)</c>, <c>New Size(…)</c>) holds for a part edit.</para>
/// </summary>
public static class FormCompositeRows
{
    /// <summary>Gives <paramref name="parent"/> its parts when its type has them; otherwise does nothing.</summary>
    /// <param name="inherited">
    /// What the row's control INHERITS for this property when it sets none (<see cref="FormAmbient.Inherited"/> — the
    /// nearest container's, else the Form's, else the catalog default). Null for a row with no parent to inherit from
    /// (the Form's own rows): its parts then start from the Form row's catalog default.
    /// </param>
    public static void Attach(FormPropertyRow parent, Func<string?>? inherited = null)
    {
        // ⛔ ONE home for "what this row inherits" (slice 4 D-2): the Font parts below AND the Font dialog's start value
        // read it through EffectiveFont, so the two can never start from different fonts.
        parent.Inherited = inherited;

        var parts = parent.Definition?.Type switch
        {
            FormPropertyType.Font => FontParts(parent, inherited),
            FormPropertyType.Padding => PaddingParts(parent),
            FormPropertyType.Size => SizeParts(parent),
            _ => null
        };

        if (parts != null)
        {
            parent.AdoptChildren(parts);
        }
    }

    private static FormPropertyRow Part(
        FormPropertyRow parent, string name, FormPropertyType type, Func<string> read, Func<string, string?> compose) =>
        new(name, type,
            read,
            // A frozen parent (Degraded) has frozen parts: nothing may coerce the preserved text.
            parent.IsFrozen
                ? null
                : value => compose(value) is { } whole && parent.CommitFromPart(whole),
            // ⚠ No second Edited: the parent's Commit raised it already.
            onChanged: () => { },
            frozenReason: parent.FrozenReason,
            category: parent.Category,
            description: parent.Description)
        {
            // Slice 6 D-5: a merged part pre-judges each member's COMPOSED whole through this, writing nothing.
            Compose = compose
        };

    // ==================================================================
    // Font → Name, Size, Bold, Italic, Underline (VS's sub-rows; Strikeout rides along untouched)
    // ==================================================================

    /// <summary>
    /// The font the parts start from when the row shows none (an ambient Font is absent and displays empty): the font the
    /// control actually INHERITS — ⛔ code review I1: the catalog default regardless shrank a Label on a 10pt Bold form to
    /// 9pt and dropped its bold the moment Italic was ticked. The Form row's catalog default only when there is nothing to
    /// inherit from.
    /// </summary>
    private static string BaseFont(Func<string?>? inherited) =>
        inherited?.Invoke() ?? FormControlCatalog.FormRoot.Property("Font")!.Default!;

    private static FormFontValue? Font(FormPropertyRow parent, Func<string?>? inherited) =>
        FormFontValue.TryParse(parent.DisplayValue, out var font) ? font
        : parent.DisplayValue.Length == 0 && FormFontValue.TryParse(BaseFont(inherited), out var fallback) ? fallback
        : null;

    /// <summary>
    /// The font a Font row SHOWS: its own value, or — absent — what its control inherits (<see cref="FormPropertyRow.Inherited"/>,
    /// set by <see cref="Attach"/>); null for a value that does not parse. The Font dialog starts here (slice 4 D-2), from
    /// the same rule the parts read.
    /// </summary>
    public static FormFontValue? EffectiveFont(FormPropertyRow row) => Font(row, row.Inherited);

    private static IReadOnlyList<FormPropertyRow> FontParts(FormPropertyRow parent, Func<string?>? inherited)
    {
        string Style(FormFontValue f, Func<FormFontValue, bool> on) => on(f) ? "true" : "false";
        FormFontValue? Font(FormPropertyRow row) => FormCompositeRows.Font(row, inherited);

        return new[]
        {
            Part(parent, "Name", FormPropertyType.String,
                () => Font(parent)?.Family ?? "",
                v => Font(parent) is { } f ? (f with { Family = v.Trim() }).Canonical : null),
            Part(parent, "Size", FormPropertyType.String,
                () => Font(parent)?.Size.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                v => Font(parent) is { } f &&
                     decimal.TryParse(v.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size) &&
                     size > 0
                    ? (f with { Size = size }).Canonical
                    : null),
            Part(parent, "Bold", FormPropertyType.Bool,
                () => Font(parent) is { } f ? Style(f, x => x.Bold) : "false",
                v => Font(parent) is { } f && bool.TryParse(v, out var on) ? (f with { Bold = on }).Canonical : null),
            Part(parent, "Italic", FormPropertyType.Bool,
                () => Font(parent) is { } f ? Style(f, x => x.Italic) : "false",
                v => Font(parent) is { } f && bool.TryParse(v, out var on) ? (f with { Italic = on }).Canonical : null),
            Part(parent, "Underline", FormPropertyType.Bool,
                () => Font(parent) is { } f ? Style(f, x => x.Underline) : "false",
                v => Font(parent) is { } f && bool.TryParse(v, out var on) ? (f with { Underline = on }).Canonical : null)
        };
    }

    // ==================================================================
    // Padding → All, Left, Top, Right, Bottom (All is -1 when the sides differ, as WinForms' Padding.All is)
    // ==================================================================

    private static FormPaddingValue? Padding(FormPropertyRow parent) =>
        FormPaddingValue.TryParse(parent.DisplayValue, out var padding) ? padding : null;

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<FormPropertyRow> PaddingParts(FormPropertyRow parent)
    {
        FormPropertyRow Side(string name, Func<FormPaddingValue, int> get, Func<FormPaddingValue, int, FormPaddingValue> set) =>
            Part(parent, name, FormPropertyType.Int,
                () => Padding(parent) is { } p ? N(get(p)) : "",
                v => Padding(parent) is { } p && FormPropertyDef.TryParseInt(v, out var n) && n >= 0
                    ? set(p, n).Canonical
                    : null);

        return new[]
        {
            Part(parent, "All", FormPropertyType.Int,
                () => Padding(parent) is { } p ? (p.IsUniform ? N(p.Left) : "-1") : "",
                v => FormPropertyDef.TryParseInt(v, out var n) && n >= 0 ? N(n) : null),
            Side("Left", p => p.Left, (p, n) => p with { Left = n }),
            Side("Top", p => p.Top, (p, n) => p with { Top = n }),
            Side("Right", p => p.Right, (p, n) => p with { Right = n }),
            Side("Bottom", p => p.Bottom, (p, n) => p with { Bottom = n })
        };
    }

    // ==================================================================
    // Size → Width, Height
    // ==================================================================

    private static IReadOnlyList<FormPropertyRow> SizeParts(FormPropertyRow parent)
    {
        (int W, int H)? Size() =>
            FormPropertyDef.TryParseSize(parent.DisplayValue, out var w, out var h) ? (w, h) : null;

        return new[]
        {
            Part(parent, "Width", FormPropertyType.Int,
                () => Size() is { } s ? N(s.W) : "",
                v => Size() is { } s && FormPropertyDef.TryParseInt(v, out var n) ? $"{N(n)}, {N(s.H)}" : null),
            Part(parent, "Height", FormPropertyType.Int,
                () => Size() is { } s ? N(s.H) : "",
                v => Size() is { } s && FormPropertyDef.TryParseInt(v, out var n) ? $"{N(s.W)}, {N(n)}" : null)
        };
    }
}
