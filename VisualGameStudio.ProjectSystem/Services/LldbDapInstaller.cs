using System.IO.Compression;
using System.Security.Cryptography;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Outcome of a <see cref="LldbDapInstaller.InstallAsync"/> run. The installer never shows UI —
/// callers compose toasts from this result.
///
/// <para><see cref="InstalledExePath"/> is the absolute <c>bin/lldb-dap.exe</c> path, non-null
/// exactly when <see cref="Success"/>.</para>
///
/// <para>On failure, <see cref="FailureStep"/> is one of the <c>LldbDapInstaller.Step*</c> constants
/// (a closed vocabulary the toast composer switches on) and <see cref="FailureDetail"/> is a short
/// human-readable explanation (e.g. actual vs expected hash prefix). "An install is already in
/// progress" is represented as a failure with <see cref="FailureStep"/> =
/// <see cref="LldbDapInstaller.StepAlreadyInProgress"/> — a distinct step value rather than an extra
/// bool, so every non-success outcome routes through the same step switch.</para>
/// </summary>
public sealed record LldbDapInstallResult(
    bool Success,
    string? InstalledExePath,
    string? FailureStep,
    string? FailureDetail);

/// <summary>
/// The download seam <see cref="LldbDapInstaller"/> consumes: stream <paramref name="url"/> to
/// <paramref name="destinationPath"/> within <paramref name="deadline"/>, reporting
/// (bytes downloaded, total bytes or -1). <see cref="FileDownloader.DownloadAsync"/> binds to this
/// as a method group; tests inject fakes so they never perform HTTP.
/// </summary>
public delegate Task LldbDapDownloadDelegate(
    string url,
    string destinationPath,
    TimeSpan deadline,
    IProgress<(long bytesDownloaded, long totalBytes)>? progress,
    CancellationToken ct);

/// <summary>
/// Downloads the pinned lldb-dap release, verifies it (size, then SHA-256), and installs it as
/// <c>&lt;toolsRoot&gt;/lldb-dap_22.1.0/bin/lldb-dap.exe</c> — exactly the layout
/// <see cref="LldbDapLocator.FindInToolsRoot"/> probes first. The archive is extracted WHOLE:
/// the zip is self-contained by design (<c>liblldb.dll</c>, <c>lldb-argdumper.exe</c> and their
/// runtime dependencies ride alongside the adapter — lldb-dap.exe alone cannot start).
/// The member-for-member sibling of <see cref="ClangdInstaller"/>.
///
/// <para>The install is staged: extract into <c>&lt;toolsRoot&gt;/.staging-&lt;guid&gt;/</c>, verify the
/// expected layout, then swap the versioned directory into place — a failed download or a bad
/// archive never leaves a half-written <c>lldb-dap_22.1.0</c> behind. Installs are single-flight:
/// a second call while one is running reports <see cref="StepAlreadyInProgress"/> instead of
/// queueing a second large download.</para>
/// </summary>
public sealed class LldbDapInstaller : IDisposable
{
    // ---------------------------------------------------------------- pinned release facts
    // ⚠ PLACEHOLDER PINS — the self-hosted zip is a release-time deliverable
    // (docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md, Task 13).
    // Fill DownloadUrl/ExpectedSha256/ExpectedSizeBytes/InstalledDirName from the runbook's
    // measured values; IsReleasePinned gates the download UX until then.

    /// <summary>The self-hosted release asset (runbook, Task 13); the tag and file name are the runbook's contract.</summary>
    public const string DownloadUrl =
        "https://github.com/gracepriest/VisualGameStudioEngine/releases/download/lldb-dap-22.1.0/lldb-dap-windows-22.1.0.zip";

    /// <summary>SHA-256 of the release zip, uppercase hex; compared ordinal-ignore-case. Placeholder until measured.</summary>
    public const string ExpectedSha256 = "REPLACE-AT-RELEASE-TIME";

    /// <summary>Exact byte size of the release zip. Placeholder (0) until measured.</summary>
    public const long ExpectedSizeBytes = 0;

    /// <summary>The zip's own root folder name, which is also the install directory name under the tools root.</summary>
    public const string InstalledDirName = "lldb-dap_22.1.0";

    /// <summary>
    /// Whether the release pins above hold measured values rather than placeholders. False until
    /// the runbook zip ships; <c>LldbDapDownloadFlow</c> gates the download UX on this, so the
    /// placeholder URL is never fetched.
    /// </summary>
    public static bool IsReleasePinned =>
        !ExpectedSha256.StartsWith("REPLACE", StringComparison.Ordinal);

