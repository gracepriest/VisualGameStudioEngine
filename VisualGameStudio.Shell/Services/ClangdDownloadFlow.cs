using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Shell.Services;

/// <summary>
/// The one-click "Download C++ tools" policy: pre-flight checks (already installed? supported
/// platform?), the <see cref="ClangdInstaller"/> call with a live progress toast, and the outcome
/// toast — including the restart-bound success wording, because a mid-session install is visible
/// to the locator immediately but NOT to DI registration until the IDE restarts.
///
/// <para><b>Deliberately DI-free.</b> Its sinks ARE <c>MainWindowViewModel</c>'s toast/progress
/// methods, and the VM is a DI singleton no service can take a dependency on without a cycle — so
/// the VM constructs this flow itself with lambdas over its own methods (3 lines of wiring), and
/// every dependency here is a plain delegate the test fixture can fake. Only
/// <see cref="ClangdInstaller"/> lives in DI (it owns a disposable downloader).</para>
///
/// <para><b>Never throws.</b> Both entry points (the missing-clangd toast action and the Tools
/// menu command) launch it as <c>_ = flow.RunAsync()</c> from synchronous callbacks — anything
/// escaping <see cref="RunAsync"/> would become an unobserved task exception and vanish. So the
/// catch-all here is the last line of defense: unexpected exceptions become an error toast,
/// caller cancellation becomes a "cancelled" info toast.</para>
/// </summary>
public sealed class ClangdDownloadFlow
{
    /// <summary>Progress-toast id: every update targets this id so the toast updates in place.</summary>
    public const string ProgressNotificationId = "clangd-download";

    private readonly Func<IProgress<(long bytesDownloaded, long totalBytes)>?, CancellationToken, Task<ClangdInstallResult>> _install;
    private readonly Func<string?> _resolveExisting;
    private readonly Action<string, string> _showToast;
    private readonly Action<string, string, double> _showProgress;
    private readonly Action<string> _dismissProgress;
    private readonly bool _isWindows;
    private readonly Func<Action<(long bytesDownloaded, long totalBytes)>, IProgress<(long bytesDownloaded, long totalBytes)>> _progressWrapper;

    /// <param name="install">
    /// The installer seam — production binds <see cref="ClangdInstaller.InstallAsync"/> as a method
    /// group. A delegate rather than the class so tests script outcomes without touching disk.
    /// </param>
    /// <param name="resolveExisting">
    /// "Is clangd already resolvable on disk?" — production binds <c>ClangdLocator.Locate</c>,
    /// which re-scans the whole chain (override → tools root → PATH → LLVM dirs) on every call, so
    /// the answer is current even for a copy installed minutes ago. The locator, not the registry:
    /// the registry only knows what DI resolved at startup, and offering a 28MB download the disk
    /// already holds would be wrong.
    /// </param>
    /// <param name="showToast">Toast sink: (message, severity).</param>
    /// <param name="showProgress">Progress-toast sink: (id, message, fraction 0..1 or -1 = indeterminate).</param>
    /// <param name="dismissProgress">Dismisses the progress toast by id.</param>
    /// <param name="isWindows">Platform gate; null means the real OS. The pinned release asset is Windows-only.</param>
    /// <param name="progressWrapper">
    /// How the raw tuple handler becomes the <see cref="IProgress{T}"/> handed to the installer.
    /// Null means <see cref="Progress{T}"/> — REQUIRED in production: the installer invokes
    /// <c>Report</c> INLINE on its transfer loop (a pool thread), and <see cref="Progress{T}"/>
    /// posts each report to the SynchronizationContext captured at construction. The flow is
    /// constructed and run from the UI thread, so reports marshal to the UI. Tests inject a
    /// synchronous wrapper instead — the test host has no SynchronizationContext, so
    /// <see cref="Progress{T}"/> callbacks there would land on the pool and race assertions.
    /// </param>
    public ClangdDownloadFlow(
        Func<IProgress<(long bytesDownloaded, long totalBytes)>?, CancellationToken, Task<ClangdInstallResult>> install,
        Func<string?> resolveExisting,
        Action<string, string> showToast,
        Action<string, string, double> showProgress,
        Action<string> dismissProgress,
        bool? isWindows = null,
        Func<Action<(long bytesDownloaded, long totalBytes)>, IProgress<(long bytesDownloaded, long totalBytes)>>? progressWrapper = null)
    {
        _install = install;
        _resolveExisting = resolveExisting;
        _showToast = showToast;
        _showProgress = showProgress;
        _dismissProgress = dismissProgress;
        _isWindows = isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _progressWrapper = progressWrapper
            ?? (handler => new Progress<(long bytesDownloaded, long totalBytes)>(handler));
    }

    /// <summary>
    /// Runs the whole flow: short-circuit if clangd is already resolvable or the platform is
    /// unsupported, otherwise install with a live progress toast and report the outcome. Safe to
    /// fire-and-forget — see the class remarks. No ConfigureAwait(false), deliberately: the
    /// continuations call the UI sinks, and resuming on the captured (UI) context is what makes
    /// those calls thread-safe.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            var existing = _resolveExisting();
            if (existing != null)
            {
                _showToast($"clangd is already installed at {existing}", "info");
                return;
            }

            if (!_isWindows)
            {
                _showToast("Downloading C++ tools is not supported on this platform yet.", "info");
                return;
            }

            var progress = _progressWrapper(ReportProgress);
            ClangdInstallResult result;
            try
            {
                result = await _install(progress, ct);
            }
            finally
            {
                // Success, failure, or a thrown contract violation — the progress toast must
                // never outlive the transfer it reports on.
                _dismissProgress(ProgressNotificationId);
            }

            if (result.Success)
            {
                _showToast("clangd installed — restart the IDE to enable C++ IntelliSense.", "info");
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
            _showToast("clangd download cancelled.", "info");
        }
        catch (Exception ex)
        {
            // Contract violation somewhere below — surfaced, never swallowed silently and never
            // rethrown into the void of a fire-and-forget task.
            _showToast($"clangd download failed unexpectedly: {ex.Message}", "error");
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
    /// from <see cref="ClangdInstaller.ExpectedSizeBytes"/> that the transfer didn't report.
    /// Public and pure so the mapping is pinnable without running a flow.
    /// </summary>
    public static (double fraction, string message) FormatProgress(long bytesDownloaded, long totalBytes)
    {
        if (totalBytes > 0)
        {
            var fraction = Math.Clamp((double)bytesDownloaded / totalBytes, 0.0, 1.0);
            return (fraction,
                $"Downloading clangd… {FormatMegabytes(bytesDownloaded)} of {FormatMegabytes(totalBytes)}");
        }

        return (-1, $"Downloading clangd… {FormatMegabytes(bytesDownloaded)}");
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
        if (step == ClangdInstaller.StepAlreadyInProgress)
        {
            return ("A clangd download is already running.", "info");
        }

        var sentence = step switch
        {
            ClangdInstaller.StepDownload => "clangd download failed",
            ClangdInstaller.StepDownloadTimeout => "clangd download timed out",
            ClangdInstaller.StepVerifySize => "clangd download failed size verification",
            ClangdInstaller.StepVerifySha256 => "clangd download failed SHA-256 verification",
            ClangdInstaller.StepExtract => "clangd archive could not be extracted",
            ClangdInstaller.StepLayout => "clangd archive had an unexpected layout",
            ClangdInstaller.StepInstall => "clangd could not be moved into the tools directory",
            _ => $"clangd install failed ({step ?? "unknown step"})"
        };
        return ($"{sentence}: {detail}", "error");
    }
}
