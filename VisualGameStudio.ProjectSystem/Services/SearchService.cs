using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for searching files and content.
/// </summary>
public class SearchService : ISearchService
{
    private readonly List<string> _fileSearchHistory = new();
    private readonly List<string> _textSearchHistory = new();
    private readonly List<string> _symbolSearchHistory = new();
    private const int MaxHistoryItems = 50;

    private static readonly string[] DefaultExcludePatterns = new[]
    {
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
        "**/.git/**",
        "**/.vs/**",
        "**/packages/**",
        "**/BuildOutput/**",
        "**/*.exe",
        "**/*.dll",
        "**/*.pdb"
    };

    public event EventHandler<SearchEventArgs>? SearchStarted;
    public event EventHandler<SearchEventArgs>? SearchCompleted;
    public event EventHandler<SearchMatchEventArgs>? MatchFound;

    public async Task<IReadOnlyList<QuickOpenResult>> SearchFilesAsync(
        string query,
        FileSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FileSearchOptions();
        var results = new List<QuickOpenResult>();
        var searchPath = options.RootPath ?? Directory.GetCurrentDirectory();

        AddToHistory(_fileSearchHistory, query);
        SearchStarted?.Invoke(this, new SearchEventArgs(query, SearchType.Files));

        var startTime = DateTime.UtcNow;

        try
        {
            await Task.Run(() =>
            {
                var files = EnumerateFiles(searchPath, options);

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (results.Count >= options.MaxResults) break;

                    var fileName = Path.GetFileName(file);
                    var (isMatch, score, matchRanges) = options.FuzzyMatch
                        ? FuzzyMatch(fileName, query)
                        : ExactMatch(fileName, query);

                    if (isMatch)
                    {
                        var fileInfo = new FileInfo(file);
                        var result = new QuickOpenResult
                        {
                            FilePath = file,
                            RelativePath = Path.GetRelativePath(searchPath, file),
                            Score = score,
                            FileSize = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTime,
                            MatchRanges = matchRanges
                        };

                        results.Add(result);
                        MatchFound?.Invoke(this, new SearchMatchEventArgs(result, SearchType.Files));
                    }
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Search cancelled
        }

        // Sort by score
        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        var eventArgs = new SearchEventArgs(query, SearchType.Files)
        {
            ResultCount = results.Count,
            Duration = DateTime.UtcNow - startTime
        };
        SearchCompleted?.Invoke(this, eventArgs);

        return results;
    }

    public async Task<IReadOnlyList<TextSearchResult>> SearchTextAsync(
        string query,
        TextSearchOptions? options = null,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TextSearchOptions();
        var results = new List<TextSearchResult>();
        var searchPath = options.RootPath ?? Directory.GetCurrentDirectory();

        AddToHistory(_textSearchHistory, query);
        SearchStarted?.Invoke(this, new SearchEventArgs(query, SearchType.Text));

        var startTime = DateTime.UtcNow;
        var totalMatches = 0;

        try
        {
            var regex = CreateSearchRegex(query, options);
            var files = EnumerateFilesForText(searchPath, options).ToList();
            var filesSearched = 0;

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (totalMatches >= options.MaxResults) break;

                    try
                    {
                        var fileMatches = SearchInFile(file, regex, options);
                        if (fileMatches.Count > 0)
                        {
                            var result = new TextSearchResult
                            {
                                FilePath = file,
                                RelativePath = Path.GetRelativePath(searchPath, file),
                                Matches = fileMatches
                            };

                            results.Add(result);
                            totalMatches += fileMatches.Count;
                            MatchFound?.Invoke(this, new SearchMatchEventArgs(result, SearchType.Text));
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }

                    filesSearched++;
                    progress?.Report(new SearchProgress
                    {
                        CurrentFile = file,
                        FilesSearched = filesSearched,
                        TotalFiles = files.Count,
                        MatchesFound = totalMatches
                    });
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Search cancelled
        }

        var eventArgs = new SearchEventArgs(query, SearchType.Text)
        {
            ResultCount = totalMatches,
            Duration = DateTime.UtcNow - startTime
        };
        SearchCompleted?.Invoke(this, eventArgs);

        return results;
    }

    public async Task<IReadOnlyList<QuickSymbolResult>> SearchSymbolsAsync(
        string query,
        SymbolSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SymbolSearchOptions();
        var results = new List<QuickSymbolResult>();

        AddToHistory(_symbolSearchHistory, query);
        SearchStarted?.Invoke(this, new SearchEventArgs(query, SearchType.Symbols));

        var startTime = DateTime.UtcNow;

        // This would integrate with LSP for real symbol search
        // For now, use simple regex-based symbol extraction
        var searchPath = options.RootPath ?? Directory.GetCurrentDirectory();
        var extensions = options.IncludeExtensions ?? new List<string> { ".cs", ".bl" };

        try
        {
            await Task.Run(() =>
            {
                var files = Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (results.Count >= options.MaxResults) break;

                    try
                    {
                        var symbols = ExtractSymbols(file, query, options);
                        results.AddRange(symbols);
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Search cancelled
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        results = results.Take(options.MaxResults).ToList();

        var eventArgs = new SearchEventArgs(query, SearchType.Symbols)
        {
            ResultCount = results.Count,
            Duration = DateTime.UtcNow - startTime
        };
        SearchCompleted?.Invoke(this, eventArgs);

        return results;
    }

    public async Task<ReplaceResult> ReplaceAsync(
        string searchQuery,
        string replaceWith,
        TextSearchOptions? options = null,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var searchResults = await SearchTextAsync(searchQuery, options, progress, cancellationToken);
        var result = new ReplaceResult();
        var regex = CreateSearchRegex(searchQuery, options ?? new TextSearchOptions());

        foreach (var fileResult in searchResults)
        {
            try
            {
                var content = await File.ReadAllTextAsync(fileResult.FilePath, cancellationToken);
                var newContent = regex.Replace(content, replaceWith);

                if (content != newContent)
                {
                    await File.WriteAllTextAsync(fileResult.FilePath, newContent, cancellationToken);
                    result.FilesModified++;
                    result.ReplacementsCount += fileResult.MatchCount;
                    result.ModifiedFiles.Add(fileResult.FilePath);
                }
            }
            catch (Exception ex)
            {
                result.FailedFiles.Add((fileResult.FilePath, ex.Message));
            }
        }

        result.Success = result.FailedFiles.Count == 0;
        return result;
    }

    public async Task<ReplaceResult> ReplaceInFileAsync(
        string filePath,
        string searchQuery,
        string replaceWith,
        TextSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ReplaceResult();
        var regex = CreateSearchRegex(searchQuery, options ?? new TextSearchOptions());

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var matchCount = regex.Matches(content).Count;
            var newContent = regex.Replace(content, replaceWith);

            if (content != newContent)
            {
                await File.WriteAllTextAsync(filePath, newContent, cancellationToken);
                result.Success = true;
                result.FilesModified = 1;
                result.ReplacementsCount = matchCount;
                result.ModifiedFiles.Add(filePath);
            }
            else
            {
                result.Success = true;
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.FailedFiles.Add((filePath, ex.Message));
        }

        return result;
    }

    public IReadOnlyList<string> GetSearchHistory(SearchType type)
    {
        return type switch
        {
            SearchType.Files => _fileSearchHistory.ToList(),
            SearchType.Text => _textSearchHistory.ToList(),
            SearchType.Symbols => _symbolSearchHistory.ToList(),
            _ => new List<string>()
        };
    }

    public void ClearSearchHistory(SearchType type)
    {
        switch (type)
        {
            case SearchType.Files:
                _fileSearchHistory.Clear();
                break;
            case SearchType.Text:
                _textSearchHistory.Clear();
                break;
            case SearchType.Symbols:
                _symbolSearchHistory.Clear();
                break;
        }
    }

    #region Private Methods

    private static void AddToHistory(List<string> history, string query)
    {
        history.Remove(query);
        history.Insert(0, query);
        while (history.Count > MaxHistoryItems)
        {
            history.RemoveAt(history.Count - 1);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string path, FileSearchOptions options)
    {
        var excludePatterns = options.ExcludePatterns ?? DefaultExcludePatterns.ToList();

        foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
        {
            if (ShouldExclude(file, path, excludePatterns, options.IncludeHidden, options.IncludeIgnored))
                continue;

            if (options.IncludeExtensions != null && options.IncludeExtensions.Count > 0)
            {
                var ext = Path.GetExtension(file);
                if (!options.IncludeExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            if (options.ExcludeExtensions != null)
            {
                var ext = Path.GetExtension(file);
                if (options.ExcludeExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateFilesForText(string path, TextSearchOptions options)
    {
        var excludePatterns = options.ExcludePatterns ?? DefaultExcludePatterns.ToList();

        foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
        {
            if (ShouldExclude(file, path, excludePatterns, options.IncludeHidden, options.IncludeIgnored))
                continue;

            if (options.MaxFileSize > 0)
            {
                var info = new FileInfo(file);
                if (info.Length > options.MaxFileSize)
                    continue;
            }

            if (!options.IncludeBinary && IsBinaryFile(file))
                continue;

            yield return file;
        }
    }

    private static bool ShouldExclude(string file, string basePath, List<string> patterns, bool includeHidden, bool includeIgnored)
    {
        var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');
        var fileName = Path.GetFileName(file);

        if (!includeHidden && fileName.StartsWith('.'))
            return true;

        foreach (var pattern in patterns)
        {
            if (MatchGlobPattern(relativePath, pattern))
                return true;
        }

        return false;
    }

    private static bool MatchGlobPattern(string path, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }

    private static bool IsBinaryFile(string filePath)
    {
        var binaryExtensions = new[] { ".exe", ".dll", ".pdb", ".obj", ".bin", ".png", ".jpg", ".gif", ".ico", ".pdf", ".zip", ".7z", ".rar" };
        return binaryExtensions.Any(ext => filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static (bool isMatch, double score, List<(int Start, int Length)> ranges) FuzzyMatch(string text, string query)
    {
        if (string.IsNullOrEmpty(query))
            return (true, 1.0, new List<(int, int)>());

        var ranges = new List<(int Start, int Length)>();
        var queryLower = query.ToLowerInvariant();
        var textLower = text.ToLowerInvariant();

        // Check for exact match first
        var exactIndex = textLower.IndexOf(queryLower);
        if (exactIndex >= 0)
        {
            ranges.Add((exactIndex, query.Length));
            return (true, 1.0 + (1.0 / (exactIndex + 1)), ranges);
        }

        // Fuzzy match
        var queryIndex = 0;
        var score = 0.0;
        var consecutiveBonus = 0.0;
        var lastMatchIndex = -2;

        for (var i = 0; i < textLower.Length && queryIndex < queryLower.Length; i++)
        {
            if (textLower[i] == queryLower[queryIndex])
            {
                if (i == lastMatchIndex + 1)
                {
                    consecutiveBonus += 0.5;
                    if (ranges.Count > 0)
                    {
                        var last = ranges[^1];
                        ranges[^1] = (last.Start, last.Length + 1);
                    }
                }
                else
                {
                    ranges.Add((i, 1));
                }

                score += 1.0 + consecutiveBonus;
                if (i == 0 || !char.IsLetterOrDigit(textLower[i - 1]))
                    score += 0.5;

                lastMatchIndex = i;
                queryIndex++;
            }
        }

        if (queryIndex < queryLower.Length)
            return (false, 0, new List<(int, int)>());

        score = score / query.Length;
        return (true, score, ranges);
    }

    private static (bool isMatch, double score, List<(int Start, int Length)> ranges) ExactMatch(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return (true, 1.0, new List<(int, int)> { (index, query.Length) });
        }
        return (false, 0, new List<(int, int)>());
    }

    private static Regex CreateSearchRegex(string query, TextSearchOptions options)
    {
        var pattern = options.UseRegex ? query : Regex.Escape(query);

        if (options.WholeWord)
            pattern = $@"\b{pattern}\b";

        var regexOptions = RegexOptions.Compiled;
        if (!options.CaseSensitive)
            regexOptions |= RegexOptions.IgnoreCase;

        return new Regex(pattern, regexOptions);
    }

    private static List<TextMatch> SearchInFile(string filePath, Regex regex, TextSearchOptions options)
    {
        var matches = new List<TextMatch>();
        var lines = File.ReadAllLines(filePath);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineMatches = regex.Matches(line);

            foreach (Match match in lineMatches)
            {
                var textMatch = new TextMatch
                {
                    LineNumber = i + 1,
                    Column = match.Index + 1,
                    Length = match.Length,
                    LineContent = line,
                    MatchedText = match.Value
                };

                if (options.ContextLinesBefore > 0)
                {
                    for (var j = Math.Max(0, i - options.ContextLinesBefore); j < i; j++)
                    {
                        textMatch.ContextBefore.Add(lines[j]);
                    }
                }

                if (options.ContextLinesAfter > 0)
                {
                    for (var j = i + 1; j < Math.Min(lines.Length, i + options.ContextLinesAfter + 1); j++)
                    {
                        textMatch.ContextAfter.Add(lines[j]);
                    }
                }

                matches.Add(textMatch);
            }
        }

        return matches;
    }

    private static List<QuickSymbolResult> ExtractSymbols(string filePath, string query, SymbolSearchOptions options)
    {
        var results = new List<QuickSymbolResult>();
        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');

        // Simple symbol extraction patterns
        var patterns = new Dictionary<SearchSymbolKind, string>
        {
            { SearchSymbolKind.Class, @"(?:public|private|internal|protected)?\s*(?:static|abstract|sealed)?\s*class\s+(\w+)" },
            { SearchSymbolKind.Interface, @"(?:public|private|internal)?\s*interface\s+(\w+)" },
            { SearchSymbolKind.Struct, @"(?:public|private|internal)?\s*struct\s+(\w+)" },
            { SearchSymbolKind.Enum, @"(?:public|private|internal)?\s*enum\s+(\w+)" },
            { SearchSymbolKind.Method, @"(?:public|private|internal|protected)?\s*(?:static|virtual|override|async)?\s*\w+\s+(\w+)\s*\(" },
            { SearchSymbolKind.Property, @"(?:public|private|internal|protected)?\s*(?:static|virtual|override)?\s*\w+\s+(\w+)\s*\{\s*(?:get|set)" }
        };

        var kindsToSearch = options.Kinds?.Select(k => (SearchSymbolKind)k).ToList() ?? patterns.Keys.ToList();

        foreach (var kind in kindsToSearch)
        {
            if (!patterns.TryGetValue(kind, out var pattern)) continue;

            var regex = new Regex(pattern);

            for (var i = 0; i < lines.Length; i++)
            {
                var matches = regex.Matches(lines[i]);
                foreach (Match match in matches)
                {
                    var name = match.Groups[1].Value;
                    var (isMatch, score, _) = options.FuzzyMatch
                        ? FuzzyMatch(name, query)
                        : ExactMatch(name, query);

                    if (isMatch)
                    {
                        results.Add(new QuickSymbolResult
                        {
                            Name = name,
                            Kind = kind,
                            FilePath = filePath,
                            LineNumber = i + 1,
                            Column = match.Index + 1,
                            Score = score
                        });
                    }
                }
            }
        }

        return results;
    }

    #endregion
}
