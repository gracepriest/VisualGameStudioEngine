using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.Services;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Pins <see cref="ClangdDownloadFlow"/> — the policy behind the "Download C++ tools" toast action
/// and the Tools-menu command. The flow is the LAST line of defense on a fire-and-forget path
/// (<c>_ = flow.RunAsync()</c> from a sync toast callback), so beyond the happy path the fixture
/// pins the honesty rules: it never throws, it names the failing step from the installer's closed
/// FailureStep vocabulary, and its progress messages never invent a total the transfer didn't report.
///
/// <para>Every dependency is a seam: the installer call, the "already resolvable?" probe, the three
/// UI sinks, the platform flag, and the progress wrapper. No test performs HTTP, touches
/// <c>~/.vgs</c>, or needs a UI thread. The exact toast strings asserted here are the contract the
/// Task 14 smoke script quotes.</para>
/// </summary>
[TestFixture]
public class ClangdDownloadFlowTests
{
    /// <summary>
    /// Synchronous stand-in for the production <c>Progress&lt;T&gt;</c> wrapper. Production wraps the
    /// raw tuple handler in <c>Progress&lt;T&gt;</c>, which posts each report to the captured
    /// SynchronizationContext; the NUnit host has none, so those callbacks would land on the thread
    /// pool and race every assertion. Injecting this through the flow's <c>progressWrapper</c> seam
    /// runs each report inline on the reporting thread, making the sink deterministic to assert.
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
        public Func<IProgress<(long bytesDownloaded, long totalBytes)>?, CancellationToken, Task<ClangdInstallResult>> Install =
            (_, _) => Task.FromResult(SuccessResult);

        public static ClangdInstallResult SuccessResult { get; } = new(
            true, @"C:\Users\test\.vgs\tools\clangd_22.1.6\bin\clangd.exe", null, null);

