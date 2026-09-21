using Avalonia;
using BasicLang.Forms;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>What a drop produced, or why it produced nothing.</summary>
/// <param name="Control">The control added to the document, or null when refused.</param>
/// <param name="Refusal">
/// Why nothing was added, in words a user can act on. Null on success.
/// <para>⛔ A drop that silently does nothing is indistinguishable from a broken IDE — the user
/// drags a Button onto the canvas, nothing appears, and there is no way to tell whether the
/// designer refused, crashed, or never received the gesture. Every refusal here says why.</para>
/// </param>
public sealed record FormPlacementResult(FormControl? Control, string? Refusal);

/// <summary>
/// Placing a control on the canvas — the model half of toolbox drag-and-drop.
///
/// <para>The canvas contributes exactly one thing to a drop: the point, mapped to form space by
/// <see cref="FormCanvasTransform"/> — the same mapping that drew the picture, so the control lands
/// where the pointer was. Everything after that is this class, and none of it needs a visual tree,
/// which is why it is tested directly.</para>
///
/// <para>⛔ Lives beside the canvas rather than in <c>BasicLang/Forms/</c> because containment is
/// the canvas's rule: <see cref="FormCanvasTransform.ContainerAt"/> decides what a point is inside,
/// and selection uses the same walk. A second containment rule in the compiler assembly is exactly
/// the mirrored-logic trap this repo has already been bitten by — and the symptom would be a drop
/// that nests into a Panel the canvas says you are not over.</para>
/// </summary>
public static class FormPlacement
{
    /// <summary>
    /// Adds a control of <paramref name="kind"/> at the form-space point
    /// (<paramref name="x"/>, <paramref name="y"/>), or refuses with a reason.
    /// </summary>
    public static FormPlacementResult Place(FormDocument document, string kind, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(document);

        var definition = FormControlCatalog.Find(kind);
        if (definition == null)
        {
            return new FormPlacementResult(
                null, $"'{kind}' is not a control this designer knows about.");
        }

        if (!definition.SupportsTarget(document.Target))
        {
            return new FormPlacementResult(
                null, $"{definition.Kind} is not available on a {document.Target} form.");
        }

        // A component (Task 25) has no place: it goes in the tray wherever it was dropped, on either
        // target, and needs no cell and no <Layout>. Nothing positional is minted for it — no
        // geometry, no tab index, no caption — because it has none of those to have.
        if (definition.IsComponent)
        {
            var component = new FormControl { Kind = definition.Kind, Id = NextId(document, definition.Kind) };
            document.Components.Add(component);
            return new FormPlacementResult(component, null);
        }

        // ⛔ An ITEM (spec §1, Place == Item) has no place of its own ANYWHERE — not a pixel, not a
        // cell, not the tray. It is created from its host's "Type Here" slot (§6) through
        // <see cref="PlaceItem"/>. Refused BEFORE the containment walk below, which would otherwise
        // nest it in whatever Panel the pointer happened to be over and give it geometry and a tab
        // order it cannot have.
        if (definition.Place == FormPlace.Item)
        {
            return new FormPlacementResult(
                null, $"'{definition.Kind}' is created from its menu's Type Here slot, not dropped.");
        }

        // ⛔ A DOCKED strip (spec §1) takes the row's own Dock and nothing else: no geometry, no tab
        // order, and TOP-LEVEL regardless of the point. Before the container walk for the same reason
        // the Item arm is — a strip dropped over a Panel would land in `panel.Children`, where nothing
        // emits it as chrome and the canvas's band layout never looks.
        //
        // ⚠ Both targets. A web page's strip is chrome too (Task 16's <nav>/<footer>), so this
        // deliberately precedes the .blwebform branch below rather than sitting inside the WinForms
        // half: a cell would describe a position a page's chrome does not have.
        if (definition.Place == FormPlace.Docked)
        {
            var strip = new FormControl { Kind = definition.Kind, Id = NextId(document, definition.Kind) };

            // The row's default, never anything derived from the point. StatusStrip docks Bottom and
            // a MenuStrip Top, and that is a property of the KIND.
            var dock = definition.Property("Dock");
            if (dock?.Default != null)
            {
                strip.Properties["Dock"] = dock.Default;
            }

            document.Controls.Add(strip);
            return new FormPlacementResult(strip, null);
        }

        // ⛔ D3. A .blwebform positions controls by CELL, not by pixel, so the web path produces a
        // GridGeometry from the cell the pointer is in rather than an X/Y.
        if (document.Target != FormTarget.WinForms)
        {
            return PlaceOnWeb(document, definition, x, y);
        }

        var container = FormCanvasTransform.ContainerAt(document, new Point(x, y));
        var siblings = container?.Container.Children ?? document.Controls;

        // Child coordinates are relative to the CONTAINER, not to the form. Storing form-space
        // coordinates on a child draws correctly and runs wrong, which is the worst shape this
        // kind of bug takes.
        var origin = container?.Origin ?? new Point(0, 0);
        var local = new Point(x - origin.X, y - origin.Y);

        var (surfaceWidth, surfaceHeight) = SurfaceOf(document, container?.Container);

        var control = new FormControl
        {
            Kind = definition.Kind,
            Id = NextId(document, definition.Kind),
            TabIndex = NextTabIndex(document),
            Geometry = new PixelGeometry
            {
                X = Clamp(local.X, definition.DefaultWidth, surfaceWidth),
                Y = Clamp(local.Y, definition.DefaultHeight, surfaceHeight),
                Width = definition.DefaultWidth,
                Height = definition.DefaultHeight
            }
        };

        // ⛔ Ask the catalog whether this kind HAS a caption. Stamping Text onto one that does not
        // sends an attribute the writer does not know through to the file, and on WinForms it is
        // csc that finds out.
        if (definition.Property("Text") != null)
        {
            control.Properties["Text"] = control.Id;
        }

        siblings.Add(control);
        return new FormPlacementResult(control, null);
    }

