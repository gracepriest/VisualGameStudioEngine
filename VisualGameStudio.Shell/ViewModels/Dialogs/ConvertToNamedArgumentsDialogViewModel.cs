using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ConvertToNamedArgumentsDialogViewModel : ObservableObject
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

    public ObservableCollection<ArgumentViewModel> Arguments { get; } = new();

    public bool CanConvert => Arguments.Any(a => a.IsSelected && !a.IsAlreadyNamed) &&
                               string.IsNullOrEmpty(ErrorMessage);

    public int SelectedCount => Arguments.Count(a => a.IsSelected && !a.IsAlreadyNamed);

    public ConvertToNamedArgumentsDialogViewModel(
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
            if (!arg.IsAlreadyNamed)
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
                var vm = new ArgumentViewModel(
                    arg.Index,
                    arg.ParameterName,
                    arg.ParameterType,
                    arg.Value,
                    arg.IsNamed);
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ArgumentViewModel.IsSelected))
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
            else if (Arguments.All(a => a.IsAlreadyNamed))
            {
                ErrorMessage = "All arguments are already named.";
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

        // Build preview with selected arguments converted
        var args = new List<string>();
        foreach (var arg in Arguments)
        {
            if ((arg.IsSelected || arg.IsAlreadyNamed) && !string.IsNullOrEmpty(arg.ParameterName))
            {
                args.Add($"{arg.ParameterName}:={arg.Value}");
            }
            else
            {
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
                .Where(a => a.IsSelected && !a.IsAlreadyNamed)
                .Select(a => a.Index)
                .ToList();

            var options = new ConvertToNamedArgumentsOptions
            {
                ConvertAll = false,
                ArgumentIndices = selectedIndices
            };

            var result = await _refactoringService.ConvertToNamedArgumentsAsync(
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

public partial class ArgumentViewModel : ObservableObject
{
    public int Index { get; }
    public string ParameterName { get; }
    public string? ParameterType { get; }
    public string Value { get; }
    public bool IsAlreadyNamed { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayText
    {
        get
        {
            var typeInfo = !string.IsNullOrEmpty(ParameterType) ? $" As {ParameterType}" : "";
            if (IsAlreadyNamed)
            {
                return $"{ParameterName}:={Value}{typeInfo} (already named)";
            }
            return $"{ParameterName}{typeInfo} = {Value}";
        }
    }

    public ArgumentViewModel(int index, string parameterName, string? parameterType, string value, bool isAlreadyNamed)
    {
        Index = index;
        ParameterName = parameterName;
        ParameterType = parameterType;
        Value = value;
        IsAlreadyNamed = isAlreadyNamed;
        IsSelected = !isAlreadyNamed; // Select by default if not already named
    }
}
