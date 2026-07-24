using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Enforces the engine⇄wrapper sync invariant for raylib text Batch 2: every
/// Framework_&lt;name&gt; export in framework.h has a matching binding of the same name in
/// RaylibWrapper.vb (for string-returning functions the `... As IntPtr` DllImport carries the
/// token; the extra public String wrapper is a convenience overload). Pure text scan — no engine load.
/// </summary>
[TestFixture]
public class RaylibTextParityTests
{
    private static readonly string[] Batch2 =
    {
        // Group 1a — clean, non-string-return (22)
        "GetFontDefault","LoadFont","LoadFontFromImage","LoadFontFromMemory","IsFontValid","ExportFontAsCode",
        "DrawTextPro","DrawTextCodepoint","DrawTextCodepoints","SetTextLineSpacing","GetGlyphIndex","GetGlyphInfo",
        "GetGlyphAtlasRec","GetCodepointCount","GetCodepoint","GetCodepointNext","GetCodepointPrevious",
        "TextIsEqual","TextLength","TextFindIndex","TextToInteger","TextToFloat",
        // Group 1b — clean, string return (7)
        "TextSubtext","TextToUpper","TextToLower","TextToPascal","TextToSnake","TextToCamel","CodepointToUTF8",
        // Faithful MeasureTextEx (renamed) (1)
        "MeasureTextExV",
        // Malloc-return string wrappers (3)
        "TextReplace","TextInsert","LoadUTF8",
        // Array/buffer wrappers (5)
        "LoadCodepoints","TextJoin","TextCopy","TextAppend","TextSplit",
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
    public void Every_batch2_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch2)
            {
                // Trailing '(' is the token boundary: Framework_GetCodepoint( never matches
                // Framework_GetCodepointCount(, nor Framework_LoadFont( vs Framework_LoadFontFromImage(.
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch2, Has.Length.EqualTo(38));
        });
    }
}
