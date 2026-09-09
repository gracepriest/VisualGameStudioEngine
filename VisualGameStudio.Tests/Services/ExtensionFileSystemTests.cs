using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// The <c>workspace.fs</c> family — the first slice of the JS→C# methods that had no handler at all.
///
/// <para>⛔ These eight are REQUESTS, not notifications, and that is why they were chosen first. A
/// notification with no handler is a silent no-op (an output channel simply never clears); a REQUEST
/// with no handler produces a JSON-RPC error response, which rejects the promise
/// <c>rpc.sendRequest</c> is awaiting (rpc.js:53) — usually inside an extension's activate(), where
/// it takes the whole activation down. An extension reading its own config file is the first thing
/// most of them do.</para>
///
/// <para>Tested behaviourally rather than as a source guard: this logic is real (base64, URI→path,
/// error mapping), and the host's constructor does not spawn Node — that happens in StartAsync — so
/// nothing here needs a process.</para>
/// </summary>
[TestFixture]
public class ExtensionFileSystemTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "vgs-extfs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { Thread.Sleep(250); try { Directory.Delete(_root, recursive: true); } catch { } }
    }

    private string PathIn(string name) => Path.Combine(_root, name);

    /// <summary>The wire always carries a file:// URI, never a bare path — that is what the JS sends.</summary>
    private string UriIn(string name) => ExtensionHost.ToDocumentUri(PathIn(name));

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    private static string UnB64(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64));

    // ------------------------------------------------------------------ readFile / writeFile

    /// <summary>
    /// workspace.js does <c>Buffer.from(result, 'base64')</c>, so the result must be BARE base64 —
    /// not a data: URI, not JSON-wrapped. Returning raw text would decode to garbage rather than
    /// failing, which is the kind of wrong that reaches a user as a corrupt file.
    /// </summary>
    [Test]
    public void ReadFile_ReturnsBareBase64()
    {
        File.WriteAllText(PathIn("a.txt"), "hello world");

        var result = ExtensionFileSystem.ReadFile(UriIn("a.txt"));

        Assert.That(UnB64(result), Is.EqualTo("hello world"));
    }

    /// <summary>Bytes must survive intact — the channel is base64 precisely so non-UTF8 content can cross.</summary>
    [Test]
    public void ReadFile_PreservesNonTextBytes()
    {
        var bytes = new byte[] { 0x00, 0xFF, 0x10, 0x80, 0x7F };
        File.WriteAllBytes(PathIn("b.bin"), bytes);

        var result = ExtensionFileSystem.ReadFile(UriIn("b.bin"));

        Assert.That(Convert.FromBase64String(result), Is.EqualTo(bytes));
    }

    [Test]
    public void WriteFile_DecodesBase64Content()
    {
        ExtensionFileSystem.WriteFile(UriIn("c.txt"), B64("written by an extension"));

        Assert.That(File.ReadAllText(PathIn("c.txt")), Is.EqualTo("written by an extension"));
    }

    /// <summary>
    /// VS Code's writeFile creates missing parent directories by default. Without this an extension
    /// writing its own cache under a fresh folder fails on a DirectoryNotFoundException.
    /// </summary>
    [Test]
    public void WriteFile_CreatesMissingParentDirectories()
    {
        var uri = ExtensionHost.ToDocumentUri(Path.Combine(_root, "nested", "deep", "d.txt"));

        ExtensionFileSystem.WriteFile(uri, B64("x"));

        Assert.That(File.Exists(Path.Combine(_root, "nested", "deep", "d.txt")), Is.True);
    }

    // ------------------------------------------------------------------ stat

    /// <summary>
    /// The FileStat shape is fixed by VS Code: <c>type</c> is 1 for a file and 2 for a directory,
    /// and the times are milliseconds since the Unix epoch. An extension branches on
    /// <c>stat.type === 2</c>, so a wrong constant sends it down the wrong path silently.
    /// </summary>
    [Test]
    public void Stat_ReportsAFileAsTypeOne()
    {
        File.WriteAllText(PathIn("e.txt"), "12345");

        var stat = ExtensionFileSystem.Stat(UriIn("e.txt"));

        Assert.That(stat.Type, Is.EqualTo(1), "VS Code FileType.File == 1");
        Assert.That(stat.Size, Is.EqualTo(5));
        Assert.That(stat.Mtime, Is.GreaterThan(0), "mtime is epoch milliseconds, not a DateTime");
    }

    [Test]
    public void Stat_ReportsADirectoryAsTypeTwo()
    {
        Directory.CreateDirectory(PathIn("sub"));

        var stat = ExtensionFileSystem.Stat(UriIn("sub"));

        Assert.That(stat.Type, Is.EqualTo(2), "VS Code FileType.Directory == 2");
    }

    /// <summary>
    /// ⛔ A missing file must FAIL, not answer a zero-sized stat. An extension uses stat as an
    /// existence probe; a stat that always succeeds tells it every path exists.
    /// </summary>
    [Test]
    public void Stat_ThrowsForAMissingPath()
    {
        Assert.That(() => ExtensionFileSystem.Stat(UriIn("nope.txt")),
            Throws.TypeOf<FileNotFoundException>());
    }

    // ------------------------------------------------------------------ readDirectory

    /// <summary>
    /// readDirectory returns PAIRS — <c>[[name, type], …]</c> — not a list of names and not objects.
    /// The name is the entry name alone, never a full path.
    /// </summary>
    [Test]
    public void ReadDirectory_ReturnsNameAndTypePairs()
    {
        File.WriteAllText(PathIn("f.txt"), "");
        Directory.CreateDirectory(PathIn("g-dir"));

        var entries = ExtensionFileSystem.ReadDirectory(ExtensionHost.ToDocumentUri(_root));

        Assert.That(entries, Has.Count.EqualTo(2));

        var file = entries.Single(e => (string)e[0] == "f.txt");
        var dir = entries.Single(e => (string)e[0] == "g-dir");

        Assert.That(file[1], Is.EqualTo(1), "a file entry carries FileType.File");
        Assert.That(dir[1], Is.EqualTo(2), "a directory entry carries FileType.Directory");
    }

    [Test]
    public void ReadDirectory_ThrowsForAMissingDirectory()
    {
        Assert.That(() => ExtensionFileSystem.ReadDirectory(UriIn("no-such-dir")),
            Throws.TypeOf<DirectoryNotFoundException>());
    }

    // ------------------------------------------------------------------ createDirectory / delete

    /// <summary>Creating an existing directory is a no-op in VS Code, not an error.</summary>
    [Test]
    public void CreateDirectory_IsIdempotent()
    {
        ExtensionFileSystem.CreateDirectory(UriIn("h-dir"));

        Assert.That(() => ExtensionFileSystem.CreateDirectory(UriIn("h-dir")), Throws.Nothing);
        Assert.That(Directory.Exists(PathIn("h-dir")), Is.True);
    }

    [Test]
    public void Delete_RemovesAFile()
    {
        File.WriteAllText(PathIn("i.txt"), "");

        ExtensionFileSystem.Delete(UriIn("i.txt"), recursive: false);

        Assert.That(File.Exists(PathIn("i.txt")), Is.False);
    }

    /// <summary>
    /// ⛔ A non-recursive delete of a NON-EMPTY directory must fail. Silently deleting the contents
    /// is the difference between an extension removing its own cache folder and an extension
    /// removing a folder of the user's source.
    /// </summary>
    [Test]
    public void Delete_RefusesANonEmptyDirectoryWithoutRecursive()
    {
        Directory.CreateDirectory(PathIn("j-dir"));
        File.WriteAllText(Path.Combine(PathIn("j-dir"), "inner.txt"), "");

        Assert.That(() => ExtensionFileSystem.Delete(UriIn("j-dir"), recursive: false),
            Throws.Exception);
        Assert.That(File.Exists(Path.Combine(PathIn("j-dir"), "inner.txt")), Is.True,
            "the refused delete must not have removed anything");
    }

    [Test]
    public void Delete_RemovesADirectoryTreeWhenRecursive()
    {
        Directory.CreateDirectory(PathIn("k-dir"));
        File.WriteAllText(Path.Combine(PathIn("k-dir"), "inner.txt"), "");

        ExtensionFileSystem.Delete(UriIn("k-dir"), recursive: true);

        Assert.That(Directory.Exists(PathIn("k-dir")), Is.False);
    }

    // ------------------------------------------------------------------ rename / copy

    [Test]
    public void Rename_MovesAFile()
    {
        File.WriteAllText(PathIn("l.txt"), "content");

        ExtensionFileSystem.Rename(UriIn("l.txt"), UriIn("l-renamed.txt"), overwrite: false);

        Assert.That(File.Exists(PathIn("l.txt")), Is.False);
        Assert.That(File.ReadAllText(PathIn("l-renamed.txt")), Is.EqualTo("content"));
    }

    /// <summary>
    /// ⛔ overwrite:false must REFUSE rather than clobber. This is the flag that decides whether an
    /// extension's "save as" can destroy an existing file without asking.
    /// </summary>
    [Test]
    public void Rename_RefusesToClobberWithoutOverwrite()
    {
        File.WriteAllText(PathIn("m.txt"), "source");
        File.WriteAllText(PathIn("m-target.txt"), "PRECIOUS");

        Assert.That(() => ExtensionFileSystem.Rename(UriIn("m.txt"), UriIn("m-target.txt"), overwrite: false),
            Throws.Exception);
        Assert.That(File.ReadAllText(PathIn("m-target.txt")), Is.EqualTo("PRECIOUS"),
            "the refused rename must not have touched the target");
    }

    [Test]
    public void Rename_ReplacesTheTargetWhenOverwriteIsSet()
    {
        File.WriteAllText(PathIn("n.txt"), "source");
        File.WriteAllText(PathIn("n-target.txt"), "old");

        ExtensionFileSystem.Rename(UriIn("n.txt"), UriIn("n-target.txt"), overwrite: true);

        Assert.That(File.ReadAllText(PathIn("n-target.txt")), Is.EqualTo("source"));
    }

    [Test]
    public void Copy_LeavesTheSourceInPlace()
    {
        File.WriteAllText(PathIn("o.txt"), "content");

        ExtensionFileSystem.Copy(UriIn("o.txt"), UriIn("o-copy.txt"), overwrite: false);

        Assert.That(File.ReadAllText(PathIn("o.txt")), Is.EqualTo("content"));
        Assert.That(File.ReadAllText(PathIn("o-copy.txt")), Is.EqualTo("content"));
    }

    [Test]
    public void Copy_RefusesToClobberWithoutOverwrite()
    {
        File.WriteAllText(PathIn("p.txt"), "source");
        File.WriteAllText(PathIn("p-target.txt"), "PRECIOUS");

        Assert.That(() => ExtensionFileSystem.Copy(UriIn("p.txt"), UriIn("p-target.txt"), overwrite: false),
            Throws.Exception);
        Assert.That(File.ReadAllText(PathIn("p-target.txt")), Is.EqualTo("PRECIOUS"));
    }

    // ------------------------------------------------------------------ URI handling

    /// <summary>
    /// Every uri on this wire is a <c>file://</c> string. Treating it as a path would look for a
    /// literal directory named "file:" — so each operation must convert, and a bare path (which a
    /// test or a future caller may pass) must still work.
    /// </summary>
    [Test]
    public void Operations_AcceptABarePathAsWellAsAFileUri()
    {
        File.WriteAllText(PathIn("q.txt"), "either way");

        Assert.That(UnB64(ExtensionFileSystem.ReadFile(UriIn("q.txt"))), Is.EqualTo("either way"));
        Assert.That(UnB64(ExtensionFileSystem.ReadFile(PathIn("q.txt"))), Is.EqualTo("either way"));
    }

    /// <summary>A URI with a space arrives percent-encoded and must decode back to the real path.</summary>
    [Test]
    public void Operations_DecodeAPercentEncodedUri()
    {
        File.WriteAllText(PathIn("has space.txt"), "spaced");

        var uri = ExtensionHost.ToDocumentUri(PathIn("has space.txt"));

        Assert.That(uri, Does.Contain("%20"), "sanity: the URI really is percent-encoded");
        Assert.That(UnB64(ExtensionFileSystem.ReadFile(uri)), Is.EqualTo("spaced"));
    }
}
