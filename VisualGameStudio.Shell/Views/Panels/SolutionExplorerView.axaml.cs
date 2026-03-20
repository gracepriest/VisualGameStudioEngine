using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class SolutionExplorerView : UserControl
{
    /// <summary>
    /// Accumulates typed characters for type-ahead search (jump to matching item).
    /// Resets after a brief delay.
    /// </summary>
    private string _typeAheadBuffer = "";
    private DispatcherTimer? _typeAheadResetTimer;

    public SolutionExplorerView()
    {
        InitializeComponent();

        _typeAheadResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _typeAheadResetTimer.Tick += (s, e) =>
        {
            _typeAheadBuffer = "";
            _typeAheadResetTimer.Stop();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not SolutionExplorerViewModel vm) { base.OnKeyDown(e); return; }
        var selectedNode = vm.SelectedNode;

        switch (e.Key)
        {
            case Key.Enter:
                // Open the selected file
                if (selectedNode != null)
                {
                    if (selectedNode.IsFile)
                    {
                        vm.DoubleClickCommand.Execute(null);
                    }
                    else if (selectedNode.IsFolder || selectedNode.IsProject)
                    {
                        selectedNode.IsExpanded = !selectedNode.IsExpanded;
                    }
                    e.Handled = true;
                }
                break;

            case Key.Space:
                // Toggle selection state
                if (selectedNode != null)
                {
                    selectedNode.IsSelected = !selectedNode.IsSelected;
                    e.Handled = true;
                }
                break;

            case Key.Right:
                // Expand folder, or move to first child if already expanded
                if (selectedNode != null && (selectedNode.IsFolder || selectedNode.IsProject))
                {
                    if (!selectedNode.IsExpanded)
                    {
                        selectedNode.IsExpanded = true;
                        e.Handled = true;
                    }
                    else if (selectedNode.Children.Count > 0)
                    {
                        vm.SelectedNode = selectedNode.Children[0];
                        e.Handled = true;
                    }
                }
                break;

            case Key.Left:
                // Collapse folder, or go to parent
                if (selectedNode != null)
                {
                    if ((selectedNode.IsFolder || selectedNode.IsProject) && selectedNode.IsExpanded)
                    {
                        selectedNode.IsExpanded = false;
                        e.Handled = true;
                    }
                    else
                    {
                        // Navigate to parent
                        var parent = FindParentNode(vm.Nodes, selectedNode);
                        if (parent != null)
                        {
                            vm.SelectedNode = parent;
                            e.Handled = true;
                        }
                    }
                }
                break;

            case Key.Home:
                // Jump to first item
                if (vm.Nodes.Count > 0)
                {
                    vm.SelectedNode = vm.Nodes[0];
                    e.Handled = true;
                }
                break;

            case Key.End:
                // Jump to last visible item
                var lastNode = GetLastVisibleNode(vm.Nodes);
                if (lastNode != null)
                {
                    vm.SelectedNode = lastNode;
                    e.Handled = true;
                }
                break;

            default:
                // Type-ahead: jump to matching item when letters are typed
                if (e.Key >= Key.A && e.Key <= Key.Z)
                {
                    var c = e.Key.ToString();
                    if (e.KeyModifiers == KeyModifiers.None)
                    {
                        _typeAheadBuffer += c;
                        _typeAheadResetTimer?.Stop();
                        _typeAheadResetTimer?.Start();

                        var match = FindNodeByPrefix(vm.Nodes, _typeAheadBuffer);
                        if (match != null)
                        {
                            vm.SelectedNode = match;
                            e.Handled = true;
                        }
                    }
                }
                break;
        }

        if (!e.Handled)
            base.OnKeyDown(e);
    }

    /// <summary>
    /// Finds the parent node of the given target in the tree.
    /// </summary>
    private static TreeNode? FindParentNode(System.Collections.ObjectModel.ObservableCollection<TreeNode> nodes, TreeNode target)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Contains(target))
                return node;

            var found = FindParentNode(node.Children, target);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Gets the last visible node in the tree (deepest last expanded child).
    /// </summary>
    private static TreeNode? GetLastVisibleNode(System.Collections.ObjectModel.ObservableCollection<TreeNode> nodes)
    {
        if (nodes.Count == 0) return null;
        var last = nodes[^1];
        while (last.IsExpanded && last.Children.Count > 0)
        {
            last = last.Children[^1];
        }
        return last;
    }

    /// <summary>
    /// Finds the first node whose name starts with the given prefix (case-insensitive).
    /// Searches all visible (expanded) nodes in tree order.
    /// </summary>
    private static TreeNode? FindNodeByPrefix(System.Collections.ObjectModel.ObservableCollection<TreeNode> nodes, string prefix)
    {
        foreach (var node in nodes)
        {
            if (node.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return node;

            if (node.IsExpanded && node.Children.Count > 0)
            {
                var found = FindNodeByPrefix(node.Children, prefix);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void OnTreeViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        // This handler is kept for backwards compatibility but main handling is in OnItemDoubleTapped
        if (DataContext is SolutionExplorerViewModel vm && vm.SelectedNode != null && vm.SelectedNode.IsFile)
        {
            vm.DoubleClickCommand.Execute(null);
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        // This handler is attached directly to the Grid inside TreeDataTemplate
        // So the sender's DataContext should be the TreeNode
        if (sender is Control ctrl && ctrl.DataContext is TreeNode node)
        {
            if (DataContext is SolutionExplorerViewModel vm && node.IsFile)
            {
                vm.SelectedNode = node;
                vm.DoubleClickCommand.Execute(null);
                e.Handled = true; // Prevent event from bubbling
            }
        }
    }

    private void OnOpenMenuItemClick(object? sender, RoutedEventArgs e)
    {
        // Handle context menu Open click
        if (DataContext is SolutionExplorerViewModel vm)
        {
            vm.OpenFileCommand.Execute(null);
        }
    }
}
