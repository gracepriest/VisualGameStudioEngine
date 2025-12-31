using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class OverrideMethodDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private OverridableMethodsInfo? _methodsInfo;
    private string _filePath = "";
    private int _line;
    private int _column;

    [ObservableProperty]
    private string _className = "";

    [ObservableProperty]
    private string _baseClassName = "";

    [ObservableProperty]
    private ObservableCollection<OverridableMethodViewModel> _methods = new();

    [ObservableProperty]
    private bool _callBaseMethod = true;

    [ObservableProperty]
    private bool _insertRegion = true;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private int _totalOverridableCount;

    [ObservableProperty]
    private int _alreadyOverriddenCount;

    public bool DialogResult { get; private set; }
    public OverrideMethodResult? Result { get; private set; }

    public event EventHandler<OverrideMethodResult>? OverrideCompleted;
    public event EventHandler? Cancelled;

    public OverrideMethodDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
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
            _methodsInfo = await _refactoringService.GetOverridableMethodsAsync(filePath, line, column);

            if (_methodsInfo != null)
            {
                ClassName = _methodsInfo.ClassName;
                BaseClassName = _methodsInfo.BaseClassName ?? "";

                Methods.Clear();
                foreach (var method in _methodsInfo.Methods)
                {
                    var vm = new OverridableMethodViewModel
                    {
                        Name = method.Name,
                        Kind = method.Kind,
                        Signature = method.Signature,
                        ReturnType = method.ReturnType,
                        DeclaringClass = method.DeclaringClass,
                        IsAbstract = method.IsAbstract,
                        IsOverridden = method.IsOverridden,
                        IsSelected = !method.IsOverridden && method.IsAbstract // Auto-select abstract methods
                    };

                    foreach (var param in method.Parameters)
                    {
                        vm.Parameters.Add(new OverridableParameterViewModel
                        {
                            Name = param.Name,
                            Type = param.Type,
                            IsByRef = param.IsByRef,
                            IsOptional = param.IsOptional,
                            DefaultValue = param.DefaultValue
                        });
                    }

                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(OverridableMethodViewModel.IsSelected))
                        {
                            UpdatePreview();
                            ValidateInput();
                        }
                    };

                    Methods.Add(vm);
                }

                UpdateCounts();
                UpdatePreview();
                ValidateInput();
            }
            else
            {
                ErrorMessage = "No overridable methods found. Make sure the class inherits from a base class with Overridable or MustOverride members.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateCounts()
    {
        TotalOverridableCount = Methods.Count(m => !m.IsOverridden);
        AlreadyOverriddenCount = Methods.Count(m => m.IsOverridden);
    }

    partial void OnCallBaseMethodChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnInsertRegionChanged(bool value)
    {
        UpdatePreview();
    }

    private void ValidateInput()
    {
        if (_methodsInfo == null)
        {
            ErrorMessage = "No method information available";
            return;
        }

        var selectedCount = Methods.Count(m => m.IsSelected && !m.IsOverridden);
        if (selectedCount == 0)
        {
            ErrorMessage = "Select at least one method to override";
            return;
        }

        ErrorMessage = "";
    }

    private void UpdatePreview()
    {
        if (_methodsInfo == null)
        {
            Preview = "";
            return;
        }

        var sb = new System.Text.StringBuilder();
        var selectedMethods = Methods
            .Where(m => m.IsSelected && !m.IsOverridden)
            .ToList();

        if (selectedMethods.Count == 0)
        {
            Preview = "' No methods selected";
            return;
        }

        if (InsertRegion)
        {
            sb.AppendLine($"#Region \"Overrides\"");
            sb.AppendLine();
        }

        foreach (var method in selectedMethods)
        {
            switch (method.Kind)
            {
                case OverridableMethodKind.Sub:
                    sb.AppendLine($"Public Overrides Sub {method.Name}({FormatParameters(method)})");
                    if (CallBaseMethod && !method.IsAbstract)
                        sb.AppendLine($"    MyBase.{method.Name}({FormatArgumentList(method)})");
                    else
                        sb.AppendLine("    ' TODO: Implement override");
                    sb.AppendLine("End Sub");
                    break;

                case OverridableMethodKind.Function:
                    sb.AppendLine($"Public Overrides Function {method.Name}({FormatParameters(method)}) As {method.ReturnType ?? "Object"}");
                    if (CallBaseMethod && !method.IsAbstract)
                        sb.AppendLine($"    Return MyBase.{method.Name}({FormatArgumentList(method)})");
                    else
                        sb.AppendLine("    ' TODO: Implement override\n    Return Nothing");
                    sb.AppendLine("End Function");
                    break;

                case OverridableMethodKind.Property:
                    sb.AppendLine($"Public Overrides Property {method.Name} As {method.ReturnType ?? "Object"}");
                    sb.AppendLine("    Get");
                    if (CallBaseMethod && !method.IsAbstract)
                        sb.AppendLine($"        Return MyBase.{method.Name}");
                    else
                        sb.AppendLine("        Return _backing");
                    sb.AppendLine("    End Get");
                    sb.AppendLine("    Set(value)");
                    if (CallBaseMethod && !method.IsAbstract)
                        sb.AppendLine($"        MyBase.{method.Name} = value");
                    else
                        sb.AppendLine("        _backing = value");
                    sb.AppendLine("    End Set");
                    sb.AppendLine("End Property");
                    break;
            }

            sb.AppendLine();
        }

        if (InsertRegion)
        {
            sb.AppendLine("#End Region");
        }

        Preview = sb.ToString().TrimEnd();
    }

    private string FormatParameters(OverridableMethodViewModel method)
    {
        if (method.Parameters.Count == 0)
            return "";

        var parts = method.Parameters.Select(p =>
        {
            var byRef = p.IsByRef ? "ByRef " : "";
            var optional = p.IsOptional ? "Optional " : "";
            var defaultVal = p.IsOptional && p.DefaultValue != null ? $" = {p.DefaultValue}" : "";
            return $"{optional}{byRef}{p.Name} As {p.Type}{defaultVal}";
        });

        return string.Join(", ", parts);
    }

    private string FormatArgumentList(OverridableMethodViewModel method)
    {
        if (method.Parameters.Count == 0)
            return "";

        return string.Join(", ", method.Parameters.Select(p => p.Name));
    }

    [RelayCommand]
    private void SelectAllMethods()
    {
        foreach (var method in Methods.Where(m => !m.IsOverridden))
        {
            method.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoneMethods()
    {
        foreach (var method in Methods)
        {
            method.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAbstractOnly()
    {
        foreach (var method in Methods)
        {
            method.IsSelected = method.IsAbstract && !method.IsOverridden;
        }
    }

    [RelayCommand]
    private async Task OverrideAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage) || _methodsInfo == null)
            return;

        IsLoading = true;

        try
        {
            var options = new OverrideMethodOptions
            {
                SelectedMethods = Methods
                    .Where(m => m.IsSelected && !m.IsOverridden)
                    .Select(m => m.Name)
                    .ToList(),
                CallBaseMethod = CallBaseMethod,
                InsertRegion = InsertRegion
            };

            Result = await _refactoringService.OverrideMethodAsync(_filePath, _line, _column, options);

            if (Result.Success)
            {
                DialogResult = true;
                OverrideCompleted?.Invoke(this, Result);
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

public partial class OverridableMethodViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private OverridableMethodKind _kind;

    [ObservableProperty]
    private string _signature = "";

    [ObservableProperty]
    private string? _returnType;

    [ObservableProperty]
    private string? _declaringClass;

    [ObservableProperty]
    private bool _isAbstract;

    [ObservableProperty]
    private bool _isOverridden;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<OverridableParameterViewModel> _parameters = new();

    public string KindText => Kind switch
    {
        OverridableMethodKind.Sub => "Sub",
        OverridableMethodKind.Function => "Function",
        OverridableMethodKind.Property => "Property",
        _ => ""
    };

    public string DisplayText => Kind switch
    {
        OverridableMethodKind.Sub => $"Sub {Name}(...)",
        OverridableMethodKind.Function => $"Function {Name}(...) As {ReturnType ?? "Object"}",
        OverridableMethodKind.Property => $"Property {Name} As {ReturnType ?? "Object"}",
        _ => Name
    };

    public string StatusText
    {
        get
        {
            if (IsOverridden) return "(already overridden)";
            if (IsAbstract) return "(abstract - must override)";
            return "";
        }
    }
}

public partial class OverridableParameterViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _type = "";

    [ObservableProperty]
    private bool _isByRef;

    [ObservableProperty]
    private bool _isOptional;

    [ObservableProperty]
    private string? _defaultValue;
}
