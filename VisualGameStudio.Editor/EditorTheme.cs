using Avalonia.Media;
using VisualGameStudio.Editor.Highlighting;

namespace VisualGameStudio.Editor;

/// <summary>
/// Provides theme colors for the code editor.
/// Updated by the Shell's ThemeManager when the user switches themes.
/// Supports custom color overrides from VS Code extension themes.
/// </summary>
public static class EditorTheme
{
    /// <summary>
    /// Raised when the theme changes. Subscribe to re-apply editor colors.
    /// </summary>
    public static event EventHandler? ThemeChanged;

    public static bool IsDark { get; private set; } = true;

    /// <summary>
    /// Gets whether the current theme is the High Contrast accessibility variant.
    /// </summary>
    public static bool IsHighContrast { get; private set; }

    /// <summary>
    /// Gets whether a custom VS Code theme is currently active (overriding built-in colors).
    /// </summary>
    public static bool IsCustomTheme { get; private set; }

    // Custom color overrides from VS Code themes (null = use built-in default)
    private static Color? _customBackground;
    private static Color? _customForeground;
    private static Color? _customLineNumbersForeground;
    private static Color? _customCurrentLineBackground;
    private static Color? _customSelectionBackground;
    private static Color? _customMarginBackground;
    private static Color? _customMinimapBackground;
    private static Color? _customCursorForeground;
    private static Color? _customLineNumberActiveForeground;

    // Syntax highlighting color overrides from VS Code tokenColors
    private static Color? _customCommentColor;
    private static Color? _customKeywordColor;
    private static Color? _customStringColor;
    private static Color? _customNumberColor;
    private static Color? _customTypeColor;
    private static Color? _customFunctionColor;
    private static Color? _customVariableColor;
    private static Color? _customOperatorColor;
    private static Color? _customControlKeywordColor;
    private static Color? _customConstantColor;

    private static bool _customCommentItalic;
    private static bool _customKeywordBold;

    // Editor surface colors
    public static Color Background => _customBackground
        ?? (IsHighContrast ? Color.Parse("#000000")
        : IsDark ? Color.Parse("#1E1E1E") : Color.Parse("#FFFFFF"));

    public static Color Foreground => _customForeground
        ?? (IsHighContrast ? Color.Parse("#FFFFFF")
        : IsDark ? Color.Parse("#D4D4D4") : Color.Parse("#000000"));

    public static Color LineNumbersForeground => _customLineNumbersForeground
        ?? (IsHighContrast ? Color.Parse("#FFFFFF")
        : IsDark ? Color.Parse("#858585") : Color.Parse("#237893"));

    public static Color CurrentLineBackground => _customCurrentLineBackground
        ?? (IsHighContrast ? Color.Parse("#1A1A2E")
        : IsDark ? Color.Parse("#2A2D2E") : Color.Parse("#EFF2F6"));

    public static Color SelectionBackground => _customSelectionBackground
        ?? (IsHighContrast ? Color.Parse("#264F78")
        : IsDark ? Color.Parse("#264F78") : Color.Parse("#ADD6FF"));

    public static Color MarginBackground => _customMarginBackground
        ?? (IsHighContrast ? Color.Parse("#000000")
        : IsDark ? Color.Parse("#1E1E1E") : Color.Parse("#F0F0F0"));

    public static Color MinimapBackground => _customMinimapBackground
        ?? (IsHighContrast ? Color.Parse("#000000")
        : IsDark ? Color.Parse("#252526") : Color.Parse("#F3F3F3"));

    public static Color CursorForeground => _customCursorForeground ?? Foreground;

    public static Color LineNumberActiveForeground => _customLineNumberActiveForeground ?? Foreground;

    // Syntax token colors (used by HighlightingLoader for dynamic xshd generation)
    public static Color? CommentColor => _customCommentColor;
    public static Color? KeywordColor => _customKeywordColor;
    public static Color? StringColor => _customStringColor;
    public static Color? NumberColor => _customNumberColor;
    public static Color? TypeColor => _customTypeColor;
    public static Color? FunctionColor => _customFunctionColor;
    public static Color? VariableColor => _customVariableColor;
    public static Color? OperatorColor => _customOperatorColor;
    public static Color? ControlKeywordColor => _customControlKeywordColor;
    public static Color? ConstantColor => _customConstantColor;
    public static bool CommentItalic => _customCommentItalic;
    public static bool KeywordBold => _customKeywordBold;

