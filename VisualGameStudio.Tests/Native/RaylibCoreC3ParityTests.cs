using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C3 (raw drawing-mode + VR-simulator functions). All headless — runs
/// in the fast subset. Three checks:
///   1. Every_core_C3_mode_function_is_bound_3_ways — name 3-way for ALL 16 drawing-mode/VR functions, PLUS a raylib.h
///      completeness cross-check that the BeginMode2D..UnloadVrStereoConfig RLAPI range is EXACTLY those 16. Six of the 16
///      (BeginMode2D/EndMode2D, BeginTextureMode/EndTextureMode, BeginShaderMode/EndShaderMode) were already bound; C3
///      adds the other 10.
///   2. Wrapper_C3_bindings_and_VR_structs_declare_the_correct_marshaling — TYPE scan: the NESTED FIXED-SIZE arrays are the
///      whole risk of this batch, so it pins VrDeviceInfo (7 scalars + two &lt;ByValArray SizeConst:=4&gt; float() ) and
///      VrStereoConfig (two &lt;ByValArray SizeConst:=2&gt; Matrix() + six &lt;ByValArray SizeConst:=2&gt; float(), in order) in
///      Utiliy.vb, plus the bindings (Camera3D/VrStereoConfig/VrDeviceInfo by value, Integer blend/scissor params,
///      LoadVrStereoConfig returns VrStereoConfig). This is the ONLY guard that runs on headless CI, so it must be precise.
///   3. Core_C3_new_exports_are_each_declared_exactly_once — duplicate-export guard on the 10 NEW names.
/// LoadVrStereoConfig is a case-distinct non-substring of UnloadVrStereoConfig, and BeginMode2D/BeginMode3D differ, so the
/// trailing "(" on every check keeps prefixes from cross-matching.
/// </summary>
[TestFixture]
public class RaylibCoreC3ParityTests
{
    // All 16 raylib 5.5 drawing-mode + VR functions (the contiguous BeginMode2D..UnloadVrStereoConfig block).
    private static readonly string[] C3AllModeNames =
    {
        "BeginMode2D", "EndMode2D", "BeginMode3D", "EndMode3D", "BeginTextureMode", "EndTextureMode",
        "BeginShaderMode", "EndShaderMode", "BeginBlendMode", "EndBlendMode", "BeginScissorMode", "EndScissorMode",
        "BeginVrStereoMode", "EndVrStereoMode", "LoadVrStereoConfig", "UnloadVrStereoConfig",
    };

