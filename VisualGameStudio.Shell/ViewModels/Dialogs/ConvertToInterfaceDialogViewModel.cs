using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ConvertToInterfaceDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private ConvertToInterfaceInfo? _classInfo;
    private string _filePath = "";
    private int _line;
    private int _column;

    [ObservableProperty]
    private string _className = "";

    [ObservableProperty]
    private string _interfaceName = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private bool _createInSameFile = true;

    [ObservableProperty]
    private bool _createInNewFile;

    [ObservableProperty]
    private bool _implementInterface = true;

    [ObservableProperty]
    private bool _addAboveClass = true;

    [ObservableProperty]
    private ObservableCollection<ConvertToInterfaceMemberViewModel> _members = new();

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private int _selectedMemberCount;

    [ObservableProperty]
    private ObservableCollection<string> _existingInterfaces = new();

    public bool DialogResult { get; private set; }
    public ConvertToInterfaceResult? Result { get; private set; }

    public event EventHandler<ConvertToInterfaceResult>? ConvertCompleted;
    public event EventHandler? Cancelled;

    public ConvertToInterfaceDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
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
            _classInfo = await _refactoringService.GetConvertToInterfaceInfoAsync(filePath, line, column);

            if (_classInfo != null)
            {
                ClassName = _classInfo.ClassName;
                InterfaceName = _classInfo.SuggestedInterfaceName;
                FileName = InterfaceName + ".bas";

                // Show existing interfaces
                ExistingInterfaces.Clear();
                foreach (var iface in _classInfo.ExistingInterfaces)
                {
                    ExistingInterfaces.Add(iface);
                }

                // Populate members
                Members.Clear();
                foreach (var member in _classInfo.Members)
                {
                    var vm = new ConvertToInterfaceMemberViewModel
                    {
                        Name = member.Name,
                        Kind = member.Kind.ToString(),
                        Signature = member.Signature,
                        InterfaceSignature = member.InterfaceSignature,
                        IsSelected = true
                    };
                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(ConvertToInterfaceMemberViewModel.IsSelected))
                        {
                            UpdateSelectedCount();
                            UpdatePreview();
                        }
                    };
                    Members.Add(vm);
                }

                UpdateSelectedCount();
                UpdatePreview();
                ValidateInput();
            }
            else
            {
                ErrorMessage = "Could not find a class with public members at the specified location";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedMemberCount = Members.Count(m => m.IsSelected);
    }

    partial void OnInterfaceNameChanged(string value)
    {
        if (CreateInNewFile && !string.IsNullOrEmpty(value))
        {
            FileName = value + ".bas";
        }
        ValidateInput();
        UpdatePreview();
    }

    partial void OnFileNameChanged(string value)
    {
        ValidateInput();
    }

    partial void OnCreateInSameFileChanged(bool value)
    {
        if (value)
        {
            CreateInNewFile = false;
        }
        ValidateInput();
        UpdatePreview();
    }

    partial void OnCreateInNewFileChanged(bool value)
    {
        if (value)
        {
            CreateInSameFile = false;
        }
        ValidateInput();
        UpdatePreview();
    }

    partial void OnAddAboveClassChanged(bool value)
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

        if (string.IsNullOrWhiteSpace(InterfaceName))
        {
            ErrorMessage = "Interface name is required";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(InterfaceName, @"^[A-Za-z_]\w*$"))
        {
            ErrorMessage = "Interface name is not a valid identifier";
            return;
        }

        // Check if interface already exists
        if (_classInfo.ExistingInterfaces.Contains(InterfaceName, StringComparer.OrdinalIgnoreCase))
        {
            ErrorMessage = $"Class already implements '{InterfaceName}'";
            return;
        }

        if (CreateInNewFile)
        {
            if (string.IsNullOrWhiteSpace(FileName))
            {
                ErrorMessage = "File name is required";
                return;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            if (FileName.Any(c => invalidChars.Contains(c)))
            {
                ErrorMessage = "File name contains invalid characters";
                return;
            }

            // Check if file already exists
            var sourceDir = Path.GetDirectoryName(_filePath) ?? "";
            var fullPath = Path.Combine(sourceDir, FileName);
            if (File.Exists(fullPath))
            {
                ErrorMessage = $"File '{FileName}' already exists";
                return;
            }
        }

        if (SelectedMemberCount == 0)
        {
            ErrorMessage = "Select at least one member";
            return;
        }

        ErrorMessage = "";
    }

    private void UpdatePreview()
    {
        if (_classInfo == null || string.IsNullOrEmpty(InterfaceName))
        {
            Preview = "";
            return;
        }

        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Public Interface {InterfaceName}");

        foreach (var member in Members.Where(m => m.IsSelected))
        {
            sb.AppendLine($"    {member.InterfaceSignature}");
        }

        sb.AppendLine("End Interface");

        if (ImplementInterface)
        {
            sb.AppendLine();
            sb.AppendLine($"' Class {ClassName} will implement {InterfaceName}");
        }

        if (CreateInSameFile)
        {
            sb.AppendLine();
            if (AddAboveClass)
            {
                sb.AppendLine("' Interface will be added above the class definition");
            }
            else
            {
                sb.AppendLine("' Interface will be added after imports");
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine($"' Interface will be created in {FileName}");
        }

        Preview = sb.ToString();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var member in Members)
        {
            member.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var member in Members)
        {
            member.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage) || _classInfo == null)
            return;

        IsLoading = true;

        try
        {
            var interfaceFilePath = CreateInNewFile
                ? Path.Combine(Path.GetDirectoryName(_filePath) ?? "", FileName)
                : null;

            var options = new ConvertToInterfaceOptions
            {
                InterfaceName = InterfaceName,
                MemberNames = Members.Where(m => m.IsSelected).Select(m => m.Name).ToList(),
                ImplementInterface = ImplementInterface,
                CreateInSeparateFile = CreateInNewFile,
                InterfaceFilePath = interfaceFilePath,
                AddAboveClass = AddAboveClass
            };

            Result = await _refactoringService.ConvertToInterfaceAsync(_filePath, _line, _column, options);

            if (Result.Success)
            {
                DialogResult = true;
                ConvertCompleted?.Invoke(this, Result);
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

public partial class ConvertToInterfaceMemberViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _kind = "";

    [ObservableProperty]
    private string _signature = "";

    [ObservableProperty]
    private string _interfaceSignature = "";

    [ObservableProperty]
    private bool _isSelected = true;
}
