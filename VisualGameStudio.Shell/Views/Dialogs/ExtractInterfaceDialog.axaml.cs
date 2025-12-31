using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ExtractInterfaceDialog : Window
{
    public ExtractInterfaceDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Focus the interface name textbox
        InterfaceNameTextBox?.Focus();
        InterfaceNameTextBox?.SelectAll();

        // Wire up events from the view model
        if (DataContext is ExtractInterfaceDialogViewModel vm)
        {
            vm.ExtractCompleted += OnExtractCompleted;
            vm.Cancelled += OnCancelled;
        }
    }

    private void OnExtractCompleted(object? sender, ExtractInterfaceResult result)
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
        else if (e.Key == Key.Enter && DataContext is ExtractInterfaceDialogViewModel vm)
        {
            if (string.IsNullOrEmpty(vm.ErrorMessage))
            {
                vm.ExtractCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
