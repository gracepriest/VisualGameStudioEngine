using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3b (Image in RAM): every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; of the same name in
/// RaylibWrapper.vb. Pure text scan — no engine load. The trailing '(' token boundary keeps the
/// faithful names distinct (Framework_LoadImage( != Framework_LoadImageRaw(,
/// Framework_ImageFromImage( != Framework_ImageFromChannel().
/// </summary>
[TestFixture]
public class RaylibImageParityTests
{
    private static readonly string[] Batch3b =
    {
        "LoadImageRaw", "LoadImageAnim", "LoadImageAnimFromMemory", "LoadImageFromMemory", "IsImageValid",
        "ExportImage", "ExportImageAsCode", "GenImageColor", "GenImageGradientLinear", "GenImageGradientRadial",
        "GenImageGradientSquare", "GenImageChecked", "GenImageWhiteNoise", "GenImagePerlinNoise", "GenImageCellular",
        "ImageCopy", "ImageFromImage", "ImageFromChannel", "GetImageAlphaBorder", "GetImageColor",
        "LoadImageColors", "LoadImagePalette",
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
    public void Every_batch3b_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3b)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3b, Has.Length.EqualTo(22));
        });
    }
}
