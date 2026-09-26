using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// The designer's property grid (spec §3): the object selector, Categorized/Alphabetical, search, the
/// rows, and the description pane.
///
/// <para>⛔ Rows come from <see cref="FormControlCatalog"/> (or <see cref="FormControlCatalog.FormRoot"/>
/// for the form), never from whatever the document happens to carry. The catalog is the single source
/// of truth for "what can be set on this control" — the same table the markup emitter, the region
/// writer and the CI gate read. Building rows from the document's own attributes instead would offer
/// exactly the properties already set and no way to add a new one.</para>
///
/// <para>⛔⛔ The grid NEVER selects anything itself. The object selector raises
/// <see cref="SelectionRequested"/>; the document view model answers through its ONE selection store
/// (<c>SelectInDesigner</c>), which then sets <see cref="SelectedControl"/>. A grid that wrote its own
/// selection is the two-store disagreement that once deleted the wrong component (CLAUDE.md).</para>
///
/// <para>⚠ <see cref="Rows"/> stays the model: catalog order, every row. <see cref="DisplayItems"/> is
/// the VIEW of it — headers, sort, search, collapse — so every test and caller that reads
/// <see cref="Rows"/> keeps its meaning.</para>
///
/// <para>⚠ Identifiers here are <c>Form*</c>, not <c>WebView*</c> — that prefix is taken by the
/// extension host's HTML source document type across Core, Shell, DockFactory and ViewLocator.</para>
/// </summary>
public partial class FormPropertyGridViewModel : ObservableObject
{
    private FormFile? _file;

    /// <summary>
    /// The display projection — headers, sort, search, collapse memory, selection kept across a rebuild.
    /// One instance per grid, so a collapse survives reselection.
    /// </summary>
    private readonly FormPropertyDisplayList _display = new();

    /// <summary>Set while the grid itself points the selector at the store's selection (an echo).</summary>
    private bool _syncingObjects;

    /// <summary>Raised when a row edited the model, so the host can write the document.</summary>
    public event EventHandler? Edited;

    /// <summary>
    /// The user picked an object in the selector. The argument is the control, or null for the form.
    /// ⛔ The host answers through its selection store; the grid does not select.
    /// </summary>
    public event EventHandler<FormControl?>? SelectionRequested;

    [ObservableProperty]
    private FormControl? _selectedControl;

    /// <summary>Every row for the current selection, in catalog order. Empty when there is no document.</summary>
    public ObservableCollection<FormPropertyRow> Rows { get; } = new();

    /// <summary>What the list SHOWS: <see cref="FormPropertyCategoryHeader"/>s and <see cref="FormPropertyRow"/>s.</summary>
    public ObservableCollection<object> DisplayItems => _display.Items;

    public FormPropertyGridViewModel()
    {
        // A collapse that hid the described row: the pane must not describe a row nobody can see.
        _display.RowsHidden += (_, _) =>
        {
            if (SelectedRow != null && !DisplayItems.Contains(SelectedRow))
            {
                SelectedItem = null;
                SelectedRow = null;
            }
        };
    }

    /// <summary>The object selector's entries: the form, every control, every tray component.</summary>
    public ObservableCollection<FormObjectItem> Objects { get; } = new();

    [ObservableProperty]
    private FormObjectItem? _selectedObject;

    /// <summary>Filters the displayed rows by name, in both sort modes.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>Categorized (VS's default) or Alphabetical.</summary>
    [ObservableProperty]
    private bool _isCategorized = true;

    /// <summary>The Alphabetical toggle — the other face of <see cref="IsCategorized"/>.</summary>
    public bool IsAlphabetical
    {
        get => !IsCategorized;
        set => IsCategorized = !value;
    }

    /// <summary>The list's selected item — a header or a row.</summary>
    [ObservableProperty]
    private object? _selectedItem;

    /// <summary>
    /// The selected control's id — or the FORM's name when nothing is selected, because that is
    /// what the grid is then showing.
    /// </summary>
    public string Header => SelectedControl?.Id ?? _file?.Model.Name ?? "No selection";

    /// <summary>
    /// The control's KIND beside its id, the way VS's property window shows "button1  Button".
    /// Empty with no selection, so the header does not read "No selection No selection".
    /// </summary>
    public string HeaderKind => SelectedControl?.Kind
        ?? (_file != null ? _file.Model.RootElementName : string.Empty);

