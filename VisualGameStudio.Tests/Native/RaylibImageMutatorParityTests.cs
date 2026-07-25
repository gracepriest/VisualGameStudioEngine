using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3c-i (Image mutators): every
/// Framework_&lt;name&gt; export in framework.h has a matching &lt;DllImport&gt; of the same name in
/// RaylibWrapper.vb. Pure text scan — no engine load. Trailing '(' anchors near-name pairs
/// (Framework_ImageResize( != Framework_ImageResizeNN( != Framework_ImageResizeCanvas(;
/// Framework_ImageColorContrast( != Framework_ImageColorBrightness().
/// </summary>
[TestFixture]
public class RaylibImageMutatorParityTests
{
    private static readonly string[] Batch3cI =
    {
        "ImageFormat", "ImageToPOT", "ImageCrop", "ImageAlphaCrop", "ImageAlphaClear",
        "ImageAlphaMask", "ImageAlphaPremultiply", "ImageBlurGaussian", "ImageKernelConvolution",
        "ImageResizeNN", "ImageResizeCanvas", "ImageMipmaps", "ImageDither", "ImageFlipHorizontal",
        "ImageRotate", "ImageRotateCW", "ImageRotateCCW", "ImageColorTint", "ImageColorGrayscale",
        "ImageColorContrast", "ImageColorBrightness", "ImageColorReplace",
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
    public void Every_batch3cI_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3cI)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3cI, Has.Length.EqualTo(22));
        });
    }
}
