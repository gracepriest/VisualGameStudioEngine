using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ConvertToPositionalArgumentsDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;

    [ObservableProperty]
    private string _methodName = "";

    [ObservableProperty]
    private string _originalCall = "";

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _convertAll = true;

    public ObservableCollection<PositionalArgumentViewModel> Arguments { get; } = new();

    public bool CanConvert => Arguments.Any(a => a.IsSelected && a.IsNamed) &&
                               string.IsNullOrEmpty(ErrorMessage);

    public int SelectedCount => Arguments.Count(a => a.IsSelected && a.IsNamed);

    public ConvertToPositionalArgumentsDialogViewModel(
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

    partial void OnConvertAllChanged(bool value)
    {
        foreach (var arg in Arguments)
        {
            if (arg.IsNamed)
            {
                arg.IsSelected = value;
            }
        }
        UpdatePreview();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var callSiteInfo = await _refactoringService.GetCallSiteInfoAsync(
                _filePath, _line, _column);

            if (callSiteInfo == null)
            {
                ErrorMessage = "Could not find method call at cursor position.";
                return;
            }

            MethodName = callSiteInfo.MethodName;
            OriginalCall = callSiteInfo.OriginalCall;

            Arguments.Clear();

            foreach (var arg in callSiteInfo.Arguments)
            {
                var vm = new PositionalArgumentViewModel(
                    arg.Index,
                    arg.ParameterName,
                    arg.ParameterType,
                    arg.Value,
                    arg.IsNamed);
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(PositionalArgumentViewModel.IsSelected))
                    {
                        UpdatePreview();
                        OnPropertyChanged(nameof(CanConvert));
                        OnPropertyChanged(nameof(SelectedCount));
                    }
                };
                Arguments.Add(vm);
            }

            if (Arguments.Count == 0)
            {
                ErrorMessage = "Method call has no arguments.";
            }
            else if (!callSiteInfo.HasNamedArguments)
            {
                ErrorMessage = "No named arguments to convert. All arguments are already positional.";
            }

            UpdatePreview();
            OnPropertyChanged(nameof(CanConvert));
            OnPropertyChanged(nameof(SelectedCount));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading call site info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(OriginalCall) || Arguments.Count == 0)
        {
            Preview = "";
            return;
        }

        // Build preview with selected named arguments converted to positional
        // Arguments must be sorted by index for positional ordering
        var sortedArgs = Arguments.OrderBy(a => a.Index).ToList();
        var args = new List<string>();

        foreach (var arg in sortedArgs)
        {
            if (arg.IsNamed && arg.IsSelected)
            {
                // Convert to positional - just the value
                args.Add(arg.Value);
            }
            else if (arg.IsNamed)
            {
                // Keep as named (not selected for conversion)
                args.Add($"{arg.ParameterName}:={arg.Value}");
            }
            else
            {
                // Already positional
                args.Add(arg.Value);
            }
        }

        Preview = $"{MethodName}({string.Join(", ", args)})";
        OnPropertyChanged(nameof(CanConvert));
    }

    [RelayCommand]
    private void SelectAll()
    {
        ConvertAll = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        ConvertAll = false;
        foreach (var arg in Arguments)
        {
            arg.IsSelected = false;
        }
        UpdatePreview();
    }

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (!CanConvert)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var selectedIndices = Arguments
                .Where(a => a.IsSelected && a.IsNamed)
                .Select(a => a.Index)
                .ToList();

            var options = new ConvertToPositionalArgumentsOptions
            {
                ConvertAll = false,
                ArgumentIndices = selectedIndices
            };

            var result = await _refactoringService.ConvertToPositionalArgumentsAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to convert arguments.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error converting arguments: {ex.Message}";
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

public partial class PositionalArgumentViewModel : ObservableObject
{
    public int Index { get; }
    public string ParameterName { get; }
    public string? ParameterType { get; }
    public string Value { get; }
    public bool IsNamed { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayText
    {
        get
        {
            var typeInfo = !string.IsNullOrEmpty(ParameterType) ? $" As {ParameterType}" : "";
            if (IsNamed)
            {
                return $"{ParameterName}:={Value}{typeInfo}";
            }
            return $"{Value}{typeInfo} (already positional)";
        }
    }

    public PositionalArgumentViewModel(int index, string parameterName, string? parameterType, string value, bool isNamed)
    {
        Index = index;
        ParameterName = parameterName;
        ParameterType = parameterType;
        Value = value;
        IsNamed = isNamed;
        IsSelected = isNamed; // Select by default if named (eligible for conversion)
    }
}
