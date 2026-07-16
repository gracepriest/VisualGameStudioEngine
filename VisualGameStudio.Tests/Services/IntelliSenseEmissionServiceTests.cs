using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Task 10 (C++ Phase 3a): the IDE side of IntelliSense emission — the thing project-open calls.
///
/// <para>
/// Task 9 already pins WHAT gets emitted (<c>IntelliSenseEmitterTests</c>, at the compiler seam).
/// This fixture pins the properties that only exist because the caller is an IDE: emission runs
/// off the calling thread, a burst of opens coalesces instead of queueing a front-end run each,
/// two emissions never write <c>obj/gen</c> at once, a failure never escapes to the caller, and
/// a non-native project pays nothing at all.
/// </para>
///
/// <para>
/// <b>Why not test through MainWindowViewModel:</b> nothing in this suite constructs it — it
/// takes ~30 injected services and owns the Avalonia dock tree. The emission POLICY lives here,
/// in a service; the view model contribution is one discarded call, which reads as a fact about
/// wiring rather than behavior worth a harness.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class IntelliSenseEmissionServiceTests
{
    private string _dir = null!;
    private RecordingOutput _output = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-ise-svc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _output = new RecordingOutput();
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    // ---- harness ----

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>A service whose emitter is <paramref name="emit"/>: (projectFilePath, configuration).</summary>
    private IntelliSenseEmissionService Service(Func<string, string, CppProjectBuildResult> emit)
        => new(_output, emit);

    private static CppProjectBuildResult Ok() => new() { Success = true };

    private BasicLangProject Project(TargetBackend backend = TargetBackend.Cpp,
        ProjectLanguage language = ProjectLanguage.BasicLang)
        => new()
        {
            Name = "App",
            FilePath = Path.Combine(_dir, "App.blproj"),
            TargetBackend = backend,
            Language = language,
        };

    private static void RecordMax(ref int slot, int candidate)
    {
        int seen;
        while (candidate > (seen = Volatile.Read(ref slot)))
            if (Interlocked.CompareExchange(ref slot, candidate, seen) == seen) return;
    }

    // ================= native-only =================

    // A C#-backend project has no C++ for clangd to read. Emission is a multi-second front-end
    // run, so paying it for every non-native open would be a pure tax.
    [Test]
    public async Task RequestEmit_CSharpBackendProject_NeverReachesTheEmitter()
    {
        var calls = 0;
        var svc = Service((_, _) => { Interlocked.Increment(ref calls); return Ok(); });

        await svc.RequestEmit(Project(backend: TargetBackend.CSharp), "Debug");

        Assert.That(calls, Is.Zero, "a C#-backend project must not pay for a C++ compile database");
    }

    [Test]
    public async Task RequestEmit_NullProject_NeverReachesTheEmitter()
    {
        var calls = 0;
        var svc = Service((_, _) => { Interlocked.Increment(ref calls); return Ok(); });

        await svc.RequestEmit(null, "Debug");

        Assert.That(calls, Is.Zero);
    }

    // Both halves of IsNativeBuild: hand-written C++ (Language=Cpp) AND BasicLang transpiled to
    // native (TargetBackend=Cpp). Keyed on the property, not on one of its two causes.
    [TestCase(TargetBackend.Cpp, ProjectLanguage.BasicLang, TestName = "RequestEmit_TargetBackendCpp_Emits")]
    [TestCase(TargetBackend.CSharp, ProjectLanguage.Cpp, TestName = "RequestEmit_LanguageCpp_Emits")]
    public async Task RequestEmit_NativeProject_ReachesTheEmitter(TargetBackend backend, ProjectLanguage language)
    {
        var calls = 0;
        var svc = Service((_, _) => { Interlocked.Increment(ref calls); return Ok(); });

        await svc.RequestEmit(Project(backend, language), "Debug");

        Assert.That(calls, Is.EqualTo(1));
    }

    // ================= off the calling (UI) thread =================

    // "Off the UI thread" without an Avalonia dispatcher: assert the two properties that MAKE it
    // true. (1) RequestEmit returns while the emitter is still running — so the caller was never
    // blocked. (2) The emitter body runs on a pool thread, on a different thread than the caller,
    // with no SynchronizationContext — a caller-installed context stands in for the UI thread's,
    // and the body must not have continued back onto it.
    [Test]
    public async Task RequestEmit_RunsOnAPoolThread_AndReturnsBeforeTheEmitterFinishes()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var emitThreadId = 0;
        var onPool = false;
        var underCallerContext = true;

        var callerContext = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(callerContext);
        try
        {
            var svc = Service((_, _) =>
            {
                emitThreadId = Environment.CurrentManagedThreadId;
                onPool = Thread.CurrentThread.IsThreadPoolThread;
                underCallerContext = ReferenceEquals(SynchronizationContext.Current, callerContext);
                entered.Set();
                release.Wait(Patience);
                return Ok();
            });

            var callerThreadId = Environment.CurrentManagedThreadId;
            var task = svc.RequestEmit(Project(), "Debug");

            Assert.That(entered.Wait(Patience), Is.True, "the emission never started");
            Assert.That(task.IsCompleted, Is.False,
                "RequestEmit only returned after the emitter finished — the calling thread was blocked "
                + "for a full front-end run, which on the open path is the UI thread");

            release.Set();
            await task;

            Assert.Multiple(() =>
            {
                Assert.That(emitThreadId, Is.Not.EqualTo(callerThreadId),
                    "emission ran on the calling thread");
                Assert.That(onPool, Is.True, "emission must run on the thread pool");
                Assert.That(underCallerContext, Is.False,
                    "emission ran under the caller's SynchronizationContext — on the open path that "
                    + "context is the UI thread's, so this would freeze the IDE");
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    // ================= coalescing / cancellation =================

    // A burst of opens must cost the in-flight emission plus the last request — nothing in
    // between. The configuration string doubles as each request's identity.
    [Test]
    public async Task RequestEmit_SupersededRequest_NeverReachesTheEmitter()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var seen = new ConcurrentQueue<string>();

        var svc = Service((_, configuration) =>
        {
            seen.Enqueue(configuration);
            if (configuration == "First") { firstEntered.Set(); release.Wait(Patience); }
            return Ok();
        });

        var t1 = svc.RequestEmit(Project(), "First");
        Assert.That(firstEntered.Wait(Patience), Is.True, "sanity: the first emission must be in flight");

        var t2 = svc.RequestEmit(Project(), "Superseded");
        var t3 = svc.RequestEmit(Project(), "Last");

        release.Set();
        await Task.WhenAll(t1, t2, t3);

        Assert.That(seen, Is.EqualTo(new[] { "First", "Last" }),
            "a request that a newer one superseded before it started must never run: the whole point "
            + "of coalescing is that N rapid opens do not cost N front-end runs");
    }

    // Two emissions writing obj/gen for the same project at once would interleave their writes.
    // Cancellation alone does NOT prevent this: superseding cancels the QUEUED request's token,
    // but an emission already inside the front end cannot be interrupted — so the next one must
    // WAIT for it rather than start alongside it.
    [Test]
    public async Task RequestEmit_WhileOneIsInFlight_TheNextWaitsInsteadOfOverlapping()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var concurrent = 0;
        var peak = 0;

        var svc = Service((_, configuration) =>
        {
            RecordMax(ref peak, Interlocked.Increment(ref concurrent));
            if (configuration == "First") { firstEntered.Set(); release.Wait(Patience); }
            Interlocked.Decrement(ref concurrent);
            return Ok();
        });

        var t1 = svc.RequestEmit(Project(), "First");
        Assert.That(firstEntered.Wait(Patience), Is.True, "sanity: the first emission must be in flight");

        var t2 = svc.RequestEmit(Project(), "Second");
        // Give an unserialized implementation every chance to start Second alongside First.
        // Assert on the PEAK, not on `concurrent`: an unserialized Second runs to completion and
        // decrements again, so by now `concurrent` would read 1 either way — it catches nothing.
        Thread.Sleep(300);
        Assert.That(Volatile.Read(ref peak), Is.EqualTo(1),
            "the second emission started while the first was still writing obj/gen");

        release.Set();
        await Task.WhenAll(t1, t2);

        Assert.That(peak, Is.EqualTo(1), "two emissions must never write obj/gen at the same time");
    }

    // ================= failure never escapes =================

    // The open path is `_ = RequestEmit(...)` — a faulted task there is an unobserved exception.
    // And project open must survive a broken project regardless.
    [Test]
    public async Task RequestEmit_WhenTheEmitterThrows_TheTaskStillCompletesAndOutputSaysWhy()
    {
        var svc = Service((_, _) => throw new IOException("obj is read-only"));

        var task = svc.RequestEmit(Project(), "Debug");
        await task;

        Assert.Multiple(() =>
        {
            Assert.That(task.IsFaulted, Is.False, "a faulted task on the open path is an unobserved exception");
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_output.Lines.Where(l => l.Contains("obj is read-only")), Is.Not.Empty,
                "a swallowed failure with no trace is worse than the crash: " + _output.Dump());
        });
    }

    // A transpile failure is the NORMAL mid-edit state, so it must be reported as one concise
    // line — not as a phantom build's worth of diagnostics on the Output panel — and it must not
    // be silent either.
    [Test]
    public async Task RequestEmit_WhenEmissionFails_ReportsItConciselyAndDoesNotThrow()
    {
        var failure = new CppProjectBuildResult { Success = false };
        failure.Diagnostics.Add(new CppDiagnostic { Code = "BL0104", Message = "Unexpected token '((('" });
        failure.Diagnostics.Add(new CppDiagnostic { Code = "BL0104", Message = "Expected 'End Function'" });
        var svc = Service((_, _) => failure);

        await svc.RequestEmit(Project(), "Debug");

        Assert.Multiple(() =>
        {
            Assert.That(_output.Lines.Where(l => l.Contains("BL0104")), Is.Not.Empty,
                "a failure must say something: " + _output.Dump());
            Assert.That(_output.Lines, Has.Count.EqualTo(1),
                "one concise line — opening a project must not read like a failed build: " + _output.Dump());
        });
    }

    // An emission already inside the front end cannot be interrupted, so a superseded one still
    // runs to completion and still produces a result — describing artifacts the newer emission is
    // about to overwrite. Reporting THAT failure would be a stale message about state that no
    // longer holds. (Found by mutation testing: deleting the post-emit cancellation check left
    // every other test in this fixture green.)
    [Test]
    public async Task RequestEmit_WhenASupersededEmissionFails_ItsStaleResultIsNotReported()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var stale = new CppProjectBuildResult { Success = false };
        stale.Diagnostics.Add(new CppDiagnostic { Code = "BL0104", Message = "mid-edit garbage" });

        var svc = Service((_, configuration) =>
        {
            if (configuration != "Superseded") return Ok();
            firstEntered.Set();
            release.Wait(Patience);
            return stale;
        });

        var t1 = svc.RequestEmit(Project(), "Superseded");
        Assert.That(firstEntered.Wait(Patience), Is.True, "sanity: the first emission must be in flight");

        // Supersedes t1 — while t1 is already inside the emitter, so t1 cannot be stopped.
        var t2 = svc.RequestEmit(Project(), "Last");
        release.Set();
        await Task.WhenAll(t1, t2);

        Assert.That(_output.Lines, Is.Empty,
            "the superseded emission's failure describes headers the newer one has already replaced: "
            + _output.Dump());
    }

    [Test]
    public async Task RequestEmit_OnSuccess_SaysNothing()
    {
        var svc = Service((_, _) => Ok());

        await svc.RequestEmit(Project(), "Debug");

        Assert.That(_output.Lines, Is.Empty,
            "the happy path is invisible — opening a project must not chatter into Output: " + _output.Dump());
    }

    // ================= end to end, through the real emitter =================

    // The production constructor, a real project on disk, no fakes. Also THE D2 pin: this machine
    // has MSVC and no clang++/g++ on PATH, so CppToolchain.Find() returns MSVC here. A "cl" driver
    // in the database would mean project-open probed for a toolchain; "clang++" proves it did not.
    [Test]
    public async Task RequestEmit_RealNativeProject_WritesHeadersAndAnUnprobedClangDatabase()
    {
        File.WriteAllText(Path.Combine(_dir, "App.bas"), "Sub Main()\n    PrintLine 7\nEnd Sub\n");
        File.WriteAllText(Path.Combine(_dir, "App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var svc = new IntelliSenseEmissionService(_output);
        await svc.RequestEmit(Project(), "Debug");

        var dbPath = Path.Combine(_dir, "obj", "compile_commands.json");
        Assert.That(File.Exists(dbPath), Is.True,
            "clangd has nothing to read without a compile database: " + _output.Dump());
        Assert.That(File.Exists(Path.Combine(_dir, "obj", "gen", "BasicLangRuntime.g.h")), Is.True,
            "the generated headers are the other half of what clangd needs");

        var args = JsonNode.Parse(File.ReadAllText(dbPath))![0]!["arguments"]!
            .AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Multiple(() =>
        {
            // clangd reads arguments[0] as the driver — assert the POSITION, not a substring of
            // the file (a path merely containing "clang++" would pass that).
            Assert.That(args[0], Is.EqualTo("clang++"),
                "D2: project open must not shell out to vswhere. This machine's CppToolchain.Find() "
                + "returns MSVC, so a \"cl\" here would mean open probed for a toolchain");
            Assert.That(args, Has.One.EqualTo("-std=c++20"),
                "the GNU flag style that pairs with the clang++ driver, as an exact token");
            Assert.That(args, Has.None.StartsWith("/std:"),
                "MSVC flags under a clang++ driver mis-parse silently in clangd");
        });
    }

    // Emission must never create build-output directories: opening a project is not a build.
    // Task 9 leaves result.OutputPath pointing at bin/<config> even so — nothing may act on it.
    [Test]
    public async Task RequestEmit_RealNativeProject_DoesNotCreateBinDirectory()
    {
        File.WriteAllText(Path.Combine(_dir, "App.bas"), "Sub Main()\n    PrintLine 7\nEnd Sub\n");
        File.WriteAllText(Path.Combine(_dir, "App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var svc = new IntelliSenseEmissionService(_output);
        await svc.RequestEmit(Project(), "Debug");

        Assert.That(Directory.Exists(Path.Combine(_dir, "bin")), Is.False,
            "opening a project must not litter it with empty build-output dirs");
    }

    // A project whose .blproj is not on disk (deleted between open and the handler). ProjectFile.Load
    // throws FileNotFoundException; it must not reach the caller.
    [Test]
    public async Task RequestEmit_RealMissingProjectFile_DoesNotThrow()
    {
        var svc = new IntelliSenseEmissionService(_output);

        var task = svc.RequestEmit(Project(), "Debug");
        await task;

        Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion), _output.Dump());
    }

    // ================= disposal =================

    // Shutdown must not start a fresh multi-second front-end run.
    [Test]
    public async Task RequestEmit_AfterDispose_NeverReachesTheEmitter()
    {
        var calls = 0;
        var svc = Service((_, _) => { Interlocked.Increment(ref calls); return Ok(); });

        svc.Dispose();
        await svc.RequestEmit(Project(), "Debug");

        Assert.That(calls, Is.Zero, "a disposed service must not schedule work");
    }

    // The _disposed guard above only covers requests arriving AFTER Dispose. This covers the one
    // already QUEUED at the moment of disposal: shutdown must not hand the thread pool a fresh
    // multi-second front-end run on the way out. Dispose deliberately does not WAIT (that would
    // hold shutdown behind a compile) — cancelling is the whole mechanism, so it needs a test.
    [Test]
    public async Task Dispose_CancelsAnAlreadyQueuedEmission()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var seen = new ConcurrentQueue<string>();

        var svc = Service((_, configuration) =>
        {
            seen.Enqueue(configuration);
            if (configuration == "First") { firstEntered.Set(); release.Wait(Patience); }
            return Ok();
        });

        var t1 = svc.RequestEmit(Project(), "First");
        Assert.That(firstEntered.Wait(Patience), Is.True, "sanity: the first emission must be in flight");

        // Queued behind the in-flight one, so it has not reached the emitter yet.
        var t2 = svc.RequestEmit(Project(), "Queued");

        svc.Dispose();
        release.Set();
        await Task.WhenAll(t1, t2);

        Assert.That(seen, Is.EqualTo(new[] { "First" }),
            "an emission queued at the moment of disposal must never start: the IDE is shutting down "
            + "and its artifacts would be regenerated on next open anyway");
    }

    /// <summary>Thread-safe recording IOutputService — emission runs on the pool.</summary>
    private sealed class RecordingOutput : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => _lines.ToArray();
        public string Dump() => _lines.IsEmpty ? "<no output>" : string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => _lines.ToArray();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }
}