    /// <summary>True when there is nothing to show, so the view can say so rather than look broken.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// The row the description pane is describing.
    ///
    /// <para>⚠ Selection here is a VIEW concern only — it changes nothing in the document. It exists
    /// because VS's property window devotes its bottom third to explaining the highlighted property,
    /// and that pane is the single most useful thing the window does for someone who does not
    /// already know the control's API.</para>
    /// </summary>
    [ObservableProperty]
    private FormPropertyRow? _selectedRow;

    /// <summary>The description pane's title: the property's name, or a prompt when nothing is picked.</summary>
    public string DescriptionTitle => SelectedRow?.Name ?? (IsEmpty ? string.Empty : "Properties");

    /// <summary>
    /// The description pane's body (spec §3) — the property's Description (its type when it has none),
    /// and WHY it is read-only when it is.
    ///
    /// <para>⛔ A frozen row's reason belongs here rather than only under the value. D9 freezes a
    /// property when its value did not parse, and "why can I not edit this" is exactly the question
    /// this pane is for.</para>
    /// </summary>
    public string DescriptionBody
    {
        get
        {
            if (SelectedRow is not { } row)
            {
                return IsEmpty
                    ? "Select a control on the canvas to see its properties."
                    : "Select a property to see what it does.";
            }

            var text = string.IsNullOrEmpty(row.Description) ? row.TypeName : row.Description;
            return row.IsFrozen && !string.IsNullOrEmpty(row.FrozenReason)
                ? $"{text} — read-only. {row.FrozenReason}"
                : text;
        }
    }

    /// <summary>A header selected in the list describes nothing: the pane falls back to its prompt.</summary>
    partial void OnSelectedItemChanged(object? value) => SelectedRow = value as FormPropertyRow;

    partial void OnSelectedRowChanged(FormPropertyRow? value)
    {
        OnPropertyChanged(nameof(DescriptionTitle));
        OnPropertyChanged(nameof(DescriptionBody));
    }