    // The 10 raw functions NEWLY bound by C3 (the other 6 were already raw passthroughs before C3).
    private static readonly string[] C3NewNames =
    {
        "BeginMode3D", "EndMode3D", "BeginBlendMode", "EndBlendMode", "BeginScissorMode", "EndScissorMode",
        "BeginVrStereoMode", "EndVrStereoMode", "LoadVrStereoConfig", "UnloadVrStereoConfig",
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
    public void Every_core_C3_mode_function_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // A genuine 3-way check: header export, framework.cpp forwarder, AND wrapper import. Without the .cpp leg a
            // dropped forwarder would only surface at native link time, never on headless CI.
            foreach (var name in C3AllModeNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            // Completeness cross-check against raylib.h: the RLAPI range BeginMode2D..UnloadVrStereoConfig (a comment+blank
            // separate the Begin/End block from the two Load/Unload lines, both skipped) is EXACTLY the 16 mode names.
            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var range = ExtractRlapiRange(raylibHeader, "BeginMode2D(", "UnloadVrStereoConfig(");
            Assert.That(range, Is.EquivalentTo(C3AllModeNames),
                "raylib's BeginMode2D..UnloadVrStereoConfig range must be exactly the 16 drawing-mode/VR names");
        });
    }

    [Test]
    public void Wrapper_C3_bindings_and_VR_structs_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));
        var utility = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb"));

        Assert.Multiple(() =>
        {
            // ---- VrDeviceInfo: 7 scalars in order, then two ByValArray SizeConst:=4 float() arrays. ----
            Assert.That(Regex.IsMatch(utility,
                @"Structure VrDeviceInfo\s*Public hResolution As Integer[^\r\n]*\s*Public vResolution As Integer[^\r\n]*\s*Public hScreenSize As Single[^\r\n]*\s*Public vScreenSize As Single[^\r\n]*\s*Public eyeToScreenDistance As Single[^\r\n]*\s*Public lensSeparationDistance As Single[^\r\n]*\s*Public interpupillaryDistance As Single"),
                Is.True, "VrDeviceInfo must declare its 7 scalar fields (2 Integer + 5 Single) in order");
            Assert.That(Regex.IsMatch(utility,
                @"<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=4\)>\s*Public lensDistortionValues As Single\(\)"),
                Is.True, "VrDeviceInfo.lensDistortionValues must be <ByValArray SizeConst:=4> Single()");
            Assert.That(Regex.IsMatch(utility,
                @"<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=4\)>\s*Public chromaAbCorrection As Single\(\)"),
                Is.True, "VrDeviceInfo.chromaAbCorrection must be <ByValArray SizeConst:=4> Single()");

            // ---- VrStereoConfig: projection + viewOffset are ByValArray SizeConst:=2 Matrix(); then six ByValArray
            // SizeConst:=2 Single() arrays, IN ORDER. This full-order pin is the headless defense against a struct reorder. ----
            Assert.That(Regex.IsMatch(utility,
                @"<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public projection As Matrix\(\)"),
                Is.True, "VrStereoConfig.projection must be <ByValArray SizeConst:=2> Matrix()");
            Assert.That(Regex.IsMatch(utility,
                @"<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public viewOffset As Matrix\(\)"),
                Is.True, "VrStereoConfig.viewOffset must be <ByValArray SizeConst:=2> Matrix()");
            Assert.That(Regex.IsMatch(utility,
                @"Structure VrStereoConfig\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public projection As Matrix\(\)[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public viewOffset As Matrix\(\)[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public leftLensCenter As Single\(\)[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public rightLensCenter As Single\(\)[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public leftScreenCenter As Single\(\)[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public rightScreenCenter As Single\(\)[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public scale As Single\(\)[^\r\n]*\s*<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=2\)>\s*Public scaleIn As Single\(\)"),
                Is.True, "VrStereoConfig must list projection, viewOffset, then the six Single() arrays in raylib field order");

            // ---- Bindings: by-value structs, Integer params, the by-value VrStereoConfig return. ----
            Assert.That(wrapper.Contains("Framework_BeginMode3D(camera As Camera3D)"), Is.True, "BeginMode3D takes Camera3D by value");
            Assert.That(wrapper.Contains("Framework_BeginBlendMode(mode As Integer)"), Is.True, "BeginBlendMode takes an Integer mode");
            Assert.That(wrapper.Contains("Framework_BeginScissorMode(x As Integer, y As Integer, width As Integer, height As Integer)"), Is.True, "BeginScissorMode takes four Integers");
            Assert.That(wrapper.Contains("Framework_BeginVrStereoMode(config As VrStereoConfig)"), Is.True, "BeginVrStereoMode takes VrStereoConfig by value");
            Assert.That(wrapper.Contains("Framework_LoadVrStereoConfig(device As VrDeviceInfo) As VrStereoConfig"), Is.True, "LoadVrStereoConfig: VrDeviceInfo in, VrStereoConfig out (both by value)");
            Assert.That(wrapper.Contains("Framework_UnloadVrStereoConfig(config As VrStereoConfig)"), Is.True, "UnloadVrStereoConfig takes VrStereoConfig by value");
            // The four void End* + EndVrStereoMode are Subs.
            foreach (var sub in new[] { "EndMode3D", "EndBlendMode", "EndScissorMode", "EndVrStereoMode" })
                Assert.That(Regex.IsMatch(wrapper, $@"Public Sub Framework_{sub}\(\)"), Is.True, $"{sub} must be a parameterless Sub");
        });
    }

    [Test]
    public void Core_C3_new_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in C3NewNames)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    // Extracts RLAPI function names from the first line containing firstContains through the line containing lastContains
    // (both inclusive). Non-RLAPI lines (comments/blanks) are skipped; the non-greedy .*? lands the function name '('.
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
