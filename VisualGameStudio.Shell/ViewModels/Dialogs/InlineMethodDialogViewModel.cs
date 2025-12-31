using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class InlineMethodDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private string _methodName = "";

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private int _column;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _methodBody = "";

    [ObservableProperty]
    private int _callSiteCount;

    [ObservableProperty]
    private bool _removeDefinition = true;

    [ObservableProperty]
    private bool _isFunction;

    [ObservableProperty]
    private string _methodSignature = "";

    [ObservableProperty]
    private ObservableCollection<CallSitePreviewItem> _callSites = new();

    [ObservableProperty]
    private bool _canInline = true;

    [ObservableProperty]
    private string _warningMessage = "";

    public event EventHandler<InlineMethodResult>? InlineCompleted;
    public event EventHandler? Cancelled;

    public InlineMethodDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public async Task InitializeAsync(string filePath, int line, int column, string methodName)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        MethodName = methodName;

        await LoadMethodInfoAsync();
    }

    private async Task LoadMethodInfoAsync()
    {
        IsLoading = true;
        ErrorMessage = "";
        WarningMessage = "";
        CanInline = true;
        CallSites.Clear();

        try
        {
            var methodInfo = await _refactoringService.GetMethodInfoAsync(FilePath, Line, Column);

            if (methodInfo == null)
            {
                ErrorMessage = "Could not find method definition";
                CanInline = false;
                return;
            }

            MethodName = methodInfo.Name;
            MethodBody = methodInfo.Body;
            CallSiteCount = methodInfo.CallSiteCount;
            IsFunction = methodInfo.IsFunction;

            // Build method signature
            var methodType = methodInfo.IsFunction ? "Function" : "Sub";
            var returnPart = methodInfo.IsFunction && methodInfo.ReturnType != null
                ? $" As {methodInfo.ReturnType}"
                : "";
            var paramsPart = methodInfo.Parameters.Length > 0
                ? string.Join(", ", methodInfo.Parameters)
                : "";
            MethodSignature = $"{methodType} {methodInfo.Name}({paramsPart}){returnPart}";

            // Add call sites
            foreach (var callSite in methodInfo.CallSites)
            {
                CallSites.Add(new CallSitePreviewItem
                {
                    FilePath = callSite.FilePath,
                    FileName = Path.GetFileName(callSite.FilePath),
                    Line = callSite.Line,
                    Text = callSite.Text,
                    IsIncluded = true
                });
            }

            // Check for limitations
            if (methodInfo.Parameters.Length > 0)
            {
                WarningMessage = "Methods with parameters cannot be inlined yet.";
                CanInline = false;
            }
            else if (methodInfo.IsFunction)
            {
                WarningMessage = "Functions with return values cannot be inlined yet.";
                CanInline = false;
            }
            else if (methodInfo.CallSiteCount == 0)
            {
                WarningMessage = "No call sites found for this method.";
                CanInline = false;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading method info: {ex.Message}";
            CanInline = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task InlineAsync()
    {
        if (!CanInline) return;

        IsLoading = true;

        try
        {
            var result = await _refactoringService.InlineMethodAsync(FilePath, Line, Column, RemoveDefinition);

            if (result.Success)
            {
                // Apply the edits
                foreach (var fileEdit in result.FileEdits)
                {
                    await ApplyEditsToFileAsync(fileEdit);
                }
            }

            InlineCompleted?.Invoke(this, result);
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
            else
            {
                // Multi-line edit (removing definition)
                if (string.IsNullOrEmpty(edit.NewText))
                {
                    // Remove lines
                    var startIndex = edit.StartLine - 1;
                    var count = edit.EndLine - edit.StartLine;
                    if (startIndex >= 0 && startIndex < lines.Count)
                    {
                        for (var i = 0; i < count && startIndex < lines.Count; i++)
                        {
                            lines.RemoveAt(startIndex);
                        }
                    }
                }
                else
                {
                    // Replace range with new text
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

public partial class CallSitePreviewItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isIncluded = true;
}
