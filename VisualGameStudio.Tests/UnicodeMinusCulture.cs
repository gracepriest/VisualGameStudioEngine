using System.Globalization;

namespace VisualGameStudio.Tests;

/// <summary>
/// The precondition every <c>[SetCulture("sv-SE")]</c> "no U+2212 in the output" test needs.
///
/// <para>⛔ sv-SE's NegativeSign is U+2212 only under ICU. Under NLS (Windows with
/// <c>System.Globalization.UseNls</c>, or an old runtime) it is the ASCII hyphen, and a test asserting
/// "the output uses an ASCII hyphen" then passes whether or not the code is culture-invariant — it
/// proves nothing. Such a run SKIPS with the reason rather than passing silently.</para>
/// </summary>
internal static class UnicodeMinusCulture
{
    /// <summary>U+2212 MINUS SIGN, built from its code point so no editor can store a lookalike.</summary>
    public static readonly string Minus = ((char)0x2212).ToString();

    /// <summary>Skip the current test unless the current culture formats negatives with U+2212.</summary>
    public static void Require()
    {
        var sign = CultureInfo.CurrentCulture.NumberFormat.NegativeSign;
        if (sign != Minus)
        {
            TestSkip.IgnoreEvenInsideMultiple(
                $"{CultureInfo.CurrentCulture.Name}'s NegativeSign is U+{(int)sign[0]:X4}, not U+2212 " +
                "(NLS globalization?) — this test would pass without the fix, so it proves nothing here.");
        }
    }
}
