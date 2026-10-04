using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using BasicLang.Forms;

namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>One entry in the colour drop-down's Web or System list: the name it writes, and a preview swatch.</summary>
/// <param name="Name">The value written into the document — the table's own spelling (<c>Red</c>, <c>Control</c>).</param>
/// <param name="Swatch">Preview only (slice 4 D-1e); never written. Null when the name has no colour this machine can show.</param>
public sealed record FormColorChoice(string Name, IBrush? Swatch);

/// <summary>The colour drop-down's tabs, in their order in the pop-up.</summary>
public enum FormColorTab
{
    Custom,
    Web,
    System
}

/// <summary>
/// What the colour editor (slice 4 D-1) offers and how it turns a picked colour into document text.
///
/// <para>⛔ Every list is the CATALOG's answer (D-1d): <see cref="FormKnownColors.Names"/> and
/// <see cref="FormSystemColors.Names"/>, each filtered through <see cref="FormPropertyDef.Accepts(string?, FormTarget)"/> on
/// the row's target. That hides the 25 system colours with no CSS on a web form and keeps every WinForms name — never a
/// hand list, so a drop-down can never offer a value Judge would then refuse.</para>
/// </summary>
public static class FormColorChoices
{
    /// <summary>The named colours (<c>System.Drawing.Color</c>'s properties) the row's target accepts, in the table's order.</summary>
    public static IReadOnlyList<FormColorChoice> Web(FormPropertyDef definition, FormTarget target) =>
        Offered(FormKnownColors.Names, definition, target);

    /// <summary>The Windows system colours the row's target accepts — all 33 on WinForms, the 8 with a CSS meaning on the web.</summary>
    public static IReadOnlyList<FormColorChoice> System(FormPropertyDef definition, FormTarget target) =>
        Offered(FormSystemColors.Names, definition, target);

    private static IReadOnlyList<FormColorChoice> Offered(IEnumerable<string> names, FormPropertyDef definition, FormTarget target) =>
        names.Where(name => definition.Accepts(name, target))
            .Select(name => new FormColorChoice(name, SwatchFor(name)))
            .ToList();

    /// <summary>
    /// Which tab the drop-down opens on — the one holding the row's current value, as VS's does: a system colour on
    /// System, a named colour on Web, anything else (hex, empty) on Custom.
    /// </summary>
    public static FormColorTab TabFor(string? value) =>
        string.IsNullOrWhiteSpace(value) ? FormColorTab.Custom
        : FormSystemColors.TryCanonical(value.Trim(), out _) ? FormColorTab.System
        : FormKnownColors.TryCanonical(value.Trim(), out _) ? FormColorTab.Web
        : FormColorTab.Custom;

    /// <summary>
    /// A colour picked on the Custom tab, as document text: <c>#RRGGBB</c> when opaque, <c>#AARRGGBB</c> otherwise (D-1c).
    /// Both are the catalog's canonical hex spellings, upper case.
    /// </summary>
    public static string ToDocumentText(Color color) =>
        color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>The preview brush for a document value, or null when it names no colour this machine can show (D-1e).</summary>
    public static IBrush? SwatchFor(string value) =>
        TryResolve(value, out var color) ? new ImmutableSolidColorBrush(color) : null;

    /// <summary>
    /// The colour a document value DISPLAYS as — preview only, never written (D-1e). Hex: <c>#rgb</c>, <c>#rrggbb</c>,
    /// <c>#aarrggbb</c>. A name: a WinForms named or system colour through <c>System.Drawing.Color.FromName</c> (on Windows
    /// a system colour is the LIVE one), else a name Avalonia's own table knows. ⚠ A CSS-only name the web accepts
    /// (<c>RebeccaPurple</c>) is in neither table: it previews as "?" — never as a guessed colour.
    /// </summary>
    public static bool TryResolve(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        if (value[0] == '#')
        {
            var digits = value[1..];
            if (!digits.All(Uri.IsHexDigit))
            {
                return false;
            }

            byte Hex(string two) => byte.Parse(two, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            switch (digits.Length)
            {
                case 3:
                    color = Color.FromRgb(Hex(new string(digits[0], 2)), Hex(new string(digits[1], 2)), Hex(new string(digits[2], 2)));
                    return true;
                case 6:
                    color = Color.FromRgb(Hex(digits[..2]), Hex(digits[2..4]), Hex(digits[4..6]));
                    return true;
                case 8:
                    color = Color.FromArgb(Hex(digits[..2]), Hex(digits[2..4]), Hex(digits[4..6]), Hex(digits[6..8]));
                    return true;
                default:
                    return false;
            }
        }

        if (FormKnownColors.TryCanonical(value, out var known) || FormSystemColors.TryCanonical(value, out known))
        {
            var drawing = global::System.Drawing.Color.FromName(known);
            if (drawing.IsKnownColor)
            {
                color = Color.FromArgb(drawing.A, drawing.R, drawing.G, drawing.B);
                return true;
            }
        }

        return Color.TryParse(value, out color);
    }
}
