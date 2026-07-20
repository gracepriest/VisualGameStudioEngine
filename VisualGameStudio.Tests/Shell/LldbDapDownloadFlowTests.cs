using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Services;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Pins <see cref="LldbDapDownloadFlow"/> — the policy behind the "Download C++ Debugger" toast
/// action and the Tools-menu command, the structural sibling of
/// <see cref="ClangdDownloadFlowTests"/>. Same honesty rules (never throws, names the failing
/// step, progress never invents a total), plus this flow's two deliberate differences:
///
/// <para>(a) the release-pin gate — until <see cref="LldbDapInstaller.IsReleasePinned"/> flips
/// (the zip is a release-time deliverable, runbook Task 13), the flow reports "not yet published"
/// and never calls the installer; (b) the success toast says press F5, with NO restart language —
/// the lldb-dap locator runs at debug-session start, so a mid-session install is live on the very
/// next F5, unlike clangd's DI-time resolution.</para>
///
/// <para>Every dependency is a seam: the installer call, the "already resolvable?" probe, the
/// three UI sinks, the platform flag, the pin flag, and the progress wrapper. No test performs
/// HTTP, touches <c>~/.vgs</c>, or needs a UI thread.</para>
/// </summary>
[TestFixture]
public class LldbDapDownloadFlowTests
{
    /// <summary>
    /// Synchronous stand-in for the production <c>Progress&lt;T&gt;</c> wrapper — same reason as
    /// <see cref="ClangdDownloadFlowTests"/>: the NUnit host has no SynchronizationContext, so
    /// real <c>Progress&lt;T&gt;</c> callbacks would land on the pool and race assertions.
    /// </summary>
    private sealed class InlineProgress : IProgress<(long bytesDownloaded, long totalBytes)>
    {
        private readonly Action<(long bytesDownloaded, long totalBytes)> _handler;
        public InlineProgress(Action<(long bytesDownloaded, long totalBytes)> handler) => _handler = handler;
        public void Report((long bytesDownloaded, long totalBytes) value) => _handler(value);
    }

    private sealed class Harness
    {
        public readonly List<(string Message, string Severity)> Toasts = new();
        public readonly List<(string Id, string Message, double Fraction)> Progress = new();
        public readonly List<string> Dismissed = new();
        public int InstallCalls;

        public string? ResolvedPath;
        public bool IsWindows = true;

        // True by default so the install-path tests exercise the pipeline; the real pin state is
        // false until the runbook ships, which WhenNotReleasePinned pins explicitly.
        public bool IsReleasePinned = true;

        public Func<IProgress<(long bytesDownloaded, long totalBytes)>?, CancellationToken, Task<LldbDapInstallResult>> Install =
            (_, _) => Task.FromResult(SuccessResult);

        public static LldbDapInstallResult SuccessResult { get; } = new(
            true, @"C:\Users\test\.vgs\tools\lldb-dap_22.1.0\bin\lldb-dap.exe", null, null);

        public LldbDapDownloadFlow CreateFlow() => new(
            (progress, ct) => { InstallCalls++; return Install(progress, ct); },
            () => ResolvedPath,
            (message, severity) => Toasts.Add((message, severity)),
            (id, message, fraction) => Progress.Add((id, message, fraction)),
            id => Dismissed.Add(id),
            isWindows: IsWindows,
            isReleasePinned: IsReleasePinned,
            progressWrapper: handler => new InlineProgress(handler));
    }

    // ---------------------------------------------------------------- pre-flight short-circuits

