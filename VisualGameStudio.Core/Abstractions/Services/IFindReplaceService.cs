using System.Text.RegularExpressions;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides find and replace functionality for the IDE.
/// </summary>
public interface IFindReplaceService
{
    /// <summary>
    /// Gets or sets the current search options.
    /// </summary>
    FindReplaceOptions Options { get; set; }

    /// <summary>
    /// Finds all occurrences of a pattern in a single document.
    /// </summary>
    /// <param name="content">The document content to search.</param>
    /// <param name="pattern">The search pattern.</param>
    /// <returns>A list of matches found.</returns>
    IReadOnlyList<FindMatch> FindInDocument(string content, string pattern);

    /// <summary>
    /// Finds the next occurrence starting from a position.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="startOffset">The character offset to start searching from.</param>
    /// <returns>The next match, or null if not found.</returns>
    FindMatch? FindNext(string content, string pattern, int startOffset);

    /// <summary>
    /// Finds the previous occurrence before a position.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="startOffset">The character offset to search before.</param>
    /// <returns>The previous match, or null if not found.</returns>
    FindMatch? FindPrevious(string content, string pattern, int startOffset);

    /// <summary>
    /// Replaces a single occurrence in a document.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="match">The match to replace.</param>
    /// <param name="replacement">The replacement text.</param>
    /// <returns>The modified content.</returns>
    string ReplaceOne(string content, FindMatch match, string replacement);

    /// <summary>
    /// Replaces all occurrences in a document.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="replacement">The replacement text.</param>
    /// <returns>The result containing modified content and replacement count.</returns>
    ReplaceAllResult ReplaceAll(string content, string pattern, string replacement);

