using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ExtractInterfaceDialogViewModel : ViewModelBase
{
    private readonly IRefactoringService _refactoringService;
    private readonly IFileService _fileService;
    private ClassMemberInfo? _classInfo;
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
    private bool _createInSameFile;

    [ObservableProperty]
    private bool _createInNewFile = true;

    [ObservableProperty]
    private bool _implementInterface = true;

    [ObservableProperty]
    private string _selectedAccessibility = "Public";

    [ObservableProperty]
    private ObservableCollection<string> _accessibilityOptions = new() { "Public", "Friend" };

    [ObservableProperty]
    private ObservableCollection<MemberViewModel> _members = new();

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private int _selectedMemberCount;

    public bool DialogResult { get; private set; }
    public ExtractInterfaceResult? Result { get; private set; }

    public event EventHandler<ExtractInterfaceResult>? ExtractCompleted;
    public event EventHandler? Cancelled;

    public ExtractInterfaceDialogViewModel(IRefactoringService refactoringService, IFileService fileService)
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
            _classInfo = await _refactoringService.GetClassMembersAsync(filePath, line, column);

            if (_classInfo != null)
            {
                ClassName = _classInfo.ClassName;
                InterfaceName = "I" + _classInfo.ClassName;
                FileName = InterfaceName + ".bas";

                // Populate members
                Members.Clear();
                foreach (var member in _classInfo.Members)
                {
                    var vm = new MemberViewModel
                    {
                        Name = member.Name,
                        Kind = member.Kind.ToString(),
                        Signature = member.Signature,
                        IsSelected = !member.IsShared // Don't select shared members by default
                    };
                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(MemberViewModel.IsSelected))
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
                ErrorMessage = "Could not find class at the specified location";
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
        if (!CreateInSameFile && !string.IsNullOrEmpty(value))
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

    partial void OnSelectedAccessibilityChanged(string value)
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
        var accessStr = SelectedAccessibility;

        sb.AppendLine($"{accessStr} Interface {InterfaceName}");

        foreach (var member in Members.Where(m => m.IsSelected))
        {
            // Format for interface (remove accessibility modifiers)
            var signature = member.Signature;
            signature = System.Text.RegularExpressions.Regex.Replace(
                signature,
                @"^(Public\s+|Private\s+|Protected\s+|Friend\s+)",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            sb.AppendLine($"    {signature}");
        }

        sb.AppendLine("End Interface");

        if (ImplementInterface)
        {
            sb.AppendLine();
            sb.AppendLine($"' Class {ClassName} will implement {InterfaceName}");
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
    private async Task ExtractAsync()
    {
        if (!string.IsNullOrEmpty(ErrorMessage) || _classInfo == null)
            return;

        IsLoading = true;

        try
        {
            var options = new ExtractInterfaceOptions
            {
                InterfaceName = InterfaceName,
                FileName = CreateInNewFile ? FileName : null,
                CreateInSameFile = CreateInSameFile,
                ImplementInterface = ImplementInterface,
                SelectedMembers = Members.Where(m => m.IsSelected).Select(m => m.Name).ToList(),
                Accessibility = SelectedAccessibility == "Public" ? InterfaceAccessibility.Public : InterfaceAccessibility.Friend
            };

            Result = await _refactoringService.ExtractInterfaceAsync(_filePath, _line, _column, options);

            if (Result.Success)
            {
                DialogResult = true;
                ExtractCompleted?.Invoke(this, Result);
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

public partial class MemberViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _kind = "";

    [ObservableProperty]
    private string _signature = "";

    [ObservableProperty]
    private bool _isSelected = true;
}
