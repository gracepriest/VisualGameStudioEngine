using System.Text;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Provides find and replace functionality for BasicLang source files.
/// </summary>
public class FindReplaceService : IFindReplaceService
{
    /// <inheritdoc/>
    public FindReplaceOptions Options { get; set; } = new();

    /// <inheritdoc/>
    public event EventHandler<FindReplaceEventArgs>? SearchStarted;

    /// <inheritdoc/>
    public event EventHandler<FindReplaceEventArgs>? SearchCompleted;

    /// <inheritdoc/>
    public event EventHandler<FindReplaceProgressEventArgs>? SearchProgress;

    /// <inheritdoc/>
    public IReadOnlyList<FindMatch> FindInDocument(string content, string pattern)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(pattern))
        {
            return Array.Empty<FindMatch>();
        }

        var matches = new List<FindMatch>();
        var regex = CreateRegex(pattern);

        if (regex == null)
        {
            return Array.Empty<FindMatch>();
        }

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var lineOffsets = CalculateLineOffsets(lines);

        foreach (Match match in regex.Matches(content))
        {
            var (line, column) = GetLineAndColumn(match.Index, lineOffsets);
            var lineText = line > 0 && line <= lines.Length ? lines[line - 1] : "";

            matches.Add(new FindMatch
            {
                StartOffset = match.Index,
                Length = match.Length,
                MatchedText = match.Value,
                Line = line,
                Column = column,
                LineText = lineText,
                Groups = match.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToList()
            });
        }

        return matches;
    }

    /// <inheritdoc/>
    public FindMatch? FindNext(string content, string pattern, int startOffset)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        var regex = CreateRegex(pattern);
        if (regex == null)
        {
            return null;
        }

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var lineOffsets = CalculateLineOffsets(lines);

        // Search from startOffset
        var match = regex.Match(content, Math.Min(startOffset, content.Length));
        if (match.Success)
        {
            var (line, column) = GetLineAndColumn(match.Index, lineOffsets);
            return new FindMatch
            {
                StartOffset = match.Index,
                Length = match.Length,
                MatchedText = match.Value,
                Line = line,
                Column = column,
                LineText = line > 0 && line <= lines.Length ? lines[line - 1] : "",
                Groups = match.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToList()
            };
        }

        // Wrap around if enabled
        if (Options.WrapAround && startOffset > 0)
        {
            match = regex.Match(content, 0);
            if (match.Success && match.Index < startOffset)
            {
                var (line, column) = GetLineAndColumn(match.Index, lineOffsets);
                return new FindMatch
                {
                    StartOffset = match.Index,
                    Length = match.Length,
                    MatchedText = match.Value,
                    Line = line,
                    Column = column,
                    LineText = line > 0 && line <= lines.Length ? lines[line - 1] : "",
                    Groups = match.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToList()
                };
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public FindMatch? FindPrevious(string content, string pattern, int startOffset)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        var allMatches = FindInDocument(content, pattern);
        if (allMatches.Count == 0)
        {
            return null;
        }

        // Find the last match before startOffset
        FindMatch? previous = null;
        foreach (var match in allMatches)
        {
            if (match.StartOffset < startOffset)
            {
                previous = match;
            }
            else
            {
                break;
            }
        }

        // Wrap around if enabled
        if (previous == null && Options.WrapAround && allMatches.Count > 0)
        {
            previous = allMatches[^1];
        }

        return previous;
    }

    /// <inheritdoc/>
    public string ReplaceOne(string content, FindMatch match, string replacement)
    {
        if (string.IsNullOrEmpty(content) || match == null)
        {
            return content;
        }

        var actualReplacement = ProcessReplacement(match.MatchedText, replacement);

        return content.Substring(0, match.StartOffset) +
               actualReplacement +
               content.Substring(match.EndOffset);
    }

    /// <inheritdoc/>
    public ReplaceAllResult ReplaceAll(string content, string pattern, string replacement)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(pattern))
        {
            return new ReplaceAllResult { Content = content };
        }

        var matches = FindInDocument(content, pattern);
        if (matches.Count == 0)
        {
            return new ReplaceAllResult { Content = content };
        }

        var result = new StringBuilder();
        var replacements = new List<ReplacementInfo>();
        var lastEnd = 0;

        foreach (var match in matches)
        {
            // Add content before this match
            result.Append(content.Substring(lastEnd, match.StartOffset - lastEnd));

            // Process and add replacement
            var actualReplacement = ProcessReplacement(match.MatchedText, replacement);
            result.Append(actualReplacement);

            replacements.Add(new ReplacementInfo
            {
                OriginalText = match.MatchedText,
                ReplacementText = actualReplacement,
                Offset = match.StartOffset,
                Line = match.Line
            });

            lastEnd = match.EndOffset;
        }

        // Add remaining content
        result.Append(content.Substring(lastEnd));

        return new ReplaceAllResult
        {
            Content = result.ToString(),
            ReplacementCount = replacements.Count,
            Replacements = replacements
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileSearchResult>> FindInFilesAsync(
        IEnumerable<string> filePaths,
        string pattern,
        string? filePattern = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return Array.Empty<FileSearchResult>();
        }

        var pathList = filePaths.ToList();
        var results = new List<FileSearchResult>();
        var totalMatches = 0;

        SearchStarted?.Invoke(this, new FindReplaceEventArgs(pattern));

        var filteredPaths = FilterFilesByPattern(pathList, filePattern);
        var totalFiles = filteredPaths.Count;
        var filesSearched = 0;

        foreach (var filePath in filteredPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(filePath))
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var matches = FindInDocument(content, pattern);

                    if (matches.Count > 0)
                    {
                        results.Add(new FileSearchResult
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            Matches = matches
                        });
                        totalMatches += matches.Count;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Skip files that can't be read
            }

            filesSearched++;
            SearchProgress?.Invoke(this, new FindReplaceProgressEventArgs(
                filePath, filesSearched, totalFiles, totalMatches));
        }

        SearchCompleted?.Invoke(this, new FindReplaceEventArgs(pattern, totalMatches));

        return results;
    }

    /// <inheritdoc/>
    public async Task<ReplaceInFilesResult> ReplaceInFilesAsync(
        IEnumerable<string> filePaths,
        string pattern,
        string replacement,
        string? filePattern = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return new ReplaceInFilesResult();
        }

        var pathList = filePaths.ToList();
        var modifiedFiles = new List<FileReplacementInfo>();
        var errors = new List<FileOperationError>();
        var totalReplacements = 0;

        SearchStarted?.Invoke(this, new FindReplaceEventArgs(pattern));

        var filteredPaths = FilterFilesByPattern(pathList, filePattern);
        var totalFiles = filteredPaths.Count;
        var filesProcessed = 0;

        foreach (var filePath in filteredPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(filePath))
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var result = ReplaceAll(content, pattern, replacement);

                    if (result.ReplacementCount > 0)
                    {
                        await File.WriteAllTextAsync(filePath, result.Content, cancellationToken);

                        modifiedFiles.Add(new FileReplacementInfo
                        {
                            FilePath = filePath,
                            ReplacementCount = result.ReplacementCount,
                            BackupCreated = false
                        });
                        totalReplacements += result.ReplacementCount;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError
                {
                    FilePath = filePath,
                    Message = ex.Message,
                    Exception = ex
                });
            }

            filesProcessed++;
            SearchProgress?.Invoke(this, new FindReplaceProgressEventArgs(
                filePath, filesProcessed, totalFiles, totalReplacements));
        }

        SearchCompleted?.Invoke(this, new FindReplaceEventArgs(pattern, totalReplacements));

        return new ReplaceInFilesResult
        {
            FilesModified = modifiedFiles.Count,
            TotalReplacements = totalReplacements,
            ModifiedFiles = modifiedFiles,
            Errors = errors
        };
    }

    /// <inheritdoc/>
    public bool IsValidPattern(string pattern)
    {
        return GetPatternError(pattern) == null;
    }

    /// <inheritdoc/>
    public string? GetPatternError(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return "Pattern cannot be empty";
        }

        if (Options.UseRegex)
        {
            try
            {
                _ = new Regex(pattern);
                return null;
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
        }

        return null;
    }

    private Regex? CreateRegex(string pattern)
    {
        try
        {
            var regexPattern = Options.UseRegex ? pattern : Regex.Escape(pattern);

            if (Options.WholeWord)
            {
                regexPattern = $@"\b{regexPattern}\b";
            }

            var options = RegexOptions.Multiline;
            if (!Options.CaseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return new Regex(regexPattern, options);
        }
        catch
        {
            return null;
        }
    }

    private string ProcessReplacement(string matchedText, string replacement)
    {
        if (!Options.PreserveCase)
        {
            return replacement;
        }

        // Preserve case: match the case pattern of the original text
        if (string.IsNullOrEmpty(matchedText) || string.IsNullOrEmpty(replacement))
        {
            return replacement;
        }

        // All uppercase
        if (matchedText.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            return replacement.ToUpperInvariant();
        }

        // All lowercase
        if (matchedText.All(c => !char.IsLetter(c) || char.IsLower(c)))
        {
            return replacement.ToLowerInvariant();
        }

        // Title case (first letter uppercase)
        if (char.IsUpper(matchedText[0]))
        {
            return char.ToUpperInvariant(replacement[0]) + replacement.Substring(1).ToLowerInvariant();
        }

        return replacement;
    }

    private static int[] CalculateLineOffsets(string[] lines)
    {
        var offsets = new int[lines.Length + 1];
        offsets[0] = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            // +1 or +2 for line ending (simplified - assumes consistent line endings)
            offsets[i + 1] = offsets[i] + lines[i].Length + 1;
        }

        return offsets;
    }

    private static (int line, int column) GetLineAndColumn(int offset, int[] lineOffsets)
    {
        for (int i = 0; i < lineOffsets.Length - 1; i++)
        {
            if (offset >= lineOffsets[i] && offset < lineOffsets[i + 1])
            {
                return (i + 1, offset - lineOffsets[i] + 1);
            }
        }

        return (lineOffsets.Length - 1, 1);
    }

    private List<string> FilterFilesByPattern(List<string> filePaths, string? filePattern)
    {
        if (string.IsNullOrEmpty(filePattern))
        {
            return filePaths;
        }

        var patterns = filePattern.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (patterns.Count == 0)
        {
            return filePaths;
        }

        return filePaths.Where(path =>
        {
            var fileName = Path.GetFileName(path);
            return patterns.Any(pattern => MatchesWildcard(fileName, pattern));
        }).ToList();
    }

    private static bool MatchesWildcard(string fileName, string pattern)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }
}
