namespace BasicLang.Forms;

/// <summary>
/// ⛔⛔ Where each <see cref="FormControlCatalog.FormRoot"/> row's value LIVES (spec §2.3) — meant to be
/// the ONE answer. A second copy of this mapping anywhere is a mirrored pair, and this repo carries the
/// scar of those twice (FormAssetEmitter.Tracks ↔ FormGridLayout.ParseTracks; the four private "how big
/// is the form" copies that became FormCanvasTransform.SurfaceSize).
///
/// <para>⚠ What actually reads it TODAY: the region writer's WinForms root emission (<see cref="Get"/>),
/// the reader's and the retarget's "is this root attribute modelled?" test (<see cref="RowForAttribute"/>),
/// and the property grid's Form rows (slice 2: <see cref="Get"/>, <see cref="Set"/>, <see cref="CanReset"/>; Task 9:
/// <see cref="RefusalOf"/>, the reason the grid shows when <see cref="Set"/> refuses).
/// Slice 3: every FormRoot row that is not a typed field is PROPERTIES-STORED (<see cref="IsStoredInProperties"/>) and
/// goes through the generic path end to end — the reader models it in <see cref="FormDocument.Properties"/> with its
/// tier, the writer patches and removes it, the retarget crosses or names it, the region writer emits it — so a new
/// Properties-stored row needs no code here. The TYPED rows are still spelled by hand in the reader's parse and the
/// writer (Text/Width/Height/Layout) and the retarget crosses Text and derives the layout edge itself: a new TYPED row
/// must be mapped here AND taught to them — FormRootRetargetTests' catalog sweep goes red for a row the retarget
/// neither crosses nor names. The row's APPLICABILITY is <see cref="Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/>
/// — the one predicate the reader's known-attribute test, the region writer, the grid, the tier and the
/// retarget call (spec 2026-09-27 §2.3). ⚠ The reader's size PARSE and the writer's size WRITE ask
/// <see cref="FormVocabulary.IsPixel(FormTarget, FormLayoutKind?)"/> instead — a second table for the same
/// fact ("ClientSize exists" == "the root speaks pixels"). They are kept one fact by
/// <c>FormRootLayoutTests.TheClientSizeRowExists_ExactlyWhereTheRootSpeaksPixels</c>, not by construction.</para>
/// </summary>
public static class FormRootValues
{
    /// <summary>The rows backed by TYPED FormDocument fields (Text, the client size, the web layout); every other FormRoot row is Properties-stored.</summary>
    private static readonly HashSet<string> TypedRows = new(StringComparer.Ordinal)
    {
        "Text", "ClientSize", "Cols", "Rows", "Gap", "MobileBreakpoint"
    };

    /// <summary>
    /// ⛔ A FormRoot row that is not a typed field (slice 3 — FormBorderStyle, BackColor, AcceptButton…): stored in
    /// <see cref="FormDocument.Properties"/> as the root attribute of its OWN name. Asked BY REFERENCE of FormRoot's own
    /// rows, so a row that is not in FormRoot (a stray definition, a typo in a test) still throws from every accessor —
    /// never a guessed storage attribute the reader would call "known" while nothing models it.
    /// </summary>
    private static bool IsPropertiesStored(FormPropertyDef row) =>
        !TypedRows.Contains(row.Name) && FormControlCatalog.FormRoot.Properties.Any(p => ReferenceEquals(p, row));

    private static InvalidOperationException Unmapped(FormPropertyDef row) => new(
        $"FormRoot row '{row.Name}' has no storage in FormRootValues — every root row must be mapped here.");

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
        "MobileBreakpoint" => form.Layout?.MobileBreakpoint,
        _ when IsPropertiesStored(row) => form.Properties.TryGetValue(row.Name, out var value) ? value : null,
        _ => throw Unmapped(row)
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

                // Parsed ONCE: the rule that refuses is the rule that yields the numbers (ClientSizeRefusal).
                if (ClientSizeRefusal(value, out var width, out var height) != null)
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

            case "MobileBreakpoint":
                if (value == null)
                {
                    if (form.Layout != null)
                    {
                        form.Layout.MobileBreakpoint = null;
                    }

                    return true;
                }

                // ⛔ Refused, never coerced (spec §7): the grid cannot manufacture a Degraded value of its own.
                if (MobileBreakpointRefusal(value, out var pixels) != null)
                {
                    return false;
                }

                (form.Layout ??= new FormLayout()).MobileBreakpoint =
                    pixels.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;

            default:
                if (!IsPropertiesStored(row))
                {
                    throw Unmapped(row);
                }

                // ⚠ The document's TEXT, as a control's bag holds it: the grid has already judged the value (Judge →
                // Accepts), and a value the row cannot use is Degraded by its tier, not refused by the store.
                if (value == null)
                {
                    form.Properties.Remove(row.Name);
                }
                else
                {
                    form.Properties[row.Name] = value;
                }

