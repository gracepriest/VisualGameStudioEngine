using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ConvertToNamedArgumentsDialog : Window
{
    public ConvertToNamedArgumentsDialog()
    {
        InitializeComponent();
    }

    public ConvertToNamedArgumentsDialog(ConvertToNamedArgumentsDialogViewModel viewModel) : this()
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

        if (DataContext is ConvertToNamedArgumentsDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
