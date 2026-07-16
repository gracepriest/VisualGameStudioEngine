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
    // basiclang" trap. This is the state the IDE actually ships in until Task 12 registers clangd.
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

    // Only BasicLang until Task 11 resolves a clangd path for Task 12 to register.
    [Test]
    public void Di_RegistersBasicLangOnly()
    {
        using var provider = BuildProvider();

        var ids = provider.GetRequiredService<ILanguageServiceRegistry>().All.Select(s => s.Descriptor.Id);

        Assert.That(ids, Is.EqualTo(new[] { "basiclang" }));
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
