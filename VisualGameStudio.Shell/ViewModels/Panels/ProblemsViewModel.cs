using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// VS Code-style Problems panel ViewModel that aggregates diagnostics from
/// LSP, the build system, and the runtime.
/// </summary>
public partial class ProblemsViewModel : ViewModelBase
{
    private List<ProblemItemViewModel> _allProblems = new();

    [ObservableProperty]
    private ObservableCollection<ProblemItemViewModel> _filteredProblems = new();

    [ObservableProperty]
    private ObservableCollection<ProblemFileGroupViewModel> _groupedProblems = new();

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _infoCount;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _showWarnings = true;

    [ObservableProperty]
    private bool _showInfo = true;

    [ObservableProperty]
    private ProblemItemViewModel? _selectedProblem;

    [ObservableProperty]
    private string? _currentFileFilter;

    [ObservableProperty]
    private bool _isFilterByCurrentFile;

    [ObservableProperty]
    private string _statusText = "No problems detected";

    [ObservableProperty]
    private bool _hasProblems;

    [ObservableProperty]
    private bool _isGroupedByFile = true;

    /// <summary>
    /// Raised when the user double-clicks or activates a problem to navigate to it.
    /// </summary>
    public event EventHandler<ProblemItemViewModel>? NavigateToProblemRequested;

    /// <summary>
    /// Updates the problems list from a collection of diagnostics.
    /// Called from LSP, build, or runtime diagnostic sources.
    /// </summary>
    public void UpdateDiagnostics(IEnumerable<DiagnosticItem> diagnostics, string source = "BasicLang")
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Remove existing problems from this source, keep others
            _allProblems.RemoveAll(p => p.Source == source);

            // Add new problems
            foreach (var d in diagnostics)
            {
                _allProblems.Add(new ProblemItemViewModel(d, source));
            }

