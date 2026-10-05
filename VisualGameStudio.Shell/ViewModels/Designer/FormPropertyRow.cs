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
        Func<string, string?>? storeRefusal,
        Func<IReadOnlyList<string>>? referenceCandidates)
    {
        _onChanged = onChanged;
        _referenceCandidates = referenceCandidates;
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
    /// <param name="referenceCandidates">For a <see cref="FormPropertyType.Reference"/> row: the Ids it may name, asked on
    /// EVERY read of <see cref="Choices"/> (<c>FormReferences.Candidates</c>) — never captured here, so the list follows
    /// the document (slice 4 D-7).</param>
    public static FormPropertyRow ForStoredValue(
        FormPropertyDef definition,
        FormTarget target,
        Func<string?> read,
        Func<string, bool> write,
        Action? remove,
        Action onChanged,
        string? frozenReason = null,
        string? frozenText = null,
        Func<string, string?>? storeRefusal = null,
        Func<IReadOnlyList<string>>? referenceCandidates = null) =>
        new(definition, target, read, write, remove, frozenReason, frozenText, onChanged, storeRefusal, referenceCandidates);

    /// <summary>A Reference row's candidate Ids, asked on every read; null for any other row. See <see cref="ForStoredValue"/>.</summary>
    private readonly Func<IReadOnlyList<string>>? _referenceCandidates;

    // ==================================================================
    // MERGED mode (property-grid slice 6, pre-flight 2026-10-05 D-3/D-5): ONE row over the same row of several selected
    // controls. A mode, not a class, so the view's templates, the display list, search, sort and collapse work unchanged.
    // ==================================================================

    /// <summary>The members — the same row of each selected control, in SELECTION order (primary LAST); null when not merged.</summary>
    private readonly IReadOnlyList<FormPropertyRow>? _members;

    /// <summary>Whose row each member is (same order as <see cref="_members"/>) — a frozen or refused member is NAMED.</summary>
    private readonly IReadOnlyList<FormControl>? _owners;

    /// <summary>The gesture tally every member was built with (<see cref="FormEditTally.Mark"/> as its change callback).</summary>
    private readonly FormEditTally? _tally;

    /// <summary>A merged row (slice 6): see <see cref="Merged"/>.</summary>
    private FormPropertyRow(
        IReadOnlyList<FormPropertyRow> members, IReadOnlyList<FormControl> owners, FormEditTally tally, Action onChanged)
    {
        var primary = members[^1];
        _members = members;
        _owners = owners;
        _tally = tally;
        _onChanged = onChanged;
        Name = primary.Name;
        _type = primary._type;
        _choices = primary._choices;
        _definition = primary._definition;
        _target = primary._target;
        _editor = primary._editor;
        Category = primary.Category;
        Description = primary.Description;

        // D-3: a frozen member (D9 Degraded) freezes the set, and the reason names it — nothing may coerce a preserved value,
        // even in company.
        var frozen = members.Select((m, i) => (Member: m, Owner: owners[i])).FirstOrDefault(x => x.Member.IsFrozen);
        FrozenReason = frozen.Member != null ? $"'{frozen.Owner.Id}': {frozen.Member.FrozenReason}" : null;
    }

    /// <summary>
    /// For a composite PART: how it composes the parent's WHOLE value from a part value (null = nothing to write). Set by
    /// <see cref="FormCompositeRows"/>; read by <see cref="Preview"/>, so a merged part can pre-judge each member's whole
    /// (slice 6 D-5) without writing anything.
    /// </summary>
    internal Func<string, string?>? Compose { get; set; }

    /// <summary>
    /// What a commit of <paramref name="value"/> WOULD do to this row — the same decision <see cref="Commit"/> takes
    /// (the catalog's Judge, or the intrinsic no-op rule; a part asks its parent about the composed whole) — writing
    /// nothing. The refusal's reason comes with a Refuse. Slice 6 D-5: a merged row pre-judges every member with this.
    /// </summary>
    internal (FormEditVerdict Verdict, string? Refusal) Preview(string value)
    {
        if (IsFrozen)
        {
            return (FormEditVerdict.Refuse, FrozenReason);
        }

        if (Compose != null && Parent != null)
        {
            return Compose(value) is { } whole ? Parent.Preview(whole) : (FormEditVerdict.NoOp, null);
        }

        if (_definition != null)
        {
            var verdict = _definition.Judge(value, IsPresent ? RawValue : null, _target);
            return (verdict, verdict == FormEditVerdict.Refuse ? _definition.DescribeRefusedEdit(value, _target) : null);
        }

        return (IsSameIntrinsicValue(value, DisplayValue) ? FormEditVerdict.NoOp : FormEditVerdict.Write, null);
    }

    /// <summary>
    /// ⛔⛔ The multi-edit (slice 6 D-5): ONE Edited, ONE write, ONE undo step — and all-or-nothing.
    /// <list type="number">
    /// <item>Every member is PRE-JUDGED (<see cref="Preview"/>). If any refuses, nothing is written: the refusal names
    /// the members (<c>'txt' (TextBox): …</c>) and the editor snaps back. VS cancels the whole transaction too.</item>
    /// <item>A Reset verdict on any member is the Reset gesture: done for all when every member can reset, otherwise the
    /// editor snaps back and nothing is written.</item>
    /// <item>Otherwise each member commits through its OWN rule (its own no-op / write against its own value), counted by
    /// the tally the members were built with, and <see cref="_onChanged"/> is raised ONCE when anything changed.</item>
    /// </list>
    /// </summary>
    private void CommitMerged(string value)
    {
        var verdicts = _members!.Select((m, i) => (Member: m, Owner: _owners![i], Judged: m.Preview(value))).ToList();

        Refusal = null;
        var refused = verdicts.Where(v => v.Judged.Verdict == FormEditVerdict.Refuse).ToList();
        if (refused.Count > 0)
        {
            Refusal = string.Join(" ", refused.Select(r => $"'{r.Owner.Id}' ({r.Owner.Kind}): {r.Judged.Refusal}"));
            RaiseEditorRefresh(value);
            return;
        }

        if (verdicts.Any(v => v.Judged.Verdict == FormEditVerdict.Reset))
        {
            if (CanReset)
            {
                ResetMerged();
            }
            else
            {
                RaiseEditorRefresh(value);
            }

            return;
        }

        _tally!.Reset();
        foreach (var member in _members!)
        {
            member.Commit(value);
        }

        if (_tally.Count == 0)
        {
            // Nothing moved (every member already showed it, or the store declined it everywhere — unparseable Int text):
            // the editor re-reads what the members show.
            RaiseEditorRefresh(value);
            return;
        }

        RaiseValueChanged();
        _onChanged();
    }

    /// <summary>Reset over a merged row (offered only when every member can, D-3): every member resets, ONE Edited.</summary>
    private void ResetMerged()
    {
        _tally!.Reset();
        foreach (var member in _members!)
        {
            member.Reset();
        }

        if (_tally.Count > 0)
        {
            RaiseValueChanged();
            _onChanged();
        }
    }

    /// <summary>
    /// D-9: the document changed under the rows while the selection stood (an Arrange, a drag, an undo-less refresh) —
    /// every view of this row re-reads, parts and members included. ⛔ Notifications ONLY: never a commit, never the editor
    /// echo; a row inside its refused value's posted two-step echo (<see cref="_editorEcho"/> set) is skipped, its posted
    /// step owns the refresh (pre-flight D-9 re-entrancy rule).
    /// </summary>
    internal void RefreshValue()
    {
        if (_editorEcho != null)
        {
            return;
        }

        RaiseOwnValueChanged();
        foreach (var child in _children)
        {
            child.RefreshValue();
        }

        foreach (var member in Members)
        {
            member.RefreshValue();
        }
    }

    /// <summary>
    /// ONE row over <paramref name="members"/> — the same row (<see cref="FormPropertyDef.SharesShapeWith"/>, or the same
    /// intrinsic row) of each selected control, in selection order with the PRIMARY last; <paramref name="owners"/> are
    /// their controls in the same order. Every member must have been built with <paramref name="tally"/>'s
    /// <see cref="FormEditTally.Mark"/> as its change callback; <paramref name="onChanged"/> is raised ONCE per gesture.
    /// A composite's parts are merged too, part by part (each member's own parts, by index).
    /// </summary>
    public static FormPropertyRow Merged(
        IReadOnlyList<FormPropertyRow> members, IReadOnlyList<FormControl> owners, FormEditTally tally, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(owners);
        if (members.Count < 2 || owners.Count != members.Count)
        {
            throw new ArgumentException("a merged row has two or more members, each with its owner", nameof(members));
        }

        var merged = new FormPropertyRow(members, owners, tally, onChanged);
        var parts = members[^1].Children.Count;
        if (parts > 0 && members.All(m => m.Children.Count == parts))
        {
            merged.AdoptChildren(Enumerable.Range(0, parts)
                .Select(i => Merged(members.Select(m => m.Children[i]).ToList(), owners, tally, onChanged))
                .ToList());
        }

        return merged;
    }

    /// <summary>True for a row over several selected controls (slice 6).</summary>
    public bool IsMerged => _members != null;

    /// <summary>A merged row's members, primary last; empty for an ordinary row.</summary>
    public IReadOnlyList<FormPropertyRow> Members => _members ?? Array.Empty<FormPropertyRow>();

    /// <summary>
    /// D-3: a merged row whose members SHOW different values — it shows blank. Each member's own canonical display is
    /// compared (ordinal), so two absent rows whose kinds default differently are mixed too.
    /// </summary>
    public bool IsMixed => _members != null && !AllShow(m => m.DisplayValue);

    private bool AllShow(Func<FormPropertyRow, string> read)
    {
        var first = read(_members![0]);
        return _members.All(m => string.Equals(read(m), first, StringComparison.Ordinal));
    }

    /// <summary>The members' shared text, or "" when they differ (D-3: VS shows blank, never the primary's value).</summary>
    private string Shared(Func<FormPropertyRow, string> read) => AllShow(read) ? read(_members![0]) : "";

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

    /// <summary>
    /// The catalog row, for a composite helper that needs its type; null for an intrinsic row. Public since slice 6: the
    /// multi-select tests judge each member through its own catalog row (the Shell grants the tests no internals).
    /// </summary>
    public FormPropertyDef? Definition => _definition;

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

    /// <summary>
    /// ⛔ Always false in the designer's grid (slice 4 D-3). The shared editor's switch is the SETTINGS dialog's Bool
    /// editor; the grid shows a Bool as VS does, a True/False drop-down — the shared editor's existing ComboBox arm
    /// (<see cref="IsComboBox"/>). Replacing the switch inside <c>TypedValueEditor</c> would have changed Settings too.
    /// </summary>
    public bool IsCheckBox => false;

    /// <summary>
    /// ⚠ Not while a merged row is MIXED (slice 6 D-4): an int cannot say "blank" and the NumericUpDown would show a false
    /// 0 (measured, pre-flight M5) — that row takes the text box (<see cref="IsTextBox"/>). Both flags are raised whenever
    /// the value changes, so the swap follows the mixedness.
    /// </summary>
    public bool IsNumericUpDown => Typed && _type == FormPropertyType.Int && !IsMixed;

    /// <summary>
    /// A drop-down: an Enum's members, a Cursor row's <c>Cursors</c> members (<see cref="FormPropertyDef.Choices"/>), a
    /// Bool's <c>True</c>/<c>False</c> (slice 4 D-3), or a Reference's <c>(none)</c> and the controls it may name (D-7).
    /// </summary>
    public bool IsComboBox => Typed &&
        _type is FormPropertyType.Enum or FormPropertyType.Cursor or FormPropertyType.Bool or FormPropertyType.Reference;

    /// <summary>
    /// Free text: a String, and the typed-text rows — a Color, a Size (<c>800, 450</c>), a Font (FontConverter text),
    /// a Padding (<c>4</c> or <c>4, 2, 4, 2</c>) and a Fraction (<c>0.85</c>). The composite rows (slice 3 Task 6) and the
    /// colour and font editors (slice 4) sit beside this text, which stays the parent's own editor. (A Reference is a
    /// drop-down since slice 4 D-7: <see cref="IsComboBox"/>.)
    /// </summary>
    public bool IsTextBox => Typed && !IsCollectionEditor &&
        (_type == FormPropertyType.Int && IsMixed ||
         _type is FormPropertyType.String or FormPropertyType.Color or FormPropertyType.Size
            or FormPropertyType.Font or FormPropertyType.Padding or FormPropertyType.Fraction
            or FormPropertyType.CssClasses
            // Slice 4 Task 8: an image/icon path is typed text (Resources/logo.png) — the picker (Task 10) sits beside it.
            or FormPropertyType.Image or FormPropertyType.Icon);

    /// <summary>
    /// An item collection (ComboBox / ListBox / CheckedListBox <c>Items</c>, slice 4 Task 7): VS's read-only
    /// <c>(Collection)</c> text and a <c>…</c> that opens the String Collection Editor — never a one-line text box, which
    /// cannot hold one item per line.
    /// </summary>
    public bool IsCollectionEditor => Typed && FormItems.IsCollection(_definition);

    /// <summary>What an item-collection row shows in its value cell, as VS does.</summary>
    public string CollectionSummary => "(Collection)";

    /// <summary>
    /// The String Collection Editor's OK (the model value, ADR 0020): an empty list is RESET — the property leaves the
    /// document, never <c>Items=""</c> — and anything else goes through the row's own Commit (the same list again is a
    /// no-op, per Judge).
    /// </summary>
    public void ApplyItems(string modelValue)
    {
        if (FormItems.Split(modelValue).Count == 0)
        {
            Reset(); // checks CanReset itself: an absent list stays absent
            return;
        }

        Commit(modelValue);
    }

    /// <summary>The four-edge Anchor box (Task 26).</summary>
    public bool IsAnchorPicker => IsEditable && _editor == FormRowEditor.AnchorPicker;

    /// <summary>The nine-region Dock box (Task 26).</summary>
    public bool IsDockPicker => IsEditable && _editor == FormRowEditor.DockPicker;

    /// <summary>
    /// A colour row: its value cell carries VS's colour drop-down (<c>FormColorDropDown</c>: Custom / Web / System, slice 4
    /// D-1) to the LEFT of the text box, which stays the row's own editor (typed <c>#hex</c> or a name, D-1a). ⚠ Requires
    /// the default editor like every type-driven flag, and is false while frozen: a Degraded colour shows its raw text only.
    /// </summary>
    public bool IsColor => Typed && _type == FormPropertyType.Color;

    private IReadOnlyList<FormColorChoice>? _webColors;
    private IReadOnlyList<FormColorChoice>? _systemColors;

    /// <summary>The drop-down's Web tab: the named colours this row's target accepts (<see cref="FormColorChoices.Web"/>).</summary>
    public IReadOnlyList<FormColorChoice> WebColorChoices =>
        _webColors ??= _definition == null ? Array.Empty<FormColorChoice>() : FormColorChoices.Web(_definition, _target);

    /// <summary>The drop-down's System tab: all 33 on WinForms, the 8 with CSS on the web (<see cref="FormColorChoices.System"/>).</summary>
    public IReadOnlyList<FormColorChoice> SystemColorChoices =>
        _systemColors ??= _definition == null ? Array.Empty<FormColorChoice>() : FormColorChoices.System(_definition, _target);

    /// <summary>
    /// Whether the Custom tab offers an alpha channel: false on a WinForms row whose control throws on a translucent colour
    /// (a TextBox's BackColor — <see cref="FormPropertyDef.AcceptsTranslucentOn"/>, the catalog's answer).
    /// </summary>
    public bool AllowsAlpha => _definition?.AcceptsTranslucentOn(_target) ?? true;

    /// <summary>The colour the row's value displays as — the drop-down's swatch and the Custom tab's start. Preview only.</summary>
    public Avalonia.Media.Color? SwatchColor =>
        IsColor && FormColorChoices.TryResolve(DisplayValue, out var color) ? color : null;

    /// <summary>The swatch brush, or null (the swatch then shows "?") when the value names nothing this machine can draw.</summary>
    public Avalonia.Media.IBrush? Swatch =>
        SwatchColor is { } color ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color) : null;

    /// <summary>
    /// True when a colour row SHOWS a value that cannot be previewed (a CSS name only the browser knows) — the swatch
    /// shows "?" instead of a colour. An empty value (no default: a Label's BackColor inherits) is an empty swatch, not "?".
    /// </summary>
    public bool HasUnknownSwatch => IsColor && DisplayValue.Length > 0 && SwatchColor == null;

    /// <summary>
    /// A colour picked in the drop-down — a Web or System name (one pick, one write) or the Custom tab's colour when the
    /// pop-up closes (D-1c: once, never per drag step). Through the same Commit as typed text, so Judge decides: the same
    /// colour is a no-op, a refused one is said.
    /// </summary>
    public void ApplyColor(string value) => Commit(value);

    // ==================================================================
    // The Font dialog (slice 4 D-2): a `…` button on a Font row opens it; OK writes ONE canonical value
    // ==================================================================

    /// <summary>
    /// What this row's control INHERITS for the property when it sets none (<c>FormAmbient.Inherited</c>), or null when
    /// there is nothing to inherit from (the Form's own rows). Set once by <see cref="FormCompositeRows.Attach"/>; the Font
    /// parts and the Font dialog both read it, through <see cref="EffectiveFont"/>.
    /// </summary>
    internal Func<string?>? Inherited { get; set; }

    /// <summary>The target whose value rules this row applies — the Font dialog shows the web hint on <see cref="FormTarget.Web"/>.</summary>
    public FormTarget Target => _target;

    /// <summary>A Font row carries VS's <c>…</c> button, which opens the Font dialog. Not while frozen (the typed-editor rule).</summary>
    public bool HasEllipsis => Typed && _type == FormPropertyType.Font;

    /// <summary>
    /// An Image/Icon row carries a <c>…</c> that opens a file picker (slice 4 Task 10); the typed path stays the row's own
    /// editor beside it. Not while frozen.
    /// </summary>
    public bool HasAssetPicker => Typed && _type is FormPropertyType.Image or FormPropertyType.Icon;

    /// <summary>The picker's result: ONE value through the row's own Commit (Judge decides; a refused value is said).</summary>
    public void ApplyAsset(string value) => Commit(value);

    /// <summary>
    /// The font the dialog starts from: the row's value, or — absent — what its control inherits. Merged (slice 6 D-4): the
    /// PRIMARY's effective font — the shared one when the members agree, a useful start when they differ (OK writes one
    /// whole font to all either way).
    /// </summary>
    public FormFontValue? EffectiveFont => _members != null ? _members[^1].EffectiveFont : FormCompositeRows.EffectiveFont(this);

    /// <summary>The Font dialog's OK: ONE canonical value through the row's own Commit (fan-in; Judge decides).</summary>
    public void ApplyFont(string canonical) => Commit(canonical);

    /// <summary>VS's two Bool items, in VS's order — the spelling <see cref="StringValue"/> shows a Bool in.</summary>
    private static readonly IReadOnlyList<string> BoolChoices = new[] { "True", "False" };

    /// <summary>
    /// The filtered <see cref="Choices"/>, computed once. ⚠ Assumes <see cref="_choices"/>, <see cref="_definition"/> and
    /// <see cref="_target"/> never change for this row — all three are readonly today. ⛔ A Reference row's list follows
    /// the DOCUMENT and never goes through this cache (<see cref="ReferenceChoices"/>, asked on every read).
    /// </summary>
    private IReadOnlyList<string>? _offered;

    /// <summary>
    /// What the drop-down OFFERS. ⛔ The catalog's answer, filtered through <see cref="FormPropertyDef.Accepts(string?,
    /// FormTarget)"/> on this row's target (slice 4 D-4): a web Cursor row drops the members with no CSS, generically —
    /// there is no Cursor special case, and never a hand list. Offering a member Judge would then REFUSE is a drop-down
    /// whose items do nothing.
    /// </summary>
    public IReadOnlyList<string>? Choices =>
        _type == FormPropertyType.Bool ? BoolChoices
        : _type == FormPropertyType.Reference ? ReferenceChoices()
        : _definition == null || _choices == null ? _choices
        : _offered ??= _choices.Where(choice => _definition.Accepts(choice, _target)).ToList();

    // ==================================================================
    // The Reference drop-down (slice 4 D-7): (none), then the controls the row may name
    // ==================================================================

    /// <summary>
    /// The Reference drop-down's first item. ⛔ A DISPLAY item, never a value: picking it is Reset, mapped before Judge
    /// is asked (<see cref="CommitReference"/>). No control can carry it as an Id — parentheses are not legal there.
    /// </summary>
    internal const string NoReferenceItem = "(none)";

    /// <summary>
    /// Marks a stored Id that names no candidate (BL8034 at build): <c>btnGone (missing)</c>. An Id holds no space, so
    /// the marked text can never be a real Id either.
    /// </summary>
    internal const string MissingReferenceSuffix = " (missing)";

    private IReadOnlyList<string> ReferenceCandidates => _referenceCandidates?.Invoke() ?? Array.Empty<string>();

    /// <summary>The last list <see cref="ReferenceChoices"/> handed out — returned again while its items are unchanged.</summary>
    private IReadOnlyList<string>? _referenceList;

    /// <summary>Stored Ids this row has shown marked missing, in the order first shown — kept for the row's life.</summary>
    private readonly List<string> _missingShown = new();

    /// <summary>
    /// <c>(none)</c>, the candidates in document order (<c>FormReferences.Candidates</c>, asked NOW — the list follows the
    /// document), and — when the stored Id names none of them — that Id marked missing, so the row never shows
    /// <c>(none)</c> over a value the document holds.
    ///
    /// <para>⛔⛔ The SAME list instance comes back while its items are unchanged, and a missing Id stays listed for the
    /// row's life. Measured through the real ComboBox: a pick commits INSIDE the combo's own SelectedItem push, and the
    /// commit raises <c>Choices</c> (here, and again through the document view model's revision). A NEW list there swaps
    /// the combo's ItemsSource mid-push, and the combo pushed its PREVIOUS item back — <c>(none)</c>, i.e. Reset — so
    /// picking <c>btn</c> wrote it and removed it in one click. An unchanged instance is no ItemsSource change at all; a
    /// sticky missing entry means picking a real Button over a dangling Id does not shrink the list mid-push either.</para>
    /// </summary>
    private IReadOnlyList<string> ReferenceChoices()
    {
        var candidates = ReferenceCandidates; // asked ONCE per read: the display below uses the same list
        var stored = ReferenceDisplay(candidates);
        if (stored.EndsWith(MissingReferenceSuffix, StringComparison.Ordinal))
        {
            var id = stored[..^MissingReferenceSuffix.Length];
            if (!_missingShown.Contains(id, StringComparer.Ordinal))
            {
                _missingShown.Add(id);
            }
        }

        var list = new List<string> { NoReferenceItem };
        list.AddRange(candidates);
        list.AddRange(_missingShown
            .Where(id => !candidates.Contains(id, StringComparer.Ordinal))
            .Select(id => id + MissingReferenceSuffix));

        if (_referenceList == null || !_referenceList.SequenceEqual(list, StringComparer.Ordinal))
        {
            _referenceList = list;
        }

        return _referenceList;
    }

    /// <summary>What a Reference row's drop-down selects: <c>(none)</c> when absent, the Id, or the Id marked missing.</summary>
    private string ReferenceDisplay(IReadOnlyList<string> candidates)
    {
        var text = EditorText;
        if (text.Length == 0)
        {
            return NoReferenceItem;
        }

        // ⚠ An editor echo (RaiseEditorRefresh) hands back what the combo pushed, which is already a display item.
        // ⚠ Only an EDITABLE row reaches here: a hand-written "x (missing)" or "(none)" is not a legal Id, so it is Degraded
        // and frozen, and never read through these marks (FormPropertyGridTests.AHandWrittenDisplayMark_…).
        if (text == NoReferenceItem || text.EndsWith(MissingReferenceSuffix, StringComparison.Ordinal) ||
            candidates.Contains(text, StringComparer.Ordinal))
        {
            return text;
        }

        return text + MissingReferenceSuffix;
    }

    /// <summary>
    /// A pick in the Reference drop-down. ⛔ <c>(none)</c> is Reset (remove the property; a no-op when it is absent), and it
    /// is mapped HERE, before <see cref="Commit"/> asks Judge — Judge would refuse the text <c>(none)</c> as an illegal Id,
    /// and the stored Id would silently stay. A marked missing item is its Id without the mark: the value already stored
    /// (a no-op, per Judge) or, after the user picked a real Button, the dangling Id they are putting back. Anything else
    /// is an Id, committed as usual.
    /// </summary>
    private void CommitReference(string? value)
    {
        if (value == NoReferenceItem)
        {
            Reset(); // checks CanReset itself: absent or frozen → nothing
            return;
        }

        if (value != null && value.EndsWith(MissingReferenceSuffix, StringComparison.Ordinal))
        {
            value = value[..^MissingReferenceSuffix.Length];
        }

        Commit(value);
    }

    /// <summary>
    /// The document changed under this row (the grid's <c>RefreshReferenceChoices</c>): a Reference row's bound drop-down
    /// re-reads its items, then its selection — in that order, so the selected item is one of the new items.
    /// </summary>
    internal void RefreshChoices()
    {
        if (_type != FormPropertyType.Reference)
        {
            return;
        }

        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(StringValue));
    }

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
        _members != null ? _members.Any(m => m.IsPresent)
        : _isPresent?.Invoke() ?? (_control != null ? _control.Properties.ContainsKey(Name) : true);

    /// <summary>The target's default, canonicalised — what an absent row shows. Null = no static default.</summary>
    public string? DefaultValue =>
        _definition?.DefaultFor(_target) is { } d ? _definition.Canonical(d) : null;

    /// <summary>
    /// Greyed: the row shows a default the document does not carry. Merged (slice 6 D-3): every member greyed and not
    /// mixed — a mixed row is never grey.
    /// </summary>
    public bool IsDefaultShown =>
        _members != null ? !IsMixed && _members.All(m => m.IsDefaultShown)
        : _definition != null && !IsPresent;

    /// <summary>
    /// ⛔ Present AND different from the displayed default. A null default DISPLAYS as empty, so a
    /// present empty Text is not bold while a present "Hi" is.
    ///
    /// <para>Merged (slice 6 D-3, VS's merged <c>ShouldSerializeValue</c>): bold when ANY member is bold.</para>
    /// </summary>
    public bool IsBold =>
        _members != null ? _members.Any(m => m.IsBold)
        : _definition != null && IsPresent && !_definition.SameValue(DisplayValue, _definition.Displayed(null, _target));

    /// <summary>
    /// Offered on present, editable rows that know how to remove themselves.
    ///
    /// <para>Merged (slice 6 D-3, VS's merged <c>CanResetValue</c>): only when EVERY member can reset — so one member set
    /// and one absent is bold with no Reset, deliberately. (A frozen member cannot reset, so a frozen set offers none.)</para>
    /// </summary>
    public bool CanReset =>
        _members != null ? _members.All(m => m.CanReset)
        : IsEditable && IsPresent && (_reset != null || (_control != null && _definition != null));

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

        if (_members != null)
        {
            ResetMerged();
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
        _members != null ? Shared(m => m.RawValue)
        : _read != null ? _read()
        : _control != null && _control.Properties.TryGetValue(Name, out var value) ? value : "";

    /// <summary>
    /// What the editor shows: the document's value in its CANONICAL spelling (spec §2.8) when present —
    /// a legacy <c>TextAlign="Left"</c> shows as <c>MiddleLeft</c>, which is what the nine-member combo
    /// can match — and the target's default when absent (spec §2.7).
    ///
    /// <para>⛔ Except when FROZEN: a Degraded row shows the document's text exactly, because its
    /// reason quotes that text and says it is "preserved exactly as written".</para>
    ///
    /// <para>Merged (slice 6 D-3): what every member SHOWS when they all show the same, else "" (<see cref="IsMixed"/>) —
    /// each member applies its own rule above (a frozen member its raw text).</para>
    /// </summary>
    public string DisplayValue =>
        _members != null ? Shared(m => m.DisplayValue)
        : IsFrozen ? RawValue
        : _definition != null ? _definition.Displayed(IsPresent ? RawValue : null, _target)
        : RawValue;

    /// <summary>
    /// What the three editor properties read: <see cref="DisplayValue"/>, except for the one moment
    /// <see cref="RaiseEditorRefresh"/> ECHOES a refused value back (see there).
    /// </summary>
    private string EditorText => _editorEcho ?? DisplayValue;

    /// <summary>Set only inside <see cref="RaiseEditorRefresh"/>'s posted step; null otherwise.</summary>
    private string? _editorEcho;

    /// <summary>
    /// The editor's text: <see cref="DisplayValue"/> (frozen → raw; absent → the default). ⚠ An editable Bool shows VS's
    /// <c>True</c>/<c>False</c>: the drop-down's SelectedItem must equal one of its items exactly, or it shows nothing and
    /// pushes null. The document keeps <c>true</c>/<c>false</c> (<see cref="FormPropertyDef.ToDocument"/>), and the
    /// no-op rule treats the two spellings as one value (<see cref="IsSameIntrinsicValue"/>, the catalog's Canonical).
    /// </summary>
    public string StringValue
    {
        get => IsEditableBool && bool.TryParse(EditorText, out var flag) ? (flag ? BoolChoices[0] : BoolChoices[1])
            : IsEditableReference ? ReferenceDisplay(ReferenceCandidates)
            : EditorText;
        set
        {
            // ⛔ Slice 6 D-4: an EMPTY push from a merged row's mixed editor is not an edit — blank is how "mixed" LOOKS. A
            // mixed combo (M4: it pushes null, guarded in Commit) or a mixed text box left without typing must never write
            // "" into every member (a String would take it; a typed row would read it as Reset). ⚠ Keyed on the EDITOR,
            // not on today's mixedness, for an Int row: its StringValue is pushed ONLY by the text box it gets while mixed
            // (the NumericUpDown pushes IntValue), so a text box dying after the row became un-mixed under the focus
            // still pushes its stale "" here — and is still ignored.
            if (_members != null && value is "" && (IsMixed || _type == FormPropertyType.Int))
            {
                return;
            }

            if (IsEditableReference)
            {
                CommitReference(value);
            }
            else
            {
                Commit(value);
            }
        }
    }

    /// <summary>
    /// A Reference row the grid edits through its drop-down (slice 4 D-7). ⚠ <see cref="UsesTypedEditor"/>, as
    /// <see cref="IsEditableBool"/>: a frozen row shows its raw text and is never mapped.
    /// </summary>
    private bool IsEditableReference => _type == FormPropertyType.Reference && UsesTypedEditor;

    /// <summary>
    /// VS's double-click on a Bool row: flips the value (slice 4 D-3). Through the same Commit as the drop-down, so Judge
    /// decides — an absent Enabled (shown True) writes <c>false</c>, a present <c>false</c> writes <c>true</c>. Nothing for
    /// a row that is not an editable Bool: a frozen row is never coerced. (Cycling an Enum on double-click is a follow-up.)
    /// </summary>
    /// <returns>Whether the value FLIPPED — the view marks the gesture handled only then.</returns>
    public bool ToggleBool()
    {
        if (!IsEditableBool)
        {
            return false;
        }

        var before = DisplayValue;
        // Slice 6 D-4: a MIXED merged Bool cycles to VS's first standard value — True for all.
        Commit(IsMixed ? "true" : BoolValue ? "false" : "true");
        return !string.Equals(before, DisplayValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// ONE answer to "is this a Bool the grid edits?" — what <see cref="StringValue"/>'s True/False display and
    /// <see cref="ToggleBool"/> both ask. ⚠ <see cref="UsesTypedEditor"/>, not <see cref="IsEditable"/>: it is the typed
    /// editor (the drop-down) that speaks True/False, and only a row that renders it may be toggled.
    /// </summary>
    private bool IsEditableBool => _type == FormPropertyType.Bool && UsesTypedEditor;

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

    /// <summary>
    /// VS's one-line Anchor text — <c>Top, Left</c>, edges in <see cref="EdgeOrder"/>, or <c>None</c> — shown beside the
    /// drop-down whose pop-up holds the four-edge box (slice 4 D-4). Read through the same parser as the box
    /// (<see cref="Edges"/>), so an unset Anchor says WinForms' default, never blank. Display only: never written.
    /// </summary>
    public string AnchorSummary
    {
        get
        {
            // Slice 6 D-4: a mixed merged Anchor says nothing (the box still starts from WinForms' Top, Left).
            if (IsMixed)
            {
                return "";
            }

            var edges = Edges;
            var names = EdgeOrder.Where(e => edges.HasFlag(e)).Select(e => e.ToString()).ToList();
            return names.Count == 0 ? "None" : string.Join(", ", names);
        }
    }

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
        // ⛔ The box is in a pop-up; this text is what stays on the row.
        OnPropertyChanged(nameof(AnchorSummary));
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

    // Slice 6 D-4: a MIXED merged Dock lights no region and its summary is blank.
    public bool IsDockedNone => !IsMixed && DockValue.Equals("None", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedTop => !IsMixed && DockValue.Equals("Top", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedBottom => !IsMixed && DockValue.Equals("Bottom", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedLeft => !IsMixed && DockValue.Equals("Left", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedRight => !IsMixed && DockValue.Equals("Right", StringComparison.OrdinalIgnoreCase);
    public bool IsDockedFill => !IsMixed && DockValue.Equals("Fill", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] DockRegions = { "None", "Top", "Bottom", "Left", "Right", "Fill" };

    /// <summary>
    /// The Dock row's one-line text beside its drop-down (slice 4 D-4): the region in <c>DockStyle</c>'s own spelling
    /// (<c>left</c> shows as <c>Left</c>), <c>None</c> when unset; an unknown word is shown as the document holds it.
    /// Display only: never written.
    /// </summary>
    public string DockSummary =>
        IsMixed ? "" : DockRegions.FirstOrDefault(r => r.Equals(DockValue, StringComparison.OrdinalIgnoreCase)) ?? DockValue;

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
                     nameof(IsDockedFill), nameof(DockSummary)
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

        // Slice 6: a merged row commits over its members — pre-judged, all-or-nothing, ONE Edited.
        if (_members != null)
        {
            CommitMerged(value);
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
                      ?? (IsSameIntrinsicValue(value, DisplayValue)
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
        value = _definition?.ToDocument(value) ?? IntrinsicDocumentText(value);

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
    /// The no-op rule for a row with NO catalog definition (the catalog's Judge covers every other row): the exact text —
    /// except a Bool, compared in the document's word (<see cref="FormPropertyDef.BoolWord"/>, the catalog's own rule).
    ///
    /// <para>⛔ Slice 4 D-3. The Font's Bold/Italic/Underline parts are catalog-less Bools read as <c>true</c>/<c>false</c>,
    /// and their drop-down speaks <c>True</c>/<c>False</c>. Ordinal, a push of the item the part already shows was a Write —
    /// and on an ABSENT ambient Font the part composes the inherited font and the parent STORES it: <c>Font="Segoe UI,
    /// 9pt"</c> and an undo entry for a value nobody changed (<c>FormCompositeRowTests.ABoldPart_PushedItsOwnShownItem_…</c>).
    /// ⚠ Measured: the real headless ComboBox does NOT push its item back on bind, so the real-view twin
    /// (<c>SelectingALabel_AndExpandingItsFont_ChangesNothing_…</c>) cannot see this mutant; the view-model test is the
    /// kill.</para>
    /// </summary>
    private bool IsSameIntrinsicValue(string value, string shown) =>
        string.Equals(IntrinsicDocumentText(value), IntrinsicDocumentText(shown), StringComparison.Ordinal);

    /// <summary>What a catalog-less row stores: the value itself, a Bool in the document's lower-case word.</summary>
    private string IntrinsicDocumentText(string value) =>
        _type == FormPropertyType.Bool ? FormPropertyDef.BoolWord(value) : value;

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
        // A Reference row's ITEMS depend on its value (a stored Id naming no candidate is listed, marked missing), so they
        // re-read first — before StringValue, so the new selection is one of the new items.
        if (_type == FormPropertyType.Reference)
        {
            OnPropertyChanged(nameof(Choices));
        }

        foreach (var name in new[]
                 {
                     nameof(RawValue), nameof(DisplayValue), nameof(StringValue), nameof(BoolValue),
                     nameof(IntValue), nameof(IsPresent), nameof(IsBold), nameof(IsDefaultShown), nameof(CanReset),
                     // Slice 6: mixedness can change with the value, and these follow it (D-4).
                     nameof(IsMixed), nameof(IsNumericUpDown), nameof(IsTextBox), nameof(AnchorSummary),
                     nameof(AnchorTop), nameof(AnchorBottom), nameof(AnchorLeft), nameof(AnchorRight),
                     nameof(DockValue), nameof(DockSummary), nameof(IsDockedNone), nameof(IsDockedTop),
                     nameof(IsDockedBottom), nameof(IsDockedLeft), nameof(IsDockedRight), nameof(IsDockedFill),
                     nameof(SwatchColor), nameof(Swatch), nameof(HasUnknownSwatch)
                 })
        {
            OnPropertyChanged(name);
        }

        ResetCommand.NotifyCanExecuteChanged();
    }
}
