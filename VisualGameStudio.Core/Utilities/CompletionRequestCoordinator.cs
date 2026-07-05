namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Tracks in-flight completion requests for a single document so that
/// (a) starting a new request cancels the previous one, and
/// (b) responses for anything but the most recent request can be detected
/// and dropped — a late response must never reopen a dismissed popup or
/// replace a newer result. Thread-safe.
/// </summary>
public sealed class CompletionRequestCoordinator
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private int _latestId;

    /// <summary>
    /// Starts a new request: cancels the previous request's token and returns
    /// a fresh (id, token) pair. The id is monotonically increasing.
    /// </summary>
    public (int RequestId, CancellationToken Token) BeginRequest()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            // Deliberately not disposed: the token may still be observed by an
            // in-flight request; CTSs without timers hold no unmanaged state.
            _cts = new CancellationTokenSource();
            _latestId++;
            return (_latestId, _cts.Token);
        }
    }

    /// <summary>
    /// True only for the most recently started request. Callers must drop
    /// (not publish) results whose request id is no longer current.
    /// </summary>
    public bool IsCurrent(int requestId)
    {
        lock (_lock)
        {
            return _latestId != 0 && requestId == _latestId;
        }
    }

    /// <summary>
    /// Cancels any in-flight request and invalidates its id, e.g. when the
    /// document closes or the user dismisses the completion UI.
    /// </summary>
    public void CancelAll()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = null;
            _latestId++;
        }
    }
}
