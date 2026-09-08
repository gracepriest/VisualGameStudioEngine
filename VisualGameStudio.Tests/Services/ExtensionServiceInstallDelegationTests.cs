using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins the split between the two halves of an install once <see cref="ExtensionService"/> delegates
/// to <see cref="VsixInstaller"/>.
///
/// <para>Three implementations of "install a .vsix" existed side by side — this service's inline
/// path, VsixInstaller, and the extensions panel's own copy — and the asymmetry between them is what
/// decides ownership. VsixInstaller is ACQUISITION ONLY: it extracts, validates, copies and records,
/// and it never loads a contribution, never activates, never speaks to the host. ExtensionService is
/// the only thing that can make an extension LIVE. So the seam is acquisition vs runtime, and each
/// side gets exactly one owner.</para>
///
/// <para>⛔ The failure mode these tests exist for is DOUBLE REGISTRATION. InstallFromFileAsync
/// already loads contributions and may activate; a caller that then also runs discovery would
/// register the same extension twice, and the symptom (duplicated commands, keybindings fired twice)
/// appears far from the cause. <see cref="Install_RegistersTheExtensionExactlyOnce"/> is the guard
/// that has to keep passing through the delegation, not just after it.</para>
/// </summary>
[TestFixture]
public class ExtensionServiceInstallDelegationTests
{
    private string _root = null!;
    private string _extensionsDir = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "vgs-install-delegation", Guid.NewGuid().ToString("N"));
        // ExtensionService appends "extensions" to its root (ExtensionService.cs:115) while
        // VsixInstaller treats the path it is given AS the extensions directory, so the two are
        // pointed at the same physical folder by construction rather than by coincidence.
        _extensionsDir = Path.Combine(_root, "extensions");
        Directory.CreateDirectory(_extensionsDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { Thread.Sleep(250); try { Directory.Delete(_root, recursive: true); } catch { } }
    }

    private VsixInstaller NewInstaller() => new(vsxClient: null, extensionsDir: _extensionsDir);

    private ExtensionService NewService(VsixInstaller installer) =>
        new(Mock.Of<IOutputService>(), extensionsRoot: _root, vsixInstaller: installer);

    /// <summary>Builds a .vsix (a zip) with the manifest under <c>extension/</c>, as Open VSX ships them.</summary>
    private string MakeVsix(string manifestJson, string? extraFile = null)
    {
        var path = Path.Combine(_root, $"fixture-{Guid.NewGuid():N}.vsix");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        Write(archive, "extension/package.json", manifestJson);
        if (extraFile != null) Write(archive, $"extension/{extraFile}", "// content");

        return path;

        static void Write(ZipArchive a, string entryName, string content)
        {
            using var writer = new StreamWriter(a.CreateEntry(entryName).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static string Manifest(
        string publisher = "acme",
        string name = "sample",
        string version = "1.0.0",
        string contributes = """{ "commands": [ { "command": "sample.run", "title": "Run Sample" } ] }""") =>
        $$"""
        { "name": "{{name}}", {{(publisher.Length == 0 ? "" : $"\"publisher\": \"{publisher}\",")}}
          "version": "{{version}}", "displayName": "Sample",
          "engines": { "vscode": "^1.75.0" },
          "contributes": {{contributes}} }
        """;

    // ---------------------------------------------------------------- acquisition is delegated

    /// <summary>
    /// The acquisition layer must actually RUN, not merely be held in a field. Its state file
    /// (<c>extensions.json</c>) is the observable proof: the service's own inline path never writes
    /// it, so its presence can only mean VsixInstaller performed the install.
    ///
    /// <para>Asserting on the file rather than on a mock is deliberate — a mock would confirm a call
    /// was made, this confirms the install actually landed.</para>
    /// </summary>
    [Test]
    public async Task Install_RunsTheAcquisitionLayer()
    {
        using var service = NewService(NewInstaller());
        var vsix = MakeVsix(Manifest(), extraFile: "main.js");

        var result = await service.InstallFromFileAsync(vsix);

        Assert.That(result.Success, Is.True, result.Error);

        var stateFile = Path.Combine(_extensionsDir, "extensions.json");
        Assert.That(File.Exists(stateFile), Is.True,
            "extensions.json is written only by VsixInstaller — its absence means the inline path ran");
        Assert.That(File.ReadAllText(stateFile), Does.Contain("acme.sample"),
            "the acquisition layer recorded the install");
    }

    /// <summary>
    /// The installed directory must be FLAT — package.json at its root, no surviving
    /// <c>extension/</c> wrapper — because that is the shape discovery scans for. Both
    /// implementations claim to do this; delegation must not lose it.
    /// </summary>
    [Test]
    public async Task Install_LeavesAFlatInstallDirectoryDiscoveryCanSee()
    {
        using var service = NewService(NewInstaller());

        await service.InstallFromFileAsync(MakeVsix(Manifest(), extraFile: "main.js"));

        var installDir = Path.Combine(_extensionsDir, "acme.sample-1.0.0");
        Assert.That(File.Exists(Path.Combine(installDir, "package.json")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(installDir, "extension")), Is.False);

        var discovered = await service.DiscoverExtensionsAsync();
        Assert.That(discovered, Has.Count.EqualTo(1), "a fresh discovery pass must find the install");
    }

    // ---------------------------------------------------------------- runtime stays here

    /// <summary>
    /// ⛔ THE DOUBLE-REGISTRATION GUARD. Install performs runtime registration itself; nothing may
    /// register a second time. One contributed command in the manifest must yield exactly one
    /// contributed command after install.
    /// </summary>
    [Test]
    public async Task Install_RegistersTheExtensionExactlyOnce()
    {
        using var service = NewService(NewInstaller());
        var installedEvents = 0;
        service.ExtensionInstalled += (_, _) => installedEvents++;

        await service.InstallFromFileAsync(MakeVsix(Manifest()));

        Assert.That(installedEvents, Is.EqualTo(1), "ExtensionInstalled must fire once per install");
        Assert.That(service.GetExtension("acme.sample"), Is.Not.Null,
            "install registers the extension without needing a discovery pass");
        Assert.That(service.GetContributedCommands(), Has.Count.EqualTo(1),
            "one manifest command must not become two");
    }

    /// <summary>
    /// The runtime half is what delegation must NOT hand away: a purely static extension (no main)
    /// is marked active by the install path itself.
    /// </summary>
    [Test]
    public async Task Install_StillActivatesAPureStaticExtension()
    {
        using var service = NewService(NewInstaller());

        var result = await service.InstallFromFileAsync(MakeVsix(Manifest()));

        Assert.That(result.Extension, Is.Not.Null);
        Assert.That(result.Extension!.IsActive, Is.True,
            "a static-only extension is active the moment it is installed");
    }

    // ---------------------------------------------------------------- validation tightens

    /// <summary>
    /// A BEHAVIOUR CHANGE, stated rather than smuggled. The inline path required only a name, so a
    /// manifest with no publisher installed under the id <c>".sample"</c> — a leading-dot id that no
    /// activation event, command lookup or uninstall could ever match again. The acquisition layer
    /// requires publisher and version, so the install now fails loudly instead of succeeding into an
    /// unreachable state.
    /// </summary>
    [Test]
    public async Task Install_RejectsAManifestWithNoPublisher()
    {
        using var service = NewService(NewInstaller());

        var result = await service.InstallFromFileAsync(MakeVsix(Manifest(publisher: "")));

        Assert.That(result.Success, Is.False,
            "a publisher-less manifest previously installed as \".sample\" and was then unreachable");
        Assert.That(result.Error, Does.Contain("publisher"),
            "the reason must name the missing field, not just say the package was invalid");
        Assert.That(Directory.GetDirectories(_extensionsDir), Is.Empty,
            "a rejected install must leave nothing behind");
    }

    /// <summary>A missing file is still reported as a failed result, never an exception.</summary>
    [Test]
    public async Task Install_ReportsAMissingPackageAsAFailedResult()
    {
        using var service = NewService(NewInstaller());

        var result = await service.InstallFromFileAsync(Path.Combine(_root, "does-not-exist.vsix"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
    }
}