    partial void OnIsCategorizedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAlphabetical));
        RefreshDisplay();
    }

    partial void OnSearchTextChanged(string value) => RefreshDisplay();

    /// <summary>
    /// ⛔ A pick REQUESTS the selection. An echo of the store's own change (<see cref="RefreshObjects"/>
    /// runs with <c>_syncingObjects</c> set) and a null push from the combo are ignored, and picking what
    /// is already selected requests nothing — re-running SelectInDesigner for it would close an open Type
    /// Here editor for a click that changed nothing.
    /// </summary>
    partial void OnSelectedObjectChanged(FormObjectItem? value)
    {
        if (_syncingObjects || value == null || ReferenceEquals(value.Control, SelectedControl))
        {
            return;
        }

        SelectionRequested?.Invoke(this, value.Control);
    }

    /// <summary>
    /// Points the grid at a loaded document. Passing the <see cref="FormFile"/> rather than the
    /// model is what carries D9's <b>Degraded</b> reasons — the model alone cannot say which values
    /// failed to parse.
    /// </summary>
    public void Load(FormFile? file)
    {
        _file = file;

        // ⛔ Rebuild EXACTLY ONCE. Assigning null over null raises no change, so OnSelectedControlChanged
        // does not fire — which was harmless while an empty selection meant an empty grid, and stopped
        // being harmless the moment a form with no selection had rows of its own to show. Assigning null
        // over a control DOES fire it, and a second explicit Rebuild would build every row twice.
        if (SelectedControl == null)
        {
            Rebuild();
        }
        else
        {
            SelectedControl = null;
        }
    }

    partial void OnSelectedControlChanged(FormControl? value) => Rebuild();

    /// <summary>
    /// The rows VS shows for EVERY control, which this grid had none of: its name, where it is, how
    /// big it is, and its tab order.
    ///
    /// <para>⛔ These are not catalog properties — they are fields on the control and its geometry,
    /// so the catalog cannot supply them and adding them there would invent attributes the document
    /// does not have. Without them the grid could style a control but not place one: there was no
    /// way to type an exact X, and dragging is the wrong tool for "line these three up at 96".</para>
    ///
    /// <para>⚠ Name is READ-ONLY, deliberately and visibly. The id names the generated field, so a
    /// rename has to rewrite the user's designer region and check the new name is unique and legal;
    /// until that exists, a frozen row says so where an editable one would silently produce a
    /// document that no longer compiles. The Degraded tier already renders exactly this shape.</para>
    /// </summary>
    // The description-pane texts of the intrinsic rows: WinForms' own [Description] where WinForms
    // has the property (the measured reference beside the spec), ours where it does not (Col/Row).
    private const string NameDescription = "Indicates the name used in code to identify the object.";
    private const string LocationDescription =
        "The coordinates of the upper-left corner of the control relative to the upper-left corner of its container.";
    private const string SizeDescription = "The size of the control in pixels.";
    private const string AnchorDescription =
        "Defines the edges of the container to which a certain control is bound. When a control is anchored to " +
        "an edge, the distance between the control's closest edge and the specified edge will remain constant.";
    private const string DockDescription = "Defines which borders of the control are bound to the container.";
    private const string CellDescription = "The page grid cell the control occupies (0-based).";
    private const string TabIndexDescription = "Determines the index in the TAB order that this control will occupy.";

    private void AddIntrinsicRows(FormControl control)
    {
        void Changed() => Edited?.Invoke(this, EventArgs.Empty);

        Rows.Add(new FormPropertyRow(
            "Name", FormPropertyType.String,
            () => control.Id,
            write: null,
            Changed,
            "The id names the generated field. Renaming is not supported here yet.",
            category: "Design",
            description: NameDescription));

        // ⚠ Geometry rows follow the shape the control actually HAS. A web control lives in a grid
        // cell and has no X/Y at all; offering them would let the user set a number the emitter
        // cannot use — the same designer/runtime divergence D9 exists to prevent.
        switch (control.Geometry)
        {
            case PixelGeometry pixel:
                Rows.Add(IntRow("X", () => pixel.X, v => pixel.X = v, Changed, "Layout", LocationDescription));
                Rows.Add(IntRow("Y", () => pixel.Y, v => pixel.Y = v, Changed, "Layout", LocationDescription));
                Rows.Add(IntRow("Width", () => pixel.Width, v => pixel.Width = Math.Max(1, v), Changed, "Layout", SizeDescription));
                Rows.Add(IntRow("Height", () => pixel.Height, v => pixel.Height = Math.Max(1, v), Changed, "Layout", SizeDescription));

                // ⛔⛔ PIXEL GEOMETRY ONLY, and that is D3 rather than an oversight. Anchor and Dock
                // are WinForms layout vocabulary; a .blwebform control lives in a grid CELL and has
                // no edges to anchor to. Offering them on the web would let the user set a value the
                // emitter cannot use — the designer/runtime divergence D9 exists to prevent — and
                // Task 21's retarget reports the loss when a form crosses formats.
                //
                // ⚠ Reached only through this arm, so the web grid cannot show them by accident:
                // there is no `if (target == Web) hide` to forget.
                Rows.Add(new FormPropertyRow(
                    "Anchor", FormPropertyType.String,
                    () => pixel.Anchor ?? "",
                    Changing(() => pixel.Anchor, v => pixel.Anchor = string.IsNullOrWhiteSpace(v) ? null : v),
                    Changed,
                    editor: FormRowEditor.AnchorPicker,
                    category: "Layout",
                    description: AnchorDescription));

                Rows.Add(new FormPropertyRow(
                    "Dock", FormPropertyType.String,
                    () => pixel.Dock ?? "",
                    Changing(() => pixel.Dock, v => pixel.Dock = string.IsNullOrWhiteSpace(v) ? null : v),
                    Changed,
                    editor: FormRowEditor.DockPicker,
                    category: "Layout",
                    description: DockDescription));
                break;

            case GridGeometry grid:
                Rows.Add(IntRow("Col", () => grid.Col, v => grid.Col = Math.Max(0, v), Changed, "Layout", CellDescription));
                Rows.Add(IntRow("Row", () => grid.Row, v => grid.Row = Math.Max(0, v), Changed, "Layout", CellDescription));
                break;
        }

        // Only a POSITIONED control has a tab order, and this is the same filter
        // FormDocument.RenumberTabIndexes and FormPlacement.NextTabIndex apply. A component (Tray), a
        // strip (Docked) and a menu item (Item) each have none: geometry rows were already absent for
        // all three — the switch above has no arm for a null Geometry — but TabIndex was gated only on
        // Tray, so a MenuStrip and a ToolStripMenuItem still offered a row that writes a number the
        // emitter cannot use and the writer will not emit.
        //
        // ⛔ `is null or FormPlace.Positioned`, never `== FormPlace.Positioned`: a control whose kind
        // is not in the catalog has a NULL Definition and IS positioned, and the equality answers
        // false for it — silently taking the TabIndex row away from exactly the control that most
        // needs one.
        if (control.Definition?.Place is null or FormPlace.Positioned)
        {
            Rows.Add(IntRow(
                "TabIndex", () => control.TabIndex, v => control.TabIndex = Math.Max(0, v), Changed,
                "Behavior", TabIndexDescription));
        }
    }

    /// <summary>
    /// An intrinsic Int row. The accessors read and write the live model, so the canvas redraws from
    /// the same object the grid edited rather than from a copy that has to be pushed back.
    /// </summary>
    private static FormPropertyRow IntRow(
        string name, Func<int> read, Action<int> write, Action changed, string category, string description) =>
        new(name,
            FormPropertyType.Int,
            // ⛔ Invariant, and parsed by the catalog's own reader: see FormPropertyRow.IntValue.
            () => read().ToString(CultureInfo.InvariantCulture),
            text =>
            {
                // ⚠ An unparseable value is IGNORED rather than coerced to zero. The numeric editor
                // should not produce one, but a binding can push mid-edit text — and snapping a
                // control to the origin because the user was halfway through typing "1" of "128" is
                // the kind of thing that makes a designer feel haunted.
                if (FormPropertyDef.TryParseInt(text, out var parsed))
                {
                    // ⛔ Reports whether the number MOVED: "007" on 7, or 0 on a Width clamped to 1,
                    // is not an edit (FormPropertyRow._write).
                    var before = read();
                    write(parsed);
                    return read() != before;
                }

                return false;
            },
            changed,
            category: category,
            description: description);

    /// <summary>
    /// Wraps an intrinsic setter so it reports whether it CHANGED the value (FormPropertyRow._write): a
    /// write that leaves the model as it was is not an edit and raises no Edited.
    /// </summary>
    private static Func<string, bool> Changing(Func<string?> read, Action<string> write) =>
        value =>
        {
            var before = read();
            write(value);
            return !string.Equals(before, read(), StringComparison.Ordinal);
        };

    /// <summary>
    /// The FORM's own properties — what VS shows when nothing on the surface is selected — from
    /// <see cref="FormControlCatalog.FormRoot"/> (spec §2.3), their values through
    /// <see cref="FormRootValues"/>, the ONE map to where each lives. So the form shows ClientSize (not a
    /// Width/Height pair) on WinForms, and the grid tracks on the web — with or without a
    /// <c>&lt;Layout&gt;</c> yet: the first write creates one.
    ///
    /// <para>⚠ Name is frozen for a stronger reason than a control's: it names the generated CLASS,
    /// and the document's own file name has to agree with it — a mismatch is refused at load.</para>
    /// </summary>
    private void AddFormRows(FormDocument form)
    {
        void Changed() => Edited?.Invoke(this, EventArgs.Empty);

        Rows.Add(new FormPropertyRow(
            "Name", FormPropertyType.String,
            () => form.Name,
            write: null,
            Changed,
            "The form's name is its class name, and the file name must agree with it.",
            category: "Design",
            description: NameDescription));

        foreach (var row in FormControlCatalog.FormRoot.Properties.Where(p => p.AppliesTo(form.Target)))
        {
            var definition = row;

            // ⚠ Ordinal, as FormFile.DegradedReasonOfRoot is: a root row's name is its attribute spelling.
            var degraded = _file?.DegradedRoot.FirstOrDefault(d =>
                string.Equals(d.Property, definition.Name, StringComparison.Ordinal));

            Rows.Add(FormPropertyRow.ForStoredValue(
                definition,
                form.Target,
                read: () => FormRootValues.Get(form, definition),
                write: value =>
                {
                    // ⛔ Set returns false for a value it REFUSES (a non-positive ClientSize passes Accepts
                    // and fails here); a Set that stores the same value ("400,300" over "400, 300") changed
                    // nothing either. Neither is an edit.
                    var before = FormRootValues.Get(form, definition);
                    return FormRootValues.Set(form, definition, value) &&
                           !string.Equals(before, FormRootValues.Get(form, definition), StringComparison.Ordinal);
                },
                remove: FormRootValues.CanReset(definition)
                    ? () => FormRootValues.Set(form, definition, null)
                    : null,
                Changed,
                frozenReason: degraded?.Reason,
                frozenText: degraded?.Value));
        }
    }

    private void Rebuild()
    {
        Rows.Clear();

        var control = SelectedControl;
        var definition = control?.Definition;

        if (control != null)
        {
            var target = _file?.Model.Target ?? FormTarget.Web;

            // ⚠ A control whose kind the catalog does not know (null Definition) still has a name, a place
            // and a tab order: it shows those — never the FORM's rows, as though nothing were selected.
            AddIntrinsicRows(control);

            foreach (var property in definition?.Properties ?? Enumerable.Empty<FormPropertyDef>())
            {

                // ⛔ A property the target does not have is not offered. WinForms RadioButton has
                // no GroupName and WinForms ListBox no MultiSelect — measured, by csc. Offering
                // them would let the user set a value that silently never reaches the generated
                // code, which is the designer/runtime divergence D9 exists to prevent.
                if (!property.AppliesTo(target))
                {
                    continue;
                }

                Rows.Add(new FormPropertyRow(
                    control,
                    property,
                    target,
                    _file?.DegradedReason(control.Id, property.Name),
                    () => Edited?.Invoke(this, EventArgs.Empty)));
            }
        }
        else if (_file?.Model is { } form)
        {
            // ⛔ The FORM's own properties, shown when no control is selected — which is what VS
            // does, and what this grid did not: clicking the form said "No selection" and offered
            // nothing, so a form's caption and size could only be changed by editing the XML.
            AddFormRows(form);
        }

        // ⚠ The selected item is cleared first: it points at a row of the PREVIOUS control, and leaving
        // it would leave the description pane describing a property that is no longer on screen.
        // (SelectedRow too, directly: today's view still binds it TwoWay, and a row selected there
        // never went through SelectedItem.)
        SelectedItem = null;
        SelectedRow = null;

        RefreshObjects();
        RefreshDisplay();

        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(HeaderKind));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(DescriptionTitle));
        OnPropertyChanged(nameof(DescriptionBody));
    }

    // ==================================================================
    // The object selector
    // ==================================================================

    /// <summary>
    /// Rebuilds the selector's entries only when the document's objects changed, then points it at the
    /// current selection. ⚠ Not cleared on every selection: clearing an ItemsSource while the combo is
    /// mid-change is how a combo pushes a stray null back into the binding.
    ///
    /// <para>⚠ Runs ONLY from <see cref="Rebuild"/> — a load, or a selection change. A drop and a paste
    /// select what they added (pinned for a drop: <c>PlacingAControl_ThroughTheDocumentViewModel_…</c>) and
    /// a delete leaves nothing selected, so each reaches here through that selection change. An edit that
    /// changes the objects WITHOUT changing the selection does not — today no such edit exists.</para>
    /// </summary>
    // ⚠ When rename lands (the Name row is frozen today), it must call RefreshObjects: a renamed control
    // keeps its selection, so nothing else would refresh the selector's "Name  Kind" entry.
    private void RefreshObjects()
    {
        var model = _file?.Model;
        var wanted = new List<FormObjectItem>();
        if (model != null)
        {
            wanted.Add(new FormObjectItem(model.Name, model.RootElementName, null));
            foreach (var control in model.AllControls().Concat(model.AllComponents()))
            {
                wanted.Add(new FormObjectItem(control.Id, control.Kind, control));
            }
        }

        _syncingObjects = true;
        try
        {
            var same = Objects.Count == wanted.Count &&
                       Objects.Zip(wanted).All(p => ReferenceEquals(p.First.Control, p.Second.Control) &&
                                                    p.First.Name == p.Second.Name &&
                                                    p.First.Kind == p.Second.Kind);
            if (!same)
            {
                Objects.Clear();
                foreach (var item in wanted)
                {
                    Objects.Add(item);
                }
            }

            // ⛔ The SAME item instance when nothing changed, so the store's own echo is a no-op here.
            SelectedObject = Objects.FirstOrDefault(o => ReferenceEquals(o.Control, SelectedControl));
        }
        finally
        {
            _syncingObjects = false;
        }
    }

    // ==================================================================
    // What the list shows
    // ==================================================================

    /// <summary>
    /// Rebuilds <see cref="DisplayItems"/> through <see cref="FormPropertyDisplayList"/>, keeping the
    /// selected row (or header) when it is still shown and clearing it DELIBERATELY when it is not — a
    /// search keystroke or a sort toggle must not reset the description pane, and a row the search hides
    /// must not stay described.
    /// </summary>
    private void RefreshDisplay()
    {
        // ⚠ Captured BEFORE the rebuild: clearing a bound list pushes a null selection back into us.
        // SelectedRow FIRST — today's view binds it directly (Task 13 moves the list to SelectedItem), so a
        // row picked there never reached SelectedItem, which may still hold an older header.
        var selected = (object?)SelectedRow ?? SelectedItem;
        var keep = _display.Refresh(Rows, SearchText, IsCategorized, selected);

        SelectedItem = keep;
        SelectedRow = keep as FormPropertyRow;
    }
}
