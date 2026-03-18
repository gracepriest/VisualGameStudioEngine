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

    /// <summary>
    /// Gets the file path that was selected for opening, if any.
    /// </summary>
    public string? SelectedFilePath { get; private set; }

    /// <summary>
    /// Gets the line number requested via go-to-line mode, or -1 if not applicable.
    /// </summary>
    public int RequestedLineNumber { get; private set; } = -1;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var searchBox = this.FindControl<TextBox>("SearchBox");
        searchBox?.Focus();

        if (DataContext is CommandPaletteViewModel vm)
        {
            vm.CommandExecuted += OnCommandExecuted;
            vm.Dismissed += OnDismissed;
            vm.FileOpenRequested += OnFileOpenRequested;
            vm.GoToLineRequested += OnGoToLineRequested;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm)
        {
            vm.CommandExecuted -= OnCommandExecuted;
            vm.Dismissed -= OnDismissed;
            vm.FileOpenRequested -= OnFileOpenRequested;
            vm.GoToLineRequested -= OnGoToLineRequested;
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

    private void OnFileOpenRequested(object? sender, string filePath)
    {
        SelectedFilePath = filePath;
        Close(null);
    }

    private void OnGoToLineRequested(object? sender, int lineNumber)
    {
        RequestedLineNumber = lineNumber;
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
