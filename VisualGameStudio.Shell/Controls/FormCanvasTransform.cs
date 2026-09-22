using Avalonia;
using BasicLang.Forms;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Shell.Controls;

/// <summary>
/// The <b>one</b> mapping between form coordinates and canvas pixels, used by rendering,
/// hit-testing and selection alike.
///
/// <para>⛔⛔ <b>One object, deliberately — this is the whole reason the type exists.</b>
/// <c>MinimapControl</c> hand-duplicates its forward/inverse transform at three separate sites
/// (<c>MinimapControl.axaml.cs</c> around <c>:386</c>, <c>:439</c> and <c>:835</c>), and the copies
/// have already drifted. A drifted copy here does not look broken: the form renders perfectly and
/// clicks land on the wrong control, or on nothing, and only at some zoom levels. There is no
/// visual symptom to notice and no exception to catch, which is exactly why the mapping is one
/// object with tests rather than three expressions with none.</para>
///
/// <para>Immutable. Zooming or panning produces a new transform, so a half-updated one cannot be
/// observed by a render pass that is already running.</para>
/// </summary>
public sealed class FormCanvasTransform
{
    /// <summary>Below this, a form is a smudge; above it, one pixel of form is a tile.</summary>
    public const double MinZoom = 0.1;
    public const double MaxZoom = 8.0;

