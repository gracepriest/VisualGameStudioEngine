namespace BasicLang.Forms;

/// <summary>
/// ⛔⛔ Where each <see cref="FormControlCatalog.FormRoot"/> row's value LIVES (spec §2.3) — meant to be
/// the ONE answer. A second copy of this mapping anywhere is a mirrored pair, and this repo carries the
/// scar of those twice (FormAssetEmitter.Tracks ↔ FormGridLayout.ParseTracks; the four private "how big
/// is the form" copies that became FormCanvasTransform.SurfaceSize).
///
/// <para>⚠ What actually reads it TODAY: the region writer's WinForms root emission (<see cref="Get"/>),
/// the reader's and the retarget's "is this root attribute modelled?" test (<see cref="RowForAttribute"/>),
/// and the property grid's Form rows (slice 2: <see cref="Get"/>, <see cref="Set"/>, <see cref="CanReset"/>).
/// NOT yet: the reader's typed parse and the writer still spell <c>Text</c>/<c>Width</c>/<c>Height</c>
/// themselves (typed fields, one per row — slice 3's Properties-stored rows are where a generic path
/// pays), and the retarget crosses Text and derives the layout edge itself. Until those land, a new row
/// must be mapped here AND taught to them — FormRootRetargetTests' catalog sweep goes red for a row the
/// retarget neither crosses nor names.</para>
/// </summary>
public static class FormRootValues
{
    /// <summary>The row's document value, or null when the document does not carry one.</summary>
    /// <exception cref="InvalidOperationException">A FormRoot row this map does not know — map it here.</exception>
    public static string? Get(FormDocument form, FormPropertyDef row) => row.Name switch
    {
        "Text" => form.Text,
        // ⚠ Null unless BOTH are positive — the same condition the region writer always applied before
        // this map existed, so an unset or degraded size emits nothing. ⛔ Invariant: an int formats its
        // minus as U+2212 under sv-SE (only reachable for a negative, which this guard excludes — kept
        // invariant anyway so the text never depends on the culture).
        "ClientSize" => form.Width is > 0 && form.Height is > 0
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{form.Width}, {form.Height}")
            : null,
        "Cols" => form.Layout?.Cols,
        "Rows" => form.Layout?.Rows,
        "Gap" => form.Layout?.Gap,
        _ => throw new InvalidOperationException(
            $"FormRoot row '{row.Name}' has no storage in FormRootValues — every root row must be mapped here.")
    };

    /// <summary>
    /// Stores <paramref name="value"/> (null = absent). Returns false, changing nothing, when the value
    /// does not parse — invalid input is refused, never coerced (spec §7).
    /// </summary>
    public static bool Set(FormDocument form, FormPropertyDef row, string? value)
    {
        switch (row.Name)
        {
            case "Text":
                form.Text = value;
                return true;

            case "ClientSize":
                if (value == null)
                {
                    form.Width = null;
                    form.Height = null;
                    return true;
                }

                if (!FormPropertyDef.TryParseSize(value, out var width, out var height) || width <= 0 || height <= 0)
                {
                    return false;
                }

                form.Width = width;
                form.Height = height;
                return true;

            case "Cols":
                (form.Layout ??= new FormLayout()).Cols = value;
                return true;

            case "Rows":
                (form.Layout ??= new FormLayout()).Rows = value;
                return true;

            case "Gap":
                (form.Layout ??= new FormLayout()).Gap = value;
                return true;

            default:
                throw new InvalidOperationException(
                    $"FormRoot row '{row.Name}' has no storage in FormRootValues — every root row must be mapped here.");
        }
    }

    /// <summary>
    /// Whether "remove it from the document" is expressible for this row. ⚠ Not ClientSize: the writer
    /// deliberately never removes Width/Height on a null (FormDocumentWriter.ApplyFormAttributes — null
    /// also means "present but unparseable"), and the designer always writes a size.
    /// </summary>
    public static bool CanReset(FormPropertyDef row) => row.Type != FormPropertyType.Size;

    /// <summary>
    /// The ROOT-ELEMENT attributes that carry a row's value. Empty for a row stored on a child element
    /// (<c>&lt;Layout&gt;</c>'s Cols/Rows/Gap).
    ///
    /// <para>⛔ Throws for an unmapped row, exactly as <see cref="Get"/> and <see cref="Set"/> do — never a
    /// guess. A guessed <c>row.Name</c> would make the reader call that attribute "known" (so it is not
    /// kept as an unknown attribute) while nothing models it, and the next save would DELETE it.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">A FormRoot row this map does not know — map it here.</exception>
    public static IReadOnlyList<string> StorageAttributes(FormPropertyDef row) => row.Name switch
    {
        "Text" => new[] { "Text" },
        "ClientSize" => new[] { "Width", "Height" },
        "Cols" or "Rows" or "Gap" => Array.Empty<string>(),
        _ => throw new InvalidOperationException(
            $"FormRoot row '{row.Name}' has no storage in FormRootValues — every root row must be mapped here.")
    };

    /// <summary>
    /// The FormRoot row a root attribute belongs to on <paramref name="target"/>, or null. ⚠ Ordinal:
    /// XML attribute names are case-sensitive and the reader has always matched them exactly — and
    /// <see cref="Serialization.FormFile.TierOfRoot"/> uses the same comparison, so the tier API and the
    /// reader cannot disagree about whether <c>text</c> is the Text row.
    /// </summary>
    public static FormPropertyDef? RowForAttribute(string attribute, FormTarget target) =>
        FormControlCatalog.FormRoot.Properties
            .Where(r => r.AppliesTo(target))
            .FirstOrDefault(r => StorageAttributes(r).Contains(attribute, StringComparer.Ordinal));
}
