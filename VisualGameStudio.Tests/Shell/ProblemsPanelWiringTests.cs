using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using VisualGameStudio.Shell.Configuration;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Guards the Problems panel's wiring. It shipped with the same defect as the Extensions
/// panel (see ExtensionsPanelWiringTests): <c>ProblemsViewModel</c> was never constructed,
/// and <c>MainWindowViewModel</c> omitted the optional, null-defaulting <c>problems:</c>
/// argument to <c>SetViewModels(...)</c>, leaving <c>ProblemsView</c> with a null DataContext.
///
/// Problems has a second failure mode Extensions does not: it is push-fed. Even correctly
/// constructed and bound, it stays permanently empty unless something calls
/// <c>ReplaceAllDiagnostics</c>. The ErrorList is fed from
/// <c>_diagnosticsAggregator.GetSnapshot()</c> at three sites (LSP publish, project close,
/// build completion); Problems must be fed at every one of them, which the drift guard below
/// enforces by counting rather than by spot-checking any single site.
/// </summary>
[TestFixture]
public class ProblemsPanelWiringTests
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

    private static string? ReadMainWindowViewModelSource()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping source guard.");
            return null;
        }
        return File.ReadAllText(path);
    }

    [Test]
    public void ProblemsViewModel_IsRegisteredInTheContainer()
    {
        var services = new ServiceCollection();
        services.ConfigureServices();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ProblemsViewModel));

        Assert.That(descriptor, Is.Not.Null,
            "ProblemsViewModel must be registered in ServiceConfiguration — without it the Problems " +
            "panel resolves a null ViewModel and every binding in ProblemsView fails.");
        Assert.That(descriptor!.Lifetime, Is.EqualTo(ServiceLifetime.Singleton),
            "Panel ViewModels are singletons so the panel keeps its state across dock show/hide.");
    }

    [Test]
    public void SetViewModelsCallSite_PassesTheProblemsViewModel()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var callIdx = src.IndexOf("_dockFactory.SetViewModels(", StringComparison.Ordinal);
        Assert.That(callIdx, Is.GreaterThanOrEqualTo(0),
            "Could not find the _dockFactory.SetViewModels(...) call site.");

        var callEnd = src.IndexOf(");", callIdx, StringComparison.Ordinal);
        Assert.That(callEnd, Is.GreaterThan(callIdx), "Could not find the end of the SetViewModels call.");

        var call = src.Substring(callIdx, callEnd - callIdx);

        Assert.That(call, Does.Contain("problems:"),
            "MainWindowViewModel must pass problems: to SetViewModels. The parameter is optional and " +
            "defaults to null (DockFactory.cs ~:130), so omitting it compiles cleanly and silently " +
            "leaves the Problems panel with a null DataContext.");
    }

    /// <summary>
    /// Drift guard. A panel that is bound but never fed is indistinguishable from a working
    /// panel on a clean project, so this counts feed sites instead of checking one: every
    /// place that pushes an aggregator snapshot into the ErrorList must push the same
    /// snapshot into Problems.
    /// </summary>
    [Test]
    public void EveryErrorListFeedSite_AlsoFeedsTheProblemsPanel()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var errorListFeeds = Regex.Matches(src, @"ErrorList\.UpdateDiagnostics\(").Count;
        var problemsFeeds = Regex.Matches(src, @"Problems\.(ReplaceAllDiagnostics|UpdateDiagnostics)\(").Count;

        Assert.That(errorListFeeds, Is.GreaterThan(0),
            "Expected at least one ErrorList.UpdateDiagnostics(...) call — the guard is meaningless without one.");
        Assert.That(problemsFeeds, Is.EqualTo(errorListFeeds),
            $"Found {errorListFeeds} ErrorList feed site(s) but {problemsFeeds} Problems feed site(s). " +
            "Both panels render the same aggregator snapshot, so a diagnostic source added to one " +
            "must be added to the other — otherwise Problems silently under-reports.");
    }
}
