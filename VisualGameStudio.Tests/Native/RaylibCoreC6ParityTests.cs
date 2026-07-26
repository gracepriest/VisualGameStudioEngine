using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C6 (timing/frame control + random + misc + 2 input stragglers).
/// All headless (no engine load, no window) — runs in the fast subset. Three checks:
///   1. Every_core_C6_export_has_a_matching_wrapper_import — name-level 3-way presence for all 15, PLUS two raylib.h
///      completeness cross-checks: (a) the timing/frame/random/misc range ("// Timing-related functions".."MemFree") is
///      covered by exactly {13 new non-input C6 names} ∪ {6 already-bound}; (b) the ENTIRE rcore input-handling section
///      (keyboard..touch) is now bound in framework.h — proving SetGamepadVibration + GetTouchPosition were the only
///      input stragglers left and C6 closed the section.
///   2. Wrapper_C6_bindings_declare_the_correct_marshaling_types — TYPE-level scan of RaylibWrapper.vb (Double WaitTime,
///      UInteger seed/flags/size, IntPtr random-seq/Mem returns, Vector2 GetTouchPosition, Single vibration motors,
///      CharSet.Ansi on OpenURL/TraceLog, the Integer getters, and the managed LoadRandomSequence Integer() helper). This
///      is the only HEADLESS guard on the wrapper's real marshaling (the runtime fixtures use their own local [DllImport]
///      mirrors), so without it a wrong VB type would ship green on any headless machine.
///   3. Already_exported_timing_misc_functions_are_not_re_declared_by_core_C6 — duplicate-export guard on the 6
///      pre-existing timing/misc functions.
/// TraceLog is variadic in raylib; it binds as a fixed 2-arg forwarder (text routed through "%s" engine-side). OpenURL is
/// name/type-checked only — it is never invoked at runtime (it would launch a browser).
/// </summary>
[TestFixture]
public class RaylibCoreC6ParityTests
{
    // 13 new NON-input C6 names (timing/frame control + random + misc), import name == export name.
    private static readonly string[] NonInputNames =
    {
        "SwapScreenBuffer", "PollInputEvents", "WaitTime", "SetRandomSeed", "GetRandomValue", "LoadRandomSequence",
        "UnloadRandomSequence", "SetConfigFlags", "OpenURL", "TraceLog", "SetTraceLogLevel", "MemAlloc", "MemRealloc",
    };

    // 2 input stragglers — the only rcore input-section functions the engine did not already bind.
    private static readonly string[] InputStragglers = { "SetGamepadVibration", "GetTouchPosition" };

    // Pre-existing timing/misc exports in the "// Timing-related functions".."MemFree" range — C6 must NOT re-declare
    // these. Together with NonInputNames they EXACTLY cover that raylib.h range (see the cross-check).
    private static readonly string[] AlreadyBoundTimingMisc =
    {
        "SetTargetFPS", "GetFrameTime", "GetTime", "GetFPS", "TakeScreenshot", "MemFree",
    };

