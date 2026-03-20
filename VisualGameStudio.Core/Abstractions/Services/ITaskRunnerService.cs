using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for loading, saving, and executing tasks defined in .vgs/tasks.json.
/// Provides VS Code-style task runner functionality.
/// </summary>
public interface ITaskRunnerService
{
    /// <summary>
    /// Loads the tasks configuration from .vgs/tasks.json in the given project path.
    /// Returns default tasks if no configuration file exists.
    /// </summary>
    Task<TasksConfig> LoadTasksAsync(string projectPath);

    /// <summary>
    /// Saves the tasks configuration to .vgs/tasks.json in the given project path.
    /// </summary>
    Task SaveTasksAsync(string projectPath, TasksConfig config);

    /// <summary>
    /// Runs a task, sending output to the terminal/output panel.
    /// </summary>
    Task<int> RunTaskAsync(TaskDefinition task, string workspaceFolder, CancellationToken ct = default);

    /// <summary>
    /// Gets the default build task (group=build, isDefault=true), or null if none defined.
    /// </summary>
    Task<TaskDefinition?> GetDefaultBuildTaskAsync(string projectPath);

    /// <summary>
    /// Gets the default test task (group=test, isDefault=true), or null if none defined.
    /// </summary>
    Task<TaskDefinition?> GetDefaultTestTaskAsync(string projectPath);

    /// <summary>
    /// Returns all available tasks: user-configured from tasks.json plus auto-detected tasks.
    /// </summary>
    IReadOnlyList<TaskDefinition> GetAvailableTasks(string projectPath);

    /// <summary>
    /// Returns the path to the tasks.json file for a given project.
    /// </summary>
    string GetTasksFilePath(string projectPath);

    /// <summary>
    /// Creates a default tasks.json template file if one does not exist.
    /// Returns the file path.
    /// </summary>
    Task<string> CreateDefaultTasksFileAsync(string projectPath);

    /// <summary>
    /// Raised when a task starts executing.
    /// </summary>
    event EventHandler<TaskEventArgs> TaskStarted;

    /// <summary>
    /// Raised when a task finishes executing.
    /// </summary>
    event EventHandler<TaskEventArgs> TaskCompleted;

    /// <summary>
    /// Raised when a task produces output text.
    /// </summary>
    event EventHandler<TaskOutputEventArgs> TaskOutput;
}

/// <summary>
/// Event arguments for task output events.
/// </summary>
public class TaskOutputEventArgs : EventArgs
{
    public TaskDefinition Task { get; }
    public string Text { get; }
    public bool IsError { get; }

    public TaskOutputEventArgs(TaskDefinition task, string text, bool isError = false)
    {
        Task = task;
        Text = text;
        IsError = isError;
    }
}
