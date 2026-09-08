using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins what <see cref="VsixInstaller"/> actually does, before it is promoted onto the install path.
///
/// <para>It is the most complete installer in the repo and has never executed in production — it is
/// not DI-registered, and a repo-wide search finds no test that touched it before this file.
/// Consolidating onto it means putting ~620 lines of never-run code on the one path a user cares
/// about, which is precisely the shape that produced twenty never-executed extension bugs. These
/// tests are the safety net that has to exist first.</para>
///
/// <para>Its constructor takes an extensions directory, so every test installs into a temp root and
/// never touches the developer's real <c>~/.vgs/extensions</c>.</para>
/// </summary>
[TestFixture]
public class VsixInstallerCharacterizationTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "vgs-vsix-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { Thread.Sleep(250); try { Directory.Delete(_root, recursive: true); } catch { } }
    }

    private VsixInstaller NewInstaller() => new(vsxClient: null, extensionsDir: _root);

    /// <summary>
    /// Writes a .vsix — which is just a zip — with its manifest under <c>extension/</c>, the layout
    /// Open VSX actually ships.
    /// </summary>
    private string MakeVsix(string manifestJson, string? extraFile = null, string? entryPrefix = "extension/")
    {
        var path = Path.Combine(_root, $"fixture-{Guid.NewGuid():N}.vsix");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        Write(archive, $"{entryPrefix}package.json", manifestJson);
        if (extraFile != null) Write(archive, $"{entryPrefix}{extraFile}", "// content");

        return path;

        static void Write(ZipArchive a, string entryName, string content)
        {
            using var writer = new StreamWriter(a.CreateEntry(entryName).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static string Manifest(string publisher = "acme", string name = "sample", string version = "1.0.0") =>
        $$"""
        { "name": "{{name}}", "publisher": "{{publisher}}", "version": "{{version}}",
          "displayName": "Sample", "engines": { "vscode": "^1.75.0" } }
        """;

    // ------------------------------------------------------------------ layout

    /// <summary>
    /// THE layout invariant. The VSIX nests content under <c>extension/</c>, but the installed
    /// directory must be FLAT with package.json at its root — that is what discovery scans for and
    /// what makes uninstall a single directory delete. Getting this wrong yields an extension that
    /// installs "successfully" and is then invisible to discovery.
    /// </summary>
    [Test]
    public async Task Install_FlattensTheExtensionSubdirectory()
    {
        var vsix = MakeVsix(Manifest(), extraFile: "main.js");

        var info = await NewInstaller().InstallVsixAsync(vsix);

        Assert.That(File.Exists(Path.Combine(info.InstallPath, "package.json")), Is.True,
            "package.json must sit at the root of the install directory, not under extension/");
        Assert.That(File.Exists(Path.Combine(info.InstallPath, "main.js")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(info.InstallPath, "extension")), Is.False,
            "the extension/ wrapper must not survive into the install directory");
    }

    /// <summary>A VSIX with its manifest at the root is equally valid and must also work.</summary>
    [Test]
    public async Task Install_AcceptsARootLevelManifest()
    {
        var vsix = MakeVsix(Manifest(), entryPrefix: "");

        var info = await NewInstaller().InstallVsixAsync(vsix);

        Assert.That(File.Exists(Path.Combine(info.InstallPath, "package.json")), Is.True);
    }

    /// <summary>
    /// ⚠ Directory naming comes from the MANIFEST, not from registry search metadata. The panel
    /// currently names it from the registry, so for any extension whose manifest disagrees the first
    /// post-migration install lands somewhere different and the old directory survives — two
    /// directories claiming one id. Pinned so that change is deliberate rather than discovered.
    /// </summary>
    [Test]
    public async Task Install_NamesTheDirectoryFromTheManifest()
    {
        var vsix = MakeVsix(Manifest(publisher: "acme", name: "sample", version: "2.5.1"));

        var info = await NewInstaller().InstallVsixAsync(vsix);

        Assert.That(Path.GetFileName(info.InstallPath), Is.EqualTo("acme.sample-2.5.1"));
        Assert.That(info.Id, Is.EqualTo("acme.sample"));
        Assert.That(info.Version, Is.EqualTo("2.5.1"));
    }

    // -------------------------------------------------------------- validation

    /// <summary>
    /// Each required field fails with its OWN message. The panel does not open the manifest at all
    /// today, so these rejections are new behaviour: a VSIX that installed before may now be
    /// refused. That is correct — but it must be a stated change, not a surprise.
    /// </summary>
    [TestCase("""{ "publisher": "acme", "version": "1.0.0" }""", "name")]
    [TestCase("""{ "name": "sample", "version": "1.0.0" }""", "publisher")]
    [TestCase("""{ "name": "sample", "publisher": "acme" }""", "version")]
    public void Install_RejectsAManifestMissingARequiredField(string manifestJson, string field)
    {
        var vsix = MakeVsix(manifestJson);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await NewInstaller().InstallVsixAsync(vsix));

        Assert.That(ex!.Message, Does.Contain(field),
            "the message must name the offending field — 'invalid package.json' alone leaves the "
            + "user with nothing to act on");
    }

    /// <summary>An archive with no manifest anywhere is not an extension.</summary>
    [Test]
    public void Install_RejectsAnArchiveWithNoManifest()
    {
        var path = Path.Combine(_root, "empty.vsix");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("readme.txt");
        }

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await NewInstaller().InstallVsixAsync(path));

        Assert.That(ex!.Message, Does.Contain("package.json"));
    }

    [Test]
    public void Install_RejectsAMissingFile()
    {
        Assert.ThrowsAsync<FileNotFoundException>(
            async () => await NewInstaller().InstallVsixAsync(Path.Combine(_root, "nope.vsix")));
    }

    // ----------------------------------------------------------- version sweep

    /// <summary>
    /// Installing a new version must remove the old one. Two directories claiming one extension id
    /// is the state that makes discovery log "Ignoring duplicate" and silently drop one of them.
    /// </summary>
    [Test]
    public async Task Install_RemovesAnOlderVersionOfTheSameExtension()
    {
        var installer = NewInstaller();

        await installer.InstallVsixAsync(MakeVsix(Manifest(version: "1.0.0")));
        var updated = await installer.InstallVsixAsync(MakeVsix(Manifest(version: "2.0.0")));

        Assert.That(Directory.Exists(Path.Combine(_root, "acme.sample-1.0.0")), Is.False,
            "the superseded version must not survive alongside the new one");
        Assert.That(Directory.Exists(updated.InstallPath), Is.True);
    }

    /// <summary>The sweep must not touch a DIFFERENT extension that merely installed nearby.</summary>
    [Test]
    public async Task Install_LeavesOtherExtensionsAlone()
    {
        var installer = NewInstaller();

        var other = await installer.InstallVsixAsync(MakeVsix(Manifest(name: "other")));
        await installer.InstallVsixAsync(MakeVsix(Manifest(name: "sample", version: "3.0.0")));

        Assert.That(Directory.Exists(other.InstallPath), Is.True,
            "the old-version sweep keys on extension id and must not reach past it");
    }

    /// <summary>
    /// Reinstalling the SAME version replaces cleanly rather than merging into what is already
    /// there. This is the behaviour the panel's own installer gets wrong: it swallows a failed
    /// delete and then copies over the survivors, producing a mixed-version install reported as
    /// success. Here the delete is unguarded, so a failure surfaces instead — pinned because it is
    /// the single strongest argument for the migration.
    /// </summary>
    [Test]
    public async Task Install_ReplacesTheSameVersionRatherThanMergingIntoIt()
    {
        var installer = NewInstaller();

        await installer.InstallVsixAsync(MakeVsix(Manifest(), extraFile: "stale.js"));
        var reinstalled = await installer.InstallVsixAsync(MakeVsix(Manifest(), extraFile: "fresh.js"));

        Assert.That(File.Exists(Path.Combine(reinstalled.InstallPath, "fresh.js")), Is.True);
        Assert.That(File.Exists(Path.Combine(reinstalled.InstallPath, "stale.js")), Is.False,
            "a file from the previous install surviving into the new one IS the mixed-version bug");
    }
}
