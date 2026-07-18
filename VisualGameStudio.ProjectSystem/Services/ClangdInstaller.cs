using System.IO.Compression;
using System.Security.Cryptography;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Outcome of a <see cref="ClangdInstaller.InstallAsync"/> run. The installer never shows UI —
/// callers compose toasts from this result.
///
/// <para><see cref="InstalledExePath"/> is the absolute <c>bin/clangd.exe</c> path, non-null
/// exactly when <see cref="Success"/>.</para>
///
/// <para>On failure, <see cref="FailureStep"/> is one of the <c>ClangdInstaller.Step*</c> constants
/// (a closed vocabulary the toast composer switches on) and <see cref="FailureDetail"/> is a short
/// human-readable explanation (e.g. actual vs expected hash prefix). "An install is already in
/// progress" is represented as a failure with <see cref="FailureStep"/> =
/// <see cref="ClangdInstaller.StepAlreadyInProgress"/> — a distinct step value rather than an extra
/// bool, so every non-success outcome routes through the same step switch.</para>
/// </summary>
public sealed record ClangdInstallResult(
    bool Success,
    string? InstalledExePath,
    string? FailureStep,
    string? FailureDetail);

/// <summary>
/// The download seam <see cref="ClangdInstaller"/> consumes: stream <paramref name="url"/> to
/// <paramref name="destinationPath"/> within <paramref name="deadline"/>, reporting
/// (bytes downloaded, total bytes or -1). <see cref="FileDownloader.DownloadAsync"/> binds to this
/// as a method group; tests inject fakes so they never perform HTTP.
/// </summary>
public delegate Task ClangdDownloadDelegate(
    string url,
    string destinationPath,
    TimeSpan deadline,
    IProgress<(long bytesDownloaded, long totalBytes)>? progress,
    CancellationToken ct);

/// <summary>
/// Downloads the pinned clangd release, verifies it (size, then SHA-256), and installs it as
/// <c>&lt;toolsRoot&gt;/clangd_22.1.6/bin/clangd.exe</c> — exactly the layout the locator's
/// tools-root probe looks for. The archive is extracted WHOLE: <c>lib/clang/22/include/</c> holds
/// ~340 builtin headers clangd needs for include resolution.
///
/// <para>The install is staged: extract into <c>&lt;toolsRoot&gt;/.staging-&lt;guid&gt;/</c>, verify the
/// expected layout, then swap the versioned directory into place — a failed download or a bad
/// archive never leaves a half-written <c>clangd_22.1.6</c> behind. Installs are single-flight: a
/// second call while one is running reports <see cref="StepAlreadyInProgress"/> instead of queueing
/// a second 28MB download.</para>
/// </summary>
public sealed class ClangdInstaller : IDisposable
{
    // ---------------------------------------------------------------- pinned release facts (S0.1)

    /// <summary>The measured release asset. No .sha256 sidecar asset exists; the hash below matches GitHub's API digest.</summary>
    public const string DownloadUrl = "https://github.com/clangd/clangd/releases/download/22.1.6/clangd-windows-22.1.6.zip";

    /// <summary>SHA-256 of the release zip, uppercase hex; compared ordinal-ignore-case.</summary>
    public const string ExpectedSha256 = "CE54F16E0B4FD76D450EEDA9664420B195360B73FEBCFE40E661108FA57F2CE1";

    /// <summary>Exact byte size of the release zip.</summary>
    public const long ExpectedSizeBytes = 28_198_778;

    /// <summary>The zip's own root folder name, which is also the install directory name under the tools root.</summary>
    public const string InstalledDirName = "clangd_22.1.6";

    /// <summary>Whole-transfer deadline: ~28MB at a slow ~1Mbps is ~4 minutes; 10 gives headroom while still bounding a wedged transfer.</summary>
    public static readonly TimeSpan DownloadDeadline = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The production tools root, <c>%USERPROFILE%\.vgs\tools</c> — the locator's tools-root probe
    /// composes its default from this same property, single-sourcing the root between installer and
    /// probe. Another copy of the <c>~/.vgs</c> path construction (SettingsService, VsixInstaller,
    /// ...); a canonical helper is future work.
    /// </summary>
    public static string DefaultToolsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vgs", "tools");

