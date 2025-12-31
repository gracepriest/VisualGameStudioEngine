using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ChangeSignatureDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private string _methodName = "";

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private int _column;

    [ObservableProperty]
    private bool _isFunction;

    [ObservableProperty]
    private string? _returnType;

    [ObservableProperty]
    private string? _newReturnType;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _previewSignature = "";

    [ObservableProperty]
    private int _callSiteCount;

    [ObservableProperty]
    private ParameterViewModel? _selectedParameter;

    public ObservableCollection<ParameterViewModel> Parameters { get; } = new();

    public string[] CommonTypes { get; } = { "Integer", "Long", "Single", "Double", "String", "Boolean", "Object", "Char", "Byte" };

    public event EventHandler<ChangeSignatureResult>? ChangeCompleted;
    public event EventHandler? Cancelled;

    public ChangeSignatureDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public async Task InitializeAsync(string filePath, int line, int column, string methodName)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        MethodName = methodName;
        IsLoading = true;

        try
        {
            var signatureInfo = await _refactoringService.GetSignatureInfoAsync(filePath, line, column);
            if (signatureInfo == null)
            {
                ErrorMessage = "Could not find method signature";
                return;
            }

            MethodName = signatureInfo.Name;
            IsFunction = signatureInfo.IsFunction;
            ReturnType = signatureInfo.ReturnType;
            NewReturnType = signatureInfo.ReturnType;
            CallSiteCount = signatureInfo.CallSiteCount;

            Parameters.Clear();
            foreach (var paramInfo in signatureInfo.Parameters)
            {
                var paramVm = new ParameterViewModel
                {
                    Name = paramInfo.Name,
                    Type = paramInfo.Type,
                    IsByRef = paramInfo.IsByRef,
                    IsOptional = paramInfo.IsOptional,
                    DefaultValue = paramInfo.DefaultValue,
                    OriginalIndex = paramInfo.OriginalIndex,
                    IsNew = false
                };
                paramVm.PropertyChanged += (s, e) => UpdatePreview();
                Parameters.Add(paramVm);
            }

            UpdatePreview();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnNewReturnTypeChanged(string? value)
    {
        UpdatePreview();
    }

    [RelayCommand]
    private void AddParameter()
    {
        var newParam = new ParameterViewModel
        {
            Name = $"param{Parameters.Count + 1}",
            Type = "Object",
            IsNew = true,
            OriginalIndex = -1
        };
        newParam.PropertyChanged += (s, e) => UpdatePreview();
        Parameters.Add(newParam);
        SelectedParameter = newParam;
        UpdatePreview();
    }

    [RelayCommand]
    private void RemoveParameter()
    {
        if (SelectedParameter != null)
        {
            var index = Parameters.IndexOf(SelectedParameter);
            Parameters.Remove(SelectedParameter);

            if (Parameters.Count > 0)
            {
                SelectedParameter = Parameters[Math.Min(index, Parameters.Count - 1)];
            }
            else
            {
                SelectedParameter = null;
            }

            UpdatePreview();
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedParameter != null)
        {
            var index = Parameters.IndexOf(SelectedParameter);
            if (index > 0)
            {
                Parameters.Move(index, index - 1);
                UpdatePreview();
            }
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedParameter != null)
        {
            var index = Parameters.IndexOf(SelectedParameter);
            if (index < Parameters.Count - 1)
            {
                Parameters.Move(index, index + 1);
                UpdatePreview();
            }
        }
    }

    private void UpdatePreview()
    {
        var paramStrings = new List<string>();

        foreach (var param in Parameters)
        {
            var sb = new System.Text.StringBuilder();

            if (param.IsByRef)
                sb.Append("ByRef ");

            if (param.IsOptional)
                sb.Append("Optional ");

            sb.Append(param.Name);

            if (!string.IsNullOrEmpty(param.Type))
                sb.Append($" As {param.Type}");

            if (!string.IsNullOrEmpty(param.DefaultValue))
                sb.Append($" = {param.DefaultValue}");

            paramStrings.Add(sb.ToString());
        }

        var paramsString = string.Join(", ", paramStrings);

        if (IsFunction)
        {
            var retType = NewReturnType ?? ReturnType ?? "Object";
            PreviewSignature = $"Function {MethodName}({paramsString}) As {retType}";
        }
        else
        {
            PreviewSignature = $"Sub {MethodName}({paramsString})";
        }

        ValidateParameters();
    }

    private void ValidateParameters()
    {
        ErrorMessage = "";

        // Check for duplicate names
        var names = Parameters.Select(p => p.Name?.ToLowerInvariant()).ToList();
        if (names.Distinct().Count() != names.Count)
        {
            ErrorMessage = "Duplicate parameter names are not allowed";
            return;
        }

        // Check for empty names
        if (Parameters.Any(p => string.IsNullOrWhiteSpace(p.Name)))
        {
            ErrorMessage = "Parameter names cannot be empty";
            return;
        }

        // Check for invalid names
        foreach (var param in Parameters)
        {
            if (!string.IsNullOrEmpty(param.Name) && !IsValidIdentifier(param.Name))
            {
                ErrorMessage = $"'{param.Name}' is not a valid identifier";
                return;
            }
        }

        // Check that optional parameters come after required parameters
        var seenOptional = false;
        foreach (var param in Parameters)
        {
            if (param.IsOptional)
                seenOptional = true;
            else if (seenOptional)
            {
                ErrorMessage = "Required parameters cannot follow optional parameters";
                return;
            }
        }

        // Check that new parameters have default values
        foreach (var param in Parameters)
        {
            if (param.IsNew && string.IsNullOrWhiteSpace(param.DefaultValue))
            {
                ErrorMessage = $"New parameter '{param.Name}' must have a default value for existing call sites";
                return;
            }
        }
    }

    private bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage)) return;

        IsLoading = true;

        try
        {
            // Build the signature change
            var change = new SignatureChange
            {
                NewReturnType = IsFunction && NewReturnType != ReturnType ? NewReturnType : null,
                Parameters = new List<ParameterChange>()
            };

            for (var i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                change.Parameters.Add(new ParameterChange
                {
                    Kind = param.IsNew ? ParameterChangeKind.Add :
                           (param.OriginalIndex != i ? ParameterChangeKind.Modify : ParameterChangeKind.Keep),
                    OriginalIndex = param.OriginalIndex,
                    NewIndex = i,
                    Name = param.Name ?? "",
                    Type = param.Type,
                    IsByRef = param.IsByRef,
                    IsOptional = param.IsOptional,
                    DefaultValue = param.DefaultValue
                });
            }

            var result = await _refactoringService.ChangeSignatureAsync(FilePath, Line, Column, change);

            if (result.Success)
            {
                // Apply edits to files
                foreach (var fileEdit in result.FileEdits)
                {
                    await ApplyEditsToFileAsync(fileEdit);
                }
            }

            ChangeCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ApplyEditsToFileAsync(FileEdit fileEdit)
    {
        var content = await _fileService.ReadFileAsync(fileEdit.FilePath);
        var lines = content.Split('\n').ToList();

        // Apply edits in reverse order to maintain line numbers
        foreach (var edit in fileEdit.Edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            if (edit.StartLine == edit.EndLine)
            {
                // Single line edit
                if (edit.StartLine <= lines.Count)
                {
                    var line = lines[edit.StartLine - 1];
                    var before = edit.StartColumn > 1 && edit.StartColumn <= line.Length + 1
                        ? line.Substring(0, edit.StartColumn - 1)
                        : "";
                    var after = edit.EndColumn <= line.Length + 1
                        ? line.Substring(Math.Min(edit.EndColumn - 1, line.Length))
                        : "";
                    lines[edit.StartLine - 1] = before + edit.NewText + after;
                }
            }
        }

        var newContent = string.Join("\n", lines);
        await _fileService.WriteFileAsync(fileEdit.FilePath, newContent);
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}

public partial class ParameterViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private string? _type;

    [ObservableProperty]
    private bool _isByRef;

    [ObservableProperty]
    private bool _isOptional;

    [ObservableProperty]
    private string? _defaultValue;

    [ObservableProperty]
    private int _originalIndex;

    [ObservableProperty]
    private bool _isNew;
}
