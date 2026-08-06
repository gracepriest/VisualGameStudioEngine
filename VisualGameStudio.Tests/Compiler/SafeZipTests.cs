using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BasicLang.Runtime;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Chip task_f1ff697e — zip-slip.
///
/// <para>An archive entry named <c>../evil.txt</c> escapes the directory it is extracted
/// into. <c>ZipFile.ExtractToDirectory</c> does guard the common case, but the repo extracts
/// archives at FIVE sites and one of them takes fully untrusted input: third-party
/// <c>.vsix</c> packages downloaded from Open VSX. A shared, tested guard is cheaper than
/// trusting each call site to have thought about it.</para>
///
/// <para>The guard also refuses ROOTED entries (<c>C:\…</c>, <c>/etc/…</c>), which are a
/// second escape route that a purely <c>..</c>-focused check misses.</para>
/// </summary>
[TestFixture]
public class SafeZipTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BasicLang_SafeZip_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a locked temp dir must not fail a passing test */ }
    }

    /// <summary>
    /// Writes a zip with the given entry names. Uses the raw entry name verbatim so a
    /// traversal entry can actually be created — the normal Create* helpers sanitise.
    /// </summary>
    private string MakeZip(string name, params string[] entryNames)
    {
        var path = Path.Combine(_dir, name);
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName);

            // A directory entry must stay EMPTY — .NET refuses "name ends in a separator but
            // contains data" — so only file entries get a payload.
            if (entryName.EndsWith("/", StringComparison.Ordinal) ||
                entryName.EndsWith("\\", StringComparison.Ordinal)) continue;

            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("payload");
        }
        return path;
    }

    private string Dest => Path.Combine(_dir, "out");

    // ---------------------------------------------------------------- the happy path

    [Test]
    public void ExtractsOrdinaryEntries()
    {
        SafeZip.ExtractToDirectory(MakeZip("ok.zip", "a.txt", "sub/b.txt"), Dest);

        Assert.That(File.Exists(Path.Combine(Dest, "a.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(Dest, "sub", "b.txt")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(Dest, "a.txt")), Is.EqualTo("payload"));
    }

    [Test]
    public void CreatesTheDestinationDirectory()
    {
        var nested = Path.Combine(_dir, "a", "b", "c");
        SafeZip.ExtractToDirectory(MakeZip("ok.zip", "x.txt"), nested);

        Assert.That(File.Exists(Path.Combine(nested, "x.txt")), Is.True);
    }

    /// <summary>Overwrite is opt-in, matching ZipFile's own signature.</summary>
    [Test]
    public void OverwritesOnlyWhenAsked()
    {
        Directory.CreateDirectory(Dest);
        File.WriteAllText(Path.Combine(Dest, "a.txt"), "original");
        var zip = MakeZip("ok.zip", "a.txt");

        Assert.That(() => SafeZip.ExtractToDirectory(zip, Dest), Throws.Exception);

        SafeZip.ExtractToDirectory(zip, Dest, overwriteFiles: true);
        Assert.That(File.ReadAllText(Path.Combine(Dest, "a.txt")), Is.EqualTo("payload"));
    }

    // ---------------------------------------------------------------- the guard

    /// <summary>THE attack: a relative entry that climbs out of the destination.</summary>
    [Test]
    public void RefusesParentTraversal()
    {
        var zip = MakeZip("evil.zip", "../escaped.txt");

        var ex = Assert.Throws<InvalidDataException>(
            () => SafeZip.ExtractToDirectory(zip, Dest));

        Assert.That(ex!.Message, Does.Contain("escaped.txt"), "the diagnostic must name the entry");
        Assert.That(File.Exists(Path.Combine(_dir, "escaped.txt")), Is.False,
            "nothing may be written outside the destination");
    }

    [Test]
    public void RefusesDeepTraversal()
        => Assert.That(() => SafeZip.ExtractToDirectory(
                MakeZip("evil.zip", "a/../../escaped.txt"), Dest),
            Throws.TypeOf<InvalidDataException>());

    /// <summary>
    /// Backslash separators — Compress-Archive writes these, and the lldb-dap runbook relies
    /// on them being accepted, so the guard must normalise rather than simply reject them.
    /// </summary>
    [Test]
    public void RefusesBackslashTraversal()
        => Assert.That(() => SafeZip.ExtractToDirectory(
                MakeZip("evil.zip", @"..\escaped.txt"), Dest),
            Throws.TypeOf<InvalidDataException>());

    /// <summary>A ROOTED entry escapes without containing `..` at all.</summary>
    [Test]
    public void RefusesRootedEntries()
    {
        var rooted = OperatingSystem.IsWindows() ? @"C:\escaped.txt" : "/tmp/escaped.txt";

        Assert.That(() => SafeZip.ExtractToDirectory(MakeZip("evil.zip", rooted), Dest),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>
    /// A sibling whose name merely STARTS WITH the destination's is not inside it — the same
    /// boundary bug the preview server's containment check exists for.
    /// </summary>
    [Test]
    public void RefusesSiblingPrefixEscape()
        => Assert.That(() => SafeZip.ExtractToDirectory(
                MakeZip("evil.zip", "../out_evil/x.txt"), Dest),
            Throws.TypeOf<InvalidDataException>());

    /// <summary>
    /// ⛔ NOTHING may be written before the refusal. An archive whose FIRST entry is innocent
    /// and second is hostile must leave no partial extraction behind — otherwise a crafted
    /// archive plants files and merely reports an error.
    /// </summary>
    [Test]
    public void RefusesBeforeWritingAnything()
    {
        var zip = MakeZip("evil.zip", "innocent.txt", "../escaped.txt");

        Assert.That(() => SafeZip.ExtractToDirectory(zip, Dest),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(File.Exists(Path.Combine(Dest, "innocent.txt")), Is.False,
            "entries must be validated up front, not as they are written");
        Assert.That(File.Exists(Path.Combine(_dir, "escaped.txt")), Is.False);
    }

    /// <summary>A directory entry (trailing separator) is legal and must not be mistaken for an escape.</summary>
    [Test]
    public void AllowsDirectoryEntries()
    {
        Assert.That(() => SafeZip.ExtractToDirectory(MakeZip("ok.zip", "sub/", "sub/a.txt"), Dest),
            Throws.Nothing);
        Assert.That(File.Exists(Path.Combine(Dest, "sub", "a.txt")), Is.True);
    }
}
