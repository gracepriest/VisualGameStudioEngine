using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3a (color/pixel): every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; of the same name in
/// RaylibWrapper.vb. Pure text scan — no engine load. The trailing '(' token boundary keeps the
/// faithful names distinct from the existing underscore Framework_Color_* helpers
/// (Framework_ColorToHSV( != Framework_Color_ToHSV(), Framework_GetColor( != Framework_GetPixelColor().
/// </summary>
[TestFixture]
public class RaylibColorParityTests
{
    private static readonly string[] Batch3a =
    {
        "ColorIsEqual", "Fade", "ColorToInt", "ColorNormalize", "ColorFromNormalized",
        "ColorToHSV", "ColorFromHSV", "ColorTint", "ColorBrightness", "ColorContrast",
        "ColorAlpha", "ColorAlphaBlend", "ColorLerp", "GetColor", "GetPixelColor",
        "SetPixelColor", "GetPixelDataSize",
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
    public void Every_batch3a_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3a)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3a, Has.Length.EqualTo(17));
        });
    }
}
