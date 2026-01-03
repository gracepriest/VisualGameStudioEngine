namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides Debug Adapter Protocol (DAP) client functionality.
/// Enables communication with any DAP-compliant debug adapter.
/// </summary>
public interface IDapClientService : IDisposable
{
    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    DapConnectionState State { get; }

    /// <summary>
    /// Gets the adapter capabilities after initialization.
    /// </summary>
    DapCapabilities? Capabilities { get; }

    /// <summary>
    /// Gets whether the debuggee is currently stopped.
    /// </summary>
    bool IsStopped { get; }

    /// <summary>
    /// Gets the current threads.
    /// </summary>
    IReadOnlyList<DapThread> Threads { get; }

    /// <summary>
    /// Starts a debug adapter process and connects to it.
    /// </summary>
    /// <param name="adapterPath">Path to the debug adapter executable.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> StartAdapterAsync(string adapterPath, string? arguments = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to an existing debug adapter via TCP.
    /// </summary>
    /// <param name="host">The host address.</param>
    /// <param name="port">The port number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the debug adapter.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Launches a program for debugging.
    /// </summary>
    /// <param name="request">The launch request configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches to a running process.
    /// </summary>
    /// <param name="request">The attach request configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> AttachAsync(AttachRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets breakpoints in a source file.
    /// </summary>
    /// <param name="source">The source file.</param>
    /// <param name="breakpoints">The breakpoints to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DapBreakpoint>> SetBreakpointsAsync(DapSource source, IEnumerable<DapSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets function breakpoints.
    /// </summary>
    /// <param name="breakpoints">The function breakpoints.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DapBreakpoint>> SetFunctionBreakpointsAsync(IEnumerable<DapFunctionBreakpoint> breakpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets exception breakpoints.
    /// </summary>
    /// <param name="filters">The exception filter IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetExceptionBreakpointsAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues execution.
    /// </summary>
    /// <param name="threadId">The thread to continue, or null for all.</param>
    Task ContinueAsync(int? threadId = null);

    /// <summary>
    /// Pauses execution.
    /// </summary>
    /// <param name="threadId">The thread to pause, or null for all.</param>
    Task PauseAsync(int? threadId = null);

    /// <summary>
    /// Steps to the next statement.
    /// </summary>
    /// <param name="threadId">The thread ID.</param>
    Task NextAsync(int threadId);

    /// <summary>
    /// Steps into a function.
    /// </summary>
    /// <param name="threadId">The thread ID.</param>
    Task StepInAsync(int threadId);

    /// <summary>
    /// Steps out of the current function.
    /// </summary>
    /// <param name="threadId">The thread ID.</param>
    Task StepOutAsync(int threadId);

    /// <summary>
    /// Terminates the debuggee.
    /// </summary>
    Task TerminateAsync();

    /// <summary>
    /// Restarts the debug session.
    /// </summary>
    Task RestartAsync();

    /// <summary>
    /// Gets the stack trace for a thread.
    /// </summary>
    /// <param name="threadId">The thread ID.</param>
    /// <param name="startFrame">Starting frame index.</param>
    /// <param name="levels">Number of frames to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StackTraceResult> GetStackTraceAsync(int threadId, int startFrame = 0, int levels = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the scopes for a stack frame.
    /// </summary>
    /// <param name="frameId">The frame ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DapScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets variables in a scope.
    /// </summary>
    /// <param name="variablesReference">The variables reference.</param>
    /// <param name="start">Starting index.</param>
    /// <param name="count">Number of variables.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DapVariable>> GetVariablesAsync(int variablesReference, int? start = null, int? count = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a variable value.
    /// </summary>
    /// <param name="variablesReference">The variables reference.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The new value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SetVariableResult> SetVariableAsync(int variablesReference, string name, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates an expression.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="frameId">The stack frame context.</param>
    /// <param name="context">The evaluation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DapEvaluateResult> EvaluateAsync(string expression, int? frameId = null, string? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets completions for an expression.
    /// </summary>
    /// <param name="text">The text to complete.</param>
    /// <param name="column">The cursor column.</param>
    /// <param name="frameId">The stack frame context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CompletionTarget>> GetCompletionsAsync(string text, int column, int? frameId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the connection state changes.
    /// </summary>
    event EventHandler<DapStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised when execution stops.
    /// </summary>
    event EventHandler<DapStoppedEventArgs>? Stopped;

    /// <summary>
    /// Raised when execution continues.
    /// </summary>
    event EventHandler<ContinuedEventArgs>? Continued;

    /// <summary>
    /// Raised when a thread is started.
    /// </summary>
    event EventHandler<ThreadEventArgs>? ThreadStarted;

    /// <summary>
    /// Raised when a thread exits.
    /// </summary>
    event EventHandler<ThreadEventArgs>? ThreadExited;

