using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class SolutionExplorerView : UserControl
{
    public SolutionExplorerView()
    {
        InitializeComponent();
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
