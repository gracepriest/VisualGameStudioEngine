using Avalonia.Media;
using VisualGameStudio.Editor.Highlighting;

namespace VisualGameStudio.Editor;

/// <summary>
/// Provides theme colors for the code editor.
/// Updated by the Shell's ThemeManager when the user switches themes.
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

    // Editor surface colors
    public static Color Background => IsHighContrast ? Color.Parse("#000000")
        : IsDark ? Color.Parse("#1E1E1E") : Color.Parse("#FFFFFF");

    public static Color Foreground => IsHighContrast ? Color.Parse("#FFFFFF")
        : IsDark ? Color.Parse("#D4D4D4") : Color.Parse("#000000");

    public static Color LineNumbersForeground => IsHighContrast ? Color.Parse("#FFFFFF")
        : IsDark ? Color.Parse("#858585") : Color.Parse("#237893");

    public static Color CurrentLineBackground => IsHighContrast ? Color.Parse("#1A1A2E")
        : IsDark ? Color.Parse("#2A2D2E") : Color.Parse("#EFF2F6");

    public static Color SelectionBackground => IsHighContrast ? Color.Parse("#264F78")
        : IsDark ? Color.Parse("#264F78") : Color.Parse("#ADD6FF");

    public static Color MarginBackground => IsHighContrast ? Color.Parse("#000000")
        : IsDark ? Color.Parse("#1E1E1E") : Color.Parse("#F0F0F0");

    public static Color MinimapBackground => IsHighContrast ? Color.Parse("#000000")
        : IsDark ? Color.Parse("#252526") : Color.Parse("#F3F3F3");

    /// <summary>
    /// Called by ThemeManager to switch between dark, light, and high contrast.
    /// </summary>
    public static void SetTheme(bool isDark, bool isHighContrast)
    {
        bool changed = IsDark != isDark || IsHighContrast != isHighContrast;
        IsDark = isDark;
        IsHighContrast = isHighContrast;

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
}