    /// <summary>
    /// Raised when output is received.
    /// </summary>
    event EventHandler<DapOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Raised when the debuggee terminates.
    /// </summary>
    event EventHandler<TerminatedEventArgs>? Terminated;

    /// <summary>
    /// Raised when a breakpoint changes.
    /// </summary>
    event EventHandler<BreakpointEventArgs>? BreakpointChanged;

    /// <summary>
    /// Raised when a module is loaded/unloaded.
    /// </summary>
    event EventHandler<ModuleEventArgs>? ModuleChanged;
}

#region DAP Types

/// <summary>
/// DAP connection state.
/// </summary>
public enum DapConnectionState
{
    /// <summary>Not connected.</summary>
    Disconnected,
    /// <summary>Connecting to adapter.</summary>
    Connecting,
    /// <summary>Initializing protocol.</summary>
    Initializing,
    /// <summary>Ready for requests.</summary>
    Ready,
    /// <summary>Debugging in progress.</summary>
    Running,
    /// <summary>Paused at breakpoint/step.</summary>
    Paused,
    /// <summary>Terminating.</summary>
    Terminating,
    /// <summary>Error state.</summary>
    Error
}

/// <summary>
/// Debug adapter capabilities.
/// </summary>
public class DapCapabilities
{
    public bool SupportsConfigurationDoneRequest { get; set; }
    public bool SupportsFunctionBreakpoints { get; set; }
    public bool SupportsConditionalBreakpoints { get; set; }
    public bool SupportsHitConditionalBreakpoints { get; set; }
    public bool SupportsEvaluateForHovers { get; set; }
    public List<ExceptionBreakpointsFilter>? ExceptionBreakpointFilters { get; set; }
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
    public bool SupportsDisassembleRequest { get; set; }
    public bool SupportsCancelRequest { get; set; }
    public bool SupportsBreakpointLocationsRequest { get; set; }
    public bool SupportsClipboardContext { get; set; }
    public bool SupportsSteppingGranularity { get; set; }
    public bool SupportsInstructionBreakpoints { get; set; }
    public bool SupportsExceptionFilterOptions { get; set; }
}

/// <summary>
/// Exception breakpoint filter.
/// </summary>
public class ExceptionBreakpointsFilter
{
    public string Filter { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public bool Default { get; set; }
    public bool SupportsCondition { get; set; }
    public string? ConditionDescription { get; set; }
}

/// <summary>
/// Launch request configuration.
/// </summary>
public class LaunchRequest
{
    public string Program { get; set; } = "";
    public List<string>? Args { get; set; }
    public string? Cwd { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public bool NoDebug { get; set; }
    public bool StopOnEntry { get; set; }
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}

/// <summary>
/// Attach request configuration.
/// </summary>
public class AttachRequest
{
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public int? Port { get; set; }
    public string? Host { get; set; }
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}

/// <summary>
/// Source information.
/// </summary>
public class DapSource
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public int? SourceReference { get; set; }
    public string? PresentationHint { get; set; }
    public string? Origin { get; set; }
    public List<DapSource>? Sources { get; set; }
}

/// <summary>
/// DAP Source breakpoint.
/// </summary>
public class DapSourceBreakpoint
{
    public int Line { get; set; }
    public int? Column { get; set; }
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }
    public string? LogMessage { get; set; }
}

/// <summary>
/// DAP Function breakpoint.
/// </summary>
public class DapFunctionBreakpoint
{
    public string Name { get; set; } = "";
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }
}

/// <summary>
/// Breakpoint information.
/// </summary>
public class DapBreakpoint
{
    public int? Id { get; set; }
    public bool Verified { get; set; }
    public string? Message { get; set; }
    public DapSource? Source { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public string? InstructionReference { get; set; }
    public int? Offset { get; set; }
}

/// <summary>
/// Thread information.
/// </summary>
public class DapThread
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Stack frame information.
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
    public bool? CanRestart { get; set; }
    public string? InstructionPointerReference { get; set; }
    public int? ModuleId { get; set; }
    public string? PresentationHint { get; set; }
}

/// <summary>
/// Stack trace result.
/// </summary>
public class StackTraceResult
{
    public List<DapStackFrame> StackFrames { get; set; } = new();
    public int? TotalFrames { get; set; }
}

/// <summary>
/// Variable scope.
/// </summary>
public class DapScope
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
/// Variable information.
/// </summary>
public class DapVariable
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
/// Set variable result.
/// </summary>
public class SetVariableResult
{
    public string Value { get; set; } = "";
    public string? Type { get; set; }
    public int? VariablesReference { get; set; }
    public int? NamedVariables { get; set; }
    public int? IndexedVariables { get; set; }
}

/// <summary>
/// DAP Evaluate result.
/// </summary>
public class DapEvaluateResult
{
    public string Result { get; set; } = "";
    public string? Type { get; set; }
    public string? PresentationHint { get; set; }
    public int VariablesReference { get; set; }
    public int? NamedVariables { get; set; }
    public int? IndexedVariables { get; set; }
    public string? MemoryReference { get; set; }
}

