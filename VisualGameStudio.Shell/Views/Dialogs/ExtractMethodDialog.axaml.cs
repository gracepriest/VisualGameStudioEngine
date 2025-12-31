using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ExtractMethodDialog : Window
{
    public ExtractMethodDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Focus the method name textbox
        MethodNameTextBox?.Focus();
        MethodNameTextBox?.SelectAll();

        // Wire up events from the view model
        if (DataContext is ExtractMethodDialogViewModel vm)
        {
            vm.ExtractCompleted += OnExtractCompleted;
            vm.Cancelled += OnCancelled;
        }
    }

    private void OnExtractCompleted(object? sender, ExtractMethodResult result)
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
        else if (e.Key == Key.Enter && DataContext is ExtractMethodDialogViewModel vm)
        {
            if (string.IsNullOrEmpty(vm.ErrorMessage))
            {
                vm.ExtractCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
