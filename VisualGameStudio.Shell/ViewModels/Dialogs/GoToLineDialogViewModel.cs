using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class GoToLineDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _lineNumberText = "";

    [ObservableProperty]
    private int _currentLine;

    [ObservableProperty]
    private int _totalLines;

    [ObservableProperty]
    private string _errorMessage = "";

    public int? ResultLine { get; private set; }
    public bool DialogResult { get; private set; }

    public event EventHandler? LineSelected;
    public event EventHandler? Cancelled;

    public GoToLineDialogViewModel()
    {
    }

    public GoToLineDialogViewModel(int currentLine, int totalLines)
    {
        CurrentLine = currentLine;
        TotalLines = totalLines;
        LineNumberText = currentLine.ToString();
    }

    public string PromptText => $"Line number (1 - {TotalLines}):";

    partial void OnLineNumberTextChanged(string value)
    {
        ValidateLineNumber();
    }

    private void ValidateLineNumber()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(LineNumberText))
        {
            return;
        }

        if (!int.TryParse(LineNumberText.Trim(), out int lineNumber))
        {
            ErrorMessage = "Please enter a valid number";
            return;
        }

        if (lineNumber < 1)
        {
            ErrorMessage = "Line number must be at least 1";
            return;
        }

        if (lineNumber > TotalLines)
        {
            ErrorMessage = $"Line number must be at most {TotalLines}";
        }
    }

    [RelayCommand]
    private void GoToLine()
    {
        ValidateLineNumber();

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LineNumberText))
        {
            ErrorMessage = "Please enter a line number";
            return;
        }

        if (int.TryParse(LineNumberText.Trim(), out int lineNumber))
        {
            // Clamp to valid range
            lineNumber = Math.Max(1, Math.Min(lineNumber, TotalLines));
            ResultLine = lineNumber;
            DialogResult = true;
            LineSelected?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
