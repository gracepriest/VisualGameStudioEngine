using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Xml.Linq;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// A menu entry with a broken binding is invisible dead weight: Avalonia does not fail the build
/// for a missing command property, so the item renders, does nothing, and has no diagnostic.
/// This walks every static MenuItem in the main menu bar and proves its command property exists.
/// </summary>
[TestFixture]
public class MainMenuWiringTests
{
    [Test]
    public void EveryMainMenuItem_WithACommand_HasThatCommandOnTheViewModel()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "Views", "MainWindow.axaml");
        if (path == null)
        {
            Assert.Ignore("MainWindow.axaml not found from the test base directory.");
            return;
        }

        var document = XDocument.Load(path);
        var commands = document.Descendants()
            .Where(e => e.Name.LocalName == "MenuItem")
            .Select(e => (string?)e.Attribute("Command"))
            .OfType<string>()
            .Select(text => Regex.Match(text, @"^\{Binding\s+([A-Za-z_][A-Za-z0-9_]*Command)\s*\}$"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.That(commands, Is.Not.Empty, "the main menu must contain bound commands");

        var missing = commands
            .Where(name =>
            {
                var property = typeof(MainWindowViewModel).GetProperty(name);
                return property == null || !typeof(ICommand).IsAssignableFrom(property.PropertyType);
            })
            .ToList();

        Assert.That(missing, Is.Empty,
            "these menu bindings name no ICommand on MainWindowViewModel: " + string.Join(", ", missing));
    }

    [Test]
    public void EveryLeafMainMenuItem_HasACommandClickOrItemsSource()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "Views", "MainWindow.axaml");
        if (path == null)
        {
            Assert.Ignore("MainWindow.axaml not found from the test base directory.");
            return;
        }

        var document = XDocument.Load(path);
        var dead = document.Descendants()
            .Where(e => e.Name.LocalName == "MenuItem")
            .Where(e => e.Attribute("Command") == null &&
                        e.Attribute("Click") == null &&
                        e.Attribute("ItemsSource") == null &&
                        !e.Elements().Any(child => child.Name.LocalName == "MenuItem"))
            .Select(e => (string?)e.Attribute("Header") ?? "(no header)")
            .ToList();

        Assert.That(dead, Is.Empty,
            "these menu leaves have neither a command nor a click handler: " + string.Join(", ", dead));
    }

    [Test]
    public void EveryLeafMenuItem_InEveryView_HasACommandClickOrItemsSource()
    {
        var root = FindRepoRoot();
        if (root == null)
        {
            Assert.Ignore("Repository root not found from the test base directory.");
            return;
        }

        var dead = new System.Collections.Generic.List<string>();
        foreach (var view in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (view.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                view.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var document = XDocument.Load(view);
            dead.AddRange(document.Descendants()
                .Where(e => e.Name.LocalName == "MenuItem")
                .Where(e => e.Attribute("Command") == null &&
                            e.Attribute("Click") == null &&
                            e.Attribute("ItemsSource") == null &&
                            !e.Elements().Any(child => child.Name.LocalName == "MenuItem"))
                .Select(e => $"{Path.GetFileName(view)}: {(string?)e.Attribute("Header") ?? "(no header)"}"));
        }

        Assert.That(dead, Is.Empty,
            "these menu leaves have neither a command nor a click handler: " + string.Join(", ", dead));
    }

    /// <summary>
    /// These command-palette entries were implemented as status-line-only stubs / unbound
    /// properties; offering them made the IDE look broken. They return only when the feature
    /// behind them exists.
    /// </summary>
    [Test]
    public void CommandPalette_DoesNotOfferKnownStubCommands()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "Dialogs", "CommandPaletteViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("CommandPaletteViewModel.cs not found from the test base directory.");
            return;
        }

        var source = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("\"Split Editor Right\""));
            Assert.That(source, Does.Not.Contain("\"Split Editor Down\""));
            Assert.That(source, Does.Not.Contain("\"Single Editor Layout\""));
            Assert.That(source, Does.Not.Contain("\"Two Columns Layout\""));
            Assert.That(source, Does.Not.Contain("\"Grid Layout\""));
            Assert.That(source, Does.Not.Contain("\"Go Back\""));
            Assert.That(source, Does.Not.Contain("\"Go Forward\""));
            Assert.That(source, Does.Not.Contain("\"Toggle Side Bar\""));
            Assert.That(source, Does.Not.Contain("\"Toggle Panel\""));
        });
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "VisualGameStudio.Shell"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
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
