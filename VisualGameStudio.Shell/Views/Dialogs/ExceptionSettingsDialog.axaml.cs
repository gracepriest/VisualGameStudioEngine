using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ExceptionSettingsDialog : Window
{
    public ExceptionSettingsDialog()
    {
        InitializeComponent();
    }

    public ExceptionSettingsDialog(ExceptionSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.SettingsApplied += (s, e) =>
        {
            Close(viewModel.ResultSettings);
        };

        viewModel.Cancelled += (s, e) =>
        {
            Close(null);
        };
    }
}
