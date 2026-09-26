using System.Xml.Linq;

namespace BasicLang.Forms.Serialization;

/// <summary>
/// A loaded <c>.blwebform</c>: the model, the diagnostics reading it produced, and enough of the
/// original to write it back without disturbing anything the designer did not change.
///
/// <para><b>The original text and the original XML are held deliberately.</b> Writing from the model
/// alone would reformat the whole file on every save and lose every comment and unknown element —
/// exactly the defect Slice 0 removed from <c>ProjectSerializer</c>, which is the same problem in a
/// different format. Holding both is what makes "a no-op patch writes nothing" achievable rather
/// than aspirational.</para>
/// </summary>
public sealed class FormFile
{
    internal FormFile(
        FormDocument model,
        XDocument xml,
        string originalText,
        string filePath,
        IReadOnlyList<DesignDiagnostic> diagnostics,
        IReadOnlyList<DegradedProperty> degraded,
        IReadOnlyList<DegradedProperty>? degradedRoot = null)
    {
        Model = model;
        Xml = xml;
        OriginalText = originalText;
        CurrentText = originalText;
        FilePath = filePath;
        Diagnostics = diagnostics;
        Degraded = degraded;
        DegradedRoot = degradedRoot ?? Array.Empty<DegradedProperty>();
    }

    public FormDocument Model { get; }

    public string FilePath { get; }

    /// <summary>Everything reading produced — refusals and informational findings alike.</summary>
    public IReadOnlyList<DesignDiagnostic> Diagnostics { get; }

    /// <summary>Property-grid rows to freeze, with the reason and the value to round-trip.</summary>
    public IReadOnlyList<DegradedProperty> Degraded { get; }

    /// <summary>
    /// True when the DOCUMENT is unsafe to write. The form opens read-only in the designer with the
    /// naming diagnostic; Code view stays fully editable. Refusal is document-level by design —
    /// a per-property problem is Degraded, not Refused.
    /// </summary>
    public bool IsRefused => Diagnostics.Any(d => !d.IsWarning);

    internal XDocument Xml { get; }

    /// <summary>The text as loaded. Never changes — it is the baseline byte-identity is measured against.</summary>
    internal string OriginalText { get; }

    /// <summary>
    /// The text this document last serialized to, which starts equal to <see cref="OriginalText"/>.
    ///
    /// <para>⛔ Needed because the writer mutates <see cref="Xml"/> IN PLACE. Without it, a second
    /// <c>Write</c> on the same instance sees a tree that already holds the first edit, finds
    /// "nothing changed", and returns <see cref="OriginalText"/> — the text from BEFORE that edit.
    /// A second <c>Save</c> would then write the pre-edit document back over the saved one, which is
    /// the exact opposite of the no-op it is supposed to be.</para>
    /// </summary>
    internal string CurrentText { get; set; }

    /// <summary>The frozen reason for one property, or null when that property is not Degraded.</summary>
    public string? DegradedReason(string controlId, string property) =>
        Degraded.FirstOrDefault(d =>
            string.Equals(d.ControlId, controlId, StringComparison.Ordinal) &&
            string.Equals(d.Property, property, StringComparison.OrdinalIgnoreCase))?.Reason;

    public PropertyTier TierOf(string controlId, string property)
    {
        if (DegradedReason(controlId, property) != null)
        {
            return PropertyTier.Degraded;
        }

        var control = Model.FindById(controlId);
        var definition = control?.Definition;
        return definition?.Property(property) != null ? PropertyTier.Canon : PropertyTier.Unknown;
    }

    /// <summary>The FORM's frozen rows — its own list, never a reserved control id (spec §2.3): a
    /// control with <c>Id=""</c> is legal to read.</summary>
    public IReadOnlyList<DegradedProperty> DegradedRoot { get; }

    /// <summary>The frozen reason for one FormRoot row, or null when it is not Degraded.</summary>
    /// <remarks>⚠ Ordinal — see <see cref="TierOfRoot"/>.</remarks>
    public string? DegradedReasonOfRoot(string property) =>
        DegradedRoot.FirstOrDefault(d => string.Equals(d.Property, property, StringComparison.Ordinal))?.Reason;

    /// <summary>The D9 tier of one FormRoot row on this document's target.</summary>
    /// <remarks>
    /// ⚠ ORDINAL, deliberately unlike <see cref="TierOf"/>'s case-insensitive control lookup: a root row's
    /// name is its XML attribute spelling, and <see cref="FormRootValues.RowForAttribute"/> (what the
    /// reader asks) matches attributes ordinally because XML is case-sensitive. A case-insensitive tier
    /// would call <c>text</c> Canon while the reader keeps <c>text="x"</c> as an unknown attribute.
    /// Callers pass <c>row.Name</c>.
    /// </remarks>
    public PropertyTier TierOfRoot(string property)
    {
        if (DegradedReasonOfRoot(property) != null)
        {
            return PropertyTier.Degraded;
        }

        return FormControlCatalog.FormRoot.Properties.FirstOrDefault(r => string.Equals(r.Name, property, StringComparison.Ordinal))
                   is { } row && row.AppliesTo(Model.Target)
            ? PropertyTier.Canon
            : PropertyTier.Unknown;
    }
}
