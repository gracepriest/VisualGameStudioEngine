namespace VisualGameStudio.Core.Constants;

public static class AppConstants
{
    public const string AppName = "Visual Game Studio";
    public const string AppVersion = "1.0.0";

    public const string DefaultFontFamily = "Cascadia Code, Consolas, Courier New, monospace";
    public const double DefaultFontSize = 14.0;
    public const int DefaultTabSize = 4;

    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";
    public const string DefaultTheme = DarkTheme;

    public const string SettingsFileName = "settings.json";
    public const string LayoutFileName = "layout.json";
    public const string RecentProjectsFileName = "recent.json";

    public static class FileFilters
    {
        public static readonly (string Name, string[] Extensions) BasicLangFiles =
            ("BasicLang Files", new[] { "*.bas", "*.bl", "*.basic" });

        public static readonly (string Name, string[] Extensions) ProjectFiles =
            ("BasicLang Project", new[] { "*.blproj" });

        public static readonly (string Name, string[] Extensions) SolutionFiles =
            ("BasicLang Solution", new[] { "*.blsln" });

        public static readonly (string Name, string[] Extensions) AllFiles =
            ("All Files", new[] { "*.*" });
    }
}
