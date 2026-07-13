using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 2.7 — the theme-flow leftovers:
///
/// * the unknown-theme fallback (a saved theme whose file is gone resolves to Dark, not a silent
///   wrong render) — pinned via the pure <see cref="ThemeManager.IsKnownTheme"/> /
///   <see cref="ThemeManager.ResolveEffectiveThemeName"/> seams (Apply early-returns headless);
/// * imported theme file paths round-trip through <c>workbench.importedThemes</c> and are
///   re-registered at startup by <see cref="ThemeManager.ReloadImportedThemesAsync"/>, which prunes
///   files that no longer exist;
/// * a raw-JSON edit that changes <c>workbench.colorTheme</c> reaches the store (the detection input
///   the Settings dialog now routes through <see cref="ThemeManager.Apply"/>);
/// * the theme keys name consumers in <see cref="SettingsConsumerRegistry"/>.
///
/// The visual apply (<see cref="ThemeManager.Apply"/>) no-ops when Application.Current is null (true
/// in this headless suite), so these tests assert the resolution logic and the persisted store, not
/// the applied Avalonia variant.
/// </summary>
[TestFixture]
public class SettingsThemeFlowWiringTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsThemeFlow_{Guid.NewGuid()}");
        Directory.CreateDirectory(_homeDir);
        _service = new SettingsService(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { _service.Dispose(); } catch { /* ignore */ }
        try { if (Directory.Exists(_homeDir)) Directory.Delete(_homeDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    // ---- Unknown-theme fallback resolution (pure seam) ----

    [Test]
    public void BuiltInThemes_AreKnown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ThemeManager.IsKnownTheme("Dark"), Is.True);
            Assert.That(ThemeManager.IsKnownTheme("Light"), Is.True);
            Assert.That(ThemeManager.IsKnownTheme("High Contrast"), Is.True);
        });
    }

    [Test]
    public void UnknownTheme_ResolvesToDark()
    {
        Assert.That(ThemeManager.IsKnownTheme("Some Deleted Theme"), Is.False);
        Assert.That(ThemeManager.ResolveEffectiveThemeName("Some Deleted Theme"), Is.EqualTo("Dark"),
            "a saved theme whose file is gone must fall back to Dark, not render Dark as if it were the saved theme");
        Assert.That(ThemeManager.ResolveEffectiveThemeName(null), Is.EqualTo("Dark"));
        Assert.That(ThemeManager.ResolveEffectiveThemeName("Light"), Is.EqualTo("Light"));
    }

    // ---- Imported theme path persistence + startup reload (round-trip through a temp service) ----

    [Test]
    public void RememberImportedThemePath_AddsIdempotently()
    {
        var path = Path.Combine(_homeDir, "my-theme.json");

        ThemeManager.RememberImportedThemePath(_service, path);
        ThemeManager.RememberImportedThemePath(_service, path); // repeat must not duplicate

        var stored = _service.Get<List<string>>(ThemeManager.ImportedThemesKey, new List<string>());
        Assert.That(stored, Is.EqualTo(new[] { path }));
    }

    [Test]
    public void RememberImportedThemePath_NullServiceOrEmptyPath_NoThrow()
    {
        Assert.DoesNotThrow(() => ThemeManager.RememberImportedThemePath(null, "x"));
        Assert.DoesNotThrow(() => ThemeManager.RememberImportedThemePath(_service, ""));
        Assert.That(_service.Has(ThemeManager.ImportedThemesKey, SettingsScope.User), Is.False);
    }

    [Test]
    public async Task ReloadImportedThemes_RegistersExistingFile_AndPrunesMissing()
    {
        var themeName = $"RoundTripTheme_{Guid.NewGuid():N}";
        var existing = Path.Combine(_homeDir, "existing-theme.json");
        await File.WriteAllTextAsync(existing,
            $"{{ \"name\": \"{themeName}\", \"type\": \"dark\", \"colors\": {{}} }}");
        var missing = Path.Combine(_homeDir, "deleted-theme.json"); // never created

        _service.Set(ThemeManager.ImportedThemesKey, new List<string> { existing, missing }, SettingsScope.User);

        await ThemeManager.ReloadImportedThemesAsync(_service);

        Assert.That(ThemeManager.ExtensionThemeNames, Does.Contain(themeName),
            "an imported theme's file must be re-registered at startup so the saved theme resolves");

        var pruned = _service.Get<List<string>>(ThemeManager.ImportedThemesKey, new List<string>());
        Assert.That(pruned, Is.EqualTo(new[] { existing }),
            "a theme file that no longer exists must be pruned from workbench.importedThemes");
    }

    [Test]
    public async Task ReloadImportedThemes_NullService_NoThrow()
        => await ThemeManager.ReloadImportedThemesAsync(null); // must simply return

    // ---- JSON-editor edit of the theme reaches the store (detection input) ----

    [Test]
    public void JsonEditorSave_ChangingColorTheme_UpdatesEffectiveValue()
    {
        _service.Set("workbench.colorTheme", "Dark", SettingsScope.User);
        var vm = new SettingsViewModel(_service);

        // A raw-JSON edit that flips the theme to Light.
        vm.JsonEditorContent = "{ \"workbench.colorTheme\": \"Light\" }";
        vm.SaveJsonEditorCommand.Execute(null);

        Assert.That(_service.Get<string>("workbench.colorTheme", "Dark", SettingsScope.Effective),
            Is.EqualTo("Light"),
            "the JSON edit must reach the store — this is the value SaveJsonEditor routes through ThemeManager.Apply");
    }

    // ---- Consumer registry ----

    [Test]
    public void ThemeConsumers_AreNamed()
    {
        // Touching ThemeManager runs its static ctor, which registers both theme keys.
        _ = ThemeManager.AllThemeNames;

        Assert.That(SettingsConsumerRegistry.IsRegistered("workbench.colorTheme"), Is.True);
        Assert.That(SettingsConsumerRegistry.IsRegistered(ThemeManager.ImportedThemesKey), Is.True);
    }
}
