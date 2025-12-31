using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class MakeParameterOptionalDialog : Window
{
    public MakeParameterOptionalDialog()
    {
        InitializeComponent();
    }

    public MakeParameterOptionalDialog(MakeParameterOptionalDialogViewModel viewModel) : this()
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

        if (DataContext is MakeParameterOptionalDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
