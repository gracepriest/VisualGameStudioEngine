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
/// Pins <see cref="LldbDapInstaller"/> — the download → verify → stage → swap pipeline that puts
/// <c>&lt;toolsRoot&gt;/lldb-dap_22.1.0/bin/lldb-dap.exe</c> on disk (the exact layout
/// <see cref="LldbDapLocator.FindInToolsRoot"/> probes). The structural sibling of
/// <see cref="ClangdInstallerTests"/> — same seams, same hygiene contracts.
///
/// <para>No test here ever performs HTTP or touches the real <c>~/.vgs</c>: the downloader is an
/// injected seam that writes small in-test zip fixtures, the tools root is a per-test temp dir,
/// and the expected size/SHA-256 are computed from the fixture bytes so the tests exercise the
/// MECHANISM. The production release facts are placeholders until the self-hosted zip ships
/// (runbook, Task 13); <see cref="ReleasePins_MatchTheRunbookOnceFilled"/> ignores itself until
/// <see cref="LldbDapInstaller.IsReleasePinned"/> flips, then holds the pins to runbook shape.</para>
/// </summary>
[TestFixture]
public class LldbDapInstallerTests
{
    private string _tempDir = null!;
    private string _toolsRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "vgs-lldbdapinstaller-tests", Guid.NewGuid().ToString("N"));
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

        using var installer = new LldbDapInstaller(
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

        var installedDir = Path.Combine(_toolsRoot, LldbDapInstaller.InstalledDirName);
        var expectedExe = Path.Combine(installedDir, "bin", "lldb-dap.exe");
        Assert.That(result.InstalledExePath, Is.EqualTo(expectedExe),
            "the result must hand back exactly the path the locator's tools-root probe will look for");
        Assert.That(File.Exists(expectedExe), Is.True, "bin/lldb-dap.exe must land in the final layout");
        Assert.That(File.Exists(Path.Combine(installedDir, "bin", "liblldb.dll")), Is.True,
            "liblldb.dll must survive the install — the self-contained zip is the whole point " +
            "(lldb-dap.exe alone cannot start without its engine DLL)");
        Assert.That(File.Exists(Path.Combine(installedDir, "bin", "lldb-argdumper.exe")), Is.True,
            "lldb-argdumper.exe must survive the install alongside the adapter");

        Assert.That(seamUrl, Is.EqualTo(LldbDapInstaller.DownloadUrl), "the pinned release URL must reach the downloader");
        Assert.That(seamDeadline, Is.EqualTo(LldbDapInstaller.DownloadDeadline), "the named deadline must reach the downloader");

        Assert.That(Directory.GetDirectories(_toolsRoot, ".staging-*"), Is.Empty,
            "a successful install must clean its staging directory");
        Assert.That(File.Exists(seamDest!), Is.False, "a successful install must delete the temp zip");
    }

    [Test]
    public async Task ExistingSameVersionDir_IsReplaced()
    {
        // A pre-existing lldb-dap_22.1.0 dir is a broken install (this flow only runs when the
        // locator found nothing, or the user forced a reinstall) — it must be swapped out whole.
        var finalDir = Path.Combine(_toolsRoot, LldbDapInstaller.InstalledDirName);
        Directory.CreateDirectory(finalDir);
        var staleMarker = Path.Combine(finalDir, "stale-marker.txt");
        await File.WriteAllTextAsync(staleMarker, "left behind by a broken install");

        var fixture = StandardFixtureZip();
        using var installer = new LldbDapInstaller(
            _toolsRoot,
            WriteBytesSeam(fixture),
            expectedSha256: Sha256Hex(fixture),
            expectedSizeBytes: fixture.Length);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.True,
            $"install failed at step '{result.FailureStep}': {result.FailureDetail}");
        Assert.That(File.Exists(staleMarker), Is.False, "the stale dir must be replaced, not merged into");
        Assert.That(File.Exists(Path.Combine(finalDir, "bin", "lldb-dap.exe")), Is.True);
    }

    // ---------------------------------------------------------------- verification gate

    [Test]
    public async Task ShaMismatch_RejectsBeforeExtraction()
    {
        var fixture = StandardFixtureZip();
        var corrupted = (byte[])fixture.Clone();
        corrupted[corrupted.Length / 2] ^= 0xFF; // one flipped byte: same size, different hash

        string? seamDest = null;
        using var installer = new LldbDapInstaller(
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
        Assert.That(result.FailureStep, Is.EqualTo(LldbDapInstaller.StepVerifySha256));
        Assert.That(result.FailureDetail, Is.Not.Null.And.Not.Empty,
            "the failure must carry a human-readable detail for the eventual toast");
        Assert.That(Directory.Exists(_toolsRoot), Is.False,
            "a rejected download must never create anything under the tools root");
        Assert.That(File.Exists(seamDest!), Is.False, "the corrupt temp zip must be deleted");
    }

    [Test]
    public async Task TruncatedDownload_SizeMismatchRejectsBeforeHashing()
    {
        // The downloader writes HALF the fixture bytes (a truncated transfer). The expected SHA
        // is garbage on purpose: if the implementation hashed before checking the size, the
        // failure would name the sha step — asserting the SIZE step proves the cheap check runs
        // first (no point streaming a hash over an obviously wrong file).
        var fixture = StandardFixtureZip();
        var truncated = fixture.Take(fixture.Length / 2).ToArray();

        string? seamDest = null;
        using var installer = new LldbDapInstaller(
            _toolsRoot,
            (url, dest, deadline, progress, ct) =>
            {
                seamDest = dest;
                File.WriteAllBytes(dest, truncated);
                return Task.CompletedTask;
            },
            expectedSha256: new string('0', 64),
            expectedSizeBytes: fixture.Length); // the FULL length — the truncated file must miss it

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureStep, Is.EqualTo(LldbDapInstaller.StepVerifySize),
            "a short download must fail the size gate, not reach the SHA-256 step");
        Assert.That(result.FailureDetail, Is.Not.Null.And.Not.Empty);
        Assert.That(Directory.Exists(_toolsRoot), Is.False,
            "a rejected download must never create anything under the tools root");
        Assert.That(File.Exists(seamDest!), Is.False, "the truncated temp zip must be deleted");
    }

    // ---------------------------------------------------------------- staging hygiene

    [Test]
    public async Task StagingDir_NeverLeaksOnFailure()
    {
        // A truncated zip whose sha/size ARE the expected fixture values: verification passes,
        // extraction throws — the staging dir must not survive the failure.
        var valid = StandardFixtureZip();
        var truncated = valid.Take(valid.Length / 2).ToArray();

        using var installer = new LldbDapInstaller(
            _toolsRoot,
            WriteBytesSeam(truncated),
            expectedSha256: Sha256Hex(truncated),
            expectedSizeBytes: truncated.Length);

        var result = await installer.InstallAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureStep, Is.EqualTo(LldbDapInstaller.StepExtract));
        Assert.That(Directory.Exists(_toolsRoot), Is.True,
            "extraction is the step that lazily creates the tools root, so it exists by now");
        Assert.That(Directory.GetDirectories(_toolsRoot, ".staging-*"), Is.Empty,
            "no staging directory may leak out of a failed install");
        Assert.That(Directory.Exists(Path.Combine(_toolsRoot, LldbDapInstaller.InstalledDirName)), Is.False,
            "a failed install must not produce the final directory");
    }

    [Test]
    public void ToolsRoot_IsNotCreatedAtConstruction()
    {
        // VsixInstaller eagerly mkdirs in its ctor; that was explicitly rejected for the clangd
        // installer and equally holds here — only an actual install may touch the disk.
        using var installer = new LldbDapInstaller(
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

        using var installer = new LldbDapInstaller(
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
        Assert.That(second.FailureStep, Is.EqualTo(LldbDapInstaller.StepAlreadyInProgress));
        Assert.That(downloaderCalls, Is.EqualTo(1),
            "a second concurrent InstallAsync must NOT start a second download");

        gate.SetResult();
        var firstResult = await first;
        Assert.That(firstResult.Success, Is.False);
        Assert.That(firstResult.FailureStep, Is.EqualTo(LldbDapInstaller.StepDownload),
            "the first install must run to its own (failure) result, unaffected by the coalesced second call");
    }

    // ---------------------------------------------------------------- pinned release facts

    [Test]
    public void ReleasePins_MatchTheRunbookOnceFilled()
    {
        // Stable regardless of pin state: the versioned dir name is what the locator's
        // FindInToolsRoot probe ranks (lldb-dap_<version>), and the URL points at the
        // self-hosted release asset the runbook (Task 13) publishes.
        Assert.That(LldbDapInstaller.InstalledDirName, Is.EqualTo("lldb-dap_22.1.0"));
        Assert.That(LldbDapInstaller.DownloadUrl, Is.EqualTo(
            "https://github.com/gracepriest/VisualGameStudioEngine/releases/download/lldb-dap-22.1.0/lldb-dap-windows-22.1.0.zip"));

        if (!LldbDapInstaller.IsReleasePinned)
        {
            Assert.Ignore("zip not published — runbook pending " +
                "(docs/superpowers/specs/2026-07-19-lldb-dap-zip-release-runbook.md); " +
                "sha/size pins are placeholders until the release asset is measured.");
        }

        // Once the runbook fills the pins, hold them to measured-release shape (the
        // ClangdInstallerTests.PinnedConstants_MatchTheMeasuredRelease role): a real 64-hex-char
        // SHA-256 and a real byte size — never a partial fill that bricks every install.
        Assert.That(LldbDapInstaller.ExpectedSha256, Does.Match("^[0-9A-Fa-f]{64}$"),
            "a pinned release must carry the measured SHA-256, 64 hex chars");
        Assert.That(LldbDapInstaller.ExpectedSizeBytes, Is.GreaterThan(0L),
            "a pinned release must carry the measured byte size");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A fixture zip with the runbook's root layout: <c>lldb-dap_22.1.0/</c> already at the
    /// archive root, containing <c>bin/lldb-dap.exe</c> plus the DLLs that make the install
    /// self-contained (<c>liblldb.dll</c>, <c>lldb-argdumper.exe</c>).
    /// </summary>
    private static byte[] StandardFixtureZip() => BuildZip(
        ("lldb-dap_22.1.0/bin/lldb-dap.exe", "fake lldb-dap binary"),
        ("lldb-dap_22.1.0/bin/liblldb.dll", "fake lldb engine dll"),
        ("lldb-dap_22.1.0/bin/lldb-argdumper.exe", "fake argdumper binary"));

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
    private static LldbDapDownloadDelegate WriteBytesSeam(byte[] bytes)
        => (url, dest, deadline, progress, ct) =>
        {
            File.WriteAllBytes(dest, bytes);
            return Task.CompletedTask;
        };
}
