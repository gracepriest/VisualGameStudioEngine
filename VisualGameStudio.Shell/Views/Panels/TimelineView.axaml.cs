using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class TimelineView : UserControl
{
    public TimelineView()
    {
        InitializeComponent();
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is TimelineItemViewModel item &&
            DataContext is TimelineViewModel vm)
        {
            vm.OpenDiffCommand.Execute(item);
        }
    }
}