                return true;
        }
    }

    /// <summary>
    /// Why <see cref="Set"/> would refuse <paramref name="value"/> for <paramref name="row"/>, or null when it would store
    /// it. ⛔ THE one rule: <see cref="Set"/> asks it, and the property grid shows its text (plan 2026-09-27 Task 9 — a
    /// store refusal used to snap the editor back with no reason, because the catalog accepts the parse). The ending
    /// matches <see cref="FormPropertyDef.DescribeRefusedEdit"/>'s.
    /// </summary>
    /// <exception cref="InvalidOperationException">A FormRoot row this map does not know — map it here.</exception>
    public static string? RefusalOf(FormPropertyDef row, string value)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(value);

        return row.Name switch
        {
            "ClientSize" => ClientSizeRefusal(value, out _, out _),
            "MobileBreakpoint" => MobileBreakpointRefusal(value, out _),
            "Text" or "Cols" or "Rows" or "Gap" => null,
            // A Properties-stored row has no store rule of its own: its value rules are the catalog's (Accepts/Judge).
            _ when IsPropertiesStored(row) => null,
            _ => throw Unmapped(row)
        };
    }

    /// <summary>ClientSize's rule, parsed once: the refusal, or null with the usable width and height.</summary>
    private static string? ClientSizeRefusal(string value, out int width, out int height) =>
        FormPropertyDef.TryParseSize(value, out width, out height) && width > 0 && height > 0
            ? null
            : $"'{value}' is not a usable size — ClientSize needs a width and a height, both greater than 0. " +
              "It was not applied; ClientSize is unchanged.";

    /// <summary>MobileBreakpoint's rule, parsed once: the refusal, or null with the usable pixel count.</summary>
    private static string? MobileBreakpointRefusal(string value, out int pixels) =>
        FormLayout.TryParseMobileBreakpoint(value, out pixels)
            ? null
            : $"'{value}' is not a usable phone breakpoint — MobileBreakpoint must be a whole number of pixels from 0 " +
              "to 2147483647, where 0 means never stack. It was not applied; MobileBreakpoint is unchanged.";

    /// <summary>
    /// Whether "remove it from the document" is expressible for this row. ⚠ Not ClientSize: the writer
    /// deliberately never removes Width/Height on a null (FormDocumentWriter.ApplyFormAttributes — null
    /// also means "present but unparseable"), and the designer always writes a size. ⚠ By NAME since slice 3: a
    /// Properties-stored Size (MinimumSize) resets like any other attribute.
    /// </summary>
    public static bool CanReset(FormPropertyDef row) => row.Name != "ClientSize";

    /// <summary>
    /// The ROOT-ELEMENT attributes that carry a row's value. Empty for a row stored on a child element
    /// (<c>&lt;Layout&gt;</c>'s Cols/Rows/Gap/MobileBreakpoint).
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
        "Cols" or "Rows" or "Gap" or "MobileBreakpoint" => Array.Empty<string>(),
        _ when IsPropertiesStored(row) => new[] { row.Name },
        _ => throw Unmapped(row)
    };

    /// <summary>
    /// True for a row stored in <see cref="FormDocument.Properties"/> — the reader and the writer route exactly these
    /// through the bag (the typed rows keep their own fields). ⛔ The same predicate every accessor above uses.
    /// </summary>
    public static bool IsStoredInProperties(FormPropertyDef row) => IsPropertiesStored(row);

    /// <summary>
    /// ⛔⛔ Whether <paramref name="row"/> exists on a document of <paramref name="target"/> laid out
    /// <paramref name="layout"/> (spec 2026-09-27 §2.3) — THE one predicate for a FormRoot row. Never call
    /// <see cref="FormPropertyDef.AppliesTo"/> on a root row directly: ClientSize targets the web (a Canvas
    /// page's design size) and does not exist on a Grid page, and <c>AppliesTo(Web)</c> alone says it does.
    /// </summary>
    /// <param name="layout">
    /// The web document's layout; ignored for WinForms. Null on the web means Grid — the default a page
    /// with no <c>&lt;Layout&gt;</c> has (<see cref="FormVocabulary.LayoutOf"/>).
    /// </param>
    public static bool Applies(FormPropertyDef row, FormTarget target, FormLayoutKind? layout)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!row.AppliesTo(target))
        {
            return false;
        }

        return target != FormTarget.Web || row.WebLayouts == null ||
               row.WebLayouts.Contains(layout ?? FormLayoutKind.Grid);
    }

    /// <summary><see cref="Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/> for a document.</summary>
    public static bool Applies(FormPropertyDef row, FormDocument form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Applies(row, form.Target, FormVocabulary.LayoutOf(form));
    }

    /// <summary>
    /// The FormRoot row a root attribute belongs to on a document of (<paramref name="target"/>,
    /// <paramref name="layout"/>), or null — through <see cref="Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/>,
    /// never a copy of it. ⚠ Ordinal: XML attribute names are case-sensitive and the reader has always matched
    /// them exactly — and <see cref="Serialization.FormFile.TierOfRoot"/> uses the same comparison, so the
    /// tier API and the reader cannot disagree about whether <c>text</c> is the Text row.
    /// </summary>
    public static FormPropertyDef? RowForAttribute(string attribute, FormTarget target, FormLayoutKind? layout) =>
        FormControlCatalog.FormRoot.Properties
            .Where(r => Applies(r, target, layout))
            .FirstOrDefault(r => StorageAttributes(r).Contains(attribute, StringComparer.Ordinal));
}
