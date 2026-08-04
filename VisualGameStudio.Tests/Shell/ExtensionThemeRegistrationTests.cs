using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Guards the last link in the extension theme pipeline. Extensions' themes were parsed and
/// counted but never registered: ExtensionService.LoadThemesFromExtension called
/// TextMateService.LoadThemeFromJson, set the returned theme's Type, incremented a counter for
/// the Output log, and let the object go out of scope. The counter therefore reported work
/// ATTEMPTED as work COMPLETED — "1 theme(s)" in the log while nothing became selectable.
///
/// The registry (ThemeManager) lives in the Shell and the parser in ProjectSystem, which the
/// Shell references — so ExtensionService cannot call ThemeManager directly. The fix surfaces
/// each theme file's path on the existing (previously unconsumed) ContributionsLoaded event and
/// registers them Shell-side, keeping the dependency direction intact.
/// </summary>
[TestFixture]
public class ExtensionThemeRegistrationTests
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
    /// The event must be able to carry the paths at all, and must never hand a null list to a
    /// subscriber that will foreach over it.
    /// </summary>
    [Test]
    public void ContributionsLoadedEventArgs_ExposesThemeFilePaths()
    {
        var args = new ExtensionContributionsLoadedEventArgs(new Extension { Id = "test.ext" });

        Assert.That(args.ThemeFilePaths, Is.Not.Null, "subscribers enumerate this without a null check");
        Assert.That(args.ThemeFilePaths, Is.Empty);

        args.ThemeFilePaths.Add("C:/x/theme.json");
        Assert.That(args.ThemeFilePaths, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Behavioural: the registration mechanism the fix depends on genuinely makes a theme
    /// selectable. ThemeManager.LoadVsCodeThemeFileAsync is the same call the file-picker import
    /// command uses, and is headless-safe (only Apply touches Application.Current).
    /// </summary>
    [Test]
    public async Task LoadVsCodeThemeFile_MakesTheThemeSelectable()
    {
        var themeName = $"ExtThemeTest_{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), $"{themeName}.json");
        await File.WriteAllTextAsync(path,
            $"{{ \"name\": \"{themeName}\", \"type\": \"dark\", \"colors\": {{}} }}");

        try
        {
            var label = await ThemeManager.LoadVsCodeThemeFileAsync(path);

            Assert.That(label, Is.Not.Null, "a valid VS Code theme file must load");
            Assert.That(ThemeManager.ExtensionThemeNames, Does.Contain(themeName),
                "a registered theme must appear among the selectable theme names — this is the step " +
                "the extension path was missing entirely.");
            Assert.That(ThemeManager.IsKnownTheme(themeName), Is.True,
                "and it must resolve as a known theme so a saved selection survives a restart.");
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Source guard: ExtensionService must hand the collected paths out on the event. Its
    /// extensions directory is hard-coded to ~/.vgs/extensions, so a behavioural test at that
    /// layer would write into the developer's real profile.
    /// </summary>
    [Test]
    public void ExtensionService_SurfacesThemeFilePathsOnTheEvent()
    {
        var path = FindRepoFile("VisualGameStudio.ProjectSystem", "Services", "ExtensionService.cs");
        if (path == null)
        {
            Assert.Ignore("ExtensionService.cs not found from the test base directory — skipping source guard.");
            return;
        }

        var src = File.ReadAllText(path);

        Assert.That(src, Does.Contain("LoadThemesFromExtension(extension, stats.ThemeFilePaths)"),
            "the theme loader must record each resolved theme file on the event args, otherwise the " +
            "Shell has nothing to register and themes are silently dropped again.");
    }

    /// <summary>
    /// Source guard: the Shell must actually consume the event. ContributionsLoaded existed and
    /// was fired for a long time with no subscriber at all — declaring it is not wiring it.
    /// </summary>
    [Test]
    public void MainWindowViewModel_SubscribesAndRegistersExtensionThemes()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping source guard.");
            return;
        }

        var src = File.ReadAllText(path);

        Assert.That(src, Does.Contain("ContributionsLoaded += OnExtensionContributionsLoaded"),
            "the Shell must subscribe to ContributionsLoaded — the event was fired with no listener before.");
        Assert.That(src, Does.Contain("ThemeManager.LoadVsCodeThemeFileAsync("),
            "and the handler must register each contributed theme with ThemeManager.");
    }
}
