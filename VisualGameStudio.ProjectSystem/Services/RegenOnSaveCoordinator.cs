using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Regenerates the IntelliSense artifacts (obj/gen headers + compile_commands.json) when
/// BasicLang sources are saved (Task 10 / Phase 3b): <see cref="FileSavedEvent"/> →
/// trailing-edge debounce → <see cref="IIntelliSenseEmissionService.RequestEmit"/>. Without
/// this, a native project's C++ IntelliSense only refreshes on project open or a full build —
/// edit a .bas file and clangd keeps resolving against last build's generated headers.
///
/// <para>
/// <b>Filtering happens here, cheaply, on the trigger path.</b> The <see cref="FileSavedEvent"/>
/// handler runs synchronously on the publisher's (UI) thread, and the .blproj watcher callback
/// arrives on a <see cref="FileSystemWatcher"/> worker thread — both therefore do only string
/// checks before <see cref="TrailingEdgeDebouncer.Signal"/> (lock-guarded, thread-safe): the
/// save path checks extension (.bas/.mod/.cls — a .cpp edit changes nothing about the GENERATED
/// artifacts; clangd re-parses it itself on didChange) and path-under-the-current-project; the
/// watcher path checks path-is-the-watched-.blproj. Everything expensive is behind the
/// debouncer on the thread pool.
/// </para>
///
/// <para>
/// <b>.blproj watching (Task 11):</b> an EXTERNAL edit of the open project's .blproj (hand
/// edit, git checkout, another tool) changes what the emission would generate, but never
/// raises <see cref="FileSavedEvent"/> — so the coordinator watches the file through
/// <see cref="IFileWatcherService"/> (its first production caller) and routes
/// <see cref="IFileWatcherService.FileChangedExternally"/> into the SAME trailing-edge
/// debouncer. The watch follows the project lifecycle: started on
/// <see cref="IProjectService.ProjectOpened"/> (which also retires any previous watch —
/// <c>ProjectService.CreateProjectAsync</c> raises Opened with NO prior Closed), stopped on
/// <see cref="IProjectService.ProjectClosed"/>. IDE-initiated project saves do not loop back:
/// <c>ProjectService.SaveProjectAsync</c> wraps its .blproj write in
/// <see cref="IFileWatcherService.SuppressNotifications"/>.
/// </para>
///
/// <para>
/// <b>Routing vs. the watcher's own debounce:</b> <c>FileWatcherService.IsDebounced</c> is a
/// LEADING-edge throttle (500ms) that DROPS trailing raw events — the shape
/// <see cref="TrailingEdgeDebouncer"/>'s doc warns against. It sits upstream of us and cannot
/// be bypassed without abandoning the service, but it is harmless HERE: every raw FS event
/// either passes the throttle (→ <c>Signal</c>) or falls within 500ms AFTER one that passed,
/// and the trailing fire lands a full quiet period (1.5s &gt; 500ms) after the last signal —
/// by which time the emission's own disk re-read sees the dropped event's content anyway. The
/// trailing edge that must not be lost is the coordinator's, and that one always fires.
/// </para>
///
/// <para>
/// <b>Accepted limitation (spec §4):</b> <c>RequestEmit</c>'s gate reads the STALE in-memory
/// project, so an external edit that flips a project TO native won't pass the gate until
/// reopen (the emission itself reloads from disk; only the gate lags).
/// </para>
///
/// <para>
/// <b>No didChangeWatchedFiles nudge to clangd — measured, do not re-add (Phase 3b S0.5):</b>
/// after a regen rewrites obj/gen + the compilation database, we deliberately do NOT send
/// clangd <c>workspace/didChangeWatchedFiles</c>. clangd 22.1.6 ACCEPTS the notification
/// silently and does nothing with it — no CDB reload, no re-parse (measured both for a
/// created-late and a changed-existing database); it self-reloads the CDB via its
/// <c>compilationDatabase.automaticReload</c> contract on the next didChange-triggered build.
/// Typing heals on its own (~0.2s once the db is right), and a .bas-save regen means the user
/// is editing .bas anyway — the next keystroke in any open .cpp re-parses against the fresh
/// artifacts.
/// </para>
///
/// <para>
/// <b>The fire re-reads state; the save-time check only gates the signal.</b> The debouncer
/// callback passes <c>CurrentProject</c> and <c>CurrentConfiguration</c> as they are at FIRE
/// time, not at save time — a project switched mid-quiet-period must not emit for the OLD (now
/// closed) project. <c>RequestEmit</c>'s own gate re-checks native-ness of whatever it receives
/// and reads project state from disk, so a non-native current project costs nothing.
/// </para>
///
/// <para>
/// <b>Lifetime:</b> a DI singleton that nothing injects — it must be resolved eagerly at
/// startup (App.OnFrameworkInitializationCompleted, beside GitAutoFetchService) because a
/// lazily-resolved singleton nobody injects is never constructed and therefore never
/// subscribes. The container disposes it on shutdown; Dispose cancels any pending fire (the
/// debouncer's no-flush contract — a pending regen must never hold up app exit).
/// </para>
/// </summary>
public sealed class RegenOnSaveCoordinator : IDisposable
{
    /// <summary>
    /// Production quiet period. Derivation: an emission runs the whole compiler front end —
    /// seconds of work — and is non-cancellable once in flight, so firing eagerly on every
    /// keystroke-save would queue overlapping multi-second runs; 1.5s absorbs auto-save and
    /// save-all bursts into one emission without adding perceptible staleness on top of the
    /// emission's own duration.
    /// </summary>
    public static readonly TimeSpan ProductionQuietPeriod = TimeSpan.FromMilliseconds(1500);

