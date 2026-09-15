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
/// <para>⚠ <b>Not visually verified.</b> <see cref="FormCanvasTransform"/> — the mapping shared by
/// rendering, hit-testing and selection — is covered by tests, because a drift there has no visual
/// symptom. What the canvas LOOKS like is checked by running the IDE and nothing else; no test here
/// claims otherwise.</para>
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

    public FormCanvasControl()
    {
        // ⚠ These two handlers are on THIS control's own attached routed events — they live and die
        // with the instance and are not the external subscription the note below warns about. The
        // canvas still holds no handler on anything it does not own.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// The one mapping, shared by <see cref="Render"/> and <see cref="OnPointerPressed"/>.
    ///
    /// <para>⛔⛔ Never re-derive this inline. <c>MinimapControl</c> hand-duplicates its own
    /// transform at three sites and the copies drifted; the symptom there — and here — is that the
    /// picture looks right and the clicks land somewhere else.</para>
    /// </summary>
    private FormCanvasTransform _transform = new();

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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var document = Document;
        if (document == null)
        {
            return;
        }

        var point = e.GetPosition(this);

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

        if (handle == FormResizeHandle.None)
        {
            // The SAME transform the last Render used, so selection cannot disagree with the picture.
            SelectedControl = _transform.HitTest(document, point);
        }

        if (SelectedControl?.Geometry is PixelGeometry pixel)
        {
            _dragHandle = handle;
            _dragOrigin = point;
            _dragStart = (pixel.X, pixel.Y, pixel.Width, pixel.Height);
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
        if (_dragHandle == FormResizeHandle.None)
        {
            changed = FormGeometryEdit.MoveTo(document, control, start.X + dx, start.Y + dy);
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

        var wasDragging = _dragOrigin != null;
        _dragOrigin = null;
        _dragHandle = FormResizeHandle.None;
        e.Pointer.Capture(null);

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
        if (SelectedControl == null || CanvasBoundsOf(document, SelectedControl) is not { } bounds)
        {
            return FormResizeHandle.None;
        }

        return FormCanvasTransform.HandleAt(bounds, point);
    }

    /// <summary>A control's rectangle in CANVAS space, or null when it has no pixel geometry.</summary>
    private Rect? CanvasBoundsOf(FormDocument document, FormControl control)
    {
        foreach (var (candidate, bounds) in FormCanvasTransform.Layout(document))
        {
            if (ReferenceEquals(candidate, control))
            {
                return _transform.ToCanvas(bounds);
            }
        }

        return null;
    }

    private Point? _dragOrigin;
    private FormResizeHandle _dragHandle;
    private (int X, int Y, int Width, int Height) _dragStart;
    private bool _dragChanged;

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
        var point = _transform.ToForm(e.GetPosition(this));
        var request = new FormControlDropRequest(kind, (int)Math.Round(point.X), (int)Math.Round(point.Y));

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
        if (SelectedControl != null && CanvasBoundsOf(document, SelectedControl) is { } selection)
        {
            DrawHandles(context, selection);
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
    /// </summary>
    private static FormCanvasTransform Fit(FormDocument document, Size viewport)
    {
        var width = document.Width is > 0 ? document.Width.Value : 400;
        var height = document.Height is > 0 ? document.Height.Value : 300;

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

    private void DrawSurface(DrawingContext context, FormDocument document)
    {
        var width = document.Width is > 0 ? document.Width.Value : 400;
        var height = document.Height is > 0 ? document.Height.Value : 300;

        var surface = _transform.ToCanvas(new Rect(0, 0, width, height));
        context.DrawRectangle(SurfaceBrush, SurfacePen, surface);

        var caption = document.Text ?? document.Name;
        if (!string.IsNullOrEmpty(caption))
        {
            context.DrawText(
                Text(caption, CaptionBrush),
                new Point(surface.X + 6, Math.Max(0, surface.Y - 18)));
        }
    }

    private void DrawControl(DrawingContext context, FormControl control, Rect bounds)
    {
        var selected = ReferenceEquals(control, SelectedControl);
        context.DrawRectangle(ControlBrush, selected ? SelectionPen : ControlPen, bounds);

        // The control's own Text if it has one, else its id — a box with no label is unidentifiable
        // on a schematic, which is the one thing the canvas has to get right.
        var label = control.Properties.TryGetValue("Text", out var text) && !string.IsNullOrEmpty(text)
            ? text
            : control.Id;

        if (string.IsNullOrEmpty(label) || bounds.Width < 12 || bounds.Height < 10)
        {
            return;
        }

        using (context.PushClip(bounds))
        {
            context.DrawText(Text(label, LabelBrush), new Point(bounds.X + 4, bounds.Y + 2));
        }
    }

    private static FormattedText Text(string text, IBrush brush) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily.Default),
        12,
        brush);

    // Deliberately flat and obviously diagrammatic — see the "schematic, not a preview" note above.
    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
    private static readonly IPen SurfacePen = new Pen(new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6F)));
    private static readonly IBrush ControlBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));
    private static readonly IPen ControlPen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x8F)));
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)), 2);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly IBrush CaptionBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB8));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)));

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
