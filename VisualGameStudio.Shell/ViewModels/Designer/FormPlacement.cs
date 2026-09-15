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

    private static int NextTabIndex(FormDocument document)
    {
        var used = document.AllControls().Select(c => c.TabIndex).ToList();
        return used.Count == 0 ? 0 : used.Max() + 1;
    }
}
