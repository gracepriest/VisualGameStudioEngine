using Dock.Model.Core;
using NUnit.Framework;
using VisualGameStudio.Shell.Dock;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Regression coverage for "panels disappear and won't reopen after several close/open cycles".
/// Closing a tool empties and removes its ToolDock; closing enough panels also removes the parent
/// region (LeftDock / BottomDock), leaving nothing for a reopen to attach to. ActivateTool must
/// rebuild the missing region so a tool always comes back — in its proper area.
/// </summary>
[TestFixture]
public class DockReopenResilienceTests
{
    [Test]
    public void RepeatedCloseReopen_SameTool_AlwaysComesBack()
    {
        var (factory, root) = NewLayout();

        for (int i = 0; i < 8; i++)
        {
            var tool = FindLive(root, "Output");
            Assert.That(tool, Is.Not.Null, $"iter {i}: present before close");
            factory.CloseDockable(tool!);
            Assert.That(FindLive(root, "Output"), Is.Null, $"iter {i}: gone after close");
            factory.ActivateTool("Output");
            Assert.That(FindLive(root, "Output"), Is.Not.Null, $"iter {i}: reopened");
        }
    }

    [Test]
    public void ReopenAfterHomeDockEmptied_ReturnsToBottomRegion()
    {
        var (factory, root) = NewLayout();

        // Close every tool in the bottom-left group so the ToolDock (and maybe BottomDock) is removed.
        foreach (var id in new[] { "Output", "ErrorList", "Problems", "Terminal", "FindInFiles", "CallHierarchy" })
        {
            var t = FindLive(root, id);
            if (t != null) factory.CloseDockable(t);
        }

        factory.ActivateTool("Output");

        Assert.That(FindLive(root, "Output"), Is.Not.Null, "Output should reopen after its home dock emptied");
        Assert.That(IsUnder(root, "BottomDock", "Output"), Is.True,
            "a bottom tool should reopen back in the bottom region, not the left sidebar");
    }

    [Test]
    public void CloseEveryTool_ThenReopen_RebuildsRegionAndComesBack()
    {
        var (factory, root) = NewLayout();

        foreach (var id in AllToolIds)
        {
            var t = FindLive(root, id);
            if (t != null) factory.CloseDockable(t);
        }
        Assert.That(FindFirstToolDock(root), Is.Null, "sanity: every tool dock was removed");

        // Reopening a bottom tool must rebuild the bottom region and a left tool the left region.
        factory.ActivateTool("Output");
        factory.ActivateTool("SolutionExplorer");

        Assert.That(FindLive(root, "Output"), Is.Not.Null, "Output must reopen even after every panel was closed");
        Assert.That(IsUnder(root, "BottomDock", "Output"), Is.True, "Output rebuilt into the bottom region");
        Assert.That(IsUnder(root, "LeftDock", "SolutionExplorer"), Is.True, "Solution Explorer rebuilt into the left region");
    }

    [Test]
    public void ManyToolsManyCycles_AllComeBackEveryTime()
    {
        var (factory, root) = NewLayout();
        var ids = new[] { "Output", "ErrorList", "Terminal", "Variables", "Watch", "Breakpoints", "SolutionExplorer", "Bookmarks" };

        for (int cycle = 0; cycle < 6; cycle++)
        {
            foreach (var id in ids)
            {
                var t = FindLive(root, id);
                if (t != null) factory.CloseDockable(t);
            }
            foreach (var id in ids)
            {
                factory.ActivateTool(id);
                Assert.That(FindLive(root, id), Is.Not.Null, $"cycle {cycle}: {id} should reopen");
            }
        }
    }

    private static readonly string[] AllToolIds =
    {
        "SolutionExplorer","GitChanges","GitBranches","GitStash","GitBlame","Timeline","DocumentOutline","Bookmarks","Extensions",
        "Output","ErrorList","Problems","Terminal","FindInFiles","CallHierarchy",
        "CallStack","Variables","Breakpoints","Watch","ImmediateWindow","DebugConsole","Threads"
    };

    private static (DockFactory, IDockable) NewLayout()
    {
        var factory = new DockFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        return (factory, root);
    }

    /// <summary>True if the dockable with <paramref name="toolId"/> lives somewhere under the container with <paramref name="containerId"/>.</summary>
    private static bool IsUnder(IDockable root, string containerId, string toolId)
    {
        var container = FindLive(root, containerId);
        return container != null && FindLive(container, toolId) != null;
    }

    private static IDockable? FindFirstToolDock(IDockable d)
    {
        if (d is Dock.Model.Controls.IToolDock) return d;
        if (d is IDock dock && dock.VisibleDockables != null)
            foreach (var c in dock.VisibleDockables)
            {
                var r = FindFirstToolDock(c);
                if (r != null) return r;
            }
        return null;
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