    /// <summary>
    /// Whole-transfer deadline. The self-contained zip (liblldb rides along) is expected to
    /// outweigh clangd's 28MB severalfold, so this is 15 minutes to clangd's 10 — still bounding
    /// a wedged transfer while leaving slow-link headroom.
    /// </summary>
    public static readonly TimeSpan DownloadDeadline = TimeSpan.FromMinutes(15);

    // ---------------------------------------------------------------- FailureStep vocabulary
    // The closed set of LldbDapInstallResult.FailureStep values; the toast composer switches on these.

    /// <summary>Another install holds the single-flight gate; no second download was started.</summary>
    public const string StepAlreadyInProgress = "already-in-progress";

    /// <summary>The download itself failed (network/HTTP error).</summary>
    public const string StepDownload = "download";

    /// <summary>The download exceeded <see cref="DownloadDeadline"/> — the toast can say "timed out".</summary>
    public const string StepDownloadTimeout = "download-timeout";

    /// <summary>The downloaded file's size differs from <see cref="ExpectedSizeBytes"/>.</summary>
    public const string StepVerifySize = "verify-size";

    /// <summary>The downloaded file's SHA-256 differs from <see cref="ExpectedSha256"/>.</summary>
    public const string StepVerifySha256 = "verify-sha256";

    /// <summary>The verified zip could not be extracted (corrupt archive despite matching hash — or disk trouble).</summary>
    public const string StepExtract = "extract";

    /// <summary>The extracted archive lacked <c>lldb-dap_22.1.0/bin/lldb-dap.exe</c>.</summary>
    public const string StepLayout = "layout";

    /// <summary>The final swap (delete stale dir / move staged dir into place) failed.</summary>
    public const string StepInstall = "install";

    // ---------------------------------------------------------------- state

    private readonly string _toolsRoot;
    private readonly LldbDapDownloadDelegate _download;
    private readonly string _expectedSha256;
    private readonly long _expectedSizeBytes;
    private readonly FileDownloader? _ownedDownloader;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="toolsRoot">
    /// Where versioned tool directories live; null means <see cref="ClangdInstaller.DefaultToolsRoot"/>
    /// — deliberately the CLANGD installer's property, the single source both installers and both
    /// locators' tools-root probes share, so all IDE-acquired tools live together and installer
    /// and probe can never drift apart. Computed but NOT created here — only an actual install
    /// may touch the disk (VsixInstaller's eager ctor mkdir was explicitly rejected for the
    /// clangd installer, and equally here).
    /// </param>
    /// <param name="downloader">
    /// The download seam; null means a real <see cref="FileDownloader"/> owned (and disposed) by
    /// this instance. Tests inject a fake so they never perform HTTP.
    /// </param>
    /// <param name="expectedSha256">Test seam: expected hash of the downloaded file; null means <see cref="ExpectedSha256"/>.</param>
    /// <param name="expectedSizeBytes">Test seam: expected size of the downloaded file; null means <see cref="ExpectedSizeBytes"/>.</param>
    public LldbDapInstaller(
        string? toolsRoot = null,
        LldbDapDownloadDelegate? downloader = null,
        string? expectedSha256 = null,
        long? expectedSizeBytes = null)
    {
        _toolsRoot = toolsRoot ?? ClangdInstaller.DefaultToolsRoot;

        if (downloader == null)
        {
            _ownedDownloader = new FileDownloader();
            _download = _ownedDownloader.DownloadAsync;
        }
        else
        {
            _download = downloader;
        }

        _expectedSha256 = expectedSha256 ?? ExpectedSha256;
        _expectedSizeBytes = expectedSizeBytes ?? ExpectedSizeBytes;
    }

