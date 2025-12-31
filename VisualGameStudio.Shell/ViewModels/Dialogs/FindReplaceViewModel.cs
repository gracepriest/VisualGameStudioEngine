using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class FindReplaceViewModel : ViewModelBase
{
    private readonly Action<FindResult>? _navigateToResult;
    private readonly Func<string>? _getCurrentDocumentText;
    private readonly Func<string?>? _getCurrentDocumentPath;
    private readonly Action<string, string, bool, bool, bool>? _replaceInDocument;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _replaceText = "";

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _matchWholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private bool _isReplaceMode;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _currentMatchIndex = -1;

    [ObservableProperty]
    private ObservableCollection<FindResult> _results = new();

    [ObservableProperty]
    private FindResult? _selectedResult;

    public FindReplaceViewModel(
        Action<FindResult>? navigateToResult = null,
        Func<string>? getCurrentDocumentText = null,
        Func<string?>? getCurrentDocumentPath = null,
        Action<string, string, bool, bool, bool>? replaceInDocument = null)
    {
        _navigateToResult = navigateToResult;
        _getCurrentDocumentText = getCurrentDocumentText;
        _getCurrentDocumentPath = getCurrentDocumentPath;
        _replaceInDocument = replaceInDocument;
    }

    [RelayCommand]
    private void FindNext()
    {
        if (string.IsNullOrEmpty(SearchText)) return;

        var text = _getCurrentDocumentText?.Invoke() ?? "";
        var path = _getCurrentDocumentPath?.Invoke() ?? "Untitled";

        if (string.IsNullOrEmpty(text))
        {
            StatusText = "No document open";
            return;
        }

        Results.Clear();
        var matches = FindMatches(text, SearchText, MatchCase, MatchWholeWord, UseRegex);

        foreach (var match in matches)
        {
            Results.Add(new FindResult
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Line = GetLineNumber(text, match.Index),
                Column = GetColumnNumber(text, match.Index),
                StartOffset = match.Index,
                Length = match.Length,
                PreviewText = GetPreviewText(text, match.Index, match.Length)
            });
        }

        if (Results.Count > 0)
        {
            CurrentMatchIndex = 0;
            SelectedResult = Results[0];
            _navigateToResult?.Invoke(Results[0]);
            StatusText = $"Match 1 of {Results.Count}";
        }
        else
        {
            StatusText = "No matches found";
        }
    }

    [RelayCommand]
    private void FindPrevious()
    {
        if (Results.Count == 0)
        {
            FindNext();
            return;
        }

        CurrentMatchIndex--;
        if (CurrentMatchIndex < 0)
            CurrentMatchIndex = Results.Count - 1;

        SelectedResult = Results[CurrentMatchIndex];
        _navigateToResult?.Invoke(Results[CurrentMatchIndex]);
        StatusText = $"Match {CurrentMatchIndex + 1} of {Results.Count}";
    }

    [RelayCommand]
    private void FindNextMatch()
    {
        if (Results.Count == 0)
        {
            FindNext();
            return;
        }

        CurrentMatchIndex++;
        if (CurrentMatchIndex >= Results.Count)
            CurrentMatchIndex = 0;

        SelectedResult = Results[CurrentMatchIndex];
        _navigateToResult?.Invoke(Results[CurrentMatchIndex]);
        StatusText = $"Match {CurrentMatchIndex + 1} of {Results.Count}";
    }

    [RelayCommand]
    private void Replace()
    {
        if (string.IsNullOrEmpty(SearchText)) return;
        if (SelectedResult == null)
        {
            FindNext();
            return;
        }

        _replaceInDocument?.Invoke(SearchText, ReplaceText, MatchCase, MatchWholeWord, UseRegex);

        // Find next after replace
        FindNext();
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (string.IsNullOrEmpty(SearchText)) return;

        var text = _getCurrentDocumentText?.Invoke() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            StatusText = "No document open";
            return;
        }

        var matches = FindMatches(text, SearchText, MatchCase, MatchWholeWord, UseRegex);
        var count = matches.Count;

        if (count > 0)
        {
            // Replace all occurrences
            for (int i = count - 1; i >= 0; i--)
            {
                _replaceInDocument?.Invoke(SearchText, ReplaceText, MatchCase, MatchWholeWord, UseRegex);
            }
            StatusText = $"Replaced {count} occurrences";
        }
        else
        {
            StatusText = "No matches found";
        }

        Results.Clear();
    }

    [RelayCommand]
    private void ToggleReplaceMode()
    {
        IsReplaceMode = !IsReplaceMode;
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
            // Invalid regex, return empty
        }

        return results;
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

    private static string GetPreviewText(string text, int offset, int length)
    {
        // Get the line containing the match
        int lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
            lineStart--;

        int lineEnd = offset + length;
        while (lineEnd < text.Length && text[lineEnd] != '\n')
            lineEnd++;

        var line = text.Substring(lineStart, lineEnd - lineStart).Trim();
        if (line.Length > 100)
        {
            // Truncate long lines
            var matchPos = offset - lineStart;
            var start = Math.Max(0, matchPos - 40);
            var end = Math.Min(line.Length, matchPos + length + 40);
            line = (start > 0 ? "..." : "") + line.Substring(start, end - start) + (end < line.Length ? "..." : "");
        }
        return line;
    }
}

public class FindResult
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public int StartOffset { get; set; }
    public int Length { get; set; }
    public string PreviewText { get; set; } = "";

    public string DisplayText => $"{FileName}({Line},{Column}): {PreviewText}";
}
