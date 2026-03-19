using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// High-performance file search engine for Find in Files / Replace in Files.
/// Supports regex, case-sensitive, whole-word matching with streaming results.
/// Respects .gitignore, skips binary files, and limits file sizes.
/// </summary>
public class FileSearchService
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".obj", ".bin", ".lib", ".so", ".dylib",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tiff", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2",
        ".mp3", ".mp4", ".avi", ".mkv", ".wav", ".flac",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".db", ".sqlite", ".mdb",
        ".nupkg", ".snupkg", ".vsix"
    };

    private static readonly string[] DefaultExcludeDirs =
    {
        ".git", ".vs", ".idea", "node_modules", "bin", "obj",
        "packages", "BuildOutput", ".nuget", "TestResults",
        "__pycache__", ".svn", ".hg"
    };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
    private const int BinaryCheckSize = 8192;
    private const int MaxTotalMatches = 10000;

    /// <summary>
    /// Searches for text across files in the given directory, streaming results via callback.
    /// </summary>
    public async Task SearchAsync(
        string rootPath,
        string searchQuery,
        SearchOptions options,
        Action<FileSearchMatchResult> onFileResult,
        IProgress<SearchProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(searchQuery))
            return;

        var regex = BuildRegex(searchQuery, options);
        if (regex == null) return;

        var gitignorePatterns = LoadGitignorePatterns(rootPath);
        var includePatterns = ParseGlobPatterns(options.IncludePattern);
        var excludePatterns = ParseGlobPatterns(options.ExcludePattern);
        var totalMatches = 0;
        var filesSearched = 0;

        // Enumerate files lazily
        var files = EnumerateSearchableFiles(rootPath, gitignorePatterns, includePatterns, excludePatterns);

        // Process files in parallel batches for performance
        var fileList = new List<string>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileList.Add(file);
        }

        var resultBag = new ConcurrentBag<FileSearchMatchResult>();
        var matchCount = 0;

        await Task.Run(() =>
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            };

            Parallel.ForEach(fileList, parallelOptions, (file, state) =>
            {
                if (Volatile.Read(ref matchCount) >= MaxTotalMatches)
                {
                    state.Break();
                    return;
                }

                try
                {
                    var result = SearchInFile(file, rootPath, regex, options);
                    if (result != null && result.Matches.Count > 0)
                    {
                        var newCount = Interlocked.Add(ref matchCount, result.Matches.Count);
                        if (newCount - result.Matches.Count < MaxTotalMatches)
                        {
                            // Trim matches if we exceeded the limit
                            if (newCount > MaxTotalMatches)
                            {
                                var excess = newCount - MaxTotalMatches;
                                var trimmed = result.Matches.Count - excess;
                                if (trimmed > 0)
                                {
                                    result = new FileSearchMatchResult
                                    {
                                        FilePath = result.FilePath,
                                        RelativePath = result.RelativePath,
                                        Matches = result.Matches.GetRange(0, trimmed)
                                    };
                                }
                                else
                                {
                                    return;
                                }
                            }
                            resultBag.Add(result);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    state.Stop();
                }
                catch
                {
                    // Skip files that can't be read
                }

                var searched = Interlocked.Increment(ref filesSearched);
                if (searched % 50 == 0)
                {
                    progress?.Report(new SearchProgressInfo
                    {
                        FilesSearched = searched,
                        TotalFiles = fileList.Count,
                        MatchesFound = Volatile.Read(ref matchCount)
                    });
                }
            });
        }, cancellationToken);

        // Deliver results sorted by file path
        var sortedResults = resultBag.OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var result in sortedResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onFileResult(result);
        }

        progress?.Report(new SearchProgressInfo
        {
            FilesSearched = filesSearched,
            TotalFiles = fileList.Count,
            MatchesFound = Volatile.Read(ref matchCount),
            IsComplete = true,
            LimitReached = Volatile.Read(ref matchCount) >= MaxTotalMatches
        });
    }

    /// <summary>
    /// Replaces all matches in a single file.
    /// </summary>
    public async Task<int> ReplaceInFileAsync(
        string filePath,
        string searchQuery,
        string replaceText,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var regex = BuildRegex(searchQuery, options);
        if (regex == null) return 0;

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var matchCount = regex.Matches(content).Count;

        if (matchCount > 0)
        {
            string newContent;
            if (options.PreserveCase)
            {
                newContent = regex.Replace(content, match => PreserveCaseReplace(match.Value, replaceText));
            }
            else
            {
                newContent = regex.Replace(content, replaceText);
            }

            if (content != newContent)
            {
                await File.WriteAllTextAsync(filePath, newContent, cancellationToken);
            }
        }

        return matchCount;
    }

    /// <summary>
    /// Replaces all matches across all files in the directory.
    /// </summary>
    public async Task<(int totalReplacements, int filesModified)> ReplaceAllAsync(
        string rootPath,
        string searchQuery,
        string replaceText,
        SearchOptions options,
        IProgress<SearchProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var totalReplacements = 0;
        var filesModified = 0;
        var fileResults = new List<FileSearchMatchResult>();

        await SearchAsync(rootPath, searchQuery, options, result =>
        {
            fileResults.Add(result);
        }, progress, cancellationToken);

        foreach (var fileResult in fileResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var count = await ReplaceInFileAsync(
                    fileResult.FilePath, searchQuery, replaceText, options, cancellationToken);
                if (count > 0)
                {
                    totalReplacements += count;
                    filesModified++;
                }
            }
            catch
            {
                // Skip files that can't be written
            }
        }

        return (totalReplacements, filesModified);
    }

    #region Private Methods

    private static Regex? BuildRegex(string query, SearchOptions options)
    {
        try
        {
            var pattern = options.IsRegex ? query : Regex.Escape(query);
            if (options.IsWholeWord)
            {
                pattern = $@"\b{pattern}\b";
            }

            var regexOptions = RegexOptions.Compiled | RegexOptions.Multiline;
            if (!options.IsCaseSensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            return new Regex(pattern, regexOptions, TimeSpan.FromSeconds(5));
        }
        catch (RegexParseException)
        {
            return null;
        }
    }

    private FileSearchMatchResult? SearchInFile(string filePath, string rootPath, Regex regex, SearchOptions options)
    {
        var matches = new List<SearchMatch>();

        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var lineMatches = regex.Matches(line);

            foreach (Match match in lineMatches)
            {
                var column = match.Index + 1;
                var matchLength = match.Length;

                // Compute preview parts
                var trimmedLine = line.TrimStart();
                var leadingWhitespace = line.Length - trimmedLine.Length;
                var adjustedIndex = match.Index - leadingWhitespace;

                string previewBefore, matchText, previewAfter;
                if (adjustedIndex >= 0 && adjustedIndex + matchLength <= trimmedLine.Length)
                {
                    previewBefore = trimmedLine.Substring(0, adjustedIndex);
                    matchText = trimmedLine.Substring(adjustedIndex, matchLength);
                    previewAfter = trimmedLine.Substring(adjustedIndex + matchLength);
                }
                else
                {
                    // Fallback for edge cases
                    previewBefore = line.Substring(0, match.Index);
                    matchText = match.Value;
                    previewAfter = line.Substring(match.Index + matchLength);
                }

                // Truncate long previews
                if (previewBefore.Length > 80) previewBefore = "..." + previewBefore.Substring(previewBefore.Length - 77);
                if (previewAfter.Length > 80) previewAfter = previewAfter.Substring(0, 77) + "...";

                matches.Add(new SearchMatch
                {
                    LineNumber = lineNumber,
                    Column = column,
                    MatchLength = matchLength,
                    LineText = trimmedLine.Length > 200 ? trimmedLine.Substring(0, 197) + "..." : trimmedLine,
                    PreviewBefore = previewBefore,
                    MatchText = matchText,
                    PreviewAfter = previewAfter
                });
            }
        }

        if (matches.Count == 0) return null;

        return new FileSearchMatchResult
        {
            FilePath = filePath,
            RelativePath = Path.GetRelativePath(rootPath, filePath),
            Matches = matches
        };
    }

    private IEnumerable<string> EnumerateSearchableFiles(
        string rootPath,
        List<Regex> gitignorePatterns,
        List<Regex>? includePatterns,
        List<Regex>? excludePatterns)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            // Check if directory should be excluded
            var dirName = Path.GetFileName(dir);
            if (DefaultExcludeDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                continue;

            var relativeDirPath = Path.GetRelativePath(rootPath, dir).Replace('\\', '/');
            if (relativeDirPath != "." && IsGitignored(relativeDirPath + "/", gitignorePatterns))
                continue;

            // Enumerate subdirectories
            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var subdir in subdirs.OrderByDescending(d => d))
            {
                stack.Push(subdir);
            }

            // Enumerate files
            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (BinaryExtensions.Contains(ext))
                    continue;

                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > MaxFileSizeBytes)
                        continue;

                    if (fileInfo.Length == 0)
                        continue;

                    // Check first bytes for binary content
                    if (IsBinaryByContent(file))
                        continue;
                }
                catch
                {
                    continue;
                }

                var relativeFilePath = Path.GetRelativePath(rootPath, file).Replace('\\', '/');

                if (IsGitignored(relativeFilePath, gitignorePatterns))
                    continue;

                // Check include patterns (if specified, file must match at least one)
                if (includePatterns != null && includePatterns.Count > 0)
                {
                    if (!includePatterns.Any(p => p.IsMatch(relativeFilePath) || p.IsMatch(Path.GetFileName(file))))
                        continue;
                }

                // Check exclude patterns
                if (excludePatterns != null && excludePatterns.Count > 0)
                {
                    if (excludePatterns.Any(p => p.IsMatch(relativeFilePath) || p.IsMatch(Path.GetFileName(file))))
                        continue;
                }

                yield return file;
            }
        }
    }

    private static bool IsBinaryByContent(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[Math.Min(BinaryCheckSize, stream.Length)];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0) return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static List<Regex> LoadGitignorePatterns(string rootPath)
    {
        var patterns = new List<Regex>();
        var gitignorePath = Path.Combine(rootPath, ".gitignore");

        if (!File.Exists(gitignorePath)) return patterns;

        try
        {
            foreach (var line in File.ReadAllLines(gitignorePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                var regex = GlobToRegex(trimmed);
                if (regex != null)
                {
                    patterns.Add(regex);
                }
            }
        }
        catch
        {
            // Ignore gitignore parse errors
        }

        return patterns;
    }

    private static bool IsGitignored(string relativePath, List<Regex> patterns)
    {
        return patterns.Any(p => p.IsMatch(relativePath));
    }

    private static List<Regex>? ParseGlobPatterns(string? patternString)
    {
        if (string.IsNullOrWhiteSpace(patternString)) return null;

        var patterns = new List<Regex>();
        var parts = patternString.Split(',', ';').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p));

        foreach (var part in parts)
        {
            var regex = GlobToRegex(part);
            if (regex != null)
            {
                patterns.Add(regex);
            }
        }

        return patterns.Count > 0 ? patterns : null;
    }

    private static Regex? GlobToRegex(string glob)
    {
        try
        {
            var negated = glob.StartsWith('!');
            var pattern = negated ? glob.Substring(1) : glob;
            pattern = pattern.TrimEnd('/');

            // Convert glob to regex
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*\\*/", "(.+/)?")
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*")
                .Replace("\\?", "[^/]") + "(/.*)?$";

            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch
        {
            return null;
        }
    }

    private static string PreserveCaseReplace(string original, string replacement)
    {
        if (string.IsNullOrEmpty(replacement)) return replacement;

        if (original == original.ToUpper())
            return replacement.ToUpper();
        if (original == original.ToLower())
            return replacement.ToLower();
        if (char.IsUpper(original[0]) && original.Substring(1) == original.Substring(1).ToLower())
            return char.ToUpper(replacement[0]) + replacement.Substring(1).ToLower();

        return replacement;
    }

    #endregion
}

