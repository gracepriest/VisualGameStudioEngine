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

    /// <summary>
    /// The PRIMARY of the selection the grid shows (the control selected last), or null for the Form.
    ///
    /// <para>⛔⛔ Slice 6 D-1: it stays the primary in a multi-selection, never null. The canvas's <c>SelectedControl</c> is
    /// TwoWay-bound to this (<c>CodeEditorDocumentView.axaml</c>) and reads it for its handles, the open drop-down, hit-testing
    /// and rename arming — a null meaning "several" would drop all four. The whole set is <see cref="SelectedControls"/>.
    /// Setting this to a control that is not the current primary (the canvas's echo of a single click, a test) means
    /// "exactly that one control"; the host uses <see cref="SetSelection"/>.</para>
    /// </summary>
    [ObservableProperty]
    private FormControl? _selectedControl;

    /// <summary>The selection the rows were built for, in selection order (primary LAST) — a copy, never the store's list.</summary>
    private IReadOnlyList<FormControl> _selectedControls = Array.Empty<FormControl>();

    /// <summary>Set while <see cref="SetSelection"/> moves <see cref="SelectedControl"/>: the rebuild is its, once.</summary>
    private bool _settingSelection;

    /// <summary>
    /// What the grid shows (slice 6 D-1): the selection in selection order, the primary LAST — as
    /// <c>FormSelection.Controls</c>. Empty for the Form.
    /// </summary>
    public IReadOnlyList<FormControl> SelectedControls => _selectedControls;

    /// <summary>Two or more controls: the rows are the MERGED rows (D-2/D-3).</summary>
    public bool IsMultiSelection => _selectedControls.Count > 1;

    /// <summary>
    /// ⛔ THE host's one entry point (slice 6 D-1): shows <paramref name="controls"/> — copied, so a later change to the
    /// store's own list is not silently seen — with <see cref="SelectedControl"/> = the last, and rebuilds EXACTLY ONCE.
    /// A no-op when the list is element-wise reference-equal to the one the rows were built for. ⚠ The grid never
    /// selects: the host calls this from its ONE selection store's <c>Changed</c> (and <c>SelectInDesigner</c>).
    /// </summary>
    public void SetSelection(IReadOnlyList<FormControl> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        if (controls.Count == _selectedControls.Count &&
            controls.Zip(_selectedControls).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        _selectedControls = controls.ToList();
        _settingSelection = true;
        try
        {
            SelectedControl = _selectedControls.Count == 0 ? null : _selectedControls[^1];
        }
        finally
        {
            _settingSelection = false;
        }

        Rebuild();
    }

    /// <summary>Every row for the current selection, in catalog order. Empty when there is no document.</summary>
    public ObservableCollection<FormPropertyRow> Rows { get; } = new();

    /// <summary>
    /// The form document's path on disk (slice 4 Task 10) — where the image picker finds the project
    /// (<c>FormAssetPaths.ProjectRootFor</c>) to store a picked file relative to it, or copy one into its Resources.
    /// Set by the document view model with every sync; null for a grid with no document (the picker then does nothing).
    /// </summary>
    public string? DocumentPath { get; set; }

    /// <summary>
    /// What the list SHOWS: <see cref="FormPropertyCategoryHeader"/>s with <see cref="FormPropertyRow"/>s — or, in
    /// <see cref="IsEventsMode"/>, with <see cref="FormEventRow"/>s. ⛔ Each mode has its OWN display list, so collapsing
    /// "Behavior" among the events does not collapse it among the properties.
    /// </summary>
    public ObservableCollection<object> DisplayItems => IsEventsMode ? _eventDisplay.Items : _display.Items;

    /// <summary>The Events tab's display projection (slice 5 D-5) — its own collapse memory.</summary>
    private readonly FormPropertyDisplayList _eventDisplay = new();

    /// <summary>The Events tab's rows for the current selection: <see cref="FormEvents.WiredOn"/>, never more.</summary>
    public ObservableCollection<FormEventRow> EventRows { get; } = new();

    /// <summary>
    /// Properties or Events (VS's lightning bolt). ⛔ Survives a selection change, as VS's does.
    /// </summary>
    [ObservableProperty]
    private bool _isEventsMode;

    /// <summary>The Properties toggle — the other face of <see cref="IsEventsMode"/>.</summary>
    public bool IsPropertiesMode
    {
        get => !IsEventsMode;
        set => IsEventsMode = !value;
    }

    /// <summary>
    /// The form's code-behind as the user sees it now — the open tab's text when it is open, else the file — PUSHED by
    /// the document view model (D-5 freshness). The Events tab's handler drop-downs read it; setting it refreshes them.
    /// ⛔ The grid never writes it.
    /// </summary>
    [ObservableProperty]
    private string _codeBehindText = "";

    /// <summary>
    /// An Events-tab row wants a handler: a typed new name, or a double-click (D-5). ⛔ The host owns the code-behind:
    /// it writes the stub through <see cref="FormHandlers.Plan"/>, binds it, and opens the code. The grid writes nothing.
    /// </summary>
    public event EventHandler<FormHandlerRequest>? HandlerRequested;

    /// <summary>
    /// A handler drop-down is opening (D-5 freshness): the host re-reads the code-behind — the open tab first, then the
    /// disk — and pushes it into <see cref="CodeBehindText"/>, so a Sub typed into an UNSAVED tab is offered.
    /// </summary>
    public event EventHandler? CodeBehindRefreshRequested;

    /// <summary>Asks the host for a fresh <see cref="CodeBehindText"/> (the view calls this on a drop-down's open).</summary>
    public void RequestCodeBehindRefresh() => CodeBehindRefreshRequested?.Invoke(this, EventArgs.Empty);

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
        _eventDisplay.RowsHidden += (_, _) =>
        {
            if (SelectedEventRow != null && !DisplayItems.Contains(SelectedEventRow))
            {
                SelectedItem = null;
            }
        };
    }

    /// <summary>The Events-tab row the description pane describes (the list's selected item when it is one).</summary>
    public FormEventRow? SelectedEventRow => SelectedItem as FormEventRow;

    /// <summary>The Events-tab row whose last typed value was refused, while that is the newest thing to say.</summary>
    private FormEventRow? _refusedEventRow;

    partial void OnIsEventsModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPropertiesMode));
        OnPropertyChanged(nameof(DisplayItems));
        SelectedItem = null;
        SelectedRow = null;
        RefreshDisplay();
        RaiseDescription();
    }

    /// <summary>
    /// How the grid scans the code-behind — <see cref="FormCodeScan.Scan"/>; a SEAM, so a test can count the scans of one
    /// refresh (round 4 ruling 11: never a static counter in a compiler type). Public: the Shell grants the tests no
    /// internals.
    /// </summary>
    public Func<string, string?, FormCodeScanResult> CodeScanner { get; set; } = FormCodeScan.Scan;

    /// <summary>The ONE scan of <see cref="CodeBehindText"/> every Events-tab row reads (review ruling 5), and what it was of.</summary>
    private (string Text, string? FormName, FormCodeScanResult Result)? _codeScan;

    /// <summary>The current scan of the code-behind — made once per (text, form) and shared by every row.</summary>
    private FormCodeScanResult CodeScan()
    {
        var formName = _file?.Model.Name;
        if (_codeScan is not { } cached || !ReferenceEquals(cached.Text, CodeBehindText) || cached.FormName != formName)
        {
            _codeScan = (CodeBehindText, formName, CodeScanner(CodeBehindText, formName));
        }

        return _codeScan.Value.Result;
    }

    /// <summary>
    /// ⛔ The host bound or unbound a handler BEHIND the rows (round 4 ruling 1, CRITICAL): every row re-reads its Handler,
    /// so no cell keeps a stale value with focus in it for the next LostFocus to commit.
    /// </summary>
    public void RefreshHandlers()
    {
        foreach (var row in EventRows)
        {
            row.HandlerChanged();
        }
    }

    /// <summary>
    /// The host refused a name typed into <paramref name="owner"/>'s <paramref name="evt"/> row (round 4 ruling 5): said in
    /// that row's description, and the cell reverts. Returns false when no such row is showing (the caller reports it).
    /// </summary>
    public bool RefuseHandler(FormBindOwner owner, FormEventDef evt, string why, string? refusedText = null)
    {
        var row = EventRows.FirstOrDefault(r => ReferenceEquals(r.Owner.Control, owner.Control) && ReferenceEquals(r.Event, evt));
        row?.Refuse(why, refusedText);
        return row != null;
    }

    partial void OnCodeBehindTextChanged(string value)
    {
        CodeScan();
        foreach (var row in EventRows)
        {
            row.RefreshChoices();
        }
    }

    private void OnEventRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FormEventRow.Refusal) || sender is not FormEventRow row || !EventRows.Contains(row))
        {
            return;
        }

        _refusedEventRow = row.Refusal != null ? row : ReferenceEquals(_refusedEventRow, row) ? null : _refusedEventRow;
        RaiseDescription();
    }

    /// <summary>
    /// An Events-tab row's cell must show its bound handler again (a refusal — the row's own or the host's): the view puts
    /// the text back (see <see cref="FormEventRow.Reverted"/> for why a property notification is not enough).
    /// </summary>
    public event EventHandler<FormHandlerCellRevert>? HandlerCellReverted;

    private void OnEventRowReverted(object? sender, string? refusedText)
    {
        if (sender is FormEventRow row)
        {
            HandlerCellReverted?.Invoke(this, new FormHandlerCellRevert(row, refusedText));
        }
    }

    /// <summary>Rebuilds the Events-tab rows for the selection: the control's row, or the Form's with nothing selected.</summary>
    private void RebuildEventRows()
    {
        foreach (var old in EventRows)
        {
            old.PropertyChanged -= OnEventRowPropertyChanged;
            old.Reverted -= OnEventRowReverted;
        }

        EventRows.Clear();
        _refusedEventRow = null;

        // ⚠ Slice 6 Task 2: a multi-selection shows no event rows until Task 5 brings the intersection (D-8); the pane says
        // so (DescriptionBody). Never the primary's rows alone — a bind there would wire one control of several.
        if (_file?.Model is not { } form || IsMultiSelection)
        {
            return;
        }

        var owner = new FormBindOwner(form, SelectedControl);
        if (owner.Definition is not { } definition)
        {
            return;
        }

        foreach (var evt in FormEvents.WiredOn(definition, form.Target))
        {
            var row = new FormEventRow(form, owner, evt, CodeScan, RaiseEdited,
                request => HandlerRequested?.Invoke(this, request));
            row.PropertyChanged += OnEventRowPropertyChanged;
            row.Reverted += OnEventRowReverted;
            EventRows.Add(row);
        }
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
    /// what the grid is then showing. Empty for a multi-selection (slice 6 D-6), as VS's header is.
    /// </summary>
    public string Header => IsMultiSelection ? string.Empty : SelectedControl?.Id ?? _file?.Model.Name ?? "No selection";

    /// <summary>
    /// The control's KIND beside its id, the way VS's property window shows "button1  Button".
    /// Empty with no selection, so the header does not read "No selection No selection" — and for a multi-selection.
    /// </summary>
    public string HeaderKind => IsMultiSelection ? string.Empty : SelectedControl?.Kind
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

    /// <summary>
    /// The row whose last typed value was REFUSED (spec §7), while that refusal is the newest thing the
    /// grid has to say: a successful edit anywhere or a different row selected retracts it, and a new
    /// selection turns it into <see cref="_carriedRefusal"/>. ⚠ Not SelectedRow: typing into a row's editor
    /// does not select the row.
    ///
    /// <para>⚠ By design a refusal can outlive its row's VISIBILITY: a search that filters the row out, or a
    /// collapse, leaves the pane still saying why the value was refused — the refusal is about the edit the
    /// user just made, not about what the list shows.</para>
    /// </summary>
    private FormPropertyRow? _refusedRow;

    /// <summary>
    /// A refusal still standing at the next REBUILD — a selection change, or a reload (an undo or redo
    /// re-syncs through <see cref="Load"/>) — carried over that one rebuild and titled with the owner whose
    /// rows it was typed into (<c>lbl.ForeColor</c>). The natural gesture — type into a row, then click
    /// another control on the canvas — commits (and refuses) on the press and rebuilds the rows in the same
    /// press, so without this the reason was retracted before anyone could read it. Retracted by the next
    /// row pick, edit, refusal or selection change: carried ONCE.
    /// </summary>
    private (string Title, string Body)? _carriedRefusal;

    /// <summary>Whose rows are shown — the owner a carried refusal is titled with.</summary>
    private string? _shownOwner;

    /// <summary>The description pane's title: the property's name, or a prompt when nothing is picked.</summary>
    public string DescriptionTitle => IsEventsMode
        ? _refusedEventRow?.Name ?? SelectedEventRow?.Name ?? (EventRows.Count == 0 ? string.Empty : "Events")
        : _refusedRow?.Name ?? _carriedRefusal?.Title ?? SelectedRow?.Name ?? (IsEmpty ? string.Empty : "Properties");

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
            // Slice 5 D-5: in the Events tab the pane describes the selected EVENT, and says why a typed handler was refused.
            if (IsEventsMode)
            {
                return _refusedEventRow?.Refusal
                       ?? SelectedEventRow?.Description
                       ?? (EventRows.Count > 0
                           ? "Select an event to see when it is raised. Double-click it to write its handler."
                           // Slice 6 D-6: a multi-selection whose Events intersection is empty.
                           : IsMultiSelection
                               ? "The selected controls share no events on " +
                                 $"{(_file?.Model.Target == FormTarget.Web ? "the web" : "WinForms")}."
                           : SelectedControl != null
                               ? $"'{SelectedControl.Id}' ({SelectedControl.Kind}) has no events on " +
                                 $"{(_file?.Model.Target == FormTarget.Web ? "the web" : "WinForms")}."
                               : "Select a control on the canvas to see its events.");
            }

            // ⛔ Spec §7: a refused value is not written, and the editor snaps back — so this pane is the
            // only place that says what was refused and why.
            if (_refusedRow?.Refusal is { } refusal)
            {
                return refusal;
            }

            if (_carriedRefusal is { } carried)
            {
                return carried.Body;
            }

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
    partial void OnSelectedItemChanged(object? value)
    {
        SelectedRow = value as FormPropertyRow;
        if (value is FormEventRow picked && !ReferenceEquals(picked, _refusedEventRow))
        {
            _refusedEventRow = null;
        }

        OnPropertyChanged(nameof(SelectedEventRow));
        RaiseDescription();
    }

    partial void OnSelectedRowChanged(FormPropertyRow? value)
    {
        // Choosing a different row asks the pane about THAT row; the refusal is retracted.
        if (value != null)
        {
            _carriedRefusal = null;
            if (!ReferenceEquals(value, _refusedRow))
            {
                _refusedRow = null;
            }
        }

        RaiseDescription();
    }

    /// <summary>Every row edit reaches the host through here — and retracts a standing refusal.</summary>
    private void RaiseEdited()
    {
        if (_refusedRow != null || _carriedRefusal != null)
        {
            _refusedRow = null;
            _carriedRefusal = null;
            RaiseDescription();
        }

        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseDescription()
    {
        OnPropertyChanged(nameof(DescriptionTitle));
        OnPropertyChanged(nameof(DescriptionBody));
    }

    /// <summary>A row refused a typed value (or retracted its refusal): the pane follows it.</summary>
    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FormPropertyRow.Refusal) || sender is not FormPropertyRow row)
        {
            return;
        }

        // ⛔ A row of a PREVIOUS selection is never described over the rows now shown. Rebuild unsubscribes
        // old rows before clearing them and delivery is synchronous, so this cannot fire today — it is a
        // belt-and-braces backstop in case a future path subscribes a row that is not in Rows.
        if (!Rows.Contains(row))
        {
            return;
        }

        if (row.Refusal != null)
        {
            // Set and raised even when the SAME row refuses again — its text may differ.
            _refusedRow = row;
            _carriedRefusal = null;
            RaiseDescription();
        }
        else if (ReferenceEquals(_refusedRow, row))
        {
            _refusedRow = null;
            RaiseDescription();
        }
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
        // ⚠ Slice 6 D-6: in a multi-selection a pick of ANY object — the primary included — requests that one control
        // (the store collapses the set, as VS does).
        if (_syncingObjects || value == null || (!IsMultiSelection && ReferenceEquals(value.Control, SelectedControl)))
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

    /// <summary>
    /// A direct set (the canvas's TwoWay echo of a single click, <see cref="Load"/>, a test) means exactly that one control
    /// — or nothing. <see cref="SetSelection"/>'s own move rebuilds once, after it.
    /// </summary>
    partial void OnSelectedControlChanged(FormControl? value)
    {
        if (_settingSelection)
        {
            return;
        }

        _selectedControls = value == null ? Array.Empty<FormControl>() : new[] { value };
        Rebuild();
    }

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

    /// <summary>
    /// Slice 6 D-2 rule 3: whether an intrinsic row is offered for a MULTI-selection. Name never (VS hides <c>(Name)</c>; a
    /// shared id is meaningless) and TabIndex never (<c>Control.TabIndex</c> is <c>[MergableProperty(false)]</c>, measured —
    /// <c>FormMultiSelectCatalogTests</c> pins this answer against the oracle). The geometry rows follow the members'
    /// geometry, which the intersection already requires.
    /// </summary>
    public static bool OffersIntrinsicForMultiSelection(string name) => name is not ("Name" or "TabIndex");

    /// <summary>
    /// The intrinsic rows of <paramref name="control"/>, every one built with <paramref name="changed"/> as its change
    /// callback — the grid's <c>RaiseEdited</c> for a single selection, a member tally's <c>Mark</c> for a multi-selection
    /// (slice 6 D-5). ⛔ ONE builder, two consumers: the multi path never carries a second copy of the geometry switch.
    /// </summary>
    private List<FormPropertyRow> IntrinsicRows(FormControl control, Action changed)
    {
        var rows = new List<FormPropertyRow>();
        void Changed() => changed();

        // ⛔ A structural integer the reader could not read (X="5&#9;", a U+2212 minus) is D9 Degraded (slice 3
        // backlog (1)): the row is FROZEN with the reader's reason and shows the document's own text — never the 0 the
        // model fell back to, which the editor would otherwise offer as if it were the value (slice 2 B1's rule).
        FormPropertyRow IntRow(string name, Func<int> read, Action<int> write, Action changed, string category, string description)
        {
            var degraded = _file?.Degraded.FirstOrDefault(d =>
                string.Equals(d.ControlId, control.Id, StringComparison.Ordinal) &&
                string.Equals(d.Property, name, StringComparison.Ordinal));

            return degraded != null
                ? new FormPropertyRow(name, FormPropertyType.Int, () => degraded.Value, write: null, changed,
                    degraded.Reason, category: category, description: description)
                : FormPropertyGridViewModel.IntRow(name, read, write, changed, category, description);
        }

        rows.Add(new FormPropertyRow(
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
                // ⛔ Spec §3 / §2.4: Location and Size are COMPOSITE rows OVER the canvas's own geometry — never a second
                // copy of it. The parent reads/writes "x, y" (VS's PointConverter/SizeConverter text); its parts are
                // the X/Y and Width/Height rows the grid always had.
                rows.Add(GeometryComposite("Location", LocationDescription,
                    IntRow("X", () => pixel.X, v => pixel.X = v, Changed, "Layout", LocationDescription),
                    IntRow("Y", () => pixel.Y, v => pixel.Y = v, Changed, "Layout", LocationDescription),
                    (x, y) => { pixel.X = x; pixel.Y = y; }, Changed));
                rows.Add(GeometryComposite("Size", SizeDescription,
                    IntRow("Width", () => pixel.Width, v => pixel.Width = Math.Max(1, v), Changed, "Layout", SizeDescription),
                    IntRow("Height", () => pixel.Height, v => pixel.Height = Math.Max(1, v), Changed, "Layout", SizeDescription),
                    (w, h) => { pixel.Width = Math.Max(1, w); pixel.Height = Math.Max(1, h); }, Changed));

                // ⛔⛔ PIXEL GEOMETRY ONLY, and that is D3 rather than an oversight: Anchor and Dock
                // are the PIXEL vocabulary — a .blform's, and a Canvas page's (spec 2026-09-27) — and
                // a Grid/Flow page's control lives in a CELL with no edges to anchor to. Offering them
                // there would let the user set a value the
                // emitter cannot use — the designer/runtime divergence D9 exists to prevent — and
                // Task 21's retarget reports the loss when a form crosses formats.
                //
                // ⚠ Reached only through this arm, so the web grid cannot show them by accident:
                // there is no `if (target == Web) hide` to forget.
                rows.Add(new FormPropertyRow(
                    "Anchor", FormPropertyType.String,
                    () => pixel.Anchor ?? "",
                    Changing(() => pixel.Anchor, v => pixel.Anchor = string.IsNullOrWhiteSpace(v) ? null : v),
                    Changed,
                    editor: FormRowEditor.AnchorPicker,
                    category: "Layout",
                    description: AnchorDescription));

                rows.Add(new FormPropertyRow(
                    "Dock", FormPropertyType.String,
                    () => pixel.Dock ?? "",
                    Changing(() => pixel.Dock, v => pixel.Dock = string.IsNullOrWhiteSpace(v) ? null : v),
                    Changed,
                    editor: FormRowEditor.DockPicker,
                    category: "Layout",
                    description: DockDescription));
                break;

            case GridGeometry grid:
                rows.Add(IntRow("Col", () => grid.Col, v => grid.Col = Math.Max(0, v), Changed, "Layout", CellDescription));
                rows.Add(IntRow("Row", () => grid.Row, v => grid.Row = Math.Max(0, v), Changed, "Layout", CellDescription));
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
            rows.Add(IntRow(
                "TabIndex", () => control.TabIndex, v => control.TabIndex = Math.Max(0, v), Changed,
                "Behavior", TabIndexDescription));
        }

        return rows;
    }

    /// <summary>
    /// The catalog rows of <paramref name="control"/> on the document's target, in catalog order, each built with
    /// <paramref name="changed"/> (see <see cref="IntrinsicRows"/>) and given its composite parts.
    /// </summary>
    private List<FormPropertyRow> CatalogRows(FormControl control, Action changed)
    {
        var rows = new List<FormPropertyRow>();
        var target = _file?.Model.Target ?? FormTarget.Web;

        foreach (var property in control.Definition?.Properties ?? Enumerable.Empty<FormPropertyDef>())
        {
            // ⛔ A property the target does not have is not offered. WinForms RadioButton has
            // no GroupName and WinForms ListBox no MultiSelect — measured, by csc. Offering
            // them would let the user set a value that silently never reaches the generated
            // code, which is the designer/runtime divergence D9 exists to prevent.
            if (!property.AppliesTo(target))
            {
                continue;
            }

            var row = new FormPropertyRow(
                control,
                property,
                target,
                _file?.DegradedReason(control.Id, property.Name),
                changed);
            // A part of an ambient row starts from what this control INHERITS (code review I1).
            var model = _file?.Model;
            var name = property.Name;
            FormCompositeRows.Attach(row, model == null ? null : () => FormAmbient.Inherited(model, control, name));
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// The rows a MULTI-selection offers (slice 6 D-2), merged (D-3), in the PRIMARY's order, intrinsic rows first.
    ///
    /// <para>A row is offered iff EVERY member has the same row: an intrinsic row by name (and
    /// <see cref="OffersIntrinsicForMultiSelection"/> — never Name or TabIndex; the geometry rows exist only where the
    /// member's geometry has them), a catalog row by <see cref="FormPropertyDef.SharesShapeWith"/>, mergeable on every
    /// member (<see cref="FormPropertyDef.Mergeable"/>, VS's <c>[MergableProperty]</c>) and applying on the target. ⛔ No
    /// hand list of hidden rows beside the catalog.</para>
    ///
    /// <para>⛔ Every member row is built with ONE tally's <see cref="FormEditTally.Mark"/> as its change callback, never
    /// <see cref="RaiseEdited"/> — the merged row raises Edited ONCE per gesture (D-5).</para>
    /// </summary>
    private List<FormPropertyRow> MergedRows(IReadOnlyList<FormControl> controls)
    {
        var tally = new FormEditTally();
        var perMember = controls
            .Select(c => (Intrinsic: IntrinsicRows(c, tally.Mark), Catalog: CatalogRows(c, tally.Mark)))
            .ToList();
        var primary = perMember[^1];
        var merged = new List<FormPropertyRow>();

        foreach (var row in primary.Intrinsic.Where(r => OffersIntrinsicForMultiSelection(r.Name)))
        {
            var members = perMember.Select(m => m.Intrinsic.FirstOrDefault(r => r.Name == row.Name)).ToList();
            if (members.All(m => m != null))
            {
                merged.Add(FormPropertyRow.Merged(members.Select(m => m!).ToList(), controls, tally, RaiseEdited));
            }
        }

        foreach (var row in primary.Catalog.Where(r => r.Definition!.Mergeable))
        {
            var members = perMember
                .Select(m => m.Catalog.FirstOrDefault(r => r.Definition!.Mergeable && r.Definition.SharesShapeWith(row.Definition!)))
                .ToList();
            if (members.All(m => m != null))
            {
                merged.Add(FormPropertyRow.Merged(members.Select(m => m!).ToList(), controls, tally, RaiseEdited));
            }
        }

        return merged;
    }

    /// <summary>
    /// A geometry composite (Location, Size): "a, b" over its two part rows. Typed text sets both numbers at once;
    /// a part sets its own. ⚠ Frozen when either part is (a Degraded coordinate): the parent must not rewrite text the
    /// reader could not read.
    /// </summary>
    private static FormPropertyRow GeometryComposite(
        string name, string description, FormPropertyRow first, FormPropertyRow second, Action<int, int> write,
        Action changed)
    {
        var frozen = first.FrozenReason ?? second.FrozenReason;
        var parent = new FormPropertyRow(
            name, FormPropertyType.Size,
            () => $"{first.DisplayValue}, {second.DisplayValue}",
            frozen != null
                ? null
                : text =>
                {
                    if (!FormPropertyDef.TryParseSize(text, out var a, out var b))
                    {
                        return false;
                    }

                    var before = $"{first.DisplayValue}, {second.DisplayValue}";
                    write(a, b);
                    return !string.Equals(before, $"{first.DisplayValue}, {second.DisplayValue}", StringComparison.Ordinal);
                },
            changed,
            frozen,
            category: "Layout",
            description: description);

        parent.AdoptChildren(new[] { first, second });
        return parent;
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
        void Changed() => RaiseEdited();

        Rows.Add(new FormPropertyRow(
            "Name", FormPropertyType.String,
            () => form.Name,
            write: null,
            Changed,
            "The form's name is its class name, and the file name must agree with it.",
            category: "Design",
            description: NameDescription));

        foreach (var row in FormControlCatalog.FormRoot.Properties.Where(p => FormRootValues.Applies(p, form)))
        {
            var definition = row;

            // ⚠ Ordinal, as FormFile.DegradedReasonOfRoot is: a root row's name is its attribute spelling.
            var degraded = _file?.DegradedRoot.FirstOrDefault(d =>
                string.Equals(d.Property, definition.Name, StringComparison.Ordinal));

            Rows.Add(Composite(FormPropertyRow.ForStoredValue(
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
                frozenText: degraded?.Value,
                storeRefusal: value => FormRootValues.RefusalOf(definition, value),
                // ⛔ A Func, asked on every read of the row's Choices — never a list captured now (slice 4 D-7). The SAME
                // function the region writer's BL8034 check is membership of.
                referenceCandidates: definition.Type == FormPropertyType.Reference
                    ? () => FormReferences.Candidates(form, definition)
                    : null)));
        }
    }

    /// <summary>
    /// The document changed under the rows (slice 4 D-7): each Reference row's drop-down re-reads its candidates, which
    /// its <see cref="FormPropertyRow.Choices"/> asks of the document on every read. ⛔ The document view model calls this
    /// on every model revision — the tray follows the same revision — so a Button that arrives while the Form stays
    /// selected is listed without reselecting. A rebuild (a selection change, a reload) builds fresh rows anyway.
    /// </summary>
    public void RefreshReferenceChoices()
    {
        foreach (var row in Rows)
        {
            row.RefreshChoices();
        }
    }

    /// <summary>
    /// ⛔ Slice 6 D-9: the document changed under the rows while the selection stands — an Arrange, a drag, an arrow nudge,
    /// a paste — and every row re-reads its value (parts and merged members included), every Events row its handler. So
    /// the shared Location of three selected controls follows an align-lefts without a reselect; and the same was true of
    /// a SINGLE selection, whose X row kept showing the pre-drag number (measured, pre-flight M2).
    ///
    /// <para>⚠ Called on EVERY model revision — including the one the grid's own edit makes, i.e. INSIDE a row's
    /// Commit → Edited → write chain. It therefore only notifies: no commit, no echo, and a row in its refusal's posted echo
    /// is skipped (<see cref="FormPropertyRow"/>.<c>RefreshValue</c>). Never a rebuild: that would drop the focus and the
    /// expanded parts mid-edit and re-run slice 5's stale-LostFocus hazard.</para>
    /// </summary>
    public void RefreshValues()
    {
        foreach (var row in Rows)
        {
            row.RefreshValue();
        }

        foreach (var row in EventRows)
        {
            row.HandlerChanged();
        }
    }

    /// <summary>Gives a catalog row its composite parts (a Font, a Size, a Padding), and returns it.</summary>
    private static FormPropertyRow Composite(FormPropertyRow row)
    {
        FormCompositeRows.Attach(row);
        return row;
    }

    /// <summary>Every row, parts included, in display order within each parent — for callers that look a row up by name.</summary>
    public IEnumerable<FormPropertyRow> AllRows() => Rows.SelectMany(r => r.Children.Prepend(r));

    /// <summary>
    /// The composites the user has expanded, by NAME — remembered across selections, as VS remembers an expanded Font
    /// when you click the next control.
    /// </summary>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    private void OnCompositeToggled(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FormPropertyRow.IsExpanded) || sender is not FormPropertyRow row || !Rows.Contains(row))
        {
            return;
        }

        if (row.IsExpanded)
        {
            _expanded.Add(row.Name);
        }
        else
        {
            _expanded.Remove(row.Name);
        }

        _display.RowToggled(row, SearchText);
    }

    private void Rebuild()
    {
        // A refusal still standing is carried over this ONE rebuild, titled with its owner (see
        // _carriedRefusal); a carried one from the rebuild before is not carried again.
        var carry = _refusedRow?.Refusal is { } refusal
            ? (Title: $"{_shownOwner ?? "?"}.{_refusedRow.Name}", Body: refusal)
            : ((string Title, string Body)?)null;

        // ⛔ Unsubscribed BEFORE the rows go: an editor detached or recycled by this rebuild can still push
        // into its old row, and that row's refusal must not reach the pane over another control's rows.
        foreach (var old in Rows)
        {
            old.PropertyChanged -= OnRowPropertyChanged;
            old.PropertyChanged -= OnCompositeToggled;
        }

        Rows.Clear();

        var control = SelectedControl;

        if (IsMultiSelection)
        {
            // Slice 6: the rows the whole selection shares, merged (D-2/D-3).
            foreach (var row in MergedRows(_selectedControls))
            {
                Rows.Add(row);
            }
        }
        else if (control != null)
        {
            // ⚠ A control whose kind the catalog does not know (null Definition) still has a name, a place
            // and a tab order: it shows those — never the FORM's rows, as though nothing were selected.
            foreach (var row in IntrinsicRows(control, RaiseEdited).Concat(CatalogRows(control, RaiseEdited)))
            {
                Rows.Add(row);
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
        // (SelectedRow too, directly: the view binds SelectedItem, but a caller or test may set
        // SelectedRow alone, and that never goes through SelectedItem.)
        SelectedItem = null;
        SelectedRow = null;

        // A refusal belongs to a row of the previous selection: its row is gone, so it survives only as the
        // carried, owner-titled text. Each new row reports its own.
        _refusedRow = null;
        _carriedRefusal = carry;
        // D-6: a carried refusal from a multi-selection is titled with every id ("btn, btn2.BackColor").
        _shownOwner = IsMultiSelection ? string.Join(", ", _selectedControls.Select(c => c.Id)) : Header;
        foreach (var row in Rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;

            if (row.IsComposite)
            {
                // Restored BEFORE subscribing: the display list below lays out the remembered expansion in one pass.
                row.IsExpanded = _expanded.Contains(row.Name);
                row.PropertyChanged += OnCompositeToggled;
            }
        }

        RebuildEventRows();
        RefreshObjects();
        RefreshDisplay();

        OnPropertyChanged(nameof(SelectedControls));
        OnPropertyChanged(nameof(IsMultiSelection));
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

            // ⛔ The SAME item instance when nothing changed, so the store's own echo is a no-op here. Blank for a
            // multi-selection (slice 6 D-6, VS) — the null push is ignored by OnSelectedObjectChanged.
            SelectedObject = IsMultiSelection
                ? null
                : Objects.FirstOrDefault(o => ReferenceEquals(o.Control, SelectedControl));
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
        // SelectedRow FIRST — the view binds SelectedItem (FormPropertyGridView), but a row set through
        // SelectedRow directly never reached SelectedItem, which may still hold an older header.
        if (IsEventsMode)
        {
            // Slice 5: the Events tab's own projection — the same search and sort, its own collapse memory.
            SelectedItem = _eventDisplay.Refresh(EventRows, SearchText, IsCategorized, SelectedItem);
            return;
        }

        var selected = (object?)SelectedRow ?? SelectedItem;
        var keep = _display.Refresh(Rows, SearchText, IsCategorized, selected);

        SelectedItem = keep;
        SelectedRow = keep as FormPropertyRow;
    }
}
