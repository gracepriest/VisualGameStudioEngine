using System.ComponentModel;

namespace VisualGameStudio.Core.Abstractions.ViewModels;

/// <summary>
/// One editable row of "a name and a typed value" — the shape the Settings dialog and the form
/// designer's property grid both need.
///
/// <para>⛔ Extracted rather than copied. The Settings dialog already carried the SAME four editors
/// twice (<c>SettingsDialog.axaml</c> at <c>:140</c> and again at <c>:266</c>), and the property
/// grid would have been the third copy. Four editors times three sites is where a fix lands in one
/// place and the other two keep the bug.</para>
///
/// <para>⚠ Deliberately does NOT cover every kind either host needs. The Settings dialog's
/// file-path row (browse button, validation message) and the designer's colour row are host
/// specific, and each keeps its own editor beside the shared one. Pushing those in here would make
/// the shared abstraction carry two features neither host shares — which is how a shared type turns
/// into the union of its callers.</para>
/// </summary>
public interface ITypedValueRow : INotifyPropertyChanged
{
    /// <summary>The row's label.</summary>
    string Name { get; }

    // One flag per editor rather than an enum, because that is what the AXAML binds IsVisible to
    // and it is what both existing templates already do.
    bool IsCheckBox { get; }
    bool IsNumericUpDown { get; }
    bool IsComboBox { get; }
    bool IsTextBox { get; }

    bool BoolValue { get; set; }
    int IntValue { get; set; }
    string StringValue { get; set; }

    /// <summary>Choices for the combo editor; null for every other kind.</summary>
    IReadOnlyList<string>? Choices { get; }

    int Minimum { get; }
    int Maximum { get; }
    int Increment { get; }

    /// <summary>
    /// False freezes the row's editor.
    ///
    /// <para>⛔ This is D9's <b>Degraded</b> tier reaching the UI. A value the catalog knows but
    /// cannot parse must be shown, must round-trip unchanged, and must NOT be editable — editing it
    /// would replace the user's unparseable text with whatever the editor coerced it to, which is
    /// the silent data loss the tier exists to prevent. Settings rows are always editable.</para>
    /// </summary>
    bool IsEditable { get; }

    /// <summary>Why the row is frozen, shown in place of the editor. Null when editable.</summary>
    string? FrozenReason { get; }
}
