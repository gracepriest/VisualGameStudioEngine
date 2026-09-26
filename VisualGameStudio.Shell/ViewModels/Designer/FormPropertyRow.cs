using System.Globalization;
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
///
/// <para>⛔ Spec §2.7: an ABSENT property DISPLAYS the target's default (greyed, not bold); BOLD is
/// present AND different from that default; RESET removes the property. The three rules read ONE
/// value — <see cref="DefaultValue"/> — so they cannot disagree.</para>
/// </summary>
public partial class FormPropertyRow : ObservableObject, ITypedValueRow, IFormDisplayRow
{
    private readonly FormControl? _control;
    private readonly Action _onChanged;
    private readonly FormPropertyType _type;
    private readonly IReadOnlyList<string>? _choices;

    /// <summary>The catalog row, for a catalog property; null for an intrinsic row.</summary>
    private readonly FormPropertyDef? _definition;

    /// <summary>Whose default an absent row shows — WinForms' and the browser's can differ (spec §2.7).</summary>
    private readonly FormTarget _target;

    /// <summary>For a catalog row stored outside the bag (a FormRoot row); null = the attribute rule.</summary>
    private readonly Func<bool>? _isPresent;

    /// <summary>How a non-attribute row removes itself; null = remove the attribute.</summary>
    private readonly Action? _reset;

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

    /// <summary>
    /// Stores a value that lives outside the bag, and answers whether it CHANGED anything. ⛔ False covers
    /// both "refused" (FormRootValues.Set on a non-positive size, IntRow on unparseable text) and "the
    /// same value again" (<c>007</c> on an X of 7) — either way no edit happened, so no Edited is raised
    /// and the editor re-reads what the model still holds.
    /// </summary>
    private readonly Func<string, bool>? _write;

    /// <summary>A catalog property of the control — an attribute the document carries as text.</summary>
    public FormPropertyRow(
        FormControl control, FormPropertyDef definition, FormTarget target, string? frozenReason, Action onChanged)
    {
        _control = control;
        _onChanged = onChanged;
        Name = definition.Name;
        _type = definition.Type;
        _choices = definition.AllowedValues;
        _definition = definition;
        _target = target;
        FrozenReason = frozenReason;
        Category = CategoryName(definition.Category);
        Description = definition.Description ?? "";
    }

    /// <summary>
    /// An INTRINSIC row: a field of the control or its geometry (Name, X, TabIndex…), or the form's Name —
    /// no catalog row, so no default, no bold and no reset; it is always present.
    /// Pass <paramref name="write"/> as null for one the designer cannot change yet, and give
    /// <paramref name="frozenReason"/> the reason — the Degraded tier already renders exactly that.
    /// <paramref name="write"/> returns whether it changed the model (see <see cref="_write"/>).
    /// </summary>
    public FormPropertyRow(
        string name,
        FormPropertyType type,
        Func<string> read,
        Func<string, bool>? write,
        Action onChanged,
        string? frozenReason = null,
        FormRowEditor editor = FormRowEditor.Default,
        string category = "Misc",
        string description = "")
    {
        _onChanged = onChanged;
        Name = name;
        _type = type;
        _read = read;
        _write = write;
        _editor = editor;
        FrozenReason = frozenReason ?? (write == null ? "This value is not editable here." : null);
        Category = category;
        Description = description;
    }

    /// <summary>A catalog row stored outside the bag — see <see cref="ForStoredValue"/>.</summary>
    private FormPropertyRow(
        FormPropertyDef definition,
        FormTarget target,
        Func<string?> read,
        Func<string, bool> write,
        Action? remove,
        string? frozenReason,
        string? frozenText,
        Action onChanged)
    {
        _onChanged = onChanged;
        Name = definition.Name;
        _type = definition.Type;
        _choices = definition.AllowedValues;
        _definition = definition;
        _target = target;

        // ⚠ A frozen value the model could not hold (a Degraded ClientSize is null in the model) shows the
        // document's own text — its reason says it is "preserved exactly as written" — and IS present: the
        // document carries it, so it must not grey out as a default.
        var carried = frozenReason != null ? frozenText : null;

        // ⛔ Presence and value come from ONE reader, so they cannot disagree: null IS absent.
        _isPresent = () => read() != null || carried != null;
        _read = () => read() ?? carried ?? "";
        _write = write;
        _reset = remove;
        FrozenReason = frozenReason;
        Category = CategoryName(definition.Category);
        Description = definition.Description ?? "";
    }

