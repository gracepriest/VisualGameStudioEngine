using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ExtractConstantDialog : Window
{
    public ExtractConstantDialog()
    {
        InitializeComponent();
    }

    public ExtractConstantDialog(ExtractConstantDialogViewModel viewModel) : this()
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

        if (DataContext is ExtractConstantDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
