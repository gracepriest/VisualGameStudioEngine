using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Tests;

/// <summary>
/// Task 2.9 (Decision D4): the F1 Keyboard Shortcuts dialog and the Settings ▸ Keyboard grid are
/// generated from <see cref="KeyboardShortcutRegistry"/>, whose <see cref="KeyboardShortcutRegistry.Global"/>
/// entries are meant to mirror <c>MainWindow.axaml</c>'s real <c>Window.KeyBindings</c> exactly.
///
/// The old hand-maintained lists drifted: they showed Ctrl+Shift+F5 as "Run in External Console"
/// (it is actually Restart Debugging), invented gestures like New Project = Ctrl+Shift+N, and
/// omitted ~15 real bindings. This fixture parses the AXAML at test time and fails if the registry
/// and the AXAML ever diverge again, so the reference can't silently rot.
/// </summary>
[TestFixture]
public class KeyboardShortcutRegistryTests
{
    private static readonly Regex BindingCommandRegex =
        new(@"\{\s*Binding\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);

    /// <summary>
    /// Parses <c>MainWindow.axaml</c>'s <c>Window.KeyBindings</c> into (command, gesture) pairs.
    /// Returns null when the source file cannot be located (binary-only run).
    /// </summary>
    private static List<(string Command, string Gesture)>? ReadAxamlKeyBindings()
    {
        var axaml = FindMainWindowAxaml();
        if (axaml == null) return null;

        var doc = XDocument.Load(axaml);
        var result = new List<(string, string)>();

        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "KeyBinding"))
        {
            var gesture = el.Attribute("Gesture")?.Value;
            var commandAttr = el.Attribute("Command")?.Value;
            if (string.IsNullOrWhiteSpace(gesture) || string.IsNullOrWhiteSpace(commandAttr))
                continue;

            var m = BindingCommandRegex.Match(commandAttr);
            if (!m.Success) continue;

            result.Add((m.Groups[1].Value, gesture.Trim()));
        }

        return result;
    }

    private static string? FindMainWindowAxaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName,
                "VisualGameStudio.Shell", "Views", "MainWindow.axaml");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Test]
    public void GlobalEntries_MatchMainWindowKeyBindings_NoDrift()
    {
        var axaml = ReadAxamlKeyBindings();
        if (axaml == null)
        {
            Assert.Ignore("MainWindow.axaml not found from the test base directory — skipping drift check.");
            return;
        }

        var axamlPairs = axaml
            .Select(p => $"{p.Command}={p.Gesture}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var registryPairs = KeyboardShortcutRegistry.Global
            .Select(s => $"{s.CommandName}={s.Gesture}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var missingFromRegistry = axamlPairs.Except(registryPairs).ToList();
        var extraInRegistry = registryPairs.Except(axamlPairs).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(missingFromRegistry, Is.Empty,
                "MainWindow.axaml declares KeyBindings the registry is missing (add them to KeyboardShortcutRegistry.Global): "
                + string.Join(", ", missingFromRegistry));
            Assert.That(extraInRegistry, Is.Empty,
                "The registry claims global bindings that no longer exist in (or differ from) MainWindow.axaml (fix or remove them): "
                + string.Join(", ", extraInRegistry));
        });
    }

    [Test]
    public void GlobalEntries_HaveNoDuplicateCommands()
    {
        var dupes = KeyboardShortcutRegistry.Global
            .GroupBy(s => s.CommandName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.That(dupes, Is.Empty, "Duplicate command entries in the global registry: " + string.Join(", ", dupes));
    }

    [Test]
    public void EveryGlobalEntry_HasGestureAndCommand()
    {
        foreach (var s in KeyboardShortcutRegistry.Global)
        {
            Assert.That(s.Gesture, Is.Not.Empty, $"Global entry '{s.DisplayName}' must have a gesture.");
            Assert.That(s.CommandName, Does.EndWith("Command"),
                $"Global entry '{s.DisplayName}' must name a bound command (…Command).");
            Assert.That(s.DisplayName, Is.Not.Empty, $"Entry for {s.CommandName} must have a display name.");
        }
    }

    [Test]
    public void CorrectsKnownWrongEntries_FromTheOldHandMaintainedList()
    {
        // Ctrl+Shift+F5 is Restart Debugging — the old list mislabeled it "Run in External Console".
        var ctrlShiftF5 = KeyboardShortcutRegistry.Global
            .Where(s => s.Gesture == "Ctrl+Shift+F5")
            .Select(s => s.DisplayName)
            .ToList();
        Assert.That(ctrlShiftF5, Is.EqualTo(new[] { "Restart Debugging" }),
            "Ctrl+Shift+F5 must be Restart Debugging (the old list wrongly showed 'Run in External Console').");

        Assert.That(KeyboardShortcutRegistry.All.Any(s => s.DisplayName == "Run in External Console"), Is.False,
            "'Run in External Console' was a phantom command with no real binding — it must not appear.");

        // "Change Signature..." had an EMPTY gesture in the old list; it is really Ctrl+Shift+-.
        var changeSig = KeyboardShortcutRegistry.Global.Single(s => s.CommandName == "ChangeSignatureCommand");
        Assert.That(changeSig.Gesture, Is.EqualTo("Ctrl+Shift+OemMinus"));
        Assert.That(changeSig.DisplayGesture, Is.EqualTo("Ctrl+Shift+-"));

        // Real bindings the old list omitted are now present.
        var commands = KeyboardShortcutRegistry.Global.Select(s => s.CommandName).ToHashSet();
        foreach (var required in new[]
                 {
                     "AttachToProcessCommand", "SetNextStatementCommand", "GoToNextErrorCommand",
                     "GoToPreviousErrorCommand", "ToggleWhitespaceCommand", "ZoomInCommand",
                     "ZoomOutCommand", "ZoomResetCommand", "FocusEditorCommand", "ShowKeyboardShortcutsCommand"
                 })
        {
            Assert.That(commands, Does.Contain(required), $"Real binding {required} must be represented.");
        }
    }

    [Test]
    public void HumanizeGesture_ConvertsOemAndDigitTokens()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("Ctrl+Shift+OemMinus"), Is.EqualTo("Ctrl+Shift+-"));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("Ctrl+OemPeriod"), Is.EqualTo("Ctrl+."));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("Ctrl+OemTilde"), Is.EqualTo("Ctrl+`"));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("Ctrl+OemPlus"), Is.EqualTo("Ctrl++"));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("Ctrl+D1"), Is.EqualTo("Ctrl+1"));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("Ctrl+Shift+D0"), Is.EqualTo("Ctrl+Shift+0"));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("F5"), Is.EqualTo("F5"));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture("Shift+Alt+Enter"), Is.EqualTo("Shift+Alt+Enter"));
            Assert.That(KeyboardShortcutRegistry.HumanizeGesture(""), Is.EqualTo(""));
        });
    }
}
