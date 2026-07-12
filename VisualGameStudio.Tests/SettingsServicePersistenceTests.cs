using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests;

/// <summary>
/// Regression tests for the settings-write corruption observed during the Jul 12 audit:
/// ~/.vgs/settings.json ended up with duplicate top-level keys because concurrent
/// non-atomic <c>File.WriteAllTextAsync</c> calls (two IDE instances + watcher-triggered
/// re-saves) could interleave. Saves must land via a temp file + atomic replace/move so a
/// reader can never observe a torn file, and loading must tolerate a file that already has
/// duplicate keys (last value wins) rather than throwing or silently corrupting state.
/// </summary>
[TestFixture]
public class SettingsServicePersistenceTests
{
    private string _homeDir = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsPersistenceTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_homeDir)) Directory.Delete(_homeDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    [Test]
    public async Task SaveScopeAsync_StagesFullContentInTempFile_DestinationUntouchedUntilAtomicSwap()
    {
        // using: the service starts a FileSystemWatcher, and Set() schedules a debounced
        // background save -- both must be torn down before TearDown deletes the temp dir.
        using var service = new SettingsService(_homeDir);
        var settingsPath = service.UserSettingsPath;
        var tempPath = settingsPath + ".tmp";

        // Seed the destination with content a concurrent reader could observe.
        File.WriteAllText(settingsPath, "{\"editor.fontSize\": 1}");

        service.Set("editor.fontSize", 42);

        // Hold an exclusive lock on the destination so the final atomic replace/move step
        // cannot complete. This proves the bulk of the write (the full new JSON content)
        // lands in a *different* file first -- the destination is never opened for direct
        // writing, only swapped into place at the very end.
        using (var lockStream = new FileStream(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await service.SaveScopeAsync(SettingsScope.User);

            Assert.That(File.Exists(tempPath), Is.True,
                "expected the save to stage the full new content in a temp file before attempting the atomic swap");
            var tempContent = File.ReadAllText(tempPath);
            Assert.That(tempContent, Does.Contain("42"),
                "temp file should already contain the fully serialized new content");

            // Read through the handle we already hold (a second File.ReadAllText call would
            // itself throw, since we opened with FileShare.None) to prove the destination's
            // bytes were never touched while locked -- i.e. no direct in-place write occurred.
            lockStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(lockStream, leaveOpen: true);
            var lockedDestContent = await reader.ReadToEndAsync();
            Assert.That(lockedDestContent, Does.Contain("\"editor.fontSize\": 1"),
                "destination should still hold the original content while locked");
            Assert.That(lockedDestContent, Does.Not.Contain("42"),
                "a reader must never observe partially-written / torn content in the destination file");
        }

        // Once unlocked, a subsequent save completes the atomic swap and cleans up the temp file.
        await service.SaveScopeAsync(SettingsScope.User);
        Assert.That(File.Exists(tempPath), Is.False, "temp file should not linger after a successful save");
        var finalContent = File.ReadAllText(settingsPath);
        Assert.That(finalContent, Does.Contain("42"));
    }

    [Test]
    public async Task SetRawJsonAsync_AlsoWritesAtomically_ThroughTheSameTempFileMechanism()
    {
        // SetRawJsonAsync (used by the raw settings.json editor) used to bypass the
        // save lock and write directly with File.WriteAllTextAsync -- the most likely
        // real-world source of the interleaved-write corruption seen in the audit.
        using var service = new SettingsService(_homeDir);
        var settingsPath = service.UserSettingsPath;
        var tempPath = settingsPath + ".tmp";

        File.WriteAllText(settingsPath, "{\"editor.fontSize\": 1}");

        using (var lockStream = new FileStream(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await service.SetRawJsonAsync("{\"editor.fontSize\": 77}", SettingsScope.User);

            Assert.That(File.Exists(tempPath), Is.True,
                "SetRawJsonAsync should stage its write through the same atomic temp-file mechanism as SaveScopeAsync");

            lockStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(lockStream, leaveOpen: true);
            var lockedDestContent = await reader.ReadToEndAsync();
            Assert.That(lockedDestContent, Does.Not.Contain("77"),
                "a reader must never observe torn content from SetRawJsonAsync either");
        }
    }

    [Test]
    public async Task Dispose_FlushesPendingDebouncedSave_NoExitDataLossWindow()
    {
        // Saves are debounced by 500ms. Dispose() used to just CANCEL the pending save without
        // flushing, and app shutdown never flushed settings either -- so any edit made within
        // ~500ms of quitting was silently lost. Dispose must complete one final save when a
        // debounced save is still pending.
        var service = new SettingsService(_homeDir);
        service.Set("editor.fontSize", 61, SettingsScope.User);

        // Dispose immediately -- well inside the 500ms debounce window, so the scheduled
        // background save has not fired yet.
        service.Dispose();

        using var reloaded = new SettingsService(_homeDir);
        await reloaded.LoadAsync();
        Assert.That(reloaded.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(61),
            "a value set just before Dispose must survive: Dispose has to flush the pending " +
            "debounced save instead of cancelling it (the 500ms exit data-loss window)");
    }

    [Test]
    public async Task LoadAsync_ToleratesDuplicateKeysInUserSettingsFile_LastValueWins()
    {
        // Reproduces the exact corruption pattern observed live: workbench.colorTheme
        // appeared twice in ~/.vgs/settings.json after concurrent non-atomic writes.
        var vgsDir = Path.Combine(_homeDir, ".vgs");
        Directory.CreateDirectory(vgsDir);
        File.WriteAllText(Path.Combine(vgsDir, "settings.json"),
            "{\"workbench.colorTheme\": \"Dark\", \"editor.fontSize\": 10, \"workbench.colorTheme\": \"Light\"}");

        using var service = new SettingsService(_homeDir);
        await service.LoadAsync();

        Assert.That(service.Get<string>("workbench.colorTheme", "Dark"), Is.EqualTo("Light"),
            "load must not throw on a duplicate key and must take the last occurrence's value");
        Assert.That(service.Get<int>("editor.fontSize", 14), Is.EqualTo(10));
    }
}
