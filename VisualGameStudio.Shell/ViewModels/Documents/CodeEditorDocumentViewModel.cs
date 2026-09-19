using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Editor.Margins;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Documents;

public partial class CodeEditorDocumentViewModel : Document, IDocumentViewModel
{
    private readonly IFileService _fileService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IBookmarkService? _bookmarkService;
    private string _originalText = "";

    /// <summary>
    /// The TextDocument that holds the text content and undo history.
    /// Using this instead of Text preserves undo when switching tabs.
    /// </summary>
    [ObservableProperty]
    private TextDocument _textDocument = new();

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    [ObservableProperty]
    private int _totalLines = 1;

    [ObservableProperty]
    private bool _isSplitView;

    /// <summary>
    /// True when the Design view is showing instead of the text editor.
    ///
    /// <para>⛔ A MODE ON THE EXISTING DOCUMENT, not a new document type — the same idiom as
    /// <see cref="IsSplitView"/>, which this is cloned from. A second document type would mean two
    /// tabs for one file, two undo stacks, and two things that both think they own the text; D1
    /// puts the designer's output INSIDE the user's own file precisely so there is one of each.</para>
    /// </summary>
    [ObservableProperty]
    private bool _isDesignMode;

    [ObservableProperty]
    private SplitOrientation _splitOrientation = SplitOrientation.Horizontal;

    /// <summary>
    /// True when the debugger is paused. Used to prioritize debug data tips over LSP hover.
    /// </summary>
    public bool IsDebugPaused { get; set; }

    /// <summary>
    /// When true, trailing whitespace is trimmed from all lines before saving.
    /// Set by the MainWindowViewModel based on the user setting.
    /// </summary>
    public bool TrimTrailingWhitespaceOnSave { get; set; }

    public new string Id => FilePath ?? Guid.NewGuid().ToString();

    /// <summary>
    /// True when this file is a form document, so the Design toggle is worth offering.
    ///
    /// <para>⛔ Asks <c>FileExtensions</c> rather than testing the extension here. That list is
    /// already the single source of truth for which extensions are form documents, and a second
    /// copy would be a second thing to update — with the failure mode that a new extension shows
    /// no Design tab and nobody can say why.</para>
    /// </summary>
    public bool IsFormDocument =>
        FilePath != null && VisualGameStudio.Core.Constants.FileExtensions.IsFormDocument(FilePath);

    /// <summary>
    /// The parsed document behind the Design view, or null when this file is not one, cannot be
    /// parsed, or is refused.
    ///
    /// <para>⚠ Read from <see cref="Text"/> on demand rather than cached. The text is the truth and
    /// the user can edit it in Code view at any moment; a cached model would show a canvas that no
    /// longer matches the file, which is the designer/runtime divergence D9 exists to prevent — one
    /// step earlier.</para>
    ///
    /// <para>⚠ A REFUSED document yields null, so the canvas shows nothing rather than showing the
    /// empty model a refusal produces. A refusal's model is deliberately unpopulated; drawing it
    /// would tell the user their form has no controls.</para>
    /// </summary>
    public BasicLang.Forms.FormDocument? DesignDocument => DesignFile?.Model;

    private BasicLang.Forms.Serialization.FormFile? _designFile;
    private string? _designFileText;

    /// <summary>
    /// The loaded form file — the model plus the diagnostics and D9 tiers reading it produced.
    ///
    /// <para>⛔ Cached against the TEXT it was parsed from, and re-parsed only when that text
    /// changes. Not an optimisation: the canvas and the property grid hold references to
    /// <c>FormControl</c> objects out of this model, and re-parsing on every read would hand each
    /// caller a DIFFERENT object graph. The selected control would then never reference-equal
    /// anything in the document being drawn, so the selection outline would vanish and the property
    /// grid would edit a model nothing else can see.</para>
    ///
    /// <para>⚠ Still follows the text. An edit in Code view invalidates the cache, so the canvas
    /// never shows a form the file no longer describes.</para>
    /// </summary>
    public BasicLang.Forms.Serialization.FormFile? DesignFile
    {
        get
        {
            if (!IsFormDocument || string.IsNullOrEmpty(Text))
            {
                return null;
            }

            if (_designFile != null && string.Equals(_designFileText, Text, StringComparison.Ordinal))
            {
                return _designFile;
            }

            var form = BasicLang.Forms.Serialization.FormDocumentReader.Read(FilePath!, Text);
            _designFileText = Text;
            _designFile = form.IsRefused ? null : form;
            return _designFile;
        }
    }

    [RelayCommand]
    private void ToggleDesignMode()
    {
        IsDesignMode = !IsDesignMode;
        if (IsDesignMode)
        {
            SyncDesignerPanels();
        }
    }

    /// <summary>
    /// Opens this document IN the designer when it is a form document that parses. Returns whether
    /// the mode changed. Called once, by the file-open route — not on every reload, so a user who
    /// switched to Code view stays there.
    ///
    /// <para>⛔⛔ Without this, opening a <c>.blform</c> shows its raw XML with a "Design" button
    /// the user has to know to press. Every piece of the designer existed and worked when the
    /// report came back as "I can't see the form designer" — a form opens in its designer, the
    /// same as it does in every other tool that has one.</para>
    ///
    /// <para>⚠ A REFUSED or unparseable document stays in Code view deliberately. Its model is
    /// empty, so the canvas would be blank, and Code view is the only place the user can see what
    /// is wrong with the file.</para>
    /// </summary>
    public bool EnterDesignModeForFormDocument()
    {
        if (IsDesignMode || !IsFormDocument || DesignDocument == null)
        {
            return false;
        }

        IsDesignMode = true;
        SyncDesignerPanels();
        return true;
    }

    /// <summary>The property grid beside the canvas. Always present; empty until something is selected.</summary>
    public ViewModels.Designer.FormPropertyGridViewModel PropertyGrid { get; } = new();

    /// <summary>The toolbox beside the canvas, driven from the catalog for this document's target.</summary>
    public ViewModels.Designer.FormToolboxViewModel Toolbox { get; } = new();

    /// <summary>
    /// Bumped whenever the designer changed the model in place, so the canvas repaints.
    ///
    /// <para>⛔ The property grid edits <c>FormControl.Properties</c> directly — the canvas, the
    /// grid and the writer deliberately share ONE object graph, so the document reference never
    /// changes and Avalonia's <c>AffectsRender</c> has nothing to notice. Without this counter the
    /// user renames a button, the file updates, and the box on the canvas keeps the old caption.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private int _designModelRevision;

    private bool _designerPanelsWired;
    private bool _applyingDesignerEdit;

    /// <summary>
    /// Points the designer panels at the current document.
    ///
    /// <para>⚠ The <c>Edited</c> subscription is wired ONCE. The panels are owned by this view
    /// model and live as long as it does, so re-subscribing on every sync would add a handler per
    /// toggle into Design view — and each edit would then write the document two, three, four
    /// times over.</para>
    /// </summary>
    private void SyncDesignerPanels()
    {
        var file = DesignFile;

        if (!_designerPanelsWired)
        {
            PropertyGrid.Edited += OnDesignerEdited;
            _designerPanelsWired = true;
        }

        PropertyGrid.Load(file);
        if (file != null)
        {
            Toolbox.Target = file.Model.Target;
        }

        Tray.Rebuild(file?.Model);
    }

