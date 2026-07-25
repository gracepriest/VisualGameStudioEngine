using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raylib textures Batch 3d (Texture GPU round-trips + font-image).
/// Pure text scan — no engine load. Trailing '(' anchors near-name pairs (Framework_ImageText( !=
/// Framework_ImageTextEx(; Framework_ImageDrawText( != Framework_ImageDrawTextEx(;
/// Framework_LoadImageFromTexture( != Framework_LoadImageFromScreen().
/// </summary>
[TestFixture]
public class RaylibTexture3dParityTests
{
    private static readonly string[] Batch3d =
    {
        "LoadTextureFromImage", "LoadTextureCubemap", "LoadImageFromTexture", "LoadImageFromScreen",
        "ImageText", "ImageTextEx", "ImageDrawText", "ImageDrawTextEx", "GenImageText",
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
    public void Every_batch3d_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch3d)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch3d, Has.Length.EqualTo(9));
        });
    }
}
