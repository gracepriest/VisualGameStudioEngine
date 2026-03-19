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

    private void OnReplaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FindInFilesViewModel vm)
        {
            vm.ReplaceAllCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnFileHeaderPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: SearchResultFileViewModel group })
        {
            group.IsExpanded = !group.IsExpanded;
        }
    }

    private void OnMatchPressed(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: SearchResultMatchViewModel match } &&
            DataContext is FindInFilesViewModel vm)
        {
            // Navigate to match location on click
            var result = new FindResult
            {
                FilePath = match.FilePath,
                Line = match.LineNumber,
                Column = match.Column,
                Length = match.MatchLength,
                PreviewText = match.LineText
            };
            vm.NavigateToResultCommand.Execute(result);
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
