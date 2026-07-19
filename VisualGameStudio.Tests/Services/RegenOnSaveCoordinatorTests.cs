using System.Diagnostics;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Task 10 (C++ Phase 3b): saving a BasicLang source file regenerates the IntelliSense
/// artifacts (obj/gen headers + compile_commands.json) — debounced, filtered, trailing-edge.
///
/// <para>
/// The emission itself is <c>IntelliSenseEmissionService</c>'s business (its own fixture); this
/// fixture pins the COORDINATION policy: which saves count (.bas/.mod/.cls, under the current
/// project, project open), that a burst coalesces to one trailing emit, that the emit re-reads
/// project/configuration at FIRE time, that the aggregator's WeakReference cannot silently
/// unsubscribe us (the strong-root landmine), and that Dispose stops everything. Emission is an
/// <see cref="IIntelliSenseEmissionService"/> recorder — we assert CALLS, not emission internals.
/// </para>
///
/// <para>
/// Timing discipline mirrors <c>TrailingEdgeDebouncerTests</c>: every positive wait is a bounded
/// poll, every negative assertion uses a settle window several times the quiet period, and the
/// two tests whose result depends on staying INSIDE the quiet period (the burst, the mid-quiet
/// project switch) measure their own gaps and go Inconclusive on a stalled runner.
/// </para>
///
/// <para>
/// Task 11 extends the fixture with <c>.blproj</c> watching: an EXTERNAL edit of the open
/// project's <c>.blproj</c> reaches the same trailing-edge debouncer (via the real
/// <see cref="FileWatcherService"/> — these tests write real files under a per-test temp root),
/// IDE-side saves are suppressed (<c>SuppressNotifications</c> window, wired inside
/// <c>ProjectService.SaveProjectAsync</c>), and the watch follows the project lifecycle
/// (starts on open, stops on close, moves on switch — including the
/// <c>CreateProjectAsync</c> shape, which raises <c>ProjectOpened</c> with NO prior
/// <c>ProjectClosed</c>). Negative assertions here use <see cref="WatcherSettleWindow"/>, which
/// also covers the watcher's 200ms post-dispose unsuppression grace and FS-event latency.
/// </para>
/// </summary>
[TestFixture]
public class RegenOnSaveCoordinatorTests
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Settle window for negative assertions that involve REAL FileSystemWatcher events: must
    /// exceed FS-event delivery latency plus the watcher's 200ms delayed unsuppression grace
    /// (<c>FileWatcherService.Unsuppress</c>), and stay several times the quiet period.
    /// </summary>
    private static readonly TimeSpan WatcherSettleWindow = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Waits out <c>FileWatcherService.DebounceMilliseconds</c> (500ms, private const) — its
    /// LEADING-edge throttle drops raw FS events inside that window, so any test that writes the
    /// same watched file twice and needs the SECOND write's event delivered must first let the
    /// window lapse (otherwise a "no emit" assertion passes vacuously).
    /// </summary>
    private static readonly TimeSpan ThrottleLapse = TimeSpan.FromMilliseconds(650);

    private EventAggregator _events = null!;
    private RecordingEmissionService _emission = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IBuildService> _buildService = null!;
    private FileWatcherService _watcher = null!;
    private readonly List<string> _tempRoots = new();

    // Re-read through closures by the mocks, so tests (and the coordinator, at fire time)
    // always observe the CURRENT value, never a setup-time capture.
    private BasicLangProject? _currentProject;
    private BuildConfiguration? _currentConfiguration;

    [SetUp]
    public void SetUp()
    {
        _events = new EventAggregator();
        _emission = new RecordingEmissionService();
        _currentProject = null;
        _currentConfiguration = new BuildConfiguration { Name = "Debug" };

        _projectService = new Mock<IProjectService>();
        _projectService.SetupGet(p => p.CurrentProject).Returns(() => _currentProject);

        _buildService = new Mock<IBuildService>();
        _buildService.SetupGet(b => b.CurrentConfiguration).Returns(() => _currentConfiguration!);

        _watcher = new FileWatcherService();
    }

    [TearDown]
    public void TearDown()
    {
        _watcher.Dispose();
        foreach (var root in _tempRoots)
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
        _tempRoots.Clear();
    }

    // ---- harness ----

    private RegenOnSaveCoordinator CreateCoordinator()
        => new(_emission, _projectService.Object, _buildService.Object, _events, _watcher, QuietPeriod);

    /// <summary>A project whose directory is a fake (never-touched) temp-rooted path.</summary>
    private static BasicLangProject NewProject(string dirName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vgs-t10-regen", dirName);
        return new BasicLangProject
        {
            Name = dirName,
            FilePath = Path.Combine(dir, dirName + ".blproj"),
            TargetBackend = TargetBackend.Cpp,
        };
    }

    /// <summary>
    /// A project whose directory and <c>.blproj</c> REALLY exist, under a fresh per-test temp
    /// root (tracked for TearDown deletion) — required by the watcher tests:
    /// <c>FileWatcherService.WatchFile</c> silently refuses non-existent directories, and a
    /// fresh root per test keeps the watcher's per-path leading-edge throttle state
    /// (<c>_lastNotified</c>) from bleeding between tests.
    /// </summary>
    private BasicLangProject NewProjectOnDisk(string dirName)
    {
        var root = Path.Combine(Path.GetTempPath(), "vgs-t11-regen", Guid.NewGuid().ToString("N"));
        _tempRoots.Add(root);
        var dir = Path.Combine(root, dirName);
        Directory.CreateDirectory(dir);
        var project = new BasicLangProject
        {
            Name = dirName,
            FilePath = Path.Combine(dir, dirName + ".blproj"),
            TargetBackend = TargetBackend.Cpp,
        };
        File.WriteAllText(project.FilePath, "<BasicLangProject Version=\"1.0\" />");
        return project;
    }

    /// <summary>Externally rewrites the project's <c>.blproj</c> (fresh content every time).</summary>
    private static void TouchProjectFile(BasicLangProject project)
        => File.WriteAllText(project.FilePath,
            $"<BasicLangProject Version=\"1.0\" /><!-- {Guid.NewGuid():N} -->");

    private void OpenProject(BasicLangProject project)
    {
        _currentProject = project;
        _projectService.Raise(p => p.ProjectOpened += null, new ProjectEventArgs(project));
    }

    private void CloseProject(BasicLangProject project)
    {
        _currentProject = null;
        _projectService.Raise(p => p.ProjectClosed += null, new ProjectEventArgs(project));
    }

    private void PublishSave(string filePath) => _events.Publish(new FileSavedEvent(filePath));

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    // ---- tests ----

    [Test]
    public async Task BasSaveUnderTheCurrentProject_TriggersOneEmitAfterTheQuietPeriod()
    {
        var project = NewProject("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();

        // The saved path spells the directory prefix in a DIFFERENT case than
        // ProjectDirectory: Windows paths are case-insensitive, so this must still count
        // as "under the project" (the coordinator's OrdinalIgnoreCase compare).
        PublishSave(Path.Combine(project.ProjectDirectory.ToUpperInvariant(), "Main.bas"));

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "expected one RequestEmit within the wait bound");
        await Task.Delay(SettleWindow);
        Assert.That(_emission.CallCount, Is.EqualTo(1),
            "a single save must produce exactly one emit");
    }

    [Test]
    public async Task UppercaseExtensionSave_Triggers()
    {
        var project = NewProject("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();

        // The EXTENSION compare must be OrdinalIgnoreCase too (the test above covers the
        // directory prefix): a save reported as MAIN.BAS still counts.
        PublishSave(Path.Combine(project.ProjectDirectory, "MAIN.BAS"));

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "an upper-cased .BAS extension must still trigger the regen");
    }

    [TestCase("Module1.mod")]
    [TestCase("Class1.cls")]
    public async Task ModAndClsSaves_Trigger(string fileName)
    {
        // Positive coverage for the OTHER two BasicLang extensions — the .cpp negative
        // below alone would pass even if the filter accepted only .bas.
        var project = NewProject("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();

        PublishSave(Path.Combine(project.ProjectDirectory, fileName));

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, $"a {Path.GetExtension(fileName)} save must trigger like a .bas save");
    }

    [Test]
    public async Task SaveBurst_CoalescesToOneEmit()
    {
        var project = NewProject("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();
        var savedPath = Path.Combine(project.ProjectDirectory, "Main.bas");

        // 5 saves ~10ms apart — each inside the previous quiet period. Timestamps bracket
        // each publish so a scheduler stall between saves is detectable (the same technique
        // as TrailingEdgeDebouncerTests.Burst_CoalescesToOneTrailingFire): if every
        // afterMs[i+1] - beforeMs[i] window stayed under the quiet period, no early fire
        // was possible and the count assertion below is meaningful.
        var clock = Stopwatch.StartNew();
        var beforeMs = new long[5];
        var afterMs = new long[5];
        for (int i = 0; i < 5; i++)
        {
            if (i > 0) await Task.Delay(10);
            beforeMs[i] = clock.ElapsedMilliseconds;
            PublishSave(savedPath);
            afterMs[i] = clock.ElapsedMilliseconds;
        }
        long maxArmToCancelMs = 0;
        for (int i = 0; i < 4; i++)
        {
            maxArmToCancelMs = Math.Max(maxArmToCancelMs, afterMs[i + 1] - beforeMs[i]);
        }

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "expected the trailing emit within the wait bound");
        await Task.Delay(SettleWindow);
        if (maxArmToCancelMs >= (long)QuietPeriod.TotalMilliseconds)
        {
            Assert.Inconclusive("runner stalled; the burst did not stay inside the quiet period");
        }
        Assert.That(_emission.CallCount, Is.EqualTo(1),
            "a burst of saves must coalesce to exactly one trailing emit");
    }

    [Test]
    public async Task CppSave_DoesNotTrigger()
    {
        var project = NewProject("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();

        // Only BasicLang sources (.bas/.mod/.cls) regenerate: a .cpp edit is clangd's own
        // business — it re-parses on didChange; nothing about the GENERATED artifacts changed.
        PublishSave(Path.Combine(project.ProjectDirectory, "native.cpp"));

        bool fired = await WaitUntilAsync(() => _emission.CallCount > 0, SettleWindow);
        Assert.That(fired, Is.False, "a C++ save must not trigger emission");
    }

    [Test]
    public async Task SaveOutsideTheProjectDirectory_DoesNotTrigger()
    {
        var project = NewProject("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();

        // Plainly elsewhere.
        PublishSave(Path.Combine(Path.GetTempPath(), "vgs-t10-regen", "elsewhere", "Main.bas"));
        // The false-prefix trap: "...\proj" IS a string prefix of "...\project2\Main.bas".
        // A naive StartsWith without a trailing separator would count this as inside.
        PublishSave(Path.Combine(project.ProjectDirectory + "ect2", "Main.bas"));

        bool fired = await WaitUntilAsync(() => _emission.CallCount > 0, SettleWindow);
        Assert.That(fired, Is.False,
            "saves outside the project directory (including sibling dirs whose name merely " +
            "starts with the project directory's name) must not trigger emission");
    }

    [Test]
    public async Task NoProjectOpen_DoesNotTrigger()
    {
        _currentProject = null;
        using var coordinator = CreateCoordinator();

        PublishSave(Path.Combine(Path.GetTempPath(), "vgs-t10-regen", "proj", "Main.bas"));

        bool fired = await WaitUntilAsync(() => _emission.CallCount > 0, SettleWindow);
        Assert.That(fired, Is.False, "with no project open there is nothing to emit for");
    }

    [Test]
    public async Task Subscription_IsHeldStrongly()
    {
        // THE WeakReference landmine, pinned: EventAggregator holds handlers by WeakReference,
        // so a coordinator that discards the returned subscription (the only strong root for
        // the handler delegate) is silently unsubscribed by the next gen-0 collection — every
        // test above would still pass if it ran before a GC. Force the collection, then save.
        var project = NewProject("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        PublishSave(Path.Combine(project.ProjectDirectory, "Main.bas"));

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "a save after a full GC must still trigger: the subscription (and with " +
                     "it the handler delegate) must be strongly rooted by the coordinator");
        GC.KeepAlive(coordinator);
    }

    [Test]
    public async Task Dispose_StopsTriggering()
    {
        var project = NewProject("proj");
        _currentProject = project;
        var coordinator = CreateCoordinator();
        coordinator.Dispose();

        PublishSave(Path.Combine(project.ProjectDirectory, "Main.bas"));

        bool fired = await WaitUntilAsync(() => _emission.CallCount > 0, SettleWindow);
        Assert.That(fired, Is.False, "a disposed coordinator must not react to saves");
    }

    [Test]
    public async Task EmitReceives_CurrentProjectAndConfiguration()
    {
        var project = NewProject("proj");
        _currentProject = project;
        _currentConfiguration = new BuildConfiguration { Name = "Release" };
        using var coordinator = CreateCoordinator();

        PublishSave(Path.Combine(project.ProjectDirectory, "Main.bas"));

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "expected one RequestEmit within the wait bound");
        var call = _emission.Snapshot()[0];
        Assert.That(call.Project, Is.SameAs(project),
            "the emit must receive the CURRENT project instance");
        Assert.That(call.Configuration, Is.EqualTo("Release"),
            "the emit must carry the build service's current configuration name");
    }

    [Test]
    public async Task NullConfiguration_FallsBackToDebug()
    {
        var project = NewProject("proj");
        _currentProject = project;
        _currentConfiguration = null;
        using var coordinator = CreateCoordinator();

        PublishSave(Path.Combine(project.ProjectDirectory, "Main.bas"));

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "expected one RequestEmit within the wait bound");
        Assert.That(_emission.Snapshot()[0].Configuration, Is.EqualTo("Debug"),
            "with no current configuration the emit must fall back to Debug");
    }

    [Test]
    public async Task ProjectSwitchedDuringTheQuietPeriod_EmitReceivesTheProjectAtFireTime()
    {
        // The fire must RE-READ CurrentProject, not use a save-time capture: a project switched
        // mid-quiet-period must not emit for the OLD (now closed) project. RequestEmit's own
        // gate re-checks native-ness of whatever it receives.
        var projectA = NewProject("proj-a");
        var projectB = NewProject("proj-b");
        _currentProject = projectA;
        using var coordinator = CreateCoordinator();

        var clock = Stopwatch.StartNew();
        PublishSave(Path.Combine(projectA.ProjectDirectory, "Main.bas"));
        _currentProject = projectB;
        var switchedAt = clock.Elapsed;

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "expected one RequestEmit within the wait bound");
        if (switchedAt >= QuietPeriod)
        {
            Assert.Inconclusive("runner stalled; the project switch did not land inside the quiet period");
        }
        Assert.That(_emission.Snapshot()[0].Project, Is.SameAs(projectB),
            "the emit must receive the project current at FIRE time, not at save time");
    }

    // ---- Task 11: .blproj watching ----

    [Test]
    public async Task ExternalBlprojChange_TriggersOneEmit_ThroughTheTrailingDebouncer()
    {
        var project = NewProjectOnDisk("proj");
        using var coordinator = CreateCoordinator();
        OpenProject(project);

        long beforeWriteMs = _emission.NowMs;
        TouchProjectFile(project);

        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "an external .blproj change under the open project must emit");
        await Task.Delay(SettleWindow);
        Assert.That(_emission.CallCount, Is.EqualTo(1),
            "one external change must produce exactly one emit");

        // The routing pin (Task 10 review flag 1): the watcher event must go through the
        // coordinator's TRAILING-edge debouncer, never straight to the fire. A debounced fire
        // can never land earlier than one quiet period after the write (FS-event latency only
        // ADDS); a fire wired directly to the watcher event lands within FS latency (~ms).
        // The small tolerance absorbs timer-vs-stopwatch skew. Stall-immune by construction:
        // a slow runner makes the fire LATER, never earlier.
        Assert.That(_emission.Snapshot()[0].AtMs - beforeWriteMs,
            Is.GreaterThanOrEqualTo((long)QuietPeriod.TotalMilliseconds - 10),
            "the emit must arrive through the trailing-edge debouncer (>= one quiet period " +
            "after the external write), not directly off the watcher event");
    }

    [Test]
    public async Task BlprojChangeWithNoProjectOpen_DoesNotTrigger_UntilTheProjectOpens()
    {
        var project = NewProjectOnDisk("proj");
        using var coordinator = CreateCoordinator(); // nothing open — nothing watched

        TouchProjectFile(project);
        bool fired = await WaitUntilAsync(() => _emission.CallCount > 0, WatcherSettleWindow);
        Assert.That(fired, Is.False,
            "with no project open the .blproj must not be watched, so no emit");

        // The pre-open write above raised no watcher event at all (no watcher existed), so the
        // watcher's leading-edge throttle has no entry for this path and the post-open write's
        // event is guaranteed delivery.
        OpenProject(project);
        TouchProjectFile(project);
        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "opening the project must start the .blproj watch");
    }

    [Test]
    public async Task ProjectClose_StopsTheBlprojWatch()
    {
        var project = NewProjectOnDisk("proj");
        using var coordinator = CreateCoordinator();
        OpenProject(project);

        TouchProjectFile(project);
        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "precondition: the watch must be demonstrably ACTIVE before the close — " +
                     "otherwise the no-emit assertion below would pass vacuously");

        // Wait out the watcher's 500ms leading-edge throttle: without this, the post-close
        // write's raw FS event would be throttle-dropped even if a buggy close handler left
        // the watch alive, and the assertion below would prove nothing.
        await Task.Delay(ThrottleLapse);

        CloseProject(project);

        // Service-level probe: the coordinator's own path filter would ALSO swallow the emit
        // (watched == null after close), so the emission recorder alone cannot distinguish
        // "watch retired" from "watch leaked but filtered". The close must actually call
        // UnwatchFile — i.e. the watcher service must raise NO raw event for this path at all.
        int rawEventsAfterClose = 0;
        _watcher.FileChangedExternally += (_, e) =>
        {
            if (string.Equals(e.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref rawEventsAfterClose);
        };
        TouchProjectFile(project);

        bool firedAgain = await WaitUntilAsync(
            () => _emission.CallCount > 1 || Volatile.Read(ref rawEventsAfterClose) > 0,
            WatcherSettleWindow);
        Assert.That(firedAgain, Is.False, "closing the project must stop the .blproj watch");
        Assert.That(_emission.CallCount, Is.EqualTo(1),
            "only the pre-close change may have emitted");
        Assert.That(Volatile.Read(ref rawEventsAfterClose), Is.EqualTo(0),
            "the close must UNWATCH the file at the service level, not merely filter the " +
            "coordinator's handler — a leaked FileSystemWatcher keeps raising events forever");
    }

    [Test]
    public async Task ProjectSwitchWithoutClose_MovesTheBlprojWatch()
    {
        // ProjectService.CreateProjectAsync raises ProjectOpened WITHOUT a ProjectClosed for
        // the old project (OpenProjectAsync closes first; Create does not) — so the coordinator
        // must retire the old watch itself on every open, not rely on a close event.
        var projectA = NewProjectOnDisk("proj-a");
        var projectB = NewProjectOnDisk("proj-b");
        using var coordinator = CreateCoordinator();
        OpenProject(projectA);
        OpenProject(projectB); // no close in between — the CreateProjectAsync shape

        // Service-level probe (same rationale as ProjectClose_StopsTheBlprojWatch): the
        // coordinator's path filter alone would swallow the old project's emit even with the
        // old watch LEAKED, so also assert the watcher service raises no raw event for A.
        int rawEventsForOld = 0;
        _watcher.FileChangedExternally += (_, e) =>
        {
            if (string.Equals(e.FilePath, projectA.FilePath, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref rawEventsForOld);
        };
        TouchProjectFile(projectA);
        bool firedForOld = await WaitUntilAsync(
            () => _emission.CallCount > 0 || Volatile.Read(ref rawEventsForOld) > 0,
            WatcherSettleWindow);
        Assert.That(firedForOld, Is.False,
            "after the switch, the OLD project's .blproj must be unwatched");
        Assert.That(Volatile.Read(ref rawEventsForOld), Is.EqualTo(0),
            "the switch must retire the OLD watch at the service level (UnwatchFile), " +
            "not merely filter the coordinator's handler");

        TouchProjectFile(projectB);
        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "after the switch, the NEW project's .blproj must be watched");
    }

    [Test]
    public async Task SuppressedWrite_DoesNotTrigger_AndTheWatchOutlivesTheSuppression()
    {
        var project = NewProjectOnDisk("proj");
        using var coordinator = CreateCoordinator();
        OpenProject(project);

        // The IDE-save shape: suppress → write → dispose (ProjectService.SaveProjectAsync
        // wraps its serializer write exactly like this). WatcherSettleWindow exceeds the
        // watcher's 200ms post-dispose unsuppression grace, so the follow-up write below is
        // genuinely unsuppressed.
        using (_watcher.SuppressNotifications(project.FilePath))
        {
            TouchProjectFile(project);
        }
        bool fired = await WaitUntilAsync(() => _emission.CallCount > 0, WatcherSettleWindow);
        Assert.That(fired, Is.False,
            "a suppressed (IDE-side) .blproj write must not loop back into a regen");

        // Suppression is a WINDOW, not a kill switch: a later external write must still emit.
        // (Suppressed events skip the watcher's throttle bookkeeping — IsSuppressed is checked
        // BEFORE IsDebounced — so this event is guaranteed delivery.)
        TouchProjectFile(project);
        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "the watch must outlive the suppression window");
    }

    [Test]
    public async Task SaveProjectAsync_IsSuppressed_ButExternalEditsStillTrigger()
    {
        // End-to-end over the REAL ProjectService — the production writer of the open
        // project's .blproj: SaveProjectAsync must wrap its serializer write in
        // SuppressNotifications so IDE-initiated project saves do not bounce back as
        // "external" changes, while a genuinely external edit still does. Also exercises the
        // real ProjectOpened wiring (CreateProjectAsync raises it after writing the .blproj,
        // so the creation write itself precedes the watch and is invisible).
        var root = Path.Combine(Path.GetTempPath(), "vgs-t11-regen", Guid.NewGuid().ToString("N"));
        _tempRoots.Add(root);
        Directory.CreateDirectory(root);
        using var fileService = new FileService();
        var projectService = new ProjectService(fileService, null, _watcher);
        using var coordinator = new RegenOnSaveCoordinator(
            _emission, projectService, _buildService.Object, _events, _watcher, QuietPeriod);

        var project = await projectService.CreateProjectAsync(
            "SuppressedSave", root, ProjectTemplateKind.ConsoleApplication);

        await projectService.SaveProjectAsync();
        bool fired = await WaitUntilAsync(() => _emission.CallCount > 0, WatcherSettleWindow);
        Assert.That(fired, Is.False,
            "an IDE-side SaveProjectAsync must not trigger a regen (SuppressNotifications)");

        File.WriteAllText(project.FilePath,
            "<BasicLangProject Version=\"1.0\" /><!-- external -->");
        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "an external edit of the same .blproj must still trigger — the save's " +
                     "suppression must be a disposed window, not a permanent kill");
    }

    [Test]
    public async Task ProjectAlreadyOpenAtConstruction_IsWatched()
    {
        // Defensive lifecycle pin: in production the coordinator is resolved at startup,
        // before any project can open — but if construction ever moves after a
        // restore-on-launch open, the already-open project's .blproj must still get its
        // watch (picked up in the ctor, no event needed).
        var project = NewProjectOnDisk("proj");
        _currentProject = project;
        using var coordinator = CreateCoordinator();

        TouchProjectFile(project);
        Assert.That(await WaitUntilAsync(() => _emission.CallCount >= 1, WaitBound),
            Is.True, "a project already open at construction must be watched");
    }

    // ---- fakes ----

    /// <summary>
    /// Thread-safe call recorder for <see cref="IIntelliSenseEmissionService"/> — the debouncer
    /// fires on a thread-pool thread while tests read counts from the runner's thread.
    /// </summary>
    private sealed class RecordingEmissionService : IIntelliSenseEmissionService
    {
        private readonly object _lock = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(BasicLangProject? Project, string Configuration, long AtMs)> _calls = new();

        public int CallCount { get { lock (_lock) return _calls.Count; } }

        /// <summary>
        /// Milliseconds on the recorder's own clock — the SAME clock that stamps
        /// <c>AtMs</c> on each call, so tests can compare "before the trigger" against
        /// "at the emit" without cross-clock skew.
        /// </summary>
        public long NowMs => _clock.ElapsedMilliseconds;

        public (BasicLangProject? Project, string Configuration, long AtMs)[] Snapshot()
        {
            lock (_lock) return _calls.ToArray();
        }

        public Task RequestEmit(BasicLangProject? project, string configuration)
        {
            lock (_lock) _calls.Add((project, configuration, _clock.ElapsedMilliseconds));
            return Task.CompletedTask;
        }
    }
}
