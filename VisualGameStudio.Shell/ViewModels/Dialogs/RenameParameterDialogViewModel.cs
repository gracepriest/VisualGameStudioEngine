using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class RenameParameterDialogViewModel : ObservableObject
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
    private RenameableParameterViewModel? _selectedParameter;

    [ObservableProperty]
    private string _newName = "";

    public ObservableCollection<RenameableParameterViewModel> Parameters { get; } = new();

    public bool CanRename => SelectedParameter != null &&
                             !string.IsNullOrWhiteSpace(NewName) &&
                             string.IsNullOrEmpty(ErrorMessage) &&
                             NewName != SelectedParameter?.Name;

    public RenameParameterDialogViewModel(
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

    partial void OnSelectedParameterChanged(RenameableParameterViewModel? value)
    {
        if (value != null)
        {
            NewName = value.Name;
        }
        else
        {
            NewName = "";
        }
        ValidateAndUpdatePreview();
    }

    partial void OnNewNameChanged(string value)
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
                var vm = new RenameableParameterViewModel(i, param.Name, param.Type ?? "Object", param.IsOptional)
                {
                    DefaultValue = param.DefaultValue
                };
                Parameters.Add(vm);
            }

            if (Parameters.Count == 0)
            {
                ErrorMessage = "This method has no parameters to rename.";
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
            OnPropertyChanged(nameof(CanRename));
            return;
        }

        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Parameter name cannot be empty.";
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(CanRename));
            return;
        }

        // Validate identifier
        if (!IsValidIdentifier(NewName))
        {
            ErrorMessage = $"'{NewName}' is not a valid identifier.";
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(CanRename));
            return;
        }

        // Check for conflicts with other parameters
        foreach (var param in Parameters)
        {
            if (param != SelectedParameter && param.Name.Equals(NewName, StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = $"A parameter named '{NewName}' already exists.";
                Preview = CurrentSignature;
                OnPropertyChanged(nameof(CanRename));
                return;
            }
        }

        // Generate preview
        UpdatePreview();
        OnPropertyChanged(nameof(CanRename));
    }

    private bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // First character must be a letter or underscore
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        // Remaining characters must be letters, digits, or underscores
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }

        // Check for reserved keywords
        var reservedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "If", "Then", "Else", "ElseIf", "End", "For", "Next", "To", "Step",
            "While", "Wend", "Do", "Loop", "Until", "Select", "Case", "Return",
            "Sub", "Function", "Class", "Module", "Namespace", "Property",
            "Dim", "As", "New", "Nothing", "True", "False", "And", "Or", "Not",
            "ByVal", "ByRef", "Optional", "Public", "Private", "Protected",
            "Shared", "Overridable", "Overrides", "Me", "MyBase", "Try", "Catch",
            "Finally", "Throw", "Imports", "Integer", "Long", "Single", "Double",
            "String", "Boolean", "Char", "Object", "Byte", "Short", "Decimal"
        };

        if (reservedKeywords.Contains(name))
        {
            ErrorMessage = $"'{name}' is a reserved keyword.";
            return false;
        }

        return true;
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(CurrentSignature) || SelectedParameter == null)
        {
            Preview = "";
            return;
        }

        // Generate preview by replacing the parameter name
        var signatureParts = ParseSignature(CurrentSignature);
        if (signatureParts == null)
        {
            Preview = CurrentSignature;
            return;
        }

        var (prefix, parameters, suffix) = signatureParts.Value;

        // Replace the selected parameter's name
        if (SelectedParameter.Index < parameters.Count)
        {
            var oldParam = parameters[SelectedParameter.Index];
            var newParam = ReplaceParameterName(oldParam, SelectedParameter.Name, NewName);
            parameters[SelectedParameter.Index] = newParam;
        }

        Preview = $"{prefix}({string.Join(", ", parameters)}){suffix}";
    }

    private string ReplaceParameterName(string paramDecl, string oldName, string newName)
    {
        // Pattern: name As Type or ByRef name As Type, etc.
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(oldName)}\b(?=\s+As\b)";
        return System.Text.RegularExpressions.Regex.Replace(paramDecl, pattern, newName,
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
    private async Task RenameParameterAsync()
    {
        if (SelectedParameter == null || string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        if (NewName == SelectedParameter.Name)
        {
            _closeAction?.Invoke(false);
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = new RenameParameterOptions
            {
                ParameterIndex = SelectedParameter.Index,
                NewName = NewName
            };

            var result = await _refactoringService.RenameParameterAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to rename parameter.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error renaming parameter: {ex.Message}";
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

public partial class RenameableParameterViewModel : ObservableObject
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

    public RenameableParameterViewModel(int index, string name, string type, bool isOptional)
    {
        Index = index;
        Name = name;
        Type = type;
        IsOptional = isOptional;
    }
}
