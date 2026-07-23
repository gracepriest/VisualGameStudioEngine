using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Pins Feature A — "Close Project" is renamed from the Command-Palette-only
/// <c>CloseFolderAsync</c>/<c>CloseFolderCommand</c> to <c>CloseProjectAsync</c>/
/// <c>CloseProjectCommand</c>, surfaced on the File menu, and gated on a new
/// <c>HasProjectOpen</c> flag kept in sync from the project/solution open/close events.
///
/// <see cref="VisualGameStudio.Shell.ViewModels.MainWindowViewModel"/> is DI-only (~40 ctor
/// deps) and is never constructed in this suite, so this is a SOURCE GUARD — it reads the
/// source text directly, mirroring MwvmDebuggerOverrideSourceGuardTests.cs's FindRepoFile /
/// ReadSource pattern. Real runtime behavior is covered by a later manual smoke task.
/// </summary>
[TestFixture]
public class MainWindowCloseProjectSourceGuardTests
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

    private static string? ReadSource(params string[] relativeParts)
    {
        var path = FindRepoFile(relativeParts);
        if (path == null)
        {
            Assert.Ignore($"{Path.Combine(relativeParts)} not found from the test base directory — skipping source guard.");
            return null;
        }
        return File.ReadAllText(path);
    }

    [Test]
    public void MainWindowViewModel_renames_close_and_wires_HasProjectOpen()
    {
        var src = ReadSource("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (src == null) return;

        Assert.That(src, Does.Contain("CloseProjectAsync"));
        Assert.That(src, Does.Not.Contain("CloseFolderAsync"),
            "CloseFolderAsync must be renamed to CloseProjectAsync, not duplicated.");
        Assert.That(src, Does.Contain("HasProjectOpen"));
        Assert.That(src, Does.Contain("RecomputeHasProjectOpen"));
    }

    [Test]
    public void CommandPalette_targets_CloseProject_not_CloseFolder()
    {
        var src = ReadSource("VisualGameStudio.Shell", "ViewModels", "Dialogs", "CommandPaletteViewModel.cs");
        if (src == null) return;

        Assert.That(src, Does.Contain("\"Close Project\"").And.Contain("CloseProjectCommand"));
        Assert.That(src, Does.Not.Contain("CloseFolderCommand"));
        Assert.That(src, Does.Not.Contain("\"Close Folder\""));
    }
}
