using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class IntroduceVariableDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private string _variableName = "newVariable";

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private int _startLine;

    [ObservableProperty]
    private int _startColumn;

    [ObservableProperty]
    private int _endLine;

    [ObservableProperty]
    private int _endColumn;

    [ObservableProperty]
    private string _selectedExpression = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string? _inferredType;

    [ObservableProperty]
    private string? _customType;

    [ObservableProperty]
    private bool _useInferredType = true;

    [ObservableProperty]
    private bool _replaceAllOccurrences;

    [ObservableProperty]
    private string _previewDeclaration = "";

    public string[] CommonTypes { get; } = { "Integer", "Long", "Single", "Double", "String", "Boolean", "Object" };

    public event EventHandler<IntroduceVariableResult>? IntroduceCompleted;
    public event EventHandler? Cancelled;

    public IntroduceVariableDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public void Initialize(string filePath, int startLine, int startColumn, int endLine, int endColumn, string selectedExpression)
    {
        FilePath = filePath;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        SelectedExpression = selectedExpression;

        // Generate default variable name from expression
        GenerateDefaultVariableName();

        // Try to infer type
        InferredType = InferTypeFromExpression(selectedExpression);
        UseInferredType = InferredType != null;

        UpdatePreview();
    }

    private void GenerateDefaultVariableName()
    {
        var expr = SelectedExpression.Trim();

        // For string literals, use "text" or "message"
        if (expr.StartsWith("\"") && expr.EndsWith("\""))
        {
            VariableName = "text";
            return;
        }

        // For numeric literals
        if (double.TryParse(expr, out _))
        {
            VariableName = "value";
            return;
        }

        // For function calls, use the function name
        if (expr.Contains("("))
        {
            var funcName = expr.Split('(')[0].Trim();
            if (!string.IsNullOrEmpty(funcName))
            {
                // Convert to camelCase
                VariableName = char.ToLower(funcName[0]) + funcName.Substring(1) + "Result";
                return;
            }
        }

        // For property access, use the property name
        if (expr.Contains("."))
        {
            var parts = expr.Split('.');
            var lastPart = parts[^1].Trim();
            if (!string.IsNullOrEmpty(lastPart) && char.IsLetter(lastPart[0]))
            {
                VariableName = char.ToLower(lastPart[0]) + lastPart.Substring(1);
                return;
            }
        }

        // Default name
        VariableName = "newVariable";
    }

    private string? InferTypeFromExpression(string expression)
    {
        expression = expression.Trim();

        // String literals
        if (expression.StartsWith("\"") && expression.EndsWith("\""))
            return "String";

        // Integer literals
        if (int.TryParse(expression, out _))
            return "Integer";

        // Long literals
        if (expression.EndsWith("L", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(expression.TrimEnd('L', 'l'), out _))
            return "Long";

        // Double/Single literals
        if (expression.Contains('.') && double.TryParse(expression.TrimEnd('D', 'd', 'F', 'f'), out _))
        {
            if (expression.EndsWith("F", StringComparison.OrdinalIgnoreCase))
                return "Single";
            return "Double";
        }

        // Boolean literals
        if (expression.Equals("True", StringComparison.OrdinalIgnoreCase) ||
            expression.Equals("False", StringComparison.OrdinalIgnoreCase))
            return "Boolean";

        // Function calls - try to infer from common patterns
        if (expression.Contains("("))
        {
            var funcName = expression.Split('(')[0].Trim().ToLowerInvariant();
            return funcName switch
            {
                "len" or "instr" or "val" or "cint" or "abs" => "Integer",
                "mid" or "left" or "right" or "trim" or "ltrim" or "rtrim" or "ucase" or "lcase" or "str" or "cstr" => "String",
                "cdbl" or "sqrt" or "sin" or "cos" or "tan" => "Double",
                "cbool" => "Boolean",
                _ => null
            };
        }

        return null;
    }

    partial void OnVariableNameChanged(string value)
    {
        ValidateName();
        UpdatePreview();
    }

    partial void OnUseInferredTypeChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnCustomTypeChanged(string? value)
    {
        UpdatePreview();
    }

    private void ValidateName()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(VariableName))
        {
            ErrorMessage = "Variable name cannot be empty";
            return;
        }

        if (!char.IsLetter(VariableName[0]) && VariableName[0] != '_')
        {
            ErrorMessage = "Name must start with a letter or underscore";
            return;
        }

        if (!VariableName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            ErrorMessage = "Name can only contain letters, digits, and underscores";
            return;
        }

        // Check for reserved keywords
        var keywords = new[] { "Dim", "Sub", "Function", "If", "Then", "Else", "For", "While", "Do", "Loop", "Next", "End", "Return", "Class", "Module", "As", "New", "Nothing", "True", "False" };
        if (keywords.Any(k => k.Equals(VariableName, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = "Name cannot be a reserved keyword";
        }
    }

    private void UpdatePreview()
    {
        var typeToUse = UseInferredType ? InferredType : CustomType;

        if (!string.IsNullOrEmpty(typeToUse))
        {
            PreviewDeclaration = $"Dim {VariableName} As {typeToUse} = {SelectedExpression.Trim()}";
        }
        else
        {
            PreviewDeclaration = $"Dim {VariableName} = {SelectedExpression.Trim()}";
        }
    }

    [RelayCommand]
    private async Task IntroduceAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage)) return;

        IsLoading = true;

        try
        {
            var typeToUse = UseInferredType ? InferredType : CustomType;

            var result = await _refactoringService.IntroduceVariableAsync(
                FilePath,
                StartLine,
                StartColumn,
                EndLine,
                EndColumn,
                VariableName,
                typeToUse,
                ReplaceAllOccurrences);

            if (result.Success && result.FileEdit != null)
            {
                await ApplyEditsToFileAsync(result.FileEdit);
            }

            IntroduceCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ApplyEditsToFileAsync(FileEdit fileEdit)
    {
        var content = await _fileService.ReadFileAsync(fileEdit.FilePath);
        var lines = content.Split('\n').ToList();

        // Apply edits in reverse order to maintain line numbers
        foreach (var edit in fileEdit.Edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            if (edit.StartLine == edit.EndLine)
            {
                if (edit.StartColumn == 1 && edit.EndColumn == 1)
                {
                    // This is an insertion at the beginning of a line
                    lines.Insert(edit.StartLine - 1, edit.NewText.TrimEnd('\n'));
                }
                else
                {
                    // Single line edit
                    if (edit.StartLine <= lines.Count)
                    {
                        var line = lines[edit.StartLine - 1];
                        var before = edit.StartColumn > 1 && edit.StartColumn <= line.Length + 1
                            ? line.Substring(0, edit.StartColumn - 1)
                            : "";
                        var after = edit.EndColumn <= line.Length + 1
                            ? line.Substring(Math.Min(edit.EndColumn - 1, line.Length))
                            : "";
                        lines[edit.StartLine - 1] = before + edit.NewText + after;
                    }
                }
            }
            else
            {
                // Multi-line edit
                var startLine = lines[edit.StartLine - 1];
                var endLine = edit.EndLine <= lines.Count ? lines[edit.EndLine - 1] : "";
                var before = edit.StartColumn > 1 ? startLine.Substring(0, Math.Min(edit.StartColumn - 1, startLine.Length)) : "";
                var after = edit.EndColumn <= endLine.Length + 1 ? endLine.Substring(Math.Min(edit.EndColumn - 1, endLine.Length)) : "";

                // Remove intermediate lines
                for (var i = edit.EndLine - 1; i >= edit.StartLine; i--)
                {
                    if (i < lines.Count)
                        lines.RemoveAt(i);
                }

                lines.Insert(edit.StartLine - 1, before + edit.NewText + after);
            }
        }

        var newContent = string.Join("\n", lines);
        await _fileService.WriteFileAsync(fileEdit.FilePath, newContent);
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
