using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class UseBaseTypeDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _symbolName = "";

    [ObservableProperty]
    private string _currentType = "";

    [ObservableProperty]
    private string _symbolKind = "";

    [ObservableProperty]
    private string _declaration = "";

    [ObservableProperty]
    private BaseTypeCandidateViewModel? _selectedBaseType;

    [ObservableProperty]
    private bool _updateAllOccurrences = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _preview = "";

    public ObservableCollection<BaseTypeCandidateViewModel> BaseTypes { get; } = new();

    public bool CanApply => !IsLoading &&
                            string.IsNullOrEmpty(ErrorMessage) &&
                            SelectedBaseType != null;

    public UseBaseTypeDialogViewModel(
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

    partial void OnSelectedBaseTypeChanged(BaseTypeCandidateViewModel? value)
    {
        OnPropertyChanged(nameof(CanApply));
        UpdatePreview();
    }

    partial void OnUpdateAllOccurrencesChanged(bool value)
    {
        UpdatePreview();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var info = await _refactoringService.GetUseBaseTypeInfoAsync(
                _filePath, _line, _column);

            if (info == null)
            {
                ErrorMessage = "Could not find a type declaration at cursor position.";
                return;
            }

            SymbolName = info.SymbolName;
            CurrentType = info.CurrentType;
            SymbolKind = info.SymbolKind.ToString();
            Declaration = info.Declaration;

            // Populate base types
            BaseTypes.Clear();
            foreach (var baseType in info.BaseTypes)
            {
                BaseTypes.Add(new BaseTypeCandidateViewModel(baseType));
            }

            if (BaseTypes.Count == 0)
            {
                ErrorMessage = "No base types found for this type.";
                return;
            }

            // Select first base type by default
            SelectedBaseType = BaseTypes[0];

            UpdatePreview();
            OnPropertyChanged(nameof(CanApply));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading type info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (SelectedBaseType == null)
        {
            Preview = "";
            return;
        }

        var scope = UpdateAllOccurrences ? "all occurrences" : "this declaration only";
        Preview = $"Change '{CurrentType}' to '{SelectedBaseType.TypeName}' ({scope})";
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!CanApply || SelectedBaseType == null)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new UseBaseTypeOptions
            {
                NewTypeName = SelectedBaseType.TypeName,
                UpdateAllOccurrences = UpdateAllOccurrences
            };

            var result = await _refactoringService.UseBaseTypeAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to change type.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error changing type: {ex.Message}";
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

public partial class BaseTypeCandidateViewModel : ObservableObject
{
    public string TypeName { get; }
    public bool IsInterface { get; }
    public bool IsBaseClass { get; }
    public string Description { get; }
    public string DisplayText { get; }
    public string TypeKind { get; }

    public BaseTypeCandidateViewModel(BaseTypeCandidate candidate)
    {
        TypeName = candidate.TypeName;
        IsInterface = candidate.IsInterface;
        IsBaseClass = candidate.IsBaseClass;
        Description = candidate.Description;
        TypeKind = IsInterface ? "Interface" : "Class";
        DisplayText = $"{TypeName} ({Description})";
    }
}
