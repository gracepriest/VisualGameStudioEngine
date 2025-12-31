using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class MoveTypeToFileDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private TypeDefinitionInfo? _typeInfo;
    private string _filePath = "";
    private int _line;
    private int _column;

    [ObservableProperty]
    private string _typeName = "";

    [ObservableProperty]
    private string _typeKind = "";

    [ObservableProperty]
    private string _newFileName = "";

    [ObservableProperty]
    private string _targetDirectory = "";

    [ObservableProperty]
    private bool _includeImports = true;

    [ObservableProperty]
    private bool _removeFromOriginalFile = true;

    [ObservableProperty]
    private bool _addToProject = true;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string _validationMessage = "";

    [ObservableProperty]
    private bool _isLoading;

    public bool DialogResult { get; private set; }
    public MoveTypeToFileResult? Result { get; private set; }

    public event EventHandler<MoveTypeToFileResult>? MoveCompleted;
    public event EventHandler? Cancelled;

    public MoveTypeToFileDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
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
            _typeInfo = await _refactoringService.GetTypeInfoAsync(filePath, line, column);

            if (_typeInfo != null)
            {
                TypeName = _typeInfo.Name;
                TypeKind = _typeInfo.Kind.ToString();
                NewFileName = _typeInfo.SuggestedFileName;
                TargetDirectory = Path.GetDirectoryName(filePath) ?? "";

                UpdatePreview();
                ValidateInput();
            }
            else
            {
                ValidationMessage = "Could not find type definition at the specified location";
                IsValid = false;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnNewFileNameChanged(string value)
    {
        ValidateInput();
        UpdatePreview();
    }

    partial void OnTargetDirectoryChanged(string value)
    {
        ValidateInput();
        UpdatePreview();
    }

    partial void OnIncludeImportsChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnRemoveFromOriginalFileChanged(bool value)
    {
        UpdatePreview();
    }

    private void ValidateInput()
    {
        if (_typeInfo == null)
        {
            IsValid = false;
            ValidationMessage = "No type selected";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewFileName))
        {
            IsValid = false;
            ValidationMessage = "File name is required";
            return;
        }

        // Check for invalid file name characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (NewFileName.Any(c => invalidChars.Contains(c)))
        {
            IsValid = false;
            ValidationMessage = "File name contains invalid characters";
            return;
        }

        // Check if file already exists
        var fullPath = Path.Combine(TargetDirectory, NewFileName);
        if (File.Exists(fullPath))
        {
            IsValid = false;
            ValidationMessage = $"File '{NewFileName}' already exists";
            return;
        }

        // Check if target directory exists
        if (!string.IsNullOrEmpty(TargetDirectory) && !Directory.Exists(TargetDirectory))
        {
            IsValid = false;
            ValidationMessage = "Target directory does not exist";
            return;
        }

        IsValid = true;
        ValidationMessage = "";
    }

    private void UpdatePreview()
    {
        if (_typeInfo == null)
        {
            Preview = "";
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("' New file content preview:");
        sb.AppendLine($"' File: {NewFileName}");
        sb.AppendLine();

        if (IncludeImports && _typeInfo.Imports.Count > 0)
        {
            foreach (var import in _typeInfo.Imports)
            {
                sb.AppendLine(import);
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(_typeInfo.Namespace))
        {
            sb.AppendLine($"Namespace {_typeInfo.Namespace}");
            sb.AppendLine();
        }

        // Show truncated type definition
        var lines = _typeInfo.FullDefinition.Split('\n');
        var maxLines = 10;
        for (var i = 0; i < Math.Min(lines.Length, maxLines); i++)
        {
            sb.AppendLine(lines[i].TrimEnd('\r'));
        }

        if (lines.Length > maxLines)
        {
            sb.AppendLine("    ' ... (more content)");
            sb.AppendLine(lines[^1].TrimEnd('\r'));
        }

        if (!string.IsNullOrEmpty(_typeInfo.Namespace))
        {
            sb.AppendLine();
            sb.AppendLine("End Namespace");
        }

        Preview = sb.ToString();
    }

    [RelayCommand]
    private async Task MoveTypeAsync()
    {
        if (!IsValid || _typeInfo == null)
            return;

        IsLoading = true;

        try
        {
            var options = new MoveTypeToFileOptions
            {
                NewFileName = NewFileName,
                TargetDirectory = TargetDirectory,
                IncludeImports = IncludeImports,
                RemoveFromOriginalFile = RemoveFromOriginalFile,
                AddToProject = AddToProject
            };

            Result = await _refactoringService.MoveTypeToFileAsync(_filePath, _line, _column, options);

            if (Result.Success)
            {
                DialogResult = true;
                MoveCompleted?.Invoke(this, Result);
            }
            else
            {
                ValidationMessage = Result.ErrorMessage ?? "Unknown error occurred";
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
