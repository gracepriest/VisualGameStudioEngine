using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class ThreadsView : UserControl
{
    public ThreadsView()
    {
        InitializeComponent();
    }

    private void OnThreadDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ThreadsViewModel vm && vm.SelectedThread != null)
        {
            vm.SwitchThreadCommand.Execute(vm.SelectedThread);
        }
    }
}
