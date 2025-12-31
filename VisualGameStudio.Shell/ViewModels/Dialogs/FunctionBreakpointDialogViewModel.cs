using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class FunctionBreakpointDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _functionName = "";

    [ObservableProperty]
    private string _condition = "";

    [ObservableProperty]
    private string _errorMessage = "";

    public event EventHandler<string>? FunctionBreakpointAdded;
    public event EventHandler? Cancelled;

    partial void OnFunctionNameChanged(string value)
    {
        ValidateFunctionName();
    }

    private void ValidateFunctionName()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(FunctionName))
        {
            ErrorMessage = "Function name cannot be empty";
            return;
        }

        // Basic validation - function name should be a valid identifier
        var name = FunctionName.Trim();
        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            ErrorMessage = "Function name must start with a letter or underscore";
            return;
        }

        // Allow dots for qualified names like Module.Function
        if (!name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.'))
        {
            ErrorMessage = "Function name can only contain letters, digits, underscores, and dots";
        }
    }

    [RelayCommand]
    private void Add()
    {
        ValidateFunctionName();
        if (!string.IsNullOrEmpty(ErrorMessage)) return;

        FunctionBreakpointAdded?.Invoke(this, FunctionName.Trim());
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
