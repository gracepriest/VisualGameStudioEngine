using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests;

[TestFixture]
public class NewProjectWizardSwapGuardTests
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

    [Test]
    public void NewProjectAsync_OpensNewWizard_NotOldCreateProjectView()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping swap guard.");
            return;
        }

        var src = File.ReadAllText(path);
        Assert.That(src, Does.Contain("NewProjectSelectView"),
            "NewProjectAsync must open the new wizard's select window.");
        Assert.That(src, Does.Not.Contain("new Views.Dialogs.CreateProjectView"),
            "The app must no longer construct the old CreateProjectView.");
    }

    [Test]
    public void DeadNewProjectDialog_IsDeleted()
    {
        Assert.That(FindRepoFile("VisualGameStudio.Shell", "Views", "Dialogs", "NewProjectDialog.axaml"),
            Is.Null, "The dead NewProjectDialog should be removed.");
        Assert.That(FindRepoFile("VisualGameStudio.Shell", "ViewModels", "Dialogs", "NewProjectViewModel.cs"),
            Is.Null, "The dead NewProjectViewModel should be removed.");
    }
}
