using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rcamera batch (2 raw camera-system functions). All headless — fast subset.
/// Three checks:
///   1. Every_rcamera_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for both, PLUS a
///      raylib.h completeness cross-check that the UpdateCamera..UpdateCameraPro range is EXACTLY those 2.
///   2. Wrapper_rcamera_bindings_declare_the_correct_marshaling — TYPE scan: both are Subs (void), the Camera3D is passed
///      ByRef (Camera* mutated in place), mode is Integer, movement/rotation are Vector3 BY VALUE, zoom is Single.
///   3. Rcamera_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibCameraParityTests
{
    private static readonly string[] Names = { "UpdateCamera", "UpdateCameraPro" };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_rcamera_export_is_bound_3_ways()
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
            var range = ExtractRlapiRange(raylibHeader, "UpdateCamera(", "UpdateCameraPro(");
            Assert.That(range, Is.EquivalentTo(Names),
                "raylib's UpdateCamera..UpdateCameraPro range must be exactly the 2 rcamera names");
        });
    }

    [Test]
    public void Wrapper_rcamera_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // Both are Subs (void return). Camera3D passed ByRef (Camera* mutated in place). Full signatures pin the
            // marshaling: mode -> Integer, movement/rotation -> Vector3 by value, zoom -> Single.
            Assert.That(wrapper.Contains("Public Sub Framework_UpdateCamera("), Is.True, "UpdateCamera is a Sub (void return)");
            Assert.That(wrapper.Contains("Public Sub Framework_UpdateCameraPro("), Is.True, "UpdateCameraPro is a Sub (void return)");
            Assert.That(wrapper.Contains("Framework_UpdateCamera(ByRef camera As Camera3D, mode As Integer)"), Is.True,
                "UpdateCamera: ByRef Camera3D + Integer mode");
            Assert.That(wrapper.Contains("Framework_UpdateCameraPro(ByRef camera As Camera3D, movement As Vector3, rotation As Vector3, zoom As Single)"), Is.True,
                "UpdateCameraPro: ByRef Camera3D + two Vector3 by value + Single zoom");
        });
    }

    [Test]
    public void Rcamera_exports_are_each_declared_exactly_once()
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
