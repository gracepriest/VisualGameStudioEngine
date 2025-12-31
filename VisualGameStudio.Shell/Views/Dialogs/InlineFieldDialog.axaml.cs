using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class InlineFieldDialog : Window
{
    public InlineFieldDialog()
    {
        InitializeComponent();
    }

    public InlineFieldDialog(InlineFieldDialogViewModel viewModel) : this()
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

        if (DataContext is InlineFieldDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
