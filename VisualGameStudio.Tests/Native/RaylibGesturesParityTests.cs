using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rgestures batch (8 raw gesture/touch functions). All headless — fast subset.
/// Three checks:
///   1. Every_rgestures_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for all 8, PLUS a
///      raylib.h completeness cross-check that the SetGesturesEnabled..GetGesturePinchAngle range is EXACTLY those 8.
///   2. Wrapper_rgestures_bindings_declare_the_correct_marshaling — TYPE scan: UInteger flag params, I1 IsGestureDetected,
///      Integer/Single getters, and the two Vector2-BY-VALUE returns (GetGestureDragVector/GetGesturePinchVector).
///   3. Rgestures_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibGesturesParityTests
{
    private static readonly string[] Names =
    {
        "SetGesturesEnabled", "IsGestureDetected", "GetGestureDetected", "GetGestureHoldDuration",
        "GetGestureDragVector", "GetGestureDragAngle", "GetGesturePinchVector", "GetGesturePinchAngle",
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
    public void Every_rgestures_export_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Names)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var range = ExtractRlapiRange(raylibHeader, "SetGesturesEnabled(", "GetGesturePinchAngle(");
            Assert.That(range, Is.EquivalentTo(Names),
                "raylib's SetGesturesEnabled..GetGesturePinchAngle range must be exactly the 8 rgestures names");
        });
    }

    [Test]
    public void Wrapper_rgestures_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            Assert.That(wrapper.Contains("Framework_SetGesturesEnabled(flags As UInteger)"), Is.True, "SetGesturesEnabled takes UInteger flags");
            Assert.That(wrapper.Contains("Framework_IsGestureDetected(gesture As UInteger) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "IsGestureDetected: UInteger + I1 Boolean");
            Assert.That(wrapper.Contains("Framework_GetGestureDetected() As Integer"), Is.True, "GetGestureDetected returns Integer");
            Assert.That(wrapper.Contains("Framework_GetGestureHoldDuration() As Single"), Is.True, "GetGestureHoldDuration returns Single");
            Assert.That(wrapper.Contains("Framework_GetGestureDragVector() As Vector2"), Is.True, "GetGestureDragVector returns Vector2 by value");
            Assert.That(wrapper.Contains("Framework_GetGestureDragAngle() As Single"), Is.True, "GetGestureDragAngle returns Single");
            Assert.That(wrapper.Contains("Framework_GetGesturePinchVector() As Vector2"), Is.True, "GetGesturePinchVector returns Vector2 by value");
            Assert.That(wrapper.Contains("Framework_GetGesturePinchAngle() As Single"), Is.True, "GetGesturePinchAngle returns Single");
        });
    }

    [Test]
    public void Rgestures_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in Names)
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
