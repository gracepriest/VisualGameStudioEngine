using System.Globalization;

namespace BasicLang.Forms;

/// <summary>
/// A <see cref="FormPropertyType.Padding"/> value (spec §2.2): <c>4</c> (all sides) or <c>4, 2, 4, 2</c> — Left, Top,
/// Right, Bottom, the order WinForms' <c>PaddingConverter</c> writes and <c>New Padding(l, t, r, b)</c> takes.
/// Non-negative whole numbers (a negative padding is not a CSS padding; such a value is Degraded, not clamped).
///
/// <para>⚠ The CSS shorthand runs in a DIFFERENT order — top, right, bottom, left — so <see cref="Css"/> reorders; the
/// one place that knows both orders is here.</para>
/// </summary>
public sealed record FormPaddingValue(int Left, int Top, int Right, int Bottom)
{
    public bool IsUniform => Left == Top && Top == Right && Right == Bottom;

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>4</c> when uniform, else <c>4, 2, 4, 2</c> — re-emitted from the parsed numbers.</summary>
    public string Canonical => IsUniform ? N(Left) : $"{N(Left)}, {N(Top)}, {N(Right)}, {N(Bottom)}";

    /// <summary>ONE statement's right-hand side (the fan-in rule): <c>New Padding(4)</c> / <c>New Padding(4, 2, 4, 2)</c>.</summary>
    public string WinFormsLiteral => IsUniform ? $"New Padding({N(Left)})" : $"New Padding({Canonical})";

    /// <summary><c>padding</c> in CSS order (top right bottom left), in pixels.</summary>
    public string Css => IsUniform ? $"{N(Left)}px" : $"{N(Top)}px {N(Right)}px {N(Bottom)}px {N(Left)}px";

    public static bool TryParse(string? value, out FormPaddingValue padding)
    {
        padding = null!;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length is not (1 or 4))
        {
            return false;
        }

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!FormPropertyDef.TryParseInt(parts[i], out numbers[i]) || numbers[i] < 0)
            {
                return false;
            }
        }

        padding = parts.Length == 1
            ? new FormPaddingValue(numbers[0], numbers[0], numbers[0], numbers[0])
            : new FormPaddingValue(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }
}
