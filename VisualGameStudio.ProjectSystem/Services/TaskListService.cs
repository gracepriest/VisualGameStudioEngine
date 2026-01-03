using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Provides task list/TODO comment management functionality.
/// </summary>
public class TaskListService : ITaskListService
{
    private readonly ConcurrentDictionary<string, List<TaskItem>> _tasksByFile = new();
    private readonly object _lock = new();

    /// <inheritdoc/>
    public TaskListOptions Options { get; set; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<TaskItem> Tasks
    {
        get
        {
            lock (_lock)
            {
                return _tasksByFile.Values.SelectMany(t => t).ToList();
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<TaskListEventArgs>? TasksAdded;

    /// <inheritdoc/>
    public event EventHandler<TaskListEventArgs>? TasksRemoved;

    /// <inheritdoc/>
    public event EventHandler<TaskListEventArgs>? TaskListUpdated;

    /// <inheritdoc/>
    public IReadOnlyList<TaskItem> ScanDocument(string content, string filePath)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<TaskItem>();
        }

        var tasks = new List<TaskItem>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var fileName = Path.GetFileName(filePath);
        var comparison = Options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.TrimStart();

            // Check if this is a comment line
            bool isComment = false;
            string commentContent = line;

            foreach (var prefix in Options.CommentPrefixes)
            {
                if (trimmedLine.StartsWith(prefix, comparison))
                {
                    isComment = true;
                    commentContent = trimmedLine.Substring(prefix.Length).TrimStart();
                    break;
                }
            }

            // If we only scan comments and this isn't a comment, skip
            if (Options.CommentsOnly && !isComment)
            {
                continue;
            }

            // Look for task tokens
            foreach (var token in Options.Tokens)
            {
                var tokenPattern = Options.CaseSensitive
                    ? $@"\b{Regex.Escape(token.Key)}(\s*:|\s+|$)"
                    : $@"(?i)\b{Regex.Escape(token.Key)}(\s*:|\s+|$)";

                var match = Regex.Match(commentContent, tokenPattern);
                if (match.Success)
                {
                    var taskText = ExtractTaskText(commentContent, match.Index + match.Length);
                    var (assignee, tag) = ExtractMetadata(taskText);

                    tasks.Add(new TaskItem
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        Line = i + 1,
                        Column = line.IndexOf(match.Value, comparison) + 1,
                        Text = taskText,
                        Token = token.Key,
                        Type = token.Value.Type,
                        Priority = token.Value.Priority,
                        LineText = line,
                        Assignee = assignee,
                        Tag = tag
                    });
                    break; // Only match first token per line
                }
            }
        }

        return tasks;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TaskItem>> ScanFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var allTasks = new List<TaskItem>();

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(filePath))
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var tasks = ScanDocument(content, filePath);
                    allTasks.AddRange(tasks);
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
        }

        return allTasks;
    }

    /// <inheritdoc/>
    public void UpdateDocument(string content, string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        var oldTasks = new List<TaskItem>();

        lock (_lock)
        {
            if (_tasksByFile.TryGetValue(normalizedPath, out var existing))
            {
                oldTasks = existing.ToList();
            }
        }

        var newTasks = ScanDocument(content, filePath);

        lock (_lock)
        {
            _tasksByFile[normalizedPath] = newTasks.ToList();
        }

        if (oldTasks.Count > 0)
        {
            TasksRemoved?.Invoke(this, new TaskListEventArgs(oldTasks, filePath));
        }

        if (newTasks.Count > 0)
        {
            TasksAdded?.Invoke(this, new TaskListEventArgs(newTasks, filePath));
        }

        TaskListUpdated?.Invoke(this, new TaskListEventArgs(newTasks, filePath));
    }

    /// <inheritdoc/>
    public void RemoveFile(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);

        lock (_lock)
        {
            if (_tasksByFile.TryRemove(normalizedPath, out var removed))
            {
                TasksRemoved?.Invoke(this, new TaskListEventArgs(removed, filePath));
                TaskListUpdated?.Invoke(this, new TaskListEventArgs(Array.Empty<TaskItem>(), filePath));
            }
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        List<TaskItem> allTasks;

        lock (_lock)
        {
            allTasks = _tasksByFile.Values.SelectMany(t => t).ToList();
            _tasksByFile.Clear();
        }

        if (allTasks.Count > 0)
        {
            TasksRemoved?.Invoke(this, new TaskListEventArgs(allTasks));
        }

        TaskListUpdated?.Invoke(this, new TaskListEventArgs(Array.Empty<TaskItem>()));
    }

    /// <inheritdoc/>
    public IReadOnlyList<TaskItem> GetByType(TaskType type)
    {
        return Tasks.Where(t => t.Type == type).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<TaskItem> GetByPriority(TaskPriority priority)
    {
        return Tasks.Where(t => t.Priority == priority).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<TaskItem> GetByFile(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);

        lock (_lock)
        {
            if (_tasksByFile.TryGetValue(normalizedPath, out var tasks))
            {
                return tasks.ToList();
            }
        }

        return Array.Empty<TaskItem>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<TaskItem> Search(string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return Tasks;
        }

        return Tasks.Where(t =>
            t.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            t.FileName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            t.Token.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            (t.Assignee?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true) ||
            (t.Tag?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
        ).ToList();
    }

    /// <inheritdoc/>
    public TaskStatistics GetStatistics()
    {
        var tasks = Tasks;
        var stats = new TaskStatistics
        {
            TotalCount = tasks.Count,
            FileCount = tasks.Select(t => t.FilePath).Distinct().Count()
        };

        // Count by type
        foreach (var type in Enum.GetValues<TaskType>())
        {
            var count = tasks.Count(t => t.Type == type);
            if (count > 0)
            {
                stats.ByType[type] = count;
            }
        }

        // Count by priority
        foreach (var priority in Enum.GetValues<TaskPriority>())
        {
            var count = tasks.Count(t => t.Priority == priority);
            if (count > 0)
            {
                stats.ByPriority[priority] = count;
            }
        }

        // Count by file
        foreach (var group in tasks.GroupBy(t => t.FileName))
        {
            stats.ByFile[group.Key] = group.Count();
        }

        return stats;
    }

    /// <inheritdoc/>
    public void AddCustomToken(string token, TaskType type, TaskPriority priority)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        Options.Tokens[token.ToUpperInvariant()] = new TaskTokenInfo
        {
            Type = type,
            Priority = priority
        };
    }

    /// <inheritdoc/>
    public void RemoveCustomToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        Options.Tokens.Remove(token.ToUpperInvariant());
    }

    #region Private Helper Methods

    private static string ExtractTaskText(string line, int startIndex)
    {
        if (startIndex >= line.Length)
        {
            return "";
        }

        var text = line.Substring(startIndex).Trim();

        // Remove leading colon if present
        if (text.StartsWith(':'))
        {
            text = text.Substring(1).TrimStart();
        }

        return text;
    }

    private static (string? assignee, string? tag) ExtractMetadata(string text)
    {
        string? assignee = null;
        string? tag = null;

        // Look for @username pattern for assignee
        var assigneeMatch = Regex.Match(text, @"@(\w+)");
        if (assigneeMatch.Success)
        {
            assignee = assigneeMatch.Groups[1].Value;
        }

        // Look for #tag or [tag] pattern
        var tagMatch = Regex.Match(text, @"[#\[](\w+)[#\]]?");
        if (tagMatch.Success)
        {
            tag = tagMatch.Groups[1].Value;
        }

        return (assignee, tag);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).ToLowerInvariant();
    }

    #endregion
}
