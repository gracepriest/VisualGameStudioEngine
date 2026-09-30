using System.Globalization;

namespace BasicLang.Forms;

/// <summary>
/// A <see cref="FormPropertyType.Font"/> value (spec §2.2): the document stores WinForms' own <c>FontConverter</c>
/// invariant text — <c>Segoe UI, 9pt, style=Bold, Italic</c> — which is what VS's grid shows and what the reflected
/// snapshot's defaults are written in. ONE parser, and every answer (canonical text, WinForms source, CSS) is built
/// from what it parsed, never from the input text.
///
/// <para>⛔ The family is restricted to letters, digits, spaces and hyphens. It is spliced into a BasicLang string
/// literal AND a CSS <c>font-family</c> string; a quote, a semicolon or a brace in it could end either. Real Windows
/// family names ("Segoe UI Semibold", "MS Reference Sans Serif", "Courier New") fit; anything else is Degraded —
/// preserved and explained, never escaped into something the user did not write.</para>
///
/// <para>⚠ Points only (<c>9pt</c>, or a bare number, which FontConverter also reads as points). Another unit is
/// Degraded rather than converted: the designer emits <c>New Font(family, size)</c>, which is points.</para>
/// </summary>
public sealed record FormFontValue(string Family, decimal Size, bool Bold, bool Italic, bool Underline, bool Strikeout)
{
    /// <summary>The FontStyle flags value: Bold 1, Italic 2, Underline 4, Strikeout 8.</summary>
    public int StyleFlags => (Bold ? 1 : 0) | (Italic ? 2 : 0) | (Underline ? 4 : 0) | (Strikeout ? 8 : 0);

    /// <summary>The styles present, in FontStyle flag order — the order FontConverter writes them.</summary>
    public IReadOnlyList<string> Styles =>
        new[] { (Bold, "Bold"), (Italic, "Italic"), (Underline, "Underline"), (Strikeout, "Strikeout") }
            .Where(s => s.Item1).Select(s => s.Item2).ToList();

    private string SizeText => Size.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary><c>Family, 9pt[, style=Bold, Italic]</c> — FontConverter's own shape, styles in flag order.</summary>
    public string Canonical =>
        $"{Family}, {SizeText}pt" + (Styles.Count > 0 ? ", style=" + string.Join(", ", Styles) : "");

    /// <summary>
    /// ONE statement's right-hand side (the fan-in rule): <c>New Font("Segoe UI", 9F)</c>, with one style as its
    /// member and several as <c>CType(n, FontStyle)</c> — MEASURED: BasicLang's <c>Or</c> demands Boolean operands,
    /// and the cast is what <c>RegionWriter.AnchorExpression</c> already relies on for AnchorStyles.
    /// </summary>
    public string WinFormsLiteral
    {
        get
        {
            var head = $"New Font({FormPropertyDef.StringLiteral(Family)}, {SizeText}F";
            return Styles.Count switch
            {
                0 => head + ")",
                1 => head + $", FontStyle.{Styles[0]})",
                _ => head + string.Create(CultureInfo.InvariantCulture, $", CType({StyleFlags}, FontStyle))")
            };
        }
    }

    /// <summary>
    /// The page's declarations — only the ones the value HAS: a regular font says nothing about weight, style or
    /// decoration, so the element keeps its own (a heading stays bold under a regular body font).
    /// </summary>
    public IReadOnlyList<(string Property, string Value)> Css
    {
        get
        {
            var css = new List<(string, string)>
            {
                ("font-family", $"\"{Family}\""),
                ("font-size", SizeText + "pt")
            };

            if (Bold) css.Add(("font-weight", "bold"));
            if (Italic) css.Add(("font-style", "italic"));

            var decoration = string.Join(" ", new[] { (Underline, "underline"), (Strikeout, "line-through") }
                .Where(d => d.Item1).Select(d => d.Item2));
            if (decoration.Length > 0) css.Add(("text-decoration", decoration));

            return css;
        }
    }

    /// <summary>Parses FontConverter invariant text; false (Degraded) for anything else.</summary>
    public static bool TryParse(string? value, out FormFontValue font)
    {
        font = null!;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length < 2)
        {
            return false;
        }

        var family = parts[0].Trim(' ');
        if (family.Length == 0 || !family.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-'))
        {
            return false;
        }

        var sizeText = parts[1].Trim(' ');
        if (sizeText.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            sizeText = sizeText[..^2].TrimEnd(' ');
        }

        // ⛔ At most two decimal places (code review M1): a 0.00001pt size re-emitted as `New Font(…, 0F)` — WinForms
        // throws on a zero em-size at run time, with the build green. Trailing zeros are not precision (9.750 is 9.75).
        if (!decimal.TryParse(sizeText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size) ||
            size <= 0 || size > 1000 || decimal.Round(size, 2) != size)
        {
            return false;
        }

        bool bold = false, italic = false, underline = false, strikeout = false;
        for (var i = 2; i < parts.Length; i++)
        {
            var style = parts[i].Trim(' ');
            if (i == 2)
            {
                if (!style.StartsWith("style=", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                style = style["style=".Length..].Trim(' ');
            }

            switch (style.ToLowerInvariant())
            {
                case "regular": break;
                case "bold": bold = true; break;
                case "italic": italic = true; break;
                case "underline": underline = true; break;
                case "strikeout": strikeout = true; break;
                default: return false;
            }
        }

        font = new FormFontValue(family, size, bold, italic, underline, strikeout);
        return true;
    }
}
