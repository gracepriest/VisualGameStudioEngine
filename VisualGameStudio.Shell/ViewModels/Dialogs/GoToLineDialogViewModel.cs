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

    [ObservableProperty]
    private string _modeHint = "";

    public int? ResultLine { get; private set; }
    public int? ResultColumn { get; private set; }
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

    public string PromptText => $"Go to Line (1 - {TotalLines}):";

    partial void OnLineNumberTextChanged(string value)
    {
        ValidateInput();
    }

    private void ValidateInput()
    {
        ErrorMessage = "";
        ModeHint = "";

        if (string.IsNullOrWhiteSpace(LineNumberText))
        {
            return;
        }

        var input = LineNumberText.Trim();

        // Support line:column format
        if (input.Contains(':'))
        {
            ModeHint = "Go to Line:Column";
            var parts = input.Split(':', 2);

            if (!int.TryParse(parts[0].Trim(), out int line))
            {
                ErrorMessage = "Please enter a valid line number";
                return;
            }

            if (line < 1)
            {
                ErrorMessage = "Line number must be at least 1";
                return;
            }

            if (line > TotalLines)
            {
                ErrorMessage = $"Line number must be at most {TotalLines}";
                return;
            }

            if (!string.IsNullOrEmpty(parts[1]))
            {
                if (!int.TryParse(parts[1].Trim(), out int col) || col < 1)
                {
                    ErrorMessage = "Please enter a valid column number";
                    return;
                }
            }
        }
        else
        {
            if (!int.TryParse(input, out int lineNumber))
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
    }

    [RelayCommand]
    private void GoToLine()
    {
        ValidateInput();

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LineNumberText))
        {
            ErrorMessage = "Please enter a line number";
            return;
        }

        var input = LineNumberText.Trim();

        if (input.Contains(':'))
        {
            var parts = input.Split(':', 2);
            if (int.TryParse(parts[0].Trim(), out int line))
            {
                line = Math.Max(1, Math.Min(line, TotalLines));
                ResultLine = line;

                if (!string.IsNullOrEmpty(parts[1]) && int.TryParse(parts[1].Trim(), out int col))
                {
                    ResultColumn = Math.Max(1, col);
                }
                else
                {
                    ResultColumn = 1;
                }

                DialogResult = true;
                LineSelected?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (int.TryParse(input, out int lineNumber))
        {
            lineNumber = Math.Max(1, Math.Min(lineNumber, TotalLines));
            ResultLine = lineNumber;
            ResultColumn = 1;
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
