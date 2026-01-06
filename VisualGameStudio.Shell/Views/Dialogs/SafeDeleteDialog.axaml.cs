using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class SafeDeleteDialog : Window
{
    public SafeDeleteDialog()
    {
        InitializeComponent();
    }

    public SafeDeleteDialog(SafeDeleteDialogViewModel viewModel) : this()
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
            if (DataContext is SafeDeleteDialogViewModel vm)
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
