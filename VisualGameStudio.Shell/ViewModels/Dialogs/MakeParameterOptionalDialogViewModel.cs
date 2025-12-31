using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class MakeParameterOptionalDialogViewModel : ObservableObject
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
    private MakeOptionalParameterViewModel? _selectedParameter;

    [ObservableProperty]
    private string _defaultValue = "";

    [ObservableProperty]
    private bool _removeDefaultArgumentsFromCallSites;

    [ObservableProperty]
    private int _callSiteCount;

    public ObservableCollection<MakeOptionalParameterViewModel> Parameters { get; } = new();

    public ObservableCollection<string> CommonDefaultValues { get; } = new()
    {
        "Nothing",
        "0",
        "1",
        "-1",
        "\"\"",
        "True",
        "False"
    };

    public bool CanMakeOptional => SelectedParameter != null &&
                                    !string.IsNullOrWhiteSpace(DefaultValue) &&
                                    string.IsNullOrEmpty(ErrorMessage);

    public MakeParameterOptionalDialogViewModel(
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

    partial void OnSelectedParameterChanged(MakeOptionalParameterViewModel? value)
    {
        if (value != null)
        {
            // Suggest a default value based on type
            DefaultValue = SuggestDefaultValue(value.Type);
        }
        else
        {
            DefaultValue = "";
        }
        ValidateAndUpdatePreview();
    }

    partial void OnDefaultValueChanged(string value)
    {
        ValidateAndUpdatePreview();
    }

    private string SuggestDefaultValue(string type)
    {
        var typeLower = type.ToLowerInvariant();
        return typeLower switch
        {
            "integer" or "long" or "short" or "byte" => "0",
            "single" or "double" or "decimal" => "0.0",
            "boolean" => "False",
            "string" => "\"\"",
            "char" => "\" \"c",
            "date" => "Nothing",
            _ when typeLower.EndsWith("()") => "Nothing",
            _ when typeLower.StartsWith("list(") => "Nothing",
            _ when typeLower.StartsWith("dictionary(") => "Nothing",
            _ => "Nothing"
        };
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

            // Only show non-optional parameters that can be made optional
            for (int i = 0; i < methodInfo.ExistingParameters.Count; i++)
            {
                var param = methodInfo.ExistingParameters[i];
                if (!param.IsOptional)
                {
                    // Check if all parameters after this one are optional
                    var canBeOptional = methodInfo.ExistingParameters
                        .Skip(i + 1)
                        .All(p => p.IsOptional);

                    var vm = new MakeOptionalParameterViewModel(
                        i,
                        param.Name,
                        param.Type ?? "Object",
                        canBeOptional);
                    Parameters.Add(vm);
                }
            }

            if (Parameters.Count == 0)
            {
                ErrorMessage = "No required parameters to make optional.";
            }
            else
            {
                // Select the last non-optional parameter (most likely candidate)
                SelectedParameter = Parameters.LastOrDefault(p => p.CanBeOptional) ?? Parameters[0];
            }
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
            OnPropertyChanged(nameof(CanMakeOptional));
            return;
        }

        if (!SelectedParameter.CanBeOptional)
        {
            ErrorMessage = "This parameter cannot be made optional because there are required parameters after it. Reorder parameters first.";
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(CanMakeOptional));
            return;
        }

        if (string.IsNullOrWhiteSpace(DefaultValue))
        {
            ErrorMessage = "Default value is required.";
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(CanMakeOptional));
            return;
        }

        // Generate preview
        UpdatePreview();
        OnPropertyChanged(nameof(CanMakeOptional));
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(CurrentSignature) || SelectedParameter == null)
        {
            Preview = "";
            return;
        }

        // Generate preview by making the parameter optional
        var signatureParts = ParseSignature(CurrentSignature);
        if (signatureParts == null)
        {
            Preview = CurrentSignature;
            return;
        }

        var (prefix, parameters, suffix) = signatureParts.Value;

        // Update the selected parameter to be optional
        if (SelectedParameter.Index < parameters.Count)
        {
            var oldParam = parameters[SelectedParameter.Index];
            var newParam = MakeParameterOptional(oldParam, DefaultValue);
            parameters[SelectedParameter.Index] = newParam;
        }

        Preview = $"{prefix}({string.Join(", ", parameters)}){suffix}";
    }

    private string MakeParameterOptional(string paramDecl, string defaultValue)
    {
        // Add Optional keyword at the start and = defaultValue at the end
        // Pattern: [ByRef|ByVal] name As Type
        if (paramDecl.Trim().StartsWith("Optional", StringComparison.OrdinalIgnoreCase))
        {
            return paramDecl; // Already optional
        }

        var trimmed = paramDecl.Trim();
        if (trimmed.StartsWith("ByRef", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ByVal", StringComparison.OrdinalIgnoreCase))
        {
            return $"Optional {trimmed} = {defaultValue}";
        }

        return $"Optional {trimmed} = {defaultValue}";
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
    private async Task MakeOptionalAsync()
    {
        if (SelectedParameter == null || string.IsNullOrWhiteSpace(DefaultValue))
        {
            return;
        }

        if (!SelectedParameter.CanBeOptional)
        {
            ErrorMessage = "This parameter cannot be made optional.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new MakeParameterOptionalOptions
            {
                ParameterIndex = SelectedParameter.Index,
                DefaultValue = DefaultValue,
                RemoveDefaultArgumentsFromCallSites = RemoveDefaultArgumentsFromCallSites
            };

            var result = await _refactoringService.MakeParameterOptionalAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to make parameter optional.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error making parameter optional: {ex.Message}";
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

public partial class MakeOptionalParameterViewModel : ObservableObject
{
    public int Index { get; }
    public string Name { get; }
    public string Type { get; }
    public bool CanBeOptional { get; }

    public string DisplayText
    {
        get
        {
            var text = $"[{Index}] {Name} As {Type}";
            if (!CanBeOptional)
            {
                text += " (reorder first)";
            }
            return text;
        }
    }

    public MakeOptionalParameterViewModel(int index, string name, string type, bool canBeOptional)
    {
        Index = index;
        Name = name;
        Type = type;
        CanBeOptional = canBeOptional;
    }
}
