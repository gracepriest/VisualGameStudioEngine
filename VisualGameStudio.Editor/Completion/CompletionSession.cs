namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Tracks the lifecycle of a single completion session in the editor.
///
/// The client model is one LSP request per session: a fresh trigger ('.',
/// first identifier character of a word, Ctrl+Space) begins a session and
/// fires exactly one request; while the window is open the cached list
/// refilters in place. The session ends on Escape, commit, caret escape or
/// when the typed word is fully deleted — and once ended, late responses
/// must never (re)open the popup.
/// </summary>
public class CompletionSession
{
    public enum SessionState
    {
        /// <summary>No active session; results arriving now are stale and must be dropped.</summary>
        None,

        /// <summary>A request was fired for this session; results may open the window.</summary>
        Requested,

        /// <summary>The window is showing; results update the open list in place.</summary>
        Open
    }

    /// <summary>
    /// How long a Requested session blocks fresh word triggers before it is
    /// considered lost (response never arrived). Slightly above the LSP
    /// client's own per-request timeout would starve typing; a few seconds is
    /// plenty since the pipeline always publishes (possibly empty) results.
    /// </summary>
    public static readonly TimeSpan PendingRequestTimeout = TimeSpan.FromSeconds(5);

    private DateTime _beganAtUtc;

    public SessionState State { get; private set; } = SessionState.None;

    /// <summary>Caret offset at the moment the session's request was fired.</summary>
    public int TriggerOffset { get; private set; }

    /// <summary>Caret line at the moment the session's request was fired.</summary>
    public int TriggerLine { get; private set; }

    /// <summary>
    /// True while this session has a request in flight that should suppress
    /// new word triggers. A session whose response never arrived expires
    /// after <see cref="PendingRequestTimeout"/> so typing can recover.
    /// </summary>
    public bool HasPendingRequest => HasPendingRequestAt(DateTime.UtcNow);

    /// <summary>Testable overload of <see cref="HasPendingRequest"/>.</summary>
    public bool HasPendingRequestAt(DateTime nowUtc)
    {
        return State == SessionState.Requested && nowUtc - _beganAtUtc <= PendingRequestTimeout;
    }

    /// <summary>Starts a new session (fresh trigger) and records the trigger point.</summary>
    public void Begin(int caretOffset, int caretLine)
    {
        State = SessionState.Requested;
        TriggerOffset = caretOffset;
        TriggerLine = caretLine;
        _beganAtUtc = DateTime.UtcNow;
    }

    /// <summary>Marks the session's window as opened.</summary>
    public void WindowOpened()
    {
        if (State == SessionState.Requested)
        {
            State = SessionState.Open;
        }
    }

    /// <summary>Ends the session (Escape, commit, caret escape, word deleted, window closed).</summary>
    public void End()
    {
        State = SessionState.None;
    }

    /// <summary>
    /// Whether completion results arriving now are allowed to open a NEW
    /// window: only when this session requested them and the caret is still
    /// in a plausible position (same line, at or after the trigger point).
    /// A dismissed or moved-away session always answers false.
    /// </summary>
    public bool ShouldOpenWindow(int caretOffset, int caretLine)
    {
        return State == SessionState.Requested
               && caretLine == TriggerLine
               && caretOffset >= TriggerOffset;
    }
}
