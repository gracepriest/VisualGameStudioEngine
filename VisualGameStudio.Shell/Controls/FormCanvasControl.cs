using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BasicLang.Forms;

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

        // The SAME transform the last Render used, so selection cannot disagree with the picture.
        SelectedControl = _transform.HitTest(document, e.GetPosition(this));
        e.Handled = true;
    }

    // ==================================================================
    // Render
    // ==================================================================

    public override void Render(DrawingContext context)
    {
        base.Render(context);

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
}
