using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Dock;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 2.6 — the Workbench settings group wired through <see cref="ISettingsService"/>:
///
/// * <c>workbench.startupEditor</c> resolves (welcomePage / none / newUntitledFile) and governs the
///   initial document area built by <see cref="DockFactory.CreateLayout"/> (welcomePage seeds the
///   Welcome document; none / newUntitledFile start empty) — while per-project layout restore still
///   takes precedence (that path replaces the whole layout, so it isn't re-asserted here);
/// * the Welcome page's "Show welcome page on startup" checkbox round-trips through the same key
///   (previously dead — nothing read or persisted it);
/// * <c>workbench.sideBar.location</c> flips the root child order (left / right) at CreateLayout time
///   while keeping the LeftDock / LeftTools ids intact so the reopen-resilience helpers still work;
/// * <c>workbench.iconTheme</c> is removed from the dialog (D3);
/// * every wired key names a consumer in <see cref="SettingsConsumerRegistry"/>.
/// </summary>
[TestFixture]
public class SettingsWorkbenchWiringTests
{
    private string _homeDir = null!;
    private SettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), $"SettingsWorkbenchWiring_{Guid.NewGuid()}");
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

    // ---- workbench.startupEditor resolution seam ----

    [Test]
    public void StartupEditor_DefaultResolvesToWelcomePage()
        => Assert.That(DockFactory.ResolveStartupEditorMode(_service), Is.EqualTo(StartupEditorMode.WelcomePage));

    [Test]
    public void StartupEditor_None_Resolves()
    {
        _service.Set("workbench.startupEditor", "none");
        Assert.That(DockFactory.ResolveStartupEditorMode(_service), Is.EqualTo(StartupEditorMode.None));
    }

    [Test]
    public void StartupEditor_NewUntitledFile_Resolves()
    {
        _service.Set("workbench.startupEditor", "newUntitledFile");
        Assert.That(DockFactory.ResolveStartupEditorMode(_service), Is.EqualTo(StartupEditorMode.NewUntitledFile));
    }

    [Test]
    public void StartupEditor_UnknownOrNull_FallsBackToWelcomePage()
    {
        _service.Set("workbench.startupEditor", "somethingWeird");
        Assert.That(DockFactory.ResolveStartupEditorMode(_service), Is.EqualTo(StartupEditorMode.WelcomePage));
        Assert.That(DockFactory.ResolveStartupEditorMode(null), Is.EqualTo(StartupEditorMode.WelcomePage));
    }

    // ---- workbench.startupEditor governs CreateLayout's initial document (headless) ----

    [Test]
    public void CreateLayout_WelcomePage_SeedsWelcomeDocument()
    {
        // schema default is welcomePage
        var root = new DockFactory(_service).CreateLayout();
        var docDock = FindLive(root, "DocumentDock") as IDock;
        Assert.That(docDock, Is.Not.Null);
        Assert.That(docDock!.VisibleDockables!.Any(d => d.Id == "Welcome"), Is.True,
            "welcomePage must seed the Start Page document");
    }

    [Test]
    public void CreateLayout_None_LeavesDocumentAreaEmpty()
    {
        _service.Set("workbench.startupEditor", "none");
        var root = new DockFactory(_service).CreateLayout();
        var docDock = FindLive(root, "DocumentDock") as IDock;
        Assert.That(docDock, Is.Not.Null);
        Assert.That(docDock!.VisibleDockables!.Count, Is.EqualTo(0),
            "none must start with an empty document dock (no Welcome page)");
    }

    [Test]
    public void CreateLayout_NewUntitledFile_LeavesDocumentAreaEmpty_ForMwvmToFill()
    {
        // CreateLayout cannot build an editor document itself; MainWindowViewModel materializes the
        // untitled file after InitLayout. So the document dock is empty at CreateLayout time.
        _service.Set("workbench.startupEditor", "newUntitledFile");
        var root = new DockFactory(_service).CreateLayout();
        var docDock = FindLive(root, "DocumentDock") as IDock;
        Assert.That(docDock!.VisibleDockables!.Any(d => d.Id == "Welcome"), Is.False,
            "newUntitledFile must not seed the Welcome page");
    }

    // ---- workbench.sideBar.location flips root child order (headless CreateLayout assertion) ----

    [Test]
    public void SideBarLocation_Left_KeepsSidebarBeforeMainArea()
    {
        // schema default is left
        var root = new DockFactory(_service).CreateLayout();
        var (leftIdx, mainIdx) = RootChildIndices(root);
        Assert.That(leftIdx, Is.GreaterThanOrEqualTo(0));
        Assert.That(mainIdx, Is.GreaterThanOrEqualTo(0));
        Assert.That(leftIdx, Is.LessThan(mainIdx), "left: LeftDock comes before MainArea");
    }

    [Test]
    public void SideBarLocation_Right_PutsMainAreaBeforeSidebar_KeepingIds()
    {
        _service.Set("workbench.sideBar.location", "right");
        var root = new DockFactory(_service).CreateLayout();
        var (leftIdx, mainIdx) = RootChildIndices(root);
        Assert.That(mainIdx, Is.LessThan(leftIdx), "right: MainArea comes before LeftDock");

        // Ids must stay stable so FindHomeToolDock / EnsureLeftRegion and layout restore still work.
        Assert.That(FindLive(root, "LeftDock"), Is.Not.Null, "sidebar keeps its LeftDock id when on the right");
        Assert.That(FindLive(root, "LeftTools"), Is.Not.Null, "sidebar keeps its LeftTools id when on the right");
    }

    [Test]
    public void IsSideBarOnRight_Seam()
    {
        Assert.That(DockFactory.IsSideBarOnRight(null), Is.False);
        Assert.That(DockFactory.IsSideBarOnRight(_service), Is.False, "default is left");
        _service.Set("workbench.sideBar.location", "right");
        Assert.That(DockFactory.IsSideBarOnRight(_service), Is.True);
    }

    // ---- Welcome page checkbox round-trips through workbench.startupEditor ----

    [Test]
    public void WelcomeCheckbox_SeedsFromSetting()
    {
        _service.Set("workbench.startupEditor", "none");
        var vm = NewWelcomeVm();
        Assert.That(vm.ShowWelcomeOnStartup, Is.False, "unchecked when startupEditor != welcomePage");

        _service.Set("workbench.startupEditor", "welcomePage");
        var vm2 = NewWelcomeVm();
        Assert.That(vm2.ShowWelcomeOnStartup, Is.True, "checked when startupEditor == welcomePage");
    }

    [Test]
    public void WelcomeCheckbox_TogglePersistsStartupEditor()
    {
        var vm = NewWelcomeVm(); // default welcomePage -> checked

        vm.ShowWelcomeOnStartup = false;
        Assert.That(_service.Get<string>("workbench.startupEditor", "welcomePage", SettingsScope.User),
            Is.EqualTo("none"), "unchecking persists none");

        vm.ShowWelcomeOnStartup = true;
        Assert.That(_service.Get<string>("workbench.startupEditor", "welcomePage", SettingsScope.User),
            Is.EqualTo("welcomePage"), "re-checking persists welcomePage");
    }

    // ---- workbench.iconTheme removed from the dialog (D3) ----

    [Test]
    public void IconTheme_NotInDialogInventory()
    {
        var vm = new SettingsViewModel(_service);
        vm.SearchText = "workbench.iconTheme";
        Assert.That(vm.FilteredSettings.Any(i => i.Key == "workbench.iconTheme"), Is.False,
            "workbench.iconTheme must be removed from the dialog (D3)");

        // Positive control: the sibling workbench keys are still present.
        vm.SearchText = "workbench.startupEditor";
        Assert.That(vm.FilteredSettings.Any(i => i.Key == "workbench.startupEditor"), Is.True);
    }

    // ---- Consumer registry ----

    [Test]
    public void WorkbenchConsumers_AreNamed_AfterConstruction()
    {
        _ = new DockFactory(_service);   // registers workbench.startupEditor + workbench.sideBar.location
        _ = NewWelcomeVm();              // (co-)registers workbench.startupEditor

        foreach (var key in new[] { "workbench.startupEditor", "workbench.sideBar.location" })
        {
            Assert.That(SettingsConsumerRegistry.IsRegistered(key), Is.True,
                $"{key} must name a consumer in SettingsConsumerRegistry (wired in Task 2.6)");
        }
    }

    // ---- helpers ----

    private WelcomeDocumentViewModel NewWelcomeVm()
    {
        var recent = new Mock<IRecentProjectsService>();
        recent.Setup(r => r.GetRecentProjects()).Returns(new List<RecentProjectInfo>());
        return new WelcomeDocumentViewModel(recent.Object, _service);
    }

    private static (int leftIdx, int mainIdx) RootChildIndices(IDockable root)
    {
        // The single ProportionalDock directly under the root holds the horizontal Left|Main split.
        var rootLayout = (IDock)((IDock)root).VisibleDockables!.First();
        var ids = rootLayout.VisibleDockables!.Select(d => d.Id).ToList();
        return (ids.IndexOf("LeftDock"), ids.IndexOf("MainArea"));
    }

    private static IDockable? FindLive(IDockable dockable, string id)
    {
        if (dockable.Id == id) return dockable;
        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var found = FindLive(child, id);
                if (found != null) return found;
            }
        }
        return null;
    }
}