    // ---------------------------------------------------------------- FailureStep vocabulary
    // The closed set of ClangdInstallResult.FailureStep values; the toast composer switches on these.

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

    /// <summary>The extracted archive lacked <c>clangd_22.1.6/bin/clangd.exe</c>.</summary>
    public const string StepLayout = "layout";

    /// <summary>The final swap (delete stale dir / move staged dir into place) failed.</summary>
    public const string StepInstall = "install";

    // ---------------------------------------------------------------- state

    private readonly string _toolsRoot;
    private readonly ClangdDownloadDelegate _download;
    private readonly string _expectedSha256;
    private readonly long _expectedSizeBytes;
    private readonly FileDownloader? _ownedDownloader;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="toolsRoot">
    /// Where versioned tool directories live; null means <see cref="DefaultToolsRoot"/>. Computed
    /// but NOT created here — only an actual install may touch the disk (VsixInstaller's eager
    /// ctor mkdir was explicitly rejected for this class).
    /// </param>
    /// <param name="downloader">
    /// The download seam; null means a real <see cref="FileDownloader"/> owned (and disposed) by
    /// this instance. Tests inject a fake so they never perform HTTP.
    /// </param>
    /// <param name="expectedSha256">Test seam: expected hash of the downloaded file; null means <see cref="ExpectedSha256"/>.</param>
    /// <param name="expectedSizeBytes">Test seam: expected size of the downloaded file; null means <see cref="ExpectedSizeBytes"/>.</param>
    public ClangdInstaller(
        string? toolsRoot = null,
        ClangdDownloadDelegate? downloader = null,
        string? expectedSha256 = null,
        long? expectedSizeBytes = null)
    {
        _toolsRoot = toolsRoot ?? DefaultToolsRoot;

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
    public async Task<ClangdInstallResult> InstallAsync(
        IProgress<(long bytesDownloaded, long totalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        // Single-flight: try-enter without waiting; a concurrent caller gets an answer, not a queue.
        if (!_gate.Wait(0))
        {
            return new ClangdInstallResult(false, null, StepAlreadyInProgress,
                "a clangd install is already running");
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"vgs-clangd-{Guid.NewGuid():N}.zip");
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
                return new ClangdInstallResult(false, null, StepDownloadTimeout, ex.Message);
            }
            catch (Exception ex)
            {
                return new ClangdInstallResult(false, null, StepDownload, ex.Message);
            }

            // 2. Verify — size first (cheap), then the streamed SHA-256 (never 28MB in memory).
            // Both stages are guarded: I/O on the just-downloaded zip can fail (e.g. an AV
            // scanner holding it with no sharing), and the never-throws contract must survive.
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
                return new ClangdInstallResult(false, null, StepVerifySize,
                    $"could not read the downloaded file: {ex.Message}");
            }
            if (actualSize != _expectedSizeBytes)
            {
                return new ClangdInstallResult(false, null, StepVerifySize,
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
                return new ClangdInstallResult(false, null, StepVerifySha256,
                    $"could not read the downloaded file: {ex.Message}");
            }
            if (!string.Equals(actualSha, _expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ClangdInstallResult(false, null, StepVerifySha256,
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
                return new ClangdInstallResult(false, null, StepExtract, ex.Message);
            }

            var stagedVersionDir = Path.Combine(stagingDir, InstalledDirName);
            var stagedExe = Path.Combine(stagedVersionDir, "bin", "clangd.exe");
            if (!File.Exists(stagedExe))
            {
                return new ClangdInstallResult(false, null, StepLayout,
                    $"the archive did not contain {InstalledDirName}/bin/clangd.exe");
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
                return new ClangdInstallResult(false, null, StepInstall, ex.Message);
            }

            return new ClangdInstallResult(true, Path.Combine(finalDir, "bin", "clangd.exe"), null, null);
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