    /// <summary>
    /// Called by ThemeManager to switch between dark, light, and high contrast.
    /// Clears any custom VS Code theme overrides.
    /// </summary>
    public static void SetTheme(bool isDark, bool isHighContrast)
    {
        bool changed = IsDark != isDark || IsHighContrast != isHighContrast || IsCustomTheme;
        IsDark = isDark;
        IsHighContrast = isHighContrast;

        // Clear custom overrides when switching to a built-in theme
        ClearCustomColors();

        if (changed)
        {
            // Switch syntax highlighting colors to match the theme
            HighlightingLoader.UpdateForTheme(isDark, isHighContrast);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Called by ThemeManager to switch between dark and light (backward compatibility).
    /// </summary>
    public static void SetDark(bool isDark)
    {
        SetTheme(isDark, isHighContrast: false);
    }

    /// <summary>
    /// Applies custom editor colors from a VS Code theme.
    /// Overrides the built-in dark/light/HC color defaults.
    /// </summary>
    /// <param name="editorColors">Map of VS Code color keys to hex values.</param>
    /// <param name="tokenColors">Map of TextMate scope names to (foreground hex, bold, italic) tuples.</param>
    /// <param name="isDark">Whether the theme is a dark variant.</param>
    public static void ApplyCustomColors(
        Dictionary<string, string> editorColors,
        Dictionary<string, (string? foreground, bool bold, bool italic)> tokenColors,
        bool isDark)
    {
        IsDark = isDark;
        IsHighContrast = false;
        IsCustomTheme = true;

        // Apply editor surface colors
        _customBackground = TryParseColor(editorColors, "editor.background");
        _customForeground = TryParseColor(editorColors, "editor.foreground");
        _customLineNumbersForeground = TryParseColor(editorColors, "editorLineNumber.foreground");
        _customLineNumberActiveForeground = TryParseColor(editorColors, "editorLineNumber.activeForeground");
        _customCurrentLineBackground = TryParseColor(editorColors, "editor.lineHighlightBackground");
        _customSelectionBackground = TryParseColor(editorColors, "editor.selectionBackground");
        _customMarginBackground = TryParseColor(editorColors, "editorGutter.background") ?? _customBackground;
        _customMinimapBackground = TryParseColor(editorColors, "minimap.background") ?? _customBackground;
        _customCursorForeground = TryParseColor(editorColors, "editorCursor.foreground");

        // Apply token colors by matching TextMate scopes
        ApplyTokenColor(tokenColors, new[] { "comment", "comment.line", "comment.block" },
            out _customCommentColor, out _, out _customCommentItalic);

        ApplyTokenColor(tokenColors, new[] { "keyword", "keyword.other", "storage.type" },
            out _customKeywordColor, out _customKeywordBold, out _);

        ApplyTokenColor(tokenColors, new[] { "keyword.control", "keyword.control.flow" },
            out _customControlKeywordColor, out _, out _);

        ApplyTokenColor(tokenColors, new[] { "string", "string.quoted", "string.quoted.double" },
            out _customStringColor, out _, out _);

        ApplyTokenColor(tokenColors, new[] { "constant.numeric", "constant.numeric.integer", "constant.numeric.float" },
            out _customNumberColor, out _, out _);

        ApplyTokenColor(tokenColors, new[] { "entity.name.type", "entity.name.class", "support.type", "storage.type" },
            out _customTypeColor, out _, out _);

        ApplyTokenColor(tokenColors, new[] { "entity.name.function", "support.function", "meta.function-call" },
            out _customFunctionColor, out _, out _);

        ApplyTokenColor(tokenColors, new[] { "variable", "variable.other", "variable.parameter" },
            out _customVariableColor, out _, out _);

        ApplyTokenColor(tokenColors, new[] { "keyword.operator", "punctuation" },
            out _customOperatorColor, out _, out _);

        ApplyTokenColor(tokenColors, new[] { "constant.language", "constant.other" },
            out _customConstantColor, out _, out _);

        // Update syntax highlighting with the new custom colors
        HighlightingLoader.UpdateForCustomTheme();

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all custom color overrides, reverting to built-in defaults.
    /// </summary>
    private static void ClearCustomColors()
    {
        IsCustomTheme = false;
        _customBackground = null;
        _customForeground = null;
        _customLineNumbersForeground = null;
        _customLineNumberActiveForeground = null;
        _customCurrentLineBackground = null;
        _customSelectionBackground = null;
        _customMarginBackground = null;
        _customMinimapBackground = null;
        _customCursorForeground = null;

        _customCommentColor = null;
        _customKeywordColor = null;
        _customStringColor = null;
        _customNumberColor = null;
        _customTypeColor = null;
        _customFunctionColor = null;
        _customVariableColor = null;
        _customOperatorColor = null;
        _customControlKeywordColor = null;
        _customConstantColor = null;
        _customCommentItalic = false;
        _customKeywordBold = false;
    }

    private static Color? TryParseColor(Dictionary<string, string> colors, string key)
    {
        if (colors.TryGetValue(key, out var hex) && !string.IsNullOrEmpty(hex))
        {
            try
            {
                return Color.Parse(hex);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static void ApplyTokenColor(
        Dictionary<string, (string? foreground, bool bold, bool italic)> tokenColors,
        string[] scopeCandidates,
        out Color? color,
        out bool bold,
        out bool italic)
    {
        color = null;
        bold = false;
        italic = false;

        foreach (var scope in scopeCandidates)
        {
            if (tokenColors.TryGetValue(scope, out var style) && style.foreground != null)
            {
                try
                {
                    color = Color.Parse(style.foreground);
                    bold = style.bold;
                    italic = style.italic;
                    return;
                }
                catch
                {
                    // Skip invalid color
                }
            }
        }
    }
}
