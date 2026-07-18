using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Configuration;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Pins the registry that lets there be more than one language server: N services, one per
/// <see cref="LanguageServerDescriptor"/>, routed to by file extension.
///
/// <para>
/// The regression it exists to prevent is a SECOND server corrupting the first's state — the
/// process, the reader/writer, the request-id space, the restart budget. Instantiating one
/// <c>LanguageService</c> per descriptor solves that by construction; these tests pin that the
/// construction actually holds (<see cref="PerServerState_IsNeverSharedBetweenServices"/>) and
/// that the registry only routes.
/// </para>
///
/// <para>
/// Routing tests use Moq fakes — no server process is ever started. The two tests that must
/// speak about REAL per-server state build real <c>LanguageService</c> instances, which is
/// inert: nothing is spawned until <c>StartAsync</c>.
/// </para>
/// </summary>
[TestFixture]
public class LanguageServiceRegistryTests
{
    private const string BasicLangCompiler = @"C:\x\BasicLang.dll";
    private const string ClangdExe = @"C:\x\clangd.exe";

    // ---- Fakes -------------------------------------------------------------

    private static Mock<ILanguageService> Fake(LanguageServerDescriptor descriptor, bool connected)
    {
        var mock = new Mock<ILanguageService>();
        mock.SetupGet(x => x.Descriptor).Returns(descriptor);
        mock.SetupGet(x => x.IsConnected).Returns(connected);
        mock.Setup(x => x.StartAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.StopAsync()).Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<ILanguageService> FakeBasicLang(bool connected = false) =>
        Fake(LanguageServerDescriptor.BasicLang(BasicLangCompiler), connected);

    private static Mock<ILanguageService> FakeClangd(bool connected = false) =>
        Fake(LanguageServerDescriptor.Clangd(ClangdExe), connected);

    private static LanguageServiceRegistry RegistryOf(params Mock<ILanguageService>[] mocks) =>
        new(mocks.Select(m => m.Object).ToArray());

    // ---- Routing -----------------------------------------------------------

    [Test]
    public void GetFor_RoutesByExtension()
    {
        var r = RegistryOf(FakeBasicLang(), FakeClangd());

        Assert.Multiple(() =>
        {
            Assert.That(r.GetFor("a.bas")!.Descriptor.Id, Is.EqualTo("basiclang"));
            Assert.That(r.GetFor("a.cpp")!.Descriptor.Id, Is.EqualTo("clangd"));
            Assert.That(r.GetFor("a.h")!.Descriptor.Id, Is.EqualTo("clangd"));
            Assert.That(r.GetFor("a.txt"), Is.Null, "no server owns .txt");
        });
    }

    // A registry holding only BasicLang must not answer for C++ — the "null silently becomes
    // basiclang" trap. This is the state the IDE ships in on any machine where no clangd is
    // found (DI registers clangd only when ClangdLocator resolves one).
    [Test]
    public void GetFor_UnregisteredLanguage_IsNull()
    {
        var r = RegistryOf(FakeBasicLang());

        Assert.Multiple(() =>
        {
            Assert.That(r.GetFor("a.cpp"), Is.Null, ".cpp is routed by the map but no server is registered for it");
            Assert.That(r.GetFor("a.bas")!.Descriptor.Id, Is.EqualTo("basiclang"));
        });
    }

    // GetFor is called on every keystroke against whatever document is focused, including files
    // with no extension at all. It must answer null, not throw.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("Makefile")]
    [TestCase(@"C:\proj\src")]
    public void GetFor_PathWithNoRoutableExtension_IsNull(string? path) =>
        Assert.That(RegistryOf(FakeBasicLang(), FakeClangd()).GetFor(path), Is.Null);

    // The registry's answer and the descriptor's own ownership rule must never disagree: a file
    // routed to a server the server does not think it owns would be rejected by LanguageService's
    // didOpen guard, silently, with IntelliSense simply absent.
    [Test]
    public void GetFor_AgreesWithTheOwningDescriptor_ForEveryRoutedExtension()
    {
        var r = RegistryOf(FakeBasicLang(), FakeClangd());

        Assert.Multiple(() =>
        {
            foreach (var ext in LanguageFileTypes.LspRoutedExtensions)
            {
                var path = "a" + ext;
                var service = r.GetFor(path);

                Assert.That(service, Is.Not.Null, $"{ext} is LSP-routed but the registry found no server");
                Assert.That(service!.Descriptor.Owns(path), Is.True,
                    $"the registry routed {ext} to {service.Descriptor.Id}, which does not own it");
                Assert.That(service.Descriptor.LanguageIdFor(path), Is.EqualTo(LanguageFileTypes.GetLspLanguageId(path)),
                    $"{ext}: the server would announce a languageId the routing map disagrees with");
            }
        });
    }

    // GetById reaches a specific server by identity (not by routing a document) — the path the
    // BasicLang-only rootless autostart uses instead of a representative filename.
    [Test]
    public void GetById_ReturnsTheServerWithThatDescriptorId()
    {
        var r = RegistryOf(FakeBasicLang(), FakeClangd());

        Assert.Multiple(() =>
        {
            Assert.That(r.GetById(LanguageServerDescriptor.BasicLangId)!.Descriptor.Id, Is.EqualTo("basiclang"));
            Assert.That(r.GetById(LanguageServerDescriptor.ClangdId)!.Descriptor.Id, Is.EqualTo("clangd"));
        });
    }

    [Test]
    public void GetById_UnknownOrUnregisteredId_IsNull()
    {
        var r = RegistryOf(FakeBasicLang()); // clangd not registered

        Assert.Multiple(() =>
        {
            Assert.That(r.GetById(LanguageServerDescriptor.ClangdId), Is.Null, "clangd is not registered");
            Assert.That(r.GetById("nope"), Is.Null);
            Assert.That(r.GetById("BasicLang"), Is.Null, "id match is ordinal, not case-insensitive");
        });
    }

    [Test]
    public void All_ExposesEveryRegisteredServer()
    {
        var bl = FakeBasicLang();
        var cl = FakeClangd();

        var r = RegistryOf(bl, cl);

        Assert.That(r.All, Is.EquivalentTo(new[] { bl.Object, cl.Object }));
    }

    // IReadOnlyList is a read-only VIEW, not a read-only object: had All handed back its backing
    // array, a caller could downcast it and swap a server out from under the registry. Pinned so
    // the returned object is genuinely not the mutable array — same defence LanguageFileTypes
    // applies to its routing arrays.
    [Test]
    public void All_CannotBeDowncastToTheMutableBackingArray() =>
        Assert.That(RegistryOf(FakeBasicLang(), FakeClangd()).All, Is.Not.AssignableTo<ILanguageService[]>());

    // ---- Per-server connection state ---------------------------------------

    // THE regression this whole task exists to prevent.
    [Test]
    public void IsConnectedFor_IsPerServer_NotGlobal()
    {
        var bl = FakeBasicLang(connected: false);      // BasicLang down/restarting
        var cl = FakeClangd(connected: true);

        var r = RegistryOf(bl, cl);

        Assert.Multiple(() =>
        {
            Assert.That(r.IsConnectedFor("a.cpp"), Is.True,
                "clangd must keep working when the BasicLang server is down");
            Assert.That(r.IsConnectedFor("a.bas"), Is.False);
        });
    }

    [Test]
    public void IsConnectedFor_UnknownExtension_IsFalse() =>
        Assert.That(RegistryOf(FakeBasicLang(connected: true)).IsConnectedFor("a.txt"), Is.False);

    /// <summary>
    /// The state a second server would corrupt is per-INSTANCE state, so one service per
    /// descriptor fixes it for free — provided nobody ever makes one of these fields static.
    /// This asks the real type, because that is the only thing that can answer.
    /// </summary>
    /// <remarks>
    /// Reflection, deliberately: this assembly has no <c>InternalsVisibleTo</c> for
    /// VisualGameStudio.ProjectSystem, and these fields must stay private — exposing them just to
    /// be testable would be the larger sin. <c>BindingFlags.Instance</c> is the assertion: the
    /// lookup returns null the moment a field becomes static (one shared request-id space, one
    /// shared restart budget across both servers), and the field-value comparison catches a
    /// <c>static readonly</c> that would be shared while still resolving as a field.
    /// </remarks>
    [TestCase("_restartPolicy", Description = "one restart budget: a crash-looping clangd would exhaust BasicLang's")]
    [TestCase("_pendingRequests", Description = "one id space: clangd's response would complete BasicLang's request")]
    [TestCase("_lock")]
    [TestCase("_frameWriter")]
    public void PerServerState_IsNeverSharedBetweenServices(string fieldName)
    {
        var bl = new LanguageService(new NullOutput(), null, LanguageServerDescriptor.BasicLang(BasicLangCompiler));
        var cl = new LanguageService(new NullOutput(), null, LanguageServerDescriptor.Clangd(ClangdExe));
        using var r = new LanguageServiceRegistry(new ILanguageService[] { bl, cl });

        var field = typeof(LanguageService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null,
            $"LanguageService.{fieldName} is not an instance field — if it went static, both servers now share it");

        var basicLangState = field!.GetValue(r.GetFor("a.bas"));
        var clangdState = field.GetValue(r.GetFor("a.cpp"));

        Assert.That(basicLangState, Is.Not.Null);
        Assert.That(basicLangState, Is.Not.SameAs(clangdState),
            $"both servers share one {fieldName}");
    }

    /// <summary>Each server owning its own restart budget, stated the way the plan asks for it.</summary>
    [Test]
    public void EachServer_HasItsOwnRestartPolicy()
    {
        var bl = new LanguageService(new NullOutput(), null, LanguageServerDescriptor.BasicLang(BasicLangCompiler));
        var cl = new LanguageService(new NullOutput(), null, LanguageServerDescriptor.Clangd(ClangdExe));
        using var r = new LanguageServiceRegistry(new ILanguageService[] { bl, cl });

        Assert.That(RestartPolicyOf(r.GetFor("a.bas")!), Is.Not.SameAs(RestartPolicyOf(r.GetFor("a.cpp")!)));
    }

    private static RestartPolicy RestartPolicyOf(ILanguageService service) =>
        (RestartPolicy)typeof(LanguageService)
            .GetField("_restartPolicy", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(service)!;

    // ---- Construction guards -----------------------------------------------

    // Two services claiming one language is ambiguous routing — and the way it arises is the
    // two-process bug: BasicLang registered twice means two `dotnet --lsp` children, one of them
    // permanently orphaned because only one is ever routed to (and only one gets Stop/Dispose).
    [Test]
    public void Ctor_RejectsTwoServersClaimingTheSameLanguage()
    {
        var ex = Assert.Throws<ArgumentException>(() => RegistryOf(FakeBasicLang(), FakeBasicLang()));

        Assert.That(ex!.Message, Does.Contain("basiclang"));
    }

    [Test]
    public void Ctor_RejectsAnEmptyRegistry() =>
        Assert.Throws<ArgumentException>(() => new LanguageServiceRegistry(Array.Empty<ILanguageService>()));

    [Test]
    public void Ctor_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => new LanguageServiceRegistry(null!));

    // ---- Lifetime ----------------------------------------------------------

    [Test]
    public void Dispose_DisposesEveryServer()   // guards the orphan-on-exit regression
    {
        var bl = FakeBasicLang();
        var cl = FakeClangd();

        RegistryOf(bl, cl).Dispose();

        bl.Verify(x => x.Dispose(), Times.Once);
        cl.Verify(x => x.Dispose(), Times.Once);
    }

    // One bad child must not orphan the others' server processes, and must not abort the
    // container's own disposal loop (which would orphan every singleton registered after this one).
    [Test]
    public void Dispose_DisposesEveryServer_EvenWhenOneThrows()
    {
        var bl = FakeBasicLang();
        bl.Setup(x => x.Dispose()).Throws<InvalidOperationException>();
        var cl = FakeClangd();

        var r = RegistryOf(bl, cl);

        Assert.DoesNotThrow(() => r.Dispose(), "Dispose must never throw — it runs inside the container's disposal loop");
        cl.Verify(x => x.Dispose(), Times.Once, "clangd's process was orphaned because BasicLang's Dispose threw");
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        var bl = FakeBasicLang();

        var r = RegistryOf(bl);
        r.Dispose();
        r.Dispose();

        bl.Verify(x => x.Dispose(), Times.Once);
    }

    // ---- Start / stop ------------------------------------------------------

    [Test]
    public async Task StartAllAsync_StartsEveryServerRootedAtTheWorkspace()
    {
        var bl = FakeBasicLang();
        var cl = FakeClangd();

        await RegistryOf(bl, cl).StartAllAsync(@"C:\proj");

        bl.Verify(x => x.StartAsync(@"C:\proj", It.IsAny<CancellationToken>()), Times.Once);
        cl.Verify(x => x.StartAsync(@"C:\proj", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A rootless bulk start is the bug this method exists to make un-expressible: clangd with no
    /// root omits --compile-commands-dir entirely, never finds obj/compile_commands.json, and
    /// answers with garbage — silently. The nullable annotation cannot enforce it (CS8604 is in
    /// NoWarn repo-wide), so the check is at runtime.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void StartAllAsync_RejectsARootlessStart(string? root)
    {
        var bl = FakeBasicLang();

        Assert.ThrowsAsync<ArgumentException>(() => RegistryOf(bl).StartAllAsync(root!));

        bl.Verify(x => x.StartAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "a rootless start must be refused before any server is launched, not half-applied");
    }

    // Graceful degradation: clangd failing to launch must not cost the user BasicLang IntelliSense.
    [Test]
    public void StartAllAsync_StartsTheOtherServers_WhenOneFails()
    {
        var bl = FakeBasicLang();
        var cl = FakeClangd();
        cl.Setup(x => x.StartAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("clangd exploded"));

        var ex = Assert.ThrowsAsync<AggregateException>(() => RegistryOf(bl, cl).StartAllAsync(@"C:\proj"));

        Assert.Multiple(() =>
        {
            bl.Verify(x => x.StartAsync(@"C:\proj", It.IsAny<CancellationToken>()), Times.Once,
                "BasicLang was never started because clangd failed first");
            Assert.That(ex!.InnerExceptions.Select(e => e.Message), Has.One.EqualTo("clangd exploded"),
                "the failure must be reported, not swallowed");
        });
    }

    [Test]
    public async Task StopAllAsync_StopsEveryServer()
    {
        var bl = FakeBasicLang();
        var cl = FakeClangd();

        await RegistryOf(bl, cl).StopAllAsync();

        bl.Verify(x => x.StopAsync(), Times.Once);
        cl.Verify(x => x.StopAsync(), Times.Once);
    }

    [Test]
    public void StopAllAsync_StopsTheOtherServers_WhenOneFails()
    {
        var bl = FakeBasicLang();
        bl.Setup(x => x.StopAsync()).ThrowsAsync(new InvalidOperationException("boom"));
        var cl = FakeClangd();

        Assert.ThrowsAsync<AggregateException>(() => RegistryOf(bl, cl).StopAllAsync());

        cl.Verify(x => x.StopAsync(), Times.Once, "clangd was left running because BasicLang's Stop threw");
    }

    // ---- Project switch / re-rooting ---------------------------------------
    //
    // StartAsync NO-OPS on an already-connected server (StartCoreAsync: `if (IsConnected) return;`),
    // so opening project B while clangd is connected to project A used to leave clangd rooted at A
    // — silently answering completions and diagnostics from project A's compilation database while
    // the user edits project B. Nothing fails; the answers merely look right and are wrong.
    // Re-rooting is therefore Stop-then-Start, and it is the registry's job.

    /// <summary>Records the lifecycle calls a service receives, in order.</summary>
    private static List<string> RecordLifecycle(Mock<ILanguageService> mock)
    {
        var calls = new List<string>();
        mock.Setup(x => x.StopAsync()).Returns(Task.CompletedTask).Callback(() => calls.Add("stop"));
        mock.Setup(x => x.StartAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<string?, CancellationToken>((root, _) => calls.Add($"start:{root}"));
        return calls;
    }

    // Asserts the ORDER, not merely that both happened: a Start before the Stop would be the
    // no-op that leaves the server rooted at the old project, and "Stop was called once, Start
    // was called once" reads green for it.
    [Test]
    public async Task StartAllAsync_SecondProject_StopsThenRestartsEveryServer_AtTheNewRoot()
    {
        var bl = FakeBasicLang(connected: true);
        var cl = FakeClangd(connected: true);
        var blCalls = RecordLifecycle(bl);
        var clCalls = RecordLifecycle(cl);
        using var r = RegistryOf(bl, cl);

        await r.StartAllAsync(@"C:\projects\A");
        await r.StartAllAsync(@"C:\projects\B");

        Assert.Multiple(() =>
        {
            Assert.That(clCalls, Is.EqualTo(new[] { @"start:C:\projects\A", "stop", @"start:C:\projects\B" }),
                "clangd must be stopped before being restarted at project B — a bare StartAsync no-ops " +
                "on a connected server and leaves it rooted at project A's compile_commands.json");
            Assert.That(blCalls, Is.EqualTo(new[] { @"start:C:\projects\A", "stop", @"start:C:\projects\B" }));
        });
    }

    // The first project must not pay for a teardown that has nothing to tear down.
    [Test]
    public async Task StartAllAsync_FirstProject_StartsWithoutStopping()
    {
        var cl = FakeClangd();
        var calls = RecordLifecycle(cl);
        using var r = RegistryOf(FakeBasicLang(), cl);

        await r.StartAllAsync(@"C:\projects\A");

        Assert.That(calls, Is.EqualTo(new[] { @"start:C:\projects\A" }));
    }

    // Re-opening the SAME project (or a second ProjectOpened for it) must not bounce a healthy
    // server: the restart costs a multi-second handshake and drops every didOpen.
    [Test]
    public async Task StartAllAsync_SameRootTwice_DoesNotRestart()
    {
        var cl = FakeClangd(connected: true);
        var calls = RecordLifecycle(cl);
        using var r = RegistryOf(FakeBasicLang(connected: true), cl);

        await r.StartAllAsync(@"C:\projects\A");
        await r.StartAllAsync(@"C:\projects\A");

        Assert.That(calls, Is.EqualTo(new[] { @"start:C:\projects\A", @"start:C:\projects\A" }),
            "the same root must never trigger a Stop");
    }

    // A server whose Stop throws is NOT silently re-Started: its StartAsync would no-op (it may
    // still be connected) and leave it rooted at the old project while we reported success. The
    // failure is surfaced, and the OTHER servers still re-root.
    [Test]
    public void StartAllAsync_ReRoot_AServerThatCannotStop_IsReportedAndNotLeftMisRooted()
    {
        var bl = FakeBasicLang(connected: true);
        var cl = FakeClangd(connected: true);
        var blCalls = RecordLifecycle(bl);
        cl.Setup(x => x.StopAsync()).ThrowsAsync(new InvalidOperationException("clangd will not stop"));
        using var r = RegistryOf(bl, cl);

        Assert.DoesNotThrowAsync(() => r.StartAllAsync(@"C:\projects\A"));
        Assert.ThrowsAsync<AggregateException>(() => r.StartAllAsync(@"C:\projects\B"));

        cl.Verify(x => x.StartAsync(@"C:\projects\B", It.IsAny<CancellationToken>()), Times.Never,
            "a server that failed to stop is still rooted at project A — restarting it would no-op " +
            "and silently pin it there while we claimed it had moved");
        Assert.That(blCalls, Is.EqualTo(new[] { @"start:C:\projects\A", "stop", @"start:C:\projects\B" }),
            "clangd's failure must not cost BasicLang its re-root");
    }

    /// <summary>
    /// Like <see cref="RecordLifecycle"/>, but the FIRST StopAsync throws (recorded as
    /// "stop:throw"); every later stop succeeds. Models a server that hung on one shutdown and
    /// recovered — the setup both dirty-flag tests below share.
    /// </summary>
    private static List<string> RecordLifecycleWithOneStopFailure(Mock<ILanguageService> mock)
    {
        var calls = new List<string>();
        var stops = 0;
        mock.Setup(x => x.StopAsync()).Returns(() =>
        {
            if (++stops == 1)
            {
                calls.Add("stop:throw");
                return Task.FromException(new InvalidOperationException("clangd hung on shutdown (once)"));
            }

            calls.Add("stop");
            return Task.CompletedTask;
        });
        mock.Setup(x => x.StartAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<string?, CancellationToken>((root, _) => calls.Add($"start:{root}"));
        return calls;
    }

    // The adjudicated Task-12 review defect: StartAllAsync commits _startedRoot BEFORE the
    // stop-then-start loop, so after a failed A→B pass a RETRY of B used to compute
    // reRooting=false, never re-attempt the stop, and report success — while (at the
    // ILanguageService contract level) a still-connected clangd no-ops its StartAsync and keeps
    // answering from project A's compile_commands.json. The _rootIsDirty flag makes any failed
    // pass force a full re-root on the next call, whatever its root.
    [Test]
    public async Task StartAllAsync_RetryAfterFailedStop_ReAttemptsTheStop()
    {
        var bl = FakeBasicLang(connected: true);
        var cl = FakeClangd(connected: true);
        RecordLifecycle(bl);
        var clCalls = RecordLifecycleWithOneStopFailure(cl);
        using var r = RegistryOf(bl, cl);

        await r.StartAllAsync(@"C:\projects\A");
        Assert.ThrowsAsync<AggregateException>(() => r.StartAllAsync(@"C:\projects\B"),
            "the failed pass must still be reported — the fix is about the RETRY, not the failure");

        await r.StartAllAsync(@"C:\projects\B"); // the retry must succeed, not throw

        Assert.That(clCalls, Is.EqualTo(new[]
            { @"start:C:\projects\A", "stop:throw", "stop", @"start:C:\projects\B" }),
            "the retry of project B must re-attempt the stop the failed pass could not complete — " +
            "trusting the _startedRoot=B that half-finished pass wrote computes reRooting=false, " +
            "skips the stop, and a still-connected clangd would no-op its StartAsync and keep " +
            "answering from project A's compile database while the retry reports success");
    }

    // The other direction, which the early commit of _startedRoot exists to keep correct (and the
    // reason a naive set-_startedRoot-only-on-success is NOT the fix): BasicLang genuinely moved
    // to B during the failed pass, so a switch BACK to A must re-root it — a registry still
    // believing "rooted at A" would compute reRooting=false and silently pin it at B. Passes
    // before and after the dirty-flag fix; kept as the regression pin for the direction the fix
    // must not break.
    [Test]
    public async Task StartAllAsync_SwitchBackAfterFailedReRoot_ReRootsTheHealthyServer()
    {
        var bl = FakeBasicLang(connected: true);
        var cl = FakeClangd(connected: true);
        var blCalls = RecordLifecycle(bl);
        var clCalls = RecordLifecycleWithOneStopFailure(cl);
        using var r = RegistryOf(bl, cl);

        await r.StartAllAsync(@"C:\projects\A");
        Assert.ThrowsAsync<AggregateException>(() => r.StartAllAsync(@"C:\projects\B"));

        await r.StartAllAsync(@"C:\projects\A"); // switch BACK after the failed pass

        Assert.Multiple(() =>
        {
            Assert.That(blCalls, Is.EqualTo(new[]
                { @"start:C:\projects\A", "stop", @"start:C:\projects\B", "stop", @"start:C:\projects\A" }),
                "BasicLang really moved to project B during the failed pass; the switch back must " +
                "re-root it to A, not trust bookkeeping that says it never left");
            Assert.That(clCalls, Is.EqualTo(new[]
                { @"start:C:\projects\A", "stop:throw", "stop", @"start:C:\projects\A" }),
                "clangd must get its stop re-attempted and be started at A");
        });
    }

    // ---- Graceful degradation when clangd is not installed ------------------

    // clangd does not ship with the IDE. On a machine without it the registry holds BasicLang
    // alone: C++ files get no server (highlighting still works — that is Phase 1), and nothing
    // throws. BasicLang must be entirely unaffected.
    [Test]
    public void ClangdAbsent_RegistryDegrades_WithoutThrowing_AndBasicLangIsUnaffected()
    {
        var bl = FakeBasicLang(connected: true);
        using var r = RegistryOf(bl);

        Assert.DoesNotThrowAsync(() => r.StartAllAsync(@"C:\projects\A"));
        Assert.Multiple(() =>
        {
            Assert.That(r.GetFor("a.cpp"), Is.Null, "no clangd is registered, so nothing owns .cpp");
            Assert.That(r.IsConnectedFor("a.cpp"), Is.False);
            Assert.That(r.GetById("clangd"), Is.Null);
            Assert.That(r.IsConnectedFor("a.bas"), Is.True, "BasicLang must be untouched by clangd's absence");
        });
        bl.Verify(x => x.StartAsync(@"C:\projects\A", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- DI ----------------------------------------------------------------

    /// <summary>
    /// Task 7 removed the temporary <c>ILanguageService</c> shim: every consumer now routes through
    /// <see cref="ILanguageServiceRegistry"/>, so there must be NO directly-injectable
    /// <c>ILanguageService</c>. Re-adding one would register a second <c>LanguageService</c> — a
    /// second `dotnet --lsp` child process that nothing routes to, starts, stops or disposes, and
    /// nothing else would fail to say so. This fails loudly if someone reintroduces the shim.
    /// </summary>
    [Test]
    public void Di_DoesNotRegisterADirectLanguageServiceShim()
    {
        using var provider = BuildProvider();

        Assert.That(provider.GetService<ILanguageService>(), Is.Null,
            "consumers must route through ILanguageServiceRegistry; a direct ILanguageService " +
            "registration would spawn a second, orphaned server process");
    }

    /// <summary>
    /// The orphan-on-exit fix is <c>App.axaml.cs</c>'s <c>(Services as IDisposable)?.Dispose()</c>,
    /// which only reaches services the CONTAINER owns. A registry that lazily <c>new</c>s services
    /// outside the container is never disposed and re-orphans them on every exit. This walks the
    /// real chain: container → registry → the BasicLang service's own <c>_disposed</c>.
    /// </summary>
    [Test]
    public void Di_DisposingTheContainerDisposesTheServersTheRegistryHolds()
    {
        var provider = BuildProvider();
        var basicLang = provider.GetRequiredService<ILanguageServiceRegistry>().GetFor("x.bas");
        Assert.That(IsDisposed(basicLang!), Is.False, "precondition");

        provider.Dispose();

        Assert.That(IsDisposed(basicLang!), Is.True,
            "the container did not reach the registry's servers — every exit orphans them");
    }

    // BasicLang ships with the IDE, so it is registered unconditionally — on every machine,
    // whatever clangd's fate. This half of the roster can be asserted outright.
    [Test]
    public void Di_AlwaysRegistersBasicLang()
    {
        using var provider = BuildProvider();

        var ids = provider.GetRequiredService<ILanguageServiceRegistry>().All.Select(s => s.Descriptor.Id);

        Assert.That(ids, Does.Contain("basiclang"));
    }

    /// <summary>
    /// clangd is registered EXACTLY when one was found, and never otherwise.
    /// </summary>
    /// <remarks>
    /// Cannot be a fixed expectation: clangd does not ship with the IDE, so the roster genuinely
    /// differs per machine — an unconditional <c>Is.EqualTo(new[] { "basiclang" })</c> would fail
    /// on any dev box with clangd on PATH, and the opposite would fail on every box without it.
    /// So it asserts the RULE — DI's roster agrees with the locator's answer — which holds on both.
    /// <para>
    /// <see cref="BuildProvider"/> swaps in a loose <see cref="ISettingsService"/> mock whose
    /// <c>Get&lt;string&gt;</c> returns null, so there is no <c>cpp.clangd.path</c> override in
    /// play and the locator's answer here is the full auto-probe chain — <c>~/.vgs/tools</c>,
    /// then PATH, then the conventional LLVM install dirs — which is exactly what the container
    /// is resolving through.
    /// </para>
    /// </remarks>
    [Test]
    public void Di_RegistersClangd_ExactlyWhenTheLocatorFindsOne()
    {
        var located = ClangdLocator.ResolveClangdPath(configuredPath: null);
        using var provider = BuildProvider();

        var ids = provider.GetRequiredService<ILanguageServiceRegistry>().All.Select(s => s.Descriptor.Id);

        Assert.That(ids, Is.EqualTo(located == null
            ? new[] { "basiclang" }
            : new[] { "basiclang", "clangd" }),
            located == null
                ? "no clangd on this machine, so the registry must hold BasicLang alone"
                : $"clangd was found at {located}, so it must be registered and route .cpp");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.ConfigureServices();
        // Last registration wins in MS.DI, so this keeps the real ILanguageService/registry wiring
        // under test while stopping the real SettingsService from creating ~/.vgs and a file watcher.
        services.AddSingleton(Mock.Of<ISettingsService>());
        return services.BuildServiceProvider();
    }

    private static bool IsDisposed(ILanguageService service) =>
        (bool)typeof(LanguageService)
            .GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(service)!;

    private sealed class NullOutput : IOutputService
    {
        public void WriteLine(string message, OutputCategory category = OutputCategory.General) { }
        public void Write(string message, OutputCategory category = OutputCategory.General) { }
        public void WriteError(string message, OutputCategory category = OutputCategory.General) { }
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => Array.Empty<string>();
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
