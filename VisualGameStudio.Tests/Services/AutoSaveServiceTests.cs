using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class AutoSaveServiceTests
{
    private const string TestPath = @"C:\test\file.bas";

    private Mock<ISettingsService> _settings = null!;
    private AutoSaveService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = CreatePassthroughSettings();
        _service = new AutoSaveService(_settings.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    /// <summary>
    /// Creates an ISettingsService mock whose Get() returns the supplied default value,
    /// mirroring a settings store with nothing configured.
    /// </summary>
    private static Mock<ISettingsService> CreatePassthroughSettings()
    {
        var mock = new Mock<ISettingsService>();
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SettingsScope>()))
            .Returns((string _, string d, SettingsScope _) => d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SettingsScope>()))
            .Returns((string _, int d, SettingsScope _) => d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<SettingsScope>()))
            .Returns((string _, bool d, SettingsScope _) => d);
        return mock;
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    // ── Settings ───────────────────────────────────────────────────────

    [Test]
    public void Defaults_AutoSaveIsOff()
    {
        Assert.That(_service.Mode, Is.EqualTo(AutoSaveMode.Off));
        Assert.That(_service.DelayMilliseconds, Is.EqualTo(1000));
    }

    [Test]
    public void LoadSettings_ReadsFilesAutoSaveKeys()
    {
        var settings = CreatePassthroughSettings();
        settings.Setup(s => s.Get("files.autoSave", It.IsAny<string>(), It.IsAny<SettingsScope>()))
                .Returns("afterDelay");
        settings.Setup(s => s.Get("files.autoSaveDelay", It.IsAny<int>(), It.IsAny<SettingsScope>()))
                .Returns(2500);

        using var service = new AutoSaveService(settings.Object);

        Assert.That(service.Mode, Is.EqualTo(AutoSaveMode.AfterDelay));
        Assert.That(service.DelayMilliseconds, Is.EqualTo(2500));
    }

    [Test]
    public void LoadSettings_FallsBackToLegacyEditorKeys()
    {
        var settings = CreatePassthroughSettings();
        settings.Setup(s => s.Get(SettingsKeys.AutoSave, It.IsAny<string>(), It.IsAny<SettingsScope>()))
                .Returns("afterDelay");
        settings.Setup(s => s.Get(SettingsKeys.AutoSaveDelay, It.IsAny<int>(), It.IsAny<SettingsScope>()))
                .Returns(750);

        using var service = new AutoSaveService(settings.Object);

        Assert.That(service.Mode, Is.EqualTo(AutoSaveMode.AfterDelay));
        Assert.That(service.DelayMilliseconds, Is.EqualTo(750));
    }

    [Test]
    public async Task SettingChangedToOff_CancelsPendingTimer()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 200;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        // User turns auto-save off while the debounce timer is pending
        _settings.Setup(s => s.Get("files.autoSave", It.IsAny<string>(), It.IsAny<SettingsScope>()))
                 .Returns("off");
        _settings.Raise(s => s.SettingChanged += null,
            new SettingChangedEventArgs("files.autoSave", "afterDelay", "off"));

        await Task.Delay(600);
        Assert.That(saveCount, Is.Zero);
    }

    // ── AfterDelay mode ────────────────────────────────────────────────

    [Test]
    public async Task AfterDelay_DirtyDocument_SaveCallbackInvoked()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        Assert.That(await WaitForAsync(() => saveCount == 1), Is.True,
            "Save callback should be invoked after the delay elapses");
    }

    [Test]
    public async Task AfterDelay_RaisesDocumentAutoSavedEvent()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;

        var tcs = new TaskCompletionSource<AutoSaveEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DocumentAutoSaved += (s, e) => tcs.TrySetResult(e);

        _service.RegisterDocument(TestPath,
            () => Task.FromResult(true),
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.That(completed, Is.SameAs(tcs.Task), "DocumentAutoSaved should be raised");
        Assert.That(tcs.Task.Result.FilePath, Is.EqualTo(TestPath));
        Assert.That(tcs.Task.Result.Success, Is.True);
    }

    [Test]
    public async Task AfterDelay_CleanDocument_NotSaved()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => false, // not dirty
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        await Task.Delay(400);
        Assert.That(saveCount, Is.Zero);
    }

    [Test]
    public async Task AfterDelay_ReadOnlyDocument_NotSaved()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => true); // readonly

        _service.NotifyDocumentChanged(TestPath);

        await Task.Delay(400);
        Assert.That(saveCount, Is.Zero);
    }

    [Test]
    public async Task AfterDelay_MultipleRapidChanges_SavesOnce()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 100;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        // Rapid edits reset the debounce timer; only one save should result
        _service.NotifyDocumentChanged(TestPath);
        _service.NotifyDocumentChanged(TestPath);
        _service.NotifyDocumentChanged(TestPath);

        Assert.That(await WaitForAsync(() => saveCount >= 1), Is.True);
        await Task.Delay(300);
        Assert.That(saveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ModeOff_ChangeNotification_DoesNothing()
    {
        _service.Mode = AutoSaveMode.Off;
        _service.DelayMilliseconds = 50;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        await Task.Delay(400);
        Assert.That(saveCount, Is.Zero);
    }

    // ── Unregister / edge cases ────────────────────────────────────────

    [Test]
    public async Task Unregister_BeforeTimerFires_NoSave()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 100;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);
        _service.UnregisterDocument(TestPath); // simulates document close

        await Task.Delay(500);
        Assert.That(saveCount, Is.Zero);
    }

    [Test]
    public void NotifyDocumentChanged_UnregisteredPath_DoesNotThrow()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        Assert.DoesNotThrow(() => _service.NotifyDocumentChanged(@"C:\not\registered.bas"));
    }

    [Test]
    public void RegisterDocument_EmptyPath_Ignored()
    {
        // Untitled documents (no path) must never be registered/auto-saved
        Assert.DoesNotThrow(() => _service.RegisterDocument("",
            () => Task.FromResult(true), () => true, () => false));
        Assert.DoesNotThrow(() => _service.NotifyDocumentChanged(""));
    }

    [Test]
    public async Task Dispose_CancelsPendingTimers()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 100;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);
        _service.Dispose();

        await Task.Delay(500);
        Assert.That(saveCount, Is.Zero);
    }

    [Test]
    public async Task SaveCallbackThrows_RaisesEventWithFailure()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;

        var tcs = new TaskCompletionSource<AutoSaveEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DocumentAutoSaved += (s, e) => tcs.TrySetResult(e);

        _service.RegisterDocument(TestPath,
            () => throw new IOException("disk full"),
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.That(completed, Is.SameAs(tcs.Task));
        Assert.That(tcs.Task.Result.Success, Is.False);
    }

    // ── Focus-change modes ─────────────────────────────────────────────

    [Test]
    public async Task OnFocusChange_EditorLostFocus_SavesDirtyDocument()
    {
        _service.Mode = AutoSaveMode.OnFocusChange;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyEditorLostFocus(TestPath);

        Assert.That(await WaitForAsync(() => saveCount == 1), Is.True);
    }

    [Test]
    public async Task OnWindowChange_WindowLostFocus_SavesAllDirtyDocuments()
    {
        _service.Mode = AutoSaveMode.OnWindowChange;

        var saved = new List<string>();
        void Register(string path) => _service.RegisterDocument(path,
            () => { lock (saved) saved.Add(path); return Task.FromResult(true); },
            () => true,
            () => false);

        Register(@"C:\test\a.bas");
        Register(@"C:\test\b.bas");

        _service.NotifyWindowLostFocus();

        Assert.That(await WaitForAsync(() => { lock (saved) return saved.Count == 2; }), Is.True);
    }

    // ── Notify routing (mode gating) ───────────────────────────────────

    [Test]
    public async Task NotifyEditorLostFocus_InAfterDelayMode_DoesNotSave()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 60000; // debounce far in the future

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyEditorLostFocus(TestPath);

        await Task.Delay(300);
        Assert.That(saveCount, Is.Zero,
            "Editor focus loss must not trigger a save unless the mode is OnFocusChange");
    }

    [Test]
    public async Task NotifyWindowLostFocus_InOnFocusChangeMode_DoesNotSave()
    {
        _service.Mode = AutoSaveMode.OnFocusChange;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyWindowLostFocus();

        await Task.Delay(300);
        Assert.That(saveCount, Is.Zero,
            "Window focus loss must not trigger a save unless the mode is OnWindowChange");
    }

    [Test]
    public void NotifyEditorLostFocus_UnregisteredPath_DoesNotThrow()
    {
        _service.Mode = AutoSaveMode.OnFocusChange;
        Assert.DoesNotThrow(() => _service.NotifyEditorLostFocus(@"C:\not\registered.bas"));
        Assert.DoesNotThrow(() => _service.NotifyEditorLostFocus(""));
    }

    [Test]
    public async Task OnFocusChange_CleanDocument_NotSaved()
    {
        _service.Mode = AutoSaveMode.OnFocusChange;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => false, // not dirty
            () => false);

        _service.NotifyEditorLostFocus(TestPath);

        await Task.Delay(300);
        Assert.That(saveCount, Is.Zero);
    }

    // ── Skip on errors (files.autoSaveSkipOnErrors) ────────────────────

    [Test]
    public void LoadSettings_ReadsSkipOnErrorsKey()
    {
        var settings = CreatePassthroughSettings();
        settings.Setup(s => s.Get("files.autoSaveSkipOnErrors", It.IsAny<bool>(), It.IsAny<SettingsScope>()))
                .Returns(true);

        using var service = new AutoSaveService(settings.Object);

        Assert.That(service.SkipOnErrors, Is.True);
    }

    [Test]
    public async Task SkipOnErrors_DocumentWithErrors_NotAutoSaved()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;
        _service.SkipOnErrors = true;
        _service.HasErrorsProvider = _ => true;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        await Task.Delay(400);
        Assert.That(saveCount, Is.Zero,
            "Documents with error diagnostics must not be auto-saved when SkipOnErrors is on");
    }

    [Test]
    public async Task SkipOnErrors_DocumentWithoutErrors_Saved()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;
        _service.SkipOnErrors = true;
        _service.HasErrorsProvider = _ => false;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        Assert.That(await WaitForAsync(() => saveCount == 1), Is.True);
    }

    [Test]
    public async Task SkipOnErrorsDisabled_DocumentWithErrors_StillSaved()
    {
        _service.Mode = AutoSaveMode.AfterDelay;
        _service.DelayMilliseconds = 50;
        _service.SkipOnErrors = false;
        _service.HasErrorsProvider = _ => true;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyDocumentChanged(TestPath);

        Assert.That(await WaitForAsync(() => saveCount == 1), Is.True,
            "SkipOnErrors=false must not filter documents with errors");
    }

    [Test]
    public async Task SkipOnErrors_OnFocusChange_DocumentWithErrors_NotSaved()
    {
        _service.Mode = AutoSaveMode.OnFocusChange;
        _service.SkipOnErrors = true;
        _service.HasErrorsProvider = _ => true;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyEditorLostFocus(TestPath);

        await Task.Delay(300);
        Assert.That(saveCount, Is.Zero);
    }

    [Test]
    public async Task SkipOnErrors_OnWindowChange_SavesOnlyDocumentsWithoutErrors()
    {
        const string CleanPath = @"C:\test\clean.bas";
        const string BrokenPath = @"C:\test\broken.bas";

        _service.Mode = AutoSaveMode.OnWindowChange;
        _service.SkipOnErrors = true;
        _service.HasErrorsProvider = path =>
            string.Equals(path, BrokenPath, StringComparison.OrdinalIgnoreCase);

        var saved = new List<string>();
        void Register(string path) => _service.RegisterDocument(path,
            () => { lock (saved) saved.Add(path); return Task.FromResult(true); },
            () => true,
            () => false);

        Register(CleanPath);
        Register(BrokenPath);

        _service.NotifyWindowLostFocus();

        Assert.That(await WaitForAsync(() => { lock (saved) return saved.Count >= 1; }), Is.True);
        await Task.Delay(300);
        lock (saved)
        {
            Assert.That(saved, Is.EquivalentTo(new[] { CleanPath }),
                "Only the document without error diagnostics should be auto-saved");
        }
    }

    [Test]
    public async Task SkipOnErrors_ProviderThrows_SaveStillProceeds()
    {
        _service.Mode = AutoSaveMode.OnFocusChange;
        _service.SkipOnErrors = true;
        _service.HasErrorsProvider = _ => throw new InvalidOperationException("diagnostics unavailable");

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyEditorLostFocus(TestPath);

        Assert.That(await WaitForAsync(() => saveCount == 1), Is.True,
            "A failing error-state provider must not block auto-save");
    }

    [Test]
    public async Task SkipOnErrors_NoProviderInstalled_SavesNormally()
    {
        _service.Mode = AutoSaveMode.OnFocusChange;
        _service.SkipOnErrors = true;
        _service.HasErrorsProvider = null;

        var saveCount = 0;
        _service.RegisterDocument(TestPath,
            () => { Interlocked.Increment(ref saveCount); return Task.FromResult(true); },
            () => true,
            () => false);

        _service.NotifyEditorLostFocus(TestPath);

        Assert.That(await WaitForAsync(() => saveCount == 1), Is.True);
    }
}