    private readonly IIntelliSenseEmissionService _emission;
    private readonly IProjectService _projects;
    private readonly IBuildService _build;
    private readonly IFileWatcherService _watcher;
    private readonly TrailingEdgeDebouncer _debouncer;

    /// <summary>
    /// The normalized (<see cref="Path.GetFullPath(string)"/>) path of the currently watched
    /// .blproj, or null when no project is open. Guarded by <see cref="_watchLock"/>: project
    /// open/close arrives on the UI thread while <see cref="OnProjectFileChangedExternally"/>
    /// reads it from a watcher worker thread.
    /// </summary>
    private string? _watchedProjectFile;
    private readonly object _watchLock = new();

    /// <summary>
    /// The strong root for the event subscription. <see cref="EventAggregator"/> holds handlers
    /// by <see cref="WeakReference"/>; the returned subscription object is the only strong
    /// reference to the handler delegate, so DISCARDING it would let the next gen-0 GC silently
    /// unsubscribe this coordinator — everything would work in a quick manual test and die
    /// minutes into a session.
    /// </summary>
    private readonly IDisposable _subscription;

    public RegenOnSaveCoordinator(
        IIntelliSenseEmissionService emission,
        IProjectService projects,
        IBuildService build,
        IEventAggregator events,
        IFileWatcherService watcher)
        : this(emission, projects, build, events, watcher, ProductionQuietPeriod)
    {
    }

    /// <summary>
    /// Seam constructor: <paramref name="quietPeriod"/> injected so tests run on a 50ms
    /// debounce instead of 1.5s. PUBLIC deliberately — this assembly has no
    /// <c>InternalsVisibleTo</c> for VisualGameStudio.Tests (precedent:
    /// <see cref="IntelliSenseEmissionService"/>'s emitter-seam constructor).
    /// </summary>
    public RegenOnSaveCoordinator(
        IIntelliSenseEmissionService emission,
        IProjectService projects,
        IBuildService build,
        IEventAggregator events,
        IFileWatcherService watcher,
        TimeSpan quietPeriod)
    {
        _emission = emission;
        _projects = projects;
        _build = build;
        _watcher = watcher;
        _debouncer = new TrailingEdgeDebouncer(quietPeriod, Fire);
        _subscription = events.Subscribe<FileSavedEvent>(OnFileSaved);
        _projects.ProjectOpened += OnProjectOpened;
        _projects.ProjectClosed += OnProjectClosed;
        _watcher.FileChangedExternally += OnProjectFileChangedExternally;
        // A project already open when we are constructed still gets its watch. Defensive: in
        // production the coordinator is resolved at startup, before any project can open, but
        // if that ordering ever shifts (e.g. a restore-on-launch open), the open project's
        // .blproj must not silently go unwatched.
        WatchProjectFile(_projects.CurrentProject?.FilePath);
    }

    private void OnProjectOpened(object? sender, ProjectEventArgs e)
        => WatchProjectFile(e.Project.FilePath);

    // ProjectClosed only ever fires for the current (watched) project — CloseProjectAsync
    // closes CurrentProject and OpenProjectAsync closes-then-opens — so an unconditional
    // unwatch is correct; the switch-without-close shape (CreateProjectAsync) is handled by
    // OnProjectOpened retiring the previous watch itself.
    private void OnProjectClosed(object? sender, ProjectEventArgs e)
        => WatchProjectFile(null);

