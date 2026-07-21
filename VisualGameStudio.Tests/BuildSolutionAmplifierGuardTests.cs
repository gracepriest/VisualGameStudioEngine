using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins Task 17 — the "Build Solution" command must raise exactly one
/// <c>BuildCompleted</c> event per gesture, same as the <c>BuildAsync</c> command's combined
/// path. Previously the command looped <c>_buildService.BuildProjectAsync(project)</c> once
/// per project in the solution; <c>BuildService.BuildProjectAsync</c> fires its own
/// <c>BuildCompleted</c> per call (BuildService.cs ~:384), so a solution of N projects produced
/// N quiet toasts (Task 16) + N Output reveals from a single "Build Solution" click. The fix
/// swaps the loop for the single combined <c>_buildService.BuildSolutionAsync(solution)</c> call
/// (BuildService.cs ~:54), which aggregates all projects into one <c>BuildResult</c> and fires
/// <c>BuildCompleted</c> exactly once (BuildService.cs ~:174).
///
/// <see cref="VisualGameStudio.Shell.ViewModels.MainWindowViewModel"/> is DI-only and never
/// constructed in this suite (~40 services). This guard reads the source text directly and
/// brace-matches the command body, mirroring NewProjectWizardSwapGuardTests.cs /
/// ToastAutoDismissTests.cs's FindRepoFile + ExtractMethodBody pattern.
/// </summary>
[TestFixture]
public class BuildSolutionAmplifierGuardTests
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

    /// <summary>Extracts a method's full body (braces included) by brace-depth scanning, so the
    /// guard doesn't depend on which method happens to follow it in the file.</summary>
    private static string ExtractMethodBody(string src, string methodSignatureNeedle)
    {
        var startIdx = src.IndexOf(methodSignatureNeedle, StringComparison.Ordinal);
        Assert.That(startIdx, Is.GreaterThanOrEqualTo(0), $"'{methodSignatureNeedle}' not found in source.");

        var braceStart = src.IndexOf('{', startIdx);
        Assert.That(braceStart, Is.GreaterThan(startIdx), "Could not find the method body's opening brace.");

        var depth = 0;
        var i = braceStart;
        for (; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }
        Assert.That(i, Is.LessThan(src.Length), "Could not find the method body's closing brace.");
        return src.Substring(braceStart, i - braceStart + 1);
    }

    [Test]
    public void BuildSolutionCommand_DoesNotLoopBuildProjectAsync()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task BuildSolutionAsync()");

        Assert.That(body, Does.Not.Contain("BuildProjectAsync("),
            "The 'Build Solution' command must not loop _buildService.BuildProjectAsync(project) per " +
            "project — BuildService fires a per-call BuildCompleted (BuildService.cs ~:384), so an " +
            "N-project loop raises BuildCompleted N times from a single gesture (N quiet toasts + N " +
            "Output reveals). Use the combined _buildService.BuildSolutionAsync(solution) call instead " +
            "(BuildService.cs ~:54), which fires BuildCompleted exactly once (~:174).");

        Assert.That(body, Does.Match(@"_buildService\.BuildSolutionAsync\("),
            "The 'Build Solution' command must call the combined _buildService.BuildSolutionAsync(...) " +
            "so all projects build under a single BuildCompleted event.");
    }

    [Test]
    public void BuildSolutionCommand_StillGuardsNoSolutionOpen()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task BuildSolutionAsync()");

        Assert.That(body, Does.Contain("HasSolution"),
            "The 'Build Solution' command must still guard against no solution being open.");
        Assert.That(body, Does.Contain("\"No solution is open.\""),
            "The no-solution guard message must be preserved.");
    }

    [Test]
    public void BuildSolutionCommand_StillPairsProgressNotification()
    {
        var src = ReadMainWindowViewModelSource();
        if (src == null) return;

        var body = ExtractMethodBody(src, "private async Task BuildSolutionAsync()");

        Assert.That(body, Does.Contain("ShowProgressNotification("),
            "The 'Build Solution' command must still show a progress notification while building.");
        Assert.That(body, Does.Contain("DismissNotification("),
            "The 'Build Solution' command must still dismiss its progress notification once the build completes.");
    }
}
