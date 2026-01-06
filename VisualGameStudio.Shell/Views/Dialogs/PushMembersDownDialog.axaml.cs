using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class PushMembersDownDialog : Window
{
    public PushMembersDownDialog()
    {
        InitializeComponent();
    }

    public PushMembersDownDialog(PushMembersDownDialogViewModel viewModel) : this()
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

        try
        {
            if (DataContext is PushMembersDownDialogViewModel vm)
            {
                await vm.InitializeAsync();
            }
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }
}
