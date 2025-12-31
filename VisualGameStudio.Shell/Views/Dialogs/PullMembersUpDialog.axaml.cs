using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class PullMembersUpDialog : Window
{
    public PullMembersUpDialog()
    {
        InitializeComponent();
    }

    public PullMembersUpDialog(PullMembersUpDialogViewModel viewModel) : this()
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

        if (DataContext is PullMembersUpDialogViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
