using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class AttachToProcessDialog : Window
{
    public AttachToProcessDialog()
    {
        InitializeComponent();
    }

    public AttachToProcessDialog(AttachToProcessViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.ProcessSelected += (s, e) =>
        {
            Close(viewModel.SelectedProcessId);
        };

        viewModel.Cancelled += (s, e) =>
        {
            Close(null);
        };
    }
}
