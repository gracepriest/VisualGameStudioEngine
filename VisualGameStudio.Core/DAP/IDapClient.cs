using System.Text.Json;

namespace VisualGameStudio.Core.DAP;

/// <summary>
/// Generic DAP client interface for connecting to any debug adapter
/// </summary>
public interface IDapClient : IDisposable
{
    /// <summary>
    /// Whether the client is connected
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Capabilities of the debug adapter
    /// </summary>
    DapCapabilities? Capabilities { get; }

    /// <summary>
    /// Initialize the debug adapter
    /// </summary>
    Task<bool> InitializeAsync(string adapterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the debug adapter
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Send a request to the debug adapter
    /// </summary>
    Task<TResponse?> SendRequestAsync<TResponse>(string command, object? arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when debug adapter sends an event
    /// </summary>
    event EventHandler<DapEventArgs>? EventReceived;

    /// <summary>
    /// Event raised when debugging is stopped
    /// </summary>
    event EventHandler<StoppedEventArgs>? Stopped;

    /// <summary>
    /// Event raised when debugging is continued
    /// </summary>
    event EventHandler<ContinuedEventArgs>? Continued;

    /// <summary>
    /// Event raised when debugging session terminates
    /// </summary>
    event EventHandler<TerminatedEventArgs>? Terminated;

    /// <summary>
    /// Event raised when output is received
    /// </summary>
    event EventHandler<OutputEventArgs>? Output;

    /// <summary>
    /// Event raised when a breakpoint is hit
    /// </summary>
    event EventHandler<BreakpointEventArgs>? BreakpointHit;

    /// <summary>
    /// Event raised when a thread is started or exited
    /// </summary>
    event EventHandler<ThreadEventArgs>? ThreadEvent;

    #region Debug Operations

    /// <summary>
    /// Launch a program for debugging
    /// </summary>
    Task<bool> LaunchAsync(LaunchRequestArguments args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attach to a running process
    /// </summary>
    Task<bool> AttachAsync(AttachRequestArguments args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set breakpoints for a source file
    /// </summary>
    Task<SetBreakpointsResponse?> SetBreakpointsAsync(string sourcePath, IReadOnlyList<SourceBreakpoint> breakpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continue execution
    /// </summary>
    Task<bool> ContinueAsync(int threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause execution
    /// </summary>
    Task<bool> PauseAsync(int threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Step over
    /// </summary>
    Task<bool> NextAsync(int threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Step into
    /// </summary>
    Task<bool> StepInAsync(int threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Step out
    /// </summary>
    Task<bool> StepOutAsync(int threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all threads
    /// </summary>
    Task<IReadOnlyList<DapThread>?> GetThreadsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get stack trace for a thread
    /// </summary>
    Task<IReadOnlyList<DapStackFrame>?> GetStackTraceAsync(int threadId, int? startFrame = null, int? levels = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get scopes for a stack frame
    /// </summary>
    Task<IReadOnlyList<Scope>?> GetScopesAsync(int frameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get variables for a scope
    /// </summary>
    Task<IReadOnlyList<Variable>?> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluate an expression
    /// </summary>
    Task<EvaluateResponse?> EvaluateAsync(string expression, int? frameId = null, string? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminate the debug session
    /// </summary>
    Task TerminateAsync(CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Event args for DAP events
/// </summary>
public class DapEventArgs : EventArgs
{
    public string Event { get; set; } = "";
    public JsonElement? Body { get; set; }
}

/// <summary>
/// Event args for stopped event
/// </summary>
public class StoppedEventArgs : EventArgs
{
    public string Reason { get; set; } = "";
    public int ThreadId { get; set; }
    public bool AllThreadsStopped { get; set; }
    public string? Description { get; set; }
    public string? Text { get; set; }
}

/// <summary>
/// Event args for continued event
/// </summary>
public class ContinuedEventArgs : EventArgs
{
    public int ThreadId { get; set; }
    public bool AllThreadsContinued { get; set; }
}

/// <summary>
/// Event args for terminated event
/// </summary>
public class TerminatedEventArgs : EventArgs
{
    public bool Restart { get; set; }
}

/// <summary>
/// Event args for output event
/// </summary>
public class OutputEventArgs : EventArgs
{
    public string Category { get; set; } = "";
    public string Output { get; set; } = "";
    public DapSource? Source { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

/// <summary>
/// Event args for breakpoint events
/// </summary>
public class BreakpointEventArgs : EventArgs
{
    public string Reason { get; set; } = "";
    public Breakpoint Breakpoint { get; set; } = new();
}

/// <summary>
/// Event args for thread events
/// </summary>
public class ThreadEventArgs : EventArgs
{
    public string Reason { get; set; } = "";
    public int ThreadId { get; set; }
}

/// <summary>
/// Debug adapter capabilities
/// </summary>
public class DapCapabilities
{
    public bool SupportsConfigurationDoneRequest { get; set; }
    public bool SupportsFunctionBreakpoints { get; set; }
    public bool SupportsConditionalBreakpoints { get; set; }
    public bool SupportsHitConditionalBreakpoints { get; set; }
    public bool SupportsEvaluateForHovers { get; set; }
    public bool SupportsStepBack { get; set; }
    public bool SupportsSetVariable { get; set; }
    public bool SupportsRestartFrame { get; set; }
    public bool SupportsGotoTargetsRequest { get; set; }
    public bool SupportsStepInTargetsRequest { get; set; }
    public bool SupportsCompletionsRequest { get; set; }
    public bool SupportsModulesRequest { get; set; }
    public bool SupportsRestartRequest { get; set; }
    public bool SupportsExceptionOptions { get; set; }
    public bool SupportsValueFormattingOptions { get; set; }
    public bool SupportsExceptionInfoRequest { get; set; }
    public bool SupportTerminateDebuggee { get; set; }
    public bool SupportsDelayedStackTraceLoading { get; set; }
    public bool SupportsLoadedSourcesRequest { get; set; }
    public bool SupportsLogPoints { get; set; }
    public bool SupportsTerminateThreadsRequest { get; set; }
    public bool SupportsSetExpression { get; set; }
    public bool SupportsTerminateRequest { get; set; }
    public bool SupportsDataBreakpoints { get; set; }
    public bool SupportsReadMemoryRequest { get; set; }
    public bool SupportsWriteMemoryRequest { get; set; }
    public bool SupportsDisassembleRequest { get; set; }
    public bool SupportsCancelRequest { get; set; }
    public bool SupportsBreakpointLocationsRequest { get; set; }
    public bool SupportsClipboardContext { get; set; }
    public bool SupportsSteppingGranularity { get; set; }
    public bool SupportsInstructionBreakpoints { get; set; }
}

/// <summary>
/// Launch request arguments
/// </summary>
public class LaunchRequestArguments
{
    public bool NoDebug { get; set; }
    public string? Program { get; set; }
    public List<string>? Args { get; set; }
    public string? Cwd { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public bool StopOnEntry { get; set; }
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}

/// <summary>
/// Attach request arguments
/// </summary>
public class AttachRequestArguments
{
    public int? ProcessId { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}

/// <summary>
/// Source breakpoint
/// </summary>
public class SourceBreakpoint
{
    public int Line { get; set; }
    public int? Column { get; set; }
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }
    public string? LogMessage { get; set; }
}

/// <summary>
/// Set breakpoints response
/// </summary>
public class SetBreakpointsResponse
{
    public List<Breakpoint> Breakpoints { get; set; } = new();
}

/// <summary>
/// Breakpoint
/// </summary>
public class Breakpoint
{
    public int Id { get; set; }
    public bool Verified { get; set; }
    public string? Message { get; set; }
    public DapSource? Source { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
}

/// <summary>
/// Source reference
/// </summary>
public class DapSource
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public int? SourceReference { get; set; }
    public string? PresentationHint { get; set; }
    public string? Origin { get; set; }
}

/// <summary>
/// Thread
/// </summary>
public class DapThread
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Stack frame (named DapStackFrame to avoid conflict with System.Diagnostics.StackFrame)
/// </summary>
public class DapStackFrame
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DapSource? Source { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public bool CanRestart { get; set; }
    public string? InstructionPointerReference { get; set; }
    public int? ModuleId { get; set; }
    public string? PresentationHint { get; set; }
}

/// <summary>
/// Scope
/// </summary>
public class Scope
{
    public string Name { get; set; } = "";
    public string? PresentationHint { get; set; }
    public int VariablesReference { get; set; }
    public int? NamedVariables { get; set; }
    public int? IndexedVariables { get; set; }
    public bool Expensive { get; set; }
    public DapSource? Source { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
}

/// <summary>
/// Variable
/// </summary>
public class Variable
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Type { get; set; }
    public string? PresentationHint { get; set; }
    public string? EvaluateName { get; set; }
    public int VariablesReference { get; set; }
    public int? NamedVariables { get; set; }
    public int? IndexedVariables { get; set; }
    public string? MemoryReference { get; set; }
}

/// <summary>
/// Evaluate response
/// </summary>
public class EvaluateResponse
{
    public string Result { get; set; } = "";
    public string? Type { get; set; }
    public string? PresentationHint { get; set; }
    public int VariablesReference { get; set; }
    public int? NamedVariables { get; set; }
    public int? IndexedVariables { get; set; }
    public string? MemoryReference { get; set; }
}
