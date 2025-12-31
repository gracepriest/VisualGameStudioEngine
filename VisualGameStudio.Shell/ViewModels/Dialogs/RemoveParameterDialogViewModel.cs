using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class RemoveParameterDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _methodName = "";

    [ObservableProperty]
    private string _containingType = "";

    [ObservableProperty]
    private string _currentSignature = "";

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _callSiteCount;

    [ObservableProperty]
    private bool _updateCallSites = true;

    public ObservableCollection<RemovableParameterViewModel> Parameters { get; } = new();

    public bool HasParametersSelected => Parameters.Any(p => p.IsSelected);

    public RemoveParameterDialogViewModel(
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

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var methodInfo = await _refactoringService.GetMethodForParameterAsync(
                _filePath, _line, _column);

            if (methodInfo == null)
            {
                ErrorMessage = "Could not find method at cursor position.";
                return;
            }

            MethodName = methodInfo.MethodName;
            ContainingType = methodInfo.ContainingType;
            CurrentSignature = methodInfo.Signature;
            CallSiteCount = methodInfo.CallSiteCount;

            Parameters.Clear();
            for (int i = 0; i < methodInfo.ExistingParameters.Count; i++)
            {
                var param = methodInfo.ExistingParameters[i];
                var vm = new RemovableParameterViewModel(i, param.Name, param.Type ?? "Object", param.IsOptional)
                {
                    DefaultValue = param.DefaultValue
                };
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(RemovableParameterViewModel.IsSelected))
                    {
                        UpdatePreview();
                        OnPropertyChanged(nameof(HasParametersSelected));
                    }
                };
                Parameters.Add(vm);
            }

            if (Parameters.Count == 0)
            {
                ErrorMessage = "This method has no parameters to remove.";
            }

            UpdatePreview();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading method info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(CurrentSignature))
        {
            Preview = "";
            return;
        }

        var selectedIndices = Parameters
            .Where(p => p.IsSelected)
            .Select(p => p.Index)
            .ToList();

        if (selectedIndices.Count == 0)
        {
            Preview = CurrentSignature;
            return;
        }

        // Generate preview by removing selected parameters
        var signatureParts = ParseSignature(CurrentSignature);
        if (signatureParts == null)
        {
            Preview = CurrentSignature;
            return;
        }

        var (prefix, parameters, suffix) = signatureParts.Value;

        var remainingParams = parameters
            .Select((p, i) => (param: p, index: i))
            .Where(x => !selectedIndices.Contains(x.index))
            .Select(x => x.param)
            .ToList();

        Preview = $"{prefix}({string.Join(", ", remainingParams)}){suffix}";

        // Validate
        ErrorMessage = null;
        if (selectedIndices.Count == Parameters.Count)
        {
            // Removing all parameters is valid, just a warning
        }
    }

    private (string prefix, List<string> parameters, string suffix)? ParseSignature(string signature)
    {
        var openParen = signature.IndexOf('(');
        var closeParen = signature.LastIndexOf(')');

        if (openParen < 0 || closeParen < 0 || closeParen <= openParen)
            return null;

        var prefix = signature.Substring(0, openParen);
        var suffix = closeParen < signature.Length - 1 ? signature.Substring(closeParen + 1) : "";
        var paramsSection = signature.Substring(openParen + 1, closeParen - openParen - 1);

        var parameters = SplitParameters(paramsSection);
        return (prefix, parameters, suffix);
    }

    private List<string> SplitParameters(string paramsSection)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(paramsSection))
            return result;

        var current = "";
        var parenDepth = 0;

        foreach (var ch in paramsSection)
        {
            if (ch == '(') parenDepth++;
            else if (ch == ')') parenDepth--;
            else if (ch == ',' && parenDepth == 0)
            {
                result.Add(current.Trim());
                current = "";
                continue;
            }
            current += ch;
        }

        if (!string.IsNullOrWhiteSpace(current))
            result.Add(current.Trim());

        return result;
    }

    [RelayCommand]
    private async Task RemoveParametersAsync()
    {
        var selectedIndices = Parameters
            .Where(p => p.IsSelected)
            .Select(p => p.Index)
            .ToList();

        if (selectedIndices.Count == 0)
        {
            ErrorMessage = "Please select at least one parameter to remove.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new RemoveParameterOptions
            {
                ParameterIndices = selectedIndices,
                UpdateCallSites = UpdateCallSites
            };

            var result = await _refactoringService.RemoveParameterAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to remove parameters.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error removing parameters: {ex.Message}";
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

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var param in Parameters)
        {
            param.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var param in Parameters)
        {
            param.IsSelected = false;
        }
    }
}

public partial class RemovableParameterViewModel : ObservableObject
{
    public int Index { get; }
    public string Name { get; }
    public string Type { get; }
    public bool HasDefaultValue { get; }
    public string? DefaultValue { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayText
    {
        get
        {
            var text = $"{Name} As {Type}";
            if (HasDefaultValue && !string.IsNullOrEmpty(DefaultValue))
            {
                text += $" = {DefaultValue}";
            }
            return text;
        }
    }

    public RemovableParameterViewModel(int index, string name, string type, bool hasDefaultValue)
    {
        Index = index;
        Name = name;
        Type = type;
        HasDefaultValue = hasDefaultValue;
    }
}
