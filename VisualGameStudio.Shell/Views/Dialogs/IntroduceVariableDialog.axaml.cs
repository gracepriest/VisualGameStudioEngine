using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class IntroduceVariableDialog : Window
{
    public IntroduceVariableDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Focus the variable name textbox
        VariableNameTextBox?.Focus();
        VariableNameTextBox?.SelectAll();

        // Wire up events from the view model
        if (DataContext is IntroduceVariableDialogViewModel vm)
        {
            vm.IntroduceCompleted += OnIntroduceCompleted;
            vm.Cancelled += OnCancelled;
        }
    }

    private void OnIntroduceCompleted(object? sender, IntroduceVariableResult result)
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
        else if (e.Key == Key.Enter && DataContext is IntroduceVariableDialogViewModel vm)
        {
            if (string.IsNullOrEmpty(vm.ErrorMessage))
            {
                vm.IntroduceCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
