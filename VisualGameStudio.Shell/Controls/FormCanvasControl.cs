using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
            DocumentProperty, SelectedControlProperty, ModelRevisionProperty);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

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

        var control = _transform.HitTest(document, e.GetPosition(this));
        if (control == null)
        {
            return;
        }

        SelectedControl = control;

        var command = ActivateControlCommand;
        if (command?.CanExecute(control) == true)
        {
            command.Execute(control);
        }

        e.Handled = true;
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

        // ⚠ Left button only. Arming a drag on a right-click means the context menu gesture also
        // moves the control the user was about to right-click.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            SelectedControl = _transform.HitTest(document, point);
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
            // The SAME transform the last Render used, so selection cannot disagree with the picture.
            var hit = _transform.HitTest(document, point);
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

            ApplyClickSelection(hit, extend);
        }

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
    /// </summary>
    private static Rect? FormBoundsOf(FormDocument document, FormControl control)
    {
        foreach (var (candidate, bounds) in FormCanvasTransform.Layout(document))
        {
            if (ReferenceEquals(candidate, control))
            {
                return bounds;
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

        var document = Document;
        if (document == null)
        {
            return;
        }

        _transform = Fit(document, Bounds.Size);

        DrawSurface(context, document);

        // ⚠ Containers before their children — Layout guarantees that order, and drawing a
        // container after its children would paint over them.
        foreach (var (control, bounds) in FormCanvasTransform.Layout(document))
        {
            DrawControl(context, control, _transform.ToCanvas(bounds));
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
    private void DrawControl(DrawingContext context, FormControl control, Rect bounds)
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
        // on a schematic, which is the one thing the canvas has to get right.
        var label = control.Properties.TryGetValue("Text", out var text) && !string.IsNullOrEmpty(text)
            ? text
            : control.Id;

        // Where the label goes once the shape has had its say: indented past a tick or a bullet,
        // centred in a button, at the top-left of everything else.
        var labelOrigin = new Point(bounds.X + 4, bounds.Y + 2);
        var tooSmallForText = bounds.Width < 12 || bounds.Height < 10;

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

            default:
                // A TextBox and anything text-entry shaped: white client, sunken edge.
                context.FillRectangle(client, bounds);
                Bevel(context, bounds, raised: false);
                break;
        }

        if (string.IsNullOrEmpty(label) || tooSmallForText)
        {
            return;
        }

        using (context.PushClip(bounds))
        {
            context.DrawText(Text(label, ink), labelOrigin);
        }
    }

    private static FormattedText Text(string text, IBrush brush) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default),
        12,
        brush);

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
