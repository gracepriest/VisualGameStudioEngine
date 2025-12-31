using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class InlineConstantDialog : Window
{
    public InlineConstantDialog()
    {
        InitializeComponent();
    }

    public InlineConstantDialog(InlineConstantDialogViewModel viewModel) : this()
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

        if (DataContext is InlineConstantDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
