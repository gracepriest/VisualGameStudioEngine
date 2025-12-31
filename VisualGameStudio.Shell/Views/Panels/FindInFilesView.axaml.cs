using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class FindInFilesView : UserControl
{
    public FindInFilesView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Focus search box when panel is shown
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public void FocusSearchBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FindInFilesViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TreeView treeView || DataContext is not FindInFilesViewModel vm)
            return;

        // Check what was selected
        if (treeView.SelectedItem is FindResult result)
        {
            vm.NavigateToResultCommand.Execute(result);
        }
        else if (treeView.SelectedItem is FindResultGroup group && group.Results.Count > 0)
        {
            // Navigate to the first result in the group
            vm.NavigateToResultCommand.Execute(group.Results[0]);
        }
    }
}