    public FormCanvasTransform(double zoom = 1.0, Vector pan = default)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        Pan = pan;
    }

    /// <summary>Canvas pixels per form pixel. Never zero — dividing by it is the inverse map.</summary>
    public double Zoom { get; }

    /// <summary>Canvas-space offset of the form's origin.</summary>
    public Vector Pan { get; }

    public FormCanvasTransform WithZoom(double zoom) => new(zoom, Pan);

    public FormCanvasTransform WithPan(Vector pan) => new(Zoom, pan);

    /// <summary>
    /// Zooms about a fixed canvas point — the gesture a mouse wheel performs.
    ///
    /// <para>⚠ The point under the cursor must not move. Zooming about the origin instead is the
    /// classic mistake: the form slides away under the pointer and the user chases it.</para>
    /// </summary>
    public FormCanvasTransform ZoomAbout(Point canvasAnchor, double zoom)
    {
        var formAnchor = ToForm(canvasAnchor);
        var zoomed = new FormCanvasTransform(zoom, Pan);

        // Re-pan so formAnchor maps back to canvasAnchor under the new zoom.
        var moved = zoomed.ToCanvas(formAnchor);
        return zoomed.WithPan(zoomed.Pan + (canvasAnchor - moved));
    }

    // ==================================================================
    // The mapping
    // ==================================================================

    public Point ToCanvas(Point form) => new((form.X * Zoom) + Pan.X, (form.Y * Zoom) + Pan.Y);

    public Point ToForm(Point canvas) => new((canvas.X - Pan.X) / Zoom, (canvas.Y - Pan.Y) / Zoom);

    public Rect ToCanvas(Rect form) => new(ToCanvas(form.TopLeft), new Size(form.Width * Zoom, form.Height * Zoom));

    public Rect ToForm(Rect canvas) => new(ToForm(canvas.TopLeft), new Size(canvas.Width / Zoom, canvas.Height / Zoom));

    // ==================================================================
    // Hit-testing — the same mapping, so it cannot disagree with what was drawn
    // ==================================================================

    /// <summary>
    /// The control under <paramref name="canvasPoint"/>, or null.
    ///
    /// <para>⛔ <b>Topmost wins, and topmost is the LAST match in document order</b> — document
    /// order is z-order, so a later sibling paints over an earlier one. Returning the first match
    /// makes overlapping controls unselectable from the front, which reads as "the canvas ignores
    /// my clicks" rather than as an ordering bug.</para>
    ///
    /// <para>⛔ A child's stored X/Y is relative to ITS CONTAINER, not to the form. Taking those
    /// numbers as absolute puts every control inside a Panel at the wrong place the moment the
    /// Panel is not at the origin — and the catalog ships Panel and GroupBox as containers, so this
    /// is reachable from ordinary use rather than a corner case. <see cref="Layout"/> is what adds
    /// the container origins up, which is the reason this reads Layout rather than the model.</para>
    /// </summary>
    /// <param name="selected">
    /// The designer's selection, forwarded to <see cref="Layout"/>, which decides from it which
    /// dropdowns are open and where the Type Here slot sits (Task 20). Threaded through so the hit
    /// test cannot read a DIFFERENT picture from the one the render pass paints: a click would
    /// otherwise select a menu item the canvas is not showing, or miss one it is.
    /// </param>
    public FormControl? HitTest(FormDocument document, Point canvasPoint, FormControl? selected = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var formPoint = ToForm(canvasPoint);

        // ⛔⛔ BOTH targets read Layout now — the recursive BoundsOf walk that used to serve WinForms
        // is gone. It could not see a BAND: a strip carries no pixel geometry, so the walk skipped it
        // and a click anywhere on a menu bar the canvas had just painted selected nothing at all.
        // Rebuilding the band rule inside a second walk is the drift this class exists to prevent,
        // so the WinForms branch was deleted rather than taught about strips.
        //
        // ⚠ ONE measured behaviour change, and it is the honest one: Layout yields child rects
        // UNCLIPPED, while the recursive walk tested a container's own bounds before ever looking
        // inside it. A child overflowing its Panel is now hittable where it is PAINTED, rather than
        // being unreachable in the part of itself that hangs outside its parent.
        return Layout(document, selected)
            .Where(entry => entry.Control != null && entry.Bounds.Contains(formPoint))
            .Select(entry => entry.Control)
            .LastOrDefault();
    }

    /// <summary>
    /// The host whose Type Here slot is under <paramref name="canvasPoint"/>, or null — the empty
    /// cell at the end of a strip's or a dropdown's items, which a click turns into an editor.
    ///
    /// <para>⛔⛔ <b>The exact twin of <see cref="HitTest"/>, deliberately: same signature shape,
    /// same coordinate space, same <c>ToForm</c>, same LAST-match rule over the same
    /// <see cref="Layout"/>.</b> The two are called back-to-back on ONE point in
    /// <c>OnPointerPressed</c>, and the drift they would otherwise suffer has no symptom. Make this
    /// one take FORM coordinates while its sibling takes CANVAS coordinates, and any later edit
    /// that drops the <c>ToForm</c> on one of those two call sites produces a slot lookup that is
    /// exactly right at 1:1 zoom and wrong at every other zoom — and the headless tests run at
    /// identity zoom, where <c>ToForm</c> IS the identity function, so nothing here can see it. Two
    /// lookups over one picture is the defect class this whole type exists to prevent (see its own
    /// summary); keeping the siblings identical in shape is what stops them drifting apart.</para>
    ///
    /// <para>⛔ LAST match, for <see cref="HitTest"/>'s reason: document order is z-order, and a
    /// nested dropdown's slot is yielded after anything it is painted over.</para>
    /// </summary>
    /// <param name="selected">
    /// The designer's selection, forwarded to <see cref="Layout"/>. A slot exists ONLY for the
    /// active strip and for the hosts the selection expands, so with nothing selected this always
    /// returns null — there is no slot for any point to land in.
    /// </param>
    public FormControl? TypeHereAt(FormDocument document, Point canvasPoint, FormControl? selected = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var formPoint = ToForm(canvasPoint);

        return Layout(document, selected)
            .Where(entry => entry.Role == FormLayoutRole.TypeHere && entry.Bounds.Contains(formPoint))
            .Select(entry => entry.Host)
            .LastOrDefault();
    }

    /// <summary>
    /// Every control a rubber-band selection covers. <paramref name="formRect"/> is in FORM units.
    ///
    /// <para>⚠ INTERSECTS rather than contains, which is what VS does: dragging a band across a row
    /// of controls selects them without having to enclose the widest one completely.</para>
    ///
    /// <para>⛔⛔ <b>A container and its own descendants are never both returned.</b> A band dragged
    /// across a Panel intersects the Panel AND everything inside it, and selecting both is actively
    /// harmful: a group move would translate the Panel — which carries its children — and then
    /// translate each child again, so they would travel twice as far as the container they live in.
    /// The container wins, because that is what the user drew a band around.</para>
    ///
    /// <para>⚠ Reads <see cref="Layout"/>, so it inherits the one authority on where controls are
    /// and works for both a pixel form and a web page's cells.</para>
    /// </summary>
    public static IReadOnlyList<FormControl> ControlsIn(FormDocument document, Rect formRect)
    {
        ArgumentNullException.ThrowIfNull(document);

        // ⛔ By PLACE, never by "Layout does not yield it yet". A marquee dragged across the form
        // covers the menu bar's band and (from Task 20) its item cells, and VS band-selects
        // positioned controls only — a bar or a menu item is not something a rubber band picks up,
        // and a group move would have nowhere to move it to.
        var hit = Layout(document)
            .Where(entry => entry.Control != null &&
                            entry.Control.Definition?.Place is not (FormPlace.Item or FormPlace.Docked) &&
                            entry.Bounds.Intersects(formRect))
            .Select(entry => entry.Control!)
            .ToList();

        if (hit.Count < 2)
        {
            return hit;
        }

        // Anything whose ancestor is also in the band is dropped — the ancestor already carries it.
        var covered = new HashSet<FormControl>(
            hit.SelectMany(c => c.Children.SelectMany(child => child.SelfAndDescendants())));

        return hit.Where(c => !covered.Contains(c)).ToList();
    }

    /// <summary>
    /// The form's drawable area in FORM units — its client size, or a default for a page that has
    /// no intrinsic one.
    ///
    /// <para>⛔ ONE authority, because four things have to agree about it: the rectangle the canvas
    /// draws, the transform that fits it to the viewport, the clamp that keeps controls on it, and
    /// the grid a web page's cells are computed from. A second copy that drifts puts the grid lines
    /// somewhere other than the page they are supposed to divide — and a drop then lands in a cell
    /// the user did not aim at, with nothing on screen looking wrong.</para>
    /// </summary>
    public static Size SurfaceSize(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Size(
            document.Width is > 0 ? document.Width.Value : 400,
            document.Height is > 0 ? document.Height.Value : 300);
    }

    /// <summary>
    /// Half the side of a resize handle's grab area, in CANVAS pixels. The handle straddles the
    /// border, so this much of it lies outside the control and this much inside.
    /// </summary>
    public const double HandleReach = 4;

    /// <summary>
    /// Which handle of <paramref name="canvasBounds"/> the point grabbed, or
    /// <see cref="FormResizeHandle.None"/> for the body (a move) or a miss.
    ///
    /// <para>⛔⛔ Takes CANVAS coordinates, and that is the whole design. A grab area measured in
    /// FORM pixels shrinks with the form: the canvas zooms to fit, so an 800x450 form in a docked
    /// panel draws at well under 1:1 and a handle sized in form pixels becomes one or two physical
    /// pixels. The control silently stops being resizable, with nothing on screen to explain why
    /// and nothing failing anywhere. In canvas space the grab area is the same size on screen at
    /// every zoom.</para>
    ///
    /// <para>⚠ Corners are tested before edges. They overlap, and a corner is the more specific
    /// gesture — testing edges first makes the corners unreachable, so a control can be stretched
    /// in one direction at a time but never scaled.</para>
    /// </summary>
    public static FormResizeHandle HandleAt(Rect canvasBounds, Point canvasPoint)
    {
        var onLeft = Math.Abs(canvasPoint.X - canvasBounds.X) <= HandleReach;
        var onRight = Math.Abs(canvasPoint.X - canvasBounds.Right) <= HandleReach;
        var onTop = Math.Abs(canvasPoint.Y - canvasBounds.Y) <= HandleReach;
        var onBottom = Math.Abs(canvasPoint.Y - canvasBounds.Bottom) <= HandleReach;

        var withinX = canvasPoint.X >= canvasBounds.X - HandleReach &&
                      canvasPoint.X <= canvasBounds.Right + HandleReach;
        var withinY = canvasPoint.Y >= canvasBounds.Y - HandleReach &&
                      canvasPoint.Y <= canvasBounds.Bottom + HandleReach;

        if (!withinX || !withinY)
        {
            return FormResizeHandle.None;
        }

        return (onLeft, onRight, onTop, onBottom) switch
        {
            (true, _, true, _) => FormResizeHandle.TopLeft,
            (_, true, true, _) => FormResizeHandle.TopRight,
            (true, _, _, true) => FormResizeHandle.BottomLeft,
            (_, true, _, true) => FormResizeHandle.BottomRight,
            (true, _, _, _) => FormResizeHandle.Left,
            (_, true, _, _) => FormResizeHandle.Right,
            (_, _, true, _) => FormResizeHandle.Top,
            (_, _, _, true) => FormResizeHandle.Bottom,
            _ => FormResizeHandle.None
        };
    }

    /// <summary>
    /// The innermost CONTAINER a drop at <paramref name="formPoint"/> lands in, with its origin in
    /// form space — or null for the form surface itself.
    ///
    /// <para>⛔⛔ Lives here, beside <see cref="HitTest"/>, and walks the tree the same way for the
    /// same reason the transform is one object: if placement decided containment differently from
    /// selection, you would drop a Button onto a Panel the canvas agrees you are over and it would
    /// land on the form behind it — or worse, the reverse. Two rules, one picture, no symptom.</para>
    ///
    /// <para>⚠ A NON-container under the point swallows it: dropping on a Button means dropping on
    /// the form at that spot, not into whatever sits behind the Button. Walking past it to a
    /// container underneath would nest controls into a Panel the user cannot see at that point.</para>
    /// </summary>
    /// <param name="ignore">
    /// A control to skip, together with everything inside it.
    ///
    /// <para>⛔⛔ Two separate reasons, both load-bearing while DRAGGING. First, the pointer is over
    /// the dragged control — that is what dragging is — so without skipping it a Button swallows
    /// its own point and reports "no container here", and nothing can ever be dragged into
    /// anything. Second, and worse: a Panel whose target is itself or one of its own descendants
    /// makes a LOOP in the tree, and every walker in this feature recurses — the writer, the canvas
    /// layout, the region writer, <c>AllControls</c>. That is a stack overflow that takes the IDE
    /// down with no diagnostic, so the illegal targets are excluded from the search rather than
    /// detected after the fact.</para>
    /// </param>
    public static (FormControl Container, Point Origin)? ContainerAt(
        FormDocument document, Point formPoint, FormControl? ignore = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ContainerAt(document.Controls, formPoint, new Point(0, 0), ignore);
    }

    private static (FormControl, Point)? ContainerAt(
        IReadOnlyList<FormControl> controls, Point formPoint, Point containerOrigin, FormControl? ignore)
    {
        // Topmost first, exactly as HitTest does — document order is z-order.
        for (var i = controls.Count - 1; i >= 0; i--)
        {
            var control = controls[i];
            if (ReferenceEquals(control, ignore))
            {
                continue;
            }

            // ⛔ A strip is never a drop target. It has no pixel geometry, so BoundsOf below would
            // skip it today anyway — but that is an accident of the model, not the rule. Stated
            // here, dropping on the menu bar lands on the FORM behind it, which is what the band
            // being page chrome means; left implicit, the day a strip acquires bounds the canvas
            // would start nesting Buttons inside a MenuStrip with nothing to explain it.
            if (control.Definition?.Place == FormPlace.Docked)
            {
                continue;
            }

            var bounds = BoundsOf(control, containerOrigin);
            if (bounds == null || !bounds.Value.Contains(formPoint))
            {
                continue;
            }

            if (control.Definition?.IsContainer != true)
            {
                return null;
            }

            var nested = ContainerAt(control.Children, formPoint, bounds.Value.TopLeft, ignore);
            return nested ?? (control, bounds.Value.TopLeft);
        }

        return null;
    }

    /// <summary>
    /// A control's rectangle in FORM space, or null when it carries no pixel geometry.
    ///
    /// <para>⚠ Null rather than an empty rect: a control with no geometry has no position, and an
    /// empty rect at the origin would silently make it a zero-size target sitting in the corner.
    /// Web documents use grid geometry, which this canvas does not place.</para>
    /// </summary>
    public static Rect? BoundsOf(FormControl control, Point containerOrigin)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control.Geometry is not PixelGeometry pixel)
        {
            return null;
        }

        return new Rect(
            containerOrigin.X + pixel.X,
            containerOrigin.Y + pixel.Y,
            Math.Max(0, pixel.Width),
            Math.Max(0, pixel.Height));
    }

    /// <summary>
    /// Everything the canvas places, in paint order: every positioned control with its FORM-space
    /// rectangle (containers before their children), then each strip's band, that strip's item
    /// CELLS, its Type Here slot when it is active — and finally every open DROPDOWN.
    /// </summary>
    /// <param name="selected">
    /// The control the designer has selected. It decides which chrome is EDITABLE right now: which
    /// strip is active (so gets a slot), and which hosts are expanded (so paint a dropdown). With
    /// nothing selected the bands and their cells are still laid out — they are always visible —
    /// but no slot and no dropdown exist.
    /// </param>
    public static IEnumerable<FormLayoutEntry> Layout(FormDocument document, FormControl? selected = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        // ⚠ Bands LAST, so a band paints over — and out-hit-tests — anything that overlaps it.
        // Document order is z-order everywhere in this class, and chrome is on top of the surface.
        return document.Target == FormTarget.Web
            ? WebLayout(document, selected)
            : Layout(document.Controls, new Point(0, 0)).Concat(Bands(document, selected));
    }

    /// <summary>
    /// A Docked strip's BAND: the full width of the surface, the catalog row's height, stacked from
    /// whichever edge it docks to.
    ///
    /// <para>⛔ A strip carries NO pixel geometry — its position is its Dock property — so it can
    /// never come through <see cref="BoundsOf"/>. Reusing that would collapse every strip to a 0x0
    /// rect at the origin: a phantom control in the corner with a resize grip nobody dropped, and
    /// the menu bar itself invisible and unclickable, with nothing else on screen looking wrong.</para>
    ///
    /// <para>⛔ Top strips stack DOWNWARD in document order and Bottom strips stack UPWARD from the
    /// edge, so the first-documented bottom bar is the one ON the edge — the same order
    /// <c>FormAssetEmitter.Html</c> emits the page's bottom chrome in (reversed, for the same
    /// reason). Which edge each one is on is <see cref="FormControl.IsDockedToBottom"/>, shared with
    /// that emitter: two copies of that lookup would let this canvas draw the status band on one
    /// edge while the page puts its <c>&lt;footer&gt;</c> on the other, from ONE document.</para>
    ///
    /// <para>⛔⛔ <b>The item CELLS, the Type Here slot and the open dropdowns are yielded from
    /// INSIDE this walk, and that is the whole reason there is no <c>Cells(document, selected)</c>
    /// beside it.</b> This loop uniquely owns where each band SITS — the stacking from each edge is
    /// derived here and nowhere else — and a cell's rectangle is that band's own Y and Height. A
    /// separate method would have to re-derive the stacking from the same document, which is the
    /// <see cref="SurfaceSize"/> / <c>Tracks</c>-vs-<c>ParseTracks</c> / <c>DockOf</c> lesson
    /// arriving a fourth time in this one file. Worse, the drift would be INVISIBLE: every Item row
    /// in the catalog inherits the record's default <c>DefaultHeight</c> of 24, which is exactly
    /// MenuStrip's own band height — so a copy that read the item's height instead of the band's
    /// would be pixel-perfect on a menu bar and silently 1px short on a ToolStrip (25) and 2px tall
    /// on a StatusStrip (22). Hence the rule below, stated once: a cell takes <c>band.Y</c> and
    /// <c>band.Height</c> off the entry this same iteration just produced, and NOTHING here ever
    /// reads an item's own <c>DefaultHeight</c>.</para>
    /// </summary>
    private static IEnumerable<FormLayoutEntry> Bands(FormDocument document, FormControl? selected)
    {
        var surface = SurfaceSize(document);
        double top = 0, bottom = surface.Height;

        var parents = ParentMap(document);
        var activeStrip = ActiveStrip(selected, parents);
        var expanded = ExpansionPath(selected, parents);

        // Where each EXPANDED host was laid out. Filled in as the walk reaches it, never looked up:
        // the outermost expanded host is a band cell produced by the loop just below, and every
        // deeper one is a dropdown row produced by the host before it in `expanded`. So the chain
        // is always populated before it is read, and no rectangle is computed twice.
        var cellOf = new Dictionary<FormControl, Rect>();

        foreach (var strip in document.Controls.Where(c => c.Definition?.Place == FormPlace.Docked))
        {
            var height = strip.Definition!.DefaultHeight;
            Rect band;

            if (strip.IsDockedToBottom)
            {
                bottom -= height;
                band = new Rect(0, bottom, surface.Width, height);
            }
            else
            {
                band = new Rect(0, top, surface.Width, height);
                top += height;
            }

            yield return new FormLayoutEntry(strip, band, FormLayoutRole.Band);

            // The strip's own items, left to right along the band. ⛔ band.Y and band.Height — see
            // the method summary; the item's inherited DefaultHeight agrees with a MenuStrip by
            // coincidence and with nothing else.
            double x = 0;
            foreach (var item in strip.Children)
            {
                var cell = new Rect(x, band.Y, CellWidth(item), band.Height);
                x = cell.Right;

                if (expanded.Contains(item))
                {
                    cellOf[item] = cell;
                }

                yield return new FormLayoutEntry(item, cell, FormLayoutRole.Cell);
            }

            // ⚠ The ACTIVE strip only. A slot on every strip at once would offer three places to
            // type into with no way to say which one a keystroke meant.
            if (ReferenceEquals(strip, activeStrip))
            {
                yield return new FormLayoutEntry(
                    null, new Rect(x, band.Y, TypeHereWidth, band.Height), FormLayoutRole.TypeHere, strip);
            }
        }

        // ⛔ EVERY dropdown entry comes after EVERY band and cell entry, outermost host first.
        // Document order is z-order here and HitTest takes the LAST match, so a dropdown that hangs
        // over the band below it must be later in the sequence than that band — otherwise the bar
        // it drops out of swallows every click on its own open menu.
        foreach (var host in expanded)
        {
            if (!cellOf.TryGetValue(host, out var hostCell))
            {
                // The chain is built from the parent map, so a host is missing only if the document
                // tree and its own strip list disagree. Everything deeper hangs off this one, so
                // there is nothing further to place.
                break;
            }

            // A band cell drops DOWN from its bottom edge; a dropdown row flies out to the RIGHT,
            // which is what a submenu does. The question is the host's own parent, not its depth.
            var origin = parents.TryGetValue(host, out var parent) && parent.Definition?.Place == FormPlace.Docked
                ? new Point(hostCell.X, hostCell.Bottom)
                : new Point(hostCell.Right, hostCell.Y);

            var width = DropdownWidth(host);
            var y = origin.Y;

            foreach (var row in host.Children)
            {
                var rect = new Rect(origin.X, y, width, RowHeight(row));
                y = rect.Bottom;

                if (expanded.Contains(row))
                {
                    cellOf[row] = rect;
                }

                yield return new FormLayoutEntry(row, rect, FormLayoutRole.Cell);
            }

            // A dropdown's slot is as wide as the dropdown, never the Type Here caption's own
            // width: it is the next ROW of that menu, and a narrower one would tear the edge.
            yield return new FormLayoutEntry(
                null, new Rect(origin.X, y, width, ItemRowHeight), FormLayoutRole.TypeHere, host);
        }
    }

    // ==================================================================
    // The schematic metrics — stated ONCE, because the layout and Task 21's drawing must agree
    // ==================================================================

    /// <summary>The caption a Type Here slot is drawn with, and therefore the text it is sized from.</summary>
    public const string TypeHereCaption = "Type Here";

    /// <summary>A cell's padding either side of its caption, in FORM units.</summary>
    private const double CellPadding = 8;

    /// <summary>Nominal width of one character of a cell's caption. The canvas is a schematic, not a
    /// preview — it does not measure the font it will not be rendered in.</summary>
    private const double CharWidth = 7;

    /// <summary>One dropdown row.</summary>
    private const double ItemRowHeight = 22;

    /// <summary>
    /// A separator's extent along the axis its host lays items out on: 6 units WIDE in a horizontal
    /// band, 6 units HIGH as a dropdown row. One constant because it is one thing — the thickness
    /// of a rule — seen from two directions.
    /// </summary>
    private const double SeparatorExtent = 6;

    /// <summary>
    /// A dropdown is never narrower than this, however short its widest item. A menu that hugged
    /// the word "Cut" would be a tooltip, and its Type Here slot would have no room for a caption.
    /// </summary>
    private const double MinDropdownWidth = 80;

    /// <summary>The slot's width in a BAND — derived from the caption, so the two cannot disagree.</summary>
    private static readonly double TypeHereWidth = CellWidth(TypeHereCaption);

    /// <summary>
    /// The schematic width of a caption: padding, 7 units a character, padding.
    ///
    /// <para>⚠ <c>&amp;</c> is counted as a written character rather than stripped as an
    /// accelerator marker. The designer shows what the document SAYS — a user who typed
    /// <c>&amp;File</c> sees <c>&amp;File</c> — and the two rules differ by 7 units, which is a
    /// visible misalignment of the slot at the end of the bar.</para>
    /// </summary>
    private static double CellWidth(string caption) => CellPadding + (CharWidth * caption.Length) + CellPadding;

    /// <summary>An item's own cell width: its caption's, or a separator's fixed thickness.</summary>
    private static double CellWidth(FormControl item) =>
        IsSeparator(item) ? SeparatorExtent : CellWidth(CaptionOf(item));

    /// <summary>An item's height as a dropdown ROW.</summary>
    private static double RowHeight(FormControl item) => IsSeparator(item) ? SeparatorExtent : ItemRowHeight;

    /// <summary>
    /// ⛔ Asked of the CATALOG, never of the kind's name. A second list of "which kinds are
    /// separators" spelled as a string comparison is exactly the shape that falls to its default
    /// the day a row is added — and a separator that measured itself as a caption would push every
    /// item after it along the bar.
    /// </summary>
    private static bool IsSeparator(FormControl item) => item.Definition?.Schematic == FormSchematic.Separator;

    /// <summary>
    /// What a cell is labelled with — the same rule the canvas draws by (<c>DrawControl</c>): the
    /// control's own non-empty <c>Text</c>, else its id, because an unlabelled box on a schematic
    /// is unidentifiable.
    /// </summary>
    private static string CaptionOf(FormControl item) =>
        item.Properties.TryGetValue("Text", out var text) && !string.IsNullOrEmpty(text) ? text : item.Id;

    /// <summary>As wide as its widest item, floored at <see cref="MinDropdownWidth"/>.</summary>
    private static double DropdownWidth(FormControl host) =>
        Math.Max(MinDropdownWidth, host.Children.Select(CellWidth).DefaultIfEmpty(0).Max());

    // ==================================================================
    // Which chrome the selection makes editable
    // ==================================================================

    /// <summary>
    /// Each control's parent. The model stores children but not parents, and both questions below
    /// are "what is above this" — built once per layout rather than re-walked per lookup.
    /// </summary>
    private static Dictionary<FormControl, FormControl> ParentMap(FormDocument document)
    {
        var parents = new Dictionary<FormControl, FormControl>();

        foreach (var control in document.AllControls())
        {
            foreach (var child in control.Children)
            {
                parents[child] = control;
            }
        }

        return parents;
    }

    /// <summary>
    /// The strip a Type Here slot belongs to: the selected strip itself, or the strip the selected
    /// ITEM lives under, however deeply nested. Null when the selection is a positioned control or
    /// nothing at all.
    ///
    /// <para>⚠ The root strip rather than the immediate host, deliberately: with a submenu item
    /// selected the bar itself stays typeable, so a user can keep adding top-level menus without
    /// first clicking back onto the bar.</para>
    /// </summary>
    private static FormControl? ActiveStrip(
        FormControl? selected, IReadOnlyDictionary<FormControl, FormControl> parents)
    {
        var current = selected;
        while (current != null)
        {
            if (current.Definition?.Place == FormPlace.Docked)
            {
                return current;
            }

            current = parents.TryGetValue(current, out var parent) ? parent : null;
        }

        return null;
    }

    /// <summary>
    /// The hosts whose dropdowns are open, OUTERMOST FIRST: every item ancestor of the selection,
    /// plus the selection itself when it is a host in its own right.
    ///
    /// <para>⚠ "A host in its own right" is the catalog's <c>IsHost</c>, not "has children" — every
    /// <c>ToolStripMenuItem</c> can drop down, so selecting an empty one opens an empty dropdown
    /// with a slot in it. That empty dropdown IS the feature: it is the only place to type the
    /// item's first child.</para>
    ///
    /// <para>⚠ Empty for a selected STRIP. Selecting the bar makes the bar typeable; it does not
    /// open the menus on it.</para>
    /// </summary>
    private static List<FormControl> ExpansionPath(
        FormControl? selected, IReadOnlyDictionary<FormControl, FormControl> parents)
    {
        var path = new List<FormControl>();

        if (selected?.Definition?.Place != FormPlace.Item)
        {
            return path;
        }

        var current = selected;
        while (parents.TryGetValue(current, out var parent) && parent.Definition?.Place == FormPlace.Item)
        {
            path.Add(parent);
            current = parent;
        }

        // Collected innermost-first walking up; the dropdowns open outermost-first, because an
        // inner one hangs off a row of the outer one that must exist before it can be placed.
        path.Reverse();

        if (selected.Definition?.IsHost == true)
        {
            path.Add(selected);
        }

        return path;
    }

    /// <summary>
    /// A web page's controls, each in the grid cell it names.
    ///
    /// <para>⚠ TOP LEVEL only, and that is the model's limit rather than a shortcut:
    /// <see cref="FormLayout"/> is a property of the DOCUMENT, so a Panel's children carry Col/Row
    /// against a grid that is not described anywhere. Laying them out would mean inventing one.</para>
    ///
    /// <para>⚠ Grid only. <c>Flow</c> positions by document order and <c>Canvas</c> is the
    /// unimplemented pixel escape hatch; for both, there is no cell a point could mean, and drawing
    /// a guess is exactly the preview this canvas must not pretend to be.</para>
    /// </summary>
    private static IEnumerable<FormLayoutEntry> WebLayout(FormDocument document, FormControl? selected)
    {
        if (document.Layout?.Kind == FormLayoutKind.Grid)
        {
            var surface = SurfaceSize(document);
            foreach (var control in document.Controls)
            {
                if (control.Geometry is GridGeometry grid)
                {
                    yield return new FormLayoutEntry(
                        control,
                        FormGridLayout.CellRect(
                            document.Layout, surface, grid.Col, grid.Row, grid.ColSpan, grid.RowSpan),
                        FormLayoutRole.Control);
                }
            }
        }

        // ⛔ ALWAYS, deliberately OUTSIDE the Grid branch. A band is page CHROME, not a cell: the
        // emitter puts a strip's <nav>/<footer> outside <div class="vgs-form"> precisely because it
        // occupies no track the user declared. Gating it on the layout kind would make a Flow page's
        // menu bar — which the emitted page certainly has — invisible and unclickable in the
        // designer, from a document the canvas otherwise lays out correctly.
        //
        // ⚠ `selected` goes through unchanged: a page's menu is edited with the same cells, the
        // same dropdowns and the same Type Here slot as a form's. Dropping it here would make the
        // designer read-only for web chrome, with the bar still drawn and clicks still landing.
        foreach (var band in Bands(document, selected))
        {
            yield return band;
        }
    }

    private static IEnumerable<FormLayoutEntry> Layout(
        IReadOnlyList<FormControl> controls, Point containerOrigin)
    {
        foreach (var control in controls)
        {
            // ⛔ A Docked strip is yielded by Bands() and by nothing else — never through BoundsOf,
            // which knows only pixel geometry and would drop it silently (or, if one ever acquired
            // geometry, yield it TWICE, once as a Control and once as a Band). Skipping the root
            // also skips its ITEMS, which carry no pixel geometry either: Bands() places them, as
            // cells derived from the band's own rectangle, and that is their only route here.
            if (control.Definition?.Place == FormPlace.Docked)
            {
                continue;
            }

            var bounds = BoundsOf(control, containerOrigin);
            if (bounds == null)
            {
                continue;
            }

            yield return new FormLayoutEntry(control, bounds.Value, FormLayoutRole.Control);

            foreach (var nested in Layout(control.Children, bounds.Value.TopLeft))
            {
                yield return nested;
            }
        }
    }
}

