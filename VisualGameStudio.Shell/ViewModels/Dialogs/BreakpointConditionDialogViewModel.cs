using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class BreakpointConditionDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _location = "";

    [ObservableProperty]
    private string _condition = "";

    [ObservableProperty]
    private string _hitCount = "";

    [ObservableProperty]
    private string _logMessage = "";

    [ObservableProperty]
    private bool _isConditionalExpression = true;

    [ObservableProperty]
    private bool _isHitCount;

    [ObservableProperty]
    private bool _isLogMessage;

    [ObservableProperty]
    private string _errorMessage = "";

    // Result properties
    public string? ResultCondition { get; private set; }
    public string? ResultHitCount { get; private set; }
    public string? ResultLogMessage { get; private set; }
    public bool DialogResult { get; private set; }

    public event EventHandler? ConditionSet;
    public event EventHandler? Cancelled;

    public BreakpointConditionDialogViewModel()
    {
    }

    public BreakpointConditionDialogViewModel(string location, string? condition, string? hitCount, string? logMessage)
    {
        Location = location;
        Condition = condition ?? "";
        HitCount = hitCount ?? "";
        LogMessage = logMessage ?? "";

        // Determine which tab to show initially
        if (!string.IsNullOrEmpty(logMessage))
        {
            IsLogMessage = true;
            IsConditionalExpression = false;
            IsHitCount = false;
        }
        else if (!string.IsNullOrEmpty(hitCount))
        {
            IsHitCount = true;
            IsConditionalExpression = false;
            IsLogMessage = false;
        }
        else
        {
            IsConditionalExpression = true;
            IsHitCount = false;
            IsLogMessage = false;
        }
    }

    partial void OnConditionChanged(string value)
    {
        ValidateCondition();
    }

    partial void OnHitCountChanged(string value)
    {
        ValidateHitCount();
    }

    private void ValidateCondition()
    {
        ErrorMessage = "";

        if (IsConditionalExpression && !string.IsNullOrWhiteSpace(Condition))
        {
            // Basic validation - check for balanced parentheses
            int balance = 0;
            foreach (char c in Condition)
            {
                if (c == '(') balance++;
                else if (c == ')') balance--;
                if (balance < 0)
                {
                    ErrorMessage = "Unbalanced parentheses in expression";
                    return;
                }
            }
            if (balance != 0)
            {
                ErrorMessage = "Unbalanced parentheses in expression";
            }
        }
    }

    private void ValidateHitCount()
    {
        ErrorMessage = "";

        if (IsHitCount && !string.IsNullOrWhiteSpace(HitCount))
        {
            var trimmed = HitCount.Trim();

            // Allow formats: "5", ">5", ">=5", "=5", "%5" (modulo)
            if (trimmed.StartsWith(">") || trimmed.StartsWith("<") || trimmed.StartsWith("=") || trimmed.StartsWith("%"))
            {
                var numPart = trimmed.TrimStart('>', '<', '=', '%');
                if (!int.TryParse(numPart, out int val) || val < 0)
                {
                    ErrorMessage = "Hit count must be a positive number";
                }
            }
            else if (!int.TryParse(trimmed, out int val) || val < 1)
            {
                ErrorMessage = "Hit count must be a positive number (or use >, >=, =, % prefix)";
            }
        }
    }

    [RelayCommand]
    private void SetCondition()
    {
        if (IsConditionalExpression)
        {
            ValidateCondition();
        }
        else if (IsHitCount)
        {
            ValidateHitCount();
        }

        if (!string.IsNullOrEmpty(ErrorMessage)) return;

        // Set results
        ResultCondition = IsConditionalExpression && !string.IsNullOrWhiteSpace(Condition) ? Condition.Trim() : null;
        ResultHitCount = IsHitCount && !string.IsNullOrWhiteSpace(HitCount) ? HitCount.Trim() : null;
        ResultLogMessage = IsLogMessage && !string.IsNullOrWhiteSpace(LogMessage) ? LogMessage.Trim() : null;

        DialogResult = true;
        ConditionSet?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearCondition()
    {
        Condition = "";
        HitCount = "";
        LogMessage = "";
        ResultCondition = null;
        ResultHitCount = null;
        ResultLogMessage = null;
        DialogResult = true;
        ConditionSet?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectConditional()
    {
        IsConditionalExpression = true;
        IsHitCount = false;
        IsLogMessage = false;
        ErrorMessage = "";
    }

    [RelayCommand]
    private void SelectHitCount()
    {
        IsConditionalExpression = false;
        IsHitCount = true;
        IsLogMessage = false;
        ErrorMessage = "";
    }

    [RelayCommand]
    private void SelectLogMessage()
    {
        IsConditionalExpression = false;
        IsHitCount = false;
        IsLogMessage = true;
        ErrorMessage = "";
    }
}
