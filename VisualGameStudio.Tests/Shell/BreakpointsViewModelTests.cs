using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Shell;

[TestFixture]
public class BreakpointsViewModelTests
{
    /// <summary>
    /// The line-breakpoint Enable/Disable menu items had no Command at all, and the row checkbox
    /// toggled only the item's own property without syncing/saving. One command serves both.
    /// </summary>
    [Test]
    public void ToggleLineBreakpointCommand_FlipsEnabled_AndRaisesChanged()
    {
        var vm = new BreakpointsViewModel(Mock.Of<IDebugService>());
        vm.AddBreakpoint("Main.bas", 12);
        var breakpoint = vm.Breakpoints.Single();

        var changed = 0;
        vm.BreakpointsChanged += (_, _) => changed++;

        vm.ToggleLineBreakpointCommand.Execute(breakpoint);
        Assert.Multiple(() =>
        {
            Assert.That(breakpoint.IsEnabled, Is.False);
            Assert.That(changed, Is.EqualTo(1), "the debugger and editor margins need the change notification");
        });

        vm.ToggleLineBreakpointCommand.Execute(breakpoint);
        Assert.Multiple(() =>
        {
            Assert.That(breakpoint.IsEnabled, Is.True);
            Assert.That(changed, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// The command existing is not enough: all three line-breakpoint enable paths (the two
    /// context-menu items and the row checkbox) must actually bind to it.
    /// </summary>
    [Test]
    public void BreakpointsView_WiresTheLineBreakpointToggleCommand()
    {
        var axaml = FindRepoFile("VisualGameStudio.Shell", "Views", "Panels", "BreakpointsView.axaml");
        if (axaml == null)
        {
            Assert.Ignore("BreakpointsView.axaml not found from the test base directory.");
            return;
        }

        var text = File.ReadAllText(axaml);
        var bindings = Regex.Matches(text, "ToggleLineBreakpointCommand").Count;
        Assert.That(bindings, Is.GreaterThanOrEqualTo(3),
            "Enable, Disable, and the row checkbox must all invoke the command");
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
