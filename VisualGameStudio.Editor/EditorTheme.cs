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

    // Editor surface colors
    public static Color Background => IsDark ? Color.Parse("#1E1E1E") : Color.Parse("#FFFFFF");
    public static Color Foreground => IsDark ? Color.Parse("#D4D4D4") : Color.Parse("#000000");
    public static Color LineNumbersForeground => IsDark ? Color.Parse("#858585") : Color.Parse("#237893");
    public static Color CurrentLineBackground => IsDark ? Color.Parse("#2A2D2E") : Color.Parse("#EFF2F6");
    public static Color SelectionBackground => IsDark ? Color.Parse("#264F78") : Color.Parse("#ADD6FF");
    public static Color MarginBackground => IsDark ? Color.Parse("#1E1E1E") : Color.Parse("#F0F0F0");
    public static Color MinimapBackground => IsDark ? Color.Parse("#252526") : Color.Parse("#F3F3F3");

    /// <summary>
    /// Called by ThemeManager to switch between dark and light.
    /// </summary>
    public static void SetDark(bool isDark)
    {
        if (IsDark == isDark) return;
        IsDark = isDark;

        // Switch syntax highlighting colors to match the theme
        HighlightingLoader.UpdateForTheme(isDark);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
