using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class ErrorListView : UserControl
{
    public ErrorListView()
    {
        InitializeComponent();

        ErrorDataGrid.DoubleTapped += OnDataGridDoubleTapped;
    }

    private void OnDataGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ErrorListViewModel vm)
        {
            vm.GoToErrorCommand.Execute(null);
        }
    }
}