    /// <summary>
    /// A property-grid edit, written back through the structure-preserving writer.
    ///
    /// <para>⛔⛔ <c>_applyingDesignerEdit</c> exists because setting <see cref="Text"/> normally
    /// invalidates the parsed document — which is right for a Code-view edit and WRONG here. The
    /// cached model is not stale: it is precisely the model this new text was written FROM.
    /// Dropping it would re-parse into a fresh object graph, and the control the user has selected
    /// would no longer be in the document being drawn — the selection outline would vanish and the
    /// property grid would go empty on every keystroke they committed.</para>
    ///
    /// <para>⚠ The writer returns the original text unchanged when the model asked for nothing, so
    /// a no-op edit sets Text to what it already was and marks nothing dirty.</para>
    /// </summary>
    private void OnDesignerEdited(object? sender, EventArgs e) => WriteDesignerEditBack();

    /// <summary>
    /// Places a control of <paramref name="kind"/> at a point in FORM space — the model half of a
    /// toolbox drop, and the only way a drop reaches the file.
    ///
    /// <para>⛔⛔ This method exists so that something in a shipping build CALLS the placer. Three
    /// pieces of this feature have already shipped complete, unit-tested and unreachable; a placer
    /// with no caller would be the fourth, and the symptom is the one the user actually reports —
    /// "I drag a Button onto the form and nothing happens".</para>
    ///
    /// <para>⚠ The point is in FORM space, not canvas pixels. The canvas converts, using the same
    /// <c>FormCanvasTransform</c> it rendered and hit-tested with, so the control lands under the
    /// pointer at any zoom.</para>
    /// </summary>
    /// <returns>Null when the control was placed; otherwise why it was not.</returns>
    /// <summary>
    /// What the canvas's <c>DropCommand</c> is bound to. A refusal is reported the way every other
    /// designer finding is — through the Error List — rather than being swallowed.
    /// </summary>
    /// <summary>
    /// Removes a control from the document — what Delete does on the canvas.
    ///
    /// <para>⛔ Removed from the list it actually LIVES in, which is its container's when it is
    /// nested. Removing from <c>Document.Controls</c> unconditionally would silently do nothing for
    /// any control inside a Panel, and Delete would look broken only for nested controls.</para>
    ///
    /// <para>⚠ The selection is cleared BEFORE the write. The property grid holds the control being
    /// deleted, and rebuilding its rows against an object no longer in the document is how a
    /// designer starts editing a ghost.</para>
    /// </summary>
    [RelayCommand]
    private void DeleteControl(BasicLang.Forms.FormControl? control)
    {
        var file = DesignFile;
        if (control == null || file == null)
        {
            return;
        }

        var siblings = file.Model.ListContaining(control);
        if (siblings == null || !siblings.Remove(control))
        {
            return;
        }

        PropertyGrid.SelectedControl = null;
        WriteDesignerEditBack();
    }

    /// <summary>
    /// What is selected on the canvas (Task 20). Owned here rather than by the canvas, because the
    /// align, size, z-order and clipboard commands below all operate on it.
    /// </summary>
    public ViewModels.Designer.FormSelection Selection { get; } = new();

    /// <summary>
    /// The designer's copy buffer.
    ///
    /// <para>⚠ Static, so copy in one open form and paste into another works — which is most of the
    /// point. ⚠ NOT the OS clipboard: Avalonia's clipboard is async and reached through a
    /// <c>TopLevel</c>, which a document view model does not have. The cost is that Ctrl+C here does
    /// not put anything on the system clipboard, and pasting from another application does nothing.
    /// Within one IDE session, which is where a form subtree is meaningful at all, it behaves.</para>
    /// </summary>
    private static string? _designerClipboard;

    /// <summary>
    /// Align and make-same-size over the multi-selection, against its primary.
    ///
    /// <para>⚠ Writes the document only when something actually moved, so pressing "align left" on
    /// an already-aligned selection does not mark the form dirty or add an undo step.</para>
    /// </summary>
    [RelayCommand]
    private void Arrange(ViewModels.Designer.FormArrangeKind kind)
    {
        var file = DesignFile;
        if (file == null)
        {
            return;
        }

        if (ViewModels.Designer.FormArrange.Apply(
                file.Model, kind, Selection.Controls, Selection.Primary))
        {
            WriteDesignerEditBack();
        }
    }

    [RelayCommand]
    private void BringToFront() => Reorder(toFront: true);

    [RelayCommand]
    private void SendToBack() => Reorder(toFront: false);

    /// <summary>
    /// ⚠ Every selected control, in an order that keeps the selection's own relative layering. Sent
    /// to the back one at a time in FORWARD order, each lands at index 0 and pushes the previous one
    /// back — which reverses them. Walking backwards for that case keeps the group's internal order.
    /// </summary>
    private void Reorder(bool toFront)
    {
        var file = DesignFile;
        if (file == null || Selection.IsEmpty)
        {
            return;
        }

        var order = toFront
            ? Selection.Controls.ToList()
            : Selection.Controls.Reverse().ToList();

        var changed = false;
        foreach (var control in order)
        {
            changed |= toFront ? file.Model.BringToFront(control) : file.Model.SendToBack(control);
        }

        if (changed)
        {
            WriteDesignerEditBack();
        }
    }

    [RelayCommand]
    private void CopyControls()
    {
        var file = DesignFile;
        if (file == null || Selection.IsEmpty)
        {
            return;
        }

        _designerClipboard = BasicLang.Forms.FormClipboard.SerializeSubtree(
            file.Model.Target, Selection.Controls);
    }

    [RelayCommand]
    private void CutControls()
    {
        var file = DesignFile;
        if (file == null || Selection.IsEmpty)
        {
            return;
        }

        CopyControls();

        // ⚠ A snapshot: removing mutates the document, and Selection.Clear below would otherwise be
        // iterating the collection it is emptying.
        foreach (var control in Selection.Controls.ToList())
        {
            file.Model.ListContaining(control)?.Remove(control);
        }

        Selection.Clear();
        PropertyGrid.SelectedControl = null;
        WriteDesignerEditBack();
    }

    /// <summary>
    /// Pastes the copy buffer, renaming anything whose id is taken.
    ///
    /// <para>⛔ <c>DeserializeSubtree</c> does the renaming AND retargets the binds that named the
    /// old id — which is why it was built alongside the model rather than when Ctrl+V was wired.
    /// Pasting a button called <c>btnLogin</c> beside an existing one must not produce two controls
    /// answering to one handler.</para>
    ///
    /// <para>⚠ Offset by a grid step so a paste is VISIBLE. Pasting exactly on top of the original
    /// looks like nothing happened, and the user pastes again.</para>
    /// </summary>
    [RelayCommand]
    private void PasteControls()
    {
        var file = DesignFile;
        if (file == null || string.IsNullOrEmpty(_designerClipboard))
        {
            return;
        }

        // Both lists: a component and a control become fields of one class, so a pasted Timer
        // must not be allowed the name of an existing Button either.
        var taken = new HashSet<string>(
            file.Model.AllControls().Concat(file.Model.AllComponents()).Select(c => c.Id),
            StringComparer.OrdinalIgnoreCase);

        var pasted = BasicLang.Forms.FormClipboard.DeserializeSubtree(
            _designerClipboard, file.Model.Target, id => taken.Contains(id));

        if (pasted.Count == 0)
        {
            return;
        }

        foreach (var control in pasted)
        {
            if (control.Geometry is BasicLang.Forms.PixelGeometry pixel)
            {
                pixel.X += 8;
                pixel.Y += 8;
            }

            // ⛔ By the ROW, never by where the copy came from: a component pasted among the
            // controls would be drawn nowhere, emitted with Controls.Add, and refused on reload.
            (control.Definition?.IsComponent == true ? file.Model.Components : file.Model.Controls).Add(control);
        }

        file.Model.RenumberTabIndexes();
        Selection.SetRange(pasted);
        PropertyGrid.SelectedControl = Selection.Primary;
        WriteDesignerEditBack();
    }

