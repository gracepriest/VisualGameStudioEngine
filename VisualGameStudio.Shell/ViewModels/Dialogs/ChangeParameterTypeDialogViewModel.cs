using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ChangeParameterTypeDialogViewModel : ObservableObject
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
    private ChangeTypeParameterViewModel? _selectedParameter;

    [ObservableProperty]
    private string _newType = "";

    public ObservableCollection<ChangeTypeParameterViewModel> Parameters { get; } = new();

    public ObservableCollection<string> CommonTypes { get; } = new()
    {
        "Integer",
        "Long",
        "Single",
        "Double",
        "Decimal",
        "String",
        "Boolean",
        "Char",
        "Byte",
        "Short",
        "Object",
        "Date",
        "TimeSpan",
        "Guid",
        "Integer()",
        "String()",
        "Object()",
        "List(Of Integer)",
        "List(Of String)",
        "List(Of Object)",
        "Dictionary(Of String, Object)"
    };

    public bool CanChangeType => SelectedParameter != null &&
                                  !string.IsNullOrWhiteSpace(NewType) &&
                                  string.IsNullOrEmpty(ErrorMessage) &&
                                  !NewType.Equals(SelectedParameter?.Type, StringComparison.OrdinalIgnoreCase);

    public ChangeParameterTypeDialogViewModel(
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

    partial void OnSelectedParameterChanged(ChangeTypeParameterViewModel? value)
    {
        if (value != null)
        {
            NewType = value.Type;
        }
        else
        {
            NewType = "";
        }
        ValidateAndUpdatePreview();
    }

    partial void OnNewTypeChanged(string value)
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

            Parameters.Clear();

            for (int i = 0; i < methodInfo.ExistingParameters.Count; i++)
            {
                var param = methodInfo.ExistingParameters[i];
                var vm = new ChangeTypeParameterViewModel(i, param.Name, param.Type ?? "Object", param.IsOptional)
                {
                    DefaultValue = param.DefaultValue
                };
                Parameters.Add(vm);
            }

            if (Parameters.Count == 0)
            {
                ErrorMessage = "This method has no parameters.";
            }
            else
            {
                SelectedParameter = Parameters[0];
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
            OnPropertyChanged(nameof(CanChangeType));
            return;
        }

        if (string.IsNullOrWhiteSpace(NewType))
        {
            ErrorMessage = "Type cannot be empty.";
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(CanChangeType));
            return;
        }

        // Generate preview
        UpdatePreview();
        OnPropertyChanged(nameof(CanChangeType));
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(CurrentSignature) || SelectedParameter == null)
        {
            Preview = "";
            return;
        }

        // Generate preview by replacing the parameter type
        var signatureParts = ParseSignature(CurrentSignature);
        if (signatureParts == null)
        {
            Preview = CurrentSignature;
            return;
        }

        var (prefix, parameters, suffix) = signatureParts.Value;

        // Replace the selected parameter's type
        if (SelectedParameter.Index < parameters.Count)
        {
            var oldParam = parameters[SelectedParameter.Index];
            var newParam = ReplaceParameterType(oldParam, NewType);
            parameters[SelectedParameter.Index] = newParam;
        }

        Preview = $"{prefix}({string.Join(", ", parameters)}){suffix}";
    }

    private string ReplaceParameterType(string paramDecl, string newType)
    {
        // Pattern: name As OldType or ByRef name As OldType, etc.
        var pattern = @"(\w+\s+As\s+)\w+(\(.*?\))?";
        return System.Text.RegularExpressions.Regex.Replace(paramDecl, pattern, $"$1{newType}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
    private async Task ChangeTypeAsync()
    {
        if (SelectedParameter == null || string.IsNullOrWhiteSpace(NewType))
        {
            return;
        }

        if (NewType.Equals(SelectedParameter.Type, StringComparison.OrdinalIgnoreCase))
        {
            _closeAction?.Invoke(false);
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new ChangeParameterTypeOptions
            {
                ParameterIndex = SelectedParameter.Index,
                NewType = NewType
            };

            var result = await _refactoringService.ChangeParameterTypeAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to change parameter type.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error changing parameter type: {ex.Message}";
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

public partial class ChangeTypeParameterViewModel : ObservableObject
{
    public int Index { get; }
    public string Name { get; }
    public string Type { get; }
    public bool IsOptional { get; }
    public string? DefaultValue { get; set; }

    public string DisplayText
    {
        get
        {
            var text = $"[{Index}] {Name} As {Type}";
            if (IsOptional && !string.IsNullOrEmpty(DefaultValue))
            {
                text += $" = {DefaultValue}";
            }
            return text;
        }
    }

    public ChangeTypeParameterViewModel(int index, string name, string type, bool isOptional)
    {
        Index = index;
        Name = name;
        Type = type;
        IsOptional = isOptional;
    }
}