    [Test]
    public async Task WhenAlreadyResolved_ReportsPathAndDoesNotDownload()
    {
        var h = new Harness { ResolvedPath = @"C:\LLVM\bin\lldb-dap.exe" };

        await h.CreateFlow().RunAsync();

        Assert.That(h.InstallCalls, Is.Zero,
            "an lldb-dap the locator can already resolve must never trigger a re-download");
        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message, Is.EqualTo(@"lldb-dap is already installed at C:\LLVM\bin\lldb-dap.exe"));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"));
    }

    [Test]
    public async Task WhenNotReleasePinned_ReportsNotYetPublished_AndDoesNotDownload()
    {
        // The REAL pin state today: the self-hosted zip is a release-time deliverable, and until
        // the runbook fills the installer's pins the flow must refuse honestly — never fetch the
        // placeholder URL, never hash against "REPLACE-AT-RELEASE-TIME".
        var h = new Harness { IsReleasePinned = false };

        await h.CreateFlow().RunAsync();

        Assert.That(h.InstallCalls, Is.Zero,
            "an unpinned release must never reach the installer — the placeholder URL/hash would " +
            "either 404 or reject every download");
        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message, Is.EqualTo(
            "The C++ debugger download is not published yet — the lldb-dap release zip is a " +
            "release-time deliverable (docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md). " +
            "Until then, point the cpp.lldbDap.path setting at an existing lldb-dap executable."));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"),
            "an unpublished release is a known project state, not a user-facing error");
        Assert.That(h.Progress, Is.Empty, "no transfer starts, so no progress toast may appear");
    }

    // ---------------------------------------------------------------- outcome toasts

    [Test]
    public async Task SuccessfulInstall_SaysPressF5_WithNoRestartLanguage()
    {
        var h = new Harness();

        await h.CreateFlow().RunAsync();

        Assert.That(h.InstallCalls, Is.EqualTo(1));
        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        // Exact wording is the contract — and the deliberate DIFFERENCE from clangd's flow: the
        // lldb-dap locator resolves at debug-SESSION start (DebugAdapterDescriptor.ResolveLaunchCommand),
        // so a mid-session install is live on the very next F5. Restart language here would be
        // wrong in the opposite direction from clangd's: it would demand a restart nobody needs.
        Assert.That(h.Toasts[0].Message, Is.EqualTo("lldb-dap installed — press F5 to debug."));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"));
        Assert.That(h.Toasts[0].Message, Does.Not.Contain("restart").IgnoreCase,
            "no restart language, ever — the next F5 sees the install");
        Assert.That(h.Dismissed, Is.EqualTo(new[] { "lldb-dap-download" }),
            "success must not leave a stuck progress toast behind");
    }

    [Test]
    public async Task FailedInstall_NamesTheFailingStep()
    {
        var detail = "SHA-256 AAAABBBBCCCC… does not match the expected DDDDEEEEFFFF…";
        var h = new Harness
        {
            Install = (_, _) => Task.FromResult(
                new LldbDapInstallResult(false, null, LldbDapInstaller.StepVerifySha256, detail))
        };

        await h.CreateFlow().RunAsync();

        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message,
            Is.EqualTo($"lldb-dap download failed SHA-256 verification: {detail}"),
            "the toast must name the failing step honestly and carry the installer's detail");
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("error"));
        Assert.That(h.Dismissed, Is.EqualTo(new[] { "lldb-dap-download" }),
            "failure must not leave a stuck progress toast behind");
    }

    // ---------------------------------------------------------------- progress mapping

    [Test]
    public async Task ProgressTuples_BecomeFractions()
    {
        var h = new Harness();
        h.Install = (progress, _) =>
        {
            // Raw installer tuples: (bytesDownloaded, totalBytes-or-minus-one).
            progress!.Report((14_099_389, 28_198_778));
            progress.Report((14_099_389, -1));
            return Task.FromResult(Harness.SuccessResult);
        };

        await h.CreateFlow().RunAsync();

        Assert.That(h.Progress, Has.Count.EqualTo(2));

        Assert.That(h.Progress[0].Id, Is.EqualTo("lldb-dap-download"));
        Assert.That(h.Progress[0].Fraction, Is.EqualTo(0.5).Within(0.01));
        Assert.That(h.Progress[0].Message, Is.EqualTo("Downloading lldb-dap… 13.4 MB of 26.9 MB"));

        // An unknown total must stay honest: indeterminate bar, bytes-only message — the flow
        // must NOT substitute ExpectedSizeBytes for a total the transfer never reported.
        Assert.That(h.Progress[1].Id, Is.EqualTo("lldb-dap-download"));
        Assert.That(h.Progress[1].Fraction, Is.EqualTo(-1));
        Assert.That(h.Progress[1].Message, Is.EqualTo("Downloading lldb-dap… 13.4 MB"));
    }

    // ---------------------------------------------------------------- single-flight

    [Test]
    public async Task ConcurrentTrigger_ReportsAlreadyInProgress()
    {
        var h = new Harness
        {
            Install = (_, _) => Task.FromResult(new LldbDapInstallResult(
                false, null, LldbDapInstaller.StepAlreadyInProgress, "an lldb-dap install is already running"))
        };

        await h.CreateFlow().RunAsync();

        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message, Is.EqualTo("An lldb-dap download is already running."));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"),
            "a second click while the first download runs is normal impatience, not an error");
        Assert.That(h.Dismissed, Is.Empty,
            "the already-in-progress reply belongs to the FIRST flow's still-running transfer " +
            "— the progress toast is that flow's, and dismissing it here would kill a live bar");
    }
}
