using Avalonia.Styling;

namespace VisualGameStudio.Shell;

/// <summary>
/// Custom Avalonia <see cref="ThemeVariant"/>s defined by the IDE beyond the built-in Light/Dark.
/// </summary>
public static class AppThemes
{
    /// <summary>
    /// The High-Contrast theme variant. Its inheritance parent is <see cref="ThemeVariant.Dark"/>,
    /// so any resource key NOT defined in the High-Contrast <c>ThemeDictionary</c>
    /// (AppStyles.axaml) resolves from the Dark dictionary. This lets the HC dictionary override
    /// only the keys that need pure black/white/yellow values while inheriting the rest.
    /// </summary>
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);
}
