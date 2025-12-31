using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ListSelectionDialog : Window
{
    private readonly ListSelectionDialogViewModel _viewModel;

    public ListSelectionDialog(ListSelectionDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedIndex >= 0)
        {
            Close(_viewModel.SelectedIndex);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(-1);
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.SelectedIndex >= 0)
        {
            Close(_viewModel.SelectedIndex);
        }
    }
}
