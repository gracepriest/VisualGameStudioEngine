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

        // ⛔ D3. A .blwebform positions controls by Grid/Flow cell, not by X/Y, and the canvas draws
        // nothing at all for grid geometry — so a "successful" drop here would add a control the
        // user can see only in the Code view. Refusing is honest; placing an invisible control is
        // the silent-success failure this feature exists to avoid.
        if (document.Target != FormTarget.WinForms)
        {
            return new FormPlacementResult(
                null,
                "A web form positions controls by its layout, not by pixels — " +
                "add the control in Code view and give it a Col and Row.");
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

        return (document.Width is > 0 ? document.Width.Value : 400,
                document.Height is > 0 ? document.Height.Value : 300);
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
