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
}
