using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class RenameDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private string _originalName = "";

    [ObservableProperty]
    private string _newName = "";

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
    private ObservableCollection<ReferencePreviewItem> _references = new();

    [ObservableProperty]
    private bool _previewChanges = true;

    public event EventHandler<RenameResult>? RenameCompleted;
    public event EventHandler? Cancelled;

    public RenameDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public async Task InitializeAsync(string filePath, int line, int column, string symbolName)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        OriginalName = symbolName;
        NewName = symbolName;

        await LoadReferencesAsync();
    }

    private async Task LoadReferencesAsync()
    {
        IsLoading = true;
        References.Clear();

        try
        {
            var refs = await _refactoringService.FindAllReferencesAsync(FilePath, Line, Column);
            foreach (var r in refs)
            {
                References.Add(new ReferencePreviewItem
                {
                    FilePath = r.FilePath,
                    FileName = Path.GetFileName(r.FilePath),
                    Line = r.Line,
                    Column = r.Column,
                    Text = r.Text,
                    IsDefinition = r.Type == SymbolLocationType.Definition,
                    IsIncluded = true
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading references: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnNewNameChanged(string value)
    {
        ValidateName();
    }

    private void ValidateName()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Name cannot be empty";
            return;
        }

        if (NewName == OriginalName)
        {
            ErrorMessage = "New name is the same as original";
            return;
        }

        if (!char.IsLetter(NewName[0]) && NewName[0] != '_')
        {
            ErrorMessage = "Name must start with a letter or underscore";
            return;
        }

        if (!NewName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            ErrorMessage = "Name can only contain letters, digits, and underscores";
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage)) return;

        IsLoading = true;

        try
        {
            var result = await _refactoringService.RenameSymbolAsync(FilePath, Line, Column, NewName);

            if (result.Success)
            {
                // Apply the edits
                foreach (var fileEdit in result.FileEdits)
                {
                    await ApplyEditsToFileAsync(fileEdit);
                }
            }

            RenameCompleted?.Invoke(this, result);
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
                var line = lines[edit.StartLine - 1];
                var before = line.Substring(0, edit.StartColumn - 1);
                var after = line.Substring(edit.EndColumn - 1);
                lines[edit.StartLine - 1] = before + edit.NewText + after;
            }
            else
            {
                // Multi-line edit (not typically used for rename)
                var startLine = lines[edit.StartLine - 1];
                var endLine = lines[edit.EndLine - 1];
                var before = startLine.Substring(0, edit.StartColumn - 1);
                var after = endLine.Substring(edit.EndColumn - 1);

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

public partial class ReferencePreviewItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private int _column;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isDefinition;

    [ObservableProperty]
    private bool _isIncluded = true;
}
