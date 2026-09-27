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

    /// <summary>A <c>.blwebform</c> — browser page, Grid/Flow cells or (Canvas) pixels (D3; spec 2026-09-27 D1).</summary>
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
/// Pixel geometry: absolute <c>X</c>/<c>Y</c>/<c>Width</c>/<c>Height</c> plus <c>Anchor</c>/<c>Dock</c> —
/// a <c>.blform</c>'s, and a Canvas page's (spec 2026-09-27 D2). The WinForms idiom, and what the shipped
/// template writes.
/// </summary>
public sealed class PixelGeometry : FormGeometry
{
    /// <summary>
    /// The target whose NATIVE vocabulary this is. ⚠ A Canvas page borrows it — never decide a document's
    /// vocabulary from this; ask <see cref="FormVocabulary.IsPixel(FormTarget, FormLayoutKind?)"/>.
    /// </summary>
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
/// <para>⚠ A cell. A web page laid out in pixels is a Canvas page, and its controls carry
/// <see cref="PixelGeometry"/> (spec 2026-09-27 D1) — pixels are a property of the DOCUMENT's layout,
/// never of one control.</para>
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
    /// Absolute pixels, designed like a WinForms form (spec 2026-09-27 D1): the page stores a design size and
    /// per-control X/Y/Width/Height/Anchor/Dock, is exact at the design size, follows Anchor/Dock on resize and
    /// stacks below <see cref="FormLayout.MobileBreakpoint"/>. The DEFAULT for a new web form. The IDE may
    /// label it "Pixel"; the XML spelling stays <c>Canvas</c>.
    /// </summary>
    Canvas
}

/// <summary>
/// The document-level layout of a <c>.blwebform</c>: <c>&lt;Layout Kind="Grid" Cols="120px,1fr"
/// Rows="auto,auto" Gap="8px"/&gt;</c>, or a pixel page's <c>&lt;Layout Kind="Canvas" MobileBreakpoint="600"/&gt;</c>.
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

    /// <summary>The page width, in pixels, below which a Canvas page stacks into one column when none is set.</summary>
    public const int DefaultMobileBreakpoint = 600;

    /// <summary>
    /// A Canvas page's phone breakpoint (spec 2026-09-27 §2.3, §5), as the document's RAW text — e.g.
    /// <c>"600"</c>; <c>"0"</c> never stacks. Null = absent (the default applies). Canvas only.
    ///
    /// <para>⛔ Raw text, like <see cref="Cols"/>, never a parsed int: a value that cannot be used is then
    /// never null in the model, so no save can remove it (the writer removes an attribute whose model value
    /// is null). Its Degraded tier comes from the reader; its number from
    /// <see cref="EffectiveMobileBreakpoint"/>.</para>
    /// </summary>
    public string? MobileBreakpoint { get; set; }

    /// <summary>
    /// The breakpoint the page uses: the document's value when usable, else <see cref="DefaultMobileBreakpoint"/>
    /// — a Degraded value never reaches generated output, exactly as a Degraded control property does not.
    /// </summary>
    public int EffectiveMobileBreakpoint =>
        TryParseMobileBreakpoint(MobileBreakpoint, out var pixels) ? pixels : DefaultMobileBreakpoint;

    /// <summary>
    /// A usable breakpoint: 0 or a positive whole number, read culture-free with the catalog's parser
    /// (<see cref="FormPropertyDef.TryParseInt"/>). ⛔ The ONE rule — the reader's Degraded check,
    /// <see cref="FormRootValues.Set"/> and <see cref="EffectiveMobileBreakpoint"/> all ask it.
    /// </summary>
    public static bool TryParseMobileBreakpoint(string? text, out int pixels)
    {
        if (text != null && FormPropertyDef.TryParseInt(text, out pixels) && pixels >= 0)
        {
            return true;
        }

        pixels = DefaultMobileBreakpoint;
        return false;
    }

    public FormLayout Clone() => new()
    {
        Kind = Kind, Cols = Cols, Rows = Rows, Gap = Gap, Dir = Dir, MobileBreakpoint = MobileBreakpoint
    };
}
