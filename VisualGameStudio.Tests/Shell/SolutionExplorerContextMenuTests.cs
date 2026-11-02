using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Panels;
using VisualGameStudio.Shell.Views.Panels;

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

    /// <summary>
    /// The real view, driven by a real secondary click. This is the behavior the context-menu
    /// commands depend on; TreeNode.SelectedNode is not automatically updated by Avalonia.
    /// </summary>
    [AvaloniaTest]
    public void RightClickingAnItem_SelectsIt()
    {
        var vm = new SolutionExplorerViewModel(
            new Mock<IProjectService>().Object,
            new Mock<IFileService>().Object,
            new Mock<IDialogService>().Object,
            new Mock<ISolutionService>().Object);

        var node = new TreeNode
        {
            Name = "Main.bas",
            NodeType = TreeNodeType.SourceFile,
            FullPath = Path.Combine(Path.GetTempPath(), "Main.bas")
        };
        vm.Nodes.Add(node);

        var view = new SolutionExplorerView { DataContext = vm };
        var window = new Window { Content = view, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var item = view.GetVisualDescendants().OfType<TreeViewItem>().First();
        var at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.SelectedNode, Is.SameAs(node), "the context menu commands must see the clicked node");
        window.Close();
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
