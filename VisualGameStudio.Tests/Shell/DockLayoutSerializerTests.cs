using Dock.Model.Core;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.Dock;

namespace VisualGameStudio.Tests.Shell;

[TestFixture]
public class DockLayoutSerializerTests
{
    private DockFactory _factory = null!;
    private DockLayoutSerializer _serializer = null!;

    [SetUp]
    public void SetUp()
    {
        // View-models are left null: CreateLayout builds the tree structure without
        // dereferencing them, which is all these structural tests need.
        _factory = new DockFactory();
        _serializer = new DockLayoutSerializer();
    }

    [Test]
    public void Capture_DefaultLayout_ProducesRootWithKnownTools()
    {
        var node = _serializer.Capture(_factory.CreateLayout());

        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Kind, Is.EqualTo(DockNodeKind.Root));

        var toolIds = CollectToolIds(node);
        Assert.That(toolIds, Does.Contain("SolutionExplorer"));
        Assert.That(toolIds, Does.Contain("Output"));
        Assert.That(toolIds, Does.Contain("ErrorList"));
        Assert.That(toolIds, Does.Contain("Terminal"));
        Assert.That(toolIds, Does.Contain("Breakpoints"));
    }

    [Test]
    public void RoundTrip_PreservesToolSetAndDocumentArea()
    {
        var captured = _serializer.Capture(_factory.CreateLayout());
        var rebuilt = _serializer.Rebuild(captured, _factory.GetToolFactoryMap());
        var recaptured = _serializer.Capture(rebuilt);

        Assert.That(rebuilt, Is.Not.Null);
        Assert.That(FindLive(rebuilt!, "DocumentDock"), Is.Not.Null, "document area must survive");
        Assert.That(CollectToolIds(recaptured!), Is.EquivalentTo(CollectToolIds(captured!)));
    }

    [Test]
    public void RoundTrip_PreservesProportionAndOrientation()
    {
        var captured = _serializer.Capture(_factory.CreateLayout());

        var leftNode = FindNode(captured!, "LeftDock");
        Assert.That(leftNode, Is.Not.Null);
        Assert.That(leftNode!.Proportion, Is.EqualTo(0.2).Within(1e-9));
        Assert.That(leftNode.Orientation, Is.EqualTo(DockOrientation.Vertical));

        var rebuilt = _serializer.Rebuild(captured, _factory.GetToolFactoryMap());
        var leftLive = FindLive(rebuilt!, "LeftDock") as Dock.Model.Mvvm.Controls.ProportionalDock;
        Assert.That(leftLive, Is.Not.Null);
        Assert.That(leftLive!.Proportion, Is.EqualTo(0.2).Within(1e-9));
        Assert.That(leftLive.Orientation, Is.EqualTo(Dock.Model.Core.Orientation.Vertical));
    }

    [Test]
    public void Rebuild_AppliesTweakedProportion()
    {
        var captured = _serializer.Capture(_factory.CreateLayout());
        FindNode(captured!, "LeftDock")!.Proportion = 0.33;

        var rebuilt = _serializer.Rebuild(captured, _factory.GetToolFactoryMap());

        var leftLive = FindLive(rebuilt!, "LeftDock") as IDock;
        Assert.That(leftLive!.Proportion, Is.EqualTo(0.33).Within(1e-9));
    }

    [Test]
    public void Rebuild_AppliesTweakedActiveTab()
    {
        var captured = _serializer.Capture(_factory.CreateLayout());
        FindNode(captured!, "BottomLeftTools")!.ActiveDockableId = "Terminal";

        var rebuilt = _serializer.Rebuild(captured, _factory.GetToolFactoryMap());

        var bottomLeft = FindLive(rebuilt!, "BottomLeftTools") as IDock;
        Assert.That(bottomLeft!.ActiveDockable?.Id, Is.EqualTo("Terminal"));
    }

    [Test]
    public void Rebuild_ToolLeaf_IsCorrectConcreteSubclass()
    {
        // ViewLocator resolves a panel's view from its concrete Tool subclass, so restore
        // must reconstruct the exact type — not a bare Tool.
        var made = _factory.GetToolFactoryMap()["SolutionExplorer"]();
        Assert.That(made, Is.InstanceOf<SolutionExplorerTool>());

        var rebuilt = _serializer.Rebuild(_serializer.Capture(_factory.CreateLayout()), _factory.GetToolFactoryMap());
        Assert.That(FindLive(rebuilt!, "SolutionExplorer"), Is.InstanceOf<SolutionExplorerTool>());
    }

    [Test]
    public void Rebuild_UnknownToolId_IsSkipped()
    {
        // A layout saved by a build that had a panel this build lacks must not break restore.
        var node = new DockNode
        {
            Kind = DockNodeKind.Root,
            Id = "Root",
            Children =
            {
                new DockNode
                {
                    Kind = DockNodeKind.ToolDock,
                    Id = "BottomLeftTools",
                    Children =
                    {
                        new DockNode { Kind = DockNodeKind.Tool, Id = "PanelFromTheFuture" },
                        new DockNode { Kind = DockNodeKind.Tool, Id = "Output" }
                    }
                }
            }
        };

        var rebuilt = _serializer.Rebuild(node, _factory.GetToolFactoryMap());

        Assert.That(rebuilt, Is.Not.Null);
        Assert.That(FindLive(rebuilt!, "Output"), Is.Not.Null);
        Assert.That(FindLive(rebuilt!, "PanelFromTheFuture"), Is.Null);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static List<string> CollectToolIds(DockNode node)
    {
        var acc = new List<string>();
        void Walk(DockNode n)
        {
            if (n.Kind == DockNodeKind.Tool && n.Id != null) acc.Add(n.Id);
            foreach (var c in n.Children) Walk(c);
        }
        Walk(node);
        return acc;
    }

    private static DockNode? FindNode(DockNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var c in node.Children)
        {
            var found = FindNode(c, id);
            if (found != null) return found;
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
