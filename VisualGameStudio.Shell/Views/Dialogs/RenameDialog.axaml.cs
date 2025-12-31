using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class RenameDialog : Window
{
    public RenameDialog()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var textBox = this.FindControl<TextBox>("NewNameTextBox");
        textBox?.Focus();
        textBox?.SelectAll();

        if (DataContext is RenameDialogViewModel vm)
        {
            vm.RenameCompleted += (s, result) => Close(result);
            vm.Cancelled += (s, _) => Close(null);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (DataContext is RenameDialogViewModel vm)
            {
                vm.RenameCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
