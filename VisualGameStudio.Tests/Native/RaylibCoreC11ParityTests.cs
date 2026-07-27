using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C11 (raw automation-event functions). All headless — fast subset.
/// Three checks:
///   1. Every_core_C11_export_is_bound_3_ways — a GENUINE 3-way (framework.h export + framework.cpp forwarder + wrapper
///      import) for all 8, PLUS a raylib.h completeness cross-check that the LoadAutomationEventList..PlayAutomationEvent
///      RLAPI range is EXACTLY those 8. (All 8 are new — zero pre-existing automation bindings.)
///   2. Wrapper_C11_bindings_and_structs_declare_the_correct_marshaling — TYPE scan: AutomationEvent {UInteger frame,
///      UInteger type, &lt;ByValArray SizeConst:=4&gt; Integer() params} and AutomationEventList {UInteger capacity, UInteger
///      count, IntPtr events} in order; AutomationEventList returned/passed BY VALUE; AutomationEvent by value;
///      SetAutomationEventList takes IntPtr (raylib retains the pointer); I1 on Export; CharSet.Ansi on the string inputs.
///   3. Core_C11_exports_are_each_declared_exactly_once — duplicate-export guard.
/// Case-sensitive substring traps handled by the trailing "(": LoadAutomationEventList vs UnloadAutomationEventList (Load
/// has a capital L), SetAutomationEventList vs SetAutomationEventBaseFrame.
/// </summary>
[TestFixture]
public class RaylibCoreC11ParityTests
{
    private static readonly string[] C11Names =
    {
        "LoadAutomationEventList", "UnloadAutomationEventList", "ExportAutomationEventList", "SetAutomationEventList",
        "SetAutomationEventBaseFrame", "StartAutomationEventRecording", "StopAutomationEventRecording", "PlayAutomationEvent",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_core_C11_export_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in C11Names)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var range = ExtractRlapiRange(raylibHeader, "LoadAutomationEventList(", "PlayAutomationEvent(");
            Assert.That(range, Is.EquivalentTo(C11Names),
                "raylib's LoadAutomationEventList..PlayAutomationEvent range must be exactly the 8 automation names");
        });
    }

    [Test]
    public void Wrapper_C11_bindings_and_structs_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));
        var utility = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb"));

        Assert.Multiple(() =>
        {
            // AutomationEvent: UInteger frame, UInteger type, then the int[4] params as <ByValArray SizeConst:=4> Integer().
            Assert.That(Regex.IsMatch(utility,
                @"Structure AutomationEvent\s+Public frame As UInteger[^\r\n]*\s*Public type As UInteger[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=4\)>\s*Public params As Integer\(\)"),
                Is.True, "AutomationEvent must be {UInteger frame, UInteger type, <ByValArray SizeConst:=4> Integer() params}");
            // AutomationEventList: UInteger capacity, UInteger count, IntPtr events (the raylib-owned events pointer).
            Assert.That(Regex.IsMatch(utility,
                @"Structure AutomationEventList\s+Public capacity As UInteger[^\r\n]*\s*Public count As UInteger[^\r\n]*\s*Public events As IntPtr"),
                Is.True, "AutomationEventList must be {UInteger capacity, UInteger count, IntPtr events} in order");

            // AutomationEventList returned/passed BY VALUE.
            Assert.That(wrapper.Contains("Framework_LoadAutomationEventList(fileName As String) As AutomationEventList"), Is.True, "LoadAutomationEventList returns AutomationEventList by value");
            Assert.That(wrapper.Contains("Framework_UnloadAutomationEventList(list As AutomationEventList)"), Is.True, "UnloadAutomationEventList takes AutomationEventList by value");
            Assert.That(Regex.IsMatch(wrapper, @"Framework_ExportAutomationEventList\(list As AutomationEventList, fileName As String\) As <MarshalAs\(UnmanagedType\.I1\)> Boolean"), Is.True, "ExportAutomationEventList: AutomationEventList by value + I1 return");
            // SetAutomationEventList takes an IntPtr (raylib retains the pointer — NOT ByRef/by-value which would dangle).
            Assert.That(wrapper.Contains("Framework_SetAutomationEventList(list As IntPtr)"), Is.True, "SetAutomationEventList takes IntPtr (retained pointer)");
            // AutomationEvent passed BY VALUE (evt avoids the VB Event keyword).
            Assert.That(wrapper.Contains("Framework_PlayAutomationEvent(evt As AutomationEvent)"), Is.True, "PlayAutomationEvent takes AutomationEvent by value");
            // Plain int + parameterless Subs.
            Assert.That(wrapper.Contains("Framework_SetAutomationEventBaseFrame(frame As Integer)"), Is.True, "SetAutomationEventBaseFrame takes an Integer");
            foreach (var sub in new[] { "StartAutomationEventRecording", "StopAutomationEventRecording" })
                Assert.That(Regex.IsMatch(wrapper, $@"Public Sub Framework_{sub}\(\)"), Is.True, $"{sub} must be a parameterless Sub");

            // CharSet.Ansi on the two string-input bindings.
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_LoadAutomationEventList\("), Is.True, "LoadAutomationEventList fileName marshals Ansi");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_ExportAutomationEventList\("), Is.True, "ExportAutomationEventList fileName marshals Ansi");
        });
    }

    [Test]
    public void Core_C11_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in C11Names)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    private static List<string> ExtractRlapiRange(string raylibHeader, string firstContains, string lastContains)
    {
        var lines = raylibHeader.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains("RLAPI") && l.Contains(firstContains));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"raylib.h start anchor '{firstContains}' not found");
        var names = new List<string>();
        var rx = new Regex(@"RLAPI\s+.*?(\w+)\s*\(");
        for (int i = start; i < lines.Length; i++)
        {
            if (lines[i].Contains("RLAPI"))
            {
                var m = rx.Match(lines[i]);
                if (m.Success) names.Add(m.Groups[1].Value);
            }
            if (lines[i].Contains(lastContains)) break;
        }
        return names;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
