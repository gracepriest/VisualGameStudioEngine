using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class InlineConstantDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _constantName = "";

    [ObservableProperty]
    private string _constantValue = "";

    [ObservableProperty]
    private string? _constantType;

    [ObservableProperty]
    private string _accessibility = "";

    [ObservableProperty]
    private bool _isShared;

    [ObservableProperty]
    private string? _containingType;

    [ObservableProperty]
    private int _referenceCount;

    [ObservableProperty]
    private bool _removeDeclaration = true;

    [ObservableProperty]
    private bool _inlineAllReferences = true;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<ConstantReferenceViewModel> References { get; } = new();

    public bool CanInline => !IsLoading &&
                              string.IsNullOrEmpty(ErrorMessage) &&
                              ReferenceCount > 0 &&
                              !string.IsNullOrEmpty(ConstantValue);

    public InlineConstantDialogViewModel(
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

    partial void OnInlineAllReferencesChanged(bool value)
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
            var constantInfo = await _refactoringService.GetConstantInfoAsync(
                _filePath, _line, _column);

            if (constantInfo == null)
            {
                ErrorMessage = "Could not find constant at cursor position.";
                return;
            }

            ConstantName = constantInfo.Name;
            ConstantValue = constantInfo.Value;
            ConstantType = constantInfo.Type;
            Accessibility = constantInfo.Accessibility;
            IsShared = constantInfo.IsShared;
            ContainingType = constantInfo.ContainingType;
            ReferenceCount = constantInfo.ReferenceCount;

            // Check for issues
            if (string.IsNullOrEmpty(constantInfo.Value))
            {
                ErrorMessage = "Constant has no value to inline.";
                return;
            }

            if (constantInfo.ReferenceCount == 0)
            {
                WarningMessage = "Constant has no references. Consider removing it instead.";
            }

            // Populate references
            References.Clear();
            foreach (var reference in constantInfo.References)
            {
                References.Add(new ConstantReferenceViewModel(
                    reference.Line,
                    reference.Column,
                    reference.FilePath,
                    $"Line {reference.Line}, Column {reference.Column}"));
            }

            UpdatePreview();
            OnPropertyChanged(nameof(CanInline));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading constant info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(ConstantValue) || ReferenceCount == 0)
        {
            Preview = "";
            return;
        }

        var refText = InlineAllReferences
            ? (ReferenceCount == 1 ? "1 reference" : $"all {ReferenceCount} references")
            : "selected reference only";

        var declText = RemoveDeclaration && InlineAllReferences
            ? " and remove declaration"
            : "";

        Preview = $"Replace {refText} of '{ConstantName}' with '{ConstantValue}'{declText}";
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
            var options = new InlineConstantOptions
            {
                RemoveDeclaration = RemoveDeclaration,
                InlineAllReferences = InlineAllReferences
            };

            var result = await _refactoringService.InlineConstantAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to inline constant.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error inlining constant: {ex.Message}";
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

public class ConstantReferenceViewModel
{
    public int Line { get; }
    public int Column { get; }
    public string FilePath { get; }
    public string DisplayText { get; }

    public ConstantReferenceViewModel(int line, int column, string filePath, string displayText)
    {
        Line = line;
        Column = column;
        FilePath = filePath;
        DisplayText = displayText;
    }
}