/// <summary>
/// What a <see cref="FormLayoutEntry"/> IS, so a consumer that only wants real controls can say so
/// rather than inferring it from a null <c>Control</c> or from a rectangle's shape.
/// </summary>
public enum FormLayoutRole
{
    /// <summary>An ordinary positioned control or a web page's cell-placed one.</summary>
    Control,

    /// <summary>A Docked strip's band across the surface (Task 17).</summary>
    Band,

    /// <summary>One item's cell inside a band or a dropdown (Task 20, commit 24d).</summary>
    Cell,

    /// <summary>The empty "Type Here" slot at the end of a host's items (Task 20, commit 24d).</summary>
    TypeHere
}

/// <summary>
/// One thing the canvas lays out: a control, a band, an item cell or a Type Here slot.
///
/// <para>⛔ Declared ONCE, with all FOUR fields, although <see cref="Host"/> is unused in commit 24c.
/// A positional record struct's <c>Deconstruct</c> arity is part of its shape: adding the field in
/// 24d would silently change every deconstruction written against a three-field version, and the
/// tests that deconstruct it would break — or worse, bind their names to different fields. The field
/// is here from the start and stays null until Task 20 fills it.</para>
/// </summary>
/// <param name="Control">The control this entry places, or null for a <see cref="FormLayoutRole.TypeHere"/> slot.</param>
/// <param name="Bounds">Its rectangle in FORM space.</param>
/// <param name="Role">What the entry is — see <see cref="FormLayoutRole"/>.</param>
/// <param name="Host">
/// The host a <see cref="FormLayoutRole.TypeHere"/> slot belongs to; null for everything else.
/// Two slots can be visible at once (a strip's and an expanded item's), which is why the slot names
/// its host rather than the caller inferring it from position.
/// </param>
public readonly record struct FormLayoutEntry(
    FormControl? Control, Rect Bounds, FormLayoutRole Role, FormControl? Host = null);
