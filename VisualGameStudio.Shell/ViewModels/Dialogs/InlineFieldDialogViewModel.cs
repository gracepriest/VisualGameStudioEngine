using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class InlineFieldDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _fieldName = "";

    [ObservableProperty]
    private string? _fieldType;

    [ObservableProperty]
    private string _initializerExpression = "";

    [ObservableProperty]
    private string _accessibility = "";

    [ObservableProperty]
    private bool _isShared;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private int _referenceCount;

    [ObservableProperty]
    private bool _removeDeclaration = true;

    [ObservableProperty]
    private bool _addParenthesesIfNeeded = true;

    [ObservableProperty]
    private bool _inlineAcrossFiles = true;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<FieldUsageViewModel> References { get; } = new();

    public bool CanInline => !IsLoading &&
                              string.IsNullOrEmpty(ErrorMessage) &&
                              ReferenceCount > 0 &&
                              !string.IsNullOrEmpty(InitializerExpression);

    public InlineFieldDialogViewModel(
        IRefactoringService refactoringService,
        string filePath,
        int line,
        int column)
    {
        _refactoringService = refactoringService;
        _filePath = filePath;
        _line = line;
        _column = column;
    }

    public void SetCloseAction(Action<bool> closeAction)
    {
        _closeAction = closeAction;
    }

    partial void OnRemoveDeclarationChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnAddParenthesesIfNeededChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnInlineAcrossFilesChanged(bool value)
    {
        UpdatePreview();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        WarningMessage = null;

        try
        {
            var fieldInfo = await _refactoringService.GetFieldInfoAsync(
                _filePath, _line, _column);

            if (fieldInfo == null)
            {
                ErrorMessage = "Could not find field at cursor position.";
                return;
            }

            FieldName = fieldInfo.Name;
            FieldType = fieldInfo.Type;
            InitializerExpression = fieldInfo.InitialValue ?? "";
            Accessibility = fieldInfo.Accessibility.ToString();
            IsShared = fieldInfo.IsShared;
            IsReadOnly = fieldInfo.IsReadOnly;
            ReferenceCount = fieldInfo.ReferenceCount;

            // Check for issues
            if (string.IsNullOrEmpty(fieldInfo.InitialValue))
            {
                ErrorMessage = "Field has no initializer expression to inline.";
                return;
            }

            if (fieldInfo.ReferenceCount == 0)
            {
                WarningMessage = "Field is not used anywhere. Consider removing it instead.";
            }

            // Populate references
            References.Clear();
            foreach (var reference in fieldInfo.References)
            {
                var fileName = Path.GetFileName(reference.FilePath);
                References.Add(new FieldUsageViewModel(
                    reference.Line,
                    reference.Column,
                    reference.FilePath,
                    $"{fileName}:{reference.Line}"));
            }

            UpdatePreview();
            OnPropertyChanged(nameof(CanInline));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading field info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(InitializerExpression) || ReferenceCount == 0)
        {
            Preview = "";
            return;
        }

        var expr = InitializerExpression;
        if (AddParenthesesIfNeeded && ContainsOperators(expr))
        {
            expr = $"({expr})";
        }

        var refText = ReferenceCount == 1 ? "1 reference" : $"{ReferenceCount} references";
        var declText = RemoveDeclaration ? " and remove declaration" : "";
        var filesText = InlineAcrossFiles ? " across all files" : " in current file only";

        Preview = $"Replace {refText} of '{FieldName}' with '{expr}'{filesText}{declText}";
    }

    private bool ContainsOperators(string expression)
    {
        return expression.Contains("+") || expression.Contains("-") ||
               expression.Contains("*") || expression.Contains("/") ||
               expression.Contains("&") || expression.Contains("And") ||
               expression.Contains("Or") || expression.Contains("Mod");
    }

    [RelayCommand]
    private async Task InlineAsync()
    {
        if (!CanInline)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new InlineFieldOptions
            {
                RemoveDeclaration = RemoveDeclaration,
                AddParenthesesIfNeeded = AddParenthesesIfNeeded,
                InlineAcrossFiles = InlineAcrossFiles
            };

            var result = await _refactoringService.InlineFieldAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to inline field.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error inlining field: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeAction?.Invoke(false);
    }
}

public class FieldUsageViewModel
{
    public int Line { get; }
    public int Column { get; }
    public string FilePath { get; }
    public string DisplayText { get; }

    public FieldUsageViewModel(int line, int column, string filePath, string displayText)
    {
        Line = line;
        Column = column;
        FilePath = filePath;
        DisplayText = displayText;
    }
}
