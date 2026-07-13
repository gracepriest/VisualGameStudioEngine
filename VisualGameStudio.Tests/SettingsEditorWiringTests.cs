using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 2.1 — the editor settings group wired end-to-end through ISettingsService:
///
/// * the AutoSaveSettingToService event-ordering fix (the static <see cref="SettingsViewModel.SettingsChanged"/>
///   live-re-apply signal now fires for the two special-cased toggles — ShowLineNumbers /
///   AutoCloseBrackets — which used to `return` before it, exactly once, with no double-fire for
///   normal keys);
/// * the special-case persistence transforms are preserved (lineNumbers -> "on"/"off",
///   autoClosingBrackets -> "always"/"never");
/// * the D3 dialog-option removals (cursorBlinking removed; wordWrapColumn removed; renderWhitespace
///   boundary/selection removed) with the keys kept in the schema;
/// * every editor.* consumer names itself in the <see cref="SettingsConsumerRegistry"/>.
///
/// The editor-visual applications (IndentationSize, minimap visibility, sticky scroll, etc.) are
/// UI-bound and covered by code-trace + build + the live boot smoke, not asserted here.
/// </summary>
[TestFixture]
public class SettingsEditorWiringTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SettingsEditorWiring_{Guid.NewGuid()}");
        System.IO.Directory.CreateDirectory(_homeDir);
        _service = new SettingsService(_homeDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { _service.Dispose(); } catch { /* ignore */ }
        try { if (System.IO.Directory.Exists(_homeDir)) System.IO.Directory.Delete(_homeDir, true); }
        catch { /* ignore cleanup errors */ }
    }

    /// <summary>Finds the flat proxy item the AXAML binds to for a settings key, via the search box.</summary>
    private static SearchableSettingItem? Proxy(SettingsViewModel vm, string key)
    {
        vm.SearchText = key;
        return vm.FilteredSettings.FirstOrDefault(i => i.Key == key);
    }

    /// <summary>Counts how many times the static live-re-apply event fires during <paramref name="action"/>.</summary>
    private static int CountSettingsChangedFires(Action action)
    {
        int count = 0;
        EventHandler handler = (_, _) => count++;
        SettingsViewModel.SettingsChanged += handler;
        try { action(); }
        finally { SettingsViewModel.SettingsChanged -= handler; }
        return count;
    }

    // ---- AutoSaveSettingToService event-ordering fix ----

    [Test]
    public void EditingShowLineNumbers_FiresLiveReapplyEventOnce()
    {
        var vm = new SettingsViewModel(_service);
        var proxy = Proxy(vm, "editor.lineNumbers");
        Assert.That(proxy, Is.Not.Null);

        var fires = CountSettingsChangedFires(() => proxy!.BoolValue = !proxy.BoolValue);

        Assert.That(fires, Is.EqualTo(1),
            "ShowLineNumbers used to return before the SettingsChanged broadcast — the live re-apply must now fire exactly once");
    }

    [Test]
    public void EditingAutoCloseBrackets_FiresLiveReapplyEventOnce()
    {
        var vm = new SettingsViewModel(_service);
        var proxy = Proxy(vm, "editor.autoClosingBrackets");
        Assert.That(proxy, Is.Not.Null);

        var fires = CountSettingsChangedFires(() => proxy!.BoolValue = !proxy.BoolValue);

        Assert.That(fires, Is.EqualTo(1),
            "AutoCloseBrackets used to return before the SettingsChanged broadcast — the live re-apply must now fire exactly once");
    }

    [Test]
    public void EditingNormalKey_FiresLiveReapplyEventExactlyOnce_NoDoubleFire()
    {
        var vm = new SettingsViewModel(_service);
        var proxy = Proxy(vm, "editor.fontSize");
        Assert.That(proxy, Is.Not.Null);

        var fires = CountSettingsChangedFires(() => proxy!.IntValue = proxy.IntValue + 1);

        Assert.That(fires, Is.EqualTo(1), "a normal key must fire the live re-apply exactly once (no double-fire)");
    }

    // ---- Special-case persistence transforms must survive the fix ----

    [Test]
    public void ShowLineNumbers_StillPersistsAsOnOffString()
    {
        var vm = new SettingsViewModel(_service);
        var proxy = Proxy(vm, "editor.lineNumbers")!;

        proxy.BoolValue = false;
        Assert.That(_service.Get("editor.lineNumbers", "on", SettingsScope.User), Is.EqualTo("off"));

        proxy.BoolValue = true;
        Assert.That(_service.Get("editor.lineNumbers", "off", SettingsScope.User), Is.EqualTo("on"));
    }

    [Test]
    public void AutoCloseBrackets_StillPersistsAsAlwaysNeverString()
    {
        var vm = new SettingsViewModel(_service);
        var proxy = Proxy(vm, "editor.autoClosingBrackets")!;

        proxy.BoolValue = false;
        Assert.That(_service.Get("editor.autoClosingBrackets", "always", SettingsScope.User), Is.EqualTo("never"));

        proxy.BoolValue = true;
        Assert.That(_service.Get("editor.autoClosingBrackets", "never", SettingsScope.User), Is.EqualTo("always"));
    }

    // ---- D3 dialog-option removals (keys stay in schema) ----

    [Test]
    public void CursorBlinking_RemovedFromDialogInventory()
    {
        var vm = new SettingsViewModel(_service);
        Assert.That(Proxy(vm, "editor.cursorBlinking"), Is.Null,
            "editor.cursorBlinking must not appear in the dialog (D3) — no cursor-blink rendering surface exists");
    }

    [Test]
    public void WordWrapDialogChoices_ExcludeWordWrapColumn()
    {
        var vm = new SettingsViewModel(_service);
        var proxy = Proxy(vm, "editor.wordWrap")!;
        Assert.That(proxy.Choices, Is.Not.Null);
        Assert.That(proxy.Choices!, Is.EquivalentTo(new[] { "off", "on" }),
            "wordWrapColumn is unsupported and must be dropped from the dialog (D3)");
    }

    [Test]
    public void RenderWhitespaceDialogChoices_ExcludeBoundaryAndSelection()
    {
        var vm = new SettingsViewModel(_service);
        var proxy = Proxy(vm, "editor.renderWhitespace")!;
        Assert.That(proxy.Choices, Is.Not.Null);
        Assert.That(proxy.Choices!, Is.EquivalentTo(new[] { "none", "all" }),
            "boundary/selection are unsupported and must be dropped from the dialog (D3)");
    }

    [Test]
    public void RenderWhitespace_NonNoneValue_IsTreatedAsVisible()
    {
        // The editor maps any non-"none" value to whitespace-visible; confirm the setting stored by
        // the "all" dialog choice round-trips to the same predicate ApplyEditorSettings uses.
        _service.Set("editor.renderWhitespace", "all", SettingsScope.User);
        var visible = _service.Get("editor.renderWhitespace", "none") != "none";
        Assert.That(visible, Is.True);
    }

    // ---- Consumer registry: every wired editor.* key names its consumer ----

    [Test]
    public void EditorSettings_AllNameAConsumer_AfterConsumerTypesInitialize()
    {
        // Force the consumer type initializers to run (they only call RegisterConsumer — no Avalonia
        // app is needed). This is the same discipline the Phase 3 contract test will use.
        RuntimeHelpers.RunClassConstructor(typeof(CodeEditorDocumentView).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ThemeManager).TypeHandle);

        var editorKeys = new[]
        {
            "editor.fontFamily", "editor.fontSize", "editor.fontLigatures", "editor.lineNumbers",
            "editor.wordWrap", "editor.tabSize", "editor.insertSpaces", "editor.highlightCurrentLine",
            "editor.stickyScroll.enabled", "editor.bracketPairColorization", "editor.autoClosingBrackets",
            "editor.smoothScrolling", "editor.minimap.enabled", "editor.renderWhitespace",
        };

        foreach (var key in editorKeys)
        {
            Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True,
                $"{key} must name a consumer in SettingsConsumerRegistry (wired in ApplyEditorSettings)");
        }

        Assert.That(SettingsConsumerRegistry.IsRegistered("workbench.colorTheme"), Is.True,
            "workbench.colorTheme consumer (ThemeManager) must be registered");
    }
}
