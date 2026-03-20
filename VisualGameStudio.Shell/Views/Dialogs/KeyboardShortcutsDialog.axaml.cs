using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class KeyboardShortcutsDialog : AccessibleDialog
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
        EnterActivatesDefaultButton = false; // Search box uses Enter for searching
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var searchBox = this.FindControl<TextBox>("SearchBox");
        searchBox?.Focus();

        if (DataContext is KeyboardShortcutsViewModel vm)
        {
            vm.CloseDialog = () => Close();
        }
    }
}