    /// <summary>
    /// A CATALOG row whose value is stored somewhere other than <see cref="FormControl.Properties"/> — a
    /// <see cref="FormControlCatalog.FormRoot"/> row, stored in typed <see cref="FormDocument"/> fields via
    /// <see cref="FormRootValues"/>. It shows its <paramref name="target"/>'s default, bolds, judges and
    /// resets exactly like a bag row.
    /// </summary>
    /// <param name="definition">The catalog row: type, default, description, category, value rules.</param>
    /// <param name="target">⛔ REQUIRED — whose default an absent row shows and whose value rules apply.</param>
    /// <param name="read">The stored value, or NULL when the document does not carry it. ⛔ Presence is
    /// read from here, never passed separately.</param>
    /// <param name="write">Stores a value; returns whether it CHANGED anything (false = refused or same).</param>
    /// <param name="remove">Removes the value from the document, or null when removal is not expressible
    /// for this row (then no Reset is offered, and clearing a typed row snaps back).</param>
    /// <param name="frozenReason">D9's Degraded reason, or null when editable.</param>
    /// <param name="frozenText">What a frozen row shows when <paramref name="read"/> has nothing — the
    /// document's own text. Ignored unless <paramref name="frozenReason"/> is given.</param>
    /// <param name="onChanged">Raised after an edit that changed the model.</param>
    public static FormPropertyRow ForStoredValue(
        FormPropertyDef definition,
        FormTarget target,
        Func<string?> read,
        Func<string, bool> write,
        Action? remove,
        Action onChanged,
        string? frozenReason = null,
        string? frozenText = null) =>
        new(definition, target, read, write, remove, frozenReason, frozenText, onChanged);

    private readonly FormRowEditor _editor = FormRowEditor.Default;

    public string Name { get; }

    /// <summary>The Visual Studio group this row is listed under (spec §3).</summary>
    public string Category { get; }

    /// <summary>The description pane's text (spec §3). Empty when the row has none.</summary>
    public string Description { get; }

    /// <summary>The declared type, shown beside the name so a frozen row is explicable.</summary>
    public string TypeName => _type.ToString();

    /// <summary>"Window Style", not "WindowStyle" — VS's own spelling of its groups.</summary>
    internal static string CategoryName(FormPropertyCategory? category) => category switch
    {
        null => "Misc",
        FormPropertyCategory.WindowStyle => "Window Style",
        var c => c.Value.ToString()
    };

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

    /// <summary>Size is text for now (<c>800, 450</c>); its composite editor arrives in slice 3.</summary>
    public bool IsTextBox => Typed &&
        _type is FormPropertyType.String or FormPropertyType.Color or FormPropertyType.Size;

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
    // Spec §2.7 — present, default, bold, reset
    // ==================================================================

    /// <summary>
    /// Whether the document CARRIES this property. An intrinsic row with no catalog definition (X,
    /// TabIndex) is always present: it has no default to fall back to.
    /// </summary>
    public bool IsPresent =>
        _isPresent?.Invoke() ?? (_control != null ? _control.Properties.ContainsKey(Name) : true);

    /// <summary>The target's default, canonicalised — what an absent row shows. Null = no static default.</summary>
    public string? DefaultValue =>
        _definition?.DefaultFor(_target) is { } d ? _definition.Canonical(d) : null;

    /// <summary>Greyed: the row shows a default the document does not carry.</summary>
    public bool IsDefaultShown => _definition != null && !IsPresent;

    /// <summary>
    /// ⛔ Present AND different from the displayed default. A null default DISPLAYS as empty, so a
    /// present empty Text is not bold while a present "Hi" is.
    /// </summary>
    public bool IsBold => _definition != null && IsPresent &&
                          !_definition.SameValue(DisplayValue, _definition.Displayed(null, _target));

    /// <summary>Offered on present, editable rows that know how to remove themselves.</summary>
    public bool CanReset => IsEditable && IsPresent && (_reset != null || (_control != null && _definition != null));

    /// <summary>
    /// Reset (spec §2.7): REMOVE the property from the document — never write the default, which would
    /// leave a property the user now has to know is redundant.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        // ⚠ RelayCommand.Execute does NOT consult CanExecute — a context menu, a key binding or a test
        // can run this on an absent or frozen row, and it must not report an edit that changed nothing.
        if (!CanReset)
        {
            return;
        }

        if (_reset != null)
        {
            _reset();
        }
        else
        {
            _control!.Properties.Remove(Name);
        }

