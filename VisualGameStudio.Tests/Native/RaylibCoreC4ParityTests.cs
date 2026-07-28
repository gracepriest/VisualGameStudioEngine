using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C4 (raw raylib shader management). All headless (no engine load, no
/// window) — runs in the fast subset. Three checks:
///   1. Every_core_C4_shader_function_is_bound_3_ways — name-level 3-way presence for ALL 10 raylib shader functions, PLUS a
///      raylib.h completeness cross-check that the LoadShader..UnloadShader RLAPI range is EXACTLY those 10 names. Two of the
///      ten (GetShaderLocation, UnloadShader) were already bound as raw passthroughs before C4; C4 adds the other 8.
///   2. Wrapper_C4_bindings_declare_the_correct_marshaling_types — TYPE-level scan: Shader {Integer id, IntPtr locs} +
///      Matrix present; Shader returned BY VALUE (LoadShader/LoadShaderFromMemory); I1 IsShaderValid; const void* value ->
///      IntPtr on SetShaderValue(V); Matrix BY VALUE (SetShaderValueMatrix); Texture2D BY VALUE (SetShaderValueTexture);
///      CharSet.Ansi on all three string-input bindings (LoadShader, LoadShaderFromMemory, GetShaderLocationAttrib).
///   3. Core_C4_new_shader_exports_are_each_declared_exactly_once — duplicate-export guard on the 8 NEW names.
/// SetShaderValue is a prefix of SetShaderValueV/Matrix/Texture and LoadShader of LoadShaderFromMemory/LoadShaderF — every
/// Contains()/count check appends "(" so the prefix never matches the longer name.
/// </summary>
[TestFixture]
public class RaylibCoreC4ParityTests
{
    // All 10 raylib 5.5 shader-management functions (the contiguous LoadShader..UnloadShader RLAPI block).
    private static readonly string[] C4ShaderNames =
    {
        "LoadShader", "LoadShaderFromMemory", "IsShaderValid", "GetShaderLocation", "GetShaderLocationAttrib",
        "SetShaderValue", "SetShaderValueV", "SetShaderValueMatrix", "SetShaderValueTexture", "UnloadShader",
    };

    // The 8 raw functions NEWLY bound by C4 (GetShaderLocation + UnloadShader were already raw passthroughs).
    private static readonly string[] C4NewNames =
    {
        "LoadShader", "LoadShaderFromMemory", "IsShaderValid", "GetShaderLocationAttrib",
        "SetShaderValue", "SetShaderValueV", "SetShaderValueMatrix", "SetShaderValueTexture",
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
    public void Every_core_C4_shader_function_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in C4ShaderNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            // Completeness cross-check against raylib.h: the RLAPI range LoadShader..UnloadShader is EXACTLY the 10 shader
            // names — catches a forgotten function in that contiguous block (e.g. a missed SetShaderValueV).
            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var range = ExtractRlapiRange(raylibHeader, "LoadShader(", "UnloadShader(");
            Assert.That(range, Is.EquivalentTo(C4ShaderNames),
                "raylib's LoadShader..UnloadShader range must be exactly the 10 shader-management names");
        });
    }

    [Test]
    public void Wrapper_C4_bindings_declare_the_correct_marshaling_types()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));
        var utility = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb"));

        Assert.Multiple(() =>
        {
            // Shader { Integer id, IntPtr locs } — id is 4 bytes (raylib's unsigned int), locs an int* pointer (8 bytes on
            // x64). Wrong here → the by-value Shader return/param is misaligned.
            Assert.That(Regex.IsMatch(utility,
                @"Structure Shader\s+Public id As Integer\s+Public locs As IntPtr"),
                Is.True, "Shader must be {Integer id, IntPtr locs} in order");
            // Matrix field ORDER is the silent-regression risk for SetShaderValueMatrix (defined back in C5): raylib
            // declares the 16 floats scrambled (m0,m4,m8,m12 / m1,m5,m9,m13 / ...) and the by-value ABI only lines up if VB
            // lists them in that exact order. Pin all 16 in sequence (the [^\r\n]*\s* separator tolerates trailing comments
            // but not a reordered/missing field). This is the headless guard against a Matrix reorder that no display CI runs.
            Assert.That(Regex.IsMatch(utility,
                @"Structure Matrix\s*Public m0 As Single[^\r\n]*\s*Public m4 As Single[^\r\n]*\s*Public m8 As Single[^\r\n]*\s*Public m12 As Single[^\r\n]*\s*Public m1 As Single[^\r\n]*\s*Public m5 As Single[^\r\n]*\s*Public m9 As Single[^\r\n]*\s*Public m13 As Single[^\r\n]*\s*Public m2 As Single[^\r\n]*\s*Public m6 As Single[^\r\n]*\s*Public m10 As Single[^\r\n]*\s*Public m14 As Single[^\r\n]*\s*Public m3 As Single[^\r\n]*\s*Public m7 As Single[^\r\n]*\s*Public m11 As Single[^\r\n]*\s*Public m15 As Single"),
                Is.True, "Matrix must list all 16 floats in raylib's scrambled m0,m4,m8,m12/m1,m5,m9,m13/... declaration order");
            // Texture2D field order (passed by value to SetShaderValueTexture): id then width,height,mipmaps,format.
            Assert.That(Regex.IsMatch(utility,
                @"Structure Texture2D\s*Public id As UInteger[^\r\n]*\s*Public width As Integer[^\r\n]*\s*Public height As Integer[^\r\n]*\s*Public mipmaps As Integer[^\r\n]*\s*Public format As Integer"),
                Is.True, "Texture2D must be {UInteger id, Integer width, height, mipmaps, format} in order");

            // Shader returned BY VALUE.
            Assert.That(wrapper.Contains("Framework_LoadShader(vsFileName As String, fsFileName As String) As Shader"), Is.True, "LoadShader returns Shader by value");
            Assert.That(wrapper.Contains("Framework_LoadShaderFromMemory(vsCode As String, fsCode As String) As Shader"), Is.True, "LoadShaderFromMemory returns Shader by value");
            // IsShaderValid -> I1 Boolean, Shader by value.
            Assert.That(wrapper.Contains("Framework_IsShaderValid(shader As Shader) As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "IsShaderValid: Shader by value + I1 Boolean");
            // GetShaderLocationAttrib -> Integer, Shader by value, String in.
            Assert.That(wrapper.Contains("Framework_GetShaderLocationAttrib(shader As Shader, attribName As String) As Integer"), Is.True, "GetShaderLocationAttrib: Shader by value + Integer");
            // const void* value -> IntPtr (caller marshals the payload); uniformType is a plain Integer enum.
            Assert.That(wrapper.Contains("Framework_SetShaderValue(shader As Shader, locIndex As Integer, value As IntPtr, uniformType As Integer)"), Is.True, "SetShaderValue: void* -> IntPtr");
            Assert.That(wrapper.Contains("Framework_SetShaderValueV(shader As Shader, locIndex As Integer, value As IntPtr, uniformType As Integer, count As Integer)"), Is.True, "SetShaderValueV: void* -> IntPtr + count");
            // Matrix BY VALUE (64 bytes) and Texture2D BY VALUE (20 bytes).
            Assert.That(wrapper.Contains("Framework_SetShaderValueMatrix(shader As Shader, locIndex As Integer, mat As Matrix)"), Is.True, "SetShaderValueMatrix: Matrix by value");
            Assert.That(wrapper.Contains("Framework_SetShaderValueTexture(shader As Shader, locIndex As Integer, texture As Texture2D)"), Is.True, "SetShaderValueTexture: Texture2D by value");

            // CharSet.Ansi on all three string-input bindings. The \( after each name stops LoadShader from matching
            // LoadShaderFromMemory/LoadShaderF.
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_LoadShader\("), Is.True, "LoadShader marshals Ansi");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_LoadShaderFromMemory\("), Is.True, "LoadShaderFromMemory marshals Ansi");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_GetShaderLocationAttrib\("), Is.True, "GetShaderLocationAttrib marshals Ansi");
        });
    }

    [Test]
    public void Core_C4_new_shader_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in C4NewNames)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    // Extracts RLAPI function names from the first line containing firstContains through the line containing lastContains
    // (both inclusive). Only RLAPI lines match; the non-greedy .*? lands the first (name '(' — the function itself.
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
