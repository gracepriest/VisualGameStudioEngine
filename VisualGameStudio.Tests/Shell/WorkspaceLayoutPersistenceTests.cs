using Dock.Model.Core;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Dock;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// End-to-end check of the persistence pipeline the IDE uses: capture the live dock tree,
/// round-trip it through the on-disk store, and re-apply it via the factory.
/// </summary>
[TestFixture]
public class WorkspaceLayoutPersistenceTests
{
    private string _storageRoot = null!;
    private string _projectDir = null!;
    private WorkspaceStateStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), $"WsLayoutTest_{Guid.NewGuid()}");
        _projectDir = Path.Combine(Path.GetTempPath(), $"WsLayoutProj_{Guid.NewGuid()}");
        Directory.CreateDirectory(_projectDir);
        _store = new WorkspaceStateStore(_storageRoot);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var dir in new[] { _storageRoot, _projectDir })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* ignore */ }
        }
    }

    [Test]
    public void CaptureSaveLoadApply_RestoresTweakedLayout()
    {
        var factory = new DockFactory();
        var root = factory.CreateLayout();

        // Simulate the user resizing the left dock and switching the active bottom tab.
        (FindLive(root, "LeftDock") as IDock)!.Proportion = 0.42;
        var bottomLeft = FindLive(root, "BottomLeftTools") as IDock;
        bottomLeft!.ActiveDockable = bottomLeft.VisibleDockables!.First(d => d.Id == "Terminal");

        // Capture → save to disk → load → apply, exactly as the VM does across a close/reopen.
        _store.Save(_projectDir, new WorkspaceStateModel { DockLayout = factory.SerializeCurrentLayout() });
        var loaded = _store.Load(_projectDir);
        Assert.That(loaded?.DockLayout, Is.Not.Null);

        var applied = factory.TryApplyLayout(loaded!.DockLayout!);

        Assert.That(applied, Is.Not.Null, "restore should succeed");
        Assert.That((FindLive(applied!, "LeftDock") as IDock)!.Proportion, Is.EqualTo(0.42).Within(1e-9));
        Assert.That((FindLive(applied!, "BottomLeftTools") as IDock)!.ActiveDockable?.Id, Is.EqualTo("Terminal"));
        Assert.That(FindLive(applied!, "DocumentDock"), Is.Not.Null, "document area must survive restore");
    }

    [Test]
    public void TryApplyLayout_WithoutDocumentArea_ReturnsNullSoCallerKeepsDefault()
    {
        var factory = new DockFactory();

        // A layout tree missing the document dock is unusable — restore must refuse it.
        var node = new DockNode
        {
            Kind = DockNodeKind.Root,
            Id = "Root",
            Children = { new DockNode { Kind = DockNodeKind.ToolDock, Id = "LonelyDock" } }
        };

        Assert.That(factory.TryApplyLayout(node), Is.Null);
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
