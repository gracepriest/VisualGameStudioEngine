using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ReorderParametersDialog : Window
{
    public ReorderParametersDialog()
    {
        InitializeComponent();
    }

    public ReorderParametersDialog(ReorderParametersDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.SetCloseAction(success =>
        {
            Close(success);
        });
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is ReorderParametersDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
