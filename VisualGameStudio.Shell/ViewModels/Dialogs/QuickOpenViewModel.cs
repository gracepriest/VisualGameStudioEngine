using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class QuickOpenViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly ILanguageService _languageService;
    private readonly List<QuickOpenItem> _allItems = new();
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ObservableCollection<QuickOpenItem> _results = new();

    [ObservableProperty]
    private QuickOpenItem? _selectedItem;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private QuickOpenMode _mode = QuickOpenMode.Files;

    [ObservableProperty]
    private string _placeholder = "Type to search files...";

    public event EventHandler<QuickOpenResult>? ItemSelected;
    public event EventHandler? Cancelled;

    public QuickOpenViewModel(IProjectService projectService, ILanguageService languageService)
    {
        _projectService = projectService;
        _languageService = languageService;
    }

    public void Open(QuickOpenMode mode)
    {
        Mode = mode;
        SearchText = "";
        Results.Clear();
        SelectedIndex = 0;

        Placeholder = mode switch
        {
            QuickOpenMode.Files => "Type to search files... (use @ for symbols)",
            QuickOpenMode.Symbols => "Type to search symbols...",
            QuickOpenMode.Commands => "Type to search commands...",
            _ => "Type to search..."
        };

        LoadItems();
    }

    private void LoadItems()
    {
        _allItems.Clear();

        if (_projectService.CurrentProject == null) return;

        if (Mode == QuickOpenMode.Files || Mode == QuickOpenMode.All)
        {
            // Load all project files
            foreach (var item in _projectService.CurrentProject.Items)
            {
                var fullPath = Path.Combine(_projectService.CurrentProject.ProjectDirectory, item.Include);
                _allItems.Add(new QuickOpenItem
                {
                    Name = item.FileName,
                    Description = item.Include,
                    FullPath = fullPath,
                    ItemType = QuickOpenItemType.File,
                    Icon = GetFileIcon(item.FileName)
                });
            }
        }

        if (Mode == QuickOpenMode.Symbols || Mode == QuickOpenMode.All)
        {
            // Load symbols from language service
            _ = LoadSymbolsAsync();
        }

        // Show initial results
        UpdateResults();
    }

    private async Task LoadSymbolsAsync()
    {
        if (_projectService.CurrentProject == null) return;

        foreach (var item in _projectService.CurrentProject.Items)
        {
            var fullPath = Path.Combine(_projectService.CurrentProject.ProjectDirectory, item.Include);
            if (!File.Exists(fullPath)) continue;

            try
            {
                var content = await File.ReadAllTextAsync(fullPath);
                var symbols = ParseSymbols(content, fullPath, item.FileName);
                foreach (var symbol in symbols)
                {
                    _allItems.Add(symbol);
                }
            }
            catch
            {
                // Ignore file read errors
            }
        }

        UpdateResults();
    }

    private List<QuickOpenItem> ParseSymbols(string content, string fullPath, string fileName)
    {
        var symbols = new List<QuickOpenItem>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            // Parse Module
            if (line.StartsWith("Module ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Module ");
                symbols.Add(CreateSymbol(name, "Module", fileName, fullPath, lineNumber, QuickOpenItemType.Module));
            }
            // Parse Class
            else if (line.StartsWith("Class ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractName(line, "Class ");
                symbols.Add(CreateSymbol(name, "Class", fileName, fullPath, lineNumber, QuickOpenItemType.Class));
            }
            // Parse Sub
            else if (line.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Sub ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractSubFunctionName(line, "Sub ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbol(name, "Sub", fileName, fullPath, lineNumber, QuickOpenItemType.Method));
            }
            // Parse Function
            else if (line.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Function ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractSubFunctionName(line, "Function ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbol(name, "Function", fileName, fullPath, lineNumber, QuickOpenItemType.Method));
            }
            // Parse Property
            else if (line.StartsWith("Property ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains(" Property ", StringComparison.OrdinalIgnoreCase))
            {
                var name = ExtractSubFunctionName(line, "Property ");
                if (!string.IsNullOrEmpty(name))
                    symbols.Add(CreateSymbol(name, "Property", fileName, fullPath, lineNumber, QuickOpenItemType.Property));
            }
        }

        return symbols;
    }

    private static string ExtractName(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = line.Substring(idx + keyword.Length).Trim();
        var endIdx = rest.IndexOfAny(new[] { ' ', '(', '\r', '\n' });
        return endIdx > 0 ? rest.Substring(0, endIdx) : rest;
    }

    private static string ExtractSubFunctionName(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = line.Substring(idx + keyword.Length).Trim();
        var endIdx = rest.IndexOf('(');
        if (endIdx < 0) endIdx = rest.IndexOfAny(new[] { ' ', '\r', '\n' });
        return endIdx > 0 ? rest.Substring(0, endIdx).Trim() : rest.Trim();
    }

    private static QuickOpenItem CreateSymbol(string name, string kind, string fileName, string fullPath, int line, QuickOpenItemType type)
    {
        return new QuickOpenItem
        {
            Name = name,
            Description = $"{kind} in {fileName}:{line}",
            FullPath = fullPath,
            Line = line,
            ItemType = type,
            Icon = type switch
            {
                QuickOpenItemType.Module => "📦",
                QuickOpenItemType.Class => "🔷",
                QuickOpenItemType.Method => "🔹",
                QuickOpenItemType.Property => "🔸",
                _ => "📄"
            }
        };
    }

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".bas" => "📄",
            ".bl" => "📄",
            ".blproj" => "📦",
            _ => "📋"
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        // Check for mode switch prefix
        if (value.StartsWith("@"))
        {
            if (Mode != QuickOpenMode.Symbols)
            {
                Mode = QuickOpenMode.Symbols;
                LoadItems();
            }
            value = value.Substring(1);
        }
        else if (value.StartsWith(">"))
        {
            if (Mode != QuickOpenMode.Commands)
            {
                Mode = QuickOpenMode.Commands;
                LoadItems();
            }
            value = value.Substring(1);
        }

        UpdateResults(value);
    }

    private void UpdateResults(string? filter = null)
    {
        filter = filter?.Trim() ?? SearchText.TrimStart('@', '>').Trim();

        Results.Clear();

        IEnumerable<QuickOpenItem> filtered = _allItems;

        if (!string.IsNullOrEmpty(filter))
        {
            // Fuzzy match
            filtered = _allItems
                .Select(item => new { Item = item, Score = FuzzyMatch(item.Name, filter) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Name.Length)
                .Select(x => x.Item);
        }
        else
        {
            filtered = _allItems.OrderBy(x => x.Name);
        }

        foreach (var item in filtered.Take(50))
        {
            Results.Add(item);
        }

        if (Results.Count > 0)
        {
            SelectedIndex = 0;
            SelectedItem = Results[0];
        }
    }

    private static int FuzzyMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 1;
        if (string.IsNullOrEmpty(text)) return 0;

        // Exact match (case insensitive)
        if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return 100 + (text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ? 50 : 0);
        }

        // Fuzzy match - characters must appear in order
        var patternIdx = 0;
        var score = 0;
        var consecutive = 0;

        for (var i = 0; i < text.Length && patternIdx < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(pattern[patternIdx]))
            {
                score += 10 + consecutive * 5;
                consecutive++;
                patternIdx++;
            }
            else
            {
                consecutive = 0;
            }
        }

        return patternIdx == pattern.Length ? score : 0;
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (Results.Count == 0) return;
        SelectedIndex = Math.Max(0, SelectedIndex - 1);
        SelectedItem = Results[SelectedIndex];
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (Results.Count == 0) return;
        SelectedIndex = Math.Min(Results.Count - 1, SelectedIndex + 1);
        SelectedItem = Results[SelectedIndex];
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedItem != null)
        {
            ItemSelected?.Invoke(this, new QuickOpenResult
            {
                FilePath = SelectedItem.FullPath,
                Line = SelectedItem.Line,
                Column = 1
            });
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}

public class QuickOpenItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string FullPath { get; set; } = "";
    public int Line { get; set; } = 1;
    public QuickOpenItemType ItemType { get; set; }
    public string Icon { get; set; } = "📄";
}

public class QuickOpenResult
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; } = 1;
    public int Column { get; set; } = 1;
}

public enum QuickOpenMode
{
    Files,
    Symbols,
    Commands,
    All
}

public enum QuickOpenItemType
{
    File,
    Module,
    Class,
    Method,
    Property,
    Command
}
