using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class SolutionExplorerView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private bool _potentialDrag;
    private TreeNode? _renameNode;

    public SolutionExplorerView()
    {
        InitializeComponent();

        // Wire up keyboard shortcuts
        KeyDown += OnKeyDown;

        // Wire up clipboard copy from ViewModel
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SolutionExplorerViewModel vm)
            {
                vm.ClipboardCopyRequested += OnClipboardCopyRequested;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private async void OnClipboardCopyRequested(object? sender, string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SolutionExplorerViewModel.IsRenaming))
        {
            if (sender is SolutionExplorerViewModel vm && vm.IsRenaming)
            {
                // Start inline rename: need to show TextBox overlay in the tree
                // This is handled by the RenameAdorner approach via attached behavior
                StartInlineRename(vm);
            }
        }
        else if (e.PropertyName == nameof(SolutionExplorerViewModel.IsCreatingNew))
        {
            if (sender is SolutionExplorerViewModel vm && vm.IsCreatingNew)
            {
                // Focus the new item text box
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    NewItemTextBox?.Focus();
                    NewItemTextBox?.SelectAll();
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }
        else if (e.PropertyName == nameof(SolutionExplorerViewModel.IsFilterVisible))
        {
            if (sender is SolutionExplorerViewModel vm && vm.IsFilterVisible)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    FilterTextBox?.Focus();
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }
    }

    private void StartInlineRename(SolutionExplorerViewModel vm)
    {
        if (vm.SelectedNode == null) return;

        _renameNode = vm.SelectedNode;

        // Find the TreeViewItem for the selected node and overlay a TextBox
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var treeViewItem = FindTreeViewItemForNode(SolutionTreeView, _renameNode);
            if (treeViewItem == null) return;

            // Find the name TextBlock inside the item template
            var nameBlock = FindNameTextBlock(treeViewItem);
            if (nameBlock == null) return;

            // Create inline rename TextBox
            var renameBox = new TextBox
            {
                Text = vm.RenameText,
                FontSize = 13,
                Padding = new Thickness(2, 0),
                Background = Avalonia.Media.Brushes.Transparent,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCCCCC")),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#007ACC")),
                BorderThickness = new Thickness(1),
                MinWidth = 80,
                Tag = "RenameBox"
            };

            // Replace the TextBlock with the TextBox in the parent Grid
            if (nameBlock.Parent is Grid parentGrid)
            {
                var col = Grid.GetColumn(nameBlock);
                nameBlock.IsVisible = false;

                Grid.SetColumn(renameBox, col);
                parentGrid.Children.Add(renameBox);

                renameBox.Focus();

                // Select filename without extension
                if (vm.SelectedNode.IsFile)
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(vm.RenameText);
                    renameBox.SelectionStart = 0;
                    renameBox.SelectionEnd = nameWithoutExt.Length;
                }
                else
                {
                    renameBox.SelectAll();
                }

                renameBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        vm.RenameText = renameBox.Text ?? "";
                        vm.ConfirmRenameCommand.Execute(null);
                        CleanupRenameBox(parentGrid, nameBlock, renameBox);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        vm.CancelRenameCommand.Execute(null);
                        CleanupRenameBox(parentGrid, nameBlock, renameBox);
                        e.Handled = true;
                    }
                };

                renameBox.LostFocus += (s, e) =>
                {
                    // Confirm on focus loss (like VS Code)
                    if (vm.IsRenaming)
                    {
                        vm.RenameText = renameBox.Text ?? "";
                        vm.ConfirmRenameCommand.Execute(null);
                        CleanupRenameBox(parentGrid, nameBlock, renameBox);
                    }
                };
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static void CleanupRenameBox(Grid parentGrid, TextBlock nameBlock, TextBox renameBox)
    {
        nameBlock.IsVisible = true;
        parentGrid.Children.Remove(renameBox);
    }

    private static TreeViewItem? FindTreeViewItemForNode(TreeView treeView, TreeNode targetNode)
    {
        return FindTreeViewItemRecursive(treeView, targetNode);
    }

    private static TreeViewItem? FindTreeViewItemRecursive(Control container, TreeNode targetNode)
    {
        if (container is TreeViewItem tvi && tvi.DataContext == targetNode)
        {
            return tvi;
        }

        foreach (var child in container.GetVisualDescendants())
        {
            if (child is TreeViewItem item && item.DataContext == targetNode)
            {
                return item;
            }
        }

        return null;
    }

    private static TextBlock? FindNameTextBlock(TreeViewItem treeViewItem)
    {
        // Find the TextBlock in column 2 (the name column)
        foreach (var descendant in treeViewItem.GetVisualDescendants())
        {
            if (descendant is TextBlock tb && Grid.GetColumn(tb) == 2)
            {
                return tb;
            }
        }
        return null;
    }

    // ── Keyboard shortcuts ──────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SolutionExplorerViewModel vm) return;

        // Handle inline new item
        if (vm.IsCreatingNew)
        {
            if (e.Key == Key.Enter)
            {
                vm.ConfirmNewItemCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CancelNewItemCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // Handle filter
        if (vm.IsFilterVisible && e.Key == Key.Escape && FilterTextBox?.IsFocused == true)
        {
            vm.ToggleFilterCommand.Execute(null);
            SolutionTreeView.Focus();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F2:
                vm.StartRenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete:
                vm.DeleteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                if (vm.SelectedNode?.IsFile == true)
                {
                    vm.OpenFileCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.CopyCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.X when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.CutCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.V when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.PasteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.E when e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                SolutionTreeView.Focus();
                e.Handled = true;
                break;

            case Key.F when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                vm.IsFilterVisible = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => FilterTextBox?.Focus());
                e.Handled = true;
                break;
        }
    }

    // ── Double-tap handlers ─────────────────────────────────────

    private void OnTreeViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SolutionExplorerViewModel vm && vm.SelectedNode != null && vm.SelectedNode.IsFile)
        {
            vm.DoubleClickCommand.Execute(null);
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control ctrl && ctrl.DataContext is TreeNode node)
        {
            if (DataContext is SolutionExplorerViewModel vm && node.IsFile)
            {
                vm.SelectedNode = node;
                vm.DoubleClickCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnOpenMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SolutionExplorerViewModel vm)
        {
            vm.OpenFileCommand.Execute(null);
        }
    }

    // ── Multi-select + Drag and Drop ────────────────────────────

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SolutionExplorerViewModel vm) return;
        if (sender is not Control ctrl || ctrl.DataContext is not TreeNode node) return;

        var props = e.GetCurrentPoint(ctrl).Properties;
        if (!props.IsLeftButtonPressed) return;

        // Handle multi-select
        var mods = e.KeyModifiers;
        vm.HandleNodeClick(node, mods.HasFlag(KeyModifiers.Control), mods.HasFlag(KeyModifiers.Shift));

        // Prepare for potential drag
        _dragStartPoint = e.GetPosition(this);
        _potentialDrag = true;
        _isDragging = false;
    }

    private void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not SolutionExplorerViewModel vm) return;
        if (!_potentialDrag) return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _dragStartPoint;

        // Start drag if moved enough
        if (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5)
        {
            _isDragging = true;

            if (vm.SelectedNode != null)
            {
                vm.StartDrag(vm.SelectedNode);
            }

            // Find drop target
            if (sender is Control ctrl)
            {
                var hitTest = this.InputHitTest(e.GetPosition(this));
                if (hitTest is Visual visual)
                {
                    var targetNode = FindTreeNodeFromVisual(visual);
                    if (targetNode != null && targetNode != vm.DragNode)
                    {
                        var relativeY = e.GetPosition(visual as Control ?? this).Y;
                        var height = (visual as Control)?.Bounds.Height ?? 22;

                        DropPosition pos;
                        if (targetNode.IsFolder || targetNode.IsProject)
                        {
                            pos = DropPosition.Inside;
                        }
                        else if (relativeY < height * 0.3)
                        {
                            pos = DropPosition.Before;
                        }
                        else if (relativeY > height * 0.7)
                        {
                            pos = DropPosition.After;
                        }
                        else
                        {
                            pos = DropPosition.Inside;
                        }

                        vm.UpdateDropTarget(targetNode, pos);
                    }
                }
            }
        }
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not SolutionExplorerViewModel vm) return;

        if (_isDragging && vm.DragNode != null && vm.DropTarget != null)
        {
            var isCopy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            _ = vm.CompleteDrop(isCopy);
        }
        else
        {
            vm.CancelDrag();
        }

        _potentialDrag = false;
        _isDragging = false;
    }

    private static TreeNode? FindTreeNodeFromVisual(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is Control { DataContext: TreeNode node })
            {
                return node;
            }
            visual = visual.GetVisualParent();
        }
        return null;
    }
}
