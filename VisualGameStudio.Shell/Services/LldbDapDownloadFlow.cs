using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// The one-click "Download C++ Debugger" policy: pre-flight checks (already installed? release
/// pinned? supported platform?), the <see cref="LldbDapInstaller"/> call with a live progress
/// toast, and the outcome toast. The structural sibling of <see cref="ClangdDownloadFlow"/>, with
/// two deliberate differences:
///
/// <para><b>The release-pin gate.</b> The self-hosted lldb-dap zip is a release-time deliverable
/// (docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md, Task 13); until the
/// runbook fills <see cref="LldbDapInstaller"/>'s pins, <see cref="LldbDapInstaller.IsReleasePinned"/>
/// is false and this flow reports "not yet published" instead of fetching a placeholder URL. The
/// gate sits BEFORE the platform check: pre-release, "nothing is published" is the true state on
/// every platform, whereas "not supported on this platform" is a claim about an asset that does
/// not exist yet.</para>
///
/// <para><b>No restart language — "available on the next F5".</b> clangd's success toast is
/// restart-bound because DI resolves its path once at startup; lldb-dap's launch command resolves
/// at debug-SESSION start (<c>DebugAdapterDescriptor.ResolveLaunchCommand</c> runs the locator on
/// every F5), so a mid-session install is live immediately. The spec's "download → F5 resumes" is
/// DELIBERATELY implemented as "available on the next F5" — the 3b pattern; no restart is needed,
/// and no mid-flow F5 resumption is attempted (the user presses F5 again when the toast says so).</para>
///
/// <para><b>Deliberately DI-free</b> and <b>never throws</b>, exactly like the clangd flow: its
/// sinks ARE <c>MainWindowViewModel</c>'s toast/progress methods (the VM constructs it with
/// lambdas over its own members), and both entry points launch it as <c>_ = flow.RunAsync()</c>
/// from synchronous callbacks, so the catch-all here is the last line of defense.</para>
/// </summary>
public sealed class LldbDapDownloadFlow
{
    /// <summary>Progress-toast id: every update targets this id so the toast updates in place.</summary>
    public const string ProgressNotificationId = "lldb-dap-download";

    private readonly Func<IProgress<(long bytesDownloaded, long totalBytes)>?, CancellationToken, Task<LldbDapInstallResult>> _install;
    private readonly Func<string?> _resolveExisting;
    private readonly Action<string, string> _showToast;
    private readonly Action<string, string, double> _showProgress;
    private readonly Action<string> _dismissProgress;
    private readonly bool _isWindows;
    private readonly bool _isReleasePinned;
    private readonly Func<Action<(long bytesDownloaded, long totalBytes)>, IProgress<(long bytesDownloaded, long totalBytes)>> _progressWrapper;

    /// <param name="install">
    /// The installer seam — production binds <see cref="LldbDapInstaller.InstallAsync"/> as a
    /// method group. A delegate rather than the class so tests script outcomes without touching disk.
    /// </param>
    /// <param name="resolveExisting">
    /// "Is lldb-dap already resolvable on disk?" — production binds <c>LldbDapLocator.Locate</c>,
    /// which re-scans the whole chain (override → tools root → PATH → known dirs) on every call,
    /// so the answer is current even for a copy installed minutes ago.
    /// </param>
    /// <param name="showToast">Toast sink: (message, severity).</param>
    /// <param name="showProgress">Progress-toast sink: (id, message, fraction 0..1 or -1 = indeterminate).</param>
    /// <param name="dismissProgress">Dismisses the progress toast by id.</param>
    /// <param name="isWindows">Platform gate; null means the real OS. The pinned release asset is Windows-only.</param>
    /// <param name="isReleasePinned">
    /// The release-pin gate; null means <see cref="LldbDapInstaller.IsReleasePinned"/>. A seam
    /// because the REAL value is false until the runbook ships — tests could not reach the
    /// install path at all without injecting true.
    /// </param>
    /// <param name="progressWrapper">
    /// How the raw tuple handler becomes the <see cref="IProgress{T}"/> handed to the installer.
    /// Null means <see cref="Progress{T}"/> — REQUIRED in production: the installer invokes
    /// <c>Report</c> INLINE on its transfer loop (a pool thread), and <see cref="Progress{T}"/>
    /// posts each report to the SynchronizationContext captured at construction (the UI thread).
    /// Tests inject a synchronous wrapper — the test host has no SynchronizationContext.
    /// </param>
    public LldbDapDownloadFlow(
        Func<IProgress<(long bytesDownloaded, long totalBytes)>?, CancellationToken, Task<LldbDapInstallResult>> install,
        Func<string?> resolveExisting,
        Action<string, string> showToast,
        Action<string, string, double> showProgress,
        Action<string> dismissProgress,
        bool? isWindows = null,
        bool? isReleasePinned = null,
        Func<Action<(long bytesDownloaded, long totalBytes)>, IProgress<(long bytesDownloaded, long totalBytes)>>? progressWrapper = null)
    {
        _install = install;
        _resolveExisting = resolveExisting;
        _showToast = showToast;
        _showProgress = showProgress;
        _dismissProgress = dismissProgress;
        _isWindows = isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _isReleasePinned = isReleasePinned ?? LldbDapInstaller.IsReleasePinned;
        _progressWrapper = progressWrapper
            ?? (handler => new Progress<(long bytesDownloaded, long totalBytes)>(handler));
    }

