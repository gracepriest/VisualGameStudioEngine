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
/// <b>Filtering happens here, cheaply, on the save path.</b> The subscription handler runs
/// synchronously on the publisher's (UI) thread, so it does only string checks: extension
/// (.bas/.mod/.cls — a .cpp edit changes nothing about the GENERATED artifacts; clangd
/// re-parses it itself on didChange), and path-under-the-current-project. Everything expensive
/// is behind the debouncer on the thread pool.
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
    private readonly TrailingEdgeDebouncer _debouncer;

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
        IEventAggregator events)
        : this(emission, projects, build, events, ProductionQuietPeriod)
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
        TimeSpan quietPeriod)
    {
        _emission = emission;
        _projects = projects;
        _build = build;
        _debouncer = new TrailingEdgeDebouncer(quietPeriod, Fire);
        _subscription = events.Subscribe<FileSavedEvent>(OnFileSaved);
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
        _debouncer.Dispose();
        _subscription.Dispose();
    }
}
