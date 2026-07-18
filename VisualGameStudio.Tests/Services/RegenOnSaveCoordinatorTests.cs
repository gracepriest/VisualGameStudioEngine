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
/// </summary>
[TestFixture]
public class RegenOnSaveCoordinatorTests
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(5);

    private EventAggregator _events = null!;
    private RecordingEmissionService _emission = null!;
    private Mock<IProjectService> _projectService = null!;
    private Mock<IBuildService> _buildService = null!;

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
    }

    // ---- harness ----

    private RegenOnSaveCoordinator CreateCoordinator()
        => new(_emission, _projectService.Object, _buildService.Object, _events, QuietPeriod);

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

    // ---- fakes ----

    /// <summary>
    /// Thread-safe call recorder for <see cref="IIntelliSenseEmissionService"/> — the debouncer
    /// fires on a thread-pool thread while tests read counts from the runner's thread.
    /// </summary>
    private sealed class RecordingEmissionService : IIntelliSenseEmissionService
    {
        private readonly object _lock = new();
        private readonly List<(BasicLangProject? Project, string Configuration)> _calls = new();

        public int CallCount { get { lock (_lock) return _calls.Count; } }

        public (BasicLangProject? Project, string Configuration)[] Snapshot()
        {
            lock (_lock) return _calls.ToArray();
        }

        public Task RequestEmit(BasicLangProject? project, string configuration)
        {
            lock (_lock) _calls.Add((project, configuration));
            return Task.CompletedTask;
        }
    }
}
