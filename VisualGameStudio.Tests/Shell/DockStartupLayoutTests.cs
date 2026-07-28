using Dock.Model.Core;
using NUnit.Framework;
using VisualGameStudio.Shell.Dock;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The fresh IDE (default layout, no restored per-project session) must open WITHOUT the debug tool
/// windows cluttering the bottom bar. The seven debug panels — Call Stack, Variables, Breakpoints,
/// Watch, Immediate, Debug Console, Threads — are hidden at startup and reached on demand from the
/// View ▸ Debug Windows menu (which routes through <see cref="DockFactory.ActivateTool"/>). The
/// general bottom tools (Output, Error List, Problems, Terminal, Find) stay put.
/// </summary>
[TestFixture]
public class DockStartupLayoutTests
{
    private static readonly string[] DebugToolIds =
    {
        "CallStack", "Variables", "Breakpoints", "Watch", "ImmediateWindow", "DebugConsole", "Threads"
    };

    [Test]
    public void DefaultLayout_HasNoDebugToolWindows()
    {
        var factory = new DockFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        foreach (var id in DebugToolIds)
        {
            Assert.That(FindLive(root, id), Is.Null,
                $"debug panel '{id}' must not be present in the fresh default layout");
        }
    }

    [Test]
    public void DefaultLayout_StillShowsTheGeneralBottomTools()
    {
        var factory = new DockFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        // The clean-up is scoped to the debug group only; the general bottom tools must remain.
        foreach (var id in new[] { "Output", "ErrorList", "Problems", "Terminal", "FindInFiles" })
        {
            Assert.That(FindLive(root, id), Is.Not.Null,
                $"general bottom tool '{id}' must remain in the default layout");
        }
    }

    [Test]
    public void HiddenDebugPanel_StillReopensIntoTheBottomRegion_OnDemand()
    {
        var factory = new DockFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        // View ▸ Debug Windows ▸ Breakpoints routes here; a hidden-at-startup panel must still open,
        // rebuilding its bottom-right debug group.
        factory.ActivateTool("Breakpoints");

        Assert.That(FindLive(root, "Breakpoints"), Is.Not.Null,
            "a hidden debug panel must reopen from the menu");
        Assert.That(IsUnder(root, "BottomDock", "Breakpoints"), Is.True,
            "the reopened debug panel lands in the bottom region, not the sidebar");
    }

    [Test]
    public void CloseDebugPanels_DismissesEveryOpenDebugWindow_OnDebugEnd()
    {
        var factory = new DockFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        // During a debug session the user opens some debug windows from View ▸ Debug Windows.
        factory.ActivateTool("Breakpoints");
        factory.ActivateTool("Variables");
        Assert.That(FindLive(root, "Breakpoints"), Is.Not.Null);
        Assert.That(FindLive(root, "Variables"), Is.Not.Null);

        // When the debug session ends they must disappear, returning to the clean startup state.
        factory.CloseDebugPanels();

        foreach (var id in DebugToolIds)
        {
            Assert.That(FindLive(root, id), Is.Null,
                $"debug panel '{id}' must be closed when the debug session ends");
        }
        // General bottom tools are untouched by the debug-end cleanup.
        Assert.That(FindLive(root, "Output"), Is.Not.Null);
    }

    [Test]
    public void CloseDebugPanels_WhenNoDebugWindowsAreOpen_IsANoOp()
    {
        var factory = new DockFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        Assert.DoesNotThrow(() => factory.CloseDebugPanels(),
            "closing debug panels when none are open must be a safe no-op");
        Assert.That(FindLive(root, "Output"), Is.Not.Null,
            "the general tools remain after a no-op debug-end cleanup");
    }

    private static bool IsUnder(IDockable root, string containerId, string toolId)
    {
        var container = FindLive(root, containerId);
        return container != null && FindLive(container, toolId) != null;
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
