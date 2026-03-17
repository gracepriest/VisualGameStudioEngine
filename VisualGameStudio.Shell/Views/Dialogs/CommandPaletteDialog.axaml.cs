using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class CommandPaletteDialog : Window
{
    public CommandPaletteDialog()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var searchBox = this.FindControl<TextBox>("SearchBox");
        searchBox?.Focus();

        if (DataContext is CommandPaletteViewModel vm)
        {
            vm.CommandExecuted += OnCommandExecuted;
            vm.Dismissed += OnDismissed;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm)
        {
            vm.CommandExecuted -= OnCommandExecuted;
            vm.Dismissed -= OnDismissed;
        }
        base.OnClosed(e);
    }

    private void OnCommandExecuted(object? sender, CommandPaletteItem item)
    {
        Close(item);
    }

    private void OnDismissed(object? sender, EventArgs e)
    {
        Close(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm)
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
                    vm.DismissCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        base.OnKeyDown(e);
    }
}
