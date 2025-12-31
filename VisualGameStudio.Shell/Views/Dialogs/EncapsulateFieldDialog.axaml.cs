using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class EncapsulateFieldDialog : Window
{
    public EncapsulateFieldDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Focus the property name textbox
        PropertyNameTextBox?.Focus();
        PropertyNameTextBox?.SelectAll();

        // Wire up events from the view model
        if (DataContext is EncapsulateFieldDialogViewModel vm)
        {
            vm.EncapsulateCompleted += OnEncapsulateCompleted;
            vm.Cancelled += OnCancelled;
        }
    }

    private void OnEncapsulateCompleted(object? sender, EncapsulateFieldResult result)
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
        else if (e.Key == Key.Enter && DataContext is EncapsulateFieldDialogViewModel vm)
        {
            if (string.IsNullOrEmpty(vm.ErrorMessage))
            {
                vm.EncapsulateCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
