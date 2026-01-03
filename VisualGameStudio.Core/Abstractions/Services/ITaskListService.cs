namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides task list/TODO comment management functionality.
/// </summary>
public interface ITaskListService
{
    /// <summary>
    /// Gets or sets the task list options.
    /// </summary>
    TaskListOptions Options { get; set; }

    /// <summary>
    /// Gets the current tasks.
    /// </summary>
    IReadOnlyList<TaskItem> Tasks { get; }

    /// <summary>
    /// Scans a document for task comments.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="filePath">The file path.</param>
    /// <returns>List of tasks found.</returns>
    IReadOnlyList<TaskItem> ScanDocument(string content, string filePath);

    /// <summary>
    /// Scans multiple files for task comments.
    /// </summary>
    /// <param name="filePaths">The file paths to scan.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>All tasks found across files.</returns>
    Task<IReadOnlyList<TaskItem>> ScanFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the task list with tasks from a document.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="filePath">The file path.</param>
    void UpdateDocument(string content, string filePath);

    /// <summary>
    /// Removes all tasks from a file from the list.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    void RemoveFile(string filePath);

    /// <summary>
    /// Clears all tasks.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets tasks filtered by type.
    /// </summary>
    /// <param name="type">The task type to filter by.</param>
    /// <returns>Filtered tasks.</returns>
    IReadOnlyList<TaskItem> GetByType(TaskType type);

    /// <summary>
    /// Gets tasks filtered by priority.
    /// </summary>
    /// <param name="priority">The priority to filter by.</param>
    /// <returns>Filtered tasks.</returns>
    IReadOnlyList<TaskItem> GetByPriority(TaskPriority priority);

    /// <summary>
    /// Gets tasks for a specific file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>Tasks in the file.</returns>
    IReadOnlyList<TaskItem> GetByFile(string filePath);

    /// <summary>
    /// Searches tasks by text.
    /// </summary>
    /// <param name="searchText">The text to search for.</param>
    /// <returns>Matching tasks.</returns>
    IReadOnlyList<TaskItem> Search(string searchText);

    /// <summary>
    /// Gets task statistics.
    /// </summary>
    /// <returns>Statistics about the tasks.</returns>
    TaskStatistics GetStatistics();

    /// <summary>
    /// Adds a custom task token.
    /// </summary>
    /// <param name="token">The token to add.</param>
    /// <param name="type">The task type.</param>
    /// <param name="priority">The priority.</param>
    void AddCustomToken(string token, TaskType type, TaskPriority priority);

    /// <summary>
    /// Removes a custom task token.
    /// </summary>
    /// <param name="token">The token to remove.</param>
    void RemoveCustomToken(string token);

    /// <summary>
    /// Raised when tasks are added.
    /// </summary>
    event EventHandler<TaskListEventArgs>? TasksAdded;

    /// <summary>
    /// Raised when tasks are removed.
    /// </summary>
    event EventHandler<TaskListEventArgs>? TasksRemoved;

    /// <summary>
    /// Raised when the task list is updated.
    /// </summary>
    event EventHandler<TaskListEventArgs>? TaskListUpdated;
}

/// <summary>
/// Options for the task list service.
/// </summary>
public class TaskListOptions
{
    /// <summary>
    /// Gets or sets the tokens to scan for.
    /// </summary>
    public Dictionary<string, TaskTokenInfo> Tokens { get; set; } = new()
    {
        { "TODO", new TaskTokenInfo { Type = TaskType.Todo, Priority = TaskPriority.Normal } },
        { "FIXME", new TaskTokenInfo { Type = TaskType.Fixme, Priority = TaskPriority.High } },
        { "HACK", new TaskTokenInfo { Type = TaskType.Hack, Priority = TaskPriority.Normal } },
        { "BUG", new TaskTokenInfo { Type = TaskType.Bug, Priority = TaskPriority.High } },
        { "NOTE", new TaskTokenInfo { Type = TaskType.Note, Priority = TaskPriority.Low } },
        { "UNDONE", new TaskTokenInfo { Type = TaskType.Undone, Priority = TaskPriority.Normal } },
        { "XXX", new TaskTokenInfo { Type = TaskType.Warning, Priority = TaskPriority.High } }
    };

