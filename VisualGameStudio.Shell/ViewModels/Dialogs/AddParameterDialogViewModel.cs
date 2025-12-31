using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class AddParameterDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private AddParameterInfo? _methodInfo;
    private string _filePath = "";
    private int _line;
    private int _column;

    [ObservableProperty]
    private string _methodName = "";

    [ObservableProperty]
    private string _currentSignature = "";

    [ObservableProperty]
    private string? _containingType;

    [ObservableProperty]
    private ObservableCollection<ExistingParameterViewModel> _existingParameters = new();

    [ObservableProperty]
    private string _parameterName = "";

    [ObservableProperty]
    private string _parameterType = "Object";

    [ObservableProperty]
    private bool _isByRef;

    [ObservableProperty]
    private bool _isOptional;

    [ObservableProperty]
    private string _defaultValue = "";

    [ObservableProperty]
    private int _insertPosition = -1;

    [ObservableProperty]
    private string _callSiteValue = "";

    [ObservableProperty]
    private bool _updateCallSites = true;

    [ObservableProperty]
    private int _callSiteCount;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private ObservableCollection<string> _commonTypes = new()
    {
        "Object",
        "String",
        "Integer",
        "Long",
        "Boolean",
        "Double",
        "Single",
        "Date",
        "Byte",
        "Char"
    };

    [ObservableProperty]
    private ObservableCollection<InsertPositionOption> _insertPositions = new();

    public bool DialogResult { get; private set; }
    public AddParameterResult? Result { get; private set; }

    public event EventHandler<AddParameterResult>? AddParameterCompleted;
    public event EventHandler? Cancelled;

    public AddParameterDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public async Task InitializeAsync(string filePath, int line, int column)
    {
        _filePath = filePath;
        _line = line;
        _column = column;

        IsLoading = true;

        try
        {
            _methodInfo = await _refactoringService.GetMethodForParameterAsync(filePath, line, column);

            if (_methodInfo != null)
            {
                MethodName = _methodInfo.MethodName;
                CurrentSignature = _methodInfo.Signature;
                ContainingType = _methodInfo.ContainingType;
                CallSiteCount = _methodInfo.CallSiteCount;

                ExistingParameters.Clear();
                foreach (var param in _methodInfo.ExistingParameters)
                {
                    ExistingParameters.Add(new ExistingParameterViewModel
                    {
                        Name = param.Name,
                        Type = param.Type,
                        IsByRef = param.IsByRef,
                        IsOptional = param.IsOptional,
                        DefaultValue = param.DefaultValue,
                        Index = param.Index
                    });
                }

                // Build insert position options
                InsertPositions.Clear();
                InsertPositions.Add(new InsertPositionOption { Position = -1, DisplayText = "At end" });
                for (var i = 0; i <= _methodInfo.ExistingParameters.Count; i++)
                {
                    if (i == 0)
                    {
                        InsertPositions.Add(new InsertPositionOption { Position = 0, DisplayText = "At beginning" });
                    }
                    else if (i < _methodInfo.ExistingParameters.Count)
                    {
                        InsertPositions.Add(new InsertPositionOption
                        {
                            Position = i,
                            DisplayText = $"After '{_methodInfo.ExistingParameters[i - 1].Name}'"
                        });
                    }
                }

                InsertPosition = -1; // Default to end

                UpdatePreview();
                ValidateInput();
            }
            else
            {
                ErrorMessage = "Could not find method definition. Place cursor on a Sub or Function.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnParameterNameChanged(string value)
    {
        UpdatePreview();
        ValidateInput();
    }

    partial void OnParameterTypeChanged(string value)
    {
        UpdatePreview();
        UpdateDefaultCallSiteValue();
    }

    partial void OnIsByRefChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnIsOptionalChanged(bool value)
    {
        UpdatePreview();
        ValidateInput();
    }

    partial void OnDefaultValueChanged(string value)
    {
        UpdatePreview();
        ValidateInput();
    }

    partial void OnInsertPositionChanged(int value)
    {
        UpdatePreview();
    }

    partial void OnCallSiteValueChanged(string value)
    {
        UpdatePreview();
    }

    private void UpdateDefaultCallSiteValue()
    {
        if (string.IsNullOrEmpty(CallSiteValue))
        {
            CallSiteValue = GetDefaultValueForType(ParameterType);
        }
    }

    private string GetDefaultValueForType(string typeName)
    {
        return typeName.ToLower() switch
        {
            "integer" or "int" or "long" or "short" or "byte" => "0",
            "single" or "double" or "decimal" => "0.0",
            "boolean" or "bool" => "False",
            "string" => "\"\"",
            "char" => "\"\"c",
            "date" or "datetime" => "Nothing",
            _ => "Nothing"
        };
    }

    private void ValidateInput()
    {
        if (_methodInfo == null)
        {
            ErrorMessage = "No method information available";
            return;
        }

        if (string.IsNullOrWhiteSpace(ParameterName))
        {
            ErrorMessage = "Parameter name is required";
            return;
        }

        // Check for valid identifier
        if (!System.Text.RegularExpressions.Regex.IsMatch(ParameterName, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            ErrorMessage = "Parameter name must be a valid identifier";
            return;
        }

        // Check for duplicate name
        if (_methodInfo.ExistingParameters.Any(p => p.Name.Equals(ParameterName, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"A parameter named '{ParameterName}' already exists";
            return;
        }

        // Check for reserved keywords
        var reserved = new[] { "Sub", "Function", "End", "If", "Then", "Else", "For", "Next", "While", "Do", "Loop", "Dim", "As", "ByVal", "ByRef", "Optional" };
        if (reserved.Any(r => r.Equals(ParameterName, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"'{ParameterName}' is a reserved keyword";
            return;
        }

        // Validate optional parameter requirements
        if (IsOptional && string.IsNullOrWhiteSpace(DefaultValue))
        {
            ErrorMessage = "Optional parameters require a default value";
            return;
        }

        ErrorMessage = "";
    }

    private void UpdatePreview()
    {
        if (_methodInfo == null || string.IsNullOrWhiteSpace(ParameterName))
        {
            Preview = "";
            return;
        }

        var sb = new System.Text.StringBuilder();

        // Build new parameter
        var newParam = BuildNewParameterString();

        // Build new parameter list
        var newParamList = new List<string>();
        foreach (var p in _methodInfo.ExistingParameters)
        {
            newParamList.Add(FormatParameter(p));
        }

        // Insert at correct position
        var actualPos = InsertPosition >= 0 ? Math.Min(InsertPosition, newParamList.Count) : newParamList.Count;
        newParamList.Insert(actualPos, newParam);

        // Build preview signature
        if (_methodInfo.IsFunction)
        {
            sb.Append($"Function {_methodInfo.MethodName}(");
            sb.Append(string.Join(", ", newParamList));
            sb.Append($") As {_methodInfo.ReturnType ?? "Object"}");
        }
        else
        {
            sb.Append($"Sub {_methodInfo.MethodName}(");
            sb.Append(string.Join(", ", newParamList));
            sb.Append(")");
        }

        sb.AppendLine();
        sb.AppendLine();

        // Show call site update preview if applicable
        if (UpdateCallSites && CallSiteCount > 0)
        {
            sb.AppendLine($"' {CallSiteCount} call site(s) will be updated with:");
            sb.AppendLine($"' New argument: {(string.IsNullOrEmpty(CallSiteValue) ? GetDefaultValueForType(ParameterType) : CallSiteValue)}");
        }
        else if (CallSiteCount > 0)
        {
            sb.AppendLine($"' Warning: {CallSiteCount} call site(s) will NOT be updated");
        }

        Preview = sb.ToString();
    }

    private string BuildNewParameterString()
    {
        var sb = new System.Text.StringBuilder();

        if (IsOptional)
            sb.Append("Optional ");

        if (IsByRef)
            sb.Append("ByRef ");

        sb.Append(ParameterName);
        sb.Append(" As ");
        sb.Append(ParameterType);

        if (!string.IsNullOrWhiteSpace(DefaultValue))
        {
            sb.Append(" = ");
            sb.Append(DefaultValue);
        }

        return sb.ToString();
    }

    private string FormatParameter(ExistingParameterInfo param)
    {
        var sb = new System.Text.StringBuilder();

        if (param.IsOptional)
            sb.Append("Optional ");

        if (param.IsByRef)
            sb.Append("ByRef ");

        sb.Append(param.Name);
        sb.Append(" As ");
        sb.Append(param.Type ?? "Object");

        if (param.DefaultValue != null)
        {
            sb.Append(" = ");
            sb.Append(param.DefaultValue);
        }

        return sb.ToString();
    }

    [RelayCommand]
    private async Task AddParameterAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage) || _methodInfo == null)
            return;

        IsLoading = true;

        try
        {
            var options = new AddParameterOptions
            {
                ParameterName = ParameterName,
                ParameterType = ParameterType,
                IsByRef = IsByRef,
                IsOptional = IsOptional,
                DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue,
                InsertPosition = InsertPosition,
                CallSiteValue = string.IsNullOrWhiteSpace(CallSiteValue) ? null : CallSiteValue,
                UpdateCallSites = UpdateCallSites
            };

            Result = await _refactoringService.AddParameterAsync(_filePath, _line, _column, options);

            if (Result.Success)
            {
                DialogResult = true;
                AddParameterCompleted?.Invoke(this, Result);
            }
            else
            {
                ErrorMessage = Result.ErrorMessage ?? "Unknown error occurred";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}

public partial class ExistingParameterViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string? _type;

    [ObservableProperty]
    private bool _isByRef;

    [ObservableProperty]
    private bool _isOptional;

    [ObservableProperty]
    private string? _defaultValue;

    [ObservableProperty]
    private int _index;

    public string DisplayText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            if (IsOptional) sb.Append("Optional ");
            if (IsByRef) sb.Append("ByRef ");
            sb.Append(Name);
            sb.Append(" As ");
            sb.Append(Type ?? "Object");
            if (DefaultValue != null)
            {
                sb.Append(" = ");
                sb.Append(DefaultValue);
            }
            return sb.ToString();
        }
    }
}

public class InsertPositionOption
{
    public int Position { get; set; }
    public string DisplayText { get; set; } = "";
}
