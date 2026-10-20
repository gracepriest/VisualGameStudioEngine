namespace BasicLang.Forms;

/// <summary>
/// The code-generation style recorded by a form's designer markers.
///
/// <para>This is deliberately separate from <see cref="FormTarget"/>: a <c>.blwebform</c> can
/// generate DOM code or portable Forms-library code, and the only authority is the marker. A
/// <c>.blform</c> is WinForms, except for the piece-3 <see cref="Dual"/> wrapper.</para>
/// </summary>
public enum FormStyle
{
    /// <summary>A web document using the DOM API: <c>getElementById</c> and <c>addEventListener</c>.</summary>
    Dom,

    /// <summary>A web document using the portable Forms library: WinForms-shaped code, no DOM.</summary>
    Portable,

    /// <summary>A desktop form.</summary>
    WinForms,

    /// <summary>
    /// A desktop document whose regions wrap both the desktop and web shapes for piece 3. Task 30
    /// only accepts the marker; code generation treats it as the desktop shape until conversion
    /// owns the wrapped region.
    /// </summary>
    Dual
}

/// <summary>The result of reading a file's marker style.</summary>
/// <param name="Style">The effective style, or the target's historical default when no style was recorded.</param>
/// <param name="Regions">The regions found by the one scan, so callers never scan twice.</param>
/// <param name="Problem">A user-facing <c>BL8012</c> explanation, or <see langword="null"/> when the style is legal.</param>
/// <param name="Line">The marker line to blame, or 0 when there is no marker problem.</param>
public sealed record FormStyleRead(
    FormStyle Style,
    IReadOnlyList<FormRegion> Regions,
    string? Problem,
    int Line);

/// <summary>
/// The ONE reader of a file's generated-code style from its designer markers.
///
/// <para>⛔ Do not infer the style from the target, the file contents, or the presence of a control.
/// A marker without <c>style</c> is the historical style for that extension; a marker with
/// <c>style="portable"</c> is portable. The scaffold writes that attribute into empty regions
/// before it calls the writer, so an empty new form is portable even though it has nothing to
/// detect.</para>
///
/// <para>The rules are intentionally strict. <c>style</c> is only legal between <c>form</c> and
/// <c>hash</c> (the regex in <see cref="RegionMarkers"/> enforces the position).
/// <c>portable</c> is legal only on <c>.blwebform</c>; <c>dual</c> is legal only on
/// <c>.blform</c>. Unknown values, a value illegal for the extension, and two regions that disagree
/// are all <c>RegionMarkersMalformed</c> (BL8012) on both extensions.</para>
/// </summary>
public static class FormCodeStyle
{
    public const string PortableValue = "portable";
    public const string DualValue = "dual";

    /// <summary>
    /// Reads the style from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The user's code-behind.</param>
    /// <param name="formFileName">
    /// The paired form document name (<c>LoginForm.blform</c>/<c>LoginForm.blwebform</c>), or
    /// <see langword="null"/> when the caller only has markers (the handler planner does). With no
    /// extension, the marker value itself selects the style and <paramref name="fallbackTarget"/>
    /// selects the no-style default.
    /// </param>
    /// <param name="fallbackTarget">The document target, used only when no style was recorded.</param>
    public static FormStyleRead Of(
        string source,
        string? formFileName = null,
        FormTarget fallbackTarget = FormTarget.Web)
    {
        var regions = RegionMarkers.Scan(source);
        var relevant = regions
            .Where(r => string.Equals(r.Name, RegionMarkers.Controls, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.Name, RegionMarkers.Init, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var defaultStyle = DefaultStyle(formFileName, fallbackTarget);
        if (relevant.Count == 0)
        {
            return new FormStyleRead(defaultStyle, regions, null, 0);
        }

        var first = relevant[0];
        var distinct = relevant
            .Select(r => r.Style)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count > 1)
        {
            var shown = string.Join(" and ",
                distinct.Select(s => string.IsNullOrEmpty(s) ? "(none)" : $"\"{s}\""));
            return new FormStyleRead(
                defaultStyle,
                regions,
                $"the two designer regions disagree about the code style ({shown}). " +
                "Both region markers must carry the same style.",
                first.Line);
        }

        var raw = distinct[0];
        if (raw == null)
        {
            return new FormStyleRead(defaultStyle, regions, null, 0);
        }

        // An explicit style="" is not the same thing as an absent attribute: it is a style value the
        // reader does not know, and the refusal is the only safe answer. Only a missing attribute
        // means the historical default.
        var style = raw.ToLowerInvariant() switch
        {
            PortableValue => FormStyle.Portable,
            DualValue => FormStyle.Dual,
            _ => (FormStyle?)null
        };

        if (style == null)
        {
            return new FormStyleRead(
                defaultStyle,
                regions,
                $"the designer regions carry an unknown code style \"{raw}\". " +
                "A web form may say style=\"portable\"; a desktop form may say style=\"dual\".",
                first.Line);
        }

        var isDesktop = IsExtension(formFileName, ".blform");
        var isWeb = IsExtension(formFileName, ".blwebform");
        if (style == FormStyle.Portable && isDesktop)
        {
            return new FormStyleRead(
                defaultStyle,
                regions,
                "style=\"portable\" is only legal on a .blwebform; a .blform uses the desktop shape.",
                first.Line);
        }

        if (style == FormStyle.Dual && isWeb)
        {
            return new FormStyleRead(
                defaultStyle,
                regions,
                "style=\"dual\" is only legal on a .blform; a .blwebform uses plain or portable code.",
                first.Line);
        }

        return new FormStyleRead(style.Value, regions, null, 0);
    }

    private static FormStyle DefaultStyle(string? formFileName, FormTarget fallbackTarget) =>
        IsExtension(formFileName, ".blform") ? FormStyle.WinForms :
        IsExtension(formFileName, ".blwebform") ? FormStyle.Dom :
        fallbackTarget == FormTarget.WinForms ? FormStyle.WinForms : FormStyle.Dom;

    private static bool IsExtension(string? fileName, string extension) =>
        fileName is not null &&
        fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}
