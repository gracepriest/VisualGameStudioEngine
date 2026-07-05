namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Bounded auto-restart budget for the language server with a stability
/// window.
///
/// The budget (3 attempts, exponential backoff) is only refunded after a
/// connection PROVES stable by surviving <see cref="StabilityWindow"/>.
/// Refunding on every successful reconnect — the previous behavior — made the
/// bound meaningless: a server that crashes a few seconds after each
/// reconnect (e.g. the same poisonous didOpen re-sent on every connect) was
/// killed and respawned forever, spamming the output pane and flickering the
/// status bar indefinitely.
/// </summary>
public sealed class RestartPolicy
{
    public const int MaxAttempts = 3;

    /// <summary>Uptime a connection must survive before the attempt budget is refunded.</summary>
    public static readonly TimeSpan StabilityWindow = TimeSpan.FromSeconds(60);

    private int _attempts;
    private DateTime? _connectedAtUtc;

    /// <summary>Restart attempts consumed since the last stable connection.</summary>
    public int Attempts => _attempts;

    /// <summary>True while the restart budget is not exhausted.</summary>
    public bool CanAttempt => _attempts < MaxAttempts;

    /// <summary>Records that a connection (initial or restarted) was established.</summary>
    public void OnConnected(DateTime utcNow)
    {
        _connectedAtUtc = utcNow;
    }

    /// <summary>
    /// Records that the connection was lost. Refunds the attempt budget ONLY
    /// when the lost connection had survived the stability window — a crash
    /// shortly after a reconnect keeps consuming the same budget so restart
    /// storms terminate at <see cref="MaxAttempts"/>.
    /// </summary>
    public void OnDisconnected(DateTime utcNow)
    {
        if (_connectedAtUtc is { } connectedAt && utcNow - connectedAt >= StabilityWindow)
        {
            _attempts = 0;
        }

        _connectedAtUtc = null;
    }

    /// <summary>
    /// Consumes one restart attempt and returns the backoff delay to wait
    /// before it (1s, 2s, 4s). Callers must check <see cref="CanAttempt"/> first.
    /// </summary>
    public TimeSpan BeginAttempt()
    {
        _attempts++;
        return TimeSpan.FromSeconds(Math.Pow(2, _attempts - 1));
    }
}
