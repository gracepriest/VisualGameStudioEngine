using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ConvertToPositionalArgumentsDialog : Window
{
    public ConvertToPositionalArgumentsDialog()
    {
        InitializeComponent();
    }

    public ConvertToPositionalArgumentsDialog(ConvertToPositionalArgumentsDialogViewModel viewModel) : this()
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

        if (DataContext is ConvertToPositionalArgumentsDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
