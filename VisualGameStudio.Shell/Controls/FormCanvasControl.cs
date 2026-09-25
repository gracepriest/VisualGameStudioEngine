using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BasicLang.Forms;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Shell.Controls;

/// <summary>
/// The designer canvas: a <b>schematic</b> of a form document, drawn directly.
///
/// <para>⛔ A schematic, not a preview — and the spec says so (D-WYSIWYG): the IDE has no browser
/// and no WinForms surface, so nothing here can show what the running program looks like. F5 to the
/// real target is the renderer. Drawing rectangles that merely suggest a preview would be worse
/// than drawing obvious boxes, because the user would trust it.</para>
///
/// <para>⛔ Lives in <c>VisualGameStudio.Shell</c>, not <c>VisualGameStudio.Editor</c>, which has no
/// reference to <c>BasicLang/Forms/</c> and so cannot see a <see cref="FormDocument"/> at all.</para>
///
/// <para>⛔ <b>This used to say "not visually verified — what the canvas LOOKS like is checked by
/// running the IDE and nothing else". That is no longer true, and believing it cost real defects.</b>
/// <c>Avalonia.Headless</c> + <c>Avalonia.Skia</c> drive this control and render real pixels:
/// <c>FormCanvasRenderTests</c> renders every catalog kind and hashes the frames. It was written
/// after the canvas was found drawing all ten kinds as one identical grey box — invisible to every
/// existing test, because they all asked about geometry and hit-testing, never about what was
/// DRAWN. <see cref="FormCanvasTransform"/> remains separately covered, since a drift there has no
/// visual symptom at all.</para>
/// </summary>
public class FormCanvasControl : Control
{
    public static readonly StyledProperty<FormDocument?> DocumentProperty =
        AvaloniaProperty.Register<FormCanvasControl, FormDocument?>(nameof(Document));

    public static readonly StyledProperty<FormControl?> SelectedControlProperty =
        AvaloniaProperty.Register<FormCanvasControl, FormControl?>(
            nameof(SelectedControl), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Bumped by the host whenever the MODEL changed without the document reference changing.
    ///
    /// <para>⛔⛔ Without this the canvas never redraws after a property-grid edit. The grid
    /// mutates <c>FormControl.Properties</c> IN PLACE — deliberately, so the canvas, the grid and
    /// the writer all share one object graph — so <see cref="DocumentProperty"/> still holds the
    /// same reference, <c>AffectsRender</c> sees no change, and nothing invalidates. The user
    /// types a new caption, the document and the file both update, and the box on the canvas keeps
    /// the old text. Handing the canvas a fresh copy instead would fix the paint and break the
    /// selection, which is the trade the shared graph exists to avoid.</para>
    /// </summary>
    public static readonly StyledProperty<int> ModelRevisionProperty =
        AvaloniaProperty.Register<FormCanvasControl, int>(nameof(ModelRevision));

    /// <summary>
    /// Invoked with a <see cref="FormControlDropRequest"/> when a toolbox control is dropped.
    ///
    /// <para>⛔ A COMMAND, not an event, and that is deliberate. This control's contract is "state
    /// arrives through styled properties, redraws come from AffectsRender, input comes from an
    /// override" — precisely so it holds nothing that needs tearing down when Dock re-attaches the
    /// same instance on a tab drag or a panel maximize. A C# event here would need the host to
    /// subscribe, and the host has no single place to unsubscribe; the symptom of getting that
    /// wrong is one extra handler per re-dock, so a single drop eventually places two or three
    /// controls. A bound command has no such lifetime.</para>
    /// </summary>
    public static readonly StyledProperty<ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(DropCommand));

