using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class EncapsulateFieldDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;

    [ObservableProperty]
    private string _fieldName = "";

    [ObservableProperty]
    private string _propertyName = "";

    [ObservableProperty]
    private string _newFieldName = "";

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private int _column;

    [ObservableProperty]
    private string? _fieldType;

    [ObservableProperty]
    private bool _isShared;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _generateGetter = true;

    [ObservableProperty]
    private bool _generateSetter = true;

    [ObservableProperty]
    private bool _updateReferences = true;

    [ObservableProperty]
    private int _referenceCount;

    [ObservableProperty]
    private string _selectedPropertyAccessibility = "Public";

    [ObservableProperty]
    private string _selectedFieldAccessibility = "Private";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _previewCode = "";

    public string[] AccessibilityOptions { get; } = { "Public", "Private", "Protected", "Friend" };

    public event EventHandler<EncapsulateFieldResult>? EncapsulateCompleted;
    public event EventHandler? Cancelled;

    public EncapsulateFieldDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
    {
        _refactoringService = refactoringService;
        _fileService = fileService;
    }

    public async Task InitializeAsync(string filePath, int line, int column, string fieldName)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        FieldName = fieldName;
        IsLoading = true;

        try
        {
            var fieldInfo = await _refactoringService.GetFieldInfoAsync(filePath, line, column);
            if (fieldInfo == null)
            {
                ErrorMessage = "Could not find field definition";
                return;
            }

            FieldName = fieldInfo.Name;
            FieldType = fieldInfo.Type;
            IsShared = fieldInfo.IsShared;
            IsReadOnly = fieldInfo.IsReadOnly;
            ReferenceCount = fieldInfo.ReferenceCount;

            // Generate default names
            PropertyName = GeneratePropertyName(fieldInfo.Name);
            NewFieldName = GenerateFieldName(fieldInfo.Name);

            // Set accessibility based on original field
            SelectedPropertyAccessibility = fieldInfo.Accessibility == FieldAccessibility.Public ? "Public" : "Public";
            SelectedFieldAccessibility = "Private";

            // If field is read-only, disable setter generation
            if (IsReadOnly)
            {
                GenerateSetter = false;
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

    private string GeneratePropertyName(string fieldName)
    {
        // Remove underscore prefix if present, capitalize first letter
        var name = fieldName.TrimStart('_');
        if (string.IsNullOrEmpty(name))
            return "Property1";

        return char.ToUpper(name[0]) + name.Substring(1);
    }

    private string GenerateFieldName(string fieldName)
    {
        // Add underscore prefix, lowercase first letter
        var name = fieldName.TrimStart('_');
        if (string.IsNullOrEmpty(name))
            return "_field";

        return "_" + char.ToLower(name[0]) + name.Substring(1);
    }

    partial void OnPropertyNameChanged(string value)
    {
        ValidateNames();
        UpdatePreview();
    }

    partial void OnNewFieldNameChanged(string value)
    {
        ValidateNames();
        UpdatePreview();
    }

    partial void OnGenerateGetterChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnGenerateSetterChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnSelectedPropertyAccessibilityChanged(string value)
    {
        UpdatePreview();
    }

    partial void OnSelectedFieldAccessibilityChanged(string value)
    {
        UpdatePreview();
    }

    private void ValidateNames()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(PropertyName))
        {
            ErrorMessage = "Property name cannot be empty";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewFieldName))
        {
            ErrorMessage = "Field name cannot be empty";
            return;
        }

        if (!IsValidIdentifier(PropertyName))
        {
            ErrorMessage = "Property name is not a valid identifier";
            return;
        }

        if (!IsValidIdentifier(NewFieldName))
        {
            ErrorMessage = "Field name is not a valid identifier";
            return;
        }

        if (PropertyName.Equals(NewFieldName, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Property and field names must be different";
            return;
        }

        if (!GenerateGetter && !GenerateSetter)
        {
            ErrorMessage = "At least one accessor (Get or Set) must be generated";
            return;
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

    private void UpdatePreview()
    {
        var sb = new System.Text.StringBuilder();

        // Field declaration
        sb.Append(SelectedFieldAccessibility);
        sb.Append(' ');

        if (IsShared)
            sb.Append("Shared ");

        if (IsReadOnly)
            sb.Append("ReadOnly ");

        sb.Append(NewFieldName);
        sb.Append(" As ");
        sb.AppendLine(FieldType ?? "Object");

        sb.AppendLine();

        // Property declaration
        sb.Append(SelectedPropertyAccessibility);
        sb.Append(' ');

        if (IsShared)
            sb.Append("Shared ");

        if (GenerateGetter && !GenerateSetter)
            sb.Append("ReadOnly ");

        sb.Append("Property ");
        sb.Append(PropertyName);
        sb.Append(" As ");
        sb.AppendLine(FieldType ?? "Object");

        // Getter
        if (GenerateGetter)
        {
            sb.AppendLine("    Get");
            sb.Append("        Return ");
            sb.AppendLine(NewFieldName);
            sb.AppendLine("    End Get");
        }

        // Setter
        if (GenerateSetter && !IsReadOnly)
        {
            sb.AppendLine("    Set(value As " + (FieldType ?? "Object") + ")");
            sb.Append("        ");
            sb.Append(NewFieldName);
            sb.AppendLine(" = value");
            sb.AppendLine("    End Set");
        }

        sb.Append("End Property");

        PreviewCode = sb.ToString();
    }

    [RelayCommand]
    private async Task EncapsulateAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage)) return;

        IsLoading = true;

        try
        {
            var options = new EncapsulateFieldOptions
            {
                PropertyName = PropertyName,
                FieldName = NewFieldName,
                GenerateGetter = GenerateGetter,
                GenerateSetter = GenerateSetter && !IsReadOnly,
                PropertyAccessibility = ParseAccessibility(SelectedPropertyAccessibility),
                FieldAccessibility = ParseAccessibility(SelectedFieldAccessibility),
                UpdateReferences = UpdateReferences
            };

            var result = await _refactoringService.EncapsulateFieldAsync(FilePath, Line, Column, options);

            if (result.Success)
            {
                // Apply edits to files
                foreach (var fileEdit in result.FileEdits)
                {
                    await ApplyEditsToFileAsync(fileEdit);
                }
            }

            EncapsulateCompleted?.Invoke(this, result);
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

    private FieldAccessibility ParseAccessibility(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "public" => FieldAccessibility.Public,
            "private" => FieldAccessibility.Private,
            "protected" => FieldAccessibility.Protected,
            "friend" => FieldAccessibility.Friend,
            _ => FieldAccessibility.Private
        };
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
