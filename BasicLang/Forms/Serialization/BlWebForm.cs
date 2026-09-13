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
public sealed class BlWebForm
{
    internal BlWebForm(
        FormDocument model,
        XDocument xml,
        string originalText,
        string filePath,
        IReadOnlyList<DesignDiagnostic> diagnostics,
        IReadOnlyList<DegradedProperty> degraded)
    {
        Model = model;
        Xml = xml;
        OriginalText = originalText;
        FilePath = filePath;
        Diagnostics = diagnostics;
        Degraded = degraded;
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

    internal string OriginalText { get; }

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
}
