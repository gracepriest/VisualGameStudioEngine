using System.Globalization;

namespace BasicLang.Forms;

/// <summary>
/// The ONE owner of "what does this row's value become in CSS" (spec §2.1). <c>FormAssetEmitter</c>
/// walks the catalog and asks here; nothing else converts a value to CSS.
/// </summary>
public static class FormCss
{
    /// <summary>
    /// The declaration for <paramref name="property"/> = <paramref name="value"/>, or null when the row
    /// has no CSS meaning, the value is not usable on the web (Degraded — never emitted), or the
    /// converter says "no declaration" (Visible=true).
    /// </summary>
    public static (string Property, string Value)? Declaration(FormPropertyDef property, string value)
    {
        // ⛔ Accepts(value, WEB), never Accepts(value): a system colour with no CSS equivalent
        // (ActiveCaption) is valid on WinForms and Degraded here, and must not reach the stylesheet.
        if (property.CssProperty is not { } css || !property.Accepts(value, FormTarget.Web))
        {
            return null;
        }

        var converted = property.CssConverter switch
        {
            FormCssConverter.None => IsSafeVerbatim(value) ? value : null,
            FormCssConverter.Color => ColorToCss(value),
            FormCssConverter.ContentAlignmentHorizontal => HorizontalPart(property.Canonical(value)),
            FormCssConverter.VisibleToDisplay => bool.TryParse(value, out var visible) && !visible ? "none" : null,
            // ⛔ Never a silent "no declaration": a converter added to the enum without an arm here
            // would drop its row from every page with nothing looking wrong.
            _ => throw new ArgumentOutOfRangeException(nameof(property), property.CssConverter,
                $"FormCss has no arm for the converter on '{property.Name}'.")
        };

        return converted == null ? null : (css, converted);
    }

    /// <summary>
    /// ⛔ A verbatim value is spliced into <c>#id { prop: VALUE; }</c> inside a <c>&lt;style&gt;</c>.
    /// Any of these characters could end the declaration, the rule or the element, or open a string or
    /// escape — so the value is refused (no declaration) rather than escaped. The catalog also keeps
    /// verbatim rows to Int and Enum (<c>FormCssTests.EveryVerbatimCssRow_IsIntOrEnum</c>); this is the
    /// second line for a row that slips past it.
    /// </summary>
    private static bool IsSafeVerbatim(string value) =>
        value.IndexOfAny(UnsafeVerbatim) < 0;

    private static readonly char[] UnsafeVerbatim = { ';', '{', '}', '<', '>', '"', '\'', '\\', '\n', '\r' };

    /// <summary>
    /// ⛔ <c>#AARRGGBB</c> is WinForms ARGB in the document; CSS reads eight hex digits as RRGGBBAA, so
    /// it becomes <c>rgba()</c>. A system colour becomes its CSS system colour (null when there is none
    /// — which <c>Accepts(value, Web)</c> has already refused).
    /// </summary>
    private static string? ColorToCss(string value)
    {
        if (FormSystemColors.TryCanonical(value, out var system))
        {
            return FormSystemColors.CssFor(system);
        }

        // Accepts has already proved the eight digits are hex (IsColor), so Convert cannot throw.
        if (value.Length == 9 && value[0] == '#')
        {
            int Hex(int at) => Convert.ToInt32(value.Substring(at, 2), 16);
            var alpha = (Hex(1) / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
            return $"rgba({Hex(3)}, {Hex(5)}, {Hex(7)}, {alpha})";
        }

        return value;
    }

    /// <summary>
    /// A ContentAlignment member's horizontal part, lower-case (spec §2.8). The vertical part has no web
    /// meaning and is not emitted — <c>middleleft</c> is not CSS.
    /// </summary>
    private static string? HorizontalPart(string member)
    {
        if (member.EndsWith("Left", StringComparison.Ordinal)) return "left";
        if (member.EndsWith("Center", StringComparison.Ordinal)) return "center";
        if (member.EndsWith("Right", StringComparison.Ordinal)) return "right";
        return null;
    }
}
