using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class MakeParameterRequiredDialogViewModel : ObservableObject
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
    private MakeRequiredParameterViewModel? _selectedParameter;

    [ObservableProperty]
    private string _callSiteValue = "";

    [ObservableProperty]
    private int _callSiteCount;

    [ObservableProperty]
    private int _callSitesNeedingUpdate;

    public ObservableCollection<MakeRequiredParameterViewModel> Parameters { get; } = new();

    public bool CanMakeRequired => SelectedParameter != null &&
                                    !string.IsNullOrWhiteSpace(CallSiteValue) &&
                                    string.IsNullOrEmpty(ErrorMessage);

    public MakeParameterRequiredDialogViewModel(
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

    partial void OnSelectedParameterChanged(MakeRequiredParameterViewModel? value)
    {
        if (value != null)
        {
            // Use the current default value as the suggested call site value
            CallSiteValue = value.DefaultValue ?? "";
        }
        else
        {
            CallSiteValue = "";
        }
        ValidateAndUpdatePreview();
    }

    partial void OnCallSiteValueChanged(string value)
    {
        ValidateAndUpdatePreview();
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

            // Only show optional parameters that can be made required
            for (int i = 0; i < methodInfo.ExistingParameters.Count; i++)
            {
                var param = methodInfo.ExistingParameters[i];
                if (param.IsOptional)
                {
                    var vm = new MakeRequiredParameterViewModel(
                        i,
                        param.Name,
                        param.Type ?? "Object",
                        param.DefaultValue);
                    Parameters.Add(vm);
                }
            }

            if (Parameters.Count == 0)
            {
                ErrorMessage = "No optional parameters to make required.";
            }
            else
            {
                // Select the first optional parameter
                SelectedParameter = Parameters[0];
            }

            // Estimate how many call sites might need updating
            // This is a rough estimate - call sites that omit optional arguments
            CallSitesNeedingUpdate = CallSiteCount;
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

    private void ValidateAndUpdatePreview()
    {
        ErrorMessage = null;

        if (SelectedParameter == null)
        {
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(CanMakeRequired));
            return;
        }

        if (string.IsNullOrWhiteSpace(CallSiteValue))
        {
            ErrorMessage = "A value is required to insert at call sites that omit this argument.";
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(CanMakeRequired));
            return;
        }

        // Generate preview
        UpdatePreview();
        OnPropertyChanged(nameof(CanMakeRequired));
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(CurrentSignature) || SelectedParameter == null)
        {
            Preview = "";
            return;
        }

        // Generate preview by making the parameter required
        var signatureParts = ParseSignature(CurrentSignature);
        if (signatureParts == null)
        {
            Preview = CurrentSignature;
            return;
        }

        var (prefix, parameters, suffix) = signatureParts.Value;

        // Update the selected parameter to be required
        if (SelectedParameter.Index < parameters.Count)
        {
            var oldParam = parameters[SelectedParameter.Index];
            var newParam = MakeParameterRequired(oldParam);
            parameters[SelectedParameter.Index] = newParam;
        }

        Preview = $"{prefix}({string.Join(", ", parameters)}){suffix}";
    }

    private string MakeParameterRequired(string paramDecl)
    {
        // Remove Optional keyword and default value
        var trimmed = paramDecl.Trim();

        // Remove "Optional " prefix
        if (trimmed.StartsWith("Optional ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring("Optional ".Length);
        }

        // Remove " = defaultValue" suffix
        var equalsIndex = trimmed.LastIndexOf('=');
        if (equalsIndex > 0)
        {
            // Make sure we're not inside a type like "Dictionary(Of String, Integer)"
            var parenDepth = 0;
            for (int i = equalsIndex - 1; i >= 0; i--)
            {
                if (trimmed[i] == ')') parenDepth++;
                else if (trimmed[i] == '(') parenDepth--;
            }

            if (parenDepth == 0)
            {
                trimmed = trimmed.Substring(0, equalsIndex).TrimEnd();
            }
        }

        return trimmed;
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
    private async Task MakeRequiredAsync()
    {
        if (SelectedParameter == null || string.IsNullOrWhiteSpace(CallSiteValue))
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new MakeParameterRequiredOptions
            {
                ParameterIndex = SelectedParameter.Index,
                CallSiteValue = CallSiteValue
            };

            var result = await _refactoringService.MakeParameterRequiredAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to make parameter required.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error making parameter required: {ex.Message}";
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

public partial class MakeRequiredParameterViewModel : ObservableObject
{
    public int Index { get; }
    public string Name { get; }
    public string Type { get; }
    public string? DefaultValue { get; }

    public string DisplayText
    {
        get
        {
            var text = $"[{Index}] {Name} As {Type}";
            if (!string.IsNullOrEmpty(DefaultValue))
            {
                text += $" = {DefaultValue}";
            }
            return text;
        }
    }

    public MakeRequiredParameterViewModel(int index, string name, string type, string? defaultValue)
    {
        Index = index;
        Name = name;
        Type = type;
        DefaultValue = defaultValue;
    }
}
