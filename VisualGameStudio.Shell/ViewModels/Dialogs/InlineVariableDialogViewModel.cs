using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class InlineVariableDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _variableName = "";

    [ObservableProperty]
    private string? _variableType;

    [ObservableProperty]
    private string _initializerExpression = "";

    [ObservableProperty]
    private string _declarationText = "";

    [ObservableProperty]
    private int _usageCount;

    [ObservableProperty]
    private string? _containingMethod;

    [ObservableProperty]
    private bool _removeDeclaration = true;

    [ObservableProperty]
    private bool _addParenthesesIfNeeded = true;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<UsageViewModel> Usages { get; } = new();

    public bool CanInline => !IsLoading &&
                              string.IsNullOrEmpty(ErrorMessage) &&
                              UsageCount > 0 &&
                              !string.IsNullOrEmpty(InitializerExpression);

    public InlineVariableDialogViewModel(
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

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        WarningMessage = null;

        try
        {
            var variableInfo = await _refactoringService.GetVariableInfoAsync(
                _filePath, _line, _column);

            if (variableInfo == null)
            {
                ErrorMessage = "Could not find variable at cursor position.";
                return;
            }

            VariableName = variableInfo.Name;
            VariableType = variableInfo.Type;
            InitializerExpression = variableInfo.InitializerExpression;
            DeclarationText = variableInfo.DeclarationText;
            UsageCount = variableInfo.UsageCount;
            ContainingMethod = variableInfo.ContainingMethod;

            // Check for issues
            if (variableInfo.IsParameter)
            {
                ErrorMessage = "Cannot inline parameters.";
                return;
            }

            if (variableInfo.IsField)
            {
                ErrorMessage = "Cannot inline fields. Use 'Inline Field' refactoring instead.";
                return;
            }

            if (!variableInfo.HasInitializer)
            {
                ErrorMessage = "Variable has no initializer expression to inline.";
                return;
            }

            if (variableInfo.IsReassigned)
            {
                ErrorMessage = "Cannot inline variable that is reassigned after declaration.";
                return;
            }

            if (variableInfo.UsageCount == 0)
            {
                WarningMessage = "Variable is not used anywhere. Consider removing it instead.";
            }

            // Populate usages
            Usages.Clear();
            foreach (var usage in variableInfo.Usages)
            {
                Usages.Add(new UsageViewModel(usage.Line, usage.Column, $"Line {usage.Line}, Column {usage.Column}"));
            }

            UpdatePreview();
            OnPropertyChanged(nameof(CanInline));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading variable info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(InitializerExpression) || UsageCount == 0)
        {
            Preview = "";
            return;
        }

        var expr = InitializerExpression;
        if (AddParenthesesIfNeeded && ContainsOperators(expr))
        {
            expr = $"({expr})";
        }

        var usageText = UsageCount == 1 ? "1 usage" : $"{UsageCount} usages";
        var declText = RemoveDeclaration ? " and remove declaration" : "";

        Preview = $"Replace {usageText} of '{VariableName}' with '{expr}'{declText}";
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
            var options = new InlineVariableOptions
            {
                RemoveDeclaration = RemoveDeclaration,
                AddParenthesesIfNeeded = AddParenthesesIfNeeded
            };

            var result = await _refactoringService.InlineVariableAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to inline variable.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error inlining variable: {ex.Message}";
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

public class UsageViewModel
{
    public int Line { get; }
    public int Column { get; }
    public string DisplayText { get; }

    public UsageViewModel(int line, int column, string displayText)
    {
        Line = line;
        Column = column;
        DisplayText = displayText;
    }
}
