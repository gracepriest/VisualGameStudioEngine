using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the rtext font-data sub-batch (LoadFontData, GenImageFontAtlas, UnloadFontData) — the
/// last 3 bindable raylib.h public functions, closing raylib.h to 100%. All headless (text scan) — fast subset. Checks:
///   1. Every_fontdata_export_is_bound_3_ways — genuine 3-way (framework.h + framework.cpp + wrapper) for the 3, PLUS a
///      raylib.h completeness cross-check (LoadFontData..UnloadFontData == exactly these 3, contiguous @1466-1468).
///   2. TextFormat_is_intentionally_left_unbound — the ONLY remaining raylib.h public fn we do NOT bind is TextFormat, which
///      is variadic (`const char *TextFormat(const char *text, ...)`) and cannot be P/Invoke'd; assert it exists in raylib.h
///      but is deliberately absent from framework.h (managed code uses String.Format instead). This makes the exclusion honest.
///   3. Wrapper_fontdata_bindings_declare_the_correct_marshaling — TYPE scan: LoadFontData mirrors LoadFontFromMemory
///      (Byte() fileData + Integer() codepoints) and returns IntPtr (raylib-owned GlyphInfo* array); GenImageFontAtlas takes the
///      IntPtr glyph array + ByRef IntPtr glyphRecs out-param and returns Image by value; UnloadFontData is a Sub(IntPtr, Integer).
///   4. Fontdata_exports_are_each_declared_exactly_once — duplicate-export guard (the "(" delimiter distinguishes
///      Framework_UnloadFontData from the pre-existing Framework_UnloadFont).
/// </summary>
[TestFixture]
public class RaylibTextFontDataParityTests
{
    private static readonly string[] FontDataNames = { "LoadFontData", "GenImageFontAtlas", "UnloadFontData" };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_fontdata_export_is_bound_3_ways()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var forwarder = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.cpp"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in FontDataNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(forwarder.Contains($"Framework_{name}("), Is.True, $"framework.cpp missing forwarder Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }

            var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
            var range = ExtractRlapiRange(raylibHeader, "LoadFontData(", "UnloadFontData(");
            Assert.That(range, Is.EquivalentTo(FontDataNames), "raylib's LoadFontData..UnloadFontData range must be exactly these 3 font-data fns");
        });
    }

    [Test]
    public void TextFormat_is_intentionally_left_unbound()
    {
        var root = RepoRoot();
        var raylibHeader = File.ReadAllText(Path.Combine(root, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h"));
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            // TextFormat is variadic in raylib.h — it CANNOT be marshaled through P/Invoke, so it is deliberately not bound.
            Assert.That(raylibHeader.Contains("RLAPI const char *TextFormat(const char *text, ...)"), Is.True,
                "raylib.h's TextFormat must still be the variadic signature that justifies excluding it");
            Assert.That(header.Contains("Framework_TextFormat("), Is.False,
                "TextFormat is variadic and intentionally NOT bound (managed code uses String.Format)");
        });
    }

    [Test]
    public void Wrapper_fontdata_bindings_declare_the_correct_marshaling()
    {
        var root = RepoRoot();
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            Assert.That(wrapper.Contains("Public Function Framework_LoadFontData(fileData As Byte(), dataSize As Integer, fontSize As Integer, codepoints As Integer(), codepointCount As Integer, type As Integer) As IntPtr"), Is.True,
                "LoadFontData: Byte() fileData + Integer() codepoints -> IntPtr (raylib-owned GlyphInfo* array)");
            Assert.That(wrapper.Contains("Public Function Framework_GenImageFontAtlas(glyphs As IntPtr, ByRef glyphRecs As IntPtr, glyphCount As Integer, fontSize As Integer, padding As Integer, packMethod As Integer) As Image"), Is.True,
                "GenImageFontAtlas: IntPtr glyphs + ByRef IntPtr glyphRecs out-param -> Image by value");
            Assert.That(wrapper.Contains("Public Sub Framework_UnloadFontData(glyphs As IntPtr, glyphCount As Integer)"), Is.True,
                "UnloadFontData: IntPtr glyph array + Integer count");
        });
    }

    [Test]
    public void Fontdata_exports_are_each_declared_exactly_once()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));

        Assert.Multiple(() =>
        {
            foreach (var name in FontDataNames)
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