    /// <summary>
    /// The double-click gesture (Task 22): put the caret in this control's handler, creating it if
    /// it does not exist yet.
    ///
    /// <para>⛔⛔ <b>This writes a file the user owns and that this document is not even open on</b> —
    /// the <c>.bas</c> beside the form. Everything else the designer does edits the document in this
    /// buffer; this reaches sideways, so it re-reads the code-behind from disk each time rather than
    /// caching it. A cached copy would be stale the moment the user typed in Code view, and the stub
    /// would be inserted into a file that no longer looked like that.</para>
    ///
    /// <para>⚠ The bind is ensured even when the handler already EXISTS. A user can write
    /// <c>btnLogin_Click</c> by hand before ever double-clicking; without this the gesture would
    /// navigate to it and still leave it unwired, which looks exactly like the designer working.</para>
    ///
    /// <para>⚠ The document is written back only when something actually changed, so double-clicking
    /// a control that is already wired does not mark the form dirty.</para>
    /// </summary>
    [RelayCommand]
    private async Task ActivateControlAsync(BasicLang.Forms.FormControl? control)
    {
        var file = DesignFile;
        if (control == null || file == null || FilePath == null)
        {
            return;
        }

        var codePath = BasicLang.Forms.FormCodeBehind.PathFor(FilePath);

        try
        {
            if (!await _fileService.FileExistsAsync(codePath))
            {
                ReportDesignerRefusal(
                    codePath,
                    BasicLang.Forms.DesignCodes.RegionAbsent,
                    $"'{Path.GetFileName(FilePath)}' has no code-behind: expected " +
                    $"'{Path.GetFileName(codePath)}' beside it, so there is nowhere to put the " +
                    "handler.");
                return;
            }

            var before = await _fileService.ReadFileAsync(codePath, CancellationToken.None);
            var plan = BasicLang.Forms.FormHandlers.PlanDefault(file.Model, control, before);

            if (plan.Outcome == BasicLang.Forms.HandlerOutcome.Refused)
            {
                ReportDesignerRefusal(
                    codePath, BasicLang.Forms.DesignCodes.RegionAbsent,
                    plan.Refusal ?? "the handler could not be created.");
                return;
            }

            if (plan.Outcome == BasicLang.Forms.HandlerOutcome.Created)
            {
                await _fileService.WriteFileAsync(codePath, plan.CodeText, CancellationToken.None);
                _eventAggregator.Publish(new FileSavedEvent(codePath));
            }

            if (BasicLang.Forms.FormHandlers.EnsureBind(control, plan.EventName, plan.Handler))
            {
                WriteDesignerEditBack();
            }

            PropertyGrid.SelectedControl = control;
            _eventAggregator.Publish(new NavigateToFileEvent(codePath, plan.CaretLine));
        }
        catch (Exception ex)
        {
            ReportDesignerRefusal(
                codePath, BasicLang.Forms.DesignCodes.RegionAbsent,
                $"the designer could not open a handler in '{Path.GetFileName(codePath)}': {ex.Message}");
        }
    }

    /// <summary>
    /// Publishes one designer finding against the code-behind's key.
    ///
    /// <para>⚠ Always the CODE-BEHIND's path, never the document's. The aggregator keys findings by
    /// (collection, file), and a finding filed against the .blform would never be cleared by the
    /// save path — which republishes on the .bas — leaving a phantom in the Error List for the rest
    /// of the session.</para>
    /// </summary>
    private void ReportDesignerRefusal(string codePath, string code, string message) =>
        _eventAggregator.Publish(new DesignerDiagnosticsEvent(codePath, new List<DiagnosticItem>
        {
            new()
            {
                Id = code,
                Message = message,
                Severity = DiagnosticSeverity.Warning,
                FilePath = codePath,
                Source = DesignerDiagnosticSource
            }
        }));

    [RelayCommand]
    private void PlaceDroppedControl(Controls.FormControlDropRequest? request)
    {
        if (request == null)
        {
            return;
        }

        var refusal = PlaceControl(request.Kind, request.X, request.Y);
        if (refusal != null)
        {
            ReportPlacementRefusal(refusal);
        }
    }

    /// <summary>
    /// A drop on the component TRAY (Task 25). Its own command, deliberately: a
    /// <c>FormControlDropRequest</c> carries no origin, so the canvas's command cannot tell a tray
    /// drop from a canvas drop and would place a Button at (0,0). A component kind is placed — the
    /// point is irrelevant to it — and a control kind is refused the way a bad canvas drop is.
    /// </summary>
    [RelayCommand]
    private void TrayDrop(string? kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return;
        }

        if (BasicLang.Forms.FormControlCatalog.Find(kind) is not { IsComponent: true })
        {
            ReportPlacementRefusal($"'{kind}' has a position; drop it on the form, not the tray.");
            return;
        }

