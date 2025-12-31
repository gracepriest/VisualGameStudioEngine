using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class QuickOpenDialog : Window
{
    public QuickOpenDialog()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var searchBox = this.FindControl<TextBox>("SearchBox");
        searchBox?.Focus();

        if (DataContext is QuickOpenViewModel vm)
        {
            vm.ItemSelected += OnItemSelected;
            vm.Cancelled += OnCancelled;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is QuickOpenViewModel vm)
        {
            vm.ItemSelected -= OnItemSelected;
            vm.Cancelled -= OnCancelled;
        }
        base.OnClosed(e);
    }

    private void OnItemSelected(object? sender, QuickOpenResult e)
    {
        Close(e);
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        Close(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is QuickOpenViewModel vm)
        {
            switch (e.Key)
            {
                case Key.Up:
                    vm.MoveUpCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.MoveDownCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ConfirmCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.CancelCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        base.OnKeyDown(e);
    }
}
