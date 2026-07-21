using System.Text.RegularExpressions;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// The language a file is classified as for color-swatch detection.
/// </summary>
public enum ColorLanguage { None, BasicLang, Cpp }

/// <summary>
/// The kind of source pattern a <see cref="ColorMatch"/> was produced from.
/// <see cref="CppHex"/> and <see cref="BraceInit"/> are future kinds — the enum
/// members exist now so consumers can switch exhaustively, but no patterns emit
/// them yet.
/// </summary>
public enum ColorMatchKind { RgbCall, VbHex, CppHex, BraceInit }

/// <summary>
/// One detected color value in a single line of source text.
/// </summary>
/// <param name="Kind">Which pattern produced the match.</param>
/// <param name="ReplaceStart">Offset in the line text where the rewrite range begins.</param>
/// <param name="ReplaceLength">Length of the rewrite range.</param>
/// <param name="R">Red component.</param>
/// <param name="G">Green component.</param>
/// <param name="B">Blue component.</param>
/// <param name="A">Alpha component (255 when the source has none).</param>
/// <param name="HasAlphaComponent">
/// True when the source text itself carries an alpha component (a fourth numeric
/// argument, or an 8-digit hex literal).
/// </param>
public sealed record ColorMatch(
    ColorMatchKind Kind,
    int ReplaceStart,
    int ReplaceLength,
    byte R, byte G, byte B, byte A,
    bool HasAlphaComponent);

/// <summary>
/// Pure, headless-testable color-value detection extracted from
/// <c>InlineColorRenderer</c>. The renderer keeps only geometry (swatch layout,
/// scroll-offset math, click hit-testing); everything about WHAT counts as a color
/// in WHICH language lives here.
///
/// Replace-range semantics (the renderer's click-to-pick rewrite depends on these
/// exactly): <see cref="ColorMatchKind.RgbCall"/> spans from the first R digit
/// through the closing paren inclusive; <see cref="ColorMatchKind.VbHex"/> spans
/// from the <c>&amp;H</c> start through the end of the literal.
/// </summary>
public static class ColorMatchFinder
{
    /// <summary>
    /// Matches RGB triplets in function calls: FuncName(R, G, B) or FuncName(..., R, G, B) or
    /// FuncName(..., R, G, B, A). Captures the three or four numeric arguments at the end.
    /// </summary>
    private static readonly Regex RgbCallPattern = new(
        @"(\w+)\s*\(([^)]*?)\b(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*(\d{1,3}))?\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches VB-style hex color literals: &amp;HRRGGBB or &amp;HAARRGGBB
    /// </summary>
    private static readonly Regex HexColorPattern = new(
        @"&H([0-9A-Fa-f]{6,8})\b",
        RegexOptions.Compiled);

    /// <summary>
    /// The C-ABI export prefix in <c>VisualGameStudioEngine/framework.h</c>.
    /// BasicLang accepts it optionally (wrapper names AND raw exports light up);
    /// C++ REQUIRES it (unprefixed names never match — raylib's own geometry
    /// overloads like DrawRectangle(x, y, w, h) would otherwise false-positive).
    /// </summary>
    private const string EnginePrefix = "Framework_";

    /// <summary>
    /// Known engine functions that take color parameters (R, G, B or R, G, B, A at the end).
    /// Names are the framework.h export names with the <see cref="EnginePrefix"/> stripped.
    /// If empty, all matching patterns are treated as potential colors.
    /// </summary>
    private static readonly HashSet<string> ColorFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClearBackground", "DrawPixel", "DrawLine", "DrawCircle", "DrawCircleLines",
        "DrawRectangle", "DrawRectangleLines", "DrawTriangle", "DrawTriangleLines",
        "DrawText", "DrawTextEx", "DrawPoly", "DrawPolyLines",
        "DrawEllipse", "DrawEllipseLines", "DrawRing", "DrawRingLines",
        "DrawRectangleRounded", "DrawRectangleRoundedLines",
        "DrawRectangleGradientV", "DrawRectangleGradientH",
        "DrawRectangleGradientEx", "DrawLineBezier",
        "SetColor", "SetBackgroundColor", "SetForegroundColor",
        "Color", "NewColor", "MakeColor", "ColorFromRGB", "ColorFromRGBA",
        "DrawSprite", "DrawSpriteEx", "DrawTexture", "DrawTextureEx",
        "FillRectangle", "FillCircle", "FillEllipse", "FillTriangle",
        "SetPixel", "DrawString", "DrawLineEx", "DrawCircleSector",
        "DrawCircleSectorLines", "DrawCircleGradient",
        "DrawArc", "DrawArcLines", "SetTint", "SetTextColor",

        // framework.h audit (color-tail exports: params end in r, g, b [, a]).
        // Exports whose color is NOT the parameter tail (e.g. Camera_Flash,
        // Effects_Flash, Tween_Color, Lighting_SetDayAmbient, the Color_* utils,
        // Cutscene_SetDialogueColors) are deliberately absent — trailing non-color
        // args would misparse as components.
        "Atlas_DrawSprite", "Atlas_DrawSpriteEx", "Atlas_DrawSpritePro",
        "Batch_AddSprite", "Batch_AddSpriteSimple",
        "Cmd_SetBackgroundColor", "Cmd_SetTextColor", "Console_PrintColored",
        "DebugDraw_Arrow", "DebugDraw_Circle", "DebugDraw_CircleFilled",
        "DebugDraw_Cross", "DebugDraw_Grid", "DebugDraw_Line",
        "DebugDraw_Point", "DebugDraw_Rect", "DebugDraw_RectFilled",
        "DebugDraw_Text", "Debug_SetOverlayColor",
        "DrawBezierCubic", "DrawBezierQuad",
        "DrawGradientCircle", "DrawGradientLine", "DrawGradientRect4",
        "DrawGradientRectH", "DrawGradientRectV",
        "DrawSpline", "DrawTextCentered", "DrawTextExH", "DrawTextRight",
        "DrawTextureH", "DrawTextureNPatch", "DrawTexturePro", "DrawTextureProH",
        "DrawTextureRec", "DrawTextureRecH", "DrawTextureV", "DrawTextureVH",
        "DrawTextureExH",
        "Ecs_SetEmitterColorEnd", "Ecs_SetEmitterColorStart",
        "Ecs_SetEmitterColorStop", "Ecs_SetEmitterTrailColor", "Ecs_SetSpriteTint",
        "Effects_SetFadeColor", "Effects_SetTintColor", "Effects_SetVignetteColor",
        "Level_SetBackground", "Light_SetColor",
        "Lighting_SetAmbientColor", "Lighting_SetDirectionalColor",
        "Lighting_SetShadowColor",
        "Parallax_SetTint", "Path_DrawDebug",
        "Scene_SetTransitionColor", "Skeleton_Draw", "SpriteSheet_DrawFrame",
        "Trail_SetColor",
        "UI_SetBackgroundColor", "UI_SetBorderColor", "UI_SetDisabledColor",
        "UI_SetHoverColor", "UI_SetPressedColor", "UI_SetTextColor", "UI_SetTint"
    };