    /// <summary>
    /// Runs the whole flow: short-circuit if lldb-dap is already resolvable, the release is not
    /// pinned yet, or the platform is unsupported; otherwise install with a live progress toast
    /// and report the outcome. Safe to fire-and-forget — see the class remarks. No
    /// ConfigureAwait(false), deliberately: the continuations call the UI sinks, and resuming on
    /// the captured (UI) context is what makes those calls thread-safe.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            var existing = _resolveExisting();
            if (existing != null)
            {
                _showToast($"lldb-dap is already installed at {existing}", "info");
                return;
            }

            if (!_isReleasePinned)
            {
                _showToast(
                    "The C++ debugger download is not published yet — the lldb-dap release zip is a " +
                    "release-time deliverable (docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md). " +
                    $"Until then, point the {DebugAdapterDescriptor.LldbDapSettingsKey} setting at an " +
                    "existing lldb-dap executable.",
                    "info");
                return;
            }

            if (!_isWindows)
            {
                _showToast("Downloading the C++ debugger is not supported on this platform yet.", "info");
                return;
            }

            var progress = _progressWrapper(ReportProgress);
            LldbDapInstallResult? result = null;
            try
            {
                result = await _install(progress, ct);
            }
            finally
            {
                // Success, failure, or a thrown contract violation — the progress toast must
                // never outlive the transfer it reports on. EXCEPT already-in-progress: that
                // reply means ANOTHER flow's transfer owns the toast (both flows share the
                // one ProgressNotificationId), and dismissing here would kill its live bar.
                if (result?.FailureStep != LldbDapInstaller.StepAlreadyInProgress)
                {
                    _dismissProgress(ProgressNotificationId);
                }
            }

            if (result.Success)
            {
                // NO restart language — see the class remarks: the locator runs at session
                // start, so the very next F5 sees this install.
                _showToast("lldb-dap installed — press F5 to debug.", "info");
            }
            else
            {
                var (message, severity) = ComposeFailureToast(result.FailureStep, result.FailureDetail);
                _showToast(message, severity);
            }
        }
        catch (OperationCanceledException)
        {
            // The installer's one sanctioned throw: deliberate caller cancellation. Not an error.
            _showToast("lldb-dap download cancelled.", "info");
        }
        catch (Exception ex)
        {
            // Contract violation somewhere below — surfaced, never swallowed silently and never
            // rethrown into the void of a fire-and-forget task.
            _showToast($"lldb-dap download failed unexpectedly: {ex.Message}", "error");
        }
    }

    /// <summary>Raw installer tuple → progress-toast update, via <see cref="FormatProgress"/>.</summary>
    private void ReportProgress((long bytesDownloaded, long totalBytes) raw)
    {
        var (fraction, message) = FormatProgress(raw.bytesDownloaded, raw.totalBytes);
        _showProgress(ProgressNotificationId, message, fraction);
    }

    /// <summary>
    /// Maps a raw (bytes, total-or-minus-one) tuple to the progress toast's (fraction, message).
    /// A known total gives a clamped 0..1 fraction and "13.4 MB of 26.9 MB"; an unknown total
    /// stays honest — indeterminate (-1) with a bytes-only message, never a denominator invented
    /// from <see cref="LldbDapInstaller.ExpectedSizeBytes"/> that the transfer didn't report.
    /// Public and pure so the mapping is pinnable without running a flow.
    /// </summary>
    public static (double fraction, string message) FormatProgress(long bytesDownloaded, long totalBytes)
    {
        if (totalBytes > 0)
        {
            var fraction = Math.Clamp((double)bytesDownloaded / totalBytes, 0.0, 1.0);
            return (fraction,
                $"Downloading lldb-dap… {FormatMegabytes(bytesDownloaded)} of {FormatMegabytes(totalBytes)}");
        }

        return (-1, $"Downloading lldb-dap… {FormatMegabytes(bytesDownloaded)}");
    }

    private static string FormatMegabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>
    /// The failure-step switch: every member of the installer's closed FailureStep vocabulary gets
    /// a human sentence, with the installer's detail appended verbatim. Already-in-progress is the
    /// one non-error: a second click while the first download runs deserves an info toast, not red.
    /// The default arm is defensive — the vocabulary is closed today, but a step added to the
    /// installer without a sentence here must still surface honestly rather than crash or vanish.
    /// </summary>
    private static (string message, string severity) ComposeFailureToast(string? step, string? detail)
    {
        if (step == LldbDapInstaller.StepAlreadyInProgress)
        {
            return ("An lldb-dap download is already running.", "info");
        }

        var sentence = step switch
        {
            LldbDapInstaller.StepDownload => "lldb-dap download failed",
            LldbDapInstaller.StepDownloadTimeout => "lldb-dap download timed out",
            LldbDapInstaller.StepVerifySize => "lldb-dap download failed size verification",
            LldbDapInstaller.StepVerifySha256 => "lldb-dap download failed SHA-256 verification",
            LldbDapInstaller.StepExtract => "lldb-dap archive could not be extracted",
            LldbDapInstaller.StepLayout => "lldb-dap archive had an unexpected layout",
            LldbDapInstaller.StepInstall => "lldb-dap could not be moved into the tools directory",
            _ => $"lldb-dap install failed ({step ?? "unknown step"})"
        };
        return ($"{sentence}: {detail}", "error");
    }
}
