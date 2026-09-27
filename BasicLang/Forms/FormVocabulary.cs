namespace BasicLang.Forms;

/// <summary>
/// ⛔⛔ THE one answer to "is this document laid out in PIXELS or in CELLS?" (spec 2026-09-27 §2.1).
///
/// <para>A <c>.blform</c> always speaks pixels. A <c>.blwebform</c> speaks pixels when its
/// <c>&lt;Layout Kind="Canvas"&gt;</c> says so, and cells (Grid) or document order (Flow) otherwise.
/// Every site that decides a vocabulary asks HERE: the reader's root size and geometry, the writer,
/// <see cref="FormControlCatalog.IsStructural(string, FormTarget)"/>, the clipboard, the canvas and
/// placement. Before this existed, each of them asked <c>Target == WinForms</c>, which silently
/// routed every web document down the Grid path.</para>
///
/// <para>⚠ It takes VALUES, not a document: the reader decides the vocabulary of the root
/// attributes before any model exists (§2.2's pre-scan hands it the layout).</para>
/// </summary>
public static class FormVocabulary
{
    /// <summary>True for WinForms, or for a web document laid out <see cref="FormLayoutKind.Canvas"/>.</summary>
    /// <param name="layout">The web document's layout; ignored for WinForms. Null on the web means Grid.</param>
    public static bool IsPixel(FormTarget target, FormLayoutKind? layout) =>
        target == FormTarget.WinForms || layout == FormLayoutKind.Canvas;

    /// <summary>
    /// The document's layout in the sense <see cref="IsPixel(FormTarget, FormLayoutKind?)"/> and
    /// <c>FormRootValues.Applies(row, target, layout)</c> take it: null for
    /// WinForms (a window has no <c>&lt;Layout&gt;</c>), and on the web the document's own kind — Grid when
    /// it carries none, exactly as <see cref="FormLayout.Kind"/> defaults.
    /// </summary>
    public static FormLayoutKind? LayoutOf(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Target == FormTarget.Web ? document.Layout?.Kind ?? FormLayoutKind.Grid : null;
    }

    /// <summary><see cref="IsPixel(FormTarget, FormLayoutKind?)"/> for a document.</summary>
    public static bool IsPixel(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return IsPixel(document.Target, LayoutOf(document));
    }
}