    /// <summary>
    /// Invoked ONCE when a move or resize drag ends, so the host writes the document.
    ///
    /// <para>⛔⛔ Once, on RELEASE — not per pointer-move. A commit per move would run the
    /// structure-preserving writer, re-serialize the document and reset the editor's text on every
    /// mouse event: dozens of writes per drag, dozens of undo entries to get back one nudge, and
    /// the text of the file changing under the user's own Code view while they are still holding
    /// the button down. The model is mutated live so the canvas repaints; only the FILE waits.</para>
    /// </summary>
    public static readonly StyledProperty<ICommand?> CommitGeometryCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(CommitGeometryCommand));

    /// <summary>
    /// The drag payload a toolbox item carries: the catalog kind, as a string.
    ///
    /// <para>⚠ A private format rather than <c>DataFormats.Text</c>. Dragging text out of the
    /// editor — or in from another application — would otherwise look exactly like a toolbox drag,
    /// and dropping a paragraph of prose on the form would place a control named after its first
    /// word, or refuse with a message about a control kind the user never mentioned.</para>
    /// </summary>
    public const string ControlKindFormat = "vgs/form-control-kind";

    static FormCanvasControl()
    {
        // Re-draw when what is drawn changes. Without this the canvas keeps showing the previous
        // document after a switch, which reads as "the designer opened the wrong file".
        AffectsRender<FormCanvasControl>(
            DocumentProperty, SelectedControlProperty, ModelRevisionProperty, TypeHereHostProperty,
            EditingItemProperty);
    }

    public FormDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public FormControl? SelectedControl
    {
        get => GetValue(SelectedControlProperty);
        set => SetValue(SelectedControlProperty, value);
    }

    public int ModelRevision
    {
        get => GetValue(ModelRevisionProperty);
        set => SetValue(ModelRevisionProperty, value);
    }

    public ICommand? DropCommand
    {
        get => GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    public ICommand? CommitGeometryCommand
    {
        get => GetValue(CommitGeometryCommandProperty);
        set => SetValue(CommitGeometryCommandProperty, value);
    }

    /// <summary>
    /// Invoked when Delete is pressed with a control selected.
    ///
    /// <para>⛔ A command rather than the canvas removing the control itself. Deleting changes the
    /// document's SHAPE, not just a geometry field: the selection has to be cleared, the designer
    /// region regenerated and the file written. The canvas mutates geometry in place because the
    /// host owns the same object graph; it does not get to restructure it.</para>
    /// </summary>
    public static readonly StyledProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(DeleteCommand));

    public ICommand? DeleteCommand
    {
        get => GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    /// <summary>
    /// Invoked when a control is double-clicked, with that control as the parameter — VS's "open my
    /// handler" gesture (Task 22).
    ///
    /// <para>⛔ A command, for the same reason as <see cref="DeleteCommand"/>, only more so.
    /// Answering this gesture writes a SECOND file (the user's <c>.bas</c>), opens a document and
    /// moves a caret. None of that belongs to a drawing surface; the canvas reports the gesture and
    /// which control it landed on, and the host decides what it means.</para>
    ///
    /// <para>⚠ The parameter is the control, never null-meaning-"use the selection". A double-click
    /// on the form's background is not a gesture on a control, and passing the previous selection
    /// would open a handler for something the user was not pointing at.</para>
    /// </summary>
    public static readonly StyledProperty<ICommand?> ActivateControlCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(ActivateControlCommand));

    public ICommand? ActivateControlCommand
    {
        get => GetValue(ActivateControlCommandProperty);
        set => SetValue(ActivateControlCommandProperty, value);
    }

    /// <summary>
    /// The multi-selection (Task 20). <see cref="SelectedControl"/> remains its PRIMARY.
    ///
    /// <para>⚠ Owned by the HOST, not by the canvas, because the align, size, z-order and clipboard
    /// commands all operate on it and they live on the view model. The canvas is what mutates it —
    /// clicks and the rubber band are gestures — but it is not what it belongs to.</para>
    ///
    /// <para>⛔ This is the one external object the canvas subscribes to, so it is also the one it
    /// has to UNSUBSCRIBE from: <see cref="OnPropertyChanged"/> detaches the old selection when the
    /// property changes. Without that, re-docking or switching documents leaves a handler per past
    /// selection alive and every repaint fires all of them.</para>
    /// </summary>
    public static readonly StyledProperty<FormSelection?> SelectionProperty =
        AvaloniaProperty.Register<FormCanvasControl, FormSelection?>(nameof(Selection));

    public FormSelection? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>Ctrl+C, Ctrl+X, Ctrl+V. Commands for the same reason <see cref="DeleteCommand"/> is.</summary>
    public static readonly StyledProperty<ICommand?> CopyCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(CopyCommand));

    public static readonly StyledProperty<ICommand?> CutCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(CutCommand));

    public static readonly StyledProperty<ICommand?> PasteCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(PasteCommand));

    public ICommand? CopyCommand
    {
        get => GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public ICommand? CutCommand
    {
        get => GetValue(CutCommandProperty);
        set => SetValue(CutCommandProperty, value);
    }

    public ICommand? PasteCommand
    {
        get => GetValue(PasteCommandProperty);
        set => SetValue(PasteCommandProperty, value);
    }

    /// <summary>
    /// Task 21 (commit 24d). The host whose Type Here slot is currently being edited: the canvas
    /// highlights that slot and reports its rectangle through <see cref="TypeHereBounds"/>.
    ///
    /// <para>⛔ In <c>AffectsRender</c> because it is an INPUT to drawing — and it is the ONLY thing
    /// that changes on the real gesture. A slot exists only for the selected strip, so by the time
    /// the host begins editing it is ALREADY the selection: neither <c>Document</c> nor
    /// <c>SelectedControl</c> changes, nothing else invalidates, and without this registration no
    /// render would run — leaving <see cref="TypeHereBounds"/> at its previous value and the overlay
    /// editor over the wrong rectangle, or over none at all.</para>
    ///
    /// <para>⚠ Matched by REFERENCE against a slot entry's <c>Host</c>, never against its
    /// <c>Control</c> — a slot entry's Control is always null. See the predicate in <c>Render</c>.</para>
    /// </summary>
    public static readonly StyledProperty<FormControl?> TypeHereHostProperty =
        AvaloniaProperty.Register<FormCanvasControl, FormControl?>(nameof(TypeHereHost));

    public FormControl? TypeHereHost
    {
        get => GetValue(TypeHereHostProperty);
        set => SetValue(TypeHereHostProperty, value);
    }

    /// <summary>
    /// Task 21 (commit 24d). An OUTPUT, not an input: the CANVAS rectangle of the Type Here slot
    /// <see cref="TypeHereHost"/> names, so a bound overlay control can be positioned exactly over
    /// it, and <c>default</c> when no such slot is on screen. Correct after every render; normally
    /// already correct before one, because each input republishes it as it changes.
    ///
    /// <para>⛔⛔ Never CHANGED from inside <c>Render</c> — its subscriber lays out a TextBox, and a
    /// layout invalidation during the render pass throws inside the binding, where it is swallowed
    /// (the owner's "typed NOT in the Type Here"). See <c>PublishTypeHereBoundsFromRender</c>. And
    /// deliberately NOT in <c>AffectsRender</c>: it is an output of the same walk Render does.</para>
    ///
    /// <para>⚠ Canvas pixels, not form units. The overlay is a sibling of this control in the visual
    /// tree, so it is positioned in the same space this control is drawn in — a rectangle in form
    /// units would be right at 1:1 zoom and wrong at every other, which is the class of bug that
    /// only appears on someone else's monitor.</para>
    /// </summary>
    public static readonly StyledProperty<Rect> TypeHereBoundsProperty =
        AvaloniaProperty.Register<FormCanvasControl, Rect>(nameof(TypeHereBounds));

    public Rect TypeHereBounds
    {
        get => GetValue(TypeHereBoundsProperty);
        set => SetValue(TypeHereBoundsProperty, value);
    }

    /// <summary>
    /// Task 21 (commit 24d). Invoked with the HOST when a left press lands on that host's Type Here
    /// slot — the gesture that turns the slot into an editor.
    ///
    /// <para>⛔ A command, for <see cref="DeleteCommand"/>'s reason: answering this opens a text
    /// editor over the canvas, moves focus and eventually restructures the document. The canvas
    /// reports which host was pointed at; the host decides what that means.</para>
    ///
    /// <para>⚠ The press check that fires it runs BEFORE the handle test in
    /// <see cref="OnPointerPressed"/> — see the comment at that site for why the ordering is
    /// load-bearing in two different ways.</para>
    /// </summary>
    public static readonly StyledProperty<ICommand?> BeginTypeHereCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(BeginTypeHereCommand));

    public ICommand? BeginTypeHereCommand
    {
        get => GetValue(BeginTypeHereCommandProperty);
        set => SetValue(BeginTypeHereCommandProperty, value);
    }

    /// <summary>
    /// "Can't edit a menu item once it is entered" (owner's report, 2026-09-23). Invoked with an
    /// EXISTING item when the user's rename gesture lands on it — a SECOND single click on a cell
    /// that is already the selection, or F2 with an item selected — as opposed to
    /// <see cref="BeginTypeHereCommand"/>, which is the Type Here SLOT's "create a new item" press.
    /// Offered only for an item <see cref="FormCanvasTransform.IsRenamableItem"/> accepts (never a
    /// separator). See <see cref="OfferRename"/> for how it stays apart from a double-click.
    /// </summary>
    public static readonly StyledProperty<ICommand?> EditItemCommandProperty =
        AvaloniaProperty.Register<FormCanvasControl, ICommand?>(nameof(EditItemCommand));

    public ICommand? EditItemCommand
    {
        get => GetValue(EditItemCommandProperty);
        set => SetValue(EditItemCommandProperty, value);
    }

    /// <summary>
    /// The item currently being RENAMED in place (bound to <c>StripEditor.EditTarget</c>), or null.
    ///
    /// <para>An INPUT to drawing (in <c>AffectsRender</c>): while it is set, <see cref="TypeHereBounds"/>
    /// publishes the ITEM's OWN cell (<see cref="FormCanvasTransform.EditBoxBounds"/>) and no Type
    /// Here slot is highlighted — in rename mode <see cref="TypeHereHost"/> is the item too, and its
    /// open dropdown's slot is hosted by it.</para>
    ///
    /// <para>⚠ TwoWay BY DEFAULT, like a selection: the canvas writes it when it offers a rename and
    /// CLEARS it when the second press of a double-click arrives, and the view model's
    /// <c>OnEditTargetChanged</c> closes the editor on that withdrawal. The AXAML binding carries no
    /// <c>Mode</c>, so the mode lives here.</para>
    /// </summary>
    public static readonly StyledProperty<FormControl?> EditingItemProperty =
        AvaloniaProperty.Register<FormCanvasControl, FormControl?>(
            nameof(EditingItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public FormControl? EditingItem
    {
        get => GetValue(EditingItemProperty);
        set => SetValue(EditingItemProperty, value);
    }

    /// <summary>
    /// The item the user's PREVIOUS primary press on this canvas landed on and left as the sole
    /// selection — the only thing that arms VS's "click again to rename" (owner's report,
    /// 2026-09-24: "not able to edit an existing item at all").
    ///
    /// <para>⛔⛔ <c>ReferenceEquals(hit, SelectedControl)</c> alone is NOT the rule. A Type Here
    /// commit selects the item it just created, through the view model's one selection store and
    /// the TwoWay <c>SelectedControl</c> binding, before the user has clicked it at all — so the
    /// user's FIRST physical click on it looked like a second one, opened the rename silently, and
    /// the user's next click closed it again. Selection that arrives by ANY route other than a
    /// press here (Type Here commit, property grid, undo, tray, keyboard, a document switch) must
    /// never arm a rename, so <see cref="OnPropertyChanged"/> disarms on every
    /// <see cref="SelectedControl"/> change that <see cref="OnPointerPressed"/> did not make, and
    /// every press consumes the armed state before deciding anything.</para>
    ///
    /// <para>F2 does not read this: it is an explicit request, not an inference from clicks.</para>
    /// </summary>
    private FormControl? _renameArmedFor;

    /// <summary>True only while <see cref="OnPointerPressed"/> itself is changing the selection, so
    /// that change — and only that change — does not disarm <see cref="_renameArmedFor"/>.</summary>
    private bool _pressIsSelecting;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // See _renameArmedFor: a selection this canvas's own press did not make never arms a rename.
        if ((change.Property == SelectedControlProperty && !_pressIsSelecting) ||
            change.Property == DocumentProperty ||
            change.Property == SelectionProperty)
        {
            _renameArmedFor = null;
        }

        // Every input to TypeHereBounds — the AffectsRender list above plus the size Fit reads —
        // republishes it HERE, outside the render pass. See PublishTypeHereBoundsFromRender.
        if (change.Property == DocumentProperty ||
            change.Property == SelectedControlProperty ||
            change.Property == ModelRevisionProperty ||
            change.Property == TypeHereHostProperty ||
            change.Property == EditingItemProperty ||
            change.Property == BoundsProperty)
        {
            RefreshTypeHereBounds();
        }

        if (change.Property != SelectionProperty)
        {
            return;
        }

        if (change.OldValue is FormSelection old)
        {
            old.Changed -= OnSelectionChanged;
        }

        if (change.NewValue is FormSelection next)
        {
            next.Changed += OnSelectionChanged;
        }

        InvalidateVisual();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        // The primary is what the property grid shows, so it follows the selection rather than being
        // set separately at every call site.
        SelectedControl = Selection?.Primary;
        InvalidateVisual();
    }

    /// <summary>
    /// Everything currently selected — the multi-selection when there is one, otherwise just the
    /// primary, so a canvas with no host-supplied <see cref="Selection"/> still behaves.
    /// </summary>
    private IReadOnlyList<FormControl> SelectedSet =>
        Selection is { IsEmpty: false } selection
            ? selection.Controls
            : SelectedControl is { } single
                ? new[] { single }
                : Array.Empty<FormControl>();

    public FormCanvasControl()
    {
        // ⚠ These two handlers are on THIS control's own attached routed events — they live and die
        // with the instance and are not the external subscription the note below warns about. The
        // canvas still holds no handler on anything it does not own.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // ⚠ Gestures.DoubleTappedEvent rather than an OnDoubleTapped override: Control does not
        // offer one, and the gesture is raised by the recognizer, not by the pointer events above.
        AddHandler(Gestures.DoubleTappedEvent, OnCanvasDoubleTapped);

        // ⛔ Focusable so the design view's Ctrl+Z reaches it. A Control is not focusable by
        // default, so without this the keyboard focus stays on the code editor UNDERNEATH the
        // design overlay: Ctrl+Z would run the EDITOR's undo, which now shares this document's
        // undo stack, so the text would rewind correctly and the canvas would keep drawing the
        // control where it used to be.
        Focusable = true;

        // ⛔⛔ F2 is a KEY BINDING on this control, not an OnKeyDown arm — measured against
        // Avalonia 11.3.13's KeyboardDevice.ProcessRawEvent: on KeyDown it walks the KeyBindings of
        // the focused element and then every VISUAL ANCESTOR up to the window, nearest first, and
        // only THEN raises KeyDownEvent — already Handled if any binding's command ran. So
        // MainWindow's own <KeyBinding Gesture="F2" Command="{Binding NextBookmarkCommand}"/> ran
        // and consumed F2 before this control's OnKeyDown (or even a Tunnel handler, which could
        // only observe a key the window had already acted on) ever saw it. A binding on the focused
        // element itself is the one thing tried before the window's. Its CanExecute is the whole
        // "does the designer want this F2" test: false leaves the key unhandled, so the walk goes
        // on to the window and F2 is Next Bookmark again. SolutionExplorerView coexists with the
        // same window binding the same way.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F2),
            Command = new RenameOnF2Command(this)
        });
    }

    /// <summary>
    /// F2's command: rename the selected item in place, the keyboard half of VS's rename gesture —
    /// the second click's equivalent.
    ///
    /// <para>⛔ <see cref="CanExecute"/> must be exactly "F2 would open a rename", never looser:
    /// <see cref="KeyBinding.TryHandle"/> marks the key Handled whenever it is true, and a handled
    /// F2 never reaches the window's Next Bookmark binding.</para>
    /// </summary>
    private sealed class RenameOnF2Command : ICommand
    {
        private readonly FormCanvasControl _canvas;

        public RenameOnF2Command(FormCanvasControl canvas) => _canvas = canvas;

        // KeyBinding asks CanExecute at the moment of the key press, so there is nothing to raise.
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) =>
            _canvas.Document != null &&
            _canvas.SelectedControl is { } control &&
            FormCanvasTransform.IsRenamableItem(control) &&
            _canvas.EditItemCommand?.CanExecute(control) == true;

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                _canvas.OfferRename(_canvas.SelectedControl!);
            }
        }
    }

    /// <summary>
    /// The one mapping, shared by <see cref="Render"/> and <see cref="OnPointerPressed"/>.
    ///
    /// <para>⛔⛔ Never re-derive this inline. <c>MinimapControl</c> hand-duplicates its own
    /// transform at three sites and the copies drifted; the symptom there — and here — is that the
    /// picture looks right and the clicks land somewhere else.</para>
    /// </summary>
    private FormCanvasTransform _transform = new();

    /// <summary>Where a rubber-band drag started, in CANVAS units, or null when none is running.</summary>
    private Point? _marqueeOrigin;

    private Point _marqueeCurrent;

    /// <summary>
    /// A control clicked while it was already part of a multi-selection.
    ///
    /// <para>⚠ The selection collapses to it on RELEASE, and only if the pointer never moved — so
    /// clicking one control of a group and dragging moves the GROUP, while clicking and letting go
    /// picks that one out of it. Collapsing on press instead makes a multi-selection impossible to
    /// drag, because the press that begins the drag destroys it.</para>
    /// </summary>
    private FormControl? _collapseTo;

    /// <summary>
    /// Every selected control's geometry as the drag began, keyed by control.
    ///
    /// <para>⛔ Re-derived from these and the TOTAL delta on every move, never nudged frame by
    /// frame — the same rule the single-control drag follows. Nudging compounds each clamp, so
    /// dragging a group into an edge and back leaves it displaced from the pointer by however much
    /// the edge held it.</para>
    /// </summary>
    private readonly Dictionary<FormControl, (int X, int Y, int Width, int Height)> _dragStarts = new();

    // ⛔ No _cachedBitmap / _bitmapDirty here, deliberately. MinimapControl carries both plus two
    // companions and caches NOTHING: the bitmap is never created and the dirty flag is never read.
    // Copying that pattern would import four dead fields and the appearance of an optimisation.

    // ==================================================================
    // Attach / detach — READ THIS BEFORE ADDING A SUBSCRIPTION
    // ==================================================================
    //
    // ⛔⛔ This control deliberately holds NO event subscriptions, timers or external handlers.
    // State arrives through styled properties, redraws come from AffectsRender, and input comes
    // from an override — none of which needs tearing down. So there is no OnAttachedToVisualTree
    // override here, because an empty one that calls an empty Subscribe() is the dead scaffolding
    // this file already refuses to copy from MinimapControl.
    //
    // ⛔ THE MOMENT YOU ADD ONE, add the guard with it: Dock re-attaches the SAME control instance
    // on a tab drag, a float/re-dock and a panel maximize (CodeEditorControl.axaml.cs:762-767), and
    // OnDetachedFromVisualTree is not guaranteed to have run in between. So attach must
    // DETACH FIRST — unsubscribe, then subscribe — and OnDetachedFromVisualTree must unsubscribe.
    // Without that you get one extra handler per gesture, and a single click eventually applies an
    // edit two or three times. The only visible symptom is an undo stack that needs several
    // presses to undo one action, which nobody reads as a subscription leak.

    // ==================================================================
    // Input
    // ==================================================================

    /// <summary>
    /// The keyboard half of the designer, which did not exist: a control could be dragged and could
    /// not be DELETED, and nothing could be nudged a pixel.
    ///
    /// <para>Visual Studio's bindings, because they are the ones in muscle memory: arrows move by
    /// one, <b>Ctrl</b>+arrows move by a grid step, <b>Shift</b>+arrows resize, Delete removes.</para>
    ///
    /// <para>⚠ Web controls are moved by CELL, so a one-pixel nudge means nothing to them — arrows
    /// step them a whole cell instead, and Shift does not resize what has no size of its own.</para>
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var document = Document;
        if (document == null)
        {
            return;
        }

        // ⚠ Esc clears, and is handled even with nothing selected — otherwise the key falls through
        // to the editor underneath the design overlay, where it means something else entirely.
        if (e.Key == Key.Escape)
        {
            if (_marqueeOrigin != null)
            {
                _marqueeOrigin = null;
                InvalidateVisual();
            }
            else
            {
                Selection?.Clear();
                SelectedControl = null;
            }

            e.Handled = true;
            return;
        }

        // ⚠ Before the SelectedControl guard: PASTE is the one clipboard gesture that is meaningful
        // with nothing selected, and guarding it behind a selection would make Ctrl+V dead on an
        // empty form — exactly when someone is most likely to use it.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var clipboard = e.Key switch
            {
                Key.C => CopyCommand,
                Key.X => CutCommand,
                Key.V => PasteCommand,
                _ => null
            };

            if (clipboard?.CanExecute(null) == true)
            {
                clipboard.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (SelectedControl is not { } control)
        {
            return;
        }

        // ⛔ F2 is NOT handled here — see RenameOnF2Command and the KeyBinding in the constructor.
        // A window-level F2 binding runs before OnKeyDown, so an arm here was dead in the IDE. An
        // F2 that reaches this point is one the designer declined; leave it unhandled.
        if (e.Key == Key.F2)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            var command = DeleteCommand;
            if (command?.CanExecute(control) == true)
            {
                command.Execute(control);
                e.Handled = true;
            }

            return;
        }

        var (dx, dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0)
        };

        if (dx == 0 && dy == 0)
        {
            return;
        }

        var resize = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var coarse = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var step = coarse ? (int)GridStep : 1;

        bool changed;
        switch (control.Geometry)
        {
            // One CELL per press; Shift does nothing, because a cell has no size of its own to grow.
            case GridGeometry grid when !resize:
            {
                var col = Math.Max(0, grid.Col + dx);
                var row = Math.Max(0, grid.Row + dy);
                changed = col != grid.Col || row != grid.Row;
                grid.Col = col;
                grid.Row = row;
                break;
            }

            // ⚠ MoveTo, not MoveToForm. A nudge is parent-relative and must NOT re-parent: arrowing
            // a control one pixel past a Panel's edge should move it one pixel, not move it into
            // the Panel — a drag says where the pointer is, a keypress says how far.
            case PixelGeometry pixel:
                changed = resize
                    ? FormGeometryEdit.Resize(
                        document, control, FormResizeHandle.BottomRight, dx * step, dy * step)
                    : FormGeometryEdit.MoveTo(
                        document, control, pixel.X + (dx * step), pixel.Y + (dy * step));
                break;

            default:
                changed = false;
                break;
        }

        if (changed)
        {
            InvalidateVisual();

            // ⚠ Committed per KEYPRESS, unlike a drag which commits once on release. A keypress is
            // already a discrete edit — there is no "still holding it" state to wait for, and not
            // committing would leave the file behind the canvas until the user happened to drag
            // something.
            var commit = CommitGeometryCommand;
            if (commit?.CanExecute(null) == true)
            {
                commit.Execute(null);
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// VS's most-used designer gesture: double-click a control, land in its handler.
    ///
    /// <para>⚠ Hit-tested afresh rather than trusting <see cref="SelectedControl"/>. The first click
    /// of the pair does set the selection, but a double-click that began on the background would
    /// otherwise open a handler for whatever was selected before — on a control the user is not even
    /// pointing at.</para>
    /// </summary>
    private void OnCanvasDoubleTapped(object? sender, TappedEventArgs e)
    {
        var document = Document;
        if (document == null)
        {
            return;
        }

        // ⚠ The CURRENT selection goes in, exactly as OnPointerPressed's hit test does: a dropdown
        // exists in the layout only for the selection that opened it, and the first click of the
        // pair is what opened it. Without it this can never find a nested item and "double-click a
        // menu item to reach its handler" is dead for everything except a top-level one.
        var control = _transform.HitTest(document, e.GetPosition(this), SelectedControl);
        if (control == null)
        {
            return;
        }

        SelectedControl = control;

        // A double-click is its own gesture, not a selecting press: the click after it starts
        // afresh rather than reading as the "second click" of a rename.
        _renameArmedFor = null;

        // ⛔ The FIRST press of this pair landed on an already-selected item and offered a rename
        // (OfferRename). A double-click opens the handler and must not leave that edit open behind
        // it, so the rename is WITHDRAWN here — through the TwoWay binding, whose view-model side
        // closes the editor. (When the overlay has already covered the cell, the second press lands
        // on IT instead; FormTypeHereEditor forwards the double-click for the same reason.)
        if (EditingItem != null)
        {
            EditingItem = null;
        }

        var command = ActivateControlCommand;
        if (command?.CanExecute(control) == true)
        {
            command.Execute(control);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Offers an in-place rename of <paramref name="item"/>: marks it <see cref="EditingItem"/> and
    /// runs <see cref="EditItemCommand"/>. Returns whether the command ran.
    ///
    /// <para>⛔ Keeping this apart from a DOUBLE-click: the rename is offered on the FIRST press of a
    /// pair (<c>ClickCount == 1</c> on an already-selected item) — immediately, not after a
    /// double-click-time delay, because a delay makes the gesture feel dead and cannot be driven by a
    /// synchronous test. The SECOND press of a double-click has <c>ClickCount == 2</c>, never offers
    /// a rename, and <c>OnCanvasDoubleTapped</c> WITHDRAWS the one the first press offered before it
    /// opens the handler. So a double-click always ends with the handler open and no edit.</para>
    ///
    /// <para>⚠ EditingItem is written BEFORE the command runs, through its TwoWay binding: the view
    /// model's <c>BeginEditItem</c> then finds its target already named, and withdraws it if it
    /// refuses. The canvas asks <see cref="FormCanvasTransform.IsRenamableItem"/> first, the same
    /// question the view model asks, so a refusal is not expected.</para>
    /// </summary>
    private bool OfferRename(FormControl item)
    {
        var command = EditItemCommand;
        if (command?.CanExecute(item) != true)
        {
            return false;
        }

        EditingItem = item;
        command.Execute(item);
        return true;
    }

    /// <summary>
    /// What a click does to the selection.
    ///
    /// <para>⛔ Clicking a control that is ALREADY part of a multi-selection must not collapse the
    /// selection to it — that is how a drag of three controls becomes a drag of one, and the user
    /// cannot move a group at all. The selection collapses on RELEASE instead, and only if the
    /// pointer never moved.</para>
    /// </summary>
    private void ApplyClickSelection(FormControl? hit, bool extend)
    {
        var selection = Selection;
        if (selection == null)
        {
            SelectedControl = hit;
            return;
        }

        if (hit == null)
        {
            selection.Clear();
            return;
        }

        if (extend)
        {
            selection.Toggle(hit);
        }
        else if (!selection.Contains(hit))
        {
            selection.Set(hit);
        }
        else
        {
            // Already selected: keep the group, but make this the primary so align and size use the
            // control the user just pointed at — which is what VS does.
            _collapseTo = selection.Controls.Count > 1 ? hit : null;
        }

        SelectedControl = selection.Primary;
    }

    private void BeginMarquee(Point point)
    {
        _marqueeOrigin = point;
        _marqueeCurrent = point;
        Focus();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var document = Document;
        if (document == null)
        {
            return;
        }

        var point = e.GetPosition(this);

        // Take focus so the design view's key bindings (Ctrl+Z, Ctrl+Y) are on this element's
        // route rather than the hidden editor's.
        Focus();

        // ⛔ Every press CONSUMES the rename arming — a right press, a Type Here press, a handle or
        // grip press, a background press. Only the ordinary item press below re-arms it, so "the
        // PREVIOUS press landed on this item" is literally what the second-click test reads.
        var renameArmedFor = _renameArmedFor;
        _renameArmedFor = null;

        // ⚠ Left button only. Arming a drag on a right-click means the context menu gesture also
        // moves the control the user was about to right-click.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            SelectedControl = _transform.HitTest(document, point, SelectedControl);
            e.Handled = true;
            return;
        }

        // ⛔⛔ The Type Here slot FIRST — ahead of the handle test, the form grips, the ordinary hit
        // test and the marquee branch. Two separate ways a later check goes wrong, both silent:
        //
        // • This is STRICTER than the spec's "before the marquee branch", deliberately. A slot sits
        //   flush against the right edge of the last cell in its band, so when that cell is the
        //   SELECTION its right-hand handle straddles the shared edge and reaches HandleReach px
        //   INTO the slot. A handle test first would swallow a click 1–4 px inside the slot and arm
        //   a resize on a control that has no geometry to resize — the slot would be unreachable
        //   along the one edge the user aims at coming off the last menu.
        // • A late check fails in a way that looks like nothing at all. A BAND slot's rectangle lies
        //   entirely INSIDE the band, so HitTest resolves the point to the STRIP and the press is
        //   merely a selection: BeginTypeHereCommand never fires, nothing throws, and the whole
        //   symptom is that clicking "Type Here" selects the menu bar.
        var slotHost = _transform.TypeHereAt(document, point, SelectedControl);
        if (slotHost != null)
        {
            BeginTypeHereCommand?.Execute(slotHost);
            e.Handled = true;
            return;
        }

        // A handle of the CURRENT selection wins over what is underneath it. Handles straddle the
        // border, so the outer half of one sits over whatever is behind the control — hit-testing
        // first would make every handle on a control's outer edge unusable.
        var handle = HandleUnder(document, point);

        // ⚠ AFTER the control's handles, before selection. A control sitting against the form's
        // right edge puts its own grips on top of the form's; the control's win, because that is
        // what the user was looking at when they selected it. Pixel forms only — a web page has no
        // client size to drag.
        if (handle == FormResizeHandle.None && document.Target == FormTarget.WinForms)
        {
            var grip = FormGripAt(SurfaceCanvasRect(document), point);
            if (grip != FormResizeHandle.None)
            {
                _formGrip = grip;
                _dragOrigin = point;
                _formStart = (
                    (int)FormCanvasTransform.SurfaceSize(document).Width,
                    (int)FormCanvasTransform.SurfaceSize(document).Height);
                _dragChanged = false;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        if (handle == FormResizeHandle.None)
        {
            // The SAME transform the last Render used, so selection cannot disagree with the
            // picture — and the SAME selection, so it cannot disagree with which dropdowns are
            // open either. Layout yields a dropdown's rows only for the selection that opened it,
            // so without SelectedControl here a press on a menu the canvas is plainly showing hits
            // NOTHING, starts a rubber band, and the empty-band release clears the selection
            // outright: the open menu vanishes as you click on it.
            var hit = _transform.HitTest(document, point, SelectedControl);
            var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
                         e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (hit == null && !extend)
            {
                // ⚠ Empty background with no modifier starts a RUBBER BAND rather than clearing
                // immediately. Clearing happens on release if the band caught nothing, so a click
                // that turns out to be a drag does not flash the selection away first.
                BeginMarquee(point);
                e.Handled = true;
                return;
            }

            // VS's rename gesture: a SECOND single click on the item that is already the whole
            // selection. Decided BEFORE ApplyClickSelection, which is what makes the first click's
            // target the selection. ClickCount == 1 is what keeps it apart from a double-click —
            // see OfferRename.
            // ⛔⛔ AND the user's PREVIOUS press must have landed on this same item (renameArmedFor).
            // Being selected is not enough: a Type Here commit selects the new item before any
            // click, and without this the first click on it opened a rename — see _renameArmedFor.
            var secondClick = !extend && e.ClickCount == 1 && hit != null &&
                              ReferenceEquals(hit, renameArmedFor) &&
                              ReferenceEquals(hit, SelectedControl) &&
                              (Selection == null || Selection.Controls.Count <= 1) &&
                              FormCanvasTransform.IsRenamableItem(hit);

            _pressIsSelecting = true;
            try
            {
                ApplyClickSelection(hit, extend);
            }
            finally
            {
                _pressIsSelecting = false;
            }

            var renameOffered = secondClick && OfferRename(hit!);

            // Arm the NEXT press: this one landed on an item and left it as the sole selection.
            // Checked AFTER OfferRename, so a view model that moved the selection in answer to it
            // (which disarms through OnPropertyChanged) is read as it now stands.
            // ⛔⛔ But NOT when this press OPENED a rename — like a double-click, a rename is its own
            // gesture and the click after it starts afresh. Armed, the first click after committing
            // a rename re-opened the rename box at once over the (still selected) item, the user's
            // intended second click then landed IN that box and dropped the caret mid-caption,
            // un-selecting the pre-filled text: "&Save" renamed to "&Sa&SaveAgainve". Invisible until
            // the overlay actually sat over the cell (see PublishTypeHereBoundsFromRender); pinned
            // by FormDesignerRealViewTests.RenamingTheSameItemTwiceThenADifferentItem_ViaSecondClick.
            if (!renameOffered && !extend && hit != null &&
                ReferenceEquals(hit, SelectedControl) &&
                (Selection == null || Selection.Controls.Count <= 1))
            {
                _renameArmedFor = hit;
            }
        }

        // ⚠ A band, a strip's item and a dropdown row all fall through BOTH branches below with no
        // guard of their own, and that is measured rather than assumed: their Geometry is null, so
        // neither `is GridGeometry` nor `is PixelGeometry` matches, no pointer capture is taken and
        // no drag origin is recorded. HandleUnder says None for the same reason. Pressing a menu
        // item therefore selects it and nothing else — pinned by
        // FormStripCanvasTests.PressingMnuFilesCell_SelectsIt_AndArmsNoDrag, because "no drag was
        // armed" is otherwise indistinguishable from "a drag was armed and moved nothing".
        if (SelectedControl?.Geometry is GridGeometry)
        {
            // A web control has no pixel geometry to rewind to and no handles to grab: the whole
            // gesture is "which cell is the pointer over", answered afresh on every move.
            _dragHandle = FormResizeHandle.None;
            _dragOrigin = point;
            _dragStart = default;
            _dragStartForm = default;
            _dragChanged = false;
            e.Pointer.Capture(this);
        }
        else if (SelectedControl?.Geometry is PixelGeometry pixel)
        {
            _dragHandle = handle;
            _dragOrigin = point;
            _dragStart = (pixel.X, pixel.Y, pixel.Width, pixel.Height);

            // ⛔ The ABSOLUTE position too, because a move is tracked in form space so it can cross
            // a container boundary: the control's own X/Y mean something different on each side of
            // one, and a drag that re-bases them halfway through would jump.
            _dragStartForm = FormBoundsOf(document, SelectedControl)?.TopLeft
                             ?? new Point(pixel.X, pixel.Y);

            // Every OTHER selected control's starting geometry, so a group drag is re-derived from
            // the start and the total delta exactly as the primary's is.
            _dragStarts.Clear();
            foreach (var other in SelectedSet)
            {
                if (other.Geometry is PixelGeometry g)
                {
                    _dragStarts[other] = (g.X, g.Y, g.Width, g.Height);
                }
            }

            _dragChanged = false;
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Drags the selection. The geometry is recomputed from where the drag STARTED each time, never
    /// nudged by the last step.
    ///
    /// <para>⛔ Applying each frame's delta to the current geometry compounds every clamp: drag a
    /// control into the left edge, keep pulling, come back, and it has crept away from the pointer
    /// by exactly as much as the edge held it. Re-deriving from the start point and the TOTAL delta
    /// makes the control track the pointer exactly, and makes a drag that returns to where it began
    /// a genuine no-op.</para>
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var document = Document;
        if (document == null)
        {
            return;
        }

        // ⚠ BEFORE every other gesture. A rubber band has no selected control and no drag origin,
        // so any guard that checks those first would drop it on its first move.
        if (_marqueeOrigin != null)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _marqueeOrigin = null;
                InvalidateVisual();
                return;
            }

            _marqueeCurrent = e.GetPosition(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ⚠ BEFORE the SelectedControl guard below. A form resize has no selected control, so
        // checking it first would drop the gesture on its first move.
        if (_formGrip != FormResizeHandle.None && _dragOrigin != null)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _formGrip = FormResizeHandle.None;
                _dragOrigin = null;
                _dragChanged = false;
                return;
            }

            var moved = _transform.ToForm(e.GetPosition(this)) - _transform.ToForm(_dragOrigin.Value);

            // Re-derived from the START size and the TOTAL delta, never nudged by the last frame —
            // the same rule the control drag follows, and for the same reason: clamping at the
            // minimum would otherwise compound and the edge would creep away from the pointer.
            var width = _formGrip == FormResizeHandle.Bottom
                ? _formStart.Width
                : Math.Max(MinimumFormSide, _formStart.Width + (int)Math.Round(moved.X));
            var height = _formGrip == FormResizeHandle.Right
                ? _formStart.Height
                : Math.Max(MinimumFormSide, _formStart.Height + (int)Math.Round(moved.Y));

            if (width != document.Width || height != document.Height)
            {
                document.Width = width;
                document.Height = height;
                _dragChanged = true;
                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        if (_dragOrigin == null || SelectedControl is not { } control)
        {
            UpdateCursor(document, e.GetPosition(this));
            return;
        }

        // ⚠ Capture can be lost without a release — the window deactivates, another control grabs
        // the pointer, the platform cancels the gesture. Without this the canvas keeps "dragging"
        // on every subsequent mouse move with no button held: the control follows the pointer
        // around the screen and only stops when the user clicks, which reads as the IDE going mad.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragOrigin = null;
            _dragHandle = FormResizeHandle.None;
            _dragChanged = false;
            return;
        }

        var start = _dragStart;
        var total = _transform.ToForm(e.GetPosition(this)) - _transform.ToForm(_dragOrigin.Value);
        var dx = (int)Math.Round(total.X);
        var dy = (int)Math.Round(total.Y);

        bool changed;
        if (control.Geometry is GridGeometry)
        {
            // ⛔ The pointer's own position, NOT the drag-start origin plus a delta. See
            // FormGeometryEdit.MoveToCell: on a grid you point at the cell you want.
            var pointer = _transform.ToForm(e.GetPosition(this));
            changed = FormGeometryEdit.MoveToCell(
                document, control, (int)Math.Round(pointer.X), (int)Math.Round(pointer.Y));
        }
        else if (_dragHandle == FormResizeHandle.None)
        {
            // Absolute, so the control re-parents when the pointer crosses into or out of a Panel.
            // ⚠ Snapped on the RESULT, not on the delta: snapping the delta would carry the
            // control's original off-grid offset forward for ever, so a control that started at 13
            // would land on 21 and never on 16.
            changed = FormGeometryEdit.MoveToForm(
                document, control,
                Snap(_dragStartForm.X + dx, e.KeyModifiers),
                Snap(_dragStartForm.Y + dy, e.KeyModifiers));

            // ⛔ The REST of a multi-selection moves by the same delta, and NOT through MoveToForm.
            // The primary re-parents when the pointer crosses a Panel boundary because the pointer
            // is over that Panel; the others are somewhere else entirely, and re-parenting each of
            // them to whatever happens to be under its own new position would scatter a group drag
            // across containers the user never pointed at.
            foreach (var other in SelectedSet)
            {
                if (ReferenceEquals(other, control) ||
                    other.Geometry is not PixelGeometry geometry ||
                    !_dragStarts.TryGetValue(other, out var from))
                {
                    continue;
                }

                var x = Snap(from.X + dx, e.KeyModifiers);
                var y = Snap(from.Y + dy, e.KeyModifiers);
                if (x == geometry.X && y == geometry.Y)
                {
                    continue;
                }

                changed |= FormGeometryEdit.MoveTo(document, other, x, y);
            }
        }
        else
        {
            // Rewind to the geometry the drag began with, then apply the whole delta once.
            if (control.Geometry is PixelGeometry pixel)
            {
                (pixel.X, pixel.Y, pixel.Width, pixel.Height) = start;
            }

            changed = FormGeometryEdit.Resize(document, control, _dragHandle, dx, dy);
        }

        if (changed)
        {
            _dragChanged = true;

            // The model was mutated in place behind an unchanged Document reference, so nothing
            // invalidates on its own — the same reason ModelRevision exists for the property grid.
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // A rubber band selects on release rather than live, so a band dragged across the form does
        // not thrash the property grid through every control it passes over.
        if (_marqueeOrigin is { } origin)
        {
            var band = new Rect(origin, _marqueeCurrent);
            _marqueeOrigin = null;

            if (Document is { } doc)
            {
                Selection?.SetRange(FormCanvasTransform.ControlsIn(doc, _transform.ToForm(band)));
                SelectedControl = Selection?.Primary;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var wasDragging = _dragOrigin != null;
        _dragOrigin = null;
        _dragHandle = FormResizeHandle.None;
        _formGrip = FormResizeHandle.None;
        e.Pointer.Capture(null);

        // Clicking one control of a multi-selection WITHOUT dragging picks it out of the group.
        // Deferred to here precisely so the same click could have started a group drag instead.
        if (_collapseTo is { } single)
        {
            _collapseTo = null;
            if (!_dragChanged)
            {
                Selection?.Set(single);
                SelectedControl = single;
            }
        }

        if (!wasDragging || !_dragChanged)
        {
            return;
        }

        _dragChanged = false;

        var command = CommitGeometryCommand;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>
    /// The pointer is the only thing telling the user a control can be resized at all — there is no
    /// tooltip and no status bar for it. A wrong cursor here is a feature nobody discovers.
    /// </summary>
    private void UpdateCursor(FormDocument document, Point point)
    {
        var handle = HandleUnder(document, point);

        // ⛔ Cached and assigned only on CHANGE. A Cursor wraps a platform handle, and this runs on
        // every pointer move across the canvas — building a new one each time allocates a native
        // object per mouse event and churns the cursor while the user is trying to aim at a handle
        // four pixels wide.
        var cursor = handle switch
        {
            FormResizeHandle.Left or FormResizeHandle.Right => WestEastCursor,
            FormResizeHandle.Top or FormResizeHandle.Bottom => NorthSouthCursor,
            FormResizeHandle.None => ArrowCursor,
            _ => SizeAllCursor
        };

        if (!ReferenceEquals(Cursor, cursor))
        {
            Cursor = cursor;
        }
    }

    /// <summary>
    /// The handle of the current selection under a canvas point.
    ///
    /// <para>⚠ Returns None when the selection has no drawn rectangle — a web control, say. Testing
    /// against a default <c>Rect</c> instead would put a phantom set of handles at the canvas
    /// origin, where a click would arm a resize on something that has no pixel geometry AND skip
    /// the hit-test that should have changed the selection.</para>
    /// </summary>
    private FormResizeHandle HandleUnder(FormDocument document, Point point)
    {
        // ⚠ Pixel geometry only. A web control's size IS its cell, so there is no edge to drag —
        // and since the canvas now lays web controls out, CanvasBoundsOf returns a rectangle for
        // them too. Without this guard a click near a cell edge would arm a resize that can never
        // do anything AND swallow the move the user was starting.
        if (SelectedControl?.Geometry is not PixelGeometry ||
            CanvasBoundsOf(document, SelectedControl) is not { } bounds)
        {
            return FormResizeHandle.None;
        }

        return FormCanvasTransform.HandleAt(bounds, point);
    }

    /// <summary>The form's client rectangle in CANVAS space.</summary>
    private Rect SurfaceCanvasRect(FormDocument document)
    {
        var size = FormCanvasTransform.SurfaceSize(document);
        return _transform.ToCanvas(new Rect(0, 0, size.Width, size.Height));
    }

    /// <summary>Centre of one form grip, in canvas space.</summary>
    private static Point FormGripCentre(Rect surface, FormResizeHandle grip) => grip switch
    {
        FormResizeHandle.Right => new Point(surface.Right, surface.Center.Y),
        FormResizeHandle.Bottom => new Point(surface.Center.X, surface.Bottom),
        _ => surface.BottomRight
    };

    /// <summary>
    /// Which form grip the pointer is over, or None.
    ///
    /// <para>⚠ The corner is tested FIRST. It overlaps both edge grips, and a corner drag is the one
    /// the user meant if they aimed at the corner — losing it to the edge underneath would make the
    /// only two-axis resize unreachable.</para>
    /// </summary>
    private static FormResizeHandle FormGripAt(Rect surface, Point point)
    {
        var reach = FormCanvasTransform.HandleReach;

        foreach (var grip in new[]
                 { FormResizeHandle.BottomRight, FormResizeHandle.Right, FormResizeHandle.Bottom })
        {
            var centre = FormGripCentre(surface, grip);
            if (Math.Abs(point.X - centre.X) <= reach && Math.Abs(point.Y - centre.Y) <= reach)
            {
                return grip;
            }
        }

        return FormResizeHandle.None;
    }

    /// <summary>A control's rectangle in CANVAS space, or null when it has no pixel geometry.</summary>
    private Rect? CanvasBoundsOf(FormDocument document, FormControl control) =>
        FormBoundsOf(document, control) is { } bounds ? _transform.ToCanvas(bounds) : null;

    /// <summary>
    /// A control's ABSOLUTE rectangle in form space — its own coordinates plus every container
    /// origin above it. <c>Layout</c> already walks that chain, so this asks it rather than adding
    /// a second accumulation that could drift from the one the canvas draws with.
    ///
    /// <para>⛔ It asks under the CURRENT <see cref="SelectedControl"/> — the very picture
    /// <see cref="Render"/> paints and <c>HitTest</c> resolves against — which is the whole reason
    /// this stopped being static when Task 20 made <c>Layout</c> selection-dependent. A dropdown's
    /// rows exist in the layout only for the selection that opens that dropdown, so a call with no
    /// selection answers "no bounds" for every row of the menu the canvas is currently showing, and
    /// this one control would be holding two different pictures at once. The visible cost today is
    /// small and real — the secondary outline of a multi-selected dropdown item would simply not be
    /// drawn, with nothing failing — and it is the same drift <see cref="FormCanvasTransform"/>
    /// exists to prevent, so it is not left for a later caller to rediscover.</para>
    ///
    /// <para>⚠ Nothing else changes: <c>Layout</c> yields positioned controls first and bands after
    /// them, so a positioned control's and a band cell's first match are exactly what they were.</para>
    /// </summary>
    private Rect? FormBoundsOf(FormDocument document, FormControl control)
    {
        foreach (var entry in FormCanvasTransform.Layout(document, SelectedControl))
        {
            if (ReferenceEquals(entry.Control, control))
            {
                return entry.Bounds;
            }
        }

        return null;
    }

    private Point? _dragOrigin;
    private Point _dragStartForm;
    private FormResizeHandle _dragHandle;
    private (int X, int Y, int Width, int Height) _dragStart;
    private bool _dragChanged;

    /// <summary>
    /// Which edge of the FORM is being dragged, or None. Separate from <see cref="_dragHandle"/>,
    /// which is a control's.
    /// </summary>
    private FormResizeHandle _formGrip;

    private (int Width, int Height) _formStart;

    /// <summary>
    /// ⛔ A form is anchored at its top-left, so only the three grips that grow it are drawn — east,
    /// south, south-east. VB6 does the same, and for the same reason: dragging the top edge would
    /// have to move the form's origin, which does not exist. Grips that cannot do anything are worse
    /// than no grips (see the title-bar buttons, which are decoration and say so).
    /// </summary>
    private static readonly FormResizeHandle[] FormGrips =
    {
        FormResizeHandle.Right, FormResizeHandle.Bottom, FormResizeHandle.BottomRight
    };

    /// <summary>Smallest form the grips will produce. Below this the title bar has nowhere to go.</summary>
    private const int MinimumFormSide = 48;

    /// <summary>
    /// Shows the "you can drop here" cursor only where a drop would actually do something.
    ///
    /// <para>⚠ Without this, Avalonia's default is NO drop effect and the drag is refused with no
    /// explanation — the user drags a Button across the canvas, the cursor says no, and there is
    /// nothing to read. The inverse is worse: advertising a drop on a document that will refuse it.
    /// So the answer is the same one the drop itself will give.</para>
    /// </summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CanAccept(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!CanAccept(e))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var kind = (string)e.Data.Get(ControlKindFormat)!;

        // ⛔ Through the SAME transform that rendered, so the control lands under the pointer at
        // any zoom or pan. Re-deriving the mapping here is the drift this type exists to prevent.
        // ⚠ Snapped like a drag, so a dropped control lands on the same grid a dragged one does —
        // otherwise every control arrives off-grid and has to be nudged before it lines up.
        var point = _transform.ToForm(e.GetPosition(this));
        var request = new FormControlDropRequest(
            kind, Snap(point.X, e.KeyModifiers), Snap(point.Y, e.KeyModifiers));

        var command = DropCommand;
        if (command?.CanExecute(request) == true)
        {
            command.Execute(request);
        }

        e.DragEffects = DragDropEffects.Copy;
    }

    private bool CanAccept(DragEventArgs e) =>
        Document != null &&
        DropCommand != null &&
        e.Data.Contains(ControlKindFormat) &&
        e.Data.Get(ControlKindFormat) is string kind &&
        !string.IsNullOrEmpty(kind);

    // ==================================================================
    // Render
    // ==================================================================

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // ⛔ The whole viewport, transparent, FIRST — and it is input, not decoration. A Control
        // that draws nothing at a point is not hit-testable there, so without this the canvas
        // receives pointer and drop events only over the rectangles it painted: dropping a Button
        // on the empty margin around the form would do nothing at all, and the failure would look
        // exactly like a drag-and-drop that was never wired up. Costs no pixels.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        // The schematic probe (see RenderSchematicForTest): ONE shape, at the bounds asked for, with
        // no document at all. Honoured only when non-null, so a canvas that was never handed one
        // behaves exactly as before — and the probe still reaches the shape through the seam
        // DrawControl uses, rather than through a copy of it that could drift.
        if (_schematicOverride is { } probe)
        {
            // ⚠ The ONLY place a caption origin is retained. It is recorded here, inside the branch
            // that is already probe-only, rather than beside the DrawText call — so the live draw
            // path below stays free of it.
            _probeResult = new SchematicProbeResult(DrawSchematic(
                context, probe.Schematic, probe.Bounds, probe.Label, SurfaceBrush, WindowBrush, LabelBrush)?.Origin);
            return;
        }

        var document = Document;
        if (document == null)
        {
            return;
        }

        _transform = Fit(document, Bounds.Size);

        DrawSurface(context, document);

        // ⛔⛔ ONE answer to "is this the slot being edited", asked by BOTH the highlight below and
        // the TypeHereBounds published at the end of this method.
        //
        // ⛔ It is the entry's HOST, never its Control — and the plan text spells it both ways in
        // two consecutive sentences. `Control == TypeHereHost` is a DEFECT: a slot entry is always
        // (Control: null, Bounds, Role: TypeHere, Host: host), so that comparison is true exactly
        // when TypeHereHost is NULL. The highlight would appear on every slot while nothing was
        // being edited and vanish the moment something was — and the bounds, spelled the other way
        // one sentence later, would disagree with the pixels. One predicate, so they cannot.
        //
        // ⛔ While an item is being RENAMED there is no edited slot at all: TypeHereHost is then the
        // item itself, which hosts its own open dropdown's slot — matching on it would highlight
        // that slot and publish ITS rectangle, parking the overlay a row below the caption being
        // edited.
        // The predicate itself lives in EditedRectOf, shared with ComputeTypeHereBounds — the
        // eager, outside-the-render copy of this same walk. See PublishTypeHereBoundsFromRender.
        var renaming = EditingItem;
        var typeHereHost = TypeHereHost;

        // The canvas rectangle of the slot TypeHereHost names, or default when it names none —
        // captured here and published at the very END of this method. See the assignment there.
        var editedSlot = default(Rect);

        // ⚠ Containers before their children — Layout guarantees that order, and drawing a
        // container after its children would paint over them.
        foreach (var entry in FormCanvasTransform.Layout(document, SelectedControl))
        {
            var bounds = _transform.ToCanvas(entry.Bounds);
            var editedRect = EditedRectOf(entry, bounds, typeHereHost, renaming, _transform.Zoom);
            if (editedRect is { } rect)
            {
                editedSlot = rect;
            }

            if (entry.Role == FormLayoutRole.TypeHere)
            {
                var edited = editedRect != null;

                var slotCaption = DrawTypeHereSlot(context, bounds, edited, entry.Host, _transform.Zoom);
                _captionLog?.Add(new CaptionRecord(null, entry.Host, bounds, FormCanvasTransform.TypeHereCaption, slotCaption));
                continue;
            }

            // ⛔ Band, Control and Cell alike go through DrawControl, which is the ONE place a
            // control's shape is decided — and it decides it from the CATALOG ROW
            // (FormControlCatalog.Find(kind)?.Schematic), never from a switch over control.Kind.
            //
            // ⚠ The plan asks for a Cell to call DrawSchematic DIRECTLY with "the item schematic".
            // Taken literally that needs a second copy of DrawControl's label rule (Text ?? Id) and
            // its BackColor/ForeColor lookup right here — a second list of the same answers — and
            // the first thing it costs is the caption: a menu item would draw as an empty cell
            // where "&File" should be. Same catalog lookup, one copy of it.
            if (entry.Control != null)
            {
                var drawn = DrawControl(context, entry.Control, bounds);
                _captionLog?.Add(new CaptionRecord(entry.Control, null, bounds, CaptionForTest(entry.Control), drawn));
            }
        }

        // Handles LAST, over everything. Drawn in the loop they would be painted over by the next
        // control, so the selection's handles would disappear behind whatever overlaps it — which
        // is exactly when you most need to grab them.
        // ⚠ Handles only where they DO something. A web control lives in a grid cell — its size is
        // the cell's, so there is nothing to drag an edge of, and OnPointerPressed will not arm a
        // resize for it. Drawing eight grips on it would advertise a gesture that silently does
        // nothing, which is worse than drawing none.
        // ⚠ Every SECONDARY member of a multi-selection gets an outline but no handles — VS's
        // convention, and an honest one: only the primary can be resized by dragging, so only the
        // primary advertises grips. Outlining them all is what tells the user a group command will
        // affect more than the one control they can see handles on.
        foreach (var member in SelectedSet)
        {
            if (!ReferenceEquals(member, SelectedControl) &&
                CanvasBoundsOf(document, member) is { } outline)
            {
                context.DrawRectangle(null, SecondarySelectionPen, outline);
            }
        }

        if (SelectedControl?.Geometry is PixelGeometry &&
            CanvasBoundsOf(document, SelectedControl) is { } selection)
        {
            DrawHandles(context, selection);
        }

        // The rubber band, over everything — it is transient and must never be hidden behind a
        // control it is being dragged across.
        if (_marqueeOrigin is { } bandOrigin)
        {
            var band = new Rect(bandOrigin, _marqueeCurrent);
            context.DrawRectangle(MarqueeBrush, MarqueePen, band);
        }

        // The form's own grips, last of all. Unlike the title-bar buttons these are REAL: they
        // resize the form, so drawing them is a promise the canvas keeps.
        if (document.Target == FormTarget.WinForms)
        {
            var surface = SurfaceCanvasRect(document);
            var reach = FormCanvasTransform.HandleReach;
            foreach (var grip in FormGrips)
            {
                var centre = FormGripCentre(surface, grip);
                context.DrawRectangle(HandleBrush, HandlePen, new Rect(
                    centre.X - reach, centre.Y - reach, reach * 2, reach * 2));
            }
        }

        // ⚠ Published UNCONDITIONALLY, including the `default` case. Clearing TypeHereHost — or
        // selecting something that makes that slot disappear from the layout altogether — must move
        // the editor off it, not leave it parked over a rectangle that is no longer on screen.
        // ⛔⛔ But never WRITTEN from here when it changes — see PublishTypeHereBoundsFromRender.
        PublishTypeHereBoundsFromRender(editedSlot);
    }

    /// <summary>
    /// The rectangle <paramref name="entry"/> contributes to <see cref="TypeHereBounds"/>, or null.
    /// The ONE predicate for "is this what the overlay sits on", asked by Render (for the slot
    /// highlight and the published rectangle) and by <see cref="ComputeTypeHereBounds"/>.
    ///
    /// <para>⛔ A slot matches by its HOST, never its Control (always null on a slot) — see the
    /// comment in <c>Render</c>. And while an item is being RENAMED no slot matches at all: the
    /// rectangle is the item's OWN cell, at the height the overlay recovers its zoom from
    /// (<see cref="FormCanvasTransform.EditBoxBounds"/> states why it is not the bare cell).</para>
    /// </summary>
    private static Rect? EditedRectOf(
        FormLayoutEntry entry, Rect canvasBounds, FormControl? typeHereHost, FormControl? renaming, double zoom)
    {
        if (entry.Role == FormLayoutRole.TypeHere)
        {
            return renaming == null && typeHereHost is { } host && ReferenceEquals(entry.Host, host)
                ? canvasBounds
                : null;
        }

        if (renaming != null && entry.Control != null && entry.Role == FormLayoutRole.Cell &&
            ReferenceEquals(entry.Control, renaming))
        {
            return FormCanvasTransform.EditBoxBounds(renaming, canvasBounds, zoom);
        }

        return null;
    }

    /// <summary>
    /// What <c>Render</c> will publish as <see cref="TypeHereBounds"/> for the canvas as it stands
    /// now — the same Fit, the same Layout and the same <see cref="EditedRectOf"/> — or null when
    /// Render would publish nothing (no document, the schematic probe) or there is no size to fit
    /// into yet.
    /// </summary>
    private Rect? ComputeTypeHereBounds()
    {
        if (_schematicOverride != null || Document is not { } document ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return null;
        }

        var transform = Fit(document, Bounds.Size);
        var renaming = EditingItem;
        var typeHereHost = TypeHereHost;
        var result = default(Rect);
        foreach (var entry in FormCanvasTransform.Layout(document, SelectedControl))
        {
            if (EditedRectOf(entry, transform.ToCanvas(entry.Bounds), typeHereHost, renaming, transform.Zoom) is { } rect)
            {
                result = rect;
            }
        }

        return result;
    }

    /// <summary>The newest value of <see cref="TypeHereBounds"/>, from whichever path computed it last.</summary>
    private Rect _latestTypeHereBounds;

    /// <summary>True while a deferred <see cref="TypeHereBounds"/> write is queued (at most one).</summary>
    private bool _typeHereBoundsWritePosted;

    /// <summary>
    /// The EAGER path: recomputes and writes <see cref="TypeHereBounds"/> the moment an input to it
    /// changes, OUTSIDE any render pass — so by the time Render runs, the value it computes is the
    /// value already published and its own publish is a no-op. See
    /// <see cref="PublishTypeHereBoundsFromRender"/> for why this must exist.
    /// </summary>
    private void RefreshTypeHereBounds()
    {
        if (ComputeTypeHereBounds() is { } bounds)
        {
            _latestTypeHereBounds = bounds;
            TypeHereBounds = bounds;
        }
    }

    /// <summary>
    /// Render's publish of <see cref="TypeHereBounds"/>.
    ///
    /// <para>⛔⛔ OWNER'S REPORT (2026-09-24): "the text was typed NOT in the Type Here". The overlay
    /// TextBox sat at its default spot (editor-local 0,0, auto size) in the running IDE while
    /// <c>TypeHereBounds</c> held the right rectangle. MEASURED cause: <c>TypeHereBounds</c> used to
    /// be WRITTEN here, inside Render; the <c>SlotBounds</c> binding delivers it synchronously, so
    /// <see cref="FormTypeHereEditor"/> set the box's Padding/FontSize/Canvas.Left/Width — every one
    /// of them AffectsMeasure/Arrange — DURING THE RENDER PASS, and Avalonia throws
    /// <c>InvalidOperationException: Visual was invalidated during the render pass</c>
    /// (<c>CompositingRenderer.AddDirty</c>). The binding pipeline SWALLOWS it: no crash, no log a
    /// user sees, and the positioning never runs. Keeping <c>TypeHereBounds</c> out of
    /// <c>AffectsRender</c> only stopped THIS control re-entering; it never protected the subscriber.</para>
    ///
    /// <para>So no change is ever raised from inside Render. The eager path
    /// (<see cref="RefreshTypeHereBounds"/>, driven from <see cref="OnPropertyChanged"/> for every
    /// input — including <c>Bounds</c>) normally has the value right already, and then this is a
    /// no-op. When it is not (a render invalidated without a property change, e.g. a drag moving
    /// the strip in place), the write is POSTED to run after the render pass instead — one frame
    /// late, never dropped, never inside the render.</para>
    /// </summary>
    private void PublishTypeHereBoundsFromRender(Rect bounds)
    {
        _latestTypeHereBounds = bounds;
        if (bounds == TypeHereBounds || _typeHereBoundsWritePosted)
        {
            return;
        }

        _typeHereBoundsWritePosted = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _typeHereBoundsWritePosted = false;
            TypeHereBounds = _latestTypeHereBounds;
        });
    }

    /// <summary>
    /// The eight grab points, drawn at <see cref="FormCanvasTransform.HandleReach"/> so what the
    /// user aims at is what <c>HandleAt</c> tests. A handle drawn larger than its hit area is a
    /// control that ignores clicks near its own corner; drawn smaller, it grabs when the user meant
    /// to select.
    /// </summary>
    private static void DrawHandles(DrawingContext context, Rect bounds)
    {
        var reach = FormCanvasTransform.HandleReach;

        foreach (var centre in new[]
        {
            bounds.TopLeft, new Point(bounds.Center.X, bounds.Y), bounds.TopRight,
            new Point(bounds.Right, bounds.Center.Y), bounds.BottomRight,
            new Point(bounds.Center.X, bounds.Bottom), bounds.BottomLeft,
            new Point(bounds.X, bounds.Center.Y)
        })
        {
            context.DrawRectangle(
                HandleBrush,
                HandlePen,
                new Rect(centre.X - reach, centre.Y - reach, reach * 2, reach * 2));
        }
    }

    /// <summary>
    /// Centres the form in the viewport at a zoom that fits, never enlarging past 1:1.
    ///
    /// <para>⚠ Recomputed per render rather than stored, because it depends on
    /// <see cref="Visual.Bounds"/>, which changes on every resize and dock move. A stored fit goes
    /// stale on the first tab drag and the form drifts off the edge of the canvas.</para>
    ///
    /// <para>⛔ <b>Public because it is the single authority, not because a test wanted in.</b> The
    /// form is CENTRED at a fitted zoom, so canvas coordinates are not form coordinates and anything
    /// that needs to know where a control is on screen must ask this. The alternative — each caller
    /// re-deriving the margin and the centring — is exactly how <c>MinimapControl</c> ended up with
    /// three copies that drifted, where the picture looks right and the clicks land somewhere else.</para>
    /// </summary>
    public static FormCanvasTransform Fit(FormDocument document, Size viewport)
    {
        var surface = FormCanvasTransform.SurfaceSize(document);
        var width = surface.Width;
        var height = surface.Height;

        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return new FormCanvasTransform();
        }

        const double margin = 24;
        var zoom = Math.Min(
            Math.Max(1, viewport.Width - (margin * 2)) / width,
            Math.Max(1, viewport.Height - (margin * 2)) / height);
        zoom = Math.Min(1.0, zoom);

        var pan = new Vector(
            (viewport.Width - (width * zoom)) / 2,
            (viewport.Height - (height * zoom)) / 2);

        return new FormCanvasTransform(zoom, pan);
    }

    /// <summary>
    /// The form itself, drawn the way VB6 draws it: a real window with a title bar and a raised
    /// frame, its client area dotted with the alignment grid.
    ///
    /// <para>⚠ The title bar sits ABOVE the surface rectangle because the form's coordinate space is
    /// its CLIENT area — <c>Width</c>/<c>Height</c> are the client size, exactly as WinForms'
    /// <c>ClientSize</c> is. Drawing it inside would make every control's Y look 18px wrong.</para>
    /// </summary>
    private void DrawSurface(DrawingContext context, FormDocument document)
    {
        var size = FormCanvasTransform.SurfaceSize(document);
        var surface = _transform.ToCanvas(new Rect(0, 0, size.Width, size.Height));

        // ⚠ WinForms only. A .blwebform is a PAGE — it has no title bar, no window frame and no
        // alignment grid, and dressing one up as a window would claim a shape the browser will
        // never give it.
        var isWindow = document.Target == FormTarget.WinForms;

        if (isWindow)
        {
            var title = new Rect(
                surface.X, Math.Max(0, surface.Y - TitleBarHeight), surface.Width, TitleBarHeight);

            context.FillRectangle(TitleBarBrush, title);
            Bevel(context, new Rect(
                title.X - 2, title.Y - 2, title.Width + 4, surface.Height + title.Height + 4), raised: true);

            var captionRight = DrawTitleBarButtons(context, title);

            var caption = document.Text ?? document.Name;
            if (!string.IsNullOrEmpty(caption) && captionRight > title.X + 8)
            {
                // ⚠ Clipped to where the BUTTONS start, not to the title bar. A long caption
                // running under the close box is the one detail that instantly reads as "not a
                // real window".
                using (context.PushClip(new Rect(
                    title.X, title.Y, captionRight - title.X, title.Height)))
                {
                    context.DrawText(Text(caption, CaptionBrush), new Point(title.X + 4, title.Y + 2));
                }
            }
        }

        context.FillRectangle(SurfaceBrush, surface);

        if (isWindow)
        {
            DrawAlignmentGrid(context, size, surface);
        }

        // ⛔ The web cell guides, UNDER the controls. Without them a page is a blank rectangle with
        // no clue where a drop will land — the cells are the only thing on screen that says what a
        // .blwebform's geometry even means, because its controls are placed by cell, not by pixel.
        if (document.Target == FormTarget.Web && document.Layout?.Kind == FormLayoutKind.Grid)
        {
            context.DrawRectangle(null, ShadowPen, surface);
            foreach (var (_, _, cell) in FormGridLayout.Cells(document.Layout, size))
            {
                context.DrawRectangle(null, GridPen, _transform.ToCanvas(cell));
            }

            var pageCaption = document.Text ?? document.Name;
            if (!string.IsNullOrEmpty(pageCaption))
            {
                context.DrawText(
                    Text(pageCaption, LabelBrush),
                    new Point(surface.X + 4, Math.Max(0, surface.Y - 16)));
            }
        }
    }

    /// <summary>
    /// Minimise, maximise and close — three raised boxes at the right of the title bar.
    ///
    /// <para>⚠ Decoration, and the ONLY decoration on this canvas that depicts something the user
    /// cannot change. They are not hit-tested and do nothing: a close box that closed the designer
    /// would be a trap, and one that closed the form being designed means nothing at design time.
    /// They are here because a title bar without them does not read as a window.</para>
    ///
    /// <para>Returns the x where the buttons begin, so the caption can be clipped short of them.</para>
    /// </summary>
    private static double DrawTitleBarButtons(DrawingContext context, Rect title)
    {
        const double side = 14;
        const double gap = 2;

        // Nothing at all rather than a smear: a narrow form gets its caption and no buttons.
        if (title.Width < (side * 3) + (gap * 2) + 24)
        {
            return title.Right;
        }

        var top = title.Y + ((title.Height - side) / 2);
        var close = new Rect(title.Right - 2 - side, top, side, side);
        var maximise = new Rect(close.X - gap - side, top, side, side);
        var minimise = new Rect(maximise.X - gap - side, top, side, side);

        foreach (var box in new[] { minimise, maximise, close })
        {
            context.FillRectangle(SurfaceBrush, box);
            Bevel(context, box, raised: true);
        }

        // Minimise: a bar sitting on the floor of the box.
        context.DrawLine(GlyphPen,
            new Point(minimise.X + 3, minimise.Bottom - 4),
            new Point(minimise.Right - 4, minimise.Bottom - 4));

        // Maximise: a rectangle with a doubled top edge, which is how Win95 drew a title bar.
        var pane = new Rect(maximise.X + 3, maximise.Y + 3, side - 7, side - 7);
        context.DrawRectangle(null, GlyphPen, pane);
        context.DrawLine(GlyphPen, new Point(pane.X, pane.Y + 1), new Point(pane.Right, pane.Y + 1));

        // Close: an X, inset so it does not touch the bevel.
        context.DrawLine(GlyphPen,
            new Point(close.X + 4, close.Y + 4), new Point(close.Right - 4, close.Bottom - 4));
        context.DrawLine(GlyphPen,
            new Point(close.Right - 4, close.Y + 4), new Point(close.X + 4, close.Bottom - 4));

        return minimise.X - 4;
    }

    /// <summary>
    /// VB6's alignment dots: one pixel every <see cref="GridStep"/> form units.
    ///
    /// <para>⚠ Skipped once the spacing falls below about 4 canvas pixels. Zoomed out, dots that
    /// close stop reading as a grid and turn the form face into grey noise — and there are
    /// thousands of them, so it is the one thing here that could cost a frame.</para>
    /// </summary>
    private void DrawAlignmentGrid(DrawingContext context, Size size, Rect surface)
    {
        var step = GridStep * _transform.Zoom;
        if (step < 4)
        {
            return;
        }

        using (context.PushClip(surface))
        {
            for (var x = GridStep; x < size.Width; x += GridStep)
            {
                for (var y = GridStep; y < size.Height; y += GridStep)
                {
                    var dot = _transform.ToCanvas(new Rect(x, y, 1, 1)).TopLeft;
                    context.FillRectangle(GridDotBrush, new Rect(dot.X, dot.Y, 1, 1));
                }
            }
        }
    }

    /// <summary>
    /// A Win95 bevel: two lit edges top-left, two unlit bottom-right, swapped for sunken.
    ///
    /// <para>This one helper is every piece of chrome on the canvas — a raised Button, a sunken
    /// TextBox, a form frame. Drawn as lines rather than nested rectangles so the corners meet the
    /// way the real thing does.</para>
    /// </summary>
    private static void Bevel(DrawingContext context, Rect r, bool raised)
    {
        if (r.Width < 4 || r.Height < 4)
        {
            return;
        }

        var outerTopLeft = raised ? HighlightPen : ShadowPen;
        var innerTopLeft = raised ? FacePen : DarkShadowPen;
        var outerBottomRight = raised ? DarkShadowPen : HighlightPen;
        var innerBottomRight = raised ? ShadowPen : FacePen;

        // Outer ring.
        context.DrawLine(outerTopLeft, r.TopLeft, new Point(r.Right - 1, r.Y));
        context.DrawLine(outerTopLeft, r.TopLeft, new Point(r.X, r.Bottom - 1));
        context.DrawLine(outerBottomRight, new Point(r.X, r.Bottom - 1), new Point(r.Right - 1, r.Bottom - 1));
        context.DrawLine(outerBottomRight, new Point(r.Right - 1, r.Y), new Point(r.Right - 1, r.Bottom - 1));

        // Inner ring.
        var i = new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        context.DrawLine(innerTopLeft, i.TopLeft, new Point(i.Right - 1, i.Y));
        context.DrawLine(innerTopLeft, i.TopLeft, new Point(i.X, i.Bottom - 1));
        context.DrawLine(innerBottomRight, new Point(i.X, i.Bottom - 1), new Point(i.Right - 1, i.Bottom - 1));
        context.DrawLine(innerBottomRight, new Point(i.Right - 1, i.Y), new Point(i.Right - 1, i.Bottom - 1));
    }

    /// <summary>
    /// Draws one control as the shape its CATALOG ROW asks for.
    ///
    /// <para>⛔ Still a schematic (D-WYSIWYG) — flat, obviously diagrammatic, and not a claim about
    /// what the running program looks like. The point is only that the kinds are TELLABLE APART: one
    /// rectangle for all ten made a populated form unreadable, because a Button, a CheckBox and a
    /// TextBox were pixel-identical and only the label text differed.</para>
    ///
    /// <para>⛔ The shape comes from <see cref="FormSchematic"/> on the catalog row, never from a
    /// switch over <c>control.Kind</c> here. A switch keyed on kind is a SECOND list of controls,
    /// and this repo has been bitten by exactly that: it falls to its default the day a row is
    /// added, and the symptom is a new control that silently draws as a plain box.</para>
    /// </summary>
    private CaptionDraw? DrawControl(DrawingContext context, FormControl control, Rect bounds)
    {
        // ⚠ No selected/unselected variant here, deliberately. VB6 marks a selection with its eight
        // handles and NOTHING else — a control does not change colour or gain an outline when you
        // click it. DrawHandles, called after every control is painted, is the whole indication.
        var schematic = FormControlCatalog.Find(control.Kind)?.Schematic ?? FormSchematic.Input;

        // The control's own colours win over the system ones. `face` is what a chrome-coloured
        // control fills with, `client` what a white-interior one does — a BackColor overrides
        // whichever of the two this schematic uses.
        var back = ControlColour(control, "BackColor");
        var face = back ?? SurfaceBrush;
        var client = back ?? WindowBrush;
        var ink = ControlColour(control, "ForeColor") ?? LabelBrush;

        // The control's own Text if it has one, else its id — a box with no label is unidentifiable
        // on a schematic, which is the one thing the canvas has to get right. ⛔ The SAME answer the
        // layout sized this control's cell from (FormCanvasTransform.Caption): an item's accelerator
        // is resolved, so "&Open" draws as "Open" with the O underlined, as the running form does.
        var (label, underline) = FormCanvasTransform.Caption(control);

        // ⚠ What was drawn is RETURNED, never stored here: the live canvas discards it, and only
        // Render's test-only caption log (null on every real canvas) records it — see
        // RenderDocumentForTest.
        return DrawSchematic(context, schematic, bounds, label, face, client, ink, underline);
    }

    /// <summary>
    /// TEST SEAM (form-designer menu-editor defects): the exact string <see cref="DrawControl"/>
    /// hands to <c>DrawSchematic</c> — i.e. the caption that actually reaches <c>DrawText</c>. It is
    /// <see cref="FormCanvasTransform.Caption"/>, the one rule DrawControl and the layout both call,
    /// so the seam cannot drift from the draw path. Public only because the Shell grants no
    /// <c>InternalsVisibleTo</c> to the test project.
    /// </summary>
    public static string CaptionForTest(FormControl control) => FormCanvasTransform.Caption(control).Display;

    /// <summary>
    /// TEST SEAM (menu-editor defects, zoom-scaling correction): measures a caption the way the
    /// canvas is meant to draw one under the DECIDED design — same typeface, same
    /// <see cref="FormCanvasTransform.CaptionFontSize"/>, scaled by <paramref name="zoom"/>. Exposed
    /// so a clipping test can compare a real Avalonia-measured width against a cell's (zoom-scaled)
    /// layout width without duplicating the font settings.
    ///
    /// <para>⚠ <paramref name="zoom"/> defaults to 1.0, so every existing caller is
    /// behaviour-identical to the old, zoom-less overload — this is purely additive.</para>
    ///
    /// <para>⛔ This seam does NOT itself fix the live drawing bug: <see cref="Text(string, IBrush)"/>
    /// — the helper <see cref="DrawSchematic"/> and <see cref="DrawTypeHereSlot"/> actually call — is
    /// still FIXED at <see cref="FormCanvasTransform.CaptionFontSize"/> regardless of zoom. A test
    /// using this seam therefore checks whether the DESIGN is self-consistent (a caption that fits at
    /// zoom 1:1 keeps fitting once both the font and the cell are scaled by the same zoom), not
    /// whether the live render path has been updated to apply that scaling yet — that update is a
    /// production change out of this pass's scope.</para>
    /// </summary>
    public static FormattedText MeasureCaptionForTest(string text, double zoom = 1.0) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default),
        FormCanvasTransform.CaptionFontSize * zoom,
        Brushes.Black);

    /// <summary>
    /// The empty cell at the end of a strip's or a dropdown's items — VS's "Type Here".
    ///
    /// <para>⛔ Drawn HERE rather than through <see cref="DrawSchematic"/>, and that is not an
    /// oversight. Every schematic is a shape a CATALOG ROW selects; a slot has no control, so no
    /// row can ever select it, and adding a <c>FormSchematic.TypeHere</c> value for it would put a
    /// shape into the per-value pin that nothing in the catalog can reach. It is the one thing this
    /// canvas draws that is not a control.</para>
    ///
    /// <para>⛔ The caption is <see cref="FormCanvasTransform.TypeHereCaption"/>, never a literal
    /// "Type Here" here. The slot's WIDTH is derived from that same constant
    /// (<c>CellWidth(TypeHereCaption)</c>), so a second spelling would draw a caption the box was
    /// not sized for — and the drift would show up only as text creeping past the slot's edge.</para>
    ///
    /// <para>⚠ Grey and italic, which is what says "not a control yet". The ACTIVE slot — the one
    /// <see cref="TypeHereHost"/> names — additionally takes the window colour and a solid
    /// selection-coloured border, because the overlay TextBox is positioned exactly on it and the
    /// user needs to see WHERE the thing they are typing into is before it appears.</para>
    /// </summary>
    private static CaptionDraw? DrawTypeHereSlot(
        DrawingContext context, Rect bounds, bool active, FormControl? host, double zoom)
    {
        context.FillRectangle(active ? WindowBrush : SurfaceBrush, bounds);
        context.DrawRectangle(null, active ? ActiveSlotPen : SlotPen, bounds);

        // The same floor DrawSchematic uses: below it a caption is a smear rather than a word.
        if (bounds.Width < 12 || bounds.Height < 10)
        {
            return null;
        }

        // ⛔ At the inset and size of the item this slot will COMMIT to (FormCanvasTransform.
        // SlotCaption — the host's default item kind's caption inset, scaled by the zoom like the
        // cell is). The overlay editor asks the same function, so the placeholder, the typed text
        // and the committed caption all start at one x. Clipped to the slot, like every caption.
        var (inset, fontSize) = FormCanvasTransform.SlotCaption(host, zoom);
        var caption = SlotText(fontSize);
        var origin = new Point(
            bounds.X + inset,
            bounds.Y + Math.Max(2 * zoom, (bounds.Height - caption.Height) / 2));

        using (context.PushClip(bounds))
        {
            context.DrawText(caption, origin);
        }

        return new CaptionDraw(origin, caption.Width, caption.Height, fontSize);
    }

    /// <summary>
    /// Draws ONE schematic at ONE rectangle — the whole of what a shape is, and the only place a
    /// shape is decided. <see cref="DrawControl"/> resolves a control to its row's schematic, its
    /// colours and its label, and hands them here.
    ///
    /// <para>⛔ The seam exists so a schematic can be pinned per VALUE rather than per catalog ROW:
    /// <c>FormSchematicPinTests</c> hashes a frame for every <see cref="FormSchematic"/> at one
    /// bounds with one label and requires them ALL PAIRWISE DISTINCT. The row-driven gate cannot see
    /// a schematic no row uses — and from Task 24 the item schematics are exactly that, because an
    /// item has no document of its own to render.</para>
    ///
    /// <para>⛔ The post-switch LABEL DRAW is part of this method, deliberately.
    /// <c>labelOrigin</c> is declared before the switch and MUTATED by arms — Button centres it,
    /// Check and Radio move it past the glyph, Tabs drops it below the tab strip — and the draw
    /// after the switch consumes the mutated value. Split the two and every Button caption paints
    /// at the top left and every CheckBox caption paints over its own tick, from a green suite.</para>
    ///
    /// <para>⚠ The three BAND arms <c>return</c> rather than <c>break</c>: a strip is chrome and
    /// draws no caption at all (spec §6). Painting a band's id across it would be the only pixel a
    /// render gate ever sees, which is how a band would come to look "verified".</para>
    /// </summary>
    /// <returns>
    /// The origin the caption was actually drawn at, or <c>null</c> when this render drew no caption
    /// at all — a band, an empty label, or a rectangle too small for text.
    ///
    /// <para>⛔ This return value is the ONLY pin caption POSITION can have, and it exists because
    /// a hash gate cannot see one. A frame is compared for DISTINCTNESS, so deleting an arm's
    /// centring arithmetic still yields a unique frame that is still different from every other —
    /// both the pairwise pin and the caption-presence pin stay green while the caption sits in the
    /// wrong place. Mutation testing proved exactly that. Golden pixel hashes would catch it and are
    /// refused here: they break on every Avalonia or Skia bump. So the seam reports the ARITHMETIC
    /// instead, and <c>FormSchematicPinTests</c> asserts on coordinates rather than glyphs. The
    /// offsets this protects are load-bearing and say so in their own arms: <c>MenuItem</c>'s 8px
    /// inset, <c>ToolButton</c>'s inset box, <c>StatusLabel</c>'s vertical centring, <c>Button</c>'s
    /// centring, and the glyph clearance <c>Check</c>/<c>Radio</c>/<c>Tabs</c> apply.</para>
    ///
    /// <para>⚠ Returned, NOT written to a field from beside the <c>DrawText</c> call. A write there
    /// would put test-only bookkeeping in the live draw path, which runs for every control on every
    /// render, and would record only the LAST control drawn — meaningless for a real document. A
    /// return value costs the live path nothing: <see cref="DrawControl"/> discards it.</para>
    ///
    /// <para>⛔ Do NOT "simplify" this by extracting a pure <c>CaptionOriginFor(...)</c> that both
    /// the draw and the test call. That is a SECOND copy of every offset, and a mirrored pair that
    /// drifts is the exact defect this pin exists to catch — the copy would keep agreeing with the
    /// test while the drawn caption moved. The arms also interleave the arithmetic with drawing and
    /// with measured text (<c>Button</c> and <c>StatusLabel</c> centre against <c>caption.Width</c>
    /// and <c>caption.Height</c>), so there is no separable pure function here anyway.</para>
    /// </returns>
    private CaptionDraw? DrawSchematic(
        DrawingContext context,
        FormSchematic schematic,
        Rect bounds,
        string label,
        IBrush face,
        IBrush client,
        IBrush ink,
        int underline = -1)
    {
        // Where the label goes once the shape has had its say: indented past a tick or a bullet,
        // centred in a button, at the top-left of everything else.
        var labelOrigin = new Point(bounds.X + 4, bounds.Y + 2);
        var tooSmallForText = bounds.Width < 12 || bounds.Height < 10;

        // ⛔⛔ An ITEM's caption scales with the zoom — font size AND inset — because its cell does:
        // the cell is laid out in form units from the caption and scaled by the zoom, so a caption
        // drawn at a fixed 12px outgrew it below 1:1 (the owner's "&Ope"). Scaling both sides by the
        // same zoom means a caption that fits at 1:1 fits at every zoom. The three item arms set
        // captionSize; every other arm keeps the fixed size (a positioned control's caption is not
        // part of this fix — see the report). The probe renders with the default transform, zoom 1.
        var zoom = _transform.Zoom;
        var captionSize = FormCanvasTransform.CaptionFontSize;

        switch (schematic)
        {
            case FormSchematic.Text:
                // No box and no fill: a Label is words ON the form face, which is why it is the one
                // control whose background is whatever it sits on.
                break;

            case FormSchematic.Button:
                context.FillRectangle(face, bounds);
                Bevel(context, bounds, raised: true);
                if (!tooSmallForText && !string.IsNullOrEmpty(label))
                {
                    var caption = Text(label, ink);
                    labelOrigin = new Point(
                        bounds.X + Math.Max(3, (bounds.Width - caption.Width) / 2),
                        bounds.Y + Math.Max(2, (bounds.Height - caption.Height) / 2));
                }

                break;

            case FormSchematic.Check:
            case FormSchematic.Radio:
            {
                // The glyph is vertically centred and clamped to the box, so a tall CheckBox does
                // not get its tick floating at the top edge.
                var side = Math.Min(GlyphSide, Math.Min(bounds.Width, bounds.Height) - 2);
                if (side > 3)
                {
                    var glyph = new Rect(
                        bounds.X + 1, bounds.Y + ((bounds.Height - side) / 2), side, side);

                    if (schematic == FormSchematic.Radio)
                    {
                        // A radio is round, so it gets a drawn ring rather than a bevel — two arcs
                        // would be the faithful thing and are not worth the geometry here.
                        context.DrawEllipse(WindowBrush, ShadowPen, glyph.Center,
                            glyph.Width / 2, glyph.Height / 2);
                    }
                    else
                    {
                        context.FillRectangle(WindowBrush, glyph);
                        Bevel(context, glyph, raised: false);
                    }

                    labelOrigin = new Point(glyph.Right + 4, bounds.Y + 2);
                }

                break;
            }

            case FormSchematic.Dropdown:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // The drop button: a raised square on the right with a filled triangle, the way
                // every Win95 combo has one.
                var buttonSide = Math.Min(bounds.Height - 4, 16);
                if (buttonSide > 6 && bounds.Width > buttonSide + 8)
                {
                    var button = new Rect(
                        bounds.Right - buttonSide - 2, bounds.Y + 2, buttonSide, buttonSide);
                    context.FillRectangle(SurfaceBrush, button);
                    Bevel(context, button, raised: true);

                    var cx = button.Center.X;
                    var cy = button.Center.Y;
                    context.DrawLine(DarkShadowPen, new Point(cx - 3, cy - 1), new Point(cx + 3, cy - 1));
                    context.DrawLine(DarkShadowPen, new Point(cx - 2, cy), new Point(cx + 2, cy));
                    context.DrawLine(DarkShadowPen, new Point(cx - 1, cy + 1), new Point(cx + 1, cy + 1));
                }

                break;
            }

            case FormSchematic.List:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // ⚠ Rows start ONE row down from the top edge, not 18px down. The old offset was
                // taller than a short ListBox, so the rules vanished entirely on exactly the
                // controls that most needed the hint — a 26px-high list drew as a blank white box
                // indistinguishable from a TextBox.
                const double rowHeight = 13;
                for (var y = bounds.Y + 2 + rowHeight; y < bounds.Bottom - 2; y += rowHeight)
                {
                    context.DrawLine(RowPen, new Point(bounds.X + 3, y), new Point(bounds.Right - 3, y));
                }

                break;
            }

            case FormSchematic.Container:
                // Face-coloured with a sunken edge, like a Win95 Panel. It holds other controls, so
                // the fill matches the form rather than hiding them under a different tone.
                context.FillRectangle(face, bounds);
                Bevel(context, bounds, raised: false);
                break;

            case FormSchematic.Group:
            {
                // An etched frame starting below the caption, with the caption sitting IN the gap —
                // the GroupBox shape everyone recognises.
                var frame = new Rect(
                    bounds.X, bounds.Y + 6, bounds.Width, Math.Max(4, bounds.Height - 6));
                context.DrawRectangle(null, ShadowPen, frame);
                context.DrawRectangle(null, HighlightPen,
                    new Rect(frame.X + 1, frame.Y + 1, Math.Max(1, frame.Width - 1), Math.Max(1, frame.Height - 1)));

                if (!tooSmallForText && !string.IsNullOrEmpty(label))
                {
                    var caption = Text(label, LabelBrush);
                    context.FillRectangle(SurfaceBrush,
                        new Rect(bounds.X + 6, bounds.Y, caption.Width + 4, 12));
                }

                break;
            }

            case FormSchematic.Image:
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);
                context.DrawLine(RowPen, new Point(bounds.X + 2, bounds.Y + 2),
                    new Point(bounds.Right - 2, bounds.Bottom - 2));
                context.DrawLine(RowPen, new Point(bounds.Right - 2, bounds.Y + 2),
                    new Point(bounds.X + 2, bounds.Bottom - 2));
                break;

            // ==================================================================
            // Task 23's widening. ⛔ Every one of these must paint DIFFERENTLY from every other
            // shape, not merely differently in intent: EveryControlKindRendersDistinctly hashes a
            // frame per kind over identical geometry and an identical id, so two schematics that
            // happen to produce the same pixels collide and fail. That guard is the whole reason a
            // new kind cannot quietly become another plain box.
            // ==================================================================

            case FormSchematic.Link:
                // No box, like a Label — the underline is the entire difference, which is exactly
                // what a LinkLabel is.
                if (!tooSmallForText && !string.IsNullOrEmpty(label))
                {
                    var linkText = Text(label, ink);
                    var baseline = bounds.Y + 2 + linkText.Height - 1;
                    context.DrawLine(
                        new Pen(ink, 1),
                        new Point(bounds.X + 4, baseline),
                        new Point(Math.Min(bounds.Right - 2, bounds.X + 4 + linkText.Width), baseline));
                }

                break;

            case FormSchematic.CheckList:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // Rows with a tick box at the left of each — a ListBox that can be ticked.
                const double rowHeight = 13;
                for (var y = bounds.Y + 3; y < bounds.Bottom - 6; y += rowHeight)
                {
                    var box = new Rect(bounds.X + 3, y + 2, 7, 7);
                    if (box.Bottom < bounds.Bottom - 2)
                    {
                        context.DrawRectangle(WindowBrush, ShadowPen, box);
                    }
                }

                break;
            }

            case FormSchematic.Spinner:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // The stacked up/down pair a NumericUpDown carries at its right edge.
                var w = Math.Min(14.0, bounds.Width / 3);
                if (w > 5 && bounds.Height > 8)
                {
                    var half = (bounds.Height - 4) / 2;
                    var up = new Rect(bounds.Right - w - 2, bounds.Y + 2, w, half);
                    var down = new Rect(bounds.Right - w - 2, up.Bottom, w, half);
                    context.FillRectangle(SurfaceBrush, up);
                    Bevel(context, up, raised: true);
                    context.FillRectangle(SurfaceBrush, down);
                    Bevel(context, down, raised: true);
                    Chevron(context, up.Center, pointingDown: false);
                    Chevron(context, down.Center, pointingDown: true);
                }

                break;
            }

            case FormSchematic.DatePicker:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // A small calendar block at the right — the glyph that tells it from a plain box.
                var side = Math.Min(bounds.Height - 4, 16);
                if (side > 7 && bounds.Width > side + 10)
                {
                    var pad = new Rect(bounds.Right - side - 2, bounds.Y + 2, side, side);
                    context.FillRectangle(SurfaceBrush, pad);
                    Bevel(context, pad, raised: true);

                    // A header band plus two rules: unmistakably a calendar at this size.
                    context.FillRectangle(TitleBarBrush, new Rect(pad.X + 2, pad.Y + 2, pad.Width - 4, 3));
                    for (var i = 1; i <= 2; i++)
                    {
                        var y = pad.Y + 5 + (i * 3);
                        if (y < pad.Bottom - 1)
                        {
                            context.DrawLine(RowPen, new Point(pad.X + 2, y), new Point(pad.Right - 2, y));
                        }
                    }
                }

                break;
            }

            case FormSchematic.Slider:
            {
                // No client box at all: a TrackBar sits ON the form face.
                var midY = bounds.Y + (bounds.Height / 3);
                var groove = new Rect(bounds.X + 2, midY - 2, Math.Max(4, bounds.Width - 4), 4);
                context.FillRectangle(WindowBrush, groove);
                Bevel(context, groove, raised: false);

                // The thumb, at the left because Value defaults to Minimum.
                var thumb = new Rect(bounds.X + 3, midY - 7, 8, 14);
                if (thumb.Right < bounds.Right)
                {
                    context.FillRectangle(SurfaceBrush, thumb);
                    Bevel(context, thumb, raised: true);
                }

                // Ticks below, which is what makes it a TrackBar rather than a scrollbar.
                for (var x = bounds.X + 4; x < bounds.Right - 2; x += 10)
                {
                    context.DrawLine(ShadowPen,
                        new Point(x, bounds.Bottom - 6), new Point(x, bounds.Bottom - 2));
                }

                break;
            }

            case FormSchematic.Progress:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // Segmented blocks filling about a third — the classic Blocks style. Drawn at a
                // fixed fraction rather than from Value, because the canvas is a schematic and a
                // half-accurate bar would invite the user to read it as a preview.
                var inner = new Rect(bounds.X + 2, bounds.Y + 2,
                    Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4));
                var filled = inner.Width / 3;
                for (var x = inner.X; x < inner.X + filled; x += 8)
                {
                    var block = new Rect(x, inner.Y, Math.Min(6, inner.X + filled - x), inner.Height);
                    if (block.Width > 0)
                    {
                        context.FillRectangle(TitleBarBrush, block);
                    }
                }

                break;
            }

            case FormSchematic.ListDetail:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // A raised header band with column dividers, then rows.
                var header = new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), 12);
                if (header.Bottom < bounds.Bottom)
                {
                    context.FillRectangle(SurfaceBrush, header);
                    Bevel(context, header, raised: true);
                    for (var x = header.X + (header.Width / 3); x < header.Right; x += header.Width / 3)
                    {
                        context.DrawLine(ShadowPen, new Point(x, header.Y), new Point(x, header.Bottom));
                    }
                }

                for (var y = header.Bottom + 12; y < bounds.Bottom - 2; y += 12)
                {
                    context.DrawLine(RowPen, new Point(bounds.X + 3, y), new Point(bounds.Right - 3, y));
                }

                break;
            }

            case FormSchematic.Tree:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                // Indented rows with expander boxes — a hierarchy, not a flat list.
                var row = 0;
                for (var y = bounds.Y + 4; y < bounds.Bottom - 8; y += 13, row++)
                {
                    var indent = (row % 3) * 10;
                    var box = new Rect(bounds.X + 4 + indent, y, 7, 7);
                    if (box.Right < bounds.Right - 2 && box.Bottom < bounds.Bottom - 2)
                    {
                        context.DrawRectangle(WindowBrush, ShadowPen, box);
                        context.DrawLine(DarkShadowPen,
                            new Point(box.X + 2, box.Center.Y), new Point(box.Right - 2, box.Center.Y));
                        context.DrawLine(RowPen,
                            new Point(box.Right + 3, box.Center.Y),
                            new Point(Math.Min(bounds.Right - 3, box.Right + 40), box.Center.Y));
                    }
                }

                break;
            }

            case FormSchematic.DataGrid:
            {
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);

                var header = new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), 12);
                if (header.Bottom < bounds.Bottom)
                {
                    context.FillRectangle(SurfaceBrush, header);
                    Bevel(context, header, raised: true);
                }

                // A FULL lattice — both rules — which is what separates a grid from a list.
                for (var y = header.Bottom + 12; y < bounds.Bottom - 2; y += 12)
                {
                    context.DrawLine(RowPen, new Point(bounds.X + 3, y), new Point(bounds.Right - 3, y));
                }

                for (var x = bounds.X + 18; x < bounds.Right - 3; x += 34)
                {
                    context.DrawLine(RowPen, new Point(x, bounds.Y + 3), new Point(x, bounds.Bottom - 3));
                }

                break;
            }

            case FormSchematic.Tabs:
            {
                // The body, starting below the strip.
                var body = new Rect(bounds.X, bounds.Y + 16, bounds.Width, Math.Max(4, bounds.Height - 16));
                context.FillRectangle(face, body);
                Bevel(context, body, raised: true);

                // Two tabs, the first raised and joined to the body.
                var tabWidth = Math.Min(48.0, Math.Max(16, bounds.Width / 3));
                for (var i = 0; i < 2; i++)
                {
                    var tab = new Rect(
                        bounds.X + 2 + (i * (tabWidth + 2)),
                        bounds.Y + (i == 0 ? 2 : 4),
                        tabWidth,
                        i == 0 ? 15 : 13);

                    if (tab.Right < bounds.Right)
                    {
                        context.FillRectangle(SurfaceBrush, tab);
                        Bevel(context, tab, raised: true);
                    }
                }

                labelOrigin = new Point(bounds.X + 6, bounds.Y + 18);
                break;
            }

            case FormSchematic.Split:
            {
                context.FillRectangle(face, bounds);
                Bevel(context, bounds, raised: false);

                // A splitter bar a third of the way across, with a pane either side.
                var x = bounds.X + (bounds.Width / 3);
                var bar = new Rect(x - 2, bounds.Y + 2, 4, Math.Max(0, bounds.Height - 4));
                context.FillRectangle(SurfaceBrush, bar);
                Bevel(context, bar, raised: true);

                context.DrawRectangle(null, ShadowPen,
                    new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, x - bounds.X - 5), Math.Max(0, bounds.Height - 4)));
                context.DrawRectangle(null, ShadowPen,
                    new Rect(x + 3, bounds.Y + 2, Math.Max(0, bounds.Right - x - 5), Math.Max(0, bounds.Height - 4)));
                break;
            }

            case FormSchematic.FlowContainer:
            {
                context.FillRectangle(face, bounds);
                Bevel(context, bounds, raised: false);

                // Chevrons along the top edge: this container ORDERS what is dropped in it, which
                // is the one thing a user needs to know before dropping anything.
                for (var x = bounds.X + 8; x < bounds.Right - 6; x += 12)
                {
                    Chevron(context, new Point(x, bounds.Y + 7), pointingDown: false, sideways: true);
                }

                break;
            }

            case FormSchematic.TableContainer:
            {
                context.FillRectangle(face, bounds);
                Bevel(context, bounds, raised: false);

                // Ruled into cells so the layout is visible while it is still empty — the state a
                // TableLayoutPanel spends most of its design life in.
                for (var x = bounds.X + (bounds.Width / 2); x < bounds.Right - 2; x += bounds.Width / 2)
                {
                    context.DrawLine(ShadowPen, new Point(x, bounds.Y + 2), new Point(x, bounds.Bottom - 2));
                }

                for (var y = bounds.Y + (bounds.Height / 2); y < bounds.Bottom - 2; y += bounds.Height / 2)
                {
                    context.DrawLine(ShadowPen, new Point(bounds.X + 2, y), new Point(bounds.Right - 2, y));
                }

                break;
            }

            // ==================================================================
            // Task 25's tray. ⚠ UNREACHABLE from the canvas and deliberately present: a component
            // has no geometry, so Layout never yields bounds for one and DrawControl is never
            // called with these. They are here because the per-VALUE pin hashes every schematic
            // through this seam, and without an arm each of the four falls to `default` and paints
            // EXACTLY what Input paints — five values, one frame. The arms are also the honest
            // answer to "what if a positioned row ever picks one": its own mark, never a TextBox.
            // ==================================================================

            case FormSchematic.Clock:
            {
                // A clock face with two hands — a Timer.
                var radius = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 2 - 3);
                var centre = bounds.Center;
                context.DrawEllipse(client, ShadowPen, centre, radius, radius);
                context.DrawLine(DarkShadowPen, centre, new Point(centre.X, centre.Y - radius + 3));
                context.DrawLine(DarkShadowPen, centre, new Point(centre.X + radius - 4, centre.Y));
                break;
            }

            case FormSchematic.Hint:
            {
                // A hint bubble with a tail — what a ToolTip puts on screen.
                var bubble = new Rect(
                    bounds.X + 4, bounds.Y + 4,
                    Math.Max(6, bounds.Width - 12), Math.Max(6, bounds.Height - 14));
                context.DrawRectangle(client, ShadowPen, bubble);
                context.DrawLine(ShadowPen,
                    new Point(bubble.X + 8, bubble.Bottom), new Point(bubble.X + 8, bubble.Bottom + 6));
                context.DrawLine(ShadowPen,
                    new Point(bubble.X + 8, bubble.Bottom + 6), new Point(bubble.X + 16, bubble.Bottom));
                break;
            }

            case FormSchematic.Alert:
            {
                // The badge an ErrorProvider puts BESIDE a control, so it sits at the right edge.
                var radius = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 2 - 3);
                var centre = new Point(bounds.Right - radius - 3, bounds.Center.Y);
                context.DrawEllipse(client, DarkShadowPen, centre, radius, radius);
                context.DrawLine(DarkShadowPen,
                    new Point(centre.X, centre.Y - radius + 4), new Point(centre.X, centre.Y + radius - 8));
                context.DrawLine(DarkShadowPen,
                    new Point(centre.X, centre.Y + radius - 5), new Point(centre.X, centre.Y + radius - 4));
                break;
            }

            case FormSchematic.Worker:
            {
                // Two offset boxes: the same work, running somewhere other than the UI thread.
                var w = Math.Max(4, bounds.Width / 3);
                var h = Math.Max(4, bounds.Height / 2);
                var behind = new Rect(bounds.X + 4, bounds.Y + 4, w, h);
                var front = new Rect(behind.X + 6, behind.Y + 6, w, h);
                context.DrawRectangle(client, ShadowPen, behind);
                context.DrawRectangle(client, DarkShadowPen, front);
                break;
            }

            // ==================================================================
            // Task 24 — menus, toolbars and status bars. Landed in commit 24b, BEFORE any catalog
            // row uses one, so the pairwise pin is in force from the moment the shapes exist.
            //
            // ⛔ The three BANDS return, they do not break: a strip draws NO caption (spec §6). Its
            //   id painted across the band would be the only pixel a render gate ever sees, and the
            //   band would look verified while being a grey rectangle.
            // ==================================================================

            case FormSchematic.MenuBar:
                // A flat band with a rule along its BOTTOM edge — where the menu ends and the client
                // area begins. Flat, not bevelled: a menu strip is not a raised panel.
                context.FillRectangle(face, bounds);
                context.DrawLine(ShadowPen,
                    new Point(bounds.X, bounds.Bottom - 1), new Point(bounds.Right, bounds.Bottom - 1));
                return null;

            case FormSchematic.ToolBar:
            {
                // A band wearing its drag grip at the left edge — the one detail that tells a tool
                // strip from a menu bar at a glance, and the same two-tone pair Win95 draws.
                context.FillRectangle(face, bounds);

                var gripTop = bounds.Y + 3;
                var gripBottom = Math.Max(gripTop + 1, bounds.Bottom - 3);
                context.DrawLine(HighlightPen,
                    new Point(bounds.X + 3, gripTop), new Point(bounds.X + 3, gripBottom));
                context.DrawLine(ShadowPen,
                    new Point(bounds.X + 5, gripTop), new Point(bounds.X + 5, gripBottom));
                return null;
            }

            case FormSchematic.StatusBar:
            {
                // A band with the rule along its TOP edge — the mirror of the menu bar, because the
                // client area ends ABOVE a status strip — and the sizing grip at the bottom right.
                context.FillRectangle(face, bounds);
                context.DrawLine(ShadowPen, new Point(bounds.X, bounds.Y), new Point(bounds.Right, bounds.Y));

                for (var i = 1; i <= 3; i++)
                {
                    var offset = i * 4;
                    context.DrawLine(ShadowPen,
                        new Point(bounds.Right - 2, bounds.Bottom - offset),
                        new Point(bounds.Right - offset, bounds.Bottom - 2));
                }

                return null;
            }

            case FormSchematic.MenuItem:
                // A client-coloured cell behind the caption and NO border: a menu item is not a
                // widget, it is a highlightable strip of a menu.
                // ⚠ NOT "the caption with a 2px pad and no box" — that is the Label arm shifted two
                // pixels, and the pairwise pin refuses it. The filled cell is the difference.
                context.FillRectangle(client, bounds);
                captionSize = FormCanvasTransform.CaptionFontSize * zoom;
                labelOrigin = new Point(
                    bounds.X + (FormCanvasTransform.ItemCaptionInset(FormSchematic.MenuItem) * zoom),
                    bounds.Y + (2 * zoom));
                break;

            case FormSchematic.Separator:
            {
                // ONE rule, running whichever way its cell does: across a tool strip, down a
                // dropdown. Nothing else — a separator has no face of its own.
                if (bounds.Width > bounds.Height)
                {
                    var y = bounds.Y + (bounds.Height / 2);
                    context.DrawLine(ShadowPen, new Point(bounds.X + 2, y), new Point(bounds.Right - 2, y));
                }
                else
                {
                    var x = bounds.X + (bounds.Width / 2);
                    context.DrawLine(ShadowPen, new Point(x, bounds.Y + 2), new Point(x, bounds.Bottom - 2));
                }

                break;
            }

            case FormSchematic.ToolButton:
            {
                // A small raised box INSET in its cell, caption at its left. ⚠ Inset and left, both
                // load-bearing: a full-bounds raised box with a centred caption IS the Button arm.
                var boxInset = FormCanvasTransform.ToolButtonBoxInset * zoom;
                var box = new Rect(
                    bounds.X + boxInset, bounds.Y + boxInset,
                    Math.Max(4, bounds.Width - (2 * boxInset)), Math.Max(4, bounds.Height - (2 * boxInset)));
                context.FillRectangle(face, box);
                Bevel(context, box, raised: true);
                captionSize = FormCanvasTransform.CaptionFontSize * zoom;
                labelOrigin = new Point(
                    bounds.X + (FormCanvasTransform.ItemCaptionInset(FormSchematic.ToolButton) * zoom),
                    box.Y + (3 * zoom));
                break;
            }

            case FormSchematic.StatusLabel:
            {
                // The panel divider — a rule down the LEFT edge, where one status panel ends and the
                // next begins — with the caption centred in the band's height.
                // ⚠ "The caption at the left" ALONE is pixel-identical to the Label arm, which draws
                // no box and puts its caption at (X+4, Y+2). The rule and the centring are the shape.
                context.DrawLine(new Pen(ink, 1),
                    new Point(bounds.X, bounds.Y + 2), new Point(bounds.X, bounds.Bottom - 2));

                captionSize = FormCanvasTransform.CaptionFontSize * zoom;
                if (!tooSmallForText && !string.IsNullOrEmpty(label))
                {
                    var caption = Text(label, ink, captionSize);
                    labelOrigin = new Point(
                        bounds.X + (FormCanvasTransform.ItemCaptionInset(FormSchematic.StatusLabel) * zoom),
                        bounds.Y + Math.Max(2 * zoom, (bounds.Height - caption.Height) / 2));
                }

                break;
            }

            default:
                // A TextBox and anything text-entry shaped: white client, sunken edge.
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);
                break;
        }

        if (string.IsNullOrEmpty(label) || tooSmallForText)
        {
            return null;
        }

        // ⛔ The mnemonic underline is a decoration ON the formatted text, not a line drawn at a
        // computed x: the shaper decides where that glyph starts, so this lands under the right
        // character in any font, and moves with it wherever an arm put the caption.
        var fontSize = captionSize;
        var formatted = Text(label, ink, fontSize);
        if (underline >= 0 && underline < label.Length)
        {
            formatted.SetTextDecorations(TextDecorations.Underline, underline, 1);
        }

        using (context.PushClip(bounds))
        {
            context.DrawText(formatted, labelOrigin);
        }

        // ⚠ Reported AFTER the draw, and every field is the very value the draw above consumed —
        // not a recomputation. Anything else could agree with the test while disagreeing with the
        // pixels, which is the whole failure this return value exists to make impossible.
        return new CaptionDraw(labelOrigin, formatted.Width, formatted.Height, fontSize);
    }

    /// <summary>
    /// What one caption draw actually did: where, how big the shaped text was, and the font size it
    /// was shaped at. Returned by <see cref="DrawSchematic"/> and <see cref="DrawTypeHereSlot"/>;
    /// discarded by the live canvas and recorded only by the test-only caption log.
    /// </summary>
    public sealed record CaptionDraw(Point Origin, double Width, double Height, double FontSize);

    /// <summary>One caption the LIVE document render drew — see <see cref="RenderDocumentForTest"/>.</summary>
    /// <param name="Control">The control whose caption this is; null for a Type Here slot.</param>
    /// <param name="SlotHost">The slot's host; null for a control.</param>
    /// <param name="Bounds">The canvas rectangle the caption was drawn in.</param>
    /// <param name="Label">The caption text.</param>
    /// <param name="Drawn">What was drawn, or null when this render drew no caption there.</param>
    public sealed record CaptionRecord(
        FormControl? Control, FormControl? SlotHost, Rect Bounds, string Label, CaptionDraw? Drawn);

    /// <summary>The result of <see cref="RenderDocumentForTest"/>.</summary>
    public sealed record DocumentCaptions(double Zoom, IReadOnlyList<CaptionRecord> Captions);

    /// <summary>
    /// Null on every real canvas. Set only by <see cref="RenderDocumentForTest"/>, for the one
    /// render it drives — so the live path pays a null check per control and records nothing.
    /// </summary>
    private List<CaptionRecord>? _captionLog;

    /// <summary>
    /// TEST SEAM: renders a DOCUMENT through the real <see cref="Render"/> — Fit, Layout, DrawControl,
    /// DrawTypeHereSlot — at <paramref name="viewport"/>, and reports every caption it drew, as drawn.
    ///
    /// <para>⛔ Exists because the clipping tests measure through <see cref="MeasureCaptionForTest"/>,
    /// which is NOT the draw path: a draw that ignored the zoom left them green. This reports the
    /// font size and extent the live render actually shaped, so a fixed-size draw is visible.</para>
    /// </summary>
    public static DocumentCaptions RenderDocumentForTest(
        FormDocument document, FormControl? selected, FormControl? typeHereHost, Size viewport)
    {
        var canvas = new FormCanvasControl
        {
            Document = document, SelectedControl = selected, TypeHereHost = typeHereHost
        };
        canvas._captionLog = new List<CaptionRecord>();
        canvas.Measure(viewport);
        canvas.Arrange(new Rect(viewport));

        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(viewport.Width), (int)Math.Ceiling(viewport.Height)), new Vector(96, 96));
        bitmap.Render(canvas);

        return new DocumentCaptions(canvas._transform.Zoom, canvas._captionLog);
    }

    /// <summary>The one shape a probing canvas draws — see <see cref="RenderSchematicForTest"/>.</summary>
    private sealed record SchematicProbe(FormSchematic Schematic, Rect Bounds, string Label);

    /// <summary>
    /// What one probe render reported back.
    ///
    /// <para>⛔ A REFERENCE wrapper around the nullable origin, and that is the whole point: it makes
    /// "the probe never ran" (this object is null) distinguishable from "the probe ran and drew no
    /// caption" (this object holds a null <c>CaptionOrigin</c>). Collapse the two into a bare
    /// <c>Point?</c> field and a probe branch that stopped executing — an early return added above it
    /// in <see cref="Render"/>, say — reads as null, which is exactly what a BAND is asserted to
    /// report. Every band pin would then pass by absence, and the bands are the arms this gate was
    /// built for.</para>
    /// </summary>
    private sealed record SchematicProbeResult(Point? CaptionOrigin);

    /// <summary>
    /// Set by <see cref="RenderSchematicForTest"/> alone. Honoured only when non-null: a canvas that
    /// was never handed one renders a document exactly as it always did.
    /// </summary>
    private SchematicProbe? _schematicOverride;

    /// <summary>
    /// Written by <see cref="Render"/>'s probe branch only, read by <see cref="RenderSchematicForTest"/>
    /// only. Stays null on any canvas that was never handed a probe.
    /// </summary>
    private SchematicProbeResult? _probeResult;

    /// <summary>
    /// One probe render's observable result: the pixels, hashed, and where the caption landed.
    /// </summary>
    /// <param name="Hash">SHA-256 of the rendered frame — the DISTINCTNESS gate.</param>
    /// <param name="CaptionOrigin">
    /// The exact point the caption was drawn at, or null when the render drew none (a band, an empty
    /// label, or a rectangle too small for text) — the POSITION gate a hash cannot provide.
    /// </param>
    public sealed record SchematicFrame(string Hash, Point? CaptionOrigin);

    /// <summary>
    /// Renders ONE <see cref="FormSchematic"/> at one rectangle with one label, through the real
    /// control and the real <see cref="DrawSchematic"/> seam, and returns both a hash of the pixels
    /// and the origin the caption was drawn at.
    ///
    /// <para>⛔ Two facts, because a hash alone cannot pin caption POSITION: frames are compared for
    /// DISTINCTNESS, so a caption that moves still hashes uniquely and every existing pin stays
    /// green. <see cref="SchematicFrame.CaptionOrigin"/> is the coordinate the arms actually used —
    /// null for the three bands, which draw no caption at all.</para>
    ///
    /// <para>⛔ PUBLIC, not internal: the Shell grants no <c>InternalsVisibleTo</c> to the test
    /// project, by convention (public seams, never internal + IVT — see
    /// <c>CodeEditorDocumentView.axaml.cs:770</c>). <c>FormSchematicPinTests</c> drives every enum
    /// value through this and requires the frames ALL PAIRWISE DISTINCT, which is the only gate an
    /// item schematic can have: it has no catalog row and no document to be rendered from.</para>
    ///
    /// <para>⚠ A <see cref="RenderTargetBitmap"/> rather than a headless <c>Window</c> and
    /// <c>CaptureRenderedFrame</c>: that extension lives in <c>Avalonia.Headless</c>, which this
    /// project does not reference and MUST NOT — it is a test platform, and referencing it here
    /// would ship it inside the IDE. <c>RenderTargetBitmap</c> is base Avalonia and renders the same
    /// visual through the same Skia backend. Caller must be on the dispatcher thread with a
    /// rendering platform up, i.e. under <c>[AvaloniaTest]</c> with Skia — which is also true of
    /// every other pixel test here.</para>
    /// </summary>
    public static SchematicFrame RenderSchematicForTest(FormSchematic schematic, Rect bounds, string label)
    {
        // Big enough to hold the requested rectangle whatever it is, with a margin, so a shape that
        // draws slightly outside its bounds still lands in the hash instead of being cropped away.
        var size = new Size(
            Math.Max(1, Math.Ceiling(bounds.Right) + 20),
            Math.Max(1, Math.Ceiling(bounds.Bottom) + 20));

        var canvas = new FormCanvasControl();
        canvas._schematicOverride = new SchematicProbe(schematic, bounds, label);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));

        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        bitmap.Render(canvas);

        // ⛔ Refuse rather than report null. `bitmap.Render` is what drives Render, and if the probe
        // branch there ever stops running, a null origin is indistinguishable from the null a BAND
        // legitimately reports — so every band assertion would pass by absence while measuring
        // nothing. That is the failure this whole seam was added to remove, so it must not be the
        // thing the seam quietly does.
        if (canvas._probeResult is not { } result)
        {
            throw new InvalidOperationException(
                $"The probe branch of {nameof(Render)} never ran for {schematic}, so no caption " +
                "origin was recorded. The frame hash would still be returned and every band's " +
                "expected-null assertion would pass while measuring nothing — refusing instead.");
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return new SchematicFrame(
            Convert.ToHexString(SHA256.HashData(stream.ToArray())), result.CaptionOrigin);
    }

    /// <summary>
    /// A three-line arrowhead, the Win95 way of drawing one.
    ///
    /// <para>⚠ Three stacked lines rather than a filled triangle: a <c>StreamGeometry</c> per
    /// chevron allocates on every render, and at this size the two are indistinguishable.</para>
    /// </summary>
    private static void Chevron(
        DrawingContext context, Point centre, bool pointingDown, bool sideways = false)
    {
        for (var i = 0; i < 3; i++)
        {
            var spread = 3 - i;
            if (sideways)
            {
                var x = centre.X - 1 + i;
                context.DrawLine(DarkShadowPen,
                    new Point(x, centre.Y - spread), new Point(x, centre.Y + spread));
            }
            else
            {
                var y = pointingDown ? centre.Y - 1 + i : centre.Y + 1 - i;
                context.DrawLine(DarkShadowPen,
                    new Point(centre.X - spread, y), new Point(centre.X + spread, y));
            }
        }
    }

    private static FormattedText Text(string text, IBrush brush) =>
        Text(text, brush, FormCanvasTransform.CaptionFontSize);

    private static FormattedText Text(string text, IBrush brush, double size) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default),
        size,
        brush);

    /// <summary>
    /// The Type Here caption — the ONE piece of text on this canvas that is not a control's own, so
    /// the one that is allowed a face of its own. Italic and grey: a slot is a prompt, not a thing
    /// the document contains, and drawing it in the same ink as a real menu item would claim the
    /// form has an item called "Type Here".
    /// </summary>
    private static FormattedText SlotText(double size) => new(
        FormCanvasTransform.TypeHereCaption,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default, FontStyle.Italic),
        size,
        SlotInkBrush);

    // ── The classic Win95/98 system palette, which is what a VB6 form designer IS ────────────
    //
    // ⚠ Deliberately NOT theme-aware, and that matches VB6: the thing being designed is a Windows
    // window, so it keeps window colours whatever colour the IDE around it is. The canvas MARGIN
    // follows the IDE theme; the form does not.
    //
    // ⛔ Still a schematic (D-WYSIWYG). Classic chrome makes the kinds recognisable at a glance — a
    // raised Button, a sunken TextBox — but nothing here consults the user's real theme, fonts, DPI
    // or control styles, and none of it runs. F5 to the real target is still the only renderer.
    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xD0, 0xC8));
    private static readonly IBrush WindowBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IBrush TitleBarBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
    private static readonly IBrush CaptionBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    // The four edge tones every piece of Win95 chrome is built from: white and face on the lit
    // edges, shadow and dark shadow on the unlit ones. Raised and sunken are the same four pens in
    // the opposite order, which is why Bevel takes a bool rather than having two copies.
    private static readonly IPen HighlightPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly IPen FacePen = new Pen(new SolidColorBrush(Color.FromRgb(0xD4, 0xD0, 0xC8)));
    private static readonly IPen ShadowPen = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
    private static readonly IPen DarkShadowPen = new Pen(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)));

    /// <summary>Black, for the glyphs inside title-bar buttons. Chrome tones are too pale to read at 14px.</summary>
    private static readonly IPen GlyphPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)));

    /// <summary>
    /// A control's own <c>BackColor</c>/<c>ForeColor</c> when it sets one, else null.
    ///
    /// <para>⛔ Without this the canvas drew classic chrome and IGNORED the colours the property
    /// grid had just written: setting BackColor changed the document, the generated code and the
    /// running program, and nothing at all on screen — which reads as "setting BackColor does not
    /// work". The value is the document's own text, so it parses the same "#rrggbb" / "#rgb" /
    /// bare-name vocabulary the catalog accepts.</para>
    ///
    /// <para>⚠ An unparseable value returns null and the control keeps its system colour, rather
    /// than falling back to black or throwing. D9 already FREEZES such a value in the grid with its
    /// reason; the canvas's job is to not make it worse.</para>
    /// </summary>
    private static IBrush? ControlColour(FormControl control, string property) =>
        control.Properties.TryGetValue(property, out var text)
        && !string.IsNullOrWhiteSpace(text)
        && Color.TryParse(text, out var colour)
            ? new SolidColorBrush(colour)
            : null;

    /// <summary>The alignment grid's dots — VB6's single most recognisable detail.</summary>
    private static readonly IBrush GridDotBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));

    /// <summary>Web cell guides. Not a VB6 idea, and deliberately still diagrammatic.</summary>
    private static readonly IPen GridPen = new Pen(
        new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)), dashStyle: DashStyle.Dash);

    /// <summary>
    /// The item rules inside a ListBox and the cross inside a PictureBox.
    ///
    /// <para>⚠ Mid grey, not light grey. These sit on WHITE, and at #C0C0C0 they were technically
    /// drawn and effectively invisible — a short ListBox read as a blank box indistinguishable from
    /// a TextBox, which is the thing the rules exist to prevent.</para>
    /// </summary>
    private static readonly IPen RowPen = new Pen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)));

    // ⚠ VB6 marks a selection with HANDLES ALONE — no outline. Solid navy squares, which read
    // against the form face and a white control interior alike.
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80));

    /// <summary>
    /// The outline on a secondary member of a multi-selection: the primary's navy, dashed so the two
    /// are distinguishable at a glance without a second colour to learn.
    /// </summary>
    private static readonly IPen SecondarySelectionPen = new Pen(
        new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80)), 1,
        new DashStyle(new double[] { 3, 3 }, 0));

    /// <summary>The rubber band: a faint wash so controls stay readable underneath it.</summary>
    private static readonly IBrush MarqueeBrush =
        new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x80));

    private static readonly IPen MarqueePen = new Pen(
        new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80)), 1,
        new DashStyle(new double[] { 2, 2 }, 0));
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)));

    /// <summary>The Type Here caption's ink — grey, so the prompt never reads as a real item.</summary>
    private static readonly IBrush SlotInkBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

    /// <summary>
    /// An idle Type Here slot's border: dashed and grey, the same "this is not a thing yet"
    /// vocabulary the rubber band and a secondary selection are drawn in.
    /// </summary>
    private static readonly IPen SlotPen = new Pen(
        new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 1,
        new DashStyle(new double[] { 2, 2 }, 0));

    /// <summary>
    /// The slot being edited: a SOLID selection-coloured border, so the one slot a keystroke will
    /// land in is distinguishable at a glance from the other slot that can be visible at the same
    /// time (a strip's own and an open item's dropdown are both on screen).
    /// </summary>
    private static readonly IPen ActiveSlotPen = new Pen(
        new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80)));

    /// <summary>Side of a CheckBox tick or RadioButton bullet, before it is clamped to the bounds.</summary>
    private const double GlyphSide = 13;

    /// <summary>Form units between alignment dots — VB6's default grid.</summary>
    private const double GridStep = 8;

    /// <summary>
    /// Rounds a form-space coordinate to the alignment grid.
    ///
    /// <para>⛔ The canvas DREW the grid and snapped to nothing, so the dots were decoration and two
    /// controls dropped side by side were one or two pixels out of line with no way to tell. A grid
    /// you can see and cannot feel is worse than no grid: it implies an alignment the document does
    /// not have.</para>
    ///
    /// <para>⚠ <b>Alt bypasses it</b>, which is the convention in both VB6 and VS. Without an
    /// escape the designer cannot express a deliberate odd offset at all, and "snap" becomes
    /// "you may not".</para>
    /// </summary>
    private static int Snap(double value, KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Alt)
            ? (int)Math.Round(value)
            : (int)(Math.Round(value / GridStep) * GridStep);

    /// <summary>
    /// Canvas height of the drawn title bar. FIXED, not scaled with the form: it is window chrome
    /// rather than part of the form's own coordinate space, and a scaled one becomes an illegible
    /// smear at low zoom.
    /// </summary>
    private const double TitleBarHeight = 18;

    // One each, for the life of the type — see UpdateCursor.
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor WestEastCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor NorthSouthCursor = new(StandardCursorType.SizeNorthSouth);
}

/// <summary>
/// A toolbox control dropped on the canvas, in FORM coordinates.
///
/// <para>⚠ Form space, not canvas pixels — the canvas has already applied its transform. A request
/// carrying canvas pixels would place controls correctly at 100% zoom and wrongly at every other,
/// which is the class of bug that only shows up on someone else's monitor.</para>
/// </summary>
public sealed record FormControlDropRequest(string Kind, int X, int Y);