        RaiseValueChanged();
        _onChanged();
    }

    // ==================================================================
    // The value
    // ==================================================================

    /// <summary>The raw document text for this property, or "" when it carries none.</summary>
    public string RawValue =>
        _read != null
            ? _read()
            : _control != null && _control.Properties.TryGetValue(Name, out var value) ? value : "";

    /// <summary>
    /// What the editor shows: the document's value in its CANONICAL spelling (spec §2.8) when present —
    /// a legacy <c>TextAlign="Left"</c> shows as <c>MiddleLeft</c>, which is what the nine-member combo
    /// can match — and the target's default when absent (spec §2.7).
    ///
    /// <para>⛔ Except when FROZEN: a Degraded row shows the document's text exactly, because its
    /// reason quotes that text and says it is "preserved exactly as written".</para>
    /// </summary>
    public string DisplayValue =>
        IsFrozen ? RawValue
        : _definition != null ? _definition.Displayed(IsPresent ? RawValue : null, _target)
        : RawValue;

    /// <summary>The editor's text: <see cref="DisplayValue"/> (frozen → raw; absent → the default).</summary>
    public string StringValue
    {
        get => DisplayValue;
        set => Commit(value);
    }

    public bool BoolValue
    {
        get => bool.TryParse(DisplayValue, out var parsed) && parsed;
        // ⛔ Lower case, matching the document's own vocabulary — the reader accepts either, but a
        // round trip that rewrote every "true" as "True" would report a change the user never made.
        set => Commit(value ? "true" : "false");
    }

    /// <summary>
    /// ⛔⛔ Culture-INVARIANT both ways. sv-SE/fi-FI/nb-NO format a negative number with U+2212,
    /// which <see cref="FormPropertyDef.TryParseInt"/> refuses — so a current-culture write froze
    /// SelectedIndex's own default (-1) the moment the user set it. Read with the same parser the
    /// catalog judges tiers with, so the row and the document can never disagree on a value.
    ///
    /// <para>Reads <see cref="DisplayValue"/>, not <see cref="RawValue"/>: an absent row shows the
    /// target's default (spec §2.7).</para>
    ///
    /// <para>⚠ An absent row with NO default (a web TextBox's MaxLength: no limit) displays "", which
    /// this reads as 0 — an int cannot say "empty", and neither can the NumericUpDown. The row is greyed
    /// (<see cref="IsDefaultShown"/>), and the 0 the editor pushes back is a no-op
    /// (<see cref="FormPropertyDef.Judge"/>), so the displayed 0 never reaches the document.</para>
    /// </summary>
    public int IntValue
    {
        get => FormPropertyDef.TryParseInt(DisplayValue, out var parsed) ? parsed : 0;
        set => Commit(value.ToString(CultureInfo.InvariantCulture));
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
        // (A frozen row returns BEFORE the comparison below, so its raw display never meets it.)
        if (value == null || IsFrozen)
        {
            return;
        }

        // ⛔ The DECISION is the catalog's (FormPropertyDef.Judge — a pure, table-tested function: no-op,
        // reset, refuse or write, with every rule and its reason stated there). This row only carries
        // it out. ⚠ DELIBERATE no-ops, per Judge: a case-only change (`middleleft` → `MiddleLeft`,
        // `#ff0000` → `#FF0000`) and picking the member an alias already means (`Left` → `MiddleLeft`)
        // change nothing the program does, so a legacy `Left` stays until the user picks a genuinely
        // DIFFERENT value. An intrinsic row (no definition) has only the exact no-op, and IntRow ignores
        // text it cannot parse.
        var verdict = _definition?.Judge(value, IsPresent ? RawValue : null, _target)
                      ?? (string.Equals(value, DisplayValue, StringComparison.Ordinal)
                          ? FormEditVerdict.NoOp
                          : FormEditVerdict.Write);

        switch (verdict)
        {
            case FormEditVerdict.NoOp:
                return;

            case FormEditVerdict.Reset:
                // The SAME operation as the Reset command. Nothing to remove (absent, or a row that
                // cannot remove itself) → the editor snaps back to the default it was showing.
                if (CanReset)
                {
                    Reset();
                }
                else
                {
                    RaiseEditorRefresh();
                }

                return;

            case FormEditVerdict.Refuse:
                // Spec §7: never written; the editor re-reads, so it shows what the document holds.
                RaiseEditorRefresh();
                return;
        }

        if (_write != null)
        {
            // ⛔ A write that changed nothing is not an edit: refused by the store (a non-positive
            // ClientSize, unparseable Int text) or the same value re-spelled ("007" on an X of 7). No
            // Edited, and the editor re-reads what the model holds.
            if (!_write(value))
            {
                RaiseEditorRefresh();
                return;
            }
        }
        else if (_control != null)
        {
            _control.Properties[Name] = value;
        }
        else
        {
            return;
        }

        RaiseValueChanged();
        _onChanged();
    }

    /// <summary>A refused edit: the editor re-reads the value the document still holds.</summary>
    private void RaiseEditorRefresh()
    {
        OnPropertyChanged(nameof(StringValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(IntValue));
    }

    private void RaiseValueChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(RawValue), nameof(DisplayValue), nameof(StringValue), nameof(BoolValue),
                     nameof(IntValue), nameof(IsPresent), nameof(IsBold), nameof(IsDefaultShown), nameof(CanReset)
                 })
        {
            OnPropertyChanged(name);
        }

        ResetCommand.NotifyCanExecuteChanged();
    }
}
