using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rmodels ANIMATIONS sub-batch (6 fns — CLOSES the rmodels module 76/76). All headless
/// (text scan) — fast subset. Checks:
///   1. Every_animations_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for all 6, PLUS a
///      raylib.h completeness cross-check (LoadModelAnimations..IsModelAnimationValid == exactly the 6), so nothing was missed.
///   2. Wrapper_animations_bindings_declare_the_correct_marshaling — TYPE scan: LoadModelAnimations -> IntPtr (raylib-owned
///      ModelAnimation* array) + ByRef animCount + CharSet.Ansi; Update*/Unload(single) are Subs taking Model/ModelAnimation by
///      value; UnloadModelAnimations takes an IntPtr array pointer + count; IsModelAnimationValid -> <MarshalAs(I1)> Boolean.
///   3. Animation_structs_mirror_raylib_layout — field/width/order pin for ModelAnimation, BoneInfo, and Transform, incl. the
///      ByValArray SizeConst:=32 on the two char[32] name fields (a wrong width/order desyncs the by-value ModelAnimation ABI).
///   4. Animations_exports_are_each_declared_exactly_once — duplicate-export guard.
/// </summary>
[TestFixture]
public class RaylibModelsAnimationsParityTests
{
    private static readonly string[] AnimNames =
    {
        "LoadModelAnimations", "UpdateModelAnimation", "UpdateModelAnimationBones",
        "UnloadModelAnimation", "UnloadModelAnimations", "IsModelAnimationValid",
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
    public void Every_animations_export_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in AnimNames)
            {
                // "Framework_UnloadModelAnimation(" would substring-match "Framework_UnloadModelAnimations(" — use a name+delimiter
                // check that pins the exact arg list, so the two are counted distinctly.
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var animRange = ExtractRlapiRange(raylibHeader, "LoadModelAnimations(", "IsModelAnimationValid(");
            Assert.That(animRange, Is.EquivalentTo(AnimNames), "raylib's LoadModelAnimations..IsModelAnimationValid range must be exactly the 6 animation fns");
        });
    }

    [Test]
    public void Wrapper_animations_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // LoadModelAnimations: raylib-owned ModelAnimation* array -> IntPtr, count via ByRef, path is a CharSet.Ansi String.
            Assert.That(wrapper.Contains("Public Function Framework_LoadModelAnimations(fileName As String, ByRef animCount As Integer) As IntPtr"), Is.True, "LoadModelAnimations: String + ByRef Integer -> IntPtr");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi\)>\s*\r?\n\s*Public Function Framework_LoadModelAnimations\("), Is.True, "LoadModelAnimations import must set CharSet.Ansi");

            // Update* + UnloadModelAnimation take Model/ModelAnimation by value (Subs).
            Assert.That(wrapper.Contains("Public Sub Framework_UpdateModelAnimation(model As Model, anim As ModelAnimation, frame As Integer)"), Is.True, "UpdateModelAnimation: Model + ModelAnimation by value + Integer");
            Assert.That(wrapper.Contains("Public Sub Framework_UpdateModelAnimationBones(model As Model, anim As ModelAnimation, frame As Integer)"), Is.True, "UpdateModelAnimationBones: Model + ModelAnimation by value + Integer");
            Assert.That(wrapper.Contains("Public Sub Framework_UnloadModelAnimation(anim As ModelAnimation)"), Is.True, "UnloadModelAnimation: ModelAnimation by value");

            // UnloadModelAnimations takes the raylib-owned array pointer + count (IntPtr, not ByRef/array).
            Assert.That(wrapper.Contains("Public Sub Framework_UnloadModelAnimations(animations As IntPtr, animCount As Integer)"), Is.True, "UnloadModelAnimations: IntPtr array pointer + Integer count");

            // IsModelAnimationValid -> I1 Boolean, both structs by value.
            Assert.That(wrapper.Contains("Public Function Framework_IsModelAnimationValid(model As Model, anim As ModelAnimation) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "IsModelAnimationValid: Model + ModelAnimation -> I1 Boolean");
        });
    }

    [Test]
    public void Animation_structs_mirror_raylib_layout()
    {
        var root = RepoRoot();
        var util = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb")).Replace("\r\n", "\n");

        // Transform: Vector3 translation; Quaternion(=Vector4) rotation; Vector3 scale.
        var tr = Regex.Match(util, @"Public Structure Transform\n(.*?)\n\s*End Structure", RegexOptions.Singleline);
        Assert.That(tr.Success, Is.True, "Utiliy.vb must declare a Transform structure");
        Assert.That(ScanFields(tr.Groups[1].Value), Is.EqualTo(new[] { "translation:Vector3", "rotation:Vector4", "scale:Vector3" }),
            "Transform fields must mirror raylib (Vector3 translation; Vector4/Quaternion rotation; Vector3 scale)");

        // BoneInfo: char[32] name (ByValArray Byte) + int parent.
        var bi = Regex.Match(util, @"Public Structure BoneInfo\n(.*?)\n\s*End Structure", RegexOptions.Singleline);
        Assert.That(bi.Success, Is.True, "Utiliy.vb must declare a BoneInfo structure");
        var biBody = bi.Groups[1].Value;
        Assert.That(ScanFields(biBody), Is.EqualTo(new[] { "name:Byte", "parent:Integer" }),
            "BoneInfo fields must mirror raylib (char[32] name; int parent)");
        Assert.That(Regex.IsMatch(biBody, @"<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=32\)>\s*\n\s*Public name As Byte\(\)"), Is.True,
            "BoneInfo.name must be an inline fixed 32-byte array (ByValArray SizeConst:=32)");

        // ModelAnimation: int boneCount; int frameCount; BoneInfo* bones; Transform** framePoses; char[32] name.
        var ma = Regex.Match(util, @"Public Structure ModelAnimation\n(.*?)\n\s*End Structure", RegexOptions.Singleline);
        Assert.That(ma.Success, Is.True, "Utiliy.vb must declare a ModelAnimation structure");
        var maBody = ma.Groups[1].Value;
        Assert.That(ScanFields(maBody), Is.EqualTo(new[] { "boneCount:Integer", "frameCount:Integer", "bones:IntPtr", "framePoses:IntPtr", "name:Byte" }),
            "ModelAnimation fields must mirror raylib's 5 fields, in order, with correct widths");
        Assert.That(Regex.IsMatch(maBody, @"<MarshalAs\(UnmanagedType\.ByValArray, SizeConst:=32\)>\s*\n\s*Public name As Byte\(\)"), Is.True,
            "ModelAnimation.name must be an inline fixed 32-byte array (ByValArray SizeConst:=32)");
    }

    [Test]
    public void Animations_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            // UnloadModelAnimation("<x>)") vs UnloadModelAnimations: pin the exact parameter start so the substring
            // "Framework_UnloadModelAnimation(" doesn't over-count by matching the plural. UnloadModelAnimation takes a
            // ModelAnimation by value; UnloadModelAnimations takes a ModelAnimation* — distinct first tokens after '('.
            Assert.That(CountOccurrences(header, "Framework_UnloadModelAnimation(ModelAnimation anim)"), Is.EqualTo(1), "UnloadModelAnimation declared exactly once");
            Assert.That(CountOccurrences(header, "Framework_UnloadModelAnimations(ModelAnimation* animations"), Is.EqualTo(1), "UnloadModelAnimations declared exactly once");

            foreach (var name in new[] { "LoadModelAnimations", "UpdateModelAnimation(", "UpdateModelAnimationBones", "IsModelAnimationValid" })
            {
                int count = CountOccurrences(header, $"Framework_{name}");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    private static List<string> ScanFields(string structBody)
    {
        var fields = new List<string>();
        foreach (Match f in Regex.Matches(structBody, @"Public (\w+) As (\w+)"))
            fields.Add($"{f.Groups[1].Value}:{f.Groups[2].Value}");
        return fields;
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
                var mm = rx.Match(lines[i]);
                if (mm.Success) names.Add(mm.Groups[1].Value);
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
