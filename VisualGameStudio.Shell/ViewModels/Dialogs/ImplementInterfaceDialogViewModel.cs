using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ImplementInterfaceDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private ImplementableInterfacesInfo? _interfacesInfo;
    private string _filePath = "";
    private int _line;
    private int _column;

    [ObservableProperty]
    private string _className = "";

    [ObservableProperty]
    private ObservableCollection<InterfaceViewModel> _interfaces = new();

    [ObservableProperty]
    private InterfaceViewModel? _selectedInterface;

    [ObservableProperty]
    private bool _throwNotImplementedException = true;

    [ObservableProperty]
    private bool _insertRegion = true;

    [ObservableProperty]
    private bool _generateExplicitImplementation;

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private int _totalUnimplementedCount;

    public bool DialogResult { get; private set; }
    public ImplementInterfaceResult? Result { get; private set; }

    public event EventHandler<ImplementInterfaceResult>? ImplementCompleted;
    public event EventHandler? Cancelled;

    public ImplementInterfaceDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
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
            _interfacesInfo = await _refactoringService.GetImplementableInterfacesAsync(filePath, line, column);

            if (_interfacesInfo != null)
            {
                ClassName = _interfacesInfo.ClassName;

                Interfaces.Clear();
                foreach (var iface in _interfacesInfo.Interfaces)
                {
                    var vm = new InterfaceViewModel
                    {
                        Name = iface.Name,
                        FilePath = iface.FilePath,
                        IsFullyImplemented = iface.IsFullyImplemented,
                        UnimplementedCount = iface.UnimplementedCount
                    };

                    foreach (var member in iface.Members)
                    {
                        var memberVm = new InterfaceMemberViewModel
                        {
                            Name = member.Name,
                            Kind = member.Kind,
                            Signature = member.Signature,
                            ReturnType = member.ReturnType,
                            IsImplemented = member.IsImplemented,
                            IsSelected = !member.IsImplemented
                        };
                        memberVm.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(InterfaceMemberViewModel.IsSelected))
                            {
                                UpdatePreview();
                                ValidateInput();
                            }
                        };
                        vm.Members.Add(memberVm);
                    }

                    Interfaces.Add(vm);
                }

                // Select the first interface with unimplemented members
                SelectedInterface = Interfaces.FirstOrDefault(i => !i.IsFullyImplemented) ?? Interfaces.FirstOrDefault();

                UpdateTotalCount();
                UpdatePreview();
                ValidateInput();
            }
            else
            {
                ErrorMessage = "No interfaces found to implement. Make sure the class has an Implements clause.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateTotalCount()
    {
        TotalUnimplementedCount = Interfaces.Sum(i => i.UnimplementedCount);
    }

    partial void OnSelectedInterfaceChanged(InterfaceViewModel? value)
    {
        UpdatePreview();
        ValidateInput();
    }

    partial void OnThrowNotImplementedExceptionChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnInsertRegionChanged(bool value)
    {
        UpdatePreview();
    }

    partial void OnGenerateExplicitImplementationChanged(bool value)
    {
        UpdatePreview();
    }

    private void ValidateInput()
    {
        if (_interfacesInfo == null)
        {
            ErrorMessage = "No interface information available";
            return;
        }

        if (SelectedInterface == null)
        {
            ErrorMessage = "Please select an interface";
            return;
        }

        var selectedCount = SelectedInterface.Members.Count(m => m.IsSelected && !m.IsImplemented);
        if (selectedCount == 0)
        {
            ErrorMessage = "Select at least one member to implement";
            return;
        }

        ErrorMessage = "";
    }

    private void UpdatePreview()
    {
        if (_interfacesInfo == null || SelectedInterface == null)
        {
            Preview = "";
            return;
        }

        var sb = new System.Text.StringBuilder();
        var selectedMembers = SelectedInterface.Members
            .Where(m => m.IsSelected && !m.IsImplemented)
            .ToList();

        if (selectedMembers.Count == 0)
        {
            Preview = "' No members selected";
            return;
        }

        if (InsertRegion)
        {
            sb.AppendLine($"#Region \"{SelectedInterface.Name} Implementation\"");
            sb.AppendLine();
        }

        foreach (var member in selectedMembers)
        {
            var implementsClause = GenerateExplicitImplementation
                ? $" Implements {SelectedInterface.Name}.{member.Name}"
                : "";

            switch (member.Kind)
            {
                case InterfaceMemberKind.Sub:
                    sb.AppendLine($"Public Sub {member.Name}(...){implementsClause}");
                    if (ThrowNotImplementedException)
                        sb.AppendLine("    Throw New NotImplementedException()");
                    else
                        sb.AppendLine("    ' TODO: Implement");
                    sb.AppendLine("End Sub");
                    break;

                case InterfaceMemberKind.Function:
                    sb.AppendLine($"Public Function {member.Name}(...) As {member.ReturnType ?? "Object"}{implementsClause}");
                    if (ThrowNotImplementedException)
                        sb.AppendLine("    Throw New NotImplementedException()");
                    else
                        sb.AppendLine("    Return Nothing");
                    sb.AppendLine("End Function");
                    break;

                case InterfaceMemberKind.Property:
                    sb.AppendLine($"Public Property {member.Name} As {member.ReturnType ?? "Object"}{implementsClause}");
                    sb.AppendLine("    Get");
                    if (ThrowNotImplementedException)
                        sb.AppendLine("        Throw New NotImplementedException()");
                    else
                        sb.AppendLine("        Return _backing");
                    sb.AppendLine("    End Get");
                    sb.AppendLine("    Set(value)");
                    if (ThrowNotImplementedException)
                        sb.AppendLine("        Throw New NotImplementedException()");
                    else
                        sb.AppendLine("        _backing = value");
                    sb.AppendLine("    End Set");
                    sb.AppendLine("End Property");
                    break;

                case InterfaceMemberKind.Event:
                    sb.AppendLine($"Public Event {member.Name}(...)");
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

    [RelayCommand]
    private void SelectAllMembers()
    {
        if (SelectedInterface == null) return;

        foreach (var member in SelectedInterface.Members.Where(m => !m.IsImplemented))
        {
            member.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoneMembers()
    {
        if (SelectedInterface == null) return;

        foreach (var member in SelectedInterface.Members)
        {
            member.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task ImplementAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage) || _interfacesInfo == null || SelectedInterface == null)
            return;

        IsLoading = true;

        try
        {
            var options = new ImplementInterfaceOptions
            {
                InterfaceName = SelectedInterface.Name,
                SelectedMembers = SelectedInterface.Members
                    .Where(m => m.IsSelected && !m.IsImplemented)
                    .Select(m => m.Name)
                    .ToList(),
                ThrowNotImplementedException = ThrowNotImplementedException,
                InsertRegion = InsertRegion,
                GenerateExplicitImplementation = GenerateExplicitImplementation
            };

            Result = await _refactoringService.ImplementInterfaceAsync(_filePath, _line, _column, options);

            if (Result.Success)
            {
                DialogResult = true;
                ImplementCompleted?.Invoke(this, Result);
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

public partial class InterfaceViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isFullyImplemented;

    [ObservableProperty]
    private int _unimplementedCount;

    [ObservableProperty]
    private ObservableCollection<InterfaceMemberViewModel> _members = new();

    public string DisplayName => IsFullyImplemented
        ? $"{Name} (fully implemented)"
        : $"{Name} ({UnimplementedCount} member{(UnimplementedCount == 1 ? "" : "s")} to implement)";
}

public partial class InterfaceMemberViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private InterfaceMemberKind _kind;

    [ObservableProperty]
    private string _signature = "";

    [ObservableProperty]
    private string? _returnType;

    [ObservableProperty]
    private bool _isImplemented;

    [ObservableProperty]
    private bool _isSelected;

    public string KindText => Kind switch
    {
        InterfaceMemberKind.Sub => "Sub",
        InterfaceMemberKind.Function => "Function",
        InterfaceMemberKind.Property => "Property",
        InterfaceMemberKind.Event => "Event",
        _ => ""
    };

    public string DisplayText => Kind switch
    {
        InterfaceMemberKind.Sub => $"Sub {Name}(...)",
        InterfaceMemberKind.Function => $"Function {Name}(...) As {ReturnType ?? "Object"}",
        InterfaceMemberKind.Property => $"Property {Name} As {ReturnType ?? "Object"}",
        InterfaceMemberKind.Event => $"Event {Name}(...)",
        _ => Name
    };
}
