using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins the Task 0.5 contract for the Settings dialog:
///
///  1. OK (Save) no longer floods the active scope. Every edit is already live-written to the
///     active scope by AutoSaveSettingToService, so OK persists nothing extra — the old
///     SaveToService() dumped all ~45 keys into the active scope (e.g. every key into a project's
///     workspace settings.json). The "flooding" regression test proves OK writes ONLY the keys
///     the user actually touched.
///
///  2. Reset All (a) requires an explicit confirmation — declined or unwired changes nothing;
///     (b) clears ONLY the currently-active scope, never both; (c) after a Reset, a Cancel still
///     restores the values the dialog opened with (the D1a snapshot revert undoes the reset —
///     "Cancel = leave everything as it was at open").
///
/// Tests drive the same live-apply pipeline the AXAML uses via the <see cref="SearchableSettingItem"/>
/// proxy, and (per Task 0.4) assert restored SERVICE values, since ThemeManager.Apply early-returns
/// when Application.Current is null.
/// </summary>
[TestFixture]
public class SettingsResetTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsResetTest_{Guid.NewGuid()}");
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

    /// <summary>Builds a VM whose Reset never touches the real %APPDATA% legacy file.</summary>
    private SettingsViewModel MakeVm()
    {
        var vm = new SettingsViewModel(_service);
        vm.LegacyResetPath = Path.Combine(_homeDir, "legacy_settings.json");
        return vm;
    }

    // ---- (d) OK writes only the touched key; it must not flood the active (workspace) scope ----

    [Test]
    public void Save_WithWorkspaceActive_WritesOnlyTouchedKey_NotEveryKey()
    {
        var workspaceDir = Path.Combine(_homeDir, "workspace");
        Directory.CreateDirectory(workspaceDir);
        _service.SetWorkspacePath(workspaceDir);

        var vm = MakeVm();
        vm.ActiveScope = SettingsScope.Workspace; // subsequent live-applies write the workspace store

        Proxy(vm, "editor.fontSize").IntValue = 30; // the ONLY edit

        vm.SaveCommand.Execute(null);

        var ws = _service.GetAll(SettingsScope.Workspace);
        Assert.That(ws.Count, Is.EqualTo(1),
            "OK must not dump untouched keys into the workspace scope (the flooding bug)");
        Assert.That(ws.ContainsKey("editor.fontSize"), Is.True,
            "only the key the user edited belongs in the workspace store");
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.Workspace), Is.EqualTo(30));
    }

    // ---- (a) Reset requires confirmation: declined -> nothing changes ----

    [Test]
    public async Task Reset_WhenConfirmationDeclined_ChangesNothing()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);

        var vm = MakeVm();
        vm.ConfirmResetInteraction = (_, _) => Task.FromResult(false); // user clicks "No"

        await vm.ResetToDefaultsCommand.ExecuteAsync(null);

        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.True,
            "a declined reset must leave every override in place");
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(20),
            "a declined reset must not change any value");
    }

    // ---- (a') Reset with no confirmation wired at all: fail safe, nothing changes ----

    [Test]
    public async Task Reset_WhenConfirmationUnwired_FailsSafe_ChangesNothing()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);

        var vm = MakeVm();
        vm.ConfirmResetInteraction = null; // never wired

        await vm.ResetToDefaultsCommand.ExecuteAsync(null);

        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.True,
            "an unwired confirm must fail safe: a reset can never fire without a confirmation");
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(20));
    }

    // ---- (b) Confirmed reset clears ONLY the active scope; the other scope is untouched ----

    [Test]
    public async Task Reset_Confirmed_ClearsOnlyActiveScope()
    {
        var workspaceDir = Path.Combine(_homeDir, "workspace");
        Directory.CreateDirectory(workspaceDir);
        _service.SetWorkspacePath(workspaceDir);

        _service.Set("editor.fontSize", 20, SettingsScope.User);
        _service.Set("editor.fontSize", 40, SettingsScope.Workspace);

        var vm = MakeVm();
        vm.ConfirmResetInteraction = (_, _) => Task.FromResult(true);
        vm.ActiveScope = SettingsScope.Workspace; // reset must hit ONLY the workspace store

        await vm.ResetToDefaultsCommand.ExecuteAsync(null);

        Assert.That(_service.Has("editor.fontSize", SettingsScope.Workspace), Is.False,
            "reset must clear the active (workspace) scope");
        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(20),
            "reset must NOT touch the inactive (user) scope");
    }

    // ---- (c) Reset -> Cancel restores the values the dialog opened with (snapshot wins) ----

    [Test]
    public async Task Reset_ThenCancel_RestoresPreResetValues()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);
        _service.Set("workbench.colorTheme", "Light", SettingsScope.User);

        var vm = MakeVm(); // snapshots (fontSize=20, theme=Light) at open
        vm.ConfirmResetInteraction = (_, _) => Task.FromResult(true);

        await vm.ResetToDefaultsCommand.ExecuteAsync(null);

        // Reset removed the active (user) overrides.
        Assert.That(_service.Has("editor.fontSize", SettingsScope.User), Is.False);
        Assert.That(_service.Has("workbench.colorTheme", SettingsScope.User), Is.False);

        // D1a: Cancel = restore the state at open, so a Cancel after Reset undoes the reset.
        vm.CancelCommand.Execute(null);

        Assert.That(_service.Get<int>("editor.fontSize", 14, SettingsScope.User), Is.EqualTo(20),
            "Cancel after Reset must restore the pre-reset font size");
        Assert.That(_service.Get<string>("workbench.colorTheme", "Dark", SettingsScope.User), Is.EqualTo("Light"),
            "Cancel after Reset must restore the pre-reset theme");
    }

    // ---- Confirmed reset broadcasts SettingsChanged so open editors re-read ----

    [Test]
    public async Task Reset_Confirmed_RaisesSettingsChanged()
    {
        _service.Set("editor.fontSize", 20, SettingsScope.User);

        var vm = MakeVm();
        vm.ConfirmResetInteraction = (_, _) => Task.FromResult(true);

        int raised = 0;
        EventHandler handler = (_, _) => raised++;
        SettingsViewModel.SettingsChanged += handler;
        try
        {
            await vm.ResetToDefaultsCommand.ExecuteAsync(null);
        }
        finally
        {
            SettingsViewModel.SettingsChanged -= handler;
        }

        Assert.That(raised, Is.GreaterThanOrEqualTo(1),
            "a confirmed reset must broadcast SettingsChanged so open editors re-read the reset values");
    }
}