/// <summary>
/// Options for file search operations.
/// </summary>
public class SearchOptions
{
    public bool IsRegex { get; set; }
    public bool IsCaseSensitive { get; set; }
    public bool IsWholeWord { get; set; }
    public bool PreserveCase { get; set; }
    public string? IncludePattern { get; set; }
    public string? ExcludePattern { get; set; }
}

/// <summary>
/// Search result for a single file containing matches.
/// </summary>
public class FileSearchMatchResult
{
    public string FilePath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public List<SearchMatch> Matches { get; set; } = new();
    public int MatchCount => Matches.Count;
}

/// <summary>
/// A single match within a file.
/// </summary>
public class SearchMatch
{
    public int LineNumber { get; set; }
    public int Column { get; set; }
    public int MatchLength { get; set; }
    public string LineText { get; set; } = "";
    public string PreviewBefore { get; set; } = "";
    public string MatchText { get; set; } = "";
    public string PreviewAfter { get; set; } = "";
}

/// <summary>
/// Progress information for search operations.
/// </summary>
public class SearchProgressInfo
{
    public int FilesSearched { get; set; }
    public int TotalFiles { get; set; }
    public int MatchesFound { get; set; }
    public bool IsComplete { get; set; }
    public bool LimitReached { get; set; }
}