            RefreshCounts();
            RefreshFiltered();
        });
    }

    /// <summary>
    /// Updates the problems list, replacing all problems (from all sources).
    /// Used when diagnostics come from a build that covers the whole project.
    /// </summary>
    public void ReplaceAllDiagnostics(IEnumerable<DiagnosticItem> diagnostics, string source = "BasicLang")
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _allProblems.Clear();

            foreach (var d in diagnostics)
            {
                _allProblems.Add(new ProblemItemViewModel(d, source));
            }

            RefreshCounts();
            RefreshFiltered();
        });
    }

    /// <summary>
    /// Adds a single problem (e.g., from a runtime exception event).
    /// </summary>
    public void AddProblem(DiagnosticItem diagnostic, string source = "Runtime")
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _allProblems.Add(new ProblemItemViewModel(diagnostic, source));
            RefreshCounts();
            RefreshFiltered();
        });
    }

    [RelayCommand]
    private void NavigateToProblem()
    {
        if (SelectedProblem != null)
        {
            NavigateToProblemRequested?.Invoke(this, SelectedProblem);
        }
    }

    [RelayCommand]
    private void CopyMessage()
    {
        if (SelectedProblem != null)
        {
            _ = CopyToClipboardAsync(SelectedProblem.Message);
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedProblem?.FilePath != null)
        {
            _ = CopyToClipboardAsync(SelectedProblem.FilePath);
        }
    }

    [RelayCommand]
    private void ClearProblems()
    {
        _allProblems.Clear();
        RefreshCounts();
        RefreshFiltered();
    }

    [RelayCommand]
    private void ToggleErrors()
    {
        ShowErrors = !ShowErrors;
    }

    [RelayCommand]
    private void ToggleWarnings()
    {
        ShowWarnings = !ShowWarnings;
    }

    [RelayCommand]
    private void ToggleInfo()
    {
        ShowInfo = !ShowInfo;
    }

    [RelayCommand]
    private void FilterByCurrentFile()
    {
        IsFilterByCurrentFile = true;
        RefreshFiltered();
    }

    [RelayCommand]
    private void ShowAllFiles()
    {
        IsFilterByCurrentFile = false;
        CurrentFileFilter = null;
        RefreshFiltered();
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in GroupedProblems)
        {
            group.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var group in GroupedProblems)
        {
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void ToggleGroupByFile()
    {
        IsGroupedByFile = !IsGroupedByFile;
        RefreshFiltered();
    }

    /// <summary>
    /// Sets the current file path for the "current file" filter.
    /// Called when the active editor document changes.
    /// </summary>
    public void SetCurrentFile(string? filePath)
    {
        CurrentFileFilter = filePath;
        if (IsFilterByCurrentFile)
        {
            RefreshFiltered();
        }
    }

    // Property change handlers that trigger re-filtering
    partial void OnShowErrorsChanged(bool value) => RefreshFiltered();
    partial void OnShowWarningsChanged(bool value) => RefreshFiltered();
    partial void OnShowInfoChanged(bool value) => RefreshFiltered();
    partial void OnFilterTextChanged(string value) => RefreshFiltered();

    private void RefreshCounts()
    {
        ErrorCount = _allProblems.Count(p => p.Severity == DiagnosticSeverity.Error);
        WarningCount = _allProblems.Count(p => p.Severity == DiagnosticSeverity.Warning);
        InfoCount = _allProblems.Count(p => p.Severity == DiagnosticSeverity.Info || p.Severity == DiagnosticSeverity.Hidden);
        HasProblems = _allProblems.Count > 0;
    }

    private void RefreshFiltered()
    {
        var filtered = _allProblems.Where(p =>
        {
            // Severity filter
            var severityOk = p.Severity switch
            {
                DiagnosticSeverity.Error => ShowErrors,
                DiagnosticSeverity.Warning => ShowWarnings,
                DiagnosticSeverity.Info => ShowInfo,
                DiagnosticSeverity.Hidden => ShowInfo,
                _ => true
            };
            if (!severityOk) return false;

            // File filter
            if (IsFilterByCurrentFile && CurrentFileFilter != null)
            {
                if (!string.Equals(p.FilePath, CurrentFileFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Text filter
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var search = FilterText.Trim();

                // Support negation with ! prefix (e.g., "!info")
                if (search.StartsWith("!"))
                {
                    var exclude = search.Substring(1);
                    if (string.IsNullOrWhiteSpace(exclude)) return true;
                    return !p.Message.Contains(exclude, StringComparison.OrdinalIgnoreCase)
                        && !p.Code.Contains(exclude, StringComparison.OrdinalIgnoreCase)
                        && !p.Source.Contains(exclude, StringComparison.OrdinalIgnoreCase)
                        && !p.FileName.Contains(exclude, StringComparison.OrdinalIgnoreCase);
                }

                return p.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || p.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || p.Source.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || p.FileName.Contains(search, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }).ToList();

        // Update flat list
        FilteredProblems.Clear();
        foreach (var p in filtered)
        {
            FilteredProblems.Add(p);
        }

        // Update grouped list
        GroupedProblems.Clear();
        if (IsGroupedByFile)
        {
            var groups = filtered
                .GroupBy(p => p.FileGroupKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var fileGroup = new ProblemFileGroupViewModel(
                    group.Key,
                    group.Key != "(no file)" ? Path.GetFileName(group.Key) : "(no file)",
                    group.OrderBy(p => p.Line).ThenBy(p => p.Column).ToList());
                GroupedProblems.Add(fileGroup);
            }
        }

        // Update status text
        var total = _allProblems.Count;
        var shown = filtered.Count;
        if (total == 0)
        {
            StatusText = "No problems detected";
        }
        else if (shown == total)
        {
            StatusText = $"{total} problem{(total != 1 ? "s" : "")}";
        }
        else
        {
            StatusText = $"Showing {shown} of {total} problems";
        }
    }

    /// <summary>
    /// Gets the next problem in the list (for F8 cycling).
    /// </summary>
    public ProblemItemViewModel? GetNextProblem()
    {
        if (FilteredProblems.Count == 0) return null;

        var currentIndex = SelectedProblem != null ? FilteredProblems.IndexOf(SelectedProblem) : -1;
        var nextIndex = (currentIndex + 1) % FilteredProblems.Count;
        SelectedProblem = FilteredProblems[nextIndex];
        return SelectedProblem;
    }

    /// <summary>
    /// Gets the previous problem in the list (for Shift+F8 cycling).
    /// </summary>
    public ProblemItemViewModel? GetPreviousProblem()
    {
        if (FilteredProblems.Count == 0) return null;

        var currentIndex = SelectedProblem != null ? FilteredProblems.IndexOf(SelectedProblem) : 0;
        var prevIndex = currentIndex - 1;
        if (prevIndex < 0) prevIndex = FilteredProblems.Count - 1;
        SelectedProblem = FilteredProblems[prevIndex];
        return SelectedProblem;
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                }
            }
        }
        catch
        {
            // Clipboard access can fail in some environments
        }
    }
}

/// <summary>
/// Represents a group of problems from the same file, for grouped display.
/// </summary>
public partial class ProblemFileGroupViewModel : ObservableObject
{
    public ProblemFileGroupViewModel(string filePath, string fileName, List<ProblemItemViewModel> problems)
    {
        FilePath = filePath;
        FileName = fileName;
        Problems = new ObservableCollection<ProblemItemViewModel>(problems);
        ProblemCount = problems.Count;
        IsExpanded = true;
    }

    public string FilePath { get; }
    public string FileName { get; }
    public ObservableCollection<ProblemItemViewModel> Problems { get; }
    public int ProblemCount { get; }

    [ObservableProperty]
    private bool _isExpanded = true;
}

/// <summary>
/// Converters used by ProblemsView.axaml.
/// </summary>
public static class ProblemsView_Converters
{
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, string> FileScopeTextConverter =
        new(isFiltered => isFiltered ? "File" : "Workspace");
}
