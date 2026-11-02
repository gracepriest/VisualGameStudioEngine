using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Shell;

[TestFixture]
public class SolutionExplorerContextMenuTests
{
    /// <summary>
    /// The context-menu commands (Open in Explorer, Open in Terminal, Copy Path, Rename, ...)
    /// read SelectedNode. A secondary click must set that selection before the menu opens.
    /// </summary>
    [Test]
    public void SolutionExplorer_SelectsTheRightClickedNode()
    {
        var axaml = FindRepoFile("VisualGameStudio.Shell", "Views", "Panels", "SolutionExplorerView.axaml");
        var code = FindRepoFile("VisualGameStudio.Shell", "Views", "Panels", "SolutionExplorerView.axaml.cs");
        var viewModel = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "Panels", "SolutionExplorerViewModel.cs");
        if (axaml == null || code == null || viewModel == null)
        {
            Assert.Ignore("SolutionExplorerView files not found from the test base directory.");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(axaml), Does.Contain("PointerPressed=\"OnTreeViewPointerPressed\""),
                "right-clicking a tree item must run the selection handler");
            Assert.That(File.ReadAllText(code), Does.Contain("OnTreeViewPointerPressed"),
                "and the handler must exist");
            Assert.That(File.ReadAllText(viewModel), Does.Contain("File.Exists(path)"),
                "project/solution nodes are files even though TreeNode.IsFile is false");
        });
    }

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
}