/// <summary>
/// Completion target.
/// </summary>
public class CompletionTarget
{
    public string Label { get; set; } = "";
    public string? Text { get; set; }
    public string? SortText { get; set; }
    public string? Detail { get; set; }
    public CompletionItemType Type { get; set; }
    public int? Start { get; set; }
    public int? Length { get; set; }
    public int? SelectionStart { get; set; }
    public int? SelectionLength { get; set; }
}

/// <summary>
/// Completion item type.
/// </summary>
public enum CompletionItemType
{
    Method,
    Function,
    Constructor,
    Field,
    Variable,
    Class,
    Interface,
    Module,
    Property,
    Unit,
    Value,
    Enum,
    Keyword,
    Snippet,
    Text,
    Color,
    File,
    Reference,
    CustomColor
}

/// <summary>
/// Module information.
/// </summary>
public class DapModule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public bool? IsOptimized { get; set; }
    public bool? IsUserCode { get; set; }
    public string? Version { get; set; }
    public string? SymbolStatus { get; set; }
    public string? SymbolFilePath { get; set; }
    public string? DateTimeStamp { get; set; }
    public string? AddressRange { get; set; }
}

#endregion

#region Event Args

/// <summary>
/// DAP state changed event args.
/// </summary>
public class DapStateChangedEventArgs : EventArgs
{
    public DapConnectionState OldState { get; }
    public DapConnectionState NewState { get; }
    public string? Error { get; }

    public DapStateChangedEventArgs(DapConnectionState oldState, DapConnectionState newState, string? error = null)
    {
        OldState = oldState;
        NewState = newState;
        Error = error;
    }
}

/// <summary>
/// DAP Stopped event args.
/// </summary>
public class DapStoppedEventArgs : EventArgs
{
    public string Reason { get; }
    public string? Description { get; }
    public int? ThreadId { get; }
    public bool PreserveFocusHint { get; }
    public string? Text { get; }
    public bool AllThreadsStopped { get; }
    public List<int>? HitBreakpointIds { get; }

    public DapStoppedEventArgs(string reason, string? description = null, int? threadId = null,
        bool preserveFocusHint = false, string? text = null, bool allThreadsStopped = true,
        List<int>? hitBreakpointIds = null)
    {
        Reason = reason;
        Description = description;
        ThreadId = threadId;
        PreserveFocusHint = preserveFocusHint;
        Text = text;
        AllThreadsStopped = allThreadsStopped;
        HitBreakpointIds = hitBreakpointIds;
    }
}

/// <summary>
/// Continued event args.
/// </summary>
public class ContinuedEventArgs : EventArgs
{
    public int ThreadId { get; }
    public bool AllThreadsContinued { get; }

    public ContinuedEventArgs(int threadId, bool allThreadsContinued = true)
    {
        ThreadId = threadId;
        AllThreadsContinued = allThreadsContinued;
    }
}

/// <summary>
/// Thread event args.
/// </summary>
public class ThreadEventArgs : EventArgs
{
    public string Reason { get; }
    public int ThreadId { get; }

    public ThreadEventArgs(string reason, int threadId)
    {
        Reason = reason;
        ThreadId = threadId;
    }
}

/// <summary>
/// DAP Output event args.
/// </summary>
public class DapOutputEventArgs : EventArgs
{
    public string Category { get; }
    public string Output { get; }
    public string? Group { get; }
    public int? VariablesReference { get; }
    public DapSource? Source { get; }
    public int? Line { get; }
    public int? Column { get; }

    public DapOutputEventArgs(string category, string output, string? group = null,
        int? variablesReference = null, DapSource? source = null, int? line = null, int? column = null)
    {
        Category = category;
        Output = output;
        Group = group;
        VariablesReference = variablesReference;
        Source = source;
        Line = line;
        Column = column;
    }
}

/// <summary>
/// Terminated event args.
/// </summary>
public class TerminatedEventArgs : EventArgs
{
    public bool Restart { get; }

    public TerminatedEventArgs(bool restart = false)
    {
        Restart = restart;
    }
}

/// <summary>
/// Breakpoint event args.
/// </summary>
public class BreakpointEventArgs : EventArgs
{
    public string Reason { get; }
    public DapBreakpoint Breakpoint { get; }

    public BreakpointEventArgs(string reason, DapBreakpoint breakpoint)
    {
        Reason = reason;
        Breakpoint = breakpoint;
    }
}

/// <summary>
/// Module event args.
/// </summary>
public class ModuleEventArgs : EventArgs
{
    public string Reason { get; }
    public DapModule Module { get; }

    public ModuleEventArgs(string reason, DapModule module)
    {
        Reason = reason;
        Module = module;
    }
}

#endregion
