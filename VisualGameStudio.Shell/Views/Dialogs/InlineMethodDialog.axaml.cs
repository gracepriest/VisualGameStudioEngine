using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class InlineMethodDialog : Window
{
    public InlineMethodDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Wire up events from the view model
        if (DataContext is InlineMethodDialogViewModel vm)
        {
            vm.InlineCompleted += OnInlineCompleted;
            vm.Cancelled += OnCancelled;
        }
    }

    private void OnInlineCompleted(object? sender, InlineMethodResult result)
    {
        Close(result);
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        Close(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && DataContext is InlineMethodDialogViewModel vm)
        {
            if (vm.CanInline)
            {
                vm.InlineCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