    /// <summary>
    /// A drop on a <c>.blwebform</c>: the control goes in the CELL the pointer is over.
    ///
    /// <para>⚠ Grid only, and the refusals say why. <c>Flow</c> is flexbox — position comes from
    /// document ORDER, not from a cell, so a point on the canvas means nothing there. A page with
    /// no <c>&lt;Layout&gt;</c> gets no grid CSS either, so a Col and Row would describe a grid the
    /// emitted page does not have. Both are better said than guessed at.</para>
    ///
    /// <para>⚠ Always a top-level control. A web container's children carry Col/Row against a grid
    /// that the model has no vocabulary for — <see cref="FormLayout"/> is a property of the
    /// DOCUMENT, not of a Panel — so there is no nested grid to drop into, and pretending otherwise
    /// would write geometry nothing can lay out.</para>
    /// </summary>
    private static FormPlacementResult PlaceOnWeb(
        FormDocument document, FormControlDef definition, int x, int y)
    {
        if (document.Layout == null)
        {
            return new FormPlacementResult(
                null,
                "This page has no <Layout>, so there are no cells to drop into. " +
                "Add a Grid layout in Code view first.");
        }

        if (document.Layout.Kind != FormLayoutKind.Grid)
        {
            return new FormPlacementResult(
                null,
                $"A {document.Layout.Kind} layout positions controls by document order, not by " +
                "cell, so there is nothing here to drop onto. Add the control in Code view.");
        }

        var cell = FormGridLayout.CellAt(
            document.Layout, FormCanvasTransform.SurfaceSize(document), new Point(x, y));

        if (cell == null)
        {
            return new FormPlacementResult(null, "That is outside the page.");
        }

        var control = new FormControl
        {
            Kind = definition.Kind,
            Id = NextId(document, definition.Kind),
            TabIndex = NextTabIndex(document),

            // ⚠ Span 1 is the default and the writer deliberately omits it, so setting anything
            // else here would stamp ColSpan="1" into every element the designer touches.
            Geometry = new GridGeometry { Col = cell.Value.Col, Row = cell.Value.Row }
        };

        if (definition.Property("Text") != null)
        {
            control.Properties["Text"] = control.Id;
        }

        document.Controls.Add(control);
        return new FormPlacementResult(control, null);
    }

    /// <summary>
    /// "Type Here" (spec §6): appends an item of <paramref name="kind"/> to <paramref name="host"/>,
    /// or refuses. The one entry point for creating an item — <see cref="Place"/> refuses a dropped
    /// one by name, because an item has no place of its own on the canvas.
    ///
    /// <para>⛔ The host's own <see cref="FormItemRule"/> decides what it accepts, never the shape of
    /// the kind's name: a StatusStrip holds only a ToolStripStatusLabel, and a separator dropped into
    /// one has to be refused rather than emitted into a verb that cannot take it.</para>
    /// </summary>
    public static FormPlacementResult PlaceItem(
        FormDocument document, FormControl host, string kind, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(host);

        var rule = host.Definition?.Items;
        var definition = FormControlCatalog.Find(kind);
        if (rule == null || definition == null || !rule.Accepts(definition.Kind))
        {
            return new FormPlacementResult(
                null,
                $"'{host.Id}' ({host.Kind}) holds " +
                $"{string.Join(", ", rule?.Kinds ?? Array.Empty<string>())}, not a {kind}.");
        }

        var item = new FormControl { Kind = definition.Kind, Id = ItemId(document, definition, text) };

        // ⛔ Ask the catalog, the same rule Place uses. A ToolStripSeparator has no Text row at all,
        // so the first half already answers no; the explicit kind check says WHY out loud, because a
        // separator's caption is the marker "-" the user typed, never something to store.
        if (definition.Property("Text") != null && definition.Kind != "ToolStripSeparator")
        {
            item.Properties["Text"] = text;
        }

        // Appended — last in the list is last on the bar, and the host's verb emits in document order.
        host.Children.Add(item);
        return new FormPlacementResult(item, null);
    }