    /// <summary>
    /// Gets or sets whether to include user tasks (user-defined in code).
    /// </summary>
    public bool IncludeUserTasks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to scan comments only.
    /// </summary>
    public bool CommentsOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the token matching is case sensitive.
    /// </summary>
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Gets or sets the comment prefixes to recognize.
    /// </summary>
    public List<string> CommentPrefixes { get; set; } = new() { "'", "//", "REM" };
}

/// <summary>
/// Information about a task token.
/// </summary>
public class TaskTokenInfo
{
    /// <summary>
    /// Gets or sets the task type.
    /// </summary>
    public TaskType Type { get; set; }

    /// <summary>
    /// Gets or sets the priority.
    /// </summary>
    public TaskPriority Priority { get; set; }
}

/// <summary>
/// Represents a task item found in code.
/// </summary>
public class TaskItem
{
    /// <summary>
    /// Gets or sets the unique ID.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Gets or sets the line number.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the column number.
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets or sets the task text/description.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Gets or sets the token that triggered this task (e.g., "TODO").
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// Gets or sets the task type.
    /// </summary>
    public TaskType Type { get; set; }

    /// <summary>
    /// Gets or sets the priority.
    /// </summary>
    public TaskPriority Priority { get; set; }

    /// <summary>
    /// Gets or sets when this task was found.
    /// </summary>
    public DateTime FoundAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Gets or sets the full line text.
    /// </summary>
    public string LineText { get; set; } = "";

    /// <summary>
    /// Gets or sets the assignee if specified in comment.
    /// </summary>
    public string? Assignee { get; set; }

    /// <summary>
    /// Gets or sets any tag/category in the comment.
    /// </summary>
    public string? Tag { get; set; }
}

/// <summary>
/// Types of tasks.
/// </summary>
public enum TaskType
{
    /// <summary>A TODO item.</summary>
    Todo,
    /// <summary>A FIXME item.</summary>
    Fixme,
    /// <summary>A HACK/workaround.</summary>
    Hack,
    /// <summary>A bug marker.</summary>
    Bug,
    /// <summary>A note.</summary>
    Note,
    /// <summary>Undone/incomplete work.</summary>
    Undone,
    /// <summary>Warning marker.</summary>
    Warning,
    /// <summary>User-defined task type.</summary>
    UserDefined
}

/// <summary>
/// Task priorities.
/// </summary>
public enum TaskPriority
{
    /// <summary>Low priority.</summary>
    Low = 0,
    /// <summary>Normal priority.</summary>
    Normal = 1,
    /// <summary>High priority.</summary>
    High = 2,
    /// <summary>Critical priority.</summary>
    Critical = 3
}

/// <summary>
/// Statistics about tasks.
/// </summary>
public class TaskStatistics
{
    /// <summary>
    /// Gets or sets the total task count.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets counts by type.
    /// </summary>
    public Dictionary<TaskType, int> ByType { get; set; } = new();

    /// <summary>
    /// Gets or sets counts by priority.
    /// </summary>
    public Dictionary<TaskPriority, int> ByPriority { get; set; } = new();

    /// <summary>
    /// Gets or sets counts by file.
    /// </summary>
    public Dictionary<string, int> ByFile { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of unique files with tasks.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Gets or sets when the statistics were calculated.
    /// </summary>
    public DateTime CalculatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Event arguments for task list events.
/// </summary>
public class TaskListEventArgs : EventArgs
{
    /// <summary>
    /// Gets the tasks involved.
    /// </summary>
    public IReadOnlyList<TaskItem> Tasks { get; }

    /// <summary>
    /// Gets the file path if applicable.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public TaskListEventArgs(IReadOnlyList<TaskItem> tasks, string? filePath = null)
    {
        Tasks = tasks;
        FilePath = filePath;
    }
}
