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

    /// <summary>
    /// Why the STORE refused a value, for a stored-value row (<see cref="ForStoredValue"/>), or null. ⛔ Asked only when
    /// <see cref="_write"/> returns false, which also means "the same value again" — so it answers null for that, and
    /// non-null only for a genuine refusal (<c>FormRootValues.RefusalOf</c>).
    /// </summary>
    private readonly Func<string, string?>? _storeRefusal;

    /// <summary>A catalog property of the control — an attribute the document carries as text.</summary>
    public FormPropertyRow(
        FormControl control, FormPropertyDef definition, FormTarget target, string? frozenReason, Action onChanged)
    {
        _control = control;
        _onChanged = onChanged;
        Name = definition.Name;
        _type = definition.Type;
        _choices = definition.Choices;
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
        Action onChanged,
        Func<string, string?>? storeRefusal)
    {
        _onChanged = onChanged;
        Name = definition.Name;
        _type = definition.Type;
        _choices = definition.Choices;
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
        _storeRefusal = storeRefusal;
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
    /// <param name="storeRefusal">Names a value the store refuses (shown in the description pane); null for a store
    /// that refuses nothing.</param>
    public static FormPropertyRow ForStoredValue(
        FormPropertyDef definition,
        FormTarget target,
        Func<string?> read,
        Func<string, bool> write,
        Action? remove,
        Action onChanged,
        string? frozenReason = null,
        string? frozenText = null,
        Func<string, string?>? storeRefusal = null) =>
        new(definition, target, read, write, remove, frozenReason, frozenText, onChanged, storeRefusal);

    private readonly FormRowEditor _editor = FormRowEditor.Default;

    // ==================================================================
    // Composite rows (spec §3, slice 3 Task 6): Font → Name/Size/Bold/Italic/Underline, Size → Width/Height,
    // Location → X/Y, Padding → All/Left/Top/Right/Bottom. The PARENT still owns the value (and accepts typed text);
    // each part reads its piece of it and writes the WHOLE value back through the parent — one statement per
    // composite, the fan-in rule, and every no-op/refusal rule of the parent's own Commit.
    // ==================================================================

    private readonly List<FormPropertyRow> _children = new();

    /// <summary>The parts of a composite row, in the order VS lists them. Empty for a plain row.</summary>
    public IReadOnlyList<FormPropertyRow> Children => _children;

    /// <summary>The composite this row is a part of, or null for a top-level row.</summary>
    public FormPropertyRow? Parent { get; private set; }

    /// <summary>True when the row has parts to expand.</summary>
    public bool IsComposite => _children.Count > 0;

    /// <summary>Whether the parts are shown (the display list inserts them right after this row).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    private bool _isExpanded;

    /// <summary>VS's +/− box — a real minus sign, U+2212, as wide as the plus (the category header's glyph).</summary>
    public string Glyph => IsExpanded ? ((char)0x2212).ToString() : "+";

    /// <summary>
    /// Where the name starts: a plain row 12px in (as it always was), a composite 2px after its 12px expander box — so
    /// both names line up — and a part one step further in, under its parent.
    /// </summary>
    public Avalonia.Thickness NameMargin => new(Parent != null ? 26 : IsComposite ? 2 : 12, 0, 0, 0);

    IReadOnlyList<IFormDisplayRow> IFormDisplayRow.SubRows => _children;

    /// <summary>The catalog row, for a composite helper that needs its type; null for an intrinsic row.</summary>
    internal FormPropertyDef? Definition => _definition;

    /// <summary>Makes <paramref name="children"/> this row's parts. Called once, while the grid builds its rows.</summary>
    internal void AdoptChildren(IEnumerable<FormPropertyRow> children)
    {
        foreach (var child in children)
        {
            child.Parent = this;
            _children.Add(child);
        }
    }

    /// <summary>
    /// A part writing the WHOLE value (<paramref name="value"/>) through its parent — the same Commit a typed edit of the
    /// parent takes, so the parent's no-op, reset and §7 refusal rules hold for a part edit too. Returns whether the
    /// parent's value CHANGED (false for a refusal or the same value: the part then snaps back).
    /// </summary>
    internal bool CommitFromPart(string value)
    {
        var before = DisplayValue;
        Commit(value);
        return !string.Equals(before, DisplayValue, StringComparison.Ordinal);
    }

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

    /// <summary>An Enum's members, or a Cursor row's <c>Cursors</c> members (<see cref="FormPropertyDef.Choices"/>).</summary>
    public bool IsComboBox => Typed && _type is FormPropertyType.Enum or FormPropertyType.Cursor;

    /// <summary>
    /// Free text: a String, and the typed-text rows — a Color, a Size (<c>800, 450</c>), a Font (FontConverter text),
    /// a Padding (<c>4</c> or <c>4, 2, 4, 2</c>), a Fraction (<c>0.85</c>) and a Reference (a control's Id). The composite
    /// rows (slice 3 Task 6) and the colour, font and reference editors (slice 4) sit beside this text, which stays the
    /// parent's own editor.
    /// </summary>
    public bool IsTextBox => Typed &&
        _type is FormPropertyType.String or FormPropertyType.Color or FormPropertyType.Size
            or FormPropertyType.Font or FormPropertyType.Padding or FormPropertyType.Fraction or FormPropertyType.Reference
            or FormPropertyType.CssClasses;

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

    /// <summary>
    /// What the three editor properties read: <see cref="DisplayValue"/>, except for the one moment
    /// <see cref="RaiseEditorRefresh"/> ECHOES a refused value back (see there).
    /// </summary>
    private string EditorText => _editorEcho ?? DisplayValue;

    /// <summary>Set only inside <see cref="RaiseEditorRefresh"/>'s posted step; null otherwise.</summary>
    private string? _editorEcho;

    /// <summary>The editor's text: <see cref="DisplayValue"/> (frozen → raw; absent → the default).</summary>
    public string StringValue
    {
        get => EditorText;
        set => Commit(value);
    }

    public bool BoolValue
    {
        get => bool.TryParse(EditorText, out var parsed) && parsed;
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
        get => FormPropertyDef.TryParseInt(EditorText, out var parsed) ? parsed : 0;
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
    private static readonly FormAnchorEdges[] EdgeOrder =
    {
        FormAnchorEdges.Top, FormAnchorEdges.Bottom, FormAnchorEdges.Left, FormAnchorEdges.Right
    };

    public bool AnchorTop
    {
        get => HasEdge(FormAnchorEdges.Top);
        set => SetEdge(FormAnchorEdges.Top, value);
    }

    public bool AnchorBottom
    {
        get => HasEdge(FormAnchorEdges.Bottom);
        set => SetEdge(FormAnchorEdges.Bottom, value);
    }

    public bool AnchorLeft
    {
        get => HasEdge(FormAnchorEdges.Left);
        set => SetEdge(FormAnchorEdges.Left, value);
    }

    public bool AnchorRight
    {
        get => HasEdge(FormAnchorEdges.Right);
        set => SetEdge(FormAnchorEdges.Right, value);
    }

    /// <summary>
    /// ⛔⛔ An UNSET Anchor is not "anchored to nothing" — WinForms defaults a control to
    /// <c>Top, Left</c>. Showing four empty edges would tell the user something false about a form
    /// that has never been touched, and the first edge they clicked would appear to ADD an anchor
    /// while silently REMOVING the two they already had.
    ///
    /// <para>⚠ Reading the default does not WRITE it: the document keeps its null until the user
    /// actually toggles an edge, so opening a form and closing it changes nothing.</para>
    ///
    /// <para>⛔ Read through <see cref="FormAnchor.Parse"/>, the ONE Anchor parser (plan 2026-09-27 scope
    /// call S10) — its absent-means-Top|Left default is the one described above. A second split here would
    /// be a picker showing edges the region writer and the page do not read.</para>
    /// </summary>
    private bool HasEdge(FormAnchorEdges edge) => Edges.HasFlag(edge);

    private FormAnchorEdges Edges => FormAnchor.Parse(RawValue, out _);

    /// <summary>
    /// Rewrites the whole set, in <see cref="EdgeOrder"/>, one spelling per edge. ⚠ An unknown edge name
    /// in the old value is DROPPED: the picker writes only edges it has. Such a document was already
    /// refused by the region writer (<c>AnchorNotExpressible</c>), so nothing that built is lost.
    /// </summary>
    private void SetEdge(FormAnchorEdges edge, bool on)
    {
        var current = Edges;
        if (current.HasFlag(edge) == on)
        {
            return;
        }

        var next = on ? current | edge : current & ~edge;
        var edges = EdgeOrder.Where(e => next.HasFlag(e)).Select(e => e.ToString()).ToList();

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

        // ⚠ An editor pushing BACK the echoed text (a future ranged NumericUpDown coercing it) must not start
        // another commit, refusal and echo — that would loop.
        if (_editorEcho != null)
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

        // The last refusal is about the LAST commit only: any other outcome retracts it.
        // ⚠ DescribeRefusedEdit throws for a usable value, so it is asked only on the Refuse verdict,
        // which Judge gives exactly when the catalog does not accept the value.
        // Cleared first, so a SECOND identical refusal still raises a change the pane can hear.
        Refusal = null;
        if (verdict == FormEditVerdict.Refuse)
        {
            Refusal = _definition!.DescribeRefusedEdit(value, _target);
        }

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
                    RaiseEditorRefresh(value);
                }

                return;

            case FormEditVerdict.Refuse:
                // Spec §7: never written; the editor re-reads, so it shows what the document holds.
                RaiseEditorRefresh(value);
                return;
        }

        // ⛔ Stored in the DOCUMENT's vocabulary: an Opacity typed as "80%" is written as WinForms' "0.8" (owner decision
        // 2026-09-29) — the catalog's one conversion, a no-op for every other type.
        value = _definition?.ToDocument(value) ?? value;

        if (_write != null)
        {
            // ⛔ A write that changed nothing is not an edit: refused by the store (a non-positive
            // ClientSize, unparseable Int text) or the same value re-spelled ("007" on an X of 7). No
            // Edited, and the editor re-reads what the model holds.
            if (!_write(value))
            {
                // ⛔ A store refusal is SAID (plan 2026-09-27 Task 9): a value the catalog accepts and the store refuses
                // (ClientSize "0, 300", MobileBreakpoint -5) used to vanish with no reason. "The same value again"
                // has none, so this stays null for it.
                Refusal = _storeRefusal?.Invoke(value);
                RaiseEditorRefresh(value);
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

    /// <summary>
    /// Why the last value typed into this row was REFUSED (spec §7 "refused in the editor, never
    /// written"), or null when the last commit was not a refusal. The grid's description pane shows it:
    /// without it a refused value simply vanished from the box, with nothing saying why.
    /// </summary>
    [ObservableProperty]
    private string? _refusal;

    /// <summary>
    /// A refused edit: the editor re-reads the value the document still holds.
    ///
    /// <para>⛔⛔ Measured headless on Avalonia 11.3
    /// (<c>FormPropertyGridRealViewTests.ARefusedValue_SnapsTheRealEditorBack_…</c>): raising
    /// PropertyChanged(StringValue) alone does NOT snap a real TextBox back — the refused <c>12345</c>
    /// stayed in the box while the document held nothing. Neither synchronously (the refusal happens
    /// INSIDE the binding's own LostFocus push) nor posted after it: the typed text lives in the TextBox as
    /// its CURRENT value, over the binding, and the binding re-applies only a value that DIFFERS from the
    /// last one it read — which is the very value being snapped back to ("" before, "" after). So the
    /// posted step first ECHOES the pushed text (the binding now holds what the box shows: no visible
    /// change), then raises the real value, which now differs and is applied. The same holds for the Int
    /// editor (Width pushed to 0 and clamped back to the 1 it already was — measured through the real
    /// NumericUpDown by <c>AnIntTheStoreClampsBackToTheSameValue_SnapsTheRealNumericUpDownBack</c>).</para>
    ///
    /// <para>The synchronous raise stays for every listener that is not a binding mid-push.</para>
    ///
    /// <para>The post runs at Default priority, above Input, so a second edit cannot be processed between the
    /// refusal and its snap-back; and a post that lands after a rebuild raises on an orphan row that no live
    /// binding reads any more — harmless.</para>
    ///
    /// <para>⚠ View-model [Test]s never drain the dispatcher, so the posted echo is covered ONLY by the
    /// real-view tests.</para>
    /// </summary>
    /// <param name="pushed">The text the editor pushed, and still shows.</param>
    private void RaiseEditorRefresh(string pushed)
    {
        RaiseEditorProperties();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _editorEcho = pushed;
            try
            {
                RaiseEditorProperties();
            }
            finally
            {
                _editorEcho = null;
            }

            RaiseEditorProperties();
        });
    }

    private void RaiseEditorProperties()
    {
        OnPropertyChanged(nameof(StringValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(IntValue));
    }

    /// <summary>
    /// This row's value changed: every view of it re-reads — and, for a composite, every PART re-reads too (a part is a
    /// view of the parent's value). A part that changed asks its PARENT, so its siblings follow (Padding's All and Left).
    /// </summary>
    private void RaiseValueChanged()
    {
        if (Parent != null)
        {
            Parent.RaiseValueChanged();
            return;
        }

        RaiseOwnValueChanged();
        foreach (var child in _children)
        {
            child.RaiseOwnValueChanged();
        }
    }

    private void RaiseOwnValueChanged()
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
