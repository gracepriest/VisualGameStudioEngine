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
    /// Gets whether the current theme is the High Contrast variant.
    /// </summary>
    public static bool IsHighContrast { get; private set; }

    /// <summary>
    /// Gets the current theme name ("Dark", "Light", or "High Contrast").
    /// </summary>
    public static string CurrentTheme { get; private set; } = "Dark";

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
                IsHighContrast = false;
                break;
            case "High Contrast":
                variant = ThemeVariant.Dark; // Avalonia has no HC variant; use Dark base
                IsDark = true;
                IsHighContrast = true;
                break;
            default: // "Dark"
                variant = ThemeVariant.Dark;
                IsDark = true;
                IsHighContrast = false;
                break;
        }

        CurrentTheme = themeName;
        Application.Current.RequestedThemeVariant = variant;

        // Apply High Contrast overrides via style classes on the top-level window
        if (Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (IsHighContrast)
                {
                    if (!window.Classes.Contains("highContrast"))
                        window.Classes.Add("highContrast");
                }
                else
                {
                    window.Classes.Remove("highContrast");
                }
            }
        }

        // Update editor colors (Editor project doesn't reference Shell,
        // so we bridge via the static EditorTheme class)
        EditorTheme.SetTheme(IsDark, IsHighContrast);

        if (raiseEvent)
        {
            ThemeChanged?.Invoke(null, IsDark);
        }
    }
}
