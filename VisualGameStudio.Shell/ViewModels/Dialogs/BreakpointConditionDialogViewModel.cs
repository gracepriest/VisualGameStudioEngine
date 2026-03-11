using System.Text.RegularExpressions;
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

    [ObservableProperty]
    private string _conditionError = "";

    [ObservableProperty]
    private string _logMessageError = "";

    // Result properties
    public string? ResultCondition { get; private set; }
    public string? ResultHitCount { get; private set; }
    public string? ResultLogMessage { get; private set; }
    public bool DialogResult { get; private set; }

    public event EventHandler? ConditionSet;
    public event EventHandler? Cancelled;

    /// <summary>
    /// Binary operators that cannot appear at the start or end of an expression.
    /// </summary>
    private static readonly string[] BinaryOperatorWords = { "And", "Or" };
    private static readonly char[] BinaryOperatorChars = { '+', '-', '*', '/', '=', '<', '>' };
    private static readonly string[] TrailingOperatorTokens = { "And", "Or", "+", "-", "*", "/", "=", "<", ">", "<>" };
    private static readonly string[] LeadingBinaryOperatorTokens = { "And", "Or", "*", "/", "<>", ">=", "<=", ">" , "<" };

    /// <summary>
    /// Patterns for detecting doubled/repeated operators like "++ ", "== ==", etc.
    /// </summary>
    private static readonly Regex DoubleOperatorRegex = new(
        @"(\+\s*\+|--\s*--|==\s*==|\*\s*\*|//|<>\s*<>|<<|>>|&&|\|\|)",
        RegexOptions.Compiled);

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

    partial void OnLogMessageChanged(string value)
    {
        ValidateLogMessage();
    }

    internal void ValidateCondition()
    {
        ConditionError = "";
        ErrorMessage = "";

        if (!IsConditionalExpression)
            return;

        // Reject empty/whitespace-only conditions
        if (string.IsNullOrWhiteSpace(Condition))
        {
            if (Condition.Length > 0)
            {
                ConditionError = "Condition cannot be empty or whitespace only";
                ErrorMessage = ConditionError;
            }
            return;
        }

        var expr = Condition;

        // Check balanced parentheses
        int parenBalance = 0;
        foreach (char c in expr)
        {
            if (c == '(') parenBalance++;
            else if (c == ')') parenBalance--;
            if (parenBalance < 0)
            {
                ConditionError = "Unbalanced parentheses in expression";
                ErrorMessage = ConditionError;
                return;
            }
        }
        if (parenBalance != 0)
        {
            ConditionError = "Unbalanced parentheses in expression";
            ErrorMessage = ConditionError;
            return;
        }

        // Check balanced square brackets
        int bracketBalance = 0;
        foreach (char c in expr)
        {
            if (c == '[') bracketBalance++;
            else if (c == ']') bracketBalance--;
            if (bracketBalance < 0)
            {
                ConditionError = "Unbalanced square brackets in expression";
                ErrorMessage = ConditionError;
                return;
            }
        }
        if (bracketBalance != 0)
        {
            ConditionError = "Unbalanced square brackets in expression";
            ErrorMessage = ConditionError;
            return;
        }

        // Check balanced string quotes (count unescaped double-quotes)
        int quoteCount = 0;
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"')
            {
                // Check if escaped by a preceding backslash
                bool escaped = false;
                int backslashCount = 0;
                for (int j = i - 1; j >= 0 && expr[j] == '\\'; j--)
                    backslashCount++;
                if (backslashCount % 2 == 1)
                    escaped = true;

                if (!escaped)
                    quoteCount++;
            }
        }
        if (quoteCount % 2 != 0)
        {
            ConditionError = "Unbalanced string quotes in expression";
            ErrorMessage = ConditionError;
            return;
        }

        // Check for double operators
        if (DoubleOperatorRegex.IsMatch(expr))
        {
            ConditionError = "Expression contains double operators";
            ErrorMessage = ConditionError;
            return;
        }

        // Detect trailing operators
        var trimmedEnd = expr.TrimEnd();
        foreach (var op in TrailingOperatorTokens)
        {
            if (op.Length == 1)
            {
                // Single char operator at end
                if (trimmedEnd.EndsWith(op))
                {
                    ConditionError = $"Expression ends with incomplete operator '{op}'";
                    ErrorMessage = ConditionError;
                    return;
                }
            }
            else
            {
                // Multi-char like "And", "Or", "<>"
                if (trimmedEnd.EndsWith(op, StringComparison.Ordinal))
                {
                    // For word operators, make sure they're standalone (preceded by space or start of string)
                    if (char.IsLetter(op[0]))
                    {
                        int startIdx = trimmedEnd.Length - op.Length;
                        if (startIdx == 0 || !char.IsLetterOrDigit(trimmedEnd[startIdx - 1]))
                        {
                            ConditionError = $"Expression ends with incomplete operator '{op}'";
                            ErrorMessage = ConditionError;
                            return;
                        }
                    }
                    else
                    {
                        ConditionError = $"Expression ends with incomplete operator '{op}'";
                        ErrorMessage = ConditionError;
                        return;
                    }
                }
            }
        }

        // Detect leading binary operators
        var trimmedStart = expr.TrimStart();
        foreach (var op in LeadingBinaryOperatorTokens)
        {
            if (op.Length == 1)
            {
                if (trimmedStart.StartsWith(op))
                {
                    // Allow leading '-' and '+' as unary (they are not in LeadingBinaryOperatorTokens for * / < > <>)
                    ConditionError = $"Expression starts with binary operator '{op}'";
                    ErrorMessage = ConditionError;
                    return;
                }
            }
            else
            {
                if (trimmedStart.StartsWith(op, StringComparison.Ordinal))
                {
                    if (char.IsLetter(op[0]))
                    {
                        // Word operator: check it's standalone
                        if (trimmedStart.Length == op.Length || !char.IsLetterOrDigit(trimmedStart[op.Length]))
                        {
                            ConditionError = $"Expression starts with binary operator '{op}'";
                            ErrorMessage = ConditionError;
                            return;
                        }
                    }
                    else
                    {
                        ConditionError = $"Expression starts with binary operator '{op}'";
                        ErrorMessage = ConditionError;
                        return;
                    }
                }
            }
        }
    }

    internal void ValidateHitCount()
    {
        ErrorMessage = "";

        if (IsHitCount && !string.IsNullOrWhiteSpace(HitCount))
        {
            var trimmed = HitCount.Trim();

            // Allow formats: "5", ">5", ">=5", "=5", "%5" (modulo)
            // Also allow whitespace around operators: "> 5", ">= 10", "% 3"
            if (trimmed.StartsWith(">="))
            {
                var numPart = trimmed.Substring(2).Trim();
                if (!int.TryParse(numPart, out int val) || val < 0)
                {
                    ErrorMessage = "Hit count must be a non-negative number";
                    return;
                }
            }
            else if (trimmed.StartsWith(">") || trimmed.StartsWith("<") || trimmed.StartsWith("="))
            {
                var numPart = trimmed.Substring(1).Trim();
                if (!int.TryParse(numPart, out int val) || val < 0)
                {
                    ErrorMessage = "Hit count must be a non-negative number";
                    return;
                }
            }
            else if (trimmed.StartsWith("%"))
            {
                var numPart = trimmed.Substring(1).Trim();
                if (!int.TryParse(numPart, out int val) || val <= 0)
                {
                    ErrorMessage = val == 0
                        ? "Modulo by zero is invalid"
                        : "Hit count modulo must be a positive number";
                    return;
                }
            }
            else if (int.TryParse(trimmed, out int val))
            {
                if (val < 0)
                {
                    ErrorMessage = "Hit count cannot be negative";
                    return;
                }
                if (val < 1)
                {
                    ErrorMessage = "Hit count must be a positive number (or use >, >=, =, % prefix)";
                    return;
                }
            }
            else
            {
                ErrorMessage = "Hit count must be a positive number (or use >, >=, =, % prefix)";
            }
        }
    }

    internal void ValidateLogMessage()
    {
        LogMessageError = "";
        ErrorMessage = "";

        if (!IsLogMessage || string.IsNullOrWhiteSpace(LogMessage))
            return;

        var msg = LogMessage;

        // Check balanced {} braces for interpolation expressions
        int braceBalance = 0;
        bool inBrace = false;
        int braceStart = -1;

        for (int i = 0; i < msg.Length; i++)
        {
            if (msg[i] == '{')
            {
                // Check for nested braces
                if (inBrace)
                {
                    LogMessageError = "Nested braces are not allowed in interpolation expressions";
                    ErrorMessage = LogMessageError;
                    return;
                }
                braceBalance++;
                inBrace = true;
                braceStart = i;
            }
            else if (msg[i] == '}')
            {
                if (!inBrace)
                {
                    LogMessageError = "Unbalanced braces in log message";
                    ErrorMessage = LogMessageError;
                    return;
                }

                // Check for empty interpolation {}
                var content = msg.Substring(braceStart + 1, i - braceStart - 1);
                if (string.IsNullOrWhiteSpace(content))
                {
                    LogMessageError = "Empty interpolation expression '{}' is not allowed";
                    ErrorMessage = LogMessageError;
                    return;
                }

                braceBalance--;
                inBrace = false;
            }
        }

        if (braceBalance != 0)
        {
            LogMessageError = "Unbalanced braces in log message";
            ErrorMessage = LogMessageError;
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
        else if (IsLogMessage)
        {
            ValidateLogMessage();
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
        ConditionError = "";
        LogMessageError = "";
    }

    [RelayCommand]
    private void SelectHitCount()
    {
        IsConditionalExpression = false;
        IsHitCount = true;
        IsLogMessage = false;
        ErrorMessage = "";
        ConditionError = "";
        LogMessageError = "";
    }

    [RelayCommand]
    private void SelectLogMessage()
    {
        IsConditionalExpression = false;
        IsHitCount = false;
        IsLogMessage = true;
        ErrorMessage = "";
        ConditionError = "";
        LogMessageError = "";
    }
}
