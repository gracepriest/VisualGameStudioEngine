using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Shell.Controls;

public partial class TabStripControl : UserControl
{
    public TabStripControl()
    {
        InitializeComponent();
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: TabItemViewModel tab } && DataContext is TabManagerViewModel vm)
        {
            vm.ActivateTab(tab);
        }
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // TODO: handle tab drag end
    }

    private void OnTabPointerMoved(object? sender, PointerEventArgs e)
    {
        // TODO: handle tab drag
    }

    private void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TabItemViewModel tab } && DataContext is TabManagerViewModel vm)
        {
            vm.CloseTabCommand.Execute(tab);
        }
    }

    private void OnOverflowClick(object? sender, RoutedEventArgs e)
    {
        // TODO: show overflow tab list
    }

    private void OnSplitEditorClick(object? sender, RoutedEventArgs e)
    {
        // TODO: split editor
    }
}
