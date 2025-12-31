using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class GoToSymbolDialogViewModel : ViewModelBase
{
    private readonly List<SymbolItem> _allSymbols = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ObservableCollection<SymbolItem> _filteredSymbols = new();

    [ObservableProperty]
    private SymbolItem? _selectedSymbol;

    [ObservableProperty]
    private string _statusText = "";

    public SymbolItem? ResultSymbol { get; private set; }
    public bool DialogResult { get; private set; }

    public event EventHandler? SymbolSelected;
    public event EventHandler? Cancelled;

    public GoToSymbolDialogViewModel()
    {
    }

    public GoToSymbolDialogViewModel(IEnumerable<DocumentSymbol> symbols, string? filePath = null)
    {
        LoadSymbols(symbols, filePath);
        FilterSymbols("");
    }

    public GoToSymbolDialogViewModel(string sourceCode, string? filePath = null)
    {
        var symbols = ParseSymbolsFromSource(sourceCode);
        LoadSymbols(symbols, filePath);
        FilterSymbols("");
    }

    private void LoadSymbols(IEnumerable<DocumentSymbol> symbols, string? filePath)
    {
        _allSymbols.Clear();
        FlattenSymbols(symbols, filePath, null);
        StatusText = $"{_allSymbols.Count} symbol(s)";
    }

    private void FlattenSymbols(IEnumerable<DocumentSymbol> symbols, string? filePath, string? containerName)
    {
        foreach (var symbol in symbols)
        {
            var displayName = containerName != null ? $"{containerName}.{symbol.Name}" : symbol.Name;

            _allSymbols.Add(new SymbolItem
            {
                Name = symbol.Name,
                DisplayName = displayName,
                Detail = symbol.Detail,
                Kind = symbol.Kind,
                Line = symbol.Line,
                Column = symbol.Column,
                FilePath = filePath,
                Icon = GetSymbolIcon(symbol.Kind)
            });

            if (symbol.Children.Count > 0)
            {
                FlattenSymbols(symbol.Children, filePath, displayName);
            }
        }
    }

    private static string GetSymbolIcon(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Class => "C",
            SymbolKind.Module => "M",
            SymbolKind.Function => "F",
            SymbolKind.Method => "m",
            SymbolKind.Property => "P",
            SymbolKind.Field => "f",
            SymbolKind.Variable => "v",
            SymbolKind.Constant => "c",
            SymbolKind.Enum => "E",
            SymbolKind.Interface => "I",
            SymbolKind.Struct => "S",
            SymbolKind.Constructor => "C",
            SymbolKind.Namespace => "N",
            _ => "?"
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterSymbols(value);
    }

    private void FilterSymbols(string searchText)
    {
        FilteredSymbols.Clear();

        var query = searchText.Trim();
        IEnumerable<SymbolItem> results;

        if (string.IsNullOrEmpty(query))
        {
            results = _allSymbols.Take(100); // Limit initial display
        }
        else
        {
            // Support fuzzy matching with camelCase
            results = _allSymbols
                .Where(s => MatchesSearch(s, query))
                .OrderByDescending(s => GetMatchScore(s, query))
                .Take(100);
        }

        foreach (var symbol in results)
        {
            FilteredSymbols.Add(symbol);
        }

        if (FilteredSymbols.Count > 0 && SelectedSymbol == null)
        {
            SelectedSymbol = FilteredSymbols[0];
        }

        StatusText = $"{FilteredSymbols.Count} of {_allSymbols.Count} symbol(s)";
    }

    private static bool MatchesSearch(SymbolItem symbol, string query)
    {
        // Case-insensitive contains
        if (symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        if (symbol.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        // CamelCase matching (e.g., "gds" matches "GetDocumentSymbols")
        if (MatchesCamelCase(symbol.Name, query))
            return true;

        return false;
    }

    private static bool MatchesCamelCase(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;

        var queryIndex = 0;
        var queryLower = query.ToLowerInvariant();

        foreach (var c in text)
        {
            if (char.ToLowerInvariant(c) == queryLower[queryIndex])
            {
                queryIndex++;
                if (queryIndex >= queryLower.Length)
                    return true;
            }
        }

        return false;
    }

    private static int GetMatchScore(SymbolItem symbol, string query)
    {
        var score = 0;
        var nameLower = symbol.Name.ToLowerInvariant();
        var queryLower = query.ToLowerInvariant();

        // Exact match gets highest score
        if (nameLower == queryLower) score += 100;
        // Starts with query
        else if (nameLower.StartsWith(queryLower)) score += 50;
        // Contains query
        else if (nameLower.Contains(queryLower)) score += 25;

        // Prefer shorter names (more specific matches)
        score -= symbol.Name.Length / 10;

        // Prefer certain symbol types
        if (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Module)
            score += 5;
        else if (symbol.Kind == SymbolKind.Function || symbol.Kind == SymbolKind.Method)
            score += 3;

        return score;
    }

    [RelayCommand]
    private void SelectSymbol()
    {
        if (SelectedSymbol == null)
        {
            if (FilteredSymbols.Count > 0)
            {
                SelectedSymbol = FilteredSymbols[0];
            }
            else
            {
                return;
            }
        }

        ResultSymbol = SelectedSymbol;
        DialogResult = true;
        SymbolSelected?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectNext()
    {
        if (FilteredSymbols.Count == 0) return;

        var currentIndex = SelectedSymbol != null ? FilteredSymbols.IndexOf(SelectedSymbol) : -1;
        var nextIndex = (currentIndex + 1) % FilteredSymbols.Count;
        SelectedSymbol = FilteredSymbols[nextIndex];
    }

    [RelayCommand]
    private void SelectPrevious()
    {
        if (FilteredSymbols.Count == 0) return;

        var currentIndex = SelectedSymbol != null ? FilteredSymbols.IndexOf(SelectedSymbol) : 0;
        var prevIndex = currentIndex <= 0 ? FilteredSymbols.Count - 1 : currentIndex - 1;
        SelectedSymbol = FilteredSymbols[prevIndex];
    }

    /// <summary>
    /// Simple regex-based symbol parser for BasicLang source code
    /// </summary>
    private static List<DocumentSymbol> ParseSymbolsFromSource(string source)
    {
        var symbols = new List<DocumentSymbol>();
        var lines = source.Split('\n');

        // Patterns for BasicLang constructs
        var patterns = new (Regex Regex, SymbolKind Kind)[]
        {
            (new Regex(@"^\s*(?:Public\s+|Private\s+)?(?:Shared\s+)?Sub\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Method),
            (new Regex(@"^\s*(?:Public\s+|Private\s+)?(?:Shared\s+)?Function\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Function),
            (new Regex(@"^\s*(?:Public\s+|Private\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Class),
            (new Regex(@"^\s*(?:Public\s+)?Module\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Module),
            (new Regex(@"^\s*(?:Public\s+|Private\s+)?Interface\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Interface),
            (new Regex(@"^\s*(?:Public\s+|Private\s+)?Enum\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Enum),
            (new Regex(@"^\s*(?:Public\s+|Private\s+)?(?:Shared\s+)?Property\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Property),
            (new Regex(@"^\s*(?:Public\s+|Private\s+)?Const\s+(\w+)", RegexOptions.IgnoreCase), SymbolKind.Constant),
            (new Regex(@"^\s*Namespace\s+(\w+(?:\.\w+)*)", RegexOptions.IgnoreCase), SymbolKind.Namespace),
        };

        for (var lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            var line = lines[lineNum];

            foreach (var (regex, kind) in patterns)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    symbols.Add(new DocumentSymbol
                    {
                        Name = match.Groups[1].Value,
                        Kind = kind,
                        Line = lineNum + 1, // 1-based line numbers
                        Column = match.Index + 1
                    });
                    break; // Only match one pattern per line
                }
            }
        }

        return symbols;
    }
}

public class SymbolItem
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Detail { get; set; }
    public SymbolKind Kind { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string? FilePath { get; set; }
    public string Icon { get; set; } = "?";

    public string KindText => Kind.ToString();
    public string LocationText => $"Line {Line}";
}