    /// <summary>
    /// VS's own id: the caption camel-cased and sanitised plus the kind (<c>openToolStripMenuItem</c>);
    /// <c>-</c> → <c>toolStripSeparator1</c>; an unusable caption (empty, leading digit) →
    /// <c>toolStripMenuItem1</c>.
    ///
    /// <para>⚠ PUBLIC and on this class because the id rule and the placement that mints it are one
    /// answer — a second spelling of "what is this item called" would let the designer name an item
    /// one thing and a later consumer another.</para>
    /// </summary>
    public static string ItemId(FormDocument document, FormControlDef definition, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);

        var kindPart = char.ToLowerInvariant(definition.Kind[0]) + definition.Kind[1..];

        // ⛔ `&&` is a LITERAL ampersand in a WinForms caption and a lone `&` is the accelerator mark:
        // ONE regex, never String.Replace with an empty pattern (which throws), and never two Replace
        // calls (stripping "&" first turns "&&" into "" rather than "&"). This is the same rule,
        // spelt the same way, as FormAssetEmitter's accelerator stripping — change them together.
        var plain = System.Text.RegularExpressions.Regex.Replace(text ?? "", "&(&?)", "$1");
        var words = new string(plain.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var caption = string.Concat(words.Select((w, i) =>
            i == 0 ? char.ToLowerInvariant(w[0]) + w[1..] : char.ToUpperInvariant(w[0]) + w[1..]));

        var usable = caption.Length > 0 && char.IsLetter(caption[0]) &&
                     definition.Kind != "ToolStripSeparator";
        Func<string, bool> taken = id => document.FindById(id) != null;

        // ⚠ MakeUniqueId returns its argument UNCHANGED when free (FormDocument.cs:153-158), so the
        // FALLBACK is seeded with the "1" the way NextId seeds `kind + "1"` — `toolStripSeparator1`,
        // not `toolStripSeparator`. The caption stem is NOT seeded: `openToolStripMenuItem` first,
        // then `openToolStripMenuItem1` once that one is taken.
        return usable && FormDocument.IsLegalControlId(caption + definition.Kind)
            ? FormDocument.MakeUniqueId(caption + definition.Kind, taken)
            : FormDocument.MakeUniqueId(kindPart + "1", taken);
    }

    /// <summary>
    /// Keeps the whole control on its surface. A control dropped half off the edge is placeable in
    /// a real designer only because you can drag it back; this one cannot be dragged yet.
    /// </summary>
    private static int Clamp(double value, int size, int surface) =>
        (int)Math.Max(0, Math.Min(value, surface - size));

    /// <summary>
    /// The usable area a control is being dropped into: the container's own box, or the form's
    /// client size. The fallbacks match <c>FormCanvasControl.Fit</c>'s, so clamping cannot disagree
    /// with the rectangle the canvas drew.
    /// </summary>
    private static (int Width, int Height) SurfaceOf(FormDocument document, FormControl? container)
    {
        if (container?.Geometry is PixelGeometry pixel)
        {
            return (pixel.Width, pixel.Height);
        }

        var surface = FormCanvasTransform.SurfaceSize(document);
        return ((int)surface.Width, (int)surface.Height);
    }

    /// <summary>
    /// <c>Button1</c>, <c>Button2</c>, … — the convention every VB and WinForms designer uses, and
    /// one the user can predict before they drop.
    ///
    /// <para>⛔ Checked against the WHOLE document, hand-written controls included. Minting an id
    /// that already exists gives one form two controls with one name, and the region writer then
    /// declares the same field twice.</para>
    /// </summary>
    private static string NextId(FormDocument document, string kind) =>
        FormDocument.MakeUniqueId(kind + "1", id => document.FindById(id) != null);

    /// <summary>
    /// One past the highest tab order already in use.
    ///
    /// <para>⛔ POSITIONED rows only, the same filter <see cref="FormDocument.RenumberTabIndexes"/>
    /// applies (Task 24). Strips and items are inside <c>AllControls()</c> — the tray is excluded a
    /// list at a time, these are excluded a ROW at a time — and they all hold TabIndex 0, so an
    /// unfiltered Max would still answer 0 and hand a second dropped control the index the first one
    /// already has.</para>
    ///
    /// <para>⛔ <c>is null or FormPlace.Positioned</c>: a control with no catalog row is positioned,
    /// and <c>== FormPlace.Positioned</c> is false for a null <c>Definition</c>.</para>
    /// </summary>
    private static int NextTabIndex(FormDocument document)
    {
        var used = document.AllControls()
            .Where(c => c.Definition?.Place is null or FormPlace.Positioned)
            .Select(c => c.TabIndex)
            .ToList();

        return used.Count == 0 ? 0 : used.Max() + 1;
    }
}
