namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for searching files and content.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches for files by name pattern.
    /// </summary>
    Task<IReadOnlyList<QuickOpenResult>> SearchFilesAsync(
        string query,
        FileSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for text content in files.
    /// </summary>
    Task<IReadOnlyList<TextSearchResult>> SearchTextAsync(
        string query,
        TextSearchOptions? options = null,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for symbols (classes, methods, etc.).
    /// </summary>
    Task<IReadOnlyList<QuickSymbolResult>> SearchSymbolsAsync(
        string query,
        SymbolSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces text in files.
    /// </summary>
    Task<ReplaceResult> ReplaceAsync(
        string searchQuery,
        string replaceWith,
        TextSearchOptions? options = null,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces text in a specific file.
    /// </summary>
    Task<ReplaceResult> ReplaceInFileAsync(
        string filePath,
        string searchQuery,
        string replaceWith,
        TextSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets search history.
    /// </summary>
    IReadOnlyList<string> GetSearchHistory(SearchType type);

    /// <summary>
    /// Clears search history.
    /// </summary>
    void ClearSearchHistory(SearchType type);

    /// <summary>
    /// Raised when search starts.
    /// </summary>
    event EventHandler<SearchEventArgs>? SearchStarted;

    /// <summary>
    /// Raised when search completes.
    /// </summary>
    event EventHandler<SearchEventArgs>? SearchCompleted;

    /// <summary>
    /// Raised when a match is found.
    /// </summary>
    event EventHandler<SearchMatchEventArgs>? MatchFound;
}

#region Search Types

/// <summary>
/// Type of search.
/// </summary>
public enum SearchType
{
    Files,
    Text,
    Symbols,
    Replace
}

/// <summary>
/// Options for file search.
/// </summary>
public class FileSearchOptions
{
    /// <summary>
    /// Root path to search in.
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// Maximum results to return.
    /// </summary>
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Whether to use fuzzy matching.
    /// </summary>
    public bool FuzzyMatch { get; set; } = true;

    /// <summary>
    /// File extensions to include (null = all).
    /// </summary>
    public List<string>? IncludeExtensions { get; set; }

    /// <summary>
    /// File extensions to exclude.
    /// </summary>
    public List<string>? ExcludeExtensions { get; set; }

    /// <summary>
    /// Patterns to exclude (glob patterns).
    /// </summary>
    public List<string>? ExcludePatterns { get; set; }

    /// <summary>
    /// Whether to include hidden files.
    /// </summary>
    public bool IncludeHidden { get; set; }

    /// <summary>
    /// Whether to include files in .gitignore.
    /// </summary>
    public bool IncludeIgnored { get; set; }
}

/// <summary>
/// Options for text search.
/// </summary>
public class TextSearchOptions
{
    /// <summary>
    /// Root path to search in.
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// Maximum results to return.
    /// </summary>
    public int MaxResults { get; set; } = 1000;

    /// <summary>
    /// Whether to use case-sensitive search.
    /// </summary>
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// Whether to match whole words only.
    /// </summary>
    public bool WholeWord { get; set; }

    /// <summary>
    /// Whether query is a regular expression.
    /// </summary>
    public bool UseRegex { get; set; }

    /// <summary>
    /// File patterns to include (glob patterns).
    /// </summary>
    public List<string>? IncludePatterns { get; set; }

    /// <summary>
    /// File patterns to exclude (glob patterns).
    /// </summary>
    public List<string>? ExcludePatterns { get; set; }

    /// <summary>
    /// Whether to search in hidden files.
    /// </summary>
    public bool IncludeHidden { get; set; }

    /// <summary>
    /// Whether to search in ignored files.
    /// </summary>
    public bool IncludeIgnored { get; set; }

    /// <summary>
    /// Number of context lines before match.
    /// </summary>
    public int ContextLinesBefore { get; set; }

    /// <summary>
    /// Number of context lines after match.
    /// </summary>
    public int ContextLinesAfter { get; set; }

    /// <summary>
    /// Maximum file size to search (bytes). 0 = no limit.
    /// </summary>
    public long MaxFileSize { get; set; }

    /// <summary>
    /// Whether to search in binary files.
    /// </summary>
    public bool IncludeBinary { get; set; }
}

/// <summary>
/// Options for symbol search.
/// </summary>
public class SymbolSearchOptions
{
    /// <summary>
    /// Root path to search in.
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// Maximum results to return.
    /// </summary>
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Symbol kinds to include.
    /// </summary>
    public List<SearchSymbolKind>? Kinds { get; set; }

    /// <summary>
    /// Whether to use fuzzy matching.
    /// </summary>
    public bool FuzzyMatch { get; set; } = true;

    /// <summary>
    /// File extensions to include.
    /// </summary>
    public List<string>? IncludeExtensions { get; set; }
}

/// <summary>
/// Symbol kinds for search (mirrors SymbolKind from ILanguageService).
/// </summary>
public enum SearchSymbolKind
{
    File,
    Module,
    Namespace,
    Package,
    Class,
    Method,
    Property,
    Field,
    Constructor,
    Enum,
    Interface,
    Function,
    Variable,
    Constant,
    String,
    Number,
    Boolean,
    Array,
    Object,
    Key,
    Null,
    EnumMember,
    Struct,
    Event,
    Operator,
    TypeParameter
}

/// <summary>
/// Result of a quick file search (for quick open dialog).
/// </summary>
public class QuickOpenResult
{
    /// <summary>
    /// Full path to the file.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// File name.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Relative path from search root.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Match score (higher = better match).
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Last modified date.
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Ranges in the file name that matched.
    /// </summary>
    public List<(int Start, int Length)> MatchRanges { get; set; } = new();
}

/// <summary>
/// Result of a text search.
/// </summary>
public class TextSearchResult
{
    /// <summary>
    /// Full path to the file.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// File name.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Relative path from search root.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Matches found in this file.
    /// </summary>
    public List<TextMatch> Matches { get; set; } = new();

    /// <summary>
    /// Total match count in this file.
    /// </summary>
    public int MatchCount => Matches.Count;
}

/// <summary>
/// A single text match.
/// </summary>
public class TextMatch
{
    /// <summary>
    /// Line number (1-based).
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Column number (1-based).
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Length of the match.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// The matching line content.
    /// </summary>
    public string LineContent { get; set; } = "";

    /// <summary>
    /// The matched text.
    /// </summary>
    public string MatchedText { get; set; } = "";

    /// <summary>
    /// Context lines before the match.
    /// </summary>
    public List<string> ContextBefore { get; set; } = new();

    /// <summary>
    /// Context lines after the match.
    /// </summary>
    public List<string> ContextAfter { get; set; } = new();
}

/// <summary>
/// Result of a symbol search.
/// </summary>
public class QuickSymbolResult
{
    /// <summary>
    /// Symbol name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Symbol kind.
    /// </summary>
    public SearchSymbolKind Kind { get; set; }

    /// <summary>
    /// Container name (parent class, namespace, etc.).
    /// </summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Full path to the file.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Line number (1-based).
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Column number (1-based).
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Match score.
    /// </summary>
    public double Score { get; set; }
}

/// <summary>
/// Result of a replace operation.
/// </summary>
public class ReplaceResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of files modified.
    /// </summary>
    public int FilesModified { get; set; }

    /// <summary>
    /// Total replacements made.
    /// </summary>
    public int ReplacementsCount { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Files that were modified.
    /// </summary>
    public List<string> ModifiedFiles { get; set; } = new();

    /// <summary>
    /// Files that failed to modify.
    /// </summary>
    public List<(string FilePath, string Error)> FailedFiles { get; set; } = new();
}

/// <summary>
/// Search progress information.
/// </summary>
public class SearchProgress
{
    /// <summary>
    /// Current file being searched.
    /// </summary>
    public string? CurrentFile { get; set; }

    /// <summary>
    /// Files searched so far.
    /// </summary>
    public int FilesSearched { get; set; }

    /// <summary>
    /// Total files to search (if known).
    /// </summary>
    public int? TotalFiles { get; set; }

    /// <summary>
    /// Matches found so far.
    /// </summary>
    public int MatchesFound { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double? Percentage => TotalFiles > 0 ? (double)FilesSearched / TotalFiles * 100 : null;
}

/// <summary>
/// Event args for search events.
/// </summary>
public class SearchEventArgs : EventArgs
{
    /// <summary>
    /// The search query.
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// Search type.
    /// </summary>
    public SearchType Type { get; }

    /// <summary>
    /// Total results found (for completed event).
    /// </summary>
    public int? ResultCount { get; set; }

    /// <summary>
    /// Duration of search (for completed event).
    /// </summary>
    public TimeSpan? Duration { get; set; }

    public SearchEventArgs(string query, SearchType type)
    {
        Query = query;
        Type = type;
    }
}

/// <summary>
/// Event args for match found events.
/// </summary>
public class SearchMatchEventArgs : EventArgs
{
    /// <summary>
    /// The match found.
    /// </summary>
    public object Match { get; }

    /// <summary>
    /// Search type.
    /// </summary>
    public SearchType Type { get; }

    public SearchMatchEventArgs(object match, SearchType type)
    {
        Match = match;
        Type = type;
    }
}

#endregion
