using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Discovery is re-entrant: <c>DiscoverExtensionsAsync</c> runs on startup AND after every install.
/// It cleared <c>_extensions</c> but none of the five indexes derived from it, and the keybinding
/// and menu registrations append unconditionally — so each pass stacked another copy of every
/// contribution on top of the last.
///
/// <para>Pre-existing, but latent while manifests were failing to bind: an extension that never
/// loaded contributed nothing to duplicate. Fixing the binding is what makes it observable, so it
/// is fixed alongside rather than left to look like a regression.</para>
///
/// <para>These are real behavioural tests, not source guards — the extensions root is injectable as
/// of this change, so they run against a temp directory instead of the developer's ~/.vgs.</para>
/// </summary>
[TestFixture]
public class ExtensionDiscoveryReentrancyTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "vgs-discovery-" + Path.GetRandomFileName());
        var ext = Path.Combine(_root, "extensions", "acme.sample-1.0.0");
        Directory.CreateDirectory(ext);

        File.WriteAllText(Path.Combine(ext, "package.json"), """
            {
              "name": "sample",
              "displayName": "Sample",
              "version": "1.0.0",
              "publisher": "acme",
              "activationEvents": [ "onLanguage:basiclang" ],
              "contributes": {
                "commands": [ { "command": "sample.run", "title": "Run Sample" } ],
                "keybindings": [ { "command": "sample.run", "key": "ctrl+alt+r" } ]
              }
            }
            """);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir */ }
    }

    private ExtensionService NewService() =>
        new(Mock.Of<IOutputService>(), extensionsRoot: _root);

    [Test]
    public async Task RediscoveringDoesNotDuplicateKeybindings()
    {
        using var service = NewService();

        await service.DiscoverExtensionsAsync();
        var afterFirst = service.GetContributedKeybindings().Count;

        await service.DiscoverExtensionsAsync();

        Assert.That(afterFirst, Is.EqualTo(1), "sanity: the fixture contributes exactly one keybinding");
        Assert.That(service.GetContributedKeybindings(), Has.Count.EqualTo(1),
            "discovery runs again after every install, so an appending registration stacks a "
            + "duplicate binding on each pass");
    }

    [Test]
    public async Task RediscoveringDoesNotDuplicateExtensions()
    {
        using var service = NewService();

        await service.DiscoverExtensionsAsync();
        await service.DiscoverExtensionsAsync();

        var extensions = await service.DiscoverExtensionsAsync();

        Assert.That(extensions, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// An extension removed from disk must stop being activatable. The activation index keyed
    /// <c>onLanguage:basiclang</c> to its id and was never cleared, so a rediscovery after uninstall
    /// left the id behind — a later activation would then look up an extension that no longer exists.
    /// </summary>
    [Test]
    public async Task RediscoveringAfterRemoval_ForgetsTheOldExtension()
    {
        using var service = NewService();

        await service.DiscoverExtensionsAsync();
        Assert.That(service.GetContributedCommands(), Has.Count.EqualTo(1), "sanity");

        Directory.Delete(Path.Combine(_root, "extensions", "acme.sample-1.0.0"), recursive: true);
        await service.DiscoverExtensionsAsync();

        Assert.That(service.GetContributedCommands(), Is.Empty,
            "a command from an extension that is no longer installed must not stay registered");
        Assert.That(service.GetContributedKeybindings(), Is.Empty);
    }
}
