using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using VisualGameStudio.Shell.Configuration;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Guards the Extensions panel's wiring. The panel shipped with ~600 lines of working
/// Open VSX search/install logic that was never reachable: <c>ExtensionsViewModel</c> was
/// never constructed anywhere in the repo, and <c>MainWindowViewModel</c>'s
/// <c>SetViewModels(...)</c> call omitted the optional, null-defaulting <c>extensions:</c>
/// parameter (DockFactory.cs ~:129). The result was
/// <c>ExtensionsTool.ViewModel == null</c> -> <c>ExtensionsView.DataContext == null</c> ->
/// every binding silently failed. Because a failed Avalonia binding yields UnsetValue and
/// <c>IsVisible</c> defaults to <c>true</c>, the dead panel rendered MORE chrome than a live
/// one: the "No extensions found." and "Installing..." overlays were both permanently
/// visible at once, which is a state a live ViewModel can never produce.
///
/// <see cref="VisualGameStudio.Shell.ViewModels.MainWindowViewModel"/> is DI-only and never
/// constructed in this suite (~40 services), so the call-site assertion is a source guard,
/// mirroring BuildSolutionAmplifierGuardTests.cs / NewProjectWizardSwapGuardTests.cs.
/// </summary>
[TestFixture]
public class ExtensionsPanelWiringTests
{
    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// The ViewModel must be container-registered. Asserted against the ServiceCollection
    /// descriptors rather than by resolving it: <c>ExtensionsViewModel</c>'s constructor
    /// mkdirs ~/.vgs/extensions and enumerates it, and a registration guard has no business
    /// touching the user's profile.
    /// </summary>
    [Test]
    public void ExtensionsViewModel_IsRegisteredInTheContainer()
    {
        var services = new ServiceCollection();
        services.ConfigureServices();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ExtensionsViewModel));

        Assert.That(descriptor, Is.Not.Null,
            "ExtensionsViewModel must be registered in ServiceConfiguration — without it the " +
            "Extensions panel resolves a null ViewModel and every binding in ExtensionsView fails.");
        Assert.That(descriptor!.Lifetime, Is.EqualTo(ServiceLifetime.Singleton),
            "Panel ViewModels are singletons (see the DocumentOutline/Bookmarks/Timeline registrations) " +
            "so the panel keeps its state across dock show/hide.");
    }

    /// <summary>
    /// The DockFactory hand-off must actually pass the ViewModel. Every parameter after
    /// <c>errorList</c> on <c>SetViewModels</c> is optional and null-defaulting, so omitting
    /// <c>extensions:</c> is not a compile error and produces no runtime exception — only a
    /// permanently inert panel.
    /// </summary>
    [Test]
    public void SetViewModelsCallSite_PassesTheExtensionsViewModel()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping source guard.");
            return;
        }

        var src = File.ReadAllText(path);

        var callIdx = src.IndexOf("_dockFactory.SetViewModels(", StringComparison.Ordinal);
        Assert.That(callIdx, Is.GreaterThanOrEqualTo(0),
            "Could not find the _dockFactory.SetViewModels(...) call site.");

        var callEnd = src.IndexOf(");", callIdx, StringComparison.Ordinal);
        Assert.That(callEnd, Is.GreaterThan(callIdx), "Could not find the end of the SetViewModels call.");

        var call = src.Substring(callIdx, callEnd - callIdx);

        Assert.That(call, Does.Contain("extensions:"),
            "MainWindowViewModel must pass extensions: to SetViewModels. The parameter is optional and " +
            "defaults to null (DockFactory.cs ~:129), so omitting it compiles cleanly and silently " +
            "leaves the Extensions panel with a null DataContext.");
    }

    /// <summary>
    /// The panel needs IExtensionService to install/activate; <c>SetExtensionService</c> is a
    /// post-construction hand-off (the ViewModel has a parameterless ctor for the designer),
    /// so DI registration alone does not wire it.
    /// </summary>
    [Test]
    public void MainWindowViewModel_HandsTheExtensionServiceToThePanel()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping source guard.");
            return;
        }

        var src = File.ReadAllText(path);

        Assert.That(src, Does.Contain("SetExtensionService("),
            "MainWindowViewModel must call extensions.SetExtensionService(extensionService) — otherwise " +
            "the panel's _extensionService stays null and Install silently skips discovery/activation.");
    }
}