    private static string[] AllC6Names => NonInputNames.Concat(InputStragglers).ToArray();

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_core_C6_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));
        var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in AllC6Names)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            // (a) Completeness over the timing/frame/random/misc range: {13 non-input} ∪ {6 already-bound} == raylib's
            // "// Timing-related functions".."MemFree" range EXACTLY. Fails if a raylib fn there is bound by neither set.
            var timingMisc = ExtractRlapiNames(raylibHeader, "// Timing-related functions", "MemFree(").ToHashSet();
            var covered = NonInputNames.Concat(AlreadyBoundTimingMisc).ToHashSet();
            Assert.That(timingMisc, Is.EquivalentTo(covered),
                "NonInputNames ∪ AlreadyBoundTimingMisc must exactly cover raylib's timing/frame/random/misc range");

            // (b) The whole rcore input-handling section (keyboard..touch) is now bound in framework.h — this is what
            // makes "only SetGamepadVibration + GetTouchPosition were left" a proven fact, not an assumption.
            foreach (var name in ExtractRlapiNames(raylibHeader, "// Input-related functions: keyboard", "GetTouchPointCount("))
                Assert.That(header.Contains($"Framework_{name}("), Is.True,
                    $"rcore input section not fully bound: framework.h missing Framework_{name} (an unhandled input straggler)");
        });
    }

    [Test]
    public void Wrapper_C6_bindings_declare_the_correct_marshaling_types()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // void frame-control / trace / config Subs.
            Assert.That(wrapper.Contains("Public Sub Framework_SwapScreenBuffer()"), Is.True, "SwapScreenBuffer is a parameterless Sub");
            Assert.That(wrapper.Contains("Public Sub Framework_PollInputEvents()"), Is.True, "PollInputEvents is a parameterless Sub");
            Assert.That(wrapper.Contains("Public Sub Framework_SetTraceLogLevel(logLevel As Integer)"), Is.True, "SetTraceLogLevel takes Integer");
            // WaitTime seconds must be Double (raylib double), not Single/Integer.
            Assert.That(wrapper.Contains("Framework_WaitTime(seconds As Double)"), Is.True, "WaitTime takes a Double");
            // unsigned int -> UInteger.
            Assert.That(wrapper.Contains("Framework_SetRandomSeed(seed As UInteger)"), Is.True, "SetRandomSeed seed is UInteger");
            Assert.That(wrapper.Contains("Framework_SetConfigFlags(flags As UInteger)"), Is.True, "SetConfigFlags flags is UInteger");
            Assert.That(wrapper.Contains("Framework_MemAlloc(size As UInteger) As IntPtr"), Is.True, "MemAlloc(UInteger) returns IntPtr");
            Assert.That(wrapper.Contains("Framework_MemRealloc(ptr As IntPtr, size As UInteger) As IntPtr"), Is.True, "MemRealloc(IntPtr,UInteger) returns IntPtr");
            // Integer getters.
            Assert.That(wrapper.Contains("Framework_GetRandomValue(min As Integer, max As Integer) As Integer"), Is.True, "GetRandomValue returns Integer");
            // Random sequence: raw IntPtr export + Sub(IntPtr) unload + managed Integer() helper using Marshal.Copy.
            Assert.That(wrapper.Contains("Framework_LoadRandomSequence(count As UInteger, min As Integer, max As Integer) As IntPtr"), Is.True, "LoadRandomSequence raw export returns IntPtr");
            Assert.That(wrapper.Contains("Framework_UnloadRandomSequence(sequence As IntPtr)"), Is.True, "UnloadRandomSequence takes IntPtr");
            Assert.That(wrapper.Contains("Public Function LoadRandomSequence(count As UInteger, min As Integer, max As Integer) As Integer()"), Is.True, "LoadRandomSequence Integer() helper present");
            Assert.That(Regex.IsMatch(wrapper, @"Public Function LoadRandomSequence\(count As UInteger, min As Integer, max As Integer\) As Integer\(\)[\s\S]{0,320}?Marshal\.Copy"), Is.True, "LoadRandomSequence helper uses Marshal.Copy");
            // Ownership: the helper MUST free the raylib-heap sequence it copied (guards against a regression that drops
            // the free → leak). The helper is only static-checked here — the tests exercise the raw export path, not the
            // managed helper — so this assertion is the guard that its ownership contract stays intact.
            Assert.That(Regex.IsMatch(wrapper, @"Public Function LoadRandomSequence\(count As UInteger, min As Integer, max As Integer\) As Integer\(\)[\s\S]{0,400}?Framework_UnloadRandomSequence"), Is.True, "LoadRandomSequence helper frees via UnloadRandomSequence (no leak)");
            // Vector2 by-value return.
            Assert.That(wrapper.Contains("Framework_GetTouchPosition(index As Integer) As Vector2"), Is.True, "GetTouchPosition returns Vector2");
            // Single vibration motors (raylib floats), not Double.
            Assert.That(wrapper.Contains("Framework_SetGamepadVibration(gamepad As Integer, leftMotor As Single, rightMotor As Single, duration As Single)"), Is.True, "SetGamepadVibration motors are Single");
            // TraceLog fixed 2-arg (Integer, String); string INPUTs carry CharSet.Ansi on their own DllImport line.
            Assert.That(wrapper.Contains("Framework_TraceLog(logLevel As Integer, text As String)"), Is.True, "TraceLog binds as (Integer, String)");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Sub Framework_TraceLog\("), Is.True, "TraceLog DllImport sets CharSet.Ansi");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Sub Framework_OpenURL\("), Is.True, "OpenURL DllImport sets CharSet.Ansi");
            Assert.That(wrapper.Contains("Framework_OpenURL(url As String)"), Is.True, "OpenURL takes a String");
        });
    }

    [Test]
    public void Already_exported_timing_misc_functions_are_not_re_declared_by_core_C6()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in AlreadyBoundTimingMisc)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    // Extracts RLAPI function names from startAnchor until (and including) the first line containing stopContains.
    private static List<string> ExtractRlapiNames(string raylibHeader, string startAnchor, string stopContains)
    {
        var lines = raylibHeader.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains(startAnchor));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"raylib.h anchor '{startAnchor}' not found");
        var names = new List<string>();
        var rx = new Regex(@"RLAPI\s+.*?(\w+)\s*\(");
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].Contains("RLAPI"))
            {
                var m = rx.Match(lines[i]);
                if (m.Success) names.Add(m.Groups[1].Value);
            }
            if (lines[i].Contains(stopContains)) break; // inclusive stop
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
