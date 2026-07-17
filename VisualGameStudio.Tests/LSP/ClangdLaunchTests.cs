using System.Diagnostics;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Starts a REAL clangd and pins that the launch path this phase built actually works: the
/// process comes up, the handshake completes, and the position encoding negotiates to utf-16.
///
/// <para>
/// <b>Launch-level only.</b> Whether clangd can then ANSWER (didOpen → completion/hover) is
/// Task 13's end-to-end test. What is pinned here is everything between "clangd is installed"
/// and "the client is connected" — the layer Task 12 owns.
/// </para>
///
/// <para>
/// <b>Skips when clangd is not installed</b>, mirroring the toolchain-conditional native tests.
/// clangd does not ship with the IDE and is absent from most dev machines, so an unconditional
/// fixture here would fail for everyone. It resolves through <see cref="ClangdLocator"/> the same
/// way the IDE does — a configured-path override first, then PATH — so either putting clangd on
/// PATH or having one under <c>~\.vgs\tools\clangd*\bin</c> (the per-user tools directory this
/// suite's dev machines use, and where Phase 3b will download to) makes these run.
/// </para>
/// </summary>
[TestFixture]
[Category("RequiresClangd")]
public class ClangdLaunchTests
{
    private string _projectDir = "";

    /// <summary>
    /// The clangd the IDE would use, or null — the skip condition for every test here.
    /// <para>
    /// The <c>~\.vgs\tools</c> probe is passed as <c>configuredPath</c>, which ALSO exercises
    /// Task 11's <c>cpp.clangd.path</c> override branch against a real executable — on a machine
    /// with clangd deliberately kept off PATH, the override is the only route these tests have,
    /// exactly as it is the only route such a user's IDE has.
    /// </para>
    /// </summary>
    private static string? LocateClangd() =>
        ClangdLocator.ResolveClangdPath(configuredPath: ProbeVgsToolsDir());

