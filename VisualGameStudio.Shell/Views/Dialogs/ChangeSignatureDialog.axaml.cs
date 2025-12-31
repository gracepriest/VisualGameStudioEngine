using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class ChangeSignatureDialog : Window
{
    public ChangeSignatureDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Wire up events from the view model
        if (DataContext is ChangeSignatureDialogViewModel vm)
        {
            vm.ChangeCompleted += OnChangeCompleted;
            vm.Cancelled += OnCancelled;
        }
    }

    private void OnChangeCompleted(object? sender, ChangeSignatureResult result)
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
    }
}
