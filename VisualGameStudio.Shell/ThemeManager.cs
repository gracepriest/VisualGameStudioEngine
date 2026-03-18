using Avalonia;
using Avalonia.Styling;
using VisualGameStudio.Editor;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell;

/// <summary>
/// Manages IDE theme switching (Dark, Light, High Contrast).
/// Applies Avalonia's RequestedThemeVariant and notifies the editor to update colors.
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// Raised when the theme changes so editors can update their colors.
    /// The bool parameter is true when the theme is a dark variant.
    /// </summary>
    public static event EventHandler<bool>? ThemeChanged;

    /// <summary>
    /// Gets whether the current theme is a dark variant.
    /// </summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>
    /// Applies the theme from the saved settings on startup.
    /// </summary>
    public static void ApplyFromSettings()
    {
        var settings = SettingsViewModel.LoadCurrentSettings();
        var theme = settings.SelectedTheme ?? "Dark";
        Apply(theme, raiseEvent: false);
    }

    /// <summary>
    /// Applies the named theme ("Dark", "Light", or "High Contrast").
    /// </summary>
    public static void Apply(string themeName, bool raiseEvent = true)
    {
        if (Application.Current == null) return;

        ThemeVariant variant;
        switch (themeName)
        {
            case "Light":
                variant = ThemeVariant.Light;
                IsDark = false;
                break;
            case "High Contrast":
                variant = ThemeVariant.Dark; // Avalonia has no HC variant; use Dark base
                IsDark = true;
                break;
            default: // "Dark"
                variant = ThemeVariant.Dark;
                IsDark = true;
                break;
        }

        Application.Current.RequestedThemeVariant = variant;

        // Update editor colors (Editor project doesn't reference Shell,
        // so we bridge via the static EditorTheme class)
        EditorTheme.SetDark(IsDark);

        if (raiseEvent)
        {
            ThemeChanged?.Invoke(null, IsDark);
        }
    }
}
