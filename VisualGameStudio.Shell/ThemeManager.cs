using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using VisualGameStudio.Core.Abstractions.Services;
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

    static ThemeManager()
    {
        // ThemeManager is the sole consumer of the color-theme setting: it reads
        // workbench.colorTheme at startup (ResolveStartupThemeName) and applies the matching
        // Avalonia RequestedThemeVariant. Name that consumer so the Phase 3 contract test knows
        // this dialog setting is live.
        SettingsConsumerRegistry.RegisterConsumer(
            "workbench.colorTheme",
            "ThemeManager.ResolveStartupThemeName → RequestedThemeVariant");
    }

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
    /// Applies the theme from the saved settings on startup. Reads the single store
    /// (~/.vgs via <see cref="ISettingsService"/>) — no longer the retired legacy %APPDATA% file.
    /// Must be called after the DI container is built and <c>LoadUserSettingsAtStartup</c> has run
    /// (see <c>App.OnFrameworkInitializationCompleted</c>) so the store is populated from disk.
    /// </summary>
    public static void ApplyFromSettings()
    {
        var service = App.Services?.GetService(typeof(ISettingsService)) as ISettingsService;
        var theme = ResolveStartupThemeName(service);
        Apply(theme, raiseEvent: false);
    }

    /// <summary>
    /// Resolves the theme name to apply at startup from the single settings store, running the
    /// one-time legacy-theme migration first. Pure (no Avalonia dependency) so it is unit-testable;
    /// <see cref="ApplyFromSettings"/> wraps it and hands the result to <see cref="Apply"/>.
    /// </summary>
    public static string ResolveStartupThemeName(ISettingsService? service)
        => ResolveStartupThemeName(service, SettingsViewModel.LegacyThemeStorePath);

    /// <summary>
    /// Test/seam overload of <see cref="ResolveStartupThemeName(ISettingsService?)"/> that takes an
    /// explicit legacy-store path so tests never touch the real %APPDATA% file.
    /// </summary>
    public static string ResolveStartupThemeName(ISettingsService? service, string? legacyPath)
    {
        if (service == null) return "Dark";

        if (!string.IsNullOrEmpty(legacyPath))
        {
            SettingsViewModel.MigrateLegacyThemeIfNeeded(service, legacyPath);
        }

        // "Dark" mirrors the SettingsService schema default for workbench.colorTheme.
        return service.Get<string>("workbench.colorTheme", "Dark");
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
                // Custom variant whose inheritance parent is Dark: keys defined in the
                // HighContrast ThemeDictionary (AppStyles.axaml) win; everything else falls
                // back to the Dark palette. (Proven in ThemeVariantFallbackSpikeTests.)
                variant = AppThemes.HighContrast;
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
        SweepOpenWindowsHighContrastClass();

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

    #region Window class registration

    /// <summary>
    /// Backing flag so <see cref="EnsureGlobalWindowClassHook"/> installs its class handler once.
    /// </summary>
    private static bool _globalWindowHookInstalled;

    /// <summary>
    /// Installs a process-wide class handler on <see cref="Control.LoadedEvent"/> so EVERY
    /// <see cref="Window"/> — the main window, every dialog, and Dock's floating <c>HostWindow</c>s,
    /// present and future — has the <c>highContrast</c> style class stamped (or removed) to match the
    /// active theme the instant it loads. Idempotent; safe to call more than once.
    ///
    /// Why this is the reliable route (verified against the Avalonia 11.3.13 source): <c>LoadedEvent</c>
    /// is a <see cref="Avalonia.Interactivity.RoutingStrategies.Direct"/> routed event raised on each
    /// control — including the Window itself — as it loads, and
    /// <c>RoutedEvent.AddClassHandler&lt;TTarget&gt;</c> matches the sender by <c>IsInstanceOfType</c>,
    /// so a handler registered for <see cref="Window"/> also fires for every Window subclass (e.g.
    /// Dock's <c>HostWindow</c>). This is the one global per-window hook Avalonia exposes —
    /// <c>Window.Opened</c> is a plain CLR event with no global equivalent — so it covers all windows
    /// from a single site instead of stamping each <c>ShowDialog</c> caller by hand.
    /// </summary>
    public static void EnsureGlobalWindowClassHook()
    {
        if (_globalWindowHookInstalled) return;

        Control.LoadedEvent.AddClassHandler<Window>((window, _) => Register(window));
        _globalWindowHookInstalled = true;
    }

    /// <summary>
    /// Stamps (or removes) the <c>highContrast</c> style class on a single <paramref name="window"/>
    /// to match the current <see cref="IsHighContrast"/> state. Invoked by the global Loaded hook
    /// (see <see cref="EnsureGlobalWindowClassHook"/>) for every window, and directly at the main
    /// window and Dock <c>HostWindow</c> creation sites so the class is already present before the
    /// window's first render.
    /// </summary>
    public static void Register(Window window)
    {
        if (window == null) return;
        ApplyHighContrastClass(window.Classes, IsHighContrast);
    }

    /// <summary>
    /// Pure add/remove decision for the <c>highContrast</c> class, factored onto an
    /// <see cref="IList{T}"/> so it is unit-testable without constructing a <see cref="Window"/> (the
    /// test suite has no Avalonia platform). Adds the class when <paramref name="highContrast"/> is
    /// true and it is absent; removes it otherwise. Idempotent in both directions.
    /// </summary>
    public static void ApplyHighContrastClass(IList<string> classes, bool highContrast)
    {
        if (classes == null) return;
        const string highContrastClass = "highContrast";
        if (highContrast)
        {
            if (!classes.Contains(highContrastClass))
                classes.Add(highContrastClass);
        }
        else
        {
            classes.Remove(highContrastClass);
        }
    }

    #endregion

    #region Private Implementation

    private static void ApplyVsCodeTheme(LoadedTheme theme, bool raiseEvent)
    {
        if (Application.Current == null) return;

        CurrentVsCodeTheme = theme;
        IsDark = theme.Type != ThemeType.Light;
        IsHighContrast = theme.Type == ThemeType.HighContrastDark || theme.Type == ThemeType.HighContrastLight;
        CurrentTheme = theme.Label;

        Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        SweepOpenWindowsHighContrastClass();
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
            "IdeListHover", "IdeListSelected", "IdeFooterBg", "IdeFooterBorder",
            "IdeStatusBarBg", "IdeStatusBarDebugBg",
            // Task 1.6 surface keys — kept in sync so imported VS Code themes can retint and
            // cleanly release editor/accent/muted/overlay/activity-bar/semantic surfaces.
            "IdeEditorBg", "IdeAccent", "IdeFgMuted", "IdeFgSubtle", "IdeOverlayBg",
            "IdeActivityBarBg", "IdeActivityBarFg",
            "IdeWarningBg", "IdeWarningBorder", "IdeWarningFg",
            "IdeSuccessBg", "IdeSuccessBorder", "IdeSuccessFg",
            "IdeErrorBg", "IdeErrorBorder", "IdeErrorFg",
            "IdeDiffAddedBg", "IdeDiffRemovedBg"
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

    /// <summary>
    /// Re-stamps the <c>highContrast</c> class across all currently open windows to match the active
    /// theme. Runs at theme-switch time (from <see cref="Apply"/>/<see cref="ApplyVsCodeTheme"/>) to
    /// update windows that are already open; windows opened later are handled by the global Loaded
    /// hook (see <see cref="EnsureGlobalWindowClassHook"/>). Removes the class when leaving High
    /// Contrast, so switching out is covered too.
    /// </summary>
    private static void SweepOpenWindowsHighContrastClass()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                ApplyHighContrastClass(window.Classes, IsHighContrast);
            }
        }
    }

    #endregion
}
