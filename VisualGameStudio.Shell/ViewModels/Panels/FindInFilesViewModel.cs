using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class FindInFilesViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;
    private readonly Action<string, int>? _openFileAtLine;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _replaceText = "";

    [ObservableProperty]
    private string _fileFilter = "*.bas;*.bl;*.basic";

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _matchWholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ObservableCollection<FindResultGroup> _resultGroups = new();

    [ObservableProperty]
    private FindResult? _selectedResult;

    public FindInFilesViewModel(
        IProjectService projectService,
        IFileService fileService,
        Action<string, int>? openFileAtLine = null)
    {
        _projectService = projectService;
        _fileService = fileService;
        _openFileAtLine = openFileAtLine;
    }

    /// <summary>
    /// Sets the navigation callback for opening files at specific lines.
    /// Used when the view model is created via DI without the callback.
    /// </summary>
    public void SetNavigationCallback(Action<string, int> openFileAtLine)
    {
        // Use reflection to set the readonly field, or store in a mutable field
        _openFileAtLineCallback = openFileAtLine;
    }

    private Action<string, int>? _openFileAtLineCallback;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        if (_projectService.CurrentProject == null)
        {
            StatusText = "No project open";
            return;
        }

        // Cancel any existing search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        ResultGroups.Clear();
        StatusText = "Searching...";

        try
        {
            var projectPath = Path.GetDirectoryName(_projectService.CurrentProject.FilePath) ?? "";
            var filters = FileFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var totalMatches = 0;
            var filesSearched = 0;

            foreach (var filter in filters)
            {
                var pattern = filter.Trim();
                var files = Directory.GetFiles(projectPath, pattern, SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) break;

                    var content = await File.ReadAllTextAsync(file, token);
                    var matches = FindMatches(content, SearchText, MatchCase, MatchWholeWord, UseRegex);

                    if (matches.Count > 0)
                    {
                        var group = new FindResultGroup
                        {
                            FilePath = file,
                            FileName = Path.GetFileName(file),
                            MatchCount = matches.Count
                        };

                        foreach (var match in matches)
                        {
                            var line = GetLineNumber(content, match.Index);
                            var column = GetColumnNumber(content, match.Index);
                            var preview = GetLineText(content, match.Index);

                            group.Results.Add(new FindResult
                            {
                                FilePath = file,
                                FileName = Path.GetFileName(file),
                                Line = line,
                                Column = column,
                                StartOffset = match.Index,
                                Length = match.Length,
                                PreviewText = preview
                            });
                        }

                        ResultGroups.Add(group);
                        totalMatches += matches.Count;
                    }

                    filesSearched++;
                    StatusText = $"Searching... ({filesSearched} files)";
                }
            }

            StatusText = $"Found {totalMatches} matches in {ResultGroups.Count} files";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void CancelSearch()
    {
        _searchCts?.Cancel();
    }

    [RelayCommand]
    private void NavigateToResult(FindResult? result)
    {
        if (result == null) return;
        var callback = _openFileAtLineCallback ?? _openFileAtLine;
        callback?.Invoke(result.FilePath, result.Line);
    }

    [RelayCommand]
    private async Task ReplaceAllAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        if (ResultGroups.Count == 0)
        {
            await SearchAsync();
            if (ResultGroups.Count == 0) return;
        }

        var totalReplaced = 0;

        foreach (var group in ResultGroups)
        {
            try
            {
                var content = await File.ReadAllTextAsync(group.FilePath);
                var newContent = ReplaceAll(content, SearchText, ReplaceText, MatchCase, MatchWholeWord, UseRegex);

                if (content != newContent)
                {
                    await File.WriteAllTextAsync(group.FilePath, newContent);
                    totalReplaced += group.MatchCount;
                }
            }
            catch
            {
                // Skip files that can't be modified
            }
        }

        StatusText = $"Replaced {totalReplaced} occurrences";
        ResultGroups.Clear();
    }

    partial void OnSelectedResultChanged(FindResult? value)
    {
        if (value != null)
        {
            NavigateToResult(value);
        }
    }

    private static List<Match> FindMatches(string text, string searchText, bool matchCase, bool matchWholeWord, bool useRegex)
    {
        var results = new List<Match>();

        try
        {
            var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            string pattern;

            if (useRegex)
            {
                pattern = searchText;
            }
            else
            {
                pattern = Regex.Escape(searchText);
            }

            if (matchWholeWord)
            {
                pattern = $@"\b{pattern}\b";
            }

            var regex = new Regex(pattern, options);
            var matches = regex.Matches(text);

            foreach (Match match in matches)
            {
                results.Add(match);
            }
        }
        catch (RegexParseException)
        {
            // Invalid regex
        }

        return results;
    }

    private static string ReplaceAll(string text, string searchText, string replaceText, bool matchCase, bool matchWholeWord, bool useRegex)
    {
        try
        {
            var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            string pattern;

            if (useRegex)
            {
                pattern = searchText;
            }
            else
            {
                pattern = Regex.Escape(searchText);
            }

            if (matchWholeWord)
            {
                pattern = $@"\b{pattern}\b";
            }

            var regex = new Regex(pattern, options);
            return regex.Replace(text, replaceText);
        }
        catch
        {
            return text;
        }
    }

    private static int GetLineNumber(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }
        return line;
    }

    private static int GetColumnNumber(string text, int offset)
    {
        int column = 1;
        for (int i = offset - 1; i >= 0; i--)
        {
            if (text[i] == '\n') break;
            column++;
        }
        return column;
    }

    private static string GetLineText(string text, int offset)
    {
        int lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
            lineStart--;

        int lineEnd = offset;
        while (lineEnd < text.Length && text[lineEnd] != '\n')
            lineEnd++;

        var line = text.Substring(lineStart, lineEnd - lineStart).Trim();
        if (line.Length > 150)
        {
            line = line.Substring(0, 147) + "...";
        }
        return line;
    }
}

public class FindResultGroup
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int MatchCount { get; set; }
    public ObservableCollection<FindResult> Results { get; } = new();

    public string DisplayText => $"{FileName} ({MatchCount} matches)";
}
