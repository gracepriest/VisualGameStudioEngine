using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Periodically runs <c>git fetch</c> in the background so ahead/behind counts stay current without
/// the user asking. Honors two settings, live:
/// <list type="bullet">
/// <item><description><c>git.autoFetch</c> (bool, default true) — the master switch.</description></item>
/// <item><description><c>git.autoFetchInterval</c> (int seconds, schema default 180, min 60) — the
/// period between fetches.</description></item>
/// </list>
/// A change to either setting restarts the timer (via <see cref="ISettingsService.SettingChanged"/>),
/// exactly like <see cref="AutoSaveService"/>. No fetch is issued when no repository is open
/// (<see cref="IGitService.IsGitRepository"/> is false) — and <see cref="IGitService.FetchAsync"/>
/// is itself a no-op in that state, so a stray tick is harmless. The timer is disposed cleanly on
/// <see cref="Dispose"/> (the DI container disposes this singleton at shutdown).
/// </summary>
public sealed class GitAutoFetchService : IDisposable
{
    private readonly IGitService _gitService;
    private readonly ISettingsService _settingsService;
    private readonly object _timerLock = new();
    private System.Timers.Timer? _timer;
    private bool _disposed;
    private bool _fetchInFlight;

    /// <summary>Floor for the fetch interval (seconds) — matches the schema <c>minimum</c> of 60.</summary>
    public const int MinIntervalSeconds = 60;

    /// <summary>Schema default interval (seconds) when <c>git.autoFetchInterval</c> is unset.</summary>
    public const int DefaultIntervalSeconds = 180;

    public GitAutoFetchService(IGitService gitService, ISettingsService settingsService)
    {
        _gitService = gitService;
        _settingsService = settingsService;

        // Name the consumers so the Phase 3 settings-consumer contract test sees these dialog
        // settings are live.
        SettingsConsumerRegistry.RegisterConsumer("git.autoFetch", "GitAutoFetchService → periodic background git fetch on/off");
        SettingsConsumerRegistry.RegisterConsumer("git.autoFetchInterval", "GitAutoFetchService → seconds between background fetches");

        _settingsService.SettingChanged += OnSettingChanged;
        RestartTimer();
    }

    // ── Pure resolution seams (headless-testable) ──

    /// <summary><c>git.autoFetch</c> (default true).</summary>
    public static bool ResolveAutoFetchEnabled(ISettingsService? settings)
        => settings?.Get("git.autoFetch", true) ?? true;

    /// <summary>
    /// <c>git.autoFetchInterval</c> resolved to milliseconds, clamped to at least
    /// <see cref="MinIntervalSeconds"/> (the schema minimum). Empty/invalid values fall back to
    /// <see cref="DefaultIntervalSeconds"/>.
    /// </summary>
    public static int ResolveAutoFetchIntervalMs(ISettingsService? settings)
    {
        var seconds = settings?.Get("git.autoFetchInterval", DefaultIntervalSeconds) ?? DefaultIntervalSeconds;
        if (seconds < MinIntervalSeconds) seconds = MinIntervalSeconds;
        return seconds * 1000;
    }

    /// <summary>A fetch should run only when auto-fetch is enabled AND a repository is open.</summary>
    public static bool ShouldFetchNow(bool enabled, bool isRepository) => enabled && isRepository;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key == "git.autoFetch" || e.Key == "git.autoFetchInterval")
        {
            RestartTimer();
        }
    }

    private void RestartTimer()
    {
        lock (_timerLock)
        {
            if (_disposed) return;

            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;

            if (!ResolveAutoFetchEnabled(_settingsService))
            {
                // Disabled — leave the timer torn down until the setting flips back on.
                return;
            }

            var timer = new System.Timers.Timer(ResolveAutoFetchIntervalMs(_settingsService))
            {
                AutoReset = true
            };
            timer.Elapsed += async (_, _) => await OnTimerElapsedAsync();
            _timer = timer;
            timer.Start();
        }
    }

    private async Task OnTimerElapsedAsync()
    {
        // The Elapsed subscription is effectively async-void on a threadpool thread: ANY exception
        // that escapes this method — including one from the guard prologue (settings read,
        // IsGitRepository) — would take down the process. The entire body is therefore inside
        // try/catch: a background fetch, or even deciding whether to fetch, must never crash the IDE.
        var acquired = false;
        try
        {
            if (_disposed) return;

            // Re-read enabled/repo state at tick time (settings may have changed since arming), and
            // skip the tick if the previous fetch has not finished (a slow network must not stack
            // overlapping git processes).
            if (!ShouldFetchNow(ResolveAutoFetchEnabled(_settingsService), _gitService.IsGitRepository)) return;

            lock (_timerLock)
            {
                if (_fetchInFlight) return;
                _fetchInFlight = true;
                acquired = true;
            }

            await _gitService.FetchAsync();
        }
        catch (Exception ex)
        {
            // A failed background fetch (offline, auth prompt, etc.) must never crash the IDE.
            System.Diagnostics.Debug.WriteLine($"[GitAutoFetch] Fetch failed: {ex.Message}");
        }
        finally
        {
            // Only the tick that actually claimed the in-flight flag may release it — otherwise a
            // skipped tick would clear a concurrent fetch's flag and let ticks stack.
            if (acquired)
            {
                lock (_timerLock) { _fetchInFlight = false; }
            }
        }
    }

    public void Dispose()
    {
        lock (_timerLock)
        {
            if (_disposed) return;
            _disposed = true;

            _settingsService.SettingChanged -= OnSettingChanged;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }
    }
}
