using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel vm)
        {
            vm.CloseDialog = () => Close(vm.DialogResult);
        }
    }
}
