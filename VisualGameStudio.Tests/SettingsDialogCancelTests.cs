using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins the Settings dialog Cancel/X contract (Task 0.4, decision D1a — live-apply + snapshot
/// revert): every change made while the dialog is open is live-applied to the settings service,
/// but Cancel (and closing via the window X / Esc) must restore the exact per-scope state the
/// dialog opened with — theme included — while OK (Save) makes the changes stick.
///
/// Changes are driven through the public <see cref="SearchableSettingItem"/> proxy the AXAML
/// binds to (its value setters route through the same auto-save path a real control edit does),
/// so these tests exercise the real live-apply pipeline, not a shortcut.
///
/// The theme's *visual* apply (<see cref="ThemeManager.Apply"/>) early-returns when
/// Application.Current is null (true here), so — per the task — these tests assert the restored
/// SERVICE value, not the applied Avalonia variant.
/// </summary>
[TestFixture]
public class SettingsDialogCancelTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsCancelTest_{Guid.NewGuid()}");
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

    /// <summary>Finds the flat proxy item the AXAML binds to for a settings key, via the search box.</summary>
    private static SearchableSettingItem Proxy(SettingsViewModel vm, string key)
    {
        vm.SearchText = key;
        var item = vm.FilteredSettings.FirstOrDefault(i => i.Key == key);
        Assert.That(item, Is.Not.Null, $"no settings proxy found for key '{key}'");
        return item!;
    }

    // ---- Cancel restores values that HAD an override at open (restore old value) ----

    [Test]
    public void Cancel_RestoresPreExistingUserValues_AndTheme()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);
        _service.Set("workbench.colorTheme", "Light", SettingsScope.User);

        var vm = new SettingsViewModel(_service); // snapshots (fontSize=20, theme=Light) at open

        Proxy(vm, "editor.fontSize").IntValue = 30;         // live-apply
        Proxy(vm, "workbench.colorTheme").StringValue = "High Contrast";

        // Sanity: the live change reached the service before Cancel.
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(30));
        Assert.That(_service.Get<string>("workbench.colorTheme", "Dark", SettingsScope.User), Is.EqualTo("High Contrast"));

        vm.CancelCommand.Execute(null);

        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(20),
            "Cancel must restore the font size the dialog opened with");
        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.True,
            "the pre-existing override must remain (restored, not removed)");
        Assert.That(_service.Get<string>("workbench.colorTheme", "Dark", SettingsScope.User), Is.EqualTo("Light"),
            "Cancel must restore the theme the dialog opened with");
    }

    // ---- Cancel removes overrides that were ABSENT at open ----

    [Test]
    public void Cancel_RemovesOverridesThatWereAbsentAtOpen()
    {
        // Nothing seeded: editor.fontSize has no User override at open (effective = schema default 14).
        var vm = new SettingsViewModel(_service);

        Proxy(vm, "editor.fontSize").IntValue = 30;
        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.True,
            "live-apply must add the override");

        vm.CancelCommand.Execute(null);

        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.False,
            "Cancel must REMOVE an override that was absent at open, not leave a stale value");
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.Effective), Is.EqualTo(14),
            "with the override gone the effective value falls back to the schema default");
    }

    // ---- OK (Save) makes changes stick ----

    [Test]
    public void Save_KeepsChanges_AndMarksDialogSoCloseDoesNotRevert()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);
        var vm = new SettingsViewModel(_service);

        Proxy(vm, "editor.fontSize").IntValue = 30;

        vm.SaveCommand.Execute(null);

        Assert.That(vm.DialogResult, Is.True, "OK must mark the dialog as accepted");
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(30),
            "OK must persist the live-applied change");

        // The window Closing handler only reverts when DialogResult != true; a defensive
        // RevertToSnapshot call after Save must be a no-op so an accepted dialog never rolls back.
        vm.RevertToSnapshot();
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(30),
            "revert must not undo an already-saved dialog");
    }

    // ---- Window X / Esc close behaves like Cancel ----

    [Test]
    public void WindowClose_WithoutSave_RevertsLikeCancel()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);
        var vm = new SettingsViewModel(_service);

        Proxy(vm, "editor.fontSize").IntValue = 30;

        // Exactly what SettingsDialog's Closing handler does when the user hits X / Esc
        // (DialogResult is still false because Save was never invoked).
        Assert.That(vm.DialogResult, Is.False);
        vm.RevertToSnapshot();

        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(20),
            "closing the window without OK must revert like Cancel");
    }

    // ---- Revert is guarded to run exactly once (Cancel path + Closing path) ----

    [Test]
    public void RevertToSnapshot_IsIdempotent_RunsExactlyOnce()
    {
        var vm = new SettingsViewModel(_service);

        Proxy(vm, "editor.fontSize").IntValue = 30;
        vm.RevertToSnapshot(); // first revert removes the absent-at-open override

        // A later, unrelated write; a second revert (e.g. the Closing handler after Cancel already
        // reverted) must NOT fire again and clobber it.
        _service.Set("editor.fontSize", 55, SettingsScope.User);
        vm.RevertToSnapshot();

        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(55),
            "the second RevertToSnapshot must be a guarded no-op");
    }

    // ---- Scope-correct revert: a Workspace-scope change is undone in Workspace, User untouched ----

    [Test]
    public void Cancel_RevertsChangeInTheScopeItWasMade()
    {
        var workspaceDir = Path.Combine(_homeDir, "workspace");
        Directory.CreateDirectory(workspaceDir);
        _service.SetWorkspacePath(workspaceDir);

        var vm = new SettingsViewModel(_service); // HasWorkspace true -> snapshots User + Workspace
        vm.ActiveScope = SettingsScope.Workspace;  // subsequent live-applies write the workspace store

        Proxy(vm, "editor.fontSize").IntValue = 40;
        Assert.That(_service.Has("editor.fontSize", SettingsScope.Workspace), Is.True,
            "the change must land in the workspace scope (the active tab)");
        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.False,
            "a workspace-scope change must not touch the user store");

        vm.CancelCommand.Execute(null);

        Assert.That(_service.Has("editor.fontSize", SettingsScope.Workspace), Is.False,
            "Cancel must remove the workspace override that was absent at open");
        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.False,
            "the user store must remain untouched");
    }
}
