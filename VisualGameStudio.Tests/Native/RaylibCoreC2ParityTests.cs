using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C2 (raw raylib window/monitor query getters &amp; clipboard). All
/// headless (no engine load, no window) — runs in the fast subset. Three checks:
///   1. Every_core_C2_export_has_a_matching_wrapper_import — name-level 3-way presence + a raylib.h completeness
///      cross-check (the whole window-query range after GetWindowHandle up to the "// Cursor-related functions" comment
///      is covered by exactly QueryNames ∪ AlreadyBound, so a forgotten function can't slip through).
///   2. Wrapper_C2_bindings_declare_the_correct_marshaling_types — TYPE-level scan of RaylibWrapper.vb (Vector2/Image
///      by-value returns, IntPtr + PtrToStringAnsi for const char* returns, CharSet.Ansi clipboard input, Integer
///      getters). This is the only HEADLESS guard on the wrapper's real marshaling: the runtime fixture
///      (RaylibWindowQueryClipboardTests) declares its own local [DllImport] mirrors and self-Ignores when no display
///      exists, so without this scan a wrong VB type (e.g. an LPStr String return, a missing Vector2) would ship green
///      on any headless machine.
///   3. Already_exported_query_getters_are_not_re_declared_by_core_C2 — duplicate-export guard on the 7 pre-existing
///      screen/monitor getters.
/// None of the 13 new names collide with existing exports/helpers, so all bind unsuffixed with raylib's exact spelling.
/// </summary>
[TestFixture]
public class RaylibCoreC2ParityTests
{
    // The 13 raw window-query / clipboard names newly bound by C2 (import name == export name — zero collisions).
    private static readonly string[] QueryNames =
    {
        "GetRenderWidth", "GetRenderHeight", "GetMonitorPosition", "GetMonitorPhysicalWidth",
        "GetMonitorPhysicalHeight", "GetWindowPosition", "GetWindowScaleDPI", "GetMonitorName",
        "SetClipboardText", "GetClipboardText", "GetClipboardImage", "EnableEventWaiting", "DisableEventWaiting",
    };

