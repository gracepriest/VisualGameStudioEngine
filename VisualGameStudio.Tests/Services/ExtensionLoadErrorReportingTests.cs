using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Guards that dropped contribution sections are actually reported.
///
/// <para><c>ExtensionContributionsConverter</c> records a dropped section on
/// <c>ExtensionContributions.LoadErrors</c>, but recording is not reporting: if nothing drains that
/// list, isolation converts a loud whole-extension failure into permanent SILENT data loss, which
/// is strictly worse than the bug it replaces.</para>
///
/// <para>Asserted as a source guard because <c>ExtensionService._extensionsDir</c> is hard-coded to
/// <c>~/.vgs/extensions</c> and is not injectable, so a behavioural test at this layer would write
/// into the developer's real profile. Mirrors BuildSolutionAmplifierGuardTests.cs /
/// NewProjectWizardSwapGuardTests.cs.</para>
/// </summary>
[TestFixture]
public class ExtensionLoadErrorReportingTests
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

    private static string ExtensionServiceSource()
    {
        var path = FindRepoFile("VisualGameStudio.ProjectSystem", "Services", "ExtensionService.cs");
        Assert.That(path, Is.Not.Null, "could not locate ExtensionService.cs from the test output dir");
        return File.ReadAllText(path!);
    }

    [Test]
    public void DroppedSections_AreDrainedToTheOutputPane()
    {
        Assert.That(ExtensionServiceSource(), Does.Contain("LoadErrors"),
            "nothing reads ExtensionContributions.LoadErrors, so a dropped contribution section "
            + "would vanish without a trace");
    }

    /// <summary>
    /// The drain must sit in the manifest-load path, NOT in <c>LoadContributionsAsync</c>.
    ///
    /// <para>That method's Output line lives inside <c>if (total &gt; 0)</c>, where <c>total</c>
    /// counts only successfully loaded themes/grammars/snippets/commands/keybindings. An extension
    /// whose ONLY contribution was the dropped one has <c>total == 0</c> — precisely the case where
    /// silence is worst. A warning gated on a success counter cannot report a total failure.</para>
    /// </summary>
    [Test]
    public void TheDrain_IsNotGatedOnASuccessCounter()
    {
        var source = ExtensionServiceSource();

        var loadFromDirectory = source.IndexOf("LoadExtensionFromDirectoryAsync", StringComparison.Ordinal);
        var loadContributions = source.IndexOf("private async Task LoadContributionsAsync", StringComparison.Ordinal);
        var drain = source.IndexOf("LoadErrors", StringComparison.Ordinal);

        Assert.That(loadFromDirectory, Is.GreaterThan(-1));
        Assert.That(loadContributions, Is.GreaterThan(-1));
        Assert.That(drain, Is.GreaterThan(-1), "no drain found at all");

        Assert.That(drain, Is.GreaterThan(loadFromDirectory).And.LessThan(loadContributions),
            "the drain must live in LoadExtensionFromDirectoryAsync, where the manifest is bound — "
            + "not in LoadContributionsAsync, whose reporting is gated on if (total > 0)");
    }
}
