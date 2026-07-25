using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3c-ii (Image software drawing): every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; in RaylibWrapper.vb.
/// Pure text scan — no engine load. Trailing '(' anchors near-name pairs (Framework_ImageDraw(
/// != Framework_ImageDrawPixel(; Framework_ImageDrawCircle( != Framework_ImageDrawCircleV( !=
/// Framework_ImageDrawCircleLines( != Framework_ImageDrawCircleLinesV().
/// </summary>
[TestFixture]
public class RaylibImageDrawParityTests
{
    private static readonly string[] Batch3cII =
    {
        "ImageClearBackground", "ImageDrawPixel", "ImageDrawPixelV", "ImageDrawLine", "ImageDrawLineV",
        "ImageDrawLineEx", "ImageDrawCircle", "ImageDrawCircleV", "ImageDrawCircleLines", "ImageDrawCircleLinesV",
        "ImageDrawRectangle", "ImageDrawRectangleV", "ImageDrawRectangleRec", "ImageDrawRectangleLines",
        "ImageDrawTriangle", "ImageDrawTriangleEx", "ImageDrawTriangleLines", "ImageDrawTriangleFan",
        "ImageDrawTriangleStrip", "ImageDraw",
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
    public void Every_batch3cII_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3cII)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3cII, Has.Length.EqualTo(20));
        });
    }
}
