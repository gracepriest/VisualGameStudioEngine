namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Pure, headless-testable apply-side counterpart of <see cref="ColorMatchFinder"/>:
/// given a match's kind and its replace-range text, produces the replacement text
/// for a picked color. Extracted verbatim from <c>CodeEditorControl.OnColorPicked</c>
/// so the rewrite rules are pinned by tests instead of welded to the control:
/// <list type="bullet">
/// <item><see cref="ColorMatchKind.RgbCall"/> keeps the comma-count alpha heuristic
/// (original text had &gt;= 3 commas OR the picked alpha is translucent → four
/// components) and re-emits the closing paren because the replace range includes
/// it.</item>
/// <item><see cref="ColorMatchKind.VbHex"/> digit count is picked-alpha-driven:
/// a &lt; 255 → 8 digits <c>&amp;H{A}{R}{G}{B}</c>, else 6 digits
/// <c>&amp;H{R}{G}{B}</c> — uppercase X2.</item>
/// </list>
/// <see cref="ColorMatchKind.CppHex"/> and <see cref="ColorMatchKind.BraceInit"/>
/// have no rewrite rules yet (their patterns land with their branches) — they throw.
/// </summary>
public static class ColorTextRewriter
{
    /// <summary>
    /// Rewrites the replace-range text of a color match for the picked color.
    /// <paramref name="oldText"/> is exactly the match's replace range (RgbCall:
    /// components through the closing paren; VbHex: the whole &amp;H literal).
    /// </summary>
    public static string Rewrite(ColorMatchKind kind, string oldText, byte r, byte g, byte b, byte a)
    {
        switch (kind)
        {
            case ColorMatchKind.RgbCall:
            {
                // RGB/RGBA numeric arguments: rebuild the numeric values portion.
                // The replace range runs from the first R value through the closing
                // paren, so the paren is re-emitted. Alpha is kept when the original
                // already had one (>= 3 commas) or the picked color is translucent.
                var commaCount = oldText.Count(c => c == ',');
                if (commaCount >= 3 || a < 255)
                    return $"{r}, {g}, {b}, {a})";
                return $"{r}, {g}, {b})";
            }

            case ColorMatchKind.VbHex:
                // Hex color literal: digit count follows the PICKED alpha.
                if (a < 255)
                    return $"&H{a:X2}{r:X2}{g:X2}{b:X2}";
                return $"&H{r:X2}{g:X2}{b:X2}";

            default:
                throw new NotSupportedException(
                    $"ColorTextRewriter has no rewrite rule for {kind} yet.");
        }
    }
}
