using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class GenerateConstructorDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private ClassFieldsInfo? _classInfo;
    private string _filePath = "";
    private int _line;
    private int _column;

    [ObservableProperty]
    private string _className = "";

    [ObservableProperty]
    private ObservableCollection<FieldViewModel> _fields = new();

    [ObservableProperty]
    private ObservableCollection<FieldViewModel> _properties = new();

    [ObservableProperty]
    private ObservableCollection<string> _existingConstructors = new();

    [ObservableProperty]
    private string _selectedAccessibility = "Public";

    [ObservableProperty]
    private ObservableCollection<string> _accessibilityOptions = new() { "Public", "Private", "Protected", "Friend" };

    [ObservableProperty]
    private bool _generateNullChecks;

    [ObservableProperty]
    private bool _callBaseConstructor;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private int _selectedFieldCount;

    [ObservableProperty]
    private int _selectedPropertyCount;

    public bool DialogResult { get; private set; }
    public GenerateConstructorResult? Result { get; private set; }

    public event EventHandler<GenerateConstructorResult>? GenerateCompleted;
    public event EventHandler? Cancelled;

    public GenerateConstructorDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
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
            _classInfo = await _refactoringService.GetClassFieldsAsync(filePath, line, column);

            if (_classInfo != null)
            {
                ClassName = _classInfo.ClassName;

                // Populate fields
                Fields.Clear();
                foreach (var field in _classInfo.Fields)
                {
                    var vm = new FieldViewModel
                    {
                        Name = field.Name,
                        Type = field.Type ?? "Object",
                        ParameterName = field.ParameterName ?? field.Name.ToLowerInvariant(),
                        IsSelected = field.IsSelected,
                        IsReadOnly = field.IsReadOnly,
                        HasInitializer = field.HasInitializer
                    };
                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(FieldViewModel.IsSelected))
                        {
                            UpdateSelectedCounts();
                            UpdatePreview();
                            ValidateInput();
                        }
                    };
                    Fields.Add(vm);
                }

                // Populate properties
                Properties.Clear();
                foreach (var prop in _classInfo.Properties)
                {
                    var vm = new FieldViewModel
                    {
                        Name = prop.Name,
                        Type = prop.Type ?? "Object",
                        ParameterName = prop.ParameterName ?? prop.Name.ToLowerInvariant(),
                        IsSelected = prop.IsSelected,
                        IsReadOnly = prop.IsReadOnly,
                        HasInitializer = prop.HasInitializer
                    };
                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(FieldViewModel.IsSelected))
                        {
                            UpdateSelectedCounts();
                            UpdatePreview();
                            ValidateInput();
                        }
                    };
                    Properties.Add(vm);
                }

                // Populate existing constructors
                ExistingConstructors.Clear();
                foreach (var ctor in _classInfo.ExistingConstructors)
                {
                    ExistingConstructors.Add(ctor.Signature);
                }

                UpdateSelectedCounts();
                UpdatePreview();
                ValidateInput();
            }
            else
            {
                ErrorMessage = "Could not find class at the specified location";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateSelectedCounts()
    {
        SelectedFieldCount = Fields.Count(f => f.IsSelected);
        SelectedPropertyCount = Properties.Count(p => p.IsSelected);
    }

    partial void OnSelectedAccessibilityChanged(string value)
    {
        UpdatePreview();
    }

    partial void OnGenerateNullChecksChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnCallBaseConstructorChanged(bool value)
    {
        UpdatePreview();
    }

    private void ValidateInput()
    {
        if (_classInfo == null)
        {
            ErrorMessage = "No class found";
            return;
        }

        var totalSelected = SelectedFieldCount + SelectedPropertyCount;
        if (totalSelected == 0)
        {
            ErrorMessage = "Select at least one field or property";
            return;
        }

        // Check for duplicate constructor signature
        if (_classInfo.ExistingConstructors.Count > 0)
        {
            var selectedTypes = Fields.Where(f => f.IsSelected).Select(f => f.Type)
                .Concat(Properties.Where(p => p.IsSelected).Select(p => p.Type))
                .ToList();

            foreach (var existing in _classInfo.ExistingConstructors)
            {
                if (existing.ParameterTypes.Count == selectedTypes.Count)
                {
                    var match = true;
                    for (var i = 0; i < selectedTypes.Count; i++)
                    {
                        if (!string.Equals(existing.ParameterTypes[i], selectedTypes[i], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        ErrorMessage = "A constructor with the same signature already exists";
                        return;
                    }
                }
            }
        }

        ErrorMessage = "";
    }

    private void UpdatePreview()
    {
        if (_classInfo == null)
        {
            Preview = "";
            return;
        }

        var sb = new System.Text.StringBuilder();
        var accessStr = SelectedAccessibility;

        // Build parameter list
        var parameters = new List<string>();
        var selectedMembers = Fields.Where(f => f.IsSelected)
            .Concat(Properties.Where(p => p.IsSelected))
            .ToList();

        foreach (var member in selectedMembers)
        {
            parameters.Add($"{member.ParameterName} As {member.Type}");
        }

        sb.AppendLine($"{accessStr} Sub New({string.Join(", ", parameters)})");

        if (CallBaseConstructor)
        {
            sb.AppendLine("    MyBase.New()");
        }

        if (GenerateNullChecks)
        {
            foreach (var member in selectedMembers)
            {
                var memberType = member.Type.ToLowerInvariant();
                if (memberType == "string" || memberType == "object" ||
                    (memberType != "integer" && memberType != "long" && memberType != "short" &&
                     memberType != "byte" && memberType != "single" && memberType != "double" &&
                     memberType != "decimal" && memberType != "boolean" && memberType != "char" &&
                     memberType != "date"))
                {
                    sb.AppendLine($"    If {member.ParameterName} Is Nothing Then");
                    sb.AppendLine($"        Throw New ArgumentNullException(NameOf({member.ParameterName}))");
                    sb.AppendLine($"    End If");
                }
            }
        }

        foreach (var member in selectedMembers)
        {
            sb.AppendLine($"    {member.Name} = {member.ParameterName}");
        }

        sb.Append("End Sub");

        Preview = sb.ToString();
    }

    [RelayCommand]
    private void SelectAllFields()
    {
        foreach (var field in Fields)
        {
            field.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoneFields()
    {
        foreach (var field in Fields)
        {
            field.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAllProperties()
    {
        foreach (var prop in Properties)
        {
            prop.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoneProperties()
    {
        foreach (var prop in Properties)
        {
            prop.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage) || _classInfo == null)
            return;

        IsLoading = true;

        try
        {
            var options = new GenerateConstructorOptions
            {
                SelectedFields = Fields.Where(f => f.IsSelected).Select(f => f.Name).ToList(),
                SelectedProperties = Properties.Where(p => p.IsSelected).Select(p => p.Name).ToList(),
                GenerateNullChecks = GenerateNullChecks,
                CallBaseConstructor = CallBaseConstructor,
                Accessibility = SelectedAccessibility switch
                {
                    "Public" => ConstructorAccessibility.Public,
                    "Private" => ConstructorAccessibility.Private,
                    "Protected" => ConstructorAccessibility.Protected,
                    "Friend" => ConstructorAccessibility.Friend,
                    _ => ConstructorAccessibility.Public
                }
            };

            Result = await _refactoringService.GenerateConstructorAsync(_filePath, _line, _column, options);

            if (Result.Success)
            {
                DialogResult = true;
                GenerateCompleted?.Invoke(this, Result);
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

public partial class FieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _type = "";

    [ObservableProperty]
    private string _parameterName = "";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _hasInitializer;
}