    /// <summary>
    /// Searches for a pattern across multiple files.
    /// </summary>
    /// <param name="filePaths">The file paths to search.</param>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="filePattern">Optional glob pattern to filter files (e.g., "*.bl").</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of matches grouped by file.</returns>
    Task<IReadOnlyList<FileSearchResult>> FindInFilesAsync(
        IEnumerable<string> filePaths,
        string pattern,
        string? filePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a pattern across multiple files.
    /// </summary>
    /// <param name="filePaths">The file paths to search.</param>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="replacement">The replacement text.</param>
    /// <param name="filePattern">Optional glob pattern to filter files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary of the replacements made.</returns>
    Task<ReplaceInFilesResult> ReplaceInFilesAsync(
        IEnumerable<string> filePaths,
        string pattern,
        string replacement,
        string? filePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a regex pattern.
    /// </summary>
    /// <param name="pattern">The pattern to validate.</param>
    /// <returns>True if the pattern is valid, false otherwise.</returns>
    bool IsValidPattern(string pattern);

    /// <summary>
    /// Gets the error message for an invalid pattern.
    /// </summary>
    /// <param name="pattern">The pattern to check.</param>
    /// <returns>The error message, or null if the pattern is valid.</returns>
    string? GetPatternError(string pattern);

    /// <summary>
    /// Raised when a search operation starts.
    /// </summary>
    event EventHandler<FindReplaceEventArgs>? SearchStarted;

    /// <summary>
    /// Raised when a search operation completes.
    /// </summary>
    event EventHandler<FindReplaceEventArgs>? SearchCompleted;

    /// <summary>
    /// Raised to report search progress.
    /// </summary>
    event EventHandler<FindReplaceProgressEventArgs>? SearchProgress;
}

/// <summary>
/// Options for find and replace operations.
/// </summary>
public class FindReplaceOptions
{
    /// <summary>
    /// Gets or sets whether the search is case sensitive.
    /// </summary>
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to match whole words only.
    /// </summary>
    public bool WholeWord { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to use regular expressions.
    /// </summary>
    public bool UseRegex { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to search in subdirectories.
    /// </summary>
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to wrap around when reaching the end.
    /// </summary>
    public bool WrapAround { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to preserve case in replacements.
    /// </summary>
    public bool PreserveCase { get; set; } = false;

    /// <summary>
    /// Gets or sets the search scope.
    /// </summary>
    public SearchScope Scope { get; set; } = SearchScope.CurrentDocument;

    /// <summary>
    /// Gets or sets file patterns to include (e.g., "*.bl;*.txt").
    /// </summary>
    public string? IncludePatterns { get; set; }

    /// <summary>
    /// Gets or sets file patterns to exclude (e.g., "*.bak;bin/*").
    /// </summary>
    public string? ExcludePatterns { get; set; }
}

/// <summary>
/// Defines the scope of a search operation.
/// </summary>
public enum SearchScope
{
    /// <summary>Search in the current document only.</summary>
    CurrentDocument,
    /// <summary>Search in the current selection only.</summary>
    Selection,
    /// <summary>Search in all open documents.</summary>
    OpenDocuments,
    /// <summary>Search in the current project.</summary>
    CurrentProject,
    /// <summary>Search in the entire solution.</summary>
    EntireSolution,
    /// <summary>Search in a custom set of folders.</summary>
    CustomFolders
}

/// <summary>
/// Represents a single match found during a search.
/// </summary>
public class FindMatch
{
    /// <summary>
    /// Gets or sets the character offset where the match starts.
    /// </summary>
    public int StartOffset { get; set; }

    /// <summary>
    /// Gets or sets the length of the matched text.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets the matched text.
    /// </summary>
    public string MatchedText { get; set; } = "";

    /// <summary>
    /// Gets or sets the line number (1-based).
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the column number (1-based).
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets or sets the entire line containing the match.
    /// </summary>
    public string LineText { get; set; } = "";

    /// <summary>
    /// Gets or sets captured groups (for regex matches).
    /// </summary>
    public IReadOnlyList<string> Groups { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets the end offset of the match.
    /// </summary>
    public int EndOffset => StartOffset + Length;
}

/// <summary>
/// Represents the result of a replace all operation.
/// </summary>
public class ReplaceAllResult
{
    /// <summary>
    /// Gets or sets the modified content.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of replacements made.
    /// </summary>
    public int ReplacementCount { get; set; }

    /// <summary>
    /// Gets or sets the list of replacements for undo support.
    /// </summary>
    public IReadOnlyList<ReplacementInfo> Replacements { get; set; } = Array.Empty<ReplacementInfo>();
}

/// <summary>
/// Information about a single replacement.
/// </summary>
public class ReplacementInfo
{
    /// <summary>
    /// Gets or sets the original matched text.
    /// </summary>
    public string OriginalText { get; set; } = "";

    /// <summary>
    /// Gets or sets the replacement text.
    /// </summary>
    public string ReplacementText { get; set; } = "";

    /// <summary>
    /// Gets or sets the offset where the replacement occurred.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// Gets or sets the line number.
    /// </summary>
    public int Line { get; set; }
}

/// <summary>
/// Represents search results for a single file.
/// </summary>
public class FileSearchResult
{
    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Gets or sets the matches found in the file.
    /// </summary>
    public IReadOnlyList<FindMatch> Matches { get; set; } = Array.Empty<FindMatch>();

    /// <summary>
    /// Gets the total number of matches in this file.
    /// </summary>
    public int MatchCount => Matches.Count;
}

/// <summary>
/// Represents the result of a replace in files operation.
/// </summary>
public class ReplaceInFilesResult
{
    /// <summary>
    /// Gets or sets the total number of files modified.
    /// </summary>
    public int FilesModified { get; set; }

    /// <summary>
    /// Gets or sets the total number of replacements made.
    /// </summary>
    public int TotalReplacements { get; set; }

    /// <summary>
    /// Gets or sets the list of modified files with their replacement counts.
    /// </summary>
    public IReadOnlyList<FileReplacementInfo> ModifiedFiles { get; set; } = Array.Empty<FileReplacementInfo>();

    /// <summary>
    /// Gets or sets any errors that occurred.
    /// </summary>
    public IReadOnlyList<FileOperationError> Errors { get; set; } = Array.Empty<FileOperationError>();
}

/// <summary>
/// Information about replacements in a single file.
/// </summary>
public class FileReplacementInfo
{
    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of replacements in this file.
    /// </summary>
    public int ReplacementCount { get; set; }

    /// <summary>
    /// Gets or sets whether a backup was created.
    /// </summary>
    public bool BackupCreated { get; set; }
}

/// <summary>
/// Represents an error during a file operation.
/// </summary>
public class FileOperationError
{
    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets the exception that occurred.
    /// </summary>
    public Exception? Exception { get; set; }
}

/// <summary>
/// Event arguments for find/replace operations.
/// </summary>
public class FindReplaceEventArgs : EventArgs
{
    /// <summary>
    /// Gets the search pattern.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets the total matches found (for completed events).
    /// </summary>
    public int TotalMatches { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public FindReplaceEventArgs(string pattern, int totalMatches = 0)
    {
        Pattern = pattern;
        TotalMatches = totalMatches;
    }
}

/// <summary>
/// Event arguments for search progress.
/// </summary>
public class FindReplaceProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the current file being searched.
    /// </summary>
    public string? CurrentFile { get; }

    /// <summary>
    /// Gets the number of files searched so far.
    /// </summary>
    public int FilesSearched { get; }

    /// <summary>
    /// Gets the total number of files to search.
    /// </summary>
    public int TotalFiles { get; }

    /// <summary>
    /// Gets the number of matches found so far.
    /// </summary>
    public int MatchesFound { get; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public int PercentComplete => TotalFiles > 0 ? (FilesSearched * 100) / TotalFiles : 0;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public FindReplaceProgressEventArgs(string? currentFile, int filesSearched, int totalFiles, int matchesFound)
    {
        CurrentFile = currentFile;
        FilesSearched = filesSearched;
        TotalFiles = totalFiles;
        MatchesFound = matchesFound;
    }
}
