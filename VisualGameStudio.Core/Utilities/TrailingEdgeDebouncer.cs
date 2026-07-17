namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Trailing-edge debouncer: each <see cref="Signal"/> (re)starts a quiet-period timer,
/// and <c>fire</c> runs exactly once, on a thread-pool thread, after the LAST signal's
/// quiet period elapses — a burst of signals coalesces into one trailing fire, and the
/// trailing edge always fires.
///
/// KEEP IN SYNC — template: this is the debounce mechanics of
/// <c>SettingsService.ScheduleSave</c> (CTS + <c>Task.Delay</c> + cancel-restart; the
/// trailing edge always fires), extracted here so new consumers (save-regen
/// coordination, .blproj watching) share one implementation.
/// ANTI-template: <c>FileWatcherService.IsDebounced</c> is a leading-edge throttle that
/// DROPS the trailing edge — signals inside its window are discarded with nothing
/// scheduled to fire when it lapses. Never reuse that shape for work that must not
/// lose the last event.
///
/// No flush-on-dispose, by design: <see cref="Dispose"/> CANCELS a pending fire rather
/// than running it, so a consumer (e.g. a save-triggered regen coordinator) can never
/// hold app shutdown for a pending fire. Anything that must survive shutdown belongs in
/// the consumer's own flush path, not here. <see cref="Signal"/> after dispose is a
/// no-op. Exceptions thrown by <c>fire</c> are swallowed and do not prevent later
/// cycles — the consumer owns its own error reporting.
///
/// Thread-safe: all mutable state is guarded by a single lock. Fires do NOT
/// serialize: a new cycle's fire can start while a previous slow fire is still
/// running — consumers whose fires must not overlap own that gating themselves
/// (e.g. a regen coordinator's single-flight request gate).
/// </summary>
public sealed class TrailingEdgeDebouncer : IDisposable
{
    private readonly object _lock = new();
    private readonly TimeSpan _quietPeriod;
    private readonly Action _fire;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public TrailingEdgeDebouncer(TimeSpan quietPeriod, Action fire)
    {
        if (quietPeriod < TimeSpan.Zero)
        {
            // Never swallow this into a silent never-fire: -1ms IS
            // Timeout.InfiniteTimeSpan, which Task.Delay accepts and waits on
            // forever with no exception at all.
            throw new ArgumentOutOfRangeException(nameof(quietPeriod), quietPeriod,
                "The quiet period must be non-negative. A negative value would become a " +
                "silent never-fire: -1ms is Timeout.InfiniteTimeSpan, which Task.Delay " +
                "accepts and never completes.");
        }
        _quietPeriod = quietPeriod;
        _fire = fire ?? throw new ArgumentNullException(nameof(fire));
    }

    /// <summary>
    /// (Re)arms the debouncer: cancels any pending fire and schedules a new one for one
    /// quiet period from now. Returns immediately; the fire runs on a thread-pool
    /// thread. No-op after <see cref="Dispose"/>.
    /// </summary>
    public void Signal()
    {
        CancellationToken token;
        lock (_lock)
        {
            if (_disposed) return;
            _cts?.Cancel();
            // Deliberately not disposed: an in-flight delay may still observe the
            // token; CTSs without timers hold no unmanaged state.
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // With the ctor guard, OCE is the only exception this delay can throw.
                await Task.Delay(_quietPeriod, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (token.IsCancellationRequested) return;
            try
            {
                _fire();
            }
            catch { /* consumer owns error reporting; a throwing fire must not kill later cycles */ }
        });
    }

    /// <summary>Cancels any pending fire (no flush) and makes further signals no-ops.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts = null;
        }
    }
}