        public ClangdDownloadFlow CreateFlow() => new(
            (progress, ct) => { InstallCalls++; return Install(progress, ct); },
            () => ResolvedPath,
            (message, severity) => Toasts.Add((message, severity)),
            (id, message, fraction) => Progress.Add((id, message, fraction)),
            id => Dismissed.Add(id),
            isWindows: IsWindows,
            progressWrapper: handler => new InlineProgress(handler));
    }

    // ---------------------------------------------------------------- pre-flight short-circuits

    [Test]
    public async Task WhenClangdAlreadyResolved_ReportsPathAndDoesNotInstall()
    {
        var h = new Harness { ResolvedPath = @"C:\LLVM\bin\clangd.exe" };

        await h.CreateFlow().RunAsync();

        Assert.That(h.InstallCalls, Is.Zero,
            "a clangd the locator can already resolve must never trigger a 28MB re-download");
        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message, Is.EqualTo(@"clangd is already installed at C:\LLVM\bin\clangd.exe"));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"));
    }

    [Test]
    public async Task NonWindowsPlatform_ReportsNotSupportedAndDoesNotInstall()
    {
        var h = new Harness { IsWindows = false };

        await h.CreateFlow().RunAsync();

        Assert.That(h.InstallCalls, Is.Zero,
            "the pinned release asset is Windows-only; other platforms must not download it");
        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message,
            Is.EqualTo("Downloading C++ tools is not supported on this platform yet."));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"));
    }

    // ---------------------------------------------------------------- outcome toasts

    [Test]
    public async Task SuccessfulInstall_ShowsRestartPromptWording()
    {
        var h = new Harness();

        await h.CreateFlow().RunAsync();

        Assert.That(h.InstallCalls, Is.EqualTo(1));
        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        // Exact wording is the contract: a mid-session install is visible to the locator but NOT
        // to DI registration, so the success toast must bind the benefit to a restart — anything
        // promising live IntelliSense would be a lie.
        Assert.That(h.Toasts[0].Message,
            Is.EqualTo("clangd installed — restart the IDE to enable C++ IntelliSense."));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"));
    }

    [Test]
    public async Task FailedInstall_NamesTheStepAndDetail()
    {
        var detail = "SHA-256 AAAABBBBCCCC… does not match the expected DDDDEEEEFFFF…";
        var h = new Harness
        {
            Install = (_, _) => Task.FromResult(
                new ClangdInstallResult(false, null, ClangdInstaller.StepVerifySha256, detail))
        };

        await h.CreateFlow().RunAsync();

        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message,
            Is.EqualTo($"clangd download failed SHA-256 verification: {detail}"),
            "the toast must name the failing step honestly and carry the installer's detail");
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("error"));
    }

    [Test]
    public async Task DownloadTimeout_SaysTimedOut()
    {
        var h = new Harness
        {
            Install = (_, _) => Task.FromResult(new ClangdInstallResult(
                false, null, ClangdInstaller.StepDownloadTimeout, "the download did not finish within 10 minutes"))
        };

        await h.CreateFlow().RunAsync();

        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message,
            Is.EqualTo("clangd download timed out: the download did not finish within 10 minutes"));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("error"));
    }

    [Test]
    public async Task AlreadyInProgress_IsInfoNotError()
    {
        var h = new Harness
        {
            Install = (_, _) => Task.FromResult(new ClangdInstallResult(
                false, null, ClangdInstaller.StepAlreadyInProgress, "a clangd install is already running"))
        };

        await h.CreateFlow().RunAsync();

        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message, Is.EqualTo("A clangd download is already running."));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"),
            "a second click while the first download runs is normal impatience, not an error");
    }

    // ---------------------------------------------------------------- progress mapping

    [Test]
    public async Task ProgressTuples_BecomeFractionsAndMessages()
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

        Assert.That(h.Progress[0].Id, Is.EqualTo("clangd-download"));
        Assert.That(h.Progress[0].Fraction, Is.EqualTo(0.5).Within(0.01));
        Assert.That(h.Progress[0].Message, Is.EqualTo("Downloading clangd… 13.4 MB of 26.9 MB"));

        // An unknown total must stay honest: indeterminate bar, bytes-only message — the flow
        // must NOT substitute ExpectedSizeBytes for a total the transfer never reported.
        Assert.That(h.Progress[1].Id, Is.EqualTo("clangd-download"));
        Assert.That(h.Progress[1].Fraction, Is.EqualTo(-1));
        Assert.That(h.Progress[1].Message, Is.EqualTo("Downloading clangd… 13.4 MB"));
    }

    [Test]
    public async Task ProgressToast_DismissedOnBothOutcomes()
    {
        var success = new Harness();
        await success.CreateFlow().RunAsync();
        Assert.That(success.Dismissed, Is.EqualTo(new[] { "clangd-download" }),
            "success must not leave a stuck progress toast behind");

        var failure = new Harness
        {
            Install = (_, _) => Task.FromResult(new ClangdInstallResult(
                false, null, ClangdInstaller.StepDownload, "connection reset"))
        };
        await failure.CreateFlow().RunAsync();
        Assert.That(failure.Dismissed, Is.EqualTo(new[] { "clangd-download" }),
            "failure must not leave a stuck progress toast behind");
    }

    // ---------------------------------------------------------------- last line of defense

    [Test]
    public void FlowNeverThrows_InstallerExceptionBecomesErrorToast()
    {
        // The installer contract says it never throws (except caller-OCE) — but the flow runs
        // fire-and-forget from a sync toast callback, so a contract violation must still be
        // caught here rather than becoming an unobserved task exception.
        var h = new Harness
        {
            Install = (_, _) => throw new InvalidOperationException("boom")
        };

        Assert.DoesNotThrowAsync(() => h.CreateFlow().RunAsync());

        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message, Does.Contain("boom"));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("error"));
        Assert.That(h.Dismissed, Is.EqualTo(new[] { "clangd-download" }),
            "even a throwing installer must not leave a stuck progress toast");
    }

    [Test]
    public async Task CallerCancellation_BecomesCancelledInfoToast()
    {
        var h = new Harness
        {
            Install = (_, _) => Task.FromException<ClangdInstallResult>(new OperationCanceledException())
        };

        await h.CreateFlow().RunAsync();

        Assert.That(h.Toasts, Has.Count.EqualTo(1));
        Assert.That(h.Toasts[0].Message, Is.EqualTo("clangd download cancelled."));
        Assert.That(h.Toasts[0].Severity, Is.EqualTo("info"),
            "a deliberate cancellation is not an error and must not read like one");
        Assert.That(h.Dismissed, Is.EqualTo(new[] { "clangd-download" }));
    }
}
