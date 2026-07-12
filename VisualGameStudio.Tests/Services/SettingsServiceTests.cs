using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Hermetic SettingsService tests over a temp directory (never touches the real ~/.vgs).
/// </summary>
[TestFixture]
public class SettingsServiceTests
{
    private string _tempDir = null!;
    private string _settingsDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "vgs-settings-tests-" + Guid.NewGuid().ToString("N"));
        _settingsDir = Path.Combine(_tempDir, ".vgs");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Test]
    public async Task Dispose_FlushesPendingDebouncedUserSave()
    {
        // Set a value and dispose immediately — well inside the 500ms debounce window.
        var service = new SettingsService(_settingsDir);
        service.Set("editor.fontSize", 42, SettingsScope.User);
        service.Dispose();

        var reloaded = new SettingsService(_settingsDir);
        try
        {
            await reloaded.LoadAsync();
            Assert.That(reloaded.Get("editor.fontSize", 0, SettingsScope.User), Is.EqualTo(42),
                "a setting changed just before Dispose must survive the pending debounced save");
        }
        finally
        {
            reloaded.Dispose();
        }
    }

    [Test]
    public async Task Dispose_FlushesPendingDebouncedWorkspaceSave()
    {
        var workspaceDir = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(workspaceDir);

        var service = new SettingsService(_settingsDir);
        service.SetWorkspacePath(workspaceDir);
        service.Set("build.defaultConfiguration", "Release", SettingsScope.Workspace);
        service.Dispose();

        var reloaded = new SettingsService(_settingsDir);
        try
        {
            reloaded.SetWorkspacePath(workspaceDir);
            await reloaded.LoadAsync();
            Assert.That(reloaded.Get("build.defaultConfiguration", "", SettingsScope.Workspace), Is.EqualTo("Release"),
                "a workspace setting changed just before Dispose must survive the pending debounced save");
        }
        finally
        {
            reloaded.Dispose();
        }
    }

    [Test]
    public void Dispose_WithNoPendingChanges_DoesNotCreateSettingsFile()
    {
        var service = new SettingsService(_settingsDir);
        service.Dispose();

        Assert.That(File.Exists(Path.Combine(_settingsDir, "settings.json")), Is.False,
            "disposing a service with no pending changes must not write a settings file");
    }
}
