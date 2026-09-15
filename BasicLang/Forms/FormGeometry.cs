namespace BasicLang.Forms;

/// <summary>
/// Which of the two document formats a model belongs to.
///
/// <para>D2: the formats share the element grammar, the <c>Id</c> rules, the
/// <c>&lt;Bind Event=&gt;</c> shape and the reserved sections, and deliberately diverge in layout
/// vocabulary and catalog. Every model type below therefore carries the target rather than trying
/// to be neutral between them.</para>
/// </summary>
public enum FormTarget
{
    /// <summary>A <c>.blform</c> — WinForms desktop, absolute pixel layout (D3).</summary>
    WinForms,

    /// <summary>A <c>.blwebform</c> — browser page, Grid/Flow layout (D3).</summary>
    Web
}

/// <summary>
/// Where a control sits. Two shapes, because D3 gives each target its native idiom rather than
/// forcing one vocabulary to satisfy both.
/// </summary>
public abstract class FormGeometry
{
    /// <summary>The target whose layout vocabulary this geometry is written in.</summary>
    public abstract FormTarget Target { get; }

    /// <summary>Deep copy — used by subtree serialization and by the canvas's undo stack.</summary>
    public abstract FormGeometry Clone();
}

/// <summary>
/// <c>.blform</c> geometry: absolute <c>X</c>/<c>Y</c>/<c>Width</c>/<c>Height</c> plus
/// <c>Anchor</c>/<c>Dock</c>. The target's real idiom, and what the shipped template writes.
/// </summary>
public sealed class PixelGeometry : FormGeometry
{
    public override FormTarget Target => FormTarget.WinForms;

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>A WinForms <c>AnchorStyles</c> list, e.g. <c>"Left,Top,Right"</c>. Null = unset.</summary>
    public string? Anchor { get; set; }

    /// <summary>A WinForms <c>DockStyle</c> name, e.g. <c>"Fill"</c>. Null = unset.</summary>
    public string? Dock { get; set; }

    public override FormGeometry Clone() => new PixelGeometry
    {
        X = X, Y = Y, Width = Width, Height = Height, Anchor = Anchor, Dock = Dock
    };
}

/// <summary>
/// <c>.blwebform</c> geometry: a cell in the document's <see cref="FormLayout"/>.
///
/// <para>⚠ Absolute pixels are NOT the web default and are not modeled here. D3 demotes them to an
/// explicitly marked <c>Canvas</c> layout, which is a property of the document, not of a control —
/// pixel-per-control is exactly what VS 2002/2003 emitted under <c>MS_POSITIONING="GridLayout"</c>
/// and what Microsoft flipped away from in VS 2005 because those pages broke on text resize,
/// different fonts and localisation.</para>
/// </summary>
public sealed class GridGeometry : FormGeometry
{
    public override FormTarget Target => FormTarget.Web;

    public int Col { get; set; }
    public int Row { get; set; }

    /// <summary>Grid span; 1 means "one cell" and is not written out.</summary>
    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;

    public override FormGeometry Clone() => new GridGeometry
    {
        Col = Col, Row = Row, ColSpan = ColSpan, RowSpan = RowSpan
    };
}

/// <summary>How a <c>.blwebform</c> arranges its controls (D3).</summary>
public enum FormLayoutKind
{
    /// <summary>CSS grid. <c>Cols</c>/<c>Rows</c> are <c>grid-template-*</c> track lists.</summary>
    Grid,

    /// <summary>Flexbox. <c>Dir</c> selects row or column.</summary>
    Flow,

    /// <summary>
    /// The absolute-pixel escape hatch. Explicitly marked on the canvas — hatched border,
    /// "fixed layout" badge — so a fixed-layout page is a visible choice, never a default.
    /// </summary>
    Canvas
}

/// <summary>
/// The document-level layout of a <c>.blwebform</c>: <c>&lt;Layout Kind="Grid" Cols="120px,1fr"
/// Rows="auto,auto" Gap="8px"/&gt;</c>.
///
/// <para>Track lists are kept as the author's own CSS text rather than parsed into a structure.
/// <c>Cols="120px,1fr"</c> IS <c>grid-template-columns</c>, and re-emitting a parsed form would
/// silently normalise units the user chose deliberately.</para>
/// </summary>
public sealed class FormLayout
{
    public FormLayoutKind Kind { get; set; } = FormLayoutKind.Grid;

    /// <summary><c>grid-template-columns</c> verbatim, e.g. <c>"120px,1fr"</c>. Grid only.</summary>
    public string? Cols { get; set; }

    /// <summary><c>grid-template-rows</c> verbatim, e.g. <c>"auto,auto"</c>. Grid only.</summary>
    public string? Rows { get; set; }

    /// <summary>CSS <c>gap</c>, e.g. <c>"8px"</c>.</summary>
    public string? Gap { get; set; }

    /// <summary><c>"Horizontal"</c> or <c>"Vertical"</c>. Flow only.</summary>
    public string? Dir { get; set; }

    public FormLayout Clone() => new()
    {
        Kind = Kind, Cols = Cols, Rows = Rows, Gap = Gap, Dir = Dir
    };
}
