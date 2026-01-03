namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides integrated terminal/console functionality.
/// </summary>
public interface ITerminalService : IDisposable
{
    /// <summary>
    /// Gets the active terminal sessions.
    /// </summary>
    IReadOnlyList<TerminalSession> Sessions { get; }

    /// <summary>
    /// Gets the currently active session.
    /// </summary>
    TerminalSession? ActiveSession { get; }

    /// <summary>
    /// Creates a new terminal session.
    /// </summary>
    /// <param name="options">Options for the terminal session.</param>
    /// <returns>The created terminal session.</returns>
    TerminalSession CreateSession(TerminalOptions? options = null);

    /// <summary>
    /// Closes a terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to close.</param>
    void CloseSession(string sessionId);

    /// <summary>
    /// Sets the active terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to activate.</param>
    void SetActiveSession(string sessionId);

    /// <summary>
    /// Sends input to the active terminal.
    /// </summary>
    /// <param name="input">The input to send.</param>
    void SendInput(string input);

    /// <summary>
    /// Sends input to a specific terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="input">The input to send.</param>
    void SendInput(string sessionId, string input);

    /// <summary>
    /// Executes a command and waits for completion.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The command result.</returns>
    Task<CommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command in the background.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <returns>The session running the command.</returns>
    TerminalSession ExecuteInBackground(string command, string? workingDirectory = null);

    /// <summary>
    /// Clears the terminal output.
    /// </summary>
    /// <param name="sessionId">The session ID to clear, or null for active session.</param>
    void Clear(string? sessionId = null);

    /// <summary>
    /// Gets the output history for a session.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>The output history.</returns>
    IReadOnlyList<TerminalOutput> GetHistory(string sessionId);

    /// <summary>
    /// Raised when output is received from a terminal.
    /// </summary>
    event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Raised when a session is created.
    /// </summary>
    event EventHandler<TerminalSessionEventArgs>? SessionCreated;

    /// <summary>
    /// Raised when a session is closed.
    /// </summary>
    event EventHandler<TerminalSessionEventArgs>? SessionClosed;

    /// <summary>
    /// Raised when the active session changes.
    /// </summary>
    event EventHandler<TerminalSessionEventArgs>? ActiveSessionChanged;

    /// <summary>
    /// Raised when a command completes.
    /// </summary>
    event EventHandler<CommandCompletedEventArgs>? CommandCompleted;
}

/// <summary>
/// Options for creating a terminal session.
/// </summary>
public class TerminalOptions
{
    /// <summary>
    /// Gets or sets the shell to use (e.g., "cmd", "powershell", "bash").
    /// </summary>
    public string? Shell { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets environment variables to set.
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets the terminal name/title.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets whether to use the system shell.
    /// </summary>
    public bool UseSystemShell { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum output history lines.
    /// </summary>
    public int MaxHistoryLines { get; set; } = 10000;
}

/// <summary>
/// Represents a terminal session.
/// </summary>
public class TerminalSession
{
    /// <summary>
    /// Gets the unique session ID.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the session name.
    /// </summary>
    public string Name { get; set; } = "Terminal";

    /// <summary>
    /// Gets or sets the shell being used.
    /// </summary>
    public string Shell { get; set; } = "";

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the session is running.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Gets or sets when the session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Gets or sets the current command being executed.
    /// </summary>
    public string? CurrentCommand { get; set; }

    /// <summary>
    /// Gets or sets the exit code of the last command.
    /// </summary>
    public int? LastExitCode { get; set; }
}

/// <summary>
/// Represents terminal output.
/// </summary>
public class TerminalOutput
{
    /// <summary>
    /// Gets or sets the output text.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Gets or sets the output type.
    /// </summary>
    public TerminalOutputType Type { get; set; }

    /// <summary>
    /// Gets or sets when the output was received.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Types of terminal output.
/// </summary>
public enum TerminalOutputType
{
    /// <summary>Standard output.</summary>
    StandardOutput,
    /// <summary>Standard error.</summary>
    StandardError,
    /// <summary>User input echo.</summary>
    Input,
    /// <summary>System message.</summary>
    System
}

/// <summary>
/// Result of a command execution.
/// </summary>
public class CommandResult
{
    /// <summary>
    /// Gets or sets whether the command succeeded (exit code 0).
    /// </summary>
    public bool Success => ExitCode == 0;

    /// <summary>
    /// Gets or sets the exit code.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Gets or sets the standard output.
    /// </summary>
    public string StandardOutput { get; set; } = "";

    /// <summary>
    /// Gets or sets the standard error.
    /// </summary>
    public string StandardError { get; set; } = "";

    /// <summary>
    /// Gets or sets the command that was executed.
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Gets or sets the execution duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets when the command started.
    /// </summary>
    public DateTime StartTime { get; set; }
}

/// <summary>
/// Event arguments for terminal output.
/// </summary>
public class TerminalOutputEventArgs : EventArgs
{
    /// <summary>
    /// Gets the session ID.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the output.
    /// </summary>
    public TerminalOutput Output { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public TerminalOutputEventArgs(string sessionId, TerminalOutput output)
    {
        SessionId = sessionId;
        Output = output;
    }
}

/// <summary>
/// Event arguments for terminal session events.
/// </summary>
public class TerminalSessionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the session.
    /// </summary>
    public TerminalSession Session { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public TerminalSessionEventArgs(TerminalSession session)
    {
        Session = session;
    }
}

/// <summary>
/// Event arguments for command completion.
/// </summary>
public class CommandCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the session ID.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the command result.
    /// </summary>
    public CommandResult Result { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public CommandCompletedEventArgs(string sessionId, CommandResult result)
    {
        SessionId = sessionId;
        Result = result;
    }
}
