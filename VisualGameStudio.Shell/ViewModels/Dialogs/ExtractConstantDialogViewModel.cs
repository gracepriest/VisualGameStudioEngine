using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ExtractConstantDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private readonly string _filePath;
    private readonly int _startLine;
    private readonly int _startColumn;
    private readonly int _endLine;
    private readonly int _endColumn;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _literalValue = "";

    [ObservableProperty]
    private string _literalType = "";

    [ObservableProperty]
    private string _inferredType = "";

    [ObservableProperty]
    private string? _containingType;

    [ObservableProperty]
    private string? _containingMethod;

    [ObservableProperty]
    private int _occurrenceCount;

    [ObservableProperty]
    private string _constantName = "";

    [ObservableProperty]
    private string _constantType = "";

    [ObservableProperty]
    private string _selectedAccessibility = "Private";

    [ObservableProperty]
    private bool _replaceAllOccurrences = true;

    [ObservableProperty]
    private bool _createAsShared = true;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<OccurrenceViewModel> Occurrences { get; } = new();

    public ObservableCollection<string> AccessibilityOptions { get; } = new()
    {
        "Private",
        "Public",
        "Protected",
        "Friend"
    };

    public bool CanExtract => !IsLoading &&
                               string.IsNullOrEmpty(ErrorMessage) &&
                               !string.IsNullOrEmpty(ConstantName) &&
                               !string.IsNullOrEmpty(LiteralValue);

    public ExtractConstantDialogViewModel(
        IRefactoringService refactoringService,
        IFileService fileService,
        string filePath,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
        _filePath = filePath;
        _startLine = startLine;
        _startColumn = startColumn;
        _endLine = endLine;
        _endColumn = endColumn;
    }

    public void SetCloseAction(Action<bool> closeAction)
    {
        _closeAction = closeAction;
    }

    partial void OnConstantNameChanged(string value)
    {
        ValidateConstantName();
        UpdatePreview();
        OnPropertyChanged(nameof(CanExtract));
    }

    partial void OnConstantTypeChanged(string value)
    {
        UpdatePreview();
    }

    partial void OnSelectedAccessibilityChanged(string value)
    {
        UpdatePreview();
    }

    partial void OnReplaceAllOccurrencesChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnCreateAsSharedChanged(bool value)
    {
        UpdatePreview();
    }

    private void ValidateConstantName()
    {
        if (string.IsNullOrEmpty(ConstantName))
        {
            ErrorMessage = null;
            return;
        }

        if (!char.IsLetter(ConstantName[0]) && ConstantName[0] != '_')
        {
            ErrorMessage = "Constant name must start with a letter or underscore";
            return;
        }

        if (!ConstantName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            ErrorMessage = "Constant name can only contain letters, digits, and underscores";
            return;
        }

        ErrorMessage = null;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        WarningMessage = null;

        try
        {
            var literalInfo = await _refactoringService.GetLiteralInfoAsync(
                _filePath, _startLine, _startColumn, _endLine, _endColumn);

            if (literalInfo == null)
            {
                ErrorMessage = "No valid literal found at the selected location. Select a string, number, or boolean value.";
                return;
            }

            LiteralValue = literalInfo.Value;
            LiteralType = literalInfo.Type.ToString();
            InferredType = literalInfo.InferredType;
            ConstantType = literalInfo.InferredType;
            ContainingType = literalInfo.ContainingType;
            ContainingMethod = literalInfo.ContainingMethod;
            OccurrenceCount = literalInfo.OccurrenceCount;
            ConstantName = literalInfo.SuggestedName;

            // Populate occurrences
            Occurrences.Clear();
            foreach (var occurrence in literalInfo.Occurrences)
            {
                Occurrences.Add(new OccurrenceViewModel(
                    occurrence.Line,
                    occurrence.Column,
                    $"Line {occurrence.Line}, Column {occurrence.Column}"));
            }

            if (OccurrenceCount > 1)
            {
                WarningMessage = $"Found {OccurrenceCount} occurrences of this literal. Enable 'Replace all occurrences' to update all of them.";
            }

            UpdatePreview();
            OnPropertyChanged(nameof(CanExtract));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading literal info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(ConstantName) || string.IsNullOrEmpty(LiteralValue))
        {
            Preview = "";
            return;
        }

        var type = string.IsNullOrEmpty(ConstantType) ? InferredType : ConstantType;
        var accessMod = CreateAsShared ? SelectedAccessibility : "";
        var sharedMod = CreateAsShared ? "" : "Local ";

        var declaration = CreateAsShared
            ? $"{SelectedAccessibility} Const {ConstantName} As {type} = {LiteralValue}"
            : $"Const {ConstantName} As {type} = {LiteralValue}";

        var replaceText = ReplaceAllOccurrences && OccurrenceCount > 1
            ? $"all {OccurrenceCount} occurrences"
            : "selected occurrence";

        Preview = $"{sharedMod}constant declaration:\n{declaration}\n\nReplaces {replaceText} with '{ConstantName}'";
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (!CanExtract)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var accessibility = SelectedAccessibility switch
            {
                "Public" => ConstantAccessibility.Public,
                "Private" => ConstantAccessibility.Private,
                "Protected" => ConstantAccessibility.Protected,
                "Friend" => ConstantAccessibility.Friend,
                _ => ConstantAccessibility.Private
            };

            var options = new ExtractConstantOptions
            {
                ConstantName = ConstantName,
                ConstantType = string.IsNullOrEmpty(ConstantType) ? null : ConstantType,
                Accessibility = accessibility,
                ReplaceAllOccurrences = ReplaceAllOccurrences,
                CreateAsShared = CreateAsShared
            };

            var result = await _refactoringService.ExtractConstantAsync(
                _filePath, _startLine, _startColumn, _endLine, _endColumn, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to extract constant.";
                return;
            }

            // Apply the edits
            if (result.FileEdit != null)
            {
                var content = await _fileService.ReadFileAsync(_filePath);
                var lines = content.Split('\n').ToList();

                foreach (var edit in result.FileEdit.Edits)
                {
                    lines = ApplyEdit(lines, edit);
                }

                await _fileService.WriteFileAsync(_filePath, string.Join("\n", lines));
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error extracting constant: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private List<string> ApplyEdit(List<string> lines, TextEdit edit)
    {
        if (edit.StartLine == edit.EndLine)
        {
            var lineIndex = edit.StartLine - 1;
            if (lineIndex >= 0 && lineIndex < lines.Count)
            {
                var line = lines[lineIndex];
                var startCol = Math.Min(edit.StartColumn - 1, line.Length);
                var endCol = Math.Min(edit.EndColumn - 1, line.Length);

                if (startCol <= endCol)
                {
                    lines[lineIndex] = line.Substring(0, startCol) + edit.NewText + line.Substring(endCol);
                }
            }
            else if (edit.StartColumn == 1 && edit.EndColumn == 1)
            {
                // Insert new line
                lines.Insert(Math.Max(0, edit.StartLine - 1), edit.NewText.TrimEnd('\n', '\r'));
            }
        }
        return lines;
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeAction?.Invoke(false);
    }
}

public class OccurrenceViewModel
{
    public int Line { get; }
    public int Column { get; }
    public string DisplayText { get; }

    public OccurrenceViewModel(int line, int column, string displayText)
    {
        Line = line;
        Column = column;
        DisplayText = displayText;
    }
}
