namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Debug service interface for DAP-based debugging
/// </summary>
public interface IDebugService : IDisposable
{
    /// <summary>
    /// Current debug state
    /// </summary>
    DebugState State { get; }

    /// <summary>
    /// Whether a debug session is active
    /// </summary>
    bool IsDebugging { get; }

    /// <summary>
    /// Fires when debug state changes
    /// </summary>
    event EventHandler<DebugStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Fires when execution stops (breakpoint, step, etc.)
    /// </summary>
    event EventHandler<StoppedEventArgs>? Stopped;

    /// <summary>
    /// Fires when debug output is received
    /// </summary>
    event EventHandler<DebugOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Fires when breakpoints are validated by the debugger
    /// </summary>
    event EventHandler<BreakpointsChangedEventArgs>? BreakpointsChanged;

    /// <summary>
    /// Start debugging a project
    /// </summary>
    Task<bool> StartDebuggingAsync(DebugConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start debugging with initial breakpoints
    /// </summary>
    Task<bool> StartDebuggingAsync(DebugConfiguration config, Dictionary<string, IEnumerable<SourceBreakpoint>> breakpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start without debugging (run)
    /// </summary>
    Task<bool> StartWithoutDebuggingAsync(DebugConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send input to the running program's stdin
    /// </summary>
    Task SendInputAsync(string input);

    /// <summary>
    /// Stop the current debug session
    /// </summary>
    Task StopDebuggingAsync();

    /// <summary>
    /// Continue execution
    /// </summary>
    Task ContinueAsync();

    /// <summary>
    /// Step over
    /// </summary>
    Task StepOverAsync();

    /// <summary>
    /// Step into
    /// </summary>
    Task StepIntoAsync();

    /// <summary>
    /// Step out
    /// </summary>
    Task StepOutAsync();

    /// <summary>
    /// Pause execution
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Run to cursor - execute until reaching the specified line
    /// </summary>
    /// <param name="filePath">The file path</param>
    /// <param name="line">The target line</param>
    /// <param name="existingBreakpoints">Existing breakpoints for this file to preserve</param>
    Task RunToCursorAsync(string filePath, int line, IEnumerable<SourceBreakpoint>? existingBreakpoints = null);

    /// <summary>
    /// Set next statement - move execution point to a different line without executing
    /// </summary>
    /// <param name="filePath">The file path</param>
    /// <param name="line">The target line to move execution to</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SetNextStatementAsync(string filePath, int line);

    /// <summary>
    /// Set exception breakpoints - configure which exceptions to break on
    /// </summary>
    /// <param name="filters">Exception filter IDs (e.g., "all", "uncaught", "caught")</param>
    /// <param name="filterOptions">Additional filter options for specific exception types</param>
    Task SetExceptionBreakpointsAsync(IEnumerable<string> filters, IEnumerable<ExceptionFilterOption>? filterOptions = null);

    /// <summary>
    /// Set breakpoints for a file
    /// </summary>
    Task<IReadOnlyList<BreakpointInfo>> SetBreakpointsAsync(string filePath, IEnumerable<SourceBreakpoint> breakpoints);

    /// <summary>
    /// Set function breakpoints
    /// </summary>
    Task<IReadOnlyList<FunctionBreakpointInfo>> SetFunctionBreakpointsAsync(IEnumerable<FunctionBreakpoint> breakpoints);

    /// <summary>
    /// Get current call stack
    /// </summary>
    Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId = 1);

    /// <summary>
    /// Get scopes for a stack frame
    /// </summary>
    Task<IReadOnlyList<ScopeInfo>> GetScopesAsync(int frameId);

    /// <summary>
    /// Get variables for a scope or variable reference
    /// </summary>
    Task<IReadOnlyList<VariableInfo>> GetVariablesAsync(int variablesReference);

    /// <summary>
    /// Evaluate an expression in the current context
    /// </summary>
    Task<EvaluateResult> EvaluateAsync(string expression, int? frameId = null);
}

public enum DebugState
{
    NotStarted,
    Initializing,
    Running,
    Paused,
    Stopped
}

public class DebugStateChangedEventArgs : EventArgs
{
    public DebugState OldState { get; set; }
    public DebugState NewState { get; set; }
}

public class StoppedEventArgs : EventArgs
{
    public StopReason Reason { get; set; }
    public int ThreadId { get; set; }
    public string? Description { get; set; }
    public string? Text { get; set; }
    public bool AllThreadsStopped { get; set; }
}

public enum StopReason
{
    Step,
    Breakpoint,
    Exception,
    Pause,
    Entry,
    Goto,
    FunctionBreakpoint,
    DataBreakpoint
}

public class DebugOutputEventArgs : EventArgs
{
    public string Category { get; set; } = "console";
    public string Output { get; set; } = "";
}

public class BreakpointsChangedEventArgs : EventArgs
{
    public string FilePath { get; set; } = "";
    public IReadOnlyList<BreakpointInfo> Breakpoints { get; set; } = Array.Empty<BreakpointInfo>();
}

public class DebugConfiguration
{
    public string Program { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Environment { get; set; } = new();
    public bool StopOnEntry { get; set; }
}

public class SourceBreakpoint
{
    public int Line { get; set; }
    public int? Column { get; set; }
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }
    public string? LogMessage { get; set; }
}

public class BreakpointInfo
{
    public int Id { get; set; }
    public bool Verified { get; set; }
    public string? Message { get; set; }
    public int Line { get; set; }
    public int? Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
}

public class StackFrameInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public string? ModuleName { get; set; }
}

public class ScopeInfo
{
    public string Name { get; set; } = "";
    public int VariablesReference { get; set; }
    public bool Expensive { get; set; }
}

public class VariableInfo
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Type { get; set; }
    public int VariablesReference { get; set; }
}

public class EvaluateResult
{
    public string Result { get; set; } = "";
    public string? Type { get; set; }
    public int VariablesReference { get; set; }
}

public class FunctionBreakpoint
{
    public string Name { get; set; } = "";
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }
}

public class FunctionBreakpointInfo
{
    public int Id { get; set; }
    public bool Verified { get; set; }
    public string? Message { get; set; }
}

public class ExceptionFilterOption
{
    public string FilterId { get; set; } = "";
    public string? Condition { get; set; }
}
