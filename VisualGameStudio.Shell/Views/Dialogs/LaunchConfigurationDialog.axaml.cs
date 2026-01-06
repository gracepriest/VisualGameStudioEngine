using Avalonia.Controls;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class LaunchConfigurationDialog : Window
{
    private LaunchConfigurationDialogViewModel? _viewModel;

    public LaunchConfigurationDialog()
    {
        InitializeComponent();
    }

    public LaunchConfigurationDialog(LaunchConfigurationDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
