using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C9 (raw raylib directory listing &amp; dropped files). All headless
/// (no engine load, no window) — runs in the fast subset. Three checks:
///   1. Every_core_C9_export_has_a_matching_wrapper_import — name-level 3-way presence for all 7, PLUS two raylib.h
///      completeness cross-checks: (a) the LoadDirectoryFiles..GetFileModTime sub-range is EXACTLY the 7 C9 names (nothing
///      in the directory/dropped block missed), and (b) the ENTIRE file-I/O surface (LoadFileData..GetFileModTime,
///      spanning C7+C8+C9) is now bound in framework.h — proving C9 completed it.
///   2. Wrapper_C9_bindings_declare_the_correct_marshaling_types — TYPE-level scan: FilePathList by-value returns, I1
///      scanSubdirs/IsFileDropped, GetFileModTime As Integer (raylib's C `long` is 32-bit on Win64 — NOT Long), CharSet.Ansi
///      path inputs, the FilePathList struct fields (UInteger/UInteger/IntPtr in order), and the managed String() helpers
///      (Marshal.ReadIntPtr + PtrToStringAnsi, freeing via the matching Unload).
///   3. Core_C9_exports_are_each_declared_exactly_once — duplicate-export guard on the 7 new names.
/// FilePathList is the module's FIRST by-value struct-with-pointer-array return. Zero wrapper collisions (all 7 plain).
/// </summary>
[TestFixture]
public class RaylibCoreC9ParityTests
{
    // The 7 raw directory/dropped-file names newly bound by C9 (import name == export name — zero collisions).
    private static readonly string[] C9Names =
    {
        "LoadDirectoryFiles", "LoadDirectoryFilesEx", "UnloadDirectoryFiles", "IsFileDropped",
        "LoadDroppedFiles", "UnloadDroppedFiles", "GetFileModTime",
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
    public void Every_core_C9_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in C9Names)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));

            // (a) The raylib directory/dropped sub-range (LoadDirectoryFiles .. GetFileModTime) is EXACTLY the 7 C9 names —
            // catches a forgotten function in that contiguous block (e.g. a missed UnloadDroppedFiles).
            var subRange = ExtractRlapiRange(raylibHeader, "LoadDirectoryFiles(", "GetFileModTime(");
            Assert.That(subRange, Is.EquivalentTo(C9Names),
                "raylib's LoadDirectoryFiles..GetFileModTime range must be exactly the 7 C9 names");

            // (b) The ENTIRE file-I/O surface (LoadFileData .. GetFileModTime = C7 files-management + C8 path queries + C9)
            // is now bound in framework.h — proving C9 completed the file-system section, nothing left unbound.
            var fileIo = ExtractRlapiRange(raylibHeader, "LoadFileData(", "GetFileModTime(");
            foreach (var name in fileIo)
                Assert.That(header.Contains($"Framework_{name}("), Is.True,
                    $"file-I/O function Framework_{name} still unbound after C9 (LoadFileData..GetFileModTime must be complete)");
        });
    }

    [Test]
    public void Wrapper_C9_bindings_declare_the_correct_marshaling_types()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));
        var utility = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "Utiliy.vb"));

        Assert.Multiple(() =>
        {
            // FilePathList struct: UInteger capacity, UInteger count, IntPtr paths — IN THAT ORDER (Sequential layout ABI).
            Assert.That(Regex.IsMatch(utility,
                @"Structure FilePathList\s+Public capacity As UInteger\s+Public count As UInteger\s+Public paths As IntPtr"),
                Is.True, "FilePathList must be {UInteger capacity, UInteger count, IntPtr paths} in order");

            // FilePathList returned BY VALUE.
            Assert.That(wrapper.Contains("Framework_LoadDirectoryFiles(dirPath As String) As FilePathList"), Is.True, "LoadDirectoryFiles returns FilePathList");
            Assert.That(wrapper.Contains("Framework_LoadDroppedFiles() As FilePathList"), Is.True, "LoadDroppedFiles returns FilePathList");
            // FilePathList passed BY VALUE to Unload (Sub).
            Assert.That(wrapper.Contains("Framework_UnloadDirectoryFiles(files As FilePathList)"), Is.True, "UnloadDirectoryFiles takes FilePathList by value");
            Assert.That(wrapper.Contains("Framework_UnloadDroppedFiles(files As FilePathList)"), Is.True, "UnloadDroppedFiles takes FilePathList by value");
            // scanSubdirs is a C bool -> I1; the Ex export also returns FilePathList.
            Assert.That(Regex.IsMatch(wrapper, @"Framework_LoadDirectoryFilesEx\(basePath As String, filter As String, <MarshalAs\(UnmanagedType\.I1\)> scanSubdirs As Boolean\) As FilePathList"),
                Is.True, "LoadDirectoryFilesEx: I1 scanSubdirs + FilePathList return");
            // IsFileDropped -> I1 Boolean.
            Assert.That(wrapper.Contains("Framework_IsFileDropped() As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "IsFileDropped returns I1 Boolean");
            // GetFileModTime: raylib's C `long` is 32-bit on Win64 — MUST be Integer, never Long (would misread 8 bytes).
            Assert.That(wrapper.Contains("Framework_GetFileModTime(fileName As String) As Integer"), Is.True, "GetFileModTime must return Integer (32-bit long)");
            Assert.That(wrapper.Contains("Framework_GetFileModTime(fileName As String) As Long"), Is.False, "GetFileModTime must NOT be Long (raylib long is 32-bit on Win64)");
            // Ansi path inputs.
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_LoadDirectoryFiles\("), Is.True, "LoadDirectoryFiles dirPath marshals Ansi");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_LoadDirectoryFilesEx\("), Is.True, "LoadDirectoryFilesEx basePath/filter marshal Ansi");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Function Framework_GetFileModTime\("), Is.True, "GetFileModTime fileName marshals Ansi");

            // Managed String() helpers walk the char** and copy via PtrToStringAnsi, then free via the matching Unload.
            Assert.That(wrapper.Contains("Public Function LoadDirectoryFiles(dirPath As String) As String()"), Is.True, "LoadDirectoryFiles String() helper present");
            Assert.That(wrapper.Contains("Public Function LoadDirectoryFilesEx(basePath As String, filter As String, scanSubdirs As Boolean) As String()"), Is.True, "LoadDirectoryFilesEx String() helper present");
            Assert.That(wrapper.Contains("Public Function LoadDroppedFiles() As String()"), Is.True, "LoadDroppedFiles String() helper present");
            Assert.That(Regex.IsMatch(wrapper, @"Private Function MarshalFilePathList[\s\S]{0,400}?Marshal\.ReadIntPtr[\s\S]{0,120}?PtrToStringAnsi"), Is.True, "MarshalFilePathList walks char** via ReadIntPtr + PtrToStringAnsi");
            // Pin the char** stride: entries are pointer-sized (IntPtr.Size = 8 on x64), NOT a hardcoded 4. This is the
            // only static guard on the production walk — the runtime oracle re-implements it with a local mirror.
            Assert.That(Regex.IsMatch(wrapper, @"Private Function MarshalFilePathList[\s\S]{0,400}?ReadIntPtr\(list\.paths, i \* IntPtr\.Size\)"), Is.True, "MarshalFilePathList strides by IntPtr.Size");
            Assert.That(Regex.IsMatch(wrapper, @"Public Function LoadDirectoryFiles\(dirPath As String\) As String\(\)[\s\S]{0,300}?Framework_UnloadDirectoryFiles"), Is.True, "LoadDirectoryFiles helper frees via UnloadDirectoryFiles");
            Assert.That(Regex.IsMatch(wrapper, @"Public Function LoadDroppedFiles\(\) As String\(\)[\s\S]{0,300}?Framework_UnloadDroppedFiles"), Is.True, "LoadDroppedFiles helper frees via UnloadDroppedFiles");
        });
    }

    [Test]
    public void Core_C9_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        // No accidental duplicate export (e.g. GetFileModTime declared twice).
        Assert.Multiple(() =>
        {
            foreach (var name in C9Names)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    // Extracts RLAPI function names from the first line containing firstContains through the line containing lastContains
    // (both inclusive). Skips comments/blank lines (only RLAPI lines match).
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
