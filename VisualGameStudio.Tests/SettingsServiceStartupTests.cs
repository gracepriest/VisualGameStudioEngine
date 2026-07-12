using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins the App startup contract: ~/.vgs/settings.json must actually be read before anything
/// (e.g. MainWindowViewModel) reads settings, otherwise every Get() silently returns schema
/// defaults regardless of what the user saved. Regression test for SettingsService.LoadAsync
/// having zero callers.
/// </summary>
[TestFixture]
public class SettingsServiceStartupTests
{
    private string _homeDir = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsStartupTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_homeDir)) Directory.Delete(_homeDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    [Test]
    public void LoadUserSettingsAtStartup_ReadsPersistedUserSettings()
    {
        // Seed ~/.vgs/settings.json the way a returning user's IDE would have left it.
        var vgsDir = Path.Combine(_homeDir, ".vgs");
        Directory.CreateDirectory(vgsDir);
        File.WriteAllText(Path.Combine(vgsDir, "settings.json"), "{\"editor.fontSize\": 99}");

        // Build a DI container the way App.OnFrameworkInitializationCompleted does, except the
        // settings service is pointed at a temp "home" dir so this test never touches the real
        // user profile's settings.json.
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService>(new SettingsService(_homeDir));
        var provider = services.BuildServiceProvider();

        // The exact call App makes right after the DI container is built.
        App.LoadUserSettingsAtStartup(provider);

        var settingsService = provider.GetRequiredService<ISettingsService>();
        Assert.That(settingsService.Get<int>("editor.fontSize", 14), Is.EqualTo(99));
    }

    // A legacy path that is guaranteed not to exist, so the startup theme resolver proves it
    // returns the right answer WITHOUT consulting any %APPDATA% legacy settings.json.
    private string NoLegacyFile => Path.Combine(_homeDir, "no-such-legacy", "settings.json");

    [Test]
    public async Task ResolveStartupThemeName_RoundTripsThroughSingleStore_WithoutLegacyAppDataFile()
    {
        // First run: the user picks Light; it is persisted to ~/.vgs via the service (the single
        // store). No legacy %APPDATA% file is ever written.
        using (var writer = new SettingsService(_homeDir))
        {
            writer.Set("workbench.colorTheme", "Light", SettingsScope.User);
            await writer.SaveScopeAsync(SettingsScope.User);
        }

        // Simulate a restart: a brand-new service reads the same store back from disk.
        using var service = new SettingsService(_homeDir);
        await service.LoadAsync();

        // The startup theme resolver must return Light, reading ONLY the single store.
        var theme = ThemeManager.ResolveStartupThemeName(service, NoLegacyFile);

        Assert.That(theme, Is.EqualTo("Light"),
            "theme saved through ISettingsService must survive a restart via the single store");
    }

    [Test]
    public void ResolveStartupThemeName_EmptyStore_ReturnsSchemaDefault()
    {
        using var service = new SettingsService(_homeDir);

        // No theme persisted, no legacy file -> the schema default ("Dark") must be returned.
        Assert.That(ThemeManager.ResolveStartupThemeName(service, NoLegacyFile), Is.EqualTo("Dark"));
    }

    [Test]
    public void ResolveStartupThemeName_MigratesLegacyThemeIntoSingleStore_WhenNewStoreHasNone()
    {
        // A returning user whose theme only ever lived in the retired legacy %APPDATA% store.
        var legacyPath = Path.Combine(_homeDir, "legacy", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "{\"SelectedTheme\": \"Light\"}");

        using var service = new SettingsService(_homeDir);

        var theme = ThemeManager.ResolveStartupThemeName(service, legacyPath);

        Assert.That(theme, Is.EqualTo("Light"),
            "legacy theme must be migrated into the single store and resolved on startup");
        Assert.That(service.Has("workbench.colorTheme", SettingsScope.User), Is.True,
            "migration must persist the theme into the new store's User scope");
    }

    [Test]
    public async Task ResolveStartupThemeName_LegacyMigrationDoesNotOverrideExistingTheme()
    {
        // Both stores diverge (the exact split-brain bug: legacy=Light, ~/.vgs=Dark). The single
        // store must win -- the legacy value must NOT clobber a theme the new store already holds.
        var legacyPath = Path.Combine(_homeDir, "legacy", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "{\"SelectedTheme\": \"Light\"}");

        using (var writer = new SettingsService(_homeDir))
        {
            writer.Set("workbench.colorTheme", "Dark", SettingsScope.User);
            await writer.SaveScopeAsync(SettingsScope.User);
        }

        using var service = new SettingsService(_homeDir);
        await service.LoadAsync();

        Assert.That(ThemeManager.ResolveStartupThemeName(service, legacyPath), Is.EqualTo("Dark"));
    }
}
