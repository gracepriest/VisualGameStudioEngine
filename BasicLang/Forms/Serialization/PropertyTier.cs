namespace BasicLang.Forms.Serialization;

/// <summary>
/// D9's per-property safety tier. The <b>unit is one property on one control</b>, not the control
/// and not the document — that is the whole point of the design: one unparseable value freezes one
/// property-grid row, and every other row on the same control stays editable.
/// </summary>
public enum PropertyTier
{
    /// <summary>The catalog knows the attribute AND its value parses to the declared type. Fully editable.</summary>
    Canon,

    /// <summary>
    /// The catalog knows the attribute but the value does not parse. That row is frozen with a
    /// reason, and the value <b>round-trips unchanged</b> on save.
    /// </summary>
    Degraded,

    /// <summary>
    /// The catalog does not know the attribute at all. Neither Canon nor Degraded — it round-trips
    /// untouched, which is what makes the format forward-compatible and is why a future addition
    /// needs no reserved slot.
    /// </summary>
    Unknown
}

/// <summary>Why one property-grid row is frozen.</summary>
/// <param name="ControlId">The control carrying the property.</param>
/// <param name="Property">The attribute name, as the document spells it.</param>
/// <param name="Value">The value verbatim — this is what gets written back unchanged.</param>
/// <param name="Reason">Shown in the property grid in place of an editor.</param>
public sealed record DegradedProperty(string ControlId, string Property, string Value, string Reason);
