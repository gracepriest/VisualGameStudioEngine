using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C1 (raw raylib window state &amp; control passthroughs). All
/// headless (no engine load, no window) — runs in the fast subset. Three checks:
///   1. Every_core_C1_export_has_a_matching_wrapper_import — name-level 3-way presence + a raylib.h completeness
///      cross-check (the whole window-control range InitWindow..GetWindowHandle is covered by exactly WindowNames ∪
///      AlreadyBound, so a forgotten function can't slip through).
///   2. Wrapper_C1_bindings_declare_the_correct_marshaling_types — TYPE-level scan of RaylibWrapper.vb (Single opacity,
///      UInteger flags, I1 bool returns, CharSet.Ansi title, by-value Image, IntPtr array/handle). This is the only
///      HEADLESS guard on the wrapper's real marshaling: the runtime fixture (RaylibWindowStateControlTests) declares
///      its own local [DllImport] mirrors and self-Ignores when no display exists, so without this scan a wrong VB type
///      (e.g. Single→Integer, a missing I1) would ship green on any headless machine.
///   3. Already_exported_window_utilities_are_not_re_declared_by_core_C1 — duplicate-export guard on the 5 pre-existing
///      window utilities.
/// None of the 24 names collide with existing exports/helpers, so all bind unsuffixed with raylib's exact spelling.
/// </summary>
[TestFixture]
public class RaylibCoreC1ParityTests
{
    // The 24 raw window-control names newly bound by C1 (import name == export name — zero collisions).
    private static readonly string[] WindowNames =
    {
        "InitWindow", "CloseWindow", "WindowShouldClose", "IsWindowReady",
        "IsWindowFullscreen", "IsWindowHidden", "IsWindowMinimized", "IsWindowMaximized",
        "IsWindowFocused", "IsWindowResized", "IsWindowState", "SetWindowState", "ClearWindowState",
        "MaximizeWindow", "MinimizeWindow", "RestoreWindow", "SetWindowIcon", "SetWindowIcons",
        "SetWindowPosition", "SetWindowMonitor", "SetWindowMaxSize", "SetWindowOpacity",
        "SetWindowFocused", "GetWindowHandle",
    };

    // Pre-existing raw window exports — C1 must NOT re-declare these (would be a duplicate export). Together with
    // WindowNames these are EXACTLY raylib's window-control range (InitWindow..GetWindowHandle) — see the cross-check.
    private static readonly string[] AlreadyBound =
    {
        "ToggleFullscreen", "ToggleBorderlessWindowed", "SetWindowSize", "SetWindowMinSize", "SetWindowTitle",
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
    public void Every_core_C1_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in WindowNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            // Completeness cross-check against the authoritative raylib 5.5 header: the window-control range
            // (InitWindow .. GetWindowHandle) must be covered EXACTLY by WindowNames ∪ AlreadyBound. This is not a
            // tautology — it reads the real function list and fails if a raylib window-control fn is neither newly
            // bound (WindowNames) nor already bound (AlreadyBound), i.e. it catches a forgotten 25th function.
            var raylibHeader = Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h");
            var control = ExtractWindowControlNames(File.ReadAllText(raylibHeader)).ToHashSet();
            var covered = WindowNames.Concat(AlreadyBound).ToHashSet();
            Assert.That(control, Is.EquivalentTo(covered),
                "WindowNames ∪ AlreadyBound must exactly cover raylib's window-control range InitWindow..GetWindowHandle");
        });
    }

    [Test]
    public void Wrapper_C1_bindings_declare_the_correct_marshaling_types()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        // TYPE-aware assertions on the actual VB declarations — the headless guard against a wrong wrapper signature.
        Assert.Multiple(() =>
        {
            // float opacity must be Single, not Integer.
            Assert.That(wrapper.Contains("Framework_SetWindowOpacity(opacity As Single)"), Is.True,
                "SetWindowOpacity must take a Single (raylib float), not Integer");
            // unsigned int flags must be UInteger, not Integer.
            Assert.That(wrapper.Contains("Framework_IsWindowState(flag As UInteger)"), Is.True, "IsWindowState flag must be UInteger");
            Assert.That(wrapper.Contains("Framework_SetWindowState(flags As UInteger)"), Is.True, "SetWindowState flags must be UInteger");
            Assert.That(wrapper.Contains("Framework_ClearWindowState(flags As UInteger)"), Is.True, "ClearWindowState flags must be UInteger");
            // bool returns must carry the I1 marshal (a bare Boolean marshals as 4-byte BOOL and misreads raylib's 1-byte bool).
            Assert.That(wrapper.Contains("Framework_WindowShouldClose() As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "WindowShouldClose must return I1 Boolean");
            Assert.That(wrapper.Contains("Framework_IsWindowReady() As <MarshalAs(UnmanagedType.I1)> Boolean"), Is.True, "IsWindowReady must return I1 Boolean");
            // Image struct by value.
            Assert.That(wrapper.Contains("Framework_SetWindowIcon(image As Image)"), Is.True, "SetWindowIcon takes an Image by value");
            // Image* + count via a pinned IntPtr.
            Assert.That(wrapper.Contains("Framework_SetWindowIcons(images As IntPtr, count As Integer)"), Is.True, "SetWindowIcons takes IntPtr + count");
            // Native handle as IntPtr (void*).
            Assert.That(wrapper.Contains("Framework_GetWindowHandle() As IntPtr"), Is.True, "GetWindowHandle returns IntPtr");
            // InitWindow: Ansi title. The String parameter AND a CharSet.Ansi on its own DllImport line.
            Assert.That(wrapper.Contains("Framework_InitWindow(width As Integer, height As Integer, title As String)"), Is.True, "InitWindow title must be a String");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Sub Framework_InitWindow\("), Is.True,
                "InitWindow's DllImport must set CharSet.Ansi so the const char* title marshals as Ansi");
        });
    }

    [Test]
    public void Already_exported_window_utilities_are_not_re_declared_by_core_C1()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        // Each already-bound name must still appear exactly once as an export declaration (C1 didn't duplicate it).
        Assert.Multiple(() =>
        {
            foreach (var name in AlreadyBound)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    // Extracts the raylib window-control function names: from the "// Window-related functions" comment through the
    // GetWindowHandle line (the last of the control subset, right before the Get*Width/Height/Monitor query getters).
    private static List<string> ExtractWindowControlNames(string raylibHeader)
    {
        var lines = raylibHeader.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains("// Window-related functions"));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "raylib.h '// Window-related functions' anchor not found");
        var names = new List<string>();
        var rx = new Regex(@"RLAPI\s+.*?(\w+)\s*\(");
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (!lines[i].Contains("RLAPI")) continue;
            var m = rx.Match(lines[i]);
            if (m.Success) names.Add(m.Groups[1].Value);
            if (lines[i].Contains("GetWindowHandle(")) break; // last function of the window-control subset
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
