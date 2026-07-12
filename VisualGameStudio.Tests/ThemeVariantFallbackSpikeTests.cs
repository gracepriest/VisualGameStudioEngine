using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using NUnit.Framework;
using VisualGameStudio.Shell;

namespace VisualGameStudio.Tests;

/// <summary>
/// Spike + permanent regression test pinning the Avalonia 11.3.13 framework behaviour that the
/// High-Contrast palette depends on: a custom <see cref="ThemeVariant"/> whose inheritance parent
/// is <see cref="ThemeVariant.Dark"/> resolves keys present in its own theme dictionary, and
/// FALLS BACK to the Dark dictionary for keys it does not define.
///
/// Task 1.1 replaces the dead <c>Style.Resources</c> High-Contrast block with a third
/// <c>ThemeDictionary</c> keyed by <see cref="AppThemes.HighContrast"/>. That design is only correct
/// if this fallback semantics holds — so we prove it directly against the resource APIs (headless,
/// no Avalonia app needed). If a future Avalonia upgrade changes this, this test breaks loudly.
/// </summary>
[TestFixture]
public class ThemeVariantFallbackSpikeTests
{
    private static ResourceDictionary BuildRootWithThemeDictionaries(
        out ThemeVariant highContrast)
    {
        // The exact shape AppStyles.axaml uses: a root ResourceDictionary carrying
        // ThemeDictionaries for Dark and a custom HighContrast variant.
        highContrast = new ThemeVariant("HighContrast", ThemeVariant.Dark);

        var dark = new ResourceDictionary
        {
            { "IdeBg", new SolidColorBrush(Color.Parse("#1E1E1E")) },       // present in both
            { "IdeSelectionBg", new SolidColorBrush(Color.Parse("#264F78")) } // Dark-only (not in HC)
        };

        var hc = new ResourceDictionary
        {
            { "IdeBg", new SolidColorBrush(Color.Parse("#000000")) }        // HC override
            // deliberately no IdeSelectionBg -> must fall back to Dark
        };

        var root = new ResourceDictionary();
        root.ThemeDictionaries[ThemeVariant.Dark] = dark;
        root.ThemeDictionaries[highContrast] = hc;
        return root;
    }

    [Test]
    public void CustomVariant_PresentKey_ResolvesFromOwnDictionary()
    {
        var root = BuildRootWithThemeDictionaries(out var highContrast);

        var found = root.TryGetResource("IdeBg", highContrast, out var value);

        Assert.That(found, Is.True, "key present in the HC dictionary must resolve");
        var brush = (ISolidColorBrush)value!;
        Assert.That(brush.Color, Is.EqualTo(Color.Parse("#000000")),
            "IdeBg must come from the HC dictionary (black), not the Dark dictionary");
    }

    [Test]
    public void CustomVariant_MissingKey_FallsBackToInheritedDarkDictionary()
    {
        var root = BuildRootWithThemeDictionaries(out var highContrast);

        // IdeSelectionBg is NOT in the HC dictionary; the custom variant inherits Dark,
        // so resolution must walk InheritVariant and hit the Dark dictionary.
        var found = root.TryGetResource("IdeSelectionBg", highContrast, out var value);

        Assert.That(found, Is.True,
            "a key absent from the HC dictionary must fall back to the inherited Dark dictionary");
        var brush = (ISolidColorBrush)value!;
        Assert.That(brush.Color, Is.EqualTo(Color.Parse("#264F78")),
            "the fallback value must be the Dark dictionary's value");
    }

    [Test]
    public void AppThemes_HighContrast_InheritsFromDark()
    {
        // Pins the AppThemes definition the AXAML x:Static binding and ThemeManager rely on.
        Assert.That(AppThemes.HighContrast.InheritVariant, Is.EqualTo(ThemeVariant.Dark),
            "HighContrast must inherit Dark so undefined keys resolve from the Dark palette");
        Assert.That(AppThemes.HighContrast.Key, Is.EqualTo("HighContrast"));
    }
}