    /// <summary>
    /// Retires the current .blproj watch (if any) and, for a non-null path, watches the new
    /// one. Same-path re-open is a no-op so a redundant Opened event cannot churn the
    /// underlying <see cref="FileSystemWatcher"/>.
    /// </summary>
    private void WatchProjectFile(string? projectFilePath)
    {
        string? normalized = null;
        if (!string.IsNullOrEmpty(projectFilePath))
        {
            try
            {
                normalized = Path.GetFullPath(projectFilePath);
            }
            catch (Exception)
            {
                // A path that cannot be normalized cannot be watched (WatchFile would reject
                // it anyway) — treat it as "no project file", same as the save-path IsUnder.
                normalized = null;
            }
        }

        lock (_watchLock)
        {
            if (string.Equals(_watchedProjectFile, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (_watchedProjectFile != null)
            {
                _watcher.UnwatchFile(_watchedProjectFile);
            }
            _watchedProjectFile = normalized;
            if (normalized != null)
            {
                _watcher.WatchFile(normalized);
            }
        }
    }

    /// <summary>
    /// Watcher callback (a FileSystemWatcher worker thread): an external change to the watched
    /// .blproj re-arms the SAME trailing-edge debouncer as a source save. The event is global
    /// to the shared <see cref="IFileWatcherService"/> singleton — other consumers may watch
    /// other files — so filter to the watched path first. Like the save path, this only gates
    /// the signal; the fire re-reads all state, and the emission re-reads the .blproj from
    /// disk, so content-level staleness cannot survive the quiet period.
    /// </summary>
    private void OnProjectFileChangedExternally(object? sender, FileChangedExternallyEventArgs e)
    {
        string? watched;
        lock (_watchLock)
        {
            watched = _watchedProjectFile;
        }
        if (watched == null || string.IsNullOrEmpty(e.FilePath)) return;

        string changedFull;
        try
        {
            // WatchFile-sourced events already carry the normalized watch path, but directory
            // watchers on the same shared service raise raw OS paths — normalize defensively.
            changedFull = Path.GetFullPath(e.FilePath);
        }
        catch (Exception)
        {
            return;
        }
        if (!string.Equals(watched, changedFull, StringComparison.OrdinalIgnoreCase)) return;

        _debouncer.Signal();
    }

    private void OnFileSaved(FileSavedEvent e)
    {
        if (!HasBasicLangExtension(e.FilePath)) return;
        var project = _projects.CurrentProject;
        if (project == null) return;
        if (!IsUnder(project.ProjectDirectory, e.FilePath)) return;
        _debouncer.Signal();
    }

    /// <summary>Runs on a thread-pool thread after the quiet period (the debouncer's contract).</summary>
    private void Fire()
    {
        // Task deliberately discarded: RequestEmit never faults (its contract), and this fire
        // is a background refresh nothing waits on.
        _ = _emission.RequestEmit(_projects.CurrentProject, _build.CurrentConfiguration?.Name ?? "Debug");
    }

    private static bool HasBasicLangExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".bas", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mod", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cls", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <paramref name="filePath"/> is inside <paramref name="directory"/>. Both are
    /// normalized through <see cref="Path.GetFullPath(string)"/> and compared
    /// OrdinalIgnoreCase — Windows paths are case-insensitive, and the editor's saved-path
    /// casing routinely disagrees with the .blproj's (drive letter, user-typed opens), so a
    /// case-sensitive compare would silently drop real saves. The directory gets a trailing
    /// separator before the prefix compare so a sibling whose name merely EXTENDS the project
    /// directory's (<c>C:\proj</c> vs <c>C:\project2</c>) does not false-match.
    /// </summary>
    private static bool IsUnder(string directory, string filePath)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(filePath)) return false;

        string directoryFull;
        string fileFull;
        try
        {
            directoryFull = Path.GetFullPath(directory);
            fileFull = Path.GetFullPath(filePath);
        }
        catch (Exception)
        {
            // GetFullPath throws on malformed input (invalid characters, over-long segments,
            // unsupported forms). A path that cannot be normalized cannot be located under the
            // project — and this handler sits on the editor's save path, where a throw would
            // surface as a failed save, not a missed regen.
            return false;
        }

        if (!directoryFull.EndsWith(Path.DirectorySeparatorChar))
        {
            directoryFull += Path.DirectorySeparatorChar;
        }
        return fileFull.StartsWith(directoryFull, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        // Safe order: debouncer FIRST (cancels any pending fire; later signals no-op), then
        // the trigger subscriptions, then the watch. A callback racing this teardown at worst
        // signals the already-disposed debouncer — a documented no-op. The watcher service
        // itself is a shared DI singleton the container disposes; we only retire OUR watch.
        _debouncer.Dispose();
        _subscription.Dispose();
        _watcher.FileChangedExternally -= OnProjectFileChangedExternally;
        _projects.ProjectOpened -= OnProjectOpened;
        _projects.ProjectClosed -= OnProjectClosed;
        lock (_watchLock)
        {
            if (_watchedProjectFile != null)
            {
                _watcher.UnwatchFile(_watchedProjectFile);
                _watchedProjectFile = null;
            }
        }
    }
}
