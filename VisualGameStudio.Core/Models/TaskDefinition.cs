namespace VisualGameStudio.Core.Models;

/// <summary>
/// Defines a single task that can be run from the task runner system.
/// Equivalent to a task entry in VS Code's tasks.json.
/// </summary>
public class TaskDefinition
{
    /// <summary>
    /// Human-readable label for the task, shown in the task picker.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Task type: "shell" runs in a shell, "process" runs directly.
    /// </summary>
    public string Type { get; set; } = "shell";

    /// <summary>
    /// The command to execute (executable name or shell command).
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Arguments passed to the command.
    /// </summary>
    public string[]? Args { get; set; }

    /// <summary>
    /// Working directory for the task. Supports ${workspaceFolder} variable.
    /// </summary>
    public string? Cwd { get; set; }

    /// <summary>
    /// Additional environment variables for the task.
    /// </summary>
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Task group: "build", "test", or "none".
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Whether this is the default task for its group.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Problem matcher name to parse errors from output: "$gcc", "$msCompile", etc.
    /// </summary>
    public string? ProblemMatcher { get; set; }

    /// <summary>
    /// Controls how the task output is presented in the terminal.
    /// </summary>
    public TaskPresentation? Presentation { get; set; }

    /// <summary>
    /// Whether this task was auto-detected rather than explicitly configured.
    /// </summary>
    public bool IsAutoDetected { get; set; }

    /// <summary>
    /// Returns the full command string including arguments.
    /// </summary>
    public string GetFullCommand()
    {
        if (Args == null || Args.Length == 0)
            return Command;
        return $"{Command} {string.Join(" ", Args)}";
    }

    public override string ToString() => Label;
}

/// <summary>
/// Controls how task output is presented in the terminal panel.
/// </summary>
public class TaskPresentation
{
    /// <summary>
    /// When to reveal the terminal: "always", "silent", "never".
    /// </summary>
    public string Reveal { get; set; } = "always";

    /// <summary>
    /// Whether to focus the terminal when the task runs.
    /// </summary>
    public bool Focus { get; set; }

    /// <summary>
    /// Whether to use a dedicated panel for this task.
    /// </summary>
    public bool Panel { get; set; } = true;

    /// <summary>
    /// Whether to clear the terminal before running.
    /// </summary>
    public bool Clear { get; set; }
}

/// <summary>
/// Root configuration object for .vgs/tasks.json.
/// </summary>
public class TasksConfig
{
    /// <summary>
    /// Configuration file version.
    /// </summary>
    public string Version { get; set; } = "2.0.0";

    /// <summary>
    /// List of task definitions.
    /// </summary>
    public List<TaskDefinition> Tasks { get; set; } = new();
}

/// <summary>
/// Event arguments for task lifecycle events.
/// </summary>
public class TaskEventArgs : EventArgs
{
    /// <summary>
    /// The task that triggered the event.
    /// </summary>
    public TaskDefinition Task { get; }

    /// <summary>
    /// Exit code of the task process, if completed.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Whether the task completed successfully.
    /// </summary>
    public bool Success => ExitCode.HasValue && ExitCode.Value == 0;

    /// <summary>
    /// Duration of the task execution.
    /// </summary>
    public TimeSpan Duration { get; set; }

    public TaskEventArgs(TaskDefinition task)
    {
        Task = task;
    }
}
