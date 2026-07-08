using System.Collections.ObjectModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Models;
using CoreOrientation = VisualGameStudio.Core.Models.DockOrientation;
using DockOrientation = Dock.Model.Core.Orientation;

namespace VisualGameStudio.Shell.Dock;

/// <summary>
/// Converts between a live Dock.Avalonia layout tree (<see cref="IRootDock"/>) and the
/// framework-agnostic <see cref="DockNode"/> DTO that gets persisted per project.
///
/// Capture is a pure read of the tree. Rebuild reconstructs container docks directly and
/// looks up tool leaves by their stable <c>Id</c> in a supplied map so each panel comes
/// back as its correct concrete subclass with its view-model already wired (which is what
/// <c>ViewLocator</c> resolves views from). Unknown ids are skipped, so a layout saved by a
/// different build degrades gracefully instead of breaking.
///
/// Documents are intentionally NOT captured here — the <c>DocumentDock</c> is serialized as
/// an empty container and its open files are restored separately from
/// <see cref="WorkspaceStateModel.OpenDocuments"/>.
/// </summary>
public class DockLayoutSerializer
{
    /// <summary>Reads a live layout tree into a serializable <see cref="DockNode"/>.</summary>
    public DockNode? Capture(IRootDock? root)
    {
        return root == null ? null : CaptureNode(root);
    }

    private DockNode? CaptureNode(IDockable dockable)
    {
        var proportion = (dockable as IDock)?.Proportion ?? double.NaN;
        var node = new DockNode
        {
            Id = string.IsNullOrEmpty(dockable.Id) ? null : dockable.Id,
            Title = string.IsNullOrEmpty(dockable.Title) ? null : dockable.Title,
            Proportion = double.IsNaN(proportion) ? null : proportion
        };

        switch (dockable)
        {
            case IRootDock root:
                node.Kind = DockNodeKind.Root;
                node.ActiveDockableId = root.ActiveDockable?.Id;
                CaptureChildren(root, node);
                break;

            case IProportionalDock prop:
                node.Kind = DockNodeKind.Proportional;
                node.Orientation = prop.Orientation == DockOrientation.Horizontal
                    ? CoreOrientation.Horizontal
                    : CoreOrientation.Vertical;
                node.ActiveDockableId = prop.ActiveDockable?.Id;
                CaptureChildren(prop, node);
                break;

            case IDocumentDock:
                // Position in the tree matters; its documents do not (restored from session).
                node.Kind = DockNodeKind.DocumentDock;
                break;

            case IToolDock toolDock:
                node.Kind = DockNodeKind.ToolDock;
                node.ActiveDockableId = toolDock.ActiveDockable?.Id;
                CaptureChildren(toolDock, node);
                break;

            case IProportionalDockSplitter:
                node.Kind = DockNodeKind.Splitter;
                break;

            case ITool:
                node.Kind = DockNodeKind.Tool;
                break;

            default:
                // Documents and anything unrecognized are not persisted here.
                return null;
        }

        return node;
    }

    private void CaptureChildren(IDock dock, DockNode node)
    {
        if (dock.VisibleDockables == null) return;

        foreach (var child in dock.VisibleDockables)
        {
            var childNode = CaptureNode(child);
            if (childNode != null)
            {
                node.Children.Add(childNode);
            }
        }
    }

    /// <summary>
    /// Rebuilds a live layout tree from a captured <see cref="DockNode"/>. Tool leaves are
    /// constructed via <paramref name="toolFactory"/> (id → dockable); unknown ids are
    /// dropped. Returns null if the root can't be reconstructed (caller falls back to the
    /// default layout).
    /// </summary>
    public IRootDock? Rebuild(DockNode? node, IReadOnlyDictionary<string, Func<IDockable>> toolFactory)
    {
        if (node is not { Kind: DockNodeKind.Root }) return null;

        var built = RebuildNode(node, toolFactory);
        return built as IRootDock;
    }

    private IDockable? RebuildNode(DockNode node, IReadOnlyDictionary<string, Func<IDockable>> toolFactory)
    {
        switch (node.Kind)
        {
            case DockNodeKind.Tool:
                if (node.Id != null && toolFactory.TryGetValue(node.Id, out var make))
                {
                    return make();
                }
                return null; // unknown tool from another build — skip

            case DockNodeKind.Splitter:
                return new ProportionalDockSplitter();

            case DockNodeKind.DocumentDock:
                var documentDock = new DocumentDock
                {
                    Id = node.Id ?? "DocumentDock",
                    Title = node.Title ?? "Documents",
                    Proportion = ToProportion(node.Proportion),
                    VisibleDockables = new ObservableCollection<IDockable>(),
                    CanCreateDocument = false,
                    IsCollapsable = false
                };
                return documentDock;

            case DockNodeKind.ToolDock:
            {
                var children = RebuildChildren(node, toolFactory);
                var toolDock = new ToolDock
                {
                    Id = node.Id ?? "",
                    Title = node.Title ?? "",
                    Proportion = ToProportion(node.Proportion),
                    VisibleDockables = new ObservableCollection<IDockable>(children),
                    GripMode = GripMode.Visible
                };
                toolDock.ActiveDockable = PickActive(children, node.ActiveDockableId);
                return toolDock;
            }

            case DockNodeKind.Proportional:
            {
                var children = RebuildChildren(node, toolFactory);
                var prop = new ProportionalDock
                {
                    Id = node.Id ?? "",
                    Title = node.Title ?? "",
                    Proportion = ToProportion(node.Proportion),
                    Orientation = node.Orientation == CoreOrientation.Horizontal
                        ? DockOrientation.Horizontal
                        : DockOrientation.Vertical,
                    VisibleDockables = new ObservableCollection<IDockable>(children)
                };
                prop.ActiveDockable = PickActive(children, node.ActiveDockableId);
                return prop;
            }

            case DockNodeKind.Root:
            {
                var children = RebuildChildren(node, toolFactory);
                if (children.Count == 0) return null;

                var root = new RootDock
                {
                    Id = node.Id ?? "Root",
                    Title = node.Title ?? "Root",
                    VisibleDockables = new ObservableCollection<IDockable>(children)
                };
                var active = PickActive(children, node.ActiveDockableId) ?? children[0];
                root.ActiveDockable = active;
                root.DefaultDockable = active;
                return root;
            }

            default:
                return null;
        }
    }

    private List<IDockable> RebuildChildren(DockNode node, IReadOnlyDictionary<string, Func<IDockable>> toolFactory)
    {
        var children = new List<IDockable>();
        foreach (var childNode in node.Children)
        {
            var child = RebuildNode(childNode, toolFactory);
            if (child != null)
            {
                children.Add(child);
            }
        }
        return children;
    }

    private static IDockable? PickActive(List<IDockable> children, string? activeId)
    {
        if (children.Count == 0) return null;
        if (activeId != null)
        {
            var match = children.FirstOrDefault(c => c.Id == activeId);
            if (match != null) return match;
        }
        return children[0];
    }

    private static double ToProportion(double? value) => value ?? double.NaN;
}
