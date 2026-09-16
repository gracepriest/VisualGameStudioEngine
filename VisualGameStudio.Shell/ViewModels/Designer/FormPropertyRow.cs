using CommunityToolkit.Mvvm.ComponentModel;
using BasicLang.Forms;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// One row of the designer's property grid: a catalog property of the selected control.
///
/// <para>⛔ Values live in <see cref="FormControl.Properties"/> as STRINGS, because that is what
/// the document carries — an XML attribute is text. This row parses on read and formats on write
/// rather than holding a typed copy, so there is exactly one representation of the value and no
/// second one to fall out of step with the document.</para>
/// </summary>
public partial class FormPropertyRow : ObservableObject, ITypedValueRow
{
    private readonly FormControl? _control;
    private readonly Action _onChanged;
    private readonly FormPropertyType _type;
    private readonly IReadOnlyList<string>? _choices;

    /// <summary>
    /// Where an INTRINSIC row's value lives, or null for a catalog row.
    ///
    /// <para>⛔ Intrinsic rows — Name, Location, Size, TabIndex — are the ones VS shows at the top
    /// of every control and this grid had none of: their values are not attributes in
    /// <see cref="FormControl.Properties"/>, they are fields ON the control and its geometry. Rather
    /// than a second row class with its own copy of the editor selection, the value's storage is a
    /// pair of accessors and everything else about a row stays identical.</para>
    /// </summary>
    private readonly Func<string>? _read;

    private readonly Action<string>? _write;

    /// <summary>A catalog property of the control — an attribute the document carries as text.</summary>
    public FormPropertyRow(
        FormControl control, FormPropertyDef definition, string? frozenReason, Action onChanged)
    {
        _control = control;
        _onChanged = onChanged;
        Name = definition.Name;
        _type = definition.Type;
        _choices = definition.AllowedValues;
        FrozenReason = frozenReason;
    }

    /// <summary>
    /// An intrinsic row: a field of the control or its geometry, not a document attribute.
    /// Pass <paramref name="write"/> as null for one the designer cannot change yet, and give
    /// <paramref name="frozenReason"/> the reason — the Degraded tier already renders exactly that.
    /// </summary>
    public FormPropertyRow(
        string name,
        FormPropertyType type,
        Func<string> read,
        Action<string>? write,
        Action onChanged,
        string? frozenReason = null)
    {
        _onChanged = onChanged;
        Name = name;
        _type = type;
        _read = read;
        _write = write;
        FrozenReason = frozenReason ?? (write == null ? "This value is not editable here." : null);
    }

    public string Name { get; }

    /// <summary>The declared type, shown beside the name so a frozen row is explicable.</summary>
    public string TypeName => _type.ToString();

    // ==================================================================
    // D9 — which tier this row is in
    // ==================================================================

    /// <summary>
    /// Why this row is frozen, or null when it is editable.
    ///
    /// <para>⛔⛔ D9's <b>Degraded</b> tier. The catalog knows the attribute but the document's
    /// value does not parse to its declared type. The value must be SHOWN, must round-trip
    /// unchanged, and must not be editable: handing it to a typed editor would coerce it — a
    /// <c>Checked="maybe"</c> would come back as <c>False</c> the moment the row rendered, and the
    /// user's text would be gone without them touching anything.</para>
    /// </summary>
    public string? FrozenReason { get; }

    public bool IsEditable => FrozenReason == null;

    public bool IsFrozen => !IsEditable;

    // ==================================================================
    // Which editor
    // ==================================================================

    // ⚠ A frozen row shows its raw text, never its typed editor — see FrozenReason. So every
    // editor flag is false while frozen, and the view shows the value plus the reason instead.
    public bool IsCheckBox => IsEditable && _type == FormPropertyType.Bool;

    public bool IsNumericUpDown => IsEditable && _type == FormPropertyType.Int;

    public bool IsComboBox => IsEditable && _type == FormPropertyType.Enum;

    public bool IsTextBox => IsEditable &&
        _type is FormPropertyType.String or FormPropertyType.Color;

    /// <summary>
    /// ⚠ Colour rows are TEXT for now, deliberately. Avalonia 11.3 base ships no colour picker, so
    /// a real one is hand-built — and <c>SettingControlKind.ColorPicker</c> is a declared but never
    /// rendered arm in the Settings dialog, so it is not a precedent to copy. A text row round-trips
    /// the value correctly and says what it is; a half-built picker would not.
    /// </summary>
    public bool IsColor => IsEditable && _type == FormPropertyType.Color;

    public IReadOnlyList<string>? Choices => _choices;

    // The catalog does not carry per-property ranges, and inventing them would silently clamp a
    // value the document legitimately holds.
    public int Minimum => int.MinValue;
    public int Maximum => int.MaxValue;
    public int Increment => 1;

    // ==================================================================
    // The value
    // ==================================================================

    /// <summary>The raw document text for this property, or "" when it carries none.</summary>
    public string RawValue =>
        _read != null
            ? _read()
            : _control != null && _control.Properties.TryGetValue(Name, out var value) ? value : "";

    public string StringValue
    {
        get => RawValue;
        set => Commit(value);
    }

    public bool BoolValue
    {
        get => bool.TryParse(RawValue, out var parsed) && parsed;
        // ⛔ Lower case, matching the document's own vocabulary — the reader accepts either, but a
        // round trip that rewrote every "true" as "True" would report a change the user never made.
        set => Commit(value ? "true" : "false");
    }

    public int IntValue
    {
        get => int.TryParse(RawValue, out var parsed) ? parsed : 0;
        set => Commit(value.ToString());
    }

    /// <summary>
    /// Writes the value back into the control, and tells the owner something changed.
    ///
    /// <para>⛔ A no-op write is dropped rather than forwarded. The property grid re-pushes every
    /// row's value whenever the selection changes, and forwarding those would mark the document
    /// dirty and regenerate the user's region for a selection click — which is how a designer
    /// starts producing diffs nobody asked for.</para>
    ///
    /// <para>⛔ A frozen row cannot write at all. Even with the editor disabled, a binding can
    /// still push a value on load, and that is exactly the coercion the Degraded tier exists to
    /// prevent.</para>
    /// </summary>
    private void Commit(string value)
    {
        if (IsFrozen || string.Equals(RawValue, value, StringComparison.Ordinal))
        {
            return;
        }

        if (_write != null)
        {
            _write(value);
        }
        else if (_control != null)
        {
            _control.Properties[Name] = value;
        }
        else
        {
            return;
        }

        OnPropertyChanged(nameof(RawValue));
        OnPropertyChanged(nameof(StringValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(IntValue));
        _onChanged();
    }
}
