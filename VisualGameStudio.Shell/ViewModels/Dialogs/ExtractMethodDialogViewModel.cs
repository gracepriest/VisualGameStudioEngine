using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ExtractMethodDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private string _methodName = "NewMethod";

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
    private string _selectedCode = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _previewCode = "";

    [ObservableProperty]
    private bool _createAsFunction;

    [ObservableProperty]
    private string _accessModifier = "Private";

    public string[] AccessModifiers { get; } = { "Private", "Public", "Protected" };

    public event EventHandler<ExtractMethodResult>? ExtractCompleted;
    public event EventHandler? Cancelled;

    public ExtractMethodDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public void Initialize(string filePath, int startLine, int startColumn, int endLine, int endColumn, string selectedCode)
    {
        FilePath = filePath;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        SelectedCode = selectedCode;

        // Generate default method name based on first line
        GenerateDefaultMethodName();
        UpdatePreview();
    }

    private void GenerateDefaultMethodName()
    {
        // Try to generate a meaningful name from the selected code
        var firstLine = SelectedCode.Split('\n').FirstOrDefault()?.Trim() ?? "";

        // Look for common patterns
        if (firstLine.StartsWith("If ", StringComparison.OrdinalIgnoreCase))
        {
            MethodName = "CheckCondition";
        }
        else if (firstLine.StartsWith("For ", StringComparison.OrdinalIgnoreCase) ||
                 firstLine.StartsWith("While ", StringComparison.OrdinalIgnoreCase))
        {
            MethodName = "ProcessLoop";
        }
        else if (firstLine.Contains("Print", StringComparison.OrdinalIgnoreCase))
        {
            MethodName = "DisplayOutput";
        }
        else if (firstLine.StartsWith("Dim ", StringComparison.OrdinalIgnoreCase))
        {
            MethodName = "InitializeVariables";
        }
        else
        {
            MethodName = "ExtractedMethod";
        }
    }

    partial void OnMethodNameChanged(string value)
    {
        ValidateName();
        UpdatePreview();
    }

    partial void OnCreateAsFunctionChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnAccessModifierChanged(string value)
    {
        UpdatePreview();
    }

    private void ValidateName()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(MethodName))
        {
            ErrorMessage = "Method name cannot be empty";
            return;
        }

        if (!char.IsLetter(MethodName[0]) && MethodName[0] != '_')
        {
            ErrorMessage = "Name must start with a letter or underscore";
            return;
        }

        if (!MethodName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            ErrorMessage = "Name can only contain letters, digits, and underscores";
            return;
        }

        // Check for reserved keywords
        var keywords = new[] { "Sub", "Function", "If", "Then", "Else", "For", "While", "Do", "Loop", "Next", "End", "Dim", "Return", "Class", "Module" };
        if (keywords.Any(k => k.Equals(MethodName, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = "Name cannot be a reserved keyword";
        }
    }

    private void UpdatePreview()
    {
        var methodType = CreateAsFunction ? "Function" : "Sub";
        var returnType = CreateAsFunction ? " As Object" : "";
        var indent = "    ";

        var codeLines = SelectedCode.Split('\n')
            .Select(l => $"{indent}{indent}{l.TrimStart()}")
            .ToList();

        PreviewCode = $@"{AccessModifier} {methodType} {MethodName}(){returnType}
{string.Join("\n", codeLines)}
{indent}End {methodType}";
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage)) return;

        IsLoading = true;

        try
        {
            var result = await _refactoringService.ExtractMethodAsync(
                FilePath,
                StartLine,
                StartColumn,
                EndLine,
                EndColumn,
                MethodName);

            if (result.Success && result.FileEdit != null)
            {
                // Apply the edits - we need to modify them for our custom settings
                await ApplyExtractAsync(result.FileEdit);
            }

            ExtractCompleted?.Invoke(this, result);
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

    private async Task ApplyExtractAsync(FileEdit fileEdit)
    {
        var content = await _fileService.ReadFileAsync(fileEdit.FilePath);
        var lines = content.Split('\n').ToList();

        // Apply edits in reverse order to maintain line numbers
        foreach (var edit in fileEdit.Edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            if (edit.NewText.Contains("Private Sub"))
            {
                // This is the new method definition - customize it
                var methodType = CreateAsFunction ? "Function" : "Sub";
                var returnType = CreateAsFunction ? " As Object" : "";

                // Get the extracted code lines from the original edit
                var editLines = edit.NewText.Split('\n').ToList();
                var codeLines = new List<string>();

                // Find the code between Sub and End Sub
                var inMethod = false;
                foreach (var line in editLines)
                {
                    if (line.TrimStart().StartsWith("End Sub", StringComparison.OrdinalIgnoreCase) ||
                        line.TrimStart().StartsWith("End Function", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    if (inMethod)
                    {
                        codeLines.Add(line);
                    }
                    if (line.TrimStart().StartsWith("Private Sub", StringComparison.OrdinalIgnoreCase))
                    {
                        inMethod = true;
                    }
                }

                // Detect indentation from the insertion point
                var indent = "";
                if (edit.StartLine - 1 < lines.Count)
                {
                    var refLine = lines[edit.StartLine - 1];
                    indent = new string(' ', refLine.TakeWhile(char.IsWhiteSpace).Count());
                }

                // Build the customized method
                var newMethod = $"\n{indent}{AccessModifier} {methodType} {MethodName}(){returnType}\n";
                foreach (var codeLine in codeLines)
                {
                    newMethod += $"{codeLine}\n";
                }
                newMethod += $"{indent}End {methodType}\n";

                edit.NewText = newMethod;
            }

            // Apply the edit
            if (edit.StartLine == edit.EndLine)
            {
                var line = lines[edit.StartLine - 1];
                var before = edit.StartColumn > 1 ? line.Substring(0, edit.StartColumn - 1) : "";
                var after = edit.EndColumn <= line.Length ? line.Substring(edit.EndColumn - 1) : "";
                lines[edit.StartLine - 1] = before + edit.NewText + after;
            }
            else
            {
                // Multi-line replacement (for replacing selected code with method call)
                var startLine = lines[edit.StartLine - 1];
                var endLine = lines[edit.EndLine - 1];
                var before = edit.StartColumn > 1 ? startLine.Substring(0, edit.StartColumn - 1) : "";
                var after = edit.EndColumn <= endLine.Length + 1 ? endLine.Substring(Math.Min(edit.EndColumn - 1, endLine.Length)) : "";

                // Remove intermediate lines
                for (var i = edit.EndLine - 1; i >= edit.StartLine; i--)
                {
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