    // Pre-existing raw window-query exports (managed monitor section) — C2 must NOT re-declare these. Together with
    // QueryNames these are EXACTLY raylib's window-query range (GetScreenWidth..DisableEventWaiting) — see the cross-check.
    private static readonly string[] AlreadyBound =
    {
        "GetScreenWidth", "GetScreenHeight", "GetMonitorWidth", "GetMonitorHeight",
        "GetMonitorCount", "GetMonitorRefreshRate", "GetCurrentMonitor",
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
    public void Every_core_C2_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in QueryNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            // Completeness cross-check against the authoritative raylib 5.5 header: the window-query range (everything
            // after GetWindowHandle up to "// Cursor-related functions") must be covered EXACTLY by QueryNames ∪
            // AlreadyBound. Not a tautology — it reads the real function list and fails if a raylib window-query fn is
            // neither newly bound (QueryNames) nor already bound (AlreadyBound), i.e. it catches a forgotten function.
            var raylibHeader = Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h");
            var query = ExtractWindowQueryNames(File.ReadAllText(raylibHeader)).ToHashSet();
            var covered = QueryNames.Concat(AlreadyBound).ToHashSet();
            Assert.That(query, Is.EquivalentTo(covered),
                "QueryNames ∪ AlreadyBound must exactly cover raylib's window-query range GetScreenWidth..DisableEventWaiting");
        });
    }

    [Test]
    public void Wrapper_C2_bindings_declare_the_correct_marshaling_types()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        // TYPE-aware assertions on the actual VB declarations — the headless guard against a wrong wrapper signature.
        Assert.Multiple(() =>
        {
            // Vector2 returns by value (a wrong scalar/ByRef type would corrupt the by-value struct ABI).
            Assert.That(wrapper.Contains("Framework_GetMonitorPosition(monitor As Integer) As Vector2"), Is.True, "GetMonitorPosition returns Vector2");
            Assert.That(wrapper.Contains("Framework_GetWindowPosition() As Vector2"), Is.True, "GetWindowPosition returns Vector2");
            Assert.That(wrapper.Contains("Framework_GetWindowScaleDPI() As Vector2"), Is.True, "GetWindowScaleDPI returns Vector2");
            // Image by value.
            Assert.That(wrapper.Contains("Framework_GetClipboardImage() As Image"), Is.True, "GetClipboardImage returns Image by value");
            // const char* returns MUST come back as IntPtr (never an LPStr String return — that frees raylib-owned memory).
            Assert.That(wrapper.Contains("Framework_GetMonitorName(monitor As Integer) As IntPtr"), Is.True, "GetMonitorName raw export returns IntPtr");
            Assert.That(wrapper.Contains("Framework_GetClipboardText() As IntPtr"), Is.True, "GetClipboardText raw export returns IntPtr");
            Assert.That(Regex.IsMatch(wrapper, @"Framework_GetMonitorName\([^)]*\)\s+As\s+<MarshalAs\(UnmanagedType\.LPStr\)>"), Is.False, "GetMonitorName must NOT return an LPStr String");
            Assert.That(Regex.IsMatch(wrapper, @"Framework_GetClipboardText\(\)\s+As\s+<MarshalAs\(UnmanagedType\.LPStr\)>"), Is.False, "GetClipboardText must NOT return an LPStr String");
            // String-returning managed helpers copy via PtrToStringAnsi.
            Assert.That(wrapper.Contains("Public Function GetMonitorName(monitor As Integer) As String"), Is.True, "GetMonitorName String helper present");
            Assert.That(wrapper.Contains("Public Function GetClipboardText() As String"), Is.True, "GetClipboardText String helper present");
            Assert.That(Regex.IsMatch(wrapper, @"Public Function GetMonitorName\(monitor As Integer\) As String[\s\S]{0,220}?PtrToStringAnsi"), Is.True, "GetMonitorName helper uses PtrToStringAnsi");
            Assert.That(Regex.IsMatch(wrapper, @"Public Function GetClipboardText\(\) As String[\s\S]{0,220}?PtrToStringAnsi"), Is.True, "GetClipboardText helper uses PtrToStringAnsi");
            // Clipboard input marshals Ansi: the String parameter AND a CharSet.Ansi on its own DllImport line.
            Assert.That(wrapper.Contains("Framework_SetClipboardText(text As String)"), Is.True, "SetClipboardText takes a String");
            Assert.That(Regex.IsMatch(wrapper, @"CharSet:=CharSet\.Ansi[^\r\n]*\)>\s*[\r\n]+\s*Public Sub Framework_SetClipboardText\("), Is.True,
                "SetClipboardText's DllImport must set CharSet.Ansi so the const char* text marshals as Ansi");
            // Plain int getters.
            Assert.That(wrapper.Contains("Framework_GetRenderWidth() As Integer"), Is.True, "GetRenderWidth returns Integer");
            Assert.That(wrapper.Contains("Framework_GetRenderHeight() As Integer"), Is.True, "GetRenderHeight returns Integer");
            Assert.That(wrapper.Contains("Framework_GetMonitorPhysicalWidth(monitor As Integer) As Integer"), Is.True, "GetMonitorPhysicalWidth returns Integer");
            Assert.That(wrapper.Contains("Framework_GetMonitorPhysicalHeight(monitor As Integer) As Integer"), Is.True, "GetMonitorPhysicalHeight returns Integer");
            // void/void event-waiting toggles must be parameterless Subs (a stray return type or parameter would misbind).
            Assert.That(wrapper.Contains("Public Sub Framework_EnableEventWaiting()"), Is.True, "EnableEventWaiting is a parameterless Sub");
            Assert.That(wrapper.Contains("Public Sub Framework_DisableEventWaiting()"), Is.True, "DisableEventWaiting is a parameterless Sub");
        });
    }

    [Test]
    public void Already_exported_query_getters_are_not_re_declared_by_core_C2()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        // Each already-bound name must still appear exactly once as an export declaration (C2 didn't duplicate it).
        // Note: the "(" suffix keeps Framework_GetMonitorWidth( from matching Framework_GetMonitorPhysicalWidth(.
        Assert.Multiple(() =>
        {
            foreach (var name in AlreadyBound)
            {
                int count = CountOccurrences(header, $"Framework_{name}(");
                Assert.That(count, Is.EqualTo(1), $"Framework_{name} should be declared exactly once (found {count})");
            }
        });
    }

    // Extracts the raylib window-query function names: everything after the GetWindowHandle line (last of the
    // window-control subset) up to the "// Cursor-related functions" comment (start of the next subset).
    private static List<string> ExtractWindowQueryNames(string raylibHeader)
    {
        var lines = raylibHeader.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains("GetWindowHandle("));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "raylib.h 'GetWindowHandle' anchor not found");
        var names = new List<string>();
        var rx = new Regex(@"RLAPI\s+.*?(\w+)\s*\(");
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].Contains("// Cursor-related functions")) break; // end of the window-query/clipboard subset
            if (!lines[i].Contains("RLAPI")) continue;
            var m = rx.Match(lines[i]);
            if (m.Success) names.Add(m.Groups[1].Value);
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
