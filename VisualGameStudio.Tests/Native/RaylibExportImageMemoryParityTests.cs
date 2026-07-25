using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for the file-I/O straggler batch: raylib 5.5's
/// <c>ExportImageToMemory</c> (the lone textures fn deferred from Batch 3b) plus the <c>MemFree</c>
/// primitive it requires so callers can release the malloc'd buffer. Pure text scan — no engine load.
/// Trailing '(' anchors the near-name boundary (Framework_MemFree( is a distinct token from raylib's
/// MemAlloc/MemRealloc, which are NOT bound in this batch).
/// </summary>
[TestFixture]
public class RaylibExportImageMemoryParityTests
{
    private static readonly string[] Batch =
    {
        "ExportImageToMemory", "MemFree",
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
    public void Every_fileio_straggler_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in Batch)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(Batch, Has.Length.EqualTo(2));
        });
    }
}
