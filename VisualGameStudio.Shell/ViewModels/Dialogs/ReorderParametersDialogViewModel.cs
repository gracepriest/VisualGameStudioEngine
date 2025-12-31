using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class ReorderParametersDialogViewModel : ObservableObject
{
    private readonly IRefactoringService _refactoringService;
    private readonly string _filePath;
    private readonly int _line;
    private readonly int _column;
    private Action<bool>? _closeAction;
    private List<int> _originalOrder = new();

    [ObservableProperty]
    private string _methodName = "";

    [ObservableProperty]
    private string _containingType = "";

    [ObservableProperty]
    private string _currentSignature = "";

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _callSiteCount;

    [ObservableProperty]
    private bool _updateCallSites = true;

    [ObservableProperty]
    private ReorderableParameterViewModel? _selectedParameter;

    public ObservableCollection<ReorderableParameterViewModel> Parameters { get; } = new();

    public bool HasOrderChanged
    {
        get
        {
            if (Parameters.Count != _originalOrder.Count)
                return false;

            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].OriginalIndex != _originalOrder[i])
                    return true;
            }
            return false;
        }
    }

    public bool CanMoveUp => SelectedParameter != null && Parameters.IndexOf(SelectedParameter) > 0;
    public bool CanMoveDown => SelectedParameter != null && Parameters.IndexOf(SelectedParameter) < Parameters.Count - 1;

    public ReorderParametersDialogViewModel(
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

    partial void OnSelectedParameterChanged(ReorderableParameterViewModel? value)
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var methodInfo = await _refactoringService.GetMethodForParameterAsync(
                _filePath, _line, _column);

            if (methodInfo == null)
            {
                ErrorMessage = "Could not find method at cursor position.";
                return;
            }

            MethodName = methodInfo.MethodName;
            ContainingType = methodInfo.ContainingType;
            CurrentSignature = methodInfo.Signature;
            CallSiteCount = methodInfo.CallSiteCount;

            Parameters.Clear();
            _originalOrder.Clear();

            for (int i = 0; i < methodInfo.ExistingParameters.Count; i++)
            {
                var param = methodInfo.ExistingParameters[i];
                var vm = new ReorderableParameterViewModel(i, param.Name, param.Type ?? "Object", param.IsOptional)
                {
                    DefaultValue = param.DefaultValue
                };
                Parameters.Add(vm);
                _originalOrder.Add(i);
            }

            if (Parameters.Count < 2)
            {
                ErrorMessage = "This method needs at least 2 parameters to reorder.";
            }

            UpdatePreview();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading method info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(CurrentSignature) || Parameters.Count == 0)
        {
            Preview = "";
            OnPropertyChanged(nameof(HasOrderChanged));
            return;
        }

        // Generate preview based on current parameter order
        var signatureParts = ParseSignature(CurrentSignature);
        if (signatureParts == null)
        {
            Preview = CurrentSignature;
            OnPropertyChanged(nameof(HasOrderChanged));
            return;
        }

        var (prefix, _, suffix) = signatureParts.Value;

        // Build new parameter list from current order
        var reorderedParams = Parameters.Select(p => p.FullDeclaration).ToList();
        Preview = $"{prefix}({string.Join(", ", reorderedParams)}){suffix}";

        OnPropertyChanged(nameof(HasOrderChanged));
    }

    private (string prefix, List<string> parameters, string suffix)? ParseSignature(string signature)
    {
        var openParen = signature.IndexOf('(');
        var closeParen = signature.LastIndexOf(')');

        if (openParen < 0 || closeParen < 0 || closeParen <= openParen)
            return null;

        var prefix = signature.Substring(0, openParen);
        var suffix = closeParen < signature.Length - 1 ? signature.Substring(closeParen + 1) : "";
        var paramsSection = signature.Substring(openParen + 1, closeParen - openParen - 1);

        var parameters = SplitParameters(paramsSection);
        return (prefix, parameters, suffix);
    }

    private List<string> SplitParameters(string paramsSection)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(paramsSection))
            return result;

        var current = "";
        var parenDepth = 0;

        foreach (var ch in paramsSection)
        {
            if (ch == '(') parenDepth++;
            else if (ch == ')') parenDepth--;
            else if (ch == ',' && parenDepth == 0)
            {
                result.Add(current.Trim());
                current = "";
                continue;
            }
            current += ch;
        }

        if (!string.IsNullOrWhiteSpace(current))
            result.Add(current.Trim());

        return result;
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedParameter == null) return;

        var index = Parameters.IndexOf(SelectedParameter);
        if (index <= 0) return;

        Parameters.Move(index, index - 1);
        UpdatePreview();
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedParameter == null) return;

        var index = Parameters.IndexOf(SelectedParameter);
        if (index < 0 || index >= Parameters.Count - 1) return;

        Parameters.Move(index, index + 1);
        UpdatePreview();
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    [RelayCommand]
    private void MoveToTop()
    {
        if (SelectedParameter == null) return;

        var index = Parameters.IndexOf(SelectedParameter);
        if (index <= 0) return;

        Parameters.Move(index, 0);
        UpdatePreview();
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    [RelayCommand]
    private void MoveToBottom()
    {
        if (SelectedParameter == null) return;

        var index = Parameters.IndexOf(SelectedParameter);
        if (index < 0 || index >= Parameters.Count - 1) return;

        Parameters.Move(index, Parameters.Count - 1);
        UpdatePreview();
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    [RelayCommand]
    private void Reset()
    {
        // Restore original order
        var sortedByOriginal = Parameters.OrderBy(p => p.OriginalIndex).ToList();
        Parameters.Clear();
        foreach (var param in sortedByOriginal)
        {
            Parameters.Add(param);
        }

        if (Parameters.Count > 0)
            SelectedParameter = Parameters[0];

        UpdatePreview();
    }

    [RelayCommand]
    private async Task ReorderParametersAsync()
    {
        if (!HasOrderChanged)
        {
            _closeAction?.Invoke(false);
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Build the new order: current position -> original index
            var newOrder = Parameters.Select(p => p.OriginalIndex).ToList();

            var options = new ReorderParametersOptions
            {
                NewOrder = newOrder,
                UpdateCallSites = UpdateCallSites
            };

            var result = await _refactoringService.ReorderParametersAsync(
                _filePath, _line, _column, options);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Failed to reorder parameters.";
                return;
            }

            _closeAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error reordering parameters: {ex.Message}";
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

public partial class ReorderableParameterViewModel : ObservableObject
{
    public int OriginalIndex { get; }
    public string Name { get; }
    public string Type { get; }
    public bool IsOptional { get; }
    public string? DefaultValue { get; set; }

    public string DisplayText => $"[{OriginalIndex}] {Name} As {Type}";

    public string FullDeclaration
    {
        get
        {
            var decl = $"{Name} As {Type}";
            if (IsOptional && !string.IsNullOrEmpty(DefaultValue))
            {
                decl += $" = {DefaultValue}";
            }
            return decl;
        }
    }

    public ReorderableParameterViewModel(int originalIndex, string name, string type, bool isOptional)
    {
        OriginalIndex = originalIndex;
        Name = name;
        Type = type;
        IsOptional = isOptional;
    }
}