    /// <summary>
    /// A <c>clangd.exe</c> under <c>%USERPROFILE%\.vgs\tools\clangd*\bin</c> (the highest-sorting
    /// directory name wins — any working clangd will do here), or null. Test-only: the production
    /// locator deliberately knows nothing about this directory in Phase 3a (acquisition is
    /// Phase 3b); a user with clangd there points cpp.clangd.path at it.
    /// </summary>
    private static string? ProbeVgsToolsDir()
    {
        try
        {
            var toolsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vgs", "tools");
            if (!Directory.Exists(toolsDir)) return null;

            return Directory.GetDirectories(toolsDir, "clangd*")
                .OrderByDescending(dir => dir, StringComparer.OrdinalIgnoreCase)
                .Select(dir => Path.Combine(dir, "bin", "clangd.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null; // An unreadable profile dir must degrade to the PATH probe, not fail the fixture.
        }
    }

    [SetUp]
    public void SetUp()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bl-clangd-launch-" + Guid.NewGuid().ToString("N"));
        // clangd is pointed at <projectDir>/obj by --compile-commands-dir. The directory must
        // exist for the launch to be representative; an empty database is enough to start.
        Directory.CreateDirectory(Path.Combine(_projectDir, "obj"));
        File.WriteAllText(Path.Combine(_projectDir, "obj", "compile_commands.json"), "[]");
    }

    [TearDown]
    public void TearDown()
    {
        // One bounded retry: the just-stopped clangd can briefly hold this directory (its cwd /
        // --compile-commands-dir) open, making the first delete fail and silently leaking
        // %TEMP%\bl-clangd-launch-* dirs. Still best-effort — the final failure is swallowed.
        try { Directory.Delete(_projectDir, recursive: true); }
        catch
        {
            Thread.Sleep(250);
            try { Directory.Delete(_projectDir, recursive: true); } catch { }
        }
    }

    private static void RequireClangd(string? clangdPath)
    {
        if (clangdPath == null)
        {
            Assert.Ignore(
                "clangd is not installed on this machine (not on PATH, no cpp.clangd.path override). " +
                "This test exercises a real clangd process; install clangd or put it on PATH to run it.");
        }
    }

    /// <summary>
    /// THE TASK, PROVEN AGAINST THE REAL THING: clangd starts and reaches a connected state.
    /// Everything else in Phase 3a is unit-level and would pass just as happily against a clangd
    /// that never came up.
    /// </summary>
    [Test]
    public async Task RealClangd_StartsAndConnects_RootedAtTheProject()
    {
        var clangdPath = LocateClangd();
        RequireClangd(clangdPath);

        using var service = new LanguageService(
            new NullOutput(), null, LanguageServerDescriptor.Clangd(clangdPath!));

        await service.StartAsync(_projectDir);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(service.IsConnected, Is.True, "clangd did not complete the initialize handshake");
                Assert.That(service.ServerProcessId, Is.Not.Null, "no clangd process was ever started");
            });
        }
        finally
        {
            await service.StopAsync();
        }
    }

    /// <summary>
    /// THE ENCODING, VERIFIED RATHER THAN ASSUMED. This client converts positions as
    /// <c>character = column - 1</c> against AvaloniaEdit's 1-based UTF-16 <c>Caret.Column</c> at
    /// 12+ call sites, so utf-16 is the only encoding it can read — and <b>clangd's own default is
    /// utf-8</b>. The only reason it picks utf-16 is the <c>general.positionEncodings</c> pin.
    /// </summary>
    /// <remarks>
    /// Asserts BOTH fields real clangd sends: the standard <c>capabilities.positionEncoding</c> and
    /// clangd's own top-level <c>offsetEncoding</c>.
    /// <para>
    /// ⚠ This test is <b>not</b> vacuous, and that was verified rather than assumed: adding
    /// <c>--offset-encoding=utf-8</c> to the launch arguments makes real clangd answer utf-8 in
    /// BOTH fields and this fixture fails (the client refuses the handshake outright, so
    /// <c>IsConnected</c> goes false). The flag genuinely beats the
    /// <c>general.positionEncodings</c> pin — the hazard is real, not theoretical.
    /// </para>
    /// </remarks>
    [Test]
    public async Task RealClangd_NegotiatesUtf16_InBothTheStandardAndClangdSpecificFields()
    {
        var clangdPath = LocateClangd();
        RequireClangd(clangdPath);

        using var service = new LanguageService(
            new NullOutput(), null, LanguageServerDescriptor.Clangd(clangdPath!));

        await service.StartAsync(_projectDir);

        try
        {
            Assert.That(service.IsConnected, Is.True, "precondition: clangd must be connected to report capabilities");

            var caps = service.Capabilities;
            Assert.That(caps, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(caps!.PositionEncoding, Is.EqualTo("utf-16"),
                    "clangd defaults to utf-8 — if this is not utf-16, the general.positionEncodings " +
                    "pin has been weakened and every position on every non-ASCII line is now wrong");
                Assert.That(caps.OffsetEncoding, Is.EqualTo("utf-16"),
                    "clangd's own offsetEncoding must agree; a stray --offset-encoding flag would " +
                    "override the negotiation the pin won and show up here first");
                Assert.That(LanguageService.DescribeEncodingMismatch(caps), Is.Null);
            });
        }
        finally
        {
            await service.StopAsync();
        }
    }

    /// <summary>
    /// CONCURRENT STARTS SPAWN ONE PROCESS, NOT TWO. The shell fires StartAsync from two
    /// independent fire-and-forget paths (the constructor's autostart and ProjectOpened's
    /// StartAllAsync — a project auto-opened at launch fires the second inside the first's
    /// handshake). Without the start gate, both pass the IsConnected check and both spawn: the
    /// second overwrites _serverProcess, so the first clangd is ORPHANED past Stop/Dispose —
    /// exactly the leaked-server bug class this phase exists to kill.
    /// </summary>
    /// <remarks>
    /// Detected via the real observable harm: after Stop, no clangd started by this test may
    /// still be alive. A PID-count assertion on the happy path would be weaker — the orphan IS
    /// the failure, so the orphan is what is asserted. Mutation-checked: removing the
    /// <c>_startGate</c> serialization makes this fail with one surviving process.
    /// </remarks>
    [Test]
    public async Task RealClangd_TwoConcurrentStarts_SpawnOneProcess_AndStopLeavesNoOrphan()
    {
        var clangdPath = LocateClangd();
        RequireClangd(clangdPath);

        var preExisting = ClangdPids();

        using var service = new LanguageService(
            new NullOutput(), null, LanguageServerDescriptor.Clangd(clangdPath!));

        // Same shape as the shell: two unawaited starts racing each other.
        var first = service.StartAsync(_projectDir);
        var second = service.StartAsync(_projectDir);
        await Task.WhenAll(first, second);

        try
        {
            Assert.That(service.IsConnected, Is.True, "precondition: at least one start must have succeeded");
        }
        finally
        {
            await service.StopAsync();
        }

        // Give the stopped process a moment to actually exit before sweeping for survivors.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        List<int> orphans;
        while ((orphans = ClangdPids().Except(preExisting).ToList()).Count > 0 &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        // Hygiene first, verdict second: a failing run must not leave clangd processes behind.
        foreach (var pid in orphans)
        {
            try { Process.GetProcessById(pid).Kill(); } catch { }
        }

        Assert.That(orphans, Is.Empty,
            "a clangd outlived StopAsync — a second concurrent start spawned a process the " +
            "service lost track of and can never stop");
    }

    /// <summary>PIDs of every clangd currently alive (the survivor sweep's unit of account).</summary>
    /// <remarks>
    /// Sweeps globally by process name, so a clangd started by something ELSE on this machine
    /// after the preExisting snapshot (say, VS Code opening mid-run) would be counted — and
    /// killed — as an orphan. Accepted edge: impossible in-suite (NUnit runs this fixture's
    /// tests sequentially in one process) and improbable in the seconds-wide window. If it ever
    /// flakes, filter by command line containing the test's temp project dir instead of by name.
    /// </remarks>
    private static List<int> ClangdPids()
    {
        var pids = new List<int>();
        foreach (var process in Process.GetProcessesByName("clangd"))
        {
            using (process)
            {
                pids.Add(process.Id);
            }
        }
        return pids;
    }

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
