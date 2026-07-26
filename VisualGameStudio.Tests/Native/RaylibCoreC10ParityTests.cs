using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for rcore Batch core-C10 (raw raylib compression / encoding / hash passthroughs).
/// Pure text scan — no engine load. None of the 7 names collide with existing exports or convenience helpers, so all
/// bind unsuffixed with raylib's exact spelling. The trailing '(' anchor keeps near-name boundaries apart
/// (Framework_ComputeMD5( != Framework_ComputeSHA1(; Framework_CompressData( != Framework_DecompressData().
/// </summary>
[TestFixture]
public class RaylibCoreC10ParityTests
{
    // All 7 raw compression/encoding/hash names bind unsuffixed (import name == export name) — no collision.
    private static readonly string[] CodecNames =
    {
        "CompressData", "DecompressData", "EncodeDataBase64", "DecodeDataBase64",
        "ComputeCRC32", "ComputeMD5", "ComputeSHA1",
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
    public void Every_core_C10_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in CodecNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(CodecNames.Length, Is.EqualTo(7));
        });
    }
}