    /// <summary>
    /// Runs the full download → verify → stage → swap pipeline. Failures come back as a result
    /// (never an exception), with one deliberate exception to that rule: an
    /// <see cref="OperationCanceledException"/> caused by <paramref name="ct"/> rethrows — the
    /// caller cancelled deliberately and there is nothing to report.
    /// </summary>
    /// <param name="progress">Forwarded to the downloader: (bytes downloaded, total bytes or -1).</param>
    /// <param name="ct">Caller cancellation; see the rethrow note above.</param>
    public async Task<LldbDapInstallResult> InstallAsync(
        IProgress<(long bytesDownloaded, long totalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        // Single-flight: try-enter without waiting; a concurrent caller gets an answer, not a queue.
        if (!_gate.Wait(0))
        {
            return new LldbDapInstallResult(false, null, StepAlreadyInProgress,
                "an lldb-dap install is already running");
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"vgs-lldb-dap-{Guid.NewGuid():N}.zip");
        string? stagingDir = null;
        try
        {
            // 1. Download to the temp zip through the seam.
            try
            {
                await _download(DownloadUrl, tempZip, DownloadDeadline, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // deliberate caller cancellation — not a failure to report
            }
            catch (TimeoutException ex)
            {
                return new LldbDapInstallResult(false, null, StepDownloadTimeout, ex.Message);
            }
            catch (Exception ex)
            {
                return new LldbDapInstallResult(false, null, StepDownload, ex.Message);
            }

            // 2. Verify — size first (cheap), then the streamed SHA-256 (never the whole zip in
            // memory). Both stages are guarded: I/O on the just-downloaded zip can fail (e.g. an
            // AV scanner holding it with no sharing), and the never-throws contract must survive.
            long actualSize;
            try
            {
                actualSize = new FileInfo(tempZip).Length;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // deliberate caller cancellation — not a failure to report
            }
            catch (Exception ex)
            {
                return new LldbDapInstallResult(false, null, StepVerifySize,
                    $"could not read the downloaded file: {ex.Message}");
            }
            if (actualSize != _expectedSizeBytes)
            {
                return new LldbDapInstallResult(false, null, StepVerifySize,
                    $"downloaded {actualSize:N0} bytes but the release is {_expectedSizeBytes:N0} bytes");
            }

            string actualSha;
            try
            {
                await using var zipStream = File.OpenRead(tempZip);
                actualSha = Convert.ToHexString(await SHA256.HashDataAsync(zipStream, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // deliberate caller cancellation — not a failure to report
            }
            catch (Exception ex)
            {
                return new LldbDapInstallResult(false, null, StepVerifySha256,
                    $"could not read the downloaded file: {ex.Message}");
            }
            if (!string.Equals(actualSha, _expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new LldbDapInstallResult(false, null, StepVerifySha256,
                    $"SHA-256 {Prefix(actualSha)}… does not match the expected {Prefix(_expectedSha256)}…");
            }

            // 3. Extract into a staging dir. The tools root is created lazily HERE, never earlier.
            stagingDir = Path.Combine(_toolsRoot, $".staging-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(stagingDir);
                ZipFile.ExtractToDirectory(tempZip, stagingDir);
            }
            catch (Exception ex)
            {
                return new LldbDapInstallResult(false, null, StepExtract, ex.Message);
            }

            var stagedVersionDir = Path.Combine(stagingDir, InstalledDirName);
            var stagedExe = Path.Combine(stagedVersionDir, "bin", "lldb-dap.exe");
            if (!File.Exists(stagedExe))
            {
                return new LldbDapInstallResult(false, null, StepLayout,
                    $"the archive did not contain {InstalledDirName}/bin/lldb-dap.exe");
            }

            // 4. Swap into place. A pre-existing dir is a broken install (this flow only runs when
            // the locator found nothing, or the user forced a reinstall) — replace it whole.
            // The delete-then-move pair is not atomic: a move failure after a successful delete
            // leaves NO install at all, surfaced as StepInstall; the next install attempt repairs it.
            var finalDir = Path.Combine(_toolsRoot, InstalledDirName);
            try
            {
                if (Directory.Exists(finalDir))
                {
                    Directory.Delete(finalDir, recursive: true);
                }
                Directory.Move(stagedVersionDir, finalDir);
            }
            catch (Exception ex)
            {
                return new LldbDapInstallResult(false, null, StepInstall, ex.Message);
            }

            return new LldbDapInstallResult(true, Path.Combine(finalDir, "bin", "lldb-dap.exe"), null, null);
        }
        finally
        {
            // Cleanup is best-effort: on success the staging shell and temp zip are spent; on
            // failure they must not leak. Either way the original outcome is what surfaces.
            if (stagingDir != null)
            {
                try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
            }
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }

            // Dispose-during-install (DI shutdown) must not throw from the finally.
            try { _gate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private static string Prefix(string sha) => sha.Length <= 12 ? sha : sha[..12];

    public void Dispose()
    {
        _ownedDownloader?.Dispose();
        _gate.Dispose();
    }
}
