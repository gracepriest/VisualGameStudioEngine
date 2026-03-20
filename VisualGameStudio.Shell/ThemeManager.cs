using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using VisualGameStudio.Core.Models.Extensions;
using VisualGameStudio.Editor;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell;

/// <summary>
/// Manages IDE theme switching (Dark, Light, High Contrast, and VS Code extension themes).
/// Applies Avalonia's RequestedThemeVariant, updates DynamicResource brushes, and notifies
/// the editor to update colors.
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
    /// Gets the current theme name ("Dark", "Light", "High Contrast", or an extension theme name).
    /// </summary>
    public static string CurrentTheme { get; private set; } = "Dark";

    /// <summary>
    /// Gets the currently applied VS Code theme, or null if a built-in theme is active.
    /// </summary>
    public static LoadedTheme? CurrentVsCodeTheme { get; private set; }

    /// <summary>
    /// Loaded VS Code extension themes available for selection.
    /// Key is the display label, value is the loaded theme data.
    /// </summary>
    private static readonly Dictionary<string, LoadedTheme> _extensionThemes = new();

    /// <summary>
    /// Theme loader instance for parsing VS Code theme JSON files.
    /// </summary>
    private static readonly VsCodeThemeLoader _themeLoader = new();

    /// <summary>
    /// Gets the names of all loaded extension themes.
    /// </summary>
    public static IReadOnlyList<string> ExtensionThemeNames => _extensionThemes.Keys.ToList();

    /// <summary>
    /// Gets all available theme names (built-in + extension themes).
    /// </summary>
    public static IReadOnlyList<string> AllThemeNames
    {
        get
        {
            var names = new List<string> { "Dark", "Light", "High Contrast" };
            names.AddRange(_extensionThemes.Keys.OrderBy(k => k));
            return names;
        }
    }

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
    /// Applies the named theme ("Dark", "Light", "High Contrast", or a loaded extension theme name).
    /// </summary>
    public static void Apply(string themeName, bool raiseEvent = true)
    {
        if (Application.Current == null) return;

        // Check if this is an extension theme
        if (_extensionThemes.TryGetValue(themeName, out var extensionTheme))
        {
            ApplyVsCodeTheme(extensionTheme, raiseEvent);
            return;
        }

        // Built-in theme
        CurrentVsCodeTheme = null;

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
        ApplyHighContrastClass();

        // Clear any custom resource overrides from a previous extension theme
        ClearResourceOverrides();

        // Update editor colors (Editor project doesn't reference Shell,
        // so we bridge via the static EditorTheme class)
        EditorTheme.SetTheme(IsDark, IsHighContrast);

        if (raiseEvent)
        {
            ThemeChanged?.Invoke(null, IsDark);
        }
    }

    /// <summary>
    /// Loads a VS Code theme JSON file and registers it as an available theme.
    /// Returns the display label if successful, null otherwise.
    /// </summary>
    public static async Task<string?> LoadVsCodeThemeFileAsync(string filePath)
    {
        try
        {
            var info = _themeLoader.PeekThemeInfo(filePath);
            if (info == null) return null;

            var (name, type) = info.Value;
            var uiTheme = type == "light" ? "vs" : type == "hc" ? "hc-black" : "vs-dark";

            var theme = await _themeLoader.LoadThemeAsync(filePath, name, name, uiTheme, "user");
            if (theme == null) return null;

            _extensionThemes[theme.Label] = theme;
            return theme.Label;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a VS Code theme from JSON content and registers it.
    /// </summary>
    public static string? LoadVsCodeThemeFromJson(string json, string displayName)
    {
        try
        {
            var type = "vs-dark";
            if (json.Contains("\"type\"") && json.Contains("\"light\""))
                type = "vs";

            var theme = _themeLoader.LoadThemeFromJson(json, displayName, displayName, type, "user");
            if (theme == null) return null;

            _extensionThemes[theme.Label] = theme;
            return theme.Label;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Registers a pre-loaded theme for selection.
    /// </summary>
    public static void RegisterExtensionTheme(LoadedTheme theme)
    {
        _extensionThemes[theme.Label] = theme;
    }

    /// <summary>
    /// Removes a loaded extension theme.
    /// </summary>
    public static bool RemoveExtensionTheme(string label)
    {
        return _extensionThemes.Remove(label);
    }

    /// <summary>
    /// Scans a directory for VS Code theme JSON files and loads them all.
    /// Returns the number of themes loaded.
    /// </summary>
    public static async Task<int> LoadThemesFromDirectoryAsync(string directory)
    {
        if (!Directory.Exists(directory)) return 0;

        var count = 0;
        var jsonFiles = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);

        foreach (var file in jsonFiles)
        {
            var label = await LoadVsCodeThemeFileAsync(file);
            if (label != null) count++;
        }

        return count;
    }

    #region Private Implementation

    private static void ApplyVsCodeTheme(LoadedTheme theme, bool raiseEvent)
    {
        if (Application.Current == null) return;

        CurrentVsCodeTheme = theme;
        IsDark = theme.Type != ThemeType.Light;
        IsHighContrast = theme.Type == ThemeType.HighContrastDark || theme.Type == ThemeType.HighContrastLight;
        CurrentTheme = theme.Label;

        Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        ApplyHighContrastClass();
        ApplyResourceOverrides(theme);

        var tokenColorMap = _themeLoader.ExtractTokenColorMap(theme);
        EditorTheme.ApplyCustomColors(theme.Colors, tokenColorMap, IsDark);

        if (raiseEvent)
        {
            ThemeChanged?.Invoke(null, IsDark);
        }
    }

    private static void ApplyResourceOverrides(LoadedTheme theme)
    {
        if (Application.Current == null) return;

        var ideResources = _themeLoader.MapToIdeResourceKeys(theme);
        foreach (var kvp in ideResources)
        {
            try
            {
                var color = Color.Parse(kvp.Value);
                Application.Current.Resources[kvp.Key] = new SolidColorBrush(color);
            }
            catch { }
        }

        var detailedResources = _themeLoader.MapToResourceKeys(theme);
        foreach (var kvp in detailedResources)
        {
            try
            {
                var color = Color.Parse(kvp.Value);
                Application.Current.Resources[kvp.Key] = new SolidColorBrush(color);
            }
            catch { }
        }
    }

    private static void ClearResourceOverrides()
    {
        if (Application.Current == null) return;

        var keysToRemove = new[]
        {
            "IdeBg", "IdeFg", "IdePanelBg", "IdeMenuBg", "IdeBorder", "IdeHoverBg",
            "IdeInputBg", "IdeInputBorder", "IdeSelectionBg", "IdeHeaderBg",
            "IdeThumbBg", "IdeThumbHover", "IdeContextBg", "IdeContextBorder",
            "IdeSecondaryBg", "IdeSecondaryFg", "IdeSecondaryBorder", "IdeSecondaryHover",
            "IdeListHover", "IdeListSelected", "IdeFooterBg", "IdeFooterBorder"
        };

        foreach (var key in keysToRemove)
        {
            Application.Current.Resources.Remove(key);
        }

        foreach (var resourceKey in VsCodeColorMapping.ColorKeyMap.Values)
        {
            Application.Current.Resources.Remove(resourceKey);
        }
    }

    private static void ApplyHighContrastClass()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
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
    }

    #endregion
}