    /// <summary>
    /// Classifies a file path into a <see cref="ColorLanguage"/> via the canonical
    /// <see cref="LanguageFileTypes"/> map (never a hand-rolled extension list —
    /// this repo already has exactly two language-id maps, deliberately).
    /// Null paths and unknown extensions classify as <see cref="ColorLanguage.None"/>.
    /// </summary>
    public static ColorLanguage ClassifyFile(string? filePath)
    {
        if (LanguageFileTypes.IsBasicLangSourceFile(filePath)) return ColorLanguage.BasicLang;
        if (LanguageFileTypes.IsCppSourceFile(filePath)) return ColorLanguage.Cpp;
        return ColorLanguage.None;
    }

    /// <summary>
    /// Finds every color value in a single line of source text, using the pattern
    /// set for <paramref name="language"/>. BasicLang runs {RgbCall, VbHex} with the
    /// <see cref="EnginePrefix"/> optional on RgbCall names; Cpp runs {RgbCall} with
    /// the prefix REQUIRED (unprefixed names never match — that keeps raylib-style
    /// geometry calls like DrawRectangle(10, 20, 100, 50) swatch-free); None never
    /// matches.
    /// </summary>
    public static IReadOnlyList<ColorMatch> FindMatches(string lineText, ColorLanguage language)
    {
        if (language == ColorLanguage.None || string.IsNullOrEmpty(lineText))
            return Array.Empty<ColorMatch>();

        var results = new List<ColorMatch>();

        // Detect RGB patterns in function calls
        foreach (Match match in RgbCallPattern.Matches(lineText))
        {
            var funcName = match.Groups[1].Value;
            var hasEnginePrefix = funcName.StartsWith(EnginePrefix, StringComparison.OrdinalIgnoreCase);

            // C++ only ever calls the raw framework.h exports — the prefix is required.
            if (language == ColorLanguage.Cpp && !hasEnginePrefix)
                continue;

            // The whitelist stores prefix-stripped base names.
            var baseName = hasEnginePrefix ? funcName.Substring(EnginePrefix.Length) : funcName;

            // Only report matches for known color functions (or if list is empty, all matches)
            if (ColorFunctions.Count > 0 && !ColorFunctions.Contains(baseName))
                continue;

            if (!int.TryParse(match.Groups[3].Value, out int r) || r > 255) continue;
            if (!int.TryParse(match.Groups[4].Value, out int g) || g > 255) continue;
            if (!int.TryParse(match.Groups[5].Value, out int b) || b > 255) continue;

            int a = 255;
            if (match.Groups[6].Success && int.TryParse(match.Groups[6].Value, out int alpha) && alpha <= 255)
                a = alpha;

            // Replace range: first R digit through the closing paren inclusive.
            var replaceStart = match.Groups[3].Index;
            var replaceLength = match.Index + match.Length - replaceStart;

            results.Add(new ColorMatch(
                ColorMatchKind.RgbCall,
                replaceStart, replaceLength,
                (byte)r, (byte)g, (byte)b, (byte)a,
                HasAlphaComponent: match.Groups[6].Success));
        }

        // Detect hex color patterns (&H literals are BasicLang-only syntax)
        if (language != ColorLanguage.BasicLang)
            return results;

        foreach (Match match in HexColorPattern.Matches(lineText))
        {
            var hexStr = match.Groups[1].Value;
            byte r, g, b, a = 255;

            if (hexStr.Length == 8)
            {
                a = Convert.ToByte(hexStr.Substring(0, 2), 16);
                r = Convert.ToByte(hexStr.Substring(2, 2), 16);
                g = Convert.ToByte(hexStr.Substring(4, 2), 16);
                b = Convert.ToByte(hexStr.Substring(6, 2), 16);
            }
            else // 6 chars
            {
                r = Convert.ToByte(hexStr.Substring(0, 2), 16);
                g = Convert.ToByte(hexStr.Substring(2, 2), 16);
                b = Convert.ToByte(hexStr.Substring(4, 2), 16);
            }

            // Replace range: the &H start through the literal end.
            results.Add(new ColorMatch(
                ColorMatchKind.VbHex,
                match.Index, match.Length,
                r, g, b, a,
                HasAlphaComponent: hexStr.Length == 8));
        }

        return results;
    }
}
