using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins <see cref="ClangdInstaller"/> — the download → verify → stage → swap pipeline that puts
/// <c>&lt;toolsRoot&gt;/clangd_22.1.6/bin/clangd.exe</c> on disk (the exact layout the locator probes).
///
/// <para>No test here ever performs HTTP or touches the real <c>~/.vgs</c>: the downloader is an
/// injected seam that writes small in-test zip fixtures, the tools root is a per-test temp dir,
/// and the expected size/SHA-256 are computed from the fixture bytes so the tests exercise the
/// MECHANISM. The production release values are pinned separately by
/// <see cref="PinnedConstants_MatchTheMeasuredRelease"/> so a typo'd URL or hash fails loudly.</para>
/// </summary>
[TestFixture]
public class ClangdInstallerTests
{
    private string _tempDir = null!;
    private string _toolsRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "vgs-clangdinstaller-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // Deliberately NOT created: several tests assert the installer's lazy-creation contract.
        _toolsRoot = Path.Combine(_tempDir, "tools");
    }

    [TearDown]
    public void TearDown()
    {
        // Bounded-retry delete: try, catch, wait 250ms, one retry, swallow.
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            Thread.Sleep(250);
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    // ---------------------------------------------------------------- happy path

    [Test]
    public async Task Install_ProducesTheProbeLayout()
    {
        var fixture = StandardFixtureZip();
        string? seamUrl = null;
        string? seamDest = null;
        var seamDeadline = TimeSpan.Zero;

        using var installer = new ClangdInstaller(
            _toolsRoot,
            (url, dest, deadline, progress, ct) =>
            {
                seamUrl = url;
                seamDest = dest;
                seamDeadline = deadline;
                File.WriteAllBytes(dest, fixture);
                return Task.CompletedTask;
            },
            expectedSha256: Sha256Hex(fixture),
            expectedSizeBytes: fixture.Length);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.True,
            $"install failed at step '{result.FailureStep}': {result.FailureDetail}");

        var expectedExe = Path.Combine(_toolsRoot, ClangdInstaller.InstalledDirName, "bin", "clangd.exe");
        Assert.That(result.InstalledExePath, Is.EqualTo(expectedExe),
            "the result must hand back exactly the path the locator's probe will look for");
        Assert.That(File.Exists(expectedExe), Is.True, "bin/clangd.exe must land in the final layout");
        Assert.That(
            File.Exists(Path.Combine(_toolsRoot, ClangdInstaller.InstalledDirName, "lib", "clang", "22", "include", "x.h")),
            Is.True,
            "the builtin headers must be extracted too — clangd's include resolution needs the WHOLE archive");

        Assert.That(seamUrl, Is.EqualTo(ClangdInstaller.DownloadUrl), "the pinned release URL must reach the downloader");
        Assert.That(seamDeadline, Is.EqualTo(ClangdInstaller.DownloadDeadline), "the named deadline must reach the downloader");

        Assert.That(Directory.GetDirectories(_toolsRoot, ".staging-*"), Is.Empty,
            "a successful install must clean its staging directory");
        Assert.That(File.Exists(seamDest!), Is.False, "a successful install must delete the temp zip");
    }

    [Test]
    public async Task ExistingSameVersionDir_IsReplaced()
    {
        // A pre-existing clangd_22.1.6 dir is a broken install (this flow only runs when the
        // locator found nothing, or the user forced a reinstall) — it must be swapped out whole.
        var finalDir = Path.Combine(_toolsRoot, ClangdInstaller.InstalledDirName);
        Directory.CreateDirectory(finalDir);
        var staleMarker = Path.Combine(finalDir, "stale-marker.txt");
        await File.WriteAllTextAsync(staleMarker, "left behind by a broken install");

        var fixture = StandardFixtureZip();
        using var installer = new ClangdInstaller(
            _toolsRoot,
            WriteBytesSeam(fixture),
            expectedSha256: Sha256Hex(fixture),
            expectedSizeBytes: fixture.Length);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.True,
            $"install failed at step '{result.FailureStep}': {result.FailureDetail}");
        Assert.That(File.Exists(staleMarker), Is.False, "the stale dir must be replaced, not merged into");
        Assert.That(File.Exists(Path.Combine(finalDir, "bin", "clangd.exe")), Is.True);
    }

    // ---------------------------------------------------------------- verification gate

    [Test]
    public async Task ShaMismatch_RejectsBeforeExtraction()
    {
        var fixture = StandardFixtureZip();
        var corrupted = (byte[])fixture.Clone();
        corrupted[corrupted.Length / 2] ^= 0xFF; // one flipped byte: same size, different hash

        string? seamDest = null;
        using var installer = new ClangdInstaller(
            _toolsRoot,
            (url, dest, deadline, progress, ct) =>
            {
                seamDest = dest;
                File.WriteAllBytes(dest, corrupted);
                return Task.CompletedTask;
            },
            expectedSha256: Sha256Hex(fixture), // the hash of the UNcorrupted bytes
            expectedSizeBytes: fixture.Length);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureStep, Is.EqualTo(ClangdInstaller.StepVerifySha256));
        Assert.That(result.FailureDetail, Is.Not.Null.And.Not.Empty,
            "the failure must carry a human-readable detail for the eventual toast");
        Assert.That(Directory.Exists(_toolsRoot), Is.False,
            "a rejected download must never create anything under the tools root");
        Assert.That(File.Exists(seamDest!), Is.False, "the corrupt temp zip must be deleted");
    }

    [Test]
    public async Task SizeMismatch_RejectsBeforeHashing()
    {
        var fixture = StandardFixtureZip();

        // The expected SHA is garbage on purpose: if the implementation hashed before checking the
        // size, the failure would name the sha step — asserting the SIZE step proves the cheap
        // check runs first (no point streaming a 28MB hash over an obviously wrong file).
        using var installer = new ClangdInstaller(
            _toolsRoot,
            WriteBytesSeam(fixture),
            expectedSha256: new string('0', 64),
            expectedSizeBytes: fixture.Length + 1);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureStep, Is.EqualTo(ClangdInstaller.StepVerifySize));
        Assert.That(Directory.Exists(_toolsRoot), Is.False,
            "a rejected download must never create anything under the tools root");
    }

    // ---------------------------------------------------------------- staging hygiene

    [Test]
    public async Task StagingNeverLeaksOnFailure()
    {
        // A truncated zip whose sha/size ARE the expected fixture values: verification passes,
        // extraction throws — the staging dir must not survive the failure.
        var valid = StandardFixtureZip();
        var truncated = valid.Take(valid.Length / 2).ToArray();

        using var installer = new ClangdInstaller(
            _toolsRoot,
            WriteBytesSeam(truncated),
            expectedSha256: Sha256Hex(truncated),
            expectedSizeBytes: truncated.Length);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureStep, Is.EqualTo(ClangdInstaller.StepExtract));
        Assert.That(Directory.Exists(_toolsRoot), Is.True,
            "extraction is the step that lazily creates the tools root, so it exists by now");
        Assert.That(Directory.GetDirectories(_toolsRoot, ".staging-*"), Is.Empty,
            "no staging directory may leak out of a failed install");
        Assert.That(Directory.Exists(Path.Combine(_toolsRoot, ClangdInstaller.InstalledDirName)), Is.False,
            "a failed install must not produce the final directory");
    }

    [Test]
    public void ToolsRoot_NotCreatedAtConstruction()
    {
        // VsixInstaller eagerly mkdirs in its ctor; that was explicitly rejected for this class —
        // only an actual install may touch the disk.
        using var installer = new ClangdInstaller(
            _toolsRoot,
            WriteBytesSeam(Array.Empty<byte>()),
            expectedSha256: new string('0', 64),
            expectedSizeBytes: 0);

        Assert.That(Directory.Exists(_toolsRoot), Is.False,
            "constructing the installer must not create the tools root");
    }

    // ---------------------------------------------------------------- single-flight

    [Test]
    public async Task SecondConcurrentInstall_IsCoalesced()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloaderCalls = 0;

        using var installer = new ClangdInstaller(
            _toolsRoot,
            async (url, dest, deadline, progress, ct) =>
            {
                Interlocked.Increment(ref downloaderCalls);
                await gate.Task;
                throw new InvalidOperationException("seam ends the first install after the assertion window");
            },
            expectedSha256: new string('0', 64),
            expectedSizeBytes: 0);

        var first = installer.InstallAsync(); // parked inside the seam, holding the single-flight gate
        var second = await installer.InstallAsync();

        Assert.That(second.Success, Is.False);
        Assert.That(second.FailureStep, Is.EqualTo(ClangdInstaller.StepAlreadyInProgress));
        Assert.That(downloaderCalls, Is.EqualTo(1),
            "a second concurrent InstallAsync must NOT start a second download");

        gate.SetResult();
        var firstResult = await first;
        Assert.That(firstResult.Success, Is.False);
        Assert.That(firstResult.FailureStep, Is.EqualTo(ClangdInstaller.StepDownload),
            "the first install must run to its own (failure) result, unaffected by the coalesced second call");
    }

    // ---------------------------------------------------------------- downloader exception mapping

    [Test]
    public async Task DownloaderTimeout_MapsToDownloadStepFailure()
    {
        const string timeoutMessage = "Download of 'x' did not complete within the deadline of 00:10:00.";
        using var installer = new ClangdInstaller(
            _toolsRoot,
            (url, dest, deadline, progress, ct) => throw new TimeoutException(timeoutMessage),
            expectedSha256: new string('0', 64),
            expectedSizeBytes: 0);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureStep, Is.EqualTo(ClangdInstaller.StepDownloadTimeout),
            "a deadline timeout must be distinguishable from other download failures (the toast says 'timed out')");
        Assert.That(result.FailureDetail, Is.EqualTo(timeoutMessage));
    }

    [Test]
    public void CallerCancellation_Rethrows()
    {
        // FileDownloader's contract splits deadline (TimeoutException) from caller cancellation
        // (OperationCanceledException). The installer maps the former to a failure result but must
        // RETHROW the latter — the caller cancelled deliberately, there is nothing to toast.
        using var cts = new CancellationTokenSource();
        using var installer = new ClangdInstaller(
            _toolsRoot,
            (url, dest, deadline, progress, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            expectedSha256: new string('0', 64),
            expectedSizeBytes: 0);

        Assert.CatchAsync<OperationCanceledException>(
            async () => await installer.InstallAsync(ct: cts.Token));

        // The gate must have been released on the rethrow path: a follow-up call runs the seam
        // again (and cancels again) instead of reporting already-in-progress.
        Assert.CatchAsync<OperationCanceledException>(
            async () => await installer.InstallAsync(ct: cts.Token));
    }

    // ---------------------------------------------------------------- pinned release facts

    [Test]
    public void PinnedConstants_MatchTheMeasuredRelease()
    {
        // Verbatim S0.1 measurements of the real release asset — a typo here bricks the install
        // (wrong URL 404s; wrong hash rejects every good download), so pin all four exactly.
        Assert.That(ClangdInstaller.DownloadUrl,
            Is.EqualTo("https://github.com/clangd/clangd/releases/download/22.1.6/clangd-windows-22.1.6.zip"));
        Assert.That(ClangdInstaller.ExpectedSha256,
            Is.EqualTo("CE54F16E0B4FD76D450EEDA9664420B195360B73FEBCFE40E661108FA57F2CE1"));
        Assert.That(ClangdInstaller.ExpectedSizeBytes, Is.EqualTo(28_198_778L));
        Assert.That(ClangdInstaller.InstalledDirName, Is.EqualTo("clangd_22.1.6"));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A fixture zip with the REAL release's root layout: <c>clangd_22.1.6/</c> already at the
    /// archive root, containing <c>bin/clangd.exe</c>, <c>LICENSE.TXT</c>, and a builtin header.
    /// </summary>
    private static byte[] StandardFixtureZip() => BuildZip(
        ("clangd_22.1.6/bin/clangd.exe", "fake clangd binary"),
        ("clangd_22.1.6/LICENSE.TXT", "license text"),
        ("clangd_22.1.6/lib/clang/22/include/x.h", "// builtin header"));

    private static byte[] BuildZip(params (string entryPath, string content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryPath, content) in entries)
            {
                var entry = archive.CreateEntry(entryPath);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return buffer.ToArray();
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>A downloader seam that just writes <paramref name="bytes"/> to the destination.</summary>
    private static Func<string, string, TimeSpan, IProgress<(long, long)>?, CancellationToken, Task> WriteBytesSeam(byte[] bytes)
        => (url, dest, deadline, progress, ct) =>
        {
            File.WriteAllBytes(dest, bytes);
            return Task.CompletedTask;
        };
}
