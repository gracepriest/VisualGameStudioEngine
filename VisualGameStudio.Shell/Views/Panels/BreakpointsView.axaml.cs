using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class BreakpointsView : UserControl
{
    public BreakpointsView()
    {
        InitializeComponent();
    }

    private void OnBreakpointDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.DataContext is BreakpointItem breakpoint)
        {
            if (DataContext is BreakpointsViewModel vm)
            {
                vm.NavigateToBreakpointCommand.Execute(breakpoint);
            }
        }
    }
}