        var refusal = PlaceControl(kind, 0, 0);
        if (refusal != null)
        {
            ReportPlacementRefusal(refusal);
        }
    }

    /// <summary>
    /// ⛔ A drop that does nothing and says nothing is the failure this feature was added to fix.
    /// The finding goes on the CODE-BEHIND's key, like every other designer diagnostic, so the next
    /// good save clears it — see RegenerateDesignerRegionsAsync, which republishes the same
    /// (collection, file) pair and would otherwise leave this stranded in the Error List.
    /// </summary>
    private void ReportPlacementRefusal(string refusal)
    {
        var codePath = BasicLang.Forms.FormCodeBehind.PathFor(FilePath ?? "");
        _eventAggregator.Publish(new DesignerDiagnosticsEvent(codePath, new List<DiagnosticItem>
        {
            new()
            {
                Id = BasicLang.Forms.DesignCodes.PlacementRefused,
                Message = refusal,
                Severity = DiagnosticSeverity.Warning,
                FilePath = codePath,
                Source = DesignerDiagnosticSource
            }
        }));
    }

    /// <summary>
    /// Writes a finished move or resize back to the document. Bound to the canvas's
    /// <c>CommitGeometryCommand</c> and executed once, when the drag ends.
    ///
    /// <para>⛔ The canvas has already mutated the model — it shares this view model's object graph
    /// — so there is nothing to apply here, only to persist. Re-applying the drag from a delta
    /// would be a second implementation of the geometry maths, and the two would drift.</para>
    ///
    /// <para>⚠ A drag that ended where it started still lands here on some paths. That is harmless:
    /// the structure-preserving writer returns the original text unchanged when the model asked for
    /// nothing, so a no-op commit writes nothing and marks nothing dirty.</para>
    /// </summary>
    [RelayCommand]
    private void CommitGeometry() => WriteDesignerEditBack();

    public string? PlaceControl(string kind, int x, int y)
    {
        var file = DesignFile;
        if (file == null)
        {
            return "This document is not a form the designer can edit.";
        }

        var result = ViewModels.Designer.FormPlacement.Place(file.Model, kind, x, y);
        if (result.Control == null)
        {
            return result.Refusal ?? $"'{kind}' could not be placed.";
        }

        WriteDesignerEditBack();

        // A designer drops a control and puts its properties in front of you. Selecting it also
        // draws the selection outline, which is how the user sees WHERE it landed.
        PropertyGrid.SelectedControl = result.Control;
        return null;
    }

    private void WriteDesignerEditBack()
    {
        var file = DesignFile;
        if (file == null)
        {
            return;
        }

        var written = BasicLang.Forms.Serialization.FormDocumentWriter.Write(file);

        // The model changed behind an unchanged reference — tell the canvas to repaint.
        DesignModelRevision++;

        _applyingDesignerEdit = true;
        try
        {
            // ⛔ ORDER MATTERS, and getting it wrong is silent. Text first: its setter is what
            // raises the change notification the canvas and the dirty indicator listen for, and it
            // only raises when the backing field actually changes. ReplaceContent writes that same
            // field directly, so doing it first leaves the setter with nothing to notice — one
            // edit, zero notifications, a canvas that never repaints.
            Text = written;

            // ⛔⛔ Then the EDITOR'S document. The two are one document kept in two places, and the
            // editor owns the undo stack. Writing only Text left the editor holding the pre-edit
            // copy — and the view syncs that copy back on every keystroke, so the first character
            // typed in Code view silently discarded every drop, move and resize the user had made.
            // ReplaceContent is the same call the refactoring tools use: it writes the editor's
            // document as ONE undoable operation, which is also the whole of the designer's undo.
            ReplaceContent(written);
            _designFileText = written;
        }
        finally
        {
            _applyingDesignerEdit = false;
        }
    }

    /// <summary>
    /// Undoes the last designer edit — a drop, a move, a resize, a property change.
    ///
    /// <para>⛔ The editor's undo stack, deliberately, not a second one of the designer's own. The
    /// document text is the truth; a model-level stack would be a second truth that can disagree
    /// with it, and the disagreement would surface as a form that redraws one way and saves
    /// another. It also means Ctrl+Z means the same thing in both views.</para>
    /// </summary>
    [RelayCommand]
    private void UndoDesignerEdit()
    {
        if (!TextDocument.UndoStack.CanUndo)
        {
            return;
        }

        TextDocument.UndoStack.Undo();
        AdoptDocumentText();
    }

    /// <summary>Redoes what <see cref="UndoDesignerEdit"/> took away.</summary>
    [RelayCommand]
    private void RedoDesignerEdit()
    {
        if (!TextDocument.UndoStack.CanRedo)
        {
            return;
        }

        TextDocument.UndoStack.Redo();
        AdoptDocumentText();
    }

    /// <summary>
    /// Takes the editor document's text as the truth after an undo or redo, and rebuilds the
    /// designer's view of it.
    ///
    /// <para>⛔ The re-parse is the point. Undo rewinds TEXT; the canvas and the property grid hold
    /// references into the model that text was parsed from, and that model still has the control
    /// the undo just removed. Without dropping it, the control stays on the canvas and comes back
    /// on the next save — the file and the picture disagreeing, which is the failure a designer
    /// cannot have.</para>
    /// </summary>
    private void AdoptDocumentText()
    {
        Text = TextDocument.Text;

        _designFile = null;
        _designFileText = null;
        OnPropertyChanged(nameof(DesignFile));
        OnPropertyChanged(nameof(DesignDocument));
        SyncDesignerPanels();
        DesignModelRevision++;
    }
    public new string Title => GetTitle();
    public new bool CanClose => true;

    public event EventHandler? DirtyChanged;
    public event EventHandler? TitleChanged;
    public event EventHandler? CaretPositionChanged;
    public event EventHandler<string>? TextChanged;
    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler<string>? AddToWatchRequested;
    public event EventHandler<DataTipEvaluationRequestEventArgs>? DataTipEvaluationRequested;
    public event EventHandler? FindRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? GoToDefinitionRequested;
    public event EventHandler? FindAllReferencesRequested;
    public event EventHandler? ShowCallHierarchyRequested;
    public event EventHandler? ToggleCommentRequested;
    public event EventHandler<bool>? ToggleWhitespaceRequested;
    public event EventHandler<bool>? ToggleColumnSelectionRequested;
    public event EventHandler? DuplicateLineRequested;
    public event EventHandler? MoveLineUpRequested;
    public event EventHandler? MoveLineDownRequested;
    public event EventHandler? DeleteLineRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;
    public event EventHandler? CutRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? RenameSymbolRequested;
    public event EventHandler? ExtractMethodRequested;
    public event EventHandler? InlineMethodRequested;
    public event EventHandler? IntroduceVariableRequested;
    public event EventHandler? ExtractConstantRequested;
    public event EventHandler? InlineConstantRequested;
    public event EventHandler? InlineVariableRequested;
    public event EventHandler? ChangeSignatureRequested;
    public event EventHandler? EncapsulateFieldRequested;
    public event EventHandler? InlineFieldRequested;
    public event EventHandler? MoveTypeToFileRequested;
    public event EventHandler? ExtractInterfaceRequested;
    public event EventHandler? GenerateConstructorRequested;
    public event EventHandler? ImplementInterfaceRequested;
    public event EventHandler? OverrideMethodRequested;
    public event EventHandler? AddParameterRequested;
    public event EventHandler? RemoveParameterRequested;
    public event EventHandler? ReorderParametersRequested;
    public event EventHandler? RenameParameterRequested;
    public event EventHandler? ChangeParameterTypeRequested;
    public event EventHandler? MakeParameterOptionalRequested;
    public event EventHandler? MakeParameterRequiredRequested;
    public event EventHandler? ConvertToNamedArgumentsRequested;
    public event EventHandler? ConvertToPositionalArgumentsRequested;
    public event EventHandler? SafeDeleteRequested;
    public event EventHandler? PullMembersUpRequested;
    public event EventHandler? PushMembersDownRequested;
    public event EventHandler? UseBaseTypeRequested;
    public event EventHandler? ConvertToInterfaceRequested;
    public event EventHandler? InvertIfRequested;
    public event EventHandler? ConvertToSelectCaseRequested;
    public event EventHandler? SplitDeclarationRequested;
    public event EventHandler? IntroduceFieldRequested;
    public event EventHandler? SurroundWithRequested;
    public event EventHandler? PeekDefinitionRequested;
    public event EventHandler<int>? BreakpointToggled;
    public event EventHandler<int>? ConditionalBreakpointRequested;
    public event EventHandler<int>? LogpointRequested;
    public event EventHandler<int>? EditBreakpointRequested;
    public event EventHandler<int>? RemoveBreakpointRequested;
    public event EventHandler<int>? ToggleEnableBreakpointRequested;
    public event EventHandler? FormatDocumentRequested;
    public event EventHandler<OnTypeFormattingRequestEventArgs>? OnTypeFormattingRequested;
    public event EventHandler? CodeActionsRequested;
    public event EventHandler<HoverRequestEventArgs>? HoverRequested;
    public event EventHandler<HoverResultEventArgs>? HoverResultReceived;
    public event EventHandler<CodeActionResultEventArgs>? CodeActionsReceived;
    public event EventHandler<LocationResultEventArgs>? DefinitionResultReceived;
    public event EventHandler<ReferencesResultEventArgs>? ReferencesResultReceived;
    public event EventHandler<SignatureHelpRequestEventArgs>? SignatureHelpRequested;
    public event EventHandler<SignatureHelpResultEventArgs>? SignatureHelpResultReceived;
    public event EventHandler<DocumentHighlightRequestEventArgs>? DocumentHighlightRequested;
    public event EventHandler<DocumentHighlightResultEventArgs>? DocumentHighlightResultReceived;
    public event EventHandler<RenameResultEventArgs>? RenameResultReceived;
    public event EventHandler<DocumentSymbolsResultEventArgs>? DocumentSymbolsReceived;
    public event EventHandler? ExpandSelectionRequested;
    public event EventHandler? ShrinkSelectionRequested;
    public event EventHandler<SelectionRangeResultEventArgs>? SelectionRangeReceived;

    /// <summary>
    /// Callback to get selection info from the view
    /// </summary>
    public Func<SelectionInfoDto?>? GetSelectionInfo { get; set; }

    public CodeEditorDocumentViewModel(IFileService fileService, IEventAggregator eventAggregator, IBookmarkService? bookmarkService = null)
    {
        _fileService = fileService;
        _eventAggregator = eventAggregator;
        _bookmarkService = bookmarkService;

        // ⚠ In the constructor, not a property initializer: an initializer cannot reference the
        // instance member Selection (CS0236), and the tray marks its items from that selection.
        Tray = new ViewModels.Designer.FormTrayViewModel(Selection);
    }

    /// <summary>The component tray under the canvas (Task 25): a view of <c>DesignDocument.Components</c>.</summary>
    public ViewModels.Designer.FormTrayViewModel Tray { get; }

    /// <summary>
    /// The tray follows the DOCUMENT: every designer edit bumps the revision, and an undo re-parses
    /// and bumps it too, so the strip can never show a component the file no longer has.
    /// </summary>
    partial void OnDesignModelRevisionChanged(int value) => Tray.Rebuild(DesignDocument);

    public IBookmarkService? BookmarkService => _bookmarkService;

    /// <summary>
    /// Language service for LSP-based features (folding ranges, etc.)
    /// </summary>
    public ILanguageService? LanguageService { get; set; }

    /// <summary>
    /// Git service for gutter change indicators
    /// </summary>
    public IGitService? GitService { get; set; }

    private string GetTitle()
    {
        var fileName = FilePath != null ? Path.GetFileName(FilePath) : "Untitled";
        return IsDirty ? $"{fileName} *" : fileName;
    }

    partial void OnTextChanged(string value)
    {
        // The Design view reads DesignDocument from Text on demand, so it only redraws when told
        // the property changed. Without this, an edit in Code view leaves a stale canvas.
        // ⛔ Not while the designer is writing its OWN edit back — see OnDesignerEdited. The
        // cached model is what produced this text, so discarding it would drop the selection.
        if (IsFormDocument && !_applyingDesignerEdit)
        {
            _designFile = null;
            _designFileText = null;
            OnPropertyChanged(nameof(DesignFile));
            OnPropertyChanged(nameof(DesignDocument));
            SyncDesignerPanels();
        }

        var wasDirty = IsDirty;
        IsDirty = value != _originalText;

        // Update total lines count
        TotalLines = string.IsNullOrEmpty(value) ? 1 : value.Split('\n').Length;

        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }

        // Notify listeners about text change (for LSP synchronization)
        TextChanged?.Invoke(this, value);
    }

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(Title));
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCaretLineChanged(int value)
    {
        CaretPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCaretColumnChanged(int value)
    {
        CaretPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateCaretPosition(int line, int column)
    {
        CaretLine = line;
        CaretColumn = column;
    }

    public void NavigateTo(int line, int column = 1)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(line, column));
    }

    public void ShowFind()
    {
        FindRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowReplace()
    {
        ReplaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestGoToDefinition()
    {
        GoToDefinitionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestFindAllReferences()
    {
        FindAllReferencesRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestShowCallHierarchy()
    {
        ShowCallHierarchyRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestToggleComment()
    {
        ToggleCommentRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestToggleWhitespace(bool show)
    {
        ToggleWhitespaceRequested?.Invoke(this, show);
    }

    public void RequestToggleColumnSelection(bool enabled)
    {
        ToggleColumnSelectionRequested?.Invoke(this, enabled);
    }

    public void RequestDuplicateLine()
    {
        DuplicateLineRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMoveLineUp()
    {
        MoveLineUpRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMoveLineDown()
    {
        MoveLineDownRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestDeleteLine()
    {
        DeleteLineRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestUndo()
    {
        UndoRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRedo()
    {
        RedoRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCut()
    {
        CutRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCopy()
    {
        CopyRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPaste()
    {
        PasteRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRenameSymbol()
    {
        RenameSymbolRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestExtractMethod()
    {
        ExtractMethodRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineMethod()
    {
        InlineMethodRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestIntroduceVariable()
    {
        IntroduceVariableRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestExtractConstant()
    {
        ExtractConstantRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineConstant()
    {
        InlineConstantRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineVariable()
    {
        InlineVariableRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestChangeSignature()
    {
        ChangeSignatureRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestEncapsulateField()
    {
        EncapsulateFieldRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineField()
    {
        InlineFieldRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMoveTypeToFile()
    {
        MoveTypeToFileRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestExtractInterface()
    {
        ExtractInterfaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestGenerateConstructor()
    {
        GenerateConstructorRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestImplementInterface()
    {
        ImplementInterfaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestOverrideMethod()
    {
        OverrideMethodRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestAddParameter()
    {
        AddParameterRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRemoveParameter()
    {
        RemoveParameterRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestReorderParameters()
    {
        ReorderParametersRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRenameParameter()
    {
        RenameParameterRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestChangeParameterType()
    {
        ChangeParameterTypeRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMakeParameterOptional()
    {
        MakeParameterOptionalRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMakeParameterRequired()
    {
        MakeParameterRequiredRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToNamedArguments()
    {
        ConvertToNamedArgumentsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToPositionalArguments()
    {
        ConvertToPositionalArgumentsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSafeDelete()
    {
        SafeDeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPullMembersUp()
    {
        PullMembersUpRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPushMembersDown()
    {
        PushMembersDownRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestUseBaseType()
    {
        UseBaseTypeRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToInterface()
    {
        ConvertToInterfaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInvertIf()
    {
        InvertIfRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToSelectCase()
    {
        ConvertToSelectCaseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSplitDeclaration()
    {
        SplitDeclarationRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestIntroduceField()
    {
        IntroduceFieldRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSurroundWith()
    {
        SurroundWithRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPeekDefinition()
    {
        PeekDefinitionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestFormatDocument()
    {
        FormatDocumentRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestOnTypeFormatting(int line, int column, string triggerCharacter)
    {
        OnTypeFormattingRequested?.Invoke(this, new OnTypeFormattingRequestEventArgs(line, column, triggerCharacter));
    }

    public void RequestCodeActions()
    {
        CodeActionsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestHover(int line, int column)
    {
        HoverRequested?.Invoke(this, new HoverRequestEventArgs(line, column));
    }

    public void ProvideHoverResult(HoverInfo? hover)
    {
        HoverResultReceived?.Invoke(this, new HoverResultEventArgs(hover));
    }

    public void ProvideCodeActions(IEnumerable<CodeActionInfo> actions)
    {
        CodeActionsReceived?.Invoke(this, new CodeActionResultEventArgs(actions.ToList()));
    }

    public void ProvideDefinitionResult(LocationInfo? location)
    {
        DefinitionResultReceived?.Invoke(this, new LocationResultEventArgs(location));
    }

    public void ProvideReferencesResult(IEnumerable<LocationInfo> locations)
    {
        ReferencesResultReceived?.Invoke(this, new ReferencesResultEventArgs(locations.ToList()));
    }

    public void RequestSignatureHelp(int line, int column)
    {
        SignatureHelpRequested?.Invoke(this, new SignatureHelpRequestEventArgs(line, column));
    }

    public void ProvideSignatureHelp(SignatureHelp? help)
    {
        SignatureHelpResultReceived?.Invoke(this, new SignatureHelpResultEventArgs(help));
    }

    public void RequestDocumentHighlight(int line, int column)
    {
        DocumentHighlightRequested?.Invoke(this, new DocumentHighlightRequestEventArgs(line, column));
    }

    public void ProvideDocumentHighlights(IEnumerable<DocumentHighlightInfo> highlights)
    {
        DocumentHighlightResultReceived?.Invoke(this, new DocumentHighlightResultEventArgs(highlights.ToList()));
    }

    public void ProvideRenameResult(WorkspaceEditInfo? edit, string? errorMessage = null)
    {
        RenameResultReceived?.Invoke(this, new RenameResultEventArgs(edit, errorMessage));
    }

    public void ProvideDocumentSymbols(IEnumerable<DocumentSymbol> symbols)
    {
        DocumentSymbolsReceived?.Invoke(this, new DocumentSymbolsResultEventArgs(symbols.ToList()));
    }

    public void RequestExpandSelection()
    {
        ExpandSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestShrinkSelection()
    {
        ShrinkSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ProvideSelectionRange(SelectionRangeInfo? range)
    {
        SelectionRangeReceived?.Invoke(this, new SelectionRangeResultEventArgs(range));
    }

    public void OnBreakpointToggled(int line)
    {
        BreakpointToggled?.Invoke(this, line);
    }

    public void OnConditionalBreakpointRequested(int line) => ConditionalBreakpointRequested?.Invoke(this, line);
    public void OnLogpointRequested(int line) => LogpointRequested?.Invoke(this, line);
    public void OnEditBreakpointRequested(int line) => EditBreakpointRequested?.Invoke(this, line);
    public void OnRemoveBreakpointRequested(int line) => RemoveBreakpointRequested?.Invoke(this, line);
    public void OnToggleEnableBreakpointRequested(int line) => ToggleEnableBreakpointRequested?.Invoke(this, line);

    public void RequestAddToWatch(string expression)
    {
        if (!string.IsNullOrWhiteSpace(expression))
        {
            AddToWatchRequested?.Invoke(this, expression);
        }
    }

    public void RequestDataTipEvaluation(string expression, double screenX, double screenY, int line = 0, int column = 0)
    {
        if (!string.IsNullOrWhiteSpace(expression))
        {
            DataTipEvaluationRequested?.Invoke(this, new DataTipEvaluationRequestEventArgs(expression, screenX, screenY, line, column));
        }
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return false;
        }

        try
        {
            // Trim trailing whitespace from all lines before saving if enabled
            if (TrimTrailingWhitespaceOnSave)
            {
                var lines = Text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = lines[i].TrimEnd(' ', '\t', '\r');
                }
                Text = string.Join("\n", lines);
            }

            await _fileService.WriteFileAsync(FilePath, Text, cancellationToken);
            _originalText = Text;
            IsDirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);

            _eventAggregator.Publish(new FileSavedEvent(FilePath));
        }
        catch
        {
            return false;
        }

        // ⚠ OUTSIDE the try that owns the save, and deliberately last. The document is already
        // on disk and the save has already succeeded; a problem regenerating the companion .bas is
        // reported as a diagnostic on that file, not as a failed save of this one.
        await RegenerateDesignerRegionsAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Puts the saved document's controls into the user's own <c>.bas</c> (D1).
    ///
    /// <para>⛔⛔ <b>This is what makes the designer more than a drawing.</b> The document
    /// describes the form; nothing on the desktop compiles from it. Only the two marked regions in
    /// the <c>.bas</c> declare the fields and build the controls, so without this call a user could
    /// drag a button onto the canvas, save, build, and get an error about the
    /// <c>InitializeComponent</c> the scaffold calls — in a file they never wrote a line of.</para>
    ///
    /// <para>⚠ On SAVE rather than on each edit. The two files are one unit: regenerating on every
    /// committed property change would put the .bas on disk while the .blform it was generated from
    /// was still an unsaved buffer, so a crash or a discarded edit would leave the pair describing
    /// two different forms.</para>
    ///
    /// <para>⚠ A refusal WRITES NOTHING and must still be seen. RegionWriter refuses rather than
    /// overwriting a hand-edited region (BL8011), and a refusal the user is never shown is
    /// indistinguishable from the designer quietly not working — so the findings go to the error
    /// list either way, keyed to the .bas they are about.</para>
    /// </summary>
    private async Task RegenerateDesignerRegionsAsync(CancellationToken cancellationToken)
    {
        if (!IsFormDocument || FilePath == null)
        {
            return;
        }

        // ⛔⛔ EVERY path below publishes exactly once, and always on THIS key. The aggregator
        // keys findings by (collection, file), so a finding published against the .blform is a
        // DIFFERENT key from one against the .bas — and a later good save, which publishes on the
        // .bas, would never clear it. One transient IO error would have left a phantom entry in
        // the Error List for the rest of the session, pointing at a problem that no longer exists
        // and that nothing could remove. Same reason the early returns publish an empty list
        // rather than returning silently: an empty publish is how the previous save's findings
        // are retracted.
        var codePath = BasicLang.Forms.FormCodeBehind.PathFor(FilePath);
        var findings = new List<DiagnosticItem>();

        try
        {
            // A REFUSED document has no trustworthy model to generate from. The designer has
            // already opened it read-only and said why, but the .bas is now out of step with a
            // document the user just saved, and silence would read as "the designer wrote it".
            var file = DesignFile;
            if (file == null)
            {
                findings.Add(new DiagnosticItem
                {
                    Id = BasicLang.Forms.DesignCodes.MalformedDocument,
                    Message = $"'{Path.GetFileName(FilePath)}' could not be read as a form, so " +
                              $"'{Path.GetFileName(codePath)}' was not regenerated and no longer " +
                              "matches it. Fix the document and save again.",
                    Severity = DiagnosticSeverity.Warning,
                    FilePath = codePath,
                    Source = DesignerDiagnosticSource
                });
            }
            else if (!await _fileService.FileExistsAsync(codePath))
            {
                findings.Add(new DiagnosticItem
                {
                    Id = BasicLang.Forms.DesignCodes.RegionAbsent,
                    Message = $"'{Path.GetFileName(FilePath)}' has no code-behind: expected " +
                              $"'{Path.GetFileName(codePath)}' beside it. The designer has " +
                              "nowhere to write the controls, so nothing was generated.",
                    Severity = DiagnosticSeverity.Warning,
                    FilePath = codePath,
                    Source = DesignerDiagnosticSource
                });
            }
            else
            {
                var before = await _fileService.ReadFileAsync(codePath, cancellationToken);
                var result = BasicLang.Forms.FormCodeBehind.Regenerate(file, codePath, before);

                if (result.Changed)
                {
                    await _fileService.WriteFileAsync(codePath, result.Text, cancellationToken);
                    _eventAggregator.Publish(new FileSavedEvent(codePath));
                }

                findings.AddRange(result.Diagnostics.Select(ToDiagnosticItem));
            }
        }
        catch (Exception ex)
        {
            findings.Add(new DiagnosticItem
            {
                Id = BasicLang.Forms.DesignCodes.RegionAbsent,
                Message = $"the designer could not update '{Path.GetFileName(codePath)}': {ex.Message}",
                Severity = DiagnosticSeverity.Error,
                FilePath = codePath,
                Source = DesignerDiagnosticSource
            });
        }

        _eventAggregator.Publish(new DesignerDiagnosticsEvent(codePath, findings));
    }

    /// <summary>Names the collection these findings own, so a republish replaces only its own.</summary>
    public const string DesignerDiagnosticSource = "Form designer";

    private static DiagnosticItem ToDiagnosticItem(BasicLang.Forms.DesignDiagnostic diagnostic) =>
        new()
        {
            Id = diagnostic.Code,
            Message = diagnostic.Message,
            Severity = diagnostic.IsWarning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            FilePath = diagnostic.FilePath,
            Line = diagnostic.Line,
            Column = diagnostic.Column,
            Source = DesignerDiagnosticSource
        };

    public async Task<bool> SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        var oldPath = FilePath;
        FilePath = path;

        if (await SaveAsync(cancellationToken))
        {
            return true;
        }

        FilePath = oldPath;
        return false;
    }

    public Task<bool> CloseAsync()
    {
        return Task.FromResult(true);
    }

    public void SetContent(string content)
    {
        _originalText = content;

        // ⛔ Setting Text does NOT clear the undo stack — it is an ordinary replace, and an
        // ordinary replace is undoable. The comment here used to claim the opposite, and the claim
        // was false: opening a file left "load the file" sitting on the stack as step one, so a
        // single Ctrl+Z on a freshly opened document emptied it. Found by a designer-undo test,
        // but it was never designer-specific — Code view had it too.
        TextDocument.Text = content;
        TextDocument.UndoStack.ClearAll();
        // Keep Text in sync for backward compatibility
        Text = content;
        IsDirty = false;
    }

    /// <summary>
    /// Replace content while preserving undo history (VS Code-like behavior).
    /// Use this after refactoring operations instead of SetContent.
    /// </summary>
    public void ReplaceContent(string newContent)
    {
        if (TextDocument.Text == newContent) return;

        // Use Replace which creates an undoable operation
        TextDocument.BeginUpdate();
        try
        {
            TextDocument.Replace(0, TextDocument.TextLength, newContent);
        }
        finally
        {
            TextDocument.EndUpdate();
        }

        // Update backing fields
        _text = newContent;

        // Update dirty state
        var wasDirty = IsDirty;
        IsDirty = _text != _originalText;

        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Called by the view when editor text changes. Updates Text without triggering
    /// binding feedback that would clear the undo stack.
    /// </summary>
    public void UpdateTextFromEditor(string newText)
    {
        var changed = !string.Equals(_text, newText, StringComparison.Ordinal);

        // Update the backing field directly to avoid triggering property change
        // that would push back to the editor and clear undo
        _text = newText;

        // ⛔ The canvas draws DesignDocument, which is bound — so it only refreshes when told the
        // property changed, and this path deliberately bypasses the property setter. Without this
        // the canvas keeps drawing the form as it was: edit the XML in Code view and switch to
        // Design and you see the OLD form, and — now that the editor's own Ctrl+Z can undo a
        // designer edit, because those edits are on its stack — undoing from the editor would move
        // the control in the file and leave it where it was on screen.
        if (changed && IsFormDocument)
        {
            _designFile = null;
            _designFileText = null;
            OnPropertyChanged(nameof(DesignFile));
            OnPropertyChanged(nameof(DesignDocument));
            DesignModelRevision++;
        }

        // Update dirty state
        var wasDirty = IsDirty;
        IsDirty = _text != _originalText;

        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }

        // Notify that text changed (for any listeners that need the new text)
        TextChanged?.Invoke(this, newText);
    }

    [RelayCommand]
    private void ToggleSplitView()
    {
        IsSplitView = !IsSplitView;
    }

    [RelayCommand]
    private void SplitHorizontal()
    {
        SplitOrientation = SplitOrientation.Horizontal;
        IsSplitView = true;
    }

    [RelayCommand]
    private void SplitVertical()
    {
        SplitOrientation = SplitOrientation.Vertical;
        IsSplitView = true;
    }

    [RelayCommand]
    private void CloseSplit()
    {
        IsSplitView = false;
    }

    #region Editor Focus

    /// <summary>
    /// Raised when the editor control for this document loses keyboard focus
    /// (focus moved outside the editor). Used for auto-save OnFocusChange mode.
    /// </summary>
    public event EventHandler? EditorFocusLost;

    /// <summary>
    /// Called by the view when the editor control loses focus to something
    /// outside the editor. Delegates to subscribers (auto-save wiring).
    /// </summary>
    public void NotifyEditorFocusLost()
    {
        EditorFocusLost?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Diagnostics and Completion

    public event EventHandler<IEnumerable<DiagnosticItem>>? DiagnosticsUpdated;
    public event EventHandler<CompletionRequestedEventArgs>? CompletionRequested;
    public event EventHandler<IEnumerable<Core.Abstractions.Services.CompletionItem>>? CompletionReceived;

    /// <summary>
    /// Updates diagnostics for this document (error highlighting)
    /// </summary>
    public void UpdateDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        DiagnosticsUpdated?.Invoke(this, diagnostics);
    }

    /// <summary>
    /// Requests code completion at the specified position
    /// </summary>
    public void RequestCompletion(int line, int column)
    {
        CompletionRequested?.Invoke(this, new CompletionRequestedEventArgs(line, column));
    }

    /// <summary>
    /// Provides completion items to the editor
    /// </summary>
    public void ProvideCompletions(IEnumerable<Core.Abstractions.Services.CompletionItem> completions)
    {
        var list = completions.ToList();
        CompletionReceived?.Invoke(this, list);
    }

    #endregion

    #region Code Lens

    public event EventHandler<IEnumerable<CodeLensItemInfo>>? CodeLensUpdated;
    public event EventHandler<CodeLensClickedInfo>? CodeLensCommandRequested;

    /// <summary>
    /// Shows code lens annotations above function/class lines.
    /// </summary>
    public void ShowCodeLenses(IEnumerable<CodeLensItemInfo> lenses)
    {
        CodeLensUpdated?.Invoke(this, lenses);
    }

    /// <summary>
    /// Clears all code lens annotations.
    /// </summary>
    public void ClearCodeLenses()
    {
        CodeLensUpdated?.Invoke(this, Enumerable.Empty<CodeLensItemInfo>());
    }

    /// <summary>
    /// Called when user clicks a code lens item.
    /// </summary>
    public void OnCodeLensClicked(CodeLensClickedInfo info)
    {
        CodeLensCommandRequested?.Invoke(this, info);
    }

    #endregion

    #region Inline Debug Values

    public event EventHandler<IEnumerable<InlineDebugValueInfo>>? InlineDebugValuesUpdated;

    /// <summary>
    /// Shows inline debug variable values next to code lines.
    /// </summary>
    public void ShowInlineDebugValues(IEnumerable<InlineDebugValueInfo> values)
    {
        InlineDebugValuesUpdated?.Invoke(this, values);
    }

    /// <summary>
    /// Clears all inline debug values.
    /// </summary>
    public void ClearInlineDebugValues()
    {
        InlineDebugValuesUpdated?.Invoke(this, Enumerable.Empty<InlineDebugValueInfo>());
    }

    #endregion

    #region Semantic Tokens

    public event EventHandler<int[]>? SemanticTokensUpdated;

    /// <summary>
    /// Updates semantic token highlighting in the editor.
    /// </summary>
    public void UpdateSemanticTokens(int[] encodedData)
    {
        SemanticTokensUpdated?.Invoke(this, encodedData);
    }

    /// <summary>
    /// Clears semantic token highlighting.
    /// </summary>
    public void ClearSemanticTokens()
    {
        SemanticTokensUpdated?.Invoke(this, Array.Empty<int>());
    }

    #endregion

    #region Inline Blame Annotations

    /// <summary>
    /// Fired when the inline blame annotation for the current line should be updated.
    /// The tuple contains (lineNumber, annotationText). If annotationText is empty, the annotation should be cleared.
    /// </summary>
    public event EventHandler<(int LineNumber, string AnnotationText)>? BlameAnnotationUpdated;

    /// <summary>
    /// Updates the inline blame annotation displayed at the specified line.
    /// </summary>
    public void UpdateBlameAnnotation(int lineNumber, string annotationText)
    {
        BlameAnnotationUpdated?.Invoke(this, (lineNumber, annotationText));
    }

    /// <summary>
    /// Clears the inline blame annotation.
    /// </summary>
    public void ClearBlameAnnotation()
    {
        BlameAnnotationUpdated?.Invoke(this, (0, ""));
    }

    #endregion

    #region Breakpoint Visuals

    public event EventHandler<Dictionary<int, BreakpointVisualInfo>>? BreakpointVisualsUpdated;

    /// <summary>
    /// Updates the breakpoint margin visuals with verified/unverified state and breakpoint kind.
    /// </summary>
    public void UpdateBreakpointVisuals(Dictionary<int, BreakpointVisualInfo> visuals)
    {
        BreakpointVisualsUpdated?.Invoke(this, visuals);
    }

    #endregion

    #region Editor Action Requests (Toggle, Cursor, etc.)

    public event EventHandler? ToggleBlockCommentRequested;
    public event EventHandler? SelectAllRequested;
    public event EventHandler? CopyLineUpRequested;
    public event EventHandler? CopyLineDownRequested;
    public event EventHandler? AddCursorAboveRequested;
    public event EventHandler? AddCursorBelowRequested;
    public event EventHandler? AddCursorsToLineEndsRequested;
    public event EventHandler<bool>? ToggleMinimapRequested;
    public event EventHandler<bool>? ToggleBreadcrumbsRequested;
    public event EventHandler<bool>? ToggleStickyScrollRequested;
    public event EventHandler<bool>? ToggleWordWrapRequested;
    public event EventHandler? GoToBracketRequested;

    public void RequestToggleBlockComment()
    {
        ToggleBlockCommentRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSelectAll()
    {
        SelectAllRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCopyLineUp()
    {
        CopyLineUpRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCopyLineDown()
    {
        CopyLineDownRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestAddCursorAbove()
    {
        AddCursorAboveRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestAddCursorBelow()
    {
        AddCursorBelowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestAddCursorsToLineEnds()
    {
        AddCursorsToLineEndsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestToggleMinimap(bool show)
    {
        ToggleMinimapRequested?.Invoke(this, show);
    }

    public void RequestToggleBreadcrumbs(bool show)
    {
        ToggleBreadcrumbsRequested?.Invoke(this, show);
    }

    public void RequestToggleStickyScroll(bool show)
    {
        ToggleStickyScrollRequested?.Invoke(this, show);
    }

    public void RequestToggleWordWrap(bool wrap)
    {
        ToggleWordWrapRequested?.Invoke(this, wrap);
    }

    public void RequestGoToBracket()
    {
        GoToBracketRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Execution Line

    public event EventHandler<int?>? ExecutionLineChanged;

    public void SetExecutionLine(int line)
    {
        ExecutionLineChanged?.Invoke(this, line);
    }

    public void ClearExecutionLine()
    {
        ExecutionLineChanged?.Invoke(this, null);
    }

    #endregion
}

/// <summary>
/// Represents a code lens item to display above a line.
/// </summary>
public class CodeLensItemInfo
{
    public int Line { get; set; }
    public string Title { get; set; } = "";
    public string CommandName { get; set; } = "";
    public List<object>? CommandArguments { get; set; }
}

/// <summary>
/// Info about a clicked code lens command.
/// </summary>
public class CodeLensClickedInfo
{
    public string Title { get; set; } = "";
    public string CommandName { get; set; } = "";
    public List<object>? CommandArguments { get; set; }
    public int Line { get; set; }
}

/// <summary>
/// Represents a variable value to display inline during debugging.
/// </summary>
public class InlineDebugValueInfo
{
    public int Line { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public class CompletionRequestedEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public CompletionRequestedEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public enum SplitOrientation
{
    Horizontal,
    Vertical
}

public class NavigationRequestedEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public NavigationRequestedEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class DataTipEvaluationRequestEventArgs : EventArgs
{
    public string Expression { get; }
    public double ScreenX { get; }
    public double ScreenY { get; }
    public int Line { get; }
    public int Column { get; }

    public DataTipEvaluationRequestEventArgs(string expression, double screenX, double screenY, int line = 0, int column = 0)
    {
        Expression = expression;
        ScreenX = screenX;
        ScreenY = screenY;
        Line = line;
        Column = column;
    }
}

public class HoverRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public HoverRequestEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class HoverResultEventArgs : EventArgs
{
    public HoverInfo? Hover { get; }

    public HoverResultEventArgs(HoverInfo? hover)
    {
        Hover = hover;
    }
}

public class CodeActionResultEventArgs : EventArgs
{
    public IReadOnlyList<CodeActionInfo> Actions { get; }

    public CodeActionResultEventArgs(IReadOnlyList<CodeActionInfo> actions)
    {
        Actions = actions;
    }
}

public class LocationResultEventArgs : EventArgs
{
    public LocationInfo? Location { get; }

    public LocationResultEventArgs(LocationInfo? location)
    {
        Location = location;
    }
}

public class ReferencesResultEventArgs : EventArgs
{
    public IReadOnlyList<LocationInfo> Locations { get; }

    public ReferencesResultEventArgs(IReadOnlyList<LocationInfo> locations)
    {
        Locations = locations;
    }
}

public class SignatureHelpRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public SignatureHelpRequestEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class SignatureHelpResultEventArgs : EventArgs
{
    public SignatureHelp? Help { get; }

    public SignatureHelpResultEventArgs(SignatureHelp? help)
    {
        Help = help;
    }
}

public class DocumentHighlightRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public DocumentHighlightRequestEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class DocumentHighlightResultEventArgs : EventArgs
{
    public IReadOnlyList<DocumentHighlightInfo> Highlights { get; }

    public DocumentHighlightResultEventArgs(IReadOnlyList<DocumentHighlightInfo> highlights)
    {
        Highlights = highlights;
    }
}

public class DocumentHighlightInfo
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public bool IsWrite { get; set; }
}

public class RenameResultEventArgs : EventArgs
{
    public WorkspaceEditInfo? Edit { get; }
    public string? ErrorMessage { get; }
    public bool Success => Edit != null && ErrorMessage == null;

    public RenameResultEventArgs(WorkspaceEditInfo? edit, string? errorMessage = null)
    {
        Edit = edit;
        ErrorMessage = errorMessage;
    }
}

public class DocumentSymbolsResultEventArgs : EventArgs
{
    public IReadOnlyList<DocumentSymbol> Symbols { get; }

    public DocumentSymbolsResultEventArgs(IReadOnlyList<DocumentSymbol> symbols)
    {
        Symbols = symbols;
    }
}

public class SelectionRangeResultEventArgs : EventArgs
{
    public SelectionRangeInfo? Range { get; }

    public SelectionRangeResultEventArgs(SelectionRangeInfo? range)
    {
        Range = range;
    }
}

public class OnTypeFormattingRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }
    public string TriggerCharacter { get; }

    public OnTypeFormattingRequestEventArgs(int line, int column, string triggerCharacter)
    {
        Line = line;
        Column = column;
        TriggerCharacter = triggerCharacter;
    }
}
