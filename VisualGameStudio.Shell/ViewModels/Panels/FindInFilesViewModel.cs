using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class FindInFilesViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly FileSearchService _fileSearchService;
    private Action<string, int>? _openFileAtLine;
    private Action<string, int, int>? _openFileAtLineColumn;
    private CancellationTokenSource? _searchCts;
    private System.Timers.Timer? _debounceTimer;

    // === Search Input Properties ===

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _replaceText = "";

    [ObservableProperty]
    private bool _isRegex;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _matchWholeWord;

    [ObservableProperty]
    private bool _preserveCase;

    [ObservableProperty]
    private string _includePattern = "";

    [ObservableProperty]
    private string _excludePattern = "";

    [ObservableProperty]
    private bool _showFilters;

    // === Results ===

    [ObservableProperty]
    private ObservableCollection<SearchResultFileViewModel> _results = new();

    [ObservableProperty]
    private int _totalMatchCount;

    [ObservableProperty]
    private int _totalFileCount;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isReplaceMode;

    [ObservableProperty]
    private string _searchSummary = "";

    [ObservableProperty]
    private string _statusText = "Type to search in files";

    [ObservableProperty]
    private bool _limitReached;

    // === Legacy compatibility (used by FindReferences) ===

    [ObservableProperty]
    private ObservableCollection<FindResultGroup> _resultGroups = new();

    [ObservableProperty]
    private FindResult? _selectedResult;

    [ObservableProperty]
    private string _fileFilter = "*.bas;*.bl;*.basic";

    [ObservableProperty]
    private bool _useRegex;

    public FindInFilesViewModel(
        IProjectService projectService,
        IFileService fileService,
        IDialogService dialogService)
    {
        _projectService = projectService;
        _fileService = fileService;
        _dialogService = dialogService;
        _fileSearchService = new FileSearchService();
    }

    /// <summary>
    /// Sets the navigation callback for opening files at specific lines.
    /// </summary>
    public void SetNavigationCallback(Action<string, int> openFileAtLine)
    {
        _openFileAtLine = openFileAtLine;
    }

    /// <summary>
    /// Sets the navigation callback for opening files at specific line and column.
    /// </summary>
    public void SetNavigationCallback(Action<string, int, int> openFileAtLineColumn)
    {
        _openFileAtLineColumn = openFileAtLineColumn;
    }

    /// <summary>
    /// Populates the search box with the given text and activates search.
    /// </summary>
    public void SetSearchText(string text, bool replaceMode = false)
    {
        SearchText = text;
        IsReplaceMode = replaceMode;
    }

    // === Commands ===

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;

        var rootPath = GetSearchRootPath();
        if (rootPath == null)
        {
            StatusText = "No project or folder open";
            return;
        }

        // Cancel any existing search
        if (_searchCts != null)
        {
            await _searchCts.CancelAsync();
        }
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        Results.Clear();
        TotalMatchCount = 0;
        TotalFileCount = 0;
        LimitReached = false;
        StatusText = "Searching...";
        SearchSummary = "";

        try
        {
            var options = new SearchOptions
            {
                IsRegex = IsRegex,
                IsCaseSensitive = MatchCase,
                IsWholeWord = MatchWholeWord,
                PreserveCase = PreserveCase,
                IncludePattern = string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
                ExcludePattern = string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern
            };

            var progress = new Progress<SearchProgressInfo>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = $"Searching... ({p.FilesSearched} files, {p.MatchesFound} matches)";
                });
            });

            await _fileSearchService.SearchAsync(rootPath, SearchText, options, fileResult =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var fileVm = new SearchResultFileViewModel
                    {
                        FilePath = fileResult.FilePath,
                        FileName = Path.GetFileName(fileResult.FilePath),
                        RelativePath = fileResult.RelativePath,
                        MatchCount = fileResult.Matches.Count
                    };

                    foreach (var match in fileResult.Matches)
                    {
                        fileVm.Matches.Add(new SearchResultMatchViewModel
                        {
                            FilePath = fileResult.FilePath,
                            LineNumber = match.LineNumber,
                            Column = match.Column,
                            MatchLength = match.MatchLength,
                            LineText = match.LineText,
                            PreviewBefore = match.PreviewBefore,
                            MatchText = match.MatchText,
                            PreviewAfter = match.PreviewAfter
                        });
                    }

                    Results.Add(fileVm);
                    TotalMatchCount += fileResult.Matches.Count;
                    TotalFileCount++;
                    UpdateSummary();
                });
            }, progress, token);

            // Final progress update
            var finalProgress = new SearchProgressInfo { IsComplete = true };
            Dispatcher.UIThread.Post(() =>
            {
                UpdateSummary();
                if (TotalMatchCount == 0)
                {
                    StatusText = "No results found";
                }
                else
                {
                    StatusText = SearchSummary;
                }
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task CancelSearchAsync()
    {
        if (_searchCts != null)
        {
            await _searchCts.CancelAsync();
        }
    }

    [RelayCommand]
    private async Task ReplaceAllAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;

        var rootPath = GetSearchRootPath();
        if (rootPath == null) return;

        // Confirm with user
        var totalCount = TotalMatchCount > 0 ? TotalMatchCount : 0;
        var message = totalCount > 0
            ? $"Replace {totalCount} occurrence{(totalCount == 1 ? "" : "s")} in {TotalFileCount} file{(TotalFileCount == 1 ? "" : "s")}?"
            : $"Replace all occurrences of \"{SearchText}\" with \"{ReplaceText}\"?";

        var confirmed = await _dialogService.ConfirmAsync("Replace All", message);
        if (!confirmed) return;

        IsSearching = true;
        StatusText = "Replacing...";

        try
        {
            var options = new SearchOptions
            {
                IsRegex = IsRegex,
                IsCaseSensitive = MatchCase,
                IsWholeWord = MatchWholeWord,
                PreserveCase = PreserveCase,
                IncludePattern = string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
                ExcludePattern = string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern
            };

            var (totalReplacements, filesModified) = await _fileSearchService.ReplaceAllAsync(
                rootPath, SearchText, ReplaceText, options);

            StatusText = $"Replaced {totalReplacements} occurrence{(totalReplacements == 1 ? "" : "s")} in {filesModified} file{(filesModified == 1 ? "" : "s")}";

            // Re-run search to refresh results
            Results.Clear();
            TotalMatchCount = 0;
            TotalFileCount = 0;
            SearchSummary = "";
        }
        catch (Exception ex)
        {
            StatusText = $"Replace error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task ReplaceInFileAsync(SearchResultFileViewModel? fileVm)
    {
        if (fileVm == null || string.IsNullOrWhiteSpace(SearchText)) return;

        try
        {
            var options = new SearchOptions
            {
                IsRegex = IsRegex,
                IsCaseSensitive = MatchCase,
                IsWholeWord = MatchWholeWord,
                PreserveCase = PreserveCase
            };

            var count = await _fileSearchService.ReplaceInFileAsync(
                fileVm.FilePath, SearchText, ReplaceText, options);

            if (count > 0)
            {
                // Remove the file from results
                TotalMatchCount -= fileVm.MatchCount;
                TotalFileCount--;
                Results.Remove(fileVm);
                UpdateSummary();
                StatusText = $"Replaced {count} occurrence{(count == 1 ? "" : "s")} in {fileVm.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Replace error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NavigateToMatch(SearchResultMatchViewModel? match)
    {
        if (match == null) return;

        if (_openFileAtLineColumn != null)
        {
            _openFileAtLineColumn(match.FilePath, match.LineNumber, match.Column);
        }
        else
        {
            _openFileAtLine?.Invoke(match.FilePath, match.LineNumber);
        }
    }

    [RelayCommand]
    private void NavigateToResult(FindResult? result)
    {
        if (result == null) return;
        _openFileAtLine?.Invoke(result.FilePath, result.Line);
    }

    [RelayCommand]
    private void DismissFileResult(SearchResultFileViewModel? fileVm)
    {
        if (fileVm == null) return;

        TotalMatchCount -= fileVm.MatchCount;
        TotalFileCount--;
        Results.Remove(fileVm);
        UpdateSummary();
    }

    [RelayCommand]
    private void DismissMatchResult(SearchResultMatchViewModel? matchVm)
    {
        if (matchVm == null) return;

        foreach (var fileResult in Results)
        {
            if (fileResult.Matches.Remove(matchVm))
            {
                TotalMatchCount--;
                fileResult.MatchCount--;
                OnPropertyChanged(nameof(fileResult.DisplayText));

                if (fileResult.Matches.Count == 0)
                {
                    TotalFileCount--;
                    Results.Remove(fileResult);
                }

                UpdateSummary();
                break;
            }
        }
    }

    [RelayCommand]
    private void ToggleRegex()
    {
        IsRegex = !IsRegex;
    }

    [RelayCommand]
    private void ToggleCase()
    {
        MatchCase = !MatchCase;
    }

    [RelayCommand]
    private void ToggleWholeWord()
    {
        MatchWholeWord = !MatchWholeWord;
    }

    [RelayCommand]
    private void TogglePreserveCase()
    {
        PreserveCase = !PreserveCase;
    }

    [RelayCommand]
    private void ToggleFilters()
    {
        ShowFilters = !ShowFilters;
    }

    [RelayCommand]
    private void ToggleReplaceMode()
    {
        IsReplaceMode = !IsReplaceMode;
    }

    [RelayCommand]
    private void ClearResults()
    {
        Results.Clear();
        TotalMatchCount = 0;
        TotalFileCount = 0;
        LimitReached = false;
        SearchSummary = "";
        StatusText = "Type to search in files";
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var file in Results)
        {
            file.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var file in Results)
        {
            file.IsExpanded = true;
        }
    }

    // === Property Change Handlers ===

    partial void OnSelectedResultChanged(FindResult? value)
    {
        if (value != null)
        {
            NavigateToResult(value);
        }
    }

    // === Helper Methods ===

    private string? GetSearchRootPath()
    {
        if (_projectService.CurrentProject != null)
        {
            return Path.GetDirectoryName(_projectService.CurrentProject.FilePath);
        }
        return null;
    }

    private void UpdateSummary()
    {
        if (TotalMatchCount == 0)
        {
            SearchSummary = "No results";
        }
        else
        {
            var matchWord = TotalMatchCount == 1 ? "result" : "results";
            var fileWord = TotalFileCount == 1 ? "file" : "files";
            SearchSummary = $"{TotalMatchCount} {matchWord} in {TotalFileCount} {fileWord}";
            if (LimitReached)
            {
                SearchSummary += " (result limit reached)";
            }
        }
        StatusText = SearchSummary;
    }

    // === Legacy compatibility for FindReferences ===

    private static List<Match> FindMatches(string text, string searchText, bool matchCase, bool matchWholeWord, bool useRegex)
    {
        var results = new List<Match>();
        try
        {
            var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            string pattern = useRegex ? searchText : Regex.Escape(searchText);
            if (matchWholeWord) pattern = $@"\b{pattern}\b";
            var regex = new Regex(pattern, options);
            foreach (Match match in regex.Matches(text)) results.Add(match);
        }
        catch (RegexParseException) { }
        return results;
    }

    private static string ReplaceAll(string text, string searchText, string replaceText, bool matchCase, bool matchWholeWord, bool useRegex)
    {
        try
        {
            var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            string pattern = useRegex ? searchText : Regex.Escape(searchText);
            if (matchWholeWord) pattern = $@"\b{pattern}\b";
            return new Regex(pattern, options).Replace(text, replaceText);
        }
        catch { return text; }
    }

    private Action<string, int>? _openFileAtLineCallback;
}

public class FindResultGroup
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int MatchCount { get; set; }
    public ObservableCollection<FindResult> Results { get; } = new();
    public string DisplayText => $"{FileName} ({MatchCount} matches)";
}
