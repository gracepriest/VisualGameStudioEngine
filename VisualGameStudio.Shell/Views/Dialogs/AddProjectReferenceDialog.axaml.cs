using Avalonia.Controls;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class AddProjectReferenceDialog : Window
{
    public AddProjectReferenceDialog()
    {
        InitializeComponent();
    }

    public AddProjectReferenceDialog(AddProjectReferenceViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseDialog = () => Close(vm.DialogResult);
    }
}
