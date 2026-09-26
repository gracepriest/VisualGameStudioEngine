using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BasicLang.Forms;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// Which editor a row renders, where the catalog's <see cref="FormPropertyType"/> is not enough to
/// say.
///
/// <para>⚠ Anchor and Dock are both "a string" by type, and a text box for either would be a
/// designer asking the user to spell <c>"Top,Bottom,Left,Right"</c> by hand. They are also the two
/// most recognisable widgets in VS's property grid, so they get the real thing.</para>
/// </summary>
public enum FormRowEditor
{
    /// <summary>Pick the editor from <see cref="FormPropertyType"/>, as every catalog row does.</summary>
    Default,

    /// <summary>The four-edge box: click an edge to anchor to it.</summary>
    AnchorPicker,

    /// <summary>The nine-region box: Top/Bottom/Left/Right/Fill/None.</summary>
    DockPicker
}

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

    /// <summary>The catalog row, for a catalog property; null for an intrinsic row.</summary>
    private readonly FormPropertyDef? _definition;

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
        _definition = definition;
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
        string? frozenReason = null,
        FormRowEditor editor = FormRowEditor.Default)
    {
        _onChanged = onChanged;
        Name = name;
        _type = type;
        _read = read;
        _write = write;
        _editor = editor;
        FrozenReason = frozenReason ?? (write == null ? "This value is not editable here." : null);
    }

    private readonly FormRowEditor _editor = FormRowEditor.Default;

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
    //
    // ⛔ Every type-driven flag also requires the DEFAULT editor. Without that an Anchor row, whose
    // declared type is String, would light up IsTextBox as well as IsAnchorPicker and the view would
    // render both — a picker with a text box underneath it, each able to contradict the other.
    /// <summary>
    /// Whether the shared <c>TypedValueEditor</c> renders this row.
    ///
    /// <para>⛔ The view binds the typed editor's visibility to THIS rather than to
    /// <see cref="IsEditable"/>. An Anchor row is editable and its declared type is String, so the
    /// shared editor would render a text box underneath the picker — two editors for one value,
    /// each able to contradict the other.</para>
    /// </summary>
    public bool UsesTypedEditor => IsEditable && _editor == FormRowEditor.Default;

    private bool Typed => UsesTypedEditor;

    public bool IsCheckBox => Typed && _type == FormPropertyType.Bool;

    public bool IsNumericUpDown => Typed && _type == FormPropertyType.Int;

    public bool IsComboBox => Typed && _type == FormPropertyType.Enum;

    public bool IsTextBox => Typed &&
        _type is FormPropertyType.String or FormPropertyType.Color;

    /// <summary>The four-edge Anchor box (Task 26).</summary>
    public bool IsAnchorPicker => IsEditable && _editor == FormRowEditor.AnchorPicker;

    /// <summary>The nine-region Dock box (Task 26).</summary>
    public bool IsDockPicker => IsEditable && _editor == FormRowEditor.DockPicker;

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

    /// <summary>
    /// The value as the editor shows it: the CANONICAL spelling (spec §2.8) — a legacy
    /// <c>TextAlign="Left"</c> shows as <c>MiddleLeft</c>, which is what the nine-member combo can match.
    /// </summary>
    public string StringValue
    {
        get => _definition != null && RawValue.Length > 0 ? _definition.Canonical(RawValue) : RawValue;
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

    // ==================================================================
    // The Anchor picker (Task 26)
    // ==================================================================

    /// <summary>
    /// The edges, in <c>AnchorStyles</c> flag order — which is also the order VS lists them.
    /// ⚠ Not alphabetical: writing "Bottom,Left,Right,Top" would round-trip correctly and read as
    /// though the designer had scrambled it.
    /// </summary>
    private static readonly string[] EdgeOrder = { "Top", "Bottom", "Left", "Right" };

    public bool AnchorTop
    {
        get => HasEdge("Top");
        set => SetEdge("Top", value);
    }

    public bool AnchorBottom
    {
        get => HasEdge("Bottom");
        set => SetEdge("Bottom", value);
    }

    public bool AnchorLeft
    {
        get => HasEdge("Left");
        set => SetEdge("Left", value);
    }

    public bool AnchorRight
    {
        get => HasEdge("Right");
        set => SetEdge("Right", value);
    }

    /// <summary>
    /// ⛔⛔ An UNSET Anchor is not "anchored to nothing" — WinForms defaults a control to
    /// <c>Top, Left</c>. Showing four empty edges would tell the user something false about a form
    /// that has never been touched, and the first edge they clicked would appear to ADD an anchor
    /// while silently REMOVING the two they already had.
    ///
    /// <para>⚠ Reading the default does not WRITE it: the document keeps its null until the user
    /// actually toggles an edge, so opening a form and closing it changes nothing.</para>
    /// </summary>
    private bool HasEdge(string edge)
    {
        var raw = RawValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return edge is "Top" or "Left";
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Any(e => string.Equals(e, edge, StringComparison.OrdinalIgnoreCase));
    }

    private void SetEdge(string edge, bool on)
    {
        if (HasEdge(edge) == on)
        {
            return;
        }

        var edges = EdgeOrder
            .Where(e => string.Equals(e, edge, StringComparison.OrdinalIgnoreCase) ? on : HasEdge(e))
            .ToList();

        // ⚠ No edges is a real, expressible state — AnchorStyles.None — and it is NOT the same as
        // unset. A control the user has explicitly un-anchored moves half the distance the form is
        // resized, which is a deliberate WinForms behaviour, so it has to survive as a value.
        Commit(edges.Count == 0 ? "None" : string.Join(",", edges));

        OnPropertyChanged(nameof(AnchorTop));
        OnPropertyChanged(nameof(AnchorBottom));
        OnPropertyChanged(nameof(AnchorLeft));
        OnPropertyChanged(nameof(AnchorRight));
    }

    // ==================================================================
    // The Dock picker (Task 26)
    // ==================================================================

    /// <summary>
    /// ⛔ <c>DockStyle</c> is NOT a flags enum — exactly one region is selected at a time, which is
    /// why this is a single value where Anchor is four toggles. Its numbering differs too
    /// (<c>Left</c> is 3, not 4), so the two must never share a conversion.
    /// </summary>
    public string DockValue => string.IsNullOrWhiteSpace(RawValue) ? "None" : RawValue.Trim();

    public bool IsDockedNone => DockValue.Equals("None", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedTop => DockValue.Equals("Top", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedBottom => DockValue.Equals("Bottom", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedLeft => DockValue.Equals("Left", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedRight => DockValue.Equals("Right", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedFill => DockValue.Equals("Fill", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sets the dock region. ⚠ <c>None</c> writes the empty string rather than the word, so an
    /// undocked control carries no <c>Dock</c> attribute at all — the same "write only non-default
    /// values" rule the rest of the document follows.
    /// </summary>
    [RelayCommand]
    private void SetDockRegion(string? region) => SetDock(region ?? "None");

    public void SetDock(string region)
    {
        Commit(region.Equals("None", StringComparison.OrdinalIgnoreCase) ? "" : region);

        foreach (var name in new[]
                 {
                     nameof(DockValue), nameof(IsDockedNone), nameof(IsDockedTop),
                     nameof(IsDockedBottom), nameof(IsDockedLeft), nameof(IsDockedRight),
                     nameof(IsDockedFill)
                 })
        {
            OnPropertyChanged(name);
        }
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
    private void Commit(string? value)
    {
        // ⛔ A null push (a combo whose SelectedItem matched nothing) and a frozen row write nothing.
        if (value == null || IsFrozen || string.Equals(RawValue, value, StringComparison.Ordinal))
        {
            return;
        }

        // ⛔ The SAME value in its canonical spelling is not an edit: the combo pushes "MiddleLeft" back
        // for a document holding "Left" the moment the row renders, and writing it would rewrite the
        // user's attribute for a selection click (spec §2.8: round-trips byte-for-byte unless EDITED).
        if (_definition != null && RawValue.Length > 0 &&
            string.Equals(_definition.Canonical(RawValue), _definition.Canonical(value), StringComparison.Ordinal))
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
