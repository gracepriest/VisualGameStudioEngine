using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 2.8 — Settings dialog chrome:
///   * the raw-JSON editor Save is honest — a parse error is surfaced in JsonValidationErrors and the
///     store is left intact, instead of being swallowed while the UI implies success;
///   * SettingsService.FlushPendingSaves forces a debounced write to disk so the JSON view (which
///     reads the file) never shows text that lags the UI;
///   * the search-results panel hides when the JSON editor is open (they shared a content panel).
///
/// ThemeManager.Apply no-ops headless (no Application.Current), so these tests assert the store and
/// the VM state, not the applied Avalonia variant.
/// </summary>
[TestFixture]
public class SettingsDialogChromeTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsChrome_{Guid.NewGuid()}");
        Directory.CreateDirectory(_homeDir);
        _service = new SettingsService(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        try { Directory.Delete(_homeDir, recursive: true); } catch { }
    }

    // ---- Honest raw-JSON save ----

    [Test]
    public async Task SaveJsonEditor_MalformedJson_SurfacesError_AndLeavesStoreIntact()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);
        _service.FlushPendingSaves();
        var vm = new SettingsViewModel(_service);

        vm.JsonEditorContent = "{ this is : not valid json ";
        await vm.SaveJsonEditorCommand.ExecuteAsync(null);

        Assert.That(vm.JsonValidationErrors, Is.Not.Empty,
            "a parse failure must be surfaced, not swallowed while implying success");
        Assert.That(_service.Get("editor.fontSize", 0, SettingsScope.User), Is.EqualTo(20),
            "malformed JSON must not clobber the existing store");
    }

    [Test]
    public async Task SaveJsonEditor_ValidJson_UpdatesStore_AndClearsErrors()
    {
        var vm = new SettingsViewModel(_service);

        vm.JsonEditorContent = "{ \"editor.fontSize\": 33 }";
        await vm.SaveJsonEditorCommand.ExecuteAsync(null);

        Assert.That(vm.JsonValidationErrors, Is.Empty, "a successful save must not leave error text behind");
        Assert.That(_service.Get("editor.fontSize", 0, SettingsScope.Effective), Is.EqualTo(33));
    }

    // ---- Flush seam ----

    [Test]
    public void FlushPendingSaves_WritesDebouncedEditToDiskImmediately()
    {
        // Set schedules a 500ms-debounced save; without a flush the file lags the in-memory value.
        _service.Set("editor.fontSize", 42, SettingsScope.User);
        _service.FlushPendingSaves();

        Assert.That(File.Exists(_service.UserSettingsPath), Is.True, "flush must materialize the file");
        var onDisk = File.ReadAllText(_service.UserSettingsPath);
        Assert.That(onDisk, Does.Contain("42"), "flush must persist the just-set value without waiting for the debounce");
    }

    [Test]
    public void OpeningJsonEditor_LoadsCurrentRawJson_ReflectingLiveEdits()
    {
        var vm = new SettingsViewModel(_service);

        // A live-applied edit that is still inside the debounce window...
        _service.Set("editor.fontSize", 51, SettingsScope.User);

        // ...must be visible the moment the JSON editor opens (LoadJsonEditorContent flushes first).
        vm.IsJsonEditorActive = true;

        Assert.That(vm.JsonEditorContent, Does.Contain("51"),
            "opening the JSON view flushes pending saves, so it shows the latest value, not stale text");
    }

    // ---- Search-results / JSON-editor overlap ----

    [Test]
    public void IsSearchResultsVisible_TrueOnlyWhenSearchingAndJsonEditorClosed()
    {
        var vm = new SettingsViewModel(_service);

        vm.SearchText = "";               // search inactive
        vm.IsJsonEditorActive = false;
        Assert.That(vm.IsSearchResultsVisible, Is.False, "no search → not visible");

        vm.SearchText = "font";           // search active
        vm.IsJsonEditorActive = false;
        Assert.That(vm.IsSearchResultsVisible, Is.True, "searching + UI view → visible");

        vm.IsJsonEditorActive = true;     // JSON editor open on top
        Assert.That(vm.IsSearchResultsVisible, Is.False, "JSON editor open must hide the search results (no overlap)");

        vm.SearchText = "";               // search cleared while JSON open
        Assert.That(vm.IsSearchResultsVisible, Is.False);
    }
}
