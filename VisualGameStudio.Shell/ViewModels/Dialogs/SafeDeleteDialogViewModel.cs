using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class SafeDeleteDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _symbolName = "";

    [ObservableProperty]
    private string _symbolKind = "";

    [ObservableProperty]
    private string? _symbolType;

    [ObservableProperty]
    private string _accessibility = "";

    [ObservableProperty]
    private string? _containingType;

    [ObservableProperty]
    private string? _containingMethod;

    [ObservableProperty]
    private string _declarationText = "";

    [ObservableProperty]
    private int _usageCount;

    [ObservableProperty]
    private bool _forceDelete;

    [ObservableProperty]
    private bool _commentOutUsages;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canSafelyDelete;

    public ObservableCollection<SymbolUsageViewModel> Usages { get; } = new();

    public bool CanDelete => !IsLoading &&
                              string.IsNullOrEmpty(ErrorMessage) &&
                              !string.IsNullOrEmpty(SymbolName) &&
                              (CanSafelyDelete || ForceDelete);

    public SafeDeleteDialogViewModel(
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

    partial void OnForceDeleteChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDelete));
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        WarningMessage = null;

        try
        {
            var symbolInfo = await _refactoringService.GetDeletableSymbolInfoAsync(
                _filePath, _line, _column);

            if (symbolInfo == null)
            {
                ErrorMessage = "Could not find symbol at cursor position.";
                return;
            }

            SymbolName = symbolInfo.Name;
            SymbolKind = GetSymbolKindDisplayName(symbolInfo.Kind);
            SymbolType = symbolInfo.Type;
            Accessibility = symbolInfo.Accessibility;
            ContainingType = symbolInfo.ContainingType;
            ContainingMethod = symbolInfo.ContainingMethod;
            DeclarationText = symbolInfo.DeclarationText;
            UsageCount = symbolInfo.UsageCount;
            CanSafelyDelete = symbolInfo.CanSafelyDelete;

            // Set warning message based on usages
            if (symbolInfo.UsageCount > 0)
            {
                WarningMessage = symbolInfo.WarningMessage;
            }

            // Populate usages
            Usages.Clear();
            foreach (var usage in symbolInfo.Usages)
            {
                Usages.Add(new SymbolUsageViewModel(
                    usage.Line,
                    usage.Column,
                    usage.FilePath,
                    usage.ContextLine,
                    GetUsageKindDisplayName(usage.Kind),
                    usage.ContainingMethod,
                    usage.ContainingType));
            }

            OnPropertyChanged(nameof(CanDelete));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading symbol info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetSymbolKindDisplayName(DeletableSymbolKind kind)
    {
        return kind switch
        {
            DeletableSymbolKind.LocalVariable => "Local Variable",
            DeletableSymbolKind.Field => "Field",
            DeletableSymbolKind.Constant => "Constant",
            DeletableSymbolKind.Property => "Property",
            DeletableSymbolKind.Sub => "Sub",
            DeletableSymbolKind.Function => "Function",
            DeletableSymbolKind.Class => "Class",
            DeletableSymbolKind.Module => "Module",
            DeletableSymbolKind.Interface => "Interface",
            DeletableSymbolKind.Enum => "Enum",
            DeletableSymbolKind.Structure => "Structure",
            DeletableSymbolKind.Parameter => "Parameter",
            DeletableSymbolKind.Event => "Event",
            DeletableSymbolKind.Delegate => "Delegate",
            _ => kind.ToString()
        };
    }

    private string GetUsageKindDisplayName(SymbolUsageKind kind)
    {
        return kind switch
        {
            SymbolUsageKind.Reference => "Reference",
            SymbolUsageKind.Assignment => "Assignment",
            SymbolUsageKind.Call => "Call",
            SymbolUsageKind.Inheritance => "Inheritance",
            SymbolUsageKind.Implementation => "Implementation",
            SymbolUsageKind.TypeReference => "Type Reference",
            SymbolUsageKind.Parameter => "Parameter",
            _ => kind.ToString()
        };
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanDelete)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new SafeDeleteOptions
            {
                ForceDelete = ForceDelete,
                CommentOutUsages = CommentOutUsages
            };

            var result = await _refactoringService.SafeDeleteAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to delete symbol.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error deleting symbol: {ex.Message}";
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

public class SymbolUsageViewModel
{
    public int Line { get; }
    public int Column { get; }
    public string FilePath { get; }
    public string ContextLine { get; }
    public string UsageKind { get; }
    public string? ContainingMethod { get; }
    public string? ContainingType { get; }
    public string DisplayText { get; }

    public SymbolUsageViewModel(int line, int column, string filePath, string contextLine,
        string usageKind, string? containingMethod, string? containingType)
    {
        Line = line;
        Column = column;
        FilePath = filePath;
        ContextLine = contextLine;
        UsageKind = usageKind;
        ContainingMethod = containingMethod;
        ContainingType = containingType;

        // Build display text
        var location = !string.IsNullOrEmpty(containingMethod)
            ? $"in {containingMethod}()"
            : (!string.IsNullOrEmpty(containingType) ? $"in {containingType}" : "");

        DisplayText = $"Line {line}: {contextLine.Trim()} {location}".Trim();
    }
}
